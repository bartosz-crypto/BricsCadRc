using System;
using System.Collections.Generic;
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
        [CommandMethod("RC_PUNCHING_TAG")]
        public static void RcPunchingTag()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;

            var dlg = new PunchingTagDialog();
            if (Application.ShowModalWindow(dlg) != true) return;

            try
            {
                ed.WriteMessage($"\n[RC_PUNCHING_TAG] Source: " +
                    $"{dlg.Source.Circles.Count} piles, " +
                    $"{dlg.Source.PhLabels.Count} PH labels. " +
                    $"Mapped: {dlg.Mapping.Mapped}.");

                var stats = PunchingTagEngine.AnnotateTarget(doc, dlg.Mapping);

                ed.WriteMessage($"\n[RC_PUNCHING_TAG] Tagged {stats.Tagged} piles. " +
                                $"Skipped {stats.Skipped} (no PH match). " +
                                $"OrphanIds {stats.OrphanIds}. " +
                                $"Cleaned {stats.Cleaned} existing entities. " +
                                $"Mapping warnings: {dlg.Mapping.Warnings.Count}. " +
                                $"Annotation warnings: {stats.Warnings.Count}.");

                var applicable = PunchingTagEngine.UpdateApplicablePiles(doc, dlg.Mapping);
                ed.WriteMessage($"\n[RC_PUNCHING_TAG] AP-TEXT updated: " +
                                $"{applicable.Updated} MTEXTs, " +
                                $"{applicable.Skipped} skipped (no anchor), " +
                                $"{applicable.NoPhDetected} skipped (no PH detected). " +
                                $"Warnings: {applicable.Warnings.Count}.");

                int cap = 30;
                int shown = 0;
                foreach (var w in dlg.Mapping.Warnings)
                {
                    if (shown++ >= cap) break;
                    ed.WriteMessage($"\n  [{w.Kind}] {w.Message}");
                }
                foreach (var w in stats.Warnings)
                {
                    if (shown++ >= cap) break;
                    ed.WriteMessage($"\n  [{w.Kind}] {w.Message}");
                }
                foreach (var w in applicable.Warnings)
                {
                    if (shown++ >= cap) break;
                    ed.WriteMessage($"\n  [{w.Kind}] {w.Message}");
                }
                int total = dlg.Mapping.Warnings.Count + stats.Warnings.Count
                          + applicable.Warnings.Count;
                if (total > cap)
                    ed.WriteMessage($"\n  ... and {total - cap} more warnings");

                // Pop-up with summary + full warnings list (always shown)
                var allWarnings = new List<string>();
                foreach (var w in dlg.Mapping.Warnings)
                    allWarnings.Add($"[{w.Kind}] {w.Message}");
                foreach (var w in stats.Warnings)
                    allWarnings.Add($"[{w.Kind}] {w.Message}");
                foreach (var w in applicable.Warnings)
                    allWarnings.Add($"[{w.Kind}] {w.Message}");

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Source: {dlg.Source.Circles.Count} piles, " +
                              $"{dlg.Source.PhLabels.Count} PH labels.");
                sb.AppendLine($"Mapped: {dlg.Mapping.Mapped} piles. " +
                              $"Mapping warnings: {dlg.Mapping.Warnings.Count}.");
                sb.AppendLine();
                sb.AppendLine($"Tagged: {stats.Tagged} piles.");
                sb.AppendLine($"Skipped: {stats.Skipped} (no PH match).");
                sb.AppendLine($"Orphan pile-ids: {stats.OrphanIds}.");
                sb.AppendLine($"Cleaned: {stats.Cleaned} pre-existing entities.");
                sb.AppendLine($"Annotation warnings: {stats.Warnings.Count}.");
                sb.AppendLine();
                sb.AppendLine($"AP-TEXT updated: {applicable.Updated} MTEXTs.");
                sb.AppendLine($"AP-TEXT skipped (no anchor): {applicable.Skipped}.");
                sb.AppendLine($"AP-TEXT skipped (no PH): {applicable.NoPhDetected}.");
                sb.AppendLine($"AP-TEXT warnings: {applicable.Warnings.Count}.");

                var resultsDlg = new PunchingTagResultsDialog(
                    sb.ToString().TrimEnd(), allWarnings);
                Application.ShowModalWindow(resultsDlg);
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[RC_PUNCHING_TAG] EXCEPTION: {ex.Message}");
            }
        }

        [CommandMethod("RC_PUNCHING_SUMMARY_BARS")]
        public static void RcPunchingSummaryBars()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc.Editor;
            var db  = doc.Database;

            try
            {
                int prevErased = PunchingTagEngine.DeleteSummaryBars(doc);
                if (prevErased > 0)
                    ed.WriteMessage(
                        $"\n[RC_PUNCHING_SUMMARY_BARS] Removed {prevErased} entities from previous run.");

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
                        "AP-TEXT MTEXTs — run RC_PUNCHING_TAG first.");
                    return;
                }

                var dlg = new PunchingSummaryDialog(totals);
                if (Application.ShowModalWindow(dlg) != true) return;

                // Collect placement points outside document lock
                var placements = new List<(int posNr, int realCount, Point3d insertPt)>();

                if (dlg.Wants501)
                {
                    var ptRes = ed.GetPoint(
                        "\nClick placement point for Poz. 501 (H12 × 2250mm): ");
                    if (ptRes.Status != PromptStatus.OK) return;
                    placements.Add((501, dlg.Count501, ptRes.Value));
                }

                if (dlg.Wants502)
                {
                    var ptRes = ed.GetPoint(
                        "\nClick placement point for Poz. 502 (H16 × 2500mm): ");
                    if (ptRes.Status != PromptStatus.OK) return;
                    placements.Add((502, dlg.Count502, ptRes.Value));
                }

                using (doc.LockDocument())
                {
                    foreach (var (posNr, realCount, insertPt) in placements)
                    {
                        int    dia     = posNr == 501 ? 12 : 16;
                        double lenA    = posNr == 501 ? 2250.0 : 2500.0;
                        const  string layerCode = "T1";

                        // 1) RC_BAR — elevation polyline
                        var elevBar = BuildSummaryBarData(dia, posNr, lenA, layerCode);
                        elevBar.Mark = BarData.FormatMark(dia, posNr, 0, 1);  // "H12-501"
                        ObjectId barId = SingleBarEngine.PlaceBar(db, elevBar, insertPt);

                        // 2) Elevation label: arrow at bar body, text above-right
                        Point3d textPt = new Point3d(
                            insertPt.X + lenA * 0.5,
                            insertPt.Y + 200.0, 0);
                        Point3d arrowTip;
                        using (var trTip = db.TransactionManager.StartTransaction())
                        {
                            arrowTip = SingleBarEngine.GetBarArrowTip(barId, elevBar, textPt, trTip);
                            trTip.Commit();
                        }
                        ObjectId labelId = SingleBarEngine.PlaceBarLabel(
                            db, arrowTip, textPt, elevBar.Mark, barId);
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

                        // 3) RC_DISTRIBUTION — 1 bar drawn visually, realCount in XData
                        var distBar = BuildSummaryBarData(dia, posNr, lenA, layerCode);
                        distBar.Mark            = BarData.FormatMark(dia, posNr, 1000.0, 2); // "H12-501-1000"
                        distBar.Spacing         = 1000.0;
                        distBar.Direction       = "X";
                        distBar.Count           = 0;   // auto-calc → 1 (span 100 < spacing 1000)
                        distBar.SourceBarHandle = barId.Handle.Value.ToString("X8");

                        // Place 1000mm below RC_BAR; span=100mm gives count=1
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
                            ed.WriteMessage(
                                $"\n[RC_PUNCHING_SUMMARY_BARS] Failed to generate " +
                                $"distribution for poz. {posNr} — skipping.");
                            continue;
                        }

                        // Store real total in CountDisplay (slot [27]).
                        // Count (slot [3]) stays at 1 (auto-set by GenerateFromBounds).
                        // Rebuild paths read Count=1 -> single bar preserved visually.
                        // RC_SCHEDULE reads EffectiveCount = CountDisplay ?? Count = realCount.
                        using (var trCount = db.TransactionManager.StartTransaction())
                        {
                            var brCount = trCount.GetObject(
                                barResult.BlockRefId, OpenMode.ForWrite) as BlockReference;
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
                        distBar.CountDisplay = realCount;  // label shows real total
                        // distBar.Count = 1 (set by GenerateFromBounds auto-calc)
                        // distBar.BarsSpan = 100mm (set by GenerateFromBounds)

                        var annotResult = AnnotationEngine.CreateLeader(
                            db, barResult, distBar,
                            leaderHorizontal: true, posNr: posNr,
                            barsHorizontal: true,
                            leaderRight: true, leaderUp: true);

                        if (annotResult.BlockRefId != ObjectId.Null)
                            BarBlockEngine.LinkAnnotation(
                                db, barResult.BlockRefId, annotResult.BlockRefId);

                        // 5) Reserve position number
                        PositionCounter.CommitUsed(db, posNr);

                        ed.WriteMessage(
                            $"\n[RC_PUNCHING_SUMMARY_BARS] Poz. {posNr}: " +
                            $"{distBar.Mark} × {realCount}");
                    }
                }

                ed.WriteMessage(
                    $"\n[RC_PUNCHING_SUMMARY_BARS] Done — " +
                    $"{placements.Count} position(s) created.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage(
                    $"\n[RC_PUNCHING_SUMMARY_BARS] EXCEPTION: {ex.Message}");
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

        [CommandMethod("RC_PUNCHING_TAG_DEBUG")]
        public static void RcPunchingTagDebug()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var ed = doc.Editor;

            var dlg = new OpenFileDialog
            {
                Title  = "Select source punching analysis file",
                Filter = "CAD Files|*.dwg;*.dxf|DWG (*.dwg)|*.dwg|DXF (*.dxf)|*.dxf"
            };
            if (dlg.ShowDialog() != true) return;
            string path = dlg.FileName;

            try
            {
                ed.WriteMessage($"\n[RC_PUNCHING_TAG_DEBUG] Reading source: {path}");
                var src = PunchingTagEngine.ReadSource(path);
                ed.WriteMessage(
                    $"\n[RC_PUNCHING_TAG_DEBUG] Source: {src.Circles.Count} circles, " +
                    $"{src.Ids.Count} pile-id texts, {src.PhLabels.Count} PH labels");

                var map = PunchingTagEngine.BuildMapping(src);
                ed.WriteMessage(
                    $"\n[RC_PUNCHING_TAG_DEBUG] Mapping: {map.Mapped} mapped, " +
                    $"{map.Skipped} unmapped piles, {map.Warnings.Count} warnings");

                // PH distribution histogram
                var hist = new SortedDictionary<string, int>();
                foreach (var kv in map.PileIdToPh)
                {
                    int cnt;
                    hist.TryGetValue(kv.Value, out cnt);
                    hist[kv.Value] = cnt + 1;
                }
                ed.WriteMessage("\n  PH distribution:");
                foreach (var kv in hist)
                    ed.WriteMessage($"\n    {kv.Key}: {kv.Value}");

                // First 10 mappings (sample)
                ed.WriteMessage("\n  First 10 mappings:");
                int n = 0;
                foreach (var kv in map.PileIdToPh)
                {
                    if (n++ >= 10) break;
                    ed.WriteMessage($"\n    {kv.Key} -> {kv.Value}");
                }

                // Warnings (cap at 20 to avoid flooding)
                if (map.Warnings.Count > 0)
                {
                    ed.WriteMessage($"\n  Warnings ({map.Warnings.Count}):");
                    int w = 0;
                    foreach (var warn in map.Warnings)
                    {
                        if (w++ >= 20)
                        {
                            ed.WriteMessage(
                                $"\n    ... and {map.Warnings.Count - 20} more");
                            break;
                        }
                        ed.WriteMessage($"\n    [{warn.Kind}] {warn.Message}");
                    }
                }

                ed.WriteMessage("\n[RC_PUNCHING_TAG_DEBUG] Done.");
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[RC_PUNCHING_TAG_DEBUG] EXCEPTION: {ex.Message}");
            }
        }
    }
}
