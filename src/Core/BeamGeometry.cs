#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Parametry belki / pogrubienia (rekord RC_BEAM — sekcja 4i ARCHITECTURE.md). Czysta klasa (bez Teigha).
    /// Wartości domyślne = rozstrzygnięcia DOMYŚLNE planu (docs/PLAN_BELKI_2026-10-10.md, 1a / 1b / 1c) —
    /// „domyślne, do potwierdzenia” przez użytkownika; każda do zmiany w oknie RC_BEAM.
    /// </summary>
    public sealed class BeamParams
    {
        /// <summary>Wysokość belki od wierzchu płyty [mm] (z opisu „450THK” w GA; 0 = nieznana).</summary>
        public double Height;
        /// <summary>Szerokość trzonu belki [mm] (z geometrii strefy; typowo 600).</summary>
        public double Width = 600;
        /// <summary>Nib belki: szerokość × wysokość [mm] (decyzja 1a: 135 × 150).</summary>
        public double NibWidth = 135, NibHeight = 150;
        /// <summary>Strzemiona: średnica i rozstaw (1a: H10 co 200; CP przy palach podaje H12@200 — decyzja Bartka).</summary>
        public int StirrupDia = 10;
        public double StirrupSpacing = 200;
        /// <summary>Pręty podłużne: dół i góra (1a: 4 H16 + 4 H16).</summary>
        public int BottomDia = 16, BottomCount = 4, TopDia = 16, TopCount = 4;
        /// <summary>U-bary na końcach (E4) — średnica.</summary>
        public int UBarDia = 16;
        /// <summary>Kod kształtu strzemienia (1a: 51).</summary>
        public string StirrupShape = "51";
        /// <summary>Zakład prętów podłużnych [mm] (1c: domyślnie 800 ≈ 50d, do potwierdzenia).</summary>
        public double Lap = 800;
        /// <summary>Maksymalna długość pręta [mm] (1b.5: jak płyta, 6000).</summary>
        public double MaxLength = 6000;
        /// <summary>Góra strzemienia pod: 2 = obiema warstwami T (domyślne, 1b.1), 1 = jedną warstwą T.</summary>
        public int StirrupTopLayers = 2;

        public BeamParams Clone() => (BeamParams)MemberwiseClone();
    }

    /// <summary>
    /// Belki / pogrubienia (downstand) — czysta geometria (bez Teigha), testy `tests/BeamGeometryTests.cs`.
    /// Strefa = zamknięta polilinia (warstwa RC-BEAM, kopia SD-Thickening-Zone z GA): pas szerokości ~600 wzdłuż krawędzi
    /// płyty (U, L+L, prosty). Analiza: pary równoległych, przeciwnie skierowanych krawędzi osiowych w odległości
    /// MinWidth–MaxWidth → prostokąty; prostokąty o wspólnym licu → odcinki (nogi); odcinek z przerwą / zmienną
    /// szerokością (skos 45°, PLOT 319) = „detal ręczny”. Narożnik L = kwadrat między końcami dwóch prostopadłych odcinków,
    /// T = koniec odcinka na licu bocznym innego. Pozostałe końce: Edge (lico końca ≤ nib od obrysu płyty) albo Internal.
    /// Liczby strzemion, plan prętów podłużnych, wymiary strzemienia 51 — też tutaj (testowalne).
    /// </summary>
    public static class BeamGeometry
    {
        /// <summary>Kody warstw prętów belki (NIE zaczynają się od B1/B2/T1/T2 — EraseOldDistributions płyty ich nie widzi).</summary>
        public const string CodeBottom = "BB", CodeTop = "BT", CodeLinks = "BL";
        /// <summary>Sufiksy opisów: „4 H16-05 BEAM B”, „4 H16-103 BEAM T”, „52 H10-07-200 LINKS”.</summary>
        public const string SuffixBottom = "BEAM B", SuffixTop = "BEAM T", SuffixLinks = "LINKS";

        public static bool IsBeamCode(string code)
            => code == CodeBottom || code == CodeTop || code == CodeLinks;

        public const double MinWidth = 150.0, MaxWidth = 1500.0;
        /// <summary>Krawędź osiowa z tolerancją 4 mm (jak obrys płyty — OutlineAxisRules).</summary>
        public const double AxisTol = 4.0;
        /// <summary>Styk prostokątów / narożników / wspólne lico [mm].</summary>
        public const double JoinTol = 5.0;
        /// <summary>Koniec przy krawędzi: lico końca najwyżej tyle od obrysu płyty (nib ≤ 255 + 5).</summary>
        public const double EdgeEndMaxGap = 260.0;
        /// <summary>Największa przerwa między kawałkami jednego odcinka (skos) [mm].</summary>
        public const double MaxLegGap = 3000.0;

        public enum EndKind { Edge, Internal, Corner, Tee }

        /// <summary>Odcinek belki (noga strefy) — oś wzdłuż X (AlongX) albo Y.</summary>
        public sealed class Segment
        {
            public bool AlongX;
            /// <summary>Lica boczne (y dla odcinka wzdłuż X, x dla pionowego).</summary>
            public double AcrossLo, AcrossHi;
            /// <summary>Własny zakres odcinka wzdłuż osi (bez kwadratów narożników) — strefa strzemion.</summary>
            public double OwnLo, OwnHi;
            /// <summary>Lica końców (narożnik L — lico zewnętrzne narożnika, T — dalsze lico belki poprzecznej).</summary>
            public double FaceLo, FaceHi;
            public EndKind LoKind = EndKind.Internal, HiKind = EndKind.Internal;
            /// <summary>Lico boczne przy krawędzi płyty: −1 = AcrossLo, +1 = AcrossHi, 0 = żadne.</summary>
            public int OuterSide;
            /// <summary>Odcinek nieosiowy / o zmiennej szerokości — bez prętów, komunikat „detal ręczny”.</summary>
            public bool Manual;
            public string ManualReason;

            public double Width => AcrossHi - AcrossLo;
            public double Axis => (AcrossLo + AcrossHi) / 2.0;
            public double Length => FaceHi - FaceLo;

            public override string ToString()
                => string.Format(CultureInfo.InvariantCulture, "{0} {1:0.#}..{2:0.#} @{3:0.#}..{4:0.#} {5}/{6}{7}",
                    AlongX ? "X" : "Y", FaceLo, FaceHi, AcrossLo, AcrossHi, LoKind, HiKind, Manual ? " MANUAL" : "");
        }

        public sealed class Result
        {
            public List<Segment> Segments = new List<Segment>();
            /// <summary>Komunikaty po polsku (bez prefiksu), liczby InvariantCulture.</summary>
            public List<string> Messages = new List<string>();
            /// <summary>Typowa szerokość (najczęstsza wśród odcinków osiowych; 0 = brak).</summary>
            public double TypicalWidth;
        }

        // ── wewnętrzne ──────────────────────────────────────────────────

        private sealed class Edge
        {
            public bool Horizontal;      // y = C
            public double C, Lo, Hi;
            public int Inward;           // +1: wnętrze po stronie rosnącej współrzędnej
        }

        private sealed class Rect
        {
            public bool AlongX;
            public double Lo, Hi;        // wzdłuż osi
            public double CLo, CHi;      // w poprzek
        }

        private sealed class Leg
        {
            public bool AlongX;
            public List<Rect> Rects = new List<Rect>();
            public double Lo, Hi, CLo, CHi;
            public bool Regular;
            public Segment Seg;
            public (double lo, double hi) EndAcross(bool atLo)
            {
                var r = atLo ? Rects.OrderBy(x => x.Lo).First() : Rects.OrderByDescending(x => x.Hi).First();
                return (r.CLo, r.CHi);
            }
        }

        /// <summary>
        /// Analiza strefy: odcinki z końcami Edge / Internal / Corner (L) / Tee. outline = obrys zewnętrzny płyty
        /// (null — końce Internal, bez lica zewnętrznego). Strefa bez krawędzi osiowych → komunikat, brak odcinków.
        /// </summary>
        public static Result Analyze(IList<(double X, double Y)> zone, IList<(double X, double Y)> outline)
        {
            var res = new Result();
            var poly = Clean(zone);
            if (poly.Count < 4)
            {
                res.Messages.Add("strefa belki: za mało wierzchołków — pominięto.");
                return res;
            }
            if (SignedArea(poly) < 0) poly.Reverse();
            var outl = outline != null ? Clean(outline) : null;

            var (axisEdges, diagonals) = Edges(poly);
            var rects = Pair(axisEdges, poly);
            if (rects.Count == 0)
            {
                res.Messages.Add("strefa belki bez par krawędzi osiowych (strefa nieosiowa) — detal ręczny.");
                return res;
            }

            var legs = GroupLegs(rects, poly);
            foreach (var leg in legs)
            {
                var s = new Segment
                {
                    AlongX = leg.AlongX, AcrossLo = leg.CLo, AcrossHi = leg.CHi,
                    OwnLo = leg.Lo, OwnHi = leg.Hi, FaceLo = leg.Lo, FaceHi = leg.Hi, Manual = !leg.Regular
                };
                if (s.Manual)
                {
                    bool skew = diagonals.Any(d => NearLeg(d, leg));
                    s.ManualReason = skew ? "odcinek nieosiowy (skos)" : "zmienna szerokość / przerwa w strefie";
                }
                leg.Seg = s;
                res.Segments.Add(s);
            }

            Joints(legs, poly);

            // Końce bez złącza: Edge / Internal; lico boczne przy krawędzi płyty
            foreach (var leg in legs)
            {
                var s = leg.Seg;
                if (s.LoKind == EndKind.Internal && outl != null && EndAtEdge(s, atLo: true, outl)) s.LoKind = EndKind.Edge;
                if (s.HiKind == EndKind.Internal && outl != null && EndAtEdge(s, atLo: false, outl)) s.HiKind = EndKind.Edge;
                if (outl != null) s.OuterSide = OuterSideOf(s, outl);
            }

            var widths = res.Segments.Where(x => !x.Manual).Select(x => Math.Round(x.Width)).ToList();
            res.TypicalWidth = widths.Count > 0
                ? widths.GroupBy(w => w).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key
                : Math.Round(rects.Min(r => r.CHi - r.CLo));

            PropagateManual(legs);

            // Kolejność deterministyczna: najpierw wzdłuż X, potem Y; po położeniu
            res.Segments = res.Segments.OrderBy(x => x.AlongX ? 0 : 1).ThenBy(x => x.AcrossLo).ThenBy(x => x.FaceLo).ToList();
            foreach (var s in res.Segments.Where(x => x.Manual))
                res.Messages.Add(string.Format(CultureInfo.InvariantCulture,
                    "odcinek {0} ({1:0}..{2:0}): {3} — detal ręczny, pręty pominięte.",
                    s.AlongX ? "poziomy" : "pionowy", s.FaceLo, s.FaceHi, s.ManualReason));
            return res;
        }

        private static List<(double X, double Y)> Clean(IList<(double X, double Y)> pts)
        {
            var r = new List<(double X, double Y)>();
            foreach (var p in pts)
                if (r.Count == 0 || Dist(r[r.Count - 1], p) > 1e-3) r.Add(p);
            if (r.Count > 2 && Dist(r[0], r[r.Count - 1]) < 1e-3) r.RemoveAt(r.Count - 1);
            // współliniowe wierzchołki (osiowe, z tolerancją) — usuń
            bool changed = true;
            while (changed && r.Count > 3)
            {
                changed = false;
                for (int i = 0; i < r.Count; i++)
                {
                    var a = r[(i - 1 + r.Count) % r.Count]; var b = r[i]; var c = r[(i + 1) % r.Count];
                    bool h = Math.Abs(a.Y - b.Y) <= AxisTol && Math.Abs(b.Y - c.Y) <= AxisTol;
                    bool v = Math.Abs(a.X - b.X) <= AxisTol && Math.Abs(b.X - c.X) <= AxisTol;
                    if (h || v) { r.RemoveAt(i); changed = true; break; }
                }
            }
            return r;
        }

        private static (List<Edge> axis, List<((double X, double Y) a, (double X, double Y) b)> diag)
            Edges(List<(double X, double Y)> poly)
        {
            var axis = new List<Edge>();
            var diag = new List<((double X, double Y), (double X, double Y))>();
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                if (Math.Abs(dy) <= AxisTol && Math.Abs(dx) > AxisTol)
                    // CCW: wnętrze po lewej; krawędź w +X → wnętrze +Y
                    axis.Add(new Edge { Horizontal = true, C = (a.Y + b.Y) / 2, Lo = Math.Min(a.X, b.X), Hi = Math.Max(a.X, b.X), Inward = dx > 0 ? 1 : -1 });
                else if (Math.Abs(dx) <= AxisTol && Math.Abs(dy) > AxisTol)
                    axis.Add(new Edge { Horizontal = false, C = (a.X + b.X) / 2, Lo = Math.Min(a.Y, b.Y), Hi = Math.Max(a.Y, b.Y), Inward = dy > 0 ? -1 : 1 });
                else if (Math.Abs(dx) > AxisTol || Math.Abs(dy) > AxisTol)
                    diag.Add((a, b));
            }
            return (axis, diag);
        }

        /// <summary>Pary krawędzi „lico — lico”: dla każdej krawędzi najbliższa przeciwległa w zakresie MinWidth–MaxWidth.</summary>
        private static List<Rect> Pair(List<Edge> edges, List<(double X, double Y)> poly)
        {
            var rects = new List<Rect>();
            foreach (var e in edges)
            {
                var cands = edges.Where(f => f != e && f.Horizontal == e.Horizontal && f.Inward == -e.Inward)
                                 .Select(f => (f, d: (f.C - e.C) * e.Inward))
                                 .Where(x => x.d >= MinWidth && x.d <= MaxWidth)
                                 .OrderBy(x => x.d).ToList();
                var free = new List<(double lo, double hi)> { (e.Lo, e.Hi) };
                foreach (var (f, d) in cands)
                {
                    var next = new List<(double lo, double hi)>();
                    foreach (var (lo, hi) in free)
                    {
                        double olo = Math.Max(lo, f.Lo), ohi = Math.Min(hi, f.Hi);
                        if (ohi - olo <= 1.0) { next.Add((lo, hi)); continue; }
                        // tylko raz na parę (krawędź o mniejszej współrzędnej) — druga da ten sam prostokąt
                        if (e.C < f.C)
                        {
                            var r = new Rect { AlongX = e.Horizontal, Lo = olo, Hi = ohi, CLo = e.C, CHi = f.C };
                            var mid = r.AlongX ? ((r.Lo + r.Hi) / 2, (r.CLo + r.CHi) / 2) : ((r.CLo + r.CHi) / 2, (r.Lo + r.Hi) / 2);
                            if (Inside(poly, mid)) rects.Add(r);
                        }
                        if (olo - lo > 1.0) next.Add((lo, olo));
                        if (hi - ohi > 1.0) next.Add((ohi, hi));
                    }
                    free = next;
                    if (free.Count == 0) break;
                }
            }
            return rects;
        }

        /// <summary>Prostokąty tej samej orientacji o wspólnym licu (przerwa w strefie) → jeden odcinek.</summary>
        private static List<Leg> GroupLegs(List<Rect> rects, List<(double X, double Y)> poly)
        {
            int n = rects.Count;
            var parent = Enumerable.Range(0, n).ToArray();
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    var a = rects[i]; var b = rects[j];
                    if (a.AlongX != b.AlongX) continue;
                    bool sideLo = Math.Abs(a.CLo - b.CLo) <= JoinTol, sideHi = Math.Abs(a.CHi - b.CHi) <= JoinTol;
                    if (!sideLo && !sideHi) continue;
                    double gap = Math.Max(a.Lo, b.Lo) - Math.Min(a.Hi, b.Hi);
                    if (gap > MaxLegGap) continue;
                    if (gap > JoinTol)
                    {
                        // przerwa musi leżeć w strefie (np. skos) — próbka przy wspólnym licu
                        double g = (Math.Min(a.Hi, b.Hi) + Math.Max(a.Lo, b.Lo)) / 2;
                        double c = sideLo ? a.CLo + 1.0 : a.CHi - 1.0;
                        if (!Inside(poly, a.AlongX ? (g, c) : (c, g))) continue;
                    }
                    parent[Find(i)] = Find(j);
                }
            var legs = new List<Leg>();
            foreach (var grp in Enumerable.Range(0, n).GroupBy(Find))
            {
                var rs = grp.Select(i => rects[i]).OrderBy(r => r.Lo).ToList();
                var leg = new Leg { AlongX = rs[0].AlongX, Rects = rs, Lo = rs.Min(r => r.Lo), Hi = rs.Max(r => r.Hi) };
                bool sameSide = rs.All(r => Math.Abs(r.CLo - rs[0].CLo) <= JoinTol && Math.Abs(r.CHi - rs[0].CHi) <= JoinTol);
                bool contiguous = true;
                for (int k = 1; k < rs.Count; k++) if (rs[k].Lo - rs[k - 1].Hi > JoinTol) contiguous = false;
                leg.Regular = sameSide && contiguous;
                leg.CLo = leg.Regular ? rs.Average(r => r.CLo) : rs.Min(r => r.CLo);
                leg.CHi = leg.Regular ? rs.Average(r => r.CHi) : rs.Max(r => r.CHi);
                legs.Add(leg);
            }
            return legs;
        }

        private static bool NearLeg(((double X, double Y) a, (double X, double Y) b) d, Leg leg)
        {
            double mx = (d.a.X + d.b.X) / 2, my = (d.a.Y + d.b.Y) / 2;
            double along = leg.AlongX ? mx : my, across = leg.AlongX ? my : mx;
            return along >= leg.Lo - JoinTol && along <= leg.Hi + JoinTol
                && across >= leg.CLo - MaxWidth && across <= leg.CHi + MaxWidth;
        }

        /// <summary>Narożniki L (kwadrat między końcami dwóch prostopadłych odcinków) i złącza T.</summary>
        private static void Joints(List<Leg> legs, List<(double X, double Y)> poly)
        {
            foreach (var a in legs.Where(l => l.AlongX))
                foreach (var b in legs.Where(l => !l.AlongX))
                    foreach (bool aLo in new[] { true, false })
                        foreach (bool bLo in new[] { true, false })
                        {
                            var (ya0, ya1) = a.EndAcross(aLo);
                            var (xb0, xb1) = b.EndAcross(bLo);
                            bool aTouch = aLo ? Math.Abs(xb1 - a.Lo) <= JoinTol : Math.Abs(xb0 - a.Hi) <= JoinTol;
                            bool bTouch = bLo ? Math.Abs(ya1 - b.Lo) <= JoinTol : Math.Abs(ya0 - b.Hi) <= JoinTol;
                            if (!aTouch || !bTouch) continue;
                            if (!Inside(poly, ((xb0 + xb1) / 2, (ya0 + ya1) / 2))) continue;
                            if (aLo) { a.Seg.LoKind = EndKind.Corner; a.Seg.FaceLo = xb0; }
                            else     { a.Seg.HiKind = EndKind.Corner; a.Seg.FaceHi = xb1; }
                            if (bLo) { b.Seg.LoKind = EndKind.Corner; b.Seg.FaceLo = ya0; }
                            else     { b.Seg.HiKind = EndKind.Corner; b.Seg.FaceHi = ya1; }
                        }

            // T: koniec odcinka na licu bocznym innego (prostopadłego), wewnątrz jego zakresu
            foreach (var b in legs)
                foreach (bool bLo in new[] { true, false })
                {
                    if ((bLo ? b.Seg.LoKind : b.Seg.HiKind) != EndKind.Internal) continue;
                    double end = bLo ? b.Lo : b.Hi;
                    var (c0, c1) = b.EndAcross(bLo);
                    foreach (var a in legs.Where(l => l.AlongX != b.AlongX))
                    {
                        if (c0 < a.Lo + JoinTol || c1 > a.Hi - JoinTol) continue;
                        double face;
                        if (bLo && Math.Abs(end - a.CHi) <= JoinTol) face = a.CLo;        // b nad a — dalsze lico a
                        else if (!bLo && Math.Abs(end - a.CLo) <= JoinTol) face = a.CHi;
                        else continue;
                        if (bLo) { b.Seg.LoKind = EndKind.Tee; b.Seg.FaceLo = face; }
                        else     { b.Seg.HiKind = EndKind.Tee; b.Seg.FaceHi = face; }
                        break;
                    }
                }
        }

        /// <summary>
        /// Odcinek, którego koniec bez rozpoznanego złącza (Internal / Edge — nie Corner / Tee) przylega (wspólny koniec / naroże,
        /// tolerancja JoinTol) do odcinka „detal ręczny”, też staje się „detal ręczny” (bez prętów i strzemion) — inaczej pręty
        /// kończyłyby się w środku węzła (np. L z krótką nogą 600 × 1200). Powtarzane do ustalenia (łańcuch odcinków).
        /// Złącza rozpoznane (narożnik L / T z odcinkiem ręcznym — PLOT 319) bez zmian.
        /// </summary>
        private static void PropagateManual(List<Leg> legs)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var leg in legs)
                {
                    var s = leg.Seg;
                    if (s.Manual) continue;
                    foreach (bool atLo in new[] { true, false })
                    {
                        var kind = atLo ? s.LoKind : s.HiKind;
                        if (kind == EndKind.Corner || kind == EndKind.Tee) continue;
                        double along = atLo ? s.OwnLo : s.OwnHi;
                        // lico końca jako odcinek (prostokąt zdegenerowany) w układzie X / Y
                        double x0, x1, y0, y1;
                        if (s.AlongX) { x0 = x1 = along; y0 = s.AcrossLo; y1 = s.AcrossHi; }
                        else          { y0 = y1 = along; x0 = s.AcrossLo; x1 = s.AcrossHi; }
                        var hit = legs.FirstOrDefault(m => m != leg && m.Seg.Manual && m.Rects.Any(r =>
                        {
                            double rx0 = r.AlongX ? r.Lo : r.CLo, rx1 = r.AlongX ? r.Hi : r.CHi;
                            double ry0 = r.AlongX ? r.CLo : r.Lo, ry1 = r.AlongX ? r.CHi : r.Hi;
                            return x0 <= rx1 + JoinTol && x1 >= rx0 - JoinTol && y0 <= ry1 + JoinTol && y1 >= ry0 - JoinTol;
                        }));
                        if (hit == null) continue;
                        s.Manual = true;
                        s.ManualReason = "przylega do odcinka „detal ręczny” (węzeł nierozpoznany)";
                        changed = true;
                        break;
                    }
                }
            }
        }

        private static bool EndAtEdge(Segment s, bool atLo, List<(double X, double Y)> outline)
        {
            double along = atLo ? s.OwnLo : s.OwnHi;
            int dir = atLo ? -1 : 1;
            var p = s.AlongX ? (along, s.Axis) : (s.Axis, along);
            var v = s.AlongX ? ((double)dir, 0.0) : (0.0, (double)dir);
            double d = RayHit(outline, p, v);
            return d <= EdgeEndMaxGap;
        }

        private static int OuterSideOf(Segment s, List<(double X, double Y)> outline)
        {
            double mid = (s.OwnLo + s.OwnHi) / 2;
            double best = double.MaxValue; int side = 0;
            foreach (int sgn in new[] { -1, 1 })
            {
                double c = sgn < 0 ? s.AcrossLo : s.AcrossHi;
                var p = s.AlongX ? (mid, c) : (c, mid);
                var v = s.AlongX ? (0.0, (double)sgn) : ((double)sgn, 0.0);
                double d = RayHit(outline, p, v);
                if (d <= EdgeEndMaxGap && d < best) { best = d; side = sgn; }
            }
            return side;
        }

        /// <summary>Odległość od p wzdłuż kierunku v (jednostkowego, osiowego) do najbliższego przecięcia z obrysem; ∞ gdy brak.</summary>
        private static double RayHit(List<(double X, double Y)> poly, (double X, double Y) p, (double X, double Y) v)
        {
            double best = double.PositiveInfinity;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                double ex = b.X - a.X, ey = b.Y - a.Y;
                double den = v.X * ey - v.Y * ex;
                if (Math.Abs(den) < 1e-12) continue;
                double wx = a.X - p.X, wy = a.Y - p.Y;
                double t = (wx * ey - wy * ex) / den;      // wzdłuż promienia
                double u = (wx * v.Y - wy * v.X) / den;    // wzdłuż krawędzi
                if (t >= -1e-6 && u >= -1e-9 && u <= 1 + 1e-9 && t < best) best = Math.Max(0, t);
            }
            return best;
        }

        // ── strzemiona, plan prętów, wymiary ─────────────────────────────

        /// <summary>
        /// Strzemiona w zakresie [lo, hi] (własny zakres odcinka): bez UB 03 — n = ceil((L − 2·endOffset)/s) + 1,
        /// równo od lo + endOffset do hi − endOffset (rozstaw ≤ s, plan 1c). Z UB 03 wzdłuż tego odcinka (położenia
        /// ub03, rozstaw ≤ s) — położenia UB 03 + shift (domyślnie 100: w połowie między U-barami, kolizja dolnych ramion),
        /// rozstaw jak UB 03, w granicach [lo + endOffset, hi − endOffset]. Wynik: pierwszy, rozstaw, liczba.
        /// </summary>
        public static (double First, double Step, int Count, bool FromUb03) StirrupLayout(
            double lo, double hi, double spacing = 200, double endOffset = 50, IList<double> ub03 = null, double shift = 100)
        {
            double L = hi - lo;
            if (L <= 0 || spacing <= 0) return (lo, 0, 0, false);
            if (L <= 2 * endOffset + 1e-6) return ((lo + hi) / 2, 0, 1, false);

            if (ub03 != null && ub03.Count >= 2)
            {
                var u = ub03.OrderBy(x => x).ToList();
                double su = (u[u.Count - 1] - u[0]) / (u.Count - 1);
                if (su > 0.5 * spacing && su <= spacing + 0.5)
                {
                    double b = u[0] + shift;
                    int kmin = (int)Math.Ceiling((lo + endOffset - b) / su - 1e-9);
                    int kmax = (int)Math.Floor((hi - endOffset - b) / su + 1e-9);
                    int cnt = kmax - kmin + 1;
                    if (cnt >= 2) return (b + kmin * su, su, cnt, true);
                }
            }

            double span = L - 2 * endOffset;
            int n = (int)Math.Ceiling(span / spacing - 1e-9) + 1;
            return (lo + endOffset, n > 1 ? span / (n - 1) : 0, n, false);
        }

        /// <summary>
        /// Plan prętów podłużnych (proste, siatka 250, maks. maxLen, zakład w przedziale [lap, lap + grid) — decyzja 1c: 800,
        /// zakład 800 … &lt; 1050; domyślne, do potwierdzenia).
        /// available ≤ maxLen → jeden pręt przycięty w dół do siatki (jak ComputeDistributionPlan; krótszy niż minLen — do 25 mm),
        /// WYŚRODKOWANY (offset = (available − L)/2 — równa otulina na obu końcach).
        /// Dłużej (N = najmniejsza liczba prętów; mniej zakładów zdecydowanie lepiej): a) N prętów równych (jedna pozycja) na siatce
        /// (≤ maxLen), równe zakłady (N·L − available)/(N−1) w przedziale; b) N prętów: N − 1 równych na siatce (zakład = lap)
        /// + ostatni krótszy na siatce (osobna pozycja), jego zakład w przedziale; c) dopiero potem N + 1 prętów równych.
        /// Np. 6251 → 3750 + 3500 (zakład 999), nie 3 × 2750.
        /// Wynik: (początek względem lico + otulina, długość).
        /// </summary>
        public static List<(double Offset, double Length)> PlanBars(double available, double maxLen = 6000, double lap = 800,
                                                                    double grid = 250, double minLen = 1250)
        {
            var res = new List<(double, double)>();
            if (available <= 0 || maxLen <= 0 || grid <= 0) return res;
            if (available <= maxLen + 0.5)
            {
                double L = Math.Floor(available / grid + 1e-9) * grid;
                if (L < minLen) L = Math.Floor(available / 25.0 + 1e-9) * 25.0;
                L = Math.Min(L, maxLen);
                if (L > 0) res.Add((Math.Max(0.0, (available - L) / 2.0), L));
                return res;
            }
            if (lap >= maxLen) return res;
            double lapHi = lap + grid;                                    // zakład < lap + grid
            double gridMax = Math.Floor(maxLen / grid + 1e-9) * grid;     // najdłuższy pręt na siatce
            int n0 = Math.Max(2, (int)Math.Ceiling((available - lap) / (maxLen - lap) - 1e-9));
            // Mniej zakładów zdecydowanie lepiej (zasada AutoRebar): dla N prętów najpierw a) równe, potem b) N − 1 równych +
            // ostatni krótszy; dopiero gdy przy N nie wyjdzie żaden wariant — c) N + 1 równych (i dalej kolejne N).
            for (int N = n0; N <= n0 + 20; N++)
            {
                if (TryEqualBars(available, N, lap, lapHi, grid, gridMax, res)) return res;      // a) N równych
                // b) N − 1 prętów równych (zakład = lap) + ostatni krótszy na siatce (zakład w przedziale)
                {
                    double L = Math.Ceiling((available + (N - 1) * lap) / N / grid - 1e-9) * grid;
                    if (L <= gridMax + 1e-6)
                    {
                        double end = (N - 1) * L - (N - 2) * lap;             // koniec (N−1)-ego pręta
                        double need = available - end + lap;                  // ostatni pręt z zakładem lap
                        double Llast = Math.Ceiling(need / grid - 1e-9) * grid;
                        if (need > lap + 1e-6                                 // N − 1 prętów nie wystarcza
                            && Llast <= L + 1e-6 && Llast <= gridMax + 1e-6)
                        {
                            for (int i = 0; i < N - 1; i++) res.Add((i * (L - lap), L));
                            res.Add((available - Llast, Llast));
                            return res;
                        }
                    }
                }
                if (TryEqualBars(available, N + 1, lap, lapHi, grid, gridMax, res)) return res;  // c) N + 1 równych
            }
            return res;
        }

        /// <summary>
        /// Pręty równe (jedna pozycja): n prętów na siatce (≤ gridMax), równe zakłady (n·L − available)/(n − 1) w [lap, lapHi).
        /// Dopisuje plan do res i zwraca true; gdy się nie da — false (res bez zmian).
        /// </summary>
        private static bool TryEqualBars(double available, int n, double lap, double lapHi, double grid, double gridMax,
                                         List<(double, double)> res)
        {
            if (n < 2) return false;
            double L = Math.Ceiling((available + (n - 1) * lap) / n / grid - 1e-9) * grid;
            if (L > gridMax + 1e-6) return false;
            double O = (n * L - available) / (n - 1);
            if (O < lap - 1e-6 || O >= lapHi - 1e-6) return false;
            for (int i = 0; i < n; i++) res.Add((i * (L - O), L));
            return true;
        }

        /// <summary>Zakład planu (z dwóch pierwszych prętów); 0 dla jednego pręta.</summary>
        public static double LapOf(List<(double Offset, double Length)> plan)
            => plan != null && plan.Count > 1 ? plan[0].Offset + plan[0].Length - plan[1].Offset : 0;

        /// <summary>Wszystkie zakłady planu (między kolejnymi prętami); pusta lista dla jednego pręta.</summary>
        public static List<double> LapsOf(List<(double Offset, double Length)> plan)
        {
            var r = new List<double>();
            if (plan == null) return r;
            for (int i = 1; i < plan.Count; i++) r.Add(plan[i - 1].Offset + plan[i - 1].Length - plan[i].Offset);
            return r;
        }

        /// <summary>
        /// Wysokość strzemienia 51 (wymiar zewnętrzny A, 1b.1): H − otulina górą − dT1 (− dT2 przy topLayers = 2) − otulina dołem,
        /// zaokrąglona W DÓŁ do 5 mm. Np. 450, H12 + H12: 450 − 35 − 24 − 40 = 351 → 350.
        /// </summary>
        public static double StirrupHeight(double h, int topLayers = 2, double dT1 = 12, double dT2 = 12,
                                           double coverTop = 35, double coverBottom = 40)
        {
            double a = h - coverTop - dT1 - (topLayers >= 2 ? dT2 : 0) - coverBottom;
            return a > 0 ? Math.Floor(a / 5.0 + 1e-9) * 5.0 : 0;
        }

        /// <summary>
        /// Szerokość strzemienia 51 (wymiar zewnętrzny B): szerokość − 2 · otulina boczna, w dół do 5 mm (600 → 520; 600.4 → 520 —
        /// strefa z GA z szumem rysunku nie daje osobnej pozycji).
        /// </summary>
        public static double StirrupWidth(double width, double coverSide = 40)
        {
            double b = width - 2 * coverSide;
            return b > 0 ? Math.Floor(b / 5.0 + 1e-9) * 5.0 : 0;
        }

        /// <summary>
        /// Pręty podłużne w poprzek belki: pierwszy / ostatni w osi otulina + strzemię + d/2 od lica, liczba prętów równo
        /// (600, H10, H16: 58 … 542, co 161.3). Zwraca (pierwszy, rozstaw).
        /// </summary>
        public static (double First, double Spacing) BarsAcross(double lo, double hi, int count, double d,
                                                                double dLink = 10, double coverSide = 40)
        {
            double off = coverSide + dLink + d / 2.0;
            double a = lo + off, b = hi - off;
            if (count <= 1 || b <= a) return ((lo + hi) / 2, 0);
            return (a, (b - a) / (count - 1));
        }

        /// <summary>Wysokość belki z opisu GA („450THK”, „375 THK”; bez „mm” — to grubość płyty); null gdy brak.</summary>
        public static int? ParseThk(string line)
        {
            if (string.IsNullOrEmpty(line) || Regex.IsMatch(line, @"SLAB", RegexOptions.IgnoreCase)) return null;
            var m = Regex.Match(line, @"(?<![\d.,])(\d{3,4})\s*THK\b", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        /// <summary>Kontrola krzyżowa wysokości: SSL − NIB TOC + wysokość nibu (319: 44.375 − 44.075 + 150 = 450).</summary>
        public static int HeightFromLevels(double ssl, double nibToc, double nibHeight = 150)
            => (int)Math.Round((ssl - nibToc) * 1000.0 + nibHeight, MidpointRounding.AwayFromZero);

        /// <summary>„NIB TOC=44.075” → 44.075; null gdy brak.</summary>
        public static double? ParseNibToc(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var m = Regex.Match(text, @"NIB\s*TOC\s*[=:]?\s*\+?(-?\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            return double.Parse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // ── pomocnicze ──────────────────────────────────────────────────

        public static bool Inside(IList<(double X, double Y)> poly, (double X, double Y) p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static double SignedArea(List<(double X, double Y)> v)
        {
            double a = 0;
            for (int i = 0; i < v.Count; i++) { var p = v[i]; var q = v[(i + 1) % v.Count]; a += p.X * q.Y - q.X * p.Y; }
            return a / 2;
        }

        private static double Dist((double X, double Y) a, (double X, double Y) b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }
}
