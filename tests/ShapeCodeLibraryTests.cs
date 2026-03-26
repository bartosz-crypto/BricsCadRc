using NUnit.Framework;
using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    [TestFixture]
    public class ShapeCodeLibraryTests
    {
        // ── MinBendRadius ────────────────────────────────────────────────────

        [TestCase(8,  16.0)]    // 2d, d<=16
        [TestCase(12, 24.0)]    // 2d, d<=16
        [TestCase(16, 32.0)]    // 2d, d=16 (granica)
        [TestCase(20, 70.0)]    // 3.5d, d<=25
        [TestCase(25, 87.5)]    // 3.5d, d=25 (granica)
        [TestCase(26, 104.0)]   // 4d, d>25
        [TestCase(32, 128.0)]   // 4d, d>25
        public void MinBendRadius_ReturnsCorrectValue(double d, double expected)
        {
            Assert.That(BarShape.MinBendRadius(d), Is.EqualTo(expected).Within(1e-9));
        }

        // ── Get / Contains / rejestr ─────────────────────────────────────────

        [TestCase("00")] [TestCase("11")] [TestCase("12")] [TestCase("13")]
        [TestCase("14")] [TestCase("15")] [TestCase("21")] [TestCase("22")]
        [TestCase("23")] [TestCase("24")] [TestCase("25")] [TestCase("26")]
        [TestCase("27")] [TestCase("28")] [TestCase("29")] [TestCase("31")]
        [TestCase("32")] [TestCase("33")] [TestCase("34")] [TestCase("35")]
        [TestCase("36")] [TestCase("41")] [TestCase("44")] [TestCase("46")]
        [TestCase("47")] [TestCase("51")] [TestCase("56")] [TestCase("63")]
        [TestCase("64")] [TestCase("75")] [TestCase("98")] [TestCase("99")]
        public void Get_AllCodes_ReturnNonNull(string code)
        {
            var shape = ShapeCodeLibrary.Get(code);
            Assert.That(shape, Is.Not.Null);
            Assert.That(shape!.Code, Is.EqualTo(code));
        }

        [Test]
        public void GetAll_Returns32Shapes()
        {
            int count = 0;
            foreach (var _ in ShapeCodeLibrary.GetAll()) count++;
            Assert.That(count, Is.EqualTo(32));
        }

        [Test]
        public void Get_NullCode_ReturnsNull()
        {
            Assert.That(ShapeCodeLibrary.Get(null!), Is.Null);
        }

        [Test]
        public void Get_UnknownCode_ReturnsNull()
        {
            Assert.That(ShapeCodeLibrary.Get("XX"), Is.Null);
        }

        [Test]
        public void Contains_NullCode_ReturnsFalse()
        {
            Assert.That(ShapeCodeLibrary.Contains(null!), Is.False);
        }

        // ── CalculateTotalLength — walidacja ─────────────────────────────────

        [Test]
        public void CalculateTotalLength_ZeroDiameter_ThrowsArgumentException()
        {
            var shape = ShapeCodeLibrary.Get("00")!;
            Assert.That(() => shape.CalculateTotalLength(new[] { 1000.0 }, 0),
                Throws.ArgumentException);
        }

        [Test]
        public void CalculateTotalLength_NegativeDiameter_ThrowsArgumentException()
        {
            var shape = ShapeCodeLibrary.Get("11")!;
            Assert.That(() => shape.CalculateTotalLength(new[] { 300.0, 200.0 }, -12),
                Throws.ArgumentException);
        }

        [Test]
        public void CalculateTotalLength_WrongParamCount_ThrowsArgumentException()
        {
            var shape = ShapeCodeLibrary.Get("11")!;  // oczekuje 2 parametrów
            Assert.That(() => shape.CalculateTotalLength(new[] { 300.0 }, 12),
                Throws.ArgumentException);
        }

        [Test]
        public void CalculateTotalLength_NullParams_ThrowsArgumentException()
        {
            var shape = ShapeCodeLibrary.Get("00")!;
            Assert.That(() => shape.CalculateTotalLength(null!, 12),
                Throws.ArgumentException);
        }

        // ── CalculateTotalLength — wartości ──────────────────────────────────

        [Test]
        public void Code00_Straight_ReturnsA()
        {
            // A=1000, d=12 → 1000.0
            var result = ShapeCodeLibrary.Get("00")!.CalculateTotalLength(new[] { 1000.0 }, 12);
            Assert.That(result, Is.EqualTo(1000.0).Within(1e-9));
        }

        [Test]
        public void Code11_LShape_Formula()
        {
            // A=300, B=200, d=12 → r=24 → raw=300+200-12-12=476 → CEILING(476/25)*25=500
            var result = ShapeCodeLibrary.Get("11")!.CalculateTotalLength(new[] { 300.0, 200.0 }, 12);
            Assert.That(result, Is.EqualTo(500.0).Within(1e-9));
        }

        [Test]
        public void Code21_UBar_Formula()
        {
            // A=300, B=150, C=300, d=12 → r=24 → raw=702 → CEILING(702/25)*25=725
            var result = ShapeCodeLibrary.Get("21")!.CalculateTotalLength(new[] { 300.0, 150.0, 300.0 }, 12);
            Assert.That(result, Is.EqualTo(725.0).Within(1e-9));
        }

        [Test]
        public void Code21_UBar_RoundingExample_BS8666()
        {
            // Przykład z kalkulatora: A=665, B=215, C=665, d=12 → r=24 → raw=1497 → 1500
            var result = ShapeCodeLibrary.Get("21")!.CalculateTotalLength(new[] { 665.0, 215.0, 665.0 }, 12);
            Assert.That(result, Is.EqualTo(1500.0).Within(1e-9));
        }

        [Test]
        public void Code22_ZBar_Formula()
        {
            // A=300, B=150, C=200, D=100, d=12 → r=24 → raw=750-36-36=678 → CEILING(678/25)*25=700
            var result = ShapeCodeLibrary.Get("22")!.CalculateTotalLength(new[] { 300.0, 150.0, 200.0, 100.0 }, 12);
            Assert.That(result, Is.EqualTo(700.0).Within(1e-9));
        }

        [Test]
        public void Code36_ClosedShape_Formula()
        {
            // A=300, B=200, C=300, D=200, d=12 → r=24 → raw=1000-24-24=952 → CEILING(952/25)*25=975
            var result = ShapeCodeLibrary.Get("36")!.CalculateTotalLength(new[] { 300.0, 200.0, 300.0, 200.0 }, 12);
            Assert.That(result, Is.EqualTo(975.0).Within(1e-9));
        }

        [Test]
        public void Code41_ClosedQuadrilateral_Formula()
        {
            // A=300, B=200, C=300, D=200, E=150, d=12 → r=24 → raw=1150-48-48=1054 → CEILING(1054/25)*25=1075
            var result = ShapeCodeLibrary.Get("41")!.CalculateTotalLength(new[] { 300.0, 200.0, 300.0, 200.0, 150.0 }, 12);
            Assert.That(result, Is.EqualTo(1075.0).Within(1e-9));
        }

        [Test]
        public void Code34_ClosedRectangle_Formula()
        {
            // A=500, B=400, C=500, E=400, d=12 → r=24 → raw=1776 → CEILING(1776/25)*25=1800
            var result = ShapeCodeLibrary.Get("34")!.CalculateTotalLength(new[] { 500.0, 400.0, 500.0, 400.0 }, 12);
            Assert.That(result, Is.EqualTo(1800.0).Within(1e-9));
        }

        [Test]
        public void Code35_ClosedSquare_Formula()
        {
            // A=500, B=400, C=500, E=400, d=12 → raw=1776 → 1800
            var result = ShapeCodeLibrary.Get("35")!.CalculateTotalLength(new[] { 500.0, 400.0, 500.0, 400.0 }, 12);
            Assert.That(result, Is.EqualTo(1800.0).Within(1e-9));
        }

        [Test]
        public void Code44_Circle_Formula()
        {
            // A=500, B=400, C=500, D=400, E=300, d=12 → r=24 → raw=2100-48-48=2004 → CEILING(2004/25)*25=2025
            var result = ShapeCodeLibrary.Get("44")!.CalculateTotalLength(new[] { 500.0, 400.0, 500.0, 400.0, 300.0 }, 12);
            Assert.That(result, Is.EqualTo(2025.0).Within(1e-9));
        }

        [Test]
        public void Code75_Helix_Formula()
        {
            // A=100, B=500, d=12 → raw=π*(100-12)+500=π*88+500≈776.46 → CEILING(776.46/25)*25=800
            var result = ShapeCodeLibrary.Get("75")!.CalculateTotalLength(new[] { 100.0, 500.0 }, 12);
            Assert.That(result, Is.EqualTo(800.0).Within(1e-9));
        }

        [Test]
        public void Code12_HookBothEnds_Formula()
        {
            // A=300, B=200, R=60, d=12 → raw=459.8 → CEILING(459.8/25)*25=475
            var result = ShapeCodeLibrary.Get("12")!.CalculateTotalLength(new[] { 300.0, 200.0, 60.0 }, 12);
            Assert.That(result, Is.EqualTo(475.0).Within(1e-9));
        }

        [Test]
        public void Code13_Hook180_Formula()
        {
            // A=300, B=150, C=200, d=12 → raw=566.3 → CEILING(566.3/25)*25=575
            var result = ShapeCodeLibrary.Get("13")!.CalculateTotalLength(new[] { 300.0, 150.0, 200.0 }, 12);
            Assert.That(result, Is.EqualTo(575.0).Within(1e-9));
        }

        [Test]
        public void Code14_Hook45_Formula()
        {
            // A=400, B=100, C=300, d=12 → raw=652 → CEILING(652/25)*25=675
            var result = ShapeCodeLibrary.Get("14")!.CalculateTotalLength(new[] { 400.0, 100.0, 300.0 }, 12);
            Assert.That(result, Is.EqualTo(675.0).Within(1e-9));
        }

        [Test]
        public void Code15_Hook135_Formula()
        {
            // A=400, B=100, C=300, d=12 → raw=700 → already multiple of 25 → 700
            var result = ShapeCodeLibrary.Get("15")!.CalculateTotalLength(new[] { 400.0, 100.0, 300.0 }, 12);
            Assert.That(result, Is.EqualTo(700.0).Within(1e-9));
        }

        [Test]
        public void Code24_Crank_Formula()
        {
            // A=300, B=200, C=400, d=12 → raw=900 → already multiple of 25 → 900
            var result = ShapeCodeLibrary.Get("24")!.CalculateTotalLength(new[] { 300.0, 200.0, 400.0 }, 12);
            Assert.That(result, Is.EqualTo(900.0).Within(1e-9));
        }

        [Test]
        public void Code25_HookCrank_Formula()
        {
            // A=300, B=200, E=250, d=12 → raw=300+200+250=750 → already multiple of 25 → 750
            var result = ShapeCodeLibrary.Get("25")!.CalculateTotalLength(new[] { 300.0, 200.0, 250.0 }, 12);
            Assert.That(result, Is.EqualTo(750.0).Within(1e-9));
        }

        [Test]
        public void Code26_HookLeg_Formula()
        {
            // A=300, B=200, C=400, d=12 → raw=900 → already multiple of 25 → 900
            var result = ShapeCodeLibrary.Get("26")!.CalculateTotalLength(new[] { 300.0, 200.0, 400.0 }, 12);
            Assert.That(result, Is.EqualTo(900.0).Within(1e-9));
        }

        [Test]
        public void Code33_4LegVariant_Formula()
        {
            // A=400, B=200, C=400, d=12 → raw=1892 → CEILING(1892/25)*25=1900
            var result = ShapeCodeLibrary.Get("33")!.CalculateTotalLength(new[] { 400.0, 200.0, 400.0 }, 12);
            Assert.That(result, Is.EqualTo(1900.0).Within(1e-9));
        }

        [Test]
        public void Code46_ClosedRectVariant_Formula()
        {
            // A=400, B=200, C=400, E=100, d=12 → raw=1300 → already multiple of 25 → 1300
            var result = ShapeCodeLibrary.Get("46")!.CalculateTotalLength(new[] { 400.0, 200.0, 400.0, 100.0 }, 12);
            Assert.That(result, Is.EqualTo(1300.0).Within(1e-9));
        }

        [Test]
        public void Code47_ClosedTriangle_Formula_MaxFrom21d()
        {
            // A=300, B=200, d=12 → max(252,240)=252 → raw=1052 → CEILING(1052/25)*25=1075
            var result = ShapeCodeLibrary.Get("47")!.CalculateTotalLength(new[] { 300.0, 200.0 }, 12);
            Assert.That(result, Is.EqualTo(1075.0).Within(1e-9));
        }

        [Test]
        public void Code47_ClosedTriangle_Formula_MaxFrom240()
        {
            // A=300, B=200, d=10 → max(210,240)=240 → raw=1040 → CEILING(1040/25)*25=1050
            var result = ShapeCodeLibrary.Get("47")!.CalculateTotalLength(new[] { 300.0, 200.0 }, 10);
            Assert.That(result, Is.EqualTo(1050.0).Within(1e-9));
        }

        [Test]
        public void Code51_ClosedStirrup_Formula_MaxFrom16d()
        {
            // A=400, B=300, C=0, d=12 → hook=192, r=24 → 2(892)-60-60=1664 → CEIL=1675
            var result = ShapeCodeLibrary.Get("51")!.CalculateTotalLength(new[] { 400.0, 300.0, 0.0 }, 12);
            Assert.That(result, Is.EqualTo(1675.0).Within(1e-9));
        }

        [Test]
        public void Code51_ClosedStirrup_Formula_MaxFrom160()
        {
            // A=400, B=300, C=0, d=8 → hook=160, r=16 → 2(860)-40-40=1640 → CEIL=1650
            var result = ShapeCodeLibrary.Get("51")!.CalculateTotalLength(new[] { 400.0, 300.0, 0.0 }, 8);
            Assert.That(result, Is.EqualTo(1650.0).Within(1e-9));
        }

        [Test]
        public void Code51_ExplicitHook_UsesC()
        {
            // A=400, B=300, C=210, d=12 → hook=210, r=24 → 2(910)-60-60=1700 → CEIL=1700
            var result = ShapeCodeLibrary.Get("51")!.CalculateTotalLength(new[] { 400.0, 300.0, 210.0 }, 12);
            Assert.That(result, Is.EqualTo(1700.0).Within(1e-9));
        }

        [Test]
        public void Code63_ClosedStirrup_Formula_MaxFrom14d()
        {
            // A=300, B=200, C=0, d=12: hook=168, r=24
            // raw = 2*300 + 3*200 + 2*168 - 3*24 - 6*12 = 600+600+336-72-72 = 1392 → CEIL=1400
            var result = ShapeCodeLibrary.Get("63")!.CalculateTotalLength(new[] { 300.0, 200.0, 0.0 }, 12);
            Assert.That(result, Is.EqualTo(1400.0).Within(1e-9));
        }

        [Test]
        public void Code63_ClosedStirrup_Formula_MaxFrom150()
        {
            // A=300, B=200, C=0, d=8: hook=max(112,150)=150, r=16
            // raw = 2*300 + 3*200 + 2*150 - 3*16 - 6*8 = 600+600+300-48-48 = 1404 → CEIL=1425
            var result = ShapeCodeLibrary.Get("63")!.CalculateTotalLength(new[] { 300.0, 200.0, 0.0 }, 8);
            Assert.That(result, Is.EqualTo(1425.0).Within(1e-9));
        }

        [Test]
        public void Code63_ClosedStirrup_ExplicitHook_UsesC()
        {
            // A=300, B=200, C=180, d=12: r=24
            // raw = 2*300 + 3*200 + 2*180 - 3*24 - 6*12 = 600+600+360-72-72 = 1416 → CEIL=1425
            var result = ShapeCodeLibrary.Get("63")!.CalculateTotalLength(new[] { 300.0, 200.0, 180.0 }, 12);
            Assert.That(result, Is.EqualTo(1425.0).Within(1e-9));
        }

        [Test]
        public void Code98_Special_Formula()
        {
            // A=300, B=200, C=400, D=150, d=12 → r=24 → raw=1154 → CEILING(1154/25)*25=1175
            var result = ShapeCodeLibrary.Get("98")!.CalculateTotalLength(new[] { 300.0, 200.0, 400.0, 150.0 }, 12);
            Assert.That(result, Is.EqualTo(1175.0).Within(1e-9));
        }

        // ── SvgPreview nie jest pusty ─────────────────────────────────────────

        [TestCase("00")] [TestCase("21")] [TestCase("44")] [TestCase("75")]
        public void SvgPreview_IsNotNullOrEmpty(string code)
        {
            Assert.That(ShapeCodeLibrary.Get(code)!.SvgPreview, Is.Not.Null.And.Not.Empty);
        }
    }
}
