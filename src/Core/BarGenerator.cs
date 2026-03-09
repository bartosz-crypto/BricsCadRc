using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Generuje geometrie ukladu pretow (linie) w rysunku.
    /// </summary>
    public static class BarGenerator
    {
        /// <summary>
        /// Generuje uklad rownolegly pretow w zadanym obszarze.
        /// </summary>
        /// <param name="db">Baza danych rysunku</param>
        /// <param name="bar">Dane preta (srednica, rozstaw, itp.)</param>
        /// <param name="startPoint">Poczatek obszaru ukladu</param>
        /// <param name="endPoint">Koniec obszaru ukladu</param>
        /// <param name="direction">Kierunek pretow: true = poziome (X), false = pionowe (Y)</param>
        /// <returns>Lista ObjectId wygenerowanych linii</returns>
        public static List<ObjectId> GenerateLayout(
            Database db,
            BarData bar,
            Point3d startPoint,
            Point3d endPoint,
            bool horizontal)
        {
            var ids = new List<ObjectId>();

            XDataHelper.EnsureAppIdRegistered(db);
            LayerManager.EnsureLayersExist(db);

            string layerName = LayerManager.GetLayerName(bar.LayerCode);
            double spacingDrawing = bar.Spacing; // rozstaw w jednostkach rysunku (mm)

            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            double x0 = startPoint.X;
            double y0 = startPoint.Y;
            double x1 = endPoint.X;
            double y1 = endPoint.Y;

            if (horizontal)
            {
                // Prety poziome — biegna wzdluz X, rozlozone wzdluz Y
                double totalLength = y1 - y0;
                int count = (int)(totalLength / spacingDrawing) + 1;
                bar.Count = count;

                for (int i = 0; i < count; i++)
                {
                    double y = y0 + i * spacingDrawing;
                    var line = CreateBarLine(
                        tr, space, bar, layerName,
                        new Point3d(x0, y, 0),
                        new Point3d(x1, y, 0),
                        i == 0   // pierwszy pret jest reprezentatywny
                    );
                    ids.Add(line);
                }
            }
            else
            {
                // Prety pionowe — biegna wzdluz Y, rozlozone wzdluz X
                double totalLength = x1 - x0;
                int count = (int)(totalLength / spacingDrawing) + 1;
                bar.Count = count;

                for (int i = 0; i < count; i++)
                {
                    double x = x0 + i * spacingDrawing;
                    var line = CreateBarLine(
                        tr, space, bar, layerName,
                        new Point3d(x, y0, 0),
                        new Point3d(x, y1, 0),
                        i == 0
                    );
                    ids.Add(line);
                }
            }

            tr.Commit();
            return ids;
        }

        private static ObjectId CreateBarLine(
            Transaction tr,
            BlockTableRecord space,
            BarData bar,
            string layerName,
            Point3d start,
            Point3d end,
            bool isRepresentative)
        {
            var line = new Line(start, end)
            {
                Layer = layerName
            };

            // Ustaw grubosc linii odpowiadajaca srednicy preta (wizualnie)
            line.LineWeight = DiameterToLineWeight(bar.Diameter);

            space.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);

            // Zapisz dane preta w XData
            var barData = new BarData
            {
                Mark              = bar.Mark,
                Diameter          = bar.Diameter,
                Spacing           = bar.Spacing,
                Count             = bar.Count,
                ShapeCode         = bar.ShapeCode,
                LengthA           = bar.LengthA,
                LengthB           = bar.LengthB,
                LengthC           = bar.LengthC,
                LengthD           = bar.LengthD,
                LengthE           = bar.LengthE,
                Position          = bar.Position,
                LayerCode         = bar.LayerCode,
                RepresentativeFlag = isRepresentative ? 1 : 0
            };
            XDataHelper.Write(line, barData);

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
