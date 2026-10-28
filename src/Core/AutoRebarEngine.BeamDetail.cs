using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.Colors;
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
            /// <summary>Ramka detalu (okno) — obrys dla czytelności prętów reprezentatywnych.</summary>
            public ObjectId Frame = ObjectId.Null;
            public string ZoneHandle;
            /// <summary>Rozkłady utworzone w detalu (tytuł pod najniższym opisem).</summary>
            public List<ObjectId> Dists = new List<ObjectId>();
            /// <summary>Strefa nowych szablonów prętów belki — obok detalu, jak przy detalu otworu (decyzja użytkownika 2026-10-12
            /// „pręty, które rozkładamy — jak w otworze, w jakiejś odległości od detalu”), zamiast strefy rebar_bottom rzutu.</summary>
            public Extents3d TemplateZone;
            /// <summary>Ramka detalu w układzie rzutu (wielokąt — offset strefy L / U, decyzja 2026-10-13; albo prostokąt okna).</summary>
            public List<(double X, double Y)> FramePlan;
            /// <summary>Uwaga Bartka 2026-10-23: podpis elementu (identyczne belki / wsporniki — jeden detal) i strefa detalu głównego,
            /// gdy ten detal jest kopią rozkładów nad nim (null — detal główny).</summary>
            public string Signature, CopyOf;
        }

        /// <summary>Strefa szablonów prętów belki w trybie detalu (null — strefa rebar_bottom rzutu, jak przed decyzją 2026-10-12).</summary>
        private static Extents3d? _beamTemplateZone;

        private static void SetBeamDetailOverrides(BeamDrawTarget tg)
        {
            _annotScaleOverride = tg.AnnotScale;
            _leaderArmOverride = BeamDetailLeaderArm * tg.AnnotScale;   // ramię opisu na papierze jak dawniej na rzucie 1:50 (2400)
            _beamTemplateZone = tg.TemplateZone;
        }

        private static void ResetBeamDetailOverrides()
        {
            _annotScaleOverride = 1.0;
            _leaderArmOverride = double.NaN;
            _beamTemplateZone = null;
        }

        /// <summary>Kolor detalu otworu / ramki detalu belki (czerwony, jak RC_DETAL_OTWORU) i skala przerywanej.</summary>
        private const short BeamDetailFrameColor = 10;
        private const double BeamDetailFrameLtScale = 25.0;
        /// <summary>Lica belki w detalu (ANCHOR) — szara linia przerywana (belka pod płytą; decyzja 2026-10-12, wzór — screen Bartka).</summary>
        private const short BeamDetailHiddenColor = 8;

        /// <summary>Kolor i rodzaj linii obiektu źródłowego (ByLayer → z warstwy) — linie betonu w detalu jak na rzucie.</summary>
        private static (Color color, ObjectId lt, double lts)? SourceStyle(Database db, ObjectId id)
        {
            if (id.IsNull || id.IsErased) return null;
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) return null;
                var layer = (LayerTableRecord)tr.GetObject(e.LayerId, OpenMode.ForRead);
                var color = e.Color.IsByLayer || e.Color.IsByBlock ? layer.Color : e.Color;
                var lt = string.Equals(e.Linetype, "ByLayer", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(e.Linetype, "ByBlock", StringComparison.OrdinalIgnoreCase) ? layer.LinetypeObjectId : e.LinetypeId;
                var res = (color, lt, e.LinetypeScale);
                tr.Commit();
                return res;
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.SourceStyle", ex); return null; }
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
            /// <summary>Strefa efektywna (ANCHOR) w układzie rzutu — końce belki przy krawędzi (UB 03 co 150, decyzja 2026-10-12).</summary>
            public List<Point2d> Verts;
            /// <summary>Strzemiona belki (BL) w detalu, w układzie rzutu: pręty wzdłuż X?, zakres wzdłuż linii rozkładu, liczba
            /// (Count — bez krotności 4-ciętych) — UB 03 wzdłuż belki = strzemiona + 2 (decyzja 2026-10-12).</summary>
            public List<(bool barsX, double lo, double hi, int count)> Links = new List<(bool, double, double, int)>();
            public List<List<(double X, double Y)>> UbLoops;
            /// <summary>Odcinki strefy efektywnej (końce przy krawędzi — także noga belki L / U; decyzja 2026-10-13).</summary>
            public List<BeamGeometry.Segment> Segs;
            public bool DetailView;
            public bool HasDetail;
            public Vector2d Shift;
            public Extents3d Box;
        }

        /// <summary>Krawędź UB 03 rysowana w detalu strefy.</summary>
        private sealed class NibUbTarget
        {
            public NibUbZone Zone;
            /// <summary>Koniec belki przy krawędzi (szerokość belki + nib): UB 03 co 150 od narożnika do uskoku (decyzja 2026-10-12).</summary>
            public bool EndAt150;
            /// <summary>Narożnik płyty na górnym końcu odcinka (pręty od segHigh w dół do uskoku); inaczej od segLow w górę.</summary>
            public bool FromHigh;
            /// <summary>2026-10-13: krawędź przy belce w trybie detalu, detal jeszcze nie istnieje — UB 03 NIE na rzucie (Beam narysuje je
            /// w detalu; wcześniej rysowane na rzucie i znikały po Beam).</summary>
            public bool Skip;
        }

        /// <summary>UB 03 na końcu belki przy krawędzi: rozstaw (decyzja użytkownika 2026-10-12 „dajemy co 150 UB … TYLKO do stepa w nibie”).</summary>
        public const double NibUbEndSpacing = 150.0;

        /// <summary>
        /// Krawędź UB 03 prostopadła do osi belki (koniec belki przy krawędzi płyty, lico końca ≤ 260 od krawędzi), gdy na krawędzi
        /// leży szerokość belki + nib (bok strefy ≤ 260 od narożnika = końca odcinka krawędzi): część krawędzi od narożnika do lica
        /// wewnętrznego belki (uskok w nibie) → UB 03 w detalu co 150. Każdy odcinek strefy (także noga belki L / U — 2026-10-13) w trybie
        /// detalu z istniejącym detalem.
        /// Null — brak (UB 03 jak dotąd).
        /// </summary>
        private static (double lo, double hi, NibUbZone zone, bool fromHigh)? BeamEndUbPortion(List<NibUbZone> zones, bool verticalEdge,
                                                                                              double edgeCoord, double segLow, double segHigh,
                                                                                              bool needDetail = true)
        {
            if (zones == null) return null;
            foreach (var z in zones)
            {
                if (!z.DetailView || (needDetail && !z.HasDetail) || z.Segs == null) continue;
                // każdy odcinek strefy (prostokąt, noga L / U) z końcem przy krawędzi płyty (decyzja 2026-10-13 — wcześniej tylko
                // strefy prostokątne, belka L traciła UB 03 co 150 i pojedynczy UB 03 drugiego kierunku)
                foreach (var sg in z.Segs)
                {
                    if (sg.Manual || verticalEdge != sg.AlongX) continue;   // krawędź równoległa do osi — to nie koniec belki
                    foreach (var (f, kind) in new[] { (sg.FaceLo, sg.LoKind), (sg.FaceHi, sg.HiKind) })
                    {
                        // koniec przy krawędzi (narożnik L / złącze T — nie koniec belki)
                        if (kind == BeamGeometry.EndKind.Corner || kind == BeamGeometry.EndKind.Tee
                            || Math.Abs(f - edgeCoord) > BeamGeometry.EdgeEndMaxGap) continue;
                        double a0 = sg.AcrossLo, a1 = sg.AcrossHi;            // lica boczne (wzdłuż krawędzi)
                        if (a1 <= segLow + 1.0 || a0 >= segHigh - 1.0) continue;
                        if (segHigh - a1 >= -1.0 && segHigh - a1 <= BeamGeometry.EdgeEndMaxGap && a0 > segLow + 1.0)
                            return (a0, segHigh, z, true);
                        if (a0 - segLow >= -1.0 && a0 - segLow <= BeamGeometry.EdgeEndMaxGap && a1 < segHigh - 1.0)
                            return (segLow, a1, z, false);
                    }
                }
            }
            return null;
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
                var zones = BeamStore.MeshZonesOfSlab(tr, db, slabOuterId, outer);
                if (zones.Count > 0)
                {
                    var anchors = ReadBeamDetailAnchors(tr, db);
                    // strzemiona belek w detalach (BL ze znacznikiem strefy) — położenie detalu, przeliczane niżej na rzut (− Shift)
                    var links = new List<(string zone, bool barsX, double x, double y, double span, int count)>();
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                        string tag = BeamStore.ReadTag(br);
                        if (string.IsNullOrEmpty(tag)) continue;
                        var bar = BarBlockEngine.ReadXData(br);
                        if (bar == null || bar.LayerCode != BeamGeometry.CodeLinks || bar.Count <= 0) continue;
                        links.Add((XLink.Norm(tag), bar.Direction == "X", br.Position.X, br.Position.Y, bar.BarsSpan, bar.Count));
                    }
                    foreach (var zid in zones)
                    {
                        if (!(tr.GetObject(zid, OpenMode.ForRead) is Polyline pl)) continue;
                        var p = BeamStore.Read(pl) ?? new BeamParams();
                        var zt = GeometryHelper.GetPolylineVertices(pl).Select(v => (v.X, v.Y)).ToList();
                        var an = BeamGeometry.Analyze(zt, outl);
                        var zeP = EffectiveZoneVerts(GeometryHelper.GetPolylineVertices(pl), p, outer);
                        var ze = zeP.Select(v => (v.X, v.Y)).ToList();   // ANCHOR
                        var z = new NibUbZone
                        {
                            Zone = XLink.Norm(pl.Handle.ToString()), DetailView = p.DetailView, Verts = zeP,
                            UbLoops = RectilinearPolygons.Union(BeamGeometry.UbExclusionRects(an)),
                            Segs = BeamGeometry.Analyze(ze, outl).Segments
                        };
                        if (p.DetailView && anchors.TryGetValue(z.Zone, out var av))
                        {
                            var at = av.Select(v => (v.X, v.Y)).ToList();
                            var s = BeamDetailRules.ShiftOf(at, ze);
                            if (BeamDetailRules.Congruent(at, ze, s))
                            {
                                var w = BeamDetailRules.DetailWindow(ze, outl).Offset(s.X, s.Y);
                                z.HasDetail = true;
                                z.Shift = new Vector2d(s.X, s.Y);
                                z.Box = new Extents3d(new Point3d(w.X0, w.Y0, 0), new Point3d(w.X1, w.Y1, 0));
                                // pręty X → rozkład wzdłuż Y (i odwrotnie); położenie rzutu = detal − Shift
                                foreach (var l in links.Where(l => l.zone == z.Zone))
                                {
                                    double lo = l.barsX ? l.y - s.Y : l.x - s.X;
                                    z.Links.Add((l.barsX, lo, lo + l.span, l.count));
                                }
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
                    // 2026-10-13 (uwaga Bartka: UB 03 na rzucie po Mesh znikały po Beam): od początku bez UB 03 na rzucie przy belce
                    ed.WriteMessage($"\n[AutoRebar UB] UB 03: krawędź {(vertical ? "x" : "y")}={edgeCoord:F0} przy belce {z.Zone} — detal 1:25 jeszcze " +
                                    "nie istnieje: UB 03 przy belce nie na rzucie, RC_GENERATE → Beam narysuje je w detalu.\n");
                    return new NibUbTarget { Zone = z, Skip = true };
                }
                return new NibUbTarget { Zone = z };
            }
            return null;
        }

        /// <summary>
        /// Krawędź UB 03, przy której strefa w detalu leży tylko na części (&lt; 50 % — np. krótka noga belki L na długiej krawędzi;
        /// decyzja użytkownika 2026-10-13 „brakuje UB 03 na tej krawędzi”): podział na część przy belce (cel — detal tej strefy)
        /// i resztę (null — rzut jak dotąd). Części krótsze niż 100 mm pomijane. Null — brak strefy z detalem przy krawędzi (≥ 300 mm).
        /// </summary>
        private static List<(double lo, double hi, NibUbZone zone)> SplitEdgeByDetailZones(List<NibUbZone> zones, bool vertical,
                                                                                          double innerCoord, double lo, double hi)
        {
            if (zones == null || hi - lo < 1e-6) return null;
            foreach (var z in zones)
            {
                if (!z.DetailView) continue;   // bez detalu — części przy belce pomijane (Skip), Beam narysuje je w detalu
                var free = RectilinearPolygons.UncoveredParts(lo, hi, z.UbLoops, horizontal: !vertical, innerCoord)
                                              .Where(q => q.Hi - q.Lo > 1.0).OrderBy(q => q.Lo).ToList();
                if (hi - lo - free.Sum(q => q.Hi - q.Lo) < 300.0) continue;
                var res = new List<(double lo, double hi, NibUbZone zone)>();
                double cur = lo;
                foreach (var q in free)
                {
                    if (q.Lo - cur > 1.0) res.Add((cur, q.Lo, z));
                    res.Add((q.Lo, q.Hi, null));
                    cur = q.Hi;
                }
                if (hi - cur > 1.0) res.Add((cur, hi, z));
                return res.Where(r => r.hi - r.lo >= 100.0).ToList();
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
                bool any03 = false, inDetail = false, detailMode = false, noNib = false, anyPlan03 = false;
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
                        if (!string.IsNullOrEmpty(BeamStore.ReadTag(br))) inDetail = true; else anyPlan03 = true;
                    }
                    foreach (var zid in BeamStore.MeshZonesOfSlab(tr, db, bottom.OuterId, bottom.Verts))
                    {
                        var zp = tr.GetObject(zid, OpenMode.ForRead) is Polyline zpl ? BeamStore.Read(zpl) : null;
                        if (zp?.DetailView ?? false) detailMode = true;
                        if (zp?.NoNib ?? false) noNib = true;   // rekord [38] „bez nibu” — UB 03 przy tej belce usuwane
                    }
                    tr.Commit();
                }
                if (!any03 && detailMode)
                {
                    // decyzja 2026-10-12 („jak jest na detalu, to musimy go zazbroić”): siatki (Mesh) jeszcze nie ma — UB 03 tylko
                    // na krawędziach nibu w detalu belki; Mesh później wygeneruje UB 03 całej płyty (detal — ten sam wynik)
                    msg("UB 03 w detalu belki (siatki płyty jeszcze nie ma — UB 03 na rzucie wygeneruje Mesh).");
                    GenerateNibUBLayer(doc, bottom.OuterId, "rebar_bottom", representativeOnly: true, onlyBeamDetail: true);
                    return;
                }
                if (!any03 || !(detailMode || inDetail || noNib)) return;
                msg("UB 03 płyty — ponowne generowanie (decyzja 2026-10-11: UB 03 przy belce w detalu 1:25, poza belką — na rzucie " +
                    "bez zmian).");
                GenerateNibUBLayer(doc, bottom.OuterId, "rebar_bottom", representativeOnly: true, onlyBeamDetail: !anyPlan03);
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
                                                        Action<string> msg, List<Point2d> textPoly = null, bool offsetFrame = false,
                                                        List<((double X, double Y) A, (double X, double Y) B)> extraLines = null,
                                                        bool allPilesInFrame = false, string signature = null)
        {
            var db = doc.Database;
            string name = $"belka {zbHandle}";
            try
            {
                var zoneT = zbPoly.Select(v => (v.X, v.Y)).ToList();
                var outlT = bottom.Verts.Select(v => (v.X, v.Y)).ToList();
                // decyzja użytkownika 2026-10-13: ramka = offset strefy (L / U jak strefa), okno = jej obwiednia;
                // offsetFrame (wspornik, uwaga Bartka 2026-10-21) — sam offset strefy, bez przycinania do obrysu + 300
                var frameT = BeamDetailRules.FramePolygon(zoneT, offsetFrame ? null : outlT);
                var win = frameT != null ? BeamDetailRules.Bbox(frameT) : BeamDetailRules.Window(zoneT, outlT);
                if (frameT == null) frameT = BeamDetailRules.RectPoly(win);

                // 1. Istniejący detal strefy: numer i miejsce z ANCHOR-a (bez cache — MOVE detalu zostaje)
                int nr = 0;
                (double X, double Y)? shift = null;
                string oldMaster = "";
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var old = BeamStore.FindDetail(tr, db, zbHandle);
                    foreach (var (id, n, role) in old)
                    {
                        if (role != BeamStore.RoleAnchor || !(tr.GetObject(id, OpenMode.ForRead) is Polyline apl)) continue;
                        var av = GeometryHelper.GetPolylineVertices(apl).Select(v => (v.X, v.Y)).ToList();
                        shift = BeamDetailRules.ShiftOf(av, zoneT);
                        if (n > 0) nr = n;
                        oldMaster = BeamStore.ReadDetailGroup(apl).master;
                        break;
                    }
                    if (nr <= 0) nr = old.Select(o => o.nr).FirstOrDefault(n => n > 0);
                    tr.Commit();
                }

                // 1a. Uwaga Bartka 2026-10-23: identyczny element (ten sam podpis) ma już detal główny — ten element podpisany tym samym
                //     detalem, jego rozkłady jako kopia nad detalem głównym (w ramce z przekątnymi); własny detal główny — bez zmian
                string copyOf = null;
                if (!string.IsNullOrEmpty(signature) && (shift == null || !string.IsNullOrEmpty(oldMaster)))
                {
                    var m = FindDetailMaster(db, signature, zbHandle);
                    if (m != null)
                    {
                        copyOf = m.Value.zone; nr = m.Value.nr;
                        bool sameSlot = shift != null && XLink.Same(oldMaster, copyOf);
                        if (!sameSlot) shift = CopySlotShift(db, m.Value, zbHandle);
                        msg($"{name}: identyczny element jak w DETAIL '{nr}' (strefa {copyOf}) — ten sam detal, rozkłady jako kopia nad detalem.");
                    }
                    else if (!string.IsNullOrEmpty(oldMaster)) shift = null;   // detal główny zniknął — własny detal w nowym miejscu
                }
                BeamStore.EraseDetail(db, new[] { zbHandle });
                if (nr <= 0) nr = NextDetailNumber(db);

                // 2. Miejsce nowego detalu — decyzja użytkownika 2026-10-12 „jak przy detalu otworu — sami klikamy miejsce”: wskazany
                //    środek detalu (Enter / tryb bez okien — automatycznie nad rzutami, kolejne w prawo bez nachodzenia na rysunek)
                if (shift == null)
                {
                    Point3d? pick = null;
                    if (!ScriptMode.IsNonInteractive())
                    {
                        var po = new PromptPointOptions($"\n{BeamTag} {name}: wskaż miejsce DETAIL '{nr}' (środek detalu, okno " +
                                                        $"{win.W:F0} × {win.H:F0}) <Enter = automatycznie nad rzutami>: ") { AllowNone = true };
                        var pr = doc.Editor.GetPoint(po);
                        if (pr.Status == PromptStatus.OK) pick = pr.Value;
                        else if (pr.Status != PromptStatus.None)
                        {
                            msg($"{name}: anulowano wskazanie miejsca detalu — pręty belki pominięte.");
                            return null;
                        }
                    }
                    if (pick.HasValue)
                    {
                        shift = (pick.Value.X - (win.X0 + win.X1) / 2.0, pick.Value.Y - (win.Y0 + win.Y1) / 2.0);
                        msg($"{name}: DETAIL '{nr}' (1:25) — w miejscu wskazanym.");
                    }
                    else
                    {
                        double x0 = bottom.Box.MinPoint.X, yTop = bottom.Box.MaxPoint.Y;
                        if (top != null) { x0 = Math.Min(x0, top.Box.MinPoint.X); yTop = Math.Max(yTop, top.Box.MaxPoint.Y); }
                        double lm = BeamDetailRules.LabelMargin, tm = BeamDetailRules.TitleMargin;
                        // + kolumna szablonów prętów po prawej (TemplateZone)
                        var r = BeamDetailRules.Place(win.W + 2 * lm + TemplateZoneWidth, win.H + 2 * lm + tm, OccupiedBoxes(db), x0,
                                                      yTop + BeamDetailRules.AboveViewsGap, BeamDetailRules.PlaceGap);
                        shift = (r.X0 + lm - win.X0, r.Y0 + lm + tm - win.Y0);
                        msg($"{name}: DETAIL '{nr}' (1:25) — nowe miejsce nad rzutami (przesunięcie {shift.Value.X:F0}, {shift.Value.Y:F0}).");
                    }
                }
                else msg($"{name}: DETAIL '{nr}' (1:25) — w dotychczasowym miejscu (ANCHOR).");

                var sv = new Vector2d(shift.Value.X, shift.Value.Y);
                var w = win.Offset(sv.X, sv.Y);
                // szablony prętów (nowe pozycje) — kolumna na prawo od detalu, za opisami (jak detal otworu: ramka + 3000)
                double tz0 = w.X1 + BeamDetailRules.LabelMargin;
                var tg = new BeamDrawTarget
                {
                    Shift = sv, Number = nr, ZoneHandle = zbHandle,
                    Box = new Extents3d(new Point3d(w.X0, w.Y0, 0), new Point3d(w.X1, w.Y1, 0)),
                    Verts = bottom.Verts.Select(v => v + sv).ToList(),
                    TemplateZone = new Extents3d(new Point3d(tz0, w.Y1 - 1.0e7, 0), new Point3d(tz0 + TemplateZoneWidth, w.Y1, 0)),
                    FramePlan = frameT,
                    Signature = signature, CopyOf = copyOf
                };

                // 3. Obiekty detalu (warstwa RC-BEAM-DETAIL) — dane czytane przed transakcją zapisu
                SingleBarEngine.EnsureLayer(db, BeamStore.DetailLayer, 7);
                var dashed = EnsureDashedLinetype(db);
                // KARDYNALNE (2026-10-28): obrys obliczeniowy płyty szeregu (niewidoczny) nie jest rysowany — linie betonu z obrysu GA
                // (warstwa rysunkowa) bierze OtherPlanLines, bez linii w poprzek wylotu pasa GB
                bool hiddenOutl = IsHidden(db, bottom.OuterId);
                var lines = hiddenOutl ? new List<((double X, double Y) A, (double X, double Y) B)>() : BeamDetailRules.ClipPolygon(frameT, outlT);
                // rekord [38] „bez nibu”: przy tej belce bez linii uskoku (nib płyty bez części przy belkach bez nibu)
                var nib = WithoutBeamNibs(db, NibDetector.Detect(db, bottom.OuterId));
                var nibLines = nib?.Inner != null
                    ? BeamDetailRules.ClipPolygon(frameT, nib.Inner.Select(v => (v.X, v.Y)).ToList(), outlT)
                    : new List<((double X, double Y) A, (double X, double Y) B)>();
                // decyzja 2026-10-12 (wzór — screen Bartka): linie betonu w kolorach / rodzajach linii rzutu (obrys, linia uskoku)
                var outlStyle = SourceStyle(db, bottom.OuterId);
                var nibStyle = nib != null ? SourceStyle(db, nib.InnerId) ?? outlStyle : outlStyle;
                var others = OtherPlanLines(db, win, frameT, bottom, nib?.InnerId ?? ObjectId.Null,
                                            hiddenOutl ? new List<(double X, double Y)>() : outlT, nib?.Inner, m => msg($"{name}: {m}"));
                // GB poza płytą (2026-10-23): pale w całej ramce (także w pasie i w drugiej płycie), nie tylko w obrysie płyty [6]
                var piles = FindPiles(db, allPilesInFrame ? frameT.Select(q => new Point2d(q.X, q.Y)).ToList() : bottom.Verts)
                                .Where(pc => BeamDetailRules.InsideOrOn(frameT, (pc.c.X, pc.c.Y))).ToList();
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

                    // ANCHOR = lica belki (kopia strefy): szara przerywana — belka pod płytą (decyzja 2026-10-12; wcześniej czerwona)
                    var anchor = new Polyline();
                    for (int i = 0; i < zbPoly.Count; i++) anchor.AddVertexAt(i, zbPoly[i] + sv, 0, 0, 0);
                    anchor.Closed = true;
                    tg.Anchor = Add(anchor, BeamStore.RoleAnchor);
                    if (!string.IsNullOrEmpty(signature)) BeamStore.TagDetailAnchor(tr, db, anchor, zbHandle, nr, signature, copyOf ?? "");
                    // kolor jak obrys płyty na rzucie (zielony — uwaga Bartka 2026-10-12), linia przerywana
                    if (outlStyle != null) anchor.Color = outlStyle.Value.color; else anchor.ColorIndex = BeamDetailHiddenColor;
                    if (!dashed.IsNull) { anchor.LinetypeId = dashed; anchor.LinetypeScale = BeamDetailLtScale; }

                    // ramka detalu = okno (czerwona przerywana, jak detal otworu) — w odległości od belki (lico wewnętrzne + 600,
                    // krawędź płyty + 300); decyzja 2026-10-12
                    // decyzja 2026-10-13: ramka = offset strefy (L / U), nie prostokąt
                    var frame = new Polyline();
                    for (int i = 0; i < frameT.Count; i++) frame.AddVertexAt(i, new Point2d(frameT[i].X + sv.X, frameT[i].Y + sv.Y), 0, 0, 0);
                    frame.Closed = true;
                    tg.Frame = Add(frame, BeamStore.RoleFrame);
                    frame.ColorIndex = BeamDetailFrameColor;
                    if (!dashed.IsNull) { frame.LinetypeId = dashed; frame.LinetypeScale = BeamDetailFrameLtScale; }

                    // linie betonu jako osobne odcinki (Line) — nigdy zamknięty obrys przystający do płyty (rzuty w RC_SECTION);
                    // kolor i rodzaj linii jak obiekt na rzucie (obrys / linia uskoku)
                    void AddLines(List<((double X, double Y) A, (double X, double Y) B)> segs, (Color color, ObjectId lt, double lts)? st)
                    {
                        foreach (var (a, b) in segs)
                        {
                            var ln = new Line(new Point3d(a.X + sv.X, a.Y + sv.Y, 0), new Point3d(b.X + sv.X, b.Y + sv.Y, 0));
                            Add(ln, BeamStore.RoleLine);
                            if (st == null) continue;
                            ln.Color = st.Value.color;
                            if (!st.Value.lt.IsNull) { ln.LinetypeId = st.Value.lt; ln.LinetypeScale = st.Value.lts; }
                        }
                    }
                    AddLines(lines, outlStyle);
                    AddLines(nibLines, nibStyle);
                    // dodatkowe linie (uskok w poprzek wspornika) — bez dublowania linii uskoku nibu już narysowanej w tym miejscu
                    if (extraLines != null)
                        AddLines(extraLines.Where(x =>
                        {
                            var m = ((x.A.X + x.B.X) / 2, (x.A.Y + x.B.Y) / 2);
                            return !nibLines.Any(s => BeamDetailRules.DistToSegment(s.A, s.B, m) < 1.0);
                        }).Select(x => BeamDetailRules.ClipToPolygon(frameT, x.A, x.B)).SelectMany(x => x).ToList(), nibStyle);
                    // pozostałe linie rzutu w oknie (np. uskok w nibie na końcu belki — uwaga Bartka 2026-10-12 „nie zbiera tego z rzutu”)
                    foreach (var (segsO, st) in others) AddLines(segsO, st);

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
        /// Detal główny identycznego elementu (uwaga Bartka 2026-10-23): ANCHOR innej, istniejącej strefy z tym samym podpisem [5] i bez [6]
        /// (sam jest główny). Null — brak.
        /// </summary>
        private static (string zone, int nr, Vector2d shift, Extents3d frame)? FindDetailMaster(Database db, string signature, string ownZone)
        {
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline apl)) continue;
                    var d = BeamStore.ReadDetail(apl);
                    if (d == null || d.Value.role != BeamStore.RoleAnchor || string.IsNullOrEmpty(d.Value.zone) || XLink.Same(d.Value.zone, ownZone)) continue;
                    var g = BeamStore.ReadDetailGroup(apl);
                    if (g.signature != signature || !string.IsNullOrEmpty(g.master)) continue;
                    if (!XLink.TryParse(d.Value.zone, out long h) || !db.TryGetObjectId(new Handle(h), out var zid) || zid.IsErased) continue;
                    if (!(tr.GetObject(zid, OpenMode.ForRead) is Polyline zpl)) continue;
                    var zv = EffectiveZoneOf(tr, db, zpl).Select(v => (v.X, v.Y)).ToList();
                    var av = GeometryHelper.GetPolylineVertices(apl).Select(v => (v.X, v.Y)).ToList();
                    var sh = BeamDetailRules.ShiftOf(av, zv);
                    // ramka detalu głównego (FRAME) — wysokość miejsca na kopię
                    Extents3d fr = apl.GeometricExtents;
                    foreach (var (fid, _, role) in BeamStore.FindDetail(tr, db, d.Value.zone))
                        if (role == BeamStore.RoleFrame && tr.GetObject(fid, OpenMode.ForRead) is Entity fe) { fr = fe.GeometricExtents; break; }
                    tr.Commit();
                    return (d.Value.zone, d.Value.nr, new Vector2d(sh.X, sh.Y), fr);
                }
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.FindDetailMaster", ex); }
            return null;
        }

        /// <summary>
        /// Miejsce kopii rozkładów nad detalem głównym: przesunięcie detalu głównego + k · (wysokość ramki + 2 ramiona opisów + 1500) w górę,
        /// najmniejsze k ≥ 1 niezajęte przez inne kopie tego detalu.
        /// </summary>
        private static (double X, double Y) CopySlotShift(Database db, (string zone, int nr, Vector2d shift, Extents3d frame) m, string ownZone)
        {
            double pitch = (m.frame.MaxPoint.Y - m.frame.MinPoint.Y) + 2 * BeamDetailLeaderArm * BeamDetailAnnotScale + 1500.0;
            var used = new HashSet<int>();
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Polyline apl)) continue;
                    var d = BeamStore.ReadDetail(apl);
                    if (d == null || d.Value.role != BeamStore.RoleAnchor || XLink.Same(d.Value.zone, ownZone)) continue;
                    if (!XLink.Same(BeamStore.ReadDetailGroup(apl).master, m.zone)) continue;
                    double y = apl.GeometricExtents.MinPoint.Y;
                    double y0 = m.frame.MinPoint.Y;   // ramka ≈ ANCHOR − margines; k z różnicy dolnych krawędzi
                    used.Add((int)Math.Round((y - y0) / pitch));
                }
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.CopySlotShift", ex); }
            int k = 1;
            while (used.Contains(k)) k++;
            return (m.shift.X, m.shift.Y + k * pitch);
        }

        /// <summary>
        /// Tytuł detalu belki / wspornika: „DETAIL 'n'” (podkreślony), przy kilku identycznych elementach „{count}No.” (podkreślony —
        /// wzór Bartka 2026-10-26), „SCALE 1:25”.
        /// </summary>
        private static string BeamDetailTitle(int nr, int count)
            => "{\\H3.125x;\\L\\C4;DETAIL '" + nr + "'" + (count > 1 ? "\\P" + count + "No." : "")
               + "\\P\\H0.8x;\\l\\C256;SCALE 1:" + DetailScale + " }";

        /// <summary>
        /// Ramka z przekątnymi wokół kopii rozkładów identycznych elementów nad detalem głównym (wzór Bartka 2026-10-23): obwiednia obiektów
        /// detali i rozkładów stref z [6] = <paramref name="masterZone"/> + 300; stara ramka (COPYBOX) usuwana. Zwraca liczbę kopii.
        /// </summary>
        private static int RefreshCopyBox(Database db, string masterZone, int nr)
        {
            int copies = 0;
            try
            {
                var old = new List<ObjectId>();
                double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                    var copyZones = new List<string>();
                    foreach (ObjectId id in ms)
                    {
                        if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                        var d = BeamStore.ReadDetail(e);
                        if (d == null) continue;
                        if (d.Value.role == BeamStore.RoleCopyBox && XLink.Same(d.Value.zone, masterZone)) old.Add(id);
                        if (d.Value.role == BeamStore.RoleAnchor && XLink.Same(BeamStore.ReadDetailGroup(e).master, masterZone)) copyZones.Add(d.Value.zone);
                    }
                    copies = copyZones.Count;
                    void Grow(Entity e)
                    {
                        try
                        {
                            var x = e.GeometricExtents;
                            x0 = Math.Min(x0, x.MinPoint.X); y0 = Math.Min(y0, x.MinPoint.Y); x1 = Math.Max(x1, x.MaxPoint.X); y1 = Math.Max(y1, x.MaxPoint.Y);
                        }
                        catch (System.Exception) { }
                    }
                    foreach (var z in copyZones)
                    {
                        foreach (var (id, _, role) in BeamStore.FindDetail(tr, db, z))
                            if (role != BeamStore.RolePlanRef && tr.GetObject(id, OpenMode.ForRead) is Entity e) Grow(e);
                        foreach (var (dist, annot) in BeamStore.FindGenerated(tr, db, z, null))
                            foreach (var oid in new[] { dist, annot })
                                if (!oid.IsNull && !oid.IsErased && tr.GetObject(oid, OpenMode.ForRead) is Entity e) Grow(e);
                    }
                    tr.Commit();
                }
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    // tytuł detalu głównego: liczba identycznych elementów („8No.”, uwaga Bartka 2026-10-26)
                    foreach (var (tid, _, role) in BeamStore.FindDetail(tr, db, masterZone))
                        if (role == BeamStore.RoleTitle && tr.GetObject(tid, OpenMode.ForWrite) is MText tmt)
                            tmt.Contents = BeamDetailTitle(nr, copies + 1);
                    foreach (var id in old) if (!id.IsErased) tr.GetObject(id, OpenMode.ForWrite).Erase();
                    if (copies > 0 && x1 > x0 && y1 > y0)
                    {
                        x0 -= 300; y0 -= 300; x1 += 300; y1 += 300;
                        var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                        var ents = new List<Entity>();
                        var box = new Polyline();
                        box.AddVertexAt(0, new Point2d(x0, y0), 0, 0, 0); box.AddVertexAt(1, new Point2d(x1, y0), 0, 0, 0);
                        box.AddVertexAt(2, new Point2d(x1, y1), 0, 0, 0); box.AddVertexAt(3, new Point2d(x0, y1), 0, 0, 0);
                        box.Closed = true;
                        ents.Add(box);
                        ents.Add(new Line(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0)));
                        ents.Add(new Line(new Point3d(x0, y1, 0), new Point3d(x1, y0, 0)));
                        foreach (var e in ents)
                        {
                            e.SetDatabaseDefaults(db);
                            e.Layer = BeamStore.DetailLayer;
                            e.ColorIndex = 7;
                            ms.AppendEntity(e);
                            tr.AddNewlyCreatedDBObject(e, true);
                            BeamStore.TagDetail(tr, db, e, masterZone, nr, BeamStore.RoleCopyBox);
                        }
                    }
                    tr.Commit();
                }
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.RefreshCopyBox", ex); }
            return copies;
        }

        /// <summary>
        /// Po prętach detalu: kolizje prętów reprezentatywnych w obrysie ANCHOR-a (pręty belki są pomijane — dla porządku jak detal otworu),
        /// tytuł „DETAIL 'n'” / „SCALE 1:25” pod najniższym opisem detalu, na rzutach (dolnym i górnym — <paramref name="topDelta"/> =
        /// przesunięcie rzutu górnego) ramka okna detalu i odnośnik „DETAIL 'n'” do ramki — jak detal otworu (decyzja 2026-10-12).
        /// </summary>
        private static void FinishBeamDetail(Document doc, BeamDrawTarget tg, ObjectId zb, ObjectId zt, Action<string> msg,
                                             List<Point2d> bottomVerts, Vector2d topDelta)
        {
            var db = doc.Database;
            string name = $"belka {tg.ZoneHandle}";
            try
            {
                // decyzja 2026-10-12 („wygaszajmy tak, żeby pręty na siebie nie nachodziły, jak na Generate Mesh”): widoczne pręty
                // rozkładów w ramce detalu (pręty belki, UB 03 w detalu) z odstępem jak na rzucie (250 · 0,5 — ta sama odległość na papierze)
                var area = !tg.Frame.IsNull ? tg.Frame : tg.Anchor;
                if (!area.IsNull) ResolveRepresentativeCollisions(doc, area, RepresentativeClearance * tg.AnnotScale, beamDetail: true);

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
                // kopia rozkładów identycznego elementu (2026-10-23) — bez tytułu (tytuł ma detal główny)
                if (tg.CopyOf == null)
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                    var mt = new MText();
                    mt.SetDatabaseDefaults(db);
                    mt.Layer = BeamStore.DetailLayer;
                    // tytuł jak detal otworu (GenerateHoleDetail): „DETAIL 'n'” 125 (5 mm w 1:25), „SCALE 1:25” 0,8×
                    mt.Contents = BeamDetailTitle(tg.Number, 1);
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

                // rzuty 1:50 — jak detal otworu (decyzja 2026-10-12, wzór — screen Bartka): ramka okna detalu (czerwona przerywana)
                // i odnośnik „DETAIL 'n'” z grotem na ramce, po stronie poza płytą (zwykle krawędź płyty przy belce)
                int refs = 0;
                var dashed = EnsureDashedLinetype(db);
                // tylko rzut dolny — na rzucie górnym bez odnośnika (jak detal otworu; uwaga Bartka 2026-10-12)
                foreach (var (z, d) in new[] { (zb, new Vector2d()) })
                {
                    if (z.IsNull || z.IsErased) continue;
                    // ramka detalu na rzucie — ten sam wielokąt co w detalu (offset strefy L / U, decyzja 2026-10-13)
                    var fp = (tg.FramePlan ?? BeamDetailRules.RectPoly(new BeamDetailRules.Rect(
                                 tg.Box.MinPoint.X - tg.Shift.X, tg.Box.MinPoint.Y - tg.Shift.Y,
                                 tg.Box.MaxPoint.X - tg.Shift.X, tg.Box.MaxPoint.Y - tg.Shift.Y)))
                             .Select(v => (X: v.X + d.X, Y: v.Y + d.Y)).ToList();
                    if (RectilinearPolygons.SignedArea(fp) < 0) fp.Reverse();
                    var fid = DrawPolygon(db, fp, BeamStore.DetailLayer, BeamDetailFrameColor, dashed, BeamDetailFrameLtScale);
                    var verts = bottomVerts?.Select(v => v + d).ToList();
                    // grot na środku boku ramki poza płytą: kolejno góra, dół, prawo, lewo (najdłuższy bok danej strony)
                    var cand = new List<(int pri, double len, Point2d tip, Vector2d dir)>();
                    for (int i = 0; i < fp.Count; i++)
                    {
                        var a = fp[i]; var b = fp[(i + 1) % fp.Count];
                        double ex = b.X - a.X, ey = b.Y - a.Y, len = Math.Sqrt(ex * ex + ey * ey);
                        if (len < 1.0) continue;
                        var n = new Vector2d(ey / len, -ex / len);   // na zewnątrz (CCW)
                        int pri = n.Y > 0.5 ? 0 : n.Y < -0.5 ? 1 : n.X > 0.5 ? 2 : 3;
                        var tip = new Point2d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0);
                        if (verts != null && verts.Count >= 3 && GeometryHelper.IsPointInsidePolygon(verts, tip)) pri += 10;
                        var dir = n + (Math.Abs(n.Y) > 0.5 ? new Vector2d(1, 0) : new Vector2d(0, 1));
                        cand.Add((pri, len, tip, dir));
                    }
                    var side = cand.OrderBy(c => c.pri).ThenByDescending(c => c.len).FirstOrDefault();
                    if (cand.Count == 0) side = (0, 0, new Point2d(fp[0].X, fp[0].Y), new Vector2d(1, 1));
                    var lid = SingleBarEngine.PlaceNoteLeader(db, new[] { new Point3d(side.tip.X, side.tip.Y, 0) },
                                                              new Point3d(side.tip.X + 500 * side.dir.X, side.tip.Y + 500 * side.dir.Y, 0),
                                                              $"DETAIL '{tg.Number}'", BeamDetailRefHeight, 2, NoteTextStyle, BeamStore.DetailLayer);
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        foreach (var id in new[] { fid, lid })
                            if (!id.IsNull)
                                BeamStore.TagDetail(tr, db, (Entity)tr.GetObject(id, OpenMode.ForWrite), tg.ZoneHandle, tg.Number, BeamStore.RolePlanRef);
                        tr.Commit();
                    }
                    if (!lid.IsNull || !fid.IsNull) refs++;
                }
                msg($"{name}: DETAIL '{tg.Number}' — rozkładów w detalu {tg.Dists.Count}, odnośniki na rzutach: {refs}.");
                if (!string.IsNullOrEmpty(tg.Signature))
                {
                    int nc = RefreshCopyBox(db, tg.CopyOf ?? tg.ZoneHandle, tg.Number);
                    if (nc > 0) msg($"{name}: DETAIL '{tg.Number}' — identycznych elementów: {nc + 1} (kopie rozkładów nad detalem: {nc}).");
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("AutoRebar.FinishBeamDetail", ex);
                msg($"{name}: błąd tytułu / odnośników detalu — {ex.Message}");
            }
        }

        /// <summary>Obiekt niewidoczny (obrys obliczeniowy płyty szeregu po Prepare GA, 2026-10-28).</summary>
        private static bool IsHidden(Database db, ObjectId id)
        {
            if (id.IsNull) return false;
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                bool h = tr.GetObject(id, OpenMode.ForRead) is Entity e && !e.Visible;
                tr.Commit();
                return h;
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.IsHidden", ex); return false; }
        }

        /// <summary>
        /// Pozostałe linie rzutu dolnego w oknie detalu (dowolna warstwa poza obiektami programu, SD-Text, SD-Pile…, SD-Thickening-Zone): Line, Polyline
        /// (cięciwy łuków), Arc (cięciwy) — przycięte do okna, bez odcinków leżących na obrysie albo linii uskoku (te rysowane osobno);
        /// kolor / rodzaj linii z obiektu. Np. linia uskoku w nibie na końcu belki.
        /// </summary>
        private static List<(List<((double X, double Y) A, (double X, double Y) B)> segs, (Color color, ObjectId lt, double lts)? style)>
            OtherPlanLines(Database db, BeamDetailRules.Rect win, IList<(double X, double Y)> frame, BeamView bottom, ObjectId innerId,
                           List<(double X, double Y)> outline, List<Point2d> inner, Action<string> msg = null)
        {
            var res = new List<(List<((double X, double Y) A, (double X, double Y) B)>, (Color, ObjectId, double)?)>();
            var innerT = inner?.Select(v => (v.X, v.Y)).ToList();
            bool OnKnown((double X, double Y) p)
                => (outline != null && outline.Count > 2 && BeamDetailRules.DistToBoundary(outline, p) < 1.0) || (innerT != null && innerT.Count > 2 && BeamDetailRules.DistToBoundary(innerT, p) < 1.0);
            var taken = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var skipped = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            void Count(SortedDictionary<string, int> d, string k) { d.TryGetValue(k, out int v); d[k] = v + 1; }
            bool Excluded(string layer)
                => BeamStore.IsBeamLayer(layer) || BeamStore.IsManualLayer(layer)
                   || layer.Equals(BeamStore.DetailLayer, StringComparison.OrdinalIgnoreCase)
                   || layer.Equals("rebar_bottom", StringComparison.OrdinalIgnoreCase)
                   || layer.Equals("rebar_top", StringComparison.OrdinalIgnoreCase)
                   || layer.Equals("SD-Text", StringComparison.OrdinalIgnoreCase)
                   // pale (SD-Pile, SD-Pile Text…) — UWAGA: nie StartsWith("SD-Pile"), bo łapało SD-PILED-RAFT (obrys, uskok)
                   || layer.Equals("SD-Pile", StringComparison.OrdinalIgnoreCase)
                   || layer.StartsWith("SD-Pile ", StringComparison.OrdinalIgnoreCase)
                   || layer.Equals(GaImportEngine.ThickeningLayer, StringComparison.OrdinalIgnoreCase);
            bool Touches(Entity e)
            {
                try
                {
                    var x = e.GeometricExtents;
                    return x.MaxPoint.X >= win.X0 && x.MinPoint.X <= win.X1 && x.MaxPoint.Y >= win.Y0 && x.MinPoint.Y <= win.Y1;
                }
                catch (System.Exception) { return false; }
            }
            // punkty krzywej (cięciwy łuków / splajnów) — Line, Polyline, Polyline2d / 3d, Arc, Spline, Ellipse
            List<(double X, double Y)> PointsOf(Curve c, out bool closed)
            {
                closed = false;
                var pts = new List<(double X, double Y)>();
                if (c is Line ln) { pts.Add((ln.StartPoint.X, ln.StartPoint.Y)); pts.Add((ln.EndPoint.X, ln.EndPoint.Y)); return pts; }
                if (c is Polyline pl)
                {
                    for (int i = 0; i < pl.NumberOfVertices; i++) { var v = pl.GetPoint2dAt(i); pts.Add((v.X, v.Y)); }
                    closed = pl.Closed;
                    return pts;
                }
                if (c is Circle) return pts;   // okręgi (pale) — osobno
                double p0 = c.StartParam, p1 = c.EndParam;
                bool vertexParams = c is Polyline2d || c is Polyline3d;
                int n = vertexParams ? (int)Math.Round(p1 - p0) : 24;
                if (n < 1 || n > 10000) return pts;
                for (int i = 0; i <= n; i++)
                {
                    var q = c.GetPointAtParameter(p0 + (p1 - p0) * i / n);
                    pts.Add((q.X, q.Y));
                }
                return pts;
            }
            void Take(Curve c, (Color, ObjectId, double)? style, string what)
            {
                var pts = PointsOf(c, out bool closed);
                if (pts.Count < 2) return;
                var segs = new List<((double X, double Y) A, (double X, double Y) B)>();
                int n = closed ? pts.Count : pts.Count - 1;
                for (int i = 0; i < n; i++)
                {
                    var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                    // ramka L / U (decyzja 2026-10-13) — przycięcie do wielokąta ramki
                    foreach (var cl in frame != null ? BeamDetailRules.ClipToPolygon(frame, a, b)
                                                     : (BeamDetailRules.ClipToRect(win, a, b) is var r && r.HasValue
                                                        ? new List<((double X, double Y) A, (double X, double Y) B)> { r.Value }
                                                        : new List<((double X, double Y) A, (double X, double Y) B)>()))
                    {
                        var m = ((cl.A.X + cl.B.X) / 2, (cl.A.Y + cl.B.Y) / 2);
                        if (OnKnown(cl.A) && OnKnown(cl.B) && OnKnown(m)) continue;   // obrys / linia uskoku — już narysowane
                        segs.Add(cl);
                    }
                }
                if (segs.Count > 0) { res.Add((segs, style)); Count(taken, what); }
            }
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || id == bottom.OuterId || id == innerId) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e) || !(e is Curve || e is BlockReference)) continue;
                    if (!e.Visible) continue;   // obrysy obliczeniowe płyt szeregu (2026-10-28) — nie ma ich na rysunku GA
                    if (!Touches(e)) continue;
                    // decyzja 2026-10-12 („to, co jest w czerwonej obramówce, program zbiera i daje jako detal”): wszystkie linie rzutu
                    // w oknie, dowolna warstwa — poza obiektami programu (strefy belek, detale, ramki szablonów, pręty, przekroje: XData
                    // RC_… inne niż RC_GA) i warstwami opisów / pali (teksty „xxxTHK” i pale rysowane osobno)
                    string layer = e.Layer ?? "";
                    string what = layer + " / " + e.GetType().Name;
                    if (Excluded(layer)) { Count(skipped, what); continue; }
                    string app = ProgramAppOf(e);
                    if (app != null) { Count(skipped, what + " [" + app + "]"); continue; }
                    if (e is Curve c) { Take(c, SourceStyle(db, id), what); continue; }
                    // blok z GA (np. symbole, linie w bloku) — linie z rozbicia (bez zapisu do rysunku)
                    var br = (BlockReference)e;
                    var parts = new DBObjectCollection();
                    try { br.Explode(parts); }
                    catch (System.Exception) { Count(skipped, what + " (nie rozbija się)"); continue; }
                    var brStyle = SourceStyle(db, id);
                    foreach (DBObject o in parts)
                    {
                        if (o is Curve pc && !(pc is Circle))
                        {
                            (Color, ObjectId, double)? st = brStyle;
                            if (!pc.Color.IsByBlock && !pc.Color.IsByLayer) st = (pc.Color, brStyle?.Item2 ?? ObjectId.Null, brStyle?.Item3 ?? 1.0);
                            Take(pc, st, what + " (blok)");
                        }
                        o.Dispose();
                    }
                }
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.OtherPlanLines", ex); }
            msg?.Invoke("detal: linie z rzutu w ramce — " + (taken.Count == 0 ? "brak" : string.Join(", ", taken.Select(k => $"{k.Key} ×{k.Value}"))) +
                        (skipped.Count == 0 ? "" : "; pominięte: " + string.Join(", ", skipped.Select(k => $"{k.Key} ×{k.Value}"))) + ".");
            return res;
        }

        /// <summary>Aplikacje XData obiektów programu, których detal nie przenosi (pręty, rozkłady, opisy, strefy belek, detale, przekroje,
        /// mapy, tagi przebicia, notki cięcia). Inne XData (np. RC_GA, tagi z pliku GA) — linia przenoszona.</summary>
        private static readonly HashSet<string> ProgramApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            SingleBarEngine.XAppName, "RC_BAR_BLOCK", "RC_BAR", "RC_BAR_ANNOT", "RC_BAR_LABEL", "RC_BAR_LINK",
            "RC_BEAM", "RC_BEAM_DETAIL", "RC_BEAM_MANUAL", "RC_AUTOREBAR_BEAM",
            "RC_DETAIL", "RC_SECTION", "RC_SECTION_PART", "RC_MAP", "RC_PH", "RC_CUT_NOTE", "RC_ADD"
        };

        /// <summary>Aplikacja XData obiektu programu (null — obiekt z rysunku / GA).</summary>
        private static string ProgramAppOf(Entity e)
        {
            using (var rb = e.XData)
            {
                if (rb == null) return null;
                foreach (TypedValue tv in rb)
                {
                    if (tv.TypeCode != (short)DxfCode.ExtendedDataRegAppName) continue;
                    string app = Convert.ToString(tv.Value, CultureInfo.InvariantCulture) ?? "";
                    if (ProgramApps.Contains(app)) return app;
                }
            }
            return null;
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
