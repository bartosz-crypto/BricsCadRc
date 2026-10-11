#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Wielokąty osiowe (prostokątne) — różnica / suma prostokątów (krok K7 planu docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md:
    /// siatka płyty przy belce, E2). Czysta klasa (bez Teigha), testy <c>RectilinearPolygonsTests</c>.
    /// Algorytm: siatka komórek z unikalnych współrzędnych (wierzchołki wielokąta + boki prostokątów), komórka „w środku” =
    /// środek w wielokącie (parzystość) i poza wszystkimi prostokątami odejmowanymi; obrys = krawędzie między komórką w środku
    /// i poza, łączone w pętle (wnętrze po lewej: pętla zewnętrzna CCW, otwór CW), wierzchołki współliniowe usunięte.
    /// Współrzędne wyniku = dokładnie współrzędne wejścia (bez arytmetyki), więc wielokąt bez odjęć ma te same wierzchołki.
    /// Wielokąt nieosiowy (skos) — <see cref="IsRectilinear"/> = false; wywołujący wtedy nie stosuje różnicy.
    /// </summary>
    public static class RectilinearPolygons
    {
        /// <summary>Prostokąt osiowy [X0, X1] × [Y0, Y1] (X0 ≤ X1, Y0 ≤ Y1 — konstruktor porządkuje).</summary>
        public readonly struct Rect
        {
            public readonly double X0, Y0, X1, Y1;
            public Rect(double x0, double y0, double x1, double y1)
            {
                X0 = Math.Min(x0, x1); X1 = Math.Max(x0, x1);
                Y0 = Math.Min(y0, y1); Y1 = Math.Max(y0, y1);
            }
            public bool IsEmpty => X1 - X0 < 1e-9 || Y1 - Y0 < 1e-9;
            public bool Contains(double x, double y) => x > X0 && x < X1 && y > Y0 && y < Y1;
            public override string ToString()
                => string.Format(System.Globalization.CultureInfo.InvariantCulture, "[{0:0.#},{1:0.#}]x[{2:0.#},{3:0.#}]", X0, X1, Y0, Y1);
        }

        /// <summary>Wszystkie krawędzie wielokąta osiowe (tolerancja 1e-6).</summary>
        public static bool IsRectilinear(IList<(double X, double Y)> poly)
        {
            if (poly == null || poly.Count < 4) return false;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                if (Math.Abs(a.X - b.X) > 1e-6 && Math.Abs(a.Y - b.Y) > 1e-6) return false;
            }
            return true;
        }

        /// <summary>
        /// Wielokąt osiowy minus prostokąty → pętle wyniku (pierwsza — największa pętla zewnętrzna; otwory CW). Pusta lista,
        /// gdy nic nie zostaje. Prostokąty puste i leżące poza wielokątem nie zmieniają wyniku.
        /// </summary>
        public static List<List<(double X, double Y)>> Subtract(IList<(double X, double Y)> poly, IEnumerable<Rect> holes)
        {
            var hs = (holes ?? Enumerable.Empty<Rect>()).Where(r => !r.IsEmpty).ToList();
            var xs = Unique(poly.Select(p => p.X).Concat(hs.SelectMany(r => new[] { r.X0, r.X1 })));
            var ys = Unique(poly.Select(p => p.Y).Concat(hs.SelectMany(r => new[] { r.Y0, r.Y1 })));
            double minX = poly.Min(p => p.X), maxX = poly.Max(p => p.X), minY = poly.Min(p => p.Y), maxY = poly.Max(p => p.Y);
            xs = xs.Where(x => x >= minX && x <= maxX).ToList();
            ys = ys.Where(y => y >= minY && y <= maxY).ToList();
            return Trace(xs, ys, (cx, cy) => Inside(poly, cx, cy) && !hs.Any(r => r.Contains(cx, cy)));
        }

        /// <summary>Suma prostokątów → pętle (otwory CW).</summary>
        public static List<List<(double X, double Y)>> Union(IEnumerable<Rect> rects)
        {
            var rs = (rects ?? Enumerable.Empty<Rect>()).Where(r => !r.IsEmpty).ToList();
            if (rs.Count == 0) return new List<List<(double X, double Y)>>();
            var xs = Unique(rs.SelectMany(r => new[] { r.X0, r.X1 }));
            var ys = Unique(rs.SelectMany(r => new[] { r.Y0, r.Y1 }));
            return Trace(xs, ys, (cx, cy) => rs.Any(r => r.Contains(cx, cy)));
        }

        /// <summary>
        /// Wielokąt osiowy plus prostokąty, przycięte do wielokąta osiowego <paramref name="clip"/> → pętle (pierwsza — największa pętla
        /// zewnętrzna). Belka bez nibu (rekord RC_BEAM [38]): linia uskoku + pas nibu przy belce, w obrysie płyty. Bez prostokątów —
        /// wielokąt przycięty (te same wierzchołki, gdy leży w clip).
        /// </summary>
        public static List<List<(double X, double Y)>> AddClipped(IList<(double X, double Y)> poly, IEnumerable<Rect> add,
                                                                 IList<(double X, double Y)> clip)
        {
            var rs = (add ?? Enumerable.Empty<Rect>()).Where(r => !r.IsEmpty).ToList();
            var xs = Unique(poly.Select(p => p.X).Concat(clip.Select(p => p.X)).Concat(rs.SelectMany(r => new[] { r.X0, r.X1 })));
            var ys = Unique(poly.Select(p => p.Y).Concat(clip.Select(p => p.Y)).Concat(rs.SelectMany(r => new[] { r.Y0, r.Y1 })));
            double minX = clip.Min(p => p.X), maxX = clip.Max(p => p.X), minY = clip.Min(p => p.Y), maxY = clip.Max(p => p.Y);
            xs = xs.Where(x => x >= minX && x <= maxX).ToList();
            ys = ys.Where(y => y >= minY && y <= maxY).ToList();
            return Trace(xs, ys, (cx, cy) => Inside(clip, cx, cy) && (Inside(poly, cx, cy) || rs.Any(r => r.Contains(cx, cy))));
        }

        /// <summary>Punkt w zbiorze pętli (parzystość — otwory odejmują się same).</summary>
        public static bool Inside(IList<List<(double X, double Y)>> loops, double x, double y)
        {
            bool inside = false;
            foreach (var l in loops) if (Inside(l, x, y)) inside = !inside;
            return inside;
        }

        /// <summary>Pole (suma pól ze znakiem: pętle zewnętrzne CCW +, otwory CW −).</summary>
        public static double Area(IList<List<(double X, double Y)>> loops)
            => loops.Sum(l => SignedArea(l));

        public static double SignedArea(IList<(double X, double Y)> v)
        {
            double a = 0;
            for (int i = 0; i < v.Count; i++) { var p = v[i]; var q = v[(i + 1) % v.Count]; a += p.X * q.Y - q.X * p.Y; }
            return a / 2;
        }

        /// <summary>
        /// Odcinki prostej (horizontal: y = c, wynik wzdłuż X; inaczej x = c, wynik wzdłuż Y) wewnątrz zbioru pętli (parzystość):
        /// przecięcia z krawędziami nierównoległymi (półotwarte jak GeometryHelper.FindIntersectionsH), posortowane, w parach.
        /// </summary>
        public static List<(double Lo, double Hi)> IntervalsOnLine(IList<List<(double X, double Y)>> loops, bool horizontal, double c)
        {
            var xs = new List<double>();
            foreach (var l in loops ?? new List<List<(double X, double Y)>>())
                for (int i = 0; i < l.Count; i++)
                {
                    var p1 = l[i]; var p2 = l[(i + 1) % l.Count];
                    double a1 = horizontal ? p1.Y : p1.X, a2 = horizontal ? p2.Y : p2.X;
                    double b1 = horizontal ? p1.X : p1.Y, b2 = horizontal ? p2.X : p2.Y;
                    if (Math.Abs(a1 - a2) < 1e-9) continue;
                    if ((a1 <= c && a2 > c) || (a2 <= c && a1 > c))
                        xs.Add(b1 + (c - a1) / (a2 - a1) * (b2 - b1));
                }
            xs.Sort();
            var res = new List<(double, double)>();
            for (int k = 0; k + 1 < xs.Count; k += 2) res.Add((xs[k], xs[k + 1]));
            return res;
        }

        /// <summary>Różnica zbiorów odcinków (a − b), oba posortowane i rozłączne; kawałki krótsze niż 1e-6 pomijane.</summary>
        public static List<(double Lo, double Hi)> SubtractIntervals(IList<(double Lo, double Hi)> a, IList<(double Lo, double Hi)> b)
        {
            var res = new List<(double, double)>();
            foreach (var (lo0, hi0) in a)
            {
                var parts = new List<(double lo, double hi)> { (lo0, hi0) };
                foreach (var (blo, bhi) in b)
                {
                    var next = new List<(double, double)>();
                    foreach (var (lo, hi) in parts)
                    {
                        if (bhi <= lo || blo >= hi) { next.Add((lo, hi)); continue; }
                        if (blo - lo > 1e-6) next.Add((lo, blo));
                        if (hi - bhi > 1e-6) next.Add((bhi, hi));
                    }
                    parts = next;
                }
                res.AddRange(parts);
            }
            return res;
        }

        /// <summary>
        /// Części odcinka krawędzi [lo, hi] na prostej c (horizontal — krawędź pozioma y = c), które NIE leżą w zbiorze pętli
        /// (domkniętym: krawędź na obrysie pętli też jest „w środku” — próbki po obu stronach prostej w odległości tol).
        /// Kawałki krótsze niż minLen pomijane.
        /// </summary>
        public static List<(double Lo, double Hi)> UncoveredParts(double lo, double hi, IList<List<(double X, double Y)>> loops,
                                                                 bool horizontal, double c, double tol = 0.5, double minLen = 1.0)
        {
            var whole = new List<(double, double)> { (Math.Min(lo, hi), Math.Max(lo, hi)) };
            if (loops == null || loops.Count == 0) return whole;
            var cov = IntervalsOnLine(loops, horizontal, c - tol).Concat(IntervalsOnLine(loops, horizontal, c + tol))
                                                                 .OrderBy(x => x.Lo).ToList();
            var merged = new List<(double Lo, double Hi)>();
            foreach (var iv in cov)
            {
                if (merged.Count > 0 && iv.Lo <= merged[merged.Count - 1].Hi + 1e-6)
                    merged[merged.Count - 1] = (merged[merged.Count - 1].Lo, Math.Max(merged[merged.Count - 1].Hi, iv.Hi));
                else merged.Add(iv);
            }
            return SubtractIntervals(whole, merged).Where(p => p.Hi - p.Lo >= minLen).ToList();
        }

        // ── wewnętrzne ──────────────────────────────────────────────────

        private static List<double> Unique(IEnumerable<double> vals)
        {
            var r = new List<double>();
            foreach (var v in vals.OrderBy(x => x))
                if (r.Count == 0 || v - r[r.Count - 1] > 1e-6) r.Add(v);
            return r;
        }

        private static bool Inside(IList<(double X, double Y)> poly, double x, double y)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.Y > y) != (b.Y > y) && x < (b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        /// <summary>Komórki siatki xs × ys z funkcją „w środku” → pętle obrysu (wnętrze po lewej).</summary>
        private static List<List<(double X, double Y)>> Trace(List<double> xs, List<double> ys, Func<double, double, bool> inside)
        {
            int nx = xs.Count - 1, ny = ys.Count - 1;
            var res = new List<List<(double X, double Y)>>();
            if (nx < 1 || ny < 1) return res;
            var cell = new bool[nx, ny];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                    cell[i, j] = inside((xs[i] + xs[i + 1]) / 2, (ys[j] + ys[j + 1]) / 2);
            bool In(int i, int j) => i >= 0 && j >= 0 && i < nx && j < ny && cell[i, j];

            // krawędzie skierowane w indeksach węzłów (wnętrze po lewej)
            var edges = new Dictionary<(int, int), List<(int, int)>>();
            void Add((int, int) a, (int, int) b)
            {
                if (!edges.TryGetValue(a, out var l)) edges[a] = l = new List<(int, int)>();
                l.Add(b);
            }
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < ny; j++)
                {
                    if (!cell[i, j]) continue;
                    if (!In(i, j - 1)) Add((i, j), (i + 1, j));           // dół → w prawo
                    if (!In(i + 1, j)) Add((i + 1, j), (i + 1, j + 1));   // prawy → w górę
                    if (!In(i, j + 1)) Add((i + 1, j + 1), (i, j + 1));   // góra → w lewo
                    if (!In(i - 1, j)) Add((i, j + 1), (i, j));           // lewy → w dół
                }

            while (edges.Count > 0)
            {
                var start = edges.Keys.OrderBy(k => k.Item2).ThenBy(k => k.Item1).First();
                var loop = new List<(int, int)>();
                var cur = start;
                (int dx, int dy) dir = (0, 0);
                int guard = 0;
                while (guard++ < 1000000)
                {
                    if (!edges.TryGetValue(cur, out var outs) || outs.Count == 0) break;
                    // wierzchołek z dwiema krawędziami wychodzącymi (styk komórek po przekątnej) — skręt w lewo (pętle rozdzielne)
                    int pick = 0;
                    if (outs.Count > 1 && dir != (0, 0))
                    {
                        var left = (-dir.dy, dir.dx);
                        int k = outs.FindIndex(o => (Math.Sign(o.Item1 - cur.Item1), Math.Sign(o.Item2 - cur.Item2)) == left);
                        if (k >= 0) pick = k;
                    }
                    var nxt = outs[pick];
                    outs.RemoveAt(pick);
                    if (outs.Count == 0) edges.Remove(cur);
                    loop.Add(cur);
                    dir = (Math.Sign(nxt.Item1 - cur.Item1), Math.Sign(nxt.Item2 - cur.Item2));
                    cur = nxt;
                    if (cur == start) break;
                }
                var pts = Simplify(loop).Select(k => (xs[k.Item1], ys[k.Item2])).ToList();
                if (pts.Count >= 4) res.Add(pts);
            }
            // pierwsza — największa pętla zewnętrzna (CCW), potem reszta wg pola malejąco
            return res.OrderByDescending(l => SignedArea(l)).ToList();
        }

        /// <summary>Usuwa wierzchołki współliniowe (indeksy siatki).</summary>
        private static List<(int, int)> Simplify(List<(int, int)> loop)
        {
            var r = new List<(int, int)>(loop);
            bool changed = true;
            while (changed && r.Count > 3)
            {
                changed = false;
                for (int i = 0; i < r.Count; i++)
                {
                    var a = r[(i - 1 + r.Count) % r.Count]; var b = r[i]; var c = r[(i + 1) % r.Count];
                    bool col = (a.Item1 == b.Item1 && b.Item1 == c.Item1) || (a.Item2 == b.Item2 && b.Item2 == c.Item2);
                    if (col) { r.RemoveAt(i); changed = true; break; }
                }
            }
            return r;
        }
    }
}
