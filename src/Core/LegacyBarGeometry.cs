using System;
using System.Collections.Generic;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Plan W4 (docs/PLAN_W4_BS8666.md, W4a-0) — ZAMROŻONA kopia geometrii prętów sprzed W4 (BarGeometryBuilder
    /// wzorca aba0ed1): parametry po pozycji, łuk zawsze z punktem styczności w odległości r od narożnika,
    /// promień 2d / 3.5d (bez Table 2), 44 = okrąg, 75 = helisa, odcinki zerowe bez ochrony.
    ///
    /// Służy WYŁĄCZNIE do rozpoznania ramy (obrót, odbicie) obrysów narysowanych przed W4
    /// (BarFrameRules.TryFitVariants, BarGeometryOptions.Legacy). NIE poprawiać — każda zmiana psuje rozpoznanie
    /// starych rysunków. Bez API BricsCAD (kompilowana także w testach jednostkowych).
    /// </summary>
    internal static class LegacyBarGeometry
    {
        /// <summary>Promień gięcia sprzed W4: 2d dla d ≤ 16, 3.5d dla d ≥ 20 (H25 → 87.5).</summary>
        internal static double MinBendRadius(double d) => d <= 16.0 ? 2.0 * d : 3.5 * d;

        /// <summary>Węzły osi w układzie lokalnym — dokładnie jak BarGeometryBuilder.GetLocalPoints sprzed W4.</summary>
        public static List<(double X, double Y)> GetLocalPoints(string shapeCode, double[] paramValues, double diameter)
        {
            if (shapeCode == "44") return CirclePoints(paramValues);
            if (shapeCode == "75") return SpiralPoints(paramValues);
            if (shapeCode == "13") return HairpinPoints(paramValues, diameter);

            double r = MinBendRadius(diameter);
            var sharp = GetSharpPoints(shapeCode, paramValues, r, diameter);
            if (sharp.Count < 3) return sharp;

            var result = new List<(double, double)>();
            result.Add(sharp[0]);
            for (int i = 1; i < sharp.Count - 1; i++)
                foreach (var p in BendArcPoints(sharp[i - 1], sharp[i], sharp[i + 1], r))
                    result.Add(p);
            result.Add(sharp[sharp.Count - 1]);
            return result;
        }

        /// <summary>
        /// Etykiety odcinków osi geometrii sprzed W4 (Plan W4, 0-POPRAWKI p.1) — liczone z liczby punktów, punkty
        /// GetLocalPoints bez zmian: n = 2 → ramię 0; 13 → ramię 0, półkole (łuk 1), ramię 1; inaczej n = 7N − 12
        /// (N ostrych punktów): ramię k = odcinek 7k, łuk w ostrym punkcie c (1..N−2), pododcinek m = odcinek 7(c−1)+1+m.
        /// Pierścień / helisa (44, 75 sprzed W4) i nietypowa liczba punktów → null.
        /// </summary>
        public static List<BarSegmentLabel> SegmentLabels(string shapeCode, double[] paramValues, double diameter)
        {
            if (shapeCode == "44" || shapeCode == "75") return null;
            int n = GetLocalPoints(shapeCode, paramValues, diameter).Count;
            var labels = new List<BarSegmentLabel>(Math.Max(0, n - 1));
            if (shapeCode == "13") { BarGeometryBuilder.HairpinLabels(n, labels); return labels; }
            if (n == 2) { labels.Add(new BarSegmentLabel(BarSegmentLabel.Leg, 0, 0)); return labels; }
            if (n < 2 || (n + 12) % 7 != 0) return null;
            for (int j = 0; j < n - 1; j++)
                labels.Add(j % 7 == 0 ? new BarSegmentLabel(BarSegmentLabel.Leg, j / 7, 0)
                                      : new BarSegmentLabel(BarSegmentLabel.Arc, j / 7 + 1, j % 7 - 1));
            return labels;
        }

        private static readonly double Cos45 = Math.Sqrt(2.0) / 2.0;

        private static List<(double X, double Y)> GetSharpPoints(
            string shapeCode, double[] paramValues, double r, double diameter)
        {
            double a = Param(paramValues, 0);
            double b = Param(paramValues, 1);
            double c = Param(paramValues, 2);
            double d = Param(paramValues, 3);
            double e = Param(paramValues, 4);

            switch (shapeCode ?? "00")
            {
                case "00":
                    return Pts((0, 0), (a, 0));
                case "11":
                    return Pts((0, 0), (a, 0), (a, b));
                case "12":
                    return Pts((0, 0), (a, 0), (a, b));
                case "14":
                    return Pts((0, 0), (a, 0), (a + b * Cos45, b * Cos45));
                case "15":
                    return Pts((0, 0), (a, 0), (a - b * Cos45, b * Cos45));
                case "21":
                {
                    double cv = paramValues != null && paramValues.Length > 2 ? paramValues[2] : b;
                    return Pts((0, 0), (0, -a), (b, -a), (b, cv - a));
                }
                case "22":
                    return Pts((0, a), (0, 0), (b, 0), (b, c));
                case "23":
                    return Pts((0, 0), (a, 0), (a, b), (a + c, b));
                case "24":
                {
                    const double angleRad = Math.PI / 6.0;
                    double skosDX = c * Math.Cos(angleRad);
                    double skosDY = c * Math.Sin(angleRad);
                    return Pts((0, 0), (a, 0), (a + skosDX, skosDY), (a + skosDX + b, skosDY));
                }
                case "25":
                    return Pts((0, 0), (a, 0), (a, b), (a + c, b), (a + c + d * Cos45, b + d * Cos45));
                case "26":
                    return Pts((0, 0), (a, 0), (a, -b), (a + c, -b));
                case "27":
                    return Pts((0, 0), (a, 0), (a + b, b), (a + b + c, b), (a + b + c, b + r));
                case "28":
                    return Pts((0, 0), (a, 0), (a + b, b), (a + b + c, b), (a + b + c, b - r));
                case "29":
                    return Pts((0, -r), (0, 0), (a, 0), (a + b, b), (a + b + c, b));
                case "31":
                    return Pts((0, 0), (a, 0), (a, b), (a + c, b), (a + c + d * Cos45, b + d * Cos45));
                case "32":
                    return Pts((0, 0), (a, 0), (a, b), (a + c, b), (a + c, -d));
                case "33":
                    return Pts((0, 0), (a, 0), (a, -b), (a + c, -b), (a + c, d));
                case "34":
                    return Pts((0, 0), (a, 0), (a, b), (0, b), (0, 0));
                case "35":
                    return Pts((0, 0), (a, 0), (a, a), (0, a), (0, 0));
                case "36":
                    return Pts((0, 0), (a, 0), (a, b), (0, c), (0, 0));
                case "41":
                    return Pts((0, 0), (a, 0), (a + b * Cos45, b * Cos45), (a, b), (0, 0));
                case "46":
                    return Pts((a / 2, 0), (a, b / 2), (a / 2, b), (0, b / 2), (a / 2, 0));
                case "47":
                    return Pts((0, 0), (a, 0), (a / 2, b), (0, 0));
                case "51":
                {
                    double hook51 = c > 0 ? c : Math.Max(16.0 * diameter, 160.0);
                    return Pts((a, b - hook51), (a, b), (0, b), (0, 0), (a, 0), (a, b), (a - hook51, b));
                }
                case "63":
                {
                    double hook63 = c > 0 ? c : Math.Max(14.0 * diameter, 150.0);
                    return Pts((0, hook63), (0, a), (b, a), (b, 0), (0, 0), (0, a), (b, a), (b, a - hook63));
                }
                case "56":
                case "64":
                    return Pts((0, 0), (a, 0), (a, b), (a + c, b), (a + c, b - d), (a + c + e, b - d));
                case "98":
                case "99":
                default:
                    return Pts((0, 0), (a, 0));
            }
        }

        private static List<(double X, double Y)> CirclePoints(double[] paramValues)
        {
            double diam = Param(paramValues, 0);
            double cx   = diam / 2.0;
            double cy   = diam / 2.0;
            double rad  = diam / 2.0;
            var pts = new List<(double, double)>(9);
            for (int i = 0; i <= 8; i++)
            {
                double angle = i * Math.PI / 4.0;
                pts.Add((cx + rad * Math.Cos(angle), cy + rad * Math.Sin(angle)));
            }
            return pts;
        }

        private static List<(double X, double Y)> HairpinPoints(double[] paramValues, double diameter)
        {
            double a = Param(paramValues, 0);
            double b = Param(paramValues, 1);
            double c = Param(paramValues, 2);
            double r = b / 2.0;
            double cx = a - r;
            double cy = r;

            var pts = new List<(double, double)>();
            pts.Add((0.0, 0.0));
            pts.Add((cx, 0.0));
            const int arcSteps = 12;
            double startAngle = -Math.PI / 2.0;
            double sweep = Math.PI;
            for (int i = 1; i <= arcSteps; i++)
            {
                double t = (double)i / arcSteps;
                double angle = startAngle + sweep * t;
                pts.Add((cx + r * Math.Cos(angle), cy + r * Math.Sin(angle)));
            }
            pts.Add((a - c, b));
            return pts;
        }

        private static List<(double X, double Y)> SpiralPoints(double[] paramValues)
        {
            double diam   = Param(paramValues, 0);
            double nTurns = Param(paramValues, 1);
            double pitch  = Param(paramValues, 2);
            int    n      = (int)Math.Max(1, Math.Round(nTurns));
            const  int stepsPerTurn = 12;
            int    total  = n * stepsPerTurn + 1;
            double radius = diam / 2.0;
            var pts = new List<(double, double)>(total);
            for (int i = 0; i < total; i++)
            {
                double angle = 2.0 * Math.PI * i / stepsPerTurn;
                pts.Add((radius + radius * Math.Cos(angle), pitch * i / stepsPerTurn));
            }
            return pts;
        }

        private static double Param(double[] pv, int i) => pv != null && pv.Length > i ? pv[i] : 0.0;

        private static List<(double X, double Y)> Pts(params (double X, double Y)[] points) =>
            new List<(double, double)>(points);

        private static IEnumerable<(double X, double Y)> BendArcPoints(
            (double X, double Y) prev, (double X, double Y) curr, (double X, double Y) next, double r, int steps = 6)
        {
            double d1x = curr.X - prev.X, d1y = curr.Y - prev.Y;
            double len1 = Math.Sqrt(d1x * d1x + d1y * d1y);
            d1x /= len1; d1y /= len1;

            double d2x = next.X - curr.X, d2y = next.Y - curr.Y;
            double len2 = Math.Sqrt(d2x * d2x + d2y * d2y);
            d2x /= len2; d2y /= len2;

            double tp1x = curr.X - d1x * r,  tp1y = curr.Y - d1y * r;
            double tp2x = curr.X + d2x * r,  tp2y = curr.Y + d2y * r;

            double cross = d1x * d2y - d1y * d2x;
            bool ccw = cross > 0;

            double nx = ccw ? -d1y :  d1y;
            double ny = ccw ?  d1x : -d1x;

            double cx = tp1x + nx * r;
            double cy = tp1y + ny * r;

            double startAngle = Math.Atan2(tp1y - cy, tp1x - cx);
            double endAngle   = Math.Atan2(tp2y - cy, tp2x - cx);

            double sweep = endAngle - startAngle;
            if ( ccw && sweep < 0) sweep += 2 * Math.PI;
            if (!ccw && sweep > 0) sweep -= 2 * Math.PI;

            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                double angle = startAngle + sweep * t;
                yield return (cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
            }
        }
    }
}
