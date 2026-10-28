using System.Collections.Generic;
using System.Linq;
using BricsCadRc.Core;
using NUnit.Framework;

namespace BricsCadRc.Tests
{
    /// <summary>GA PLOT 367 (uwaga Bartka 2026-10-28): obrys SD-PILED-RAFT z linii i otwartych polilinii, styki „na T”.</summary>
    public class OutlineFromPiecesTests
    {
        private static readonly ((double X, double Y) A, (double X, double Y) B)[] Plot367 =
        {
            ((6153916, -359511), (6148168, -359511)), ((6148168, -352771), (6153916, -352771)), ((6147638, -353724), (6145978, -353724)),
            ((6145978, -358911), (6147893, -358911)), ((6153916, -353371), (6148168, -353371)), ((6148168, -358911), (6153981, -358911)),
            ((6145591, -359511), (6143041, -359511)), ((6143041, -358911), (6145591, -358911)), ((6143041, -359511), (6142441, -359511)),
            ((6142441, -359511), (6142441, -352771)), ((6148168, -359511), (6145591, -359511)), ((6145591, -358911), (6145978, -358911)),
            ((6145978, -358911), (6145978, -353371)), ((6145978, -353371), (6143041, -353371)), ((6143041, -353371), (6143041, -358911)),
            ((6148168, -353371), (6148168, -353724)), ((6148168, -353724), (6147638, -353724)), ((6147638, -353724), (6147638, -356758)),
            ((6147638, -356758), (6147893, -356758)), ((6147893, -356758), (6147893, -358911)), ((6147893, -358911), (6148168, -358911)),
            ((6153916, -353521), (6153981, -353521)), ((6153981, -353521), (6153981, -358911)), ((6142441, -352771), (6148168, -352771)),
            ((6153916, -353521), (6153916, -353371)), ((6153791, -359511), (6154581, -359511)), ((6154581, -359511), (6154581, -352771)),
            ((6154581, -352771), (6153791, -352771)),
        };

        [Test]
        public void Plot367_OuterRectangle()
        {
            var r = OutlineFromPieces.OuterBoundary(Plot367, (6148839, -356077));
            Assert.That(r, Is.Not.Null);
            Assert.That(r.Count, Is.EqualTo(4), string.Join(" ", r.Select(p => $"({p.X:F0},{p.Y:F0})")));
            Assert.That(r.Min(p => p.X), Is.EqualTo(6142441).Within(1e-6));
            Assert.That(r.Max(p => p.X), Is.EqualTo(6154581).Within(1e-6));
            Assert.That(r.Min(p => p.Y), Is.EqualTo(-359511).Within(1e-6));
            Assert.That(r.Max(p => p.Y), Is.EqualTo(-352771).Within(1e-6));
            Assert.That(OutlineFromPieces.OuterBoundary(Plot367, (6100000, -356077)), Is.Null, "punkt poza obrysem");
        }

        [Test]
        public void OpenChain_NotClosed_Null()
            => Assert.That(OutlineFromPieces.OuterBoundary(new[] { ((0.0, 0.0), (10.0, 0.0)), ((10.0, 0.0), (10.0, 10.0)), ((10.0, 10.0), (0.0, 10.0)) },
                                                           (5, 5)), Is.Null);
    }
}
