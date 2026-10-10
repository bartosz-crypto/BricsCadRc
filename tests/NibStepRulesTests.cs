using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// NibStepRules — liczba prętów góry / UB 01/02 na krawędzi uskoku (ARCHITECTURE §4 „Nib”) i wolny koniec nibu
    /// zawiniętego (POPRAWKI_WZORCA 29, krok K1). Bez rzutu dolnego: pręty dołu = floor((zakres − 2·40) / 200) + 1.
    /// </summary>
    [TestFixture]
    public class NibStepRulesTests
    {
        private const double C = 40.0, S = 200.0, Nib = 135.0;

        private static int Rule(double lo, double hi, double oLo, double oHi, bool fLo = false, bool fHi = false)
            => NibStepRules.RuleCount(lo, hi, oLo, oHi, fLo, fHi, null!, C, S, out _);

        // ── przykłady z ARCHITECTURE §4 — bez zmian (końce niewolne) ─────────

        [Test]
        public void Architecture_Convex57To55()
        {
            double len = 2 * C + 56 * S;                       // 57 prętów dołu
            Assert.That(Rule(Nib, len - Nib, 0, len), Is.EqualTo(55));
        }

        [Test]
        public void Architecture_Indent8To10()
        {
            double len = 2 * C + 7 * S;                        // 8 prętów dołu
            Assert.That(Rule(-Nib, len + Nib, 0, len), Is.EqualTo(10));
        }

        [Test]
        public void Architecture_ConvexAndConcave13To13()
        {
            double len = 2 * C + 12 * S;                       // 13
            Assert.That(Rule(Nib, len + Nib, 0, len), Is.EqualTo(13));
        }

        [Test]
        public void SameLength_SameCount()
        {
            double len = 2 * C + 20 * S;
            Assert.That(Rule(0, len, 0, len), Is.EqualTo(21));
        }

        // ── wolny koniec ─────────────────────────────────────────────────

        [Test]
        public void FreeEnd_Criterion()
        {
            // zawinięcie: koniec w środku krawędzi zewnętrznej, sąsiednia krawędź uskoku nie jest nibem → wolny
            Assert.That(NibStepRules.IsFreeEnd(11400, 0, 12000, adjacentIsNibEdge: false), Is.True);
            // narożnik nibu (sąsiednia krawędź też nibem) → niewolny, mimo końca w środku
            Assert.That(NibStepRules.IsFreeEnd(11865, 0, 12000, adjacentIsNibEdge: true), Is.False);
            // koniec na końcu krawędzi zewnętrznej (nib tylko na tej krawędzi) → niewolny (reguła ± 0, jak wzorzec)
            Assert.That(NibStepRules.IsFreeEnd(0, 0, 12000, adjacentIsNibEdge: false), Is.False);
            // koniec poza krawędzią zewnętrzną (wcięcie) → niewolny
            Assert.That(NibStepRules.IsFreeEnd(-135, 0, 12000, adjacentIsNibEdge: false), Is.False);
        }

        [Test]
        public void Wrapped321_ShortStepEdge_SpacingNear200()
        {
            // PLOT 321: krawędź uskoku 643 mm na zawinięciu nibu, krawędź zewnętrzna ~12,5 m.
            // Wzorzec: pręty dołu z CAŁEJ krawędzi zewnętrznej → 33 UB, rozstaw 17,6 mm.
            double oLo = 0, oHi = 12518, lo = 11740, hi = oHi - Nib;   // dolny koniec wolny, górny — narożnik nibu (wypukły)
            int old = Rule(lo, hi, oLo, oHi);
            int now = Rule(lo, hi, oLo, oHi, fLo: true);
            var pos = NibStepRules.EvenPositions(lo, hi, now, C);
            double gap = NibStepRules.MinGap(pos);
            Assert.That(old, Is.GreaterThan(50), "wzorzec — cała krawędź zewnętrzna");
            Assert.That(now, Is.InRange(2, 4));
            Assert.That(gap, Is.InRange(150.0, 400.0), "rozstaw rzędu 200, nie 17,6");
        }

        [Test]
        public void Wrapped_WithBottomView_CountsOnlyStepRange()
        {
            // pręty dołu co 200 od otuliny przy narożniku: 12478, 12278, …
            double oLo = 0, oHi = 12518, lo = 11740, hi = oHi - Nib;
            var bottom = Enumerable.Range(0, 63).Select(i => oHi - C - i * S).Where(p => p >= oLo + C).ToList();
            int Count(double a, double b) => bottom.Count(p => p >= a && p <= b);
            int now = NibStepRules.RuleCount(lo, hi, oLo, oHi, true, false, Count, C, S, out int nOuter);
            // zakres liczenia [lo, oHi]: pręty dołu 12478, 12278, 12078, 11878 → 4, narożnik wypukły −1 → 3
            Assert.That(nOuter, Is.EqualTo(4));
            Assert.That(now, Is.EqualTo(3));
        }

        [Test]
        public void NibInMiddleOfEdge_BothEndsFree()
        {
            // nib w środku krawędzi 10 m (3000..6000) — oba końce wolne: pręty tylko na długości uskoku, bez ± 1
            Assert.That(Rule(3000, 6000, 0, 10000, fLo: true, fHi: true), Is.EqualTo(15));
            Assert.That(Rule(3000, 6000, 0, 10000), Is.EqualTo(48), "wzorzec: cała krawędź 50 − 1 − 1");
            double gap = NibStepRules.MinGap(NibStepRules.EvenPositions(3000, 6000, 15, C));
            Assert.That(gap, Is.InRange(195.0, 215.0));
        }

        // ── kryterium końców na liście krawędzi nibu (RuleCountOnEdges) ──

        private static NibStepRules.NibEdgeRef V(double outer, double inner, double lo, double hi)
            => new NibStepRules.NibEdgeRef(true, outer, inner, lo, hi);
        private static NibStepRules.NibEdgeRef H(double outer, double inner, double lo, double hi)
            => new NibStepRules.NibEdgeRef(false, outer, inner, lo, hi);

        private static int OnEdges(List<NibStepRules.NibEdgeRef> edges, double c, double outerC, double lo, double hi,
            double oLo, double oHi, out bool fLo, out bool fHi)
            => NibStepRules.RuleCountOnEdges(edges, true, c, outerC, lo, hi, oLo, oHi, null!, C, S, out _, out fLo, out fHi);

        [Test]
        public void CollinearVertex_NotFree_AsPattern()
        {
            // nib 135 wzdłuż lewej krawędzi (x = 0, 0..len) z narożnikami nibu na dole i górze; linia uskoku ma wierzchołek
            // współliniowy w y = 5000 (NibDetector nie scala odcinków) → dwie krawędzie nibu stykające się w 5000
            double len = 2 * C + 56 * S, p = 5000;
            var split = new List<NibStepRules.NibEdgeRef>
            {
                V(0, Nib, 0, p), V(0, Nib, p, len), H(0, Nib, 0, 8000), H(len, len - Nib, 0, 8000)
            };
            Assert.That(NibStepRules.ClassifyEnd(split, true, Nib, 0, p, lowEnd: false), Is.EqualTo(NibStepRules.EndKind.Collinear));
            Assert.That(NibStepRules.ClassifyEnd(split, true, Nib, 0, p, lowEnd: true), Is.EqualTo(NibStepRules.EndKind.Collinear));
            Assert.That(NibStepRules.ClassifyEnd(split, true, Nib, 0, Nib, lowEnd: true), Is.EqualTo(NibStepRules.EndKind.Corner));

            int a = OnEdges(split, Nib, 0, Nib, p, 0, len, out bool aLo, out bool aHi);
            int b = OnEdges(split, Nib, 0, p, len - Nib, 0, len, out bool bLo, out bool bHi);
            Assert.That(aLo || aHi || bLo || bHi, Is.False, "wierzchołek współliniowy nie jest wolnym końcem");
            // wynik jak wzorzec (każda połówka reguła ± 1 z całej krawędzi zewnętrznej), nie przycięty do połówki
            Assert.That(a, Is.EqualTo(Rule(Nib, p, 0, len)));
            Assert.That(b, Is.EqualTo(Rule(p, len - Nib, 0, len)));

            // bez wierzchołka: ta sama krawędź jako jedna — przykład §4 (57 → 55), końce niewolne
            var whole = new List<NibStepRules.NibEdgeRef> { V(0, Nib, 0, len), H(0, Nib, 0, 8000), H(len, len - Nib, 0, 8000) };
            Assert.That(OnEdges(whole, Nib, 0, Nib, len - Nib, 0, len, out bool wLo, out bool wHi), Is.EqualTo(55));
            Assert.That(wLo || wHi, Is.False);
        }

        [Test]
        public void CollinearVertex_OnWrappedEdge_FreeEndStays()
        {
            // 321: krawędź uskoku zawinięcia 11740..12383 z wierzchołkiem współliniowym w 12000 — koniec 11740 nadal wolny
            // (linia uskoku wraca do obrysu), 12000 — niewolny (kontynuacja), 12383 — narożnik nibu
            double oHi = 12518, lo = 11740, p = 12000, hi = oHi - Nib;
            var edges = new List<NibStepRules.NibEdgeRef> { V(0, Nib, lo, p), V(0, Nib, p, oHi), H(oHi, hi, 0, 8000) };
            OnEdges(edges, Nib, 0, lo, p, 0, oHi, out bool aLo, out bool aHi);
            OnEdges(edges, Nib, 0, p, hi, 0, oHi, out bool bLo, out bool bHi);
            Assert.That(aLo, Is.True);
            Assert.That(aHi, Is.False);
            Assert.That(bLo, Is.False);
            Assert.That(bHi, Is.False);
            // bez wierzchołka — ten sam wynik wolnych końców i liczba = K1
            var whole = new List<NibStepRules.NibEdgeRef> { V(0, Nib, lo, oHi), H(oHi, hi, 0, 8000) };
            int n = OnEdges(whole, Nib, 0, lo, hi, 0, oHi, out bool wLo, out bool wHi);
            Assert.That(wLo, Is.True);
            Assert.That(wHi, Is.False);
            Assert.That(n, Is.EqualTo(Rule(lo, hi, 0, oHi, fLo: true)));
        }

        [Test]
        public void OpenEnd_NoNeighbour_Free()
        {
            // nib w środku krawędzi (3000..6000) — oba końce otwarte (linia uskoku wraca do obrysu)
            var edges = new List<NibStepRules.NibEdgeRef> { V(0, Nib, 3000, 6000) };
            Assert.That(NibStepRules.ClassifyEnd(edges, true, Nib, 0, 3000, true), Is.EqualTo(NibStepRules.EndKind.Open));
            Assert.That(OnEdges(edges, Nib, 0, 3000, 6000, 0, 10000, out bool fLo, out bool fHi), Is.EqualTo(15));
            Assert.That(fLo && fHi, Is.True);
        }

        [Test]
        public void OffsetStepLine_PatternSpacingOk_AsPattern()
        {
            // odsadzka linii uskoku w y = 5000: nib 135 (dół) → 200 (góra) wzdłuż tej samej krawędzi zewnętrznej x = 0.
            // Wzorzec: każda połówka z całej krawędzi zewnętrznej, −1 na odsadzce → 48 prętów na 4865 mm, rozstaw ~102 ≥ 100 → jak wzorzec
            double len = 10000, p = 5000;
            var edges = new List<NibStepRules.NibEdgeRef>
            {
                V(0, Nib, 0, p), V(0, 200, p, len), H(0, Nib, 0, 8000), H(len, len - 200, 0, 8000)
            };
            Assert.That(NibStepRules.ClassifyEnd(edges, true, Nib, 0, p, lowEnd: false), Is.EqualTo(NibStepRules.EndKind.Offset));
            int a = OnEdges(edges, Nib, 0, Nib, p, 0, len, out bool aLo, out bool aHi);
            Assert.That(aLo || aHi, Is.False);
            Assert.That(a, Is.EqualTo(Rule(Nib, p, 0, len)));
            Assert.That(a, Is.EqualTo(48));
            Assert.That(NibStepRules.MinGap(NibStepRules.EvenPositions(Nib, p, a, C)), Is.GreaterThanOrEqualTo(NibStepRules.MinSaneSpacing));
        }

        [Test]
        public void OffsetStepLine_PatternSpacingBelow100_FreeEnd()
        {
            // odsadzka w y = 3000: wzorzec dałby 48 prętów na 2865 mm (rozstaw ~59 < 100) → koniec na odsadzce wolny (nowa reguła):
            // zakres liczenia [0, 3000] → 15 prętów dołu, narożnik nibu −1 → 14 (rozstaw ~214)
            double len = 10000, p = 3000;
            var edges = new List<NibStepRules.NibEdgeRef>
            {
                V(0, Nib, 0, p), V(0, 200, p, len), H(0, Nib, 0, 8000), H(len, len - 200, 0, 8000)
            };
            int wz = Rule(Nib, p, 0, len);
            Assert.That(NibStepRules.MinGap(NibStepRules.EvenPositions(Nib, p, wz, C)), Is.LessThan(NibStepRules.MinSaneSpacing));
            int a = OnEdges(edges, Nib, 0, Nib, p, 0, len, out bool aLo, out bool aHi);
            Assert.That(aLo, Is.False);
            Assert.That(aHi, Is.True);
            Assert.That(a, Is.EqualTo(14));
            // dłuższa połówka (6865 mm): wzorzec 48, rozstaw ~144 → jak wzorzec
            int b = OnEdges(edges, 200, 0, p, len - 200, 0, len, out bool bLo, out bool bHi);
            Assert.That(bLo || bHi, Is.False);
            Assert.That(b, Is.EqualTo(Rule(p, len - 200, 0, len)));
        }

        // ── plan pasów: każdy pręt górny ma swój UB ──────────────────────

        [Test]
        public void TwoOwnerEdgesOfOneStrip_UbCountEqualsTopBars()
        {
            // pas góry dochodzi do krawędzi uskoku na obu końcach prętów (perp 0 i 5000), krawędzie mają różne liczby z reguły;
            // pas bierze położenia z jednej (pierwszej z równo pasujących), UB na DRUGIEJ = pręty góry pasu, nie jej reguła
            var e1 = new NibStepRules.StepEdge(0, 0, 3000, 10);
            var e2 = new NibStepRules.StepEdge(5000, 0, 3000, 15);
            var edges = new List<NibStepRules.StepEdge> { e1, e2 };
            var strip = new NibStepRules.Strip(0, 3000, 0, 5000);
            var owner = NibStepRules.OwnerOf(edges, strip);
            Assert.That(owner, Is.SameAs(e1));
            var pos = NibStepRules.StripPositions(owner!, strip, C);
            Assert.That(pos.Count, Is.EqualTo(10));
            var ub1 = NibStepRules.UbPositions(new[] { (strip, pos) }, 0, 0, 3000);
            var ub2 = NibStepRules.UbPositions(new[] { (strip, pos) }, 5000, 0, 3000);
            Assert.That(ub1!.Count, Is.EqualTo(pos.Count));
            Assert.That(ub2!.Count, Is.EqualTo(pos.Count), "liczba UB = liczba prętów góry");
            Assert.That(ub2, Is.EqualTo(pos));
        }

        [Test]
        public void UbPositions_TwoStripsOnOneEdge_Sum()
        {
            var e = new NibStepRules.StepEdge(0, 0, 4000, 20);
            var s1 = new NibStepRules.Strip(0, 2000, 0, 3000);
            var s2 = new NibStepRules.Strip(2000, 4000, 0, 3000);
            var p1 = NibStepRules.StripPositions(e, s1, C);
            var p2 = NibStepRules.StripPositions(e, s2, C);
            var ub = NibStepRules.UbPositions(new[] { (s1, p1), (s2, p2) }, 0, 0, 4000);
            Assert.That(ub!.Count, Is.EqualTo(20));
            Assert.That(p1.Count + p2.Count, Is.EqualTo(20));
        }

        [Test]
        public void UbPositions_TouchingStripWithoutPlan_Null()
        {
            var s1 = new NibStepRules.Strip(0, 2000, 0, 3000);
            Assert.That(NibStepRules.UbPositions(new[] { (s1, (List<double>)null!) }, 0, 0, 2000), Is.Null);
        }
    }
}
