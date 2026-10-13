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
            // decyzja 2026-10-12 „tyle, ile się zmieści”: L = 10182.5, zakres 10082.5 → n = floor(10082.5 / 200) + 1 = 51, rozstaw 200,
            // reszta 82.5 po równo na końcach
            // 2026-10-13: ostatnie 50 + 82.5 = 132.5 > 80 od lica → +1 strzemię, rozstaw równy 10082.5 / 51 ≤ 200
            var (first, step, n, fromUb) = BeamGeometry.StirrupLayout(49807.038, 59989.538);
            Assert.That(n, Is.EqualTo(52));
            Assert.That(step, Is.EqualTo(10082.5 / 51).Within(1e-9));
            Assert.That(first + (n - 1) * step, Is.EqualTo(59989.538 - 50).Within(1e-6));
            Assert.That(first, Is.EqualTo(49857.038).Within(1e-9), "2026-10-13: pierwsze przy licu, reszta na drugim końcu");
            Assert.That(fromUb, Is.False);
        }

        [Test]
        public void Stirrups_ExactMultiple_And_Short()
        {
            Assert.That(BeamGeometry.StirrupLayout(0, 2100).Count, Is.EqualTo(11));   // 2000 / 200 + 1
            Assert.That(BeamGeometry.StirrupLayout(0, 2101).Count, Is.EqualTo(11));   // 2001 — 12. się nie mieści (2026-10-12)
            // 2026-10-13: reszta przy końcu ≤ 80 od lica — bez dodatkowego (2130: ostatnie 80 od lica → 11); 2131 → 12, rozstaw ≤ 200
            var a = BeamGeometry.StirrupLayout(0, 2130);
            Assert.That((a.Count, a.Step), Is.EqualTo((11, 200.0)));
            var b = BeamGeometry.StirrupLayout(0, 2131);
            Assert.That(b.Count, Is.EqualTo(12));
            Assert.That(b.First + (b.Count - 1) * b.Step, Is.EqualTo(2081).Within(1e-9));
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
            // otuliny 2026-10-12: górna 40, dół 45, boki 45 (35 / 40 / 40 + 5 zapasu) — suma góra + dół jak dawniej (85)
            Assert.That(BeamGeometry.StirrupHeight(450), Is.EqualTo(340));          // 341 → 340
            Assert.That(BeamGeometry.StirrupHeight(375), Is.EqualTo(265));          // 266 → 265
            Assert.That(BeamGeometry.StirrupHeight(300), Is.EqualTo(190));          // 191 → 190
            Assert.That(BeamGeometry.StirrupHeight(450, topLayers: 1), Is.EqualTo(350));   // 353 → 350
            Assert.That(BeamGeometry.StirrupHeight(450, coverBottom: 40), Is.EqualTo(345));   // otulina dolna 40 (górna 40): 346 → 345
            Assert.That(BeamGeometry.StirrupWidth(600), Is.EqualTo(510));
            Assert.That(BeamGeometry.StirrupWidth(600.404), Is.EqualTo(510));   // PLOT 539 — dolna noga
            Assert.That(BeamGeometry.StirrupWidth(600, 40), Is.EqualTo(520));
            Assert.That(new BeamParams().CoverSide, Is.EqualTo(45));
            Assert.That(new BeamParams().CoverBottom, Is.EqualTo(45));
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

        // ── K2 / K3 (praktyka biura C12 / Holbrook — docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md) ──

        /// <summary>Plan K3 domyślny: 4 H16, zakład 40·Ø = 650, koniec pręta prostego 61 od lica, przed kwadratem narożnika.</summary>
        private static BeamGeometry.LayerPlan Plan(BeamGeometry.Result r, bool intoSquare = false)
            => BeamGeometry.PlanLayer(r, 16, 650, 6000, 45, 61, intoSquare);

        private static List<BeamGeometry.VUBarRun> UBars(BeamGeometry.Result r, BeamGeometry.LayerPlan lp, double stirrupA, List<string> msgs = null,
                                                         bool intoSquare = false)
            => BeamGeometry.PlanVerticalUBars(r, lp, 4, 16, 16, stirrupA, 12, 650, 90, 35, 6000, 45, 61, intoSquare, msgs ?? new List<string>());

        [Test]
        public void K2_StirrupHeight_CoverTop_A1_A2()
        {
            // A1 (otulina górna 40 = 35 + 5, dolna 45 — 2026-10-12): 450 → 365, 375 → 290, 300 → 215
            Assert.That(BeamGeometry.StirrupHeightCovers(450), Is.EqualTo(365));
            Assert.That(BeamGeometry.StirrupHeightCovers(375), Is.EqualTo(290));
            Assert.That(BeamGeometry.StirrupHeightCovers(300), Is.EqualTo(215));
            // A2 (otulina górna 50, dolna 45): 450 → 355, 600 → 505, 750 → 655
            Assert.That(BeamGeometry.StirrupHeightCovers(450, BeamGeometry.CoverTopA2), Is.EqualTo(355));
            Assert.That(BeamGeometry.StirrupHeightCovers(600, BeamGeometry.CoverTopA2), Is.EqualTo(505));
            Assert.That(BeamGeometry.StirrupHeightCovers(750, BeamGeometry.CoverTopA2), Is.EqualTo(655));
            Assert.That(BeamGeometry.StirrupFirstOffset(12), Is.EqualTo(51), "pierwsze strzemię: otulina 45 + Ø/2");
        }

        [Test]
        public void LinkLayers_X_Minus15_UBarsNested_2026_10_13()
        {
            // H 450: strzemiona Y 365 (1. warstwa), X 350 (− 15); U-bary H16 / strzemiona H12: koniec X 325, koniec Y / narożnik Y 340,
            // narożnik X 305 (wewnątrz U-bara nogi Y)
            double a = BeamGeometry.StirrupHeightCovers(450);
            Assert.That(a, Is.EqualTo(365));
            Assert.That(BeamGeometry.LinkHeightFor(a, false), Is.EqualTo(365));
            Assert.That(BeamGeometry.LinkHeightFor(a, true), Is.EqualTo(350));
            Assert.That(BeamGeometry.Ceil5(2 * 12), Is.EqualTo(25));
            Assert.That(BeamGeometry.Ceil5(2 * 16), Is.EqualTo(35));
            Assert.That(new BeamParams().UBarDrop, Is.EqualTo(0), "0 = auto 2·Ø U-bara");
        }

        [Test]
        public void K2_Defaults_And_Laps()
        {
            var p = new BeamParams();
            Assert.That(p.StirrupDia, Is.EqualTo(12));
            Assert.That(p.CoverTop, Is.EqualTo(40));
            Assert.That(p.SideMode, Is.EqualTo(3));
            Assert.That(p.NibTopDia, Is.EqualTo(0));
            Assert.That(p.NibBottomDia, Is.EqualTo(10), "pręty w nibie 2T+1B — H10 (decyzja 2026-10-11)");
            Assert.That(p.UsesBothMark, Is.True);
            // zakład 40·Ø (nota 12 biura): H10 400, H12 500, H16 650
            Assert.That(p.LapFor(10), Is.EqualTo(400));
            Assert.That(p.LapFor(12), Is.EqualTo(500));
            Assert.That(p.LapFor(16), Is.EqualTo(650));
            Assert.That(p.StraightEndOffsetAuto, Is.EqualTo(61), "otulina 45 + Ø U-bara 16");
            p.StraightEndOffset = 90;
            Assert.That(p.StraightEndOffsetAuto, Is.EqualTo(90));
            p.TopCount = 3;
            Assert.That(p.UsesBothMark, Is.False, "dół ≠ góra → BB / BT");
        }

        [Test]
        public void K2_SideBarModes_B1_B2_B3()
        {
            var p = new BeamParams();
            Assert.That(p.SideBarsActive(450), Is.False, "B3: H ≤ 450 — brak");
            Assert.That(p.SideBarsActive(525), Is.True, "B3: od 525");
            p.SideMode = 1;
            Assert.That(p.SideBarsActive(750), Is.False, "B1: nigdy");
            p.SideMode = 2;
            Assert.That(p.SideBarsActive(300), Is.True, "B2: zawsze");
            p.SideDia = 0;
            Assert.That(p.SideBarsActive(750), Is.False, "Ø 0 = brak");
        }

        [Test]
        public void K2_MigrateV1_NewDefaults_GeometryKept_Message()
        {
            var v1 = new BeamParams
            {
                Height = 375, Width = 600, NibWidth = 160, NibHeight = 150, CoverSide = 45, CoverBottom = 50,
                StirrupDia = 10, Lap = 800, SideDia = 10, NibTopDia = 10, NibBottomDia = 16
            };
            var n = BeamParams.MigrateV1(v1);
            Assert.That(n.MigratedFromV1, Is.True);
            Assert.That((n.Height, n.Width, n.NibWidth, n.NibHeight), Is.EqualTo((375.0, 600.0, 160.0, 150.0)));
            Assert.That(n.StirrupDia, Is.EqualTo(12));
            Assert.That(n.NibTopDia, Is.EqualTo(0));
            Assert.That(n.NibBottomDia, Is.EqualTo(10));
            Assert.That(n.SideMode, Is.EqualTo(3));
            Assert.That(n.LapFor(16), Is.EqualTo(650));
            Assert.That(n.MigrationNote, Does.Contain("H10 → H12"));
            Assert.That(n.MigrationNote, Does.Contain("zakład 800"));
            Assert.That(n.MigrationNote, Does.Contain("nib górą H10 → brak"));
            Assert.That(n.MigrationNote, Does.Contain("nib dołem H16 → pręty w nibie 2T+1B H10"));
            Assert.That(n.MigrationNote, Does.Contain("tryb B3"));
        }

        [Test]
        public void K3_CountText_2x4()
        {
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeBoth, 4, 8), Is.EqualTo("2x4"));
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeBoth, 4, null), Is.EqualTo("4"));
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeBoth, 4, 6), Is.EqualTo("6"), "krotność niecałkowita — liczba");
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeSide, 2, 4), Is.EqualTo("2x2"), "K4: BS „2x{n}”");
            Assert.That(BarLabelRules.CountText("B1", 31, null), Is.EqualTo("31"));
            Assert.That(BarLabelRules.CountText("B1", 31, 33), Is.EqualTo("33"));
            Assert.That(BarLabelRules.KeepsMultiplicity(BeamGeometry.CodeBoth), Is.True);
            Assert.That(BarLabelRules.KeepsMultiplicity(BeamGeometry.CodeSide), Is.True);
            Assert.That(BarLabelRules.KeepsMultiplicity(BeamGeometry.CodeUBar), Is.False);
        }

        [Test]
        public void K3_UBarHeights_Holbrook_C12()
        {
            // B U-bara = A strzemienia − 2·Ø strzemienia (w dół do 5): Holbrook A1 528 → 265, 319 → 340, 539 → 190; C12 A2 450 → 325,
            // 600 → 475, 750 → 625
            var r = BeamGeometry.Analyze(Zone528, Outline528);
            var lp = Plan(r);
            // 2026-10-12: otulina dolna 45, A1 = 40
            foreach (var (h, ct, b) in new[] { (375.0, 40.0, 265.0), (450.0, 40.0, 340.0), (300.0, 40.0, 190.0),
                                               (450.0, 50.0, 330.0), (600.0, 50.0, 480.0), (750.0, 50.0, 630.0) })
            {
                var us = UBars(r, lp, BeamGeometry.StirrupHeightCovers(h, ct));
                Assert.That(us.Select(u => u.B).Distinct(), Is.EqualTo(new[] { b }), $"H {h}, otulina górna {ct}");
            }
        }

        [Test]
        public void K3_Plot528_StraightBars_VerticalUBars()
        {
            var r = BeamGeometry.Analyze(Zone528, Outline528);
            var s = r.Segments.Single();
            Assert.That(s.LoClose, Is.EqualTo(BeamGeometry.Closure.UBar));
            Assert.That(s.HiClose, Is.EqualTo(BeamGeometry.Closure.UBar));
            var lp = Plan(r);
            TestContext.WriteLine(string.Join(" | ", lp.Straight.Select(x => $"{x.Start:0.#}+{x.Length:0}")) + " || " + string.Join(" | ", lp.Messages));
            // pręty proste do lica − (otulina 45 + Ø U 16) = 61; zakład ≥ 650 (mniej zakładów: 5500 + 5250 zamiast 3 prętów ręcznie)
            Assert.That(lp.Straight[0].Start, Is.EqualTo(s.FaceLo + 61).Within(1e-6));
            var last = lp.Straight.Last();
            Assert.That(last.Start + last.Length, Is.EqualTo(s.FaceHi - 61).Within(1e-6));
            Assert.That(lp.Straight.Select(x => x.Length), Is.EqualTo(new[] { 5500.0, 5250.0 }));
            Assert.That(BeamGeometry.LapsOf(lp.Straight.Select(x => (x.Start, x.Length)).ToList()).All(l => l >= 650 - 1e-6));
            var us = UBars(r, lp, 290);
            Assert.That(us.Count, Is.EqualTo(2), "po jednym rozkładzie na koniec");
            foreach (var u in us)
            {
                Assert.That(u.Kind, Is.EqualTo("E"));
                // liczba U-barów = liczba kolumn prętów głównych (korekta użytkownika 2026-10-11 „tyle samo co prętów głównych”)
                Assert.That(u.Count, Is.EqualTo(4), "4 U-bary w kolumnach prętów");
                Assert.That((u.A, u.B, u.C), Is.EqualTo((750.0, 265.0, 750.0)), "A = Ceil25(650 + 90), B = 290 − 24 → 265 (Holbrook 770-265-770)");
                Assert.That(u.Dropped, Is.False);
                Assert.That(u.Overlap, Is.GreaterThanOrEqualTo(650 - 1e-6));
                Assert.That(u.Across0, Is.EqualTo(s.AcrossLo + 65).Within(1e-6), "kolumny prętów: 45 + strzemię H12 + 8");
                Assert.That(u.Spacing, Is.EqualTo(470.0 / 3).Within(1e-6));
            }
            var lo = us.Single(u => u.AtLo);
            Assert.That(lo.Back, Is.EqualTo(s.FaceLo + 45).Within(1e-6), "tył U na otulinie czołowej 45");
            Assert.That(lo.LineStart, Is.EqualTo(lo.Back).Within(1e-6));
            Assert.That(lo.LineLength, Is.EqualTo(750));
            Assert.That(lo.BendAtStart, Is.True);
            var hi = us.Single(u => !u.AtLo);
            Assert.That(hi.Back, Is.EqualTo(s.FaceHi - 45).Within(1e-6));
            Assert.That(hi.LineStart + hi.LineLength, Is.EqualTo(hi.Back).Within(1e-6));
            Assert.That(hi.BendAtStart, Is.False);
            // strzemiona (2026-10-12 „tyle, ile się zmieści”): zakres 10182.5 − 102 = 10080.5 → floor(/200) + 1 = 51, rozstaw 200,
            // reszta 80.5 po równo (Holbrook ręcznie 51)
            var st = BeamGeometry.StirrupLayout(s.LinkLo, s.LinkHi, 200, 51, null, 100, 51);
            Assert.That(st.First, Is.EqualTo(s.FaceLo + 51).Within(1e-6));
            Assert.That(st.Count, Is.EqualTo(52), "2026-10-13: reszta 80.5 + 51 > 80 → +1, rozstaw ≤ 200");
            Assert.That(st.Step, Is.LessThanOrEqualTo(200.0));
        }

        [Test]
        public void K3_Plot539_Corners_LegUBars_DropOnLegY()
        {
            var r = BeamGeometry.Analyze(Zone539, Outline539);
            var top = r.Segments.Single(s => s.AlongX && s.AcrossLo > 50000);
            var left = r.Segments.Single(s => !s.AlongX);
            var bottom = r.Segments.Single(s => s.AlongX && s.AcrossLo < 50000);
            Assert.That(top.LoClose, Is.EqualTo(BeamGeometry.Closure.LBar));
            Assert.That(top.HiClose, Is.EqualTo(BeamGeometry.Closure.UBar));
            Assert.That(left.LoClose, Is.EqualTo(BeamGeometry.Closure.LBar));
            Assert.That(left.HiClose, Is.EqualTo(BeamGeometry.Closure.LBar));
            Assert.That(bottom.HiClose, Is.EqualTo(BeamGeometry.Closure.UBar));
            var lp = Plan(r);
            TestContext.WriteLine(string.Join(" | ", lp.Straight.Select(x => $"{(x.Seg.AlongX ? "X" : "Y")} {x.Start:0.#}+{x.Length:0}")) + " || " + string.Join(" | ", lp.Messages));
            // decyzja 2026-10-12: noga pozioma (X) — pręty proste przed kwadratem narożnika; noga pionowa (Y) — w kwadrat do lica zewnętrznego
            Assert.That(lp.Bars[top].Lo, Is.GreaterThanOrEqualTo(top.OwnLo - 1e-6));
            Assert.That(lp.Bars[left].Lo, Is.LessThan(left.OwnLo - 1.0));
            Assert.That(lp.Bars[left].Hi, Is.GreaterThan(left.OwnHi + 1.0));
            var us = UBars(r, lp, 215);
            TestContext.WriteLine(string.Join(" | ", us));
            Assert.That(us.Count, Is.EqualTo(6), "2 narożniki × 2 nogi + 2 końce");
            Assert.That(us.Count(u => u.Kind == "L"), Is.EqualTo(4));
            Assert.That(us.Count(u => u.Kind == "E"), Is.EqualTo(2));
            // decyzja 2026-10-13: noga Y (lewa) — 1. warstwa, B = 215 − 25 = 190; nogi X — 2. warstwa: koniec (215 − 15) − 25 = 175,
            // narożnik — wewnątrz U-barów nogi Y: 190 − 2·16 w górę do 5 = 155
            Assert.That(us.Where(u => u.Seg == left).Select(u => u.B).Distinct(), Is.EqualTo(new[] { 190.0 }));
            Assert.That(us.Where(u => u.Seg != left && u.Kind == "L").Select(u => u.B).Distinct(), Is.EqualTo(new[] { 155.0 }));
            Assert.That(us.Where(u => u.Seg != left && u.Kind == "E").Select(u => u.B).Distinct(), Is.EqualTo(new[] { 175.0 }));
            // tył U-barów narożnika na licu zewnętrznym narożnika (otulina 45); ramię ≈ szerokość drugiej nogi + zakład
            var tl = us.Single(u => u.Seg == top && u.AtLo);
            Assert.That(tl.Back, Is.EqualTo(top.FaceLo + 45).Within(1e-6));
            Assert.That(tl.A, Is.InRange(1200.0, 1400.0));
            Assert.That(tl.Overlap, Is.GreaterThanOrEqualTo(650 - 1e-6));
            foreach (var u in us)
            {
                Assert.That(u.C, Is.EqualTo(u.A));
                Assert.That(u.A % 25.0, Is.EqualTo(0).Within(1e-9));
                Assert.That(u.Count, Is.EqualTo(4));
            }
            Assert.That(us.Where(u => u.Kind == "E").Select(u => u.A).Distinct(), Is.EqualTo(new[] { 750.0 }));
        }

        [Test]
        public void K3_IntoSquare_Option_StraightBarsToOuterFace()
        {
            var r = BeamGeometry.Analyze(Zone539, Outline539);
            var left = r.Segments.Single(s => !s.AlongX);
            var top = r.Segments.Single(s => s.AlongX && s.AcrossLo > 50000);
            // decyzja 2026-10-12: noga Y zawsze w kwadrat do lica zewnętrznego, noga X przed kwadratem (opcja [31] — obie w kwadrat)
            var (lo0, hi0) = BeamGeometry.StraightRange(left, 45, 61, intoSquare: false);
            Assert.That(lo0, Is.EqualTo(left.FaceLo + 61).Within(1e-6));
            Assert.That(hi0, Is.EqualTo(left.FaceHi - 61).Within(1e-6));
            var (tlo0, _) = BeamGeometry.StraightRange(top, 45, 61, intoSquare: false);
            Assert.That(tlo0, Is.EqualTo(top.OwnLo).Within(1e-6));
            var (tlo1, _) = BeamGeometry.StraightRange(top, 45, 61, intoSquare: true);
            Assert.That(tlo1, Is.EqualTo(top.FaceLo + 61).Within(1e-6));
        }

        [Test]
        public void K3_Plot319_ManualLeg_NoUBarsAtThatCorner_RightCornerAndInternalEnd()
        {
            var r = BeamGeometry.Analyze(Zone319, Outline319);
            var top = r.Segments.Single(s => s.AlongX);
            var right = r.Segments.Single(s => !s.AlongX && s.AcrossLo > 110000);
            Assert.That(top.LoClose, Is.EqualTo(BeamGeometry.Closure.None), "narożnik z nogą „detal ręczny”");
            Assert.That(top.HiClose, Is.EqualTo(BeamGeometry.Closure.LBar));
            Assert.That(right.LoClose, Is.EqualTo(BeamGeometry.Closure.UBar), "koniec wewnętrzny");
            var lp = Plan(r);
            Assert.That(lp.Straight.First(x => x.Seg == top).Start, Is.EqualTo(top.FaceLo + 45).Within(1e-6), "koniec przy odcinku ręcznym: otulina");
            Assert.That(lp.Messages.Count(m => m.Contains("detal ręczny")), Is.EqualTo(1), string.Join(" | ", lp.Messages));
            var msgs = new List<string>();
            var us = UBars(r, lp, 365, msgs);
            TestContext.WriteLine(string.Join(" | ", us) + " || " + string.Join(" | ", msgs));
            Assert.That(us.Count, Is.EqualTo(3), "narożnik L (2 nogi) + koniec wewnętrzny prawej nogi");
            Assert.That(us.All(u => u.Seg != top || !u.AtLo), Is.True, "bez U-barów przy narożniku z odcinkiem ręcznym");
            // decyzja 2026-10-13: noga Y — 1. warstwa (365 − 25 = 340), noga X narożnika — wewnątrz: 340 − 35 = 305
            Assert.That(us.Single(u => u.Seg == right && !u.AtLo).B, Is.EqualTo(340), "noga Y narożnika: 1. warstwa");
            Assert.That(us.Single(u => u.Seg == right && u.AtLo).B, Is.EqualTo(340), "koniec wewnętrzny nogi Y");
            Assert.That(us.Single(u => u.Seg == top).B, Is.EqualTo(305), "noga X narożnika: 340 − 2·16 w górę do 5");
        }

        [Test]
        public void K3_Tee_ContinuousBeamUntouched_IncomingLikeCornerLeg()
        {
            var zone = P(0, 0, 10000, 0, 10000, 600, 5300, 600, 5300, 5000, 4700, 5000, 4700, 600, 0, 600);
            var r = BeamGeometry.Analyze(zone, null);
            var v = r.Segments.Single(s => !s.AlongX);
            var h = r.Segments.Single(s => s.AlongX);
            Assert.That(h.Manual, Is.False, "belka ciągła przez złącze T");
            Assert.That(v.LoClose, Is.EqualTo(BeamGeometry.Closure.Tee));
            var lp = Plan(r);
            // belka ciągła — pręty bez przerwy przez węzeł; dochodząca — pręty do bliższego lica belki ciągłej (y = 600)
            Assert.That(lp.Bars[v].Lo, Is.GreaterThanOrEqualTo(600 - 1e-6));
            Assert.That(lp.Straight.Where(x => x.Seg == h).Min(x => x.Start), Is.EqualTo(61).Within(1e-6));
            var us = UBars(r, lp, 290);
            TestContext.WriteLine(string.Join(" | ", us));
            var t = us.Single(u => u.Kind == "T");
            Assert.That(t.Seg, Is.SameAs(v));
            Assert.That(t.Back, Is.EqualTo(45).Within(1e-6), "tył na dalszym licu belki ciągłej (y = 0) z otuliną");
            Assert.That(t.B, Is.EqualTo(265 - 35), "belka dochodząca — drugi kierunek, B − 35");
            Assert.That(t.Overlap, Is.GreaterThanOrEqualTo(650 - 1e-6));
            Assert.That(us.Count(u => u.Kind == "E"), Is.EqualTo(3), "końce: belka ciągła × 2 + dochodząca");
            Assert.That(us.Any(u => u.Seg == h && u.Kind != "E"), Is.False, "belka ciągła bez U-barów w węźle");
        }

        [Test]
        public void K3_ShortSegment_UBarArmsOverlap_Message()
        {
            // odcinek 1550 (szerokość 600 — jedna para lic): ramiona 775 z obu końców (tył 45) nachodzą o 40
            var r = BeamGeometry.Analyze(P(0, 0, 1550, 0, 1550, 600, 0, 600), null);
            Assert.That(r.Segments.Count, Is.EqualTo(1));
            var msgs = new List<string>();
            var us = UBars(r, Plan(r), 290, msgs);
            TestContext.WriteLine(string.Join(" | ", us) + " || " + string.Join(" | ", msgs));
            Assert.That(msgs.Count(m => m.Contains("z obu końców nachodzą")), Is.EqualTo(1));
            var lp528 = Plan(BeamGeometry.Analyze(Zone528, Outline528));
            var m528 = new List<string>();
            UBars(BeamGeometry.Analyze(Zone528, Outline528), lp528, 290, m528);
            Assert.That(m528.Any(m => m.Contains("nachodzą")), Is.False);
        }

        [Test]
        public void K3_Manual_And_Crossing_NoUBars()
        {
            var shortL = BeamGeometry.Analyze(P(0, 0, 5000, 0, 5000, 600, 600, 600, 600, 1200, 0, 1200), null);
            Assert.That(Plan(shortL).Straight, Is.Empty);
            Assert.That(UBars(shortL, Plan(shortL), 290), Is.Empty);
            var plus = BeamGeometry.Analyze(P(0, 0, 4700, 0, 4700, -4000, 5300, -4000, 5300, 0, 10000, 0, 10000, 600, 5300, 600,
                                              5300, 4600, 4700, 4600, 4700, 600, 0, 600), null);
            Assert.That(plus.Segments.All(s => s.Manual), Is.True);
            Assert.That(UBars(plus, Plan(plus), 290), Is.Empty);
        }

        [Test]
        public void K3_ShapeLength_21_BS8666()
        {
            // BS 8666:2020: 21 — A + B + C − r − 2d (H16: r = 32), w górę do 25 mm: 750-265-750 → 1700 (1701 → 1725?) — liczone
            double l = BarLengthRules.TotalLengthFor("21", new[] { 750.0, 265.0, 750.0 }, 16);
            Assert.That(l, Is.EqualTo(BeamGeometry.Ceil25(750 + 265 + 750 - 32 - 32)));
        }

        [Test]
        public void BeamCodes_DoNotCollideWithSlabCodes()
        {
            foreach (var c in new[] { BeamGeometry.CodeBottom, BeamGeometry.CodeTop, BeamGeometry.CodeLinks, BeamGeometry.CodeSide,
                                      BeamGeometry.CodeNib, BeamGeometry.CodeBoth, BeamGeometry.CodeUBar })
            {
                Assert.That(BeamGeometry.IsBeamCode(c), c);
                Assert.That(new[] { "B1", "B2", "T1", "T2", "B3" }.Any(s => c.StartsWith(s)), Is.False);
            }
            Assert.That(BeamGeometry.IsBeamCode("B1"), Is.False);
            Assert.That(BeamGeometry.IsBeamCode("T2"), Is.False);
        }
    }
}
