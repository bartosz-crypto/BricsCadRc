using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Plan dozbrojenia dołem (B1 / B2 ADD) z map zbrojenia — czysta geometria (bez API BricsCAD, testowalna).
    /// Oś „along” = wzdłuż prętów (B1: X, B2: Y), „across” = oś rozkładu.
    ///   • strefa mapy: prostokąt (zakres rozkładu, z marginesem z mapy) + kontur (faktyczny obszar przekroczenia),
    ///   • pręt: kontur + ≥ 600 mm zakotwienia z każdej strony, min. 2000 mm, długość co 250, maks. 6000,
    ///   • rozkład co 200 mm, pokrywa cały prostokąt strefy w osi rozkładu,
    ///   • średnica wg wymaganego As z mapy: ≤ 2·393 → H10, ≤ 393+565 → H12, dalej H16 (ostrzeżenie > 393+1005),
    ///   • strefy blisko siebie (zachodzące wzdłuż prętów, odstęp w osi rozkładu ≤ 400) — jeden rozkład,
    ///   • położenia zaokrąglane do 50 mm względem najbliższego pala (wymiary „okrągłe”), pręty w obrysie płyty (otulina).
    /// </summary>
    public static class AddReinfPlanner
    {
        public const double Anchorage   = 600.0;
        public const double MinLength   = 2000.0;
        public const double MaxLength   = 6000.0;
        public const double LengthStep  = 250.0;
        public const double Spacing     = 200.0;
        public const double Cover       = 40.0;
        public const double MergeGap    = 400.0;
        public const double SnapStep    = 50.0;
        public const double PileRefDist = 3000.0;

        public const double BaseAs = 393.0;                    // H10@200 (siatka podstawowa dołem)
        public static double AsAt200(int dia) => Math.PI * dia * dia / 4.0 * 1000.0 / Spacing;   // H10 393, H12 565, H16 1005

        public sealed class Zone
        {
            public double RectA0, RectA1, RectC0, RectC1;      // prostokąt strefy z mapy (plan)
            public double ContA0, ContA1, ContC0, ContC1;      // kontur (NaN = brak)
            public double Value;                               // wymagane As [mm²/m]
            public string Label = "";                          // np. „#1” albo „430”
            public bool HasContour => !double.IsNaN(ContA0);
        }

        public sealed class Dist
        {
            public double A0, A1;          // końce prętów
            public double C0, C1;          // pierwszy / ostatni pręt
            public int    Count;
            public int    Diameter;
            public double Length => A1 - A0;
            public double Value;
            public List<int> Zones = new List<int>();
            public double RefA = double.NaN, RefC = double.NaN;   // pal odniesienia (wymiary); NaN = brak
            public List<string> Warnings = new List<string>();
        }

        public static int DiameterFor(double value)
        {
            if (value <= 2 * BaseAs + 1e-6) return 10;
            if (value <= BaseAs + AsAt200(12) + 1e-6) return 12;
            return 16;
        }

        /// <param name="horizontal">true = pręty wzdłuż X (B1), false = wzdłuż Y (B2)</param>
        /// <param name="slab">obrys płyty (x, y)</param>
        /// <param name="piles">środki pali (x, y)</param>
        public static List<Dist> Plan(List<Zone> zones, bool horizontal, List<(double x, double y)> slab,
                                      List<(double x, double y)> piles)
        {
            // 1. Wymagany zasięg każdej strefy
            var groups = zones.Select((z, i) => new G
            {
                Idx = new List<int> { i },
                NA0 = z.HasContour ? z.ContA0 - Anchorage : z.RectA0,
                NA1 = z.HasContour ? z.ContA1 + Anchorage : z.RectA1,
                C0 = z.RectC0, C1 = z.RectC1, Value = z.Value
            }).ToList();

            // 2. Scalanie stref zachodzących wzdłuż prętów i bliskich w osi rozkładu
            bool merged = true;
            while (merged)
            {
                merged = false;
                for (int i = 0; i < groups.Count && !merged; i++)
                    for (int j = i + 1; j < groups.Count && !merged; j++)
                    {
                        var a = groups[i]; var b = groups[j];
                        bool alongOverlap = Math.Min(a.NA1, b.NA1) - Math.Max(a.NA0, b.NA0) > 0;
                        double gap = Math.Max(a.C0, b.C0) - Math.Min(a.C1, b.C1);
                        if (!alongOverlap || gap > MergeGap) continue;
                        groups[i] = new G
                        {
                            Idx = a.Idx.Concat(b.Idx).ToList(),
                            NA0 = Math.Min(a.NA0, b.NA0), NA1 = Math.Max(a.NA1, b.NA1),
                            C0 = Math.Min(a.C0, b.C0), C1 = Math.Max(a.C1, b.C1),
                            Value = Math.Max(a.Value, b.Value)
                        };
                        groups.RemoveAt(j);
                        merged = true;
                    }
            }

            // 3. Rozkłady
            var result = new List<Dist>();
            foreach (var g in groups.OrderBy(g => g.C0).ThenBy(g => g.NA0))
            {
                var d = new Dist { Value = g.Value, Diameter = DiameterFor(g.Value), Zones = g.Idx };
                double need = g.NA1 - g.NA0;
                double len = Math.Max(MinLength, Math.Ceiling((need - 1e-6) / LengthStep) * LengthStep);
                if (len > MaxLength)
                {
                    d.Warnings.Add($"strefa wymaga pręta {need:F0} mm (+ zakotwienie) — przyjęto maks. {MaxLength:F0} mm, sprawdź ręcznie");
                    len = MaxLength;
                }
                double midA = (g.NA0 + g.NA1) / 2, midC = (g.C0 + g.C1) / 2;

                int n = Math.Max(2, (int)Math.Ceiling((g.C1 - g.C0) / Spacing - 1e-9) + 1);
                double span = (n - 1) * Spacing;
                double a0 = midA - len / 2, c0 = midC - span / 2;

                // Pal odniesienia (najbliższy środkowi strefy) — zaokrąglenie położeń do 50 mm względem pala
                var pile = piles.Select(p => (p, dist: Math.Sqrt(Sq(Al(p, horizontal) - midA) + Sq(Ac(p, horizontal) - midC))))
                                .Where(t => t.dist <= PileRefDist).OrderBy(t => t.dist).Select(t => ((double, double)?)t.p).FirstOrDefault();
                double refA = 0, refC = 0;
                if (pile.HasValue)
                {
                    refA = Al(pile.Value, horizontal); refC = Ac(pile.Value, horizontal);
                    d.RefA = refA; d.RefC = refC;
                }
                a0 = refA + Math.Round((a0 - refA) / SnapStep) * SnapStep;
                c0 = refC + Math.Round((c0 - refC) / SnapStep) * SnapStep;

                // W obrysie płyty: zakres prętów wzdłuż (cięciwy przez wszystkie pręty) i rozkładu (w środku długości)
                var (lo, hi) = AlongLimits(slab, horizontal, c0, c0 + span, a0 + len / 2);
                if (hi - lo < len)
                {
                    double fit = Math.Floor((hi - lo + 1e-6) / LengthStep) * LengthStep;
                    d.Warnings.Add($"płyta za krótka na pręt {len:F0} mm — przyjęto {fit:F0} mm");
                    len = fit;
                }
                if (a0 < lo) a0 = lo;
                if (a0 + len > hi) a0 = hi - len;

                var (clo, chi) = AcrossLimits(slab, horizontal, a0 + len / 2);
                while (n > 1 && c0 < clo - 1e-6) { c0 += Spacing; n--; }
                while (n > 1 && c0 + (n - 1) * Spacing > chi + 1e-6) n--;
                span = (n - 1) * Spacing;

                d.A0 = a0; d.A1 = a0 + len; d.C0 = c0; d.C1 = c0 + span; d.Count = n;

                // Kontrola pokrycia: kontur + 600 i prostokąt strefy w osi rozkładu
                foreach (int zi in g.Idx)
                {
                    var z = zones[zi];
                    double za0 = z.HasContour ? z.ContA0 - Anchorage : z.RectA0;
                    double za1 = z.HasContour ? z.ContA1 + Anchorage : z.RectA1;
                    if (d.A0 > za0 + 1 || d.A1 < za1 - 1)
                        d.Warnings.Add($"strefa {z.Label}: zakotwienie < {Anchorage:F0} mm z jednej strony (brak miejsca w płycie)");
                    if (d.C0 > z.RectC0 + Spacing / 2 + 1 || d.C1 < z.RectC1 - Spacing / 2 - 1)
                        d.Warnings.Add($"strefa {z.Label}: rozkład nie pokrywa całej strefy (brzeg płyty)");
                }
                result.Add(d);
            }
            return result;
        }

        private sealed class G
        {
            public List<int> Idx;
            public double NA0, NA1, C0, C1, Value;
        }

        private static double Sq(double v) => v * v;
        private static double Al((double x, double y) p, bool h) => h ? p.x : p.y;
        private static double Ac((double x, double y) p, bool h) => h ? p.y : p.x;

        /// <summary>Wspólny zakres wzdłuż prętów dla linii across c0..c1 (próbkowanie co 50), w otulinie.</summary>
        private static (double lo, double hi) AlongLimits(List<(double x, double y)> slab, bool h, double c0, double c1, double aMid)
        {
            double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
            for (double c = c0; c <= c1 + 1e-6; c += Math.Max(50.0, (c1 - c0) / 20))
            {
                var seg = Chord(slab, h, c, aMid);
                lo = Math.Max(lo, seg.lo); hi = Math.Min(hi, seg.hi);
            }
            return (lo + Cover, hi - Cover);
        }

        private static (double lo, double hi) AcrossLimits(List<(double x, double y)> slab, bool h, double aMid)
        {
            var seg = Chord(slab, !h, aMid, double.NaN);
            return (seg.lo + Cover, seg.hi - Cover);
        }

        /// <summary>
        /// Cięciwa wielokąta: linia stałej współrzędnej across = c (dla h: y = c, wynik w x).
        /// Zwraca odcinek zawierający <paramref name="near"/> (NaN = najdłuższy).
        /// </summary>
        public static (double lo, double hi) Chord(List<(double x, double y)> poly, bool h, double c, double near)
        {
            var xs = new List<double>();
            int n = poly.Count;
            for (int i = 0; i < n; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % n];
                double pc = h ? p.y : p.x, qc = h ? q.y : q.x;
                double pa = h ? p.x : p.y, qa = h ? q.x : q.y;
                if ((pc <= c && qc > c) || (qc <= c && pc > c))
                    xs.Add(pa + (c - pc) / (qc - pc) * (qa - pa));
            }
            xs.Sort();
            (double lo, double hi) best = (double.NaN, double.NaN);
            for (int i = 0; i + 1 < xs.Count; i += 2)
            {
                var s = (xs[i], xs[i + 1]);
                if (!double.IsNaN(near) && near >= s.Item1 - 1 && near <= s.Item2 + 1) return s;
                if (double.IsNaN(best.lo) || s.Item2 - s.Item1 > best.hi - best.lo) best = s;
            }
            return double.IsNaN(best.lo) ? (double.NegativeInfinity, double.PositiveInfinity) : best;
        }

        /// <summary>Pierwsza liczba w tekście („430”, „[!] 845” → 845); NaN gdy brak.</summary>
        public static double ParseValue(string text)
        {
            if (string.IsNullOrEmpty(text)) return double.NaN;
            var m = System.Text.RegularExpressions.Regex.Match(text, @"(?<![\w.=])(\d{2,5}(?:\.\d+)?)(?![\w.])");
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
        }
    }
}
