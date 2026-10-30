using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using BricsCadRc.Core;

namespace BricsCadRc.Commands
{
    public static class CantileverCommands
    {
        private const string T = "[RC CANT]";

        /// <summary>
        /// RC_CANTILEVER / RC_WSPORNIK (decyzja Bartka 2026-10-21, etap 1) — wsporniki w istniejącym rysunku RC (RC_PREPARE_GA robi to
        /// samo przy wstawianiu płyty): języczki zamkniętych obrysów SD-PILED-RAFT (szer. 300–1500, wysięg 300–3000) z opisem „GB-…”
        /// albo strefą przy palu → wyłączone z obrysu siatki (prostokąt RC-CANTILEVER + rekord RC_CANT); bez opisu i strefy — pytanie.
        /// Potem Generate (Mesh / UB / …) liczy płytę z krawędzią prosto przez nasadę wspornika. Zbrojenie wspornika — etap 2.
        /// </summary>
        [CommandMethod("RC_CANTILEVER", CommandFlags.Modal)]
        public static void Cantilever() => Run();

        [CommandMethod("RC_WSPORNIK", CommandFlags.Modal)]
        public static void CantileverPl() => Run();

        private static void Run()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;
            try
            {
                var filter = new SelectionFilter(new[]
                {
                    new TypedValue((int)DxfCode.Start, "LWPOLYLINE"),
                    new TypedValue((int)DxfCode.LayerName, CantileverEngine.SlabLayer)
                });
                var po = new PromptSelectionOptions { MessageForAdding = "\nWskaż obrysy płyty (SD-PILED-RAFT; Enter — wszystkie): " };
                var sel = ed.GetSelection(po, filter);
                List<ObjectId> outlines;
                if (sel.Status == PromptStatus.OK) outlines = sel.Value.GetObjectIds().ToList();
                else if (sel.Status == PromptStatus.Error || sel.Status == PromptStatus.None) outlines = AllClosedSlab(db);
                else return;
                if (outlines.Count == 0) { ed.WriteMessage($"\n{T} Brak zamkniętych obrysów {CantileverEngine.SlabLayer}.\n"); return; }

                var upg = new List<string>();
                using (doc.LockDocument()) CantileverEngine.UpgradeOldStubs(db, upg);
                foreach (var m in upg) ed.WriteMessage($"\n{m}");
                var texts = CantileverEngine.ModelTexts(db);
                var pieces = CantileverEngine.OpenSlabPieces(db);
                bool Ask(string q)
                {
                    var ko = new PromptKeywordOptions($"\n{q}. Wyłączyć go z obrysu siatki jako wspornik? [Tak/Nie] <Nie>: ", "Tak Nie") { AllowNone = true };
                    ko.Keywords.Default = "Nie";
                    var kr = ed.GetKeywords(ko);
                    return kr.Status == PromptStatus.OK && kr.StringResult == "Tak";
                }
                CantileverEngine.Report rep;
                var restored = new List<string>();
                using (doc.LockDocument())
                {
                    // pogrubienie w narożniku (2026-10-30): strefa z linii przerywanych — kawałki z liniami (LINE)
                    var cornerPieces = CantileverEngine.OpenSlabPieces(db, includeLines: true);
                    var corner = CantileverEngine.ProcessCornerTabs(db, outlines, cornerPieces, null, texts, "rysunek");
                    pieces.RemoveAll(id => corner.Moved.Contains(id));
                    rep = CantileverEngine.ProcessClosedOutlines(db, outlines, pieces, texts, "rysunek", Ask, usedTexts: corner.UsedTexts);
                    rep.Made += corner.Made; rep.Info.AddRange(corner.Info); rep.Messages.AddRange(corner.Messages);
                    // obrys na rysunku jak w GA (języczek) — także rysunki przygotowane poprzednią wersją (obrys przecięty)
                    CantileverEngine.RestoreOutlineTabs(db, outlines, restored);
                }
                foreach (var m in rep.Info) ed.WriteMessage($"\n{m}");
                foreach (var m in rep.Messages) ed.WriteMessage($"\n{m}");
                foreach (var m in restored) ed.WriteMessage($"\n{m}");
                if (rep.Made == 0) { ed.WriteMessage($"\n{T} Nie znaleziono nowych wsporników (obrysy bez języczków albo już wyłączone).\n"); return; }
                foreach (var m in BeamZonesInCantilevers(db)) ed.WriteMessage($"\n{T} uwaga: {m}");
                ed.WriteMessage($"\n{T} Wsporniki: {rep.Made} — wyłączone z obrysu siatki. Wygeneruj ponownie siatkę płyty (RC_GENERATE → Mesh); " +
                                "zbrojenie wspornika — kolejny etap.\n");
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_CANTILEVER", ex);
                ed.WriteMessage($"\n{T} Błąd: {ex.Message}\n");
            }
        }

