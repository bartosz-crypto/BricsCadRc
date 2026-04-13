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
        // Stałe BS 8666:2020 — naddatek na hak/zagięcie [mm]
        // ----------------------------------------------------------------
        private const double Hook90  = 4.0;   // mnożnik r × (domyślna minimalna mandrel = 4d) — uproszczenie
        private const double Hook135 = 3.0;
        private const double Hook180 = 4.0;

        // ----------------------------------------------------------------
        // Oblicza długość cięcia jednego pręta [mm] wg BS 8666:2020.
        // Parametry A–E w mm. pR = promień łuku = 0 (kąt 90° → naddatek standardowy).
        // ----------------------------------------------------------------
        public static double CalcCuttingLength(string shapeCode, int diameter,
            double a, double b, double c, double d, double e)
        {
            // Naddatek na zagięcie wg BS 8666:2020 tabela 2 (uproszczone):
            // dla zagięcia 90°: n = 2d (dla d ≤ 16mm) lub 3d (d > 16mm)
            // Używamy uproszczonego modelu spójnego z ASD.
            double n90  = diameter <= 16 ? 2.0 * diameter : 3.0 * diameter;
            double n135 = diameter <= 16 ? 1.5 * diameter : 2.0 * diameter;
            double n180 = diameter <= 16 ? 1.5 * diameter : 2.0 * diameter;

            switch (shapeCode)
            {
                // 00 — pręt prosty
                case "00":
                    return a;

                // 11 — L-bar z hakiem 90° (A + B - n)
                case "11":
                    return a + b - n90;

                // 12 — L-bar z hakiem 135°
                case "12":
                    return a + b - n135;

                // 13 — L-bar z hakiem 180° (= U-bar z jednym ramieniem)
                case "13":
                    return a + b - n180;

                // 21 — Z/S-bar (A + B + C - 2n)
                case "21":
                    return a + b + c - 2.0 * n90;

                // 22 — Cranked bar (A + B + C - 2n) tak samo jak 21
                case "22":
                    return a + b + c - 2.0 * n90;

                // 23 — S-bar asymetryczny
                case "23":
                    return a + b + c - 2.0 * n90;

                // 24
                case "24":
                    return a + b + c - 2.0 * n90;

                // 25 — U-bar (A + 2B - 2n)
                case "25":
                    return a + 2.0 * b - 2.0 * n90;

                // 26
                case "26":
                    return a + b + c + d - 3.0 * n90;

                // 32 — stirrup zamknięty 4-cięty (2A + 2B - 4n + hak)
                case "32":
                    return 2.0 * a + 2.0 * b - 4.0 * n90 + 24.0 * diameter;

                // 33 — stirrup zamknięty (jak 32 ale trapez)
                case "33":
                    return 2.0 * a + b + c - 3.0 * n90 + 24.0 * diameter;

                // 51 — strzemię zamknięte wg BS 8666: 2(A+B+C) - 2.5r - 5d
                // C = długość haka (domyślnie MAX(16d, 160) gdy C=0)
                case "51":
                {
                    double hook51 = c > 0 ? c : Math.Max(16 * diameter, 160);
                    double r51    = BarShape.MinBendRadius(diameter);
                    return 2 * (a + b + hook51) - 2.5 * r51 - 5 * diameter;
                }

                // Pozostałe — suma wszystkich parametrów (konserw. podejście)
                default:
                    return a + b + c + d + e;
            }
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

                    double rawLength = CalcCuttingLength(
                        bar.ShapeCode, bar.Diameter,
                        bar.LengthA, bar.LengthB, bar.LengthC, bar.LengthD, bar.LengthE);

                    int.TryParse(bar.ShapeCode, out int sc);
                    // BS 8666:2020 — identycznie jak Excel: ROUNDUP(L/25,0)*25
                    // Kod 00 (prosty): długość bez zaokrąglania (lub zaokrąglona do 1mm)
                    // Pozostałe: zaokrąglone w górę do najbliższego 25mm
                    int lengthMm = sc == 0
                        ? (int)Math.Round(rawLength, MidpointRounding.AwayFromZero)
                        : (int)(Math.Ceiling(rawLength / 25.0) * 25);
                    double cutLen = lengthMm;

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
