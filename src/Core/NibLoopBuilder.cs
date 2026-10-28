#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Automat domykania nibu (POPRAWKI_WZORCA 23) — czysta geometria (bez Teigha), testy `tests/NibLoopBuilderTests.cs`.
    /// Wejście: zamknięty obrys płyty (wierzchołki + wybrzuszenia) i otwarte kawałki linii uskoku (polilinie / linie / łuki
    /// zamienione na listy punktów z wybrzuszeniami). Wynik: zamknięta pętla = obrys z odciętym pasem nibu (linia uskoku
    /// domknięta krawędzią obrysu), dokładnie jak w GA (bez offsetu).
    /// Kroki: (1) czyszczenie obrysu; (2) filtr kawałków — WSZYSTKIE próbki (wierzchołki + środki odcinków) w obrysie
    /// lub ≤ gapTol od niego i ≤ maxWidth + 1 od obrysu, kawałki efektywnie zamknięte (próg drzwi) i leżące na obrysie
    /// pomijane cicho; (3) graf końców (klastry ≤ gapTol), stopień ≥ 3 = rozgałęzienie, przerwa — przecięcie prostych albo
    /// środek; (4) składowa zamknięta = kandydat; (5) wolne końce ≤ gapTol od obrysu przyciągane (przecięcie z bokiem /
    /// rzut), koniec na łuku obrysu — komunikat; (6)–(7) każdy łańcuch odcina od obrysu mniejszą część (pas nibu),
    /// przedziały na obrysie rozłączne, jedno obejście obrysu (łuki obrysu przenoszone); bezpiecznik: pas &gt; 0,4 pola
    /// obrysu — komunikat; (8) scalanie współliniowych; (9) <see cref="LooksLikeNib"/> = kryteria NibDetector.
    /// Ograniczenie: styk „T” (koniec kawałka w środku boku innego kawałka) nie jest łączony — komunikat o przerwie.
    /// </summary>
    public static class NibLoopBuilder
    {
        /// <summary>Tolerancja przerwy [mm] (decyzja użytkownika 2026-10-09: 4 mm).</summary>
        public const double DefaultGapTol = 4.0;
        /// <summary>Pas nibu większy niż ta część pola obrysu → komunikat zamiast zgadywania.</summary>
        public const double MaxStripAreaRatio = 0.4;

        private const double DupTol = 1e-3;        // jak NibDetector.Clean
        private const double VertexSnap = 1e-4;    // przyciągnięty koniec = wierzchołek obrysu
        private const double AxisEps = 1e-3;       // jak GeometryHelper.EnumerateAxisAlignedEdges (po prostowaniu — NdSquared)
        private const double CrossTol = 0.01;      // styk linii z obrysem tylko na końcach
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Kawałek linii uskoku: punkty i wybrzuszenia odcinków (Bulges[i] = odcinek i → i+1; brak = proste).</summary>
        public sealed class Piece
        {
            public readonly List<(double X, double Y)> Points;
            public readonly List<double> Bulges;

            public Piece(IEnumerable<(double X, double Y)> points, IEnumerable<double> bulges = null)
            {
                Points = (points ?? Enumerable.Empty<(double X, double Y)>()).ToList();
                Bulges = (bulges ?? Enumerable.Empty<double>()).ToList();
                int segs = Math.Max(0, Points.Count - 1);
                while (Bulges.Count < segs) Bulges.Add(0.0);
                if (Bulges.Count > segs) Bulges.RemoveRange(segs, Bulges.Count - segs);
            }
        }

        public sealed class Result
        {
            /// <summary>Pętla gotowa do zapisu (spełnia <see cref="LooksLikeNib"/>).</summary>
            public bool Ok;
            /// <summary>Zamknięta pętla (bez powtórzonego pierwszego punktu) i wybrzuszenia jej odcinków.</summary>
            public List<(double X, double Y)> Loop = new List<(double X, double Y)>();
            public List<double> LoopBulges = new List<double>();
            /// <summary>Indeksy (w liście wejściowej) kawałków użytych do pętli — do usunięcia.</summary>
            public List<int> Used = new List<int>();
            /// <summary>Powody braku domknięcia (bez prefiksu „Nib: ”), po polsku, liczby InvariantCulture.</summary>
            public List<string> Problems = new List<string>();
            /// <summary>Kawałki w pasie nibu (po filtrze). 0 = brak linii uskoku — bez komunikatu.</summary>
            public int InBand;
        }

        // ── wewnętrzne struktury ────────────────────────────────────────

        private struct P
        {
            public double X, Y;
            public P(double x, double y) { X = x; Y = y; }
            public static P operator -(P a, P b) => new P(a.X - b.X, a.Y - b.Y);
            public static P operator +(P a, P b) => new P(a.X + b.X, a.Y + b.Y);
            public static P operator *(P a, double k) => new P(a.X * k, a.Y * k);
        }

        private sealed class Work
        {
            public int Index;
            public List<P> Pts;
            public List<double> B;      // B[i] = odcinek i → i+1
            public double MaxOffset;    // największa odległość próbki od obrysu
        }

        private sealed class Chain
        {
            public List<P> Pts = new List<P>();
            public List<double> B = new List<double>();
            public List<int> Pieces = new List<int>();
            public bool Closed;
            public double TFrom, TTo;   // parametry końców na obrysie (po orientacji: from → to)
            public double MaxOffset;
        }

        // ── API ─────────────────────────────────────────────────────────

        /// <summary>
        /// Buduje zamkniętą pętlę nibu z obrysu i otwartych kawałków linii uskoku. minWidth / maxWidth — kryteria
        /// NibDetector (MinNibWidth / MaxNibWidth). outlineBulges: wybrzuszenie odcinka i → i+1 obrysu (null = proste).
        /// </summary>
        public static Result Build(IList<(double X, double Y)> outline, IList<double> outlineBulges, IList<Piece> pieces,
                                   double minWidth, double maxWidth, double gapTol = DefaultGapTol)
        {
            var res = new Result();
            var O = CleanClosed(outline?.Select(p => new P(p.X, p.Y)).ToList() ?? new List<P>(),
                                outlineBulges, out var OB);
            if (O.Count < 3) { res.Problems.Add("obrys płyty ma mniej niż 3 wierzchołki."); return res; }
            var Odense = Densify(O, OB, closed: true);

            // (2) Filtr kawałków
            var work = new List<Work>();
            for (int i = 0; i < (pieces?.Count ?? 0); i++)
            {
                var pc = pieces[i];
                if (pc == null) continue;
                var pts = CleanOpen(pc.Points.Select(p => new P(p.X, p.Y)).ToList(), pc.Bulges, out var bs);
                if (pts.Count < 2) continue;
                if (pts.Count >= 3 && Dist(pts[0], pts[pts.Count - 1]) <= DupTol) continue;   // efektywnie zamknięty
                bool ok = true, onOutline = true;
                double maxOff = 0;
                foreach (var s in Samples(pts, bs))
                {
                    double d = OutlineDist(O, OB, s, out _, straightOnly: false);
                    maxOff = Math.Max(maxOff, d);
                    if (d > maxWidth + 1.0 || (d > gapTol && !Inside(Odense, s))) { ok = false; break; }
                    if (d > gapTol) onOutline = false;
                }
                if (!ok || onOutline) continue;
                work.Add(new Work { Index = i, Pts = pts, B = bs, MaxOffset = maxOff });
            }
            res.InBand = work.Count;
            if (work.Count == 0) return res;

            // (3) Graf końców: endpoint e = 2·k (początek kawałka k) / 2·k + 1 (koniec)
            int ne = work.Count * 2;
            P EndPt(int e) { var w = work[e / 2]; return e % 2 == 0 ? w.Pts[0] : w.Pts[w.Pts.Count - 1]; }
            var parent = Enumerable.Range(0, ne).ToArray();
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            for (int a = 0; a < ne; a++)
                for (int b = a + 1; b < ne; b++)
                    if (Dist(EndPt(a), EndPt(b)) <= gapTol) parent[Find(a)] = Find(b);
            var clusters = Enumerable.Range(0, ne).GroupBy(Find).Select(g => g.ToList()).ToList();
            var partner = Enumerable.Repeat(-1, ne).ToArray();
            foreach (var c in clusters)
            {
                if (c.Count >= 3)
                {
                    var p = EndPt(c[0]);
                    res.Problems.Add($"rozgałęzienie linii uskoku w punkcie ({F1(p.X)}, {F1(p.Y)}) — {c.Count} końce kawałków w jednym miejscu.");
                    return res;
                }
                if (c.Count == 2)
                {
                    partner[c[0]] = c[1]; partner[c[1]] = c[0];
                    var j = Joint(work, c[0], c[1], gapTol);
                    SetEnd(work, c[0], j);
                    SetEnd(work, c[1], j);
                }
            }

            // Łańcuchy: najpierw od wolnych końców, potem cykle
            var visited = new bool[work.Count];
            var open = new List<Chain>();
            var closed = new List<Chain>();
            for (int e = 0; e < ne; e++)
            {
                if (partner[e] >= 0 || visited[e / 2]) continue;
                open.Add(Walk(work, partner, visited, e));
            }
            for (int k = 0; k < work.Count; k++)
            {
                if (visited[k]) continue;
                var ch = Walk(work, partner, visited, 2 * k);
                ch.Closed = true;
                if (ch.Pts.Count > 1 && Dist(ch.Pts[0], ch.Pts[ch.Pts.Count - 1]) <= DupTol)
                    ch.Pts.RemoveAt(ch.Pts.Count - 1);
                else ch.B.Add(0.0);   // nie powinno się zdarzyć — domknięcie prostym odcinkiem
                closed.Add(ch);
            }

            List<P> loop; List<double> loopB;
            var all = open.Concat(closed).ToList();
            double offset = all.Count > 0 ? all.Max(c => c.MaxOffset) : 0;
            if (closed.Count > 0)
            {
                // (4) Składowa zamknięta = kandydat
                if (closed.Count > 1 || open.Count > 0)
                {
                    res.Problems.Add("kilka osobnych linii uskoku (zamknięta pętla i inne linie) — domknij nib ręcznie.");
                    return res;
                }
                loop = closed[0].Pts;
                loopB = closed[0].B;
            }
            else
            {
                // (5) Wolne końce → obrys
                var ends = new List<(Chain ch, bool start)>();
                foreach (var ch in open) { ends.Add((ch, true)); ends.Add((ch, false)); }
                foreach (var (ch, start) in ends)
                {
                    var p = start ? ch.Pts[0] : ch.Pts[ch.Pts.Count - 1];
                    double dStraight = OutlineDist(O, OB, p, out int seg, straightOnly: true);
                    if (dStraight <= gapTol) continue;
                    double dAll = OutlineDist(O, OB, p, out _, straightOnly: false);
                    if (dAll <= gapTol)
                    {
                        res.Problems.Add($"koniec linii uskoku w punkcie ({F1(p.X)}, {F1(p.Y)}) leży na łuku obrysu płyty — domknięcie łukiem nieobsługiwane.");
                        continue;
                    }
                    double dOther = double.MaxValue;
                    foreach (var (ch2, s2) in ends)
                    {
                        if (ReferenceEquals(ch2, ch) && s2 == start) continue;
                        var q = s2 ? ch2.Pts[0] : ch2.Pts[ch2.Pts.Count - 1];
                        dOther = Math.Min(dOther, Dist(p, q));
                    }
                    res.Problems.Add(dOther < dAll
                        ? $"przerwa {F1g(dOther)} mm w punkcie ({F1(p.X)}, {F1(p.Y)}) — linia uskoku nie jest ciągła (tolerancja {F1g(gapTol)} mm)."
                        : $"przerwa {F1g(dAll)} mm w punkcie ({F1(p.X)}, {F1(p.Y)}) — linia uskoku nie dochodzi do obrysu płyty (tolerancja {F1g(gapTol)} mm).");
                }
                if (res.Problems.Count > 0) return res;

                foreach (var ch in open)
                {
                    ch.TFrom = SnapEnd(O, OB, ch, start: true, gapTol);
                    ch.TTo = SnapEnd(O, OB, ch, start: false, gapTol);
                }

                // (6) Kontrola przecięć: łańcuch z obrysem (poza końcami) i łańcuchy między sobą
                for (int a = 0; a < open.Count; a++)
                {
                    var da = Densify(open[a].Pts, open[a].B, closed: false);
                    var ea = new[] { open[a].Pts[0], open[a].Pts[open[a].Pts.Count - 1] };
                    if (FirstCrossing(da, false, Odense, true, ea, out var x))
                    {
                        res.Problems.Add($"linia uskoku przecina obrys płyty w punkcie ({F1(x.X)}, {F1(x.Y)}).");
                        return res;
                    }
                    for (int b = a + 1; b < open.Count; b++)
                    {
                        var db = Densify(open[b].Pts, open[b].B, closed: false);
                        if (FirstCrossing(da, false, db, false, new P[0], out x))
                        {
                            res.Problems.Add($"linie uskoku przecinają się w punkcie ({F1(x.X)}, {F1(x.Y)}).");
                            return res;
                        }
                    }
                }

                // (6)–(7) Strona pasa nibu każdego łańcucha: mniejsza część obrysu
                double outlineArea = Math.Abs(Area(O, OB));
                foreach (var ch in open)
                {
                    double tS = ch.TFrom, tE = ch.TTo;
                    if (Fwd(O.Count, tS, tE) < 1e-12)
                    {
                        res.Problems.Add("oba końce linii uskoku w tym samym punkcie obrysu.");
                        return res;
                    }
                    double aA = Math.Abs(StripArea(O, OB, ch.Pts, ch.B, tS, tE, reversed: false));
                    double aB = Math.Abs(StripArea(O, OB, ch.Pts, ch.B, tE, tS, reversed: true));
                    double strip = Math.Min(aA, aB);
                    if (strip > MaxStripAreaRatio * outlineArea)
                    {
                        res.Problems.Add($"linia uskoku dzieli płytę na dwie duże części ({F2(aA / 1e6)} m² i {F2(aB / 1e6)} m²) — nie wiadomo, która jest nibem.");
                        return res;
                    }
                    if (aB < aA) Reverse(ch);   // od: koniec, do: początek
                }

                var sorted = open.OrderBy(c => c.TFrom).ToList();
                if (sorted.Count > 1)
                {
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        var c = sorted[i]; var nx = sorted[(i + 1) % sorted.Count];
                        if (Fwd(O.Count, c.TFrom, c.TTo) >= Fwd(O.Count, c.TFrom, nx.TFrom) - 1e-12)
                        {
                            res.Problems.Add("linie uskoku zachodzą na siebie wzdłuż obrysu płyty — domknij nib ręcznie.");
                            return res;
                        }
                    }
                }

                // Jedno obejście obrysu: łańcuch (from → to), obrys od to do from następnego
                loop = new List<P>(); loopB = new List<double>();
                for (int i = 0; i < sorted.Count; i++)
                {
                    var c = sorted[i]; var nx = sorted[(i + 1) % sorted.Count];
                    for (int k = 0; k < c.Pts.Count - 1; k++) { loop.Add(c.Pts[k]); loopB.Add(c.B[k]); }
                    var path = OutlinePath(O, c.Pts[c.Pts.Count - 1], c.TTo, nx.Pts[0], nx.TFrom);
                    for (int k = 0; k < path.Count - 1; k++)
                    {
                        loop.Add(path[k].p);
                        loopB.Add(PathBulge(O.Count, OB, path[k].t, path[k + 1].t));
                    }
                }
            }

            // (8) Scalanie współliniowych, duplikaty
            Simplify(loop, loopB);
            if (loop.Count < 3) { res.Problems.Add("pętla nibu ma mniej niż 3 wierzchołki."); return res; }

            // (9) Kryteria NibDetector
            // obrys jak go czyta NibDetector.Detect: surowe wierzchołki polilinii (prostowanie przed czyszczeniem)
            var loopT = loop.Select(p => (p.X, p.Y)).ToList();
            var outT = outline.Select(p => (p.X, p.Y)).ToList();
            if (!LooksLikeNib(outT, loopT, minWidth, maxWidth))
            {
                res.Problems.Add($"linia uskoku {F0(offset)} mm od krawędzi — poza zakresem nibu {F0(minWidth)}–{F0(maxWidth)} mm.");
                return res;
            }

            res.Ok = true;
            res.Loop = loopT;
            res.LoopBulges = loopB.ToList();
            res.Used = all.SelectMany(c => c.Pieces).Distinct().OrderBy(i => i).ToList();
            return res;
        }

        /// <summary>
        /// Replika kryteriów NibDetector.Detect dla pary (obrys, pętla w obrysie): bbox pętli w bbox obrysu z wcięciem
        /// −1…maxWidth+1, każdy wierzchołek pętli ≤ maxWidth+1 od obrysu i w obrysie (albo ≤ 1 mm od niego), co najmniej
        /// jedna para krawędzi osiowych w odległości minWidth…maxWidth+1 zachodząca ≥ 1 mm. Obie listy najpierw prostowane
        /// <see cref="OutlineAxisRules.Square"/> (tolerancja 4 mm, jak NibDetector.Detect — POPRAWKI_WZORCA 25).
        /// ZMIANA KRYTERIÓW W NibDetector → zmiana tutaj (i odwrotnie).
        /// </summary>
        public static bool LooksLikeNib(IList<(double X, double Y)> outer, IList<(double X, double Y)> inner,
                                        double minWidth, double maxWidth)
        {
            var p = NdClean(NdSquared(outer));
            var q = NdClean(NdSquared(inner));
            if (p.Count < 3 || q.Count < 3) return false;
            if (!NdWithin(Box(q), Box(p), maxWidth)) return false;

            foreach (var v in q)
            {
                double d = NdDistToBoundary(p, v);
                if (d > maxWidth + 1.0) return false;
                if (d > 1.0 && !Inside(p, v)) return false;
            }
            var oe = AxisEdges(p);
            var ie = AxisEdges(q);
            foreach (var e in oe)
            {
                if (e.orient == 0) continue;
                bool vert = e.orient == 1;
                double c = vert ? e.a.X : e.a.Y;
                double lo = vert ? Math.Min(e.a.Y, e.b.Y) : Math.Min(e.a.X, e.b.X);
                double hi = vert ? Math.Max(e.a.Y, e.b.Y) : Math.Max(e.a.X, e.b.X);
                double mid = (lo + hi) / 2.0;
                var probe = vert ? new P(c + 1.0, mid) : new P(mid, c + 1.0);
                int inward = Inside(p, probe) ? 1 : -1;
                foreach (var f in ie)
                {
                    if (f.orient != e.orient) continue;
                    double fc = vert ? f.a.X : f.a.Y;
                    double d = (fc - c) * inward;
                    if (d < minWidth || d > maxWidth + 1.0) continue;
                    double flo = vert ? Math.Min(f.a.Y, f.b.Y) : Math.Min(f.a.X, f.b.X);
                    double fhi = vert ? Math.Max(f.a.Y, f.b.Y) : Math.Max(f.a.X, f.b.X);
                    if (Math.Min(hi, fhi) - Math.Max(lo, flo) < 1.0) continue;
                    return true;
                }
            }
            return false;
        }

        // ── kroki ───────────────────────────────────────────────────────

        private static P Joint(List<Work> work, int e1, int e2, double gapTol)
        {
            var p1 = EndOf(work, e1, out var a1, out double b1);
            var p2 = EndOf(work, e2, out var a2, out double b2);
            if (Dist(p1, p2) <= 1e-9) return p1;
            var mid = (p1 + p2) * 0.5;
            if (Math.Abs(b1) > 1e-9 || Math.Abs(b2) > 1e-9) return mid;
            var d1 = p1 - a1; var d2 = p2 - a2;
            double l1 = Len(d1), l2 = Len(d2);
            if (l1 < 1e-9 || l2 < 1e-9) return mid;
            double cr = Cross(d1, d2);
            if (Math.Abs(cr) <= 1e-6 * l1 * l2) return mid;   // równoległe
            double t = Cross(p2 - p1, d2) / cr;
            var x = p1 + d1 * t;
            return Dist(x, p1) <= 2 * gapTol && Dist(x, p2) <= 2 * gapTol ? x : mid;
        }

        /// <summary>Punkt końca, sąsiedni punkt (kierunek dojścia) i wybrzuszenie odcinka końcowego.</summary>
        private static P EndOf(List<Work> work, int e, out P prev, out double bulge)
        {
            var w = work[e / 2];
            if (e % 2 == 0) { prev = w.Pts[1]; bulge = w.B[0]; return w.Pts[0]; }
            prev = w.Pts[w.Pts.Count - 2]; bulge = w.B[w.B.Count - 1]; return w.Pts[w.Pts.Count - 1];
        }

        private static void SetEnd(List<Work> work, int e, P p)
        {
            var w = work[e / 2];
            if (e % 2 == 0) w.Pts[0] = p; else w.Pts[w.Pts.Count - 1] = p;
        }

        private static Chain Walk(List<Work> work, int[] partner, bool[] visited, int startEnd)
        {
            var ch = new Chain();
            int e = startEnd;
            while (true)
            {
                int k = e / 2;
                if (visited[k]) break;
                visited[k] = true;
                var w = work[k];
                bool fwd = e % 2 == 0;
                var pts = fwd ? w.Pts : Enumerable.Reverse(w.Pts).ToList();
                var bs = fwd ? w.B : Enumerable.Reverse(w.B).Select(b => -b).ToList();
                int skip = ch.Pts.Count > 0 ? 1 : 0;
                for (int i = skip; i < pts.Count; i++) ch.Pts.Add(pts[i]);
                ch.B.AddRange(bs);
                ch.Pieces.Add(w.Index);
                ch.MaxOffset = Math.Max(ch.MaxOffset, w.MaxOffset);
                int other = fwd ? e + 1 : e - 1;
                int next = partner[other];
                if (next < 0) break;
                e = next;
            }
            return ch;
        }

        /// <summary>Przyciąga koniec łańcucha do obrysu (przecięcie kierunku z bokiem albo rzut); zwraca parametr na obrysie.</summary>
        private static double SnapEnd(List<P> O, List<double> OB, Chain ch, bool start, double gapTol)
        {
            int n = O.Count;
            int last = ch.Pts.Count - 1;
            var p = start ? ch.Pts[0] : ch.Pts[last];
            var prev = start ? ch.Pts[1] : ch.Pts[last - 1];
            double eb = start ? ch.B[0] : ch.B[last - 1];
            OutlineDist(O, OB, p, out int j, straightOnly: true);
            var a = O[j]; var b = O[(j + 1) % n];
            var ab = b - a; double len2 = Dot(ab, ab);
            double f = len2 > 1e-24 ? Clamp01(Dot(p - a, ab) / len2) : 0;
            var snap = a + ab * f;
            if (Math.Abs(eb) <= 1e-9)
            {
                var d = p - prev; double cr = Cross(d, ab);
                if (Len(d) > 1e-9 && Math.Abs(cr) > 1e-6 * Len(d) * Math.Sqrt(len2))
                {
                    // przecięcie kierunku dojścia z bokiem: a + ab·u = p + d·s (linia osiowa zostaje osiowa)
                    double u = Cross(p - a, d) / Cross(ab, d);
                    if (u >= -1e-9 && u <= 1 + 1e-9)
                    {
                        var x = a + ab * u;
                        if (Dist(x, p) <= 2 * gapTol) { snap = x; f = Clamp01(u); }
                    }
                }
            }
            double t = j + f;
            if (Dist(snap, a) <= VertexSnap) { snap = a; t = j; }
            else if (Dist(snap, b) <= VertexSnap) { snap = b; t = (j + 1) % n; }
            if (start) ch.Pts[0] = snap; else ch.Pts[last] = snap;
            return t;
        }

        private static void Reverse(Chain ch)
        {
            ch.Pts.Reverse();
            ch.B.Reverse();
            for (int i = 0; i < ch.B.Count; i++) ch.B[i] = -ch.B[i];
            double t = ch.TFrom; ch.TFrom = ch.TTo; ch.TTo = t;
        }

        /// <summary>Pole pasa odciętego przez łańcuch: obrys od tFrom do tTo (naprzód) + łańcuch z powrotem.</summary>
        private static double StripArea(List<P> O, List<double> OB, List<P> chPts, List<double> chB,
                                        double tFrom, double tTo, bool reversed)
        {
            // Łańcuch zorientowany from → to: reversed = łańcuch zaczyna się w tTo
            var cp = reversed ? Enumerable.Reverse(chPts).ToList() : chPts.ToList();
            var cb = reversed ? Enumerable.Reverse(chB).Select(b => -b).ToList() : chB.ToList();
            var path = OutlinePath(O, cp[0], tFrom, cp[cp.Count - 1], tTo);
            var pts = new List<P>(); var bs = new List<double>();
            for (int k = 0; k < path.Count - 1; k++) { pts.Add(path[k].p); bs.Add(PathBulge(O.Count, OB, path[k].t, path[k + 1].t)); }
            // łańcuch od to z powrotem do from
            for (int k = cp.Count - 1; k >= 1; k--) { pts.Add(cp[k]); bs.Add(-cb[k - 1]); }
            return Area(pts, bs);
        }

        /// <summary>Obrys naprzód od (pa, ta) do (pb, tb): punkty z parametrami (końce włącznie).</summary>
        private static List<(P p, double t)> OutlinePath(List<P> O, P pa, double ta, P pb, double tb)
        {
            int n = O.Count;
            var res = new List<(P p, double t)> { (pa, ta) };
            double span = Fwd(n, ta, tb);
            if (span < 1e-12) span = n;   // pełne obejście
            var mids = new List<(P p, double t, double off)>();
            for (int k = 0; k < n; k++)
            {
                double off = Fwd(n, ta, k);
                if (off > 1e-12 && off < span - 1e-12) mids.Add((O[k], k, off));
            }
            res.AddRange(mids.OrderBy(m => m.off).Select(m => (m.p, m.t)));
            res.Add((pb, tb));
            return res;
        }

        private static double PathBulge(int n, List<double> OB, double ta, double tb)
        {
            if (!IsInt(ta) || !IsInt(tb)) return 0.0;
            int ka = (int)Math.Round(ta) % n, kb = (int)Math.Round(tb) % n;
            return (ka + 1) % n == kb ? OB[ka] : 0.0;
        }

        private static void Simplify(List<P> pts, List<double> bs)
        {
            bool changed = true;
            while (changed && pts.Count >= 3)
            {
                changed = false;
                for (int i = 0; i < pts.Count && pts.Count >= 3; i++)
                {
                    int n = pts.Count;
                    int ip = (i - 1 + n) % n, inx = (i + 1) % n;
                    if (Dist(pts[i], pts[inx]) <= DupTol)
                    {
                        // odcinek zerowy i → i+1: zostaje wybrzuszenie odcinka i+1 →
                        bs[i] = bs[inx];
                        pts.RemoveAt(inx); bs.RemoveAt(inx);
                        changed = true; break;
                    }
                    if (Math.Abs(bs[ip]) > 1e-9 || Math.Abs(bs[i]) > 1e-9) continue;
                    var a = pts[ip]; var b = pts[i]; var c = pts[inx];
                    var ac = c - a; double l = Len(ac);
                    if (l < 1e-9) continue;
                    if (Math.Abs(Cross(ac, b - a)) / l <= 1e-4 && Dot(b - a, c - b) > 0)
                    {
                        pts.RemoveAt(i); bs.RemoveAt(i);
                        changed = true; break;
                    }
                }
            }
        }

        // ── geometria ───────────────────────────────────────────────────

        private static List<P> CleanClosed(List<P> v, IList<double> bulges, out List<double> bs)
        {
            var pts = new List<P>(); bs = new List<double>();
            for (int i = 0; i < v.Count; i++)
            {
                double b = bulges != null && i < bulges.Count ? bulges[i] : 0.0;
                if (pts.Count > 0 && Dist(pts[pts.Count - 1], v[i]) <= DupTol) { bs[bs.Count - 1] = b; continue; }
                pts.Add(v[i]); bs.Add(b);
            }
            while (pts.Count > 2 && Dist(pts[0], pts[pts.Count - 1]) <= DupTol)
            {
                pts.RemoveAt(pts.Count - 1); bs.RemoveAt(bs.Count - 1);
            }
            return pts;
        }

        private static List<P> CleanOpen(List<P> v, IList<double> bulges, out List<double> bs)
        {
            var pts = new List<P>(); bs = new List<double>();
            for (int i = 0; i < v.Count; i++)
            {
                double b = bulges != null && i < bulges.Count ? bulges[i] : 0.0;
                if (pts.Count > 0 && Dist(pts[pts.Count - 1], v[i]) <= DupTol) { bs[bs.Count - 1] = b; continue; }
                pts.Add(v[i]); bs.Add(b);
            }
            if (bs.Count > 0) bs.RemoveAt(bs.Count - 1);
            return pts;
        }

        private static IEnumerable<P> Samples(List<P> pts, List<double> bs)
        {
            for (int i = 0; i < pts.Count; i++)
            {
                yield return pts[i];
                if (i < pts.Count - 1) yield return SegMid(pts[i], pts[i + 1], bs[i]);
            }
        }

        private static P SegMid(P a, P b, double bulge)
        {
            var m = (a + b) * 0.5;
            if (Math.Abs(bulge) <= 1e-9) return m;
            var d = b - a; double len = Len(d);
            if (len < 1e-12) return m;
            var left = new P(-d.Y / len, d.X / len);
            return m - left * (bulge * len / 2.0);   // dodatnie wybrzuszenie: łuk po prawej stronie cięciwy
        }

        private static bool ArcGeom(P a, P b, double bulge, out P c, out double r, out double a0, out double sweep)
        {
            c = default; r = 0; a0 = 0; sweep = 0;
            var d = b - a; double len = Len(d);
            if (Math.Abs(bulge) <= 1e-9 || len < 1e-12) return false;
            double s = len / 2.0;
            var left = new P(-d.Y / len, d.X / len);
            double h = s * (1 - bulge * bulge) / (2 * bulge);
            c = (a + b) * 0.5 + left * h;
            r = Dist(c, a);
            a0 = Math.Atan2(a.Y - c.Y, a.X - c.X);
            sweep = 4 * Math.Atan(bulge);
            return true;
        }

        private static double SegDist(P p, P a, P b, double bulge)
        {
            if (ArcGeom(a, b, bulge, out var c, out double r, out double a0, out double sweep))
            {
                double ap = Math.Atan2(p.Y - c.Y, p.X - c.X);
                double delta = (ap - a0) * Math.Sign(sweep);
                delta %= 2 * Math.PI; if (delta < 0) delta += 2 * Math.PI;
                if (delta <= Math.Abs(sweep)) return Math.Abs(Dist(p, c) - r);
                return Math.Min(Dist(p, a), Dist(p, b));
            }
            var ab = b - a; double len2 = Dot(ab, ab);
            double t = len2 > 1e-24 ? Clamp01(Dot(p - a, ab) / len2) : 0;
            return Dist(p, a + ab * t);
        }

        private static double OutlineDist(List<P> O, List<double> OB, P p, out int seg, bool straightOnly)
        {
            double best = double.MaxValue; seg = 0;
            int n = O.Count;
            for (int i = 0; i < n; i++)
            {
                if (straightOnly && Math.Abs(OB[i]) > 1e-9) continue;
                double d = SegDist(p, O[i], O[(i + 1) % n], OB[i]);
                if (d < best) { best = d; seg = i; }
            }
            return best;
        }

        private static List<P> Densify(List<P> pts, List<double> bs, bool closed)
        {
            var res = new List<P>();
            int n = pts.Count;
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % n];
                res.Add(a);
                if (ArcGeom(a, b, bs[i], out var c, out double r, out double a0, out double sweep))
                    for (int k = 1; k < 16; k++)
                    {
                        double ang = a0 + sweep * k / 16.0;
                        res.Add(new P(c.X + r * Math.Cos(ang), c.Y + r * Math.Sin(ang)));
                    }
            }
            if (!closed && n > 0) res.Add(pts[n - 1]);
            return res;
        }

        /// <summary>Signed pole wielokąta z łukami (dodatnie = CCW).</summary>
        private static double Area(List<P> pts, List<double> bs)
        {
            double a = 0; int n = pts.Count;
            for (int i = 0; i < n; i++)
            {
                var p = pts[i]; var q = pts[(i + 1) % n];
                a += (p.X * q.Y - q.X * p.Y) / 2.0;
                double b = bs != null && i < bs.Count ? bs[i] : 0.0;
                if (ArcGeom(p, q, b, out _, out double r, out _, out double sweep))
                {
                    double th = Math.Abs(sweep);
                    a += Math.Sign(b) * r * r / 2.0 * (th - Math.Sin(th));
                }
            }
            return a;
        }

        /// <summary>Pierwsze przecięcie polilinii A z polilinią B poza punktami „allowed” (± CrossTol).</summary>
        private static bool FirstCrossing(List<P> A, bool aClosed, List<P> B, bool bClosed, P[] allowed, out P hit)
        {
            hit = default;
            int na = aClosed ? A.Count : A.Count - 1, nb = bClosed ? B.Count : B.Count - 1;
            for (int i = 0; i < na; i++)
            {
                var a1 = A[i]; var a2 = A[(i + 1) % A.Count];
                for (int j = 0; j < nb; j++)
                {
                    var b1 = B[j]; var b2 = B[(j + 1) % B.Count];
                    foreach (var x in SegIntersections(a1, a2, b1, b2))
                        if (!allowed.Any(q => Dist(q, x) <= CrossTol)) { hit = x; return true; }
                }
            }
            return false;
        }

        private static IEnumerable<P> SegIntersections(P p1, P p2, P q1, P q2)
        {
            var r = p2 - p1; var s = q2 - q1;
            double rxs = Cross(r, s), lr = Len(r), ls = Len(s);
            if (lr < 1e-12 || ls < 1e-12) yield break;
            var qp = q1 - p1;
            if (Math.Abs(rxs) > 1e-12 * lr * ls)
            {
                double t = Cross(qp, s) / rxs, u = Cross(qp, r) / rxs;
                const double e = 1e-9;
                if (t >= -e && t <= 1 + e && u >= -e && u <= 1 + e) yield return p1 + r * t;
                yield break;
            }
            // równoległe: współliniowe → końce wspólnego zakresu
            if (Math.Abs(Cross(qp, r)) / lr > 1e-6) yield break;
            double rr = Dot(r, r);
            double t0 = Dot(q1 - p1, r) / rr, t1 = Dot(q2 - p1, r) / rr;
            double lo = Math.Max(0, Math.Min(t0, t1)), hi = Math.Min(1, Math.Max(t0, t1));
            if (lo > hi + 1e-12) yield break;
            yield return p1 + r * lo;
            if (hi - lo > 1e-12) yield return p1 + r * hi;
        }

        /// <summary>Parzystość przecięć poziomej półprostej — jak GeometryHelper.IsPointInsidePolygon.</summary>
        private static bool Inside(List<P> v, P t)
        {
            if (v == null || v.Count < 3) return false;
            int n = v.Count, left = 0;
            for (int i = 0; i < n; i++)
            {
                var p1 = v[i]; var p2 = v[(i + 1) % n];
                if (Math.Abs(p1.Y - p2.Y) < 1e-9) continue;
                if ((p1.Y <= t.Y && p2.Y > t.Y) || (p2.Y <= t.Y && p1.Y > t.Y))
                {
                    double x = p1.X + (t.Y - p1.Y) / (p2.Y - p1.Y) * (p2.X - p1.X);
                    if (x < t.X) left++;
                }
            }
            return left % 2 == 1;
        }

        // ── replika NibDetector ─────────────────────────────────────────

        /// <summary>Jak GeometryHelper.SquaredOrSame: kopia wyprostowana (szum ≤ 4 mm) albo te same wartości.</summary>
        private static List<P> NdSquared(IList<(double X, double Y)> v)
        {
            var r = OutlineAxisRules.Square(v.ToList());
            IEnumerable<(double X, double Y)> src = r.AxisAligned && r.Changed ? r.Points : v;
            return src.Select(t => new P(t.X, t.Y)).ToList();
        }

        private static List<P> NdClean(List<P> v)
        {
            var r = new List<P>();
            foreach (var p in v)
                if (r.Count == 0 || Dist(r[r.Count - 1], p) > 1e-3) r.Add(p);
            if (r.Count > 2 && Dist(r[0], r[r.Count - 1]) < 1e-3) r.RemoveAt(r.Count - 1);
            return r;
        }

        private static (double x0, double y0, double x1, double y1) Box(List<P> v)
            => (v.Min(p => p.X), v.Min(p => p.Y), v.Max(p => p.X), v.Max(p => p.Y));

        private static bool NdWithin((double x0, double y0, double x1, double y1) a,
                                     (double x0, double y0, double x1, double y1) b, double maxWidth)
        {
            double t = maxWidth + 1.0;
            bool In(double inset) => inset >= -1.0 && inset <= t;
            return In(a.x0 - b.x0) && In(a.y0 - b.y0) && In(b.x1 - a.x1) && In(b.y1 - a.y1);
        }

        private static double NdDistToBoundary(List<P> poly, P p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                var ab = b - a; double len2 = Dot(ab, ab);
                double t = len2 > 1e-12 ? Math.Max(0, Math.Min(1, Dot(p - a, ab) / len2)) : 0;
                best = Math.Min(best, Dist(a + ab * t, p));
            }
            return best;
        }

        /// <summary>Krawędzie zamkniętej listy: orient 1 = pionowa, 2 = pozioma, 0 = inna (jak EnumerateAxisAlignedEdges).</summary>
        private static List<(P a, P b, int orient)> AxisEdges(List<P> v)
        {
            var res = new List<(P a, P b, int orient)>();
            for (int i = 0; i < v.Count; i++)
            {
                var a = v[i]; var b = v[(i + 1) % v.Count];
                double dx = Math.Abs(b.X - a.X), dy = Math.Abs(b.Y - a.Y);
                int o = dx < AxisEps && dy < AxisEps ? 0 : dx < AxisEps ? 1 : dy < AxisEps ? 2 : 0;
                res.Add((a, b, o));
            }
            return res;
        }

        // ── drobne ──────────────────────────────────────────────────────

        private static double Fwd(int n, double a, double b) { double d = (b - a) % n; return d < 0 ? d + n : d; }
        private static bool IsInt(double t) => Math.Abs(t - Math.Round(t)) < 1e-12;
        private static double Dist(P a, P b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
        private static double Len(P a) => Math.Sqrt(a.X * a.X + a.Y * a.Y);
        private static double Dot(P a, P b) => a.X * b.X + a.Y * b.Y;
        private static double Cross(P a, P b) => a.X * b.Y - a.Y * b.X;
        private static double Clamp01(double t) => t < 0 ? 0 : t > 1 ? 1 : t;
        private static string F0(double v) => v.ToString("0", Inv);
        private static string F1(double v) => v.ToString("0.0", Inv);
        private static string F1g(double v) => v.ToString("0.#", Inv);
        private static string F2(double v) => v.ToString("0.00", Inv);
    }

    /// <summary>
    /// Obrys płyty z kawałków (uwaga Bartka 2026-10-28, GA PLOT 367: obrys SD-PILED-RAFT rozbity na linie i otwarte polilinie, kawałki
    /// stykają się także „na T” — koniec jednego w środku drugiego): odcinki dzielone w punktach przecięć / styków, graf płaski bez
    /// zwisających końców, ściany grafu; wynik — zewnętrzna granica składowej zawierającej punkt (największa granica zewnętrzna wokół
    /// punktu). Null — nie da się zamknąć.
    /// </summary>
    public static class OutlineFromPieces
    {
        public static List<(double X, double Y)> OuterBoundary(IList<((double X, double Y) A, (double X, double Y) B)> segs,
                                                              (double X, double Y) pt, double tol = 1.0)
        {
            if (segs == null || segs.Count < 3) return null;
            // 1. podział w punktach przecięć / styków
            var parts = new List<((double X, double Y) A, (double X, double Y) B)>();
            for (int i = 0; i < segs.Count; i++)
            {
                var (a, b) = segs[i];
                double dx = b.X - a.X, dy = b.Y - a.Y, L2 = dx * dx + dy * dy;
                if (L2 < tol * tol) continue;
                var ts = new List<double> { 0, 1 };
                for (int j = 0; j < segs.Count; j++)
                {
                    if (j == i) continue;
                    var (c, d) = segs[j];
                    foreach (var q in new[] { c, d })
                    {
                        double t = ((q.X - a.X) * dx + (q.Y - a.Y) * dy) / L2;
                        if (t <= 0 || t >= 1) continue;
                        double px = a.X + t * dx - q.X, py = a.Y + t * dy - q.Y;
                        if (px * px + py * py <= tol * tol) ts.Add(t);
                    }
                    double ex = d.X - c.X, ey = d.Y - c.Y, den = dx * ey - dy * ex;
                    if (Math.Abs(den) < 1e-9) continue;
                    double tt = ((c.X - a.X) * ey - (c.Y - a.Y) * ex) / den, uu = ((c.X - a.X) * dy - (c.Y - a.Y) * dx) / den;
                    if (tt > 0 && tt < 1 && uu >= 0 && uu <= 1) ts.Add(tt);
                }
                ts = ts.Distinct().OrderBy(t => t).ToList();
                for (int k = 1; k < ts.Count; k++)
                {
                    var p0 = (a.X + ts[k - 1] * dx, a.Y + ts[k - 1] * dy);
                    var p1 = (a.X + ts[k] * dx, a.Y + ts[k] * dy);
                    if (Math.Sqrt((p1.Item1 - p0.Item1) * (p1.Item1 - p0.Item1) + (p1.Item2 - p0.Item2) * (p1.Item2 - p0.Item2)) > tol) parts.Add((p0, p1));
                }
            }
            // 2. węzły (scalanie w tolerancji) i krawędzie bez powtórzeń
            var nodes = new List<(double X, double Y)>();
            int Node((double X, double Y) q)
            {
                for (int i = 0; i < nodes.Count; i++)
                    if (Math.Abs(nodes[i].X - q.X) <= tol && Math.Abs(nodes[i].Y - q.Y) <= tol) return i;
                nodes.Add(q);
                return nodes.Count - 1;
            }
            var edges = new HashSet<(int, int)>();
            foreach (var (a, b) in parts)
            {
                int u = Node(a), v = Node(b);
                if (u == v) continue;
                edges.Add(u < v ? (u, v) : (v, u));
            }
            var adj = new Dictionary<int, HashSet<int>>();
            foreach (var (u, v) in edges)
            {
                if (!adj.ContainsKey(u)) adj[u] = new HashSet<int>();
                if (!adj.ContainsKey(v)) adj[v] = new HashSet<int>();
                adj[u].Add(v); adj[v].Add(u);
            }
            // 3. bez zwisających końców
            bool pruned = true;
            while (pruned)
            {
                pruned = false;
                foreach (var n in adj.Keys.ToList())
                    if (adj.TryGetValue(n, out var nb) && nb.Count <= 1)
                    {
                        foreach (var m in nb) adj[m].Remove(n);
                        adj.Remove(n);
                        pruned = true;
                    }
            }
            if (adj.Count < 3) return null;
            double Ang(int u, int v) => Math.Atan2(nodes[v].Y - nodes[u].Y, nodes[v].X - nodes[u].X);
            var sorted = adj.ToDictionary(kv => kv.Key, kv => kv.Value.OrderBy(v => Ang(kv.Key, v)).ToList());
            // 4. ściany: z u→v dalej krawędzią najbliższą zgodnie z ruchem wskazówek od v→u (ściana po lewej)
            var usedD = new HashSet<(int, int)>();
            List<(double X, double Y)> best = null; double bestA = double.MaxValue;
            foreach (var u0 in sorted.Keys)
                foreach (var v0 in sorted[u0])
                {
                    if (usedD.Contains((u0, v0))) continue;
                    var cyc = new List<int>();
                    int u = u0, v = v0, guard = 0;
                    while (!usedD.Contains((u, v)) && guard++ < 100000)
                    {
                        usedD.Add((u, v)); cyc.Add(u);
                        var lst = sorted[v];
                        int idx = lst.IndexOf(u);
                        int w = lst[(idx - 1 + lst.Count) % lst.Count];
                        u = v; v = w;
                    }
                    if (cyc.Count < 3) continue;
                    var poly = cyc.Select(i => nodes[i]).ToList();
                    double area = 0;
                    for (int i = 0; i < poly.Count; i++) { var p = poly[i]; var q = poly[(i + 1) % poly.Count]; area += p.X * q.Y - q.X * p.Y; }
                    area /= 2;
                    // ściana po lewej: ograniczone dodatnie, zewnętrzna granica składowej ujemna; linie uskoku bywają osobną składową wewnątrz
                    // obrysu — bierzemy NAJWIĘKSZĄ granicę zewnętrzną wokół punktu (obrys zewnętrzny płyty)
                    if (area >= 0) continue;
                    if (!Inside(poly, pt)) continue;
                    if (best == null || -area > bestA) { bestA = -area; best = poly; }
                }
            if (best == null) return null;
            best.Reverse();   // przeciwnie do ruchu wskazówek
            // bez punktów współliniowych
            bool ch = true;
            while (ch && best.Count > 3)
            {
                ch = false;
                for (int i = 0; i < best.Count; i++)
                {
                    var a = best[(i - 1 + best.Count) % best.Count]; var b = best[i]; var c = best[(i + 1) % best.Count];
                    double cr = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                    double L = Math.Sqrt((c.X - a.X) * (c.X - a.X) + (c.Y - a.Y) * (c.Y - a.Y));
                    if (L > 1e-9 && Math.Abs(cr) / L <= tol * 0.5) { best.RemoveAt(i); ch = true; break; }
                }
            }
            return best.Count >= 3 ? best : null;
        }

        /// <summary>Odległość punktu od brzegu wielokąta.</summary>
        public static double DistToBoundary(IList<(double X, double Y)> poly, (double X, double Y) p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y, L2 = dx * dx + dy * dy;
                double t = L2 > 1e-12 ? Math.Max(0, Math.Min(1, ((p.X - a.X) * dx + (p.Y - a.Y) * dy) / L2)) : 0;
                double ex = a.X + t * dx - p.X, ey = a.Y + t * dy - p.Y;
                best = Math.Min(best, Math.Sqrt(ex * ex + ey * ey));
            }
            return best;
        }

        private static bool Inside(IList<(double X, double Y)> poly, (double X, double Y) p)
        {
            bool c = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y)
                    && p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X) c = !c;
            return c;
        }
    }
}
