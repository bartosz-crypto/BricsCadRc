using Bricscad.ApplicationServices;
using BricsCadRc.Core;
using Teigha.Runtime;

namespace BricsCadRc.Commands
{
    public class CountCommands
    {
        [CommandMethod("RC_COUNT_BBS", CommandFlags.Modal)]
        public void CountBbs()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;

            // POPRAWKI 26: także pręty AutoRebar / ręczne (RC_SINGLE_BAR + rozkłady) — to samo źródło co RC_BBS
            var warnings = new System.Collections.Generic.List<string>();
            var rows = BbsCounter.CountAll(db, warnings);

            if (rows.Count == 0)
            {
                doc.Editor.WriteMessage("\n[RC SLAB] Nie znaleziono zadnych pretow RC SLAB w rysunku.\n");
                foreach (var w in warnings) doc.Editor.WriteMessage($"\n[RC SLAB] {w}");
                return;
            }

            BbsCounter.PrintToConsole(rows);
            foreach (var w in warnings) doc.Editor.WriteMessage($"\n[RC SLAB] {w}");
            if (warnings.Count > 0) doc.Editor.WriteMessage("\n");
        }
    }
}
