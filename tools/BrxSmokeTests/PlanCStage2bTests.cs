// PLAN C ETAP 2b — testy dymne: BBS, PositionReconciler, etykiety i numeracja widzą pręt-blok v2 (BricsCAD V25).
// Uruchamiane skryptem planc-e2b.scr (run-planc-e2b.ps1). S5–S8 z docs/PLAN_C_etap2.md (sekcja 6.1).
//
// Dostęp do wtyczki WYŁĄCZNIE przez refleksję na zestawie NaSmartRc wczytanym NETLOAD-em — jak w etapach 1 i 2a.
// Numerowany pręt-blok tworzą tylko testy (1-POPRAWKI p.1): TestBarBlockEngine.Create → Mark H12-nn
// (PositionCounter.NextAuto) → BarXData.Write(formatVersion 2) → ZAWSZE etykieta (PlaceBarLabel + LabelHandle, p.2).
// Rozkład + opis bez dialogów i jigów: BarBlockEngine.GenerateFromBounds → AnnotationEngine.CreateLeader →
// BarBlockEngine.LinkAnnotation → AnnotationEngine.UpdateBarLabelCount (jak RC_DISTRIBUTION).

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
    public class PlanCStage2bTests
    {
        // ----------------------------------------------------------------
        // Stałe testu
        // ----------------------------------------------------------------

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const string ResultsFile = "planc-e2b-results.txt";
        private const string Prefix      = "[RC E2B]";
        private const string XApp        = "RC_SINGLE_BAR";
        private const int    HeadLen     = 14;     // [0]–[13]
        // W4d (runda 2): pręt z PlaceBar ma znacznik geometrii W4d — polilinia [14] = 1, [15] = 2021; pręt-blok [14] = 2, [15] = 2021
        private const int    PlacedPolyLen  = 16;
        private const int    V2Len       = 15;     // [0]–[13] + [14] wersja formatu
        private const short  TcInt16     = 1070;
        private const short  TcHandle    = 1005;
        private const double Spacing     = 200.0;
        private const double DistSpan    = 1000.0; // 1000 / 200 + 1 = 6 prętów w rozkładzie

        private const string SingleBarType  = "BricsCadRc.Core.SingleBarEngine";
        private const string EngineType     = "BricsCadRc.Core.TestBarBlockEngine";
        private const string BarEntityType  = "BricsCadRc.Core.BarEntity";
        private const string BarXDataType   = "BricsCadRc.Core.BarXData";
        private const string BarDataType    = "BricsCadRc.Core.BarData";
        private const string BarBlockType   = "BricsCadRc.Core.BarBlockEngine";
        private const string AnnotType      = "BricsCadRc.Core.AnnotationEngine";
        private const string CounterType    = "BricsCadRc.Core.PositionCounter";
        private const string ReconcilerType = "BricsCadRc.Core.PositionReconciler";
        private const string BbsReaderType  = "BricsCadRc.Core.BbsDrawingReader";

        private static readonly string[] ExpectedSteps = { "SETUP", "S5", "S6", "S7", "S8" };

        // ----------------------------------------------------------------
        // Stan sesji testowej
        // ----------------------------------------------------------------

        private static readonly List<string> _results  = new List<string>();
        private static readonly List<string> _executed = new List<string>();
        private static int _stepStart;
        private static int _s5Nr;                  // numer pręta-bloku z S5 (sprawdzany też w S8)

        private sealed class AssertFail : System.Exception
        {
            public AssertFail(string msg) : base(msg) { }
        }

        // ================================================================
        // Komendy
        // ================================================================

        [CommandMethod("RCSMOKE_E2B_SETUP", CommandFlags.Modal)]
        public void Setup()
        {
            _results.Clear();
            _executed.Clear();
            _s5Nr = 0;

            const string step = "SETUP";
            Run(step, db =>
            {
                Check(step, "wtyczka NaSmartRc wczytana (jedna kopia)", () =>
                {
                    var all = AppDomain.CurrentDomain.GetAssemblies()
                                       .Where(a => a.GetName().Name == "NaSmartRc").ToList();
                    var p = Plugin;
                    if (all.Count != 1)
                        throw new AssertFail($"wczytane kopie NaSmartRc: {all.Count} ({string.Join("; ", all.Select(Loc))})");
                    return "Location=" + Loc(p);
                });

                Check(step, "typy wtyczki używane w testach", () =>
                {
                    foreach (var tn in new[] { SingleBarType, EngineType, BarEntityType, BarXDataType, BarDataType, BarBlockType,
                                               AnnotType, CounterType, ReconcilerType, BbsReaderType })
                        Plugin.GetType(tn, true);
                    return "OK";
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

                Check(step, "rejestracja RegApp (SingleBarEngine, BarBlockEngine, AnnotationEngine)", () =>
                {
                    Call(SingleBarType, "EnsureAppIdRegistered", db);
                    Call(BarBlockType, "EnsureAppIdRegistered", db);
                    Call(AnnotType, "EnsureAppIdRegistered", db);
                    return "OK";
                });
            });
        }

        // ---- S5: v2 z numerem + etykieta + rozkład + opis → BbsDrawingReader.ReadBars: wiersz poz. nn, ilość, długość = A ----
        [CommandMethod("RCSMOKE_E2B_S5", CommandFlags.Modal)]
        public void S5()
        {
            const string step = "S5";
            Run(step, db =>
            {
                const double a = 3500.0;
                int nr = 0;
                ObjectId barId = ObjectId.Null, lblId = ObjectId.Null;

                Check(step, "pręt-blok v2 H12-nn (NextAuto) z etykietą", () =>
                {
                    nr = NextAuto(db);
                    var r = CreateNumberedV2(db, nr, a, new Point3d(0, 0, 0));
                    barId = r.Item1; lblId = r.Item2;
                    _s5Nr = nr;
                    return "nr=" + nr.ToString(Inv) + "; " + DescribeV2(db, barId, Mark(nr), lblId);
                });
                if (barId.IsNull || lblId.IsNull) return;

                // Długość widoku rozkładu (3000) ≠ A pręta (3500): ścieżka „rozkład bez pręta źródłowego”
                // dałaby długość 3000 — wiersz z długością 3500 dowodzi, że BBS czyta pręt-blok.
                ObjectId distId = ObjectId.Null;
                int count = 0;
                Check(step, "rozkład (GenerateFromBounds) + opis (CreateLeader) + LinkAnnotation", () =>
                {
                    var d = MakeDistribution(db, barId, nr, 0, -3000, 3000.0);
                    distId = d.Item1; count = d.Item3;
                    return "rozkład=" + distId.Handle + " opis=" + d.Item2.Handle + "; " + Int("ilość prętów", count, 6)
                         + "; " + True((bool)Call(BarBlockType, "IsAnnotAlive", db, d.Item2.Handle.Value.ToString("X8", Inv)),
                                       "opis żywy (IsAnnotAlive)");
                });
                if (distId.IsNull) return;

                Check(step, "UpdateBarLabelCount → etykieta pręta-bloku „n H12-nn”", () =>
                {
                    Call(AnnotType, "UpdateBarLabelCount", db, barId.Handle.Value.ToString("X8", Inv),
                         (string)Call(BarDataType, "FormatMark", 12, nr, Spacing, count));
                    return Eq("tekst etykiety", LabelText(db, lblId), count.ToString(Inv) + " " + Mark(nr));
                });

                Check(step, "BbsDrawingReader.ReadBars: wiersz poz. nn, ilość, długość = A", () =>
                {
                    var res = Call(BbsReaderType, "ReadBars", db);
                    var rows = ((IEnumerable)Prop(res, "Rows")).Cast<object>().ToList();
                    var warns = ((IEnumerable<string>)Prop(res, "Warnings")).ToList();
                    var mine = rows.Where(rw => (int)Prop(rw, "BarMark") == nr).ToList();
                    if (mine.Count != 1)
                        throw new AssertFail($"wiersze poz. {nr}: {mine.Count} (oczekiwano 1); wszystkie: {Rows(rows)}; ostrzeżenia: {string.Join(" | ", warns)}");
                    var row = mine[0];
                    var sb = new StringBuilder();
                    sb.Append(Eq("Diameter", Prop(row, "Diameter"), 12));
                    sb.Append("; ").Append(Eq("ShapeCode", Prop(row, "ShapeCode"), "00"));
                    sb.Append("; ").Append(Eq("NoEach", Prop(row, "NoEach"), count));
                    double len = (double)Prop(row, "Length");
                    if (double.IsNaN(len) || Math.Abs(len - a) > 0.5)
                        throw new AssertFail($"Length: jest {F(len)}, oczekiwano {F(a)} (A pręta-bloku)");
                    sb.Append("; Length=").Append(F(len));
                    var orphan = warns.FirstOrDefault(w => w.IndexOf("bez pręta źródłowego", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (orphan != null) throw new AssertFail("ostrzeżenie: " + orphan);
                    sb.Append("; Distributions=").Append(Convert.ToString(Prop(res, "Distributions"), Inv));
                    return sb.ToString();
                });
            });
        }

        // ---- S6: Reconciler — polilinia 05 L3000 + v2 05 L3500; zapis z v2 zachowuje [14] = 2 ----
        [CommandMethod("RCSMOKE_E2B_S6", CommandFlags.Modal)]
        public void S6()
        {
            const string step = "S6";
            Run(step, db =>
            {
                // ---- a) zmieniona polilinia, ten sam numer ma pręt-blok o innej długości → nowy numer ----
                int nrA = 0;
                ObjectId plId = ObjectId.Null, v2Id = ObjectId.Null;
                HashSet<int> usedBefore = null;
                Check(step, "a) polilinia H12-nn L3000 + pręt-blok v2 H12-nn L3500 (z etykietą)", () =>
                {
                    nrA = NextAuto(db);
                    plId = (ObjectId)Call(SingleBarType, "PlaceBar", db, NewBar(Mark(nrA), 3000), new Point3d(0, 10000, 0));
                    v2Id = CreateNumberedV2(db, nrA, 3500.0, new Point3d(0, 11000, 0)).Item1;
                    usedBefore = Used(db);
                    return "nr=" + nrA.ToString(Inv) + " polilinia=" + plId.Handle + " v2=" + v2Id.Handle;
                });
                if (!plId.IsNull && !v2Id.IsNull)
                {
                    Check(step, "a) Reconciler(polilinia) → nowy numer (konflikt z prętem-blokiem)", () =>
                    {
                        var newMark = (string)Call(ReconcilerType, "ReconcileAfterGeometryChange", db, plId);
                        if (newMark == null) throw new AssertFail("Reconciler zwrócił null — pręt-blok nie został uznany za konflikt");
                        int newNr = PosNr(newMark);
                        True(newNr != nrA, "numer bez zmian: " + newMark);
                        True(!usedBefore.Contains(newNr), "nowy numer " + newNr.ToString(Inv) + " był już zajęty");
                        var r = ReadRecord(db, plId);
                        return "nowy Mark=" + newMark + "; " + Int("długość rekordu polilinii", r.Length, PlacedPolyLen)
                             + "; " + Eq("[1] polilinii", r[1].Value, newMark);
                    });
                    Check(step, "a) pręt-blok bez zmian (Mark, 15 pól, [14] = 2)", () =>
                    {
                        var r = ReadRecord(db, v2Id);
                        return Int("długość rekordu", r.Length, V2Len) + "; " + Ver2(r) + "; " + Eq("[1]", r[1].Value, Mark(nrA));
                    });
                }

                // ---- b) Reconciler wywołany dla pręta-bloku → zapis przez BarEntity zachowuje [14] = 2 ----
                int nrB = 0;
                ObjectId plB = ObjectId.Null, v2B = ObjectId.Null, lblB = ObjectId.Null;
                TypedValue[] before = null;
                Check(step, "b) polilinia H12-mm L2750 + pręt-blok v2 H12-mm L3250 (z etykietą)", () =>
                {
                    nrB = NextAuto(db);
                    plB = (ObjectId)Call(SingleBarType, "PlaceBar", db, NewBar(Mark(nrB), 2750), new Point3d(0, 14000, 0));
                    var r = CreateNumberedV2(db, nrB, 3250.0, new Point3d(0, 15000, 0));
                    v2B = r.Item1; lblB = r.Item2;
                    before = ReadRecord(db, v2B);
                    return "nr=" + nrB.ToString(Inv) + "; " + DescribeV2(db, v2B, Mark(nrB), lblB);
                });
                if (!plB.IsNull && !v2B.IsNull && before != null)
                {
                    Check(step, "b) Reconciler(pręt-blok) → nowy Mark, 15 pól, [14] = Int16 2, [2]–[13] bez zmian", () =>
                    {
                        var newMark = (string)Call(ReconcilerType, "ReconcileAfterGeometryChange", db, v2B);
                        if (newMark == null) throw new AssertFail("Reconciler zwrócił null — pręt-blok nie jest obsługiwany");
                        True(PosNr(newMark) != nrB, "numer bez zmian: " + newMark);
                        var r = ReadRecord(db, v2B);
                        Int("długość rekordu", r.Length, V2Len);
                        Ver2(r);
                        Eq("[1]", r[1].Value, newMark);
                        for (int i = 2; i < HeadLen; i++) SameField(i, before[i], r[i]);
                        return "nowy Mark=" + newMark + "; pola=15; [14]=Int16 2; [13]=" + Desc(r[13]);
                    });
                    Check(step, "b) KindOf(pręt-blok po Reconcilerze) = Block; polilinia bez zmian", () =>
                    {
                        string k;
                        using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                        {
                            var kind = Call(BarEntityType, "KindOf", tr.GetObject(v2B, OpenMode.ForRead));
                            k = kind == null ? "null" : Convert.ToString(kind, Inv);
                        }
                        Eq("KindOf", k, "Block");
                        var rp = ReadRecord(db, plB);
                        return "KindOf=Block; " + Eq("[1] polilinii", rp[1].Value, Mark(nrB)) + "; " + Int("długość rekordu polilinii", rp.Length, PlacedPolyLen);
                    });
                }
            });
        }

        // ---- S7: UpdateBarLabelCount — polilinia z etykietą i 2 rozkładami → „n H12-nn” (logika wzorca) ----
        [CommandMethod("RCSMOKE_E2B_S7", CommandFlags.Modal)]
        public void S7()
        {
            const string step = "S7";
            Run(step, db =>
            {
                int nr = 0;
                ObjectId plId = ObjectId.Null, lblId = ObjectId.Null;
                Check(step, "polilinia H12-nn L4000 z etykietą (PlaceBar + PlaceBarLabel + LabelHandle)", () =>
                {
                    nr = NextAuto(db);
                    var at = new Point3d(0, 20000, 0);
                    plId = (ObjectId)Call(SingleBarType, "PlaceBar", db, NewBar(Mark(nr), 4000), at);
                    lblId = (ObjectId)Call(SingleBarType, "PlaceBarLabel", db, new Point3d(at.X + 2000, at.Y, 0),
                                           new Point3d(at.X + 1950, at.Y + 300, 0), Mark(nr), plId);
                    True(!lblId.IsNull, "PlaceBarLabel zwrócił Null");
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var e = (Entity)tr.GetObject(plId, OpenMode.ForWrite);
                        var bar = Call(SingleBarType, "ReadBarXData", e);
                        SetProp(bar, "LabelHandle", lblId.Handle.ToString());
                        Call(BarXDataType, "Write", e, bar, null);
                        tr.Commit();
                    }
                    var r = ReadRecord(db, plId);
                    return "nr=" + nr.ToString(Inv) + "; " + Int("długość rekordu", r.Length, PlacedPolyLen) + "; " + Tc("[13]", r[13], TcHandle)
                         + "; " + True(SameHandle(r[13].Value, lblId.Handle), "[13]=" + Desc(r[13]) + " etykieta=" + lblId.Handle);
                });
                if (plId.IsNull || lblId.IsNull) return;

                int total = 0;
                string distMark = null;
                Check(step, "2 rozkłady z opisami", () =>
                {
                    var d1 = MakeDistribution(db, plId, nr, 0, 17000, 4000.0);
                    var d2 = MakeDistribution(db, plId, nr, 6000, 17000, 4000.0, span: 1400.0);   // 1400 / 200 + 1 = 8
                    total = d1.Item3 + d2.Item3;
                    distMark = (string)Call(BarDataType, "FormatMark", 12, nr, Spacing, d1.Item3);
                    return Int("rozkład 1", d1.Item3, 6) + "; " + Int("rozkład 2", d2.Item3, 8);
                });
                if (total == 0) return;

                Check(step, "UpdateBarLabelCount(markOverride jak RC_DISTRIBUTION) → „n H12-nn”", () =>
                {
                    Call(AnnotType, "UpdateBarLabelCount", db, plId.Handle.Value.ToString("X8", Inv), distMark);
                    return Eq("tekst etykiety", LabelText(db, lblId), total.ToString(Inv) + " " + Mark(nr));
                });

                Check(step, "UpdateBarLabelCount(bez markOverride) → „n H12-nn” z Mark pręta", () =>
                {
                    Call(AnnotType, "UpdateBarLabelCount", db, plId.Handle.Value.ToString("X8", Inv), null);
                    return Eq("tekst etykiety", LabelText(db, lblId), total.ToString(Inv) + " " + Mark(nr));
                });
            });
        }

        // ---- S8: GetUsedPositionNumbers zawiera numer pręta-bloku v2 ----
        [CommandMethod("RCSMOKE_E2B_S8", CommandFlags.Modal)]
        public void S8()
        {
            const string step = "S8";
            Run(step, db =>
            {
                int nr = 0;
                ObjectId id = ObjectId.Null;
                Check(step, "pręt-blok v2 z nowym numerem, bez rozkładu (z etykietą)", () =>
                {
                    nr = NextAuto(db);
                    True(!Used(db).Contains(nr), "NextAuto zwrócił zajęty numer " + nr.ToString(Inv));
                    var r = CreateNumberedV2(db, nr, 2500.0, new Point3d(0, 30000, 0));
                    id = r.Item1;
                    return "nr=" + nr.ToString(Inv) + "; " + DescribeV2(db, id, Mark(nr), r.Item2);
                });
                if (id.IsNull) return;

                Check(step, "GetUsedPositionNumbers zawiera numer pręta-bloku (bez rozkładu)", () =>
                {
                    var used = Used(db);
                    return True(used.Contains(nr), "brak " + nr.ToString(Inv) + " w {" + string.Join(",", used.OrderBy(x => x)) + "}");
                });

                Check(step, "GetUsedPositionNumbers zawiera numer pręta-bloku z S5", () =>
                {
                    if (_s5Nr <= 0) throw new AssertFail("S5 nie utworzył pręta-bloku");
                    var used = Used(db);
                    return True(used.Contains(_s5Nr), "brak " + _s5Nr.ToString(Inv) + " w {" + string.Join(",", used.OrderBy(x => x)) + "}");
                });

                Check(step, "NextAuto pomija numer pręta-bloku", () =>
                {
                    int next = NextAuto(db);
                    return True(next != nr, "NextAuto = " + next.ToString(Inv) + " (numer pręta-bloku)") + " (NextAuto=" + next.ToString(Inv) + ")";
                });
            });
        }

        // ---- Raport końcowy ----
        [CommandMethod("RCSMOKE_E2B_REPORT", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Report()
        {
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
        // Wspólne kroki testowe
        // ================================================================

        private static string Mark(int nr) => (string)Call(BarDataType, "FormatMark", 12, nr, 0.0, 1);   // „H12-nn”

        private static int PosNr(string mark) => (int)Call(SingleBarType, "ExtractPosNr", mark);

        private static HashSet<int> Used(Database db) => (HashSet<int>)Call(CounterType, "GetUsedPositionNumbers", db);

        private static int NextAuto(Database db) => (int)Call(CounterType, "NextAuto", db, Used(db));

        /// <summary>
        /// Numerowany pręt-blok (tylko testy): TestBarBlockEngine.Create → Mark H12-nn → BarXData.Write(…, 2)
        /// → etykieta PlaceBarLabel z back-linkiem → LabelHandle (1005) → BarXData.Write(…, 2).
        /// </summary>
        private static Tuple<ObjectId, ObjectId> CreateNumberedV2(Database db, int nr, double a, Point3d at)
        {
            string mark = Mark(nr);
            var t = (ValueTuple<ObjectId, string, double>)Call(EngineType, "Create", db, at, new Point3d(at.X + a, at.Y, 0), false);
            var id = t.Item1;
            if (id.IsNull) throw new AssertFail("TestBarBlockEngine.Create zwrócił Null");
            if (Math.Abs(t.Item3 - a) > 1e-6) throw new AssertFail("długość pręta-bloku " + F(t.Item3) + ", oczekiwano " + F(a));

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var e = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                var bar = Call(SingleBarType, "ReadBarXData", e);
                SetProp(bar, "Mark", mark);
                Call(BarXDataType, "Write", e, bar, (short?)2);
                tr.Commit();
            }

            var lblId = (ObjectId)Call(SingleBarType, "PlaceBarLabel", db, new Point3d(at.X + a / 2, at.Y, 0),
                                       new Point3d(at.X + a / 2 - 50, at.Y + 300, 0), mark, id);
            if (lblId.IsNull) throw new AssertFail("PlaceBarLabel zwrócił Null");

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var e = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                var bar = Call(SingleBarType, "ReadBarXData", e);
                SetProp(bar, "LabelHandle", lblId.Handle.ToString());
                Call(BarXDataType, "Write", e, bar, (short?)2);
                tr.Commit();
            }
            return Tuple.Create(id, lblId);
        }

        /// <summary>Sprawdza pręt-blok: 15 pól, [14] = Int16 2, [1] = Mark, [13] = 1005 etykiety, KindOf = Block.</summary>
        private static string DescribeV2(Database db, ObjectId id, string mark, ObjectId lblId)
        {
            var r = ReadRecord(db, id);
            var sb = new StringBuilder();
            sb.Append(Int("długość rekordu", r.Length, V2Len)).Append("; ").Append(Ver2(r));
            sb.Append("; ").Append(Eq("[1]", r[1].Value, mark));
            sb.Append("; ").Append(Tc("[13]", r[13], TcHandle));
            sb.Append("; ").Append(True(SameHandle(r[13].Value, lblId.Handle), "[13]=" + Desc(r[13]) + " etykieta=" + lblId.Handle));
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var kind = Call(BarEntityType, "KindOf", tr.GetObject(id, OpenMode.ForRead));
                sb.Append("; ").Append(Eq("KindOf", kind == null ? "null" : Convert.ToString(kind, Inv), "Block"));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Rozkład + opis jak RC_DISTRIBUTION, bez dialogów i jigów: dane pręta źródłowego (ReadBarXData) + SourceBarHandle,
        /// pręty poziome od (x0, y0), długość widoku viewLen, zakres span co 200 → GenerateFromBounds → CreateLeader → LinkAnnotation.
        /// Zwraca (rozkład, opis, EffectiveCount z XData rozkładu).
        /// </summary>
        private static Tuple<ObjectId, ObjectId, int> MakeDistribution(Database db, ObjectId barId, int nr, double x0, double y0,
                                                                        double viewLen, double span = DistSpan)
        {
            object bar;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                bar = Call(SingleBarType, "ReadBarXData", tr.GetObject(barId, OpenMode.ForRead));
            if (bar == null) throw new AssertFail("pręt źródłowy bez RC_SINGLE_BAR");

            SetProp(bar, "SourceBarHandle", barId.Handle.Value.ToString("X8", Inv));
            SetProp(bar, "ViewingDirection", "Auto");
            SetProp(bar, "Direction", "X");
            SetProp(bar, "Cover", 40.0);
            SetProp(bar, "Pt1X", x0);
            SetProp(bar, "Pt1Y", y0);
            SetProp(bar, "BarsSpan", span);
            SetProp(bar, "Spacing", Spacing);
            SetProp(bar, "Count", 0);
            SetProp(bar, "Mark", (string)Call(BarDataType, "FormatMark", 12, nr, Spacing, 2));

            var res = Call(BarBlockType, "GenerateFromBounds", db, x0, y0, x0 + viewLen, y0 + span, bar, true, nr);
            var distId = (ObjectId)Prop(res, "BlockRefId");
            if (distId.IsNull) throw new AssertFail("GenerateFromBounds nie utworzył rozkładu");

            var ann = Call(AnnotType, "CreateLeader", db, res, bar, false, nr, null, true, true, true);
            var annotId = (ObjectId)Prop(ann, "BlockRefId");
            if (annotId.IsNull) throw new AssertFail("CreateLeader nie utworzył opisu");

            Call(BarBlockType, "LinkAnnotation", db, distId, annotId);

            int count;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var dist = Call(BarBlockType, "ReadXData", tr.GetObject(distId, OpenMode.ForRead));
                if (dist == null) throw new AssertFail("rozkład bez RC_BAR_BLOCK");
                count = (int)Prop(dist, "EffectiveCount");
                if (!SameHandle(Prop(dist, "AnnotHandle"), annotId.Handle))
                    throw new AssertFail("AnnotHandle rozkładu=" + Prop(dist, "AnnotHandle") + ", opis=" + annotId.Handle);
                if (!SameHandle(Prop(dist, "SourceBarHandle"), barId.Handle))
                    throw new AssertFail("SourceBarHandle rozkładu=" + Prop(dist, "SourceBarHandle") + ", pręt=" + barId.Handle);
            }
            return Tuple.Create(distId, annotId, count);
        }

        private static string LabelText(Database db, ObjectId lblId)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ml = tr.GetObject(lblId, OpenMode.ForRead) as MLeader;
                if (ml == null) throw new AssertFail("etykieta nie jest MLeaderem");
                return ml.MText?.Contents;
            }
        }

        private static string Rows(List<object> rows)
            => string.Join(", ", rows.Select(rw => "poz." + Convert.ToString(Prop(rw, "BarMark"), Inv) + " x"
                                                 + Convert.ToString(Prop(rw, "NoEach"), Inv) + " L=" + F((double)Prop(rw, "Length"))));

        private static object NewBar(string mark, double a)
        {
            var bar = Activator.CreateInstance(Plugin.GetType(BarDataType, true));
            SetProp(bar, "Mark", mark);
            SetProp(bar, "Diameter", 12);
            SetProp(bar, "ShapeCode", "00");
            SetProp(bar, "LengthA", a);
            SetProp(bar, "TotalLength", a);
            SetProp(bar, "LengthOverridden", false);
            SetProp(bar, "LayerCode", "B1");
            SetProp(bar, "Position", "BOT");
            return bar;
        }

        private static TypedValue[] ReadRecord(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                using (var rb = e.GetXDataForApplication(XApp))
                    return rb?.AsArray() ?? new TypedValue[0];
            }
        }

        /// <summary>Pole i: ten sam kod typu, ten sam typ .NET wartości i ta sama wartość.</summary>
        private static void SameField(int i, TypedValue a, TypedValue b)
        {
            if (a.TypeCode != b.TypeCode)
                throw new AssertFail($"[{i}] typ: jest {b.TypeCode}, oczekiwano {a.TypeCode} ({Desc(b)} vs {Desc(a)})");
            var ta = a.Value?.GetType(); var tb = b.Value?.GetType();
            if (ta != tb)
                throw new AssertFail($"[{i}] typ wartości: jest {tb?.Name ?? "null"}, oczekiwano {ta?.Name ?? "null"}");
            if (!Equals(a.Value, b.Value))
                throw new AssertFail($"[{i}] wartość: jest {Desc(b)}, oczekiwano {Desc(a)}");
        }

        private static string Ver2(TypedValue[] r)
        {
            if (r.Length <= 14) throw new AssertFail("brak [14]");
            var tv = r[14];
            if (tv.TypeCode != TcInt16 || !(tv.Value is short s) || s != 2)
                throw new AssertFail("[14]=" + Desc(tv) + ", oczekiwano Int16 2");
            return "[14]=Int16 2";
        }

        private static bool SameHandle(object v, Handle h)
        {
            if (v is Handle hv) return hv.Value == h.Value;
            return v != null && long.TryParse(v.ToString(), NumberStyles.HexNumber, Inv, out long x) && x == h.Value;
        }

        private static string Desc(TypedValue tv)
            => tv.TypeCode.ToString(Inv) + ":" + (tv.Value == null ? "null" : tv.Value.GetType().Name + " " + Convert.ToString(tv.Value, Inv));

        // ================================================================
        // Infrastruktura: refleksja, wyniki, pliki (jak w etapach 1 i 2a)
        // ================================================================

        private static Assembly _plugin;

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
            return Invoke(cands[0], null, args);
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

        private static string OutDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        private static Database ActiveDb() => Application.DocumentManager.MdiActiveDocument.Database;

        private static void Msg(string text)
        {
            try { Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(text); }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("PlanCStage2bTests.Msg: " + ex); }
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

        /// <summary>Zapis wyników (po każdym kroku, żeby przy zawieszeniu skryptu zostały wyniki częściowe).</summary>
        private static void Flush(IEnumerable<string> tail)
        {
            try
            {
                var lines = new List<string>
                {
                    "PLAN C ETAP 2b — testy dymne BricsCAD (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")",
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

        private static string Eq(string what, object act, object exp)
        {
            if (!Equals(act, exp))
                throw new AssertFail($"{what}: jest {Show(act)}, oczekiwano {Show(exp)}");
            return $"{what}={Show(act)}";
        }

        private static string Tc(string what, TypedValue tv, short exp)
        {
            if (tv.TypeCode != exp) throw new AssertFail($"{what} typ: jest {Desc(tv)}, oczekiwano {exp.ToString(Inv)}");
            return $"{what} typ={exp.ToString(Inv)}";
        }

        private static string True(bool cond, string detail)
        {
            if (!cond) throw new AssertFail(detail);
            return detail;
        }

        private static string Show(object o)
            => o == null ? "null" : o.GetType().Name + " " + Convert.ToString(o, Inv);

        private static string F(double d) => d.ToString("0.###", Inv);
    }
}
