using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using BricsCadRc.Core;
using BricsCadRc.Dialogs;

namespace BricsCadRc.Commands
{
    /// <summary>
    /// Belki / pogrubienia (docs/PLAN_BELKI_2026-10-10.md, E1 / E3; sekcja 4i ARCHITECTURE.md):
    ///   • RC_BEAM — wskazanie strefy (zamknięta polilinia; opcja Krawedz: obrys + 2 punkty krawędzi → strefa 600 od linii
    ///     uskoku), okno parametrów (BeamDialog), zapis na strefach obu rzutów, pręty belki (opcja w oknie),
    ///   • RC_GENERATE_BEAM (RC_GENERATE → Beam) — pręty wszystkich belek płyty: obrys rzutu dolnego, górnego (Enter = automatycznie).
    /// Pręty belki wyłącznie na jawne żądanie — siatka płyty (Mesh) ich nie tworzy. Jedna komenda = jedna grupa UNDO.
    /// </summary>
    public class BeamCommands
    {
        private const string T = "[RC BEAM]";

        [CommandMethod("RC_BEAM", CommandFlags.Modal)]
        public void Beam()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;
            var msgs = new List<string>();
            try
            {
                // decyzja 2026-10-11 (punkt D): warstwa RC-BEAM-MANUAL do ręcznego rysowania belek
                using (doc.LockDocument())
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    BeamStore.EnsureManualLayer(tr, db);
                    tr.Commit();
                }
                var peo = new PromptEntityOptions($"\nWskaż strefę belki (zamkniętą polilinię, np. na warstwie {BeamStore.ManualLayer}) albo [Krawedz]: ");
                peo.SetRejectMessage("\nTo nie jest polilinia.");
                peo.AddAllowedClass(typeof(Polyline), true);
                peo.Keywords.Add("Krawedz");
                var r = ed.GetEntity(peo);
                ObjectId zoneId;
                if (r.Status == PromptStatus.Keyword)
                {
                    zoneId = ZoneFromEdge(doc, msgs);
                    Flush(ed, msgs);
                    if (zoneId.IsNull) return;
                }
                else if (r.Status == PromptStatus.OK) zoneId = r.ObjectId;
                else return;

                AutoRebarEngine.BeamZoneContext ctx;
                using (doc.LockDocument()) ctx = AutoRebarEngine.PrepareBeamZone(doc, zoneId, msgs);
                Flush(ed, msgs);
                if (ctx == null) return;

