// PLAN C, krok 3f — testy dymne: format nowych prętów (RC_BAR_FORMAT) i gripy pręta-bloku z propagacją numeru,
// rozkładów i etykiety (BarChangePropagator) w BricsCAD V25. Uruchamiane skryptem planc-f3.scr (run-planc-f3.ps1).
// Komendy RCSMOKE_F3_*. Klasa częściowa PlanCFixW1Tests — te same helpery (pręty, rozkłady, refleksja);
// RCSMOKE_F3_SETUP przełącza plik wyników na planc-f3-results.txt.
//
// FMT0 / FMTB / FMTP  RC_BAR_FORMAT: brak pliku → Polilinia (domyślnie), fallback pliku starej wtyczki, komenda skryptu
//      „Blok” → %APPDATA%\NaSmartRc\barformat.txt „Format=Block”, odczyt pliku po ponownym wczytaniu, PlaceBar → pręt-blok;
//      „Polilinia” → plik i PlaceBar → polilinia. Plik użytkownika (jeśli był) przywracany DOKŁADNIE (FMTP i REPORT).
// S16  numerowany pręt-blok 00 H12 A = 3000 + etykieta + rozkład i drugi pręt-blok o tym samym numerze (kopia) + etykieta
//      + rozkład → ApplyLengthEdit (ścieżka CommandEnded gripa, wywołana wprost) L + 500 → nowy numer, rozkład 3500 z nowym
//      numerem, etykieta „n H12-nn”, grot na obrysie; kopia bez zmian; RC_BBS (ReadBars) — dwa wiersze. S16U: _.U → stan sprzed.
// S17  pręt-blok 21 → 1 grip (00 numerowany → 2), punkty STRETCH 2; ApplyEndLength / ApplyLengthEdit odrzuca (kształt gięty);
//      Info (RC_TEST_BARBLOCK) opisuje numerowany pręt-blok.
// S18  LengthOverridden: A 3000 → 3500, TotalLength 9999 bez zmian + ostrzeżenie [RC AUTO].
// S16S _.STRETCH końca numerowanego pręta-bloku z kopią (skrypt, prawdziwy overrule) → 3505, nowy numer, rozkład, etykieta;
//      _.UNDO 1 → stan sprzed; _.REDO → stan po STRETCH.
// S17S _.STRETCH końca pręta-bloku 21 → odrzucone, pręt bez zmian.
// AR   pełny AutoRebar (B1, B2, T1, T2, UB B1, UB B2) w trybie Polilinia i Blok (licznik pozycji wyzerowany przed każdym):
//      te same numery i ten sam BBS (ReadBars: wiersze i ostrzeżenia); w trybie Blok wszystkie pręty to bloki.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using Application = Bricscad.ApplicationServices.Application;

namespace BricsCadRc.BrxSmokeTests
{
    public partial class PlanCFixW1Tests
    {
        private static bool _f3Mode;

        private static readonly List<string> ExpectedStepsF3 = new List<string>
        {
            "SETUP", "FMT0", "FMTB", "FMTP",
            "S16SETUP", "S16", "S16U", "S17", "S18", "G1",
            "S16SSETUP", "S16SV", "S16SU", "S16SR", "S17SSETUP", "S17SV",
            "AR",
        };

        private const string GripOverruleType = "BricsCadRc.Core.SingleBarGripOverrule";
        private const string SingleBlkType    = "BricsCadRc.Core.SingleBarBlock";
        private const string TestEngineType   = "BricsCadRc.Core.TestBarBlockEngine";
        private const string BbsReaderType    = "BricsCadRc.Core.BbsDrawingReader";
        private const string AutoRebarType    = "BricsCadRc.Core.AutoRebarEngine";
        private const string CounterKey       = "RC_SLAB_POS_COUNTER";

        private static void SetModeF3()
        {
            _w2Mode = false;
            _w4Mode = false;
            _f3Mode = true;
            ResultsFile = "planc-f3-results.txt";
            Prefix = "[RC F3]";
            Title = "Plan C 3f — format prętów i gripy pręta-bloku z propagacją";
        }

        // ---- plik ustawienia formatu (stan użytkownika zapamiętany w SETUP, przywracany w FMTP i REPORT) ----

