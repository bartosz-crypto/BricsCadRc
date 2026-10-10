#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Nib — liczba prętów góry i UB 01/02 na krawędzi uskoku (czysta logika, bez Teigha; testy <c>NibStepRulesTests</c>).
    /// Reguła (ARCHITECTURE §4 „Nib”): UB 01/02 na uskoku = UB 03 krawędzi zewnętrznej ± 1 na każdy koniec — narożnik wypukły
    /// (uskok krótszy) −1, wklęsły (uskok dłuższy) +1, ta sama długość → ta sama liczba.
    /// <para>
    /// <b>Wolny koniec (POPRAWKI_WZORCA 29, krok K1 planu PLAN_BELKI_PRAKTYKA):</b> krawędź uskoku nibu zawiniętego (np. 600 mm
    /// zawinięcia na sąsiedniej krawędzi, PLOT 321 / 525) albo nibu w środku krawędzi kończy się WEWNĄTRZ zakresu krawędzi
    /// zewnętrznej, a sąsiednia krawędź linii uskoku w tym wierzchołku nie jest krawędzią nibu. Wzorzec liczył wtedy pręty dołu
    /// na CAŁEJ krawędzi zewnętrznej (321: 33 UB na 643 mm, rozstaw 17,6 mm). Teraz zakres liczenia przycięty do [lo, hi]
    /// na wolnym końcu i bez ± 1 na tym końcu; koniec niewolny — jak dotąd (bit w bit).
    /// </para>
    /// </summary>
    public static class NibStepRules
    {
        /// <summary>Rozkład o rozstawie mniejszym niż ten próg — ostrzeżenie (bezpiecznik po błędzie wzorca 29).</summary>
        public const double MinSaneSpacing = 100.0;

        /// <summary>Krawędź uskoku z liczbą prętów wg reguły (oś rozkładu: lo..hi, c — współrzędna krawędzi).</summary>
        public sealed class StepEdge
        {
            public double C, Lo, Hi;
            public int Rule;
            public StepEdge(double c, double lo, double hi, int rule) { C = c; Lo = lo; Hi = hi; Rule = rule; }
        }

        /// <summary>Pas prętów góry (jak <c>StripBounds</c>: Scan = oś rozkładu, Perp = wzdłuż prętów).</summary>
        public sealed class Strip
        {
            public double ScanLow, ScanHigh, PerpLow, PerpHigh;
            public Strip(double scanLow, double scanHigh, double perpLow, double perpHigh)
            { ScanLow = scanLow; ScanHigh = scanHigh; PerpLow = perpLow; PerpHigh = perpHigh; }
        }

        /// <summary>
        /// Koniec krawędzi uskoku jest „wolny”: leży wewnątrz zakresu krawędzi zewnętrznej (więcej niż 1 mm od jej końców),
        /// a sąsiednia krawędź linii uskoku w tym wierzchołku nie jest krawędzią nibu (uskok wraca do obrysu — zawinięcie
        /// nibu albo nib w środku krawędzi). Narożnik nibu (sąsiednia krawędź też jest nibem) — koniec niewolny (reguła ± 1).
        /// </summary>
        public static bool IsFreeEnd(double end, double outerLo, double outerHi, bool adjacentIsNibEdge)
            => !adjacentIsNibEdge && end > outerLo + 1.0 && end < outerHi - 1.0;

        /// <summary>Krawędź nibu (pola <c>NibDetector.NibEdge</c> bez Teigha): pionowa, współrzędna obrysu / linii uskoku, zakres.</summary>
        public sealed class NibEdgeRef
        {
            public bool Vertical;
            public double OuterCoord, InnerCoord, Lo, Hi;
            public NibEdgeRef(bool vertical, double outerCoord, double innerCoord, double lo, double hi)
            { Vertical = vertical; OuterCoord = outerCoord; InnerCoord = innerCoord; Lo = lo; Hi = hi; }
        }

        /// <summary>Co jest w wierzchołku końca krawędzi uskoku.</summary>
        public enum EndKind
        {
            /// <summary>Brak sąsiedniej krawędzi nibu — linia uskoku wraca do obrysu (zawinięcie, nib w środku krawędzi).</summary>
            Open,
            /// <summary>Narożnik nibu: prostopadła krawędź nibu w tym wierzchołku.</summary>
            Corner,
            /// <summary>Wierzchołek współliniowy: dalej krawędź nibu tej samej orientacji i tego samego InnerCoord.</summary>
            Collinear,
            /// <summary>Odsadzka linii uskoku: dalej krawędź nibu tej samej orientacji i krawędzi zewnętrznej, inny InnerCoord
            /// (zmiana szerokości nibu wzdłuż krawędzi).</summary>
            Offset
        }

        /// <summary>
        /// Rodzaj końca <paramref name="end"/> krawędzi uskoku (orientacja <paramref name="stepVertical"/>, współrzędna c,
        /// krawędź zewnętrzna <paramref name="outerCoord"/>) na liście krawędzi nibu. <paramref name="lowEnd"/> — koniec dolny
        /// (kontynuacja leży poniżej), inaczej górny. NibDetector nie scala odcinków współliniowych — wierzchołek na prostej
        /// linii uskoku daje dwie krawędzie nibu stykające się w tym punkcie.
        /// </summary>
        public static EndKind ClassifyEnd(IEnumerable<NibEdgeRef> edges, bool stepVertical, double c, double outerCoord,
            double end, bool lowEnd)
        {
            var list = edges as IList<NibEdgeRef> ?? edges.ToList();
            if (list.Any(nz => nz.Vertical != stepVertical && Math.Abs(nz.InnerCoord - end) < 1.0
                               && c >= nz.Lo - 1.0 && c <= nz.Hi + 1.0))
                return EndKind.Corner;
            bool Continues(NibEdgeRef nz) => nz.Vertical == stepVertical
                && (lowEnd ? Math.Abs(nz.Hi - end) < 1.0 && nz.Lo < end - 1.0
                           : Math.Abs(nz.Lo - end) < 1.0 && nz.Hi > end + 1.0);
            if (list.Any(nz => Continues(nz) && Math.Abs(nz.InnerCoord - c) < 1.0)) return EndKind.Collinear;
            if (list.Any(nz => Continues(nz) && Math.Abs(nz.OuterCoord - outerCoord) < 1.0)) return EndKind.Offset;
            return EndKind.Open;
        }

        /// <summary>
        /// Liczba prętów / UB na krawędzi uskoku [lo, hi] z rozpoznaniem końców na liście krawędzi nibu
        /// (<see cref="ClassifyEnd"/>):
        /// <list type="bullet">
        /// <item>narożnik nibu, wierzchołek współliniowy, koniec poza wnętrzem krawędzi zewnętrznej — niewolny (reguła ± 1, jak wzorzec);</item>
        /// <item>koniec otwarty wewnątrz krawędzi zewnętrznej — wolny (POPRAWKI_WZORCA 29);</item>
        /// <item>odsadzka linii uskoku wewnątrz krawędzi zewnętrznej — jak wzorzec (niewolny), chyba że wzorzec daje rozstaw
        /// &lt; <see cref="MinSaneSpacing"/> na tej krawędzi — wtedy wolny (nowa reguła, POPRAWKI_WZORCA 29, pytanie do użytkownika).</item>
        /// </list>
        /// </summary>
        public static int RuleCountOnEdges(IEnumerable<NibEdgeRef> edges, bool stepVertical, double c, double outerCoord,
            double lo, double hi, double outerLo, double outerHi, Func<double, double, int> bottomCount,
            double cover, double spacing, out int nOuter, out bool freeLow, out bool freeHigh)
        {
            var list = edges as IList<NibEdgeRef> ?? edges.ToList();
            var kLo = ClassifyEnd(list, stepVertical, c, outerCoord, lo, true);
            var kHi = ClassifyEnd(list, stepVertical, c, outerCoord, hi, false);
            freeLow  = IsFreeEnd(lo, outerLo, outerHi, kLo != EndKind.Open);
            freeHigh = IsFreeEnd(hi, outerLo, outerHi, kHi != EndKind.Open);
            int n = RuleCount(lo, hi, outerLo, outerHi, freeLow, freeHigh, bottomCount, cover, spacing, out nOuter);

            bool offLo = kLo == EndKind.Offset && IsFreeEnd(lo, outerLo, outerHi, false);
            bool offHi = kHi == EndKind.Offset && IsFreeEnd(hi, outerLo, outerHi, false);
            if ((offLo || offHi) && MinGap(EvenPositions(lo, hi, n, cover)) < MinSaneSpacing)
            {
                freeLow  |= offLo;
                freeHigh |= offHi;
                n = RuleCount(lo, hi, outerLo, outerHi, freeLow, freeHigh, bottomCount, cover, spacing, out nOuter);
            }
            return n;
        }

        /// <summary>
        /// Liczba prętów / UB na krawędzi uskoku [lo, hi] (krawędź zewnętrzna [outerLo, outerHi]).
        /// <paramref name="bottomCount"/>(od, do) — liczba prętów dołu w zakresie osi rozkładu (null / 0 = brak rzutu dolnego:
        /// floor((zakres − 2·otulina) / rozstaw) + 1). Zakres liczenia: na końcu wolnym — koniec krawędzi uskoku, na niewolnym —
        /// koniec krawędzi zewnętrznej (jak wzorzec). ± 1 tylko na końcach niewolnych.
        /// </summary>
        public static int RuleCount(double lo, double hi, double outerLo, double outerHi, bool freeLow, bool freeHigh,
            Func<double, double, int> bottomCount, double cover, double spacing, out int nOuter)
        {
            double cLo = freeLow ? lo : outerLo, cHi = freeHigh ? hi : outerHi;
            nOuter = bottomCount != null ? bottomCount(cLo + cover - 0.5, cHi - cover + 0.5) : 0;
            if (nOuter == 0) nOuter = (int)Math.Floor((cHi - cLo - 2 * cover) / spacing + 1e-9) + 1;
            int dLow  = freeLow  ? 0 : lo < outerLo - 1.0 ? 1 : lo > outerLo + 1.0 ? -1 : 0;
            int dHigh = freeHigh ? 0 : hi > outerHi + 1.0 ? 1 : hi < outerHi - 1.0 ? -1 : 0;
            return Math.Max(1, nOuter + dLow + dHigh);
        }

        /// <summary>n położeń równo od otuliny do otuliny na [lo, hi]; n ≤ 1 albo brak miejsca — jedno w środku.</summary>
        public static List<double> EvenPositions(double lo, double hi, int n, double cover)
        {
            double a = lo + cover, b = hi - cover;
            var res = new List<double>();
            if (n <= 1 || b < a) { res.Add((lo + hi) / 2.0); return res; }
            for (int i = 0; i < n; i++) res.Add(a + (b - a) * i / (n - 1));
            return res;
        }

        /// <summary>
        /// Krawędź-właściciel pasa: krawędź uskoku na końcu prętów pasa (PerpLow / PerpHigh), zachodząca na pas, o zakresie
        /// najlepiej pasującym do pasa (pierwsza z równych — kolejność listy). null — pas nie dochodzi do krawędzi uskoku.
        /// </summary>
        public static StepEdge OwnerOf(IList<StepEdge> edges, Strip s)
            => edges.Where(e => (Math.Abs(e.C - s.PerpLow) < 1.0 || Math.Abs(e.C - s.PerpHigh) < 1.0)
                             && Math.Min(e.Hi, s.ScanHigh) - Math.Max(e.Lo, s.ScanLow) > 1.0)
                    .OrderBy(e => Math.Abs(e.Lo - s.ScanLow) + Math.Abs(e.Hi - s.ScanHigh))
                    .FirstOrDefault();

        /// <summary>Położenia prętów góry pasa: położenia krawędzi-właściciela w zakresie pasa (brak — jeden pręt w środku pasa).</summary>
        public static List<double> StripPositions(StepEdge owner, Strip s, double cover)
        {
            var pos = EvenPositions(owner.Lo, owner.Hi, owner.Rule, cover)
                .Where(p => p >= s.ScanLow - 0.5 && p < s.ScanHigh - 0.5).ToList();
            if (pos.Count == 0) pos.Add((s.ScanLow + s.ScanHigh) / 2.0);
            return pos;
        }

        /// <summary>
        /// Położenia UB na krawędzi uskoku c [segLow, segHigh] = pręty góry wszystkich pasów dochodzących do niej (każdy pręt
        /// górny ma swój UB — także, gdy właścicielem pasa jest krawędź na drugim końcu prętów). null — któryś dochodzący pas
        /// nie ma planu albo żaden nie dochodzi.
        /// </summary>
        public static List<double> UbPositions(IEnumerable<(Strip strip, List<double> pos)> strips, double c, double segLow, double segHigh)
        {
            var res = new List<double>();
            bool any = false;
            foreach (var (s, pos) in strips)
            {
                bool touches = Math.Abs(s.PerpLow - c) < 1.0 || Math.Abs(s.PerpHigh - c) < 1.0;
                if (!touches || Math.Min(s.ScanHigh, segHigh) - Math.Max(s.ScanLow, segLow) < 1.0) continue;
                if (pos == null) return null;
                any = true;
                foreach (double p in pos)
                    if (p >= segLow - 0.5 && p <= segHigh + 0.5 && !res.Any(q => Math.Abs(q - p) < 1.0)) res.Add(p);
            }
            if (!any) return null;
            res.Sort();
            return res;
        }

        /// <summary>Najmniejszy odstęp kolejnych położeń (posortowanych); &lt; 2 położenia — +∞.</summary>
        public static double MinGap(IList<double> sortedPositions)
        {
            double m = double.PositiveInfinity;
            for (int i = 1; i < sortedPositions.Count; i++) m = Math.Min(m, sortedPositions[i] - sortedPositions[i - 1]);
            return m;
        }
    }
}
