using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Wykrywanie nibu (uskoku płyty przy krawędzi: niższy pas szer. zwykle 115–135 mm, &lt; 255 mm).
    /// Na rysunku nib = druga zamknięta polilinia wewnątrz obrysu płyty, równoległa do krawędzi
    /// w odległości &lt; 255 mm na jakiejś długości (linia uskoku). Gdzie nibu nie ma, linia wewnętrzna
    /// pokrywa się z obrysem.
    ///   • obrys ZEWNĘTRZNY — dół (B1/B2) i UB 03 w nibie,
    ///   • obrys WEWNĘTRZNY (uskok) — góra (T1/T2) i UB 01/02 (pręty górne nie mieszczą się w nibie).
    /// Użytkownik może wskazać którykolwiek z obu obrysów — Detect zwraca oba.
    /// </summary>
    public static class NibDetector
    {
        public const double MaxNibWidth = 255.0;
        public const double MinNibWidth = 50.0;

        public sealed class NibEdge
        {
            /// <summary>Krawędź pionowa (x = const) — pręty X dochodzą do niej prostopadle.</summary>
            public bool   Vertical;
            /// <summary>Współrzędna krawędzi zewnętrznej (x dla pionowej, y dla poziomej).</summary>
            public double OuterCoord;
            /// <summary>Współrzędna linii uskoku.</summary>
            public double InnerCoord;
            /// <summary>Zakres nibu wzdłuż krawędzi (y dla pionowej, x dla poziomej) — do narożników obrysu.</summary>
            public double Lo, Hi;
            /// <summary>+1: wnętrze płyty po stronie rosnącej współrzędnej, −1: malejącej.</summary>
            public int    InwardSign;
            public double Width => Math.Abs(InnerCoord - OuterCoord);
        }

        public sealed class NibInfo
        {
            public ObjectId      OuterId;
            public ObjectId      InnerId;
            public List<Point2d> Outer;
            public List<Point2d> Inner;
            public List<NibEdge> Edges = new List<NibEdge>();
        }

        /// <summary>
        /// Nib dla wskazanego obrysu (zewnętrznego albo wewnętrznego); null gdy płyta nie ma nibu.
        /// </summary>
        public static NibInfo Detect(Database db, ObjectId slabId)
        {
            if (db == null || slabId.IsNull) return null;
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                if (!(tr.GetObject(slabId, OpenMode.ForRead) is Polyline slab)) return null;
                if (!GeometryHelper.IsEffectivelyClosed(slab)) return null;
                var p = Clean(GeometryHelper.GetPolylineVertices(slab));
                if (p.Count < 3) return null;
                var pb = Box(p);

                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                NibInfo best = null;
                foreach (ObjectId oid in ms)
                {
                    if (oid.IsErased || oid == slabId) continue;
                    if (!(tr.GetObject(oid, OpenMode.ForRead) is Polyline pl) || pl.NumberOfVertices < 3) continue;
                    if (!GeometryHelper.IsEffectivelyClosed(pl)) continue;
                    var q = Clean(GeometryHelper.GetPolylineVertices(pl));
                    if (q.Count < 3) continue;
                    var qb = Box(q);

                    NibInfo cand = null;
                    if (Within(qb, pb)) cand = Build(slabId, p, oid, q);        // wskazano obrys zewnętrzny
                    else if (Within(pb, qb)) cand = Build(oid, q, slabId, p);   // wskazano linię uskoku
                    if (cand == null) continue;
                    if (best == null || cand.Edges.Sum(e => e.Hi - e.Lo) > best.Edges.Sum(e => e.Hi - e.Lo))
                        best = cand;
                }
                return best;
            }
            catch (System.Exception ex) { Log.Error("NibDetector.Detect", ex); return null; }
        }

        /// <summary>Obrys zewnętrzny płyty (gdy wskazano linię uskoku) — inaczej ten sam obrys.</summary>
        public static ObjectId OuterOf(Database db, ObjectId slabId)
        {
            var nib = Detect(db, slabId);
            return nib != null ? nib.OuterId : slabId;
        }

        // ── wewnętrzne ──────────────────────────────────────────────────────

        private static NibInfo Build(ObjectId outerId, List<Point2d> outer, ObjectId innerId, List<Point2d> inner)
        {
            // Każdy wierzchołek linii uskoku najwyżej MaxNibWidth od obrysu i wewnątrz/na obrysie
            foreach (var v in inner)
            {
                if (DistToBoundary(outer, v) > MaxNibWidth + 1.0) return null;
                if (DistToBoundary(outer, v) > 1.0 && !GeometryHelper.IsPointInsidePolygon(outer, v)) return null;
            }

            var info = new NibInfo { OuterId = outerId, InnerId = innerId, Outer = outer, Inner = inner };
            var oe = GeometryHelper.EnumerateAxisAlignedEdges(Closed(outer));
            var ie = GeometryHelper.EnumerateAxisAlignedEdges(Closed(inner));
            foreach (var e in oe)
            {
                if (e.Orientation != GeometryHelper.EdgeOrientation.Vertical &&
                    e.Orientation != GeometryHelper.EdgeOrientation.Horizontal) continue;
                bool vert = e.Orientation == GeometryHelper.EdgeOrientation.Vertical;
                double c  = vert ? e.Start.X : e.Start.Y;
                double lo = vert ? Math.Min(e.Start.Y, e.End.Y) : Math.Min(e.Start.X, e.End.X);
                double hi = vert ? Math.Max(e.Start.Y, e.End.Y) : Math.Max(e.Start.X, e.End.X);
                double mid = (lo + hi) / 2.0;
                var probe = vert ? new Point2d(c + 1.0, mid) : new Point2d(mid, c + 1.0);
                int inward = GeometryHelper.IsPointInsidePolygon(outer, probe) ? 1 : -1;

                foreach (var f in ie)
                {
                    if (f.Orientation != e.Orientation) continue;
                    double fc = vert ? f.Start.X : f.Start.Y;
                    double d  = (fc - c) * inward;                       // > 0 = w stronę wnętrza
                    if (d < MinNibWidth || d > MaxNibWidth + 1.0) continue;
                    double flo = vert ? Math.Min(f.Start.Y, f.End.Y) : Math.Min(f.Start.X, f.End.X);
                    double fhi = vert ? Math.Max(f.Start.Y, f.End.Y) : Math.Max(f.Start.X, f.End.X);
                    double olo = Math.Max(lo, flo), ohi = Math.Min(hi, fhi);
                    if (ohi - olo < 1.0) continue;
                    // Do narożników obrysu: przy narożniku wypukłym linia uskoku kończy się o szerokość nibu wcześniej
                    if (olo - lo <= d + 1.0) olo = lo;
                    if (hi - ohi <= d + 1.0) ohi = hi;
                    info.Edges.Add(new NibEdge
                    {
                        Vertical = vert, OuterCoord = c, InnerCoord = fc, Lo = olo, Hi = ohi, InwardSign = inward
                    });
                }
            }
            return info.Edges.Count > 0 ? info : null;
        }

        private static List<Point2d> Clean(List<Point2d> v)
        {
            var r = new List<Point2d>();
            foreach (var p in v)
                if (r.Count == 0 || r[r.Count - 1].GetDistanceTo(p) > 1e-3) r.Add(p);
            if (r.Count > 2 && r[0].GetDistanceTo(r[r.Count - 1]) < 1e-3) r.RemoveAt(r.Count - 1);
            return r;
        }

        private static List<Point2d> Closed(List<Point2d> v)
        {
            var r = new List<Point2d>(v);
            if (r.Count > 0) r.Add(r[0]);
            return r;
        }

        private static (double x0, double y0, double x1, double y1) Box(List<Point2d> v)
            => (v.Min(p => p.X), v.Min(p => p.Y), v.Max(p => p.X), v.Max(p => p.Y));

        /// <summary>a leży w b, każdy bok odsunięty o 0..MaxNibWidth.</summary>
        private static bool Within((double x0, double y0, double x1, double y1) a,
                                   (double x0, double y0, double x1, double y1) b)
        {
            double t = MaxNibWidth + 1.0;
            bool In(double inset) => inset >= -1.0 && inset <= t;
            return In(a.x0 - b.x0) && In(a.y0 - b.y0) && In(b.x1 - a.x1) && In(b.y1 - a.y1);
        }

        private static double DistToBoundary(List<Point2d> poly, Point2d p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                var ab = b - a; double len2 = ab.DotProduct(ab);
                double t = len2 > 1e-12 ? Math.Max(0, Math.Min(1, (p - a).DotProduct(ab) / len2)) : 0;
                var c = a + ab * t;
                best = Math.Min(best, c.GetDistanceTo(p));
            }
            return best;
        }
    }
}
