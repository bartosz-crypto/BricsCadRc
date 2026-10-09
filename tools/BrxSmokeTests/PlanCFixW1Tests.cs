// W1 — testy dymne poprawek błędów wzorca (docs/PLAN_POPRAWKI_W1_W2.md 6.1, docs/POPRAWKI_WZORCA.md) w BricsCAD V25.
// Uruchamiane skryptem planc-w1.scr (run-planc-w1.ps1). Komendy RCSMOKE_W1_*.
//
// Każdy przypadek: komenda tworząca → (komenda edycji / komenda skryptu: STRETCH, ROTATE, MIRROR, MOVE, RC_UPDATE_BAR)
// → komenda sprawdzająca (NoUndoMarker) — reakcje watchera (BarGeometryWatcher) zachodzą w CommandEnded komendy edycji.
// Dostęp do wtyczki WYŁĄCZNIE przez refleksję na zestawie NaSmartRc wczytanym NETLOAD-em (jak w etapach 1–3).

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.BrxSmokeTests
{
    public partial class PlanCFixW1Tests
    {
        // ----------------------------------------------------------------
        // Stałe testu
        // ----------------------------------------------------------------

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const double GeoTol      = 1e-6;
        // Przełączane przez RCSMOKE_W2_SETUP (testy W2 w tej samej klasie częściowej — PlanCFixW2Tests.cs)
        private static string ResultsFile = "planc-w1-results.txt";
        private static string Prefix      = "[RC W1]";
        private static string Title       = "W1 — testy dymne poprawek wzorca";
        private const string XApp        = "RC_SINGLE_BAR";
        private const string XLinkApp    = "RC_BAR_LINK";

        private const string SingleBarType  = "BricsCadRc.Core.SingleBarEngine";
        private const string BarEntityType  = "BricsCadRc.Core.BarEntity";
        private const string BarDataType    = "BricsCadRc.Core.BarData";
        private const string BarKindsType   = "BricsCadRc.Core.BarKinds";
        private const string BarFormatType  = "BricsCadRc.Core.BarFormat";
        private const string BarFormatKind  = "BricsCadRc.Core.BarFormatKind";
        private const string ShapeLibType   = "BricsCadRc.Core.ShapeCodeLibrary";
        private const string BarBlockType   = "BricsCadRc.Core.BarBlockEngine";
        private const string AnnotType      = "BricsCadRc.Core.AnnotationEngine";
        private const string CounterType    = "BricsCadRc.Core.PositionCounter";
        private const string EditApplyType  = "BricsCadRc.Core.BarEditApply";

        private const int KPolyline = 1, KBlock = 2;

        /// <summary>Kroki oczekiwane w raporcie — dopisywane przez kolejne części (W1a–W1e).</summary>
        private static List<string> ExpectedSteps => _f3Mode ? ExpectedStepsF3 : _w4Mode ? ExpectedStepsW4 : _w2Mode ? ExpectedStepsW2 : ExpectedStepsW1;

        private static readonly List<string> ExpectedStepsW1 = new List<string>
        {
            "SETUP",
            "1", "1EDIT", "1V", "1G", "1G_FAIL", "1GV",
            "2", "2A_ERASE", "2A_UNDO", "2A_REDO", "2BC",
            "4", "4EDIT", "4V",
            "5", "5EDIT", "5V", "5U",
            "7", "7EDIT", "7V",
        };

        // ----------------------------------------------------------------
        // Stan sesji testowej
        // ----------------------------------------------------------------

        private static readonly List<string> _results  = new List<string>();
        private static readonly List<string> _executed = new List<string>();
        private static int _stepStart;

        private sealed class AssertFail : System.Exception
        {
            public AssertFail(string msg) : base(msg) { }
        }

        /// <summary>Definicja pręta testowego.</summary>
        private sealed class Def
        {
            public string Code = "00";
            public int D = 12;
            public double A, B, C;
            public int Nr;
            public Point3d At;
        }

        // ================================================================
        // Komendy wspólne
        // ================================================================

        [CommandMethod("RCSMOKE_W1_SETUP", CommandFlags.Modal)]
        public void Setup()
        {
            SetMode(false);
            _results.Clear();
            _executed.Clear();
            const string step = "SETUP";
            Run(step, db =>
            {
                Check(step, "wtyczka NaSmartRc wczytana (jedna kopia)", () =>
                {
                    var all = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "NaSmartRc").ToList();
                    var p = Plugin;
                    if (all.Count != 1)
                        throw new AssertFail($"wczytane kopie NaSmartRc: {all.Count} ({string.Join("; ", all.Select(Loc))})");
                    return "Location=" + Loc(p);
                });
                Check(step, "czyszczenie przestrzeni modelu + RegApp", () =>
                {
                    int n = CleanModelSpace(db);
                    Call(SingleBarType, "EnsureAppIdRegistered", db);
                    Call(BarBlockType, "EnsureAppIdRegistered", db);
                    Call(AnnotType, "EnsureAppIdRegistered", db);
                    SetFormat("Polyline");
                    return "usunięto " + n.ToString(Inv) + "; format Polyline";
                });
            });
        }

        [CommandMethod("RCSMOKE_W1_REPORT", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Report()
        {
            try { SetFormat("Polyline"); }
            catch (System.Exception ex) { Add("FAIL", "REPORT", "przywrócenie formatu Polyline", ex.Message); }
            foreach (var st in ExpectedSteps)
                if (!_executed.Contains(st))
                    Add("FAIL", st, "krok nie został wykonany", "skrypt nie dotarł do komendy kroku albo ją pominął");

            int pass = _results.Count(r => r.StartsWith("PASS", StringComparison.Ordinal));
            int fail = _results.Count(r => r.StartsWith("FAIL", StringComparison.Ordinal));
            int skip = _results.Count(r => r.StartsWith("SKIP", StringComparison.Ordinal));
            Flush(new[] { "", $"PODSUMOWANIE: PASS={pass} FAIL={fail} SKIP={skip}", "END" });
            Msg($"\n{Prefix} Raport: {Path.Combine(OutDir, ResultsFile)}  PASS={pass} FAIL={fail} SKIP={skip}\n");
        }

        // ================================================================
        // W1b (#1) — RC_EDIT_BAR (BarEditApply.Apply) nie dolicza zmiany drugi raz; STRETCH końca działa jak dotąd
        // ================================================================

        private sealed class Set1 { public ObjectId Bar, Label, Dist; }
        private static Set1 _s1a, _s1b, _s1c, _s1e, _s1f;
        private static ObjectId _b1d;

        [CommandMethod("RCSMOKE_W1_1", CommandFlags.Modal)]
        public void T1()
        {
            const string step = "1";
            Run(step, db =>
            {
                _s1a = _s1b = _s1c = _s1e = null;
                _b1d = ObjectId.Null;
                Check(step, "1a/1c/1e: 00 H12 A=3000 + etykieta + rozkład; 1b: 21 H10 500/300/500 + etykieta + rozkład", () =>
                {
                    _s1a = MakeSet1(db, new Def { A = 3000, At = new Point3d(0, 10000, 0) });
                    _s1b = MakeSet1(db, new Def { Code = "21", D = 10, A = 500, B = 300, C = 500, At = new Point3d(0, 14000, 0) });
                    _s1c = MakeSet1(db, new Def { A = 3000, At = new Point3d(0, 18000, 0) });
                    _s1e = MakeSet1(db, new Def { A = 3000, At = new Point3d(0, 26000, 0) });
                    return "OK";
                });
                Check(step, "1d: pręt-blok 00 H12 A=3000 + etykieta", () =>
                {
                    _b1d = PlaceBarWithLabel(db, "Block", new Def { A = 3000, At = new Point3d(0, 22000, 0) }).bar;
                    return Eq("KindOf", Convert.ToString(KindOf(db, _b1d), Inv), "Block");
                });
            });
        }

        private static Set1 MakeSet1(Database db, Def d)
        {
            var s = new Set1();
            (s.Bar, s.Label) = PlaceBarWithLabel(db, "Polyline", d);
            s.Dist = Distribute(db, s.Bar, 40, d.At.Y + 1040, 3040, d.At.Y + 2000).dist;
            return s;
        }

        [CommandMethod("RCSMOKE_W1_1EDIT", CommandFlags.Modal)]
        public void T1Edit()
        {
            const string step = "1EDIT";
            Run(step, db =>
            {
                Check(step, "1a: BarEditApply A 3000 → 3500", () => { EditApply(db, _s1a.Bar, b => SetProp(b, "LengthA", 3500.0)); return "OK"; });
                Check(step, "1b: BarEditApply C 500 → 700", () => { EditApply(db, _s1b.Bar, b => SetProp(b, "LengthC", 700.0)); return "OK"; });
                Check(step, "1c: BarEditApply A 3000 → 1000", () => { EditApply(db, _s1c.Bar, b => SetProp(b, "LengthA", 1000.0)); return "OK"; });
                Check(step, "1d: BarEditApply (blok) A 3000 → 3500", () => { EditApply(db, _b1d, b => SetProp(b, "LengthA", 3500.0)); return "OK"; });
                Check(step, "1a: przed CommandEnded A=3500", () => Num("A", BarNum(db, _s1a.Bar, "LengthA"), 3500));
                Check(step, "1f: 00 + etykieta + rozkład utworzone W TEJ komendzie (pręt w kolejce watchera), BarEditApply A → 3500", () =>
                {
                    _s1f = MakeSet1(db, new Def { A = 3000, At = new Point3d(0, 28000 + 3000, 0) });
                    EditApply(db, _s1f.Bar, b => SetProp(b, "LengthA", 3500.0));
                    return "OK";
                });
            });
        }

        [CommandMethod("RCSMOKE_W1_1V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void T1V()
        {
            const string step = "1V";
            Run(step, db =>
            {
                Check(step, "1a: po CommandEnded A=TL=3500, rozkład [8]=3500, oś obrysu 3500, Mark bez zmian (#1)", () =>
                {
                    var b = ReadBar(db, _s1a.Bar);
                    var ax = Axis(PolyVerts(db, _s1a.Bar));
                    return Num("A", Convert.ToDouble(Prop(b, "LengthA"), Inv), 3500) + "; "
                         + Num("TL", Convert.ToDouble(Prop(b, "TotalLength"), Inv), 3500) + "; "
                         + Num("rozkład A", DistLength(db, _s1a.Dist), 3500) + "; "
                         + Num("oś obrysu", ax[0].DistanceTo(ax[ax.Count - 1]), 3500, 1e-6) + "; "
                         + Eq("Mark", Prop(b, "Mark"), MarkOf(12, PosOfBar(db, _s1a.Bar)));
                });
                Check(step, "1b: C=700 dokładnie, A=500, B=300 (#1)", () =>
                    Num("C", BarNum(db, _s1b.Bar, "LengthC"), 700) + "; " + Num("A", BarNum(db, _s1b.Bar, "LengthA"), 500)
                    + "; " + Num("B", BarNum(db, _s1b.Bar, "LengthB"), 300));
                Check(step, "1c: A=1000 (kontrola)", () =>
                    Num("A", BarNum(db, _s1c.Bar, "LengthA"), 1000) + "; " + Num("rozkład A", DistLength(db, _s1c.Dist), 1000));
                Check(step, "1d: pręt-blok A=3500", () => Num("A", BarNum(db, _b1d, "LengthA"), 3500));
                Check(step, "1f: pręt z kolejki watchera tej komendy — po CommandEnded A=3500 (nie 4000), rozkład 3500", () =>
                    Num("A", BarNum(db, _s1f.Bar, "LengthA"), 3500) + "; " + Num("rozkład A", DistLength(db, _s1f.Dist), 3500));
                Check(step, "1e: _.STRETCH końca +500 (po RC_EDIT_BAR w sesji — watcher nie wyciszony) → A=3500, rozkład 3500", () =>
                    Num("A", BarNum(db, _s1e.Bar, "LengthA"), 3500) + "; " + Num("TL", BarNum(db, _s1e.Bar, "TotalLength"), 3500)
                    + "; " + Num("rozkład A", DistLength(db, _s1e.Dist), 3500));
            });
        }

        // ================================================================
        // W1c (#2) — ERASE pręta bez etykiety usuwa jego rozkłady i opisy (polilinia i pręt-blok), UNDO/REDO
        // ================================================================

        private sealed class Set2 { public ObjectId Bar, Label, Dist, Annot; }
        private static Set2 _s2a, _s2b, _s2cA, _s2cB;

        [CommandMethod("RCSMOKE_W1_2", CommandFlags.Modal)]
        public void T2()
        {
            const string step = "2";
            Run(step, db =>
            {
                _s2a = _s2b = _s2cA = _s2cB = null;
                Check(step, "2a: polilinia 00 BEZ etykiety + rozkład + opis", () =>
                {
                    _s2a = MakeSet2(db, "Polyline", new Def { A = 3000, At = new Point3d(0, 30000, 0) }, label: false, dist: true);
                    return True(string.IsNullOrEmpty((string)Prop(ReadBar(db, _s2a.Bar), "LabelHandle")), "LabelHandle pusty");
                });
                Check(step, "2b: pręt-blok 00 BEZ etykiety + rozkład + opis (dawny E2D S11)", () =>
                {
                    _s2b = MakeSet2(db, "Block", new Def { A = 3000, At = new Point3d(0, 34000, 0) }, label: false, dist: true);
                    return Eq("KindOf", Convert.ToString(KindOf(db, _s2b.Bar), Inv), "Block");
                });
                Check(step, "2c: pręt A z etykietą; kopia B z [13] → etykieta A (back-link → A) + rozkład B", () =>
                {
                    _s2cA = MakeSet2(db, "Polyline", new Def { A = 3000, At = new Point3d(0, 38000, 0) }, label: true, dist: false);
                    _s2cB = MakeSet2(db, "Polyline", new Def { A = 3000, Nr = PosOfBar(db, _s2cA.Bar), At = new Point3d(0, 39000, 0) },
                                     label: false, dist: false);
                    WriteBar(db, _s2cB.Bar, b => SetProp(b, "LabelHandle", _s2cA.Label.Handle.ToString()));
                    (_s2cB.Dist, _s2cB.Annot) = Distribute(db, _s2cB.Bar, 40, 40040, 3040, 41000);
                    return "OK";
                });
            });
        }

        private static Set2 MakeSet2(Database db, string fmt, Def d, bool label, bool dist)
        {
            var s = new Set2();
            if (label) (s.Bar, s.Label) = PlaceBarWithLabel(db, fmt, d);
            else s.Bar = PlaceBar(db, fmt, d);
            if (dist) (s.Dist, s.Annot) = Distribute(db, s.Bar, 40, d.At.Y + 1040, 3040, d.At.Y + 2000);
            return s;
        }

        [CommandMethod("RCSMOKE_W1_2A_ERASE", CommandFlags.Modal)]
        public void T2AErase()
        {
            const string step = "2A_ERASE";
            Run(step, db =>
            {
                Check(step, "2a: Erase polilinii bez etykiety → rozkład i opis usunięte (#2)", () =>
                {
                    EraseInCode(db, _s2a.Bar);
                    True(_s2a.Bar.IsErased, "pręt nie usunięty");
                    True(_s2a.Dist.IsErased, "rozkład nie usunięty (kaskada bez etykiety — błąd #2)");
                    return True(_s2a.Annot.IsErased, "opis nie usunięty (kaskada bez etykiety — błąd #2)");
                });
            });
        }

        [CommandMethod("RCSMOKE_W1_2A_UNDO", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void T2AUndo()
        {
            const string step = "2A_UNDO";
            Run(step, db =>
            {
                Check(step, "2a: _.UNDO 1 → pręt, rozkład i opis przywrócone, [18] → pręt", () =>
                {
                    True(!_s2a.Bar.IsErased, "pręt nadal usunięty");
                    True(!_s2a.Dist.IsErased, "rozkład nadal usunięty");
                    True(!_s2a.Annot.IsErased, "opis nadal usunięty");
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        var dist = CallD(BarBlockType, "ReadXData", tr.GetObject(_s2a.Dist, OpenMode.ForRead));
                        return True(SameHandle(Prop(dist, "SourceBarHandle"), _s2a.Bar.Handle), "SourceBarHandle=" + Prop(dist, "SourceBarHandle"));
                    }
                });
            });
        }

        [CommandMethod("RCSMOKE_W1_2A_REDO", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void T2ARedo()
        {
            const string step = "2A_REDO";
            Run(step, db =>
            {
                Check(step, "2a: _.REDO → pręt, rozkład i opis ponownie usunięte", () =>
                {
                    True(_s2a.Bar.IsErased, "pręt nieusunięty — _.REDO nie zadziałało");
                    True(_s2a.Dist.IsErased, "rozkład przywrócony przy REDO");
                    return True(_s2a.Annot.IsErased, "opis przywrócony przy REDO");
                });
            });
        }

        [CommandMethod("RCSMOKE_W1_2BC", CommandFlags.Modal)]
        public void T2BC()
        {
            const string step = "2BC";
            Run(step, db =>
            {
                Check(step, "2b: Erase pręta-bloku bez etykiety → rozkład i opis usunięte (#2)", () =>
                {
                    EraseInCode(db, _s2b.Bar);
                    True(_s2b.Bar.IsErased, "pręt nie usunięty");
                    True(_s2b.Dist.IsErased, "rozkład nie usunięty");
                    return True(_s2b.Annot.IsErased, "opis nie usunięty");
                });
                Check(step, "2c: Erase kopii B ([13] → cudza etykieta) → etykieta A zostaje, rozkład B usunięty", () =>
                {
                    EraseInCode(db, _s2cB.Bar);
                    True(_s2cB.Bar.IsErased, "pręt B nie usunięty");
                    True(!_s2cA.Label.IsErased, "etykieta A usunięta (back-link → A)");
                    True(!_s2cA.Bar.IsErased, "pręt A usunięty");
                    True(_s2cB.Dist.IsErased, "rozkład B nie usunięty");
                    return True(_s2cB.Annot.IsErased, "opis B nie usunięty");
                });
            });
        }

        // ================================================================
        // W1d (#4) — przebudowa w ramie pręta: ROTATE / MIRROR zachowane (watcher Plan B, RC_EDIT_BAR, STRETCH końca)
        // ================================================================

        private static ObjectId _b4a, _b4b, _b4bLabel, _b4c, _b4d, _b4e, _b4f, _b4g;
        private static List<Point3d> _b4bOutline;
        private static string _b4dMark;
        private static readonly Vector3d Dir30 = new Vector3d(Math.Cos(Math.PI / 6), Math.Sin(Math.PI / 6), 0);

        [CommandMethod("RCSMOKE_W1_4", CommandFlags.Modal)]
        public void T4()
        {
            const string step = "4";
            Run(step, db =>
            {
                _b4a = _b4b = _b4bLabel = _b4c = _b4d = _b4e = _b4f = _b4g = ObjectId.Null;
                Check(step, "4a 21 (0,42000); 4b 21 + etykieta (0,45000); 4c 00 (0,48000); 4d 21 (0,51000); " +
                            "4e 21 (0,54000) obrócony w kodzie 30°; 4f 00 (0,57000); 4g 00 (0,60000)", () =>
                {
                    _b4a = PlaceBar(db, "Polyline", new Def { Code = "21", D = 10, A = 500, B = 300, C = 500, At = new Point3d(0, 42000, 0) });
                    (_b4b, _b4bLabel) = PlaceBarWithLabel(db, "Polyline", new Def { Code = "21", D = 10, A = 500, B = 300, C = 500, At = new Point3d(0, 45000, 0) });
                    _b4c = PlaceBar(db, "Polyline", new Def { A = 3000, At = new Point3d(0, 48000, 0) });
                    _b4d = PlaceBar(db, "Polyline", new Def { Code = "21", D = 10, A = 500, B = 300, C = 500, At = new Point3d(0, 51000, 0) });
                    _b4dMark = (string)Prop(ReadBar(db, _b4d), "Mark");
                    _b4e = PlaceBar(db, "Polyline", new Def { Code = "21", D = 10, A = 500, B = 300, C = 500, At = new Point3d(0, 54000, 0) });
                    _b4f = PlaceBar(db, "Polyline", new Def { A = 3000, At = new Point3d(0, 57000, 0) });
                    _b4g = PlaceBar(db, "Polyline", new Def { A = 3000, At = new Point3d(0, 60000, 0) });
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var e = (Entity)tr.GetObject(_b4e, OpenMode.ForWrite);
                        e.TransformBy(Matrix3d.Rotation(Math.PI / 6, Vector3d.ZAxis, new Point3d(0, 54000, 0)));
                        tr.Commit();
                    }
                    _b4bOutline = PolyVerts(db, _b4b);
                    return "OK";
                });
            });
        }

        [CommandMethod("RCSMOKE_W1_4EDIT", CommandFlags.Modal)]
        public void T4Edit()
        {
            const string step = "4EDIT";
            Run(step, db =>
            {
                Check(step, "4e: po obrocie w kodzie (watcher Plan B) pręt nadal 30°", () =>
                {
                    var v = PolyVerts(db, _b4e);
                    return Pts("4e obrys", v, ExpectedOutline(ReadBar(db, _b4e), new Point3d(0, 54000, 0), Dir30, false));
                });
                Check(step, "4c: BarEditApply A 3000 → 3500 (pręt obrócony 30° skryptem)", () =>
                { EditApply(db, _b4c, b => SetProp(b, "LengthA", 3500.0)); return "OK"; });
                Check(step, "4d: odbity (MIRROR Y) 21: BarEditApply B 300 → 400", () =>
                {
                    _b4d = FindBar(db, _b4dMark, 51000, ObjectId.Null);
                    EditApply(db, _b4d, b => SetProp(b, "LengthB", 400.0));
                    return "kopia=" + _b4d.Handle;
                });
                Check(step, "4e: jeden wierzchołek (5) przesunięty o (50,50) → Plan B w CommandEnded", () =>
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var pl = (Polyline)tr.GetObject(_b4e, OpenMode.ForWrite);
                        var p = pl.GetPoint2dAt(5);
                        pl.SetPointAt(5, new Point2d(p.X + 50, p.Y + 50));
                        tr.Commit();
                    }
                    return "OK";
                });
                Check(step, "4f: poziomy 00: BarEditApply A 3000 → 3500", () =>
                { EditApply(db, _b4f, b => SetProp(b, "LengthA", 3500.0)); return "OK"; });
            });
        }

        [CommandMethod("RCSMOKE_W1_4V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void T4V()
        {
            const string step = "4V";
            Run(step, db =>
            {
                Check(step, "4a: _.ROTATE 30° pręta 21 → po Plan B obrys obrócony 30° (#4)", () =>
                    Pts("4a obrys", PolyVerts(db, _b4a), ExpectedOutline(ReadBar(db, _b4a), new Point3d(0, 42000, 0), Dir30, false)));
                Check(step, "4b: _.MIRROR N (pręt 21 + etykieta) → kopia odbita indeksami, oryginał bez zmian (#4)", () =>
                {
                    var copy = FindBar(db, (string)Prop(ReadBar(db, _b4b), "Mark"), 45000, _b4b);
                    var exp = _b4bOutline.Select(p => MirX(p, 5000)).ToList();
                    var (ang, mir) = AxisFrame(PolyVerts(db, copy));
                    return Pts("kopia", PolyVerts(db, copy), exp) + "; " + Pts("oryginał", PolyVerts(db, _b4b), _b4bOutline)
                         + "; " + Eq("odbita", mir, true) + " kąt=" + F(ang);
                });
                Check(step, "4c: 00 obrócony 30°, RC_EDIT_BAR A 3500 → 30°, axis[0] w miejscu", () =>
                    Pts("4c obrys", PolyVerts(db, _b4c), ExpectedOutline(ReadBar(db, _b4c), new Point3d(0, 48000, 0), Dir30, false))
                    + "; " + Num("A", BarNum(db, _b4c, "LengthA"), 3500));
                Check(step, "4d: odbity 21, RC_EDIT_BAR B 400 → nadal odbity, axis[0] (10000,51000)", () =>
                    Pts("4d obrys", PolyVerts(db, _b4d), ExpectedOutline(ReadBar(db, _b4d), new Point3d(10000, 51000, 0), new Vector3d(-1, 0, 0), true))
                    + "; " + Num("B", BarNum(db, _b4d, "LengthB"), 400));
                Check(step, "4e: zepsuty obrys → pręt 30° (kierunek sprzed komendy) w bieżącym axis[0]", () =>
                {
                    var v = PolyVerts(db, _b4e);
                    var a0 = Axis(v)[0];
                    return Pts("4e obrys", v, ExpectedOutline(ReadBar(db, _b4e), a0, Dir30, false)) + "; axis0=" + P(a0);
                });
                Check(step, "4f: poziomy pręt po RC_EDIT_BAR == ComputeOutlineVertices(…, (1,0,0)) DOKŁADNIE (bit w bit)", () =>
                    Pts("4f obrys", PolyVerts(db, _b4f), ExpectedOutline(ReadBar(db, _b4f), new Point3d(0, 57000, 0), new Vector3d(1, 0, 0)), 0.0));
                Check(step, "4g: 00 obrócony 30°, _.STRETCH końca +500 wzdłuż osi → A 3500, nadal 30°", () =>
                    Num("A", BarNum(db, _b4g, "LengthA"), 3500) + "; "
                    + Pts("4g obrys", PolyVerts(db, _b4g), ExpectedOutline(ReadBar(db, _b4g), new Point3d(0, 60000, 0), Dir30, false)));
            });
        }

        // ================================================================
        // W1e (#5) — pierścień (dziś kształt 44): przebudowa obu okręgów, środek zostaje; pręt-blok jak polilinia
        // ================================================================

        private static ObjectId _b5a, _b5b, _b5bBlk, _b5c, _b5e00, _b5e44;

        [CommandMethod("RCSMOKE_W1_5", CommandFlags.Modal)]
        public void T5()
        {
            const string step = "5";
            Run(step, db =>
            {
                _b5a = _b5b = _b5bBlk = _b5c = _b5e00 = _b5e44 = ObjectId.Null;
                Check(step, "pierścienie 44 H12 A=300: 5a (0,80000), 5b (0,83000), 5b-blok (3000,83000), 5c (0,86000); " +
                            "5e: 00 A=1000 (0,89000) i 44 (2000,89000)", () =>
                {
                    _b5a = PlaceBar(db, "Polyline", new Def { Code = W4Compat.RingCode, A = 300, At = new Point3d(0, 80000, 0) });
                    _b5b = PlaceBar(db, "Polyline", new Def { Code = W4Compat.RingCode, A = 300, At = new Point3d(0, 83000, 0) });
                    _b5bBlk = PlaceBar(db, "Block", new Def { Code = W4Compat.RingCode, A = 300, At = new Point3d(3000, 83000, 0) });
                    _b5c = PlaceBar(db, "Polyline", new Def { Code = W4Compat.RingCode, A = 300, At = new Point3d(0, 86000, 0) });
                    _b5e00 = PlaceBar(db, "Polyline", new Def { A = 1000, At = new Point3d(0, 89000, 0) });
                    _b5e44 = PlaceBar(db, "Polyline", new Def { Code = W4Compat.RingCode, A = 300, At = new Point3d(2000, 89000, 0) });
                    return Int("5a RC_BAR_LINK", LinksOf(db, _b5a).Count, 1) + "; " + RingCheck(db, _b5b, new Point3d(150, 83150, 0), 300, 12);
                });
            });
        }

        [CommandMethod("RCSMOKE_W1_5EDIT", CommandFlags.Modal)]
        public void T5Edit()
        {
            const string step = "5EDIT";
            Run(step, db =>
            {
                Check(step, "5b: BarEditApply A 300 → 400 (polilinia)", () => { EditApply(db, _b5b, b => SetProp(b, "LengthA", 400.0)); return "OK"; });
                Check(step, "5b-blok: BarEditApply A 300 → 400 (pręt-blok)", () => { EditApply(db, _b5bBlk, b => SetProp(b, "LengthA", 400.0)); return "OK"; });
                Check(step, "5e: 00 → 44 A=300 i 44 → 00 A=1000 bez wyjątku", () =>
                {
                    EditApply(db, _b5e00, b => { SetProp(b, "ShapeCode", W4Compat.RingCode); SetProp(b, "LengthA", 300.0); });
                    EditApply(db, _b5e44, b => { SetProp(b, "ShapeCode", "00"); SetProp(b, "LengthA", 1000.0); });
                    return "OK";
                });
            });
        }

        [CommandMethod("RCSMOKE_W1_5V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void T5V()
        {
            const string step = "5V";
            Run(step, db =>
            {
                Check(step, "5a: _.MOVE samego okręgu zewnętrznego +1000 → oba okręgi przesunięte (środek (1150,80150)), 1 RC_BAR_LINK",
                      () => RingCheck(db, _b5a, new Point3d(1150, 80150, 0), 300, 12));
                Check(step, "5b: A 400 → promienie 200 ± 6, środek (150,83150) w miejscu, 1 RC_BAR_LINK",
                      () => RingCheck(db, _b5b, new Point3d(150, 83150, 0), 400, 12) + "; " + Num("A", BarNum(db, _b5b, "LengthA"), 400));
                Check(step, "5b-blok: środek (3150,83150) w miejscu; pierścienie = polilinia 5b przesunięta o (3000,0)", () =>
                {
                    var blk = BlockRings(db, _b5bBlk);
                    var pl = PolyRings(db, _b5b);
                    Int("pierścienie bloku", blk.Count, 2);
                    var sb = new StringBuilder();
                    for (int r = 0; r < 2; r++)
                        sb.Append(Pts("pierścień " + r, blk[r], pl[r].Select(p => p + new Vector3d(3000, 0, 0)).ToList())).Append("; ");
                    return sb + Pt("środek", Center(blk[0]), new Point3d(3150, 83150, 0));
                });
                Check(step, "5c: _.ROTATE 45° obu okręgów wokół środka → środek, kąt v0 45°, 2 pierścienie", () =>
                {
                    var rings = PolyRings(db, _b5c);
                    var c = Center(rings[0]);
                    double ang = Math.Atan2(rings[0][0].Y - c.Y, rings[0][0].X - c.X) * 180 / Math.PI;
                    return RingCheck(db, _b5c, new Point3d(150, 86150, 0), 300, 12) + "; " + Num("kąt v0", ang, 45, 1e-6);
                });
                Check(step, "5e: 00 → 44: pierścień od axis[0] starego pręta (środek (150,89150)); 44 → 00: 4 wierzchołki, bez RC_BAR_LINK", () =>
                    RingCheck(db, _b5e00, new Point3d(150, 89150, 0), 300, 12) + "; "
                    + Int("wierzchołki 44→00", PolyVerts(db, _b5e44).Count, 4) + "; " + Int("RC_BAR_LINK 44→00", LinksOf(db, _b5e44).Count, 0));
            });
        }

        [CommandMethod("RCSMOKE_W1_5U", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void T5U()
        {
            const string step = "5U";
            Run(step, db =>
            {
                Check(step, "_.UNDO 1 (cofa 5EDIT): 5b A=300, pierścienie (A = 300) wokół (150,83150), 1 RC_BAR_LINK", () =>
                    Num("A", BarNum(db, _b5b, "LengthA"), 300) + "; " + RingCheck(db, _b5b, new Point3d(150, 83150, 0), 300, 12));
                Check(step, "_.UNDO 1: 5b-blok środek (3150,83150), A=300", () =>
                    Num("A", BarNum(db, _b5bBlk, "LengthA"), 300) + "; " + Pt("środek", Center(BlockRings(db, _b5bBlk)[0]), new Point3d(3150, 83150, 0)));
            });
        }

        /// <summary>
        /// Pręt-pierścień-polilinia: 2 pierścienie (główny + 1 RC_BAR_LINK) wokół c, promienie oś ± d/2 — oś A/2 (przed W4e)
        /// albo (A − d)/2 (W4e: A = średnica zewnętrzna; W4Compat.RingAxisRadius).
        /// </summary>
        private static string RingCheck(Database db, ObjectId bar, Point3d c, double a, double d)
        {
            var rings = PolyRings(db, bar);
            Int("pierścienie (główny + RC_BAR_LINK)", rings.Count, 2);
            double ax = W4Compat.RingAxisRadius(a, d);
            var exp = new[] { ax + d / 2, ax - d / 2 };
            for (int r = 0; r < 2; r++)
            {
                Pt("środek " + r, Center(rings[r]), c);
                foreach (var p in rings[r])
                    if (Math.Abs(p.DistanceTo(c) - exp[r]) > 1e-6)
                        throw new AssertFail($"pierścień {r}: promień {F(p.DistanceTo(c))}, oczekiwano {F(exp[r])}");
            }
            return "środek=" + P(c) + " r=" + F(exp[0]) + "/" + F(exp[1]);
        }

        private static List<List<Point3d>> PolyRings(Database db, ObjectId bar)
        {
            var res = new List<List<Point3d>> { PolyVerts(db, bar) };
            foreach (var l in LinksOf(db, bar)) res.Add(PolyVerts(db, l));
            return res;
        }

        private static List<List<Point3d>> BlockRings(Database db, ObjectId bar)
        {
            var res = new List<List<Point3d>>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var br = (BlockReference)tr.GetObject(bar, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl)) continue;
                    var r = new List<Point3d>();
                    for (int i = 0; i < pl.NumberOfVertices; i++) r.Add(pl.GetPoint3dAt(i).TransformBy(br.BlockTransform));
                    res.Add(r);
                }
            }
            return res;
        }

        /// <summary>Pręt-polilinia o danym Mark w pasie Y (±2500), inny niż except.</summary>
        private static ObjectId FindBar(Database db, string mark, double y, ObjectId except)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || id == except || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl)) continue;
                    var b = Call(SingleBarType, "ReadBarXData", pl);
                    if (b == null || (string)Prop(b, "Mark") != mark) continue;
                    if (Math.Abs(pl.GetPoint3dAt(0).Y - y) < 2500) return id;
                }
            }
            throw new AssertFail("brak pręta " + mark + " w pasie Y=" + F(y));
        }

        // ---- 1g: nieudana RC_EDIT_BAR (wyjątek w Apply po IgnoreThisCommand, komenda kończy się błędem)
        //      → następny _.STRETCH tego pręta NIE jest pomijany (runda poprawek 1) ----
        private static Set1 _s1g;

        [CommandMethod("RCSMOKE_W1_1G", CommandFlags.Modal)]
        public void T1G()
        {
            const string step = "1G";
            Run(step, db =>
            {
                _s1g = null;
                Check(step, "1g: 00 H12 A=3000 + etykieta + rozkład (0,96000)", () =>
                {
                    _s1g = MakeSet1(db, new Def { A = 3000, At = new Point3d(0, 96000, 0) });
                    return "OK";
                });
            });
        }

        // Komendy testowej nie da się zakończyć błędem w /automation (BricsCAD pokazuje okno modalne przy wyjątku
        // i przy przerwaniu z kodu) — handlery CommandFailed / CommandEnded watchera wywoływane refleksją
        // z argumentami jak od BricsCAD; stan _ignoredThisCommand odczytywany refleksją.
        private const string WatcherType = "BricsCadRc.Core.BarGeometryWatcher";

        [CommandMethod("RCSMOKE_W1_1G_FAIL", CommandFlags.Modal)]
        public void T1GFail()
        {
            const string step = "1G_FAIL";
            Run(step, db =>
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                Check(step, "1g: BarEditApply z błędnym numerem pozycji („XX”) → wyjątek PO IgnoreThisCommand, pręt w zbiorze pomijanych", () =>
                {
                    object old = ReadBar(db, _s1g.Bar), upd = ReadBar(db, _s1g.Bar);
                    SetProp(upd, "LengthA", 3500.0);
                    System.Exception fail = null;
                    try { Call(EditApplyType, "Apply", db, null, _s1g.Bar, old, upd, "XX"); }
                    catch (FormatException ex) { fail = ex; }
                    True(fail != null, "Apply nie rzucił wyjątku");
                    True(Ignored().Contains(_s1g.Bar), "pręt nie trafił do _ignoredThisCommand");
                    return Num("A bez zmian", BarNum(db, _s1g.Bar, "LengthA"), 3000);
                });
                Check(step, "koniec przezroczystego 'ZOOM (CommandEnded / CommandFailed) w trakcie komendy → zbiór NIE czyszczony", () =>
                {
                    InvokeWatcher("OnCommandEnded", doc, "ZOOM");
                    InvokeWatcher("OnCommandFailed", doc, "ZOOM");
                    return True(Ignored().Contains(_s1g.Bar), "zbiór wyczyszczony przy końcu komendy zagnieżdżonej");
                });
                Check(step, "CommandFailed komendy najwyższego poziomu → zbiór pomijanych wyczyszczony", () =>
                {
                    InvokeWatcher("OnCommandFailed", doc, "RCSMOKE_W1_1G_FAIL");
                    return True(!Ignored().Contains(_s1g.Bar), "pręt nadal pomijany po CommandFailed");
                });
                Check(step, "CommandCancelled komendy najwyższego poziomu → zbiór pomijanych wyczyszczony", () =>
                {
                    Call(WatcherType, "IgnoreThisCommand", _s1g.Bar);
                    InvokeWatcher("OnCommandCancelled", doc, "RCSMOKE_W1_1G_FAIL");
                    return True(!Ignored().Contains(_s1g.Bar), "pręt nadal pomijany po CommandCancelled");
                });
            });
        }

        private static HashSet<ObjectId> Ignored()
        {
            var fi = Plugin.GetType(WatcherType, true).GetField("_ignoredThisCommand", BindingFlags.Static | BindingFlags.NonPublic);
            if (fi == null) throw new AssertFail("brak pola BarGeometryWatcher._ignoredThisCommand");
            return new HashSet<ObjectId>((HashSet<ObjectId>)fi.GetValue(null));
        }

        /// <summary>Wywołanie prywatnego handlera watchera (sender = dokument, CommandEventArgs z nazwą komendy).</summary>
        private static void InvokeWatcher(string handler, Document doc, string cmd)
        {
            var mi = Plugin.GetType(WatcherType, true).GetMethod(handler, BindingFlags.Static | BindingFlags.NonPublic);
            if (mi == null) throw new AssertFail("brak handlera BarGeometryWatcher." + handler);
            var ctor = typeof(CommandEventArgs).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                                               .FirstOrDefault(c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType == typeof(string));
            if (ctor == null) throw new AssertFail("CommandEventArgs bez konstruktora (string)");
            var args = (CommandEventArgs)ctor.Invoke(new object[] { cmd });
            Invoke(mi, null, new object[] { doc, args });
        }

        [CommandMethod("RCSMOKE_W1_1GV", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void T1GV()
        {
            const string step = "1GV";
            Run(step, db =>
            {
                Check(step, "1g: _.STRETCH końca +500 po nieudanej edycji → XData A=3500, rozkład 3500, oś obrysu 3500", () =>
                {
                    var ax = Axis(PolyVerts(db, _s1g.Bar));
                    return Num("A", BarNum(db, _s1g.Bar, "LengthA"), 3500) + "; " + Num("rozkład A", DistLength(db, _s1g.Dist), 3500)
                         + "; " + Num("oś obrysu", ax[0].DistanceTo(ax[ax.Count - 1]), 3500);
                });
            });
        }

        private static int PosOfBar(Database db, ObjectId id)
            => (int)Call(SingleBarType, "ExtractPosNr", (string)Prop(ReadBar(db, id), "Mark"));

        private static object KindOf(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                return Call(BarEntityType, "KindOf", tr.GetObject(id, OpenMode.ForRead));
        }

        // ================================================================
        // W1a (#7) — TotalLength po zmianie A (watcher 00, RC_UPDATE_BAR); nadpisana długość bez zmian
        // ================================================================

        private static ObjectId _b7a, _b7b, _b7c;
        private const double Y7 = 70000;

        [CommandMethod("RCSMOKE_W1_7", CommandFlags.Modal)]
        public void T7()
        {
            const string step = "7";
            Run(step, db =>
            {
                _b7a = _b7b = _b7c = ObjectId.Null;
                Check(step, "7a/7b/7c: polilinie 00 H12 A=3000; 7c TotalLength 2000 (stary rekord)", () =>
                {
                    _b7a = PlaceBar(db, "Polyline", new Def { A = 3000, At = new Point3d(0, Y7, 0) });
                    _b7b = PlaceBar(db, "Polyline", new Def { A = 3000, At = new Point3d(0, Y7 + 1000, 0) });
                    _b7c = PlaceBar(db, "Polyline", new Def { A = 3000, At = new Point3d(0, Y7 + 2000, 0) });
                    WriteBar(db, _b7c, b => SetProp(b, "TotalLength", 2000.0));
                    return Num("7c TL", BarNum(db, _b7c, "TotalLength"), 2000);
                });
            });
        }

        [CommandMethod("RCSMOKE_W1_7EDIT", CommandFlags.Modal)]
        public void T7Edit()
        {
            const string step = "7EDIT";
            Run(step, db =>
            {
                Check(step, "7a: XData A=2500 TL=2500 (obrys 3000) → watcher w CommandEnded", () =>
                {
                    WriteBar(db, _b7a, b => { SetProp(b, "LengthA", 2500.0); SetProp(b, "TotalLength", 2500.0); });
                    return "OK";
                });
                Check(step, "7b: XData A=2500, LengthOverridden, TL=3333", () =>
                {
                    WriteBar(db, _b7b, b =>
                    {
                        SetProp(b, "LengthA", 2500.0);
                        SetProp(b, "LengthOverridden", true);
                        SetProp(b, "TotalLength", 3333.0);
                    });
                    return "OK";
                });
            });
        }

        [CommandMethod("RCSMOKE_W1_7V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void T7V()
        {
            const string step = "7V";
            Run(step, db =>
            {
                Check(step, "7a: A=3000 z obrysu, TotalLength przeliczona 3000 (#7)", () =>
                    Num("A", BarNum(db, _b7a, "LengthA"), 3000) + "; " + Num("TL", BarNum(db, _b7a, "TotalLength"), 3000));
                Check(step, "7b: A=3000, nadpisana TotalLength 3333 bez zmian", () =>
                    Num("A", BarNum(db, _b7b, "LengthA"), 3000) + "; " + Num("TL", BarNum(db, _b7b, "TotalLength"), 3333)
                    + "; " + Eq("LengthOverridden", Prop(ReadBar(db, _b7b), "LengthOverridden"), true));
                Check(step, "7c: RC_UPDATE_BAR (skrypt) → TotalLength 3000 (#7)", () =>
                    Num("A", BarNum(db, _b7c, "LengthA"), 3000) + "; " + Num("TL", BarNum(db, _b7c, "TotalLength"), 3000));
            });
        }

        // ================================================================
        // Pomocnicze: pręty, rozkłady, edycja
        // ================================================================

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

        private static int NextAuto(Database db)
            => (int)Call(CounterType, "NextAuto", db, Call(CounterType, "GetUsedPositionNumbers", db));

        private static string MarkOf(int d, int nr) => (string)Call(BarDataType, "FormatMark", d, nr, 0.0, 1);

        private static object NewBar(Def d)
        {
            var bar = Activator.CreateInstance(Plugin.GetType(BarDataType, true));
            SetProp(bar, "Mark", MarkOf(d.D, d.Nr));
            SetProp(bar, "Diameter", d.D);
            SetProp(bar, "ShapeCode", d.Code);
            SetProp(bar, "LengthA", d.A);
            SetProp(bar, "LengthB", d.B);
            SetProp(bar, "LengthC", d.C);
            SetProp(bar, "LayerCode", "B1");
            SetProp(bar, "Position", "BOT");
            SetProp(bar, "LengthOverridden", false);
            SetProp(bar, "TotalLength", TotalLength(bar));
            return bar;
        }

        private static double TotalLength(object bar)
        {
            var shape = Call(ShapeLibType, "Get", (string)Prop(bar, "ShapeCode"));
            int n = ((string[])Prop(shape, "Parameters")).Length;
            var pv = ((double[])Prop(bar, "ParamValues")).Take(n).ToArray();
            var m = shape.GetType().GetMethod("CalculateTotalLength");
            return (double)Invoke(m, shape, new object[] { pv, (double)(int)Prop(bar, "Diameter") });
        }

        private static void SetFormat(string kind)
        {
            var kt = Plugin.GetType(BarFormatKind, true);
            Call(BarFormatType, "Set", Enum.Parse(kt, kind), false);
        }

        /// <summary>Pręt w formacie fmt (Polyline / Block) — SingleBarEngine.PlaceBar; numer d.Nr (0 → NextAuto).</summary>
        private static ObjectId PlaceBar(Database db, string fmt, Def d)
        {
            if (d.Nr == 0) d.Nr = NextAuto(db);
            SetFormat(fmt);
            try { return (ObjectId)Call(SingleBarType, "PlaceBar", db, NewBar(d), d.At); }
            finally { SetFormat("Polyline"); }
        }

        /// <summary>RC_BAR bez dialogu (jak tools/RegressionDump): PlaceBar + etykieta w punkcie środka + LabelHandle.</summary>
        private static (ObjectId bar, ObjectId label) PlaceBarWithLabel(Database db, string fmt, Def d)
        {
            var barId = PlaceBar(db, fmt, d);
            Point3d textPt, arrowTip;
            object bar;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var center = (Point3d)Inst(OpenBar(tr, barId), "MidPoint", tr);
                textPt = new Point3d(center.X - 50, center.Y + 150, 0);
                bar = Call(SingleBarType, "ReadBarXData", (Entity)tr.GetObject(barId, OpenMode.ForRead));
                arrowTip = (Point3d)Call(SingleBarType, "GetBarArrowTip", barId, bar, textPt, tr);
                tr.Commit();
            }
            var labelId = (ObjectId)CallD(SingleBarType, "PlaceBarLabel", db, arrowTip, textPt, (string)Prop(bar, "Mark"), barId);
            if (labelId.IsNull) throw new AssertFail("PlaceBarLabel: brak etykiety");
            WriteBar(db, barId, b => SetProp(b, "LabelHandle", labelId.Handle.ToString()));
            return (barId, labelId);
        }

        /// <summary>Zmiana pól rekordu i zapis BarXData.Write (zachowuje [14+] pręta-bloku).</summary>
        private static void WriteBar(Database db, ObjectId id, Action<object> change)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                var bar = Call(SingleBarType, "ReadBarXData", ent);
                change(bar);
                CallD("BricsCadRc.Core.BarXData", "Write", ent, bar);
                tr.Commit();
            }
        }

        private static object ReadBar(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                return Call(SingleBarType, "ReadBarXData", (Entity)tr.GetObject(id, OpenMode.ForRead));
        }

        private static double BarNum(Database db, ObjectId id, string field) => Convert.ToDouble(Prop(ReadBar(db, id), field), Inv);

        /// <summary>RC_DISTRIBUTION bez dialogów (jak tools/RegressionDump): rozkład + opis + powiązania + liczba na etykiecie.</summary>
        private static (ObjectId dist, ObjectId annot) Distribute(Database db, ObjectId barId, double x0, double y0, double x1, double y1)
        {
            const double spacing = 200;
            var src = ReadBar(db, barId);
            string srcHandle = barId.Handle.Value.ToString("X8", Inv);
            SetProp(src, "SourceBarHandle", srcHandle);
            SetProp(src, "Angle", 0.0);
            double rawSpan = y1 - y0;
            int autoCount = Math.Max(1, (int)(rawSpan / spacing) + 1);
            int posNr = (int)Call(SingleBarType, "ExtractPosNr", (string)Prop(src, "Mark"));
            SetProp(src, "Direction", "X");
            SetProp(src, "Cover", 40.0);
            SetProp(src, "Pt1X", x0);
            SetProp(src, "Pt1Y", y0);
            SetProp(src, "BarsSpan", rawSpan);
            SetProp(src, "Spacing", spacing);
            SetProp(src, "Flipped", false);

            var res = CallD(BarBlockType, "GenerateFromBounds", db, x0, y0, x1, y1, src, true, posNr);
            var distId = (ObjectId)Prop(res, "BlockRefId");
            if (distId.IsNull) throw new AssertFail("GenerateFromBounds: brak rozkładu");
            string mark = (string)Call(BarDataType, "FormatMark", (int)Prop(src, "Diameter"), posNr, spacing, autoCount) + " B1";
            SetProp(src, "Mark", mark);
            SetProp(src, "CountDisplay", null);
            WriteDistMark(db, distId, mark, true);
            var ann = CallD(AnnotType, "CreateLeader", db, res, src, false, posNr, null, true, true, true);
            var annId = (ObjectId)Prop(ann, "BlockRefId");
            if (annId.IsNull) throw new AssertFail("CreateLeader: brak opisu");
            CallD(BarBlockType, "LinkAnnotation", db, distId, annId);
            CallD(AnnotType, "UpdateBarLabelCount", db, srcHandle, mark);
            WriteDistMark(db, distId, mark, false);
            CallD(AnnotType, "UpdateBarLabelCount", db, srcHandle, mark);
            return (distId, annId);
        }

        private static void WriteDistMark(Database db, ObjectId distId, string mark, bool withCountDisplay)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = (BlockReference)tr.GetObject(distId, OpenMode.ForWrite);
                var xd = CallD(BarBlockType, "ReadXData", br);
                if (xd != null)
                {
                    SetProp(xd, "Mark", mark);
                    if (withCountDisplay) SetProp(xd, "CountDisplay", null);
                    CallD(BarBlockType, "WriteXData", br, xd);
                }
                tr.Commit();
            }
        }

        private static double DistLength(Database db, ObjectId distId)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var xd = CallD(BarBlockType, "ReadXData", tr.GetObject(distId, OpenMode.ForRead));
                if (xd == null) throw new AssertFail("rozkład bez RC_BAR_BLOCK");
                return Convert.ToDouble(Prop(xd, "LengthA"), Inv);
            }
        }

        /// <summary>BarEditApply.Apply (RC_EDIT_BAR po dialogu): TotalLength wg kształtu, numer bez zmian.</summary>
        private static void EditApply(Database db, ObjectId barId, Action<object> edit)
        {
            object old = ReadBar(db, barId), upd = ReadBar(db, barId);
            edit(upd);
            SetProp(upd, "TotalLength", TotalLength(upd));
            int nr = (int)Call(SingleBarType, "ExtractPosNr", (string)Prop(old, "Mark"));
            var ok = Call(EditApplyType, "Apply", db, null, barId, old, upd, nr.ToString("D2", Inv));
            if (!(ok is bool b) || !b) throw new AssertFail("BarEditApply.Apply zwrócił " + Show(ok));
        }

        private static void EraseInCode(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                tr.GetObject(id, OpenMode.ForWrite).Erase();
                tr.Commit();
            }
        }

        private static object OpenBar(Transaction tr, ObjectId id)
        {
            var be = Call(BarEntityType, "Open", tr, id, OpenMode.ForRead,
                          Enum.ToObject(Plugin.GetType(BarKindsType, true), KPolyline | KBlock));
            if (be == null) throw new AssertFail("BarEntity.Open zwrócił null dla " + id.Handle);
            return be;
        }

        /// <summary>Wierzchołki obrysu pręta w WCS (BarEntity.OutlineVertices — polilinia albo pręt-blok).</summary>
        private static List<Point3d> Outline(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                return ((IEnumerable<Point3d>)Inst(OpenBar(tr, id), "OutlineVertices", tr)).ToList();
        }

        /// <summary>Wierzchołki polilinii (WCS).</summary>
        private static List<Point3d> PolyVerts(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var pl = (Polyline)tr.GetObject(id, OpenMode.ForRead);
                var r = new List<Point3d>(pl.NumberOfVertices);
                for (int i = 0; i < pl.NumberOfVertices; i++) r.Add(pl.GetPoint3dAt(i));
                return r;
            }
        }

        /// <summary>Polilinie RC_BAR_LINK wskazujące na pręt (w przestrzeni modelu).</summary>
        private static List<ObjectId> LinksOf(Database db, ObjectId barId)
        {
            var res = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    using (var rb = e.GetXDataForApplication(XLinkApp))
                    {
                        var v = rb?.AsArray();
                        if (v != null && v.Length >= 2 && SameHandle(v[1].Value, barId.Handle)) res.Add(id);
                    }
                }
            }
            return res;
        }

        /// <summary>Oś obrysu left + reverse(right): axis[i] = środek (v[i], v[n−1−i]).</summary>
        private static List<Point3d> Axis(List<Point3d> v)
        {
            int t = v.Count, n = t / 2;
            var r = new List<Point3d>(n);
            for (int i = 0; i < n; i++) r.Add(new Point3d((v[i].X + v[t - 1 - i].X) / 2, (v[i].Y + v[t - 1 - i].Y) / 2, 0));
            return r;
        }

        /// <summary>Kąt pierwszego odcinka osi [°] i odbicie (lewa strona obrysu po prawej stronie osi).</summary>
        private static (double angleDeg, bool mirrored) AxisFrame(List<Point3d> v)
        {
            var ax = Axis(v);
            var dir = ax[1] - ax[0];
            var side = v[0] - ax[0];
            double cross = dir.X * side.Y - dir.Y * side.X;
            return (Math.Atan2(dir.Y, dir.X) * 180.0 / Math.PI, cross < 0);
        }

        private static Point3d Center(List<Point3d> v) => new Point3d(v.Average(p => p.X), v.Average(p => p.Y), 0);

        private static Point3d Rot(Point3d p, Point3d c, double deg)
            => p.TransformBy(Matrix3d.Rotation(deg * Math.PI / 180.0, Vector3d.ZAxis, c));

        private static Point3d MirX(Point3d p, double x) => new Point3d(2 * x - p.X, p.Y, p.Z);

        /// <summary>
        /// Obrys z SingleBarEngine.ComputeOutlineVertices dla danych pręta: oś zaczyna się w axis0, kierunek dir,
        /// odbicie mirrored (parametr dodany w W1d; null = wywołanie bez parametru odbicia).
        /// </summary>
        private static List<Point3d> ExpectedOutline(object bar, Point3d axis0, Vector3d dir, bool? mirrored = null)
        {
            var shape = Call(ShapeLibType, "Get", (string)Prop(bar, "ShapeCode"));
            var pv = (double[])Prop(bar, "ParamValues");
            double d = (int)Prop(bar, "Diameter");
            var lPts = (IList)CallD("BricsCadRc.Core.BarGeometryBuilder", "GetLocalPoints", (string)Prop(bar, "ShapeCode"), pv, d);
            var l0 = ((double X, double Y))lPts[0];
            var u = dir.GetNormal();
            var perp = mirrored == true ? new Vector3d(u.Y, -u.X, 0) : new Vector3d(-u.Y, u.X, 0);
            var start = axis0 - u * l0.X - perp * l0.Y;
            var pts = mirrored.HasValue
                ? (IEnumerable)CallD(SingleBarType, "ComputeOutlineVertices", shape, pv, d, start, dir, mirrored.Value)
                : (IEnumerable)CallD(SingleBarType, "ComputeOutlineVertices", shape, pv, d, start, dir);
            return pts.Cast<Point2d>().Select(p => new Point3d(p.X, p.Y, 0)).ToList();
        }

        // ================================================================
        // Infrastruktura: refleksja, wyniki, pliki (jak w etapach 1–3)
        // ================================================================

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
            return Invoke(cands[0], null, args);
        }

        /// <summary>Jak Call, ale z parametrami opcjonalnymi (brakujące argumenty = wartości domyślne).</summary>
        private static object CallD(string typeName, string method, params object[] args)
        {
            var t = Plugin.GetType(typeName, true);
            var cands = t.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                         .Where(m => m.Name == method)
                         .Where(m =>
                         {
                             var ps = m.GetParameters();
                             return ps.Length >= args.Length && ps.Skip(args.Length).All(p => p.IsOptional);
                         })
                         .ToList();
            if (cands.Count != 1)
                throw new InvalidOperationException($"{typeName}.{method}/{args.Length}+opc.: znaleziono {cands.Count} przeciążeń");
            var pars = cands[0].GetParameters();
            var full = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++)
                full[i] = i < args.Length ? args[i] : pars[i].DefaultValue;
            return Invoke(cands[0], null, full);
        }

        private static object Inst(object target, string method, params object[] args)
        {
            var t = target.GetType();
            var cands = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .Where(m => m.Name == method && m.GetParameters().Length == args.Length).ToList();
            if (cands.Count != 1)
                throw new InvalidOperationException($"{t.FullName}.{method}/{args.Length}: znaleziono {cands.Count} przeciążeń");
            return Invoke(cands[0], target, args);
        }

        private static object Invoke(MethodInfo mi, object target, object[] args)
        {
            try { return mi.Invoke(target, args); }
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

        private static void SetProp(object o, string name, object value)
        {
            var pi = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi == null) throw new InvalidOperationException(o.GetType().FullName + ": brak właściwości " + name);
            pi.SetValue(o, value);
        }

        private static bool SameHandle(object v, Handle h)
        {
            if (v is Handle hv) return hv.Value == h.Value;
            return v != null && long.TryParse(v.ToString(), NumberStyles.HexNumber, Inv, out long x) && x == h.Value;
        }

        private static string OutDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        private static Database ActiveDb() => Application.DocumentManager.MdiActiveDocument.Database;

        private static void Msg(string text)
        {
            try { Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(text); }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("PlanCFixW1Tests.Msg: " + ex); }
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

        // ---- asercje elementarne (rzucają AssertFail, zwracają opis) ----

        private static string Int(string what, int act, int exp)
        {
            if (act != exp) throw new AssertFail($"{what}: jest {act.ToString(Inv)}, oczekiwano {exp.ToString(Inv)}");
            return $"{what}={act.ToString(Inv)}";
        }

        private static string Num(string what, double act, double exp, double tol = GeoTol)
        {
            if (Math.Abs(act - exp) > tol) throw new AssertFail($"{what}: jest {F(act)}, oczekiwano {F(exp)}");
            return $"{what}={F(act)}";
        }

        private static string Eq(string what, object act, object exp)
        {
            if (!Equals(act, exp)) throw new AssertFail($"{what}: jest {Show(act)}, oczekiwano {Show(exp)}");
            return $"{what}={Show(act)}";
        }

        private static string Pt(string what, Point3d act, Point3d exp, double tol = GeoTol)
        {
            double d = act.DistanceTo(exp);
            if (d > tol) throw new AssertFail($"{what}: jest {P(act)}, oczekiwano {P(exp)} (odchyłka {d.ToString("E2", Inv)})");
            return $"{what}={P(act)}";
        }

        private static string Pts(string what, List<Point3d> act, List<Point3d> exp, double tol = GeoTol)
        {
            if (exp.Count == 0) throw new AssertFail(what + ": brak oczekiwanych wierzchołków");
            Int(what + " liczba wierzchołków", act.Count, exp.Count);
            double max = 0;
            for (int i = 0; i < exp.Count; i++)
            {
                double d = act[i].DistanceTo(exp[i]);
                if (d > tol) throw new AssertFail($"{what} wierzchołek {i}: jest {P(act[i])}, oczekiwano {P(exp[i])} (odchyłka {d.ToString("E2", Inv)})");
                max = Math.Max(max, d);
            }
            return what + ": wierzchołki=" + exp.Count.ToString(Inv) + " max odchyłka=" + max.ToString("E2", Inv);
        }

        private static string True(bool cond, string detail)
        {
            if (!cond) throw new AssertFail(detail);
            return detail;
        }

        private static string Show(object o)
            => o == null ? "null" : o.GetType().Name + " " + Convert.ToString(o, Inv);

        private static string F(double d) => d.ToString("0.######", Inv);
        private static string P(Point3d p) => "(" + p.X.ToString("0.######", Inv) + "," + p.Y.ToString("0.######", Inv) + ")";
    }
}
