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
            }
            // Fallback dla nieznanych shape codes
            double fallback = Math.Max(a, Math.Max(b, Math.Max(c, Math.Max(d, e))));
            return Math.Ceiling(fallback / 25.0) * 25.0;
        }

        // ----------------------------------------------------------------
        // Buduje zestawienie ze wszystkich prętów w rysunku.
        // Krok 1: iteruje RC_SINGLE_BAR — tworzy entry z TotalCount=0.
        // Krok 2: iteruje RC_BAR_BLOCK — sumuje EffectiveCount (honoruje CountDisplay override).
        // Krok 3: fallback — pręt bez rozkładów zlicza siebie jako 1.
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

                // Krok 2 — sumuj EffectiveCount z każdego RC_BAR_BLOCK do odpowiedniego pręta
                foreach (ObjectId oid in modelSpace)
                {
                    if (oid.IsErased) continue;
                    var br = tr.GetObject(oid, OpenMode.ForRead) as BlockReference;
                    if (br == null) continue;

                    var barBlock = BarBlockEngine.ReadXData(br);
                    if (barBlock == null || string.IsNullOrEmpty(barBlock.SourceBarHandle)) continue;

                    if (!BarBlockEngine.IsAnnotAlive(db, barBlock.AnnotHandle)) continue;

                    if (byHandle.TryGetValue(barBlock.SourceBarHandle, out var entry))
                        entry.TotalCount += barBlock.EffectiveCount;
                }

                // Krok 3 — usuń pręty bez rozkładów (nie pojawiają się w zestawieniu)
                var toRemove = byHandle.Where(kv => kv.Value.TotalCount == 0).Select(kv => kv.Key).ToList();
                foreach (var key in toRemove)
                    byHandle.Remove(key);

                tr.Commit();
            }

            // Posortuj po numerze pozycji
            var result = byHandle.Values
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
