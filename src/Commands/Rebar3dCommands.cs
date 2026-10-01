using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using BricsCadRc.Core;

namespace BricsCadRc.Commands
{
    /// <summary>
    /// RC_SIATKA_3D — poglądowy model 3D zbrojenia płyty, budowany WPROST z rozkładów 2D
    /// (rysunek 2D jest źródłem; model jest odtwarzany przy każdym wywołaniu).
    ///
    /// • obrys dołu (B1, B2, U-bary) + obrys góry (T1, T2) — góra przesunięta na płytę dolną,
    /// • wszystkie pręty rozkładu (także ukryte przy wygaszaniu), z cięciem przy otworach,
    /// • poziomy osi: B1 = c + d/2, B2 nad B1, T1 = h − c − d/2, T2 pod T1,
    /// • pręty kolejnych odcinków w pasie przesunięte o średnicę (widać zakłady),
    /// • U-bary jako bryły kształtu 21 z gięciami, beton z otworami (półprzezroczysty), pale,
    /// • jeden rozkład 2D = jeden blok 3D (nazwa bloku = opis pozycji), model obok płyty.
    /// </summary>
    public class Rebar3dCommands
    {
        private const string SlabLayer = "SD-PILED-RAFT";
        private const string App3d     = "RC_3D";
        private const double ModelGapRight = 60000.0;   // od prawej krawędzi siatki górnej (albo dolnej)
        private const double PileDepth     = 1000.0;    // pokazany fragment pala pod płytą

        private sealed class Dist3d
        {
            public ObjectId Id;
            public BarData  Bar;
            public Point3d  Pos;
            public Vector3d Shift;       // góra → położenie płyty dolnej
            public double   LapOffset;   // przesunięcie poprzeczne (zakład)
            public string   Code;        // warstwa w modelu (B1/B2/T1/T2) — dla prętów B+T z detalu osobno dół i góra
            public bool     Detail;      // rozkład z rysunku detalu (przeniesiony na plan)
        }

        /// <summary>Detal wskazany przez użytkownika: obszar rysunku detalu i przesunięcie na plan.</summary>
        private sealed class Detail3d
        {
            public int      Nr;
            public Extents3d Frame;
            public Vector3d ToPlan;      // punkt planu = punkt detalu + ToPlan
        }

        [CommandMethod("RC_SIATKA_3D", CommandFlags.Modal)]
        public void Generate3d()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;

            var thickOpts = new PromptKeywordOptions("\nGrubość płyty") { AllowNone = true };
            thickOpts.Keywords.Add("225");
            thickOpts.Keywords.Add("300");
            thickOpts.Keywords.Default = "300";
            var thickRes = ed.GetKeywords(thickOpts);
            if (thickRes.Status == PromptStatus.Cancel) return;
            double h = 300;
            if (thickRes.Status == PromptStatus.OK && double.TryParse(thickRes.StringResult, out double th)) h = th;

            var bottomId = PickOutline(ed, "\n[RC 3D] Wskaż obrys siatki DOLNEJ (B1, B2, U-bary): ", false);
            if (bottomId.IsNull) return;
            var topId = PickOutline(ed, "\n[RC 3D] Wskaż obrys siatki GÓRNEJ (T1, T2) <Enter = bez góry>: ", true);
            var detailSel = PickDetails(ed);

