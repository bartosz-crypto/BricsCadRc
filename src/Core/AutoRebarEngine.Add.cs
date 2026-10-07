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
    /// Dozbrojenie dołem z zaimportowanych map zbrojenia (RC_IMPORT_MAP): TYLKO mapy B1 / B2 (T1 / T2 bez zmian).
    /// Strefy map (prostokąt SD-B? ADD + kontur + wartość As) → rozkłady „H10-nn-200 B1 ADD” (plan: AddReinfPlanner),
    /// wymiary położenia względem najbliższego pala (albo krawędzi płyty), opis jak w siatce.
    /// Ponowne wywołanie zastępuje dozbrojenie tej płyty.
    /// </summary>
    public static partial class AutoRebarEngine
    {
        public const string XAddDimApp = "RC_ADD";
        private const string AddDimStyle = "SPEEDECK-1-50 RC";
        private const string AddDimLayer = "SD-PILED-RAFT";

        public sealed class AddResult
        {
            public int Distributions;
            public int Zones;
            public List<string> Messages = new List<string>();   // H12 / H16 i ostrzeżenia (okno na końcu)
        }

        private sealed class MapData
        {
            public string Plot, Code;
            public Extents3d? Slab;
            public List<Extents3d> Rects = new List<Extents3d>();
            public List<Extents3d> Contours = new List<Extents3d>();
            public List<(string text, Point3d pt)> Texts = new List<(string, Point3d)>();
        }

        public static AddResult GenerateAddFromMaps(Document doc, ObjectId slabId)
        {
            var db = doc.Database;
            var ed = doc.Editor;
            var res = new AddResult();
            slabId = NibDetector.OuterOf(db, slabId);

            List<Point2d> verts;
            Extents3d slabBox;
            string slabTag, slabPlot = null;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                if (!(tr.GetObject(slabId, OpenMode.ForRead) is Polyline pl)) return res;
                verts = GeometryHelper.GetPolylineVertices(pl);
                slabBox = GeometryHelper.PolylineBbox(pl);
                slabTag = CurrentSlabTagFor(pl);
                var ga = pl.GetXDataForApplication(GaImportEngine.XApp)?.AsArray();
                if (ga != null && ga.Length > 1) slabPlot = ga[1].Value as string;
                tr.Commit();
            }

            var maps = ReadBottomMaps(db);
            if (maps.Count == 0)
            {
                res.Messages.Add("Brak zaimportowanych map B1 / B2 (RC_IMPORT_MAP).");
                return res;
            }

            var slabPoly = verts.Select(v => (v.X, v.Y)).ToList();
            var piles = FindPilesFor(db, slabId).Select(p => (p.c.X, p.c.Y)).ToList();
            double sw = slabBox.MaxPoint.X - slabBox.MinPoint.X, sh = slabBox.MaxPoint.Y - slabBox.MinPoint.Y;

            var saveHoles = _currentHoles; var saveSlab = _currentSlabHandle;
            try
            {
                using (doc.LockDocument())
                {
                    _currentSlabHandle = slabTag;
                    _currentHoles = FindHoles(db, slabId, verts);
                    _addMode = true;
                    _addSlabPoly = verts;
                    InitLabelOccupancy(db);

                    foreach (var code in new[] { "B1", "B2" })
                    {
                        // Mapa tej płyty: obrys mapy przystający do obrysu płyty (ten sam bbox); przy kilku — ta z PLOT płyty
                        var cands = maps.Where(m => m.Code == code && m.Slab.HasValue
                            && Math.Abs(m.Slab.Value.MaxPoint.X - m.Slab.Value.MinPoint.X - sw) < 10
                            && Math.Abs(m.Slab.Value.MaxPoint.Y - m.Slab.Value.MinPoint.Y - sh) < 10).ToList();
                        var map = cands.FirstOrDefault(m => slabPlot != null && Norm(m.Plot).StartsWith(Norm(slabPlot)))
                                  ?? cands.FirstOrDefault();
                        if (map == null)
                        {
                            if (maps.Any(m => m.Code == code))
                                res.Messages.Add($"Mapa {code}: obrys mapy nie pasuje do wskazanej płyty — pominięta.");
                            continue;
                        }

                        var off = slabBox.MinPoint - map.Slab.Value.MinPoint;
                        bool horizontal = code == "B1";
                        var zones = BuildZones(map, off, horizontal, res, code);
                        EraseOldAdd(db, verts, slabTag, code);
                        if (zones.Count == 0)
                        {
                            ed.WriteMessage($"\n[RC ADD] Mapa {code}: brak stref dozbrojenia.\n");
                            continue;
                        }
                        res.Zones += zones.Count;

                        var plan = AddReinfPlanner.Plan(zones, horizontal, slabPoly, piles);
                        foreach (var d in plan)
                        {
                            if (CreateAddDistribution(db, ed, slabBox, horizontal, code, d, slabPoly, slabTag))
                                res.Distributions++;

                            string zl = string.Join(", ", d.Zones.Select(i => zones[i].Label));
                            string what = $"{code} ADD: {d.Count} H{d.Diameter}-200 L={d.Length:F0} (strefy {zl}, As = {d.Value:F0} mm²/m)";
                            ed.WriteMessage($"\n[RC ADD] {what}");
                            if (d.Diameter > 10)
                                res.Messages.Add($"{what} — wymagane > {(d.Diameter == 12 ? 2 * AddReinfPlanner.BaseAs : AddReinfPlanner.BaseAs + AddReinfPlanner.AsAt200(12)):F0} mm²/m → H{d.Diameter}.");
                            if (d.Value > AddReinfPlanner.BaseAs + AddReinfPlanner.AsAt200(16) + 1e-6)
                                res.Messages.Add($"{code} strefy {zl}: As = {d.Value:F0} mm²/m > H10 + H16@200 ({AddReinfPlanner.BaseAs + AddReinfPlanner.AsAt200(16):F0}) — zaprojektuj ręcznie!");
                            foreach (var w in d.Warnings) res.Messages.Add($"{code} strefy {zl}: {w}.");
                        }
                    }
                }
            }
            finally
            {
                _currentHoles = saveHoles; _currentSlabHandle = saveSlab;
                _addMode = false;
                _addSlabPoly = null;
            }
            int recolored = BarBlockEngine.RecolorAllAdd(db);
            if (recolored > 0) ed.WriteMessage($"\n[RC ADD] Rozkłady ADD w kolorze cyan: {recolored} (także wcześniejsze).");
            ed.WriteMessage("\n");
            return res;
        }

        private static string Norm(string s) => System.Text.RegularExpressions.Regex.Replace((s ?? "").ToUpperInvariant(), @"\s+", " ").Trim();

        // ------------------------------------------------------------------
        //  Odczyt map (XData RC_MAP [PLOT, mapa])
        // ------------------------------------------------------------------

        private static List<MapData> ReadBottomMaps(Database db)
        {
            var maps = new Dictionary<string, MapData>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    var x = e.GetXDataForApplication(MapImportEngine.XApp)?.AsArray();
                    if (x == null || x.Length < 3) continue;
                    string plot = x[1].Value as string ?? "", code = (x[2].Value as string ?? "").ToUpperInvariant();
                    if (code != "B1" && code != "B2") continue;             // T1 / T2 — bez zmian
                    string key = plot + "|" + code;
                    if (!maps.TryGetValue(key, out var m)) maps[key] = m = new MapData { Plot = plot, Code = code };

                    string layer = e.Layer.ToUpperInvariant();
                    if (e is Polyline pl)
                    {
                        var box = GeometryHelper.PolylineBbox(pl);
                        if (layer == $"PH-{code}-SLAB") { if (pl.Closed || GeometryHelper.IsEffectivelyClosed(pl)) m.Slab = box; }
                        else if (layer == $"SD-{code} ADD")
                        {
                            if ((pl.Closed || GeometryHelper.IsEffectivelyClosed(pl)) && pl.NumberOfVertices <= 5 && GeometryHelper.IsAxisAlignedPolyline(pl))
                                m.Rects.Add(box);
                            else m.Contours.Add(box);
                        }
                    }
                    else if (layer == $"SD-{code} ADD")
                    {
                        if (e is DBText t) m.Texts.Add((t.TextString, t.Position));
                        else if (e is MText mt) m.Texts.Add((mt.Text, mt.Location));
                    }
                }
                tr.Commit();
            }
            return maps.Values.ToList();
        }

        private static List<AddReinfPlanner.Zone> BuildZones(MapData map, Vector3d off, bool horizontal, AddResult res, string code)
        {
            var zones = new List<AddReinfPlanner.Zone>();
            int k = 0;
            foreach (var r in map.Rects.OrderByDescending(r => r.MaxPoint.Y).ThenBy(r => r.MinPoint.X))
            {
                k++;
                double x0 = r.MinPoint.X + off.X, x1 = r.MaxPoint.X + off.X, y0 = r.MinPoint.Y + off.Y, y1 = r.MaxPoint.Y + off.Y;
                var z = new AddReinfPlanner.Zone
                {
                    RectA0 = horizontal ? x0 : y0, RectA1 = horizontal ? x1 : y1,
                    RectC0 = horizontal ? y0 : x0, RectC1 = horizontal ? y1 : x1,
                    ContA0 = double.NaN, ContA1 = double.NaN, ContC0 = double.NaN, ContC1 = double.NaN
                };
                foreach (var c in map.Contours.Where(c => c.MinPoint.X >= r.MinPoint.X - 5 && c.MaxPoint.X <= r.MaxPoint.X + 5
                                                       && c.MinPoint.Y >= r.MinPoint.Y - 5 && c.MaxPoint.Y <= r.MaxPoint.Y + 5))
                {
                    double ca0 = (horizontal ? c.MinPoint.X + off.X : c.MinPoint.Y + off.Y);
                    double ca1 = (horizontal ? c.MaxPoint.X + off.X : c.MaxPoint.Y + off.Y);
                    double cc0 = (horizontal ? c.MinPoint.Y + off.Y : c.MinPoint.X + off.X);
                    double cc1 = (horizontal ? c.MaxPoint.Y + off.Y : c.MaxPoint.X + off.X);
                    z.ContA0 = double.IsNaN(z.ContA0) ? ca0 : Math.Min(z.ContA0, ca0);
                    z.ContA1 = double.IsNaN(z.ContA1) ? ca1 : Math.Max(z.ContA1, ca1);
                    z.ContC0 = double.IsNaN(z.ContC0) ? cc0 : Math.Min(z.ContC0, cc0);
                    z.ContC1 = double.IsNaN(z.ContC1) ? cc1 : Math.Max(z.ContC1, cc1);
                }
                var vals = map.Texts.Where(t => t.pt.X >= r.MinPoint.X - 5 && t.pt.X <= r.MaxPoint.X + 5
                                             && t.pt.Y >= r.MinPoint.Y - 5 && t.pt.Y <= r.MaxPoint.Y + 5)
                                    .Select(t => AddReinfPlanner.ParseValue(t.text)).Where(v => !double.IsNaN(v)).ToList();
                if (vals.Count > 0) z.Value = vals.Max();
                else
                {
                    z.Value = 2 * AddReinfPlanner.BaseAs;
                    res.Messages.Add($"Mapa {code}, strefa {k}: brak wartości As w strefie — przyjęto H10.");
                }
                z.Label = $"#{k} ({z.Value.ToString("F0", CultureInfo.InvariantCulture)})";
                zones.Add(z);
            }
            return zones;
        }

        // ------------------------------------------------------------------
        //  Rozkład + wymiary
        // ------------------------------------------------------------------

        private static bool CreateAddDistribution(Database db, Bricscad.EditorInput.Editor ed, Extents3d slabBox, bool horizontal,
                                                  string code, AddReinfPlanner.Dist d, List<(double x, double y)> slabPoly, string slabTag)
        {
            if (d.Count < 1 || d.Length < 1) return false;
            var tpl = FindOrCreateAddTemplate(db, ed, slabBox, d.Diameter, d.Length, code);
            if (tpl.id.IsNull) return false;

            double x0, y0, x1, y1;
            if (horizontal) { x0 = d.A0; x1 = d.A1; y0 = d.C0; y1 = d.C1; }
            else            { x0 = d.C0; x1 = d.C1; y0 = d.A0; y1 = d.A1; }
            double sMin = horizontal ? slabBox.MinPoint.Y : slabBox.MinPoint.X;
            double sMax = horizontal ? slabBox.MaxPoint.Y : slabBox.MaxPoint.X;

            // Leader do NAJBLIŻSZEJ rzeczywistej krawędzi płyty (cięciwa w miejscu linii rozkładu), nie do bboxa —
            // w płycie L opis nie przechodzi przez całą płytę. Ramię 600 / 1500 za krawędzią, potem druga strona.
            Func<Point3d, List<List<Point3d>>> leaders = ins =>
            {
                double al = horizontal ? ins.X : ins.Y;
                var ch = AddReinfPlanner.Chord(slabPoly, !horizontal, al, (d.C0 + d.C1) / 2);
                if (double.IsInfinity(ch.lo) || double.IsInfinity(ch.hi)) ch = (sMin, sMax);
                bool hiFirst = ch.hi - d.C1 <= d.C0 - ch.lo;
                var list = new List<List<Point3d>>();
                foreach (bool hi in hiFirst ? new[] { true, false } : new[] { false, true })
                    foreach (double arm in new[] { 600.0, 1500.0, LeaderArmExtension })
                    {
                        double endC = hi ? ch.hi + arm : ch.lo - arm;
                        var start = horizontal ? new Point3d(al, hi ? d.C0 : d.C0, 0) : new Point3d(d.C0, al, 0);
                        var end = horizontal ? new Point3d(al, endC, 0) : new Point3d(endC, al, 0);
                        list.Add(new List<Point3d> { start, end });
                    }
                return list;
            };

            _lastDistId = ObjectId.Null;
            bool ok = GenerateDistributionWithLeaderAtOffset(
                db, x0, y0, x1, y1, tpl.id, tpl.bar, d.Diameter, d.Length, AddReinfPlanner.Spacing,
                code, horizontal ? "X" : "Y", DefaultCover, AddReinfPlanner.Spacing, SpacingMode.Nominal, sMin, sMax,
                representativeSegment: 0, markSuffix: code + " ADD", leaderWcsFor: leaders);
            if (!ok || _lastDistId.IsNull) return false;

            // Widoczny pręt (reprezentatywny) i linia rozkładu z opisu — punkty wymiarów leżą na nich
            var visibleC = new List<double>();
            double lineA = (d.A0 + d.A1) / 2;
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                if (tr.GetObject(_lastDistId, OpenMode.ForRead) is BlockReference br)
                {
                    var bar = BarBlockEngine.ReadXData(br);
                    if (bar != null)
                    {
                        double c0 = horizontal ? br.Position.Y : br.Position.X;
                        foreach (int i in BarBlockEngine.GetVisibleIndicesPublic(bar.VisibilityMode, bar.VisibleIndices, bar.Count))
                            visibleC.Add(c0 + i * bar.Spacing);
                        if (!string.IsNullOrEmpty(bar.AnnotHandle)
                            && long.TryParse(bar.AnnotHandle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long h)
                            && db.TryGetObjectId(new Handle(h), out ObjectId annId)
                            && tr.GetObject(annId, OpenMode.ForRead) is BlockReference ann)
                            lineA = horizontal ? ann.Position.X : ann.Position.Y;
                    }
                }
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.CreateAddDistribution", ex); }
            if (visibleC.Count == 0) visibleC.Add(d.C0 + Math.Floor((d.Count - 1) / 2.0) * AddReinfPlanner.Spacing);

            AddDimensions(db, horizontal, code, d, slabPoly, slabTag, visibleC, lineA);
            return true;
        }

        /// <summary>Pozycja pręta prostego: istniejąca (także z siatki dołem) o tej średnicy i długości albo nowa w strefie szablonów dołu.</summary>
        private static (ObjectId id, BarData bar) FindOrCreateAddTemplate(Database db, Bricscad.EditorInput.Editor ed,
                                                                          Extents3d slabBox, int dia, double len, string code)
        {
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId oid in ms)
                {
                    if (oid.IsErased || !(tr.GetObject(oid, OpenMode.ForRead) is Polyline pl)) continue;
                    var b = SingleBarEngine.ReadBarXData(pl);
                    if (b == null || b.Diameter != dia || (b.ShapeCode ?? "00") != "00") continue;
                    int nr = SingleBarEngine.ExtractPosNr(b.Mark);
                    if (nr < PositionCounter.FirstAutoNumber || nr >= PositionCounter.TopSeriesStart) continue;
                    if (Math.Abs(b.LengthA - len) < 1.0) { tr.Commit(); return (oid, b); }
                }
                tr.Commit();
            }

            ObjectId rectId; Extents3d zone; int count = 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                (rectId, zone) = FindOrCreateTemplateZone(db, tr, "rebar_bottom", slabBox, ed);
                foreach (int dd in new[] { 6, 8, 10, 12, 16, 20, 25, 32, 40 })
                    count += ScanTemplates(db, tr, zone, dd).Count;
                tr.Commit();
            }
            var created = CreateNewTemplate(db, zone, count, dia, len, code);
            GrowZoneToFit(db, rectId, created.barId, zone);
            ed.WriteMessage($"\n[RC ADD] Nowa pozycja {created.elevBar.Mark} L={len:F0} mm (strefa szablonów dołu).");
            return created;
        }

        /// <summary>
        /// Wymiary dozbrojenia: od środka pala odniesienia do końca prętów (wzdłuż) i do skrajnego pręta (w poprzek);
        /// bez pala w pobliżu — od krawędzi płyty. Styl wymiarów jak na rysunku (SPEEDECK-1-50 RC), warstwa SD-PILED-RAFT.
        /// </summary>
        private static void AddDimensions(Database db, bool horizontal, string code, AddReinfPlanner.Dist d,
                                          List<(double x, double y)> slabPoly, string slabTag,
                                          List<double> visibleC, double lineA)
        {
            Point3d P(double a, double c) => horizontal ? new Point3d(a, c, 0) : new Point3d(c, a, 0);
            double rotAlong = horizontal ? 0.0 : Math.PI / 2, rotAcross = horizontal ? Math.PI / 2 : 0.0;
            double midA = (d.A0 + d.A1) / 2, midC = (d.C0 + d.C1) / 2;
            var dims = new List<(Point3d p1, Point3d p2, Point3d line, double rot)>();

            const double DimOff = 350.0;   // linia wymiarowa od pala / pręta
            if (!double.IsNaN(d.RefA))
            {
                double pa = d.RefA, pc = d.RefC;
                // wzdłuż: środek pala → koniec WIDOCZNEGO pręta (najbliższego palowi)
                double barC = visibleC.OrderBy(c => Math.Abs(c - pc)).First();
                double end = Math.Abs(d.A1 - pa) <= Math.Abs(d.A0 - pa) ? d.A1 : d.A0;
                if (Math.Abs(end - pa) >= 50)
                {
                    double side = barC >= pc ? -1 : 1;                       // linia po stronie pala, z dala od pręta
                    dims.Add((P(pa, pc), P(end, barC), P((pa + end) / 2, pc + side * DimOff), rotAlong));
                }
                // w poprzek: środek pala → koniec LINII ROZKŁADU (skrajny pręt) na linii opisu
                double edge = Math.Abs(d.C0 - pc) <= Math.Abs(d.C1 - pc) ? d.C0 : d.C1;
                if (Math.Abs(edge - pc) >= 50)
                {
                    double la = Math.Abs(lineA - pa) > 2 * DimOff ? (pa + lineA) / 2 : pa + (pa <= lineA ? -DimOff : DimOff);
                    dims.Add((P(pa, pc), P(lineA, edge), P(la, (pc + edge) / 2), rotAcross));
                }
            }
            else
            {
                // krawędź płyty: wzdłuż (na widocznym pręcie) i w poprzek (na linii rozkładu)
                double barC = visibleC[0];
                var ch = AddReinfPlanner.Chord(slabPoly, horizontal, barC, midA);
                bool toLo = d.A0 - ch.lo <= ch.hi - d.A1;
                double eA = toLo ? ch.lo : ch.hi, bA = toLo ? d.A0 : d.A1;
                dims.Add((P(eA, barC), P(bA, barC), P((eA + bA) / 2, barC - DimOff), rotAlong));
                var cc = AddReinfPlanner.Chord(slabPoly, !horizontal, lineA, midC);
                bool toLoC = d.C0 - cc.lo <= cc.hi - d.C1;
                double eC = toLoC ? cc.lo : cc.hi, bC = toLoC ? d.C0 : d.C1;
                dims.Add((P(lineA, eC), P(lineA, bC), P(lineA - DimOff, (eC + bC) / 2), rotAcross));
            }

            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                EnsureRegApp(tr, db, XAddDimApp);
                var dst = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
                ObjectId styleId = dst.Has(AddDimStyle) ? dst[AddDimStyle] : db.Dimstyle;
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                var space = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                foreach (var (p1, p2, line, rot) in dims)
                {
                    var dim = new RotatedDimension(rot, p1, p2, line, "", styleId);
                    dim.SetDatabaseDefaults(db);
                    dim.DimensionStyle = styleId;
                    if (lt.Has(AddDimLayer)) dim.Layer = AddDimLayer;
                    space.AppendEntity(dim);
                    tr.AddNewlyCreatedDBObject(dim, true);
                    dim.XData = new ResultBuffer(
                        new TypedValue((int)DxfCode.ExtendedDataRegAppName, XAddDimApp),
                        new TypedValue((int)DxfCode.ExtendedDataAsciiString, slabTag ?? ""),
                        new TypedValue((int)DxfCode.ExtendedDataAsciiString, code));
                    // kolejne opisy omijają wymiar (linia wymiarowa + tekst, z zapasem)
                    var xs = new[] { p1.X, p2.X, line.X }; var ys = new[] { p1.Y, p2.Y, line.Y };
                    _labelRects.Add((xs.Min() - 150, ys.Min() - 150, xs.Max() + 150, ys.Max() + 150));
                }
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.AddDimensions", ex); }
        }

        private static void EnsureRegApp(Transaction tr, Database db, string name)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(name)) return;
            rat.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = name };
            rat.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        /// <summary>Poprzednie dozbrojenie tej płyty w warstwie <paramref name="code"/>: rozkłady „… B1 ADD” z opisami i wymiary RC_ADD.</summary>
        private static void EraseOldAdd(Database db, List<Point2d> verts, string slabTag, string code)
        {
            List<(ObjectId, ObjectId)> old;
            var dims = new List<ObjectId>();
            using (var tr = db.TransactionManager.StartTransaction())
            {
                old = ScanOldDistributions(db, tr, verts, code, markSuffix: code + " ADD");
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Dimension dim)) continue;
                    var x = dim.GetXDataForApplication(XAddDimApp)?.AsArray();
                    if (x == null || x.Length < 3) continue;
                    if ((x[2].Value as string) != code) continue;
                    string tag = x[1].Value as string;
                    bool mine = !string.IsNullOrEmpty(tag) && slabTag != null
                        ? SameHandle(tag, slabTag)
                        : GeometryHelper.IsPointInsidePolygon(verts, new Point2d(dim.TextPosition.X, dim.TextPosition.Y));
                    if (mine) dims.Add(id);
                }
                tr.Commit();
            }
            EraseOldDistributions(db, old);
            if (dims.Count == 0) return;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var id in dims)
                    try { tr.GetObject(id, OpenMode.ForWrite).Erase(); } catch (System.Exception ex) { Log.Error("AutoRebar.EraseOldAdd", ex); }
                tr.Commit();
            }
        }
    }
}
