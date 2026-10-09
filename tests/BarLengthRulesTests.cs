using NUnit.Framework;
using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Plan C, etap 3a: E3-U1 (LocalStart pręta-bloku) i E3-U2 (reguły długości przy zmianie końca).
    /// Oczekiwania liczone niezależnie od BarLengthRules (wzór z planu / wzorca TryEndStretch).
    /// </summary>
    [TestFixture]
    public class BarLengthRulesTests
    {
        private const double Tol = 1e-9;

        // ---- E3-U1: LocalStart ----

        [TestCase("00", 3000, 0, 0, 12)]
        [TestCase("11", 1000, 300, 0, 12)]
        [TestCase("21", 500, 300, 500, 10)]
        [TestCase("13", 800, 200, 500, 12)]
        [TestCase("22", 400, 600, 300, 12)]
        public void LocalStart_NotRing_IsMinusFirstLocalPoint(string code, double a, double b, double c, int d)
        {
            var pv = new[] { a, b, c, 0.0, 0.0 };
            var p0 = BarGeometryBuilder.GetLocalPoints(code, pv, d)[0];
            var ls = BarLengthRules.LocalStart(code, pv, d);
            Assert.That(ls.X, Is.EqualTo(-p0.X).Within(Tol));
            Assert.That(ls.Y, Is.EqualTo(-p0.Y).Within(Tol));
            // Oś zaczyna się w (0,0) bloku
            Assert.That(ls.X + p0.X, Is.EqualTo(0).Within(Tol));
            Assert.That(ls.Y + p0.Y, Is.EqualTo(0).Within(Tol));
        }

        [Test]
        public void LocalStart_Shape22_FirstPointAboveOrigin()
        {
            // Kształt 22 (W4d-2, BS 8666:2020): lPts[0] = (0, −A_osi) → LocalStart = (0, A_osi);
            // W4e (wymiary zewnętrzne): A_osi = A − d/2 = 394
            var ls = BarLengthRules.LocalStart("22", new[] { 400.0, 600, 300, 0, 0 }, 12);
            Assert.That(ls.X, Is.EqualTo(0).Within(Tol));
            Assert.That(ls.Y, Is.EqualTo(394).Within(Tol));
        }

        [Test]
        public void LocalStart_Ring75_IsOrigin()
        {
            // Pierścień (W4c: 75): referencja w punkcie wstawienia polilinii, geometria od Origin (lPts[0] ≠ (0,0))
            var pv = new[] { 300.0, 0, 0, 0, 0 };
            var p0 = BarGeometryBuilder.GetLocalPoints("75", pv, 12)[0];
            Assert.That(p0.X, Is.Not.EqualTo(0).Within(Tol), "założenie testu: lPts[0] pierścienia ≠ (0,0)");
            var ls = BarLengthRules.LocalStart("75", pv, 12);
            Assert.That(ls.X, Is.EqualTo(0).Within(Tol));
            Assert.That(ls.Y, Is.EqualTo(0).Within(Tol));
        }

        [Test]
        public void LocalStart_UnknownShape_AsStraight()
        {
            var ls = BarLengthRules.LocalStart("XX", new[] { 2000.0, 0, 0, 0, 0 }, 12);
            Assert.That(ls.X, Is.EqualTo(0).Within(Tol));
            Assert.That(ls.Y, Is.EqualTo(0).Within(Tol));
        }

        // ---- E3-U2: reguły długości ----

        [TestCase(12, 50)]
        [TestCase(10, 50)]
        [TestCase(25, 50)]
        [TestCase(32, 64)]
        [TestCase(40, 80)]
        public void MinSegment_IsMax2dAnd50(int d, double expected)
            => Assert.That(BarLengthRules.MinSegment(d), Is.EqualTo(expected).Within(Tol));

        [TestCase(3502, 3500)]
        [TestCase(3502.5, 3505)]   // połówki od zera
        [TestCase(3197, 3195)]
        [TestCase(3197.5, 3200)]
        [TestCase(3000, 3000)]
        public void RoundToStep_5mm(double raw, double expected)
            => Assert.That(BarLengthRules.RoundToStep(raw), Is.EqualTo(expected).Within(Tol));

        [Test]
        public void Stretched_EndOutward_RoundedTo5()
            => Assert.That(BarLengthRules.StretchedLength(3000, 502, 12), Is.EqualTo(3500).Within(Tol));

        [Test]
        public void Stretched_StartInward_RoundedTo5()
            => Assert.That(BarLengthRules.StretchedLength(3500, -303, 12), Is.EqualTo(3195).Within(Tol));

        [Test]
        public void Stretched_ExactlyMinimum_Accepted()
            => Assert.That(BarLengthRules.StretchedLength(3000, -2950, 12), Is.EqualTo(50).Within(Tol));

        [Test]
        public void Stretched_RoundsUpToMinimum_Accepted()
            // 3000 − 2952.5 = 47.5 → 50 (zaokrąglenie przed sprawdzeniem minimum)
            => Assert.That(BarLengthRules.StretchedLength(3000, -2952.5, 12), Is.EqualTo(50).Within(Tol));

        [Test]
        public void Stretched_BelowMinimum_Rejected()
            => Assert.That(BarLengthRules.StretchedLength(3000, -2953, 12), Is.Null);   // 47 → 45 < 50

        [Test]
        public void Stretched_BelowMinimum_LargeDiameter_Rejected()
        {
            Assert.That(BarLengthRules.StretchedLength(100, -40, 32), Is.Null);           // 60 < 64
            Assert.That(BarLengthRules.StretchedLength(100, -35, 32), Is.EqualTo(65).Within(Tol));
        }

        [Test]
        public void Stretched_NegativeLength_Rejected()
            => Assert.That(BarLengthRules.StretchedLength(3000, -3500, 12), Is.Null);

        [Test]
        public void TotalLength_Straight_EqualsA()
            => Assert.That(BarLengthRules.TotalLengthFor("00", new[] { 3500.0, 0, 0, 0, 0 }, 12), Is.EqualTo(3500).Within(Tol));

        [Test]
        public void TotalLength_99_ARoundedUpTo25()
            // BarShape.CalculateTotalLength: wszystkie kody (od W4g także "00") w górę do 25 mm; 99 = A → 1250
            => Assert.That(BarLengthRules.TotalLengthFor("99", new[] { 1234.0, 0, 0, 0, 0 }, 12), Is.EqualTo(1250).Within(Tol));

        [Test]
        public void TotalLength_Bent_FromShapeFormula()
        {
            // 11: A + B − 0.5r − d, r = 2d = 24 → 1000 + 300 − 12 − 12 = 1276 → zaokrąglenie w górę do 25 → 1300
            Assert.That(BarLengthRules.TotalLengthFor("11", new[] { 1000.0, 300, 0, 0, 0 }, 12), Is.EqualTo(1300).Within(Tol));
        }

        // ---- W1a (#7): ostrzeżenie przy nadpisanej długości całkowitej ----

        [Test]
        public void OverriddenWarning_Text()
            => Assert.That(BarLengthRules.OverriddenTotalLengthWarning("H12-09", 3333.4),
                           Is.EqualTo("[RC AUTO] Pręt H12-09 ma ręcznie nadpisaną długość całkowitą (3333 mm) — nie zmieniona; sprawdź w RC_EDIT_BAR."));
    }
}
