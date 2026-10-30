#nullable disable   // kompilowany też w tests/ (Nullable enable) — bez ostrzeżeń CS86xx
using System;
using System.Text.RegularExpressions;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Nazwy płyt: „PLOT …” i „GARAGE …” (garaże jako osobne płyty — POPRAWKI_WZORCA 22).
    /// Czysta logika (bez Teigha) — testy jednostkowe `tests/PlotLabelRulesTests.cs`.
    ///   • opis płyty w GA (SD-Text): „PLOT 9-10”, „PLOTS 324-325”, „GARAGE 321”, „GARAGES 477-478 &amp; 526-527”,
    ///   • nagłówek płyty w raporcie przebicia: „PLOT 538 (10 piles)”, „GARAGE 321 (4 piles)”,
    ///   • klucz płyty w XData RC_PH [3] (etykieta z raportu).
    /// </summary>
    public static class PlotLabelRules
    {
        // 2026-10-30: także „APARTMENT n” / „APARTMENTS …” (GA RG215UH001 BLOCK 2 — Prepare GA nie widział płyty)
        private static readonly Regex GaLabelRx = new Regex(@"^(?:PLOTS?\s+\S|GARAGES?\s+\d|APARTMENTS?\s+\d)", RegexOptions.IgnoreCase);

        /// <summary>Nagłówek PLOT w raporcie — wzorzec bez zmian (np. „PLOTS 3-4” nierozpoznane, jak dawniej).</summary>
        internal static readonly Regex ReportPlotRx =
            new Regex(@"^\s*(PLOT\b.*?)\s*(?:\(\s*(\d+)\s*piles?\s*\))?\s*$", RegexOptions.IgnoreCase);

        /// <summary>Nagłówek garażu w raporcie: „GARAGE 321 (4 piles)” → „GARAGE 321”, 4.</summary>
        private static readonly Regex ReportGarageRx =
            new Regex(@"^\s*(GARAGES?\s+\d.*?)\s*(?:\(\s*(\d+)\s*piles?\s*\))?\s*$", RegexOptions.IgnoreCase);

        private static readonly Regex GarageKeyRx = new Regex(@"^\s*GARAGES?\s+\d", RegexOptions.IgnoreCase);

        /// <summary>Nagłówek / klucz płyty „APARTMENT n…” (2026-10-30).</summary>
        private static readonly Regex ReportApartmentRx =
            new Regex(@"^\s*(APARTMENTS?\s+\d.*?)\s*(?:\(\s*(\d+)\s*piles?\s*\))?\s*$", RegexOptions.IgnoreCase);
        private static readonly Regex ApartmentKeyRx = new Regex(@"^\s*APARTMENTS?\s+\d", RegexOptions.IgnoreCase);

        /// <summary>Kolejność płyt na liście Prepare GA: PLOT 0, APARTMENT 1, GARAGE 2 (domyślnie pierwsza PLOT).</summary>
        public static int SortRank(string label)
        {
            string s = (label ?? "").TrimStart();
            if (s.StartsWith("GARAGE", StringComparison.OrdinalIgnoreCase)) return 2;
            if (s.StartsWith("APARTMENT", StringComparison.OrdinalIgnoreCase)) return 1;
            return 0;
        }

        /// <summary>Opis płyty w rysunku GA (pierwsza linia tekstu SD-Text): „PLOT …”, „PLOTS …”, „GARAGE n…”.</summary>
        public static bool IsPlotLabel(string s) => GaLabelRx.IsMatch((s ?? "").Trim());

        /// <summary>
        /// Nagłówek płyty w raporcie przebicia (komórka A wiersza z jedną komórką). PLOT — dokładnie dawna logika
        /// (etykieta bez „(n piles)”, gdy nawias stoi na końcu; inaczej cały tekst i piles = 0); GARAGE — tak samo.
        /// </summary>
        public static bool TryParseReportHeader(string a, out string label, out int piles)
        {
            label = null; piles = 0;
            if (a == null) return false;
            var pm = ReportPlotRx.Match(a);
            if (!(pm.Success && a.TrimStart().StartsWith("PLOT", StringComparison.OrdinalIgnoreCase)))
            {
                pm = ReportGarageRx.Match(a);
                if (!pm.Success) pm = ReportApartmentRx.Match(a);
                if (!pm.Success) return false;
            }
            label = pm.Groups[1].Value.Trim();
            piles = int.TryParse(pm.Groups[2].Value, out int n) ? n : 0;
            return true;
        }

        /// <summary>
        /// Klucz płyty z raportu (XData RC_PH [3]): „PLOT …” albo „GARAGE n…”. Inny / pusty = stary zapis bez płyty
        /// (np. notatka MANUAL ze starszej wersji) — sprzątany przy każdym przebiciu.
        /// </summary>
        public static bool IsReportPlotKey(string key)
        {
            string k = key ?? "";
            return k.StartsWith("PLOT", StringComparison.OrdinalIgnoreCase) || GarageKeyRx.IsMatch(k) || ApartmentKeyRx.IsMatch(k);
        }

        /// <summary>
        /// Numery płyt z etykiety (uwaga Bartka 2026-10-26, mapa „PLOT 357, 358” dla płyt 357 i 358 z grupy): „PLOT 357, 358” → 357, 358;
        /// „PLOT 357-362” → 357…362 (zakres ≤ 50); „PLOT 359” → 359. Liczby w nawiasie / po „=” (np. „h = 225 mm”) pomijane.
        /// </summary>
        public static System.Collections.Generic.List<int> PlotNumbers(string label)
        {
            var res = new System.Collections.Generic.List<int>();
            string s = label ?? "";
            int cut = s.IndexOfAny(new[] { '=', '(', '|' });
            if (cut >= 0) s = s.Substring(0, cut);
            foreach (Match m in Regex.Matches(s, @"(\d+)(?:\s*-\s*(\d+))?"))
            {
                int a = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                int b = m.Groups[2].Success ? int.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) : a;
                if (b < a || b - a > 50) b = a;
                for (int k = a; k <= b; k++) if (!res.Contains(k)) res.Add(k);
            }
            return res;
        }

        /// <summary>
        /// Mapa należy do płyty (AutoRebar Add, POPRAWKI_WZORCA 28): etykieta mapy (po normalizacji: wielkie litery,
        /// pojedyncze spacje) zaczyna się od etykiety płyty z GA, a następny znak NIE jest cyfrą ani „-”
        /// (wzorzec: sam StartsWith — „PLOT 52” pasowało do „PLOT 526” i „PLOT 52-53”). Dalszy ciąg etykiety mapy
        /// („PLOT 526h = 225 mm …”, „PLOT 526 (…)”) dozwolony jak dawniej.
        /// </summary>
        public static bool MapPlotMatches(string mapPlot, string slabPlot)
        {
            if (slabPlot == null) return false;
            string m = NormLabel(mapPlot), s = NormLabel(slabPlot);
            if (!m.StartsWith(s, StringComparison.Ordinal)) return false;
            if (m.Length == s.Length || s.Length == 0) return true;
            char next = m[s.Length];
            return !char.IsDigit(next) && next != '-';
        }

        /// <summary>
        /// Decyzja Bartka 2026-10-23 (PLOT 304–307): kilka płyt połączonych belkami GB w jednym obrysie GA — jedna pozycja na liście
        /// Prepare GA. Ten sam przedrostek (PLOT / PLOTS / GARAGE…) i same numery → „PLOT 304-307” (najmniejszy – największy; jeden
        /// numer → „PLOT 304”); inaczej etykiety złączone „ + ” w podanej kolejności.
        /// </summary>
        public static string MergeLabels(System.Collections.Generic.IList<string> labels)
        {
            if (labels == null || labels.Count == 0) return "";
            if (labels.Count == 1) return (labels[0] ?? "").Trim();
            string prefix = null;
            var nums = new System.Collections.Generic.List<int>();
            foreach (var l in labels)
            {
                var m = Regex.Match((l ?? "").Trim(), @"^([A-Za-z]+)\s+(\d+(?:\s*-\s*\d+)*)$");
                if (!m.Success) return string.Join(" + ", labels);
                string pre = m.Groups[1].Value.ToUpperInvariant();
                if (pre.EndsWith("S")) pre = pre.Substring(0, pre.Length - 1);
                if (prefix == null) prefix = pre;
                else if (prefix != pre) return string.Join(" + ", labels);
                foreach (Match n in Regex.Matches(m.Groups[2].Value, @"\d+")) nums.Add(int.Parse(n.Value, System.Globalization.CultureInfo.InvariantCulture));
            }
            int lo = int.MaxValue, hi = int.MinValue;
            foreach (int n in nums) { if (n < lo) lo = n; if (n > hi) hi = n; }
            return lo == hi ? $"{prefix} {lo}" : $"{prefix} {lo}-{hi}";
        }

        private static string NormLabel(string s) => Regex.Replace((s ?? "").ToUpperInvariant(), @"\s+", " ").Trim();
    }
}
