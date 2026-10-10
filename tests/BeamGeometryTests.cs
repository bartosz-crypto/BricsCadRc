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
            Assert.That(BeamGeometry.StirrupHeight(450), Is.EqualTo(350));          // 351 → 350
            Assert.That(BeamGeometry.StirrupHeight(375), Is.EqualTo(275));          // 276 → 275
            Assert.That(BeamGeometry.StirrupHeight(300), Is.EqualTo(200));          // 201 → 200
            Assert.That(BeamGeometry.StirrupHeight(450, topLayers: 1), Is.EqualTo(360));   // 363 → 360
            Assert.That(BeamGeometry.StirrupWidth(600), Is.EqualTo(520));
            Assert.That(BeamGeometry.StirrupWidth(600.404), Is.EqualTo(520));   // PLOT 539 — dolna noga
        }

        [Test]
        public void BarsAcross_600_FourH16()
        {
            var (first, sp) = BeamGeometry.BarsAcross(0, 600, 4, 16);
            Assert.That(first, Is.EqualTo(58));
            Assert.That(sp, Is.EqualTo(484.0 / 3).Within(1e-9));
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
