using System;
using System.Collections.Generic;
using System.Linq;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Zakłady GÓRY omijające pale — przeszukanie szersze niż wzór „pierwszy, środkowe równe, ostatni” z jednym zakładem
    /// (decyzja 2026-10-15, płyta z 14 palami: przy wzorze żaden układ nie mijał pali → „zakład nachodzi na pal”).
    /// Każdy pręt dowolnej długości z siatki 250 (minLen … maxLen), każdy zakład osobno w [oMin, oMax] (krok 25, ostatni dowolny),
    /// strefy zakładów: poza strefami zakazanymi pali (lico ± odstęp) i ≥ staggerGap w świetle od zakładów dołu.
    /// Najpierw najmniej prętów, potem dłuższe pręty i zakład bliżej docelowego. Czysta geometria — bez API BricsCAD.
    /// </summary>
    public static class TopLapSearch
    {
        public const int MaxNodes = 20000;   // budżet na jedną liczbę prętów N (dół próbuje wielu planów — szybko)

        /// <summary>
        /// Bezpiecznik czasu (uwaga Bartka 2026-10-30: RC_GENERATE → Mesh zawieszał BricsCAD — płyta 25,7 m z 40 palami, góra nie mieściła
        /// zakładów między palami i przeszukanie szło w miliardy prób): jedno wywołanie <see cref="Find"/> najwyżej <see cref="FindBudgetMs"/>,
        /// a cały plan pasa — do <see cref="Deadline"/> (Stopwatch; 0 = bez limitu). Po czasie — null (plan awaryjny z ostrzeżeniem).
        /// </summary>
        public const int FindBudgetMs = 250;
        public static long Deadline;
        public static int TimeOuts;

        /// <summary>Limit czasu całego planu pasa (ms od teraz); zwraca poprzedni (do odtworzenia w finally).</summary>
        public static long BeginBudget(int ms)
        {
            long prev = Deadline;
            long d = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(ms / 1000.0 * System.Diagnostics.Stopwatch.Frequency);
            Deadline = prev != 0 && prev < d ? prev : d;
            return prev;
        }

        public static bool Expired => Deadline != 0 && System.Diagnostics.Stopwatch.GetTimestamp() > Deadline;

        public static List<(double xOffset, double length)> Find(
            double available, IList<(double lo, double hi)> bottomLaps, IList<(double lo, double hi)> forbidden,
            int nMin, int nMax, double minLen, double maxLen, double gridStep,
            double oMin, double oMax, double oTarget, double staggerGap)
        {
            if (available <= maxLen + 0.5 || nMin < 2) return null;
            var grid = new List<double>();
            for (double L = maxLen; L >= minLen - 1e-6; L -= gridStep) grid.Add(L);
            var overlaps = new List<double>();
            for (double o = oMin; o <= oMax + 1e-6; o += 25.0) overlaps.Add(o);
            overlaps = overlaps.OrderBy(o => Math.Abs(o - oTarget)).ToList();

            bool Clear(double lo, double hi)
            {
                if (forbidden != null)
                    foreach (var f in forbidden)
                        if (Math.Min(hi, f.hi) - Math.Max(lo, f.lo) > -1e-6) return false;
                if (bottomLaps != null)
                    foreach (var b in bottomLaps)
                        if (Math.Max(lo - b.hi, b.lo - hi) < staggerGap - 1e-6) return false;
                return true;
            }

            if (Expired) { TimeOuts++; return null; }
            long end = System.Diagnostics.Stopwatch.GetTimestamp() + (long)(FindBudgetMs / 1000.0 * System.Diagnostics.Stopwatch.Frequency);
            if (Deadline != 0 && Deadline < end) end = Deadline;
            bool timedOut = false;
            // stany bez rozwiązania (pozostało prętów, początek pręta) — wynik zależy tylko od nich, wspólne dla wszystkich N
            // (te same rozwiązania w tej samej kolejności, bez ponownego przeszukiwania tych samych gałęzi)
            var failed = new HashSet<(int, long)>();
            for (int N = nMin; N <= nMax; N++)
            {
                var plan = new List<(double, double)>();
                int nodes = 0;
                bool Dfs(int left, double x)
                {
                    var key = (left, (long)Math.Round(x));
                    if (failed.Contains(key)) return false;
                    if (++nodes > MaxNodes) return false;
                    if ((nodes & 63) == 0 && System.Diagnostics.Stopwatch.GetTimestamp() > end) { timedOut = true; return false; }
                    bool ok = DfsCore(left, x);
                    if (!ok && !timedOut && nodes <= MaxNodes) failed.Add(key);
                    return ok;
                }
                bool DfsCore(int left, double x)
                {
                    if (left == 2)
                    {
                        // przedostatni pręt z siatki + ostatni z siatki dokładnie do końca; zakład dowolny w [oMin, oMax]
                        foreach (double L in grid)
                            foreach (double Ll in grid)
                            {
                                double s = available - Ll, e = x + L, o = e - s;
                                if (o < oMin - 1e-6 || o > oMax + 1e-6 || s <= x + 1e-6) continue;
                                if (!Clear(s, e)) continue;
                                plan.Add((x, L)); plan.Add((s, Ll));
                                return true;
                            }
                        // uwaga Bartka 2026-10-26: okno zakładu węższe niż siatka — ostatni pręt dowolnej długości (siatka 25)
                        foreach (double L in grid)
                            foreach (double o in overlaps)
                            {
                                double e = x + L, s = e - o, Ll = Math.Ceiling((available - s) / 25.0 - 1e-9) * 25.0;
                                s = available - Ll; double oo = e - s;
                                if (oo < oMin - 1e-6 || oo > oMax + 1e-6 || s <= x + 1e-6 || Ll < minLen - 1e-6 || Ll > maxLen + 1e-6) continue;
                                if (!Clear(s, e)) continue;
                                plan.Add((x, L)); plan.Add((s, Ll));
                                return true;
                            }
                        return false;
                    }
                    foreach (double L in grid)
                        foreach (double o in overlaps)
                        {
                            double e = x + L, s = e - o;
                            int rem = left - 1;
                            if (s + rem * maxLen - (rem - 1) * oMin < available - 1e-6) continue;   // reszta nie dosięgnie końca
                            if (s + rem * minLen - (rem - 1) * oMax > available + 1e-6) continue;   // reszta za długa
                            if (!Clear(s, e)) continue;
                            plan.Add((x, L));
                            if (Dfs(left - 1, s)) return true;
                            plan.RemoveAt(plan.Count - 1);
                            if (nodes > MaxNodes) return false;
                        }
                    return false;
                }
                if (Dfs(N, 0)) return plan;
                if (timedOut) { TimeOuts++; return null; }
            }
            return null;
        }

        /// <summary>
        /// „Najpierw góra” do końca (decyzja Bartka 2026-10-20, płyta z palami co ok. 2250 mm — przy dole ze wzoru „pierwszy, środkowe
        /// równe, ostatni” góra nie mieściła zakładów między palami i dostawała zakład 93 mm od lica): GÓRA swobodnie (pręty różnej
        /// długości z siatki, zakłady poza strefami pali, bez dołu), potem DÓŁ swobodnie dopasowany do góry (zakłady ≥ gap w świetle
        /// od zakładów góry, bez pali). <paramref name="topInsetLo"/> — przesunięcie układu góry względem dołu (nib): x_dołu = x_góry + inset.
        /// Najpierw najmniej prętów góry, potem dołu. Null — nie da się.
        /// </summary>
        public static (List<(double xOffset, double length)> bottom, List<(double xOffset, double length)> top)? TopFirst(
            double available, double topAvail, double topInsetLo, IList<(double lo, double hi)> topForbidden,
            int nBMin, int nTMin, IList<double> minLens, double maxLen, double gridStep,
            double bMin, double bMax, double bTarget, double tMin, double tMax, double tTarget, double staggerGap)
        {
            var none = new List<(double lo, double hi)>();
            for (int nt = nTMin; nt <= nTMin + 4; nt++)
                foreach (double minT in minLens)
                {
                    var top = Find(topAvail, none, topForbidden, nt, nt, minT, maxLen, gridStep, tMin, tMax, tTarget, 0);
                    if (top == null) continue;
                    var tl = new List<(double lo, double hi)>();
                    for (int i = 0; i + 1 < top.Count; i++)
                        tl.Add((top[i + 1].xOffset + topInsetLo, top[i].xOffset + top[i].length + topInsetLo));
                    foreach (double minB in minLens)
                    {
                        var bottom = Find(available, tl, none, nBMin, nBMin + 5, minB, maxLen, gridStep, bMin, bMax, bTarget, staggerGap);
                        if (bottom != null) return (bottom, top);
                    }
                }
            return null;
        }
    }
}
