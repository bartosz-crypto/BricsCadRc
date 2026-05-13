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

        /// <summary>Min vertical edge length for UB B1 segment generation (Q17).
        /// Shorter edges skipped + warning.</summary>
        public const double UBMinSegmentLength = 1000.0;

        // UB B2 constants (Y-bars, horizontal edges) — separate from UB B1
        private const double UBB2_225_LengthA   = 715.0;
        private const double UBB2_225_LengthB   = 115.0;
        private const double UBB2_225_LengthC   = 715.0;
        private const string UBB2_225_ShapeCode = "13";

        private const double UBB2_300_LengthA   = 675.0;
        private const double UBB2_300_LengthB   = 190.0;
        private const double UBB2_300_LengthC   = 675.0;
        private const string UBB2_300_ShapeCode = "21";

        private const int UBPosNrB2 = 2;

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

            // Strip decomposition (Etap 1E + 1B Faza 2 dispatch).
            // horizontal=true (X-bars/B1) → DecomposeIntoYStrips (Y-axis scan).
            // horizontal=false (Y-bars/B2) → DecomposeIntoXStrips (X-axis scan).
            List<StripBounds> strips = horizontal
                ? DecomposeIntoYStrips(plan.SlabVertices, plan.SlabBbox).Select(s => ToStripBounds(s)).ToList()
                : DecomposeIntoXStrips(plan.SlabVertices, plan.SlabBbox).Select(s => ToStripBounds(s)).ToList();
            int validStrips   = strips.Count(s => s.Valid);
            int skippedStrips = strips.Count - validStrips;

            ed.WriteMessage($"\n[AutoRebar] Strips: {strips.Count} total, " +
                            $"{validStrips} valid, {skippedStrips} skipped.\n");

            foreach (var s in strips.Where(s => !s.Valid))
                ed.WriteMessage($"\n[AutoRebar] Skip strip scan={s.ScanLow:F0}..{s.ScanHigh:F0}: {s.SkipReason}\n");

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
                    double stripHeight = strip.ScanHigh - strip.ScanLow;

                    // Thin strip skip
                    if (stripHeight < 50.0)
                    {
                        ed.WriteMessage($"\n*** WARNING *** [AutoRebar] Strip " +
                            $"scan={strip.ScanLow:F0}..{strip.ScanHigh:F0} h={stripHeight:F0}mm < 50mm — skipped\n");
                        continue;
                    }

                    // First/last bar offsets per Q7=B
                    double lowerOffset = strip.LowerIsExternal ? cover : spacing / 2.0;
                    double upperOffset = strip.UpperIsExternal ? cover : spacing / 2.0;
                    double y0 = strip.ScanLow  + lowerOffset;   // first bar pos (scan axis)
                    double y1 = strip.ScanHigh - upperOffset;   // last bar pos  (scan axis)

                    // Try-fit single bar for short strips
                    bool singleBarMode = stripHeight < spacing || (y1 - y0) <= 0;
                    if (singleBarMode)
                    {
                        double yCenter = (strip.ScanLow + strip.ScanHigh) * 0.5;
                        y0 = yCenter;
                        y1 = yCenter;
                        ed.WriteMessage($"\n*** WARNING *** [AutoRebar] Strip " +
                            $"scan={strip.ScanLow:F0}..{strip.ScanHigh:F0} h={stripHeight:F0}mm < spacing " +
                            $"— 1 bar centered at scan={yCenter:F0}\n");
                    }

                    SpacingMode spacingMode;
                    if (singleBarMode)
                        spacingMode = SpacingMode.Nominal;
                    else if (strip.UpperIsExternal)
                        spacingMode = SpacingMode.AdjustedExternal;
                    else
                        spacingMode = SpacingMode.ContinuousInternal;

                    // X multi-dist plan for this strip
                    double xAvailable = (strip.PerpHigh - strip.PerpLow) - 2.0 * cover;
                    var distPlan = ComputeDistributionPlan(xAvailable, spacing);
                    if (distPlan.Count == 0)
                    {
                        ed.WriteMessage($"\n[AutoRebar] Strip scan={strip.ScanLow:F0}..{strip.ScanHigh:F0}: " +
                            $"brak rozwiązania dist plan (xAvailable={xAvailable:F0}mm) — skipped\n");
                        continue;
                    }

                    ed.WriteMessage($"\n[AutoRebar] Strip scan={strip.ScanLow:F0}..{strip.ScanHigh:F0} " +
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
                                                               diameter);
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
                                                                   diameter);
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

                        double x0 = strip.PerpLow + cover + xOffset;   // bar start (perp axis)
                        double x1 = x0 + length;

                        // Map local scan/perp → WCS bbox for GenerateFromBounds.
                        // B1 (horizontal=true):  scan=Y, perp=X → wcsX=perp(x0/x1),  wcsY=scan(y0/y1)
                        // B2 (horizontal=false): scan=X, perp=Y → wcsX=scan(y0/y1), wcsY=perp(x0/x1)
                        double wcsX0   = horizontal ? x0 : y0;
                        double wcsY0   = horizontal ? y0 : x0;
                        double wcsX1   = horizontal ? x1 : y1;
                        double wcsY1   = horizontal ? y1 : x1;
                        double slabMin = horizontal ? plan.SlabBbox.MinPoint.Y : plan.SlabBbox.MinPoint.X;
                        double slabMax = horizontal ? plan.SlabBbox.MaxPoint.Y : plan.SlabBbox.MaxPoint.X;

                        bool ok = GenerateDistributionWithLeaderAtOffset(
                            db, wcsX0, wcsY0, wcsX1, wcsY1,
                            templateBarId, templateBar,
                            diameter, length, spacing, layerCode, filterDirection,
                            lowerOffset, stripHeight, spacingMode,
                            slabMin, slabMax);

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
            string   filterDirection,
            double   spacing = DefaultSpacing,
            double   cover   = DefaultCover)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            // Pick UB params per thickness + direction (UB B1 = X-bars vertical edges,
            // UB B2 = Y-bars horizontal edges with separate constants).
            bool   isUBB1 = filterDirection == "X";
            double ubLengthA, ubLengthB, ubLengthC;
            string ubShapeCode;
            int    ubPosNr;
            if (isUBB1)
            {
                // UB B1 — existing constants, hardcoded shape "21"
                if (slabThickness == 225)
                { ubLengthA = UB_225_LengthA; ubLengthB = UB_225_LengthB; ubLengthC = UB_225_LengthC; }
                else if (slabThickness == 300)
                { ubLengthA = UB_300_LengthA; ubLengthB = UB_300_LengthB; ubLengthC = UB_300_LengthC; }
                else
                {
                    ed.WriteMessage($"\n[AutoRebar UB] Nieobsługiwana grubość: {slabThickness}mm (225 lub 300).\n");
                    return -1;
                }
                ubShapeCode = "21";
                ubPosNr     = UBPosNrB1;
            }
            else
            {
                // UB B2 — new constants, shape dispatch per thickness (225->"13", 300->"21")
                if (slabThickness == 225)
                {
                    ubLengthA = UBB2_225_LengthA; ubLengthB = UBB2_225_LengthB; ubLengthC = UBB2_225_LengthC;
                    ubShapeCode = UBB2_225_ShapeCode;
                }
                else if (slabThickness == 300)
                {
                    ubLengthA = UBB2_300_LengthA; ubLengthB = UBB2_300_LengthB; ubLengthC = UBB2_300_LengthC;
                    ubShapeCode = UBB2_300_ShapeCode;
                }
                else
                {
                    ed.WriteMessage($"\n[AutoRebar UB] Nieobsługiwana grubość: {slabThickness}mm (225 lub 300).\n");
                    return -1;
                }
                ubPosNr = UBPosNrB2;
            }

            // Check posNr conflict
            var usedNrs = PositionCounter.GetUsedPositionNumbers(db);
            if (usedNrs.Contains(ubPosNr))
            {
                bool sameUB = IsExistingPosNrUB(db, UBDiameter, ubPosNr);
                if (!sameUB)
                {
                    var dlgResult = System.Windows.MessageBox.Show(
                        $"PosNr {ubPosNr:D2} jest już używany przez inny pręt (nie UB H{UBDiameter}).\n" +
                        $"AutoRebar UB używa posNr={ubPosNr:D2}. Kontynuować?\n\n" +
                        "Tak = wymuś posNr (może spowodować konflikt w schedule)\n" +
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
            List<Point2d> slabVertices;

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

                slabBbox     = GeometryHelper.PolylineBbox(slabPl);
                slabVertices = GeometryHelper.GetPolylineVertices(slabPl);
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

                oldUBs = ScanOldUBDistributions(db, tr, slabBbox, layerCode, filterDirection);
                tr.Commit();
            }

            // Edge enumeration — classify all polyline edges
            var edges = GeometryHelper.EnumerateAxisAlignedEdges(slabVertices);

            var verticalCandidates   = new List<GeometryHelper.PolylineEdge>();
            var horizontalCandidates = new List<GeometryHelper.PolylineEdge>();
            int diagonalCount        = 0;
            int zeroLengthCount      = 0;

            foreach (var e in edges)
            {
                switch (e.Orientation)
                {
                    case GeometryHelper.EdgeOrientation.Vertical:   verticalCandidates.Add(e); break;
                    case GeometryHelper.EdgeOrientation.Horizontal: horizontalCandidates.Add(e); break;
                    case GeometryHelper.EdgeOrientation.Diagonal:   diagonalCount++; break;
                    case GeometryHelper.EdgeOrientation.ZeroLength: zeroLengthCount++; break;
                }
            }

            // Direction dispatch: UB B1 → vertical edges (X-bars), UB B2 → horizontal edges (Y-bars)
            var    candidates   = isUBB1 ? verticalCandidates : horizontalCandidates;
            int    skippedCount = isUBB1 ? horizontalCandidates.Count : verticalCandidates.Count;
            string skippedKind  = isUBB1 ? "horizontal" : "vertical";

            if (skippedCount > 0)
                ed.WriteMessage($"\n[AutoRebar UB] Skipped {skippedCount} {skippedKind} edge(s) " +
                                $"(filterDirection={filterDirection})\n");
            if (diagonalCount > 0)
                ed.WriteMessage($"\n*** WARNING *** [AutoRebar UB] Skipped {diagonalCount} " +
                                $"diagonal edge(s) — axis-aligned slabs only\n");

            // Filter by min length and compute SymbolSide per segment
            var validSegments = new List<(double edgeCoord, double segLow, double segHigh, string symbolSide)>();
            int tooShortCount = 0;

            foreach (var seg in candidates)
            {
                if (seg.Length < UBMinSegmentLength)
                {
                    ed.WriteMessage($"\n*** WARNING *** [AutoRebar UB] Edge " +
                        $"length {seg.Length:F0}mm < {UBMinSegmentLength:F0}mm, skipped\n");
                    tooShortCount++;
                    continue;
                }

                // Axis dispatch: UB B1 (vertical edge) — edgeCoord=X, segLow/High=Y bounds.
                // UB B2 (horizontal edge) — edgeCoord=Y, segLow/High=X bounds.
                double edgeCoord, segLow, segHigh;
                if (isUBB1)
                {
                    edgeCoord = seg.Start.X;
                    segLow    = Math.Min(seg.Start.Y, seg.End.Y);
                    segHigh   = Math.Max(seg.Start.Y, seg.End.Y);
                }
                else
                {
                    edgeCoord = seg.Start.Y;
                    segLow    = Math.Min(seg.Start.X, seg.End.X);
                    segHigh   = Math.Max(seg.Start.X, seg.End.X);
                }

                // Auto-detect inward direction: sample point slightly OFFSET from edge midpoint
                // to positive side (right of vertical edge, above horizontal edge).
                double midSeg = (segLow + segHigh) * 0.5;
                Teigha.Geometry.Point2d testPoint = isUBB1
                    ? new Teigha.Geometry.Point2d(edgeCoord + 0.1, midSeg)   // right of vertical edge
                    : new Teigha.Geometry.Point2d(midSeg, edgeCoord + 0.1);  // above horizontal edge
                bool   interiorOnPositiveSide = GeometryHelper.IsPointInsidePolygon(slabVertices, testPoint);
                string symbolSide = interiorOnPositiveSide ? "Left" : "Right";

                validSegments.Add((edgeCoord, segLow, segHigh, symbolSide));
            }

            ed.WriteMessage($"\n[AutoRebar UB] Segments ({filterDirection}): {candidates.Count} total, " +
                            $"{validSegments.Count} valid, {tooShortCount} too short.\n");

            if (validSegments.Count == 0)
            {
                ed.WriteMessage($"\n[AutoRebar UB] No valid segments — abort.\n");
                return -1;
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
                        UBDiameter, ubLengthA, ubLengthB, ubLengthC, layerCode,
                        ubPosNr, ubShapeCode);
                    ed.WriteMessage($"\n[AutoRebar UB] Created UB template H{UBDiameter}-01 " +
                        $"(A={ubLengthA}, B={ubLengthB}, C={ubLengthC})\n");
                }

                // Axis dispatch: across-axis is Y dla UB B1 (vertical edges, dist along Y)
                // vs X dla UB B2 (horizontal edges, dist along X). Loop-invariant.
                double slabAcrossMin = isUBB1 ? slabBbox.MinPoint.Y : slabBbox.MinPoint.X;
                double slabAcrossMax = isUBB1 ? slabBbox.MaxPoint.Y : slabBbox.MaxPoint.X;

                foreach (var (edgeCoord, segLow, segHigh, symbolSide) in validSegments)
                {
                    // Per-endpoint external detection (Q19=b, analog B1 strip semantics)
                    bool lowerIsExternal = Math.Abs(segLow  - slabAcrossMin) < 1e-3;
                    bool upperIsExternal = Math.Abs(segHigh - slabAcrossMax) < 1e-3;

                    double lowerOffset = lowerIsExternal ? cover : spacing / 2.0;
                    double upperOffset = upperIsExternal ? cover : spacing / 2.0;

                    // Q20=b: SpacingMode per upper endpoint (analog B1 GenerateLayer)
                    SpacingMode spacingMode = upperIsExternal
                        ? SpacingMode.AdjustedExternal
                        : SpacingMode.ContinuousInternal;

                    ed.WriteMessage($"\n[AutoRebar UB] Segment edge={edgeCoord:F0} seg=[{segLow:F0}..{segHigh:F0}] " +
                        $"length {segHigh - segLow:F0}mm SymbolSide={symbolSide} " +
                        $"endpoints(L={(lowerIsExternal ? "ext" : "int")},U={(upperIsExternal ? "ext" : "int")}) " +
                        $"mode={spacingMode}\n");

                    // Strategy D: per-segment try/catch — gdyby 1 segment crashował,
                    // pozostałe nadal się generują. Bez tego cała komenda abortuje.
                    try
                    {
                        bool ok = GenerateUBDistribution(
                            db, edgeCoord, segLow, segHigh, lowerOffset, upperOffset,
                            templateBarId, templateBar,
                            ubLengthA, ubLengthB, ubLengthC, spacing, layerCode, symbolSide,
                            spacingMode,
                            slabAcrossMin, slabAcrossMax,
                            ubPosNr, ubShapeCode, filterDirection);
                        if (ok) generated++;
                    }
                    catch (System.Exception ex)
                    {
                        ed.WriteMessage($"\n*** ERROR *** [AutoRebar UB] Segment edge={edgeCoord:F0} " +
                            $"seg=[{segLow:F0}..{segHigh:F0}] failed: {ex.Message}\n");
                    }
                }
            }

            ed.WriteMessage(
                $"\n[AutoRebar UB] Wygenerowano {generated} z {validSegments.Count} rozkładów UB B1 " +
                $"(grubość {slabThickness}mm).\n");
            return generated;
        }

        // ----------------------------------------------------------------
        // Phase 1 — read-only plan (inside caller's transaction)
        // ----------------------------------------------------------------

        private enum SpacingMode
        {
            /// <summary>Use nominalSpacing as-is, no redistribution. Single bar mode,
            /// or fallback when other modes can't apply.</summary>
            Nominal,

            /// <summary>Existing logic: 70mm-from-edge check, may zagęścić if last bar
            /// too far from external upper edge. For rect slab and strips with
            /// UpperIsExternal=true.</summary>
            AdjustedExternal,

            /// <summary>NEW p373: force last bar at y1 by redistributing count,
            /// achieving continuity through internal cut. For strips with
            /// UpperIsExternal=false.</summary>
            ContinuousInternal
        }

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

        private class XStrip
        {
            public double XLow;
            public double XHigh;
            public double YLow;
            public double YHigh;
            public bool   LowerIsExternal;
            public bool   UpperIsExternal;
            public bool   Valid;
            public string SkipReason;
        }

        private class StripBounds
        {
            public double ScanLow;          // primary axis low (perpendicular to bars)
            public double ScanHigh;         // primary axis high
            public double PerpLow;          // along-bar axis low
            public double PerpHigh;         // along-bar axis high
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
            var templates = ScanTemplates(db, tr, rebarBbox, diameter);

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

        private static List<XStrip> DecomposeIntoXStrips(List<Point2d> vertices, Extents3d slabBbox)
        {
            var strips = new List<XStrip>();

            // Phase A: unique X-coords sorted + dedup epsilon 1e-3
            var rawXs = new List<double>();
            foreach (var v in vertices)
                rawXs.Add(v.X);
            rawXs.Sort();

            var xCoords = new List<double>();
            foreach (double x in rawXs)
            {
                if (xCoords.Count == 0 || Math.Abs(x - xCoords[xCoords.Count - 1]) >= 1e-3)
                    xCoords.Add(x);
            }

            if (xCoords.Count < 2) return strips;

            // Phase B: filter phantom vertices (compare ray-cast x-0.5 vs x+0.5)
            // Keep first + last always; check inner ones
            var filteredXs = new List<double> { xCoords[0] };
            for (int i = 1; i < xCoords.Count - 1; i++)
            {
                double xc = xCoords[i];
                var ysLeft  = GeometryHelper.FindIntersectionsV(vertices, xc - 0.5);
                var ysRight = GeometryHelper.FindIntersectionsV(vertices, xc + 0.5);
                if (!XIntersectionsEqual(ysLeft, ysRight))
                    filteredXs.Add(xc);
            }
            filteredXs.Add(xCoords[xCoords.Count - 1]);

            // Phase C: build strips per X-pair
            double slabMinX = slabBbox.MinPoint.X;
            double slabMaxX = slabBbox.MaxPoint.X;

            for (int i = 0; i < filteredXs.Count - 1; i++)
            {
                double xLow  = filteredXs[i];
                double xHigh = filteredXs[i + 1];

                double xMid = (xLow + xHigh) * 0.5 + 0.1;
                var ys = GeometryHelper.FindIntersectionsV(vertices, xMid);

                if (ys.Count != 2)
                {
                    string reason = ys.Count > 2
                        ? $"Multi-span detected ({ys.Count / 2} intervals), Etap 1G future"
                        : $"Degenerate strip ({ys.Count} intersections at mid-X={xMid:F1})";
                    strips.Add(new XStrip
                    {
                        XLow = xLow, XHigh = xHigh,
                        Valid = false, SkipReason = reason
                    });
                    continue;
                }

                strips.Add(new XStrip
                {
                    XLow            = xLow,
                    XHigh           = xHigh,
                    YLow            = ys[0],
                    YHigh           = ys[1],
                    LowerIsExternal = Math.Abs(xLow  - slabMinX) < 1e-3,
                    UpperIsExternal = Math.Abs(xHigh - slabMaxX) < 1e-3,
                    Valid           = true,
                });
            }

            return strips;
        }

        private static StripBounds ToStripBounds(YStrip s)
        {
            // YStrip: scan axis = Y, perp axis = X (X-bars rozciągają się X→X)
            return new StripBounds
            {
                ScanLow         = s.YLow,
                ScanHigh        = s.YHigh,
                PerpLow         = s.XLow,
                PerpHigh        = s.XHigh,
                LowerIsExternal = s.LowerIsExternal,
                UpperIsExternal = s.UpperIsExternal,
                Valid           = s.Valid,
                SkipReason      = s.SkipReason,
            };
        }

        private static StripBounds ToStripBounds(XStrip s)
        {
            // XStrip: scan axis = X, perp axis = Y (Y-bars rozciągają się Y→Y)
            return new StripBounds
            {
                ScanLow         = s.XLow,
                ScanHigh        = s.XHigh,
                PerpLow         = s.YLow,
                PerpHigh        = s.YHigh,
                LowerIsExternal = s.LowerIsExternal,
                UpperIsExternal = s.UpperIsExternal,
                Valid           = s.Valid,
                SkipReason      = s.SkipReason,
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
            Database db, Transaction tr, Extents3d rebarBbox, int diameter)
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
                // Etap 1E: usunięto filter InferDirectionFromPolyline — B1/B2 share template pool.
                // Match po diameter + length (downstream), Direction irrelevant for elev template.
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

        /// <summary>Check if existing posNr entity is UB H{diameter} (safe reuse) or different type (conflict).</summary>
        private static bool IsExistingPosNrUB(Database db, int diameter, int posNr)
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
                        && (bar.ShapeCode == "21" || bar.ShapeCode == "13")
                        && SingleBarEngine.ExtractPosNr(bar.Mark) == posNr)
                    {
                        tr.Commit();
                        return true;
                    }
                }
                tr.Commit();
            }
            return false;
        }

        /// <summary>Scan UB templates (shape "21" or "13") in rebar box.</summary>
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
                // Etap 2 Faza B: UB B1 + UB B2 share template pool (shape "21" OR "13" dla 225mm).
                if (bar.ShapeCode != "21" && bar.ShapeCode != "13") continue;
                if (bar.Diameter != diameter) continue;
                var insPt = pl.GetPoint3dAt(0);
                if (!GeometryHelper.IsInsideBbox(insPt, rebarBbox)) continue;
                result.Add((oid, bar));
            }
            return result;
        }

        /// <summary>Scan old UB distributions on slab (identified by Mark suffix " UB").</summary>
        private static List<(ObjectId, ObjectId)> ScanOldUBDistributions(
            Database db, Transaction tr, Extents3d slabBbox, string layerCode, string filterDirection)
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
                if (bar.Direction != filterDirection) continue;
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
            int diameter, double lengthA, double lengthB, double lengthC, string layerCode,
            int posNr, string shapeCode)
        {
            // posNr i shapeCode parametryczne (UB B1 → 1/"21", UB B2 → 2/"13" lub "21")

            // User-requested: UB templates from RIGHT edge of rebar box.
            // Shape "21" (U-bar) bar width in X = lengthB (the bend dimension);
            // shape "13" (hairpin) bar width in X = lengthA.
            double barWidth = shapeCode == "13" ? lengthA : lengthB;
            double insertX  = rebarBbox.MaxPoint.X - TemplateOffsetX - barWidth;
            double insertY  = rebarBbox.MaxPoint.Y - TemplateOffsetY - TemplateSpacingY * existingCount;
            var insertPt    = new Point3d(insertX, insertY, 0);

            var elevBar = BuildBarData(diameter, posNr, lengthA, layerCode);
            elevBar.ShapeCode = shapeCode;
            elevBar.LengthA   = lengthA;
            elevBar.LengthB   = lengthB;
            elevBar.LengthC   = lengthC;
            elevBar.Mark = BarData.FormatMark(diameter, posNr, 0, 1);  // "H12-01" or "H12-02"

            ObjectId barId = SingleBarEngine.PlaceBar(db, elevBar, insertPt);

            Point3d textPt = new Point3d(
                insertPt.X + barWidth * 0.5,            // centered over actual bar geometry
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
            SpacingMode spacingMode,
            double slabMinY,     // B1: slab Y bounds; B2: slab X bounds (dispatch from GenerateLayer)
            double slabMaxY)     // (param names kept for B1 backward compat; semantics differ for B2)
        {
            bool horizontal = filterDirection == "X";

            double availableSpan = horizontal ? (y1 - y0) : (x1 - x0);

            double effectiveSpacing = spacing;
            int    adjustStatus     = 0;
            switch (spacingMode)
            {
                case SpacingMode.Nominal:
                    // No adjustment — single bar mode or explicit nominal
                    break;

                case SpacingMode.AdjustedExternal:
                    (effectiveSpacing, adjustStatus) = ComputeAdjustedSpacing(
                        availableSpan, spacing, coverForAdjusted, slabSpanForAdjusted);
                    break;

                case SpacingMode.ContinuousInternal:
                    (effectiveSpacing, adjustStatus) = ComputeContinuousSpacing(
                        availableSpan, spacing);
                    break;
            }

            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc?.Editor;
            if (adjustStatus != 0 && ed != null)
            {
                string msg;
                if (spacingMode == SpacingMode.AdjustedExternal)
                {
                    msg = adjustStatus == 1
                        ? $"[AutoRebar] AdjExt spacing {effectiveSpacing:F1}mm. Label nominal {spacing:F0}mm."
                        : adjustStatus == 2
                            ? $"*** WARNING *** [AutoRebar] AdjExt spacing {effectiveSpacing:F1}mm in soft-min range (192-194)."
                            : $"*** WARNING *** [AutoRebar] AdjExt cannot adjust; last bar > 70mm from edge.";
                }
                else if (spacingMode == SpacingMode.ContinuousInternal)
                {
                    msg = adjustStatus == 1
                        ? $"[AutoRebar] ContInt spacing {effectiveSpacing:F1}mm " +
                          $"(deviation {Math.Abs(effectiveSpacing - spacing) / spacing * 100:F1}%). Label nominal {spacing:F0}mm."
                        : $"*** WARNING *** [AutoRebar] ContInt deviation > 15%, fallback nominal {spacing:F0}mm — gap to next strip may differ from spacing.";
                }
                else
                {
                    msg = $"[AutoRebar] Unexpected status {adjustStatus} for mode {spacingMode}";
                }
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

            // Etap 1C: proximity dispatch — B1 Y-axis via ComputeAnnotLeaderForHorizontalBars,
            // B2 X-axis via ComputeAnnotLeaderForVerticalBars.
            bool leaderUp;
            bool leaderRight = true;  // B1: always right (leaderRight unused for X-bars); B2: computed below
            if (horizontal)
            {
                var (lu, encoded) = ComputeAnnotLeaderForHorizontalBars(
                    firstBarY, lastBarY, annotInsertY, slabMinY, slabMaxY);
                leaderUp = lu;
                distBar.LeaderPoints = encoded;
            }
            else
            {
                // Etap 1C: proximity-based X-axis leader.
                // slabMinY/slabMaxY for B2 = X coords (dispatch from GenerateLayer Zmiana B).
                var (lr, encodedV) = ComputeAnnotLeaderForVerticalBars(
                    firstBarX_world:    barResult.MinPoint.X,
                    lastBarX_world:     barResult.MinPoint.X + distBar.BarsSpan,
                    annotInsertX_world: barResult.MinPoint.X,
                    slabMinX: slabMinY,
                    slabMaxX: slabMaxY);
                leaderRight = lr;
                leaderUp    = true;
                distBar.LeaderPoints = encodedV;
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
                barsHorizontal: horizontal, leaderRight: leaderRight, leaderUp: leaderUp);

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
            Database db,
            double edgeCoord, double segLow, double segHigh,
            double lowerOffset, double upperOffset,
            ObjectId templateBarId, BarData templateBar,
            double lengthA, double lengthB, double lengthC,
            double spacing, string layerCode, string symbolSide,
            SpacingMode spacingMode,
            double slabMinAcross, double slabMaxAcross,
            int posNr, string shapeCode, string filterDirection)
        {
            bool isXBars = filterDirection == "X";

            // Bar bounds (along bar axis = perpendicular to edge, extends INTO slab from edge).
            double barLow, barHigh;
            if (symbolSide == "Left")  { barLow = edgeCoord + DefaultCover; barHigh = barLow  + lengthA; }
            else                       { barHigh = edgeCoord - DefaultCover; barLow  = barHigh - lengthA; }

            // Dist bounds (along edge axis = distribution axis): per-endpoint offsets.
            double distLow  = segLow  + lowerOffset;
            double distHigh = segHigh - upperOffset;

            double availSpacingSpan = distHigh - distLow;
            double segmentSpan      = segHigh - segLow;

            double effSpacing = spacing;
            int    adjStatus  = 0;
            switch (spacingMode)
            {
                case SpacingMode.Nominal:
                    break;
                case SpacingMode.AdjustedExternal:
                    (effSpacing, adjStatus) = ComputeAdjustedSpacing(
                        availSpacingSpan, spacing, lowerOffset, segmentSpan);
                    break;
                case SpacingMode.ContinuousInternal:
                    (effSpacing, adjStatus) = ComputeContinuousSpacing(
                        availSpacingSpan, spacing);
                    break;
            }

            var doc = Application.DocumentManager.MdiActiveDocument;
            var ed  = doc?.Editor;
            if (adjStatus != 0 && ed != null)
            {
                string side = symbolSide;
                string msg;
                if (spacingMode == SpacingMode.AdjustedExternal)
                {
                    msg = adjStatus == 1
                        ? $"[AutoRebar UB {side}] AdjExt spacing {effSpacing:F1}mm. Label nominal {spacing:F0}mm."
                        : adjStatus == 2
                            ? $"*** WARNING *** [AutoRebar UB {side}] AdjExt spacing {effSpacing:F1}mm in soft-min range."
                            : $"*** WARNING *** [AutoRebar UB {side}] AdjExt cannot adjust; last bar > 70mm from edge.";
                }
                else if (spacingMode == SpacingMode.ContinuousInternal)
                {
                    msg = adjStatus == 1
                        ? $"[AutoRebar UB {side}] ContInt spacing {effSpacing:F1}mm " +
                          $"(deviation {Math.Abs(effSpacing - spacing) / spacing * 100:F1}%). Label nominal {spacing:F0}mm."
                        : $"*** WARNING *** [AutoRebar UB {side}] ContInt deviation > 15%, fallback nominal {spacing:F0}mm.";
                }
                else
                {
                    msg = $"[AutoRebar UB {side}] Unexpected status {adjStatus} for mode {spacingMode}";
                }
                ed.WriteMessage($"\n{msg}\n");
            }

            var distBar = BuildBarData(UBDiameter, posNr, lengthA, layerCode);
            distBar.ShapeCode = shapeCode;
            distBar.LengthA   = lengthA;
            distBar.LengthB   = lengthB;
            distBar.LengthC   = lengthC;

            // Mark with UB suffix (NOT " B1" / " B2")
            string baseMark = BarData.FormatMark(UBDiameter, posNr, spacing, 2);
            distBar.Mark            = $"{baseMark} {UBSuffix}";  // "H12-01-200 UB" or "H12-02-200 UB"
            distBar.Spacing         = effSpacing;
            distBar.Direction       = filterDirection;
            distBar.Count           = 0;
            distBar.SourceBarHandle = templateBarId.Handle.Value.ToString("X8");
            if (adjStatus != 0) distBar.IsLabelManual = true;

            // Circle markers on outer end only (at slab edge)
            distBar.SymbolSide = symbolSide;

            // Map bar/dist intermediates → WCS x0/y0/x1/y1 per direction.
            double x0, y0, x1, y1;
            if (isXBars)
            {
                // UB B1 — bar along X, distribution along Y
                x0 = barLow;  x1 = barHigh;  y0 = distLow;  y1 = distHigh;
            }
            else
            {
                // UB B2 — bar along Y, distribution along X
                y0 = barLow;  y1 = barHigh;  x0 = distLow;  x1 = distHigh;
            }

            // Step 1: generate distribution block (sets distBar.BarsSpan)
            var barResult = BarBlockEngine.GenerateFromBounds(
                db, x0, y0, x1, y1, distBar, horizontal: isXBars, posNr);
            if (!barResult.IsValid) return false;

            // Step 2: leader points — dispatch per direction.
            bool   leaderUp    = false;
            bool   leaderRight = true;
            string encoded;
            if (isXBars)
            {
                // UB B1: bars horizontal, leader vertical, proximity in Y
                double firstBarY    = barResult.MinPoint.Y;
                double lastBarY     = barResult.MinPoint.Y + distBar.BarsSpan;
                double annotInsertY = y0;
                (leaderUp, encoded) = ComputeAnnotLeaderForHorizontalBars(
                    firstBarY, lastBarY, annotInsertY, slabMinAcross, slabMaxAcross);
            }
            else
            {
                // UB B2: bars vertical, leader horizontal, proximity in X
                double firstBarX    = barResult.MinPoint.X;
                double lastBarX     = barResult.MinPoint.X + distBar.BarsSpan;
                double annotInsertX = x0;
                (leaderRight, encoded) = ComputeAnnotLeaderForVerticalBars(
                    firstBarX, lastBarX, annotInsertX, slabMinAcross, slabMaxAcross);
            }
            distBar.LeaderPoints = encoded;

            // Step 3: annotation insert point — dispatch per direction.
            // (Use explicit bounds, NOT barResult.MinPoint — circle markers via SymbolSide
            // pollute GeometricExtents by ±35mm, causing dist line misalignment.)
            var annotInsertPt = isXBars
                ? new Point3d(x0 + lengthA / 2.0, y0, 0)
                : new Point3d(x0, y0 + lengthA / 2.0, 0);

            // Step 4: annotation
            var annotResult = AnnotationEngine.CreateLeader(
                db, barResult, distBar,
                leaderHorizontal: !isXBars, posNr: posNr,
                customInsertPt: annotInsertPt,
                barsHorizontal: isXBars,
                leaderRight: leaderRight,
                leaderUp: leaderUp);

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

        /// <summary>
        /// Compute annotation leader direction (right/left) and pre-set LeaderPoints
        /// for vertical bars (B2, Direction="Y"). Mirror of ComputeAnnotLeaderForHorizontalBars
        /// on X-axis. Leader points in local annotation BTR coords (X-axis, Y=0).
        /// slabMinX/slabMaxX — passed as slabMinY/slabMaxY from caller; for B2 these are X coords.
        /// </summary>
        /// <returns>(leaderRight flag for CreateLeader, encoded LeaderPoints for distBar)</returns>
        private static (bool leaderRight, string leaderPointsEncoded) ComputeAnnotLeaderForVerticalBars(
            double firstBarX_world,
            double lastBarX_world,
            double annotInsertX_world,
            double slabMinX,
            double slabMaxX)
        {
            double distFirstBarToSlabMin = firstBarX_world - slabMinX;
            double distLastBarToSlabMax  = slabMaxX - lastBarX_world;

            // Q9 analog: <= tie-break = right (positive X, backward compat rect case)
            bool leaderRight = distLastBarToSlabMax <= distFirstBarToSlabMin;

            double armEndX_world = leaderRight
                ? slabMaxX + LeaderArmExtension
                : slabMinX - LeaderArmExtension;

            double armEndX_local = armEndX_world - annotInsertX_world;

            string encoded = AnnotationEngine.EncodeLeaderPoints(new List<Point3d>
            {
                new Point3d(0,             0, 0),
                new Point3d(armEndX_local, 0, 0),
            });

            return (leaderRight, encoded);
        }

        /// <summary>
        /// Compute spacing for ContinuousInternal mode — force last bar at y1
        /// (= yHigh - spacing/2) so gap to next strip's first bar equals nominalSpacing.
        /// Q11=a deviation threshold ±15%, Q12=i pick count closer to nominal.
        ///
        /// Returns (effectiveSpacing, status):
        ///   status 0 = no adjustment needed (nominal works exactly OR single bar)
        ///   status 1 = continuous mode applied (deviation ≤ 15%, silent OK)
        ///   status 2 = deviation > threshold, fallback to nominal (gap warn)
        /// </summary>
        private static (double effectiveSpacing, int status) ComputeContinuousSpacing(
            double availableSpan, double nominalSpacing)
        {
            if (availableSpan <= 0) return (nominalSpacing, 0);  // degenerate

            int nominalCount = (int)Math.Floor(availableSpan / nominalSpacing) + 1;
            if (nominalCount < 2) return (nominalSpacing, 0);    // only 1 bar fits

            // If nominal spacing already lands last bar exactly at y1 → no change
            double nominalLastBarRel = (nominalCount - 1) * nominalSpacing;
            if (Math.Abs(nominalLastBarRel - availableSpan) < 0.5)
                return (nominalSpacing, 0);

            // Try count = nominalCount and nominalCount+1
            double effSpacing1 = availableSpan / (nominalCount - 1);  // sparser
            double effSpacing2 = availableSpan / nominalCount;         // denser

            double delta1 = Math.Abs(effSpacing1 - nominalSpacing);
            double delta2 = Math.Abs(effSpacing2 - nominalSpacing);

            double chosen    = (delta1 <= delta2) ? effSpacing1 : effSpacing2;
            double deviation = Math.Abs(chosen - nominalSpacing) / nominalSpacing;

            if (deviation > 0.15) return (nominalSpacing, 2);  // fallback + warn
            return (chosen, 1);                                  // continuous applied
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
