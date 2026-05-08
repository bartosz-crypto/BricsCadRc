using System;
using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Auto-generates RC_BAR_BLOCK distributions on a slab from a rebar template library.
    ///
    /// Library workflow (Etap 1B):
    ///   1. User clicks slab polyline (SD-PILED-RAFT layer)
    ///   2. Engine computes required bar length = slab span − 2×cover, snapped down to 250mm grid
    ///   3. Engine scans nearest rebar_bottom/rebar_top rect for matching template (diameter + length)
    ///   4. Match found → reuse; no match → create new RC_SINGLE_BAR template in the rect
    ///   5. Cleanup any previous AutoRebar distributions of same layerCode on this slab
    ///   6. Generate RC_BAR_BLOCK distribution + annotation + bidirectional link
    ///
    /// Chain pattern mirrors RC_PUNCHING_SUMMARY_BARS (PunchingTagCommands.cs).
    /// Etap 1: axis-aligned slabs only.
    /// </summary>
    public static class AutoRebarEngine
    {
        public const double DefaultSpacing       = 200.0;
        public const double DefaultCover         = 40.0;
        public const double TemplateMinLen       = 1250.0;
        public const double TemplateMaxLen       = 6000.0;
        public const double TemplateGridStep     = 250.0;
        public const double TemplateOffsetX      = 200.0;
        public const double TemplateOffsetY      = 500.0;
        public const double TemplateSpacingY     = 700.0;
        public const double TemplateLabelOffsetY = 200.0;
        /// <summary>
        /// Distance from last distribution bar to leader text endpoint (pre-set LeaderPoints).
        /// Text lands at (anchorX, BarsSpan + LeaderArmExtension) in local block coords.
        /// </summary>
        public const double LeaderArmExtension  = 2400.0;

        // ----------------------------------------------------------------
        // Public entry point
        // ----------------------------------------------------------------

        /// <summary>
        /// Generates a distribution for one layer code (B1/B2/T1/T2) on the given slab.
        /// Creates a template in rebar_X if no matching one exists.
        /// Cleans up previous distributions of the same layerCode on this slab first.
        /// </summary>
        /// <returns>1 on success; -1 on validation error or generation failure.</returns>
        public static int GenerateLayer(
            Document doc,
            ObjectId slabPolyId,
            string   sourceLayer,
            string   filterDirection,
            string   layerCode,
            int      diameter = 10,
            double   spacing  = DefaultSpacing,
            double   cover    = DefaultCover)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            // Phase 1 (read-only tx): validate slab, compute plan
            Phase1Result plan;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                plan = BuildPlan(doc, tr, slabPolyId, sourceLayer, filterDirection,
                                 layerCode, diameter, cover);
                tr.Commit();
            }
            if (plan == null) return -1;

            ObjectId templateBarId = ObjectId.Null;
            BarData  templateBar   = null;
            bool     templateReused;

            using (doc.LockDocument())
            {
                // Phase 2a: cleanup old distributions on this slab
                EraseOldDistributions(db, plan.OldDistributionsToErase);

                // Phase 2b: get existing template OR create new
                if (plan.MatchedTemplate.HasValue)
                {
                    templateBarId  = plan.MatchedTemplate.Value.id;
                    templateBar    = plan.MatchedTemplate.Value.bar;
                    templateReused = true;
                    ed.WriteMessage($"\n[AutoRebar] Reusing template {templateBar.Mark} L={plan.SnappedLen}mm\n");
                }
                else
                {
                    (templateBarId, templateBar) = CreateNewTemplate(
                        db, plan.RebarBbox, plan.ExistingTemplateCount,
                        diameter, plan.SnappedLen, layerCode);
                    templateReused = false;
                    ed.WriteMessage($"\n[AutoRebar] Utworzono template {templateBar.Mark} L={plan.SnappedLen}mm w {sourceLayer}\n");
                }

                // Phase 2c: distribution + annotation chain (mirrors RC_PUNCHING_SUMMARY_BARS)
                bool ok = GenerateDistributionWithLeader(
                    db, plan.SlabBbox, cover,
                    templateBarId, templateBar,
                    diameter, plan.SnappedLen, spacing, layerCode, filterDirection);

                if (!ok)
                {
                    ed.WriteMessage("\n[AutoRebar] Generowanie rozkładu nie powiodło się.\n");
                    return -1;
                }
            }

            ed.WriteMessage(
                $"\n[AutoRebar] Wygenerowano rozkład {layerCode}: {templateBar.Mark} " +
                $"L={plan.SnappedLen}mm spacing={spacing}mm " +
                $"({(templateReused ? "reused" : "created")} template).\n");
            return 1;
        }

        // ----------------------------------------------------------------
        // Phase 1 — read-only plan (inside caller's transaction)
        // ----------------------------------------------------------------

        private class Phase1Result
        {
            public Extents3d                             SlabBbox;
            public Extents3d                             RebarBbox;
            public double                                SnappedLen;
            public int                                   ExistingTemplateCount;
            public (ObjectId id, BarData bar)?           MatchedTemplate;   // null if no length match
            public List<(ObjectId distId, ObjectId annotId)> OldDistributionsToErase;
        }

        private static Phase1Result BuildPlan(
            Document doc, Transaction tr,
            ObjectId slabPolyId, string sourceLayer, string filterDirection,
            string layerCode, int diameter, double cover)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            // 1. Validate slab
            if (!(tr.GetObject(slabPolyId, OpenMode.ForRead) is Polyline slabPl))
            {
                ed.WriteMessage("\nWybrana encja nie jest polilinią.\n");
                return null;
            }
            if (!GeometryHelper.IsEffectivelyClosed(slabPl))
            {
                ed.WriteMessage("\nObrys płyty musi być zamknięty.\n");
                return null;
            }
            if (!GeometryHelper.IsAxisAlignedPolyline(slabPl))
            {
                ed.WriteMessage("\n[AutoRebar] Etap 1 nie obsługuje płyt pod kątem. Obróć do osi lub użyj RC_GENERATE_SLAB.\n");
                return null;
            }

            var slabBbox     = GeometryHelper.PolylineBbox(slabPl);
            var slabCentroid = GeometryHelper.Centroid(slabBbox);

            // 2. Compute required bar length and snap to 250mm grid
            bool   horizontal  = filterDirection == "X";
            double slabSpan    = horizontal
                ? slabBbox.MaxPoint.X - slabBbox.MinPoint.X
                : slabBbox.MaxPoint.Y - slabBbox.MinPoint.Y;
            double requiredLen = slabSpan - 2.0 * cover;
            double snappedLen  = GeometryHelper.SnapDownToGrid(
                requiredLen, TemplateGridStep, TemplateMinLen, TemplateMaxLen);
            if (snappedLen < 0)
            {
                ed.WriteMessage(
                    $"\n[AutoRebar] Płyta zbyt mała dla {layerCode}: " +
                    $"requiredLen={requiredLen:F0}mm < min {TemplateMinLen:F0}mm.\n");
                return null;
            }

            // 3. Find nearest rebar_X rect
            var rects = ScanLayerRectangles(db, tr, sourceLayer);
            if (rects.Count == 0)
            {
                ed.WriteMessage($"\n[AutoRebar] Brak polilinii na warstwie '{sourceLayer}'.\n");
                return null;
            }
            var (_, rebarBbox) = FindNearestRect(rects, slabCentroid);

            // 4. Scan existing templates in rebar box (Direction inferred + Diameter match)
            var templates = ScanTemplates(db, tr, rebarBbox, filterDirection, diameter);

            // 5. Match by length (tolerance ±1mm)
            (ObjectId id, BarData bar)? match = null;
            foreach (var (tid, tb) in templates)
            {
                if (Math.Abs(tb.LengthA - snappedLen) < 1.0)
                {
                    match = (tid, tb);
                    break;
                }
            }

            // 6. Scan old distributions on this slab to erase
            var oldDists = ScanOldDistributions(db, tr, slabBbox, layerCode);

            return new Phase1Result
            {
                SlabBbox                 = slabBbox,
                RebarBbox                = rebarBbox,
                SnappedLen               = snappedLen,
                ExistingTemplateCount    = templates.Count,
                MatchedTemplate          = match,
                OldDistributionsToErase  = oldDists,
            };
        }

        // ----------------------------------------------------------------
        // Scanning helpers (Phase 1, inside transaction)
        // ----------------------------------------------------------------

        private static List<(ObjectId, Extents3d)> ScanLayerRectangles(
            Database db, Transaction tr, string layerName)
        {
            var result = new List<(ObjectId, Extents3d)>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(
                bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId oid in ms)
            {
                if (oid.IsErased) continue;
                if (!(tr.GetObject(oid, OpenMode.ForRead) is Polyline pl)) continue;
                if (pl.Layer != layerName || pl.NumberOfVertices < 3) continue;
                result.Add((oid, GeometryHelper.PolylineBbox(pl)));
            }
            return result;
        }

        private static (ObjectId, Extents3d) FindNearestRect(
            List<(ObjectId, Extents3d)> rects, Point3d slabCentroid)
        {
            var best     = rects[0];
            double minD  = GeometryHelper.Centroid(best.Item2).DistanceTo(slabCentroid);
            for (int i = 1; i < rects.Count; i++)
            {
                double d = GeometryHelper.Centroid(rects[i].Item2).DistanceTo(slabCentroid);
                if (d < minD) { minD = d; best = rects[i]; }
            }
            return best;
        }

        private static List<(ObjectId, BarData)> ScanTemplates(
            Database db, Transaction tr, Extents3d rebarBbox,
            string filterDirection, int diameter)
        {
            var result = new List<(ObjectId, BarData)>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(
                bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId oid in ms)
            {
                if (oid.IsErased) continue;
                if (!(tr.GetObject(oid, OpenMode.ForRead) is Polyline pl)) continue;
                var bar = SingleBarEngine.ReadBarXData(pl);
                if (bar == null) continue;
                if (bar.Diameter != diameter) continue;
                var insPt = pl.GetPoint3dAt(0);
                if (!GeometryHelper.IsInsideBbox(insPt, rebarBbox)) continue;
                if (GeometryHelper.InferDirectionFromPolyline(pl) != filterDirection) continue;
                result.Add((oid, bar));
            }
            return result;
        }

        private static List<(ObjectId distId, ObjectId annotId)> ScanOldDistributions(
            Database db, Transaction tr, Extents3d slabBbox, string layerCode)
        {
            var result = new List<(ObjectId, ObjectId)>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(
                bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId oid in ms)
            {
                if (oid.IsErased) continue;
                if (!(tr.GetObject(oid, OpenMode.ForRead) is BlockReference br)) continue;
                var bar = BarBlockEngine.ReadXData(br);
                if (bar == null || bar.LayerCode != layerCode) continue;
                if (!GeometryHelper.IsInsideBbox(br.Position, slabBbox)) continue;

                // Resolve annotation via AnnotHandle
                ObjectId annotId = ObjectId.Null;
                if (!string.IsNullOrEmpty(bar.AnnotHandle))
                {
                    try
                    {
                        long h = Convert.ToInt64(bar.AnnotHandle, 16);
                        if (db.TryGetObjectId(new Handle(h), out ObjectId aid))
                            annotId = aid;
                    }
                    catch { }
                }
                result.Add((oid, annotId));
            }
            return result;
        }

        // ----------------------------------------------------------------
        // Phase 2 helpers (each uses its own transactions)
        // ----------------------------------------------------------------

        private static void EraseOldDistributions(
            Database db, List<(ObjectId distId, ObjectId annotId)> toErase)
        {
            if (toErase.Count == 0) return;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var (distId, annotId) in toErase)
                {
                    try
                    {
                        if (!distId.IsNull && !distId.IsErased)
                        {
                            var ent = tr.GetObject(distId, OpenMode.ForWrite) as Entity;
                            ent?.Erase();
                        }
                        if (!annotId.IsNull && !annotId.IsErased)
                        {
                            var ent = tr.GetObject(annotId, OpenMode.ForWrite) as Entity;
                            ent?.Erase();
                        }
                    }
                    catch { }
                }
                tr.Commit();
            }
        }

        /// <summary>
        /// Creates new RC_SINGLE_BAR template + MLeader label in rebar box (top-down stack).
        /// Mirrors RC_PUNCHING_SUMMARY_BARS chain steps 1–2.
        /// </summary>
        private static (ObjectId barId, BarData elevBar) CreateNewTemplate(
            Database db, Extents3d rebarBbox, int existingCount,
            int diameter, double snappedLen, string layerCode)
        {
            // Allocate posNr (conflict-free)
            var usedNrs = PositionCounter.GetUsedPositionNumbers(db);
            int posNr   = PositionCounter.GetNextFreeFrom(usedNrs, 1);

            // Compute insert point — top-down stack inside rebar_X rect
            double insertX = rebarBbox.MinPoint.X + TemplateOffsetX;
            double insertY = rebarBbox.MaxPoint.Y - TemplateOffsetY
                           - TemplateSpacingY * existingCount;
            var insertPt = new Point3d(insertX, insertY, 0);

            // Build BarData for template — pure prefix Mark, no spacing
            var elevBar = BuildBarData(diameter, posNr, snappedLen, layerCode);
            elevBar.Mark = BarData.FormatMark(diameter, posNr, 0, 1);  // "H10-01"

            // Step 1: place bar polyline (RC_SINGLE_BAR)
            ObjectId barId = SingleBarEngine.PlaceBar(db, elevBar, insertPt);

            // Step 2: label MLeader for the template (issue #1 fix)
            Point3d textPt = new Point3d(
                insertPt.X + snappedLen * 0.5,
                insertPt.Y + TemplateLabelOffsetY,
                0);
            Point3d arrowTip;
            using (var trTip = db.TransactionManager.StartTransaction())
            {
                arrowTip = SingleBarEngine.GetBarArrowTip(barId, elevBar, textPt, trTip);
                trTip.Commit();
            }
            ObjectId labelId = SingleBarEngine.PlaceBarLabel(
                db, arrowTip, textPt, elevBar.Mark, barId);

            // Save labelId in template XData
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

            PositionCounter.CommitUsed(db, posNr);
            return (barId, elevBar);
        }

        /// <summary>
        /// Generates distribution on slab + annotation leader + bidirectional link.
        /// Mirrors RC_PUNCHING_SUMMARY_BARS chain steps 3–5.
        /// </summary>
        private static bool GenerateDistributionWithLeader(
            Database db, Extents3d slabBbox, double cover,
            ObjectId templateBarId, BarData templateBar,
            int diameter, double snappedLen, double spacing,
            string layerCode, string filterDirection)
        {
            // Cover-inset bounds (GenerateFromBounds expects post-cover bounds)
            double x0 = slabBbox.MinPoint.X + cover;
            double y0 = slabBbox.MinPoint.Y + cover;
            double x1 = slabBbox.MaxPoint.X - cover;
            double y1 = slabBbox.MaxPoint.Y - cover;

            bool horizontal = filterDirection == "X";
            int  posNr      = SingleBarEngine.ExtractPosNr(templateBar.Mark);
            if (posNr <= 0) posNr = 1;

            // Step 3: build distBar — Mark with spacing + " {layerCode}" suffix (issue #3 fix)
            var distBar = BuildBarData(diameter, posNr, snappedLen, layerCode);
            string baseMark     = BarData.FormatMark(diameter, posNr, spacing, 2);  // "H10-01-200"
            distBar.Mark        = $"{baseMark} {layerCode}";                         // "H10-01-200 B1"
            distBar.Spacing     = spacing;
            distBar.Direction   = filterDirection;
            distBar.Count       = 0;   // auto-calc by GenerateFromBounds
            distBar.SourceBarHandle = templateBarId.Handle.Value.ToString("X8");     // X8 padded (issue #4 fix)

            // Step 3: distribution block
            var barResult = BarBlockEngine.GenerateFromBounds(
                db, x0, y0, x1, y1, distBar, horizontal, posNr);

            if (!barResult.IsValid) return false;

            // Step 3.5: pre-set leader points — arm from dist center to LeaderArmExtension above last bar.
            // BuildHorizontal/Vertical skip default construction (mid-span + 500mm) when ≥2 pts decoded.
            // Rescale pins pts[0].Y to BarsSpan/2; pts[1].Y is preserved.
            double armEndY  = distBar.BarsSpan + LeaderArmExtension;
            distBar.LeaderPoints = AnnotationEngine.EncodeLeaderPoints(new List<Point3d>
            {
                new Point3d(0, 0,       0),  // start — Y pinned to BarsSpan/2 by rescale
                new Point3d(0, armEndY, 0),  // end   — Y preserved = last bar + 2400mm
            });

            // Step 4: annotation leader
            // leaderHorizontal=false → straight arm parallel to dist line span axis
            // (extension of dist line, not L-shape with sideways text).
            var annotResult = AnnotationEngine.CreateLeader(
                db, barResult, distBar,
                leaderHorizontal: false, posNr: posNr,
                barsHorizontal: horizontal, leaderRight: true, leaderUp: true);

            // Step 5: CRITICAL — bidirectional link dist ↔ annot (issue #2, #5 fix)
            if (annotResult.BlockRefId != ObjectId.Null)
                BarBlockEngine.LinkAnnotation(db, barResult.BlockRefId, annotResult.BlockRefId);

            // Step 6: register posNr (idempotent if already committed from template creation)
            PositionCounter.CommitUsed(db, posNr);

            return true;
        }

        private static BarData BuildBarData(int diameter, int posNr, double lengthA, string layerCode)
        {
            return new BarData
            {
                Diameter   = diameter,
                LengthA    = lengthA,
                ShapeCode  = "00",
                Position   = layerCode.StartsWith("B") ? "BOT" : "TOP",
                LayerCode  = layerCode,
                Direction  = "X",       // overridden in distribution path
                AnnotScale = 1.0,
                Cover      = DefaultCover,
                Count      = 1,
            };
        }
    }
}
