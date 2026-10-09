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

        /// <summary>
        /// Geometria osi w umowie OSI (wymiary = oś) — wariant etapu W4d (BarGeometryOptions.W4d). Od W4e (poprawka #13)
        /// geometria bieżąca przelicza wymiary zewnętrzne na oś (BarDimensionRules) — testy W4e: BarGeometryW4eTests.
        /// </summary>
        private static List<(double X, double Y)> Geo(string code, double[] pv, double d)
            => BarGeometryBuilder.GetLocalPoints(code, pv, d, BarGeometryOptions.W4d);

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
            Assert.That(Geo("00", new[] { 3000.0 }, D).Count, Is.EqualTo(2));

        [Test]
        public void Code00_StartsAtOrigin()
        {
            var pts = Geo("00", new[] { 3000.0 }, D);
            Assert.That(pts[0], Is.EqualTo((0.0, 0.0)));
        }

        [Test]
        public void Code00_EndsAtA()
        {
            var pts = Geo("00", new[] { 3000.0 }, D);
            Assert.That(pts[1].X, Is.EqualTo(3000.0).Within(Tol));
            Assert.That(pts[1].Y, Is.EqualTo(0.0).Within(Tol));
        }

        // ── 11  90° hook at one end  (9 pts) ─────────────────────────────────

        [Test]
        public void Code11_VertexCount_Is9()
        {
            var pts = Geo("11", new[] { 500.0, 200.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(9));
        }

        [Test]
        public void Code11_StartsAtOrigin()
        {
            var pts = Geo("11", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[0], Is.EqualTo((0.0, 0.0)));
        }

        [Test]
        public void Code11_EndsAt_A_B()
        {
            var pts = Geo("11", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[8].X, Is.EqualTo(500.0).Within(Tol));
            Assert.That(pts[8].Y, Is.EqualTo(200.0).Within(Tol));
        }

        [Test]
        public void Code11_ArcStartTangent_Is_Ar_0()
        {
            var pts = Geo("11", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[1].X, Is.EqualTo(500.0 - R).Within(Tol));
            Assert.That(pts[1].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code11_ArcEndTangent_Is_A_r()
        {
            var pts = Geo("11", new[] { 500.0, 200.0 }, D);
            Assert.That(pts[7].X, Is.EqualTo(500.0).Within(Tol));
            Assert.That(pts[7].Y, Is.EqualTo(R).Within(Tol));
        }

        [Test]
        public void Code11_BendArc_AllPointsOnCircle()
        {
            var pts = Geo("11", new[] { 500.0, 200.0 }, D);
            AssertOnCircle(pts, from: 1, to: 7, cx: 500.0 - R, cy: R);
        }

        // ── 12  L z łukiem o promieniu R (W4d-2: BS 8666:2020, R = trzeci parametr)  (9 pts) ──
        // {A 500, B 300, R 100}: (0,0) → (500,0) → (500,300), łuk R = 100: styczne (400,0) i (500,100), środek (400,100)

        [Test]
        public void Code12_VertexCount_Is9()
        {
            var pts = Geo("12", new[] { 500.0, 300.0, 100.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(9));
        }

        [Test]
        public void Code12_StartsAtOrigin()
        {
            var pts = Geo("12", new[] { 500.0, 300.0, 100.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code12_EndsAt_A_B()
        {
            var pts = Geo("12", new[] { 500.0, 300.0, 100.0 }, D);
            Assert.That(pts[8].X, Is.EqualTo(500.0).Within(Tol));
            Assert.That(pts[8].Y, Is.EqualTo(300.0).Within(Tol));
        }

        [Test]
        public void Code12_Bend_TangentPoints_R()
        {
            var pts = Geo("12", new[] { 500.0, 300.0, 100.0 }, D);
            Assert.That(pts[1].X, Is.EqualTo(400.0).Within(Tol));
            Assert.That(pts[1].Y, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[7].X, Is.EqualTo(500.0).Within(Tol));
            Assert.That(pts[7].Y, Is.EqualTo(100.0).Within(Tol));
        }

        [Test]
        public void Code12_Bend_AllPointsOnCircle_R()
        {
            var pts = Geo("12", new[] { 500.0, 300.0, 100.0 }, D);
            AssertOnCircle(pts, from: 1, to: 7, cx: 400.0, cy: 100.0, r: 100.0);
        }

        [Test]
        public void Code12_R0_UsesMinBendRadius()
        {
            // R ≤ 0 → minimalny promień gięcia (Table 2: H12 → 24)
            var pts = Geo("12", new[] { 500.0, 300.0, 0.0 }, D);
            AssertOnCircle(pts, from: 1, to: 7, cx: 500.0 - R, cy: R);
        }

        [Test]
        public void Code12_Formula_750()
        {
            // A + (B) − 0.43R − 1.2d = 500 + 300 − 43 − 14.4 = 742.6 → 750
            Assert.That(ShapeCodeLibrary.Get("12")!.CalculateTotalLength(new[] { 500.0, 300.0, 100.0 }, D), Is.EqualTo(750.0));
        }

        // ── 13  Hairpin 180° (15 pts: start, styczna, 12 pkt półkola, koniec) ─

        [Test]
        public void Code13_VertexCount_Is15()
        {
            var pts = Geo("13", new[] { 500.0, 200.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(15));
        }

        [Test]
        public void Code13_StartsAtOrigin()
        {
            var pts = Geo("13", new[] { 500.0, 200.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code13_EndsAt_AminusC_B()
        {
            // Koniec krótkiej (górnej) nogi: (A − C, B)
            var pts = Geo("13", new[] { 500.0, 200.0, 300.0 }, D);
            Assert.That(pts[14].X, Is.EqualTo(200.0).Within(Tol));
            Assert.That(pts[14].Y, Is.EqualTo(200.0).Within(Tol));
        }

        // ── 14  (W4d-2) A skośne, C poziome WSTECZ; kąt z B i D (brak → 45°)  (9 pts) ──

        [Test]
        public void Code14_VertexCount_Is9()
        {
            var pts = Geo("14", new[] { 500.0, 300.0, 300.0, 400.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(9));
        }

        [Test]
        public void Code14_StartsAt_FreeEndOfA()
        {
            // θ = atan2(B 300, D 400): A·sinθ = 300, A·cosθ = 400 → start (0, 300), zagięcie (400, 0)
            var pts = Geo("14", new[] { 500.0, 300.0, 300.0, 400.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(300.0).Within(Tol));
        }

        [Test]
        public void Code14_EndsAt_C_Back()
        {
            // C poziome wstecz od zagięcia: (400 − 300, 0)
            var pts = Geo("14", new[] { 500.0, 300.0, 300.0, 400.0 }, D);
            Assert.That(pts[8].X, Is.EqualTo(100.0).Within(Tol));
            Assert.That(pts[8].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code14_NoD_Default45()
        {
            double a = 500.0, c = 300.0;
            var pts = Geo("14", new[] { a, 200.0, c, 0.0 }, D);
            Assert.That(pts[0].Y, Is.EqualTo(a * Cos45).Within(Tol));
            Assert.That(pts[8].X, Is.EqualTo(a * Cos45 - c).Within(Tol));
            Assert.That(pts[8].Y, Is.EqualTo(0.0).Within(Tol));
        }

        // ── 15  (W4d-1) A skośne, C poziome dalej; kąt z B i D (brak → 45°)  (9 pts) ──

        [Test]
        public void Code15_VertexCount_Is9()
        {
            var pts = Geo("15", new[] { 500.0, 300.0, 300.0, 400.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(9));
        }

        [Test]
        public void Code15_StartsAt_FreeEndOfA()
        {
            var pts = Geo("15", new[] { 500.0, 300.0, 300.0, 400.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(300.0).Within(Tol));
        }

        [Test]
        public void Code15_EndsAt_BendPlusC()
        {
            var pts = Geo("15", new[] { 500.0, 300.0, 300.0, 400.0 }, D);
            Assert.That(pts[8].X, Is.EqualTo(700.0).Within(Tol));
            Assert.That(pts[8].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code15_NoD_Default45()
        {
            double a = 500.0, c = 300.0;
            var pts = Geo("15", new[] { a, 200.0, c, 0.0 }, D);
            Assert.That(pts[0].Y, Is.EqualTo(a * Cos45).Within(Tol));
            Assert.That(pts[8].X, Is.EqualTo(a * Cos45 + c).Within(Tol));
        }

        // ── 21  U-bar  (16 pts) ───────────────────────────────────────────────

        [Test]
        public void Code21_VertexCount_Is16()
        {
            var pts = Geo("21", new[] { 400.0, 300.0, 350.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(16));
        }

        [Test]
        public void Code21_StartsAtOrigin()
        {
            var pts = Geo("21", new[] { 400.0, 300.0, 350.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code21_EndsAt_B_CminusA()
        {
            var pts = Geo("21", new[] { 400.0, 300.0, 350.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(300.0).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(350.0 - 400.0).Within(Tol));
        }

        [Test]
        public void Code21_BottomTangentPoints_BothOnMinusA()
        {
            double a   = 400.0;
            var pts    = Geo("21", new[] { a, 300.0, 400.0 }, D);
            Assert.That(pts[7].Y,  Is.EqualTo(-a).Within(Tol));
            Assert.That(pts[8].Y,  Is.EqualTo(-a).Within(Tol));
        }

        [Test]
        public void Code21_AsymmetricLegs()
        {
            var pts = Geo("21", new[] { 500.0, 200.0, 300.0 }, D);
            Assert.That(pts[0].Y,  Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(300.0 - 500.0).Within(Tol));
        }

        [Test]
        public void Code21_BottomStraightSection_Length_Is_B_minus_2r()
        {
            double b   = 300.0;
            var pts    = Geo("21", new[] { 400.0, b, 400.0 }, D);
            double len = pts[8].X - pts[7].X;
            Assert.That(len, Is.EqualTo(b - 2 * R).Within(Tol));
        }

        [Test]
        public void Code21_FirstBend_AllPointsOnCircle()
        {
            double a   = 400.0;
            var pts    = Geo("21", new[] { a, 300.0, 350.0 }, D);
            AssertOnCircle(pts, from: 1, to: 7, cx: R, cy: -a + R);
        }

        [Test]
        public void Code21_SecondBend_AllPointsOnCircle()
        {
            double a   = 400.0;
            var pts    = Geo("21", new[] { a, 300.0, 350.0 }, D);
            AssertOnCircle(pts, from: 8, to: 14, cx: 300.0 - R, cy: -a + R);
        }

        // ── 22  (W4d-2) A – B – półkole C – D  (22 pts) ──────────────────────
        // {A 400, B 600, C 150, D 300}: start (0,−400), narożnik (0,0) łuk r 24, półkole r 75 środek (525,−75), koniec (300,−150)

        [Test]
        public void Code22_VertexCount_Is22()
        {
            var pts = Geo("22", new[] { 400.0, 600.0, 150.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(22));
        }

        [Test]
        public void Code22_StartsAt_0_minusA()
        {
            var pts = Geo("22", new[] { 400.0, 600.0, 150.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(-400.0).Within(Tol));
        }

        [Test]
        public void Code22_EndsAt_BminusD_minusC()
        {
            var pts = Geo("22", new[] { 400.0, 600.0, 150.0, 300.0 }, D);
            Assert.That(pts[21].X, Is.EqualTo(300.0).Within(Tol));
            Assert.That(pts[21].Y, Is.EqualTo(-150.0).Within(Tol));
        }

        [Test]
        public void Code22_SemiCircle_Center_525_minus75()
        {
            var pts = Geo("22", new[] { 400.0, 600.0, 150.0, 300.0 }, D);
            AssertOnCircle(pts, from: 8, to: 20, cx: 525.0, cy: -75.0, r: 75.0);
            AssertOnCircle(pts, from: 1, to: 7, cx: R, cy: -R);   // narożnik (0,0)
        }

        // ── 23  Z-bar  (16 pts) ───────────────────────────────────────────────

        [Test]
        public void Code23_VertexCount_Is16()
        {
            var pts = Geo("23", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(16));
        }

        [Test]
        public void Code23_StartsAtOrigin()
        {
            var pts = Geo("23", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code23_EndsAt_ApC_B()
        {
            var pts = Geo("23", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(800.0).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(400.0).Within(Tol));
        }

        // ── 24  Crank łagodny – ta sama geometria co 23  (16 pts) ─────────────

        [Test]
        public void Code24_VertexCount_Is16()
        {
            var pts = Geo("24", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(16));
        }

        [Test]
        public void Code24_StartsAtOrigin()
        {
            var pts = Geo("24", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code24_EndsAt_Theta30()
        {
            // W4d-2: A poziome, B skos 30° (brak D/E), C pionowe: (500 + 400·cos30, 400·sin30 + 300) = (846.41, 500)
            var pts = Geo("24", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(500.0 + 400.0 * Math.Cos(Math.PI / 6)).Within(Tol));
            Assert.That(pts[15].X, Is.EqualTo(846.41).Within(0.005));
            Assert.That(pts[15].Y, Is.EqualTo(500.0).Within(Tol));
        }

        [Test]
        public void Code24_AngleFrom_D_E()
        {
            // θ = atan2(D 240, E 320): B 400 → rzut (320, 240); koniec (820, 540)
            var pts = Geo("24", new[] { 500.0, 400.0, 300.0, 240.0, 320.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(820.0).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(540.0).Within(Tol));
        }

        // ── 25  Hook + crank  (23 pts) ────────────────────────────────────────

        [Test]
        public void Code25_VertexCount_Is16()
        {
            // W4a-3: 25 {A,B,E} — E = paramValues[2]; C = 0 → odcinek zerowy usunięty (W4a-2), 2 łuki
            var pts = Geo("25", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(16));
            foreach (var p in pts) Assert.That(double.IsNaN(p.X) || double.IsNaN(p.Y), Is.False);
        }

        [Test]
        public void Code25_StartsAtOrigin()
        {
            var pts = Geo("25", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code25_EndsAt_45deg()
        {
            // W4a-3: ostatnie ramię 45° = E (po nazwie), C = 0: koniec (A + E·cos45, B + E·sin45)
            double a = 500.0, b = 400.0, e = 300.0;
            var pts = Geo("25", new[] { a, b, e }, D);
            Assert.That(pts[15].X, Is.EqualTo(a + e * Cos45).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(b + e * Cos45).Within(Tol));
        }

        // ── 26  Hook + leg  (16 pts) ──────────────────────────────────────────

        [Test]
        public void Code26_VertexCount_Is16()
        {
            var pts = Geo("26", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(16));
        }

        [Test]
        public void Code26_StartsAtOrigin()
        {
            var pts = Geo("26", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code26_EndsAt_ApC_minusB()
        {
            var pts = Geo("26", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(800.0).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(-400.0).Within(Tol));
        }

        // ── 27  Crank z hakiem górnym  (23 pts) ───────────────────────────────

        [Test]
        public void Code27_VertexCount_Is23()
        {
            var pts = Geo("27", new[] { 300.0, 200.0, 400.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code27_StartsAtOrigin()
        {
            var pts = Geo("27", new[] { 300.0, 200.0, 400.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code27_EndsAt_ApBpC_BpR()
        {
            double a = 300.0, b = 200.0, c = 400.0;
            var pts = Geo("27", new[] { a, b, c }, D);
            Assert.That(pts[22].X, Is.EqualTo(a + b + c).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(b + R).Within(Tol));
        }

        // ── 28  Crank z hakiem dolnym  (23 pts) ───────────────────────────────

        [Test]
        public void Code28_VertexCount_Is23()
        {
            var pts = Geo("28", new[] { 300.0, 200.0, 400.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code28_StartsAtOrigin()
        {
            var pts = Geo("28", new[] { 300.0, 200.0, 400.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code28_EndsAt_ApBpC_BminusR()
        {
            double a = 300.0, b = 200.0, c = 400.0;
            var pts = Geo("28", new[] { a, b, c }, D);
            Assert.That(pts[22].X, Is.EqualTo(a + b + c).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(b - R).Within(Tol));
        }

        // ── 29  Crank symetryczny  (23 pts) ───────────────────────────────────

        [Test]
        public void Code29_VertexCount_Is23()
        {
            var pts = Geo("29", new[] { 300.0, 200.0, 400.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code29_StartsAt_0_minusR()
        {
            var pts = Geo("29", new[] { 300.0, 200.0, 400.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(-R).Within(Tol));
        }

        [Test]
        public void Code29_EndsAt_ApBpC_B()
        {
            double a = 300.0, b = 200.0, c = 400.0;
            var pts = Geo("29", new[] { a, b, c }, D);
            Assert.That(pts[22].X, Is.EqualTo(a + b + c).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(b).Within(Tol));
        }

        // ── 31  Z + hook 45°  (23 pts) ────────────────────────────────────────

        [Test]
        public void Code31_VertexCount_Is23()
        {
            // W4b: 31 {A,B,C,D} — D dopisane na końcu parametrów
            var pts = Geo("31", new[] { 400.0, 300.0, 200.0, 150.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code31_WithoutD_NoNaN()
        {
            // 31 bez D (stary rekord): odcinek zerowy bez łuku i bez NaN (W4a-2)
            var pts = Geo("31", new[] { 400.0, 300.0, 200.0, 0.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(16));
            foreach (var p in pts) Assert.That(double.IsNaN(p.X) || double.IsNaN(p.Y), Is.False);
        }

        [Test]
        public void Code31_StartsAtOrigin()
        {
            var pts = Geo("31", new[] { 400.0, 300.0, 200.0, 150.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code31_EndsAt_45deg()
        {
            double a = 400.0, b = 300.0, c = 200.0, d = 150.0;
            var pts = Geo("31", new[] { a, b, c, d }, D);
            Assert.That(pts[22].X, Is.EqualTo(a + c + d * Cos45).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(b + d * Cos45).Within(Tol));
        }

        // ── 32  S-shape  (23 pts) ─────────────────────────────────────────────

        [Test]
        public void Code32_VertexCount_Is23()
        {
            var pts = Geo("32", new[] { 400.0, 300.0, 200.0, 150.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code32_StartsAtOrigin()
        {
            var pts = Geo("32", new[] { 400.0, 300.0, 200.0, 150.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code32_EndsAt_ApC_minusD()
        {
            double a = 400.0, c = 200.0, d = 150.0;
            var pts = Geo("32", new[] { a, 300.0, c, d }, D);
            Assert.That(pts[22].X, Is.EqualTo(a + c).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(-d).Within(Tol));
        }

        // ── 33  (W4d-1) „bieżnia”: półkola średnicy B, długość A, zakład C  (41 pts) ──
        // {A 900, B 200, C 300}: start (600,200), półkola r 100 — prawe środek (800,100), lewe (100,100); koniec (600,0)

        [Test]
        public void Code33_VertexCount_Is41()
        {
            var pts = Geo("33", new[] { 900.0, 200.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(41));
        }

        [Test]
        public void Code33_StartAndEnd_AtLap()
        {
            var pts = Geo("33", new[] { 900.0, 200.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(600.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(200.0).Within(Tol));
            Assert.That(pts[40].X, Is.EqualTo(600.0).Within(Tol));
            Assert.That(pts[40].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code33_SemiCircles_OnCircles()
        {
            var pts = Geo("33", new[] { 900.0, 200.0, 300.0 }, D);
            AssertOnCircle(pts, from: 1, to: 13, cx: 800.0, cy: 100.0, r: 100.0);
            AssertOnCircle(pts, from: 14, to: 26, cx: 100.0, cy: 100.0, r: 100.0);
            AssertOnCircle(pts, from: 27, to: 39, cx: 800.0, cy: 100.0, r: 100.0);
            Assert.That(pts[20].X, Is.EqualTo(0.0).Within(Tol));   // lewa skrajnia
            Assert.That(pts[7].X, Is.EqualTo(900.0).Within(Tol));  // prawa skrajnia = A
        }

        // ── 34  Closed rectangle  (23 pts) ───────────────────────────────────

        [Test]
        public void Code34_VertexCount_Is23()
        {
            var pts = Geo("34", new[] { 500.0, 400.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code34_IsClosed()
        {
            var pts = Geo("34", new[] { 500.0, 400.0 }, D);
            Assert.That(pts[22].X, Is.EqualTo(pts[0].X).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(pts[0].Y).Within(Tol));
        }

        [Test]
        public void Code34_StartsAndEndsAtOrigin()
        {
            var pts = Geo("34", new[] { 500.0, 400.0 }, D);
            Assert.That(pts[0],  Is.EqualTo((0.0, 0.0)));
            Assert.That(pts[22].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code34_Bend1_TangentPoints()
        {
            var pts = Geo("34", new[] { 500.0, 400.0 }, D);
            Assert.That(pts[1].X, Is.EqualTo(500.0 - R).Within(Tol));
            Assert.That(pts[1].Y, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[7].X, Is.EqualTo(500.0).Within(Tol));
            Assert.That(pts[7].Y, Is.EqualTo(R).Within(Tol));
        }

        [Test]
        public void Code34_Bend2_TangentPoints()
        {
            var pts = Geo("34", new[] { 500.0, 400.0 }, D);
            Assert.That(pts[8].X,  Is.EqualTo(500.0).Within(Tol));
            Assert.That(pts[8].Y,  Is.EqualTo(400.0 - R).Within(Tol));
            Assert.That(pts[14].X, Is.EqualTo(500.0 - R).Within(Tol));
            Assert.That(pts[14].Y, Is.EqualTo(400.0).Within(Tol));
        }

        [Test]
        public void Code34_Bend3_TangentPoints()
        {
            var pts = Geo("34", new[] { 500.0, 400.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(R).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(400.0).Within(Tol));
            Assert.That(pts[21].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[21].Y, Is.EqualTo(400.0 - R).Within(Tol));
        }

        [Test]
        public void Code34_AllBends_OnCircle()
        {
            var pts = Geo("34", new[] { 500.0, 400.0 }, D);
            AssertOnCircle(pts, 1,  7,  500.0 - R,   R);
            AssertOnCircle(pts, 8,  14, 500.0 - R, 400.0 - R);
            AssertOnCircle(pts, 15, 21, R,         400.0 - R);
        }

        // ── 35  Closed square  (23 pts) ──────────────────────────────────────

        [Test]
        public void Code35_VertexCount_Is23()
        {
            var pts = Geo("35", new[] { 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code35_IsClosed()
        {
            var pts = Geo("35", new[] { 300.0 }, D);
            Assert.That(pts[22].X, Is.EqualTo(pts[0].X).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(pts[0].Y).Within(Tol));
        }

        [Test]
        public void Code35_AllBends_OnCircle()
        {
            double a   = 300.0;
            var pts    = Geo("35", new[] { a }, D);
            AssertOnCircle(pts, 1,  7,  a - R, R);
            AssertOnCircle(pts, 8,  14, a - R, a - R);
            AssertOnCircle(pts, 15, 21, R,     a - R);
        }

        [Test]
        public void Code35_BothAxesSymmetric()
        {
            double a   = 400.0;
            var pts    = Geo("35", new[] { a }, D);
            double d1  = a - pts[1].X;
            double d2  = a - pts[8].Y;
            Assert.That(d1, Is.EqualTo(d2).Within(Tol));
        }

        // ── 36  Prostokąt nierówny  (23 pts) ─────────────────────────────────

        [Test]
        public void Code36_VertexCount_Is23()
        {
            var pts = Geo("36", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code36_StartsAtOrigin()
        {
            var pts = Geo("36", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code36_IsClosed()
        {
            var pts = Geo("36", new[] { 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[22].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(0.0).Within(Tol));
        }

        // ── 41  Wielokąt 4-boczny  (23 pts) ──────────────────────────────────

        [Test]
        public void Code41_VertexCount_Is23()
        {
            var pts = Geo("41", new[] { 400.0, 200.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(23));
        }

        [Test]
        public void Code41_StartsAtOrigin()
        {
            var pts = Geo("41", new[] { 400.0, 200.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code41_IsClosed()
        {
            var pts = Geo("41", new[] { 400.0, 200.0 }, D);
            Assert.That(pts[22].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[22].Y, Is.EqualTo(0.0).Within(Tol));
        }

        // ── 75  Pierścień (W4c; dawniej 44) (9 pts: 8 unikalnych + zamknięcie) ─

        [Test]
        public void Code75Ring_VertexCount_Is9()
        {
            var pts = Geo("75", new[] { 500.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(9));
        }

        [Test]
        public void Code75Ring_StartsAt_A_halfA()
        {
            // angle=0°: (cx+r, cy) = (A/2+A/2, A/2) = (A, A/2)
            double a = 500.0;
            var pts = Geo("75", new[] { a }, D);
            Assert.That(pts[0].X, Is.EqualTo(a).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(a / 2.0).Within(Tol));
        }

        [Test]
        public void Code75Ring_IsClosed()
        {
            var pts = Geo("75", new[] { 500.0 }, D);
            Assert.That(pts[8].X, Is.EqualTo(pts[0].X).Within(Tol));
            Assert.That(pts[8].Y, Is.EqualTo(pts[0].Y).Within(Tol));
        }

        [Test]
        public void Code75Ring_AllPointsOnCircle()
        {
            double a  = 500.0;
            var pts   = Geo("75", new[] { a }, D);
            double cx = a / 2.0, cy = a / 2.0, rad = a / 2.0;
            foreach (var p in pts)
            {
                double dist = Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));
                Assert.That(dist, Is.EqualTo(rad).Within(Tol));
            }
        }

        // ── 46  (W4d-1) A – B skos – C – B skos – E; kąt z D (wysokość skosu)  (30 pts) ──
        // Parametry {A,B,C,E,D}: D dopisane ZA E

        [Test]
        public void Code46_VertexCount_Is30()
        {
            var pts = Geo("46", new[] { 200.0, 300.0, 400.0, 200.0, 150.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(30));
        }

        [Test]
        public void Code46_StartsAtOrigin()
        {
            var pts = Geo("46", new[] { 200.0, 300.0, 400.0, 200.0, 150.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code46_EndsAt_AngleFromD()
        {
            // θ = asin(D 150 / B 300) = 30°: koniec (A + 2B·cos30 + C + E, 0)
            var pts = Geo("46", new[] { 200.0, 300.0, 400.0, 200.0, 150.0 }, D);
            Assert.That(pts[29].X, Is.EqualTo(200.0 + 2 * 300.0 * Math.Cos(Math.PI / 6) + 400.0 + 200.0).Within(Tol));
            Assert.That(pts[29].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code46_NoD_Default45()
        {
            var pts = Geo("46", new[] { 200.0, 300.0, 400.0, 200.0, 0.0 }, D);
            Assert.That(pts[29].X, Is.EqualTo(200.0 + 2 * 300.0 * Cos45 + 400.0 + 200.0).Within(Tol));
        }

        // ── 47  Trójkąt  (16 pts) ─────────────────────────────────────────────

        [Test]
        public void Code47_VertexCount_Is16()
        {
            var pts = Geo("47", new[] { 500.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(16));
        }

        [Test]
        public void Code47_StartsAtOrigin()
        {
            var pts = Geo("47", new[] { 500.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code47_IsClosed()
        {
            var pts = Geo("47", new[] { 500.0, 300.0 }, D);
            Assert.That(pts[15].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[15].Y, Is.EqualTo(0.0).Within(Tol));
        }

        // ── 51  Closed stirrup — jeden otwarty pręt z overlapem w górnym prawym rogu ──
        // W4d-1 (P9): A = WYSOKOŚĆ, B = szerokość. 7 ostrych węzłów, 5 narożników 90° → 1+5×7+1=37 pkt
        // W4b: hook = C_eff = (max(16d,160) + 2.5r + 5d)/2 = 156 dla d=12
        // node0=(B, A−hook)=(300,244); node6=(B−hook, A)=(144,400)

        [Test]
        public void Code51_VertexCount_Is37()
        {
            var pts = Geo("51", new[] { 400.0, 300.0, 0.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(37));
        }

        [Test]
        public void Code51_StartsAt_B_AminusHook()
        {
            var pts = Geo("51", new[] { 400.0, 300.0, 0.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(300.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(244.0).Within(Tol));
        }

        [Test]
        public void Code51_EndsAt_BminusHook_A()
        {
            var pts = Geo("51", new[] { 400.0, 300.0, 0.0 }, D);
            Assert.That(pts[36].X, Is.EqualTo(144.0).Within(Tol));
            Assert.That(pts[36].Y, Is.EqualTo(400.0).Within(Tol));
        }

        [Test]
        public void Code51_ExplicitHook_UsesC()
        {
            // C=210 → hook=210; start=(300, 400−210)=(300,190); end=(300−210, 400)=(90,400)
            var pts = Geo("51", new[] { 400.0, 300.0, 210.0 }, D);
            Assert.That(pts[0].X,  Is.EqualTo(300.0).Within(Tol));
            Assert.That(pts[0].Y,  Is.EqualTo(190.0).Within(Tol));
            Assert.That(pts[36].X, Is.EqualTo(90.0).Within(Tol));
            Assert.That(pts[36].Y, Is.EqualTo(400.0).Within(Tol));
        }

        // ── 51 / 63  Jawny hak (C > 0) ─────────────────────────────────────────

        // ── 56  Complex 5-leg  (30 pts) ───────────────────────────────────────

        [Test]
        public void Code56_VertexCount_Is30()
        {
            var pts = Geo("56", new[] { 400.0, 300.0, 200.0, 150.0, 100.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(30));
        }

        [Test]
        public void Code56_StartsAtOrigin()
        {
            var pts = Geo("56", new[] { 400.0, 300.0, 200.0, 150.0, 100.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code56_EndsAt_ApCpE_BminusD()
        {
            double a = 400.0, b = 300.0, c = 200.0, d = 150.0, e = 100.0;
            var pts = Geo("56", new[] { a, b, c, d, e }, D);
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
            var pts = Geo("63", new[] { 400.0, 300.0, 0.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(44));
        }

        [Test]
        public void Code63_StartsAt_0_Hook()
        {
            // W4b: hook = C_eff = (max(14d,140) + 3r + 6d)/2 = (168+72+72)/2 = 156; lewy hak w dół od góry: start = (0, A − hook)
            double hook = BarShape.Code63EffectiveHook(D);
            Assert.That(hook, Is.EqualTo(156.0).Within(Tol));
            var pts = Geo("63", new[] { 400.0, 300.0, 0.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(400.0 - hook).Within(Tol));
        }

        [Test]
        public void Code63_EndsAt_B_AminusHook()
        {
            // W4b: end = (B, A-hook) = (300, 400-156) = (300, 244)
            double a = 400.0, b = 300.0;
            double hook = BarShape.Code63EffectiveHook(D);
            var pts = Geo("63", new[] { a, b, 0.0 }, D);
            Assert.That(pts[43].X, Is.EqualTo(b).Within(Tol));
            Assert.That(pts[43].Y, Is.EqualTo(a - hook).Within(Tol));
        }

        [Test]
        public void Code63_ExplicitHook_UsesC()
        {
            // C=210 → hook=210; oba haki długości C: start=(0, 400-210)=(0,190); end=(300, 400-210)=(300,190)
            var pts = Geo("63", new[] { 400.0, 300.0, 210.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(44));
            Assert.That(pts[0].X,  Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y,  Is.EqualTo(190.0).Within(Tol));
            Assert.That(pts[43].X, Is.EqualTo(300.0).Within(Tol));
            Assert.That(pts[43].Y, Is.EqualTo(190.0).Within(Tol));
        }

        // ── 64  Complex 5-leg variant – ta sama geometria co 56  (30 pts) ─────

        [Test]
        public void Code64_VertexCount_Is30()
        {
            var pts = Geo("64", new[] { 400.0, 300.0, 200.0, 150.0, 100.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(30));
        }

        [Test]
        public void Code64_StartsAtOrigin()
        {
            var pts = Geo("64", new[] { 400.0, 300.0, 200.0, 150.0, 100.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code64_EndsAt_ApCpE_BminusD()
        {
            double a = 400.0, b = 300.0, c = 200.0, d = 150.0, e = 100.0;
            var pts = Geo("64", new[] { a, b, c, d, e }, D);
            Assert.That(pts[29].X, Is.EqualTo(a + c + e).Within(Tol));
            Assert.That(pts[29].Y, Is.EqualTo(b - d).Within(Tol));
        }

        // ── 44  Kapelusz (W4c, BS 8666:2020)  (30 pts: 6 ostrych, 4 łuki) ────

        [Test]
        public void Code44Hat_VertexCount_Is30()
        {
            var pts = Geo("44", new[] { 500.0, 400.0, 500.0, 400.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(30));
        }

        [Test]
        public void Code44Hat_StartsAtOrigin_EndsAt_ApCpE_0()
        {
            // (0,0) → (A,0) → (A,−B) → (A+C,−B) → (A+C,−B+D) → (A+C+E,−B+D) = (1300, 0)
            var pts = Geo("44", new[] { 500.0, 400.0, 500.0, 400.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[29].X, Is.EqualTo(1300.0).Within(Tol));
            Assert.That(pts[29].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code44Hat_FirstBend_OnCircle()
        {
            // narożnik (500,0), zagięcie w dół (CW): środek (500 − r, −r)
            var pts = Geo("44", new[] { 500.0, 400.0, 500.0, 400.0, 300.0 }, D);
            AssertOnCircle(pts, from: 1, to: 7, cx: 500.0 - R, cy: -R);
        }

        // ── 98 / 99  Custom – prosta linia  ───────────────────────────────────

        [Test]
        public void Code98_VertexCount_Is2()
        {
            var pts = Geo("98", new[] { 2000.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(2));
        }

        [Test]
        public void Code98_StartsAtOrigin_EndsAtA()
        {
            var pts = Geo("98", new[] { 2000.0, 300.0 }, D);
            Assert.That(pts[0].X, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[0].Y, Is.EqualTo(0.0).Within(Tol));
            Assert.That(pts[1].X, Is.EqualTo(2000.0).Within(Tol));
            Assert.That(pts[1].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void Code99_VertexCount_Is2()
        {
            var pts = Geo("99", new[] { 1500.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(2));
        }

        [Test]
        public void Code99_StartsAtOrigin_EndsAtA()
        {
            var pts = Geo("99", new[] { 1500.0 }, D);
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
            var pts = Geo(code, new[] { 2000.0, 300.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(2));
            Assert.That(pts[0], Is.EqualTo((0.0, 0.0)));
            Assert.That(pts[1].X, Is.EqualTo(2000.0).Within(Tol));
            Assert.That(pts[1].Y, Is.EqualTo(0.0).Within(Tol));
        }

        [Test]
        public void NullCode_FallsBackToStraightLine()
        {
            var pts = Geo(null, new[] { 1500.0 }, D);
            Assert.That(pts.Count, Is.EqualTo(2));
        }
    }
}
