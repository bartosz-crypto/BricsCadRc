// HB — testy dymne poprawek po teście Holbrook 2026-10-09 (docs/POPRAWKI_WZORCA.md, wiersze 24–27) w BricsCAD V25.
// Uruchamiane skryptem planc-hb.scr (run-planc-hb.ps1). Komendy RCSMOKE_HB_*. Klasa częściowa PlanCFixW1Tests —
// te same helpery (pręty, rozkłady, refleksja, ScanMs / Rec / Link, ArRect, GenB1); RCSMOKE_HB_SETUP przełącza plik
// wyników na planc-hb-results.txt.
//
// ADDP / ADDB (27, regresja Plan C 3b): RC_GENERATE → Add (GenerateAddFromMaps) z mapą B1 (RC_MAP) dwa razy — za drugim
//      razem pręt prosty 00 tej samej średnicy i długości JUŻ istnieje (szablon z pierwszego wywołania) → brak wyjątku
//      (dawniej eNotOpenForRead w FindOrCreateAddTemplate), liczba prętów bez zmian, rozkład ADD [18] → ta sama pozycja.
//      ADDP — format Polilinia, ADDB — format Blok.
// SQ523 (25): obrys płyty 523 z GA (krawędź z odchyłką 0,0115 mm) → AutoRebar B1 generuje siatkę (dawniej „płyta pod kątem”),
//      obrys w rysunku bit w bit bez zmian; kontrola: odchyłka 12,5 mm → odmowa (-1), brak rozkładów.
// COUNT (26): RC_COUNT_BBS (BbsCounter.CountAll) — masa prętów AutoRebar = suma mas RC_BBS (BbsDrawingReader.ReadBars),
//      stary pręt RC_BAR (linia) liczony jak dawniej (1 szt.), encja z RC_SINGLE_BAR i RC_BAR liczona raz.
// MAPLABEL (24): GuessPlot na opisie płyty „PLOT 526\PSSL=…” → płyta „PLOT 526”; AnalysisStore — dawna etykieta sklejona
//      → „PLOT 526”; ReadSource prawdziwych DXF map Holbrook (gdy są na dysku — inaczej SKIP): etykiety bez „h = …”.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;
using Application = Bricscad.ApplicationServices.Application;

namespace BricsCadRc.BrxSmokeTests
{
    public partial class PlanCFixW1Tests
    {
        private static bool _hbMode;

        private static readonly List<string> ExpectedStepsHb = new List<string>
        {
            "SETUP", "ADDP", "ADDB", "SQ523", "COUNT", "MAPLABEL",
        };

        private const string MapApp       = "RC_MAP";
        private const string MapEngType   = "BricsCadRc.Core.MapImportEngine";
        private const string CounterBbs   = "BricsCadRc.Core.BbsCounter";
        private const string StoreType    = "BricsCadRc.Core.AnalysisStore";

        /// <summary>Folder z DXF map Holbrook (DE736TQ004_*_punching_reinf_maps.dxf); zmienna RC_HB_MAPS nadpisuje.</summary>
        private static string HbMapsDir =>
            Environment.GetEnvironmentVariable("RC_HB_MAPS") is string s && s.Length > 0 ? s
            : @"C:\Users\test\AppData\Local\Temp\claude\C--Users-test-Desktop-AI-BricsCadRc---BJ\234c7608-bb95-42da-b7e6-ea57a103e39e\scratchpad\holbrook\data\fem";

        // GA P02 plotu 523, LWPOLYLINE 11168C5 (SD-PILED-RAFT): krawędź 4 → 5 ma dy = 0,0115 mm
        private static readonly Point2d[] Plot523 =
        {
            new Point2d(1101509.489430889, 57694.01998171644),
            new Point2d(1108411.989430889, 57694.01998171644),
            new Point2d(1108411.989430889, 47754.01998171644),
            new Point2d(1103871.989430889, 47754.01998171644),
            new Point2d(1103871.989430889, 48654.01986008426),
            new Point2d(1101509.489430889, 48654.00836587109),
        };

        private static ObjectId _hb523;

