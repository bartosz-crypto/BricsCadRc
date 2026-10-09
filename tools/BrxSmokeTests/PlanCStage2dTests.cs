// PLAN C ETAP 2d — testy dymne: kaskada usuwania pręta (polilinia i pręt-blok v2) w BricsCAD V25.
// Uruchamiane skryptem planc-e2d.scr (run-planc-e2d.ps1). S9–S12 z docs/PLAN_C_etap2.md (sekcja 6.1).
//
// Dostęp do wtyczki WYŁĄCZNIE przez refleksję na zestawie NaSmartRc wczytanym NETLOAD-em — jak w etapach 1, 2a i 2b.
// Numerowany pręt-blok tworzą tylko testy (1-POPRAWKI p.1): TestBarBlockEngine.Create → Mark H12-nn
// (PositionCounter.NextAuto) → BarXData.Write(formatVersion 2) → etykieta (PlaceBarLabel + LabelHandle, p.2).
// Wyjątek „bez etykiety” tylko w S11 (poprawka #2, W1c: kaskada działa także bez etykiety).
//
// Usunięcie pręta: Entity.Erase() w transakcji komendy (jak ERASE) — przechodzi przez ObjectOverrule.Erase
// (BarPolylineEraseOverrule / SingleBarBlockEraseOverrule). Cofnięcie: _.UNDO 1 w skrypcie, potem _.REDO;
// komendy sprawdzające po UNDO/REDO mają NoUndoMarker (nie zajmują pozycji na liście undo).

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
    public class PlanCStage2dTests
    {
        // ----------------------------------------------------------------
        // Stałe testu
        // ----------------------------------------------------------------

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const string ResultsFile = "planc-e2d-results.txt";
        private const string Prefix      = "[RC E2D]";
        private const string XApp        = "RC_SINGLE_BAR";
        private const string XAppBlock   = "RC_BAR_BLOCK";
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
        private const string BbsReaderType  = "BricsCadRc.Core.BbsDrawingReader";
        private const string ScheduleType   = "BricsCadRc.Core.BarScheduleEngine";
        private const string CascadeType    = "BricsCadRc.Core.BarEraseCascade";
        private const string BlockEraseType = "BricsCadRc.Core.SingleBarBlockEraseOverrule";

        private static readonly string[] ExpectedSteps =
        {
            "SETUP", "S9_SETUP", "S9_ERASE", "S9_UNDO", "S9_REDO",
            "S10_SETUP", "S10_ERASE", "S10_UNDO", "S10_REDO", "S11", "S12"
        };

        // ----------------------------------------------------------------
        // Stan sesji testowej
        // ----------------------------------------------------------------

        private static readonly List<string> _results  = new List<string>();
        private static readonly List<string> _executed = new List<string>();
        private static int _stepStart;

        /// <summary>Obiekty jednego scenariusza kaskady: pręt, etykieta, rozkład, opis rozkładu.</summary>
        private sealed class Scenario
        {
            public ObjectId Bar   = ObjectId.Null;
            public ObjectId Label = ObjectId.Null;
            public ObjectId Dist  = ObjectId.Null;
            public ObjectId Annot = ObjectId.Null;
            public int      Nr;
            public bool     Complete => !Bar.IsNull && !Label.IsNull && !Dist.IsNull && !Annot.IsNull;
        }

        private static Scenario _s9;    // pręt-blok v2
        private static Scenario _s10;   // polilinia

        private sealed class AssertFail : System.Exception
        {
            public AssertFail(string msg) : base(msg) { }
        }

        // ================================================================
        // Komendy
        // ================================================================

        [CommandMethod("RCSMOKE_E2D_SETUP", CommandFlags.Modal)]
        public void Setup()
        {
            _results.Clear();
            _executed.Clear();
            _s9 = null;
            _s10 = null;

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

                Check(step, "typy wtyczki używane w testach (w tym BarEraseCascade, SingleBarBlockEraseOverrule)", () =>
                {
                    foreach (var tn in new[] { SingleBarType, EngineType, BarEntityType, BarXDataType, BarDataType, BarBlockType,
                                               AnnotType, CounterType, BbsReaderType, ScheduleType, CascadeType, BlockEraseType })
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

        // ---- S9: pręt-blok v2 z etykietą + rozkład + opis; BBS-reader i BarScheduleEngine widzą pozycję ----
        [CommandMethod("RCSMOKE_E2D_S9_SETUP", CommandFlags.Modal)]
        public void S9Setup()
        {
            const string step = "S9_SETUP";
            Run(step, db =>
            {
                var sc = new Scenario();
                _s9 = sc;
                const double a = 3500.0;

                Check(step, "pręt-blok v2 H12-nn (NextAuto) z etykietą", () =>
                {
                    sc.Nr = NextAuto(db);
                    var r = CreateNumberedV2(db, sc.Nr, a, new Point3d(0, 40000, 0), withLabel: true);
                    sc.Bar = r.Item1; sc.Label = r.Item2;
                    return "nr=" + sc.Nr.ToString(Inv) + "; " + DescribeV2(db, sc.Bar, Mark(sc.Nr), sc.Label);
                });
                if (sc.Bar.IsNull || sc.Label.IsNull) return;

                int count = 0;
                Check(step, "rozkład (GenerateFromBounds) + opis (CreateLeader) + LinkAnnotation", () =>
                {
                    var d = MakeDistribution(db, sc.Bar, sc.Nr, 0, 37000, 3000.0);
                    sc.Dist = d.Item1; sc.Annot = d.Item2; count = d.Item3;
                    return "rozkład=" + sc.Dist.Handle + " opis=" + sc.Annot.Handle + "; " + Int("ilość prętów", count, 6);
                });
                if (!sc.Complete) return;

                Check(step, "BbsDrawingReader.ReadBars: wiersz poz. nn (pełna sekwencja 1-POPRAWKI p.2)", () =>
                {
                    var res = Call(BbsReaderType, "ReadBars", db);
                    var rows = ((IEnumerable)Prop(res, "Rows")).Cast<object>().ToList();
                    var mine = rows.Where(rw => (int)Prop(rw, "BarMark") == sc.Nr).ToList();
                    if (mine.Count != 1) throw new AssertFail($"wiersze poz. {sc.Nr}: {mine.Count} (oczekiwano 1)");
                    return Eq("NoEach", Prop(mine[0], "NoEach"), count);
                });

                Check(step, "BarScheduleEngine.BuildSchedule: pozycja pręta-bloku z ilością z rozkładu", () =>
                {
                    var list = ((IEnumerable)Call(ScheduleType, "BuildSchedule", db)).Cast<object>().ToList();
                    var mine = list.Where(en => Equals(Prop(en, "Mark"), Mark(sc.Nr))).ToList();
                    if (mine.Count != 1) throw new AssertFail($"wpisy {Mark(sc.Nr)}: {mine.Count} (oczekiwano 1)");
                    return Eq("TotalCount", Prop(mine[0], "TotalCount"), count);
                });
            });
        }

        [CommandMethod("RCSMOKE_E2D_S9_ERASE", CommandFlags.Modal)]
        public void S9Erase() => EraseStep("S9_ERASE", _s9, "pręt-blok v2");

        [CommandMethod("RCSMOKE_E2D_S9_UNDO", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void S9Undo() => UndoStep("S9_UNDO", _s9, v2: true);

        [CommandMethod("RCSMOKE_E2D_S9_REDO", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void S9Redo() => RedoStep("S9_REDO", _s9);

        // ---- S10: to samo dla polilinii (identyczna kaskada jak we wzorcu) ----
        [CommandMethod("RCSMOKE_E2D_S10_SETUP", CommandFlags.Modal)]
        public void S10Setup()
        {
            const string step = "S10_SETUP";
            Run(step, db =>
            {
                var sc = new Scenario();
                _s10 = sc;

                Check(step, "polilinia H12-nn L3000 z etykietą (PlaceBar + PlaceBarLabel + LabelHandle)", () =>
                {
                    sc.Nr = NextAuto(db);
                    var at = new Point3d(0, 50000, 0);
                    sc.Bar = (ObjectId)Call(SingleBarType, "PlaceBar", db, NewBar(Mark(sc.Nr), 3000), at);
                    sc.Label = (ObjectId)Call(SingleBarType, "PlaceBarLabel", db, new Point3d(at.X + 1500, at.Y, 0),
                                              new Point3d(at.X + 1450, at.Y + 300, 0), Mark(sc.Nr), sc.Bar);
                    True(!sc.Label.IsNull, "PlaceBarLabel zwrócił Null");
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var e = (Entity)tr.GetObject(sc.Bar, OpenMode.ForWrite);
                        var bar = Call(SingleBarType, "ReadBarXData", e);
                        SetProp(bar, "LabelHandle", sc.Label.Handle.ToString());
                        Call(BarXDataType, "Write", e, bar, null);
                        tr.Commit();
                    }
                    var r = ReadRecord(db, sc.Bar);
                    return "nr=" + sc.Nr.ToString(Inv) + "; " + Int("długość rekordu", r.Length, PlacedPolyLen) + "; " + Tc("[13]", r[13], TcHandle)
                         + "; " + True(SameHandle(r[13].Value, sc.Label.Handle), "[13]=" + Desc(r[13]) + " etykieta=" + sc.Label.Handle);
                });
                if (sc.Bar.IsNull || sc.Label.IsNull) return;

                Check(step, "rozkład + opis", () =>
                {
                    var d = MakeDistribution(db, sc.Bar, sc.Nr, 0, 47000, 3000.0);
                    sc.Dist = d.Item1; sc.Annot = d.Item2;
                    return "rozkład=" + sc.Dist.Handle + " opis=" + sc.Annot.Handle + "; " + Int("ilość prętów", d.Item3, 6);
                });
            });
        }

        [CommandMethod("RCSMOKE_E2D_S10_ERASE", CommandFlags.Modal)]
        public void S10Erase() => EraseStep("S10_ERASE", _s10, "polilinia");

        [CommandMethod("RCSMOKE_E2D_S10_UNDO", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void S10Undo() => UndoStep("S10_UNDO", _s10, v2: false);

        [CommandMethod("RCSMOKE_E2D_S10_REDO", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void S10Redo() => RedoStep("S10_REDO", _s10);

        // ---- S11: pręt-blok v2 BEZ etykiety + rozkład → Erase → rozkład i opis usunięte (poprawka #2, W1c;
        //      wzorzec aba0ed1 zostawiał je — quirk usunięty decyzją użytkownika 2026-10-09) ----
        [CommandMethod("RCSMOKE_E2D_S11", CommandFlags.Modal)]
        public void S11()
        {
            const string step = "S11";
            Run(step, db =>
            {
                var sc = new Scenario();
                Check(step, "pręt-blok v2 H12-nn BEZ etykiety (LabelHandle pusty)", () =>
                {
                    sc.Nr = NextAuto(db);
                    sc.Bar = CreateNumberedV2(db, sc.Nr, 3000.0, new Point3d(0, 60000, 0), withLabel: false).Item1;
                    object bar;
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                        bar = Call(SingleBarType, "ReadBarXData", tr.GetObject(sc.Bar, OpenMode.ForRead));
                    var lh = (string)Prop(bar, "LabelHandle");
                    True(string.IsNullOrEmpty(lh), "LabelHandle niepusty: " + lh);
                    var r = ReadRecord(db, sc.Bar);
                    return "nr=" + sc.Nr.ToString(Inv) + "; " + Int("długość rekordu", r.Length, V2Len) + "; " + Ver2(r) + "; LabelHandle pusty";
                });
                if (sc.Bar.IsNull) return;

                Check(step, "rozkład + opis", () =>
                {
                    var d = MakeDistribution(db, sc.Bar, sc.Nr, 0, 57000, 3000.0);
                    sc.Dist = d.Item1; sc.Annot = d.Item2;
                    return "rozkład=" + sc.Dist.Handle + " opis=" + sc.Annot.Handle;
                });
                if (sc.Dist.IsNull || sc.Annot.IsNull) return;

                Check(step, "Erase pręta (kod, transakcja) → pręt usunięty", () =>
                {
                    EraseInCode(db, sc.Bar);
                    return True(sc.Bar.IsErased, "pręt nie został usunięty");
                });
                Check(step, "poprawka #2: rozkład usunięty", () => True(sc.Dist.IsErased, "rozkład został (kaskada bez etykiety nie działa — błąd #2 wzorca)"));
                Check(step, "poprawka #2: opis rozkładu usunięty", () => True(sc.Annot.IsErased, "opis został (kaskada bez etykiety nie działa — błąd #2 wzorca)"));
            });
        }

        // ---- S12: dwie bazy — kaskada w A nie rusza B (wariant z drugą bazą: side Database) ----
        // Dwa dokumenty w skrypcie są trudne (skrypt biegnie w aktywnym dokumencie), więc „rysunek B” to side Database
        // z blokiem niosącym kopię rekordu RC_BAR_BLOCK rozkładu A (SourceBarHandle i AnnotHandle = uchwyty z A, jako 1000).
        // Gdyby kaskada szukała rozkładów poza bazą usuwanego pręta (stan globalny), usunęłaby ten blok.
        [CommandMethod("RCSMOKE_E2D_S12", CommandFlags.Modal)]
        public void S12()
        {
            const string step = "S12";
            Run(step, db =>
            {
                var sc = new Scenario();
                Check(step, "A: pręt-blok v2 z etykietą + rozkład + opis", () =>
                {
                    sc.Nr = NextAuto(db);
                    var r = CreateNumberedV2(db, sc.Nr, 3000.0, new Point3d(0, 70000, 0), withLabel: true);
                    sc.Bar = r.Item1; sc.Label = r.Item2;
                    var d = MakeDistribution(db, sc.Bar, sc.Nr, 0, 67000, 3000.0);
                    sc.Dist = d.Item1; sc.Annot = d.Item2;
                    return "nr=" + sc.Nr.ToString(Inv) + " pręt=" + sc.Bar.Handle + " rozkład=" + sc.Dist.Handle;
                });
                if (!sc.Complete) return;

                Database side = null;
                try
                {
                    ObjectId bId = ObjectId.Null;
                    Check(step, "B (side Database): blok z kopią rekordu RC_BAR_BLOCK rozkładu A", () =>
                    {
                        side = new Database(true, true);
                        Call(SingleBarType, "EnsureAppIdRegistered", side);
                        Call(BarBlockType, "EnsureAppIdRegistered", side);
                        Call(AnnotType, "EnsureAppIdRegistered", side);

                        TypedValue[] recA;
                        using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                        using (var rb = ((Entity)tr.GetObject(sc.Dist, OpenMode.ForRead)).GetXDataForApplication(XAppBlock))
                            recA = rb?.AsArray() ?? throw new AssertFail("rozkład A bez RC_BAR_BLOCK");
                        // Uchwyty 1005 → tekst 1000 (stary format, XLink.Read go akceptuje) — w B nie ma tych obiektów.
                        var recB = recA.Select(tv => tv.TypeCode == TcHandle
                                                     ? new TypedValue((int)DxfCode.ExtendedDataAsciiString, HandleText(tv.Value))
                                                     : tv).ToArray();

                        using (var tr = side.TransactionManager.StartTransaction())
                        {
                            var bt = (BlockTable)tr.GetObject(side.BlockTableId, OpenMode.ForWrite);
                            var btr = new BlockTableRecord { Name = "RCSMOKE_E2D_B", Origin = Point3d.Origin };
                            var btrId = bt.Add(btr);
                            tr.AddNewlyCreatedDBObject(btr, true);
                            var ln = new Line(Point3d.Origin, new Point3d(1000, 0, 0));
                            btr.AppendEntity(ln);
                            tr.AddNewlyCreatedDBObject(ln, true);

                            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(side), OpenMode.ForWrite);
                            var br = new BlockReference(Point3d.Origin, btrId);
                            br.SetDatabaseDefaults(side);
                            bId = ms.AppendEntity(br);
                            tr.AddNewlyCreatedDBObject(br, true);
                            br.XData = new ResultBuffer(recB);
                            tr.Commit();
                        }

                        object dist;
                        using (var tr = side.TransactionManager.StartOpenCloseTransaction())
                            dist = Call(BarBlockType, "ReadXData", tr.GetObject(bId, OpenMode.ForRead));
                        if (dist == null) throw new AssertFail("blok B bez czytelnego RC_BAR_BLOCK");
                        return True(SameHandle(Prop(dist, "SourceBarHandle"), sc.Bar.Handle),
                                    "SourceBarHandle B=" + Prop(dist, "SourceBarHandle") + ", pręt A=" + sc.Bar.Handle);
                    });
                    if (bId.IsNull) return;

                    Check(step, "A: Erase pręta → kaskada w A (etykieta, rozkład, opis usunięte)", () =>
                    {
                        EraseInCode(db, sc.Bar);
                        True(sc.Bar.IsErased, "pręt nie został usunięty");
                        True(sc.Label.IsErased, "etykieta nie usunięta");
                        True(sc.Dist.IsErased, "rozkład nie usunięty");
                        True(sc.Annot.IsErased, "opis nie usunięty");
                        return "OK";
                    });

                    Check(step, "B: blok z rekordem rozkładu A nietknięty", () =>
                    {
                        True(!bId.IsErased, "blok w B usunięty przez kaskadę w A");
                        object dist;
                        using (var tr = side.TransactionManager.StartOpenCloseTransaction())
                            dist = Call(BarBlockType, "ReadXData", tr.GetObject(bId, OpenMode.ForRead));
                        if (dist == null) throw new AssertFail("blok B stracił RC_BAR_BLOCK");
                        return True(SameHandle(Prop(dist, "SourceBarHandle"), sc.Bar.Handle), "SourceBarHandle B zmieniony: " + Prop(dist, "SourceBarHandle"));
                    });
                }
                finally
                {
                    side?.Dispose();
                }
            });
        }

        // ---- Raport końcowy ----
        [CommandMethod("RCSMOKE_E2D_REPORT", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
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

        /// <summary>Erase pręta w kodzie → kaskada: etykieta, rozkład i opis usunięte.</summary>
        private static void EraseStep(string step, Scenario sc, string what)
        {
            Run(step, db =>
            {
                if (sc == null || !sc.Complete)
                {
                    Add("FAIL", step, "brak obiektów scenariusza", "krok *_SETUP nie utworzył pręta, etykiety, rozkładu i opisu");
                    return;
                }
                Check(step, "Erase (" + what + ") w transakcji komendy → pręt usunięty", () =>
                {
                    EraseInCode(db, sc.Bar);
                    return True(sc.Bar.IsErased, "pręt nie został usunięty");
                });
                const string hint = " (overrule Erase nie odpalił przy Erase() z kodu? — wtedy w skrypcie _.ERASE oknem)";
                Check(step, "kaskada: etykieta usunięta", () => True(sc.Label.IsErased, "etykieta nie usunięta" + hint));
                Check(step, "kaskada: rozkład usunięty", () => True(sc.Dist.IsErased, "rozkład nie usunięty" + hint));
                Check(step, "kaskada: opis rozkładu usunięty", () => True(sc.Annot.IsErased, "opis nie usunięty" + hint));
            });
        }

        /// <summary>Po _.UNDO 1: pręt, etykieta, rozkład i opis przywrócone, powiązania jak przed usunięciem.</summary>
        private static void UndoStep(string step, Scenario sc, bool v2)
        {
            Run(step, db =>
            {
                if (sc == null || !sc.Complete)
                {
                    Add("FAIL", step, "brak obiektów scenariusza", "krok *_SETUP nie utworzył obiektów");
                    return;
                }
                Check(step, "UNDO: pręt przywrócony", () => True(!sc.Bar.IsErased, "pręt nadal usunięty — UNDO 1 nie cofnęło *_ERASE"));
                Check(step, "UNDO: etykieta przywrócona", () => True(!sc.Label.IsErased, "etykieta nadal usunięta"));
                Check(step, "UNDO: rozkład przywrócony", () => True(!sc.Dist.IsErased, "rozkład nadal usunięty"));
                Check(step, "UNDO: opis rozkładu przywrócony", () => True(!sc.Annot.IsErased, "opis nadal usunięty"));
                Check(step, "UNDO: rekord pręta i powiązania", () =>
                {
                    var r = ReadRecord(db, sc.Bar);
                    var sb = new StringBuilder();
                    sb.Append(Int("długość rekordu", r.Length, v2 ? V2Len : PlacedPolyLen));
                    if (v2) sb.Append("; ").Append(Ver2(r));
                    sb.Append("; ").Append(True(SameHandle(r[13].Value, sc.Label.Handle), "[13]=" + Desc(r[13]) + " etykieta=" + sc.Label.Handle));
                    object dist;
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                        dist = Call(BarBlockType, "ReadXData", tr.GetObject(sc.Dist, OpenMode.ForRead));
                    if (dist == null) throw new AssertFail("rozkład bez RC_BAR_BLOCK po UNDO");
                    sb.Append("; ").Append(True(SameHandle(Prop(dist, "SourceBarHandle"), sc.Bar.Handle), "SourceBarHandle=" + Prop(dist, "SourceBarHandle")));
                    sb.Append("; ").Append(True(SameHandle(Prop(dist, "AnnotHandle"), sc.Annot.Handle), "AnnotHandle=" + Prop(dist, "AnnotHandle")));
                    return sb.ToString();
                });
            });
        }

        /// <summary>Po _.REDO: ponownie usunięte (pręt + kaskada). Gdy REDO w skrypcie nic nie zrobiło — FAIL.</summary>
        private static void RedoStep(string step, Scenario sc)
        {
            Run(step, db =>
            {
                if (sc == null || !sc.Complete)
                {
                    Add("FAIL", step, "brak obiektów scenariusza", "krok *_SETUP nie utworzył obiektów");
                    return;
                }
                if (!sc.Bar.IsErased)
                {
                    Add("FAIL", step, "REDO: pręt ponownie usunięty", "pręt nieusunięty — _.REDO nie zadziałało (komenda sprawdzająca między UNDO a REDO?)");
                    return;
                }
                Check(step, "REDO: etykieta usunięta", () => True(sc.Label.IsErased, "etykieta przywrócona przy REDO"));
                Check(step, "REDO: rozkład usunięty", () => True(sc.Dist.IsErased, "rozkład przywrócony przy REDO"));
                Check(step, "REDO: opis rozkładu usunięty", () => True(sc.Annot.IsErased, "opis przywrócony przy REDO"));
            });
        }

        /// <summary>Usunięcie encji w transakcji (jak ERASE) — przez ObjectOverrule.Erase.</summary>
        private static void EraseInCode(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                tr.GetObject(id, OpenMode.ForWrite).Erase();
                tr.Commit();
            }
        }

        private static string Mark(int nr) => (string)Call(BarDataType, "FormatMark", 12, nr, 0.0, 1);   // „H12-nn”

        private static HashSet<int> Used(Database db) => (HashSet<int>)Call(CounterType, "GetUsedPositionNumbers", db);

        private static int NextAuto(Database db) => (int)Call(CounterType, "NextAuto", db, Used(db));

        /// <summary>
        /// Numerowany pręt-blok (tylko testy): TestBarBlockEngine.Create → Mark H12-nn → BarXData.Write(…, 2)
        /// → (withLabel) etykieta PlaceBarLabel z back-linkiem → LabelHandle (1005) → BarXData.Write(…, 2).
        /// </summary>
        private static Tuple<ObjectId, ObjectId> CreateNumberedV2(Database db, int nr, double a, Point3d at, bool withLabel)
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
            if (!withLabel) return Tuple.Create(id, ObjectId.Null);

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
        /// Rozkład + opis jak RC_DISTRIBUTION, bez dialogów i jigów (kopia z PlanCStage2bTests): dane pręta źródłowego
        /// + SourceBarHandle, pręty poziome od (x0, y0), długość widoku viewLen, zakres span co 200 → GenerateFromBounds
        /// → CreateLeader → LinkAnnotation. Zwraca (rozkład, opis, EffectiveCount z XData rozkładu).
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

        /// <summary>Wartość pola 1005 jako tekst hex (X8) — do zapisu jako 1000 w drugiej bazie.</summary>
        private static string HandleText(object v)
        {
            if (v is Handle hv) return hv.Value.ToString("X8", Inv);
            return Convert.ToString(v, Inv) ?? "";
        }

        private static string Desc(TypedValue tv)
            => tv.TypeCode.ToString(Inv) + ":" + (tv.Value == null ? "null" : tv.Value.GetType().Name + " " + Convert.ToString(tv.Value, Inv));

        // ================================================================
        // Infrastruktura: refleksja, wyniki, pliki (jak w etapach 1, 2a i 2b)
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
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("PlanCStage2dTests.Msg: " + ex); }
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
                    "PLAN C ETAP 2d — testy dymne BricsCAD (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")",
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
