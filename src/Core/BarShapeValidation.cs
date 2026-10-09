using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

// Kompilowany także w tests (net8.0, Nullable enable) — kontekst nullable jak we wtyczce (wyłączony).
#nullable disable

namespace BricsCadRc.Core
{
    /// <summary>
    /// Plan W4f (docs/PLAN_W4_BS8666.md) — walidacja wymiarów pręta wg BS 8666:2020: wyłącznie OSTRZEŻENIA (dialog RC_BAR /
    /// RC_EDIT_BAR / picker — TextBlock, OK nie blokowane; RC_BBS — lista ostrzeżeń). Czysta logika bez API BricsCAD.
    /// Reguły (wymiary zewnętrzne jak w XData, pola po nazwie parametru kształtu):
    ///  - kl. 8.1.4: żaden wymiar nie może być zerowy (poza wymiarami opcjonalnymi — kąt W4d, C 51 / 63); 12: R = 0 → rysunek
    ///    z r min, długość wzorem z R = 0 (W5: komunikat zgodny z obliczeniem);
    ///  - brakujące wymiary kąta (BarShape.DefaultAngleWarning);
    ///  - Table 5: wolne końce ≥ P (Table 2 — kolumna wg kąta zagięcia końcowego: strzemiona z zagięciem końcowym &lt; 150°
    ///    (51, 63 — haki 90°) — „links”; pozostałe, także 47 z hakami 180° — „general bend”), 12: A, B ≥ (R + d) + max(5d, 90),
    ///    13: q ≤ B ≤ 400 + 2d, A, C ≥ B/2 + max(5d, 90); 22: q ≤ C ≤ 400 + 2d, D ≥ C/2 + max(5d, 90);
    ///    33: q ≤ B ≤ 400 + 2d, C ≥ B/2 + max(5d, 90), A ≥ B/2 + C; 47 / 51 / 63: hak C ≤ A (51: także ≤ B), przy pustym C
    ///    — hak minimalny C_eff (geometria, XData bez zmian);
    ///  - kl. 8.4: prosty odcinek osi między łukami dwóch zagięć ≥ 4d (kształty z geometrią wg normy; np. 22: narożnik–półkole);
    ///  - Table 4 (kl. 8.4): wymiar między dwoma zagięciami ≥ X = 10d (min. 75) dla d ≤ 16, 13d dla d &gt; 16 —
    ///    21 B, 23 B, 44 B / C / D, 46 C, 51 A / B, 63 A / B;
    ///  - kl. 8.2.4 (korby — 26, 46): skos B ≥ 10d (min. 75) / 13d, przesunięcie (46: D) ≥ 2d;
    ///  - kl. 7.2: wymiary co 5 mm; kl. 7.9: krótszy bok prostokąta obrysu ≤ 2750 mm, długość &gt; 12 m — uwaga;
    ///  - W5: długość wg wzoru ≤ 0 / nieokreślona (InvalidBarLengthException) — uwaga (BBS: „brak długości”).
    /// Kształty z geometrią do W4d-3 (25–29, 31, 32, 34–36, 41, 56, 64, 98) — tylko reguły P i ogólne.
    /// </summary>
    public static class BarShapeValidation
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Wolne końce ≥ P (Table 5, „Neither … shall be less than P in Table 2”).</summary>
        private static readonly Dictionary<string, string[]> EndProjection = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["11"] = new[] { "A", "B" }, ["14"] = new[] { "A", "C" }, ["15"] = new[] { "A", "C" },
            ["21"] = new[] { "A", "C" }, ["22"] = new[] { "A", "D" }, ["23"] = new[] { "A", "C" },
            ["24"] = new[] { "A", "C" }, ["25"] = new[] { "A", "B" }, ["26"] = new[] { "A", "C" },
            ["27"] = new[] { "A", "C" }, ["28"] = new[] { "A", "C" }, ["29"] = new[] { "A", "C" },
            ["31"] = new[] { "A", "D" }, ["32"] = new[] { "A", "D" }, ["34"] = new[] { "A", "E" },
            ["35"] = new[] { "A", "E" }, ["36"] = new[] { "A", "D" }, ["41"] = new[] { "A", "E" },
            ["44"] = new[] { "A", "E" }, ["46"] = new[] { "A", "E" }, ["47"] = new[] { "C" },
            ["51"] = new[] { "C" },      ["56"] = new[] { "E" },      ["63"] = new[] { "C" },
            ["64"] = new[] { "A" },      ["98"] = new[] { "C", "D" },
        };

        /// <summary>Wymiary między dwoma zagięciami (Table 4).</summary>
        private static readonly Dictionary<string, string[]> BetweenBends = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["21"] = new[] { "B" }, ["23"] = new[] { "B" }, ["44"] = new[] { "B", "C", "D" },
            ["46"] = new[] { "C" }, ["51"] = new[] { "A", "B" }, ["63"] = new[] { "A", "B" },
        };

        /// <summary>
        /// Ostrzeżenia dla kształtu code, wymiarów pv (pv[i] = Parameters[i], jak XData) i średnicy d. Kolejność: wymiary zerowe,
        /// kąt, Table 5, Table 4, korby, 5 mm, kl. 7.9. Bez prefiksu i znaków nowej linii; pusta lista = brak uwag.
        /// Nieznany kształt / d ≤ 0 → pusta lista.
        /// </summary>
        public static IReadOnlyList<string> Check(string code, double[] pv, double d)
        {
            var w = new List<string>();
            var shape = ShapeCodeLibrary.Get(code);
            if (shape == null || d <= 0) return w;
            pv = pv ?? new double[0];
            double V(string n) => shape.Value(pv, n);

            // kl. 8.1.4 — wymiar zerowy
            for (int i = 0; i < shape.Parameters.Length; i++)
            {
                if (shape.IsOptionalParam(i)) continue;
                string n = shape.Parameters[i];
                double v = i < pv.Length ? pv[i] : 0.0;
                if (v > 0) continue;
                if (code == "12" && n == "R")
                    // W5: długość liczona wzorem z R = 0 (A + B − 1.2d), tylko rysunek ma minimalny promień — komunikat jak obliczenie
                    w.Add("R = 0 — długość liczona wzorem z R = 0 (A + B − 1.2d, dłuższa niż z rzeczywistym promieniem), rysunek z minimalnym promieniem gięcia r = "
                        + F(BarShape.MinBendRadius(d)) + " mm (BS 8666:2020 kl. 8.1.4: podaj R).");
                else
                    w.Add("Wymiar " + n + " = 0 (BS 8666:2020 kl. 8.1.4: żaden wymiar nie może być zerowy).");
            }

            // brakujące wymiary kąta (W4d)
            string ang = shape.DefaultAngleWarning(pv);
            if (ang != null) w.Add(ang);

            // Table 5 — wolne końce ≥ P (Table 2)
            if (EndProjection.TryGetValue(code, out var ends))
            {
                double p = BarShape.MinEndProjectionP(d, EndBendBelow150(code));
                if (!double.IsNaN(p))
                    foreach (var n in ends) Min(w, n, V(n), p, "P", "Table 2");
            }

            double q = HookQ(d);
            double maxQ = 400.0 + 2.0 * d;
            double straight = Math.Max(5.0 * d, 90.0);
            switch (code)
            {
                case "12":
                {
                    double r = V("R") > 0 ? V("R") : BarShape.MinBendRadius(d);
                    double lim = r + d + straight;
                    Min(w, "A", V("A"), lim, "(R + d) + max(5d, 90)", "Table 5");
                    Min(w, "B", V("B"), lim, "(R + d) + max(5d, 90)", "Table 5");
                    break;
                }
                case "13":
                    LoopRange(w, "B", V("B"), q, maxQ);
                    Min(w, "A", V("A"), V("B") / 2.0 + straight, "B/2 + max(5d, 90)", "Table 5");
                    Min(w, "C", V("C"), V("B") / 2.0 + straight, "B/2 + max(5d, 90)", "Table 5");
                    break;
                case "22":
                    LoopRange(w, "C", V("C"), q, maxQ);
                    Min(w, "D", V("D"), V("C") / 2.0 + straight, "C/2 + max(5d, 90)", "Table 5");
                    break;
                case "33":
                    LoopRange(w, "B", V("B"), q, maxQ);
                    Min(w, "C", V("C"), V("B") / 2.0 + straight, "B/2 + max(5d, 90)", "Table 5");
                    Min(w, "A", V("A"), V("B") / 2.0 + V("C"), "B/2 + C", "Table 5");
                    break;
                case "47":
                    Max(w, "C", V("C"), V("A"), "A");
                    break;
                case "63":
                    Max(w, "C", V("C"), V("A"), "A");
                    if (V("C") <= 0) MaxEff(w, BarShape.Code63EffectiveHook(d), V("A"), "A");
                    break;
                case "51":
                    Max(w, "C", V("C"), Math.Min(V("A"), V("B")), "min(A, B)");
                    if (V("C") <= 0) MaxEff(w, BarShape.Code51EffectiveHook(d), Math.Min(V("A"), V("B")), "min(A, B)");
                    break;
            }

            // kl. 8.4 — prosty odcinek osi między łukami zagięć ≥ 4d
            if (NormGeometry.Contains(code)) StraightBetweenBends(w, code, pv, d);

            // Table 4 — wymiar między dwoma zagięciami
            if (BetweenBends.TryGetValue(code, out var between))
                foreach (var n in between) Min(w, n, V(n), BetweenBendsX(d), "X", "Table 4");

            // kl. 8.2.4 — korby: skos ≥ 10d (min. 75) / 13d, przesunięcie ≥ 2d
            if (code == "26" || code == "46")
            {
                Min(w, "B", V("B"), BetweenBendsX(d), d <= 16 ? "10d (min. 75)" : "13d", "kl. 8.2.4");
                if (code == "46") Min(w, "D", V("D"), 2.0 * d, "2d", "kl. 8.2.4");
            }

            // kl. 7.2 — wymiary co 5 mm
            for (int i = 0; i < shape.Parameters.Length && i < pv.Length; i++)
            {
                double v = pv[i];
                if (v > 0 && Math.Abs(v / 5.0 - Math.Round(v / 5.0)) * 5.0 > 1e-3)
                    w.Add(shape.Parameters[i] + " = " + F(v) + " mm — wymiar nie jest wielokrotnością 5 mm (BS 8666:2020 kl. 7.2).");
            }

            // kl. 7.9 — prostokąt obrysu (krótszy bok ≤ 2750) i długość > 12 m
            if (code != "00" && code != "99")
            {
                double shortSide = ShortSide(code, pv, d);
                if (shortSide > 2750.0)
                    w.Add("Pręt nie mieści się w prostokącie o krótszym boku 2750 mm (krótszy bok " + F(shortSide)
                        + " mm) — BS 8666:2020 kl. 7.9 (transport).");
            }
            try
            {
                var p = new double[shape.Parameters.Length];
                for (int i = 0; i < p.Length && i < pv.Length; i++) p[i] = pv[i];
                double len = shape.CalculateTotalLength(p, d);
                if (len > 12000.0)
                    w.Add("Długość pręta " + F(len) + " mm > 12 m — zwykle maks. 12 m, dłuższe (do 18 m) po uzgodnieniu z dostawcą (BS 8666:2020 kl. 7.9).");
            }
            catch (InvalidBarLengthException ex)
            {
                // W5: długość ≤ 0 / nieokreślona — BBS i zestawienie pokażą „brak długości”
                w.Add("Brak długości: " + ex.Message);
            }
            catch (Exception ex)
            {
                // brak długości (47 bez C, 64) — komunikat daje formuła / BBS; tu tylko log
                Log.Info("BarShapeValidation.Check: brak długości kształtu " + code + " — " + ex.Message);
            }
            return w;
        }

        /// <summary>Kształty z geometrią wg rysunków normy (W4d / W4e) — reguła kl. 8.4 liczona z osi.</summary>
        private static readonly HashSet<string> NormGeometry = new HashSet<string>(StringComparer.Ordinal)
        {
            "11", "12", "13", "14", "15", "21", "22", "23", "24", "33", "44", "46", "51", "63",
        };

        /// <summary>
        /// Table 2 — kolumna P wg kąta zagięcia końcowego: „Links where end bend &lt; 150°” tylko dla strzemion z zagięciem
        /// końcowym &lt; 150° (51, 63 — haki 90° w geometrii); „General bend (bob) or links where bend ≥ 150°” — pozostałe
        /// (pręty gięte; 47 — haki 180°).
        /// </summary>
        private static bool EndBendBelow150(string code) => code == "51" || code == "63";

        /// <summary>
        /// kl. 8.4: każdy prosty odcinek osi między łukami dwóch zagięć (etykieta ramienia nie na końcu pręta) ≥ 4d. Odcinek
        /// zerowy (łuki stykają się — brak ramienia) albo „wsteczny” (łuki zachodzą) → długość ≤ 0. Oś bieżąca (W4e).
        /// </summary>
        private static void StraightBetweenBends(List<string> w, string code, double[] pv, double d)
        {
            try
            {
                var opt = BarGeometryOptions.Current;
                var pts = BarGeometryBuilder.GetLocalPoints(code, pv, d, opt);
                var labels = BarGeometryBuilder.GetSegmentLabels(code, pv, d, opt);
                if (pts == null || labels == null || labels.Count + 1 != pts.Count) return;
                double min = 4.0 * d, worst = double.PositiveInfinity;
                for (int j = 1; j + 1 < labels.Count; j++)
                {
                    double len = double.PositiveInfinity;
                    if (labels[j].Kind == BarSegmentLabel.Leg)
                    {
                        double sx = pts[j + 1].X - pts[j].X, sy = pts[j + 1].Y - pts[j].Y;
                        double tx = pts[j].X - pts[j - 1].X, ty = pts[j].Y - pts[j - 1].Y;   // kierunek wyjścia z łuku
                        len = Math.Sqrt(sx * sx + sy * sy);
                        if (sx * tx + sy * ty < 0) len = -len;
                    }
                    else if (labels[j - 1].Kind == BarSegmentLabel.Arc && labels[j - 1].Index != labels[j].Index)
                        len = 0.0;   // łuk bezpośrednio po łuku innego zagięcia — brak prostego odcinka
                    if (len < worst) worst = len;
                }
                if (worst < min - 1e-6)
                    w.Add("Prosty odcinek między zagięciami = " + F(Math.Max(0.0, worst)) + " mm < 4d (" + F(min)
                        + ") — BS 8666:2020 kl. 8.4 (Table 4).");
            }
            catch (Exception ex) { Log.Error("BarShapeValidation.StraightBetweenBends", ex); }
        }

        /// <summary>51 / 63 przy pustym C: hak minimalny C_eff (z formuły minimalnej) nie może być dłuższy od boku.</summary>
        private static void MaxEff(List<string> w, double ceff, double lim, string limName)
        {
            if (lim <= 0 || ceff <= lim + 1e-6) return;
            w.Add("hak minimalny C_eff = " + F(ceff) + " mm > " + limName + " (" + F(lim)
                + ") — przy pustym C hak nie mieści się w boku; podaj C albo zwiększ wymiary (BS 8666:2020 Table 5).");
        }

        /// <summary>Table 4: X = 10d (min. 75) dla d ≤ 16, 13d dla d &gt; 16 (6 → 75, 12 → 120, 20 → 260, 25 → 325).</summary>
        public static double BetweenBendsX(double d) => d <= 16.0 ? Math.Max(10.0 * d, 75.0) : 13.0 * d;

        /// <summary>q z Table 2; d = 50 / spoza tabeli → NaN.</summary>
        private static double HookQ(double d)
        {
            try { return BarShape.HookDiameterQ(d); }
            catch (InvalidOperationException) { return double.NaN; }   // „N/A” — reguła q pomijana
        }

        private static void Min(List<string> w, string n, double v, double lim, string limName, string src)
        {
            if (v <= 0 || double.IsNaN(lim) || v >= lim - 1e-6) return;
            w.Add(n + " < " + limName + " (" + F(lim) + "): " + n + " = " + F(v) + " mm (BS 8666:2020 " + src + ").");
        }

        private static void Max(List<string> w, string n, double v, double lim, string limName)
        {
            if (v <= 0 || lim <= 0 || v <= lim + 1e-6) return;
            w.Add(n + " > " + limName + " (" + F(lim) + "): " + n + " = " + F(v) + " mm (BS 8666:2020 Table 5).");
        }

        /// <summary>Pętla / półkole: q ≤ v ≤ 400 + 2d.</summary>
        private static void LoopRange(List<string> w, string n, double v, double q, double max)
        {
            if (v <= 0) return;
            if (double.IsNaN(q)) w.Add(n + ": q = N/A dla tej średnicy (BS 8666:2020 Table 2) — pętli nie da się wygiąć standardowym trzpieniem.");
            else Min(w, n, v, q, "q", "Table 2");
            if (v > max + 1e-6) w.Add(n + " > 400+2d (" + F(max) + "): " + n + " = " + F(v) + " mm (BS 8666:2020 Table 5).");
        }

        /// <summary>Krótszy bok prostokąta obejmującego obrys pręta (układ lokalny kształtu; pierścień — A).</summary>
        private static double ShortSide(string code, double[] pv, double d)
        {
            try
            {
                if (BarFrameRules.IsRingShape(code)) return pv.Length > 0 ? pv[0] : 0.0;
                var o = BarFrameRules.LocalOutline(code, pv, d);
                if (o == null || o.Count == 0) return 0.0;
                return Math.Min(o.Max(p => p.X) - o.Min(p => p.X), o.Max(p => p.Y) - o.Min(p => p.Y));
            }
            catch (Exception ex)
            {
                Log.Error("BarShapeValidation.ShortSide", ex);
                return 0.0;
            }
        }

        private static string F(double v) => v.ToString("0.##", Inv);
    }
}
