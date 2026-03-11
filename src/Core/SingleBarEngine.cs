using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Tworzy i przechowuje pojedynczy pret w widoku elewacji (FLOW 1 — RC_BAR).
    /// XData: RC_SINGLE_BAR na Polyline.
    /// </summary>
    public static class SingleBarEngine
    {
        public const string XAppName = "RC_SINGLE_BAR";

        // ----------------------------------------------------------------
        // Rejestracja AppId
        // ----------------------------------------------------------------

        public static void EnsureAppIdRegistered(Database db)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var regTable = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
            if (!regTable.Has(XAppName))
            {
                regTable.UpgradeOpen();
                var rec = new RegAppTableRecord { Name = XAppName };
                regTable.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }
            tr.Commit();
        }

        // ----------------------------------------------------------------
        // PlaceBar — wstawia polilinie preta + etykiete pozycji w Model Space
        // ----------------------------------------------------------------

        public static void PlaceBar(Database db, BarData bar, Point3d insertPt)
        {
            EnsureAppIdRegistered(db);
            LayerManager.EnsureLayersExist(db);

            var pts = GetShapePoints(bar);
            if (pts.Count < 2) return;

            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            // Polilinia ksztaltu preta (widok elewacji)
            var pline = new Polyline();
            pline.Layer      = LayerManager.GetLayerName(bar.LayerCode);
            pline.LineWeight = DiameterToLineWeight(bar.Diameter);
            for (int i = 0; i < pts.Count; i++)
            {
                var p = pts[i];
                pline.AddVertexAt(i, new Point2d(insertPt.X + p.X, insertPt.Y + p.Y), 0, 0, 0);
            }
            space.AppendEntity(pline);
            tr.AddNewlyCreatedDBObject(pline, true);
            WriteXData(pline, bar);

            // Etykieta pozycji (oznaczenie preta, 50mm ponizej)
            var styleId = GetTextStyleId(db, tr);
            var label = new DBText
            {
                TextString = bar.Mark,
                Position   = new Point3d(insertPt.X, insertPt.Y - 80, 0),
                Height     = 70,
                Layer      = LayerManager.AnnotLayer,
                TextStyleId = styleId
            };
            space.AppendEntity(label);
            tr.AddNewlyCreatedDBObject(label, true);

            tr.Commit();
        }

        // ----------------------------------------------------------------
        // GetShapePoints — zwraca punkty polilinii ksztaltu wg BS8666
        // ----------------------------------------------------------------

        public static List<Point3d> GetShapePoints(BarData bar)
        {
            double a = bar.LengthA;
            double b = bar.LengthB;
            double c = bar.LengthC;

            switch (bar.ShapeCode ?? "00")
            {
                case "00": // Prosty pret
                    return new List<Point3d>
                    {
                        new Point3d(0, 0, 0),
                        new Point3d(a, 0, 0)
                    };

                case "11": // Hak na jednym koncu (prawy gorny)
                    return new List<Point3d>
                    {
                        new Point3d(0, 0, 0),
                        new Point3d(a, 0, 0),
                        new Point3d(a, b, 0)
                    };

                case "12": // Haki na obu koncach
                    return new List<Point3d>
                    {
                        new Point3d(0, b, 0),
                        new Point3d(0, 0, 0),
                        new Point3d(a, 0, 0),
                        new Point3d(a, b, 0)
                    };

                case "13": // 90° na jednym koncu (prawy dolny)
                    return new List<Point3d>
                    {
                        new Point3d(0, 0, 0),
                        new Point3d(a, 0, 0),
                        new Point3d(a, -b, 0)
                    };

                case "21": // L-shape (zagiecie na lewym dolnym koncu)
                    return new List<Point3d>
                    {
                        new Point3d(0, b, 0),
                        new Point3d(0, 0, 0),
                        new Point3d(a, 0, 0)
                    };

                case "25": // U-shape (hairpin): dwie nogi A, pomost B
                    return new List<Point3d>
                    {
                        new Point3d(0, 0, 0),
                        new Point3d(0, -a, 0),
                        new Point3d(b, -a, 0),
                        new Point3d(b, 0, 0)
                    };

                case "32": // Z-shape: A poziome + B gora + C poziome
                    return new List<Point3d>
                    {
                        new Point3d(0, 0, 0),
                        new Point3d(a, 0, 0),
                        new Point3d(a, b, 0),
                        new Point3d(a + c, b, 0)
                    };

                case "51": // Strzup zamkniety
                    b = bar.LengthB > 0 ? bar.LengthB : a;
                    return new List<Point3d>
                    {
                        new Point3d(0, 0, 0),
                        new Point3d(a, 0, 0),
                        new Point3d(a, b, 0),
                        new Point3d(0, b, 0),
                        new Point3d(0, 0, 0)
                    };

                default:
                    return new List<Point3d>
                    {
                        new Point3d(0, 0, 0),
                        new Point3d(a, 0, 0)
                    };
            }
        }

        // ----------------------------------------------------------------
        // XData: [0]AppName [1]Mark [2]Diameter [3]ShapeCode
        //        [4]LengthA [5]LengthB [6]LengthC [7]LayerCode [8]Position
        // ----------------------------------------------------------------

        public static void WriteXData(Entity entity, BarData bar)
        {
            entity.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName,  XAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Mark ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)bar.Diameter),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.ShapeCode ?? "00"),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.LengthA),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.LengthB),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.LengthC),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.LayerCode ?? "B1"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Position  ?? "BOT")
            );
        }

        public static BarData ReadBarXData(Entity entity)
        {
            var xdata = entity.GetXDataForApplication(XAppName);
            if (xdata == null) return null;
            var v = xdata.AsArray();
            if (v.Length < 8) return null;

            var bd = new BarData
            {
                Mark      = (string)v[1].Value,
                Diameter  = (short)v[2].Value,
                ShapeCode = (string)v[3].Value,
                LengthA   = (double)v[4].Value,
                LengthB   = (double)v[5].Value,
                LengthC   = (double)v[6].Value,
                LayerCode = (string)v[7].Value,
                Position  = v.Length >= 9 ? (string)v[8].Value : "BOT"
            };
            bd.Direction = (bd.LayerCode == "B1" || bd.LayerCode == "T1") ? "X" : "Y";
            return bd;
        }

        public static bool IsBar(Entity entity)
            => entity.GetXDataForApplication(XAppName) != null;

        /// <summary>Wyciaga numer pozycji z marka "H12-13" → 13.</summary>
        public static int ExtractPosNr(string mark)
        {
            if (string.IsNullOrEmpty(mark)) return 0;
            var parts = mark.Split('-');
            if (parts.Length >= 2 && int.TryParse(parts[1], out int nr))
                return nr;
            return 0;
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static ObjectId GetTextStyleId(Database db, Transaction tr)
        {
            var styleTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            return styleTable.Has(LayerManager.AnnotTextStyle)
                ? styleTable[LayerManager.AnnotTextStyle]
                : db.Textstyle;
        }

        private static LineWeight DiameterToLineWeight(int diameter) => diameter switch
        {
            <= 10 => LineWeight.LineWeight025,
            <= 16 => LineWeight.LineWeight035,
            <= 20 => LineWeight.LineWeight050,
            _     => LineWeight.LineWeight070
        };
    }
}
