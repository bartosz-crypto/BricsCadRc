using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NPOI.HSSF.UserModel;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Generator BBS (.xls) na szablonie Speedeck (default-bbs.xls) — port ASD-BBS z AsdRcSlab:
    ///   • arkusz szablonu klonowany na każdą stronę (26 wierszy: od „BOTTOM LAYER” do „Accessories”),
    ///   • dół (pozycje &lt; 100) i góra (101+) na osobnych stronach; layouty „Bottom + Top” → jedna kartka,
    ///     jeśli się mieści (z pustym wierszem odstępu),
    ///   • nagłówek: opis A3, Contract No., adres (zawijany), rewizja, lista rysunków, „Page X of Y”,
    ///   • akcesoria (TRIC-TRAK / HYSTOOLS) tylko na pierwszym arkuszu,
    ///   • formuły szablonu (masa, tonaż) przeliczane przed zapisem.
    /// Szablon: plik użytkownika albo wbudowany (EmbeddedResource „BricsCadRc.default-bbs.xls”).
    /// Bez API BricsCAD — testowalny poza CAD-em.
    /// </summary>
    public static class BbsXlsGenerator
    {
        public const string EmbeddedTemplateName = "BricsCadRc.default-bbs.xls";

        // Wiersze / kolumny nagłówka (0-based) — układ szablonu Speedeck
        private const int RowA3 = 2, RowB5 = 4, RowH5 = 4, RowH6 = 5, RowH7 = 6, RowB7 = 6, RowL8 = 7, RowM5 = 4;
        private const int ColA = 0, ColB = 1, ColC = 2, ColD = 3, ColE = 4, ColF = 5, ColG = 6,
                          ColH = 7, ColI = 8, ColL = 11, ColM = 12;
        private const int DataColLast = 12;
        private const int MaxSheetNameLength = 31;

        private const string BaseDescription = "REINFORCEMENT DETAILS OF SPEEDECK PILED RAFT FOUNDATION - ";

        // ------------------------------------------------------------------
        //  Szablon
        // ------------------------------------------------------------------

        /// <summary>Otwiera szablon: plik (gdy podany i istnieje) albo wbudowany.</summary>
        public static IWorkbook OpenTemplate(string templatePath, out string sourceInfo)
        {
            if (!string.IsNullOrWhiteSpace(templatePath))
            {
                if (!File.Exists(templatePath))
                    throw new FileNotFoundException("Nie znaleziono szablonu BBS.", templatePath);
                using (var fs = new FileStream(templatePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    sourceInfo = templatePath;
                    return Path.GetExtension(templatePath).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)
                        ? (IWorkbook)new XSSFWorkbook(fs)
                        : new HSSFWorkbook(fs);
                }
            }

            var asm = typeof(BbsXlsGenerator).Assembly;
            using (var s = asm.GetManifestResourceStream(EmbeddedTemplateName))
            {
                if (s == null)
                    throw new InvalidOperationException("Brak wbudowanego szablonu BBS (" + EmbeddedTemplateName + ").");
                sourceInfo = "wbudowany default-bbs.xls";
                return new HSSFWorkbook(s);
            }
        }

        /// <summary>Sekcja danych w arkuszu szablonu: wiersz „BOTTOM LAYER” … wiersz przed „Accessories”.</summary>
        public sealed class TemplateLayout
        {
            public int SheetIndex;
            public int FirstDataRow;
            public int Capacity;
        }

        public static TemplateLayout DetectLayout(IWorkbook wb)
        {
            for (int s = 0; s < wb.NumberOfSheets; s++)
            {
                var sheet = wb.GetSheetAt(s);
                int? label = null, acc = null, top = null;
                for (int r = 0; r <= sheet.LastRowNum; r++)
                {
                    string a = CellText(sheet, r, ColA)?.Trim().ToUpperInvariant();
                    if (string.IsNullOrEmpty(a)) continue;
                    if (a == "BOTTOM LAYER" && label == null) label = r;
                    else if (a == "TOP LAYER" && top == null) top = r;
                    else if (a.StartsWith("ACCESSORIES") && acc == null) acc = r;
                }
                int? first = label ?? top;
                if (first == null) continue;
                int end = acc ?? (sheet.LastRowNum + 1);
                int cap = end - first.Value;
                if (cap <= 0) continue;
                return new TemplateLayout { SheetIndex = s, FirstDataRow = first.Value, Capacity = cap };
            }
            return null;
        }

        // ------------------------------------------------------------------
        //  Generowanie
        // ------------------------------------------------------------------

        public static BbsGenerateResult Generate(BbsGenerationContext ctx, List<BbsBarRow> rows,
                                                 string templatePath, string outputPath)
        {
            if (ctx == null) throw new ArgumentNullException(nameof(ctx));
            if (rows == null) throw new ArgumentNullException(nameof(rows));
            if (string.IsNullOrWhiteSpace(outputPath)) return Fail("Nie podano pliku wynikowego.");

            IWorkbook wb = OpenTemplate(templatePath, out _);
            var lay = DetectLayout(wb);
            if (lay == null)
                return Fail("Szablon nie ma sekcji „BOTTOM LAYER” w kolumnie A (od niej liczona jest pojemność strony).");

            var template = wb.GetSheetAt(lay.SheetIndex);
            int cap = lay.Capacity;

            var bottom = rows.Where(r => r.Layer == BbsLayer.Bottom).OrderBy(r => r.BarMark).ToList();
            var top    = rows.Where(r => r.Layer == BbsLayer.Top).OrderBy(r => r.BarMark).ToList();
            if (bottom.Count + top.Count == 0) return Fail("Brak prętów do zapisania.");

            if (bottom.Count > 0 && ctx.BottomLayouts.Count == 0)
                return Fail($"{bottom.Count} pozycji dołem, ale żaden layout nie jest przypisany jako BOTTOM.");
            if (top.Count > 0 && ctx.TopLayouts.Count == 0)
                return Fail($"{top.Count} pozycji górą, ale żaden layout nie jest przypisany jako TOP.");

            // Wszystkie przypisane layouty = „Bottom + Top” → wspólna numeracja stron (i jedna kartka, gdy się mieści)
            var bt = ctx.BottomAndTopLayouts;
            bool allBt = bt.Count > 0 && ctx.BottomLayouts.Count == bt.Count && ctx.TopLayouts.Count == bt.Count;

            var res = new BbsGenerateResult { OutputPath = outputPath };
            var pages = new List<Page>();

            if (allBt && bottom.Count + top.Count + (bottom.Count > 0 && top.Count > 0 ? 1 : 0) <= cap)
            {
                pages.Add(new Page { Layouts = bt, Description = "BOTTOM & TOP LAYER", Bottom = bottom, Top = top });
            }
            else
            {
                var bLay = allBt ? bt : ctx.BottomLayouts;
                var tLay = allBt ? bt : ctx.TopLayouts;
                foreach (var chunk in Chunk(bottom, cap))
                    pages.Add(new Page { Layouts = bLay, Description = "BOTTOM LAYER", Bottom = chunk, Group = 0 });
                foreach (var chunk in Chunk(top, cap))
                    pages.Add(new Page { Layouts = tLay, Description = "TOP LAYER", Top = chunk, Group = allBt ? 0 : 1 });
            }

            // Numeracja „Page X of Y” w obrębie grupy (osobno dół i góra, wspólnie przy Bottom + Top)
            foreach (var g in pages.GroupBy(p => p.Group))
            {
                int n = 0, total = g.Count();
                foreach (var p in g) { p.PageNo = ++n; p.PageCount = total; }
            }

            bool first = true;
            foreach (var p in pages)
            {
                ISheet sh = wb.CloneSheet(lay.SheetIndex);
                int idx = wb.GetSheetIndex(sh);
                string prefix = BuildSheetPrefix(p.Layouts, ctx.Revision);
                wb.SetSheetName(idx, UniqueSheetName(wb, BuildSheetName(prefix, p.PageNo, p.PageCount)));
                CopyStructure(template, sh);

                FillHeader(sh, ctx, BuildDrgLines(p.Layouts), BuildA3(p.Description, ctx.PlotSuffix), p.PageNo, p.PageCount);

                // Etykieta sekcji z szablonu („BOTTOM LAYER”) — nadpisywana właściwą
                SetString(sh, lay.FirstDataRow, ColA, "");
                int r = lay.FirstDataRow;
                if (p.Bottom.Count > 0)
                {
                    SetString(sh, r, ColA, "BOTTOM LAYER");
                    foreach (var b in p.Bottom) WriteRow(sh, r++, b);
                    res.BottomRows += p.Bottom.Count;
                    if (p.Top.Count > 0) r++;   // wiersz odstępu na wspólnej kartce
                }
                if (p.Top.Count > 0)
                {
                    SetString(sh, r, ColA, "TOP LAYER");
                    foreach (var b in p.Top) WriteRow(sh, r++, b);
                    res.TopRows += p.Top.Count;
                }

                if (first) FillAccessories(sh, ctx);
                else ClearAccessories(sh);
                first = false;

                wb.SetPrintArea(idx, 0, DataColLast, 0, 43);
                if (p.Bottom.Count > 0) res.BottomPages++; else res.TopPages++;
            }

            wb.RemoveSheetAt(lay.SheetIndex);
            // Klony dziedziczą zaznaczenie zakładki szablonu → Excel otwierałby arkusze jako [Grupa]
            for (int i = 0; i < wb.NumberOfSheets; i++) wb.GetSheetAt(i).IsSelected = i == 0;
            wb.SetActiveSheet(0);
            wb.SetSelectedTab(0);

            try
            {
                wb.GetCreationHelper().CreateFormulaEvaluator().EvaluateAll();
            }
            catch (Exception ex)
            {
                // Bez przeliczenia Excel pokaże stare wartości do pierwszej edycji — nie blokujemy zapisu
                LogSafe("BbsXlsGenerator.EvaluateAll", ex);
            }
            for (int i = 0; i < wb.NumberOfSheets; i++) wb.GetSheetAt(i).ForceFormulaRecalculation = true;

            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
            using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                wb.Write(fs);
            wb.Close();

            res.Success = true;
            res.Message = pages.Count == 1 && pages[0].Bottom.Count > 0 && pages[0].Top.Count > 0
                ? $"1 arkusz (dół {res.BottomRows} + góra {res.TopRows} pozycji)."
                : $"Dół: {res.BottomPages} str. ({res.BottomRows} poz.), góra: {res.TopPages} str. ({res.TopRows} poz.).";
            return res;
        }

        private sealed class Page
        {
            public List<BbsLayoutInfo> Layouts;
            public string Description;
            public List<BbsBarRow> Bottom = new List<BbsBarRow>();
            public List<BbsBarRow> Top    = new List<BbsBarRow>();
            public int Group, PageNo = 1, PageCount = 1;
        }

        private static IEnumerable<List<BbsBarRow>> Chunk(List<BbsBarRow> list, int size)
        {
            for (int i = 0; i < list.Count; i += size)
                yield return list.Skip(i).Take(size).ToList();
        }

        private static BbsGenerateResult Fail(string msg) => new BbsGenerateResult { Success = false, Message = msg };

        private static void LogSafe(string where, Exception ex)
        {
            try { Log.Error(where, ex); } catch { /* log niedostępny poza CAD-em */ }
        }

        // ------------------------------------------------------------------
        //  Wiersz pręta (B–M) — jak BbsXlsWriter.WriteOneRow w ASD
        // ------------------------------------------------------------------

        private static void WriteRow(ISheet sh, int r, BbsBarRow b)
        {
            var row = sh.GetRow(r) ?? sh.CreateRow(r);
            Cell(row, ColB).SetCellValue(b.BarMark);
            Cell(row, ColC).SetCellValue(b.TypeSize);
            Cell(row, ColD).SetCellValue(b.NoMembers);
            Cell(row, ColE).SetCellValue(b.NoEach);
            Cell(row, ColF).SetCellValue(b.Total);
            if (!double.IsNaN(b.Length)) Cell(row, ColG).SetCellValue(Math.Round(b.Length));

            if (b.IsStraight)
            {
                Cell(row, ColH).SetCellValue("00");
                Cell(row, ColI).SetCellValue("STR");
                return;
            }

            if (int.TryParse(b.ShapeCode, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
                Cell(row, ColH).SetCellValue(code);
            else
                Cell(row, ColH).SetCellValue(b.ShapeCode ?? "");

            for (int i = 0; i < 5; i++)
            {
                double v = b.Dims[i];
                if (!double.IsNaN(v) && v != 0) Cell(row, ColI + i).SetCellValue(Math.Round(v));
            }
        }

        // ------------------------------------------------------------------
        //  Nagłówek
        // ------------------------------------------------------------------

        private static void FillHeader(ISheet sh, BbsGenerationContext ctx, List<string> drgLines,
                                       string description, int page, int pages)
        {
            SetString(sh, RowA3, ColA, description);
            SetString(sh, RowB5, ColB, ctx.ContractNo ?? "");
            var addr = SplitAddress(ctx.AddressLine1, ctx.AddressLine2, ctx.AddressLine3);
            SetString(sh, RowH5, ColH, addr[0]);
            SetString(sh, RowH6, ColH, addr[1]);
            SetString(sh, RowH7, ColH, addr[2]);
            SetString(sh, RowB7, ColB, ctx.Revision ?? "");
            for (int i = 0; i < drgLines.Count; i++)
                SetString(sh, RowM5 + i, ColM, drgLines[i]);
            SetString(sh, RowL8, ColL, $"Page {page} of {pages}");
        }

        public static string BuildA3(string layerLabel, string plotSuffix) =>
            string.IsNullOrWhiteSpace(plotSuffix)
                ? BaseDescription + layerLabel
                : BaseDescription + layerLabel + " - " + plotSuffix.Trim();

        /// <summary>Jeden rysunek na linię (M5, M6 …), nieostatnie z przecinkiem.</summary>
        private static List<string> BuildDrgLines(List<BbsLayoutInfo> layouts)
        {
            var nums = layouts.Select(l => l.ShortNumber).ToList();
            return nums.Select((n, i) => i < nums.Count - 1 ? n + "," : n).ToList();
        }

        /// <summary>„RC010-RC012-C1”.</summary>
        private static string BuildSheetPrefix(List<BbsLayoutInfo> layouts, string revision)
        {
            string list = string.Join("-", layouts.Select(l => l.ShortNumber));
            if (list.Length == 0) list = "BBS";
            return string.IsNullOrWhiteSpace(revision) ? list : list + "-" + revision.Trim();
        }

        private static string BuildSheetName(string prefix, int page, int total)
        {
            prefix = Regex.Replace(prefix ?? "BBS", @"[\[\]\*\?/\\:]", "_");
            string suffix = total <= 1 ? "" : $"-Page-{page}of{total}";
            string name = prefix + suffix;
            if (name.Length <= MaxSheetNameLength) return name;
            int maxPrefix = MaxSheetNameLength - suffix.Length;
            return maxPrefix < 1 ? $"Page-{page}of{total}" : prefix.Substring(0, maxPrefix) + suffix;
        }

        private static string UniqueSheetName(IWorkbook wb, string name)
        {
            if (wb.GetSheetIndex(name) < 0) return name;
            for (int i = 2; i < 100; i++)
            {
                string sfx = "_" + i;
                string b = name.Length + sfx.Length > MaxSheetNameLength ? name.Substring(0, MaxSheetNameLength - sfx.Length) : name;
                if (wb.GetSheetIndex(b + sfx) < 0) return b + sfx;
            }
            return name;
        }

        /// <summary>Adres w 3 liniach H5–H7: linia 1 ≤ 25 znaków → bez zmian; dłuższa → sklejenie i podział po przecinkach.</summary>
        public static List<string> SplitAddress(string line1, string line2, string line3, int maxPerLine = 25, int maxLines = 3)
        {
            string l1 = (line1 ?? "").Trim(), l2 = (line2 ?? "").Trim(), l3 = (line3 ?? "").Trim();
            if (l1.Length <= maxPerLine) return new List<string> { l1, l2, l3 };

            string full = string.Join(" ", new[] { l1, l2, l3 }.Where(s => s.Length > 0));
            var parts = full.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
            var result = new List<string>();
            string cur = "";
            for (int i = 0; i < parts.Count; i++)
            {
                string tok = parts[i] + (i < parts.Count - 1 ? "," : "");
                string cand = cur.Length == 0 ? tok : cur + " " + tok;
                if (cand.Length <= maxPerLine || cur.Length == 0) { cur = cand; continue; }
                if (result.Count >= maxLines - 1) { cur = cand; continue; }   // ostatnia linia — reszta bez zawijania
                result.Add(cur);
                cur = tok;
            }
            if (cur.Length > 0) result.Add(cur);
            while (result.Count < maxLines) result.Add("");
            return result.GetRange(0, maxLines);
        }

        // ------------------------------------------------------------------
        //  Akcesoria
        // ------------------------------------------------------------------

        private static void FillAccessories(ISheet sh, BbsGenerationContext ctx)
        {
            var tric = FindCellContaining(sh, "TRIC") ?? CellAt(sh, 41, ColA);
            var hys  = FindCellContaining(sh, "HYSTOOLS") ?? CellAt(sh, 42, ColI);
            tric.SetCellValue(ctx.BuildTricTrakLine());
            hys.SetCellValue(ctx.BuildHystoolsLine());
        }

        /// <summary>Kolejne arkusze: bez linii TRIC-TRAK / HYSTOOLS (ramki i etykiety zostają).</summary>
        private static void ClearAccessories(ISheet sh)
        {
            foreach (var token in new[] { "TRIC", "HYSTOOLS" })
            {
                var c = FindCellContaining(sh, token);
                if (c != null) c.SetCellType(CellType.Blank);
            }
        }

        private static ICell FindCellContaining(ISheet sh, string token, int firstRow = 34, int lastRow = 49)
        {
            for (int r = firstRow; r <= lastRow; r++)
            {
                var row = sh.GetRow(r);
                if (row == null) continue;
                for (int c = 0; c <= DataColLast; c++)
                {
                    var cell = row.GetCell(c);
                    if (cell == null || cell.CellType != CellType.String) continue;
                    if ((cell.StringCellValue ?? "").IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                        return cell;
                }
            }
            return null;
        }

        // ------------------------------------------------------------------
        //  Pomocnicze NPOI
        // ------------------------------------------------------------------

        /// <summary>CloneSheet w HSSF gubi czasem szerokości kolumn i scalenia — uzupełnienie ze wzorca.</summary>
        private static void CopyStructure(ISheet src, ISheet dst, int maxCol = 30)
        {
            dst.DefaultColumnWidth = src.DefaultColumnWidth;
            dst.DefaultRowHeight   = src.DefaultRowHeight;
            for (int c = 0; c < maxCol; c++) dst.SetColumnWidth(c, src.GetColumnWidth(c));
            var have = new HashSet<string>();
            for (int i = 0; i < dst.NumMergedRegions; i++) have.Add(dst.GetMergedRegion(i).FormatAsString());
            for (int i = 0; i < src.NumMergedRegions; i++)
            {
                var m = src.GetMergedRegion(i);
                if (!have.Contains(m.FormatAsString())) dst.AddMergedRegion(m);
            }
        }

        private static ICell Cell(IRow row, int col) => row.GetCell(col) ?? row.CreateCell(col);

        private static ICell CellAt(ISheet sh, int r, int c) => Cell(sh.GetRow(r) ?? sh.CreateRow(r), c);

        private static void SetString(ISheet sh, int r, int c, string v) => CellAt(sh, r, c).SetCellValue(v ?? "");

        private static string CellText(ISheet sh, int r, int c)
        {
            var cell = sh.GetRow(r)?.GetCell(c);
            if (cell == null) return null;
            switch (cell.CellType)
            {
                case CellType.String:  return cell.StringCellValue;
                case CellType.Numeric: return cell.NumericCellValue.ToString(CultureInfo.InvariantCulture);
                default: return null;
            }
        }

        /// <summary>„…-DR-…-ASD.dwg” → „…-BBS-….xls” (jak ASD); bez „-DR-” → nazwa rysunku + „-BBS.xls”.</summary>
        public static string SuggestOutputPath(string dwgPath)
        {
            if (string.IsNullOrWhiteSpace(dwgPath)) return "";
            string dir = Path.GetDirectoryName(dwgPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(dwgPath);
            if (string.IsNullOrWhiteSpace(name)) return "";
            name = Regex.Replace(name, @"\s*-\s*ASD\s*$", "").Trim();
            name = name.Contains("-DR-") ? name.Replace("-DR-", "-BBS-") : name + "-BBS";
            return Path.Combine(dir, name + ".xls");
        }
    }
}
