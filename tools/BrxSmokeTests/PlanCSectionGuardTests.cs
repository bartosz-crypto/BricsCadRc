// SG — testy dymne bezpiecznika aktualności przekrojów RC_SECTION (docs/PLAN_BEZPIECZNIK_PRZEKROJOW_2026-10-10.md, etap 1)
// w BricsCAD V25. Uruchamiane skryptem planc-sg.scr (run-planc-sg.ps1). Komendy RCSMOKE_SG_*.
// Osobna klasa z własną (skróconą) infrastrukturą. Dostęp do wtyczki WYŁĄCZNIE przez refleksję (zestaw NaSmartRc z NETLOAD).
//
// Rysunek: płyta 6000 × 4000 (rzut dolny w (0, 0), górny w (0, 10000)), opis „SSL=21.925 / 225mm THK SLAB”, rozkłady RC_BAR_BLOCK:
// D1 B1 (X, w widoku), D2 B2 (Y, kropki), D3 B1 daleko od cięcia, D4 T1 (X) na rzucie górnym. Przekrój A: cięcie y = 2000,
// patrzenie +Y, wstawienie (8000, 3000) — AutoRebarEngine.PrepareSection → SectionPlanner.Plan → DrawSectionRecorded (jak RC_SECTION
// bez okna).
// SG1  utworzenie: RegApp, XData tytułu ([1]–[6]), Xrecord, części [1]/[4], klucze znaczników, CHECK = aktualny; geometria
//      z danymi = geometria bez danych (ta sama liczba, rodzaj, warstwa, zakres — przesunięcie o Δ wstawienia).
// SG3  daleki rozkład (D3) zmieniony → aktualny, bez chmurki.
// SG2  rozstaw D2 200 → 150 → (RC_SECTION_CHECK) → NIEAKTUALNY z przyczyną, chmurka na RC-SECTION-STATUS (kolor 1, niedrukowana).
// SG12 drugi RC_SECTION_CHECK bez zmian → zero zapisów (ObjectAppended / Modified / Erased).
// SG5  pal na linii cięcia → „nowy pal”; usunięty → bez przyczyny pala.
// SG4  ERASE obrysu → osierocony; _.UNDO 1 → znów NIEAKTUALNY (stan sprzed).
// SG2R rozstaw przywrócony → (RC_SECTION_CHECK) → aktualny, chmurka usunięta.
// SG8  COPY całego przekroju (bez znaczników) → drugi przekrój A: nowy GUID, własne części, aktualny, „litera A użyta”;
//      COPY samego opisu → zwykły tekst bez RC_SECTION_PART (P-b), CHECK bez „części bez tytułu”; COPY samego tytułu → zwykły tekst (nieznany).
// SG9  COPY rzutu płyty → przekrój A NIEAKTUALNY („rzuty płyty”), obrys [4] bez zmian; _.UNDO 1 → aktualny.
// SG10 drugi rysunek (baza w pamięci) — przekroje rozdzielne.
// SG11 stary przekrój (DrawSection bez rekordu) → nieznany; ReadAll bez starych — pominięty.
// SG14 RC_BBS w trybie skryptu: SectionGuard.ConfirmBeforeOutput → true + komunikat, chmurka; po przywróceniu — bez chmurki.

