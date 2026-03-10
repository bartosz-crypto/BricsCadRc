using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Tworzy opis tekstowy (annotacje) grupy pretow w rysunku.
    /// Format: "47 H12-01-200 B1" — identyczny z ASD.
    /// </summary>
    public static class AnnotationEngine
    {
        public const string AnnotAppName = "RC_BAR_ANNOT";

        /// <summary>
        /// Standardowa wysokosc tekstu opisu w jednostkach rysunku [mm].
        /// Dla skali 1:50 → 2.5mm na papierze. Moze byc nadpisana przez M21 Settings.
        /// </summary>
        public const double DefaultTextHeight = 200.0;

        /// <summary>
        /// Odleglosc opisu od krawedzi grupy pretow [mm].
        /// </summary>
        public const double AnnotOffset = 300.0;

        // ----------------------------------------------------------------
        // Rejestracja AppId
        // ----------------------------------------------------------------

        public static void EnsureAppIdRegistered(Database db)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var regTable = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);

            if (!regTable.Has(AnnotAppName))
            {
                regTable.UpgradeOpen();
                var rec = new RegAppTableRecord { Name = AnnotAppName };
                regTable.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }

            tr.Commit();
        }

        // ----------------------------------------------------------------
        // Tworzenie annotacji
        // ----------------------------------------------------------------

        /// <summary>
        /// Tworzy opis tekstowy dla wygenerowanej grupy pretow i zwraca jego ObjectId.
        /// </summary>
        /// <param name="db">Baza danych rysunku</param>
        /// <param name="bar">Dane grupy pretow (Mark, Count, Diameter, Spacing, LayerCode)</param>
        /// <param name="result">Wynik generowania (bounding box pretow)</param>
        /// <param name="horizontal">Kierunek pretow (wplywa na pozycje i rotacje opisu)</param>
        public static ObjectId CreateAnnotation(
            Database db,
            BarData bar,
            SlabGenResult result,
            bool horizontal)
        {
            EnsureAppIdRegistered(db);

            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            // Tekst opisu: "47 H12-01-200 B1"
            string text = $"{bar.Count} {bar.Mark} {bar.LayerCode}";

            Point3d pos;
            double rotation;

            if (horizontal)
            {
                // Prety poziome (wzdluz X) — opis z lewej strony, pionowo (czyta sie od dolu do gory)
                pos      = new Point3d(result.MinPoint.X - AnnotOffset, result.MinPoint.Y, 0);
                rotation = Math.PI / 2.0; // 90°
            }
            else
            {
                // Prety pionowe (wzdluz Y) — opis nad grupą, poziomo
                pos      = new Point3d(result.MinPoint.X, result.MaxPoint.Y + AnnotOffset, 0);
                rotation = 0.0;
            }

            var dbText = new DBText
            {
                TextString     = text,
                Layer          = LayerManager.AnnotLayer,
                Height         = DefaultTextHeight,
                Position       = pos,
                Rotation       = rotation,
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode   = TextVerticalMode.TextBase,
                TextStyleId    = GetTextStyleId(db),   // "style1" z romans.shx jak ASD
            };

            space.AppendEntity(dbText);
            tr.AddNewlyCreatedDBObject(dbText, true);

            // Zapisz metadane annotacji w XData
            WriteAnnotXData(dbText, bar);

            tr.Commit();
            return dbText.ObjectId;
        }

        // ----------------------------------------------------------------
        // Pomocnicze
        // ----------------------------------------------------------------

        /// <summary>
        /// Zwraca ObjectId stylu tekstu "style1". Jesli nie istnieje — zwraca Id stylu Standard.
        /// </summary>
        private static ObjectId GetTextStyleId(Database db)
        {
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var styleTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);

            if (styleTable.Has(LayerManager.AnnotTextStyle))
                return styleTable[LayerManager.AnnotTextStyle];

            // Fallback: styl Standard
            return db.Textstyle;
        }

        // ----------------------------------------------------------------
        // XData annotacji
        // ----------------------------------------------------------------

        private static void WriteAnnotXData(DBText text, BarData bar)
        {
            var xdata = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName, AnnotAppName),
                // [1] Mark
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Mark),
                // [2] LayerCode
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.LayerCode),
                // [3] Count
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)bar.Count),
                // [4] Diameter
                new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)bar.Diameter),
                // [5] Spacing
                new TypedValue((int)DxfCode.ExtendedDataReal, bar.Spacing),
                // [6] Direction
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Direction),
                // [7] Position (BOT/TOP)
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Position),
                // [8] LengthA (srednia dlugosc preta)
                new TypedValue((int)DxfCode.ExtendedDataReal, bar.LengthA)
            );

            text.XData = xdata;
        }

        /// <summary>Odczytuje dane z XData annotacji. Zwraca null jesli to nie annotacja RC SLAB.</summary>
        public static BarData ReadAnnotXData(Entity entity)
        {
            var xdata = entity.GetXDataForApplication(AnnotAppName);
            if (xdata == null) return null;

            var vals = xdata.AsArray();
            if (vals.Length < 9) return null;

            return new BarData
            {
                Mark      = (string)vals[1].Value,
                LayerCode = (string)vals[2].Value,
                Count     = (short)vals[3].Value,
                Diameter  = (short)vals[4].Value,
                Spacing   = (double)vals[5].Value,
                Direction = (string)vals[6].Value,
                Position  = (string)vals[7].Value,
                LengthA   = (double)vals[8].Value
            };
        }

        /// <summary>Sprawdza czy entity jest annotacja RC SLAB.</summary>
        public static bool IsAnnotation(Entity entity)
            => entity.GetXDataForApplication(AnnotAppName) != null;
    }
}