        private static string FmtPath(string folder)
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), folder, "barformat.txt");

        private static string FmtFile => FmtPath("NaSmartRc");
        private static string FmtLegacyFile => FmtPath("BricsCadRc");

        private static bool _fmtSaved, _fmtExisted;
        private static byte[] _fmtContent;

        private static void SaveUserFormatFile()
        {
            _fmtExisted = File.Exists(FmtFile);
            _fmtContent = _fmtExisted ? File.ReadAllBytes(FmtFile) : null;
            if (_fmtExisted) File.WriteAllBytes(Path.Combine(OutDir, "planc-f3-barformat.bak"), _fmtContent);
            _fmtSaved = true;
        }

        private static string RestoreUserFormatFile()
        {
            if (!_fmtSaved) return "brak zapamiętanego stanu";
            if (_fmtExisted)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FmtFile));
                File.WriteAllBytes(FmtFile, _fmtContent);
                return "przywrócono plik użytkownika (" + _fmtContent.Length.ToString(Inv) + " B)";
            }
            if (File.Exists(FmtFile)) File.Delete(FmtFile);
            return "plik usunięty (nie istniał przed testem)";
        }

        /// <summary>BarFormat._loaded = false — następne Current czyta zmienną środowiskową / plik od nowa.</summary>
        private static void ResetFormatLoaded()
        {
            var fi = Plugin.GetType(BarFormatType, true).GetField("_loaded", BindingFlags.Static | BindingFlags.NonPublic);
            if (fi == null) throw new AssertFail("brak pola BarFormat._loaded");
            fi.SetValue(null, false);
        }

        private static string CurrentFormat() => Convert.ToString(Call(BarFormatType, "get_Current"), Inv);

        // ================================================================
        // SETUP / REPORT
        // ================================================================

        [CommandMethod("RCSMOKE_F3_SETUP", CommandFlags.Modal)]
        public void F3Setup()
        {
            SetModeF3();
            _results.Clear();
            _executed.Clear();
            const string step = "SETUP";
            Run(step, db =>
            {
                Check(step, "wtyczka NaSmartRc wczytana (jedna kopia), typy kroku 3f", () =>
                {
                    var all = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "NaSmartRc").ToList();
                    if (all.Count != 1) throw new AssertFail("wczytane kopie NaSmartRc: " + all.Count);
                    Plugin.GetType("BricsCadRc.Core.BarChangePropagator", true);
                    Plugin.GetType("BricsCadRc.Commands.BarFormatCommands", true);
                    return "Location=" + Loc(Plugin);
                });
                Check(step, "zmienna RC_REG_BARFORMAT nieustawiona (inaczej ma pierwszeństwo przed plikiem)", () =>
                    True(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RC_REG_BARFORMAT")),
                         "RC_REG_BARFORMAT=" + Environment.GetEnvironmentVariable("RC_REG_BARFORMAT")));
                Check(step, "zapamiętanie pliku ustawienia użytkownika", () =>
                {
                    SaveUserFormatFile();
                    return _fmtExisted ? "plik istnieje (" + _fmtContent.Length.ToString(Inv) + " B, kopia planc-f3-barformat.bak)" : "brak pliku";
                });
                Check(step, "czyszczenie + RegApp + format Polyline", () =>
                {
                    int n = CleanModelSpace(db);
                    Call(SingleBarType, "EnsureAppIdRegistered", db);
                    Call(BarBlockType, "EnsureAppIdRegistered", db);
                    Call(AnnotType, "EnsureAppIdRegistered", db);
                    SetFormat("Polyline");
                    return "usunięto " + n.ToString(Inv);
                });
            });
        }

        [CommandMethod("RCSMOKE_F3_REPORT", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void F3Report()
        {
            try { Add("PASS", "REPORT", "plik ustawienia formatu", RestoreUserFormatFile()); }
            catch (System.Exception ex) { Add("FAIL", "REPORT", "przywrócenie pliku ustawienia formatu", ex.Message); }
            Report();
            SetMode(false);
        }

        // ================================================================
        // RC_BAR_FORMAT
        // ================================================================

        [CommandMethod("RCSMOKE_F3_FMT0", CommandFlags.Modal)]
        public void F3Fmt0()
        {
            const string step = "FMT0";
            Run(step, db =>
            {
                Check(step, "BarFormat.UserSettingEnabled = true (krok 3f)", () =>
                {
                    var fi = Plugin.GetType(BarFormatType, true).GetField("UserSettingEnabled", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    if (fi == null) throw new AssertFail("brak pola UserSettingEnabled");
                    return Eq("UserSettingEnabled", fi.GetValue(null), true);
                });
                bool legacyExists = File.Exists(FmtLegacyFile);
                Check(step, "brak pliku → Current = Polyline (domyślnie), DisplayName „polilinia”", () =>
                {
                    if (File.Exists(FmtFile)) File.Delete(FmtFile);
                    if (legacyExists) { Add("SKIP", step, "domyślny format", "istnieje plik starej wtyczki " + FmtLegacyFile); return "pominięte"; }
                    ResetFormatLoaded();
                    var cur = Call(BarFormatType, "get_Current");
                    return Eq("Current", Convert.ToString(cur, Inv), "Polyline") + "; "
                         + Eq("DisplayName", Call(BarFormatType, "DisplayName", cur), "polilinia");
                });
                Check(step, "fallback: tylko plik starej wtyczki „Format=Block” → Current = Block", () =>
                {
                    if (legacyExists) return "pominięte (plik starej wtyczki istnieje — nie ruszam)";
                    string dir = Path.GetDirectoryName(FmtLegacyFile);
                    bool dirCreated = !Directory.Exists(dir);
                    try
                    {
                        Directory.CreateDirectory(dir);
                        File.WriteAllText(FmtLegacyFile, "Format=Block" + Environment.NewLine);
                        ResetFormatLoaded();
                        return Eq("Current", CurrentFormat(), "Block");
                    }
                    finally
                    {
                        if (File.Exists(FmtLegacyFile)) File.Delete(FmtLegacyFile);
                        if (dirCreated && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
                        ResetFormatLoaded();
                    }
                });
                Check(step, "po fallbacku: Current = Polyline (bez plików)", () => Eq("Current", CurrentFormat(), "Polyline"));
            });
        }

        [CommandMethod("RCSMOKE_F3_FMTB", CommandFlags.Modal)]
        public void F3FmtB()
        {
            const string step = "FMTB";
            Run(step, db =>
            {
                Check(step, "RC_BAR_FORMAT „Blok” (skrypt): plik „Format=Block”, Current = Block", () =>
                {
                    if (!File.Exists(FmtFile)) throw new AssertFail("brak pliku " + FmtFile);
                    string txt = File.ReadAllText(FmtFile).Trim();
                    return Eq("plik", txt, "Format=Block") + "; " + Eq("Current", CurrentFormat(), "Block");
                });
                Check(step, "odczyt pliku po ponownym wczytaniu → Block", () =>
                {
                    ResetFormatLoaded();
                    return Eq("Current", CurrentFormat(), "Block");
                });
                Check(step, "PlaceBar (bez przełączania w teście) → pręt-blok v2", () =>
                {
                    var d = new Def { A = 2000, At = new Point3d(0, 5000, 0) };
                    d.Nr = NextAuto(db);
                    var id = (ObjectId)Call(SingleBarType, "PlaceBar", db, NewBar(d), d.At);
                    string kind = Convert.ToString(KindOf(db, id), Inv);
                    EraseInCode(db, id);
                    return Eq("KindOf", kind, "Block");
                });
            });
        }

        [CommandMethod("RCSMOKE_F3_FMTP", CommandFlags.Modal)]
        public void F3FmtP()
        {
            const string step = "FMTP";
            Run(step, db =>
            {
                Check(step, "RC_BAR_FORMAT „Polilinia” (skrypt): plik „Format=Polyline”, Current = Polyline", () =>
                {
                    string txt = File.Exists(FmtFile) ? File.ReadAllText(FmtFile).Trim() : "(brak pliku)";
                    return Eq("plik", txt, "Format=Polyline") + "; " + Eq("Current", CurrentFormat(), "Polyline");
                });
                Check(step, "PlaceBar → polilinia", () =>
                {
                    var d = new Def { A = 2000, At = new Point3d(0, 6000, 0) };
                    d.Nr = NextAuto(db);
                    var id = (ObjectId)Call(SingleBarType, "PlaceBar", db, NewBar(d), d.At);
                    string kind = Convert.ToString(KindOf(db, id), Inv);
                    EraseInCode(db, id);
                    return Eq("KindOf", kind, "Polyline");
                });
                Check(step, "przywrócenie pliku użytkownika", () =>
                {
                    string r = RestoreUserFormatFile();
                    SetFormat("Polyline");
                    return r;
                });
            });
        }

        // ================================================================
        // S16 — numerowany pręt-blok z rozkładem + kopia o tym samym numerze → L + 500 (wprost)
        // ================================================================

        private sealed class F3Set { public ObjectId Bar, Label, Dist; public string Mark = ""; public int N; }
        private static F3Set _a16, _b16, _a16s, _b16s;

        /// <summary>Dwa pręty-bloki 00 H12 A = 3000 o tym samym numerze (oryginał i „kopia”), każdy z etykietą i rozkładem.</summary>
        private static (F3Set a, F3Set b) MakePair(Database db, double y)
        {
            int nr = NextAuto(db);
            var a = new F3Set();
            var b = new F3Set();
            (a.Bar, a.Label) = PlaceBarWithLabel(db, "Block", new Def { A = 3000, Nr = nr, At = new Point3d(0, y, 0) });
            a.Dist = Distribute(db, a.Bar, 40, y + 1040, 3040, y + 2000).dist;
            (b.Bar, b.Label) = PlaceBarWithLabel(db, "Block", new Def { A = 3000, Nr = nr, At = new Point3d(0, y + 5000, 0) });
            b.Dist = Distribute(db, b.Bar, 40, y + 6040, 3040, y + 7000).dist;
            a.Mark = b.Mark = MarkOf(12, nr);
            a.N = LeadInt(LabelText(db, a.Label));
            b.N = LeadInt(LabelText(db, b.Label));
            if (a.N <= 0 || a.N != b.N) throw new AssertFail($"liczba na etykietach: {a.N} / {b.N}");
            return (a, b);
        }

        private static Matrix3d BlockXf(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                return ((BlockReference)tr.GetObject(id, OpenMode.ForRead)).BlockTransform;
        }

        private static List<string> ApplyLength(Database db, ObjectId id, double delta, string cmd, bool expected = true)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            object len = expected ? (object)BarNum(db, id, "LengthA") : null;
            object xf = expected ? (object)BlockXf(db, id) : null;
            return ((IEnumerable<string>)Call(GripOverruleType, "ApplyLengthEdit", doc, id, false, delta, len, xf, cmd)).ToList();
        }

        /// <summary>Długość obrysu prostego pręta (rozpiętość wierzchołków wzdłuż X — pręt poziomy).</summary>
        private static double OutlineSpan(Database db, ObjectId id)
        {
            var v = Outline(db, id);
            return v.Max(p => p.X) - v.Min(p => p.X);
        }

        /// <summary>Etykieta pręta ([13]) z back-linkiem: tekst; grot na obrysie (±1e-6).</summary>
        private static string LabelCheck(Database db, ObjectId bar, ObjectId label, string expText)
        {
            BarLabelPair(db, bar, label);
            string text = LabelText(db, label);
            if (!text.Contains(expText)) throw new AssertFail($"tekst etykiety „{text}”, oczekiwano „{expText}”");
            double dist;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ml = (MLeader)tr.GetObject(label, OpenMode.ForRead);
                var li = (int)ml.GetLeaderIndexes()[0];
                var tip = ml.GetFirstVertex((int)ml.GetLeaderLineIndexes(li)[0]);
                var cp = (Point3d)((Point3d?)Inst(OpenBar(tr, bar), "ClosestPoint", tr, tip)).Value;
                dist = cp.DistanceTo(tip);
                tr.Commit();
            }
            if (dist > GeoTol) throw new AssertFail("grot etykiety odległy od obrysu o " + dist.ToString("E2", Inv));
            return "etykieta „" + text + "”, grot na obrysie";
        }

        private static string DistCheck(Database db, F3Set s, double len, string mark)
        {
            var dists = DistsPointingTo(db, s.Bar);
            if (dists.Count != 1 || dists[0] != s.Dist) throw new AssertFail("rozkłady pręta: " + dists.Count.ToString(Inv));
            string dm = Convert.ToString(Rec(db, s.Dist, DistApp2)[1].Value, Inv);
            if (!dm.StartsWith(mark + "-", StringComparison.Ordinal)) throw new AssertFail($"Mark rozkładu „{dm}”, oczekiwano {mark}-…");
            return Num("rozkład [8]", DistLength(db, s.Dist), len) + "; Mark rozkładu=" + dm;
        }

        /// <summary>Wiersze BBS (ReadBars) jako „pozycja|H|kształt|szt.|L”, posortowane; ostrzeżenia.</summary>
        private static (List<string> rows, List<string> warns, int dists) Bbs(Database db)
        {
            var res = CallD(BbsReaderType, "ReadBars", db);
            var rows = ((IEnumerable)Prop(res, "Rows")).Cast<object>()
                .Select(r => Convert.ToString(Prop(r, "BarMark"), Inv) + "|H" + Convert.ToString(Prop(r, "Diameter"), Inv)
                           + "|" + Prop(r, "ShapeCode") + "|" + Convert.ToString(Prop(r, "NoEach"), Inv)
                           + "|L=" + F(Convert.ToDouble(Prop(r, "Length"), Inv))
                           + "|" + string.Join(",", ((double[])Prop(r, "Dims")).Select(F)))
                .OrderBy(s => s, StringComparer.Ordinal).ToList();
            var warns = ((IEnumerable<string>)Prop(res, "Warnings")).OrderBy(s => s, StringComparer.Ordinal).ToList();
            return (rows, warns, Convert.ToInt32(Prop(res, "Distributions"), Inv));
        }

        [CommandMethod("RCSMOKE_F3_S16SETUP", CommandFlags.Modal)]
        public void F3S16Setup()
        {
            const string step = "S16SETUP";
            Run(step, db =>
            {
                Check(step, "dwa pręty-bloki 00 H12 A=3000 o tym samym numerze + etykiety + rozkłady (Y=20000 / 25000)", () =>
                {
                    CleanModelSpace(db);
                    (_a16, _b16) = MakePair(db, 20000);
                    return _a16.Mark + ": " + Convert.ToString(KindOf(db, _a16.Bar), Inv) + " / " + Convert.ToString(KindOf(db, _b16.Bar), Inv)
                         + "; " + LabelCheck(db, _a16.Bar, _a16.Label, _a16.N + " " + _a16.Mark);
                });
                Check(step, "BBS przed: jeden wiersz pozycji (2n szt.)", () =>
                {
                    var b = Bbs(db);
                    return Int("wiersze", b.rows.Count, 1) + "; " + string.Join(" ; ", b.rows);
                });
            });
        }

        [CommandMethod("RCSMOKE_F3_S16", CommandFlags.Modal)]
        public void F3S16()
        {
            const string step = "S16";
            Run(step, db =>
            {
                if (_a16 == null) { Add("FAIL", step, "brak S16SETUP", ""); return; }
                string newMark = null;
                Check(step, "ApplyLengthEdit (grip końca, wprost) L + 500 → komunikat [RC AUTO] „nowa pozycja”", () =>
                {
                    var msgs = ApplyLength(db, _a16.Bar, 500, "GRIP_STRETCH");
                    string m = msgs.FirstOrDefault(x => x.StartsWith("[RC AUTO]", StringComparison.Ordinal) && x.Contains("nowa pozycja"));
                    if (m == null) throw new AssertFail("komunikaty: " + string.Join(" | ", msgs));
                    return m;
                });
                Check(step, "pręt: A = 3500, TotalLength = 3500, nowy numer, [14] = 2, obrys 3500", () =>
                {
                    var bar = ReadBar(db, _a16.Bar);
                    newMark = (string)Prop(bar, "Mark");
                    int nr = (int)Call(SingleBarType, "ExtractPosNr", newMark);
                    return Num("A", BarNum(db, _a16.Bar, "LengthA"), 3500) + "; " + Num("TL", BarNum(db, _a16.Bar, "TotalLength"), 3500)
                         + "; " + True(nr > 0 && newMark != _a16.Mark, "Mark=" + newMark + " (był " + _a16.Mark + ")")
                         + "; " + Eq("KindOf", Convert.ToString(KindOf(db, _a16.Bar), Inv), "Block")
                         + "; " + Eq("[14]", Rec(db, _a16.Bar, XApp)[14].Value, (short)2)
                         + "; " + Num("obrys", OutlineSpan(db, _a16.Bar), 3500);
                });
                if (newMark == null) return;
                Check(step, "rozkład pręta: [8] = 3500, Mark z nowym numerem", () => DistCheck(db, _a16, 3500, newMark));
                Check(step, "etykieta pręta: „n H12-nn” (nowy numer), back-link, grot na obrysie", () => LabelCheck(db, _a16.Bar, _a16.Label, _a16.N + " " + newMark));
                Check(step, "kopia bez zmian: A = 3000, stary numer, rozkład 3000, etykieta „n " + _b16.Mark + "”", () =>
                    Num("A", BarNum(db, _b16.Bar, "LengthA"), 3000) + "; " + Eq("Mark", BarMark(db, _b16.Bar), _b16.Mark)
                    + "; " + DistCheck(db, _b16, 3000, _b16.Mark) + "; " + LabelCheck(db, _b16.Bar, _b16.Label, _b16.N + " " + _b16.Mark));
                Check(step, "RC_BBS (ReadBars): dwa wiersze (stary numer 3000 × n, nowy 3500 × n), bez „różne pręty”", () =>
                {
                    var b = Bbs(db);
                    int oldNr = (int)Call(SingleBarType, "ExtractPosNr", _b16.Mark), nr = (int)Call(SingleBarType, "ExtractPosNr", newMark);
                    bool okOld = b.rows.Any(r => r.StartsWith(oldNr.ToString(Inv) + "|H12|00|" + _b16.N.ToString(Inv) + "|L=3000|", StringComparison.Ordinal));
                    bool okNew = b.rows.Any(r => r.StartsWith(nr.ToString(Inv) + "|H12|00|" + _a16.N.ToString(Inv) + "|L=3500|", StringComparison.Ordinal));
                    if (b.rows.Count != 2 || !okOld || !okNew) throw new AssertFail("wiersze: " + string.Join(" ; ", b.rows));
                    if (b.warns.Any(w => w.Contains("różne pręty"))) throw new AssertFail("ostrzeżenia: " + string.Join(" | ", b.warns));
                    return string.Join(" ; ", b.rows);
                });
            });
        }

        [CommandMethod("RCSMOKE_F3_S16U", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void F3S16U()
        {
            const string step = "S16U";
            Run(step, db =>
            {
                if (_a16 == null) { Add("FAIL", step, "brak S16SETUP", ""); return; }
                Check(step, "_.U po S16: pręt A = 3000, stary numer, rozkład 3000, etykieta „n " + _a16.Mark + "”, obrys 3000", () =>
                    Num("A", BarNum(db, _a16.Bar, "LengthA"), 3000) + "; " + Eq("Mark", BarMark(db, _a16.Bar), _a16.Mark)
                    + "; " + DistCheck(db, _a16, 3000, _a16.Mark) + "; " + LabelCheck(db, _a16.Bar, _a16.Label, _a16.N + " " + _a16.Mark)
                    + "; " + Num("obrys", OutlineSpan(db, _a16.Bar), 3000));
                Check(step, "BBS po _.U: jeden wiersz", () =>
                {
                    var b = Bbs(db);
                    return Int("wiersze", b.rows.Count, 1) + "; " + string.Join(" ; ", b.rows);
                });
            });
        }

        // ================================================================
        // S17 — pręt-blok gięty: 1 grip, odrzucenie zmiany długości; Info
        // S18 — LengthOverridden
        // ================================================================

        private static int GripCount(Database db, ObjectId id, out int stretchCount)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = (BlockReference)tr.GetObject(id, OpenMode.ForRead);
                var pts = new Point3dCollection();
                br.GetGripPoints(pts, new IntegerCollection(), new IntegerCollection());
                var sp = new Point3dCollection();
                br.GetStretchPoints(sp);
                stretchCount = sp.Count;
                tr.Commit();
                return pts.Count;
            }
        }

        [CommandMethod("RCSMOKE_F3_S17", CommandFlags.Modal)]
        public void F3S17()
        {
            const string step = "S17";
            Run(step, db =>
            {
                CleanModelSpace(db);
                ObjectId b21 = ObjectId.Null, b00 = ObjectId.Null;
                Check(step, "pręty-bloki: 21 H10 500/300/500 (numerowany, etykieta) i 00 H12 A=3000 (numerowany)", () =>
                {
                    b21 = PlaceBarWithLabel(db, "Block", new Def { Code = "21", D = 10, A = 500, B = 300, C = 500, At = new Point3d(0, 40000, 0) }).bar;
                    b00 = PlaceBar(db, "Block", new Def { A = 3000, At = new Point3d(0, 42000, 0) });
                    return BarMark(db, b21) + " / " + BarMark(db, b00);
                });
                if (b21.IsNull || b00.IsNull) return;
                Check(step, "gripy: 21 → 1 (tylko przesunięcie), 00 numerowany → 2; punkty STRETCH 2 / 2", () =>
                {
                    int g21 = GripCount(db, b21, out int s21), g00 = GripCount(db, b00, out int s00);
                    return Int("gripy 21", g21, 1) + "; " + Int("gripy 00", g00, 2) + "; " + Int("STRETCH 21", s21, 2) + "; " + Int("STRETCH 00", s00, 2);
                });
                Check(step, "SingleBarBlock.ApplyEndLength na 21 → NotLengthShape, pręt bez zmian (A, BTR, obrys)", () =>
                {
                    var before = Outline(db, b21);
                    string btr = BtrOf(db, b21);
                    var r = Call(SingleBlkType, "ApplyEndLength", db, b21, false, 500.0, null, null);
                    return Eq("NotLengthShape", Prop(r, "NotLengthShape"), true) + "; " + Eq("Applied", Prop(r, "Applied"), false)
                         + "; " + Num("A", BarNum(db, b21, "LengthA"), 500) + "; " + Eq("BTR", BtrOf(db, b21), btr)
                         + "; " + Pts("obrys", Outline(db, b21), before);
                });
                Check(step, "ApplyLengthEdit na 21 → komunikat [RC AUTO] „kształt gięty”, A = 500", () =>
                {
                    var msgs = ApplyLength(db, b21, 500, "GRIP_STRETCH");
                    string m = msgs.FirstOrDefault(x => x.StartsWith("[RC AUTO]", StringComparison.Ordinal) && x.Contains("kształt gięty"));
                    if (m == null) throw new AssertFail("komunikaty: " + string.Join(" | ", msgs));
                    return m + "; " + Num("A", BarNum(db, b21, "LengthA"), 500);
                });
                Check(step, "Info (RC_TEST_BARBLOCK) opisuje numerowany pręt-blok (IsV2, Describe z Mark)", () =>
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var br = (BlockReference)tr.GetObject(b00, OpenMode.ForRead);
                        bool v2 = (bool)Call(TestEngineType, "IsV2", br);
                        string d = (string)Call(TestEngineType, "Describe", tr, br);
                        tr.Commit();
                        string mark = BarMark(db, b00);
                        return True(v2, "IsV2") + "; " + True(d.Contains("Mark=" + mark), "Describe: " + d.Replace("\n", " "));
                    }
                });
            });
        }

        [CommandMethod("RCSMOKE_F3_S18", CommandFlags.Modal)]
        public void F3S18()
        {
            const string step = "S18";
            Run(step, db =>
            {
                ObjectId b = ObjectId.Null;
                Check(step, "pręt-blok 00 H12 A=3000 numerowany, LengthOverridden, TotalLength 9999", () =>
                {
                    b = PlaceBar(db, "Block", new Def { A = 3000, At = new Point3d(0, 50000, 0) });
                    WriteBar(db, b, x => { SetProp(x, "LengthOverridden", true); SetProp(x, "TotalLength", 9999.0); });
                    return BarMark(db, b);
                });
                if (b.IsNull) return;
                Check(step, "ApplyLengthEdit L + 500 → A = 3500, TotalLength 9999 bez zmian, ostrzeżenie [RC AUTO]", () =>
                {
                    string mark = BarMark(db, b);
                    var msgs = ApplyLength(db, b, 500, "GRIP_STRETCH");
                    string w = msgs.FirstOrDefault(x => x.StartsWith("[RC AUTO]", StringComparison.Ordinal)
                                                     && x.Contains("ręcznie nadpisaną długość całkowitą (9999 mm)"));
                    if (w == null) throw new AssertFail("komunikaty: " + string.Join(" | ", msgs));
                    return Num("A", BarNum(db, b, "LengthA"), 3500) + "; " + Num("TL", BarNum(db, b, "TotalLength"), 9999)
                         + "; " + Eq("LengthOverridden", Prop(ReadBar(db, b), "LengthOverridden"), true)
                         + "; " + Eq("Mark", BarMark(db, b), mark) + "; " + w;
                });
            });
        }

        // ================================================================
        // G1 — grip w trybie kopiowania (symulacja: pending + klon dopisany w tej komendzie → CommandEnded overrule'a)
        // ================================================================

        private static void AddPending(ObjectId id, double oldLen, Matrix3d xf, double delta)
        {
            var ot = Plugin.GetType(GripOverruleType, true);
            var pt = ot.GetNestedType("PendingEdit", BindingFlags.NonPublic);
            if (pt == null) throw new AssertFail("brak typu SingleBarGripOverrule.PendingEdit");
            object pe = Activator.CreateInstance(pt);
            pt.GetField("AtStart").SetValue(pe, false);
            pt.GetField("Delta").SetValue(pe, delta);
            pt.GetField("OldLength").SetValue(pe, oldLen);
            pt.GetField("Transform").SetValue(pe, xf);
            var dict = (IDictionary)ot.GetField("_pending", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            dict[id] = pe;
        }

        private static int OverruleStateCount(string field, Database db)
        {
            var d = (IDictionary)Plugin.GetType(GripOverruleType, true).GetField(field, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            return d.Keys.Cast<ObjectId>().Count(k => k.Database == db);
        }

        /// <summary>Klon referencji (jak kopia BricsCAD): ten sam BTR, XData PRZED dopisaniem do bazy, przesunięty o off.</summary>
        private static ObjectId AppendCloneLikeBricsCad(Database db, ObjectId srcId, Vector3d off)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var src = (BlockReference)tr.GetObject(srcId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var br = new BlockReference(src.Position + off, src.BlockTableRecord) { Rotation = src.Rotation, Layer = src.Layer };
                using (var xd = src.XData) br.XData = xd;
                var id = ms.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                tr.Commit();
                return id;
            }
        }

        [CommandMethod("RCSMOKE_F3_G1", CommandFlags.Modal)]
        public void F3G1()
        {
            const string step = "G1";
            Run(step, db =>
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                CleanModelSpace(db);
                ObjectId a = ObjectId.Null, aDist = ObjectId.Null, c = ObjectId.Null, clone = ObjectId.Null;
                Check(step, "pręty-bloki 00 H12 A=3000: A (kopiowany gripem) + rozkład, C (kontrola, zwykły grip)", () =>
                {
                    (a, _) = PlaceBarWithLabel(db, "Block", new Def { A = 3000, At = new Point3d(0, 90000, 0) });
                    aDist = Distribute(db, a, 40, 91040, 3040, 92000).dist;
                    c = PlaceBar(db, "Block", new Def { A = 3000, At = new Point3d(0, 95000, 0) });
                    return BarMark(db, a) + " / " + BarMark(db, c);
                });
                if (a.IsNull || c.IsNull) return;
                Check(step, "pending L + 500 dla A i C; klon A dopisany w tej komendzie (ObjectAppended) → zapamiętany", () =>
                {
                    AddPending(a, 3000, BlockXf(db, a), 500);
                    AddPending(c, 3000, BlockXf(db, c), 500);
                    clone = AppendCloneLikeBricsCad(db, a, new Vector3d(0, 2500, 0));
                    return Int("_appended", OverruleStateCount("_appended", db), 1);
                });
                Check(step, "CommandEnded overrule'a: A i jego rozkład bez zmian (kopiowanie gripem odrzucone), C → 3500, klon 3000", () =>
                {
                    var mi = Plugin.GetType(GripOverruleType, true).GetMethod("OnCommandEnded", BindingFlags.Static | BindingFlags.NonPublic);
                    var ctor = typeof(Bricscad.ApplicationServices.CommandEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                                       .First(k => k.GetParameters().Length == 1 && k.GetParameters()[0].ParameterType == typeof(string));
                    Invoke(mi, null, new object[] { doc, ctor.Invoke(new object[] { "RCSMOKE_F3_G1" }) });
                    return Num("A", BarNum(db, a, "LengthA"), 3000) + "; " + Num("rozkład A", DistLength(db, aDist), 3000)
                         + "; " + Num("obrys A", OutlineSpan(db, a), 3000) + "; " + Num("C", BarNum(db, c, "LengthA"), 3500)
                         + "; " + Num("klon", BarNum(db, clone, "LengthA"), 3000)
                         + "; " + Int("pending po", OverruleStateCount("_pending", db), 0) + "; " + Int("_appended po", OverruleStateCount("_appended", db), 0);
                });
                Check(step, "komunikat odrzucenia (stała GripCopyRefusedMessage)", () =>
                {
                    var f = Plugin.GetType(GripOverruleType, true).GetField("GripCopyRefusedMessage", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    string m = (string)f.GetValue(null);
                    return True(m.StartsWith("[RC AUTO] Kopiowanie gripem ze zmianą długości", StringComparison.Ordinal), m);
                });
            });
        }

        // ================================================================
        // S16S — _.STRETCH (skrypt) końca numerowanego pręta-bloku z kopią; UNDO / REDO
        // ================================================================

        [CommandMethod("RCSMOKE_F3_S16SSETUP", CommandFlags.Modal)]
        public void F3S16SSetup()
        {
            const string step = "S16SSETUP";
            Run(step, db =>
            {
                Check(step, "dwa pręty-bloki 00 H12 A=3000 o tym samym numerze + etykiety + rozkłady (Y=60000 / 65000)", () =>
                {
                    CleanModelSpace(db);
                    (_a16s, _b16s) = MakePair(db, 60000);
                    return _a16s.Mark + "; koniec osi A = (3000, 60000)";
                });
            });
        }

        private static string _s16sNewMark;

        [CommandMethod("RCSMOKE_F3_S16SV", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void F3S16SV() => S16SAfter("S16SV", "po _.STRETCH +503");

        [CommandMethod("RCSMOKE_F3_S16SR", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void F3S16SR() => S16SAfter("S16SR", "po _.REDO");

        private static void S16SAfter(string step, string when)
        {
            Run(step, db =>
            {
                if (_a16s == null) { Add("FAIL", step, "brak S16SSETUP", ""); return; }
                string newMark = null;
                Check(step, when + ": pręt A = 3505 (5 mm), TotalLength 3525 (00 w górę do 25), nowy numer, obrys 3505", () =>
                {
                    newMark = BarMark(db, _a16s.Bar);
                    if (step == "S16SV") _s16sNewMark = newMark;
                    return Num("A", BarNum(db, _a16s.Bar, "LengthA"), 3505) + "; " + Num("TL", BarNum(db, _a16s.Bar, "TotalLength"), 3525)
                         + "; " + True(newMark != _a16s.Mark && (int)Call(SingleBarType, "ExtractPosNr", newMark) > 0, "Mark=" + newMark)
                         + (step == "S16SR" ? "; " + Eq("Mark jak po STRETCH", newMark, _s16sNewMark) : "")
                         + "; " + Num("obrys", OutlineSpan(db, _a16s.Bar), 3505)
                         + "; " + Pt("axis[0]", ((Point3d)PosOf(db, _a16s.Bar)), new Point3d(0, 60000, 0));
                });
                if (newMark == null) return;
                Check(step, when + ": rozkład [8] = 3505 z nowym numerem; etykieta „n H12-nn”", () =>
                    DistCheck(db, _a16s, 3505, newMark) + "; " + LabelCheck(db, _a16s.Bar, _a16s.Label, _a16s.N + " " + newMark));
                Check(step, when + ": kopia bez zmian", () =>
                    Num("A", BarNum(db, _b16s.Bar, "LengthA"), 3000) + "; " + Eq("Mark", BarMark(db, _b16s.Bar), _b16s.Mark)
                    + "; " + DistCheck(db, _b16s, 3000, _b16s.Mark));
                Check(step, when + ": BBS dwa wiersze", () =>
                {
                    var b = Bbs(db);
                    return Int("wiersze", b.rows.Count, 2) + "; " + string.Join(" ; ", b.rows);
                });
            });
        }

        [CommandMethod("RCSMOKE_F3_S16SU", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void F3S16SU()
        {
            const string step = "S16SU";
            Run(step, db =>
            {
                if (_a16s == null) { Add("FAIL", step, "brak S16SSETUP", ""); return; }
                Check(step, "_.UNDO 1 po STRETCH: A = 3000, stary numer, obrys 3000, rozkład 3000, etykieta „n " + _a16s.Mark + "”", () =>
                    Num("A", BarNum(db, _a16s.Bar, "LengthA"), 3000) + "; " + Eq("Mark", BarMark(db, _a16s.Bar), _a16s.Mark)
                    + "; " + Num("obrys", OutlineSpan(db, _a16s.Bar), 3000)
                    + "; " + DistCheck(db, _a16s, 3000, _a16s.Mark) + "; " + LabelCheck(db, _a16s.Bar, _a16s.Label, _a16s.N + " " + _a16s.Mark));
                Check(step, "BBS po UNDO: jeden wiersz", () =>
                {
                    var b = Bbs(db);
                    return Int("wiersze", b.rows.Count, 1) + "; " + string.Join(" ; ", b.rows);
                });
            });
        }

        // ================================================================
        // S17S — _.STRETCH (skrypt) końca pręta-bloku 21 → odrzucone
        // ================================================================

        private static ObjectId _b17s;
        private static List<Point3d> _b17sOutline;
        private static string _b17sBtr;

        [CommandMethod("RCSMOKE_F3_S17SSETUP", CommandFlags.Modal)]
        public void F3S17SSetup()
        {
            const string step = "S17SSETUP";
            Run(step, db =>
            {
                Check(step, "pręt-blok 21 H10 500/300/500 numerowany; koniec osi przesunięty do (5000, 80000)", () =>
                {
                    CleanModelSpace(db);
                    _b17s = PlaceBar(db, "Block", new Def { Code = "21", D = 10, A = 500, B = 300, C = 500, At = new Point3d(4000, 80000, 0) });
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var br = (BlockReference)tr.GetObject(_b17s, OpenMode.ForWrite);
                        var bar = Call(SingleBarType, "ReadBarXData", br);
                        var end = (Point3d)Call(SingleBlkType, "AxisEndWcs", br, bar);
                        br.TransformBy(Matrix3d.Displacement(new Point3d(5000, 80000, 0) - end));
                        var start = (Point3d)Call(SingleBlkType, "AxisStartWcs", br);
                        tr.Commit();
                        if (start.DistanceTo(new Point3d(5000, 80000, 0)) < 50) throw new AssertFail("początek osi za blisko końca");
                    }
                    _b17sOutline = Outline(db, _b17s);
                    _b17sBtr = BtrOf(db, _b17s);
                    return BarMark(db, _b17s) + " Position=" + P((Point3d)PosOf(db, _b17s));
                });
            });
        }

        [CommandMethod("RCSMOKE_F3_S17SV", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void F3S17SV()
        {
            const string step = "S17SV";
            Run(step, db =>
            {
                if (_b17s.IsNull) { Add("FAIL", step, "brak S17SSETUP", ""); return; }
                Check(step, "po _.STRETCH końca 21: pręt bez zmian (A, BTR, obrys)", () =>
                    Num("A", BarNum(db, _b17s, "LengthA"), 500) + "; " + Num("C", BarNum(db, _b17s, "LengthC"), 500)
                    + "; " + Eq("BTR", BtrOf(db, _b17s), _b17sBtr) + "; " + Pts("obrys", Outline(db, _b17s), _b17sOutline));
            });
        }

        // ================================================================
        // AR — pełny AutoRebar: tryb Blok = tryb Polilinia (numery, BBS)
        // ================================================================

        private const double ArW = 12000, ArH = 8000, ArTopDy = -20000;   // jak E3 S6 / tools/RegressionDump

        private static ObjectId ArRect(Database db, double x0, double y0, double x1, double y1)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has("SD-PILED-RAFT"))
                {
                    lt.UpgradeOpen();
                    var ltr = new LayerTableRecord { Name = "SD-PILED-RAFT" };
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var pl = new Polyline(4);
                pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
                pl.Closed = true;
                pl.Layer = "SD-PILED-RAFT";
                var id = ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                tr.Commit();
                return id;
            }
        }

        private static void ResetCounter(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
                if (nod.Contains(CounterKey))
                {
                    nod.UpgradeOpen();
                    var id = nod.GetAt(CounterKey);
                    nod.Remove(CounterKey);
                    tr.GetObject(id, OpenMode.ForWrite).Erase();
                }
                tr.Commit();
            }
        }

        private sealed class ArResult
        {
            public string Gen = "";
            public List<int> Numbers = new List<int>();
            public List<string> Rows = new List<string>(), Warns = new List<string>();
            public int Dists, Bars, BlockBars, PolyBars;
        }

        private static ArResult ArRun(Database db, string fmt)
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            CleanModelSpace(db);
            ResetCounter(db);
            var bottom = ArRect(db, 0, 0, ArW, ArH);
            var top = ArRect(db, 0, ArTopDy, ArW, ArH + ArTopDy);
            var r = new ArResult();
            SetFormat(fmt);
            try
            {
                var g = new List<string>();
                void G(string name, object v)
                {
                    int n = Convert.ToInt32(v, Inv);
                    g.Add(name + "=" + n.ToString(Inv));
                    if (n < 0) throw new AssertFail(string.Join(" ", g) + " — " + name + " zwrócił błąd");
                }
                G("B1", Call(AutoRebarType, "GenerateLayer", doc, bottom, "rebar_bottom", "X", "B1", 10, 200.0, 40.0, true));
                G("B2", Call(AutoRebarType, "GenerateLayer", doc, bottom, "rebar_bottom", "Y", "B2", 10, 200.0, 40.0, true));
                G("T1", Call(AutoRebarType, "GenerateLayer", doc, top, "rebar_top", "X", "T1", 12, 200.0, 40.0, true));
                G("T2", Call(AutoRebarType, "GenerateLayer", doc, top, "rebar_top", "Y", "T2", 12, 200.0, 40.0, true));
                G("UB_B1", Call(AutoRebarType, "GenerateUBLayer", doc, bottom, "rebar_bottom", "B1", 225, "X", 200.0, 40.0, true));
                G("UB_B2", Call(AutoRebarType, "GenerateUBLayer", doc, bottom, "rebar_bottom", "B2", 225, "Y", 200.0, 40.0, true));
                r.Gen = string.Join(" ", g);
            }
            finally { SetFormat("Polyline"); }
            r.Numbers = ((IEnumerable)Call(CounterType, "GetUsedPositionNumbers", db)).Cast<int>().OrderBy(n => n).ToList();
            (r.Rows, r.Warns, r.Dists) = Bbs(db);
            foreach (var id in ScanMs(db, e => Has(e, XApp)))
            {
                string k = Convert.ToString(KindOf(db, id), Inv);
                if (k == "Block") r.BlockBars++;
                else if (k == "Polyline") r.PolyBars++;
                r.Bars++;
            }
            return r;
        }

        [CommandMethod("RCSMOKE_F3_AR", CommandFlags.Modal)]
        public void F3Ar()
        {
            const string step = "AR";
            Run(step, db =>
            {
                ArResult p = null, b = null;
                Check(step, "AutoRebar w trybie Polilinia (B1, B2, T1, T2, UB B1, UB B2)", () =>
                {
                    p = ArRun(db, "Polyline");
                    return p.Gen + "; pręty=" + p.Bars.ToString(Inv) + " numery=" + string.Join(",", p.Numbers) + " wiersze BBS=" + p.Rows.Count.ToString(Inv);
                });
                Check(step, "AutoRebar w trybie Blok", () =>
                {
                    b = ArRun(db, "Block");
                    return b.Gen + "; pręty=" + b.Bars.ToString(Inv) + " numery=" + string.Join(",", b.Numbers) + " wiersze BBS=" + b.Rows.Count.ToString(Inv);
                });
                if (p == null || b == null) return;
                Check(step, "tryb Polilinia: wszystkie pręty to polilinie; tryb Blok: wszystkie to pręty-bloki", () =>
                    True(p.Bars > 0 && p.PolyBars == p.Bars, $"Polilinia: pręty={p.Bars} polilinie={p.PolyBars}")
                    + "; " + True(b.Bars > 0 && b.BlockBars == b.Bars, $"Blok: pręty={b.Bars} bloki={b.BlockBars}")
                    + "; " + Int("liczba prętów", b.Bars, p.Bars));
                Check(step, "numery pozycji Blok = Polilinia", () =>
                    Eq("numery", string.Join(",", b.Numbers), string.Join(",", p.Numbers)));
                Check(step, "BBS (ReadBars) Blok = Polilinia: wiersze, ostrzeżenia, liczba rozkładów", () =>
                {
                    for (int i = 0; i < Math.Max(p.Rows.Count, b.Rows.Count); i++)
                    {
                        string rp = i < p.Rows.Count ? p.Rows[i] : "(brak)", rb = i < b.Rows.Count ? b.Rows[i] : "(brak)";
                        if (rp != rb) throw new AssertFail($"wiersz {i}: Blok „{rb}”, Polilinia „{rp}”");
                    }
                    Eq("ostrzeżenia", string.Join(" | ", b.Warns), string.Join(" | ", p.Warns));
                    return Int("rozkłady", b.Dists, p.Dists) + "; wiersze=" + b.Rows.Count.ToString(Inv) + ": " + string.Join(" ; ", b.Rows);
                });
            });
        }
    }
}
