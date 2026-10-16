using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Czyste reguły tekstów detali PH1–PH9 (bez API BricsCAD — testowalne):
    ///   • tytuł detalu: „… PILE CONDITION PHn” w pierwszej linii, „EXTRA BARS (…)” od drugiej; wszystkie akapity
    ///     wyśrodkowane (decyzja użytkownika 2026-10-16),
    ///   • opisy prętów w detalach „7 H12-113-200 T” → numer faktycznie użytego pręta zbiorczego („7 H12-125-200 T”).
    /// </summary>
    public static class PhDetailRules
    {
        /// <summary>Warstwa opisów rozkładów w detalach PH (z szablonu ASD).</summary>
        public const string LabelLayer = "AutoCAD_Structural_Detailing_Distribution description";

        /// <summary>Pręty zbiorcze przebicia: H12 L2250 (PH1–3), H16 L2500 (PH4–9).</summary>
        public static readonly (int Dia, double Length)[] SummaryBars = { (12, 2250.0), (16, 2500.0) };

        /// <summary>Długość pręta zbiorczego dla średnicy; 0 — to nie średnica pręta zbiorczego.</summary>
        public static double SummaryLength(int dia)
        {
            foreach (var (d, l) in SummaryBars) if (d == dia) return l;
            return 0;
        }

        private static readonly Regex TitleBreakRx =
            new Regex(@"(PILE\s+CONDITION\s+PH\s*[1-9])(?!\d)[ \t]+(?=EXTRA\s+BARS)", RegexOptions.IgnoreCase);
        private static readonly Regex ParaRx  = new Regex(@"\\p([^;\\]*);");          // \p…; (małe p — formatowanie akapitu)
        private static readonly Regex AlignRx = new Regex(@"(^x|^|,)q[^,]*");

        /// <summary>
        /// Tytuł detalu PH: łamanie linii po „PHn” przed „EXTRA BARS”, wyrównanie wszystkich akapitów do środka
        /// (q* / ql / qr / qj → qc), bez tabulatorów ^I na początku akapitu. Wołać tylko dla MText tytułów detali PH.
        /// </summary>
        public static string NormalizeTitle(string contents)
        {
            if (string.IsNullOrEmpty(contents)) return contents;
            string s = TitleBreakRx.Replace(contents, "$1\\P");
            s = ParaRx.Replace(s, m =>
            {
                string body = m.Groups[1].Value;
                string nb = AlignRx.IsMatch(body) ? AlignRx.Replace(body, "$1qc") : body;
                return "\\p" + nb + ";";
            });
            s = s.Replace("^I", "");
            if (!s.Contains("qc;") && !s.Contains("qc,")) s = "\\pxqc;" + s;   // brak wyrównania — całość do środka
            return s;
        }

        // „7 H12-113-200 T”, „14 H16-116-100 T” (liczba, średnica, numer, rozstaw, warstwa)
        private static readonly Regex LabelRx = new Regex(@"(?<!\d)(\d+\s*H)(\d+)(\s*-\s*)(\d+)(\s*-\s*\d+)");

        /// <summary>
        /// Opis rozkładu w detalu PH: numer pozycji zamieniony na numer pręta zbiorczego tej średnicy
        /// (<paramref name="numberByDia"/>: Ø → numer). Średnica bez numeru — bez zmian.
        /// </summary>
        public static string ReplaceBarNumber(string contents, IDictionary<int, int> numberByDia)
        {
            if (string.IsNullOrEmpty(contents) || numberByDia == null || numberByDia.Count == 0) return contents;
            return LabelRx.Replace(contents, m =>
            {
                if (!int.TryParse(m.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dia)
                    || !numberByDia.TryGetValue(dia, out int nr) || nr <= 0)
                    return m.Value;
                return m.Groups[1].Value + m.Groups[2].Value + m.Groups[3].Value
                     + nr.ToString("D2", CultureInfo.InvariantCulture) + m.Groups[5].Value;
            });
        }

        /// <summary>Czy tekst wygląda jak opis rozkładu detalu PH („7 H12-113-200 T”).</summary>
        public static bool IsBarLabel(string text) => !string.IsNullOrEmpty(text) && LabelRx.IsMatch(text);

        /// <summary>
        /// Numer pręta zbiorczego: istniejący pręt o tym samym Ø i długości w serii górnej (101–499) — ten sam numer
        /// (najmniejszy, gdy kilka); inaczej <paramref name="nextTop"/>() — następny wolny numer serii górnej.
        /// <paramref name="candidates"/>: (Ø, kształt, długość A, numer) prętów z rysunku.
        /// </summary>
        public static int PickNumber(int dia, double length, IEnumerable<(int Dia, string Shape, double LengthA, int Nr)> candidates,
                                     int topLo, int topHi, Func<int> nextTop)
        {
            int best = 0;
            foreach (var c in candidates)
            {
                if (c.Dia != dia || c.Nr < topLo || c.Nr >= topHi) continue;
                if (!string.Equals(string.IsNullOrEmpty(c.Shape) ? "00" : c.Shape, "00", StringComparison.OrdinalIgnoreCase)) continue;
                if (Math.Abs(c.LengthA - length) > 1.0) continue;
                if (best == 0 || c.Nr < best) best = c.Nr;
            }
            return best > 0 ? best : nextTop();
        }
    }
}
