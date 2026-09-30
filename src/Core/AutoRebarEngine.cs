using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
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
        /// <summary>
        /// Minimalna długość pręta w siatce podstawowej, gdy geometria nie wymusza krótszego
        /// (krótsze tylko gdy pas jest węższy albo bez nich nie da się ułożyć planu).
        /// </summary>
        public const double TemplatePreferredMinLen = 2500.0;
        public const double TemplateMaxLen       = 6000.0;
        public const double TemplateGridStep     = 250.0;
        public const double TemplateOffsetX      = 200.0;
        public const double TemplateOffsetY      = 500.0;
        public const double TemplateSpacingY     = 700.0;
        public const double TemplateLabelOffsetY = 200.0;

        // Automatyczna "podkładka" (strefa szablonów), gdy w rysunku nie ma prostokąta
        // na warstwie rebar_bottom: prostokąt na prawo od płyty, rośnie w dół w miarę
        // dodawania nowych prętów.
        public const double TemplateZoneGap    = 2000.0;  // odstęp od prawej krawędzi płyty
        public const double TemplateZoneWidth  = 8000.0;  // proste od lewej (≤6000+200), UB od prawej
        public const double TemplateZoneHeight = 4000.0;  // startowa wysokość (auto-rośnie)

        /// <summary>Maks. wysokość paska przy skośnej krawędzi (obrys rysowany) — długości prętów idą za skosem.</summary>
        public const double SkewBandHeight     = 1000.0;

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

        /// <summary>Preferowane pasmo zakładu dołu (wybierane przed wszystkim innym przy tej samej liczbie prętów).</summary>
        public const double OverlapPreferredMin = 450.0;
        public const double OverlapPreferredMax = 550.0;

        /// <summary>
        /// Minimalny odstęp w świetle między strefą zakładu góry (T) a strefą zakładu dołu (B)
        /// w tym samym kierunku. Zakłady góra/dół nie mogą być w tym samym miejscu.
        /// </summary>
        public const double LapStaggerMinGap = 750.0;

        // Zakład GÓRY (Ø12 — większa średnica): twardo 500–700, preferowane 550–650, cel 600.
        public const double TopOverlapMin          = 500.0;
        public const double TopOverlapMax          = 700.0;
        public const double TopOverlapPreferredMin = 550.0;
        public const double TopOverlapPreferredMax = 650.0;
        public const double TopOverlapTarget       = 600.0;

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
                ? DecomposeStrips(plan.SlabVertices, scanIsY: true)
                : DecomposeStrips(plan.SlabVertices, scanIsY: false);
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
                InitLabelOccupancy(db);

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
                    // Góra (T1/T2): zakłady przesunięte względem dołu (B1/B2) tego samego kierunku
                    // Plan wspólny dół+góra: TA SAMA liczba prętów w pasie, zakłady góry mijają dół
                    var joint    = ComputeJointPlan(xAvailable, spacing, ed);
                    var distPlan = layerCode.StartsWith("T") ? joint.top : joint.bottom;
                    if (distPlan.Count == 0)
                    {
                        ed.WriteMessage($"\n[AutoRebar] Strip scan={strip.ScanLow:F0}..{strip.ScanHigh:F0}: " +
                            $"brak rozwiązania dist plan (xAvailable={xAvailable:F0}mm) — skipped\n");
                        continue;
                    }

                    ed.WriteMessage($"\n[AutoRebar] Strip scan={strip.ScanLow:F0}..{strip.ScanHigh:F0} " +
                        $"(h={stripHeight:F0}mm, external: lower={strip.LowerIsExternal}, " +
                        $"upper={strip.UpperIsExternal}): {distPlan.Count} dist, " +
                        $"lengths: " + string.Join(",", distPlan.Select(d => $"{d.length:F0}")) +
                        (distPlan.Count > 1
                            ? $", zakład {distPlan[0].xOffset + distPlan[0].length - distPlan[1].xOffset:F0}mm"
                            : "") + "\n");

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
                            // Strefa szablonów rośnie w dół, żeby kolejne pręty były w środku
                            // (wcześniej szablony poniżej prostokąta nie były znajdowane → duplikaty).
                            plan.RebarBbox = GrowZoneToFit(db, plan.RebarRectId, templateBarId, plan.RebarBbox);
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
            ObjectId  rebarRectId = ObjectId.Null;
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
                _currentSlabHandle = CurrentSlabTagFor(slabPl);

                (rebarRectId, rebarBbox) = FindOrCreateTemplateZone(db, tr, sourceLayer, slabBbox, ed);

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

                oldUBs = ScanOldUBDistributions(db, tr, slabVertices, layerCode, filterDirection);
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
                InitLabelOccupancy(db);

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
                    rebarBbox = GrowZoneToFit(db, rebarRectId, templateBarId, rebarBbox);
                    ed.WriteMessage($"\n[AutoRebar UB] Created UB template H{UBDiameter}-{ubPosNr:D2} " +
                        $"(A={ubLengthA}, B={ubLengthB}, C={ubLengthC})\n");
                }

                // Axis dispatch: across-axis is Y dla UB B1 (vertical edges, dist along Y)
                // vs X dla UB B2 (horizontal edges, dist along X). Loop-invariant.
                double slabAcrossMin = isUBB1 ? slabBbox.MinPoint.Y : slabBbox.MinPoint.X;
                double slabAcrossMax = isUBB1 ? slabBbox.MaxPoint.Y : slabBbox.MaxPoint.X;

                // Pasy prętów głównych prostopadłych do krawędzi (UB B1 ↔ pasy B1, UB B2 ↔ pasy B2).
                // UB na krawędzi dzielimy dokładnie jak te pasy — z tymi samymi odsunięciami
                // i trybem rozstawu — żeby LICZBA I POŁOŻENIE UB = pręty dochodzące do krawędzi.
                // (Wcześniej jeden UB na całą krawędź z równym rozstawem: np. 92 UB przy 27+39+27=93 prętach.)
                var mainStrips = DecomposeStrips(slabVertices, scanIsY: isUBB1)
                    .Where(st => st.Valid).ToList();

                foreach (var (edgeCoord, segLow, segHigh, symbolSide) in validSegments)
                {
                    var parts = new List<(double lo, double hi, double lowOff, double highOff, SpacingMode mode, bool lowExt, bool highExt)>();

                    foreach (var st in mainStrips)
                    {
                        bool touchesEdge = Math.Abs(st.PerpLow - edgeCoord) < 1.0 || Math.Abs(st.PerpHigh - edgeCoord) < 1.0;
                        double lo = Math.Max(st.ScanLow, segLow), hi = Math.Min(st.ScanHigh, segHigh);
                        if (!touchesEdge || hi - lo < 1.0) continue;
                        if (st.ScanHigh - st.ScanLow < 50.0) continue;                           // B też pomija
                        if ((st.PerpHigh - st.PerpLow) - 2.0 * cover < TemplateMinLen) continue;  // B nie ma tu prętów

                        double lowOff  = st.LowerIsExternal ? cover : spacing / 2.0;
                        double highOff = st.UpperIsExternal ? cover : spacing / 2.0;
                        SpacingMode mode = st.UpperIsExternal ? SpacingMode.AdjustedExternal : SpacingMode.ContinuousInternal;

                        // Jak w GenerateLayer: wąski pas → 1 pręt w osi
                        double h = hi - lo;
                        if (h < spacing || h - lowOff - highOff <= 0)
                        {
                            lowOff = highOff = h / 2.0;
                            mode = SpacingMode.Nominal;
                        }
                        parts.Add((lo, hi, lowOff, highOff, mode, st.LowerIsExternal, st.UpperIsExternal));
                    }

                    if (parts.Count == 0)
                    {
                        // Fallback: cała krawędź jako jeden rozkład, końce wg narożnika (wypukły → otulina).
                        double inward = symbolSide == "Left" ? 50.0 : -50.0;
                        Teigha.Geometry.Point2d pLowOut = isUBB1
                            ? new Teigha.Geometry.Point2d(edgeCoord + inward, segLow - 0.5)
                            : new Teigha.Geometry.Point2d(segLow - 0.5, edgeCoord + inward);
                        Teigha.Geometry.Point2d pHighOut = isUBB1
                            ? new Teigha.Geometry.Point2d(edgeCoord + inward, segHigh + 0.5)
                            : new Teigha.Geometry.Point2d(segHigh + 0.5, edgeCoord + inward);
                        bool lowExt  = !GeometryHelper.IsPointInsidePolygon(slabVertices, pLowOut);
                        bool highExt = !GeometryHelper.IsPointInsidePolygon(slabVertices, pHighOut);
                        parts.Add((segLow, segHigh,
                                   lowExt ? cover : spacing / 2.0, highExt ? cover : spacing / 2.0,
                                   highExt ? SpacingMode.AdjustedExternal : SpacingMode.ContinuousInternal,
                                   lowExt, highExt));
                    }

                    // JEDEN rozkład UB na krawędź (jedna pozycja, jeden opis), ale liczba UB =
                    // suma prętów głównych dochodzących do krawędzi. Liczba prętów w każdej części
                    // liczona tym samym algorytmem co w GenerateLayer; UB rozłożone równo
                    // od pierwszego do ostatniego pręta głównego.
                    parts.Sort((a, b) => a.lo.CompareTo(b.lo));
                    int totalCount = 0;
                    foreach (var part in parts)
                    {
                        if (part.mode == SpacingMode.Nominal) { totalCount += 1; continue; }
                        double avail = (part.hi - part.highOff) - (part.lo + part.lowOff);
                        double eff = spacing;
                        if (part.mode == SpacingMode.AdjustedExternal)
                            eff = ComputeAdjustedSpacing(avail, spacing, part.lowOff, part.hi - part.lo).Item1;
                        else if (part.mode == SpacingMode.ContinuousInternal)
                            eff = ComputeContinuousSpacing(avail, spacing).Item1;
                        totalCount += eff > 0 ? (int)(avail / eff + 1e-9) + 1 : 1;
                    }

                    double distLow  = parts[0].lo + parts[0].lowOff;
                    double distHigh = parts[parts.Count - 1].hi - parts[parts.Count - 1].highOff;
                    double uSpacing = totalCount > 1 && distHigh > distLow
                        ? (distHigh - distLow) / (totalCount - 1)
                        : spacing;
                    if (totalCount <= 1) distHigh = distLow;

                    ed.WriteMessage($"\n[AutoRebar UB] Edge={edgeCoord:F0} [{segLow:F0}..{segHigh:F0}] " +
                        $"{parts.Count} pas(y) prętów głównych → {totalCount} UB, rozstaw {uSpacing:F1}mm\n");

                    try
                    {
                        bool ok = GenerateUBDistribution(
                            db, edgeCoord, distLow, distHigh, 0.0, 0.0,
                            templateBarId, templateBar,
                            ubLengthA, ubLengthB, ubLengthC, spacing, layerCode, symbolSide,
                            SpacingMode.Nominal,
                            slabAcrossMin, slabAcrossMax,
                            ubPosNr, ubShapeCode, filterDirection,
                            forcedSpacing: uSpacing);
                        if (ok) generated++;
                    }
                    catch (System.Exception ex)
                    {
                        Log.Error($"AutoRebar UB edge={edgeCoord:F0}", ex);
                        ed.WriteMessage($"\n*** ERROR *** [AutoRebar UB] Edge={edgeCoord:F0} failed: {ex.Message}\n");
                    }
                }
            }

            ed.WriteMessage(
                $"\n[AutoRebar UB] Wygenerowano {generated} rozkładów UB {layerCode} na {validSegments.Count} krawędziach " +
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
            public ObjectId                              RebarRectId;
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

            // 3. Find nearest rebar_X rect — albo utwórz automatyczną strefę szablonów
            var (rebarRectId, rebarBbox) = FindOrCreateTemplateZone(db, tr, sourceLayer, slabBbox, ed);

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
            _currentSlabHandle = CurrentSlabTagFor(slabPl);
            var oldDists = ScanOldDistributions(db, tr, slabVertices, layerCode);

            return new Phase1Result
            {
                SlabBbox                 = slabBbox,
                RebarBbox                = rebarBbox,
                RebarRectId              = rebarRectId,
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

        /// <summary>
        /// Podział płyty na pasy po PRAWDZIWYM obrysie (zastępuje DecomposeIntoY/XStrips).
        ///
        /// Pracuje w "przestrzeni skanu": scan = oś prostopadła do prętów (B1: Y, B2: X),
        /// perp = oś wzdłuż prętów. Dla B2 współrzędne wierzchołków są zamieniane (x↔y).
        ///
        /// Zmiany względem starej wersji:
        ///   • Wiele odcinków w jednym pasie (płyty U/C/T) — każdy odcinek to osobny pas
        ///     (wcześniej cały pas był pomijany: "Multi-span ... future").
        ///   • Krawędź zewnętrzna = tuż za końcem pasa NIE ma płyty na choćby części szerokości
        ///     (test punktów po obrysie). Wcześniej: tylko gdy koniec leżał na bboxie płyty,
        ///     więc uskoki płyt L/T były traktowane jak cięcie wewnętrzne (pręt 100 mm od krawędzi).
        ///     Zasada "choćby części" jest bezpieczna: na styku z sąsiednim pasem daje gęściej, nie rzadziej.
        ///   • Skośne krawędzie (obrys rysowany): długość pręta = część wspólna szerokości na dole,
        ///     w środku i na górze pasa → pręty nigdy nie wychodzą poza płytę.
        /// </summary>
        private static List<StripBounds> DecomposeStrips(List<Point2d> vertices, bool scanIsY)
        {
            var result = new List<StripBounds>();
            if (vertices == null || vertices.Count < 3) return result;

            // Przestrzeń skanu: X = perp (wzdłuż prętów), Y = scan
            var pts = scanIsY ? vertices : vertices.Select(v => new Point2d(v.Y, v.X)).ToList();

            // Unikalne współrzędne scan (eps 1e-3)
            var raw = pts.Select(v => v.Y).OrderBy(y => y).ToList();
            var coords = new List<double>();
            foreach (double y in raw)
                if (coords.Count == 0 || Math.Abs(y - coords[coords.Count - 1]) >= 1e-3)
                    coords.Add(y);
            if (coords.Count < 2) return result;

            // Odrzuć wierzchołki "fantomowe" (np. współliniowe) — ten sam układ przecięć pod i nad
            var cuts = new List<double> { coords[0] };
            for (int i = 1; i < coords.Count - 1; i++)
            {
                var below = GeometryHelper.FindIntersectionsH(pts, coords[i] - 0.5);
                var above = GeometryHelper.FindIntersectionsH(pts, coords[i] + 0.5);
                if (!XIntersectionsEqual(below, above)) cuts.Add(coords[i]);
            }
            cuts.Add(coords[coords.Count - 1]);

            // Pasy między cięciami; pasy ze skośną krawędzią dzielimy na paski ≤ SkewBandHeight,
            // żeby długości prętów podążały za skosem (każdy pasek = osobny rozkład).
            var bands = new List<(double lo, double hi)>();
            for (int i = 0; i < cuts.Count - 1; i++)
            {
                double lo = cuts[i], hi = cuts[i + 1];
                double pr = Math.Min(0.5, (hi - lo) * 0.25);
                var a = GeometryHelper.FindIntersectionsH(pts, lo + pr);
                var b = GeometryHelper.FindIntersectionsH(pts, hi - pr);
                bool skewed = a.Count == b.Count && a.Zip(b, (u, w) => Math.Abs(u - w)).Any(d => d > 1.0);
                int nSub = skewed ? Math.Max(1, (int)Math.Ceiling((hi - lo) / SkewBandHeight)) : 1;
                for (int k = 0; k < nSub; k++)
                    bands.Add((lo + (hi - lo) * k / nSub, lo + (hi - lo) * (k + 1) / nSub));
            }

            foreach (var (lo, hi) in bands)
            {
                double h     = hi - lo;
                double probe = Math.Min(0.5, h * 0.25);

                var xsMid = GeometryHelper.FindIntersectionsH(pts, (lo + hi) * 0.5 + 0.1);
                var xsLo  = GeometryHelper.FindIntersectionsH(pts, lo + probe);
                var xsHi  = GeometryHelper.FindIntersectionsH(pts, hi - probe);

                if (xsMid.Count < 2 || xsMid.Count % 2 != 0)
                {
                    result.Add(new StripBounds
                    {
                        ScanLow = lo, ScanHigh = hi, Valid = false,
                        SkipReason = $"Degenerate strip ({xsMid.Count} intersections)",
                    });
                    continue;
                }

                bool sameTopology = xsLo.Count == xsMid.Count && xsHi.Count == xsMid.Count;

                for (int k = 0; k + 1 < xsMid.Count; k += 2)
                {
                    double pLo = xsMid[k], pHi = xsMid[k + 1];
                    if (sameTopology)
                    {
                        // Skos: część wspólna dołu/środka/góry paska (pręty nie wyjdą poza płytę)
                        pLo = Math.Max(pLo, Math.Max(xsLo[k],     xsHi[k]));
                        pHi = Math.Min(pHi, Math.Min(xsLo[k + 1], xsHi[k + 1]));
                    }
                    if (pHi - pLo < 1.0)
                    {
                        result.Add(new StripBounds
                        {
                            ScanLow = lo, ScanHigh = hi, Valid = false,
                            SkipReason = "Strip too narrow after skew clipping",
                        });
                        continue;
                    }

                    result.Add(new StripBounds
                    {
                        ScanLow         = lo,
                        ScanHigh        = hi,
                        PerpLow         = pLo,
                        PerpHigh        = pHi,
                        LowerIsExternal = IsBoundaryExternal(pts, pLo, pHi, lo - 0.5),
                        UpperIsExternal = IsBoundaryExternal(pts, pLo, pHi, hi + 0.5),
                        Valid           = true,
                    });
                }
            }
            return result;
        }

        /// <summary>
        /// Czy za końcem pasa (linia scan = <paramref name="scanProbe"/>) choć na części zakresu
        /// [pLo, pHi] nie ma płyty → koniec pasa to krawędź płyty (otulina, reguła 70 mm).
        /// </summary>
        private static bool IsBoundaryExternal(List<Point2d> pts, double pLo, double pHi, double scanProbe)
        {
            double width = pHi - pLo;
            int    n     = Math.Max(4, Math.Min(400, (int)Math.Ceiling(width / 50.0)));
            for (int j = 0; j <= n; j++)
            {
                // próbki od pLo+1 do pHi-1 (bez samych narożników)
                double x = pLo + 1.0 + (width - 2.0) * j / n;
                if (!GeometryHelper.IsPointInsidePolygon(pts, new Point2d(x, scanProbe)))
                    return true;
            }
            return false;
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

        /// <summary>
        /// Najbliższy prostokąt szablonów na warstwie <paramref name="layer"/>; gdy nie ma żadnego —
        /// tworzy automatyczną strefę szablonów na prawo od płyty (użytkownik nie musi
        /// przygotowywać "podkładki" z prętami).
        /// </summary>
        private static (ObjectId id, Extents3d bbox) FindOrCreateTemplateZone(
            Database db, Transaction tr, string layer, Extents3d slabBbox, Editor ed)
        {
            var rects = ScanLayerRectangles(db, tr, layer);
            if (rects.Count > 0)
                return FindNearestRect(rects, GeometryHelper.Centroid(slabBbox));

            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(layer))
            {
                lt.UpgradeOpen();
                var ltr = new LayerTableRecord { Name = layer };
                lt.Add(ltr);
                tr.AddNewlyCreatedDBObject(ltr, true);
            }

            // Strefa góry (rebar_top) w drugiej kolumnie, obok strefy dołu
            int    col  = layer == "rebar_top" ? 1 : 0;
            double x0   = slabBbox.MaxPoint.X + TemplateZoneGap + col * (TemplateZoneWidth + TemplateZoneGap);
            double yTop = slabBbox.MaxPoint.Y;
            var pl = new Polyline(4);
            pl.AddVertexAt(0, new Point2d(x0,                     yTop),                      0, 0, 0);
            pl.AddVertexAt(1, new Point2d(x0 + TemplateZoneWidth, yTop),                      0, 0, 0);
            pl.AddVertexAt(2, new Point2d(x0 + TemplateZoneWidth, yTop - TemplateZoneHeight), 0, 0, 0);
            pl.AddVertexAt(3, new Point2d(x0,                     yTop - TemplateZoneHeight), 0, 0, 0);
            pl.Closed = true;
            pl.Layer  = layer;

            var ms = (BlockTableRecord)tr.GetObject(
                SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            var id = ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);

            ed?.WriteMessage($"\n[AutoRebar] Brak prostokąta '{layer}' — utworzono strefę szablonów " +
                             $"na prawo od płyty (X={x0:F0}).\n");
            return (id, GeometryHelper.PolylineBbox(pl));
        }

        /// <summary>
        /// Powiększa prostokąt strefy szablonów w dół, jeśli nowy pręt (z zapasem na kolejny rząd)
        /// wychodzi poza jego dolną krawędź. Zwraca aktualny bbox strefy.
        /// </summary>
        private static Extents3d GrowZoneToFit(Database db, ObjectId rectId, ObjectId barId, Extents3d zone)
        {
            if (rectId.IsNull || barId.IsNull || rectId.IsErased) return zone;
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var bar = tr.GetObject(barId, OpenMode.ForRead) as Entity;
                if (bar != null)
                {
                    double needMinY = bar.GeometricExtents.MinPoint.Y - TemplateSpacingY;
                    if (needMinY < zone.MinPoint.Y
                        && tr.GetObject(rectId, OpenMode.ForWrite) is Polyline pl)
                    {
                        double oldMin = zone.MinPoint.Y;
                        for (int i = 0; i < pl.NumberOfVertices; i++)
                        {
                            var p = pl.GetPoint2dAt(i);
                            if (Math.Abs(p.Y - oldMin) < 1e-3)
                                pl.SetPointAt(i, new Point2d(p.X, needMinY));
                        }
                        zone = GeometryHelper.PolylineBbox(pl);
                    }
                }
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.GrowZoneToFit", ex); }
            return zone;
        }

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
                // Tylko pręty proste — gięty pręt o tej samej długości A nie może być szablonem B1/B2
                if ((bar.ShapeCode ?? "00") != "00") continue;
                var insPt = pl.GetPoint3dAt(0);
                if (!GeometryHelper.IsInsideBbox(insPt, rebarBbox)) continue;
                // Etap 1E: usunięto filter InferDirectionFromPolyline — B1/B2 share template pool.
                // Match po diameter + length (downstream), Direction irrelevant for elev template.
                result.Add((oid, bar));
            }
            return result;
        }

        // ── Przynależność rozkładu do płyty ─────────────────────────────────
        // Każdy rozkład AutoRebar dostaje XData RC_AUTOREBAR_SLAB z handle obrysu płyty.
        // Przy ponownym uruchomieniu kasujemy tylko rozkłady tej płyty (także przesunięte),
        // a nie wszystko, co ma punkt wstawienia w bboxie (sąsiednie płyty we wcięciu L/U).
        // Rozkłady bez znacznika (stare / z obrysu rysowanego): test punktu w obrysie.
        private const string XSlabApp = "RC_AUTOREBAR_SLAB";
        private static string _currentSlabHandle;   // null = obrys rysowany (tymczasowy)

        private static string CurrentSlabTagFor(Polyline slabPl)
            => slabPl.Layer == "RC-TEMP-OUTLINE" ? null : slabPl.Handle.ToString();

        private static string ReadSlabTag(Entity ent)
        {
            var rb = ent.GetXDataForApplication(XSlabApp);
            if (rb == null) return null;
            foreach (var tv in rb.AsArray())
                if (tv.TypeCode == (int)DxfCode.ExtendedDataAsciiString || tv.TypeCode == (int)DxfCode.ExtendedDataHandle)
                {
                    string h = XLink.Read(tv);
                    return string.IsNullOrEmpty(h) ? null : h;
                }
            return null;
        }

        private static bool SameHandle(string a, string b)
            => long.TryParse(a, System.Globalization.NumberStyles.HexNumber, null, out long x)
            && long.TryParse(b, System.Globalization.NumberStyles.HexNumber, null, out long y)
            && x == y;

        private static bool BelongsToCurrentSlab(BlockReference br, List<Point2d> slabVertices)
        {
            string tag = ReadSlabTag(br);
            if (tag != null && _currentSlabHandle != null)
                return SameHandle(tag, _currentSlabHandle);
            return GeometryHelper.IsPointInsidePolygon(
                slabVertices, new Point2d(br.Position.X, br.Position.Y));
        }

        private static void TagWithSlab(Database db, ObjectId blockRefId)
        {
            if (_currentSlabHandle == null || blockRefId.IsNull) return;
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                if (!rat.Has(XSlabApp))
                {
                    rat.UpgradeOpen();
                    var rec = new RegAppTableRecord { Name = XSlabApp };
                    rat.Add(rec);
                    tr.AddNewlyCreatedDBObject(rec, true);
                }
                var ent = tr.GetObject(blockRefId, OpenMode.ForWrite) as Entity;
                if (ent != null)
                    ent.XData = new ResultBuffer(
                        new TypedValue((int)DxfCode.ExtendedDataRegAppName, XSlabApp),
                        XLink.Write(_currentSlabHandle));   // 1005: COPY płyty z rozkładami przemapuje znacznik
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.TagWithSlab", ex); }
        }

        private static List<(ObjectId distId, ObjectId annotId)> ScanOldDistributions(
            Database db, Transaction tr, List<Point2d> slabVertices, string layerCode)
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
                if (!BelongsToCurrentSlab(br, slabVertices)) continue;

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
            Database db, Transaction tr, List<Point2d> slabVertices, string layerCode, string filterDirection)
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
                if (!BelongsToCurrentSlab(br, slabVertices)) continue;

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
            int posNr   = PositionCounter.NextAutoFree(usedNrs);   // 01/02 zarezerwowane dla UB

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
                        : $"*** WARNING *** [AutoRebar] ContInt spacing {effectiveSpacing:F1}mm — zagęszczenie > 15% (wąski pas). Label nominal {spacing:F0}mm.";
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
            TagWithSlab(db, barResult.BlockRefId);

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

            // Step 4: annotation (z odsunięciem opisu, jeśli koliduje z istniejącymi)
            distBar.LeaderPoints = AvoidLabelCollision(
                distBar.LeaderPoints, annotInsertPt, $"{distBar.EffectiveCount} {distBar.Mark}", distBar.AnnotScale);
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
            int posNr, string shapeCode, string filterDirection,
            double? forcedSpacing = null)
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
                    if (forcedSpacing.HasValue && forcedSpacing.Value > 0)
                    {
                        effSpacing = forcedSpacing.Value;   // opis nadal nominalny (-200)
                        if (Math.Abs(effSpacing - spacing) > 0.5) adjStatus = 1;
                    }
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
            if (adjStatus != 0 && ed != null && spacingMode != SpacingMode.Nominal)
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
            TagWithSlab(db, barResult.BlockRefId);

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

            // Step 4: annotation (z odsunięciem opisu, jeśli koliduje z istniejącymi)
            distBar.LeaderPoints = AvoidLabelCollision(
                distBar.LeaderPoints, annotInsertPt, $"{distBar.EffectiveCount} {distBar.Mark}", distBar.AnnotScale);
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

        // ── Kolizje opisów ───────────────────────────────────────────────────
        // Prostokąty zajęte przez teksty opisów (WCS). Inicjalizowane z istniejących annotacji
        // w rysunku, uzupełniane o każdy nowy opis. Gdy nowy opis nachodzi na zajęty prostokąt,
        // ramię leadera jest wydłużane "po drabince" (o długość tekstu + odstęp), aż będzie wolne.
        private static readonly List<(double x0, double y0, double x1, double y1)> _labelRects =
            new List<(double, double, double, double)>();
        private const double LabelGap = 150.0;

        private static void InitLabelOccupancy(Database db)
        {
            _labelRects.Clear();
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    if (!AnnotationEngine.IsAnnotation(br)) continue;
                    var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    foreach (ObjectId eid in btr)
                    {
                        if (eid.IsErased || !(tr.GetObject(eid, OpenMode.ForRead) is DBText t)) continue;
                        Extents3d ext;
                        try { ext = t.GeometricExtents; } catch { continue; }
                        var a = ext.MinPoint.TransformBy(br.BlockTransform);
                        var b = ext.MaxPoint.TransformBy(br.BlockTransform);
                        _labelRects.Add((Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                                         Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
                    }
                }
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.InitLabelOccupancy", ex); }
        }

        private static bool LabelOverlaps((double x0, double y0, double x1, double y1) r)
        {
            foreach (var o in _labelRects)
                if (r.x0 < o.x1 + LabelGap && r.x1 > o.x0 - LabelGap &&
                    r.y0 < o.y1 + LabelGap && r.y1 > o.y0 - LabelGap)
                    return true;
            return false;
        }

        /// <summary>
        /// Wydłuża ostatni segment leadera, aż prostokąt tekstu nie koliduje z innymi opisami.
        /// Punkty leadera są lokalne względem annotInsertPt (bloki annotacji nieobrócone).
        /// </summary>
        private static string AvoidLabelCollision(string encoded, Point3d annotInsertPt, string text, double annotScale)
        {
            var pts = AnnotationEngine.DecodeLeaderPoints(encoded);
            if (pts.Count < 2) return encoded;

            double sc      = annotScale > 0 ? annotScale : 1.0;
            double h       = AnnotationEngine.DefaultTextHeight * sc;
            double off     = AnnotationEngine.TextArmOffset * sc;
            double textLen = (text?.Length ?? 10) * AnnotationEngine.TextCharWidth * sc + off;

            var dir = pts[pts.Count - 1] - pts[pts.Count - 2];
            if (dir.Length < 1e-6) return encoded;
            dir = dir.GetNormal();
            var perp = new Vector3d(-dir.Y, dir.X, 0);
            double cross = off + h;

            (double, double, double, double) RectFor(Point3d endLocal)
            {
                var e = new Point3d(annotInsertPt.X + endLocal.X, annotInsertPt.Y + endLocal.Y, 0);
                var s0 = e - dir * textLen;
                var c = new[] { e + perp * cross, e - perp * cross, s0 + perp * cross, s0 - perp * cross };
                return (c.Min(p => p.X), c.Min(p => p.Y), c.Max(p => p.X), c.Max(p => p.Y));
            }

            var last = pts[pts.Count - 1];
            var rect = RectFor(last);
            for (int k = 0; k < 12 && LabelOverlaps(rect); k++)
            {
                last = last + dir * (textLen + LabelGap);
                rect = RectFor(last);
            }
            _labelRects.Add(rect);
            pts[pts.Count - 1] = last;
            return AnnotationEngine.EncodeLeaderPoints(pts);
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

        // ── Plan góry (T1/T2): zakłady przesunięte względem dołu ─────────────
        private static List<(double lo, double hi)> LapZones(List<(double xOffset, double length)> plan)
        {
            var sorted = plan.OrderBy(p => p.xOffset).ToList();
            var z = new List<(double, double)>();
            for (int i = 0; i + 1 < sorted.Count; i++)
                z.Add((sorted[i + 1].xOffset, sorted[i].xOffset + sorted[i].length));
            return z;
        }

        /// <summary>
        /// Wspólny plan dołu i góry dla pasa o danej szerokości (deterministyczny — B1 i T1
        /// liczone osobno, na osobnych rzutach, dostają spójne plany):
        ///   • ta sama liczba prętów w pasie dołem i górą,
        ///   • zakład dołu 400–650 (pref. 450–550), góry 500–700 (pref. 550–650),
        ///   • strefy zakładów góry ≥ LapStaggerMinGap w świetle od stref dołu,
        ///   • najpierw minimalna liczba prętów, pręty ≥ 2500 (1250 tylko gdy konieczne).
        /// Każdy pręt może mieć inną długość na siatce 250 (wzór: pierwszy, środkowe równe, ostatni).
        /// </summary>
        private static (List<(double xOffset, double length)> bottom, List<(double xOffset, double length)> top)
            ComputeJointPlan(double available, double spacing, Editor ed)
        {
            if (available <= TemplateMaxLen + 0.5)
            {
                var single = ComputeDistributionPlan(available, spacing);
                return (single, single);
            }

            int n0 = Math.Max(2, Math.Max(
                (int)Math.Ceiling((available - OverlapMin)    / (TemplateMaxLen - OverlapMin)),
                (int)Math.Ceiling((available - TopOverlapMin) / (TemplateMaxLen - TopOverlapMin))));

            for (int N = n0; N <= n0 + 5; N++)
                foreach (double minLen in new[] { TemplatePreferredMinLen, TemplateMinLen })
                {
                    var B = PlanCandidates(available, N, minLen, OverlapMin, OverlapMax,
                                           OverlapPreferredMin, OverlapPreferredMax, OverlapTarget);
                    if (B.Count == 0) continue;
                    var T = PlanCandidates(available, N, minLen, TopOverlapMin, TopOverlapMax,
                                           TopOverlapPreferredMin, TopOverlapPreferredMax, TopOverlapTarget);
                    if (T.Count == 0) continue;

                    bool found = false;
                    (int, int, double) bestKey = default;
                    List<(double, double)> bestB = null, bestT = null;

                    foreach (var b in B)
                    {
                        var bz = LapZones(b.plan);
                        foreach (var t in T)   // T posortowane — pierwszy pasujący jest najlepszy dla tego b
                        {
                            if (!LapsStaggered(LapZones(t.plan), bz)) continue;
                            var key = (b.key.outBand + t.key.outBand,
                                       b.key.distinct + t.key.distinct,
                                       b.key.dO + t.key.dO);
                            if (!found || key.CompareTo(bestKey) < 0)
                            { found = true; bestKey = key; bestB = b.plan; bestT = t.plan; }
                            break;
                        }
                    }
                    if (found) return (bestB, bestT);
                }

            ed?.WriteMessage($"\n*** WARNING *** [AutoRebar] Pas {available:F0}mm: nie znaleziono wspólnego planu " +
                             "dół/góra — góra jak dół (zakłady w tym samym miejscu!).\n");
            var fallback = ComputeDistributionPlan(available, spacing);
            return (fallback, fallback);
        }

        private static bool LapsStaggered(List<(double lo, double hi)> top, List<(double lo, double hi)> bottom)
        {
            foreach (var t in top)
                foreach (var b in bottom)
                    if (Math.Max(t.lo - b.hi, b.lo - t.hi) < LapStaggerMinGap - 1e-6) return false;
            return true;
        }

        /// <summary>Wszystkie plany N prętów (pierwszy, środkowe równe, ostatni) z zakładem w [oMin, oMax], posortowane wg jakości.</summary>
        private static List<((int outBand, int distinct, double dO) key, List<(double xOffset, double length)> plan)>
            PlanCandidates(double available, int N, double minLen,
                           double oMin, double oMax, double pMin, double pMax, double target)
        {
            var result = new List<((int, int, double), List<(double, double)>)>();
            if (N < 2) return result;
            var grid = new List<double>();
            for (double L = minLen; L <= TemplateMaxLen + 1e-6; L += TemplateGridStep) grid.Add(L);
            var middle = N >= 3 ? grid : new List<double> { 0 };

            foreach (double L0 in grid)
            foreach (double L1 in middle)
            foreach (double L2 in grid)
            {
                var lens = new List<double> { L0 };
                for (int i = 0; i < N - 2; i++) lens.Add(L1);
                lens.Add(L2);

                double O = (lens.Sum() - available) / (N - 1);
                if (O < oMin - 1e-6 || O > oMax + 1e-6) continue;

                var plan = new List<(double, double)>();
                double x = 0;
                foreach (double L in lens) { plan.Add((x, L)); x += L - O; }

                int outBand = (O >= pMin - 1e-6 && O <= pMax + 1e-6) ? 0 : 1;
                result.Add(((outBand, lens.Distinct().Count() - 1, Math.Abs(O - target)), plan));
            }
            result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return result;
        }

        /// <summary>Plan o minimalnej liczbie zakładów z prętami ≥ minLen (null gdy brak rozwiązania).</summary>
        private static List<(double xOffset, double length)> TryPlanWithMinLength(
            double available, int N_min, int N_max, double minLen)
        {
            for (int N = N_min; N <= N_max; N++)
            {
                bool   found  = false;
                double bestL1 = 0, bestL2 = 0, bestO = 0;
                (int outBand, int distinct, double dO, double negL1) bestKey = (int.MaxValue, int.MaxValue, double.MaxValue, 0);

                for (double L1 = TemplateMaxLen; L1 >= minLen - 1e-6; L1 -= TemplateGridStep)
                {
                    for (double L2 = minLen; L2 <= L1 + 1e-6; L2 += TemplateGridStep)
                    {
                        double O = ((N - 1) * L1 + L2 - available) / (N - 1);
                        if (O < OverlapMin - 1e-6 || O > OverlapMax + 1e-6) continue;

                        int outBand = (O >= OverlapPreferredMin - 1e-6 && O <= OverlapPreferredMax + 1e-6) ? 0 : 1;
                        var key = (outBand, Math.Abs(L1 - L2) < 1e-6 ? 0 : 1, Math.Abs(O - OverlapTarget), -L1);
                        if (!found || key.CompareTo(bestKey) < 0)
                        {
                            found = true; bestKey = key;
                            bestL1 = L1; bestL2 = L2; bestO = O;
                        }
                    }
                }

                if (found)
                {
                    var result = new List<(double, double)>();
                    for (int i = 0; i < N - 1; i++)
                        result.Add((i * (bestL1 - bestO), bestL1));
                    result.Add(((N - 1) * (bestL1 - bestO), bestL2));
                    return result;
                }
            }

            return null;
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

            // Tylko ZAGĘSZCZANIE: rozstaw nigdy nie może być większy niż projektowy
            // (wcześniej przy remisie wybierany był rzadszy, do +15%, np. 230 mm przy opisie -200).
            int    intervals = (int)Math.Ceiling(availableSpan / nominalSpacing - 1e-9);
            if (intervals < 1) intervals = 1;
            double denser    = availableSpan / intervals;          // ≤ nominalSpacing
            double deviation = (nominalSpacing - denser) / nominalSpacing;

            // status 2 = mocne zagęszczenie (> 15%) — dopuszczamy, ale z ostrzeżeniem
            return (denser, deviation > 0.15 ? 2 : 1);
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
            // available = N*L - (N-1)*O. Największe pokrycie przy L=6000 i NAJMNIEJSZYM zakładzie:
            //   N*6000 - (N-1)*400 >= available  →  N >= (available - 400) / (6000 - 400)
            // (wcześniej użyty był OverlapMax=650 → np. 11500 mm dawało 3-4 pręty zamiast 2×6000)
            int N_min = (int)Math.Ceiling((available - OverlapMin) / (TemplateMaxLen - OverlapMin));
            if (N_min < 2) N_min = 2;

            // Hard upper bound (prevent runaway)
            int N_max = (int)Math.Ceiling((available - OverlapMin) / (TemplateMinLen - OverlapMin)) + 1;

            // Minimalna liczba zakładów (decyzja: mniej zakładów > równe długości).
            // Dla każdego N od najmniejszego szukamy pary długości na siatce 250:
            //   N-1 prętów o długości L1 + ostatni L2 (L2 ≤ L1), zakład O w [400, 650]:
            //   available = (N-1)·L1 + L2 - (N-1)·O
            // Kolejność preferencji przy tym samym N:
            //   1) zakład w preferowanym paśmie 450–550 (twarde granice nadal 400–650),
            //   2) wszystkie pręty równe (mniej pozycji w BBS),
            //   3) zakład najbliższy 500, 4) dłuższe pręty.
            // Pierwsze N z rozwiązaniem wygrywa (minimalna liczba zakładów).
            // Najpierw pręty ≥ 2500 (siatka podstawowa bez krótkich prętów), dopiero gdy się nie da —
            // dopuszczamy 1250 (wymusza to geometria).
            foreach (double minLen in new[] { TemplatePreferredMinLen, TemplateMinLen })
            {
                var plan = TryPlanWithMinLength(available, N_min, N_max, minLen);
                if (plan != null) return plan;
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
