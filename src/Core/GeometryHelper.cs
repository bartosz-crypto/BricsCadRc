using System;
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
    }
}
