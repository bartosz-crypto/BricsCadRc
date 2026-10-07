using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// RC_PRZYGOTUJ_GA — przygotowanie rysunku RC (plik „default”) z rysunku GA:
    ///   • płyty w pliku GA: opis „PLOT …” (SD-Text) wewnątrz obrysu SD-PILED-RAFT,
    ///   • kopiowane tylko: obrys + linia uskoku (SD-PILED-RAFT, także door threshold i linie stopni),
    ///     pale (koła SD-Pile), teksty SD-Text poza „NIB TOC=…” (opisy belek itp. zostają),
    ///     podpisy pali (SD-Pile Text*) — TYLKO na rzucie górnym,
    ///   • ramka opisu płyty przebudowana: zostaje nr plotu, SSL i grubość płyty,
    ///   • dwa rzuty (dolny i górny) obok siebie; tytuły rzutów (S02_TEXT035) pod płytą, na środku, ~2400 mm
    ///     od rysunku płyty; ramki szablonów rebar_bottom / rebar_top po bokach (z zawartością),
    ///   • otwarte kawałki linii (np. uskok narysowany dwiema polilinami) łączone w zamkniętą polilinię,
    ///   • XData RC_GA [PLOT, B|T] — ponowne wywołanie zastępuje poprzednio wstawioną płytę.
    /// Wymiary, architektura, poziomy, kanalizacja itp. nie są kopiowane.
    /// </summary>
    public static class GaImportEngine
    {
        public const string XApp = "RC_GA";
        public const string SlabLayer = "SD-PILED-RAFT";
        public const double RegionMargin = 1500.0;   // wokół obrysu: door threshold, opisy przy krawędzi
        public const double TitleGap     = 2400.0;   // od rysunku płyty do tytułu rzutu
        public const double PlanGap      = 7500.0;   // między rzutami i między rzutem a ramką szablonów
        public const double LabelMargin  = 70.0;     // ramka opisu płyty wokół tekstu
        private const double FrameWidth  = 8065.0;   // ramka szablonów (gdy brak w rysunku)
        private const double FrameHeight = 20288.0;

        public sealed class GaPlot
        {
            public string Label;                  // „PLOT 9-10”
            public ObjectId LabelId;              // MText/DBText opisu płyty
            public ObjectId LabelBoxId;           // ramka opisu (SD-Text), może być Null
            public ObjectId OutlineId;            // obrys zewnętrzny
            public Extents3d Outline;
            public Extents3d Region;              // obrys + margines
            public override string ToString()
                => $"{Label}   —   {Outline.MaxPoint.X - Outline.MinPoint.X:F0} × {Outline.MaxPoint.Y - Outline.MinPoint.Y:F0} mm";
        }

        public sealed class Source : IDisposable
        {
            public Database Db;
            public List<GaPlot> Plots = new List<GaPlot>();
            public void Dispose() { Db?.Dispose(); Db = null; }
        }

        // ----------------------------------------------------------------
        // Odczyt GA
        // ----------------------------------------------------------------

        public static Source ReadSource(string path, List<string> warnings)
        {
            var src = new Source { Db = new Database(false, true) };
            try
            {
                if (Path.GetExtension(path).Equals(".dwg", StringComparison.OrdinalIgnoreCase))
                    src.Db.ReadDwgFile(path, FileShare.Read, true, null);
                else
                    src.Db.DxfIn(path, null);

                var outlines = new List<(ObjectId id, List<Point2d> v, Extents3d ext, double area)>();
                var labels = new List<(ObjectId id, string label, Point3d pt)>();
                var boxes = new List<(ObjectId id, Extents3d ext)>();
                using (var tr = src.Db.TransactionManager.StartOpenCloseTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(src.Db), OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                        string layer = e.Layer ?? "";
                        if (layer.Equals(SlabLayer, StringComparison.OrdinalIgnoreCase) && e is Polyline pl
                            && pl.NumberOfVertices >= 3 && GeometryHelper.IsEffectivelyClosed(pl))
                        {
                            var v = GeometryHelper.GetPolylineVertices(pl);
                            outlines.Add((id, v, Ext(v), Math.Abs(Area(v))));
                        }
                        else if (layer.Equals("SD-Text", StringComparison.OrdinalIgnoreCase))
                        {
                            if (e is MText mt && IsPlotLabel(FirstLine(mt)))
                                labels.Add((id, FirstLine(mt), mt.Location));
                            else if (e is DBText dt && IsPlotLabel(dt.TextString))
                                labels.Add((id, dt.TextString.Trim(), dt.Position));
                            else if (e is Polyline bp && bp.NumberOfVertices >= 4 && GeometryHelper.IsEffectivelyClosed(bp))
                                boxes.Add((id, Ext(GeometryHelper.GetPolylineVertices(bp))));
                        }
                    }
                    tr.Commit();
                }

                foreach (var (id, label, pt) in labels)
                {
                    var p2 = new Point2d(pt.X, pt.Y);
                    // największy obrys zawierający opis = obrys zewnętrzny (linia uskoku też go zawiera)
                    var hit = outlines.Where(o => GeometryHelper.IsPointInsidePolygon(o.v, p2))
                                      .OrderByDescending(o => o.area).FirstOrDefault();
                    if (hit.v == null)
                    {
                        warnings.Add($"{label}: opis poza obrysem {SlabLayer} — pominięto.");
                        continue;
                    }
                    if (src.Plots.Any(pp => string.Equals(pp.Label, label, StringComparison.OrdinalIgnoreCase)))
                    {
                        warnings.Add($"{label}: opis występuje więcej niż raz — użyto pierwszego.");
                        continue;
                    }
                    var box = boxes.Where(b => b.ext.MinPoint.X <= pt.X + 200 && b.ext.MaxPoint.X >= pt.X - 200
                                            && b.ext.MinPoint.Y <= pt.Y + 200 && b.ext.MaxPoint.Y >= pt.Y - 200)
                                   .OrderBy(b => (b.ext.MaxPoint.X - b.ext.MinPoint.X) * (b.ext.MaxPoint.Y - b.ext.MinPoint.Y))
                                   .FirstOrDefault();
                    var o = hit.ext;
                    src.Plots.Add(new GaPlot
                    {
                        Label = label, LabelId = id, LabelBoxId = box.id, OutlineId = hit.id, Outline = o,
                        Region = new Extents3d(new Point3d(o.MinPoint.X - RegionMargin, o.MinPoint.Y - RegionMargin, 0),
                                               new Point3d(o.MaxPoint.X + RegionMargin, o.MaxPoint.Y + RegionMargin, 0))
                    });
                }
                if (src.Plots.Count == 0)
                    throw new InvalidOperationException(
                        $"W pliku nie znaleziono płyt (opis „PLOT …” na SD-Text wewnątrz zamkniętego obrysu {SlabLayer}).");
                src.Plots = src.Plots.OrderBy(p => p.Label, StringComparer.OrdinalIgnoreCase).ToList();
                return src;
            }
            catch
            {
                src.Dispose();
                throw;
            }
        }

        // ----------------------------------------------------------------
        // Import
        // ----------------------------------------------------------------

        public sealed class Result
        {
            public int Bottom, Top, Joined;
        }

        public static Result Import(Document doc, Source src, GaPlot plot, List<string> warnings)
        {
            var db = doc.Database;
            var res = new Result();

            // 1. Co kopiujemy (źródło) + zakres rysunku płyty
            var common = new List<ObjectId>();
            var pileLabels = new List<ObjectId>();
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            using (var tr = src.Db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(src.Db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    string layer = e.Layer ?? "";
                    Extents3d x;
                    try { x = e.GeometricExtents; }
                    catch { continue; }
                    var c = new Point3d((x.MinPoint.X + x.MaxPoint.X) / 2, (x.MinPoint.Y + x.MaxPoint.Y) / 2, 0);
                    if (!InsideXY(plot.Region, c)) continue;

                    int kind = Classify(e, layer, id, plot);
                    if (kind == 0) continue;
                    if (kind == 2) { pileLabels.Add(id); continue; }
                    common.Add(id);
                    x0 = Math.Min(x0, x.MinPoint.X); y0 = Math.Min(y0, x.MinPoint.Y);
                    x1 = Math.Max(x1, x.MaxPoint.X); y1 = Math.Max(y1, x.MaxPoint.Y);
                }
                tr.Commit();
            }
            if (common.Count == 0) throw new InvalidOperationException($"{plot.Label}: nic do skopiowania.");
            double w = x1 - x0;

            using (doc.LockDocument())
            {
                EraseOld(db);

                // 2. Tytuły i ramki szablonów w rysunku docelowym
                var (titleB, titleT, frameB, frameT) = FindTemplate(db);
                Extents3d? tbExt = ExtOf(db, titleB), ttExt = ExtOf(db, titleT);
                if (!tbExt.HasValue) warnings.Add("Brak tytułu rzutu dolnego (…BOTTOM LAYER) — rzut wstawiony w punkcie (0,0).");
                if (!ttExt.HasValue) warnings.Add("Brak tytułu rzutu górnego (…TOP LAYER) — bez tytułu.");

                // Rzut dolny: środek nad tytułem, dół rysunku płyty TitleGap nad tytułem
                double cxB, botB;
                if (tbExt.HasValue)
                {
                    cxB = (tbExt.Value.MinPoint.X + tbExt.Value.MaxPoint.X) / 2;
                    botB = tbExt.Value.MaxPoint.Y + TitleGap;
                }
                else { cxB = w / 2; botB = 0; }
                var dB = new Vector3d(cxB - (x0 + x1) / 2, botB - y0, 0);
                var dT = dB + new Vector3d(w + PlanGap, 0, 0);

                // 3. Kopie
                res.Bottom = CloneInto(db, src, common, dB, plot, "B", warnings, ref res.Joined);
                var topIds = new List<ObjectId>(common); topIds.AddRange(pileLabels);
                res.Top = CloneInto(db, src, topIds, dT, plot, "T", warnings, ref res.Joined);

                // 4. Tytuł górny pod rzutem górnym (ten sam poziom co dolny), ramki szablonów po bokach
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    double planB0 = x0 + dB.X, planT1 = x1 + dT.X;
                    if (ttExt.HasValue && tbExt.HasValue && tr.GetObject(titleT, OpenMode.ForWrite) is Entity tt)
                    {
                        double cxT = (x0 + x1) / 2 + dT.X;
                        tt.TransformBy(Matrix3d.Displacement(new Vector3d(
                            cxT - (ttExt.Value.MinPoint.X + ttExt.Value.MaxPoint.X) / 2,
                            tbExt.Value.MaxPoint.Y - ttExt.Value.MaxPoint.Y, 0)));
                    }
                    double planTop = y1 + dB.Y;
                    PlaceFrame(tr, db, frameB, "rebar_bottom", warnings, top: planTop, rightEdge: planB0 - PlanGap);
                    PlaceFrame(tr, db, frameT, "rebar_top", warnings, top: planTop, leftEdge: planT1 + PlanGap);
                    tr.Commit();
                }
            }
            return res;
        }

        /// <summary>0 = pomiń, 1 = oba rzuty, 2 = tylko rzut górny (podpisy pali).</summary>
        private static int Classify(Entity e, string layer, ObjectId id, GaPlot plot)
        {
            if (layer.Equals(SlabLayer, StringComparison.OrdinalIgnoreCase))
            {
                if (e is MText m && m.TextHeight >= 1000) return 0;   // notatki arkusza
                return e is Polyline || e is Line || e is Arc || e is MText || e is DBText ? 1 : 0;
            }
            if (layer.Equals("SD-Pile", StringComparison.OrdinalIgnoreCase))
                return e is Circle ? 1 : 0;
            if (layer.StartsWith("SD-Pile Text", StringComparison.OrdinalIgnoreCase))
                return e is DBText || e is MText ? 2 : 0;
            if (layer.Equals("SD-Text", StringComparison.OrdinalIgnoreCase))
            {
                if (id == plot.LabelId || id == plot.LabelBoxId) return 1;   // przebudowywane po skopiowaniu
                string t = e is MText mt ? string.Join(" ", TextLines(mt)) : e is DBText dt ? dt.TextString : null;
                if (t != null && Regex.IsMatch(t, @"NIB\s*TOC", RegexOptions.IgnoreCase)) return 0;
                return e is MText || e is DBText || e is Polyline || e is Line ? 1 : 0;
            }
            return 0;
        }

        private static int CloneInto(Database db, Source src, List<ObjectId> ids, Vector3d d, GaPlot plot,
                                     string view, List<string> warnings, ref int joined)
        {
            var col = new ObjectIdCollection(ids.ToArray());
            var idMap = new IdMapping();
            var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
            src.Db.WblockCloneObjects(col, msId, idMap, DuplicateRecordCloning.Ignore, false);

            int n = 0;
            ObjectId label = ObjectId.Null, box = ObjectId.Null;
            var openPl = new List<ObjectId>();
            var textClones = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                EnsureApp(tr, db);
                var move = Matrix3d.Displacement(d);
                foreach (IdPair pair in idMap)
                {
                    if (!pair.IsCloned || !pair.IsPrimary || !ids.Contains(pair.Key)) continue;
                    if (!(tr.GetObject(pair.Value, OpenMode.ForWrite, false, true) is Entity e)) continue;
                    if (e.OwnerId != msId) continue;
                    e.TransformBy(move);
                    Tag(e, plot.Label, view);
                    n++;
                    if (pair.Key == plot.LabelId) label = e.ObjectId;
                    else if (e is MText && (e.Layer ?? "").Equals("SD-Text", StringComparison.OrdinalIgnoreCase))
                        textClones.Add(e.ObjectId);
                    else if (pair.Key == plot.LabelBoxId) box = e.ObjectId;
                    else if (e is Polyline pl && (e.Layer ?? "").Equals(SlabLayer, StringComparison.OrdinalIgnoreCase)
                             && !GeometryHelper.IsEffectivelyClosed(pl))
                        openPl.Add(e.ObjectId);
                }

                // Opis płyty: tylko nr plotu, SSL i grubość; ramka dopasowana do tekstu
                if (!label.IsNull) label = RebuildLabel(tr, msId, label, box, textClones, plot.Label, view, warnings);

                // Otwarte kawałki obrysu/uskoku tworzące pętlę → jedna zamknięta polilinia (NibDetector)
                joined += JoinLoops(tr, msId, openPl, plot.Label, view);
                tr.Commit();
            }
            return n;
        }

        /// <summary>
        /// Opis płyty od nowa: czysty MText (te same warstwa, styl, wysokość, kolor, punkt i wyrównanie) z liniami
        /// nr plotu / SSL / grubość. Kopie tego samego opisu (duplikaty w GA) usuwane. Ramka dopasowana do tekstu.
        /// </summary>
        private static ObjectId RebuildLabel(Transaction tr, ObjectId msId, ObjectId labelId, ObjectId boxId,
            List<ObjectId> textClones, string plot, string view, List<string> warnings)
        {
            var ent = (Entity)tr.GetObject(labelId, OpenMode.ForWrite);
            List<string> lines = ent is MText m0 ? TextLines(m0)
                               : ent is DBText d0 ? new List<string> { d0.TextString.Trim() } : new List<string>();
            if (lines.Count == 0) return labelId;
            var keep = new List<string> { lines[0] };
            var ssl = lines.FirstOrDefault(l => l.StartsWith("SSL", StringComparison.OrdinalIgnoreCase));
            if (ssl != null) keep.Add(ssl);
            else warnings.Add($"{lines[0]}: w opisie brak SSL.");
            var thk = lines.FirstOrDefault(l => Regex.IsMatch(l, @"\bTHK\b", RegexOptions.IgnoreCase));
            if (thk != null) keep.Add(Regex.Replace(thk, @"\s+ON\s*$", "", RegexOptions.IgnoreCase).Trim());
            else warnings.Add($"{lines[0]}: w opisie brak grubości płyty (… THK SLAB).");

            // Duplikaty opisu (ta sama treść) — usuń
            string sig = string.Join("|", lines);
            foreach (var id in textClones)
                if (id != labelId && tr.GetObject(id, OpenMode.ForWrite) is MText dup && string.Join("|", TextLines(dup)) == sig)
                    dup.Erase();

            Entity fresh;
            if (ent is MText src)
            {
                var mt = new MText
                {
                    Layer = src.Layer, Color = src.Color, TextStyleId = src.TextStyleId, TextHeight = src.TextHeight,
                    Location = src.Location, Attachment = src.Attachment, Rotation = src.Rotation, Width = 0,
                    Contents = string.Join("\\P", keep)
                };
                var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);
                ms.AppendEntity(mt);
                tr.AddNewlyCreatedDBObject(mt, true);
                Tag(mt, plot, view);
                src.Erase();
                fresh = mt;
            }
            else
            {
                ((DBText)ent).TextString = string.Join(" ", keep);
                fresh = ent;
            }

            if (boxId.IsNull || !(tr.GetObject(boxId, OpenMode.ForWrite) is Polyline bx)) return fresh.ObjectId;
            Extents3d te;
            try { te = fresh.GeometricExtents; }
            catch (System.Exception ex) { Log.Error("GaImport.RebuildLabel", ex); return fresh.ObjectId; }
            double a = te.MinPoint.X - LabelMargin, b = te.MinPoint.Y - LabelMargin;
            double c = te.MaxPoint.X + LabelMargin, d = te.MaxPoint.Y + LabelMargin;
            while (bx.NumberOfVertices > 4) bx.RemoveVertexAt(bx.NumberOfVertices - 1);
            while (bx.NumberOfVertices < 4) bx.AddVertexAt(bx.NumberOfVertices, new Point2d(0, 0), 0, 0, 0);
            var pts = new[] { new Point2d(a, b), new Point2d(c, b), new Point2d(c, d), new Point2d(a, d) };
            for (int i = 0; i < 4; i++) { bx.SetPointAt(i, pts[i]); bx.SetBulgeAt(i, 0); }
            bx.Closed = true;
            return fresh.ObjectId;
        }

        /// <summary>
        /// Linie tekstu MText z surowej treści (Contents): \P = nowa linia, kody formatowania usunięte.
        /// (MText.Text w BricsCAD skleja linie bez separatora — „PLOT 30FFL=…”.)
        /// </summary>
        internal static List<string> TextLines(MText mt)
        {
            string c = mt.Contents ?? "";
            c = Regex.Replace(c, @"\\[ACcFfHhQqTtWwp][^;\\]*;", "");   // kody formatowania (\pxqc; to akapit, nie nowa linia)
            c = Regex.Replace(c, @"\\P", "\n");
            c = Regex.Replace(c, @"\\[LlOoKk]", "");
            c = c.Replace("\\~", " ").Replace("{", "").Replace("}", "");
            return c.Split('\n').Select(l => Regex.Replace(l, @"\s+", " ").Trim()).Where(l => l.Length > 0).ToList();
        }

        /// <summary>Łączy otwarte polilinie (bez łuków), których końce się stykają, w zamknięte pętle.</summary>
        private static int JoinLoops(Transaction tr, ObjectId msId, List<ObjectId> open, string plot, string view)
        {
            const double tol = 1.0;
            var parts = new List<(ObjectId id, List<Point2d> v)>();
            foreach (var id in open)
            {
                var pl = (Polyline)tr.GetObject(id, OpenMode.ForRead);
                bool arcs = false;
                for (int i = 0; i < pl.NumberOfVertices; i++) if (Math.Abs(pl.GetBulgeAt(i)) > 1e-9) arcs = true;
                if (!arcs) parts.Add((id, GeometryHelper.GetPolylineVertices(pl)));
            }
            int made = 0;
            var used = new HashSet<ObjectId>();
            foreach (var start in parts)
            {
                if (used.Contains(start.id)) continue;
                var chain = new List<Point2d>(start.v);
                var members = new List<ObjectId> { start.id };
                bool progress = true;
                while (progress && chain[0].GetDistanceTo(chain[chain.Count - 1]) > tol)
                {
                    progress = false;
                    foreach (var p in parts)
                    {
                        if (used.Contains(p.id) || members.Contains(p.id)) continue;
                        var end = chain[chain.Count - 1];
                        if (p.v[0].GetDistanceTo(end) <= tol) chain.AddRange(p.v.Skip(1));
                        else if (p.v[p.v.Count - 1].GetDistanceTo(end) <= tol) chain.AddRange(Enumerable.Reverse(p.v).Skip(1));
                        else continue;
                        members.Add(p.id);
                        progress = true;
                        break;
                    }
                }
                if (members.Count < 2 || chain[0].GetDistanceTo(chain[chain.Count - 1]) > tol) continue;
                chain.RemoveAt(chain.Count - 1);

                var first = (Polyline)tr.GetObject(members[0], OpenMode.ForRead);
                var pl = new Polyline { Layer = first.Layer, Color = first.Color, LinetypeId = first.LinetypeId,
                                        LineWeight = first.LineWeight };
                for (int i = 0; i < chain.Count; i++) pl.AddVertexAt(i, chain[i], 0, 0, 0);
                pl.Closed = true;
                var ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForWrite);
                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                Tag(pl, plot, view);
                foreach (var id in members) { ((Entity)tr.GetObject(id, OpenMode.ForWrite)).Erase(); used.Add(id); }
                made++;
            }
            return made;
        }

        // ----------------------------------------------------------------
        // Szablon rysunku RC: tytuły rzutów i ramki szablonów
        // ----------------------------------------------------------------

        private static (ObjectId titleB, ObjectId titleT, ObjectId frameB, ObjectId frameT) FindTemplate(Database db)
        {
            ObjectId tb = ObjectId.Null, tt = ObjectId.Null, fb = ObjectId.Null, ft = ObjectId.Null;
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased) continue;
                var o = tr.GetObject(id, OpenMode.ForRead);
                if (o is MText mt)
                {
                    string t = (mt.Text ?? "").ToUpperInvariant();
                    if (!t.Contains("REINFORCEMENT DETAILS")) continue;
                    if (t.Contains("BOTTOM LAYER") && tb.IsNull) tb = id;
                    else if (t.Contains("TOP LAYER") && tt.IsNull) tt = id;
                }
                else if (o is Polyline pl)
                {
                    if (pl.Layer.Equals("rebar_bottom", StringComparison.OrdinalIgnoreCase) && fb.IsNull) fb = id;
                    else if (pl.Layer.Equals("rebar_top", StringComparison.OrdinalIgnoreCase) && ft.IsNull) ft = id;
                }
            }
            tr.Commit();
            return (tb, tt, fb, ft);
        }

        /// <summary>Ramka szablonów (z zawartością — pręty-szablony i ich opisy) przesunięta obok rzutu; brak → nowa.</summary>
        private static void PlaceFrame(Transaction tr, Database db, ObjectId frameId, string layer, List<string> warnings,
                                       double top, double rightEdge = double.NaN, double leftEdge = double.NaN)
        {
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
            if (frameId.IsNull)
            {
                EnsureLayer(tr, db, layer);
                double l = double.IsNaN(leftEdge) ? rightEdge - FrameWidth : leftEdge;
                var pl = new Polyline { Layer = layer };
                pl.AddVertexAt(0, new Point2d(l, top), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(l + FrameWidth, top), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(l + FrameWidth, top - FrameHeight), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(l, top - FrameHeight), 0, 0, 0);
                pl.Closed = true;
                ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                warnings.Add($"Brak ramki {layer} — utworzono nową.");
                return;
            }
            var frame = (Polyline)tr.GetObject(frameId, OpenMode.ForRead);
            var fe = Ext(GeometryHelper.GetPolylineVertices(frame));
            double dx = double.IsNaN(leftEdge) ? rightEdge - fe.MaxPoint.X : leftEdge - fe.MinPoint.X;
            double dy = top - fe.MaxPoint.Y;
            if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6) return;
            var move = Matrix3d.Displacement(new Vector3d(dx, dy, 0));
            foreach (ObjectId id in ms)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                if (id != frameId)
                {
                    Extents3d x;
                    try { x = e.GeometricExtents; } catch { continue; }
                    var c = new Point3d((x.MinPoint.X + x.MaxPoint.X) / 2, (x.MinPoint.Y + x.MaxPoint.Y) / 2, 0);
                    if (!InsideXY(fe, c)) continue;
                    if (e.GetXDataForApplication(XApp) != null) continue;
                }
                e.UpgradeOpen();
                e.TransformBy(move);
            }
        }

        // ----------------------------------------------------------------

        private static void EraseOld(Database db)
        {
            var old = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    using var rb = e.GetXDataForApplication(XApp);
                    if (rb != null) old.Add(id);
                }
                tr.Commit();
            }
            if (old.Count == 0) return;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in old)
                    if (tr.GetObject(id, OpenMode.ForWrite) is Entity e) e.Erase();
                tr.Commit();
            }
        }

        private static void Tag(Entity e, string plot, string view)
            => e.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, XApp),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, plot),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, view));

        private static void EnsureApp(Transaction tr, Database db)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(XApp)) return;
            rat.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = XApp };
            rat.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        private static void EnsureLayer(Transaction tr, Database db, string name)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(name)) return;
            lt.UpgradeOpen();
            var rec = new LayerTableRecord { Name = name };
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        private static Extents3d? ExtOf(Database db, ObjectId id)
        {
            if (id.IsNull) return null;
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                var e = (Entity)tr.GetObject(id, OpenMode.ForRead);
                var x = e.GeometricExtents;
                tr.Commit();
                return x;
            }
            catch (System.Exception ex) { Log.Error("GaImport.ExtOf", ex); return null; }
        }

        private static bool IsPlotLabel(string s) => Regex.IsMatch((s ?? "").Trim(), @"^PLOTS?\s+\S", RegexOptions.IgnoreCase);

        private static string FirstLine(MText mt) => TextLines(mt).FirstOrDefault() ?? "";

        private static Extents3d Ext(List<Point2d> v)
            => new Extents3d(new Point3d(v.Min(p => p.X), v.Min(p => p.Y), 0), new Point3d(v.Max(p => p.X), v.Max(p => p.Y), 0));

        private static double Area(List<Point2d> v)
        {
            double a = 0;
            for (int i = 0; i < v.Count; i++) { var p = v[i]; var q = v[(i + 1) % v.Count]; a += p.X * q.Y - q.X * p.Y; }
            return a / 2;
        }

        private static bool InsideXY(Extents3d e, Point3d p)
            => p.X >= e.MinPoint.X && p.X <= e.MaxPoint.X && p.Y >= e.MinPoint.Y && p.Y <= e.MaxPoint.Y;
    }
}
