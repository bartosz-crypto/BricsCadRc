using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Teigha.DatabaseServices;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Odczyt z rysunku wszystkiego, czego potrzebuje BBS:
    ///   • pręty: rozkłady (RC_BAR_BLOCK) z żywym opisem → pozycja pręta (RC_SINGLE_BAR), ilości z EffectiveCount,
    ///     długość cięcia jak w programie (ShapeCodeLibrary / długość nadpisana), wymiary A–E/R wg parametrów kształtu,
    ///   • layouty z blokiem tytułowym A1-BL,
    ///   • notatki płyty: SLAB AREA, HYSTOOLS DK90/DK165, grubość płyty.
    /// </summary>
    public static class BbsDrawingReader
    {
        public sealed class BarsResult
        {
            public List<BbsBarRow> Rows     { get; } = new List<BbsBarRow>();
            public List<string>    Warnings { get; } = new List<string>();
            public int Distributions;          // policzone rozkłady
        }

        private sealed class Group
        {
            public BbsBarRow Row;
            public string    Signature;
            public string    Mark;
        }

        // ------------------------------------------------------------------
        //  Pręty
        // ------------------------------------------------------------------

        public static BarsResult ReadBars(Database db)
        {
            var res = new BarsResult();
            var bars = new Dictionary<string, BarData>(StringComparer.OrdinalIgnoreCase);
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var noAnnot = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var orphans = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            var orphanStraight = new List<BarData>();

            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

                // 1) definicje prętów (pozycje)
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity ent) || ent is BlockReference) continue;
                    BarData bar;
                    try { bar = SingleBarEngine.ReadBarXData(ent); }
                    catch (Exception ex) { Log.Error("BbsDrawingReader.ReadBarXData", ex); continue; }
                    if (bar != null) bars[ent.Handle.Value.ToString("X8")] = bar;
                }

                // 2) rozkłady → ilości
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    BarData dist;
                    try { dist = BarBlockEngine.ReadXData(br); }
                    catch (Exception ex) { Log.Error("BbsDrawingReader.ReadXData", ex); continue; }
                    if (dist == null) continue;

                    // Zasada programu: rozkład bez opisu nie jest liczony
                    if (!BarBlockEngine.IsAnnotAlive(db, dist.AnnotHandle))
                    {
                        noAnnot.Add(string.IsNullOrEmpty(dist.Mark) ? "?" : dist.Mark);
                        continue;
                    }

                    string src = NormHandle(dist.SourceBarHandle);
                    if (src.Length > 0 && bars.ContainsKey(src))
                    {
                        counts[src] = (counts.TryGetValue(src, out int n) ? n : 0) + dist.EffectiveCount;
                        res.Distributions++;
                        continue;
                    }

                    // Brak pręta źródłowego — prosty pręt da się opisać z danych rozkładu
                    if ((dist.ShapeCode ?? "00") == "00" && dist.LengthA > 0)
                    {
                        var copy = new BarData
                        {
                            Mark = dist.Mark, Diameter = dist.Diameter, ShapeCode = "00",
                            LengthA = dist.LengthA, Count = dist.EffectiveCount
                        };
                        orphanStraight.Add(copy);
                        res.Distributions++;
                    }
                    orphans.Add(string.IsNullOrEmpty(dist.Mark) ? "?" : dist.Mark);
                }
                tr.Commit();
            }

            // 3) grupowanie po numerze pozycji
            var groups = new Dictionary<int, List<Group>>();
            void AddBar(BarData bar, int count, string origin)
            {
                if (count <= 0) return;
                if (!TryPosNr(bar.Mark, out int pos))
                {
                    res.Warnings.Add($"Pręt „{bar.Mark}” ({origin}): nie da się odczytać numeru pozycji — pominięty.");
                    return;
                }
                var row = BuildRow(bar, pos, out string warn);
                if (warn != null) res.Warnings.Add(warn);
                row.NoEach = count;
                string sig = Signature(row);
                if (!groups.TryGetValue(pos, out var list)) groups[pos] = list = new List<Group>();
                var same = list.FirstOrDefault(g => g.Signature == sig);
                if (same != null) same.Row.NoEach += count;
                else list.Add(new Group { Row = row, Signature = sig, Mark = bar.Mark });
            }

            foreach (var kv in counts) AddBar(bars[kv.Key], kv.Value, "rozkład");
            foreach (var b in orphanStraight) AddBar(b, b.Count, "rozkład bez pręta");

            foreach (var kv in groups.OrderBy(k => k.Key))
            {
                if (kv.Value.Count > 1)
                {
                    string variants = string.Join("; ", kv.Value.Select(g =>
                        $"{g.Row.TypeSize} kod {g.Row.ShapeCode} L={g.Row.LengthText} ({g.Row.NoEach} szt.)"));
                    res.Warnings.Add($"Pozycja {kv.Key:00} ma {kv.Value.Count} różne pręty: {variants} — sprawdź numerację (RC_UPDATE_BAR).");
                    foreach (var g in kv.Value) g.Row.Note = AppendNote(g.Row.Note, "różne pręty pod jednym numerem");
                }
                foreach (var g in kv.Value) res.Rows.Add(g.Row);
            }

            if (noAnnot.Count > 0)
                res.Warnings.Add($"Rozkłady bez opisu NIE są liczone ({noAnnot.Count} poz.): {string.Join(", ", noAnnot.Take(15))}" +
                                 (noAnnot.Count > 15 ? " …" : "") + ".");
            if (orphans.Count > 0)
                res.Warnings.Add($"Rozkłady bez pręta źródłowego: {string.Join(", ", orphans.Take(15))}" +
                                 (orphans.Count > 15 ? " …" : "") + " — proste liczone z danych rozkładu, giętych nie liczono.");
            int unused = bars.Keys.Count(h => !counts.ContainsKey(h));
            if (unused > 0)
                Log.Info($"BBS: {unused} prętów bez rozkładów (szablony) — pominięte.");
            return res;
        }

        private static string NormHandle(string h) =>
            !string.IsNullOrWhiteSpace(h) && long.TryParse(h.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long v)
                ? v.ToString("X8") : "";

        /// <summary>„H12-03” → 3, „H12-101” → 101.</summary>
        public static bool TryPosNr(string mark, out int pos)
        {
            pos = 0;
            if (string.IsNullOrWhiteSpace(mark)) return false;
            var m = Regex.Match(mark, @"^\s*[A-Za-z]*\d+\s*-\s*(\d+)");
            return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out pos);
        }

        private static BbsBarRow BuildRow(BarData bar, int pos, out string warning)
        {
            warning = null;
            var row = new BbsBarRow
            {
                BarMark   = pos,
                Diameter  = bar.Diameter,
                ShapeCode = string.IsNullOrWhiteSpace(bar.ShapeCode) ? "00" : bar.ShapeCode.Trim(),
            };
            if (row.ShapeCode == "0") row.ShapeCode = "00";

            var shape = ShapeCodeLibrary.Get(row.ShapeCode);
            double[] vals = bar.ParamValues;

            if (shape == null)
            {
                for (int i = 0; i < 5; i++) row.Dims[i] = vals[i];
                row.Length = bar.LengthOverridden && bar.TotalLength > 0 ? bar.TotalLength : double.NaN;
                if (double.IsNaN(row.Length))
                {
                    warning = $"Pozycja {pos:00}: nieznany kod kształtu {row.ShapeCode} — brak długości (uzupełnij w arkuszu).";
                    row.Note = "nieznany kształt";
                }
                return row;
            }

            // Wymiary do kolumn wg nazw parametrów kształtu (A,B,C,D,E/R)
            for (int i = 0; i < shape.Parameters.Length && i < vals.Length; i++)
            {
                int col = ColumnOf(shape.Parameters[i]);
                if (col >= 0) row.Dims[col] = vals[i];
            }

            if (bar.LengthOverridden && bar.TotalLength > 0)
            {
                row.Length = bar.TotalLength;
                row.Note = "długość nadpisana";
            }
            else
            {
                try
                {
                    row.Length = shape.CalculateTotalLength(vals.Take(shape.Parameters.Length).ToArray(), bar.Diameter);
                }
                catch (Exception ex)
                {
                    Log.Error("BbsDrawingReader.Length", ex);
                    warning = $"Pozycja {pos:00}: nie udało się policzyć długości ({ex.Message}).";
                    row.Note = "brak długości";
                }
            }
            if (bar.Diameter <= 0 || BarData.GetLinearMass(bar.Diameter) <= 0)
            {
                warning = $"Pozycja {pos:00}: nietypowa średnica {bar.Diameter} — masa nie zostanie policzona w arkuszu.";
                row.Note = AppendNote(row.Note, "średnica?");
            }
            return row;
        }

        private static int ColumnOf(string param)
        {
            switch ((param ?? "").Trim().ToUpperInvariant())
            {
                case "A": return 0;
                case "B": return 1;
                case "C": return 2;
                case "D": return 3;
                case "E":
                case "R":
                case "E/R": return 4;
                default: return -1;
            }
        }

        private static string Signature(BbsBarRow r) =>
            string.Join("|", r.Diameter, r.ShapeCode, Math.Round(double.IsNaN(r.Length) ? -1 : r.Length),
                string.Join(",", r.Dims.Select(d => double.IsNaN(d) ? "-" : Math.Round(d).ToString(CultureInfo.InvariantCulture))));

        private static string AppendNote(string a, string b) => string.IsNullOrEmpty(a) ? b : a + "; " + b;

        // ------------------------------------------------------------------
        //  Layouty z blokiem A1-BL
        // ------------------------------------------------------------------

        public static List<BbsLayoutInfo> ReadLayouts(Database db, string titleBlockName = "A1-BL")
        {
            var result = new List<BbsLayoutInfo>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry e in dict)
                {
                    if (!(tr.GetObject(e.Value, OpenMode.ForRead) is Layout layout) || layout.ModelType) continue;
                    var btr = (BlockTableRecord)tr.GetObject(layout.BlockTableRecordId, OpenMode.ForRead);
                    foreach (ObjectId id in btr)
                    {
                        if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                        if (!string.Equals(EffectiveName(tr, br), titleBlockName, StringComparison.OrdinalIgnoreCase)) continue;

                        var attrs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (ObjectId aid in br.AttributeCollection)
                            if (tr.GetObject(aid, OpenMode.ForRead) is AttributeReference ar)
                                attrs[ar.Tag] = ar.TextString;
                        attrs.TryGetValue("DRAWING_NUMBER", out string drg);
                        result.Add(new BbsLayoutInfo { LayoutName = layout.LayoutName, DrawingNumber = drg, Attributes = attrs });
                        break;
                    }
                }
                tr.Commit();
            }
            result.Sort((a, b) => string.Compare(a.LayoutName, b.LayoutName, StringComparison.OrdinalIgnoreCase));
            return result;
        }

        private static string EffectiveName(Transaction tr, BlockReference br)
        {
            try
            {
                if (br.IsDynamicBlock)
                    return ((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord, OpenMode.ForRead)).Name;
            }
            catch (Exception ex) { Log.Error("BbsDrawingReader.EffectiveName", ex); }
            return br.Name;
        }

        // ------------------------------------------------------------------
        //  Notatki płyty (akcesoria)
        // ------------------------------------------------------------------

        public sealed class SlabNotes
        {
            public List<double> Areas { get; } = new List<double>();   // wszystkie różne SLAB AREA
            public string HystoolsType;                                // „DK90” / „DK165” / null
            public int?   ThicknessMm;
        }

        private static readonly Regex RxFormat    = new Regex(@"\\[ACcFfHhQqTtWwp][^;\\]*;|\\[LlOoKkP~]|[{}]");
        private static readonly Regex RxArea      = new Regex(@"SLAB\s+AREA\s*=\s*([\d]+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
        private static readonly Regex RxHystools  = new Regex(@"HYSTOOLS\s+DK\s*(90|165)", RegexOptions.IgnoreCase);
        private static readonly Regex RxThickness = new Regex(@"SLAB\s+THICKNESS\s*=\s*(\d+)|(\d{3})\s*mm\s+THK", RegexOptions.IgnoreCase);

        public static SlabNotes ReadSlabNotes(Database db)
        {
            var notes = new SlabNotes();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId btrId in bt)
                {
                    var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                    if (!btr.IsLayout) continue;
                    foreach (ObjectId id in btr)
                    {
                        string raw = null;
                        var o = tr.GetObject(id, OpenMode.ForRead);
                        if (o is MText mt) raw = mt.Contents;
                        else if (o is DBText dt) raw = dt.TextString;
                        if (string.IsNullOrEmpty(raw)) continue;
                        string t = RxFormat.Replace(raw, " ");

                        foreach (Match m in RxArea.Matches(t))
                            if (double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double a)
                                && a > 0 && !notes.Areas.Any(x => Math.Abs(x - a) < 0.005))
                                notes.Areas.Add(a);

                        if (notes.HystoolsType == null)
                        {
                            var h = RxHystools.Match(t);
                            if (h.Success) notes.HystoolsType = "DK" + h.Groups[1].Value;
                        }
                        if (notes.ThicknessMm == null)
                        {
                            var th = RxThickness.Match(t);
                            if (th.Success && int.TryParse(th.Groups[1].Success ? th.Groups[1].Value : th.Groups[2].Value, out int mm))
                                notes.ThicknessMm = mm;
                        }
                    }
                }
                tr.Commit();
            }
            return notes;
        }
    }
}
