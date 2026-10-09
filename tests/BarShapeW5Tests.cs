using NUnit.Framework;
using BricsCadRc.Core;
using System;
using System.Linq;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// W5 (docs/POPRAWKI_WZORCA.md) — poprawki obliczeń BBS: 27 / 28 wg BS 8666:2020 Table 5 (A + B + (C) − 0.5r − d),
    /// zaokrąglenie do 25 mm z tolerancją 1e-9 kroku dla wszystkich kodów, długość cięcia wspólna dla zestawienia
    /// i tonażu (BarLengthRules.CuttingLength), długość ≤ 0 / nieokreślona → InvalidBarLengthException („brak długości”).
    /// </summary>
    [TestFixture]
    public class BarShapeW5Tests
    {
        private static double Len(string code, double d, params double[] v)
            => ShapeCodeLibrary.Get(code)!.CalculateTotalLength(v, d);

        private static double Ceil25(double raw) => Math.Ceiling(raw / 25.0 - 1e-9) * 25.0;

        // ── 1. Kształty 27 i 28: A + B + (C) − 0.5r − d ──────────────────────

        [TestCase("27", 12, 1500.0)]   // 1000 + 300 + 200 − 12 − 12 = 1476 → 1500 (dawniej −2d: 1464 → 1475)
        [TestCase("28", 12, 1500.0)]
        [TestCase("27", 40, 1400.0)]   // 1500 − 70 − 40 = 1390 → 1400 (dawniej 1350)
        [TestCase("28", 40, 1400.0)]
        public void Code27_28_Bs8666_2020(string code, double d, double expected)
            => Assert.That(Len(code, d, 1000, 300, 200), Is.EqualTo(expected));

        [Test]
        public void Code27_28_FormulaForAllDiameters()
        {
            foreach (var code in new[] { "27", "28" })
                foreach (double d in new double[] { 6, 8, 10, 12, 16, 20, 25, 32, 40, 50 })
                    foreach (double c in new double[] { 0, 200 })
                    {
                        double r = BarShape.MinBendRadius(d);
                        double expected = Ceil25(500 + 600 + c - 0.5 * r - d);
                        Assert.That(Len(code, d, 500, 600, c), Is.EqualTo(expected), $"{code} H{d} C={c}");
                    }
        }

        [Test]
        public void Code27_28_StoredLengthSafetyCheck()
        {
            // Bezpiecznik BBS: stare TL (wzór −2d) ≠ nowy wzór → ostrzeżenie
            Assert.That(BarShape.FormulaChangedIn2020("27", 12), Is.True);
            Assert.That(BarShape.FormulaChangedIn2020("28", 12), Is.True);
            Assert.That(BarShape.StoredLengthDiffers("27", 12, false, 1475, 1500), Is.True);
        }

        // ── 2. Zaokrąglenie w górę do 25 mm z tolerancją ─────────────────────

        [Test]
        public void Code12_FloatNoise_NotRoundedUp()
        {
            // 115 + 920 − 0.43·920 − 1.2·12 = 625 (double: 625.0000000000001) → 625, nie 650
            double raw = 115.0 + 920.0 - 0.43 * 920.0 - 1.2 * 12.0;
            Assert.That(raw, Is.GreaterThan(625.0), "przykład musi mieć szum double powyżej 625");
            Assert.That(Len("12", 12, 115, 920, 920), Is.EqualTo(625.0));
        }

        [TestCase(3500.0000000004, 3500.0)]
        [TestCase(3500.01, 3525.0)]   // prawdziwy nadmiar — w górę
        [TestCase(3010.0, 3025.0)]
        public void Code00_Rounding(double a, double expected)
            => Assert.That(Len("00", 12, a), Is.EqualTo(expected));

        [Test]
        public void RealExcess_StillRoundedUp()
        {
            // 11 H12: 500 + 300.5 − 12 − 12 = 776.5 → 800; 21 H10: 500 + 300 + 500 − 20 − 20 = 1260 → 1275
            Assert.That(Len("11", 12, 500, 300.5), Is.EqualTo(800.0));
            Assert.That(Len("21", 10, 500, 300, 500), Is.EqualTo(1275.0));
        }

        // ── 3. Długość cięcia (zestawienie i tonaż RC_COUNT_BBS) ─────────────

        [Test]
        public void CuttingLength_BentBar_FromFormula_NotA()
        {
            // 21 H10 500/300/500: długość cięcia 1275 (masa liczona z niej), nie A = 500
            Assert.That(BarLengthRules.CuttingLength("21", 10, 500, 300, 500, 0, 0), Is.EqualTo(1275.0));
            // nadmiarowe pola D, E nie wchodzą do kształtu 3-parametrowego
            Assert.That(BarLengthRules.CuttingLength("21", 10, 500, 300, 500, 999, 999), Is.EqualTo(1275.0));
        }

        [Test]
        public void CuttingLength_StraightAndUnknown()
        {
            Assert.That(BarLengthRules.CuttingLength("00", 12, 3010, 0, 0, 0, 0), Is.EqualTo(3025.0));
            Assert.That(BarLengthRules.CuttingLength("XX", 12, 1010, 300, 0, 0, 0), Is.EqualTo(1025.0));   // max(A…E)
        }

        [Test]
        public void CuttingLength_Error_PropagatesToCaller()
        {
            Assert.Catch<InvalidOperationException>(() => BarLengthRules.CuttingLength("47", 12, 400, 300, 0, 0, 0));   // 47 bez C
            Assert.Throws<InvalidBarLengthException>(() => BarLengthRules.CuttingLength("21", 12, 0, 0, 0, 0, 0));
        }

        [Test]
        public void CuttingLength_Unknown_ZeroOrNaN_Throws_AndToleratesNoise()
        {
            // Nieznany kod: same zera / ujemne / NaN → „brak długości” (jak znane kształty)
            Assert.Throws<InvalidBarLengthException>(() => BarLengthRules.CuttingLength("XX", 12, 0, 0, 0, 0, 0));
            Assert.Throws<InvalidBarLengthException>(() => BarLengthRules.CuttingLength("XX", 12, -100, -5, 0, -1, 0));
            Assert.Throws<InvalidBarLengthException>(() => BarLengthRules.CuttingLength("XX", 12, double.NaN, 0, 0, 0, 0));
            // Szum z obrysu nad wielokrotnością 25 mm nie podbija o krok
            Assert.That(BarLengthRules.CuttingLength("XX", 12, 1000.0 + 1e-9, 0, 0, 0, 0), Is.EqualTo(1000.0));
        }

        // ── 4. Długość ≤ 0 / NaN / nieskończona → wyjątek ────────────────────

        [TestCase("21", 12, new double[] { 0, 0, 0 })]     // −48 → −25
        [TestCase("11", 40, new double[] { 10, 10 })]      // 20 − 70 − 40 = −90
        [TestCase("00", 12, new double[] { 0 })]           // 0
        [TestCase("00", 12, new double[] { -100 })]
        public void NonPositiveLength_Throws(string code, double d, double[] v)
        {
            var ex = Assert.Throws<InvalidBarLengthException>(() => Len(code, d, v));
            Assert.That(ex!.Message, Does.Contain("≤ 0"));
            Assert.That(ex.Message, Does.Contain("kształtu " + code));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public void NonFiniteLength_Throws(double a)
        {
            var ex = Assert.Throws<InvalidBarLengthException>(() => Len("21", 12, a, 300, 500));
            Assert.That(ex!.Message, Does.Contain("nieokreślona"));
            Assert.Throws<InvalidBarLengthException>(() => Len("00", 12, a));
        }

        [Test]
        public void TotalLengthFor_NonPositive_Throws()
            => Assert.Throws<InvalidBarLengthException>(() => BarLengthRules.TotalLengthFor("21", new double[] { 0, 0, 0, 0, 0 }, 12));

        [Test]
        public void SmallPositiveLength_RoundedTo25()
            => Assert.That(Len("00", 12, 5), Is.EqualTo(25.0));

        [Test]
        public void Validation_NonPositiveLength_Warned()
        {
            var w = BarShapeValidation.Check("21", new double[] { 0, 0, 0, 0, 0 }, 12);
            Assert.That(w.Any(x => x.StartsWith("Brak długości: ") && x.Contains("≤ 0")), Is.True, string.Join(" | ", w));
            var ok = BarShapeValidation.Check("21", new double[] { 500, 300, 500, 0, 0 }, 10);
            Assert.That(ok.Any(x => x.Contains("Brak długości")), Is.False, string.Join(" | ", ok));
        }

        // ── 5. Komunikat 12 przy R = 0 zgodny z obliczeniem ──────────────────

        [Test]
        public void Code12_R0_MessageMatchesFormula()
        {
            // Długość liczona z R = 0: 500 + 300 − 14.4 = 785.6 → 800 (wzór bez zmian)
            Assert.That(Len("12", 12, 500, 300, 0), Is.EqualTo(800.0));
            var w = BarShapeValidation.Check("12", new double[] { 500, 300, 0, 0, 0 }, 12);
            Assert.That(w.Any(x => x.Contains("długość liczona wzorem z R = 0")), Is.True, string.Join(" | ", w));
            Assert.That(w.Any(x => x.Contains("przyjęto minimalny promień")), Is.False, string.Join(" | ", w));
        }
    }
}
