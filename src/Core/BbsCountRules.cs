#nullable disable   // kompilowany też w tests/ (Nullable enable) — bez ostrzeżeń CS86xx
using System;
using System.Globalization;

namespace BricsCadRc.Core
{
    /// <summary>
    /// RC_COUNT_BBS (BbsCounter) — czysta logika wierszy tonażu (bez Teigha), testy `tests/BbsCountRulesTests.cs`
    /// (POPRAWKI_WZORCA 26). Pręty AutoRebar / ręczne (RC_SINGLE_BAR: polilinie i pręty-bloki, ilości z rozkładów
    /// RC_BAR_BLOCK z żywym opisem) przychodzą z tego samego źródła co RC_BBS (<c>BbsDrawingReader.ReadBars</c>) —
    /// długość cięcia (z nadpisaną długością) i masa jak w BBS. Stare pręty RC_BAR — jak dotąd (każda linia = 1 pręt).
    /// </summary>
    internal static class BbsCountRules
    {
        /// <summary>Masa [kg] = sztuki × długość cięcia [m] × masa liniowa; brak długości (NaN) → 0 (jak BbsBarRow.MassKg).</summary>
        public static double MassKg(int count, double cuttingLengthMm, double linearMassKgPerM)
            => double.IsNaN(cuttingLengthMm) ? 0.0 : count * (cuttingLengthMm / 1000.0) * linearMassKgPerM;

        /// <summary>Oznaczenie pozycji jak w programie: „H12-03”, „H16-502”.</summary>
        public static string MarkOf(int diameter, int pos)
            => "H" + diameter.ToString(CultureInfo.InvariantCulture) + "-" + pos.ToString("00", CultureInfo.InvariantCulture);

        /// <summary>
        /// Wiersz RC_COUNT_BBS z wiersza BBS (BbsBarRow: pozycja, średnica, kształt, wymiar A, długość cięcia, sztuki
        /// razem, uwaga). Masa liczona tą samą formułą co arkusz BBS.
        /// </summary>
        public static BbsRow FromBbs(int barMark, int diameter, string shapeCode, bool top, double dimA,
                                     double cuttingLength, int total, string note, double linearMassKgPerM)
        {
            var row = new BbsRow
            {
                Mark              = MarkOf(diameter, barMark),
                Diameter          = diameter,
                ShapeCode         = string.IsNullOrWhiteSpace(shapeCode) ? "00" : shapeCode,
                Position          = barMark.ToString("00", CultureInfo.InvariantCulture),
                LayerCode         = top ? "TOP" : "BOTTOM",
                LengthA           = double.IsNaN(dimA) ? 0.0 : dimA,
                CuttingLength     = cuttingLength,
                BarLinesInDrawing = total,
                Note              = string.IsNullOrEmpty(note) ? null : note,
            };
            if (double.IsNaN(cuttingLength))
                row.LengthError = string.IsNullOrEmpty(note) ? "brak długości (jak w RC_BBS)" : note;
            row.TotalMassKg = MassKg(total, cuttingLength, linearMassKgPerM);
            return row;
        }
    }

    /// <summary>Wiersz RC_COUNT_BBS (przeniesiony z BbsCounter.cs bez zmian pól; Note — POPRAWKI 26).</summary>
    public class BbsRow
    {
        public string Mark { get; set; }
        public int Diameter { get; set; }
        public string ShapeCode { get; set; }
        public string Position { get; set; }
        public string LayerCode { get; set; }
        public double LengthA { get; set; }
        public int BarLinesInDrawing { get; set; }
        public double TotalMassKg { get; set; }
        /// <summary>W5: dlugosc ciecia [mm] (BarLengthRules.CuttingLength); NaN = brak dlugosci.</summary>
        public double CuttingLength { get; set; }
        /// <summary>W5: komunikat bledu dlugosci (null = dlugosc policzona).</summary>
        public string LengthError { get; set; }
        /// <summary>POPRAWKI 26: uwaga wiersza z BBS (np. „długość nadpisana”); null = brak.</summary>
        public string Note { get; set; }
    }
}
