// K1 — nib zawinięty (POPRAWKI_WZORCA 29, docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md, krok K1 + poprawka recenzji 1) w BricsCAD V25.
// Uruchamiane skryptem planc-k1.scr (run-planc-k1.ps1). Komendy RCSMOKE_K1_*. Dostęp do wtyczki wyłącznie refleksją.
// Dane: GA Holbrook P02 (DXF, tylko odczyt), folder: zmienna RC_BEAM_GA (jak test belek).
//
// Najpierw przebieg bazowy z wtyczką HEAD (run-planc-k1.ps1 -Baseline -PluginDll <DLL HEAD>, RC_K1_TAG=head) — zrzut rozkładów
// po Prepare GA + Mesh (225) płyt 458, 461-462, 524, 526, 527, 322, 321, 525; potem właściwy (RC_K1_TAG=new) — porównanie:
//  - rozkłady dołu (B1 / B2), UB 03, B3, T IN NIB — identyczne z HEAD;
//  - różnice dozwolone TYLKO w rozkładach T1 / T2 i UB 01 / 02 przy krawędziach uskoku z wolnym końcem (nib zawinięty / w środku
//    krawędzi — AutoRebarEngine.NibFreeStepEdges); płyta bez takiej krawędzi — identyczna z HEAD;
//  - bezpiecznik: żaden rozkład T1 / T2 / UB 01 / 02 nie ma rozstawu < 100 mm;
//  - informacja: UB 02 / UB 01 (sztuki), metry prętów góry, masa (RC_BBS ReadBars) HEAD → nowy.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using Application = Bricscad.ApplicationServices.Application;

namespace BricsCadRc.BrxSmokeTests
{
    public class PlanCK1Tests
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string ResultsFile = "planc-k1-results.txt";
        private const string Prefix = "[RC K1 TEST]";
        private const string Title = "K1 — nib zawinięty (POPRAWKI_WZORCA 29): Holbrook z bazą HEAD";
        private const string Slab = "SD-PILED-RAFT";
        private const string Engine = "BricsCadRc.Core.AutoRebarEngine";
        private const string GaType = "BricsCadRc.Core.GaImportEngine";
        private const string NibDetType = "BricsCadRc.Core.NibDetector";
        private const string DistApp = "RC_BAR_BLOCK";

        private static readonly (string step, string key)[] Plates =
        {
            ("P458", "PLOT 458"), ("P461", "PLOT 461-462"), ("P524", "PLOT 524"), ("P526", "PLOT 526"),
            ("P527", "PLOT 527"), ("P322", "Plot 322"), ("P321", "Plot 321"), ("P525", "PLOT 525"),
        };

        private static readonly List<string> _results = new List<string>();
        private static readonly List<string> _executed = new List<string>();
        private static List<string> _expected = new List<string>();
        private static int _stepStart;

        private static string Tag => Environment.GetEnvironmentVariable("RC_K1_TAG") is string t && t.Length > 0 ? t : "new";
        private static string GaDir => Environment.GetEnvironmentVariable("RC_BEAM_GA") ?? "";

