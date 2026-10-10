// BEAM — testy dymne belek / pogrubień (docs/PLAN_BELKI_2026-10-10.md, E1 + E3 + E4) w BricsCAD V25.
// Uruchamiane skryptem planc-beam.scr (run-planc-beam.ps1). Komendy RCSMOKE_BEAM_*. Dostęp do wtyczki wyłącznie refleksją.
// Dane: GA Holbrook P02 (DXF, tylko odczyt — GaImportEngine.ReadSource czyta do bazy pomocniczej), folder: zmienna RC_BEAM_GA.
//
// D526  Prepare GA 526 (bez stref) → zrzut DrawingDump (pełny, z warstwami) = zrzut wtyczki HEAD (bit w bit).
// D319  Prepare GA 319 + Mesh (225) → zrzut bez warstwy RC-BEAM = zrzut HEAD (strefy belek nie zmieniają siatki — E2 wstrzymany;
//       obrys 319 ma skosy — AutoRebar siatki go odrzuca, zostaje B3 ADD). D528 / D539 — to samo na płytach osiowych z belką.
//       Kroki D* działają też z wtyczką HEAD (przebieg -Baseline, RC_BEAM_DUMP_TAG=head) — porównanie w przebiegu „new”.
// G319  strefa + H = 450 (oba rzuty, [6] = obrys rzutu), generowanie: lewa noga ze skosem — „detal ręczny” (narożnik z nią bez prętów L),
//       góra + prawa: narożnik L (pręty L), koniec wewnętrzny prawej nogi — 2 U-bary na warstwę; strzemiona 41 (góra przez kwadrat
//       narożnika) / 12.
// G539  H = 300, L + L; format Blok (szablony = pręty-bloki): strzemiona 13 / 24 / 52 (lewa noga przez oba kwadraty narożników),
//       pręty proste: dół 4, góra 4; narożniki L: 2 × 4 pręty L (kształt 11) na warstwę, końce (wewnętrzny + przy krawędzi): 4 U-bary
//       (kształt 21) na warstwę; wymiary prętów L i U-barów, skos rozkładu prętów L, symbol haka.
// G528  H = 375, prosta: strzemiona 51 (pierwsze 120 od lica — za tyłami U-barów), dół 2 × 4 H16 L5500, góra 2 × 4 H16 (seria 101+),
//       U-bary na obu końcach (2 na warstwę i koniec, A = C = 800 / 775 — wewnętrzny o Ø + 20 głębiej, B = 500 / 175 — 2 pozycje
//       na warstwę), numery ≥ 04 bez 01–03, opisy, znaczniki.
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
// E5 / E6 (RC_SECTION z belką, BBS):
// S528  528 + Mesh + pręty belki; RC_SECTION A prostopadle do belki: profil (spód −375, nib belki), kropki 4 H16 dół / góra, strzemię 51
//       w widoku, wymiary H / szerokość, opisy; E4b: pręty boczne BS (1 na bok) i w nibie BN (H10 górą, H16 dołem) — rozkłady i kropki;
//       RC_SECTION_CHECK — aktualny, składnik BEAM:<strefa> i pręty belki w odcisku.
// S528PDF kopia rysunku z layoutem „BELKA 528” (rzut dolny + przekrój A) do PDF (RC_BEAM_PDF_DIR).
// S528C przekrój C „sprzed E5” (planer bez belki) → NIEAKTUALNY „belka … nieuwzględniona”; H strefy 375 → 450 → A NIEAKTUALNY
//       „zmieniona belka … H 375 → 450”; RC_SECTION_UPDATE (SectionUpdater.Update) A i C → aktualne, spód −450, BEAM w odcisku.
// S539  539 + Mesh + pręty belki; RC_SECTION B wzdłuż najdłuższego odcinka (oś strefy): pręty dołu / góry w widoku, strzemiona jako pionowe
//       ramiona, profil −300; CHECK aktualny, UPDATE bez zmian.
// S539BBS RC_BBS (ReadBars) / RC_SCHEDULE / RC_COUNT_BBS / .xls: pozycje belki (BB / BL dół, BT góra; 00 / 11 / 21 / 51), długości cięcia
//       BS 8666:2020, sztuki = suma rozkładów, masa .xls = COUNT.
// S539PDF kopia rysunku z layoutem „BELKA 539” (rzut dolny + przekrój B) do PDF.

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
        private const string Title = "BEAM — testy dymne belek / pogrubień (E1 + E3 + E4 + E5 + E6)";
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
            "S528", "S528PDF", "S528C", "S539", "S539BBS", "S539PDF",
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
                Check(step, "generowanie: lewa noga (skos) — detal ręczny; góra + prawa — strzemiona 41 / 12, proste dół 3 / góra 3, narożnik L 1, U-bary 2 na warstwę", () =>
                {
                    var res = GenerateBeams(db, b, t);
                    var msgs = Messages(res);
                    if (!msgs.Any(m => m.Contains("detal ręczny"))) throw new AssertFail("brak komunikatu „detal ręczny”: " + string.Join(" | ", msgs));
                    if (msgs.Any(m => m.Contains("w przygotowaniu"))) throw new AssertFail("komunikat „E4 w przygotowaniu” nadal jest");
                    if (!msgs.Any(m => m.Contains("narożnik L z odcinkiem „detal ręczny”"))) throw new AssertFail("brak komunikatu o narożniku z odcinkiem ręcznym");
                    if (!msgs.Any(m => m.Contains("narożniki L: 1") && m.Contains("końce z U-barami: 1")))
                        throw new AssertFail("komunikat zamknięcia końców: " + string.Join(" | ", msgs.Where(m => m.Contains("zamknięcie"))));
                    var bl = Dists(db, "BL");
                    var counts = bl.Select(x => x.count).OrderBy(x => x).ToList();
                    var sb = new StringBuilder(Seq("strzemiona (liczby)", counts, new[] { 12, 41 }));
                    foreach (var code in new[] { "BB", "BT" })
                    {
                        var all = Dists(db, code);
                        sb.Append("; ").Append(Int(code + " proste", all.Count(x => x.shape == "00"), 3))
                          .Append("; ").Append(Int(code + " pręty L", all.Count(x => x.shape == "11"), 1))
                          .Append("; ").Append(Int(code + " U-bary", all.Count(x => x.shape == "21"), 2));
                        if (all.Where(x => x.shape == "11").Any(x => x.count != 4)) throw new AssertFail(code + ": pręty L nie w 4 rzędach");
                    }
                    return sb.Append("; ").Append(NumberChecks(db)).Append("; ").Append(BentInsideZones(db)).ToString();
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
                Check(step, "format Blok: strzemiona 13 / 24 / 52, proste dół 4 / góra 4, szablony = pręty-bloki, strzemię 51 A=200 B=520", () =>
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
                    return Seq("strzemiona", counts, new[] { 13, 24, 52 })
                         + "; " + Int("BB proste", Dists(db, "BB").Count(x => x.shape == "00"), 4)
                         + "; " + Int("BT proste", Dists(db, "BT").Count(x => x.shape == "00"), 4)
                         + "; szablony=" + string.Join(",", tpl.Select(x => x.mark)) + "; " + NumberChecks(db);
                });
                Check(step, "E4: narożniki L (2 × 4 pręty L kształt 11) i U-bary (4 kształt 21, w tym koniec wewnętrzny) na warstwę; wymiary, skos, symbol haka, opisy", () =>
                {
                    var sb = new StringBuilder();
                    var tpl = Templates(db);
                    foreach (var code in new[] { "BB", "BT" })
                    {
                        var all = Dists(db, code);
                        var L = all.Where(x => x.shape == "11").ToList();
                        var U = all.Where(x => x.shape == "21").ToList();
                        sb.Append(Int(code + " pręty L", L.Count, 2)).Append("; ").Append(Int(code + " U-bary", U.Count, 4)).Append("; ");
                        foreach (var x in L)
                        {
                            if (x.count != 4) throw new AssertFail("pręty L: " + x.count + " " + x.mark);
                            if (!x.mark.EndsWith(" BEAM CORNER")) throw new AssertFail("opis prętów L: " + x.mark);
                            var v = RawOf(db, x.id, DistApp);
                            if (Convert.ToString(v[17].Value, Inv) != "Hook") throw new AssertFail("symbol prętów L: " + v[17].Value);
                            double skew = Math.Abs(Convert.ToDouble(v[25].Value, Inv) - Convert.ToDouble(v[26].Value, Inv));
                            if (Math.Abs(skew - 484) > 2) throw new AssertFail("skos rozkładu prętów L: " + skew.ToString("0.#", Inv));
                        }
                        foreach (var x in U)
                            if (x.count != 1 || !x.mark.EndsWith(" BEAM END")) throw new AssertFail("U-bar: " + x.count + " " + x.mark);
                        bool topSer = code == "BT";
                        var tl = tpl.Where(x => x.shape == "11" && (x.pos >= 101) == topSer).Select(x => $"{x.a:0}/{x.b:0}").OrderBy(x => x).ToList();
                        var tu = tpl.Where(x => x.shape == "21" && (x.pos >= 101) == topSer).Select(x => $"{x.a:0}/{x.b:0}/{x.c:0}").OrderBy(x => x).ToList();
                        Seq2(code + " szablony L", tl, new[] { "1375/1375", "1450/1350" });
                        // 900 / 850 — koniec wewnętrzny (pręt prosty wyśrodkowany), 825 / 800 — koniec przy krawędzi (U-bar wewnętrzny — tył
                        // o Ø + 20 głębiej, ramię krótsze)
                        Seq2(code + " szablony U", tu, new[] { "800/175/800", "825/500/825", "850/175/850", "900/500/900" });
                    }
                    return sb.Append(NumberChecks(db)).Append("; ").Append(BentInsideZones(db)).ToString();
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
                Check(step, "strzemiona 51 H10 co 200 (LINKS, pierwsze 120 od lica — za tyłami U-barów), dół 2 × 4 H16 L5500 (BEAM B), góra 2 × 4 H16 (BEAM T, 101+), znaczniki", () =>
                {
                    GenerateBeams(db, _b528, _t528);
                    var bl = Dists(db, "BL");
                    var bb = Dists(db, "BB").Where(x => x.shape == "00").ToList(); var bt = Dists(db, "BT").Where(x => x.shape == "00").ToList();
                    var sb = new StringBuilder();
                    sb.Append(Int("BL", bl.Count, 1)).Append("; ").Append(Int("strzemiona", bl[0].count, 51));
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
                Check(step, "E4: U-bary na obu końcach — 2 na warstwę i koniec (A = C = 800 / 775, B = 500 / 175, 2 pozycje na warstwę), znaczniki, opisy", () =>
                {
                    var sb = new StringBuilder();
                    var zt = Zones(db).FirstOrDefault(z => XLinkOf(db, z, BeamApp, 6) == Hx(_t528));
                    foreach (var code in new[] { "BB", "BT" })
                    {
                        var all = Dists(db, code);
                        var U = all.Where(x => x.shape == "21").ToList();
                        sb.Append(Int(code + " U-bary", U.Count, 4)).Append("; ").Append(Int(code + " pręty L", all.Count(x => x.shape == "11"), 0)).Append("; ");
                        var pos = U.Select(x => x.pos).Distinct().ToList();
                        if (pos.Count != 2) throw new AssertFail(code + ": pozycje U-barów " + string.Join(",", pos));
                        foreach (var x in U)
                        {
                            if (x.count != 1 || !x.mark.EndsWith(" BEAM END")) throw new AssertFail("U-bar: " + x.count + " " + x.mark);
                            Eq("znacznik " + x.mark, XLinkOf(db, x.id, TagApp, 1), code == "BT" ? Hx(zt) : Hx(_z528));
                            if (AnnotOf(db, x.id).IsNull) throw new AssertFail("brak opisu " + x.mark);
                        }
                        // oba końce: rozkłady U-barów przy dolnym i górnym licu strefy (pręty wzdłuż Y)
                        var ys = U.Select(x => PosOf(db, x.id).Y).OrderBy(y => y).ToList();
                        if (ys[3] - ys[0] < 5000) throw new AssertFail(code + ": U-bary tylko przy jednym końcu");
                        sb.Append(code).Append(" U: ").Append(string.Join(" | ", U.Select(x => x.mark))).Append("; ");
                    }
                    var tu = Templates(db).Where(x => x.shape == "21").Select(x => $"{x.a:0}/{x.b:0}/{x.c:0}").OrderBy(x => x).ToList();
                    Seq2("szablony U (dół + góra)", tu, new[] { "775/175/775", "775/175/775", "800/500/800", "800/500/800" });
                    return sb.Append(NumberChecks(db)).ToString();
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
                return Int("rozkłady belki", all.Count, 19) + "; " + Int("szablony", tpl1, tpl0) + "; " + NumberChecks(db);   // E4b: + 2 BS + 4 BN
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
                    if (cd.Count != 13) throw new AssertFail("rozkłady-kopie: " + cd.Count);   // E4b: 7 + 2 BS + 4 BN
                    foreach (var id in cd) Eq("znacznik kopii " + id.Handle, XLinkOf(db, id, TagApp, 1), Hx(cz));
                    return "kopie=" + cd.Count.ToString(Inv);
                });
                Check(step, "ponowne generowanie ORYGINAŁU — rozkłady kopii zostają", () =>
                {
                    GenerateBeams(db, _b528, _t528);
                    int lost = (cd ?? new List<ObjectId>()).Count(id => id.IsErased);
                    if (lost > 0) throw new AssertFail("usunięto rozkłady kopii: " + lost);
                    return Int("rozkłady belek razem", AllBeamDists(db).Count, 32);
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
                if (md.Count != 13) throw new AssertFail("rozkłady-odbicia: " + md.Count);
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

        /// <summary>Kopia rzutu dolnego 528 w pasie X (ARRAY / PASTECLIP): jedna strefa, [6] → obrys kopii, 13 rozkładów belki (strzemiona,
        /// 2 proste, 4 U-bary, E4b: 2 boczne, 4 w nibie) → strefa kopii.</summary>
        private static string CloneRegionChecks(Database db, Func<double, bool> inX, string what)
        {
            var zs = Zones(db).Where(z => Verts(db, z).All(p => inX(p.X))).ToList();
            if (zs.Count != 1) throw new AssertFail($"strefy {what}: {zs.Count} (oczekiwano 1)");
            var ob = Ms(db, e => e is Polyline pl && pl.Layer == Slab && pl.Closed && Verts(db, e.ObjectId).All(p => inX(p.X)))
                     .OrderByDescending(id => Area(db, id)).FirstOrDefault();
            if (ob.IsNull) throw new AssertFail("brak obrysu " + what);
            Eq("strefa " + what + " [6]", XLinkOf(db, zs[0], BeamApp, 6), Hx(ob));
            var ds = AllBeamDists(db).Where(id => inX(PosOf(db, id).X)).ToList();
            if (ds.Count != 13) throw new AssertFail($"rozkłady {what}: {ds.Count}");
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
                if (c.Any(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"X " + app + @"\[2\] 1000=""(BB|BT|BL|BS|BN)"""))) continue;
                var markLine = c.FirstOrDefault(l => l.Contains("X " + app + "[1] 1000="));
                int nr = PosNr(markLine ?? "");
                var norm = c.Select((l, i) =>
                {
                    string s = i == 0 ? System.Text.RegularExpressions.Regex.Replace(l, @"^#\d+ ", "") : l;
                    s = System.Text.RegularExpressions.Regex.Replace(s, @"1005=(#\d+|@\S+)", "1005=@ref");
                    s = System.Text.RegularExpressions.Regex.Replace(s, @"(H\d+)-\d+", "$1-NN");
                    s = System.Text.RegularExpressions.Regex.Replace(s, @"(RC_[A-Z_]+?)_\d{3}", "$1_NN");
                    // E4b: pręt siatki identyczny z prętem belki (np. B1 H10 L5500 = pręt boczny BS) — wspólna pozycja, BTR rozkładu
                    // dostaje sufiks unikalnej nazwy („_#” w zrzucie); treść rozkładu bez zmian
                    s = s.Replace("RC_SLAB_BARS_NN_#", "RC_SLAB_BARS_NN");
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
        // E5 — RC_SECTION przez belkę; E6 — BBS z belką
        // ================================================================

        private const string Planner = "BricsCadRc.Core.SectionPlanner";
        private const string Store = "BricsCadRc.Core.SectionStore";
        private const string Updater = "BricsCadRc.Core.SectionUpdater";
        private static Point3d _tlA, _tlC, _tlB;
        private static ObjectId _b539;

        /// <summary>Przekrój RC_SECTION drogą komendy (PrepareSection → Plan → DrawSectionRecorded); zwraca (kontekst, plan).</summary>
        private static (object ctx, object plan) DrawSection(Database db, ObjectId slab, Point2d p1, Point2d p2, Point2d look, Point3d tl,
                                                             string letter, Action<object> tweakInput = null)
        {
            var ctx = Call(Engine, "PrepareSection", db, slab, p1, p2, look) ?? throw new AssertFail("PrepareSection = null");
            tweakInput?.Invoke(Prop(ctx, "Input"));
            var plan = Call(Planner, "Plan", Prop(ctx, "Input"));
            int n = (int)Call(Engine, "DrawSectionRecorded", db, ctx, plan, tl, letter);
            if (n <= 0) throw new AssertFail("DrawSectionRecorded: 0 obiektów");
            return (ctx, plan);
        }

        private static List<object> L(object o, string name) => ((System.Collections.IEnumerable)Prop(o, name)).Cast<object>().ToList();
        private static double Dv(object o, string name) => Convert.ToDouble(Prop(o, name), Inv);

        private static List<object> CheckAll(Database db) => ((System.Collections.IEnumerable)Call(Store, "CheckAll", db, false)).Cast<object>().ToList();

        private static object Section(Database db, string letter)
            => CheckAll(db).Where(i => (string)Prop(i, "Letter") == letter && !((ObjectId)Prop(i, "TitleId")).IsNull)
                           .OrderByDescending(i => ((ObjectId)Prop(i, "TitleId")).Handle.Value).FirstOrDefault()
               ?? throw new AssertFail("brak przekroju " + letter);

        private static string StatusOf(object info) => Convert.ToString(Prop(info, "Status"), Inv);
        private static List<string> ReasonsOf(object info) => L(info, "Reasons").Cast<string>().ToList();

        /// <summary>Bbox wierzchołków.</summary>
        private static (double x0, double y0, double x1, double y1) Box(List<Point2d> v)
            => (v.Min(p => p.X), v.Min(p => p.Y), v.Max(p => p.X), v.Max(p => p.Y));

        /// <summary>Pozycje belki z szablonów: kod (BB / BT / BL) → numery.</summary>
        private static Dictionary<int, (string code, string shape, double a, double b, double c, int dia)> BeamPositions(Database db)
        {
            var res = new Dictionary<int, (string, string, double, double, double, int)>();
            foreach (var id in Ms(db, e => Raw(e, "RC_SINGLE_BAR") is TypedValue[] v && v.Length > 7
                                           && new[] { "BB", "BT", "BL", "BS", "BN" }.Contains(Convert.ToString(v[7].Value, Inv))))
            {
                var v = RawOf(db, id, "RC_SINGLE_BAR");
                int pos = PosNr(Convert.ToString(v[1].Value, Inv));
                res[pos] = (Convert.ToString(v[7].Value, Inv), Convert.ToString(v[3].Value, Inv), Convert.ToDouble(v[4].Value, Inv),
                            Convert.ToDouble(v[5].Value, Inv), Convert.ToDouble(v[6].Value, Inv), Convert.ToInt32(v[2].Value, Inv));
            }
            return res;
        }

        [CommandMethod("RCSMOKE_BEAM_S528", CommandFlags.Modal)]
        public void S528()
        {
            const string step = "S528";
            Run(step, db =>
            {
                object plan = null, ctx = null;
                Check(step, "528: Prepare GA, Mesh (225), pręty belki; RC_SECTION A prostopadle do belki (y = 30 % długości strefy, poza zakładem)", () =>
                {
                    SetFormat("Polyline");
                    CleanModelSpace(db);
                    ImportGa("PLOT 528-529");
                    (_b528, _t528) = Views(db);
                    Mesh(_b528, _t528, 225);
                    var gm = Messages(GenerateBeams(db, _b528, _t528));
                    // poprawka recenzji: BN górą przy T IN NIB wzdłuż nibu — komunikat „może dublować T IN NIB”
                    if (!gm.Any(m => m.Contains("może dublować T IN NIB"))) throw new AssertFail("brak komunikatu BN górą / T IN NIB: " + string.Join(" | ", gm.Where(m => m.Contains("nib"))));
                    _z528 = Zones(db).FirstOrDefault(z => XLinkOf(db, z, BeamApp, 6) == Hx(_b528));
                    var zb = Box(Verts(db, _z528)); var sb = Box(Verts(db, _b528));
                    if (zb.y1 - zb.y0 < zb.x1 - zb.x0) throw new AssertFail("strefa 528 nie jest wzdłuż Y");
                    double y = zb.y0 + 0.3 * (zb.y1 - zb.y0);
                    _tlA = new Point3d(sb.x0, sb.y0 - 9000, 0);
                    (ctx, plan) = DrawSection(db, _b528, new Point2d(sb.x0 - 1000, y), new Point2d(zb.x1 + 3000, y), new Point2d(sb.x0, y + 100), _tlA, "A");
                    var beams = L(Prop(ctx, "Input"), "Beams");
                    if (beams.Count != 1) throw new AssertFail("pasy belek na linii cięcia: " + beams.Count);
                    return $"pas {Dv(beams[0], "S0"):0.#}..{Dv(beams[0], "S1"):0.#}, H={Dv(beams[0], "Height"):0}, A strzemienia={Dv(beams[0], "StirrupA"):0}";
                });
                Check(step, "A: profil (spód −375 w pasie, nib belki −375…−225), kropki 4 H16 dół (−317) i góra (−78), strzemię 51 w widoku, wymiar H 375; E4b: boczne (−197,5, po 1 na bok), w nibie (w narożach UB 03; bez UB 03 dołem −317, górą −270), rozkłady BS / BN", () =>
                {
                    if (plan == null) throw new AssertFail("brak planu");
                    // pozycje mogą być wspólne (identyczny pręt: BN H16 = BB H16 L5500, BS H10 = B1 siatki) — rozpoznanie po geometrii
                    var sg = L(plan, "Segments").First(x => Prop(x, "Prof") != null);
                    var prof = L(sg, "Prof").Select(p => (ValueTuple<double, double, double, double>)p).ToList();
                    var trz = prof.FirstOrDefault(p => Math.Abs(p.Item3 + 375) < 1e-6 && p.Item4 == 0);
                    var nibP = prof.FirstOrDefault(p => Math.Abs(p.Item3 + 375) < 1e-6 && Math.Abs(p.Item4 + 225) < 1e-6);
                    if (trz.Item2 - trz.Item1 < 1) throw new AssertFail("brak trzonu −375…0: " + string.Join(" ", prof));
                    if (nibP.Item2 - nibP.Item1 < 1) throw new AssertFail("brak nibu belki −375…−225: " + string.Join(" ", prof));
                    var dots = L(plan, "Dots").Where(d => (bool)Prop(d, "Beam")).ToList();
                    bool In(ValueTuple<double, double, double, double> iv, object d) => Dv(d, "U") >= iv.Item1 - 1 && Dv(d, "U") <= iv.Item2 + 1;
                    List<object> At(double v, ValueTuple<double, double, double, double> iv) => dots.Where(d => In(iv, d) && Math.Abs(Dv(d, "V") - v) < 0.01).ToList();
                    // BN w narożach spinki UB 03 (poprawka recenzji), gdy UB 03 jest w przekroju: ramiona ± (5 + d/2); bez UB 03 — wzór (−317 / −270)
                    var ub3 = L(plan, "Bars").FirstOrDefault(b => Convert.ToString(Prop(b, "Kind"), Inv) == "NibUB");
                    var ubP = ub3 == null ? null : ((System.Collections.IEnumerable)Prop(ub3, "Path")).Cast<object>().Select(p => (ValueTuple<double, double>)p).ToList();
                    double vNb = ubP != null ? ubP.Min(p => p.Item2) + 5 + 8 : -317, vNt = ubP != null ? ubP.Max(p => p.Item2) - 5 - 5 : -270;
                    var bbD = At(-317, trz); var btD = At(-78, trz); var bsD = At(-197.5, trz); var nbD = At(vNb, nibP); var ntD = At(vNt, nibP);
                    string Pos(List<object> l) => string.Join(",", l.Select(d => (string)Prop(d, "PosNr")).Distinct());
                    if (bbD.Count != 4 || btD.Count != 4) throw new AssertFail($"kropki dół {bbD.Count} (poz. {Pos(bbD)}), góra {btD.Count} (poz. {Pos(btD)})");
                    if (bsD.Count != 2) throw new AssertFail($"kropki boczne {bsD.Count}: " + string.Join(" ", dots.Select(d => $"{Dv(d, "U"):0.#},{Dv(d, "V"):0.#}")));
                    if (nbD.Count != 1 || ntD.Count != 1) throw new AssertFail($"kropki w nibie (UB 03: {(ubP != null ? "tak" : "nie")}, v {vNb:0.#} / {vNt:0.#}): dołem {nbD.Count}, górą {ntD.Count}: " +
                                                                               string.Join(" ", dots.Select(d => $"{Dv(d, "U"):0.#},{Dv(d, "V"):0.#}")));
                    // T IN NIB nie nachodzi na BN górą (odstęp lic ≥ 0)
                    foreach (var t in L(plan, "Dots").Where(d => (bool)Prop(d, "TinNib")))
                    {
                        double du = Dv(t, "U") - Dv(ntD[0], "U"), dv = Dv(t, "V") - Dv(ntD[0], "V");
                        double need = (Convert.ToDouble(Prop(t, "Diameter"), Inv) + Convert.ToDouble(Prop(ntD[0], "Diameter"), Inv)) / 2.0;
                        if (Math.Sqrt(du * du + dv * dv) < need - 1e-6) throw new AssertFail($"T IN NIB {Dv(t, "U"):0.#},{Dv(t, "V"):0.#} nachodzi na BN górą {Dv(ntD[0], "U"):0.#},{Dv(ntD[0], "V"):0.#}");
                    }
                    var links = L(plan, "Bars").Where(b => Convert.ToString(Prop(b, "Kind"), Inv) == "Beam").ToList();
                    if (links.Count != 1) throw new AssertFail("prętów belki w widoku (strzemię): " + links.Count);
                    var lp = ((System.Collections.IEnumerable)Prop(links[0], "Path")).Cast<object>().Select(p => (ValueTuple<double, double>)p).ToList();
                    double vmin = lp.Min(p => p.Item2), vmax = lp.Max(p => p.Item2);
                    if (Math.Abs(vmin + 330) > 0.01 || Math.Abs(vmax + 65) > 0.01) throw new AssertFail($"strzemię v {vmin:0.#}..{vmax:0.#} (oczekiwano −330..−65)");
                    var dims = L(plan, "Dims");
                    if (!dims.Any(d => (bool)Prop(d, "Vertical") && Math.Abs(Math.Abs(Dv(d, "V1") - Dv(d, "V2")) - 375) < 0.01)) throw new AssertFail("brak wymiaru H 375");
                    if (!dims.Any(d => !(bool)Prop(d, "Vertical") && Math.Abs(Math.Abs(Dv(d, "U1") - Dv(d, "U2")) - 600) < 1.5)) throw new AssertFail("brak wymiaru szerokości 600");
                    var lbl = L(plan, "Labels").Select(l => (string)Prop(l, "Text")).ToList();
                    foreach (var p in bbD.Concat(btD).Concat(bsD).Concat(nbD).Concat(ntD).Select(d => (string)Prop(d, "PosNr")).Concat(new[] { (string)Prop(links[0], "PosNr") }).Distinct())
                        if (!lbl.Contains(p)) throw new AssertFail($"brak opisu poz. {p} (dół {Pos(bbD)}, góra {Pos(btD)}, boczne {Pos(bsD)}, nib {Pos(nbD)}/{Pos(ntD)}, strzemię {Prop(links[0], "PosNr")}; opisy: {string.Join(",", lbl.Distinct())})");
                    // rozkłady E4b na rysunku: BS po 2 pręty (1 na bok), BN górą H10 i dołem H16, znacznik strefy
                    var bs = Dists(db, "BS"); var bn = Dists(db, "BN");
                    if (bs.Count == 0 || bs.Any(x => x.count != 2 || !x.mark.EndsWith(" BEAM SIDE") || !x.mark.StartsWith("H10-"))) throw new AssertFail("BS: " + string.Join(" | ", bs.Select(x => x.count + " " + x.mark)));
                    var nt = bn.Where(x => x.mark.EndsWith(" BEAM NIB T")).ToList(); var nb = bn.Where(x => x.mark.EndsWith(" BEAM NIB B")).ToList();
                    if (nt.Count == 0 || nb.Count == 0 || nt.Any(x => !x.mark.StartsWith("H10-")) || nb.Any(x => !x.mark.StartsWith("H16-")) || bn.Any(x => x.count != 1))
                        throw new AssertFail("BN: " + string.Join(" | ", bn.Select(x => x.count + " " + x.mark)));
                    foreach (var x in bs.Concat(bn)) Eq("znacznik " + x.mark, XLinkOf(db, x.id, TagApp, 1), Hx(_z528));
                    return $"dół poz. {Pos(bbD)} ×4 −317, góra {Pos(btD)} ×4 −78, boczne {Pos(bsD)} ×2 −197,5, nib dołem {Pos(nbD)} {vNb:0.#} / górą {Pos(ntD)} {vNt:0.#} (UB 03: {(ubP != null ? "tak" : "nie")}), strzemię {Prop(links[0], "PosNr")} −330..−65; " +
                           $"BS: {string.Join(", ", bs.Select(x => x.count + " " + x.mark))}; BN: {string.Join(", ", bn.Select(x => x.count + " " + x.mark))}";
                });
                Check(step, "RC_SECTION_CHECK: A aktualny; odcisk ma składnik BEAM:<strefa> i pręty belki (DIST Kind:Beam)", () =>
                {
                    var a = Section(db, "A");
                    if (StatusOf(a) != "Aktualny") throw new AssertFail("status A: " + StatusOf(a) + " — " + string.Join(" | ", ReasonsOf(a)));
                    var comps = (System.Collections.Generic.IDictionary<string, string>)Prop(Prop(a, "Record"), "Components");
                    var beamKeys = comps.Keys.Where(k => k.StartsWith("BEAM:", StringComparison.Ordinal)).ToList();
                    if (beamKeys.Count != 1 || beamKeys[0] != "BEAM:" + Hx(_z528)) throw new AssertFail("składniki BEAM: " + string.Join(",", beamKeys) + " (strefa " + Hx(_z528) + ")");
                    int nb = comps.Values.Count(v => v.Contains("Kind:Beam"));
                    if (nb < 3) throw new AssertFail("rozkłady belki w odcisku: " + nb);
                    return $"{beamKeys[0]}; rozkłady belki w odcisku: {nb}";
                });
            });
        }

        [CommandMethod("RCSMOKE_BEAM_S528PDF", CommandFlags.Modal)]
        public void S528Pdf()
        {
            const string step = "S528PDF";
            Run(step, db => Check(step, "kopia rysunku z layoutem „BELKA 528” (rzut dolny + przekrój A) do PDF", () =>
                SavePdfCopy(db, "BELKA 528", "PLOT 528-529 — RC_SECTION A-A przez belkę (prostopadle) — NA Smart RC, plan belek E5 (do testów)",
                            _b528, _tlA, "belka-528.dwg")));
        }

        [CommandMethod("RCSMOKE_BEAM_S528C", CommandFlags.Modal)]
        public void S528C()
        {
            const string step = "S528C";
            Run(step, db =>
            {
                Check(step, "przekrój sprzed E5 (bez belki i jej prętów) → RC_SECTION_CHECK: NIEAKTUALNY „belka … nieuwzględniona”", () =>
                {
                    var zb = Box(Verts(db, _z528)); var sb = Box(Verts(db, _b528));
                    double y = zb.y0 + 0.3 * (zb.y1 - zb.y0);
                    _tlC = new Point3d(sb.x0, sb.y0 - 16000, 0);
                    DrawSection(db, _b528, new Point2d(sb.x0 - 1000, y), new Point2d(zb.x1 + 3000, y), new Point2d(sb.x0, y + 100), _tlC, "C", inp =>
                    {
                        ((System.Collections.IList)Prop(inp, "Beams")).Clear();
                        var ds = (System.Collections.IList)Prop(inp, "Dists");
                        for (int i = ds.Count - 1; i >= 0; i--) if (Convert.ToString(Prop(ds[i], "Kind"), Inv) == "Beam") ds.RemoveAt(i);
                    });
                    var c = Section(db, "C");
                    var rs = ReasonsOf(c);
                    if (StatusOf(c) != "Nieaktualny" || !rs.Any(r => r.Contains("nieuwzględniona"))) throw new AssertFail("C: " + StatusOf(c) + " — " + string.Join(" | ", rs));
                    return "C: " + string.Join(" | ", rs.Take(3));
                });
                Check(step, "zmiana H strefy 528 (375 → 450) → A NIEAKTUALNY „zmieniona belka … H 375 → 450”", () =>
                {
                    SetZoneHeight(db, _z528, 450);
                    var a = Section(db, "A");
                    var rs = ReasonsOf(a);
                    if (StatusOf(a) != "Nieaktualny" || !rs.Any(r => r.StartsWith("zmieniona belka " + Hx(_z528), StringComparison.Ordinal) && r.Contains("H 375 → 450")))
                        throw new AssertFail("A: " + StatusOf(a) + " — " + string.Join(" | ", rs));
                    return "A: " + string.Join(" | ", rs.Take(2));
                });
                Check(step, "RC_SECTION_UPDATE (SectionUpdater.Update) A i C → aktualne; A: spód belki −450, C: z belką (BEAM w odcisku)", () =>
                {
                    var doc = Application.DocumentManager.MdiActiveDocument;
                    foreach (var letter in new[] { "A", "C" })
                    {
                        var info = Section(db, letter);
                        bool ok = (bool)Call(Updater, "Update", doc, info, _b528);
                        if (!ok) throw new AssertFail("Update " + letter + " = false: " + string.Join(" | ", UpdaterMessages()));
                    }
                    var sb = new StringBuilder();
                    foreach (var letter in new[] { "A", "C" })
                    {
                        var info = Section(db, letter);
                        if (StatusOf(info) != "Aktualny") throw new AssertFail(letter + " po UPDATE: " + StatusOf(info) + " — " + string.Join(" | ", ReasonsOf(info)));
                        var comps = (System.Collections.Generic.IDictionary<string, string>)Prop(Prop(info, "Record"), "Components");
                        if (!comps.ContainsKey("BEAM:" + Hx(_z528))) throw new AssertFail(letter + ": brak składnika BEAM po UPDATE");
                        // beton przekroju (części SLAB): najniższy punkt = TopLeft.Y − 450
                        var tl = (ValueTuple<double, double, double>)Prop(Prop(info, "Record"), "TopLeft");
                        var slabParts = L(info, "Parts").Select(p => (ValueTuple<ObjectId, string, string>)p).Where(p => p.Item2 == "SLAB").Select(p => p.Item1).ToList();
                        double minY = slabParts.SelectMany(id => Verts(db, id)).Min(p => p.Y);
                        if (Math.Abs(minY - (tl.Item2 - 450)) > 0.5) throw new AssertFail($"{letter}: spód betonu {minY - tl.Item2:0.#} (oczekiwano −450)");
                        sb.Append(letter).Append(" aktualny, spód −450; ");
                    }
                    return sb.Append(string.Join(" | ", UpdaterMessages().Where(x => x.Contains("zaktualizowany")))).ToString();
                });
            });
        }

        private static List<string> UpdaterMessages()
            => ((System.Collections.IEnumerable)Plugin.GetType(Updater, true).GetProperty("LastMessages").GetValue(null)).Cast<string>().ToList();

        /// <summary>RC_BEAM [2] H strefy (zapis punktowy pola Real; reszta rekordu bez zmian).</summary>
        private static void SetZoneHeight(Database db, ObjectId zone, double h)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var e = (Entity)tr.GetObject(zone, OpenMode.ForWrite);
                var v = Raw(e, BeamApp);
                v[2] = new TypedValue((int)DxfCode.ExtendedDataReal, h);
                e.XData = new ResultBuffer(v);
                tr.Commit();
            }
        }

        [CommandMethod("RCSMOKE_BEAM_S539", CommandFlags.Modal)]
        public void S539()
        {
            const string step = "S539";
            Run(step, db =>
            {
                object plan = null, ctx = null;
                Check(step, "539: Prepare GA, Mesh (225), pręty belki; RC_SECTION B wzdłuż najdłuższego odcinka belki (oś strefy)", () =>
                {
                    CleanModelSpace(db);
                    ImportGa("PLOT 539");
                    var (b, t) = Views(db);
                    _b539 = b;
                    Mesh(b, t, 225);
                    GenerateBeams(db, b, t);
                    var z = Zones(db).FirstOrDefault(x => XLinkOf(db, x, BeamApp, 6) == Hx(b));
                    var zv = Verts(db, z).Select(p => (p.X, p.Y)).ToList();
                    var ov = Verts(db, b).Select(p => (p.X, p.Y)).ToList();
                    var an = Call("BricsCadRc.Core.BeamGeometry", "Analyze", zv, ov);
                    var seg = L(an, "Segments").Where(s => !(bool)Prop(s, "Manual")).OrderByDescending(s => Dv(s, "FaceHi") - Dv(s, "FaceLo")).First();
                    bool ax = (bool)Prop(seg, "AlongX");
                    double axis = (Dv(seg, "AcrossLo") + Dv(seg, "AcrossHi")) / 2, lo = Dv(seg, "FaceLo") - 1000, hi = Dv(seg, "FaceHi") + 1000;
                    var p1 = ax ? new Point2d(lo, axis) : new Point2d(axis, lo);
                    var p2 = ax ? new Point2d(hi, axis) : new Point2d(axis, hi);
                    var look = ax ? new Point2d(lo, axis + 100) : new Point2d(axis + 100, lo);
                    var sb = Box(Verts(db, b));
                    _tlB = new Point3d(sb.x0, sb.y0 - 16000, 0);   // poniżej długich odnośników opisów rzutu
                    (ctx, plan) = DrawSection(db, b, p1, p2, look, _tlB, "B");
                    var beams = L(Prop(ctx, "Input"), "Beams");
                    if (beams.Count < 1) throw new AssertFail("brak pasa belki na linii cięcia");
                    // E4b: H 300 → 0 prętów bocznych; pręty w nibie wzdłuż obu nóg (górą H10, dołem H16)
                    if (Dists(db, "BS").Count != 0) throw new AssertFail("BS przy H 300: " + Dists(db, "BS").Count);
                    var bn = Dists(db, "BN");
                    if (!bn.Any(x => x.mark.EndsWith(" BEAM NIB T")) || !bn.Any(x => x.mark.EndsWith(" BEAM NIB B"))) throw new AssertFail("BN: " + string.Join(" | ", bn.Select(x => x.mark)));
                    return $"odcinek {(ax ? "X" : "Y")} {Dv(seg, "FaceLo"):0}..{Dv(seg, "FaceHi"):0} oś {axis:0.#}; pasy: " +
                           string.Join(", ", beams.Select(x => $"{Dv(x, "S0"):0}..{Dv(x, "S1"):0} H={Dv(x, "Height"):0}"));
                });
                Check(step, "B: pręty belki w widoku — dół (−242) i góra (−78) liniami, strzemiona 51 jako pionowe ramiona (−255…−65), profil −300", () =>
                {
                    if (plan == null) throw new AssertFail("brak planu");
                    var pos = BeamPositions(db);
                    var bars = L(plan, "Bars").Where(b => Convert.ToString(Prop(b, "Kind"), Inv) == "Beam").ToList();
                    List<(double U, double V)> P(object b) => ((System.Collections.IEnumerable)Prop(b, "Path")).Cast<object>().Select(p => (ValueTuple<double, double>)p).Select(p => (p.Item1, p.Item2)).ToList();
                    int nb = 0, nt = 0, nl = 0;
                    foreach (var b in bars)
                    {
                        var pth = P(b);
                        int pnr = int.Parse((string)Prop(b, "PosNr"), Inv);
                        if (!pos.TryGetValue(pnr, out var tp)) throw new AssertFail("pręt w widoku poz. " + pnr + " nie jest pozycją belki");
                        if (tp.code == "BL")
                        {
                            if (pth.Count != 2 || Math.Abs(pth[0].U - pth[1].U) > 0.01 || Math.Abs(pth.Min(p => p.V) + 255) > 0.01 || Math.Abs(pth.Max(p => p.V) + 65) > 0.01)
                                throw new AssertFail("strzemię " + pnr + ": " + string.Join(" ", pth.Select(p => $"{p.U:0.#},{p.V:0.#}")));
                            nl++;
                        }
                        else if (tp.shape == "00" && (tp.code == "BB" || tp.code == "BT"))
                        {
                            double v = tp.code == "BT" ? -78 : -242;
                            if (pth.Any(p => Math.Abs(p.V - v) > 0.01)) throw new AssertFail($"pręt {pnr} ({tp.code}) v={pth[0].V:0.#}, oczekiwano {v}");
                            if (tp.code == "BT") nt++; else nb++;
                        }
                    }
                    if (nb < 1 || nt < 1 || nl < 10) throw new AssertFail($"w widoku: dół {nb}, góra {nt}, strzemiona {nl}");
                    var prof = L(plan, "Segments").Where(s => Prop(s, "Prof") != null).SelectMany(s => L(s, "Prof")).Select(p => (ValueTuple<double, double, double, double>)p).ToList();
                    if (!prof.Any(p => Math.Abs(p.Item3 + 300) < 1e-6 && p.Item4 == 0)) throw new AssertFail("brak trzonu −300: " + string.Join(" ", prof));
                    int lbl = L(plan, "Labels").Count(l => pos.Any(kv => kv.Value.code == "BL" && kv.Key.ToString("00", Inv) == (string)Prop(l, "Text")));
                    if (lbl != 1) throw new AssertFail("opisy strzemion: " + lbl);
                    return $"dół {nb} linii (−242), góra {nt} (−78), strzemiona {nl} pionowych ramion, opis strzemion 1";
                });
                Check(step, "RC_SECTION_CHECK: B aktualny, BEAM w odcisku; RC_SECTION_UPDATE bez zmian rysunku — nadal aktualny", () =>
                {
                    var bInfo = Section(db, "B");
                    if (StatusOf(bInfo) != "Aktualny") throw new AssertFail("B: " + StatusOf(bInfo) + " — " + string.Join(" | ", ReasonsOf(bInfo)));
                    var comps = (System.Collections.Generic.IDictionary<string, string>)Prop(Prop(bInfo, "Record"), "Components");
                    if (!comps.Keys.Any(k => k.StartsWith("BEAM:", StringComparison.Ordinal))) throw new AssertFail("brak BEAM w odcisku B");
                    bool ok = (bool)Call(Updater, "Update", Application.DocumentManager.MdiActiveDocument, bInfo, _b539);
                    var b2 = Section(db, "B");
                    if (!ok || StatusOf(b2) != "Aktualny") throw new AssertFail("UPDATE B: " + ok + " / " + StatusOf(b2));
                    return "B aktualny (przed i po UPDATE)";
                });
            });
        }

        [CommandMethod("RCSMOKE_BEAM_S539BBS", CommandFlags.Modal)]
        public void S539Bbs()
        {
            const string step = "S539BBS";
            Run(step, db =>
            {
                object bbs = null; List<object> rows = null;
                var pos = BeamPositions(db);
                Check(step, "RC_BBS (ReadBars): pozycje belki — BB / BL < 100 (dół), BT 101+ (góra), kształty 00 / 11 / 21 / 51, długość cięcia BS 8666:2020, sztuki = suma rozkładów; bez ostrzeżeń „długość wg BS 8666:2020” dla belki", () =>
                {
                    bbs = Call("BricsCadRc.Core.BbsDrawingReader", "ReadBars", db);
                    rows = L(bbs, "Rows");
                    var warns = L(bbs, "Warnings").Cast<string>().ToList();
                    var sb = new StringBuilder();
                    var shapes = new HashSet<string>();
                    foreach (var kv in pos.OrderBy(k => k.Key))
                    {
                        var r = rows.Where(x => (int)Prop(x, "BarMark") == kv.Key).ToList();
                        if (r.Count != 1) throw new AssertFail($"poz. {kv.Key}: wierszy {r.Count}");
                        var t = kv.Value;
                        bool top = Convert.ToString(Prop(r[0], "Layer"), Inv) == "Top";
                        if (top != (t.code == "BT")) throw new AssertFail($"poz. {kv.Key} ({t.code}) w warstwie {Prop(r[0], "Layer")}");
                        double len = Convert.ToDouble(Call("BricsCadRc.Core.BarLengthRules", "CuttingLength", t.shape, (double)t.dia, t.a, t.b, t.c, 0.0, 0.0), Inv);
                        if (Math.Abs(Dv(r[0], "Length") - len) > 0.01) throw new AssertFail($"poz. {kv.Key}: L {Dv(r[0], "Length")} ≠ {len}");
                        int cnt = Ms(db, e => e is BlockReference && Raw(e, DistApp) is TypedValue[] v && v.Length > 18 && PosNr(Convert.ToString(v[1].Value, Inv)) == kv.Key)
                                  .Sum(id => EffCount(RawOf(db, id, DistApp)));
                        if ((int)Prop(r[0], "Total") != cnt) throw new AssertFail($"poz. {kv.Key}: sztuk {Prop(r[0], "Total")} ≠ suma rozkładów {cnt}");
                        var lw = warns.Where(w => w.StartsWith($"Pozycja {kv.Key:00} (kształt {t.shape}): długość wg BS 8666:2020", StringComparison.Ordinal)).ToList();
                        if (lw.Count > 0) throw new AssertFail($"ostrzeżenie długości dla poz. {kv.Key} ({t.code} {t.shape} A={t.a} B={t.b} C={t.c}): {lw[0]}");
                        shapes.Add(t.shape);
                        sb.Append($"{kv.Key:00} {t.code} {t.shape} H{t.dia} L{len:0} ×{cnt}; ");
                    }
                    foreach (var s in new[] { "00", "11", "21", "51" }) if (!shapes.Contains(s)) throw new AssertFail("brak kształtu " + s + " wśród pozycji belki");
                    return sb.ToString();
                });
                Check(step, "RC_SCHEDULE (BuildSchedule): pozycje belki — te same sztuki i długości cięcia", () =>
                {
                    var entries = ((System.Collections.IEnumerable)Call("BricsCadRc.Core.BarScheduleEngine", "BuildSchedule", db, new List<string>())).Cast<object>().ToList();
                    foreach (var kv in pos)
                    {
                        var e = entries.Where(x => int.TryParse((string)Prop(x, "PosNr"), out int p) && p == kv.Key).ToList();
                        int tot = e.Sum(x => (int)Prop(x, "TotalCount"));
                        var r = rows.First(x => (int)Prop(x, "BarMark") == kv.Key);
                        if (tot != (int)Prop(r, "Total")) throw new AssertFail($"poz. {kv.Key}: RC_SCHEDULE {tot} ≠ RC_BBS {Prop(r, "Total")}");
                        if (e.Any(x => Math.Abs(Dv(x, "CuttingLength") - Dv(r, "Length")) > 0.01)) throw new AssertFail($"poz. {kv.Key}: długość RC_SCHEDULE ≠ RC_BBS");
                    }
                    return "pozycji belki: " + pos.Count;
                });
                Check(step, "RC_COUNT_BBS (CountAll): masa = RC_BBS (wszystkie wiersze, także pozycje belki)", () =>
                {
                    var cnt = ((System.Collections.IEnumerable)Call("BricsCadRc.Core.BbsCounter", "CountAll", db, new List<string>())).Cast<object>().ToList();
                    double mCount = cnt.Sum(x => Dv(x, "TotalMassKg")), mBbs = rows.Sum(x => Dv(x, "MassKg"));
                    double mBeamC = cnt.Where(x => int.TryParse((string)Prop(x, "Position"), out int p) && pos.ContainsKey(p)).Sum(x => Dv(x, "TotalMassKg"));
                    double mBeamB = rows.Where(x => pos.ContainsKey((int)Prop(x, "BarMark"))).Sum(x => Dv(x, "MassKg"));
                    if (Math.Abs(mCount - mBbs) > 0.01 || Math.Abs(mBeamC - mBeamB) > 0.01) throw new AssertFail($"masa COUNT {mCount:0.00} / BBS {mBbs:0.00}; belka {mBeamC:0.00} / {mBeamB:0.00}");
                    return $"masa {mCount:0.00} kg (belka {mBeamB:0.00} kg)";
                });
                Check(step, "BBS .xls (BbsXlsGenerator, szablon wbudowany): liczba pozycji = RC_BBS, pozycje belki na arkuszu dolnym / górnym, masa z arkusza = RC_COUNT_BBS", () =>
                {
                    var bt = Plugin.GetType("BricsCadRc.Core.BbsLayoutInfo", true);
                    var lb = Activator.CreateInstance(bt); bt.GetProperty("LayoutName").SetValue(lb, "TEST-B"); bt.GetProperty("DrawingNumber").SetValue(lb, "SO000-RC0001");
                    var ltop = Activator.CreateInstance(bt); bt.GetProperty("LayoutName").SetValue(ltop, "TEST-T"); bt.GetProperty("DrawingNumber").SetValue(ltop, "SO000-RC0002");
                    var list = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(bt));
                    list.Add(lb); list.Add(ltop);
                    var gctx = Call("BricsCadRc.Core.BbsGenerationContext", "BuildInitial", list);
                    var asg = L(gctx, "Assignments");
                    var at = Plugin.GetType("BricsCadRc.Core.BbsLayerAssignment", true);
                    asg[0].GetType().GetProperty("Assignment").SetValue(asg[0], Enum.Parse(at, "Bottom"));
                    asg[1].GetType().GetProperty("Assignment").SetValue(asg[1], Enum.Parse(at, "Top"));
                    string xls = Path.Combine(OutDir, "beam-bbs-539.xls");
                    if (File.Exists(xls)) File.Delete(xls);
                    var gen = Call("BricsCadRc.Core.BbsXlsGenerator", "Generate", gctx, Prop(bbs, "Rows"), null, xls);
                    if (!(bool)Prop(gen, "Success")) throw new AssertFail("Generate: " + Prop(gen, "Message"));
                    var xr = ReadXlsRows(xls);
                    if (xr.Count != rows.Count) throw new AssertFail($"wierszy w arkuszu {xr.Count} ≠ RC_BBS {rows.Count}");
                    foreach (var kv in pos)
                    {
                        var x = xr.Where(r => r.mark == kv.Key).ToList();
                        if (x.Count != 1) throw new AssertFail("arkusz: poz. " + kv.Key + " wierszy " + x.Count);
                        if (x[0].top != (kv.Value.code == "BT")) throw new AssertFail($"arkusz: poz. {kv.Key} w sekcji {(x[0].top ? "TOP" : "BOTTOM")}");
                    }
                    double mx = xr.Sum(r => r.total * r.length / 1000.0 * LinearMass(r.dia));
                    var cnt = ((System.Collections.IEnumerable)Call("BricsCadRc.Core.BbsCounter", "CountAll", db, new List<string>())).Cast<object>().ToList();
                    double mc = cnt.Sum(x => Dv(x, "TotalMassKg"));
                    if (Math.Abs(mx - mc) > 0.05) throw new AssertFail($"masa z arkusza {mx:0.00} ≠ RC_COUNT_BBS {mc:0.00}");
                    return $"{Prop(gen, "Message")} wierszy {xr.Count}, masa {mx:0.00} kg = COUNT; plik {xls}";
                });
            });
        }

        /// <summary>Liczba prętów rozkładu do BBS: [27] CountDisplay (−1 = brak) albo [3] Count.</summary>
        private static int EffCount(TypedValue[] v)
        {
            int c = Convert.ToInt32(v[3].Value, Inv);
            if (v.Length > 27 && v[27].Value is short cd && cd >= 0) return cd;
            return c;
        }

        private static double LinearMass(int dia) => Convert.ToDouble(Call("BricsCadRc.Core.BarData", "GetLinearMass", dia), Inv);

        /// <summary>
        /// Wiersze prętów z pliku .xls (NPOI wtyczki — przez refleksję na wczytanym zestawie): kolumna B = pozycja (liczba), C = „H16”,
        /// F = sztuki razem, G = długość; sekcja z kolumny A („BOTTOM LAYER” / „TOP LAYER”).
        /// </summary>
        private static List<(int mark, int dia, int total, double length, bool top)> ReadXlsRows(string path)
        {
            var npoi = AppDomain.CurrentDomain.GetAssemblies().First(a => a.GetName().Name == "NPOI");
            var wbT = npoi.GetType("NPOI.HSSF.UserModel.HSSFWorkbook", true);
            var res = new List<(int, int, int, double, bool)>();
            using (var fs = File.OpenRead(path))
            {
                var wb = Activator.CreateInstance(wbT, new object[] { fs });
                int n = (int)wbT.GetProperty("NumberOfSheets").GetValue(wb);
                for (int s = 0; s < n; s++)
                {
                    var sh = wbT.GetMethod("GetSheetAt").Invoke(wb, new object[] { s });
                    int last = (int)sh.GetType().GetProperty("LastRowNum").GetValue(sh);
                    bool top = false;
                    for (int r = 0; r <= last; r++)
                    {
                        var row = sh.GetType().GetMethod("GetRow").Invoke(sh, new object[] { r });
                        if (row == null) continue;
                        object Cell(int c) => row.GetType().GetMethod("GetCell", new[] { typeof(int) }).Invoke(row, new object[] { c });
                        string Str(object cell) => cell == null ? "" : cell.ToString();
                        string a = Str(Cell(0)).Trim().ToUpperInvariant();
                        if (a == "TOP LAYER") top = true; else if (a == "BOTTOM LAYER") top = false;
                        string sizeTxt = Str(Cell(2)).Trim();
                        if (!sizeTxt.StartsWith("H", StringComparison.Ordinal) || !int.TryParse(sizeTxt.Substring(1), NumberStyles.Integer, Inv, out int dia)) continue;
                        if (!double.TryParse(Str(Cell(1)), NumberStyles.Float, Inv, out double mk)) continue;
                        double.TryParse(Str(Cell(5)), NumberStyles.Float, Inv, out double tot);
                        double.TryParse(Str(Cell(6)), NumberStyles.Float, Inv, out double len);
                        res.Add(((int)mk, dia, (int)Math.Round(tot), len, top));
                    }
                }
            }
            return res;
        }

        [CommandMethod("RCSMOKE_BEAM_S539PDF", CommandFlags.Modal)]
        public void S539Pdf()
        {
            const string step = "S539PDF";
            Run(step, db => Check(step, "kopia rysunku z layoutem „BELKA 539” (rzut dolny + przekrój B) do PDF", () =>
                SavePdfCopy(db, "BELKA 539", "PLOT 539 — RC_SECTION B-B wzdłuż belki — NA Smart RC, plan belek E5 (do testów)",
                            _b539, _tlB, "belka-539.dwg")));
        }

        /// <summary>
        /// Layout do PDF (A3 poziomo, Print As PDF.pc3): rzutnia rzutu dolnego płyty (z belką i znacznikami przekroju) i rzutnia przekroju
        /// (tytuł i części przekroju — zakres z rekordu); pozostałe layouty usunięte; kopia rysunku (SaveAs) w folderze RC_BEAM_PDF_DIR
        /// (domyślnie katalog wyników). Rysunek testowy NIE jest zapisywany (koniec skryptu: _QUIT _Y).
        /// </summary>
        private static string SavePdfCopy(Database db, string layoutName, string title, ObjectId slab, Point3d tl, string file)
        {
            string dir = Environment.GetEnvironmentVariable("RC_BEAM_PDF_DIR");
            if (string.IsNullOrWhiteSpace(dir)) dir = OutDir;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, file);
            if (File.Exists(path)) File.Delete(path);

            // zakres przekroju: części (poza znacznikami na rzutach) i tytuł przekroju wstawionego w tl
            var infos = CheckAll(db).Where(i => !((ObjectId)Prop(i, "TitleId")).IsNull).ToList();
            var sec = infos.FirstOrDefault(i =>
            {
                var t = (ValueTuple<double, double, double>)Prop(Prop(i, "Record"), "TopLeft");
                return Math.Abs(t.Item1 - tl.X) < 1 && Math.Abs(t.Item2 - tl.Y) < 1;
            }) ?? throw new AssertFail("brak przekroju wstawionego w " + tl);
            Extents3d secExt = ExtOf(db, (ObjectId)Prop(sec, "TitleId"));
            foreach (var p in L(sec, "Parts").Select(p => (ValueTuple<ObjectId, string, string>)p).Where(p => p.Item2 != "MARK" && p.Item2 != "MARKTXT"))
                secExt.AddExtents(ExtOf(db, p.Item1));
            var sb = Box(Verts(db, slab));
            var planExt = new Extents3d(new Point3d(sb.x0 - 1500, sb.y0 - 1500, 0), new Point3d(sb.x1 + 1500, sb.y1 + 1500, 0));

            var lm = LayoutManager.Current;
            if (LayoutNames(db).Contains(layoutName)) lm.DeleteLayout(layoutName);
            var lid = lm.CreateLayout(layoutName);
            lm.CurrentLayout = layoutName;
            string media = "";
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lay = (Layout)tr.GetObject(lid, OpenMode.ForWrite);
                var psv = PlotSettingsValidator.Current;
                try
                {
                    psv.SetPlotConfigurationName(lay, "Print As PDF.pc3", null);
                    psv.RefreshLists(lay);
                    var all = psv.GetCanonicalMediaNameList(lay).Cast<string>().ToList();
                    media = all.FirstOrDefault(m => m.IndexOf("A3", StringComparison.OrdinalIgnoreCase) >= 0 && m.Contains("420")) ?? all.FirstOrDefault() ?? "";
                    if (media.Length > 0) psv.SetCanonicalMediaName(lay, media);
                    psv.SetPlotPaperUnits(lay, PlotPaperUnit.Millimeters);
                    psv.SetPlotRotation(lay, lay.PlotPaperSize.X >= lay.PlotPaperSize.Y ? PlotRotation.Degrees000 : PlotRotation.Degrees090);
                    psv.SetCurrentStyleSheet(lay, "speedeck-mono.ctb");
                }
                catch (System.Exception ex) { Msg($"\n{Prefix} Ustawienia wydruku: {ex.Message}\n"); }
                double W = Math.Max(lay.PlotPaperSize.X, lay.PlotPaperSize.Y), H = Math.Min(lay.PlotPaperSize.X, lay.PlotPaperSize.Y);
                if (W < 100 || H < 100) { W = 420; H = 297; }
                var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForWrite);
                // domyślne rzutnie (poza rzutnią arkusza) — usunięte
                var vps = btr.Cast<ObjectId>().Where(id => tr.GetObject(id, OpenMode.ForRead) is Viewport).ToList();
                foreach (var id in vps.Skip(1)) tr.GetObject(id, OpenMode.ForWrite).Erase();
                void Vp(double x0, double y0, double x1, double y1, Extents3d ext)
                {
                    var vp = new Viewport();
                    btr.AppendEntity(vp); tr.AddNewlyCreatedDBObject(vp, true);
                    double w = x1 - x0, h = y1 - y0;
                    vp.CenterPoint = new Point3d((x0 + x1) / 2, (y0 + y1) / 2, 0);
                    vp.Width = w; vp.Height = h;
                    double ew = ext.MaxPoint.X - ext.MinPoint.X, eh = ext.MaxPoint.Y - ext.MinPoint.Y;
                    vp.ViewCenter = new Point2d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2);
                    vp.ViewHeight = Math.Max(eh, ew * h / w) * 1.06;
                    vp.On = true;
                }
                // u dołu przekrój na całą szerokość; u góry rzut dolny (z belką i znacznikami) i powiększenie lewego końca przekroju (2,5 m)
                double m = 12;
                double sw = W - 2 * m, sew = secExt.MaxPoint.X - secExt.MinPoint.X, seh = secExt.MaxPoint.Y - secExt.MinPoint.Y;
                double hs = Math.Max(40, Math.Min(H * 0.42, sw * seh / Math.Max(1, sew) * 1.15));
                Vp(m, m, W - m, m + hs, secExt);
                double yt = m + hs + m;
                Vp(m, yt, W * 0.45, H - 22, planExt);
                var det = new Extents3d(secExt.MinPoint, new Point3d(Math.Min(secExt.MaxPoint.X, secExt.MinPoint.X + 2500), secExt.MaxPoint.Y, 0));
                Vp(W * 0.45 + m, yt, W - m, H - 22, det);
                var txt = new MText { Contents = title, TextHeight = 5, Location = new Point3d(m, H - 10, 0), Attachment = AttachmentPoint.TopLeft, Width = W - 2 * m };
                btr.AppendEntity(txt); tr.AddNewlyCreatedDBObject(txt, true);
                tr.Commit();
            }
            lm.CurrentLayout = "Model";
            foreach (var n in LayoutNames(db).Where(n => n != "Model" && n != layoutName).ToList())
            {
                try { lm.DeleteLayout(n); }
                catch (System.Exception ex) { Msg($"\n{Prefix} DeleteLayout {n}: {ex.Message}\n"); }
            }
            db.SaveAs(path, DwgVersion.Current);
            if (!File.Exists(path)) throw new AssertFail("brak kopii " + path);
            return $"{path} (layout „{layoutName}”, papier {media}); przekrój {secExt.MinPoint.X:0},{secExt.MinPoint.Y:0}..{secExt.MaxPoint.X:0},{secExt.MaxPoint.Y:0}";
        }

        private static Extents3d ExtOf(Database db, ObjectId id) => With(db, id, e => e.GeometricExtents);

        private static List<string> LayoutNames(Database db)
        {
            var res = new List<string>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var dict = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);
                foreach (DBDictionaryEntry e in dict) res.Add(e.Key);
                tr.Commit();
            }
            return res;
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
            foreach (var code in new[] { "BB", "BL", "BT", "BS", "BN" })
                foreach (var d in Dists(db, code))
                {
                    if (d.pos <= 3) bad.Add(d.mark);
                    if (code == "BT" ? d.pos < 101 || d.pos >= 500 : d.pos < 4 || d.pos >= 100) bad.Add(d.mark);
                }
            if (bad.Count > 0) throw new AssertFail("numery: " + string.Join(", ", bad));
            return "numery OK";
        }

        /// <summary>
        /// E4: linie prętów bloku rozkładu prętów L / U-barów leżą w strefie belki (dół — strefa rzutu dolnego, góra — górnego; wystarczy
        /// dowolna strefa); symbol haka: podstawa w strefie i punkt 100 mm od podstawy w kierunku zagięcia (składowa prostopadła haka)
        /// też w strefie — hak wskazuje drugie ramię (rząd przy licu: zły kierunek wychodzi poza strefę). Kreska haka rzędu wewnętrznego
        /// narożnika L może wyjść ~13 mm za wewnętrzny narożnik strefy — jej koniec nie jest sprawdzany. Liczba haków = liczba prętów.
        /// </summary>
        private static string BentInsideZones(Database db)
        {
            var zones = Zones(db).Select(z => Verts(db, z)).ToList();
            int lines = 0, hooks = 0, bars = 0;
            foreach (var code in new[] { "BB", "BT" })
                foreach (var x in Dists(db, code).Where(d => d.shape == "11" || d.shape == "21"))
                {
                    bars += x.count;
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        var br = (BlockReference)tr.GetObject(x.id, OpenMode.ForRead);
                        var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                        foreach (ObjectId cid in btr)
                        {
                            if (!(tr.GetObject(cid, OpenMode.ForRead) is Line ln)) continue;
                            var p = ln.StartPoint.TransformBy(br.BlockTransform);
                            var q = ln.EndPoint.TransformBy(br.BlockTransform);
                            lines++;
                            var pts = new[] { p, q };
                            if (Math.Abs(p.X - q.X) > 1 && Math.Abs(p.Y - q.Y) > 1)
                            {
                                hooks++;
                                // składowa prostopadła haka: oś inna niż oś pręta (pręty wzdłuż X → Y i odwrotnie)
                                bool barsX = Convert.ToString(RawOf(db, x.id, DistApp)[6].Value, Inv) != "Y";
                                double sx = barsX ? 0 : Math.Sign(q.X - p.X), sy = barsX ? Math.Sign(q.Y - p.Y) : 0;
                                pts = new[] { p, new Point3d(p.X + 100 * sx, p.Y + 100 * sy, 0) };
                            }
                            foreach (var pt in pts)
                                if (!zones.Any(z => InPoly(z, pt.X, pt.Y)))
                                {
                                    var all = new List<string>();
                                    foreach (ObjectId c2 in btr)
                                        if (tr.GetObject(c2, OpenMode.ForRead) is Line l2)
                                        {
                                            var a2 = l2.StartPoint.TransformBy(br.BlockTransform); var b2 = l2.EndPoint.TransformBy(br.BlockTransform);
                                            all.Add($"({a2.X:0.#},{a2.Y:0.#})-({b2.X:0.#},{b2.Y:0.#})");
                                        }
                                    var v = RawOf(db, x.id, DistApp);
                                    var zb = zones.Select(z => $"[{z.Min(w => w.X):0.#}..{z.Max(w => w.X):0.#} × {z.Min(w => w.Y):0.#}..{z.Max(w => w.Y):0.#}]");
                                    throw new AssertFail($"{x.mark}: punkt linii ({pt.X:0.#}, {pt.Y:0.#}) poza strefą belki; linie: {string.Join(" ", all)}; " +
                                                         $"dir={v[6].Value} side={v[13].Value} symdir={v[14].Value} skewS={v[26].Value} skewE={v[25].Value}; strefy {string.Join(" ", zb)}");
                                }
                        }
                        tr.Commit();
                    }
                }
            if (hooks != bars) throw new AssertFail($"symbole haka: {hooks}, pręty L / U-bary: {bars}");
            return $"linie prętów L / U-barów w strefie ({lines}, haki {hooks})";
        }

        private static bool InPoly(List<Point2d> v, double x, double y)
        {
            bool inside = false;
            for (int i = 0, j = v.Count - 1; i < v.Count; j = i++)
                if ((v[i].Y > y) != (v[j].Y > y) && x < (v[j].X - v[i].X) * (y - v[i].Y) / (v[j].Y - v[i].Y) + v[i].X) inside = !inside;
            return inside;
        }

        // ================================================================
        // Pomocnicze — rysunek
        // ================================================================

        private sealed class D { public ObjectId id; public string mark, shape; public int count, pos; public double length; }

        private static List<D> Dists(Database db, string code)
        {
            var res = new List<D>();
            foreach (var id in Ms(db, e => e is BlockReference && Raw(e, DistApp) is TypedValue[] v && v.Length > 8 && Convert.ToString(v[2].Value, Inv) == code))
            {
                var v = RawOf(db, id, DistApp);
                string mark = Convert.ToString(v[1].Value, Inv);
                res.Add(new D { id = id, mark = mark, count = Convert.ToInt32(v[3].Value, Inv), length = Convert.ToDouble(v[8].Value, Inv), pos = PosNr(mark),
                                shape = v.Length > 12 ? Convert.ToString(v[12].Value, Inv) : "00" });
            }
            return res;
        }

        private static List<ObjectId> AllBeamDists(Database db)
            => Ms(db, e => e is BlockReference && Raw(e, DistApp) is TypedValue[] v && v.Length > 2
                           && new[] { "BB", "BT", "BL", "BS", "BN" }.Contains(Convert.ToString(v[2].Value, Inv)));

        private static List<ObjectId> Zones(Database db)
            => Ms(db, e => e is Polyline && Raw(e, BeamApp) != null);

        private static List<(string mark, string shape, double a, double b, bool isBlock, double c, int pos)> Templates(Database db)
        {
            var res = new List<(string, string, double, double, bool, double, int)>();
            foreach (var id in Ms(db, e => Raw(e, "RC_SINGLE_BAR") is TypedValue[] v && v.Length > 7
                                           && new[] { "BB", "BT", "BL", "BS", "BN" }.Contains(Convert.ToString(v[7].Value, Inv))))
            {
                var v = RawOf(db, id, "RC_SINGLE_BAR");
                bool isBlock = With(db, id, e => e is BlockReference);
                string mark = Convert.ToString(v[1].Value, Inv);
                res.Add((mark, Convert.ToString(v[3].Value, Inv),
                         Convert.ToDouble(v[4].Value, Inv), Convert.ToDouble(v[5].Value, Inv), isBlock,
                         Convert.ToDouble(v[6].Value, Inv), PosNr(mark)));
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

        private static string Seq2(string what, IEnumerable<string> act, IEnumerable<string> exp)
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
