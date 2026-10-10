// S-REG — przypadki poprawek błędów wzorca (docs/PLAN_POPRAWKI_W1_W2.md 5.2–5.3, docs/POPRAWKI_WZORCA.md).
// Ten sam scenariusz z DLL wzorca i z naszym DLL; różnice zrzutu = DOKŁADNIE lista expected-diffs (run-regression.ps1).
//
// Kolejność w reg.scr (po RC_REG_DUMP):
//   RC_REG_FIX_SETUP   — każdy przypadek w swoim pasie Y = 100000 + 10000·i (X od 0): pręty, etykiety, rozkłady
//   (komendy skryptu)  — _.STRETCH / _.ROTATE / _.MIRROR / _.MOVE / RC_UPDATE_BAR na współrzędnych pasów
//   RC_REG_FIX_CHECK   — edycje w kodzie: RC_EDIT_BAR (nasza: BarEditApply.Apply, wzorzec: te same publiczne kroki),
//                        ERASE (Entity.Erase w transakcji — przez overrule), zapis XData (ścieżka watchera 00)
//   RC_REG_DUMP_FIX <plik> — per przypadek: wiersze semantyczne + DrawingDump pasa (numeracja lokalna)
//
// Wiersze przypadku zaczynają się od jego Id („F1b bar …”, „F1b| …”) — run-regression.ps1 wycina po Id przypadki
// tylko-polilinia z porównania Block. Linie „#…” (nagłówki encji, notatki) są poza diffem.
// Dostęp do wtyczki wyłącznie przez refleksję, tylko API obecne w obu wersjach.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace RegressionDump
{
    public partial class RegCommands
    {
        private const double FixBaseY = 100000, FixStepY = 10000;

        /// <summary>Przypadek poprawki: Id (np. F1b), indeks pasa, obiekty.</summary>
        private sealed class FixCase
        {
            public string Id;
            public int Index;
            public bool PolylineOnly;
            public ObjectId Bar = ObjectId.Null, Bar2 = ObjectId.Null;
            public double Y0 => FixBaseY + FixStepY * Index;
        }

        /// <summary>
        /// Przypadki w STAŁEJ kolejności — indeksy = pasy Y, na które wskazują współrzędne w reg.scr.
        /// PolylineOnly — tylko ścieżka polilinii (F1e, F4d, F7a–c); w przebiegu Block tworzone, ale pomijane w porównaniu.
        /// </summary>
        private static readonly FixCase[] FixCases =
        {
            new FixCase { Id = "F1b", Index = 1 },
            new FixCase { Id = "F1c", Index = 2 },
            new FixCase { Id = "F1e", Index = 3,  PolylineOnly = true },
            new FixCase { Id = "F2a", Index = 4 },
            new FixCase { Id = "F2b", Index = 5 },
            new FixCase { Id = "F4a", Index = 6 },
            new FixCase { Id = "F4b", Index = 7 },
            new FixCase { Id = "F4c", Index = 8 },
            new FixCase { Id = "F4d", Index = 9,  PolylineOnly = true },
            new FixCase { Id = "F5a", Index = 10 },
            new FixCase { Id = "F5b", Index = 11 },
            new FixCase { Id = "F7a", Index = 12, PolylineOnly = true },
            new FixCase { Id = "F7b", Index = 13, PolylineOnly = true },
            new FixCase { Id = "F7c", Index = 14, PolylineOnly = true },
            // W2 — mapa klonowania (#3, #6, 3b): komendy kopiowania w reg.scr, kopie w tym samym pasie (przesunięcie w X)
            new FixCase { Id = "F3a", Index = 15 },   // COPY pręt + etykieta + rozkład + opis
            new FixCase { Id = "F3b", Index = 16 },   // COPY rozkład + opis (kontrola: [18] → oryginał)
            new FixCase { Id = "F3c", Index = 17 },   // MIRROR Y zestawu (BricsCAD V25: odbicie w miejscu — kontrola)
            new FixCase { Id = "F3d", Index = 18 },   // _-ARRAY 1×3
            new FixCase { Id = "F3e", Index = 19 },   // -WBLOCK rozkładu + opisu, -INSERT * (inny rysunek)
            new FixCase { Id = "F6",  Index = 20 },   // A bez etykiety, B z etykietą 40 mm nad A; COPY A, B, etykiety
            new FixCase { Id = "F5c", Index = 21 },   // COPY 44, RC_EDIT_BAR oryginału
            new FixCase { Id = "F3f", Index = 22 },   // MIRROR N zestawu (geometria opisu po normalizacji — 0-POPRAWKI p.1)
            // Przeniesienie zmian kolegi (docs/PLAN_PRZENIESIENIA_2026-10-09.md)
            new FixCase { Id = "F12", Index = 23 },   // K6 (#12): _.STRETCH końca 00 do 20 mm (< 50) — pręt sprzed STRETCH
            new FixCase { Id = "F14", Index = 24 },   // K7 (B3 ADD, D1): płyta L — B3 ADD w narożniku wklęsłym (wzorzec: brak metody)
            new FixCase { Id = "F3g", Index = 25 },   // K5 (3c): B1 na płycie, COPY płyty z rozkładami (+20000 w X), B1 ponownie na oryginale
            // K1 planu PLAN_BELKI_PRAKTYKA (#29): nib zawinięty — pas 26 pusty (odstęp od F3g na opisy), płyta w pasie 27
            new FixCase { Id = "F29", Index = 27 },   // płyta 12000×6000, nib 135 wzdłuż prawej krawędzi zawinięty 600 na górnej i dolnej; T2, UB B2
            // K7 planu PLAN_BELKI_PRAKTYKA (#30, E2): siatka przy belce — pas 28 pusty (opisy F29), płyta w pasie 29
            new FixCase { Id = "FBEAM", Index = 29 }, // płyta 12000×6000, nib 160 wzdłuż lewej krawędzi, strefa belki RC_BEAM 600 od uskoku; B1, B2, UB B1;
                                                      // rzut górny (+20000 w X, własna strefa [6]): T1, T2; UB 03 (rzut dolny)
        };

        /// <summary>Plik -WBLOCK przypadku F3e (ścieżka bez spacji — skrypt); usuwany w RC_REG_FIX_SETUP.</summary>
        private const string F3eFile = "C:/Users/test/AppData/Local/Temp/reg_f3e.dwg";

        private static readonly List<string> _fixLog = new List<string>();

        private static FixCase Fx(string id) => FixCases.First(c => c.Id == id);

        // ================================================================
        // Komendy
        // ================================================================

        [CommandMethod("RC_REG_FIX_SETUP", CommandFlags.Modal)]
        public void FixSetup()
        {
            _fixLog.Clear();
            var db = Application.DocumentManager.MdiActiveDocument.Database;
            foreach (var c in FixCases) { c.Bar = ObjectId.Null; c.Bar2 = ObjectId.Null; }

            // #1 — RC_EDIT_BAR dolicza zmianę dwa razy (F1b: 21, zmiana C; F1c: 00 3000 → 1000; F1e: STRETCH końca)
            FixStep("F1b.setup", () =>
            {
                var c = Fx("F1b");
                c.Bar = PlaceBarWithLabel(db, 10, "21", 500, 300, 500, NextPos(db), new Point3d(0, c.Y0, 0));
                return Distribute(db, c.Bar, 40, c.Y0 + 2040, 3040, c.Y0 + 3000, true, 200, "B1");
            });
            FixStep("F1c.setup", () => SetupStraight(db, Fx("F1c"), 3000, label: true, dist: true));
            FixStep("F1e.setup", () => SetupStraight(db, Fx("F1e"), 3000, label: true, dist: true));

            // #2 — ERASE pręta bez etykiety (F2a) / kopia z [13] → cudza etykieta (F2b, kontrola)
            FixStep("F2a.setup", () => SetupStraight(db, Fx("F2a"), 3000, label: false, dist: true));
            FixStep("F2b.setup", () =>
            {
                var c = Fx("F2b");
                string r = SetupStraight(db, c, 3000, label: true, dist: false);
                // „Kopia” B: ten sam numer, [13] → etykieta A (back-link etykiety → A), własny rozkład
                object barA = ReadBar(db, c.Bar);
                c.Bar2 = PlaceBarWithLabel(db, 12, "00", 3000, 0, 0, Pos(barA), new Point3d(0, c.Y0 + 1000, 0), withLabel: false);
                SetLabelHandle(db, c.Bar2, (string)Get(barA, "LabelHandle"));
                return r + "; B " + Distribute(db, c.Bar2, 40, c.Y0 + 2040, 3040, c.Y0 + 3000, true, 200, "B1");
            });

            // #4 — przebudowa zawsze w poziomie (F4a: 21 ROTATE; F4b: 00 ROTATE + A 3500; F4c: MIRROR N; F4d: STRETCH obróconego)
            FixStep("F4a.setup", () =>
            {
                var c = Fx("F4a");
                c.Bar = PlaceBarWithLabel(db, 10, "21", 500, 300, 500, NextPos(db), new Point3d(0, c.Y0, 0), withLabel: false);
                return BarMark(db, c.Bar);
            });
            FixStep("F4b.setup", () => SetupStraight(db, Fx("F4b"), 3000, label: false, dist: false));
            FixStep("F4c.setup", () =>
            {
                var c = Fx("F4c");
                c.Bar = PlaceBarWithLabel(db, 10, "21", 500, 300, 500, NextPos(db), new Point3d(0, c.Y0, 0));
                return BarMark(db, c.Bar);
            });
            FixStep("F4d.setup", () => SetupStraight(db, Fx("F4d"), 3000, label: false, dist: false));

            // #5 — kształt 44 (F5a: MOVE; F5b: A 300 → 400)
            foreach (var id in new[] { "F5a", "F5b" })
            {
                var c = Fx(id);
                FixStep(id + ".setup", () =>
                {
                    c.Bar = PlaceBarWithLabel(db, 12, RingShapeCode, 300, 0, 0, NextPos(db), new Point3d(0, c.Y0, 0), withLabel: false);
                    return BarMark(db, c.Bar);
                });
            }

            // #7 — TotalLength po zmianie A (F7a: watcher 00; F7b: LengthOverridden — kontrola; F7c: RC_UPDATE_BAR)
            FixStep("F7a.setup", () => SetupStraight(db, Fx("F7a"), 3000, label: false, dist: false));
            FixStep("F7b.setup", () => SetupStraight(db, Fx("F7b"), 3000, label: false, dist: false));
            FixStep("F7c.setup", () =>
            {
                var c = Fx("F7c");
                string r = SetupStraight(db, c, 3000, label: false, dist: false);
                // TotalLength 2000 przy A 3000 (np. stary rekord) — RC_UPDATE_BAR w skrypcie
                WriteBarFields(db, c.Bar, b => Set(b, "TotalLength", 2000.0));
                return r;
            });

            // W2 — #3 / #6 / 3b: zestawy pod komendy kopiowania w reg.scr
            foreach (var id in new[] { "F3a", "F3b", "F3c", "F3d", "F3e", "F3f" })
            {
                var c = Fx(id);
                FixStep(id + ".setup", () => SetupStraight(db, c, 3000, label: true, dist: true));
            }
            FixStep("F3e.file", () =>
            {
                if (File.Exists(F3eFile)) File.Delete(F3eFile);
                return "OK";
            });
            FixStep("F6.setup", () =>
            {
                var c = Fx("F6");
                c.Bar = PlaceBarWithLabel(db, 12, "00", 3000, 0, 0, NextPos(db), new Point3d(0, c.Y0, 0), withLabel: false);
                c.Bar2 = PlaceBarWithLabel(db, 12, "00", 3000, 0, 0, NextPos(db), new Point3d(0, c.Y0 + 40, 0));
                return BarMark(db, c.Bar) + "; " + BarMark(db, c.Bar2);
            });
            FixStep("F5c.setup", () =>
            {
                var c = Fx("F5c");
                c.Bar = PlaceBarWithLabel(db, 12, RingShapeCode, 300, 0, 0, NextPos(db), new Point3d(0, c.Y0, 0), withLabel: false);
                return BarMark(db, c.Bar);
            });

            // K6 (#12): zestaw pod _.STRETCH końca w reg.scr
            FixStep("F12.setup", () => SetupStraight(db, Fx("F12"), 3000, label: true, dist: true));
            // K7 (B3 ADD): płyta L w pasie (Y0 ± 4000), narożnik wklęsły (6000, Y0); Bar = obrys płyty
            FixStep("F14.setup", () =>
            {
                var c = Fx("F14");
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, SlabLayer, 7);
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    var pl = new Polyline(6);
                    var pts = new[] { (0.0, -4000.0), (12000.0, -4000.0), (12000.0, 0.0), (6000.0, 0.0), (6000.0, 4000.0), (0.0, 4000.0) };
                    for (int i = 0; i < pts.Length; i++) pl.AddVertexAt(i, new Point2d(pts[i].Item1, c.Y0 + pts[i].Item2), 0, 0, 0);
                    pl.Closed = true;
                    pl.Layer = SlabLayer;
                    c.Bar = ms.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                    tr.Commit();
                }
                return "OK";
            });
            // #29 (K1): płyta 12000×6000 (Y0 ± 3000) + linia uskoku: nib 135 wzdłuż prawej krawędzi, zawinięty 600 mm na górnej
            // i dolnej krawędzi (jak PLOT 321 / 525); Bar = obrys płyty. Generowanie na końcu RC_REG_FIX_CHECK (numeracja).
            FixStep("F29.setup", () =>
            {
                var c = Fx("F29");
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, SlabLayer, 7);
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    c.Bar = AddRect(ms, tr, 0, c.Y0 - 3000, 12000, c.Y0 + 3000, SlabLayer);
                    var pl = new Polyline(8);
                    var pts = new[] { (0.0, -3000.0), (11400.0, -3000.0), (11400.0, -2865.0), (11865.0, -2865.0),
                                      (11865.0, 2865.0), (11400.0, 2865.0), (11400.0, 3000.0), (0.0, 3000.0) };
                    for (int i = 0; i < pts.Length; i++) pl.AddVertexAt(i, new Point2d(pts[i].Item1, c.Y0 + pts[i].Item2), 0, 0, 0);
                    pl.Closed = true;
                    pl.Layer = SlabLayer;
                    ms.AppendEntity(pl);
                    tr.AddNewlyCreatedDBObject(pl, true);
                    tr.Commit();
                }
                return "OK";
            });
            // #30 (K7, E2): płyta 12000×6000 (Y0 ± 3000) + linia uskoku 160 wzdłuż lewej krawędzi + strefa belki (warstwa RC-BEAM, rekord
            // RC_BEAM v2: H 375, szer. 600, [6] = obrys) x 160..760, y ± 2840 (końce przy krawędzi). Wzorzec strefy nie zna (zwykła polilinia
            // poza zakresem nibu); nasz: siatka przy belce (E2). Rzut górny (poprawka recenzji K7, pkt 2): przystający obrys +20000 w X
            // z linią uskoku i własną strefą ([6] = obrys rzutu górnego) — T1 / T2 z E2 (Bar2 = obrys górny). Generowanie na końcu
            // RC_REG_FIX_CHECK.
            FixStep("FBEAM.setup", () =>
            {
                var c = Fx("FBEAM");
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, SlabLayer, 7);
                    EnsureLayer(db, tr, "RC-BEAM", 6);
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
                    if (!rat.Has("RC_BEAM"))
                    {
                        rat.UpgradeOpen();
                        var rec = new RegAppTableRecord { Name = "RC_BEAM" };
                        rat.Add(rec);
                        tr.AddNewlyCreatedDBObject(rec, true);
                    }
                    // rzut dolny (x 0) i górny (x 20000): obrys, linia uskoku, strefa belki z [6] = obrys tego rzutu
                    foreach (double dx in new[] { 0.0, 20000.0 })
                    {
                        var outer = AddRect(ms, tr, dx, c.Y0 - 3000, dx + 12000, c.Y0 + 3000, SlabLayer);
                        if (dx == 0) c.Bar = outer; else c.Bar2 = outer;
                        AddRect(ms, tr, dx + 160, c.Y0 - 3000, dx + 12000, c.Y0 + 3000, SlabLayer);
                        var zone = AddRect(ms, tr, dx + 160, c.Y0 - 2840, dx + 760, c.Y0 + 2840, "RC-BEAM");
                        var ze = (Entity)tr.GetObject(zone, OpenMode.ForWrite);
                        ze.XData = new ResultBuffer(
                            new TypedValue((int)DxfCode.ExtendedDataRegAppName, "RC_BEAM"),
                            new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)2),
                            new TypedValue((int)DxfCode.ExtendedDataReal, 375.0),
                            new TypedValue((int)DxfCode.ExtendedDataReal, 600.0),
                            new TypedValue((int)DxfCode.ExtendedDataReal, 160.0),
                            new TypedValue((int)DxfCode.ExtendedDataReal, 150.0),
                            new TypedValue((int)DxfCode.ExtendedDataHandle, outer.Handle));
                    }
                    tr.Commit();
                }
                return "OK";
            });
            // K5 (3c): płyta 12000×8000 w pasie (Y0 ± 4000) + AutoRebar B1; Bar = obrys płyty
            FixStep("F3g.setup", () =>
            {
                var c = Fx("F3g");
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    EnsureLayer(db, tr, SlabLayer, 7);
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    c.Bar = AddRect(ms, tr, 0, c.Y0 - SlabH / 2, SlabW, c.Y0 + SlabH / 2, SlabLayer);
                    tr.Commit();
                }
                var doc = Application.DocumentManager.MdiActiveDocument;
                return Call("AutoRebarEngine", "GenerateLayer", doc, c.Bar, "rebar_bottom", "X", "B1", 10, 200.0, 40.0, true);
            });
        }

        [CommandMethod("RC_REG_FIX_CHECK", CommandFlags.Modal)]
        public void FixCheck()
        {
            var db = Application.DocumentManager.MdiActiveDocument.Database;

            FixStep("F1b.edit", () => FixEditBar(db, Fx("F1b").Bar, b => Set(b, "LengthC", 700.0)));
            FixStep("F1c.edit", () => FixEditBar(db, Fx("F1c").Bar, b => Set(b, "LengthA", 1000.0)));
            FixStep("F2a.erase", () => { EraseEntity(db, Fx("F2a").Bar); return "OK"; });
            FixStep("F2b.erase", () => { EraseEntity(db, Fx("F2b").Bar2); return "OK"; });
            FixStep("F4b.edit", () => FixEditBar(db, Fx("F4b").Bar, b => Set(b, "LengthA", 3500.0)));
            FixStep("F5b.edit", () => FixEditBar(db, Fx("F5b").Bar, b => Set(b, "LengthA", 400.0)));
            FixStep("F5c.edit", () => FixEditBar(db, Fx("F5c").Bar, b => Set(b, "LengthA", 400.0)));
            // Ścieżka watchera 00 (BarGeometryWatcher: zapis A z obrysu): XData A ≠ obrys → w CommandEnded A z obrysu
            FixStep("F7a.xdata", () =>
            {
                WriteBarFields(db, Fx("F7a").Bar, b => { Set(b, "LengthA", 2500.0); Set(b, "TotalLength", 2500.0); });
                return "OK";
            });
            FixStep("F7b.xdata", () =>
            {
                WriteBarFields(db, Fx("F7b").Bar, b =>
                {
                    Set(b, "LengthA", 2500.0);
                    Set(b, "LengthOverridden", true);
                    Set(b, "TotalLength", 3333.0);
                });
                return "OK";
            });
            // K5 (3c): ponowne B1 na ORYGINALE po COPY płyty z rozkładami (reg.scr) — wzorzec kasuje rozkłady kopii
            FixStep("F3g.regen", () =>
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                return Call("AutoRebarEngine", "GenerateLayer", doc, Fx("F3g").Bar, "rebar_bottom", "X", "B1", 10, 200.0, 40.0, true);
            });
            // K7 (B3 ADD, decyzja D1): na końcu — nowy szablon H10 L=1250 nie przesuwa numeracji innych przypadków.
            // Wzorzec aba0ed1 nie ma GenerateB3Corners → wynik „brak B3 ADD” bez wyjątku (różnica oczekiwana 14).
            FixStep("F14.b3", () =>
            {
                if (PT("AutoRebarEngine").GetMethod("GenerateB3Corners",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic) == null)
                    return "brak B3 ADD (wtyczka bez GenerateB3Corners)";
                var doc = Application.DocumentManager.MdiActiveDocument;
                return Call("AutoRebarEngine", "GenerateB3Corners", doc, Fx("F14").Bar);
            });
            // #29 (K1): nib zawinięty — góra T2 i UB B2 (UB 02 na krawędziach uskoku), bez dołu B2 (liczba prętów dołu z długości;
            // szablon H10 przesunąłby numer szablonu B3 ADD przypadku F14). Wzorzec: UB 02 / pręty T2 na krótkich krawędziach
            // uskoku zawinięcia liczone z całej krawędzi zewnętrznej (58 UB co 6,6 mm na 465 mm).
            FixStep("F29.t2", () => Call("AutoRebarEngine", "GenerateLayer", Application.DocumentManager.MdiActiveDocument,
                                         Fx("F29").Bar, "rebar_top", "Y", "T2", 12, 200.0, 40.0, true));
            FixStep("F29.ub2", () => Call("AutoRebarEngine", "GenerateUBLayer", Application.DocumentManager.MdiActiveDocument,
                                          Fx("F29").Bar, "rebar_bottom", "B2", 225, "Y", 200.0, 40.0, true));
            // #30 (K7, E2): siatka dolna B1 / B2 i UB B1 na płycie z belką (na końcu — numeracja innych przypadków bez zmian).
            // Wzorzec: siatka jak bez belki (B2 w nibie i w belce, UB 01 na linii uskoku); nasz: B1 do linii uskoku − 40, B2 od lica + 50,
            // UB 01 tylko na prawej krawędzi.
            FixStep("FBEAM.b1", () => Call("AutoRebarEngine", "GenerateLayer", Application.DocumentManager.MdiActiveDocument,
                                           Fx("FBEAM").Bar, "rebar_bottom", "X", "B1", 10, 200.0, 40.0, true));
            FixStep("FBEAM.b2", () => Call("AutoRebarEngine", "GenerateLayer", Application.DocumentManager.MdiActiveDocument,
                                           Fx("FBEAM").Bar, "rebar_bottom", "Y", "B2", 10, 200.0, 40.0, true));
            FixStep("FBEAM.ub1", () => Call("AutoRebarEngine", "GenerateUBLayer", Application.DocumentManager.MdiActiveDocument,
                                            Fx("FBEAM").Bar, "rebar_bottom", "B1", 225, "X", 200.0, 40.0, true));
            // Poprawka recenzji K7 (pkt 2): rzut górny ze strefą — T1 / T2 (góra do linii uskoku, krawędź uskoku przy belce bez reguły
            // UB 01/02, równoległe od s/2 od lica; rzut dolny = FBEAM.Bar) i UB 03 w nibie (liczba z prętów dołu rzutu dolnego).
            FixStep("FBEAM.t1", () => Call("AutoRebarEngine", "GenerateLayer", Application.DocumentManager.MdiActiveDocument,
                                           Fx("FBEAM").Bar2, "rebar_top", "X", "T1", 12, 200.0, 40.0, true));
            FixStep("FBEAM.t2", () => Call("AutoRebarEngine", "GenerateLayer", Application.DocumentManager.MdiActiveDocument,
                                           Fx("FBEAM").Bar2, "rebar_top", "Y", "T2", 12, 200.0, 40.0, true));
            FixStep("FBEAM.ubnib", () => Call("AutoRebarEngine", "GenerateNibUBLayer", Application.DocumentManager.MdiActiveDocument,
                                              Fx("FBEAM").Bar, "rebar_bottom", true));
        }

        [CommandMethod("RC_REG_DUMP_FIX", CommandFlags.Modal)]
        public void DumpFix()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            var db = doc.Database;
            var ed = doc.Editor;
            var pr = ed.GetString(new PromptStringOptions("\nPlik zrzutu przypadków: ") { AllowSpaces = true });
            if (pr.Status != PromptStatus.OK || string.IsNullOrWhiteSpace(pr.StringResult))
            {
                ed.WriteMessage($"\n{Prefix} Brak ścieżki pliku — przerwano.\n");
                return;
            }
            string path = pr.StringResult.Trim().Trim('"');

            WriteDump(ed, path, BuildFixDump(db, false));
            if ((Environment.GetEnvironmentVariable(EnvBarEquiv) ?? "").Trim() == "1")
            {
                string eqPath = Path.Combine(Path.GetDirectoryName(path),
                                             Path.GetFileNameWithoutExtension(path) + "-bareq" + Path.GetExtension(path));
                WriteDump(ed, eqPath, BuildFixDump(db, true));
            }
        }

        // ================================================================
        // Zrzut przypadków
        // ================================================================

        private static List<string> BuildFixDump(Database db, bool barEquiv)
        {
            var lines = new List<string>();
            try
            {
                lines.Add("# S-REG zrzut przypadków poprawek (linie z # nie są porównywane)" + (barEquiv ? " — tryb bar-equiv" : ""));
                lines.Add("# plugin=" + SafeLoc());
                lines.Add("# czas=" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
                lines.AddRange(_notes);
                lines.Add("== FIX-KROKI");
                lines.AddRange(_fixLog);

                foreach (var c in FixCases)
                {
                    lines.Add("== FIX " + c.Id);
                    double y0 = c.Y0;
                    Func<Entity, bool> inBand = e => InBand(e, y0);
                    var block = new List<string>();
                    Step2(block, () => Semantic(db, c.Id, inBand));
                    Step2(block, () => DrawingDump.Run(db, barEquiv, inBand, withLayers: false));
                    foreach (var l in block)
                        lines.Add(l.StartsWith("#", StringComparison.Ordinal) ? l : c.Id + "| " + l);
                }
                lines.Add("END");
            }
            catch (System.Exception ex)
            {
                lines.Add("FATAL " + ex.GetType().Name + ": " + ex.Message);
                lines.Add("END");
            }
            return lines;
        }

        /// <summary>
        /// Plan W4c: kod kształtu pręta-pierścienia wtyczki — BarFrameRules.RingShapeCode („75” po W4c, BS 8666:2020);
        /// wzorzec i wtyczka sprzed W4c (brak pola) → „44”. Różnica wzorzec ↔ nasz: expected-diffs 08 (# bug: 8).
        /// </summary>
        private static string RingShapeCode
        {
            get
            {
                var f = Plugin.GetType("BricsCadRc.Core.BarFrameRules", false)?.GetField("RingShapeCode",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                return f != null ? (string)f.GetValue(null) : "44";
            }
        }

        /// <summary>Pierścień wg wtyczki (BarFrameRules.IsRingShape); brak metody (wzorzec) → kod „44”.</summary>
        private static bool IsRingCode(string code)
        {
            var m = Plugin.GetType("BricsCadRc.Core.BarFrameRules", false)?.GetMethod("IsRingShape",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, null, new[] { typeof(string) }, null);
            return m != null ? (bool)m.Invoke(null, new object[] { code }) : code == "44";
        }

        /// <summary>Pas przypadku: środek GeometricExtents w [Y0 − 5000, Y0 + 5000).</summary>
        private static bool InBand(Entity e, double y0)
        {
            try
            {
                var x = e.GeometricExtents;
                double cy = (x.MinPoint.Y + x.MaxPoint.Y) / 2.0;
                return cy >= y0 - FixStepY / 2 && cy < y0 + FixStepY / 2;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("RegFixCommands.InBand: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Wiersze semantyczne przypadku: pręty (wymiary, TL, nadpisanie, kąt, odbicie, liczba pierścieni),
        /// rozkłady (Mark, liczba, długość, pręt źródłowy), etykiety (treść, pręt z back-linku).
        /// Kąt i odbicie z obrysu w WCS — ta sama metoda dla polilinii i pręta-bloku (bar-equiv).
        /// </summary>
        private static List<string> Semantic(Database db, string id, Func<Entity, bool> inBand)
        {
            var bars = new List<string>();
            var dists = new List<string>();
            var labels = new List<string>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                var all = new List<Entity>();
                foreach (ObjectId oid in ms)
                {
                    if (oid.IsErased) continue;
                    if (tr.GetObject(oid, OpenMode.ForRead) is Entity e && inBand(e)) all.Add(e);
                }

                foreach (var e in all)
                {
                    var bar = Call("SingleBarEngine", "ReadBarXData", e);
                    if (bar != null)
                    {
                        var rings = WcsRings(tr, e, all);
                        string geo = rings.Count == 0 ? "angle=? mirrored=?"
                                   : IsRingCode((string)Get(bar, "ShapeCode")) ? RingFrame(rings[0]) : AxisFrame(rings[0]);
                        bars.Add($"{id} bar Mark={Get(bar, "Mark")} shape={Get(bar, "ShapeCode")} A={Fmt(Get(bar, "LengthA"))} " +
                                 $"B={Fmt(Get(bar, "LengthB"))} C={Fmt(Get(bar, "LengthC"))} TL={Fmt(Get(bar, "TotalLength"))} " +
                                 $"ovr={Fmt(Get(bar, "LengthOverridden"))} {geo} rings={rings.Count.ToString(Inv)} " +
                                 $"label={Resolve(db, tr, (string)Get(bar, "LabelHandle"))}");
                        continue;
                    }
                    if (e is BlockReference br)
                    {
                        var dx = Call("BarBlockEngine", "ReadXData", br);
                        if (dx != null)
                            dists.Add($"{id} dist Mark={Get(dx, "Mark")} count={Fmt(Get(dx, "EffectiveCount"))} " +
                                      $"A={Fmt(Get(dx, "LengthA"))} src={Resolve(db, tr, (string)Get(dx, "SourceBarHandle"))}");
                        continue;
                    }
                    if (e is MLeader ml)
                    {
                        string back = (string)Call("SingleBarEngine", "ReadBarHandleFromLabel", ml);
                        if (back != null)
                            labels.Add($"{id} label \"{ml.MText?.Contents}\" bar={Resolve(db, tr, back)}");
                    }
                }
            }
            bars.Sort(StringComparer.Ordinal);
            dists.Sort(StringComparer.Ordinal);
            labels.Sort(StringComparer.Ordinal);
            return bars.Concat(dists).Concat(labels).ToList();
        }

        /// <summary>Handle → Mark pręta / „etykieta” / „usunięty” / „brak” / „inny”.</summary>
        private static string Resolve(Database db, Transaction tr, string h)
        {
            if (string.IsNullOrEmpty(h)) return "brak";
            if (!long.TryParse(h, NumberStyles.HexNumber, Inv, out long hv)) return "zły:" + h;
            if (!db.TryGetObjectId(new Handle(hv), out var oid) || oid.IsNull) return "brak-obiektu";
            if (oid.IsErased) return "usunięty";
            var o = tr.GetObject(oid, OpenMode.ForRead) as Entity;
            if (o == null) return "inny";
            var b = Call("SingleBarEngine", "ReadBarXData", o);
            if (b != null) return "pręt " + Get(b, "Mark");
            if (o is MLeader) return "etykieta";
            return "inny";
        }

        /// <summary>Pierścienie obrysu pręta w WCS: polilinia + towarzysze RC_BAR_LINK z pasa; blok — polilinie BTR.</summary>
        private static List<List<Point3d>> WcsRings(Transaction tr, Entity e, List<Entity> band)
        {
            var res = new List<List<Point3d>>();
            if (e is Polyline pl)
            {
                res.Add(Verts(pl, Matrix3d.Identity));
                string h = e.Handle.Value.ToString("X", Inv);
                foreach (var o in band)
                {
                    if (!(o is Polyline cp)) continue;
                    using (var rb = cp.GetXDataForApplication("RC_BAR_LINK"))
                    {
                        var v = rb?.AsArray();
                        if (v == null || v.Length < 2) continue;
                        string t = v[1].Value is Handle hh ? hh.Value.ToString("X", Inv) : Convert.ToString(v[1].Value, Inv);
                        if (long.TryParse(t, NumberStyles.HexNumber, Inv, out long tv) && tv == e.Handle.Value)
                            res.Add(Verts(cp, Matrix3d.Identity));
                    }
                }
            }
            else if (e is BlockReference br)
            {
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                foreach (ObjectId id in btr)
                    if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Polyline bp)
                        res.Add(Verts(bp, br.BlockTransform));
            }
            return res;
        }

        private static List<Point3d> Verts(Polyline pl, Matrix3d xf)
        {
            var r = new List<Point3d>(pl.NumberOfVertices);
            for (int i = 0; i < pl.NumberOfVertices; i++) r.Add(pl.GetPoint3dAt(i).TransformBy(xf));
            return r;
        }

        /// <summary>Kąt i odbicie z obrysu left + reverse(right): oś axis[0]→axis[1], lewa strona = v0 − axis[0].</summary>
        private static string AxisFrame(List<Point3d> v)
        {
            int t = v.Count;
            if (t < 4 || t % 2 != 0) return "angle=? mirrored=?";
            var a0 = Mid(v[0], v[t - 1]);
            var a1 = Mid(v[1], v[t - 2]);
            var dir = a1 - a0;
            var side = v[0] - a0;
            double cross = dir.X * side.Y - dir.Y * side.X;
            double ang = Math.Atan2(dir.Y, dir.X) * 180.0 / Math.PI;
            return $"angle={DrawingDump.F(ang)} mirrored={(cross < 0).ToString(Inv)} axis0={DrawingDump.P(a0)}";
        }

        /// <summary>Pierścień: środek = średnia, kąt v0 − c, odbicie = obieg zgodny z ruchem wskazówek zegara.</summary>
        private static string RingFrame(List<Point3d> v)
        {
            if (v.Count < 3) return "angle=? mirrored=?";
            double cx = v.Average(p => p.X), cy = v.Average(p => p.Y);
            var c = new Point3d(cx, cy, 0);
            var r0 = v[0] - c;
            var r1 = v[1] - c;
            double cross = r0.X * r1.Y - r0.Y * r1.X;
            double ang = Math.Atan2(r0.Y, r0.X) * 180.0 / Math.PI;
            return $"angle={DrawingDump.F(ang)} mirrored={(cross < 0).ToString(Inv)} center={DrawingDump.P(c)} " +
                   $"r={DrawingDump.F(r0.Length)}";
        }

        private static Point3d Mid(Point3d a, Point3d b) => new Point3d((a.X + b.X) / 2, (a.Y + b.Y) / 2, 0);

        // ================================================================
        // Pomocnicze
        // ================================================================

        private static void FixStep(string name, Func<object> f) => Step("FIX." + name, f, _fixLog);

        private static int NextPos(Database db)
        {
            var used = Call("PositionCounter", "GetUsedPositionNumbers", db);
            return (int)Call("PositionCounter", "NextAuto", db, used);
        }

        /// <summary>Pręt 00 Ø12 w (0, Y0), opcjonalnie z etykietą i rozkładem B1 (Y0 + 2040 … Y0 + 3000).</summary>
        private static string SetupStraight(Database db, FixCase c, double a, bool label, bool dist)
        {
            c.Bar = PlaceBarWithLabel(db, 12, "00", a, 0, 0, NextPos(db), new Point3d(0, c.Y0, 0), withLabel: label);
            string r = BarMark(db, c.Bar);
            if (dist) r += "; " + Distribute(db, c.Bar, 40, c.Y0 + 2040, 3040, c.Y0 + 3000, true, 200, "B1");
            return r;
        }

        private static object ReadBar(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                return Call("SingleBarEngine", "ReadBarXData", (Entity)tr.GetObject(id, OpenMode.ForRead));
        }

        private static string BarMark(Database db, ObjectId id) => (string)Get(ReadBar(db, id), "Mark");

        private static int Pos(object bar) => (int)Call("SingleBarEngine", "ExtractPosNr", (string)Get(bar, "Mark"));

        /// <summary>Zmiana pól rekordu RC_SINGLE_BAR i zapis (SingleBarEngine.WriteXData — w obu wersjach).</summary>
        private static void WriteBarFields(Database db, ObjectId id, Action<object> change)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                var bar = Call("SingleBarEngine", "ReadBarXData", ent);
                change(bar);
                Call("SingleBarEngine", "WriteXData", ent, bar);
                tr.Commit();
            }
        }

        private static void SetLabelHandle(Database db, ObjectId id, string handle)
            => WriteBarFields(db, id, b => Set(b, "LabelHandle", handle));

        private static void EraseEntity(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                tr.GetObject(id, OpenMode.ForWrite).Erase();
                tr.Commit();
            }
        }

        /// <summary>
        /// RC_EDIT_BAR po dialogu (jak REC.bar3_edit): nasza wtyczka — BarEditApply.Apply, wzorzec — EditBarSteps.
        /// TotalLength jak BarElevationDialog (wg kształtu), numer pozycji bez zmian.
        /// </summary>
        private static string FixEditBar(Database db, ObjectId id, Action<object> change)
        {
            object old, upd;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ent = (Entity)tr.GetObject(id, OpenMode.ForRead);
                old = Call("SingleBarEngine", "ReadBarXData", ent);
                upd = Call("SingleBarEngine", "ReadBarXData", ent);
            }
            change(upd);
            Set(upd, "TotalLength", TotalLength(upd));
            string posNrStr = Pos(old).ToString("D2", Inv);
            if (Plugin.GetType("BricsCadRc.Core.BarEditApply", false) != null)
            {
                var ok = Call("BarEditApply", "Apply", db, null, id, old, upd, posNrStr);
                if (!(ok is bool b) || !b) throw new InvalidOperationException("BarEditApply.Apply = " + Fmt(ok));
            }
            else
            {
                EditBarSteps(db, id, old, upd, posNrStr);
            }
            return (string)Get(upd, "Mark");
        }
    }
}
