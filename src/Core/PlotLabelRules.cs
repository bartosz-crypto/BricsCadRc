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
        private static readonly Regex GaLabelRx = new Regex(@"^(?:PLOTS?\s+\S|GARAGES?\s+\d)", RegexOptions.IgnoreCase);

        /// <summary>Nagłówek PLOT w raporcie — wzorzec bez zmian (np. „PLOTS 3-4” nierozpoznane, jak dawniej).</summary>
        internal static readonly Regex ReportPlotRx =
            new Regex(@"^\s*(PLOT\b.*?)\s*(?:\(\s*(\d+)\s*piles?\s*\))?\s*$", RegexOptions.IgnoreCase);

        /// <summary>Nagłówek garażu w raporcie: „GARAGE 321 (4 piles)” → „GARAGE 321”, 4.</summary>
        private static readonly Regex ReportGarageRx =
            new Regex(@"^\s*(GARAGES?\s+\d.*?)\s*(?:\(\s*(\d+)\s*piles?\s*\))?\s*$", RegexOptions.IgnoreCase);

        private static readonly Regex GarageKeyRx = new Regex(@"^\s*GARAGES?\s+\d", RegexOptions.IgnoreCase);

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
            return k.StartsWith("PLOT", StringComparison.OrdinalIgnoreCase) || GarageKeyRx.IsMatch(k);
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

        private static string NormLabel(string s) => Regex.Replace((s ?? "").ToUpperInvariant(), @"\s+", " ").Trim();
    }
}
