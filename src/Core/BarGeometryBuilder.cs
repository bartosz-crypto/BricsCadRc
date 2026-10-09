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
    /// Każdy naróż zastępowany jest łukiem kołowym OSI pręta o promieniu r + d/2
    /// (r = BarShape.MinBendRadius — promień wewnętrzny gięcia wg BS 8666), punkty styczne
    /// w odległości R·tan(φ/2) od naroża (φ = kąt zmiany kierunku).
    ///
    /// Kształty z listy wyboru (00, 11, 13, 15, 21, 33, 44, 46, 51, 63): wymiary A–E są ZEWNĘTRZNE
    /// (BS 8666 kl. 7.1, jak w BBS) — tu przeliczane na oś pręta, więc obrys ma gabaryty z BBS.
    /// Pozostałe kody: wymiary jak dotąd (oś).
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
            if (IsLegacyRing(shapeCode, paramValues)) return CirclePoints(paramValues);
            if (shapeCode == "75") return SpiralPoints(paramValues);
            if (shapeCode == "13") return HairpinPoints(paramValues, diameter);
            if (shapeCode == "33") return LoopPoints(paramValues, diameter);

            double r = AxisBendRadius(diameter);
            var raw = GetSharpPoints(shapeCode, paramValues, BarShape.MinBendRadius(diameter), diameter);
            // odcinki zerowej długości (np. brak wymiaru) — bez nich (wcześniej NaN w łukach)
            var sharp = new List<(double X, double Y)>(raw.Count);
            foreach (var p in raw)
                if (sharp.Count == 0 || Math.Abs(p.X - sharp[sharp.Count - 1].X) + Math.Abs(p.Y - sharp[sharp.Count - 1].Y) > 1e-6)
                    sharp.Add(p);
            if (sharp.Count < 3) return sharp.Count >= 2 ? sharp : raw;

            var result = new List<(double, double)>();
            result.Add(sharp[0]);
            for (int i = 1; i < sharp.Count - 1; i++)
                foreach (var p in BendArcPoints(sharp[i - 1], sharp[i], sharp[i + 1], r))
                    result.Add(p);
            result.Add(sharp[sharp.Count - 1]);
            return result;
        }

        /// <summary>Promień łuku OSI pręta: promień wewnętrzny gięcia (BS 8666) + d/2.</summary>
        public static double AxisBendRadius(double diameter) => BarShape.MinBendRadius(diameter) + diameter / 2.0;

        /// <summary>
        /// Stary pierścień zapisany jako 44 (tylko A = średnica, B–E puste) — 44 w BS 8666 to „kapelusz”,
        /// ale stare rysunki z pierścieniem dalej rysują się jako okrąg.
        /// </summary>
        public static bool IsLegacyRing(string shapeCode, double[] pv)
            => shapeCode == "44" && Param(pv, 1) <= 0 && Param(pv, 2) <= 0 && Param(pv, 3) <= 0 && Param(pv, 4) <= 0;

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
            => BendArcPoints(prev, curr, next, AxisBendRadius(diameter));

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
                case "11": // BS 8666: (B) poziomo, A pionowo w górę; wymiary zewnętrzne
                {
                    double h = diameter / 2.0;
                    return Pts((0, 0), (b - h, 0), (b - h, a - h));
                }

                case "12": // L-bend z większym promieniem R (R = p[2] = LengthC)
                    return Pts((0, 0), (a, 0), (a, b));

                // ── Haki 45° i 135° ──────────────────────────────────────────
                case "14": // hook 45°
                    return Pts((0, 0), (a, 0), (a + b * Cos45, b * Cos45));

                case "15": // BS 8666: A skośnie (rzut pionowy B), potem (C) poziomo; L = A + (C)
                {
                    double h = diameter / 2.0;
                    double la = Math.Max(a - h, 1.0), lc = Math.Max(c - h, 0.0);
                    double sin = Math.Min(1.0, Math.Max(0.0, (b - diameter) / la));
                    double cos = Math.Sqrt(1.0 - sin * sin);
                    return Pts((0, la * sin), (la * cos, 0), (la * cos + lc, 0));
                }

                // ── U-bary ────────────────────────────────────────────────────
                case "21": // U-bar: A lewe ramię, B szerokość, (C) prawe ramię — wymiary zewnętrzne
                {
                    double h = diameter / 2.0;
                    double cv = paramValues != null && paramValues.Length > 2 ? paramValues[2] : b;
                    double aa = Math.Max(a - h, 1.0), bb = Math.Max(b - diameter, 1.0), cc = Math.Max(cv - h, 0.0);
                    return Pts((0, 0), (0, -aa), (bb, -aa), (bb, cc - aa));
                }

                case "22": // U-shape nierówny
                    return Pts((0, a), (0, 0), (b, 0), (b, c));

                // ── Z-bary i cranki ───────────────────────────────────────────
                case "23": // Z-bar (BS 8666) — prostokątne zgięcia 90°
                    return Pts((0, 0), (a, 0), (a, b), (a + c, b));

                case "24": // Crank łagodny (BS 8666) — skośny segment C pod kątem 30° od poziomu
                // TODO: zweryfikować kąt z pełnym tekstem BS 8666:2020 — 30° to prowizorka,
                //       norma może definiować specyficzny slope (np. 1:6 lub 1:12)
                {
                    const double angleRad = Math.PI / 6.0; // 30°
                    double skosDX = c * Math.Cos(angleRad);
                    double skosDY = c * Math.Sin(angleRad);
                    return Pts(
                        (0,              0),
                        (a,              0),
                        (a + skosDX,     skosDY),
                        (a + skosDX + b, skosDY));
                }

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

                case "46": // BS 8666: A poziomo, B skos w dół, C dno, B skos w górę, (E) poziomo; D = głębokość (zewn.)
                {
                    double dep = Math.Max(d - diameter, 0.0);
                    double sin = b > 1e-9 ? Math.Min(1.0, dep / b) : 0.0;
                    double dx = b * Math.Sqrt(1.0 - sin * sin), dy = b * sin;
                    return Pts((0, 0), (a, 0), (a + dx, -dy), (a + dx + c, -dy), (a + 2 * dx + c, 0), (a + 2 * dx + c + e, 0));
                }

                case "44": // BS 8666 „kapelusz”: A półka, B w dół, C dno, D w górę, (E) półka — wymiary zewnętrzne
                {
                    double h = diameter / 2.0;
                    double x1 = Math.Max(a - h, 0.0);
                    double x2 = x1 + Math.Max(c - diameter, 1.0);
                    double yb = -Math.Max(b - diameter, 1.0);
                    double yr = yb + Math.Max(d - diameter, 1.0);
                    return Pts((0, 0), (x1, 0), (x1, yb), (x2, yb), (x2, yr), (x2 + Math.Max(e - h, 0.0), yr));
                }

                case "47": // trójkąt
                    return Pts((0, 0), (a, 0), (a / 2, b), (0, 0));

                // ── Linki zamknięte z hakiem ──────────────────────────────────
                case "51": // BS8666: closed link — jeden pręt, overlap w górnym prawym rogu
                // Górny prawy narożnik odwiedzany DWUKROTNIE (oba haki wychodzą z tego samego rogu).
                // A × B zewnętrzne → oś (A−d) × (B−d). Hak = C (= D, od zewnętrznej krawędzi),
                // puste C → MAX(16d,160).
                {
                    double hook51 = c > 0 ? Math.Max(c - diameter / 2.0, 1.0) : Math.Max(16.0 * diameter, 160.0);
                    a = Math.Max(a - diameter, 1.0); b = Math.Max(b - diameter, 1.0);
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
                // A=wysokość, B=szerokość (zewnętrzne → oś −d). Hak = C (od zewnętrznej krawędzi),
                // puste C → MAX(14d,150).
                {
                    double hook63 = c > 0 ? Math.Max(c - diameter / 2.0, 1.0) : Math.Max(14.0 * diameter, 150.0);
                    a = Math.Max(a - diameter, 1.0); b = Math.Max(b - diameter, 1.0);
                    return Pts((0,         a - hook63),  // lewy hak koniec — free end (hak w dół od górnego rogu)
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

        // BS 8666 shape 13 — hairpin z łukiem 180°.
        // A = długa (dolna) noga, B = wysokość pętli oś-do-osi (= 2·r),
        // C = krótka (górna) noga. Promień łuku = B/2 (parametr usera, NIE MinBendRadius).
        // Długość fizyczna: (A - B/2) + π·B/2 + (C - B/2) = A + C + B·(π/2 - 1).
        // TODO: dla B/2 < MinBendRadius(d) bar naruszałby minimalny promień gięcia normy —
        //       nie blokujemy, plugin rysuje zgodnie z wymiarami podanymi przez usera.
        private static List<(double X, double Y)> HairpinPoints(double[] paramValues, double diameter)
        {
            // wymiary zewnętrzne (BS 8666) → oś: A, C − d/2 (do zewnętrznej łuku), B − d (wysokość pętli)
            double h = diameter / 2.0;
            double b = Math.Max(Param(paramValues, 1) - diameter, 1.0);
            double a = Math.Max(Param(paramValues, 0) - h, b / 2.0);
            double c = Math.Max(Param(paramValues, 2) - h, b / 2.0);
            double r = b / 2.0;

            // Środek łuku półkola: prawy kraniec pętli minus promień, na wysokości B/2
            double cx = a - r;
            double cy = r;

            var pts = new List<(double, double)>();

            // 1. Lewy koniec dolnej nogi
            pts.Add((0.0, 0.0));

            // 2. Punkt styczny dolnej nogi do łuku (= start półkola)
            pts.Add((cx, 0.0));

            // 3. Półkole 180° od dołu (kąt -π/2) do góry (kąt +π/2), 13 punktów pośrednich
            //    (12 kroków po 15°). Pomijamy endpoint startu bo już dodany w pkt. 2.
            const int arcSteps = 12;
            double startAngle = -Math.PI / 2.0;       // punkt (cx, 0) = dół
            double sweep = Math.PI;                    // +180°, obrót CCW
            for (int i = 1; i <= arcSteps; i++)
            {
                double t = (double)i / arcSteps;
                double angle = startAngle + sweep * t;
                pts.Add((cx + r * Math.Cos(angle), cy + r * Math.Sin(angle)));
            }

            // Teraz ostatni dodany punkt to (cx, 2r) = (a - r, b) — koniec półkola, góra

            // 4. Lewy koniec górnej nogi
            pts.Add((a - c, b));

            return pts;
        }

        /// <summary>
        /// BS 8666 kształt 33: pętla zamknięta z dwoma półkolami. A = długość całkowita, B = szerokość
        /// (zewnętrzne), (C) = zakład końców na górnej prostej (od wolnego końca do zewnętrznej łuku).
        /// Oba końce leżą na górnej prostej i na długości zakładu na siebie nachodzą.
        /// </summary>
        private static List<(double X, double Y)> LoopPoints(double[] paramValues, double diameter)
        {
            double A = Param(paramValues, 0), B = Param(paramValues, 1), C = Param(paramValues, 2);
            double h = diameter / 2.0;
            double rho = Math.Max((B - diameter) / 2.0, diameter);      // promień osi półkola
            double lc  = Math.Max(A - B, 0.0);                          // rozstaw środków półkoli
            double xEnd = Math.Min(lc + rho + h - Math.Max(C, 0.0), lc);
            const int steps = 12;
            var pts = new List<(double, double)> { (lc, rho), (0, rho) };
            for (int i = 1; i <= steps; i++)                            // lewe półkole: 90° → 270°
            {
                double t = Math.PI / 2 + Math.PI * i / steps;
                pts.Add((rho * Math.Cos(t), rho * Math.Sin(t)));
            }
            pts.Add((lc, -rho));
            for (int i = 1; i <= steps; i++)                            // prawe półkole: −90° → 90°
            {
                double t = -Math.PI / 2 + Math.PI * i / steps;
                pts.Add((lc + rho * Math.Cos(t), rho * Math.Sin(t)));
            }
            // koniec po półkolu wraca po górnej prostej na długości zakładu — ramiona na siebie nachodzą
            pts.Add((Math.Min(xEnd, lc), rho));
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
            double d2x = next.X - curr.X, d2y = next.Y - curr.Y;
            double len2 = Math.Sqrt(d2x * d2x + d2y * d2y);
            if (len1 < 1e-9 || len2 < 1e-9) { yield return curr; yield break; }
            d1x /= len1; d1y /= len1;
            d2x /= len2; d2y /= len2;

            // Kąt zmiany kierunku φ; punkty styczne w odległości R·tan(φ/2) od naroża
            double cross = d1x * d2y - d1y * d2x;
            double dot   = d1x * d2x + d1y * d2y;
            double phi   = Math.Atan2(Math.Abs(cross), dot);
            if (phi < 1e-6 || phi > Math.PI - 1e-3) { yield return curr; yield break; }
            double t = r * Math.Tan(phi / 2.0);
            double tMax = Math.Min(len1, len2);
            if (t > tMax) { t = tMax; r = t / Math.Tan(phi / 2.0); }   // krótkie ramię — mniejszy łuk

            double tp1x = curr.X - d1x * t,  tp1y = curr.Y - d1y * t;
            bool ccw = cross > 0;

            // Normalna wewnętrzna (ku środkowi łuku) w tp1
            double nx = ccw ? -d1y :  d1y;
            double ny = ccw ?  d1x : -d1x;

            double cx = tp1x + nx * r;
            double cy = tp1y + ny * r;

            double startAngle = Math.Atan2(tp1y - cy, tp1x - cx);
            double sweep = ccw ? phi : -phi;

            for (int i = 0; i <= steps; i++)
            {
                double k     = (double)i / steps;
                double angle = startAngle + sweep * k;
                yield return (cx + r * Math.Cos(angle), cy + r * Math.Sin(angle));
            }
        }
    }
}
