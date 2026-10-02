using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Minimalny czytnik .xlsx (zip + XML), bez zewnętrznych bibliotek.
    /// Czyta wartości komórek (dla formuł — wartość zapisaną w pliku), teksty inline i ze sharedStrings.
    /// Plik może być otwarty w Excelu (FileShare.ReadWrite).
    /// </summary>
    public static class XlsxReader
    {
        private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace Rel  = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace Pkg  = "http://schemas.openxmlformats.org/package/2006/relationships";

        /// <summary>Nazwy arkuszy w kolejności skoroszytu.</summary>
        public static List<string> SheetNames(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            return ReadSheets(zip).Select(s => s.name).ToList();
        }

        /// <summary>
        /// Wiersze arkusza: numer wiersza (1-based) → (numer kolumny 1-based → tekst).
        /// Liczby w InvariantCulture. Arkusz wg nazwy (bez rozróżniania wielkości liter); null = brak.
        /// </summary>
        public static SortedDictionary<int, Dictionary<int, string>> ReadSheet(string path, string sheetName)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var zip = new ZipArchive(fs, ZipArchiveMode.Read);
            var sheet = ReadSheets(zip).FirstOrDefault(s => string.Equals(s.name, sheetName, StringComparison.OrdinalIgnoreCase));
            if (sheet.target == null) return null;
            var entry = zip.GetEntry(sheet.target);
            if (entry == null) return null;

            var shared = ReadSharedStrings(zip);
            var rows = new SortedDictionary<int, Dictionary<int, string>>();
            XDocument doc;
            using (var s = entry.Open()) doc = XDocument.Load(s);
            int autoRow = 0;
            foreach (var row in doc.Descendants(Main + "row"))
            {
                int r = int.TryParse((string)row.Attribute("r"), out int rr) ? rr : autoRow + 1;
                autoRow = r;
                var cells = new Dictionary<int, string>();
                int autoCol = 0;
                foreach (var c in row.Elements(Main + "c"))
                {
                    string refStr = (string)c.Attribute("r");
                    int col = refStr != null ? ColumnIndex(refStr) : autoCol + 1;
                    autoCol = col;
                    string t = (string)c.Attribute("t") ?? "n";
                    string val;
                    if (t == "inlineStr")
                        val = string.Concat(c.Descendants(Main + "t").Select(x => x.Value));
                    else
                    {
                        string v = c.Element(Main + "v")?.Value;
                        if (v == null) continue;
                        if (t == "s")
                            val = int.TryParse(v, out int si) && si >= 0 && si < shared.Count ? shared[si] : "";
                        else if (t == "b")
                            val = v == "1" ? "TRUE" : "FALSE";
                        else
                            val = v;   // n, str, e
                    }
                    if (!string.IsNullOrEmpty(val)) cells[col] = val;
                }
                if (cells.Count > 0) rows[r] = cells;
            }
            return rows;
        }

        private static List<(string name, string target)> ReadSheets(ZipArchive zip)
        {
            var result = new List<(string, string)>();
            var wbEntry = zip.GetEntry("xl/workbook.xml");
            if (wbEntry == null) return result;
            XDocument wb, rels = null;
            using (var s = wbEntry.Open()) wb = XDocument.Load(s);
            var relEntry = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (relEntry != null) using (var s = relEntry.Open()) rels = XDocument.Load(s);
            var map = rels?.Root?.Elements(Pkg + "Relationship")
                          .ToDictionary(e => (string)e.Attribute("Id"), e => (string)e.Attribute("Target"))
                      ?? new Dictionary<string, string>();
            int i = 0;
            foreach (var sh in wb.Descendants(Main + "sheet"))
            {
                i++;
                string name = (string)sh.Attribute("name");
                string rid = (string)sh.Attribute(Rel + "id");
                string target = rid != null && map.TryGetValue(rid, out var t) ? t : $"worksheets/sheet{i}.xml";
                target = target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target;
                result.Add((name, target));
            }
            return result;
        }

        private static List<string> ReadSharedStrings(ZipArchive zip)
        {
            var list = new List<string>();
            var e = zip.GetEntry("xl/sharedStrings.xml");
            if (e == null) return list;
            XDocument doc;
            using (var s = e.Open()) doc = XDocument.Load(s);
            foreach (var si in doc.Root.Elements(Main + "si"))
                list.Add(string.Concat(si.Descendants(Main + "t").Select(x => x.Value)));
            return list;
        }

        /// <summary>"AB12" → 28.</summary>
        public static int ColumnIndex(string cellRef)
        {
            int col = 0;
            foreach (char ch in cellRef)
            {
                if (ch >= 'A' && ch <= 'Z') col = col * 26 + (ch - 'A' + 1);
                else if (ch >= 'a' && ch <= 'z') col = col * 26 + (ch - 'a' + 1);
                else break;
            }
            return col;
        }
    }
}
