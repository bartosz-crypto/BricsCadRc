// W4 — testy dymne kształtów BS 8666 (docs/PLAN_W4_BS8666.md: W4a, W4b, W4c) w BricsCAD V25.
// Uruchamiane skryptem planc-w4.scr (run-planc-w4.ps1). Komendy RCSMOKE_W4_*. Klasa częściowa PlanCFixW1Tests —
// te same helpery (pręty, rozkłady, refleksja); RCSMOKE_W4_SETUP przełącza plik wyników na planc-w4-results.txt.
//
// W4a (pasy Y 10000–18000):
//  A1 15 H12 {500,200} z obrysem SPRZED W4 (LegacyBarGeometry), obrót 30° + MIRROR (x = 5000) w kodzie (watcher wyciszony)
//     → _.MOVE +1000 (skrypt) → Plan B: obrys = geometria bieżąca (łuk styczny), rama (150°, odbicie) zachowana
//  A2 to samo → BarEditApply B 200 → 250 → geometria bieżąca w ramie obrysu sprzed W4
// W4b (pasy Y 20000–30000):
//  B1 51 H12 {400,300,C pusty} → TL 1600 (BS 8666:2020), hak C_eff = 156
//  B2 47 H12 bez C + rozkład + opis, 21 H10 + rozkład + opis → RC_BBS (ReadBars): ostrzeżenie „brak długości” dla 47,
//     21 z długością; RC_SCHEDULE (BuildSchedule) bez wyjątku, wiersz 47 „brak długości”
//  B3 47 H12 {300,200,200} z zapisaną długością 1075 (formuła 2005) → RC_BBS: bezpiecznik „wg BS 8666:2020 = 1250 mm, w rysunku 1075”
// W4c (pasy Y 40000–58000) — 44 = kapelusz (znacznik [15] = 2020), 75 = pierścień; stary pierścień „44” bez znacznika:
//  C1 stary pierścień (polilinia, rekord 14 pól z „44”) → odczyt 75 + LegacyRing44; _.MOVE +1000 → oba okręgi, rekord bez zmian
//  C2 stary pierścień → BarEditApply A 300 → 400 → zapis „75” (migracja), okręgi 200 ± 6; _.UNDO 1 → „44”, A 300
//  C3 nowy 44 H12 {500,400,500,400,300} → [14] = 1, [15] = 2020; _.ROTATE 30° → kapelusz 30°, znacznik; RC_EDIT_BAR B 350
//  C4 stary pierścień jako pręt-blok ([14] = 2, bez [15]) → 75 + LegacyRing44
//  C5 nowy 44 → _.MIRROR N (x = 5000) i _.COPY +2000: kopie ze znacznikiem, odczyt 44, kopia odbita
//  C6 stary pierścień (pole C = 200 — śmieć) + rozkład + opis → RC_BBS: ostrzeżenie „stary kształt 44 … 75”, wiersz 75;
//     RC_SCHEDULE: wiersz bez C–E (runda 1, uwaga 4)
// Runda 1, uwaga 1 (pasy Y 60000–68000) — długość rozkładu Manual wg geometrii sprzed W4 (0-POPRAWKI p.1):
//  D  15 H12 {500,200,300}: obrys sprzed W4 (wzorzec), polilinia bieżąca obrócona 30°, pręt-blok bieżący; 11 i 21 kontrolnie
// W4d (pasy Y 70000–92000) — geometria wg rysunków BS 8666:2020 (Bs2020Sharp):
//  E  12 14 15 22 24 33 46 51 H12 (polilinie, x = 0) + rozkład Manual = długość wzorca; zamrożone obrysy etapu W4b–W4c
//     (15, 51) obrócone 30° + MIRROR x = 5000 w kodzie; pręty-bloki 15 / 22 / 33 / 51 (x = 20000) → RC_EDIT_BAR
//  skrypt: _.MOVE +1000 (EV) → _.ROTATE 30° wokół (1000,70000) (ER) → _.MIRROR x = 5000, usuń źródło (EM) →
//  RC_EDIT_BAR A + 50 (EEDIT / EED): po każdym kroku obrys = geometria W4d w ramie pręta (rama zachowana)
//  F (runda 1, uwaga 1): po ROTATE i MIRROR — rozkład Manual 33 / 15 / 46 (polilinia i pręt-blok): tworzenie
//     (BarCommands.ManualSegmentLength + GenerateFromBounds) i edycja (RebuildWithNewLayout, inne ramię) = odcinek W4d
// B00 (runda 1, uwaga 2): RC_BBS — 00 A 3010 z TL 3010 → ostrzeżenie 3025; 3000 bez; nadpisana 3010 bez
// G / GV (runda 2): znacznik [15] = 2021 (obrys W4d). 51 H12 A = B = 400, C = 0 (obrys W4b–W4c == W4d): stary pręt bez
//     znacznika (polilinia i blok, obrót 30° + MIRROR w kodzie) → rozkład Manual haka = 168 (wzorzec); nowy (ze znacznikiem)
//     → 132; stary pręt po _.MOVE (Plan B przebudowuje obrys geometrią W4d) → znacznik dopisany → 132 (D34)
// H / HV / HU / HR (runda 3): historyczny pręt-blok 51 A = B (bez [15]) → RC_UPDATE_BAR (UpdateBarBlock) i _.SCALE 2
//     (automatyczna naprawa skali) → [14] = 2, [15] = 2021, Manual 132; _.U i _.REDO — rekord i długość Manual spójne
// W / WV / WEDIT / WED (W4e, poprawka #13 — wymiary zewnętrzne, pasy Y 140000–147000):
//  W1 UB 225 z AutoRebar (CreateUBTemplate 700 / 140 / 700) → obrys 140 × 700 = B × A z XData
//  W2 RC_SECTION: pręt wzorcowy w widoku (SourceShape, bez AxisParams) → obrys 140 × 700 (jak przed W4e)
//  W3 21 / 15 / 44 z ZAMROŻONYM obrysem etapu W4d (znacznik 2021), obrót 30° + MIRROR w kodzie → rozkład Manual (każdy odcinek)
//     → skrypt _.MOVE +1000 → obrys W4e w tej samej ramie, axis0 zachowany, Manual bez zmian (0-POPRAWKI p.1)
//  W4 pręt-blok 21 → RC_EDIT_BAR A 750 → obrys BTR 140 × 750
// WF (W4f): 13 H12 B = 80 i 11 H12 A = 100 + rozkłady → RC_BBS: ostrzeżenia „B < q (85)”, „A < P (125)” (raz, BBS liczony)
// WR / WRV / WRU (RC_REBUILD_BARS): 21 z obrysem W4d (obrót + odbicie, etykieta) → przebudowa w ramie, grot na obrysie;
//     pręt bieżący i zepsuty obrys bez zmian; _.U cofa komendę w całości
// Obrysy „starych” prętów (A, D, E zamrożone) — rekord bez ogona [14+] (jak rysunek sprzed W4d)

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.BrxSmokeTests
{
    /// <summary>
    /// Plan W4c: kod kształtu pręta-pierścienia wtyczki (refleksja) — BarFrameRules.RingShapeCode („75” po W4c);
    /// wtyczka bez tego pola (przed W4c) → „44”. Rozpoznanie: BarFrameRules.IsRingShape (brak → kod == „44”).
    /// </summary>
    internal static class W4Compat
    {
        private static System.Reflection.Assembly PluginAsm
            => AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "NaSmartRc");

        private static Type FrameRules => PluginAsm?.GetType("BricsCadRc.Core.BarFrameRules", false);

        public static string RingCode
        {
            get
            {
                var f = FrameRules?.GetField("RingShapeCode",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                return f != null ? (string)f.GetValue(null) : "44";
            }
        }

        /// <summary>
        /// W4e: promień osi pierścienia — BarDimensionRules.RingAxisRadius (A = średnica zewnętrzna → (A − d)/2);
        /// wtyczka sprzed W4e (brak klasy) → A/2.
        /// </summary>
        public static double RingAxisRadius(double a, double d)
        {
            var t = PluginAsm?.GetType("BricsCadRc.Core.BarDimensionRules", false);
            var m = t?.GetMethod("RingAxisRadius", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public,
                                 null, new[] { typeof(double), typeof(double) }, null);
            return m != null ? (double)m.Invoke(null, new object[] { a, d }) : a / 2;
        }

        /// <summary>W4e: flaga ExternalDims w BarGeometryOptions.Current (wymiary zewnętrzne); brak właściwości → false.</summary>
        public static bool ExternalDims
        {
            get
            {
                var t = PluginAsm?.GetType("BricsCadRc.Core.BarGeometryOptions", false);
                var cur = t?.GetProperty("Current")?.GetValue(null);
                var p = t?.GetProperty("ExternalDims");
                return cur != null && p != null && (bool)p.GetValue(cur);
            }
        }

        public static bool IsRing(string code)
        {
            var m = FrameRules?.GetMethod("IsRingShape",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
            return m != null ? (bool)m.Invoke(null, new object[] { code }) : code == "44";
        }
    }

    public partial class PlanCFixW1Tests
    {
        private static bool _w4Mode;

        private static readonly List<string> ExpectedStepsW4 = new List<string>
        {
            "SETUP",
            "A", "AEDIT", "AV",
            "B",
            "C", "CV", "CEDIT", "CEDV", "CU",
            "D",
            "B00",
            "E", "EV", "ER", "EM", "EEDIT", "EED", "F", "G", "GV", "H", "HV", "HU", "HR",
            "W", "WV", "WEDIT", "WED", "WF", "WR", "WRV", "WRU", "WG", "WGV",
        };

        private const string FrameRulesType = "BricsCadRc.Core.BarFrameRules";

        private static void SetModeW4()
        {
            _w2Mode = false;
            _f3Mode = false;
            _w4Mode = true;
            ResultsFile = "planc-w4-results.txt";
            Prefix = "[RC W4]";
            Title = "W4 — testy dymne kształtów BS 8666";
        }

        [CommandMethod("RCSMOKE_W4_SETUP", CommandFlags.Modal)]
        public void W4Setup()
        {
            SetModeW4();
            _results.Clear();
            _executed.Clear();
            const string step = "SETUP";
            Run(step, db =>
            {
                Check(step, "wtyczka NaSmartRc wczytana (jedna kopia)", () =>
                {
                    var all = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "NaSmartRc").ToList();
                    if (all.Count != 1) throw new AssertFail("wczytane kopie NaSmartRc: " + all.Count);
                    Plugin.GetType("BricsCadRc.Core.LegacyBarGeometry", true);
                    return "Location=" + Loc(Plugin);
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

        [CommandMethod("RCSMOKE_W4_REPORT", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4Report()
        {
            Report();
            SetMode(false);
        }

        // ================================================================
        // W4a — obrysy sprzed W4 rozpoznawane (rama), przebudowa do geometrii bieżącej
        // ================================================================

        private static ObjectId _w4a1, _w4a2;
        private static readonly Vector3d Dir150 = new Vector3d(-Math.Cos(Math.PI / 6), Math.Sin(Math.PI / 6), 0);

        [CommandMethod("RCSMOKE_W4_A", CommandFlags.Modal)]
        public void W4A()
        {
            const string step = "A";
            Run(step, db =>
            {
                _w4a1 = _w4a2 = ObjectId.Null;
                Check(step, "A1 (0,10000) i A2 (0,14000): 15 H12 {500,200} z obrysem sprzed W4, obrót 30° + MIRROR x=5000", () =>
                {
                    // W4d: C = 300 (geometria 15 wg normy: A skośne + C poziome; sprzed W4 C bez wpływu na obrys)
                    _w4a1 = PlaceLegacyOutlineBar(db, new Def { Code = "15", A = 500, B = 200, C = 300, At = new Point3d(0, 10000, 0) }, true);
                    _w4a2 = PlaceLegacyOutlineBar(db, new Def { Code = "15", A = 500, B = 200, C = 300, At = new Point3d(0, 14000, 0) }, true);
                    return "OK";
                });
                Check(step, "A1: obrys sprzed W4 ≠ geometria bieżąca (łuk 135°), oś startuje w (10000,10000)", () =>
                {
                    var v = PolyVerts(db, _w4a1);
                    var cur = ExpectedOutline(ReadBar(db, _w4a1), new Point3d(10000, 10000, 0), Dir150, true);
                    double max = MaxDev(v, cur);
                    if (max < 1.0) throw new AssertFail("obrys sprzed W4 nie różni się od bieżącego (max " + F(max) + ")");
                    return Pt("axis0", Axis(v)[0], new Point3d(10000, 10000, 0), 1e-6) + "; różnica od bieżącej " + F(max) + " mm";
                });
            });
        }

        [CommandMethod("RCSMOKE_W4_AEDIT", CommandFlags.Modal)]
        public void W4AEdit()
        {
            const string step = "AEDIT";
            Run(step, db =>
            {
                Check(step, "A2: BarEditApply B 200 → 250", () => { EditApply(db, _w4a2, b => SetProp(b, "LengthB", 250.0)); return "OK"; });
            });
        }

        [CommandMethod("RCSMOKE_W4_AV", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4AV()
        {
            const string step = "AV";
            Run(step, db =>
            {
                Check(step, "A1: _.MOVE +1000 → geometria bieżąca, kierunek 150°, odbicie, axis0 (11000,10000)", () =>
                    Pts("A1 obrys", PolyVerts(db, _w4a1), ExpectedOutline(ReadBar(db, _w4a1), new Point3d(11000, 10000, 0), Dir150, true)));
                Check(step, "A2: RC_EDIT_BAR B 250 → geometria bieżąca w ramie obrysu sprzed W4, axis0 (10000,14000)", () =>
                    Num("B", BarNum(db, _w4a2, "LengthB"), 250) + "; "
                    + Pts("A2 obrys", PolyVerts(db, _w4a2), ExpectedOutline(ReadBar(db, _w4a2), new Point3d(10000, 14000, 0), Dir150, true)));
            });
        }

        // ================================================================
        // W4b — formuły BS 8666:2020, BBS / RC_SCHEDULE odporne na brak długości, bezpiecznik
        // ================================================================

        [CommandMethod("RCSMOKE_W4_B", CommandFlags.Modal)]
        public void W4B()
        {
            const string step = "B";
            Run(step, db =>
            {
                ObjectId b1 = ObjectId.Null, b2 = ObjectId.Null, b3 = ObjectId.Null;
                string mark2 = "", mark3 = "";
                // W4e: hak C_eff jest wymiarem zewnętrznym → oś C_eff − d/2 = 150
                double hook = W4Compat.ExternalDims ? 150 : 156;
                Check(step, "B1: 51 H12 {400,300,C pusty} → TL 1600, hak osi " + F(hook) + " (C_eff 156)", () =>
                {
                    b1 = PlaceBar(db, "Polyline", new Def { Code = "51", A = 400, B = 300, C = 0, At = new Point3d(0, 20000, 0) });
                    var ax = Axis(PolyVerts(db, b1));
                    return Num("TL", BarNum(db, b1, "TotalLength"), 1600) + "; "
                         + Num("hak (axis[ost].Y − axis[0].Y)", ax[ax.Count - 1].Y - ax[0].Y, hook, 1e-6);
                });
                Check(step, "B2: 47 H12 bez C (zapis bez długości) + rozkład + opis; 21 H10 + rozkład + opis", () =>
                {
                    b2 = PlaceBar(db, "Polyline", new Def { Code = "47", A = 300, B = 200, C = 200, At = new Point3d(0, 24000, 0) });
                    WriteBar(db, b2, b => { SetProp(b, "LengthC", 0.0); SetProp(b, "TotalLength", 1075.0); });
                    mark2 = (string)Prop(ReadBar(db, b2), "Mark");
                    Distribute(db, b2, 40, 25040, 3040, 26000);
                    var u = PlaceBar(db, "Polyline", new Def { Code = "21", D = 10, A = 500, B = 300, C = 500, At = new Point3d(5000, 24000, 0) });
                    Distribute(db, u, 5040, 25040, 8040, 26000);
                    return "47=" + mark2;
                });
                Check(step, "B3: 47 H12 {300,200,200}, zapisana długość 1075 (formuła 2005) + rozkład + opis", () =>
                {
                    b3 = PlaceBar(db, "Polyline", new Def { Code = "47", A = 300, B = 200, C = 200, At = new Point3d(0, 28000, 0) });
                    WriteBar(db, b3, b => SetProp(b, "TotalLength", 1075.0));
                    mark3 = (string)Prop(ReadBar(db, b3), "Mark");
                    Distribute(db, b3, 40, 29040, 3040, 30000);
                    return "47=" + mark3;
                });
                Check(step, "RC_BBS (ReadBars): 47 bez C → „brak długości”; 47 z C → bezpiecznik 1250 / 1075; 21 z długością", () =>
                {
                    var res = CallD("BricsCadRc.Core.BbsDrawingReader", "ReadBars", db);
                    var warns = ((IEnumerable<string>)Prop(res, "Warnings")).ToList();
                    int p2 = (int)Call(SingleBarType, "ExtractPosNr", mark2), p3 = (int)Call(SingleBarType, "ExtractPosNr", mark3);
                    string w2 = warns.FirstOrDefault(w => w.Contains("Pozycja " + p2.ToString("00", Inv) + " (kształt 47)") && w.Contains("brak długości"));
                    string w3 = warns.FirstOrDefault(w => w.Contains("Pozycja " + p3.ToString("00", Inv) + " (kształt 47)")
                                                         && w.Contains("BS 8666:2020 = 1250 mm") && w.Contains("w rysunku 1075"));
                    if (w2 == null) throw new AssertFail("brak ostrzeżenia 47 bez C; ostrzeżenia: " + string.Join(" | ", warns));
                    if (w3 == null) throw new AssertFail("brak bezpiecznika 47 1250/1075; ostrzeżenia: " + string.Join(" | ", warns));
                    var rows = ((IEnumerable)Prop(res, "Rows")).Cast<object>().ToList();
                    var r21 = rows.FirstOrDefault(r => (string)Prop(r, "ShapeCode") == "21");
                    if (r21 == null) throw new AssertFail("brak wiersza 21");
                    return Num("21 długość", Convert.ToDouble(Prop(r21, "Length"), Inv), 1275) + "; " + w2 + " | " + w3;
                });
                Check(step, "RC_SCHEDULE (BuildSchedule): bez wyjątku, 47 bez C → „brak długości”, ostrzeżenie", () =>
                {
                    var warns = new List<string>();
                    var entries = ((IEnumerable)Call("BricsCadRc.Core.BarScheduleEngine", "BuildSchedule", db, warns)).Cast<object>().ToList();
                    var e47 = entries.FirstOrDefault(e => (string)Prop(e, "Mark") == mark2);
                    if (e47 == null) throw new AssertFail("brak wiersza " + mark2);
                    return Eq("ColLength", Prop(e47, "ColLength"), "brak długości") + "; " + Int("wiersze", entries.Count, 3)
                         + "; " + True(warns.Any(w => w.Contains(mark2)), "ostrzeżenie: " + string.Join(" | ", warns));
                });
            });
        }

        // ================================================================
        // W4c — 44 = kapelusz, 75 = pierścień, stary 44-pierścień
        // ================================================================

        private static ObjectId _c1, _c2, _c3, _c4, _c5, _c6;
        private static string _c5Mark = "";
        private static readonly double[] HatPv = { 500, 400, 500, 400, 300 };

        [CommandMethod("RCSMOKE_W4_C", CommandFlags.Modal)]
        public void W4C()
        {
            const string step = "C";
            Run(step, db =>
            {
                _c1 = _c2 = _c3 = _c4 = _c5 = _c6 = ObjectId.Null;
                Check(step, "RingShapeCode = 75 (W4c)", () => Eq("RingCode", W4Compat.RingCode, "75"));
                Check(step, "C1 (0,40000), C2 (0,43000): stary pierścień H12 A=300 (rekord 14 pól „44”) → odczyt 75 + LegacyRing44", () =>
                {
                    _c1 = PlaceLegacyRing(db, new Point3d(0, 40000, 0), "Polyline");
                    _c2 = PlaceLegacyRing(db, new Point3d(0, 43000, 0), "Polyline");
                    var b = ReadBar(db, _c1);
                    return Eq("ShapeCode", Prop(b, "ShapeCode"), "75") + "; " + Eq("LegacyRing44", Prop(b, "LegacyRing44"), true)
                         + "; " + Eq("rekord [3]", RawField(db, _c1, 3), "44") + "; " + Int("rekord pól", RawCount(db, _c1), 14)
                         + "; " + RingCheck(db, _c1, new Point3d(150, 40150, 0), 300, 12);
                });
                Check(step, "C3 (0,46000): nowy 44 H12 {500,400,500,400,300} → [14] = 1, [15] = 2020, odczyt 44, obrys 60 wierzchołków", () =>
                {
                    _c3 = PlaceHat(db, new Point3d(0, 46000, 0));
                    return HatRecordCheck(db, _c3) + "; " + Int("wierzchołki", PolyVerts(db, _c3).Count, 60)
                         + "; " + Pts("obrys", PolyVerts(db, _c3), ExpectedOutline(ReadBar(db, _c3), new Point3d(0, 46000, 0), new Vector3d(1, 0, 0), false));
                });
                Check(step, "C4 (3000,40000): stary pierścień jako pręt-blok ([14] = 2, bez [15]) → 75 + LegacyRing44", () =>
                {
                    _c4 = PlaceLegacyRing(db, new Point3d(3000, 40000, 0), "Block");
                    var b = ReadBar(db, _c4);
                    return Eq("KindOf", Convert.ToString(KindOf(db, _c4), Inv), "Block") + "; " + Eq("ShapeCode", Prop(b, "ShapeCode"), "75")
                         + "; " + Eq("LegacyRing44", Prop(b, "LegacyRing44"), true) + "; " + Int("rekord pól", RawCount(db, _c4), 15)
                         + "; " + Int("pierścienie bloku", BlockRings(db, _c4).Count, 2);
                });
                Check(step, "C5 (0,52000): nowy 44 (do MIRROR / COPY)", () =>
                {
                    _c5 = PlaceHat(db, new Point3d(0, 52000, 0));
                    _c5Mark = (string)Prop(ReadBar(db, _c5), "Mark");
                    return HatRecordCheck(db, _c5);
                });
                Check(step, "C6 (0,56000): stary pierścień + rozkład + opis", () =>
                {
                    _c6 = PlaceLegacyRing(db, new Point3d(0, 56000, 0), "Polyline", garbageC: 200.0);
                    Distribute(db, _c6, 40, 57040, 3040, 58000);
                    return "OK";
                });
            });
        }

        [CommandMethod("RCSMOKE_W4_CV", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4CV()
        {
            const string step = "CV";
            Run(step, db =>
            {
                Check(step, "C1: _.MOVE okręgu zewnętrznego +1000 → oba okręgi wokół (1150,40150), rekord nadal „44” (bez zapisu)", () =>
                    RingCheck(db, _c1, new Point3d(1150, 40150, 0), 300, 12) + "; " + Eq("rekord [3]", RawField(db, _c1, 3), "44")
                    + "; " + Eq("odczyt", Prop(ReadBar(db, _c1), "ShapeCode"), "75"));
                Check(step, "C3: _.ROTATE 30° → kapelusz 30° z axis0 (0,46000), znacznik zostaje", () =>
                    Pts("obrys", PolyVerts(db, _c3), ExpectedOutline(ReadBar(db, _c3), new Point3d(0, 46000, 0), Dir30, false))
                    + "; " + HatRecordCheck(db, _c3));
                Check(step, "C5: _.MIRROR N → kopia odbita (axis0 (10000,52000), kierunek (−1,0)), znacznik w kopii", () =>
                {
                    var copy = FindBar(db, _c5Mark, 52000, _c5);
                    if (Math.Abs(Axis(PolyVerts(db, copy))[0].X - 10000) > 1) throw new AssertFail("kopia MIRROR nie w x = 10000");
                    return Pts("kopia", PolyVerts(db, copy), ExpectedOutline(ReadBar(db, copy), new Point3d(10000, 52000, 0), new Vector3d(-1, 0, 0), true))
                         + "; " + HatRecordCheck(db, copy) + "; " + HatRecordCheck(db, _c5);
                });
                Check(step, "C5: _.COPY +2000 → kopia (0,54000) ze znacznikiem, odczyt 44", () =>
                {
                    var copy = FindBarAt(db, _c5Mark, new Point3d(0, 54000, 0));
                    return Pt("axis0", Axis(PolyVerts(db, copy))[0], new Point3d(0, 54000, 0)) + "; " + HatRecordCheck(db, copy);
                });
                Check(step, "C6: RC_BBS — ostrzeżenie „stary kształt 44 … 75”, wiersz kształtu 75", () =>
                {
                    var res = CallD("BricsCadRc.Core.BbsDrawingReader", "ReadBars", db);
                    var warns = ((IEnumerable<string>)Prop(res, "Warnings")).ToList();
                    int pos = (int)Call(SingleBarType, "ExtractPosNr", (string)Prop(ReadBar(db, _c6), "Mark"));
                    string w = warns.FirstOrDefault(x => x.Contains("Pozycja " + pos.ToString("00", Inv)) && x.Contains("stary kształt 44") && x.Contains("75"));
                    if (w == null) throw new AssertFail("brak ostrzeżenia; ostrzeżenia: " + string.Join(" | ", warns));
                    var rows = ((IEnumerable)Prop(res, "Rows")).Cast<object>().ToList();
                    var row = rows.FirstOrDefault(r => (int)Prop(r, "BarMark") == pos);
                    if (row == null) throw new AssertFail("brak wiersza pozycji " + pos);
                    return Eq("kształt", Prop(row, "ShapeCode"), "75") + "; " + Num("długość", Convert.ToDouble(Prop(row, "Length"), Inv), 950) + "; " + w;
                });
                Check(step, "C6: RC_SCHEDULE — wiersz pierścienia 75 bez C–E (pole C = 200 w XData pominięte), XData bez zmian", () =>
                {
                    string mark = (string)Prop(ReadBar(db, _c6), "Mark");
                    var entries = ((IEnumerable)Call("BricsCadRc.Core.BarScheduleEngine", "BuildSchedule", db, new List<string>())).Cast<object>().ToList();
                    var e = entries.FirstOrDefault(x => (string)Prop(x, "Mark") == mark);
                    if (e == null) throw new AssertFail("brak wiersza " + mark);
                    return Num("C", Convert.ToDouble(Prop(e, "C"), Inv), 0) + "; " + Eq("ColC", Prop(e, "ColC"), "") + "; "
                         + Num("A", Convert.ToDouble(Prop(e, "A"), Inv), 300) + "; " + Num("XData C", BarNum(db, _c6, "LengthC"), 200);
                });
            });
        }

        [CommandMethod("RCSMOKE_W4_CEDIT", CommandFlags.Modal)]
        public void W4CEdit()
        {
            const string step = "CEDIT";
            Run(step, db =>
            {
                Check(step, "C2: BarEditApply A 300 → 400 (stary pierścień)", () => { EditApply(db, _c2, b => SetProp(b, "LengthA", 400.0)); return "OK"; });
                Check(step, "C3: BarEditApply B 400 → 350 (kapelusz obrócony 30°)", () => { EditApply(db, _c3, b => SetProp(b, "LengthB", 350.0)); return "OK"; });
            });
        }

        [CommandMethod("RCSMOKE_W4_CEDV", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4CEdV()
        {
            const string step = "CEDV";
            Run(step, db =>
            {
                Check(step, "C2: zapis utrwala „75” (migracja), LegacyRing44 false, okręgi 200 ± 6 wokół (150,43150)", () =>
                    Eq("rekord [3]", RawField(db, _c2, 3), "75") + "; " + Eq("LegacyRing44", Prop(ReadBar(db, _c2), "LegacyRing44"), false)
                    + "; " + Num("A", BarNum(db, _c2, "LengthA"), 400) + "; " + RingCheck(db, _c2, new Point3d(150, 43150, 0), 400, 12));
                Check(step, "C3: RC_EDIT_BAR → kapelusz 30° (rama zachowana), B 350, znacznik", () =>
                    Num("B", BarNum(db, _c3, "LengthB"), 350) + "; "
                    + Pts("obrys", PolyVerts(db, _c3), ExpectedOutline(ReadBar(db, _c3), new Point3d(0, 46000, 0), Dir30, false))
                    + "; " + HatRecordCheck(db, _c3));
            });
        }

        [CommandMethod("RCSMOKE_W4_CU", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4CU()
        {
            const string step = "CU";
            Run(step, db =>
            {
                Check(step, "_.UNDO 1 (cofa CEDIT): C2 rekord „44” (14 pól), A 300, okręgi wokół (150,43150), odczyt 75 + LegacyRing44", () =>
                    Eq("rekord [3]", RawField(db, _c2, 3), "44") + "; " + Int("rekord pól", RawCount(db, _c2), 14)
                    + "; " + Num("A", BarNum(db, _c2, "LengthA"), 300) + "; " + Eq("LegacyRing44", Prop(ReadBar(db, _c2), "LegacyRing44"), true)
                    + "; " + RingCheck(db, _c2, new Point3d(150, 43150, 0), 300, 12));
            });
        }

        // ================================================================
        // Runda 1, uwaga 1 — długość rozkładu Manual wg geometrii sprzed W4 (polilinia i pręt-blok)
        // ================================================================

        [CommandMethod("RCSMOKE_W4_D", CommandFlags.Modal)]
        public void W4D()
        {
            const string step = "D";
            Run(step, db =>
            {
                ObjectId leg = ObjectId.Null, cur = ObjectId.Null, blk = ObjectId.Null;
                Check(step, "15 H12 {500,200,300}: obrys sprzed W4 (0,60000), bieżąca polilinia obrócona 30° (0,62000), pręt-blok (3000,62000)", () =>
                {
                    leg = PlaceLegacyOutlineBar(db, new Def { Code = "15", A = 500, B = 200, C = 300, At = new Point3d(0, 60000, 0) }, false);
                    cur = PlaceBar(db, "Polyline", new Def { Code = "15", A = 500, B = 200, C = 300, At = new Point3d(0, 62000, 0) });
                    using ((IDisposable)Call(WatcherType, "Suppress"))
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        Call(WatcherType, "IgnoreThisCommand", cur);
                        ((Entity)tr.GetObject(cur, OpenMode.ForWrite)).TransformBy(Matrix3d.Rotation(Math.PI / 6, Vector3d.ZAxis, new Point3d(0, 62000, 0)));
                        tr.Commit();
                    }
                    blk = PlaceBar(db, "Block", new Def { Code = "15", A = 500, B = 200, C = 300, At = new Point3d(3000, 62000, 0) });
                    return "OK";
                });
                foreach (bool left in new[] { true, false })
                {
                    string side = left ? "lewa" : "prawa";
                    Check(step, $"15 ramię 1 ({side} strona): obrys sprzed W4 → wzorzec; obrys W4d (polilinia i blok) → odcinek W4d (runda 1, uwaga 1)", () =>
                    {
                        int sl = SegIdxOf(db, leg, "15", 1, left, legacy: true);
                        double wz = SegLength(Outline(db, leg), sl);
                        double gotL = ManualAt(db, leg, sl, out _);
                        double gotP = ManualLength(db, cur, "15", 1, left, out double measP);
                        double gotB = ManualLength(db, blk, "15", 1, left, out double measB);
                        if (Math.Abs(measP - wz) < 1) throw new AssertFail("zmierzony odcinek W4d = wzorzec (test bez znaczenia)");
                        // W4e: obrys bieżący ma wymiary zewnętrzne — długość = odcinek geometrii W4d (D29, 0-POPRAWKI p.1)
                        double w4d = RefSegLength(db, cur, "15", 1, left, "W4d");
                        return Num("stary obrys", gotL, wz, 1e-6) + "; " + Num("polilinia → odcinek W4d", gotP, w4d, 1e-9) + "; "
                             + Num("blok → odcinek W4d", gotB, w4d, 1e-9) + "; wzorzec " + F(wz) + "; zmierzone " + F(measP) + " / " + F(measB);
                    });
                }
                Check(step, "kontrola 11 {500,300} i 21 {500,300,500} (kąty 90°): długość Manual = wzorzec (= zmierzona przed W4e)", () =>
                {
                    var b11 = PlaceBar(db, "Polyline", new Def { Code = "11", A = 500, B = 300, At = new Point3d(0, 66000, 0) });
                    var b21 = PlaceBar(db, "Polyline", new Def { Code = "21", D = 10, A = 500, B = 300, C = 500, At = new Point3d(3000, 66000, 0) });
                    double g11 = ManualLength(db, b11, "11", 1, true, out double m11);
                    double g21 = ManualLength(db, b21, "21", 1, true, out double m21);
                    double r11 = RefSegLength(db, b11, "11", 1, true, "Legacy"), r21 = RefSegLength(db, b21, "21", 1, true, "Legacy");
                    return Num("11", g11, r11, 1e-9) + "; " + Num("21", g21, r21, 1e-9) + "; zmierzone (W4e) " + F(m11) + " / " + F(m21);
                });
            });
        }

        // ================================================================
        // W4d — geometria wg rysunków BS 8666:2020 (Bs2020Sharp)
        // ================================================================

        /// <summary>Pręt W4d: kod, wartości pól A–E, położenie, oczekiwana rama (axis0, kierunek, odbicie) po kolejnych krokach.</summary>
        private sealed class W4dBar
        {
            public string Code;
            public double[] Pv;
            public ObjectId Id;
            public Point3d Axis0;
            public Vector3d Dir = new Vector3d(1, 0, 0);
            public bool Mirrored;
            public bool Frozen;
        }

        private static readonly List<W4dBar> _w4d = new List<W4dBar>();
        private static readonly List<(string Code, ObjectId Id)> _w4dBlocks = new List<(string, ObjectId)>();
        private static readonly Point3d W4dRotCenter = new Point3d(1000, 70000, 0);

        private static readonly (string Code, double[] Pv)[] W4dShapes =
        {
            ("12", new double[] { 500, 300, 100, 0, 0 }),
            ("14", new double[] { 500, 300, 300, 400, 0 }),
            ("15", new double[] { 500, 300, 300, 400, 0 }),
            ("22", new double[] { 400, 600, 150, 300, 0 }),
            ("24", new double[] { 500, 400, 300, 240, 320 }),
            ("33", new double[] { 900, 200, 300, 0, 0 }),
            ("46", new double[] { 200, 300, 400, 200, 150 }),   // {A,B,C,E,D}
            ("51", new double[] { 400, 300, 0, 0, 0 }),
        };

        /// <summary>ZAMROŻONY obrys lokalny 15 H12 {500,200,300} z etapu W4b–W4c (tests/FrozenOutlinesW4c.cs).</summary>
        private static readonly (double X, double Y)[] FrozenW4c15 =
        {
            (0.000000000000, 6.000000000000),
            (442.034583265818, 5.999950827781),
            (448.947176285617, 7.370168414797),
            (454.786796564404, 11.272077938642),
            (458.688706088249, 17.111698217428),
            (460.058874503046, 24.000000000000),
            (458.688706088249, 30.888301782572),
            (454.735862261127, 36.779475323733),
            (354.336003075571, 137.178715550190),
            (362.821284449810, 145.663996924429),
            (463.323012241919, 45.161650173221),
            (469.775260478384, 35.480502970953),
            (472.058874503046, 24.000000000000),
            (469.775260478384, 12.519497029047),
            (463.272077938642, 2.786796564404),
            (453.539377473998, -3.716385975339),
            (442.083165740273, -5.999950827781),
            (0.000000000000, -6.000000000000),
        };

        /// <summary>ZAMROŻONY obrys lokalny 51 H12 {400,300,C 0} z etapu W4b–W4c (tests/FrozenOutlinesW4c.cs).</summary>
        private static readonly (double X, double Y)[] FrozenW4c51 =
        {
            (394.000000000000, 144.000000000000),
            (394.000105025233, 275.964499411769),
            (393.386664873203, 280.658742811845),
            (391.588457268120, 285.000000000000),
            (388.727922061358, 288.727922061358),
            (385.000000000000, 291.588457268120),
            (380.658742811845, 293.386664873203),
            (375.986302321181, 294.000015635554),
            (24.013697678819, 294.000015635554),
            (19.341257188155, 293.386664873203),
            (15.000000000000, 291.588457268120),
            (11.272077938642, 288.727922061358),
            (8.411542731880, 285.000000000000),
            (6.613335126797, 280.658742811845),
            (5.999969908804, 275.980997541080),
            (5.999969908804, 24.019002458920),
            (6.613335126797, 19.341257188155),
            (8.411542731880, 15.000000000000),
            (11.272077938642, 11.272077938642),
            (15.000000000000, 8.411542731880),
            (19.341257188155, 6.613335126797),
            (24.013697678819, 5.999984364446),
            (375.986302321181, 5.999984364446),
            (380.658742811845, 6.613335126797),
            (385.000000000000, 8.411542731880),
            (388.727922061358, 11.272077938642),
            (391.588457268120, 15.000000000000),
            (393.386664873203, 19.341257188155),
            (394.000030091196, 24.019002458920),
            (394.000030091196, 275.980997541080),
            (393.386664873203, 280.658742811845),
            (391.588457268120, 285.000000000000),
            (388.727922061358, 288.727922061358),
            (385.000000000000, 291.588457268120),
            (380.658742811845, 293.386664873203),
            (375.964499411769, 294.000105025233),
            (244.000000000000, 294.000000000000),
            (244.000000000000, 306.000000000000),
            (376.035500588231, 305.999894974767),
            (383.764571353076, 304.977774788672),
            (391.000000000000, 301.980762113533),
            (397.213203435596, 297.213203435596),
            (401.980762113533, 291.000000000000),
            (404.977774788672, 283.764571353076),
            (405.999969908804, 276.019002458920),
            (405.999969908804, 23.980997541080),
            (404.977774788672, 16.235428646924),
            (401.980762113533, 9.000000000000),
            (397.213203435596, 2.786796564404),
            (391.000000000000, -1.980762113533),
            (383.764571353076, -4.977774788672),
            (376.013697678819, -5.999984364446),
            (23.986302321181, -5.999984364446),
            (16.235428646924, -4.977774788672),
            (9.000000000000, -1.980762113533),
            (2.786796564404, 2.786796564404),
            (-1.980762113533, 9.000000000000),
            (-4.977774788672, 16.235428646924),
            (-5.999969908804, 23.980997541080),
            (-5.999969908804, 276.019002458920),
            (-4.977774788672, 283.764571353076),
            (-1.980762113533, 291.000000000000),
            (2.786796564404, 297.213203435596),
            (9.000000000000, 301.980762113533),
            (16.235428646924, 304.977774788672),
            (23.986302321181, 305.999984364446),
            (376.013697678819, 305.999984364446),
            (383.764571353076, 304.977774788672),
            (391.000000000000, 301.980762113533),
            (397.213203435596, 297.213203435596),
            (401.980762113533, 291.000000000000),
            (404.977774788672, 283.764571353076),
            (405.999894974767, 276.035500588231),
            (406.000000000000, 144.000000000000),
        };

        [CommandMethod("RCSMOKE_W4_E", CommandFlags.Modal)]
        public void W4E()
        {
            const string step = "E";
            Run(step, db =>
            {
                _w4d.Clear();
                _w4dBlocks.Clear();
                Check(step, "BarGeometryOptions.Current = W4e (Bs2020Sharp + ExternalDims)", () =>
                {
                    var optType = Plugin.GetType("BricsCadRc.Core.BarGeometryOptions", true);
                    var cur = optType.GetProperty("Current").GetValue(null);
                    return Eq("Bs2020Sharp", Prop(cur, "Bs2020Sharp"), true) + "; " + Eq("ExternalDims", Prop(cur, "ExternalDims"), true)
                         + "; W4b=" + Show(optType.GetProperty("W4b").GetValue(null));
                });
                for (int i = 0; i < W4dShapes.Length; i++)
                {
                    var (code, pv) = W4dShapes[i];
                    var at = new Point3d(0, 70000 + 2000 * i, 0);
                    Check(step, $"{code} H12 {{{string.Join(",", pv.Select(v => F(v)))}}} w {P(at)}: PlaceBar → obrys W4d", () =>
                    {
                        var id = PlaceShapeBar(db, "Polyline", code, pv, at);
                        // PlaceBar: punkt wstawienia = początek układu lokalnego → axis[0] = at + lPts[0]
                        var l0 = ((double X, double Y))((IList)CallD("BricsCadRc.Core.BarGeometryBuilder", "GetLocalPoints", code, pv, 12.0))[0];
                        var axis0 = new Point3d(at.X + l0.X, at.Y + l0.Y, 0);
                        _w4d.Add(new W4dBar { Code = code, Pv = pv, Id = id, Axis0 = axis0 });
                        var v = PolyVerts(db, id);
                        int n = ((IList)Call(FrameRulesType, "LocalOutline", code, pv, 12.0)).Count;
                        return Int("wierzchołki", v.Count, n) + "; " + Pt("axis0", Axis(v)[0], axis0, 1e-6) + "; "
                             + Pts("obrys", v, ExpectedOutline(ReadBar(db, id), axis0, new Vector3d(1, 0, 0), false));
                    });
                }
                foreach (var (code, leg) in new[] { ("14", 1), ("15", 1), ("22", 1), ("46", 1), ("51", 2), ("33", 1) })
                {
                    Check(step, $"{code} ramię {leg}: rozkład Manual = odcinek W4d (zmieniony układ ramion — runda 1, uwaga 1; po W4e bez zmian)", () =>
                    {
                        var b = _w4d.First(x => x.Code == code);
                        var lo = ((IList)Call(FrameRulesType, "LegacyLocalOutline", code, b.Pv, 12.0)).Cast<(double X, double Y)>()
                                 .Select(p => new Point3d(p.X, p.Y, 0)).ToList();
                        double wz = SegLength(lo, SegIdxOf(db, b.Id, code, leg, true, legacy: true));
                        double got = ManualLength(db, b.Id, code, leg, true, out double meas);
                        double w4d = RefSegLength(db, b.Id, code, leg, true, "W4d");
                        return Num("Manual", got, w4d, 1e-9) + " (zmierzone na obrysie W4e: " + F(meas) + "; ramię sprzed W4 o tym numerze: " + F(wz) + " — nie używane)";
                    });
                }
                foreach (var (code, frozen, at) in new[] { ("15", FrozenW4c15, new Point3d(0, 88000, 0)), ("51", FrozenW4c51, new Point3d(0, 91000, 0)) })
                {
                    Check(step, $"{code}: zamrożony obrys etapu W4b–W4c w {P(at)}, obrót 30° + MIRROR x = 5000 (w kodzie)", () =>
                    {
                        var pv = code == "15" ? new double[] { 500, 200, 300, 0, 0 } : new double[] { 400, 300, 0, 0, 0 };
                        var id = PlaceShapeBar(db, "Polyline", code, pv, at);
                        ReplaceOutline(db, id, frozen.Select(p => ((double, double))p).Cast<object>().ToList(), at, true);
                        StripTail(db, id);   // rysunek z etapu W4b–W4c: bez znacznika W4d
                        var axis0 = MirX(at, 5000);
                        _w4d.Add(new W4dBar { Code = code, Pv = pv, Id = id, Axis0 = axis0, Dir = Dir150, Mirrored = true, Frozen = true });
                        var v = PolyVerts(db, id);
                        double dev = MaxDev(v, ExpectedOutline(ReadBar(db, id), axis0, Dir150, true));
                        if (dev < 1.0) throw new AssertFail("zamrożony obrys W4c = geometria W4d (test bez znaczenia)");
                        return Pt("axis0", Axis(v)[0], axis0, 1e-6) + "; różnica od W4d " + F(dev) + " mm";
                    });
                }
                for (int i = 0; i < 5; i++)
                {
                    var code = new[] { "15", "22", "33", "51", "46" }[i];
                    // runda 1: pręty-bloki w oknie MOVE skryptu (x = 4000) — przechodzą MOVE / ROTATE / MIRROR jak polilinie
                    Check(step, $"pręt-blok {code} (4000, {70000 + 3000 * i})", () =>
                    {
                        var pv = W4dShapes.First(s => s.Code == code).Pv;
                        var id = PlaceShapeBar(db, "Block", code, pv, new Point3d(4000, 70000 + 3000 * i, 0));
                        _w4dBlocks.Add((code, id));
                        return Int("wierzchołki obrysu", Outline(db, id).Count, ((IList)Call(FrameRulesType, "LocalOutline", code, pv, 12.0)).Count);
                    });
                }
            });
        }

        private static string W4dFrameCheck(Database db)
        {
            var sb = new List<string>();
            foreach (var b in _w4d)
            {
                var v = PolyVerts(db, b.Id);
                sb.Add(b.Code + (b.Frozen ? "z" : "") + ": " + Pts(b.Code + " obrys", v, ExpectedOutline(ReadBar(db, b.Id), b.Axis0, b.Dir, b.Mirrored)));
            }
            return string.Join("; ", sb);
        }

        [CommandMethod("RCSMOKE_W4_EV", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4EV()
        {
            const string step = "EV";
            Run(step, db =>
            {
                foreach (var b in _w4d) b.Axis0 = b.Axis0 + new Vector3d(1000, 0, 0);
                Check(step, "_.MOVE +1000 → obrysy W4d w ramie pręta (nowe: 0°; zamrożone W4c: 150° + odbicie → geometria W4d)", () => W4dFrameCheck(db));
            });
        }

        [CommandMethod("RCSMOKE_W4_ER", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4ER()
        {
            const string step = "ER";
            Run(step, db =>
            {
                foreach (var b in _w4d)
                {
                    b.Axis0 = Rot(b.Axis0, W4dRotCenter, 30);
                    b.Dir = b.Dir.RotateBy(Math.PI / 6, Vector3d.ZAxis);
                }
                Check(step, "_.ROTATE 30° → rama obrócona (nowe 30°, zamrożone 180° + odbicie)", () => W4dFrameCheck(db));
            });
        }

        [CommandMethod("RCSMOKE_W4_EM", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4EM()
        {
            const string step = "EM";
            Run(step, db =>
            {
                foreach (var b in _w4d)
                {
                    b.Axis0 = MirX(b.Axis0, 5000);
                    b.Dir = new Vector3d(-b.Dir.X, b.Dir.Y, 0);
                    b.Mirrored = !b.Mirrored;
                }
                Check(step, "_.MIRROR x = 5000 (usuń źródło) → rama odbita (nowe 150° + odbicie, zamrożone 0° bez odbicia)", () => W4dFrameCheck(db));
            });
        }

        [CommandMethod("RCSMOKE_W4_EEDIT", CommandFlags.Modal)]
        public void W4EEdit()
        {
            const string step = "EEDIT";
            Run(step, db =>
            {
                foreach (var b in _w4d)
                    Check(step, $"{b.Code}{(b.Frozen ? " (zamrożony)" : "")}: BarEditApply A + 50", () =>
                    {
                        double a = BarNum(db, b.Id, "LengthA");
                        EditApply(db, b.Id, x => SetProp(x, "LengthA", a + 50));
                        return "A " + F(a) + " → " + F(a + 50);
                    });
                foreach (var (code, id) in _w4dBlocks)
                    Check(step, $"pręt-blok {code}: BarEditApply A + 50", () =>
                    {
                        double a = BarNum(db, id, "LengthA");
                        EditApply(db, id, x => SetProp(x, "LengthA", a + 50));
                        return "A " + F(a) + " → " + F(a + 50);
                    });
            });
        }

        [CommandMethod("RCSMOKE_W4_EED", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4EEd()
        {
            const string step = "EED";
            Run(step, db =>
            {
                Check(step, "RC_EDIT_BAR → geometria W4d z nowym A w ramie sprzed edycji (axis0 i rama zachowane)", () =>
                {
                    var bad = _w4d.Where(b => Math.Abs(BarNum(db, b.Id, "LengthA") - (b.Pv[0] + 50)) > 1e-6).Select(b => b.Code).ToList();
                    if (bad.Count > 0) throw new AssertFail("A nie zmienione: " + string.Join(",", bad));
                    return W4dFrameCheck(db);
                });
                Check(step, "pręty-bloki 15 / 22 / 33 / 51 / 46 (po MOVE, ROTATE, MIRROR): A + 50, obrys = LocalOutline (liczba wierzchołków), TL wg formuły", () =>
                {
                    var r = new List<string>();
                    foreach (var (code, id) in _w4dBlocks)
                    {
                        var bar = ReadBar(db, id);
                        var pv = (double[])Prop(bar, "ParamValues");
                        r.Add(code + ": " + Num("A", pv[0], W4dShapes.First(s => s.Code == code).Pv[0] + 50)
                              + " " + Int("wierzch.", Outline(db, id).Count, ((IList)Call(FrameRulesType, "LocalOutline", code, pv, 12.0)).Count)
                              + " " + Num("TL", BarNum(db, id, "TotalLength"), TotalLength(bar)));
                    }
                    return string.Join("; ", r);
                });
            });
        }

        /// <summary>Pręt dowolnego kształtu z polami A–E (PlaceBar, numer NextAuto, TotalLength wg formuły).</summary>
        private static ObjectId PlaceShapeBar(Database db, string fmt, string code, double[] pv, Point3d at)
        {
            var d = new Def { Code = code, A = pv[0], B = pv[1], C = pv[2], At = at };
            d.Nr = NextAuto(db);
            var bar = NewBar(d);
            SetProp(bar, "LengthD", pv[3]);
            SetProp(bar, "LengthE", pv[4]);
            SetProp(bar, "TotalLength", TotalLength(bar));
            SetFormat(fmt);
            try { return (ObjectId)Call(SingleBarType, "PlaceBar", db, bar, at); }
            finally { SetFormat("Polyline"); }
        }

        /// <summary>Indeks odcinka obrysu ramienia k (lewa / prawa strona) wg etykiet wariantu (Current albo Legacy).</summary>
        private static int SegIdxOf(Database db, ObjectId id, string code, int legIndex, bool left, bool legacy)
        {
            var bar = ReadBar(db, id);
            var optType = Plugin.GetType("BricsCadRc.Core.BarGeometryOptions", true);
            var opt = optType.GetProperty(legacy ? "Legacy" : "Current").GetValue(null);
            var labels = ((IEnumerable)Call("BricsCadRc.Core.BarGeometryBuilder", "GetSegmentLabels", code,
                (double[])Prop(bar, "ParamValues"), (double)(int)Prop(bar, "Diameter"), opt)).Cast<object>().ToList();
            int j = labels.FindIndex(l => (int)Prop(l, "Kind") == 0 && (int)Prop(l, "Index") == legIndex);
            if (j < 0) throw new AssertFail("brak ramienia " + legIndex);
            int n = labels.Count + 1;
            return left ? j : 2 * n - 2 - j;
        }

        private static double SegLength(List<Point3d> v, int s) => v[s].DistanceTo(v[s + 1]);

        /// <summary>
        /// W4e: długość odcinka ramienia legIndex (strona left) na obrysie LOKALNYM wariantu geometrii optName
        /// („W4d” — umowa osi, punkt odniesienia rozkładu Manual; „Legacy” — wzorzec) dla danych pręta. Wariant nieobecny
        /// we wtyczce (przed W4e) → „Current”.
        /// </summary>
        private static double RefSegLength(Database db, ObjectId id, string code, int legIndex, bool left, string optName)
        {
            var bar = ReadBar(db, id);
            var pv = (double[])Prop(bar, "ParamValues");
            double d = (int)Prop(bar, "Diameter");
            var optType = Plugin.GetType("BricsCadRc.Core.BarGeometryOptions", true);
            var opt = (optType.GetProperty(optName) ?? optType.GetProperty("Current")).GetValue(null);
            // W4h: punkt odniesienia Manual = bieżąca geometria w umowie osi (ta sama reguła kąta co obrys) —
            // Current.WithoutExternalDims(); wtyczka bez tej metody → właściwość W4d
            var axisOf = optType.GetMethod("WithoutExternalDims");
            if (optName == "W4d" && axisOf != null) opt = axisOf.Invoke(optType.GetProperty("Current").GetValue(null), null);
            var labels = ((IEnumerable)Call("BricsCadRc.Core.BarGeometryBuilder", "GetSegmentLabels", code, pv, d, opt)).Cast<object>().ToList();
            int j = labels.FindIndex(l => (int)Prop(l, "Kind") == 0 && (int)Prop(l, "Index") == legIndex);
            if (j < 0) throw new AssertFail("brak ramienia " + legIndex + " w wariancie " + optName);
            int n = labels.Count + 1;
            int s = left ? j : 2 * n - 2 - j;
            var lo = ((IList)Call(FrameRulesType, "LocalOutline", code, pv, d, opt)).Cast<(double X, double Y)>()
                     .Select(p => new Point3d(p.X, p.Y, 0)).ToList();
            return SegLength(lo, s);
        }

        /// <summary>
        /// Jak GetViewLengthManual po kliknięciu odcinka: zmierzona długość odcinka bieżącego obrysu (BarEntity.OutlineVertices)
        /// i wynik BarFrameRules.LegacySegmentLength (różnica &lt; 1e-6 → zmierzona).
        /// </summary>
        private static double ManualLength(Database db, ObjectId id, string code, int legIndex, bool left, out double measured)
            => ManualAt(db, id, SegIdxOf(db, id, code, legIndex, left, legacy: false), out measured);

        /// <summary>
        /// Długość rozkładu Manual dla odcinka s obrysu (BarEntity.OutlineVertices) — ten sam kod co RC_DISTRIBUTION /
        /// RC_EDIT_DISTRIBUTION po kliknięciu odcinka (BarCommands.ManualSegmentLength); measured = długość zmierzona.
        /// </summary>
        private static double ManualAt(Database db, ObjectId id, int s, out double measured)
        {
            var v = Outline(db, id);
            measured = SegLength(v, s);
            return (double)Call("BricsCadRc.Commands.BarCommands", "ManualSegmentLength", v, s, ReadBar(db, id));
        }

        // ================================================================
        // Runda 1, uwaga 1 — rozkład Manual po ROTATE i MIRROR (tworzenie i edycja), polilinia i pręt-blok
        // ================================================================

        [CommandMethod("RCSMOKE_W4_F", CommandFlags.Modal)]
        public void W4F()
        {
            const string step = "F";
            Run(step, db =>
            {
                int row = 0;
                foreach (var (code, k1, k2) in new[] { ("33", 1, 0), ("15", 0, 1), ("46", 2, 1) })
                {
                    var ids = new List<(string Kind, ObjectId Id)>();
                    var pl = _w4d.FirstOrDefault(b => b.Code == code && !b.Frozen);
                    if (pl != null) ids.Add(("polilinia", pl.Id));
                    foreach (var bk in _w4dBlocks.Where(b => b.Code == code)) ids.Add(("blok", bk.Id));
                    foreach (var (kind, id) in ids)
                    {
                        double y0 = 100000 + 2000 * row++;
                        Check(step, $"{code} {kind} (po ROTATE + MIRROR): RC_DISTRIBUTION Manual ramię {k1} → RC_EDIT_DISTRIBUTION ramię {k2} = odcinki W4d", () =>
                        {
                            int s1 = SegIdxOf(db, id, code, k1, true, legacy: false);
                            int s2 = SegIdxOf(db, id, code, k2, false, legacy: false);
                            double len1 = ManualAt(db, id, s1, out double m1);
                            double len2 = ManualAt(db, id, s2, out double m2);
                            var bar = ReadBar(db, id);
                            var lo = ((IList)Call(FrameRulesType, "LegacyLocalOutline", code, (double[])Prop(bar, "ParamValues"), 12.0))
                                     .Cast<(double X, double Y)>().Select(p => new Point3d(p.X, p.Y, 0)).ToList();
                            double wz1 = SegLength(lo, SegIdxOf(db, id, code, k1, true, legacy: true));
                            // tworzenie: zakres rozkładu = długość z kliknięcia (jak RC_DISTRIBUTION)
                            var (dist, _) = Distribute(db, id, 30000, y0, 30000 + len1, y0 + 400);
                            double d1 = DistLength(db, dist);
                            // edycja: RC_EDIT_DISTRIBUTION → Manual, inne ramię (RebuildWithNewLayout jak EditCommands)
                            object xd;
                            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                                xd = CallD(BarBlockType, "ReadXData", tr.GetObject(dist, OpenMode.ForRead));
                            CallD(BarBlockType, "RebuildWithNewLayout", db, dist, (int)Prop(xd, "Count"), (double)Prop(xd, "Spacing"),
                                  (double)Prop(xd, "Cover"), null, (double?)len2, "Manual", s2);
                            double d2 = DistLength(db, dist);
                            // W4e: obrys z wymiarami zewnętrznymi — długość = odcinek geometrii W4d (nie zmierzony)
                            double r1 = RefSegLength(db, id, code, k1, true, "W4d"), r2 = RefSegLength(db, id, code, k2, false, "W4d");
                            return Num("ramię " + k1 + " = W4d", len1, r1, 1e-9) + " (sprzed W4: " + F(wz1) + "; zmierzone " + F(m1) + "); "
                                 + Num("rozkład utworzony", d1, len1, 1e-6) + "; "
                                 + Num("ramię " + k2 + " = W4d", len2, r2, 1e-9) + " (zmierzone " + F(m2) + "); " + Num("rozkład po edycji", d2, len2, 1e-6);
                        });
                    }
                }
            });
        }

        // ================================================================
        // Runda 1, uwaga 2 — bezpiecznik RC_BBS dla prostego 00 (W4g)
        // ================================================================

        [CommandMethod("RCSMOKE_W4_B00", CommandFlags.Modal)]
        public void W4B00()
        {
            const string step = "B00";
            Run(step, db =>
            {
                var marks = new Dictionary<string, string>();
                foreach (var (key, a, tl, ovr, y) in new[] { ("3010", 3010.0, 3010.0, false, 32000.0), ("3000", 3000.0, 3000.0, false, 34000.0),
                                                             ("nadpisana", 3010.0, 3010.0, true, 36000.0) })
                    Check(step, $"00 H12 A {F(a)}, TL w rysunku {F(tl)}{(ovr ? " (nadpisana)" : "")} + rozkład + opis", () =>
                    {
                        var id = PlaceBar(db, "Polyline", new Def { Code = "00", A = a, At = new Point3d(0, y, 0) });
                        WriteBar(db, id, b => { SetProp(b, "TotalLength", tl); SetProp(b, "LengthOverridden", ovr); });
                        marks[key] = (string)Prop(ReadBar(db, id), "Mark");
                        Distribute(db, id, 40, y + 1040, 3040, y + 1600);
                        return marks[key];
                    });
                Check(step, "RC_BBS (ReadBars): 3010 → ostrzeżenie „wg BS 8666:2020 = 3025 mm, w rysunku 3010”; 3000 i nadpisana — bez", () =>
                {
                    var res = CallD("BricsCadRc.Core.BbsDrawingReader", "ReadBars", db);
                    var warns = ((IEnumerable<string>)Prop(res, "Warnings")).ToList();
                    string W(string key)
                    {
                        int p = (int)Call(SingleBarType, "ExtractPosNr", marks[key]);
                        return warns.FirstOrDefault(w => w.Contains("Pozycja " + p.ToString("00", Inv) + " (kształt 00)"));
                    }
                    string w1 = W("3010");
                    if (w1 == null || !w1.Contains("= 3025 mm") || !w1.Contains("w rysunku 3010"))
                        throw new AssertFail("brak ostrzeżenia 00 3010/3025; ostrzeżenia: " + string.Join(" | ", warns));
                    if (W("3000") != null) throw new AssertFail("ostrzeżenie dla 3000: " + W("3000"));
                    if (W("nadpisana") != null) throw new AssertFail("ostrzeżenie dla nadpisanej: " + W("nadpisana"));
                    return w1;
                });
            });
        }

        // ================================================================
        // W4e — wymiary zewnętrzne BS 8666 (kl. 7.1), łuk osi r + d/2 (poprawka #13)
        // ================================================================

        /// <summary>Pręt W4e: obrys etapu W4d (zamrożony) obrócony + odbity, długości rozkładu Manual przed _.MOVE.</summary>
        private sealed class W4eBar
        {
            public string Code;
            public double[] Pv;
            public ObjectId Id;
            public Point3d Axis0;
            public Dictionary<string, double> Manual = new Dictionary<string, double>();
        }

        private static readonly List<W4eBar> _w4e = new List<W4eBar>();
        private static ObjectId _w4eUb, _w4eBlk, _w4hOld;
        private static Point3d _w4hAxis0;
        private static readonly double[] W4eUbPv = { 700, 140, 700, 0, 0 };

        private static (double W, double H) BboxWH(IEnumerable<Point3d> pts)
        {
            var l = pts.ToList();
            return (l.Max(p => p.X) - l.Min(p => p.X), l.Max(p => p.Y) - l.Min(p => p.Y));
        }

        /// <summary>Wymiary obrysu (bbox) = szerokość × wysokość (tolerancja 0,01 mm — wierzchołki przy łukach z uśrednionej normalnej).</summary>
        private static string BboxCheck(string what, IEnumerable<Point3d> pts, double w, double h)
        {
            var (bw, bh) = BboxWH(pts);
            return Num(what + " szerokość", bw, w, 0.01) + "; " + Num(what + " wysokość", bh, h, 0.01);
        }

        /// <summary>
        /// Długość rozkładu Manual (BarCommands.ManualSegmentLength — jak RC_DISTRIBUTION / RC_EDIT_DISTRIBUTION) dla KAŻDEGO
        /// odcinka osi (ramiona i łuki, obie strony obrysu) — klucz: etykieta odcinka + strona.
        /// </summary>
        private static Dictionary<string, double> ManualAll(Database db, ObjectId id, string code)
        {
            var bar = ReadBar(db, id);
            var pv = (double[])Prop(bar, "ParamValues");
            double d = (int)Prop(bar, "Diameter");
            var optType = Plugin.GetType("BricsCadRc.Core.BarGeometryOptions", true);
            var labels = ((IEnumerable)Call("BricsCadRc.Core.BarGeometryBuilder", "GetSegmentLabels", code, pv, d,
                          optType.GetProperty("Current").GetValue(null))).Cast<object>().ToList();
            int n = Outline(db, id).Count / 2;
            if (labels.Count + 1 != n) throw new AssertFail("liczba odcinków osi " + labels.Count + " ≠ obrys " + n);
            var r = new Dictionary<string, double>();
            for (int j = 0; j < labels.Count; j++)
                foreach (bool left in new[] { true, false })
                {
                    string key = Convert.ToString(labels[j], Inv) + (left ? " L" : " P");
                    if (r.ContainsKey(key)) continue;
                    r[key] = ManualAt(db, id, left ? j : 2 * n - 2 - j, out _);
                }
            return r;
        }

        [CommandMethod("RCSMOKE_W4_W", CommandFlags.Modal)]
        public void W4W()
        {
            const string step = "W";
            Run(step, db =>
            {
                _w4e.Clear();
                _w4eUb = _w4eBlk = ObjectId.Null;
                Check(step, "BarGeometryOptions.Current: ExternalDims (W4e)", () => Eq("ExternalDims", W4Compat.ExternalDims, true));
                Check(step, "W1: UB 225 z AutoRebar (CreateUBTemplate, H12 700 / 140 / 700) → obrys = B × A z XData (140 × 700)", () =>
                {
                    var res = Call("BricsCadRc.Core.AutoRebarEngine", "CreateUBTemplate", db,
                                   new Extents3d(new Point3d(20000, 140000, 0), new Point3d(23000, 143000, 0)), 0, 12, 700.0, 140.0, 700.0, "B1", 1, "21");
                    _w4eUb = (ObjectId)res.GetType().GetField("Item1").GetValue(res);
                    return BboxCheck("obrys", PolyVerts(db, _w4eUb), 140, 700) + "; " + Num("A", BarNum(db, _w4eUb, "LengthA"), 700)
                         + "; " + Num("B", BarNum(db, _w4eUb, "LengthB"), 140)
                         + "; " + Num("TL (AutoRebar: TL = A, cecha wzorca — D17)", BarNum(db, _w4eUb, "TotalLength"), 700);
                });
                Check(step, "W2: RC_SECTION — pręt wzorcowy UB w widoku (SourceShape, bez AutoRebarEngine.AxisParams) → obrys 140 × 700 jak przed W4e", () =>
                {
                    var t = Plugin.GetType("BricsCadRc.Core.AutoRebarEngine", true);
                    if (t.GetMethod("AxisParams", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic) != null)
                        throw new AssertFail("AutoRebarEngine.AxisParams nadal istnieje (podwójne odejmowanie)");
                    var m = t.GetMethod("SourceShape", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                    var cache = Activator.CreateInstance(m.GetParameters()[3].ParameterType);
                    object res;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        res = m.Invoke(null, new object[] { db, tr, _w4eUb.Handle.ToString(), cache });
                        tr.Commit();
                    }
                    if (res == null) throw new AssertFail("SourceShape: brak kształtu");
                    var pts = res.GetType().GetField("Item1").GetValue(res);
                    var outline = ((IList)Call(FrameRulesType, "LocalOutlineFrom", pts, 12.0)).Cast<(double X, double Y)>()
                                  .Select(p => new Point3d(p.X, p.Y, 0)).ToList();
                    return BboxCheck("przekrój", outline, 140, 700) + "; kod " + Show(res.GetType().GetField("Item2").GetValue(res));
                });
                foreach (var (code, pv, frozen, at) in new[]
                {
                    ("21", new double[] { 700, 140, 700, 0, 0 }, FrozenW4d_21_H12, new Point3d(0, 140000, 0)),
                    ("15", new double[] { 500, 300, 300, 400, 0 }, FrozenW4d_15_H12, new Point3d(0, 143000, 0)),
                    ("44", new double[] { 500, 400, 500, 400, 300 }, FrozenW4d_44_H12, new Point3d(0, 146000, 0)),
                })
                {
                    Check(step, $"W3: {code} z obrysem etapu W4d (zamrożony, znacznik 2021) w {P(at)}, obrót 30° + MIRROR x = 5000 (w kodzie); rozkład Manual przed W4e", () =>
                    {
                        var id = PlaceShapeBar(db, "Polyline", code, pv, at);
                        ReplaceOutline(db, id, frozen.Select(p => ((double, double))p).Cast<object>().ToList(), at, true);
                        var axis0 = MirX(at, 5000);
                        var b = new W4eBar { Code = code, Pv = pv, Id = id, Axis0 = axis0 };
                        _w4e.Add(b);
                        var v = PolyVerts(db, id);
                        double dev = MaxDev(v, ExpectedOutline(ReadBar(db, id), axis0, Dir150, true));
                        if (dev < 1.0) throw new AssertFail("zamrożony obrys W4d = geometria W4e (test bez znaczenia)");
                        b.Manual = ManualAll(db, id, code);
                        return Int("[15]", Std15(db, id), 2021) + "; " + Pt("axis0", Axis(v)[0], axis0, 1e-6) + "; różnica od W4e " + F(dev)
                             + " mm; odcinków Manual " + b.Manual.Count;
                    });
                }
                Check(step, "W5 (W4h): 15 H12 {500,300,300,D 0} z obrysem etapu W4e (kąt 45°) w (0,144500), obrót 30° + MIRROR — rama do _.MOVE", () =>
                {
                    var pv = new double[] { 500, 300, 300, 0, 0 };
                    var at = new Point3d(0, 144500, 0);
                    var id = PlaceShapeBar(db, "Polyline", "15", pv, at);
                    var optType = Plugin.GetType("BricsCadRc.Core.BarGeometryOptions", true);
                    var w4e = optType.GetProperty("W4e").GetValue(null);
                    var local = ((IList)Call(FrameRulesType, "LocalOutline", "15", pv, 12.0, w4e)).Cast<object>().ToList();
                    ReplaceOutline(db, id, local, at, true);
                    _w4hOld = id;
                    _w4hAxis0 = MirX(at, 5000);
                    double dev = MaxDev(PolyVerts(db, id), ExpectedOutline(ReadBar(db, id), _w4hAxis0, Dir150, true));
                    if (dev < 1.0) throw new AssertFail("obrys W4e (45°) = geometria W4h (test bez znaczenia)");
                    return "różnica od W4h " + F(dev) + " mm";
                });
                Check(step, "W6 (runda 1, uwaga 1): pierścień 75 H12 A 300 — polilinia i pręt-blok: rozkład Manual = cięciwa obrysu sprzed W4e (40,724)", () =>
                {
                    var r = new List<string>();
                    foreach (var (fmt, at) in new[] { ("Polyline", new Point3d(14000, 140000, 0)), ("Block", new Point3d(14000, 142000, 0)) })
                    {
                        var id = PlaceBar(db, fmt, new Def { Code = "75", A = 300, At = at });
                        double got = ManualAt(db, id, 0, out double meas);
                        r.Add(fmt + ": " + Num("Manual", got, 2 * 156 * Math.Sin(Math.PI / 24), 1e-6) + " (zmierzone " + F(meas) + ")");
                    }
                    return string.Join("; ", r);
                });
                Check(step, "W4: pręt-blok 21 H12 {700,140,700} (20000,146000) → obrys BTR 140 × 700", () =>
                {
                    _w4eBlk = PlaceShapeBar(db, "Block", "21", W4eUbPv, new Point3d(20000, 146000, 0));
                    return BboxCheck("blok", Outline(db, _w4eBlk), 140, 700) + "; " + Int("[15]", Std15(db, _w4eBlk), 2021);
                });
            });
        }

        [CommandMethod("RCSMOKE_W4_WV", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4WV()
        {
            const string step = "WV";
            Run(step, db =>
            {
                foreach (var b in _w4e)
                {
                    b.Axis0 = b.Axis0 + new Vector3d(1000, 0, 0);
                    Check(step, $"{b.Code}: _.MOVE +1000 → obrys W4e (wymiary zewnętrzne) w ramie pręta (150° + odbicie), axis0 zachowany, znacznik 2021", () =>
                        Pts(b.Code + " obrys", PolyVerts(db, b.Id), ExpectedOutline(ReadBar(db, b.Id), b.Axis0, Dir150, true))
                        + "; " + Int("[15]", Std15(db, b.Id), 2021));
                    Check(step, $"{b.Code}: rozkład Manual po W4e = przed W4e (każdy odcinek, obie strony)", () =>
                    {
                        var after = ManualAll(db, b.Id, b.Code);
                        var bad = new List<string>();
                        foreach (var kv in b.Manual)
                            if (!after.TryGetValue(kv.Key, out double a) || Math.Abs(a - kv.Value) > 1e-6)
                                bad.Add(kv.Key + ": " + F(kv.Value) + " → " + (after.ContainsKey(kv.Key) ? F(after[kv.Key]) : "brak"));
                        if (bad.Count > 0) throw new AssertFail(string.Join("; ", bad));
                        return "odcinków " + after.Count + " bez zmian";
                    });
                }
                Check(step, "W5 (W4h): 15 sprzed W4h po _.MOVE → kąt A = asin(B/A) = 36,87° w ramie pręta (150° + odbicie), axis0 zachowany", () =>
                {
                    var axis0 = _w4hAxis0 + new Vector3d(1000, 0, 0);
                    var v = PolyVerts(db, _w4hOld);
                    var ax = Axis(v);
                    // pierwszy odcinek osi = ramię A (od wolnego końca); kierunek lokalny = R(−150°)·M
                    var a = ax[1] - ax[0];
                    var u = Dir150.GetNormal();
                    double lx = a.X * u.X + a.Y * u.Y, ly = -(-a.X * u.Y + a.Y * u.X);
                    double deg = Math.Atan2(-ly, lx) * 180 / Math.PI;
                    return Pts("obrys", v, ExpectedOutline(ReadBar(db, _w4hOld), axis0, Dir150, true)) + "; "
                         + Num("kąt ramienia A [°]", deg, Math.Asin(0.6) * 180 / Math.PI, 1e-6);
                });
                Check(step, "21: obrys po _.MOVE = A × B z XData (bez obrotu: rzut na oś pręta)", () =>
                {
                    var b = _w4e.First(x => x.Code == "21");
                    // obrys w układzie pręta: obrót −150° (rama) i odbicie y → bbox = B × A
                    var v = PolyVerts(db, b.Id);
                    var u = Dir150.GetNormal();
                    var loc = v.Select(p => { var q = p - b.Axis0; return new Point3d(q.X * u.X + q.Y * u.Y, -(q.X * -u.Y + q.Y * u.X), 0); });
                    return BboxCheck("21 lokalnie", loc, 140, 700);
                });
            });
        }

        [CommandMethod("RCSMOKE_W4_WEDIT", CommandFlags.Modal)]
        public void W4WEdit()
        {
            const string step = "WEDIT";
            Run(step, db =>
                Check(step, "pręt-blok 21: BarEditApply A 700 → 750 (RC_EDIT_BAR)", () =>
                {
                    EditApply(db, _w4eBlk, x => SetProp(x, "LengthA", 750.0));
                    return "OK";
                }));
        }

        [CommandMethod("RCSMOKE_W4_WED", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4WEd()
        {
            const string step = "WED";
            Run(step, db =>
                Check(step, "pręt-blok 21 po RC_EDIT_BAR: obrys BTR 140 × 750 (wymiary zewnętrzne), A 750, TL wg formuły, znacznik 2021", () =>
                {
                    var bar = ReadBar(db, _w4eBlk);
                    return BboxCheck("blok", Outline(db, _w4eBlk), 140, 750) + "; " + Num("A", BarNum(db, _w4eBlk, "LengthA"), 750)
                         + "; " + Num("TL", BarNum(db, _w4eBlk, "TotalLength"), TotalLength(bar)) + "; " + Int("[15]", Std15(db, _w4eBlk), 2021);
                }));
        }

        /// <summary>ZAMROŻONY obrys lokalny W4d 21 H12 z etapu W4d (tests/FrozenOutlinesW4d.cs).</summary>
        private static readonly (double X, double Y)[] FrozenW4d_21_H12 =
        {
            (6.000000000000, 0.000000000000),
            (5.999995689222, -675.992807690343),
            (6.613335126797, -680.658742811845),
            (8.411542731880, -685.000000000000),
            (11.272077938642, -688.727922061358),
            (15.000000000000, -691.588457268120),
            (19.341257188155, -693.386664873203),
            (24.049958539228, -694.000207991575),
            (115.950041460772, -694.000207991575),
            (120.658742811845, -693.386664873203),
            (125.000000000000, -691.588457268120),
            (128.727922061358, -688.727922061358),
            (131.588457268120, -685.000000000000),
            (133.386664873203, -680.658742811845),
            (134.000004310778, -675.992807690343),
            (134.000000000000, 0.000000000000),
            (146.000000000000, 0.000000000000),
            (145.999995689222, -676.007192309657),
            (144.977774788672, -683.764571353076),
            (141.980762113533, -691.000000000000),
            (137.213203435596, -697.213203435596),
            (131.000000000000, -701.980762113533),
            (123.764571353076, -704.977774788672),
            (116.049958539228, -705.999792008425),
            (23.950041460772, -705.999792008425),
            (16.235428646924, -704.977774788672),
            (9.000000000000, -701.980762113533),
            (2.786796564404, -697.213203435596),
            (-1.980762113533, -691.000000000000),
            (-4.977774788672, -683.764571353076),
            (-5.999995689222, -676.007192309657),
            (-6.000000000000, 0.000000000000),
        };

        /// <summary>ZAMROŻONY obrys lokalny W4d 15 H12 z etapu W4d (tests/FrozenOutlinesW4d.cs).</summary>
        private static readonly (double X, double Y)[] FrozenW4d_15_H12 =
        {
            (3.600000000000, 304.800000000000),
            (397.198661492883, 9.601003588797),
            (398.803498123518, 8.526656688490),
            (400.512678589078, 7.631126547940),
            (402.307900211697, 6.923700635091),
            (404.168533057501, 6.412508391799),
            (406.073195514280, 6.103424224902),
            (408.002808827489, 5.999999342541),
            (700.000000000000, 6.000000000000),
            (700.000000000000, -6.000000000000),
            (407.997191172511, -5.999999342541),
            (404.788659190466, -5.827626291829),
            (401.614221762502, -5.312486013668),
            (398.513167019495, -4.460498941515),
            (395.521130981797, -3.281455753433),
            (392.672496872529, -1.788905519184),
            (390.001338507117, -0.001003588797),
            (-3.600000000000, 295.200000000000),
        };

        /// <summary>ZAMROŻONY obrys lokalny W4d 44 H12 z etapu W4d (tests/FrozenOutlinesW4d.cs).</summary>
        private static readonly (double X, double Y)[] FrozenW4d_44_H12 =
        {
            (0.000000000000, 6.000000000000),
            (476.010175353263, 5.999991371843),
            (483.764571353076, 4.977774788672),
            (491.000000000000, 1.980762113533),
            (497.213203435596, -2.786796564404),
            (501.980762113533, -9.000000000000),
            (504.977774788672, -16.235428646924),
            (505.999984364446, -23.986302321181),
            (505.999984364446, -375.986302321181),
            (506.613335126797, -380.658742811845),
            (508.411542731880, -385.000000000000),
            (511.272077938642, -388.727922061358),
            (515.000000000000, -391.588457268120),
            (519.341257188155, -393.386664873203),
            (524.010708311594, -394.000009555669),
            (975.989291688406, -394.000009555669),
            (980.658742811845, -393.386664873203),
            (985.000000000000, -391.588457268120),
            (988.727922061358, -388.727922061358),
            (991.588457268120, -385.000000000000),
            (993.386664873203, -380.658742811845),
            (994.000015635554, -375.986302321181),
            (994.000015635554, -23.986302321181),
            (995.022225211328, -16.235428646924),
            (998.019237886467, -9.000000000000),
            (1002.786796564404, -2.786796564404),
            (1009.000000000000, 1.980762113533),
            (1016.235428646924, 4.977774788672),
            (1023.982613544513, 5.999974809211),
            (1300.000000000000, 6.000000000000),
            (1300.000000000000, -6.000000000000),
            (1024.017386455487, -5.999974809211),
            (1019.341257188155, -6.613335126797),
            (1015.000000000000, -8.411542731880),
            (1011.272077938642, -11.272077938642),
            (1008.411542731880, -15.000000000000),
            (1006.613335126797, -19.341257188155),
            (1005.999984364446, -24.013697678819),
            (1005.999984364446, -376.013697678819),
            (1004.977774788672, -383.764571353076),
            (1001.980762113533, -391.000000000000),
            (997.213203435596, -397.213203435596),
            (991.000000000000, -401.980762113533),
            (983.764571353076, -404.977774788672),
            (976.010708311594, -405.999990444331),
            (523.989291688406, -405.999990444331),
            (516.235428646924, -404.977774788672),
            (509.000000000000, -401.980762113533),
            (502.786796564404, -397.213203435596),
            (498.019237886467, -391.000000000000),
            (495.022225211328, -383.764571353076),
            (494.000015635554, -376.013697678819),
            (494.000015635554, -24.013697678819),
            (493.386664873203, -19.341257188155),
            (491.588457268120, -15.000000000000),
            (488.727922061358, -11.272077938642),
            (485.000000000000, -8.411542731880),
            (480.658742811845, -6.613335126797),
            (475.989824646737, -5.999991371843),
            (0.000000000000, -6.000000000000),
        };

        // ================================================================
        // W4f — walidacja minimów BS 8666:2020 w RC_BBS (ostrzeżenia)
        // ================================================================

        [CommandMethod("RCSMOKE_W4_WF", CommandFlags.Modal)]
        public void W4WF()
        {
            const string step = "WF";
            Run(step, db =>
            {
                string m13 = "", m11 = "";
                Check(step, "13 H12 {500, 80, 300} (0,150000) i 11 H12 {100, 300} (0,153000) + rozkłady + opisy", () =>
                {
                    var b13 = PlaceBar(db, "Polyline", new Def { Code = "13", A = 500, B = 80, C = 300, At = new Point3d(0, 150000, 0) });
                    Distribute(db, b13, 40, 151040, 3040, 152000);
                    var b11 = PlaceBar(db, "Polyline", new Def { Code = "11", A = 100, B = 300, At = new Point3d(0, 153000, 0) });
                    Distribute(db, b11, 40, 154040, 3040, 155000);
                    m13 = (string)Prop(ReadBar(db, b13), "Mark");
                    m11 = (string)Prop(ReadBar(db, b11), "Mark");
                    return m13 + ", " + m11;
                });
                Check(step, "RC_BBS (ReadBars): ostrzeżenia „B < q (85)” (13) i „A < P (125)” (11), każde raz", () =>
                {
                    var res = CallD("BricsCadRc.Core.BbsDrawingReader", "ReadBars", db);
                    var warns = ((IEnumerable<string>)Prop(res, "Warnings")).ToList();
                    int p13 = (int)Call(SingleBarType, "ExtractPosNr", m13), p11 = (int)Call(SingleBarType, "ExtractPosNr", m11);
                    var w13 = warns.Where(w => w.StartsWith("Pozycja " + p13.ToString("00", Inv) + " (kształt 13): B < q (85)")).ToList();
                    var w11 = warns.Where(w => w.StartsWith("Pozycja " + p11.ToString("00", Inv) + " (kształt 11): A < P (125)")).ToList();
                    if (w13.Count != 1 || w11.Count != 1)
                        throw new AssertFail("13: " + w13.Count + ", 11: " + w11.Count + "; ostrzeżenia: " + string.Join(" | ", warns));
                    var rows = ((IEnumerable)Prop(res, "Rows")).Cast<object>().ToList();
                    return w13[0] + " | " + w11[0] + "; " + True(rows.Any(r => (int)Prop(r, "BarMark") == p13), "wiersz 13 w BBS (ostrzeżenie nie blokuje)");
                });
            });
        }

        // ================================================================
        // RC_REBUILD_BARS (W4e, P8 — opcjonalnie): przebudowa wszystkich prętów, jedna grupa UNDO
        // ================================================================

        private static ObjectId _wrOld, _wrCur, _wrBad, _wrLabel;
        private static List<Point3d> _wrOldV, _wrCurV, _wrBadV;
        private static readonly Point3d WrAt = new Point3d(0, 156000, 0);

        [CommandMethod("RCSMOKE_W4_WR", CommandFlags.Modal)]
        public void W4WR()
        {
            const string step = "WR";
            Run(step, db =>
            {
                Check(step, "21 H12 {700,140,700} z obrysem etapu W4d (obrót 30° + MIRROR, etykieta), 21 bieżący (0,159000), 21 z zepsutym obrysem (0,162000)", () =>
                {
                    var (bar, label) = PlaceBarWithLabel(db, "Polyline", new Def { Code = "21", A = 700, B = 140, C = 700, At = WrAt });
                    _wrOld = bar; _wrLabel = label;
                    ReplaceOutline(db, _wrOld, FrozenW4d_21_H12.Select(p => ((double, double))p).Cast<object>().ToList(), WrAt, true);
                    _wrOldV = PolyVerts(db, _wrOld);
                    _wrCur = PlaceBar(db, "Polyline", new Def { Code = "21", A = 700, B = 140, C = 700, At = new Point3d(0, 159000, 0) });
                    _wrCurV = PolyVerts(db, _wrCur);
                    _wrBad = PlaceBar(db, "Polyline", new Def { Code = "21", A = 700, B = 140, C = 700, At = new Point3d(0, 162000, 0) });
                    using ((IDisposable)Call(WatcherType, "Suppress"))
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        Call(WatcherType, "IgnoreThisCommand", _wrBad);
                        var pl = (Polyline)tr.GetObject(_wrBad, OpenMode.ForWrite);
                        var p = pl.GetPoint2dAt(10);
                        pl.SetPointAt(10, new Point2d(p.X + 50, p.Y + 50));   // obrys zmieniony ręcznie — żaden wariant
                        tr.Commit();
                    }
                    _wrBadV = PolyVerts(db, _wrBad);
                    return "OK";
                });
            });
        }

        /// <summary>Odległość grotu etykiety od obrysu pręta-polilinii.</summary>
        private static double TipDistance(Database db, ObjectId label, ObjectId bar)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ml = (MLeader)tr.GetObject(label, OpenMode.ForRead);
                var li = (int)ml.GetLeaderIndexes()[0];
                var tip = ml.GetFirstVertex((int)ml.GetLeaderLineIndexes(li)[0]);
                var pl = (Polyline)tr.GetObject(bar, OpenMode.ForRead);
                return pl.GetClosestPointTo(tip, false).DistanceTo(tip);
            }
        }

        [CommandMethod("RCSMOKE_W4_WRV", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4WRV()
        {
            const string step = "WRV";
            Run(step, db =>
            {
                var axis0 = MirX(WrAt, 5000);
                Check(step, "RC_REBUILD_BARS: obrys W4d → geometria bieżąca (wymiary zewnętrzne) w tej samej ramie, axis0 zachowany, [15] = 2021", () =>
                    Pts("obrys", PolyVerts(db, _wrOld), ExpectedOutline(ReadBar(db, _wrOld), axis0, Dir150, true)) + "; "
                    + Int("[15]", Std15(db, _wrOld), 2021) + "; " + Num("A", BarNum(db, _wrOld, "LengthA"), 700));
                Check(step, "grot etykiety na nowym obrysie (back-link)", () => Num("odległość grotu", TipDistance(db, _wrLabel, _wrOld), 0, 1e-6));
                Check(step, "pręt bieżący — bez zmian; zepsuty obrys — pominięty (bez zmian)", () =>
                    Num("bieżący max odchyłka", MaxDev(PolyVerts(db, _wrCur), _wrCurV), 0, 1e-9) + "; "
                    + Num("zepsuty max odchyłka", MaxDev(PolyVerts(db, _wrBad), _wrBadV), 0, 1e-9));
            });
        }

        [CommandMethod("RCSMOKE_W4_WRU", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4WRU()
        {
            const string step = "WRU";
            Run(step, db =>
                Check(step, "_.U → RC_REBUILD_BARS cofnięte w całości: obrys etapu W4d przywrócony", () =>
                    Num("max odchyłka od obrysu sprzed komendy", MaxDev(PolyVerts(db, _wrOld), _wrOldV), 0, 1e-6)
                    + "; " + Int("[15]", Std15(db, _wrOld), 2021)));
        }

        // ================================================================
        // W4h runda 2, uwaga 1 — STRETCH wolnego końca A (15 z kątem z samego B) przy stałym kącie
        // ================================================================

        private static ObjectId _wg;
        private static Point3d _wgAxis0, _wgEnd;

        [CommandMethod("RCSMOKE_W4_WG", CommandFlags.Modal)]
        public void W4WG()
        {
            const string step = "WG";
            Run(step, db =>
                Check(step, "15 H12 {500,300,300, D puste} w (0,148000): axis0 (0; 148298,8) — kąt 36,87° z B", () =>
                {
                    _wg = PlaceShapeBar(db, "Polyline", "15", new double[] { 500, 300, 300, 0, 0 }, new Point3d(0, 148000, 0));
                    var ax = Axis(PolyVerts(db, _wg));
                    _wgAxis0 = ax[0];
                    _wgEnd = ax[ax.Count - 1];
                    return Pt("axis0", _wgAxis0, new Point3d(0, 148298.8, 0), 0.5) + "; " + Num("D", BarNum(db, _wg, "LengthD"), 0);
                }));
        }

        [CommandMethod("RCSMOKE_W4_WGV", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4WGV()
        {
            const string step = "WGV";
            Run(step, db =>
                Check(step, "_.STRETCH końca A o 100 wzdłuż ramienia → A 600, D zapisane (400), kąt 36,87° bez zmian, koniec C w miejscu", () =>
                {
                    var v = PolyVerts(db, _wg);
                    var ax = Axis(v);
                    var a = ax[1] - ax[0];
                    double deg = Math.Atan2(-a.Y, a.X) * 180 / Math.PI;
                    var axis0 = _wgAxis0 + new Vector3d(-80, 60, 0);
                    return Num("A", BarNum(db, _wg, "LengthA"), 600) + "; " + Num("D", BarNum(db, _wg, "LengthD"), 400, 0.01) + "; "
                         + Num("kąt A [°]", deg, Math.Asin(0.6) * 180 / Math.PI, 1e-3) + "; "
                         + Pt("koniec C", ax[ax.Count - 1], _wgEnd, 0.05) + "; "
                         + Pts("obrys", v, ExpectedOutline(ReadBar(db, _wg), axis0, new Vector3d(1, 0, 0), false), 0.05);
                }));
        }

        // ================================================================
        // Pomocnicze W4
        // ================================================================

        /// <summary>
        /// Stary pierścień (rysunek sprzed W4c): pręt 75 H12 A=300 (dwa okręgi) z rekordem przepisanym na „44” bez znacznika
        /// (polilinia: 14 pól; pręt-blok: [14] = 2, bez [15]). Zapis surowy przy wyciszonym watcherze.
        /// </summary>
        private static ObjectId PlaceLegacyRing(Database db, Point3d at, string fmt, double garbageC = 0)
        {
            var id = PlaceBar(db, fmt, new Def { Code = "75", A = 300, At = at });
            using ((IDisposable)Call(WatcherType, "Suppress"))
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (fmt == "Polyline") Call(WatcherType, "IgnoreThisCommand", id);
                var e = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                var v = e.GetXDataForApplication(XApp).AsArray().ToList();
                v[3] = new TypedValue((int)DxfCode.ExtendedDataAsciiString, "44");
                if (garbageC != 0) v[6] = new TypedValue((int)DxfCode.ExtendedDataReal, garbageC);   // stare pole C (bez znaczenia dla 75)
                int keep = fmt == "Block" ? 15 : 14;
                if (v.Count > keep) v = v.Take(keep).ToList();
                e.XData = new ResultBuffer(v.ToArray());
                tr.Commit();
            }
            return id;
        }

        private static ObjectId PlaceHat(Database db, Point3d at)
        {
            var d = new Def { Code = "44", A = HatPv[0], B = HatPv[1], C = HatPv[2], At = at };
            if (d.Nr == 0) d.Nr = NextAuto(db);
            var bar = NewBar(d);
            SetProp(bar, "LengthD", HatPv[3]);
            SetProp(bar, "LengthE", HatPv[4]);
            SetProp(bar, "TotalLength", TotalLength(bar));
            SetFormat("Polyline");
            return (ObjectId)Call(SingleBarType, "PlaceBar", db, bar, at);
        }

        /// <summary>Pręt-polilinia o danym Mark, którego axis[0] leży w odległości ≤ 50 mm od p.</summary>
        private static ObjectId FindBarAt(Database db, string mark, Point3d p)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl)) continue;
                    var b = Call(SingleBarType, "ReadBarXData", pl);
                    if (b == null || (string)Prop(b, "Mark") != mark) continue;
                    var v = new List<Point3d>();
                    for (int i = 0; i < pl.NumberOfVertices; i++) v.Add(pl.GetPoint3dAt(i));
                    if (v.Count >= 4 && Axis(v)[0].DistanceTo(p) <= 50) return id;
                }
            }
            throw new AssertFail("brak pręta " + mark + " z osią w " + P(p));
        }

        private static List<TypedValue> RawRecord(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            using (var rb = ((Entity)tr.GetObject(id, OpenMode.ForRead)).GetXDataForApplication(XApp))
                return rb?.AsArray().ToList() ?? new List<TypedValue>();
        }

        private static object RawField(Database db, ObjectId id, int i)
        {
            var v = RawRecord(db, id);
            return i < v.Count ? v[i].Value : null;
        }

        private static int RawCount(Database db, ObjectId id) => RawRecord(db, id).Count;

        /// <summary>Kapelusz 44: rekord ma [14] (Int16) i [15] = Int16 2020, odczyt „44” bez LegacyRing44.</summary>
        private static string HatRecordCheck(Database db, ObjectId id)
        {
            var v = RawRecord(db, id);
            if (v.Count < 16) throw new AssertFail("rekord kapelusza bez znacznika [15] (pól " + v.Count + ")");
            // W4d (runda 2): kapelusz z obrysem W4d ma 2021 (≥ 2020 = kapelusz)
            if (v[15].TypeCode != (short)DxfCode.ExtendedDataInteger16 || !(v[15].Value is short s) || s < 2020)
                throw new AssertFail("[15] = " + Show(v[15].Value) + " (typ " + v[15].TypeCode + "), oczekiwano Int16 ≥ 2020");
            if (v[14].TypeCode != (short)DxfCode.ExtendedDataInteger16) throw new AssertFail("[14] nie Int16");
            var b = ReadBar(db, id);
            return Eq("odczyt", Prop(b, "ShapeCode"), "44") + " " + Eq("LegacyRing44", Prop(b, "LegacyRing44"), false)
                 + " [14]=" + Show(v[14].Value) + " [15]=" + Show(v[15].Value);
        }

        // ================================================================
        // Pomocnicze W4 (wspólne)
        // ================================================================

        /// <summary>
        /// Pręt-polilinia z obrysem geometrii sprzed W4 (BarFrameRules.LegacyLocalOutline) — jak rysunek starej wtyczki;
        /// opcjonalnie obrót 30° wokół d.At i MIRROR względem x = 5000. Zmiany przy wyciszonym watcherze (bez Plan B).
        /// </summary>
        private static ObjectId PlaceLegacyOutlineBar(Database db, Def d, bool rotateMirror)
        {
            var id = PlaceBar(db, "Polyline", d);
            var bar = ReadBar(db, id);
            var pv = (double[])Prop(bar, "ParamValues");
            var local = (IList)Call(FrameRulesType, "LegacyLocalOutline", d.Code, pv, (double)d.D);
            if (local == null || local.Count < 4) throw new AssertFail("LegacyLocalOutline: brak obrysu");
            ReplaceOutline(db, id, local.Cast<object>().ToList(), d.At, rotateMirror);
            StripTail(db, id);   // rysunek sprzed W4d: rekord bez [14+] (bez znacznika geometrii W4d)
            return id;
        }

        /// <summary>
        /// Rekord „starego” pręta (sprzed W4d): polilinia — tylko nagłówek [0]–[13]; pręt-blok — [0]–[14] ([14] = 2), bez [15].
        /// Zapis surowy przy wyciszonym watcherze.
        /// </summary>
        private static void StripTail(Database db, ObjectId id)
        {
            using ((IDisposable)Call(WatcherType, "Suppress"))
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var e = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                if (e is Polyline) Call(WatcherType, "IgnoreThisCommand", id);
                var v = e.GetXDataForApplication(XApp).AsArray().ToList();
                int keep = e is BlockReference ? 15 : 14;
                if (v.Count > keep) e.XData = new ResultBuffer(v.Take(keep).ToArray());
                tr.Commit();
            }
        }

        private static int Std15(Database db, ObjectId id)
        {
            var v = RawRecord(db, id);
            return v.Count > 15 && v[15].Value is short s ? s : 0;
        }

        // ================================================================
        // Runda 2 — znacznik geometrii W4d; 51 A = B (obrys W4b–W4c == W4d)
        // ================================================================

        private static ObjectId _gOldPl, _gOldBlk, _gNewPl, _gNewBlk, _gMove;
        private static readonly double[] G51 = { 400, 400, 0, 0, 0 };

        private static string HookManual(Database db, ObjectId id, double expected)
        {
            var r = new List<string>();
            foreach (bool left in new[] { true, false })
            {
                int s = SegIdxOf(db, id, "51", 0, left, legacy: false);
                double got = ManualAt(db, id, s, out double meas);
                r.Add(Num(left ? "hak (lewa)" : "hak (prawa)", got, expected, 0.5) + " (zmierzone " + F(meas) + ")");
            }
            return string.Join("; ", r) + "; [15]=" + Std15(db, id) + "; GeometryW4d=" + Show(Prop(ReadBar(db, id), "GeometryW4d"));
        }

        [CommandMethod("RCSMOKE_W4_G", CommandFlags.Modal)]
        public void W4G()
        {
            const string step = "G";
            Run(step, db =>
            {
                Check(step, "nowy pręt (PlaceBar polilinia i blok): [14], [15] = 2021, GeometryW4d", () =>
                {
                    _gNewPl  = PlaceShapeBar(db, "Polyline", "51", G51, new Point3d(0, 118000, 0));
                    _gNewBlk = PlaceShapeBar(db, "Block", "51", G51, new Point3d(3000, 118000, 0));
                    var vp = RawRecord(db, _gNewPl);
                    var vb = RawRecord(db, _gNewBlk);
                    return Int("pola polilinii", vp.Count, 16) + " [14]=" + Show(vp[14].Value) + "; " + Int("pola bloku", vb.Count, 16)
                         + " [14]=" + Show(vb[14].Value) + "; " + Int("[15] polilinii", Std15(db, _gNewPl), 2021) + "; "
                         + Int("[15] bloku", Std15(db, _gNewBlk), 2021);
                });
                Check(step, "stary 51 A = B = 400, C = 0 (polilinia, bez znacznika), obrót 30° + MIRROR w kodzie → Manual hak = 168 (wzorzec)", () =>
                {
                    _gOldPl = PlaceShapeBar(db, "Polyline", "51", G51, new Point3d(0, 110000, 0));
                    var local = ((IList)Call(FrameRulesType, "LocalOutline", "51", G51, 12.0)).Cast<object>().ToList();
                    var at = Axis(PolyVerts(db, _gOldPl))[0];
                    ReplaceOutline(db, _gOldPl, local, at, true);
                    StripTail(db, _gOldPl);
                    return HookManual(db, _gOldPl, 168);
                });
                Check(step, "stary 51 (pręt-blok bez [15]), obrót 30° + MIRROR w kodzie → Manual hak = 168", () =>
                {
                    _gOldBlk = PlaceShapeBar(db, "Block", "51", G51, new Point3d(0, 114000, 0));
                    StripTail(db, _gOldBlk);
                    using ((IDisposable)Call(WatcherType, "Suppress"))
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var br = (Entity)tr.GetObject(_gOldBlk, OpenMode.ForWrite);
                        br.TransformBy(Matrix3d.Rotation(Math.PI / 6, Vector3d.ZAxis, new Point3d(0, 114000, 0)));
                        br.TransformBy(Matrix3d.Mirroring(new Line3d(new Point3d(5000, 0, 0), new Point3d(5000, 1, 0))));
                        tr.Commit();
                    }
                    return HookManual(db, _gOldBlk, 168);
                });
                Check(step, "nowy 51 (ze znacznikiem) → Manual hak = 132 (geometria W4d), polilinia i blok", () =>
                    HookManual(db, _gNewPl, 132) + " | " + HookManual(db, _gNewBlk, 132));
                Check(step, "stary 51 bez znacznika (0,122000) do _.MOVE: Manual = 168", () =>
                {
                    _gMove = PlaceShapeBar(db, "Polyline", "51", G51, new Point3d(0, 122000, 0));
                    StripTail(db, _gMove);
                    return HookManual(db, _gMove, 168);
                });
            });
        }

        private static ObjectId _hUpd, _hScale;

        /// <summary>Blok 51: [14] = 2, [15] = 2021, skala ±1, Manual haka 132.</summary>
        private static string MarkedBlock132(Database db, ObjectId id)
        {
            var v = RawRecord(db, id);
            if (v.Count < 16) throw new AssertFail("rekord " + v.Count + " pól (brak [15])");
            if (!(v[14].Value is short s14) || s14 != 2) throw new AssertFail("[14] = " + Show(v[14].Value));
            return Int("[15]", Std15(db, id), 2021) + "; " + UnitScaleCheck(db, id) + "; " + HookManual(db, id, 132);
        }

        private static string UnitScaleCheck(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var s = ((BlockReference)tr.GetObject(id, OpenMode.ForRead)).ScaleFactors;
                if (Math.Abs(Math.Abs(s.X) - 1) > 1e-6 || Math.Abs(Math.Abs(s.Y) - 1) > 1e-6)
                    throw new AssertFail("skala " + F(s.X) + "," + F(s.Y));
                return "skala " + F(s.X) + "," + F(s.Y);
            }
        }

        /// <summary>Spójność po UNDO / REDO: znacznik ⇔ Manual 132, brak znacznika ⇔ 168 (wzorzec).</summary>
        private static string Consistent(Database db, ObjectId id)
        {
            int std = Std15(db, id);
            return (std >= 2021 ? "znacznik" : "bez znacznika") + "; " + HookManual(db, id, std >= 2021 ? 132 : 168);
        }

        [CommandMethod("RCSMOKE_W4_H", CommandFlags.Modal)]
        public void W4H()
        {
            const string step = "H";
            Run(step, db =>
            {
                Check(step, "historyczny pręt-blok 51 A = B (bez [15]) (0,126000) → RC_UPDATE_BAR (UpdateBarBlock) → [15] = 2021, Manual 132", () =>
                {
                    _hUpd = PlaceShapeBar(db, "Block", "51", G51, new Point3d(0, 126000, 0));
                    StripTail(db, _hUpd);
                    string before = HookManual(db, _hUpd, 168);
                    var doc = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument;
                    Call("BricsCadRc.Commands.BarCommands", "UpdateBarBlock", doc, _hUpd, false);
                    return "przed: " + before + " | po: " + MarkedBlock132(db, _hUpd);
                });
                Check(step, "historyczny pręt-blok 51 (0,130000) do _.SCALE 2: bez [15], Manual 168", () =>
                {
                    _hScale = PlaceShapeBar(db, "Block", "51", G51, new Point3d(0, 130000, 0));
                    StripTail(db, _hScale);
                    return Int("pola", RawCount(db, _hScale), 15) + "; " + HookManual(db, _hScale, 168);
                });
            });
        }

        [CommandMethod("RCSMOKE_W4_HV", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4HV()
        {
            const string step = "HV";
            Run(step, db =>
                Check(step, "_.SCALE 2 → automatyczna naprawa skali (RepairScale): [14] = 2, [15] = 2021, skala ±1, Manual 132", () =>
                    MarkedBlock132(db, _hScale)));
        }

        [CommandMethod("RCSMOKE_W4_HU", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4HU()
        {
            const string step = "HU";
            Run(step, db =>
                Check(step, "_.U (cofa SCALE z naprawą): rekord i Manual spójne", () => Consistent(db, _hScale) + "; [15]=" + Std15(db, _hScale)
                      + "; pola=" + RawCount(db, _hScale)));
        }

        [CommandMethod("RCSMOKE_W4_HR", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4HR()
        {
            const string step = "HR";
            Run(step, db =>
                Check(step, "_.REDO: rekord i Manual spójne", () => Consistent(db, _hScale) + "; [15]=" + Std15(db, _hScale)
                      + "; pola=" + RawCount(db, _hScale)));
        }

        [CommandMethod("RCSMOKE_W4_GV", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void W4GV()
        {
            const string step = "GV";
            Run(step, db =>
            {
                Check(step, "stary 51 po _.MOVE: Plan B przebudował obrys geometrią W4d → znacznik [15] = 2021 → Manual hak = 132 (D34)", () =>
                    Int("[15]", Std15(db, _gMove), 2021) + "; " + HookManual(db, _gMove, 132));
                Check(step, "stare pręty bez przebudowy (polilinia i blok) — nadal bez znacznika, 168", () =>
                    HookManual(db, _gOldPl, 168) + " | " + HookManual(db, _gOldBlk, 168));
            });
        }

        /// <summary>
        /// Podmiana wierzchołków polilinii pręta na obrys lokalny local (krotki (X, Y)): axis[0] obrysu w at; opcjonalnie
        /// obrót 30° wokół at i MIRROR względem x = 5000. Zmiany przy wyciszonym watcherze (bez Plan B).
        /// </summary>
        private static void ReplaceOutline(Database db, ObjectId id, IList<object> local, Point3d at, bool rotateMirror)
        {
            var d = new Def { At = at };
            using ((IDisposable)Call(WatcherType, "Suppress"))
            using (var tr = db.TransactionManager.StartTransaction())
            {
                Call(WatcherType, "IgnoreThisCommand", id);
                var pl = (Polyline)tr.GetObject(id, OpenMode.ForWrite);
                var axis0Local = Axis(local.Cast<(double X, double Y)>().Select(p => new Point3d(p.X, p.Y, 0)).ToList())[0];
                // Obrót i odbicie liczone na wierzchołkach (TransformBy z odbiciem odwraca normalną polilinii — MIRROR
                // BricsCAD tego nie robi); normalna zostaje +Z.
                int old = pl.NumberOfVertices;
                for (int i = 0; i < local.Count; i++)
                {
                    var p = ((double X, double Y))local[i];
                    var w = new Point3d(d.At.X + p.X - axis0Local.X, d.At.Y + p.Y - axis0Local.Y, 0);
                    if (rotateMirror) w = MirX(Rot(w, d.At, 30), 5000);
                    pl.AddVertexAt(old + i, new Point2d(w.X, w.Y), 0, 0, 0);
                }
                for (int i = 0; i < old; i++) pl.RemoveVertexAt(0);
                pl.Closed = true;
                tr.Commit();
            }
        }

        private static double MaxDev(List<Point3d> a, List<Point3d> b)
        {
            if (a.Count != b.Count) return double.PositiveInfinity;
            double m = 0;
            for (int i = 0; i < a.Count; i++) m = Math.Max(m, a[i].DistanceTo(b[i]));
            return m;
        }
    }
}
