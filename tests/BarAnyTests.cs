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
    }
}
