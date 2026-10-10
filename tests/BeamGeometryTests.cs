#nullable disable
using System.Collections.Generic;
using System.Linq;
using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Belki / pogrubienia (docs/PLAN_BELKI_2026-10-10.md, E1 / E3): geometria stref z GA Holbrook P02
    /// (SD-Thickening-Zone, obrys SD-PILED-RAFT — współrzędne bezwzględne z DXF), liczby strzemion, plan prętów H16,
    /// wymiary strzemienia 51, odczyt „450THK” i kontrola SSL − NIB TOC + 150.
    /// </summary>
    public class BeamGeometryTests
    {
        private static List<(double X, double Y)> P(params double[] xy)
        {
            var r = new List<(double X, double Y)>();
            for (int i = 0; i + 1 < xy.Length; i += 2) r.Add((xy[i], xy[i + 1]));
            return r;
        }

        // PLOT 528-529: prosta krawędź-krawędź (lewa krawędź płyty), nib 160 zawinięty 600 na końcach
        private static readonly List<(double X, double Y)> Zone528 = P(
            1326923.937, 59989.538, 1327523.937, 59989.538, 1327523.937, 49807.038, 1326923.937, 49807.038);
        private static readonly List<(double X, double Y)> Outline528 = P(
            1326763.937, 60149.538, 1337394.937, 60149.538, 1337394.937, 49647.038, 1326763.937, 49647.038);

        // PLOT 539: L + L (góra z końcem wewnętrznym, lewa, dół z końcem przy krawędzi)
        private static readonly List<(double X, double Y)> Zone539 = P(
            1550278.18, 58314.689, 1553336.18, 58314.689, 1553336.18, 57714.689, 1550878.18, 57714.689,
            1550878.18, 48732.189, 1555451.18, 48732.189, 1555451.18, 48131.785, 1550278.18, 48131.785);
        private static readonly List<(double X, double Y)> Outline539 = P(
            1550118.18, 58474.689, 1555611.18, 58474.689, 1555611.18, 47972.189, 1550118.18, 47972.189);

        // PLOT 319: U z odcinkami 45° w lewej nodze (zatoka obrysu), prawa noga z końcem wewnętrznym
        private static readonly List<(double X, double Y)> Zone319 = P(
            109780.206, 46465.429, 109180.206, 46465.429, 109180.206, 47935.214, 108590.272, 48525.148,
            108590.272, 49641.411, 109180.206, 50231.345, 109180.206, 51697.929, 117787.706, 51697.929,
            117787.706, 48805.429, 117187.706, 48805.429, 117187.706, 51097.929, 109780.206, 51097.929);
        private static readonly List<(double X, double Y)> Outline319 = P(
            109020.206, 51857.929, 117947.706, 51857.929, 117947.706, 46305.429, 114685.206, 46305.429,
            114685.206, 45405.429, 112057.706, 45405.429, 112057.706, 46305.429, 109020.206, 46305.429,
            109020.206, 47868.94, 108430.272, 48458.874, 108430.272, 49707.685, 109020.206, 50297.619);

        [Test]
        public void Plot528_StraightEdgeToEdge()
        {
            var r = BeamGeometry.Analyze(Zone528, Outline528);
            Assert.That(r.Segments.Count, Is.EqualTo(1), string.Join(" | ", r.Segments));
            var s = r.Segments[0];
            Assert.That(s.AlongX, Is.False);
            Assert.That(s.Manual, Is.False);
            Assert.That(s.Width, Is.EqualTo(600).Within(1e-6));
            Assert.That(s.FaceLo, Is.EqualTo(49807.038).Within(1e-6));
            Assert.That(s.FaceHi, Is.EqualTo(59989.538).Within(1e-6));
            Assert.That(s.OwnLo, Is.EqualTo(s.FaceLo));
            Assert.That(s.LoKind, Is.EqualTo(BeamGeometry.EndKind.Edge));
            Assert.That(s.HiKind, Is.EqualTo(BeamGeometry.EndKind.Edge));
            Assert.That(s.OuterSide, Is.EqualTo(-1), "lico zewnętrzne = lewe (x = 1326923.9, 160 od obrysu)");
            Assert.That(r.TypicalWidth, Is.EqualTo(600));
            Assert.That(r.Messages, Is.Empty);
        }

        [Test]
        public void Plot528_ReversedOrientation_SameResult()
        {
            var z = Zone528.AsEnumerable().Reverse().ToList();
            var r = BeamGeometry.Analyze(z, Outline528);
            Assert.That(r.Segments.Count, Is.EqualTo(1));
            Assert.That(r.Segments[0].Width, Is.EqualTo(600).Within(1e-6));
            Assert.That(r.Segments[0].LoKind, Is.EqualTo(BeamGeometry.EndKind.Edge));
        }

        [Test]
        public void Plot539_TwoLCorners_InternalAndEdgeEnds()
        {
            var r = BeamGeometry.Analyze(Zone539, Outline539);
            Assert.That(r.Segments.Count, Is.EqualTo(3), string.Join(" | ", r.Segments));
            Assert.That(r.Segments.All(s => !s.Manual));
            var bottom = r.Segments.Single(s => s.AlongX && s.AcrossLo < 50000);
            var top = r.Segments.Single(s => s.AlongX && s.AcrossLo > 50000);
            var left = r.Segments.Single(s => !s.AlongX);

            // góra: narożnik L po lewej (lico zewnętrzne x = 1550278.18), koniec wewnętrzny po prawej
            Assert.That(top.LoKind, Is.EqualTo(BeamGeometry.EndKind.Corner));
            Assert.That(top.FaceLo, Is.EqualTo(1550278.18).Within(1e-6));
            Assert.That(top.OwnLo, Is.EqualTo(1550878.18).Within(1e-6));
            Assert.That(top.HiKind, Is.EqualTo(BeamGeometry.EndKind.Internal));
            Assert.That(top.FaceHi, Is.EqualTo(1553336.18).Within(1e-6));
            Assert.That(top.OuterSide, Is.EqualTo(1));

            // lewa: narożniki na obu końcach
            Assert.That(left.LoKind, Is.EqualTo(BeamGeometry.EndKind.Corner));
            Assert.That(left.HiKind, Is.EqualTo(BeamGeometry.EndKind.Corner));
            Assert.That(left.FaceLo, Is.EqualTo(48131.785).Within(1e-6));
            Assert.That(left.FaceHi, Is.EqualTo(58314.689).Within(1e-6));
            Assert.That(left.OwnLo, Is.EqualTo(48732.189).Within(1e-6));
            Assert.That(left.OwnHi, Is.EqualTo(57714.689).Within(1e-6));
            Assert.That(left.OuterSide, Is.EqualTo(-1));

            // dół: narożnik po lewej, koniec przy krawędzi po prawej (lico 160 od obrysu)
            Assert.That(bottom.LoKind, Is.EqualTo(BeamGeometry.EndKind.Corner));
            Assert.That(bottom.HiKind, Is.EqualTo(BeamGeometry.EndKind.Edge));
            Assert.That(bottom.FaceHi, Is.EqualTo(1555451.18).Within(1e-6));
            Assert.That(bottom.Width, Is.EqualTo(600.404).Within(1e-3));
            Assert.That(r.TypicalWidth, Is.EqualTo(600));
        }

        [Test]
        public void Plot319_SkewLegManual_TopAndRightLegs()
        {
            var r = BeamGeometry.Analyze(Zone319, Outline319);
            Assert.That(r.Segments.Count, Is.EqualTo(3), string.Join(" | ", r.Segments));
            var left = r.Segments.Single(s => !s.AlongX && s.AcrossHi < 110000);
            var right = r.Segments.Single(s => !s.AlongX && s.AcrossLo > 110000);
            var top = r.Segments.Single(s => s.AlongX);

            Assert.That(left.Manual, Is.True);
            Assert.That(left.ManualReason, Does.Contain("skos"));
            Assert.That(left.LoKind, Is.EqualTo(BeamGeometry.EndKind.Edge), "dolny koniec 160 od obrysu");
            Assert.That(left.HiKind, Is.EqualTo(BeamGeometry.EndKind.Corner));
            Assert.That(r.Messages.Count(m => m.Contains("detal ręczny")), Is.EqualTo(1));

            Assert.That(top.Manual, Is.False);
            Assert.That(top.Width, Is.EqualTo(600).Within(1e-6));
            Assert.That(top.LoKind, Is.EqualTo(BeamGeometry.EndKind.Corner));
            Assert.That(top.HiKind, Is.EqualTo(BeamGeometry.EndKind.Corner));
            Assert.That(top.FaceLo, Is.EqualTo(109180.206).Within(1e-6));
            Assert.That(top.FaceHi, Is.EqualTo(117787.706).Within(1e-6));
            Assert.That(top.OwnLo, Is.EqualTo(109780.206).Within(1e-6));
            Assert.That(top.OwnHi, Is.EqualTo(117187.706).Within(1e-6));

            Assert.That(right.Manual, Is.False);
            Assert.That(right.HiKind, Is.EqualTo(BeamGeometry.EndKind.Corner));
            Assert.That(right.LoKind, Is.EqualTo(BeamGeometry.EndKind.Internal), "koniec 2500 od krawędzi dolnej");
            Assert.That(right.FaceLo, Is.EqualTo(48805.429).Within(1e-6));
            Assert.That(right.FaceHi, Is.EqualTo(51697.929).Within(1e-6));
            Assert.That(right.OuterSide, Is.EqualTo(1));
        }

        [Test]
        public void TeeJunction_EndOnSideOfOtherLeg()
        {
            // belka pozioma 0..10000 (y 0..600) i pionowa od jej górnego lica (x 4700..5300, y 600..5000)
            var zone = P(0, 0, 10000, 0, 10000, 600, 5300, 600, 5300, 5000, 4700, 5000, 4700, 600, 0, 600);
            var r = BeamGeometry.Analyze(zone, null);
            Assert.That(r.Segments.Count, Is.EqualTo(2), string.Join(" | ", r.Segments));
            var v = r.Segments.Single(s => !s.AlongX);
            Assert.That(v.LoKind, Is.EqualTo(BeamGeometry.EndKind.Tee));
            Assert.That(v.FaceLo, Is.EqualTo(0).Within(1e-6), "T: pręty do dalszego lica belki poprzecznej");
            Assert.That(v.OwnLo, Is.EqualTo(600).Within(1e-6));
            var h = r.Segments.Single(s => s.AlongX);
            Assert.That(h.LoKind, Is.EqualTo(BeamGeometry.EndKind.Internal));
        }

        [Test]
        public void DiagonalOnlyZone_NoSegments_Message()
        {
            var zone = P(0, 0, 424.26, 424.26, 7495.3, 7495.3, 7071, 7919.6);
            var r = BeamGeometry.Analyze(zone, null);
            Assert.That(r.Segments, Is.Empty);
            Assert.That(r.Messages.Single(), Does.Contain("detal ręczny"));
        }

        // ── strzemiona ──────────────────────────────────────────────────

        [Test]
        public void Stirrups_528_Formula()
        {
            // L = 10182.5: n = ceil((L − 100) / 200) + 1 = 52, rozstaw ≤ 200
            var (first, step, n, fromUb) = BeamGeometry.StirrupLayout(49807.038, 59989.538);
            Assert.That(n, Is.EqualTo(52));
            Assert.That(step, Is.LessThanOrEqualTo(200.0));
            Assert.That(step, Is.EqualTo(10082.5 / 51).Within(1e-9));
            Assert.That(first, Is.EqualTo(49857.038).Within(1e-9));
            Assert.That(fromUb, Is.False);
        }

        [Test]
        public void Stirrups_ExactMultiple_And_Short()
        {
            Assert.That(BeamGeometry.StirrupLayout(0, 2100).Count, Is.EqualTo(11));   // 2000 / 200 + 1
            Assert.That(BeamGeometry.StirrupLayout(0, 2101).Count, Is.EqualTo(12));
            var one = BeamGeometry.StirrupLayout(0, 80);
            Assert.That(one.Count, Is.EqualTo(1));
            Assert.That(one.First, Is.EqualTo(40));
        }

        [Test]
        public void Stirrups_ShiftedHalfwayBetweenUb03()
        {
            // UB 03 co 196 od 40 — strzemiona w połowie (+100), rozstaw jak UB 03, w granicach lo+50 … hi−50
            var ub = Enumerable.Range(0, 30).Select(i => 40.0 + i * 196.0).ToList();
            var (first, step, n, fromUb) = BeamGeometry.StirrupLayout(0, 5000, ub03: ub);
            Assert.That(fromUb, Is.True);
            Assert.That(step, Is.EqualTo(196.0).Within(1e-9));
            Assert.That(first, Is.EqualTo(140.0).Within(1e-9));
            Assert.That(first + (n - 1) * step, Is.LessThanOrEqualTo(4950.0 + 1e-9));
            foreach (int i in Enumerable.Range(0, n))
                Assert.That(ub.Min(u => System.Math.Abs(u - (first + i * step))), Is.GreaterThanOrEqualTo(96.0 - 1e-9));
        }

        [Test]
        public void Stirrups_Ub03TooSparse_FallsBackToFormula()
        {
            var ub = new List<double> { 0, 250, 500, 750 };
            var r = BeamGeometry.StirrupLayout(0, 2100, ub03: ub);
            Assert.That(r.FromUb03, Is.False);
            Assert.That(r.Count, Is.EqualTo(11));
        }

        // ── plan prętów podłużnych ──────────────────────────────────────

        [Test]
        public void Bars_528_TwoEqualBars_Lap800Plus()
        {
            double available = 10182.5 - 80;
            var plan = BeamGeometry.PlanBars(available);
            Assert.That(plan.Count, Is.EqualTo(2));
            Assert.That(plan.All(p => p.Length == 5500));
            double lap = BeamGeometry.LapOf(plan);
            Assert.That(lap, Is.GreaterThanOrEqualTo(800));
            Assert.That(lap, Is.LessThan(1050));
            Assert.That(lap, Is.EqualTo(897.5).Within(1e-9));
            Assert.That(plan[1].Offset + plan[1].Length, Is.EqualTo(available).Within(1e-9));
        }

        [Test]
        public void Bars_Short_SingleSnappedDown()
        {
            var plan = BeamGeometry.PlanBars(2978);
            Assert.That(plan.Single().Length, Is.EqualTo(2750));
            Assert.That(plan.Single().Offset, Is.EqualTo(114).Within(1e-9), "pręt wyśrodkowany: (2978 − 2750) / 2");
            Assert.That(BeamGeometry.PlanBars(1100).Single().Length, Is.EqualTo(1100));
            Assert.That(BeamGeometry.PlanBars(6000).Single().Length, Is.EqualTo(6000));
            Assert.That(BeamGeometry.PlanBars(6000).Single().Offset, Is.EqualTo(0));
        }

        [TestCase(5900.0)]
        [TestCase(2978.0)]
        [TestCase(1110.0)]
        [TestCase(4999.0)]
        public void Bars_Single_Centered_EqualEndCover(double available)
        {
            var b = BeamGeometry.PlanBars(available).Single();
            double lo = b.Offset, hi = available - (b.Offset + b.Length);
            Assert.That(lo, Is.EqualTo(hi).Within(1e-9), "równa otulina na obu końcach");
            Assert.That(lo, Is.GreaterThanOrEqualTo(0));
            Assert.That(b.Length % 25.0, Is.EqualTo(0).Within(1e-9));
        }

        /// <summary>Zakład w przedziale [800, 1050) dla każdej długości; pręty na siatce 250, ≤ 6000, pokrywają całość.</summary>
        [TestCase(6001.0)]
        [TestCase(6251.0)]
        [TestCase(6800.0)]
        [TestCase(9000.0)]
        [TestCase(10102.5)]
        [TestCase(12500.0)]
        [TestCase(14000.0)]
        [TestCase(17777.0)]
        [TestCase(23456.0)]
        public void Bars_Lap_InRange_800_1050(double available)
        {
            var plan = BeamGeometry.PlanBars(available, 6000, 800);
            Assert.That(plan.Count, Is.GreaterThanOrEqualTo(2), "dłuższe niż 6000 — kilka prętów");
            Assert.That(plan[0].Offset, Is.EqualTo(0).Within(1e-9));
            Assert.That(plan[plan.Count - 1].Offset + plan[plan.Count - 1].Length, Is.EqualTo(available).Within(1e-9));
            foreach (var (_, L) in plan)
            {
                Assert.That(L, Is.LessThanOrEqualTo(6000));
                Assert.That(L % 250.0, Is.EqualTo(0).Within(1e-9), "siatka 250");
            }
            foreach (var lap in BeamGeometry.LapsOf(plan))
            {
                Assert.That(lap, Is.GreaterThanOrEqualTo(800 - 1e-9));
                Assert.That(lap, Is.LessThan(1050));
            }
            // pręty równe albo N − 1 równych + ostatni krótszy (osobna pozycja)
            var firsts = plan.Take(plan.Count - 1).Select(p => p.Length).Distinct().ToList();
            Assert.That(firsts.Count, Is.EqualTo(1));
            Assert.That(plan[plan.Count - 1].Length, Is.LessThanOrEqualTo(firsts[0]));
            // mniej zakładów zdecydowanie lepiej — dokładnie minimalna liczba prętów
            int nMin = System.Math.Max(2, (int)System.Math.Ceiling((available - 800) / 5200.0 - 1e-9));
            Assert.That(plan.Count, Is.EqualTo(nMin));
        }

        [Test]
        public void Bars_6251_EqualPlusShorter_NotThreeBars()
        {
            // 2 równe: 2 × 3750 → zakład 1249 (poza [800, 1050)); mniej zakładów lepiej → 3750 + 3500 (zakład 999), nie 3 × 2750
            var plan = BeamGeometry.PlanBars(6251);
            Assert.That(plan.Count, Is.EqualTo(2));
            Assert.That(plan[0].Length, Is.EqualTo(3750));
            Assert.That(plan[1].Length, Is.EqualTo(3500));
            Assert.That(plan[0].Offset, Is.EqualTo(0));
            Assert.That(plan[1].Offset, Is.EqualTo(2751).Within(1e-9));
            Assert.That(BeamGeometry.LapsOf(plan).Single(), Is.EqualTo(999).Within(1e-9));
        }

        [Test]
        public void Bars_17777_FourBars_NotFiveEqual()
        {
            // N = 4: 4 równe (5250) → zakład 1074.3 (poza przedziałem); b) 3 × 5250 (zakład 800) + 4500 (zakład 873), nie 5 × 4250
            var plan = BeamGeometry.PlanBars(17777);
            Assert.That(plan.Count, Is.EqualTo(4));
            Assert.That(plan.Take(3).All(p => p.Length == 5250));
            Assert.That(plan[3].Length, Is.EqualTo(4500));
            var laps = BeamGeometry.LapsOf(plan);
            Assert.That(laps[0], Is.EqualTo(800).Within(1e-9));
            Assert.That(laps[1], Is.EqualTo(800).Within(1e-9));
            Assert.That(laps[2], Is.EqualTo(873).Within(1e-9));
        }

        [Test]
        public void Bars_6800_Fallback_OneEqualPlusShorterLast()
        {
            // 2 równe: L ∈ [3800, 3925) — brak na siatce → 4000 + 3750 (zakład 950)
            var plan = BeamGeometry.PlanBars(6800);
            Assert.That(plan.Count, Is.EqualTo(2));
            Assert.That(plan[0].Length, Is.EqualTo(4000));
            Assert.That(plan[1].Length, Is.EqualTo(3750));
            Assert.That(BeamGeometry.LapsOf(plan).Single(), Is.EqualTo(950).Within(1e-9));
        }

        [Test]
        public void Bars_12500_9000_6001_Values()
        {
            var p12 = BeamGeometry.PlanBars(12500);
            Assert.That(p12.Count, Is.EqualTo(3));
            Assert.That(p12.All(p => p.Length == 4750));
            Assert.That(BeamGeometry.LapOf(p12), Is.EqualTo(875).Within(1e-9));
            var p9 = BeamGeometry.PlanBars(9000);
            Assert.That(p9.Count, Is.EqualTo(2));
            Assert.That(p9.All(p => p.Length == 5000));
            Assert.That(BeamGeometry.LapOf(p9), Is.EqualTo(1000).Within(1e-9));
            var p6 = BeamGeometry.PlanBars(6001);
            Assert.That(p6.Count, Is.EqualTo(2));
            Assert.That(p6.All(p => p.Length == 3500));
            Assert.That(BeamGeometry.LapOf(p6), Is.EqualTo(999).Within(1e-9));
        }

        // ── L z krótką nogą ─────────────────────────────────────────────

        [Test]
        public void ShortLegL_600x1200_BothLegsManual()
        {
            // noga pozioma 0..5000 (y 0..600) + krótka noga pionowa x 0..600 do y = 1200 (600 ponad narożnikiem)
            var zone = P(0, 0, 5000, 0, 5000, 600, 600, 600, 600, 1200, 0, 1200);
            // z obrysem płyty (krawędź przy lewym i dolnym licu) i bez — ten sam wynik: obie nogi „detal ręczny”, bez prętów
            foreach (var outline in new[] { null, P(-160, -160, 8000, -160, 8000, 6000, -160, 6000) })
            {
                BeamGeometry.Result r = null;
                Assert.DoesNotThrow(() => r = BeamGeometry.Analyze(zone, outline));
                Assert.That(r, Is.Not.Null);
                TestContext.WriteLine(string.Join(" | ", r.Segments) + " || " + string.Join(" | ", r.Messages));
                Assert.That(r.Segments.Count, Is.EqualTo(2), string.Join(" | ", r.Segments));
                Assert.That(r.Segments.All(s => s.Manual), Is.True, "brak prętów na obu nogach: " + string.Join(" | ", r.Segments));
                // krótka noga: koniec obok odcinka ręcznego — też detal ręczny, z komunikatem
                var shortLeg = r.Segments.Single(s => !s.AlongX);
                Assert.That(shortLeg.ManualReason, Does.Contain("przylega"));
                Assert.That(r.Messages.Count(m => m.Contains("detal ręczny")), Is.EqualTo(2), string.Join(" | ", r.Messages));
            }
        }

        [Test]
        public void Bars_Long_ThreeBars_MaxLength()
        {
            var plan = BeamGeometry.PlanBars(14000, 6000, 800);
            Assert.That(plan.Count, Is.EqualTo(3));
            Assert.That(plan.All(p => p.Length <= 6000));
            Assert.That(BeamGeometry.LapOf(plan), Is.GreaterThanOrEqualTo(800));
            Assert.That(plan[2].Offset + plan[2].Length, Is.EqualTo(14000).Within(1e-9));
        }

        [Test]
        public void Bars_LapParameter()
        {
            var plan = BeamGeometry.PlanBars(10102.5, 6000, 650);
            Assert.That(BeamGeometry.LapOf(plan), Is.GreaterThanOrEqualTo(650));
            Assert.That(plan.Count, Is.EqualTo(2));
        }

        // ── strzemię 51, pręty w poprzek ─────────────────────────────────

        [Test]
        public void Stirrup51_Dimensions()
        {
            // otuliny 2026-10-10: dół 50, boki 45 (górna 35)
            Assert.That(BeamGeometry.StirrupHeight(450), Is.EqualTo(340));          // 341 → 340
            Assert.That(BeamGeometry.StirrupHeight(375), Is.EqualTo(265));          // 266 → 265
            Assert.That(BeamGeometry.StirrupHeight(300), Is.EqualTo(190));          // 191 → 190
            Assert.That(BeamGeometry.StirrupHeight(450, topLayers: 1), Is.EqualTo(350));   // 353 → 350
            Assert.That(BeamGeometry.StirrupHeight(450, coverBottom: 40), Is.EqualTo(350));   // dawna otulina 40: 351 → 350
            Assert.That(BeamGeometry.StirrupWidth(600), Is.EqualTo(510));
            Assert.That(BeamGeometry.StirrupWidth(600.404), Is.EqualTo(510));   // PLOT 539 — dolna noga
            Assert.That(BeamGeometry.StirrupWidth(600, 40), Is.EqualTo(520));
            Assert.That(new BeamParams().CoverSide, Is.EqualTo(45));
            Assert.That(new BeamParams().CoverBottom, Is.EqualTo(50));
        }

        [Test]
        public void BarsAcross_600_FourH16()
        {
            var (first, sp) = BeamGeometry.BarsAcross(0, 600, 4, 16);
            Assert.That(first, Is.EqualTo(63));                       // 45 + 10 + 8
            Assert.That(sp, Is.EqualTo(158).Within(1e-9));            // 474 / 3
        }

        // ── opisy GA ────────────────────────────────────────────────────

        [Test]
        public void ParseThk_Variants()
        {
            Assert.That(BeamGeometry.ParseThk("450THK"), Is.EqualTo(450));
            Assert.That(BeamGeometry.ParseThk("375 THK"), Is.EqualTo(375));
            Assert.That(BeamGeometry.ParseThk("1050THK"), Is.EqualTo(1050));
            Assert.That(BeamGeometry.ParseThk("225mm THK SLAB"), Is.Null);
            Assert.That(BeamGeometry.ParseThk("300THK SLAB"), Is.Null);
            Assert.That(BeamGeometry.ParseThk("NIB TOC=44.075"), Is.Null);
            Assert.That(BeamGeometry.ParseThk("12.450THK"), Is.Null);
        }

        [Test]
        public void HeightFromLevels_Holbrook()
        {
            Assert.That(BeamGeometry.HeightFromLevels(44.375, 44.075), Is.EqualTo(450));   // 319
            Assert.That(BeamGeometry.HeightFromLevels(43.675, 43.450), Is.EqualTo(375));   // 528
            Assert.That(BeamGeometry.HeightFromLevels(43.625, 43.475), Is.EqualTo(300));   // 539
            Assert.That(BeamGeometry.ParseNibToc("NIB TOC=43.475"), Is.EqualTo(43.475).Within(1e-12));
        }

        // ── E4: narożniki L, złącza T, U-bary, koniec wewnętrzny ─────────

        /// <summary>Pręty zestawu w rzucie: łamane (ramię widoczne + ramię zagięte) — do sprawdzeń krzyżowania i położenia.</summary>
        private static List<List<(double X, double Y)>> BarPolylines(BeamGeometry.BentRun r)
        {
            var res = new List<List<(double X, double Y)>>();
            for (int i = 0; i < r.Count; i++)
            {
                double c = r.Across0 + i * r.Spacing;
                double s = r.Count > 1 ? r.Start0 + (r.StartN - r.Start0) * i / (r.Count - 1) : r.Start0;
                double e = s + r.LineLength;
                double v = r.BendAtStart ? s : e;                       // wierzchołek (zagięcie) na osi pręta
                double other = r.Kind == "U" ? r.B - r.Dia : r.B - r.Dia / 2.0;   // drugie ramię / tył U (oś)
                double c2 = c + r.BendDir * other;
                (double X, double Y) P(double along, double across) => r.AlongX ? (along, across) : (across, along);
                var pts = new List<(double X, double Y)> { P(r.BendAtStart ? e : s, c), P(v, c), P(v, c2) };
                if (r.Kind == "U") pts.Add(P(r.BendAtStart ? v + (r.C - r.Dia / 2.0) : v - (r.C - r.Dia / 2.0), c2));
                res.Add(pts);
            }
            return res;
        }

        private static bool SegmentsCross((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
        {
            double Cr((double X, double Y) o, (double X, double Y) p, (double X, double Y) q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
            double d1 = Cr(c, d, a), d2 = Cr(c, d, b), d3 = Cr(a, b, c), d4 = Cr(a, b, d);
            return ((d1 > 1e-6 && d2 < -1e-6) || (d1 < -1e-6 && d2 > 1e-6)) && ((d3 > 1e-6 && d4 < -1e-6) || (d3 < -1e-6 && d4 > 1e-6));
        }

        private static void AssertNoCrossing(List<List<(double X, double Y)>> bars)
        {
            for (int i = 0; i < bars.Count; i++)
                for (int j = i + 1; j < bars.Count; j++)
                    for (int a = 0; a + 1 < bars[i].Count; a++)
                        for (int b = 0; b + 1 < bars[j].Count; b++)
                            Assert.That(SegmentsCross(bars[i][a], bars[i][a + 1], bars[j][b], bars[j][b + 1]), Is.False,
                                $"pręty {i} i {j} krzyżują się");
        }

        private static void AssertInside(List<(double X, double Y)> zone, List<List<(double X, double Y)>> bars)
        {
            foreach (var b in bars)
                foreach (var p in b)
                    Assert.That(BeamGeometry.Inside(zone, p), Is.True, $"punkt pręta ({p.X:0.#}, {p.Y:0.#}) poza strefą");
        }

        private static BeamGeometry.LayerPlan Plan(BeamGeometry.Result r) => BeamGeometry.PlanLayer(r, 4, 16, 16, 800, 6000);

        /// <summary>Tyły zagnieżdżonych U-barów jednego końca: prześwit między kolejnymi tyłami ≥ 20 (recenzja E4).</summary>
        private static void AssertUBarBackGaps(List<BeamGeometry.BentRun> us)
        {
            foreach (var g in us.Where(b => b.Kind == "U").GroupBy(b => (b.Seg, b.BendAtStart)))
            {
                // oś tyłu U-bara: przy zagięciu na początku linii — Start0, inaczej koniec linii
                var backs = g.Select(b => b.BendAtStart ? b.Start0 : b.Start0 + b.LineLength).OrderBy(x => x).ToList();
                int dia = g.First().Dia;
                for (int i = 1; i < backs.Count; i++)
                    Assert.That(backs[i] - backs[i - 1] - dia, Is.GreaterThanOrEqualTo(20 - 1e-6), "prześwit między tyłami U-barów");
            }
        }

        [Test]
        public void E4_Plot539_Closures_LinkRanges_Stirrups()
        {
            var r = BeamGeometry.Analyze(Zone539, Outline539);
            var bottom = r.Segments.Single(s => s.AlongX && s.AcrossLo < 50000);
            var top = r.Segments.Single(s => s.AlongX && s.AcrossLo > 50000);
            var left = r.Segments.Single(s => !s.AlongX);
            Assert.That(r.Joints.Count(j => !j.IsTee), Is.EqualTo(2));
            Assert.That(top.LoClose, Is.EqualTo(BeamGeometry.Closure.LBar));
            Assert.That(top.HiClose, Is.EqualTo(BeamGeometry.Closure.UBar), "koniec wewnętrzny zamknięty jak przy krawędzi");
            Assert.That(left.LoClose, Is.EqualTo(BeamGeometry.Closure.LBar));
            Assert.That(left.HiClose, Is.EqualTo(BeamGeometry.Closure.LBar));
            Assert.That(bottom.LoClose, Is.EqualTo(BeamGeometry.Closure.LBar));
            Assert.That(bottom.HiClose, Is.EqualTo(BeamGeometry.Closure.UBar));
            // strzemiona: dłuższa noga (lewa) przez oba kwadraty narożników, pozostałe — zakres własny
            Assert.That(left.LinkLo, Is.EqualTo(left.FaceLo));
            Assert.That(left.LinkHi, Is.EqualTo(left.FaceHi));
            Assert.That(top.LinkLo, Is.EqualTo(top.OwnLo));
            Assert.That(bottom.LinkLo, Is.EqualTo(bottom.OwnLo));
            Assert.That(BeamGeometry.StirrupLayout(left.LinkLo, left.LinkHi).Count, Is.EqualTo(52));
            // koniec Hi z U-barami — pierwsze strzemię 125 od lica (otulina czołowa 45; dolna noga 24 → 23)
            Assert.That(BeamGeometry.StirrupLayout(top.LinkLo, top.LinkHi, 200, 50, null, 100, 125).Count, Is.EqualTo(13));
            Assert.That(BeamGeometry.StirrupLayout(bottom.LinkLo, bottom.LinkHi, 200, 50, null, 100, 125).Count, Is.EqualTo(23));
        }

        [Test]
        public void E4_Plot539_Plan_LBarsAndUBars()
        {
            var r = BeamGeometry.Analyze(Zone539, Outline539);
            var lp = Plan(r);
            TestContext.WriteLine(string.Join(" | ", lp.Bent) + " || " + string.Join(" | ", lp.Messages));
            var top = r.Segments.Single(s => s.AlongX && s.AcrossLo > 50000);
            var left = r.Segments.Single(s => !s.AlongX);
            var bottom = r.Segments.Single(s => s.AlongX && s.AcrossLo < 50000);

            // pręty proste: góra od kwadratu narożnika (bez otuliny) do lica końca − 45, lewa między kwadratami
            Assert.That(lp.Straight.Count, Is.EqualTo(4));
            Assert.That(lp.Straight.Where(x => x.Seg == top).Select(x => x.Length), Is.EqualTo(new[] { 2250.0 }));
            Assert.That(lp.Straight.Where(x => x.Seg == left).Select(x => x.Length), Is.EqualTo(new[] { 5000.0, 5000.0 }));
            Assert.That(lp.Straight.Where(x => x.Seg == bottom).Select(x => x.Length), Is.EqualTo(new[] { 4500.0 }));
            var lefts = lp.Straight.Where(x => x.Seg == left).ToList();
            Assert.That(lefts[0].Start, Is.EqualTo(left.OwnLo).Within(1e-6));
            Assert.That(lefts[1].Start + lefts[1].Length, Is.EqualTo(left.OwnHi).Within(1e-6));

            var L = lp.Bent.Where(b => b.Kind == "L").ToList();
            var U = lp.Bent.Where(b => b.Kind == "U").ToList();
            Assert.That(L.Count, Is.EqualTo(2));
            Assert.That(U.Count, Is.EqualTo(4), "2 U-bary × 2 końce (wewnętrzny + przy krawędzi)");
            Assert.That(lp.Bent.Count, Is.EqualTo(6));
            foreach (var b in L)
            {
                Assert.That(b.Shape, Is.EqualTo("11"));
                Assert.That(b.Count, Is.EqualTo(4), "pręty L we wszystkich rzędach");
                Assert.That(b.Overlap, Is.GreaterThanOrEqualTo(800 - 1e-6));
                Assert.That(b.A % 25.0, Is.EqualTo(0).Within(1e-9));
                Assert.That(b.B % 25.0, Is.EqualTo(0).Within(1e-9));
                var pl = BarPolylines(b);
                AssertNoCrossing(pl);
                AssertInside(Zone539, pl);
            }
            // narożnik górny-lewy: A (wzdłuż X) = 600 − 50 + 84 (pręt prosty wyśrodkowany) + 800 + 8 → 1450; B = 1350
            var tl = L.Single(b => b.Seg == top);
            Assert.That((tl.A, tl.B), Is.EqualTo((1450.0, 1350.0)));
            var bl = L.Single(b => b.Seg == bottom);
            Assert.That((bl.A, bl.B), Is.EqualTo((1375.0, 1350.0)));      // otulina 45 (wcześniej 1375 × 1375)

            foreach (var b in U)
            {
                Assert.That(b.Shape, Is.EqualTo("21"));
                Assert.That(b.Count, Is.EqualTo(1));
                Assert.That(b.C, Is.EqualTo(b.A));
                Assert.That(b.Overlap, Is.GreaterThanOrEqualTo(800 - 1e-6));
                AssertInside(Zone539, BarPolylines(b));
            }
            Assert.That(U.Select(b => b.B).OrderBy(x => x), Is.EqualTo(new[] { 170.0, 170.0, 490.0, 490.0 }));   // otulina 45: 158 + 16 → 170, 474 + 16
            // koniec wewnętrzny (góra): pręt prosty wyśrodkowany (84 od otuliny) — ramiona dłuższe
            // (U-bar wewnętrzny — tył o Ø + 20 głębiej, ramię krótsze)
            Assert.That(U.Where(b => b.Seg == top).Select(b => (b.A, b.B)).OrderBy(x => x.B), Is.EqualTo(new[] { (850.0, 170.0), (900.0, 490.0) }));
            Assert.That(U.Where(b => b.Seg == bottom).Select(b => (b.A, b.B)).OrderBy(x => x.B), Is.EqualTo(new[] { (800.0, 170.0), (825.0, 490.0) }));
            AssertUBarBackGaps(U);
            // U-bary zagnieżdżone nie krzyżują się
            AssertNoCrossing(U.Where(b => b.Seg == bottom).SelectMany(BarPolylines).ToList());
            Assert.That(lp.Messages, Is.Empty, string.Join(" | ", lp.Messages));
        }

        [Test]
        public void E4_Plot528_UBarsBothEdgeEnds_StraightUnchanged()
        {
            var r = BeamGeometry.Analyze(Zone528, Outline528);
            var s = r.Segments.Single();
            Assert.That(s.LoClose, Is.EqualTo(BeamGeometry.Closure.UBar));
            Assert.That(s.HiClose, Is.EqualTo(BeamGeometry.Closure.UBar));
            Assert.That(BeamGeometry.StirrupLayout(s.LinkLo, s.LinkHi).Count, Is.EqualTo(52), "E3 (strzemię 50 od lica)");
            Assert.That(BeamGeometry.StirrupLayout(s.LinkLo, s.LinkHi, 200, 125, null, 100, 125).Count, Is.EqualTo(51),
                        "E4: pierwsze strzemię za tyłami U-barów (125 od lica)");
            var lp = Plan(r);
            Assert.That(lp.Straight.Select(x => x.Length), Is.EqualTo(new[] { 5500.0, 5500.0 }), "bez zmian względem E3");
            Assert.That(lp.Straight[0].Start, Is.EqualTo(s.FaceLo + 45).Within(1e-6));   // otulina czołowa 45
            var U = lp.Bent;
            Assert.That(U.Count, Is.EqualTo(4));
            Assert.That(U.All(b => b.Kind == "U" && b.Shape == "21" && b.C == b.A));
            // zewnętrzny 800, wewnętrzny (tył o Ø + 20 głębiej) 775 — zakład ≥ 800
            Assert.That(U.Select(b => (b.A, b.B)).OrderBy(x => x.B).ThenBy(x => x.A),
                        Is.EqualTo(new[] { (775.0, 170.0), (775.0, 170.0), (800.0, 490.0), (800.0, 490.0) }));
            Assert.That(U.Select(b => b.B).OrderBy(x => x), Is.EqualTo(new[] { 170.0, 170.0, 490.0, 490.0 }));
            Assert.That(U.All(b => b.Overlap >= 800 - 1e-6));
            // tył U-bara zewnętrznego na otulinie 45 od lica końca, wewnętrzny o Ø 16 + prześwit 20 głębiej
            var lo = U.Where(b => b.BendAtStart).OrderByDescending(b => b.B).ToList();
            Assert.That(lo[0].Start0, Is.EqualTo(s.FaceLo + 45 + 8).Within(1e-6));
            Assert.That(lo[1].Start0, Is.EqualTo(s.FaceLo + 45 + 16 + 20 + 8).Within(1e-6));
            AssertUBarBackGaps(U);
            // pierwsze strzemię za tyłami U-barów (prześwit ≥ 20): 4 H16 → 125 od lica (45 + 36 + 16 + 20 + 5 = 122 → 125)
            double offU = BeamGeometry.StirrupEndOffsetAtUBars(4, 16);
            Assert.That(offU, Is.EqualTo(125));
            Assert.That(BeamGeometry.StirrupEndOffsetAtUBars(4, 16, cover: 40), Is.EqualTo(120), "dawna otulina 40");
            var st = BeamGeometry.StirrupLayout(s.LinkLo, s.LinkHi, 200, offU, null, 100, offU);
            Assert.That(st.First, Is.EqualTo(s.FaceLo + 125).Within(1e-6));
            Assert.That(st.First + (st.Count - 1) * st.Step, Is.EqualTo(s.FaceHi - 125).Within(1e-6));
            double deepestBackInner = lo[1].Start0 + 8;
            Assert.That(st.First - 5 - deepestBackInner, Is.GreaterThanOrEqualTo(20 - 1e-6), "prześwit tył U-bara — strzemię");
            AssertInside(Zone528, U.SelectMany(BarPolylines).ToList());
        }

        [Test]
        public void E4_Plot319_CornerWithManualLeg_Unchanged_RightCornerAndInternalEnd()
        {
            var r = BeamGeometry.Analyze(Zone319, Outline319);
            var top = r.Segments.Single(s => s.AlongX);
            var right = r.Segments.Single(s => !s.AlongX && s.AcrossLo > 110000);
            Assert.That(top.LoClose, Is.EqualTo(BeamGeometry.Closure.None), "narożnik z nogą „detal ręczny” — jak E3");
            Assert.That(top.HiClose, Is.EqualTo(BeamGeometry.Closure.LBar));
            Assert.That(right.LoClose, Is.EqualTo(BeamGeometry.Closure.UBar), "koniec wewnętrzny");
            Assert.That(right.HiClose, Is.EqualTo(BeamGeometry.Closure.LBar));
            Assert.That(top.LinkLo, Is.EqualTo(top.OwnLo));
            Assert.That(top.LinkHi, Is.EqualTo(top.FaceHi), "góra dłuższa — strzemiona przez kwadrat narożnika");
            Assert.That(BeamGeometry.StirrupLayout(top.LinkLo, top.LinkHi).Count, Is.EqualTo(41));
            Assert.That(BeamGeometry.StirrupLayout(right.LinkLo, right.LinkHi, 200, 125, null, 100, 50).Count, Is.EqualTo(12));
            var lp = Plan(r);
            TestContext.WriteLine(string.Join(" | ", lp.Bent) + " || " + string.Join(" | ", lp.Messages));
            Assert.That(lp.Straight.Where(x => x.Seg == top).Select(x => x.Length), Is.EqualTo(new[] { 4500.0, 4500.0 }));
            Assert.That(lp.Straight.First(x => x.Seg == top).Start, Is.EqualTo(top.FaceLo + 45).Within(1e-6), "lewy koniec jak E3 (otulina 45)");
            Assert.That(lp.Bent.Count(b => b.Kind == "L"), Is.EqualTo(1));
            Assert.That(lp.Bent.Count(b => b.Kind == "U"), Is.EqualTo(2));
            // otulina czołowa 45 (2026-10-10): prawa noga — dostępne 2247.5 < 2250 → pręt prosty 2000 (było 2250), ramiona U-barów /
            // pręta L dłuższe i nachodzą na siebie (93 mm) — dodatkowy komunikat „krótki odcinek”
            Assert.That(lp.Messages.Count(m => m.Contains("detal ręczny")), Is.EqualTo(2), string.Join(" | ", lp.Messages));
            Assert.That(lp.Messages.Count(m => m.Contains("nachodzą na siebie (93 mm")), Is.EqualTo(1));
            Assert.That(lp.Straight.Where(x => x.Seg == right).Select(x => x.Length), Is.EqualTo(new[] { 2000.0 }));
            foreach (var b in lp.Bent)
            {
                Assert.That(b.Overlap, Is.GreaterThanOrEqualTo(800 - 1e-6));
                AssertInside(Zone319, BarPolylines(b));
            }
            AssertNoCrossing(BarPolylines(lp.Bent.Single(b => b.Kind == "L")));
        }

        [Test]
        public void E4_Tee_LBarsAnchoredInContinuousBeam()
        {
            var zone = P(0, 0, 10000, 0, 10000, 600, 5300, 600, 5300, 5000, 4700, 5000, 4700, 600, 0, 600);
            var r = BeamGeometry.Analyze(zone, null);
            var v = r.Segments.Single(s => !s.AlongX);
            var h = r.Segments.Single(s => s.AlongX);
            Assert.That(r.Joints.Single().IsTee, Is.True);
            Assert.That(h.Manual, Is.False, "belka ciągła przez złącze T (przerwa lica wypełniona strefą) — nie detal ręczny");
            Assert.That(h.OwnLo, Is.EqualTo(0).Within(1e-6));
            Assert.That(h.OwnHi, Is.EqualTo(10000).Within(1e-6));
            Assert.That(v.LoClose, Is.EqualTo(BeamGeometry.Closure.Tee));
            Assert.That(v.HiClose, Is.EqualTo(BeamGeometry.Closure.UBar));
            Assert.That(h.LoClose, Is.EqualTo(BeamGeometry.Closure.UBar));
            Assert.That(h.HiClose, Is.EqualTo(BeamGeometry.Closure.UBar));
            var lp = Plan(r);
            TestContext.WriteLine(string.Join(" | ", lp.Bent) + " || " + string.Join(" | ", lp.Messages));
            // pręty belki dochodzącej od lica belki ciągłej (y = 600), bez otuliny
            Assert.That(BeamGeometry.StraightRange(v).Lo, Is.EqualTo(600).Within(1e-6));
            Assert.That(BeamGeometry.StraightRange(v).Hi, Is.EqualTo(5000 - 45).Within(1e-6));
            var T = lp.Bent.Where(b => b.Kind == "T").ToList();
            Assert.That(T.Count, Is.EqualTo(2));
            Assert.That(T.Sum(b => b.Count), Is.EqualTo(4));
            Assert.That(T.Select(b => b.BendDir).OrderBy(x => x), Is.EqualTo(new[] { -1, 1 }));
            Assert.That(T.All(b => b.Shape == "11" && b.B == 800 && b.A == T[0].A), "jedna pozycja, ramię w belce ciągłej = zakład");
            Assert.That(T.All(b => b.Overlap >= 800 - 1e-6));
            var bars = T.SelectMany(BarPolylines).ToList();
            AssertNoCrossing(bars);
            AssertInside(zone, bars);
            // zagięcia w rzędach belki ciągłej (y 63 … 537 — otulina 45)
            foreach (var b in bars) Assert.That(b[1].Y, Is.InRange(63 - 1e-6, 537 + 1e-6));
            Assert.That(lp.Bent.Count(b => b.Kind == "U"), Is.EqualTo(6), "3 końce × 2 U-bary");
        }

        [Test]
        public void E4_ShortLegL_Manual_NoBentBars()
        {
            var zone = P(0, 0, 5000, 0, 5000, 600, 600, 600, 600, 1200, 0, 1200);
            var r = BeamGeometry.Analyze(zone, null);
            var lp = Plan(r);
            Assert.That(lp.Straight, Is.Empty);
            Assert.That(lp.Bent, Is.Empty);
        }

        [Test]
        public void E4_ShapeLengths_BS8666()
        {
            // BS 8666:2020: 11 — A + B − 0.5r − d; 21 — A + B + C − r − 2d (H16: r = 32), w górę do 25 mm
            Assert.That(BarLengthRules.TotalLengthFor("11", new[] { 1450.0, 1350.0 }, 16), Is.EqualTo(2775));   // 2768
            Assert.That(BarLengthRules.TotalLengthFor("21", new[] { 800.0, 490.0, 800.0 }, 16), Is.EqualTo(2050));   // 2026 (otulina 45)
            Assert.That(BarLengthRules.TotalLengthFor("21", new[] { 800.0, 170.0, 800.0 }, 16), Is.EqualTo(1725));   // 1706
        }

        [Test]
        public void E4_OddCount_MiddleBarWithoutUBar_Message()
        {
            var r = BeamGeometry.Analyze(Zone528, Outline528);
            var lp = BeamGeometry.PlanLayer(r, 3, 16, 16, 800, 6000);
            Assert.That(lp.Bent.Count, Is.EqualTo(2), "1 U-bar na koniec");
            Assert.That(lp.Messages.Count(m => m.Contains("bez U-bara")), Is.EqualTo(2));
        }

        [Test]
        public void E4_Crossing_Plus_ManualWithMessage_NoStirrups()
        {
            // skrzyżowanie „+”: belka pozioma 0..10000 i pionowa −4000..4600 (szer. 600) — detal ręczny, bez prętów i strzemion
            var zone = P(0, 0, 4700, 0, 4700, -4000, 5300, -4000, 5300, 0, 10000, 0, 10000, 600, 5300, 600,
                         5300, 4600, 4700, 4600, 4700, 600, 0, 600);
            var r = BeamGeometry.Analyze(zone, null);
            TestContext.WriteLine(string.Join(" | ", r.Segments) + " || " + string.Join(" | ", r.Messages));
            Assert.That(r.Segments.Count, Is.EqualTo(2));
            Assert.That(r.Segments.All(s => s.Manual), Is.True);
            Assert.That(r.Segments.All(s => s.ManualReason.Contains("skrzyżowanie")), Is.True);
            Assert.That(r.Messages.Count(m => m.Contains("skrzyżowanie") && m.Contains("detal ręczny")), Is.EqualTo(2));
            var lp = Plan(r);
            Assert.That(lp.Straight, Is.Empty);
            Assert.That(lp.Bent, Is.Empty);
        }

        [Test]
        public void E4_ShortSegment_ArmsFromBothEndsOverlap_Message()
        {
            // odcinek 1700 z U-barami na obu końcach — ramiona (zakład 800) z obu końców nachodzą
            var r = BeamGeometry.Analyze(P(0, 0, 1700, 0, 1700, 600, 0, 600), null);
            Assert.That(r.Segments.Count, Is.EqualTo(1));
            var lp = Plan(r);
            TestContext.WriteLine(string.Join(" | ", lp.Bent) + " || " + string.Join(" | ", lp.Messages));
            Assert.That(lp.Messages.Count(m => m.Contains("z obu końców nachodzą")), Is.EqualTo(1));
            // długi odcinek (528) — bez ostrzeżenia
            Assert.That(Plan(BeamGeometry.Analyze(Zone528, Outline528)).Messages.Any(m => m.Contains("nachodzą")), Is.False);
        }

        [Test]
        public void E4_OneBar_EndWithoutUBar_Message()
        {
            var r = BeamGeometry.Analyze(Zone528, Outline528);
            var lp = BeamGeometry.PlanLayer(r, 1, 16, 16, 800, 6000);
            Assert.That(lp.Bent, Is.Empty);
            Assert.That(lp.Messages.Count(m => m.Contains("bez U-bara")), Is.EqualTo(2));
            Assert.That(BeamGeometry.StirrupEndOffsetAtUBars(1, 16), Is.EqualTo(50), "bez U-barów — strzemię jak dotąd");
        }

        [Test]
        public void E4_Tee_BendsOnRowsOfContinuousBeam_RowsAcross()
        {
            // belka dochodząca od dołu (Hi koniec w złączu) — zagięcia na osiach prętów belki ciągłej (RowsAcross)
            var zone = P(0, 5000, 4700, 5000, 4700, 0, 5300, 0, 5300, 5000, 10000, 5000, 10000, 5600, 0, 5600);
            var r = BeamGeometry.Analyze(zone, null);
            var a = r.Segments.Single(s => s.AlongX);
            var rows = BeamGeometry.RowsAcross(a, 4, 16);
            var T = Plan(r).Bent.Where(b => b.Kind == "T").ToList();
            Assert.That(T.Count, Is.EqualTo(2));
            var bends = T.SelectMany(BarPolylines).Select(b => b[1].Y).OrderBy(y => y).ToList();
            foreach (var y in bends) Assert.That(rows.Any(q => System.Math.Abs(q - y) < 1e-6), Is.True, $"zagięcie y = {y} poza rzędem belki ciągłej");
            AssertNoCrossing(T.SelectMany(BarPolylines).ToList());
            AssertInside(zone, T.SelectMany(BarPolylines).ToList());
        }

        [Test]
        public void BeamCodes_DoNotCollideWithSlabCodes()
        {
            foreach (var c in new[] { BeamGeometry.CodeBottom, BeamGeometry.CodeTop, BeamGeometry.CodeLinks })
            {
                Assert.That(BeamGeometry.IsBeamCode(c));
                Assert.That(new[] { "B1", "B2", "T1", "T2", "B3" }.Any(s => c.StartsWith(s)), Is.False);
            }
            Assert.That(BeamGeometry.IsBeamCode("B1"), Is.False);
        }
    }
}
