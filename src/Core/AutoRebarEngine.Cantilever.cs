using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Wsporniki — etap 2 (decyzje Bartka 2026-10-21): pręty wspornika (strefa RC-CANTILEVER z rekordem RC_CANT) w detalu 1:25 —
    /// ta sama ścieżka co belki (K9: PrepareBeamDetail / FinishBeamDetail, ramka i „DETAIL 'n'” na rzucie dolnym, opisy 0,5, szablony
    /// obok detalu; rozkłady znakowane RC_AUTOREBAR_BEAM = handle wspornika — ponowne generowanie usuwa tylko jego pręty).
    /// Plan prętów — CantileverRules.PlanBars (U-bary H16 w kolumnach wg szerokości, strzemiona 4-leg wg Speedeck_Anchorage, pręty
    /// proste: ramię &gt; 1300 → H16 między U-barami; bez strefy przy palu → H12 1000 za krawędź). Kody belki (BL / BU / BM — seria dolna).
    /// </summary>
    public static partial class AutoRebarEngine
    {
        private const string CantTag = "[RC CANT]";

        public sealed class CantRunResult
        {
            public int Cantilevers, Links, UBars, Straight;
            public List<string> Messages = new List<string>();
        }

        /// <summary>
        /// RC_GENERATE → Cantilever: pręty wszystkich wsporników rzutu dolnego (strefy RC_CANT nachodzące na obrys
        /// <paramref name="bottomSlabId"/>). Wspornik bez wysokości GB — pominięty z komunikatem.
        /// </summary>
        public static CantRunResult GenerateCantilevers(Document doc, ObjectId bottomSlabId)
        {
            var db = doc.Database;
            var ed = doc.Editor;
            var res = new CantRunResult();
            void Msg(string m) { res.Messages.Add(m); ed.WriteMessage($"\n{CantTag} {m}"); }

            // obrys przygotowany poprzednią wersją (przecięty na nasadzie) — języczek z powrotem, jak w GA (uwaga Bartka 2026-10-22)
            try
            {
                var rm = new List<string>();
                using (doc.LockDocument()) CantileverEngine.RestoreOutlineTabs(db, new[] { bottomSlabId }, rm);
                foreach (var m in rm) Msg(m);
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.GenerateCantilevers.RestoreTabs", ex); }

            var bottom = ReadBeamView(db, bottomSlabId);
            if (bottom == null) { Msg("Obrys rzutu dolnego nie jest zamkniętą polilinią — przerwano."); return res; }

            var cants = new List<(ObjectId id, string handle, List<Point2d> verts, CantileverEngine.Data data)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl) || !CantileverEngine.IsStub(pl)) continue;
                    var v = GeometryHelper.GetPolylineVertices(pl);
                    if (!TouchesOutline(v, bottom.Verts)) continue;
                    cants.Add((id, pl.Handle.ToString(), v, CantileverEngine.Read(pl)));
                }
                tr.Commit();
            }
            if (cants.Count == 0)
            {
                Msg("Na płycie nie ma wsporników (warstwa RC-CANTILEVER) — RC_CANTILEVER albo ponowne Prepare GA.");
                return res;
            }

            var saveSlab = _currentSlabHandle;
            var saveHoles = _currentHoles;
            try
            {
                using (doc.LockDocument())
                {
                    _currentSlabHandle = null;              // rozkłady wspornika bez znacznika płyty (znacznik RC_AUTOREBAR_BEAM)
                    _currentHoles = new List<HoleBox>();
                    InitLabelOccupancy(db);
                    foreach (var c in cants)
                    {
                        try { GenerateOneCantilever(doc, bottom, c.id, c.handle, c.verts, c.data, res, Msg); }
                        catch (System.Exception ex)
                        {
                            Log.Error("AutoRebar.GenerateCantilevers", ex);
                            Msg($"wspornik {c.handle}: błąd — {ex.Message}");
                        }
                        finally { ResetBeamDetailOverrides(); }
                    }
                }
            }
            finally
            {
                _currentSlabHandle = saveSlab;
                _currentHoles = saveHoles ?? new List<HoleBox>();
                ResetBeamDetailOverrides();
            }
            Msg($"Wsporniki: {res.Cantilevers} — strzemiona: {res.Links} rozkł., U-bary: {res.UBars}, pręty proste: {res.Straight}.");
            return res;
        }

        /// <summary>Strefa wspornika nachodzi na obrys albo leży nasadą na jego krawędzi (wierzchołek w środku / na obrysie i poza nim).</summary>
        private static bool TouchesOutline(List<Point2d> zone, List<Point2d> outline)
        {
            bool inOrOn = false, outside = false;
            foreach (var p in zone)
            {
                if (GeometryHelper.IsPointInsidePolygon(outline, p) || DistToPolygon(outline, p) <= 1.0) inOrOn = true;
                else outside = true;
            }
            return inOrOn && outside;
        }

        private static double DistToPolygon(List<Point2d> poly, Point2d p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < poly.Count; i++)
            {
                Point2d a = poly[i], b = poly[(i + 1) % poly.Count];
                var ab = b - a; double L2 = ab.DotProduct(ab);
                double t = L2 > 1e-9 ? Math.Max(0, Math.Min(1, (p - a).DotProduct(ab) / L2)) : 0;
                best = Math.Min(best, (a + ab * t - p).Length);
            }
            return best;
        }

        private static void GenerateOneCantilever(Document doc, BeamView bottom, ObjectId zid, string handle, List<Point2d> zone,
                                                  CantileverEngine.Data data, CantRunResult res, Action<string> msg)
        {
            var db = doc.Database;
            string name = $"wspornik {handle}";
            data = data ?? new CantileverEngine.Data();

            // ── geometria (wspólna z RC_SECTION — CantGeometry) ──
            var g = CantGeometry(db, bottom.OuterId, bottom.Verts, zone, data);
            if (g.Error != null) { msg($"{name}: {g.Error}"); return; }
            foreach (var m in g.Notes) msg($"{name}: {m}");
            bool alongX = g.AlongX;
            int sign = g.Sign;
            double c0 = g.C0, c1 = g.C1, end = g.End, root = g.Root, rootEdge = g.RootEdge;
            double reach = g.Reach, width = g.Width, h = g.H, rootLen = g.RootLen, rootH = g.RootH;
            bool stepped = g.Stepped;

            // wspornik poziomy (oś X): strefa przy palu — jedna pozycja strzemion w obu kierunkach, A − 15, co 150 (decyzja Bartka 2026-10-23);
            // pionowy — zagnieżdżone jak dotąd
            var plan = CantileverRules.PlanBars(reach, width, h, rootLen, rootH, singleRootLink: alongX);
            CantileverRules.NestRootLinksGlobal(plan, alongX);   // bez zmian przy jednej pozycji (SingleRootLink)
            foreach (var m in plan.Messages) msg($"{name}: {m}");
            double W(double s) => root + sign * s;                         // s (lokalne) → współrzędna wzdłuż osi
            Point2d P(double along, double across) => alongX ? new Point2d(along, across) : new Point2d(across, along);

            // ── stare pręty wspornika ──
            var old = new List<(ObjectId, ObjectId)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                old.AddRange(BeamStore.FindGenerated(tr, db, handle, zone));
                tr.Commit();
            }
            EraseOldDistributions(db, old);
            if (old.Count > 0) msg($"{name}: usunięto poprzednie rozkłady ({old.Count}).");
            res.Cantilevers++;

            // ── detal 1:25: obrys z języczkiem (linie betonu wspornika, ramka obejmuje wspornik) ──
            var stubU = new CantileverRules.UShape
            {
                A = Tup(P(rootEdge, c0)), C1 = Tup(P(end, c0)), C2 = Tup(P(end, c1)), B = Tup(P(rootEdge, c1)),
                Width = width, Depth = Math.Abs(end - rootEdge), Out = alongX ? (sign, 0.0) : (0.0, sign)
            };
            var withTab = CantileverRules.InsertTab(bottom.Verts.Select(Tup).ToList(), stubU).Select(t => new Point2d(t.X, t.Y)).ToList();
            var dv = new BeamView { OuterId = bottom.OuterId, Verts = withTab, Box = GeometryHelper.VerticesBbox(withTab) };
            // linia uskoku w poprzek wspornika (strefa przy palu | wspornik) — w detalu zawsze, także gdy nib jej tam nie ma
            var stepLines = stepped
                ? new List<((double X, double Y) A, (double X, double Y) B)> { (Tup(P(root, c0)), Tup(P(root, c1))) }
                : null;
            // uwaga Bartka 2026-10-21: ramka jak przy belce — po prostu offset strefy (bez przycinania do obrysu z języczkiem)
            var tg = PrepareBeamDetail(doc, dv, null, handle, zone, m => msg(m.Replace("belka", "wspornik")), offsetFrame: true,
                                       extraLines: stepLines);
            if (tg == null) return;
            CopyCantileverLabels(db, tg, zone, m => msg($"{name}: {m}"));
            SetBeamDetailOverrides(tg);

            // ── strzemiona 4-leg: wysięg — 63 (poza płytą), strefa przy palu — 51 w obu kierunkach (decyzja Bartka 2026-10-21) ──
            double cover = BeamGeometry.DefaultCoverSide;
            foreach (var L in plan.Links)
            {
                if (!string.IsNullOrEmpty(L.Note)) msg($"{name}: {L.Note}");
                bool four = L.Legs == 4;
                var tpl = FindOrCreateBeamTemplate(db, doc.Editor, bottom.Box, top: false, d: L.Dia, shape: L.Shape, a: L.A, b: L.B,
                                                   code: BeamGeometry.CodeLinks);
                if (tpl.id.IsNull || L.Count <= 0) continue;
                double s0, s1, k0, k1Full;
                bool barsX;
                if (!L.AlongAxis)
                {
                    // pręty w poprzek osi wysięgu, rozstaw wzdłuż osi
                    double f = W(L.First), l = W(L.First + (L.Count - 1) * L.Step);
                    s0 = Math.Min(f, l); s1 = Math.Max(f, l);
                    k0 = c0 + cover; k1Full = c1 - cover;
                    barsX = !alongX;
                }
                else
                {
                    // drugi kierunek (strefa przy palu): pręty wzdłuż osi na L0…L1, rozstaw w poprzek (t od boku c0)
                    s0 = c0 + L.First; s1 = c0 + L.First + (L.Count - 1) * L.Step;
                    double a0L = Math.Min(W(L.L0), W(L.L1)), a1L = Math.Max(W(L.L0), W(L.L1));
                    k0 = a0L + cover; k1Full = a1L - cover;
                    barsX = alongX;
                }
                double k1 = four ? k0 + L.B : k1Full;
                double x0 = barsX ? k0 : s0, x1 = barsX ? k1 : s1;
                double y0 = barsX ? s0 : k0, y1 = barsX ? s1 : k1;
                int n = L.Count;
                if (BeamDistribution(db, dv, tpl, x0, y0, x1, y1, barsX, L.Dia, L.B, L.Spacing, n > 1 ? L.Step : double.NaN,
                                     BeamGeometry.CodeLinks, BeamGeometry.SuffixLinks, noSpacing: false, shape: tpl.bar.ShapeCode, repr: 0,
                                     zoneHandle: handle,
                                     customize: four ? (Action<BarData>)(bar =>
                                     {
                                         bar.CountDisplay = 2 * n;
                                         bar.PairShift = Math.Max(0, (k1Full - k0) - L.B);
                                     }) : null, tg: tg))
                    res.Links++;
                msg($"{name}: strzemiona H{L.Dia} {L.Shape} A = {L.A:F0}, B = {L.B:F0}{(four ? " (2 zachodzące — 4-leg)" : "")}, {n} szt. co {L.Step:F0} " +
                    (L.AlongAxis ? $"(w poprzek, s {L.L0:F0}…{L.L1:F0})." : $"(s {L.S0:F0}…{L.S1:F0})."));
            }

            // ── U-bary H16 (kształt 21), po jednym w kolumnie prętów ──
            foreach (var u in plan.UBars)
            {
                double ls = W(u.LineStart), le = W(u.LineEnd), back = W(u.Back);
                var r = new BeamGeometry.VUBarRun
                {
                    Kind = "C", Dia = u.Dia, Count = plan.Columns, A = u.A, B = u.B, C = u.A, AlongX = alongX,
                    Across0 = c0 + plan.T0, Spacing = plan.ColumnSpacing,
                    Back = back, LineStart = Math.Min(ls, le), LineLength = u.A,
                    BendAtStart = Math.Abs(back - Math.Min(ls, le)) < 1.0, Overlap = double.NaN
                };
                if (BeamVerticalUBar(db, doc.Editor, dv, r, CantileverRules.NominalColumnSpacing, handle, tg)) res.UBars++;
            }

            // ── pręty boczne (zasady belek, uwaga Bartka 2026-10-22): UB SIDE na końcach + pręty proste SIDE ──
            if (plan.SideLevels > 0)
            {
                double sa0 = c0 + plan.SideT0, sa1 = c0 + plan.SideT1;
                foreach (var u in plan.SideUBars)
                {
                    double ls = W(u.LineStart), le = W(u.LineEnd), back = W(u.Back);
                    var r = new BeamGeometry.SideUBarRun
                    {
                        Kind = "E", Dia = u.Dia, Levels = plan.SideLevels, A = u.A, B = u.B, C = u.A, AlongX = alongX,
                        Across0 = sa0, Across1 = sa1, Back = back, LineStart = Math.Min(ls, le), LineLength = u.A,
                        BendAtStart = Math.Abs(back - Math.Min(ls, le)) < 1.0
                    };
                    if (BeamSideUBar(db, doc.Editor, dv, r, handle, BeamGeometry.UbSideLabelSpacing, tg, standardLeader: true)) res.UBars++;
                }
                int ks = 0;
                foreach (var st in plan.SideStraight)
                {
                    double s0 = Math.Min(W(st.S0), W(st.S1)), s1 = Math.Max(W(st.S0), W(st.S1));
                    double len = s1 - s0;
                    var tpl = FindOrCreateBeamTemplate(db, doc.Editor, bottom.Box, top: false, d: st.Dia, shape: "00", a: len, b: 0,
                                                       code: BeamGeometry.CodeSide);
                    if (tpl.id.IsNull) continue;
                    double x0 = alongX ? s0 : sa0, x1 = alongX ? s1 : sa1;
                    double y0 = alongX ? sa0 : s0, y1 = alongX ? sa1 : s1;
                    int total = 2 * plan.SideLevels;
                    if (BeamDistribution(db, dv, tpl, x0, y0, x1, y1, alongX, st.Dia, len, sa1 - sa0, sa1 - sa0,
                                         BeamGeometry.CodeSide, BeamGeometry.SuffixSide, noSpacing: true, shape: null, repr: ks++,
                                         zoneHandle: handle, customize: bar => bar.CountDisplay = total, tg: tg))
                        res.Straight++;
                }
            }

            // ── pręty proste (górą i dołem jedną pozycją „2xn … B+T”) ──
            int k = 0;
            foreach (var st in plan.Straight)
            {
                double s0 = Math.Min(W(st.S0), W(st.S1)), s1 = Math.Max(W(st.S0), W(st.S1));
                double len = s1 - s0;
                var tpl = FindOrCreateBeamTemplate(db, doc.Editor, bottom.Box, top: false, d: st.Dia, shape: "00", a: len, b: 0,
                                                   code: BeamGeometry.CodeBoth);
                if (tpl.id.IsNull) continue;
                double f0 = c0 + plan.T0, fl = f0 + (plan.Columns - 1) * plan.ColumnSpacing;
                double x0 = alongX ? s0 : f0, x1 = alongX ? s1 : fl;
                double y0 = alongX ? f0 : s0, y1 = alongX ? fl : s1;
                int total = 2 * plan.Columns;
                if (BeamDistribution(db, dv, tpl, x0, y0, x1, y1, alongX, st.Dia, len, CantileverRules.NominalColumnSpacing,
                                     plan.Columns > 1 ? plan.ColumnSpacing : double.NaN, BeamGeometry.CodeBoth, BeamGeometry.SuffixBoth,
                                     noSpacing: plan.Columns <= 1, shape: null, repr: k++, zoneHandle: handle,
                                     customize: bar => bar.CountDisplay = total, tg: tg))
                    res.Straight++;
            }

            FinishBeamDetail(doc, tg, zid, ObjectId.Null, m => msg(m.Replace("belka", "wspornik")), dv.Verts, new Vector2d());
            msg($"{name}: GB {width:F0} × {h:F0}, wysięg {reach:F0}" +
                (rootLen > 0 ? $", strefa przy palu {rootH:F0} × {rootLen:F0}" : ", bez strefy przy palu") +
                $", kolumny prętów: {plan.Columns} (co {plan.ColumnSpacing:F0}).");
        }

        /// <summary>Geometria wspornika w układzie osi wysięgu (wspólna: RC_GENERATE → Cantilever i RC_SECTION).</summary>
        internal sealed class CantGeom
        {
            public bool AlongX, Stepped;
            public int Sign;
            /// <summary>Wzdłuż osi: koniec wspornika, uskok (s = 0), krawędź płyty, drugi koniec strefy; w poprzek: boki.</summary>
            public double End, Root, RootEdge, Other, C0, C1;
            public double Reach, InSlab, Width, StepIn, RootLen, RootH, H;
            public string Error;
            public List<string> Notes = new List<string>();
            /// <summary>Współrzędna wzdłuż osi dla s (0 — uskok, + — w stronę końca).</summary>
            public double W(double s) => Root + Sign * s;
        }

        /// <summary>
        /// Oś wysięgu z kierunku [7] (albo z położenia względem obrysu), krawędź płyty na nasadzie (przecięcie osi strefy z obrysem
        /// <paramref name="outline"/> do obliczeń), uskok — linia nibu ≤ 300 w głąb (uwaga Bartka 2026-10-21), wysięg od uskoku,
        /// strefa przy palu = [6] − uskok (≤ część w płycie), grubość strefy z „xxxTHK” (brak — h + 75). Error ≠ null — wspornik pominięty.
        /// </summary>
        internal static CantGeom CantGeometry(Database db, ObjectId outerId, List<Point2d> outline, List<Point2d> zone, CantileverEngine.Data data)
        {
            var g = new CantGeom();
            data = data ?? new CantileverEngine.Data();
            double zx0 = zone.Min(v => v.X), zx1 = zone.Max(v => v.X), zy0 = zone.Min(v => v.Y), zy1 = zone.Max(v => v.Y);
            string dir = data.Dir ?? "";
            if (dir.Length == 2) { g.AlongX = dir[1] == 'X'; g.Sign = dir[0] == '-' ? -1 : 1; }
            else
            {
                // rekord bez kierunku — z położenia względem obrysu: koniec poza obrysem
                g.AlongX = (zx1 - zx0) > (zy1 - zy0);
                var c = new Point2d((zx0 + zx1) / 2, (zy0 + zy1) / 2);
                var hi = g.AlongX ? new Point2d(zx1 - 1, c.Y) : new Point2d(c.X, zy1 - 1);
                g.Sign = GeometryHelper.IsPointInsidePolygon(outline, hi) ? -1 : 1;
            }
            double a0 = g.AlongX ? zx0 : zy0, a1 = g.AlongX ? zx1 : zy1;
            g.C0 = g.AlongX ? zy0 : zx0; g.C1 = g.AlongX ? zy1 : zx1;
            g.End = g.Sign > 0 ? a1 : a0; g.Other = g.Sign > 0 ? a0 : a1;
            double mid = (g.C0 + g.C1) / 2.0;
            var cuts = g.AlongX ? GeometryHelper.FindIntersectionsH(outline, mid) : GeometryHelper.FindIntersectionsV(outline, mid);
            var inRange = cuts.Where(x => x >= Math.Min(a0, a1) - 1.0 && x <= Math.Max(a0, a1) + 1.0).ToList();
            if (inRange.Count == 0) { g.Error = "nie znaleziono krawędzi płyty na nasadzie — pominięty."; return g; }
            g.Root = g.RootEdge = inRange.OrderBy(x => Math.Abs(x - g.End)).First();
            try
            {
                var nib = NibDetector.Detect(db, outerId);
                if (nib?.Inner != null && nib.Inner.Count > 2)
                {
                    var ncuts = g.AlongX ? GeometryHelper.FindIntersectionsH(nib.Inner, mid) : GeometryHelper.FindIntersectionsV(nib.Inner, mid);
                    var st = CantileverRules.StepCut(ncuts, g.Root, g.End);
                    if (st.HasValue) { g.Root = st.Value; g.Stepped = true; }
                }
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.CantGeometry.Step", ex); }
            g.Reach = Math.Abs(g.End - g.Root); g.InSlab = Math.Abs(g.Root - g.Other); g.Width = g.C1 - g.C0;
            if (g.Reach < CantileverRules.MinDepth - 1.0) { g.Error = $"wysięg {g.Reach:F0} — za mały, pominięty."; return g; }
            g.H = data.Height;
            if (g.H <= 0) { g.Error = "brak wysokości GB (opis „GB-…x…mm deep”) — pominięty; uzupełnij opis i uruchom RC_CANTILEVER / Prepare GA."; return g; }
            g.StepIn = Math.Abs(g.Root - g.RootEdge);
            if (g.Stepped) g.Notes.Add($"uskok (linia nibu) {g.StepIn:F0} od krawędzi płyty — wspornik {g.Reach:F0} od uskoku.");
            g.RootLen = data.RootLen > g.StepIn + 1.0 ? Math.Min(data.RootLen - g.StepIn, g.InSlab > 1.0 ? g.InSlab : data.RootLen) : 0;
            g.RootH = g.RootLen > 0 ? (data.RootThk > 0 ? data.RootThk : g.H + 75) : 0;
            if (g.RootLen > 0 && data.RootThk <= 0) g.Notes.Add($"strefa przy palu bez opisu „xxxTHK” — przyjęto {g.RootH:F0} (h + 75).");
            return g;
        }

        private static (double X, double Y) Tup(Point2d p) => (p.X, p.Y);

        /// <summary>Środek obwiedni wierzchołków.</summary>
        private static Point2d CentroidOf(List<Point2d> v)
            => new Point2d((v.Min(p => p.X) + v.Max(p => p.X)) / 2.0, (v.Min(p => p.Y) + v.Max(p => p.Y)) / 2.0);

        /// <summary>
        /// Opisy wspornika z rzutu w detalu (uwaga Bartka 2026-10-21 — screen 3, wzór PLOT 28): odnośniki (MLeader) „GB-…x…mm deep / TOC=…”
        /// i „xxxTHK” z grotem w strefie wspornika (z częścią w płycie) — kopia w detalu: grot przesunięty o Shift, tekst w połowie odległości
        /// (skala opisów detalu 0,5), wysokość tekstu i grot ×0,5 (1:25 — 62,5 jak w rysunkach biura). Liczba skopiowanych.
        /// </summary>
        private static int CopyCantileverLabels(Database db, BeamDrawTarget tg, List<Point2d> zone, Action<string> msg)
        {
            var src = new List<(Point3d tip, Point3d txt, string text)>();
            try
            {
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is MLeader ml)) continue;
                        if (BeamStore.ReadDetail(ml) != null) continue;
                        var lines = GaImportEngine.MLeaderLines(ml);
                        if (!lines.Any(l => CantileverRules.ParseGb(l) != null || BeamGeometry.ParseThk(l) != null)) continue;
                        var tip = GaImportEngine.MLeaderTip(ml);
                        if (tip == null) continue;
                        var t2 = new Point2d(tip.Value.X, tip.Value.Y);
                        if (!GeometryHelper.IsPointInsidePolygon(zone, t2) && DistToPolygon(zone, t2) > 50.0) continue;
                        Point3d loc;
                        try { loc = ml.TextLocation; }
                        catch (System.Exception ex) { Log.Error("AutoRebar.CopyCantileverLabels.Loc", ex); continue; }
                        string text = string.Join("\\P", lines);
                        if (src.Any(s => s.text == text)) continue;
                        src.Add((tip.Value, loc, text));
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("AutoRebar.CopyCantileverLabels", ex);
                msg($"opisy GB / THK w detalu — błąd odczytu: {ex.Message}");
                return 0;
            }

            int n = 0;
            double sc = tg.AnnotScale;
            foreach (var (tip, txt, text) in src)
            {
                try
                {
                    var tipD = new Point3d(tip.X + tg.Shift.X, tip.Y + tg.Shift.Y, 0);
                    var txtD = new Point3d(tipD.X + (txt.X - tip.X) * sc, tipD.Y + (txt.Y - tip.Y) * sc, 0);
                    var lid = SingleBarEngine.PlaceNoteLeader(db, new[] { tipD }, txtD, text, BeamDetailTextHeight, 2, NoteTextStyle,
                                                              BeamStore.DetailLayer);
                    if (lid.IsNull) continue;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var ml = (MLeader)tr.GetObject(lid, OpenMode.ForWrite);
                        try { ml.ArrowSize = ml.ArrowSize * sc; ml.DoglegLength = ml.DoglegLength * sc; }
                        catch (System.Exception ex) { Log.Error("AutoRebar.CopyCantileverLabels.Arrow", ex); }
                        BeamStore.TagDetail(tr, db, ml, tg.ZoneHandle, tg.Number, BeamStore.RoleText);
                        tr.Commit();
                    }
                    n++;
                }
                catch (System.Exception ex) { Log.Error("AutoRebar.CopyCantileverLabels.Place", ex); }
            }
            msg(n > 0 ? $"opisy z rzutu w detalu: {n} ({string.Join("; ", src.Select(s => s.text.Replace("\\P", " ")))})."
                      : "na rzucie nie ma odnośnika „GB-…” / „xxxTHK” z grotem we wsporniku — detal bez tych opisów.");
            return n;
        }

        /// <summary>
        /// UB 03 (nib) przy wspornikach (uwaga Bartka 2026-10-21, screen 1): na odcinku krawędzi, przez który przechodzi wspornik (szerokość
        /// strefy RC-CANTILEVER), UB 03 nie ma — UB 01/02 bez zmian. Zakresy [lo, hi] wzdłuż krawędzi o współrzędnej
        /// <paramref name="edgeCoord"/> (krawędź pionowa — <paramref name="vertical"/>: x = edgeCoord, zakres w Y).
        /// </summary>
        internal static List<(double Lo, double Hi)> CantileverSpansOnEdge(Database db, bool vertical, double edgeCoord)
        {
            var res = new List<(double Lo, double Hi)>();
            try
            {
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl) || !CantileverEngine.IsStub(pl)) continue;
                        var v = GeometryHelper.GetPolylineVertices(pl);
                        if (v.Count < 3) continue;
                        double x0 = v.Min(p => p.X), x1 = v.Max(p => p.X), y0 = v.Min(p => p.Y), y1 = v.Max(p => p.Y);
                        double n0 = vertical ? x0 : y0, n1 = vertical ? x1 : y1;     // w poprzek krawędzi
                        if (edgeCoord <= n0 + 1.0 || edgeCoord >= n1 - 1.0) continue;
                        // wspornik przechodzi przez krawędź tylko szerokością (dłuższy wymiar wzdłuż osi wysięgu nie jest wzdłuż krawędzi)
                        res.Add(vertical ? (y0, y1) : (x0, x1));
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.CantileverSpansOnEdge", ex); }
            return res;
        }

        /// <summary>Odcinek [lo, hi] bez zakresów <paramref name="cut"/> (części krótsze niż 1 mm pomijane).</summary>
        internal static List<(double Lo, double Hi)> SubtractSpans(double lo, double hi, IEnumerable<(double Lo, double Hi)> cut)
        {
            var parts = new List<(double Lo, double Hi)> { (lo, hi) };
            foreach (var (cl, ch) in cut)
            {
                var next = new List<(double Lo, double Hi)>();
                foreach (var (pl, ph) in parts)
                {
                    if (ch <= pl || cl >= ph) { next.Add((pl, ph)); continue; }
                    if (cl - pl > 1.0) next.Add((pl, cl));
                    if (ph - ch > 1.0) next.Add((ch, ph));
                }
                parts = next;
            }
            return parts;
        }
    }
}
