using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Microsoft.Win32;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using BricsCadRc.Core;
using BricsCadRc.Dialogs;

namespace BricsCadRc.Commands
{
    public static class PunchingTagCommands
    {
        [CommandMethod("RC_PUNCHING_SUMMARY_BARS")]
        public static void RcPunchingSummaryBars()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            try
            {
                PunchingTagEngine.PhCountTotals totals;
                try
                {
                    totals = PunchingTagEngine.ReadPhCountsFromActiveDrawing(doc);
                }
                catch (InvalidOperationException ioe)
                {
                    ed.WriteMessage($"\n{ioe.Message}");
                    return;
                }

                if (totals.TotalForPos501 == 0 && totals.TotalForPos502 == 0)
                {
                    ed.WriteMessage(
                        "\n[RC_PUNCHING_SUMMARY_BARS] No PH counts found in " +
                        "AP-TEXT MTEXTs — run RC_PUNCHING_AUTO first.");
                    return;
                }

                var dlg = new PunchingSummaryDialog(totals);
                if (Application.ShowModalWindow(dlg) != true) return;

                // Punkty PRZED usunięciem poprzednich prętów — Esc nie kasuje poprzedniego wyniku
                var placements = new List<(int dia, int realCount, Point3d insertPt)>();
                if (dlg.Wants501)
                {
                    var ptRes = ed.GetPoint("\nClick placement point for H12 × 2250mm: ");
                    if (ptRes.Status != PromptStatus.OK) return;
                    placements.Add((12, dlg.Count501, ptRes.Value));
                }
                if (dlg.Wants502)
                {
                    var ptRes = ed.GetPoint("\nClick placement point for H16 × 2500mm: ");
                    if (ptRes.Status != PromptStatus.OK) return;
                    placements.Add((16, dlg.Count502, ptRes.Value));
                }

                // Numery przed usunięciem poprzednich prętów — poprzedni pręt zbiorczy zachowuje swój numer
                var reused = new List<string>();
                var numbers = PunchingAutoEngine.ResolveSummaryNumbers(db, placements.Select(p => p.dia), reused);
                WarnReused(reused);
                int prevErased = PunchingTagEngine.DeleteSummaryBars(doc);
                if (prevErased > 0)
                    ed.WriteMessage($"\n[RC_PUNCHING_SUMMARY_BARS] Removed {prevErased} entities from previous run.");
                PlaceSummaryBars(doc, placements, numbers, "[RC_PUNCHING_SUMMARY_BARS]");
                ReportDetails(doc, "[RC_PUNCHING_SUMMARY_BARS]", null);
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_PUNCHING_SUMMARY_BARS", ex);
                ed.WriteMessage($"\n[RC_PUNCHING_SUMMARY_BARS] EXCEPTION: {ex.Message}");
            }
        }

