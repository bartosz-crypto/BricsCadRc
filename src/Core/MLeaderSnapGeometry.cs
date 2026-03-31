using System;
using System.Collections.Generic;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Czysta geometria snap-back grotu MLeadera na pręt (polilinię).
    /// Brak zależności od BRX API — w pełni testowalny w unit testach.
    ///
    /// Używany przez RcMLeaderOverrule do obliczenia czy i gdzie snapnąć grot.
    /// </summary>
    public static class MLeaderSnapGeometry
    {
        /// <summary>Domyślna tolerancja snapu w mm.</summary>
        public const double DefaultTolerance = 5.0;

        // ----------------------------------------------------------------
        // Uproszczony punkt 3D bez zależności od BRX
        // ----------------------------------------------------------------

        public readonly struct Point3
        {
            public readonly double X, Y, Z;

            public Point3(double x, double y, double z) { X = x; Y = y; Z = z; }

            public double DistanceTo(Point3 other)
            {
                double dx = X - other.X, dy = Y - other.Y, dz = Z - other.Z;
                return Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }

            public override string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
        }

        // ----------------------------------------------------------------
        // ClosestPointOnSegment — rzut prostopadły q na odcinek A–B (clamp do [0,1])
        // ----------------------------------------------------------------

        public static Point3 ClosestPointOnSegment(Point3 q, Point3 a, Point3 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            double lenSq = dx * dx + dy * dy + dz * dz;
            if (lenSq < 1e-12) return a;    // odcinek zdegenerowany
            double t = ((q.X - a.X) * dx + (q.Y - a.Y) * dy + (q.Z - a.Z) * dz) / lenSq;
            t = Math.Max(0.0, Math.Min(1.0, t));
            return new Point3(a.X + t * dx, a.Y + t * dy, a.Z + t * dz);
        }

        // ----------------------------------------------------------------
        // ClosestPointOnSegments — iteruje odcinki (polilinii), zwraca globalnie najbliższy
        // Zwraca queryPt jeśli lista jest pusta.
        // ----------------------------------------------------------------

        public static Point3 ClosestPointOnSegments(
            Point3 query,
            IReadOnlyList<(Point3 A, Point3 B)> segments)
        {
            if (segments == null || segments.Count == 0) return query;

            Point3 best    = query;
            double bestDist = double.MaxValue;

            foreach (var (a, b) in segments)
            {
                var   candidate = ClosestPointOnSegment(query, a, b);
                double d        = query.DistanceTo(candidate);
                if (d < bestDist) { bestDist = d; best = candidate; }
            }
            return best;
        }

        // ----------------------------------------------------------------
        // NeedsSnap — true gdy grot odsuwa się od pręta o więcej niż tolerance
        // ----------------------------------------------------------------

        public static bool NeedsSnap(
            Point3 arrowPt,
            Point3 closestPt,
            double tolerance = DefaultTolerance)
            => arrowPt.DistanceTo(closestPt) > tolerance;

        // ----------------------------------------------------------------
        // ComputeSnap — łączy ClosestPointOnSegments + NeedsSnap
        // Używane w unit testach i przez RcMLeaderOverrule (gdy segmenty z polilinii).
        // ----------------------------------------------------------------

        public static (bool NeedsSnap, Point3 SnapPoint) ComputeSnap(
            Point3 arrowPt,
            IReadOnlyList<(Point3 A, Point3 B)> segments,
            double tolerance = DefaultTolerance)
        {
            var closest = ClosestPointOnSegments(arrowPt, segments);
            return (NeedsSnap(arrowPt, closest, tolerance), closest);
        }
    }
}
