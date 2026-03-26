using System;
using System.Collections.Generic;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Czysta geometria kształtów prętów wg BS8666.
    /// Nie zależy od BricsCAD API — testowalny w net8.0.
    ///
    /// Układ lokalny:
    ///   X = wzdłuż pręta (wymiar A)
    ///   Y = prostopadły, "w górę" w widoku elewacji
    ///
    /// Każdy naróż wewnętrzny zastępowany jest łukiem kołowym
    /// o promieniu r = BarShape.MinBendRadius(diameter).
    /// Łuk aproksymowany jest (steps+1) punktami co 15° (steps=6 → 7 pkt).
    /// </summary>
    public static class BarGeometryBuilder
    {
        private static readonly HashSet<string> _supported = new HashSet<string>
        {
            "00", "11", "12", "13", "14", "15",
            "21", "22", "23", "24", "25", "26", "27", "28", "29",
            "31", "32", "33", "34", "35", "36",
            "41", "44", "46", "47",
            "51", "56", "63", "64",
            "75", "98", "99"
        };

        /// <summary>Zwraca true jeśli shape code jest zaimplementowany (nie fallback).</summary>
        public static bool IsSupported(string shapeCode) =>
            shapeCode != null && _supported.Contains(shapeCode);

        /// <summary>
        /// Zwraca węzły polilinii w układzie lokalnym pręta z łukami zagięć.
        /// Każde zagięcie = 7 punktów łuku (steps=6, co 15°) zamiast 1 ostrego węzła.
        /// Nieobsługiwane shape codes → fallback do 00 (prosta linia A).
        /// Kody 44 i 75 zwracają punkty bezpośrednio bez aproksymacji zagięć.
        /// </summary>
        public static List<(double X, double Y)> GetLocalPoints(
            string shapeCode, double[] paramValues, double diameter)
        {
            if (shapeCode == "44") return CirclePoints(paramValues);
            if (shapeCode == "75") return SpiralPoints(paramValues);

            double r = BarShape.MinBendRadius(diameter);
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
        /// Generuje (steps+1) punktów na łuku kołowym.
        /// Kąty w stopniach; interpolacja liniowa kąta od startAngleDeg do endAngleDeg.
        /// </summary>
        public static List<(double X, double Y)> ArcPoints(
            (double X, double Y) center, double radius,
            double startAngleDeg, double endAngleDeg, int steps = 6)
        {
            var pts = new List<(double, double)>(steps + 1);
            double startRad = startAngleDeg * Math.PI / 180.0;
            double endRad   = endAngleDeg   * Math.PI / 180.0;
            for (int i = 0; i <= steps; i++)
            {
                double t     = (double)i / steps;
                double angle = startRad + (endRad - startRad) * t;
                pts.Add((center.X + radius * Math.Cos(angle),
                         center.Y + radius * Math.Sin(angle)));
            }
            return pts;
        }

        /// <summary>
        /// 7-punktowa aproksymacja łuku na narożniku prev→curr→next.
        /// Identyczna logika jak wewnętrzna BendArcPoints używana przez GetLocalPoints.
        /// </summary>
        public static IEnumerable<(double X, double Y)> CornerArcPoints(
            (double X, double Y) prev, (double X, double Y) curr, (double X, double Y) next,
            double diameter)
            => BendArcPoints(prev, curr, next, BarShape.MinBendRadius(diameter));

        // ── Private helpers ───────────────────────────────────────────────────

        // cos45° = sin45° = √2/2
        private static readonly double Cos45 = Math.Sqrt(2.0) / 2.0;

        /// <summary>Zwraca ostre węzły kształtu (bez aproksymacji łuków).</summary>
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
                // ── Prosta ────────────────────────────────────────────────────
                case "00":
                    return Pts((0, 0), (a, 0));

                // ── Haki jednostronne / obustronne ───────────────────────────
                case "11": // hak 90° na prawym końcu
                    return Pts((0, 0), (a, 0), (a, b));

                case "12": // haki 90° na obu końcach
                    return Pts((0, b), (0, 0), (a, 0), (a, b));

                // ── Crank 45° (schodek) ───────────────────────────────────────
                case "13":
                    return Pts((0, 0), (a, 0), (a + b, b), (a + b + c, b));

                // ── Haki 45° i 135° ──────────────────────────────────────────
                case "14": // hook 45°
                    return Pts((0, 0), (a, 0), (a + b * Cos45, b * Cos45));

                case "15": // hook 135°
                    return Pts((0, 0), (a, 0), (a - b * Cos45, b * Cos45));

                // ── U-bary ────────────────────────────────────────────────────
                case "21": // U-bar symetryczny/asymetryczny
                {
                    double cv = paramValues != null && paramValues.Length > 2 ? paramValues[2] : b;
                    return Pts((0, 0), (0, -a), (b, -a), (b, cv - a));
                }

                case "22": // U-shape nierówny
                    return Pts((0, a), (0, 0), (b, 0), (b, c));

                // ── Z-bary i cranki ───────────────────────────────────────────
                case "23": // Z-bar
                case "24": // Crank łagodny (ta sama geometria)
                    return Pts((0, 0), (a, 0), (a, b), (a + c, b));

                case "25": // Hook + crank
                    return Pts((0, 0), (a, 0), (a, b), (a + c, b),
                               (a + c + d * Cos45, b + d * Cos45));

                case "26": // Hook + leg (w dół)
                    return Pts((0, 0), (a, 0), (a, -b), (a + c, -b));

                case "27": // Crank z hakiem górnym (stub = r)
                    return Pts((0, 0), (a, 0), (a + b, b), (a + b + c, b),
                               (a + b + c, b + r));

                case "28": // Crank z hakiem dolnym (stub = r)
                    return Pts((0, 0), (a, 0), (a + b, b), (a + b + c, b),
                               (a + b + c, b - r));

                case "29": // Crank symetryczny (stub startowy = r)
                    return Pts((0, -r), (0, 0), (a, 0), (a + b, b), (a + b + c, b));

                // ── Kształty Z + hak / S ──────────────────────────────────────
                case "31": // Z + hook 45°
                    return Pts((0, 0), (a, 0), (a, b), (a + c, b),
                               (a + c + d * Cos45, b + d * Cos45));

                case "32": // S-shape
                    return Pts((0, 0), (a, 0), (a, b), (a + c, b), (a + c, -d));

                case "33": // S-shape odwrócony
                    return Pts((0, 0), (a, 0), (a, -b), (a + c, -b), (a + c, d));

                // ── Zamknięte prostokąty / kwadraty ──────────────────────────
                case "34": // prostokąt zamknięty A×B
                    return Pts((0, 0), (a, 0), (a, b), (0, b), (0, 0));

                case "35": // kwadrat zamknięty A×A
                    return Pts((0, 0), (a, 0), (a, a), (0, a), (0, 0));

                case "36": // prostokąt nierówny (różne wysokości lewej i prawej)
                    return Pts((0, 0), (a, 0), (a, b), (0, c), (0, 0));

                // ── Inne zamknięte kształty ───────────────────────────────────
                case "41": // wielokąt 4-boczny z ukośnym narożnikiem
                    return Pts((0, 0), (a, 0), (a + b * Cos45, b * Cos45), (a, b), (0, 0));

                case "46": // romb
                    return Pts((a / 2, 0), (a, b / 2), (a / 2, b), (0, b / 2), (a / 2, 0));

                case "47": // trójkąt
                    return Pts((0, 0), (a, 0), (a / 2, b), (0, 0));

                // ── Linki zamknięte z hakiem ──────────────────────────────────
                case "51": // BS8666: closed link — jeden pręt, overlap w górnym prawym rogu
                // Górny prawy narożnik odwiedzany DWUKROTNIE (oba haki wychodzą z tego samego rogu).
                // Daje 5 narożników 90° (wszystkie identyczne jak w shape 34).
                {
                    double hook51 = c > 0 ? c : Math.Max(16.0 * diameter, 160.0);
                    return Pts((a, b - hook51),    // prawy hak (dół)
                               (a, b),             // górny prawy — 1. przejście: UP→LEFT
                               (0, b),             // górny lewy — LEFT→DOWN
                               (0, 0),             // dolny lewy — DOWN→RIGHT
                               (a, 0),             // dolny prawy — RIGHT→UP
                               (a, b),             // górny prawy — 2. przejście (overlap): UP→LEFT
                               (a - hook51, b));   // lewy hak (top)
                }

                case "63": // BS8666: closed link — haki PIONOWO W DÓŁ z obu górnych rogów
                // Jeden ciągły pręt, double-visit na górnych rogach (jak shape 51).
                // 8 węzłów, 6 narożników 90° CW → 1+6×7+1=44 pkt
                // A=wysokość (p[0]), B=szerokość (p[1]), C=hook (p[2])
                {
                    double hook63 = c > 0 ? c : Math.Max(14.0 * diameter, 150.0);
                    return Pts((0,         hook63),      // lewy hak koniec — free end
                               (0,         a),           // górny lewy — UP→RIGHT   (CW, 1. wizyta)
                               (b,         a),           // górny prawy — RIGHT→DOWN (CW, 1. wizyta)
                               (b,         0),           // dolny prawy — DOWN→LEFT  (CW)
                               (0,         0),           // dolny lewy — LEFT→UP    (CW)
                               (0,         a),           // górny lewy — UP→RIGHT   (CW, 2. wizyta)
                               (b,         a),           // górny prawy — RIGHT→DOWN (CW, 2. wizyta)
                               (b,         a - hook63)); // prawy hak koniec — free end
                }

                // ── Złożone 5-ramienne ────────────────────────────────────────
                case "56": // complex 5-leg
                case "64": // complex 5-leg variant (ta sama geometria)
                    return Pts((0, 0), (a, 0), (a, b), (a + c, b),
                               (a + c, b - d), (a + c + e, b - d));

                // ── Custom / fallback ─────────────────────────────────────────
                case "98":
                case "99":
                default:
                    return Pts((0, 0), (a, 0));
            }
        }


        /// <summary>
        /// Okrąg aproksymowany 8 punktami co 45°, Closed=true (9 pkt: start=koniec).
        /// Parametr A = średnica; środek w (A/2, A/2).
        /// </summary>
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

        /// <summary>
        /// Spirala aproksymowana: B zwojów po 12 punktów, skok C (pitch).
        /// A = średnica, B = liczba zwojów, C = skok.
        /// Zwraca B*12 + 1 punktów.
        /// </summary>
        private static List<(double X, double Y)> SpiralPoints(double[] paramValues)
        {
            double diam   = Param(paramValues, 0);   // A = średnica
            double nTurns = Param(paramValues, 1);   // B = liczba zwojów
            double pitch  = Param(paramValues, 2);   // C = skok
            int    n      = (int)Math.Max(1, Math.Round(nTurns));
            const  int stepsPerTurn = 12;
            int    total  = n * stepsPerTurn + 1;
            double radius = diam / 2.0;
            var pts = new List<(double, double)>(total);
            for (int i = 0; i < total; i++)
            {
                double angle = 2.0 * Math.PI * i / stepsPerTurn;
                pts.Add((radius + radius * Math.Cos(angle),
                         pitch * i / stepsPerTurn));
            }
            return pts;
        }

        // ── Micro-helpers ─────────────────────────────────────────────────────

        private static double Param(double[] pv, int i) =>
            pv != null && pv.Length > i ? pv[i] : 0.0;

        private static List<(double X, double Y)> Pts(params (double X, double Y)[] points) =>
            new List<(double, double)>(points);

        /// <summary>
        /// Zastępuje ostry naróż w <paramref name="curr"/> serią 7 punktów łuku
        /// (od punktu stycznego tp1 do tp2).
        /// </summary>
        private static IEnumerable<(double X, double Y)> BendArcPoints(
            (double X, double Y) prev,
            (double X, double Y) curr,
            (double X, double Y) next,
            double r, int steps = 6)
        {
            // Wektory jednostkowe kierunków
            double d1x = curr.X - prev.X, d1y = curr.Y - prev.Y;
            double len1 = Math.Sqrt(d1x * d1x + d1y * d1y);
            d1x /= len1; d1y /= len1;

            double d2x = next.X - curr.X, d2y = next.Y - curr.Y;
            double len2 = Math.Sqrt(d2x * d2x + d2y * d2y);
            d2x /= len2; d2y /= len2;

            // Punkty styczne (r od narożnika, na odpowiednich odcinkach)
            double tp1x = curr.X - d1x * r,  tp1y = curr.Y - d1y * r;
            double tp2x = curr.X + d2x * r,  tp2y = curr.Y + d2y * r;

            // Iloczyn wektorowy: > 0 → skręt CCW (w lewo)
            double cross = d1x * d2y - d1y * d2x;
            bool ccw = cross > 0;

            // Normalna wewnętrzna (ku środkowi łuku) w tp1
            double nx = ccw ? -d1y :  d1y;
            double ny = ccw ?  d1x : -d1x;

            double cx = tp1x + nx * r;
            double cy = tp1y + ny * r;

            double startAngle = Math.Atan2(tp1y - cy, tp1x - cx);
            double endAngle   = Math.Atan2(tp2y - cy, tp2x - cx);

            // Wymuś właściwy kierunek obrotu
            double sweep = endAngle - startAngle;
            if ( ccw && sweep < 0) sweep += 2 * Math.PI;
            if (!ccw && sweep > 0) sweep -= 2 * Math.PI;

            for (int i = 0; i <= steps; i++)
            {
                double t     = (double)i / steps;
                double angle = startAngle + sweep * t;
                yield return (cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
            }
        }
    }
}
