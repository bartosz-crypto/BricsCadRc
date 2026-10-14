using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Rozkład Any (decyzja użytkownika 2026-10-14, jak „Any” w ASD): w bloku jeden pręt — cały kształt pręta wzorcowego,
    /// najdłuższy prosty odcinek na osi prętów od punktu wstawienia (BarGeometryBuilder.AnyAxis).
    /// </summary>
    [TestFixture]
    public class BarAnyTests
    {
        private static double Len((double X, double Y) a, (double X, double Y) b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

        [Test]
        public void Straight_AlongX_FromOrigin()
        {
            var p = BarGeometryBuilder.AnyAxis("00", new double[] { 2000, 0, 0, 0, 0 }, 12, mirror: false, alongY: false);
            Assert.That(p, Is.EqualTo(new[] { (0.0, 0.0), (2000.0, 0.0) }));
            var q = BarGeometryBuilder.AnyAxis("00", new double[] { 2000, 0, 0, 0, 0 }, 12, mirror: false, alongY: true);
            Assert.That(q, Is.EqualTo(new[] { (0.0, 0.0), (0.0, 2000.0) }));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UBar_LongestLegOnAxis_ShapePreserved(bool mirror)
        {
            var dims = new double[] { 700, 250, 700, 0, 0 };
            var local = BarGeometryBuilder.GetLocalPoints("21", (double[])dims.Clone(), 12);
            var p = BarGeometryBuilder.AnyAxis("21", dims, 12, mirror, alongY: false);
            Assert.That(p.Count, Is.EqualTo(local.Count));
            // długości odcinków bez zmian (obrót / odbicie)
            for (int i = 0; i + 1 < p.Count; i++)
                Assert.That(Len(p[i], p[i + 1]), Is.EqualTo(Len(local[i], local[i + 1])).Within(1e-6));
            // najdłuższy odcinek na osi X od (0, 0) w stronę +X; zagięcia nad (bez lustra) albo pod osią
            Assert.That(p[0], Is.EqualTo((0.0, 0.0)));
            Assert.That(p[1].Y, Is.EqualTo(0).Within(1e-6));
            Assert.That(p[1].X, Is.GreaterThan(600));
            double maxAbsY = p.Max(q => Math.Abs(q.Y));
            Assert.That(mirror ? p.Min(q => q.Y) : p.Max(q => q.Y), Is.EqualTo(mirror ? -maxAbsY : maxAbsY).Within(1e-6));
        }

        [Test]
        public void Reverse_BendAtInsertionPoint()
        {
            var dims = new double[] { 700, 250, 700, 0, 0 };
            var f = BarGeometryBuilder.AnyAxis("21", dims, 12, mirror: false, alongY: false);
            var r = BarGeometryBuilder.AnyAxis("21", dims, 12, mirror: false, alongY: false, reverse: true);
            double len = f[1].X;
            // odbicie wzdłuż: x → len − x, ramię od len do 0, zagięcie przy 0
            for (int i = 0; i < f.Count; i++)
            {
                Assert.That(r[i].X, Is.EqualTo(len - f[i].X).Within(1e-6));
                Assert.That(r[i].Y, Is.EqualTo(f[i].Y).Within(1e-6));
            }
            Assert.That(r[0], Is.EqualTo((len, 0.0)));
        }

        [TestCase(300, 0)] [TestCase(375, 0)] [TestCase(400, 0)]
        [TestCase(450, 1)] [TestCase(525, 1)] [TestCase(600, 2)] [TestCase(675, 2)]
        [TestCase(750, 3)] [TestCase(825, 3)] [TestCase(900, 4)] [TestCase(975, 4)] [TestCase(1050, 5)]
        public void SideLevelsForHeight_2026_10_14(double h, int n)
            => Assert.That(BeamGeometry.SideLevelsForHeight(h), Is.EqualTo(n));
    }
}
