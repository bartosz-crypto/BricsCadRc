// BEAM — testy dymne belek / pogrubień (docs/PLAN_BELKI_2026-10-10.md E1–E6; K2 + K3 planu praktyki biura
// docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md) w BricsCAD V25. Uruchamiane skryptem planc-beam.scr (run-planc-beam.ps1). Komendy RCSMOKE_BEAM_*.
// Dostęp do wtyczki wyłącznie refleksją. Dane: GA Holbrook P02 (DXF, tylko odczyt), folder: zmienna RC_BEAM_GA.
//
// D526  Prepare GA 526 (bez stref) → zrzut DrawingDump (pełny, z warstwami) = zrzut wtyczki bazowej (bit w bit).
// D319 / D528 / D539  Prepare GA + Mesh (225) — K7 (E2, POPRAWKI_WZORCA 30): siatka przy belce wg praktyki biura (Holbrook ręczny
//       528 / 539 / 319): pręty równoległe dołu od 40 + d od lica wewnętrznego, góry od s/2; prostopadłe dołu i góry do lica wewnętrznego
//       belki (decyzja 2026-10-11 — nie wchodzą w belkę); krawędzie przy belce bez UB 01/02; UB 03 z prętów dołu (rozstaw 100…250);
//       T IN NIB przy belce — 2 pręty w nibie, opis bez rozstawu (asercje + podsumowanie rozkładów beam-e2-<płyta>.txt do porównania z ręcznym).
//       Zrzut bez RC-BEAM różni się od bazy (HEAD + K1) — oczekiwane (informacja); odcinki „detal ręczny” (319, skos) — siatka bez zmian.
// K2 / K3 (domyślne, do potwierdzenia przez Bartka): strzemiona H12 51, A = H − 35 − 50 (A1), pierwsze 51 od lica (także przy U-barach),
//       niezależnie od UB 03; pręty podłużne dół = góra jedną pozycją BM „2x4 H16-nn-150 B+T” (seria dolna, rzut dolny, CountDisplay 8),
//       zakład 40·Ø (H16 650), koniec pręta prostego 61 od lica; pionowe U-bary BU kształt 21 „4 H16-nn-150 UB” (po 1 w kolumnie):
//       koniec — A = Ceil25(650 + 90) = 750, B = A strzemienia − 2·12 (528: 265, 539: 190, 319: 340), narożnik L — każda noga
//       (noga Y: B − 35), złącze T — belka dochodząca; brak prętów L / poziomych U / BT na rzucie górnym; BS (tryb B3) i BN górą — brak
//       dla 300 / 375 / 450; BN dołem H10.
// G319  strefa + H = 450; lewa noga ze skosem — „detal ręczny”; góra + prawa: narożnik L (U-bary obu nóg), koniec wewnętrzny prawej nogi.
// G539  H = 300, L + L; format Blok (szablony = pręty-bloki): strzemię 51 A = 215, B = 510; narożniki L (4 rozkłady U-barów, noga Y
//       B 155), końce (wewnętrzny + przy krawędzi) U-bary 750 / 190 / 750.
// G528  H = 375, prosta: strzemiona 52 (pierwsze 51 od lica), BM 5500 + 5250 (2x4), U-bary na obu końcach 750 / 265 / 750, numery 04–99.
// G528R ponowne generowanie — te same liczby, bez duplikatów, szablony ponownie użyte; G528U / G528D — UNDO / REDO.
// G528C / G528M / G528A / G528P — COPY / MIRROR / -ARRAY / PASTECLIP rzutu dolnego: znaczniki klonów → strefa klonu (CloneRemap).
// G528GA ponowne Prepare GA 528 — strefy zastąpione, pręty belek starych stref usunięte.
// G528UB Mesh (225) + belka: strzemiona niezależnie od UB 03 (K2): pierwsze 51 od lica, rozstaw ≤ 200.
// M528  belka → Mesh: rozkłady siatki (bez kodów belki) = zrzut D528 tego przebiegu (Mesh bez belki; numery i odwołania znormalizowane).
// S528  RC_SECTION A prostopadle do belki 528: kropki BM dół (−305) i góra (−55), strzemię 51 w widoku (−319 … −41), BN dołem H10.
//       Decyzja 2026-10-11 (SectionPlanner.Version 4): bez kropek siatki płyty w trzonie belki (S528, C17 — asercja), T IN NIB 2 kropki w nibie.
// S528C przekrój „sprzed E5” → NIEAKTUALNY; H 375 → 450 → „zmieniona belka”; RC_SECTION_UPDATE → aktualne.
// S539  RC_SECTION B wzdłuż belki 539: BM w widoku na obu poziomach (−230 / −55), strzemiona (−244 … −41), profil −300.
// S539BBS RC_BBS / RC_SCHEDULE / RC_COUNT_BBS / .xls: wszystkie pozycje belki w serii dolnej (BM / BU / BL / BN; 00 / 21 / 51).
// G528V1 rekord RC_BEAM v1 (wersja testowa) → generowanie: komunikat migracji, rekord zapisany jako v2, strzemiona H12.
// K4    G528S: H 528 → 600 — pręty boczne BS H12 „2x2 H12-nn SIDE” i UB SIDE (BC, kształt 21, B 485) „2x1 H12-nn UB SIDE” na końcach.
// K5    S528: B IN NIB (BN dołem H10 „1 H10-nn B IN NIB”) — numer i długości jak rozkład siatki B2 najbliższy belce (Mesh przed Beam).
// K6    cofnięte 2026-10-11 (przekrój jak e1644bd, SectionPlanner.Version 1): BU i UB SIDE (BC) pominięte w przekroju, bez odgięć 45°.
// Z1–Z6 2026-10-11 (SectionPlanner.Version 3): symbole zakończeń prętów S1 (kreska 45° 30 mm do wnętrza), zakład odsunięty o 25 mm.
// K8    C17 / C3: Prepare GA C12 (kopie GA P06 bez ramek arkusza SD-PILED-RAFT; folder RC_BEAM_C12) — strefy z opisów „xxxTHK”
//       (PLOT 17: 450; PLOT 3: 600 / 675 / 975), C17PDF — kopia z przekrojem przez belkę do PDF; S319 / S319PDF — przekrój przez prawą nogę 319.

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
        private const string Title = "BEAM — testy dymne belek / pogrubień (E1–E6, K2 + K3)";
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
            "S528", "S528PDF", "S528C", "S539", "S539BBS", "S539PDF", "G528V1", "G528S", "S319", "S319PDF", "C17", "C17PDF", "C3",
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
            Run(step, db => MeshE2(step, db, "Plot 319", "319mesh", "319"));
        }

        [CommandMethod("RCSMOKE_BEAM_D528", CommandFlags.Modal)]
        public void D528() => DumpMesh("D528", "PLOT 528-529", "528mesh");

        [CommandMethod("RCSMOKE_BEAM_D539", CommandFlags.Modal)]
        public void D539() => DumpMesh("D539", "PLOT 539", "539mesh");

        private static void DumpMesh(string step, string key, string name)
            => Run(step, db => MeshE2(step, db, key, name, key.Replace("PLOT ", "")));

        /// <summary>
        /// K7 (E2): Prepare GA + Mesh (225); zrzut bez RC-BEAM zapisany (beam-dump-&lt;name&gt;-&lt;tag&gt;.txt, w przebiegu bazowym — baza
        /// HEAD + K1 bez E2; w nowym — informacja o różnicy względem bazy); asercje siatki przy belce (<see cref="E2Checks"/>) i
        /// podsumowanie rozkładów siatki do porównania z ręcznym BBS (beam-e2-&lt;plot&gt;.txt).
        /// </summary>
        private static void MeshE2(string step, Database db, string key, string name, string plot)
        {
            ObjectId b = ObjectId.Null, t = ObjectId.Null;
            List<string> lines = null;
            Check(step, "Prepare GA " + key + " + Mesh (225); zrzut bez RC-BEAM" + (Tag == "head" ? " (baza)" : " — różnica względem bazy HEAD + K1 oczekiwana (E2, informacja)"), () =>
            {
                CleanModelSpace(db);
                ImportGa(key);
                (b, t) = Views(db);
                if (b.IsNull || t.IsNull) throw new AssertFail("brak obrysów rzutów po Prepare GA");
                Mesh(b, t, 225);
                lines = RegressionDump.DrawingDump.Run(db, false,
                    e => !string.Equals(e.Layer, BeamLayer, StringComparison.OrdinalIgnoreCase), false);
                int dists = lines.Count(l => l.Contains("RC_BAR_BLOCK[0]"));
                string file = Path.Combine(OutDir, $"beam-dump-{name}-{Tag}.txt");
                File.WriteAllLines(file, lines, new UTF8Encoding(false));
                if (Tag == "head") return $"zrzut bazowy: {lines.Count} linii → {file}; rozkłady {dists}";
                string head = Path.Combine(OutDir, $"beam-dump-{name}-head.txt");
                string cmp = !File.Exists(head) ? "brak zrzutu bazy" : File.ReadAllLines(head).SequenceEqual(lines) ? "IDENTYCZNY z bazą (E2 nie zadziałał?)"
                           : $"różni się od bazy ({File.ReadAllLines(head).Length} / {lines.Count} linii) — oczekiwane";
                return $"rozkłady {dists}; {cmp}";
            });
            if (Tag == "head") return;
            Check(step, "K7 (E2) + decyzja 2026-10-11: siatka przy belce — równoległe dół ≥ lico + 40 + d, góra ≥ lico + s/2, prostopadłe dół i góra do lica wewnętrznego belki, krawędzie przy belce bez UB 01/02, UB 03 co 100…250, T IN NIB 2 pręty w nibie", () =>
                E2Checks(db, b, t, plot));
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
                Check(step, "K3: lewa noga (skos) — detal ręczny; narożnik L — U-bary obu nóg (noga Y: B − 35), koniec wewnętrzny — U-bar; BM 2x4; strzemiona H12", () =>
                {
                    var res = GenerateBeams(db, b, t);
                    var msgs = Messages(res);
                    if (!msgs.Any(m => m.Contains("detal ręczny"))) throw new AssertFail("brak komunikatu „detal ręczny”: " + string.Join(" | ", msgs));
                    if (!msgs.Any(m => m.Contains("narożniki L: 1") && m.Contains("końce: 1")))
                        throw new AssertFail("komunikat zamknięcia końców: " + string.Join(" | ", msgs.Where(m => m.Contains("zamknięcie"))));
                    var sb = new StringBuilder();
                    var bl = Dists(db, "BL");
                    sb.Append("strzemiona=").Append(string.Join(",", bl.Select(x => x.count).OrderBy(x => x)));
                    if (bl.Any(x => !x.mark.StartsWith("H12-") || !x.mark.EndsWith("-200 LINKS"))) throw new AssertFail("opis strzemion: " + string.Join(" | ", bl.Select(x => x.mark)));
                    var u = Dists(db, "BU");
                    sb.Append("; ").Append(Int("U-bary (rozkłady)", u.Count, 3));
                    var tu = Templates(db).Where(x => x.shape == "21").Select(x => $"{x.a:0}/{x.b:0}/{x.c:0}").OrderBy(x => x).ToList();
                    if (!tu.Any(x => x.EndsWith("/305/" + x.Split('/')[0])) || tu.Count(x => x.Split('/')[1] == "340") < 1)
                        throw new AssertFail("szablony U-barów (B 340 / noga Y 305): " + string.Join(",", tu));
                    sb.Append("; szablony U=").Append(string.Join(",", tu));
                    var l51 = Templates(db).Where(x => x.shape == "51").ToList();
                    if (l51.Count != 1 || Math.Abs(l51[0].a - 365) > 0.5 || Math.Abs(l51[0].b - 510) > 0.5)
                        throw new AssertFail("szablon 51: " + string.Join(",", l51.Select(x => $"A={x.a} B={x.b}")));
                    sb.Append("; ").Append(BothChecks(db)).Append("; ").Append(NoLegacyBeamBars(db)).Append("; ").Append(NumberChecks(db))
                      .Append("; ").Append(UBarsInsideZones(db));
                    return sb.ToString();
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
                Check(step, "format Blok: szablony = pręty-bloki, strzemię 51 A = 215 B = 510 (H12), BM 2x4, bez BT / prętów L", () =>
                {
                    SetFormat("Block");
                    try { GenerateBeams(db, b, t); }
                    finally { SetFormat("Polyline"); }
                    var counts = Dists(db, "BL").Select(x => x.count).OrderBy(x => x).ToList();
                    var tpl = Templates(db);
                    var links = tpl.Where(x => x.shape == "51").ToList();
                    if (links.Count != 1) throw new AssertFail("szablony 51: " + links.Count);
                    if (Math.Abs(links[0].a - 215) > 0.5 || Math.Abs(links[0].b - 510) > 0.5)
                        throw new AssertFail($"51: A={links[0].a} B={links[0].b}");
                    if (tpl.Any(x => !x.isBlock)) throw new AssertFail("szablon polilinia w formacie Blok: " + string.Join(",", tpl.Where(x => !x.isBlock).Select(x => x.mark)));
                    return "strzemiona=[" + string.Join(",", counts) + "]; " + BothChecks(db) + "; " + NoLegacyBeamBars(db)
                         + "; szablony=" + string.Join(",", tpl.Select(x => x.mark)) + "; " + NumberChecks(db);
                });
                Check(step, "K3: U-bary pionowe — 2 narożniki × 2 nogi + 2 końce (6 rozkładów × 4), noga Y B 155, pozostałe B 190, końce 750 / 190 / 750; kółko na zagięciu, linie w strefie", () =>
                {
                    var u = Dists(db, "BU");
                    var sb = new StringBuilder(Int("U-bary (rozkłady)", u.Count, 6));
                    foreach (var x in u)
                    {
                        if (x.count != 4 || !x.mark.EndsWith("-150 UB") || x.shape != "21") throw new AssertFail("U-bar: " + x.count + " " + x.mark + " " + x.shape);
                        if (AnnotOf(db, x.id).IsNull) throw new AssertFail("brak opisu " + x.mark);
                    }
                    var tu = Templates(db).Where(x => x.shape == "21").Select(x => (a: x.a, b: x.b, c: x.c)).ToList();
                    if (!tu.Any(x => x.b == 155) || !tu.Any(x => x.b == 190)) throw new AssertFail("B U-barów: " + string.Join(",", tu.Select(x => $"{x.a:0}/{x.b:0}/{x.c:0}")));
                    if (!tu.Any(x => x.a == 750 && x.b == 190 && x.c == 750)) throw new AssertFail("brak U-bara końca 750/190/750: " + string.Join(",", tu.Select(x => $"{x.a:0}/{x.b:0}/{x.c:0}")));
                    if (tu.Any(x => x.c != x.a)) throw new AssertFail("C ≠ A");
                    sb.Append("; szablony U=").Append(string.Join(",", tu.Select(x => $"{x.a:0}/{x.b:0}/{x.c:0}").OrderBy(x => x)));
                    return sb.Append("; ").Append(NumberChecks(db)).Append("; ").Append(UBarsInsideZones(db)).ToString();
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
                Check(step, "K2 / K3: strzemiona 51 H12 co 200 (52, pierwsze 51 od lica), BM 5500 + 5250 „2x4 H16-nn-150 B+T” (CountDisplay 8), bez BT na rzucie górnym, znaczniki", () =>
                {
                    GenerateBeams(db, _b528, _t528);
                    var bl = Dists(db, "BL");
                    var bm = Dists(db, "BM");
                    var sb = new StringBuilder();
                    sb.Append(Int("BL", bl.Count, 1)).Append("; ").Append(Int("strzemiona", bl[0].count, 52));
                    if (!bl[0].mark.EndsWith("-200 LINKS") || !bl[0].mark.StartsWith("H12-")) throw new AssertFail("opis strzemion: " + bl[0].mark);
                    // pierwsze strzemię 51 od lica (strefa wzdłuż Y)
                    var zb = Box(Verts(db, _z528));
                    double y0 = PosOf(db, bl[0].id).Y;
                    if (Math.Abs(y0 - (zb.y0 + 51)) > 0.6) throw new AssertFail($"pierwsze strzemię {y0 - zb.y0:0.#} od lica (oczekiwano 51)");
                    sb.Append("; ").Append(Int("BM", bm.Count, 2));
                    var lens = bm.Select(x => x.length).OrderByDescending(x => x).ToList();
                    if (Math.Abs(lens[0] - 5500) > 0.5 || Math.Abs(lens[1] - 5250) > 0.5) throw new AssertFail("długości BM: " + string.Join(",", lens));
                    sb.Append("; ").Append(BothChecks(db));
                    foreach (var x in bl.Concat(bm)) Eq("znacznik " + x.mark, XLinkOf(db, x.id, TagApp, 1), Hx(_z528));
                    foreach (var x in bl.Concat(bm)) if (AnnotOf(db, x.id).IsNull) throw new AssertFail("brak opisu " + x.mark);
                    var l51 = Templates(db).Where(x => x.shape == "51").ToList();
                    if (l51.Count != 1 || Math.Abs(l51[0].a - 290) > 0.5 || Math.Abs(l51[0].b - 510) > 0.5)
                        throw new AssertFail("szablon 51: " + string.Join(",", l51.Select(x => $"{x.mark} A={x.a} B={x.b}")));
                    if (Templates(db).Any(x => x.isBlock)) throw new AssertFail("format Polilinia: szablon jako pręt-blok");
                    _gen1 = new HashSet<long>(AllBeamDists(db).Select(x => x.Handle.Value));
                    return sb.Append("; ").Append(NoLegacyBeamBars(db)).Append("; ").Append(NumberChecks(db)).Append("; opisy: ")
                             .Append(string.Join(" | ", bl.Concat(bm).Select(x => AnnotText(db, x.id)))).ToString();
                });
                Check(step, "K3: U-bary pionowe na obu końcach (2 rozkłady × 4, 750 / 265 / 750, „4 H16-nn-150 UB”, kółko na zagięciu przy licu), znaczniki", () =>
                {
                    var u = Dists(db, "BU");
                    var sb = new StringBuilder(Int("U-bary (rozkłady)", u.Count, 2));
                    foreach (var x in u)
                    {
                        if (x.count != 4 || !x.mark.EndsWith("-150 UB")) throw new AssertFail("U-bar: " + x.count + " " + x.mark);
                        Eq("znacznik " + x.mark, XLinkOf(db, x.id, TagApp, 1), Hx(_z528));
                        if (AnnotOf(db, x.id).IsNull) throw new AssertFail("brak opisu " + x.mark);
                        var v = RawOf(db, x.id, DistApp);
                        if (Convert.ToString(v[17].Value, Inv) != "Circle") throw new AssertFail("symbol U-bara: " + v[17].Value);
                    }
                    if (u.Select(x => x.pos).Distinct().Count() != 1) throw new AssertFail("U-bary obu końców — jedna pozycja: " + string.Join(",", u.Select(x => x.mark)));
                    var ys = u.Select(x => PosOf(db, x.id).Y).OrderBy(y => y).ToList();
                    if (ys[1] - ys[0] < 5000) throw new AssertFail("U-bary tylko przy jednym końcu");
                    var tu = Templates(db).Where(x => x.shape == "21").Select(x => $"{x.a:0}/{x.b:0}/{x.c:0}").ToList();
                    Seq2("szablony U", tu, new[] { "750/265/750" });
                    return sb.Append("; U: ").Append(string.Join(" | ", u.Select(x => AnnotText(db, x.id)))).Append("; ").Append(UBarsInsideZones(db))
                             .Append("; ").Append(NumberChecks(db)).ToString();
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
                return Int("rozkłady belki", all.Count, 7) + "; " + Int("szablony", tpl1, tpl0) + "; " + NumberChecks(db);   // K3: BL + 2 BM + 2 BU + 2 BN
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
                    if (cd.Count != 7) throw new AssertFail("rozkłady-kopie: " + cd.Count);   // K3: BL + 2 BM + 2 BU + 2 BN
                    foreach (var id in cd) Eq("znacznik kopii " + id.Handle, XLinkOf(db, id, TagApp, 1), Hx(cz));
                    return "kopie=" + cd.Count.ToString(Inv);
                });
                Check(step, "ponowne generowanie ORYGINAŁU — rozkłady kopii zostają", () =>
                {
                    GenerateBeams(db, _b528, _t528);
                    int lost = (cd ?? new List<ObjectId>()).Count(id => id.IsErased);
                    if (lost > 0) throw new AssertFail("usunięto rozkłady kopii: " + lost);
                    return Int("rozkłady belek razem", AllBeamDists(db).Count, 14);
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
                if (md.Count != 7) throw new AssertFail("rozkłady-odbicia: " + md.Count);
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

        /// <summary>Kopia rzutu dolnego 528 w pasie X (ARRAY / PASTECLIP): jedna strefa, [6] → obrys kopii, 7 rozkładów belki (K3: strzemiona,
        /// 2 BM, 2 BU, 2 BN dołem) → strefa kopii.</summary>
        private static string CloneRegionChecks(Database db, Func<double, bool> inX, string what)
        {
            var zs = Zones(db).Where(z => Verts(db, z).All(p => inX(p.X))).ToList();
            if (zs.Count != 1) throw new AssertFail($"strefy {what}: {zs.Count} (oczekiwano 1)");
            var ob = Ms(db, e => e is Polyline pl && pl.Layer == Slab && pl.Closed && Verts(db, e.ObjectId).All(p => inX(p.X)))
                     .OrderByDescending(id => Area(db, id)).FirstOrDefault();
            if (ob.IsNull) throw new AssertFail("brak obrysu " + what);
            Eq("strefa " + what + " [6]", XLinkOf(db, zs[0], BeamApp, 6), Hx(ob));
            var ds = AllBeamDists(db).Where(id => inX(PosOf(db, id).X)).ToList();
            if (ds.Count != 7) throw new AssertFail($"rozkłady {what}: {ds.Count}");
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
            Run(step, db => Check(step, "Mesh (225) + belka: strzemiona niezależnie od UB 03 (K2) — pierwsze 51 od lica, rozstaw ≤ 200; UB 03 płyty bez zmian", () =>
            {
                CleanModelSpace(db);
                ImportGa("PLOT 528-529");
                var (b, t) = Views(db);
                Mesh(b, t, 225);
                int ub03 = Ms(db, e => e is BlockReference && Raw(e, DistApp) is TypedValue[] v && v.Length > 2
                                       && Convert.ToString(v[1].Value, Inv).Contains("-03-") && Convert.ToString(v[1].Value, Inv).EndsWith(" UB")).Count;
                if (ub03 == 0) throw new AssertFail("brak UB 03 na rzucie dolnym");
                var gm = Messages(GenerateBeams(db, b, t));
                if (gm.Any(m => m.Contains("otulinę dołem"))) throw new AssertFail("ostrzeżenie o otulinie UB 03 (usunięte w K2): " + string.Join(" | ", gm.Where(m => m.Contains("otulin"))));
                var bl = Dists(db, "BL");
                if (bl.Count != 1) throw new AssertFail("BL: " + bl.Count);
                var vv = RawOf(db, bl[0].id, DistApp);
                double step0 = Convert.ToDouble(vv[5].Value, Inv);
                if (step0 > 200.5) throw new AssertFail("rozstaw strzemion " + step0);
                var z = Zones(db).First(x => XLinkOf(db, x, BeamApp, 6) == Hx(b));
                double first = PosOf(db, bl[0].id).Y - Box(Verts(db, z)).y0;
                if (Math.Abs(first - 51) > 0.6) throw new AssertFail($"pierwsze strzemię {first:0.#} od lica");
                int ub03After = Ms(db, e => e is BlockReference && Raw(e, DistApp) is TypedValue[] v && v.Length > 2
                                            && Convert.ToString(v[1].Value, Inv).Contains("-03-") && Convert.ToString(v[1].Value, Inv).EndsWith(" UB")).Count;
                Eq("UB 03 po belce (bez zmian)", ub03After, ub03);
                return $"strzemiona={bl[0].count}, rozstaw={step0.ToString("0.#", Inv)}, pierwsze {first:0.#} od lica; rozkłady UB 03={ub03}";
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
                Check(step, "rozkłady siatki (bez RC-BEAM i kodów belki) = zrzut D528 tego przebiegu — Mesh bez prętów belki (numery pozycji i odwołania znormalizowane)", () =>
                {
                    if (lines == null) throw new AssertFail("brak zrzutu (poprzednia asercja)");
                    string head = Path.Combine(OutDir, $"beam-dump-528mesh-{Tag}.txt");
                    if (!File.Exists(head)) throw new AssertFail("brak zrzutu D528 " + head);
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
                    var hd = MeshChunks(File.ReadAllLines(Path.Combine(OutDir, $"beam-dump-528mesh-{Tag}.txt")), "RC_BAR_ANNOT");
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
                if (c.Any(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"X " + app + @"\[2\] 1000=""(BB|BT|BL|BS|BN|BM|BU|BC)"""))) continue;
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

        /// <summary>Pręty rozkładu siatki (Rotation 0): kierunek, położenia w poprzek (scan), zakres wzdłuż (perp), numer, kod.</summary>
        private sealed class MeshDist { public string code, mark; public bool alongX; public List<double> scan = new List<double>(); public double p0, p1; public int pos; }

        private static List<MeshDist> MeshDists(Database db, ObjectId slab)
        {
            var res = new List<MeshDist>();
            var sv = Verts(db, slab);
            string sh = Hx(slab);
            foreach (var id in Ms(db, e => e is BlockReference br && br.Rotation == 0 && Raw(e, DistApp) is TypedValue[] v && v.Length > 9))
            {
                var v = RawOf(db, id, DistApp);
                string code = Convert.ToString(v[2].Value, Inv);
                if (BeamCodes.Contains(code)) continue;
                var pos = PosOf(db, id);
                string tag = XLinkOf(db, id, "RC_AUTOREBAR_SLAB", 1);
                bool mine = tag.Length > 0 ? tag == sh : InPoly(sv, pos.X, pos.Y);
                if (!mine) continue;
                var d = new MeshDist { code = code, mark = Convert.ToString(v[1].Value, Inv), pos = PosNr(Convert.ToString(v[1].Value, Inv)),
                                       alongX = !string.Equals(Convert.ToString(v[6].Value, Inv), "Y", StringComparison.OrdinalIgnoreCase) };
                int n = Math.Max(1, Convert.ToInt32(v[3].Value, Inv));
                double sp = Convert.ToDouble(v[5].Value, Inv), len = Convert.ToDouble(v[8].Value, Inv);
                double s0 = d.alongX ? pos.Y : pos.X;
                for (int i = 0; i < n; i++) d.scan.Add(s0 + i * (n > 1 ? sp : 0));
                d.p0 = d.alongX ? pos.X : pos.Y; d.p1 = d.p0 + len;
                res.Add(d);
            }
            return res;
        }

        /// <summary>
        /// K7 (E2): asercje siatki przy belce na rzutach dolnym (b) i górnym (t); odcinki osiowe belki z analizy strefy rzutu (BeamGeometry.Analyze
        /// wtyczki), „detal ręczny” pominięte. Podsumowanie rozkładów siatki → beam-e2-&lt;plot&gt;.txt.
        /// </summary>
        private const double BeamNibMax = 260.0;

        private static string E2Checks(Database db, ObjectId b, ObjectId t, string plot)
        {
            var sb = new StringBuilder();
            var report = new List<string>();
            int nSeg = 0;
            string F(double x) => x == double.MaxValue ? "— (brak prętów)" : x.ToString("0.#", Inv);
            foreach (var (view, top) in new[] { (b, false), (t, true) })
            {
                var dists = MeshDists(db, view);
                var outl = Verts(db, view).Select(p => (p.X, p.Y)).ToList();
                var zone = Zones(db).FirstOrDefault(z => XLinkOf(db, z, BeamApp, 6) == Hx(view));
                if (zone.IsNull) throw new AssertFail("brak strefy na rzucie " + (top ? "górnym" : "dolnym"));
                var an = Call("BricsCadRc.Core.BeamGeometry", "Analyze", Verts(db, zone).Select(p => (p.X, p.Y)).ToList(), outl);
                foreach (var sg in L(an, "Segments").Where(x => !(bool)Prop(x, "Manual")))
                {
                    nSeg++;
                    bool ax = (bool)Prop(sg, "AlongX");
                    int outer = (int)Prop(sg, "OuterSide");
                    double cLo = Dv(sg, "AcrossLo"), cHi = Dv(sg, "AcrossHi"), oLo = Dv(sg, "OwnLo"), oHi = Dv(sg, "OwnHi");
                    if (outer == 0) continue;
                    double face = outer < 0 ? cHi : cLo, step = outer < 0 ? cLo : cHi;   // lico wewnętrzne / linia uskoku (lico zewnętrzne)
                    int sgn = outer < 0 ? 1 : -1;                                          // w głąb płyty
                    string pc = top ? (ax ? "T1" : "T2") : (ax ? "B1" : "B2");          // równoległe
                    double need = top ? 100 : 50;
                    double first = double.MaxValue;
                    foreach (var d in dists.Where(x => x.code == pc && x.mark.EndsWith(" " + pc) && x.alongX == ax && Math.Min(x.p1, oHi - 1) - Math.Max(x.p0, oLo + 1) > 1))
                        foreach (var sc in d.scan)
                        {
                            double off = (sc - face) * sgn;
                            if (off < need - 0.5) throw new AssertFail($"{pc} {d.mark}: pręt {off:0.#} od lica wewnętrznego belki (min. {need:0})");
                            first = Math.Min(first, off);
                        }
                    // decyzja 2026-10-11: prostopadłe dołem i górą kończą się NA LICU WEWNĘTRZNYM belki (nie wchodzą w belkę)
                    string qc = top ? (ax ? "T2" : "T1") : (ax ? "B2" : "B1");
                    double reach = double.MaxValue;
                    foreach (var d in dists.Where(x => x.code == qc && x.mark.EndsWith(" " + qc) && x.alongX != ax))
                    {
                        if (!d.scan.Any(sc => sc > oLo + 1 && sc < oHi - 1)) continue;
                        double end = outer < 0 ? d.p0 : d.p1;
                        if (Math.Abs(end - face) > 1000) continue;   // pręt nie dochodzi do belki
                        double off = (end - face) * sgn;
                        if (off < -0.5) throw new AssertFail($"{qc} {d.mark}: koniec {(-off):0.#} w belce (za licem wewnętrznym)");
                        reach = Math.Min(reach, off);
                    }
                    if (reach != double.MaxValue && Math.Abs(reach) > 0.5) throw new AssertFail($"{qc}: koniec prętów {reach:0.#} od lica wewnętrznego (oczekiwano 0)");
                    if (!top)
                    {
                        sb.Append($"odcinek {(ax ? "X" : "Y")} {oLo:0}..{oHi:0}: {pc} pierwszy {F(first)} od lica, {qc} koniec {F(reach)} od lica; ");
                        // UB 01/02: żaden pręt nie zaczyna się przy linii uskoku przed belką ani na nibie zawiniętym przy końcu
                        foreach (var d in dists.Where(x => (x.pos == 1 || x.pos == 2) && x.mark.EndsWith(" UB")))
                            foreach (var sc in d.scan)
                                foreach (var pe in new[] { d.p0, d.p1 })
                                {
                                    double ux = d.alongX ? pe : sc, uy = d.alongX ? sc : pe;
                                    double u = ax ? ux : uy, c = ax ? uy : ux;
                                    if (u > oLo + 1 && u < oHi - 1 && (c - face) * sgn < 0)
                                        throw new AssertFail($"UB {d.mark}: pręt na krawędzi przy belce ({ux:0}, {uy:0})");
                                }
                        // UB 03 przy belce (decyzja 2026-10-11): liczba z prętów dołu kończących się na licu — rozstaw jak siatka (100…250)
                        foreach (var d in dists.Where(x => x.pos == 3 && x.mark.EndsWith(" UB") && x.alongX != ax
                                                         && x.scan.Any(sc => sc > oLo + 1 && sc < oHi - 1)))
                        {
                            var gaps = d.scan.Zip(d.scan.Skip(1), (a1, b1) => b1 - a1).ToList();
                            if (gaps.Any(g => g < 100 || g > 250)) throw new AssertFail($"UB 03 {d.mark}: rozstaw {gaps.Min():0.#}…{gaps.Max():0.#}");
                            sb.Append($"UB 03 {d.scan.Count} szt. co {(gaps.Count > 0 ? gaps.Average() : 0):0.#}; ");
                        }
                    }
                    else
                    {
                        sb.Append($"odcinek {(ax ? "X" : "Y")} (góra): {pc} pierwszy {F(first)} od lica, {qc} koniec {F(reach)} od lica; ");
                        // T IN NIB przy belce (decyzja 2026-10-11): 2 pręty, oba w nibie (przed linią uskoku), opis bez rozstawu
                        int nTin = 0;
                        foreach (var d in dists.Where(x => x.mark.EndsWith(" T IN NIB") && x.alongX == ax
                                                         && Math.Min(x.p1, oHi) - Math.Max(x.p0, oLo) > 1
                                                         && x.scan.All(sc => Math.Abs(sc - step) < 300)))   // ta krawędź (nie równoległa naprzeciw)
                        {
                            nTin++;
                            if (d.scan.Count != 2) throw new AssertFail($"T IN NIB {d.mark}: {d.scan.Count} prętów (oczekiwano 2)");
                            foreach (var sc in d.scan)
                            {
                                double o = (sc - step) * sgn;   // < 0 — przed linią uskoku (w nibie)
                                if (o >= 0 || o < -BeamNibMax) throw new AssertFail($"T IN NIB {d.mark}: pręt {o:0.#} od linii uskoku (poza nibem)");
                            }
                            if (System.Text.RegularExpressions.Regex.IsMatch(d.mark, @"^H\d+-\d+-\d+ "))
                                throw new AssertFail($"T IN NIB {d.mark}: opis z rozstawem");
                            sb.Append($"T IN NIB {d.mark} w {string.Join("/", d.scan.Select(sc => ((sc - step) * sgn).ToString("0.#", Inv)))} od uskoku; ");
                        }
                        if (nTin == 0) sb.Append("INFORMACJA: brak T IN NIB wzdłuż odcinka (krawędź bez nibu?); ");
                    }
                }
                foreach (var g in dists.GroupBy(x => x.code).OrderBy(g => g.Key))
                    report.Add($"{(top ? "TOP" : "BOT")} {g.Key}: " + string.Join(" | ", g.OrderBy(x => x.scan[0]).Select(x =>
                        $"{x.scan.Count} {x.mark} [{x.scan[0]:0}..{x.scan[x.scan.Count - 1]:0}] x [{x.p0:0}..{x.p1:0}]")));
            }
            if (nSeg == 0) throw new AssertFail("brak odcinków osiowych belki");
            File.WriteAllLines(Path.Combine(OutDir, $"beam-e2-{plot}.txt"), report, new UTF8Encoding(false));
            return sb.ToString() + $"podsumowanie → beam-e2-{plot}.txt";
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

        /// <summary>
        /// Poprawka E2 w przekroju (AlignMesh przy belce): w trzonie belki (przedział profilu ze spodem poniżej płyty i wierzchem 0)
        /// brak kropek siatki płyty rozkładów, które w trzon nie wchodzą (pręty równoległe do belki — E2, stop przed licem); pierwsza
        /// kropka takiego rozkładu za belką = rzeczywisty pręt rozkładu najbliższy belce. Zwraca opis (pozycja: pierwsza kropka).
        /// </summary>
        private static string AssertMeshBesideBeam(object ctx, object plan)
        {
            var inp = Prop(ctx, "Input");
            double T = Dv(inp, "Thickness");
            bool cutX = (bool)Prop(inp, "CutAlongX");
            int look = Convert.ToInt32(Prop(inp, "LookSign"), Inv);
            int sign = cutX ? (look >= 0 ? 1 : -1) : (look >= 0 ? -1 : 1);
            var trunks = new List<(double a, double b)>();
            foreach (var sg in L(plan, "Segments").Where(x => Prop(x, "Prof") != null))
                foreach (var p in L(sg, "Prof").Select(p => (ValueTuple<double, double, double, double>)p))
                    if (p.Item3 < -T - 1 && Math.Abs(p.Item4) < 1e-6) trunks.Add((p.Item1, p.Item2));
            if (trunks.Count == 0) throw new AssertFail("brak trzonu belki w profilu przekroju");
            var mesh = L(plan, "Dots").Where(d => (bool)Prop(d, "Mesh")).ToList();
            var sb = new StringBuilder();
            foreach (var d in L(inp, "Dists").Where(x => Convert.ToString(Prop(x, "Kind"), Inv) == "Main" && (bool)Prop(x, "AlongX") != cutX))
            {
                string pos = (string)Prop(d, "PosNr");
                int n = Math.Max(1, Convert.ToInt32(Prop(d, "Count"), Inv));
                double span = Dv(d, "BarsSpan"), sp = n > 1 ? (span > 0 ? span / (n - 1) : Dv(d, "Spacing")) : 0;
                double q0 = (bool)Prop(d, "AlongX") ? Dv(d, "Y0") : Dv(d, "X0");
                var real = Enumerable.Range(0, n).Select(i => sign * (q0 + i * sp)).ToList();
                var dots = mesh.Where(x => (string)Prop(x, "PosNr") == pos).Select(x => Dv(x, "U")).ToList();
                foreach (var (a, b) in trunks)
                {
                    if (real.Any(u => u > a + 0.5 && u < b - 0.5)) continue;     // rozkład przechodzi przez belkę — schemat bez zmian
                    var inB = dots.Where(u => u > a + 0.5 && u < b - 0.5).ToList();
                    if (inB.Count > 0) throw new AssertFail($"kropki siatki poz. {pos} w trzonie belki {a:0}..{b:0}: {string.Join(" ", inB.Select(u => u.ToString("0.#", Inv)))}");
                    foreach (int side in new[] { -1, 1 })
                    {
                        double face = side > 0 ? b : a;
                        var rs = real.Where(u => (u - face) * side > 0).OrderBy(u => Math.Abs(u - face)).ToList();
                        var ds = dots.Where(u => (u - face) * side > 0 && Math.Abs(u - face) < 400).OrderBy(u => Math.Abs(u - face)).ToList();
                        if (rs.Count == 0 || ds.Count == 0) continue;
                        if (Math.Abs(ds[0] - rs[0]) > 0.5) throw new AssertFail($"poz. {pos}: pierwsza kropka za belką u = {ds[0]:0.#}, rozkład {rs[0]:0.#}");
                        sb.Append($"{pos}: {ds[0]:0.#} (lico {face:0}); ");
                    }
                }
            }
            return sb.Length > 0 ? sb.ToString() : "brak siatki równoległej przed licem";
        }

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
                                           && BeamCodes.Contains(Convert.ToString(v[7].Value, Inv))))
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
                    // K2: BN górą = 0 (domyślnie) — bez komunikatu o dublowaniu T IN NIB
                    if (gm.Any(m => m.Contains("może dublować T IN NIB"))) throw new AssertFail("BN górą przy domyślnych K2: " + string.Join(" | ", gm.Where(m => m.Contains("nib"))));
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
                Check(step, "A: profil (spód −375, nib belki −375…−225), kropki BM 4 dół (−305) i 4 góra (−55, warstwa górna), strzemię 51 w widoku (−319…−41), wymiar H 375; BN dołem H10 w narożu UB 03; bez BS / BN górą", () =>
                {
                    if (plan == null) throw new AssertFail("brak planu");
                    var sg = L(plan, "Segments").First(x => Prop(x, "Prof") != null);
                    var prof = L(sg, "Prof").Select(p => (ValueTuple<double, double, double, double>)p).ToList();
                    var trz = prof.FirstOrDefault(p => Math.Abs(p.Item3 + 375) < 1e-6 && p.Item4 == 0);
                    var nibP = prof.FirstOrDefault(p => Math.Abs(p.Item3 + 375) < 1e-6 && Math.Abs(p.Item4 + 225) < 1e-6);
                    if (trz.Item2 - trz.Item1 < 1) throw new AssertFail("brak trzonu −375…0: " + string.Join(" ", prof));
                    if (nibP.Item2 - nibP.Item1 < 1) throw new AssertFail("brak nibu belki −375…−225: " + string.Join(" ", prof));
                    var dots = L(plan, "Dots").Where(d => (bool)Prop(d, "Beam")).ToList();
                    bool In(ValueTuple<double, double, double, double> iv, object d) => Dv(d, "U") >= iv.Item1 - 1 && Dv(d, "U") <= iv.Item2 + 1;
                    List<object> At(double v, ValueTuple<double, double, double, double> iv) => dots.Where(d => In(iv, d) && Math.Abs(Dv(d, "V") - v) < 0.01).ToList();
                    var bot = At(-305, trz); var top = At(-55, trz);
                    string Pos(List<object> l) => string.Join(",", l.Select(d => (string)Prop(d, "PosNr")).Distinct());
                    string All() => string.Join(" ", dots.Select(d => $"{Dv(d, "U"):0.#},{Dv(d, "V"):0.#}"));
                    if (bot.Count != 4 || top.Count != 4) throw new AssertFail($"kropki dół {bot.Count} (poz. {Pos(bot)}), góra {top.Count} (poz. {Pos(top)}): {All()}");
                    if (Pos(bot) != Pos(top)) throw new AssertFail($"BM — jedna pozycja dół / góra: {Pos(bot)} / {Pos(top)}");
                    if (top.Any(d => !(bool)Prop(d, "Top")) || bot.Any(d => (bool)Prop(d, "Top"))) throw new AssertFail("flaga warstwy górnej kropek BM");
                    var ub3 = L(plan, "Bars").FirstOrDefault(b => Convert.ToString(Prop(b, "Kind"), Inv) == "NibUB");
                    var ubP = ub3 == null ? null : ((System.Collections.IEnumerable)Prop(ub3, "Path")).Cast<object>().Select(p => (ValueTuple<double, double>)p).ToList();
                    double vNb = ubP != null ? ubP.Min(p => p.Item2) + 5 + 5 : -315;
                    var nbD = At(vNb, nibP);
                    if (nbD.Count != 1) throw new AssertFail($"BN dołem H10 (v {vNb:0.#}, UB 03: {(ubP != null ? "tak" : "nie")}): {nbD.Count}; {All()}");
                    var links = L(plan, "Bars").Where(b => Convert.ToString(Prop(b, "Kind"), Inv) == "Beam").ToList();
                    if (links.Count != 1) throw new AssertFail("prętów belki w widoku (strzemię): " + links.Count);
                    var lp = ((System.Collections.IEnumerable)Prop(links[0], "Path")).Cast<object>().Select(p => (ValueTuple<double, double>)p).ToList();
                    double vmin = lp.Min(p => p.Item2), vmax = lp.Max(p => p.Item2);
                    if (Math.Abs(vmin + 319) > 0.01 || Math.Abs(vmax + 41) > 0.01) throw new AssertFail($"strzemię v {vmin:0.#}..{vmax:0.#} (oczekiwano −319..−41)");
                    var dims = L(plan, "Dims");
                    if (!dims.Any(d => (bool)Prop(d, "Vertical") && Math.Abs(Math.Abs(Dv(d, "V1") - Dv(d, "V2")) - 375) < 0.01)) throw new AssertFail("brak wymiaru H 375");
                    var lbl = L(plan, "Labels").Select(l => (string)Prop(l, "Text")).ToList();
                    foreach (var p in bot.Concat(nbD).Select(d => (string)Prop(d, "PosNr")).Concat(new[] { (string)Prop(links[0], "PosNr") }).Distinct())
                        if (!lbl.Contains(p)) throw new AssertFail($"brak opisu poz. {p}; opisy: {string.Join(",", lbl.Distinct())}");
                    if (Dists(db, "BS").Count != 0) throw new AssertFail("BS przy H 375 (tryb B3): " + Dists(db, "BS").Count);
                    var bn = Dists(db, "BN");
                    if (bn.Count == 0 || bn.Any(x => !x.mark.StartsWith("H10-") || !x.mark.EndsWith(" B IN NIB"))) throw new AssertFail("BN: " + string.Join(" | ", bn.Select(x => x.mark)));
                    // K5: B IN NIB — numery i długości jak pręty siatki B2 (równoległe do belki, najbliższe belce)
                    var b2 = Dists(db, "B2").Where(x => x.mark.EndsWith(" B2")).ToList();
                    foreach (var x in bn)
                        if (!b2.Any(y => y.pos == x.pos && Math.Abs(y.length - x.length) < 0.5))
                            throw new AssertFail($"B IN NIB {x.mark} L{x.length:0} — brak pręta B2 o tym numerze i długości: " + string.Join(" | ", b2.Select(y => y.mark + " L" + y.length.ToString("0", Inv))));
                    return $"BM poz. {Pos(bot)} ×4 −305 / ×4 −55, BN dołem {Pos(nbD)} {vNb:0.#}, strzemię {Prop(links[0], "PosNr")} −319..−41; BN: {string.Join(", ", bn.Select(x => x.mark))}";
                });
                Check(step, "decyzja 2026-10-11 (SectionPlanner.Version 4): bez kropek siatki płyty w trzonie belki; T IN NIB — 2 kropki w nibie belki pod górnym ramieniem UB 03, B IN NIB w dolnym narożu", () =>
                {
                    if (plan == null) throw new AssertFail("brak planu");
                    string mesh = AssertMeshBesideBeam(ctx, plan);
                    var sg = L(plan, "Segments").First(x => Prop(x, "Prof") != null);
                    var prof = L(sg, "Prof").Select(p => (ValueTuple<double, double, double, double>)p).ToList();
                    var nibP = prof.FirstOrDefault(p => Math.Abs(p.Item3 + 375) < 1e-6 && Math.Abs(p.Item4 + 225) < 1e-6);
                    var tin = L(plan, "Dots").Where(d => (bool)Prop(d, "TinNib")).ToList();
                    string T() => string.Join(" ", tin.Select(d => $"{Dv(d, "U"):0.#},{Dv(d, "V"):0.#}"));
                    if (tin.Count != 2) throw new AssertFail($"kropki T IN NIB: {tin.Count} ({T()})");
                    if (tin.Any(d => Dv(d, "U") < nibP.Item1 - 1 || Dv(d, "U") > nibP.Item2 + 1 || Dv(d, "V") > -225 || Dv(d, "V") < -375))
                        throw new AssertFail($"T IN NIB poza nibem belki {nibP.Item1:0}..{nibP.Item2:0} × −375..−225: {T()}");
                    var ub3 = L(plan, "Bars").FirstOrDefault(b => Convert.ToString(Prop(b, "Kind"), Inv) == "NibUB");
                    if (ub3 != null)
                    {
                        var ubP = ((System.Collections.IEnumerable)Prop(ub3, "Path")).Cast<object>().Select(p => (ValueTuple<double, double>)p).ToList();
                        if (tin.Any(d => Dv(d, "V") >= ubP.Max(p => p.Item2))) throw new AssertFail($"T IN NIB nie pod górnym ramieniem UB 03: {T()}");
                    }
                    return $"siatka przy belce: {mesh}; T IN NIB: {T()}";
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
                    // K2: H 300 → bez prętów bocznych (B3); pręty w nibie tylko dołem H10 (górą 0)
                    if (Dists(db, "BS").Count != 0) throw new AssertFail("BS przy H 300: " + Dists(db, "BS").Count);
                    var bn = Dists(db, "BN");
                    if (bn.Any(x => x.mark.EndsWith(" BEAM NIB T")) || !bn.Any(x => x.mark.EndsWith(" B IN NIB"))) throw new AssertFail("BN: " + string.Join(" | ", bn.Select(x => x.mark)));
                    return $"odcinek {(ax ? "X" : "Y")} {Dv(seg, "FaceLo"):0}..{Dv(seg, "FaceHi"):0} oś {axis:0.#}; pasy: " +
                           string.Join(", ", beams.Select(x => $"{Dv(x, "S0"):0}..{Dv(x, "S1"):0} H={Dv(x, "Height"):0}"));
                });
                Check(step, "B: pręty belki w widoku — BM liniami dół (−230) i góra (−55, warstwa górna; zakład +/− 25, symbole zakończeń S1 — wersja 3), strzemiona 51 jako pionowe ramiona (−244…−41), profil −300; U-bary BU pominięte (K6)", () =>
                {
                    if (plan == null) throw new AssertFail("brak planu");
                    var pos = BeamPositions(db);
                    var bars = L(plan, "Bars").Where(b => Convert.ToString(Prop(b, "Kind"), Inv) == "Beam").ToList();
                    List<(double U, double V)> P(object b) => ((System.Collections.IEnumerable)Prop(b, "Path")).Cast<object>().Select(p => (ValueTuple<double, double>)p).Select(p => (p.Item1, p.Item2)).ToList();
                    int nb = 0, nt = 0, nl = 0, nlap = 0, nsym = 0;
                    foreach (var b in bars)
                    {
                        var pth = P(b);
                        int pnr = int.Parse((string)Prop(b, "PosNr"), Inv);
                        if (!pos.TryGetValue(pnr, out var tp)) throw new AssertFail("pręt w widoku poz. " + pnr + " nie jest pozycją belki");
                        if (tp.code == "BU") throw new AssertFail("U-bar BU w przekroju przed K6");
                        if (tp.code == "BL")
                        {
                            if (pth.Count != 2 || Math.Abs(pth[0].U - pth[1].U) > 0.01 || Math.Abs(pth.Min(p => p.V) + 244) > 0.01 || Math.Abs(pth.Max(p => p.V) + 41) > 0.01)
                                throw new AssertFail("strzemię " + pnr + ": " + string.Join(" ", pth.Select(p => $"{p.U:0.#},{p.V:0.#}")));
                            nl++;
                        }
                        else if (tp.shape == "00" && tp.code == "BM")
                        {
                            bool topL = (bool)Prop(b, "Top");
                            double v = topL ? -55 : -230;
                            // wersja 3 (zakończenia Z1–Z2): poziom = najdłuższy odcinek poziomy; pręt zakładu odsunięty o 25 do wnętrza
                            // (góra w dół, dół w górę); symbole na końcach — odcinki ukośne / pionowe poza poziomem
                            var hs = Enumerable.Range(0, pth.Count - 1).Where(i => Math.Abs(pth[i].V - pth[i + 1].V) < 0.01)
                                               .OrderByDescending(i => Math.Abs(pth[i + 1].U - pth[i].U)).ToList();
                            if (hs.Count == 0) throw new AssertFail($"pręt {pnr} (BM) bez odcinka poziomego");
                            double hv = pth[hs[0]].V, lapV = v + (topL ? -25 : 25);
                            if (Math.Abs(hv - v) > 0.01 && Math.Abs(hv - lapV) > 0.01) throw new AssertFail($"pręt {pnr} (BM, góra={topL}) v={hv:0.#}, oczekiwano {v} albo {lapV} (zakład)");
                            if (Math.Abs(hv - lapV) <= 0.01) nlap++;
                            if (pth.Count > 2) nsym++;
                            if (topL) nt++; else nb++;
                        }
                    }
                    if (nb < 1 || nt < 1 || nl < 10) throw new AssertFail($"w widoku: dół {nb}, góra {nt}, strzemiona {nl}");
                    var prof = L(plan, "Segments").Where(s => Prop(s, "Prof") != null).SelectMany(s => L(s, "Prof")).Select(p => (ValueTuple<double, double, double, double>)p).ToList();
                    if (!prof.Any(p => Math.Abs(p.Item3 + 300) < 1e-6 && p.Item4 == 0)) throw new AssertFail("brak trzonu −300: " + string.Join(" ", prof));
                    int lbl = L(plan, "Labels").Count(l => pos.Any(kv => kv.Value.code == "BL" && kv.Key.ToString("00", Inv) == (string)Prop(l, "Text")));
                    if (lbl != 1) throw new AssertFail("opisy strzemion: " + lbl);
                    if (nsym < 1) throw new AssertFail("wersja 3: brak symboli zakończeń na prętach BM w widoku");
                    return $"BM: dół {nb} linii (−230), góra {nt} (−55), w tym odsuniętych w zakładzie {nlap}, z symbolem zakończenia {nsym}; strzemiona {nl} pionowych ramion, opis strzemion 1";
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
                Check(step, "RC_BBS (ReadBars): pozycje belki — wszystkie w serii dolnej (BM / BU / BL / BN), kształty 00 / 21 / 51, długość cięcia BS 8666:2020, sztuki = suma rozkładów (BM: 2 × 4 na rozkład); bez ostrzeżeń „długość wg BS 8666:2020” dla belki", () =>
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
                        if (top) throw new AssertFail($"poz. {kv.Key} ({t.code}) w warstwie {Prop(r[0], "Layer")} — belka w serii dolnej (K3)");
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
                    foreach (var s in new[] { "00", "21", "51" }) if (!shapes.Contains(s)) throw new AssertFail("brak kształtu " + s + " wśród pozycji belki");
                    if (shapes.Contains("11")) throw new AssertFail("pręty L (11) — usunięte w K3");
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
                        if (x[0].top) throw new AssertFail($"arkusz: poz. {kv.Key} w sekcji TOP — belka w BOTTOM LAYER (K3)");
                    }
                    double mx = xr.Sum(r => r.total * r.length / 1000.0 * LinearMass(r.dia));
                    var cnt = ((System.Collections.IEnumerable)Call("BricsCadRc.Core.BbsCounter", "CountAll", db, new List<string>())).Cast<object>().ToList();
                    double mc = cnt.Sum(x => Dv(x, "TotalMassKg"));
                    if (Math.Abs(mx - mc) > 0.05) throw new AssertFail($"masa z arkusza {mx:0.00} ≠ RC_COUNT_BBS {mc:0.00}");
                    return $"{Prop(gen, "Message")} wierszy {xr.Count}, masa {mx:0.00} kg = COUNT; plik {xls}";
                });
            });
        }

        [CommandMethod("RCSMOKE_BEAM_G528V1", CommandFlags.Modal)]
        public void G528V1()
        {
            const string step = "G528V1";
            Run(step, db =>
            {
                ObjectId zb = ObjectId.Null, b = ObjectId.Null, t = ObjectId.Null;
                Check(step, "rekord RC_BEAM v1 (wersja testowa: [1] = 1, 24 pola, strzemiona H10, zakład 800, BS H10, BN H10 / H16) na strefie 528", () =>
                {
                    SetFormat("Polyline");
                    CleanModelSpace(db);
                    ImportGa("PLOT 528-529");
                    (b, t) = Views(db);
                    zb = Zones(db).First(z => XLinkOf(db, z, BeamApp, 6) == Hx(b));
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var e = (Entity)tr.GetObject(zb, OpenMode.ForWrite);
                        var v = Raw(e, BeamApp).Take(24).ToArray();
                        v[1] = new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)1);
                        v[7] = new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)10);
                        v[15] = new TypedValue((int)DxfCode.ExtendedDataReal, 800.0);
                        v[18] = new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)10);
                        v[20] = new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)10);
                        v[21] = new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)16);
                        e.XData = new ResultBuffer(v);
                        tr.Commit();
                    }
                    return "długość rekordu " + RawOf(db, zb, BeamApp).Length;
                });
                Check(step, "generowanie: komunikat migracji v1, rekord zapisany jako v2 (36 pól, [1] = 2), strzemiona H12, BM, bez BS / BN górą", () =>
                {
                    var msgs = Messages(GenerateBeams(db, b, t));
                    var mig = msgs.FirstOrDefault(m => m.Contains("rekord RC_BEAM v1"));
                    if (mig == null) throw new AssertFail("brak komunikatu migracji: " + string.Join(" | ", msgs.Take(5)));
                    var v = RawOf(db, zb, BeamApp);
                    if (v.Length != 36 || Convert.ToInt32(v[1].Value, Inv) != 2) throw new AssertFail($"rekord po generowaniu: {v.Length} pól, [1] = {v[1].Value}");
                    if (Convert.ToInt32(v[7].Value, Inv) != 12) throw new AssertFail("Ø strzemion w rekordzie: " + v[7].Value);
                    var bl = Dists(db, "BL");
                    if (bl.Count != 1 || !bl[0].mark.StartsWith("H12-")) throw new AssertFail("strzemiona: " + string.Join(",", bl.Select(x => x.mark)));
                    if (Dists(db, "BS").Count > 0 || Dists(db, "BN").Any(x => x.mark.EndsWith(" BEAM NIB T"))) throw new AssertFail("BS / BN górą po migracji");
                    return mig.Length > 160 ? mig.Substring(0, 160) + "…" : mig;
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

        [CommandMethod("RCSMOKE_BEAM_G528S", CommandFlags.Modal)]
        public void G528S()
        {
            const string step = "G528S";
            Run(step, db => Check(step, "K4: H 528 → 600 — pręty boczne BS H12 „2x2 H12-nn SIDE” (2 w rzucie, CountDisplay 4) i UB SIDE BC (21, B 485) „2x1 H12-nn UB SIDE” na obu końcach", () =>
            {
                SetFormat("Polyline");
                CleanModelSpace(db);
                ImportGa("PLOT 528-529");
                var (b, t) = Views(db);
                var zb = Zones(db).First(z => XLinkOf(db, z, BeamApp, 6) == Hx(b));
                var zt = Zones(db).First(z => XLinkOf(db, z, BeamApp, 6) == Hx(t));
                SetZoneHeight(db, zb, 600); SetZoneHeight(db, zt, 600);
                var msgs = Messages(GenerateBeams(db, b, t));
                var bs = Dists(db, "BS");
                var bc = Dists(db, "BC");
                if (bs.Count == 0) throw new AssertFail("brak BS: " + string.Join(" | ", msgs.Where(m => m.Contains("boczne"))));
                foreach (var x in bs)
                {
                    if (x.count != 2 || EffCount(RawOf(db, x.id, DistApp)) != 4 || !x.mark.StartsWith("H12-") || !x.mark.EndsWith(" SIDE")) throw new AssertFail("BS: " + x.count + " " + x.mark);
                    string tx = AnnotText(db, x.id);
                    if (!tx.StartsWith("2x2 H12-") || !tx.EndsWith(" SIDE")) throw new AssertFail("opis BS «" + tx + "»");
                }
                if (bc.Count != 2) throw new AssertFail("UB SIDE: " + bc.Count);
                foreach (var x in bc)
                {
                    string tx = AnnotText(db, x.id);
                    if (x.shape != "21" || !tx.StartsWith("2x1 H12-") || !tx.EndsWith(" UB SIDE")) throw new AssertFail("UB SIDE «" + tx + "» kształt " + x.shape);
                }
                var tu = Templates(db).Where(x => x.mark.StartsWith("H12-") && x.shape == "21").Select(x => $"{x.a:0}/{x.b:0}/{x.c:0}").Distinct().ToList();
                if (!tu.All(x => x.Split('/')[1] == "485")) throw new AssertFail("szablony UB SIDE: " + string.Join(",", tu));
                return $"BS {bs.Count} («{AnnotText(db, bs[0].id)}»), UB SIDE {bc.Count} («{AnnotText(db, bc[0].id)}», {string.Join(",", tu)}); {NumberChecks(db)}";
            }));
        }

        private static ObjectId _b319, _b17;
        private static Point3d _tl319, _tl17;

        [CommandMethod("RCSMOKE_BEAM_S319", CommandFlags.Modal)]
        public void S319()
        {
            const string step = "S319";
            Run(step, db => Check(step, "319: Prepare GA, Mesh (225), pręty belki; RC_SECTION D poziomo przez prawą nogę (koniec wewnętrzny) i górną nogę", () =>
            {
                SetFormat("Polyline");
                CleanModelSpace(db);
                ImportGa("Plot 319");
                var (b, t) = Views(db);
                _b319 = b;
                Mesh(b, t, 225);
                GenerateBeams(db, b, t);
                var z = Zones(db).First(x => XLinkOf(db, x, BeamApp, 6) == Hx(b));
                var zb = Box(Verts(db, z)); var sb = Box(Verts(db, b));
                double y = zb.y1 - 1200;     // poniżej górnej nogi, przez prawą nogę
                _tl319 = new Point3d(sb.x0, sb.y0 - 9000, 0);
                var (ctx, plan) = DrawSection(db, b, new Point2d(zb.x1 - 3000, y), new Point2d(sb.x1 + 1000, y), new Point2d(zb.x1 - 3000, y + 100), _tl319, "D");
                return $"pasy belek: {L(Prop(ctx, "Input"), "Beams").Count}, pręty w widoku {L(plan, "Bars").Count}, kropki {L(plan, "Dots").Count}";
            }));
        }

        [CommandMethod("RCSMOKE_BEAM_S319PDF", CommandFlags.Modal)]
        public void S319Pdf()
        {
            const string step = "S319PDF";
            Run(step, db => Check(step, "kopia rysunku z layoutem „BELKA 319” (rzut dolny + przekrój D) do PDF", () =>
                SavePdfCopy(db, "BELKA 319", "PLOT 319 — siatka przy belce (E2), belka K2–K5, RC_SECTION D-D — NA Smart RC (do testów)", _b319, _tl319, "belka-319.dwg")));
        }

        private static string C12Dir => Environment.GetEnvironmentVariable("RC_BEAM_C12") is string d && d.Length > 0 ? d
            : Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? "", @"Temp\claude\C--Users-test-Desktop-AI-BricsCadRc---BJ\234c7608-bb95-42da-b7e6-ea57a103e39e\scratchpad\c12ga");

        private static object ImportGaPath(string path, string label)
        {
            if (!File.Exists(path)) throw new AssertFail("brak GA " + path);
            var warnings = new List<string>();
            var src = (IDisposable)Call(GaType, "ReadSource", path, warnings);
            try
            {
                var plots = ((System.Collections.IEnumerable)Prop(src, "Plots")).Cast<object>().ToList();
                var plot = plots.FirstOrDefault(p => ((string)Prop(p, "Label")).StartsWith(label, StringComparison.OrdinalIgnoreCase))
                           ?? throw new AssertFail("brak płyty " + label + " w " + path + ": " + string.Join(",", plots.Select(p => (string)Prop(p, "Label"))));
                var r = Call(GaType, "Import", Application.DocumentManager.MdiActiveDocument, src, plot, warnings);
                _lastWarnings = warnings;
                return r;
            }
            finally { src.Dispose(); }
        }

        private static string ThkSummary(Database db, object r)
        {
            var hs = ((System.Collections.IEnumerable)Prop(r, "BeamHeights")).Cast<int>().ToList();
            var zones = Zones(db);
            return $"strefy (oba rzuty) {zones.Count}, Result.Beams={Prop(r, "Beams")}, H=[{string.Join(",", hs.OrderBy(x => x))}]; komunikaty: " +
                   string.Join(" | ", _lastWarnings.Where(w => w.Contains("[RC BEAM]")));
        }

        [CommandMethod("RCSMOKE_BEAM_C17", CommandFlags.Modal)]
        public void C17()
        {
            const string step = "C17";
            Run(step, db =>
            {
                ObjectId b = ObjectId.Null, t = ObjectId.Null;
                Check(step, "K8: Prepare GA C12 PLOT 17 (bez SD-Thickening-Zone) — 1 strefa 450THK z opisu (oba rzuty), 525THK / GB — odrzucone z komunikatem", () =>
                {
                    SetFormat("Polyline");
                    CleanModelSpace(db);
                    var r = ImportGaPath(Path.Combine(C12Dir, "C12 GA (P06) - PLOT 17.dxf"), "PLOT 17");
                    // C12: obrys płyty z GA = linia uskoku (krawędź zewnętrzna nibu otwarta; JoinLoops skleja ją z belką gruntową GB
                    // w większą pętlę) — rzuty = obrysy wskazane przez strefy ([6])
                    foreach (var z in Zones(db))
                    {
                        string h = XLinkOf(db, z, BeamApp, 6);
                        if (h.Length == 0 || !db.TryGetObjectId(new Handle(Convert.ToInt64(h, 16)), out var sid)) continue;
                        var tg = RawOf(db, sid, "RC_GA");
                        string vw = tg != null && tg.Length > 2 ? Convert.ToString(tg[2].Value, Inv) : "";
                        if (vw == "B") b = sid; else if (vw == "T") t = sid;
                    }
                    _b17 = b;
                    string sum = ThkSummary(db, r);
                    var hs = ((System.Collections.IEnumerable)Prop(r, "BeamHeights")).Cast<int>().ToList();
                    if (Zones(db).Count != 2 || hs.Count != 1 || hs[0] != 450) throw new AssertFail(sum);
                    if (!_lastWarnings.Any(w => w.Contains("525THK"))) throw new AssertFail("brak komunikatu o 525THK: " + sum);
                    return sum;
                });
                Check(step, "C12 PLOT 17: Mesh (225) + belka; RC_SECTION E w poprzek belki (do PDF)", () =>
                {
                    if (b.IsNull || t.IsNull) throw new AssertFail("brak rzutów");
                    Mesh(b, t, 225);
                    var msgs = Messages(GenerateBeams(db, b, t));
                    var z = Zones(db).First(x => XLinkOf(db, x, BeamApp, 6) == Hx(b));
                    var zb = Box(Verts(db, z)); var sb = Box(Verts(db, b));
                    bool alongX = zb.x1 - zb.x0 > zb.y1 - zb.y0;
                    _tl17 = new Point3d(sb.x0, sb.y0 - 9000, 0);
                    object plan, ctx;
                    if (alongX)
                    {
                        double x = zb.x0 + 0.3 * (zb.x1 - zb.x0);
                        (ctx, plan) = DrawSection(db, b, new Point2d(x, sb.y1 + 1000), new Point2d(x, zb.y0 - 3000), new Point2d(x + 100, sb.y1), _tl17, "E");
                    }
                    else
                    {
                        double y = zb.y0 + 0.3 * (zb.y1 - zb.y0);
                        (ctx, plan) = DrawSection(db, b, new Point2d(sb.x0 - 1000, y), new Point2d(zb.x1 + 3000, y), new Point2d(sb.x0, y + 100), _tl17, "E");
                    }
                    // decyzja 2026-10-11 (SectionPlanner.Version 4): bez kropek siatki płyty w trzonie belki
                    string e2 = AssertMeshBesideBeam(ctx, plan);
                    return $"belka: {string.Join(" | ", msgs.Where(m => m.Contains("strzemiona") || m.Contains("B IN NIB") || m.Contains("U-bary pionowe")).Take(4))}; przekrój: pręty {L(plan, "Bars").Count}, kropki {L(plan, "Dots").Count}; E2: {e2}";
                });
            });
        }

        [CommandMethod("RCSMOKE_BEAM_C17PDF", CommandFlags.Modal)]
        public void C17Pdf()
        {
            const string step = "C17PDF";
            Run(step, db => Check(step, "kopia rysunku z layoutem „BELKA C12-17” do PDF", () =>
                SavePdfCopy(db, "BELKA C12-17", "C12 PLOT 17 — belka z opisu 450THK (K8), siatka E2, RC_SECTION E-E — NA Smart RC (do testów)", _b17, _tl17, "belka-c12-17.dwg")));
        }

        [CommandMethod("RCSMOKE_BEAM_C3", CommandFlags.Modal)]
        public void C3()
        {
            const string step = "C3";
            Run(step, db => Check(step, "K8: Prepare GA C12 PLOT 3 — strefy 600 / 675 (podział linią w GA) i 975 (narożnik, odnośnik z linią strefy); oba rzuty", () =>
            {
                CleanModelSpace(db);
                var r = ImportGaPath(Path.Combine(C12Dir, "C12 GA (P06) - PLOT 3.dxf"), "PLOT 3");
                string sum = ThkSummary(db, r);
                var hs = ((System.Collections.IEnumerable)Prop(r, "BeamHeights")).Cast<int>().OrderBy(x => x).ToList();
                if (!hs.SequenceEqual(new[] { 600, 675, 975 }) || Zones(db).Count != 6) throw new AssertFail(sum);
                return sum;
            }));
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
            BbsDump(db, Path.Combine(dir, Path.GetFileNameWithoutExtension(file) + "-bbs.txt"));
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

        /// <summary>Wiersze RC_BBS rysunku (BbsDrawingReader.ReadBars) do pliku — porównanie z ręcznym BBS (raport K4–K8).</summary>
        private static void BbsDump(Database db, string path)
        {
            try
            {
                var bbs = Call("BricsCadRc.Core.BbsDrawingReader", "ReadBars", db);
                var lines = new List<string>();
                double tot = 0;
                foreach (var r in L(bbs, "Rows").OrderBy(x => (int)Prop(x, "BarMark")))
                {
                    var dims = (double[])Prop(r, "Dims");
                    double m = Dv(r, "MassKg"); tot += m;
                    lines.Add(string.Format(Inv, "{0,4} H{1,-3} {2,3} {3,5} L{4,6:0} {5} {6:0.0} kg", (int)Prop(r, "BarMark"), (int)Prop(r, "Diameter"),
                        Prop(r, "ShapeCode"), (int)Prop(r, "Total"), Dv(r, "Length"),
                        string.Join("/", dims.Where(x => !double.IsNaN(x) && x > 0).Select(x => x.ToString("0", Inv))), m));
                }
                lines.Add(string.Format(Inv, "RAZEM {0:0.0} kg", tot));
                File.WriteAllLines(path, lines, new UTF8Encoding(false));
            }
            catch (System.Exception ex) { Msg("\n" + Prefix + " BbsDump: " + ex.Message + "\n"); }
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

        /// <summary>Kody rozkładów belki (K3: BM / BU; stare BB / BT / BS / BN; BL).</summary>
        private static readonly string[] BeamCodes = { "BB", "BT", "BL", "BS", "BN", "BM", "BU", "BC" };

        /// <summary>Numery pozycji prętów belki (K3): wszystkie w serii dolnej 04–99; żadnego 01–03.</summary>
        private static string NumberChecks(Database db)
        {
            var bad = new List<string>();
            foreach (var code in BeamCodes)
                foreach (var d in Dists(db, code))
                    if (d.pos < 4 || d.pos >= 100) bad.Add(d.mark);
            if (bad.Count > 0) throw new AssertFail("numery: " + string.Join(", ", bad));
            return "numery OK (seria dolna)";
        }

        /// <summary>K3: BM — opis „2x4 H16-nn-150 B+T”, Count 4, CountDisplay 8; brak BB / BT (dół = góra).</summary>
        private static string BothChecks(Database db)
        {
            var bm = Dists(db, "BM");
            if (bm.Count == 0) throw new AssertFail("brak rozkładów BM");
            foreach (var x in bm)
            {
                if (x.count != 4 || !x.mark.EndsWith("-150 B+T") || !x.mark.StartsWith("H16-")) throw new AssertFail("BM: " + x.count + " " + x.mark);
                var v = RawOf(db, x.id, DistApp);
                if (EffCount(v) != 8) throw new AssertFail("BM CountDisplay: " + EffCount(v));
                string t = AnnotText(db, x.id);
                if (!t.StartsWith("2x4 H16-") || !t.EndsWith("-150 B+T")) throw new AssertFail("tekst opisu BM: «" + t + "»");
            }
            if (Dists(db, "BB").Count + Dists(db, "BT").Count > 0) throw new AssertFail("BB / BT przy dół = góra");
            return "BM=" + bm.Count + " («" + AnnotText(db, bm[0].id) + "»)";
        }

        /// <summary>K3: brak prętów L (11), poziomych U-barów (kody BB / BT z kształtem 21) i rozkładów belki na rzucie górnym.</summary>
        private static string NoLegacyBeamBars(Database db)
        {
            var l = Ms(db, e => e is BlockReference && Raw(e, DistApp) is TypedValue[] v && v.Length > 12
                                && BeamCodes.Contains(Convert.ToString(v[2].Value, Inv)) && Convert.ToString(v[12].Value, Inv) == "11");
            if (l.Count > 0) throw new AssertFail("pręty L (11): " + l.Count);
            int bt = Dists(db, "BT").Count;
            if (bt > 0) throw new AssertFail("BT (dół = góra): " + bt);
            return "bez prętów L / BT";
        }

        /// <summary>Tekst opisu rozkładu (DBText w bloku opisu).</summary>
        private static string AnnotText(Database db, ObjectId dist)
        {
            var a = AnnotOf(db, dist);
            if (a.IsNull) return "";
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var br = (BlockReference)tr.GetObject(a, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                foreach (ObjectId cid in btr)
                    if (tr.GetObject(cid, OpenMode.ForRead) is DBText t) return t.TextString ?? "";
                tr.Commit();
            }
            return "";
        }

        /// <summary>
        /// K3: linie rozkładów pionowych U-barów (BU) leżą w strefie belki rzutu dolnego; kółko na zagięciu — jedno na pręt (kategoria UBar),
        /// środek kółka bliżej tyłu U (lico końca / narożnika) niż końca ramienia.
        /// </summary>
        private static string UBarsInsideZones(Database db)
        {
            var zones = Zones(db).Select(z => Verts(db, z)).ToList();
            int lines = 0, circles = 0, bars = 0;
            foreach (var x in Dists(db, "BU"))
            {
                bars += x.count;
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var br = (BlockReference)tr.GetObject(x.id, OpenMode.ForRead);
                    var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    foreach (ObjectId cid in btr)
                    {
                        var o = tr.GetObject(cid, OpenMode.ForRead);
                        if (o is Circle c) { circles++; continue; }
                        if (!(o is Line ln)) continue;
                        lines++;
                        foreach (var pt in new[] { ln.StartPoint.TransformBy(br.BlockTransform), ln.EndPoint.TransformBy(br.BlockTransform) })
                            if (!zones.Any(z => InPoly(z, pt.X, pt.Y)))
                                throw new AssertFail($"{x.mark}: punkt linii ({pt.X:0.#}, {pt.Y:0.#}) poza strefą belki");
                    }
                    tr.Commit();
                }
            }
            if (circles != bars) throw new AssertFail($"kółka na zagięciu: {circles}, U-bary: {bars}");
            return $"U-bary w strefie (linie {lines}, kółka {circles})";
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
                           && BeamCodes.Contains(Convert.ToString(v[2].Value, Inv)));

        private static List<ObjectId> Zones(Database db)
            => Ms(db, e => e is Polyline && Raw(e, BeamApp) != null);

        private static List<(string mark, string shape, double a, double b, bool isBlock, double c, int pos)> Templates(Database db)
        {
            var res = new List<(string, string, double, double, bool, double, int)>();
            foreach (var id in Ms(db, e => Raw(e, "RC_SINGLE_BAR") is TypedValue[] v && v.Length > 7
                                           && BeamCodes.Contains(Convert.ToString(v[7].Value, Inv))))
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
