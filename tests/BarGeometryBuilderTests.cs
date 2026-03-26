using NUnit.Framework;
using BricsCadRc.Core;
using System;
using System.Collections.Generic;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Testy BarGeometryBuilder.
    ///
    /// Parametry wspólne:
    ///   diameter = 12  →  r = MinBendRadius(12) = 2×12 = 24 mm
    ///   steps = 6  →  7 punktów na łuk (co 15°)
    ///
    /// Wzór na liczbę węzłów po aproksymacji łuków (N = liczba ostrych węzłów):
    ///   count = 1 + 7*(N-2) + 1 = 7*N - 12
    ///   N=2 →  2   (brak narożników – fallback / prosta)
    ///   N=3 →  9   (1 łuk)
    ///   N=4 → 16   (2 łuki)
    ///   N=5 → 23   (3 łuki)
    ///   N=6 → 30   (4 łuki)
    /// </summary>
    [TestFixture]
    public class BarGeometryBuilderTests
    {
        private const double D   = 12.0;
        private const double R   = 24.0;   // MinBendRadius(12) = 2×12
        private const double Tol = 1e-6;
        private static readonly double Cos45 = Math.Sqrt(2.0) / 2.0;

        // ── Pomocnik: sprawdza że wszystkie punkty z zakresu [from,to] leżą
        //   w odległości R od center (weryfikacja łuku kołowego)
        private static void AssertOnCircle(
            List<(double X, double Y)> pts, int from, int to,
            double cx, double cy, double r = R)
        {
            for (int i = from; i <= to; i++)
            {
                double dx   = pts[i].X - cx;
                double dy   = pts[i].Y - cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                Assert.That(dist, Is.EqualTo(r).Within(Tol),
                    $"pts[{i}]=({pts[i].X:F4},{pts[i].Y:F4}) not on circle center=({cx},{cy}) r={r}");
            }
        }

        // ── IsSupported ──────────────────────────────────────────────────────

        [TestCase("00",  true)]
        [TestCase("11",  true)]
        [TestCase("12",  true)]
        [TestCase("13",  true)]
        [TestCase("14",  true)]
        [TestCase("15",  true)]
        [TestCase("21",  true)]
        [TestCase("22",  true)]
        [TestCase("23",  true)]
        [TestCase("24",  true)]
        [TestCase("25",  true)]
        [TestCase("26",  true)]
        [TestCase("27",  true)]
        [TestCase("28",  true)]
        [TestCase("29",  true)]
        [TestCase("31",  true)]
        [TestCase("32",  true)]
        [TestCase("33",  true)]
        [TestCase("34",  true)]
        [TestCase("35",  true)]
        [TestCase("36",  true)]
        [TestCase("41",  true)]
        [TestCase("44",  true)]
        [TestCase("46",  true)]
        [TestCase("47",  true)]
        [TestCase("51",  true)]
        [TestCase("56",  true)]
        [TestCase("63",  true)]
        [TestCase("64",  true)]
        [TestCase("75",  true)]
        [TestCase("98",  true)]
        [TestCase("99",  true)]
        [TestCase("XX",  false)]
        [TestCase("00X", false)]
        public void IsSupported_ReturnsCorrectValue(string code, bool expected) =>
            Assert.That(BarGeometryBuilder.IsSupported(code), Is.EqualTo(expected));

        [Test]
        public void IsSupported_NullCode_ReturnsFalse() =>
            Assert.That(BarGeometryBuilder.IsSupported(null!), Is.False);

        // ── ArcPoints ────────────────────────────────────────────────────────

        [Test]
        public void ArcPoints_Returns7PointsForSteps6()
        {
            var pts = BarGeometryBuilder.ArcPoints((0, 0), 100, 0, 90, steps: 6);
            Assert.That(pts.Count, Is.EqualTo(7));
        }

        [Test]
        public void ArcPoints_AllPointsOnCircle()
        {
            var pts = BarGeometryBuilder.ArcPoints((50, 50), 100, -90, 0, steps: 6);
            foreach (var p in pts)
            {
                double dist = Math.Sqrt((p.X - 50) * (p.X - 50) + (p.Y - 50) * (p.Y - 50));
                Assert.That(dist, Is.EqualTo(100).Within(Tol));
            }
        }

        [Test]
        public void ArcPoints_StartAndEndMatchAngles()
        {
            var pts = BarGeometryBuilder.ArcPoints((0, 0), 50, 0, 90, steps: 6);
            Assert.That(pts[0].X, Is.EqualTo(50).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0).Within(Tol));
            Assert.That(pts[6].X, Is.EqualTo(0).Within(Tol));
            Assert.That(pts[6].Y, Is.EqualTo(50).Within(Tol));
        }

        // ── 00  Straight ─────────────────────────────────────────────────────

        [Test]
        public void Code00_VertexCount_Is2() =>
            Assert.That(BarGeometryBuilder.GetLocalPoints("00", new[] { 3000.0 }, D).Count, Is.EqualTo(2));

        [Test]
        public void Code00_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("00", new[] { 3000.0 }, D);
            Assert.That(pts[0], Is.EqualTo((0.0, 0.0)));
        }

        [Test]
        public void Code00_EndsAtA()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("00", new[] { 3000.0 }, D);
            Assert.That(pts[1].X, Is.EqualTo(3000.0).Within(Tol));
            Assert.That(pts[1].Y, Is.EqualTo(0.0).Within(Tol));
        }

        // ── 11  90° hook at one end  (9 pts) ─────────────────────────────────

        [Test]
        public void Code11_VertexCount_Is9()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("11", new[] { 500.0, 200.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(9));
        }

        [Test]
        public void Code11_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("11", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[0], Is.EqualTo((0.0, 0.0)));
        }

        [Test]
        public void Code11_EndsAt_A_B()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("11", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[8].X, Is.EqualTo(500.0).Within(Tol));
            Assert.That(pts[8].Y, Is.EqualTo(200.0).Within(Tol));
        }

        [Test]
        public void Code11_ArcStartTangent_Is_Ar_0()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("11", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[1].X, Is.EqualTo(500.0 - R).Within(Tol));
            Assert.That(pts[1].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code11_ArcEndTangent_Is_A_r()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("11", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[7].X, Is.EqualTo(500.0).Within(Tol));
            Assert.That(pts[7].Y, Is.EqualTo(R).Within(Tol));
        }

        [Test]
        public void Code11_BendArc_AllPointsOnCircle()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("11", new[] { 500.0, 200.0 }, D);
            AssertOnCircle(pts, from: 1, to: 7, cx: 500.0 - R, cy: R);
        }

        // ── 12  Hook at both ends  (16 pts) ──────────────────────────────────

        [Test]
        public void Code12_VertexCount_Is16()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("12", new[] { 500.0, 200.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(16));
        }

        [Test]
        public void Code12_StartsAt_0_B()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("12", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(200.0).Within(Tol));
        }

        [Test]
        public void Code12_EndsAt_A_B()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("12", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(500.0).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(200.0).Within(Tol));
        }

        [Test]
        public void Code12_FirstBend_TangentPoints()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("12", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[1].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[1].Y, Is.EqualTo(R).Within(Tol));
            Assert.That(pts[7].X, Is.EqualTo(R).Within(Tol));
            Assert.That(pts[7].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code12_SecondBend_TangentPoints()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("12", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[8].X, Is.EqualTo(500.0 - R).Within(Tol));
            Assert.That(pts[8].Y, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[14].X, Is.EqualTo(500.0).Within(Tol));
            Assert.That(pts[14].Y, Is.EqualTo(R).Within(Tol));
        }

        [Test]
        public void Code12_FirstBend_AllPointsOnCircle()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("12", new[] { 500.0, 200.0 }, D);
            AssertOnCircle(pts, from: 1, to: 7, cx: R, cy: R);
        }

        [Test]
        public void Code12_SecondBend_AllPointsOnCircle()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("12", new[] { 500.0, 200.0 }, D);
            AssertOnCircle(pts, from: 8, to: 14, cx: 500.0 - R, cy: R);
        }

        // ── 13  Crank/offset 45°  (16 pts) ────────────────────────────────────

        [Test]
        public void Code13_VertexCount_Is16()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("13", new[] { 500.0, 200.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(16));
        }

        [Test]
        public void Code13_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("13", new[] { 500.0, 200.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code13_EndsAt_ApBpC_B()
        {
            // Last sharp: (A+B+C, B)
            var pts = BarGeometryBuilder.GetLocalPoints("13", new[] { 500.0, 200.0, 300.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(1000.0).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(200.0).Within(Tol));
        }

        // ── 14  Hook 45°  (9 pts) ─────────────────────────────────────────────

        [Test]
        public void Code14_VertexCount_Is9()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("14", new[] { 500.0, 200.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(9));
        }

        [Test]
        public void Code14_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("14", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code14_EndsAt_45deg()
        {
            // Last sharp: (A + B*cos45, B*sin45)
            double a = 500.0, b = 200.0;
            var pts = BarGeometryBuilder.GetLocalPoints("14", new[] { a, b }, D);
            Assert.That(pts[8].X, Is.EqualTo(a + b * Cos45).Within(Tol));
            Assert.That(pts[8].Y, Is.EqualTo(b * Cos45).Within(Tol));
        }

        // ── 15  Hook 135°  (9 pts) ────────────────────────────────────────────

        [Test]
        public void Code15_VertexCount_Is9()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("15", new[] { 500.0, 200.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(9));
        }

        [Test]
        public void Code15_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("15", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code15_EndsAt_135deg()
        {
            // Last sharp: (A - B*cos45, B*sin45)
            double a = 500.0, b = 200.0;
            var pts = BarGeometryBuilder.GetLocalPoints("15", new[] { a, b }, D);
            Assert.That(pts[8].X, Is.EqualTo(a - b * Cos45).Within(Tol));
            Assert.That(pts[8].Y, Is.EqualTo(b * Cos45).Within(Tol));
        }

        // ── 21  U-bar  (16 pts) ───────────────────────────────────────────────

        [Test]
        public void Code21_VertexCount_Is16()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("21", new[] { 400.0, 300.0, 350.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(16));
        }

        [Test]
        public void Code21_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("21", new[] { 400.0, 300.0, 350.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code21_EndsAt_B_CminusA()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("21", new[] { 400.0, 300.0, 350.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(300.0).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(350.0 - 400.0).Within(Tol));
        }

        [Test]
        public void Code21_BottomTangentPoints_BothOnMinusA()
        {
            double a   = 400.0;
            var pts    = BarGeometryBuilder.GetLocalPoints("21", new[] { a, 300.0, 400.0 }, D);
            Assert.That(pts[7].Y,  Is.EqualTo(-a).Within(Tol));
            Assert.That(pts[8].Y,  Is.EqualTo(-a).Within(Tol));
        }

        [Test]
        public void Code21_AsymmetricLegs()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("21", new[] { 500.0, 200.0, 300.0 }, D);
            Assert.That(pts[0].Y,  Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(300.0 - 500.0).Within(Tol));
        }

        [Test]
        public void Code21_BottomStraightSection_Length_Is_B_minus_2r()
        {
            double b   = 300.0;
            var pts    = BarGeometryBuilder.GetLocalPoints("21", new[] { 400.0, b, 400.0 }, D);
            double len = pts[8].X - pts[7].X;
            Assert.That(len, Is.EqualTo(b - 2 * R).Within(Tol));
        }

        [Test]
        public void Code21_FirstBend_AllPointsOnCircle()
        {
            double a   = 400.0;
            var pts    = BarGeometryBuilder.GetLocalPoints("21", new[] { a, 300.0, 350.0 }, D);
            AssertOnCircle(pts, from: 1, to: 7, cx: R, cy: -a + R);
        }

        [Test]
        public void Code21_SecondBend_AllPointsOnCircle()
        {
            double a   = 400.0;
            var pts    = BarGeometryBuilder.GetLocalPoints("21", new[] { a, 300.0, 350.0 }, D);
            AssertOnCircle(pts, from: 8, to: 14, cx: 300.0 - R, cy: -a + R);
        }

        // ── 22  U-shape nierówny  (16 pts) ───────────────────────────────────

        [Test]
        public void Code22_VertexCount_Is16()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("22", new[] { 400.0, 300.0, 350.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(16));
        }

        [Test]
        public void Code22_StartsAt_0_A()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("22", new[] { 400.0, 300.0, 350.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(400.0).Within(Tol));
        }

        [Test]
        public void Code22_EndsAt_B_C()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("22", new[] { 400.0, 300.0, 350.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(300.0).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(350.0).Within(Tol));
        }

        // ── 23  Z-bar  (16 pts) ───────────────────────────────────────────────

        [Test]
        public void Code23_VertexCount_Is16()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("23", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(16));
        }

        [Test]
        public void Code23_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("23", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code23_EndsAt_ApC_B()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("23", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(800.0).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(400.0).Within(Tol));
        }

        // ── 24  Crank łagodny – ta sama geometria co 23  (16 pts) ─────────────

        [Test]
        public void Code24_VertexCount_Is16()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("24", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(16));
        }

        [Test]
        public void Code24_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("24", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code24_EndsAt_ApC_B()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("24", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(800.0).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(400.0).Within(Tol));
        }

        // ── 25  Hook + crank  (23 pts) ────────────────────────────────────────

        [Test]
        public void Code25_VertexCount_Is23()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("25", new[] { 500.0, 400.0, 300.0, 200.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code25_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("25", new[] { 500.0, 400.0, 300.0, 200.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code25_EndsAt_45deg()
        {
            // Last sharp: (A+C+E*cos45, B+E*sin45)
            double a = 500.0, b = 400.0, c = 300.0, e = 200.0;
            var pts = BarGeometryBuilder.GetLocalPoints("25", new[] { a, b, c, e }, D);
            Assert.That(pts[22].X, Is.EqualTo(a + c + e * Cos45).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(b + e * Cos45).Within(Tol));
        }

        // ── 26  Hook + leg  (16 pts) ──────────────────────────────────────────

        [Test]
        public void Code26_VertexCount_Is16()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("26", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(16));
        }

        [Test]
        public void Code26_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("26", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code26_EndsAt_ApC_minusB()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("26", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(800.0).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(-400.0).Within(Tol));
        }

        // ── 27  Crank z hakiem górnym  (23 pts) ───────────────────────────────

        [Test]
        public void Code27_VertexCount_Is23()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("27", new[] { 300.0, 200.0, 400.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code27_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("27", new[] { 300.0, 200.0, 400.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code27_EndsAt_ApBpC_BpR()
        {
            double a = 300.0, b = 200.0, c = 400.0;
            var pts = BarGeometryBuilder.GetLocalPoints("27", new[] { a, b, c }, D);
            Assert.That(pts[22].X, Is.EqualTo(a + b + c).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(b + R).Within(Tol));
        }

        // ── 28  Crank z hakiem dolnym  (23 pts) ───────────────────────────────

        [Test]
        public void Code28_VertexCount_Is23()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("28", new[] { 300.0, 200.0, 400.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code28_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("28", new[] { 300.0, 200.0, 400.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code28_EndsAt_ApBpC_BminusR()
        {
            double a = 300.0, b = 200.0, c = 400.0;
            var pts = BarGeometryBuilder.GetLocalPoints("28", new[] { a, b, c }, D);
            Assert.That(pts[22].X, Is.EqualTo(a + b + c).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(b - R).Within(Tol));
        }

        // ── 29  Crank symetryczny  (23 pts) ───────────────────────────────────

        [Test]
        public void Code29_VertexCount_Is23()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("29", new[] { 300.0, 200.0, 400.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code29_StartsAt_0_minusR()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("29", new[] { 300.0, 200.0, 400.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(-R).Within(Tol));
        }

        [Test]
        public void Code29_EndsAt_ApBpC_B()
        {
            double a = 300.0, b = 200.0, c = 400.0;
            var pts = BarGeometryBuilder.GetLocalPoints("29", new[] { a, b, c }, D);
            Assert.That(pts[22].X, Is.EqualTo(a + b + c).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(b).Within(Tol));
        }

        // ── 31  Z + hook 45°  (23 pts) ────────────────────────────────────────

        [Test]
        public void Code31_VertexCount_Is23()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("31", new[] { 400.0, 300.0, 200.0, 150.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code31_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("31", new[] { 400.0, 300.0, 200.0, 150.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code31_EndsAt_45deg()
        {
            double a = 400.0, b = 300.0, c = 200.0, d = 150.0;
            var pts = BarGeometryBuilder.GetLocalPoints("31", new[] { a, b, c, d }, D);
            Assert.That(pts[22].X, Is.EqualTo(a + c + d * Cos45).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(b + d * Cos45).Within(Tol));
        }

        // ── 32  S-shape  (23 pts) ─────────────────────────────────────────────

        [Test]
        public void Code32_VertexCount_Is23()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("32", new[] { 400.0, 300.0, 200.0, 150.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code32_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("32", new[] { 400.0, 300.0, 200.0, 150.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code32_EndsAt_ApC_minusD()
        {
            double a = 400.0, c = 200.0, d = 150.0;
            var pts = BarGeometryBuilder.GetLocalPoints("32", new[] { a, 300.0, c, d }, D);
            Assert.That(pts[22].X, Is.EqualTo(a + c).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(-d).Within(Tol));
        }

        // ── 33  S-shape odwrócony  (23 pts) ───────────────────────────────────

        [Test]
        public void Code33_VertexCount_Is23()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("33", new[] { 400.0, 300.0, 200.0, 150.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code33_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("33", new[] { 400.0, 300.0, 200.0, 150.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code33_EndsAt_ApC_D()
        {
            double a = 400.0, c = 200.0, d = 150.0;
            var pts = BarGeometryBuilder.GetLocalPoints("33", new[] { a, 300.0, c, d }, D);
            Assert.That(pts[22].X, Is.EqualTo(a + c).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(d).Within(Tol));
        }

        // ── 34  Closed rectangle  (23 pts) ───────────────────────────────────

        [Test]
        public void Code34_VertexCount_Is23()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("34", new[] { 500.0, 400.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code34_IsClosed()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("34", new[] { 500.0, 400.0 }, D);
            Assert.That(pts[22].X, Is.EqualTo(pts[0].X).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(pts[0].Y).Within(Tol));
        }

        [Test]
        public void Code34_StartsAndEndsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("34", new[] { 500.0, 400.0 }, D);
            Assert.That(pts[0],  Is.EqualTo((0.0, 0.0)));
            Assert.That(pts[22].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code34_Bend1_TangentPoints()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("34", new[] { 500.0, 400.0 }, D);
            Assert.That(pts[1].X, Is.EqualTo(500.0 - R).Within(Tol));
            Assert.That(pts[1].Y, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[7].X, Is.EqualTo(500.0).Within(Tol));
            Assert.That(pts[7].Y, Is.EqualTo(R).Within(Tol));
        }

        [Test]
        public void Code34_Bend2_TangentPoints()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("34", new[] { 500.0, 400.0 }, D);
            Assert.That(pts[8].X,  Is.EqualTo(500.0).Within(Tol));
            Assert.That(pts[8].Y,  Is.EqualTo(400.0 - R).Within(Tol));
            Assert.That(pts[14].X, Is.EqualTo(500.0 - R).Within(Tol));
            Assert.That(pts[14].Y, Is.EqualTo(400.0).Within(Tol));
        }

        [Test]
        public void Code34_Bend3_TangentPoints()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("34", new[] { 500.0, 400.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(R).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(400.0).Within(Tol));
            Assert.That(pts[21].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[21].Y, Is.EqualTo(400.0 - R).Within(Tol));
        }

        [Test]
        public void Code34_AllBends_OnCircle()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("34", new[] { 500.0, 400.0 }, D);
            AssertOnCircle(pts, 1,  7,  500.0 - R,   R);
            AssertOnCircle(pts, 8,  14, 500.0 - R, 400.0 - R);
            AssertOnCircle(pts, 15, 21, R,         400.0 - R);
        }

        // ── 35  Closed square  (23 pts) ──────────────────────────────────────

        [Test]
        public void Code35_VertexCount_Is23()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("35", new[] { 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code35_IsClosed()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("35", new[] { 300.0 }, D);
            Assert.That(pts[22].X, Is.EqualTo(pts[0].X).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(pts[0].Y).Within(Tol));
        }

        [Test]
        public void Code35_AllBends_OnCircle()
        {
            double a   = 300.0;
            var pts    = BarGeometryBuilder.GetLocalPoints("35", new[] { a }, D);
            AssertOnCircle(pts, 1,  7,  a - R, R);
            AssertOnCircle(pts, 8,  14, a - R, a - R);
            AssertOnCircle(pts, 15, 21, R,     a - R);
        }

        [Test]
        public void Code35_BothAxesSymmetric()
        {
            double a   = 400.0;
            var pts    = BarGeometryBuilder.GetLocalPoints("35", new[] { a }, D);
            double d1  = a - pts[1].X;
            double d2  = a - pts[8].Y;
            Assert.That(d1, Is.EqualTo(d2).Within(Tol));
        }

        // ── 36  Prostokąt nierówny  (23 pts) ─────────────────────────────────

        [Test]
        public void Code36_VertexCount_Is23()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("36", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code36_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("36", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code36_IsClosed()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("36", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[22].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(0.0).Within(Tol));
        }

        // ── 41  Wielokąt 4-boczny  (23 pts) ──────────────────────────────────

        [Test]
        public void Code41_VertexCount_Is23()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("41", new[] { 400.0, 200.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code41_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("41", new[] { 400.0, 200.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code41_IsClosed()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("41", new[] { 400.0, 200.0 }, D);
            Assert.That(pts[22].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(0.0).Within(Tol));
        }

        // ── 44  Okrąg  (9 pts: 8 unikalnych + zamknięcie) ────────────────────

        [Test]
        public void Code44_VertexCount_Is9()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("44", new[] { 500.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(9));
        }

        [Test]
        public void Code44_StartsAt_A_halfA()
        {
            // angle=0°: (cx+r, cy) = (A/2+A/2, A/2) = (A, A/2)
            double a = 500.0;
            var pts = BarGeometryBuilder.GetLocalPoints("44", new[] { a }, D);
            Assert.That(pts[0].X, Is.EqualTo(a).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(a / 2.0).Within(Tol));
        }

        [Test]
        public void Code44_IsClosed()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("44", new[] { 500.0 }, D);
            Assert.That(pts[8].X, Is.EqualTo(pts[0].X).Within(Tol));
            Assert.That(pts[8].Y, Is.EqualTo(pts[0].Y).Within(Tol));
        }

        [Test]
        public void Code44_AllPointsOnCircle()
        {
            double a  = 500.0;
            var pts   = BarGeometryBuilder.GetLocalPoints("44", new[] { a }, D);
            double cx = a / 2.0, cy = a / 2.0, rad = a / 2.0;
            foreach (var p in pts)
            {
                double dist = Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
                Assert.That(dist, Is.EqualTo(rad).Within(Tol));
            }
        }

        // ── 46  Romb  (23 pts) ────────────────────────────────────────────────

        [Test]
        public void Code46_VertexCount_Is23()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("46", new[] { 400.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code46_StartsAt_halfA_0()
        {
            double a = 400.0;
            var pts = BarGeometryBuilder.GetLocalPoints("46", new[] { a, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(a / 2.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code46_IsClosed()
        {
            double a = 400.0;
            var pts = BarGeometryBuilder.GetLocalPoints("46", new[] { a, 300.0 }, D);
            Assert.That(pts[22].X, Is.EqualTo(a / 2.0).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(0.0).Within(Tol));
        }

        // ── 47  Trójkąt  (16 pts) ─────────────────────────────────────────────

        [Test]
        public void Code47_VertexCount_Is16()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("47", new[] { 500.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(16));
        }

        [Test]
        public void Code47_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("47", new[] { 500.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code47_IsClosed()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("47", new[] { 500.0, 300.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(0.0).Within(Tol));
        }

        // ── 51  Closed stirrup — jeden otwarty pręt z overlapem w górnym prawym rogu ──
        // 7 ostrych węzłów, 5 narożników 90° (górny prawy odwiedzany dwukrotnie)
        // → 1+5×7+1=37 pkt
        // hook = Max(16d,160) = 192 mm dla d=12
        // node0=(A,B-hook)=(400,108); node6=(A-hook,B)=(208,300)

        [Test]
        public void Code51_VertexCount_Is37()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("51", new[] { 400.0, 300.0, 0.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(37));
        }

        [Test]
        public void Code51_StartsAt_A_BminusHook()
        {
            // hook = Max(16*12,160) = 192; start = (400, 300-192) = (400, 108)
            var pts = BarGeometryBuilder.GetLocalPoints("51", new[] { 400.0, 300.0, 0.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(400.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(108.0).Within(Tol));
        }

        [Test]
        public void Code51_EndsAt_AminusHook_B()
        {
            // hook = 192; end = (400-192, 300) = (208, 300)
            var pts = BarGeometryBuilder.GetLocalPoints("51", new[] { 400.0, 300.0, 0.0 }, D);
            Assert.That(pts[36].X, Is.EqualTo(208.0).Within(Tol));
            Assert.That(pts[36].Y, Is.EqualTo(300.0).Within(Tol));
        }

        [Test]
        public void Code51_ExplicitHook_UsesC()
        {
            // C=210 → hook=210; start=(400,300-210)=(400,90); end=(400-210,300)=(190,300)
            var pts = BarGeometryBuilder.GetLocalPoints("51", new[] { 400.0, 300.0, 210.0 }, D);
            Assert.That(pts[0].X,  Is.EqualTo(400.0).Within(Tol));
            Assert.That(pts[0].Y,  Is.EqualTo(90.0).Within(Tol));
            Assert.That(pts[36].X, Is.EqualTo(190.0).Within(Tol));
            Assert.That(pts[36].Y, Is.EqualTo(300.0).Within(Tol));
        }

        // ── 51 / 63  Jawny hak (C > 0) ─────────────────────────────────────────

        // ── 56  Complex 5-leg  (30 pts) ───────────────────────────────────────

        [Test]
        public void Code56_VertexCount_Is30()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("56", new[] { 400.0, 300.0, 200.0, 150.0, 100.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(30));
        }

        [Test]
        public void Code56_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("56", new[] { 400.0, 300.0, 200.0, 150.0, 100.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code56_EndsAt_ApCpE_BminusD()
        {
            double a = 400.0, b = 300.0, c = 200.0, d = 150.0, e = 100.0;
            var pts = BarGeometryBuilder.GetLocalPoints("56", new[] { a, b, c, d, e }, D);
            Assert.That(pts[29].X, Is.EqualTo(a + c + e).Within(Tol));
            Assert.That(pts[29].Y, Is.EqualTo(b - d).Within(Tol));
        }

        // ── 63  Closed stirrup — haki PIONOWO W DÓŁ z obu górnych rogów  (44 pts) ──
        // BS8666: prostokąt A(wys)×B(szer), double-visit obu górnych rogów
        // 8 węzłów: 6 narożników 90° CW → 1+6×7+1=44 pkt
        // d=12: hook=Max(14*12,150)=168; A=400,B=300
        //   start=(0,168); end=(300, 400-168)=(300,232)

        [Test]
        public void Code63_VertexCount_Is44()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("63", new[] { 400.0, 300.0, 0.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(44));
        }

        [Test]
        public void Code63_StartsAt_0_Hook()
        {
            // hook = max(14*12,150) = 168; start = (0, 168)
            double hook = Math.Max(14.0 * D, 150.0);
            var pts = BarGeometryBuilder.GetLocalPoints("63", new[] { 400.0, 300.0, 0.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(hook).Within(Tol));
        }

        [Test]
        public void Code63_EndsAt_B_AminusHook()
        {
            // end = (B, A-hook) = (300, 400-168) = (300, 232)
            double a = 400.0, b = 300.0;
            double hook = Math.Max(14.0 * D, 150.0);
            var pts = BarGeometryBuilder.GetLocalPoints("63", new[] { a, b, 0.0 }, D);
            Assert.That(pts[43].X, Is.EqualTo(b).Within(Tol));
            Assert.That(pts[43].Y, Is.EqualTo(a - hook).Within(Tol));
        }

        [Test]
        public void Code63_ExplicitHook_UsesC()
        {
            // C=210 → hook=210; start=(0,210); end=(300, 400-210)=(300,190)
            var pts = BarGeometryBuilder.GetLocalPoints("63", new[] { 400.0, 300.0, 210.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(44));
            Assert.That(pts[0].X,  Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y,  Is.EqualTo(210.0).Within(Tol));
            Assert.That(pts[43].X, Is.EqualTo(300.0).Within(Tol));
            Assert.That(pts[43].Y, Is.EqualTo(190.0).Within(Tol));
        }

        // ── 64  Complex 5-leg variant – ta sama geometria co 56  (30 pts) ─────

        [Test]
        public void Code64_VertexCount_Is30()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("64", new[] { 400.0, 300.0, 200.0, 150.0, 100.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(30));
        }

        [Test]
        public void Code64_StartsAtOrigin()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("64", new[] { 400.0, 300.0, 200.0, 150.0, 100.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code64_EndsAt_ApCpE_BminusD()
        {
            double a = 400.0, b = 300.0, c = 200.0, d = 150.0, e = 100.0;
            var pts = BarGeometryBuilder.GetLocalPoints("64", new[] { a, b, c, d, e }, D);
            Assert.That(pts[29].X, Is.EqualTo(a + c + e).Within(Tol));
            Assert.That(pts[29].Y, Is.EqualTo(b - d).Within(Tol));
        }

        // ── 75  Spirala  ──────────────────────────────────────────────────────

        [Test]
        public void Code75_VertexCount_Is_nTurns_x12_plus1()
        {
            // B=3 zwoje → 3*12+1 = 37
            var pts = BarGeometryBuilder.GetLocalPoints("75", new[] { 200.0, 3.0, 100.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(37));
        }

        [Test]
        public void Code75_StartsAt_A_0()
        {
            // i=0: x = radius + radius*cos(0) = A, y = 0
            double a = 200.0;
            var pts = BarGeometryBuilder.GetLocalPoints("75", new[] { a, 3.0, 100.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(a).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code75_EndsAt_A_nTurns_x_pitch()
        {
            // i=36 (3 zwoje * 12): angle=6π → x=A, y = pitch*36/12 = pitch*3
            double a = 200.0, pitch = 100.0;
            var pts = BarGeometryBuilder.GetLocalPoints("75", new[] { a, 3.0, pitch }, D);
            Assert.That(pts[36].X, Is.EqualTo(a).Within(Tol));
            Assert.That(pts[36].Y, Is.EqualTo(pitch * 3.0).Within(Tol));
        }

        // ── 98 / 99  Custom – prosta linia  ───────────────────────────────────

        [Test]
        public void Code98_VertexCount_Is2()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("98", new[] { 2000.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(2));
        }

        [Test]
        public void Code98_StartsAtOrigin_EndsAtA()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("98", new[] { 2000.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[1].X, Is.EqualTo(2000.0).Within(Tol));
            Assert.That(pts[1].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code99_VertexCount_Is2()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("99", new[] { 1500.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(2));
        }

        [Test]
        public void Code99_StartsAtOrigin_EndsAtA()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("99", new[] { 1500.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[1].X, Is.EqualTo(1500.0).Within(Tol));
            Assert.That(pts[1].Y, Is.EqualTo(0.0).Within(Tol));
        }

        // ── Fallback ─────────────────────────────────────────────────────────

        [TestCase("XX")]
        [TestCase("00X")]
        public void UnsupportedCode_FallsBackToStraightLine(string code)
        {
            var pts = BarGeometryBuilder.GetLocalPoints(code, new[] { 2000.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(2));
            Assert.That(pts[0], Is.EqualTo((0.0, 0.0)));
            Assert.That(pts[1].X, Is.EqualTo(2000.0).Within(Tol));
            Assert.That(pts[1].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void NullCode_FallsBackToStraightLine()
        {
            var pts = BarGeometryBuilder.GetLocalPoints(null, new[] { 1500.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(2));
        }
    }
}
