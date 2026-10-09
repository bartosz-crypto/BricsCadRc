using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// B3 ADD — pręty ukośne w narożnikach wklęsłych (kąt wewnętrzny &gt; 180°): 2 H10 co 100, L = 1250,
    /// prostopadle do dwusiecznej narożnika, pierwszy pręt 75 mm od wierzchołka (po dwusiecznej, w płytę).
    /// Plan: narożniki wklęsłe obrysu płyty (RC_GENERATE → B3 / Mesh). Detal otworu: 4 narożniki otworu.
    /// Opis „2 H10-nn-100 B3 ADD”: leader wzdłuż linii rozkładu, załamanie, ramię pionowe (jak przy ukośnych).
    /// </summary>
    public static partial class AutoRebarEngine
    {
        public const string B3AddSuffix = "B3 ADD";
        public const string B3LayerCode = "B3";
        public const int    B3Diameter  = 10;
        public const int    B3Count     = 2;
        public const double B3Length    = 1250.0;
        public const double B3Spacing   = 100.0;
        /// <summary>Pierwszy pręt od wierzchołka narożnika (po dwusiecznej) [mm].</summary>
        public const double B3CornerOffset = 75.0;
        /// <summary>Minimalny „zwrot” narożnika — prawie proste załamania obrysu pomijane [°].</summary>
        public const double B3MinTurnDeg = 20.0;

        /// <summary>
        /// Narożniki wklęsłe obrysu: wierzchołek + dwusieczna skierowana W PŁYTĘ (jednostkowa).
        /// </summary>
        internal static List<(Point2d c, Vector2d b)> ReentrantCorners(List<Point2d> pts)
        {
            var res = new List<(Point2d, Vector2d)>();
            var v = new List<Point2d>();
            foreach (var p in pts)
                if (v.Count == 0 || v[v.Count - 1].GetDistanceTo(p) > 1.0) v.Add(p);
            if (v.Count > 2 && v[0].GetDistanceTo(v[v.Count - 1]) < 1.0) v.RemoveAt(v.Count - 1);
            int n = v.Count;
            if (n < 4) return res;

            double area = 0;
            for (int i = 0; i < n; i++)
            {
                var a = v[i]; var b = v[(i + 1) % n];
                area += a.X * b.Y - b.X * a.Y;
            }
            double sgn = Math.Sign(area);
            if (sgn == 0) return res;

            double minTurn = B3MinTurnDeg * Math.PI / 180.0;
            for (int i = 0; i < n; i++)
            {
                var prev = v[(i - 1 + n) % n]; var cur = v[i]; var next = v[(i + 1) % n];
                var d1 = cur - prev; var d2 = next - cur;
                double cross = d1.X * d2.Y - d1.Y * d2.X;
                if (cross * sgn >= 0) continue;                       // narożnik wypukły / prosty
                double turn = d1.GetAngleTo(d2);
                if (turn < minTurn) continue;                         // prawie prosta linia
                var s = (prev - cur).GetNormal() + (next - cur).GetNormal();
                if (s.Length < 1e-6) continue;
                res.Add((cur, s.GetNormal().Negate()));
            }
            return res;
        }

        private static string B3Label(BarData tpl)
            => $"{B3Count} " + BarData.FormatMark(B3Diameter, SingleBarEngine.ExtractPosNr(tpl?.Mark), B3Spacing, B3Count)
             + " " + B3AddSuffix;

        /// <summary>
        /// RC_GENERATE → B3 (i Mesh): B3 ADD w narożnikach wklęsłych płyty (rzut dolny). Ponowne wywołanie
        /// zastępuje B3 ADD tej płyty. Zwraca liczbę rozkładów.
        /// </summary>
        public static int GenerateB3Corners(Document doc, ObjectId slabId)
        {
            var db = doc.Database;
            var ed = doc.Editor;
            slabId = NibDetector.OuterOf(db, slabId);

            List<Point2d> verts;
            Extents3d slabBox;
            string slabTag;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                if (!(tr.GetObject(slabId, OpenMode.ForRead) is Polyline pl)) return 0;
                verts = GeometryHelper.GetPolylineVertices(pl);
                slabBox = GeometryHelper.PolylineBbox(pl);
                slabTag = CurrentSlabTagFor(pl);
                tr.Commit();
            }

            var corners = ReentrantCorners(verts);
            int made = 0;
            var saveSlab = _currentSlabHandle;
            try
            {
                using (doc.LockDocument())
                {
                    _currentSlabHandle = slabTag;
                    List<(ObjectId, ObjectId)> old;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        old = ScanOldDistributions(db, tr, verts, B3LayerCode, markSuffix: B3AddSuffix);
                        tr.Commit();
                    }
                    EraseOldDistributions(db, old);

                    if (corners.Count == 0)
                    {
                        ed.WriteMessage("\n[RC B3] Brak narożników wklęsłych na obrysie płyty — bez B3 ADD.");
                        return 0;
                    }

                    var tpl = FindOrCreateAddTemplate(db, ed, slabBox, B3Diameter, B3Length, "B1");
                    if (tpl.id.IsNull) return 0;

                    InitLabelOccupancy(db);
                    double minY = slabBox.MinPoint.Y, maxY = slabBox.MaxPoint.Y;
                    double textLen = B3Label(tpl.bar).Length * _charPerHeight * AnnotationEngine.DefaultTextHeight
                                   + 2 * AnnotationEngine.TextArmOffset;
                    foreach (var (c, b) in corners)
                    {
                        // Opis jak w siatce: ramię pionowe do bliższej krawędzi płyty (góra / dół), koniec
                        // LeaderArm za krawędzią (tekst w rzędzie z innymi opisami). Linia rozkładu przedłużona
                        // w stronę opisu: przez wierzchołek (pustka) albo za dalszym prętem (płyta).
                        bool down = c.Y - minY <= maxY - c.Y;
                        double vy = down ? -1.0 : 1.0;
                        double tipY = down ? minY - LeaderArm : maxY + LeaderArm;
                        bool far = b.Y * vy > 1e-6;
                        var dir = far ? b : b.Negate();
                        var start = far ? c + b * (B3CornerOffset + (B3Count - 1) * B3Spacing) : c + b * B3CornerOffset;
                        // załamanie przesuwane po przekątnej, aż ramię i tekst nie kolidują z innymi opisami
                        // (brak wolnego miejsca w zasięgu ~1.5 m — najkrótsze załamanie)
                        Point2d elbow = start + dir * 50.0;
                        if (Math.Abs(dir.X) >= 1e-3)
                            for (int k = 0; k < 40; k++)
                            {
                                var e = start + dir * (50.0 + 50.0 * k);
                                if (B3ArmFree(e.X, e.Y, tipY, textLen)) { elbow = e; break; }
                            }
                        var distId = CreateCornerB3(db, tpl, c, b, new Point3d(elbow.X, tipY, 0));
                        if (distId.IsNull) continue;
                        made++;

                        var u = new Vector2d(b.Y, -b.X);
                        bool outside = false;
                        foreach (double off in new[] { B3CornerOffset, B3CornerOffset + B3Spacing })
                            foreach (double s in new[] { -B3Length / 2, B3Length / 2 })
                                if (!GeometryHelper.IsPointInsidePolygon(verts, c + b * off + u * s)) outside = true;
                        if (outside)
                            ed.WriteMessage($"\n[RC B3] Narożnik ({c.X:F0}, {c.Y:F0}): pręt B3 ADD wychodzi poza płytę — sprawdź.");
                    }
                }
            }
            finally
            {
                _currentSlabHandle = saveSlab;
            }
            ed.WriteMessage($"\n[RC B3] B3 ADD: {made} narożnik(i) wklęsłe ({B3Count} H{B3Diameter}-{B3Spacing:F0} L={B3Length:F0}).");
            return made;
        }

        /// <summary>
        /// Rozkład B3 ADD w narożniku <paramref name="corner"/>: pręty prostopadłe do <paramref name="b"/>
        /// (dwusieczna w płytę), pierwszy B3CornerOffset od wierzchołka, drugi o B3Spacing dalej.
        /// Opis: leader [środek linii rozkładu, koniec ramienia] — załamanie i ramię H/V liczy ApplyElbow.
        /// </summary>
        private static ObjectId CreateCornerB3(Database db, (ObjectId id, BarData bar) tpl,
                                               Point2d corner, Vector2d b, Point3d armTipWcs)
        {
            try
            {
                b = b.GetNormal();
                // Kierunek prętów z kątem w (-90°, 90°] — tekst i symbole czytelne
                var u = new Vector2d(b.Y, -b.X);
                if (u.X < -1e-9 || (Math.Abs(u.X) <= 1e-9 && u.Y < 0)) u = u.Negate();
                double ang = Math.Atan2(u.Y, u.X);
                var ly = new Vector2d(-u.Y, u.X);                  // lokalne +Y (kolejne pręty)
                double firstOff = ly.DotProduct(b) > 0 ? B3CornerOffset : B3CornerOffset + (B3Count - 1) * B3Spacing;
                var p1 = corner + b * firstOff - u * (B3Length / 2);

                int posNr = SingleBarEngine.ExtractPosNr(tpl.bar.Mark);
                if (posNr <= 0) posNr = 1;
                var bar = BuildBarData(B3Diameter, posNr, B3Length, B3LayerCode);
                bar.Mark            = BarData.FormatMark(B3Diameter, posNr, B3Spacing, B3Count) + " " + B3AddSuffix;
                bar.Spacing         = B3Spacing;
                bar.Count           = B3Count;
                bar.Direction       = "X";
                bar.SourceBarHandle = tpl.id.Handle.Value.ToString("X8");
                bar.Angle           = ang;
                bar.Pt1X            = p1.X;
                bar.Pt1Y            = p1.Y;

                var r = BarBlockEngine.GenerateFromBounds(db, 0, 0, B3Length, (B3Count - 1) * B3Spacing, bar, true, posNr);
                if (!r.IsValid) return ObjectId.Null;
                _lastDistId = r.BlockRefId;
                TagWithSlab(db, r.BlockRefId);

                // Opis: wstawienie na pierwszym pręcie w połowie długości (lokalnie x = 0 → linia rozkładu)
                var insW = p1 + u * (B3Length / 2);
                var ins = new Point3d(insW.X, insW.Y, 0);
                double dx = armTipWcs.X - ins.X, dy = armTipWcs.Y - ins.Y;
                double cos = Math.Cos(-ang), sin = Math.Sin(-ang);
                var tipL = new Point3d(dx * cos - dy * sin, dx * sin + dy * cos, 0);
                bar.LeaderPoints = AnnotationEngine.EncodeLeaderPoints(new List<Point3d>
                {
                    new Point3d(0, bar.BarsSpan / 2.0, 0),
                    tipL
                });

                var annot = AnnotationEngine.CreateLeader(db, r, bar, leaderHorizontal: false, posNr: posNr,
                    customInsertPt: ins, barsHorizontal: true, leaderRight: tipL.X >= 0, leaderUp: tipL.Y >= 0);
                if (annot.BlockRefId != ObjectId.Null)
                    BarBlockEngine.LinkAnnotation(db, r.BlockRefId, annot.BlockRefId);
                // wygaszanie jak w siatce: widoczny pręt bliżej wierzchołka
                ApplyVisibleIndex(db, r.BlockRefId, ly.DotProduct(b) > 0 ? 0 : B3Count - 1);
                AddBarLinesOf(db, r.BlockRefId);
                RegisterAnnotOccupancy(db, annot.BlockRefId);
                return r.BlockRefId;
            }
            catch (System.Exception ex)
            {
                Log.Error("AutoRebar.CreateCornerB3", ex);
                return ObjectId.Null;
            }
        }

        /// <summary>Ramię pionowe x (od y0 do yTip) z tekstem przy końcu — bez kolizji z liniami i tekstami opisów.</summary>
        private static bool B3ArmFree(double x, double y0, double yTip, double textLen)
        {
            double lo = Math.Min(y0, yTip), hi = Math.Max(y0, yTip);
            foreach (var (a, b) in _labelSegs)
            {
                if (Math.Abs(a.X - b.X) > 1.0) continue;                      // tylko linie pionowe
                if (Math.Abs(a.X - x) >= DetailLabelSep) continue;
                if (Math.Max(a.Y, b.Y) > lo && Math.Min(a.Y, b.Y) < hi) return false;
            }
            double half = AnnotationEngine.DefaultTextHeight + AnnotationEngine.TextArmOffset;
            var rect = yTip < y0 ? (x - half, yTip, x + half, yTip + textLen)
                                 : (x - half, yTip - textLen, x + half, yTip);
            return !LabelOverlaps(rect);
        }

        /// <summary>Nowy opis jako przeszkoda dla kolejnych (linie + tekst), jak w InitLabelOccupancy.</summary>
        private static void RegisterAnnotOccupancy(Database db, ObjectId annotId)
        {
            if (annotId.IsNull) return;
            try
            {
                using var tr = db.TransactionManager.StartOpenCloseTransaction();
                if (tr.GetObject(annotId, OpenMode.ForRead) is BlockReference br)
                {
                    AddAnnotLines(tr, br);
                    var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    foreach (ObjectId eid in btr)
                    {
                        if (eid.IsErased || !(tr.GetObject(eid, OpenMode.ForRead) is DBText t)) continue;
                        Extents3d ext;
                        try { ext = t.GeometricExtents; } catch { continue; }
                        var a = ext.MinPoint.TransformBy(br.BlockTransform);
                        var b = ext.MaxPoint.TransformBy(br.BlockTransform);
                        _labelRects.Add((Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
                    }
                }
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("AutoRebar.RegisterAnnotOccupancy", ex); }
        }

        /// <summary>
        /// Detal otworu: B3 ADD w 4 narożnikach otworu (dwusieczna 45° na zewnątrz otworu). Opis wzdłuż linii
        /// rozkładu od otworu, ramię pionowe za ramkę detalu (dolne narożniki w dół, górne w górę), w miejscu
        /// wolnym od pozostałych opisów pionowych (≥ DetailLabelSep). Zwraca najdłuższe ramię w dół.
        /// </summary>
        private static double AddHoleB3(Database db, Extents3d zone, ref int tplCount,
                                        double x0, double y0, double x1, double y1,
                                        double fy0, double fy1, IEnumerable<double> usedArmX)
        {
            var tpl = FindOrCreateStraightTemplate(db, zone, ref tplCount, B3Diameter, B3Length, "B1");
            if (tpl.id.IsNull) return 0;
            double arm = DetailArmFor(B3Label(tpl.bar));
            var used = new List<double>(usedArmX ?? Enumerable.Empty<double>());
            // ramię za końcem linii rozkładu (ostatni pręt + min. odcinek załamania), rzut na oś X
            double minDx = (B3CornerOffset + (B3Count - 1) * B3Spacing + 50.0 * DetailScale / 50.0) / Math.Sqrt(2.0);
            double down = 0;
            foreach (var (cx, cy, sx, sy) in new[] { (x0, y0, -1, -1), (x1, y0, 1, -1), (x0, y1, -1, 1), (x1, y1, 1, 1) })
            {
                double ax = cx + sx * minDx;
                for (int k = 0; k < 80 && used.Any(v => Math.Abs(v - ax) < DetailLabelSep); k++) ax += sx * 25.0;
                used.Add(ax);
                double tipY = sy < 0 ? fy0 - arm : fy1 + arm;
                var id = CreateCornerB3(db, tpl, new Point2d(cx, cy), new Vector2d(sx, sy).GetNormal(), new Point3d(ax, tipY, 0));
                if (!id.IsNull && sy < 0) down = arm;
            }
            return down;
        }
    }
}