using System;
using System.Collections;
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
    public class PlanCSectionGuardTests
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const string ResultsFile = "planc-sg-results.txt";
        private const string Prefix = "[RC SG]";
        private const string Title = "SG — testy dymne bezpiecznika przekrojów RC_SECTION (etap 1)";
        private const string Slab = "SD-PILED-RAFT";

        private const string Engine = "BricsCadRc.Core.AutoRebarEngine";
        private const string Store = "BricsCadRc.Core.SectionStore";
        private const string Planner = "BricsCadRc.Core.SectionPlanner";
        private const string BarBlock = "BricsCadRc.Core.BarBlockEngine";
        private const string Guard = "BricsCadRc.Core.SectionGuard";

        private static readonly List<string> ExpectedSteps = new List<string>
        {
            "SETUP", "SG1", "SG3", "SG2", "SG2V", "SG12", "SG5", "SG4A", "SG4V", "SG4U", "SG2R", "SG2RV",
            "SG15", "SG16", "SG8P", "SG8V", "SG9V", "SG9U", "SG10", "SG11", "SG14",
        };

        private static readonly List<string> _results = new List<string>();
        private static readonly List<string> _executed = new List<string>();
        private static int _stepStart;

        // ── stan ────────────────────────────────────────────────────────
        private static ObjectId _slab, _slabTop, _d1, _d2, _d3, _d4;
        private static ObjectId _titleA, _lblA;
        private static string _guidA = "";
        private static int _partsA, _nonMarkerPartsA;
        private static readonly Point3d TopLeftA = new Point3d(8000, 3000, 0);

        // ================================================================
        // SETUP / REPORT
        // ================================================================

        [CommandMethod("RCSMOKE_SG_SETUP", CommandFlags.Modal)]
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
                Check(step, "rysunek testowy: 2 rzuty płyty, opis, 4 rozkłady RC_BAR_BLOCK", () =>
                {
                    int n = CleanModelSpace(db);
                    EnsureLayer(db, Slab);
                    EnsureLayer(db, "SD-Pile");
                    _slab = Rect(db, 0, 0, 6000, 4000);
                    _slabTop = Rect(db, 0, 10000, 6000, 14000);
                    Text(db, new Point3d(2500, 3600, 0), "PLOT 1\\PSSL=21.925\\P225mm THK SLAB");
                    _d1 = Dist(db, "H10-05-200 B1", "B1", "X", 40, 40, 5920, 3920, 20, 200, 10);
                    _d2 = Dist(db, "H10-06-200 B2", "B2", "Y", 40, 40, 3920, 5920, 30, 200, 10);
                    _d3 = Dist(db, "H10-07-200 B1", "B1", "X", 40, 3600, 5920, 200, 2, 200, 10);
                    _d4 = Dist(db, "H12-104-200 T1", "T1", "X", 170, 10040, 5660, 3920, 20, 200, 12);
                    return "usunięto " + n.ToString(Inv) + "; obrys " + _slab.Handle + ", rzut górny " + _slabTop.Handle;
                });
            });
        }

        [CommandMethod("RCSMOKE_SG_REPORT", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
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
        // SG1 — utworzenie
        // ================================================================

        [CommandMethod("RCSMOKE_SG_SG1", CommandFlags.Modal)]
        public void SG1()
        {
            const string step = "SG1";
            Run(step, db =>
            {
                List<ObjectId> recIds = null, legacyIds = null;
                Check(step, "przekrój A z danymi (PrepareSection → Plan → DrawSectionRecorded) i ten sam bez danych (DrawSection, rec = null)", () =>
                {
                    var before = MsIds(db);
                    var ctx1 = Ctx(db, 2000);
                    int n = (int)Call(Engine, "DrawSectionRecorded", db, ctx1, PlanOf(ctx1), TopLeftA, "A");
                    recIds = MsIds(db).Except(before).OrderBy(i => i.Handle.Value).ToList();
                    var before2 = MsIds(db);
                    var ctx2 = Ctx(db, 2000);
                    Call(Engine, "DrawSection", db, ctx2, PlanOf(ctx2), TopLeftA + new Vector3d(0, -40000, 0), "A", null);
                    legacyIds = MsIds(db).Except(before2).OrderBy(i => i.Handle.Value).ToList();
                    return Int("obiektów (z danymi)", recIds.Count, n) + "; bez danych " + legacyIds.Count.ToString(Inv);
                });
                Check(step, "geometria bit w bit: rodzaj, warstwa, kolor, zakres (Δ wstawienia; znaczniki SD-SECTION — bez przesunięcia)", () =>
                {
                    if (recIds.Count != legacyIds.Count) throw new AssertFail($"liczba obiektów {recIds.Count} ≠ {legacyIds.Count}");
                    var d = new Vector3d(0, -40000, 0);
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        for (int i = 0; i < recIds.Count; i++)
                        {
                            var a = (Entity)tr.GetObject(recIds[i], OpenMode.ForRead);
                            var b = (Entity)tr.GetObject(legacyIds[i], OpenMode.ForRead);
                            if (a.GetType() != b.GetType() || a.Layer != b.Layer || a.ColorIndex != b.ColorIndex)
                                throw new AssertFail($"#{i}: {a.GetType().Name}/{a.Layer}/{a.ColorIndex} ≠ {b.GetType().Name}/{b.Layer}/{b.ColorIndex}");
                            var shift = a.Layer == "SD-SECTION" ? new Vector3d() : d;
                            var ea = a.GeometricExtents; var eb = b.GeometricExtents;
                            if (ea.MinPoint.DistanceTo(eb.MinPoint - shift) > 1e-4 || ea.MaxPoint.DistanceTo(eb.MaxPoint - shift) > 1e-4)
                                throw new AssertFail($"#{i} {a.GetType().Name} {a.Layer}: zakres {ea.MinPoint}–{ea.MaxPoint} ≠ {eb.MinPoint - shift}–{eb.MaxPoint - shift}");
                            if (HasXData(b, "RC_SECTION_PART") || HasXData(b, "RC_SECTION")) throw new AssertFail($"#{i}: dane przekroju w rysunku bez rekordu");
                        }
                        tr.Commit();
                    }
                    EraseAll(db, legacyIds);
                    return "obiektów " + recIds.Count.ToString(Inv) + " — zgodne; kopia bez danych usunięta";
                });
                Check(step, "tytuł: RC_SECTION [1] format 2, [2] GUID, [3] A, [4] obrys, [5] obrys zewn., [6] = własny handle (1005)", () =>
                {
                    _titleA = recIds.Single(id => With(db, id, e => HasXData(e, "RC_SECTION")));
                    var v = XData(db, _titleA, "RC_SECTION");
                    if (v.Length != 7) throw new AssertFail("pól " + v.Length);
                    _guidA = (string)v[2].Value;
                    string h(int k) => v[k].Value is Handle hh ? hh.Value.ToString("X8") : "1000:" + v[k].Value;
                    return Eq("[1]", Convert.ToInt32(v[1].Value), 2) + "; " + Eq("[3]", v[3].Value, "A") + "; "
                         + Eq("[4]", h(4), _slab.Handle.Value.ToString("X8")) + "; " + Eq("[5]", h(5), _slab.Handle.Value.ToString("X8")) + "; "
                         + Eq("[6]", h(6), _titleA.Handle.Value.ToString("X8")) + "; GUID " + _guidA;
                });
                Check(step, "części: każda [1] → tytuł, [4] = GUID; lista w rekordzie = części; znaczniki V0/V1 P1/P2", () =>
                {
                    var parts = recIds.Where(id => id != _titleA).ToList();
                    foreach (var id in parts)
                    {
                        var v = XData(db, id, "RC_SECTION_PART");
                        if (v == null || v.Length < 5) throw new AssertFail("część bez RC_SECTION_PART: " + id.Handle);
                        if (!(v[1].Value is Handle th) || th.Value != _titleA.Handle.Value) throw new AssertFail("[1] ≠ tytuł: " + id.Handle);
                        if ((string)v[4].Value != _guidA) throw new AssertFail("[4] ≠ GUID: " + id.Handle);
                    }
                    var info = SectionA(db, false);
                    var rec = Prop(info, "Record");
                    int listed = ((IList)Prop(rec, "Parts")).Count;
                    var keys = Parts(info).Where(p => p.Role == "MARK").Select(p => p.Key).OrderBy(k => k).ToList();
                    _partsA = parts.Count;
                    _nonMarkerPartsA = Parts(info).Count(p => p.Role != "MARK" && p.Role != "MARKTXT");
                    return Int("części", parts.Count, listed) + "; " + Int("potwierdzone", Parts(info).Count, listed) + "; "
                         + Eq("MARK", string.Join(",", keys), "V0.P1,V0.P2,V1.P1,V1.P2") + "; poza znacznikami " + _nonMarkerPartsA;
                });
                Check(step, "RC_SECTION_CHECK (CheckAll): przekrój A aktualny, bez przyczyn; warstwa RC-SECTION-STATUS nie istnieje", () =>
                {
                    var info = SectionA(db, true);
                    return Eq("status", Status(info), "Aktualny") + "; " + Int("przyczyn", Reasons(info).Count, 0) + "; "
                         + Eq("warstwa statusu", LayerExists(db, "RC-SECTION-STATUS"), false);
                });
            });
        }

        // ================================================================
        // SG3 — daleki rozkład
        // ================================================================

        [CommandMethod("RCSMOKE_SG_SG3", CommandFlags.Modal)]
        public void SG3()
        {
            const string step = "SG3";
            Run(step, db =>
            {
                Check(step, "D3 (daleko od cięcia): rozstaw 200 → 100, liczba 2 → 3 → przekrój A aktualny, bez chmurki", () =>
                {
                    EditDist(db, _d3, "H10-07-100 B1", 100, 3);
                    var info = SectionA(db, true);
                    return Eq("status", Status(info), "Aktualny") + "; " + Int("chmurki", ((IList)Prop(info, "Clouds")).Count, 0);
                });
            });
        }

        // ================================================================
        // SG2 — rozstaw przeciętego rozkładu
        // ================================================================

        [CommandMethod("RCSMOKE_SG_SG2", CommandFlags.Modal)]
        public void SG2()
        {
            const string step = "SG2";
            Run(step, db =>
            {
                Check(step, "D2 (B2, kropki w przekroju): H10-06-200 → H10-06-150, liczba 30 → 40", () =>
                {
                    EditDist(db, _d2, "H10-06-150 B2", 150, 40);
                    return "zapisano";
                });
            });
        }

        [CommandMethod("RCSMOKE_SG_SG2V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void SG2V()
        {
            const string step = "SG2V";
            Run(step, db =>
            {
                Check(step, "po RC_SECTION_CHECK: A NIEAKTUALNY, przyczyna „zmieniony rozkład poz. 06 B2 (… rozstaw 200 → 150 …)”", () =>
                {
                    var info = SectionA(db, false);
                    Call(Store, "Evaluate", db, info);
                    var rs = Reasons(info);
                    if (!rs.Any(r => r.StartsWith("zmieniony rozkład poz. 06 B2", StringComparison.Ordinal) && r.Contains("rozstaw 200 → 150")))
                        throw new AssertFail("przyczyny: " + string.Join(" | ", rs));
                    return Eq("status", Status(info), "Nieaktualny") + "; " + string.Join(" | ", rs);
                });
                Check(step, "chmurka: 2 obiekty na RC-SECTION-STATUS (polilinia + „NIEAKTUALNY”), [1] → tytuł; warstwa kolor 1, Plottable = false", () =>
                {
                    var clouds = Ms(db, e => e.Layer == "RC-SECTION-STATUS");
                    foreach (var id in clouds)
                    {
                        var v = XData(db, id, "RC_SECTION_PART");
                        if (v == null || (string)v[2].Value != "CLOUD" || !(v[1].Value is Handle th) || th.Value != _titleA.Handle.Value)
                            throw new AssertFail("chmurka bez powiązania z tytułem: " + id.Handle);
                    }
                    var (color, plot) = LayerProps(db, "RC-SECTION-STATUS");
                    return Int("obiektów chmurki", clouds.Count, 2) + "; " + Int("kolor", color, 1) + "; " + Eq("IsPlottable", plot, false);
                });
            });
        }

        // ================================================================
        // SG12 — brak zapisu bez zmiany stanu
        // ================================================================

        [CommandMethod("RCSMOKE_SG_SG12", CommandFlags.Modal)]
        public void SG12()
        {
            const string step = "SG12";
            Run(step, db =>
            {
                Check(step, "RC_SECTION_CHECK ponownie (stan bez zmian) → zero zapisów do bazy", () =>
                {
                    int app = 0, mod = 0, er = 0;
                    ObjectEventHandler a = (s, e) => app++;
                    ObjectEventHandler m = (s, e) => mod++;
                    ObjectErasedEventHandler x = (s, e) => er++;
                    db.ObjectAppended += a; db.ObjectModified += m; db.ObjectErased += x;
                    try { Call("BricsCadRc.Commands.SectionCommands", "SectionCheck"); }
                    finally { db.ObjectAppended -= a; db.ObjectModified -= m; db.ObjectErased -= x; }
                    return Int("ObjectAppended", app, 0) + "; " + Int("ObjectModified", mod, 0) + "; " + Int("ObjectErased", er, 0);
                });
            });
        }

        // ================================================================
        // SG5 — pal
        // ================================================================

        [CommandMethod("RCSMOKE_SG_SG5", CommandFlags.Modal)]
        public void SG5()
        {
            const string step = "SG5";
            Run(step, db =>
            {
                ObjectId pile = ObjectId.Null;
                Check(step, "pal SD-Pile (3000, 2000) r 300 na linii cięcia → przyczyna „nowy pal na linii cięcia (3500, 0 od punktu P1 cięcia)”", () =>
                {
                    pile = Circle(db, 3000, 2000, 300);
                    var info = SectionA(db, true);
                    var rs = Reasons(info);
                    if (!rs.Any(r => r.StartsWith("nowy pal na linii cięcia (3500, 0 od punktu P1 cięcia)", StringComparison.Ordinal)))
                        throw new AssertFail("przyczyny: " + string.Join(" | ", rs));
                    return Eq("status", Status(info), "Nieaktualny");
                });
                Check(step, "pal usunięty → bez przyczyny pala", () =>
                {
                    EraseAll(db, new List<ObjectId> { pile });
                    var rs = Reasons(SectionA(db, true));
                    if (rs.Any(r => r.Contains("pal"))) throw new AssertFail("przyczyny: " + string.Join(" | ", rs));
                    return "przyczyn " + rs.Count.ToString(Inv);
                });
            });
        }

        // ================================================================
        // SG4 — ERASE obrysu + UNDO
        // ================================================================

        [CommandMethod("RCSMOKE_SG_SG4A", CommandFlags.Modal)]
        public void SG4A()
        {
            const string step = "SG4A";
            Run(step, db => Check(step, "ERASE obrysu płyty (rzut dolny)", () => { EraseAll(db, new List<ObjectId> { _slab }); return "usunięto " + _slab.Handle; }));
        }

        [CommandMethod("RCSMOKE_SG_SG4V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void SG4V()
        {
            const string step = "SG4V";
            Run(step, db => Check(step, "przekrój A osierocony (obrys usunięty)", () =>
            {
                var info = SectionA(db, true);
                return Eq("status", Status(info), "Osierocony") + "; " + string.Join(" | ", Reasons(info));
            }));
        }

        [CommandMethod("RCSMOKE_SG_SG4U", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void SG4U()
        {
            const string step = "SG4U";
            Run(step, db => Check(step, "po _.UNDO 1: obrys wrócił, A znów NIEAKTUALNY (rozstaw D2)", () =>
            {
                var info = SectionA(db, true);
                return Eq("obrys usunięty", _slab.IsErased, false) + "; " + Eq("status", Status(info), "Nieaktualny");
            }));
        }

        // ================================================================
        // SG2R — przywrócenie
        // ================================================================

        [CommandMethod("RCSMOKE_SG_SG2R", CommandFlags.Modal)]
        public void SG2R()
        {
            const string step = "SG2R";
            Run(step, db => Check(step, "D2: przywrócony H10-06-200 B2, liczba 30", () => { EditDist(db, _d2, "H10-06-200 B2", 200, 30); return "zapisano"; }));
        }

        [CommandMethod("RCSMOKE_SG_SG2RV", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void SG2RV()
        {
            const string step = "SG2RV";
            Run(step, db => Check(step, "po RC_SECTION_CHECK: A aktualny, chmurka usunięta", () =>
            {
                var info = SectionA(db, true);
                return Eq("status", Status(info), "Aktualny") + "; " + Int("obiektów chmurki", Ms(db, e => e.Layer == "RC-SECTION-STATUS").Count, 0);
            }));
        }

        // ================================================================
        // SG15 — STRETCH krawędzi płyty daleko od cięcia (odniesienie = P1 cięcia)
        // ================================================================

        [CommandMethod("RCSMOKE_SG_SG15", CommandFlags.Modal)]
        public void SG15()
        {
            const string step = "SG15";
            Run(step, db =>
            {
                Check(step, "STRETCH krawędzi równoległych do cięcia (dolna y 0 → −300 — zmiana MinPoint, górna 4000 → 4500) na obu rzutach → A aktualny", () =>
                {
                    MoveEdgeY(db, _slab, 0, -300); MoveEdgeY(db, _slab, 4000, 4500);
                    MoveEdgeY(db, _slabTop, 10000, 9700); MoveEdgeY(db, _slabTop, 14000, 14500);
                    var info = SectionA(db, true);
                    string r = Eq("status", Status(info), "Aktualny") + "; przyczyny: " + string.Join(" | ", Reasons(info));
                    MoveEdgeY(db, _slab, -300, 0); MoveEdgeY(db, _slab, 4500, 4000);
                    MoveEdgeY(db, _slabTop, 9700, 10000); MoveEdgeY(db, _slabTop, 14500, 14000);
                    return r + "; " + Eq("po przywróceniu", Status(SectionA(db, true)), "Aktualny");
                });
            });
        }

        /// <summary>Wierzchołki obrysu o Y = from → Y = to (STRETCH krawędzi poziomej).</summary>
        private static void MoveEdgeY(Database db, ObjectId id, double from, double to)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pl = (Polyline)tr.GetObject(id, OpenMode.ForWrite);
                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    var p = pl.GetPoint2dAt(i);
                    if (Math.Abs(p.Y - from) < 1e-6) pl.SetPointAt(i, new Point2d(p.X, to));
                }
                tr.Commit();
            }
        }

        // ================================================================
        // SG16 — tytuł z tym samym GUID (wklejenie / wstawienie z przetłumaczonym [6])
        // ================================================================

        [CommandMethod("RCSMOKE_SG_SG16", CommandFlags.Modal)]
        public void SG16()
        {
            const string step = "SG16";
            Run(step, db =>
            {
                Check(step, "drugi tytuł z GUID przekroju A i poprawnym [6] → Kopia („kopia przekroju A-A (ten sam identyfikator …)”), A aktualny", () =>
                {
                    ObjectId dupId;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var orig = (MText)tr.GetObject(_titleA, OpenMode.ForRead);
                        var rec = Call(Engine, "ReadRecord", tr, orig);
                        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                        var dup = new MText();
                        dup.SetDatabaseDefaults(db);
                        dup.Layer = orig.Layer;
                        dup.Contents = orig.Contents;
                        dup.Location = orig.Location + new Vector3d(0, -50000, 0);
                        dupId = ms.AppendEntity(dup);
                        tr.AddNewlyCreatedDBObject(dup, true);
                        string sh = _slab.Handle.Value.ToString("X8");
                        dup.XData = (ResultBuffer)Call(Engine, "TitleXData", _guidA, "A", sh, sh, dupId.Handle.Value.ToString("X8"));
                        Call(Engine, "WriteRecord", tr, dup, rec);
                        tr.Commit();
                    }
                    var all = CheckAll(db, false);
                    var d = all.Single(i => (ObjectId)Prop(i, "TitleId") == dupId);
                    var a = all.Single(i => (ObjectId)Prop(i, "TitleId") == _titleA);
                    string r = Eq("status kopii", Status(d), "Kopia") + "; " + Eq("status A", Status(a), "Aktualny") + "; "
                             + Eq("przyczyna", Reasons(d).Any(x => x.StartsWith("kopia przekroju A-A (ten sam identyfikator", StringComparison.Ordinal)), true)
                             + "; " + string.Join(" | ", Reasons(d));
                    EraseAll(db, new List<ObjectId> { dupId });
                    return r;
                });
            });
        }

        // ================================================================
        // SG8 — COPY przekroju / samego opisu / samego tytułu
        // ================================================================

        [CommandMethod("RCSMOKE_SG_SG8P", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void SG8P()
        {
            const string step = "SG8P";
            Run(step, db =>
            {
                Check(step, "okno przekroju A (tytuł + części poza znacznikami) → USERR1–4; opis (LBL) → USERS1; tytuł → USERS2", () =>
                {
                    var info = SectionA(db, false);
                    Extents3d? ext = null;
                    string lbl = null;
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        void AddExt(ObjectId id)
                        {
                            var b = ((Entity)tr.GetObject(id, OpenMode.ForRead)).GeometricExtents;
                            if (ext.HasValue) { var x = ext.Value; x.AddExtents(b); ext = x; } else ext = b;
                        }
                        AddExt(_titleA);
                        foreach (var p in Parts(info))
                        {
                            if (p.Role == "MARK" || p.Role == "MARKTXT") continue;
                            AddExt(p.Id);
                            if (lbl == null && p.Role == "LBL") { lbl = p.Id.Handle.ToString(); _lblA = p.Id; }
                        }
                        tr.Commit();
                    }
                    var e = ext.Value;
                    if (e.MinPoint.X < 6500) throw new AssertFail("okno przekroju zachodzi na rzut płyty: " + e.MinPoint);
                    Application.SetSystemVariable("USERR1", e.MinPoint.X - 50);
                    Application.SetSystemVariable("USERR2", e.MinPoint.Y - 50);
                    Application.SetSystemVariable("USERR3", e.MaxPoint.X + 50);
                    Application.SetSystemVariable("USERR4", e.MaxPoint.Y + 50);
                    Application.SetSystemVariable("USERS1", lbl);
                    Application.SetSystemVariable("USERS2", _titleA.Handle.ToString());
                    return $"okno {e.MinPoint} – {e.MaxPoint}; opis {lbl}; tytuł {_titleA.Handle}";
                });
            });
        }

        [CommandMethod("RCSMOKE_SG_SG8V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void SG8V()
        {
            const string step = "SG8V";
            Run(step, db =>
            {
                var all = CheckAll(db, true);
                Check(step, "dwa przekroje A z danymi: kopia z nowym GUID, [6] = własny handle, części = części poza znacznikami, oba aktualne", () =>
                {
                    var titled = all.Where(i => (string)Prop(i, "Letter") == "A" && Status(i) != "Nieznany" && !((ObjectId)Prop(i, "TitleId")).IsNull).ToList();
                    if (titled.Count != 2) throw new AssertFail("przekrojów A z danymi: " + titled.Count + " — " + Describe(all));
                    var orig = titled.Single(i => (ObjectId)Prop(i, "TitleId") == _titleA);
                    var copy = titled.Single(i => (ObjectId)Prop(i, "TitleId") != _titleA);
                    var cid = (ObjectId)Prop(copy, "TitleId");
                    var v = XData(db, cid, "RC_SECTION");
                    string self = v[6].Value is Handle h ? h.Value.ToString("X8") : "?";
                    return Eq("GUID oryginału", Prop(orig, "Guid"), _guidA) + "; " + Eq("GUID kopii ≠", (string)Prop(copy, "Guid") != _guidA, true) + "; "
                         + Eq("[6] kopii", self, cid.Handle.Value.ToString("X8")) + "; "
                         + Int("części kopii", Parts(copy).Count, _nonMarkerPartsA) + "; " + Int("części oryginału", Parts(orig).Count, _partsA) + "; "
                         + Eq("status oryginału", Status(orig), "Aktualny") + "; " + Eq("status kopii", Status(copy), "Aktualny") + "; "
                         + Eq("ostrzeżenie litery", Warnings(orig).Any(w => w.StartsWith("litera A użyta", StringComparison.Ordinal)), true) + "; "
                         + Eq("ostrzeżenie kopii", Warnings(copy).Any(w => w.StartsWith("kopia przekroju A (bez znaczników na rzucie)", StringComparison.Ordinal)), true);
                });
                Check(step, "COPY samego opisu (P-b) → kopia bez RC_SECTION_PART (zwykły tekst), CHECK bez „części bez tytułu”", () =>
                {
                    var (txt, pos) = With(db, _lblA, e => (((DBText)e).TextString, ((DBText)e).Position));
                    var target = pos + new Vector3d(600, 0, 0);
                    var copies = Ms(db, e => e is DBText t && t.TextString == txt && t.Position.DistanceTo(target) < 1e-3);
                    if (copies.Count != 1) throw new AssertFail("kopii opisu w (+600, 0): " + copies.Count);
                    int orphans = all.Count(i => Status(i) == "CzesciBezTytulu");
                    return Eq("RC_SECTION_PART na kopii", XData(db, copies[0], "RC_SECTION_PART") != null, false) + "; "
                         + Int("grup części bez tytułu", orphans, 0) + "; " + Eq("oryginał ma RC_SECTION_PART", XData(db, _lblA, "RC_SECTION_PART") != null, true);
                });
                Check(step, "COPY samego tytułu → zwykły tekst bez RC_SECTION i Xrecordu (CHECK: nieznany)", () =>
                {
                    var unknown = all.Where(i => Status(i) == "Nieznany" && (string)Prop(i, "Letter") == "A").ToList();
                    if (unknown.Count != 1) throw new AssertFail("nieznanych A: " + unknown.Count + " — " + Describe(all));
                    var id = (ObjectId)Prop(unknown[0], "TitleId");
                    bool hasRec = With(db, id, e => !e.ExtensionDictionary.IsNull && HasRecord(db, e));
                    return Eq("RC_SECTION", XData(db, id, "RC_SECTION") != null, false) + "; " + Eq("Xrecord", hasRec, false);
                });
            });
        }

        // ================================================================
        // SG9 — COPY płyty (rzutu)
        // ================================================================

        [CommandMethod("RCSMOKE_SG_SG9V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void SG9V()
        {
            const string step = "SG9V";
            Run(step, db => Check(step, "COPY rzutu dolnego (obrys + rozkłady) → A NIEAKTUALNY („nowy rzut płyty (przesunięcie 0, -20000) …”), [4] tytułu bez zmian", () =>
            {
                var info = SectionA(db, true);
                var rs = Reasons(info);
                if (!rs.Any(r => r.StartsWith("nowy rzut płyty (przesunięcie 0, -20000)", StringComparison.Ordinal))) throw new AssertFail("przyczyny: " + string.Join(" | ", rs));
                var v = XData(db, _titleA, "RC_SECTION");
                string picked = v[4].Value is Handle h ? h.Value.ToString("X8") : "?";
                return Eq("status", Status(info), "Nieaktualny") + "; " + Eq("[4]", picked, _slab.Handle.Value.ToString("X8"));
            }));
        }

        [CommandMethod("RCSMOKE_SG_SG9U", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void SG9U()
        {
            const string step = "SG9U";
            Run(step, db => Check(step, "po _.UNDO 1 (cofnięcie COPY rzutu): A aktualny", () => Eq("status", Status(SectionA(db, true)), "Aktualny")));
        }

        // ================================================================
        // SG10 — dwa rysunki
        // ================================================================

        [CommandMethod("RCSMOKE_SG_SG10", CommandFlags.Modal)]
        public void SG10()
        {
            const string step = "SG10";
            Run(step, db =>
            {
                Check(step, "drugi rysunek (baza w pamięci): przekrój B — rozdzielne listy, oba aktualne", () =>
                {
                    int before = CheckAll(db, false).Count;
                    using (var side = new Database(true, true))
                    {
                        var prev = HostApplicationServices.WorkingDatabase;
                        try
                        {
                            HostApplicationServices.WorkingDatabase = side;
                            EnsureLayer(side, Slab);
                            var slab = Rect(side, 0, 0, 5000, 3000);
                            Dist(side, "H10-21-200 B1", "B1", "Y", 40, 40, 2920, 4920, 25, 200, 10);
                            var ctx = Call(Engine, "PrepareSection", side, slab, new Point2d(-100, 1500), new Point2d(5100, 1500), new Point2d(0, 1600));
                            Call(Engine, "DrawSectionRecorded", side, ctx, PlanOf(ctx), new Point3d(7000, 2000, 0), "B");
                            var sideList = CheckAll(side, true);
                            int after = CheckAll(db, false).Count;
                            return Int("przekrojów w drugim", sideList.Count, 1) + "; " + Eq("status B", Status(sideList[0]), "Aktualny") + "; "
                                 + Int("przekrojów w aktywnym (bez zmian)", after, before);
                        }
                        finally { HostApplicationServices.WorkingDatabase = prev; }
                    }
                });
            });
        }

        // ================================================================
        // SG11 — stary przekrój
        // ================================================================

        [CommandMethod("RCSMOKE_SG_SG11", CommandFlags.Modal)]
        public void SG11()
        {
            const string step = "SG11";
            Run(step, db =>
            {
                Check(step, "przekrój Z bez danych (DrawSection, rec = null) → CHECK: nieznany; ReadAll bez starych — pominięty", () =>
                {
                    var ctx = Ctx(db, 1000);
                    Call(Engine, "DrawSection", db, ctx, PlanOf(ctx), new Point3d(8000, -30000, 0), "Z", null);
                    var withLegacy = CheckAll(db, true).Where(i => (string)Prop(i, "Letter") == "Z").ToList();
                    var fast = ((IEnumerable)Call(Store, "ReadAll", db, false)).Cast<object>().Where(i => (string)Prop(i, "Letter") == "Z").ToList();
                    return Int("Z (ze starymi)", withLegacy.Count, 1) + "; " + Eq("status", Status(withLegacy[0]), "Nieznany") + "; "
                         + Int("Z (bez starych)", fast.Count, 0);
                });
            });
        }

        // ================================================================
        // SG14 — RC_BBS w trybie skryptu
        // ================================================================

        [CommandMethod("RCSMOKE_SG_SG14", CommandFlags.Modal)]
        public void SG14()
        {
            const string step = "SG14";
            Run(step, db =>
            {
                Check(step, "A nieaktualny (D2 150) → SectionGuard.ConfirmBeforeOutput(„BBS”) = true (tryb skryptu) + komunikat, chmurka", () =>
                {
                    EditDist(db, _d2, "H10-06-150 B2", 150, 40);
                    bool go = (bool)Call(Guard, "ConfirmBeforeOutput", Application.DocumentManager.MdiActiveDocument, "BBS");
                    var msgs = ((IEnumerable)Plugin.GetType(Guard, true).GetProperty("LastMessages").GetValue(null)).Cast<string>().ToList();
                    if (!msgs.Any(m => m.StartsWith("Przekroje nieaktualne", StringComparison.Ordinal))) throw new AssertFail("komunikaty: " + string.Join(" | ", msgs));
                    if (!msgs.Any(m => m.Contains("tryb bez okien"))) throw new AssertFail("komunikaty: " + string.Join(" | ", msgs));
                    return Eq("wynik", go, true) + "; " + Int("obiektów chmurki (A i kopia A z SG8 — po 2)", Ms(db, e => e.Layer == "RC-SECTION-STATUS").Count, 4)
                         + "; " + string.Join(" | ", msgs);
                });
                Check(step, "D2 przywrócony → ConfirmBeforeOutput = true bez komunikatu, chmurka usunięta", () =>
                {
                    EditDist(db, _d2, "H10-06-200 B2", 200, 30);
                    bool go = (bool)Call(Guard, "ConfirmBeforeOutput", Application.DocumentManager.MdiActiveDocument, "BBS");
                    var msgs = ((IEnumerable)Plugin.GetType(Guard, true).GetProperty("LastMessages").GetValue(null)).Cast<string>().ToList();
                    return Eq("wynik", go, true) + "; " + Int("komunikatów", msgs.Count, 0) + "; "
                         + Int("obiektów chmurki", Ms(db, e => e.Layer == "RC-SECTION-STATUS").Count, 0);
                });
            });
        }

        // ================================================================
        // PERF — pomiar czasu na prawdziwym rysunku (np. kopia Holbrook PLOT 321); osobny przebieg, wynik planc-sg-perf.txt
        // ================================================================

        [CommandMethod("RCSMOKE_SG_PERF", CommandFlags.Modal)]
        public void Perf()
        {
            var lines = new List<string> { "PERF bezpiecznik przekrojów " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv) };
            void L(string s) { lines.Add(s); File.WriteAllLines(Path.Combine(OutDir, "planc-sg-perf.txt"), lines, new UTF8Encoding(false)); }
            try
            {
                var db = ActiveDb();
                var doc = Application.DocumentManager.MdiActiveDocument;
                L("rysunek: " + doc.Name + ", obiektów w modelu: " + MsIds(db).Count.ToString(Inv));
                // płyta = zamknięta polilinia SD-PILED-RAFT z największą liczbą rozkładów w środku
                var dists = new List<Point2d>();
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                        if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is BlockReference br && HasXData(br, "RC_BAR_BLOCK"))
                            dists.Add(new Point2d(br.Position.X, br.Position.Y));
                    tr.Commit();
                }
                var slabs = Ms(db, e => e is Polyline p && p.Closed && e.Layer == Slab);
                ObjectId best = ObjectId.Null; int bestN = -1; Extents3d bestExt = default;
                foreach (var id in slabs)
                {
                    var ext = With(db, id, e => e.GeometricExtents);
                    int n = dists.Count(p => p.X > ext.MinPoint.X && p.X < ext.MaxPoint.X && p.Y > ext.MinPoint.Y && p.Y < ext.MaxPoint.Y);
                    if (n > bestN) { bestN = n; best = id; bestExt = ext; }
                }
                L($"rozkładów: {dists.Count}, obrysów {Slab}: {slabs.Count}, płyta {best.Handle} (rozkładów w zakresie {bestN})");
                double cx = (bestExt.MinPoint.X + bestExt.MaxPoint.X) / 2, cy = (bestExt.MinPoint.Y + bestExt.MaxPoint.Y) / 2;
                double x0 = bestExt.MinPoint.X - 300, x1 = bestExt.MaxPoint.X + 300, y0 = bestExt.MinPoint.Y - 300, y1 = bestExt.MaxPoint.Y + 300;
                var right = db.Extmax.X + 5000;
                var sw = new System.Diagnostics.Stopwatch();
                foreach (var (name, p1, p2, look, at) in new[]
                {
                    ("A (poziomy)", new Point2d(x0, cy), new Point2d(x1, cy), new Point2d(cx, cy + 100), new Point3d(right, db.Extmax.Y, 0)),
                    ("B (pionowy)", new Point2d(cx, y0), new Point2d(cx, y1), new Point2d(cx + 100, cy), new Point3d(right, db.Extmax.Y - 6000, 0)),
                })
                {
                    sw.Restart();
                    var ctx = Call(Engine, "PrepareSection", db, best, p1, p2, look);
                    var plan = PlanOf(ctx);
                    long tPrep = sw.ElapsedMilliseconds;
                    int n = (int)Call(Engine, "DrawSectionRecorded", db, ctx, plan, at, name.Substring(0, 1));
                    long tDraw = sw.ElapsedMilliseconds;
                    var infos = Call(Store, "CheckAll", db, false);
                    Call(Store, "SyncClouds", db, infos);
                    sw.Stop();
                    L($"RC_SECTION {name}: PrepareSection+Plan {tPrep} ms, rysunek {tDraw - tPrep} ms ({n} obiektów), CheckAll+SyncClouds {sw.ElapsedMilliseconds - tDraw} ms, razem {sw.ElapsedMilliseconds} ms");
                }
                for (int i = 0; i < 3; i++)
                {
                    sw.Restart();
                    var list = CheckAll(db, true);
                    sw.Stop();
                    L($"CheckAll (ze starymi) #{i + 1}: {sw.ElapsedMilliseconds} ms — " + Describe(list));
                }
                sw.Restart();
                bool go = (bool)Call(Guard, "ConfirmBeforeOutput", doc, "BBS");
                sw.Stop();
                L($"SectionGuard.ConfirmBeforeOutput: {sw.ElapsedMilliseconds} ms (wynik {go})");
                L("END");
            }
            catch (System.Exception ex) { L("BŁĄD: " + ex); L("END"); }
        }

        // ================================================================
        // Pomocnicze — przekrój
        // ================================================================

        private static object Ctx(Database db, double cutY)
            => Call(Engine, "PrepareSection", db, _slab, new Point2d(-500, cutY), new Point2d(6500, cutY), new Point2d(0, cutY + 100));

        private static object PlanOf(object ctx) => Call(Planner, "Plan", Prop(ctx, "Input"));

        private static List<object> CheckAll(Database db, bool legacy)
            => ((IEnumerable)Call(Store, "CheckAll", db, legacy)).Cast<object>().ToList();

        /// <summary>Przekrój A (tytuł _titleA): evaluate = CheckAll, inaczej ReadAll (bez przeliczenia).</summary>
        private static object SectionA(Database db, bool evaluate)
        {
            var list = evaluate ? CheckAll(db, false) : ((IEnumerable)Call(Store, "ReadAll", db, false)).Cast<object>().ToList();
            var a = list.FirstOrDefault(i => (ObjectId)Prop(i, "TitleId") == _titleA);
            if (a == null) throw new AssertFail("brak przekroju A w " + Describe(list));
            return a;
        }

        private static string Status(object info) => Prop(info, "Status").ToString();
        private static List<string> Reasons(object info) => ((IEnumerable)Prop(info, "Reasons")).Cast<string>().ToList();
        private static List<string> Warnings(object info) => ((IEnumerable)Prop(info, "Warnings")).Cast<string>().ToList();

        private static List<(ObjectId Id, string Role, string Key)> Parts(object info)
            => ((IEnumerable)Prop(info, "Parts")).Cast<object>()
               .Select(t => ((ObjectId)t.GetType().GetField("Item1").GetValue(t), (string)t.GetType().GetField("Item2").GetValue(t),
                             (string)t.GetType().GetField("Item3").GetValue(t))).ToList();

        private static string Describe(IEnumerable<object> list)
            => string.Join("; ", list.Select(i => $"{Prop(i, "Label")}={Status(i)}[{string.Join(" | ", Reasons(i))}]"));

        private static void EditDist(Database db, ObjectId id, string mark, double spacing, int count)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = (BlockReference)tr.GetObject(id, OpenMode.ForWrite);
                var xd = Call(BarBlock, "ReadXData", br);
                SetProp(xd, "Mark", mark);
                SetProp(xd, "Spacing", spacing);
                SetProp(xd, "Count", count);
                Call(BarBlock, "WriteXData", br, xd);
                tr.Commit();
            }
        }

        // ================================================================
        // Pomocnicze — rysunek
        // ================================================================

        private static ObjectId Rect(Database db, double x0, double y0, double x1, double y1)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var pl = new Polyline();
                pl.SetDatabaseDefaults(db);
                pl.Layer = Slab;
                pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
                pl.Closed = true;
                var id = ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                tr.Commit();
                return id;
            }
        }

        private static ObjectId Circle(Database db, double x, double y, double r)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var c = new Teigha.DatabaseServices.Circle(new Point3d(x, y, 0), Vector3d.ZAxis, r);
                c.SetDatabaseDefaults(db);
                c.Layer = "SD-Pile";
                var id = ms.AppendEntity(c);
                tr.AddNewlyCreatedDBObject(c, true);
                tr.Commit();
                return id;
            }
        }

        private static void Text(Database db, Point3d at, string contents)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var mt = new MText();
                mt.SetDatabaseDefaults(db);
                mt.Contents = contents;
                mt.Location = at;
                mt.TextHeight = 150;
                ms.AppendEntity(mt);
                tr.AddNewlyCreatedDBObject(mt, true);
                tr.Commit();
            }
        }

        /// <summary>Rozkład: BlockReference bloku „SG_DIST” (linia 100 mm) z XData RC_BAR_BLOCK (BarBlockEngine.WriteXData).</summary>
        private static ObjectId Dist(Database db, string mark, string layer, string dir, double x0, double y0, double lenA, double span,
                                     int count, double spacing, int dia)
        {
            Call(BarBlock, "EnsureAppIdRegistered", db);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                ObjectId btrId;
                if (bt.Has("SG_DIST")) btrId = bt["SG_DIST"];
                else
                {
                    bt.UpgradeOpen();
                    var btr = new BlockTableRecord { Name = "SG_DIST" };
                    btrId = bt.Add(btr);
                    tr.AddNewlyCreatedDBObject(btr, true);
                    var ln = new Line(new Point3d(0, 0, 0), new Point3d(100, 0, 0));
                    ln.SetDatabaseDefaults(db);
                    btr.AppendEntity(ln);
                    tr.AddNewlyCreatedDBObject(ln, true);
                }
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var br = new BlockReference(new Point3d(x0, y0, 0), btrId);
                br.SetDatabaseDefaults(db);
                var id = ms.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                var xd = Activator.CreateInstance(Plugin.GetType("BricsCadRc.Core.BarData", true));
                SetProp(xd, "Mark", mark);
                SetProp(xd, "LayerCode", layer);
                SetProp(xd, "Direction", dir);
                SetProp(xd, "LengthA", lenA);
                SetProp(xd, "BarsSpan", span);
                SetProp(xd, "Count", count);
                SetProp(xd, "Spacing", spacing);
                SetProp(xd, "Diameter", dia);
                Call(BarBlock, "WriteXData", br, xd);
                tr.Commit();
                return id;
            }
        }

        private static List<ObjectId> MsIds(Database db) => Ms(db, e => true);

        private static void EraseAll(Database db, List<ObjectId> ids)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in ids) if (!id.IsErased) tr.GetObject(id, OpenMode.ForWrite).Erase();
                tr.Commit();
            }
        }

        private static TypedValue[] XData(Database db, ObjectId id, string app)
            => With(db, id, e => { using (var rb = e.GetXDataForApplication(app)) return rb?.AsArray(); });

        private static bool HasXData(Entity e, string app)
        {
            using (var rb = e.GetXDataForApplication(app)) return rb != null;
        }

        private static bool HasRecord(Database db, Entity e)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var d = (DBDictionary)tr.GetObject(e.ExtensionDictionary, OpenMode.ForRead);
                bool has = d.Contains("NA_RC_SECTION");
                tr.Commit();
                return has;
            }
        }

        private static bool LayerExists(Database db, string name)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                bool has = lt.Has(name);
                tr.Commit();
                return has;
            }
        }

        private static (int Color, bool Plottable) LayerProps(Database db, string name)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                var rec = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForRead);
                var r = (rec.Color.ColorIndex, rec.IsPlottable);
                tr.Commit();
                return r;
            }
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
        // Infrastruktura: refleksja, wyniki (jak PlanCNibTests)
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

        private static void SetProp(object o, string name, object value)
        {
            var pi = o.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi != null) { pi.SetValue(o, value); return; }
            var fi = o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null) { fi.SetValue(o, value); return; }
            throw new InvalidOperationException(o.GetType().FullName + ": brak pola/właściwości " + name);
        }

        private static string OutDir => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        private static Database ActiveDb() => Application.DocumentManager.MdiActiveDocument.Database;

        private static void Msg(string text)
        {
            try { Application.DocumentManager.MdiActiveDocument?.Editor.WriteMessage(text); }
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("PlanCSectionGuardTests.Msg: " + ex); }
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