        [CommandMethod("RCSMOKE_K1_SETUP", CommandFlags.Modal)]
        public void Setup()
        {
            _results.Clear();
            _executed.Clear();
            _expected = new List<string> { "SETUP" };
            _expected.AddRange(Plates.Select(p => p.step));
            if (Tag == "head") ResultsFile = "planc-k1-base-results.txt";
            const string step = "SETUP";
            Run(step, db =>
            {
                Check(step, "wtyczka NaSmartRc wczytana (jedna kopia)", () =>
                {
                    var all = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "NaSmartRc").ToList();
                    if (all.Count != 1) throw new AssertFail("wczytane kopie NaSmartRc: " + all.Count);
                    return "Location=" + Loc(Plugin) + "; tag=" + Tag;
                });
                Check(step, "folder GA (RC_BEAM_GA) z DXF płyt", () =>
                {
                    foreach (var p in Plates) if (GaFile(p.key) == null) throw new AssertFail("brak DXF „" + p.key + "” w " + GaDir);
                    return GaDir;
                });
                Check(step, "czyszczenie przestrzeni modelu", () => "usunięto " + CleanModelSpace(db).ToString(Inv));
            });
        }

        [CommandMethod("RCSMOKE_K1_REPORT", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Report()
        {
            foreach (var st in _expected)
                if (!_executed.Contains(st))
                    Add("FAIL", st, "krok nie został wykonany", "skrypt nie dotarł do komendy kroku albo ją pominął");
            int pass = _results.Count(r => r.StartsWith("PASS", StringComparison.Ordinal));
            int fail = _results.Count(r => r.StartsWith("FAIL", StringComparison.Ordinal));
            int skip = _results.Count(r => r.StartsWith("SKIP", StringComparison.Ordinal));
            Flush(new[] { "", $"PODSUMOWANIE: PASS={pass} FAIL={fail} SKIP={skip}", "END" });
            Msg($"\n{Prefix} Raport: {Path.Combine(OutDir, ResultsFile)}  PASS={pass} FAIL={fail} SKIP={skip}\n");
        }

        [CommandMethod("RCSMOKE_K1_P458", CommandFlags.Modal)] public void P458() => Plate("P458");
        [CommandMethod("RCSMOKE_K1_P461", CommandFlags.Modal)] public void P461() => Plate("P461");
        [CommandMethod("RCSMOKE_K1_P524", CommandFlags.Modal)] public void P524() => Plate("P524");
        [CommandMethod("RCSMOKE_K1_P526", CommandFlags.Modal)] public void P526() => Plate("P526");
        [CommandMethod("RCSMOKE_K1_P527", CommandFlags.Modal)] public void P527() => Plate("P527");
        [CommandMethod("RCSMOKE_K1_P322", CommandFlags.Modal)] public void P322() => Plate("P322");
        [CommandMethod("RCSMOKE_K1_P321", CommandFlags.Modal)] public void P321() => Plate("P321");
        [CommandMethod("RCSMOKE_K1_P525", CommandFlags.Modal)] public void P525() => Plate("P525");

        /// <summary>Rozkład w zrzucie: tekst (kod, opis, liczba, rozstaw, kierunek, punkt wstawienia, długość, zasięg) + prostokąt.</summary>
        private sealed class Dist
        {
            public string Line, Code, Mark;
            public int Count; public double Spacing, Length, Span; public bool AlongX; public double X, Y;
            public bool IsUb12 => Code.Length == 2 && (Code == "B1" || Code == "B2") && Mark.EndsWith(" UB") && (Mark.Contains("-01") || Mark.Contains("-02"));
            public bool IsTop => (Code == "T1" || Code == "T2") && !Mark.Contains("T IN NIB");
            public bool Changeable => IsUb12 || IsTop;
            /// <summary>Prostokąt: wzdłuż prętów ± długość (UB może iść w obie strony od punktu), w poprzek od punktu na zasięg.</summary>
            public (double x0, double y0, double x1, double y1) Box
                => AlongX ? (X - Length, Y - 1, X + Length, Y + Span + 1) : (X - 1, Y - Length, X + Span + 1, Y + Length);
        }

        private static void Plate(string step)
        {
            string key = Plates.First(p => p.step == step).key;
            Run(step, db =>
            {
                List<Dist> now = null; List<(bool vertical, double c, double lo, double hi, bool freeLo, bool freeHi)> free = null;
                double massNow = double.NaN;
                Check(step, "Prepare GA " + key + " + Mesh (225) — zrzut rozkładów", () =>
                {
                    CleanModelSpace(db);
                    ImportGa(key);
                    var (b, t) = Views(db);
                    if (b.IsNull || t.IsNull) throw new AssertFail("brak obrysów rzutów po Prepare GA");
                    Mesh(b, t, 225);
                    now = ReadDists(db);
                    File.WriteAllLines(Path.Combine(OutDir, $"k1-{step}-{Tag}.txt"), now.Select(d => d.Line), new UTF8Encoding(false));
                    massNow = Mass(db);
                    File.WriteAllText(Path.Combine(OutDir, $"k1-{step}-{Tag}-mass.txt"), massNow.ToString("0.0", Inv));
                    if (Tag != "head")
                        // rzut dolny (UB 01/02) i górny (T1/T2) — krawędzie z wolnym końcem obu rzutów
                        free = new[] { b, t }.SelectMany(v => ((System.Collections.IEnumerable)Call(Engine, "NibFreeStepEdges", db, v)).Cast<ITuple>())
                            .Select(x => ((bool)x[0], (double)x[1], (double)x[2], (double)x[3], (bool)x[4], (bool)x[5])).ToList();
                    return $"rozkłady={now.Count}, masa={massNow:0.0} kg" + (free != null ? $", krawędzie uskoku z wolnym końcem: {free.Count} " +
                           string.Join(" ", free.Select(f => $"[{(f.vertical ? "x" : "y")}={f.c:0} {f.lo:0}..{f.hi:0} wolny {(f.freeLo ? "lo" : "")}{(f.freeHi ? "hi" : "")}]")) : "");
                });
                if (Tag == "head") return;
                Check(step, "bezpiecznik: rozkłady T1 / T2 / UB 01 / 02 bez rozstawu < 100 mm", () =>
                {
                    if (now == null) throw new AssertFail("brak zrzutu");
                    var bad = now.Where(d => d.Changeable && d.Count > 1 && d.Spacing < 100).ToList();
                    if (bad.Count > 0) throw new AssertFail(string.Join(" | ", bad.Select(d => d.Line)));
                    var ub = now.Where(d => d.IsUb12 && d.Count > 1).ToList();
                    return ub.Count == 0 ? "brak UB 01/02" : $"min. rozstaw UB 01/02 {ub.Min(d => d.Spacing):0.0} mm";
                });
                Check(step, "porównanie z HEAD: dół / UB 03 / B3 / T IN NIB identyczne; różnice tylko T1 / T2 / UB 01 / 02 przy krawędziach z wolnym końcem", () =>
                {
                    if (now == null) throw new AssertFail("brak zrzutu");
                    string hf = Path.Combine(OutDir, $"k1-{step}-head.txt");
                    if (!File.Exists(hf)) throw new AssertFail("brak zrzutu HEAD " + hf + " (uruchom run-planc-k1.ps1 -Baseline)");
                    var head = File.ReadAllLines(hf).Select(Parse).ToList();
                    var restNow = now.ToList();
                    var missing = new List<Dist>();
                    foreach (var h in head) { int i = restNow.FindIndex(n => n.Line == h.Line); if (i >= 0) restNow.RemoveAt(i); else missing.Add(h); }
                    var diffs = missing.Concat(restNow).ToList();
                    var notAllowed = diffs.Where(d => !d.Changeable || !NearFree(d, free)).ToList();
                    if (notAllowed.Count > 0)
                        throw new AssertFail($"różnice poza krawędziami z wolnym końcem ({notAllowed.Count}): " +
                                             string.Join(" | ", notAllowed.Take(6).Select(d => (missing.Contains(d) ? "HEAD «" : "nowy «") + d.Line + "»")));
                    double mh = double.NaN;
                    string mf = Path.Combine(OutDir, $"k1-{step}-head-mass.txt");
                    if (File.Exists(mf)) mh = double.Parse(File.ReadAllText(mf).Trim(), Inv);
                    string Ub(IEnumerable<Dist> l, string nr) => l.Where(d => d.IsUb12 && d.Mark.Contains("-" + nr)).Sum(d => d.Count).ToString(Inv);
                    double Top(IEnumerable<Dist> l) => l.Where(d => d.IsTop).Sum(d => d.Count * d.Length) / 1000.0;
                    string info = $"UB 01: {Ub(head, "01")} → {Ub(now, "01")}, UB 02: {Ub(head, "02")} → {Ub(now, "02")}, góra T1+T2: {Top(head):0} → {Top(now):0} m, " +
                                  $"masa (siatka + UB): {mh:0.0} → {massNow:0.0} kg ({(massNow - mh):+0.0;-0.0;0} kg)";
                    if (diffs.Count == 0) return "identyczne z HEAD (" + now.Count + " rozkładów); " + info;
                    if (free == null || free.Count == 0) throw new AssertFail("różnice przy płycie bez krawędzi z wolnym końcem");
                    return $"różnice dozwolone: HEAD {missing.Count} / nowy {restNow.Count} rozkładów (T1/T2/UB 01/02 przy wolnym końcu); " + info +
                           "; nowe: " + string.Join(" | ", restNow.Where(d => d.IsUb12).Select(d => $"{d.Count} {d.Mark} co {d.Spacing:0.0}"));
                });
            });
        }

        /// <summary>Rozkład zmieniony leży przy krawędzi uskoku z wolnym końcem: UB — prostokąt rozkładu przecina pas ± 400 wzdłuż krawędzi;
        /// T1 / T2 — pas prętów prostopadłych do krawędzi, zakres rozkładu zachodzi na zakres krawędzi.</summary>
        private static bool NearFree(Dist d, List<(bool vertical, double c, double lo, double hi, bool freeLo, bool freeHi)> free)
        {
            if (free == null) return false;
            // T1 / T2: cały pas prętów dochodzący do krawędzi (rozkłady pasa wzdłuż prętów — także daleko od krawędzi):
            // pręty prostopadłe do krawędzi, zakres rozkładu w osi rozkładu zachodzi na zakres krawędzi
            if (d.IsTop)
                return free.Any(f => f.vertical == d.AlongX
                                     && Math.Min(f.hi + 1, (d.AlongX ? d.Y : d.X) + d.Span + 1) > Math.Max(f.lo - 1, (d.AlongX ? d.Y : d.X) - 1));
            var b = d.Box;
            foreach (var f in free)
            {
                var e = f.vertical ? (x0: f.c - 400, y0: f.lo - 1, x1: f.c + 400, y1: f.hi + 1)
                                   : (x0: f.lo - 1, y0: f.c - 400, x1: f.hi + 1, y1: f.c + 400);
                if (Math.Min(b.x1, e.x1) > Math.Max(b.x0, e.x0) && Math.Min(b.y1, e.y1) > Math.Max(b.y0, e.y0)) return true;
            }
            return false;
        }

        private static List<Dist> ReadDists(Database db)
        {
            var res = new List<Dist>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    TypedValue[] v;
                    using (var rb = br.GetXDataForApplication(DistApp)) v = rb?.AsArray();
                    if (v == null || v.Length < 10) continue;
                    string S(int i) => Convert.ToString(v[i].Value, Inv);
                    double Dd(int i) => Convert.ToDouble(v[i].Value, Inv);
                    string line = string.Join(";", S(2), S(1), S(3), Dd(5).ToString("0.0", Inv), S(6),
                                              br.Position.X.ToString("0.0", Inv), br.Position.Y.ToString("0.0", Inv), Dd(8).ToString("0.0", Inv), Dd(9).ToString("0.0", Inv));
                    res.Add(Parse(line));
                }
                tr.Commit();
            }
            return res.OrderBy(d => d.Line, StringComparer.Ordinal).ToList();
        }

        private static Dist Parse(string line)
        {
            var p = line.Split(';');
            return new Dist
            {
                Line = line, Code = p[0], Mark = p[1], Count = int.Parse(p[2], Inv), Spacing = double.Parse(p[3], Inv), AlongX = p[4] != "Y",
                X = double.Parse(p[5], Inv), Y = double.Parse(p[6], Inv), Length = double.Parse(p[7], Inv), Span = double.Parse(p[8], Inv),
            };
        }

        /// <summary>Masa wszystkich wierszy RC_BBS (ReadBars) [kg].</summary>
        private static double Mass(Database db)
        {
            var r = Call("BricsCadRc.Core.BbsDrawingReader", "ReadBars", db);
            return ((System.Collections.IEnumerable)Prop(r, "Rows")).Cast<object>().Sum(x => Convert.ToDouble(Prop(x, "MassKg"), Inv));
        }

        // ── pomocnicze (jak PlanCBeamTests) ──────────────────────────────

        private static string GaFile(string key)
        {
            if (string.IsNullOrEmpty(GaDir) || !Directory.Exists(GaDir)) return null;
            return Directory.GetFiles(GaDir, "*.dxf").FirstOrDefault(f => Path.GetFileName(f).IndexOf("- " + key + ".dxf", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void ImportGa(string key)
        {
            string path = GaFile(key) ?? throw new AssertFail("brak DXF " + key);
            var warnings = new List<string>();
            var src = (IDisposable)Call(GaType, "ReadSource", path, warnings);
            try
            {
                var plots = ((System.Collections.IEnumerable)Prop(src, "Plots")).Cast<object>().ToList();
                string want = key.Replace("Plot", "PLOT");
                var plot = plots.FirstOrDefault(p => ((string)Prop(p, "Label")).StartsWith(want, StringComparison.OrdinalIgnoreCase)) ?? plots[0];
                Call(GaType, "Import", Application.DocumentManager.MdiActiveDocument, src, plot, warnings);
                Msg($"\n{Prefix} Import {Prop(plot, "Label")}: " + string.Join(" | ", warnings) + "\n");
            }
            finally { src.Dispose(); }
        }

        private static void Mesh(ObjectId b, ObjectId t, int thk)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            Call(Engine, "GenerateLayer", doc, b, "rebar_bottom", "X", "B1", 10, 200.0, 40.0, true);
            Call(Engine, "GenerateLayer", doc, b, "rebar_bottom", "Y", "B2", 10, 200.0, 40.0, true);
            Call(Engine, "GenerateLayer", doc, t, "rebar_top", "X", "T1", 12, 200.0, 40.0, true);
            Call(Engine, "GenerateLayer", doc, t, "rebar_top", "Y", "T2", 12, 200.0, 40.0, true);
            Call(Engine, "GenerateUBLayer", doc, b, "rebar_bottom", "B1", thk, "X", 200.0, 40.0, true);
            Call(Engine, "GenerateUBLayer", doc, b, "rebar_bottom", "B2", thk, "Y", 200.0, 40.0, true);
            if (Call(NibDetType, "Detect", doc.Database, b) != null)
                Call(Engine, "GenerateNibUBLayer", doc, b, "rebar_bottom", true);
            double clr = (double)Plugin.GetType(Engine, true).GetField("RepresentativeClearance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(null);
            Call(Engine, "ResolveRepresentativeCollisions", doc, b, clr);
            Call(Engine, "ResolveRepresentativeCollisions", doc, t, clr);
            Call(Engine, "GenerateB3Corners", doc, b);
        }

        private static (ObjectId b, ObjectId t) Views(Database db)
        {
            ObjectId Of(string v)
            {
                var best = ObjectId.Null; double area = -1;
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl) || pl.Layer != Slab || !pl.Closed) continue;
                        TypedValue[] x;
                        using (var rb = pl.GetXDataForApplication("RC_GA")) x = rb?.AsArray();
                        if (x == null || x.Length <= 2 || Convert.ToString(x[2].Value, Inv) != v) continue;
                        if (Math.Abs(pl.Area) > area) { area = Math.Abs(pl.Area); best = id; }
                    }
                    tr.Commit();
                }
                return best;
            }
            return (Of("B"), Of("T"));
        }

        private static int CleanModelSpace(Database db)
        {
            int n = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    tr.GetObject(id, OpenMode.ForWrite).Erase();
                    n++;
                }
                tr.Commit();
            }
            return n;
        }

        private sealed class AssertFail : System.Exception
        {
            public AssertFail(string m) : base(m) { }
        }

        private static Assembly _plugin;

        private static Assembly Plugin
        {
            get
            {
                if (_plugin != null) return _plugin;
                var all = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "NaSmartRc").ToList();
                if (all.Count == 0) throw new InvalidOperationException("Wtyczka NaSmartRc nie jest wczytana (NETLOAD NaSmartRc.dll).");
                _plugin = all[0];
                return _plugin;
            }
        }

        private static string Loc(Assembly a)
        {
            try { return (a.Location ?? "").Replace('/', '\\'); }
            catch (System.Exception ex) { return "? (" + ex.Message + ")"; }
        }

        private static object Call(string typeName, string method, params object[] args)
        {
            var t = Plugin.GetType(typeName, true);
            var cands = t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                         .Where(m => m.Name == method && m.GetParameters().Length == args.Length).ToList();
            if (cands.Count != 1)
                throw new InvalidOperationException($"{typeName}.{method}/{args.Length}: znaleziono {cands.Count} przeciążeń");
            try { return cands[0].Invoke(null, args); }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw;
            }
        }

        private static object Prop(object o, string name)
        {
            var pi = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi != null) return pi.GetValue(o);
            var fi = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null) return fi.GetValue(o);
            throw new InvalidOperationException(o.GetType().FullName + ": brak pola/właściwości " + name);
        }

        private static string OutDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        private static Database ActiveDb() => Application.DocumentManager.MdiActiveDocument.Database;

        private static void Msg(string text)
        {
            try { Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(text); }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("PlanCK1Tests.Msg: " + ex); }
        }

        private static void Run(string step, Action<Database> body)
        {
            _stepStart = _results.Count;
            if (!_executed.Contains(step)) _executed.Add(step);
            try { body(ActiveDb()); }
            catch (System.Exception ex) { Add("FAIL", step, "nieoczekiwany wyjątek kroku", ex.ToString()); }
            var lines = _results.Skip(_stepStart).ToList();
            Msg("\n" + Prefix + " " + step + ":\n" + string.Join("\n", lines) + "\n");
            Flush(null);
        }

        private static void Check(string step, string name, Func<string> body)
        {
            try { Add("PASS", step, name, body() ?? ""); }
            catch (AssertFail af) { Add("FAIL", step, name, af.Message); }
            catch (System.Exception ex) { Add("FAIL", step, name, "wyjątek: " + ex); }
        }

        private static void Add(string status, string step, string name, string details)
            => _results.Add($"{status} | {step} | {name} | {(details ?? "").Replace("\r", " ").Replace("\n", " ")}");

        private static void Flush(IEnumerable<string> tail)
        {
            try
            {
                var lines = new List<string>
                {
                    Title + ", BricsCAD (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")",
                    "Wtyczka: " + SafePluginLoc(),
                    "Kroki wykonane: " + string.Join(", ", _executed),
                    "",
                };
                lines.AddRange(_results);
                if (tail != null) lines.AddRange(tail);
                File.WriteAllLines(Path.Combine(OutDir, ResultsFile), lines, new UTF8Encoding(false));
            }
            catch (System.Exception ex) { Msg("\n" + Prefix + " Błąd zapisu wyników: " + ex.Message + "\n"); }
        }

        private static string SafePluginLoc()
        {
            try { return Loc(Plugin); }
            catch (System.Exception ex) { return "BRAK (" + ex.Message + ")"; }
        }
    }
}
