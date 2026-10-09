// PLAN C ETAP 3a — testy dymne pręta-bloku dla wszystkich kształtów (SingleBarBlock) w prawdziwym BricsCAD V25.
// Uruchamiane skryptem planc-e3.scr (run-planc-e3.ps1). S1 z docs/PLAN_C_etap3.md (krok 3a); S2 (testy etapu 1
// nadal PASS) uruchamia run-planc-e3.ps1 przez run-planc-e1.ps1.
//
// S1: PlaceBar w formacie Block vs Polyline (BarFormat ustawiany refleksją) dla 00/11/21/13/44/99 w TYM SAMYM
// punkcie wstawienia: pierścienie obrysu w WCS ±1e-6 (dla 44 oba), BarEntity.OutlineVertices / FirstOutlineVertex /
// MidPoint / ClosestPoint, XData [0]–[13] równe, [14] = Int16 2, rekord 15, BTR *U bez XData, Explodable = false,
// refs = 1, zawartość warstwa 0 / ByBlock, efektywne właściwości równe przy niestandardowych
// CLAYER / CECOLOR / CELTYPE / CELTSCALE / CELWEIGHT.
//
// PLAN C ETAP 3b/3c (skany widzą bloki, tworzenie w trybie Block):
// S3  ScanTemplates widzi blok H10-04 w strefie rebar_bottom — ten sam wynik co polilinia; H10-T pomijany.
// S4  IsExistingPosNrUB / ScanUBTemplates dla bloku 21 (H12-01) = polilinia.
// S5  DeleteSummaryBars usuwa blok 501 + etykietę + rozkład + opis (S5 → S5D), _.U przywraca (S5U).
// S6  pełny AutoRebar w trybie Block — wszystkie szablony to bloki; ponowne uruchomienie bez nowych szablonów i numerów.
// S7  przebicie (PlaceSummaryBars) 2× w trybie Block = jeden komplet 501/502.
// S8  grot etykiety (GetBarArrowTip) blok vs polilinia ±1e-6.
// P4  0-POPRAWKI p.4: AutoRebar z prętem bez numeru (H10-T / H12-T) w strefach = wynik jak bez niego (oba formaty).
//
// PLAN C ETAP 3d/3e/3e2 (edycja, COPY / MIRROR, SCALE):
// Z0  BarEditApply na pręcie-polilinii, długość po CommandEnded = 3500 (poprawka #1, W1b; wzorzec: podwójny przyrost 4000).
// S13 po _.COPY pręt + etykieta + rozkład + opis: kopia własny BTR *U (refs = 1), back-link etykiety; od W2 (mapa
//     klonowania, poprawka #3) [18] rozkładu kopii → kopia pręta, etykiety n / n (wzorzec: → oryginał).
// S13P to samo COPY dla pręta-polilinii — od W2 asercja (wcześniej obserwacja zachowania wzorca).
// S9  BarEditApply na kopii (A 3000 → 3500): oryginał bez zmian, kopia nowy numer, rozkład kopii zaktualizowany;
//     copy-on-write przy wspólnym BTR (klon referencji bez COPY).
// S12 _.U po S9 — stan sprzed Apply.  S10 zmiana kształtu 00 → 21 (axis[0] w miejscu, grot na obrysie).
// S11 RC_UPDATE_BAR (UpdateBarBlock) po skali 2 i (−2, 2): skala ±1 ze znakiem, obrys = dane.
// S9L BarEditApply na bloku-kopii z [13] → etykieta oryginału: etykieta oryginału nietknięta.
// S16 COPY bloku bez etykiety + polilinii z etykietą (grot < 50 mm od bloku), obie kolejności → etykieta przy polilinii
//     (od W2 z mapy klonowania — para dwustronna, bez heurystyki odległości).
// S17 dwa pokrywające się pręty-bloki, COPY obu prętów + tylko etykiety B → kopia B z etykietą B, kopia A bez etykiety
//     (od W2 z mapy klonowania).
// (Przypadek ARRAY / COPY wielokrotne pominięty: w skrypcie BricsCAD kończy COPY po pierwszym przesunięciu mimo COPYMODE = 0.)
// S14 _.COPY samego pręta: kopia bez etykiety ([13] puste), etykieta oryginału nietknięta.
// S15 _.MIRROR pręta 21 z etykietą: odbity obrys, |skala| = 1, własny BTR, back-link.
// SC  3e2: _.SCALE 2 → od razu skala 1 i obrys z danych (bez RC_UPDATE_BAR); SCU po _.UNDO 1, SCR po _.REDO.
//
// Dostęp do wtyczki WYŁĄCZNIE przez refleksję (jak w etapach 1–2).

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
    public class PlanCStage3Tests
    {
        // ----------------------------------------------------------------
        // Stałe testu
        // ----------------------------------------------------------------

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private const double GeoTol      = 1e-6;
        private const string ResultsFile = "planc-e3-results.txt";
        private const string Prefix      = "[RC E3]";
        private const string XApp        = "RC_SINGLE_BAR";
        private const string XLinkApp    = "RC_BAR_LINK";
        private const int    HeadLen     = 14;
        private const int    V2Len       = 15;
        // W4d (runda 2): pręt z PlaceBar ma znacznik geometrii W4d [15] = 2021 (polilinia: + [14] = 1)
        private const int    PlacedPolyLen  = 16;
        private const int    PlacedBlockLen = 16;
        private const short  TcInt16     = 1070;
        private const string BarLayer    = "RC-SLAB-LEADERS";

        private const string TestLayer    = "RCSMOKE_E3_LAYER";
        private const string TestLinetype = "RCSMOKE_E3_DASH";
        private const double TestLtScale  = 2.5;

        private const string SingleBarType  = "BricsCadRc.Core.SingleBarEngine";
        private const string BarEntityType  = "BricsCadRc.Core.BarEntity";
        private const string BarDataType    = "BricsCadRc.Core.BarData";
        private const string BarKindsType   = "BricsCadRc.Core.BarKinds";
        private const string BarFormatType  = "BricsCadRc.Core.BarFormat";
        private const string BarFormatKind  = "BricsCadRc.Core.BarFormatKind";
        private const string SingleBlkType  = "BricsCadRc.Core.SingleBarBlock";
        private const string ShapeLibType   = "BricsCadRc.Core.ShapeCodeLibrary";

        private const string AutoRebarType  = "BricsCadRc.Core.AutoRebarEngine";
        private const string PunchEngType   = "BricsCadRc.Core.PunchingTagEngine";
        private const string PunchCmdType   = "BricsCadRc.Commands.PunchingTagCommands";
        private const string PosCounterType = "BricsCadRc.Core.PositionCounter";
        private const string DistApp        = "RC_BAR_BLOCK";
        private const string AnnotApp       = "RC_BAR_ANNOT";
        private const string LabelApp       = "RC_BAR_LABEL";
        private const string SlabLayer      = "SD-PILED-RAFT";

        private const int KPolyline = 1, KBlock = 2;

        private static readonly string[] ExpectedSteps =
        {
            "SETUP", "S1", "S3", "S4", "S5", "S5D", "S5U", "S6", "S7", "S8", "P4",
            "Z0", "Z0V", "S9SETUP", "S13", "S9", "S12", "S10", "S11", "S14SETUP", "S14", "S15SETUP", "S15",
            "SCSETUP", "SC", "SCU", "SCR", "S13PSETUP", "S13P", "S9L", "S16SETUP", "S16",
            "S17SETUP", "S17",
        };

        // S5 → S5D → _.U → S5U: obiekty kompletu 501 (ObjectId ważne w obrębie otwartego rysunku)
        private static ObjectId _s5Bar, _s5Label, _s5Dist, _s5Annot;

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

        // ================================================================
        // Komendy
        // ================================================================

        [CommandMethod("RCSMOKE_E3_SETUP", CommandFlags.Modal)]
        public void Setup()
        {
            _results.Clear();
            _executed.Clear();

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

                Check(step, "typy etapu 3a w wtyczce (SingleBarBlock, BarFormat, BarFormatKind, BarLengthRules)", () =>
                {
                    foreach (var tn in new[] { SingleBlkType, BarFormatType, BarFormatKind, "BricsCadRc.Core.BarLengthRules" })
                        Plugin.GetType(tn, true);
                    return "OK";
                });

                Check(step, "BarFormat.Current domyślnie Polyline (zero zmian dla użytkownika)", () =>
                {
                    string cur = Convert.ToString(StaticProp(BarFormatType, "Current"), Inv);
                    return True(cur == "Polyline", "Current=" + cur
                        + " (sprawdź %APPDATA%\\NaSmartRc\\barformat.txt i zmienną RC_REG_BARFORMAT)");
                });

                Check(step, "SingleBarBlock.UseAnonymousBtr = true", () =>
                {
                    var f = Plugin.GetType(SingleBlkType, true).GetField("UseAnonymousBtr",
                                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (f == null) throw new AssertFail("brak pola UseAnonymousBtr");
                    return True((bool)f.GetValue(null), "UseAnonymousBtr=" + f.GetValue(null));
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
            });
        }

        /// <summary>Definicja pręta S1.</summary>
        private sealed class Def
        {
            public string Code, Mark;
            public int D;
            public double A, B, C;
            public Point3d At;
        }

        // ---- S1: PlaceBar Block vs Polyline (00/11/21/13/44/99) ----
        [CommandMethod("RCSMOKE_E3_S1", CommandFlags.Modal)]
        public void S1()
        {
            const string step = "S1";
            Run(step, db =>
            {
                var defs = new[]
                {
                    new Def { Code = "00", Mark = "H12-81", D = 12, A = 2000,            At = new Point3d(0,    0, 0) },
                    new Def { Code = "11", Mark = "H12-82", D = 12, A = 1000, B = 300,   At = new Point3d(0, 2000, 0) },
                    new Def { Code = "21", Mark = "H10-83", D = 10, A = 500,  B = 300, C = 500, At = new Point3d(0, 4000, 0) },
                    new Def { Code = "13", Mark = "H12-84", D = 12, A = 800,  B = 200, C = 500, At = new Point3d(0, 6000, 0) },
                    new Def { Code = W4Compat.RingCode, Mark = "H16-85", D = 16, A = 600,             At = new Point3d(0, 8000, 0) },
                    new Def { Code = "99", Mark = "H16-86", D = 16, A = 1500,            At = new Point3d(0, 10000, 0) },
                };

                // Niestandardowe zmienne bieżące (przywracane w finally)
                ObjectId oldLayer = db.Clayer, oldLt = db.Celtype;
                var oldColor = db.Cecolor;
                double oldLts = db.Celtscale;
                var oldLw = db.Celweight;
                try
                {
                    Check(step, "zmienne bieżące: CLAYER / CECOLOR / CELTYPE / CELTSCALE / CELWEIGHT niestandardowe", () =>
                    {
                        ObjectId layerId, ltId;
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            layerId = EnsureLayer(tr, db, TestLayer, 3);
                            ltId    = EnsureLinetype(tr, db, TestLinetype);
                            tr.Commit();
                        }
                        db.Clayer    = layerId;
                        db.Cecolor   = Teigha.Colors.Color.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, 1);
                        db.Celtype   = ltId;
                        db.Celtscale = TestLtScale;
                        db.Celweight = LineWeight.LineWeight050;
                        return $"CLAYER={TestLayer} CECOLOR=1 CELTYPE={TestLinetype} CELTSCALE={F(TestLtScale)} CELWEIGHT=0.50";
                    });

                    foreach (var d in defs)
                    {
                        var def = d;
                        string k = def.Code + " ";
                        ObjectId plId = ObjectId.Null, brId = ObjectId.Null;

                        Check(step, k + "PlaceBar Polyline i Block w tym samym punkcie", () =>
                        {
                            SetFormat("Polyline");
                            plId = (ObjectId)Call(SingleBarType, "PlaceBar", db, NewBar(def), def.At);
                            SetFormat("Block");
                            try { brId = (ObjectId)Call(SingleBarType, "PlaceBar", db, NewBar(def), def.At); }
                            finally { SetFormat("Polyline"); }
                            return "polilinia=" + plId.Handle + " blok=" + brId.Handle;
                        });
                        if (plId.IsNull || brId.IsNull) continue;

                        using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                        {
                            var pl = tr.GetObject(plId, OpenMode.ForRead) as Polyline;
                            var br = tr.GetObject(brId, OpenMode.ForRead) as BlockReference;
                            Check(step, k + "typy encji (Polyline / BlockReference)", () =>
                                True(pl != null && br != null, "polilinia=" + (pl != null) + " blok=" + (br != null)));
                            if (pl == null || br == null) continue;

                            Check(step, k + "KindOf: Polyline / Block", () =>
                                Eq("KindOf polilinii", Convert.ToString(Call(BarEntityType, "KindOf", pl), Inv), "Polyline") + "; "
                                + Eq("KindOf bloku", Convert.ToString(Call(BarEntityType, "KindOf", br), Inv), "Block"));

                            // ---- Geometria ----
                            var ringsP = PolylineRings(tr, db, pl);
                            var ringsB = BlockRings(tr, br);
                            Check(step, k + "pierścienie obrysu WCS ±1e-6 (polilinia + RC_BAR_LINK vs zawartość BTR)", () =>
                            {
                                Int("liczba pierścieni", ringsB.Count, ringsP.Count);
                                if (W4Compat.IsRing(def.Code)) Int("pierścienie 44/75", ringsP.Count, 2);
                                var sb = new StringBuilder();
                                for (int i = 0; i < ringsP.Count; i++)
                                    sb.Append("[" + i.ToString(Inv) + "] ").Append(Ring(ringsB[i], ringsP[i])).Append("; ");
                                return sb.ToString();
                            });

                            var beP = OpenBar(tr, plId, KPolyline);
                            var beB = OpenBar(tr, brId, KBlock);
                            Check(step, k + "BarEntity.OutlineVertices blok = polilinia ±1e-6", () =>
                                Pts(Outline(beB, tr), Outline(beP, tr)));
                            Check(step, k + "BarEntity.FirstOutlineVertex blok = polilinia ±1e-6", () =>
                                Pt("FirstOutlineVertex", NPt(Inst(beB, "FirstOutlineVertex", tr)), NPt(Inst(beP, "FirstOutlineVertex", tr))));
                            Check(step, k + "BarEntity.MidPoint blok = polilinia ±1e-6", () =>
                                Pt("MidPoint", NPt(Inst(beB, "MidPoint", tr)), NPt(Inst(beP, "MidPoint", tr))));
                            Check(step, k + "BarEntity.ClosestPoint blok = polilinia ±1e-6", () =>
                            {
                                var probe = def.At + new Vector3d(-50, 150, 0);
                                return Pt("ClosestPoint", NPt(Inst(beB, "ClosestPoint", tr, probe)), NPt(Inst(beP, "ClosestPoint", tr, probe)));
                            });

                            Check(step, k + (W4Compat.IsRing(def.Code) ? "punkt wstawienia bloku = punkt wstawienia polilinii (pierścień)"
                                                              : "punkt wstawienia bloku = axis[0] polilinii"), () =>
                            {
                                var exp = W4Compat.IsRing(def.Code)
                                    ? new Point3d(def.At.X, def.At.Y, 0)
                                    : (Point3d)Call(SingleBarType, "GetAxisFirstPointFromOutline", pl, def.Code);
                                return Pt("Position", br.Position, exp) + "; " + Eq("Rotation", F(br.Rotation), "0")
                                       + "; skala=" + F(br.ScaleFactors.X) + "," + F(br.ScaleFactors.Y) + "," + F(br.ScaleFactors.Z);
                            });

                            // ---- XData ----
                            var rP = ReadRecord(pl);
                            var rB = ReadRecord(br);
                            Check(step, k + "XData [0]–[13] blok = polilinia", () =>
                            {
                                Int("rekord polilinii", rP.Length, PlacedPolyLen);
                                if (rB.Length < HeadLen) throw new AssertFail("rekord bloku krótszy niż 14: " + rB.Length);
                                for (int i = 0; i < HeadLen; i++) SameField(i, rP[i], rB[i]);
                                return "Mark=" + Convert.ToString(rB[1].Value, Inv) + " kształt=" + Convert.ToString(rB[3].Value, Inv);
                            });
                            Check(step, k + "blok: rekord 16 (W4d: [15] = 2021), [14] = Int16 2", () => Int("długość rekordu", rB.Length, PlacedBlockLen) + "; " + Ver2(rB));

                            // ---- BTR ----
                            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                            Check(step, k + "BTR anonimowy *U, Explodable = false, refs = 1", () =>
                                True(btr.IsAnonymous && btr.Name.StartsWith("*U", StringComparison.OrdinalIgnoreCase),
                                     "BTR=" + btr.Name + " IsAnonymous=" + btr.IsAnonymous)
                                + "; " + True(!btr.Explodable, "Explodable=" + btr.Explodable)
                                + "; " + Int("refs", btr.GetBlockReferenceIds(true, true).Count, 1));
                            Check(step, k + "BTR: zawartość bez XData, warstwa 0, kolor/rodzaj linii/grubość ByBlock", () =>
                            {
                                int n = 0;
                                foreach (ObjectId eid in btr)
                                {
                                    if (eid.IsErased) continue;
                                    var e = (Entity)tr.GetObject(eid, OpenMode.ForRead);
                                    n++;
                                    using (var xd = e.XData) if (xd != null) throw new AssertFail("encja BTR z XData: " + e.GetType().Name);
                                    if (e.Layer != "0") throw new AssertFail("warstwa encji BTR: " + e.Layer);
                                    if (e.ColorIndex != 0) throw new AssertFail("kolor encji BTR: " + e.ColorIndex);
                                    if (!string.Equals(e.Linetype, "ByBlock", StringComparison.OrdinalIgnoreCase))
                                        throw new AssertFail("rodzaj linii encji BTR: " + e.Linetype);
                                    if (e.LineWeight != LineWeight.ByBlock) throw new AssertFail("grubość encji BTR: " + e.LineWeight);
                                }
                                return Int("encje BTR", n, ringsP.Count);
                            });

                            // ---- Efektywne właściwości ----
                            Check(step, k + "efektywne właściwości blok = polilinia (warstwa, kolor, rodzaj linii, skala, grubość)", () =>
                            {
                                var inner = FirstBtrEntity(tr, btr);
                                if (inner == null) throw new AssertFail("pusty BTR");
                                string ep = Eff(pl.Layer, pl.ColorIndex, pl.Linetype, pl.LinetypeScale, pl.LineWeight);
                                string eb = Eff(br.Layer, br.ColorIndex, br.Linetype, br.LinetypeScale * inner.LinetypeScale, br.LineWeight);
                                if (ep != eb) throw new AssertFail("polilinia: " + ep + " | blok: " + eb);
                                return eb + "; " + Eq("warstwa", br.Layer, BarLayer);
                            });
                        }
                    }
                }
                finally
                {
                    db.Clayer = oldLayer;
                    db.Cecolor = oldColor;
                    db.Celtype = oldLt;
                    db.Celtscale = oldLts;
                    db.Celweight = oldLw;
                    try { SetFormat("Polyline"); }
                    catch (System.Exception ex) { Add("FAIL", step, "przywrócenie BarFormat.Polyline", ex.Message); }
                }

                Check(step, "BarFormat.Current po teście = Polyline", () =>
                    Eq("Current", Convert.ToString(StaticProp(BarFormatType, "Current"), Inv), "Polyline"));
            });
        }

        // ================================================================
        // Etap 3b — skany szablonów widzą pręty-bloki
        // ================================================================

        // ---- S3: ScanTemplates (strefa rebar_bottom) — blok H10-04 = polilinia H10-04; H10-T pomijany ----
        [CommandMethod("RCSMOKE_E3_S3", CommandFlags.Modal)]
        public void S3()
        {
            const string step = "S3";
            Run(step, db =>
            {
                CleanModelSpace(db);
                // Dwie strefy rebar_bottom daleko od siebie: Z1 — blok H10-04 (+ blok H10-T), Z2 — polilinia H10-04
                var z1 = AddZone(db, "rebar_bottom", 100000, 100000);
                var z2 = AddZone(db, "rebar_bottom", 100000, 200000);
                var def = new Def { Code = "00", Mark = "H10-04", D = 10, A = 3000 };
                ObjectId blk = ObjectId.Null, pl = ObjectId.Null;
                Check(step, "PlaceBar H10-04: blok w Z1, polilinia w Z2 (slot 0), blok H10-T w Z1 (slot 1)", () =>
                {
                    blk = PlaceAs("Block", def, Slot(z1, 0));
                    pl  = PlaceAs("Polyline", def, Slot(z2, 0));
                    var t = PlaceAs("Block", new Def { Code = "00", Mark = "H10-T", D = 10, A = 3000 }, Slot(z1, 1));
                    return "blok=" + EntType(db, blk) + " polilinia=" + EntType(db, pl) + " H10-T=" + EntType(db, t);
                });
                if (blk.IsNull || pl.IsNull) return;

                Check(step, "ScanTemplates(Z1, H10) widzi blok H10-04 i pomija H10-T (numer 0)", () =>
                {
                    var t1 = ScanVia("ScanTemplates", db, z1, 10);
                    return Int("szablony Z1", t1.Count, 1) + "; " + True(t1[0].id == blk, "id=" + t1[0].id.Handle)
                           + "; " + Eq("Mark", (string)Prop(t1[0].bar, "Mark"), "H10-04");
                });
                Check(step, "existingCount blok (Z1) = polilinia (Z2), dane szablonu równe", () =>
                {
                    var t1 = ScanVia("ScanTemplates", db, z1, 10);
                    var t2 = ScanVia("ScanTemplates", db, z2, 10);
                    return Int("szablony Z2", t2.Count, t1.Count) + "; " + True(t2[0].id == pl, "id Z2=" + t2[0].id.Handle)
                           + "; " + Eq("szablon", Sig(t1[0].bar), Sig(t2[0].bar));
                });
                Check(step, "ScanTemplates(Z1, H12) = 0 (inna średnica)", () =>
                    Int("szablony H12 w Z1", ScanVia("ScanTemplates", db, z1, 12).Count, 0));
            });
        }

        // ---- S4: IsExistingPosNrUB / ScanUBTemplates dla 21 — blok = polilinia ----
        [CommandMethod("RCSMOKE_E3_S4", CommandFlags.Modal)]
        public void S4()
        {
            const string step = "S4";
            Run(step, db =>
            {
                var def = new Def { Code = "21", Mark = "H12-01", D = 12, A = 500, B = 140, C = 500 };
                var sig = new Dictionary<string, string>();
                foreach (var fmt in new[] { "Polyline", "Block" })
                {
                    string k = fmt + " ";
                    CleanModelSpace(db);
                    var z = AddZone(db, "rebar_bottom", 100000, 100000);
                    ObjectId id = ObjectId.Null;
                    Check(step, k + "PlaceBar 21 H12-01 w strefie (+ pręt bez numeru H12-T kształt 21)", () =>
                    {
                        id = PlaceAs(fmt, def, Slot(z, 0));
                        PlaceAs(fmt, new Def { Code = "21", Mark = "H12-T", D = 12, A = 500, B = 140, C = 500 }, Slot(z, 1));
                        return EntType(db, id);
                    });
                    if (id.IsNull) continue;
                    Check(step, k + "IsExistingPosNrUB: (12, 1) = true, (12, 2) = false, (10, 1) = false", () =>
                        Eq("(12,1)", Call(AutoRebarType, "IsExistingPosNrUB", db, 12, 1), true) + "; "
                        + Eq("(12,2)", Call(AutoRebarType, "IsExistingPosNrUB", db, 12, 2), false) + "; "
                        + Eq("(10,1)", Call(AutoRebarType, "IsExistingPosNrUB", db, 10, 1), false));
                    Check(step, k + "ScanUBTemplates(strefa, 12) = 1 (ten pręt, bez H12-T), (strefa, 10) = 0", () =>
                    {
                        var u = ScanVia("ScanUBTemplates", db, z, 12);
                        string r = Int("UB H12", u.Count, 1) + "; " + True(u[0].id == id, "id=" + u[0].id.Handle) + "; "
                                   + Int("UB H10", ScanVia("ScanUBTemplates", db, z, 10).Count, 0);
                        sig[fmt] = Sig(u[0].bar);
                        return r;
                    });
                }
                Check(step, "dane szablonu UB: blok = polilinia", () =>
                {
                    if (!sig.ContainsKey("Polyline") || !sig.ContainsKey("Block")) throw new AssertFail("brak wyniku jednego z formatów");
                    return Eq("szablon", sig["Block"], sig["Polyline"]);
                });
            });
        }

        // ---- S5: komplet 501 w trybie Block (dalej S5D: DeleteSummaryBars, _.U, S5U: przywrócone) ----
        [CommandMethod("RCSMOKE_E3_S5", CommandFlags.Modal)]
        public void S5()
        {
            const string step = "S5";
            Run(step, db =>
            {
                CleanModelSpace(db);
                _s5Bar = _s5Label = _s5Dist = _s5Annot = ObjectId.Null;
                var doc = Application.DocumentManager.MdiActiveDocument;
                Check(step, "PlaceSummaryBars 501 (×14) w trybie Block", () =>
                {
                    SetFormat("Block");
                    try { PlaceSummary(doc, (501, 14, new Point3d(0, 0, 0))); }
                    finally { SetFormat("Polyline"); }
                    return "OK";
                });
                Check(step, "komplet 501: pręt-blok + etykieta + rozkład + opis", () =>
                {
                    var set = SummarySet(db, "H12-501");
                    (_s5Bar, _s5Label, _s5Dist, _s5Annot) = set;
                    return DescribeSet(set);
                });
            });
        }

        [CommandMethod("RCSMOKE_E3_S5D", CommandFlags.Modal)]
        public void S5D()
        {
            const string step = "S5D";
            Run(step, db =>
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                Check(step, "DeleteSummaryBars usuwa pręt-blok 501, etykietę, rozkład i opis", () =>
                {
                    if (_s5Bar.IsNull || _s5Label.IsNull || _s5Dist.IsNull || _s5Annot.IsNull)
                        throw new AssertFail("brak kompletu z S5");
                    int n = Convert.ToInt32(Call(PunchEngType, "DeleteSummaryBars", doc), Inv);
                    string st = S5State();
                    if (!(_s5Bar.IsErased && _s5Label.IsErased && _s5Dist.IsErased && _s5Annot.IsErased))
                        throw new AssertFail("nie wszystko usunięte: " + st);
                    return "DeleteSummaryBars=" + n.ToString(Inv) + "; " + st;
                });
            });
        }

        [CommandMethod("RCSMOKE_E3_S5U", CommandFlags.Modal)]
        public void S5U()
        {
            const string step = "S5U";
            Run(step, db =>
            {
                Check(step, "_.U przywraca pręt-blok 501, etykietę, rozkład i opis", () =>
                {
                    if (_s5Bar.IsNull) throw new AssertFail("brak kompletu z S5");
                    string st = S5State();
                    if (_s5Bar.IsErased || _s5Label.IsErased || _s5Dist.IsErased || _s5Annot.IsErased)
                        throw new AssertFail("po _.U nadal usunięte: " + st);
                    return st;
                });
                Check(step, "po _.U: komplet 501 spójny (pręt-blok v2, etykieta, rozkład → pręt, opis)", () =>
                {
                    var set = SummarySet(db, "H12-501");
                    return True(set.bar == _s5Bar && set.label == _s5Label && set.dist == _s5Dist && set.annot == _s5Annot,
                                DescribeSet(set));
                });
            });
        }

        // ================================================================
        // Etap 3c — tworzenie w trybie Block
        // ================================================================

        // ---- S6: pełny AutoRebar w trybie Block, dwa przebiegi ----
        [CommandMethod("RCSMOKE_E3_S6", CommandFlags.Modal)]
        public void S6()
        {
            const string step = "S6";
            Run(step, db =>
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                CleanModelSpace(db);
                var (bottomId, topId) = AddSlabs(db, true);
                List<BarInfo> bars1 = null;
                List<int> used1 = null;
                SetFormat("Block");
                try
                {
                    Check(step, "AutoRebar 1. przebieg (B1, B2, T1, T2, UB B1, UB B2) w trybie Block", () =>
                    {
                        string r = RunAutoRebar(doc, bottomId, topId);
                        bars1 = BarsInMs(db);
                        used1 = UsedNumbers(db);
                        return r + "; pręty=" + bars1.Count.ToString(Inv) + " numery=" + string.Join(",", used1);
                    });
                    if (bars1 == null) return;
                    Check(step, "wszystkie pręty RC_SINGLE_BAR to bloki v2 (brak polilinii)", () => AllBlocks(db, bars1));
                    Check(step, "rozkłady wskazują na pręty-bloki ([18])", () => DistSourcesAreBlocks(db));
                    Check(step, "grot etykiety każdego pręta na obrysie bloku (±1e-6)", () => LabelTipsOnBars(db, bars1));

                    Check(step, "AutoRebar 2. przebieg (to samo)", () => RunAutoRebar(doc, bottomId, topId));
                    Check(step, "2. przebieg: te same pręty-szablony (bez nowych), te same numery", () =>
                    {
                        var bars2 = BarsInMs(db);
                        var used2 = UsedNumbers(db);
                        string h1 = string.Join(",", bars1.Select(b => b.Id.Handle.ToString()).OrderBy(s => s, StringComparer.Ordinal));
                        string h2 = string.Join(",", bars2.Select(b => b.Id.Handle.ToString()).OrderBy(s => s, StringComparer.Ordinal));
                        return Int("liczba prętów", bars2.Count, bars1.Count) + "; " + Eq("pręty (handle)", h2, h1) + "; "
                               + Eq("numery", string.Join(",", used2), string.Join(",", used1));
                    });
                    Check(step, "2. przebieg: rozkłady wskazują na pręty-bloki ([18])", () => DistSourcesAreBlocks(db));
                }
                finally { SetFormat("Polyline"); }
                Check(step, "BarFormat.Current po teście = Polyline", () =>
                    Eq("Current", Convert.ToString(StaticProp(BarFormatType, "Current"), Inv), "Polyline"));
            });
        }

        // ---- S7: przebicie 2× w trybie Block = jeden komplet 501 / 502 ----
        [CommandMethod("RCSMOKE_E3_S7", CommandFlags.Modal)]
        public void S7()
        {
            const string step = "S7";
            Run(step, db =>
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                CleanModelSpace(db);
                SetFormat("Block");
                try
                {
                    for (int i = 1; i <= 2; i++)
                    {
                        int run = i;
                        Check(step, $"przebieg {run}: DeleteSummaryBars + PlaceSummaryBars (501 ×14, 502 ×28)", () =>
                        {
                            int n = Convert.ToInt32(Call(PunchEngType, "DeleteSummaryBars", doc), Inv);
                            if (run == 2 && n <= 0) throw new AssertFail("2. przebieg: DeleteSummaryBars nic nie usunął (" + n.ToString(Inv) + ")");
                            PlaceSummary(doc, (501, 14, new Point3d(0, 0, 0)), (502, 28, new Point3d(5000, 0, 0)));
                            return "usunięto=" + n.ToString(Inv);
                        });
                    }
                }
                finally { SetFormat("Polyline"); }
                Check(step, "jeden komplet 501 i jeden 502 (pręt-blok, etykieta, rozkład, opis)", () =>
                    DescribeSet(SummarySet(db, "H12-501")) + " | " + DescribeSet(SummarySet(db, "H16-502")));
                Check(step, "brak innych prętów / etykiet / rozkładów / opisów", () =>
                    Int("pręty", BarsInMs(db).Count, 2) + "; " + Int("etykiety", CountApp<MLeader>(db, LabelApp), 2) + "; "
                    + Int("rozkłady", CountApp<BlockReference>(db, DistApp), 2) + "; " + Int("opisy", CountApp<BlockReference>(db, AnnotApp), 2));
                Check(step, "grot etykiet 501 / 502 na obrysie bloku (±1e-6)", () => LabelTipsOnBars(db, BarsInMs(db)));
            });
        }

        // ---- S8: GetBarArrowTip blok vs polilinia ±1e-6 ----
        [CommandMethod("RCSMOKE_E3_S8", CommandFlags.Modal)]
        public void S8()
        {
            const string step = "S8";
            Run(step, db =>
            {
                CleanModelSpace(db);
                var defs = new[]
                {
                    new Def { Code = "00", Mark = "H12-91", D = 12, A = 2000,                    At = new Point3d(0,    0, 0) },
                    new Def { Code = "21", Mark = "H10-92", D = 10, A = 500, B = 300, C = 500,   At = new Point3d(0, 2000, 0) },
                    new Def { Code = "13", Mark = "H12-93", D = 12, A = 800, B = 200, C = 500,   At = new Point3d(0, 4000, 0) },
                    new Def { Code = W4Compat.RingCode, Mark = "H16-94", D = 16, A = 600,                     At = new Point3d(0, 6000, 0) },
                };
                foreach (var d in defs)
                {
                    var def = d;
                    string k = def.Code + " ";
                    ObjectId plId = ObjectId.Null, brId = ObjectId.Null;
                    Check(step, k + "PlaceBar Polyline i Block w tym samym punkcie", () =>
                    {
                        plId = PlaceAs("Polyline", def, def.At);
                        brId = PlaceAs("Block", def, def.At);
                        return EntType(db, plId) + " / " + EntType(db, brId);
                    });
                    if (plId.IsNull || brId.IsNull) continue;
                    Check(step, k + "GetBarArrowTip blok = polilinia ±1e-6 (domyślny punkt tekstu RC_BAR i dwa inne)", () =>
                    {
                        var sb = new StringBuilder();
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            var mid = NPt(Inst(OpenBar(tr, plId, KPolyline), "MidPoint", tr));
                            var midB = NPt(Inst(OpenBar(tr, brId, KBlock), "MidPoint", tr));
                            Pt("MidPoint (domyślny tekst RC_BAR)", midB, mid);
                            var barP = Call(SingleBarType, "ReadBarXData", (Entity)tr.GetObject(plId, OpenMode.ForRead));
                            var barB = Call(SingleBarType, "ReadBarXData", (Entity)tr.GetObject(brId, OpenMode.ForRead));
                            var probes = new[]
                            {
                                new Point3d(mid.X - 50, mid.Y + 150, 0),
                                def.At + new Vector3d(def.A + 300, -400, 0),
                                def.At + new Vector3d(-200, -200, 0),
                            };
                            foreach (var probe in probes)
                            {
                                var tipP = (Point3d)Call(SingleBarType, "GetBarArrowTip", plId, barP, probe, tr);
                                var tipB = (Point3d)Call(SingleBarType, "GetBarArrowTip", brId, barB, probe, tr);
                                sb.Append(Pt("grot", tipB, tipP)).Append("; ");
                            }
                            tr.Commit();
                        }
                        return sb.ToString();
                    });
                }
            });
        }

        // ---- P4: 0-POPRAWKI p.4 — pręt bez numeru w strefie szablonów nie zmienia wyniku AutoRebar ----
        [CommandMethod("RCSMOKE_E3_P4", CommandFlags.Modal)]
        public void P4()
        {
            const string step = "P4";
            Run(step, db =>
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                foreach (var f in new[] { "Polyline", "Block" })
                {
                    string fmt = f, k = f + " ";
                    P4Result a = null, b = null;
                    Check(step, k + "AutoRebar B1 + T1 bez pręta testowego", () => { a = P4Run(doc, fmt, null, null); return a.Summary; });
                    if (a == null) continue;
                    Check(step, k + "AutoRebar B1 + T1 z H10-T (rebar_bottom) i H12-T (rebar_top) w slocie 0, długość = szablon", () =>
                    {
                        b = P4Run(doc, fmt, a.BottomLen, a.TopLen);
                        return b.Summary;
                    });
                    if (b == null) continue;
                    Check(step, k + "szablony dołu (kształt, długość, położenie) jak bez H10-T", () => Eq("dół", b.BottomSig, a.BottomSig));
                    Check(step, k + "szablony góry (Mark, długość, położenie) jak bez H12-T", () => Eq("góra", b.TopSig, a.TopSig));
                    Check(step, k + "rozkłady (liczba, opisy; numery dołu pominięte — licznik rysunku) jak bez prętów testowych", () =>
                        Eq("rozkłady", b.DistSig, a.DistSig));
                    Check(step, k + "żaden rozkład nie wskazuje na pręt testowy", () => Int("rozkłady → pręt -T", b.DistOnTest, 0));
                }
                Check(step, "BarFormat.Current po teście = Polyline", () =>
                    Eq("Current", Convert.ToString(StaticProp(BarFormatType, "Current"), Inv), "Polyline"));
            });
        }

        private sealed class P4Result
        {
            public string Summary, BottomSig, TopSig, DistSig;
            public double BottomLen, TopLen;
            public int DistOnTest;
        }

        /// <summary>Płyta (rzut dolny + górny), strefy szablonów w stałych miejscach, opcjonalnie pręty -T w slocie 0, AutoRebar B1 + T1.</summary>
        private static P4Result P4Run(Document doc, string fmt, double? lenBottom, double? lenTop)
        {
            var db = doc.Database;
            CleanModelSpace(db);
            var (bottomId, topId) = AddSlabs(db, false);
            double gap = ConstD("TemplateZoneGap"), w = ConstD("TemplateZoneWidth");
            var zb = AddZone(db, "rebar_bottom", SlabW + gap, SlabH);
            var zt = AddZone(db, "rebar_top", SlabW + gap + w + gap, SlabH + TopDy);

            var testIds = new List<ObjectId>();
            if (lenBottom.HasValue)
                testIds.Add(PlaceAs("Block", new Def { Code = "00", Mark = "H10-T", D = 10, A = lenBottom.Value }, Slot(zb, 0)));
            if (lenTop.HasValue)
                testIds.Add(PlaceAs("Block", new Def { Code = "00", Mark = "H12-T", D = 12, A = lenTop.Value }, Slot(zt, 0)));
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var tid in testIds)
                {
                    var v = NPt(Inst(OpenBar(tr, tid, KBlock), "FirstOutlineVertex", tr));
                    if (!Inside(v, zb) && !Inside(v, zt)) throw new AssertFail("pręt testowy poza strefą: " + P(v));
                }
                tr.Commit();
            }

            int nB, nT;
            SetFormat(fmt);
            try
            {
                nB = Convert.ToInt32(Call(AutoRebarType, "GenerateLayer", doc, bottomId, "rebar_bottom", "X", "B1", 10, 200.0, 40.0, true), Inv);
                nT = Convert.ToInt32(Call(AutoRebarType, "GenerateLayer", doc, topId, "rebar_top", "X", "T1", 12, 200.0, 40.0, true), Inv);
            }
            finally { SetFormat("Polyline"); }
            if (nB < 0 || nT < 0) throw new AssertFail($"GenerateLayer: B1={nB} T1={nT}");

            var res = new P4Result();
            var bottom = new List<(string sig, double a, double y)>();
            var top    = new List<(string sig, double a, double y)>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var bar in BarsInMs(db))
                {
                    if ((bar.Mark ?? "").EndsWith("-T", StringComparison.Ordinal)) continue;
                    var v = NPt(Inst(OpenBar(tr, bar.Id, KPolyline | KBlock), "FirstOutlineVertex", tr));
                    string vs = "(" + v.X.ToString("0.00", Inv) + "," + v.Y.ToString("0.00", Inv) + ")";
                    if (bar.D == 10) bottom.Add((bar.Shape + " A=" + F(bar.A) + " v=" + vs, bar.A, v.Y));
                    else             top.Add((bar.Mark + " " + bar.Shape + " A=" + F(bar.A) + " v=" + vs, bar.A, v.Y));
                }

                var dists = new List<string>();
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    var rec = ReadRecord(br, DistApp);
                    if (rec.Length == 0) continue;
                    string mark = rec.Length > 1 ? Convert.ToString(rec[1].Value, Inv) : "";
                    // Numery dołu zależą od licznika rysunku (rośnie między przebiegami) — porównanie bez numeru
                    if (mark.EndsWith(" B1", StringComparison.Ordinal))
                        mark = System.Text.RegularExpressions.Regex.Replace(mark, @"^(\d+\s+)?(H\d+)-\d+", "$1$2-nn");
                    dists.Add(mark);
                    if (rec.Length > 18 && testIds.Any(t => SameHandle(rec[18].Value, t.Handle))) res.DistOnTest++;
                }
                tr.Commit();
                dists.Sort(StringComparer.Ordinal);
                res.DistSig = "n=" + dists.Count.ToString(Inv) + " [" + string.Join(" | ", dists) + "]";
            }
            bottom.Sort((x, y) => string.CompareOrdinal(x.sig, y.sig));
            top.Sort((x, y) => string.CompareOrdinal(x.sig, y.sig));
            if (bottom.Count == 0 || top.Count == 0) throw new AssertFail($"brak szablonów: dół={bottom.Count} góra={top.Count}");
            res.BottomSig = string.Join(" | ", bottom.Select(x => x.sig));
            res.TopSig    = string.Join(" | ", top.Select(x => x.sig));
            res.BottomLen = bottom.OrderByDescending(x => x.y).First().a;   // slot 0 strefy
            res.TopLen    = top.OrderByDescending(x => x.y).First().a;
            res.Summary   = $"B1={nB} T1={nT} szablony dół={bottom.Count} góra={top.Count} rozkłady {res.DistSig.Split(' ')[0]}"
                          + $" L0 dół={F(res.BottomLen)} góra={F(res.TopLen)}";
            return res;
        }

        // ================================================================
        // Etap 3d — edycja (BarEditApply, RC_UPDATE_BAR), 3e — COPY / MIRROR, 3e2 — SCALE
        // ================================================================

        private const string BarCmdType   = "BricsCadRc.Commands.BarCommands";
        private const string EditApplyType = "BricsCadRc.Core.BarEditApply";

        // Z0 → Z0V
        private static ObjectId _z0Bar;
        // S9SETUP → (COPY) → S13 → S9 → (_.U) → S12
        private static ObjectId _s9Orig, _s9OrigLabel, _s9OrigDist, _s9Copy, _s9CopyDist, _s9CloneSrc, _s9Clone;
        private static long _s9OrigBtr, _s9CopyBtr;
        private static List<Point3d> _s9OrigOutline;
        private static Point3d _s9OrigPos;
        // S14SETUP → (COPY) → S14; S15SETUP → (MIRROR) → S15
        private static ObjectId _s14Orig, _s14OrigLabel, _s15Orig, _s15OrigLabel;
        private static List<Point3d> _s15OrigOutline;
        // SCSETUP → (SCALE) → SC → (_.UNDO 1) → SCU → (_.REDO) → SCR
        private static ObjectId _scBar;
        private static List<Vector3d> _scOffsets;
        private const double ScBaseX = 1000, ScY = 200000;

        // ---- Z0: informacyjnie — RC_EDIT_BAR (BarEditApply) na polilinii, długość po CommandEnded (watcher wzorca) ----
        [CommandMethod("RCSMOKE_E3_Z0", CommandFlags.Modal)]
        public void Z0()
        {
            const string step = "Z0";
            Run(step, db =>
            {
                CleanModelSpace(db);
                _z0Bar = ObjectId.Null;
                Check(step, "Polyline: pręt H12-49 00 A=3000 + etykieta + rozkład, BarEditApply A → 3500 (w tej komendzie)", () =>
                {
                    var def = new Def { Code = "00", Mark = "H12-49", D = 12, A = 3000, At = new Point3d(0, 30000, 0) };
                    var (bar, _) = PlaceBarWithLabel(db, "Polyline", def);
                    Distribute(db, bar, 40, 32040, 3040, 33000, true, 200, "B1");
                    EditApply(db, bar, upd => { SetProp(upd, "LengthA", 3500.0); SetProp(upd, "TotalLength", TotalLength(upd)); }, "49");
                    _z0Bar = bar;
                    return "A po Apply (przed CommandEnded)=" + F(BarA(db, bar));
                });
            });
        }

        [CommandMethod("RCSMOKE_E3_Z0V", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Z0V()
        {
            const string step = "Z0V";
            Run(step, db =>
            {
                Check(step, "A pręta-polilinii po CommandEnded komendy z BarEditApply = 3500, rozkład[8] = 3500 " +
                            "(poprawka #1, W1b: wzorzec 4000 — BarGeometryWatcher doliczał przyrost drugi raz)", () =>
                {
                    if (_z0Bar.IsNull || _z0Bar.IsErased) throw new AssertFail("brak pręta z Z0");
                    double a = BarA(db, _z0Bar);
                    double dist = DistsOf(db, _z0Bar).Select(x => Convert.ToDouble(x.rec[8].Value, Inv)).FirstOrDefault();
                    return Num("A", a, 3500) + " " + Num("rozkład[8]", dist, 3500);
                });
            });
        }

        // ---- S9SETUP: pręt-blok H12-41 + etykieta + rozkład + opis (pod COPY w skrypcie) ----
        [CommandMethod("RCSMOKE_E3_S9SETUP", CommandFlags.Modal)]
        public void S9Setup()
        {
            const string step = "S9SETUP";
            Run(step, db =>
            {
                CleanModelSpace(db);
                _s9Orig = _s9OrigLabel = _s9OrigDist = _s9Copy = _s9CopyDist = _s9CloneSrc = _s9Clone = ObjectId.Null;
                Check(step, "Block: pręt H12-41 00 A=3000 w (0,50000) + etykieta + rozkład B1 + opis", () =>
                {
                    var def = new Def { Code = "00", Mark = "H12-41", D = 12, A = 3000, At = new Point3d(0, 50000, 0) };
                    (_s9Orig, _s9OrigLabel) = PlaceBarWithLabel(db, "Block", def);
                    (_s9OrigDist, _) = Distribute(db, _s9Orig, 40, 52040, 3040, 53000, true, 200, "B1");
                    var s = ReadBS(db, _s9Orig);
                    _s9OrigBtr = s.Btr;
                    _s9OrigOutline = s.Outline;
                    _s9OrigPos = s.Pos;
                    return Describe(s) + " rozkład=" + _s9OrigDist.Handle;
                });
            });
        }

        // ---- S13: po _.COPY pręt + etykieta + rozkład + opis ----
        [CommandMethod("RCSMOKE_E3_S13", CommandFlags.Modal)]
        public void S13()
        {
            const string step = "S13";
            Run(step, db =>
            {
                if (_s9Orig.IsNull) { Add("FAIL", step, "brak S9SETUP", ""); return; }
                Check(step, "kopia pręta H12-41 (pręt-blok v2), przesunięta o (0,10000)", () =>
                {
                    var copies = BarsInMs(db).Where(b => b.Mark == "H12-41" && b.Id != _s9Orig).ToList();
                    Int("kopie H12-41", copies.Count, 1);
                    _s9Copy = copies[0].Id;
                    var c = ReadBS(db, _s9Copy);
                    _s9CopyBtr = c.Btr;
                    return Describe(c) + "; " + Pts(c.Outline, _s9OrigOutline.Select(p => p + new Vector3d(0, 10000, 0)).ToList());
                });
                if (_s9Copy.IsNull) return;
                Check(step, "kopia: własny BTR *U (≠ oryginał), Explodable = false, refs = 1; oryginał refs = 1, BTR bez zmian", () =>
                {
                    var c = ReadBS(db, _s9Copy); var o = ReadBS(db, _s9Orig);
                    return True(c.Btr != o.Btr, "BTR kopii=" + c.BtrName + " BTR oryginału=" + o.BtrName)
                           + "; " + True(c.Anon && !c.Explodable, "kopia: anon=" + c.Anon + " Explodable=" + c.Explodable)
                           + "; " + Int("refs kopii", c.Refs, 1) + "; " + Int("refs oryginału", o.Refs, 1)
                           + "; " + True(o.Btr == _s9OrigBtr, "BTR oryginału z S9SETUP: " + o.BtrName);
                });
                Check(step, "kopia: BTR bez XData, warstwa 0 / ByBlock", () => BtrContentsOk(db, _s9Copy));
                Check(step, "kopia: XData [0]–[12] = oryginał, [14] = Int16 2, rekord 15", () => SameHead(db, _s9Copy, _s9Orig, 13));
                Check(step, "etykiety: kopia ma własną etykietę z back-linkiem, oryginał — swoją", () =>
                {
                    var (cl, cOk) = LabelOf(db, _s9Copy);
                    var (ol, oOk) = LabelOf(db, _s9Orig);
                    return True(!cl.IsNull && cOk, "etykieta kopii=" + cl.Handle + " back-link=" + cOk)
                           + "; " + True(ol == _s9OrigLabel && oOk, "etykieta oryginału=" + ol.Handle + " back-link=" + oOk)
                           + "; " + True(cl != ol, "etykiety różne");
                });
                // W2 (mapa klonowania, poprawka #3): [18] rozkładu kopii → kopia pręta, rozkładu oryginału → oryginał;
                // opis kopii ↔ rozkład kopii; etykiety n / n (wzorzec: oba rozkłady → oryginał, 2n / bez liczby).
                Check(step, "W2: [18] rozkładu kopii → kopia pręta, oryginału → oryginał; opis kopii ↔ rozkład kopii", () =>
                {
                    var dc = DistsOf(db, _s9Copy); var dO = DistsOf(db, _s9Orig);
                    if (dc.Count != 1 || dO.Count != 1)
                        throw new AssertFail($"rozkłady kopii: {dc.Count}, oryginału: {dO.Count} (pręt={_s9Orig.Handle}, kopia={_s9Copy.Handle}); {DistDump(db)}");
                    _s9CopyDist = dc[0].id;
                    return True(dO[0].id == _s9OrigDist, "rozkład oryginału=" + _s9OrigDist.Handle)
                           + "; " + AnnotPair(db, _s9CopyDist) + "; " + AnnotPair(db, _s9OrigDist);
                });
                // Przygotowanie S9 niepotrzebne od W2 (mapa przepięła [18]) — tylko kontrola liczby na etykietach (n / n)
                Check(step, "etykiety po COPY: kopia i oryginał z liczbą swojego rozkładu (n / n)", () =>
                {
                    if (_s9CopyDist.IsNull) throw new AssertFail("brak rozkładu kopii");
                    string tc = LabelTextOf(db, _s9Copy), to = LabelTextOf(db, _s9Orig);
                    return True(!string.IsNullOrEmpty(tc) && tc == to, "etykieta kopii=" + tc + " oryginału=" + to);
                });
            });
        }

        // ---- S13P: to samo COPY dla pręta-polilinii (zachowanie wzorca) — informacyjnie, do porównania z S13 ----
        private static ObjectId _s13pOrig;

        [CommandMethod("RCSMOKE_E3_S13PSETUP", CommandFlags.Modal)]
        public void S13PSetup()
        {
            const string step = "S13PSETUP";
            Run(step, db =>
            {
                CleanModelSpace(db);
                Check(step, "Polyline: pręt H12-51 00 A=3000 w (0,250000) + etykieta + rozkład B1 + opis", () =>
                {
                    var def = new Def { Code = "00", Mark = "H12-51", D = 12, A = 3000, At = new Point3d(0, 250000, 0) };
                    (_s13pOrig, _) = PlaceBarWithLabel(db, "Polyline", def);
                    var (dist, _) = Distribute(db, _s13pOrig, 40, 252040, 3040, 253000, true, 200, "B1");
                    return "pręt=" + _s13pOrig.Handle + " rozkład=" + dist.Handle;
                });
            });
        }

        [CommandMethod("RCSMOKE_E3_S13P", CommandFlags.Modal)]
        public void S13P()
        {
            const string step = "S13P";
            Run(step, db =>
            {
                Check(step, "W2 (poprawka #3): polilinia po _.COPY — [18] rozkładu kopii → kopia pręta, opis kopii ↔ rozkład kopii", () =>
                {
                    var copies = BarsInMs(db).Where(b => b.Mark == "H12-51" && b.Id != _s13pOrig).Select(b => b.Id).ToList();
                    Int("kopie H12-51", copies.Count, 1);
                    var dc = DistsOf(db, copies[0]); var dO = DistsOf(db, _s13pOrig);
                    if (dc.Count != 1 || dO.Count != 1)
                        throw new AssertFail($"rozkłady kopii: {dc.Count}, oryginału: {dO.Count}; {DistDump(db)}");
                    var (cl, cOk) = LabelOf(db, copies[0]);
                    return AnnotPair(db, dc[0].id) + "; " + AnnotPair(db, dO[0].id)
                           + "; " + True(!cl.IsNull && cOk, "etykieta kopii=" + cl.Handle + " back-link=" + cOk);
                });
            });
        }

        // ---- S9: BarEditApply na kopii (A 3000 → 3500) + copy-on-write przy wspólnym BTR ----
        [CommandMethod("RCSMOKE_E3_S9", CommandFlags.Modal)]
        public void S9()
        {
            const string step = "S9";
            Run(step, db =>
            {
                if (_s9Copy.IsNull) { Add("FAIL", step, "brak kopii z S13", ""); return; }
                Check(step, "BarEditApply na kopii: A 3000 → 3500, numer z dialogu 41", () =>
                {
                    EditApply(db, _s9Copy, upd => { SetProp(upd, "LengthA", 3500.0); SetProp(upd, "TotalLength", TotalLength(upd)); }, "41");
                    return "OK";
                });
                Check(step, "kopia: A = 3500, nowy numer (≠ 41), [14] = 2, BTR własny (refs = 1), obrys = dane", () =>
                {
                    var c = ReadBS(db, _s9Copy);
                    int nr = PosNr(c.Mark);
                    return Num("A", c.A, 3500) + "; " + True(nr > 0 && nr != 41, "Mark=" + c.Mark) + "; " + Ver2(c.Rec)
                           + "; " + Int("refs", c.Refs, 1) + "; " + Num("długość obrysu", OutlineLen(c), 3500)
                           + "; " + Pt("Position (axis[0])", c.Pos, _s9OrigPos + new Vector3d(0, 10000, 0));
                });
                Check(step, "oryginał bez zmian (A, Mark, BTR, obrys)", () =>
                {
                    var o = ReadBS(db, _s9Orig);
                    return Num("A", o.A, 3000) + "; " + Eq("Mark", o.Mark, "H12-41") + "; " + True(o.Btr == _s9OrigBtr, "BTR=" + o.BtrName)
                           + "; " + Pts(o.Outline, _s9OrigOutline);
                });
                Check(step, "rozkład kopii: [8] = 3500, Mark z nowym numerem; rozkład oryginału: [8] = 3000, H12-41", () =>
                {
                    var c = ReadBS(db, _s9Copy);
                    var dc = OneDist(db, _s9Copy); var dO = OneDist(db, _s9Orig);
                    string mc = Convert.ToString(dc[1].Value, Inv), mo = Convert.ToString(dO[1].Value, Inv);
                    string prefC = "H12-" + PosNr(c.Mark).ToString("D2", Inv);
                    return Num("[8] kopii", Convert.ToDouble(dc[8].Value, Inv), 3500) + "; " + True(mc.Contains(prefC), "Mark rozkładu kopii=" + mc)
                           + "; " + Num("[8] oryginału", Convert.ToDouble(dO[8].Value, Inv), 3000) + "; " + True(mo.Contains("H12-41"), "Mark rozkładu oryginału=" + mo);
                });
                Check(step, "grot etykiety kopii na nowym obrysie (±1e-6), tekst z nowym numerem", () =>
                {
                    var c = ReadBS(db, _s9Copy);
                    return LabelTip(db, _s9Copy) + "; " + True(LabelTextOf(db, _s9Copy).Contains(c.Mark), "tekst=" + LabelTextOf(db, _s9Copy));
                });

                // Copy-on-write w Apply: dwie referencje jednego BTR (kopia bez komendy COPY — BarCopyWatcher nie działa)
                Check(step, "copy-on-write: pręt H12-45 + klon referencji (wspólny BTR, refs = 2)", () =>
                {
                    var def = new Def { Code = "00", Mark = "H12-45", D = 12, A = 2000, At = new Point3d(0, 56000, 0) };
                    _s9CloneSrc = PlaceAs("Block", def, def.At);
                    _s9Clone = CloneReference(db, _s9CloneSrc, new Vector3d(0, 1000, 0));
                    var a = ReadBS(db, _s9CloneSrc); var b = ReadBS(db, _s9Clone);
                    return True(a.Btr == b.Btr, "wspólny BTR " + a.BtrName) + "; " + Int("refs", b.Refs, 2);
                });
                Check(step, "copy-on-write: Apply na klonie (A 2000 → 2500) → klon nowy BTR, źródło bez zmian", () =>
                {
                    if (_s9Clone.IsNull) throw new AssertFail("brak klonu");
                    long srcBtr = ReadBS(db, _s9CloneSrc).Btr;
                    EditApply(db, _s9Clone, upd => { SetProp(upd, "LengthA", 2500.0); SetProp(upd, "TotalLength", TotalLength(upd)); }, "45");
                    var a = ReadBS(db, _s9CloneSrc); var b = ReadBS(db, _s9Clone);
                    return True(b.Btr != a.Btr, "BTR klonu " + b.BtrName + " ≠ BTR źródła " + a.BtrName) + "; " + True(a.Btr == srcBtr, "BTR źródła bez zmian")
                           + "; " + Int("refs źródła", a.Refs, 1) + "; " + Int("refs klonu", b.Refs, 1)
                           + "; " + Num("długość źródła", OutlineLen(a), 2000) + "; " + Num("długość klonu", OutlineLen(b), 2500)
                           + "; " + True(b.Anon && !b.Explodable, "BTR klonu anon=" + b.Anon + " Explodable=" + b.Explodable)
                           + "; Mark klonu=" + b.Mark;
                });
            });
        }

        // ---- S12: po _.U — stan sprzed S9 ----
        [CommandMethod("RCSMOKE_E3_S12", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void S12()
        {
            const string step = "S12";
            Run(step, db =>
            {
                if (_s9Copy.IsNull) { Add("FAIL", step, "brak kopii z S13", ""); return; }
                Check(step, "_.U po Apply: kopia A = 3000, Mark H12-41, BTR z COPY, obrys jak przed Apply", () =>
                {
                    var c = ReadBS(db, _s9Copy);
                    return Num("A", c.A, 3000) + "; " + Eq("Mark", c.Mark, "H12-41") + "; " + True(c.Btr == _s9CopyBtr, "BTR=" + c.BtrName)
                           + "; " + Int("refs", c.Refs, 1) + "; " + Pts(c.Outline, _s9OrigOutline.Select(p => p + new Vector3d(0, 10000, 0)).ToList());
                });
                Check(step, "_.U po Apply: rozkład kopii [8] = 3000, H12-41", () =>
                {
                    var dc = OneDist(db, _s9Copy);
                    string mc = Convert.ToString(dc[1].Value, Inv);
                    return Num("[8]", Convert.ToDouble(dc[8].Value, Inv), 3000) + "; " + True(mc.Contains("H12-41"), "Mark=" + mc);
                });
                Check(step, "_.U po Apply: pręty H12-45 i klon cofnięte (usunięte)", () =>
                    True(_s9CloneSrc.IsErased && _s9Clone.IsErased, "źródło usunięte=" + _s9CloneSrc.IsErased + " klon usunięty=" + _s9Clone.IsErased));
            });
        }

        // ---- S10: BarEditApply — zmiana kształtu 00 → 21 (axis[0] w miejscu, grot na obrysie) ----
        [CommandMethod("RCSMOKE_E3_S10", CommandFlags.Modal)]
        public void S10()
        {
            const string step = "S10";
            Run(step, db =>
            {
                CleanModelSpace(db);
                ObjectId bar = ObjectId.Null;
                Point3d pos0 = Point3d.Origin;
                Check(step, "Block: pręt H12-46 00 A=2000 + etykieta, Apply → 21 (500/300/500)", () =>
                {
                    var def = new Def { Code = "00", Mark = "H12-46", D = 12, A = 2000, At = new Point3d(0, 80000, 0) };
                    (bar, _) = PlaceBarWithLabel(db, "Block", def);
                    pos0 = ReadBS(db, bar).Pos;
                    EditApply(db, bar, upd =>
                    {
                        SetProp(upd, "ShapeCode", "21");
                        SetProp(upd, "LengthA", 500.0); SetProp(upd, "LengthB", 300.0); SetProp(upd, "LengthC", 500.0);
                        SetProp(upd, "TotalLength", TotalLength(upd));
                    }, "46");
                    return Describe(ReadBS(db, bar));
                });
                if (bar.IsNull) return;
                Check(step, "XData: kształt 21, A/B/C, Mark H12-46, [14] = 2", () =>
                {
                    var s = ReadBS(db, bar);
                    return Eq("kształt", s.Shape, "21") + "; " + Num("A", s.A, 500) + "; " + Num("B", s.B, 300) + "; " + Num("C", s.C, 500)
                           + "; " + Eq("Mark", s.Mark, "H12-46") + "; " + Ver2(s.Rec);
                });
                Check(step, "punkt wstawienia (axis[0]) bez zmian, skala 1, obrót 0", () =>
                {
                    var s = ReadBS(db, bar);
                    return Pt("Position", s.Pos, pos0) + "; " + True(SingleScale(s.Scale, 1, 1), "skala=" + S3(s.Scale)) + "; " + Num("Rotation", s.Rot, 0);
                });
                Check(step, "obrys = pręt 21 z PlaceBar (względem axis[0]) ±1e-6", () =>
                {
                    var s = ReadBS(db, bar);
                    var refId = PlaceAs("Block", new Def { Code = "21", Mark = "H12-46", D = 12, A = 500, B = 300, C = 500 }, new Point3d(20000, 80000, 0));
                    var r = ReadBS(db, refId);
                    return Pts(s.Outline.Select(p => Point3d.Origin + (p - s.Pos)).ToList(), r.Outline.Select(p => Point3d.Origin + (p - r.Pos)).ToList());
                });
                Check(step, "grot etykiety na nowym obrysie (±1e-6)", () => LabelTip(db, bar));
            });
        }

        // ---- S11: RC_UPDATE_BAR (UpdateBarBlock) po skali 2 i (−2, 2) w tej samej komendzie ----
        [CommandMethod("RCSMOKE_E3_S11", CommandFlags.Modal)]
        public void S11()
        {
            const string step = "S11";
            Run(step, db =>
            {
                CleanModelSpace(db);
                var cases = new[]
                {
                    (mark: "H12-47", sx: 2.0,  sy: 2.0, sz: 2.0, ex: 1.0,  ey: 1.0, y: 90000.0),
                    (mark: "H12-48", sx: -2.0, sy: 2.0, sz: 1.0, ex: -1.0, ey: 1.0, y: 92000.0),
                };
                var refId = PlaceAs("Block", new Def { Code = "00", Mark = "H12-47", D = 12, A = 2500 }, new Point3d(20000, 90000, 0));
                var r = ReadBS(db, refId);
                var refOffsets = r.Outline.Select(p => p - r.Pos).ToList();
                foreach (var cs in cases)
                {
                    var c = cs;
                    string k = c.mark + " ";
                    ObjectId bar = ObjectId.Null;
                    Point3d pos0 = Point3d.Origin;
                    Check(step, k + $"pręt-blok 00 A=2500 + etykieta + rozkład, skala ({F(c.sx)}; {F(c.sy)}; {F(c.sz)})", () =>
                    {
                        var def = new Def { Code = "00", Mark = c.mark, D = 12, A = 2500, At = new Point3d(0, c.y, 0) };
                        (bar, _) = PlaceBarWithLabel(db, "Block", def);
                        Distribute(db, bar, 40, c.y + 3040, 2540, c.y + 3500, true, 200, "B1");
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            var br = (BlockReference)tr.GetObject(bar, OpenMode.ForWrite);
                            pos0 = br.Position;
                            br.ScaleFactors = new Scale3d(c.sx, c.sy, c.sz);
                            tr.Commit();
                        }
                        var s = ReadBS(db, bar);
                        return "skala po zmianie=" + S3(s.Scale) + " długość obrysu=" + F(OutlineLen(s));
                    });
                    if (bar.IsNull) continue;
                    Check(step, k + "UpdateBarBlock: skala → (±1, ±1, 1) ze znakiem, Position bez zmian, obrys = dane", () =>
                    {
                        Call(BarCmdType, "UpdateBarBlock", Application.DocumentManager.MdiActiveDocument, bar, false);
                        var s = ReadBS(db, bar);
                        if (Math.Abs(s.Scale.X - c.ex) > GeoTol || Math.Abs(s.Scale.Y - c.ey) > GeoTol || Math.Abs(s.Scale.Z - 1) > GeoTol)
                            throw new AssertFail("skala=" + S3(s.Scale) + ", oczekiwano (" + F(c.ex) + "; " + F(c.ey) + "; 1)");
                        var exp = refOffsets.Select(v => s.Pos + new Vector3d(v.X * c.ex, v.Y * c.ey, 0)).ToList();
                        return "skala=" + S3(s.Scale) + "; " + Pt("Position", s.Pos, pos0) + "; " + Pts(s.Outline, exp)
                               + "; " + Num("A", s.A, 2500) + "; " + Int("refs", s.Refs, 1);
                    });
                    Check(step, k + "rozkład [8] = 2500, Mark bez zmian", () =>
                    {
                        var d = OneDist(db, bar);
                        return Num("[8]", Convert.ToDouble(d[8].Value, Inv), 2500) + "; " + Eq("Mark pręta", ReadBS(db, bar).Mark, c.mark);
                    });
                }
            });
        }

        // ---- S9L: BarEditApply na bloku-kopii, której [13] wskazuje etykietę oryginału → etykieta oryginału nietknięta ----
        [CommandMethod("RCSMOKE_E3_S9L", CommandFlags.Modal)]
        public void S9L()
        {
            const string step = "S9L";
            Run(step, db =>
            {
                CleanModelSpace(db);
                ObjectId orig = ObjectId.Null, origLabel = ObjectId.Null, clone = ObjectId.Null;
                Point3d tip0 = Point3d.Origin;
                string text0 = "";
                Check(step, "Block: pręt H12-56 00 A=3000 + etykieta; klon referencji (kopia bez wtyczki, [13] → etykieta oryginału)", () =>
                {
                    var def = new Def { Code = "00", Mark = "H12-56", D = 12, A = 3000, At = new Point3d(0, 110000, 0) };
                    (orig, origLabel) = PlaceBarWithLabel(db, "Block", def);
                    clone = CloneReference(db, orig, new Vector3d(0, 1000, 0));
                    tip0 = LabelTipPoint(db, origLabel);
                    text0 = LabelTextOf(db, orig);
                    var (cl, ok) = LabelOf(db, clone);
                    return True(cl == origLabel && !ok, "[13] klonu → " + cl.Handle + " back-link=" + ok) + "; grot=" + P(tip0);
                });
                if (clone.IsNull) return;
                Check(step, "Apply na klonie (A 3000 → 3500): grot i tekst etykiety oryginału bez zmian, back-link → oryginał", () =>
                {
                    EditApply(db, clone, upd => { SetProp(upd, "LengthA", 3500.0); SetProp(upd, "TotalLength", TotalLength(upd)); }, "56");
                    var (ol, ok) = LabelOf(db, orig);
                    return Pt("grot etykiety oryginału", LabelTipPoint(db, origLabel), tip0)
                           + "; " + True(ol == origLabel && ok, "etykieta oryginału=" + ol.Handle + " back-link=" + ok)
                           + "; " + Eq("tekst", LabelTextOf(db, orig), text0) + "; A klonu=" + F(ReadBS(db, clone).A);
                });
            });
        }

        // ---- S16: COPY bloku bez etykiety obok polilinii z etykietą (grot < 50 mm od bloku), obie kolejności ----
        private static readonly double[] S16Y = { 300000, 320000 };
        private static readonly ObjectId[] _s16Poly = new ObjectId[2], _s16PolyLabel = new ObjectId[2], _s16Blk = new ObjectId[2];

        [CommandMethod("RCSMOKE_E3_S16SETUP", CommandFlags.Modal)]
        public void S16Setup()
        {
            const string step = "S16SETUP";
            Run(step, db =>
            {
                CleanModelSpace(db);
                for (int i = 0; i < 2; i++)
                {
                    int k = i;
                    double y = S16Y[k];
                    Check(step, $"scenariusz {k + 1} ({(k == 0 ? "polilinia, potem blok" : "blok, potem polilinia")}): " +
                                "polilinia H12-6x z etykietą + blok bez etykiety 28 mm od grota", () =>
                    {
                        var poly = new Def { Code = "00", Mark = "H12-6" + (2 * k + 1).ToString(Inv), D = 12, A = 3000, At = new Point3d(0, y, 0) };
                        var blk  = new Def { Code = "00", Mark = "H12-6" + (2 * k + 2).ToString(Inv), D = 12, A = 2000, At = new Point3d(2000, y + 40, 0) };
                        if (k == 0)
                        {
                            (_s16Poly[k], _s16PolyLabel[k]) = PlaceBarWithLabel(db, "Polyline", poly);
                            _s16Blk[k] = PlaceAs("Block", blk, blk.At);
                        }
                        else
                        {
                            _s16Blk[k] = PlaceAs("Block", blk, blk.At);
                            (_s16Poly[k], _s16PolyLabel[k]) = PlaceBarWithLabel(db, "Polyline", poly);
                        }
                        var tip = LabelTipPoint(db, _s16PolyLabel[k]);
                        double d;
                        using (var tr = db.TransactionManager.StartTransaction())
                        {
                            d = NPt(Inst(OpenBar(tr, _s16Blk[k], KBlock), "ClosestPoint", tr, tip)).DistanceTo(tip);
                            tr.Commit();
                        }
                        if (d >= 50) throw new AssertFail("grot etykiety polilinii za daleko od bloku: " + F(d));
                        return "grot=" + P(tip) + " odległość od bloku=" + F(d);
                    });
                }
            });
        }

        [CommandMethod("RCSMOKE_E3_S16", CommandFlags.Modal)]
        public void S16()
        {
            const string step = "S16";
            Run(step, db =>
            {
                for (int i = 0; i < 2; i++)
                {
                    int k = i;
                    string pm = "H12-6" + (2 * k + 1).ToString(Inv), bm = "H12-6" + (2 * k + 2).ToString(Inv);
                    Check(step, $"scenariusz {k + 1}: kopia polilinii ma własną etykietę (back-link), kopia bloku bez etykiety, oryginały bez zmian", () =>
                    {
                        var pc = BarsInMs(db).Where(b => b.Mark == pm && b.Id != _s16Poly[k]).ToList();
                        var bc = BarsInMs(db).Where(b => b.Mark == bm && b.Id != _s16Blk[k]).ToList();
                        Int("kopie polilinii", pc.Count, 1); Int("kopie bloku", bc.Count, 1);
                        var (pl, pok) = LabelOf(db, pc[0].Id);
                        var (ol, ook) = LabelOf(db, _s16Poly[k]);
                        string blh = bc[0].LabelHandle == null ? "" : Convert.ToString(bc[0].LabelHandle, Inv);
                        return True(!pl.IsNull && pok && pl != _s16PolyLabel[k], "etykieta kopii polilinii=" + pl.Handle + " back-link=" + pok)
                               + "; " + True(string.IsNullOrEmpty(blh) || blh == "0", "[13] kopii bloku=" + blh)
                               + "; " + True(ol == _s16PolyLabel[k] && ook, "etykieta oryginału polilinii=" + ol.Handle + " back-link=" + ook);
                    });
                }
            });
        }

        // ---- S17: dwa pokrywające się pręty-bloki A, B; COPY obu prętów + tylko etykiety B (obie kolejności) ----
        private static readonly double[] S17Y = { 340000, 360000 };
        private static readonly ObjectId[] _s17A = new ObjectId[2], _s17ALabel = new ObjectId[2], _s17B = new ObjectId[2], _s17BLabel = new ObjectId[2];

        [CommandMethod("RCSMOKE_E3_S17SETUP", CommandFlags.Modal)]
        public void S17Setup()
        {
            const string step = "S17SETUP";
            Run(step, db =>
            {
                CleanModelSpace(db);
                for (int i = 0; i < 2; i++)
                {
                    int k = i;
                    double y = S17Y[k];
                    Check(step, $"scenariusz {k + 1} ({(k == 0 ? "A, potem B" : "B, potem A")}): A i B (bloki 00 A=3000) w tym samym miejscu; " +
                                "etykieta A przy x≈500 (poza oknem COPY), etykieta B przy x≈2950", () =>
                    {
                        var a = new Def { Code = "00", Mark = "H12-7" + (2 * k + 1).ToString(Inv), D = 12, A = 3000, At = new Point3d(0, y, 0) };
                        var b = new Def { Code = "00", Mark = "H12-7" + (2 * k + 2).ToString(Inv), D = 12, A = 3000, At = new Point3d(0, y, 0) };
                        if (k == 0)
                        {
                            (_s17A[k], _s17ALabel[k]) = PlaceBarWithLabelAt(db, a, new Point3d(500, y - 1500, 0));
                            (_s17B[k], _s17BLabel[k]) = PlaceBarWithLabel(db, "Block", b);
                        }
                        else
                        {
                            (_s17B[k], _s17BLabel[k]) = PlaceBarWithLabel(db, "Block", b);
                            (_s17A[k], _s17ALabel[k]) = PlaceBarWithLabelAt(db, a, new Point3d(500, y - 1500, 0));
                        }
                        return "A=" + _s17A[k].Handle + " etykieta A grot=" + P(LabelTipPoint(db, _s17ALabel[k]))
                               + "; B=" + _s17B[k].Handle + " etykieta B grot=" + P(LabelTipPoint(db, _s17BLabel[k]));
                    });
                }
            });
        }

        [CommandMethod("RCSMOKE_E3_S17", CommandFlags.Modal)]
        public void S17()
        {
            const string step = "S17";
            Run(step, db =>
            {
                for (int i = 0; i < 2; i++)
                {
                    int k = i;
                    string am = "H12-7" + (2 * k + 1).ToString(Inv), bm = "H12-7" + (2 * k + 2).ToString(Inv);
                    Check(step, $"scenariusz {k + 1}: kopia B ma kopię etykiety B (back-link), kopia A ma puste [13], oryginały bez zmian", () =>
                    {
                        var ac = BarsInMs(db).Where(x => x.Mark == am && x.Id != _s17A[k]).ToList();
                        var bc = BarsInMs(db).Where(x => x.Mark == bm && x.Id != _s17B[k]).ToList();
                        Int("kopie A", ac.Count, 1); Int("kopie B", bc.Count, 1);
                        Int("etykiety (2 scenariusze × A, B, kopia B)", CountApp<MLeader>(db, LabelApp), 6);
                        var (bl, bok) = LabelOf(db, bc[0].Id);
                        string alh = ac[0].LabelHandle == null ? "" : Convert.ToString(ac[0].LabelHandle, Inv);
                        var (oal, oaok) = LabelOf(db, _s17A[k]);
                        var (obl, obok) = LabelOf(db, _s17B[k]);
                        return True(!bl.IsNull && bok && bl != _s17BLabel[k], "etykieta kopii B=" + bl.Handle + " back-link=" + bok)
                               + "; " + True(string.IsNullOrEmpty(alh) || alh == "0", "[13] kopii A=" + alh)
                               + "; " + True(oal == _s17ALabel[k] && oaok, "etykieta A=" + oal.Handle + " back-link=" + oaok)
                               + "; " + True(obl == _s17BLabel[k] && obok, "etykieta B=" + obl.Handle + " back-link=" + obok);
                    });
                }
            });
        }

        /// <summary>Pręt-blok + etykieta z tekstem w textPt (grot = najbliższy punkt obrysu) + LabelHandle w XData.</summary>
        private static (ObjectId bar, ObjectId label) PlaceBarWithLabelAt(Database db, Def def, Point3d textPt)
        {
            var barId = PlaceAs("Block", def, def.At);
            Point3d arrowTip;
            object bar;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                bar = Call(SingleBarType, "ReadBarXData", (Entity)tr.GetObject(barId, OpenMode.ForRead));
                arrowTip = (Point3d)Call(SingleBarType, "GetBarArrowTip", barId, bar, textPt, tr);
                tr.Commit();
            }
            var labelId = (ObjectId)Call(SingleBarType, "PlaceBarLabel", db, arrowTip, textPt, def.Mark, barId);
            if (labelId.IsNull) throw new AssertFail("PlaceBarLabel: brak etykiety");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = (Entity)tr.GetObject(barId, OpenMode.ForWrite);
                SetProp(bar, "LabelHandle", labelId.Handle.ToString());
                Call(SingleBarType, "WriteXData", ent, bar);
                tr.Commit();
            }
            return (barId, labelId);
        }

        // ---- S14SETUP / S14: _.COPY samego pręta (bez etykiety) ----
        [CommandMethod("RCSMOKE_E3_S14SETUP", CommandFlags.Modal)]
        public void S14Setup()
        {
            const string step = "S14SETUP";
            Run(step, db =>
            {
                CleanModelSpace(db);
                _s14Orig = _s14OrigLabel = ObjectId.Null;
                Check(step, "Block: pręt H12-42 00 A=3000 w (0,100000) + etykieta", () =>
                {
                    var def = new Def { Code = "00", Mark = "H12-42", D = 12, A = 3000, At = new Point3d(0, 100000, 0) };
                    (_s14Orig, _s14OrigLabel) = PlaceBarWithLabel(db, "Block", def);
                    return Describe(ReadBS(db, _s14Orig)) + " grot etykiety=" + P(LabelTipPoint(db, _s14OrigLabel));
                });
            });
        }

        [CommandMethod("RCSMOKE_E3_S14", CommandFlags.Modal)]
        public void S14()
        {
            const string step = "S14";
            Run(step, db =>
            {
                if (_s14Orig.IsNull) { Add("FAIL", step, "brak S14SETUP", ""); return; }
                ObjectId copy = ObjectId.Null;
                Check(step, "kopia pręta bez etykiety (pręt-blok v2, przesunięta o (0,2000)), etykiet nadal 1", () =>
                {
                    var copies = BarsInMs(db).Where(b => b.Mark == "H12-42" && b.Id != _s14Orig).ToList();
                    Int("kopie", copies.Count, 1);
                    copy = copies[0].Id;
                    var c = ReadBS(db, copy); var o = ReadBS(db, _s14Orig);
                    return Pts(c.Outline, o.Outline.Select(p => p + new Vector3d(0, 2000, 0)).ToList())
                           + "; " + Int("etykiety", CountApp<MLeader>(db, LabelApp), 1);
                });
                if (copy.IsNull) return;
                Check(step, "kopia: [13] LabelHandle puste; oryginał: etykieta z back-linkiem", () =>
                {
                    var c = ReadBS(db, copy);
                    string lh = c.LabelHandle == null ? "" : Convert.ToString(c.LabelHandle, Inv);
                    var (ol, ok) = LabelOf(db, _s14Orig);
                    return True(string.IsNullOrEmpty(lh) || lh == "0", "[13] kopii=" + lh)
                           + "; " + True(ol == _s14OrigLabel && ok, "etykieta oryginału=" + ol.Handle + " back-link=" + ok);
                });
                Check(step, "kopia: własny BTR *U, Explodable = false, refs = 1; [14] = 2", () =>
                {
                    var c = ReadBS(db, copy); var o = ReadBS(db, _s14Orig);
                    return True(c.Btr != o.Btr && c.Anon && !c.Explodable, "BTR kopii=" + c.BtrName + " oryginału=" + o.BtrName + " Explodable=" + c.Explodable)
                           + "; " + Int("refs kopii", c.Refs, 1) + "; " + Int("refs oryginału", o.Refs, 1) + "; " + Ver2(c.Rec);
                });
            });
        }

        // ---- S15SETUP / S15: _.MIRROR pręta 21 z etykietą (bez usuwania źródła) ----
        [CommandMethod("RCSMOKE_E3_S15SETUP", CommandFlags.Modal)]
        public void S15Setup()
        {
            const string step = "S15SETUP";
            Run(step, db =>
            {
                CleanModelSpace(db);
                _s15Orig = _s15OrigLabel = ObjectId.Null;
                Check(step, "Block: pręt H10-43 21 (500/300/500) w (0,150000) + etykieta", () =>
                {
                    var def = new Def { Code = "21", Mark = "H10-43", D = 10, A = 500, B = 300, C = 500, At = new Point3d(0, 150000, 0) };
                    (_s15Orig, _s15OrigLabel) = PlaceBarWithLabel(db, "Block", def);
                    var s = ReadBS(db, _s15Orig);
                    _s15OrigOutline = s.Outline;
                    return Describe(s);
                });
            });
        }

        [CommandMethod("RCSMOKE_E3_S15", CommandFlags.Modal)]
        public void S15()
        {
            const string step = "S15";
            Run(step, db =>
            {
                if (_s15Orig.IsNull) { Add("FAIL", step, "brak S15SETUP", ""); return; }
                ObjectId m = ObjectId.Null;
                Check(step, "odbicie pręta H10-43 względem x = 5000: obrys = odbity obrys źródła ±1e-6", () =>
                {
                    var ms = BarsInMs(db).Where(b => b.Mark == "H10-43" && b.Id != _s15Orig).ToList();
                    Int("odbicia", ms.Count, 1);
                    m = ms[0].Id;
                    var s = ReadBS(db, m);
                    return Pts(s.Outline, _s15OrigOutline.Select(p => new Point3d(10000 - p.X, p.Y, 0)).ToList())
                           + "; skala=" + S3(s.Scale) + " obrót=" + F(s.Rot * 180 / Math.PI) + "°";
                });
                if (m.IsNull) return;
                Check(step, "odbicie: |skala| = 1 (3e2 bez zmian), własny BTR *U, Explodable = false, refs = 1, [14] = 2", () =>
                {
                    var s = ReadBS(db, m); var o = ReadBS(db, _s15Orig);
                    return True(Math.Abs(Math.Abs(s.Scale.X) - 1) < GeoTol && Math.Abs(Math.Abs(s.Scale.Y) - 1) < GeoTol, "skala=" + S3(s.Scale))
                           + "; " + True(s.Btr != o.Btr && s.Anon && !s.Explodable, "BTR=" + s.BtrName + " Explodable=" + s.Explodable)
                           + "; " + Int("refs", s.Refs, 1) + "; " + Int("refs źródła", o.Refs, 1) + "; " + Ver2(s.Rec);
                });
                Check(step, "odbicie: XData [0]–[12] = źródło; etykieta odbicia z back-linkiem; źródło bez zmian", () =>
                {
                    var (ml, ok) = LabelOf(db, m);
                    var (ol, ook) = LabelOf(db, _s15Orig);
                    return SameHead(db, m, _s15Orig, 13) + "; " + True(!ml.IsNull && ok && ml != ol, "etykieta odbicia=" + ml.Handle + " back-link=" + ok)
                           + "; " + True(ol == _s15OrigLabel && ook, "etykieta źródła=" + ol.Handle)
                           + "; " + Pts(ReadBS(db, _s15Orig).Outline, _s15OrigOutline);
                });
            });
        }

        // ---- 3e2: _.SCALE 2 → od razu skala 1 i obrys z danych (bez RC_UPDATE_BAR); _.UNDO 1 / _.REDO spójne ----
        [CommandMethod("RCSMOKE_E3_SCSETUP", CommandFlags.Modal)]
        public void ScSetup()
        {
            const string step = "SCSETUP";
            Run(step, db =>
            {
                CleanModelSpace(db);
                _scBar = ObjectId.Null;
                Check(step, "Block: pręt H12-44 00 A=3000 w (0,200000) + etykieta", () =>
                {
                    var def = new Def { Code = "00", Mark = "H12-44", D = 12, A = 3000, At = new Point3d(0, ScY, 0) };
                    (_scBar, _) = PlaceBarWithLabel(db, "Block", def);
                    var s = ReadBS(db, _scBar);
                    _scOffsets = s.Outline.Select(p => p - s.Pos).ToList();
                    return Describe(s);
                });
            });
        }

        [CommandMethod("RCSMOKE_E3_SC", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void Sc() => ScVerify("SC", ScBaseX + 2 * (0 - ScBaseX), "po _.SCALE 2 (baza 1000,200000)");

        [CommandMethod("RCSMOKE_E3_SCU", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void ScU() => ScVerify("SCU", 0, "po _.UNDO 1 (stan sprzed SCALE)");

        [CommandMethod("RCSMOKE_E3_SCR", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
        public void ScR() => ScVerify("SCR", ScBaseX + 2 * (0 - ScBaseX), "po _.REDO (stan naprawiony)");

        private static void ScVerify(string step, double expX, string when)
        {
            Run(step, db =>
            {
                if (_scBar.IsNull || _scBar.IsErased) { Add("FAIL", step, "brak pręta z SCSETUP", ""); return; }
                Check(step, when + ": skala (1; 1; 1), Position = (" + F(expX) + ", 200000), obrys = dane (A = 3000)", () =>
                {
                    var s = ReadBS(db, _scBar);
                    if (!SingleScale(s.Scale, 1, 1)) throw new AssertFail("skala=" + S3(s.Scale));
                    return "skala=" + S3(s.Scale) + "; " + Pt("Position", s.Pos, new Point3d(expX, ScY, 0))
                           + "; " + Pts(s.Outline, _scOffsets.Select(v => s.Pos + v).ToList())
                           + "; " + Num("A", s.A, 3000) + "; " + Int("refs", s.Refs, 1) + "; " + Ver2(s.Rec);
                });
            });
        }

        // ---- pomocnicze 3d/3e ----

        private sealed class BS
        {
            public ObjectId Id;
            public string Mark, Shape, BtrName;
            public double A, B, C, Rot;
            public Point3d Pos;
            public Scale3d Scale;
            public long Btr;
            public bool Anon, Explodable;
            public int Refs;
            public List<Point3d> Outline;
            public object LabelHandle;
            public TypedValue[] Rec;
        }

        private static BS ReadBS(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (id.IsNull || id.IsErased) throw new AssertFail("pręt " + id.Handle + " nie istnieje");
                if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) throw new AssertFail("pręt " + id.Handle + " nie jest BlockReference");
                var rec = ReadRecord(br);
                if (rec.Length < HeadLen) throw new AssertFail("pręt " + id.Handle + ": rekord RC_SINGLE_BAR " + rec.Length);
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                var s = new BS
                {
                    Id = id,
                    Mark = Convert.ToString(rec[1].Value, Inv),
                    Shape = Convert.ToString(rec[3].Value, Inv),
                    A = Convert.ToDouble(rec[4].Value, Inv),
                    B = Convert.ToDouble(rec[5].Value, Inv),
                    C = Convert.ToDouble(rec[6].Value, Inv),
                    LabelHandle = rec[13].Value,
                    Rec = rec,
                    Pos = br.Position,
                    Scale = br.ScaleFactors,
                    Rot = br.Rotation,
                    Btr = btr.Handle.Value,
                    BtrName = btr.Name,
                    Anon = btr.IsAnonymous,
                    Explodable = btr.Explodable,
                    Refs = btr.GetBlockReferenceIds(true, true).Count,
                    Outline = Outline(OpenBar(tr, id, KBlock), tr),
                };
                tr.Commit();
                return s;
            }
        }

        private static string Describe(BS s)
            => $"{s.Mark} {s.Shape} A={F(s.A)} uchwyt={s.Id.Handle} BTR={s.BtrName} refs={s.Refs.ToString(Inv)} Position={P(s.Pos)} skala={S3(s.Scale)}";

        private static string S3(Scale3d s) => "(" + F(s.X) + "; " + F(s.Y) + "; " + F(s.Z) + ")";

        private static bool SingleScale(Scale3d s, double x, double y)
            => Math.Abs(s.X - x) < GeoTol && Math.Abs(s.Y - y) < GeoTol && Math.Abs(s.Z - 1) < GeoTol;

        /// <summary>Długość prostego obrysu (00): rozpiętość wierzchołków wzdłuż osi X bloku (w WCS bez obrotu).</summary>
        private static double OutlineLen(BS s) => s.Outline.Max(p => p.X) - s.Outline.Min(p => p.X);

        private static int PosNr(string mark)
        {
            var parts = (mark ?? "").Split('-');
            return parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, Inv, out int n) ? n : 0;
        }

        private static string Num(string what, double act, double exp)
        {
            if (Math.Abs(act - exp) > 1e-6) throw new AssertFail($"{what}: jest {F(act)}, oczekiwano {F(exp)}");
            return what + "=" + F(act);
        }

        private static double BarA(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                return Convert.ToDouble(ReadRecord((Entity)tr.GetObject(id, OpenMode.ForRead))[4].Value, Inv);
        }

        /// <summary>Pola [0]..[n−1] rekordu RC_SINGLE_BAR równe (typ i wartość) + [14] = Int16 2, rekord 15.</summary>
        private static string SameHead(Database db, ObjectId a, ObjectId b, int n)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ra = ReadRecord((Entity)tr.GetObject(a, OpenMode.ForRead));
                var rb = ReadRecord((Entity)tr.GetObject(b, OpenMode.ForRead));
                for (int i = 0; i < n; i++) SameField(i, rb[i], ra[i]);
                return Int("długość rekordu", ra.Length, PlacedBlockLen) + "; " + Ver2(ra);
            }
        }

        /// <summary>Zawartość BTR pręta-bloku: bez XData, warstwa 0, kolor / rodzaj linii / grubość ByBlock.</summary>
        private static string BtrContentsOk(Database db, ObjectId barId)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var br = (BlockReference)tr.GetObject(barId, OpenMode.ForRead);
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                int n = 0;
                foreach (ObjectId eid in btr)
                {
                    if (eid.IsErased) continue;
                    var e = (Entity)tr.GetObject(eid, OpenMode.ForRead);
                    n++;
                    using (var xd = e.XData) if (xd != null) throw new AssertFail("encja BTR z XData: " + e.GetType().Name);
                    if (e.Layer != "0" || e.ColorIndex != 0 || e.LineWeight != LineWeight.ByBlock
                        || !string.Equals(e.Linetype, "ByBlock", StringComparison.OrdinalIgnoreCase))
                        throw new AssertFail($"encja BTR: L={e.Layer} C={e.ColorIndex} LT={e.Linetype} LW={e.LineWeight}");
                }
                if (n == 0) throw new AssertFail("pusty BTR");
                return "encje=" + n.ToString(Inv);
            }
        }

        /// <summary>Rozkłady RC_BAR_BLOCK z [18] = pręt (kolejność MS).</summary>
        private static List<(ObjectId id, TypedValue[] rec)> DistsOf(Database db, ObjectId barId)
        {
            var res = new List<(ObjectId id, TypedValue[] rec)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    var rec = ReadRecord(br, DistApp);
                    if (rec.Length > 18 && SameHandle(rec[18].Value, barId.Handle)) res.Add((id, rec));
                }
            }
            return res;
        }

        /// <summary>Jedyny rozkład pręta ([18]) — inaczej AssertFail z listą rozkładów.</summary>
        private static TypedValue[] OneDist(Database db, ObjectId barId)
        {
            var l = DistsOf(db, barId);
            if (l.Count != 1) throw new AssertFail($"rozkłady pręta {barId.Handle}: {l.Count}; {DistDump(db)}");
            return l[0].rec;
        }

        /// <summary>Diagnostyka: wszystkie rozkłady (uchwyt, Mark, [18], [11]) w kolejności MS.</summary>
        private static string DistDump(Database db)
        {
            var sb = new StringBuilder();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    var rec = ReadRecord(br, DistApp);
                    if (rec.Length == 0) continue;
                    sb.Append("rozkład ").Append(id.Handle).Append(" Mark=").Append(Convert.ToString(rec[1].Value, Inv))
                      .Append(" [18]=").Append(rec.Length > 18 ? Desc(rec[18]) : "brak")
                      .Append(" [11]=").Append(rec.Length > 11 ? Desc(rec[11]) : "brak").Append("; ");
                }
            }
            return sb.ToString();
        }

        /// <summary>Opis rozkładu: [11] rozkładu → opis, [16] opisu → ten rozkład.</summary>
        private static string AnnotPair(Database db, ObjectId distId)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var rec = ReadRecord((Entity)tr.GetObject(distId, OpenMode.ForRead), DistApp);
                var annot = rec.Length > 11 ? Resolve(db, rec[11].Value) : ObjectId.Null;
                if (annot.IsNull || annot.IsErased) throw new AssertFail("rozkład " + distId.Handle + ": brak opisu");
                var arec = ReadRecord((Entity)tr.GetObject(annot, OpenMode.ForRead), AnnotApp);
                if (arec.Length <= 16 || !SameHandle(arec[16].Value, distId.Handle))
                    throw new AssertFail("opis " + annot.Handle + " nie wskazuje na rozkład " + distId.Handle);
                return "rozkład " + distId.Handle + " ↔ opis " + annot.Handle;
            }
        }

        /// <summary>Etykieta pręta ([13]) i czy wskazuje z powrotem na pręt (back-link).</summary>
        private static (ObjectId label, bool backLink) LabelOf(Database db, ObjectId barId)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var rec = ReadRecord((Entity)tr.GetObject(barId, OpenMode.ForRead));
                var lid = Resolve(db, rec[13].Value);
                if (lid.IsNull || lid.IsErased || !(tr.GetObject(lid, OpenMode.ForRead) is MLeader ml)) return (ObjectId.Null, false);
                var lrec = ReadRecord(ml, LabelApp);
                return (lid, lrec.Length >= 2 && SameHandle(lrec[1].Value, barId.Handle));
            }
        }

        private static Point3d LabelTipPoint(Database db, ObjectId labelId)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                return ((MLeader)tr.GetObject(labelId, OpenMode.ForRead)).GetFirstVertex(0);
        }

        private static string LabelTextOf(Database db, ObjectId barId)
        {
            var (lid, _) = LabelOf(db, barId);
            if (lid.IsNull) return "";
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                return ((MLeader)tr.GetObject(lid, OpenMode.ForRead)).MText?.Contents ?? "";
        }

        /// <summary>Grot etykiety pręta leży na obrysie pręta-bloku (±1e-6).</summary>
        private static string LabelTip(Database db, ObjectId barId)
        {
            var (lid, ok) = LabelOf(db, barId);
            if (lid.IsNull || !ok) throw new AssertFail("brak etykiety z back-linkiem");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var tip = ((MLeader)tr.GetObject(lid, OpenMode.ForRead)).GetFirstVertex(0);
                var cp = NPt(Inst(OpenBar(tr, barId, KBlock), "ClosestPoint", tr, tip));
                double d = cp.DistanceTo(tip);
                tr.Commit();
                if (d > GeoTol) throw new AssertFail($"grot {P(tip)} odległy od obrysu o {d.ToString("E2", Inv)}");
                return "grot=" + P(tip) + " odległość=" + d.ToString("E2", Inv);
            }
        }

        /// <summary>BarEditApply.Apply (RC_EDIT_BAR po dialogu): dane z XData, zmiany w edit (jak wynik dialogu), numer z dialogu.</summary>
        private static void EditApply(Database db, ObjectId barId, Action<object> edit, string posNrStr)
        {
            object old, upd;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ent = (Entity)tr.GetObject(barId, OpenMode.ForRead);
                old = Call(SingleBarType, "ReadBarXData", ent);
                upd = Call(SingleBarType, "ReadBarXData", ent);
            }
            edit(upd);
            var ok = Call(EditApplyType, "Apply", db, null, barId, old, upd, posNrStr);
            if (!(ok is bool b) || !b) throw new AssertFail("BarEditApply.Apply zwrócił " + Show(ok));
        }

        /// <summary>Druga referencja tego samego BTR (bez komendy COPY), z kopią XData, przesunięta o off.</summary>
        private static ObjectId CloneReference(Database db, ObjectId srcId, Vector3d off)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var src = (BlockReference)tr.GetObject(srcId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var br = new BlockReference(src.Position + off, src.BlockTableRecord)
                {
                    Rotation = src.Rotation,
                    ScaleFactors = src.ScaleFactors,
                    Layer = src.Layer,
                    ColorIndex = src.ColorIndex,
                    LineWeight = src.LineWeight,
                };
                var id = ms.AppendEntity(br);
                tr.AddNewlyCreatedDBObject(br, true);
                using (var xd = src.XData) br.XData = xd;
                tr.Commit();
                return id;
            }
        }

        /// <summary>RC_BAR bez dialogu i jiga (jak tools/RegressionDump): PlaceBar + etykieta + LabelHandle w XData.</summary>
        private static (ObjectId bar, ObjectId label) PlaceBarWithLabel(Database db, string fmt, Def def)
        {
            var barId = PlaceAs(fmt, def, def.At);
            Point3d textPt, arrowTip;
            object bar;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var center = NPt(Inst(OpenBar(tr, barId, KPolyline | KBlock), "MidPoint", tr));
                textPt = new Point3d(center.X - 50, center.Y + 150, 0);
                bar = Call(SingleBarType, "ReadBarXData", (Entity)tr.GetObject(barId, OpenMode.ForRead));
                arrowTip = (Point3d)Call(SingleBarType, "GetBarArrowTip", barId, bar, textPt, tr);
                tr.Commit();
            }
            var labelId = (ObjectId)Call(SingleBarType, "PlaceBarLabel", db, arrowTip, textPt, def.Mark, barId);
            if (labelId.IsNull) throw new AssertFail("PlaceBarLabel: brak etykiety");
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ent = (Entity)tr.GetObject(barId, OpenMode.ForWrite);
                SetProp(bar, "LabelHandle", labelId.Handle.ToString());
                Call(SingleBarType, "WriteXData", ent, bar);
                tr.Commit();
            }
            return (barId, labelId);
        }

        /// <summary>RC_DISTRIBUTION bez dialogów (jak tools/RegressionDump): rozkład + opis + powiązania + liczba na etykiecie.</summary>
        private static (ObjectId dist, ObjectId annot) Distribute(Database db, ObjectId barId, double x0, double y0, double x1, double y1,
                                                                 bool horizontal, double spacing, string suffix)
        {
            object src;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                src = Call(SingleBarType, "ReadBarXData", (Entity)tr.GetObject(barId, OpenMode.ForRead));
                tr.Commit();
            }
            string srcHandle = barId.Handle.Value.ToString("X8");
            SetProp(src, "SourceBarHandle", srcHandle);
            SetProp(src, "Angle", 0.0);
            double rawSpan = horizontal ? (y1 - y0) : (x1 - x0);
            int autoCount = Math.Max(1, (int)(rawSpan / spacing) + 1);
            int posNr = (int)Call(SingleBarType, "ExtractPosNr", (string)Prop(src, "Mark"));
            SetProp(src, "Direction", horizontal ? "X" : "Y");
            SetProp(src, "Cover", 40.0);
            SetProp(src, "Pt1X", x0);
            SetProp(src, "Pt1Y", y0);
            SetProp(src, "BarsSpan", rawSpan);
            SetProp(src, "Spacing", spacing);
            SetProp(src, "Flipped", false);

            var res = CallD("BricsCadRc.Core.BarBlockEngine", "GenerateFromBounds", db, x0, y0, x1, y1, src, horizontal, posNr);
            var distId = (ObjectId)Prop(res, "BlockRefId");
            if (distId.IsNull) throw new AssertFail("GenerateFromBounds: brak rozkładu");
            string mark = (string)Call(BarDataType, "FormatMark", (int)Prop(src, "Diameter"), posNr, spacing, autoCount) + " " + suffix;
            SetProp(src, "Mark", mark);
            SetProp(src, "CountDisplay", null);
            WriteDistMark(db, distId, mark, true);
            var ann = CallD("BricsCadRc.Core.AnnotationEngine", "CreateLeader", db, res, src, false, posNr, null, horizontal, true, true);
            var annId = (ObjectId)Prop(ann, "BlockRefId");
            if (annId.IsNull) throw new AssertFail("CreateLeader: brak opisu");
            CallD("BricsCadRc.Core.BarBlockEngine", "LinkAnnotation", db, distId, annId);
            CallD("BricsCadRc.Core.AnnotationEngine", "UpdateBarLabelCount", db, srcHandle, mark);
            WriteDistMark(db, distId, mark, false);
            CallD("BricsCadRc.Core.AnnotationEngine", "UpdateBarLabelCount", db, srcHandle, mark);
            return (distId, annId);
        }

        private static void WriteDistMark(Database db, ObjectId distId, string mark, bool withCountDisplay)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = (BlockReference)tr.GetObject(distId, OpenMode.ForWrite);
                var xd = CallD("BricsCadRc.Core.BarBlockEngine", "ReadXData", br);
                if (xd != null)
                {
                    SetProp(xd, "Mark", mark);
                    if (withCountDisplay) SetProp(xd, "CountDisplay", null);
                    CallD("BricsCadRc.Core.BarBlockEngine", "WriteXData", br, xd);
                }
                tr.Commit();
            }
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
                         }).ToList();
            if (cands.Count != 1)
                throw new InvalidOperationException($"{typeName}.{method}: pasujących przeciążeń {cands.Count}");
            var pars = cands[0].GetParameters();
            var full = new object[pars.Length];
            for (int i = 0; i < pars.Length; i++) full[i] = i < args.Length ? args[i] : pars[i].DefaultValue;
            return Invoke(cands[0], null, full);
        }

        [CommandMethod("RCSMOKE_E3_REPORT", CommandFlags.Modal | CommandFlags.NoUndoMarker)]
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
        // Pomocnicze — geometria i rekordy
        // ================================================================

        // ---- Etap 3b/3c: scenariusz (płyta, strefy, pręty, komplety) ----

        private const double SlabW = 12000, SlabH = 8000, TopDy = -20000;   // jak tools/RegressionDump
        private const double HoleX0 = 5000, HoleY0 = 3000, HoleX1 = 6000, HoleY1 = 4000;

        private sealed class BarInfo
        {
            public ObjectId Id;
            public string Type, Mark, Shape;
            public int D;
            public double A;
            public object LabelHandle;
        }

        private static void CleanModelSpace(Database db)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;   // także encje usunięte kaskadą pręta
                    tr.GetObject(id, OpenMode.ForWrite).Erase();
                }
                tr.Commit();
            }
        }

        private static ObjectId AddRect(Database db, double x0, double y0, double x1, double y1, string layer)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureLayer(tr, db, layer, 7);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var pl = new Polyline(4);
                pl.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
                pl.Closed = true;
                pl.Layer = layer;
                var id = ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                tr.Commit();
                return id;
            }
        }

        /// <summary>Obrysy płyty: rzut dolny (0,0) i górny (przesunięty o TopDy); opcjonalnie otwór z krzyżykiem (jak S-REG).</summary>
        private static (ObjectId bottom, ObjectId top) AddSlabs(Database db, bool withHole)
        {
            var bottom = AddRect(db, 0, 0, SlabW, SlabH, SlabLayer);
            var top    = AddRect(db, 0, TopDy, SlabW, SlabH + TopDy, SlabLayer);
            if (withHole)
            {
                foreach (double dy in new[] { 0.0, TopDy })
                {
                    AddRect(db, HoleX0, HoleY0 + dy, HoleX1, HoleY1 + dy, SlabLayer);
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                        foreach (var (a, b) in new[]
                        {
                            (new Point3d(HoleX0, HoleY0 + dy, 0), new Point3d(HoleX1, HoleY1 + dy, 0)),
                            (new Point3d(HoleX0, HoleY1 + dy, 0), new Point3d(HoleX1, HoleY0 + dy, 0)),
                        })
                        {
                            var ln = new Line(a, b) { Layer = SlabLayer };
                            ms.AppendEntity(ln);
                            tr.AddNewlyCreatedDBObject(ln, true);
                        }
                        tr.Commit();
                    }
                }
            }
            return (bottom, top);
        }

        /// <summary>Strefa szablonów (prostokąt na warstwie layer) o wymiarach jak automatyczna strefa AutoRebar.</summary>
        private static Extents3d AddZone(Database db, string layer, double x0, double yTop)
        {
            double w = ConstD("TemplateZoneWidth"), h = ConstD("TemplateZoneHeight");
            AddRect(db, x0, yTop - h, x0 + w, yTop, layer);
            return new Extents3d(new Point3d(x0, yTop - h, 0), new Point3d(x0 + w, yTop, 0));
        }

        /// <summary>Punkt wstawienia k-tego szablonu prostego w strefie (jak CreateNewTemplate).</summary>
        private static Point3d Slot(Extents3d zone, int k)
            => new Point3d(zone.MinPoint.X + ConstD("TemplateOffsetX"),
                           zone.MaxPoint.Y - ConstD("TemplateOffsetY") - ConstD("TemplateSpacingY") * k, 0);

        private static bool Inside(Point3d p, Extents3d z)
            => p.X >= z.MinPoint.X && p.X <= z.MaxPoint.X && p.Y >= z.MinPoint.Y && p.Y <= z.MaxPoint.Y;

        private static double ConstD(string name)
        {
            var fi = Plugin.GetType(AutoRebarType, true).GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi == null) throw new InvalidOperationException("AutoRebarEngine: brak stałej " + name);
            return Convert.ToDouble(fi.GetValue(null), Inv);
        }

        /// <summary>PlaceBar w danym formacie; format zawsze przywracany do Polyline.</summary>
        private static ObjectId PlaceAs(string fmt, Def def, Point3d at)
        {
            SetFormat(fmt);
            try { return (ObjectId)Call(SingleBarType, "PlaceBar", ActiveDb(), NewBar(def), at); }
            finally { SetFormat("Polyline"); }
        }

        private static string EntType(Database db, ObjectId id)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                return tr.GetObject(id, OpenMode.ForRead).GetType().Name + " " + id.Handle;
        }

        /// <summary>AutoRebarEngine.ScanTemplates / ScanUBTemplates (prywatne) → lista (id, BarData).</summary>
        private static List<(ObjectId id, object bar)> ScanVia(string method, Database db, Extents3d zone, int dia)
        {
            var res = new List<(ObjectId id, object bar)>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var t in (IEnumerable)Call(AutoRebarType, method, db, tr, zone, dia))
                    res.Add(((ObjectId)Prop(t, "Item1"), Prop(t, "Item2")));
                tr.Commit();
            }
            return res;
        }

        private static string Sig(object bar)
            => Convert.ToString(Prop(bar, "Mark"), Inv) + "|" + Convert.ToString(Prop(bar, "ShapeCode"), Inv)
               + "|D=" + Convert.ToString(Prop(bar, "Diameter"), Inv)
               + "|A=" + F((double)Prop(bar, "LengthA")) + "|B=" + F((double)Prop(bar, "LengthB"))
               + "|C=" + F((double)Prop(bar, "LengthC")) + "|L=" + F((double)Prop(bar, "TotalLength"));

        private static void PlaceSummary(Document doc, params (int posNr, int realCount, Point3d insertPt)[] items)
        {
            var list = new List<(int posNr, int realCount, Point3d insertPt)>(items);
            Call(PunchCmdType, "PlaceSummaryBars", doc, list, Prefix);
        }

        /// <summary>Pręty RC_SINGLE_BAR w przestrzeni modelu (odczyt rekordu bez wtyczki), kolejność MS.</summary>
        private static List<BarInfo> BarsInMs(Database db)
        {
            var res = new List<BarInfo>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    var r = ReadRecord(e);
                    if (r.Length < HeadLen) continue;
                    res.Add(new BarInfo
                    {
                        Id = id,
                        Type = e.GetType().Name,
                        Mark = Convert.ToString(r[1].Value, Inv),
                        D = Convert.ToInt32(r[2].Value, Inv),
                        Shape = Convert.ToString(r[3].Value, Inv),
                        A = Convert.ToDouble(r[4].Value, Inv),
                        LabelHandle = r[13].Value,
                    });
                }
            }
            return res;
        }

        private static List<int> UsedNumbers(Database db)
            => ((IEnumerable)Call(PosCounterType, "GetUsedPositionNumbers", db)).Cast<int>().OrderBy(n => n).ToList();

        /// <summary>Liczba encji typu T (MLeader / BlockReference) z XData app w przestrzeni modelu.</summary>
        private static int CountApp<T>(Database db, string app) where T : Entity
        {
            int n = 0;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                    if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is T e && ReadRecord(e, app).Length > 0) n++;
            }
            return n;
        }

        private static ObjectId Resolve(Database db, object handleValue)
        {
            long h;
            if (handleValue is Handle hv) h = hv.Value;
            else if (handleValue == null || !long.TryParse(handleValue.ToString(), NumberStyles.HexNumber, Inv, out h)) return ObjectId.Null;
            return db.TryGetObjectId(new Handle(h), out var id) ? id : ObjectId.Null;
        }

        private static string RunAutoRebar(Document doc, ObjectId bottomId, ObjectId topId)
        {
            var r = new List<string>();
            void G(string name, object v)
            {
                int n = Convert.ToInt32(v, Inv);
                r.Add(name + "=" + n.ToString(Inv));
                if (n < 0) throw new AssertFail(string.Join(" ", r) + " — " + name + " zwrócił błąd");
            }
            // Kolejność i parametry jak RC_GENERUJ_SIATKA / S-REG (grubość 225, widoczny pręt reprezentatywny)
            G("B1", Call(AutoRebarType, "GenerateLayer", doc, bottomId, "rebar_bottom", "X", "B1", 10, 200.0, 40.0, true));
            G("B2", Call(AutoRebarType, "GenerateLayer", doc, bottomId, "rebar_bottom", "Y", "B2", 10, 200.0, 40.0, true));
            G("T1", Call(AutoRebarType, "GenerateLayer", doc, topId, "rebar_top", "X", "T1", 12, 200.0, 40.0, true));
            G("T2", Call(AutoRebarType, "GenerateLayer", doc, topId, "rebar_top", "Y", "T2", 12, 200.0, 40.0, true));
            G("UB_B1", Call(AutoRebarType, "GenerateUBLayer", doc, bottomId, "rebar_bottom", "B1", 225, "X", 200.0, 40.0, true));
            G("UB_B2", Call(AutoRebarType, "GenerateUBLayer", doc, bottomId, "rebar_bottom", "B2", 225, "Y", 200.0, 40.0, true));
            return string.Join(" ", r);
        }

        private static string AllBlocks(Database db, List<BarInfo> bars)
        {
            if (bars.Count == 0) throw new AssertFail("brak prętów RC_SINGLE_BAR");
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (var b in bars)
                {
                    var kind = Convert.ToString(Call(BarEntityType, "KindOf", (Entity)tr.GetObject(b.Id, OpenMode.ForRead)), Inv);
                    if (b.Type != "BlockReference" || kind != "Block")
                        throw new AssertFail($"{b.Mark} ({b.Shape}): {b.Type}, KindOf={kind}");
                }
            }
            return "pręty-bloki=" + bars.Count.ToString(Inv) + " ["
                   + string.Join(",", bars.Select(b => b.Mark + "/" + b.Shape).OrderBy(s => s, StringComparer.Ordinal)) + "]";
        }

        private static string DistSourcesAreBlocks(Database db)
        {
            int n = 0;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    var rec = ReadRecord(br, DistApp);
                    if (rec.Length == 0) continue;
                    string mark = rec.Length > 1 ? Convert.ToString(rec[1].Value, Inv) : "?";
                    if (rec.Length <= 18) throw new AssertFail($"rozkład {mark}: rekord bez [18]");
                    var src = Resolve(db, rec[18].Value);
                    if (src.IsNull || src.IsErased) throw new AssertFail($"rozkład {mark}: pręt źródłowy nie istnieje");
                    var e = (Entity)tr.GetObject(src, OpenMode.ForRead);
                    var kind = Convert.ToString(Call(BarEntityType, "KindOf", e), Inv);
                    if (!(e is BlockReference) || kind != "Block")
                        throw new AssertFail($"rozkład {mark}: pręt źródłowy {e.GetType().Name}, KindOf={kind}");
                    n++;
                }
            }
            if (n == 0) throw new AssertFail("brak rozkładów");
            return "rozkłady=" + n.ToString(Inv);
        }

        /// <summary>Każdy pręt z etykietą: grot etykiety (pierwszy wierzchołek lidera 0) leży na obrysie pręta-bloku.</summary>
        private static string LabelTipsOnBars(Database db, List<BarInfo> bars)
        {
            int n = 0;
            double max = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var b in bars)
                {
                    var lid = Resolve(db, b.LabelHandle);
                    if (lid.IsNull || lid.IsErased) continue;
                    if (!(tr.GetObject(lid, OpenMode.ForRead) is MLeader ml)) throw new AssertFail(b.Mark + ": etykieta nie jest MLeader");
                    var tip = ml.GetFirstVertex(0);
                    var cp = NPt(Inst(OpenBar(tr, b.Id, KBlock), "ClosestPoint", tr, tip));
                    double d = cp.DistanceTo(tip);
                    if (d > GeoTol) throw new AssertFail($"{b.Mark}: grot {P(tip)} odległy od obrysu o {d.ToString("E2", Inv)}");
                    max = Math.Max(max, d);
                    n++;
                }
                tr.Commit();
            }
            if (n == 0) throw new AssertFail("brak etykiet prętów");
            return "etykiety=" + n.ToString(Inv) + " max=" + max.ToString("E2", Inv);
        }

        /// <summary>Komplet pręta zbiorczego (Mark): pręt-blok v2 + etykieta (back-link) + jeden rozkład [18] + opis [11].</summary>
        private static (ObjectId bar, ObjectId label, ObjectId dist, ObjectId annot) SummarySet(Database db, string mark)
        {
            var bars = BarsInMs(db).Where(b => b.Mark == mark).ToList();
            if (bars.Count != 1) throw new AssertFail($"pręty {mark}: {bars.Count}");
            var bar = bars[0];
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var kind = Convert.ToString(Call(BarEntityType, "KindOf", (Entity)tr.GetObject(bar.Id, OpenMode.ForRead)), Inv);
                if (bar.Type != "BlockReference" || kind != "Block") throw new AssertFail($"{mark}: {bar.Type}, KindOf={kind}");

                var label = Resolve(db, bar.LabelHandle);
                if (label.IsNull || label.IsErased || !(tr.GetObject(label, OpenMode.ForRead) is MLeader ml))
                    throw new AssertFail(mark + ": brak etykiety MLeader");
                var lrec = ReadRecord(ml, LabelApp);
                if (lrec.Length < 2 || !SameHandle(lrec[1].Value, bar.Id.Handle)) throw new AssertFail(mark + ": etykieta bez back-linku");

                var dists = new List<(ObjectId id, TypedValue[] rec)>();
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    var rec = ReadRecord(br, DistApp);
                    if (rec.Length > 18 && SameHandle(rec[18].Value, bar.Id.Handle)) dists.Add((id, rec));
                }
                if (dists.Count != 1) throw new AssertFail($"{mark}: rozkłady wskazujące na pręt: {dists.Count}");
                var annot = dists[0].rec.Length > 11 ? Resolve(db, dists[0].rec[11].Value) : ObjectId.Null;
                if (annot.IsNull || annot.IsErased || ReadRecord((Entity)tr.GetObject(annot, OpenMode.ForRead), AnnotApp).Length == 0)
                    throw new AssertFail(mark + ": brak opisu rozkładu (RC_BAR_ANNOT)");
                return (bar.Id, label, dists[0].id, annot);
            }
        }

        private static string DescribeSet((ObjectId bar, ObjectId label, ObjectId dist, ObjectId annot) s)
            => $"pręt={s.bar.Handle} etykieta={s.label.Handle} rozkład={s.dist.Handle} opis={s.annot.Handle}";

        private static string S5State()
            => $"pręt usunięty={_s5Bar.IsErased} etykieta={_s5Label.IsErased} rozkład={_s5Dist.IsErased} opis={_s5Annot.IsErased}";

        private static TypedValue[] ReadRecord(Entity e, string app)
        {
            using (var rb = e.GetXDataForApplication(app))
                return rb?.AsArray() ?? new TypedValue[0];
        }

        private sealed class RingV
        {
            public Point3d P;
            public double Bulge;
        }

        /// <summary>Pierścienie pręta-polilinii: sama polilinia + encje RC_BAR_LINK wskazujące na nią (kolejność MS).</summary>
        private static List<List<RingV>> PolylineRings(Transaction tr, Database db, Polyline pl)
        {
            var rings = new List<List<RingV>> { RingOf(pl, Matrix3d.Identity) };
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased || id == pl.ObjectId) continue;
                if (!(tr.GetObject(id, OpenMode.ForRead) is Polyline cp)) continue;
                using (var rb = cp.GetXDataForApplication(XLinkApp))
                {
                    if (rb == null) continue;
                    var v = rb.AsArray();
                    if (v.Length >= 2 && SameHandle(v[1].Value, pl.Handle)) rings.Add(RingOf(cp, Matrix3d.Identity));
                }
            }
            return rings;
        }

        /// <summary>Pierścienie pręta-bloku: polilinie BTR (kolejność BTR) przekształcone BlockTransform.</summary>
        private static List<List<RingV>> BlockRings(Transaction tr, BlockReference br)
        {
            var rings = new List<List<RingV>>();
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            foreach (ObjectId id in btr)
            {
                if (id.IsErased) continue;
                if (tr.GetObject(id, OpenMode.ForRead) is Polyline bpl) rings.Add(RingOf(bpl, br.BlockTransform));
            }
            return rings;
        }

        private static List<RingV> RingOf(Polyline pl, Matrix3d xf)
        {
            var cs = xf.CoordinateSystem3d;
            double sign = cs.Xaxis.CrossProduct(cs.Yaxis).DotProduct(cs.Zaxis) < 0 ? -1.0 : 1.0;   // odbicie zmienia kierunek łuków
            var list = new List<RingV>();
            for (int i = 0; i < pl.NumberOfVertices; i++)
                list.Add(new RingV { P = pl.GetPoint3dAt(i).TransformBy(xf), Bulge = pl.GetBulgeAt(i) * sign });
            return list;
        }

        private static string Ring(List<RingV> act, List<RingV> exp)
        {
            Int("wierzchołki", act.Count, exp.Count);
            double max = 0;
            for (int i = 0; i < exp.Count; i++)
            {
                double d = act[i].P.DistanceTo(exp[i].P);
                if (d > GeoTol) throw new AssertFail($"wierzchołek {i}: jest {P(act[i].P)}, oczekiwano {P(exp[i].P)}");
                if (Math.Abs(act[i].Bulge - exp[i].Bulge) > GeoTol)
                    throw new AssertFail($"bulge {i}: jest {F(act[i].Bulge)}, oczekiwano {F(exp[i].Bulge)}");
                max = Math.Max(max, d);
            }
            return "n=" + exp.Count.ToString(Inv) + " max=" + max.ToString("E2", Inv);
        }

        private static Entity FirstBtrEntity(Transaction tr, BlockTableRecord btr)
        {
            foreach (ObjectId id in btr)
                if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Entity e) return e;
            return null;
        }

        private static string Eff(string layer, int color, string lt, double lts, LineWeight lw)
            => $"L={layer} C={color.ToString(Inv)} LT={lt} LTS={lts.ToString("0.######", Inv)} LW={lw}";

        private static ObjectId EnsureLayer(Transaction tr, Database db, string name, short color)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return lt[name];
            lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = name };
            rec.Color = Teigha.Colors.Color.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, color);
            var id = lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return id;
        }

        private static ObjectId EnsureLinetype(Transaction tr, Database db, string name)
        {
            var ltt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            if (ltt.Has(name)) return ltt[name];
            ltt.UpgradeOpen();
            var rec = new LinetypeTableRecord { Name = name, AsciiDescription = "__ __ __", PatternLength = 15.0, NumDashes = 2 };
            rec.SetDashLengthAt(0, 10.0);
            rec.SetDashLengthAt(1, -5.0);
            var id = ltt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return id;
        }

        private static object NewBar(Def d)
        {
            var bar = Activator.CreateInstance(Plugin.GetType(BarDataType, true));
            SetProp(bar, "Mark", d.Mark);
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

        private static object OpenBar(Transaction tr, ObjectId id, int accept)
        {
            var be = Call(BarEntityType, "Open", tr, id, OpenMode.ForRead, Enum.ToObject(Plugin.GetType(BarKindsType, true), accept));
            if (be == null) throw new AssertFail("BarEntity.Open zwrócił null dla " + id.Handle);
            return be;
        }

        private static List<Point3d> Outline(object be, Transaction tr)
            => ((IEnumerable<Point3d>)Inst(be, "OutlineVertices", tr)).ToList();

        private static Point3d NPt(object o)
        {
            if (o == null) throw new AssertFail("wynik null");
            return (Point3d)o;
        }

        private static TypedValue[] ReadRecord(Entity e)
        {
            using (var rb = e.GetXDataForApplication(XApp))
                return rb?.AsArray() ?? new TypedValue[0];
        }

        /// <summary>Pole i: ten sam kod typu, ten sam typ .NET wartości i ta sama wartość.</summary>
        private static void SameField(int i, TypedValue a, TypedValue b)
        {
            if (a.TypeCode != b.TypeCode)
                throw new AssertFail($"[{i}] typ: blok {Desc(b)}, polilinia {Desc(a)}");
            var ta = a.Value?.GetType(); var tb = b.Value?.GetType();
            if (ta != tb)
                throw new AssertFail($"[{i}] typ wartości: blok {tb?.Name ?? "null"}, polilinia {ta?.Name ?? "null"}");
            if (!Equals(a.Value, b.Value))
                throw new AssertFail($"[{i}] wartość: blok {Desc(b)}, polilinia {Desc(a)}");
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
        // Infrastruktura: refleksja, wyniki, pliki (jak w etapach 1–2)
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

        private static object Inst(object target, string method, params object[] args)
        {
            var t = target.GetType();
            var cands = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         .Where(m => m.Name == method && m.GetParameters().Length == args.Length).ToList();
            if (cands.Count != 1)
                throw new InvalidOperationException($"{t.FullName}.{method}/{args.Length}: znaleziono {cands.Count} przeciążeń");
            return Invoke(cands[0], target, args);
        }

        private static object StaticProp(string typeName, string name)
        {
            var pi = Plugin.GetType(typeName, true).GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi == null) throw new InvalidOperationException(typeName + ": brak właściwości statycznej " + name);
            return pi.GetValue(null);
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
            catch (System.Exception ex) { System.Diagnostics.Debug.WriteLine("PlanCStage3Tests.Msg: " + ex); }
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
                    "PLAN C ETAP 3a–3e2 — testy dymne BricsCAD (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv) + ")",
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

        private static string Pt(string what, Point3d act, Point3d exp)
        {
            double d = act.DistanceTo(exp);
            if (d > GeoTol) throw new AssertFail($"{what}: jest {P(act)}, oczekiwano {P(exp)} (odchyłka {d.ToString("E2", Inv)})");
            return $"{what}={P(act)}";
        }

        private static string Pts(List<Point3d> act, List<Point3d> exp)
        {
            if (exp.Count == 0) throw new AssertFail("brak oczekiwanych wierzchołków");
            Int("liczba wierzchołków", act.Count, exp.Count);
            double max = 0;
            for (int i = 0; i < exp.Count; i++)
            {
                double d = act[i].DistanceTo(exp[i]);
                if (d > GeoTol) throw new AssertFail($"wierzchołek {i}: jest {P(act[i])}, oczekiwano {P(exp[i])} (odchyłka {d.ToString("E2", Inv)})");
                max = Math.Max(max, d);
            }
            return "wierzchołki=" + exp.Count.ToString(Inv) + " max odchyłka=" + max.ToString("E2", Inv);
        }

        private static string True(bool cond, string detail)
        {
            if (!cond) throw new AssertFail(detail);
            return detail;
        }

        private static string Show(object o)
            => o == null ? "null" : o.GetType().Name + " " + Convert.ToString(o, Inv);

        private static string F(double d) => d.ToString("0.###", Inv);
        private static string P(Point3d p) => "(" + p.X.ToString("0.######", Inv) + "," + p.Y.ToString("0.######", Inv) + ")";
    }
}
