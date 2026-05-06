using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    public static class PunchingTagEngine
    {
        private static readonly Regex _phPattern =
            new Regex(@"^PH([1-9])\\", RegexOptions.Compiled);

        // ----------------------------------------------------------------
        // Public data types
        // ----------------------------------------------------------------

        public sealed class PileCircle
        {
            public Point3d Center;
            public double  Radius;
            public ObjectId Id;      // ObjectId.Null for source; populated for target (etap 2)
        }

        public sealed class PileIdText
        {
            public Point3d Position;
            public string  Text;
        }

        public sealed class PhLabel
        {
            public Point3d Location;
            public string  Ph;       // "PH1".."PH9"
        }

        public sealed class SourceData
        {
            public List<PileCircle>  Circles  = new List<PileCircle>();
            public List<PileIdText>  Ids      = new List<PileIdText>();
            public List<PhLabel>     PhLabels = new List<PhLabel>();
        }

        public sealed class MappingWarning
        {
            public string Kind;     // "orphan" | "conflict" | "close-call"
            public string Message;
        }

        public sealed class MappingResult
        {
            public Dictionary<string, string> PileIdToPh =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public List<MappingWarning> Warnings = new List<MappingWarning>();
            public int TotalCircles;
            public int TotalIds;
            public int TotalPhLabels;
            public int Mapped;
            public int Skipped;
        }

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        public static SourceData ReadSource(string path)
        {
            var data = new SourceData();
            Database db = null;
            try
            {
                db = new Database(false, true);
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".dwg")
                    db.ReadDwgFile(path, System.IO.FileShare.Read, true, null);
                else
                    db.DxfIn(path, null);

                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                    var ms = (BlockTableRecord)tr.GetObject(
                        bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);

                    foreach (ObjectId oid in ms)
                    {
                        if (oid.IsErased) continue;
                        var ent = tr.GetObject(oid, OpenMode.ForRead) as Entity;
                        if (ent == null) continue;

                        string layer = ent.Layer ?? string.Empty;

                        if (ent is Circle circ &&
                            string.Equals(layer, "SD-Pile",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            data.Circles.Add(new PileCircle {
                                Center = circ.Center,
                                Radius = circ.Radius,
                                Id     = ObjectId.Null
                            });
                        }
                        else if (ent is DBText txt &&
                                 layer.StartsWith("SD-Pile Text",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            data.Ids.Add(new PileIdText {
                                Position = txt.Position,
                                Text     = txt.TextString ?? string.Empty
                            });
                        }
                        else if (ent is MText mt &&
                                 string.Equals(layer, "LABELS",
                                     StringComparison.OrdinalIgnoreCase))
                        {
                            string contents = mt.Contents ?? string.Empty;
                            var m = _phPattern.Match(contents);
                            if (m.Success)
                            {
                                data.PhLabels.Add(new PhLabel {
                                    Location = mt.Location,
                                    Ph       = "PH" + m.Groups[1].Value
                                });
                            }
                        }
                    }
                    tr.Commit();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"ReadSource failed for '{path}': {ex.Message}", ex);
            }
            finally
            {
                db?.Dispose();
            }
            return data;
        }

        public static MappingResult BuildMapping(SourceData src)
        {
            var result = new MappingResult
            {
                TotalCircles  = src.Circles.Count,
                TotalIds      = src.Ids.Count,
                TotalPhLabels = src.PhLabels.Count
            };

            if (src.Circles.Count == 0)
                throw new InvalidOperationException("Source missing SD-Pile circles");

            // B1 — pile-id text -> nearest circle index
            var idToCircleIdx = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var idText in src.Ids)
            {
                var (idx, dist) = NearestCircleIndex(idText.Position, src.Circles);
                if (dist > 2000.0)
                {
                    result.Warnings.Add(new MappingWarning {
                        Kind    = "orphan",
                        Message = $"Pile-id TEXT '{idText.Text}' at " +
                                  $"({idText.Position.X:F1}, {idText.Position.Y:F1}) " +
                                  $"too far from any pile (nearest {dist:F1}mm) — skipped"
                    });
                    continue;
                }
                idToCircleIdx[idText.Text.Trim()] = idx;
            }

            // B2 — PH MTEXT -> nearest circle index
            var circleIdxToPh = new Dictionary<int, string>();

            foreach (var phLabel in src.PhLabels)
            {
                var (idx, dist, dist2) = NearestTwoCirclesIndex(
                    phLabel.Location, src.Circles);

                if (dist > 2000.0)
                {
                    result.Warnings.Add(new MappingWarning {
                        Kind    = "orphan",
                        Message = $"PH MTEXT '{phLabel.Ph}' at " +
                                  $"({phLabel.Location.X:F1}, {phLabel.Location.Y:F1}) " +
                                  $"too far from any pile (nearest {dist:F1}mm) — skipped"
                    });
                    continue;
                }

                if (dist2 > 0 && dist > 0 && dist2 / dist < 1.10)
                {
                    result.Warnings.Add(new MappingWarning {
                        Kind    = "close-call",
                        Message = $"PH MTEXT '{phLabel.Ph}' at " +
                                  $"({phLabel.Location.X:F1}, {phLabel.Location.Y:F1}) " +
                                  $"ambiguous: 1st={dist:F1}mm 2nd={dist2:F1}mm"
                    });
                }

                string oldPh;
                if (circleIdxToPh.TryGetValue(idx, out oldPh) && oldPh != phLabel.Ph)
                {
                    var circle = src.Circles[idx];
                    result.Warnings.Add(new MappingWarning {
                        Kind    = "conflict",
                        Message = $"Pile @ ({circle.Center.X:F1}, {circle.Center.Y:F1}) " +
                                  $"PH conflict: {oldPh} overwritten by {phLabel.Ph}"
                    });
                }
                circleIdxToPh[idx] = phLabel.Ph;
            }

            // B3 — compose pileIdToPh
            foreach (var kv in idToCircleIdx)
            {
                string ph;
                if (circleIdxToPh.TryGetValue(kv.Value, out ph))
                    result.PileIdToPh[kv.Key] = ph;
            }

            result.Mapped  = result.PileIdToPh.Count;
            result.Skipped = idToCircleIdx.Count - result.Mapped;
            return result;
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static (int idx, double dist) NearestCircleIndex(
            Point3d p, List<PileCircle> circles)
        {
            int best = -1;
            double bestD = double.MaxValue;
            for (int i = 0; i < circles.Count; i++)
            {
                double d = (circles[i].Center - p).Length;
                if (d < bestD) { bestD = d; best = i; }
            }
            return (best, bestD);
        }

        private static (int idx, double dist, double dist2) NearestTwoCirclesIndex(
            Point3d p, List<PileCircle> circles)
        {
            int best = -1;
            double bestD = double.MaxValue;
            double secondD = double.MaxValue;
            for (int i = 0; i < circles.Count; i++)
            {
                double d = (circles[i].Center - p).Length;
                if (d < bestD)      { secondD = bestD; bestD = d; best = i; }
                else if (d < secondD) { secondD = d; }
            }
            return (best, bestD, secondD);
        }
    }
}
