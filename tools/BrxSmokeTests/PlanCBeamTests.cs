// BEAM — testy dymne belek / pogrubień (docs/PLAN_BELKI_2026-10-10.md E1–E6; K2 + K3 planu praktyki biura
// docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md) w BricsCAD V25. Uruchamiane skryptem planc-beam.scr (run-planc-beam.ps1). Komendy RCSMOKE_BEAM_*.
// Dostęp do wtyczki wyłącznie refleksją. Dane: GA Holbrook P02 (DXF, tylko odczyt), folder: zmienna RC_BEAM_GA.
//
// D526  Prepare GA 526 (bez stref) → zrzut DrawingDump (pełny, z warstwami) = zrzut wtyczki bazowej (bit w bit).
// D319 / D528 / D539  Prepare GA + Mesh (225) — K7 (E2, POPRAWKI_WZORCA 30): siatka przy belce wg praktyki biura (Holbrook ręczny
//       528 / 539 / 319): pręty równoległe dołu od 40 + d od lica wewnętrznego, góry od s/2; prostopadłe dołu i góry przez belkę do 40
//       od lica zewnętrznego (decyzja 2026-10-11 „do 40mm side cover”); krawędzie przy belce bez UB 01/02; UB 03 z prętów dołu (rozstaw 100…250);
//       przy belce bez T IN NIB (decyzja 2026-10-11 „jedna pozycja bottom in nib i top” — pręty w nibie 2T+1B z belki; asercje + podsumowanie
//       rozkładów beam-e2-<płyta>.txt do porównania z ręcznym).
//       Zrzut bez RC-BEAM różni się od bazy (HEAD + K1) — oczekiwane (informacja); odcinki „detal ręczny” (319, skos) — siatka bez zmian.
// K2 / K3 (domyślne, do potwierdzenia przez Bartka): strzemiona H12 51, A = H − 35 − 50 (A1), pierwsze 51 od lica (także przy U-barach),
//       niezależnie od UB 03; pręty podłużne dół = góra jedną pozycją BM „2x4 H16-nn-150 B+T” (seria dolna, rzut dolny, CountDisplay 8),
//       zakład 40·Ø (H16 650), koniec pręta prostego 61 od lica; pionowe U-bary BU kształt 21 „4 H16-nn-150 UB” (po 1 w kolumnie;
//       na końcu belki w rzucie 1 pręt reprezentatywny — decyzja 2026-10-11):
//       koniec — A = Ceil25(650 + 90) = 750, B = A strzemienia − 2·12 (528: 265, 539: 190, 319: 340), narożnik L — każda noga
//       (noga Y: B − 35), złącze T — belka dochodząca; brak prętów L / poziomych U / BT na rzucie górnym; BS (tryb B3) i BN górą — brak
//       dla 300 / 375 / 450; pręty w nibie jedną pozycją BN „2+1 H10-nn T+B IN NIB” (decyzje 2026-10-11).
// G319  strefa + H = 450; lewa noga ze skosem — „detal ręczny”; góra + prawa: narożnik L (U-bary obu nóg), koniec wewnętrzny prawej nogi.
// G539  H = 300, L + L; format Blok (szablony = pręty-bloki): strzemię 51 A = 215, B = 510; narożniki L (4 rozkłady U-barów, noga Y
//       B 155), końce (wewnętrzny + przy krawędzi) U-bary 750 / 190 / 750.
// G528  H = 375, prosta: strzemiona 52 (pierwsze 51 od lica), BM 5500 + 5250 (2x4), U-bary na obu końcach 750 / 265 / 750, numery 04–99.
// G528R ponowne generowanie — te same liczby, bez duplikatów, szablony ponownie użyte; G528U / G528D — UNDO / REDO.
// G528C / G528M / G528A / G528P — COPY / MIRROR / -ARRAY / PASTECLIP rzutu dolnego: znaczniki klonów → strefa klonu (CloneRemap).
// G528GA ponowne Prepare GA 528 — strefy zastąpione, pręty belek starych stref usunięte.
// G528UB Mesh (225) + belka: strzemiona niezależnie od UB 03 (K2): pierwsze 51 od lica, rozstaw ≤ 200.
// M528  belka → Mesh: rozkłady siatki (bez kodów belki) = zrzut D528 tego przebiegu (Mesh bez belki; numery i odwołania znormalizowane).
// S528  RC_SECTION A prostopadle do belki 528: kropki BM dół (−305) i góra (−55), strzemię 51 w widoku (−319 … −41), pręty w nibie 2T+1B.
//       Decyzja 2026-10-11 (SectionPlanner.Version 4): bez kropek siatki płyty RÓWNOLEGŁEJ do belki w trzonie belki (S528, C17 — asercja),
//       pręty siatki prostopadłe do belki w widoku do 40 od lica zewnętrznego (S528 — asercja), w nibie 3 kropki jednej pozycji (2T+1B).
// S528C przekrój „sprzed E5” → NIEAKTUALNY; H 375 → 450 → „zmieniona belka”; RC_SECTION_UPDATE → aktualne.
// S539  RC_SECTION B wzdłuż belki 539: BM w widoku na obu poziomach (−230 / −55), strzemiona (−244 … −41), profil −300.
// S539BBS RC_BBS / RC_SCHEDULE / RC_COUNT_BBS / .xls: wszystkie pozycje belki w serii dolnej (BM / BU / BL / BN; 00 / 21 / 51).
// G528V1 rekord RC_BEAM v1 (wersja testowa) → generowanie: komunikat migracji, rekord zapisany jako v2, strzemiona H12.
// K4    G528S: H 528 → 600 — pręty boczne BS H12 „2x2 H12-nn SIDE” i UB SIDE (BC, kształt 21, B 485) „2x1 H12-nn UB SIDE” na końcach.
// K5    zastąpione decyzją 2026-10-11: S528 — pręty w nibie jedną pozycją BN „2+1 H10-nn T+B IN NIB” (w rzucie 2 pręty, CountDisplay 3,
//       numer własny — nie pręt siatki B2), bez T IN NIB przy belce.
// K6    cofnięte 2026-10-11 (przekrój jak e1644bd, SectionPlanner.Version 1): BU i UB SIDE (BC) pominięte w przekroju, bez odgięć 45°.
// Z1–Z6 2026-10-11 (SectionPlanner.Version 3): symbole zakończeń prętów S1 (kreska 45° 30 mm do wnętrza), zakład odsunięty o 25 mm.
// K8    C17 / C3: Prepare GA C12 (kopie GA P06 bez ramek arkusza SD-PILED-RAFT; folder RC_BEAM_C12) — strefy z opisów „xxxTHK”
//       (PLOT 17: 450; PLOT 3: 600 / 675 / 975), C17PDF — kopia z przekrojem przez belkę do PDF; S319 / S319PDF — przekrój przez prawą nogę 319.
// K9    (decyzja użytkownika 2026-10-11) detal belki 1:25 — rekord RC_BEAM [36]: wszystkie kroki wyżej w trybie rzutu ([36] = 0, helper
//       GenerateBeams — jak przed K9; G528V1: rekord v1 → domyślny detal). G528DET — rzut → detal: ten sam zbiór rozkładów (kod, numer, liczba,
//       CountDisplay, długość), położenia = rzut + Shift, skala opisów 0,5, zero rozkładów belki na rzutach, ANCHOR, tytuł, odnośniki na obu
//       rzutach, nic nowego na SD-Pile / SD-PILED-RAFT / SD-Text / RC-BEAM; G528DETR — ponowne Beam (ten sam numer i miejsce, bez duplikatów),
//       MOVE ANCHOR-a → detal w nowym miejscu z tym samym numerem; G528DETU / G528DETD — UNDO / REDO; G528DETC — COPY _ALL (klony → klon strefy);
//       S528DET — przekrój A z prętami w detalu = przekrój z prętami na rzucie (CHECK aktualny); G539DET (L), S539BBSDET (BBS / SCHEDULE /
//       COUNT_BBS rzut = detal), S319DET (319, przekrój D rzut = detal); DET528PDF / DET539PDF — kopie z layoutem rzut 1:50 + detal 1:25 do PDF.
// L*    (decyzje użytkownika 2026-10-11 — moduł belki) wszystkie kroki wyżej ze strzemionami 2-ciętymi ([37] = 2, helper GenerateBeams);
//       L600 — 528 jako 600 × 600 Auto → 4-cięte: komunikat normy, jedna pozycja BL 515 × 355, „2xn H12-nn-200 LINKS”, BBS 2 × rozstawy,
//       przekrój — 2 zachodzące strzemiona i 1 opis, PDF (rzut + przekrój, detal 1:25); L528A / L539A — Auto na Holbrook (4-cięte; 539 —
//       ostrzeżenie, ramiona 188 > 172,5), ręczne 2-cięte — ostrzeżenie; LH20 — H16 → H20 w rekordzie i ponowne Beam; LMAN — polilinia
//       na RC-BEAM-MANUAL → strefy na obu rzutach (H z „450THK”, belka w płycie — Auto 2-cięte), bez duplikatów, ponowne Prepare GA.
// L528NN (decyzja użytkownika 2026-10-11 „opcja dodania lub nie dodawania nibu”) — 528 z nibem (rekord [38] = 0, BBS bazowy), potem
//       „Nib przy belce” = nie ([38] = 1): belka przesunięta do krawędzi (szer. 600 z GA, strzemię 510, B2 / T2 od nowego lica), bez BN / UB 03 / T IN NIB / UB 01/02
//       przy belce, B1 / T1 do 40 od krawędzi, przekrój bez nibu, detal bez linii uskoku, BBS z / bez nibu, PDF (rzut + przekrój, rzut + detal).
// UB3   (decyzja użytkownika 2026-10-11 „UB w nib też pokazuj na detalu”) G528UB3 / G539UB3: UB 03
//       przy belce w trybie detalu tylko w detalu (znacznik strefy, skala 0,5), liczby jak na rzucie (528: [4,4,53], BBS 03 = 61; 539:
//       [4,13,25,46], 88 — korekta użytkownika: „1 a nie 4” dotyczy U-barów belki BU), kolejność Mesh / Beam dowolna, tryb rzutu — jak dotąd,
//       przekrój A z detalu = z rzutu (UB 03 w przekroju).

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
            "S528", "S528PDF", "S528C", "S539", "S539BBS", "S539PDF", "G528V1", "G528S", "S319", "S319PDF",
            "G528DET", "G528DETR", "G528DETU", "G528DETD", "G528DETC", "S528DET", "G528UB3", "DET528PDF", "G539DET", "S539BBSDET", "G539UB3", "DET539PDF", "S319DET",
            "C17", "C17PDF", "C3",
            "L600", "L528A", "L539A", "LH20", "LMAN", "L528NN",
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
            Run(step, db => Check(step, "Prepare GA 526 (bez stref) — zrzut pełny = HEAD (poza warstwą RC-BEAM-MANUAL — decyzja 2026-10-11, punkt D)", () =>
            {
                CleanModelSpace(db);
                ImportGa("PLOT 526");
                var lines = RegressionDump.DrawingDump.Run(db, false, null, true);
                // decyzja użytkownika 2026-10-11 (punkt D): Prepare GA tworzy warstwę RC-BEAM-MANUAL — jedyna oczekiwana różnica (wiersz warstwy)
                if (Tag != "head") lines = lines.Where(l => !l.StartsWith("layer RC-BEAM-MANUAL ", StringComparison.Ordinal)).ToList();
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
            Check(step, "K7 (E2) + decyzja 2026-10-11: siatka przy belce — równoległe dół ≥ lico + 40 + d, góra ≥ lico + s/2, prostopadłe dół i góra przez belkę do 40 od lica zewnętrznego, krawędzie przy belce bez UB 01/02, UB 03 co 100…250, bez T IN NIB przy belce (2T+1B z belki)", () =>
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
                Check(step, "K3: U-bary pionowe — 2 narożniki × 2 nogi + 2 końce (6 rozkładów × 4; końce — 1 pręt reprezentatywny w rzucie, decyzja 2026-10-11), noga Y B 155, pozostałe B 190, końce 750 / 190 / 750; kółko na zagięciu, linie w strefie", () =>
                {
                    var u = Dists(db, "BU");
                    var sb = new StringBuilder(Int("U-bary (rozkłady)", u.Count, 6));
                    foreach (var x in u)
                    {
                        if (x.count != 4 || !x.mark.EndsWith("-150 UB") || x.shape != "21") throw new AssertFail("U-bar: " + x.count + " " + x.mark + " " + x.shape);
                        // koniec (A 750) — w rzucie 1 pręt reprezentatywny, narożnik L — 4 widoczne (decyzja 2026-10-11)
                        bool end = Math.Abs(x.length - 750) < 0.5;
                        Eq("widoczne pręty U-bara " + (end ? "końca" : "narożnika"), VisibleUBarLines(db, x.id), end ? 1 : 4);
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
                Check(step, "K3: U-bary pionowe na obu końcach (2 rozkłady × 4, 750 / 265 / 750, „4 H16-nn-150 UB”, w rzucie 1 pręt reprezentatywny z kółkiem na zagięciu — decyzja 2026-10-11), znaczniki", () =>
                {
                    var u = Dists(db, "BU");
                    var sb = new StringBuilder(Int("U-bary (rozkłady)", u.Count, 2));
                    foreach (var x in u)
                    {
                        if (x.count != 4 || !x.mark.EndsWith("-150 UB")) throw new AssertFail("U-bar: " + x.count + " " + x.mark);
                        if (!System.Text.RegularExpressions.Regex.IsMatch(AnnotText(db, x.id), @"^4 H16-\d+-150 UB$")) throw new AssertFail("opis U-bara «" + AnnotText(db, x.id) + "»");
                        Eq("widoczne pręty U-bara końca", VisibleUBarLines(db, x.id), 1);
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
                    // decyzja 2026-10-11 „do 40mm side cover”: prostopadłe dołem i górą przechodzą przez belkę i kończą się 40 od LICA
                    // ZEWNĘTRZNEGO belki (step — lico zewnętrzne strefy = linia uskoku)
                    string qc = top ? (ax ? "T2" : "T1") : (ax ? "B2" : "B1");
                    double reach = double.MaxValue;
                    foreach (var d in dists.Where(x => x.code == qc && x.mark.EndsWith(" " + qc) && x.alongX != ax))
                    {
                        if (!d.scan.Any(sc => sc > oLo + 1 && sc < oHi - 1)) continue;
                        double end = outer < 0 ? d.p0 : d.p1;
                        if (Math.Abs(end - step) > 1500) continue;   // pręt nie dochodzi do belki
                        double off = (end - step) * sgn;
                        if (off < 40 - 0.5) throw new AssertFail($"{qc} {d.mark}: koniec {off:0.#} od lica zewnętrznego belki (otulina < 40)");
                        reach = Math.Min(reach, off);
                    }
                    if (reach != double.MaxValue && Math.Abs(reach - 40) > 0.5) throw new AssertFail($"{qc}: koniec prętów {reach:0.#} od lica zewnętrznego belki (oczekiwano 40)");
                    if (!top)
                    {
                        sb.Append($"odcinek {(ax ? "X" : "Y")} {oLo:0}..{oHi:0}: {pc} pierwszy {F(first)} od lica, {qc} koniec {F(reach)} od lica zewnętrznego; ");
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
                        // UB 03 przy belce: liczba z prętów dołu prostopadłych (koniec 40 od lica zewnętrznego) — rozstaw jak siatka (100…250)
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
                        sb.Append($"odcinek {(ax ? "X" : "Y")} (góra): {pc} pierwszy {F(first)} od lica, {qc} koniec {F(reach)} od lica zewnętrznego; ");
                        // decyzja 2026-10-11 „jedna pozycja bottom in nib i top”: przy belce BEZ T IN NIB płyty — pręty w nibie (2T+1B)
                        // jedną pozycją z RC_GENERATE → Beam (S528 / S539BBS)
                        var tinHere = dists.Where(x => x.mark.EndsWith(" T IN NIB") && x.alongX == ax
                                                     && Math.Min(x.p1, oHi) - Math.Max(x.p0, oLo) > 1
                                                     && x.scan.All(sc => Math.Abs(sc - step) < 300)).ToList();   // ta krawędź (nie równoległa naprzeciw)
                        if (tinHere.Count > 0)
                            throw new AssertFail("T IN NIB przy belce (oczekiwano brak — pręty w nibie 2T+1B z belki): " + string.Join(" | ", tinHere.Select(x => x.mark)));
                        sb.Append("T IN NIB przy belce: brak (2T+1B z belki); ");
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
                Check(step, "A: profil (spód −375, nib belki −375…−225), kropki BM 4 dół (−305) i 4 góra (−55, warstwa górna), strzemię 51 w widoku (−319…−41), wymiar H 375; pręty w nibie 2T+1B — jedna pozycja BN H10, opis „2+1 … T+B IN NIB” przy nibie, 3 kropki (2 pod górnym ramieniem UB 03, 1 w dolnym narożu); bez BS / BN górą", () =>
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
                    // decyzja 2026-10-11 „jedna pozycja bottom in nib i top”: 3 kropki jednej pozycji w nibie belki — 2 górą (pod górnym
                    // ramieniem UB 03, ten sam poziom), 1 dołem (nad dolnym ramieniem UB 03: + Ø UB / 2 + d / 2 = 5 + 5, H10)
                    double vNb = ubP != null ? ubP.Min(p => p.Item2) + 5 + 5 : -315;
                    double vNt = ubP != null ? ubP.Max(p => p.Item2) - 5 - 5 : -235;
                    var nbD = At(vNb, nibP);
                    var ntD = At(vNt, nibP);
                    if (nbD.Count != 1 || ntD.Count != 2) throw new AssertFail($"pręty w nibie 2T+1B (dołem v {vNb:0.#}: {nbD.Count}, górą v {vNt:0.#}: {ntD.Count}, UB 03: {(ubP != null ? "tak" : "nie")}); {All()}");
                    if (Pos(nbD) != Pos(ntD)) throw new AssertFail($"pręty w nibie — jedna pozycja dół / góra: {Pos(nbD)} / {Pos(ntD)}");
                    if (ntD.Any(d => !(bool)Prop(d, "Top")) || nbD.Any(d => (bool)Prop(d, "Top"))) throw new AssertFail("flaga warstwy górnej kropek w nibie");
                    // dołem pod prętem górą bliższym krawędzi płyty (dalej od trzonu belki)
                    double uOuter = NibLeftOfTrunk(nibP, trz) ? ntD.Min(d => Dv(d, "U")) : ntD.Max(d => Dv(d, "U"));
                    if (Math.Abs(Dv(nbD[0], "U") - uOuter) > 0.5) throw new AssertFail($"pręt dołem w nibie nie pod prętem przy krawędzi: {All()}");
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
                    if (bn.Count == 0 || bn.Any(x => !x.mark.StartsWith("H10-") || !x.mark.EndsWith(" T+B IN NIB"))) throw new AssertFail("BN: " + string.Join(" | ", bn.Select(x => x.mark)));
                    // w rzucie 2 pręty (górą), opis / BBS 3 (CountDisplay [27]); numer — zasada „identyczny pręt = ta sama pozycja” (ARCHITECTURE §3):
                    // wspólny z prętem siatki B2 tylko, gdy pręt identyczny (H10, ta sama długość) — bez dopasowania podziału do siatki (koniec K5)
                    foreach (var x in bn)
                    {
                        var v = RawOf(db, x.id, DistApp);
                        int cd = v.Length > 27 ? Convert.ToInt32(v[27].Value, Inv) : -1;
                        if (x.count != 2 || cd != 3) throw new AssertFail($"BN {x.mark}: w rzucie {x.count} (oczekiwano 2), CountDisplay {cd} (oczekiwano 3)");
                    }
                    // poprawka 2026-10-11: opis „2+1 … T+B IN NIB” stoi przy nibie — koniec linii odniesienia poza płytą po stronie nibu
                    // (najbliższy bok obrysu względem rozkładu), linia nie biegnie przez płytę
                    var sbx = Box(Verts(db, _b528));
                    var nibLbl = new List<string>();
                    foreach (var x in bn)
                    {
                        var a = AnnotOf(db, x.id);
                        if (a.IsNull) throw new AssertFail($"BN {x.mark}: brak opisu");
                        var av = RawOf(db, a, "RC_BAR_ANNOT");
                        var apos = With(db, a, e => ((BlockReference)e).Position);
                        var dpos = With(db, x.id, e => ((BlockReference)e).Position);
                        var pts = Convert.ToString(av[17].Value, Inv).Split(';').Where(t => t.Contains(','))
                                  .Select(t => t.Split(',')).Select(t => (double.Parse(t[0], Inv), double.Parse(t[1], Inv))).ToList();
                        if (pts.Count < 2) throw new AssertFail($"BN {x.mark}: linia odniesienia {av[17].Value}");
                        double tx = apos.X + pts[pts.Count - 1].Item1, ty = apos.Y + pts[pts.Count - 1].Item2;
                        // strona nibu — w poprzek prętów (pręty wzdłuż Y: lewy / prawy bok, wzdłuż X: dolny / górny)
                        bool barsY = string.Equals(Convert.ToString(RawOf(db, x.id, DistApp)[6].Value, Inv), "Y", StringComparison.OrdinalIgnoreCase);
                        var sides = barsY
                            ? new[] { (d: dpos.X - sbx.x0, ok: tx < sbx.x0, n: "lewy"), (d: sbx.x1 - dpos.X, ok: tx > sbx.x1, n: "prawy") }
                            : new[] { (d: dpos.Y - sbx.y0, ok: ty < sbx.y0, n: "dolny"), (d: sbx.y1 - dpos.Y, ok: ty > sbx.y1, n: "górny") };
                        var near = sides.OrderBy(t => Math.Abs(t.d)).First();
                        if (!near.ok) throw new AssertFail($"BN {x.mark}: opis ({tx:0},{ty:0}) nie poza płytą po stronie nibu ({near.n} bok obrysu) — linia przez płytę?");
                        nibLbl.Add($"{near.n} ({tx:0},{ty:0})");
                    }
                    var b2 = Dists(db, "B2").Where(x => x.mark.EndsWith(" B2")).ToList();
                    foreach (var x in bn)
                        if (b2.Any(y => y.pos == x.pos && Math.Abs(y.length - x.length) > 0.5))
                            throw new AssertFail($"BN {x.mark} L{x.length:0} ma numer pręta B2 o innej długości: " + string.Join(" | ", b2.Where(y => y.pos == x.pos).Select(y => y.mark + " L" + y.length.ToString("0", Inv))));
                    var shared = bn.Where(x => b2.Any(y => y.pos == x.pos)).Select(x => x.pos).Distinct().ToList();
                    if (Dists(db, "T1").Concat(Dists(db, "T2")).Any(x => x.mark.EndsWith(" T IN NIB") && bn.Any(y => y.pos == x.pos)))
                        throw new AssertFail("BN ma numer T IN NIB");
                    return $"BM poz. {Pos(bot)} ×4 −305 / ×4 −55, w nibie {Pos(nbD)} górą 2× {vNt:0.#} / dołem {vNb:0.#}, strzemię {Prop(links[0], "PosNr")} −319..−41; BN: {string.Join(", ", bn.Select(x => x.mark + " L" + x.length.ToString("0", Inv)))}; opisy BN: {string.Join(", ", nibLbl)}; numer wspólny z identycznym B2: {(shared.Count > 0 ? string.Join(",", shared) : "brak")}";
                });
                Check(step, "decyzje 2026-10-11 (SectionPlanner.Version 4): bez kropek siatki równoległej w trzonie belki; pręty siatki prostopadłe w widoku do 40 od lica zewnętrznego belki; bez kropek T IN NIB (pręty w nibie 2T+1B z belki)", () =>
                {
                    if (plan == null) throw new AssertFail("brak planu");
                    string mesh = AssertMeshBesideBeam(ctx, plan);
                    var sg = L(plan, "Segments").First(x => Prop(x, "Prof") != null);
                    var prof = L(sg, "Prof").Select(p => (ValueTuple<double, double, double, double>)p).ToList();
                    var nibP = prof.FirstOrDefault(p => Math.Abs(p.Item3 + 375) < 1e-6 && Math.Abs(p.Item4 + 225) < 1e-6);
                    // pręty siatki prostopadłe do belki (w widoku, dół i góra): koniec w trzonie 40 od lica zewnętrznego (strona nibu belki)
                    var trz = prof.FirstOrDefault(p => Math.Abs(p.Item3 + 375) < 1e-6 && p.Item4 == 0);
                    bool leftOuter = Math.Abs(nibP.Item2 - trz.Item1) < 1;
                    double outerFace = leftOuter ? trz.Item1 : trz.Item2;
                    var ends = new List<string>();
                    foreach (var bar in L(plan, "Bars").Where(b => Convert.ToString(Prop(b, "Kind"), Inv) == "Main"))
                    {
                        var path = ((System.Collections.IEnumerable)(Prop(bar, "LabelPath") ?? Prop(bar, "Path"))).Cast<object>()
                                   .Select(p => (ValueTuple<double, double>)p).ToList();
                        double e = leftOuter ? path.Min(p => p.Item1) : path.Max(p => p.Item1);
                        double off = (e - outerFace) * (leftOuter ? 1 : -1);
                        if (off < -1 || off > trz.Item2 - trz.Item1) continue;      // koniec poza trzonem belki
                        if (Math.Abs(off - 40) > 1) throw new AssertFail($"pręt siatki poz. {Prop(bar, "PosNr")} w widoku: koniec {off:0.#} od lica zewnętrznego belki (oczekiwano 40)");
                        ends.Add($"{Prop(bar, "PosNr")}@{off:0.#}");
                    }
                    if (ends.Count < 2) throw new AssertFail("pręty siatki prostopadłe w trzonie belki (dół i góra): " + ends.Count);
                    var tin = L(plan, "Dots").Where(d => (bool)Prop(d, "TinNib")).ToList();
                    string T() => string.Join(" ", tin.Select(d => $"{Dv(d, "U"):0.#},{Dv(d, "V"):0.#}"));
                    if (tin.Count != 0) throw new AssertFail($"kropki T IN NIB przy belce: {tin.Count} ({T()}) — oczekiwano 0 (2T+1B z belki)");
                    return $"siatka przy belce: {mesh}; prostopadłe od lica zewnętrznego: {string.Join(" ", ends)}; T IN NIB: brak";
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
                    // K2: H 300 → bez prętów bocznych (B3); BN górą 0; pręty w nibie jedną pozycją 2T+1B (decyzja 2026-10-11)
                    if (Dists(db, "BS").Count != 0) throw new AssertFail("BS przy H 300: " + Dists(db, "BS").Count);
                    var bn = Dists(db, "BN");
                    if (bn.Any(x => x.mark.EndsWith(" BEAM NIB T")) || !bn.Any(x => x.mark.EndsWith(" T+B IN NIB"))) throw new AssertFail("BN: " + string.Join(" | ", bn.Select(x => x.mark)));
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
                    // decyzja 2026-10-11: pręty w nibie (2T+1B) — jeden wiersz BBS na pozycję, 3 sztuki na rozkład (w rzucie 2 pręty); pozycja wspólna
                    // z identycznym prętem siatki (ARCHITECTURE §3) — wiersz = suma wszystkich rozkładów (sprawdzone wyżej)
                    var pairs = Dists(db, "BN").Where(x => x.mark.EndsWith(" T+B IN NIB")).GroupBy(x => x.pos).ToList();
                    if (pairs.Count == 0) throw new AssertFail("brak pozycji „T+B IN NIB”");
                    foreach (var g in pairs)
                    {
                        var r = rows.Where(x => (int)Prop(x, "BarMark") == g.Key).ToList();
                        int other = Ms(db, e => e is BlockReference && Raw(e, DistApp) is TypedValue[] v && v.Length > 18 && PosNr(Convert.ToString(v[1].Value, Inv)) == g.Key
                                                && !Convert.ToString(v[1].Value, Inv).EndsWith(" T+B IN NIB")).Sum(id => EffCount(RawOf(db, id, DistApp)));
                        if (r.Count != 1 || (int)Prop(r[0], "Total") != 3 * g.Count() + other)
                            throw new AssertFail($"poz. {g.Key} (2T+1B): wierszy {r.Count}, sztuk {(r.Count > 0 ? Prop(r[0], "Total") : 0)} (oczekiwano 3 × {g.Count()} + {other})");
                        sb.Append($"w nibie {g.Key:00}: 3 × {g.Count()} rozkł.{(other > 0 ? $" + {other} innych rozkładów tej pozycji" : "")} = {Prop(r[0], "Total")} szt.; ");
                    }
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
                Check(step, "generowanie: komunikat migracji v1, rekord zapisany jako v2 (39 pól z K9 [36], cięciem strzemion [37] i nibem [38] = 0 — wg geometrii, [1] = 2), strzemiona H12, BM, bez BS / BN górą", () =>
                {
                    var msgs = Messages(GenerateBeams(db, b, t));
                    var mig = msgs.FirstOrDefault(m => m.Contains("rekord RC_BEAM v1"));
                    if (mig == null) throw new AssertFail("brak komunikatu migracji: " + string.Join(" | ", msgs.Take(5)));
                    var v = RawOf(db, zb, BeamApp);
                    if (v.Length != 39 || Convert.ToInt32(v[1].Value, Inv) != 2 || Convert.ToInt32(v[38].Value, Inv) != 0) throw new AssertFail($"rekord po generowaniu: {v.Length} pól, [1] = {v[1].Value}");
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

        // ================================================================
        // K9 — detal belki 1:25 (decyzja użytkownika 2026-10-11)
        // ================================================================

        private const string DetailApp = "RC_BEAM_DETAIL";
        private const string DetailLayer = "RC-BEAM-DETAIL";

        /// <summary>Rozkład belki: kod, numer, liczba w rzucie, sztuki (CountDisplay), długość, punkt wstawienia, skala opisu [24].</summary>
        private sealed class DS
        {
            public ObjectId id; public string code, mark; public int pos, count, eff; public double len, x, y, scale;
            public string Key => $"{code}|{pos}|{count}|{eff}|{len.ToString("0.#", Inv)}";
        }

        private static void ResetCounter(Database db) => Call("BricsCadRc.Core.PositionCounter", "Reset", db);

        private static List<DS> BeamSnap(Database db)
            => AllBeamDists(db).Select(id =>
            {
                var v = RawOf(db, id, DistApp);
                var p = PosOf(db, id);
                string mark = Convert.ToString(v[1].Value, Inv);
                return new DS
                {
                    id = id, code = Convert.ToString(v[2].Value, Inv), mark = mark, pos = PosNr(mark), count = Convert.ToInt32(v[3].Value, Inv),
                    eff = EffCount(v), len = Convert.ToDouble(v[8].Value, Inv), x = p.X, y = p.Y,
                    scale = v.Length > 24 ? Convert.ToDouble(v[24].Value, Inv) : 1.0
                };
            }).ToList();

        /// <summary>Obiekty detalu (RC_BEAM_DETAIL): strefa, numer, rola.</summary>
        private static List<(ObjectId id, string zone, int nr, string role)> DetailObjs(Database db)
            => Ms(db, e => Raw(e, DetailApp) != null).Select(id =>
            {
                var v = RawOf(db, id, DetailApp);
                return (id, XLinkOf(db, id, DetailApp, 2), v.Length > 3 ? Convert.ToInt32(v[3].Value, Inv) : 0,
                        v.Length > 4 ? Convert.ToString(v[4].Value, Inv) : "");
            }).ToList();

        /// <summary>Handle obiektów na warstwach czytanych przez inne funkcje (detal nie może na nich nic dodać).</summary>
        private static HashSet<long> ProtectedLayerHandles(Database db)
        {
            var layers = new[] { "SD-Pile", "SD-PILED-RAFT", "SD-Text", BeamLayer };
            return new HashSet<long>(Ms(db, e => layers.Any(l => string.Equals(e.Layer, l, StringComparison.OrdinalIgnoreCase))).Select(id => id.Handle.Value));
        }

        private static Vector2d ShiftOfAnchor(Database db, ObjectId anchor, ObjectId zone)
        {
            var a = Box(Verts(db, anchor)); var z = Box(Verts(db, zone));
            return new Vector2d(a.x0 - z.x0, a.y0 - z.y0);
        }

        private static int _det528Nr;
        private static Vector2d _det528Shift;
        private static HashSet<long> _detGen1 = new HashSet<long>(), _detGen2 = new HashSet<long>();
        private static ObjectId _b539d, _t539d;

        /// <summary>
        /// K9: rzut → detal na tej samej płycie (pręty belki najpierw na rzucie, potem [36] = 1 i ponowne Beam): ten sam zbiór rozkładów,
        /// położenia = rzut + Shift (Shift z ANCHOR-a strefy dolnej), skala opisów 0,5, zero rozkładów belki w obrysach rzutów, ANCHOR / tytuł /
        /// odnośniki, nic nowego na warstwach czytanych przez inne funkcje, szablony ponownie użyte.
        /// </summary>
        private static string PlanVsDetail(Database db, ObjectId b, ObjectId t, out int nr, out Vector2d shift)
        {
            GenerateBeams(db, b, t);
            var plan = BeamSnap(db);
            if (plan.Count == 0) throw new AssertFail("brak rozkładów belki na rzucie");
            int tpl0 = Templates(db).Count;
            var prot0 = ProtectedLayerHandles(db);
            var msgs = Messages(GenerateBeamsDetail(db, b, t));
            var det = BeamSnap(db);
            var sb = new StringBuilder();
            // ten sam zbiór kod / numer / liczba / CountDisplay / długość
            var k0 = plan.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var k1 = det.Select(x => x.Key).OrderBy(x => x, StringComparer.Ordinal).ToList();
            if (!k0.SequenceEqual(k1)) throw new AssertFail($"zbiór rozkładów rzut ≠ detal: rzut [{string.Join(" ", k0)}] / detal [{string.Join(" ", k1)}]");
            Eq("szablony (ponownie użyte)", Templates(db).Count, tpl0);
            // ANCHOR strefy dolnej
            var zb = Zones(db).First(z => XLinkOf(db, z, BeamApp, 6) == Hx(b));
            var objs = DetailObjs(db).Where(o => o.zone == Hx(zb)).ToList();
            var anchors = objs.Where(o => o.role == "ANCHOR").ToList();
            if (anchors.Count != 1) throw new AssertFail("ANCHOR strefy: " + anchors.Count);
            var aid = anchors[0].id;
            nr = anchors[0].nr;
            if (nr <= 0) throw new AssertFail("numer detalu " + nr);
            if (!string.Equals(LayerOf(db, aid), DetailLayer, StringComparison.OrdinalIgnoreCase)) throw new AssertFail("warstwa ANCHOR-a " + LayerOf(db, aid));
            var (closed, color, lts) = With(db, aid, e => (((Polyline)e).Closed, e.ColorIndex, e.LinetypeScale));
            if (!closed || color != 1 || Math.Abs(lts - 12.5) > 1e-6) throw new AssertFail($"ANCHOR: zamknięty {closed}, kolor {color}, LTSCALE {lts}");
            var sh = ShiftOfAnchor(db, aid, zb);
            shift = sh;
            if (!SameShifted(Verts(db, aid), Verts(db, zb), sh)) throw new AssertFail("ANCHOR nie przystaje do strefy");
            // położenia = rzut + Shift (każdy rozkład rzutu ma odpowiednik w detalu)
            var free = det.ToList();
            foreach (var x in plan)
            {
                var m = free.FirstOrDefault(y => y.Key == x.Key && Math.Abs(y.x - (x.x + sh.X)) < 0.5 && Math.Abs(y.y - (x.y + sh.Y)) < 0.5);
                if (m == null) throw new AssertFail($"brak w detalu {x.mark} ({x.code}) w ({x.x + sh.X:0.#}, {x.y + sh.Y:0.#}) — rzut ({x.x:0.#}, {x.y:0.#}) + Shift");
                free.Remove(m);
            }
            if (det.Any(x => Math.Abs(x.scale - 0.5) > 1e-9)) throw new AssertFail("skala opisów w detalu: " + string.Join(",", det.Select(x => x.scale).Distinct()));
            if (plan.Any(x => Math.Abs(x.scale - 1.0) > 1e-9)) throw new AssertFail("skala opisów na rzucie ≠ 1");
            // zero rozkładów belki w obrysach rzutów
            foreach (var view in new[] { b, t }.Where(v => !v.IsNull))
            {
                var vv = Verts(db, view);
                var inside = det.Where(x => InPoly(vv, x.x, x.y)).ToList();
                if (inside.Count > 0) throw new AssertFail($"rozkłady belki na rzucie {Hx(view)}: {string.Join(", ", inside.Select(x => x.mark))}");
            }
            // tytuł i odnośniki na obu rzutach
            var titles = objs.Where(o => o.role == "TITLE").ToList();
            if (titles.Count != 1) throw new AssertFail("tytuł detalu: " + titles.Count);
            string tt = With(db, titles[0].id, e => ((MText)e).Contents);
            if (!tt.Contains("DETAIL '" + nr + "'") || !tt.Contains("SCALE 1:25")) throw new AssertFail("tytuł «" + tt + "»");
            var refs = objs.Where(o => o.role == "PLANREF").ToList();
            if (refs.Count != 2) throw new AssertFail("odnośniki DETAIL na rzutach: " + refs.Count);
            var zonesV = Zones(db).Select(z => Verts(db, z)).ToList();
            foreach (var r in refs)
            {
                var (txt, tip, h) = With(db, r.id, e =>
                {
                    var ml = (MLeader)e;
                    Point3d p0;
                    try { p0 = ml.GetFirstVertex(0); }
                    catch (System.Exception) { p0 = new Point3d(double.NaN, double.NaN, 0); }
                    return (ml.MText.Contents, p0, ml.MText.TextHeight);
                });
                if (!txt.Contains("DETAIL '" + nr + "'")) throw new AssertFail("odnośnik «" + txt + "»");
                if (!double.IsNaN(tip.X) && !zonesV.Any(z => InPoly(z, tip.X, tip.Y))) throw new AssertFail($"grot odnośnika ({tip.X:0},{tip.Y:0}) poza strefami");
                if (Math.Abs(h - 140) > 0.01) throw new AssertFail("wysokość odnośnika " + h);
            }
            int lines = objs.Count(o => o.role == "LINE"), piles = objs.Count(o => o.role == "PILE"), texts = objs.Count(o => o.role == "TEXT");
            if (lines == 0) throw new AssertFail("brak linii betonu w detalu");
            // nic nowego na warstwach czytanych przez inne funkcje
            var prot1 = ProtectedLayerHandles(db);
            if (!prot1.SetEquals(prot0)) throw new AssertFail($"zmiana obiektów na SD-Pile / SD-PILED-RAFT / SD-Text / RC-BEAM: było {prot0.Count}, jest {prot1.Count}");
            sb.Append($"DETAIL '{nr}', Shift ({sh.X:0}, {sh.Y:0}), rozkładów {det.Count} (= rzut), skala 0,5; linie {lines}, pale {piles}, teksty {texts}, odnośniki 2; ")
              .Append(string.Join(" | ", msgs.Where(m => m.Contains("DETAIL"))));
            return sb.ToString();
        }

        [CommandMethod("RCSMOKE_BEAM_G528DET", CommandFlags.Modal)]
        public void G528Det()
        {
            const string step = "G528DET";
            Run(step, db => Check(step, "528: rzut → detal 1:25 (ten sam zbiór, położenia = rzut + Shift, skala opisów 0,5, ANCHOR, tytuł, odnośniki, warstwy chronione bez zmian)", () =>
            {
                SetFormat("Polyline");
                CleanModelSpace(db);
                ResetCounter(db);   // licznik pozycji w rysunku (NOD) rośnie przez kroki — numery belki w serii dolnej < 101
                ImportGa("PLOT 528-529");
                (_b528, _t528) = Views(db);
                _z528 = Zones(db).FirstOrDefault(z => XLinkOf(db, z, BeamApp, 6) == Hx(_b528));
                string s = PlanVsDetail(db, _b528, _t528, out _det528Nr, out _det528Shift);
                _detGen1 = new HashSet<long>(AllBeamDists(db).Select(x => x.Handle.Value));
                return s;
            }));
        }

        [CommandMethod("RCSMOKE_BEAM_G528DETR", CommandFlags.Modal)]
        public void G528DetR()
        {
            const string step = "G528DETR";
            Run(step, db =>
            {
                Check(step, "ponowne Beam (detal): ten sam numer i miejsce, bez duplikatów (1 ANCHOR, 1 tytuł, 2 odnośniki), stare rozkłady usunięte", () =>
                {
                    int n0 = AllBeamDists(db).Count;
                    GenerateBeamsDetail(db, _b528, _t528);
                    var objs = DetailObjs(db).Where(o => o.zone == Hx(_z528)).ToList();
                    var a = objs.Where(o => o.role == "ANCHOR").ToList();
                    if (a.Count != 1 || objs.Count(o => o.role == "TITLE") != 1 || objs.Count(o => o.role == "PLANREF") != 2)
                        throw new AssertFail($"obiekty detalu: ANCHOR {a.Count}, tytuł {objs.Count(o => o.role == "TITLE")}, odnośniki {objs.Count(o => o.role == "PLANREF")}");
                    Eq("numer", a[0].nr, _det528Nr);
                    var s = ShiftOfAnchor(db, a[0].id, _z528);
                    if ((s - _det528Shift).Length > 0.01) throw new AssertFail($"miejsce zmienione: ({s.X:0},{s.Y:0}) ≠ ({_det528Shift.X:0},{_det528Shift.Y:0})");
                    var now = new HashSet<long>(AllBeamDists(db).Select(x => x.Handle.Value));
                    if (now.Overlaps(_detGen1)) throw new AssertFail("stare rozkłady detalu nie usunięte");
                    return Int("rozkłady belki", now.Count, n0) + $"; DETAIL '{a[0].nr}' w tym samym miejscu";
                });
                Check(step, "MOVE ANCHOR-a (+4000, +2500) i ponowne Beam: detal w nowym miejscu, ten sam numer, rozkłady = rzut + nowy Shift", () =>
                {
                    var a = DetailObjs(db).First(o => o.zone == Hx(_z528) && o.role == "ANCHOR").id;
                    var mv = new Vector3d(4000, 2500, 0);
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        ((Entity)tr.GetObject(a, OpenMode.ForWrite)).TransformBy(Matrix3d.Displacement(mv));
                        tr.Commit();
                    }
                    var before = BeamSnap(db);
                    GenerateBeamsDetail(db, _b528, _t528);
                    var objs = DetailObjs(db).Where(o => o.zone == Hx(_z528)).ToList();
                    var an = objs.Single(o => o.role == "ANCHOR");
                    Eq("numer po MOVE", an.nr, _det528Nr);
                    var s = ShiftOfAnchor(db, an.id, _z528);
                    var exp = _det528Shift + new Vector2d(mv.X, mv.Y);
                    if ((s - exp).Length > 0.01) throw new AssertFail($"Shift po MOVE ({s.X:0},{s.Y:0}) ≠ ({exp.X:0},{exp.Y:0})");
                    var after = BeamSnap(db);
                    foreach (var x in before)
                        if (!after.Any(y => y.Key == x.Key && Math.Abs(y.x - (x.x + mv.X)) < 0.5 && Math.Abs(y.y - (x.y + mv.Y)) < 0.5))
                            throw new AssertFail($"{x.mark}: brak w nowym miejscu");
                    _detGen2 = new HashSet<long>(AllBeamDists(db).Select(x => x.Handle.Value));
                    return $"Shift ({s.X:0}, {s.Y:0}); rozkładów {after.Count}";
                });
            });
        }

        [CommandMethod("RCSMOKE_BEAM_G528DETU", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void G528DetU()
        {
            const string step = "G528DETU";
            Run(step, db => Check(step, "po _.UNDO 1: detal z G528DET (rozkłady, ANCHOR w pierwotnym miejscu)", () =>
            {
                var now = new HashSet<long>(AllBeamDists(db).Select(x => x.Handle.Value));
                if (!now.SetEquals(_detGen1)) throw new AssertFail($"po UNDO {now.Count} rozkładów, wspólnych z G528DET {now.Count(_detGen1.Contains)} / {_detGen1.Count}");
                var a = DetailObjs(db).Where(o => o.zone == Hx(_z528) && o.role == "ANCHOR").ToList();
                if (a.Count != 1) throw new AssertFail("ANCHOR po UNDO: " + a.Count);
                var s = ShiftOfAnchor(db, a[0].id, _z528);
                if ((s - _det528Shift).Length > 0.01) throw new AssertFail($"ANCHOR po UNDO ({s.X:0},{s.Y:0})");
                return "OK (" + now.Count.ToString(Inv) + ")";
            }));
        }

        [CommandMethod("RCSMOKE_BEAM_G528DETD", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void G528DetD()
        {
            const string step = "G528DETD";
            Run(step, db => Check(step, "po _.REDO: detal po MOVE (rozkłady G528DETR)", () =>
            {
                var now = new HashSet<long>(AllBeamDists(db).Select(x => x.Handle.Value));
                if (!now.SetEquals(_detGen2)) throw new AssertFail($"po REDO {now.Count} rozkładów, wspólnych z G528DETR {now.Count(_detGen2.Contains)} / {_detGen2.Count}");
                var a = DetailObjs(db).Single(o => o.zone == Hx(_z528) && o.role == "ANCHOR");
                var s = ShiftOfAnchor(db, a.id, _z528);
                _det528Shift = s;
                return $"OK ({now.Count}), Shift ({s.X:0}, {s.Y:0})";
            }));
        }

        [CommandMethod("RCSMOKE_BEAM_G528DETC", CommandFlags.Modal)]
        public void G528DetC()
        {
            const string step = "G528DETC";
            Run(step, db =>
            {
                ObjectId cz = ObjectId.Null; List<ObjectId> cobj = null;
                Check(step, "COPY _ALL (+300000 Y): obiekty detalu i rozkłady detalu kopii → strefa kopii (CloneRemap)", () =>
                {
                    var zv = Verts(db, _z528);
                    cz = Zones(db).FirstOrDefault(z => SameShifted(Verts(db, z), zv, new Vector2d(0, 300000)));
                    if (cz.IsNull) throw new AssertFail("brak strefy-kopii");
                    var all = DetailObjs(db);
                    var orig = all.Where(o => o.zone == Hx(_z528)).ToList();
                    cobj = all.Where(o => o.zone == Hx(cz)).Select(o => o.id).ToList();
                    if (cobj.Count != orig.Count) throw new AssertFail($"obiekty detalu kopii → strefa kopii: {cobj.Count} (oryginał {orig.Count}); puste [2]: {all.Count(o => o.zone == "")}");
                    var ca = all.Single(o => o.zone == Hx(cz) && o.role == "ANCHOR");
                    if ((ShiftOfAnchor(db, ca.id, cz) - _det528Shift).Length > 0.01) throw new AssertFail("ANCHOR kopii — inny Shift");
                    var cd = AllBeamDists(db).Where(id => XLinkOf(db, id, TagApp, 1) == Hx(cz)).ToList();
                    int origD = AllBeamDists(db).Count(id => XLinkOf(db, id, TagApp, 1) == Hx(_z528));
                    if (cd.Count != origD) throw new AssertFail($"rozkłady kopii → strefa kopii: {cd.Count} (oryginał {origD})");
                    return $"obiekty detalu kopii {cobj.Count}, rozkłady {cd.Count}";
                });
                Check(step, "ponowne Beam ORYGINAŁU (detal) — detal kopii zostaje", () =>
                {
                    GenerateBeamsDetail(db, _b528, _t528);
                    int lost = (cobj ?? new List<ObjectId>()).Count(id => id.IsErased);
                    if (lost > 0) throw new AssertFail("usunięto obiekty detalu kopii: " + lost);
                    return "OK";
                });
            });
        }

        /// <summary>Wynik planera przekroju jako tekst (kropki, pręty w widoku, opisy, wymiary) — porównanie rzut / detal.</summary>
        private static string PlanSummary(object plan)
        {
            string R(object o, string n) => Dv(o, n).ToString("0.#", Inv);
            var dots = L(plan, "Dots").Select(d => $"{Prop(d, "PosNr")}@{R(d, "U")},{R(d, "V")}{((bool)Prop(d, "Beam") ? "B" : "")}{((bool)Prop(d, "Top") ? "T" : "")}")
                       .OrderBy(x => x, StringComparer.Ordinal);
            var bars = L(plan, "Bars").Select(b => $"{Prop(b, "Kind")}:{Prop(b, "PosNr")}:" + string.Join(";", ((System.Collections.IEnumerable)Prop(b, "Path")).Cast<object>()
                           .Select(p => (ValueTuple<double, double>)p).Select(p => p.Item1.ToString("0.#", Inv) + "," + p.Item2.ToString("0.#", Inv))))
                       .OrderBy(x => x, StringComparer.Ordinal);
            var lbl = L(plan, "Labels").Select(l => (string)Prop(l, "Text")).OrderBy(x => x, StringComparer.Ordinal);
            return "D[" + string.Join(" ", dots) + "] B[" + string.Join(" ", bars) + "] L[" + string.Join(" ", lbl) + "] dims=" + L(plan, "Dims").Count;
        }

        /// <summary>Przekrój z prętami na rzucie (narysowany, litera) → Beam w detalu → CHECK aktualny, plan z detalu = plan z rzutu.</summary>
        private static string SectionPlanVsDetail(Database db, ObjectId b, ObjectId t, Point2d p1, Point2d p2, Point2d look, Point3d tl, string letter)
        {
            GenerateBeams(db, b, t);
            var (ctx0, plan0) = DrawSection(db, b, p1, p2, look, tl, letter);
            string s0 = PlanSummary(plan0);
            int views0 = L(ctx0, "Views").Count;
            GenerateBeamsDetail(db, b, t);
            if (BeamSnap(db).Any(x => InPoly(Verts(db, b), x.x, x.y))) throw new AssertFail("pręty belki zostały na rzucie");
            var info = Section(db, letter);
            if (StatusOf(info) != "Aktualny") throw new AssertFail($"{letter} po Beam (detal): {StatusOf(info)} — {string.Join(" | ", ReasonsOf(info))}");
            var ctx1 = Call(Engine, "PrepareSection", db, b, p1, p2, look) ?? throw new AssertFail("PrepareSection = null");
            var plan1 = Call(Planner, "Plan", Prop(ctx1, "Input"));
            string s1 = PlanSummary(plan1);
            if (L(ctx1, "Views").Count != views0) throw new AssertFail($"ctx.Views: {L(ctx1, "Views").Count} ≠ {views0}");
            if (s0 != s1)
            {
                int i = 0; while (i < Math.Min(s0.Length, s1.Length) && s0[i] == s1[i]) i++;
                throw new AssertFail($"przekrój z detalu ≠ z rzutu (znak {i}): rzut «…{s0.Substring(Math.Max(0, i - 40), Math.Min(120, s0.Length - Math.Max(0, i - 40)))}» / detal «…{s1.Substring(Math.Max(0, i - 40), Math.Min(120, s1.Length - Math.Max(0, i - 40)))}»");
            }
            var beams = L(Prop(ctx1, "Input"), "Dists").Count(d => Convert.ToString(Prop(d, "Kind"), Inv) == "Beam");
            return $"{letter}: aktualny po przejściu na detal; plan bit w bit (kropki {L(plan1, "Dots").Count}, pręty {L(plan1, "Bars").Count}, rozkłady belki w wejściu {beams})";
        }

        private static Point3d _tlDet528;

        [CommandMethod("RCSMOKE_BEAM_S528DET", CommandFlags.Modal)]
        public void S528Det()
        {
            const string step = "S528DET";
            Run(step, db => Check(step, "528: Mesh + belka na rzucie, RC_SECTION A; Beam → detal: A aktualny, przekrój z prętów detalu = z rzutu (S528)", () =>
            {
                SetFormat("Polyline");
                CleanModelSpace(db);
                ResetCounter(db);   // licznik pozycji w rysunku (NOD) rośnie przez kroki — numery belki w serii dolnej < 101
                ImportGa("PLOT 528-529");
                (_b528, _t528) = Views(db);
                Mesh(_b528, _t528, 225);
                _z528 = Zones(db).FirstOrDefault(z => XLinkOf(db, z, BeamApp, 6) == Hx(_b528));
                var zb = Box(Verts(db, _z528)); var sb = Box(Verts(db, _b528));
                double y = zb.y0 + 0.3 * (zb.y1 - zb.y0);
                _tlDet528 = new Point3d(sb.x0, sb.y0 - 9000, 0);
                return SectionPlanVsDetail(db, _b528, _t528, new Point2d(sb.x0 - 1000, y), new Point2d(zb.x1 + 3000, y), new Point2d(sb.x0, y + 100), _tlDet528, "A");
            }));
        }

        // ── decyzja użytkownika 2026-10-11 „UB w nib też pokazuj na detalu” ──

        /// <summary>UB 03 płyty (kod B1 / B2, poz. 03, „… UB”): liczba, środek obwiedni, skala opisu [24], strefa znacznika, tekst opisu.</summary>
        private sealed class U3 { public ObjectId id; public int count; public double cx, cy, scale; public string zone, text, slab, code; }

        private static List<U3> Ub03(Database db)
            => Ms(db, e => e is BlockReference && Raw(e, DistApp) is TypedValue[] v && v.Length > 8
                           && (Convert.ToString(v[2].Value, Inv) == "B1" || Convert.ToString(v[2].Value, Inv) == "B2")
                           && Convert.ToString(v[1].Value, Inv).EndsWith(" UB") && PosNr(Convert.ToString(v[1].Value, Inv)) == 3)
               .Select(id =>
               {
                   var v = RawOf(db, id, DistApp);
                   var (cx, cy) = With(db, id, e => { var x = e.GeometricExtents; return ((x.MinPoint.X + x.MaxPoint.X) / 2, (x.MinPoint.Y + x.MaxPoint.Y) / 2); });
                   return new U3
                   {
                       id = id, count = Convert.ToInt32(v[3].Value, Inv), cx = cx, cy = cy,
                       scale = v.Length > 24 ? Convert.ToDouble(v[24].Value, Inv) : 1.0, zone = XLinkOf(db, id, TagApp, 1), text = AnnotText(db, id),
                       slab = XLinkOf(db, id, "RC_AUTOREBAR_SLAB", 1), code = Convert.ToString(v[2].Value, Inv)
                   };
               }).ToList();

        private static string Counts(IEnumerable<U3> u) => "[" + string.Join(",", u.Select(x => x.count).OrderBy(x => x)) + "]";

        /// <summary>Diagnostyka UB 03: liczba, kod, środek, znacznik płyty / strefy, handle.</summary>
        private static string Diag(IEnumerable<U3> u)
            => string.Join("; ", u.Select(x => $"{x.count} {x.code} ({x.cx:0},{x.cy:0}) slab={x.slab} zone={x.zone} h={Hx(x.id)}"));

        /// <summary>Wiersz BBS pozycji nr: sztuki (0 — brak).</summary>
        private static int BbsTotal(Database db, int mark)
        {
            var bbs = Call("BricsCadRc.Core.BbsDrawingReader", "ReadBars", db);
            return L(bbs, "Rows").Where(r => (int)Prop(r, "BarMark") == mark).Sum(r => (int)Prop(r, "Total"));
        }

        /// <summary>
        /// UB 03 w trybie detalu: wszystkie UB 03 przy belce w detalu (znacznik strefy, środek w oknie ANCHOR-a ± 2000, skala 0,5), na rzucie
        /// tylko UB 03 poza belką (liczba <paramref name="planExpected"/>); liczby w detalu = <paramref name="detailExpected"/>; „1 H10-03 UB”.
        /// </summary>
        private static List<U3> Ub03InDetail(Database db, ObjectId b, ObjectId zone, string detailExpected, int planExpected)
        {
            var anchor = DetailObjs(db).Single(o => o.zone == Hx(zone) && o.role == "ANCHOR").id;
            var ab = Box(Verts(db, anchor));
            var sb = Box(Verts(db, b));
            var all = Ub03(db);
            var det = all.Where(x => x.zone == Hx(zone)).ToList();
            var plan = all.Where(x => x.zone == "").ToList();
            foreach (var x in det)
            {
                if (x.cx < ab.x0 - 2000 || x.cx > ab.x1 + 2000 || x.cy < ab.y0 - 2000 || x.cy > ab.y1 + 2000)
                    throw new AssertFail($"UB 03 ze znacznikiem strefy poza detalem ({x.cx:0}, {x.cy:0})");
                if (Math.Abs(x.scale - 0.5) > 1e-9) throw new AssertFail("skala opisu UB 03 w detalu " + x.scale);
                if (x.count == 1 && !x.text.StartsWith("1 H10-03 UB")) throw new AssertFail("opis UB 03 zamykającego «" + x.text + "»");
            }
            if (plan.Any(x => x.cx < sb.x0 - 1500 || x.cx > sb.x1 + 1500 || x.cy < sb.y0 - 1500 || x.cy > sb.y1 + 1500))
                throw new AssertFail("UB 03 bez znacznika poza rzutem");
            if (Counts(det) != detailExpected) throw new AssertFail($"UB 03 w detalu {Counts(det)}, oczekiwane {detailExpected}: {Diag(all)}");
            if (plan.Count != planExpected) throw new AssertFail($"UB 03 na rzucie {Counts(plan)}, oczekiwane {planExpected} rozkł.");
            // w położeniu rzutu (− Shift detalu) — porównania niezależne od miejsca detalu
            var s = ShiftOfAnchor(db, anchor, zone);
            return det.Select(x => new U3 { id = x.id, count = x.count, cx = x.cx - s.X, cy = x.cy - s.Y, scale = x.scale, zone = x.zone, text = x.text }).ToList();
        }

        /// <summary>Zbiory UB 03 równe (liczba i środek ± 0,5 mm).</summary>
        private static void SameUb03(List<U3> a, List<U3> b, string what)
        {
            var free = b.ToList();
            foreach (var x in a)
            {
                var m = free.FirstOrDefault(y => y.count == x.count && Math.Abs(y.cx - x.cx) < 0.5 && Math.Abs(y.cy - x.cy) < 0.5);
                if (m == null) throw new AssertFail($"{what}: brak UB 03 {x.count} szt. w ({x.cx:0.#}, {x.cy:0.#})");
                free.Remove(m);
            }
            if (free.Count > 0) throw new AssertFail($"{what}: nadmiarowe UB 03 {Counts(free)}");
        }

        [CommandMethod("RCSMOKE_BEAM_G528UB3", CommandFlags.Modal)]
        public void G528Ub3()
        {
            const string step = "G528UB3";
            Run(step, db =>
            {
                List<U3> det1 = null;
                int bbsDet = 0;
                Check(step, "528 (Mesh → Beam detal): UB 03 przy belce tylko w detalu — 53 wzdłuż belki + 4 + 4 w nibie zawiniętym (jak na rzucie; korekta 2026-10-11), skala 0,5, na rzucie bez UB 03 przy belce; BBS poz. 03 = 61", () =>
                {
                    det1 = Ub03InDetail(db, _b528, _z528, "[4,4,53]", 0);
                    bbsDet = BbsTotal(db, 3);
                    Eq("BBS poz. 03 (53 + 4 + 4)", bbsDet, 61);
                    return $"detal {Counts(det1)}, rzut 0; BBS 03 = {bbsDet}";
                });
                Check(step, "kolejność Beam → Mesh: ponowne Mesh przy istniejącym detalu — UB 03 od razu w detalu, te same liczby i położenia", () =>
                {
                    Mesh(_b528, _t528, 225);
                    var det2 = Ub03InDetail(db, _b528, _z528, "[4,4,53]", 0);
                    SameUb03(det1, det2, "Mesh po Beam");
                    Eq("BBS poz. 03", BbsTotal(db, 3), bbsDet);
                    det1 = det2;
                    return "UB 03 w detalu " + Counts(det2) + " (= Mesh → Beam)";
                });
                Check(step, "przełączenie detal → rzut (Beam, [36] = 0): UB 03 wracają na rzut (53 + 4 + 4, bez znacznika strefy), BBS 61; przekrój A z detalu = z rzutu; znów detal → jak wyżej", () =>
                {
                    var zb = Box(Verts(db, _z528)); var sb = Box(Verts(db, _b528));
                    double y = zb.y0 + 0.3 * (zb.y1 - zb.y0);
                    var p1 = new Point2d(sb.x0 - 1000, y); var p2 = new Point2d(zb.x1 + 3000, y); var look = new Point2d(sb.x0, y + 100);
                    string Plan()
                    {
                        var ctx = Call(Engine, "PrepareSection", db, _b528, p1, p2, look) ?? throw new AssertFail("PrepareSection = null");
                        return PlanSummary(Call(Planner, "Plan", Prop(ctx, "Input")));
                    }
                    string sDet = Plan();
                    GenerateBeams(db, _b528, _t528);
                    var all = Ub03(db);
                    if (all.Any(x => x.zone != "")) throw new AssertFail("UB 03 ze znacznikiem strefy po przełączeniu na rzut: " + Diag(all));
                    if (Counts(all) != "[4,4,53]") throw new AssertFail("UB 03 na rzucie " + Counts(all));
                    Eq("BBS poz. 03 (rzut)", BbsTotal(db, 3), 61);
                    string sPlan = Plan();
                    if (sDet != sPlan)
                    {
                        int i = 0; while (i < Math.Min(sDet.Length, sPlan.Length) && sDet[i] == sPlan[i]) i++;
                        throw new AssertFail($"przekrój A z detalu ≠ z rzutu (znak {i}): «…{sPlan.Substring(Math.Max(0, i - 40), Math.Min(120, sPlan.Length - Math.Max(0, i - 40)))}»");
                    }
                    if (!sPlan.Contains(":03:") && !sPlan.Contains("03@")) throw new AssertFail("brak UB 03 (pręt w widoku / kropki) w przekroju A");
                    // położenia: UB 03 wzdłuż belki w detalu = rzut + Shift
                    var main = all.Single(x => x.count == 53);
                    GenerateBeamsDetail(db, _b528, _t528);
                    var det3 = Ub03InDetail(db, _b528, _z528, "[4,4,53]", 0);
                    var m3 = det3.Single(x => x.count == 53);
                    if (Math.Abs(m3.cx - main.cx) > 0.5 || Math.Abs(m3.cy - main.cy) > 0.5)
                        throw new AssertFail($"UB 03 wzdłuż belki: detal − Shift ({m3.cx:0.#}, {m3.cy:0.#}) ≠ rzut ({main.cx:0.#}, {main.cy:0.#})");
                    SameUb03(det1, det3, "detal po powrocie");
                    Eq("BBS poz. 03 (detal)", BbsTotal(db, 3), 61);
                    return $"rzut [4,4,53] / BBS 61; przekrój A detal = rzut (UB 03 w przekroju); detal ponownie {Counts(det3)} / BBS 61";
                });
            });
        }

        [CommandMethod("RCSMOKE_BEAM_G539UB3", CommandFlags.Modal)]
        public void G539Ub3()
        {
            const string step = "G539UB3";
            Run(step, db =>
            {
                var z = Zones(db).First(x => XLinkOf(db, x, BeamApp, 6) == Hx(_b539d));
                List<U3> det1 = null;
                Check(step, "539 (Beam detal → Mesh): UB 03 przy belce (L) w detalu — 46 + 25 + 13 wzdłuż nóg + 4 w nibie zawiniętym przy końcu (jak na rzucie), na rzucie bez UB 03; BBS poz. 03 = 88", () =>
                {
                    Mesh(_b539d, _t539d, 225);
                    det1 = Ub03InDetail(db, _b539d, z, "[4,13,25,46]", 0);
                    Eq("BBS poz. 03", BbsTotal(db, 3), 88);
                    return "detal " + Counts(det1);
                });
                Check(step, "ponowne Beam (detal) po Mesh: UB 03 te same (kolejność dowolna)", () =>
                {
                    GenerateBeamsDetail(db, _b539d, _t539d);
                    var det2 = Ub03InDetail(db, _b539d, z, "[4,13,25,46]", 0);
                    SameUb03(det1, det2, "Beam po Mesh");
                    Eq("BBS poz. 03", BbsTotal(db, 3), 88);
                    return "OK " + Counts(det2);
                });
                Check(step, "tryb rzutu ([36] = 0): UB 03 na rzucie [4,13,25,46], BBS 88; z powrotem detal", () =>
                {
                    GenerateBeams(db, _b539d, _t539d);
                    var all = Ub03(db);
                    if (all.Any(x => x.zone != "") || Counts(all) != "[4,13,25,46]") throw new AssertFail("UB 03 na rzucie " + Counts(all) + ": " + Diag(all));
                    Eq("BBS poz. 03 (rzut)", BbsTotal(db, 3), 88);
                    GenerateBeamsDetail(db, _b539d, _t539d);
                    SameUb03(det1, Ub03InDetail(db, _b539d, z, "[4,13,25,46]", 0), "detal po powrocie");
                    return "rzut [4,13,25,46] / BBS 88; detal ponownie";
                });
            });
        }

        [CommandMethod("RCSMOKE_BEAM_DET528PDF", CommandFlags.Modal)]
        public void Det528Pdf()
        {
            const string step = "DET528PDF";
            Run(step, db => Check(step, "kopia rysunku z layoutem „DETAL 528” (rzut dolny 1:50 + detal 1:25) do PDF", () =>
                SaveDetailPdfCopy(db, "DETAL 528", "PLOT 528-529 — rzut dolny (strefa + DETAIL) i detal belki 1:25 (K9) — NA Smart RC (do testów)",
                                  _b528, _z528, "belka-528-det.dwg")));
        }

        [CommandMethod("RCSMOKE_BEAM_G539DET", CommandFlags.Modal)]
        public void G539Det()
        {
            const string step = "G539DET";
            Run(step, db => Check(step, "539 (L): rzut → detal 1:25 — jeden detal na strefę (L), ten sam zbiór, położenia = rzut + Shift", () =>
            {
                SetFormat("Polyline");
                CleanModelSpace(db);
                ResetCounter(db);   // licznik pozycji w rysunku (NOD) rośnie przez kroki — numery belki w serii dolnej < 101
                ImportGa("PLOT 539");
                (_b539d, _t539d) = Views(db);
                string s = PlanVsDetail(db, _b539d, _t539d, out int nr, out _);
                int anchors = DetailObjs(db).Count(o => o.role == "ANCHOR");
                if (anchors != 1) throw new AssertFail("ANCHOR-y (jeden detal na strefę L): " + anchors);
                return s;
            }));
        }

        /// <summary>Wiersze BBS (numer, Ø, kształt, sztuki, długość, masa) jako tekst.</summary>
        private static string BbsSummary(Database db)
        {
            var bbs = Call("BricsCadRc.Core.BbsDrawingReader", "ReadBars", db);
            return string.Join(" ", L(bbs, "Rows").OrderBy(x => (int)Prop(x, "BarMark"))
                .Select(r => string.Format(Inv, "{0}/H{1}/{2}/{3}/{4:0}/{5:0.00}", (int)Prop(r, "BarMark"), (int)Prop(r, "Diameter"), Prop(r, "ShapeCode"),
                                           (int)Prop(r, "Total"), Dv(r, "Length"), Dv(r, "MassKg"))));
        }

        private static string ScheduleSummary(Database db)
        {
            var entries = ((System.Collections.IEnumerable)Call("BricsCadRc.Core.BarScheduleEngine", "BuildSchedule", db, new List<string>())).Cast<object>().ToList();
            return string.Join(" ", entries.Select(x => $"{Prop(x, "PosNr")}:{Prop(x, "TotalCount")}:{Dv(x, "CuttingLength"):0}").OrderBy(x => x, StringComparer.Ordinal));
        }

        private static double CountMass(Database db)
            => ((System.Collections.IEnumerable)Call("BricsCadRc.Core.BbsCounter", "CountAll", db, new List<string>())).Cast<object>().Sum(x => Dv(x, "TotalMassKg"));

        [CommandMethod("RCSMOKE_BEAM_S539BBSDET", CommandFlags.Modal)]
        public void S539BbsDet()
        {
            const string step = "S539BBSDET";
            Run(step, db => Check(step, "539: RC_BBS / RC_SCHEDULE / RC_COUNT_BBS — pręty belki na rzucie = w detalu (BBS liczy raz)", () =>
            {
                GenerateBeams(db, _b539d, _t539d);
                string b0 = BbsSummary(db), s0 = ScheduleSummary(db); double m0 = CountMass(db);
                GenerateBeamsDetail(db, _b539d, _t539d);
                string b1 = BbsSummary(db), s1 = ScheduleSummary(db); double m1 = CountMass(db);
                if (b0 != b1) throw new AssertFail($"RC_BBS rzut ≠ detal: «{b0}» / «{b1}»");
                if (s0 != s1) throw new AssertFail($"RC_SCHEDULE rzut ≠ detal: «{s0}» / «{s1}»");
                if (Math.Abs(m0 - m1) > 0.005) throw new AssertFail($"RC_COUNT_BBS rzut {m0:0.00} ≠ detal {m1:0.00}");
                return $"BBS = ({b1.Split(' ').Length} wierszy), masa {m1:0.00} kg";
            }));
        }

        [CommandMethod("RCSMOKE_BEAM_DET539PDF", CommandFlags.Modal)]
        public void Det539Pdf()
        {
            const string step = "DET539PDF";
            Run(step, db => Check(step, "kopia rysunku z layoutem „DETAL 539” (rzut dolny 1:50 + detal 1:25) do PDF", () =>
            {
                var z = Zones(db).First(x => XLinkOf(db, x, BeamApp, 6) == Hx(_b539d));
                return SaveDetailPdfCopy(db, "DETAL 539", "PLOT 539 — rzut dolny (strefa + DETAIL) i detal belki 1:25 (K9) — NA Smart RC (do testów)",
                                         _b539d, z, "belka-539-det.dwg");
            }));
        }

        [CommandMethod("RCSMOKE_BEAM_S319DET", CommandFlags.Modal)]
        public void S319Det()
        {
            const string step = "S319DET";
            Run(step, db => Check(step, "319: rzut → detal (ten sam zbiór, położenia = rzut + Shift) i RC_SECTION D przez prawą nogę — z detalu = z rzutu", () =>
            {
                SetFormat("Polyline");
                CleanModelSpace(db);
                ResetCounter(db);   // licznik pozycji w rysunku (NOD) rośnie przez kroki — numery belki w serii dolnej < 101
                ImportGa("Plot 319");
                var (b, t) = Views(db);
                Mesh(b, t, 225);
                string g = PlanVsDetail(db, b, t, out _, out _);
                var z = Zones(db).First(x => XLinkOf(db, x, BeamApp, 6) == Hx(b));
                var zb = Box(Verts(db, z)); var sb = Box(Verts(db, b));
                double y = zb.y1 - 1200;
                var tl = new Point3d(sb.x0, sb.y0 - 9000, 0);
                string s = SectionPlanVsDetail(db, b, t, new Point2d(zb.x1 - 3000, y), new Point2d(sb.x1 + 1000, y), new Point2d(zb.x1 - 3000, y + 100), tl, "D");
                return g + "; " + s;
            }));
        }

        /// <summary>
        /// K9 — layout do PDF (A1 / A0 poziomo, Print As PDF.pc3, speedeck-mono.ctb): rzutnia rzutu dolnego (strefa + odnośnik „DETAIL”)
        /// i rzutnia detalu 1:25 (skala dokładna, gdy detal mieści się na arkuszu; inaczej dopasowana — w tytule). Kopia rysunku w RC_BEAM_PDF_DIR.
        /// </summary>
        private static string SaveDetailPdfCopy(Database db, string layoutName, string title, ObjectId slab, ObjectId zone, string file)
        {
            string dir = Environment.GetEnvironmentVariable("RC_BEAM_PDF_DIR");
            if (string.IsNullOrWhiteSpace(dir)) dir = OutDir;
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, file);
            BbsDump(db, Path.Combine(dir, Path.GetFileNameWithoutExtension(file) + "-bbs.txt"));
            if (File.Exists(path)) File.Delete(path);

            // zakres detalu: obiekty RC_BEAM_DETAIL strefy (bez odnośników na rzutach) + rozkłady detalu i ich opisy
            var objs = DetailObjs(db).Where(o => o.zone == Hx(zone) && o.role != "PLANREF").ToList();
            if (objs.Count == 0) throw new AssertFail("brak detalu strefy " + Hx(zone));
            Extents3d det = ExtOf(db, objs[0].id);
            foreach (var o in objs.Skip(1)) det.AddExtents(ExtOf(db, o.id));
            // pręty belki i UB 03 przy belce w detalu (decyzja 2026-10-11) — znacznik strefy
            foreach (var d in AllBeamDists(db).Concat(Ub03(db).Select(u => u.id)).Where(id => XLinkOf(db, id, TagApp, 1) == Hx(zone)))
            {
                det.AddExtents(ExtOf(db, d));
                var a = AnnotOf(db, d);
                if (!a.IsNull) det.AddExtents(ExtOf(db, a));
            }
            var sb = Box(Verts(db, slab));
            var planExt = new Extents3d(new Point3d(sb.x0 - 1500, sb.y0 - 1500, 0), new Point3d(sb.x1 + 1500, sb.y1 + 1500, 0));

            var lm = LayoutManager.Current;
            if (LayoutNames(db).Contains(layoutName)) lm.DeleteLayout(layoutName);
            var lid = lm.CreateLayout(layoutName);
            lm.CurrentLayout = layoutName;
            string media = "", scaleNote = "";
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lay = (Layout)tr.GetObject(lid, OpenMode.ForWrite);
                var psv = PlotSettingsValidator.Current;
                try
                {
                    psv.SetPlotConfigurationName(lay, "Print As PDF.pc3", null);
                    psv.RefreshLists(lay);
                    var all = psv.GetCanonicalMediaNameList(lay).Cast<string>().ToList();
                    // A1; detal większy niż ~ 480 × 540 mm w 1:25 — A0
                    double needW = (det.MaxPoint.X - det.MinPoint.X) / 25.0 + 10, needH = (det.MaxPoint.Y - det.MinPoint.Y) / 25.0 + 10;
                    bool a0 = needW > 841 * 0.6 - 24 || needH > 594 - 34;
                    media = (a0 ? all.FirstOrDefault(m => m.IndexOf("_A0_", StringComparison.OrdinalIgnoreCase) >= 0 && m.Contains("1189")) : null)
                            ?? all.FirstOrDefault(m => m.IndexOf("A1", StringComparison.OrdinalIgnoreCase) >= 0 && m.Contains("841"))
                            ?? all.FirstOrDefault(m => m.IndexOf("A3", StringComparison.OrdinalIgnoreCase) >= 0 && m.Contains("420")) ?? all.FirstOrDefault() ?? "";
                    if (media.Length > 0) psv.SetCanonicalMediaName(lay, media);
                    psv.SetPlotPaperUnits(lay, PlotPaperUnit.Millimeters);
                    psv.SetPlotRotation(lay, lay.PlotPaperSize.X >= lay.PlotPaperSize.Y ? PlotRotation.Degrees000 : PlotRotation.Degrees090);
                    psv.SetCurrentStyleSheet(lay, "speedeck-mono.ctb");
                }
                catch (System.Exception ex) { Msg($"\n{Prefix} Ustawienia wydruku: {ex.Message}\n"); }
                double W = Math.Max(lay.PlotPaperSize.X, lay.PlotPaperSize.Y), H = Math.Min(lay.PlotPaperSize.X, lay.PlotPaperSize.Y);
                if (W < 100 || H < 100) { W = 841; H = 594; }
                var btr = (BlockTableRecord)tr.GetObject(lay.BlockTableRecordId, OpenMode.ForWrite);
                var vps = btr.Cast<ObjectId>().Where(id => tr.GetObject(id, OpenMode.ForRead) is Viewport).ToList();
                foreach (var id in vps.Skip(1)) tr.GetObject(id, OpenMode.ForWrite).Erase();
                double m = 12, top = H - 22;
                double dw = (det.MaxPoint.X - det.MinPoint.X) / 25.0 + 10, dh = (det.MaxPoint.Y - det.MinPoint.Y) / 25.0 + 10;
                double availW = W * 0.6 - 2 * m, availH = top - m;
                bool exact = dw <= availW && dh <= availH;
                double vw = exact ? dw : availW, vh = exact ? dh : availH;
                scaleNote = exact ? "detal 1:25" : "detal — skala dopasowana (detal większy niż arkusz w 1:25)";
                void Vp(double x0, double y0, double x1, double y1, Extents3d ext, double viewHeight)
                {
                    var vp = new Viewport();
                    btr.AppendEntity(vp); tr.AddNewlyCreatedDBObject(vp, true);
                    vp.CenterPoint = new Point3d((x0 + x1) / 2, (y0 + y1) / 2, 0);
                    vp.Width = x1 - x0; vp.Height = y1 - y0;
                    vp.ViewCenter = new Point2d((ext.MinPoint.X + ext.MaxPoint.X) / 2, (ext.MinPoint.Y + ext.MaxPoint.Y) / 2);
                    vp.ViewHeight = viewHeight;
                    vp.On = true;
                }
                // detal po prawej (1:25: ViewHeight = wysokość rzutni · 25), rzut dolny po lewej (dopasowany)
                double dx1 = W - m, dx0 = dx1 - vw, dy0 = m, dy1 = m + vh;
                double ew = det.MaxPoint.X - det.MinPoint.X, eh = det.MaxPoint.Y - det.MinPoint.Y;
                Vp(dx0, dy0, dx1, dy1, det, exact ? vh * 25.0 : Math.Max(eh, ew * vh / vw) * 1.04);
                double px1 = dx0 - m;
                double pw = planExt.MaxPoint.X - planExt.MinPoint.X, ph = planExt.MaxPoint.Y - planExt.MinPoint.Y;
                // rzut dolny 1:50 (gdy mieści się na arkuszu; inaczej dopasowany)
                bool plan50 = pw / 50.0 <= px1 - m && ph / 50.0 <= top - m;
                if (plan50) Vp(m, top - ph / 50.0, m + pw / 50.0, top, planExt, ph);
                else Vp(m, m, px1, top, planExt, Math.Max(ph, pw * (top - m) / Math.Max(1, px1 - m)) * 1.04);
                scaleNote = (plan50 ? "rzut 1:50, " : "rzut — skala dopasowana, ") + scaleNote;
                var txt = new MText { Contents = title + " — " + scaleNote, TextHeight = 6, Location = new Point3d(m, H - 8, 0), Attachment = AttachmentPoint.TopLeft, Width = W - 2 * m };
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
            return $"{path} (layout „{layoutName}”, papier {media}, {scaleNote}); detal {det.MinPoint.X:0},{det.MinPoint.Y:0}..{det.MaxPoint.X:0},{det.MaxPoint.Y:0}";
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
                    // decyzja 2026-10-11 (SectionPlanner.Version 4): bez kropek siatki płyty równoległej do belki w trzonie belki
                    string e2 = AssertMeshBesideBeam(ctx, plan);
                    return $"belka: {string.Join(" | ", msgs.Where(m => m.Contains("strzemiona") || m.Contains("pręty w nibie") || m.Contains("U-bary pionowe")).Take(4))}; przekrój: pręty {L(plan, "Bars").Count}, kropki {L(plan, "Dots").Count}; E2: {e2}";
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

        // ================================================================
        // Decyzje użytkownika 2026-10-11 (moduł belki): A — listy średnic (H16 → H20), B — strzemiona 4-cięte jedną pozycją,
        // C — Auto wg EN 1992-1-1 §9.2.2(8) tylko dla belek krawędziowych, D — warstwa RC-BEAM-MANUAL
        // ================================================================

        private static Point3d _tl600, _tl528A;
        private static ObjectId _b600, _z600, _b528A, _z528A;

        /// <summary>Strzemiona 4-cięte rozkładu BL: Count n, CountDisplay 2n, opis „2xn H12-nn-200 LINKS”; zwraca (pozycja, n, opis).</summary>
        private static List<(int pos, int n, string text)> FourLegChecks(Database db, double a, double b)
        {
            var bl = Dists(db, "BL");
            if (bl.Count == 0) throw new AssertFail("brak strzemion BL");
            var res = new List<(int, int, string)>();
            foreach (var x in bl)
            {
                int eff = EffCount(RawOf(db, x.id, DistApp));
                string tx = AnnotText(db, x.id);
                if (eff != 2 * x.count) throw new AssertFail($"BL {x.mark}: CountDisplay {eff} ≠ 2 × {x.count}");
                if (!tx.StartsWith($"2x{x.count} H12-") || !tx.EndsWith("-200 LINKS")) throw new AssertFail("opis strzemion 4-ciętych «" + tx + "»");
                res.Add((x.pos, x.count, tx));
            }
            if (res.Select(r => r.Item1).Distinct().Count() != 1) throw new AssertFail("strzemiona 4-cięte — więcej niż jedna pozycja: " + string.Join(",", res.Select(r => r.Item1)));
            var tpl = Templates(db).Where(t => t.shape == "51" && t.pos == res[0].Item1).ToList();
            if (tpl.Count != 1 || Math.Abs(tpl[0].a - a) > 0.5 || Math.Abs(tpl[0].b - b) > 0.5)
                throw new AssertFail("szablon strzemienia 4-ciętego: " + string.Join(",", tpl.Select(t => $"{t.mark} {t.a:0}×{t.b:0}")) + $" (oczekiwano {a:0}×{b:0})");
            return res;
        }

        /// <summary>BBS: wiersz pozycji strzemion = 2 × suma liczby rozstawów, długość 51 wg BS 8666:2020 (A, B szablonu).</summary>
        private static string FourLegBbs(Database db, int pos, double a, double b)
        {
            var bbs = Call("BricsCadRc.Core.BbsDrawingReader", "ReadBars", db);
            var rows = L(bbs, "Rows").Where(x => (int)Prop(x, "BarMark") == pos).ToList();
            if (rows.Count != 1) throw new AssertFail($"BBS poz. {pos}: wierszy {rows.Count}");
            int drawn = Dists(db, "BL").Where(x => x.pos == pos).Sum(x => x.count);
            int total = (int)Prop(rows[0], "Total");
            if (total != 2 * drawn) throw new AssertFail($"BBS poz. {pos}: sztuk {total} ≠ 2 × {drawn}");
            double len = Convert.ToDouble(Call("BricsCadRc.Core.BarLengthRules", "CuttingLength", "51", 12.0, a, b, 0.0, 0.0, 0.0), Inv);
            if (Math.Abs(Dv(rows[0], "Length") - len) > 0.01) throw new AssertFail($"BBS poz. {pos}: L {Dv(rows[0], "Length")} ≠ {len}");
            var warns = L(bbs, "Warnings").Cast<string>().Where(w => w.StartsWith($"Pozycja {pos:00} ", StringComparison.Ordinal)).ToList();
            if (warns.Count > 0) throw new AssertFail("ostrzeżenie BBS: " + warns[0]);
            return string.Format(Inv, "BBS poz. {0:00}: 51 H12 {1:0}×{2:0} L{3:0} × {4} (= 2 × {5})", pos, a, b, len, total, drawn);
        }

        /// <summary>Przekrój prostopadle do belki: strzemię pozycji pos w widoku — 2 zachodzące (identyczne, przesunięte), 1 opis.</summary>
        private static string FourLegSection(object plan, int pos, double width)
        {
            string p = pos.ToString("00", Inv);
            var links = L(plan, "Bars").Where(b => Convert.ToString(Prop(b, "Kind"), Inv) == "Beam" && (string)Prop(b, "PosNr") == p).ToList();
            if (links.Count != 2) throw new AssertFail("strzemion poz. " + p + " w widoku: " + links.Count);
            var us = links.Select(b => ((System.Collections.IEnumerable)Prop(b, "Path")).Cast<ValueTuple<double, double>>().ToList())
                          .Select(pth => (u0: pth.Min(q => q.Item1), u1: pth.Max(q => q.Item1), v0: pth.Min(q => q.Item2), v1: pth.Max(q => q.Item2)))
                          .OrderBy(x => x.u0).ToList();
            if (Math.Abs((us[0].u1 - us[0].u0) - (us[1].u1 - us[1].u0)) > 0.01 || Math.Abs(us[0].v0 - us[1].v0) > 0.01 || Math.Abs(us[0].v1 - us[1].v1) > 0.01)
                throw new AssertFail("strzemiona nie są identyczne: " + string.Join(" | ", us));
            if (!(us[1].u0 < us[0].u1 - 1)) throw new AssertFail("strzemiona nie zachodzą: " + string.Join(" | ", us));
            int lbl = L(plan, "Labels").Count(l => (string)Prop(l, "Text") == p);
            if (lbl != 1) throw new AssertFail("opisy strzemion: " + lbl);
            return string.Format(Inv, "przekrój: 2 strzemiona poz. {0} u {1:0}..{2:0} i {3:0}..{4:0}, v {5:0}..{6:0}, 1 opis", p, us[0].u0, us[0].u1, us[1].u0, us[1].u1, us[0].v0, us[0].v1);
        }

        [CommandMethod("RCSMOKE_BEAM_L600", CommandFlags.Modal)]
        public void L600()
        {
            const string step = "L600";
            Run(step, db =>
            {
                List<(int pos, int n, string text)> fl = null;
                Check(step, "528 jako 600 × 600 (H 600, krawędziowa), Auto: komunikat normy „→ 4-cięte”, jedna pozycja BL 51 H12 515 × 355, opis „2xn H12-nn-200 LINKS”", () =>
                {
                    SetFormat("Polyline");
                    CleanModelSpace(db);
                    ImportGa("PLOT 528-529");
                    (_b600, _) = Views(db);
                    var (b, t) = Views(db);
                    Mesh(b, t, 225);
                    foreach (var z in Zones(db)) SetZoneHeight(db, z, 600);
                    var msgs = Messages(GenerateBeamsLegs(db, b, t, false, 0));
                    var note = msgs.FirstOrDefault(m => m.Contains("s_t,max")) ?? throw new AssertFail("brak komunikatu normy: " + string.Join(" | ", msgs.Take(6)));
                    if (!note.Contains("600×600: d = 530") || !note.Contains("498 > 397.5 → 4-cięte") || !note.Contains("1–3 i 2–4")) throw new AssertFail(note);
                    fl = FourLegChecks(db, 515, 355);
                    _z600 = Zones(db).First(z => XLinkOf(db, z, BeamApp, 6) == Hx(b));
                    return note.Substring(note.IndexOf(':') + 1).Trim() + $"; BL {fl.Count} rozkł. «{fl[0].text}»";
                });
                Check(step, "BBS: pozycja strzemion 4-ciętych — liczba = 2 × liczba rozstawów, długość 51 wg BS 8666:2020", () => FourLegBbs(db, fl[0].pos, 515, 355));
                Check(step, "RC_SECTION A prostopadle do belki: 2 identyczne zachodzące strzemiona, jeden opis", () =>
                {
                    var zb = Box(Verts(db, _z600)); var sb = Box(Verts(db, _b600));
                    double y = zb.y0 + 0.3 * (zb.y1 - zb.y0);
                    _tl600 = new Point3d(sb.x0, sb.y0 - 9000, 0);
                    var (_, plan) = DrawSection(db, _b600, new Point2d(sb.x0 - 1000, y), new Point2d(zb.x1 + 3000, y), new Point2d(sb.x0, y + 100), _tl600, "A");
                    return FourLegSection(plan, fl[0].pos, 600);
                });
                Check(step, "kopia rysunku z layoutem „BELKA 600” (rzut dolny + przekrój A) do PDF", () =>
                    SavePdfCopy(db, "BELKA 600", "PLOT 528-529 jako belka 600 × 600 (H 600) — strzemiona 4-cięte (Auto, EN 1992-1-1 §9.2.2(8)) — RC_SECTION A-A — NA Smart RC (do testów)",
                                _b600, _tl600, "belka-600-4ciete.dwg"));
                Check(step, "detal 1:25 (Auto, 4-cięte) i kopia z layoutem „DETAL 600” do PDF", () =>
                {
                    var (b, t) = Views(db);
                    GenerateBeamsLegs(db, b, t, true, 0);
                    var fl2 = FourLegChecks(db, 515, 355);
                    return $"BL w detalu: «{fl2[0].text}»; " + SaveDetailPdfCopy(db, "DETAL 600", "PLOT 528-529 jako belka 600 × 600 — detal 1:25, strzemiona 4-cięte (Auto) — NA Smart RC (do testów)",
                                                                             b, _z600, "belka-600-4ciete-det.dwg");
                });
            });
        }

        [CommandMethod("RCSMOKE_BEAM_L528A", CommandFlags.Modal)]
        public void L528A()
        {
            const string step = "L528A";
            Run(step, db =>
            {
                Check(step, "528 (600 × 375, krawędziowa), Auto: 4-cięte (s_t,max 228.75, ramiona 188), przekrój A — 2 strzemiona, PDF", () =>
                {
                    SetFormat("Polyline");
                    CleanModelSpace(db);
                    ImportGa("PLOT 528-529");
                    var (b, t) = Views(db);
                    _b528A = b;
                    Mesh(b, t, 225);
                    var msgs = Messages(GenerateBeamsLegs(db, b, t, false, 0));
                    var note = msgs.FirstOrDefault(m => m.Contains("s_t,max")) ?? throw new AssertFail("brak komunikatu normy");
                    if (!note.Contains("→ 4-cięte") || note.Contains("OSTRZEŻENIE")) throw new AssertFail(note);
                    var fl = FourLegChecks(db, 290, 355);
                    _z528A = Zones(db).First(z => XLinkOf(db, z, BeamApp, 6) == Hx(b));
                    var zb = Box(Verts(db, _z528A)); var sb = Box(Verts(db, b));
                    double y = zb.y0 + 0.3 * (zb.y1 - zb.y0);
                    _tl528A = new Point3d(sb.x0, sb.y0 - 9000, 0);
                    var (_, plan) = DrawSection(db, b, new Point2d(sb.x0 - 1000, y), new Point2d(zb.x1 + 3000, y), new Point2d(sb.x0, y + 100), _tl528A, "A");
                    string sec = FourLegSection(plan, fl[0].pos, 600);
                    string bbs = FourLegBbs(db, fl[0].pos, 290, 355);
                    string pdf = SavePdfCopy(db, "BELKA 528 AUTO", "PLOT 528-529 (600 × 375) — strzemiona Auto → 4-cięte — RC_SECTION A-A — NA Smart RC (do testów)",
                                             b, _tl528A, "belka-528-auto.dwg");
                    return $"{note.Substring(note.IndexOf(':') + 1).Trim()}; «{fl[0].text}»; {sec}; {bbs}; {pdf}";
                });
                Check(step, "528 Auto w detalu 1:25 — kopia z layoutem „DETAL 528 AUTO” do PDF", () =>
                {
                    var (b, t) = Views(db);
                    GenerateBeamsLegs(db, b, t, true, 0);
                    FourLegChecks(db, 290, 355);
                    return SaveDetailPdfCopy(db, "DETAL 528 AUTO", "PLOT 528-529 — detal 1:25, strzemiona Auto → 4-cięte — NA Smart RC (do testów)", b, _z528A, "belka-528-auto-det.dwg");
                });
                Check(step, "528: ręcznie 2-cięte przy przekroczeniu — ostrzeżenie, nie blokada (jedno strzemię 510, bez CountDisplay)", () =>
                {
                    var (b, t) = Views(db);
                    var msgs = Messages(GenerateBeamsLegs(db, b, t, false, 2));
                    var note = msgs.FirstOrDefault(m => m.Contains("s_t,max")) ?? throw new AssertFail("brak komunikatu");
                    if (!note.Contains("OSTRZEŻENIE")) throw new AssertFail(note);
                    var bl = Dists(db, "BL");
                    if (bl.Count == 0 || bl.Any(x => EffCount(RawOf(db, x.id, DistApp)) != x.count)) throw new AssertFail("BL 2-cięte z krotnością");
                    if (!AnnotText(db, bl[0].id).StartsWith(bl[0].count + " H12-")) throw new AssertFail("opis 2-ciętych «" + AnnotText(db, bl[0].id) + "»");
                    return note.Substring(note.IndexOf(':') + 1).Trim();
                });
            });
        }

        [CommandMethod("RCSMOKE_BEAM_L539A", CommandFlags.Modal)]
        public void L539A()
        {
            const string step = "L539A";
            Run(step, db => Check(step, "539 (600 × 300, L, krawędziowa), Auto: 4-cięte z ostrzeżeniem (ramiona 188 > 172.5), detal 1:25 — PDF", () =>
            {
                SetFormat("Polyline");
                CleanModelSpace(db);
                ImportGa("PLOT 539");
                var (b, t) = Views(db);
                Mesh(b, t, 225);
                var msgs = Messages(GenerateBeamsLegs(db, b, t, true, 0));
                var notes = msgs.Where(m => m.Contains("s_t,max")).ToList();
                if (notes.Count == 0 || !notes.All(n => n.Contains("→ 4-cięte") && n.Contains("OSTRZEŻENIE"))) throw new AssertFail(string.Join(" | ", notes));
                var fl = FourLegChecks(db, 215, 355);
                var z = Zones(db).First(x => XLinkOf(db, x, BeamApp, 6) == Hx(b));
                string bbs = FourLegBbs(db, fl[0].pos, 215, 355);
                return $"{notes[0].Substring(notes[0].IndexOf(':') + 1).Trim()}; {bbs}; "
                       + SaveDetailPdfCopy(db, "DETAL 539 AUTO", "PLOT 539 (600 × 300) — detal 1:25, strzemiona Auto → 4-cięte (ostrzeżenie: rozstaw ramion > s_t,max) — NA Smart RC (do testów)",
                                           b, z, "belka-539-auto-det.dwg");
            }));
        }

        [CommandMethod("RCSMOKE_BEAM_LH20", CommandFlags.Modal)]
        public void LH20()
        {
            const string step = "LH20";
            Run(step, db => Check(step, "528: H16 → H20 w rekordzie RC_BEAM (dół, góra, U-bary) i ponowne Beam: BM „2x4 H20-nn-150 B+T”, zakład 800, U-bary 21 H20 (A = C ≥ 900, B 265), stare H16 usunięte, BBS bez ostrzeżeń", () =>
            {
                SetFormat("Polyline");
                CleanModelSpace(db);
                ImportGa("PLOT 528-529");
                var (b, t) = Views(db);
                GenerateBeams(db, b, t);
                int before = Dists(db, "BM").Count(x => x.mark.StartsWith("H16-"));
                if (before == 0) throw new AssertFail("brak BM H16 przed zmianą");
                foreach (var z in Zones(db))
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var e = (Entity)tr.GetObject(z, OpenMode.ForWrite);
                        var v = Raw(e, BeamApp);
                        foreach (int i in new[] { 9, 11, 13 }) v[i] = new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)20);
                        e.XData = new ResultBuffer(v);
                        tr.Commit();
                    }
                var msgs = Messages(GenerateBeams(db, b, t));
                var bm = Dists(db, "BM");
                if (bm.Count == 0 || bm.Any(x => !x.mark.StartsWith("H20-"))) throw new AssertFail("BM: " + string.Join(",", bm.Select(x => x.mark)));
                foreach (var x in bm)
                {
                    string tx = AnnotText(db, x.id);
                    if (!tx.StartsWith("2x4 H20-") || !tx.EndsWith("-150 B+T")) throw new AssertFail("opis BM «" + tx + "»");
                }
                var lapMsg = msgs.FirstOrDefault(m => m.Contains("(BM): pręty")) ?? "";
                if (lapMsg.Length > 0 && !lapMsg.Contains("min. 800")) throw new AssertFail("zakład H20: " + lapMsg);
                var bu = Dists(db, "BU");
                if (bu.Count == 0 || bu.Any(x => !x.mark.StartsWith("H20-"))) throw new AssertFail("BU: " + string.Join(",", bu.Select(x => x.mark)));
                var tu = Templates(db).Where(x => x.mark.StartsWith("H20-") && x.shape == "21").ToList();
                if (tu.Count == 0 || tu.Any(x => x.a < 899.5 || Math.Abs(x.a - x.c) > 0.5 || Math.Abs(x.b - 265) > 0.5))
                    throw new AssertFail("szablony U-barów H20: " + string.Join(",", tu.Select(x => $"{x.a:0}/{x.b:0}/{x.c:0}")));
                var bbs = Call("BricsCadRc.Core.BbsDrawingReader", "ReadBars", db);
                var h20 = L(bbs, "Rows").Where(r => (int)Prop(r, "Diameter") == 20).ToList();
                var warns = L(bbs, "Warnings").Cast<string>().Where(w => w.Contains("8666")).ToList();
                if (warns.Count > 0) throw new AssertFail("ostrzeżenia BBS: " + warns[0]);
                foreach (var r in h20.Where(r => Convert.ToString(Prop(r, "ShapeCode"), Inv) == "21"))
                {
                    var d = (double[])Prop(r, "Dims");
                    double len = Convert.ToDouble(Call("BricsCadRc.Core.BarLengthRules", "CuttingLength", "21", 20.0, d[0], d[1], d[2], 0.0, 0.0), Inv);
                    if (Math.Abs(Dv(r, "Length") - len) > 0.01) throw new AssertFail("BBS U-bar H20: " + Dv(r, "Length") + " ≠ " + len);
                }
                if (Ms(db, e => e is BlockReference && Raw(e, DistApp) is TypedValue[] v && v.Length > 2
                                && new[] { "BM", "BU" }.Contains(Convert.ToString(v[2].Value, Inv)) && Convert.ToString(v[1].Value, Inv).StartsWith("H16-")).Count > 0)
                    throw new AssertFail("pozostały rozkłady BM / BU H16");
                return $"BM {bm.Count} («{AnnotText(db, bm[0].id)}»), BU {bu.Count}, U-bary H20 {string.Join(",", tu.Select(x => $"{x.a:0}/{x.b:0}/{x.c:0}").Distinct())}; " +
                       $"BBS H20: {string.Join(", ", h20.Select(r => $"{Prop(r, "BarMark")} {Prop(r, "ShapeCode")} L{Dv(r, "Length"):0}×{Prop(r, "Total")}"))}; {lapMsg}";
            }));
        }

        [CommandMethod("RCSMOKE_BEAM_LMAN", CommandFlags.Modal)]
        public void LMan()
        {
            const string step = "LMAN";
            Run(step, db =>
            {
                ObjectId man = ObjectId.Null, b = ObjectId.Null, t = ObjectId.Null;
                int nibEdges = -1, blCount = 0;
                Check(step, "Prepare GA 526 (bez stref): warstwa RC-BEAM-MANUAL (kolor 30, opis); polilinia 600 × 3000 + „450THK” wewnątrz płyty (belka w płycie) — NibDetector bez zmian", () =>
                {
                    SetFormat("Polyline");
                    CleanModelSpace(db);
                    ImportGa("PLOT 526");
                    (b, t) = Views(db);
                    if (Zones(db).Count != 0) throw new AssertFail("strefy przed rysowaniem: " + Zones(db).Count);
                    string desc; short col;
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                        if (!lt.Has("RC-BEAM-MANUAL")) throw new AssertFail("brak warstwy RC-BEAM-MANUAL po Prepare GA");
                        var rec = (LayerTableRecord)tr.GetObject(lt["RC-BEAM-MANUAL"], OpenMode.ForRead);
                        desc = rec.Description; col = rec.Color.ColorIndex;
                        tr.Commit();
                    }
                    if (col != 30 || string.IsNullOrEmpty(desc)) throw new AssertFail($"warstwa: kolor {col}, opis «{desc}»");
                    object nb = Call(NibDetType, "Detect", db, b);
                    nibEdges = nb == null ? 0 : L(nb, "Edges").Count;
                    var poly = Verts(db, b); var sb = Box(poly);
                    Point2d? at = null;
                    for (double fy = 0.3; fy <= 0.7 && at == null; fy += 0.05)
                        for (double fx = 0.2; fx <= 0.8 && at == null; fx += 0.05)
                        {
                            double cx = sb.x0 + fx * (sb.x1 - sb.x0), cy = sb.y0 + fy * (sb.y1 - sb.y0);
                            bool ok = true;
                            foreach (var (dx, dy) in new[] { (-1300.0, -2500.0), (1300.0, -2500.0), (1300.0, 2500.0), (-1300.0, 2500.0), (0.0, 0.0) })
                                if (!InPoly(poly, cx + dx, cy + dy)) { ok = false; break; }
                            if (ok) at = new Point2d(cx, cy);
                        }
                    if (at == null) throw new AssertFail("brak miejsca na belkę wewnętrzną w 526");
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                        var pl = new Polyline { Layer = "RC-BEAM-MANUAL" };
                        double x0 = at.Value.X - 300, x1 = at.Value.X + 300, y0 = at.Value.Y - 1500, y1 = at.Value.Y + 1500;
                        pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0); pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
                        pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0); pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
                        pl.Closed = true;
                        man = ms.AppendEntity(pl); tr.AddNewlyCreatedDBObject(pl, true);
                        var tx = new DBText { TextString = "450THK", Position = new Point3d(at.Value.X - 100, at.Value.Y, 0), Height = 100 };
                        ms.AppendEntity(tx); tr.AddNewlyCreatedDBObject(tx, true);
                        tr.Commit();
                    }
                    object nb2 = Call(NibDetType, "Detect", db, b);
                    int e2 = nb2 == null ? 0 : L(nb2, "Edges").Count;
                    if (e2 != nibEdges) throw new AssertFail($"NibDetector: krawędzie {nibEdges} → {e2}");
                    return $"warstwa kolor {col}; polilinia {Hx(man)} w ({at.Value.X:0}, {at.Value.Y:0}); nib: {nibEdges} krawędzi (bez zmian)";
                });
                Check(step, "RC_GENERATE → Beam: polilinia → strefy RC-BEAM na obu rzutach (H 450 z opisu), polilinia zostaje ze znacznikiem; belka w płycie — Auto 2-cięte", () =>
                {
                    var msgs = Messages(Call(Engine, "GenerateBeams", Application.DocumentManager.MdiActiveDocument, b, t, ObjectId.Null));
                    var zones = Zones(db);
                    if (zones.Count != 2) throw new AssertFail("strefy: " + zones.Count + " | " + string.Join(" | ", msgs.Take(5)));
                    foreach (var z in zones)
                    {
                        if (LayerOf(db, z) != "RC-BEAM") throw new AssertFail("warstwa strefy " + LayerOf(db, z));
                        if (Math.Abs(Convert.ToDouble(RawOf(db, z, BeamApp)[2].Value, Inv) - 450) > 0.5) throw new AssertFail("H strefy");
                    }
                    if (LayerOf(db, man) != "RC-BEAM-MANUAL") throw new AssertFail("polilinia ręczna zmieniła warstwę");
                    var mk = RawOf(db, man, "RC_BEAM_MANUAL") ?? throw new AssertFail("brak znacznika RC_BEAM_MANUAL");
                    var mz = new[] { XLinkOf(db, man, "RC_BEAM_MANUAL", 2), XLinkOf(db, man, "RC_BEAM_MANUAL", 3) };
                    if (!zones.All(z => mz.Contains(Hx(z)))) throw new AssertFail("znacznik ≠ strefy: " + string.Join(",", mz));
                    if (!msgs.Any(m => m.Contains("Polilinia ręczna belki"))) throw new AssertFail("brak komunikatu");
                    var note = msgs.FirstOrDefault(m => m.Contains("belka w płycie — 2-cięte")) ?? throw new AssertFail("brak „belka w płycie — 2-cięte”: " + string.Join(" | ", msgs.Where(m => m.Contains("cięte"))));
                    var bl = Dists(db, "BL");
                    blCount = bl.Count;
                    if (blCount == 0 || bl.Any(x => EffCount(RawOf(db, x.id, DistApp)) != x.count)) throw new AssertFail("strzemiona belki w płycie");
                    return msgs.First(m => m.Contains("Polilinia ręczna belki")) + " | " + note;
                });
                Check(step, "ponowne Beam: bez duplikatów stref i prętów", () =>
                {
                    Call(Engine, "GenerateBeams", Application.DocumentManager.MdiActiveDocument, b, t, ObjectId.Null);
                    if (Zones(db).Count != 2 || Dists(db, "BL").Count != blCount) throw new AssertFail($"strefy {Zones(db).Count}, BL {Dists(db, "BL").Count}");
                    return "strefy 2, BL " + blCount;
                });
                Check(step, "ponowne Prepare GA 526: polilinia ręczna zostaje, strefy bez duplikatów, [6] przepięte na nowe obrysy; Beam — bez duplikatów", () =>
                {
                    ImportGa("PLOT 526");
                    var (b2, t2) = Views(db);
                    if (man.IsErased) throw new AssertFail("polilinia ręczna usunięta przez Prepare GA");
                    var zones = Zones(db);
                    if (zones.Count != 2) throw new AssertFail("strefy po Prepare GA: " + zones.Count);
                    var s6 = zones.Select(z => XLinkOf(db, z, BeamApp, 6)).OrderBy(x => x).ToList();
                    if (!s6.SequenceEqual(new[] { Hx(b2), Hx(t2) }.OrderBy(x => x))) throw new AssertFail("[6] stref: " + string.Join(",", s6));
                    Call(Engine, "GenerateBeams", Application.DocumentManager.MdiActiveDocument, b2, t2, ObjectId.Null);
                    if (Zones(db).Count != 2 || Dists(db, "BL").Count != blCount) throw new AssertFail($"po Beam: strefy {Zones(db).Count}, BL {Dists(db, "BL").Count}");
                    return $"strefy 2 ([6] → {string.Join(",", s6)}), BL {blCount}";
                });
            });
        }

        // ================================================================
        // Decyzja użytkownika 2026-10-11 „w generatorze dodaj opcję dodania lub nie dodawania nibu, bo czasem może nie być” —
        // okno RC_BEAM „Nib przy belce”, rekord RC_BEAM [38] (0 = wg geometrii, 1 = bez nibu)
        // ================================================================

        /// <summary>RC_BEAM [38] wszystkich stref: 1 = belka bez nibu, 0 = wg geometrii (rekord bez pola — dopisane).</summary>
        private static void SetNoNib(Database db, bool noNib)
        {
            var zones = Zones(db);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var z in zones)
                {
                    var e = (Entity)tr.GetObject(z, OpenMode.ForWrite);
                    var v = Raw(e, BeamApp);
                    if (v == null || v.Length < 38) throw new AssertFail("rekord RC_BEAM krótszy niż 38 pól: " + (v?.Length ?? 0));
                    var tv = new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)(noNib ? 1 : 0));
                    if (v.Length > 38) v[38] = tv; else v = v.Concat(new[] { tv }).ToArray();
                    e.XData = new ResultBuffer(v);
                }
                tr.Commit();
            }
        }

        /// <summary>Wiersze BBS (numer H Ø kształt × sztuk L) i masa całkowita.</summary>
        private static (List<string> rows, double mass) BbsRows(Database db)
        {
            var bbs = Call("BricsCadRc.Core.BbsDrawingReader", "ReadBars", db);
            var rows = L(bbs, "Rows").OrderBy(x => (int)Prop(x, "BarMark")).ToList();
            return (rows.Select(r => string.Format(Inv, "{0:00} H{1} {2} ×{3} L{4:0}", (int)Prop(r, "BarMark"), (int)Prop(r, "Diameter"),
                                                   Prop(r, "ShapeCode"), (int)Prop(r, "Total"), Dv(r, "Length"))).ToList(),
                    rows.Sum(r => Dv(r, "MassKg")));
        }

        private static Point3d _tlNN;

        [CommandMethod("RCSMOKE_BEAM_L528NN", CommandFlags.Modal)]
        public void L528NoNib()
        {
            const string step = "L528NN";
            Run(step, db =>
            {
                ObjectId b = ObjectId.Null, t = ObjectId.Null, zb = ObjectId.Null, zt = ObjectId.Null;
                (List<string> rows, double mass) withNib = (new List<string>(), 0);
                string pdfDir = Environment.GetEnvironmentVariable("RC_BEAM_PDF_DIR");
                if (string.IsNullOrWhiteSpace(pdfDir)) pdfDir = OutDir;
                Check(step, "528 z nibem (wg geometrii): Prepare GA → rekord RC_BEAM [38] = 0 (39 pól, odczyt wtyczki NoNib = false); Mesh + Beam (2-cięte, rzut) — pręty w nibie 2+1, UB 03 przy belce (stan jak dotąd), BBS bazowy", () =>
                {
                    SetFormat("Polyline");
                    CleanModelSpace(db);
                    ImportGa("PLOT 528-529");
                    (b, t) = Views(db);
                    zb = Zones(db).First(z => XLinkOf(db, z, BeamApp, 6) == Hx(b));
                    zt = Zones(db).First(z => XLinkOf(db, z, BeamApp, 6) == Hx(t));
                    foreach (var z in new[] { zb, zt })
                    {
                        var v = RawOf(db, z, BeamApp);
                        if (v.Length != 39 || Convert.ToInt32(v[38].Value, Inv) != 0) throw new AssertFail($"rekord strefy: {v.Length} pól, [38] = {(v.Length > 38 ? v[38].Value : "—")}");
                        if ((bool)Prop(With(db, z, e => Call("BricsCadRc.Core.BeamStore", "Read", e)), "NoNib")) throw new AssertFail("NoNib = true po Prepare GA");
                    }
                    Mesh(b, t, 225);
                    GenerateBeams(db, b, t);
                    int bn = Dists(db, "BN").Count;
                    var sb = Box(Verts(db, b)); var zbb = Box(Verts(db, zb));
                    int u3 = Ub03(db).Count(u => u.cx < zbb.x1 && u.cy > sb.y0 - 1500 && u.cy < sb.y1 + 1500);
                    if (bn == 0 || u3 == 0) throw new AssertFail($"stan z nibem: BN {bn}, UB 03 przy belce {u3}");
                    withNib = BbsRows(db);
                    BbsDump(db, Path.Combine(pdfDir, "belka-528-z-nibem-bbs.txt"));
                    return string.Format(Inv, "BN {0} rozkł., UB 03 przy belce {1} rozkł.; BBS {2} wierszy, {3:0.0} kg → belka-528-z-nibem-bbs.txt", bn, u3, withNib.rows.Count, withNib.mass);
                });
                Check(step, "„Nib przy belce” = nie ([38] = 1, odczyt NoNib = true), Mesh + Beam: komunikat „bez nibu” (belka przesunięta do krawędzi, szer. 600 z GA), strzemię 51 A 290 × B 510, brak prętów w nibie (BN), UB 03 / T IN NIB / UB 01/02 przy belce — brak, prostopadłe B1 / T1 do 40 od krawędzi płyty, równoległe B2 / T2 od nowego lica wewnętrznego (krawędź + 600) + 50 / + 100", () =>
                {
                    SetNoNib(db, true);
                    foreach (var z in new[] { zb, zt })
                        if (!(bool)Prop(With(db, z, e => Call("BricsCadRc.Core.BeamStore", "Read", e)), "NoNib")) throw new AssertFail("odczyt [38]: NoNib = false");
                    Mesh(b, t, 225);
                    var msgs = Messages(GenerateBeams(db, b, t));
                    var note = msgs.FirstOrDefault(m => m.Contains("bez nibu (RC_BEAM")) ?? throw new AssertFail("brak komunikatu „bez nibu”: " + string.Join(" | ", msgs.Take(6)));
                    if (!note.Contains("przesunięta do krawędzi") || !note.Contains("szer. 600 (z GA 600)")) throw new AssertFail(note);
                    var sb = new StringBuilder();
                    var s51 = Templates(db).Where(x => x.shape == "51").ToList();
                    if (!s51.Any(x => Math.Abs(x.a - 290) < 0.5 && Math.Abs(x.b - 510) < 0.5)) throw new AssertFail("strzemię 51: " + string.Join(",", s51.Select(x => $"{x.mark} {x.a:0}×{x.b:0}")));
                    if (Dists(db, "BN").Count != 0) throw new AssertFail("pręty w nibie (BN) przy belce bez nibu: " + string.Join(" | ", Dists(db, "BN").Select(x => x.mark)));
                    var bx = Box(Verts(db, b)); var tx = Box(Verts(db, t));
                    var zbb = Box(Verts(db, zb)); var ztb = Box(Verts(db, zt));
                    // UB 03: przy belce brak (rzut i detal) — środek UB 03 na lewo od lica wewnętrznego belki
                    var u3 = Ub03(db).Where(u => u.cx < zbb.x1 && u.cy > bx.y0 - 1500 && u.cy < bx.y1 + 1500).ToList();
                    if (u3.Count > 0) throw new AssertFail("UB 03 przy belce: " + Diag(u3));
                    int u3All = Ub03(db).Count;
                    // pręty siatki prostopadłe (B1 na rzucie dolnym, T1 na górnym): koniec 40 od krawędzi płyty (lico zewnętrzne belki)
                    foreach (var (view, box, zbox, code) in new[] { (b, bx, zbb, "B1"), (t, tx, ztb, "T1") })
                    {
                        var ends = MeshDists(db, view).Where(d => d.code == code && d.mark.EndsWith(" " + code) && d.alongX
                                                                && d.scan.Any(s => s > zbox.y0 + 1 && s < zbox.y1 - 1) && d.p0 < box.x0 + 1500)
                                                      .Select(d => d.p0 - box.x0).ToList();
                        if (ends.Count == 0) throw new AssertFail(code + ": brak prętów prostopadłych przy belce");
                        if (ends.Any(e => Math.Abs(e - 40) > 0.5)) throw new AssertFail($"{code}: końce {string.Join(",", ends.Select(e => e.ToString("0.#", Inv)).Distinct())} od krawędzi płyty (oczekiwano 40)");
                        sb.Append($"{code} {ends.Count} rozkł. — koniec 40 od krawędzi; ");
                    }
                    // siatka równoległa do belki: pierwszy pręt od NOWEGO lica wewnętrznego (krawędź + 600): dół + 40 + d = 650, góra + s/2 = 700
                    foreach (var (view, box, zbox, code, need) in new[] { (b, bx, zbb, "B2", 650.0), (t, tx, ztb, "T2", 700.0) })
                    {
                        var first = MeshDists(db, view).Where(d => d.code == code && d.mark.EndsWith(" " + code) && !d.alongX
                                                                 && Math.Min(d.p1, zbox.y1) - Math.Max(d.p0, zbox.y0) > 1)
                                                       .SelectMany(d => d.scan).Where(sc => sc > box.x0 && sc < box.x0 + 2000).DefaultIfEmpty(double.NaN).Min();
                        if (double.IsNaN(first) || Math.Abs(first - box.x0 - need) > 0.5)
                            throw new AssertFail($"{code}: pierwszy pręt równoległy {first - box.x0:0.#} od krawędzi (oczekiwano {need:0} — lico wewnętrzne 600 + {need - 600:0})");
                        sb.Append($"{code} pierwszy {need:0} od krawędzi (lico 600 + {need - 600:0}); ");
                    }
                    var ubEdge = MeshDists(db, b).Where(d => (d.pos == 1 || d.pos == 2) && d.mark.EndsWith(" UB") && d.alongX
                                                            && d.scan.Any(s => s > zbb.y0 + 1 && s < zbb.y1 - 1) && d.p0 < bx.x0 + 1000).ToList();
                    if (ubEdge.Count > 0) throw new AssertFail("UB 01/02 na krawędzi przy belce: " + string.Join(" | ", ubEdge.Select(d => d.mark)));
                    var tin = MeshDists(db, t).Where(d => d.mark.EndsWith(" T IN NIB") && !d.alongX && d.scan.Any(s => s < ztb.x1)).ToList();
                    if (tin.Count > 0) throw new AssertFail("T IN NIB przy belce: " + string.Join(" | ", tin.Select(d => d.mark)));
                    return sb + $"UB 03 przy belce: brak (płyta: {u3All} rozkł. poza belką); UB 01/02 i T IN NIB przy belce: brak; {note.Substring(note.IndexOf(':') + 1).Trim()}";
                });
                Check(step, "RC_SECTION A prostopadle do belki bez nibu: pas belki od krawędzi płyty na szerokość 600, profil bez nibu (bez przedziału −375…−225), BM 4 + 4, bez kropek w nibie, bez UB 03 / T IN NIB; PDF rzut + przekrój", () =>
                {
                    var zbb = Box(Verts(db, zb)); var sbx = Box(Verts(db, b));
                    double y = zbb.y0 + 0.3 * (zbb.y1 - zbb.y0);
                    _tlNN = new Point3d(sbx.x0, sbx.y0 - 9000, 0);
                    var (ctx, plan) = DrawSection(db, b, new Point2d(sbx.x0 - 1000, y), new Point2d(zbb.x1 + 3000, y), new Point2d(sbx.x0, y + 100), _tlNN, "A");
                    var beams = L(Prop(ctx, "Input"), "Beams");
                    if (beams.Count != 1) throw new AssertFail("pasy belek: " + beams.Count);
                    double s0 = Math.Min(Dv(beams[0], "S0"), Dv(beams[0], "S1")), s1 = Math.Max(Dv(beams[0], "S0"), Dv(beams[0], "S1"));
                    if (Math.Abs(s0 - sbx.x0) > 0.5 || Math.Abs(s1 - sbx.x0 - 600) > 0.5) throw new AssertFail($"pas belki {s0:0.#}..{s1:0.#} (oczekiwano {sbx.x0:0}..{sbx.x0 + 600:0})");
                    var sg = L(plan, "Segments").First(x => Prop(x, "Prof") != null);
                    var prof = L(sg, "Prof").Select(p => (ValueTuple<double, double, double, double>)p).ToList();
                    if (prof.Any(p => Math.Abs(p.Item4 + 225) < 1e-6)) throw new AssertFail("profil z nibem: " + string.Join(" ", prof));
                    var trz = prof.FirstOrDefault(p => Math.Abs(p.Item3 + 375) < 1e-6 && p.Item4 == 0);
                    double u0 = Dv(sg, "U0"), u1 = Dv(sg, "U1");
                    if (trz.Item2 - trz.Item1 < 1 || (Math.Abs(trz.Item1 - u0) > 0.5 && Math.Abs(trz.Item2 - u1) > 0.5))
                        throw new AssertFail($"trzon {trz.Item1:0.#}..{trz.Item2:0.#} nie od krawędzi płyty ({u0:0.#}..{u1:0.#})");
                    if (Math.Abs(trz.Item2 - trz.Item1 - 600) > 0.5) throw new AssertFail($"szerokość trzonu {trz.Item2 - trz.Item1:0.#} (oczekiwano 600)");
                    var dots = L(plan, "Dots").Where(d => (bool)Prop(d, "Beam")).ToList();
                    if (dots.Count != 8) throw new AssertFail("kropki belki: " + dots.Count + " — " + string.Join(" ", dots.Select(d => $"{Dv(d, "U"):0},{Dv(d, "V"):0}")));
                    if (L(plan, "Bars").Any(x => Convert.ToString(Prop(x, "Kind"), Inv) == "NibUB")) throw new AssertFail("UB 03 w przekroju");
                    if (L(plan, "Dots").Any(d => (bool)Prop(d, "TinNib"))) throw new AssertFail("T IN NIB w przekroju");
                    string pdf = SavePdfCopy(db, "BELKA 528 BEZ NIBU", "PLOT 528-529 — belka BEZ nibu (RC_BEAM „Nib przy belce” = nie): lico zewnętrzne = krawędź płyty — RC_SECTION A-A — NA Smart RC (do testów)",
                                             b, _tlNN, "belka-528-bez-nibu.dwg");
                    return $"pas {s0:0}..{s1:0}, trzon {trz.Item1:0}..{trz.Item2:0} (600), kropki belki 8; {pdf}";
                });
                Check(step, "detal 1:25 belki bez nibu: ANCHOR = strefa przesunięta do krawędzi (600 × długość płyty), bez linii uskoku przy belce, bez UB 03 i prętów w nibie; BBS z nibem / bez nibu; PDF rzut + detal", () =>
                {
                    GenerateBeamsDetail(db, b, t);
                    var anchor = DetailObjs(db).Single(o => o.zone == Hx(zb) && o.role == "ANCHOR").id;
                    // ANCHOR = strefa efektywna: lico zewnętrzne na krawędzi płyty, szerokość 600, końce na krawędziach
                    var ab = Box(Verts(db, anchor)); var sbb = Box(Verts(db, b));
                    if (Math.Abs(ab.x1 - ab.x0 - 600) > 0.5 || Math.Abs((ab.y1 - ab.y0) - (sbb.y1 - sbb.y0)) > 0.5)
                        throw new AssertFail($"ANCHOR {ab.x1 - ab.x0:0.#} × {ab.y1 - ab.y0:0.#} (oczekiwano 600 × {sbb.y1 - sbb.y0:0})");
                    var sh = new Vector2d(ab.x0 - sbb.x0, ab.y0 - sbb.y0);
                    var zbb = Box(Verts(db, zb));
                    double stepX = zbb.x0 + sh.X;   // linia uskoku (lico zewnętrzne strefy z GA) w detalu
                    foreach (var o in DetailObjs(db).Where(o => o.zone == Hx(zb) && o.role == "LINE"))
                    {
                        var (p0, p1) = With(db, o.id, e => e is Line ln ? (ln.StartPoint, ln.EndPoint) : (Point3d.Origin, Point3d.Origin));
                        if (Math.Abs(p0.X - stepX) < 1 && Math.Abs(p1.X - stepX) < 1 && Math.Min(Math.Max(p0.Y, p1.Y), zbb.y1 + sh.Y) - Math.Max(Math.Min(p0.Y, p1.Y), zbb.y0 + sh.Y) > 1)
                            throw new AssertFail($"linia uskoku w detalu: ({p0.X:0},{p0.Y:0})–({p1.X:0},{p1.Y:0})");
                    }
                    if (Dists(db, "BN").Count != 0) throw new AssertFail("BN w detalu");
                    if (Ub03(db).Any(u => u.zone == Hx(zb))) throw new AssertFail("UB 03 w detalu belki bez nibu");
                    var noNib = BbsRows(db);
                    var gone = withNib.rows.Except(noNib.rows).ToList();
                    var added = noNib.rows.Except(withNib.rows).ToList();
                    string pdf = SaveDetailPdfCopy(db, "DETAL 528 BEZ NIBU", "PLOT 528-529 — belka BEZ nibu — detal 1:25 (bez linii uskoku przy belce) — NA Smart RC (do testów)",
                                                   b, zb, "belka-528-bez-nibu-det.dwg");
                    return string.Format(Inv, "BBS z nibem {0:0.0} kg → bez nibu {1:0.0} kg; tylko z nibem: {2}; tylko bez nibu: {3}; {4}",
                                         withNib.mass, noNib.mass, string.Join(", ", gone), string.Join(", ", added), pdf);
                });
            });
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

        /// <summary>Pręty belek w trybie RZUTU ([36] = 0 — kroki sprzed K9 bez zmian; rekord v1 krótszy — bez zmiany, domyślny detal);
        /// strzemiona 2-cięte ([37] = 2 — kroki sprzed decyzji 2026-10-11 o 4-ciętych bez zmian; Auto — kroki L*).</summary>
        private static object GenerateBeams(Database db, ObjectId b, ObjectId t)
        {
            SetDetailMode(db, false);
            return Call(Engine, "GenerateBeams", Application.DocumentManager.MdiActiveDocument, b, t, ObjectId.Null);
        }

        /// <summary>K9: pręty belek w DETALU 1:25 ([36] = 1), strzemiona 2-cięte ([37] = 2).</summary>
        private static object GenerateBeamsDetail(Database db, ObjectId b, ObjectId t)
        {
            SetDetailMode(db, true);
            return Call(Engine, "GenerateBeams", Application.DocumentManager.MdiActiveDocument, b, t, ObjectId.Null);
        }

        /// <summary>Decyzja 2026-10-11: pręty belek z cięciem strzemion legs ([37]: 0 Auto / 2 / 4) w detalu albo na rzucie.</summary>
        private static object GenerateBeamsLegs(Database db, ObjectId b, ObjectId t, bool detail, short legs)
        {
            SetDetailMode(db, detail, legs);
            return Call(Engine, "GenerateBeams", Application.DocumentManager.MdiActiveDocument, b, t, ObjectId.Null);
        }

        /// <summary>RC_BEAM [36] i [37] wszystkich stref (rekordy ≥ 36 pól; zapis tylko przy zmianie).</summary>
        private static void SetDetailMode(Database db, bool detail, short legs = 2)
        {
            var zones = Zones(db);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var z in zones)
                {
                    var e = (Entity)tr.GetObject(z, OpenMode.ForRead);
                    var v = Raw(e, BeamApp);
                    if (v == null || v.Length < 36) continue;
                    short want = (short)(detail ? 1 : 0);
                    if (v.Length > 37 && v[36].Value is short cur && cur == want && v[37].Value is short cl && cl == legs) continue;
                    var tv = new TypedValue((int)DxfCode.ExtendedDataInteger16, want);
                    if (v.Length > 36) v[36] = tv; else v = v.Concat(new[] { tv }).ToArray();
                    var tl = new TypedValue((int)DxfCode.ExtendedDataInteger16, legs);
                    if (v.Length > 37) v[37] = tl; else v = v.Concat(new[] { tl }).ToArray();
                    e.UpgradeOpen();
                    e.XData = new ResultBuffer(v);
                }
                tr.Commit();
            }
        }

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
            // kółko na zagięciu na każdym widocznym pręcie (koniec belki — 1 pręt reprezentatywny, decyzja 2026-10-11)
            if (circles != lines) throw new AssertFail($"kółka na zagięciu: {circles}, widoczne pręty: {lines} (U-bary: {bars})");
            return $"U-bary w strefie (linie {lines}, kółka {circles})";
        }

        /// <summary>Widoczne pręty rozkładu (linie w bloku RC_BAR_BLOCK).</summary>
        private static int VisibleUBarLines(Database db, ObjectId dist)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var br = (BlockReference)tr.GetObject(dist, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                int n = 0;
                foreach (ObjectId cid in btr) if (tr.GetObject(cid, OpenMode.ForRead) is Line) n++;
                tr.Commit();
                return n;
            }
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

        /// <summary>Nib belki w przekroju po lewej stronie trzonu (prawa granica nibu = lewa granica trzonu).</summary>
        private static bool NibLeftOfTrunk(ValueTuple<double, double, double, double> nibP, ValueTuple<double, double, double, double> trz)
            => Math.Abs(nibP.Item2 - trz.Item1) < 1;

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