                bool generate = true;
                if (ScriptMode.IsNonInteractive())
                {
                    ed.WriteMessage($"\n{T} Tryb bez okien — parametry z rekordu strefy / domyślne.");
                }
                else
                {
                    var dlg = new BeamDialog(ctx.Params, ctx.HeightSource, ctx.Info, ctx.NibGeometry, ctx.NibGap);
                    if (Application.ShowModalWindow(dlg) != true) { ed.WriteMessage($"\n{T} Anulowano (strefa zostaje).\n"); return; }
                    ctx.Params = dlg.Result;
                    generate = dlg.Generate;
                }
                using (doc.LockDocument()) AutoRebarEngine.SaveBeamParams(db, ctx);
                ed.WriteMessage($"\n{T} Parametry zapisane: H = {ctx.Params.Height:F0}, {ctx.Params.BottomCount} H{ctx.Params.BottomDia} + " +
                                $"{ctx.Params.TopCount} H{ctx.Params.TopDia}, strzemiona H{ctx.Params.StirrupDia} co {ctx.Params.StirrupSpacing:F0} " +
                                $"({BeamParams.LegsText(ctx.Params.StirrupLegs)}), U-bary H{ctx.Params.UBarDia}" +
                                (ctx.NibGeometry && ctx.Params.NoNib ? ", BEZ nibu (lico zewnętrzne = krawędź płyty)" : "") + ".");
                if (generate)
                {
                    if (ctx.ZoneBottom.IsNull)
                        ed.WriteMessage($"\n{T} Strefa tylko na rzucie górnym — brak rzutu dolnego; pręty nie zostały wygenerowane.");
                    else
                        AutoRebarEngine.GenerateBeams(doc, ctx.SlabBottom, ctx.SlabTop, ctx.ZoneBottom);
                }
                ed.WriteMessage("\n");
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_BEAM", ex);
                ed.WriteMessage($"\n*** ERROR *** {T} {ex.Message}\n");
            }
        }

        /// <summary>RC_GENERATE → Beam: pręty wszystkich belek płyty (rzut dolny + górny).</summary>
        [CommandMethod("RC_GENERATE_BEAM", CommandFlags.Modal)]
        public void GenerateBeam()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            try
            {
                var bottom = PickOutline(ed, "\nWskaż obrys płyty na rzucie DOLNYM (strzemiona, pręty dolne belek): ", allowNone: false);
                if (bottom.IsNull) return;
                // Wskazano rzut górny (RC_GA = T albo same rozkłady T1/T2) — strzemiona i pręty dolne trafiłyby na rzut górny.
                // Tak samo w trybie bez okien (ScriptMode) — przerwanie z komunikatem.
                if (AutoRebarEngine.IsTopView(doc.Database, NibDetector.OuterOf(doc.Database, bottom)))
                {
                    ed.WriteMessage($"\n{T} Wskazany obrys to rzut GÓRNY — wskaż obrys rzutu DOLNEGO (strzemiona, pręty dolne belek). Przerwano.\n");
                    return;
                }
                var top = PickOutline(ed, "\nWskaż obrys płyty na rzucie GÓRNYM <Enter = automatycznie>: ", allowNone: true);
                if (top == ObjectId.Null && _lastPickCancelled) return;
                if (top.IsNull)
                {
                    var (partner, _) = AutoRebarEngine.FindPartnerView(doc.Database, NibDetector.OuterOf(doc.Database, bottom));
                    top = partner;
                    ed.WriteMessage(top.IsNull ? $"\n{T} Nie znaleziono rzutu górnego (przystający obrys)."
                                               : $"\n{T} Rzut górny znaleziony automatycznie.");
                }
                // grupa płyt (uwaga Bartka 2026-10-26, PLOT 357-362): belki wszystkich płyt grupy jednym poleceniem (góra — FindPartnerView)
                var others = GroupPick.Others(ed, doc.Database, bottom, T);
                if (others == null) return;
                AutoRebarEngine.GenerateBeams(doc, bottom, top);
                foreach (var g in others)
                {
                    ed.WriteMessage($"\n{T} ── {g.member} ──");
                    if (g.top.IsNull) ed.WriteMessage($"\n{T} {g.member}: nie znaleziono rzutu górnego (przystający obrys).");
                    AutoRebarEngine.GenerateBeams(doc, g.bottom, g.top);
                }
                ed.WriteMessage("\n");
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_GENERATE_BEAM", ex);
                ed.WriteMessage($"\n*** ERROR *** {T} {ex.Message}\n");
            }
        }

        private static bool _lastPickCancelled;

        private static ObjectId PickOutline(Editor ed, string prompt, bool allowNone)
        {
            _lastPickCancelled = false;
            var peo = new PromptEntityOptions(prompt) { AllowNone = allowNone };
            peo.SetRejectMessage("\nTo nie jest polilinia.");
            peo.AddAllowedClass(typeof(Polyline), true);
            var r = ed.GetEntity(peo);
            if (r.Status == PromptStatus.OK) return GaImportEngine.ResolveSlabPick(ed.Document.Database, r.ObjectId, r.PickedPoint);
            if (r.Status != PromptStatus.None) _lastPickCancelled = true;
            return ObjectId.Null;
        }

        private static ObjectId ZoneFromEdge(Document doc, List<string> msgs)
        {
            var ed = doc.Editor;
            var slab = PickOutline(ed, "\nWskaż obrys płyty: ", allowNone: false);
            if (slab.IsNull) return ObjectId.Null;
            var r1 = ed.GetPoint("\nPierwszy punkt na krawędzi (początek belki): ");
            if (r1.Status != PromptStatus.OK) return ObjectId.Null;
            var o2 = new PromptPointOptions("\nDrugi punkt na krawędzi (koniec belki): ") { UseBasePoint = true, BasePoint = r1.Value };
            var r2 = ed.GetPoint(o2);
            if (r2.Status != PromptStatus.OK) return ObjectId.Null;
            using (doc.LockDocument())
                return AutoRebarEngine.CreateZoneFromEdge(doc.Database, slab, new Point2d(r1.Value.X, r1.Value.Y),
                                                          new Point2d(r2.Value.X, r2.Value.Y), new BeamParams().Width, msgs);
        }

        private static void Flush(Editor ed, List<string> msgs)
        {
            foreach (var m in msgs) ed.WriteMessage($"\n{T} {m}");
            msgs.Clear();
        }
    }
}