        private static void SetModeHb()
        {
            _w2Mode = false;
            _w4Mode = false;
            _f3Mode = false;
            _portMode = false;
            _hbMode = true;
            ResultsFile = "planc-hb-results.txt";
            Prefix = "[RC HB]";
            Title = "HB — testy dymne poprawek po teście Holbrook (24–27)";
        }

        // ================================================================
        // SETUP / REPORT
        // ================================================================

        [CommandMethod("RCSMOKE_HB_SETUP", CommandFlags.Modal)]
        public void HbSetup()
        {
            SetModeHb();
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
                Check(step, "czyszczenie + RegApp (pręty, rozkłady, RC_MAP, RC_BAR) + warstwy map + format Polyline", () =>
                {
                    int n = CleanModelSpace(db);
                    Call(SingleBarType, "EnsureAppIdRegistered", db);
                    Call(BarBlockType, "EnsureAppIdRegistered", db);
                    Call(AnnotType, "EnsureAppIdRegistered", db);
                    Call("BricsCadRc.Core.XDataHelper", "EnsureAppIdRegistered", db);
                    HbRegApp(db, MapApp);
                    foreach (var l in new[] { "SD-PILED-RAFT", "PH-B1-SLAB", "SD-B1 ADD" }) HbLayer(db, l);
                    SetFormat("Polyline");
                    return "usunięto " + n.ToString(Inv);
                });
            });
        }

        [CommandMethod("RCSMOKE_HB_REPORT", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void HbReport()
        {
            Report();
            _hbMode = false;
            SetMode(false);
        }

        // ================================================================
        // 27 — RC_GENERATE → Add z istniejącym prętem 00 (eNotOpenForRead, Plan C 3b)
        // ================================================================

        [CommandMethod("RCSMOKE_HB_ADDP", CommandFlags.Modal)]
        public void HbAddP() => HbAdd("ADDP", "Polyline", 400000);

        [CommandMethod("RCSMOKE_HB_ADDB", CommandFlags.Modal)]
        public void HbAddB() => HbAdd("ADDB", "Block", 500000);

        private static void HbAdd(string step, string fmt, double y0)
        {
            Run(step, db =>
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                ObjectId slab = ObjectId.Null;
                string tpl = "";
                int bars = 0;
                Check(step, $"płyta 12000×8000 (0, {y0.ToString(Inv)}) + mapa B1 (RC_MAP) ze strefą SD-B1 ADD; format {fmt}", () =>
                {
                    CleanModelSpace(db);
                    slab = ArRect(db, 0, y0, 12000, y0 + 8000);
                    HbMapB1(db, 60000, y0, "PLOT HB");
                    return "obrys " + Hx(slab);
                });
                Check(step, "Add #1: nowa pozycja prosta (szablon) + rozkład B1 ADD", () =>
                {
                    SetFormat(fmt);
                    object res;
                    try { res = Call(AutoRebarType, "GenerateAddFromMaps", doc, slab); }
                    finally { SetFormat("Polyline"); }
                    int d = Convert.ToInt32(Prop(res, "Distributions"), Inv);
                    True(d >= 1, "rozkładów ADD: " + d.ToString(Inv));
                    var add = HbAddDists(db);
                    True(add.Count >= 1, "brak rozkładu „B1 ADD” w rysunku");
                    tpl = Link(db, add[0], DistApp2, 18);
                    True(tpl.Length > 0, "rozkład ADD bez [18]");
                    bars = HbBars(db).Count;
                    return "rozkłady=" + d.ToString(Inv) + " szablon=" + tpl + " pręty=" + bars.ToString(Inv);
                });
                Check(step, "Add #2: pręt 00 tej średnicy i długości już jest → bez wyjątku (eNotOpenForRead), ta sama pozycja", () =>
                {
                    SetFormat(fmt);
                    object res;
                    try { res = Call(AutoRebarType, "GenerateAddFromMaps", doc, slab); }
                    finally { SetFormat("Polyline"); }
                    int d = Convert.ToInt32(Prop(res, "Distributions"), Inv);
                    True(d >= 1, "rozkładów ADD: " + d.ToString(Inv));
                    var add = HbAddDists(db);
                    True(add.Count >= 1, "brak rozkładu „B1 ADD” po drugim wywołaniu");
                    foreach (var id in add) Eq("[18] rozkładu " + Hx(id), Link(db, id, DistApp2, 18), tpl);
                    return Int("pręty", HbBars(db).Count, bars) + "; rozkłady=" + d.ToString(Inv);
                });
            });
        }

        // ================================================================
        // 25 — osiowość obrysu z tolerancją 4 mm (GA 523)
        // ================================================================

        [CommandMethod("RCSMOKE_HB_SQ523", CommandFlags.Modal)]
        public void HbSq523()
        {
            const string step = "SQ523";
            Run(step, db =>
            {
                Check(step, "obrys 523 z GA (dy = 0,0115 mm) → AutoRebar B1 generuje rozkłady", () =>
                {
                    CleanModelSpace(db);
                    _hb523 = HbPoly(db, "SD-PILED-RAFT", Plot523, null);
                    int r = GenB1(db, _hb523);
                    if (r < 0) throw new AssertFail("GenerateLayer B1 zwrócił " + r.ToString(Inv) + " (płyta pod kątem?)");
                    int n = HbSlabDists(db, _hb523).Count;
                    True(n > 0, "brak rozkładów ze znacznikiem płyty 523");
                    return "rozkłady=" + n.ToString(Inv);
                });
                Check(step, "obrys w rysunku bez zmian (wierzchołki bit w bit)", () =>
                {
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        var pl = (Polyline)tr.GetObject(_hb523, OpenMode.ForRead);
                        Int("wierzchołki", pl.NumberOfVertices, Plot523.Length);
                        for (int i = 0; i < Plot523.Length; i++)
                        {
                            var p = pl.GetPoint2dAt(i);
                            if (p.X != Plot523[i].X || p.Y != Plot523[i].Y)
                                throw new AssertFail($"wierzchołek {i}: ({p.X:R}, {p.Y:R}) ≠ ({Plot523[i].X:R}, {Plot523[i].Y:R})");
                        }
                    }
                    return "OK";
                });
                Check(step, "kontrola: krawędź z odchyłką 12,5 mm (> 4 mm) → odmowa, brak rozkładów", () =>
                {
                    var bad = HbPoly(db, "SD-PILED-RAFT", new[]
                    {
                        new Point2d(0, 700000), new Point2d(12000, 700012.5), new Point2d(12000, 708000), new Point2d(0, 708000),
                    }, null);
                    int r = GenB1(db, bad);
                    Int("GenerateLayer", r, -1);
                    return Int("rozkłady", HbSlabDists(db, bad).Count, 0);
                });
            });
        }

        // ================================================================
        // 26 — RC_COUNT_BBS z prętami AutoRebar
        // ================================================================

        [CommandMethod("RCSMOKE_HB_COUNT", CommandFlags.Modal)]
        public void HbCount()
        {
            const string step = "COUNT";
            Run(step, db =>
            {
                double legacyMass = 0;
                Check(step, "stary pręt RC_BAR (linia H16-200-B1, A = 2000) + RC_BAR na pręcie RC_SINGLE_BAR (nie liczyć 2×)", () =>
                {
                    var bar = Activator.CreateInstance(Plugin.GetType(BarDataType, true));
                    SetProp(bar, "Mark", "H16-200-B1");
                    SetProp(bar, "Diameter", 16);
                    SetProp(bar, "ShapeCode", "00");
                    SetProp(bar, "LengthA", 2000.0);
                    SetProp(bar, "LayerCode", "B1");
                    SetProp(bar, "Position", "BOT");
                    var single = HbBars(db).FirstOrDefault();
                    if (single.IsNull) throw new AssertFail("brak pręta RC_SINGLE_BAR (krok SQ523 nie wygenerował szablonu?)");
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                        var ln = new Line(new Point3d(0, 800000, 0), new Point3d(2000, 800000, 0));
                        ms.AppendEntity(ln);
                        tr.AddNewlyCreatedDBObject(ln, true);
                        Call("BricsCadRc.Core.XDataHelper", "Write", ln, bar);
                        Call("BricsCadRc.Core.XDataHelper", "Write", (Entity)tr.GetObject(single, OpenMode.ForWrite), bar);
                        tr.Commit();
                    }
                    legacyMass = 2.0 * 1.579;   // 1 × 2000 mm × H16
                    return "linia + RC_BAR na " + Hx(single);
                });
                Check(step, "CountAll: masa = RC_BBS (ReadBars) + 1 stary pręt RC_BAR", () =>
                {
                    var warnings = new List<string>();
                    var rows = ((IEnumerable)Call(CounterBbs, "CountAll", db, warnings)).Cast<object>().ToList();
                    True(rows.Count > 1, "wierszy: " + rows.Count.ToString(Inv));
                    double count = rows.Sum(r => Convert.ToDouble(Prop(r, "TotalMassKg"), Inv));
                    var bbs = Call(BbsReaderType, "ReadBars", db);
                    var bbsRows = ((IEnumerable)Prop(bbs, "Rows")).Cast<object>().ToList();
                    True(bbsRows.Count > 0, "RC_BBS bez wierszy");
                    double bbsMass = bbsRows.Sum(r => Convert.ToDouble(Prop(r, "MassKg"), Inv));
                    var legacy = rows.Where(r => (string)Prop(r, "Mark") == "H16-200-B1").ToList();
                    Int("wiersze RC_BAR", legacy.Count, 1);
                    Int("sztuki RC_BAR (bez pręta RC_SINGLE_BAR z dopisanym RC_BAR)", Convert.ToInt32(Prop(legacy[0], "BarLinesInDrawing"), Inv), 1);
                    return Num("masa [kg]", count, bbsMass + legacyMass, 1e-6) + " (BBS " + bbsMass.ToString("F3", Inv) + ")";
                });
            });
        }

        // ================================================================
        // 24 — etykieta płyty z nagłówka map
        // ================================================================

        [CommandMethod("RCSMOKE_HB_MAPLABEL", CommandFlags.Modal)]
        public void HbMapLabel()
        {
            const string step = "MAPLABEL";
            Run(step, db =>
            {
                Check(step, "GuessPlot: opis płyty „PLOT 526\\PSSL=…\\P225mm THK SLAB” → „PLOT 526”", () =>
                {
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                        var mt = new MText { Location = new Point3d(0, 900000, 0), TextHeight = 100, Contents = @"PLOT 526\PSSL=21.925\P225mm THK SLAB" };
                        ms.AppendEntity(mt);
                        tr.AddNewlyCreatedDBObject(mt, true);
                        tr.Commit();
                    }
                    var pt = Plugin.GetType(MapEngType + "+MapPlot", true);
                    var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(pt));
                    foreach (var l in new[] { "PLOT 525", "PLOT 526" })
                    {
                        var p = Activator.CreateInstance(pt);
                        pt.GetField("Label").SetValue(p, l);
                        list.Add(p);
                    }
                    return Int("GuessPlot", Convert.ToInt32(Call(MapEngType, "GuessPlot", db, list), Inv), 1);
                });
                Check(step, "AnalysisStore: dawna etykieta sklejona → „PLOT 526”", () =>
                {
                    Call(StoreType, "Save", db, "m.dxf", "r.xlsx", "PLOT 526h = 225 mm  |  fck = 28 MPa  |  KWL 2155");
                    var a = new object[] { db, null, null, null };
                    True((bool)Call(StoreType, "TryLoad", a), "TryLoad = false");
                    return Eq("płyta", a[3], "PLOT 526");
                });
                foreach (var (file, expect) in new[]
                {
                    (Path.Combine(HbMapsDir, "p466", "DE736TQ004_PLOT_466_punching_reinf_maps.dxf"), new[] { "PLOT 466" }),
                    (Path.Combine(HbMapsDir, "p1", "DE736TQ004_ALL_260910_punching_reinf_maps.dxf"), new[] { "PLOT 526", "PLOT 524", "GARAGE 526-527" }),
                })
                {
                    string name = "ReadSource " + Path.GetFileName(file) + ": etykiety " + string.Join(", ", expect) + ", bez „h = …”";
                    if (!File.Exists(file)) { Add("SKIP", step, name, "brak pliku " + file + " (ustaw RC_HB_MAPS)"); continue; }
                    Check(step, name, () =>
                    {
                        var warnings = new List<string>();
                        var src = Call(MapEngType, "ReadSource", file, warnings);
                        try
                        {
                            var labels = ((IEnumerable)Prop(src, "Plots")).Cast<object>().Select(p => (string)Prop(p, "Label")).ToList();
                            foreach (var e in expect) True(labels.Contains(e), "brak etykiety „" + e + "” (są: " + string.Join(" / ", labels.Take(5)) + ")");
                            var glued = labels.Where(l => l.IndexOf("h =", StringComparison.OrdinalIgnoreCase) >= 0).ToList();
                            True(glued.Count == 0, "etykiety sklejone: " + string.Join(" / ", glued.Take(3)));
                            return "płyt=" + labels.Count.ToString(Inv);
                        }
                        finally { ((IDisposable)src).Dispose(); }
                    });
                }
            });
        }

        // ================================================================
        // Pomocnicze HB
        // ================================================================

        private static void HbRegApp(Database db, string name)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                if (!rat.Has(name))
                {
                    rat.UpgradeOpen();
                    var rec = new RegAppTableRecord { Name = name };
                    rat.Add(rec);
                    tr.AddNewlyCreatedDBObject(rec, true);
                }
                tr.Commit();
            }
        }

        private static void HbLayer(Database db, string name)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(name))
                {
                    lt.UpgradeOpen();
                    var ltr = new LayerTableRecord { Name = name };
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                tr.Commit();
            }
        }

        /// <summary>Zamknięta polilinia na warstwie; xdata (opcjonalnie) — rekord RC_MAP [PLOT, mapa].</summary>
        private static ObjectId HbPoly(Database db, string layer, IList<Point2d> pts, string[] mapRec)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var pl = new Polyline(pts.Count);
                for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, pts[i], 0, 0, 0);
                pl.Closed = true;
                pl.Layer = layer;
                var id = ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                if (mapRec != null)
                    pl.XData = new ResultBuffer(
                        new TypedValue((int)DxfCode.ExtendedDataRegAppName, MapApp),
                        new TypedValue((int)DxfCode.ExtendedDataAsciiString, mapRec[0]),
                        new TypedValue((int)DxfCode.ExtendedDataAsciiString, mapRec[1]));
                tr.Commit();
                return id;
            }
        }

        /// <summary>Mapa B1 jak po RC_IMPORT_MAP: obrys PH-B1-SLAB 12000×8000 i strefa SD-B1 ADD 2000×1000 (bez tekstu As → H10).</summary>
        private static void HbMapB1(Database db, double x0, double y0, string plot)
        {
            var rec = new[] { plot, "B1" };
            HbPoly(db, "PH-B1-SLAB", new[]
            {
                new Point2d(x0, y0), new Point2d(x0 + 12000, y0), new Point2d(x0 + 12000, y0 + 8000), new Point2d(x0, y0 + 8000),
            }, rec);
            HbPoly(db, "SD-B1 ADD", new[]
            {
                new Point2d(x0 + 5000, y0 + 3500), new Point2d(x0 + 7000, y0 + 3500),
                new Point2d(x0 + 7000, y0 + 4500), new Point2d(x0 + 5000, y0 + 4500),
            }, rec);
        }

        private static List<ObjectId> HbBars(Database db) => ScanMs(db, e => Has(e, XApp));

        private static List<ObjectId> HbAddDists(Database db)
            => ScanMs(db, e => e is BlockReference && Has(e, DistApp2)).Where(id =>
            {
                var v = Rec(db, id, DistApp2);
                return v.Length > 1 && (v[1].Value as string ?? "").EndsWith("B1 ADD", StringComparison.Ordinal);
            }).ToList();

        private static List<ObjectId> HbSlabDists(Database db, ObjectId slab)
            => ScanMs(db, e => e is BlockReference && Has(e, DistApp2)).Where(id => Link(db, id, SlabApp, 1) == Hx(slab)).ToList();
    }
}
