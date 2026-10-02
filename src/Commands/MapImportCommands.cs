using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Microsoft.Win32;
using Teigha.DatabaseServices;
using Teigha.Runtime;
using BricsCadRc.Core;
using BricsCadRc.Dialogs;

namespace BricsCadRc.Commands
{
    public static class MapImportCommands
    {
        /// <summary>
        /// RC_IMPORT_MAP — mapy zbrojenia jak ASD-IMR: wybór płyty, podgląd ramek T1/T2/B1/B2 i wstawienie
        /// we wskazanym miejscu (punkt = lewy górny róg T1). Ponowny import tej samej płyty zastępuje poprzedni.
        /// </summary>
        [CommandMethod("RC_IMPORT_MAP")]
        public static void ImportMap()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            var db = doc.Database;
            const string T = "[RC MAPY]";

            var fd = new OpenFileDialog
            {
                Title  = "Mapy zbrojenia (…_punching_reinf_maps.dxf)",
                Filter = "CAD (*.dxf;*.dwg)|*.dxf;*.dwg"
            };
            if (fd.ShowDialog() != true) return;

            var warnings = new List<string>();
            MapImportEngine.Source src = null;
            try
            {
                src = MapImportEngine.ReadSource(fd.FileName, warnings);

                // 1. Płyta
                int def = MapImportEngine.GuessPlot(db, src.Plots);
                int k = 0;
                if (src.Plots.Count > 1)
                {
                    var dlg = new PunchingPlotPickerDialog(src.Plots.Select(p => p.ToString()).ToList(), Math.Max(0, def),
                        "Mapy zbrojenia — wybierz płytę (PLOT)",
                        def >= 0 ? "Płyty z pliku map. Podświetlona: nazwa płyty jest na rysunku." : "Płyty z pliku map.");
                    if (Application.ShowModalWindow(dlg) != true) return;
                    k = dlg.SelectedIndex;
                }
                var plot = src.Plots[k];

                // 2. Punkt wstawienia z podglądem ramek (lewy górny róg T1)
                var frames = MapImportEngine.MapsToPaste(plot);
                if (frames.Count == 0)
                {
                    ed.WriteMessage($"\n{T} {plot.Label}: brak map T1/T2/B1/B2.\n");
                    return;
                }
                var jig = new MapInsertJig(frames, MapImportEngine.ReferencePoint(plot));
                PromptResult jr;
                try { jr = ed.Drag(jig); }
                finally { jig.ClearTransients(); }
                if (jr.Status != PromptStatus.OK) return;

                // 3. Wklejenie
                var counts = MapImportEngine.Import(doc, src, plot, jig.InsertPoint, warnings);
                ed.WriteMessage($"\n{T} {plot.Label}: wklejono " +
                    string.Join(", ", counts.Select(c => $"{c.Key} ({c.Value} obiektów)")) +
                    ". Ponowny import tej płyty zastąpi te mapy.");
                foreach (var w in warnings.Take(20)) ed.WriteMessage($"\n  {w}");
                ed.WriteMessage("\n");
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_IMPORT_MAP", ex);
                ed.WriteMessage($"\n{T} Błąd: {ex.Message}\n");
            }
            finally
            {
                src?.Dispose();
            }
        }
    }
}
