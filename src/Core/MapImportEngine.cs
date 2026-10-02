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
    /// RC_IMPORT_MAP — mapy zbrojenia dodatkowego (…_punching_reinf_maps.dxf), jak ASD-IMR w AsdRcSlab:
    ///   • plik map: kolumny płyt (nagłówek PH-SLAB-HEADER „PLOT …”), w kolumnie ramki PH-FRAME po jednej
    ///     na mapę; rodzaj mapy (PH/T1/T2/B1/B2) wg obrysu płyty PH-xx-SLAB w ramce,
    ///   • użytkownik wybiera płytę i wskazuje punkt wstawienia (podgląd ramek w jigu) — punkt = lewy górny róg
    ///     ramki T1; kopiowane są ramki T1, T2, B1, B2 z całą zawartością (bez nagłówka płyty i mapy PH),
    ///   • XData RC_MAP [PLOT, mapa] — ponowny import tej samej płyty zastępuje poprzedni.
    /// </summary>
    public static class MapImportEngine
    {
        public const string XApp = "RC_MAP";
        private static readonly string[] Rows = { "T1", "T2", "B1", "B2", "PH" };

        public sealed class MapFrame
        {
            public string Row;              // T1/T2/B1/B2/PH
            public Extents3d Frame;
            public Extents3d Slab;          // obrys płyty na mapie
        }

        public sealed class MapPlot
        {
            public string Label;            // "PLOT 6-8"
            public double HeaderX;
            public List<MapFrame> Maps = new List<MapFrame>();
            public override string ToString()
                => $"{Label}   —   mapy: {string.Join(", ", Maps.Select(m => m.Row))}";
        }

        public sealed class Source : IDisposable
        {
            public Database Db;
            public List<MapPlot> Plots = new List<MapPlot>();
            public void Dispose() { Db?.Dispose(); Db = null; }
        }

        // ----------------------------------------------------------------
        // Odczyt pliku map
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

                var headers = new List<(string label, double x)>();
                var frames = new List<Extents3d>();
                var slabs = new List<(string row, Extents3d ext)>();
                using (var tr = src.Db.TransactionManager.StartOpenCloseTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(src.Db), OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                        string layer = e.Layer ?? "";
                        if (layer.Equals("PH-SLAB-HEADER", StringComparison.OrdinalIgnoreCase) && e is MText mt)
                        {
                            string label = FirstLine(mt.Text);
                            if (label.Length > 0) headers.Add((label, mt.Location.X));
                        }
                        else if (layer.Equals("PH-FRAME", StringComparison.OrdinalIgnoreCase) && e is Polyline fp)
                            frames.Add(Ext(fp));
                        else if (e is Polyline sp)
                        {
                            var m = Regex.Match(layer, @"^PH-(T1|T2|B1|B2|PH)-SLAB$", RegexOptions.IgnoreCase);
                            if (m.Success) slabs.Add((m.Groups[1].Value.ToUpperInvariant(), Ext(sp)));
                        }
                    }
                    tr.Commit();
                }

                foreach (var (label, x) in headers.OrderBy(h => h.x))
                {
                    var plot = new MapPlot { Label = label, HeaderX = x };
                    foreach (var f in frames.Where(f => x >= f.MinPoint.X - 1 && x <= f.MaxPoint.X + 1))
                    {
                        var sl = slabs.Where(s => Inside(f, s.ext)).ToList();
                        if (sl.Count == 0) continue;
                        if (plot.Maps.Any(m => m.Row == sl[0].row))
                        {
                            warnings.Add($"{label}: mapa {sl[0].row} występuje dwa razy — pominięto drugą.");
                            continue;
                        }
                        plot.Maps.Add(new MapFrame { Row = sl[0].row, Frame = f, Slab = sl[0].ext });
                    }
                    plot.Maps = plot.Maps.OrderBy(m => Array.IndexOf(Rows, m.Row)).ToList();
                    if (plot.Maps.Count > 0) src.Plots.Add(plot);
                    else warnings.Add($"{label}: brak ramek map (PH-FRAME z obrysem PH-xx-SLAB).");
                }
                if (src.Plots.Count == 0)
                    throw new InvalidOperationException("W pliku nie znaleziono map (nagłówki PH-SLAB-HEADER, ramki PH-FRAME).");
                return src;
            }
            catch
            {
                src.Dispose();
                throw;
            }
        }

        // ----------------------------------------------------------------
        // Rysunek docelowy: obrysy płyty górnej / dolnej
        // ----------------------------------------------------------------

        /// <summary>Indeks płyty, której etykieta („PLOT 6-8”) jest tekstem na rysunku (-1 = brak).</summary>
        public static int GuessPlot(Database db, List<MapPlot> plots)
        {
            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    var o = tr.GetObject(id, OpenMode.ForRead);
                    if (o is MText mt) labels.Add(Norm(FirstLine(mt.Text)));
                    else if (o is DBText t) labels.Add(Norm(t.TextString));
                }
                tr.Commit();
            }
            for (int i = 0; i < plots.Count; i++)
                if (labels.Contains(Norm(plots[i].Label))) return i;
            return -1;
        }

        // ----------------------------------------------------------------
        // Import
        // ----------------------------------------------------------------

        /// <summary>Mapy wklejane razem (jak ASD-IMR): T1, T2, B1, B2 — mapa PH pominięta (tagi robi RC_PUNCHING_AUTO).</summary>
        public static List<MapFrame> MapsToPaste(MapPlot plot)
            => plot.Maps.Where(m => m.Row != "PH").ToList();

        /// <summary>Punkt odniesienia: lewy górny róg ramki T1 (gdy brak — pierwszej wklejanej ramki).</summary>
        public static Point3d ReferencePoint(MapPlot plot)
        {
            var maps = MapsToPaste(plot);
            var f = (maps.FirstOrDefault(m => m.Row == "T1") ?? maps.First()).Frame;
            return new Point3d(f.MinPoint.X, f.MaxPoint.Y, 0);
        }

        /// <summary>
        /// Kopiuje ramki map płyty (z całą zawartością) do modelu, przesunięte tak, że punkt odniesienia
        /// (lewy górny róg T1) trafia w <paramref name="insertPt"/>. Zwraca liczbę obiektów per mapa.
        /// </summary>
        public static Dictionary<string, int> Import(Document doc, Source src, MapPlot plot, Point3d insertPt,
                                                     List<string> warnings)
        {
            var db = doc.Database;
            var result = new Dictionary<string, int>();
            var maps = MapsToPaste(plot);
            var move = Matrix3d.Displacement(insertPt - ReferencePoint(plot));
            using (doc.LockDocument())
            {
                EraseOld(db, plot.Label, new HashSet<string>(StringComparer.OrdinalIgnoreCase));

                // Obiekty, których środek zakresu leży w ramce mapy (jak ASD-IMR), bez nagłówka płyty
                var perMap = maps.ToDictionary(m => m.Row, m => new List<ObjectId>());
                var ids = new ObjectIdCollection();
                using (var tr = src.Db.TransactionManager.StartOpenCloseTransaction())
                {
                    var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(src.Db), OpenMode.ForRead);
                    foreach (ObjectId id in ms)
                    {
                        if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                        if ((e.Layer ?? "").Equals("PH-SLAB-HEADER", StringComparison.OrdinalIgnoreCase)) continue;
                        Extents3d x;
                        try { x = e.GeometricExtents; }
                        catch (System.Exception ex) { Log.Error("MapImport.Extents", ex); continue; }
                        var c = new Point3d((x.MinPoint.X + x.MaxPoint.X) / 2, (x.MinPoint.Y + x.MaxPoint.Y) / 2, 0);
                        var m = maps.FirstOrDefault(mm => InsideXY(mm.Frame, c, 0));
                        if (m == null) continue;
                        perMap[m.Row].Add(id);
                        ids.Add(id);
                    }
                    tr.Commit();
                }
                if (ids.Count == 0) { warnings.Add($"{plot.Label}: ramki map są puste."); return result; }

                var rowOf = new Dictionary<ObjectId, string>();
                foreach (var kv in perMap) foreach (var id in kv.Value) rowOf[id] = kv.Key;

                var idMap = new IdMapping();
                src.Db.WblockCloneObjects(ids, SymbolUtilityServices.GetBlockModelSpaceId(db), idMap,
                                          DuplicateRecordCloning.Ignore, false);
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    EnsureApp(tr, db);
                    var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
                    foreach (IdPair pair in idMap)
                    {
                        if (!pair.IsCloned || !pair.IsPrimary || !rowOf.TryGetValue(pair.Key, out string row)) continue;
                        if (!(tr.GetObject(pair.Value, OpenMode.ForWrite, false, true) is Entity e)) continue;
                        if (e.OwnerId != msId) continue;
                        e.TransformBy(move);
                        e.XData = new ResultBuffer(
                            new TypedValue((int)DxfCode.ExtendedDataRegAppName, XApp),
                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, plot.Label),
                            new TypedValue((int)DxfCode.ExtendedDataAsciiString, row));
                        result[row] = (result.TryGetValue(row, out int n) ? n : 0) + 1;
                    }
                    tr.Commit();
                }
            }
            return result;
        }

        private static void EraseOld(Database db, string plot, HashSet<string> layersOut)
        {
            var old = new List<(ObjectId id, string layer)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                    var rb = e.GetXDataForApplication(XApp);
                    if (rb == null) continue;
                    var v = rb.AsArray();
                    rb.Dispose();
                    if (v.Length > 1 && string.Equals(v[1].Value as string, plot, StringComparison.OrdinalIgnoreCase))
                        old.Add((id, e.Layer));
                }
                tr.Commit();
            }
            if (old.Count == 0) return;
            var layers = new HashSet<string>(old.Select(o => o.layer), StringComparer.OrdinalIgnoreCase);
            SetLocked(db, layers, false);
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var (id, _) in old)
                    if (tr.GetObject(id, OpenMode.ForWrite) is Entity e) e.Erase();
                tr.Commit();
            }
            foreach (var l in layers) layersOut.Add(l);
        }

        private static void SetLocked(Database db, IEnumerable<string> layers, bool locked)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            foreach (var name in layers)
            {
                if (string.IsNullOrEmpty(name) || !lt.Has(name)) continue;
                var rec = (LayerTableRecord)tr.GetObject(lt[name], OpenMode.ForWrite);
                if (rec.Name == "0") continue;
                rec.IsLocked = locked;
            }
            tr.Commit();
        }

        private static void EnsureApp(Transaction tr, Database db)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(XApp)) return;
            rat.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = XApp };
            rat.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        // ----------------------------------------------------------------

        private static string FirstLine(string s)
            => (s ?? "").Replace("\r", "\n").Split('\n').Select(x => x.Trim()).FirstOrDefault(x => x.Length > 0) ?? "";

        private static string Norm(string s) => Regex.Replace((s ?? "").Trim(), @"\s+", " ");

        private static Extents3d Ext(Polyline pl)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            for (int i = 0; i < pl.NumberOfVertices; i++)
            {
                var p = pl.GetPoint2dAt(i);
                x0 = Math.Min(x0, p.X); y0 = Math.Min(y0, p.Y); x1 = Math.Max(x1, p.X); y1 = Math.Max(y1, p.Y);
            }
            return new Extents3d(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0));
        }

        private static bool Inside(Extents3d outer, Extents3d inner)
            => inner.MinPoint.X >= outer.MinPoint.X - 1 && inner.MaxPoint.X <= outer.MaxPoint.X + 1
            && inner.MinPoint.Y >= outer.MinPoint.Y - 1 && inner.MaxPoint.Y <= outer.MaxPoint.Y + 1;

        private static bool InsideXY(Extents3d e, Point3d p, double tol)
            => p.X >= e.MinPoint.X - tol && p.X <= e.MaxPoint.X + tol && p.Y >= e.MinPoint.Y - tol && p.Y <= e.MaxPoint.Y + tol;
    }
}
