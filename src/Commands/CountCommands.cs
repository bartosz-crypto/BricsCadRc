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

            var rows = BbsCounter.CountAll(db);

            if (rows.Count == 0)
            {
                doc.Editor.WriteMessage("\n[RC SLAB] Nie znaleziono zadnych pretow RC SLAB w rysunku.\n");
                return;
            }

            BbsCounter.PrintToConsole(rows);
        }
    }
}
