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
            Assert.That(BarLabelRules.CountText(BeamGeometry.CodeSide, 2, 4), Is.EqualTo("4"), "BS — jak dotąd (opis K4)");
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
            Assert.That(BarLabelRules.KeepsMultiplicity(BeamGeometry.CodeUBar), Is.False);
            Assert.That(BarLabelRules.KeepsMultiplicity(BeamGeometry.CodeBottom), Is.False);
            Assert.That(BarLabelRules.KeepsMultiplicity(BeamGeometry.CodeTop), Is.False);
            Assert.That(BarLabelRules.KeepsMultiplicity("UB"), Is.False);
            Assert.That(BarLabelRules.KeepsMultiplicity(null!), Is.False);
        }
    }
}