        /// <summary>
        /// Pręty zbiorcze do BBS: H12 L2250, H16 L2500. Pręt w elewacji + rozkład z 1 prętem
        /// na rysunku, prawdziwa liczba w CountDisplay (BBS liczy EffectiveCount).
        /// Numery z <paramref name="numbers"/> (Ø → numer, <see cref="PunchingAutoEngine.ResolveSummaryNumbers"/>):
        /// seria górna 101+, istniejący pręt o tym samym Ø i długości → ten sam numer (decyzja użytkownika 2026-10-16;
        /// dawniej 501 / 502). Pręt w elewacji oznaczony XData RC_PH [SUMMARY, Ø].
        /// </summary>
        public static void PlaceSummaryBars(Document doc, List<(int dia, int realCount, Point3d insertPt)> placements,
                                            Dictionary<int, int> numbers, string tag)
        {
            var db = doc.Database;
            var ed = doc.Editor;
            using (doc.LockDocument())
            {
                foreach (var (dia, realCount, insertPt) in placements)
                {
                    if (realCount <= 0) continue;
                    double lenA = PhDetailRules.SummaryLength(dia);
                    if (lenA <= 0 || !numbers.TryGetValue(dia, out int posNr) || posNr <= 0)
                    {
                        ed.WriteMessage($"\n{tag} Brak numeru pręta H{dia} — pominięty.");
                        continue;
                    }
                    const  string layerCode = "T1";

                    // 1) RC_BAR — elevation polyline
                    var elevBar = BuildSummaryBarData(dia, posNr, lenA, layerCode);
                    elevBar.Mark = BarData.FormatMark(dia, posNr, 0, 1);  // "H12-106"
                    ObjectId barId = SingleBarEngine.PlaceBar(db, elevBar, insertPt);
                    using (var trTag = db.TransactionManager.StartTransaction())
                    {
                        if (trTag.GetObject(barId, OpenMode.ForWrite) is Entity barTag)
                            PunchingAutoEngine.TagSummary(trTag, db, barTag, dia);
                        trTag.Commit();
                    }

                    // 2) Elevation label: arrow at bar body, text above-right
                    Point3d textPt = new Point3d(insertPt.X + lenA * 0.5, insertPt.Y + 200.0, 0);
                    Point3d arrowTip;
                    using (var trTip = db.TransactionManager.StartTransaction())
                    {
                        arrowTip = SingleBarEngine.GetBarArrowTip(barId, elevBar, textPt, trTip);
                        trTip.Commit();
                    }
                    ObjectId labelId = SingleBarEngine.PlaceBarLabel(db, arrowTip, textPt, elevBar.Mark, barId);
                    if (!labelId.IsNull)
                    {
                        using (var trLbl = db.TransactionManager.StartTransaction())
                        {
                            var barEnt = trLbl.GetObject(barId, OpenMode.ForWrite) as Entity;
                            if (barEnt != null)
                            {
                                elevBar.LabelHandle = labelId.Handle.ToString();
                                BarXData.Write(barEnt, elevBar);
                            }
                            trLbl.Commit();
                        }
                    }

                    // 3) RC_DISTRIBUTION — 1 bar drawn visually, realCount in XData.
                    //    Opis bez sztucznego rozstawu (1 pręt na rysunku): „N H12-106”.
                    var distBar = BuildSummaryBarData(dia, posNr, lenA, layerCode);
                    distBar.Mark            = BarData.FormatMark(dia, posNr, 0, 1);
                    distBar.Spacing         = 1000.0;
                    distBar.Direction       = "X";
                    distBar.Count           = 0;   // auto-calc → 1 (span 100 < spacing 1000)
                    distBar.SourceBarHandle = barId.Handle.Value.ToString("X8");

                    const double distOffset = 1000.0;
                    const double cover      = 40.0;
                    double distX0 = insertPt.X;
                    double distX1 = insertPt.X + lenA;
                    double distY0 = insertPt.Y - distOffset - cover;
                    double distY1 = distY0 + 100.0;

                    var barResult = BarBlockEngine.GenerateFromBounds(
                        db, distX0, distY0, distX1, distY1,
                        distBar, horizontal: true, posNr: posNr);

                    if (!barResult.IsValid)
                    {
                        ed.WriteMessage($"\n{tag} Failed to generate distribution for poz. {posNr} — skipping.");
                        continue;
                    }

                    using (var trCount = db.TransactionManager.StartTransaction())
                    {
                        var brCount = trCount.GetObject(barResult.BlockRefId, OpenMode.ForWrite) as BlockReference;
                        if (brCount != null)
                        {
                            var xd = BarBlockEngine.ReadXData(brCount);
                            if (xd != null)
                            {
                                xd.CountDisplay = realCount;
                                BarBlockEngine.WriteXData(brCount, xd);
                            }
                        }
                        trCount.Commit();
                    }

                    // 4) Annotation leader (visual based on Count=1, label on EffectiveCount)
                    distBar.CountDisplay = realCount;
                    var annotResult = AnnotationEngine.CreateLeader(
                        db, barResult, distBar,
                        leaderHorizontal: true, posNr: posNr,
                        barsHorizontal: true,
                        leaderRight: true, leaderUp: true);

                    if (annotResult.BlockRefId != ObjectId.Null)
                        BarBlockEngine.LinkAnnotation(db, barResult.BlockRefId, annotResult.BlockRefId);

                    // 5) Seria górna 101+ — nie podnosi licznika dołu (PositionCounter.Increment ją pomija)
                    PositionCounter.CommitUsed(db, posNr);

                    ed.WriteMessage($"\n{tag} Poz. {posNr}: {realCount} × {distBar.Mark} L={lenA:F0}");
                }
            }
        }

