using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>Minimum overlap between adjacent distributions (hard).</summary>
        public const double OverlapMin    = 400.0;

        /// <summary>Target overlap (algorithm aims for this value).</summary>
        public const double OverlapTarget = 500.0;

        /// <summary>Maximum overlap (hard).</summary>
        public const double OverlapMax    = 650.0;

        /// <summary>
        /// UB template params per slab thickness.
        /// Shape "21" U-bar: A (left leg), B (bottom width), C (right leg).
        /// Plan-view bar length = LengthA (longest leg).
        /// </summary>
        public const int    UBDiameter    = 12;

        public const double UB_225_LengthA = 700.0;
        public const double UB_225_LengthB = 140.0;
        public const double UB_225_LengthC = 700.0;

        public const double UB_300_LengthA = 665.0;
        public const double UB_300_LengthB = 215.0;
        public const double UB_300_LengthC = 665.0;

        /// <summary>UB posNr is ALWAYS 01 for B1 (per user spec).</summary>
        public const int    UBPosNrB1     = 1;

        /// <summary>UB Mark suffix — distinct from straight bar suffix " B1".</summary>
        public const string UBSuffix      = "UB";

        /// <summary>Maximum allowed distance from last bar to slab edge (inclusive of cover).</summary>
        public const double MaxLastBarDistanceFromEdge = 70.0;

        /// <summary>Hard lower bound for adjusted spacing (below this -> reject adjustment).</summary>
        public const double MinAdjustedSpacing = 192.0;

        /// <summary>Soft lower bound for adjusted spacing (below 194, above 192 -> accept with warning).</summary>
        public const double SoftMinAdjustedSpacing = 194.0;

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

            bool horizontal = filterDirection == "X";

            // Strip decomposition (Etap 1E)
            var strips = DecomposeIntoYStrips(plan.SlabVertices, plan.SlabBbox);
            int validStrips   = strips.Count(s => s.Valid);
            int skippedStrips = strips.Count - validStrips;

            ed.WriteMessage($"\n[AutoRebar] Strips: {strips.Count} total, " +
                            $"{validStrips} valid, {skippedStrips} skipped.\n");

            foreach (var s in strips.Where(s => !s.Valid))
                ed.WriteMessage($"\n[AutoRebar] Skip strip Y={s.YLow:F0}..{s.YHigh:F0}: {s.SkipReason}\n");

            if (validStrips == 0)
            {
                ed.WriteMessage($"\n[AutoRebar] No valid strips — abort.\n");
                return -1;
            }

            int generated = 0;
            using (doc.LockDocument())
            {
                // Phase 2a: cleanup old distributions on this slab
                EraseOldDistributions(db, plan.OldDistributionsToErase);

                foreach (var strip in strips.Where(s => s.Valid))
                {
                    double stripHeight = strip.YHigh - strip.YLow;

                    // Thin strip skip
                    if (stripHeight < 50.0)
                    {
                        ed.WriteMessage($"\n*** WARNING *** [AutoRebar] Strip " +
                            $"Y={strip.YLow:F0}..{strip.YHigh:F0} h={stripHeight:F0}mm < 50mm — skipped\n");
                        continue;
                    }

                    // First/last bar offsets per Q7=B
                    double lowerOffset = strip.LowerIsExternal ? cover : spacing / 2.0;
                    double upperOffset = strip.UpperIsExternal ? cover : spacing / 2.0;
                    double y0 = strip.YLow  + lowerOffset;
                    double y1 = strip.YHigh - upperOffset;

                    // Try-fit single bar for short strips
                    bool singleBarMode = stripHeight < spacing || (y1 - y0) <= 0;
                    if (singleBarMode)
                    {
                        double yCenter = (strip.YLow + strip.YHigh) * 0.5;
                        y0 = yCenter;
                        y1 = yCenter;
                        ed.WriteMessage($"\n*** WARNING *** [AutoRebar] Strip " +
                            $"Y={strip.YLow:F0}..{strip.YHigh:F0} h={stripHeight:F0}mm < spacing " +
                            $"— 1 bar centered at Y={yCenter:F0}\n");
                    }

                    bool applyAdjustment = !singleBarMode && strip.UpperIsExternal;

                    // X multi-dist plan for this strip
                    double xAvailable = (strip.XHigh - strip.XLow) - 2.0 * cover;
                    var distPlan = ComputeDistributionPlan(xAvailable, spacing);
                    if (distPlan.Count == 0)
                    {
                        ed.WriteMessage($"\n[AutoRebar] Strip Y={strip.YLow:F0}..{strip.YHigh:F0}: " +
                            $"brak rozwiązania dist plan (xAvailable={xAvailable:F0}mm) — skipped\n");
                        continue;
                    }

                    ed.WriteMessage($"\n[AutoRebar] Strip Y={strip.YLow:F0}..{strip.YHigh:F0} " +
                        $"(h={stripHeight:F0}mm, external: lower={strip.LowerIsExternal}, " +
                        $"upper={strip.UpperIsExternal}): {distPlan.Count} dist, " +
                        $"lengths: " + string.Join(",", distPlan.Select(d => $"{d.length:F0}")) + "\n");

                    foreach (var (xOffset, length) in distPlan)
                    {
                        ObjectId templateBarId = ObjectId.Null;
                        BarData  templateBar   = null;
                        bool     templateReused = false;

                        using (var trMatch = db.TransactionManager.StartTransaction())
                        {
                            var freshTemplates = ScanTemplates(db, trMatch, plan.RebarBbox,
                                                               filterDirection, diameter);
                            foreach (var (tid, tb) in freshTemplates)
                            {
                                if (Math.Abs(tb.LengthA - length) < 1.0)
                                {
                                    templateBarId  = tid;
                                    templateBar    = tb;
                                    templateReused = true;
                                    break;
                                }
                            }
                            trMatch.Commit();
                        }

                        if (!templateReused)
                        {
                            int existingCount;
                            using (var trCount = db.TransactionManager.StartTransaction())
                            {
                                var freshTemplates = ScanTemplates(db, trCount, plan.RebarBbox,
                                                                   filterDirection, diameter);
                                existingCount = freshTemplates.Count;
                                trCount.Commit();
                            }
                            (templateBarId, templateBar) = CreateNewTemplate(
                                db, plan.RebarBbox, existingCount, diameter, length, layerCode);
                            ed.WriteMessage($"\n[AutoRebar] Utworzono template {templateBar.Mark} " +
                                            $"L={length:F0}mm\n");
                        }
                        else
                        {
                            ed.WriteMessage($"\n[AutoRebar] Reusing template {templateBar.Mark} " +
                                            $"L={length:F0}mm\n");
                        }

                        double x0 = strip.XLow + cover + xOffset;
                        double x1 = x0 + length;

                        bool ok = GenerateDistributionWithLeaderAtOffset(
                            db, x0, y0, x1, y1,
                            templateBarId, templateBar,
                            diameter, length, spacing, layerCode, filterDirection,
                            lowerOffset, stripHeight, applyAdjustment,
                            plan.SlabBbox.MinPoint.Y,
                            plan.SlabBbox.MaxPoint.Y);

                        if (ok) generated++;
                    }
                }
            }

            ed.WriteMessage(
                $"\n[AutoRebar] Wygenerowano {generated} rozkładów {layerCode}.\n");
            return generated;
        }

        /// <summary>
        /// Generate UB (U-bar shape 21) distributions on slab edges.
        /// Creates 2 distributions (left + right slab edges), both using same template
        /// (Mark "H12-01-200 UB"). SymbolSide differs per dist (Left vs Right) so
        /// circle markers appear only on outer ends (at slab edges).
        ///
        /// Per user spec (Q1-Q9):
        /// - posNr=01 ALWAYS for UB B1 (with conflict warning if already used)
        /// - Diameter H12 for both 225 and 300 slabs
        /// - Bar length in plan = LengthA (longest leg projection)
        /// - SymbolSide=Left for left UB, Right for right UB
        /// - Spacing 200mm with auto-adjustment (per ComputeAdjustedSpacing)
        /// </summary>
        public static int GenerateUBLayer(
            Document doc,
            ObjectId slabPolyId,
            string   sourceLayer,    // "rebar_bottom"
            string   layerCode,      // "B1"
            int      slabThickness,  // 225 or 300
            double   spacing = DefaultSpacing,
            double   cover   = DefaultCover)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            // Pick UB params per thickness
            double ubLengthA, ubLengthB, ubLengthC;
            if (slabThickness == 225)
            {
                ubLengthA = UB_225_LengthA;
                ubLengthB = UB_225_LengthB;
                ubLengthC = UB_225_LengthC;
            }
            else if (slabThickness == 300)
            {
                ubLengthA = UB_300_LengthA;
                ubLengthB = UB_300_LengthB;
                ubLengthC = UB_300_LengthC;
            }
            else
            {
                ed.WriteMessage($"\n[AutoRebar UB] Nieobsługiwana grubość: {slabThickness}mm (225 lub 300).\n");
                return -1;
            }

            // Check posNr 01 conflict
            var usedNrs = PositionCounter.GetUsedPositionNumbers(db);
            if (usedNrs.Contains(UBPosNrB1))
            {
                bool sameUB = IsExistingPosNr01UB(db, UBDiameter);
                if (!sameUB)
                {
                    var dlgResult = System.Windows.MessageBox.Show(
                        $"PosNr 01 jest już używany przez inny pręt (nie UB H{UBDiameter}).\n" +
                        "AutoRebar UB ZAWSZE używa posNr=01. Kontynuować?\n\n" +
                        "Tak = wymuś posNr=01 (może spowodować konflikt w schedule)\n" +
                        "Nie = anuluj operację",
                        "AutoRebar UB - Konflikt PosNr",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning);
                    if (dlgResult != System.Windows.MessageBoxResult.Yes)
                    {
                        ed.WriteMessage("\n[AutoRebar UB] Anulowane przez użytkownika.\n");
                        return -1;
                    }
                }
            }

            // Phase 1 (read-only tx): validate, scan, plan
            Extents3d slabBbox;
            Extents3d rebarBbox;
            List<(ObjectId distId, ObjectId annotId)> oldUBs;
            (ObjectId, BarData)? matchedTemplate;
            int existingUBTemplateCount;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (!(tr.GetObject(slabPolyId, OpenMode.ForRead) is Polyline slabPl))
                {
                    ed.WriteMessage("\nWybrana encja nie jest polilinią.\n");
                    tr.Commit();
                    return -1;
                }
                if (!GeometryHelper.IsEffectivelyClosed(slabPl))
                {
                    ed.WriteMessage("\nObrys płyty musi być zamknięty.\n");
                    tr.Commit();
                    return -1;
                }
                if (slabPl.Layer != "RC-TEMP-OUTLINE" && !GeometryHelper.IsAxisAlignedPolyline(slabPl))
                {
                    ed.WriteMessage("\n[AutoRebar UB] Etap 1 nie obsługuje płyt pod kątem (NIE drawn).\n");
                    tr.Commit();
                    return -1;
                }

                slabBbox = GeometryHelper.PolylineBbox(slabPl);
                var slabCentroid = GeometryHelper.Centroid(slabBbox);

                var rects = ScanLayerRectangles(db, tr, sourceLayer);
                if (rects.Count == 0)
                {
                    ed.WriteMessage($"\nBrak prostokątów na warstwie '{sourceLayer}'.\n");
                    tr.Commit();
                    return -1;
                }
                (_, rebarBbox) = FindNearestRect(rects, slabCentroid);

                var ubTemplates = ScanUBTemplates(db, tr, rebarBbox, UBDiameter);
                existingUBTemplateCount = ubTemplates.Count;

                matchedTemplate = null;
                foreach (var (tid, tb) in ubTemplates)
                {
                    if (Math.Abs(tb.LengthA - ubLengthA) < 1.0 &&
                        Math.Abs(tb.LengthB - ubLengthB) < 1.0 &&
                        Math.Abs(tb.LengthC - ubLengthC) < 1.0)
                    {
                        matchedTemplate = (tid, tb);
                        break;
                    }
                }

                oldUBs = ScanOldUBDistributions(db, tr, slabBbox, layerCode);
                tr.Commit();
            }

            int generated = 0;
            using (doc.LockDocument())
            {
                EraseOldDistributions(db, oldUBs);

                ObjectId templateBarId;
                BarData  templateBar;
                if (matchedTemplate.HasValue)
                {
                    templateBarId = matchedTemplate.Value.Item1;
                    templateBar   = matchedTemplate.Value.Item2;
                    ed.WriteMessage($"\n[AutoRebar UB] Reusing UB template H{UBDiameter}-01 " +
                        $"(A={ubLengthA}, B={ubLengthB}, C={ubLengthC})\n");
                }
                else
                {
                    (templateBarId, templateBar) = CreateUBTemplate(
                        db, rebarBbox, existingUBTemplateCount,
                        UBDiameter, ubLengthA, ubLengthB, ubLengthC, layerCode);
                    ed.WriteMessage($"\n[AutoRebar UB] Created UB template H{UBDiameter}-01 " +
                        $"(A={ubLengthA}, B={ubLengthB}, C={ubLengthC})\n");
                }

                // Left UB: x0 = slabMinX+cover, x1 = x0+LengthA, SymbolSide="Left"
                bool ok = GenerateUBDistribution(
                    db, slabBbox, cover, templateBarId, templateBar,
                    ubLengthA, ubLengthB, ubLengthC, spacing, layerCode,
                    isLeftSide: true,
                    slabBbox.MinPoint.Y, slabBbox.MaxPoint.Y);
                if (ok) generated++;

                // Right UB: x1 = slabMaxX-cover, x0 = x1-LengthA, SymbolSide="Right"
                ok = GenerateUBDistribution(
                    db, slabBbox, cover, templateBarId, templateBar,
                    ubLengthA, ubLengthB, ubLengthC, spacing, layerCode,
                    isLeftSide: false,
                    slabBbox.MinPoint.Y, slabBbox.MaxPoint.Y);
                if (ok) generated++;
            }

            ed.WriteMessage(
                $"\n[AutoRebar UB] Wygenerowano {generated} z 2 rozkładów UB B1 (grubość {slabThickness}mm).\n");
            return generated;
        }

        // ----------------------------------------------------------------
        // Phase 1 — read-only plan (inside caller's transaction)
        // ----------------------------------------------------------------

        private class YStrip
        {
            public double YLow;
            public double YHigh;
            public double XLow;
            public double XHigh;
            public bool   LowerIsExternal;
            public bool   UpperIsExternal;
            public bool   Valid;
            public string SkipReason;
        }

        private class Phase1Result
        {
            public Extents3d                             SlabBbox;
            public Extents3d                             RebarBbox;
            public double                                SnappedLen;
            public int                                   ExistingTemplateCount;
            public (ObjectId id, BarData bar)?           MatchedTemplate;   // null if no length match
            public List<(ObjectId distId, ObjectId annotId)> OldDistributionsToErase;
            public List<Point2d>                         SlabVertices;
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
            // Note: axis-aligned check skipped for drawn polylines on RC-TEMP-OUTLINE layer.
            // Drawn polylines may be non-axis-aligned; engine uses bbox semantics in either case.
            if (slabPl.Layer != "RC-TEMP-OUTLINE" && !GeometryHelper.IsAxisAlignedPolyline(slabPl))
            {
                ed.WriteMessage("\n[AutoRebar] Etap 1 nie obsługuje płyt pod kątem (NIE drawn).\n");
                return null;
            }

            var slabBbox     = GeometryHelper.PolylineBbox(slabPl);
            var slabVertices = GeometryHelper.GetPolylineVertices(slabPl);
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
                SlabVertices             = slabVertices,
            };
        }

        // ----------------------------------------------------------------
        // Strip decomposition helpers
        // ----------------------------------------------------------------

        private static bool XIntersectionsEqual(List<double> a, List<double> b, double eps = 0.1)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (Math.Abs(a[i] - b[i]) > eps) return false;
            return true;
        }

        private static List<YStrip> DecomposeIntoYStrips(List<Point2d> vertices, Extents3d slabBbox)
        {
            var strips = new List<YStrip>();

            // Phase A: unique Y-coords sorted + dedup epsilon 1e-3
            var rawYs = new List<double>();
            foreach (var v in vertices)
                rawYs.Add(v.Y);
            rawYs.Sort();

            var yCoords = new List<double>();
            foreach (double y in rawYs)
            {
                if (yCoords.Count == 0 || Math.Abs(y - yCoords[yCoords.Count - 1]) >= 1e-3)
                    yCoords.Add(y);
            }

            if (yCoords.Count < 2) return strips;

            // Phase B: filter phantom vertices (compare ray-cast y-0.5 vs y+0.5)
            // Keep first + last always; check inner ones
            var filteredYs = new List<double> { yCoords[0] };
            for (int i = 1; i < yCoords.Count - 1; i++)
            {
                double yc = yCoords[i];
                var xsBelow = GeometryHelper.FindIntersectionsH(vertices, yc - 0.5);
                var xsAbove = GeometryHelper.FindIntersectionsH(vertices, yc + 0.5);
                if (!XIntersectionsEqual(xsBelow, xsAbove))
                    filteredYs.Add(yc);
            }
            filteredYs.Add(yCoords[yCoords.Count - 1]);

            // Phase C: build strips per Y-pair
            double slabMinY = slabBbox.MinPoint.Y;
            double slabMaxY = slabBbox.MaxPoint.Y;

            for (int i = 0; i < filteredYs.Count - 1; i++)
            {
                double yLow  = filteredYs[i];
                double yHigh = filteredYs[i + 1];

                double yMid = (yLow + yHigh) * 0.5 + 0.1;
                var xs = GeometryHelper.FindIntersectionsH(vertices, yMid);

                if (xs.Count != 2)
                {
                    string reason = xs.Count > 2
                        ? $"Multi-span detected ({xs.Count / 2} intervals), Etap 1G future"
                        : $"Degenerate strip ({xs.Count} intersections at mid-Y={yMid:F1})";
                    strips.Add(new YStrip
                    {
                        YLow = yLow, YHigh = yHigh,
                        Valid = false, SkipReason = reason
                    });
                    continue;
                }

                strips.Add(new YStrip
                {
                    YLow            = yLow,
                    YHigh           = yHigh,
                    XLow            = xs[0],
                    XHigh           = xs[1],
                    LowerIsExternal = Math.Abs(yLow  - slabMinY) < 1e-3,
                    UpperIsExternal = Math.Abs(yHigh - slabMaxY) < 1e-3,
                    Valid           = true,
                });
            }

            return strips;
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
                // Filter by Mark suffix to exclude UB and future variants from straight-bar cleanup.
                // UB Marks end with " UB", B1 Marks end with " B1" — symmetric with ScanOldUBDistributions.
                if (string.IsNullOrEmpty(bar.Mark)) continue;
                if (!bar.Mark.EndsWith($" {layerCode}")) continue;
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

        /// <summary>Check if existing posNr=01 entity is UB H{diameter} (safe reuse) or different type (conflict).</summary>
        private static bool IsExistingPosNr01UB(Database db, int diameter)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
                foreach (ObjectId oid in ms)
                {
                    if (oid.IsErased) continue;
                    if (!(tr.GetObject(oid, OpenMode.ForRead) is Polyline pl)) continue;
                    var bar = SingleBarEngine.ReadBarXData(pl);
                    if (bar != null && bar.Diameter == diameter
                        && bar.ShapeCode == "21"
                        && SingleBarEngine.ExtractPosNr(bar.Mark) == 1)
                    {
                        tr.Commit();
                        return true;
                    }
                }
                tr.Commit();
            }
            return false;
        }

        /// <summary>Scan UB templates (shape "21") in rebar box.</summary>
        private static List<(ObjectId, BarData)> ScanUBTemplates(
            Database db, Transaction tr, Extents3d rebarBbox, int diameter)
        {
            var result = new List<(ObjectId, BarData)>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId oid in ms)
            {
                if (oid.IsErased) continue;
                if (!(tr.GetObject(oid, OpenMode.ForRead) is Polyline pl)) continue;
                var bar = SingleBarEngine.ReadBarXData(pl);
                if (bar == null) continue;
                if (bar.ShapeCode != "21") continue;
                if (bar.Diameter != diameter) continue;
                var insPt = pl.GetPoint3dAt(0);
                if (!GeometryHelper.IsInsideBbox(insPt, rebarBbox)) continue;
                result.Add((oid, bar));
            }
            return result;
        }

        /// <summary>Scan old UB distributions on slab (identified by Mark suffix " UB").</summary>
        private static List<(ObjectId, ObjectId)> ScanOldUBDistributions(
            Database db, Transaction tr, Extents3d slabBbox, string layerCode)
        {
            var result = new List<(ObjectId, ObjectId)>();
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var ms = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForRead);
            foreach (ObjectId oid in ms)
            {
                if (oid.IsErased) continue;
                if (!(tr.GetObject(oid, OpenMode.ForRead) is BlockReference br)) continue;
                var bar = BarBlockEngine.ReadXData(br);
                if (bar == null || bar.LayerCode != layerCode) continue;
                if (string.IsNullOrEmpty(bar.Mark)) continue;
                if (!bar.Mark.EndsWith($" {UBSuffix}")) continue;
                if (!GeometryHelper.IsInsideBbox(br.Position, slabBbox)) continue;

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

        /// <summary>Create new UB template (shape "21") in rebar box.</summary>
        private static (ObjectId barId, BarData elevBar) CreateUBTemplate(
            Database db, Extents3d rebarBbox, int existingCount,
            int diameter, double lengthA, double lengthB, double lengthC, string layerCode)
        {
            // posNr ALWAYS 01 for UB B1
            int posNr = UBPosNrB1;

            double insertX = rebarBbox.MinPoint.X + TemplateOffsetX;
            double insertY = rebarBbox.MaxPoint.Y - TemplateOffsetY - TemplateSpacingY * existingCount;
            var insertPt = new Point3d(insertX, insertY, 0);

            var elevBar = BuildBarData(diameter, posNr, lengthA, layerCode);
            elevBar.ShapeCode = "21";
            elevBar.LengthA   = lengthA;
            elevBar.LengthB   = lengthB;
            elevBar.LengthC   = lengthC;
            elevBar.Mark = BarData.FormatMark(diameter, posNr, 0, 1);  // "H12-01"

            ObjectId barId = SingleBarEngine.PlaceBar(db, elevBar, insertPt);

            Point3d textPt = new Point3d(
                insertPt.X + lengthA * 0.5,
                insertPt.Y + TemplateLabelOffsetY,
                0);
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

            PositionCounter.CommitUsed(db, posNr);
            return (barId, elevBar);
        }

        /// <summary>
        /// Generates single distribution + annotation at given x-offset within slab.
        /// For multi-distribution: offset shifts bar bounds along distribution axis.
        /// Length passed explicitly (per-dist, from ComputeDistributionPlan).
        /// </summary>
        private static bool GenerateDistributionWithLeaderAtOffset(
            Database db,
            double x0, double y0, double x1, double y1,
            ObjectId templateBarId, BarData templateBar,
            int diameter, double length, double spacing,
            string layerCode, string filterDirection,
            double coverForAdjusted,
            double slabSpanForAdjusted,
            bool applyAdjustment,
            double slabMinY,
            double slabMaxY)
        {
            bool horizontal = filterDirection == "X";

            double availableSpan = horizontal ? (y1 - y0) : (x1 - x0);

            double effectiveSpacing = spacing;
            int    adjustStatus     = 0;
            if (applyAdjustment)
            {
                (effectiveSpacing, adjustStatus) = ComputeAdjustedSpacing(
                    availableSpan, spacing, coverForAdjusted, slabSpanForAdjusted);
            }

            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc?.Editor;
            if (adjustStatus != 0 && ed != null)
            {
                string msg = adjustStatus == 1
                    ? $"[AutoRebar] Adjusted spacing to {effectiveSpacing:F1}mm. Label nominal 200mm."
                    : adjustStatus == 2
                        ? $"*** WARNING *** [AutoRebar] Spacing {effectiveSpacing:F1}mm in soft-min range (192-194)."
                        : $"*** WARNING *** [AutoRebar] Cannot adjust; last bar > 70mm from edge.";
                ed.WriteMessage($"\n{msg}\n");
            }

            int posNr = SingleBarEngine.ExtractPosNr(templateBar.Mark);
            if (posNr <= 0) posNr = 1;

            var distBar = BuildBarData(diameter, posNr, length, layerCode);
            string baseMark = BarData.FormatMark(diameter, posNr, spacing, 2);
            distBar.Mark            = $"{baseMark} {layerCode}";
            distBar.Spacing         = effectiveSpacing;
            distBar.Direction       = filterDirection;
            distBar.Count           = 0;
            distBar.SourceBarHandle = templateBarId.Handle.Value.ToString("X8");
            if (adjustStatus != 0) distBar.IsLabelManual = true;

            // Step 3: generate distribution block (sets distBar.BarsSpan via reference)
            var barResult = BarBlockEngine.GenerateFromBounds(
                db, x0, y0, x1, y1, distBar, horizontal, posNr);
            if (!barResult.IsValid) return false;

            // Step 3.5: pre-set leader points using arm-from-slab-edge math.
            // Direction (up/down) chosen per Q8 (bar positions vs slab edges),
            // Q9 tie-break: up. armEndY_local relative to annotInsertY_world.
            double firstBarY    = barResult.MinPoint.Y;
            double lastBarY     = barResult.MinPoint.Y + distBar.BarsSpan;
            double annotInsertY = horizontal
                ? barResult.MinPoint.Y                   // per current annotInsertPt definition
                : barResult.MinPoint.Y + length / 2.0;  // (vertical case for B2 future)

            // p372 scope = horizontal bars only (B1). Skip if vertical (B2 future).
            bool leaderUp;
            if (horizontal)
            {
                var (lu, encoded) = ComputeAnnotLeaderForHorizontalBars(
                    firstBarY, lastBarY, annotInsertY, slabMinY, slabMaxY);
                leaderUp = lu;
                distBar.LeaderPoints = encoded;
            }
            else
            {
                // B2 future — fallback do starej logiki (zachowane dla compatibility)
                leaderUp = true;
                double armEndY = distBar.BarsSpan + LeaderArmExtension;
                distBar.LeaderPoints = AnnotationEngine.EncodeLeaderPoints(new List<Point3d>
                {
                    new Point3d(0, 0,       0),
                    new Point3d(0, armEndY, 0),
                });
            }

            // Step 3.6: annotation insert centered on this dist's bar span (NOT slab center).
            Point3d annotInsertPt;
            if (horizontal)
            {
                annotInsertPt = new Point3d(
                    barResult.MinPoint.X + length / 2.0,
                    barResult.MinPoint.Y,
                    0);
            }
            else
            {
                annotInsertPt = new Point3d(
                    barResult.MinPoint.X,
                    barResult.MinPoint.Y + length / 2.0,
                    0);
            }

            // Step 4: annotation
            var annotResult = AnnotationEngine.CreateLeader(
                db, barResult, distBar,
                leaderHorizontal: false, posNr: posNr,
                customInsertPt: annotInsertPt,
                barsHorizontal: horizontal, leaderRight: true, leaderUp: leaderUp);

            // Step 5: bidirectional link dist ↔ annot
            if (annotResult.BlockRefId != ObjectId.Null)
                BarBlockEngine.LinkAnnotation(db, barResult.BlockRefId, annotResult.BlockRefId);

            // Step 6: show outline
            BarBlockHighlightManager.ShowOutlineFor(barResult.BlockRefId);

            PositionCounter.CommitUsed(db, posNr);
            return true;
        }

        /// <summary>
        /// Generate single UB distribution (left or right slab edge).
        /// Bounds anchored at slab edge: outer end of bars = exactly at cover line.
        /// SymbolSide differs per side so circles appear only on outer ends.
        /// </summary>
        private static bool GenerateUBDistribution(
            Database db, Extents3d slabBbox, double cover,
            ObjectId templateBarId, BarData templateBar,
            double lengthA, double lengthB, double lengthC,
            double spacing, string layerCode, bool isLeftSide,
            double slabMinY, double slabMaxY)
        {
            // X bounds: bar plan-view length = lengthA
            // Left UB:  x0 = slabMinX+cover (outer end at slab edge), x1 = x0+lengthA
            // Right UB: x1 = slabMaxX-cover (outer end at slab edge), x0 = x1-lengthA
            double x0, x1;
            if (isLeftSide)
            {
                x0 = slabBbox.MinPoint.X + cover;
                x1 = x0 + lengthA;
            }
            else
            {
                x1 = slabBbox.MaxPoint.X - cover;
                x0 = x1 - lengthA;
            }

            // Y bounds: full slab.dy with cover
            double y0 = slabBbox.MinPoint.Y + cover;
            double y1 = slabBbox.MaxPoint.Y - cover;

            double availSpacingSpan = y1 - y0;
            double slabSpan = slabBbox.MaxPoint.Y - slabBbox.MinPoint.Y;
            var (effSpacing, adjStatus) = ComputeAdjustedSpacing(availSpacingSpan, spacing, cover, slabSpan);

            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc?.Editor;
            if (adjStatus != 0 && ed != null)
            {
                string side = isLeftSide ? "Left" : "Right";
                string msg = adjStatus == 1
                    ? $"[AutoRebar UB {side}] Adjusted spacing to {effSpacing:F1}mm. Label nominal 200mm."
                    : adjStatus == 2
                        ? $"*** WARNING *** [AutoRebar UB {side}] Spacing {effSpacing:F1}mm in soft-min range."
                        : $"*** WARNING *** [AutoRebar UB {side}] Cannot adjust; last bar > 70mm from edge.";
                ed.WriteMessage($"\n{msg}\n");
            }

            int posNr = UBPosNrB1;

            var distBar = BuildBarData(UBDiameter, posNr, lengthA, layerCode);
            distBar.ShapeCode = "21";
            distBar.LengthA   = lengthA;
            distBar.LengthB   = lengthB;
            distBar.LengthC   = lengthC;

            // Mark with UB suffix (NOT " B1")
            string baseMark = BarData.FormatMark(UBDiameter, posNr, spacing, 2);
            distBar.Mark            = $"{baseMark} {UBSuffix}";  // "H12-01-200 UB"
            distBar.Spacing         = effSpacing;
            distBar.Direction       = "X";
            distBar.Count           = 0;
            distBar.SourceBarHandle = templateBarId.Handle.Value.ToString("X8");
            if (adjStatus != 0) distBar.IsLabelManual = true;

            // Circle markers on outer end only (at slab edge)
            distBar.SymbolSide = isLeftSide ? "Left" : "Right";

            // Step 1: generate distribution block (sets distBar.BarsSpan)
            var barResult = BarBlockEngine.GenerateFromBounds(
                db, x0, y0, x1, y1, distBar, horizontal: true, posNr);
            if (!barResult.IsValid) return false;

            // Step 2: leader points using arm-from-slab-edge math.
            // UB B1 = horizontal bars (shape 21 U-bar, distributed along Y).
            // Annot direction same logic as B1 — proximity bars to slab edges.
            double firstBarY    = barResult.MinPoint.Y;
            double lastBarY     = barResult.MinPoint.Y + distBar.BarsSpan;
            double annotInsertY = y0;  // per current UB annotInsertPt definition

            var (leaderUp, encoded) = ComputeAnnotLeaderForHorizontalBars(
                firstBarY, lastBarY, annotInsertY, slabMinY, slabMaxY);
            distBar.LeaderPoints = encoded;

            // Step 3: annotation insert centered on bar span (use explicit bounds, NOT barResult.MinPoint —
            // circle markers via SymbolSide="Left"/"Right" pollute GeometricExtents by ±35mm,
            // shifting MinPoint and causing dist line misalignment with bars)
            var annotInsertPt = new Point3d(
                x0 + lengthA / 2.0,
                y0,
                0);

            // Step 4: annotation
            var annotResult = AnnotationEngine.CreateLeader(
                db, barResult, distBar,
                leaderHorizontal: false, posNr: posNr,
                customInsertPt: annotInsertPt,
                barsHorizontal: true, leaderRight: true, leaderUp: leaderUp);

            // Step 5: bidirectional link + outline
            if (annotResult.BlockRefId != ObjectId.Null)
                BarBlockEngine.LinkAnnotation(db, barResult.BlockRefId, annotResult.BlockRefId);

            BarBlockHighlightManager.ShowOutlineFor(barResult.BlockRefId);

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

        /// <summary>
        /// Compute multi-distribution plan: how many distributions, what length each, what offset.
        /// Equal-as-possible algorithm with mixed fallback (last dist shorter).
        ///
        /// <summary>
        /// Computes pre-set LeaderPoints and leaderUp flag for horizontal-bar
        /// distribution annotation, based on proximity of dist bars to slab
        /// external edges (Q8 = bar positions criterion, Q9 = up tie-break).
        ///
        /// Pre-set LeaderPoints są LOKALNE do annotation block origin
        /// (annotInsertY_world). Returned encoded string is ready for
        /// distBar.LeaderPoints assignment.
        /// </summary>
        /// <returns>(leaderUp flag for CreateLeader, encoded LeaderPoints for distBar)</returns>
        private static (bool leaderUp, string leaderPointsEncoded) ComputeAnnotLeaderForHorizontalBars(
            double firstBarY_world,
            double lastBarY_world,
            double annotInsertY_world,
            double slabMinY,
            double slabMaxY)
        {
            double distFirstBarToSlabMin = firstBarY_world - slabMinY;
            double distLastBarToSlabMax  = slabMaxY - lastBarY_world;

            // Q9: <= ensures tie-break = up (rect slab backward compat)
            bool leaderUp = distLastBarToSlabMax <= distFirstBarToSlabMin;

            double armEndY_world = leaderUp
                ? slabMaxY + LeaderArmExtension
                : slabMinY - LeaderArmExtension;

            double armEndY_local = armEndY_world - annotInsertY_world;

            string encoded = AnnotationEngine.EncodeLeaderPoints(new List<Point3d>
            {
                new Point3d(0, 0,             0),
                new Point3d(0, armEndY_local, 0),
            });

            return (leaderUp, encoded);
        }

        /// Returns list of (xOffset, length) per distribution. xOffset = position from
        /// slab+cover origin (absolute x0_dist = slabMinX + cover + xOffset).
        /// For slab fitting single dist (available &lt;= TemplateMaxLen) returns single entry.
        /// </summary>
        private static List<(double xOffset, double length)> ComputeDistributionPlan(
            double available, double targetSpacing)
        {
            var result = new List<(double, double)>();

            // Single-dist case (+0.5mm epsilon for slab geometry imprecision - CAD vertex
            // snapping can give slab.dx like 6080.0001mm, available 6000.0001mm. Epsilon
            // is subgrid (TemplateGridStep=250mm), nie wplywa na SnapDownToGrid behavior).
            if (available <= TemplateMaxLen + 0.5)
            {
                double L = GeometryHelper.SnapDownToGrid(
                    available, TemplateGridStep, TemplateMinLen, TemplateMaxLen);
                if (L > 0)
                    result.Add((0.0, L));
                return result;
            }

            // Multi-dist: compute N_min
            // available = N*L - (N-1)*O, max L = 6000, max O = 650
            //   → N >= (available - 650) / (6000 - 650)
            int N_min = (int)Math.Ceiling((available - OverlapMax) / (TemplateMaxLen - OverlapMax));
            if (N_min < 2) N_min = 2;

            // Hard upper bound (prevent runaway)
            int N_max = (int)Math.Ceiling((available - OverlapMin) / (TemplateMinLen - OverlapMin)) + 1;

            // Try equal-length solution for each N
            for (int N = N_min; N <= N_max; N++)
            {
                double L_raw     = (available + (N - 1) * OverlapTarget) / N;
                double L_snapped = Math.Round(L_raw / TemplateGridStep) * TemplateGridStep;

                if (L_snapped < TemplateMinLen || L_snapped > TemplateMaxLen) continue;

                double O_actual = (N * L_snapped - available) / (N - 1);
                if (O_actual >= OverlapMin && O_actual <= OverlapMax)
                {
                    for (int i = 0; i < N; i++)
                        result.Add((i * (L_snapped - O_actual), L_snapped));
                    return result;
                }

                // Try L_snapped ± gridStep
                foreach (int delta in new[] { -1, 1 })
                {
                    double L_try = L_snapped + delta * TemplateGridStep;
                    if (L_try < TemplateMinLen || L_try > TemplateMaxLen) continue;
                    double O_try = (N * L_try - available) / (N - 1);
                    if (O_try >= OverlapMin && O_try <= OverlapMax)
                    {
                        for (int i = 0; i < N; i++)
                            result.Add((i * (L_try - O_try), L_try));
                        return result;
                    }
                }
            }

            // Mixed fallback: first (N-1) dists at 6000mm, last dist shorter.
            // O between adjacent = OverlapTarget (by construction).
            for (int N = 2; N <= N_max; N++)
            {
                double L_last_raw = available - (N - 1) * (TemplateMaxLen - OverlapTarget);
                if (L_last_raw < TemplateMinLen || L_last_raw > TemplateMaxLen) continue;

                double L_last = Math.Round(L_last_raw / TemplateGridStep) * TemplateGridStep;
                if (L_last < TemplateMinLen || L_last > TemplateMaxLen) continue;

                for (int i = 0; i < N - 1; i++)
                    result.Add((i * (TemplateMaxLen - OverlapTarget), TemplateMaxLen));
                result.Add(((N - 1) * (TemplateMaxLen - OverlapTarget), L_last));
                return result;
            }

            // Best-effort fallback (extreme edge case)
            double fallbackL = GeometryHelper.SnapDownToGrid(
                Math.Min(available, TemplateMaxLen), TemplateGridStep, TemplateMinLen, TemplateMaxLen);
            if (fallbackL > 0) result.Add((0.0, fallbackL));
            return result;
        }

        /// <summary>
        /// Compute adjusted spacing to satisfy max-distance-from-edge constraint.
        /// Returns (adjustedSpacing, status) where status is:
        ///   0 = no adjustment needed (nominal spacing OK)
        ///   1 = adjustment applied silently (new spacing >= 194)
        ///   2 = adjustment applied with warning (192 <= new spacing &lt; 194)
        ///   3 = adjustment rejected (new spacing &lt; 192) - returns nominal spacing
        /// </summary>
        private static (double adjustedSpacing, int status) ComputeAdjustedSpacing(
            double availableSpan, double nominalSpacing, double cover, double slabSpan)
        {
            // Nominal count (jak GenerateFromBounds: count = floor(span/spacing) + 1)
            int nominalCount = (int)Math.Floor(availableSpan / nominalSpacing) + 1;

            // Last bar Y position (relative to slab origin, accounting for cover offset y0)
            double nominalLastBarY = cover + (nominalCount - 1) * nominalSpacing;
            double nominalDistanceToEdge = slabSpan - nominalLastBarY;

            if (nominalDistanceToEdge <= MaxLastBarDistanceFromEdge)
                return (nominalSpacing, 0);  // no adjustment needed

            // Try adding 1 more bar: new count = nominalCount + 1
            // New spacing fills full availableSpan: span / (count - 1)
            int newCount = nominalCount + 1;
            double newSpacing = availableSpan / (newCount - 1);

            if (newSpacing >= SoftMinAdjustedSpacing)
                return (newSpacing, 1);  // silent acceptance
            if (newSpacing >= MinAdjustedSpacing)
                return (newSpacing, 2);  // accept with warning

            return (nominalSpacing, 3);  // reject - geometric impossibility
        }
    }
}
