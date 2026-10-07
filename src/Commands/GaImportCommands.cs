using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using Microsoft.Win32;
using Teigha.Runtime;
using BricsCadRc.Core;
using BricsCadRc.Dialogs;

namespace BricsCadRc.Commands
{
    public static class GaImportCommands
    {
        /// <summary>
        /// RC_PRZYGOTUJ_GA — w otwartym pliku RC (default): wybór pliku GA i płyty z listy; płyta wstawiana
        /// jako rzut dolny i górny (czysty obrys, pale, door threshold, opisy), opis płyty: PLOT / SSL / grubość,
        /// tytuły rzutów pod płytami, ramki szablonów rebar_bottom / rebar_top po bokach.
        /// </summary>
        [CommandMethod("RC_PRZYGOTUJ_GA")]
        public static void PrepareFromGa()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;
            const string T = "[RC GA]";

            var fd = new OpenFileDialog
            {
                Title  = "Rysunek GA (plan płyty)",
                Filter = "CAD (*.dwg;*.dxf)|*.dwg;*.dxf"
            };
            if (fd.ShowDialog() != true) return;

            var warnings = new List<string>();
            GaImportEngine.Source src = null;
            try
            {
                src = GaImportEngine.ReadSource(fd.FileName, warnings);
                int k = 0;
                if (src.Plots.Count > 1)
                {
                    var dlg = new PunchingPlotPickerDialog(src.Plots.Select(p => p.ToString()).ToList(), 0,
                        "Rysunek GA — wybierz płytę (PLOT)", "Płyty znalezione w pliku GA.");
                    if (Application.ShowModalWindow(dlg) != true) return;
                    k = dlg.SelectedIndex;
                }
                var plot = src.Plots[k];
                var r = GaImportEngine.Import(doc, src, plot, warnings);
                ed.WriteMessage($"\n{T} {plot.Label}: rzut dolny {r.Bottom} obiektów, rzut górny {r.Top} (z podpisami pali)" +
                    (r.Joined > 0 ? $", połączone kawałki obrysu/uskoku: {r.Joined}" : "") +
                    ". Ponowne wywołanie zastąpi płytę.");
                foreach (var w in warnings.Take(20)) ed.WriteMessage($"\n  {w}");
                ed.WriteMessage("\n");

                // Teksty z layoutów GA (blok tytułowy, SLAB NOTES) — jak ASD-GAI; podgląd, Anuluj = pomiń
                CopyLayoutTexts(src.Db, plot.Label, T);

                try { doc.SendStringToExecute("_.ZOOM _E ", false, false, false); } catch (System.Exception ex) { Log.Error("RC_PRZYGOTUJ_GA.Zoom", ex); }
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_PRZYGOTUJ_GA", ex);
                ed.WriteMessage($"\n{T} Błąd: {ex.Message}\n");
            }
            finally
            {
                src?.Dispose();
            }
        }

        /// <summary>
        /// RC_GA_TEKSTY — teksty z layoutów GA na layouty RC (jak ASD-GAI): blok A1-BL (klient, projekt, APPROVED,
        /// prefiks TITLE_1, DRAWING_NUMBER RCxxx wg numeru GA), SLAB NOTES (powierzchnia, obwód, grubość, objętość,
        /// beton), HYSTOOLS wg grubości. Podgląd zmian przed zapisem.
        /// </summary>
        [CommandMethod("RC_GA_TEKSTY")]
        public static void CopyTextsFromGa()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            const string T = "[RC GA]";
            var fd = new OpenFileDialog { Title = "Rysunek GA (źródło bloku tytułowego i SLAB NOTES)", Filter = "CAD (*.dwg;*.dxf)|*.dwg;*.dxf" };
            if (fd.ShowDialog() != true) return;

            try
            {
                using (var ga = new Teigha.DatabaseServices.Database(false, true))
                {
                    if (fd.FileName.EndsWith(".dwg", StringComparison.OrdinalIgnoreCase))
                        ga.ReadDwgFile(fd.FileName, System.IO.FileShare.Read, true, null);
                    else
                        ga.DxfIn(fd.FileName, null);
                    CopyLayoutTexts(ga, null, T);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_GA_TEKSTY", ex);
                doc.Editor.WriteMessage($"\n{T} Błąd: {ex.Message}\n");
            }
        }

        /// <summary>Wspólne dla RC_PRZYGOTUJ_GA i RC_GA_TEKSTY: odczyt GA, podgląd, zapis.</summary>
        private static void CopyLayoutTexts(Teigha.DatabaseServices.Database ga, string plotLabel, string T)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed = doc.Editor;
            try
            {
                var data = GaTitleEngine.Read(ga, plotLabel);
                if (!data.HasTitleBlock)
                {
                    ed.WriteMessage($"\n{T} W pliku GA nie ma bloku {GaTitleEngine.TitleBlockName} na layoutach — teksty nie zostały skopiowane.\n");
                    return;
                }
                var dlg = new TextConfirmDialog("Teksty z layoutu GA → layouty RC", GaTitleEngine.Preview(data, doc.Database),
                                                "Kopiuj", "Pomiń");
                if (Application.ShowModalWindow(dlg) != true) { ed.WriteMessage($"\n{T} Teksty z GA pominięte.\n"); return; }

                var r = GaTitleEngine.Apply(data, doc.Database);
                ed.WriteMessage($"\n{T} Blok tytułowy zaktualizowany na {r.TitleLayouts} layoutach, SLAB NOTES na {r.NotesLayouts}." +
                                (r.NotesFound ? "" : " (Na layoutach RC nie ma MText ze „SLAB AREA”.)") + "\n");
            }
            catch (System.Exception ex)
            {
                Log.Error("GaImportCommands.CopyLayoutTexts", ex);
                ed.WriteMessage($"\n{T} Teksty z GA — błąd: {ex.Message}\n");
            }
        }
    }
}
