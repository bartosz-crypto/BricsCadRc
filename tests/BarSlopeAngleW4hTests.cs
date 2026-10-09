using NUnit.Framework;
using BricsCadRc.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Plan W4h (decyzja użytkownika 2026-10-09, docs/RANO_DECYZJE.md) — kąt ramienia skośnego 14, 15, 24, 46:
    /// z wymiaru B (θ = asin(B/A)), z B i D (jak dotąd), albo wpisany kąt → wymiary kąta zaokrąglone do 5 mm; brak → 45° /
    /// 30° + ostrzeżenie. Obrysy sprzed W4h (kąt domyślny przy B bez D) rozpoznawane wariantem W4e (SlopeFromB = false).
    /// </summary>
    [TestFixture]
    public class BarSlopeAngleW4hTests
    {
        private const double D = 12.0;
        private static double[] Pv(params double[] v) { var p = new double[5]; Array.Copy(v, p, Math.Min(5, v.Length)); return p; }
        private static BarShape S(string code) => ShapeCodeLibrary.Get(code)!;
        private static double Deg(double rad) => rad * 180.0 / Math.PI;

        // ── kąt z wymiarów ───────────────────────────────────────────────────

        [Test]
        public void Code15_AngleFromB_WhenDMissing()
        {
            double t = S("15").SlopeAngle(Pv(500, 300, 300, 0), out bool def);
            Assert.That(def, Is.False);
            Assert.That(t, Is.EqualTo(Math.Asin(0.6)).Within(1e-12));          // 36,87°
            Assert.That(S("15").DefaultAngleWarning(Pv(500, 300, 300, 0)), Is.Null);
        }

        [Test]
        public void Code14_AngleFromD_WhenBMissing_AndFromBothAsBefore()
        {
            Assert.That(S("14").SlopeAngle(Pv(500, 0, 300, 400), out _), Is.EqualTo(Math.Acos(0.8)).Within(1e-12));
            Assert.That(S("14").SlopeAngle(Pv(500, 300, 300, 400), out _), Is.EqualTo(Math.Atan2(300, 400)).Within(1e-12));
        }

        [Test]
        public void NoDimensions_Default45_WithWarning()
        {
            double t = S("15").SlopeAngle(Pv(500, 0, 300, 0), out bool def);
            Assert.That(def, Is.True);
            Assert.That(Deg(t), Is.EqualTo(45).Within(1e-12));
            Assert.That(S("15").DefaultAngleWarning(Pv(500, 0, 300, 0)), Does.Contain("przyjęto kąt domyślny 45°"));
        }

        [Test]
        public void BLongerThanA_90Degrees_WithWarning()
        {
            Assert.That(Deg(S("15").SlopeAngle(Pv(300, 400, 300, 0), out bool def)), Is.EqualTo(90).Within(1e-12));
            Assert.That(def, Is.False);
            Assert.That(S("15").DefaultAngleWarning(Pv(300, 400, 300, 0)), Does.Contain("B > A").And.Contain("90°"));
        }

        [Test]
        public void Runda2_Entered90_NoWarning_HorizontalProjection_0Degrees()
        {
            // wpisany kąt 90° → B = A (co 5 mm) — bez ostrzeżenia; rzut ≤ ramię + 0,5 mm — bez ostrzeżenia
            var pv = S("15").AngleToDimensions(Pv(500, 0, 300, 0), 90)!;
            Assert.That(pv[1], Is.EqualTo(500));
            Assert.That(S("15").DefaultAngleWarning(pv), Is.Null);
            Assert.That(S("15").DefaultAngleWarning(Pv(500, 500.4, 300, 0)), Is.Null);
            // rzut poziomy ≥ ramię → 0°
            Assert.That(Deg(S("14").SlopeAngle(Pv(500, 0, 300, 500), out _)), Is.EqualTo(0).Within(1e-12));
            Assert.That(S("14").DefaultAngleWarning(Pv(500, 0, 300, 520)), Does.Contain("D ≥ A").And.Contain("0°"));
            Assert.That(S("24").DefaultAngleWarning(Pv(500, 400, 300, 0, 400)), Does.Contain("E ≥ B").And.Contain("0°"));
        }

        // ── runda 2, uwaga 1: grip końca A przy stałym kącie (wariant b) ───

        [Test]
        public void Runda2_FixedSlope_D_ExactFromAB()
        {
            var f = S("15").WithFixedSlope(Pv(500, 300, 300, 0))!;
            Assert.That(f, Is.EqualTo(Pv(500, 300, 300, 400)));
            var g = S("14").WithFixedSlope(Pv(450, 200, 300, 0))!;
            Assert.That(g[3], Is.EqualTo(Math.Round(Math.Sqrt(450.0 * 450 - 200 * 200), 2)));
            Assert.That(S("15").WithFixedSlope(Pv(500, 300, 300, 400)), Is.Null, "D podane");
            Assert.That(S("15").WithFixedSlope(Pv(300, 400, 300, 0)), Is.Null, "B ≥ A");
            Assert.That(S("21").WithFixedSlope(Pv(500, 140, 500)), Is.Null);
        }

        [Test]
        public void Runda2_GripA_KeepsAngle_FixedEndStays()
        {
            // 15 H12 A 500 B 300 C 300 D puste: geometria z D = 400 = geometria bieżąca (B bez D); A + 100 przy D = 400 →
            // kąt 36,87° bez zmian, koniec C w miejscu, wolny koniec A przesunięty o 100 wzdłuż ramienia (jak FindEndParam)
            var cur = BarGeometryBuilder.GetLocalPoints("15", Pv(500, 300, 300, 0), D);
            var fix = BarGeometryBuilder.GetLocalPoints("15", S("15").WithFixedSlope(Pv(500, 300, 300, 0))!, D);
            Assert.That(fix.Count, Is.EqualTo(cur.Count));
            for (int i = 0; i < cur.Count; i++)
                Assert.That(Math.Abs(fix[i].X - cur[i].X) + Math.Abs(fix[i].Y - cur[i].Y), Is.LessThan(0.01), $"[{i}]");
            var nw = BarGeometryBuilder.GetLocalPoints("15", Pv(600, 300, 300, 400), D);
            int n = cur.Count;
            double sx = cur[n - 1].X - nw[n - 1].X, sy = cur[n - 1].Y - nw[n - 1].Y;
            double ux = cur[0].X - cur[1].X, uy = cur[0].Y - cur[1].Y, ul = Math.Sqrt(ux * ux + uy * uy);
            Assert.That(nw[0].X + sx, Is.EqualTo(cur[0].X + 100 * ux / ul).Within(0.05));
            Assert.That(nw[0].Y + sy, Is.EqualTo(cur[0].Y + 100 * uy / ul).Within(0.05));
            for (int j = 1; j < n; j++)
                Assert.That(Math.Abs(nw[j].X + sx - cur[j].X) + Math.Abs(nw[j].Y + sy - cur[j].Y), Is.LessThan(0.05), $"[{j}]");
            Assert.That(Deg(S("15").SlopeAngle(Pv(600, 300, 300, 400), out _)), Is.EqualTo(Deg(Math.Asin(0.6))).Within(1e-9));
        }

        [Test]
        public void Code24_AngleFromSingleDOrE()
        {
            Assert.That(S("24").SlopeAngle(Pv(500, 400, 300, 200, 0), out _), Is.EqualTo(Math.Asin(0.5)).Within(1e-12));
            Assert.That(S("24").SlopeAngle(Pv(500, 400, 300, 0, 200), out _), Is.EqualTo(Math.Acos(0.5)).Within(1e-12));
            Assert.That(Deg(S("24").SlopeAngle(Pv(500, 400, 300), out bool def)), Is.EqualTo(30).Within(1e-12));
            Assert.That(def, Is.True);
        }

        [Test]
        public void OldRule_FromSingleFalse_Default45()
            => Assert.That(Deg(S("15").SlopeAngle(Pv(500, 300, 300, 0), out _, false)), Is.EqualTo(45).Within(1e-12));

        // ── wpisany kąt → wymiary (co 5 mm) ──────────────────────────────────

        [Test]
        public void AngleToDimensions_RoundedTo5mm()
        {
            // 15: A 500, 30° → B = 250, D = 433,0 → 435
            Assert.That(S("15").AngleToDimensions(Pv(500, 0, 300, 0), 30), Is.EqualTo(Pv(500, 250, 300, 435)));
            // 14: A 400, 60° → B = 346,4 → 345, D = 200
            Assert.That(S("14").AngleToDimensions(Pv(400, 0, 300, 0), 60), Is.EqualTo(Pv(400, 345, 300, 200)));
            // 24: B 400 (skos), 30° → D = 200, E = 346,4 → 345
            Assert.That(S("24").AngleToDimensions(Pv(500, 400, 300, 0, 0), 30), Is.EqualTo(Pv(500, 400, 300, 200, 345)));
            // 46 {A,B,C,E,D}: B 300, 30° → D = 150
            Assert.That(S("46").AngleToDimensions(Pv(200, 300, 400, 200, 0), 30), Is.EqualTo(Pv(200, 300, 400, 200, 150)));
        }

        [Test]
        public void AngleToDimensions_Invalid_Null()
        {
            Assert.That(S("15").AngleToDimensions(Pv(500, 0, 300, 0), 0), Is.Null);
            Assert.That(S("15").AngleToDimensions(Pv(500, 0, 300, 0), 95), Is.Null);
            Assert.That(S("15").AngleToDimensions(Pv(0, 0, 300, 0), 30), Is.Null, "brak A");
            Assert.That(S("21").AngleToDimensions(Pv(500, 140, 500), 30), Is.Null, "kształt bez kąta");
            Assert.That(S("15").IsSlopeDimension(1) && S("15").IsSlopeDimension(3) && !S("15").IsSlopeDimension(0), Is.True);
        }

        [Test]
        public void EnteredAngle_GivesThatGeometryAngle()
        {
            // kąt wpisany 30° → B, D (co 5 mm) → kąt geometrii atan2(B, D) ≈ 30° (± zaokrąglenie)
            var pv = S("15").AngleToDimensions(Pv(500, 0, 300, 0), 30)!;
            Assert.That(Deg(S("15").SlopeAngle(pv, out _)), Is.EqualTo(30).Within(0.5));
        }

        // ── obrysy sprzed W4h rozpoznawane (0-POPRAWKI p.2) ─────────────────

        [Test]
        public void W4eOutline_15_BWithoutD_Recognised_AsW4eVariant()
        {
            // pręt 15 z B i bez D narysowany w W4e (kąt domyślny 45°) — po W4h geometria bieżąca ma asin(B/A)
            var pv = Pv(500, 200, 300, 0);
            var w4e = BarFrameRules.LocalOutline("15", pv, D, BarGeometryOptions.W4e)!;
            var cur = BarFrameRules.LocalOutline("15", pv, D)!;
            Assert.That(BarFrameRules.TryFit(w4e, cur, out _, out _), Is.False, "geometria W4h ≠ W4e");
            var f0 = new BarFrame2(Math.PI / 6, true);
            var world = w4e.Select(p => { var w = f0.Apply(p.X - w4e[0].X, p.Y - w4e[0].Y); return (100 + w.X, 200 + w.Y); }).ToList();
            Assert.That(BarFrameRules.TryFitVariants(world, "15", pv, D, out var f, out var matched), Is.True);
            Assert.That(matched.SameAs(BarGeometryOptions.W4e), Is.True, matched.ToString());
            Assert.That(f.Angle, Is.EqualTo(Math.PI / 6).Within(1e-9));
            Assert.That(f.Mirrored, Is.True);
        }

        [Test]
        public void W4dRefForManual_KeepsSlopeRuleOfOutline()
        {
            var cur = BarGeometryOptions.Current.WithoutExternalDims();
            Assert.That(cur.ExternalDims, Is.False);
            Assert.That(cur.SlopeFromB && cur.Bs2020Sharp, Is.True);
            Assert.That(BarGeometryOptions.W4e.WithoutExternalDims().SameAs(BarGeometryOptions.W4d), Is.True);
        }
    }
}
