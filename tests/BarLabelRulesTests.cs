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
    
        [Test]
        public void ManualMultiplicity_2x5()
        {
            // decyzja Bartka 2026-10-23: RC_EDIT_LABEL „2x5” → opis „2x5”, BBS liczy 10 (CountDisplay = 10, CountMult = 2)
            Assert.That(BarLabelRules.ParseCountInput("2x5"), Is.EqualTo(((int, int)?)(10, 2)));
            Assert.That(BarLabelRules.ParseCountInput(" 2 X 5 "), Is.EqualTo(((int, int)?)(10, 2)));
            Assert.That(BarLabelRules.ParseCountInput("3*4"), Is.EqualTo(((int, int)?)(12, 3)));
            Assert.That(BarLabelRules.ParseCountInput("10"), Is.EqualTo(((int, int)?)(10, 0)));
            Assert.That(BarLabelRules.ParseCountInput("1x7"), Is.EqualTo(((int, int)?)(7, 0)));
            Assert.That(BarLabelRules.ParseCountInput("2x"), Is.Null);
            Assert.That(BarLabelRules.ParseCountInput("abc"), Is.Null);
            Assert.That(BarLabelRules.ParseCountInput("0"), Is.Null);
            Assert.That(BarLabelRules.CountText("B1", 5, 10, 2), Is.EqualTo("2x5"));
            Assert.That(BarLabelRules.CountText("B1", 10, null, 2), Is.EqualTo("2x5"), "narysowane 10, wpis 2x5");
            Assert.That(BarLabelRules.CountText("B1", 5, 10), Is.EqualTo("10"), "bez krotności — jak dotąd");
        }
}
}
