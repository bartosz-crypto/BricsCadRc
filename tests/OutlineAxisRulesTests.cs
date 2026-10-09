using System.Globalization;
using BricsCadRc.Core;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// POPRAWKI_WZORCA 25 — osiowość obrysu płyty z tolerancją 4 mm, obliczenia na kopii wyprostowanej;
    /// obrys idealnie osiowy bez zmian (bit w bit).
    /// </summary>
    [TestFixture]
    public class OutlineAxisRulesTests
    {
        // GA P02 plotu 523 (DE736TQ004-DR-GA5230-5231 (Rev P02) - PLOT 523.dxf), LWPOLYLINE 11168C5, SD-PILED-RAFT —
        // krawędź (1103871.99, 48654.020) → (1101509.49, 48654.008): dy = 0,0115 mm
        private static readonly (double X, double Y)[] Plot523 =
        {
            (1101509.489430889, 57694.01998171644),
            (1108411.989430889, 57694.01998171644),
            (1108411.989430889, 47754.01998171644),
            (1103871.989430889, 47754.01998171644),
            (1103871.989430889, 48654.01986008426),
            (1101509.489430889, 48654.00836587109),
        };

        private static void AssertAllEdgesExactlyAxis((double X, double Y)[] p)
        {
            for (int i = 0; i < p.Length; i++)
            {
                var a = p[i]; var b = p[(i + 1) % p.Length];
                Assert.That(a.X == b.X || a.Y == b.Y, Is.True, $"krawędź {i}: ({a.X}, {a.Y}) → ({b.X}, {b.Y})");
            }
        }

        [Test]
        public void Plot523_RealGa_SquaredWithinTolerance()
        {
            var r = OutlineAxisRules.Square(Plot523);
            Assert.That(r.AxisAligned, Is.True);
            Assert.That(r.Changed, Is.True);
            Assert.That(r.MaxDeviation, Is.EqualTo(0.0115).Within(0.0001));
            Assert.That(r.WorstEdge, Is.EqualTo(4));
            AssertAllEdgesExactlyAxis(r.Points);

            // Krawędź z szumem: wspólne Y = średnia (brak wartości dominującej), reszta wierzchołków bit w bit
            double mean = (Plot523[4].Y + Plot523[5].Y) / 2;
            Assert.That(r.Points[4].Y, Is.EqualTo(mean));
            Assert.That(r.Points[5].Y, Is.EqualTo(mean));
            for (int i = 0; i < 4; i++)
            {
                Assert.That(r.Points[i].X, Is.EqualTo(Plot523[i].X));
                Assert.That(r.Points[i].Y, Is.EqualTo(Plot523[i].Y));
            }
            Assert.That(r.Points[4].X, Is.EqualTo(Plot523[4].X));
            Assert.That(r.Points[5].X, Is.EqualTo(Plot523[5].X));
        }

        [Test]
        public void IdealOutline_Unchanged_BitForBit()
        {
            var rect = new[] { (0.1, 0.3), (12000.7, 0.3), (12000.7, 8000.9), (0.1, 8000.9) };
            var r = OutlineAxisRules.Square(rect);
            Assert.That(r.AxisAligned, Is.True);
            Assert.That(r.Changed, Is.False);
            Assert.That(r.MaxDeviation, Is.EqualTo(0.0));
            Assert.That(r.Points, Is.EqualTo(rect));
        }

        [Test]
        public void OldTolerance_NoiseBelow1e3_Unchanged()
        {
            // Dawniej akceptowane (≤ 1e-3) — bez prostowania (wynik jak przed poprawką)
            var p = new[] { (0.0, 0.0), (5000.0, 0.0005), (5000.0, 3000.0), (0.0, 3000.0) };
            var r = OutlineAxisRules.Square(p);
            Assert.That(r.AxisAligned, Is.True);
            Assert.That(r.Changed, Is.False);
            Assert.That(r.Points, Is.EqualTo(p));
        }

        [TestCase(4.0, true)]
        [TestCase(3.99, true)]
        [TestCase(4.01, false)]
        [TestCase(250.0, false)]
        public void Tolerance4mm(double dy, bool aligned)
        {
            var p = new[] { (0.0, 0.0), (5000.0, dy), (5000.0, 3000.0), (0.0, 3000.0) };
            var r = OutlineAxisRules.Square(p);
            Assert.That(r.AxisAligned, Is.EqualTo(aligned));
            if (aligned) AssertAllEdgesExactlyAxis(r.Points);
            else Assert.That(r.Points, Is.EqualTo(p));      // pod kątem — bez zmian
        }

        [Test]
        public void Tolerance4mm_Chain()
        {
            // Każda krawędź ≤ 4 mm od osi, ale łańcuch „schodków” sumuje odchyłkę: rozrzut Y dolnego boku 6 mm → pod kątem
            var p = new[] { (0.0, 0.0), (3000.0, 3.0), (6000.0, 6.0), (6000.0, 3000.0), (0.0, 3000.0) };
            var r = OutlineAxisRules.Square(p);
            Assert.That(r.AxisAligned, Is.False);
            Assert.That(r.MaxDeviation, Is.EqualTo(6.0).Within(1e-9));
            Assert.That(r.Points, Is.EqualTo(p));      // bez zmian
            var ci = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pl-PL");
                string msg = OutlineAxisRules.NotAxisAlignedMessage("[AutoRebar]", r);
                Assert.That(msg, Does.Contain("odchyłka od osi 6 mm > 4 mm"));
                Assert.That(msg, Does.Contain("(0, 0) → (3000, 3)"));
            }
            finally { System.Globalization.CultureInfo.CurrentCulture = ci; }
        }

        [Test]
        public void DominantValue_CollinearChain()
        {
            // Dwie krawędzie poziome jedna za drugą: 2 wierzchołki y = 0, jeden y = 2 → wartość dominująca 0
            var p = new[] { (0.0, 0.0), (3000.0, 2.0), (6000.0, 0.0), (6000.0, 4000.0), (0.0, 4000.0) };
            var r = OutlineAxisRules.Square(p);
            Assert.That(r.AxisAligned && r.Changed, Is.True);
            Assert.That(r.Points[1].Y, Is.EqualTo(0.0));
            Assert.That(r.Points[0].Y, Is.EqualTo(0.0));
            Assert.That(r.Points[2].Y, Is.EqualTo(0.0));
            AssertAllEdgesExactlyAxis(r.Points);
        }

        [Test]
        public void LShape_NoiseOnSeveralEdges_RepeatedClosingVertex()
        {
            // Płyta L z szumem na trzech krawędziach i powtórzonym wierzchołkiem zamykającym (polilinia otwarta z końcem = początek)
            var p = new[]
            {
                (0.0, 0.0), (8000.0, 0.01), (8000.02, 3000.0), (3000.0, 3000.0), (2999.5, 6000.0), (0.0, 6000.0), (0.0, 0.0),
            };
            var r = OutlineAxisRules.Square(p);
            Assert.That(r.AxisAligned, Is.True);
            Assert.That(r.Changed, Is.True);
            Assert.That(r.MaxDeviation, Is.EqualTo(0.5).Within(1e-9));
            AssertAllEdgesExactlyAxis(r.Points);
            Assert.That(r.Points[6], Is.EqualTo(r.Points[0]));
        }

        [Test]
        public void Diagonal_NotAligned_MessageInvariant()
        {
            var saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("pl-PL");
                var p = new[] { (0.0, 0.0), (5000.0, 12.5), (5000.0, 3000.0), (0.0, 3000.0) };
                var r = OutlineAxisRules.Square(p);
                Assert.That(r.AxisAligned, Is.False);
                Assert.That(r.MaxDeviation, Is.EqualTo(12.5));
                string msg = OutlineAxisRules.NotAxisAlignedMessage("[AutoRebar]", r);
                Assert.That(msg, Does.StartWith("[AutoRebar] Etap 1 nie obsługuje płyt pod kątem"));
                Assert.That(msg, Does.Contain("odchyłka od osi 12.5 mm > 4 mm"));
                Assert.That(msg, Does.Contain("(0, 0) → (5000, 12.5)"));
                Assert.That(OutlineAxisRules.SquaredMessage("[AutoRebar]", OutlineAxisRules.Square(Plot523)),
                            Does.Contain("0.0115 mm ≤ 4 mm"));
            }
            finally { CultureInfo.CurrentCulture = saved; }
        }

        [Test]
        public void Degenerate()
        {
            Assert.That(OutlineAxisRules.Square(new (double, double)[0]).AxisAligned, Is.False);
            Assert.That(OutlineAxisRules.Square(null!).AxisAligned, Is.False);
        }
    }
}
