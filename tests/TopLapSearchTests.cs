using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>TopLapSearch — zakłady góry omijające pale (decyzja 2026-10-15).</summary>
    [TestFixture]
    public class TopLapSearchTests
    {
        private static void AssertValid(List<(double xOffset, double length)> p, double available,
                                        List<(double lo, double hi)> forbidden, List<(double lo, double hi)> bottom)
        {
            Assert.That(p, Is.Not.Null);
            Assert.That(p[0].xOffset, Is.EqualTo(0).Within(1e-6));
            Assert.That(p[p.Count - 1].xOffset + p[p.Count - 1].length, Is.EqualTo(available).Within(1e-6));
            foreach (var b in p)
            {
                Assert.That(b.length, Is.InRange(1250 - 1e-6, 6000 + 1e-6));
                Assert.That(b.length % 250, Is.EqualTo(0).Within(1e-6));
            }
            for (int i = 0; i + 1 < p.Count; i++)
            {
                double lo = p[i + 1].xOffset, hi = p[i].xOffset + p[i].length;
                Assert.That(hi - lo, Is.InRange(500 - 1e-6, 700 + 1e-6), "zakład");
                foreach (var f in forbidden) Assert.That(Math.Min(hi, f.hi) - Math.Max(lo, f.lo), Is.LessThan(0), "zakład na palu");
                foreach (var b in bottom) Assert.That(Math.Max(lo - b.hi, b.lo - hi), Is.GreaterThanOrEqualTo(750 - 1e-6), "zakład przy dole");
            }
        }

        [Test]
        public void DensePiles_FindsLapsBetweenPiles()
        {
            double available = 16290;
            // pale Ø300 co ok. 1,2–3,7 m (strefa = lico ± 500 → 1300 mm), zakłady dołu jak w płycie (pręty 6000/5500/6000, zakład 470)
            var piles = new[] { 1650.0, 2400, 3200, 6000, 9750, 11000, 13900, 15200 };
            var forbidden = piles.Select(c => (c - 650, c + 650)).ToList();
            var bottom = new List<(double, double)> { (5530, 6000), (11030, 11500) };
            var p = TopLapSearch.Find(available, bottom, forbidden, 3, 7, 1250, 6000, 250, 500, 700, 600, 750);
            AssertValid(p, available, forbidden, bottom);
        }

        [Test]
        public void NoRoomBetweenPiles_ReturnsNull()
        {
            var forbidden = new List<(double, double)> { (3000, 16000) };
            Assert.That(TopLapSearch.Find(17000, new List<(double, double)>(), forbidden, 3, 7, 1250, 6000, 250, 500, 700, 600, 750), Is.Null);
        }
    }
}
