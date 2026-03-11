using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Tworzy blok RC_ANNOT_nnn — OSOBNA annotacja ukladu pretow.
    /// Odpowiednik RBCR_ENDE_BARDDESC / lidera w ASD.
    ///
    /// Architektura bloku (horizontal, Direction="X"):
    ///   Origin = (annotX, minY)   — annotX = prawy bok pretow + offset
    ///   Romby  : (0, i*spacing)   — WZGLEDNE pozycje (i=0..count-1)
    ///            KLUCZ: NIE uzywamy world-coordinates pretow!
    ///            Dzieki temu przesuwanie annotacji wzd. X nie zrywa wyrownania.
    ///   Dist line: x=0, y: 0 → barsSpan  (linetype _DOT)
    ///   Arm      : x=0, y: barsSpan → barsSpan+armTotalLen
    ///   Tekst    : (-TextArmOffset, barsSpan+ArmLength), rot=90
    ///
    /// Gripy (sterowane przez AnnotGripOverrule):
    ///   [0] @ insertion point      → ruch boczny (X-constrained)
    ///   [1] @ top of arm           → wydluzenie ramienia
    /// </summary>
    public static class AnnotationEngine
    {
        public const string AnnotAppName = "RC_BAR_ANNOT";

        public const double DefaultTextHeight = 125.0;
        public const double DotRadius         = 35.0;
        public const double ArmLength         = 500.0;
        public const double TextCharWidth     = 80.0;
        public const double TextArmOffset     = 70.0;

        // Domyslna odleglosc annotacji od prawego boku pretow [mm]
        public const double AnnotDefaultOffset = 300.0;

        // ----------------------------------------------------------------
        // LeaderResult
        // ----------------------------------------------------------------

        public struct LeaderResult
        {
            public ObjectId BlockRefId;
        }

        // ----------------------------------------------------------------
        // CreateLeader — tworzy blok RC_ANNOT_nnn
        // Wywolywane po BarBlockEngine.Generate().
        // bar.Count i bar.Spacing musza byc juz ustawione.
        // ----------------------------------------------------------------

        public static LeaderResult CreateLeader(
            Database db,
            BarBlockEngine.BarBlockResult barResult,
            BarData bar,
            bool horizontal,
            int posNr)
        {
            var res = new LeaderResult();
            if (!barResult.IsValid || bar.Count == 0) return res;

            EnsureAppIdRegistered(db);

            string annotText   = $"{bar.Count} {bar.Mark} {bar.LayerCode}";
            double textExtentY = annotText.Length * TextCharWidth;
            double armTotalLen = ArmLength + textExtentY;

            bar.ArmTotalLen = armTotalLen;

            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            string ltName = ResolveLinetype(db, tr, "_DOT", "CENTER");

            string blockName = $"RC_ANNOT_{posNr:D3}";
            var blockTable   = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);

            if (blockTable.Has(blockName))
            {
                var oldBtr = (BlockTableRecord)tr.GetObject(blockTable[blockName], OpenMode.ForWrite);
                if (oldBtr.GetBlockReferenceIds(true, false).Count == 0)
                {
                    var ids = new List<ObjectId>();
                    foreach (ObjectId oid in oldBtr) ids.Add(oid);
                    foreach (var oid in ids)
                        if (!oid.IsErased)
                            ((DBObject)tr.GetObject(oid, OpenMode.ForWrite)).Erase();
                    oldBtr.Erase();
                }
                else blockName = $"RC_ANNOT_{posNr:D3}_{DateTime.Now.Ticks % 100000L}";
            }

            var btr   = new BlockTableRecord { Name = blockName };
            var btrId = blockTable.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            if (horizontal)
                BuildHorizontal(tr, btr, db, bar, ltName, armTotalLen);
            else
                BuildVertical(tr, btr, db, bar, ltName, armTotalLen);

            // Punkt wstawienia: dla X-bars prawy bok pretow + offset
            Point3d insertPt = horizontal
                ? new Point3d(barResult.MaxPoint.X + AnnotDefaultOffset, barResult.MinPoint.Y, 0)
                : new Point3d(barResult.MinPoint.X, barResult.MaxPoint.Y + AnnotDefaultOffset, 0);

            var blockRef = new BlockReference(insertPt, btrId) { Layer = "0" };
            space.AppendEntity(blockRef);
            tr.AddNewlyCreatedDBObject(blockRef, true);

            WriteAnnotXData(blockRef, bar);
            tr.Commit();

            res.BlockRefId = blockRef.ObjectId;
            return res;
        }

        // ----------------------------------------------------------------
        // BuildHorizontal — prety poziome (Direction="X")
        //
        // KLUCZOWA ZMIANA: romby sa w WZGLEDNYCH pozycjach (0, i*spacing)
        // — nie zaleza od world-coordinates pretow.
        // Przesuwanie bloku annotacji wzd. X nie zrywa wyrownania Y z pretami.
        //
        //   Romb i : center=(0, i*spacing),  i=0..count-1
        //   Dist   : x=0, y: 0 → barsSpan   (linetype _DOT)
        //   Arm    : x=0, y: barsSpan → barsSpan+armTotalLen
        //   Tekst  : (-TextArmOffset, barsSpan+ArmLength), rot=90
        // ----------------------------------------------------------------
        private static void BuildHorizontal(
            Transaction tr, BlockTableRecord btr, Database db,
            BarData bar, string ltName, double armTotalLen)
        {
            double barsSpan = (bar.Count - 1) * bar.Spacing;

            // 1. Dist line
            var distLine = new Line(new Point3d(0, 0, 0), new Point3d(0, barsSpan, 0))
            {
                Layer      = LayerManager.LeaderLayer,
                LineWeight = LineWeight.LineWeight018,
                Linetype   = ltName
            };
            btr.AppendEntity(distLine);
            tr.AddNewlyCreatedDBObject(distLine, true);

            // 2. Romby — wzgledne pozycje: (0, i*spacing), i=0..count-1
            for (int i = 0; i < bar.Count; i++)
                AddDot(tr, btr, new Point3d(0, i * bar.Spacing, 0), DotRadius);

            // 3. Ramie
            var arm = new Line(
                new Point3d(0, barsSpan, 0),
                new Point3d(0, barsSpan + armTotalLen, 0))
            {
                Layer      = LayerManager.LeaderLayer,
                LineWeight = LineWeight.LineWeight018,
                Linetype   = "Continuous"
            };
            btr.AppendEntity(arm);
            tr.AddNewlyCreatedDBObject(arm, true);

            // 4. Tekst
            var dbText = new DBText
            {
                TextString     = $"{bar.Count} {bar.Mark} {bar.LayerCode}",
                Layer          = LayerManager.AnnotLayer,
                Height         = DefaultTextHeight,
                Position       = new Point3d(-TextArmOffset, barsSpan + ArmLength, 0),
                Rotation       = Math.PI / 2.0,
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode   = TextVerticalMode.TextBase,
                TextStyleId    = GetTextStyleId(db)
            };
            btr.AppendEntity(dbText);
            tr.AddNewlyCreatedDBObject(dbText, true);
        }

        // ----------------------------------------------------------------
        // BuildVertical — prety pionowe (Direction="Y")
        //
        //   Romb i : center=(i*spacing, 0),  i=0..count-1
        //   Dist   : y=barHeight, x: 0 → barsSpan   (linetype _DOT)
        //   Arm    : x=0, y: barHeight → barHeight+armTotalLen
        //   Tekst  : (TextArmOffset, barHeight+ArmLength), rot=0
        // ----------------------------------------------------------------
        private static void BuildVertical(
            Transaction tr, BlockTableRecord btr, Database db,
            BarData bar, string ltName, double armTotalLen)
        {
            double barsSpan  = (bar.Count - 1) * bar.Spacing;
            double barHeight = bar.LengthA;

            // 1. Dist line (pozioma na gorze)
            var distLine = new Line(new Point3d(0, barHeight, 0), new Point3d(barsSpan, barHeight, 0))
            {
                Layer      = LayerManager.LeaderLayer,
                LineWeight = LineWeight.LineWeight018,
                Linetype   = ltName
            };
            btr.AppendEntity(distLine);
            tr.AddNewlyCreatedDBObject(distLine, true);

            // 2. Romby — (i*spacing, 0), i=0..count-1
            for (int i = 0; i < bar.Count; i++)
                AddDot(tr, btr, new Point3d(i * bar.Spacing, barHeight, 0), DotRadius);

            // 3. Ramie
            var arm = new Line(
                new Point3d(0, barHeight, 0),
                new Point3d(0, barHeight + armTotalLen, 0))
            {
                Layer      = LayerManager.LeaderLayer,
                LineWeight = LineWeight.LineWeight018,
                Linetype   = "Continuous"
            };
            btr.AppendEntity(arm);
            tr.AddNewlyCreatedDBObject(arm, true);

            // 4. Tekst
            var dbText = new DBText
            {
                TextString     = $"{bar.Count} {bar.Mark} {bar.LayerCode}",
                Layer          = LayerManager.AnnotLayer,
                Height         = DefaultTextHeight,
                Position       = new Point3d(TextArmOffset, barHeight + ArmLength, 0),
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
        // UpdateArmInBlock — grip arm-top
        // ----------------------------------------------------------------

        public static void UpdateArmInBlock(BlockReference br, double newArmTotalLen)
        {
            var bar = ReadAnnotXData(br);
            if (bar == null || bar.BarsSpan <= 0) return;

            string annotText  = $"{bar.Count} {bar.Mark} {bar.LayerCode}";
            double textLen    = annotText.Length * TextCharWidth;
            double newArmLen  = Math.Max(50.0, newArmTotalLen - textLen);
            newArmTotalLen    = newArmLen + textLen;

            bar.ArmTotalLen = newArmTotalLen;
            WriteAnnotXData(br, bar);

            using var tr = br.Database.TransactionManager.StartTransaction();
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

            Line   armLine = null;
            DBText armText = null;
            foreach (ObjectId oid in btr)
            {
                if (oid.IsErased) continue;
                var obj = tr.GetObject(oid, OpenMode.ForRead);
                if (obj is Line ln && ln.Linetype == "Continuous" && armLine == null)
                    armLine = ln;
                else if (obj is DBText txt)
                    armText = txt;
            }

            if (armLine != null)
            {
                armLine.UpgradeOpen();
                if (bar.Direction == "X")
                {
                    armLine.StartPoint = new Point3d(0, bar.BarsSpan, 0);
                    armLine.EndPoint   = new Point3d(0, bar.BarsSpan + newArmTotalLen, 0);
                }
                else
                {
                    armLine.StartPoint = new Point3d(0, bar.LengthA, 0);
                    armLine.EndPoint   = new Point3d(0, bar.LengthA + newArmTotalLen, 0);
                }
            }

            if (armText != null)
            {
                armText.UpgradeOpen();
                if (bar.Direction == "X")
                    armText.Position = new Point3d(-TextArmOffset, bar.BarsSpan + newArmLen, 0);
                else
                    armText.Position = new Point3d(TextArmOffset, bar.LengthA + newArmLen, 0);
            }

            tr.Commit();
        }

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
        // XData
        // [0]AppName [1]Mark [2]LayerCode [3]Count [4]Diameter
        // [5]Spacing [6]Direction [7]Position [8]LengthA
        // [9]BarsSpan [10]ArmTotalLen
        // ----------------------------------------------------------------

        internal static void WriteAnnotXData(Entity entity, BarData bar)
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
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.LengthA),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.BarsSpan),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.ArmTotalLen)
            );
        }

        public static BarData ReadAnnotXData(Entity entity)
        {
            var xdata = entity.GetXDataForApplication(AnnotAppName);
            if (xdata == null) return null;
            var v = xdata.AsArray();
            if (v.Length < 9) return null;
            var bd = new BarData
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
            if (v.Length >= 11)
            {
                bd.BarsSpan    = (double)v[9].Value;
                bd.ArmTotalLen = (double)v[10].Value;
            }
            return bd;
        }

        public static bool IsAnnotation(Entity entity)
            => entity.GetXDataForApplication(AnnotAppName) != null;

        // ----------------------------------------------------------------
        // Helpers
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
    }
}
