using System;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Buduje zestawienie prętów (BBS) ze wszystkich RC_SINGLE_BAR i RC_BAR_BLOCK
    /// w bieżącym rysunku. Oblicza długości cięcia wg BS 8666:2020.
    /// </summary>
    public static class BarScheduleEngine
    {
        // ----------------------------------------------------------------
        // Oblicza długość cięcia jednego pręta [mm] wg BS 8666:2020.
        // Deleguje do ShapeCodeLibrary/BarShape — formuły i zaokrąglenie
        // do 25mm wykonywane wewnątrz CalculateTotalLength.
        // ----------------------------------------------------------------
        private static double CalcCuttingLength(
            string shapeCode, double diameter,
            double a, double b, double c, double d, double e)
        {
            var shape = ShapeCodeLibrary.Get(shapeCode);
            if (shape != null)
            {
                // Przytnij do liczby parametrów kształtu (shape ma 1–5 parametrów)
                var p = new[] { a, b, c, d, e }.Take(shape.Parameters.Length).ToArray();
                return shape.CalculateTotalLength(p, diameter);
                // CalculateTotalLength już robi zaokrąglenie do 25mm wewnątrz
            }
            // Fallback dla nieznanych shape codes
            double fallback = Math.Max(a, Math.Max(b, Math.Max(c, Math.Max(d, e))));
            return Math.Ceiling(fallback / 25.0) * 25.0;
        }

        // ----------------------------------------------------------------
        // Buduje zestawienie ze wszystkich prętów w rysunku.
        // Iteruje po RC_SINGLE_BAR (polilinii pręta); sumuje liczby ze wszystkich
        // powiązanych RC_BAR_BLOCK (rozkładów).
        // ----------------------------------------------------------------
        public static List<BarScheduleEntry> BuildSchedule(Database db)
        {
            // Słownik: Handle polilinii (hex) → entry
            var byHandle = new Dictionary<string, BarScheduleEntry>(StringComparer.OrdinalIgnoreCase);

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var modelSpace = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

                // Krok 1 — zbierz wszystkie RC_SINGLE_BAR (polilinii pręta)
                foreach (ObjectId oid in modelSpace)
                {
                    Entity ent;
                    try { ent = tr.GetObject(oid, OpenMode.ForRead) as Entity; }
                    catch { continue; }

                    if (ent == null || ent.IsErased) continue;

                    var bar = SingleBarEngine.ReadBarXData(ent);
                    if (bar == null) continue;

                    string handle = ent.Handle.Value.ToString("X8");

                    double cutLen = CalcCuttingLength(
                        bar.ShapeCode, bar.Diameter,
                        bar.LengthA, bar.LengthB, bar.LengthC, bar.LengthD, bar.LengthE);
                    // zaokrąglenie do 25mm już wykonane wewnątrz CalculateTotalLength

                    // Wyciągnij numer pozycji z Mark (np. "H12-01" → "01")
                    string posNr = ExtractPosNrStr(bar.Mark);

                    var entry = new BarScheduleEntry
                    {
                        PosNr         = posNr,
                        Mark          = bar.Mark,
                        ShapeCode     = bar.ShapeCode ?? "00",
                        Diameter      = bar.Diameter,
                        A             = bar.LengthA,
                        B             = bar.LengthB,
                        C             = bar.LengthC,
                        D             = bar.LengthD,
                        E             = bar.LengthE,
                        CuttingLength = cutLen,
                        TotalCount    = 0,
                        LinearMass    = BarData.GetLinearMass(bar.Diameter)
                    };

                    byHandle[handle] = entry;
                }

                // Krok 2 — zlicz pręty ze wszystkich RC_BAR_BLOCK (rozkładów)
                foreach (ObjectId oid in modelSpace)
                {
                    Entity ent;
                    try { ent = tr.GetObject(oid, OpenMode.ForRead) as Entity; }
                    catch { continue; }

                    if (ent == null || ent.IsErased) continue;

                    var barBlock = BarBlockEngine.ReadXData(ent);
                    if (barBlock == null) continue;

                    if (string.IsNullOrEmpty(barBlock.SourceBarHandle)) continue;

                    if (byHandle.TryGetValue(barBlock.SourceBarHandle, out var entry))
                        entry.TotalCount += barBlock.Count;
                }

                tr.Commit();
            }

            // Krok 3 — posortuj po numerze pozycji i usuń pręty bez rozkładu (count=0)
            var result = byHandle.Values
                .Where(e => e.TotalCount > 0)
                .OrderBy(e => ParsePosNr(e.PosNr))
                .ThenBy(e => e.PosNr)
                .ToList();

            return result;
        }

        // ----------------------------------------------------------------
        // Suma masy w kg ze wszystkich wierszy
        // ----------------------------------------------------------------
        public static double TotalMass(List<BarScheduleEntry> entries)
            => entries.Sum(e => e.TotalMassKg);

        // ----------------------------------------------------------------
        // Pomocnicze
        // ----------------------------------------------------------------
        private static string ExtractPosNrStr(string mark)
        {
            if (string.IsNullOrEmpty(mark)) return "00";
            var parts = mark.Split('-');
            return parts.Length >= 2 ? parts[1] : "00";
        }

        private static int ParsePosNr(string s)
        {
            return int.TryParse(s, out int n) ? n : 0;
        }
    }
}
