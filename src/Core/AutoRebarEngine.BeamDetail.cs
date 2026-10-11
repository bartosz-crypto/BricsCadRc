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
    /// K9 (decyzja użytkownika 2026-10-11 „rób wg rekomendacji”): detal zbrojenia belki 1:25 jako osobny widok w przestrzeni modelu
    /// (sekcja 4i ARCHITECTURE.md „Detal 1:25”). Detal = wycinek rzutu dolnego wokół strefy belki w rzeczywistych wymiarach, z dala
    /// od rzutów (nad nimi, kolejne w prawo bez nachodzenia); pręty belki TYLKO w detalu (położenie rzutu + Shift), na rzutach 1:50 —
    /// strefa, „xxxTHK” i odnośnik „DETAIL 'n'”. Obiekty detalu niebędące prętami — warstwa RC-BEAM-DETAIL, XData RC_BEAM_DETAIL
    /// (BeamStore): ANCHOR (kopia polilinii strefy; jego położenie wyznacza Shift — bez cache, MOVE detalu zostaje), LINE, PILE, TEXT,
    /// TITLE, PLANREF. Numer detalu wspólny z detalami otworów (NextDetailNumber). Nic nie jest rysowane na warstwach czytanych przez
    /// inne funkcje (SD-PILED-RAFT, SD-Pile, SD-Text, RC-BEAM). Jedna komenda = jedna grupa UNDO (transakcje komendy).
    /// </summary>
    public static partial class AutoRebarEngine
    {
        /// <summary>Skala opisów detalu: 1:25 → 25 / 50 = 0,5 (opisy 2,5 mm na papierze jak na rzucie 1:50).</summary>
        public const double BeamDetailAnnotScale = DetailScale / 50.0;
        /// <summary>Teksty „xxxTHK” w detalu: 2,5 mm × 25 = 62,5.</summary>
        public const double BeamDetailTextHeight = 2.5 * DetailScale;
        /// <summary>Odnośnik „DETAIL 'n'” na rzucie 1:50 (≈ 3 mm; jak detal otworu).</summary>
        public const double BeamDetailRefHeight = 140.0;
        /// <summary>Skala rodzaju linii (DASHED) ANCHOR-a i pali w detalu.</summary>
        public const double BeamDetailLtScale = DetailScale / 2.0;

        /// <summary>Cel rysowania prętów belki: null = rzut (jak przed K9), inaczej detal 1:25.</summary>
        private sealed class BeamDrawTarget
        {
            /// <summary>Punkt detalu = punkt rzutu dolnego + Shift.</summary>
            public Vector2d Shift;
            /// <summary>Okno detalu (już przesunięte) — granice linii odniesienia opisów.</summary>
            public Extents3d Box;
            /// <summary>Obrys rzutu dolnego przesunięty o Shift (warianty opisu prętów w nibie).</summary>
            public List<Point2d> Verts;
            public double AnnotScale = BeamDetailAnnotScale;
            public int Number;
            public ObjectId Anchor = ObjectId.Null;
            public string ZoneHandle;
            /// <summary>Rozkłady utworzone w detalu (tytuł pod najniższym opisem).</summary>
            public List<ObjectId> Dists = new List<ObjectId>();
        }

        private static void SetBeamDetailOverrides(BeamDrawTarget tg)
        {
            _annotScaleOverride = tg.AnnotScale;
            _leaderArmOverride = LeaderArmExtension * tg.AnnotScale;   // ramię opisu na papierze jak na rzucie 1:50
        }

        private static void ResetBeamDetailOverrides()
        {
            _annotScaleOverride = 1.0;
            _leaderArmOverride = double.NaN;
        }

        // ── UB 03 przy belce w detalu 1:25 (decyzja użytkownika 2026-10-11 „UB w nib też pokazuj na detalu”; „U-bary zamykające 1 a nie 4”
        //    dotyczy pionowych U-barów belki BU, nie UB 03 — korekta użytkownika 2026-10-11) ──
        // UB 03 generuje zawsze GenerateNibUBLayer (Mesh / UB NIB) — liczba i położenia z siatki dolnej jak dotąd. Krawędź nibu przy strefie
        // belki z detalem ([36] = 1 i ANCHOR przystający do strefy) → rozkład w detalu (położenie rzutu + Shift, skala opisów detalu,
        // znacznik RC_AUTOREBAR_BEAM = strefa — RC_SECTION mapuje go do rzutu jak pręty belki); liczba UB 03 jak na rzucie (także nib
        // zawinięty za narożnik). Detal jeszcze nie istnieje → na rzucie jak dotąd; RC_GENERATE →
        // Beam po utworzeniu detalu generuje UB 03 płyty ponownie (RefreshNibUbAtBeams) — kolejność Mesh / Beam dowolna.

        /// <summary>Strefa belki płyty dla UB 03: analiza, obszary krawędzi przy belce, tryb detalu i detal (Shift, okno detalu).</summary>
        private sealed class NibUbZone
        {
            public string Zone;
            public List<List<(double X, double Y)>> UbLoops;
            public bool DetailView;
            public bool HasDetail;
            public Vector2d Shift;
            public Extents3d Box;
        }

        /// <summary>Krawędź UB 03 rysowana w detalu strefy.</summary>
        private sealed class NibUbTarget
        {
            public NibUbZone Zone;
        }

        /// <summary>ANCHOR-y detali belek: strefa (handle znormalizowany) → wierzchołki ANCHOR-a. Tylko odczyt.</summary>
        private static Dictionary<string, List<Point2d>> ReadBeamDetailAnchors(Transaction tr, Database db)
        {
            var res = new Dictionary<string, List<Point2d>>(StringComparer.OrdinalIgnoreCase);
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline pl)) continue;
                var d = BeamStore.ReadDetail(pl);
                if (d == null || d.Value.role != BeamStore.RoleAnchor || string.IsNullOrEmpty(d.Value.zone)) continue;
                res[XLink.Norm(d.Value.zone)] = GeometryHelper.GetPolylineVertices(pl);
            }
            return res;
        }

        /// <summary>
        /// Przesunięcia detali belek (strefa → Shift) — tylko ANCHOR przystający do swojej strefy (jak RC_SECTION). Pręty z detalu
        /// (znacznik strefy) w położeniu rzutu = pozycja − Shift. Tylko odczyt.
        /// </summary>
        private static Dictionary<string, Vector2d> BeamDetailShifts(Database db)
        {
            var res = new Dictionary<string, Vector2d>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                foreach (var kv in ReadBeamDetailAnchors(tr, db))
                {
                    if (!XLink.TryParse(kv.Key, out long h) || h == 0 || !db.TryGetObjectId(new Handle(h), out var zid) || zid.IsErased) continue;
                    if (!(tr.GetObject(zid, OpenMode.ForRead) is Polyline zpl)) continue;
                    // ANCHOR = strefa efektywna (rekord [38] „bez nibu” — przesunięta do krawędzi)
                    var zt = EffectiveZoneOf(tr, db, zpl).Select(v => (v.X, v.Y)).ToList();
                    var at = kv.Value.Select(v => (v.X, v.Y)).ToList();
                    var s = BeamDetailRules.ShiftOf(at, zt);
                    if (BeamDetailRules.Congruent(at, zt, s)) res[kv.Key] = new Vector2d(s.X, s.Y);
                }
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.BeamDetailShifts", ex); }
            return res;
        }

        /// <summary>
        /// Strefy belek płyty (rzut <paramref name="slabOuterId"/>, obrys zewnętrzny <paramref name="outer"/>) dla UB 03: analiza, obszary
        /// krawędzi przy belce (<see cref="BeamGeometry.UbExclusionRects"/> — kryterium jak T IN NIB), tryb [36] i detal (ANCHOR przystający
        /// do strefy → Shift i okno detalu). Pusta lista — płyta bez stref (UB 03 bit w bit jak dotąd). Tylko odczyt.
        /// </summary>
        private static List<NibUbZone> NibUbZones(Database db, ObjectId slabOuterId, List<Point2d> outer)
        {
            var res = new List<NibUbZone>();
            try
            {
                var outl = outer.Select(v => (v.X, v.Y)).ToList();
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                var zones = BeamStore.ZonesOfSlab(tr, db, slabOuterId, outer);
                if (zones.Count > 0)
                {
                    var anchors = ReadBeamDetailAnchors(tr, db);
                    foreach (var zid in zones)
                    {
                        if (!(tr.GetObject(zid, OpenMode.ForRead) is Polyline pl)) continue;
                        var p = BeamStore.Read(pl) ?? new BeamParams();
                        var zt = GeometryHelper.GetPolylineVertices(pl).Select(v => (v.X, v.Y)).ToList();
                        var an = BeamGeometry.Analyze(zt, outl);
                        var ze = EffectiveZoneVerts(GeometryHelper.GetPolylineVertices(pl), p, outer).Select(v => (v.X, v.Y)).ToList();   // ANCHOR
                        var z = new NibUbZone
                        {
                            Zone = XLink.Norm(pl.Handle.ToString()), DetailView = p.DetailView,
                            UbLoops = RectilinearPolygons.Union(BeamGeometry.UbExclusionRects(an))
                        };
                        if (p.DetailView && anchors.TryGetValue(z.Zone, out var av))
                        {
                            var at = av.Select(v => (v.X, v.Y)).ToList();
                            var s = BeamDetailRules.ShiftOf(at, ze);
                            if (BeamDetailRules.Congruent(at, ze, s))
                            {
                                var w = BeamDetailRules.Window(ze, outl).Offset(s.X, s.Y);
                                z.HasDetail = true;
                                z.Shift = new Vector2d(s.X, s.Y);
                                z.Box = new Extents3d(new Point3d(w.X0, w.Y0, 0), new Point3d(w.X1, w.Y1, 0));
                            }
                        }
                        res.Add(z);
                    }
                }
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.NibUbZones", ex); res.Clear(); }
            return res;
        }

        /// <summary>
        /// Krawędź UB 03 (krawędź zewnętrzna c = <paramref name="edgeCoord"/>, linia uskoku <paramref name="innerCoord"/>, zakres lo..hi):
        /// strefa w trybie detalu, przy której leży linia uskoku w &gt; 50 % (jak T IN NIB), z istniejącym detalem → cel rysowania w detalu
        /// (liczba UB 03 jak na rzucie). Strefa w trybie detalu bez detalu — null + komunikat (UB 03 na rzucie; Beam przeniesie).
        /// </summary>
        private static NibUbTarget NibUbTargetFor(List<NibUbZone> zones, bool vertical, double edgeCoord, double innerCoord,
                                                  double lo, double hi, Editor ed)
        {
            if (zones == null || hi - lo < 1e-6) return null;
            foreach (var z in zones)
            {
                if (!z.DetailView) continue;
                double free = RectilinearPolygons.UncoveredParts(lo, hi, z.UbLoops, horizontal: !vertical, innerCoord).Sum(q => q.Hi - q.Lo);
                if (free >= 0.5 * (hi - lo)) continue;
                if (!z.HasDetail)
                {
                    ed.WriteMessage($"\n[AutoRebar UB] UB 03: krawędź {(vertical ? "x" : "y")}={edgeCoord:F0} przy belce {z.Zone} — detal 1:25 jeszcze " +
                                    "nie istnieje, UB 03 na rzucie; RC_GENERATE → Beam przeniesie je do detalu.\n");
                    return null;
                }
                return new NibUbTarget { Zone = z };
            }
            return null;
        }

        /// <summary>
        /// Tytuł „DETAIL 'n'” detalu strefy pod najniższym rozkładem / opisem ze znacznikiem strefy (pręty belki i UB 03 w detalu) — tylko
        /// obniżany (UB 03 dochodzą do detalu po tytule). Bez zmiany — bez zapisu.
        /// </summary>
        private static void UpdateBeamDetailTitle(Database db, string zone)
        {
            try
            {
                ObjectId titleId = ObjectId.Null;
                double titleY = 0, minY = double.MaxValue;
                int extFail = 0;
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    foreach (var (id, _, role) in BeamStore.FindDetail(tr, db, zone))
                        if (role == BeamStore.RoleTitle && tr.GetObject(id, OpenMode.ForRead) is MText mt) { titleId = id; titleY = mt.Location.Y; break; }
                    if (!titleId.IsNull)
                    {
                        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                        foreach (ObjectId id in ms)
                        {
                            if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                            if (!(BeamStore.ReadTag(br) is string t) || t.Length == 0 || !XLink.Same(t, zone)) continue;
                            var bar = BarBlockEngine.ReadXData(br);
                            if (bar == null) continue;
                            var ids = new List<ObjectId> { id };
                            if (XLink.TryParse(bar.AnnotHandle, out long ah) && ah != 0 && db.TryGetObjectId(new Handle(ah), out var aid) && !aid.IsErased)
                                ids.Add(aid);
                            foreach (var x in ids)
                            {
                                try { minY = Math.Min(minY, ((Entity)tr.GetObject(x, OpenMode.ForRead)).GeometricExtents.MinPoint.Y); }
                                catch (System.Exception ex) { if (extFail++ == 0) Log.Error("AutoRebar.UpdateBeamDetailTitle.Ext", ex); }
                            }
                        }
                    }
                    tr.Commit();
                }
                if (titleId.IsNull || minY == double.MaxValue || minY - 200 >= titleY - 1.0) return;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var mt = (MText)tr.GetObject(titleId, OpenMode.ForWrite);
                    mt.Location = new Point3d(mt.Location.X, minY - 200, mt.Location.Z);
                    tr.Commit();
                }
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.UpdateBeamDetailTitle", ex); }
        }

        /// <summary>
        /// Po RC_GENERATE → Beam: UB 03 płyty generowane ponownie (GenerateNibUBLayer, pręt reprezentatywny — jak Mesh), gdy płyta ma nib
        /// i UB 03, a któraś strefa płyty jest w trybie detalu albo UB 03 leżą w detalu (przełączenie detal → rzut, nowy detal). UB 03 przy
        /// belce przechodzą do detalu / wracają na rzut; płyta bez detali i bez UB 03 w detalu — bez zmian (bit w bit). Bez UB 03 — nic
        /// (Beam nie tworzy UB 03).
        /// </summary>
        private static void RefreshNibUbAtBeams(Document doc, BeamView bottom, Action<string> msg)
        {
            var db = doc.Database;
            try
            {
                if (NibDetector.Detect(db, bottom.OuterId) == null) return;
                bool any03 = false, inDetail = false, detailMode = false, noNib = false;
                string vh = bottom.OuterId.Handle.ToString();
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                        var bar = BarBlockEngine.ReadXData(br);
                        if (bar == null || string.IsNullOrEmpty(bar.Mark) || !bar.Mark.EndsWith(" " + UBSuffix)) continue;
                        // numer z pierwszego członu opisu — także „H10-03 UB” (1 pręt, opis bez rozstawu)
                        if (BeamGeometry.IsBeamCode(bar.LayerCode) || SingleBarEngine.ExtractPosNr(bar.Mark.Split(' ')[0]) != NibUBPosNr) continue;
                        string tag = ReadSlabTag(br);
                        bool mine = tag != null ? SameHandle(tag, vh)
                                                : GeometryHelper.IsPointInsidePolygon(bottom.Verts, new Point2d(br.Position.X, br.Position.Y));
                        if (!mine) continue;
                        any03 = true;
                        if (!string.IsNullOrEmpty(BeamStore.ReadTag(br))) inDetail = true;
                    }
                    foreach (var zid in BeamStore.ZonesOfSlab(tr, db, bottom.OuterId, bottom.Verts))
                    {
                        var zp = tr.GetObject(zid, OpenMode.ForRead) is Polyline zpl ? BeamStore.Read(zpl) : null;
                        if (zp?.DetailView ?? false) detailMode = true;
                        if (zp?.NoNib ?? false) noNib = true;   // rekord [38] „bez nibu” — UB 03 przy tej belce usuwane
                    }
                    tr.Commit();
                }
                if (!any03 || !(detailMode || inDetail || noNib)) return;
                msg("UB 03 płyty — ponowne generowanie (decyzja 2026-10-11: UB 03 przy belce w detalu 1:25, poza belką — na rzucie " +
                    "bez zmian).");
                GenerateNibUBLayer(doc, bottom.OuterId, "rebar_bottom", representativeOnly: true);
            }
            catch (System.Exception ex)
            {
                Log.Error("AutoRebar.RefreshNibUbAtBeams", ex);
                msg($"UB 03 przy belce — błąd ponownego generowania: {ex.Message}");
            }
        }

        /// <summary>
        /// Przygotowanie detalu strefy: istniejący ANCHOR strefy → numer i miejsce (Shift = MinPoint(ANCHOR) − MinPoint(strefy)); inaczej
        /// nowy numer (NextDetailNumber) i miejsce nad rzutami (BeamDetailRules.Place). Stare obiekty RC_BEAM_DETAIL strefy usuwane;
        /// rysowane: ANCHOR, przycięte linie betonu (obrys, linia uskoku), pale w oknie (przerywane), teksty „xxxTHK” strefy.
        /// Null — błąd (komunikat).
        /// </summary>
        private static BeamDrawTarget PrepareBeamDetail(Document doc, BeamView bottom, BeamView top, string zbHandle, List<Point2d> zbPoly,
                                                        Action<string> msg, List<Point2d> textPoly = null)
        {
            var db = doc.Database;
            string name = $"belka {zbHandle}";
            try
            {
                var zoneT = zbPoly.Select(v => (v.X, v.Y)).ToList();
                var outlT = bottom.Verts.Select(v => (v.X, v.Y)).ToList();
                var win = BeamDetailRules.Window(zoneT, outlT);

                // 1. Istniejący detal strefy: numer i miejsce z ANCHOR-a (bez cache — MOVE detalu zostaje)
                int nr = 0;
                (double X, double Y)? shift = null;
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var old = BeamStore.FindDetail(tr, db, zbHandle);
                    foreach (var (id, n, role) in old)
                    {
                        if (role != BeamStore.RoleAnchor || !(tr.GetObject(id, OpenMode.ForRead) is Polyline apl)) continue;
                        var av = GeometryHelper.GetPolylineVertices(apl).Select(v => (v.X, v.Y)).ToList();
                        shift = BeamDetailRules.ShiftOf(av, zoneT);
                        if (n > 0) nr = n;
                        break;
                    }
                    if (nr <= 0) nr = old.Select(o => o.nr).FirstOrDefault(n => n > 0);
                    tr.Commit();
                }
                BeamStore.EraseDetail(db, new[] { zbHandle });
                if (nr <= 0) nr = NextDetailNumber(db);

                // 2. Miejsce nowego detalu: nad rzutami (dolnym i górnym), kolejne w prawo bez nachodzenia na rysunek
                if (shift == null)
                {
                    double x0 = bottom.Box.MinPoint.X, yTop = bottom.Box.MaxPoint.Y;
                    if (top != null) { x0 = Math.Min(x0, top.Box.MinPoint.X); yTop = Math.Max(yTop, top.Box.MaxPoint.Y); }
                    double lm = BeamDetailRules.LabelMargin, tm = BeamDetailRules.TitleMargin;
                    var r = BeamDetailRules.Place(win.W + 2 * lm, win.H + 2 * lm + tm, OccupiedBoxes(db), x0,
                                                  yTop + BeamDetailRules.AboveViewsGap, BeamDetailRules.PlaceGap);
                    shift = (r.X0 + lm - win.X0, r.Y0 + lm + tm - win.Y0);
                    msg($"{name}: DETAIL '{nr}' (1:25) — nowe miejsce nad rzutami (przesunięcie {shift.Value.X:F0}, {shift.Value.Y:F0}).");
                }
                else msg($"{name}: DETAIL '{nr}' (1:25) — w dotychczasowym miejscu (ANCHOR).");

                var sv = new Vector2d(shift.Value.X, shift.Value.Y);
                var w = win.Offset(sv.X, sv.Y);
                var tg = new BeamDrawTarget
                {
                    Shift = sv, Number = nr, ZoneHandle = zbHandle,
                    Box = new Extents3d(new Point3d(w.X0, w.Y0, 0), new Point3d(w.X1, w.Y1, 0)),
                    Verts = bottom.Verts.Select(v => v + sv).ToList()
                };

                // 3. Obiekty detalu (warstwa RC-BEAM-DETAIL) — dane czytane przed transakcją zapisu
                SingleBarEngine.EnsureLayer(db, BeamStore.DetailLayer, 7);
                var dashed = EnsureDashedLinetype(db);
                var lines = BeamDetailRules.ClipPolygon(win, outlT);
                // rekord [38] „bez nibu”: przy tej belce bez linii uskoku (nib płyty bez części przy belkach bez nibu)
                var nib = WithoutBeamNibs(db, NibDetector.Detect(db, bottom.OuterId));
                if (nib?.Inner != null)
                    lines.AddRange(BeamDetailRules.ClipPolygon(win, nib.Inner.Select(v => (v.X, v.Y)).ToList(), outlT));
                var piles = FindPiles(db, bottom.Verts).Where(pc => win.Contains(pc.c.X, pc.c.Y)).ToList();
                var texts = ThkTextsIn(db, textPoly ?? zbPoly);   // „xxxTHK” w strefie z GA (bez nibu — ANCHOR przesunięty)

                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    ObjectId Add(Entity e, string role)
                    {
                        e.SetDatabaseDefaults(db);
                        e.Layer = BeamStore.DetailLayer;
                        var id = ms.AppendEntity(e);
                        tr.AddNewlyCreatedDBObject(e, true);
                        BeamStore.TagDetail(tr, db, e, zbHandle, nr, role);
                        return id;
                    }

                    var anchor = new Polyline();
                    for (int i = 0; i < zbPoly.Count; i++) anchor.AddVertexAt(i, zbPoly[i] + sv, 0, 0, 0);
                    anchor.Closed = true;
                    tg.Anchor = Add(anchor, BeamStore.RoleAnchor);
                    anchor.ColorIndex = 1;
                    if (!dashed.IsNull) { anchor.LinetypeId = dashed; anchor.LinetypeScale = BeamDetailLtScale; }

                    // linie betonu jako osobne odcinki (Line) — nigdy zamknięty obrys przystający do płyty (rzuty w RC_SECTION)
                    foreach (var (a, b) in lines)
                        Add(new Line(new Point3d(a.X + sv.X, a.Y + sv.Y, 0), new Point3d(b.X + sv.X, b.Y + sv.Y, 0)), BeamStore.RoleLine);

                    foreach (var (c, rr) in piles)
                    {
                        var circle = new Circle(new Point3d(c.X + sv.X, c.Y + sv.Y, 0), Vector3d.ZAxis, rr);
                        Add(circle, BeamStore.RolePile);
                        if (!dashed.IsNull) { circle.LinetypeId = dashed; circle.LinetypeScale = BeamDetailLtScale; }
                    }

                    foreach (var (pt, txt, rot, att, style) in texts)
                    {
                        var mt = new MText();
                        Add(mt, BeamStore.RoleText);
                        mt.Contents = txt;
                        mt.TextHeight = BeamDetailTextHeight;
                        mt.Location = new Point3d(pt.X + sv.X, pt.Y + sv.Y, 0);
                        mt.Rotation = rot;
                        mt.Attachment = att;
                        if (!style.IsNull) mt.TextStyleId = style;
                    }
                    tr.Commit();
                }
                return tg;
            }
            catch (System.Exception ex)
            {
                Log.Error("AutoRebar.PrepareBeamDetail", ex);
                msg($"{name}: błąd detalu 1:25 — {ex.Message}; pręty belki pominięte.");
                return null;
            }
        }

        /// <summary>
        /// Po prętach detalu: kolizje prętów reprezentatywnych w obrysie ANCHOR-a (pręty belki są pomijane — dla porządku jak detal otworu),
        /// tytuł „DETAIL 'n'” / „SCALE 1:25” pod najniższym opisem detalu, odnośniki „DETAIL 'n'” na rzutach (strefa dolna i górna).
        /// </summary>
        private static void FinishBeamDetail(Document doc, BeamDrawTarget tg, ObjectId zb, ObjectId zt, Action<string> msg)
        {
            var db = doc.Database;
            string name = $"belka {tg.ZoneHandle}";
            try
            {
                if (!tg.Anchor.IsNull) ResolveRepresentativeCollisions(doc, tg.Anchor);

                // najniższy punkt detalu: okno, rozkłady i ich opisy
                double minY = tg.Box.MinPoint.Y;
                int extFail = 0;
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    foreach (var id in tg.Dists)
                    {
                        if (id.IsNull || id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                        var ids = new List<ObjectId> { id };
                        var bar = BarBlockEngine.ReadXData(br);
                        if (bar != null && XLink.TryParse(bar.AnnotHandle, out long ah) && ah != 0
                            && db.TryGetObjectId(new Handle(ah), out var aid) && !aid.IsErased) ids.Add(aid);
                        foreach (var x in ids)
                        {
                            try { minY = Math.Min(minY, ((Entity)tr.GetObject(x, OpenMode.ForRead)).GeometricExtents.MinPoint.Y); }
                            catch (System.Exception ex) { if (extFail++ == 0) Log.Error("AutoRebar.FinishBeamDetail.Ext", ex); }
                        }
                    }
                    tr.Commit();
                }

                SingleBarEngine.EnsureTextStyle(db, TitleTextStyle);
                double cx = (tg.Box.MinPoint.X + tg.Box.MaxPoint.X) / 2.0;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    var mt = new MText();
                    mt.SetDatabaseDefaults(db);
                    mt.Layer = BeamStore.DetailLayer;
                    // tytuł jak detal otworu (GenerateHoleDetail): „DETAIL 'n'” 125 (5 mm w 1:25), „SCALE 1:25” 0,8×
                    mt.Contents = "{\\H3.125x;\\L\\C4;DETAIL '" + tg.Number + "'\\P\\H0.8x;\\l\\C256;SCALE 1:" + DetailScale + " }";
                    mt.TextHeight = DetailScale * 1.6;
                    mt.Location = new Point3d(cx, minY - 200, 0);
                    mt.Attachment = AttachmentPoint.TopCenter;
                    mt.ColorIndex = 256;
                    var st = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                    if (st.Has(TitleTextStyle)) mt.TextStyleId = st[TitleTextStyle];
                    ms.AppendEntity(mt);
                    tr.AddNewlyCreatedDBObject(mt, true);
                    BeamStore.TagDetail(tr, db, mt, tg.ZoneHandle, tg.Number, BeamStore.RoleTitle);
                    tr.Commit();
                }

                // odnośniki na rzutach 1:50: grot w strefie, tekst „DETAIL 'n'” obok
                int refs = 0;
                foreach (var z in new[] { zb, zt })
                {
                    if (z.IsNull || z.IsErased) continue;
                    Point2d tip;
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        if (!(tr.GetObject(z, OpenMode.ForRead) is Polyline zpl)) continue;
                        tip = BeamStore.Centroid(zpl);
                        tr.Commit();
                    }
                    var lid = SingleBarEngine.PlaceNoteLeader(db, new[] { new Point3d(tip.X, tip.Y, 0) },
                                                              new Point3d(tip.X + 700, tip.Y + 700, 0), $"DETAIL '{tg.Number}'",
                                                              BeamDetailRefHeight, 2, NoteTextStyle, BeamStore.DetailLayer);
                    if (lid.IsNull) continue;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        BeamStore.TagDetail(tr, db, (Entity)tr.GetObject(lid, OpenMode.ForWrite), tg.ZoneHandle, tg.Number, BeamStore.RolePlanRef);
                        tr.Commit();
                    }
                    refs++;
                }
                msg($"{name}: DETAIL '{tg.Number}' — rozkładów w detalu {tg.Dists.Count}, odnośniki na rzutach: {refs}.");
            }
            catch (System.Exception ex)
            {
                Log.Error("AutoRebar.FinishBeamDetail", ex);
                msg($"{name}: błąd tytułu / odnośników detalu — {ex.Message}");
            }
        }

        /// <summary>Teksty „xxxTHK” (MText / DBText) w strefie: punkt wstawienia, treść (bez formatowania), obrót, zaczepienie, styl.</summary>
        private static List<(Point2d pt, string text, double rot, AttachmentPoint att, ObjectId style)> ThkTextsIn(Database db, List<Point2d> zone)
        {
            var res = new List<(Point2d, string, double, AttachmentPoint, ObjectId)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased) continue;
                    var o = tr.GetObject(id, OpenMode.ForRead);
                    List<string> lines; Point3d ins; double rot; AttachmentPoint att; ObjectId style;
                    if (o is MText mt) { lines = GaImportEngine.TextLines(mt); ins = mt.Location; rot = mt.Rotation; att = mt.Attachment; style = mt.TextStyleId; }
                    else if (o is DBText dt) { lines = new List<string> { dt.TextString ?? "" }; ins = dt.Position; rot = dt.Rotation; att = AttachmentPoint.BottomLeft; style = dt.TextStyleId; }
                    else continue;
                    if (BeamStore.ReadDetail((Entity)o) != null) continue;
                    if (!GeometryHelper.IsPointInsidePolygon(zone, new Point2d(ins.X, ins.Y))) continue;
                    if (!lines.Any(l => BeamGeometry.ParseThk(l) != null)) continue;
                    res.Add((new Point2d(ins.X, ins.Y), string.Join("\\P", lines), rot, att, style));
                }
                tr.Commit();
            }
            return res;
        }

        /// <summary>Obwiednie obiektów przestrzeni modelu (miejsce nowego detalu bez nachodzenia). Obiekty bez obwiedni pomijane.</summary>
        private static List<BeamDetailRules.Rect> OccupiedBoxes(Database db)
        {
            var res = new List<BeamDetailRules.Rect>();
            int fail = 0;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e) || !e.Visible) continue;
                    try
                    {
                        var x = e.GeometricExtents;
                        res.Add(new BeamDetailRules.Rect(x.MinPoint.X, x.MinPoint.Y, x.MaxPoint.X, x.MaxPoint.Y));
                    }
                    catch (System.Exception ex) { if (fail++ == 0) Log.Error("AutoRebar.OccupiedBoxes", ex); }
                }
                tr.Commit();
            }
            return res;
        }
    }
}
