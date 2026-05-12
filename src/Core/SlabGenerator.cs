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
        /// <returns>Wynik generowania z lista ID i bounding boxem</returns>
        public static SlabGenResult GenerateFromPolyline(
            Database db,
            ObjectId plineId,
            BarData bar,
            bool horizontal,
            double cover)
        {
            var result = new SlabGenResult();

            XDataHelper.EnsureAppIdRegistered(db);
            LayerManager.EnsureLayersExist(db);

            string layerName = LayerManager.GetLayerName(bar.LayerCode);

            // Wczytaj wierzcholki polilinii w osobnej transakcji
            List<Point2d> vertices;
            using (var readTr = db.TransactionManager.StartTransaction())
            {
                var pline = (Polyline)readTr.GetObject(plineId, OpenMode.ForRead);
                vertices = GeometryHelper.GetPolylineVertices(pline);
                readTr.Commit();
            }

            if (vertices.Count < 3) return result;

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
                return result; // Polilinia zbyt mala na zadana otuline

            // Bounding box wygenerowanych pretow
            double barMinX = double.MaxValue, barMinY = double.MaxValue;
            double barMaxX = double.MinValue, barMaxY = double.MinValue;

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

                    var segments = GeometryHelper.ClipHorizontalLine(vertices, y, x0, x1);
                    foreach (var (segX0, segX1) in segments)
                    {
                        barIndex++;
                        double lengthA = Math.Abs(segX1 - segX0);
                        var barData = CloneWithIndex(bar, barIndex, "X", lengthA);

                        var line = CreateBarLine(tr, space, barData, layerName,
                            new Point3d(segX0, y, 0),
                            new Point3d(segX1, y, 0));
                        result.BarIds.Add(line);
                        result.LeaderTickPoints.Add(new Point3d(Math.Min(segX0, segX1), y, 0));

                        if (segX0 < barMinX) barMinX = segX0;
                        if (segX1 > barMaxX) barMaxX = segX1;
                        if (y < barMinY) barMinY = y;
                        if (y > barMaxY) barMaxY = y;
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

                    var segments = GeometryHelper.ClipVerticalLine(vertices, x, y0, y1);
                    foreach (var (segY0, segY1) in segments)
                    {
                        barIndex++;
                        double lengthA = Math.Abs(segY1 - segY0);
                        var barData = CloneWithIndex(bar, barIndex, "Y", lengthA);

                        var line = CreateBarLine(tr, space, barData, layerName,
                            new Point3d(x, segY0, 0),
                            new Point3d(x, segY1, 0));
                        result.BarIds.Add(line);
                        result.LeaderTickPoints.Add(new Point3d(x, Math.Max(segY0, segY1), 0));

                        if (x < barMinX) barMinX = x;
                        if (x > barMaxX) barMaxX = x;
                        if (segY0 < barMinY) barMinY = segY0;
                        if (segY1 > barMaxY) barMaxY = segY1;
                    }
                }
            }

            bar.Count = barIndex;
            tr.Commit();

            if (result.BarIds.Count > 0)
            {
                result.MinPoint = new Point3d(barMinX, barMinY, 0);
                result.MaxPoint = new Point3d(barMaxX, barMaxY, 0);
            }

            return result;
        }

        // ----------------------------------------------------------------
        // Pomocnicze
        // ----------------------------------------------------------------

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
