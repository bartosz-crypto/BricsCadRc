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
        }

        /// <summary>
        /// Zbiera dane przekroju dla wskazanego obrysu i linii cięcia p1–p2 (pozioma albo pionowa).
        /// lookPt — punkt po stronie, w którą patrzymy. Null gdy obrys nie jest zamkniętą polilinią.
        /// </summary>
        public static SectionContext PrepareSection(Database db, ObjectId slabId, Point2d p1, Point2d p2, Point2d lookPt)
        {
            var nib = NibDetector.Detect(db, slabId);
            ObjectId outerId = nib != null ? nib.OuterId : slabId;
            var ctx = new SectionContext();
            var inp = ctx.Input;

            List<Point2d> verts;
            string outerHandle;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                if (!(tr.GetObject(outerId, OpenMode.ForRead) is Polyline pl) || !GeometryHelper.IsEffectivelyClosed(pl)) return null;
                verts = GeometryHelper.GetPolylineVertices(pl);
                outerHandle = pl.Handle.ToString();
            }
            inp.Outline = verts.Select(v => (v.X, v.Y)).ToList();

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
                foreach (ObjectId oid in ms)
                {
                    if (oid.IsErased) continue;
                    var obj = tr.GetObject(oid, OpenMode.ForRead);
                    if (obj is BlockReference br)
                    {
                        var bar = BarBlockEngine.ReadXData(br);
                        if (bar == null || Math.Abs(br.Rotation) > 1e-6 || Math.Abs(bar.Angle) > 1e-6) continue;
                        dists.Add((br, bar));
                    }
                    else if (obj is Polyline pl && oid != outerId && GeometryHelper.IsEffectivelyClosed(pl))
                        outlines.Add(pl);
                    else if (obj is MText mt)
                        texts.Add((new Point2d(mt.Location.X, mt.Location.Y), string.Join("\n", GaImportEngine.TextLines(mt))));
                    else if (obj is DBText dt)
                        texts.Add((new Point2d(dt.Position.X, dt.Position.Y), dt.TextString));
                }

                var views = new List<(Vector2d delta, string handle, List<Point2d> poly)> { (new Vector2d(0, 0), outerHandle, verts) };
                var min0 = MinPoint(verts);
                foreach (var pl in outlines)
                {
                    var v = GeometryHelper.GetPolylineVertices(pl);
                    if (v.Count != verts.Count) continue;
                    var delta = MinPoint(v) - min0;             // rzut = wskazany + delta
                    if (delta.Length < 1.0 || !Congruent(v, verts, delta)) continue;
                    if (views.Any(x => (x.delta - delta).Length < 1.0)) continue;
                    views.Add((delta, pl.Handle.ToString(), v));
                }

                int nDist = 0;
                var shapes = new Dictionary<string, (List<(double X, double Y)> pts, string code)?>(StringComparer.OrdinalIgnoreCase);
                foreach (var (br, bar) in dists)
                {
                    var pos = new Point2d(br.Position.X, br.Position.Y);
                    string tag = ReadSlabTag(br);
                    foreach (var (delta, handle, poly) in views)
                    {
                        bool inside = tag != null ? SameHandle(tag, handle) : GeometryHelper.IsPointInsidePolygon(poly, pos);
                        if (!inside) continue;
                        string pos2 = SectionPlanner.PosNrOf(bar.Mark);
                        inp.Dists.Add(new SectionPlanner.Dist
                        {
                            Mark = bar.Mark ?? "", PosNr = pos2, LayerCode = string.IsNullOrEmpty(bar.LayerCode) ? "B1" : bar.LayerCode,
                            Kind = SectionPlanner.KindOf(bar.Mark, pos2, NibUBPosNr),
                            AlongX = !string.Equals(bar.Direction, "Y", StringComparison.OrdinalIgnoreCase),
                            X0 = pos.X - delta.X, Y0 = pos.Y - delta.Y,
                            LengthA = bar.LengthA, BarsSpan = bar.BarsSpan, Count = Math.Max(1, bar.Count),
                            Spacing = bar.Spacing > 0 ? bar.Spacing : 200, Diameter = bar.Diameter > 0 ? bar.Diameter : 10,
                            ShapeCode = bar.ShapeCode ?? "00", SymbolSide = bar.SymbolSide ?? ""
                        });
                        // kopia pręta wzorcowego (RC_SINGLE_BAR) — kształt i wymiary jak w elewacji
                        var shape = SourceShape(db, tr, bar.SourceBarHandle, shapes);
                        if (shape.HasValue)
                        {
                            var last = inp.Dists[inp.Dists.Count - 1];
                            last.ShapeLocal = shape.Value.pts;
                            last.ShapeCode = shape.Value.code;
                        }
                        nDist++;
                        break;
                    }
                }
                ctx.Views = views.Select(v => v.delta).ToList();

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
                    if (ctx.ThicknessSource == null && SectionPlanner.ParseThickness(t) is double thk)
                    { inp.Thickness = thk; ctx.ThicknessSource = Flat(t); }
                    if (ctx.SslSource == null && SectionPlanner.ParseSsl(t) is string ssl)
                    { inp.Ssl = ssl; ctx.SslSource = Flat(t); }
                }

                ctx.Info.Add($"Rzuty płyty: {views.Count}, rozkłady: {nDist}, nib: {(nib != null ? nib.Edges.Count + " krawędzi" : "brak")}, " +
                             $"otwory: {inp.Holes.Count}, pale: {inp.Piles.Count}.");
                if (ctx.ThicknessSource == null) ctx.Info.Add("Nie znaleziono grubości płyty w opisie (… THK SLAB) — przyjęto 225.");
                if (ctx.SslSource == null) ctx.Info.Add("Nie znaleziono SSL w opisie płyty.");
            }
            return ctx;
        }

        /// <summary>Oś pręta wzorcowego rozkładu (układ lokalny kształtu) i jego kod kształtu; null gdy brak.</summary>
        private static (List<(double X, double Y)> pts, string code)? SourceShape(Database db, Transaction tr, string handle,
            Dictionary<string, (List<(double X, double Y)> pts, string code)?> cache)
        {
            if (string.IsNullOrEmpty(handle)) return null;
            if (cache.TryGetValue(handle, out var hit)) return hit;
            (List<(double X, double Y)> pts, string code)? res = null;
            try
            {
                var id = SingleBarEngine.HandleToObjectId(db, handle);
                if (!id.IsNull && !id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Entity ent
                    && SingleBarEngine.ReadBarXData(ent) is BarData sb && sb.LengthA > 0)
                {
                    double d = sb.Diameter > 0 ? sb.Diameter : 10;
                    var pts = BarGeometryBuilder.GetLocalPoints(sb.ShapeCode ?? "00",
                        AxisParams(sb.ShapeCode, new[] { sb.LengthA, sb.LengthB, sb.LengthC, sb.LengthD, sb.LengthE }, d), d);
                    if (pts != null && pts.Count >= 2) res = (pts, sb.ShapeCode ?? "00");
                }
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.SourceShape", ex); }
            cache[handle] = res;
            return res;
        }

        /// <summary>
        /// Wymiary BS 8666 są zewnętrzne (UB 225: B = 225 − 40 − 35 − 5 − 5 = 140 po zewnętrznej pręta) — do rysowania osi pręta
        /// w przekroju: ramiona − d/2, wymiar poprzeczny U / spinki − d. Wtedy obrys pręta ma dokładnie wymiary z rysunku.
        /// </summary>
        private static double[] AxisParams(string code, double[] p, double d)
        {
            var a = (double[])p.Clone();
            switch (code ?? "00")
            {
                case "13": case "21": case "22":
                    a[0] = Math.Max(1, a[0] - d / 2); a[1] = Math.Max(1, a[1] - d); a[2] = Math.Max(0, a[2] - d / 2);
                    break;
                case "11": case "12":
                    a[0] = Math.Max(1, a[0] - d / 2); a[1] = Math.Max(1, a[1] - d / 2);
                    break;
            }
            return a;
        }

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
        /// </summary>
        public static int DrawSection(Database db, SectionContext ctx, SectionPlanner.Result r, Point3d topLeft, string letter)
        {
            int n = 0;
            double T = ctx.Input.Thickness;
            double ox = topLeft.X - r.UMin, oy = topLeft.Y;
            Point3d W(double u, double v) => new Point3d(ox + u, oy + v, 0);

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
                }
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
                foreach (var sg in r.Segments) Add(Pl(sg.Polygon, SectionSlabLayer, true));

                // Pręty w widoku i przekroju
                foreach (var b in r.Bars)
                    Add(b.Outline != null && b.Outline.Count >= 4 ? Pl(b.Outline, SectionBarLayer, true) : Pl(b.Path, SectionBarLayer, false));
                foreach (var d in r.Dots)
                {
                    double q = d.Diameter / 4.0;
                    var pl = new Polyline { Layer = SectionBarLayer };
                    pl.AddVertexAt(0, new Point2d(ox + d.U - q, oy + d.V), 1, d.Diameter / 2.0, d.Diameter / 2.0);
                    pl.AddVertexAt(1, new Point2d(ox + d.U + q, oy + d.V), 1, d.Diameter / 2.0, d.Diameter / 2.0);
                    pl.Closed = true;
                    Add(pl);
                }

                // Pale: część w płycie przerywana, urwanie u dołu, oś
                foreach (var p in r.Piles)
                {
                    double l = p.U - p.Width / 2, rr = p.U + p.Width / 2;
                    Add(Ln(l, p.Top, rr, p.Top, SectionPileLayer, "DASHED", 3));
                    Add(Ln(l, -T, l, p.Top, SectionPileLayer, "DASHED", 3));
                    Add(Ln(rr, -T, rr, p.Top, SectionPileLayer, "DASHED", 3));
                    Add(Ln(l, -T, l, p.Bottom, SectionPileLayer));
                    Add(Ln(rr, -T, rr, p.Bottom, SectionPileLayer));
                    const double bulge = 0.426;
                    var low = new Polyline { Layer = SectionPileLayer };
                    low.AddVertexAt(0, new Point2d(ox + l, oy + p.Bottom), bulge, 0, 0);
                    low.AddVertexAt(1, new Point2d(ox + p.U, oy + p.Bottom), bulge, 0, 0);
                    low.AddVertexAt(2, new Point2d(ox + rr, oy + p.Bottom), 0, 0, 0);
                    Add(low);
                    var up = new Polyline { Layer = SectionPileLayer };
                    up.AddVertexAt(0, new Point2d(ox + p.U, oy + p.Bottom), -bulge, 0, 0);
                    up.AddVertexAt(1, new Point2d(ox + rr, oy + p.Bottom), 0, 0, 0);
                    Add(up);
                    // oś pala jak na rysunkach ASD: warstwa SD-Pile, CENTER, skala linii 2, kolor czerwony
                    Add(Ln(p.U, p.Top + 33, p.U, p.Bottom - 35, AxisLayer, "CENTER", 2, 1));
                }

                // Wymiary
                foreach (var dm in r.Dims)
                {
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
                foreach (var lb in r.Labels)
                {
                    Add(Txt(lb.Text, lb.U, lb.V, lb.Height, SectionTextLayer));
                    minV = Math.Min(minV, lb.V);
                    if (!lb.HasLeader) continue;
                    bool down = lb.LeaderV < lb.V;
                    double vStart = down ? lb.V - 10 : lb.V + lb.Height + 10;
                    double tip = lb.LeaderV, ah = down ? 40 : -40;
                    Add(Ln(lb.U, vStart, lb.U, tip + ah, SectionTextLayer));
                    var arrow = new Solid(W(lb.U, tip), W(lb.U - 9, tip + ah), W(lb.U + 9, tip + ah)) { Layer = SectionTextLayer };
                    Add(arrow);
                }
                foreach (var p in r.Piles) minV = Math.Min(minV, p.Bottom - 110);

                // Poziom SSL
                if (r.SslMark.HasValue && !string.IsNullOrEmpty(r.SslText))
                {
                    double tu = r.SslMark.Value.U;
                    var mark = Pl(new List<(double, double)> { (tu, 146), (tu, 0), (tu - 48, 47), (tu + 228, 47) }, SectionTextLayer, false);
                    mark.ColorIndex = 2;
                    Add(mark);
                    Add(Ln(tu - 56, 0, r.UMin - 15, 0, SectionTextLayer, color: 2));
                    var sslTxt = new MText
                    {
                        Contents = r.SslText, TextHeight = SectionPlanner.TextHeight, Layer = SectionTextLayer, TextStyleId = textStyle,
                        Location = W(tu + 26, 117), Attachment = AttachmentPoint.TopLeft, ColorIndex = 2
                    };
                    Add(sslTxt);
                }

                // Tytuł
                var title = new MText
                {
                    Contents = "{\\H25x;\\L\\C4;SECTION " + letter + "-" + letter + "\\P\\H0.75x;\\l\\C256;SCALE 1:20}",
                    TextHeight = 4, Layer = SectionTitleLayer, TextStyleId = titleStyle,
                    Location = W((r.UMin + r.UMax) / 2, minV - 260), Attachment = AttachmentPoint.MiddleCenter
                };
                Add(title);

                // Znaczniki przekroju na rzutach
                var a = ctx.P2 - ctx.P1;
                if (a.Length > 1e-6)
                {
                    a = a.GetNormal();
                    var nrm = ctx.Input.CutAlongX ? new Vector2d(0, ctx.Input.LookSign) : new Vector2d(ctx.Input.LookSign, 0);
                    foreach (var delta in ctx.Views)
                    {
                        foreach (var (p, dir) in new[] { (ctx.P1 + delta, a), (ctx.P2 + delta, -a) })
                        {
                            var mk = new Polyline { Layer = SectionMarkLayer };
                            var t0 = p + nrm * 294; var t1 = p + dir * 83; var t2 = p + dir * 300;
                            mk.AddVertexAt(0, t1, 0, 0, 0);
                            mk.AddVertexAt(1, t0, 0, 0, 0);
                            mk.AddVertexAt(2, p, 0, 0, 0);
                            mk.AddVertexAt(3, t2, 0, 0, 0);
                            Add(mk);
                            var lp = p + dir * 214 + nrm * 130;
                            Add(new MText
                            {
                                Contents = letter, TextHeight = 150, Layer = SectionMarkLayer, TextStyleId = titleStyle,
                                Location = new Point3d(lp.X, lp.Y, 0), Attachment = AttachmentPoint.MiddleCenter,
                                Rotation = ctx.Input.CutAlongX ? 0 : Math.PI / 2   // litera wzdłuż linii cięcia (pionowe cięcie → obrót 90°)
                            });
                        }
                    }
                }
                tr.Commit();
            }
            return n;
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
