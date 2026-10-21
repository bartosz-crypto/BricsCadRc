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
                using (doc.LockDocument())
                    rep = CantileverEngine.ProcessClosedOutlines(db, outlines, pieces, texts, "rysunek", Ask);
                foreach (var m in rep.Info) ed.WriteMessage($"\n{m}");
                foreach (var m in rep.Messages) ed.WriteMessage($"\n{m}");
                if (rep.Made == 0) { ed.WriteMessage($"\n{T} Nie znaleziono wsporników do wyłączenia (obrysy bez języczków albo już wyłączone).\n"); return; }
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
