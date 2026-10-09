using NUnit.Framework;
using BricsCadRc.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Plan W4 — runda poprawek 1: wariant etapu W4a (promień sprzed Table 2), długość rozkładu Manual wg geometrii
    /// sprzed W4 (0-POPRAWKI p.1), wymiary zestawienia tylko z definicji kształtu.
    /// </summary>
    [TestFixture]
    public class BarW4Round1Tests
    {
        private static readonly double Cos45 = Math.Sqrt(2.0) / 2.0;

        /// <summary>
        /// ZAMROŻONY obrys lokalny pręta 15 H25 {A 500, B 300} z etapu W4a (łuk styczny, r = 3.5d = 87.5) —
        /// współrzędne policzone niezależnie (skrypt poza kodem wtyczki), nie bieżącym generatorem.
        /// </summary>
        private static readonly (double X, double Y)[] FrozenW4a15H25 =
        {
            (0.000000000000, 12.500000000000),
            (288.498000560707, 12.497330696299),
            (317.457570719736, 18.209035061653),
            (341.789321881345, 34.466991411009),
            (358.047278230701, 58.798742572618),
            (363.756313292354, 87.500000000000),
            (358.047278230701, 116.201257427382),
            (341.321525696628, 141.026987431226),
            (279.029130879204, 203.293199591132),
            (296.706800408868, 220.970869120796),
            (359.934787595726, 157.716699276420),
            (381.144266543483, 125.768343236509),
            (388.756313292354, 87.500000000000),
            (381.144266543483, 49.231656763491),
            (359.466991411009, 16.789321881345),
            (327.024656528863, -4.887953251129),
            (289.014626024001, -12.497330696299),
            (0.000000000000, -12.500000000000),
        };

        private static double[] Pv(params double[] v) { var p = new double[5]; Array.Copy(v, p, v.Length); return p; }

        private static List<(double X, double Y)> ToWorld(IReadOnlyList<(double X, double Y)> local, BarFrame2 f, double tx, double ty)
            => local.Select(p => { var w = f.Apply(p.X - local[0].X, p.Y - local[0].Y); return (tx + w.X, ty + w.Y); }).ToList();

        // ── uwaga 2: wariant W4a ──────────────────────────────────────────────

        [Test]
        public void FitCandidates_16_CurrentThenW4bW4a_LegacyLast()
        {
            // W4h: bieżąca = wszystkie flagi (wymiary zewnętrzne, kąt z jednego wymiaru), potem etapy W4e, W4d, W4b–W4c i W4a
            var c = BarGeometryOptions.FitCandidates;
            Assert.That(c.Count, Is.EqualTo(17));
            Assert.That(c[0].SameAs(BarGeometryOptions.Current), Is.True);
            Assert.That(c[0].Bs2020Sharp && c[0].ExternalDims && c[0].SlopeFromB, Is.True);
            Assert.That(c[1].SameAs(BarGeometryOptions.W4e), Is.True);
            Assert.That(c[1].SlopeFromB, Is.False);
            Assert.That(c[2].SameAs(BarGeometryOptions.W4d), Is.True);
            Assert.That(c[2].ExternalDims, Is.False);
            Assert.That(c[3].SameAs(BarGeometryOptions.W4b), Is.True);
            Assert.That(c[4].SameAs(BarGeometryOptions.W4a), Is.True);
            Assert.That(c[16].IsLegacy, Is.True);
            Assert.That(c.Select(o => o.ToString()).Distinct().Count(), Is.EqualTo(17));
        }

        [Test]
        public void W4aGenerator_EqualsFrozenOutline()
        {
            var gen = BarFrameRules.LocalOutline("15", Pv(500, 300), 25, BarGeometryOptions.W4a)!;
            Assert.That(gen.Count, Is.EqualTo(FrozenW4a15H25.Length));
            for (int i = 0; i < gen.Count; i++)
            {
                Assert.That(gen[i].X, Is.EqualTo(FrozenW4a15H25[i].X).Within(1e-6), $"[{i}].X");
                Assert.That(gen[i].Y, Is.EqualTo(FrozenW4a15H25[i].Y).Within(1e-6), $"[{i}].Y");
            }
        }

        [Test]
        public void FrozenW4aOutline_H25_RotatedMirrored_FrameRecovered()
        {
            var pv = Pv(500, 300);
            var world = ToWorld(FrozenW4a15H25, new BarFrame2(Math.PI / 6, true), 1000, 2000);
            var current = BarFrameRules.LocalOutline("15", pv, 25)!;
            var legacy = BarFrameRules.LegacyLocalOutline("15", pv, 25)!;
            Assert.That(BarFrameRules.TryFit(world, current, out _, out _), Is.False, "obrys W4a ≠ bieżący (r 87 / 87.5)");
            Assert.That(BarFrameRules.TryFit(world, legacy, out _, out _), Is.False, "obrys W4a ≠ sprzed W4 (łuk t = r)");
            Assert.That(BarFrameRules.TryFitVariants(world, "15", pv, 25, out var f, out var matched), Is.True);
            Assert.That(matched.SameAs(BarGeometryOptions.W4a), Is.True, matched.ToString());
            Assert.That(f.Angle, Is.EqualTo(Math.PI / 6).Within(1e-9));
            Assert.That(f.Mirrored, Is.True);
        }

        [Test]
        public void W4aStage_51_63_OldHooks()
        {
            // etap W4a: haki sprzed W4b (51: max(16d,160) = 192; 63: max(14d,150) = 168, start (0, hak))
            var p51 = BarGeometryBuilder.GetLocalPoints("51", Pv(400, 300, 0), 12, BarGeometryOptions.W4a);
            Assert.That(p51[0].Y, Is.EqualTo(300 - 192).Within(1e-9));
            var p63 = BarGeometryBuilder.GetLocalPoints("63", Pv(400, 300, 0), 12, BarGeometryOptions.W4a);
            Assert.That(p63[0].Y, Is.EqualTo(168).Within(1e-9));
        }

        // ── uwaga 1: długość rozkładu Manual wg geometrii sprzed W4 ───────────

        private static int SegOf(string code, double[] pv, double d, BarGeometryOptions opt, BarSegmentLabel label, bool left)
        {
            var labels = BarGeometryBuilder.GetSegmentLabels(code, pv, d, opt)!;
            int j = labels.FindIndex(l => l.SameAs(label));
            int n = labels.Count + 1;
            return left ? j : 2 * n - 2 - j;
        }

        private static double SegLen(IReadOnlyList<(double X, double Y)> o, int s)
            => Math.Sqrt(Math.Pow(o[s + 1].X - o[s].X, 2) + Math.Pow(o[s + 1].Y - o[s].Y, 2));

        [TestCase(true)]
        [TestCase(false)]
        public void Manual_15_Leg1_LengthAsLegacy(bool left)
        {
            // Obrys etapu W4b–W4c (zamrożony, wariant historyczny) 15 {A 500, B 200, C 300}: długość Manual = wzorzec
            // (W4d, runda 1, uwaga 1 (a)); obrys W4d — długość odcinka W4d (test Manual_W4d_* w BarGeometryW4dTests)
            var pv = Pv(500, 200, 300);
            var leg1 = new BarSegmentLabel(BarSegmentLabel.Leg, 1, 0);
            // obrys W4c w świecie (obrót 30° + odbicie)
            var world = ToWorld(FrozenOutlinesW4c.W4c_15_H12, new BarFrame2(Math.PI / 6, true), 50, 60);
            int s = SegOf("15", pv, 12, BarGeometryOptions.W4b, leg1, left);
            double measured = SegLen(world, s);
            // wzorzec: ten sam odcinek na obrysie sprzed W4
            var legacy = BarFrameRules.LegacyLocalOutline("15", pv, 12)!;
            double expected = SegLen(legacy, SegOf("15", pv, 12, BarGeometryOptions.Legacy, leg1, left));
            double? got = BarFrameRules.LegacySegmentLength(world, s, "15", pv, 12);
            Assert.That(got, Is.Not.Null);
            Assert.That(got!.Value, Is.EqualTo(expected).Within(1e-9));
            Assert.That(Math.Abs(measured - expected), Is.GreaterThan(30), "łuk styczny skraca prosty odcinek (t 24 → 57.94)");
        }

        [Test]
        public void Manual_15_Leg0_And_ArcSub_MappedToLegacy()
        {
            var pv = Pv(500, 200, 300);
            var world = ToWorld(FrozenOutlinesW4c.W4c_15_H12, BarFrame2.Identity, 0, 0);   // obrys etapu W4b–W4c
            var legacy = BarFrameRules.LegacyLocalOutline("15", pv, 12)!;
            foreach (var lab in new[] { new BarSegmentLabel(BarSegmentLabel.Leg, 0, 0), new BarSegmentLabel(BarSegmentLabel.Arc, 1, 2) })
            {
                int s = SegOf("15", pv, 12, BarGeometryOptions.W4b, lab, true);
                double expected = SegLen(legacy, SegOf("15", pv, 12, BarGeometryOptions.Legacy, lab, true));
                Assert.That(BarFrameRules.LegacySegmentLength(world, s, "15", pv, 12)!.Value, Is.EqualTo(expected).Within(1e-9), lab.ToString());
            }
        }

        /// <summary>
        /// Kąty 90°: obrys etapu W4d (umowa osi) — długość = zmierzona (wzorzec bit w bit). Obrys W4e (wymiary zewnętrzne):
        /// BarGeometryW4eTests.Manual_W4eOutline_SameAsBeforeW4e.
        /// </summary>
        [TestCase("11", 500, 300, 0)]
        [TestCase("21", 500, 300, 500)]
        [TestCase("00", 3000, 0, 0)]
        public void Manual_RightAngles_SameAsMeasured(string code, double a, double b, double c)
        {
            var pv = Pv(a, b, c);
            var world = ToWorld(BarFrameRules.LocalOutline(code, pv, 12, BarGeometryOptions.W4d)!, new BarFrame2(0.3, false), 10, 20);
            for (int s = 0; s < world.Count - 1; s++)
            {
                double? got = BarFrameRules.LegacySegmentLength(world, s, code, pv, 12);
                if (got == null) continue;   // zaślepka końca
                Assert.That(got.Value, Is.EqualTo(SegLen(world, s)).Within(1e-6), $"{code} odcinek {s}");
            }
        }

        [Test]
        public void Manual_LegacyOutlineItself_SameAsMeasured()
        {
            // stary rysunek (obrys sprzed W4, jeszcze nieprzebudowany) — długość = zmierzona
            var pv = Pv(500, 200);
            var world = ToWorld(BarFrameRules.LegacyLocalOutline("15", pv, 12)!, new BarFrame2(1.0, true), 0, 0);
            for (int s = 0; s < world.Count - 1; s++)
            {
                double? got = BarFrameRules.LegacySegmentLength(world, s, "15", pv, 12);
                if (got == null) continue;
                Assert.That(got.Value, Is.EqualTo(SegLen(world, s)).Within(1e-6), $"odcinek {s}");
            }
        }

        [Test]
        public void Manual_RingAndCapAndUnfitted_Null()
        {
            var pv = Pv(500, 200, 300);
            var world = ToWorld(BarFrameRules.LocalOutline("15", pv, 12)!, BarFrame2.Identity, 0, 0);
            int n = world.Count / 2;
            Assert.That(BarFrameRules.LegacySegmentLength(world, n - 1, "15", pv, 12), Is.Null, "zaślepka końca");
            Assert.That(BarFrameRules.LegacySegmentLength(world, 0, "75", pv, 12), Is.Null, "pierścień");
            var broken = world.ToList(); broken[5] = (broken[5].X + 50, broken[5].Y + 50);
            Assert.That(BarFrameRules.LegacySegmentLength(broken, 0, "15", pv, 12), Is.Null, "obrys niedopasowany");
        }

        [Test]
        public void SegmentLabels_LegacyAndCurrent_SameLegsForRightAngles()
        {
            var pv = Pv(500, 300, 500);
            var cur = BarGeometryBuilder.GetSegmentLabels("21", pv, 12, BarGeometryOptions.Current)!;
            var leg = BarGeometryBuilder.GetSegmentLabels("21", pv, 12, BarGeometryOptions.Legacy)!;
            Assert.That(cur.Count, Is.EqualTo(BarGeometryBuilder.GetLocalPoints("21", pv, 12).Count - 1));
            Assert.That(cur.Select(l => l.ToString()), Is.EqualTo(leg.Select(l => l.ToString())));
            Assert.That(cur.Count(l => l.Kind == BarSegmentLabel.Leg), Is.EqualTo(3));
        }

        [Test]
        public void SegmentLabels_ZeroSegmentRemoved_KeepsOriginalLegNumbers()
        {
            // 25 {A,B,E}: C = 0 → ostre punkty 0,1,2,3(=2),4 → ramiona 0, 1, 3 (ramię 2 zerowe)
            var labels = BarGeometryBuilder.GetSegmentLabels("25", Pv(500, 400, 300), 12, BarGeometryOptions.Current)!;
            var legs = labels.Where(l => l.Kind == BarSegmentLabel.Leg).Select(l => l.Index).ToList();
            Assert.That(legs, Is.EqualTo(new[] { 0, 1, 3 }));
        }

        // ── uwaga 4: wymiary zestawienia tylko z definicji kształtu ──────────

        [Test]
        public void ShapeParamValues_Ring75_OnlyAB()
        {
            var r = ShapeCodeLibrary.ShapeParamValues("75", new[] { 300.0, 50.0, 200.0, 100.0, 80.0 });
            Assert.That(r, Is.EqualTo(new[] { 300.0, 50.0, 0, 0, 0 }));
        }

        [Test]
        public void ShapeParamValues_21_ThreeDims_Unknown_All()
        {
            Assert.That(ShapeCodeLibrary.ShapeParamValues("21", new[] { 1.0, 2, 3, 4, 5 }), Is.EqualTo(new[] { 1.0, 2, 3, 0, 0 }));
            Assert.That(ShapeCodeLibrary.ShapeParamValues("XX", new[] { 1.0, 2, 3, 4, 5 }), Is.EqualTo(new[] { 1.0, 2, 3, 4, 5 }));
        }

        // ── uwaga 3: 47 bez C przy nadpisanej długości ───────────────────────

        [Test]
        public void IsRequiredOnlyByFormula_47C()
        {
            Assert.That(ShapeCodeLibrary.Get("47")!.IsRequiredOnlyByFormula(2), Is.True);
            Assert.That(ShapeCodeLibrary.Get("47")!.IsRequiredOnlyByFormula(0), Is.False);
            Assert.That(ShapeCodeLibrary.Get("51")!.IsRequiredOnlyByFormula(2), Is.False);
        }
    }
}
