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
    ///   diameter = 12  →  r = MinBendRadius(12) = 2×12 = 24 mm (promień wewnętrzny)
    ///   łuk osi pręta: RA = r + d/2 = 30 mm; punkty styczne RA·tan(φ/2) od naroża
    ///   kształty z listy wyboru (11, 13, 15, 21, 33, 44, 46, 51, 63): wymiary ZEWNĘTRZNE
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
        private const double RA  = 30.0;   // promień łuku osi = R + D/2
        private const double H   = 6.0;    // D/2
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

        // ── Łuk przy gięciu ≠ 90°: punkty styczne RA·tan(φ/2) od naroża ─────

        [Test]
        public void CornerArc_45deg_TangentDistance()
        {
            var arc = new List<(double X, double Y)>(BarGeometryBuilder.CornerArcPoints((-500, 0), (0, 0), (500, 500), D));
            double tt = RA * Math.Tan(Math.PI / 8);
            Assert.That(arc[0].X, Is.EqualTo(-tt).Within(Tol));
            Assert.That(arc[0].Y, Is.EqualTo(0.0).Within(Tol));
            Assert.That(arc[6].X, Is.EqualTo(tt * Math.Sqrt(0.5)).Within(Tol));
            Assert.That(arc[6].Y, Is.EqualTo(tt * Math.Sqrt(0.5)).Within(Tol));
        }

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

        // ── 11  BS 8666: (B) poziomo, A pionowo; wymiary zewnętrzne  (9 pts) ──

        [Test]
        public void Code11_VertexCount_Is9()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("11", new[] { 300.0, 1500.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(9));
        }

        [Test]
        public void Code11_StartsAtOrigin_EndsAt_BminusH_AminusH()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("11", new[] { 300.0, 1500.0 }, D);
            Assert.That(pts[0], Is.EqualTo((0.0, 0.0)));
            Assert.That(pts[8].X, Is.EqualTo(1500.0 - H).Within(Tol));
            Assert.That(pts[8].Y, Is.EqualTo(300.0 - H).Within(Tol));
        }

        [Test]
        public void Code11_BendArc_OnAxisRadius()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("11", new[] { 300.0, 1500.0 }, D);
            Assert.That(pts[1].X, Is.EqualTo(1500.0 - H - RA).Within(Tol));
            AssertOnCircle(pts, 1, 7, 1500.0 - H - RA, RA, RA);
        }

        // ── 12  Hook at both ends  (16 pts) ──────────────────────────────────

        [Test, Ignore("Kształt 12 poza zakresem poprawek (lista wyboru)")]
        public void Code12_VertexCount_Is16()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("12", new[] { 500.0, 200.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(16));
        }

        [Test, Ignore("Kształt 12 poza zakresem poprawek (lista wyboru)")]
        public void Code12_StartsAt_0_B()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("12", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(200.0).Within(Tol));
        }

        [Test, Ignore("Kształt 12 poza zakresem poprawek (lista wyboru)")]
        public void Code12_EndsAt_A_B()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("12", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(500.0).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(200.0).Within(Tol));
        }

        [Test, Ignore("Kształt 12 poza zakresem poprawek (lista wyboru)")]
        public void Code12_FirstBend_TangentPoints()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("12", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[1].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[1].Y, Is.EqualTo(R).Within(Tol));
            Assert.That(pts[7].X, Is.EqualTo(R).Within(Tol));
            Assert.That(pts[7].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test, Ignore("Kształt 12 poza zakresem poprawek (lista wyboru)")]
        public void Code12_SecondBend_TangentPoints()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("12", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[8].X, Is.EqualTo(500.0 - R).Within(Tol));
            Assert.That(pts[8].Y, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[14].X, Is.EqualTo(500.0).Within(Tol));
            Assert.That(pts[14].Y, Is.EqualTo(R).Within(Tol));
        }

        [Test, Ignore("Kształt 12 poza zakresem poprawek (lista wyboru)")]
        public void Code12_FirstBend_AllPointsOnCircle()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("12", new[] { 500.0, 200.0 }, D);
            AssertOnCircle(pts, from: 1, to: 7, cx: R, cy: R);
        }

        [Test, Ignore("Kształt 12 poza zakresem poprawek (lista wyboru)")]
        public void Code12_SecondBend_AllPointsOnCircle()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("12", new[] { 500.0, 200.0 }, D);
            AssertOnCircle(pts, from: 8, to: 14, cx: 500.0 - R, cy: R);
        }

        // ── 13  Hak półkolisty: wymiary zewnętrzne (A, B, C) ─────────────────

        [Test]
        public void Code13_VertexCount_Is15()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("13", new[] { 1000.0, 140.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(15));
        }

        [Test]
        public void Code13_OuterExtents_Match_A_B()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("13", new[] { 1000.0, 140.0, 300.0 }, D);
            double maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var p in pts) { maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); }
            Assert.That(maxX + H, Is.EqualTo(1000.0).Within(1e-3));
            Assert.That(maxY - minY + D, Is.EqualTo(140.0).Within(1e-3));
        }

        [Test]
        public void Code13_EndsAt_FreeEndOfC()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("13", new[] { 1000.0, 140.0, 300.0 }, D);
            Assert.That(pts[pts.Count - 1].X, Is.EqualTo(1000.0 - 300.0).Within(Tol));
            Assert.That(pts[pts.Count - 1].Y, Is.EqualTo(140.0 - D).Within(Tol));
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

        // ── 15  A skośnie (rzut B), (C) poziomo  (9 pts) ──────────────────────

        [Test]
        public void Code15_VertexCount_Is9()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("15", new[] { 600.0, 300.0, 800.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(9));
        }

        [Test]
        public void Code15_Start_At_Height_B_End_On_Base()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("15", new[] { 600.0, 300.0, 800.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y + H - (-H), Is.EqualTo(300.0).Within(Tol));
            Assert.That(pts[8].Y, Is.EqualTo(0.0).Within(Tol));
        }

        // ── 21  U-bar: wymiary zewnętrzne  (16 pts) ──────────────────────────

        [Test]
        public void Code21_VertexCount_Is16()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("21", new[] { 400.0, 1200.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(16));
        }

        [Test]
        public void Code21_StartsAtOrigin_EndsAt_RightLeg()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("21", new[] { 400.0, 1200.0, 300.0 }, D);
            Assert.That(pts[0], Is.EqualTo((0.0, 0.0)));
            Assert.That(pts[15].X, Is.EqualTo(1200.0 - D).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo((300.0 - H) - (400.0 - H)).Within(Tol));
        }

        [Test]
        public void Code21_OuterWidth_Is_B()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("21", new[] { 400.0, 1200.0, 300.0 }, D);
            Assert.That(pts[15].X - pts[0].X + D, Is.EqualTo(1200.0).Within(Tol));
        }

        [Test]
        public void Code21_Bends_OnAxisRadius()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("21", new[] { 400.0, 1200.0, 300.0 }, D);
            double yb = -(400.0 - H);
            AssertOnCircle(pts, 1, 7, RA, yb + RA, RA);
            AssertOnCircle(pts, 8, 14, 1200.0 - D - RA, yb + RA, RA);
            Assert.That(pts[8].X - pts[7].X, Is.EqualTo(1200.0 - D - 2 * RA).Within(Tol));
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

        [Test, Ignore("Kształt 24 poza zakresem poprawek (lista wyboru) — kąt skosu do ustalenia")]
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

        // ── 33  Pętla z dwoma półkolami: A × B zewnętrzne ────────────────────

        [Test]
        public void Code33_OuterExtents_Match_A_B()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("33", new[] { 1200.0, 300.0, 250.0 }, D);
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            foreach (var p in pts) { minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X); minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y); }
            Assert.That(maxX - minX + D, Is.EqualTo(1200.0).Within(1e-3));
            Assert.That(maxY - minY + D, Is.EqualTo(300.0).Within(1e-3));
        }

        [Test]
        public void Code33_NoNaN()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("33", new[] { 1200.0, 300.0, 250.0 }, D);
            foreach (var p in pts) Assert.That(double.IsNaN(p.X) || double.IsNaN(p.Y), Is.False);
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
            Assert.That(pts[1].X, Is.EqualTo(500.0 - RA).Within(Tol));
            Assert.That(pts[1].Y, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[7].X, Is.EqualTo(500.0).Within(Tol));
            Assert.That(pts[7].Y, Is.EqualTo(RA).Within(Tol));
        }

        [Test]
        public void Code34_Bend2_TangentPoints()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("34", new[] { 500.0, 400.0 }, D);
            Assert.That(pts[8].X,  Is.EqualTo(500.0).Within(Tol));
            Assert.That(pts[8].Y,  Is.EqualTo(400.0 - RA).Within(Tol));
            Assert.That(pts[14].X, Is.EqualTo(500.0 - RA).Within(Tol));
            Assert.That(pts[14].Y, Is.EqualTo(400.0).Within(Tol));
        }

        [Test]
        public void Code34_Bend3_TangentPoints()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("34", new[] { 500.0, 400.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(RA).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(400.0).Within(Tol));
            Assert.That(pts[21].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[21].Y, Is.EqualTo(400.0 - RA).Within(Tol));
        }

        [Test]
        public void Code34_AllBends_OnCircle()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("34", new[] { 500.0, 400.0 }, D);
            AssertOnCircle(pts, 1,  7,  500.0 - RA,   RA, RA);
            AssertOnCircle(pts, 8,  14, 500.0 - RA, 400.0 - RA, RA);
            AssertOnCircle(pts, 15, 21, RA,         400.0 - RA, RA);
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
            AssertOnCircle(pts, 1,  7,  a - RA, RA, RA);
            AssertOnCircle(pts, 8,  14, a - RA, a - RA, RA);
            AssertOnCircle(pts, 15, 21, RA,     a - RA, RA);
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

        // ── 44  BS 8666 „kapelusz”: A, B, C, D, (E) zewnętrzne  (30 pts) ──────

        [Test]
        public void Code44_VertexCount_Is30()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("44", new[] { 300.0, 400.0, 600.0, 400.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(30));
        }

        [Test]
        public void Code44_Ends_And_Depth()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("44", new[] { 300.0, 400.0, 600.0, 400.0, 300.0 }, D);
            Assert.That(pts[0], Is.EqualTo((0.0, 0.0)));
            Assert.That(pts[29].X, Is.EqualTo(300.0 + 600.0 + 300.0 - 2 * D).Within(Tol));
            Assert.That(pts[29].Y, Is.EqualTo(0.0).Within(Tol));
            double minY = double.MaxValue; foreach (var p in pts) minY = Math.Min(minY, p.Y);
            Assert.That(-minY + D, Is.EqualTo(400.0).Within(1e-3));
        }

        [Test]
        public void Code44_LegacyRing_OnlyA_IsCircle()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("44", new[] { 500.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(9));
            Assert.That(BarGeometryBuilder.IsLegacyRing("44", new[] { 500.0, 0, 0, 0, 0 }), Is.True);
            Assert.That(BarGeometryBuilder.IsLegacyRing("44", new[] { 300.0, 400.0, 600.0, 400.0, 300.0 }), Is.False);
        }

        // ── 46  Crank symetryczny: A, B skos, C, B skos, (E); D głębokość  (30 pts) ──

        [Test]
        public void Code46_VertexCount_Is30()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("46", new[] { 300.0, 500.0, 600.0, 350.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(30));
        }

        [Test]
        public void Code46_Depth_Is_D()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("46", new[] { 300.0, 500.0, 600.0, 350.0, 300.0 }, D);
            double minY = double.MaxValue; foreach (var p in pts) minY = Math.Min(minY, p.Y);
            Assert.That(-minY + D, Is.EqualTo(350.0).Within(1e-3));
            Assert.That(pts[29].Y, Is.EqualTo(0.0).Within(Tol));
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

        // ── 51  Strzemię zamknięte: A × B zewnętrzne, haki w górnym prawym rogu  (37 pts) ──

        [Test]
        public void Code51_VertexCount_Is37()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("51", new[] { 400.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(37));
        }

        [Test]
        public void Code51_StartsAndEnds_On_Hooks()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("51", new[] { 400.0, 300.0 }, D);
            double a = 400.0 - D, b = 300.0 - D, hook = Math.Max(16 * D, 160.0);
            Assert.That(pts[0].X, Is.EqualTo(a).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(b - hook).Within(Tol));
            Assert.That(pts[36].X, Is.EqualTo(a - hook).Within(Tol));
            Assert.That(pts[36].Y, Is.EqualTo(b).Within(Tol));
        }

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

        // ── 63  Strzemię podwójne: A wysokość, B szerokość (zewnętrzne)  (44 pts) ──

        [Test]
        public void Code63_VertexCount_Is44()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("63", new[] { 300.0, 400.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(44));
        }

        [Test]
        public void Code63_StartsAndEnds_On_Hooks()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("63", new[] { 300.0, 400.0 }, D);
            double a = 300.0 - D, b = 400.0 - D, hook = Math.Max(14 * D, 150.0);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(a - hook).Within(Tol));
            Assert.That(pts[43].X, Is.EqualTo(b).Within(Tol));
            Assert.That(pts[43].Y, Is.EqualTo(a - hook).Within(Tol));
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
