// NIB / GARAGE — testy dymne automatu domykania nibu (POPRAWKI_WZORCA 23) i garaży (POPRAWKI_WZORCA 22) w BricsCAD V25
// (docs/PLAN_NIB_GARAZE_2026-10-09.md, A7 / B4). Uruchamiane skryptem planc-nib.scr (run-planc-nib.ps1). Komendy RCSMOKE_NIB_*.
// Osobna klasa z własną (skróconą) infrastrukturą — bez zależności od klasy częściowej W1/W2/PORT.
// Dostęp do wtyczki WYŁĄCZNIE przez refleksję na zestawie NaSmartRc wczytanym NETLOAD-em.
//
// Dane: współrzędne z GA Holbrook P02 (przesunięte do lokalnego układu płyty), warstwa SD-PILED-RAFT.
// N1  (458) obrys + otwarta linia uskoku przez całą krawędź + próg drzwi → EnsureForCommand: nib domknięty (Detect ≠ null),
//     zamknięta polilinia 4 wierzch., linia uskoku usunięta, próg drzwi zostaje.
// N2  ręczny zamknięty nib → TryClose = false, rysunek bez zmian, bez komunikatu.
// N3  przerwa 12,5 mm do obrysu → TryClose = false, komunikat „przerwa 12.5 mm … (tolerancja 4 mm)”, linia zostaje.
// N4  (324-325) Polyline + Line → domknięty, oba kawałki usunięte, progi drzwi zostają.
// 528 (528-529) linia strefy 760 mm z końcami w wierzchołkach linii uskoku → domknięty, linia 760 zostaje.
// N5A/N5/N5U  UNDO: domknięcie w osobnej komendzie, _.UNDO 1 → linia uskoku wraca, pętla znika, Detect = null.
// G2  PunchingAutoEngine.Apply dla „PLOT 538”: notatka MANUAL-NOTE garażu („GARAGE 321”) zostaje, stara bez płyty — usunięta.
// N6  mini-GA (458) → GaImportEngine.Import: NibClosed = 2 (rzut dolny i górny), Detect ≠ null; ponowny Import — bez duplikatów.
// 319 mini-GA (319) z linią strefy 760 mm: NibClosed = 2, Joined = 0, linia 760 zostaje otwarta (JoinLoops jej nie skleja).
// G1  mini-GA z „PLOT 321” i „GARAGE 321” → 2 płyty, pierwsza PLOT; Import garażu: NibClosed = 2 (U na 3 bokach).

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
    public class PlanCNibTests
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const string ResultsFile = "planc-nib-results.txt";
        private const string Prefix = "[RC NIB]";
        private const string Title = "NIB / GARAGE — testy dymne automatu domykania nibu i garaży";
        private const string Slab = "SD-PILED-RAFT";

        private const string NibCloseType = "BricsCadRc.Core.NibAutoClose";
        private const string NibDetType   = "BricsCadRc.Core.NibDetector";
        private const string GaType       = "BricsCadRc.Core.GaImportEngine";
        private const string PunchType    = "BricsCadRc.Core.PunchingAutoEngine";

        private static readonly List<string> ExpectedSteps = new List<string>
        {
            "SETUP", "N1", "N2", "N3", "N4", "528", "N5A", "N5", "N5U", "G2", "N6", "319", "G1",
        };

        private static readonly List<string> _results = new List<string>();
        private static readonly List<string> _executed = new List<string>();
        private static int _stepStart;

        // ── dane (lokalnie, z GA P02) ───────────────────────────────────

        private static readonly double[] O458 = { 7127.5, 10052.5, 0, 10052.5, 0, 0, 7127.5, 0 };
        private static readonly double[] Nib458 = { 160, 10052.5, 160, 0 };
        private static readonly double[] Door458 = { 4188.75, -417.0411, 4688.75, 19.8078, 5188.75, -417.0411, 4188.75, -417.0411 };

        private static readonly double[] O324 = { 0, 9940, 7000, 9940, 7000, 0, 0, 0 };
        private static readonly double[] Pl324 = { 6840, 9940, 6840, 160, 1880.25, 160 };
        private static readonly double[] Ln324 = { 1880.25, 160, 1880.25, 0 };
        private static readonly double[] Door324a = { 897.75, -461.9489, 1397.75, -25.1, 1897.75, -461.9489, 897.75, -461.9489 };
        private static readonly double[] Door324b = { -483.1489, 4391.5, -46.3, 4891.5, -483.1489, 5391.5, -483.1489, 4391.5 };

        private static readonly double[] O528 = { 0, 10502.5, 10631, 10502.5, 10631, 0, 0, 0 };
        private static readonly double[] Zone528 = { 760, 160, 760, 10342.5 };
        private static readonly double[] Nib528 = { 760, 0, 760, 160, 160, 160, 160, 10342.5, 760, 10342.5, 760, 10502.5 };

        private static readonly double[] O319 =
        {
            589.934, 6452.5, 9517.434, 6452.5, 9517.434, 900, 6254.934, 900, 6254.934, 0, 3627.434, 0, 3627.434, 900,
            589.934, 900, 589.934, 2463.5108, 0, 3053.4448, 0, 4302.2554, 589.934, 4892.1894
        };
        private static readonly double[] Nib319 =
        {
            1349.934, 1060, 749.934, 1060, 749.934, 2529.7849, 160, 3119.7189, 160, 4235.9813, 749.934, 4825.9152,
            749.934, 6292.5, 9357.434, 6292.5, 9357.434, 3400
        };
        private static readonly double[] Zone319 = { 1349.934, 1060, 1349.934, 5692.5, 8757.434, 5692.5, 8757.434, 3400, 9357.434, 3400 };
        private static readonly double[] Ln319a = { 1349.934, 1060, 1349.934, 900 };
        private static readonly double[] Ln319b = { 9357.434, 3400, 9517.434, 3400 };
        private static readonly double[] Stub319 = { 8757.434, 1694.5683, 8757.434, 1660 };

        private static readonly double[] O321 = { 0, 10277.5, 6790.035, 10277.5, 6790.035, 675, 3415, 675, 3415, 0, 562.5, 0, 562.5, 675, 0, 675 };
        private static readonly double[] Nib321 = { 5987.5, 10277.5, 5987.5, 10117.5, 6630.035, 10117.5, 6630.035, 835, 5312.5, 835, 5312.5, 675 };
        private static readonly double[] OG321 = { 0, 6565, 3527.5, 6565, 3527.5, 0, 0, 0 };
        private static readonly double[] NibG321 = { 140, 0, 140, 6425, 3387.5, 6425, 3387.5, 0 };

        // ── stan ────────────────────────────────────────────────────────
        private static ObjectId _n5Outline, _n5Line, _n5Loop;

        // ================================================================
        // SETUP / REPORT
        // ================================================================

        [CommandMethod("RCSMOKE_NIB_SETUP", CommandFlags.Modal)]
        public void Setup()
        {
            _results.Clear();
            _executed.Clear();
            const string step = "SETUP";
            Run(step, db =>
            {
                Check(step, "wtyczka NaSmartRc wczytana (jedna kopia)", () =>
                {
                    var all = AppDomain.CurrentDomain.GetAssemblies().Where(a => a.GetName().Name == "NaSmartRc").ToList();
                    if (all.Count != 1) throw new AssertFail("wczytane kopie NaSmartRc: " + all.Count);
                    return "Location=" + Loc(Plugin);
                });
                Check(step, "czyszczenie przestrzeni modelu + warstwa " + Slab, () =>
                {
                    int n = CleanModelSpace(db);
                    EnsureLayer(db, Slab);
                    return "usunięto " + n.ToString(Inv);
                });
            });
        }

        [CommandMethod("RCSMOKE_NIB_REPORT", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
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
        // A — automat domykania nibu na rysunku
        // ================================================================

        [CommandMethod("RCSMOKE_NIB_N1", CommandFlags.Modal)]
        public void N1()
        {
            const string step = "N1";
            Run(step, db => Check(step, "458: EnsureForCommand domyka linię uskoku przez całą krawędź; próg drzwi zostaje", () =>
            {
                var d = new Vector2d(0, 0);
                var o = Pl(db, O458, d, closed: true);
                var nib = Pl(db, Nib458, d);
                var door = Pl(db, Door458, d);
                var before = ClosedSlabPolylines(db);
                bool ok = (bool)Call(NibCloseType, "EnsureForCommand", Application.DocumentManager.MdiActiveDocument, o, Prefix);
                var loops = ClosedSlabPolylines(db).Except(before).ToList();
                return Eq("EnsureForCommand", ok, true)
                     + "; " + Eq("Detect ≠ null", Detect(db, o) != null, true)
                     + "; " + Int("nowe zamknięte polilinie", loops.Count, 1)
                     + "; " + Int("wierzchołki pętli", Vertices(db, loops[0]), 4)
                     + "; " + Eq("linia uskoku usunięta", nib.IsErased, true)
                     + "; " + Eq("próg drzwi zostaje", door.IsErased, false)
                     + "; " + Eq("warstwa pętli", Layer(db, loops[0]), Slab);
            }));
        }

        [CommandMethod("RCSMOKE_NIB_N2", CommandFlags.Modal)]
        public void N2()
        {
            const string step = "N2";
            Run(step, db => Check(step, "ręczny zamknięty nib — TryClose = false, bez zmian, bez komunikatu", () =>
            {
                var d = new Vector2d(30000, 0);
                var o = Pl(db, O458, d, closed: true);
                var inner = Pl(db, new double[] { 7127.5, 10052.5, 130, 10052.5, 130, 0, 7127.5, 0 }, d, closed: true);
                int n0 = CountMs(db);
                var msgs = new List<string>();
                bool ok = (bool)Call(NibCloseType, "TryClose", db, o, null, null, msgs);
                return Eq("TryClose", ok, false) + "; " + Int("obiekty", CountMs(db), n0)
                     + "; " + Int("komunikaty", msgs.Count, 0) + "; " + Eq("Detect ≠ null", Detect(db, o) != null, true)
                     + "; " + Eq("nib ręczny", inner.IsErased, false);
            }));
        }

        [CommandMethod("RCSMOKE_NIB_N3", CommandFlags.Modal)]
        public void N3()
        {
            const string step = "N3";
            Run(step, db => Check(step, "przerwa 12,5 mm do obrysu — komunikat, linia zostaje, brak pętli", () =>
            {
                var d = new Vector2d(60000, 0);
                var o = Pl(db, O458, d, closed: true);
                var nib = Pl(db, new double[] { 160, 10052.5, 160, 12.5 }, d);
                int n0 = CountMs(db);
                var msgs = new List<string>();
                bool ok = (bool)Call(NibCloseType, "TryClose", db, o, null, null, msgs);
                string m = string.Join(" | ", msgs);
                if (!m.Contains("przerwa 12.5 mm") || !m.Contains("tolerancja 4 mm") || !m.Contains("Płyta bez nibu"))
                    throw new AssertFail("komunikat: " + m);
                return Eq("TryClose", ok, false) + "; " + Int("obiekty", CountMs(db), n0)
                     + "; " + Eq("linia zostaje", nib.IsErased, false) + "; " + m;
            }));
        }

        [CommandMethod("RCSMOKE_NIB_N4", CommandFlags.Modal)]
        public void N4()
        {
            const string step = "N4";
            Run(step, db => Check(step, "324-325: Polyline + Line → domknięty, progi drzwi zostają", () =>
            {
                var d = new Vector2d(90000, 0);
                var o = Pl(db, O324, d, closed: true);
                var pl = Pl(db, Pl324, d);
                var ln = Ln(db, Ln324, d);
                var da = Pl(db, Door324a, d);
                var dbb = Pl(db, Door324b, d);
                var msgs = new List<string>();
                bool ok = (bool)Call(NibCloseType, "TryClose", db, o, null, null, msgs);
                return Eq("TryClose", ok, true) + "; " + Eq("Detect ≠ null", Detect(db, o) != null, true)
                     + "; " + Eq("polilinia usunięta", pl.IsErased, true) + "; " + Eq("linia usunięta", ln.IsErased, true)
                     + "; " + Eq("progi drzwi zostają", da.IsErased || dbb.IsErased, false) + "; " + string.Join(" | ", msgs);
            }));
        }

        [CommandMethod("RCSMOKE_NIB_528", CommandFlags.Modal)]
        public void T528()
        {
            const string step = "528";
            Run(step, db => Check(step, "528-529: linia strefy 760 mm z końcami w wierzchołkach linii uskoku zostaje", () =>
            {
                var d = new Vector2d(120000, 0);
                var o = Pl(db, O528, d, closed: true);
                var zone = Pl(db, Zone528, d);
                var nib = Pl(db, Nib528, d);
                var msgs = new List<string>();
                bool ok = (bool)Call(NibCloseType, "TryClose", db, o, null, null, msgs);
                return Eq("TryClose", ok, true) + "; " + Eq("Detect ≠ null", Detect(db, o) != null, true)
                     + "; " + Eq("linia uskoku usunięta", nib.IsErased, true) + "; " + Eq("linia 760 zostaje", zone.IsErased, false);
            }));
        }

        [CommandMethod("RCSMOKE_NIB_N5A", CommandFlags.Modal)]
        public void N5A()
        {
            const string step = "N5A";
            Run(step, db => Check(step, "UNDO: płyta 458 z otwartą linią uskoku (osobna komenda)", () =>
            {
                var d = new Vector2d(150000, 0);
                _n5Outline = Pl(db, O458, d, closed: true);
                _n5Line = Pl(db, Nib458, d);
                return Eq("Detect = null", Detect(db, _n5Outline) == null, true);
            }));
        }

        [CommandMethod("RCSMOKE_NIB_N5", CommandFlags.Modal)]
        public void N5()
        {
            const string step = "N5";
            Run(step, db => Check(step, "UNDO: domknięcie w komendzie", () =>
            {
                var before = ClosedSlabPolylines(db);
                var msgs = new List<string>();
                bool ok = (bool)Call(NibCloseType, "TryClose", db, _n5Outline, null, null, msgs);
                var loops = ClosedSlabPolylines(db).Except(before).ToList();
                _n5Loop = loops.Count == 1 ? loops[0] : ObjectId.Null;
                return Eq("TryClose", ok, true) + "; " + Int("nowe pętle", loops.Count, 1) + "; " + Eq("linia usunięta", _n5Line.IsErased, true);
            }));
        }

        [CommandMethod("RCSMOKE_NIB_N5U", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void N5U()
        {
            const string step = "N5U";
            Run(step, db => Check(step, "po _.UNDO 1: linia uskoku wraca, pętla znika, Detect = null, obrys zostaje", () =>
                Eq("linia uskoku", _n5Line.IsErased, false) + "; " + Eq("pętla usunięta", _n5Loop.IsNull || _n5Loop.IsErased, true)
                + "; " + Eq("obrys", _n5Outline.IsErased, false) + "; " + Eq("Detect = null", Detect(db, _n5Outline) == null, true)));
        }

        // ================================================================
        // B — garaż: notatka MANUAL-NOTE nie kasowana przy przebiciu innej płyty
        // ================================================================

        [CommandMethod("RCSMOKE_NIB_G2", CommandFlags.Modal)]
        public void G2()
        {
            const string step = "G2";
            Run(step, db => Check(step, "Apply(PLOT 538): MANUAL-NOTE „GARAGE 321” zostaje, MANUAL-NOTE bez płyty usunięta", () =>
            {
                var garage = Note(db, new Point3d(180000, 12000, 0), "PILE: G32101 — MANUAL DESIGN", "GARAGE 321");
                var legacy = Note(db, new Point3d(180000, 14000, 0), "PILE: P1 — MANUAL DESIGN", "");
                var plot = Note(db, new Point3d(180000, 16000, 0), "PILE: P53801 — MANUAL DESIGN", "PLOT 531");
                var resType = Plugin.GetType(PunchType + "+Result", true);
                var res = Activator.CreateInstance(resType);
                resType.GetField("Plot").SetValue(res, "PLOT 538");
                Call(PunchType, "Apply", Application.DocumentManager.MdiActiveDocument, res);
                return Eq("GARAGE 321 zostaje", garage.IsErased, false) + "; " + Eq("PLOT 531 zostaje", plot.IsErased, false)
                     + "; " + Eq("bez płyty usunięta", legacy.IsErased, true);
            }));
        }

        // ================================================================
        // mini-GA → Prepare GA (GaImportEngine)
        // ================================================================

        [CommandMethod("RCSMOKE_NIB_N6", CommandFlags.Modal)]
        public void N6()
        {
            const string step = "N6";
            Run(step, db =>
            {
                string path = Path.Combine(OutDir, "nib_ga_458.dwg");
                Check(step, "mini-GA 458 (obrys, linia uskoku, próg drzwi, opis PLOT 458)", () =>
                {
                    CleanModelSpace(db);
                    MakeGa(path, new[] { ("PLOT 458", O458, new[] { Nib458, Door458 }, new double[0][], new Vector2d(0, 0)) });
                    return path;
                });
                for (int round = 1; round <= 2; round++)
                {
                    int rr = round;
                    Check(step, $"Import #{rr}: NibClosed = 2, 2 obrysy + 2 pętle (bez duplikatów), Detect ≠ null, brak otwartej linii uskoku", () =>
                    {
                        var r = ImportGa(path, 0, out string label);
                        var closedGa = GaPolylines(db, closed: true);
                        var openGa = GaPolylines(db, closed: false).Where(id => !IsEffClosed(db, id)).ToList();
                        int detected = closedGa.Count(id => Detect(db, id) != null);
                        return Eq("płyta", label, "PLOT 458") + "; " + Int("NibClosed", (int)Prop(r, "NibClosed"), 2)
                             + "; " + Int("zamknięte RC_GA", closedGa.Count, 4) + "; " + Int("Detect ≠ null", detected, 4)
                             + "; " + Int("otwarte linie uskoku", openGa.Count, 0);
                    });
                }
            });
        }

        [CommandMethod("RCSMOKE_NIB_319", CommandFlags.Modal)]
        public void T319()
        {
            const string step = "319";
            Run(step, db =>
            {
                string path = Path.Combine(OutDir, "nib_ga_319.dwg");
                Check(step, "mini-GA 319: linia uskoku + linia strefy 760 mm + 2 linie + odcinek 35 mm", () =>
                {
                    CleanModelSpace(db);
                    MakeGa(path, new[] { ("PLOT 319", O319, new[] { Nib319, Zone319, Stub319 }, new[] { Ln319a, Ln319b }, new Vector2d(0, 0)) });
                    return path;
                });
                Check(step, "Import: NibClosed = 2, Joined = 0 (linia 760 nie sklejona), linia 760 zostaje otwarta w obu rzutach", () =>
                {
                    var r = ImportGa(path, 0, out _);
                    var closedGa = GaPolylines(db, closed: true);
                    int detected = closedGa.Count(id => Detect(db, id) != null);
                    int zones = GaPolylines(db, closed: false).Count(id => Vertices(db, id) == 5);
                    return Int("NibClosed", (int)Prop(r, "NibClosed"), 2) + "; " + Int("Joined", (int)Prop(r, "Joined"), 0)
                         + "; " + Int("zamknięte RC_GA", closedGa.Count, 4) + "; " + Int("Detect ≠ null", detected, 4)
                         + "; " + Int("linie 760 (5 wierzch.)", zones, 2);
                });
            });
        }

        [CommandMethod("RCSMOKE_NIB_G1", CommandFlags.Modal)]
        public void G1()
        {
            const string step = "G1";
            Run(step, db =>
            {
                string path = Path.Combine(OutDir, "nib_ga_321g.dwg");
                Check(step, "mini-GA: PLOT 321 i GARAGE 321 (U na 3 bokach)", () =>
                {
                    CleanModelSpace(db);
                    MakeGa(path, new[]
                    {
                        ("GARAGE 321", OG321, new[] { NibG321 }, new double[0][], new Vector2d(20000, 0)),
                        ("PLOT 321", O321, new[] { Nib321 }, new double[0][], new Vector2d(0, 0)),
                    });
                    return path;
                });
                Check(step, "ReadSource: 2 płyty, pierwsza PLOT 321, druga GARAGE 321", () =>
                {
                    var warnings = new List<string>();
                    var src = (IDisposable)Call(GaType, "ReadSource", path, warnings);
                    try
                    {
                        var labels = ((System.Collections.IEnumerable)Prop(src, "Plots")).Cast<object>().Select(p => (string)Prop(p, "Label")).ToList();
                        return Eq("płyty", string.Join(", ", labels), "PLOT 321, GARAGE 321");
                    }
                    finally { src.Dispose(); }
                });
                Check(step, "Import GARAGE 321: NibClosed = 2, Detect ≠ null", () =>
                {
                    var r = ImportGa(path, 1, out string label);
                    var closedGa = GaPolylines(db, closed: true);
                    int detected = closedGa.Count(id => Detect(db, id) != null);
                    return Eq("płyta", label, "GARAGE 321") + "; " + Int("NibClosed", (int)Prop(r, "NibClosed"), 2)
                         + "; " + Int("Detect ≠ null", detected, closedGa.Count);
                });
            });
        }

        // ================================================================
        // Pomocnicze — rysunek
        // ================================================================

        private static ObjectId Pl(Database db, double[] xy, Vector2d d, bool closed = false, Database target = null)
        {
            var tdb = target ?? db;
            using (var tr = tdb.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(tdb), OpenMode.ForWrite);
                var pl = new Polyline();
                pl.SetDatabaseDefaults(tdb);
                pl.Layer = Slab;
                for (int i = 0; i + 1 < xy.Length; i += 2)
                    pl.AddVertexAt(i / 2, new Point2d(xy[i] + d.X, xy[i + 1] + d.Y), 0, 0, 0);
                pl.Closed = closed;
                var id = ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                tr.Commit();
                return id;
            }
        }

        private static ObjectId Ln(Database db, double[] xy, Vector2d d, Database target = null)
        {
            var tdb = target ?? db;
            using (var tr = tdb.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(tdb), OpenMode.ForWrite);
                var ln = new Line(new Point3d(xy[0] + d.X, xy[1] + d.Y, 0), new Point3d(xy[2] + d.X, xy[3] + d.Y, 0));
                ln.SetDatabaseDefaults(tdb);
                ln.Layer = Slab;
                var id = ms.AppendEntity(ln);
                tr.AddNewlyCreatedDBObject(ln, true);
                tr.Commit();
                return id;
            }
        }

        /// <summary>Plik GA: płyty (opis na SD-Text, obrys + kawałki na SD-PILED-RAFT), zapis .dwg.</summary>
        private static void MakeGa(string path, (string label, double[] outline, double[][] pls, double[][] lines, Vector2d d)[] plots)
        {
            if (File.Exists(path)) File.Delete(path);
            using (var ga = new Database(true, true))
            {
                EnsureLayer(ga, Slab);
                EnsureLayer(ga, "SD-Text");
                foreach (var p in plots)
                {
                    Pl(null, p.outline, p.d, closed: true, target: ga);
                    foreach (var xy in p.pls) Pl(null, xy, p.d, target: ga);
                    foreach (var xy in p.lines) Ln(null, xy, p.d, target: ga);
                    using (var tr = ga.TransactionManager.StartTransaction())
                    {
                        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(ga), OpenMode.ForWrite);
                        var mt = new MText();
                        mt.SetDatabaseDefaults(ga);
                        mt.Layer = "SD-Text";
                        mt.TextHeight = 200;
                        mt.Location = new Point3d(p.d.X + 2000, p.d.Y + 4000, 0);
                        mt.Contents = p.label + "\\PFFL=22.150\\PSSL=21.925\\P300mm THK SLAB ON";
                        ms.AppendEntity(mt);
                        tr.AddNewlyCreatedDBObject(mt, true);
                        tr.Commit();
                    }
                }
                ga.SaveAs(path, DwgVersion.Current);
            }
        }

        private static object ImportGa(string path, int plotIndex, out string label)
        {
            var warnings = new List<string>();
            var src = (IDisposable)Call(GaType, "ReadSource", path, warnings);
            try
            {
                var plot = ((System.Collections.IEnumerable)Prop(src, "Plots")).Cast<object>().ElementAt(plotIndex);
                label = (string)Prop(plot, "Label");
                var r = Call(GaType, "Import", Application.DocumentManager.MdiActiveDocument, src, plot, warnings);
                Msg($"\n{Prefix} Import {label}: " + string.Join(" | ", warnings) + "\n");
                return r;
            }
            finally { src.Dispose(); }
        }

        private static ObjectId Note(Database db, Point3d at, string text, string plot)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                if (!rat.Has("RC_PH"))
                {
                    rat.UpgradeOpen();
                    var rec = new RegAppTableRecord { Name = "RC_PH" };
                    rat.Add(rec);
                    tr.AddNewlyCreatedDBObject(rec, true);
                }
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var mt = new MText();
                mt.SetDatabaseDefaults(db);
                mt.Contents = text;
                mt.Location = at;
                mt.TextHeight = 200;
                var id = ms.AppendEntity(mt);
                tr.AddNewlyCreatedDBObject(mt, true);
                mt.XData = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, "RC_PH"),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, "MANUAL-NOTE"),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, ""),
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, plot));
                tr.Commit();
                return id;
            }
        }

        private static List<ObjectId> ClosedSlabPolylines(Database db)
            => Ms(db, e => e is Polyline pl && pl.Closed && e.Layer == Slab);

        private static List<ObjectId> GaPolylines(Database db, bool closed)
            => Ms(db, e => e is Polyline pl && pl.Closed == closed && e.Layer == Slab && HasXData(e, "RC_GA"));

        private static bool IsEffClosed(Database db, ObjectId id)
            => With(db, id, e => e is Polyline pl && pl.NumberOfVertices >= 3
                                 && pl.GetPoint2dAt(0).GetDistanceTo(pl.GetPoint2dAt(pl.NumberOfVertices - 1)) < 1e-3);

        private static int Vertices(Database db, ObjectId id) => With(db, id, e => ((Polyline)e).NumberOfVertices);
        private static string Layer(Database db, ObjectId id) => With(db, id, e => e.Layer);

        private static bool HasXData(Entity e, string app)
        {
            using (var rb = e.GetXDataForApplication(app)) return rb != null;
        }

        private static T With<T>(Database db, ObjectId id, Func<Entity, T> f)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var r = f((Entity)tr.GetObject(id, OpenMode.ForRead));
                tr.Commit();
                return r;
            }
        }

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

        private static int CountMs(Database db) => Ms(db, e => true).Count;

        private static object Detect(Database db, ObjectId id) => Call(NibDetType, "Detect", db, id);

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

        private static void EnsureLayer(Database db, string name)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(name))
                {
                    lt.UpgradeOpen();
                    var rec = new LayerTableRecord { Name = name };
                    lt.Add(rec);
                    tr.AddNewlyCreatedDBObject(rec, true);
                }
                tr.Commit();
            }
        }

        // ================================================================
        // Infrastruktura: refleksja, wyniki (jak PlanCFixW1Tests, skrócona)
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
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("PlanCNibTests.Msg: " + ex); }
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

        private static string Eq(string what, object act, object exp)
        {
            if (!Equals(act, exp)) throw new AssertFail($"{what}: jest {act ?? "null"}, oczekiwano {exp ?? "null"}");
            return $"{what}={act}";
        }
    }
}
