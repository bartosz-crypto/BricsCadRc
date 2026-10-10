// BEAM — testy dymne belek / pogrubień (docs/PLAN_BELKI_2026-10-10.md, E1 + E3) w BricsCAD V25.
// Uruchamiane skryptem planc-beam.scr (run-planc-beam.ps1). Komendy RCSMOKE_BEAM_*. Dostęp do wtyczki wyłącznie refleksją.
// Dane: GA Holbrook P02 (DXF, tylko odczyt — GaImportEngine.ReadSource czyta do bazy pomocniczej), folder: zmienna RC_BEAM_GA.
//
// D526  Prepare GA 526 (bez stref) → zrzut DrawingDump (pełny, z warstwami) = zrzut wtyczki HEAD (bit w bit).
// D319  Prepare GA 319 + Mesh (225) → zrzut bez warstwy RC-BEAM = zrzut HEAD (strefy belek nie zmieniają siatki — E2 wstrzymany;
//       obrys 319 ma skosy — AutoRebar siatki go odrzuca, zostaje B3 ADD). D528 / D539 — to samo na płytach osiowych z belką.
//       Kroki D* działają też z wtyczką HEAD (przebieg -Baseline, RC_BEAM_DUMP_TAG=head) — porównanie w przebiegu „new”.
// G319  strefa + H = 450 (oba rzuty, [6] = obrys rzutu), generowanie: lewa noga ze skosem — „detal ręczny”, góra + prawa: strzemiona 38 / 12.
// G539  H = 300, L + L; format Blok (szablony = pręty-bloki): strzemiona 13 / 46 / 24, dół 4 rozkłady, góra 4.
// G528  H = 375, prosta: strzemiona 52, dół 2 × 4 H16 L5500, góra 2 × 4 H16 (seria 101+), numery ≥ 04 bez 01–03, opisy, znaczniki.
// G528R ponowne generowanie (osobna komenda) — te same liczby, bez duplikatów, szablony ponownie użyte.
// G528U po _.UNDO 1 — rozkłady sprzed ponownego generowania; G528D po _.REDO — rozkłady ponownego generowania.
// G528C po _.COPY rzutu dolnego (+40000 Y): strefa kopii [6] → obrys kopii, znaczniki rozkładów kopii → strefa kopii;
//       ponowne generowanie oryginału — rozkłady kopii zostają.
// G528M po _.MIRROR rzutu dolnego: znaczniki odbicia → strefa odbicia.
// G528A po _-ARRAY rzutu dolnego (1 × 2, +100000 X) i G528P po COPYCLIP + PASTECLIP (200000, 0): znaczniki kopii → strefa kopii,
//       [6] strefy kopii → obrys kopii (CloneRemap, plan belek 1c).
// G528GA ponowne Prepare GA 528 — strefy zastąpione (2), pręty belek starych stref usunięte.
// G528UB Mesh (225) + belka: strzemiona przesunięte względem UB 03 (≥ 90 mm od każdego UB 03).
// M528  belka → Mesh: Prepare GA 528, pręty belki, potem Mesh (225) — rozkłady siatki (bez RC-BEAM i BB / BT / BL) = zrzut HEAD
//       528mesh (numery pozycji i odwołania znormalizowane; przenumerowanie i zmiany opisów — informacja).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using Application = Bricscad.ApplicationServices.Application;

namespace BricsCadRc.BrxSmokeTests
{
    public class PlanCBeamTests
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string ResultsFile = "planc-beam-results.txt";
        private const string Prefix = "[RC BEAM TEST]";
        private const string Title = "BEAM — testy dymne belek / pogrubień (E1 + E3)";
        private const string Slab = "SD-PILED-RAFT";
        private const string Engine = "BricsCadRc.Core.AutoRebarEngine";
        private const string GaType = "BricsCadRc.Core.GaImportEngine";
        private const string NibDetType = "BricsCadRc.Core.NibDetector";
        private const string FormatType = "BricsCadRc.Core.BarFormat";
        private const string BeamLayer = "RC-BEAM";
        private const string BeamApp = "RC_BEAM";
        private const string TagApp = "RC_AUTOREBAR_BEAM";
        private const string DistApp = "RC_BAR_BLOCK";

        private static readonly List<string> StepsFull = new List<string>
        {
            "SETUP", "D526", "D319", "D528", "D539", "G319", "G539", "G528", "G528R", "G528U", "G528D", "G528C", "G528M", "G528A", "G528P", "G528GA", "G528UB", "M528",
        };
        private static readonly List<string> StepsBase = new List<string> { "SETUP", "D526", "D319", "D528", "D539" };
        private static List<string> _expected = StepsFull;

        private static readonly List<string> _results = new List<string>();
        private static readonly List<string> _executed = new List<string>();
        private static int _stepStart;

        // stan
        private static HashSet<long> _gen1 = new HashSet<long>(), _gen2 = new HashSet<long>();
        private static ObjectId _b528, _t528, _z528;

        private static string Tag => Environment.GetEnvironmentVariable("RC_BEAM_DUMP_TAG") is string t && t.Length > 0 ? t : "new";
        private static string GaDir => Environment.GetEnvironmentVariable("RC_BEAM_GA") ?? "";

        // ================================================================
        // SETUP / REPORT
        // ================================================================

