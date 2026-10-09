#nullable disable   // kompilowany też w tests/ (Nullable enable) — bez ostrzeżeń CS86xx
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Osiowość obrysu płyty (POPRAWKI_WZORCA 25, decyzja użytkownika 2026-10-09) — czysta logika (bez Teigha),
    /// testy `tests/OutlineAxisRulesTests.cs`.
    ///
    /// Krawędź, której końce różnią się w X albo w Y o ≤ <see cref="AxisTolerance"/> (4 mm), jest osiowa
    /// (wcześniej 1e-3 mm — szum 0,0115 mm w GA plotu 523 dawał „płyta pod kątem” i brak siatki). Obliczenia
    /// zbrojenia działają na KOPII obrysu „wyprostowanej”: wierzchołki połączone krawędziami poziomymi mają wspólne Y,
    /// pionowymi — wspólne X (wartość dominująca w grupie, a gdy jej brak — średnia), więc pręty nie dziedziczą
    /// odchyłki. Obrys w rysunku bez zmian.
    ///
    /// Oprócz odchyłki krawędzi sprawdzany jest rozrzut (max − min) wspólnej współrzędnej w każdej grupie wierzchołków
    /// (łańcuch krawędzi po ≤ 4 mm nie może sumować odchyłki) — rozrzut &gt; tolerancji = obrys pod kątem.
    /// Obrys idealnie osiowy (każda krawędź ≤ <see cref="ExactTolerance"/>, dawna tolerancja) — <see cref="Result.Changed"/>
    /// = false i wywołujący używa ORYGINALNYCH wierzchołków: wynik bit w bit jak przed poprawką (S-REG).
    /// </summary>
    public static class OutlineAxisRules
    {
        /// <summary>Tolerancja osiowości krawędzi obrysu [mm] (decyzja użytkownika 2026-10-09).</summary>
        public const double AxisTolerance = 4.0;

        /// <summary>Dawna tolerancja (GeometryHelper.IsAxisAlignedPolyline) — obrys „idealnie osiowy”.</summary>
        public const double ExactTolerance = 1e-3;

        public sealed class Result
        {
            /// <summary>Każda krawędź odchylona od osi o ≤ tolerancję.</summary>
            public bool AxisAligned;
            /// <summary>Wierzchołki wyprostowane różnią się od wejścia (był szum &gt; <see cref="ExactTolerance"/>).</summary>
            public bool Changed;
            /// <summary>Wierzchołki do obliczeń: wyprostowane albo kopia wejścia (gdy !Changed albo !AxisAligned).</summary>
            public (double X, double Y)[] Points;
            /// <summary>Największa odchyłka krawędzi od osi: max po krawędziach z min(|dx|, |dy|) [mm].</summary>
            public double MaxDeviation;
            /// <summary>Indeks wierzchołka początkowego krawędzi z <see cref="MaxDeviation"/> (-1 = brak krawędzi).</summary>
            public int WorstEdge = -1;
        }

        /// <summary>
        /// Odchyłka krawędzi od osi: min(|dx|, |dy|) — 0 dla krawędzi idealnie poziomej / pionowej.
        /// </summary>
        public static double EdgeDeviation((double X, double Y) a, (double X, double Y) b)
            => Math.Min(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y));

        /// <summary>
        /// Prostowanie zamkniętego obrysu (krawędzie i → i+1 oraz ostatni → pierwszy; powtórzony wierzchołek
        /// zamykający daje krawędź zerową — dopuszczalną).
        /// </summary>
        public static Result Square(IReadOnlyList<(double X, double Y)> pts, double tol = AxisTolerance)
        {
            var res = new Result { Points = pts == null ? new (double, double)[0] : pts.ToArray() };
            int n = res.Points.Length;
            if (n < 2) { res.AxisAligned = false; return res; }

            var p = res.Points;
            for (int i = 0; i < n; i++)
            {
                double dev = EdgeDeviation(p[i], p[(i + 1) % n]);
                if (dev > res.MaxDeviation || res.WorstEdge < 0) { res.MaxDeviation = Math.Max(res.MaxDeviation, dev); res.WorstEdge = i; }
            }
            res.AxisAligned = res.MaxDeviation <= tol;
            if (!res.AxisAligned || res.MaxDeviation <= ExactTolerance) return res;   // pod kątem albo idealny — bez zmian

            // Grupy wierzchołków o wspólnym X (krawędzie pionowe) i wspólnym Y (krawędzie poziome)
            var gx = new int[n]; var gy = new int[n];
            for (int i = 0; i < n; i++) { gx[i] = i; gy[i] = i; }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                double dx = Math.Abs(p[j].X - p[i].X), dy = Math.Abs(p[j].Y - p[i].Y);
                bool zero = dx <= ExactTolerance && dy <= ExactTolerance;        // np. powtórzony wierzchołek zamykający
                if (zero || dx <= dy) Union(gx, i, j);                           // pionowa: wspólne X
                if (zero || dy < dx) Union(gy, i, j);                            // pozioma: wspólne Y
            }

            // Rozrzut grupy (max − min) ≤ tol — inaczej łańcuch krawędzi sumuje odchyłkę („schodki” po ≤ 4 mm
            // dają bok uciekający o dziesiątki mm), a taki obrys jest pod kątem (recenzja POPRAWKI 25).
            double spread = 0; int spreadEdge = -1;
            GroupSpread(gx, p.Select(q => q.X).ToArray(), ref spread, ref spreadEdge);
            GroupSpread(gy, p.Select(q => q.Y).ToArray(), ref spread, ref spreadEdge);
            if (spread > tol)
            {
                res.AxisAligned = false;
                res.MaxDeviation = spread;
                if (spreadEdge >= 0) res.WorstEdge = spreadEdge;
                return res;
            }

            var outPts = new (double X, double Y)[n];
            var xs = GroupValues(gx, p.Select(q => q.X).ToArray());
            var ys = GroupValues(gy, p.Select(q => q.Y).ToArray());
            for (int i = 0; i < n; i++) outPts[i] = (xs[i], ys[i]);

            for (int i = 0; i < n && !res.Changed; i++)
                res.Changed = outPts[i].X != p[i].X || outPts[i].Y != p[i].Y;
            if (res.Changed) res.Points = outPts;
            return res;
        }

        /// <summary>Komunikat „płyta pod kątem” z odchyłką krawędzi (InvariantCulture).</summary>
        public static string NotAxisAlignedMessage(string prefix, Result r)
        {
            string edge = "";
            if (r != null && r.WorstEdge >= 0 && r.Points != null && r.Points.Length > 1)
            {
                var a = r.Points[r.WorstEdge]; var b = r.Points[(r.WorstEdge + 1) % r.Points.Length];
                edge = $" — krawędź ({F(a.X)}, {F(a.Y)}) → ({F(b.X)}, {F(b.Y)})";
            }
            double dev = r?.MaxDeviation ?? 0;
            return $"{prefix} Etap 1 nie obsługuje płyt pod kątem (NIE drawn){edge}: odchyłka od osi " +
                   dev.ToString("0.###", CultureInfo.InvariantCulture) + " mm > " +
                   AxisTolerance.ToString("0.###", CultureInfo.InvariantCulture) + " mm.";
        }

        /// <summary>Komunikat o wyprostowaniu obrysu w obliczeniach (InvariantCulture).</summary>
        public static string SquaredMessage(string prefix, Result r)
            => $"{prefix} Obrys płyty prawie osiowy (odchyłka krawędzi do " +
               (r?.MaxDeviation ?? 0).ToString("0.####", CultureInfo.InvariantCulture) + " mm ≤ " +
               AxisTolerance.ToString("0.###", CultureInfo.InvariantCulture) +
               " mm) — zbrojenie liczone na obrysie wyprostowanym; obrys w rysunku bez zmian.";

        private static string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static int Find(int[] g, int i)
        {
            while (g[i] != i) { g[i] = g[g[i]]; i = g[i]; }
            return i;
        }

        private static void Union(int[] g, int a, int b)
        {
            int ra = Find(g, a), rb = Find(g, b);
            if (ra != rb) g[Math.Max(ra, rb)] = Math.Min(ra, rb);
        }

        /// <summary>
        /// Największy rozrzut (max − min) wartości w grupie; edge = krawędź tej grupy (wierzchołek i z następnym w tej samej
        /// grupie) — do komunikatu „pod kątem”.
        /// </summary>
        private static void GroupSpread(int[] g, double[] v, ref double spread, ref int edge)
        {
            int n = v.Length;
            var lo = new Dictionary<int, double>(); var hi = new Dictionary<int, double>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(g, i);
                lo[r] = lo.TryGetValue(r, out var a) ? Math.Min(a, v[i]) : v[i];
                hi[r] = hi.TryGetValue(r, out var b) ? Math.Max(b, v[i]) : v[i];
            }
            foreach (var r in lo.Keys)
            {
                double s = hi[r] - lo[r];
                if (s <= spread) continue;
                spread = s; edge = -1;
                for (int i = 0; i < n && edge < 0; i++)
                    if (Find(g, i) == r && Find(g, (i + 1) % n) == r) edge = i;
            }
        }

        /// <summary>
        /// Wartość grupy: wszystkie równe → bez zmian (bit w bit); jedna wartość występująca częściej niż każda inna →
        /// ta wartość (dominująca); inaczej średnia arytmetyczna.
        /// </summary>
        private static double[] GroupValues(int[] g, double[] v)
        {
            int n = v.Length;
            var res = new double[n];
            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int r = Find(g, i);
                if (!groups.TryGetValue(r, out var list)) groups[r] = list = new List<int>();
                list.Add(i);
            }
            foreach (var members in groups.Values)
            {
                double value;
                var vals = members.Select(i => v[i]).ToList();
                if (vals.All(x => x == vals[0])) value = vals[0];
                else
                {
                    var counts = vals.GroupBy(x => x).Select(gr => (val: gr.Key, cnt: gr.Count()))
                                     .OrderByDescending(t => t.cnt).ToList();
                    if (counts[0].cnt > 1 && counts[0].cnt > counts[1].cnt) value = counts[0].val;
                    else value = vals.Sum() / vals.Count;
                }
                foreach (int i in members) res[i] = value;
            }
            return res;
        }
    }
}
