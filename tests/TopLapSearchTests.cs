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
        public void Width6503_TwoBarsNotThree()
        {
            // uwaga Bartka 2026-10-26 (PLOT 357): okno zakładu 500–700 węższe niż siatka 250 — wcześniej 3 pręty
            double available = 6503;
            var p = TopLapSearch.Find(available, new List<(double, double)>(), new List<(double, double)>(), 2, 4, 1250, 6000, 250, 500, 700, 600, 750);
            Assert.That(p, Is.Not.Null);
            Assert.That(p.Count, Is.EqualTo(2));
            Assert.That(p[1].xOffset + p[1].length, Is.EqualTo(available).Within(1e-6));
            Assert.That(p[0].length % 250, Is.EqualTo(0).Within(1e-6));
            Assert.That(p[1].length % 25, Is.EqualTo(0).Within(1e-6));
            Assert.That(p[0].length - p[1].xOffset, Is.InRange(500 - 1e-6, 700 + 1e-6));
            Assert.That(p[1].length, Is.GreaterThanOrEqualTo(1250));
        }

        [Test]
        public void NoRoomBetweenPiles_ReturnsNull()
        {
            var forbidden = new List<(double, double)> { (3000, 16000) };
            Assert.That(TopLapSearch.Find(17000, new List<(double, double)>(), forbidden, 3, 7, 1250, 6000, 250, 500, 700, 600, 750), Is.Null);
        }

        [Test]
        public void TopFirst_Plot372_PilesEvery2250()
        {
            // Drawing2.dwg (2026-10-20): pas T1 18765 (od x 45295), dół 19035 (nib 135), pale Ø300 przecinające pas
            double t0 = 45295, topAvail = 18765, avail = 19035, inset = 135;
            var px = new[] { 45770.0, 46170, 46235, 47759, 50181, 52429, 54677, 56926, 57238, 59175, 61596, 62910, 63585 };
            var forbidden = px.Select(x => (x - 650 - t0, x + 650 - t0)).ToList();
            var r = TopLapSearch.TopFirst(avail, topAvail, inset, forbidden, 4, 4, new[] { 2500.0, 1250 }, 6000, 250,
                                          400, 650, 500, 500, 700, 600, 750);
            Assert.That(r, Is.Not.Null);
            var top = r.Value.top; var bot = r.Value.bottom;
            AssertValid(top, topAvail, forbidden, new List<(double, double)>());
            Assert.That(bot[bot.Count - 1].xOffset + bot[bot.Count - 1].length, Is.EqualTo(avail).Within(1e-6));
            for (int i = 0; i + 1 < bot.Count; i++)
            {
                double lo = bot[i + 1].xOffset, hi = bot[i].xOffset + bot[i].length;
                Assert.That(hi - lo, Is.InRange(400 - 1e-6, 650 + 1e-6), "zakład dołu");
                for (int j = 0; j + 1 < top.Count; j++)
                {
                    double tlo = top[j + 1].xOffset + inset, thi = top[j].xOffset + top[j].length + inset;
                    Assert.That(Math.Max(lo - thi, tlo - hi), Is.GreaterThanOrEqualTo(750 - 1e-6), "dół przy zakładzie góry");
                }
            }
            // góra liczona potem względem RZECZYWISTEGO dołu (PlanTopLine → Find) — musi znaleźć układ
            var bz = new List<(double lo, double hi)>();
            for (int i = 0; i + 1 < bot.Count; i++) bz.Add((bot[i + 1].xOffset - inset, bot[i].xOffset + bot[i].length - inset));
            var again = TopLapSearch.Find(topAvail, bz, forbidden, 4, 8, 2500, 6000, 250, 500, 700, 600, 750)
                     ?? TopLapSearch.Find(topAvail, bz, forbidden, 4, 8, 1250, 6000, 250, 500, 700, 600, 750);
            AssertValid(again, topAvail, forbidden, bz);
            TestContext.WriteLine("góra " + string.Join("/", top.Select(t => t.length)) + "  dół " + string.Join("/", bot.Select(b => b.length)));
        }
    }
}
