using System;
using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Microsoft.Win32;
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

                // Pop-up with summary + full warnings list (only when warnings exist)
                var allWarnings = new List<string>();
                foreach (var w in dlg.Mapping.Warnings)
                    allWarnings.Add($"[{w.Kind}] {w.Message}");
                foreach (var w in stats.Warnings)
                    allWarnings.Add($"[{w.Kind}] {w.Message}");
                foreach (var w in applicable.Warnings)
                    allWarnings.Add($"[{w.Kind}] {w.Message}");

                if (allWarnings.Count > 0)
                {
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
            }
            catch (System.Exception ex)
            {
                ed.WriteMessage($"\n[RC_PUNCHING_TAG] EXCEPTION: {ex.Message}");
            }
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
