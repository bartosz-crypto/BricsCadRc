using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Pure geometry helpers — point-in-bbox, polyline bbox/orientation/axis-alignment.
    /// No transactions, no DB access — operates on already-resolved entities.
    /// </summary>
    public static class GeometryHelper
    {
        /// <summary>
        /// Check if point is inside axis-aligned bbox (XY only, ignores Z).
        /// </summary>
        public static bool IsInsideBbox(Point3d pt, Extents3d bbox, double tol = 1e-3)
        {
            return pt.X >= bbox.MinPoint.X - tol && pt.X <= bbox.MaxPoint.X + tol
                && pt.Y >= bbox.MinPoint.Y - tol && pt.Y <= bbox.MaxPoint.Y + tol;
        }

        /// <summary>
        /// Compute XY bbox of polyline by iterating vertices.
        /// (GeometricExtents may be invalid for newly created entities.)
        /// </summary>
        public static Extents3d PolylineBbox(Polyline pl)
        {
            if (pl == null || pl.NumberOfVertices < 1)
                throw new ArgumentException("Empty polyline");

            double xMin = double.MaxValue, yMin = double.MaxValue;
            double xMax = double.MinValue, yMax = double.MinValue;
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                var p = pl.GetPoint2dAt(i);
                if (p.X < xMin) xMin = p.X;
                if (p.X > xMax) xMax = p.X;
                if (p.Y < yMin) yMin = p.Y;
                if (p.Y > yMax) yMax = p.Y;
            }
            return new Extents3d(new Point3d(xMin, yMin, 0), new Point3d(xMax, yMax, 0));
        }

        /// <summary>Centroid of axis-aligned bbox = midpoint.</summary>
        public static Point3d Centroid(Extents3d bbox)
        {
            return new Point3d(
                (bbox.MinPoint.X + bbox.MaxPoint.X) * 0.5,
                (bbox.MinPoint.Y + bbox.MaxPoint.Y) * 0.5,
                0);
        }

        /// <summary>
        /// Returns true when every segment of the polyline is purely horizontal or
        /// purely vertical (|dx| &lt; tol OR |dy| &lt; tol).
        /// </summary>
        public static bool IsAxisAlignedPolyline(Polyline pl, double tol = 1e-3)
        {
            if (pl == null || pl.NumberOfVertices < 2) return false;
            int n        = pl.NumberOfVertices;
            int segCount = pl.Closed ? n : n - 1;
            for (int i = 0; i < segCount; i++)
            {
                var a  = pl.GetPoint2dAt(i);
                var b  = pl.GetPoint2dAt((i + 1) % n);
                double dx = Math.Abs(b.X - a.X);
                double dy = Math.Abs(b.Y - a.Y);
                if (dx > tol && dy > tol) return false;
            }
            return true;
        }

        /// <summary>
        /// Polyline is "effectively closed" if pl.Closed=true OR last vertex == first vertex.
        /// Both patterns render identically; different DXF/DWG generators produce one or other.
        /// </summary>
        public static bool IsEffectivelyClosed(Polyline pl, double tol = 1e-3)
        {
            if (pl == null || pl.NumberOfVertices < 3) return false;
            if (pl.Closed) return true;
            var first = pl.GetPoint2dAt(0);
            var last  = pl.GetPoint2dAt(pl.NumberOfVertices - 1);
            return first.GetDistanceTo(last) < tol;
        }

        /// <summary>
        /// Snap value down to nearest grid multiple, clamped to [min, max].
        /// Returns -1 if value below min (caller should error).
        /// </summary>
        public static double SnapDownToGrid(double value, double gridSize, double min, double max)
        {
            if (value < min) return -1;
            if (value > max) value = max;
            return Math.Floor(value / gridSize) * gridSize;
        }

        /// <summary>
        /// Infer bar direction from polyline first segment geometry.
        /// Returns "X" (horizontal) when |dx| &gt;= |dy|, "Y" otherwise.
        /// Reliable for axis-aligned bars; returns "X" for diagonal bars.
        /// </summary>
        public static string InferDirectionFromPolyline(Polyline pl)
        {
            if (pl == null || pl.NumberOfVertices < 2) return "X";
            var a = pl.GetPoint2dAt(0);
            var b = pl.GetPoint2dAt(1);
            return Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y) ? "X" : "Y";
        }

        // ----------------------------------------------------------------
        // Ray casting / scanline — polygon intersection helpers
        // ----------------------------------------------------------------

        /// <summary>
        /// Zwraca odcinki poziomej linii y=coord przyciete do wielokata.
        /// Wyniki to pary (xStart, xEnd) w granicach [xMin, xMax].
        /// </summary>
        public static List<(double, double)> ClipHorizontalLine(
            List<Point2d> vertices, double y, double xMin, double xMax)
        {
            var xs = FindIntersectionsH(vertices, y);
            return PairSegments(xs, xMin, xMax);
        }

        /// <summary>
        /// Zwraca odcinki pionowej linii x=coord przyciete do wielokata.
        /// Wyniki to pary (yStart, yEnd) w granicach [yMin, yMax].
        /// </summary>
        public static List<(double, double)> ClipVerticalLine(
            List<Point2d> vertices, double x, double yMin, double yMax)
        {
            var ys = FindIntersectionsV(vertices, x);
            return PairSegments(ys, yMin, yMax);
        }

        public static List<double> FindIntersectionsH(List<Point2d> vertices, double y)
        {
            var result = new List<double>();
            int n = vertices.Count;

            for (int i = 0; i < n; i++)
            {
                var p1 = vertices[i];
                var p2 = vertices[(i + 1) % n];

                // Pomijaj krawedzie rownolegle do linii skanowania
                if (Math.Abs(p1.Y - p2.Y) < 1e-9) continue;

                // Sprawdz czy linia y przechodzi przez krawedz
                // (wylaczamy dokladnie gorny punkt zeby uniknac podwojnego liczenia)
                if ((p1.Y <= y && p2.Y > y) || (p2.Y <= y && p1.Y > y))
                {
                    double t = (y - p1.Y) / (p2.Y - p1.Y);
                    double x = p1.X + t * (p2.X - p1.X);
                    result.Add(x);
                }
            }

            result.Sort();
            return result;
        }

        public static List<double> FindIntersectionsV(List<Point2d> vertices, double x)
        {
            var result = new List<double>();
            int n = vertices.Count;

            for (int i = 0; i < n; i++)
            {
                var p1 = vertices[i];
                var p2 = vertices[(i + 1) % n];

                if (Math.Abs(p1.X - p2.X) < 1e-9) continue;

                if ((p1.X <= x && p2.X > x) || (p2.X <= x && p1.X > x))
                {
                    double t = (x - p1.X) / (p2.X - p1.X);
                    double y = p1.Y + t * (p2.Y - p1.Y);
                    result.Add(y);
                }
            }

            result.Sort();
            return result;
        }

        /// <summary>Laczy posortowane przeciecia w pary i przycina do zakresu.</summary>
        public static List<(double, double)> PairSegments(List<double> coords, double min, double max)
        {
            var segments = new List<(double, double)>();

            for (int i = 0; i + 1 < coords.Count; i += 2)
            {
                double a = coords[i];
                double b = coords[i + 1];

                // Przytnij do zakresu z otuling
                double clampA = Math.Max(a, min);
                double clampB = Math.Min(b, max);

                if (clampA < clampB - 1e-6)
                    segments.Add((clampA, clampB));
            }

            return segments;
        }

        public static List<Point2d> GetPolylineVertices(Polyline pline)
        {
            var pts = new List<Point2d>();
            for (int i = 0; i < pline.NumberOfVertices; i++)
                pts.Add(pline.GetPoint2dAt(i));
            return pts;
        }
    }
}