        [CommandMethod("RCSMOKE_BEAM_SETUP", CommandFlags.Modal)]
        public void Setup()
        {
            _results.Clear();
            _executed.Clear();
            _expected = Tag == "head" ? StepsBase : StepsFull;
            if (Tag == "head") ResultsFile = "planc-beam-base-results.txt";
            const string step = "SETUP";
            Run(step, db =>
            {
                Check(step, "wtyczka NaSmartRc wczytana (jedna kopia)", () =>
                {
                    var all = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "NaSmartRc").ToList();
                    if (all.Count != 1) throw new AssertFail("wczytane kopie NaSmartRc: " + all.Count);
                    return "Location=" + Loc(Plugin) + "; tag=" + Tag;
                });
                Check(step, "folder GA (RC_BEAM_GA) z DXF 319 / 526 / 528-529 / 539", () =>
                {
                    foreach (var k in new[] { "Plot 319", "PLOT 526", "PLOT 528-529", "PLOT 539" })
                        if (GaFile(k) == null) throw new AssertFail("brak DXF „" + k + "” w " + GaDir);
                    return GaDir;
                });
                Check(step, "czyszczenie przestrzeni modelu", () => "usunięto " + CleanModelSpace(db).ToString(Inv));
            });
        }

        [CommandMethod("RCSMOKE_BEAM_REPORT", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
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

        // ================================================================
        // D — zrzuty porównywane z wtyczką HEAD (bez belek)
        // ================================================================

        [CommandMethod("RCSMOKE_BEAM_D526", CommandFlags.Modal)]
        public void D526()
        {
            const string step = "D526";
            Run(step, db => Check(step, "Prepare GA 526 (bez stref) — zrzut pełny = HEAD", () =>
            {
                CleanModelSpace(db);
                ImportGa("PLOT 526");
                var lines = RegressionDump.DrawingDump.Run(db, false, null, true);
                return CompareDump("526", lines);
            }));
        }

        [CommandMethod("RCSMOKE_BEAM_D319", CommandFlags.Modal)]
        public void D319()
        {
            const string step = "D319";
            Run(step, db => Check(step, "Prepare GA 319 + Mesh (225) — zrzut bez RC-BEAM = HEAD", () =>
            {
                CleanModelSpace(db);
                ImportGa("Plot 319");
                var (b, t) = Views(db);
                if (b.IsNull || t.IsNull) throw new AssertFail("brak obrysów rzutów po Prepare GA");
                Mesh(b, t, 225);
                var lines = RegressionDump.DrawingDump.Run(db, false,
                    e => !string.Equals(e.Layer, BeamLayer, StringComparison.OrdinalIgnoreCase), false);
                return CompareDump("319mesh", lines);
            }));
        }

        [CommandMethod("RCSMOKE_BEAM_D528", CommandFlags.Modal)]
        public void D528() => DumpMesh("D528", "PLOT 528-529", "528mesh");

        [CommandMethod("RCSMOKE_BEAM_D539", CommandFlags.Modal)]
        public void D539() => DumpMesh("D539", "PLOT 539", "539mesh");

        private static void DumpMesh(string step, string key, string name)
        {
            Run(step, db => Check(step, "Prepare GA " + key + " + Mesh (225) — zrzut bez RC-BEAM = HEAD", () =>
            {
                CleanModelSpace(db);
                ImportGa(key);
                var (b, t) = Views(db);
                if (b.IsNull || t.IsNull) throw new AssertFail("brak obrysów rzutów po Prepare GA");
                Mesh(b, t, 225);
                var lines = RegressionDump.DrawingDump.Run(db, false,
                    e => !string.Equals(e.Layer, BeamLayer, StringComparison.OrdinalIgnoreCase), false);
                int dists = lines.Count(l => l.Contains("RC_BAR_BLOCK[0]"));
                return CompareDump(name, lines) + $"; rozkłady w zrzucie: {dists}";
            }));
        }

        // ================================================================
        // G — Prepare GA + generowanie
        // ================================================================

        [CommandMethod("RCSMOKE_BEAM_G319", CommandFlags.Modal)]
        public void G319()
        {
            const string step = "G319";
            Run(step, db =>
            {
                ObjectId b = ObjectId.Null, t = ObjectId.Null;
                Check(step, "Prepare GA 319: strefa na obu rzutach, H = 450, [6] = obrys rzutu", () =>
                {
                    SetFormat("Polyline");   // format nowych prętów: Polilinia (sesja; plik barformat.txt bez zmian)
                    CleanModelSpace(db);
                    var r = ImportGa("Plot 319");
                    (b, t) = Views(db);
                    return ZoneChecks(db, r, b, t, 450);
                });
                Check(step, "generowanie: lewa noga (skos) — detal ręczny; góra + prawa — strzemiona 38 / 12, dół 3, góra 3", () =>
                {
                    var res = GenerateBeams(db, b, t);
                    var msgs = Messages(res);
                    if (!msgs.Any(m => m.Contains("detal ręczny"))) throw new AssertFail("brak komunikatu „detal ręczny”: " + string.Join(" | ", msgs));
                    if (!msgs.Any(m => m.Contains("E4: zamknięcie końców"))) throw new AssertFail("brak komunikatu E4");
                    var bl = Dists(db, "BL");
                    var counts = bl.Select(x => x.count).OrderBy(x => x).ToList();
                    return Seq("strzemiona (liczby)", counts, new[] { 12, 38 })
                         + "; " + Int("rozkłady BB", Dists(db, "BB").Count, 3)
                         + "; " + Int("rozkłady BT", Dists(db, "BT").Count, 3)
                         + "; " + NumberChecks(db);
                });
            });
        }

        [CommandMethod("RCSMOKE_BEAM_G539", CommandFlags.Modal)]
        public void G539()
        {
            const string step = "G539";
            Run(step, db =>
            {
                ObjectId b = ObjectId.Null, t = ObjectId.Null;
                Check(step, "Prepare GA 539: strefa na obu rzutach, H = 300", () =>
                {
                    CleanModelSpace(db);
                    var r = ImportGa("PLOT 539");
                    (b, t) = Views(db);
                    return ZoneChecks(db, r, b, t, 300);
                });
                Check(step, "format Blok: strzemiona 13 / 24 / 46, dół 4 rozkłady, góra 4, szablony = pręty-bloki, strzemię 51 A=200 B=520", () =>
                {
                    SetFormat("Block");
                    try { GenerateBeams(db, b, t); }
                    finally { SetFormat("Polyline"); }
                    var counts = Dists(db, "BL").Select(x => x.count).OrderBy(x => x).ToList();
                    var tpl = Templates(db);
                    var links = tpl.Where(x => x.shape == "51").ToList();
                    if (links.Count != 1) throw new AssertFail("szablony 51: " + links.Count);
                    if (Math.Abs(links[0].a - 200) > 0.5 || Math.Abs(links[0].b - 520) > 0.5)
                        throw new AssertFail($"51: A={links[0].a} B={links[0].b}");
                    if (tpl.Any(x => !x.isBlock)) throw new AssertFail("szablon polilinia w formacie Blok: " + string.Join(",", tpl.Where(x => !x.isBlock).Select(x => x.mark)));
                    return Seq("strzemiona", counts, new[] { 13, 24, 46 })
                         + "; " + Int("BB", Dists(db, "BB").Count, 4) + "; " + Int("BT", Dists(db, "BT").Count, 4)
                         + "; szablony=" + string.Join(",", tpl.Select(x => x.mark)) + "; " + NumberChecks(db);
                });
            });
        }

        [CommandMethod("RCSMOKE_BEAM_G528", CommandFlags.Modal)]
        public void G528()
        {
            const string step = "G528";
            Run(step, db =>
            {
                Check(step, "Prepare GA 528-529: strefa na obu rzutach, H = 375", () =>
                {
                    CleanModelSpace(db);
                    var r = ImportGa("PLOT 528-529");
                    (_b528, _t528) = Views(db);
                    _z528 = Zones(db).FirstOrDefault(z => XLinkOf(db, z, BeamApp, 6) == Hx(_b528));
                    return ZoneChecks(db, r, _b528, _t528, 375);
                });
                Check(step, "strzemiona 52 H10 co 200 (LINKS), dół 2 × 4 H16 L5500 (BEAM B), góra 2 × 4 H16 (BEAM T, 101+), znaczniki", () =>
                {
                    GenerateBeams(db, _b528, _t528);
                    var bl = Dists(db, "BL"); var bb = Dists(db, "BB"); var bt = Dists(db, "BT");
                    var sb = new StringBuilder();
                    sb.Append(Int("BL", bl.Count, 1)).Append("; ").Append(Int("strzemiona", bl[0].count, 52));
                    if (!bl[0].mark.EndsWith("-200 LINKS") || !bl[0].mark.StartsWith("H10-")) throw new AssertFail("opis strzemion: " + bl[0].mark);
                    sb.Append("; ").Append(Int("BB", bb.Count, 2)).Append("; ").Append(Int("BT", bt.Count, 2));
                    foreach (var x in bb.Concat(bt))
                    {
                        if (x.count != 4) throw new AssertFail("liczba prętów " + x.mark + ": " + x.count);
                        if (Math.Abs(x.length - 5500) > 0.5) throw new AssertFail("długość " + x.mark + ": " + x.length);
                    }
                    if (!bb.All(x => x.mark.EndsWith(" BEAM B") && x.mark.StartsWith("H16-") && x.mark.Split(' ')[0].Split('-').Length == 2))
                        throw new AssertFail("opisy dołu: " + string.Join(" | ", bb.Select(x => x.mark)));
                    if (!bt.All(x => x.mark.EndsWith(" BEAM T") && x.pos >= 101))
                        throw new AssertFail("opisy góry: " + string.Join(" | ", bt.Select(x => x.mark)));
                    var zt = Zones(db).FirstOrDefault(z => XLinkOf(db, z, BeamApp, 6) == Hx(_t528));
                    foreach (var x in bl.Concat(bb)) Eq("znacznik " + x.mark, XLinkOf(db, x.id, TagApp, 1), Hx(_z528));
                    foreach (var x in bt) Eq("znacznik " + x.mark, XLinkOf(db, x.id, TagApp, 1), Hx(zt));
                    foreach (var x in bl.Concat(bb).Concat(bt)) if (AnnotOf(db, x.id).IsNull) throw new AssertFail("brak opisu " + x.mark);
                    var l51 = Templates(db).Where(x => x.shape == "51").ToList();
                    if (l51.Count != 1 || Math.Abs(l51[0].a - 275) > 0.5 || Math.Abs(l51[0].b - 520) > 0.5)
                        throw new AssertFail("szablon 51: " + string.Join(",", l51.Select(x => $"{x.mark} A={x.a} B={x.b}")));
                    if (Templates(db).Any(x => x.isBlock)) throw new AssertFail("format Polilinia: szablon jako pręt-blok");
                    _gen1 = new HashSet<long>(AllBeamDists(db).Select(x => x.Handle.Value));
                    return sb.Append("; ").Append(NumberChecks(db)).Append("; opisy: ")
                             .Append(string.Join(" | ", bl.Concat(bb).Concat(bt).Select(x => x.count + " " + x.mark))).ToString();
                });
            });
        }

        [CommandMethod("RCSMOKE_BEAM_G528R", CommandFlags.Modal)]
        public void G528R()
        {
            const string step = "G528R";
            Run(step, db => Check(step, "ponowne generowanie: te same liczby, bez duplikatów, szablony ponownie użyte", () =>
            {
                int tpl0 = Templates(db).Count;
                GenerateBeams(db, _b528, _t528);
                int tpl1 = Templates(db).Count;
                var all = AllBeamDists(db);
                _gen2 = new HashSet<long>(all.Select(x => x.Handle.Value));
                if (_gen1.Overlaps(_gen2)) throw new AssertFail("stare rozkłady nie usunięte");
                return Int("rozkłady belki", all.Count, 5) + "; " + Int("szablony", tpl1, tpl0) + "; " + NumberChecks(db);
            }));
        }

        [CommandMethod("RCSMOKE_BEAM_G528U", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void G528U()
        {
            const string step = "G528U";
            Run(step, db => Check(step, "po _.UNDO 1: rozkłady pierwszego generowania wróciły, ponownego — nie ma", () =>
            {
                var now = new HashSet<long>(AllBeamDists(db).Select(x => x.Handle.Value));
                if (!now.SetEquals(_gen1)) throw new AssertFail($"po UNDO {now.Count} rozkładów, wspólnych z 1. generowaniem {now.Count(_gen1.Contains)} / {_gen1.Count}");
                return "OK (" + now.Count.ToString(Inv) + ")";
            }));
        }

        [CommandMethod("RCSMOKE_BEAM_G528D", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void G528D()
        {
            const string step = "G528D";
            Run(step, db => Check(step, "po _.REDO: rozkłady ponownego generowania", () =>
            {
                var now = new HashSet<long>(AllBeamDists(db).Select(x => x.Handle.Value));
                if (!now.SetEquals(_gen2)) throw new AssertFail($"po REDO {now.Count} rozkładów, wspólnych z 2. generowaniem {now.Count(_gen2.Contains)} / {_gen2.Count}");
                return "OK (" + now.Count.ToString(Inv) + ")";
            }));
        }

        [CommandMethod("RCSMOKE_BEAM_G528C", CommandFlags.Modal)]
        public void G528C()
        {
            const string step = "G528C";
            Run(step, db =>
            {
                ObjectId cz = ObjectId.Null; List<ObjectId> cd = null;
                Check(step, "COPY rzutu dolnego (+40000 Y): strefa kopii [6] → obrys kopii, znaczniki rozkładów kopii → strefa kopii", () =>
                {
                    var zv = Verts(db, _z528);
                    cz = Zones(db).FirstOrDefault(z => SameShifted(Verts(db, z), zv, new Vector2d(0, 40000)));
                    if (cz.IsNull) throw new AssertFail("brak strefy-kopii");
                    var bo = Verts(db, _b528);
                    var cb = Ms(db, e => e is Polyline pl && pl.Layer == Slab && SameShifted(Verts(db, e.ObjectId), bo, new Vector2d(0, 40000))).FirstOrDefault();
                    if (cb.IsNull) throw new AssertFail("brak obrysu-kopii");
                    Eq("strefa kopii [6]", XLinkOf(db, cz, BeamApp, 6), Hx(cb));
                    cd = AllBeamDists(db).Where(id => PosOf(db, id).Y > 30000).ToList();
                    if (cd.Count != 3) throw new AssertFail("rozkłady-kopie: " + cd.Count);
                    foreach (var id in cd) Eq("znacznik kopii " + id.Handle, XLinkOf(db, id, TagApp, 1), Hx(cz));
                    return "kopie=" + cd.Count.ToString(Inv);
                });
                Check(step, "ponowne generowanie ORYGINAŁU — rozkłady kopii zostają", () =>
                {
                    GenerateBeams(db, _b528, _t528);
                    int lost = (cd ?? new List<ObjectId>()).Count(id => id.IsErased);
                    if (lost > 0) throw new AssertFail("usunięto rozkłady kopii: " + lost);
                    return Int("rozkłady belek razem", AllBeamDists(db).Count, 8);
                });
            });
        }

        [CommandMethod("RCSMOKE_BEAM_G528M", CommandFlags.Modal)]
        public void G528M()
        {
            const string step = "G528M";
            Run(step, db => Check(step, "MIRROR rzutu dolnego (oś x = −20000): znaczniki odbicia → strefa odbicia, [6] → obrys odbicia", () =>
            {
                var mz = Zones(db).FirstOrDefault(z => Verts(db, z).All(p => p.X < -20000));
                if (mz.IsNull) throw new AssertFail("brak strefy-odbicia");
                var mb = Ms(db, e => e is Polyline pl && pl.Layer == Slab && pl.Closed && Verts(db, e.ObjectId).All(p => p.X < -20000))
                         .OrderByDescending(id => Area(db, id)).FirstOrDefault();
                Eq("strefa odbicia [6]", XLinkOf(db, mz, BeamApp, 6), Hx(mb));
                var md = AllBeamDists(db).Where(id => PosOf(db, id).X < -20000).ToList();
                if (md.Count != 3) throw new AssertFail("rozkłady-odbicia: " + md.Count);
                foreach (var id in md) Eq("znacznik odbicia " + id.Handle, XLinkOf(db, id, TagApp, 1), Hx(mz));
                return "odbicia=" + md.Count.ToString(Inv);
            }));
        }

        [CommandMethod("RCSMOKE_BEAM_G528A", CommandFlags.Modal)]
        public void G528A()
        {
            const string step = "G528A";
            Run(step, db => Check(step, "_-ARRAY rzutu dolnego (1 × 2, +100000 X): znaczniki kopii tablicy → strefa kopii, [6] → obrys kopii",
                () => CloneRegionChecks(db, x => x > 90000 && x < 150000, "tablicy")));
        }

        [CommandMethod("RCSMOKE_BEAM_G528P", CommandFlags.Modal)]
        public void G528P()
        {
            const string step = "G528P";
            Run(step, db => Check(step, "COPYCLIP + PASTECLIP rzutu dolnego (ten sam rysunek = COPY): znaczniki wklejonych → strefa wklejona, [6] → obrys wklejony",
                () => CloneRegionChecks(db, x => x > 150000, "wklejenia")));
        }

        /// <summary>Kopia rzutu dolnego 528 w pasie X (ARRAY / PASTECLIP): jedna strefa, [6] → obrys kopii, 3 rozkłady belki → strefa kopii.</summary>
        private static string CloneRegionChecks(Database db, Func<double, bool> inX, string what)
        {
            var zs = Zones(db).Where(z => Verts(db, z).All(p => inX(p.X))).ToList();
            if (zs.Count != 1) throw new AssertFail($"strefy {what}: {zs.Count} (oczekiwano 1)");
            var ob = Ms(db, e => e is Polyline pl && pl.Layer == Slab && pl.Closed && Verts(db, e.ObjectId).All(p => inX(p.X)))
                     .OrderByDescending(id => Area(db, id)).FirstOrDefault();
            if (ob.IsNull) throw new AssertFail("brak obrysu " + what);
            Eq("strefa " + what + " [6]", XLinkOf(db, zs[0], BeamApp, 6), Hx(ob));
            var ds = AllBeamDists(db).Where(id => inX(PosOf(db, id).X)).ToList();
            if (ds.Count != 3) throw new AssertFail($"rozkłady {what}: {ds.Count}");
            foreach (var id in ds) Eq("znacznik " + what + " " + id.Handle, XLinkOf(db, id, TagApp, 1), Hx(zs[0]));
            return "kopie=" + ds.Count.ToString(Inv);
        }

        [CommandMethod("RCSMOKE_BEAM_G528GA", CommandFlags.Modal)]
        public void G528GA()
        {
            const string step = "G528GA";
            Run(step, db => Check(step, "ponowne Prepare GA 528: strefy zastąpione (2), pręty belek starych stref usunięte", () =>
            {
                ImportGa("PLOT 528-529");
                return Int("strefy", Zones(db).Count, 2) + "; " + Int("rozkłady belek", AllBeamDists(db).Count, 0);
            }));
        }

        [CommandMethod("RCSMOKE_BEAM_G528UB", CommandFlags.Modal)]
        public void G528UB()
        {
            const string step = "G528UB";
            Run(step, db => Check(step, "Mesh (225) + belka: strzemiona przesunięte względem UB 03 (≥ 90 mm), rozstaw ≤ 200", () =>
            {
                CleanModelSpace(db);
                ImportGa("PLOT 528-529");
                var (b, t) = Views(db);
                Mesh(b, t, 225);
                var ub = new List<double>();
                foreach (var id in Ms(db, e => e is BlockReference && Raw(e, DistApp) is TypedValue[] v && v.Length > 2
                                               && Convert.ToString(v[1].Value, Inv).Contains("-03-") && Convert.ToString(v[1].Value, Inv).EndsWith(" UB")))
                {
                    var v = RawOf(db, id, DistApp);
                    bool x = Convert.ToString(v[6].Value, Inv) != "Y";
                    var p = PosOf(db, id);
                    int n = Convert.ToInt32(v[3].Value, Inv); double s = Convert.ToDouble(v[5].Value, Inv);
                    if (!x) continue;   // lewa krawędź 528: UB 03 = pręty X, położenia wzdłuż Y
                    for (int i = 0; i < n; i++) ub.Add(p.Y + i * s);
                }
                if (ub.Count < 10) throw new AssertFail("UB 03 (pręty X) na rzucie dolnym: " + ub.Count);
                GenerateBeams(db, b, t);
                var bl = Dists(db, "BL");
                if (bl.Count != 1) throw new AssertFail("BL: " + bl.Count);
                var sp0 = PosOf(db, bl[0].id).Y; var vv = RawOf(db, bl[0].id, DistApp);
                double step0 = Convert.ToDouble(vv[5].Value, Inv);
                if (step0 > 200.5) throw new AssertFail("rozstaw strzemion " + step0);
                double minD = double.MaxValue;
                for (int i = 0; i < bl[0].count; i++)
                {
                    double y = sp0 + i * step0;
                    minD = Math.Min(minD, ub.Where(u => Math.Abs(u - y) < 1000).Select(u => Math.Abs(u - y)).DefaultIfEmpty(1000).Min());
                }
                if (minD < 90) throw new AssertFail("strzemię " + minD.ToString("0.#", Inv) + " mm od UB 03");
                return $"strzemiona={bl[0].count}, rozstaw={step0.ToString("0.#", Inv)}, min. odstęp od UB 03={minD.ToString("0.#", Inv)}; UB03={ub.Count}";
            }));
        }

        [CommandMethod("RCSMOKE_BEAM_M528", CommandFlags.Modal)]
        public void M528()
        {
            const string step = "M528";
            Run(step, db =>
            {
                List<string> lines = null;
                Check(step, "belka → Mesh (528): Prepare GA, pręty belki, potem Mesh (225) na tej samej płycie", () =>
                {
                    CleanModelSpace(db);
                    ImportGa("PLOT 528-529");
                    var (b, t) = Views(db);
                    if (b.IsNull || t.IsNull) throw new AssertFail("brak obrysów rzutów po Prepare GA");
                    GenerateBeams(db, b, t);
                    int beam = AllBeamDists(db).Count;
                    if (beam == 0) throw new AssertFail("brak rozkładów belki przed Mesh");
                    Mesh(b, t, 225);
                    lines = RegressionDump.DrawingDump.Run(db, false,
                        e => !string.Equals(e.Layer, BeamLayer, StringComparison.OrdinalIgnoreCase), false);
                    File.WriteAllLines(Path.Combine(OutDir, $"beam-dump-528beammesh-{Tag}.txt"), lines, new UTF8Encoding(false));
                    return "rozkłady belki=" + beam.ToString(Inv) + ", rozkłady w zrzucie=" + lines.Count(l => l.Contains("RC_BAR_BLOCK[0]")).ToString(Inv);
                });
                Check(step, "rozkłady siatki (bez RC-BEAM i BB / BT / BL) = zrzut HEAD 528mesh (numery pozycji i odwołania znormalizowane)", () =>
                {
                    if (lines == null) throw new AssertFail("brak zrzutu (poprzednia asercja)");
                    string head = Path.Combine(OutDir, "beam-dump-528mesh-head.txt");
                    if (!File.Exists(head)) throw new AssertFail("brak zrzutu HEAD " + head + " (uruchom run-planc-beam.ps1 -Baseline)");
                    var hd = MeshChunks(File.ReadAllLines(head), "RC_BAR_BLOCK");
                    var nw = MeshChunks(lines, "RC_BAR_BLOCK");
                    string diff = ChunkDiff(hd, nw);
                    if (diff != null) throw new AssertFail("rozkłady siatki różnią się od HEAD: " + diff);
                    var hn = hd.Select(c => c.nr).Distinct().OrderBy(x => x).ToList();
                    var nn = nw.Select(c => c.nr).Distinct().OrderBy(x => x).ToList();
                    string map = MapInfo(hd, nw);
                    return $"identyczne ({hd.Count} rozkładów); numery pozycji siatki HEAD [{string.Join(",", hn)}] / po belce [{string.Join(",", nn)}]{map}";
                });
                Check(step, "INFORMACJA: opisy rozkładów siatki względem HEAD (położenie może się zmienić — omijają opisy belki)", () =>
                {
                    if (lines == null) throw new AssertFail("brak zrzutu (poprzednia asercja)");
                    var hd = MeshChunks(File.ReadAllLines(Path.Combine(OutDir, "beam-dump-528mesh-head.txt")), "RC_BAR_ANNOT");
                    var nw = MeshChunks(lines, "RC_BAR_ANNOT");
                    var rest = nw.Select(c => c.text).ToList();
                    int same = 0;
                    foreach (var c in hd) { int i = rest.IndexOf(c.text); if (i >= 0) { same++; rest.RemoveAt(i); } }
                    return $"opisy: HEAD {hd.Count}, po belce {nw.Count}; bez zmian {same}, zmienione / przesunięte {hd.Count - same}";
                });
            });
        }

        /// <summary>
        /// Encje zrzutu z XData aplikacji <paramref name="app"/> (rozkład / opis) poza kodami belek, znormalizowane: bez prefiksu „#n”,
        /// odwołania 1005 → „@ref”, numery pozycji w opisach (H10-04 → H10-NN) i nazwach bloków → NN. nr = numer z [1] Mark.
        /// </summary>
        private static List<(string text, int nr)> MeshChunks(IList<string> dump, string app)
        {
            var chunks = new List<List<string>>();
            List<string> cur = null;
            foreach (var l in dump)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(l, @"^#\d+ ")) { cur = new List<string> { l }; chunks.Add(cur); }
                else if (cur != null) cur.Add(l);
            }
            var res = new List<(string, int)>();
            foreach (var c in chunks)
            {
                if (!c.Any(l => l.Contains("X " + app + "[0] 1001"))) continue;
                if (c.Any(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"X " + app + @"\[2\] 1000=""(BB|BT|BL)"""))) continue;
                var markLine = c.FirstOrDefault(l => l.Contains("X " + app + "[1] 1000="));
                int nr = PosNr(markLine ?? "");
                var norm = c.Select((l, i) =>
                {
                    string s = i == 0 ? System.Text.RegularExpressions.Regex.Replace(l, @"^#\d+ ", "") : l;
                    s = System.Text.RegularExpressions.Regex.Replace(s, @"1005=(#\d+|@\S+)", "1005=@ref");
                    s = System.Text.RegularExpressions.Regex.Replace(s, @"(H\d+)-\d+", "$1-NN");
                    s = System.Text.RegularExpressions.Regex.Replace(s, @"(RC_[A-Z_]+?)_\d{3}", "$1_NN");
                    return s;
                });
                res.Add((string.Join("\n", norm), nr));
            }
            return res;
        }

        /// <summary>Różnica multizbiorów (null = identyczne): liczby i pierwsza encja bez pary.</summary>
        private static string ChunkDiff(List<(string text, int nr)> head, List<(string text, int nr)> now)
        {
            var rest = now.Select(c => c.text).ToList();
            var missing = new List<string>();
            foreach (var c in head) { int i = rest.IndexOf(c.text); if (i >= 0) rest.RemoveAt(i); else missing.Add(c.text); }
            if (missing.Count == 0 && rest.Count == 0) return null;
            string Short(string s) => s.Replace("\n", " ¶ ").Substring(0, Math.Min(600, s.Replace("\n", " ¶ ").Length));
            return $"HEAD {head.Count} / nowy {now.Count}; bez pary w nowym: {missing.Count}, nadmiarowe: {rest.Count}; " +
                   (missing.Count > 0 ? "HEAD «" + Short(missing[0]) + "» " : "") + (rest.Count > 0 ? "nowy «" + Short(rest[0]) + "»" : "");
        }

        /// <summary>Przemapowanie numerów HEAD → po belce (z rozkładów jednoznacznych po normalizacji); niespójność → AssertFail.</summary>
        private static string MapInfo(List<(string text, int nr)> head, List<(string text, int nr)> now)
        {
            var map = new SortedDictionary<int, int>();
            foreach (var g in head.GroupBy(c => c.text).Where(g => g.Count() == 1))
            {
                var m = now.Where(c => c.text == g.Key).ToList();
                if (m.Count != 1) continue;
                int h = g.First().nr, n = m[0].nr;
                if (map.TryGetValue(h, out int prev) && prev != n) throw new AssertFail($"niespójne przenumerowanie: {h} → {prev} i {n}");
                map[h] = n;
            }
            if (map.Values.Distinct().Count() != map.Count) throw new AssertFail("dwa numery HEAD → jeden numer po belce");
            var changed = map.Where(kv => kv.Key != kv.Value).Select(kv => $"{kv.Key}→{kv.Value}").ToList();
            return changed.Count == 0 ? "; numery bez zmian" : "; INFORMACJA: przenumerowanie (numery belki zajęte wcześniej) " + string.Join(", ", changed);
        }

        // ================================================================
        // Pomocnicze — wtyczka
        // ================================================================

        private static string GaFile(string key)
        {
            if (string.IsNullOrEmpty(GaDir) || !Directory.Exists(GaDir)) return null;
            return Directory.GetFiles(GaDir, "*.dxf").FirstOrDefault(f => Path.GetFileName(f).IndexOf("- " + key + ".dxf", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static object ImportGa(string key)
        {
            string path = GaFile(key) ?? throw new AssertFail("brak DXF " + key);
            var warnings = new List<string>();
            var src = (IDisposable)Call(GaType, "ReadSource", path, warnings);
            try
            {
                var plots = ((System.Collections.IEnumerable)Prop(src, "Plots")).Cast<object>().ToList();
                string want = key.Replace("Plot", "PLOT");
                var plot = plots.FirstOrDefault(p => ((string)Prop(p, "Label")).StartsWith(want, StringComparison.OrdinalIgnoreCase)) ?? plots[0];
                var r = Call(GaType, "Import", Application.DocumentManager.MdiActiveDocument, src, plot, warnings);
                Msg($"\n{Prefix} Import {Prop(plot, "Label")}: " + string.Join(" | ", warnings) + "\n");
                _lastWarnings = warnings;
                return r;
            }
            finally { src.Dispose(); }
        }

        private static List<string> _lastWarnings = new List<string>();

        private static object GenerateBeams(Database db, ObjectId b, ObjectId t)
            => Call(Engine, "GenerateBeams", Application.DocumentManager.MdiActiveDocument, b, t, ObjectId.Null);

        private static List<string> Messages(object res) => ((System.Collections.IEnumerable)Prop(res, "Messages")).Cast<string>().ToList();

        private static void SetFormat(string kind)
        {
            var t = Plugin.GetType(FormatType + "Kind", true);
            Call(FormatType, "Set", Enum.Parse(t, kind), false);
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

        /// <summary>Obrysy zewnętrzne rzutów (SD-PILED-RAFT z RC_GA, największe pole): dolny (B) i górny (T).</summary>
        private static (ObjectId b, ObjectId t) Views(Database db)
        {
            ObjectId Of(string v) => Ms(db, e => e is Polyline pl && pl.Layer == Slab && pl.Closed && Raw(e, "RC_GA") is TypedValue[] x
                                                   && x.Length > 2 && Convert.ToString(x[2].Value, Inv) == v)
                                     .OrderByDescending(id => Area(db, id)).FirstOrDefault();
            return (Of("B"), Of("T"));
        }

        private static string ZoneChecks(Database db, object importResult, ObjectId b, ObjectId t, int h)
        {
            if (b.IsNull || t.IsNull) throw new AssertFail("brak obrysów rzutów");
            var zones = Zones(db);
            if (zones.Count != 2) throw new AssertFail("strefy RC-BEAM: " + zones.Count);
            var sb = new StringBuilder();
            foreach (var z in zones)
            {
                var v = RawOf(db, z, BeamApp);
                double hz = Convert.ToDouble(v[2].Value, Inv);
                if (Math.Abs(hz - h) > 0.5) throw new AssertFail("H strefy " + hz);
                string s6 = XLinkOf(db, z, BeamApp, 6);
                if (s6 != Hx(b) && s6 != Hx(t)) throw new AssertFail("[6] strefy " + s6 + " ≠ obrys rzutu");
                if (!string.Equals(LayerOf(db, z), BeamLayer, StringComparison.OrdinalIgnoreCase)) throw new AssertFail("warstwa strefy");
            }
            if (XLinkOf(db, zones[0], BeamApp, 6) == XLinkOf(db, zones[1], BeamApp, 6)) throw new AssertFail("obie strefy wskazują ten sam rzut");
            int beams = (int)Prop(importResult, "Beams");
            var hs = ((System.Collections.IEnumerable)Prop(importResult, "BeamHeights")).Cast<int>().ToList();
            if (beams != 1 || hs.Count != 1 || hs[0] != h) throw new AssertFail($"Result.Beams={beams}, H={string.Join("/", hs)}");
            if (_lastWarnings.Any(w => w.Contains("≠"))) throw new AssertFail("ostrzeżenie kontroli krzyżowej: " + string.Join(" | ", _lastWarnings.Where(w => w.Contains("≠"))));
            return $"strefy=2, H={h}, ostrzeżenia belki: " + string.Join(" | ", _lastWarnings.Where(w => w.Contains("[RC BEAM]")));
        }

        /// <summary>Numery pozycji prętów belki: dół / strzemiona 04–99, góra 101–499; żadnego 01–03.</summary>
        private static string NumberChecks(Database db)
        {
            var bad = new List<string>();
            foreach (var code in new[] { "BB", "BL", "BT" })
                foreach (var d in Dists(db, code))
                {
                    if (d.pos <= 3) bad.Add(d.mark);
                    if (code == "BT" ? d.pos < 101 || d.pos >= 500 : d.pos < 4 || d.pos >= 100) bad.Add(d.mark);
                }
            if (bad.Count > 0) throw new AssertFail("numery: " + string.Join(", ", bad));
            return "numery OK";
        }

        // ================================================================
        // Pomocnicze — rysunek
        // ================================================================

        private sealed class D { public ObjectId id; public string mark; public int count, pos; public double length; }

        private static List<D> Dists(Database db, string code)
        {
            var res = new List<D>();
            foreach (var id in Ms(db, e => e is BlockReference && Raw(e, DistApp) is TypedValue[] v && v.Length > 8 && Convert.ToString(v[2].Value, Inv) == code))
            {
                var v = RawOf(db, id, DistApp);
                string mark = Convert.ToString(v[1].Value, Inv);
                res.Add(new D { id = id, mark = mark, count = Convert.ToInt32(v[3].Value, Inv), length = Convert.ToDouble(v[8].Value, Inv), pos = PosNr(mark) });
            }
            return res;
        }

        private static List<ObjectId> AllBeamDists(Database db)
            => Ms(db, e => e is BlockReference && Raw(e, DistApp) is TypedValue[] v && v.Length > 2
                           && new[] { "BB", "BT", "BL" }.Contains(Convert.ToString(v[2].Value, Inv)));

        private static List<ObjectId> Zones(Database db)
            => Ms(db, e => e is Polyline && Raw(e, BeamApp) != null);

        private static List<(string mark, string shape, double a, double b, bool isBlock)> Templates(Database db)
        {
            var res = new List<(string, string, double, double, bool)>();
            foreach (var id in Ms(db, e => Raw(e, "RC_SINGLE_BAR") is TypedValue[] v && v.Length > 7
                                           && new[] { "BB", "BT", "BL" }.Contains(Convert.ToString(v[7].Value, Inv))))
            {
                var v = RawOf(db, id, "RC_SINGLE_BAR");
                bool isBlock = With(db, id, e => e is BlockReference);
                res.Add((Convert.ToString(v[1].Value, Inv), Convert.ToString(v[3].Value, Inv),
                         Convert.ToDouble(v[4].Value, Inv), Convert.ToDouble(v[5].Value, Inv), isBlock));
            }
            return res;
        }

        private static int PosNr(string mark)
        {
            var m = System.Text.RegularExpressions.Regex.Match(mark ?? "", @"H\d+-(\d+)");
            return m.Success ? int.Parse(m.Groups[1].Value, Inv) : -1;
        }

        private static ObjectId AnnotOf(Database db, ObjectId dist)
        {
            string h = XLinkOf(db, dist, DistApp, 11);
            if (string.IsNullOrEmpty(h)) return ObjectId.Null;
            return db.TryGetObjectId(new Handle(Convert.ToInt64(h, 16)), out var id) && !id.IsErased ? id : ObjectId.Null;
        }

        private static string XLinkOf(Database db, ObjectId id, string app, int idx)
        {
            var v = RawOf(db, id, app);
            if (v == null || v.Length <= idx) return "";
            object o = v[idx].Value;
            if (o is Handle hh) return hh.Value == 0 ? "" : hh.Value.ToString("X8");
            string s = Convert.ToString(o, Inv);
            return long.TryParse(s, NumberStyles.HexNumber, Inv, out long x) && x != 0 ? x.ToString("X8") : "";
        }

        private static string Hx(ObjectId id) => id.IsNull ? "" : id.Handle.Value.ToString("X8");

        private static TypedValue[] Raw(Entity e, string app)
        {
            using (var rb = e?.GetXDataForApplication(app)) return rb?.AsArray();
        }

        private static T With<T>(Database db, ObjectId id, Func<Entity, T> f)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var r = f((Entity)tr.GetObject(id, OpenMode.ForRead, true));
                tr.Commit();
                return r;
            }
        }

        private static TypedValue[] RawOf(Database db, ObjectId id, string app) => With(db, id, e => Raw(e, app));

        private static string LayerOf(Database db, ObjectId id) => With(db, id, e => e.Layer);

        private static Point3d PosOf(Database db, ObjectId id) => With(db, id, e => ((BlockReference)e).Position);

        private static List<Point2d> Verts(Database db, ObjectId id) => With(db, id, e =>
        {
            var pl = (Polyline)e;
            var r = new List<Point2d>();
            for (int i = 0; i < pl.NumberOfVertices; i++) r.Add(pl.GetPoint2dAt(i));
            return r;
        });

        private static double Area(Database db, ObjectId id) => With(db, id, e => Math.Abs(((Polyline)e).Area));

        private static bool SameShifted(List<Point2d> a, List<Point2d> b, Vector2d d)
            => a.Count == b.Count && b.All(p => a.Any(q => q.GetDistanceTo(p + d) < 1.0));

        private static List<ObjectId> Ms(Database db, Func<Entity, bool> pred)
        {
            var res = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                    if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Entity e && pred(e)) res.Add(id);
                tr.Commit();
            }
            return res;
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

        // ── zrzuty ──────────────────────────────────────────────────────

        private static string CompareDump(string name, List<string> lines)
        {
            string file = Path.Combine(OutDir, $"beam-dump-{name}-{Tag}.txt");
            File.WriteAllLines(file, lines, new UTF8Encoding(false));
            if (Tag == "head") return $"zrzut bazowy: {lines.Count} linii → {file}";
            string head = Path.Combine(OutDir, $"beam-dump-{name}-head.txt");
            if (!File.Exists(head)) throw new AssertFail("brak zrzutu HEAD " + head + " (uruchom run-planc-beam.ps1 -Baseline)");
            var h = File.ReadAllLines(head);
            if (h.Length == lines.Count && h.SequenceEqual(lines)) return $"identyczny z HEAD ({lines.Count} linii)";
            int i = 0;
            while (i < Math.Min(h.Length, lines.Count) && h[i] == lines[i]) i++;
            throw new AssertFail($"różni się od HEAD (linie {h.Length} / {lines.Count}), pierwsza różnica w linii {i + 1}: " +
                                 $"HEAD «{(i < h.Length ? h[i] : "—")}» / nowy «{(i < lines.Count ? lines[i] : "—")}»");
        }

        // ================================================================
        // Infrastruktura (jak PlanCNibTests)
        // ================================================================

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
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("PlanCBeamTests.Msg: " + ex); }
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

        private static string Int(string what, int act, int exp)
        {
            if (act != exp) throw new AssertFail($"{what}: jest {act.ToString(Inv)}, oczekiwano {exp.ToString(Inv)}");
            return $"{what}={act.ToString(Inv)}";
        }

        private static string Seq(string what, IEnumerable<int> act, IEnumerable<int> exp)
        {
            var a = act.ToList(); var e = exp.ToList();
            if (!a.SequenceEqual(e)) throw new AssertFail($"{what}: jest [{string.Join(",", a)}], oczekiwano [{string.Join(",", e)}]");
            return $"{what}=[{string.Join(",", a)}]";
        }

        private static string Eq(string what, object act, object exp)
        {
            if (!Equals(act, exp)) throw new AssertFail($"{what}: jest {act ?? "null"}, oczekiwano {exp ?? "null"}");
            return $"{what}={act}";
        }
    }
}
