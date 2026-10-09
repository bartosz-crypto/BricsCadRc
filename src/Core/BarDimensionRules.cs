using System;

// Kompilowany także w tests (net8.0, Nullable enable) — kontekst nullable jak we wtyczce (wyłączony).
#nullable disable

namespace BricsCadRc.Core
{
    /// <summary>
    /// Plan W4e (docs/PLAN_W4_BS8666.md, poprawka #13) — wymiary BS 8666:2020 są ZEWNĘTRZNE (kl. 7.1: mierzone po zewnętrznej
    /// powierzchni pręta, sposób pomiaru wg Table 5 i rys. 4 / 5). Czysta tabela przeliczenia wymiarów zewnętrznych A–E na
    /// wymiary OSI, z których BarGeometryBuilder buduje oś (bez API BricsCAD — testy jednostkowe).
    ///
    /// Reguła ramienia (każde ramię = odcinek między ostrymi punktami osi): wymiar zewnętrzny = długość osi do ostrego punktu
    /// + δ(φ) na każdym zagiętym końcu (wolny koniec: 0), φ = kąt odchylenia kierunku w zagięciu:
    ///   φ ≤ 90° (kąt rozwarty między ramionami, rys. 5 — wymiar do przecięcia lic zewnętrznych): δ = d/2 · tan(φ/2);
    ///   φ &gt; 90° (kąt ostry, rys. 4 — wymiar do stycznej prostopadłej w skrajni łuku): δ = (r + d) − (r + d/2) · tan(φ/2);
    ///   φ = 90°: δ = d/2 dokładnie (obie reguły się pokrywają).
    /// Stąd: ramię wolny koniec → lico −d/2, ramię między dwoma licami (U: B, Z: B, strzemiona) −d, wolny–wolny 0.
    /// Łuk osi = r + d/2 (r — promień wewnętrzny z Table 2; dodawany w BarGeometryBuilder), 12: R + d/2 (R — promień
    /// wewnętrzny, parametr kształtu), 13: pętla (B − d)/2, 22: półkole (C − d)/2, 33: półkola (B − d)/2, 75: średnica osi
    /// A − d (obrys zewnętrzny A/2, środek bez zmian).
    /// Wymiary określające wyłącznie kąt (14 / 15 / 46 — D, 24 — D, E; 14 / 15 — B) zostają: kąt skosu liczy się z wymiarów
    /// zewnętrznych (BarShape.SlopeAngle) — przybliżenie (rzuty lic zamiast osi), różnica kąta pomijalna.
    /// Kształty spoza tabeli (25–29, 31, 32, 34–36, 41, 47, 56, 64, 98 — geometria do W4d-3) — wymiary bez zmian (tylko
    /// łuk osi r + d/2). Wymiar 0 (brak) zostaje 0; wynik ujemny → 0.
    /// </summary>
    public static class BarDimensionRules
    {
        /// <summary>
        /// Wymiary osi z wymiarów zewnętrznych (kopia tablicy, ta sama długość). Pola po nazwie parametru kształtu
        /// (BarShape.IndexOf), jak geometria (W4a-3). null → null; nieznany kształt → kopia bez zmian.
        /// </summary>
        public static double[] ToAxis(string code, double[] pv, double d) => ToAxis(code, pv, d, true);

