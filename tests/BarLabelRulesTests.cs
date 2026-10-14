using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// BarLabelRules — liczba prętów w opisie rozkładu (K3, PLAN_BELKI_PRAKTYKA): BM „2x4 … B+T”, pozostałe kody
    /// EffectiveCount (CountDisplay ?? Count); krotność zachowywana po gripie tylko dla BM i BS.
    /// </summary>
    [TestFixture]
    public class BarLabelRulesTests
    {
        [Test]
        public void CountText_BothOneMark_KxCount()
        {
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeBoth, 4, 8), Is.EqualTo("2x4"));
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeBoth, 3, 9), Is.EqualTo("3x3"));
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeBoth, 5, 10), Is.EqualTo("2x5"));
        }

        /// <summary>Decyzja użytkownika 2026-10-11: pręty w nibie jedną pozycją — w rzucie 2 pręty górą, CountDisplay 3 → „2+1 H10-nn T+B IN NIB”.</summary>
        [Test]
        public void CountText_NibPair_TwoPlusOne()
        {
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeNib, 2, 3), Is.EqualTo("2+1"));
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeNib, 1, null), Is.EqualTo("1"), "BN górą / stare B IN NIB — liczba");
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeNib, 2, 2), Is.EqualTo("2"));
        }

        [Test]
        public void CountText_BothOneMark_NoMultiplicity_PlainNumber()
        {
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeBoth, 4, null), Is.EqualTo("4"));
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeBoth, 4, 4), Is.EqualTo("4"), "krotność 1 — liczba");
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeBoth, 4, 6), Is.EqualTo("6"), "krotność niecałkowita — liczba");
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeBoth, 0, 8), Is.EqualTo("8"), "Count = 0 — bez dzielenia");
        }

        [Test]
        public void CountText_OtherCodes_EffectiveCount()
        {
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeSide, 2, 4), Is.EqualTo("2x2"), "K4: BS „2x{n}” (boki × liczba na bok)");
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeSide, 2, 2), Is.EqualTo("2x1"), "K4: także n = 1 (C12 „2x1 H12-13 SIDE”)");
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeSide, 2, null), Is.EqualTo("2"));
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeSideU, 1, 3), Is.EqualTo("3"), "UB SIDE jako Any (2026-10-14): „{n}”");
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeSideU, 1, null), Is.EqualTo("1"));
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeUBar, 4, null), Is.EqualTo("4"));
            Assert.That(BarLabelRules.CountText("B1", 31, null), Is.EqualTo("31"));
            Assert.That(BarLabelRules.CountText("B1", 31, 33), Is.EqualTo("33"));
            Assert.That(BarLabelRules.CountText(null!, 7, null), Is.EqualTo("7"));
        }

        [Test]
        public void KeepsMultiplicity_OnlyBothAndSide()
        {
            Assert.That(BarLabelRules.KeepsMultiplicity(BeamGeometry.CodeBoth), Is.True);
            Assert.That(BarLabelRules.KeepsMultiplicity(BeamGeometry.CodeSide), Is.True);
            Assert.That(BarLabelRules.KeepsMultiplicity(BeamGeometry.CodeSideU), Is.True);
            Assert.That(BarLabelRules.KeepsMultiplicity(BeamGeometry.CodeUBar), Is.False);
            Assert.That(BarLabelRules.KeepsMultiplicity(BeamGeometry.CodeBottom), Is.False);
            Assert.That(BarLabelRules.KeepsMultiplicity(BeamGeometry.CodeTop), Is.False);
            Assert.That(BarLabelRules.KeepsMultiplicity("UB"), Is.False);
            Assert.That(BarLabelRules.KeepsMultiplicity(null!), Is.False);
        }
    }
}
