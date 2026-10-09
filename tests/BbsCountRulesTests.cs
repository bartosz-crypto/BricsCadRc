using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// POPRAWKI_WZORCA 26 — RC_COUNT_BBS liczy także pręty AutoRebar / ręczne z wierszy RC_BBS: ta sama długość cięcia
    /// (także nadpisana) i ta sama masa co arkusz BBS (BbsBarRow.MassKg = sztuki × L / 1000 × masa liniowa).
    /// </summary>
    [TestFixture]
    public class BbsCountRulesTests
    {
        [Test]
        public void MassKg_AsBbs()
        {
            // 31 × H10 L = 3025 (siatka dołem) — 31 · 3.025 · 0.616
            Assert.That(BbsCountRules.MassKg(31, 3025, 0.616), Is.EqualTo(31 * 3.025 * 0.616).Within(1e-9));
            Assert.That(BbsCountRules.MassKg(14, double.NaN, 0.888), Is.EqualTo(0.0));
            Assert.That(BbsCountRules.MassKg(0, 2250, 0.888), Is.EqualTo(0.0));
        }

        [TestCase(12, 3, "H12-03")]
        [TestCase(10, 101, "H10-101")]
        [TestCase(16, 502, "H16-502")]
        public void MarkOf(int d, int pos, string expected)
            => Assert.That(BbsCountRules.MarkOf(d, pos), Is.EqualTo(expected));

        [Test]
        public void FromBbs_Straight()
        {
            var r = BbsCountRules.FromBbs(4, 10, "00", false, 3000, 3025, 31, "", 0.616);
            Assert.That(r.Mark, Is.EqualTo("H10-04"));
            Assert.That(r.Position, Is.EqualTo("04"));
            Assert.That(r.ShapeCode, Is.EqualTo("00"));
            Assert.That(r.LayerCode, Is.EqualTo("BOTTOM"));
            Assert.That(r.LengthA, Is.EqualTo(3000));
            Assert.That(r.CuttingLength, Is.EqualTo(3025));
            Assert.That(r.BarLinesInDrawing, Is.EqualTo(31));
            Assert.That(r.TotalMassKg, Is.EqualTo(31 * 3.025 * 0.616).Within(1e-9));
            Assert.That(r.LengthError, Is.Null);
            Assert.That(r.Note, Is.Null);
        }

        [Test]
        public void FromBbs_OverriddenLength_TopLayer()
        {
            // Długość nadpisana (LengthOverridden / TotalLength) przychodzi z BBS jako Length + uwaga
            var r = BbsCountRules.FromBbs(101, 12, "21", true, 700, 1640, 20, "długość nadpisana", 0.888);
            Assert.That(r.LayerCode, Is.EqualTo("TOP"));
            Assert.That(r.CuttingLength, Is.EqualTo(1640));
            Assert.That(r.TotalMassKg, Is.EqualTo(20 * 1.640 * 0.888).Within(1e-9));
            Assert.That(r.Note, Is.EqualTo("długość nadpisana"));
            Assert.That(r.LengthError, Is.Null);
        }

        [Test]
        public void FromBbs_NoLength_ZeroMassAndError()
        {
            var r = BbsCountRules.FromBbs(7, 12, "47", false, double.NaN, double.NaN, 5, "brak długości", 0.888);
            Assert.That(r.TotalMassKg, Is.EqualTo(0.0));
            Assert.That(r.LengthError, Is.EqualTo("brak długości"));
            Assert.That(r.LengthA, Is.EqualTo(0.0));
            var r2 = BbsCountRules.FromBbs(7, 12, "", false, 100, double.NaN, 5, "", 0.888);
            Assert.That(r2.ShapeCode, Is.EqualTo("00"));
            Assert.That(r2.LengthError, Does.Contain("brak długości"));
        }
    }
}
