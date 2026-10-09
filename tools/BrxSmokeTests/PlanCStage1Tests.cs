// PLAN C ETAP 1 — testy dymne pręta-bloku (RC_TEST_BARBLOCK) w prawdziwym BricsCAD V25.
// Uruchamiane skryptem planc-e1.scr (run-planc-e1.ps1). Operacje (STRETCH, COPY, MIRROR, UNDO, REDO)
// wykonuje skrypt prawdziwymi komendami — tu tylko przygotowanie i asercje.
//
// Dostęp do wtyczki WYŁĄCZNIE przez refleksję na zestawie BricsCadRc wczytanym NETLOAD-em
// (typy internal, bez InternalsVisibleTo; bez typów wtyczki w czasie kompilacji — jedna kopia wtyczki w procesie).

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
    public class PlanCStage1Tests
    {
        // ----------------------------------------------------------------
        // Stałe testu (oczekiwania niezależne od kodu wtyczki)
        // ----------------------------------------------------------------

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const double Tol         = 0.01;   // tolerancja współrzędnych / długości [mm]
        private const double HalfBarTol  = 6.5;    // pół średnicy H12 + zapas — obrys w WCS względem osi
        private const string ResultsFile = "planc-e1-results.txt";
        private const string StateFile   = "planc-e1-state.txt";
        private const string SaveFile    = "planc-e1-save.dwg";
        private const string Prefix      = "[RC E1]";
        private const string XApp        = "RC_SINGLE_BAR";
        private const string TestMark    = "H12-T";
        private const string NamePrefix  = "RC_BAR_TEST_";
        private const string BarLayer    = "RC-SLAB-LEADERS";
        private const int    XDataLen    = 15;     // [0]–[13] + [14] wersja formatu

        private const string EngineType       = "BricsCadRc.Core.TestBarBlockEngine";
        private const string SingleBarType    = "BricsCadRc.Core.SingleBarEngine";
        private const string PositionCntType  = "BricsCadRc.Core.PositionCounter";

        /// <summary>Pręty testowe wstawiane przez RC_E1_SETUP.</summary>
        private static readonly (string key, Point3d p1, Point3d p2, bool anon)[] Defs =
        {
            ("P1", new Point3d(0, 0, 0),    new Point3d(3000, 0, 0),    false),   // STRETCH koniec/początek/całość, UNDO/REDO
            ("P2", new Point3d(0, 2000, 0), new Point3d(2000, 4000, 0), false),   // ukośny: MIRROR, STRETCH w bok
            ("P3", new Point3d(0, 6000, 0), new Point3d(3000, 6000, 0), true),    // anonimowy *U: STRETCH do minimum 50
            ("P4", new Point3d(0, 8000, 0), new Point3d(3000, 8000, 0), false),   // COPY + STRETCH kopii
        };

        /// <summary>Kroki, które skrypt powinien wykonać (brak = FAIL w raporcie).</summary>
        private static readonly string[] ExpectedSteps =
        {
            "SETUP", "V_STRETCH_END", "V_STRETCH_START", "V_UNDO1", "V_UNDO2", "V_REDO", "V_STRETCH_ALL",
            "V_COPY", "V_COPY_STRETCH", "V_MIRROR", "V_MIRROR_STRETCH", "V_PERP", "V_MIN",
            "SAVE_REOPEN", "ISOLATION",
        };

        // ----------------------------------------------------------------
        // Stan sesji testowej (DLL zostaje wczytany między komendami skryptu)
        // ----------------------------------------------------------------

        private static readonly List<string> _results = new List<string>();
        private static readonly List<string> _executed = new List<string>();
        private static readonly Dictionary<string, string> _handles = new Dictionary<string, string>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Snap> _setup = new Dictionary<string, Snap>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Snap> _last  = new Dictionary<string, Snap>(StringComparer.Ordinal);
        private static int? _peekBaseline;
        private static int _stepStart;

        private sealed class AssertFail : System.Exception
        {
            public AssertFail(string msg) : base(msg) { }
        }

        /// <summary>Odczyt pręta testowego z bazy (tylko odczyt).</summary>
        private sealed class Snap
        {
            public string Key = "", Handle = "", Err;
            public bool Found, IsV2, HasBar, VerOk, Anon, Mirrored, HasExt;
            public int RecLen, Refs, GeoCount, GeoXData;
            public string VerDesc = "brak", Mark = "", BtrName = "", BtrHandle = "", Layer = "";
            public double L, TotalL, Rotation;
            public double GeoMinX = double.NaN, GeoMaxX = double.NaN;
            public Point3d Start, End, Position, ExtMin, ExtMax;
            public Vector3d Dir = Vector3d.XAxis;
            public LineWeight Lw;
        }

        // ================================================================
        // Komendy
        // ================================================================

        [CommandMethod("RC_E1_SETUP", CommandFlags.Modal)]
        public void Setup()
        {
            _results.Clear();
            _executed.Clear();
            _handles.Clear();
            _setup.Clear();
            _last.Clear();
            _peekBaseline = null;

            const string step = "SETUP";
            Begin(step);
            var db = ActiveDb();
            try
            {
                Check(step, "wtyczka BricsCadRc wczytana", () =>
                {
                    var all = AppDomain.CurrentDomain.GetAssemblies()
                                       .Where(a => a.GetName().Name == "NaSmartRc").ToList();
                    var p = Plugin;
                    if (all.Count != 1)
                        throw new AssertFail($"wczytane kopie BricsCadRc: {all.Count} ({string.Join("; ", all.Select(Loc))})");
                    return "Location=" + Loc(p);
                });

                Check(step, "czyszczenie przestrzeni modelu", () =>
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
                    return "usunięto " + n.ToString(Inv);
                });

                Check(step, "PositionCounter.Peek przed wstawieniem (odczyt)", () =>
                {
                    _peekBaseline = (int)Call(PositionCntType, "Peek", db);
                    return "Peek=" + _peekBaseline.Value.ToString(Inv);
                });

                foreach (var d in Defs)
                {
                    var def = d;
                    Check(step, def.key + " TestBarBlockEngine.Create", () =>
                    {
                        var res = Call(EngineType, "Create", db, def.p1, def.p2, def.anon);
                        var t = (ValueTuple<ObjectId, string, double>)res;
                        _handles[def.key] = t.Item1.Handle.ToString();
                        return $"uchwyt={t.Item1.Handle} BTR={t.Item2} L={F(t.Item3)}";
                    });
                }

                foreach (var d in Defs)
                {
                    var s = Read(db, d.key);
                    _setup[d.key] = s;
                    CheckCommon(step, s);
                    double expL = Math.Max(50.0, Math.Round(d.p1.DistanceTo(d.p2), MidpointRounding.AwayFromZero));   // min. max(2·12, 50)
                    var dir = (d.p2 - d.p1).GetNormal();
                    Check(step, d.key + " LengthA", () => Num("LengthA", Need(s).L, expL));
                    Check(step, d.key + " początek osi", () => Pt("początek", Need(s).Start, d.p1));
                    Check(step, d.key + " koniec osi (BlockTransform)", () => Pt("koniec", Need(s).End, d.p1 + dir * expL));
                    Check(step, d.key + " BTR refs = 1", () => Int("refs", Need(s).Refs, 1));
                    Check(step, d.key + (d.anon ? " BTR anonimowy *U" : " BTR nazwany " + NamePrefix + "nnn"), () =>
                    {
                        var x = Need(s);
                        bool ok = d.anon ? x.Anon && x.BtrName.StartsWith("*U", StringComparison.OrdinalIgnoreCase)
                                         : !x.Anon && x.BtrName.StartsWith(NamePrefix, StringComparison.Ordinal);
                        return True(ok, "BTR=" + x.BtrName + " IsAnonymous=" + x.Anon);
                    });
                    Check(step, d.key + " warstwa " + BarLayer, () => True(Need(s).Layer == BarLayer, "Layer=" + s.Layer));
                }

                Check(step, "PositionCounter.Peek bez zmian po wstawieniu", () =>
                {
                    int now = (int)Call(PositionCntType, "Peek", db);
                    if (_peekBaseline == null) throw new AssertFail("brak wartości bazowej");
                    return Int("Peek", now, _peekBaseline.Value);
                });

                WriteState(db);
            }
            catch (System.Exception ex) { Add("FAIL", step, "nieoczekiwany wyjątek kroku", ex.ToString()); }
            End(step);
        }

        // ---- STRETCH końca P1: okno (2900,-100)-(3100,100), przesunięcie 502,0 → L = R5(3502) = 3500 ----
        [CommandMethod("RC_E1_V_STRETCH_END", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void VStretchEnd()
        {
            const string step = "V_STRETCH_END";
            Verify(step, db =>
            {
                var s = Read(db, "P1");
                CheckCommon(step, s);
                Check(step, "P1 LengthA = R5(3000+502)", () => Num("LengthA", Need(s).L, R5(3000 + 502)));
                Check(step, "P1 początek bez zmian", () => Pt("początek", s.Start, new Point3d(0, 0, 0)));
                Check(step, "P1 koniec", () => Pt("koniec", s.End, new Point3d(3500, 0, 0)));
                CheckSameBtr(step, s, SetupOf("P1"), 1);
            });
        }

        // ---- STRETCH początku P1: okno (-100,-100)-(100,100), przesunięcie 303,0 → L = R5(3500-303) = 3195, koniec stoi ----
        [CommandMethod("RC_E1_V_STRETCH_START", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void VStretchStart()
        {
            const string step = "V_STRETCH_START";
            Verify(step, db =>
            {
                var prev = Last("P1");
                var s = Read(db, "P1");
                CheckCommon(step, s);
                double expL = R5(prev.L - 303);
                Check(step, "P1 LengthA = R5(L-303)", () => Num("LengthA", Need(s).L, expL) + " (oczekiwano 3195)");
                Check(step, "P1 koniec bez zmian", () => Pt("koniec", Need(s).End, prev.End));
                Check(step, "P1 początek przesunięty o L-nowaL wzdłuż osi", () =>
                    Pt("początek", Need(s).Start, prev.Start + prev.Dir * (prev.L - expL)));
                CheckSameBtr(step, s, SetupOf("P1"), 1);
            });
        }

        // ---- _.UNDO 1 po STRETCH początku → stan po STRETCH końca ----
        [CommandMethod("RC_E1_V_UNDO1", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void VUndo1()
        {
            const string step = "V_UNDO1";
            Verify(step, db =>
            {
                var s = Read(db, "P1");
                CheckCommon(step, s);
                Check(step, "P1 po UNDO 1: LengthA = 3500 (stan po STRETCH końca)", () =>
                {
                    var x = Need(s);
                    if (Math.Abs(x.L - 3195) < Tol)
                        throw new AssertFail("LengthA=3195 — UNDO 1 nic nie cofnęło (może cofnęło komendę weryfikacji RC_E1_V_*?)");
                    return Num("LengthA", x.L, 3500);
                });
                Check(step, "P1 po UNDO 1: początek (0,0)", () => Pt("początek", Need(s).Start, new Point3d(0, 0, 0)));
                Check(step, "P1 po UNDO 1: koniec (3500,0)", () => Pt("koniec", Need(s).End, new Point3d(3500, 0, 0)));
                CheckSameBtr(step, s, SetupOf("P1"), 1);
            });
        }

        // ---- drugie _.UNDO 1 → stan początkowy (wykrywa „2× Ctrl+Z na jedną edycję”) ----
        [CommandMethod("RC_E1_V_UNDO2", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void VUndo2()
        {
            const string step = "V_UNDO2";
            Verify(step, db =>
            {
                var s = Read(db, "P1");
                CheckCommon(step, s);
                Check(step, "P1 po 2× UNDO 1: LengthA = 3000 (stan z SETUP)", () =>
                {
                    var x = Need(s);
                    if (Math.Abs(x.L - 3500) < Tol)
                        throw new AssertFail("LengthA=3500 — drugie UNDO nie cofnęło STRETCH końca (zapis długości w osobnej grupie undo?)");
                    return Num("LengthA", x.L, 3000);
                });
                Check(step, "P1 po 2× UNDO 1: początek (0,0)", () => Pt("początek", Need(s).Start, new Point3d(0, 0, 0)));
                Check(step, "P1 po 2× UNDO 1: koniec (3000,0)", () => Pt("koniec", Need(s).End, new Point3d(3000, 0, 0)));
                CheckSameBtr(step, s, SetupOf("P1"), 1);
            });
        }

        // ---- _.REDO → stan po STRETCH końca ----
        [CommandMethod("RC_E1_V_REDO", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void VRedo()
        {
            const string step = "V_REDO";
            Verify(step, db =>
            {
                var s = Read(db, "P1");
                CheckCommon(step, s);
                Check(step, "P1 po REDO: LengthA = 3500", () => Num("LengthA", Need(s).L, 3500)
                    + (Math.Abs(s.L - 3000) < Tol ? " (REDO nie zadziałało — może przez komendę RC_E1_V_* między UNDO a REDO)" : ""));
                Check(step, "P1 po REDO: początek (0,0)", () => Pt("początek", Need(s).Start, new Point3d(0, 0, 0)));
                Check(step, "P1 po REDO: koniec (3500,0)", () => Pt("koniec", Need(s).End, new Point3d(3500, 0, 0)));
                CheckSameBtr(step, s, SetupOf("P1"), 1);
            });
        }

        // ---- STRETCH całego P1: okno (-200,-100)-(3700,100), przesunięcie 100,-200 → tylko przesunięcie ----
        [CommandMethod("RC_E1_V_STRETCH_ALL", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void VStretchAll()
        {
            const string step = "V_STRETCH_ALL";
            Verify(step, db =>
            {
                var prev = Last("P1");
                var s = Read(db, "P1");
                var off = new Vector3d(100, -200, 0);
                CheckCommon(step, s);
                Check(step, "P1 LengthA bez zmian", () => Num("LengthA", Need(s).L, prev.L));
                Check(step, "P1 początek przesunięty o (100,-200)", () => Pt("początek", Need(s).Start, prev.Start + off));
                Check(step, "P1 koniec przesunięty o (100,-200)", () => Pt("koniec", Need(s).End, prev.End + off));
                Check(step, "P1 kierunek bez zmian", () => Vec("kierunek", Need(s).Dir, prev.Dir));
                CheckSameBtr(step, s, SetupOf("P1"), 1);
            });
        }

        // ---- _.COPY P4 o (0,1000) → kopia ze wspólnym BTR ----
        [CommandMethod("RC_E1_V_COPY", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void VCopy()
        {
            const string step = "V_COPY";
            Verify(step, db =>
            {
                var p4 = Read(db, "P4");
                CheckCommon(step, p4);
                Check(step, "P4 bez zmian (L, początek)", () =>
                    Num("LengthA", Need(p4).L, 3000) + "; " + Pt("początek", p4.Start, new Point3d(0, 8000, 0)));

                Check(step, "kopia P4 odnaleziona (nowy pręt V2 w (0,9000))", () => FindNew(db, "P4C", new Point3d(0, 9000, 0)));
                var c = Read(db, "P4C");
                CheckCommon(step, c);
                Check(step, "P4C LengthA = 3000", () => Num("LengthA", Need(c).L, 3000));
                Check(step, "P4C koniec (3000,9000)", () => Pt("koniec", Need(c).End, new Point3d(3000, 9000, 0)));
                Check(step, "P4C wspólny BTR z P4 (refs = 2)", () =>
                    True(Need(c).BtrHandle == Need(p4).BtrHandle, "BTR kopii=" + c.BtrName + " BTR P4=" + p4.BtrName)
                    + "; " + Int("refs", c.Refs, 2));
                WriteState(db);
            });
        }

        // ---- STRETCH końca kopii: okno (2900,8900)-(3100,9100), przesunięcie 1000,0 → kopia L=4000, własny BTR ----
        [CommandMethod("RC_E1_V_COPY_STRETCH", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void VCopyStretch()
        {
            const string step = "V_COPY_STRETCH";
            Verify(step, db =>
            {
                var c  = Read(db, "P4C");
                var p4 = Read(db, "P4");
                CheckCommon(step, c);
                CheckCommon(step, p4);
                Check(step, "P4C LengthA = R5(3000+1000)", () => Num("LengthA", Need(c).L, 4000));
                Check(step, "P4C początek bez zmian", () => Pt("początek", c.Start, new Point3d(0, 9000, 0)));
                Check(step, "P4C koniec (4000,9000)", () => Pt("koniec", c.End, new Point3d(4000, 9000, 0)));
                Check(step, "P4C ma własny BTR (copy-on-write)", () =>
                {
                    var x = Need(c); var o = Need(p4);
                    return True(x.BtrHandle != o.BtrHandle && !x.Anon && x.BtrName.StartsWith(NamePrefix, StringComparison.Ordinal),
                                "BTR kopii=" + x.BtrName + " BTR P4=" + o.BtrName) + "; " + Int("refs kopii", x.Refs, 1);
                });
                Check(step, "P4 (oryginał) bez zmian: L, geometria, oś", () =>
                    Num("LengthA", Need(p4).L, 3000) + "; " + Num("geometria maxX", p4.GeoMaxX, 3000)
                    + "; " + Pt("początek", p4.Start, new Point3d(0, 8000, 0)) + "; " + Pt("koniec", p4.End, new Point3d(3000, 8000, 0)));
                CheckSameBtr(step, p4, SetupOf("P4"), 1);
            });
        }

        // ---- _.MIRROR P2 względem x=3000, bez usuwania → odbity pręt, oś przez BlockTransform ----
        [CommandMethod("RC_E1_V_MIRROR", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void VMirror()
        {
            const string step = "V_MIRROR";
            Verify(step, db =>
            {
                var p2 = Read(db, "P2");
                CheckCommon(step, p2);
                var p2s = SetupOf("P2");
                Check(step, "P2 (źródło) bez zmian", () =>
                    Num("LengthA", Need(p2).L, p2s.L) + "; " + Pt("początek", p2.Start, p2s.Start) + "; " + Pt("koniec", p2.End, p2s.End));

                var expStart = MirrorX(p2s.Start, 3000);
                var expEnd   = MirrorX(p2s.End, 3000);
                Check(step, "odbicie P2 odnalezione (nowy pręt V2 w " + P(expStart) + ")", () => FindNew(db, "P2M", expStart));
                var m = Read(db, "P2M");
                CheckCommon(step, m);
                Check(step, "P2M LengthA = LengthA P2", () => Num("LengthA", Need(m).L, p2s.L));
                Check(step, "P2M początek osi = odbicie początku P2", () => Pt("początek", m.Start, expStart));
                Check(step, "P2M koniec osi = odbicie końca P2", () => Pt("koniec", m.End, expEnd));
                Check(step, "P2M BlockTransform odbity (iloczyn skal < 0) — informacyjnie", () =>
                    "Mirrored=" + m.Mirrored + " Rotation=" + F(m.Rotation * 180 / Math.PI) + "°");
                Check(step, "P2M wspólny BTR z P2 (refs = 2)", () =>
                    True(Need(m).BtrHandle == Need(p2).BtrHandle, "BTR odbicia=" + m.BtrName + " BTR P2=" + p2.BtrName)
                    + "; " + Int("refs", m.Refs, 2));
                WriteState(db);
            });
        }

        // ---- STRETCH końca odbicia: okno (3900,3900)-(4100,4100), przesunięcie -300,300 → na zewnątrz, L = R5(2828+424.26) = 3250 ----
        [CommandMethod("RC_E1_V_MIRROR_STRETCH", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void VMirrorStretch()
        {
            const string step = "V_MIRROR_STRETCH";
            Verify(step, db =>
            {
                var prev = Last("P2M");
                var m  = Read(db, "P2M");
                var p2 = Read(db, "P2");
                CheckCommon(step, m);
                CheckCommon(step, p2);
                double dot = new Vector3d(-300, 300, 0).DotProduct(prev.Dir);
                double expL = R5(prev.L + dot);
                Check(step, "P2M wydłużony na zewnątrz: LengthA = R5(L + rzut)", () =>
                    Num("LengthA", Need(m).L, expL) + " (rzut=" + F(dot) + ", oczekiwano 3250)");
                Check(step, "P2M początek bez zmian", () => Pt("początek", m.Start, prev.Start));
                Check(step, "P2M kierunek bez zmian", () => Vec("kierunek", m.Dir, prev.Dir));
                Check(step, "P2M koniec = początek + L·kierunek", () => Pt("koniec", m.End, prev.Start + prev.Dir * expL));
                Check(step, "P2M ma własny BTR (copy-on-write)", () =>
                    True(Need(m).BtrHandle != Need(p2).BtrHandle, "BTR odbicia=" + m.BtrName + " BTR P2=" + p2.BtrName)
                    + "; " + Int("refs odbicia", m.Refs, 1));
                Check(step, "P2 (źródło) bez zmian", () =>
                    Num("LengthA", Need(p2).L, SetupOf("P2").L) + "; " + Pt("koniec", p2.End, SetupOf("P2").End));
                CheckSameBtr(step, p2, SetupOf("P2"), 1);
            });
        }

        // ---- STRETCH końca P2 prostopadle do osi: okno (1900,3900)-(2100,4100), przesunięcie 100,-100 ----
        [CommandMethod("RC_E1_V_PERP", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void VPerp()
        {
            const string step = "V_PERP";
            Verify(step, db =>
            {
                var prev = Last("P2");
                var s = Read(db, "P2");
                CheckCommon(step, s);
                double dot = new Vector3d(100, -100, 0).DotProduct(prev.Dir);
                double expL = R5(prev.L + dot);
                Check(step, "P2 LengthA = R5(L + rzut na oś) (rzut ≈ 0)", () =>
                    Num("LengthA", Need(s).L, expL) + " (rzut=" + F(dot) + ", L przed=" + F(prev.L) + ")");
                Check(step, "P2 kierunek bez zmian (brak obrotu)", () => Vec("kierunek", Need(s).Dir, prev.Dir));
                Check(step, "P2 początek bez zmian", () => Pt("początek", s.Start, prev.Start));
                Check(step, "P2 koniec na osi", () => Pt("koniec", s.End, prev.Start + prev.Dir * expL));
                CheckSameBtr(step, s, SetupOf("P2"), 1);
            });
        }

        // ---- STRETCH końca P3 (anonimowy) o -2950 → L = 50 = minimum max(2d, 50) dla H12 → przyjęte ----
        // (etap 3a: reguła wzorca TryEndStretch zamiast dawnego „min. 100”; odcinek < 50 byłby odrzucony)
        [CommandMethod("RC_E1_V_MIN", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void VMin()
        {
            const string step = "V_MIN";
            Verify(step, db =>
            {
                var s = Read(db, "P3");
                CheckCommon(step, s);
                Check(step, "P3 LengthA = minimum max(2d, 50) = 50", () => Num("LengthA", Need(s).L, 50));
                Check(step, "P3 początek bez zmian", () => Pt("początek", s.Start, new Point3d(0, 6000, 0)));
                Check(step, "P3 koniec (50,6000)", () => Pt("koniec", s.End, new Point3d(50, 6000, 0)));
                Check(step, "P3 BTR nadal anonimowy", () => True(Need(s).Anon, "BTR=" + s.BtrName));
                CheckSameBtr(step, s, SetupOf("P3"), 1);
            });
        }

        // ---- Zapis DWG obok DLL i odczyt w bocznej Database ----
        [CommandMethod("RC_E1_SAVE_REOPEN", CommandFlags.Modal)]
        public void SaveReopen()
        {
            const string step = "SAVE_REOPEN";
            Begin(step);
            var db = ActiveDb();
            string path = Path.Combine(OutDir, SaveFile);
            try
            {
                bool saved = false;
                Check(step, "SaveAs DWG", () =>
                {
                    if (File.Exists(path)) File.Delete(path);
                    db.SaveAs(path, DwgVersion.Current);
                    saved = File.Exists(path);
                    return True(saved, path + " rozmiar=" + (saved ? new FileInfo(path).Length.ToString(Inv) : "-"));
                });

                if (saved)
                {
                    using (var side = new Database(false, true))
                    {
                        bool read = false;
                        Check(step, "ReadDwgFile w bocznej Database", () =>
                        {
                            side.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
                            side.CloseInput(true);
                            read = true;
                            return "OK";
                        });
                        if (read)
                        {
                            foreach (var key in _handles.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList())
                            {
                                var live = Read(db, key);
                                var s = Read(side, key, remember: false);
                                CheckCommon(step, s, "(DWG) ");
                                Check(step, key + " (DWG) LengthA = stan w rysunku", () => Num("LengthA", Need(s).L, Need(live).L));
                                Check(step, key + " (DWG) oś = stan w rysunku", () =>
                                    Pt("początek", s.Start, live.Start) + "; " + Pt("koniec", s.End, live.End));
                                Check(step, key + " (DWG) BTR = stan w rysunku", () =>
                                {
                                    bool ok = s.Anon == live.Anon && (s.Anon || s.BtrName == live.BtrName);
                                    return True(ok, "BTR DWG=" + s.BtrName + " (anon=" + s.Anon + "), rysunek=" + live.BtrName + " (anon=" + live.Anon + ")")
                                           + "; " + Int("refs", s.Refs, live.Refs);
                                });
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex) { Add("FAIL", step, "nieoczekiwany wyjątek kroku", ex.ToString()); }
            End(step);
        }

        // ---- Izolacja: pręt testowy poza numeracją ----
        [CommandMethod("RC_E1_ISOLATION", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Isolation()
        {
            const string step = "ISOLATION";
            Verify(step, db =>
            {
                Check(step, "SingleBarEngine.ExtractPosNr(\"H12-T\") = 0", () =>
                    Int("ExtractPosNr", (int)Call(SingleBarType, "ExtractPosNr", TestMark), 0));
                Check(step, "PositionCounter.Peek (odczyt) = wartość sprzed wstawienia prętów", () =>
                {
                    if (_peekBaseline == null) throw new AssertFail("brak wartości bazowej z RC_E1_SETUP");
                    return Int("Peek", (int)Call(PositionCntType, "Peek", db), _peekBaseline.Value);
                });
                Check(step, "PositionCounter.GetUsedPositionNumbers: pręty testowe nie zajmują numerów", () =>
                {
                    var used = (IEnumerable)Call(PositionCntType, "GetUsedPositionNumbers", db);
                    var list = used.Cast<object>().Select(o => Convert.ToString(o, Inv)).ToList();
                    return True(list.Count == 0, "użyte=[" + string.Join(",", list) + "]");
                });
                Check(step, "liczba prętów V2 w modelu = liczba śledzonych", () =>
                    Int("pręty V2", AllV2(db).Count, _handles.Count));
            });
        }

        // ---- Raport końcowy ----
        [CommandMethod("RC_E1_REPORT", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Report()
        {
            Add("SKIP", "ARRAY", "ARRAY nieasocjacyjny 3×1",
                "pominięty — składnia -ARRAY/ARRAYRECT w BricsCAD V25 w skrypcie niepewna (test ręczny T7)");
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
        // Wspólne asercje
        // ================================================================

        /// <summary>Niezmienniki pręta testowego: XData v2, Mark, geometria BTR = LengthA, brak XData w BTR, oś = obrys w WCS.</summary>
        private static void CheckCommon(string step, Snap s, string tag = "")
        {
            string k = s.Key + " " + tag;
            Check(step, k + "istnieje", () => Need(s).Handle);
            if (!s.Found) return;
            Check(step, k + "IsV2 (TestBarBlockEngine)", () => True(s.IsV2, "IsV2=" + s.IsV2));
            Check(step, k + "[14] = 2 (Int16)", () => True(s.VerOk, "[14]=" + s.VerDesc));
            Check(step, k + "długość rekordu XData = " + XDataLen, () => Int("długość rekordu", s.RecLen, XDataLen));
            Check(step, k + "Mark = " + TestMark, () => True(s.HasBar && s.Mark == TestMark, "Mark=" + s.Mark));
            // Plan W4 (W4g, BS 8666:2020 kl. 7.2): prosty 00 — długość całkowita = A w górę do 25 mm (A bez zmian)
            Check(step, k + "TotalLength = LengthA w górę do 25 mm (W4g)", () => Num("TotalLength", s.TotalL, Math.Ceiling(s.L / 25.0 - 1e-9) * 25.0));
            Check(step, k + "geometria BTR: X od 0 do LengthA", () =>
                True(s.GeoCount > 0, "encje BTR=" + s.GeoCount)
                + "; " + Num("minX", s.GeoMinX, 0) + "; " + Num("maxX", s.GeoMaxX, s.L));
            Check(step, k + "brak XData na encjach w BTR", () => Int("encje BTR z XData", s.GeoXData, 0));
            Check(step, k + "LineWeight000 na BlockReference", () => True(s.Lw == LineWeight.LineWeight000, "LineWeight=" + s.Lw));
            Check(step, k + "obrys w WCS zgodny z osią z XData (BlockTransform)", () =>
            {
                if (!s.HasExt) throw new AssertFail("brak GeometricExtents");
                double minX = Math.Min(s.Start.X, s.End.X), maxX = Math.Max(s.Start.X, s.End.X);
                double minY = Math.Min(s.Start.Y, s.End.Y), maxY = Math.Max(s.Start.Y, s.End.Y);
                bool ok = Math.Abs(s.ExtMin.X - minX) <= HalfBarTol && Math.Abs(s.ExtMax.X - maxX) <= HalfBarTol
                       && Math.Abs(s.ExtMin.Y - minY) <= HalfBarTol && Math.Abs(s.ExtMax.Y - maxY) <= HalfBarTol;
                return True(ok, "extents " + P(s.ExtMin) + "–" + P(s.ExtMax) + ", oś " + P(s.Start) + "→" + P(s.End));
            });
        }

        /// <summary>Ten sam BTR co w SETUP (bez copy-on-write) i oczekiwana liczba referencji.</summary>
        private static void CheckSameBtr(string step, Snap s, Snap setup, int refs)
        {
            Check(step, s.Key + " BTR ten sam co po SETUP, refs = " + refs, () =>
                True(Need(s).BtrHandle == setup.BtrHandle, "BTR=" + s.BtrName + " (SETUP: " + setup.BtrName + ")")
                + "; " + Int("refs", s.Refs, refs));
        }

        // ================================================================
        // Odczyt prętów
        // ================================================================

        private static Snap Read(Database db, string key, bool remember = true)
        {
            var s = new Snap { Key = key };
            if (!_handles.TryGetValue(key, out var hex)) { s.Err = "brak uchwytu dla " + key; return s; }
            s.Handle = hex;
            try
            {
                var id = db.GetObjectId(false, new Handle(Convert.ToInt64(hex, 16)), 0);
                if (id.IsNull)   { s.Err = "uchwyt " + hex + " nie istnieje"; return s; }
                if (id.IsErased) { s.Err = "uchwyt " + hex + " usunięty"; return s; }
                // Tylko odczyt: OpenCloseTransaction bez Commit
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var br = tr.GetObject(id, OpenMode.ForRead, false) as BlockReference;
                    if (br == null) { s.Err = "uchwyt " + hex + " to nie BlockReference"; return s; }
                    Fill(tr, br, s);
                }
            }
            catch (System.Exception ex) { s.Found = false; s.Err = "wyjątek odczytu: " + ex.Message; }
            if (remember && s.Found) _last[key] = s;
            return s;
        }

        private static void Fill(Transaction tr, BlockReference br, Snap s)
        {
            s.Found  = true;
            s.IsV2   = (bool)Call(EngineType, "IsV2", br);

            using (var rb = br.GetXDataForApplication(XApp))
            {
                var v = rb?.AsArray() ?? new TypedValue[0];
                s.RecLen = v.Length;
                if (v.Length > 14)
                {
                    s.VerDesc = "typ " + v[14].TypeCode.ToString(Inv) + " wartość " + Convert.ToString(v[14].Value, Inv);
                    s.VerOk   = v[14].TypeCode == (short)DxfCode.ExtendedDataInteger16 && v[14].Value is short sv && sv == 2;
                }
            }

            var bar = Call(SingleBarType, "ReadBarXData", br);
            if (bar != null)
            {
                s.HasBar = true;
                s.Mark   = (string)Prop(bar, "Mark");
                s.L      = (double)Prop(bar, "LengthA");
                s.TotalL = (double)Prop(bar, "TotalLength");
            }

            var xf = br.BlockTransform;
            s.Start    = Point3d.Origin.TransformBy(xf);
            s.End      = new Point3d(s.L, 0, 0).TransformBy(xf);
            var ax     = Vector3d.XAxis.TransformBy(xf);
            s.Dir      = ax.Length > 1e-12 ? ax.GetNormal() : Vector3d.XAxis;
            s.Position = br.Position;
            s.Rotation = br.Rotation;
            var sc     = br.ScaleFactors;
            s.Mirrored = sc.X * sc.Y * sc.Z < 0;
            s.Lw       = br.LineWeight;
            s.Layer    = br.Layer;
            try
            {
                var ext = br.GeometricExtents;
                s.ExtMin = ext.MinPoint; s.ExtMax = ext.MaxPoint; s.HasExt = true;
            }
            catch (System.Exception ex) { s.Err = "GeometricExtents: " + ex.Message; }

            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            s.BtrName   = btr.Name;
            s.BtrHandle = btr.Handle.ToString();
            s.Anon      = btr.IsAnonymous;
            s.Refs      = btr.GetBlockReferenceIds(true, true).Count;

            double minX = double.MaxValue, maxX = double.MinValue;
            foreach (ObjectId eid in btr)
            {
                if (eid.IsNull || eid.IsErased) continue;
                if (!(tr.GetObject(eid, OpenMode.ForRead) is Entity ent)) continue;
                s.GeoCount++;
                using (var xd = ent.XData) if (xd != null) s.GeoXData++;
                var e = ent.GeometricExtents;
                minX = Math.Min(minX, e.MinPoint.X);
                maxX = Math.Max(maxX, e.MaxPoint.X);
            }
            if (s.GeoCount > 0) { s.GeoMinX = minX; s.GeoMaxX = maxX; }
        }

        /// <summary>Uchwyty wszystkich prętów V2 w przestrzeni modelu.</summary>
        private static List<string> AllV2(Database db)
        {
            var list = new List<string>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    if (tr.GetObject(id, OpenMode.ForRead) is BlockReference br && (bool)Call(EngineType, "IsV2", br))
                        list.Add(br.Handle.ToString());
                }
            }
            return list;
        }

        /// <summary>Nowy (nieśledzony) pręt V2 z początkiem osi w expStart → zapamiętany pod kluczem key.</summary>
        private static string FindNew(Database db, string key, Point3d expStart)
        {
            var known = new HashSet<string>(_handles.Values, StringComparer.OrdinalIgnoreCase);
            var fresh = AllV2(db).Where(h => !known.Contains(h)).ToList();
            var hits = new List<string>();
            var info = new List<string>();
            foreach (var h in fresh)
            {
                _handles[key] = h;
                var s = Read(db, key, remember: false);
                info.Add(h + "@" + P(s.Start));
                if (s.Found && s.Start.DistanceTo(expStart) < 1.0) hits.Add(h);
            }
            _handles.Remove(key);
            if (hits.Count != 1)
                throw new AssertFail($"dopasowań: {hits.Count}; nowe pręty V2: [{string.Join(", ", info)}]");
            _handles[key] = hits[0];
            Read(db, key);   // zapamiętaj jako ostatni stan
            return "uchwyt=" + hits[0] + "; nowe pręty V2: " + fresh.Count.ToString(Inv);
        }

        // ================================================================
        // Infrastruktura: refleksja, wyniki, pliki
        // ================================================================

        private static Assembly _plugin;

        /// <summary>Zestaw BricsCadRc wczytany NETLOAD-em (preferowany bin\NaSmartRc.dll).</summary>
        private static Assembly Plugin
        {
            get
            {
                if (_plugin != null) return _plugin;
                var all = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "NaSmartRc").ToList();
                if (all.Count == 0) throw new InvalidOperationException("Wtyczka NaSmartRc nie jest wczytana (NETLOAD bin\\NaSmartRc.dll).");
                _plugin = all.FirstOrDefault(a => Loc(a).EndsWith("\\bin\\NaSmartRc.dll", StringComparison.OrdinalIgnoreCase)) ?? all[0];
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
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("PlanCStage1Tests.Msg: " + ex); }
        }

        private static void Begin(string step)
        {
            _stepStart = _results.Count;
            if (!_executed.Contains(step)) _executed.Add(step);
        }

        private static void End(string step)
        {
            var lines = _results.Skip(_stepStart).ToList();
            Msg("\n" + Prefix + " " + step + ":\n" + string.Join("\n", lines) + "\n");
            Flush(null);
        }

        private static void Verify(string step, Action<Database> body)
        {
            Begin(step);
            try { body(ActiveDb()); }
            catch (System.Exception ex) { Add("FAIL", step, "nieoczekiwany wyjątek kroku", ex.ToString()); }
            End(step);
        }

        private static void Check(string step, string name, Func<string> body)
        {
            try { Add("PASS", step, name, body() ?? ""); }
            catch (AssertFail af) { Add("FAIL", step, name, af.Message); }
            catch (System.Exception ex) { Add("FAIL", step, name, "wyjątek: " + ex); }
        }

        private static void Add(string status, string step, string name, string details)
            => _results.Add($"{status} | {step} | {name} | {(details ?? "").Replace("\r", " ").Replace("\n", " ")}");

        /// <summary>Zapis wyników (po każdym kroku, żeby przy zawieszeniu skryptu zostały wyniki częściowe).</summary>
        private static void Flush(IEnumerable<string> tail)
        {
            try
            {
                var lines = new List<string>
                {
                    "PLAN C ETAP 1 — testy dymne BricsCAD (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")",
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

        private static void WriteState(Database db)
        {
            try
            {
                var lines = new List<string>();
                foreach (var kv in _handles.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    var s = Read(db, kv.Key, remember: false);
                    lines.Add($"{kv.Key}={kv.Value};BTR={s.BtrName};L={F(s.L)};start={P(s.Start)};end={P(s.End)}");
                }
                File.WriteAllLines(Path.Combine(OutDir, StateFile), lines, new UTF8Encoding(false));
            }
            catch (System.Exception ex) { Add("FAIL", "STATE", "zapis pliku stanu", ex.ToString()); }
        }

        private static Snap SetupOf(string key)
        {
            if (_setup.TryGetValue(key, out var s) && s.Found) return s;
            throw new AssertFail("brak stanu " + key + " z RC_E1_SETUP");
        }

        private static Snap Last(string key)
        {
            if (_last.TryGetValue(key, out var s) && s.Found) return s;
            throw new AssertFail("brak poprzedniego odczytu " + key);
        }

        // ---- asercje elementarne (rzucają AssertFail, zwracają opis) ----

        private static Snap Need(Snap s)
        {
            if (s == null || !s.Found) throw new AssertFail("brak pręta " + (s?.Key ?? "?") + ": " + (s?.Err ?? "?"));
            return s;
        }

        private static string Num(string what, double act, double exp, double tol = Tol)
        {
            if (double.IsNaN(act) || Math.Abs(act - exp) > tol)
                throw new AssertFail($"{what}: jest {F(act)}, oczekiwano {F(exp)}");
            return $"{what}={F(act)}";
        }

        private static string Int(string what, int act, int exp)
        {
            if (act != exp) throw new AssertFail($"{what}: jest {act.ToString(Inv)}, oczekiwano {exp.ToString(Inv)}");
            return $"{what}={act.ToString(Inv)}";
        }

        private static string Pt(string what, Point3d act, Point3d exp, double tol = Tol)
        {
            if (act.DistanceTo(exp) > tol)
                throw new AssertFail($"{what}: jest {P(act)}, oczekiwano {P(exp)}");
            return $"{what}={P(act)}";
        }

        private static string Vec(string what, Vector3d act, Vector3d exp, double tol = 1e-6)
        {
            if ((act - exp).Length > tol)
                throw new AssertFail($"{what}: jest {V(act)}, oczekiwano {V(exp)}");
            return $"{what}={V(act)}";
        }

        private static string True(bool cond, string detail)
        {
            if (!cond) throw new AssertFail(detail);
            return detail;
        }

        // ---- pomocnicze ----

        /// <summary>
        /// Reguła długości (niezależna kopia): zaokrąglenie do 5 mm. Minimum max(2d, 50) odrzuca zmianę
        /// (etap 3a, jak wzorzec TryEndStretch) — w krokach z R5 długości są daleko od minimum.
        /// </summary>
        private static double R5(double x) => Math.Round(x / 5.0, MidpointRounding.AwayFromZero) * 5.0;

        private static Point3d MirrorX(Point3d p, double x0) => new Point3d(2 * x0 - p.X, p.Y, p.Z);

        private static string F(double d) => d.ToString("0.###", Inv);
        private static string P(Point3d p) => "(" + F(p.X) + "," + F(p.Y) + ")";
        private static string V(Vector3d v) => "(" + v.X.ToString("0.######", Inv) + "," + v.Y.ToString("0.######", Inv) + ")";
    }
}
