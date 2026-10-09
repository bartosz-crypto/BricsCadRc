using System;
using System.Globalization;
using System.Linq;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Model kształtu pręta wg BS8666.
    /// </summary>
    public class BarShape
    {
        private readonly Func<double[], double, double> _lengthFormula;

        public string   Code       { get; }
        public string   Name       { get; }
        public string[] Parameters { get; }
        public string   SvgPreview { get; }

        public BarShape(string code, string name, string[] parameters, string svgPreview,
            Func<double[], double, double> lengthFormula)
        {
            Code           = code;
            Name           = name;
            Parameters     = parameters;
            SvgPreview     = svgPreview;
            _lengthFormula = lengthFormula;
            DisplayOrder   = Enumerable.Range(0, parameters?.Length ?? 0)
                                       .OrderBy(i => parameters[i], StringComparer.Ordinal)
                                       .ToArray();
        }

        /// <summary>
        /// W4d: kolejność wierszy w dialogach — indeksy Parameters posortowane alfabetycznie (np. 46 {A,B,C,E,D} → 0,1,2,4,3).
        /// Wiersz r pokazuje parametr Parameters[DisplayOrder[r]] i zapisuje pole XData LengthA…E o tym indeksie (mapa wiersz → slot).
        /// </summary>
        public int[] DisplayOrder { get; }

        /// <summary>
        /// Indeks parametru o danej nazwie w Parameters (= pole XData LengthA…E o tym indeksie); −1 gdy brak.
        /// Plan W4 (W4a-3): geometria czyta wymiary po nazwie, nie po pozycji.
        /// </summary>
        public int IndexOf(string name)
        {
            if (name == null || Parameters == null) return -1;
            for (int i = 0; i < Parameters.Length; i++)
                if (string.Equals(Parameters[i], name, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>Parametr kształtu obecny w Parameters i w tablicy wartości.</summary>
        public bool Has(double[] paramValues, string name)
        {
            int i = IndexOf(name);
            return i >= 0 && paramValues != null && paramValues.Length > i;
        }

        /// <summary>Wartość parametru o danej nazwie (paramValues[i] = wartość Parameters[i]); brak parametru → 0.</summary>
        public double Value(double[] paramValues, string name)
            => Has(paramValues, name) ? paramValues[IndexOf(name)] : 0.0;

        /// <summary>
        /// Oblicza całkowitą długość pręta wg formuły BS8666.
        /// </summary>
        /// <param name="paramValues">Wartości wymiarów A, B, C, … (mm)</param>
        /// <param name="diameter">Średnica pręta (mm), musi być > 0</param>
        public double CalculateTotalLength(double[] paramValues, double diameter)
        {
            if (diameter <= 0)
                throw new ArgumentException("Średnica musi być większa od 0.", nameof(diameter));
            if (paramValues == null || paramValues.Length != Parameters.Length)
                throw new ArgumentException(
                    $"Oczekiwano {Parameters.Length} parametrów, podano {paramValues?.Length ?? 0}.",
                    nameof(paramValues));

            double raw = _lengthFormula(paramValues, diameter);
            // W5: długość nieokreślona (NaN / nieskończona) → wyjątek = „brak długości” u wywołujących (nie liczba w BBS)
            if (double.IsNaN(raw) || double.IsInfinity(raw))
                throw new InvalidBarLengthException(Code, raw);
            // BS 8666:2020 kl. 7.2 — długość całkowita w górę do 25 mm. W4g (P7): także prosty 00 (A bez zmian).
            // W5: tolerancja 1e-9 kroku dla WSZYSTKICH kodów — szum double (np. 12: 625.0000000000001 przy 0.43R,
            // 00: długość z obrysu 3500.0000000004) nie podnosi długości o 25 mm.
            double len = Math.Ceiling(raw / 25.0 - 1e-9) * 25.0;
            // W5: długość zerowa / ujemna (wymiary zerowe lub za małe) → wyjątek = „brak długości”
            if (len <= 0) throw new InvalidBarLengthException(Code, raw);
            return len;
        }

        /// <summary>
        /// W5: komunikat dla długości ≤ 0 / nieokreślonej (bez prefiksu i znaku nowej linii) — wyjątek
        /// InvalidBarLengthException i ostrzeżenie BarShapeValidation.
        /// </summary>
        public static string InvalidLengthMessage(string code, double raw)
            => double.IsNaN(raw) || double.IsInfinity(raw)
                ? "długość pręta wg wzoru kształtu " + code + " nieokreślona — sprawdź wymiary albo nadpisz długość."
                : "długość pręta wg wzoru kształtu " + code + " = " + raw.ToString("F0", CultureInfo.InvariantCulture)
                  + " mm (≤ 0) — sprawdź wymiary (zerowe lub za małe) albo nadpisz długość.";

        /// <summary>
        /// Minimalny promień gięcia wg BS 8666:2020 Table 2 (B500): 6→12, 8→16, 10→20, 12→24, 16→32, 20→70,
        /// 25→87 (W4b: dawniej 87.5), 32→112, 40→140, 50→175. Średnica spoza tabeli: 2d dla d ≤ 16, 3.5d dla d ≥ 20.
        /// </summary>
        public static double MinBendRadius(double d)
        {
            int i = Table2Index(d);
            if (i >= 0) return Table2R[i];
            return d <= 16.0 ? 2.0 * d : 3.5 * d;
        }

        /// <summary>
        /// BS 8666:2020 Table 2: q — minimalna średnica (wysokość) haka/pętli. d = 50 → „N/A” (wyjątek),
        /// średnica spoza tabeli → wyjątek (brak wartości w normie).
        /// </summary>
        public static double HookDiameterQ(double d)
        {
            int i = Table2Index(d);
            if (i < 0 || double.IsNaN(Table2Q[i]))
                throw new InvalidOperationException(
                    "BS 8666:2020 Table 2: q dla H" + d.ToString(CultureInfo.InvariantCulture) + " = N/A — nadpisz długość.");
            return Table2Q[i];
        }

        /// <summary>
        /// BS 8666:2020 Table 2: P — minimalny wysięg prostego odcinka końcowego (links = strzemiona / pręty
        /// dystansowe, inaczej pręty gięte). Średnica spoza tabeli → NaN.
        /// </summary>
        public static double MinEndProjectionP(double d, bool links)
        {
            int i = Table2Index(d);
            if (i < 0) return double.NaN;
            return links ? Table2PLinks[i] : Table2PBend[i];
        }

        // BS 8666:2020 Table 2 (s. 13)
        private static readonly double[] Table2D      = { 6, 8, 10, 12, 16, 20, 25, 32, 40, 50 };
        private static readonly double[] Table2R      = { 12, 16, 20, 24, 32, 70, 87, 112, 140, 175 };
        private static readonly double[] Table2PBend  = { 110, 115, 120, 125, 140, 190, 235, 305, 380, 475 };
        private static readonly double[] Table2PLinks = { 110, 115, 130, 155, 210, 290, 365, 465, 580, 725 };
        private static readonly double[] Table2Q      = { 45, 60, 70, 85, 115, 200, 250, 320, 400, double.NaN };

        private static int Table2Index(double d)
        {
            for (int i = 0; i < Table2D.Length; i++)
                if (Math.Abs(Table2D[i] - d) < 1e-9) return i;
            return -1;
        }

        // ── 51 / 63: wariant minimalny (C = 0) wg BS 8666:2020 ─────────────────

        /// <summary>51, C = 0: dodatek formuły minimalnej 2A + 2B + max(16d, 160).</summary>
        public static double Code51MinExtra(double d) => Math.Max(16.0 * d, 160.0);

        /// <summary>
        /// 51, C = 0 — efektywna długość haka do geometrii (0-POPRAWKI p.3), z równości formuł:
        /// 2(A+B+C_eff) − 2.5r − 5d = 2A + 2B + max(16d,160) → C_eff = (max(16d,160) + 2.5r + 5d) / 2.
        /// </summary>
        public static double Code51EffectiveHook(double d)
            => (Code51MinExtra(d) + 2.5 * MinBendRadius(d) + 5.0 * d) / 2.0;

        /// <summary>63, C = 0: dodatek formuły minimalnej 2A + 3B + (d ≤ 16: max(14d, 140); d ≥ 20: 13d).</summary>
        public static double Code63MinExtra(double d) => d <= 16.0 ? Math.Max(14.0 * d, 140.0) : 13.0 * d;

        /// <summary>
        /// 63, C = 0 — efektywna długość haka do geometrii (0-POPRAWKI p.3): 2A + 3B + 2C_eff − 3r − 6d =
        /// 2A + 3B + Code63MinExtra → C_eff = (Code63MinExtra + 3r + 6d) / 2.
        /// </summary>
        public static double Code63EffectiveHook(double d)
            => (Code63MinExtra(d) + 3.0 * MinBendRadius(d) + 6.0 * d) / 2.0;

        /// <summary>
        /// Kształt, którego długość wg BS 8666:2020 (W4b) może różnić się od liczonej wcześniej: formuły 14, 29, 31, 47,
        /// 51, 56, 63, 64, 75, prosty 00 (W4g — zaokrąglenie do 25 mm) oraz każdy kształt z promieniem zależnym od Table 2
        /// przy d = 25 (r 87.5 → 87).
        /// Używane przez bezpiecznik BBS (BbsDrawingReader) — decyzja D17.
        /// </summary>
        public static bool FormulaChangedIn2020(string code, double d)
        {
            switch (code)
            {
                case "14": case "29": case "31": case "47": case "51":
                case "56": case "63": case "64": case "75":
                case "00":   // W4g (runda 1, uwaga 2): 00 w górę do 25 mm — stare TL = A ≠ wielokrotność 25
                case "27": case "28":   // W5: −0.5r − d (dawniej −0.5r − 2d) — stare TL krótsze o d (po zaokrągleniu 0–50 mm)
                    return true;
                default:
                    return Math.Abs(d - 25.0) < 1e-9 && code != "00" && code != "99";
            }
        }

        /// <summary>
        /// Bezpiecznik BBS (W4b, D17; W4g): długość wg formuły BS 8666:2020 różni się od zapisanej w rysunku — tylko kształty
        /// ze zmienioną formułą (FormulaChangedIn2020), bez długości nadpisanej, zapisana TL &gt; 0 i |różnica| &gt; 0,5 mm.
        /// </summary>
        public static bool StoredLengthDiffers(string code, double d, bool lengthOverridden, double storedTotal, double formulaTotal)
            => !lengthOverridden && FormulaChangedIn2020(code, d) && storedTotal > 0 && Math.Abs(storedTotal - formulaTotal) > 0.5;

        /// <summary>
        /// Wymiar potrzebny wyłącznie formule długości (bez niego „brak długości”): 47 — C (W4b, dopisany; stare pręty
        /// mają 0). Dialog dopuszcza brak, gdy długość całkowita jest nadpisana (runda 1, uwaga 3).
        /// </summary>
        public bool IsRequiredOnlyByFormula(int index)
            => Code == "47" && Parameters != null && index >= 0 && index < Parameters.Length && Parameters[index] == "C";

        /// <summary>
        /// Wymiar opcjonalny w dialogach (0-POPRAWKI p.5): puste / 0 = wartość domyślna. W4b: C dla 51 i 63
        /// (wariant minimalny haka); W4d: wymiary określające kąt (IsAngleParam) — puste / 0 = kąt domyślny + ostrzeżenie.
        /// </summary>
        public bool IsOptionalParam(int index)
            => Parameters != null && index >= 0 && index < Parameters.Length
               && (((Code == "51" || Code == "63") && Parameters[index] == "C") || IsAngleParam(index));

        /// <summary>
        /// W4d: wymiar dopisany w W4d, który określa wyłącznie kąt skosu (nie wchodzi do formuły długości): 14, 15, 46 — D;
        /// 24 — D i E. Stare pręty mają tam 0 (kąt domyślny).
        /// </summary>
        public bool IsAngleParam(int index)
        {
            if (Parameters == null || index < 0 || index >= Parameters.Length) return false;
            string n = Parameters[index];
            switch (Code)
            {
                case "14": case "15": case "46": return n == "D";
                case "24": return n == "D" || n == "E";
                default: return false;
            }
        }

        /// <summary>
        /// W4d — kąt skosu [rad] kształtów z zagięciem innym niż 90° (BS 8666:2020 kl. 8.1.6: kąt ze współrzędnych).
        /// 14, 15: θ = atan2(B, D) — B rzut pionowy, D rzut poziomy ramienia A (θ od poziomu);
        /// 24: θ = atan2(D, E) — D rzut pionowy, E rzut poziomy ramienia B; 46: θ = asin(D / B) — D wysokość skosu B.
        /// Brak wymiaru (0) albo wymiary sprzeczne (46: D &gt; B) → kąt domyślny (14, 15, 46: 45°; 24: 30°), isDefault = true.
        /// Inne kształty → 0, isDefault = false.
        /// </summary>
        public double SlopeAngle(double[] paramValues, out bool isDefault)
            => SlopeAngle(paramValues, out isDefault, true);

        /// <summary>
        /// Jak SlopeAngle; fromSingle (W4h, decyzja użytkownika 2026-10-09): kąt także z JEDNEGO wymiaru — 14, 15: B (rzut
        /// pionowy A) → θ = asin(B/A), albo D (rzut poziomy) → θ = acos(D/A); 24: D → asin(D/B), E → acos(E/B); rzut ≥ długości
        /// ramienia → 90°. fromSingle = false — reguła W4d–W4e (oba wymiary albo kąt domyślny) — tylko do rozpoznania obrysów
        /// sprzed W4h (BarGeometryOptions.SlopeFromB = false).
        /// </summary>
        public double SlopeAngle(double[] paramValues, out bool isDefault, bool fromSingle)
        {
            isDefault = false;
            switch (Code)
            {
                case "14": case "15":
                {
                    double b = Value(paramValues, "B"), d = Value(paramValues, "D");
                    if (b > 0 && d > 0) return Math.Atan2(b, d);
                    double a = Value(paramValues, "A");
                    if (fromSingle && a > 0 && b > 0) return Math.Asin(Math.Min(1.0, b / a));
                    if (fromSingle && a > 0 && d > 0) return Math.Acos(Math.Min(1.0, d / a));
                    isDefault = true;
                    return Math.PI / 4.0;
                }
                case "24":
                {
                    double dv = Value(paramValues, "D"), e = Value(paramValues, "E");
                    if (dv > 0 && e > 0) return Math.Atan2(dv, e);
                    double bl = Value(paramValues, "B");
                    if (fromSingle && bl > 0 && dv > 0) return Math.Asin(Math.Min(1.0, dv / bl));
                    if (fromSingle && bl > 0 && e > 0) return Math.Acos(Math.Min(1.0, e / bl));
                    isDefault = true;
                    return Math.PI / 6.0;
                }
                case "46":
                {
                    double b = Value(paramValues, "B"), d = Value(paramValues, "D");
                    if (b > 0 && d > 0 && d <= b) return Math.Asin(d / b);
                    isDefault = true;
                    return Math.PI / 4.0;
                }
                default:
                    return 0.0;
            }
        }

        /// <summary>
        /// W4d (0-POPRAWKI p.5): komunikat, gdy kąt skosu przyjęto domyślnie (brak wymiarów kąta, np. stary pręt 15 bez D);
        /// null, gdy kąt wynika z wymiarów albo kształt nie ma skosu. Bez prefiksu i bez znaków nowej linii.
        /// </summary>
        public string DefaultAngleWarning(double[] paramValues)
        {
            if (Code != "14" && Code != "15" && Code != "24" && Code != "46") return null;
            SlopeAngle(paramValues, out bool isDefault);
            if (!isDefault) return SlopeClampWarning(paramValues);   // W4h: kąt skrajny 90° / 0° z jednego rzutu
            string need = Code == "24" ? "D i E" : Code == "46" ? "D (wysokość skosu, ≤ B)" : "B i D";
            int deg = Code == "24" ? 30 : 45;
            return string.Format(CultureInfo.InvariantCulture,
                "Kształt {0}: brak wymiarów kąta {1} — przyjęto kąt domyślny {2}° (BS 8666:2020: kąt ze współrzędnych).",
                Code, need, deg);
        }

        /// <summary>
        /// W4h runda 2 (decyzja: wariant b) — grip / STRETCH wolnego końca ramienia A kształtu 14 / 15 z kątem z samego B
        /// (B &gt; 0, D = 0): kopia wymiarów z D = A·cos θ = √(A² − B²) (θ z bieżących A, B), zaokrąglonym do 0,01 mm — wtedy kąt
        /// liczy się z B i D (atan2) i NIE zależy od A, więc wydłużenie A zostawia kąt (zmiana kąta przez zaokrąglenie
        /// &lt; 0,002°). null — kształt / dane bez tej sytuacji (B ≥ A, brak A, D podane, inny kształt).
        /// </summary>
        public double[] WithFixedSlope(double[] paramValues)
        {
            if ((Code != "14" && Code != "15") || paramValues == null) return null;
            double a = Value(paramValues, "A"), b = Value(paramValues, "B"), d = Value(paramValues, "D");
            int iD = IndexOf("D");
            if (a <= 0 || b <= 0 || d > 0 || b >= a || iD < 0 || iD >= paramValues.Length) return null;
            var r = (double[])paramValues.Clone();
            r[iD] = Math.Round(Math.Sqrt(a * a - b * b), 2, MidpointRounding.AwayFromZero);
            return r;
        }

        /// <summary>
        /// W4h runda 2: kąt skrajny z jednego rzutu — rzut pionowy &gt; ramię skośne + 0,5 mm → 90°, rzut poziomy (14 / 15: D,
        /// 24: E) ≥ ramię → 0°; komunikat albo null (także dla kąta wpisanego 90°: B = A).
        /// </summary>
        public string SlopeClampWarning(double[] paramValues)
        {
            string arm, vert, hor;
            if (Code == "14" || Code == "15") { arm = "A"; vert = "B"; hor = "D"; }
            else if (Code == "24") { arm = "B"; vert = "D"; hor = "E"; }
            else return null;
            double l = Value(paramValues, arm), v = Value(paramValues, vert), h = Value(paramValues, hor);
            if (l <= 0 || (v > 0 && h > 0)) return null;
            if (v > 0 && v > l + 0.5)
                return string.Format(CultureInfo.InvariantCulture,
                    "Kształt {0}: {1} > {2} (rzut pionowy dłuższy od ramienia skośnego) — przyjęto kąt 90°; sprawdź wymiary.", Code, vert, arm);
            if (v <= 0 && h > 0 && h >= l - 1e-6)
                return string.Format(CultureInfo.InvariantCulture,
                    "Kształt {0}: {1} ≥ {2} (rzut poziomy nie krótszy od ramienia skośnego) — przyjęto kąt 0°; sprawdź wymiary.", Code, hor, arm);
            return null;
        }

        /// <summary>W4h: kształt z ramieniem skośnym, dla którego dialog pokazuje pole „Kąt [°]” (14, 15, 24, 46).</summary>
        public bool HasSlopeAngleInput => Code == "14" || Code == "15" || Code == "24" || Code == "46";

        /// <summary>W4h: wymiar wyznaczany z wpisanego kąta (AngleToDimensions): 14, 15 — B, D; 24 — D, E; 46 — D.</summary>
        public bool IsSlopeDimension(int index)
        {
            if (Parameters == null || index < 0 || index >= Parameters.Length) return false;
            string n = Parameters[index];
            switch (Code)
            {
                case "14": case "15": return n == "B" || n == "D";
                case "24": return n == "D" || n == "E";
                case "46": return n == "D";
                default: return false;
            }
        }

        /// <summary>
        /// W4h: kąt ramienia skośnego wpisany przez użytkownika (stopnie od poziomu, 0 &lt; θ ≤ 90) → wymiary kąta (BS 8666:2020
        /// kl. 8.1.6 — kąt ze współrzędnych), zaokrąglone do 5 mm (kl. 7.2), zapis w dotychczasowych polach (bez zmiany XData):
        /// 14, 15: B = A·sin θ, D = A·cos θ; 24: D = B·sin θ, E = B·cos θ; 46: D = B·sin θ (wysokość skosu).
        /// Zwraca kopię wartości; null — kształt bez pola kąta, kąt spoza zakresu albo brak długości ramienia skośnego (A / B).
        /// </summary>
        public double[] AngleToDimensions(double[] paramValues, double degrees)
        {
            if (!HasSlopeAngleInput || paramValues == null || double.IsNaN(degrees) || degrees <= 0 || degrees > 90) return null;
            string arm = Code == "14" || Code == "15" ? "A" : "B";
            double len = Value(paramValues, arm);
            if (len <= 0) return null;
            double t = degrees * Math.PI / 180.0;
            var r = (double[])paramValues.Clone();
            void Set(string n, double v)
            {
                int i = IndexOf(n);
                if (i >= 0 && i < r.Length) r[i] = Math.Round(v / 5.0, MidpointRounding.AwayFromZero) * 5.0;
            }
            if (arm == "A") { Set("B", len * Math.Sin(t)); Set("D", len * Math.Cos(t)); }
            else if (Code == "24") { Set("D", len * Math.Sin(t)); Set("E", len * Math.Cos(t)); }
            else Set("D", len * Math.Sin(t));
            return r;
        }
    }

    /// <summary>
    /// W5: długość cięcia wg wzoru kształtu ≤ 0 albo nieokreślona (NaN / nieskończona). Wywołujący traktują ją jak
    /// każdy błąd długości: pozycja BBS / zestawienia „brak długości”, w dialogach puste pole, TotalLength bez zmian.
    /// </summary>
    public sealed class InvalidBarLengthException : InvalidOperationException
    {
        public InvalidBarLengthException(string code, double raw)
            : base(BarShape.InvalidLengthMessage(code, raw))
        {
            RawLength = raw;
        }

        /// <summary>Wynik wzoru przed zaokrągleniem [mm].</summary>
        public double RawLength { get; }
    }
}
