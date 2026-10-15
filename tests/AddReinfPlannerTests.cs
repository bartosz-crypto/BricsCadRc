using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>AddReinfPlanner — decyzja 2026-10-15: ADD min. 5 prętów, przy brzegu płyty rozkład przesuwany do środka.</summary>
    [TestFixture]
    public class AddReinfPlannerTests
    {
        private static readonly List<(double x, double y)> Slab = new List<(double, double)>
            { (0, 0), (10000, 0), (10000, 8000), (0, 8000) };

        private static AddReinfPlanner.Zone Zone(double c0, double c1) => new AddReinfPlanner.Zone
        {
            RectA0 = 3000, RectA1 = 5000, RectC0 = c0, RectC1 = c1,
            ContA0 = double.NaN, ContA1 = double.NaN, ContC0 = double.NaN, ContC1 = double.NaN,
            Value = 600, Label = "#1"
        };

        [Test]
        public void NarrowZone_AtLeastFiveBars_Centred()
        {
            var d = AddReinfPlanner.Plan(new List<AddReinfPlanner.Zone> { Zone(4000, 4200) }, true, Slab,
                                         new List<(double, double)>()).Single();
            Assert.That(d.Count, Is.EqualTo(AddReinfPlanner.MinCount));
            Assert.That(d.C1 - d.C0, Is.EqualTo(4 * AddReinfPlanner.Spacing).Within(1e-6));
            Assert.That((d.C0 + d.C1) / 2, Is.EqualTo(4100).Within(AddReinfPlanner.SnapStep));
        }

        [Test]
        public void ZoneAtSlabEdge_StillFiveBars_InsideSlab()
        {
            var d = AddReinfPlanner.Plan(new List<AddReinfPlanner.Zone> { Zone(7800, 7900) }, true, Slab,
                                         new List<(double, double)>()).Single();
            Assert.That(d.Count, Is.EqualTo(AddReinfPlanner.MinCount));
            Assert.That(d.C1, Is.LessThanOrEqualTo(8000 - AddReinfPlanner.Cover + 1e-6));
            Assert.That(d.C0, Is.GreaterThanOrEqualTo(AddReinfPlanner.Cover - 1e-6));
        }

        [Test]
        public void WideZone_CountFromZone()
        {
            var d = AddReinfPlanner.Plan(new List<AddReinfPlanner.Zone> { Zone(1000, 3000) }, true, Slab,
                                         new List<(double, double)>()).Single();
            Assert.That(d.Count, Is.EqualTo(11));
        }
    }
}
