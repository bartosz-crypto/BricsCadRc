using NUnit.Framework;
using BricsCadRc.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Plan W4f (docs/PLAN_W4_BS8666.md) — walidacja minimów BS 8666:2020 (BarShapeValidation.Check): tylko ostrzeżenia.
    /// Table 2 (P, q), Table 5 (wolne końce, pętle 13 / 22 / 33, 12), Table 4 (X między dwoma zagięciami), kl. 8.2.4 (korby),
    /// kl. 7.2 (co 5 mm), kl. 7.9 (krótszy bok ≤ 2750, długość &gt; 12 m), kl. 8.1.4 (wymiar zerowy), kąt domyślny W4d.
    /// </summary>
    [TestFixture]
    public class BarShapeValidationTests
    {
        private static double[] Pv(params double[] v) { var p = new double[5]; Array.Copy(v, p, Math.Min(5, v.Length)); return p; }

        private static IReadOnlyList<string> Check(string code, double d, params double[] v) => BarShapeValidation.Check(code, Pv(v), d);

        private static void AssertHas(IReadOnlyList<string> w, string fragment)
            => Assert.That(w.Any(x => x.Contains(fragment)), Is.True, $"brak „{fragment}” w: {string.Join(" | ", w)}");

        // ── testy z planu ────────────────────────────────────────────────────

        [Test]
        public void Code13_BBelowQ() => AssertHas(Check("13", 12, 500, 80, 300), "B < q (85)");

        [Test]
        public void Code13_BAbove400Plus2d() => AssertHas(Check("13", 12, 500, 430, 500), "B > 400+2d (424)");

        [Test]
        public void Code11_ABelowP() => AssertHas(Check("11", 12, 100, 300), "A < P (125)");

        // ── pręty z AutoRebar / typowe — bez uwag (S-REG, BBS) ──────────────

        [TestCase("21", 12, 700, 140, 700)]      // UB B1 225
        [TestCase("21", 12, 665, 215, 665)]      // UB B1 300
        [TestCase("13", 12, 715, 115, 715)]      // UB B2 225
        [TestCase("21", 12, 675, 190, 675)]      // UB B2 300
        [TestCase("13", 10, 610, 70, 610)]       // UB 03 w nibie (q(10) = 70)
        [TestCase("00", 12, 6000)]
        [TestCase("00", 10, 2500)]
        [TestCase("11", 12, 500, 300)]
        [TestCase("21", 10, 500, 300, 500)]
        public void TypicalBars_NoWarnings(string code, double d, double a, double b = 0, double c = 0)
            => Assert.That(Check(code, d, a, b, c), Is.Empty);

        [Test]
        public void Hat44_And75Ring_NoWarnings()
        {
            Assert.That(Check("44", 12, 500, 400, 500, 400, 300), Is.Empty);
            Assert.That(Check("75", 12, 300, 100), Is.Empty);
        }

        // ── kl. 8.1.4, kąt domyślny, wymiary opcjonalne ──────────────────────

        [Test]
        public void ZeroDimension_Warned_OptionalNot()
        {
            AssertHas(Check("21", 12, 500, 0, 500), "Wymiar B = 0");
            var w51 = Check("51", 12, 400, 300, 0);   // C puste = hak minimalny (opcjonalny)
            Assert.That(w51.Any(x => x.Contains("Wymiar C")), Is.False, string.Join(" | ", w51));
            AssertHas(Check("12", 12, 500, 300, 0), "R = 0 — przyjęto minimalny promień gięcia r = 24");
        }

        [Test]
        public void MissingAngleDimensions_DefaultAngleWarning()
            => AssertHas(Check("15", 12, 500, 0, 300, 0), "przyjęto kąt domyślny 45°");   // W4h: brak B i D

        // ── Table 5 ──────────────────────────────────────────────────────────

        [Test]
        public void Code12_LegsVersusRadius()
            => AssertHas(Check("12", 12, 200, 300, 100), "A < (R + d) + max(5d, 90) (202)");

        [Test]
        public void Code13_LegsVersusLoop()
            => AssertHas(Check("13", 12, 500, 200, 150), "C < B/2 + max(5d, 90) (190)");

        [Test]
        public void Code22_SemicircleAndD()
        {
            var w = Check("22", 12, 400, 600, 80, 100);
            AssertHas(w, "C < q (85)");
            AssertHas(w, "D < P (125)");
            AssertHas(Check("22", 12, 400, 600, 300, 200), "D < C/2 + max(5d, 90) (240)");
        }

        [Test]
        public void Code33_Loop()
        {
            AssertHas(Check("33", 12, 900, 500, 400), "B > 400+2d (424)");
            AssertHas(Check("33", 12, 300, 200, 300), "A < B/2 + C (400)");
        }

        [Test]
        public void Links_HookNotLongerThanSide_PLinks()
        {
            AssertHas(Check("51", 12, 400, 300, 350), "C > min(A, B) (300)");
            AssertHas(Check("63", 12, 300, 200, 100), "C < P (155)");   // strzemiona z hakiem 90° — P „links” (Table 2)
        }

        [Test]
        public void Code47_Hooks180_PGeneralColumn()
        {
            // runda 1, uwaga 4: haki 180° (≥ 150°) → kolumna „general bend”: H12 P = 125 (nie 155)
            var w = Check("47", 12, 300, 200, 130);
            Assert.That(w.Any(x => x.Contains("C < P")), Is.False, string.Join(" | ", w));
            AssertHas(Check("47", 12, 300, 200, 100), "C < P (125)");
        }

        [Test]
        public void EmptyC_EffectiveHookVersusSide()
        {
            // runda 1, uwaga 3: 51 H12 A = B = 120, C puste → C_eff = 156 > 120
            AssertHas(Check("51", 12, 120, 120, 0), "hak minimalny C_eff = 156 mm > min(A, B) (120)");
            Assert.That(Check("51", 12, 400, 300, 0).Any(x => x.Contains("C_eff")), Is.False);
            AssertHas(Check("63", 12, 120, 300, 0), "> A (120)");
        }

        [Test]
        public void Code22_StraightBetweenCornerAndSemicircle_4d()
        {
            // runda 1, uwaga 5: H12 A 125, B 140, C 150, D 165 — prosty odcinek osi między łukiem narożnika a półkolem < 4d (48)
            AssertHas(Check("22", 12, 125, 140, 150, 165), "< 4d (48)");
            Assert.That(Check("22", 12, 400, 600, 150, 300).Any(x => x.Contains("4d")), Is.False);
        }

        [Test]
        public void Code13_D50_QNotApplicable()
            => AssertHas(Check("13", 50, 1500, 500, 1500), "q = N/A");

        // ── Table 4, korby ───────────────────────────────────────────────────

        [TestCase(6, 75)] [TestCase(8, 80)] [TestCase(10, 100)] [TestCase(12, 120)] [TestCase(16, 160)]
        [TestCase(20, 260)] [TestCase(25, 325)] [TestCase(32, 416)] [TestCase(40, 520)] [TestCase(50, 650)]
        public void Table4_X(double d, double x) => Assert.That(BarShapeValidation.BetweenBendsX(d), Is.EqualTo(x).Within(1e-9));

        [Test]
        public void Table4_UBarWidthBelowX() => AssertHas(Check("21", 12, 500, 100, 500), "B < X (120)");

        [Test]
        public void Crank46_OffsetAndSlope()
        {
            var w = Check("46", 12, 200, 100, 400, 200, 20);   // {A,B,C,E,D}: D = 20 < 2d
            AssertHas(w, "D < 2d (24)");
            AssertHas(w, "B < 10d (min. 75) (120)");
        }

        // ── kl. 7.2, 7.9 ─────────────────────────────────────────────────────

        [Test]
        public void NotMultipleOf5() => AssertHas(Check("21", 12, 502, 140, 500), "A = 502 mm — wymiar nie jest wielokrotnością 5 mm");

        [Test]
        public void ShortSideOver2750_AndLengthOver12m()
        {
            AssertHas(Check("21", 12, 3000, 2800, 3000), "krótszy bok 2800");
            AssertHas(Check("00", 12, 12500), "> 12 m");
            Assert.That(Check("00", 12, 12000).Any(x => x.Contains("12 m")), Is.False);
        }

        [Test]
        public void UnknownShapeOrBadDiameter_Empty()
        {
            Assert.That(Check("XX", 12, 1, 2, 3), Is.Empty);
            Assert.That(Check("21", 0, 500, 300, 500), Is.Empty);
            Assert.That(BarShapeValidation.Check("21", null!, 12).Any(x => x.Contains("Wymiar A = 0")), Is.True);
        }
    }
}
