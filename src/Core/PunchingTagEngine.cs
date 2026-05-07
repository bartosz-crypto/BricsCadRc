using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Bricscad.ApplicationServices;
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
            public ObjectId Id;      // ObjectId.Null for source; populated for target
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

        public sealed class AnnotationStats
        {
            public int Tagged;
            public int Skipped;
            public int OrphanIds;
            public int Cleaned;
            public List<MappingWarning> Warnings = new List<MappingWarning>();
        }

        // ----------------------------------------------------------------
        // Private types
        // ----------------------------------------------------------------

        private struct WorkItem
        {
            public string     Ph;
            public PileCircle Circle;
            public Point3d    Anchor;
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
                    List<PileCircle> circles;
                    List<PileIdText> ids;
                    List<PhLabel>    phs;
                    List<ObjectId>   dummy1;
                    List<ObjectId>   dummy2;
                    ScanModelSpaceInternal(db, tr, false,
                        out circles, out ids, out phs, out dummy1, out dummy2);
                    data.Circles  = circles;
                    data.Ids      = ids;
                    data.PhLabels = phs;
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

        public static AnnotationStats AnnotateTarget(Document doc, MappingResult mapping)
        {
            var stats = new AnnotationStats();
            var db    = doc.Database;
            var ed    = doc.Editor;

            // ---- 1. Validation + collection (ForRead) -------------------
            ObjectId         wygStyleId;
            List<PileCircle> circles_tgt;
            List<PileIdText> ids_tgt;
            List<PhLabel>    dummy_phs;
            List<ObjectId>   existing_ph_mtexts;
            List<ObjectId>   existing_ap_hatches;

            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);

                string[] requiredLayers = { "SD-Pile", "AP rebar top", "AP-Hatch" };
                foreach (var ln in requiredLayers)
                {
                    if (!lt.Has(ln))
                        throw new InvalidOperationException(
                            $"[RC_PUNCHING_TAG] target missing layer '{ln}'");
                }

                bool hasPileTextLayer = false;
                foreach (ObjectId lid in lt)
                {
                    if (lid.IsErased) continue;
                    var lr = (LayerTableRecord)tr.GetObject(lid, OpenMode.ForRead);
                    if (lr.Name.StartsWith("SD-Pile Text", StringComparison.OrdinalIgnoreCase))
                    {
                        hasPileTextLayer = true;
                        break;
                    }
                }
                if (!hasPileTextLayer)
                    throw new InvalidOperationException(
                        "[RC_PUNCHING_TAG] target missing layer 'SD-Pile Text*'");

                var tst = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                if (tst.Has("WYG_0MS"))
                {
                    wygStyleId = tst["WYG_0MS"];
                }
                else
                {
                    ed.WriteMessage(
                        "\n[RC_PUNCHING_TAG] Warning: text style 'WYG_0MS' not found, using Standard");
                    wygStyleId = db.Textstyle;
                }

                ScanModelSpaceInternal(db, tr, true,
                    out circles_tgt, out ids_tgt, out dummy_phs,
                    out existing_ph_mtexts, out existing_ap_hatches);

                tr.Commit();
            }

            // ---- 2. Build work list -------------------------------------
            var workList = new List<WorkItem>();

            foreach (var idText in ids_tgt)
            {
                string textNorm = idText.Text.Trim();
                string ph;
                if (!mapping.PileIdToPh.TryGetValue(textNorm, out ph))
                {
                    stats.Skipped++;
                    continue;
                }
                var (idx, dist) = NearestCircleIndex(idText.Position, circles_tgt);
                if (dist > 2000.0)
                {
                    stats.OrphanIds++;
                    stats.Warnings.Add(new MappingWarning {
                        Kind    = "orphan",
                        Message = $"Target pile-id TEXT '{textNorm}' at " +
                                  $"({idText.Position.X:F1}, {idText.Position.Y:F1}) " +
                                  $"too far from any target circle (dist={dist:F1}mm)"
                    });
                    continue;
                }
                workList.Add(new WorkItem {
                    Ph     = ph,
                    Circle = circles_tgt[idx],
                    Anchor = idText.Position
                });
            }

            // ---- 3. Cleanup + write (ForWrite) --------------------------
            using (doc.LockDocument())
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                // Use handle values as keys — ObjectId.Equals() is unstable in BRX
                var erasedHandles = new HashSet<long>();

                foreach (var item in workList)
                {
                    double threshold = 2.0 * item.Circle.Radius;

                    foreach (var oid in existing_ph_mtexts)
                    {
                        if (erasedHandles.Contains(oid.Handle.Value)) continue;
                        var mtE = (MText)tr.GetObject(oid, OpenMode.ForRead);
                        if ((mtE.Location - item.Circle.Center).Length < threshold)
                        {
                            mtE.UpgradeOpen();
                            mtE.Erase();
                            erasedHandles.Add(oid.Handle.Value);
                            stats.Cleaned++;
                        }
                    }

                    foreach (var oid in existing_ap_hatches)
                    {
                        if (erasedHandles.Contains(oid.Handle.Value)) continue;
                        try
                        {
                            var hE  = (Hatch)tr.GetObject(oid, OpenMode.ForRead);
                            var ext = hE.GeometricExtents;
                            var ctr = new Point3d(
                                (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                                (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                                (ext.MinPoint.Z + ext.MaxPoint.Z) / 2.0);
                            if ((ctr - item.Circle.Center).Length < threshold)
                            {
                                hE.UpgradeOpen();
                                hE.Erase();
                                erasedHandles.Add(oid.Handle.Value);
                                stats.Cleaned++;
                            }
                        }
                        catch { /* GeometricExtents can throw on incomplete/transient entities */ }
                    }

                    var mt = new MText();
                    mt.SetDatabaseDefaults(db);
                    mt.Contents    = item.Ph;
                    mt.Layer       = "AP rebar top";
                    mt.ColorIndex  = 4;
                    mt.TextHeight  = 120.0;
                    mt.Attachment  = AttachmentPoint.TopLeft;
                    mt.Location    = new Point3d(item.Anchor.X, item.Anchor.Y - 80.0, item.Anchor.Z);
                    mt.TextStyleId = wygStyleId;
                    ms.AppendEntity(mt);
                    tr.AddNewlyCreatedDBObject(mt, true);

                    var h = new Hatch();
                    h.SetDatabaseDefaults(db);
                    h.Layer        = "AP-Hatch";
                    h.ColorIndex   = 1;   // RED
                    h.SetHatchPattern(HatchPatternType.PreDefined, "ANSI31");
                    h.PatternAngle = 0.0;
                    h.PatternScale = 10.0;
                    h.Associative  = false;
                    ms.AppendEntity(h);
                    tr.AddNewlyCreatedDBObject(h, true);
                    h.AppendLoop(HatchLoopTypes.Outermost,
                                 new ObjectIdCollection { item.Circle.Id });
                    h.EvaluateHatch(true);

                    stats.Tagged++;
                }

                tr.Commit();
            }

            return stats;
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
        // Private helpers
        // ----------------------------------------------------------------

        private static void ScanModelSpaceInternal(
            Database         db,
            Transaction      tr,
            bool             collectExisting,
            out List<PileCircle>  circles,
            out List<PileIdText>  ids,
            out List<PhLabel>     phs,
            out List<ObjectId>    existingMt,
            out List<ObjectId>    existingHatch)
        {
            circles       = new List<PileCircle>();
            ids           = new List<PileIdText>();
            phs           = new List<PhLabel>();
            existingMt    = new List<ObjectId>();
            existingHatch = new List<ObjectId>();

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
                    string.Equals(layer, "SD-Pile", StringComparison.OrdinalIgnoreCase))
                {
                    circles.Add(new PileCircle {
                        Center = circ.Center,
                        Radius = circ.Radius,
                        Id     = collectExisting ? oid : ObjectId.Null
                    });
                }
                else if (ent is DBText txt &&
                         layer.StartsWith("SD-Pile Text", StringComparison.OrdinalIgnoreCase))
                {
                    ids.Add(new PileIdText {
                        Position = txt.Position,
                        Text     = txt.TextString ?? string.Empty
                    });
                }
                else if (ent is MText mt)
                {
                    string mtLayer = mt.Layer ?? string.Empty;
                    if (string.Equals(mtLayer, "LABELS", StringComparison.OrdinalIgnoreCase))
                    {
                        string contents = mt.Contents ?? string.Empty;
                        var m = _phPattern.Match(contents);
                        if (m.Success)
                            phs.Add(new PhLabel {
                                Location = mt.Location,
                                Ph       = "PH" + m.Groups[1].Value
                            });
                    }
                    else if (collectExisting &&
                             string.Equals(mtLayer, "AP rebar top",
                                 StringComparison.OrdinalIgnoreCase))
                    {
                        existingMt.Add(oid);
                    }
                }
                else if (collectExisting &&
                         ent is Hatch &&
                         string.Equals(layer, "AP-Hatch", StringComparison.OrdinalIgnoreCase))
                {
                    existingHatch.Add(oid);
                }
            }
        }

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
                if (d < bestD)        { secondD = bestD; bestD = d; best = i; }
                else if (d < secondD) { secondD = d; }
            }
            return (best, bestD, secondD);
        }
    }
}