        /// <summary>Jak ToAxis; slopeFromB — reguła kąta skosu (W4h: także z jednego wymiaru; BarShape.SlopeAngle).</summary>
        public static double[] ToAxis(string code, double[] pv, double d, bool slopeFromB)
        {
            if (pv == null) return null;
            var a = (double[])pv.Clone();
            var shape = ShapeCodeLibrary.Get(code);
            if (shape == null) return a;
            double h = d / 2.0;
            double r = BarShape.MinBendRadius(d);

            void Sub(string name, double amount)
            {
                int i = shape.IndexOf(name);
                if (i < 0 || i >= a.Length || a[i] <= 0) return;   // brak wymiaru zostaje 0
                a[i] = Math.Max(0.0, a[i] - amount);
            }

            switch (code)
            {
                case "11":
                    Sub("A", h); Sub("B", h);
                    break;
                case "12":   // R — promień wewnętrzny → promień osi R + d/2 (R ≤ 0 = minimalny promień, bez zmian)
                {
                    Sub("A", h); Sub("B", h);
                    int iR = shape.IndexOf("R");
                    if (iR >= 0 && iR < a.Length && a[iR] > 0) a[iR] = a[iR] + h;
                    break;
                }
                case "13":   // A, C — wolny koniec → skrajnia pętli; B — wysokość pętli po zewnętrznej → średnica osi B − d
                    Sub("A", h); Sub("B", d); Sub("C", h);
                    break;
                case "14":   // A skośne, C poziome wstecz — kąt ostry (rys. 4): φ = 180° − θ
                {
                    double dl = LegReduction(Math.PI - shape.SlopeAngle(pv, out _, slopeFromB), r, d);
                    Sub("A", dl); Sub("C", dl);
                    break;
                }
                case "15":   // A skośne, C poziome dalej — kąt rozwarty (rys. 5): φ = θ
                {
                    double dl = LegReduction(shape.SlopeAngle(pv, out _, slopeFromB), r, d);
                    Sub("A", dl); Sub("C", dl);
                    break;
                }
                case "21":
                case "23":
                    Sub("A", h); Sub("B", d); Sub("C", h);
                    break;
                case "22":   // A — wolny koniec → lico górne; B — od lica zewn. A do skrajni półkola (lico–lico, runda 1 uwaga 2);
                             // C — średnica półkola po zewnętrznej; D — wolny koniec → skrajnia półkola
                    Sub("A", h); Sub("B", d); Sub("C", d); Sub("D", h);
                    break;
                case "24":   // A poziome, B skos (θ), C pion: zagięcia φ1 = θ, φ2 = 90° − θ (oba rozwarte)
                {
                    double t = shape.SlopeAngle(pv, out _, slopeFromB);
                    double d1 = LegReduction(t, r, d), d2 = LegReduction(Math.PI / 2.0 - t, r, d);
                    Sub("A", d1); Sub("B", d1 + d2); Sub("C", d2);
                    break;
                }
                case "33":   // A — długość skrajna (obie skrajnie półkoli), B — średnica półkoli po zewnętrznej, C — od prawej skrajni
                    Sub("A", d); Sub("B", d); Sub("C", h);
                    break;
                case "44":   // kapelusz: A, E wolne końce; B, C, D między dwoma licami
                    Sub("A", h); Sub("B", d); Sub("C", d); Sub("D", d); Sub("E", h);
                    break;
                case "46":   // cztery zagięcia φ = θ: A, E — jeden zagięty koniec; B, C — dwa
                {
                    double dl = LegReduction(shape.SlopeAngle(pv, out _, slopeFromB), r, d);
                    Sub("A", dl); Sub("B", 2.0 * dl); Sub("C", 2.0 * dl); Sub("E", dl);
                    break;
                }
                case "51":   // strzemię: A (wysokość), B (szerokość) po zewnętrznej; hak C do lica (C = 0 → C_eff w geometrii)
                case "63":
                    Sub("A", d); Sub("B", d); Sub("C", h);
                    break;
                case "75":   // pierścień: A — średnica zewnętrzna → średnica osi A − d; B — zakład
                    Sub("A", d);
                    break;
            }
            return a;
        }

        /// <summary>
        /// δ(φ) — o ile wymiar zewnętrzny ramienia przekracza długość osi do ostrego punktu zagięcia o kącie odchylenia φ
        /// (r — promień wewnętrzny, d — średnica). φ = 90° → d/2 dokładnie; φ ≤ 90° → d/2·tan(φ/2) (rys. 5);
        /// φ &gt; 90° → (r + d) − (r + d/2)·tan(φ/2) (rys. 4). φ ≤ 0 → 0.
        /// </summary>
        public static double LegReduction(double phi, double r, double d)
        {
            if (phi <= 0) return 0.0;
            if (Math.Abs(phi - Math.PI / 2.0) < 1e-12) return d / 2.0;
            if (phi < Math.PI / 2.0) return d / 2.0 * Math.Tan(phi / 2.0);
            return (r + d) - (r + d / 2.0) * Math.Tan(phi / 2.0);
        }

        /// <summary>Pierścień 75: promień osi (A − d)/2 (≥ 0); obrys zewnętrzny A/2, wewnętrzny A/2 − d.</summary>
        public static double RingAxisRadius(double a, double d) => Math.Max(0.0, (a - d) / 2.0);

        /// <summary>Promień osi łuku zagięcia: promień wewnętrzny r + d/2.</summary>
        public static double AxisBendRadius(double r, double d) => r + d / 2.0;
    }
}
