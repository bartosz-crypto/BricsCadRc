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
                var placements = new List<(int posNr, int realCount, Point3d insertPt)>();
                if (dlg.Wants501)
                {
                    var ptRes = ed.GetPoint("\nClick placement point for Poz. 501 (H12 × 2250mm): ");
                    if (ptRes.Status != PromptStatus.OK) return;
                    placements.Add((501, dlg.Count501, ptRes.Value));
                }
                if (dlg.Wants502)
                {
                    var ptRes = ed.GetPoint("\nClick placement point for Poz. 502 (H16 × 2500mm): ");
                    if (ptRes.Status != PromptStatus.OK) return;
                    placements.Add((502, dlg.Count502, ptRes.Value));
                }

                int prevErased = PunchingTagEngine.DeleteSummaryBars(doc);
                if (prevErased > 0)
                    ed.WriteMessage($"\n[RC_PUNCHING_SUMMARY_BARS] Removed {prevErased} entities from previous run.");
                PlaceSummaryBars(doc, placements, "[RC_PUNCHING_SUMMARY_BARS]");
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_PUNCHING_SUMMARY_BARS", ex);
                ed.WriteMessage($"\n[RC_PUNCHING_SUMMARY_BARS] EXCEPTION: {ex.Message}");
            }
        }

        /// <summary>
        /// Pręty zbiorcze do BBS: 501 = H12 L2250, 502 = H16 L2500. Pręt w elewacji + rozkład z 1 prętem
        /// na rysunku, prawdziwa liczba w CountDisplay (BBS liczy EffectiveCount).
        /// </summary>
        public static void PlaceSummaryBars(Document doc, List<(int posNr, int realCount, Point3d insertPt)> placements, string tag)
        {
            var db = doc.Database;
            var ed = doc.Editor;
            using (doc.LockDocument())
            {
                foreach (var (posNr, realCount, insertPt) in placements)
                {
                    if (realCount <= 0) continue;
                    int    dia     = posNr == 501 ? 12 : 16;
                    double lenA    = posNr == 501 ? 2250.0 : 2500.0;
                    const  string layerCode = "T1";

                    // 1) RC_BAR — elevation polyline
                    var elevBar = BuildSummaryBarData(dia, posNr, lenA, layerCode);
                    elevBar.Mark = BarData.FormatMark(dia, posNr, 0, 1);  // "H12-501"
                    ObjectId barId = SingleBarEngine.PlaceBar(db, elevBar, insertPt);

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
                                SingleBarEngine.WriteXData(barEnt, elevBar);
                            }
                            trLbl.Commit();
                        }
                    }

                    // 3) RC_DISTRIBUTION — 1 bar drawn visually, realCount in XData.
                    //    Opis bez sztucznego rozstawu (1 pręt na rysunku): „N H12-501”.
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

                    // 5) Seria 501+ — nie podnosi licznika (PositionCounter pomija serię oddzielną)
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
            var ed = doc.Editor;
            var db = doc.Database;
            const string T = "[RC PUNCHING]";

            var fd = new OpenFileDialog
            {
                Title  = "Raport przebicia (report_punching.xlsx)",
                Filter = "Excel (*.xlsx)|*.xlsx"
            };
            if (fd.ShowDialog() != true) return;

            try
            {
                var warnings = new List<string>();
                var plots = PunchingReport.Read(fd.FileName, warnings);
                ed.WriteMessage($"\n{T} Raport: {plots.Count} płyt, {plots.Sum(p => p.Piles.Count)} pali.");

                var res = PunchingAutoEngine.Analyze(db, plots, (choices, def) =>
                {
                    if (choices.Count == 1) return 0;
                    var dlg = new PunchingPlotPickerDialog(choices.Select(c => c.ToString()).ToList(), def);
                    return Application.ShowModalWindow(dlg) == true ? dlg.SelectedIndex : -1;
                });
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
                sb.AppendLine($"BBS: 501 = {res.Bars501} × H12 L2250, 502 = {res.Bars502} × H16 L2500.");
                sb.AppendLine();
                sb.AppendLine("Pal | Typ | Util | Reinf. | PH");
                foreach (var it in res.Items.OrderBy(i => i.Code).ThenBy(i => i.R.PileId))
                    sb.AppendLine($"{it.R.PileId} | {it.R.Type} | {it.R.Util:F1}% | {it.R.Reinf} | {it.Code}");

                foreach (var line in sb.ToString().Split('\n').Take(16)) ed.WriteMessage($"\n{T} {line.TrimEnd()}");

                // Pręty zbiorcze 501/502 (Enter = pomiń)
                var placements = new List<(int posNr, int realCount, Point3d insertPt)>();
                foreach (var (pos, n, desc) in new[] { (501, res.Bars501, "H12 L=2250"), (502, res.Bars502, "H16 L=2500") })
                {
                    if (n <= 0) continue;
                    var ppo = new PromptPointOptions($"\n{T} Punkt dla prętów {pos} ({n} × {desc}) do BBS <Enter = pomiń>: ")
                        { AllowNone = true };
                    var ppr = ed.GetPoint(ppo);
                    if (ppr.Status == PromptStatus.Cancel) break;
                    if (ppr.Status == PromptStatus.OK) placements.Add((pos, n, ppr.Value));
                }
                if (placements.Count > 0)
                {
                    int prev = PunchingTagEngine.DeleteSummaryBars(doc);
                    if (prev > 0) ed.WriteMessage($"\n{T} Usunięto poprzednie pręty 501/502 ({prev} obiektów).");
                    PlaceSummaryBars(doc, placements, T);
                }

                Application.ShowModalWindow(new PunchingTagResultsDialog(sb.ToString().TrimEnd(), warnings));
                ed.WriteMessage($"\n{T} Gotowe.\n");
            }
            catch (System.Exception ex)
            {
                Log.Error("RC_PUNCHING_AUTO", ex);
                ed.WriteMessage($"\n{T} Błąd: {ex.Message}\n");
            }
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
