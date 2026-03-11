using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Tworzy i aktualizuje blok RC_SLAB_BARS_nnn — TYLKO prety, bez annotacji.
    /// Odpowiednik RBCR_EN_CONSTLINEMODULE z ASD (sam modul pretow).
    ///
    /// Architektura bloku (Direction="X", prety poziome):
    ///   Origin = (x0, y0) po otulinie
    ///   Pret i : (0, i*spacing) → (barWidth, i*spacing)
    ///   barsSpan = (count-1)*spacing
    ///
    /// Gripy:
    ///   [0] @ insertion point      → ruch boczny (X-constrained dla X-bars)
    ///   [1] @ (insX, insY+barsSpan) → rozciaganie span → recalc bar count
    /// </summary>
    public static class BarBlockEngine
    {
        public const string XAppName = "RC_BAR_BLOCK";

        // ----------------------------------------------------------------
        // Wynik generowania
        // ----------------------------------------------------------------

        public struct BarBlockResult
        {
            public ObjectId BlockRefId;
            public Point3d  MinPoint;   // (x0, y0) po otulinie
            public Point3d  MaxPoint;   // (x1, y1) po otulinie
            public bool     IsValid => BlockRefId != ObjectId.Null;
        }

        // ----------------------------------------------------------------
        // Generate — tworzy blok RC_SLAB_BARS_nnn z polilinii obrysu
        // ----------------------------------------------------------------

        public static BarBlockResult Generate(
            Database db,
            ObjectId plineId,
            BarData  bar,
            bool     horizontal,
            double   cover,
            int      posNr)
        {
            var empty = new BarBlockResult();

            EnsureAppIdRegistered(db);
            LayerManager.EnsureLayersExist(db);

            List<Point2d> vertices;
            using (var readTr = db.TransactionManager.StartTransaction())
            {
                var pline = (Polyline)readTr.GetObject(plineId, OpenMode.ForRead);
                vertices  = GetPolylineVertices(pline);
                readTr.Commit();
            }
            if (vertices.Count < 3) return empty;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var v in vertices)
            {
                if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y; if (v.Y > maxY) maxY = v.Y;
            }
            double x0 = minX + cover, y0 = minY + cover;
            double x1 = maxX - cover, y1 = maxY - cover;
            if (x0 >= x1 || y0 >= y1) return empty;

            double barLength, rawSpan;
            if (horizontal) { barLength = x1 - x0; rawSpan = y1 - y0; }
            else             { barLength = y1 - y0; rawSpan = x1 - x0; }

            int    count    = Math.Max(1, (int)(rawSpan / bar.Spacing) + 1);
            double barsSpan = (count - 1) * bar.Spacing;

            bar.Count    = count;
            bar.LengthA  = barLength;
            bar.BarsSpan = barsSpan;

            using var tr = db.TransactionManager.StartTransaction();
            var space      = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);

            string blockName = $"RC_SLAB_BARS_{posNr:D3}";
            if (blockTable.Has(blockName))
            {
                var oldBtr = (BlockTableRecord)tr.GetObject(blockTable[blockName], OpenMode.ForWrite);
                if (oldBtr.GetBlockReferenceIds(true, false).Count == 0)
                {
                    EraseAllInBtr(tr, oldBtr);
                    oldBtr.Erase();
                }
                else blockName = $"RC_SLAB_BARS_{posNr:D3}_{DateTime.Now.Ticks % 100000L}";
            }

            var btr   = new BlockTableRecord { Name = blockName };
            var btrId = blockTable.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            if (horizontal) BuildHorizontal(tr, btr, bar, barLength, count);
            else             BuildVertical  (tr, btr, bar, barLength, count);

            var insertPt = new Point3d(x0, y0, 0);
            var blockRef = new BlockReference(insertPt, btrId) { Layer = "0" };
            space.AppendEntity(blockRef);
            tr.AddNewlyCreatedDBObject(blockRef, true);

            WriteXData(blockRef, bar);
            tr.Commit();

            return new BarBlockResult
            {
                BlockRefId = blockRef.ObjectId,
                MinPoint   = new Point3d(x0, y0, 0),
                MaxPoint   = new Point3d(x1, y1, 0)
            };
        }

        // ----------------------------------------------------------------
        // BuildHorizontal — prety poziome (Direction="X")
        //   Pret i : (0, i*spacing) → (barWidth, i*spacing)
        // ----------------------------------------------------------------
        private static void BuildHorizontal(
            Transaction tr, BlockTableRecord btr,
            BarData bar, double barWidth, int count)
        {
            string barLayer = LayerManager.GetLayerName(bar.LayerCode);
            var    lw       = DiameterToLineWeight(bar.Diameter);

            for (int i = 0; i < count; i++)
            {
                double y = i * bar.Spacing;
                var line = new Line(new Point3d(0, y, 0), new Point3d(barWidth, y, 0))
                    { Layer = barLayer, LineWeight = lw };
                btr.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
            }
        }

        // ----------------------------------------------------------------
        // BuildVertical — prety pionowe (Direction="Y")
        //   Pret i : (i*spacing, 0) → (i*spacing, barHeight)
        // ----------------------------------------------------------------
        private static void BuildVertical(
            Transaction tr, BlockTableRecord btr,
            BarData bar, double barHeight, int count)
        {
            string barLayer = LayerManager.GetLayerName(bar.LayerCode);
            var    lw       = DiameterToLineWeight(bar.Diameter);

            for (int i = 0; i < count; i++)
            {
                double x = i * bar.Spacing;
                var line = new Line(new Point3d(x, 0, 0), new Point3d(x, barHeight, 0))
                    { Layer = barLayer, LineWeight = lw };
                btr.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
            }
        }

        // ----------------------------------------------------------------
        // RegenerateBarBlock — wywolywany przez grip span
        // ----------------------------------------------------------------
        public static void RegenerateBarBlock(BlockReference br, double newBarsSpan)
        {
            var bar = ReadXData(br);
            if (bar == null || bar.Spacing <= 0) return;

            newBarsSpan    = Math.Max(bar.Spacing, newBarsSpan);
            int newCount    = (int)(newBarsSpan / bar.Spacing) + 1;
            newBarsSpan     = (newCount - 1) * bar.Spacing;

            bar.Count    = newCount;
            bar.BarsSpan = newBarsSpan;

            // Zaktualizuj XData na blockref (jest juz otwarty w grip-op)
            WriteXData(br, bar);

            // Przebuduj BTR
            using var tr = br.Database.TransactionManager.StartTransaction();
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
            EraseAllInBtr(tr, btr);

            if (bar.Direction == "X") BuildHorizontal(tr, btr, bar, bar.LengthA, newCount);
            else                      BuildVertical  (tr, btr, bar, bar.LengthA, newCount);

            tr.Commit();
        }

        // ----------------------------------------------------------------
        // Pozycje gripow
        // ----------------------------------------------------------------

        public static Point3d GripLateral(BlockReference br) => br.Position;

        public static Point3d GripSpan(BlockReference br, BarData bar)
        {
            var ins = br.Position;
            return bar.Direction == "X"
                ? new Point3d(ins.X, ins.Y + bar.BarsSpan, 0)
                : new Point3d(ins.X + bar.BarsSpan, ins.Y, 0);
        }

        // ----------------------------------------------------------------
        // XData: [0]AppName [1]Mark [2]LayerCode [3]Count [4]Diameter
        //        [5]Spacing [6]Direction [7]Position [8]LengthA [9]BarsSpan
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

        internal static void WriteXData(Entity entity, BarData bar)
        {
            entity.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName,  XAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Mark),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.LayerCode),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)bar.Count),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)bar.Diameter),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.Spacing),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Direction),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Position),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.LengthA),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.BarsSpan)
            );
        }

        public static BarData ReadXData(Entity entity)
        {
            var xdata = entity.GetXDataForApplication(XAppName);
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
            if (v.Length >= 10) bd.BarsSpan = (double)v[9].Value;
            return bd;
        }

        public static bool IsBarBlock(Entity entity)
            => entity.GetXDataForApplication(XAppName) != null;

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static void EraseAllInBtr(Transaction tr, BlockTableRecord btr)
        {
            var ids = new List<ObjectId>();
            foreach (ObjectId oid in btr)
                if (!oid.IsErased) ids.Add(oid);
            foreach (var oid in ids)
                ((DBObject)tr.GetObject(oid, OpenMode.ForWrite)).Erase();
        }

        private static LineWeight DiameterToLineWeight(int diameter) => diameter switch
        {
            <= 10 => LineWeight.LineWeight025,
            <= 16 => LineWeight.LineWeight035,
            <= 20 => LineWeight.LineWeight050,
            _     => LineWeight.LineWeight070
        };

        private static List<Point2d> GetPolylineVertices(Polyline pline)
        {
            var pts = new List<Point2d>();
            for (int i = 0; i < pline.NumberOfVertices; i++)
                pts.Add(pline.GetPoint2dAt(i));
            return pts;
        }
    }
}
