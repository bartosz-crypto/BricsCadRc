using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Raport przebicia (report_punching.xlsx, arkusz „Punching EC2”): płyty PLOT → pale z wynikami.
    /// Kolumny rozpoznawane po nagłówkach (wiersz z „Pile”), nie po stałych numerach.
    /// </summary>
    public static class PunchingReport
    {
        public const string SheetName = "Punching EC2";

        public sealed class PileResult
        {
            public string Plot;          // "PLOT 6-8"
            public string PileId;        // "P617"
            public string Node;          // "617"
            public string Type;          // Internal / Edge / Corner / Reentrant
            public double Edge;          // mm (NaN dla wewnętrznych)
            public double Util;          // %
            public double T1, T2;        // mm²/m
            public string Reinf;         // "ADD H16@100" / "not required"
            public string Status;        // OK / FAIL
            public string Source;        // Punch. / Bend. / ...
            public int    Row;
        }

        public sealed class Plot
        {
            public string Label;         // "PLOT 6-8"
            public int    DeclaredPiles; // z nagłówka „(19 piles)”
            public List<PileResult> Piles = new List<PileResult>();
        }

        private static readonly Regex PlotRx    = new Regex(@"^\s*(PLOT\b.*?)\s*(?:\(\s*(\d+)\s*piles?\s*\))?\s*$", RegexOptions.IgnoreCase);
        private static readonly Regex SectionRx = new Regex(@"^\s*(INTERNAL|EDGE|CORNER|RE-?ENTRANT)\s*\(\s*\d+\s*\)", RegexOptions.IgnoreCase);

        /// <summary>Czyta raport. Arkusz „Punching EC2”, a gdy brak — pierwszy z nagłówkiem Pile + Reinf.</summary>
        public static List<Plot> Read(string path, List<string> warnings)
        {
            var rows = XlsxReader.ReadSheet(path, SheetName);
            if (rows == null)
            {
                foreach (var name in XlsxReader.SheetNames(path))
                {
                    var r = XlsxReader.ReadSheet(path, name);
                    if (r != null && r.Values.Any(c => c.Values.Any(v => Head(v) == "pile")
                                                   && c.Values.Any(v => Head(v).StartsWith("reinf"))))
                    { rows = r; warnings.Add($"Brak arkusza '{SheetName}' — czytam '{name}'."); break; }
                }
            }
            if (rows == null) throw new InvalidOperationException($"W pliku nie ma arkusza '{SheetName}' z tabelą pali.");

            var plots = new List<Plot>();
            Plot cur = null;
            string section = "Internal";
            Dictionary<string, int> col = null;

            foreach (var kv in rows)
            {
                var c = kv.Value;
                string a = Cell(c, 1);

                var pm = a != null ? PlotRx.Match(a) : Match.Empty;
                if (pm.Success && a.TrimStart().StartsWith("PLOT", StringComparison.OrdinalIgnoreCase) && c.Count == 1)
                {
                    cur = new Plot { Label = pm.Groups[1].Value.Trim(),
                                     DeclaredPiles = int.TryParse(pm.Groups[2].Value, out int n) ? n : 0 };
                    plots.Add(cur);
                    continue;
                }
                var sm = a != null ? SectionRx.Match(a) : Match.Empty;
                if (sm.Success)
                {
                    string s = sm.Groups[1].Value.ToUpperInvariant();
                    section = s == "INTERNAL" ? "Internal" : s == "EDGE" ? "Edge" : s == "CORNER" ? "Corner" : "Reentrant";
                    continue;
                }
                // Nagłówek tabeli: komórka „Pile”
                if (c.Values.Any(v => Head(v) == "pile") && c.Values.Any(v => Head(v).StartsWith("reinf")))
                {
                    col = new Dictionary<string, int>();
                    foreach (var cell in c)
                    {
                        string h = Head(cell.Value);
                        if (!col.ContainsKey(h)) col[h] = cell.Key;
                    }
                    continue;
                }
                if (col == null || cur == null) continue;

                string pile = Get(c, col, "pile");
                if (string.IsNullOrWhiteSpace(pile) || pile.Equals("Pile", StringComparison.OrdinalIgnoreCase)) continue;

                var pr = new PileResult
                {
                    Plot   = cur.Label,
                    PileId = pile.Trim(),
                    Node   = Get(c, col, "node"),
                    Type   = Normalize(Get(c, col, "type")) ?? section,
                    Edge   = Num(Get(c, col, "edge")),
                    Util   = Num(Get(c, col, "util")),
                    T1     = Num(Get(c, col, "t1")),
                    T2     = Num(Get(c, col, "t2")),
                    Reinf  = (Get(c, col, "reinf.") ?? Get(c, col, "reinf") ?? "").Trim(),
                    Status = (Get(c, col, "status") ?? "").Trim(),
                    Source = (Get(c, col, "source") ?? "").Trim(),
                    Row    = kv.Key,
                };
                // Util zapisany jako ułamek (0.95) zamiast % — przelicz
                if (!double.IsNaN(pr.Util) && pr.Util > 0 && pr.Util <= 2.0) pr.Util *= 100.0;
                cur.Piles.Add(pr);
            }

            foreach (var p in plots)
                if (p.DeclaredPiles > 0 && p.DeclaredPiles != p.Piles.Count)
                    warnings.Add($"{p.Label}: nagłówek mówi {p.DeclaredPiles} pali, w tabeli {p.Piles.Count}.");
            return plots;
        }

        private static string Normalize(string type)
        {
            if (string.IsNullOrWhiteSpace(type)) return null;
            string t = type.Trim().ToLowerInvariant();
            if (t.StartsWith("int")) return "Internal";
            if (t.StartsWith("edge")) return "Edge";
            if (t.StartsWith("corn")) return "Corner";
            if (t.StartsWith("re")) return "Reentrant";
            return type.Trim();
        }

        /// <summary>Pierwsza linia nagłówka, małymi literami ("Edge\n(mm)" → "edge").</summary>
        private static string Head(string v)
            => (v ?? "").Split('\n')[0].Trim().ToLowerInvariant();

        private static string Cell(Dictionary<int, string> c, int i) => c.TryGetValue(i, out var v) ? v : null;

        private static string Get(Dictionary<int, string> c, Dictionary<string, int> col, string key)
            => col.TryGetValue(key, out int i) ? Cell(c, i) : null;

        private static double Num(string s)
            => s != null && double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : double.NaN;
    }
}
