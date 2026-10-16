using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Pale w przekroju (decyzja użytkownika 2026-10-16): cięcie przecina pal — rzeczywiste położenie; inaczej pal ≤ 4000 od znacznika
    /// — jeden pal z osią 500 od najbliższej krawędzi płyty; opisy kropek nie przecinają linii pala.
    /// </summary>
    [TestFixture]
    public class SectionPileNearTests
    {
        private static SectionPlanner.Input Slab()
        {
            var inp = new SectionPlanner.Input
            {
                Outline = SectionPlannerGoldenTests.Rect(0, 0, 5000, 5000), CutAlongX = true, CutCoord = 2500, S0 = -100, S1 = 5100,
                LookSign = 1, Thickness = 225
            };
            inp.Dists.Add(SectionPlannerGoldenTests.D("H10-11-200 B1", "B1", false, 40, 40, 4920, 4920, 25, 200, 10));
            inp.Dists.Add(SectionPlannerGoldenTests.D("H10-12-200 B2", "B2", true, 40, 40, 4920, 4920, 25, 200, 10));
            return inp;
        }

        [Test]
        public void NearPile_NotCut_DrawnAt500FromNearestEdge()
        {
            var inp = Slab();
            inp.Piles.Add((4300, 1000, 150));               // 1500 od cięcia, bliżej prawej krawędzi
            var r = SectionPlanner.Plan(inp);
            var p = r.Piles.Single();
            Assert.That(p.U, Is.EqualTo(5000 - SectionPlanner.PileNearEdge).Within(1e-6));
            Assert.That(p.Width, Is.EqualTo(300).Within(1e-6));
            Assert.That(p.Top, Is.EqualTo(-225 + SectionPlanner.PileEmbed).Within(1e-6));
            Assert.That(p.Bottom, Is.EqualTo(-225 - SectionPlanner.PileDepth).Within(1e-6));
        }

        [Test]
        public void FarPile_NotDrawn()
        {
            var inp = Slab();
            inp.Piles.Add((4300, -1600, 150));              // 4100 od cięcia
            Assert.That(SectionPlanner.Plan(inp).Piles, Is.Empty);
        }

        [Test]
        public void CutPile_RealPosition_NoExtraNearPile()
        {
            var inp = Slab();
            inp.Piles.Add((2000, 2500, 150));               // przecięty
            inp.Piles.Add((4300, 1000, 150));               // blisko, ale jest już pal przecięty
            var p = SectionPlanner.Plan(inp).Piles.Single();
            Assert.That(p.U, Is.EqualTo(2000).Within(1e-6));
        }

        [Test]
        public void Labels_DoNotOverlapPile()
        {
            var inp = Slab();
            inp.Piles.Add((2000, 2500, 150));
            var r = SectionPlanner.Plan(inp);
            var p = r.Piles.Single();
            foreach (var l in r.Labels)
            {
                double w = Math.Max(1, l.Text.Length) * l.Height * 0.8;
                bool overlapV = l.V < p.Top && l.V + l.Height > p.Bottom;
                // opis nie przecina linii pala (boki i oś); wewnątrz obrysu między liniami — dozwolony (decyzja 2026-10-16)
                foreach (double lu in new[] { p.U - p.Width / 2, p.U, p.U + p.Width / 2 })
                    Assert.That(overlapV && l.U - w / 2 < lu && l.U + w / 2 > lu, Is.False, $"opis {l.Text} u={l.U} v={l.V} na linii pala u={lu}");
            }
        }

        // ── T IN NIB w nibie płyty (decyzja 2026-10-16): spinka UB 03 z kierunku 1 — kropki NA ramieniu, z kierunku 2 — POD ramieniem ──

        private static (SectionPlanner.Result r, SectionPlanner.BarLine ub) NibPlan(string ubCode)
        {
            var inp = SectionPlannerGoldenTests.CaseNib();
            foreach (var d in inp.Dists.Where(x => x.Mark.Contains("H10-03"))) d.LayerCode = ubCode;
            var r = SectionPlanner.Plan(inp);
            var ub = r.Bars.First(b => b.Kind == SectionPlanner.DistKind.NibUB);
            return (r, ub);
        }

        [Test]
        public void TinNib_Direction1_OnTopOfUb03()
        {
            var (r, ub) = NibPlan("B1");
            double top = ub.Path.Max(q => q.V);
            foreach (var d in r.Dots.Where(x => x.TinNib && x.U >= ub.Path.Min(q => q.U) && x.U <= ub.Path.Max(q => q.U)))
                Assert.That(d.V, Is.EqualTo(top + ub.Diameter / 2.0 + d.Diameter / 2.0).Within(1e-6));
        }

        [Test]
        public void TinNib_Direction2_UnderTopLegOfUb03()
        {
            var (r, ub) = NibPlan("B2");
            double top = ub.Path.Max(q => q.V);
            var tin = r.Dots.Where(x => x.TinNib && x.U >= ub.Path.Min(q => q.U) && x.U <= ub.Path.Max(q => q.U)).ToList();
            Assert.That(tin, Is.Not.Empty);
            foreach (var d in tin)
                Assert.That(d.V, Is.EqualTo(top - ub.Diameter / 2.0 - d.Diameter / 2.0).Within(1e-6));
        }
    }
}
