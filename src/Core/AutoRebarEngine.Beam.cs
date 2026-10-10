using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Belki / pogrubienia — generowanie prętów (plan belek E3 + E4, docs/PLAN_BELKI_2026-10-10.md; sekcja 4i ARCHITECTURE.md).
    /// TYLKO na jawne żądanie (RC_GENERATE → Beam, RC_BEAM) — siatka płyty (Mesh / B1…T2 / UB) stref belek nie widzi (E2 wstrzymany).
    ///   • Strzemiona 51 (kod BL, rzut dolny, seria &lt; 100): A = H − 35 − dT1 − dT2 − otulina dolna (50; w dół do 5),
    ///     B = szer. − 2 · otulina boczna (45) — decyzja użytkownika 2026-10-10 (parametry BeamParams.CoverSide / CoverBottom),
    ///     n = ceil((L − 100)/200) + 1 w zakresie strzemion odcinka (E4: w narożniku L dłuższy odcinek przez kwadrat narożnika);
    ///     z UB 03 wzdłuż odcinka — przesunięte o 100 względem UB 03; przy końcu z U-barami pierwsze strzemię za tyłami U-barów
    ///     (prześwit 20: 4 H16 — 125 od lica zamiast 50).
    ///   • Pręty podłużne: dół 4 H16 (BB, rzut dolny, seria &lt; 100), góra 4 H16 (BT, rzut górny, seria 101+) — proste odcinki
    ///     osiowe, plan BeamGeometry.PlanLayer / PlanBars (maks. 6000, zakład ≥ 800 — parametry), koniec U-bara / krawędzi — lico
    ///     z otuliną czołową (45), narożnik L / złącze T — lico własnego zakresu. Opis bez rozstawu: „4 H16-05 BEAM B”, „4 H16-103 BEAM T”;
    ///     strzemiona „52 H10-07-200 LINKS”.
    ///   • E4 (domyślne 1b.6–1b.8, do potwierdzenia): narożnik L — pręty L kształt 11 we wszystkich rzędach (jedna pozycja,
    ///     „4 H16-08 BEAM CORNER”), złącze T — pręty L zakotwione w belce ciągłej, końce (krawędź i wewnętrzny) — U-bary kształt 21
    ///     zagnieżdżone („1 H16-09 BEAM END”, różne B = osobne pozycje); kody BB / BT jak pręty proste. W rzucie widoczne ramię
    ///     z symbolem zagięcia (hak) na końcu przy zagięciu; pręty L — rozkład ze skosem (przesunięcie po przekątnej).
    ///   • Odcinki nieosiowe (skos — PLOT 319) → „detal ręczny”, bez prętów; narożnik z takim odcinkiem — bez prętów L (jak E3).
    ///   • Rozkłady znakowane RC_AUTOREBAR_BEAM [1] = strefa; ponowne generowanie usuwa tylko obiekty tej belki. Jedna komenda =
    ///     jedna grupa UNDO. Szablony (pręty w strefie rebar_bottom / rebar_top) zostają — identyczny pręt = istniejąca pozycja.
    /// </summary>
    public static partial class AutoRebarEngine
    {
        private const string BeamTag = "[RC BEAM]";
        /// <summary>Średnica siatki górnej płyty (T1 / T2) do wysokości strzemienia (1b.1) — domyślne, do potwierdzenia.</summary>
        public const int BeamSlabTopDia = 12;
        /// <summary>Otuliny belki — parametry BeamParams.CoverSide (boki i czoła, 45) / CoverBottom (spód belki i nibu, 50).</summary>
        public const double BeamStirrupEndOffset = 50, BeamStirrupUb03Shift = 100;
        /// <summary>Spinka UB 03 płyty w nibie: dolne ramię na otulinie dołem płyty + zapas (40 + 5 — jak SectionPlanner, poziom B1);
        /// pręt BN dołem leży w jej narożu — gdy otulina dolna belki jest większa, komunikat (UB 03 to element płyty, bez zmian).</summary>
        public const double BeamSlabUbBottomCover = 45;
        /// <summary>E4b: średnica spinki UB 03 w nibie (pręty w nibie w jej narożach) — H10 (610-70-610).</summary>
        public const int BeamNibUbDia = 10;

        public sealed class BeamRunResult
        {
            public int Zones, Stirrups, BottomDists, TopDists;
            /// <summary>E4: rozkłady prętów giętych (pręty L narożników / złącz T, U-bary) — dół + góra.</summary>
            public int BentDists;
            /// <summary>E4b: rozkłady prętów bocznych (BS) i prętów wzdłuż nibu belki (BN).</summary>
            public int SideDists, NibDists;
            public List<string> Messages = new List<string>();
        }

        /// <summary>Rzut płyty: obrys zewnętrzny + wierzchołki (wyprostowane) + bbox.</summary>
        private sealed class BeamView
        {
            public ObjectId OuterId;
            public List<Point2d> Verts;
            public Extents3d Box;
        }

        private static BeamView ReadBeamView(Database db, ObjectId slabId)
        {
            if (slabId.IsNull) return null;
            var outer = NibDetector.OuterOf(db, slabId);
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            if (!(tr.GetObject(outer, OpenMode.ForRead) is Polyline pl) || !GeometryHelper.IsEffectivelyClosed(pl)) return null;
            var v = GeometryHelper.SquaredOrSame(GeometryHelper.GetPolylineVertices(pl));
            return new BeamView { OuterId = outer, Verts = v, Box = GeometryHelper.VerticesBbox(v) };
        }

        /// <summary>
        /// Rzut partnerski płyty (dolny ↔ górny): przystający obrys (ten sam kształt, przesunięty) — preferowany z RC_GA tej samej
        /// płyty i przeciwnym rzutem, inaczej najbliższy. Zwraca (obrys, przesunięcie partner − wskazany) albo Null.
        /// </summary>
        internal static (ObjectId id, Vector2d delta) FindPartnerView(Database db, ObjectId slabOuterId)
        {
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                if (!(tr.GetObject(slabOuterId, OpenMode.ForRead) is Polyline me)) return (ObjectId.Null, new Vector2d());
                var mv = GeometryHelper.SquaredOrSame(GeometryHelper.GetPolylineVertices(me));
                var (myPlot, myView) = GaTagOf(me);
                var mMin = MinPoint(mv);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                ObjectId best = ObjectId.Null; Vector2d bestD = new Vector2d(); int bestRank = int.MaxValue; double bestLen = double.MaxValue;
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || id == slabOuterId) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Polyline pl) || !GeometryHelper.IsEffectivelyClosed(pl)) continue;
                    if (!string.Equals(pl.Layer, me.Layer, StringComparison.OrdinalIgnoreCase)) continue;
                    var v = GeometryHelper.SquaredOrSame(GeometryHelper.GetPolylineVertices(pl));
                    if (v.Count != mv.Count) continue;
                    var delta = MinPoint(v) - mMin;
                    if (delta.Length < 1.0 || !Congruent(v, mv, delta)) continue;
                    var (plot, view) = GaTagOf(pl);
                    int rank = myPlot != null && plot == myPlot && view != null && view != myView ? 0 : 1;
                    if (rank < bestRank || (rank == bestRank && delta.Length < bestLen))
                    { best = id; bestD = delta; bestRank = rank; bestLen = delta.Length; }
                }
                tr.Commit();
                return (best, bestD);
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.FindPartnerView", ex); return (ObjectId.Null, new Vector2d()); }
        }

        private static (string plot, string view) GaTagOf(Entity e)
        {
            using (var rb = e.GetXDataForApplication(GaImportEngine.XApp))
            {
                var v = rb?.AsArray();
                if (v == null || v.Length < 3) return (null, null);
                return (Convert.ToString(v[1].Value, CultureInfo.InvariantCulture), Convert.ToString(v[2].Value, CultureInfo.InvariantCulture));
            }
        }

        /// <summary>
        /// Czy obrys to rzut górny: RC_GA [2] = T; bez znacznika — rozkłady T1/T2 (a nie B1/B2) w obrysie; inaczej dolny.
        /// </summary>
        internal static bool IsTopView(Database db, ObjectId slabOuterId)
        {
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                if (!(tr.GetObject(slabOuterId, OpenMode.ForRead) is Polyline pl)) return false;
                var (_, view) = GaTagOf(pl);
                if (view == "T") return true;
                if (view == "B") return false;
                var verts = GeometryHelper.GetPolylineVertices(pl);
                int b = 0, t = 0;
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    var bar = BarBlockEngine.ReadXData(br);
                    if (bar == null) continue;
                    string tag = ReadSlabTag(br);
                    bool mine = tag != null ? SameHandle(tag, pl.Handle.ToString())
                                            : GeometryHelper.IsPointInsidePolygon(verts, new Point2d(br.Position.X, br.Position.Y));
                    if (!mine) continue;
                    if (bar.LayerCode == "B1" || bar.LayerCode == "B2") b++;
                    else if (bar.LayerCode == "T1" || bar.LayerCode == "T2") t++;
                }
                tr.Commit();
                return t > 0 && b == 0;
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.IsTopView", ex); return false; }
        }

        /// <summary>
        /// RC_GENERATE → Beam / RC_BEAM: pręty belek płyty (wszystkie strefy rzutu dolnego albo tylko <paramref name="onlyZoneId"/>).
        /// bottomSlabId — obrys rzutu dolnego (strzemiona, dół), topSlabId — rzut górny (góra; Null = pominięta z komunikatem).
        /// </summary>
        public static BeamRunResult GenerateBeams(Document doc, ObjectId bottomSlabId, ObjectId topSlabId, ObjectId onlyZoneId = default)
        {
            var db = doc.Database;
            var ed = doc.Editor;
            var res = new BeamRunResult();
            void Msg(string m) { res.Messages.Add(m); ed.WriteMessage($"\n{BeamTag} {m}"); }

            var bottom = ReadBeamView(db, bottomSlabId);
            if (bottom == null) { Msg("Obrys rzutu dolnego nie jest zamkniętą polilinią — przerwano."); return res; }
            var top = topSlabId.IsNull ? null : ReadBeamView(db, topSlabId);
            if (top != null && top.OuterId == bottom.OuterId) top = null;
            Vector2d delta = new Vector2d();
            if (top != null)
            {
                delta = MinPoint(top.Verts) - MinPoint(bottom.Verts);
                if (top.Verts.Count != bottom.Verts.Count || !Congruent(top.Verts, bottom.Verts, delta))
                {
                    Msg("Obrys górny nie przystaje do dolnego (inna płyta?) — pręty górne belek pominięte.");
                    top = null;
                }
            }

            // Strefy rzutu dolnego i partnerzy na górnym
            var pairs = new List<(ObjectId zb, ObjectId zt)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var zonesB = BeamStore.ZonesOfSlab(tr, db, bottom.OuterId, bottom.Verts);
                var zonesT = top != null ? BeamStore.ZonesOfSlab(tr, db, top.OuterId, top.Verts) : new List<ObjectId>();
                if (!onlyZoneId.IsNull)
                {
                    // wskazana strefa może leżeć na rzucie górnym — wtedy jej partner dolny
                    if (zonesB.Contains(onlyZoneId)) zonesB = new List<ObjectId> { onlyZoneId };
                    else if (zonesT.Contains(onlyZoneId))
                    {
                        var tv = ZoneVerts(tr, onlyZoneId);
                        zonesB = zonesB.Where(z => Congruent(tv, ZoneVerts(tr, z), delta)).Take(1).ToList();
                    }
                    else zonesB = new List<ObjectId>();
                }
                foreach (var zb in zonesB)
                {
                    var bv = ZoneVerts(tr, zb);
                    var zt = zonesT.FirstOrDefault(z => { var tv = ZoneVerts(tr, z); return tv.Count == bv.Count && Congruent(tv, bv, delta); });
                    pairs.Add((zb, zt));
                }
                tr.Commit();
            }
            if (pairs.Count == 0)
            {
                Msg("Na płycie nie ma stref belek (warstwa RC-BEAM) — wskaż / narysuj strefę komendą RC_BEAM.");
                return res;
            }
            if (top == null && topSlabId.IsNull) Msg("Rzut górny nie wskazany — pręty górne belek (BEAM T) pominięte.");

            var saveSlab = _currentSlabHandle;
            var saveHoles = _currentHoles;
            try
            {
                using (doc.LockDocument())
                {
                    _currentSlabHandle = null;              // rozkłady belki bez znacznika płyty (własny znacznik RC_AUTOREBAR_BEAM)
                    _currentHoles = new List<HoleBox>();
                    InitLabelOccupancy(db);
                    var ub03 = ReadUb03(db, bottom);
                    foreach (var (zb, zt) in pairs)
                    {
                        try { GenerateOneBeam(doc, bottom, top, delta, zb, zt, ub03, res, Msg); }
                        catch (System.Exception ex)
                        {
                            Log.Error("AutoRebar.GenerateBeams", ex);
                            Msg($"Błąd belki: {ex.Message}");
                        }
                    }
                }
            }
            finally
            {
                _currentSlabHandle = saveSlab;
                _currentHoles = saveHoles ?? new List<HoleBox>();
            }
            Msg($"Belki: {res.Zones} — strzemiona: {res.Stirrups} rozkł., dół: {res.BottomDists}, góra: {res.TopDists}, " +
                $"pręty L / U-bary: {res.BentDists}, boczne: {res.SideDists}, w nibie: {res.NibDists}.");
            return res;
        }

        private static List<Point2d> ZoneVerts(Transaction tr, ObjectId id)
            => tr.GetObject(id, OpenMode.ForRead) is Polyline pl ? GeometryHelper.GetPolylineVertices(pl) : new List<Point2d>();

        private static void GenerateOneBeam(Document doc, BeamView bottom, BeamView top, Vector2d delta, ObjectId zb, ObjectId zt,
                                            List<(bool barsAlongX, double c0, double c1, List<double> pos)> ub03,
                                            BeamRunResult res, Action<string> msg)
        {
            var db = doc.Database;
            BeamParams p;
            List<Point2d> zbPoly, ztPoly = null;
            string zbHandle, ztHandle = null;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var pl = (Polyline)tr.GetObject(zb, OpenMode.ForRead);
                p = BeamStore.Read(pl) ?? new BeamParams();
                zbPoly = GeometryHelper.GetPolylineVertices(pl);
                zbHandle = pl.Handle.ToString();
                if (!zt.IsNull && tr.GetObject(zt, OpenMode.ForRead) is Polyline tpl)
                {
                    ztPoly = GeometryHelper.GetPolylineVertices(tpl);
                    ztHandle = tpl.Handle.ToString();
                }
                tr.Commit();
            }
            string name = $"belka {zbHandle}";
            if (p.Height <= 0)
            {
                msg($"{name}: brak wysokości H — ustaw w RC_BEAM; pominięto.");
                return;
            }

            var an = BeamGeometry.Analyze(zbPoly.Select(v => (v.X, v.Y)).ToList(), bottom.Verts.Select(v => (v.X, v.Y)).ToList());
            foreach (var m in an.Messages) msg($"{name}: {m}");
            var segs = an.Segments.Where(s => !s.Manual).ToList();

            // Ponowne generowanie: tylko obiekty tej belki (znacznik = strefa; kopia bez znacznika — punkt w strefie)
            var old = new List<(ObjectId, ObjectId)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                old.AddRange(BeamStore.FindGenerated(tr, db, zbHandle, zbPoly, c => c != BeamGeometry.CodeTop));
                if (ztHandle != null) old.AddRange(BeamStore.FindGenerated(tr, db, ztHandle, ztPoly, c => c == BeamGeometry.CodeTop));
                tr.Commit();
            }
            EraseOldDistributions(db, old);
            if (old.Count > 0) msg($"{name}: usunięto poprzednie rozkłady belki ({old.Count}).");
            res.Zones++;
            if (segs.Count == 0) return;

            // E4: zamknięcie końców (domyślne 1b.6–1b.8, do potwierdzenia przez Bartka)
            int nL = an.Joints.Count(j => !j.IsTee && !j.A.Manual && !j.B.Manual);
            int nT = an.Joints.Count(j => j.IsTee && !j.A.Manual && !j.B.Manual);
            int nU = segs.Sum(x => (x.LoClose == BeamGeometry.Closure.UBar ? 1 : 0) + (x.HiClose == BeamGeometry.Closure.UBar ? 1 : 0));
            msg($"{name}: zamknięcie końców — narożniki L: {nL}, złącza T: {nT}, końce z U-barami: {nU} " +
                $"(pręty L kształt 11, U-bary H{p.UBarDia} kształt 21; wartości domyślne do potwierdzenia).");
            if (top != null && ztHandle == null)
                msg($"{name}: brak strefy na rzucie górnym — pręty górne (BEAM T) pominięte.");

            double H = p.Height;
            double A51 = BeamGeometry.StirrupHeight(H, p.StirrupTopLayers, BeamSlabTopDia, BeamSlabTopDia,
                                                    BeamGeometry.DefaultCoverTop, p.CoverBottom);
            // koniec z U-barami: pierwsze strzemię za tyłami U-barów (prześwit 20) — tyły nie leżą w płaszczyźnie strzemienia
            double offU = BeamGeometry.StirrupEndOffsetAtUBars(Math.Max(p.BottomCount, p.TopCount), p.UBarDia, p.StirrupDia,
                                                               p.CoverSide, BeamStirrupEndOffset);
            bool offUMsg = false;
            foreach (var s in segs)
            {
                double B51 = BeamGeometry.StirrupWidth(s.Width, p.CoverSide);
                // ── strzemiona (rzut dolny; E4: zakres strzemion — w narożniku L dłuższy odcinek przez kwadrat narożnika) ──
                if (A51 > 0 && B51 > 0)
                {
                    var tpl = FindOrCreateBeamTemplate(db, doc.Editor, bottom.Box, top: false, d: p.StirrupDia, shape: p.StirrupShape ?? "51",
                                                       a: A51, b: B51, code: BeamGeometry.CodeLinks);
                    var ubAlong = Ub03Along(ub03, s);
                    double offLo = s.LoClose == BeamGeometry.Closure.UBar ? offU : BeamStirrupEndOffset;
                    double offHi = s.HiClose == BeamGeometry.Closure.UBar ? offU : BeamStirrupEndOffset;
                    if (!offUMsg && offU > BeamStirrupEndOffset + 1e-6 && (offLo > BeamStirrupEndOffset || offHi > BeamStirrupEndOffset))
                    {
                        offUMsg = true;
                        msg($"{name}: przy końcach z U-barami pierwsze strzemię {offU:F0} od lica (za tyłami U-barów, prześwit " +
                            $"{BeamGeometry.UBarClearGap:F0}; wartość domyślna do potwierdzenia).");
                    }
                    var (first, step, n, fromUb) = BeamGeometry.StirrupLayout(s.LinkLo, s.LinkHi, p.StirrupSpacing, offLo,
                                                                              ubAlong, BeamStirrupUb03Shift, offHi);
                    if (!tpl.id.IsNull && n > 0)
                    {
                        double last = first + (n - 1) * step;
                        double c0 = s.AcrossLo + p.CoverSide, c1 = s.AcrossHi - p.CoverSide;
                        // pręty strzemion prostopadle do osi belki
                        bool barsX = !s.AlongX;
                        double x0 = barsX ? c0 : first, x1 = barsX ? c1 : last;
                        double y0 = barsX ? first : c0, y1 = barsX ? last : c1;
                        if (BeamDistribution(db, bottom, tpl, x0, y0, x1, y1, barsX, p.StirrupDia, B51, p.StirrupSpacing,
                                             n > 1 ? step : double.NaN, BeamGeometry.CodeLinks, BeamGeometry.SuffixLinks,
                                             noSpacing: false, shape: tpl.bar.ShapeCode, repr: 0, zoneHandle: zbHandle))
                            res.Stirrups++;
                        if (fromUb) msg($"{name}: strzemiona przesunięte o {BeamStirrupUb03Shift:F0} względem UB 03 (rozstaw {step:F0}).");
                    }
                }
                else msg($"{name}: wymiary strzemienia ≤ 0 (H = {H:F0}, szer. = {s.Width:F0}) — strzemiona pominięte.");
            }

            // ── pręty podłużne, pręty L (narożniki L / złącza T), U-bary (końce) — plan warstwy (E4) ──
            bool withTop = top != null && ztHandle != null;
            var planB = BeamGeometry.PlanLayer(an, p.BottomCount, p.BottomDia, p.UBarDia, p.Lap, p.MaxLength, p.StirrupDia, p.CoverSide);
            var planT = withTop
                ? BeamGeometry.PlanLayer(an, p.TopCount, p.TopDia, p.UBarDia, p.Lap, p.MaxLength, p.StirrupDia, p.CoverSide)
                : null;
            foreach (var m in planB.Messages) msg($"{name}: {m}");
            if (planT != null) foreach (var m in planT.Messages.Where(x => !planB.Messages.Contains(x))) msg($"{name}: góra: {m}");
            foreach (var g in planB.Straight.GroupBy(x => x.Seg).Where(g => g.Count() > 1))
            {
                var pl = g.Select(x => (x.Start, x.Length)).ToList();
                msg($"{name}: odcinek {g.Key.FaceLo:F0}..{g.Key.FaceHi:F0}: pręty {string.Join(" + ", pl.Select(x => x.Length.ToString("F0", CultureInfo.InvariantCulture)))}" +
                    $", zakład {string.Join(" / ", BeamGeometry.LapsOf(pl).Select(x => x.ToString("F0", CultureInfo.InvariantCulture)))}.");
            }

            foreach (var run in planB.Straight)
            {
                var s = run.Seg;
                double a0 = run.Start, a1 = run.Start + run.Length;
                var (f0, sp) = BeamGeometry.BarsAcross(s.AcrossLo, s.AcrossHi, p.BottomCount, p.BottomDia, p.StirrupDia, p.CoverSide);
                var tpl = FindOrCreateBeamTemplate(db, doc.Editor, bottom.Box, top: false, d: p.BottomDia, shape: "00", a: run.Length, b: 0,
                                                   code: BeamGeometry.CodeBottom);
                if (!tpl.id.IsNull && BeamLongitudinal(db, bottom, tpl, s, a0, a1, f0, sp, p.BottomCount, p.BottomDia,
                                                        BeamGeometry.CodeBottom, BeamGeometry.SuffixBottom, run.SegIndex + run.Index, zbHandle, new Vector2d()))
                    res.BottomDists++;
            }
            if (planT != null)
                foreach (var run in planT.Straight)
                {
                    var s = run.Seg;
                    double a0 = run.Start, a1 = run.Start + run.Length;
                    var (f0, sp) = BeamGeometry.BarsAcross(s.AcrossLo, s.AcrossHi, p.TopCount, p.TopDia, p.StirrupDia, p.CoverSide);
                    var tpl = FindOrCreateBeamTemplate(db, doc.Editor, top.Box, top: true, d: p.TopDia, shape: "00", a: run.Length, b: 0,
                                                       code: BeamGeometry.CodeTop);
                    if (!tpl.id.IsNull && BeamLongitudinal(db, top, tpl, s, a0, a1, f0, sp, p.TopCount, p.TopDia,
                                                            BeamGeometry.CodeTop, BeamGeometry.SuffixTop, run.SegIndex + run.Index, ztHandle, delta))
                        res.TopDists++;
                }

            foreach (var run in planB.Bent)
                if (BeamBent(db, doc.Editor, bottom, run, top: false, BeamGeometry.CodeBottom, zbHandle, new Vector2d())) res.BentDists++;
            if (planT != null)
                foreach (var run in planT.Bent)
                    if (BeamBent(db, doc.Editor, top, run, top: true, BeamGeometry.CodeTop, ztHandle, delta)) res.BentDists++;

            // ── E4b (wymaganie użytkownika 2026-10-10; domyślne, do potwierdzenia): pręty boczne (BS) na obu bokach przy ramionach
            //    strzemienia i pręty wzdłuż nibu belki (BN: górą i dołem w narożach spinki UB 03) — rzut dolny, seria dolna; proste,
            //    bez zagięć: narożnik L / złącze T — lico własnego zakresu, koniec z U-barami — lico z otuliną (jak pręty proste) ──
            int nSide = p.SideDia > 0 ? BeamGeometry.SideBarCount(A51, p.StirrupDia, p.BottomDia, p.TopDia, p.SideSpacing) : 0;
            if (p.SideDia > 0)
                msg($"{name}: pręty boczne H{p.SideDia}: {nSide} na bok (rozstaw pionowy ≤ {p.SideSpacing:F0}; proste — w narożnikach " +
                    "i na końcach bez zagięć, do potwierdzenia).");
            if (nSide > 0)
            {
                double lapS = BeamGeometry.LapFor(p.Lap, p.BottomDia, p.SideDia);
                int total = 2 * nSide;
                for (int i = 0; i < segs.Count; i++)
                {
                    var s = segs[i];
                    var (lo, hi) = BeamGeometry.StraightRange(s, p.CoverSide);
                    var (c0, c1) = BeamGeometry.SideBarsAcross(s, p.SideDia, p.StirrupDia, p.CoverSide);
                    var plan = BeamGeometry.PlanBars(hi - lo, p.MaxLength, lapS);
                    for (int k = 0; k < plan.Count; k++)
                    {
                        double a0 = lo + plan[k].Offset, a1 = a0 + plan[k].Length;
                        var tpl = FindOrCreateBeamTemplate(db, doc.Editor, bottom.Box, top: false, d: p.SideDia, shape: "00", a: plan[k].Length, b: 0,
                                                           code: BeamGeometry.CodeSide);
                        // dwa pręty w rzucie (po jednym na bok), opis i BBS — wszystkie (CountDisplay = 2 × liczba na bok)
                        if (!tpl.id.IsNull && BeamLongitudinal(db, bottom, tpl, s, a0, a1, c0, c1 - c0, 2, p.SideDia, BeamGeometry.CodeSide,
                                                                BeamGeometry.SuffixSide, i + k, zbHandle, new Vector2d(),
                                                                total > 2 ? (Action<BarData>)(bar => bar.CountDisplay = total) : null))
                            res.SideDists++;
                    }
                }
            }
            var outl = bottom.Verts.Select(v => (v.X, v.Y)).ToList();
            int nibRuns = 0;
            // T IN NIB płyty (rzut górny, przesunięty do układu rzutu dolnego) — BN górą może je dublować (Mesh nie widzi belek, E2 wstrzymany)
            var tinNib = p.NibTopDia > 0 ? ReadTinNib(db, top ?? bottom, top != null ? delta : new Vector2d()) : null;
            var noUbMsg = new HashSet<int>();
            bool ubCoverMsg = false;
            foreach (var (nd, suffix) in new[] { (p.NibTopDia, BeamGeometry.SuffixNibTop), (p.NibBottomDia, BeamGeometry.SuffixNibBottom) })
            {
                if (nd <= 0) continue;
                double lapN = BeamGeometry.LapFor(p.Lap, p.BottomDia, nd);
                for (int i = 0; i < segs.Count; i++)
                {
                    var line = BeamGeometry.NibBarLine(segs[i], outl, nd, BeamNibUbDia, p.CoverSide);
                    if (line == null) continue;
                    var (across, lo, hi) = line.Value;
                    // położenie w poprzek = naroże spinki UB 03 (otulina + Ø spinki + d/2) — bez UB 03 wzdłuż nibu: komunikat
                    if (Ub03Along(ub03, segs[i]) == null && noUbMsg.Add(i))
                        msg($"{name}: odcinek {i + 1} — brak UB 03 wzdłuż nibu belki; pręty BN w położeniu naroży spinki " +
                            $"(krawędź − {BeamGeometry.NibUbEdgeCover:F0} − {BeamNibUbDia} − d/2) — do sprawdzenia.");
                    // BN dołem w narożu UB 03 (element płyty, bez zmian): otulina dołem UB 03 płyty mniejsza niż otulina dolna belki — komunikat
                    if (suffix == BeamGeometry.SuffixNibBottom && !ubCoverMsg && Ub03Along(ub03, segs[i]) != null
                        && BeamSlabUbBottomCover < p.CoverBottom - 0.5)
                    {
                        ubCoverMsg = true;
                        msg($"{name}: UB 03 płyty w nibie belki ma otulinę dołem {BeamSlabUbBottomCover:F0} < otulina dolna belki {p.CoverBottom:F0} " +
                            "— pręt BN dołem liczony od UB 03 (spinka płyty bez zmian); do sprawdzenia.");
                    }
                    if (suffix == BeamGeometry.SuffixNibTop && TinAlong(tinNib, segs[i], across, lo, hi))
                        msg($"{name}: odcinek {i + 1} — pręt BN górą (H{nd}) może dublować T IN NIB wzdłuż tego nibu — do potwierdzenia " +
                            "(Nib: pręt górą = 0 w oknie RC_BEAM wyłącza BN górą).");
                    var plan = BeamGeometry.PlanBars(hi - lo, p.MaxLength, lapN);
                    for (int k = 0; k < plan.Count; k++)
                    {
                        double a0 = lo + plan[k].Offset, a1 = a0 + plan[k].Length;
                        var tpl = FindOrCreateBeamTemplate(db, doc.Editor, bottom.Box, top: false, d: nd, shape: "00", a: plan[k].Length, b: 0,
                                                           code: BeamGeometry.CodeNib);
                        if (!tpl.id.IsNull && BeamLongitudinal(db, bottom, tpl, segs[i], a0, a1, across, 0, 1, nd, BeamGeometry.CodeNib,
                                                                suffix, i + k, zbHandle, new Vector2d()))
                        { res.NibDists++; nibRuns++; }
                    }
                }
            }
            if (p.NibTopDia > 0 || p.NibBottomDia > 0)
                msg($"{name}: pręty wzdłuż nibu belki (górą H{p.NibTopDia}, dołem H{p.NibBottomDia} w narożach spinki UB 03): {nibRuns} rozkł. " +
                    "(na końcach przy krawędzi do lica z otuliną; domyślne, do potwierdzenia).");
        }

        /// <summary>
        /// E4: rozkład prętów giętych (pręty L narożnika / złącza T, U-bar końca): szablon (kształt 11 / 21, identyczny pręt = ta sama
        /// pozycja) + rozkład widocznego ramienia — skos (pręty L przesunięte po przekątnej), symbol zagięcia (hak) na końcu przy
        /// zagięciu w stronę drugiego ramienia; opis bez rozstawu („4 H16-08 BEAM CORNER”, „1 H16-09 BEAM END”), wszystkie pręty widoczne.
        /// </summary>
        private static bool BeamBent(Database db, Editor ed, BeamView view, BeamGeometry.BentRun r, bool top, string code,
                                     string zoneHandle, Vector2d delta)
        {
            var tpl = FindOrCreateBeamTemplate(db, ed, view.Box, top, d: r.Dia, shape: r.Shape, a: r.A, b: r.B, code: code, c: r.C);
            if (tpl.id.IsNull) return false;
            bool barsX = r.AlongX;
            double s0 = Math.Min(r.Start0, r.StartN);
            double c0 = r.Across0, c1 = r.Across0 + Math.Max(0, r.Count - 1) * r.Spacing;
            double x0 = barsX ? s0 : c0, x1 = barsX ? s0 + r.LineLength : c1;
            double y0 = barsX ? c0 : s0, y1 = barsX ? c1 : s0 + r.LineLength;
            double skewS = r.Start0 - s0, skewE = r.StartN - s0;
            // symbol LBar: Up = 90° w lewo od kierunku pręta (pręty X → +Y, pręty Y → −X)
            bool up = barsX ? r.BendDir > 0 : r.BendDir < 0;
            string suffix = r.Kind == "U" ? BeamGeometry.SuffixEnd : BeamGeometry.SuffixCorner;
            return BeamDistribution(db, view, tpl, x0 + delta.X, y0 + delta.Y, x1 + delta.X, y1 + delta.Y, barsX, r.Dia, r.LineLength,
                                    r.Count > 1 ? r.Spacing : 200, r.Count > 1 ? r.Spacing : double.NaN, code, suffix, noSpacing: true,
                                    shape: r.Shape, repr: -1, zoneHandle: zoneHandle,
                                    customize: bar =>
                                    {
                                        bar.SkewStart = skewS;
                                        bar.SkewEnd = skewE;
                                        bar.SymbolType = "Hook";
                                        bar.SymbolSide = r.BendAtStart ? "Left" : "Right";
                                        bar.SymbolDirection = up ? "Up" : "Down";
                                    });
        }

        /// <summary>Rozkład prętów podłużnych odcinka s (a0..a1 wzdłuż osi, count prętów w poprzek od f0 co sp), przesunięty o delta.</summary>
        private static bool BeamLongitudinal(Database db, BeamView view, (ObjectId id, BarData bar) tpl, BeamGeometry.Segment s,
                                             double a0, double a1, double f0, double sp, int count, int d,
                                             string code, string suffix, int repr, string zoneHandle, Vector2d delta,
                                             Action<BarData> customize = null)
        {
            double c1 = f0 + Math.Max(0, count - 1) * sp;
            bool barsX = s.AlongX;
            double x0 = barsX ? a0 : f0, x1 = barsX ? a1 : c1;
            double y0 = barsX ? f0 : a0, y1 = barsX ? c1 : a1;
            return BeamDistribution(db, view, tpl, x0 + delta.X, y0 + delta.Y, x1 + delta.X, y1 + delta.Y, barsX, d, a1 - a0,
                                    count > 1 ? sp : 200, count > 1 ? sp : double.NaN, code, suffix, noSpacing: true,
                                    shape: null, repr: repr, zoneHandle: zoneHandle, customize: customize);
        }

        /// <summary>
        /// Jeden rozkład belki przez wspólną ścieżkę siatki (GenerateDistributionWithLeaderAtOffset: opis bez kolizji, wygaszenie
        /// do pręta reprezentatywnego) + znacznik RC_AUTOREBAR_BEAM. barsX — pręty wzdłuż X.
        /// </summary>
        private static bool BeamDistribution(Database db, BeamView view, (ObjectId id, BarData bar) tpl,
                                             double x0, double y0, double x1, double y1, bool barsX, int d, double length,
                                             double spacing, double forced, string code, string suffix, bool noSpacing, string shape,
                                             int repr, string zoneHandle, Action<BarData> customize = null)
        {
            double sMin = barsX ? view.Box.MinPoint.Y : view.Box.MinPoint.X;
            double sMax = barsX ? view.Box.MaxPoint.Y : view.Box.MaxPoint.X;
            _lastDistId = ObjectId.Null;
            bool ok = GenerateDistributionWithLeaderAtOffset(
                db, x0, y0, x1, y1, tpl.id, tpl.bar, d, length, spacing, code, barsX ? "X" : "Y",
                DefaultCover, barsX ? (y1 - y0) : (x1 - x0), SpacingMode.Nominal, sMin, sMax,
                representativeSegment: repr, markSuffix: suffix, forcedSpacing: forced,
                noSpacingMark: noSpacing, shapeCode: shape, customizeBar: customize);
            if (!ok || _lastDistId.IsNull) return false;
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                if (tr.GetObject(_lastDistId, OpenMode.ForWrite) is Entity e) BeamStore.Tag(tr, db, e, zoneHandle);
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.BeamDistribution", ex); }
            return true;
        }

        /// <summary>
        /// Szablon pręta belki: identyczny pręt na rysunku (kształt, Ø, A, B, C; seria dołu &lt; 100 albo góry 101+) → ta pozycja;
        /// inaczej nowy w strefie szablonów (rebar_bottom / rebar_top) pod najniższym prętem strefy — numer wyłącznie
        /// PositionCounter.NextAuto (dół, strzemiona) / NextAutoTop (góra).
        /// </summary>
        private static (ObjectId id, BarData bar) FindOrCreateBeamTemplate(Database db, Editor ed, Extents3d slabBox, bool top,
                                                                          int d, string shape, double a, double b, string code,
                                                                          double c = 0)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (var be in BarEntity.ScanModelSpace(tr, db, BarKinds.Polyline | BarKinds.Block))
                {
                    var x = be.Read();
                    if (x == null || x.Diameter != d || (x.ShapeCode ?? "00") != shape) continue;
                    int nr = SingleBarEngine.ExtractPosNr(x.Mark);
                    bool series = top ? nr >= PositionCounter.TopSeriesStart && nr < PositionCounter.SeparateSeriesStart
                                      : nr >= PositionCounter.FirstAutoNumber && nr < PositionCounter.TopSeriesStart;
                    if (!series || Math.Abs(x.LengthA - a) >= 1.0) continue;
                    if (shape != "00" && (Math.Abs(x.LengthB - b) >= 1.0 || Math.Abs(x.LengthC - c) >= 1.0)) continue;
                    var foundId = be.Entity.ObjectId;
                    tr.Commit();
                    return (foundId, x);
                }
                tr.Commit();
            }

            string zoneLayer = top ? "rebar_top" : "rebar_bottom";
            ObjectId rectId; Extents3d zone; double lowY;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                (rectId, zone) = FindOrCreateTemplateZone(db, tr, zoneLayer, slabBox, ed);
                lowY = zone.MaxPoint.Y - TemplateOffsetY + TemplateSpacingY;
                foreach (var be in BarEntity.ScanModelSpace(tr, db, BarKinds.Polyline | BarKinds.Block))
                {
                    var ins = be.FirstOutlineVertex(tr);
                    if (ins == null || !GeometryHelper.IsInsideBbox(ins.Value, zone)) continue;
                    try { lowY = Math.Min(lowY, be.Entity.GeometricExtents.MinPoint.Y); }
                    catch (System.Exception ex) { Log.Error("AutoRebar.FindOrCreateBeamTemplate.Ext", ex); }
                }
                tr.Commit();
            }

            var used = PositionCounter.GetUsedPositionNumbers(db);
            int posNr = top ? PositionCounter.NextAutoTop(db, used) : PositionCounter.NextAuto(db, used);
            var bar = BuildBarData(d, posNr, a, code);
            bar.ShapeCode = shape;
            bar.LengthA = a;
            if (shape != "00") { bar.LengthB = b; bar.LengthC = c; }
            // E6: długość całkowita wg BS 8666:2020 (51: 2A + 2B + max(16d, 160), 11, 21) — bez niej rekord ma TL = 0, odczyt podaje A
            // i RC_BBS ostrzega „długość wg BS 8666:2020 … w rysunku …” dla strzemion 51 (BBS i tak liczy z formuły)
            try { bar.TotalLength = BarLengthRules.TotalLengthFor(shape, new[] { a, b, c }, d); }
            catch (System.Exception ex) { Log.Error("AutoRebar.FindOrCreateBeamTemplate.TotalLength", ex); }
            bar.Mark = BarData.FormatMark(d, posNr, 0, 1);
            // Elewacja szablonu (BarGeometryBuilder): 00 — linia; 51 — od 0 do A w górę; 11 — ramię B w górę; 21 — ramiona A w dół
            // od punktu wstawienia. shift — przesunięcie w dół (góra rysunku pod poprzednim szablonem), topOff — góra nad wstawieniem.
            double shift, topOff, w;
            switch (shape)
            {
                case "00": shift = 0; topOff = 0; w = a; break;
                case "11": shift = b; topOff = b; w = a; break;
                case "21": shift = 0; topOff = Math.Max(0, c - a); w = b; break;
                default: shift = a; topOff = a; w = b; break;    // strzemię 51: wysokość A
            }
            var insertPt = new Point3d(zone.MinPoint.X + TemplateOffsetX, lowY - TemplateSpacingY - shift, 0);

            ObjectId barId = SingleBarEngine.PlaceBar(db, bar, insertPt);
            if (barId.IsNull) return (ObjectId.Null, null);
            var textPt = new Point3d(insertPt.X + w * 0.5, insertPt.Y + topOff + TemplateLabelOffsetY, 0);
            Point3d tip;
            using (var trTip = db.TransactionManager.StartTransaction())
            {
                tip = SingleBarEngine.GetBarArrowTip(barId, bar, textPt, trTip);
                trTip.Commit();
            }
            ObjectId labelId = SingleBarEngine.PlaceBarLabel(db, tip, textPt, bar.Mark, barId);
            if (!labelId.IsNull)
            {
                using var trL = db.TransactionManager.StartTransaction();
                if (trL.GetObject(barId, OpenMode.ForWrite) is Entity be)
                {
                    bar.LabelHandle = labelId.Handle.ToString();
                    BarXData.Write(be, bar);
                }
                trL.Commit();
            }
            PositionCounter.CommitUsed(db, posNr);
            GrowZoneToFit(db, rectId, barId, zone);
            ed?.WriteMessage($"\n{BeamTag} Utworzono szablon {bar.Mark} (kształt {shape}, A={a:F0}" + (shape != "00" ? $", B={b:F0}" : "")
                             + (Math.Abs(c) > 0.5 ? $", C={c:F0}" : "") + ").");
            return (barId, bar);
        }

        // ── RC_BEAM: przygotowanie strefy (konwersja, rzut partnerski, parametry) ──

        /// <summary>Strefa belki gotowa do okna RC_BEAM: strefy obu rzutów, obrysy, parametry i opis geometrii.</summary>
        public sealed class BeamZoneContext
        {
            public ObjectId ZoneBottom = ObjectId.Null, ZoneTop = ObjectId.Null;
            public ObjectId SlabBottom = ObjectId.Null, SlabTop = ObjectId.Null;
            public BeamParams Params = new BeamParams();
            /// <summary>Skąd H: „opis w strefie”, „rekord strefy”, null = brak.</summary>
            public string HeightSource;
            public List<string> Info = new List<string>();
        }

        /// <summary>
        /// RC_BEAM: wskazana polilinia → strefa belki. Bez RC_BEAM — warstwa RC-BEAM + rekord z domyślnymi (H z opisu „…THK” w strefie);
        /// rzut partnerski (dolny ↔ górny, przystający obrys) — strefa partnerska znaleziona albo utworzona (kopia przesunięta).
        /// Null + komunikat, gdy polilinia nie nadaje się na strefę. Zapis w transakcji komendy (UNDO).
        /// </summary>
        public static BeamZoneContext PrepareBeamZone(Document doc, ObjectId zoneId, List<string> messages)
        {
            var db = doc.Database;
            var ctx = new BeamZoneContext();
            ObjectId slab = ObjectId.Null;
            List<Point2d> zv;
            bool isZone;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                if (!(tr.GetObject(zoneId, OpenMode.ForRead) is Polyline pl) || !GeometryHelper.IsEffectivelyClosed(pl) || pl.NumberOfVertices < 4)
                { messages.Add("Strefa belki musi być zamkniętą polilinią (min. 4 wierzchołki)."); return null; }
                if (string.Equals(pl.Layer, GaImportEngine.SlabLayer, StringComparison.OrdinalIgnoreCase) && !BeamStore.IsZone(pl))
                { messages.Add($"To polilinia warstwy {GaImportEngine.SlabLayer} (obrys płyty / uskok) — wskaż strefę belki albo użyj opcji Krawedz."); return null; }
                if (BarEntity.KindOf(pl) != null)
                { messages.Add("To pręt (polilinia z danymi pręta RC_SINGLE_BAR) — wskaż strefę belki albo użyj opcji Krawedz."); return null; }
                if (!BeamStore.IsZone(pl) && LooksLikeHole(tr, db, pl))
                { messages.Add("To otwór (polilinia z krzyżykiem przekątnych) — wskaż strefę belki albo użyj opcji Krawedz."); return null; }
                zv =GeometryHelper.GetPolylineVertices(pl);
                isZone = BeamStore.IsZone(pl);
                string s6 = isZone ? BeamStore.ReadSlab(pl) : "";
                if (XLink.TryParse(s6, out long h6) && h6 != 0 && db.TryGetObjectId(new Handle(h6), out var sid) && !sid.IsErased) slab = sid;
                if (slab.IsNull) slab = SlabContaining(tr, db, BeamStore.Centroid(pl));
                tr.Commit();
            }
            if (slab.IsNull) { messages.Add($"Strefa nie leży w żadnym obrysie płyty ({GaImportEngine.SlabLayer})."); return null; }
            slab = NibDetector.OuterOf(db, slab);

            bool picksTop = IsTopView(db, slab);
            var (partner, delta) = FindPartnerView(db, slab);   // partner = wskazany + delta
            if (partner.IsNull) messages.Add("Nie znaleziono drugiego rzutu płyty (przystający obrys) — belka tylko na tym rzucie.");

            using (var tr = db.TransactionManager.StartTransaction())
            {
                var pl = (Polyline)tr.GetObject(zoneId, OpenMode.ForWrite);
                BeamParams p;
                if (isZone) { p = BeamStore.Read(pl); ctx.HeightSource = p.Height > 0 ? "rekord strefy" : null; }
                else
                {
                    p = new BeamParams();
                    var h = HeightTextIn(tr, db, zv);
                    if (h.HasValue) { p.Height = h.Value; ctx.HeightSource = "opis w strefie"; }
                    var an0 = BeamGeometry.Analyze(zv.Select(v => (v.X, v.Y)).ToList(), null);
                    if (an0.TypicalWidth > 0) p.Width = an0.TypicalWidth;
                    BeamStore.EnsureLayer(tr, db);
                    pl.Layer = BeamStore.LayerName;
                    BeamStore.Write(tr, db, pl, p, slab.Handle.ToString());
                    messages.Add("Polilinia zamieniona na strefę belki (warstwa RC-BEAM).");
                }

                // Strefa partnerska (drugi rzut): przystająca strefa RC_BEAM albo kopia przesunięta o delta
                ObjectId other = ObjectId.Null;
                if (!partner.IsNull)
                {
                    var pv = GeometryHelper.GetPolylineVertices((Polyline)tr.GetObject(partner, OpenMode.ForRead));
                    foreach (var zid in BeamStore.ZonesOfSlab(tr, db, partner, pv))
                    {
                        var ov = ZoneVerts(tr, zid);
                        if (ov.Count == zv.Count && Congruent(ov, zv, delta)) { other = zid; break; }
                    }
                    if (other.IsNull)
                    {
                        var copy = (Polyline)pl.Clone();
                        copy.TransformBy(Matrix3d.Displacement(new Vector3d(delta.X, delta.Y, 0)));
                        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                        other = ms.AppendEntity(copy);
                        tr.AddNewlyCreatedDBObject(copy, true);
                        BeamStore.Write(tr, db, copy, p, partner.Handle.ToString());
                        messages.Add($"Utworzono strefę belki na drugim rzucie ({(picksTop ? "dolnym" : "górnym")}).");
                    }
                }
                ctx.Params = p;
                if (picksTop) { ctx.ZoneTop = zoneId; ctx.SlabTop = slab; ctx.ZoneBottom = other; ctx.SlabBottom = partner; }
                else { ctx.ZoneBottom = zoneId; ctx.SlabBottom = slab; ctx.ZoneTop = other; ctx.SlabTop = partner; }
                tr.Commit();
            }

            // Opis geometrii do okna
            var outl = ReadBeamView(db, ctx.SlabBottom.IsNull ? slab : ctx.SlabBottom);
            var bz = ctx.ZoneBottom.IsNull ? zoneId : ctx.ZoneBottom;
            List<Point2d> bzv;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction()) { bzv = ZoneVerts(tr, bz); tr.Commit(); }
            var an = BeamGeometry.Analyze(bzv.Select(v => (v.X, v.Y)).ToList(), outl?.Verts.Select(v => (v.X, v.Y)).ToList());
            foreach (var sg in an.Segments)
                ctx.Info.Add(string.Format(CultureInfo.InvariantCulture, "Odcinek {0}: L = {1:0}, szer. {2:0}, końce {3} / {4}{5}",
                    sg.AlongX ? "poziomy" : "pionowy", sg.Length, sg.Width, EndName(sg.LoKind), EndName(sg.HiKind),
                    sg.Manual ? " — DETAL RĘCZNY (" + sg.ManualReason + ")" : ""));
            foreach (var m in an.Messages.Where(m => !m.StartsWith("odcinek", StringComparison.Ordinal))) ctx.Info.Add(m);
            if (ctx.SlabTop.IsNull) ctx.Info.Add("Brak rzutu górnego — pręty górne (BEAM T) nie zostaną wygenerowane.");
            return ctx;
        }

        /// <summary>
        /// Otwór jak w FindHoles: obie przekątne prostokąta otaczającego polilinii narysowane liniami (Line, końce ≤ 5 mm od narożników).
        /// </summary>
        private static bool LooksLikeHole(Transaction tr, Database db, Polyline pl)
        {
            var v = GeometryHelper.GetPolylineVertices(pl);
            if (v.Count < 3) return false;
            double x0 = v.Min(p => p.X), y0 = v.Min(p => p.Y), x1 = v.Max(p => p.X), y1 = v.Max(p => p.Y);
            var p00 = new Point3d(x0, y0, 0); var p11 = new Point3d(x1, y1, 0);
            var p01 = new Point3d(x0, y1, 0); var p10 = new Point3d(x1, y0, 0);
            const double tol = 5.0;
            bool d1 = false, d2 = false;
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Line ln)) continue;
                bool Is(Point3d p, Point3d q) => (ln.StartPoint.DistanceTo(p) < tol && ln.EndPoint.DistanceTo(q) < tol)
                                               || (ln.StartPoint.DistanceTo(q) < tol && ln.EndPoint.DistanceTo(p) < tol);
                if (Is(p00, p11)) d1 = true;
                if (Is(p01, p10)) d2 = true;
                if (d1 && d2) return true;
            }
            return false;
        }

        private static string EndName(BeamGeometry.EndKind k)
            => k == BeamGeometry.EndKind.Edge ? "krawędź" : k == BeamGeometry.EndKind.Corner ? "narożnik L"
             : k == BeamGeometry.EndKind.Tee ? "złącze T" : "wewnętrzny";

        /// <summary>Zapis parametrów okna RC_BEAM na strefach obu rzutów (obrys [6] bez zmian).</summary>
        public static void SaveBeamParams(Database db, BeamZoneContext ctx)
        {
            using var tr = db.TransactionManager.StartTransaction();
            foreach (var (z, s) in new[] { (ctx.ZoneBottom, ctx.SlabBottom), (ctx.ZoneTop, ctx.SlabTop) })
            {
                if (z.IsNull || z.IsErased || !(tr.GetObject(z, OpenMode.ForWrite) is Polyline pl)) continue;
                string slab = BeamStore.ReadSlab(pl);
                if (string.IsNullOrEmpty(slab) && !s.IsNull) slab = s.Handle.ToString();
                BeamStore.Write(tr, db, pl, ctx.Params, slab);
            }
            tr.Commit();
        }

        /// <summary>Największy zamknięty obrys płyty (SD-PILED-RAFT) zawierający punkt; Null gdy brak.</summary>
        private static ObjectId SlabContaining(Transaction tr, Database db, Point2d p)
        {
            ObjectId best = ObjectId.Null; double bestA = 0;
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl) || !GeometryHelper.IsEffectivelyClosed(pl)) continue;
                if (!string.Equals(pl.Layer, GaImportEngine.SlabLayer, StringComparison.OrdinalIgnoreCase)) continue;
                var v = GeometryHelper.GetPolylineVertices(pl);
                if (!GeometryHelper.IsPointInsidePolygon(v, p)) continue;
                double a = Math.Abs(pl.Area);
                if (a > bestA) { bestA = a; best = id; }
            }
            return best;
        }

        /// <summary>Wysokość belki z opisu „450THK” (DBText / MText) w strefie; null gdy brak.</summary>
        private static int? HeightTextIn(Transaction tr, Database db, List<Point2d> zone)
        {
            var hs = new List<int>();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                var o = tr.GetObject(id, OpenMode.ForRead);
                List<string> lines; Point3d ins;
                if (o is MText mt) { lines = GaImportEngine.TextLines(mt); ins = mt.Location; }
                else if (o is DBText dt) { lines = new List<string> { dt.TextString ?? "" }; ins = dt.Position; }
                else continue;
                if (!GeometryHelper.IsPointInsidePolygon(zone, new Point2d(ins.X, ins.Y))) continue;
                foreach (var l in lines) if (BeamGeometry.ParseThk(l) is int h) hs.Add(h);
            }
            return hs.Count > 0 ? hs.GroupBy(h => h).OrderByDescending(g => g.Count()).First().Key : (int?)null;
        }

        /// <summary>
        /// RC_BEAM → Krawedz: strefa belki wzdłuż krawędzi obrysu między dwoma punktami: od linii uskoku (nib z NibDetector,
        /// bez nibu — od krawędzi) w głąb płyty na szerokość <paramref name="width"/>. Zwraca nową polilinię (RC-BEAM, bez rekordu).
        /// </summary>
        public static ObjectId CreateZoneFromEdge(Database db, ObjectId slabId, Point2d p1, Point2d p2, double width, List<string> messages)
        {
            var outer = NibDetector.OuterOf(db, slabId);
            var nib = NibDetector.Detect(db, outer);
            List<Point2d> v;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                if (!(tr.GetObject(outer, OpenMode.ForRead) is Polyline pl)) return ObjectId.Null;
                v = GeometryHelper.SquaredOrSame(GeometryHelper.GetPolylineVertices(pl));
                tr.Commit();
            }
            // krawędź osiowa najbliższa obu punktom
            double bestD = double.MaxValue; bool vert = false; double c = 0, lo = 0, hi = 0;
            for (int i = 0; i < v.Count; i++)
            {
                var a = v[i]; var b = v[(i + 1) % v.Count];
                bool isV = Math.Abs(a.X - b.X) < 1e-6, isH = Math.Abs(a.Y - b.Y) < 1e-6;
                if (!isV && !isH) continue;
                double cc = isV ? a.X : a.Y, l = isV ? Math.Min(a.Y, b.Y) : Math.Min(a.X, b.X), h = isV ? Math.Max(a.Y, b.Y) : Math.Max(a.X, b.X);
                double d = 0;
                foreach (var q in new[] { p1, p2 })
                {
                    double t = isV ? q.Y : q.X, n = isV ? q.X : q.Y;
                    double dt = t < l ? l - t : t > h ? t - h : 0;
                    d += Math.Sqrt(dt * dt + (n - cc) * (n - cc));
                }
                if (d < bestD) { bestD = d; vert = isV; c = cc; lo = l; hi = h; }
            }
            if (bestD == double.MaxValue) { messages.Add("Obrys płyty bez krawędzi osiowych."); return ObjectId.Null; }
            double mid = (lo + hi) / 2;
            int inward = GeometryHelper.IsPointInsidePolygon(v, vert ? new Point2d(c + 1, mid) : new Point2d(mid, c + 1)) ? 1 : -1;
            double t0 = Math.Max(lo, Math.Min(vert ? p1.Y : p1.X, vert ? p2.Y : p2.X));
            double t1 = Math.Min(hi, Math.Max(vert ? p1.Y : p1.X, vert ? p2.Y : p2.X));
            if (t1 - t0 < 100) { messages.Add("Za krótki odcinek krawędzi (< 100 mm)."); return ObjectId.Null; }
            double nibW = 0;
            if (nib != null)
                foreach (var e in nib.Edges)
                    if (e.Vertical == vert && Math.Abs(e.OuterCoord - c) < 1.0 && e.Hi > t0 && e.Lo < t1) { nibW = e.Width; break; }
            double c0 = c + inward * nibW, c1 = c + inward * (nibW + width);
            var pts = vert
                ? new[] { new Point2d(c0, t0), new Point2d(c1, t0), new Point2d(c1, t1), new Point2d(c0, t1) }
                : new[] { new Point2d(t0, c0), new Point2d(t1, c0), new Point2d(t1, c1), new Point2d(t0, c1) };
            using (var tr = db.TransactionManager.StartTransaction())
            {
                BeamStore.EnsureLayer(tr, db);
                var pl = new Polyline { Layer = BeamStore.LayerName };
                for (int i = 0; i < 4; i++) pl.AddVertexAt(i, pts[i], 0, 0, 0);
                pl.Closed = true;
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var id = ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                tr.Commit();
                messages.Add(string.Format(CultureInfo.InvariantCulture, "Strefa belki wzdłuż krawędzi: {0:0} × {1:0} (nib {2:0}).", t1 - t0, width, nibW));
                return id;
            }
        }

        /// <summary>UB 03 rzutu dolnego płyty: kierunek prętów, zakres prętów w poprzek (c0..c1) i położenia prętów wzdłuż krawędzi.</summary>
        private static List<(bool barsAlongX, double c0, double c1, List<double> pos)> ReadUb03(Database db, BeamView view)
        {
            var res = new List<(bool, double, double, List<double>)>();
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                string vh = view.OuterId.Handle.ToString();
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    var bar = BarBlockEngine.ReadXData(br);
                    if (bar == null || string.IsNullOrEmpty(bar.Mark) || !bar.Mark.EndsWith(" " + UBSuffix)) continue;
                    if (SingleBarEngine.ExtractPosNr(bar.Mark) != NibUBPosNr || Math.Abs(br.Rotation) > 1e-6) continue;
                    string tag = ReadSlabTag(br);
                    bool mine = tag != null ? SameHandle(tag, vh)
                                            : GeometryHelper.IsPointInsidePolygon(view.Verts, new Point2d(br.Position.X, br.Position.Y));
                    if (!mine) continue;
                    bool x = !string.Equals(bar.Direction, "Y", StringComparison.OrdinalIgnoreCase);
                    double scan0 = x ? br.Position.Y : br.Position.X, along0 = x ? br.Position.X : br.Position.Y;
                    var pos = new List<double>();
                    for (int i = 0; i < Math.Max(1, bar.Count); i++) pos.Add(scan0 + i * bar.Spacing);
                    res.Add((x, along0, along0 + bar.LengthA, pos));
                }
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.ReadUb03", ex); }
            return res;
        }

        /// <summary>
        /// Rozkłady T IN NIB rzutu (zwykle górnego) w układzie rzutu dolnego (minus shift): kierunek prętów, zakres wzdłuż (a0..a1)
        /// i położenia prętów w poprzek. Tylko odczyt.
        /// </summary>
        private static List<(bool barsAlongX, double a0, double a1, List<double> pos)> ReadTinNib(Database db, BeamView view, Vector2d shift)
        {
            var res = new List<(bool, double, double, List<double>)>();
            if (view == null) return res;
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                string vh = view.OuterId.Handle.ToString();
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    var bar = BarBlockEngine.ReadXData(br);
                    if (bar == null || string.IsNullOrEmpty(bar.Mark) || !bar.Mark.TrimEnd().EndsWith(" " + NibTopSuffix)) continue;
                    if (Math.Abs(br.Rotation) > 1e-6) continue;
                    string tag = ReadSlabTag(br);
                    bool mine = tag != null ? SameHandle(tag, vh)
                                            : GeometryHelper.IsPointInsidePolygon(view.Verts, new Point2d(br.Position.X, br.Position.Y));
                    if (!mine) continue;
                    bool x = !string.Equals(bar.Direction, "Y", StringComparison.OrdinalIgnoreCase);
                    double px = br.Position.X - shift.X, py = br.Position.Y - shift.Y;
                    double scan0 = x ? py : px, along0 = x ? px : py;
                    var pos = new List<double>();
                    for (int i = 0; i < Math.Max(1, bar.Count); i++) pos.Add(scan0 + i * bar.Spacing);
                    res.Add((x, along0, along0 + bar.LengthA, pos));
                }
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.ReadTinNib", ex); }
            return res;
        }

        /// <summary>Czy wzdłuż nibu odcinka (oś BN w poprzek across, zakres lo..hi) leży rozkład T IN NIB (pręty równoległe, ≤ 250 w poprzek, zakresy nachodzą).</summary>
        private static bool TinAlong(List<(bool barsAlongX, double a0, double a1, List<double> pos)> tin, BeamGeometry.Segment s,
                                     double across, double lo, double hi)
        {
            if (tin == null) return false;
            foreach (var (barsX, a0, a1, pos) in tin)
            {
                if (barsX != s.AlongX) continue;
                if (Math.Min(a1, hi) - Math.Max(a0, lo) < 1.0) continue;
                if (pos.Any(v => Math.Abs(v - across) <= 250.0)) return true;
            }
            return false;
        }

        /// <summary>
        /// Położenia UB 03 wzdłuż odcinka (pręty UB prostopadłe do osi belki, zasięg prętów przecina belkę) — rozkład pokrywający
        /// największą część odcinka; null gdy brak.
        /// </summary>
        private static List<double> Ub03Along(List<(bool barsAlongX, double c0, double c1, List<double> pos)> ub03, BeamGeometry.Segment s)
        {
            List<double> best = null; int bestN = 0;
            foreach (var (barsX, c0, c1, pos) in ub03)
            {
                if (barsX == s.AlongX) continue;                                // pręty UB prostopadłe do osi
                if (c1 < s.AcrossLo - 1 || c0 > s.AcrossHi + 1) continue;       // zasięg UB przecina belkę
                var inside = pos.Where(v => v >= s.OwnLo - 1 && v <= s.OwnHi + 1).ToList();
                if (inside.Count > bestN) { bestN = inside.Count; best = pos; }
            }
            return bestN >= 2 ? best : null;
        }
    }
}
