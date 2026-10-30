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
                var chosen = new List<GaImportEngine.GaPlot> { src.Plots[0] };
                if (src.Plots.Count > 1)
                {
                    // prośba Bartka 2026-10-26 (PLOT 357-362): kilka płyt naraz — Ctrl / Shift albo „Zaznacz wszystkie” → jeden rysunek RC
                    var dlg = new PunchingPlotPickerDialog(src.Plots.Select(p => p.ToString()).ToList(), 0,
                        "Rysunek GA — wybierz płytę (PLOT / GARAGE)",
                        "Płyty znalezione w pliku GA. Kilka płyt naraz (jeden rysunek RC): Ctrl / Shift + klik albo „Zaznacz wszystkie”.",
                        multiSelect: true);
                    if (Application.ShowModalWindow(dlg) != true) return;
                    List<int> idx = dlg.SelectedIndices;
                    if (idx.Count == 0) return;
                    chosen = idx.Select(i => src.Plots[i]).ToList();
                }
                var plot = GaImportEngine.Group(chosen);

                // decyzja 2026-10-15: Drawn / Checked do arkusza RC (A1-BL i tabela rewizji By / Chkd), podpowiedź = ostatnie wpisane
                // okno zamiast linii poleceń (2026-10-15: w linii poleceń podpowiedź łapała resztki kolejki, np. „'._ZOOM”)
                var (lastD, lastC) = RcTemplateEngine.LoadPeople();
                var pd = new SheetPeopleDialog(lastD, lastC);
                if (Application.ShowModalWindow(pd) != true) return;
                string drawn = pd.Drawn, chk = pd.Checked;
                RcTemplateEngine.SavePeople(drawn, chk);

                // decyzja 2026-10-14: brakujące elementy „pliku RC” z szablonu wtyczki — tytuły rzutów i czysty arkusz RC (bez rzutni, zawsze na nowo)
                // przed wstawieniem płyty (rzuty ustawiane nad tytułami), detale PH1–PH9 po wstawieniu (obok rzutu górnego)
                var tplRep = new RcTemplateEngine.Report();
                Teigha.DatabaseServices.Database tpl = null;
                try
                {
                    tpl = RcTemplateEngine.OpenTemplate();
                    if (tpl == null) ed.WriteMessage($"\n{T} Szablon RC (zasób wtyczki) niedostępny — tytuły / layout / detale PH nie zostaną dodane.");
                    else
                        using (doc.LockDocument())
                        {
                            RcTemplateEngine.EnsureStyles(doc.Database, tpl, tplRep);   // decyzja 2026-10-20: wszystkie style wtyczki od razu
                            RcTemplateEngine.EnsureStyles(doc.Database, tpl, tplRep);   // decyzja 2026-10-20: wszystkie style wtyczki od razu
                            RcTemplateEngine.EnsureTitles(doc.Database, tpl, tplRep);
                            // czysty arkusz RC nazwany wg GA (GA0300… → RC0300 + rewizja szablonu, np. RC0300C1)
                            var gaData = GaTitleEngine.Read(src.Db, plot.Label);
                            string sheet = GaTitleEngine.BuildDrawingSuffix(gaData, 0) + RcTemplateEngine.TemplateRev(tpl);
                            RcTemplateEngine.CreateSheet(doc.Database, tpl, sheet, tplRep);
                            if (tplRep.Layout != null)
                                RcTemplateEngine.FillSheetInfo(doc.Database, tplRep.Layout, drawn, chk, DateTime.Today, tplRep);
                        }
                }
                catch (System.Exception ex)
                {
                    Log.Error("RC_PRZYGOTUJ_GA.Template", ex);
                    ed.WriteMessage($"\n{T} Szablon RC — błąd: {ex.Message}");
                }

                var r = GaImportEngine.Import(doc, src, plot, warnings);

                if (tpl != null)
                {
                    try
                    {
                        if (RcTemplateEngine.PhAnchor(doc.Database, r.OutlineTop) is var a && a.HasValue)
                            using (doc.LockDocument())
                                RcTemplateEngine.EnsurePh(doc.Database, tpl, a.Value.left, a.Value.top, tplRep);
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error("RC_PRZYGOTUJ_GA.TemplatePh", ex);
                        ed.WriteMessage($"\n{T} Detale PH z szablonu — błąd: {ex.Message}");
                    }
                    finally { tpl.Dispose(); }
                }
                foreach (var m in tplRep.Messages) ed.WriteMessage($"\n{T} Szablon RC: {m}.");
                ed.WriteMessage($"\n{T} {plot.Label}: rzut dolny {r.Bottom} obiektów, rzut górny {r.Top} (z podpisami pali)" +
                    (r.Joined > 0 ? $", połączone kawałki obrysu/uskoku: {r.Joined}" : "") +
                    (r.NibClosed > 0 ? $", nib domknięty: {r.NibClosed}" : "") +
                    (r.Beams > 0 ? $", strefy belek: {r.Beams} (H = {string.Join("/", r.BeamHeights.Select(h => h > 0 ? h.ToString(System.Globalization.CultureInfo.InvariantCulture) : "?"))}; pręty belki: RC_BEAM / RC_GENERATE → Beam)" : "") +
                    (r.Cantilevers > 0 ? $", wsporniki (wyłączone z obrysu siatki): {r.Cantilevers}" : "") +
                    (r.PileCaps > 0 ? $", pile cap: {r.PileCaps}" : "") +
                    ". Ponowne wywołanie zastąpi płytę.");
                foreach (var w in warnings.Take(20)) ed.WriteMessage($"\n  {w}");
                ed.WriteMessage("\n");

                // decyzja 2026-10-15: ustawienia wydruku jak w rysunku wzorcowym (PSLTSCALE 0, rzutnie na PCN-VIEWPORT, SD-SECTION)
                ApplyPlotSetup(doc, T);

                // Teksty z layoutów GA (blok tytułowy, SLAB NOTES) — jak ASD-GAI; decyzja 2026-10-16: bez okna podglądu — od razu „Kopiuj”
                CopyLayoutTexts(src.Db, plot.Label, T, confirm: false);
                // tabela rewizji: App = APPROVED skopiowane z GA (2026-10-16)
                if (tplRep.Layout != null)
                    try
                    {
                        var appRep = new RcTemplateEngine.Report();
                        using (doc.LockDocument()) RcTemplateEngine.FillRevApproved(doc.Database, tplRep.Layout, appRep);
                        foreach (var m in appRep.Messages) ed.WriteMessage($"\n{T} Szablon RC: {m}.");
                    }
                    catch (System.Exception ex) { Log.Error("RC_PRZYGOTUJ_GA.RevApproved", ex); }

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
        /// RC_USTAW_WYDRUK (decyzja 2026-10-15): ustawienia wydruku jak w rysunku wzorcowym RC — na istniejących rysunkach
        /// (RC_PRZYGOTUJ_GA robi to samo na końcu). Szczegóły: PlotSetupEngine.
        /// </summary>
        [CommandMethod("RC_USTAW_WYDRUK")]
        public static void PlotSetup()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            ApplyPlotSetup(doc, "[RC WYDRUK]");
        }

        private static void ApplyPlotSetup(Document doc, string T)
        {
            var ed = doc.Editor;
            try
            {
                PlotSetupEngine.Result r;
                using (doc.LockDocument())
                    r = PlotSetupEngine.Apply(doc.Database);
                ed.WriteMessage($"\n{T} Wydruk jak we wzorcu: layouty {r.Layouts}, rzutnie przeniesione na {PlotSetupEngine.ViewportLayer} " +
                                $"(niedrukowana, zablokowane): {r.Viewports}, znaczniki przekroju poprawione: {r.SectionMarks}, " +
                                $"warstwy prętów przywrócone na niebieski (5): {r.BarLayers}.");
                foreach (var m in r.Messages) ed.WriteMessage($"\n{T} {m}.");
                ed.WriteMessage("\n");
            }
            catch (System.Exception ex)
            {
                Log.Error("GaImportCommands.ApplyPlotSetup", ex);
                ed.WriteMessage($"\n{T} Ustawienia wydruku — błąd: {ex.Message}\n");
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
        private static void CopyLayoutTexts(Teigha.DatabaseServices.Database ga, string plotLabel, string T, bool confirm = true)
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
                if (confirm)
                {
                    var dlg = new TextConfirmDialog("Teksty z layoutu GA → layouty RC", GaTitleEngine.Preview(data, doc.Database),
                                                    "Kopiuj", "Pomiń");
                    if (Application.ShowModalWindow(dlg) != true) { ed.WriteMessage($"\n{T} Teksty z GA pominięte.\n"); return; }
                }

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
