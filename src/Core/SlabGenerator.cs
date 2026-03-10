using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Generuje uklad pretow w obszarze wyznaczonym przez zamknieta polilinie (obrys plyty).
    /// Preta sa przycinane do ksztaltu polilinii i przesuniate o otupline od krawedzi.
    /// </summary>
    public static class SlabGenerator
    {
        // Domyslne otulin wg Speedeck (mm)
        public const double CoverBot = 40.0;
        public const double CoverTop = 35.0;

        /// <summary>
        /// Generuje uklad pretow z polilinii obrysu plyty.
        /// </summary>
        /// <param name="db">Baza danych rysunku</param>
        /// <param name="plineId">ObjectId zamknietej polilinii — obrys plyty</param>
        /// <param name="bar">Dane preta</param>
        /// <param name="horizontal">true = prety poziome (wzdluz X), false = pionowe (wzdluz Y)</param>
        /// <param name="cover">Otulina od krawedzi polilinii [mm / jednostki rysunku]</param>
        /// <returns>Lista ObjectId wygenerowanych linii</returns>
        public static List<ObjectId> GenerateFromPolyline(
            Database db,
            ObjectId plineId,
            BarData bar,
            bool horizontal,
            double cover)
        {
            var ids = new List<ObjectId>();

            XDataHelper.EnsureAppIdRegistered(db);
            LayerManager.EnsureLayersExist(db);

            string layerName = LayerManager.GetLayerName(bar.LayerCode);

            // Wczytaj wierzcholki polilinii w osobnej transakcji
            List<Point2d> vertices;
            using (var readTr = db.TransactionManager.StartTransaction())
            {
                var pline = (Polyline)readTr.GetObject(plineId, OpenMode.ForRead);
                vertices = GetPolylineVertices(pline);
                readTr.Commit();
            }

            if (vertices.Count < 3) return ids;

            // Oblicz bounding box
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var v in vertices)
            {
                if (v.X < minX) minX = v.X;
                if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y;
                if (v.Y > maxY) maxY = v.Y;
            }

            // Zakres z otuling
            double x0 = minX + cover;
            double y0 = minY + cover;
            double x1 = maxX - cover;
            double y1 = maxY - cover;

            if (x0 >= x1 || y0 >= y1)
                return ids; // Polilinia zbyt mala na zadana otuline

            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            int barIndex = 0;

            if (horizontal)
            {
                // Prety poziome — biega wzdluz X, rozmieszczone wzdluz Y
                double totalSpan = y1 - y0;
                int count = (int)(totalSpan / bar.Spacing) + 1;

                for (int i = 0; i < count; i++)
                {
                    double y = y0 + i * bar.Spacing;
                    if (y > y1) break;

                    var segments = ClipHorizontalLine(vertices, y, x0, x1);
                    foreach (var (segX0, segX1) in segments)
                    {
                        barIndex++;
                        double lengthA = Math.Abs(segX1 - segX0);
                        var barData = CloneWithIndex(bar, barIndex, "X", lengthA);

                        var line = CreateBarLine(tr, space, barData, layerName,
                            new Point3d(segX0, y, 0),
                            new Point3d(segX1, y, 0));
                        ids.Add(line);
                    }
                }
            }
            else
            {
                // Prety pionowe — biega wzdluz Y, rozmieszczone wzdluz X
                double totalSpan = x1 - x0;
                int count = (int)(totalSpan / bar.Spacing) + 1;

                for (int i = 0; i < count; i++)
                {
                    double x = x0 + i * bar.Spacing;
                    if (x > x1) break;

                    var segments = ClipVerticalLine(vertices, x, y0, y1);
                    foreach (var (segY0, segY1) in segments)
                    {
                        barIndex++;
                        double lengthA = Math.Abs(segY1 - segY0);
                        var barData = CloneWithIndex(bar, barIndex, "Y", lengthA);

                        var line = CreateBarLine(tr, space, barData, layerName,
                            new Point3d(x, segY0, 0),
                            new Point3d(x, segY1, 0));
                        ids.Add(line);
                    }
                }
            }

            bar.Count = barIndex;
            tr.Commit();
            return ids;
        }

        // ----------------------------------------------------------------
        // Przycinanie linii do wielokata (ray casting / scanline)
        // ----------------------------------------------------------------

        /// <summary>
        /// Zwraca odcinki poziomej linii y=coord przyciete do wielokata.
        /// Wyniki to pary (xStart, xEnd) w granicach [xMin, xMax].
        /// </summary>
        private static List<(double, double)> ClipHorizontalLine(
            List<Point2d> vertices, double y, double xMin, double xMax)
        {
            var xs = FindIntersectionsH(vertices, y);
            return PairSegments(xs, xMin, xMax);
        }

        /// <summary>
        /// Zwraca odcinki pionowej linii x=coord przyciete do wielokata.
        /// Wyniki to pary (yStart, yEnd) w granicach [yMin, yMax].
        /// </summary>
        private static List<(double, double)> ClipVerticalLine(
            List<Point2d> vertices, double x, double yMin, double yMax)
        {
            var ys = FindIntersectionsV(vertices, x);
            return PairSegments(ys, yMin, yMax);
        }

        private static List<double> FindIntersectionsH(List<Point2d> vertices, double y)
        {
            var result = new List<double>();
            int n = vertices.Count;

            for (int i = 0; i < n; i++)
            {
                var p1 = vertices[i];
                var p2 = vertices[(i + 1) % n];

                // Pomijaj krawedzie rownolegle do linii skanowania
                if (Math.Abs(p1.Y - p2.Y) < 1e-9) continue;

                // Sprawdz czy linia y przechodzi przez krawedz
                // (wylaczamy dokladnie gorny punkt zeby uniknac podwojnego liczenia)
                if ((p1.Y <= y && p2.Y > y) || (p2.Y <= y && p1.Y > y))
                {
                    double t = (y - p1.Y) / (p2.Y - p1.Y);
                    double x = p1.X + t * (p2.X - p1.X);
                    result.Add(x);
                }
            }

            result.Sort();
            return result;
        }

        private static List<double> FindIntersectionsV(List<Point2d> vertices, double x)
        {
            var result = new List<double>();
            int n = vertices.Count;

            for (int i = 0; i < n; i++)
            {
                var p1 = vertices[i];
                var p2 = vertices[(i + 1) % n];

                if (Math.Abs(p1.X - p2.X) < 1e-9) continue;

                if ((p1.X <= x && p2.X > x) || (p2.X <= x && p1.X > x))
                {
                    double t = (x - p1.X) / (p2.X - p1.X);
                    double y = p1.Y + t * (p2.Y - p1.Y);
                    result.Add(y);
                }
            }

            result.Sort();
            return result;
        }

        /// <summary>Laczy posortowane przeciecia w pary i przycina do zakresu.</summary>
        private static List<(double, double)> PairSegments(List<double> coords, double min, double max)
        {
            var segments = new List<(double, double)>();

            for (int i = 0; i + 1 < coords.Count; i += 2)
            {
                double a = coords[i];
                double b = coords[i + 1];

                // Przytnij do zakresu z otuling
                double clampA = Math.Max(a, min);
                double clampB = Math.Min(b, max);

                if (clampA < clampB - 1e-6)
                    segments.Add((clampA, clampB));
            }

            return segments;
        }

        // ----------------------------------------------------------------
        // Pomocnicze
        // ----------------------------------------------------------------

        private static List<Point2d> GetPolylineVertices(Polyline pline)
        {
            var pts = new List<Point2d>();
            for (int i = 0; i < pline.NumberOfVertices; i++)
                pts.Add(pline.GetPoint2dAt(i));
            return pts;
        }

        private static BarData CloneWithIndex(BarData src, int index, string direction, double lengthA)
        {
            return new BarData
            {
                Mark               = src.Mark,
                Diameter           = src.Diameter,
                Spacing            = src.Spacing,
                Count              = src.Count,
                ShapeCode          = src.ShapeCode,
                LengthA            = lengthA,
                LengthB            = src.LengthB,
                LengthC            = src.LengthC,
                LengthD            = src.LengthD,
                LengthE            = src.LengthE,
                Position           = src.Position,
                LayerCode          = src.LayerCode,
                RepresentativeFlag = 0,
                BarIndex           = index,
                Direction          = direction,
                IsLap              = 0
            };
        }

        private static ObjectId CreateBarLine(
            Transaction tr,
            BlockTableRecord space,
            BarData bar,
            string layerName,
            Point3d start,
            Point3d end)
        {
            var line = new Line(start, end)
            {
                Layer      = layerName,
                LineWeight = DiameterToLineWeight(bar.Diameter)
            };

            space.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
            XDataHelper.Write(line, bar);

            return line.ObjectId;
        }

        private static LineWeight DiameterToLineWeight(int diameter)
        {
            return diameter switch
            {
                <= 10 => LineWeight.LineWeight025,
                <= 16 => LineWeight.LineWeight035,
                <= 20 => LineWeight.LineWeight050,
                _     => LineWeight.LineWeight070
            };
        }
    }
}
