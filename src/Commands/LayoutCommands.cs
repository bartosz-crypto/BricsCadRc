using System;
using System.Linq;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Runtime;
using BricsCadRc.Core;

namespace BricsCadRc.Commands
{
    public static class LayoutCommands
    {
        /// <summary>
        /// RC_LAYOUT (decyzja użytkownika 2026-10-19) — gotowe arkusze A1: rzutnie rzutów 1:50, przekrojów 1:20, użytych detali PH
        /// i detali belek / otworów 1:25 na arkuszu z Prepare GA; gdy nie mieści się — kolejne arkusze (numery po kolei).
        /// Ponowne wywołanie układa arkusze od nowa. Szczegóły: LayoutEngine, LayoutRules.
        /// </summary>
        [CommandMethod("RC_LAYOUT", CommandFlags.Modal)]
        public static void CreateLayouts() => Run();

        [CommandMethod("RC_ARKUSZE", CommandFlags.Modal)]
        public static void CreateLayoutsPl() => Run();

        private static void Run()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;
            const string T = "[RC LAYOUT]";
            try
            {
                string current = "";
                try { current = LayoutManager.Current.CurrentLayout; } catch (System.Exception ex) { Log.Error("RC_LAYOUT.Current", ex); }
                var ctx = LayoutEngine.Prepare(db, current);
                foreach (var e in ctx.Errors) ed.WriteMessage($"\n{T} BŁĄD: {e}.");
                if (ctx.BaseLayoutId.IsNull || ctx.Sheets.Count == 0) { ed.WriteMessage("\n"); return; }
                foreach (var w in ctx.Warnings) ed.WriteMessage($"\n{T} uwaga: {w}.");

                ed.WriteMessage($"\n{T} Arkuszy: {ctx.Sheets.Count} (bazowy: {ctx.BaseLayoutName}).");
                foreach (var s in ctx.Sheets)
                    ed.WriteMessage($"\n{T}   {s.Layout}  {s.Number}  „{s.Title3}”  {s.Scale}:  {string.Join(", ", s.Contents)}");
                if (ctx.OldSheets.Count > 0)
                    ed.WriteMessage($"\n{T} Arkusze z poprzedniego RC_LAYOUT zostaną usunięte i utworzone od nowa: {string.Join(", ", ctx.OldSheets)}.");

                if (ctx.Overwrite.Count > 0)
                {
                    var ko = new PromptKeywordOptions($"\nLayout(y) {string.Join(", ", ctx.Overwrite)} już istnieją (nie z RC_LAYOUT). Zastąpić je nowymi arkuszami? [Tak/Nie] <Nie>: ", "Tak Nie")
                        { AllowNone = true };
                    ko.Keywords.Default = "Nie";
                    var kr = ed.GetKeywords(ko);
                    if (kr.Status != PromptStatus.OK || kr.StringResult != "Tak") { ed.WriteMessage($"\n{T} Przerwano — nic nie zmieniono.\n"); return; }
                }

                bool removeUser = false;
                if (ctx.UserViewports > 0)
                {
                    var ko = new PromptKeywordOptions($"\nArkusz {ctx.BaseLayoutName} ma rzutnie dodane ręcznie ({ctx.UserViewports}). Usunąć je? [Tak/Nie/Anuluj] <Tak>: ", "Tak Nie Anuluj")
                        { AllowNone = true };
                    ko.Keywords.Default = "Tak";
                    var kr = ed.GetKeywords(ko);
                    if (kr.Status != PromptStatus.OK && kr.Status != PromptStatus.None) return;
                    if (kr.StringResult == "Anuluj") return;
                    removeUser = kr.Status == PromptStatus.None || kr.StringResult == "Tak";
                }

                LayoutEngine.Result r;
                using (doc.LockDocument())
                    r = LayoutEngine.Apply(db, ctx, removeUser);
                ed.WriteMessage($"\n{T} Gotowe: rzutnie {r.Viewports}, notatki pod przekrojami {r.Notes}, nowe arkusze {r.Sheets}" +
                                (r.RemovedSheets > 0 ? $", usunięte stare arkusze {r.RemovedSheets}" : "") +
                                (r.Removed > 0 ? $", usunięte stare obiekty arkusza {r.Removed}" : "") +
                                (r.Thawed > 0 ? $", warstwy odmrożone w rzutniach {r.Thawed}" : "") + ".");
                foreach (var m in r.Messages.Distinct()) ed.WriteMessage($"\n{T} {m}.");
                ed.WriteMessage($"\n{T} Sprawdź arkusze; rzutnie są zablokowane (PCN-VIEWPORT) — przesunięcie: odblokuj, przesuń, zablokuj.\n");
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_LAYOUT", ex);
                ed.WriteMessage($"\n{T} Błąd: {ex.Message}\n");
            }
        }
    }
}