            var sw = Stopwatch.StartNew();
            try
            {
                using (doc.LockDocument())
                {
                // Otwory i pale PRZED główną transakcją — te funkcje używają własnej transakcji
                // open/close, która zamykała obiekty (model space) otwarte do zapisu w głównej
                // → eNotOpenForWrite.
                var holes = AutoRebarEngine.FindHoleBoxes(db, bottomId);
                var piles = AutoRebarEngine.FindPilesFor(db, bottomId);
                var details = ResolveDetails(db, ed, detailSel, holes);
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var bottomPl = (Polyline)tr.GetObject(bottomId, OpenMode.ForRead);
                    var bVerts = GeometryHelper.GetPolylineVertices(bottomPl);
                    var bBox = GeometryHelper.PolylineBbox(bottomPl);

                    var dists = ReadDistributions(tr, db, bVerts, new Vector3d(), bottomOnly: true, details);
                    double rightEdge = bBox.MaxPoint.X;   // model: ModelGapRight od prawej krawędzi siatki górnej
                    if (!topId.IsNull && topId != bottomId)
                    {
                        var topPl = (Polyline)tr.GetObject(topId, OpenMode.ForRead);
                        var tBox = GeometryHelper.PolylineBbox(topPl);
                        rightEdge = tBox.MaxPoint.X;
                        var shift = new Vector3d(bBox.MinPoint.X - tBox.MinPoint.X, bBox.MinPoint.Y - tBox.MinPoint.Y, 0);
                        dists.AddRange(ReadDistributions(tr, db, GeometryHelper.GetPolylineVertices(topPl), shift, bottomOnly: false, details));
                    }
                    int planCount = dists.Count;
                    dists.AddRange(ReadDetailDistributions(tr, db, details, dists));
                    if (dists.Count == 0)
                    {
                        ed.WriteMessage("\n[RC 3D] Brak rozkładów na wskazanych obrysach — najpierw RC_GENERUJ_SIATKA.\n");
                        return;
                    }

                    // Średnice i poziomy osi warstw
                    int D(string code, int def) => dists.Where(d => !d.Detail && d.Bar.LayerCode == code && IsStraight(d.Bar))
                                                        .Select(d => d.Bar.Diameter).DefaultIfEmpty(def).First();
                    double cover = AutoRebarEngine.DefaultCover;
                    int dB1 = D("B1", 10), dB2 = D("B2", 10), dT1 = D("T1", 12), dT2 = D("T2", 12);
                    var z = new Dictionary<string, double>
                    {
                        ["B1"] = cover + dB1 / 2.0,
                        ["B2"] = cover + dB1 + dB2 / 2.0,
                        ["T1"] = h - cover - dT1 / 2.0,
                        ["T2"] = h - cover - dT1 - dT2 / 2.0,
                    };

                    AssignLapOffsets(dists);

                    var place = new Vector3d(rightEdge + ModelGapRight - bBox.MinPoint.X, 0, 0);
                    EnsureLayers(tr, db);
                    EnsureApp(tr, db);
                    string prefix = "RC_3D_" + bottomId.Handle.Value.ToString("X") + "_";
                    ClearPrevious(tr, db, prefix);
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);

                    int groups = 0, solids = 0, bars = 0;
                    void Group(string name, string layer, string handle2d, IEnumerable<Entity> ents)
                    {
                        var list = ents.Where(e => e != null).ToList();
                        if (list.Count == 0) return;
                        var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                        var btr = new BlockTableRecord { Name = prefix + Sanitize(name) + "_" + groups };
                        bt.Add(btr);
                        tr.AddNewlyCreatedDBObject(btr, true);
                        foreach (var e in list) { btr.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); solids++; }
                        var br = new BlockReference(Point3d.Origin + place, btr.ObjectId) { Layer = layer };
                        ms.AppendEntity(br);
                        tr.AddNewlyCreatedDBObject(br, true);
                        br.XData = new ResultBuffer(
                            new TypedValue((int)DxfCode.ExtendedDataRegAppName, App3d),
                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, name ?? ""),
                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, handle2d ?? ""));
                        groups++;
                    }

                    // Beton z otworami
                    Group("BETON", "RC-3D-BETON", "", new[] { SlabSolid(bVerts, holes, h) });

                    // Pale (fragment pod płytą)
                    if (piles.Count > 0)
                        Group("PALE", "RC-3D-PALE", "", piles.Select(p =>
                            (Entity)Cylinder(new Point3d(p.c.X, p.c.Y, -PileDepth), new Point3d(p.c.X, p.c.Y, 0), p.r, "RC-3D-PALE")));

                    // Pręty proste: wszystkie pręty każdego rozkładu
                    foreach (var d in dists.Where(d => IsStraight(d.Bar)))
                    {
                        var bar = d.Bar;
                        if (!z.TryGetValue(d.Code ?? "", out double zz)) continue;
                        string layer = "RC-3D-" + d.Code;
                        double r = bar.Diameter / 2.0;
                        var cuts = BarBlockEngine.ParseCutZones(bar.CutZones);
                        bool barsX = bar.Direction == "X";
                        var ents = new List<Entity>();
                        for (int i = 0; i < Math.Max(1, bar.Count); i++)
                        {
                            double s = i * bar.Spacing;
                            double frac = bar.Count > 1 ? (double)i / (bar.Count - 1) : 0.0;
                            double skew = bar.SkewStart + frac * (bar.SkewEnd - bar.SkewStart);
                            foreach (var (a0, a1) in BarBlockEngine.BarPiecesAfterCuts(cuts, s, skew, bar.LengthA + skew))
                            {
                                double sc = s + d.LapOffset;
                                var p0 = barsX ? new Point3d(d.Pos.X + a0, d.Pos.Y + sc, zz) : new Point3d(d.Pos.X + sc, d.Pos.Y + a0, zz);
                                var p1 = barsX ? new Point3d(d.Pos.X + a1, d.Pos.Y + sc, zz) : new Point3d(d.Pos.X + sc, d.Pos.Y + a1, zz);
                                ents.Add(Cylinder(p0 + d.Shift, p1 + d.Shift, r, layer));
                            }
                            bars++;
                        }
                        Group((d.Detail ? "D_" : "") + bar.Mark + (d.Code != bar.LayerCode ? "_" + d.Code : ""),
                              layer, d.Id.Handle.Value.ToString("X"), ents);
                    }

                    // U-bary
                    foreach (var d in dists.Where(d => !IsStraight(d.Bar)))
                    {
                        var bar = d.Bar;
                        bool barsX = bar.Direction == "X";
                        string support = barsX ? "B1" : "B2";
                        int dSup = barsX ? dB1 : dB2;
                        double du = bar.Diameter;
                        double B = UbHeight(tr, db, bar);
                        double zb = z[support] + dSup / 2.0 + du / 2.0;
                        double zt = zb + B - du;
                        bool bendLow = (bar.SymbolSide ?? "Left") == "Left";   // gięcie przy krawędzi płyty
                        var inward = barsX ? (bendLow ? Vector3d.XAxis : -Vector3d.XAxis)
                                           : (bendLow ? Vector3d.YAxis : -Vector3d.YAxis);
                        double legAlong = bendLow ? 0.0 : bar.LengthA;
                        var ents = new List<Entity>();
                        for (int i = 0; i < Math.Max(1, bar.Count); i++)
                        {
                            double s = i * bar.Spacing;
                            var leg = barsX ? new Point3d(d.Pos.X + legAlong, d.Pos.Y + s, 0)
                                            : new Point3d(d.Pos.X + s, d.Pos.Y + legAlong, 0);
                            ents.Add(UBarSolid(leg + d.Shift, inward, bar.LengthA, zb, zt, du, 2.0 * du));
                            bars++;
                        }
                        Group((d.Detail ? "D_" : "") + bar.Mark, "RC-3D-UB", d.Id.Handle.Value.ToString("X"), ents);
                    }

                    tr.Commit();
                    sw.Stop();
                    if (details.Count > 0)
                        ed.WriteMessage($"\n[RC 3D] Detale: {string.Join(", ", details.Select(x => "'" + x.Nr + "'"))} — " +
                                        $"{dists.Count - planCount} rozkładów przeniesionych na plan.");
                    ed.WriteMessage($"\n[RC 3D] Model: {bars} prętów w {groups} grupach (1 grupa = 1 rozkład 2D), " +
                                    $"{solids} brył, płyta {h:F0} mm, {sw.ElapsedMilliseconds} ms. " +
                                    $"Model {ModelGapRight:F0} mm na prawo od płyty. Widok: _-VIEW _SWISO, _SHADEMODE _R.\n");
                }
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_SIATKA_3D", ex);
                ed.WriteMessage($"\n[RC 3D] Model 3D przerwany: {ex.Message}\n");
            }
        }

        // ----------------------------------------------------------------

        private static bool IsStraight(BarData b) => (b.ShapeCode ?? "00") == "00";

        private static ObjectId PickOutline(Editor ed, string prompt, bool optional)
        {
            while (true)
            {
                var o = new PromptEntityOptions(prompt) { AllowNone = optional };
                o.SetRejectMessage("\nWskaż polilinię obrysu płyty.");
                o.AddAllowedClass(typeof(Polyline), false);
                var r = ed.GetEntity(o);
                if (r.Status != PromptStatus.OK) return ObjectId.Null;
                return r.ObjectId;
            }
        }

        /// <summary>Rozkłady RC wewnątrz obrysu (dół: B1/B2 i U-bary; góra: T1/T2).</summary>
        private static List<Dist3d> ReadDistributions(Transaction tr, Database db, List<Point2d> verts,
                                                      Vector3d shift, bool bottomOnly, List<Detail3d> details)
        {
            var list = new List<Dist3d>();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                var bar = BarBlockEngine.ReadXData(br);
                if (bar == null || Math.Abs(br.Rotation) > 1e-6) continue;
                string code = bar.LayerCode ?? "";
                bool isBottom = code == "B1" || code == "B2";
                bool isTop    = code == "T1" || code == "T2";
                if (bottomOnly ? !isBottom : !isTop) continue;
                if (!GeometryHelper.IsPointInsidePolygon(verts, new Point2d(br.Position.X, br.Position.Y))) continue;
                if (details.Any(dt => Inside(dt.Frame, br.Position))) continue;   // rysunek detalu na płycie
                list.Add(new Dist3d { Id = id, Bar = bar, Pos = br.Position, Shift = shift, Code = bar.LayerCode });
            }
            return list;
        }


        private static bool Inside(Extents3d e, Point3d p)
            => p.X >= e.MinPoint.X - 1 && p.X <= e.MaxPoint.X + 1 && p.Y >= e.MinPoint.Y - 1 && p.Y <= e.MaxPoint.Y + 1;

        /// <summary>Wybór ramek detali (Enter = bez detali). Wystarczy kliknąć ramkę albo zaznaczyć cały detal oknem.</summary>
        private static List<ObjectId> PickDetails(Editor ed)
        {
            var opts = new PromptSelectionOptions
            {
                MessageForAdding = "\n[RC 3D] Zaznacz detale (ramki DETAIL, można oknem) <Enter = bez detali>: ",
                AllowDuplicates = false,
            };
            var filter = new SelectionFilter(new[] { new TypedValue((int)DxfCode.Start, "LWPOLYLINE") });
            var res = ed.GetSelection(opts, filter);
            return res.Status == PromptStatus.OK ? res.Value.GetObjectIds().ToList() : new List<ObjectId>();
        }

        /// <summary>
        /// Ramki detali → przesunięcie detal → plan. Można wskazać ramkę rysunku detalu ALBO obramówkę
        /// otworu na planie (wtedy rysunek detalu jest szukany po otworze o tych samych wymiarach).
        /// Ramka rysunku z XData RC_DETAIL ma przesunięcie zapisane; starsze — dopasowanie po wymiarach otworu.
        /// Wywoływać POZA główną transakcją (TryGetHole używa własnej transakcji).
        /// </summary>
        private sealed class FrameInfo
        {
            public ObjectId Id; public Extents3d Ext; public bool Tagged; public int Nr; public Vector3d ToPlan;
            public (double x0, double y0, double x1, double y1)? Hole;   // otwór narysowany w ramce
            public bool IsPlan;                                          // obramówka otworu płyty
        }

        private static List<Detail3d> ResolveDetails(Database db, Editor ed, List<ObjectId> sel,
                                                     List<(double minX, double minY, double maxX, double maxY)> planHoles)
        {
            var result = new List<Detail3d>();
            if (sel.Count == 0) return result;

            // 1. Wszystkie ramki (kolor 10 albo z RC_DETAIL) i kandydaci na otwory w ramkach
            var frames = new List<FrameInfo>();
            var cand = new List<(ObjectId id, Extents3d ext)>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl)
                        || !GeometryHelper.IsEffectivelyClosed(pl)) continue;
                    if (SingleBarEngine.ReadBarXData(pl) != null) continue;
                    var ext = GeometryHelper.PolylineBbox(pl);
                    if (AutoRebarEngine.TryReadDetailFrame(pl, out int nr, out double dx, out double dy))
                        frames.Add(new FrameInfo { Id = id, Ext = ext, Tagged = true, Nr = nr, ToPlan = new Vector3d(-dx, -dy, 0) });
                    else if (pl.ColorIndex == 10)
                        frames.Add(new FrameInfo { Id = id, Ext = ext });
                    else if (pl.NumberOfVertices <= 5)
                        cand.Add((id, ext));
                }
                tr.Commit();
            }
            foreach (var f in frames)
            {
                // największy otwór w ramce (w ramce rysunku detalu jest jeden)
                foreach (var c in cand.Where(c => Inside(f.Ext, c.ext.MinPoint) && Inside(f.Ext, c.ext.MaxPoint))
                                      .OrderByDescending(c => (c.ext.MaxPoint - c.ext.MinPoint).Length))
                {
                    if (!AutoRebarEngine.TryGetHole(db, c.id, out double x0, out double y0, out double x1, out double y1, out _)) continue;
                    f.Hole = (x0, y0, x1, y1);
                    f.IsPlan = planHoles.Any(p => Math.Abs(p.minX - x0) < 1 && Math.Abs(p.minY - y0) < 1
                                               && Math.Abs(p.maxX - x1) < 1 && Math.Abs(p.maxY - y1) < 1);
                    break;
                }
            }
            bool SameSize((double x0, double y0, double x1, double y1) a, double bx0, double by0, double bx1, double by1)
                => Math.Abs((a.x1 - a.x0) - (bx1 - bx0)) < 5 && Math.Abs((a.y1 - a.y0) - (by1 - by0)) < 5;
            var drawings = frames.Where(f => f.Hole != null && !f.IsPlan).ToList();

            void AddDetail(FrameInfo f, Vector3d toPlan)
            {
                if (result.Any(r => r.Frame.MinPoint.DistanceTo(f.Ext.MinPoint) < 1)) return;
                result.Add(new Detail3d { Nr = f.Nr, Frame = f.Ext, ToPlan = toPlan });
            }

            int skipped = 0;
            foreach (var id in sel)
            {
                var f = frames.FirstOrDefault(x => x.Id == id);
                if (f == null || f.Hole == null) continue;
                var h = f.Hole.Value;
                if (f.IsPlan)
                {
                    // Obramówka na planie → rysunek detalu tego otworu
                    var hit = drawings.Where(d => d.Tagged && Math.Abs(d.Hole.Value.x0 + d.ToPlan.X - h.x0) < 1
                                                           && Math.Abs(d.Hole.Value.y0 + d.ToPlan.Y - h.y0) < 1).ToList();
                    if (hit.Count == 0)
                        hit = drawings.Where(d => !d.Tagged && SameSize(d.Hole.Value, h.x0, h.y0, h.x1, h.y1)).ToList();
                    if (hit.Count == 1)
                        AddDetail(hit[0], hit[0].Tagged ? hit[0].ToPlan
                                                        : new Vector3d(h.x0 - hit[0].Hole.Value.x0, h.y0 - hit[0].Hole.Value.y0, 0));
                    else
                    {
                        skipped++;
                        ed.WriteMessage($"\n[RC 3D] Otwór {h.x1 - h.x0:F0}×{h.y1 - h.y0:F0}: " +
                                        (hit.Count == 0 ? "nie znaleziono rysunku detalu." : "kilka pasujących detali — wskaż ramkę rysunku detalu."));
                    }
                }
                else if (f.Tagged) AddDetail(f, f.ToPlan);
                else
                {
                    // Ramka rysunku detalu bez znacznika → otwór płyty o tych samych wymiarach
                    var match = planHoles.Where(p => SameSize(h, p.minX, p.minY, p.maxX, p.maxY)).ToList();
                    if (match.Count == 1) AddDetail(f, new Vector3d(match[0].minX - h.x0, match[0].minY - h.y0, 0));
                    else
                    {
                        skipped++;
                        ed.WriteMessage($"\n[RC 3D] Detal {h.x1 - h.x0:F0}×{h.y1 - h.y0:F0}: " +
                                        (match.Count == 0 ? "brak takiego otworu na płycie." : "kilka takich otworów — kliknij obramówkę otworu na planie.") +
                                        " (Albo wygeneruj detal ponownie RC_DETAL_OTWORU.)");
                    }
                }
            }
            if (result.Count == 0 && skipped == 0)
                ed.WriteMessage("\n[RC 3D] W zaznaczeniu nie ma ramki detalu (obramówka kolor 10 z otworem w środku).");
            return result;
        }

        /// <summary>
        /// Rozkłady z rysunków detali, przeniesione na plan. Pręty „B+T ADD” (opis 2 dołem + 2 górą) → dwie kopie:
        /// w warstwie dolnej i górnej o tym samym kierunku co siatka. U-bary jak na krawędziach płyty.
        /// </summary>
        private static List<Dist3d> ReadDetailDistributions(Transaction tr, Database db, List<Detail3d> details, List<Dist3d> plan)
        {
            var list = new List<Dist3d>();
            if (details.Count == 0) return list;
            string CodeFor(string dir, bool top)
            {
                var hit = plan.FirstOrDefault(d => IsStraight(d.Bar) && d.Bar.Direction == dir
                                                   && (d.Bar.LayerCode ?? "").StartsWith(top ? "T" : "B"));
                if (hit != null) return hit.Bar.LayerCode;
                return top ? (dir == "X" ? "T1" : "T2") : (dir == "X" ? "B1" : "B2");
            }
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                var bar = BarBlockEngine.ReadXData(br);
                if (bar == null || Math.Abs(br.Rotation) > 1e-6) continue;
                var dt = details.FirstOrDefault(x => Inside(x.Frame, br.Position));
                if (dt == null) continue;
                Dist3d Make(string code) => new Dist3d { Id = id, Bar = bar, Pos = br.Position, Shift = dt.ToPlan, Code = code, Detail = true };
                if (!IsStraight(bar)) { list.Add(Make(bar.LayerCode)); continue; }
                bool bt = (bar.Mark ?? "").IndexOf("B+T", StringComparison.OrdinalIgnoreCase) >= 0;
                if (bt)
                {
                    list.Add(Make(CodeFor(bar.Direction, false)));
                    list.Add(Make(CodeFor(bar.Direction, true)));
                }
                else list.Add(Make(bar.LayerCode));
            }
            return list;
        }

        /// <summary>
        /// Zakłady: rozkłady tej samej warstwy w tym samym pasie (ta sama współrzędna pierwszego pręta),
        /// kolejne wzdłuż prętów — co drugi przesunięty o średnicę, żeby pręty się nie nakładały.
        /// </summary>
        private static void AssignLapOffsets(List<Dist3d> dists)
        {
            foreach (var g in dists.Where(d => IsStraight(d.Bar) && !d.Detail)
                                   .GroupBy(d => (d.Bar.LayerCode, d.Bar.Direction,
                                                  Math.Round((d.Bar.Direction == "X" ? d.Pos.Y : d.Pos.X) / 10.0))))
            {
                int k = 0;
                foreach (var d in g.OrderBy(d => d.Bar.Direction == "X" ? d.Pos.X : d.Pos.Y))
                    d.LapOffset = (k++ % 2) * d.Bar.Diameter;
            }
        }

        /// <summary>Wysokość U-bara (wymiar B) z pręta-szablonu rozkładu; domyślnie 140.</summary>
        private static double UbHeight(Transaction tr, Database db, BarData bar)
        {
            try
            {
                if (XLink.TryParse(bar.SourceBarHandle, out long hv) && hv != 0
                    && db.TryGetObjectId(new Handle(hv), out var sid) && !sid.IsErased
                    && tr.GetObject(sid, OpenMode.ForRead) is Polyline pl)
                {
                    var tpl = SingleBarEngine.ReadBarXData(pl);
                    if (tpl != null && tpl.LengthB > 0) return tpl.LengthB;
                }
            }
            catch (System.Exception ex) { Log.Error("RC_SIATKA_3D.UbHeight", ex); }
            return 140.0;
        }

        private static string Sanitize(string s)
            => new string((s ?? "").Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());

        private static Solid3d Cylinder(Point3d a, Point3d b, double r, string layer)
        {
            var dir = b - a;
            double len = dir.Length;
            var s = new Solid3d();
            s.CreateFrustum(Math.Max(len, 1e-3), r, r, r);
            var zAxis = len > 1e-9 ? dir.GetNormal() : Vector3d.ZAxis;
            var xAxis = zAxis.GetPerpendicularVector();
            var yAxis = zAxis.CrossProduct(xAxis);
            s.TransformBy(Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                a + dir / 2.0, xAxis, yAxis, zAxis));
            s.Layer = layer;
            s.ColorIndex = 256;
            return s;
        }

        /// <summary>
        /// U-bar (kształt 21) jako bryła przeciągnięta po osi: ramię dolne (zb), gięcie,
        /// przewiązka przy krawędzi (leg), gięcie, ramię górne (zt). Ramiona idą w stronę inward.
        /// </summary>
        private static Solid3d UBarSolid(Point3d leg, Vector3d inward, double armAxisLen,
                                         double zb, double zt, double d, double rInner)
        {
            double rc = rInner + d / 2.0;
            if (zt - zb < d + 1e-6) return null;
            bool semi = zt - zb <= 2 * rc + 1e-6;          // niska płyta (kształt 13): jedno gięcie 180°
            if (semi) rc = (zt - zb) / 2.0;
            if (armAxisLen < rc + 1e-6) return null;
            const double q = -0.41421356237309503;   // tan(22.5°) — łuk 90°
            using (var path = new Polyline())
            {
                if (semi)
                {
                    path.AddVertexAt(0, new Point2d(armAxisLen, zb), 0, 0, 0);
                    path.AddVertexAt(1, new Point2d(rc, zb), -1.0, 0, 0);   // półokrąg
                    path.AddVertexAt(2, new Point2d(rc, zt), 0, 0, 0);
                    path.AddVertexAt(3, new Point2d(armAxisLen, zt), 0, 0, 0);
                }
                else
                {
                    path.AddVertexAt(0, new Point2d(armAxisLen, zb), 0, 0, 0);
                    path.AddVertexAt(1, new Point2d(rc, zb), q, 0, 0);
                    path.AddVertexAt(2, new Point2d(0, zb + rc), 0, 0, 0);
                    path.AddVertexAt(3, new Point2d(0, zt - rc), q, 0, 0);
                    path.AddVertexAt(4, new Point2d(rc, zt), 0, 0, 0);
                    path.AddVertexAt(5, new Point2d(armAxisLen, zt), 0, 0, 0);
                }
                var yLocal = Vector3d.ZAxis;
                var zLocal = inward.CrossProduct(yLocal);
                path.TransformBy(Matrix3d.AlignCoordinateSystem(
                    Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                    leg, inward, yLocal, zLocal));
                var start = path.StartPoint;
                var dir = path.GetFirstDerivative(path.StartParam);
                using (var profile = new Circle(start, dir.GetNormal(), d / 2.0))
                {
                    var opts = new SweepOptionsBuilder { Align = SweepOptionsAlignOption.NoAlignment, BasePoint = start, Bank = false };
                    var s = new Solid3d();
                    try
                    {
                        s.CreateSweptSolid(profile, path, opts.ToSweepOptions());
                        s.Layer = "RC-3D-UB";
                        s.ColorIndex = 256;
                        return s;
                    }
                    catch (System.Exception ex) { Log.Error("RC_SIATKA_3D.UBarSolid", ex); s.Dispose(); return null; }
                }
            }
        }

        private static Solid3d SlabSolid(List<Point2d> outline, List<(double minX, double minY, double maxX, double maxY)> holes, double h)
        {
            Polyline Ring(IList<Point2d> pts)
            {
                var pl = new Polyline();
                for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, pts[i], 0, 0, 0);
                pl.Closed = true;
                return pl;
            }
            try
            {
                var region = (Region)Region.CreateFromCurves(new DBObjectCollection { Ring(outline) })[0];
                foreach (var hb in holes)
                {
                    var ring = Ring(new[] { new Point2d(hb.minX, hb.minY), new Point2d(hb.maxX, hb.minY),
                                            new Point2d(hb.maxX, hb.maxY), new Point2d(hb.minX, hb.maxY) });
                    var hr = (Region)Region.CreateFromCurves(new DBObjectCollection { ring })[0];
                    region.BooleanOperation(BooleanOperationType.BoolSubtract, hr);
                }
                var s = new Solid3d();
                s.Extrude(region, h, 0);
                s.Layer = "RC-3D-BETON";
                s.ColorIndex = 256;
                return s;
            }
            catch (System.Exception ex) { Log.Error("RC_SIATKA_3D.SlabSolid", ex); return null; }
        }

        /// <summary>Usuwa poprzedni model tej płyty (wstawienia, zawartość i definicje bloków z prefiksem).</summary>
        private static void ClearPrevious(Transaction tr, Database db, string prefix)
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var old = new List<ObjectId>();
            foreach (ObjectId id in bt)
            {
                var rec = (BlockTableRecord)tr.GetObject(id, OpenMode.ForRead);
                if (rec.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) old.Add(id);
            }
            foreach (var id in old)
            {
                var rec = (BlockTableRecord)tr.GetObject(id, OpenMode.ForWrite);
                foreach (ObjectId rid in rec.GetBlockReferenceIds(true, false))
                    ((Entity)tr.GetObject(rid, OpenMode.ForWrite)).Erase();
                foreach (ObjectId eid in rec)
                    ((Entity)tr.GetObject(eid, OpenMode.ForWrite)).Erase();
                rec.Erase();
            }
        }

        private static void EnsureApp(Transaction tr, Database db)
        {
            var apps = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (apps.Has(App3d)) return;
            apps.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = App3d };
            apps.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        private static void EnsureLayers(Transaction tr, Database db)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            void Add(string name, short color, byte transparencyPct = 0)
            {
                if (lt.Has(name)) return;
                if (!lt.IsWriteEnabled) lt.UpgradeOpen();
                var rec = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, color) };
                if (transparencyPct > 0)
                    rec.Transparency = new Transparency((byte)(255 * (100 - transparencyPct) / 100));
                lt.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }
            Add("RC-3D-B1", 5);  Add("RC-3D-B2", 150);
            Add("RC-3D-T1", 1);  Add("RC-3D-T2", 30);
            Add("RC-3D-UB", 3);  Add("RC-3D-BETON", 8, 75);
            Add("RC-3D-PALE", 9, 50);
        }
    }
}
