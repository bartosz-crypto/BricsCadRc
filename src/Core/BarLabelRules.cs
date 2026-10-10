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
        /// <summary>Kod rozkładu z krotnością (CountDisplay = Count × krotność) zachowywaną po gripie: BM (B+T), BS (pręty boczne).</summary>
        public static bool KeepsMultiplicity(string layerCode)
            => layerCode == BeamGeometry.CodeBoth || layerCode == BeamGeometry.CodeSide;

        /// <summary>
        /// Tekst liczby prętów w opisie: BM z CountDisplay = k · Count (k ≥ 2) → „kxCount” (np. „2x4”); inaczej CountDisplay ?? Count.
        /// </summary>
        public static string CountText(string layerCode, int count, int? countDisplay)
        {
            int eff = countDisplay ?? count;
            if (layerCode == BeamGeometry.CodeBoth && countDisplay.HasValue && count > 0
                && countDisplay.Value % count == 0 && countDisplay.Value / count >= 2)
                return (countDisplay.Value / count).ToString(CultureInfo.InvariantCulture) + "x" + count.ToString(CultureInfo.InvariantCulture);
            return eff.ToString(CultureInfo.InvariantCulture);
        }
    }
}
