using NUnit.Framework;
using BricsCadRc.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BricsCadRc.Tests
{
    /// <summary>
    /// Plan W4e (docs/PLAN_W4_BS8666.md, poprawka #13) — wymiary BS 8666:2020 zewnętrzne (kl. 7.1, rys. 4 / 5), łuk osi
    /// r + d/2 (BarDimensionRules, flaga ExternalDims): obrys = wymiary z XData, długość osi ≈ formuła, rozpoznanie obrysów
    /// etapu W4d (ZAMROŻONE — FrozenOutlinesW4d), długość rozkładu Manual przed / po W4e bez zmian, RC_SECTION bez podwójnego
    /// odejmowania.
    /// </summary>
    [TestFixture]
    public class BarGeometryW4eTests
    {
        private const double D = 12.0;

        /// <summary>
        /// Tolerancja wymiarów obrysu: wierzchołki obrysu przy końcach łuków liczone z uśrednionej normalnej sąsiednich odcinków
        /// (ComputeOutlineVertices / LocalOutlineFrom — arytmetyka wzorca), odchyłka od lica rzędu 5e-4 mm.
        /// </summary>
        private const double BoxTol = 0.01;

        private static double[] Pv(params double[] v) { var p = new double[5]; Array.Copy(v, p, Math.Min(5, v.Length)); return p; }

        private static List<(double X, double Y)> ToWorld(IReadOnlyList<(double X, double Y)> local, BarFrame2 f, double tx, double ty)
            => local.Select(p => { var w = f.Apply(p.X - local[0].X, p.Y - local[0].Y); return (tx + w.X, ty + w.Y); }).ToList();

        private static (double MinX, double MinY, double MaxX, double MaxY) Bbox(IEnumerable<(double X, double Y)> pts)
            => (pts.Min(p => p.X), pts.Min(p => p.Y), pts.Max(p => p.X), pts.Max(p => p.Y));

        private static double AxisLength(IReadOnlyList<(double X, double Y)> pts)
        {
            double s = 0;
            for (int i = 0; i + 1 < pts.Count; i++)
                s += Math.Sqrt((pts[i + 1].X - pts[i].X) * (pts[i + 1].X - pts[i].X) + (pts[i + 1].Y - pts[i].Y) * (pts[i + 1].Y - pts[i].Y));
            return s;
        }

        private static double SegLen(IReadOnlyList<(double X, double Y)> o, int s)
            => Math.Sqrt((o[s + 1].X - o[s].X) * (o[s + 1].X - o[s].X) + (o[s + 1].Y - o[s].Y) * (o[s + 1].Y - o[s].Y));

        /// <summary>Indeks odcinka obrysu (lewa / prawa strona) dla etykiety odcinka osi w danym wariancie; −1 gdy brak.</summary>
        private static int SegOf(string code, double[] pv, BarGeometryOptions opt, BarSegmentLabel lab, bool left)
        {
            var labels = BarGeometryBuilder.GetSegmentLabels(code, pv, D, opt)!;
            int j = -1;
            for (int i = 0; i < labels.Count; i++) if (labels[i].SameAs(lab)) { j = i; break; }
            if (j < 0) return -1;
            int n = labels.Count + 1;
            return left ? j : 2 * n - 2 - j;
        }

        /// <summary>Formuły BS 8666:2020 bez zaokrąglenia do 25 mm (r — Table 2).</summary>
        private static double Formula(string code, double[] p, double d)
        {
            double r = BarShape.MinBendRadius(d);
            switch (code)
            {
                case "11": return p[0] + p[1] - 0.5 * r - d;
                case "12": return p[0] + p[1] - 0.43 * p[2] - 1.2 * d;
                case "13": return p[0] + 0.57 * p[1] + p[2] - 1.6 * d;
                case "14": case "15": return p[0] + p[2];
                case "21": case "23": return p[0] + p[1] + p[2] - r - 2 * d;
                case "22": return p[0] + p[1] + 0.57 * p[2] + p[3] - 0.5 * r - 2.6 * d;
                case "24": return p[0] + p[1] + p[2];
                case "33": return 2 * p[0] + 1.7 * p[1] + 2 * p[2] - 4 * d;
                case "44": return p[0] + p[1] + p[2] + p[3] + p[4] - 2 * r - 4 * d;
                case "46": return p[0] + 2 * p[1] + p[2] + p[3];
                case "51": return p[2] <= 0 ? 2 * p[0] + 2 * p[1] + BarShape.Code51MinExtra(d) : 2 * (p[0] + p[1] + p[2]) - 2.5 * r - 5 * d;
                case "63": return p[2] <= 0 ? 2 * p[0] + 3 * p[1] + BarShape.Code63MinExtra(d) : 2 * p[0] + 3 * p[1] + 2 * p[2] - 3 * r - 6 * d;
                default: throw new ArgumentException(code);
            }
        }

        // ── BarDimensionRules (czysta tabela) ───────────────────────────────

        [Test]
        public void ToAxis_Table()
        {
            Assert.That(BarDimensionRules.ToAxis("21", Pv(500, 140, 500), D), Is.EqualTo(Pv(494, 128, 494)));
            Assert.That(BarDimensionRules.ToAxis("13", Pv(500, 200, 300), D), Is.EqualTo(Pv(494, 188, 294)));
            Assert.That(BarDimensionRules.ToAxis("11", Pv(500, 300), D), Is.EqualTo(Pv(494, 294)));
            Assert.That(BarDimensionRules.ToAxis("12", Pv(500, 300, 100), D), Is.EqualTo(Pv(494, 294, 106)), "12: R + d/2");
            Assert.That(BarDimensionRules.ToAxis("12", Pv(500, 300, 0), D), Is.EqualTo(Pv(494, 294, 0)), "12: R = 0 zostaje");
            // runda 1, uwaga 2: B (lico A → skrajnia półkola) = lico–lico → −d
            Assert.That(BarDimensionRules.ToAxis("22", Pv(400, 600, 150, 300), D), Is.EqualTo(Pv(394, 588, 138, 294)));
            Assert.That(BarDimensionRules.ToAxis("33", Pv(900, 200, 300), D), Is.EqualTo(Pv(888, 188, 294)));
            Assert.That(BarDimensionRules.ToAxis("44", Pv(500, 400, 500, 400, 300), D), Is.EqualTo(Pv(494, 388, 488, 388, 294)));
            Assert.That(BarDimensionRules.ToAxis("51", Pv(400, 300, 0), D), Is.EqualTo(Pv(388, 288, 0)), "51: C = 0 → C_eff w geometrii");
            Assert.That(BarDimensionRules.ToAxis("63", Pv(300, 200, 180), D), Is.EqualTo(Pv(288, 188, 174)));
            Assert.That(BarDimensionRules.ToAxis("75", Pv(300, 100), D), Is.EqualTo(Pv(288, 100)), "75: oś A − d, zakład bez zmian");
            Assert.That(BarDimensionRules.ToAxis("00", Pv(3000), D), Is.EqualTo(Pv(3000)), "00 bez zmian");
            Assert.That(BarDimensionRules.ToAxis("XX", Pv(1, 2, 3), D), Is.EqualTo(Pv(1, 2, 3)), "nieznany kształt bez zmian");
            Assert.That(BarDimensionRules.ToAxis("21", null!, D), Is.Null);
        }

        [Test]
        public void LegReduction_RightAngle_ExactlyHalfD_AndContinuous()
        {
            Assert.That(BarDimensionRules.LegReduction(Math.PI / 2, 24, D), Is.EqualTo(6.0));
            // rys. 5 (rozwarty) i rys. 4 (ostry) zbiegają się w 90°
            Assert.That(BarDimensionRules.LegReduction(Math.PI / 2 - 1e-7, 24, D), Is.EqualTo(6.0).Within(1e-5));
            Assert.That(BarDimensionRules.LegReduction(Math.PI / 2 + 1e-7, 24, D), Is.EqualTo(6.0).Within(1e-5));
            // 45° rozwarty: d/2·tan(22.5°); 135° ostry: (r + d) − (r + d/2)·tan(67.5°)
            Assert.That(BarDimensionRules.LegReduction(Math.PI / 4, 24, D), Is.EqualTo(6 * Math.Tan(Math.PI / 8)).Within(1e-12));
            Assert.That(BarDimensionRules.LegReduction(3 * Math.PI / 4, 24, D), Is.EqualTo(36 - 30 * Math.Tan(3 * Math.PI / 8)).Within(1e-12));
        }

        // ── Obrys = wymiary zewnętrzne ───────────────────────────────────────

        [Test]
        public void Code21_Bbox_140x500_AxisArc30_AxisNearFormula()
        {
            var pv = Pv(500, 140, 500);
            var bb = Bbox(BarFrameRules.LocalOutline("21", pv, D)!);
            Assert.That(bb.MaxX - bb.MinX, Is.EqualTo(140).Within(BoxTol), "szerokość B po zewnętrznej");
            Assert.That(bb.MaxY - bb.MinY, Is.EqualTo(500).Within(BoxTol), "wysokość A po zewnętrznej");
            var pts = BarGeometryBuilder.GetLocalPoints("21", pv, D);
            // pierwszy łuk: punkty 1..7, środek (r_osi, −A_osi + r_osi) = (30, −464), promień osi 24 + 6 = 30
            for (int i = 1; i <= 7; i++)
                Assert.That(Math.Sqrt((pts[i].X - 30) * (pts[i].X - 30) + (pts[i].Y + 464) * (pts[i].Y + 464)), Is.EqualTo(30).Within(1e-9), $"[{i}]");
            double axis = AxisLength(pts);
            // wartość dokładna łuków (aproksymacja 7 pkt jest cięciwowa) — oś ≈ 1090.25, formuła 1092
            double exact = (494 - 30) + (128 - 60) + (494 - 30) + Math.PI * 30;
            Assert.That(exact, Is.EqualTo(1090.25).Within(0.01));
            Assert.That(Math.Abs(exact - Formula("21", pv, D)), Is.LessThanOrEqualTo(0.5 * D + 1));
            Assert.That(axis, Is.EqualTo(exact).Within(0.5), "długość osi obrysu (cięciwy łuków)");
        }

        [Test]
        public void UB225_AutoRebarTemplate_OutlineIsAxB()
        {
            // UB B1 225 (AutoRebarEngine.UB_225_*): 700 / 140 / 700, H12 → obrys = A × B z XData
            var bb = Bbox(BarFrameRules.LocalOutline("21", Pv(700, 140, 700), D)!);
            Assert.That(bb.MaxX - bb.MinX, Is.EqualTo(140).Within(BoxTol));
            Assert.That(bb.MaxY - bb.MinY, Is.EqualTo(700).Within(BoxTol));
            // UB B2 225: 13 {715, 115, 715}
            var bb13 = Bbox(BarFrameRules.LocalOutline("13", Pv(715, 115, 715), D)!);
            Assert.That(bb13.MaxX - bb13.MinX, Is.EqualTo(715).Within(BoxTol));
            Assert.That(bb13.MaxY - bb13.MinY, Is.EqualTo(115).Within(BoxTol));
        }

        [Test]
        public void Code13_Bbox_500x200_Loop94_End()
        {
            var pv = Pv(500, 200, 300);
            var bb = Bbox(BarFrameRules.LocalOutline("13", pv, D)!);
            Assert.That(bb.MaxX - bb.MinX, Is.EqualTo(500).Within(BoxTol));
            Assert.That(bb.MaxY - bb.MinY, Is.EqualTo(200).Within(BoxTol));
            var pts = BarGeometryBuilder.GetLocalPoints("13", pv, D);
            Assert.That(pts.Count, Is.EqualTo(15));
            // pętla: środek (A_osi − 94, 94) = (400, 94), promień osi (B − d)/2 = 94
            for (int i = 1; i <= 13; i++)
                Assert.That(Math.Sqrt((pts[i].X - 400) * (pts[i].X - 400) + (pts[i].Y - 94) * (pts[i].Y - 94)), Is.EqualTo(94).Within(1e-9), $"[{i}]");
            Assert.That(pts[14].X, Is.EqualTo(200).Within(1e-9), "koniec A − C");
            Assert.That(pts[14].Y, Is.EqualTo(188).Within(1e-9), "koniec B − d");
        }

        [Test]
        public void Code75_Ring_OuterRadius150()
        {
            var pts = BarGeometryBuilder.GetLocalPoints("75", Pv(300), D);
            foreach (var p in pts)
                Assert.That(Math.Sqrt((p.X - 150) * (p.X - 150) + (p.Y - 150) * (p.Y - 150)), Is.EqualTo(144).Within(1e-9), "oś (A − d)/2");
            Assert.That(BarDimensionRules.RingAxisRadius(300, D) + D / 2, Is.EqualTo(150), "obrys zewnętrzny A/2");
        }

        [TestCase("11", 500, 300, 0, 0, 0)]
        [TestCase("12", 500, 300, 100, 0, 0)]
        [TestCase("13", 500, 200, 300, 0, 0)]
        [TestCase("21", 500, 140, 500, 0, 0)]
        [TestCase("21", 700, 140, 700, 0, 0)]
        [TestCase("23", 300, 400, 300, 0, 0)]
        [TestCase("22", 400, 600, 150, 300, 0)]
        [TestCase("44", 500, 400, 500, 400, 300)]
        [TestCase("51", 400, 300, 0, 0, 0)]
        [TestCase("51", 400, 300, 150, 0, 0)]
        [TestCase("63", 300, 200, 0, 0, 0)]
        [TestCase("63", 300, 200, 180, 0, 0)]
        public void RightAngleShapes_AxisNearFormula(string code, double a, double b, double c, double d, double e)
        {
            // Oś z wymiarów zewnętrznych ≈ formuła BS 8666:2020 (|Δ| ≤ 0.5d + 1; dla 51 / 63 możliwe dopiero po W4e — D19).
            // Oś mierzona po wierzchołkach (łuki jako cięciwy co 15° — oś nieco krótsza od dokładnej).
            var pv = Pv(a, b, c, d, e);
            double axis = AxisLength(BarGeometryBuilder.GetLocalPoints(code, pv, D));
            double f = Formula(code, pv, D);
            Assert.That(Math.Abs(axis - f), Is.LessThanOrEqualTo(0.5 * D + 1), $"{code}: oś {axis:F2}, formuła {f:F2}");
        }

        /// <summary>
        /// 33 (bieżnia, 3 półkola): długość osi DOKŁADNA (analitycznie z wymiarów osi: 2A' + 2C' − 6ρ + 3πρ, ρ = (B − d)/2)
        /// ≈ formuła 2A + 1.7B + 2C − 4d (|Δ| ≤ 0.5d + 1); oś z wierzchołków (cięciwy półkoli) — w granicach 3 mm od dokładnej.
        /// </summary>
        [Test]
        public void Code33_ExactAxisNearFormula()
        {
            var pv = Pv(900, 200, 300);
            double a = 900 - D, rho = (200 - D) / 2, c = 300 - D / 2;
            double exact = 2 * a + 2 * c - 6 * rho + 3 * Math.PI * rho;
            Assert.That(Math.Abs(exact - Formula("33", pv, D)), Is.LessThanOrEqualTo(0.5 * D + 1));
            Assert.That(AxisLength(BarGeometryBuilder.GetLocalPoints("33", pv, D)), Is.EqualTo(exact).Within(3.0));
        }

        [TestCase("14", 400, 250, 300, 250, 0)]
        [TestCase("15", 500, 300, 300, 400, 0)]
        [TestCase("15", 500, 200, 300, 0, 0)]
        [TestCase("24", 500, 400, 300, 0, 0)]
        [TestCase("46", 200, 300, 400, 200, 150)]
        public void AngledShapes_Note3_Within3Percent(string code, double a, double b, double c, double d, double e)
        {
            var pv = Pv(a, b, c, d, e);
            double axis = AxisLength(BarGeometryBuilder.GetLocalPoints(code, pv, D));
            double f = Formula(code, pv, D);
            Assert.That(Math.Abs(axis - f) / f, Is.LessThanOrEqualTo(0.03), $"{code}: oś {axis:F2}, formuła {f:F2}");
        }

        /// <summary>
        /// Rys. 5 (kąt rozwarty, 15): A i C mierzone do przecięcia lic zewnętrznych — niezależna konstrukcja z osi: proste lic
        /// zewnętrznych ramion A i C, punkt przecięcia, odległości od wolnych końców.
        /// </summary>
        [Test]
        public void Code15_ObtuseAngle_Figure5_DimensionsToOuterFaceIntersection()
        {
            var pv = Pv(500, 300, 300, 400);   // θ = atan2(300, 400)
            var pts = BarGeometryBuilder.GetLocalPoints("15", pv, D);
            var p0 = pts[0]; var p1 = pts[1];
            var pe = pts[pts.Count - 1]; var pq = pts[pts.Count - 2];
            double ux = p1.X - p0.X, uy = p1.Y - p0.Y, ul = Math.Sqrt(ux * ux + uy * uy); ux /= ul; uy /= ul;
            double vx = pe.X - pq.X, vy = pe.Y - pq.Y, vl = Math.Sqrt(vx * vx + vy * vy); vx /= vl; vy /= vl;
            // skręt w lewo (CCW) → strona zewnętrzna = prawa: normalna (uy, −ux)
            double o1x = p0.X + uy * D / 2, o1y = p0.Y - ux * D / 2;   // punkt na licu zewn. A
            double o2x = pe.X + vy * D / 2, o2y = pe.Y - vx * D / 2;   // punkt na licu zewn. C
            // przecięcie o1 + s·u = o2 + t·v
            double det = ux * (-vy) - uy * (-vx);
            double s = ((o2x - o1x) * (-vy) - (o2y - o1y) * (-vx)) / det;
            double ix = o1x + s * ux, iy = o1y + s * uy;
            Assert.That(s, Is.EqualTo(500).Within(1e-6), "A do przecięcia lic");
            Assert.That((o2x - ix) * vx + (o2y - iy) * vy, Is.EqualTo(300).Within(1e-6), "C do przecięcia lic");
        }

        /// <summary>
        /// Rys. 4 (kąt ostry, 14): A i C mierzone do stycznej prostopadłej w skrajni łuku (zewnętrzna powierzchnia, promień
        /// r + d). Środek łuku z punktu styczności osi (pts[1]) i promienia osi r + d/2.
        /// </summary>
        [Test]
        public void Code14_AcuteAngle_Figure4_DimensionsToTangentAtBendExtremity()
        {
            var pv = Pv(400, 250, 300, 250);   // θ = 45° → zagięcie 135°
            var pts = BarGeometryBuilder.GetLocalPoints("14", pv, D);
            double R = BarShape.MinBendRadius(D) + D / 2;   // 30
            var p0 = pts[0]; var p1 = pts[1];
            double ux = p1.X - p0.X, uy = p1.Y - p0.Y, ul = Math.Sqrt(ux * ux + uy * uy); ux /= ul; uy /= ul;
            // A w dół w prawo, C wstecz w lewo → skręt w prawo (CW): środek po prawej (uy, −ux)
            double cx = p1.X + uy * R, cy = p1.Y - ux * R;
            double aExt = (cx - p0.X) * ux + (cy - p0.Y) * uy + R + D / 2;
            Assert.That(aExt, Is.EqualTo(400).Within(1e-6), "A do stycznej w skrajni łuku");
            var pe = pts[pts.Count - 1];   // C poziome w lewo, wolny koniec; skrajnia łuku w +X
            double cExt = (cx + R + D / 2) - pe.X;
            Assert.That(cExt, Is.EqualTo(300).Within(1e-6), "C do stycznej w skrajni łuku");
        }

        // ── 0-POPRAWKI p.2: obrysy etapu W4d rozpoznawane po W4e ────────────

        private static IEnumerable<TestCaseData> FrozenW4dCases()
        {
            yield return new TestCaseData("11", Pv(500, 300), 12.0, FrozenOutlinesW4d.W4d_11_H12).SetName("FrozenW4d_11");
            yield return new TestCaseData("12", Pv(500, 300, 100), 12.0, FrozenOutlinesW4d.W4d_12_H12).SetName("FrozenW4d_12");
            yield return new TestCaseData("13", Pv(500, 200, 300), 12.0, FrozenOutlinesW4d.W4d_13_H12).SetName("FrozenW4d_13");
            yield return new TestCaseData("13", Pv(610, 70, 610), 10.0, FrozenOutlinesW4d.W4d_13_H10).SetName("FrozenW4d_13_H10");
            yield return new TestCaseData("14", Pv(400, 250, 300, 250), 12.0, FrozenOutlinesW4d.W4d_14_H12).SetName("FrozenW4d_14");
            yield return new TestCaseData("15", Pv(500, 300, 300, 400), 12.0, FrozenOutlinesW4d.W4d_15_H12).SetName("FrozenW4d_15");
            yield return new TestCaseData("21", Pv(700, 140, 700), 12.0, FrozenOutlinesW4d.W4d_21_H12).SetName("FrozenW4d_21");
            yield return new TestCaseData("22", Pv(400, 600, 150, 300), 12.0, FrozenOutlinesW4d.W4d_22_H12).SetName("FrozenW4d_22");
            yield return new TestCaseData("24", Pv(500, 400, 300), 12.0, FrozenOutlinesW4d.W4d_24_H12).SetName("FrozenW4d_24");
            yield return new TestCaseData("33", Pv(900, 200, 300), 12.0, FrozenOutlinesW4d.W4d_33_H12).SetName("FrozenW4d_33");
            yield return new TestCaseData("44", Pv(500, 400, 500, 400, 300), 12.0, FrozenOutlinesW4d.W4d_44_H12).SetName("FrozenW4d_44");
            yield return new TestCaseData("46", Pv(200, 300, 400, 200, 150), 12.0, FrozenOutlinesW4d.W4d_46_H12).SetName("FrozenW4d_46");
            yield return new TestCaseData("51", Pv(400, 300, 0), 12.0, FrozenOutlinesW4d.W4d_51_H12).SetName("FrozenW4d_51");
            yield return new TestCaseData("63", Pv(300, 200, 0), 12.0, FrozenOutlinesW4d.W4d_63_H12).SetName("FrozenW4d_63");
        }

        [TestCaseSource(nameof(FrozenW4dCases))]
        public void W4dOption_ReproducesFrozenW4dOutline(string code, double[] pv, double d, (double X, double Y)[] frozen)
        {
            var gen = BarFrameRules.LocalOutline(code, pv, d, BarGeometryOptions.W4d)!;
            Assert.That(gen.Count, Is.EqualTo(frozen.Length), code);
            for (int i = 0; i < gen.Count; i++)
            {
                Assert.That(gen[i].X, Is.EqualTo(frozen[i].X).Within(1e-6), $"{code} [{i}].X");
                Assert.That(gen[i].Y, Is.EqualTo(frozen[i].Y).Within(1e-6), $"{code} [{i}].Y");
            }
        }

        [TestCaseSource(nameof(FrozenW4dCases))]
        public void FrozenW4dOutline_RotatedMirrored_FrameRecovered(string code, double[] pv, double d, (double X, double Y)[] frozen)
        {
            var current = BarFrameRules.LocalOutline(code, pv, d)!;
            foreach (bool mirrored in new[] { false, true })
            {
                var world = ToWorld(frozen, new BarFrame2(Math.PI / 6, mirrored), 1000, -2000);
                Assert.That(BarFrameRules.TryFit(world, current, out _, out _), Is.False, $"{code}: obrys W4d ≠ bieżący (W4e)");
                Assert.That(BarFrameRules.TryFitVariants(world, code, pv, d, out var f, out var matched), Is.True, $"{code}: brak dopasowania");
                Assert.That(matched.ExternalDims, Is.False, $"{code}: {matched}");
                Assert.That(matched.TangentArcs && matched.TableRadius, Is.True, $"{code}: {matched}");
                Assert.That(f.Angle, Is.EqualTo(Math.PI / 6).Within(1e-9), $"{code} mirrored={mirrored}: kąt");
                Assert.That(f.Mirrored, Is.EqualTo(mirrored), $"{code}: odbicie");
            }
        }

        [TestCaseSource(nameof(FrozenW4dCases))]
        public void W4eOutline_RotatedMirrored_MatchesCurrent(string code, double[] pv, double d, (double X, double Y)[] frozen)
        {
            var local = BarFrameRules.LocalOutline(code, pv, d)!;
            foreach (bool mirrored in new[] { false, true })
            {
                var world = ToWorld(local, new BarFrame2(-0.7, mirrored), -300, 40);
                Assert.That(BarFrameRules.TryFitVariants(world, code, pv, d, out var f, out var matched), Is.True, code);
                Assert.That(matched.SameAs(BarGeometryOptions.Current), Is.True, $"{code}: {matched}");
                Assert.That(f.Angle, Is.EqualTo(-0.7).Within(1e-9));
                Assert.That(f.Mirrored, Is.EqualTo(mirrored));
            }
        }

        // ── 0-POPRAWKI p.1: rozkład Manual — długość przed i po W4e bez zmian ─

        private static IEnumerable<TestCaseData> ManualCases()
        {
            foreach (bool marker in new[] { false, true })
            {
                yield return new TestCaseData("11", Pv(500, 300), marker);
                yield return new TestCaseData("12", Pv(500, 300, 100), marker);
                yield return new TestCaseData("13", Pv(500, 200, 300), marker);
                yield return new TestCaseData("21", Pv(700, 140, 700), marker);
                yield return new TestCaseData("44", Pv(500, 400, 500, 400, 300), marker);
                yield return new TestCaseData("14", Pv(400, 250, 300, 250), marker);
                yield return new TestCaseData("15", Pv(500, 300, 300, 400), marker);
                yield return new TestCaseData("22", Pv(400, 600, 150, 300), marker);
                yield return new TestCaseData("24", Pv(500, 400, 300), marker);
                yield return new TestCaseData("33", Pv(900, 200, 300), marker);
                yield return new TestCaseData("46", Pv(200, 300, 400, 200, 150), marker);
                yield return new TestCaseData("51", Pv(400, 300, 0), marker);
            }
        }

        /// <summary>
        /// Ten sam odcinek (etykieta ramienia / łuku) wskazany na obrysie W4d (przed W4e) i W4e (po przebudowie) → ta sama
        /// długość rozkładu Manual: bez znacznika i dla kształtów bez zmiany układu ramion — wzorzec (sprzed W4); ze znacznikiem
        /// i zmienionym układem — odcinek W4d bez wymiarów zewnętrznych (D29). Obrót + odbicie, obie strony obrysu.
        /// </summary>
        [TestCaseSource(nameof(ManualCases))]
        public void Manual_W4eOutline_SameAsBeforeW4e(string code, double[] pv, bool marker)
        {
            var labels = BarGeometryBuilder.GetSegmentLabels(code, pv, D, BarGeometryOptions.Current)!;
            var wD = ToWorld(BarFrameRules.LocalOutline(code, pv, D, BarGeometryOptions.W4d)!, new BarFrame2(Math.PI / 6, true), 50, 60);
            var wE = ToWorld(BarFrameRules.LocalOutline(code, pv, D)!, new BarFrame2(Math.PI / 6, true), 50, 60);
            int compared = 0;
            foreach (var lab in labels.Distinct(new LabelEq()))
            foreach (bool left in new[] { true, false })
            {
                int sD = SegOf(code, pv, BarGeometryOptions.W4d, lab, left);
                int sE = SegOf(code, pv, BarGeometryOptions.Current, lab, left);
                if (sD < 0 || sE < 0) continue;
                double? before = BarFrameRules.LegacySegmentLength(wD, sD, code, pv, D, marker);
                double? after  = BarFrameRules.LegacySegmentLength(wE, sE, code, pv, D, marker);
                double lb = before ?? SegLen(wD, sD);
                Assert.That(after, Is.Not.Null, $"{code} {lab} left={left}: brak odpowiednika po W4e");
                Assert.That(after!.Value, Is.EqualTo(lb).Within(1e-9), $"{code} {lab} left={left} marker={marker}");
                compared++;
            }
            Assert.That(compared, Is.GreaterThanOrEqualTo(4), code);
        }

        private sealed class LabelEq : IEqualityComparer<BarSegmentLabel>
        {
            public bool Equals(BarSegmentLabel a, BarSegmentLabel b) => a.SameAs(b);
            public int GetHashCode(BarSegmentLabel l) => l.Kind * 10007 + l.Index * 101 + l.Sub;
        }

        // ── RC_SECTION: bez podwójnego odejmowania (dawne AxisParams usunięte) ─

        /// <summary>Kopia usuniętego AutoRebarEngine.AxisParams (sprzed W4e) — punkt odniesienia testu.</summary>
        private static double[] OldSectionAxisParams(string code, double[] p, double d)
        {
            var a = (double[])p.Clone();
            switch (code)
            {
                case "13": case "21":
                    a[0] = Math.Max(1, a[0] - d / 2); a[1] = Math.Max(1, a[1] - d); a[2] = Math.Max(0, a[2] - d / 2); break;
                case "22":
                    a[0] = Math.Max(1, a[0] - d / 2); a[1] = Math.Max(1, a[1] - d / 2); a[2] = Math.Max(0, a[2] - d); a[3] = Math.Max(0, a[3] - d / 2); break;
                case "11": case "12":
                    a[0] = Math.Max(1, a[0] - d / 2); a[1] = Math.Max(1, a[1] - d / 2); break;
            }
            return a;
        }

        [TestCase("21", 700, 140, 700, 0, 12)]
        [TestCase("21", 665, 215, 665, 0, 12)]
        [TestCase("13", 715, 115, 715, 0, 12)]
        [TestCase("13", 610, 70, 610, 0, 10)]
        [TestCase("11", 500, 300, 0, 0, 12)]
        [TestCase("12", 500, 300, 100, 0, 12)]
        // 22: dawne AxisParams liczyło B − d/2 (błąd, runda 1 uwaga 2) — wymiary 22 w widoku: OutlineEqualsExternalDimensions
        public void Section_OutlineExtentsSameAsBeforeW4e(string code, double a, double b, double c, double dd, double d)
        {
            // przed W4e: geometria W4d (oś) z AxisParams; po W4e: geometria bieżąca z wymiarów z rysunku — te same wymiary
            // zewnętrzne pręta w widoku (różnią się tylko łuki: promień osi r + d/2)
            var pv = Pv(a, b, c, dd);
            var before = Bbox(BarFrameRules.LocalOutlineFrom(
                BarGeometryBuilder.GetLocalPoints(code, OldSectionAxisParams(code, pv, d), d, BarGeometryOptions.W4d), d)!);
            var after = Bbox(BarFrameRules.LocalOutlineFrom(BarGeometryBuilder.GetLocalPoints(code, pv, d), d)!);
            Assert.That(after.MinX, Is.EqualTo(before.MinX).Within(BoxTol));
            Assert.That(after.MinY, Is.EqualTo(before.MinY).Within(BoxTol));
            Assert.That(after.MaxX, Is.EqualTo(before.MaxX).Within(BoxTol));
            Assert.That(after.MaxY, Is.EqualTo(before.MaxY).Within(BoxTol));
        }

        // ── Runda poprawek 1, uwaga 2: obrys = wymiary zewnętrzne (niezależnie od tabeli ToAxis) ──

        /// <summary>
        /// Prostokąt obrysu = wymiary zewnętrzne z rysunków Table 5 (oczekiwania z wymiarów, nie z BarDimensionRules):
        /// 11 / 12 / 13 / 33 — A × B, 21 — B × A, 22 — B × A (B od lica A do skrajni półkola), 51 / 63 — B × A,
        /// 44 — (A + C + E − 2d) × B (A i E do lica ramion B / D, C między licami zewnętrznymi — ramiona pionowe liczone raz).
        /// </summary>
        [TestCase("11", 500, 300, 0, 0, 0, 500, 300)]
        [TestCase("12", 500, 300, 100, 0, 0, 500, 300)]
        [TestCase("13", 500, 200, 300, 0, 0, 500, 200)]
        [TestCase("21", 500, 140, 500, 0, 0, 140, 500)]
        [TestCase("22", 400, 600, 150, 300, 0, 600, 400)]
        [TestCase("33", 900, 200, 300, 0, 0, 900, 200)]
        [TestCase("44", 500, 400, 500, 400, 300, 1276, 400)]
        [TestCase("51", 400, 300, 0, 0, 0, 300, 400)]
        [TestCase("51", 400, 300, 150, 0, 0, 300, 400)]
        [TestCase("63", 300, 200, 0, 0, 0, 200, 300)]
        public void OutlineEqualsExternalDimensions(string code, double a, double b, double c, double dd, double e, double w, double h)
        {
            var bb = Bbox(BarFrameRules.LocalOutline(code, Pv(a, b, c, dd, e), D)!);
            Assert.That(bb.MaxX - bb.MinX, Is.EqualTo(w).Within(BoxTol), code + " szerokość");
            Assert.That(bb.MaxY - bb.MinY, Is.EqualTo(h).Within(BoxTol), code + " wysokość");
        }

        /// <summary>
        /// Kąty ≠ 90° (rys. 5 — kąt rozwarty): wymiar ramienia = odległość wzdłuż ramienia między punktami odniesienia —
        /// wolny koniec / przecięcie lic zewnętrznych sąsiednich ramion (konstrukcja z osi: proste ramion z odcinków
        /// prostych, lica przesunięte o d/2 na stronę zewnętrzną zagięcia). 15: A, C; 24: A, B, C; 46: A, B, C, B, E.
        /// (14 — kąt ostry, rys. 4: Code14_AcuteAngle_Figure4_…)
        /// </summary>
        [TestCase("15", new double[] { 500, 300, 300, 400, 0 }, new double[] { 500, 300 })]
        [TestCase("24", new double[] { 500, 400, 300, 240, 320 }, new double[] { 500, 400, 300 })]
        [TestCase("46", new double[] { 200, 300, 400, 200, 150 }, new double[] { 200, 300, 400, 300, 200 })]
        public void ObtuseLegs_ToOuterFaceIntersections(string code, double[] pv, double[] expected)
        {
            var pts = BarGeometryBuilder.GetLocalPoints(code, pv, D);
            var labels = BarGeometryBuilder.GetSegmentLabels(code, pv, D, BarGeometryOptions.Current)!;
            var legs = new List<((double X, double Y) P, (double X, double Y) U)>();
            for (int j = 0; j < labels.Count; j++)
            {
                if (labels[j].Kind != BarSegmentLabel.Leg) continue;
                double ux = pts[j + 1].X - pts[j].X, uy = pts[j + 1].Y - pts[j].Y, ul = Math.Sqrt(ux * ux + uy * uy);
                legs.Add((pts[j], (ux / ul, uy / ul)));
            }
            Assert.That(legs.Count, Is.EqualTo(expected.Length), code);
            var x = new List<(double X, double Y)>();   // punkty odniesienia zagięć (przecięcia lic zewnętrznych)
            for (int k = 0; k + 1 < legs.Count; k++)
            {
                var (p1, u1) = legs[k]; var (p2, u2) = legs[k + 1];
                bool left = u1.X * u2.Y - u1.Y * u2.X > 0;
                (double X, double Y) N((double X, double Y) u) => left ? (u.Y, -u.X) : (-u.Y, u.X);   // strona zewnętrzna
                var n1 = N(u1); var n2 = N(u2);
                double ax = p1.X + n1.X * D / 2, ay = p1.Y + n1.Y * D / 2, bx = p2.X + n2.X * D / 2, by = p2.Y + n2.Y * D / 2;
                double det = u1.X * -u2.Y - u1.Y * -u2.X;
                double s = ((bx - ax) * -u2.Y - (by - ay) * -u2.X) / det;
                x.Add((ax + s * u1.X, ay + s * u1.Y));
            }
            for (int k = 0; k < legs.Count; k++)
            {
                var u = legs[k].U;
                (double X, double Y) from = k == 0 ? pts[0] : x[k - 1];
                (double X, double Y) to = k == legs.Count - 1 ? pts[pts.Count - 1] : x[k];
                double len = (to.X - from.X) * u.X + (to.Y - from.Y) * u.Y;
                Assert.That(len, Is.EqualTo(expected[k]).Within(1e-6), $"{code} ramię {k}");
            }
        }

        // ── Runda poprawek 1, uwaga 1: rozkład Manual na pierścieniu 75 ─────

        private static List<(double X, double Y)> Ring(double cx, double cy, double r, int n, double a0, bool cw)
            => Enumerable.Range(0, n).Select(i => { double t = a0 + (cw ? -1 : 1) * 2 * Math.PI * i / n; return (cx + r * Math.Cos(t), cy + r * Math.Sin(t)); }).ToList();

        [Test]
        public void Ring75_Manual_W4eOutline_SameChordAsBeforeW4e()
        {
            var pv = Pv(300, 100);
            double before = 2 * 156 * Math.Sin(Math.PI / 24);   // obrys zewnętrzny sprzed W4e: A/2 + d/2 = 156
            Assert.That(before, Is.EqualTo(40.724).Within(1e-3));
            foreach (bool cw in new[] { false, true })
            {
                var w4e = Ring(1000, -500, 150, 24, 0.3, cw);       // obrys po W4e (polilinia / pierwsza polilinia bloku)
                for (int s = 0; s < 23; s += 7)
                    Assert.That(BarFrameRules.LegacySegmentLength(w4e, s, "75", pv, D)!.Value, Is.EqualTo(before).Within(1e-9), $"cw={cw} s={s}");
                var old = Ring(1000, -500, 156, 24, 0.3, cw);       // obrys sprzed W4e → zmierzona (null)
                Assert.That(BarFrameRules.LegacySegmentLength(old, 3, "75", pv, D), Is.Null);
            }
            Assert.That(BarFrameRules.LegacySegmentLength(Ring(0, 0, 150, 24, 0, false), 23, "75", pv, D), Is.Null, "segIdx poza zakresem");
        }

        // ── Pomocnicze: kąt skosu z wymiarów zewnętrznych, LocalStart ───────

        [Test]
        public void SlopeAngle_FromUserDimensions_NotAxis()
        {
            // 46: kąt asin(D / B) z wymiarów podanych przez użytkownika (B zewnętrzne), mimo że oś B jest krótsza
            var pv = Pv(200, 300, 400, 200, 150);
            var pts = BarGeometryBuilder.GetLocalPoints("46", pv, D);
            Assert.That(pts.Count, Is.EqualTo(30), "4 łuki po 7 pkt + 2 końce");
            // prosty odcinek skosu B: koniec 1. łuku (7) → początek 2. łuku (8)
            double t = Math.Atan2(-(pts[8].Y - pts[7].Y), pts[8].X - pts[7].X);
            Assert.That(t, Is.EqualTo(Math.Asin(150.0 / 300.0)).Within(1e-12));
        }
    }
}
