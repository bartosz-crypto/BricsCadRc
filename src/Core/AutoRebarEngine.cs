using System;
using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Auto-generates RC_BAR_BLOCK distributions on a slab from RC_SINGLE_BAR
    /// templates stored inside a rebar_X rectangle.
    ///
    /// Workflow:
    ///   1. User clicks a slab polyline (layer SD-PILED-RAFT)
    ///   2. Engine finds the nearest rebar_bottom / rebar_top rectangle
    ///   3. Engine collects RC_SINGLE_BAR templates inside that rectangle's bbox
    ///   4. Engine filters by inferred direction (from polyline geometry, not bar.Direction XData)
    ///   5. Per template: GenerateFromBounds on cover-inset slab bbox + CreateLeader
    ///
    /// Etap 1: axis-aligned slabs only (rotated slabs → warning + abort).
    /// </summary>
    public static class AutoRebarEngine
    {
        public const double DefaultSpacing = 200.0;
        public const double DefaultCover   = 40.0;

        // ----------------------------------------------------------------
        // Public entry point
        // ----------------------------------------------------------------

        /// <summary>
        /// Generates all distributions for one layer code (B1/B2/T1/T2) on a slab.
        /// </summary>
        /// <param name="doc">Active document</param>
        /// <param name="slabPolyId">ObjectId of the slab boundary polyline</param>
        /// <param name="sourceLayer">"rebar_bottom" or "rebar_top"</param>
        /// <param name="filterDirection">"X" (horizontal) or "Y" (vertical)</param>
        /// <param name="layerCode">"B1" / "B2" / "T1" / "T2"</param>
        /// <returns>Number of distributions generated; -1 on validation error.</returns>
        public static int GenerateLayer(
            Document doc,
            ObjectId slabPolyId,
            string   sourceLayer,
            string   filterDirection,
            string   layerCode,
            double   spacing = DefaultSpacing,
            double   cover   = DefaultCover)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            // ---- Phase 1: read-only scan (single transaction) ----
            Extents3d slabBbox;
            List<(ObjectId barId, BarData bar)> templates;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (!(tr.GetObject(slabPolyId, OpenMode.ForRead) is Polyline slabPl))
                {
                    ed.WriteMessage("\nWybrana encja nie jest polilinią.\n");
                    tr.Commit();
                    return -1;
                }
                if (!slabPl.Closed)
                {
                    ed.WriteMessage("\nObrys płyty musi być zamknięty.\n");
                    tr.Commit();
                    return -1;
                }
                if (!GeometryHelper.IsAxisAlignedPolyline(slabPl))
                {
                    ed.WriteMessage("\n[AutoRebar] Etap 1 nie obsługuje płyt pod kątem (non-axis-aligned). Pomiń lub obróć do osi.\n");
                    tr.Commit();
                    return -1;
                }

                slabBbox = GeometryHelper.PolylineBbox(slabPl);
                var slabCentroid = GeometryHelper.Centroid(slabBbox);

                var rebarRects = ScanLayerPolylines(db, tr, sourceLayer);
                if (rebarRects.Count == 0)
                {
                    ed.WriteMessage($"\n[AutoRebar] Brak polilinii na warstwie '{sourceLayer}'.\n");
                    tr.Commit();
                    return -1;
                }

                var (_, rebarBbox) = FindNearestRebarRect(rebarRects, slabCentroid);
                templates = ScanTemplateBarsInBbox(db, tr, rebarBbox, filterDirection);

                tr.Commit();
            }

            if (templates.Count == 0)
            {
                ed.WriteMessage($"\n[AutoRebar] Brak prętów RC_BAR (kierunek {filterDirection}) w prostokącie '{sourceLayer}'.\n");
                return -1;
            }

            // ---- Phase 2: generate (each engine call manages its own transaction) ----
            bool   horizontal = filterDirection == "X";
            string position   = layerCode.StartsWith("B") ? "BOT" : "TOP";

            // Apply cover to slab bounds before passing to GenerateFromBounds
            // (GenerateFromBounds expects post-cover bounds, per existing caller pattern)
            double x0 = slabBbox.MinPoint.X + cover;
            double y0 = slabBbox.MinPoint.Y + cover;
            double x1 = slabBbox.MaxPoint.X - cover;
            double y1 = slabBbox.MaxPoint.Y - cover;

            if (x0 >= x1 || y0 >= y1)
            {
                ed.WriteMessage("\n[AutoRebar] Płyta za mała dla podanej otuliny.\n");
                return -1;
            }

            int generated = 0;
            foreach (var (barId, templateBar) in templates)
            {
                var distBar = new BarData
                {
                    Mark            = templateBar.Mark,
                    Diameter        = templateBar.Diameter,
                    ShapeCode       = string.IsNullOrEmpty(templateBar.ShapeCode) ? "00" : templateBar.ShapeCode,
                    Spacing         = spacing,
                    Cover           = cover,
                    LayerCode       = layerCode,
                    Position        = position,
                    Direction       = horizontal ? "X" : "Y",
                    Count           = 0,     // auto-calc inside GenerateFromBounds
                    SourceBarHandle = barId.Handle.Value.ToString("X"),
                };

                int posNr = ExtractPosNrFromMark(templateBar.Mark);
                if (posNr <= 0) posNr = 1;

                try
                {
                    var barResult = BarBlockEngine.GenerateFromBounds(
                        db, x0, y0, x1, y1, distBar, horizontal, posNr);

                    if (barResult.IsValid)
                    {
                        AnnotationEngine.CreateLeader(db, barResult, distBar, horizontal, posNr);
                        generated++;
                    }
                }
                catch (Exception ex)
                {
                    ed.WriteMessage($"\n[AutoRebar] Pominięto template {templateBar.Mark}: {ex.Message}\n");
                }
            }

            ed.WriteMessage($"\n[AutoRebar] Wygenerowano {generated} rozkładów {layerCode} na płycie.\n");
            return generated;
        }

        // ----------------------------------------------------------------
        // Private helpers
        // ----------------------------------------------------------------

        private static List<(ObjectId polyId, Extents3d bbox)> ScanLayerPolylines(
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
                if (pl.Layer != layerName) continue;
                if (pl.NumberOfVertices < 3) continue;

                result.Add((oid, GeometryHelper.PolylineBbox(pl)));
            }
            return result;
        }

        private static (ObjectId polyId, Extents3d bbox) FindNearestRebarRect(
            List<(ObjectId polyId, Extents3d bbox)> rects,
            Point3d slabCentroid)
        {
            var best    = rects[0];
            double minD = GeometryHelper.Centroid(best.bbox).DistanceTo(slabCentroid);

            for (int i = 1; i < rects.Count; i++)
            {
                double d = GeometryHelper.Centroid(rects[i].bbox).DistanceTo(slabCentroid);
                if (d < minD) { minD = d; best = rects[i]; }
            }
            return best;
        }

        /// <summary>
        /// Collects RC_SINGLE_BAR polylines whose insertion point (vertex 0) lies inside
        /// rebarBbox AND whose direction (inferred from geometry) matches filterDirection.
        /// </summary>
        private static List<(ObjectId barId, BarData bar)> ScanTemplateBarsInBbox(
            Database db, Transaction tr, Extents3d rebarBbox, string filterDirection)
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

                // Spatial filter: vertex 0 inside rebar rectangle
                var insPt = pl.GetPoint3dAt(0);
                if (!GeometryHelper.IsInsideBbox(insPt, rebarBbox)) continue;

                // Direction filter: infer from geometry — bar.Direction XData unreliable
                // for single bars created via RC_BAR (defaults to "X" regardless of orientation)
                string dir = GeometryHelper.InferDirectionFromPolyline(pl);
                if (dir != filterDirection) continue;

                result.Add((oid, bar));
            }
            return result;
        }

        /// <summary>
        /// Parses posNr from Mark: "H10-01" → 1, "H12-05-200 B1" → 5, unknown → -1.
        /// </summary>
        private static int ExtractPosNrFromMark(string mark)
        {
            if (string.IsNullOrEmpty(mark)) return -1;
            var parts = mark.Split('-');
            if (parts.Length < 2) return -1;
            return int.TryParse(parts[1], out int n) ? n : -1;
        }
    }
}
