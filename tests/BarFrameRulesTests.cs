using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// W1d (#4) / W1e (#5): BarFrameRules — rozpoznanie obrotu i odbicia pręta z obrysu (docs/PLAN_POPRAWKI_W1_W2.md 6.1).
    /// Obrys w świecie budowany niezależnie: offset + R(θ)·M·local (M = odbicie y → −y).
    /// </summary>
    [TestFixture]
    public class BarFrameRulesTests
    {
        private static double[] Pv(double a, double b = 0, double c = 0) => new[] { a, b, c, 0.0, 0.0 };

        private static List<(double X, double Y)> ToWorld(List<(double X, double Y)> local, double deg, bool mirrored,
                                                          double ox = 1234.5, double oy = -987.25)
        {
            double t = deg * Math.PI / 180.0, c = Math.Cos(t), s = Math.Sin(t);
            return local.Select(p =>
            {
                double y = mirrored ? -p.Y : p.Y;
                return (ox + c * p.X - s * y, oy + s * p.X + c * y);
            }).ToList();
        }

        private static double NormDeg(double deg)
        {
            while (deg <= -180) deg += 360;
            while (deg > 180) deg -= 360;
            return deg;
        }

        [TestCase("00", 3000, 0, 0, 12)]
        [TestCase("11", 1000, 300, 0, 12)]
        [TestCase("21", 500, 300, 500, 10)]
        [TestCase("13", 800, 200, 500, 12)]
        [TestCase("99", 2500, 0, 0, 16)]
        public void TryFit_RotationAndMirror_Recovered(string code, double a, double b, double c, int d)
        {
            var local = BarFrameRules.LocalOutline(code, Pv(a, b, c), d);
            Assert.That(local, Is.Not.Null);
            foreach (double deg in new[] { 0.0, 30.0, 90.0, 180.0, -45.0 })
                foreach (bool mir in new[] { false, true })
                {
                    var world = ToWorld(local, deg, mir);
                    Assert.That(BarFrameRules.TryFit(world, local, out var f, out double dev), Is.True, $"{code} {deg} {mir}");
                    Assert.That(dev, Is.LessThan(1e-6), $"{code} {deg} {mir}");
                    Assert.That(f.Mirrored, Is.EqualTo(mir), $"{code} {deg} {mir}");
                    Assert.That(NormDeg(f.Angle * 180 / Math.PI - deg), Is.EqualTo(0).Within(1e-6), $"{code} {deg} {mir}");
                }
        }

        [Test]
        public void TryFit_Horizontal_IsExactIdentity()
        {
            var local = BarFrameRules.LocalOutline("21", Pv(500, 300, 500), 10);
            var world = ToWorld(local, 0, false);
            Assert.That(BarFrameRules.TryFit(world, local, out var f, out _), Is.True);
            Assert.That(f.IsIdentity, Is.True);
            Assert.That(f.Angle, Is.EqualTo(0.0));
        }

        [Test]
        public void TryFit_TinyAngle_SnapsToIdentity()
        {
            var local = BarFrameRules.LocalOutline("00", Pv(3000), 12);
            var world = ToWorld(local, 1e-12 * 180 / Math.PI, false);
            Assert.That(BarFrameRules.TryFit(world, local, out var f, out _), Is.True);
            Assert.That(f.IsIdentity, Is.True);
        }

        [Test]
        public void TryFit_MirroredStraight_DistinguishedFromRotation()
        {
            // MIRROR prostego o linię pionową: x → −x (= R(180°)·M)
            var local = BarFrameRules.LocalOutline("00", Pv(3000), 12);
            var world = local.Select(p => (-p.X + 5000.0, p.Y)).ToList();
            Assert.That(BarFrameRules.TryFit(world, local, out var f, out _), Is.True);
            Assert.That(f.Mirrored, Is.True);
            Assert.That(Math.Abs(NormDeg(f.Angle * 180 / Math.PI)), Is.EqualTo(180).Within(1e-9));
        }

        [Test]
        public void TryFit_BrokenOutline_False()
        {
            var local = BarFrameRules.LocalOutline("21", Pv(500, 300, 500), 10);
            var world = ToWorld(local, 30, false);
            world[5] = (world[5].X + 5, world[5].Y);
            Assert.That(BarFrameRules.TryFit(world, local, out _, out double dev), Is.False);
            Assert.That(dev, Is.GreaterThan(BarFrameRules.FitTol));
        }

        [Test]
        public void TryFit_StretchedOutline_False()
        {
            var local = BarFrameRules.LocalOutline("00", Pv(3000), 12);
            var world = ToWorld(BarFrameRules.LocalOutline("00", Pv(3500), 12), 30, false);
            Assert.That(BarFrameRules.TryFit(world, local, out _, out _), Is.False);
        }

        [Test]
        public void TryFit_CountMismatch_False()
        {
            var local = BarFrameRules.LocalOutline("00", Pv(3000), 12);
            var world = ToWorld(BarFrameRules.LocalOutline("21", Pv(500, 300, 500), 12), 0, false);
            Assert.That(BarFrameRules.TryFit(world, local, out _, out _), Is.False);
        }

        [Test]
        public void LocalOutline_Ring_Null()
            => Assert.That(BarFrameRules.LocalOutline("75", Pv(300), 12), Is.Null);

        [Test]
        public void IsRingShape_OnlyCode75()
        {
            // W4c: pierścień = 75 (BS 8666:2020); 44 = kapelusz
            Assert.That(BarFrameRules.RingShapeCode, Is.EqualTo("75"));
            Assert.That(BarFrameRules.IsRingShape("75"), Is.True);
            Assert.That(BarFrameRules.IsRingShape("44"), Is.False);
            Assert.That(BarFrameRules.IsRingShape("00"), Is.False);
            Assert.That(BarFrameRules.IsRingShape(null!), Is.False);
        }

        private static List<(double X, double Y)> Ring(double cx, double cy, double r, double deg, bool mirrored, int n = 24)
        {
            var res = new List<(double X, double Y)>();
            double t0 = deg * Math.PI / 180.0;
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n * (mirrored ? -1 : 1) + t0;
                res.Add((cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
            }
            return res;
        }

        [TestCase(0.0, false)]
        [TestCase(45.0, false)]
        [TestCase(-120.0, true)]
        [TestCase(180.0, true)]
        public void TryFitRing_CenterAngleMirror(double deg, bool mir)
        {
            var w = Ring(150, 210150, 156, deg, mir);
            Assert.That(BarFrameRules.TryFitRing(w, out var c, out var f), Is.True);
            Assert.That(c.X, Is.EqualTo(150).Within(1e-9));
            Assert.That(c.Y, Is.EqualTo(210150).Within(1e-9));
            Assert.That(f.Mirrored, Is.EqualTo(mir));
            Assert.That(NormDeg(f.Angle * 180 / Math.PI - deg), Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void TryFitRing_Unrotated_IsIdentity()
        {
            Assert.That(BarFrameRules.TryFitRing(Ring(150, 150, 156, 0, false), out _, out var f), Is.True);
            Assert.That(f.IsIdentity, Is.True);
        }

        [Test]
        public void TryFitRing_RectangleOrEllipse_False()
        {
            var rect = new List<(double X, double Y)> { (0, 6), (3000, 6), (3000, -6), (0, -6) };
            Assert.That(BarFrameRules.TryFitRing(rect, out _, out _), Is.False);
            var ell = Ring(0, 0, 150, 0, false).Select(p => (p.X * 1.1, p.Y)).ToList();
            Assert.That(BarFrameRules.TryFitRing(ell, out _, out _), Is.False);
        }

        [Test]
        public void TryChord_Rotated()
        {
            Assert.That(BarFrameRules.TryChord((0, 0), (0, 10), (0, 0), (5, 0), out var f), Is.True);
            Assert.That(f.Angle, Is.EqualTo(Math.PI / 2).Within(1e-12));
            Assert.That(f.Mirrored, Is.False);
            Assert.That(BarFrameRules.TryChord((0, 0), (0, 0), (0, 0), (5, 0), out _), Is.False);
        }

        [Test]
        public void Frame_Apply_MirrorBeforeRotation()
        {
            var f = new BarFrame2(Math.PI / 2, true);
            var (x, y) = f.Apply(1, 2);   // M: (1, −2) → R(90°): (2, 1)
            Assert.That(x, Is.EqualTo(2).Within(1e-12));
            Assert.That(y, Is.EqualTo(1).Within(1e-12));
            Assert.That(BarFrame2.Identity.Apply(3, 4), Is.EqualTo((3.0, 4.0)));
        }
    }
}
