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
    /// Belki / pogrubienia na rysunku (sekcja 4i ARCHITECTURE.md, docs/PLAN_BELKI_2026-10-10.md).
    ///   • Strefa belki = zamknięta polilinia na warstwie <see cref="LayerName"/> z XData <see cref="App"/>:
    ///     [1] FormatVersion (Int16) [2] H [3] szerokość [4] szer. nibu [5] wys. nibu (Real) [6] obrys płyty (1005, XLink)
    ///     [7] Ø strzemion (Int16) [8] rozstaw strzemion (Real) [9] Ø dołem [10] liczba dołem [11] Ø górą [12] liczba górą
    ///     [13] Ø U-barów (Int16) [14] kod strzemienia (tekst) [15] zakład [16] maks. długość (Real)
    ///     [17] góra strzemienia pod: 2 = obiema warstwami T, 1 = jedną (Int16)
    ///     [18] Ø prętów bocznych (Int16, 0 = brak) [19] rozstaw pionowy prętów bocznych (Real) [20] Ø pręta w nibie górą
    ///     [21] Ø pręta w nibie dołem (Int16, 0 = brak) — E4b.
    ///     [22] otulina boczna / czołowa strzemion [23] otulina dolna (Real) — decyzja użytkownika 2026-10-10 (45 / 50); rekord bez
    ///     tych pól (sprzed decyzji) czytany z NOWYMI domyślnymi 45 / 50 (nie dawnymi 40 / 40 — świadomie: belki generowane ponownie
    ///     dostają nowe otuliny).
    ///     Nowe pola tylko na końcu; odczyt toleruje krótsze rekordy (brak pola = wartość domyślna BeamParams).
    ///   • Obiekty wygenerowane dla belki (rozkłady RC_BAR_BLOCK) — XData <see cref="TagApp"/> [1] = handle strefy (1005).
    ///     Ponowne generowanie usuwa tylko obiekty tej strefy; kopia samych prętów (CloneRemap → "") — test punktu w strefie.
    /// Bezstanowe, bez cache (UNDO / REDO).
    /// </summary>
    public static class BeamStore
    {
        public const string LayerName = "RC-BEAM";
        public const string App = "RC_BEAM";
        public const string TagApp = "RC_AUTOREBAR_BEAM";
        public const short FormatVersion = 1;
        public const int SlabIdx = 6;     // RC_BEAM [6] obrys płyty (CloneRemap)
        public const int TagIdx = 1;      // RC_AUTOREBAR_BEAM [1] strefa (CloneRemap)

        public static bool IsBeamLayer(string layer) => string.Equals(layer, LayerName, StringComparison.OrdinalIgnoreCase);

        /// <summary>Strefa belki: zamknięta polilinia z rekordem RC_BEAM.</summary>
        public static bool IsZone(Entity e)
        {
            if (!(e is Polyline)) return false;
            using (var rb = e.GetXDataForApplication(App)) return rb != null;
        }

        // ── zapis / odczyt rekordu strefy ───────────────────────────────

        public static ResultBuffer Build(BeamParams p, string slabHandle)
            => new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, App),
                new TypedValue((int)DxfCode.ExtendedDataInteger16, FormatVersion),          // [1]
                new TypedValue((int)DxfCode.ExtendedDataReal, p.Height),                    // [2]
                new TypedValue((int)DxfCode.ExtendedDataReal, p.Width),                     // [3]
                new TypedValue((int)DxfCode.ExtendedDataReal, p.NibWidth),                  // [4]
                new TypedValue((int)DxfCode.ExtendedDataReal, p.NibHeight),                 // [5]
                XLink.Write(slabHandle),                                                    // [6]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.StirrupDia),    // [7]
                new TypedValue((int)DxfCode.ExtendedDataReal, p.StirrupSpacing),            // [8]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.BottomDia),     // [9]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.BottomCount),   // [10]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.TopDia),        // [11]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.TopCount),      // [12]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.UBarDia),       // [13]
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, p.StirrupShape ?? "51"), // [14]
                new TypedValue((int)DxfCode.ExtendedDataReal, p.Lap),                       // [15]
                new TypedValue((int)DxfCode.ExtendedDataReal, p.MaxLength),                 // [16]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.StirrupTopLayers), // [17]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.SideDia),       // [18] E4b
                new TypedValue((int)DxfCode.ExtendedDataReal, p.SideSpacing),               // [19]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.NibTopDia),     // [20]
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)p.NibBottomDia),  // [21]
                new TypedValue((int)DxfCode.ExtendedDataReal, p.CoverSide),                 // [22] otulina boczna / czołowa
                new TypedValue((int)DxfCode.ExtendedDataReal, p.CoverBottom));              // [23] otulina dolna

        /// <summary>Zapis rekordu strefy (RegApp rejestrowany); encja otwarta do zapisu w transakcji tr.</summary>
        public static void Write(Transaction tr, Database db, Entity zone, BeamParams p, string slabHandle)
        {
            EnsureApp(tr, db, App);
            if (!zone.IsWriteEnabled) zone.UpgradeOpen();
            zone.XData = Build(p, slabHandle);
        }

        /// <summary>Parametry strefy (null gdy brak RC_BEAM); krótsze rekordy — wartości domyślne.</summary>
        public static BeamParams Read(Entity e)
        {
            TypedValue[] v;
            using (var rb = e?.GetXDataForApplication(App)) v = rb?.AsArray();
            if (v == null) return null;
            var p = new BeamParams();
            double D(int i, double def) => v.Length > i ? Num(v[i], def) : def;
            int I(int i, int def) => v.Length > i ? (int)Math.Round(Num(v[i], def)) : def;
            p.Height = D(2, p.Height);
            p.Width = D(3, p.Width);
            p.NibWidth = D(4, p.NibWidth);
            p.NibHeight = D(5, p.NibHeight);
            p.StirrupDia = I(7, p.StirrupDia);
            p.StirrupSpacing = D(8, p.StirrupSpacing);
            p.BottomDia = I(9, p.BottomDia);
            p.BottomCount = I(10, p.BottomCount);
            p.TopDia = I(11, p.TopDia);
            p.TopCount = I(12, p.TopCount);
            p.UBarDia = I(13, p.UBarDia);
            if (v.Length > 14) { string s = Convert.ToString(v[14].Value, CultureInfo.InvariantCulture); if (!string.IsNullOrWhiteSpace(s)) p.StirrupShape = s.Trim(); }
            p.Lap = D(15, p.Lap);
            p.MaxLength = D(16, p.MaxLength);
            p.StirrupTopLayers = I(17, p.StirrupTopLayers);
            p.SideDia = I(18, p.SideDia);
            p.SideSpacing = D(19, p.SideSpacing);
            p.NibTopDia = I(20, p.NibTopDia);
            p.NibBottomDia = I(21, p.NibBottomDia);
            p.CoverSide = D(22, p.CoverSide);       // brak pola (rekord sprzed 2026-10-10) → nowe domyślne 45 / 50
            p.CoverBottom = D(23, p.CoverBottom);
            return p;
        }

        /// <summary>RC_BEAM [6] — handle obrysu płyty („X8” albo "").</summary>
        public static string ReadSlab(Entity e)
        {
            using (var rb = e?.GetXDataForApplication(App))
            {
                var v = rb?.AsArray();
                return v != null && v.Length > SlabIdx ? XLink.Read(v[SlabIdx]) : "";
            }
        }

        private static double Num(TypedValue tv, double def)
        {
            try
            {
                switch (tv.Value)
                {
                    case short s: return s;
                    case int i: return i;
                    case double d: return d;
                    case string str: return NumberParser.TryParseDouble(str, out double x) ? x : def;
                    default: return def;
                }
            }
            catch (System.Exception ex) { Log.Error("BeamStore.Num", ex); return def; }
        }

        // ── znacznik obiektów wygenerowanych ────────────────────────────

        /// <summary>RC_AUTOREBAR_BEAM [1] — handle strefy („X8”), "" (kopia bez strefy) albo null (brak znacznika).</summary>
        public static string ReadTag(Entity e)
        {
            using (var rb = e?.GetXDataForApplication(TagApp))
            {
                var v = rb?.AsArray();
                if (v == null) return null;
                return v.Length > TagIdx ? XLink.Read(v[TagIdx]) : "";
            }
        }

        public static void Tag(Transaction tr, Database db, Entity e, string zoneHandle)
        {
            EnsureApp(tr, db, TagApp);
            if (!e.IsWriteEnabled) e.UpgradeOpen();
            e.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, TagApp),
                XLink.Write(zoneHandle));
        }

        /// <summary>
        /// Rozkłady belki (kod BB / BT / BL) należące do strefy: znacznik = strefa; znacznik pusty / brak — punkt wstawienia
        /// w wielokącie strefy (kopia samych prętów). Zwraca pary (rozkład, opis).
        /// </summary>
        public static List<(ObjectId distId, ObjectId annotId)> FindGenerated(Transaction tr, Database db, string zoneHandle,
                                                                             List<Point2d> zonePoly, Func<string, bool> codeFilter = null)
        {
            var res = new List<(ObjectId, ObjectId)>();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
            {
                if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                var bar = BarBlockEngine.ReadXData(br);
                if (bar == null || !BeamGeometry.IsBeamCode(bar.LayerCode)) continue;
                if (codeFilter != null && !codeFilter(bar.LayerCode)) continue;
                string tag = ReadTag(br);
                bool mine = !string.IsNullOrEmpty(tag)
                    ? XLink.Same(tag, zoneHandle)
                    : zonePoly != null && GeometryHelper.IsPointInsidePolygon(zonePoly, MidOf(br, bar));
                if (!mine) continue;
                res.Add((id, AnnotOf(db, bar)));
            }
            return res;
        }

        /// <summary>Środek rozkładu (do testu przynależności): punkt wstawienia + połowa długości / rozpiętości.</summary>
        internal static Point2d MidOf(BlockReference br, BarData bar)
        {
            bool alongX = !string.Equals(bar.Direction, "Y", StringComparison.OrdinalIgnoreCase);
            double a = bar.LengthA / 2, s = bar.BarsSpan / 2;
            return alongX ? new Point2d(br.Position.X + a, br.Position.Y + s) : new Point2d(br.Position.X + s, br.Position.Y + a);
        }

        private static ObjectId AnnotOf(Database db, BarData bar)
        {
            if (string.IsNullOrEmpty(bar.AnnotHandle)) return ObjectId.Null;
            try
            {
                if (XLink.TryParse(bar.AnnotHandle, out long h) && h != 0 && db.TryGetObjectId(new Handle(h), out ObjectId aid))
                    return aid;
            }
            catch (System.Exception ex) { Log.Error("BeamStore.AnnotOf", ex); }
            return ObjectId.Null;
        }

        /// <summary>
        /// Ponowne RC_PRZYGOTUJ_GA: rozkłady belek wskazujące usuwane strefy (handle) albo bez znacznika w ich wielokącie
        /// — usunięte razem z opisami. Wywoływane PRZED usunięciem stref (wielokąty czytane z nich). Zwraca liczbę rozkładów.
        /// </summary>
        public static int EraseGeneratedForZones(Database db, List<ObjectId> zoneIds)
        {
            if (zoneIds == null || zoneIds.Count == 0) return 0;
            var all = new List<(ObjectId distId, ObjectId annotId)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                foreach (var zid in zoneIds)
                {
                    if (zid.IsErased || !(tr.GetObject(zid, OpenMode.ForRead) is Polyline pl)) continue;
                    var poly = GeometryHelper.GetPolylineVertices(pl);
                    foreach (var pair in FindGenerated(tr, db, pl.Handle.ToString(), poly))
                        if (!all.Any(x => x.distId == pair.distId)) all.Add(pair);
                }
                tr.Commit();
            }
            if (all.Count == 0) return 0;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                foreach (var (d, a) in all)
                {
                    try
                    {
                        if (!d.IsNull && !d.IsErased) tr.GetObject(d, OpenMode.ForWrite).Erase();
                        if (!a.IsNull && !a.IsErased) tr.GetObject(a, OpenMode.ForWrite).Erase();
                    }
                    catch (System.Exception ex) { Log.Error("BeamStore.EraseGeneratedForZones", ex); }
                }
                tr.Commit();
            }
            return all.Count;
        }

        /// <summary>Strefy belek (polilinie RC_BEAM) w przestrzeni modelu.</summary>
        public static List<ObjectId> AllZones(Transaction tr, Database db)
        {
            var res = new List<ObjectId>();
            var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
            foreach (ObjectId id in ms)
                if (!id.IsErased && tr.GetObject(id, OpenMode.ForRead) is Polyline pl && IsZone(pl)) res.Add(id);
            return res;
        }

        /// <summary>
        /// Strefy belek płyty: [6] = obrys (handle) albo (pusty / inny nieistniejący) — środek strefy w obrysie.
        /// </summary>
        public static List<ObjectId> ZonesOfSlab(Transaction tr, Database db, ObjectId slabId, List<Point2d> slabPoly)
        {
            string sh = slabId.Handle.ToString();
            var res = new List<ObjectId>();
            foreach (var zid in AllZones(tr, db))
            {
                var pl = (Polyline)tr.GetObject(zid, OpenMode.ForRead);
                string s = ReadSlab(pl);
                bool mine;
                if (XLink.Same(s, sh)) mine = true;
                else if (!string.IsNullOrEmpty(s) && XLink.TryParse(s, out long h) && db.TryGetObjectId(new Handle(h), out var other)
                         && !other.IsErased) mine = false;   // wskazuje inną żywą płytę
                else mine = slabPoly != null && GeometryHelper.IsPointInsidePolygon(slabPoly, Centroid(pl));
                if (mine) res.Add(zid);
            }
            return res;
        }

        internal static Point2d Centroid(Polyline pl)
        {
            var v = GeometryHelper.GetPolylineVertices(pl);
            // punkt wewnątrz strefy (pas U / L — środek ciężkości bywa poza strefą): środek pierwszego prostokąta analizy
            var r = BeamGeometry.Analyze(v.Select(p => (p.X, p.Y)).ToList(), null);
            var s = r.Segments.FirstOrDefault();
            if (s != null)
            {
                double a = (s.OwnLo + s.OwnHi) / 2;
                return s.AlongX ? new Point2d(a, s.Axis) : new Point2d(s.Axis, a);
            }
            return new Point2d(v.Average(p => p.X), v.Average(p => p.Y));
        }

        // ── warstwa i RegApp ────────────────────────────────────────────

        /// <summary>Warstwa RC-BEAM (kolor i rodzaj linii jak warstwa źródłowa GA, gdy jest; inaczej magenta).</summary>
        public static void EnsureLayer(Transaction tr, Database db, string likeLayer = null)
        {
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(LayerName)) return;
            var rec = new LayerTableRecord { Name = LayerName, Color = Color.FromColorIndex(ColorMethod.ByAci, 6) };
            if (!string.IsNullOrEmpty(likeLayer) && lt.Has(likeLayer))
            {
                var src = (LayerTableRecord)tr.GetObject(lt[likeLayer], OpenMode.ForRead);
                rec.Color = src.Color;
                rec.LinetypeObjectId = src.LinetypeObjectId;
            }
            lt.UpgradeOpen();
            lt.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }

        public static void EnsureApp(Transaction tr, Database db, string app)
        {
            var rat = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (rat.Has(app)) return;
            rat.UpgradeOpen();
            var rec = new RegAppTableRecord { Name = app };
            rat.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
        }
    }
}
