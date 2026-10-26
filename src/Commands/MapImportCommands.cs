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

            var fd = new OpenFileDialog
            {
                Title  = "Mapy zbrojenia (…_punching_reinf_maps.dxf)",
                Filter = "CAD (*.dxf;*.dwg)|*.dxf;*.dwg"
            };
            if (fd.ShowDialog() != true) return;
            RunImport(doc, fd.FileName);
        }

        /// <summary>
        /// Import map z pliku: wybór płyt (kilka naraz — prośba Bartka 2026-10-26, jak Prepare GA), podgląd ramek, wklejenie.
        /// Zwraca etykiety wklejonych płyt („PLOT 6-8”) albo null (anulowano / błąd). Używane też przez RC_IMPORT_ANALYSIS.
        /// Kilka płyt — jedno wskazanie (lewy górny róg T1 pierwszej), pozostałe w tym samym układzie co w pliku map.
        /// </summary>
        internal static List<string> RunImport(Document doc, string path)
        {
            var ed = doc.Editor;
            var db = doc.Database;
            const string T = "[RC MAPY]";
            var warnings = new List<string>();
            MapImportEngine.Source src = null;
            try
            {
                src = MapImportEngine.ReadSource(path, warnings);
                if (src.Plots.Count == 0)
                {
                    ed.WriteMessage($"\n{T} W pliku map nie ma płyt (PH-SLAB-HEADER „PLOT …” / „GARAGE …”).\n");
                    return null;
                }

                // 1. Płyty (Ctrl / Shift / „Zaznacz wszystkie”)
                int def = MapImportEngine.GuessPlot(db, src.Plots);
                var chosen = new List<int> { 0 };
                if (src.Plots.Count > 1)
                {
                    var dlg = new PunchingPlotPickerDialog(src.Plots.Select(p => p.ToString()).ToList(), Math.Max(0, def),
                        "Mapy zbrojenia — wybierz płyty (PLOT / GARAGE)",
                        (def >= 0 ? "Płyty z pliku map. Podświetlona: nazwa płyty jest na rysunku." : "Płyty z pliku map.") +
                        " Kilka płyt: Ctrl / Shift albo „Zaznacz wszystkie”.", multiSelect: true);
                    if (Application.ShowModalWindow(dlg) != true) return null;
                    List<int> sel = dlg.SelectedIndices;
                    if (sel == null || sel.Count == 0) return null;
                    chosen = sel;
                }
                var plots = chosen.Select(i => src.Plots[i]).ToList();
                var labels = plots.Select(p => p.Label).ToList();

                // 2. Punkt wstawienia z podglądem ramek (lewy górny róg T1 pierwszej płyty; pozostałe jak w pliku map)
                var withMaps = plots.Where(p => MapImportEngine.MapsToPaste(p).Count > 0).ToList();
                foreach (var p in plots.Where(p => !withMaps.Contains(p)))
                    ed.WriteMessage($"\n{T} {p.Label}: brak map T1/T2/B1/B2.");
                if (withMaps.Count == 0) { ed.WriteMessage("\n"); return labels; }
                var refPt = MapImportEngine.ReferencePoint(withMaps[0]);
                var jig = new MapInsertJig(withMaps.SelectMany(MapImportEngine.MapsToPaste), refPt);
                PromptResult jr;
                try { jr = ed.Drag(jig); }
                finally { jig.ClearTransients(); }
                if (jr.Status != PromptStatus.OK) return null;
                var shift = jig.InsertPoint - refPt;

                // 3. Wklejenie — każda płyta osobno (XData RC_MAP [PLOT] — ponowny import płyty zastępuje jej mapy)
                foreach (var plot in withMaps)
                {
                    var counts = MapImportEngine.Import(doc, src, plot, MapImportEngine.ReferencePoint(plot) + shift, warnings);
                    ed.WriteMessage($"\n{T} {plot.Label}: wklejono " +
                        string.Join(", ", counts.Select(c => $"{c.Key} ({c.Value} obiektów)")) + ".");
                }
                ed.WriteMessage(" Ponowny import płyty zastąpi jej mapy.");
                foreach (var w in warnings.Take(20)) ed.WriteMessage($"\n  {w}");
                ed.WriteMessage("\n");
                return labels;
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_IMPORT_MAP", ex);
                ed.WriteMessage($"\n{T} Błąd: {ex.Message}\n");
                return null;
            }
            finally
            {
                src?.Dispose();
            }
        }
    }
}