        // ----------------------------------------------------------------
        // RC_PUNCHING_AUTO — przebicie z raportu xlsx
        // ----------------------------------------------------------------

        [CommandMethod("RC_PUNCHING_AUTO")]
        public static void RcPunchingAuto()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            // Raport wczytany przez Import Analysis (RC_IMPORT_ANALYSIS) — tylko detalowanie, bez wyboru pliku i płyty
            if (AnalysisStore.TryLoad(doc.Database, out _, out string storedReport, out string storedPlot)
                && !string.IsNullOrEmpty(storedReport))
            {
                if (System.IO.File.Exists(storedReport))
                {
                    doc.Editor.WriteMessage($"\n[RC PUNCHING] Raport z Import Analysis: {System.IO.Path.GetFileName(storedReport)}" +
                        (string.IsNullOrEmpty(storedPlot) ? "" : $", płyta {storedPlot}") + ".");
                    RunPunching(doc, storedReport, string.IsNullOrEmpty(storedPlot) ? null : storedPlot);
                    return;
                }
                doc.Editor.WriteMessage($"\n[RC PUNCHING] Nie ma pliku raportu z Import Analysis ({storedReport}) — wskaż raport.");
            }

            var fd = new OpenFileDialog
            {
                Title  = "Raport przebicia (report_punching.xlsx)",
                Filter = "Excel (*.xlsx)|*.xlsx"
            };
            if (fd.ShowDialog() != true) return;
            RunPunching(doc, fd.FileName);
        }

        private static string NormPlot(string s)
            => System.Text.RegularExpressions.Regex.Replace((s ?? "").ToUpperInvariant(), @"\s+", " ").Trim();

        /// <summary>
        /// Przebicie z raportu xlsx (tagi PH, szablony detali, pręty 501/502). <paramref name="plotLabel"/> —
        /// płyta wybrana już wcześniej (np. przy imporcie map w RC_IMPORT_ANALYSIS): brana bez pytania, gdy jest
        /// w raporcie; inaczej zwykły wybór z listy.
        /// </summary>
        internal static void RunPunching(Document doc, string reportPath, string plotLabel = null)
        {
            var ed = doc.Editor;
            var db = doc.Database;
            const string T = "[RC PUNCHING]";
            try
            {
                var warnings = new List<string>();
                var plots = PunchingReport.Read(reportPath, warnings);
                ed.WriteMessage($"\n{T} Raport: {plots.Count} płyt, {plots.Sum(p => p.Piles.Count)} pali.");

                var res = PunchingAutoEngine.Analyze(db, plots, (System.Func<List<PunchingAutoEngine.PlotChoice>, int, List<int>>)((choices, def) =>
                {
                    // Raport bez podziału na płyty: płyta z map nie ma tu odpowiednika — cały raport, bez pytania
                    if (choices.Count == 1 && plots[0].Implicit) return new List<int> { 0 };
                    if (!string.IsNullOrEmpty(plotLabel))
                    {
                        // POPRAWKI 24: dawna etykieta sklejona z nagłówka map („PLOT 526h = 225 mm | …”) → „PLOT 526”
                        string wanted = NormPlot(MTextParagraphs.RepairGluedMapLabel(plotLabel));
                        int hit = choices.FindIndex(c => NormPlot(c.Label) == wanted);
                        if (hit >= 0) return new List<int> { hit };
                        ed.WriteMessage($"\n{T} Płyty {plotLabel} z map nie ma w raporcie przebicia — wybierz płytę z listy.");
                    }
                    if (choices.Count == 1) return new List<int> { 0 };
                    // kilka płyt naraz (2026-10-26, PLOT 357-362): Ctrl / Shift / „Zaznacz wszystkie” — tagi i szablony PH dla całości
                    var dlg = new PunchingPlotPickerDialog(choices.Select(c => c.ToString()).ToList(), def,
                        null, "Płyty z raportu przebicia. Kilka płyt: Ctrl / Shift albo „Zaznacz wszystkie”.", multiSelect: true);
                    if (Application.ShowModalWindow(dlg) != true) return null;
                    List<int> sel = dlg.SelectedIndices;
                    return sel;
                }));
                if (res.Plot == null && res.Warnings.Count == 0) return;   // anulowany wybór płyty
                warnings.AddRange(res.Warnings);
                if (res.Plot == null || res.Items.Count == 0)
                {
                    foreach (var w in warnings) ed.WriteMessage($"\n  {w}");
                    ed.WriteMessage($"\n{T} Przerwano — brak dopasowanych pali.\n");
                    return;
                }

                var stats = PunchingAutoEngine.Apply(doc, res);
                warnings.AddRange(stats.Warnings);

                // Podsumowanie
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"{res.Plot}: {res.Items.Count} pali dopasowanych.");
                for (int k = 1; k <= 9; k++)
                {
                    int n = res.Count("PH" + k);
                    if (n > 0) sb.AppendLine($"  PH{k}: {n}  ({string.Join(", ", PunchingAutoEngine.SortIds(res.Items.Where(i => i.Code == "PH" + k).Select(i => i.R.PileId)))})");
                }
                var manual = res.Items.Where(i => i.Code == "MANUAL").ToList();
                if (manual.Count > 0)
                    sb.AppendLine($"  MANUAL: {manual.Count}  ({string.Join(", ", manual.Select(i => i.R.PileId + " — " + i.Reason))})");
                int none = res.Count("NONE");
                if (none > 0)
                    sb.AppendLine($"  bez zbrojenia: {none}  ({string.Join(", ", PunchingAutoEngine.SortIds(res.Items.Where(i => i.Code == "NONE").Select(i => i.R.PileId)))})");
                sb.AppendLine();
                sb.AppendLine($"Tagi: {stats.Tagged}, MANUAL: {stats.Manual}, usunięte stare: {stats.Cleaned}.");
                sb.AppendLine($"Szablony detali: {stats.TemplatesUpdated} zaktualizowane, {stats.TemplatesCrossed} nieużyte (przekreślone).");
                sb.AppendLine($"BBS: {res.Bars501} × H12 L2250, {res.Bars502} × H16 L2500 (numery z serii górnej 101+).");
                sb.AppendLine();
                sb.AppendLine("Pal | Typ | Util | Reinf. | PH");
                foreach (var it in res.Items.OrderBy(i => i.Code).ThenBy(i => i.R.PileId))
                    sb.AppendLine($"{it.R.PileId} | {it.R.Type} | {it.R.Util:F1}% | {it.R.Reinf} | {it.Code}");

                foreach (var line in sb.ToString().Split('\n').Take(16)) ed.WriteMessage($"\n{T} {line.TrimEnd()}");

                // Pręty zbiorcze H12 / H16 (Enter = pomiń) — numery z serii górnej 101+
                var placements = new List<(int dia, int realCount, Point3d insertPt)>();
                foreach (var (dia, n, desc) in new[] { (12, res.Bars501, "H12 L=2250"), (16, res.Bars502, "H16 L=2500") })
                {
                    if (n <= 0) continue;
                    var ppo = new PromptPointOptions($"\n{T} Punkt dla prętów {desc} ({n} szt.) do BBS <Enter = pomiń>: ")
                        { AllowNone = true };
                    var ppr = ed.GetPoint(ppo);
                    if (ppr.Status == PromptStatus.Cancel) break;
                    if (ppr.Status == PromptStatus.OK) placements.Add((dia, n, ppr.Value));
                }
                if (placements.Count > 0)
                {
                    var reused = new List<string>();
                    var numbers = PunchingAutoEngine.ResolveSummaryNumbers(db, placements.Select(p => p.dia), reused);
                    WarnReused(reused);
                    warnings.AddRange(reused);
                    int prev = PunchingTagEngine.DeleteSummaryBars(doc);
                    if (prev > 0) ed.WriteMessage($"\n{T} Usunięto poprzednie pręty zbiorcze ({prev} obiektów).");
                    PlaceSummaryBars(doc, placements, numbers, T);
                }
                ReportDetails(doc, T, warnings);

                Application.ShowModalWindow(new PunchingTagResultsDialog(sb.ToString().TrimEnd(), warnings));
                ed.WriteMessage($"\n{T} Gotowe.\n");
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_PUNCHING_AUTO", ex);
                ed.WriteMessage($"\n{T} Błąd: {ex.Message}\n");
            }
        }

        // ----------------------------------------------------------------
        // RC_PH_UPDATE — opisy detali PH po zmianie numerów prętów
        // ----------------------------------------------------------------

        /// <summary>
        /// Aktualizacja detali PH: opisy prętów „7 H12-113-200 T” → numery prętów zbiorczych z rysunku (H12 L2250,
        /// H16 L2500), tytuły „… PILE CONDITION PHn” / „EXTRA BARS …” wyśrodkowane. Po zmianie numeru pręta.
        /// </summary>
        [CommandMethod("RC_PH_UPDATE")]
        public static void RcPhUpdate()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                ReportDetails(doc, "[RC PH UPDATE]", null);
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_PH_UPDATE", ex);
                doc.Editor.WriteMessage($"\n[RC PH UPDATE] Błąd: {ex.Message}\n");
            }
        }

        /// <summary>Aktualizacja detali PH + komunikat w linii poleceń; ostrzeżenia do <paramref name="warnings"/> (null → linia poleceń).</summary>
        private static void ReportDetails(Document doc, string tag, List<string> warnings)
        {
            var ed = doc.Editor;
            PunchingAutoEngine.DetailStats st;
            using (doc.LockDocument())
                st = PunchingAutoEngine.UpdateDetails(doc.Database);
            string nums = st.Numbers.Count == 0 ? "brak"
                : string.Join(", ", st.Numbers.OrderBy(k => k.Key).Select(k => $"H{k.Key} = {k.Value:D2}"));
            ed.WriteMessage($"\n{tag} Detale PH: pręty {nums}; opisy prętów {st.LabelsChanged} zmienione z {st.Labels}"
                          + (st.Titles > 0 ? $", tytuły {st.Titles} poprawione" : "") + ".");
            foreach (var w in st.Warnings)
            {
                if (warnings != null) warnings.Add(w);
                else ed.WriteMessage($"\n  {w}");
            }
        }

        /// <summary>Okienko z ostrzeżeniem: pręt zbiorczy dostał numer istniejącego, takiego samego pręta.</summary>
        private static void WarnReused(List<string> reused)
        {
            if (reused == null || reused.Count == 0) return;
            System.Windows.MessageBox.Show(
                string.Join("\n", reused) + "\n\nPręty przebicia będą liczone w BBS razem z istniejącą pozycją.",
                "Punching — pręt już istnieje",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }

        private static BarData BuildSummaryBarData(
            int dia, int posNr, double lengthA, string layerCode)
        {
            return new BarData
            {
                Diameter  = dia,
                ShapeCode = "00",
                LengthA   = lengthA,
                LayerCode = layerCode,
                Position  = "TOP",
                Cover     = 40.0,
            };
        }
    }
}
