using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// RC_SET_REPR_BAR (wstążka „Single Bar View”) — wygaszanie prętów na płycie jak przy RC_GENERATE (decyzja użytkownika
    /// 2026-10-16; odwrotność RC_SHOW_ALL_BARS): w każdym rozkładzie płyty widoczny jeden pręt — środkowy, a kolejne odcinki
    /// w pasie przesunięte o rozstaw (zakłady czytelne); rozkład przecięty otworem — pręt ucięty + najbliższy cały;
    /// UB ADD o nieparzystej liczbie — pręt obok środka (nie pod odnośnikiem). Potem <see cref="ResolveRepresentativeCollisions"/>.
    /// Pręty belki (BB / BT / BL …) — bez zmian, jak w Mesh.
    /// </summary>
    public static partial class AutoRebarEngine
    {
        public sealed class ReprResult { public int Dists, Hidden, Moved; }

        public static ReprResult ApplyRepresentativeOnSlab(Document doc, ObjectId slabId)
        {
            var db = doc.Database;
            var res = new ReprResult();
            var dists = new List<(ObjectId id, BarData bar, Point3d pos)>();
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                if (!(tr.GetObject(slabId, OpenMode.ForRead) is Polyline slabPl)) return res;
                var verts = GeometryHelper.GetPolylineVertices(slabPl);
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId oid in ms)
                {
                    if (oid.IsErased || !(tr.GetObject(oid, OpenMode.ForRead) is BlockReference br)) continue;
                    var bar = BarBlockEngine.ReadXData(br);
                    if (bar == null || Math.Abs(br.Rotation) > 1e-6 || bar.Count <= 1 || bar.Spacing <= 0) continue;
                    if (BeamGeometry.IsBeamCode(bar.LayerCode)) continue;
                    if (!GeometryHelper.IsPointInsidePolygon(verts, new Point2d(br.Position.X, br.Position.Y))) continue;
                    dists.Add((oid, bar, br.Position));
                }
                tr.Commit();
            }
            res.Dists = dists.Count;

            // Pasy: rozkłady tego samego kierunku nachodzące na siebie w osi rozkładu; w pasie kolejność wzdłuż prętów
            double ScanLo((ObjectId id, BarData bar, Point3d pos) d) => d.bar.Direction == "X" ? d.pos.Y : d.pos.X;
            double ScanHi((ObjectId id, BarData bar, Point3d pos) d) => ScanLo(d) + Math.Max(d.bar.BarsSpan, (d.bar.Count - 1) * d.bar.Spacing);
            double Along((ObjectId id, BarData bar, Point3d pos) d) => d.bar.Direction == "X" ? d.pos.X : d.pos.Y;
            var segIdx = new Dictionary<ObjectId, int>();
            foreach (var grp in dists.GroupBy(d => d.bar.Direction))
            {
                var bands = new List<List<(ObjectId id, BarData bar, Point3d pos)>>();
                foreach (var d in grp.OrderBy(ScanLo))
                {
                    var band = bands.FirstOrDefault(b => b.Any(o => Math.Min(ScanHi(o), ScanHi(d)) - Math.Max(ScanLo(o), ScanLo(d)) > 1.0));
                    if (band == null) bands.Add(band = new List<(ObjectId, BarData, Point3d)>());
                    band.Add(d);
                }
                foreach (var b in bands)
                {
                    int k = 0;
                    foreach (var d in b.OrderBy(Along)) segIdx[d.id] = k++;
                }
            }

            foreach (var (id, bar, _) in dists)
            {
                int cutIdx = CutBandIndex(bar);
                string want;
                if (cutIdx >= 0)
                {
                    int fullIdx = NearestUncutIndex(bar, cutIdx);
                    want = fullIdx >= 0 ? $"{cutIdx},{fullIdx}" : cutIdx.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    bool ubAdd = (bar.Mark ?? "").EndsWith(" " + UbAddSuffix, StringComparison.OrdinalIgnoreCase);
                    int s = ubAdd ? (bar.Count % 2 == 1 ? -1 : 0) : segIdx.TryGetValue(id, out int v) ? v : 0;
                    int idx = ((bar.Count / 2 + s) % bar.Count + bar.Count) % bar.Count;
                    want = idx.ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
                if (bar.VisibilityMode == BarVisibilityMode.Manual && (bar.VisibleIndices ?? "") == want) continue;
                ApplyVisibleIndices(db, id, want);
                res.Hidden++;
            }
            res.Moved = ResolveRepresentativeCollisions(doc, slabId);
            return res;
        }
    }
}
