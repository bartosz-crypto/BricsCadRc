using System.IO;
using Bricscad.ApplicationServices;
using Teigha.Runtime;
using BricsCadRc.Core;
using BricsCadRc.Dialogs;

namespace BricsCadRc.Commands
{
    public static class AnalysisImportCommands
    {
        /// <summary>
        /// RC_IMPORT_ANALYSIS — wczytanie plików analizy razem: mapy zbrojenia (wklejane od razu: wybór płyty, ramki)
        /// i raport przebicia (zapamiętany w rysunku razem z płytą). Potem Punching (RC_PUNCHING_AUTO) tylko
        /// detaluje — bez pytania o plik xlsx i o płytę.
        /// </summary>
        [CommandMethod("RC_IMPORT_ANALYSIS")]
        public static void ImportAnalysis()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;
            const string T = "[RC ANALIZA]";

            AnalysisStore.TryLoad(db, out string lastMaps, out string lastReport, out string lastPlot);
            var dlg = new AnalysisFilesDialog(lastMaps, lastReport);
            if (Application.ShowModalWindow(dlg) != true) return;
            string maps = dlg.MapsPath, report = dlg.ReportPath;

            string plot = null;
            if (maps.Length > 0)
            {
                plot = MapImportCommands.RunImport(doc, maps);
                if (plot == null)
                {
                    ed.WriteMessage($"\n{T} Import map przerwany — pliki analizy nie zostały zapamiętane.\n");
                    return;
                }
            }
            else
                plot = lastPlot;   // bez nowych map — płyta z poprzedniego importu map

            // Odznaczone pliki nie kasują zapamiętanych: same mapy zostawiają poprzedni raport i odwrotnie
            AnalysisStore.Save(db, maps.Length > 0 ? maps : lastMaps, report.Length > 0 ? report : lastReport, plot);
            if (report.Length == 0 && maps.Length > 0)
            {
                ed.WriteMessage($"\n{T} Wczytano same mapy (płyta {plot}).\n");
                return;
            }
            if (report.Length > 0)
                ed.WriteMessage($"\n{T} Raport przebicia zapamiętany w rysunku: {Path.GetFileName(report)}" +
                                (string.IsNullOrEmpty(plot) ? "" : $" (płyta {plot})") +
                                ". Punching (RC_PUNCHING_AUTO) użyje go bez pytania o plik.\n");
        }
    }
}
