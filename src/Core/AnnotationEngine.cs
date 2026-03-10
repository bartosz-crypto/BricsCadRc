using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Tworzy annotacje grupy pretow — styl ASD (RBCR_ENDE_BARDDESC / RBCR_EN_CONSTLINEMODULE).
    ///
    /// Architektura:
    ///   RC_BAR_nnn       = tylko prety (linie) — nieruchome
    ///   RC_BAR_nnn_ANNOT = JEDEN BlockReference RC_ANNOT_nnn
    ///                      zawierajacy: dist line + doty + ramie + tekst
    ///                      Calosc sie przesuwa razem jak w ASD.
    ///
    /// Struktura bloku (horizontal, origin = MinPoint):
    ///   (0, 0)          = dolny kraniec linii dystrybucyjnej (dolny pret)
    ///   (0, barsH)      = gorny kraniec linii dystrybucyjnej (gorny pret)
    ///   (0, barsH+arm)  = koniec tekstu (szczyt ramienia)
    ///   tekst wstawiony w (-TextArmOffset, barsH+ArmLength) rot=90 deg
    /// </summary>
    public static class AnnotationEngine
    {
        public const string AnnotAppName = "RC_BAR_ANNOT";

        public const double DefaultTextHeight = 125.0;
        public const double DotRadius         = 35.0;
        public const double ArmLength         = 500.0;   // stem od gornego preta do poczatku tekstu
        public const double TextCharWidth     = 80.0;    // szerokosc znaku romans.shx ~0.65*h
        public const double TextArmOffset     = 70.0;    // odleglosc tekstu od osi ramienia

        public const double AnnotOffset = 300.0; // zachowane dla kompatybilnosci

        // ----------------------------------------------------------------
        // AppId
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
        // LeaderResult
        // ----------------------------------------------------------------

        /// <summary>
        /// BarGroupIds : puste (dist + doty sa wewnatrz bloku)
        /// ArmIds      : ObjectId BlockReference — jedyna encja grupy ANNOT
        /// </summary>
        public struct LeaderResult
        {
            public List<ObjectId> BarGroupIds;
            public List<ObjectId> ArmIds;
        }

        // ----------------------------------------------------------------
        // CreateLeader — tworzy caly blok RC_ANNOT_nnn
        // ----------------------------------------------------------------

        public static LeaderResult CreateLeader(
            Database db,
            SlabGenResult result,
            BarData bar,
            bool horizontal,
            int posNr)
        {
            EnsureAppIdRegistered(db);

            var res = new LeaderResult
            {
                BarGroupIds = new List<ObjectId>(),
                ArmIds      = new List<ObjectId>()
            };
            if (result.BarIds.Count == 0) return res;

            // Tekst i dlugosc ramienia przez tekst
            string annotText   = $"{bar.Count} {bar.Mark} {bar.LayerCode}";
            double textExtentY = annotText.Length * TextCharWidth;
            double armTotalLen = ArmLength + textExtentY;   // ramie przez CALY tekst

            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            string ltName = ResolveLinetype(db, tr, "_DOT", "CENTER");

            var blockRefId = CreateAnnotBlock(
                tr, space, db,
                result, annotText, armTotalLen, ltName,
                horizontal, posNr, bar);

            res.ArmIds.Add(blockRefId);

            tr.Commit();
            return res;
        }

        // ----------------------------------------------------------------
        // Tworzenie bloku RC_ANNOT_nnn
        // ----------------------------------------------------------------

        private static ObjectId CreateAnnotBlock(
            Transaction tr,
            BlockTableRecord space,
            Database db,
            SlabGenResult result,
            string annotText,
            double armTotalLen,    // ArmLength + textExtentY
            string ltName,
            bool horizontal,
            int posNr,
            BarData bar)
        {
            string blockName = $"RC_ANNOT_{posNr:D3}";
            var blockTable   = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);

            // Usun stara definicje (bez referencji)
            if (blockTable.Has(blockName))
            {
                var oldBtr  = (BlockTableRecord)tr.GetObject(blockTable[blockName], OpenMode.ForWrite);
                if (oldBtr.GetBlockReferenceIds(true, false).Count == 0)
                {
                    var ids = new List<ObjectId>();
                    foreach (ObjectId oid in oldBtr) ids.Add(oid);
                    foreach (var oid in ids)
                        if (!oid.IsErased)
                            ((DBObject)tr.GetObject(oid, OpenMode.ForWrite)).Erase();
                    oldBtr.Erase();
                }
                else
                {
                    blockName = $"RC_ANNOT_{posNr:D3}_{DateTime.Now.Ticks % 100000L}";
                }
            }

            var btr   = new BlockTableRecord { Name = blockName };
            var btrId = blockTable.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            if (horizontal)
                BuildHorizontal(tr, btr, db, result, annotText, armTotalLen, ltName);
            else
                BuildVertical(tr, btr, db, result, annotText, armTotalLen, ltName);

            // Punkt wstawienia bloku
            Point3d insertPt = horizontal
                ? new Point3d(result.MinPoint.X, result.MinPoint.Y, 0)   // lewy-dolny kat pretow
                : new Point3d(result.MinPoint.X, result.MaxPoint.Y, 0);  // lewy-gorny kat pretow

            var blockRef = new BlockReference(insertPt, btrId) { Layer = "0" };
            space.AppendEntity(blockRef);
            tr.AddNewlyCreatedDBObject(blockRef, true);

            WriteAnnotXData(blockRef, bar);
            return blockRef.ObjectId;
        }

        // ----------------------------------------------------------------
        // Wypelnianie bloku — prety poziome
        // ----------------------------------------------------------------
        // Origin bloku = (MinPoint.X, MinPoint.Y)
        // Y=0           = dolny pret
        // Y=barsH       = gorny pret (szczyt dist line)
        // Y=barsH+arm   = koniec ramienia = koniec tekstu
        // ----------------------------------------------------------------

        private static void BuildHorizontal(
            Transaction tr, BlockTableRecord btr, Database db,
            SlabGenResult result, string annotText, double armTotalLen, string ltName)
        {
            double barsH = result.MaxPoint.Y - result.MinPoint.Y;

            // 1. Linia dystrybucyjna: (0,0) → (0, barsH)
            var distLine = new Line(new Point3d(0, 0, 0), new Point3d(0, barsH, 0))
            {
                Layer      = LayerManager.LeaderLayer,
                LineWeight = LineWeight.LineWeight018,
                Linetype   = ltName
            };
            btr.AppendEntity(distLine);
            tr.AddNewlyCreatedDBObject(distLine, true);

            // 2. Doty przy kazdym precie (Y wzgledne do MinPoint.Y)
            foreach (var pt in result.LeaderTickPoints)
            {
                double relY = pt.Y - result.MinPoint.Y;
                AddDot(tr, btr, new Point3d(0, relY, 0), DotRadius);
            }

            // 3. Ramie: (0, barsH) → (0, barsH + armTotalLen) — przez CALY tekst
            var arm = new Line(
                new Point3d(0, barsH, 0),
                new Point3d(0, barsH + armTotalLen, 0))
            {
                Layer      = LayerManager.LeaderLayer,
                LineWeight = LineWeight.LineWeight018,
                Linetype   = "Continuous"
            };
            btr.AppendEntity(arm);
            tr.AddNewlyCreatedDBObject(arm, true);

            // 4. Tekst: -TextArmOffset w X, barsH+ArmLength w Y, rotacja 90 deg
            //    Znaki rozciagaja sie w kierunku -X od podstawy → tekst po lewej ramienia
            var dbText = new DBText
            {
                TextString     = annotText,
                Layer          = LayerManager.AnnotLayer,
                Height         = DefaultTextHeight,
                Position       = new Point3d(-TextArmOffset, barsH + ArmLength, 0),
                Rotation       = Math.PI / 2.0,
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode   = TextVerticalMode.TextBase,
                TextStyleId    = GetTextStyleId(db)
            };
            btr.AppendEntity(dbText);
            tr.AddNewlyCreatedDBObject(dbText, true);
        }

        // ----------------------------------------------------------------
        // Wypelnianie bloku — prety pionowe
        // ----------------------------------------------------------------
        // Origin bloku = (MinPoint.X, MaxPoint.Y)
        // X=0          = lewy pret (poczatek dist line)
        // X=barsW      = prawy pret
        // Y=armTotalLen = szczyt ramienia
        // ----------------------------------------------------------------

        private static void BuildVertical(
            Transaction tr, BlockTableRecord btr, Database db,
            SlabGenResult result, string annotText, double armTotalLen, string ltName)
        {
            double barsW = result.MaxPoint.X - result.MinPoint.X;

            // 1. Linia dystrybucyjna: pozioma (0,0) → (barsW, 0)
            var distLine = new Line(new Point3d(0, 0, 0), new Point3d(barsW, 0, 0))
            {
                Layer      = LayerManager.LeaderLayer,
                LineWeight = LineWeight.LineWeight018,
                Linetype   = ltName
            };
            btr.AppendEntity(distLine);
            tr.AddNewlyCreatedDBObject(distLine, true);

            // 2. Doty
            foreach (var pt in result.LeaderTickPoints)
            {
                double relX = pt.X - result.MinPoint.X;
                AddDot(tr, btr, new Point3d(relX, 0, 0), DotRadius);
            }

            // 3. Ramie: pionowe w gore (0,0) → (0, armTotalLen)
            var arm = new Line(
                new Point3d(0, 0, 0),
                new Point3d(0, armTotalLen, 0))
            {
                Layer      = LayerManager.LeaderLayer,
                LineWeight = LineWeight.LineWeight018,
                Linetype   = "Continuous"
            };
            btr.AppendEntity(arm);
            tr.AddNewlyCreatedDBObject(arm, true);

            // 4. Tekst poziomy przy ArmLength, +TextArmOffset w X
            var dbText = new DBText
            {
                TextString     = annotText,
                Layer          = LayerManager.AnnotLayer,
                Height         = DefaultTextHeight,
                Position       = new Point3d(TextArmOffset, ArmLength, 0),
                Rotation       = 0.0,
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode   = TextVerticalMode.TextBase,
                TextStyleId    = GetTextStyleId(db)
            };
            btr.AppendEntity(dbText);
            tr.AddNewlyCreatedDBObject(dbText, true);
        }

        // ----------------------------------------------------------------
        // Dot (romb Solid)
        // ----------------------------------------------------------------

        private static void AddDot(Transaction tr, BlockTableRecord btr, Point3d c, double r)
        {
            var s = new Solid();
            s.SetPointAt(0, new Point3d(c.X,     c.Y + r, 0));
            s.SetPointAt(1, new Point3d(c.X + r, c.Y,     0));
            s.SetPointAt(2, new Point3d(c.X - r, c.Y,     0));
            s.SetPointAt(3, new Point3d(c.X,     c.Y - r, 0));
            s.Layer = LayerManager.LeaderLayer;
            btr.AppendEntity(s);
            tr.AddNewlyCreatedDBObject(s, true);
        }

        // ----------------------------------------------------------------
        // Pomocnicze
        // ----------------------------------------------------------------

        private static string ResolveLinetype(Database db, Transaction tr, params string[] preferred)
        {
            var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            foreach (var n in preferred)
                if (lt.Has(n)) return n;
            return "Continuous";
        }

        private static ObjectId GetTextStyleId(Database db)
        {
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var st = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            return st.Has(LayerManager.AnnotTextStyle) ? st[LayerManager.AnnotTextStyle] : db.Textstyle;
        }

        // ----------------------------------------------------------------
        // XData
        // ----------------------------------------------------------------

        private static void WriteAnnotXData(Entity entity, BarData bar)
        {
            entity.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName,  AnnotAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Mark),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.LayerCode),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)bar.Count),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)bar.Diameter),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.Spacing),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Direction),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Position),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.LengthA)
            );
        }

        public static BarData ReadAnnotXData(Entity entity)
        {
            var xdata = entity.GetXDataForApplication(AnnotAppName);
            if (xdata == null) return null;
            var v = xdata.AsArray();
            if (v.Length < 9) return null;
            return new BarData
            {
                Mark      = (string)v[1].Value,
                LayerCode = (string)v[2].Value,
                Count     = (short)v[3].Value,
                Diameter  = (short)v[4].Value,
                Spacing   = (double)v[5].Value,
                Direction = (string)v[6].Value,
                Position  = (string)v[7].Value,
                LengthA   = (double)v[8].Value
            };
        }

        public static bool IsAnnotation(Entity entity)
            => entity.GetXDataForApplication(AnnotAppName) != null;
    }
}
