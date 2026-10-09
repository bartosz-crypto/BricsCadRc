using System;
using System.Globalization;
using Teigha.DatabaseServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Powiązania między obiektami (pręt ↔ rozkład ↔ annotacja ↔ etykieta) zapisywane w XData
    /// jako PRAWDZIWE handle (DXF 1005), a nie zwykły tekst (1000).
    ///
    /// BricsCAD NIE przemapowuje 1005 w XData przy COPY / MIRROR / ARRAY (sprawdzone testem) —
    /// robi to BarCopyWatcher według mapy klonowania (Database.BeginDeepCloneTranslation), więc kopia
    /// pręta z etykietą wskazuje na SWOJĄ etykietę, a kopia rozkładu na kopię pręta.
    ///
    /// Odczyt przyjmuje oba formaty (stare rysunki mają 1000) i zawsze zwraca handle
    /// znormalizowany do "X8" (np. "0011093E"), bo tak porównuje reszta kodu.
    /// Stare powiązania zamieniają się na 1005 przy najbliższym zapisie XData obiektu.
    /// </summary>
    public static class XLink
    {
        /// <summary>TypedValue do zapisu powiązania: 1005 gdy handle poprawny, inaczej pusty 1000.</summary>
        public static TypedValue Write(string hex)
        {
            if (TryParse(hex, out long v) && v != 0)
                return new TypedValue((int)DxfCode.ExtendedDataHandle, new Handle(v).ToString());
            return new TypedValue((int)DxfCode.ExtendedDataAsciiString, "");
        }

        /// <summary>Odczyt powiązania z XData (1000 lub 1005) → "X8" albo "".</summary>
        public static string Read(TypedValue tv)
        {
            object val = tv.Value;
            if (val == null) return "";
            if (val is Handle h) return h.Value == 0 ? "" : h.Value.ToString("X8");
            return Norm(val.ToString());
        }

        /// <summary>Normalizacja zapisu handle do "X8" ("" gdy pusty / niepoprawny).</summary>
        public static string Norm(string hex)
            => TryParse(hex, out long v) && v != 0 ? v.ToString("X8") : "";

        /// <summary>Porównanie dwóch handle niezależnie od zapisu (wiodące zera, wielkość liter).</summary>
        public static bool Same(string a, string b)
            => TryParse(a, out long x) && TryParse(b, out long y) && x == y && x != 0;

        public static bool TryParse(string hex, out long value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            return long.TryParse(hex.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }
    }
}
