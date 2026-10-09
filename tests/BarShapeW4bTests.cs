using NUnit.Framework;
using BricsCadRc.Core;
using System;
using System.Collections.Generic;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Plan W4b (docs/PLAN_W4_BS8666.md) — formuły BS 8666:2020, Table 2, haki 51/63 spójne z formułą (0-POPRAWKI p.3),
    /// parametry opcjonalne (0-POPRAWKI p.5).
    /// </summary>
    [TestFixture]
    public class BarShapeW4bTests
    {
        private static double L(string code, double d, params double[] p) => ShapeCodeLibrary.Get(code)!.CalculateTotalLength(p, d);

        [TestCase("14", 12, new[] { 400.0, 100.0, 300.0, 0.0 }, 700.0)]   // W4d: D dopisane (tylko kąt)
        [TestCase("51", 12, new[] { 400.0, 300.0, 0.0 }, 1600.0)]
        [TestCase("51", 8,  new[] { 400.0, 300.0, 0.0 }, 1575.0)]
        [TestCase("51", 12, new[] { 400.0, 300.0, 210.0 }, 1700.0)]
        [TestCase("63", 12, new[] { 300.0, 200.0, 0.0 }, 1375.0)]
        [TestCase("63", 8,  new[] { 300.0, 200.0, 0.0 }, 1350.0)]
        [TestCase("63", 20, new[] { 300.0, 200.0, 0.0 }, 1475.0)]
        [TestCase("63", 12, new[] { 300.0, 200.0, 180.0 }, 1425.0)]
        [TestCase("75", 12, new[] { 100.0, 500.0 }, 825.0)]
        [TestCase("47", 12, new[] { 300.0, 200.0, 200.0 }, 1250.0)]
        [TestCase("31", 12, new[] { 300.0, 200.0, 300.0, 200.0 }, 950.0)]
        [TestCase("29", 12, new[] { 300.0, 200.0, 100.0 }, 600.0)]
        [TestCase("56", 12, new[] { 400.0, 300.0, 200.0, 150.0, 100.0 }, 1200.0)]
        [TestCase("44", 12, new[] { 500.0, 400.0, 500.0, 400.0, 300.0 }, 2025.0)]
        // bez zmian (AutoRebar / przebicie)
        [TestCase("00", 12, new[] { 3010.0 }, 3025.0)]   // W4g: 00 też w górę do 25 mm (kl. 7.2)
        [TestCase("11", 12, new[] { 300.0, 200.0 }, 500.0)]
        [TestCase("13", 10, new[] { 610.0, 70.0, 610.0 }, 1250.0)]
        [TestCase("21", 12, new[] { 500.0, 140.0, 500.0 }, 1100.0)]
        public void Formula_Bs8666_2020(string code, double d, double[] p, double expected)
            => Assert.That(L(code, d, p), Is.EqualTo(expected).Within(1e-9));

        [Test]
        public void Code13_UB03_UnchangedVsLegacyFormula()
        {
            // 13: A + 0.57B + C − 1.6d (bez zmian) — UB 03 610-70-610 H10: 610 + 39.9 + 610 − 16 = 1243.9 → 1250
            Assert.That(L("13", 10, 610, 70, 610), Is.EqualTo(1250.0).Within(1e-9));
        }

        [Test]
        public void Code47_WithoutC_And_D50_Throw()
        {
            Assert.Throws<InvalidOperationException>(() => L("47", 12, 300, 200, 0));
            Assert.Throws<InvalidOperationException>(() => L("47", 50, 300, 200, 200));
        }

        [Test]
        public void Code64_RequiresF_Throws()
        {
            var ex = Assert.Throws<InvalidOperationException>(() => L("64", 12, 400, 300, 200, 150, 100));
            Assert.That(ex!.Message, Does.Contain("64"));
            Assert.That(ex.Message, Does.Contain("F"));
        }

        [Test]
        public void Parameters_31_47_AppendedAtEnd()
        {
            Assert.That(ShapeCodeLibrary.Get("31")!.Parameters, Is.EqualTo(new[] { "A", "B", "C", "D" }));
            Assert.That(ShapeCodeLibrary.Get("47")!.Parameters, Is.EqualTo(new[] { "A", "B", "C" }));
        }

        // ── Table 2 ───────────────────────────────────────────────────────────

        [TestCase(6, 12)] [TestCase(8, 16)] [TestCase(10, 20)] [TestCase(12, 24)] [TestCase(16, 32)]
        [TestCase(20, 70)] [TestCase(25, 87)] [TestCase(32, 112)] [TestCase(40, 140)] [TestCase(50, 175)]
        public void MinBendRadius_Table2(double d, double r) => Assert.That(BarShape.MinBendRadius(d), Is.EqualTo(r));

        [TestCase(6, 45)] [TestCase(8, 60)] [TestCase(10, 70)] [TestCase(12, 85)] [TestCase(16, 115)]
        [TestCase(20, 200)] [TestCase(25, 250)] [TestCase(32, 320)] [TestCase(40, 400)]
        public void HookDiameterQ_Table2(double d, double q) => Assert.That(BarShape.HookDiameterQ(d), Is.EqualTo(q));

        [Test]
        public void HookDiameterQ_D50_NotAvailable()
            => Assert.Throws<InvalidOperationException>(() => BarShape.HookDiameterQ(50));

        [Test]
        public void MinEndProjectionP_Table2()
        {
            Assert.That(BarShape.MinEndProjectionP(12, false), Is.EqualTo(125));
            Assert.That(BarShape.MinEndProjectionP(12, true), Is.EqualTo(155));
            Assert.That(BarShape.MinEndProjectionP(50, true), Is.EqualTo(725));
            Assert.That(double.IsNaN(BarShape.MinEndProjectionP(14, false)), Is.True);
        }

        // ── 51 / 63: hak C_eff spójny z formułą minimalną (0-POPRAWKI p.3) ──

        private static double Raw51(double a, double b, double c, double d)
            => 2 * (a + b + c) - 2.5 * BarShape.MinBendRadius(d) - 5 * d;

        private static double Raw63(double a, double b, double c, double d)
            => 2 * a + 3 * b + 2 * c - 3 * BarShape.MinBendRadius(d) - 6 * d;

        [TestCase(8)] [TestCase(10)] [TestCase(12)] [TestCase(16)] [TestCase(20)] [TestCase(25)] [TestCase(32)]
        public void CEff_GeneralFormulaWithCEff_EqualsMinimalFormula(double d)
        {
            double a = 400, b = 300;
            Assert.That(Raw51(a, b, BarShape.Code51EffectiveHook(d), d),
                        Is.EqualTo(2 * a + 2 * b + Math.Max(16 * d, 160)).Within(1e-9));
            Assert.That(Raw63(a, b, BarShape.Code63EffectiveHook(d), d),
                        Is.EqualTo(2 * a + 3 * b + (d <= 16 ? Math.Max(14 * d, 140) : 13 * d)).Within(1e-9));
        }

        [Test]
        public void CEff_d12_Values()
        {
            Assert.That(BarShape.Code51EffectiveHook(12), Is.EqualTo((192 + 60 + 60) / 2.0));   // 156
            Assert.That(BarShape.Code63EffectiveHook(12), Is.EqualTo((168 + 72 + 72) / 2.0));   // 156
        }

        private static double AxisLength(List<(double X, double Y)> pts)
        {
            double s = 0;
            for (int i = 1; i < pts.Count; i++)
                s += Math.Sqrt(Math.Pow(pts[i].X - pts[i - 1].X, 2) + Math.Pow(pts[i].Y - pts[i - 1].Y, 2));
            return s;
        }

        /// <summary>
        /// Geometria haka 51/63 przy C = 0 = geometria przy C = C_eff (hak z formuły), więc długość osi obrysu odbiega
        /// od formuły TAK SAMO dla C = 0 i C &gt; 0 (stała różnica osi ↔ wymiarów zewnętrznych — znika w W4e;
        /// dopiero wtedy |Δ| ≤ 0.5d+1, patrz raport W4b).
        /// </summary>
        [TestCase("51", 12)] [TestCase("51", 8)] [TestCase("51", 20)] [TestCase("63", 12)] [TestCase("63", 8)] [TestCase("63", 20)]
        public void CEff_GeometryHook_ConsistentWithFormula(string code, double d)
        {
            double a = code == "51" ? 400 : 300, b = code == "51" ? 300 : 200;
            double cEff = code == "51" ? BarShape.Code51EffectiveHook(d) : BarShape.Code63EffectiveHook(d);
            var p0  = BarGeometryBuilder.GetLocalPoints(code, new[] { a, b, 0.0, 0, 0 }, d);
            var pCe = BarGeometryBuilder.GetLocalPoints(code, new[] { a, b, cEff, 0, 0 }, d);
            Assert.That(p0.Count, Is.EqualTo(pCe.Count));
            for (int i = 0; i < p0.Count; i++)
            {
                Assert.That(p0[i].X, Is.EqualTo(pCe[i].X).Within(1e-9));
                Assert.That(p0[i].Y, Is.EqualTo(pCe[i].Y).Within(1e-9));
            }
            double raw = code == "51" ? Raw51(a, b, cEff, d) : Raw63(a, b, cEff, d);
            double delta0 = AxisLength(p0) - raw;
            double c2 = 250;
            var pC = BarGeometryBuilder.GetLocalPoints(code, new[] { a, b, c2, 0, 0 }, d);
            double deltaC = AxisLength(pC) - (code == "51" ? Raw51(a, b, c2, d) : Raw63(a, b, c2, d));
            Assert.That(delta0, Is.EqualTo(deltaC).Within(1e-6), "różnica oś ↔ formuła taka sama dla C = 0 i C > 0");
        }

        // ── parametry opcjonalne (0-POPRAWKI p.5) ─────────────────────────────

        [Test]
        public void IsOptionalParam_COf51And63Only()
        {
            Assert.That(ShapeCodeLibrary.Get("51")!.IsOptionalParam(2), Is.True);
            Assert.That(ShapeCodeLibrary.Get("63")!.IsOptionalParam(2), Is.True);
            Assert.That(ShapeCodeLibrary.Get("51")!.IsOptionalParam(0), Is.False);
            Assert.That(ShapeCodeLibrary.Get("21")!.IsOptionalParam(2), Is.False);
            Assert.That(ShapeCodeLibrary.Get("47")!.IsOptionalParam(2), Is.False);
        }
    }
}
