using System;
using System.Linq;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using BricsCadRc.Core;
using BricsCadRc.Dialogs;

namespace BricsCadRc.Commands
{
    public static class SectionCommands
    {
        /// <summary>
        /// RC_SECTION — przekrój płyty z rzeczywistego cięcia: obrys płyty, linia cięcia (pozioma / pionowa),
        /// strona patrzenia, okno z parametrami (grubość / SSL z opisu płyty, nib, otuliny), punkt wstawienia.
        /// Pręty z rozkładów rzutu dolnego i górnego tej płyty, znaczniki przekroju na rzutach.
        /// </summary>
        [CommandMethod("RC_SECTION", CommandFlags.Modal)]
        public static void Section()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;
            const string T = "[RC SECTION]";

            try
            {
                var peo = new PromptEntityOptions("\nWskaż obrys płyty (rzut dolny lub górny): ");
                peo.SetRejectMessage("\nTo nie jest polilinia.");
                peo.AddAllowedClass(typeof(Polyline), true);
                var per = ed.GetEntity(peo);
                if (per.Status != PromptStatus.OK) return;

                var pp1 = ed.GetPoint(new PromptPointOptions("\nPierwszy punkt linii przekroju: "));
                if (pp1.Status != PromptStatus.OK) return;
                var o2 = new PromptPointOptions("\nDrugi punkt linii przekroju (poziomo albo pionowo): ")
                    { UseBasePoint = true, BasePoint = pp1.Value };
                var pp2 = ed.GetPoint(o2);
                if (pp2.Status != PromptStatus.OK) return;
                var p1 = new Point2d(pp1.Value.X, pp1.Value.Y);
                var p2 = new Point2d(pp2.Value.X, pp2.Value.Y);
                if (p1.GetDistanceTo(p2) < 100) { ed.WriteMessage($"\n{T} Linia przekroju za krótka.\n"); return; }

                var o3 = new PromptPointOptions("\nWskaż stronę patrzenia: ") { UseBasePoint = true, BasePoint = pp1.Value };
                var pp3 = ed.GetPoint(o3);
                if (pp3.Status != PromptStatus.OK) return;

                var ctx = AutoRebarEngine.PrepareSection(db, per.ObjectId, p1, p2, new Point2d(pp3.Value.X, pp3.Value.Y));
                if (ctx == null) { ed.WriteMessage($"\n{T} Obrys płyty musi być zamkniętą polilinią.\n"); return; }
                foreach (var s in ctx.Info) ed.WriteMessage($"\n{T} {s}");

                var dlg = new SectionDialog(ctx, AutoRebarEngine.NextSectionLetter(db));
                if (Application.ShowModalWindow(dlg) != true) { ed.WriteMessage($"\n{T} Anulowano.\n"); return; }

                var plan = SectionPlanner.Plan(ctx.Input);
                foreach (var w in plan.Warnings) ed.WriteMessage($"\n{T} {w}");
                if (plan.Segments.Count == 0) { ed.WriteMessage("\n"); return; }

                var pi = ed.GetPoint(new PromptPointOptions("\nPunkt wstawienia przekroju (lewy górny narożnik płyty): "));
                if (pi.Status != PromptStatus.OK) return;

                int n;
                using (doc.LockDocument())
                    n = AutoRebarEngine.DrawSection(db, ctx, plan, pi.Value, dlg.Letter);

                int top = plan.Dots.Count(d => d.Top), bot = plan.Dots.Count - top;
                ed.WriteMessage($"\n{T} Przekrój {dlg.Letter}-{dlg.Letter}: długość {plan.UMax - plan.UMin:F0} mm, " +
                                $"pręty w przekroju: {bot} dołem / {top} górą, w widoku: {plan.Bars.Count}, pale: {plan.Piles.Count}; " +
                                $"znaczniki na {ctx.Views.Count} rzucie(ach) ({n} obiektów).\n");
            }
            catch (System.Exception ex)
            {
                Log.Error("SectionCommands.Section", ex);
                ed.WriteMessage($"\n{T} Błąd: {ex.Message}\n");
            }
        }
    }
}
