using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using BricsCadRc.Core;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.Commands
{
    /// <summary>
    /// Plan W4e (P8, opcjonalnie) — RC_REBUILD_BARS: przebudowa obrysów WSZYSTKICH prętów rysunku (przestrzeń modelu)
    /// wg aktualnych reguł geometrii (BS 8666:2020, wymiary zewnętrzne). Jedna komenda = jedna grupa UNDO.
    /// Te same ścieżki co MOVE (Plan B watchera) / RC_UPDATE_BAR: polilinia — SingleBarEngine.RebuildCompanions (rama z obrysu,
    /// axis[0] zostaje, pierścień — środek zostaje), pręt-blok — SingleBarBlock.Rebuild (copy-on-write, punkt wstawienia = axis[0]).
    /// Pomijane: obrys już zgodny z geometrią bieżącą, obrys nierozpoznany (żaden wariant geometrii — np. ręcznie zmieniony;
    /// lista w linii poleceń), pręt testowy H12-T. Dane (XData A–E, TL), numery, rozkłady i opisy — bez zmian; znacznik [15]
    /// geometrii zapisują ścieżki przebudowy (jak MOVE). Grot etykiety (back-link) przesuwany na najbliższy punkt nowego obrysu.
    /// </summary>
    public class RebuildBarsCommands
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [CommandMethod("RC_REBUILD_BARS", CommandFlags.Modal)]
        public void RebuildBars()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            var polys = new List<ObjectId>();
            var blocks = new List<ObjectId>();
            var unfit = new List<string>();
            int current = 0;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (var be in BarEntity.ScanModelSpace(tr, db, BarKinds.Polyline | BarKinds.Block))
                {
                    try
                    {
                        var bar = be.Read();
                        if (bar == null || bar.Mark == TestBarBlockEngine.TestMark) continue;
                        var state = OutlineState(tr, be, bar);
                        if (state == 0) { current++; continue; }
                        if (state < 0) { unfit.Add(bar.Mark ?? "?"); continue; }
                        (be.Kind == BarKind.Block ? blocks : polys).Add(be.Entity.ObjectId);
                    }
                    catch (System.Exception ex) { Log.Error("RC_REBUILD_BARS.Scan", ex); }
                }
                tr.Commit();
            }

            int done = 0;
            foreach (var id in polys) BarGeometryWatcher.IgnoreThisCommand(id);
            using (BarGeometryWatcher.Suppress())
            {
                foreach (var id in polys)
                {
                    try
                    {
                        BarData bar;
                        using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                        {
                            bar = SingleBarEngine.ReadBarXData((Entity)tr.GetObject(id, OpenMode.ForRead));
                            tr.Commit();
                        }
                        SingleBarEngine.RebuildCompanions(db, id, bar);   // rama i axis[0] z bieżącego obrysu (jak Plan B po MOVE)
                        FixLabelTip(db, id, bar);
                        done++;
                    }
                    catch (System.Exception ex) { Log.Error("RC_REBUILD_BARS.Polyline", ex); }
                }
            }
            foreach (var id in blocks)
            {
                try
                {
                    BarData bar = null;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var be = BarEntity.Open(tr, id, OpenMode.ForWrite, BarKinds.Block);
                        bar = be?.Read();
                        if (bar != null && be.Entity is BlockReference br) SingleBarBlock.Rebuild(tr, br, bar);
                        tr.Commit();
                    }
                    if (bar != null) { FixLabelTip(db, id, bar); done++; }
                }
                catch (System.Exception ex) { Log.Error("RC_REBUILD_BARS.Block", ex); }
            }

            ed.WriteMessage($"\n[RC_REBUILD_BARS] Przebudowano {done} pręt(ów) wg BS 8666:2020 (wymiary zewnętrzne); "
                          + $"aktualnych: {current}; z nierozpoznanym obrysem (pominięte): {unfit.Count}.\n");
            if (unfit.Count > 0)
                ed.WriteMessage("[RC_REBUILD_BARS] Pominięte (obrys zmieniony ręcznie? — użyj RC_EDIT_BAR): "
                              + string.Join(", ", unfit.Distinct().Take(30)) + (unfit.Count > 30 ? " …" : "") + "\n");
            if (done > 0)
            {
                // Regen w tej samej komendzie (nie SendStringToExecute) — U / UNDO cofa RC_REBUILD_BARS w całości
                try { ed.Regen(); }
                catch (System.Exception ex) { Log.Error("RC_REBUILD_BARS.Regen", ex); }
            }
        }

        /// <summary>
        /// Stan obrysu: 0 — zgodny z geometrią bieżącą (bez przebudowy), 1 — rozpoznany wariant historyczny (do przebudowy),
        /// −1 — nierozpoznany (pominięty). Pierścień: promień zewnętrzny = (A − d)/2 + d/2.
        /// </summary>
        private static int OutlineState(Transaction tr, BarEntity be, BarData bar)
        {
            var shape = ShapeCodeLibrary.Get(bar.ShapeCode) ?? ShapeCodeLibrary.Get("00");
            var world = be.OutlineVertices(tr).Select(p => (p.X, p.Y)).ToList();
            if (BarFrameRules.IsRingShape(shape.Code))
            {
                if (!BarFrameRules.TryFitRing(world, out var c, out _)) return -1;
                double r = Math.Sqrt((world[0].X - c.X) * (world[0].X - c.X) + (world[0].Y - c.Y) * (world[0].Y - c.Y));
                double a = bar.ParamValues.Length > 0 ? bar.ParamValues[0] : bar.Diameter;
                double outer = BarDimensionRules.RingAxisRadius(a, bar.Diameter) + bar.Diameter / 2.0;
                return Math.Abs(r - outer) <= BarFrameRules.FitTol ? 0 : 1;
            }
            if (!BarFrameRules.TryFitVariants(world, shape.Code, bar.ParamValues, bar.Diameter, out _, out var matched)) return -1;
            return matched.SameAs(BarGeometryOptions.Current) ? 0 : 1;
        }

        /// <summary>Grot etykiety pręta (tylko z back-linkiem) → najbliższy punkt przebudowanego obrysu (jak RC_EDIT_BAR).</summary>
        private static void FixLabelTip(Database db, ObjectId barId, BarData bar)
        {
            if (string.IsNullOrEmpty(bar.LabelHandle)) return;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                try
                {
                    var lblId = SingleBarEngine.HandleToObjectId(db, bar.LabelHandle);
                    if (!lblId.IsNull && !lblId.IsErased && tr.GetObject(lblId, OpenMode.ForRead) is MLeader ml
                        && XLink.Same(SingleBarEngine.ReadBarHandleFromLabel(ml), barId.Handle.Value.ToString("X8")))
                    {
                        var be = BarEntity.From(tr.GetObject(barId, OpenMode.ForRead) as Entity, BarKinds.Polyline | BarKinds.Block);
                        var leaders = ml.GetLeaderIndexes();
                        if (be != null && leaders != null && leaders.Count > 0)
                        {
                            var lines = ml.GetLeaderLineIndexes((int)leaders[0]);
                            if (lines != null && lines.Count > 0)
                            {
                                int lni = (int)lines[0];
                                var tip = ml.GetFirstVertex(lni);
                                Point3d? nt = be.ClosestPoint(tr, tip);
                                if (nt.HasValue && nt.Value.DistanceTo(tip) > 1e-9)
                                {
                                    ml.UpgradeOpen();
                                    ml.SetFirstVertex(lni, nt.Value);
                                }
                            }
                        }
                    }
                }
                catch (System.Exception ex) { Log.Error("RC_REBUILD_BARS.FixLabelTip", ex); }
                tr.Commit();
            }
        }
    }
}
