using System.Collections.Generic;
using System.Linq;
using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>CantileverRules — wykrywanie wsporników (decyzja Bartka 2026-10-21, etap 1); dane z rysunków PLOT 28, PLOT 13-14, BLOCK C.</summary>
    [TestFixture]
    public class CantileverRulesTests
    {
        private static List<(double X, double Y)> P(params double[] xy)
        {
            var l = new List<(double X, double Y)>();
            for (int i = 0; i + 1 < xy.Length; i += 2) l.Add((xy[i], xy[i + 1]));
            return l;
        }

        [Test]
        public void Plot28_TabFoundAndRemoved()
        {
            // obrys zewnętrzny PLOT 28 (rzut dolny): języczek GB 600 × 788 na dolnej krawędzi y = 8415
            var poly = P(-40802, 8415, -40802, 7627, -40202, 7627, -40202, 8415, -38584, 8415, -38584, 7740, -35782, 7740, -35782, 8415,
                         -35107, 8415, -35107, 14817, -44322, 14817, -44322, 13017, -44997, 13017, -44997, 10215, -44322, 10215, -44322, 8415);
            var tabs = CantileverRules.FindTabs(poly);
            Assert.That(tabs.Count, Is.EqualTo(1), "wycięcie 2802 × 675 (występ) poza zakresem szerokości");
            var u = tabs[0].Shape;
            Assert.That(u.Width, Is.EqualTo(600).Within(1e-6));
            Assert.That(u.Depth, Is.EqualTo(788).Within(1e-6));
            Assert.That(CantileverRules.DirCode(u.Out), Is.EqualTo("-Y"));
            var clean = CantileverRules.RemoveTabs(poly, tabs);
            Assert.That(clean.Count, Is.EqualTo(12));
            Assert.That(clean.Any(q => q.Y < 7700), Is.False, "bez końca języczka");
            Assert.That(clean.Any(q => q.X == -40802 || q.X == -40202), Is.False, "nasady usunięte (krawędź prosta)");
        }

        [Test]
        public void BayWindowAndSmallNotch_NotCantilevers()
        {
            // występ okna 2128 × 788 (PLOT 13-14) i wcięcie 645 × 100 (BLOCK D) — nie wsporniki
            var poly = P(0, 0, 10000, 0, 10000, 5000, 6000, 5000, 6000, 5788, 3872, 5788, 3872, 5000, 2000, 5000, 2000, 5100, 1355, 5100, 1355, 5000, 0, 5000);
            Assert.That(CantileverRules.FindTabs(poly), Is.Empty);
        }

        [Test]
        public void BlockC_ManyTabsInRow()
        {
            var pts = new List<(double X, double Y)> { (0, 0) };
            double x = 1000;
            for (int k = 0; k < 5; k++, x += 3150)
            {
                pts.Add((x, 0)); pts.Add((x, -1525)); pts.Add((x + 600, -1525)); pts.Add((x + 600, 0));
            }
            pts.Add((20000, 0)); pts.Add((20000, 8000)); pts.Add((0, 8000));
            var tabs = CantileverRules.FindTabs(pts);
            Assert.That(tabs.Count, Is.EqualTo(5));
            Assert.That(tabs.All(t => t.Shape.Depth == 1525 && t.Shape.Width == 600), Is.True);
            Assert.That(CantileverRules.RemoveTabs(pts, tabs).Count, Is.EqualTo(4));
        }

        [Test]
        public void Plot13_UPieces_StubAndRoot()
        {
            var stub = CantileverRules.TryU(P(13014, 4087, 13014, 3270, 13614, 3270, 13614, 4087));
            var root = CantileverRules.TryU(P(13014, 4087, 13014, 4822, 13614, 4822, 13614, 4087));
            Assert.That(CantileverRules.InRange(stub), Is.True);
            Assert.That(stub.Depth, Is.EqualTo(817).Within(1e-6));
            Assert.That(CantileverRules.IsRootOf(root, stub), Is.True);
            Assert.That(CantileverRules.IsRootOf(stub, stub), Is.False);
            Assert.That(CantileverRules.TryU(P(0, 0, 0, 100, 600, 100, 600, 50)), Is.Null, "ramiona różnej długości");
        }

        [Test]
        public void Texts()
        {
            Assert.That(CantileverRules.ParseGb("GB-600x300mm deep\nTOC=8.525"), Is.EqualTo((600.0, 300.0)));
            Assert.That(CantileverRules.ParseGb("GB-600x225mm deep"), Is.EqualTo((600.0, 225.0)));
            Assert.That(CantileverRules.ParseGb("375THK"), Is.Null);
            Assert.That(CantileverRules.ParseToc("GB-600x300mm deep TOC=8.525"), Is.EqualTo(8.525).Within(1e-9));
            Assert.That(CantileverRules.ParseToc("NIB TOC=8.575"), Is.Null);
        }
    }
}
