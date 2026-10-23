#nullable disable
using System.Globalization;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Liczba prętów w opisie rozkładu (czysta logika, bez Teigha; testy <c>BarLabelRulesTests</c>).
    /// Zwykle EffectiveCount (CountDisplay ?? Count). Belka (K3, praktyka biura C12 / Holbrook): pręty podłużne dół = góra jedną
    /// pozycją (kod BM) — w rzucie rysowana jedna warstwa (Count = liczba kolumn), opis „2x4 H16-nn-150 B+T” (CountDisplay = 2 · 4,
    /// BBS liczy 8). Krotność (CountDisplay / Count) zostaje po zmianie liczby prętów gripem dla kodów BM i BS.
    /// </summary>
    public static class BarLabelRules
    {
        /// <summary>Kod rozkładu z krotnością (CountDisplay = Count × krotność) zachowywaną po gripie: BM (B+T), BS (pręty boczne), BC,
        /// BL (strzemiona 4-cięte, decyzja użytkownika 2026-10-11 — CountDisplay = 2 × liczba; 2-cięte bez CountDisplay — bez zmian).</summary>
        /// <summary>
        /// Wpis liczby prętów w RC_EDIT_LABEL: „10” → (10, 0); „2x5” / „2X5” / „2×5” / „2*5” → (10, 2). Null — niepoprawny.
        /// </summary>
        public static (int Total, int Mult)? ParseCountInput(string text)
        {
            var t = (text ?? "").Trim().Replace(" ", "");
            if (t.Length == 0) return null;
            int i = t.IndexOfAny(new[] { 'x', 'X', '×', '*' });
            if (i < 0)
                return int.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n >= 1 ? (n, 0) : ((int, int)?)null;
            if (!int.TryParse(t.Substring(0, i), NumberStyles.None, CultureInfo.InvariantCulture, out int k) || k < 1) return null;
            if (!int.TryParse(t.Substring(i + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int m) || m < 1) return null;
            return (k * m, k >= 2 ? k : 0);
        }

        public static bool KeepsMultiplicity(string layerCode)
            => layerCode == BeamGeometry.CodeBoth || layerCode == BeamGeometry.CodeSide || layerCode == BeamGeometry.CodeSideU
            || layerCode == BeamGeometry.CodeLinks;

        /// <summary>
        /// Tekst liczby prętów w opisie: BM i BC (K4, UB SIDE: Count = 1 w rzucie, CountDisplay = liczba poziomów) z CountDisplay
        /// = k · Count (k ≥ 2) → „kxCount” (np. „2x4”, „3x1”); BS (K4, pręty boczne: Count = 2 boki w rzucie, CountDisplay = 2 · n)
        /// → „Countxn” („2x1”, „2x3”, także n = 1); BN z CountDisplay &gt; Count (pręty w nibie 2T+1B, decyzja użytkownika 2026-10-11:
        /// w rzucie 2 pręty górą, CountDisplay 3) → „Count+reszta” („2+1 H10-nn T+B IN NIB”); BL — strzemiona 4-cięte (decyzja użytkownika 2026-10-11:
        /// CountDisplay = 2 · Count) → „2xCount” („2x29 H12-nn-200 LINKS”, jak BM); inaczej CountDisplay ?? Count.
        /// </summary>
        public static string CountText(string layerCode, int count, int? countDisplay, int countMult = 0)
        {
            int eff = countDisplay ?? count;
            string I(int x) => x.ToString(CultureInfo.InvariantCulture);
            // wpis ręczny „kxn” (RC_EDIT_LABEL, 2026-10-23) — dowolny kod rozkładu
            if (countMult >= 2 && eff % countMult == 0 && eff / countMult >= 1)
                return I(countMult) + "x" + I(eff / countMult);
            // BC (UB SIDE) — od 2026-10-14 rozkład Any: zwykła liczba („2 H12-nn-160 UB SIDE”), nie „2x1”
            if ((layerCode == BeamGeometry.CodeBoth || layerCode == BeamGeometry.CodeLinks)
                && countDisplay.HasValue && count > 0
                && countDisplay.Value % count == 0 && countDisplay.Value / count >= 2)
                return I(countDisplay.Value / count) + "x" + I(count);
            if (layerCode == BeamGeometry.CodeSide && countDisplay.HasValue && count > 1 && countDisplay.Value % count == 0)
                return I(count) + "x" + I(countDisplay.Value / count);
            if (layerCode == BeamGeometry.CodeNib && countDisplay.HasValue && count > 0 && countDisplay.Value > count)
                return I(count) + "+" + I(countDisplay.Value - count);
            return I(eff);
        }
    }
}
