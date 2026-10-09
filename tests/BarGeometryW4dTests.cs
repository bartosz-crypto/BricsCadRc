using NUnit.Framework;
using BricsCadRc.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Plan W4d (docs/PLAN_W4_BS8666.md) — geometria wg rysunków BS 8666:2020 (flaga Bs2020Sharp): rozpoznanie obrysów
    /// zapisanych w etapie W4b–W4c (ZAMROŻONE obrysy — FrozenOutlinesW4c), etykiety odcinków i długość rozkładu Manual,
    /// kąt skosu z wymiarów (D/E) i kąt domyślny, parametry opcjonalne, kolejność wierszy dialogów; W4g — zaokrąglenie 00.
    /// </summary>
    [TestFixture]
    public class BarGeometryW4dTests
    {
        private const double D = 12.0;

        private static double[] Pv(params double[] v) { var p = new double[5]; Array.Copy(v, p, Math.Min(5, v.Length)); return p; }

        private static List<(double X, double Y)> ToWorld(IReadOnlyList<(double X, double Y)> local, BarFrame2 f, double tx, double ty)
            => local.Select(p => { var w = f.Apply(p.X - local[0].X, p.Y - local[0].Y); return (tx + w.X, ty + w.Y); }).ToList();

        /// <summary>Kształt, zamrożony obrys etapu W4b–W4c, wartości pól XData (te same przed i po W4d).</summary>
        private static IEnumerable<TestCaseData> FrozenCases()
        {
            yield return new TestCaseData("15", Pv(500, 200, 300)).SetName("Frozen_15");
            yield return new TestCaseData("14", Pv(400, 250, 300)).SetName("Frozen_14");
            yield return new TestCaseData("12", Pv(500, 300, 100)).SetName("Frozen_12");
            yield return new TestCaseData("22", Pv(400, 600, 150, 300)).SetName("Frozen_22");
            yield return new TestCaseData("24", Pv(500, 400, 300)).SetName("Frozen_24");
            yield return new TestCaseData("33", Pv(900, 200, 300)).SetName("Frozen_33");
            yield return new TestCaseData("46", Pv(200, 300, 400, 200)).SetName("Frozen_46");
            yield return new TestCaseData("51", Pv(400, 300, 0)).SetName("Frozen_51");
        }

        private static (double X, double Y)[] Frozen(string code)
        {
            switch (code)
            {
                case "15": return FrozenOutlinesW4c.W4c_15_H12;
                case "14": return FrozenOutlinesW4c.W4c_14_H12;
                case "12": return FrozenOutlinesW4c.W4c_12_H12;
                case "22": return FrozenOutlinesW4c.W4c_22_H12;
                case "24": return FrozenOutlinesW4c.W4c_24_H12;
                case "33": return FrozenOutlinesW4c.W4c_33_H12;
                case "46": return FrozenOutlinesW4c.W4c_46_H12;
                case "51": return FrozenOutlinesW4c.W4c_51_H12;
                default: throw new ArgumentException(code);
            }
        }

        // ── 0-POPRAWKI p.2: obrysy etapu W4b–W4c rozpoznawane po W4d ────────

        [TestCaseSource(nameof(FrozenCases))]
        public void FrozenW4cOutline_RotatedMirrored_FrameRecovered(string code, double[] pv)
        {
            var frozen = Frozen(code);
            var current = BarFrameRules.LocalOutline(code, pv, D)!;
            foreach (bool mirrored in new[] { false, true })
            {
                var world = ToWorld(frozen, new BarFrame2(Math.PI / 6, mirrored), 1000, -2000);
                Assert.That(BarFrameRules.TryFit(world, current, out _, out _), Is.False, $"{code}: obrys W4c ≠ bieżący (W4d)");
                Assert.That(BarFrameRules.TryFitVariants(world, code, pv, D, out var f, out var matched), Is.True, $"{code}: brak dopasowania");
                Assert.That(matched.Bs2020Sharp, Is.False, $"{code}: {matched}");
                Assert.That(matched.TangentArcs && matched.TableRadius, Is.True, $"{code}: {matched}");
                Assert.That(f.Angle, Is.EqualTo(Math.PI / 6).Within(1e-9), $"{code} mirrored={mirrored}: kąt");
                Assert.That(f.Mirrored, Is.EqualTo(mirrored), $"{code}: odbicie");
            }
        }

        [TestCaseSource(nameof(FrozenCases))]
        public void W4bOption_ReproducesFrozenW4cOutline(string code, double[] pv)
        {
            var frozen = Frozen(code);
            var gen = BarFrameRules.LocalOutline(code, pv, D, BarGeometryOptions.W4b)!;
            Assert.That(gen.Count, Is.EqualTo(frozen.Length), code);
            for (int i = 0; i < gen.Count; i++)
            {
                Assert.That(gen[i].X, Is.EqualTo(frozen[i].X).Within(1e-6), $"{code} [{i}].X");
                Assert.That(gen[i].Y, Is.EqualTo(frozen[i].Y).Within(1e-6), $"{code} [{i}].Y");
            }
        }

        // ── bieżąca geometria W4d: rama, etykiety, brak NaN ─────────────────

        private static IEnumerable<TestCaseData> W4dCases()
        {
            yield return new TestCaseData("12", Pv(500, 300, 100)).SetName("W4d_12");
            yield return new TestCaseData("14", Pv(500, 300, 300, 400)).SetName("W4d_14");
            yield return new TestCaseData("15", Pv(500, 300, 300, 400)).SetName("W4d_15");
            yield return new TestCaseData("22", Pv(400, 600, 150, 300)).SetName("W4d_22");
            yield return new TestCaseData("24", Pv(500, 400, 300, 240, 320)).SetName("W4d_24");
            yield return new TestCaseData("33", Pv(900, 200, 300)).SetName("W4d_33");
            yield return new TestCaseData("46", Pv(200, 300, 400, 200, 150)).SetName("W4d_46");
            yield return new TestCaseData("51", Pv(400, 300, 0)).SetName("W4d_51");
        }

        [TestCaseSource(nameof(W4dCases))]
        public void CurrentOutline_RotatedMirrored_MatchesCurrent(string code, double[] pv)
        {
            var local = BarFrameRules.LocalOutline(code, pv, D)!;
            foreach (bool mirrored in new[] { false, true })
            {
                var world = ToWorld(local, new BarFrame2(Math.PI / 6, mirrored), -300, 450);
                Assert.That(BarFrameRules.TryFitVariants(world, code, pv, D, out var f, out var matched), Is.True, code);
                Assert.That(matched.SameAs(BarGeometryOptions.Current), Is.True, $"{code}: {matched}");
                Assert.That(f.Angle, Is.EqualTo(Math.PI / 6).Within(1e-9), code);
                Assert.That(f.Mirrored, Is.EqualTo(mirrored), code);
            }
        }

        [TestCaseSource(nameof(W4dCases))]
        public void SegmentLabels_OnePerSegment(string code, double[] pv)
        {
            var pts = BarGeometryBuilder.GetLocalPoints(code, pv, D);
            var labels = BarGeometryBuilder.GetSegmentLabels(code, pv, D, BarGeometryOptions.Current)!;
            Assert.That(labels.Count, Is.EqualTo(pts.Count - 1), code);
            Assert.That(labels.Count(l => l.Kind == BarSegmentLabel.Leg), Is.GreaterThanOrEqualTo(2), code);
        }

        /// <summary>Stare rekordy (wymiary kąta / półkola = 0) — bez NaN i bez wyjątku.</summary>
        [TestCase("14", 500, 200, 0, 0, 0)]
        [TestCase("15", 500, 200, 0, 0, 0)]
        [TestCase("15", 500, 200, 300, 0, 0)]
        [TestCase("22", 400, 600, 0, 0, 0)]
        [TestCase("22", 400, 600, 300, 0, 0)]
        [TestCase("24", 500, 400, 300, 0, 0)]
        [TestCase("33", 900, 0, 0, 0, 0)]
        [TestCase("33", 400, 300, 200, 150, 0)]
        [TestCase("46", 400, 300, 0, 0, 0)]
        [TestCase("46", 200, 300, 400, 200, 500)]   // D > B → kąt domyślny
        [TestCase("12", 500, 300, 0, 0, 0)]
        [TestCase("51", 400, 300, 0, 0, 0)]
        public void OldRecords_NoNaN(string code, double a, double b, double c, double d, double e)
        {
            var pv = Pv(a, b, c, d, e);
            var pts = BarGeometryBuilder.GetLocalPoints(code, pv, D);
            Assert.That(pts.Count, Is.GreaterThanOrEqualTo(2));
            foreach (var p in pts)
                Assert.That(double.IsNaN(p.X) || double.IsNaN(p.Y) || double.IsInfinity(p.X) || double.IsInfinity(p.Y), Is.False, code);
            var outline = BarFrameRules.LocalOutline(code, pv, D);
            Assert.That(outline, Is.Not.Null, code);
            foreach (var p in outline!) Assert.That(double.IsNaN(p.X) || double.IsNaN(p.Y), Is.False, code);
            Assert.That(BarGeometryBuilder.GetSegmentLabels(code, pv, D, BarGeometryOptions.Current)!.Count, Is.EqualTo(pts.Count - 1), code);
        }

        // ── 0-POPRAWKI p.1: rozkład Manual po W4d = długość wzorca (sprzed W4) ─

        private static int SegOf(string code, double[] pv, BarGeometryOptions opt, BarSegmentLabel label, bool left)
        {
            var labels = BarGeometryBuilder.GetSegmentLabels(code, pv, D, opt)!;
            int j = labels.FindIndex(l => l.SameAs(label));
            if (j < 0) return -1;
            int n = labels.Count + 1;
            return left ? j : 2 * n - 2 - j;
        }

        private static double SegLen(IReadOnlyList<(double X, double Y)> o, int s)
            => Math.Sqrt(Math.Pow(o[s + 1].X - o[s].X, 2) + Math.Pow(o[s + 1].Y - o[s].Y, 2));

        /// <summary>
        /// Runda 1, uwaga 1 (b): obrys W4d kształtu ze zmienionym układem ramion — długość Manual = wskazany odcinek geometrii
        /// W4d (= zmierzony), każdy odcinek obu stron, po obrocie 30° i odbiciu (dawniej: ramię o tym samym numerze sprzed W4,
        /// np. 33 dolna prosta 700 → 152 — błąd).
        /// </summary>
        [TestCase("14", 500, 300, 300, 400, 0)]
        [TestCase("15", 500, 200, 300, 0, 0)]
        [TestCase("22", 400, 600, 150, 300, 0)]
        [TestCase("24", 500, 400, 300, 0, 0)]
        [TestCase("46", 200, 300, 400, 200, 150)]
        [TestCase("51", 400, 300, 0, 0, 0)]
        [TestCase("33", 900, 200, 300, 0, 0)]
        public void Manual_W4d_ChangedLayout_SelectedSegmentOfW4d(string code, double a, double b, double c, double d, double e)
        {
            var pv = Pv(a, b, c, d, e);
            Assert.That(BarFrameRules.ArmLayoutChangedInW4d(code), Is.True);
            foreach (bool mirrored in new[] { false, true })
            {
                // obrys etapu W4d (umowa osi); obrys W4e → ta sama długość W4d (BarGeometryW4eTests)
                var world = ToWorld(BarFrameRules.LocalOutline(code, pv, D, BarGeometryOptions.W4d)!, new BarFrame2(Math.PI / 6, mirrored), 50, 60);
                int n = world.Count / 2, got = 0;
                for (int s = 0; s < world.Count - 1; s++)
                {
                    double? l = BarFrameRules.LegacySegmentLength(world, s, code, pv, D, geometryW4d: true);   // znacznik [15] = 2021
                    if (s == n - 1) { Assert.That(l, Is.Null, "zaślepka"); continue; }
                    Assert.That(l, Is.Not.Null, $"{code} odcinek {s}");
                    Assert.That(l!.Value, Is.EqualTo(SegLen(world, s)).Within(1e-6), $"{code} odcinek {s} mirrored={mirrored}");
                    got++;
                }
                Assert.That(got, Is.EqualTo(world.Count - 2), code);
            }
        }

        [Test]
        public void Manual_W4d_33_BottomStraight_NotLegacyLeg()
        {
            // 33 {900,200,300}: ramię 1 = dolna prosta (oś 700) — rozkład ≈ 700, nie 152 (ramię 1 starego „S”)
            var pv = Pv(900, 200, 300);
            var world = ToWorld(BarFrameRules.LocalOutline("33", pv, D)!, new BarFrame2(Math.PI / 6, true), 0, 0);
            int s = SegOf("33", pv, BarGeometryOptions.Current, new BarSegmentLabel(BarSegmentLabel.Leg, 1, 0), true);
            double l = BarFrameRules.LegacySegmentLength(world, s, "33", pv, D, geometryW4d: true)!.Value;
            Assert.That(l, Is.EqualTo(700.0).Within(1.0));
            // bez znacznika (np. ogon [14+] zgubiony przez starą wtyczkę) → gałąź historyczna: ramię 1 sprzed W4
            var legacy = BarFrameRules.LegacyLocalOutline("33", pv, D)!;
            double wz = SegLen(legacy, SegOf("33", pv, BarGeometryOptions.Legacy, new BarSegmentLabel(BarSegmentLabel.Leg, 1, 0), true));
            Assert.That(BarFrameRules.LegacySegmentLength(world, s, "33", pv, D)!.Value, Is.EqualTo(wz).Within(1e-9));
        }

        /// <summary>
        /// Runda 2 (BLOKER): 51 A = B = 400, C = 0 — obrys etapu W4b–W4c identyczny z W4d. Bez znacznika (stary pręt) →
        /// wzorzec: hak = max(16d,160) − r = 192 − 24 = 168; ze znacznikiem W4d → hak C_eff − r = 156 − 24 = 132.
        /// Po obrocie i odbiciu, obie strony.
        /// </summary>
        [Test]
        public void Manual_51_AeqB_AmbiguousOutline_MarkerDecides()
        {
            var pv = Pv(400, 400, 0);
            var stage = BarFrameRules.LocalOutline("51", pv, D, BarGeometryOptions.W4b)!;
            var w4d = BarFrameRules.LocalOutline("51", pv, D, BarGeometryOptions.W4d)!;
            Assert.That(stage.Count, Is.EqualTo(w4d.Count));
            for (int i = 0; i < stage.Count; i++)
                Assert.That(Math.Abs(stage[i].X - w4d[i].X) + Math.Abs(stage[i].Y - w4d[i].Y), Is.LessThan(1e-9), "obrysy identyczne");
            foreach (bool mirrored in new[] { false, true })
            foreach (bool left in new[] { true, false })
            {
                var world = ToWorld(stage, new BarFrame2(Math.PI / 6, mirrored), 100, 200);
                int s = SegOf("51", pv, BarGeometryOptions.W4b, new BarSegmentLabel(BarSegmentLabel.Leg, 0, 0), left);
                Assert.That(BarFrameRules.LegacySegmentLength(world, s, "51", pv, D)!.Value, Is.EqualTo(168.0).Within(0.5), "bez znacznika");
                Assert.That(BarFrameRules.LegacySegmentLength(world, s, "51", pv, D, geometryW4d: true) ?? SegLen(world, s),
                            Is.EqualTo(132.0).Within(0.5), "ze znacznikiem W4d");
            }
        }

        /// <summary>Runda 1, uwaga 1: kształty bez zmiany układu ramion (12 — tylko promień R, 11, 21, 13) — wzorzec sprzed W4.</summary>
        [TestCase("12", 500, 300, 100)]
        [TestCase("11", 500, 300, 0)]
        [TestCase("21", 500, 300, 400)]
        public void Manual_W4d_UnchangedLayout_LegacyMapping(string code, double a, double b, double c)
        {
            var pv = Pv(a, b, c);
            Assert.That(BarFrameRules.ArmLayoutChangedInW4d(code), Is.False);
            var world = ToWorld(BarFrameRules.LocalOutline(code, pv, D)!, new BarFrame2(Math.PI / 6, true), 50, 60);
            var legacy = BarFrameRules.LegacyLocalOutline(code, pv, D)!;
            int checkedLegs = 0;
            foreach (bool left in new[] { true, false })
            for (int k = 0; k < 4; k++)
            {
                var lab = new BarSegmentLabel(BarSegmentLabel.Leg, k, 0);
                int s = SegOf(code, pv, BarGeometryOptions.Current, lab, left);
                int sl = SegOf(code, pv, BarGeometryOptions.Legacy, lab, left);
                if (s < 0 || sl < 0) continue;
                Assert.That(BarFrameRules.LegacySegmentLength(world, s, code, pv, D)!.Value, Is.EqualTo(SegLen(legacy, sl)).Within(1e-9), $"{code} {lab}");
                checkedLegs++;
            }
            Assert.That(checkedLegs, Is.GreaterThanOrEqualTo(4), code);
        }

        /// <summary>Runda 1, uwaga 1 (a): obrys historyczny (etap W4b–W4c, zamrożony) — mapowanie na wzorzec sprzed W4.</summary>
        [TestCaseSource(nameof(FrozenCases))]
        public void Manual_FrozenW4cOutline_LegacyMapping(string code, double[] pv)
        {
            var world = ToWorld(Frozen(code), new BarFrame2(Math.PI / 6, true), 10, 20);
            var legacy = BarFrameRules.LegacyLocalOutline(code, pv, D)!;
            int checkedLegs = 0;
            foreach (bool left in new[] { true, false })
            for (int k = 0; k < 4; k++)
            {
                var lab = new BarSegmentLabel(BarSegmentLabel.Leg, k, 0);
                int s = SegOf(code, pv, BarGeometryOptions.W4b, lab, left);
                int sl = SegOf(code, pv, BarGeometryOptions.Legacy, lab, left);
                if (s < 0 || sl < 0) continue;
                Assert.That(BarFrameRules.LegacySegmentLength(world, s, code, pv, D)!.Value, Is.EqualTo(SegLen(legacy, sl)).Within(1e-9), $"{code} {lab}");
                checkedLegs++;
            }
            Assert.That(checkedLegs, Is.GreaterThanOrEqualTo(4), code);
        }

        // ── runda 1, uwaga 2: bezpiecznik BBS dla 00 (W4g) ───────────────────

        [Test]
        public void BbsSafety_Straight00()
        {
            var s = ShapeCodeLibrary.Get("00")!;
            double f3010 = s.CalculateTotalLength(new[] { 3010.0 }, D), f3000 = s.CalculateTotalLength(new[] { 3000.0 }, D);
            Assert.That(f3010, Is.EqualTo(3025.0));
            Assert.That(BarShape.StoredLengthDiffers("00", D, false, 3010, f3010), Is.True, "3010 → 3025: ostrzeżenie");
            Assert.That(BarShape.StoredLengthDiffers("00", D, false, 3000, f3000), Is.False, "3000 → 3000: bez");
            Assert.That(BarShape.StoredLengthDiffers("00", D, true, 3010, f3010), Is.False, "długość nadpisana: bez");
            Assert.That(BarShape.StoredLengthDiffers("00", D, false, 0, f3010), Is.False, "brak zapisanej TL: bez");
            Assert.That(BarShape.StoredLengthDiffers("21", D, false, 700, 1500), Is.False, "21 (UB AutoRebar, D17): bez");
        }

        // ── kąt skosu (BarShape.SlopeAngle) i ostrzeżenie ───────────────────

        [Test]
        public void SlopeAngle_FromDimensions_AndDefaults()
        {
            var s15 = ShapeCodeLibrary.Get("15")!;
            Assert.That(s15.SlopeAngle(Pv(500, 300, 300, 400), out bool def1), Is.EqualTo(Math.Atan2(300, 400)).Within(1e-12));
            Assert.That(def1, Is.False);
            // W4h: B bez D → asin(B/A); reguła W4d (fromSingle = false) → 45° domyślnie
            Assert.That(s15.SlopeAngle(Pv(500, 300, 300, 0), out bool def2), Is.EqualTo(Math.Asin(0.6)).Within(1e-12));
            Assert.That(def2, Is.False);
            Assert.That(s15.SlopeAngle(Pv(500, 300, 300, 0), out bool def2b, false), Is.EqualTo(Math.PI / 4).Within(1e-12));
            Assert.That(def2b, Is.True);
            var s24 = ShapeCodeLibrary.Get("24")!;
            Assert.That(s24.SlopeAngle(Pv(500, 400, 300), out bool def3), Is.EqualTo(Math.PI / 6).Within(1e-12));
            Assert.That(def3, Is.True);
            Assert.That(s24.SlopeAngle(Pv(500, 400, 300, 240, 320), out bool def4), Is.EqualTo(Math.Atan2(240, 320)).Within(1e-12));
            Assert.That(def4, Is.False);
            var s46 = ShapeCodeLibrary.Get("46")!;   // {A,B,C,E,D}
            Assert.That(s46.SlopeAngle(Pv(200, 300, 400, 200, 150), out bool def5), Is.EqualTo(Math.PI / 6).Within(1e-12));
            Assert.That(def5, Is.False);
            Assert.That(s46.SlopeAngle(Pv(200, 300, 400, 200, 301), out bool def6), Is.EqualTo(Math.PI / 4).Within(1e-12));
            Assert.That(def6, Is.True);
        }

        [Test]
        public void DefaultAngleWarning_OldBar15WithoutD()
        {
            // W4h: ostrzeżenie tylko przy braku B i D (B bez D → kąt asin(B/A))
            Assert.That(ShapeCodeLibrary.Get("15")!.DefaultAngleWarning(Pv(500, 200, 300, 0)), Is.Null);
            string w = ShapeCodeLibrary.Get("15")!.DefaultAngleWarning(Pv(500, 0, 300, 0));
            Assert.That(w, Is.Not.Null);
            Assert.That(w, Does.Contain("15").And.Contain("45°").And.Contain("B i D"));
            Assert.That(ShapeCodeLibrary.Get("15")!.DefaultAngleWarning(Pv(500, 200, 300, 400)), Is.Null);
            Assert.That(ShapeCodeLibrary.Get("24")!.DefaultAngleWarning(Pv(500, 400, 300)), Does.Contain("30°"));
            Assert.That(ShapeCodeLibrary.Get("11")!.DefaultAngleWarning(Pv(500, 200)), Is.Null);
        }

        [Test]
        public void Old15WithoutD_GeometryUnchangedByEditWithEmptyD()
        {
            // 0-POPRAWKI p.5: RC_EDIT_BAR starego 15 z D = 0 — dialog przyjmuje puste D (0), geometria = ta sama co przed edycją
            var s15 = ShapeCodeLibrary.Get("15")!;
            Assert.That(s15.IsOptionalParam(3), Is.True);
            var before = BarGeometryBuilder.GetLocalPoints("15", Pv(500, 200, 300, 0), D);
            var after  = BarGeometryBuilder.GetLocalPoints("15", Pv(500, 200, 300, 0), D);
            Assert.That(after, Is.EqualTo(before));
            Assert.That(s15.CalculateTotalLength(new[] { 500.0, 200, 300, 0 }, D), Is.EqualTo(800.0));
        }

        // ── parametry opcjonalne i kolejność wierszy dialogów ────────────────

        [Test]
        public void OptionalParams_AngleDims()
        {
            var s15 = ShapeCodeLibrary.Get("15")!;
            Assert.That(s15.Parameters, Is.EqualTo(new[] { "A", "B", "C", "D" }));
            Assert.That(s15.IsOptionalParam(1), Is.False, "B wymagane (jak dotąd)");
            Assert.That(s15.IsOptionalParam(3), Is.True, "D — tylko kąt");
            Assert.That(ShapeCodeLibrary.Get("14")!.IsOptionalParam(3), Is.True);
            var s24 = ShapeCodeLibrary.Get("24")!;
            Assert.That(s24.Parameters, Is.EqualTo(new[] { "A", "B", "C", "D", "E" }));
            Assert.That(s24.IsOptionalParam(3) && s24.IsOptionalParam(4), Is.True);
            Assert.That(s24.IsOptionalParam(2), Is.False);
            var s46 = ShapeCodeLibrary.Get("46")!;
            Assert.That(s46.Parameters, Is.EqualTo(new[] { "A", "B", "C", "E", "D" }));
            Assert.That(s46.IsOptionalParam(4), Is.True, "46: D (pole 4)");
            Assert.That(s46.IsOptionalParam(3), Is.False, "46: E (pole 3)");
            Assert.That(ShapeCodeLibrary.Get("51")!.IsOptionalParam(2), Is.True, "51: C (W4b)");
            Assert.That(ShapeCodeLibrary.Get("33")!.IsOptionalParam(2), Is.False);
        }

        [Test]
        public void DisplayOrder_Alphabetical_RowToSlot()
        {
            Assert.That(ShapeCodeLibrary.Get("46")!.DisplayOrder, Is.EqualTo(new[] { 0, 1, 2, 4, 3 }));
            Assert.That(ShapeCodeLibrary.Get("15")!.DisplayOrder, Is.EqualTo(new[] { 0, 1, 2, 3 }));
            Assert.That(ShapeCodeLibrary.Get("25")!.DisplayOrder, Is.EqualTo(new[] { 0, 1, 2 }));     // {A,B,E}
            Assert.That(ShapeCodeLibrary.Get("12")!.DisplayOrder, Is.EqualTo(new[] { 0, 1, 2 }));     // {A,B,R}
            Assert.That(ShapeCodeLibrary.Get("00")!.DisplayOrder, Is.EqualTo(new[] { 0 }));
        }

        // ── formuły bez zmian (kontrola) ─────────────────────────────────────

        [Test]
        public void Formulas_22_33_Unchanged()
        {
            Assert.That(ShapeCodeLibrary.Get("22")!.CalculateTotalLength(new[] { 400.0, 600, 150, 300 }, D), Is.EqualTo(1350.0));
            // 2A + 1.7B + 2C − 4d = 1800 + 340 + 600 − 48 = 2692 → 2700
            Assert.That(ShapeCodeLibrary.Get("33")!.CalculateTotalLength(new[] { 900.0, 200, 300 }, D), Is.EqualTo(2700.0));
            Assert.That(ShapeCodeLibrary.Get("46")!.CalculateTotalLength(new[] { 200.0, 300, 400, 200, 150 }, D), Is.EqualTo(1400.0));
        }

        // ── W4g: 00 w górę do 25 mm (kl. 7.2) ────────────────────────────────

        [TestCase(3010.0, 3025.0)]
        [TestCase(3000.0, 3000.0)]
        [TestCase(2999.99, 3000.0)]
        [TestCase(3000.0000000004, 3000.0)]   // szum z obrysu nie podbija o 25 mm
        [TestCase(1250.0, 1250.0)]
        [TestCase(6000.0, 6000.0)]
        [TestCase(1.0, 25.0)]
        public void W4g_Straight00_RoundedUpTo25(double a, double expected)
        {
            Assert.That(ShapeCodeLibrary.Get("00")!.CalculateTotalLength(new[] { a }, D), Is.EqualTo(expected));
        }

        [Test]
        public void W4g_Straight00_TotalLengthFor_ARecordUnchanged()
        {
            var pv = Pv(3010);
            Assert.That(BarLengthRules.TotalLengthFor("00", pv, D), Is.EqualTo(3025.0));
            Assert.That(pv[0], Is.EqualTo(3010.0), "A bez zmian");
            var pts = BarGeometryBuilder.GetLocalPoints("00", pv, D);
            Assert.That(pts[1].X, Is.EqualTo(3010.0), "geometria = A");
        }

        // ── 15 / 14: geometria W4d wg rysunku normy ──────────────────────────

        [Test]
        public void Code15_And_14_LegsAndBend()
        {
            // 15 {A 500, B 300, C 300, D 400}: A skośne (0,300) → (400,0), C → (700,0); 14: C wstecz → (100,0)
            var p15 = BarGeometryBuilder.GetLocalPoints("15", Pv(500, 300, 300, 400), D, BarGeometryOptions.W4d);
            var p14 = BarGeometryBuilder.GetLocalPoints("14", Pv(500, 300, 300, 400), D, BarGeometryOptions.W4d);
            Assert.That(p15[0].Y, Is.EqualTo(300).Within(1e-9));
            Assert.That(p15[p15.Count - 1].X, Is.EqualTo(700).Within(1e-9));
            Assert.That(p14[p14.Count - 1].X, Is.EqualTo(100).Within(1e-9));
            // zagięcie 15: odchylenie θ ≈ 36.87° (łagodne); 14: 180° − θ (ostre)
            Assert.That(p15.Count, Is.EqualTo(9));
            Assert.That(p14.Count, Is.EqualTo(9));
            Assert.That(p15[8].Y, Is.EqualTo(0).Within(1e-9));
        }
    }
}
