using NUnit.Framework;
using BricsCadRc.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Plan W4 (docs/PLAN_W4_BS8666.md) — geometria kształtów: warianty geometrii (W4a-0, 0-POPRAWKI p.2),
    /// łuki styczne (W4a-1), odcinki zerowe (W4a-2), parametry po nazwie (W4a-3).
    /// </summary>
    [TestFixture]
    public class BarGeometryW4Tests
    {
        private const double D = 12.0;
        private const double R = 24.0;   // MinBendRadius(12)
        private static readonly double Cos45 = Math.Sqrt(2.0) / 2.0;

        /// <summary>Wartości parametrów w kolejności Parameters kształtu, pola spoza kształtu = 0 (jak XData).</summary>
        private static double[] Pv(string code, params double[] vals)
        {
            var shape = ShapeCodeLibrary.Get(code)!;
            var pv = new double[5];
            for (int i = 0; i < shape.Parameters.Length && i < vals.Length; i++) pv[i] = vals[i];
            return pv;
        }

        // ── W4a-1: łuk styczny t = r·tan(φ/2) ────────────────────────────────

        [Test]
        public void W4a1_Bend45_TangentPointsAndCenter()
        {
            // 14 w ostrych punktach etapu W4b (sprzed W4d): (0,0) → (100,0) → ramię 45°; zagięcie 45° w (100,0), r = 24
            var pts = BarGeometryBuilder.GetLocalPoints("14", Pv("14", 100, 200, 0), D, BarGeometryOptions.W4b);
            double t = R * Math.Tan(Math.PI / 8);
            Assert.That(t, Is.EqualTo(9.941).Within(1e-3));
            Assert.That(pts[1].X, Is.EqualTo(90.059).Within(1e-3));
            Assert.That(pts[1].Y, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(pts[7].X, Is.EqualTo(107.029).Within(1e-3));
            Assert.That(pts[7].Y, Is.EqualTo(7.029).Within(1e-3));
            for (int i = 1; i <= 7; i++)
            {
                double dist = Math.Sqrt(Math.Pow(pts[i].X - (100 - t), 2) + Math.Pow(pts[i].Y - R, 2));
                Assert.That(dist, Is.EqualTo(R).Within(1e-9), $"pts[{i}] poza łukiem (środek (90.059; 24))");
            }
        }

        [Test]
        public void W4a1_Bend135_ArcTangentToBothLegs()
        {
            // 15 w ostrych punktach etapu W4b: zagięcie 135° — koniec łuku leży na drugim ramieniu (dawniej załamanie obrysu)
            double a = 500, b = 200;
            var pts = BarGeometryBuilder.GetLocalPoints("15", Pv("15", a, b, 0), D, BarGeometryOptions.W4b);
            var end = pts[7];
            // punkt na prostej z (a,0) w kierunku (−cos45, sin45)
            double cross = (end.X - a) * Cos45 - end.Y * (-Cos45);
            Assert.That(cross, Is.EqualTo(0.0).Within(1e-9));
            Assert.That(pts[1].X, Is.EqualTo(a - R * Math.Tan(3 * Math.PI / 8)).Within(1e-9));
        }

        /// <summary>
        /// Kształty z zagięciami 90°, parametry w naturalnej kolejności: geometria etapu W4d (umowa osi) = sprzed W4 bit w bit.
        /// (Od W4e geometria bieżąca ma wymiary zewnętrzne — BarGeometryW4eTests.)
        /// </summary>
        // 12, 22, 33, 51: W4d — geometria wg rysunków normy (inna niż sprzed W4)
        [TestCase("00")] [TestCase("11")] [TestCase("13")] [TestCase("21")]
        [TestCase("23")] [TestCase("26")] [TestCase("32")] [TestCase("34")] [TestCase("35")]
        [TestCase("56")] [TestCase("64")] [TestCase("98")]   // 63: W4b poprawia długość lewego haka
        [TestCase("99")]
        public void W4a1_RightAngles_BitIdenticalWithLegacy(string code)
        {
            foreach (double d in new[] { 8.0, 10.0, 12.0, 16.0 })
            {
                var pv = Pv(code, 500, 300, 200, 150, 120);
                var cur = BarGeometryBuilder.GetLocalPoints(code, pv, d, BarGeometryOptions.W4d);
                var leg = BarGeometryBuilder.GetLocalPoints(code, pv, d, BarGeometryOptions.Legacy);
                Assert.That(cur.Count, Is.EqualTo(leg.Count), $"{code} d{d}: liczba punktów");
                for (int i = 0; i < cur.Count; i++)
                {
                    Assert.That(cur[i].X, Is.EqualTo(leg[i].X), $"{code} d{d}: pts[{i}].X");
                    Assert.That(cur[i].Y, Is.EqualTo(leg[i].Y), $"{code} d{d}: pts[{i}].Y");
                }
            }
        }

        // ── W4a-2: odcinki zerowe ────────────────────────────────────────────

        [TestCase("25")] [TestCase("31")] [TestCase("27")] [TestCase("56")]
        public void W4a2_ZeroSegments_NoNaN(string code)
        {
            var pv = Pv(code, 500, 300, 0, 0, 0);
            var pts = BarGeometryBuilder.GetLocalPoints(code, pv, D);
            Assert.That(pts.Count, Is.GreaterThanOrEqualTo(2));
            foreach (var p in pts)
                Assert.That(double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y), Is.False);
            var outline = BarFrameRules.LocalOutline(code, pv, D);
            Assert.That(outline, Is.Not.Null);
            foreach (var p in outline!) Assert.That(double.IsNaN(p.X) || double.IsNaN(p.Y), Is.False);
        }

        // ── W4a-3: parametry po nazwie ───────────────────────────────────────

        [Test]
        public void W4a3_IndexOfAndValue_ByName()
        {
            var s25 = ShapeCodeLibrary.Get("25")!;   // {A,B,E}
            Assert.That(s25.IndexOf("E"), Is.EqualTo(2));
            Assert.That(s25.IndexOf("C"), Is.EqualTo(-1));
            Assert.That(s25.Value(new[] { 1.0, 2.0, 3.0 }, "E"), Is.EqualTo(3.0));
            Assert.That(s25.Value(new[] { 1.0, 2.0, 3.0 }, "C"), Is.EqualTo(0.0));
            Assert.That(s25.Value(new[] { 1.0 }, "B"), Is.EqualTo(0.0));
            Assert.That(s25.Has(new[] { 1.0, 2.0, 3.0 }, "E"), Is.True);
            var s12 = ShapeCodeLibrary.Get("12")!;   // {A,B,R}
            Assert.That(s12.Value(new[] { 500.0, 300.0, 100.0 }, "R"), Is.EqualTo(100.0));
        }

        [Test]
        public void W4a3_Shape25_LastLegIsE()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("25", new[] { 500.0, 400.0, 300.0, 0, 0 }, D);
            var last = pts[pts.Count - 1];
            Assert.That(last.X, Is.EqualTo(500 + 300 * Cos45).Within(1e-9));
            Assert.That(last.Y, Is.EqualTo(400 + 300 * Cos45).Within(1e-9));
        }

        // ── W4a-0 / 0-POPRAWKI p.2: rama z obrysu dowolnego wariantu geometrii ──

        [Test]
        public void W4a0_FitCandidates_CurrentFirst_LegacyLast_All16()
        {
            var c = BarGeometryOptions.FitCandidates;
            Assert.That(c.Count, Is.EqualTo(17));   // runda 1: + flaga TableRadius; W4h: + bieżąca z SlopeFromB
            Assert.That(c[0].IsLegacy, Is.False);
            Assert.That(c[0].TangentArcs, Is.EqualTo(BarGeometryOptions.Current.TangentArcs));
            Assert.That(c[c.Count - 1].IsLegacy, Is.True);
            Assert.That(c.Select(o => o.ToString()).Distinct().Count(), Is.EqualTo(17));
        }

        private static List<(double X, double Y)> ToWorld(List<(double X, double Y)> local, BarFrame2 f, double tx, double ty)
        {
            var res = new List<(double X, double Y)>(local.Count);
            foreach (var p in local)
            {
                var w = f.Apply(p.X - local[0].X, p.Y - local[0].Y);
                res.Add((tx + w.X, ty + w.Y));
            }
            return res;
        }

        /// <summary>Obrys każdego wariantu, obrót 30° + odbicie → rama odzyskana (0-POPRAWKI p.2).</summary>
        [TestCase("15", 500, 200, 0)]
        [TestCase("14", 400, 250, 0)]
        [TestCase("11", 500, 300, 0)]
        [TestCase("21", 500, 300, 400)]
        [TestCase("24", 500, 400, 300)]
        [TestCase("51", 400, 300, 0)]
        [TestCase("41", 400, 300, 200)]
        [TestCase("63", 400, 300, 0)]
        public void W4a0_TryFitVariants_RecoversFrame_AllVariants(string code, double a, double b, double c)
        {
            var pv = Pv(code, a, b, c, 150, 100);
            foreach (bool mirrored in new[] { false, true })
            foreach (var opt in BarGeometryOptions.FitCandidates)
            {
                var local = BarFrameRules.LocalOutline(code, pv, D, opt)!;
                var frame = new BarFrame2(Math.PI / 6, mirrored);
                var world = ToWorld(local, frame, 1234.5, -678.9);
                bool ok = BarFrameRules.TryFitVariants(world, code, pv, D, out var got, out _);
                Assert.That(ok, Is.True, $"{code} {opt} mirrored={mirrored}: brak dopasowania");
                Assert.That(got.Angle, Is.EqualTo(Math.PI / 6).Within(1e-9), $"{code} {opt} mirrored={mirrored}: kąt");
                Assert.That(got.Mirrored, Is.EqualTo(mirrored), $"{code} {opt}: odbicie");
            }
        }

        [Test]
        public void W4a0_LegacyOutline15_NotCurrent_ButRecognised()
        {
            var pv = Pv("15", 500, 200, 0);
            var legacy = BarFrameRules.LegacyLocalOutline("15", pv, D)!;
            var current = BarFrameRules.LocalOutline("15", pv, D)!;
            var world = ToWorld(legacy, new BarFrame2(Math.PI / 6, true), 0, 0);
            Assert.That(BarFrameRules.TryFit(world, current, out _, out _), Is.False, "obrys sprzed W4 nie pasuje do bieżącej geometrii");
            Assert.That(BarFrameRules.TryFitVariants(world, "15", pv, D, out var f, out var matched), Is.True);
            Assert.That(matched.IsLegacy || !matched.TangentArcs, Is.True, "dopasowanie wariantem z dawnym łukiem");
            Assert.That(f.Mirrored, Is.True);
        }

        [Test]
        public void W4a0_HorizontalUnmirrored_IdentityExactly()
        {
            var pv = Pv("21", 500, 300, 500);
            var local = BarFrameRules.LocalOutline("21", pv, D)!;
            var world = ToWorld(local, BarFrame2.Identity, 100, 200);
            Assert.That(BarFrameRules.TryFitVariants(world, "21", pv, D, out var f, out _), Is.True);
            Assert.That(f.IsIdentity, Is.True);
        }
    }
}
