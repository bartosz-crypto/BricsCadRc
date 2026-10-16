using System.Collections.Generic;
using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>PhDetailRules — tytuły detali PH, opisy prętów, numer pręta zbiorczego (decyzja użytkownika 2026-10-16).</summary>
    [TestFixture]
    public class PhDetailRulesTests
    {
        // Teksty z szablonu (rc-template.dxf.gz)
        private const string Ph1 = @"\pxqc;{\L\C4;\H3.125x;INTERNAL PILE CONDITION PH1 EXTRA BARS (N/A)\P}\H2.5x;SCALE 1:25\P\pa0.66667,q*,sm1.1583;\C3;\H0.75x;^IAPPLICABLE FOR PILES N/A";
        private const string Ph2 = @"\pxqc;{\L\C4;\H3.125x;EDGE PILE CONDITION PH2\PEXTRA BARS (N/A)\fArial Narrow|b0|i0|c238|p34;\H0.8x;\P}\Fromans|c238;\H2.5x;SCALE 1:25\P\C3;\H0.75x;^IAPPLICABLE FOR PILES N/A";

        [Test]
        public void Title_BreaksAfterPh_AndCentresAll()
        {
            string s = PhDetailRules.NormalizeTitle(Ph1);
            Assert.That(s, Does.Contain(@"INTERNAL PILE CONDITION PH1\PEXTRA BARS (N/A)"));
            Assert.That(s, Does.Contain(@"\pa0.66667,qc,sm1.1583;"));
            Assert.That(s, Does.Not.Contain("^I"));
            Assert.That(s, Does.StartWith(@"\pxqc;"));
        }

        [Test]
        public void Title_AlreadyBroken_OnlyTabRemoved()
        {
            string s = PhDetailRules.NormalizeTitle(Ph2);
            Assert.That(s, Is.EqualTo(Ph2.Replace("^I", "")));
            Assert.That(PhDetailRules.NormalizeTitle(s), Is.EqualTo(s));   // idempotentne
        }

        [Test]
        public void Title_LeftAligned_BecomesCentred()
        {
            Assert.That(PhDetailRules.NormalizeTitle(@"\pxql;CORNER PILE CONDITION PH9 EXTRA BARS (3No LOCATIONS)"),
                        Is.EqualTo(@"\pxqc;CORNER PILE CONDITION PH9\PEXTRA BARS (3No LOCATIONS)"));
            Assert.That(PhDetailRules.NormalizeTitle("CORNER PILE CONDITION PH3 EXTRA BARS (N/A)"),
                        Is.EqualTo(@"\pxqc;CORNER PILE CONDITION PH3\PEXTRA BARS (N/A)"));
        }

        [Test]
        public void Label_NumberByDiameter()
        {
            var nums = new Dictionary<int, int> { [12] = 125, [16] = 107 };
            Assert.That(PhDetailRules.ReplaceBarNumber("7 H12-113-200 T", nums), Is.EqualTo("7 H12-125-200 T"));
            Assert.That(PhDetailRules.ReplaceBarNumber("7 H16-116-200 T", nums), Is.EqualTo("7 H16-107-200 T"));
            Assert.That(PhDetailRules.ReplaceBarNumber("14 H16-116-100 T", nums), Is.EqualTo("14 H16-107-100 T"));
            Assert.That(PhDetailRules.ReplaceBarNumber(@"{\C4;7 H12-125-200 T}", nums), Is.EqualTo(@"{\C4;7 H12-125-200 T}"));
            // średnica bez pręta zbiorczego — bez zmian
            Assert.That(PhDetailRules.ReplaceBarNumber("7 H20-113-200 T", nums), Is.EqualTo("7 H20-113-200 T"));
            Assert.That(PhDetailRules.IsBarLabel("APPLICABLE FOR PILES N/A"), Is.False);
            Assert.That(PhDetailRules.IsBarLabel("7 H12-113-200 T"), Is.True);
        }

        [Test]
        public void PickNumber_ReusesSameBar_ElseNextTop()
        {
            var bars = new List<(int, string, double, int)>
            {
                (12, "00", 2250.0, 125),   // istniejący H12 L2250 — ten sam numer
                (12, "00", 3000.0, 104),
                (16, "21", 2500.0, 103),   // inny kształt — nie
                (16, "00", 2500.0, 502),   // dawna seria 501+ — nie
            };
            Assert.That(PhDetailRules.PickNumber(12, 2250, bars, 101, 500, () => 106), Is.EqualTo(125));
            Assert.That(PhDetailRules.PickNumber(16, 2500, bars, 101, 500, () => 106), Is.EqualTo(106));
        }
    }
}
