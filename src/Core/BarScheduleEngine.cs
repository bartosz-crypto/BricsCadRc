using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
                        TotalCount    = GetCountFromLabel(db, tr, bar.LabelHandle),
                        LinearMass    = BarData.GetLinearMass(bar.Diameter)
                    };

                    byHandle[handle] = entry;
                }

                tr.Commit();
            }

            // Krok 2 — posortuj po numerze pozycji (Count zawsze ≥ 1 z ParseLabelCount fallback)
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

        // ----------------------------------------------------------------
        // Pobierz Count z tekstu labela MLeader (np. "7 H12-01" → 7).
        // Fallback = 1 jeśli label nie istnieje lub nie ma wiodącej liczby.
        // ----------------------------------------------------------------
        private static int GetCountFromLabel(Database db, Transaction tr, string labelHandle)
        {
            if (string.IsNullOrEmpty(labelHandle)) return 1;
            if (!long.TryParse(labelHandle,
                    System.Globalization.NumberStyles.HexNumber, null, out long hVal)) return 1;

            if (!db.TryGetObjectId(new Handle(hVal), out ObjectId lblId)
                || lblId.IsNull || lblId.IsErased) return 1;

            MLeader ml;
            try { ml = tr.GetObject(lblId, OpenMode.ForRead) as MLeader; }
            catch { return 1; }
            if (ml == null) return 1;

            return ParseLabelCount(ml.MText?.Contents ?? "");
        }

        private static int ParseLabelCount(string labelText)
        {
            if (string.IsNullOrEmpty(labelText)) return 1;
            // Strip MText RTF-like formatting blocks ({\\...}) before parsing
            var plain = Regex.Replace(labelText, @"\{\\[^}]*\}", "").Trim();
            var match = Regex.Match(plain, @"^(\d+)\s");
            return match.Success && int.TryParse(match.Groups[1].Value, out int n) && n > 0 ? n : 1;
        }
    }
}
