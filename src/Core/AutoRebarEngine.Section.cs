using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Teigha.Colors;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// RC_SECTION — przekrój płyty z rzeczywistego cięcia: dane z rysunku (obrys, nib, otwory, pale, rozkłady
    /// z rzutu dolnego i górnego, opis płyty PLOT / SSL / THK) → <see cref="SectionPlanner"/> → rysunek 1:1
    /// (opis „SCALE 1:20”) + znaczniki przekroju na rzutach.
    /// </summary>
    public static partial class AutoRebarEngine
    {
        public const string SectionMarkLayer = "SD-SECTION";
        /// <summary>Opis otuliny nibu nad przekrojem (decyzja 2026-10-16) — warstwa jak na typowym A-A.</summary>
        public const string NibNoteLayer = "SD-Text";
        public const string SectionSlabLayer = "s-Slab";
        public const string SectionPileLayer = "s-Pile";
        public const string SectionBarLayer  = "RC-SECTION-BARS";
        public const string SectionTextLayer = "0-25TEXT";
        public const string SectionTitleLayer = "AP-TEXT";
        public const string SectionDimLayer  = "DIM";
        private const string AxisLayer = PileLayer;   // SD-Pile
        private static readonly string[] SectionDimStyles = { "CADS_DIM_20" };   // „RBCT DIM 20” ma groty 50 i odsunięcie 150 — nie

        public sealed class SectionContext
        {
            public SectionPlanner.Input Input = new SectionPlanner.Input();
            /// <summary>Przesunięcia rzutów tej płyty względem wskazanego (rzut = wskazany + delta), z (0,0).</summary>
            public List<Vector2d> Views = new List<Vector2d>();
            public Point2d P1, P2;
            public string ThicknessSource, SslSource;
            public int NibCount;
            public List<string> Info = new List<string>();

            // ── bezpiecznik przekrojów (odczyt, nie wpływa na rysunek) ──
            /// <summary>Handle („X8”) obrysu wskazanego i obrysu zewnętrznego (NibDetector; bez nibu = wskazany).</summary>
            public string PickedHandle = "", OuterHandle = "";
            /// <summary>MinPoint obrysu zewnętrznego rzutu wskazanego (po wyprostowaniu) — początek współrzędnych względnych.</summary>
            public Point2d MinPoint;
            /// <summary>Punkt patrzenia (wskazany).</summary>
            public Point2d LookPt;
            /// <summary>Grubość i SSL z opisu płyty (null = brak opisu) — przed oknem (P1: nadpisanie ręczne).</summary>
            public double? DescThickness;
            public string DescSsl;
            /// <summary>
            /// Znaczniki przekroju na detalach belek 1:25 / 1:50 (decyzja 2026-10-14): linia cięcia przeniesiona do detalu (rzut strefy +
            /// Shift) i przycięta do ramki detalu; Scale — skala znacznika względem rzutu 1:50 (1:25 → 0,5); Key — „D&lt;nr detalu&gt;”.
            /// Bez wpływu na odcisk przekroju (bezpiecznik) — tylko rysunek znaczników.
            /// </summary>
            public List<(Point2d P1, Point2d P2, double Scale, string Key)> DetailMarks = new List<(Point2d, Point2d, double, string)>();
        }

        /// <summary>
        /// Zbiera dane przekroju dla wskazanego obrysu i linii cięcia p1–p2 (pozioma albo pionowa).
        /// lookPt — punkt po stronie, w którą patrzymy. Null gdy obrys nie jest zamkniętą polilinią.
        /// </summary>
        public static SectionContext PrepareSection(Database db, ObjectId slabId, Point2d p1, Point2d p2, Point2d lookPt)
        {
            // rekord RC_BEAM [38] „bez nibu” (decyzja użytkownika 2026-10-11): nib płyty bez części przy takich belkach (profil bez nibu)
            var nib = WithoutBeamNibs(db, NibDetector.Detect(db, slabId));
            ObjectId outerId = nib != null ? nib.OuterId : slabId;
            var ctx = new SectionContext();
            var inp = ctx.Input;
            // wersja 3 (Z1–Z6): jeden styl zakończeń prętów w programie (stała planera); przeliczenie (RecomputeSection) idzie tą samą drogą
            inp.EndStyle = SectionPlanner.DefaultEndStyle;

            List<Point2d> verts;
            string outerHandle;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                if (!(tr.GetObject(outerId, OpenMode.ForRead) is Polyline pl) || !GeometryHelper.IsEffectivelyClosed(pl)) return null;
                // POPRAWKI 25: obrys prawie osiowy (≤ 4 mm) — kopia wyprostowana, spójna z krawędziami nibu; idealny — bez zmian
                verts = GeometryHelper.SquaredOrSame(GeometryHelper.GetPolylineVertices(pl));
                outerHandle = pl.Handle.ToString();
            }
            inp.Outline = verts.Select(v => (v.X, v.Y)).ToList();
            ctx.PickedHandle = XLink.Norm(slabId.Handle.ToString());
            ctx.OuterHandle = XLink.Norm(outerHandle);
            ctx.MinPoint = MinPoint(verts);
            ctx.LookPt = lookPt;

            // Linia cięcia: pozioma albo pionowa (wg dłuższej składowej)
            inp.CutAlongX = Math.Abs(p2.X - p1.X) >= Math.Abs(p2.Y - p1.Y);
            if (inp.CutAlongX) { inp.CutCoord = p1.Y; inp.S0 = p1.X; inp.S1 = p2.X; p2 = new Point2d(p2.X, p1.Y); }
            else               { inp.CutCoord = p1.X; inp.S0 = p1.Y; inp.S1 = p2.Y; p2 = new Point2d(p1.X, p2.Y); }
            ctx.P1 = p1; ctx.P2 = p2;
            double lookPerp = inp.CutAlongX ? lookPt.Y : lookPt.X;
            inp.LookSign = lookPerp >= inp.CutCoord ? 1 : -1;

            // Nib
            if (nib != null)
            {
                foreach (var e in nib.Edges)
                    inp.Nibs.Add(new SectionPlanner.NibEdge
                        { Vertical = e.Vertical, OuterCoord = e.OuterCoord, InnerCoord = e.InnerCoord, Lo = e.Lo, Hi = e.Hi });
                ctx.NibCount = nib.Edges.Count;
            }

            // Otwory i pale (rzut wskazany)
            foreach (var h in FindHoles(db, outerId, verts)) inp.Holes.Add((h.MinX, h.MinY, h.MaxX, h.MaxY));
            foreach (var (c, r) in FindPiles(db, verts)) inp.Piles.Add((c.X, c.Y, r));

            // Rzuty tej płyty (ten sam kształt, przesunięte) i rozkłady z każdego z nich
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                var dists = new List<(BlockReference br, BarData bar)>();
                var outlines = new List<Polyline>();
                var texts = new List<(Point2d p, string t)>();
                var beamZones = new List<List<Point2d>>();   // strefy belek (RC-BEAM): teksty „450THK” to nie grubość płyty
                // strefy belek z rekordem RC_BEAM (plan belek E5): handle, wierzchołki, punkt wewnątrz, parametry
                var zoneRecs = new List<(string handle, List<Point2d> verts, Point2d inside, BeamParams p)>();
                // K9: detale belek 1:25 — ANCHOR (kopia strefy): strefa, numer, wierzchołki
                var anchors = new List<(string zone, int nr, List<Point2d> verts)>();
                // znaczniki przekroju na detalach (2026-10-14): ramki detali (FRAME) i skala z tytułu („SCALE 1:25”)
                var frames = new List<(string zone, int nr, List<Point2d> verts)>();
                var detailScale = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                foreach (ObjectId oid in ms)
                {
                    if (oid.IsErased) continue;
                    var obj = tr.GetObject(oid, OpenMode.ForRead);
                    if (obj is BlockReference br)
                    {
                        var bar = BarBlockEngine.ReadXData(br);
                        if (bar == null || Math.Abs(br.Rotation) > 1e-6 || Math.Abs(bar.Angle) > 1e-6) continue;
                        // Pręty belki (BB / BT / BL, E5): do planera tylko jako pręty belki (rodzaj Beam — w pasie belki przeciętym
                        // linią cięcia), bez wpływu na poziomy siatki płyty (dB1 / dT1) i UB płyty
                        dists.Add((br, bar));
                    }
                    else if (obj is Polyline pl && oid != outerId && GeometryHelper.IsEffectivelyClosed(pl))
                    {
                        // K9: ANCHOR detalu belki — nie obrys rzutu
                        if (BeamStore.ReadDetail(pl) is var dd && dd != null)
                        {
                            if (dd.Value.role == BeamStore.RoleAnchor && !string.IsNullOrEmpty(dd.Value.zone))
                                anchors.Add((dd.Value.zone, dd.Value.nr, GeometryHelper.GetPolylineVertices(pl)));
                            else if (dd.Value.role == BeamStore.RoleFrame && !string.IsNullOrEmpty(dd.Value.zone))
                                frames.Add((dd.Value.zone, dd.Value.nr, GeometryHelper.GetPolylineVertices(pl)));
                            continue;
                        }
                        outlines.Add(pl);
                        // strefa belki (RC-BEAM) albo polilinia ręczna belki (RC-BEAM-MANUAL, przed zamianą na strefę) — „450THK” w niej to nie grubość płyty
                        if (BeamStore.IsBeamLayer(pl.Layer) || BeamStore.IsManualLayer(pl.Layer)) beamZones.Add(GeometryHelper.GetPolylineVertices(pl));
                        if (BeamStore.IsZone(pl) && BeamStore.Read(pl) is BeamParams bp)
                        {
                            try { zoneRecs.Add((XLink.Norm(pl.Handle.ToString()), GeometryHelper.GetPolylineVertices(pl), BeamStore.Centroid(pl), bp)); }
                            catch (System.Exception ex) { Log.Error("AutoRebar.PrepareSection.Beam", ex); }
                        }
                    }
                    else if (obj is MText mt)
                    {
                        // tytuł detalu belki — skala detalu (nie opis płyty)
                        if (BeamStore.ReadDetail(mt) is var td && td != null)
                        {
                            if (td.Value.role == BeamStore.RoleTitle && !string.IsNullOrEmpty(td.Value.zone)
                                && DetailScaleOf(mt.Contents) is double sc)
                                detailScale[XLink.Norm(td.Value.zone)] = sc;
                            continue;
                        }
                        texts.Add((new Point2d(mt.Location.X, mt.Location.Y), string.Join("\n", GaImportEngine.TextLines(mt))));
                    }
                    else if (obj is DBText dt)
                        texts.Add((new Point2d(dt.Position.X, dt.Position.Y), dt.TextString));
                }

                var views = new List<(Vector2d delta, string handle, List<Point2d> poly)> { (new Vector2d(0, 0), outerHandle, verts) };
                var min0 = MinPoint(verts);
                foreach (var pl in outlines)
                {
                    var v = GeometryHelper.SquaredOrSame(GeometryHelper.GetPolylineVertices(pl));   // jak FindBottomView
                    if (v.Count != verts.Count) continue;
                    var delta = MinPoint(v) - min0;             // rzut = wskazany + delta
                    if (delta.Length < 1.0 || !Congruent(v, verts, delta)) continue;
                    if (views.Any(x => (x.delta - delta).Length < 1.0)) continue;
                    views.Add((delta, pl.Handle.ToString(), v));
                }

                // K9 (decyzja użytkownika 2026-10-11): pręty belki w detalu 1:25 — rozkład ze znacznikiem strefy ANCHOR-a i środkiem w oknie
                // detalu liczony w położeniu rzutu (pos − Shift); dalej jak rozkład na rzucie (− przesunięcie rzutu strefy). ctx.Views bez
                // zmian — wejście planera jak z prętami na rzucie. ANCHOR nieprzystający do strefy (strefa zmieniona po detalu) — pominięty.
                var detailOf = new Dictionary<string, (Vector2d shift, BeamDetailRules.Rect win)>(StringComparer.OrdinalIgnoreCase);
                foreach (var (zh, anr, av) in anchors)
                {
                    var z = zoneRecs.FirstOrDefault(r => XLink.Same(r.handle, zh));
                    if (z.verts == null) continue;
                    var zview = views.FirstOrDefault(v => GeometryHelper.IsPointInsidePolygon(v.poly, z.inside));
                    if (zview.poly == null) continue;   // strefa innej płyty
                    // ANCHOR = strefa efektywna (rekord [38] „bez nibu” — przesunięta do krawędzi rzutu strefy)
                    var zt = EffectiveZoneVerts(z.verts, z.p, zview.poly).Select(v => (v.X, v.Y)).ToList();
                    var at = av.Select(v => (v.X, v.Y)).ToList();
                    var s = BeamDetailRules.ShiftOf(at, zt);
                    if (!BeamDetailRules.Congruent(at, zt, s))
                    {
                        ctx.Info.Add($"Detal belki {anr} nie pasuje do strefy {XLink.Norm(zh)} — ponów Beam; pręty tego detalu pominięte.");
                        continue;
                    }
                    var w = BeamDetailRules.Window(zt, zview.poly.Select(v => (v.X, v.Y)).ToList()).Offset(s.X, s.Y).Inflate(BeamDetailRules.Margin);
                    detailOf[XLink.Norm(zh)] = (new Vector2d(s.X, s.Y), w);

                    // znacznik przekroju na detalu (2026-10-14): cięcie rzutu wskazanego → rzut strefy (+ delta) → detal (+ Shift),
                    // przycięte do ramki detalu (bez ramki — do okna detalu); skala znacznika = skala detalu / 50
                    try
                    {
                        var off = zview.delta + new Vector2d(s.X, s.Y);
                        var fr = frames.FirstOrDefault(f => XLink.Same(f.zone, zh) && f.nr == anr);
                        var poly = fr.verts != null && fr.verts.Count >= 3 ? fr.verts
                                 : new List<Point2d> { new Point2d(w.X0, w.Y0), new Point2d(w.X1, w.Y0), new Point2d(w.X1, w.Y1), new Point2d(w.X0, w.Y1) };
                        var clip = ClipToPolygon(ctx.P1 + off, ctx.P2 + off, poly);
                        if (clip.HasValue && !ctx.DetailMarks.Any(m => m.Key == "D" + anr))
                        {
                            double sc = detailScale.TryGetValue(XLink.Norm(zh), out var ds) ? ds : DetailScale;
                            ctx.DetailMarks.Add((clip.Value.a, clip.Value.b, sc / 50.0, "D" + anr));
                        }
                    }
                    catch (System.Exception ex) { Log.Error("AutoRebar.PrepareSection.DetailMark", ex); }
                }

                int nDist = 0, foreignDists = 0;
                var staleTag = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                var shapes = new Dictionary<string, (List<(double X, double Y)> pts, string code, double[] dims)?>(StringComparer.OrdinalIgnoreCase);
                foreach (var (br, bar) in dists)
                {
                    var pos = new Point2d(br.Position.X, br.Position.Y);
                    // decyzja 2026-10-11: UB 03 przy belce w detalu (znacznik strefy) — tak samo jak pręty belki
                    if (detailOf.Count > 0 && (BeamGeometry.IsBeamCode(bar.LayerCode) || SingleBarEngine.ExtractPosNr((bar.Mark ?? "").Split(' ')[0]) == NibUBPosNr)
                        && BeamStore.ReadTag(br) is string btag && btag.Length > 0
                        && detailOf.TryGetValue(XLink.Norm(btag), out var det))
                    {
                        var mid = BeamStore.MidOf(br, bar);
                        if (det.win.Contains(mid.X, mid.Y)) pos -= det.shift;
                    }
                    string tag = ReadSlabTag(br);
                    bool tagOfView = tag != null && views.Any(v => SameHandle(tag, v.handle));
                    foreach (var (delta, handle, poly) in views)
                    {
                        bool inside;
                        if (tag == null) inside = GeometryHelper.IsPointInsidePolygon(poly, pos);
                        else if (tagOfView) inside = SameHandle(tag, handle);
                        else
                        {
                            // 2026-10-16: znacznik obrysu, którego już nie ma (obrys narysowany od nowa, dawny obrys = linia uskoku)
                            // — po położeniu; znacznik innej, istniejącej płyty — pominięty z komunikatem
                            string key = tag + "|" + handle;
                            if (!staleTag.TryGetValue(key, out bool st)) staleTag[key] = st = SlabTagIsStale(db, tag, poly);
                            inside = GeometryHelper.IsPointInsidePolygon(poly, pos);
                            if (inside && !st) { foreignDists++; inside = false; }
                        }
                        if (!inside) continue;
                        string pos2 = SectionPlanner.PosNrOf(bar.Mark);
                        bool beamBar = BeamGeometry.IsBeamCode(bar.LayerCode);
                        inp.Dists.Add(new SectionPlanner.Dist
                        {
                            Mark = bar.Mark ?? "", PosNr = pos2, LayerCode = string.IsNullOrEmpty(bar.LayerCode) ? "B1" : bar.LayerCode,
                            Kind = beamBar ? SectionPlanner.DistKind.Beam : SectionPlanner.KindOf(bar.Mark, pos2, NibUBPosNr),
                            AlongX = !string.Equals(bar.Direction, "Y", StringComparison.OrdinalIgnoreCase),
                            X0 = pos.X - delta.X, Y0 = pos.Y - delta.Y,
                            LengthA = bar.LengthA, BarsSpan = bar.BarsSpan, Count = Math.Max(1, bar.Count),
                            Spacing = bar.Spacing > 0 ? bar.Spacing : 200, Diameter = bar.Diameter > 0 ? bar.Diameter : 10,
                            ShapeCode = bar.ShapeCode ?? "00", SymbolSide = bar.SymbolSide ?? "",
                            SourceHandle = XLink.Norm(br.Handle.ToString()), SourceBarHandle = XLink.Norm(bar.SourceBarHandle)
                        });
                        // kopia pręta wzorcowego (RC_SINGLE_BAR) — kształt i wymiary jak w elewacji
                        var shape = SourceShape(db, tr, bar.SourceBarHandle, shapes);
                        if (shape.HasValue)
                        {
                            var last = inp.Dists[inp.Dists.Count - 1];
                            last.ShapeLocal = shape.Value.pts;
                            last.ShapeCode = shape.Value.code;
                            last.SourceDims = shape.Value.dims;
                        }
                        if (beamBar)
                        {
                            // belka (E5): skos rozkładu prętów L; U-bar (21) — drugie ramię o B − d dalej w stronę symbolu zagięcia
                            // (pręty X: „Up” = +Y; pręty Y: „Up” = −X — jak BeamBent)
                            var last = inp.Dists[inp.Dists.Count - 1];
                            last.SkewStart = bar.SkewStart; last.SkewEnd = bar.SkewEnd;
                            // E4b: pręty boczne — w rzucie po jednym na bok, opis / BBS: CountDisplay = 2 × liczba na bok
                            if (bar.LayerCode == BeamGeometry.CodeSide && bar.Count > 0)
                                last.Layers = Math.Max(1, bar.EffectiveCount / bar.Count);
                            // strzemiona 4-cięte (decyzja użytkownika 2026-10-11): CountDisplay = 2 × Count → Layers 2 (dwa strzemiona w widoku);
                            // 2-cięte (bez CountDisplay) — Layers 1, jak dotąd
                            if (bar.LayerCode == BeamGeometry.CodeLinks && bar.Count > 0 && bar.EffectiveCount == 2 * bar.Count)
                                last.Layers = 2;
                            // K3: pionowe U-bary (BU) — bez drugiego ramienia w poprzek (przekrój U-barów pionowych: krok K6)
                            if (last.ShapeCode == "21" && bar.LayerCode != BeamGeometry.CodeUBar
                                && last.SourceDims != null && last.SourceDims.Length > 1 && last.SourceDims[1] > last.Diameter)
                            {
                                bool up = !string.Equals(bar.SymbolDirection, "Down", StringComparison.OrdinalIgnoreCase);
                                int sgn = last.AlongX ? (up ? 1 : -1) : (up ? -1 : 1);
                                last.PairOffset = sgn * (last.SourceDims[1] - last.Diameter);
                            }
                        }
                        nDist++;
                        break;
                    }
                }
                ctx.Views = views.Select(v => v.delta).ToList();

                // Belki (E5): strefy RC_BEAM rzutów tej płyty (punkt wewnątrz strefy w rzucie), przesunięte do rzutu wskazanego,
                // bez duplikatów (ta sama strefa na rzucie dolnym i górnym — pierwszeństwo rzutu wskazanego) → pasy na linii cięcia
                var placed = new List<List<Point2d>>();
                foreach (var (delta, _, poly) in views)
                    foreach (var z in zoneRecs)
                    {
                        if (!GeometryHelper.IsPointInsidePolygon(poly, z.inside)) continue;
                        var zv = z.verts.Select(v => new Point2d(v.X - delta.X, v.Y - delta.Y)).ToList();
                        if (placed.Any(o => o.Count == zv.Count && zv.All(v => o.Any(w => w.GetDistanceTo(v) < 1.0)))) continue;
                        placed.Add(zv);
                        var p = z.p;
                        // rekord [38] „bez nibu”: pas belki do krawędzi płyty (bez nibu belki); domyślnie strefa bez zmian
                        if (p.NoNib) zv = EffectiveZoneVerts(zv, p, verts);
                        foreach (var (a, b) in SectionPlanner.CutIntervals(zv.Select(v => (v.X, v.Y)).ToList(), inp.CutAlongX, inp.CutCoord))
                            inp.Beams.Add(new SectionPlanner.BeamBand
                            {
                                Handle = z.handle, S0 = a, S1 = b, Height = p.Height, NibWidth = p.NibWidth, NibHeight = p.NibHeight,
                                Cover = p.CoverSide, CoverBottom = p.CoverBottom, StirrupDia = p.StirrupDia,
                                StirrupA = BeamGeometry.StirrupHeight(p.Height, p.StirrupTopLayers, BeamSlabTopDia, BeamSlabTopDia,
                                                                      BeamGeometry.DefaultCoverTop, p.CoverBottom),
                                Params = BeamParamsText(p)
                            });
                    }

                // Pale z innych rzutów (gdy na wskazanym ich nie ma)
                if (inp.Piles.Count == 0)
                    foreach (var (delta, _, poly) in views.Skip(1))
                    {
                        foreach (var (c, r) in FindPiles(db, poly)) inp.Piles.Add((c.X - delta.X, c.Y - delta.Y, r));
                        if (inp.Piles.Count > 0) break;
                    }

                // Opis płyty: grubość i SSL
                foreach (var (p, t) in texts)
                {
                    if (!views.Any(v => GeometryHelper.IsPointInsidePolygon(v.poly, p))) continue;
                    if (ctx.ThicknessSource == null && !beamZones.Any(z => GeometryHelper.IsPointInsidePolygon(z, p))
                        && SectionPlanner.ParseThickness(t) is double thk)
                    { inp.Thickness = thk; ctx.ThicknessSource = Flat(t); ctx.DescThickness = thk; }
                    if (ctx.SslSource == null && SectionPlanner.ParseSsl(t) is string ssl)
                    { inp.Ssl = ssl; ctx.SslSource = Flat(t); ctx.DescSsl = ssl; }
                }

                ctx.Info.Add($"Rzuty płyty: {views.Count}, rozkłady: {nDist}, nib: {(nib != null ? nib.Edges.Count + " krawędzi" : "brak")}, " +
                             $"otwory: {inp.Holes.Count}, pale: {inp.Piles.Count}" +
                             (inp.Beams.Count > 0 ? $", belki na linii cięcia: {inp.Beams.Count}." : "."));
                if (foreignDists > 0)
                    ctx.Info.Add($"Pominięto {foreignDists} rozkład(ów) w obrysie przypisanych do innego obrysu płyty — jeśli należą do tej płyty, " +
                                 "uruchom RC_RELINK (przypisz rozkłady do obrysu).");
                if (ctx.ThicknessSource == null) ctx.Info.Add("Nie znaleziono grubości płyty w opisie (… THK SLAB) — przyjęto 225.");
                if (ctx.SslSource == null) ctx.Info.Add("Nie znaleziono SSL w opisie płyty.");
            }
            return ctx;
        }

        /// <summary>Skala detalu z tytułu („SCALE 1:25” → 25); null gdy brak.</summary>
        internal static double? DetailScaleOf(string contents)
        {
            var m = System.Text.RegularExpressions.Regex.Match(contents ?? "", @"SCALE\s*1\s*:\s*(\d+(?:\.\d+)?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0) return v;
            return null;
        }

        /// <summary>Odcinek a–b przycięty do wielokąta (pierwszy wejściowy i ostatni wyjściowy punkt części w środku); null gdy poza.</summary>
        internal static (Point2d a, Point2d b)? ClipToPolygon(Point2d a, Point2d b, List<Point2d> poly)
        {
            var d = b - a;
            if (d.Length < 1e-9 || poly == null || poly.Count < 3) return null;
            var ts = new List<double> { 0, 1 };
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Count]; var e = q - p;
                double den = d.X * e.Y - d.Y * e.X;
                if (Math.Abs(den) < 1e-12) continue;
                var w = p - a;
                double t = (w.X * e.Y - w.Y * e.X) / den, u = (w.X * d.Y - w.Y * d.X) / den;
                if (u >= -1e-9 && u <= 1 + 1e-9 && t > 0 && t < 1) ts.Add(t);
            }
            ts.Sort();
            double lo = double.NaN, hi = double.NaN;
            for (int i = 0; i + 1 < ts.Count; i++)
            {
                if (ts[i + 1] - ts[i] < 1e-9) continue;
                var mid = a + d * ((ts[i] + ts[i + 1]) / 2);
                if (!GeometryHelper.IsPointInsidePolygon(poly, mid)) continue;
                if (double.IsNaN(lo)) lo = ts[i];
                hi = ts[i + 1];
            }
            if (double.IsNaN(lo) || (hi - lo) * d.Length < 1.0) return null;
            return (a + d * lo, a + d * hi);
        }

        /// <summary>Oś pręta wzorcowego rozkładu (układ lokalny kształtu), jego kod kształtu i wymiary A–E; null gdy brak.</summary>
        private static (List<(double X, double Y)> pts, string code, double[] dims)? SourceShape(Database db, Transaction tr, string handle,
            Dictionary<string, (List<(double X, double Y)> pts, string code, double[] dims)?> cache)
        {
            if (string.IsNullOrEmpty(handle)) return null;
            if (cache.TryGetValue(handle, out var hit)) return hit;
            (List<(double X, double Y)> pts, string code, double[] dims)? res = null;
            try
            {
                var id = SingleBarEngine.HandleToObjectId(db, handle);
                if (!id.IsNull && !id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Entity ent
                    && SingleBarEngine.ReadBarXData(ent) is BarData sb && sb.LengthA > 0)
                {
                    double d = sb.Diameter > 0 ? sb.Diameter : 10;
                    // W4e (poprawka #13): GetLocalPoints przelicza wymiary zewnętrzne BS 8666 na oś (BarDimensionRules.ToAxis —
                    // ramiona − d/2, wymiar poprzeczny U / spinki − d, łuk osi r + d/2) — dawne AxisParams usunięte (bez
                    // podwójnego odejmowania); obrys pręta w widoku ma dokładnie wymiary z rysunku (UB 225: B = 140 po zewnętrznej).
                    var dims = new[] { sb.LengthA, sb.LengthB, sb.LengthC, sb.LengthD, sb.LengthE };
                    var pts = BarGeometryBuilder.GetLocalPoints(sb.ShapeCode ?? "00", (double[])dims.Clone(), d);
                    if (pts != null && pts.Count >= 2) res = (pts, sb.ShapeCode ?? "00", dims);
                }
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.SourceShape", ex); }
            cache[handle] = res;
            return res;
        }

        /// <summary>Parametry belki do odcisku przekroju (składnik BEAM — bez wpływu na rysunek; InvariantCulture).</summary>
        private static string BeamParamsText(BeamParams p)
            => string.Format(CultureInfo.InvariantCulture, "W{0:0.#}|L{1}@{2:0.#}|B{3}x{4}|T{5}x{6}|U{7}|S{8}|LAP{9:0.#}|MAX{10:0.#}|TL{11}",
                             p.Width, p.StirrupDia, p.StirrupSpacing, p.BottomCount, p.BottomDia, p.TopCount, p.TopDia, p.UBarDia,
                             p.StirrupShape ?? "", p.Lap, p.MaxLength, p.StirrupTopLayers);

        private static string Flat(string t) => System.Text.RegularExpressions.Regex.Replace(t ?? "", @"\s+", " ").Trim();

        /// <summary>Następna wolna litera przekroju (A, B, …) — z tekstów na warstwie SD-SECTION.</summary>
        public static string NextSectionLetter(Database db)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId oid in ms)
                {
                    if (oid.IsErased) continue;
                    var obj = tr.GetObject(oid, OpenMode.ForRead);
                    if (obj is MText mt && string.Equals(mt.Layer, SectionMarkLayer, StringComparison.OrdinalIgnoreCase)) used.Add(string.Join(" ", GaImportEngine.TextLines(mt)).Trim());
                    else if (obj is DBText dt && string.Equals(dt.Layer, SectionMarkLayer, StringComparison.OrdinalIgnoreCase)) used.Add(dt.TextString.Trim());
                }
            }
            for (char ch = 'A'; ch <= 'Z'; ch++) if (!used.Contains(ch.ToString())) return ch.ToString();
            return "A";
        }

        /// <summary>
        /// Rysuje przekrój: <paramref name="topLeft"/> = lewy górny narożnik płyty (poziom SSL) na początku przekroju.
        /// Znaczniki przekroju na wszystkich rzutach płyty. Zwraca liczbę utworzonych obiektów.
        /// rec ≠ null (bezpiecznik przekrojów): części dostają XData RC_SECTION_PART ([1] tytuł, [2] rola, [3] klucz, [4] GUID),
        /// tytuł — RC_SECTION ([1] format, [2] GUID, [3] litera, [4] obrys wskazany, [5] obrys zewnętrzny, [6] sam tytuł) i Xrecord
        /// NA_RC_SECTION (rec, z listą części). Geometria i kolejność obiektów — bez zmian (rec = null: dawny rysunek bez danych).
        /// </summary>
        public static int DrawSection(Database db, SectionContext ctx, SectionPlanner.Result r, Point3d topLeft, string letter,
                                      SectionRecord rec = null)
            => DrawSectionWith(db, ctx, r, topLeft, letter, rec, null);

        /// <summary>
        /// Opcje rysowania przy RC_SECTION_UPDATE (bezpiecznik przekrojów, etap 3): przesunięcia opisów (indeks w Result.Labels),
        /// tytułu i liter znaczników (klucz V&lt;rzut&gt;.P1|P2) względem pozycji domyślnych; DrawMarks = false — bez znaczników
        /// na rzutach (przekrój bez znaczników, np. kopia). null = rysunek jak RC_SECTION (bit w bit).
        /// </summary>
        public sealed class SectionDrawOptions
        {
            public Dictionary<int, (double DX, double DY)> LabelOffsets = new Dictionary<int, (double DX, double DY)>();
            public Vector2d TitleOffset;
            public Dictionary<string, Vector2d> MarkTextOffsets = new Dictionary<string, Vector2d>(StringComparer.Ordinal);
            public bool DrawMarks = true;
        }

        /// <summary>
        /// <see cref="DrawSection"/> z opcjami RC_SECTION_UPDATE (<paramref name="opt"/> = null — bez zmian). Pozycje domyślne
        /// (bez przesunięć) zapisywane w rekordzie: opisy i wierzchołek [0] betonu (PART …|x,y|rodzaj), tytuł (TITLEDEF) —
        /// względem <paramref name="topLeft"/>.
        /// </summary>
        public static int DrawSectionWith(Database db, SectionContext ctx, SectionPlanner.Result r, Point3d topLeft, string letter,
                                          SectionRecord rec, SectionDrawOptions opt)
        {
            int n = 0;
            var parts = new List<(Entity e, string role, string key, (double X, double Y)? def, string kind)>();
            string pRole = null, pKey = null;   // część dodawana przez Add (bezpiecznik); null = bez znacznika
            (double X, double Y)? pDef = null;  // pozycja domyślna części względem topLeft (etap 3)
            string pKind = "";
            Entity titleEnt = null;
            double T = ctx.Input.Thickness;
            double ox = topLeft.X - r.UMin, oy = topLeft.Y;
            Point3d W(double u, double v) => new Point3d(ox + u, oy + v, 0);

            RcTemplateEngine.EnsureStyles(db);   // ROMANS / CADS_DIM_20 z szablonu, gdy brak (2026-10-20)
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureSectionLayer(tr, db, SectionMarkLayer, 1);
                EnsureSectionLayer(tr, db, SectionSlabLayer, 3);
                EnsureSectionLayer(tr, db, SectionPileLayer, 3);
                EnsureSectionLayer(tr, db, SectionBarLayer, 7);
                EnsureSectionLayer(tr, db, SectionTextLayer, 7);
                EnsureSectionLayer(tr, db, SectionTitleLayer, 7);
                EnsureSectionLayer(tr, db, SectionDimLayer, 1);
                EnsureSectionLayer(tr, db, AxisLayer, 7);
                LayerManager.EnsureLinetype(tr, db, "DASHED");
                LayerManager.EnsureLinetype(tr, db, "CENTER");

                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var stTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
                ObjectId textStyle = stTable.Has("ROMANS NARROW") ? stTable["ROMANS NARROW"]
                                   : stTable.Has("ROMANS") ? stTable["ROMANS"] : db.Textstyle;
                ObjectId titleStyle = stTable.Has("ROMANS") ? stTable["ROMANS"] : textStyle;
                var ltTable = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
                var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
                ObjectId dimStyle = ObjectId.Null;
                foreach (var name in SectionDimStyles) if (dst.Has(name)) { dimStyle = dst[name]; break; }
                if (dimStyle.IsNull) dimStyle = EnsureSectionDimStyle(tr, db, dst, titleStyle);

                void Add(Entity e)
                {
                    ms.AppendEntity(e); tr.AddNewlyCreatedDBObject(e, true); n++;
                    if (e is DBText dbt) dbt.AdjustAlignment(db);
                    if (rec != null && pRole != null) parts.Add((e, pRole, pKey, pDef, pKind));
                }
                void Tag(string role, string key) { pRole = role; pKey = key; pDef = null; pKind = ""; }
                // pozycja domyślna względem topLeft: W(u, v) − topLeft = (u − UMin, v)
                void TagDef(string role, string key, double u, double v, string kind) { Tag(role, key); pDef = (u - r.UMin, v); pKind = kind ?? ""; }
                int idx = 0;
                Polyline Pl(IList<(double U, double V)> pts, string layer, bool closed)
                {
                    var pl = new Polyline { Layer = layer };
                    for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, new Point2d(ox + pts[i].U, oy + pts[i].V), 0, 0, 0);
                    pl.Closed = closed;
                    return pl;
                }
                Line Ln(double u1, double v1, double u2, double v2, string layer, string lt = null, double ltScale = 1, short color = 256)
                {
                    var l = new Line(W(u1, v1), W(u2, v2)) { Layer = layer, ColorIndex = color };
                    if (lt != null && ltTable.Has(lt)) { l.Linetype = lt; l.LinetypeScale = ltScale; }
                    return l;
                }
                // szerokość tekstu ze stylu (ROMANS NARROW 0.8) — DBText jej sam nie bierze
                double widthFactor = 1.0;
                try
                {
                    if (tr.GetObject(textStyle, OpenMode.ForRead) is TextStyleTableRecord tsr && tsr.XScale > 0.01) widthFactor = tsr.XScale;
                }
                catch (System.Exception ex) { Log.Error("AutoRebar.DrawSection.Style", ex); }
                DBText Txt(string t, double u, double v, double h, string layer)
                {
                    var tx = new DBText
                    {
                        TextString = t, Height = h, Layer = layer, TextStyleId = textStyle,
                        HorizontalMode = TextHorizontalMode.TextCenter, VerticalMode = TextVerticalMode.TextBase,
                        WidthFactor = widthFactor
                    };
                    tx.Position = W(u, v);
                    tx.AlignmentPoint = W(u, v);
                    return tx;
                }

                // Beton
                idx = 0;
                foreach (var sg in r.Segments)
                {
                    if (sg.Polygon.Count > 0) TagDef("SLAB", "SEG#" + idx++, sg.Polygon[0].U, sg.Polygon[0].V, "");
                    else Tag("SLAB", "SEG#" + idx++);
                    Add(Pl(sg.Polygon, SectionSlabLayer, true));
                }

                // Pręty w widoku i przekroju
                idx = 0;
                foreach (var b in r.Bars)
                {
                    Tag("BAR", "BAR#" + idx++ + "|" + b.PosNr);
                    Add(b.Outline != null && b.Outline.Count >= 4 ? Pl(b.Outline, SectionBarLayer, true) : Pl(b.Path, SectionBarLayer, false));
                }
                idx = 0;
                foreach (var d in r.Dots)
                {
                    Tag("DOT", "DOT#" + idx++ + "|" + d.PosNr);
                    double q = d.Diameter / 4.0;
                    var pl = new Polyline { Layer = SectionBarLayer };
                    pl.AddVertexAt(0, new Point2d(ox + d.U - q, oy + d.V), 1, d.Diameter / 2.0, d.Diameter / 2.0);
                    pl.AddVertexAt(1, new Point2d(ox + d.U + q, oy + d.V), 1, d.Diameter / 2.0, d.Diameter / 2.0);
                    pl.Closed = true;
                    Add(pl);
                }

                // Pale: część w płycie przerywana, urwanie u dołu, oś
                idx = 0;
                foreach (var p in r.Piles)
                {
                    Tag("PILE", "PILE#" + idx++);
                    double l = p.U - p.Width / 2, rr = p.U + p.Width / 2;
                    double pb = double.IsNaN(p.Base) ? -T : p.Base;   // spód betonu nad palem (belka E5: −H; bez belki = −T)
                    // jak na typowych A-A / B-B (decyzja 2026-10-16): głowica w betonie — czerwona przerywana (górna skala 3, boki 1),
                    // trzon ciągły (warstwa), urwanie u dołu: łuki w dół na całej szerokości + łuk w górę na LEWEJ połowie, oś CENTER skala 7,5
                    Add(Ln(l, p.Top, rr, p.Top, SectionPileLayer, "DASHED", 3, 1));
                    Add(Ln(l, pb, l, p.Top, SectionPileLayer, "DASHED", 1, 1));
                    Add(Ln(rr, pb, rr, p.Top, SectionPileLayer, "DASHED", 1, 1));
                    Add(Ln(l, pb, l, p.Bottom, SectionPileLayer));
                    Add(Ln(rr, pb, rr, p.Bottom, SectionPileLayer));
                    const double bulge = 0.426;
                    var low = new Polyline { Layer = SectionPileLayer };
                    low.AddVertexAt(0, new Point2d(ox + l, oy + p.Bottom), bulge, 0, 0);
                    low.AddVertexAt(1, new Point2d(ox + p.U, oy + p.Bottom), bulge, 0, 0);
                    low.AddVertexAt(2, new Point2d(ox + rr, oy + p.Bottom), 0, 0, 0);
                    Add(low);
                    var up = new Polyline { Layer = SectionPileLayer };
                    up.AddVertexAt(0, new Point2d(ox + l, oy + p.Bottom), -bulge, 0, 0);
                    up.AddVertexAt(1, new Point2d(ox + p.U, oy + p.Bottom), 0, 0, 0);
                    Add(up);
                    // oś pala: warstwa SD-Pile, CENTER, skala linii 7,5, kolor czerwony
                    Add(Ln(p.U, p.Top + 33, p.U, p.Bottom - 35, AxisLayer, "CENTER", 7.5, 1));
                }

                // Wymiary
                idx = 0;
                foreach (var dm in r.Dims)
                {
                    Tag("DIM", "DIM#" + idx++);
                    try
                    {
                        var dim = new RotatedDimension(dm.Vertical ? Math.PI / 2 : 0, W(dm.U1, dm.V1), W(dm.U2, dm.V2),
                                                       W(dm.LineU, dm.LineV), "", dimStyle) { Layer = SectionDimLayer };
                        // jak CADS_DIM_20 — jawnie, żeby zmienne rysunku (np. DIMSCALE 50) nie powiększały wymiarów
                        dim.Dimscale = 20; dim.Dimtxt = 2.5; dim.Dimasz = 3; dim.Dimexo = 2; dim.Dimexe = 2;
                        dim.Dimgap = 1; dim.Dimtad = 1; dim.Dimdec = 0; dim.Dimtix = true;
                        Add(dim);
                    }
                    catch (System.Exception ex) { Log.Error("AutoRebar.DrawSection.Dim", ex); }
                }

                // Opisy numerów pozycji (ze strzałką dla prętów w widoku)
                double minV = -T;
                idx = 0;
                foreach (var lb in r.Labels)
                {
                    int li = idx;
                    string lk = "#" + idx++ + "|" + lb.Text;
                    string kind = SectionLabelCarry.KindOf(lb.HasLeader, r.Dots.Any(d => d.TinNib && d.PosNr == lb.Text));
                    TagDef("LBL", "LBL" + lk, lb.U, lb.V, kind);
                    // RC_SECTION_UPDATE: ręczne przesunięcie opisu (etap 3); bez przesunięcia — dawne wyrażenia bit w bit
                    (double DX, double DY) off = (0, 0);
                    bool moved = opt != null && opt.LabelOffsets.TryGetValue(li, out off);
                    Add(moved ? Txt(lb.Text, lb.U + off.DX, lb.V + off.DY, lb.Height, SectionTextLayer)
                              : Txt(lb.Text, lb.U, lb.V, lb.Height, SectionTextLayer));
                    minV = Math.Min(minV, lb.V);
                    if (!lb.HasLeader) continue;
                    bool down = lb.LeaderV < lb.V;
                    double vStart = down ? lb.V - 10 : lb.V + lb.Height + 10;
                    double tip = lb.LeaderV, ah = down ? 40 : -40;
                    Tag("LDR", "LDR" + lk);
                    if (!moved)
                    {
                        Add(Ln(lb.U, vStart, lb.U, tip + ah, SectionTextLayer));
                        var arrow = new Solid(W(lb.U, tip), W(lb.U - 9, tip + ah), W(lb.U + 9, tip + ah)) { Layer = SectionTextLayer };
                        Tag("ARW", "ARW" + lk);
                        Add(arrow);
                        continue;
                    }
                    // tekst i początek leadera przesunięte; grot o dU tylko, gdy zostaje na pręcie tej pozycji
                    double tu = SectionLabelCarry.LeaderTipU(lb.U, off.DX, r.Bars.Where(b => b.PosNr == lb.Text && b.Path.Count > 0)
                                                                              .Select(b => (b.Path.Min(q => q.U), b.Path.Max(q => q.U))));
                    Add(Ln(lb.U + off.DX, vStart + off.DY, tu, tip + ah, SectionTextLayer));
                    var arrowM = new Solid(W(tu, tip), W(tu - 9, tip + ah), W(tu + 9, tip + ah)) { Layer = SectionTextLayer };
                    Tag("ARW", "ARW" + lk);
                    Add(arrowM);
                }
                foreach (var p in r.Piles) minV = Math.Min(minV, p.Bottom - 110);
                // belka (E5): spód belki i wymiary pod nią (bez belki — beton i wymiary nie schodzą poniżej −T: bez zmian)
                foreach (var sg in r.Segments) foreach (var q in sg.Polygon) minV = Math.Min(minV, q.V);
                foreach (var dm in r.Dims) minV = Math.Min(minV, dm.Vertical ? Math.Min(dm.V1, dm.V2) : dm.LineV - 2 * SectionPlanner.TextHeight);

                // Poziom SSL
                if (r.SslMark.HasValue && !string.IsNullOrEmpty(r.SslText))
                {
                    double tu = r.SslMark.Value.U;
                    var mark = Pl(new List<(double, double)> { (tu, 146), (tu, 0), (tu - 48, 47), (tu + 228, 47) }, SectionTextLayer, false);
                    mark.ColorIndex = 2;
                    Tag("SSL", "SSL.MARK");
                    Add(mark);
                    Tag("SSL", "SSL.LINE");
                    Add(Ln(tu - 56, 0, r.UMin - 15, 0, SectionTextLayer, color: 2));
                    Tag("SSL", "SSL.TEXT");
                    var sslTxt = new MText
                    {
                        Contents = r.SslText, TextHeight = SectionPlanner.TextHeight, Layer = SectionTextLayer, TextStyleId = textStyle,
                        Location = W(tu + 26, 117), Attachment = AttachmentPoint.TopLeft, ColorIndex = 2
                    };
                    Add(sslTxt);
                }

                // decyzja 2026-10-16: przekrój z nibem — nad przekrojem opis otuliny nibu, DOKŁADNIE jak na typowym A-A (SD-Text, kolor 2,
                // ROMANS NARROW 60, ramka); położenie względem krawędzi nibu i wierzchu płyty (prawy nib; lewy — lustrzanie)
                {
                    var nibSeg = r.Segments.FirstOrDefault(x => x.Nib1 > 0) ?? r.Segments.FirstOrDefault(x => x.Nib0 > 0);
                    // decyzja Bartka 2026-10-20: przekrój przez belkę — opis otuliny nibu tylko, gdy żaden inny przekrój go jeszcze nie ma
                    // (opis stoi przy przekrojach nibu A-A / B-B)
                    if (nibSeg != null && r.UsedBeams.Count > 0 && OtherSectionHasNibNote(tr, ms, rec?.Guid)) nibSeg = null;
                    if (nibSeg != null)
                    {
                        bool right = nibSeg.Nib1 > 0;
                        double uE = right ? nibSeg.U1 : nibSeg.U0;
                        const double fw = 1107.069, fOut = 46.509, fv0 = 505.939, fv1 = 723.11, tIn = 27.122, tDown = 25.607;
                        double f0 = right ? uE + fOut - fw : uE - fOut, f1 = f0 + fw;
                        EnsureSectionLayer(tr, db, NibNoteLayer, 2);
                        Tag("NIBNOTE", "NIBNOTE.FRAME");
                        var fr = Pl(new List<(double, double)> { (f0, fv0), (f1, fv0), (f1, fv1), (f0, fv1) }, NibNoteLayer, true);
                        fr.ColorIndex = 2; fr.LineWeight = LineWeight.LineWeight000;
                        if (ltTable.Has("Continuous")) fr.Linetype = "Continuous";
                        fr.LinetypeScale = 100;
                        Add(fr);
                        Tag("NIBNOTE", "NIBNOTE.TEXT");
                        var nt = new MText
                        {
                            Contents = "\\A1;\\pxql;NOMINAL TOP COVER TO NIB\\PREINFORCEMENT- 30mm", TextHeight = 60, Width = 1079.946,
                            Layer = NibNoteLayer, TextStyleId = textStyle, ColorIndex = 2, LineWeight = LineWeight.LineWeight000,
                            Location = W(f0 + tIn, fv1 - tDown), Attachment = AttachmentPoint.TopLeft, LinetypeScale = 100
                        };
                        Add(nt);
                    }
                }

                // Tytuł
                var title = new MText
                {
                    Contents = "{\\H25x;\\L\\C4;SECTION " + letter + "-" + letter + "\\P\\H0.75x;\\l\\C256;SCALE 1:20}",
                    TextHeight = 4, Layer = SectionTitleLayer, TextStyleId = titleStyle,
                    Location = W((r.UMin + r.UMax) / 2, minV - 260), Attachment = AttachmentPoint.MiddleCenter
                };
                if (rec != null) rec.TitleDef = ((r.UMin + r.UMax) / 2 - r.UMin, minV - 260);
                if (opt != null && opt.TitleOffset.Length > 1e-9)
                    title.Location = title.Location + new Vector3d(opt.TitleOffset.X, opt.TitleOffset.Y, 0);   // tytuł przesunięty ręcznie
                Tag(null, null);   // tytuł = głowa przekroju, nie część
                Add(title);
                titleEnt = title;

                // Znaczniki przekroju na rzutach
                var a = ctx.P2 - ctx.P1;
                if (a.Length > 1e-6 && (opt == null || opt.DrawMarks))
                {
                    a = a.GetNormal();
                    var nrm = ctx.Input.CutAlongX ? new Vector2d(0, ctx.Input.LookSign) : new Vector2d(ctx.Input.LookSign, 0);
                    // znacznik (k — skala względem rzutu 1:50; rzuty k = 1, wynik bez zmian)
                    void Mark(Point2d p, Vector2d dir, string mkKey, double k)
                    {
                        Tag("MARK", mkKey);
                        // jak we wzorcu RC (2026-10-15): kolor 2, 0,00 mm
                        var mk = new Polyline { Layer = SectionMarkLayer, ColorIndex = 2, LineWeight = LineWeight.LineWeight000 };
                        var t0 = p + nrm * (294 * k); var t1 = p + dir * (83 * k); var t2 = p + dir * (300 * k);
                        mk.AddVertexAt(0, t1, 0, 0, 0);
                        mk.AddVertexAt(1, t0, 0, 0, 0);
                        mk.AddVertexAt(2, p, 0, 0, 0);
                        mk.AddVertexAt(3, t2, 0, 0, 0);
                        Add(mk);
                        var lp = p + dir * (214 * k) + nrm * (130 * k);
                        if (opt != null && opt.MarkTextOffsets.TryGetValue(mkKey, out var mo)) lp = lp + mo;   // litera przesunięta ręcznie
                        Tag("MARKTXT", mkKey);
                        Add(new MText
                        {
                            Contents = letter, TextHeight = 150 * k, Layer = SectionMarkLayer, TextStyleId = titleStyle,
                            ColorIndex = 2, LineWeight = LineWeight.LineWeight000,
                            Location = new Point3d(lp.X, lp.Y, 0), Attachment = AttachmentPoint.MiddleCenter,
                            Rotation = ctx.Input.CutAlongX ? 0 : Math.PI / 2   // litera wzdłuż linii cięcia (pionowe cięcie → obrót 90°)
                        });
                    }
                    int vi = 0;
                    foreach (var delta in ctx.Views)
                    {
                        // klucz V<rzut>.P1 / P2 — V0 = rzut wskazany (położenie cięcia przy przeliczeniu: wierzchołek [2])
                        Mark(ctx.P1 + delta, a, "V" + vi + ".P1", 1.0);
                        Mark(ctx.P2 + delta, -a, "V" + vi + ".P2", 1.0);
                        vi++;
                    }
                    // detale belek 1:25 / 1:50 (decyzja 2026-10-14): klucz D<nr>.P1 / P2, znacznik w skali detalu
                    foreach (var dm in ctx.DetailMarks)
                    {
                        Mark(dm.P1, a, dm.Key + ".P1", dm.Scale);
                        Mark(dm.P2, -a, dm.Key + ".P2", dm.Scale);
                    }
                }
                Tag(null, null);
                if (rec != null && titleEnt != null) WriteSectionData(tr, db, ctx, rec, titleEnt, parts);
                tr.Commit();
            }
            return n;
        }

        // ── bezpiecznik przekrojów: dane przekroju (XData, Xrecord) ─────────────

        public const string SectionTitleApp = "RC_SECTION";
        public const string SectionPartApp  = "RC_SECTION_PART";
        public const string SectionStatusLayer = "RC-SECTION-STATUS";
        public const string RoleCloud = "CLOUD";

        /// <summary>Zapis danych przekroju w transakcji rysowania: RegApp, XData części i tytułu, Xrecord rekordu (lista części).</summary>
        private static void WriteSectionData(Transaction tr, Database db, SectionContext ctx, SectionRecord rec, Entity title,
                                             List<(Entity e, string role, string key, (double X, double Y)? def, string kind)> parts)
        {
            EnsureRegApp(tr, db, SectionTitleApp);
            EnsureRegApp(tr, db, SectionPartApp);
            string th = XLink.Norm(title.Handle.ToString());
            rec.Parts.Clear();
            foreach (var (e, role, key, def, kind) in parts)
            {
                e.XData = PartXData(th, role, key, rec.Guid);
                rec.Parts.Add(new SectionRecord.Part
                    { Handle = XLink.Norm(e.Handle.ToString()), Role = role, Key = key, Def = def, Kind = kind ?? "" });
            }
            title.XData = TitleXData(rec.Guid, rec.Letter, ctx.PickedHandle, ctx.OuterHandle, th);
            WriteRecord(tr, title, rec);
        }

        /// <summary>Czy w modelu jest opis otuliny nibu (część NIBNOTE) innego przekroju niż <paramref name="guid"/>.</summary>
        private static bool OtherSectionHasNibNote(Transaction tr, BlockTableRecord ms, string guid)
        {
            foreach (ObjectId id in ms)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                try
                {
                    using var rb = e.GetXDataForApplication(SectionPartApp);
                    var v = rb?.AsArray();
                    if (v == null || v.Length < 3 || Convert.ToString(v[2].Value, CultureInfo.InvariantCulture) != "NIBNOTE") continue;
                    string g = v.Length > 4 ? Convert.ToString(v[4].Value, CultureInfo.InvariantCulture) : "";
                    if (string.IsNullOrEmpty(guid) || g != guid) return true;
                }
                catch (System.Exception ex) { Log.Error("AutoRebar.OtherSectionHasNibNote", ex); }
            }
            return false;
        }

        internal static ResultBuffer PartXData(string titleHandle, string role, string key, string guid)
            => new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, SectionPartApp),
                XLink.Write(titleHandle),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, role ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, key ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, guid ?? ""));

        internal static ResultBuffer TitleXData(string guid, string letter, string picked, string outer, string self)
            => new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, SectionTitleApp),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)SectionRecord.CurrentFormat),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, guid ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, letter ?? ""),
                XLink.Write(picked),
                XLink.Write(outer),
                XLink.Write(self));

        /// <summary>Xrecord NA_RC_SECTION w słowniku rozszerzeń tytułu (nadpisuje poprzedni). Tytuł otwarty do zapisu.</summary>
        internal static void WriteRecord(Transaction tr, Entity title, SectionRecord rec)
        {
            if (title.ExtensionDictionary.IsNull) title.CreateExtensionDictionary();
            var dict = (DBDictionary)tr.GetObject(title.ExtensionDictionary, OpenMode.ForWrite);
            var data = new ResultBuffer(rec.ToLines().Select(l => new TypedValue((int)DxfCode.Text, l)).ToArray());
            if (dict.Contains(SectionRecord.XrecordName))
            {
                var old = (Xrecord)tr.GetObject(dict.GetAt(SectionRecord.XrecordName), OpenMode.ForWrite);
                old.Data = data;
                return;
            }
            var xr = new Xrecord { Data = data };
            dict.SetAt(SectionRecord.XrecordName, xr);
            tr.AddNewlyCreatedDBObject(xr, true);
        }

        /// <summary>Odczyt Xrecordu NA_RC_SECTION tytułu; null gdy brak / nie rekord.</summary>
        internal static SectionRecord ReadRecord(Transaction tr, Entity title)
        {
            if (title == null || title.ExtensionDictionary.IsNull) return null;
            var dict = tr.GetObject(title.ExtensionDictionary, OpenMode.ForRead) as DBDictionary;
            if (dict == null || !dict.Contains(SectionRecord.XrecordName)) return null;
            var xr = tr.GetObject(dict.GetAt(SectionRecord.XrecordName), OpenMode.ForRead) as Xrecord;
            var arr = xr?.Data?.AsArray();
            if (arr == null) return null;
            return SectionRecord.Parse(arr.Select(tv => Convert.ToString(tv.Value, CultureInfo.InvariantCulture)));
        }

        /// <summary>Dane spoza planera do odcisku (punkt odniesienia = P1 cięcia, rzuty, litera, opis płyty, flagi nadpisania).</summary>
        public static SectionFingerprint.Source SectionSourceOf(SectionContext ctx, string letter, bool thkOverridden, bool sslOverridden)
            => new SectionFingerprint.Source
            {
                RefX = ctx.P1.X, RefY = ctx.P1.Y,
                Views = ctx.Views.Select(v => (v.X, v.Y)).ToList(), Letter = letter ?? "",
                DescThickness = ctx.DescThickness, ThkOverridden = thkOverridden,
                DescSsl = ctx.DescSsl, SslOverridden = sslOverridden
            };

        /// <summary>
        /// Rekord nowego przekroju (RC_SECTION, po oknie): parametry z ctx.Input, flagi nadpisania (P1: wartość ≠ opis płyty),
        /// odcisk z policzonego planu. Części dopisuje DrawSection.
        /// </summary>
        public static SectionRecord BuildSectionRecord(SectionContext ctx, SectionPlanner.Result plan, string letter, Point3d topLeft)
            => BuildSectionRecord(ctx, plan, letter, topLeft, null, null, null);

        /// <summary>
        /// Rekord przekroju; RC_SECTION_UPDATE (etap 3) podaje flagi nadpisania z poprzedniego rekordu (P1: nadpisanie zostaje)
        /// i GUID przekroju (ten sam przekrój). null = jak RC_SECTION (flagi z porównania z opisem płyty, nowy GUID).
        /// </summary>
        public static SectionRecord BuildSectionRecord(SectionContext ctx, SectionPlanner.Result plan, string letter, Point3d topLeft,
                                                       bool? thkOverridden, bool? sslOverridden, string guid)
        {
            var inp = ctx.Input;
            bool thkOvr = thkOverridden ?? Math.Abs(inp.Thickness - (ctx.DescThickness ?? new SectionPlanner.Input().Thickness)) > 1e-6;
            bool sslOvr = sslOverridden ?? !string.Equals((inp.Ssl ?? "").Trim(), (ctx.DescSsl ?? "").Trim(), StringComparison.Ordinal);
            var st = SectionFingerprint.Build(inp, plan, SectionSourceOf(ctx, letter, thkOvr, sslOvr));
            return new SectionRecord
            {
                Guid = string.IsNullOrEmpty(guid) ? System.Guid.NewGuid().ToString("D") : guid, Letter = letter ?? "",
                P1 = (ctx.P1.X, ctx.P1.Y), P2 = (ctx.P2.X, ctx.P2.Y),
                P1Rel = (ctx.P1.X - ctx.MinPoint.X, ctx.P1.Y - ctx.MinPoint.Y), P2Rel = (ctx.P2.X - ctx.MinPoint.X, ctx.P2.Y - ctx.MinPoint.Y),
                CutAlongX = inp.CutAlongX, LookSign = inp.LookSign >= 0 ? 1 : -1, LookPt = (ctx.LookPt.X, ctx.LookPt.Y),
                TopLeft = (topLeft.X, topLeft.Y, topLeft.Z), MinPoint = (ctx.MinPoint.X, ctx.MinPoint.Y),
                Thickness = inp.Thickness, NibHeight = inp.NibHeight, CoverBottom = inp.CoverBottom, CoverTop = inp.CoverTop,
                CoverNib = inp.CoverNib, UbTolerance = inp.UbTolerance, Ssl = inp.Ssl, DrawPiles = inp.DrawPiles,
                DescThickness = ctx.DescThickness, DescSsl = ctx.DescSsl, ThkOverridden = thkOvr, SslOverridden = sslOvr,
                Hash = st.Hash, ResultDigest = st.Digest, Version = st.Version, Components = st.Components
            };
        }

        /// <summary>RC_SECTION: rekord (odcisk z policzonego planu) + rysunek z danymi przekroju. Zwraca liczbę obiektów.</summary>
        public static int DrawSectionRecorded(Database db, SectionContext ctx, SectionPlanner.Result plan, Point3d topLeft, string letter)
            => DrawSection(db, ctx, plan, topLeft, letter, BuildSectionRecord(ctx, plan, letter, topLeft));

        /// <summary>Wynik przeliczenia przekroju (RC_SECTION_CHECK) — tylko odczyt.</summary>
        public sealed class SectionRecompute
        {
            public SectionContext Ctx;
            public SectionPlanner.Result Plan;
            public SectionFingerprint.State State;
            /// <summary>Błąd (obrys nie jest zamkniętą polilinią) — przekrój nie do przeliczenia.</summary>
            public string Error;
            /// <summary>Otwarta linia uskoku nibu, którą RC_SECTION by domknął (NibAutoClose.WouldClose).</summary>
            public bool NibOpen;
        }

        /// <summary>
        /// Przeliczenie przekroju tą samą drogą co tworzenie (PrepareSection → parametry z rekordu → Plan → odcisk). Tylko odczyt,
        /// bez domykania nibu (wykrycie otwartego nibu bez zapisu). p1 / p2 — cięcie (ze znacznika rzutu wskazanego albo z rekordu),
        /// strona patrzenia z rekordu. Grubość / SSL: nadpisane ręcznie — z rekordu, inaczej z bieżącego opisu płyty (P1).
        /// </summary>
        public static SectionRecompute RecomputeSection(Database db, SectionRecord rec, ObjectId slabId, Point2d p1, Point2d p2, string letter)
        {
            var res = new SectionRecompute();
            if (slabId.IsNull || slabId.IsErased) { res.Error = "brak obrysu płyty"; return res; }
            res.NibOpen = NibAutoClose.WouldClose(db, slabId);
            bool alongX = Math.Abs(p2.X - p1.X) >= Math.Abs(p2.Y - p1.Y);
            var look = alongX ? new Point2d(p1.X, p1.Y + rec.LookSign) : new Point2d(p1.X + rec.LookSign, p1.Y);
            var ctx = PrepareSection(db, slabId, p1, p2, look);
            if (ctx == null) { res.Error = "obrys płyty nie jest zamkniętą polilinią"; return res; }
            var inp = ctx.Input;
            if (rec.ThkOverridden) inp.Thickness = rec.Thickness;
            if (rec.SslOverridden) inp.Ssl = rec.Ssl;
            inp.NibHeight = rec.NibHeight; inp.CoverBottom = rec.CoverBottom; inp.CoverTop = rec.CoverTop;
            inp.CoverNib = rec.CoverNib; inp.UbTolerance = rec.UbTolerance; inp.DrawPiles = rec.DrawPiles;
            res.Ctx = ctx;
            res.Plan = SectionPlanner.Plan(inp);
            res.State = SectionFingerprint.Build(inp, res.Plan, SectionSourceOf(ctx, letter, rec.ThkOverridden, rec.SslOverridden));
            return res;
        }

        // ── chmurka „NIEAKTUALNY” (warstwa niedrukowana) ─────────────────────

        /// <summary>
        /// Chmurka wokół przekroju (prostokąt <paramref name="ext"/> + 300 mm, łuki co ok. 400 mm) i tekst „NIEAKTUALNY” na warstwie
        /// RC-SECTION-STATUS (kolor 1, Plottable = false — nie trafia do wydruku ani PDF, spike etapu 0). Części roli CLOUD
        /// z powiązaniem do tytułu. Wołać w transakcji komendy (zablokowany dokument).
        /// </summary>
        public static void DrawCloud(Transaction tr, Database db, ObjectId titleId, string guid, Extents3d ext)
        {
            EnsureRegApp(tr, db, SectionPartApp);
            EnsureStatusLayer(tr, db);
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            string th = XLink.Norm(titleId.Handle.ToString());
            const double margin = 300, step = 400;
            double x0 = ext.MinPoint.X - margin, y0 = ext.MinPoint.Y - margin, x1 = ext.MaxPoint.X + margin, y1 = ext.MaxPoint.Y + margin;
            var corners = new[] { new Point2d(x0, y0), new Point2d(x1, y0), new Point2d(x1, y1), new Point2d(x0, y1) };
            var pl = new Polyline();
            pl.SetDatabaseDefaults(db);
            pl.Layer = SectionStatusLayer;
            int vi = 0;
            for (int i = 0; i < 4; i++)
            {
                var a = corners[i]; var b = corners[(i + 1) % 4];
                int k = Math.Max(1, (int)Math.Ceiling(a.GetDistanceTo(b) / step));
                for (int j = 0; j < k; j++)
                    pl.AddVertexAt(vi++, a + (b - a) * ((double)j / k), -0.5, 0, 0);   // łuki na zewnątrz (obieg przeciwny do zegara)
            }
            pl.Closed = true;
            ms.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            pl.XData = PartXData(th, RoleCloud, "CLOUD", guid);

            var txt = new MText();
            txt.SetDatabaseDefaults(db);   // najpierw domyślne rysunku, potem właściwości
            txt.Layer = SectionStatusLayer;
            txt.Contents = "NIEAKTUALNY";
            txt.TextHeight = 150;
            txt.Location = new Point3d(x0 + 100, y1 - 100, 0);
            txt.Attachment = AttachmentPoint.TopLeft;
            ms.AppendEntity(txt);
            tr.AddNewlyCreatedDBObject(txt, true);
            txt.XData = PartXData(th, RoleCloud, "CLOUD.TXT", guid);
        }

        /// <summary>Usuwa chmurki (części roli CLOUD) — wołający podaje tylko potwierdzone obiekty. Zwraca liczbę usuniętych.</summary>
        public static int RemoveCloud(Transaction tr, IEnumerable<ObjectId> cloudIds)
        {
            int n = 0;
            foreach (var id in cloudIds)
            {
                if (id.IsNull || id.IsErased) continue;
                tr.GetObject(id, OpenMode.ForWrite).Erase();
                n++;
            }
            return n;
        }

        private static void EnsureStatusLayer(Transaction tr, Database db)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(SectionStatusLayer)) return;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord
            {
                Name = SectionStatusLayer, Color = Color.FromColorIndex(ColorMethod.ByAci, 1), IsPlottable = false
            };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        public const string SectionOwnDimStyle = "RC SECTION 1-20";

        /// <summary>Styl wymiarów przekroju 1:20 (jak CADS_DIM_20: skala 20, tekst 2.5, groty 2.5, tekst nad linią).</summary>
        private static ObjectId EnsureSectionDimStyle(Transaction tr, Database db, DimStyleTable dst, ObjectId textStyle)
        {
            if (dst.Has(SectionOwnDimStyle)) return dst[SectionOwnDimStyle];
            dst.UpgradeOpen();
            var rec = new DimStyleTableRecord
            {
                Name = SectionOwnDimStyle,
                Dimscale = 20, Dimtxt = 2.5, Dimasz = 3, Dimexo = 2, Dimexe = 2, Dimgap = 1,
                Dimtad = 1, Dimtih = false, Dimtoh = false, Dimdec = 0, Dimtix = true, Dimtxsty = textStyle,
                Dimclrd = Color.FromColorIndex(ColorMethod.ByLayer, 256),
                Dimclre = Color.FromColorIndex(ColorMethod.ByLayer, 256),
                Dimclrt = Color.FromColorIndex(ColorMethod.ByLayer, 256)
            };
            var id = dst.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            return id;
        }

        private static void EnsureSectionLayer(Transaction tr, Database db, string name, short color)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = name, Color = Color.FromColorIndex(ColorMethod.ByAci, color) };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }
    }
}