        /// <summary>
        /// RC_GENERATE_PILECAP (RC_GENERATE → PileCap; uwaga Bartka 2026-10-30, APARTMENT 2): oczepy pali (RC-PILECAP z Prepare GA) przy
        /// wskazanej płycie (rzut dolny) — detal 1:25 z przekrojami, zbrojenie standardowe (AutoRebarEngine.GeneratePileCaps).
        /// </summary>
        [CommandMethod("RC_GENERATE_PILECAP", CommandFlags.Modal)]
        public static void GeneratePileCap()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            try
            {
                var peo = new PromptEntityOptions("\nWskaż obrys płyty na rzucie DOLNYM (pile cap przy płycie): ");
                peo.SetRejectMessage("\nTo nie jest polilinia.");
                peo.AddAllowedClass(typeof(Polyline), true);
                var r = ed.GetEntity(peo);
                if (r.Status != PromptStatus.OK) return;
                var picked = GaImportEngine.ResolveSlabPick(doc.Database, r.ObjectId, r.PickedPoint);
                var outer = NibDetector.OuterOf(doc.Database, picked);
                if (AutoRebarEngine.IsTopView(doc.Database, outer))
                {
                    ed.WriteMessage($"\n{T} Wskazany obrys to rzut GÓRNY — wskaż obrys rzutu DOLNEGO. Przerwano.\n");
                    return;
                }
                AutoRebarEngine.GeneratePileCaps(doc, picked);
                ed.WriteMessage("\n");
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_GENERATE_PILECAP", ex);
                ed.WriteMessage($"\n*** ERROR *** {T} {ex.Message}\n");
            }
        }

        /// <summary>
        /// RC_GENERATE_CANTILEVER (RC_GENERATE → Cantilever; decyzje Bartka 2026-10-21, etap 2): pręty wsporników wskazanej płyty
        /// (rzut dolny) w detalu 1:25 — U-bary H16, strzemiona 4-leg, pręty proste (AutoRebarEngine.GenerateCantilevers).
        /// </summary>
        [CommandMethod("RC_GENERATE_CANTILEVER", CommandFlags.Modal)]
        public static void GenerateCantilever()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            try
            {
                var peo = new PromptEntityOptions("\nWskaż obrys płyty na rzucie DOLNYM (pręty wsporników): ");
                peo.SetRejectMessage("\nTo nie jest polilinia.");
                peo.AddAllowedClass(typeof(Polyline), true);
                var r = ed.GetEntity(peo);
                if (r.Status != PromptStatus.OK) return;
                var picked = GaImportEngine.ResolveSlabPick(doc.Database, r.ObjectId, r.PickedPoint);   // obrys GA szeregu (2026-10-28)
                var outer = NibDetector.OuterOf(doc.Database, picked);
                if (AutoRebarEngine.IsTopView(doc.Database, outer))
                {
                    ed.WriteMessage($"\n{T} Wskazany obrys to rzut GÓRNY — wskaż obrys rzutu DOLNEGO. Przerwano.\n");
                    return;
                }
                // grupa płyt (uwaga Bartka 2026-10-26, PLOT 357-362): wsporniki wszystkich płyt grupy jednym poleceniem
                var others = GroupPick.Others(ed, doc.Database, picked, T);
                if (others == null) return;
                AutoRebarEngine.GenerateCantilevers(doc, picked);
                foreach (var g in others)
                {
                    ed.WriteMessage($"\n{T} ── {g.member} ──");
                    AutoRebarEngine.GenerateCantilevers(doc, g.bottom);
                }
                ed.WriteMessage("\n");
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_GENERATE_CANTILEVER", ex);
                ed.WriteMessage($"\n*** ERROR *** {T} {ex.Message}\n");
            }
        }

        private static List<ObjectId> AllClosedSlab(Database db)
        {
            var res = new List<ObjectId>();
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
                if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Polyline pl
                    && string.Equals(pl.Layer, CantileverEngine.SlabLayer, StringComparison.OrdinalIgnoreCase) && GeometryHelper.IsEffectivelyClosed(pl))
                    res.Add(id);
            tr.Commit();
            return res;
        }

        /// <summary>Strefy belek RC-BEAM (np. z opisu „375THK” strefy przy palu, K8) leżące na wsporniku albo w jego strefie przy palu.</summary>
        private static List<string> BeamZonesInCantilevers(Database db)
        {
            var res = new List<string>();
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            var boxes = new List<Extents3d>();
            var zones = new List<(string h, Point2d c)>();
            foreach (ObjectId id in ms)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl)) continue;
                if (string.Equals(pl.Layer, CantileverEngine.LayerName, StringComparison.OrdinalIgnoreCase))
                    try { boxes.Add(pl.GeometricExtents); } catch (System.Exception ex) { Log.Error("RC_CANTILEVER.Box", ex); }
                else if (BeamStore.IsZone(pl))
                    zones.Add((pl.Handle.ToString(), BeamStore.Centroid(pl)));
            }
            tr.Commit();
            foreach (var (h, c) in zones)
                if (boxes.Any(b => c.X >= b.MinPoint.X - 1 && c.X <= b.MaxPoint.X + 1 && c.Y >= b.MinPoint.Y - 1 && c.Y <= b.MaxPoint.Y + 1))
                    res.Add($"strefa belki RC-BEAM {h} leży na wsporniku / w strefie przy palu — usuń ją przed RC_GENERATE → Beam (wspornik zbroi kolejny etap).");
            return res;
        }
    }
}
