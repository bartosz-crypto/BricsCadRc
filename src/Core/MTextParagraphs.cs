#nullable disable   // kompilowany też w tests/ (Nullable enable) — bez ostrzeżeń CS86xx
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Akapity surowej treści MText (<c>MText.Contents</c>) bez kodów formatowania — czysta logika (bez Teigha),
    /// testy jednostkowe `tests/MTextParagraphsTests.cs` (POPRAWKI_WZORCA 24).
    ///
    /// Powód: w BricsCAD <c>MText.Text</c> skleja akapity bez separatora — nagłówek mapy
    /// „\C1;\LPLOT 526\l\P\C7;h = 225 mm | …” dawał etykietę „PLOT 526h = 225 mm | …”. Akapity dzielone są po
    /// kodzie <c>\P</c> (i po zwykłych znakach nowej linii), kody formatowania usuwane (jak GaImportEngine.TextLines).
    /// </summary>
    public static class MTextParagraphs
    {
        // Znaczniki zastępcze dla znaków zapisanych jako sekwencje ucieczki (\\, \{, \}) — nie mogą wyglądać jak kody
        private const char EscBackslash = '\u0001', EscOpen = '\u0002', EscClose = '\u0003';

        // Kody z parametrem do średnika: \A1; \C7; \c…; \Ffont|…; \f…; \H2.5x; \Q15; \T1.2; \W0.8; \pxqc; (akapit — nie nowa linia)
        private static readonly Regex ParamCodes = new Regex(@"\\[ACcFfHhQqTtWwp][^;\\]*;");
        // Ułamki / stosy \S1/2; → treść stosu bez zmian
        private static readonly Regex StackCodes = new Regex(@"\\S([^;\\]*);");
        // Kody bez parametru: podkreślenie / nadkreślenie / przekreślenie
        private static readonly Regex SwitchCodes = new Regex(@"\\[LlOoKk]");

        /// <summary>
        /// Niepuste akapity (przycięte, bez kodów formatowania). Białe znaki wewnątrz akapitu bez zmian
        /// (etykieta jednolinijkowa = dokładnie dawny wynik <c>FirstLine(mt.Text)</c>).
        /// </summary>
        public static List<string> Split(string contents)
        {
            string c = contents ?? "";
            c = c.Replace("\\\\", EscBackslash.ToString()).Replace("\\{", EscOpen.ToString()).Replace("\\}", EscClose.ToString());
            c = ParamCodes.Replace(c, "");
            c = StackCodes.Replace(c, m => m.Groups[1].Value);
            c = c.Replace("\\P", "\n");
            c = SwitchCodes.Replace(c, "");
            c = c.Replace("\\~", " ").Replace("{", "").Replace("}", "");
            c = c.Replace(EscBackslash, '\\').Replace(EscOpen, '{').Replace(EscClose, '}');
            return c.Replace("\r", "\n").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        }

        /// <summary>Pierwszy niepusty akapit („PLOT 526”); "" gdy brak.</summary>
        public static string First(string contents) => Split(contents).FirstOrDefault() ?? "";

        /// <summary>
        /// Etykieta sklejona przez dawny odczyt <c>MText.Text</c> (rysunki z importem map sprzed poprawki 24):
        /// „PLOT 526h = 225 mm | fck = …” → „PLOT 526”. Rozpoznawany tylko drugi akapit nagłówka mapy
        /// („h = … mm”) doklejony do etykiety PLOT / GARAGE; inne teksty bez zmian.
        /// </summary>
        public static string RepairGluedMapLabel(string label)
        {
            string s = label ?? "";
            var m = GluedRx.Match(s);
            return m.Success ? m.Groups[1].Value.Trim() : s;
        }

        private static readonly Regex GluedRx =
            new Regex(@"^\s*((?:PLOTS?|GARAGES?)\s+.*?\S)h\s*=\s*\d+(?:\.\d+)?\s*mm\b", RegexOptions.IgnoreCase);

        /// <summary>
        /// Ta sama płyta: etykiety równe po normalizacji białych znaków (bez rozróżniania wielkości liter) — także gdy
        /// jedna z nich jest dawną etykietą sklejoną (<see cref="RepairGluedMapLabel"/>).
        /// </summary>
        public static bool SameMapLabel(string a, string b)
            => string.Equals(Norm(RepairGluedMapLabel(a)), Norm(RepairGluedMapLabel(b)), StringComparison.OrdinalIgnoreCase);

        private static string Norm(string s) => Regex.Replace((s ?? "").Trim(), @"\s+", " ");
    }
}
