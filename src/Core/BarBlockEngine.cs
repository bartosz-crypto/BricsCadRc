using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Tworzy i aktualizuje blok RC_SLAB_BARS_nnn — jeden obiekt zawierajacy
    /// prety + linie dystrybucyjna + ramie + tekst (styl RBCR_EN_CONSTLINEMODULE z ASD).
    ///
    /// Architektura bloku (prety poziome, Direction="X"):
    ///   Origin bloku = swiata (x0, y0) po otulinie (lewy-dolny rog pola pretow)
    ///   Pret i  : linia pozioma od (0, i*spacing) do (barWidth, i*spacing)
    ///   barsSpan = (count-1)*spacing   <- Y-zasieg pola pretow
    ///   Dist line: pionowa x=0, od y=0 do y=barsSpan, linetype _DOT
    ///   Arm      : pionowa x=0, od y=barsSpan do y=barsSpan+armTotalLen, Continuous
    ///   Tekst    : (-TextArmOffset, barsSpan+ArmLength), rot=90 deg
    ///
    /// Dla pretow pionowych (Direction="Y") analogicznie z zamienionymi osiami.
    ///
    /// Gripy (sterowane przez AnnotGripOverrule):
    ///   [0] @ insertion point  → ruch boczny wzd. osi pretow
    ///   [1] @ koniec dist line → rozciaganie span (recalc liczby pretow)
    ///   [2] @ koniec arm       → wydluzenie ramienia
    /// </summary>
    public static class BarBlockEngine
    {
        // ----------------------------------------------------------------
        // Stale
        // ----------------------------------------------------------------

        public const string XAppName = "RC_BAR_BLOCK";

        public const double DefaultTextHeight = 125.0;
        public const double ArmLength         = 500.0;   // ramie do tekstu (mm)
        public const double TextCharWidth     = 80.0;    // szerokosc znaku romans.shx
        public const double TextArmOffset     = 70.0;    // odleglosc tekstu od osi ramienia

        // ----------------------------------------------------------------
        // Generate — tworzy blok RC_SLAB_BARS_nnn z polilinii obrysu plyty
        // ----------------------------------------------------------------

        public static ObjectId Generate(
            Database db,
            ObjectId plineId,
            BarData bar,
            bool horizontal,
            double cover,
            int posNr)
        {
            EnsureAppIdRegistered(db);
            LayerManager.EnsureLayersExist(db);

            // Wczytaj wierzcholki polilinii
            List<Point2d> vertices;
            using (var readTr = db.TransactionManager.StartTransaction())
            {
                var pline = (Polyline)readTr.GetObject(plineId, OpenMode.ForRead);
                vertices = GetPolylineVertices(pline);
                readTr.Commit();
            }
            if (vertices.Count < 3) return ObjectId.Null;

            // Bounding box + zakres z otuling
            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            foreach (var v in vertices)
            {
                if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
                if (v.Y < minY) minY = v.Y; if (v.Y > maxY) maxY = v.Y;
            }
            double x0 = minX + cover, y0 = minY + cover;
            double x1 = maxX - cover, y1 = maxY - cover;
            if (x0 >= x1 || y0 >= y1) return ObjectId.Null;

            // Parametry ukladu pretow
            double barLength, rawSpan;
            if (horizontal) { barLength = x1 - x0; rawSpan = y1 - y0; }
            else             { barLength = y1 - y0; rawSpan = x1 - x0; }

            int    count    = Math.Max(1, (int)(rawSpan / bar.Spacing) + 1);
            double barsSpan = (count - 1) * bar.Spacing;  // snap do wielokrotnosci spacing

            string annotText  = $"{count} {bar.Mark} {bar.LayerCode}";
            double armTotalLen = ArmLength + annotText.Length * TextCharWidth;

            bar.Count      = count;
            bar.LengthA    = barLength;
            bar.BarsSpan   = barsSpan;
            bar.ArmTotalLen = armTotalLen;

            using var tr = db.TransactionManager.StartTransaction();
            var space      = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);

            string blockName = $"RC_SLAB_BARS_{posNr:D3}";

            // Usun stara definicje jesli bez referencji
            if (blockTable.Has(blockName))
            {
                var oldBtr = (BlockTableRecord)tr.GetObject(blockTable[blockName], OpenMode.ForWrite);
                if (oldBtr.GetBlockReferenceIds(true, false).Count == 0)
                {
                    EraseAllInBtr(tr, oldBtr);
                    oldBtr.Erase();
                }
                else
                {
                    blockName = $"RC_SLAB_BARS_{posNr:D3}_{DateTime.Now.Ticks % 100000L}";
                }
            }

            var btr   = new BlockTableRecord { Name = blockName };
            var btrId = blockTable.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            string ltDot = ResolveLinetype(db, tr, "_DOT", "CENTER");

            if (horizontal)
                BuildHorizontal(tr, btr, db, bar, barLength, barsSpan, count, ltDot);
            else
                BuildVertical(tr, btr, db, bar, barLength, barsSpan, count, ltDot);

            var insertPt = new Point3d(x0, y0, 0);
            var blockRef = new BlockReference(insertPt, btrId) { Layer = "0" };
            space.AppendEntity(blockRef);
            tr.AddNewlyCreatedDBObject(blockRef, true);

            WriteXData(blockRef, bar);
            tr.Commit();

            return blockRef.ObjectId;
        }

        // ----------------------------------------------------------------
        // BuildHorizontal — prety poziome (Direction="X")
        //   Pret i : (0, i*spacing) → (barWidth, i*spacing)
        //   Dist   : x=0, y: 0 → barsSpan   (linetype _DOT)
        //   Arm    : x=0, y: barsSpan → barsSpan+armTotalLen
        //   Tekst  : (-TextArmOffset, barsSpan+ArmLength), rot=90
        // ----------------------------------------------------------------
        private static void BuildHorizontal(
            Transaction tr, BlockTableRecord btr, Database db,
            BarData bar, double barWidth, double barsSpan, int count, string ltDot)
        {
            string barLayer   = LayerManager.GetLayerName(bar.LayerCode);
            var    lw         = DiameterToLineWeight(bar.Diameter);
            string annotText  = $"{count} {bar.Mark} {bar.LayerCode}";
            double textLen    = annotText.Length * TextCharWidth;
            double armTotalLen = ArmLength + textLen;

            // 1. Prety
            for (int i = 0; i < count; i++)
            {
                double y = i * bar.Spacing;
                var line = new Line(new Point3d(0, y, 0), new Point3d(barWidth, y, 0))
                    { Layer = barLayer, LineWeight = lw };
                btr.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
            }

            // 2. Dist line (pionowa, _DOT)
            var distLine = new Line(new Point3d(0, 0, 0), new Point3d(0, barsSpan, 0))
            {
                Layer      = LayerManager.LeaderLayer,
                LineWeight = LineWeight.LineWeight018,
                Linetype   = ltDot
            };
            btr.AppendEntity(distLine);
            tr.AddNewlyCreatedDBObject(distLine, true);

            // 3. Ramie (Continuous)
            var armLine = new Line(new Point3d(0, barsSpan, 0), new Point3d(0, barsSpan + armTotalLen, 0))
            {
                Layer      = LayerManager.LeaderLayer,
                LineWeight = LineWeight.LineWeight018,
                Linetype   = "Continuous"
            };
            btr.AppendEntity(armLine);
            tr.AddNewlyCreatedDBObject(armLine, true);

            // 4. Tekst
            var dbText = new DBText
            {
                TextString     = annotText,
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
        //   Pret i : (i*spacing, 0) → (i*spacing, barHeight)
        //   Dist   : y=barHeight, x: 0 → barsSpan   (linetype _DOT)
        //   Arm    : x=0, y: barHeight → barHeight+armTotalLen
        //   Tekst  : (TextArmOffset, barHeight+ArmLength), rot=0
        // ----------------------------------------------------------------
        private static void BuildVertical(
            Transaction tr, BlockTableRecord btr, Database db,
            BarData bar, double barHeight, double barsSpan, int count, string ltDot)
        {
            string barLayer   = LayerManager.GetLayerName(bar.LayerCode);
            var    lw         = DiameterToLineWeight(bar.Diameter);
            string annotText  = $"{count} {bar.Mark} {bar.LayerCode}";
            double textLen    = annotText.Length * TextCharWidth;
            double armTotalLen = ArmLength + textLen;

            // 1. Prety
            for (int i = 0; i < count; i++)
            {
                double x = i * bar.Spacing;
                var line = new Line(new Point3d(x, 0, 0), new Point3d(x, barHeight, 0))
                    { Layer = barLayer, LineWeight = lw };
                btr.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
            }

            // 2. Dist line (pozioma na gorze, _DOT)
            var distLine = new Line(new Point3d(0, barHeight, 0), new Point3d(barsSpan, barHeight, 0))
            {
                Layer      = LayerManager.LeaderLayer,
                LineWeight = LineWeight.LineWeight018,
                Linetype   = ltDot
            };
            btr.AppendEntity(distLine);
            tr.AddNewlyCreatedDBObject(distLine, true);

            // 3. Ramie (Continuous) — idzie w gore z lewego konca dist line
            var armLine = new Line(new Point3d(0, barHeight, 0), new Point3d(0, barHeight + armTotalLen, 0))
            {
                Layer      = LayerManager.LeaderLayer,
                LineWeight = LineWeight.LineWeight018,
                Linetype   = "Continuous"
            };
            btr.AppendEntity(armLine);
            tr.AddNewlyCreatedDBObject(armLine, true);

            // 4. Tekst
            var dbText = new DBText
            {
                TextString     = annotText,
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
        // RegenerateBarBlock — wywolywany gdy uzytkownik przeciaga grip span
        // Przelicza liczbe pretow na bieżąco (jak ASD), przebudowuje BTR.
        // ----------------------------------------------------------------
        public static void RegenerateBarBlock(BlockReference br, double newBarsSpan)
        {
            var bar = ReadXData(br);
            if (bar == null || bar.Spacing <= 0) return;

            newBarsSpan     = Math.Max(bar.Spacing, newBarsSpan);
            int    newCount  = (int)(newBarsSpan / bar.Spacing) + 1;
            newBarsSpan      = (newCount - 1) * bar.Spacing;  // snap

            string annotText  = $"{newCount} {bar.Mark} {bar.LayerCode}";
            double armTotalLen = bar.ArmTotalLen > 0
                ? bar.ArmTotalLen                // zachowaj dlugosc jesli zmieniona przez uzytkownika
                : ArmLength + annotText.Length * TextCharWidth;

            bar.Count      = newCount;
            bar.BarsSpan   = newBarsSpan;
            bar.ArmTotalLen = armTotalLen;

            // Zaktualizuj XData bezposrednio na blockref (jest otwarty do zapisu w grip-op)
            WriteXData(br, bar);

            // Przebuduj BTR w nowej transakcji
            using var tr = br.Database.TransactionManager.StartTransaction();
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
            EraseAllInBtr(tr, btr);

            string ltDot = ResolveLinetype(br.Database, tr, "_DOT", "CENTER");
            if (bar.Direction == "X")
                BuildHorizontal(tr, btr, br.Database, bar, bar.LengthA, newBarsSpan, newCount, ltDot);
            else
                BuildVertical(tr, btr, br.Database, bar, bar.LengthA, newBarsSpan, newCount, ltDot);

            tr.Commit();
        }

        // ----------------------------------------------------------------
        // UpdateArm — wywolywany gdy uzytkownik przeciaga grip arm-top
        // ----------------------------------------------------------------
        public static void UpdateArm(BlockReference br, double newArmTotalLen)
        {
            var bar = ReadXData(br);
            if (bar == null) return;

            string annotText = $"{bar.Count} {bar.Mark} {bar.LayerCode}";
            double textLen   = annotText.Length * TextCharWidth;
            double newArmLen = Math.Max(50.0, newArmTotalLen - textLen);
            newArmTotalLen   = newArmLen + textLen;

            // XData
            bar.ArmTotalLen = newArmTotalLen;
            WriteXData(br, bar);

            // Geometria BTR
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
        // Pozycje gripow w ukladzie swiata (uzywane przez overrule)
        // ----------------------------------------------------------------

        /// <summary>Grip 0 — ruch boczny (punkt wstawienia bloku).</summary>
        public static Point3d GripLateral(BlockReference br) => br.Position;

        /// <summary>Grip 1 — koniec dist line (rozciaganie span).</summary>
        public static Point3d GripSpan(BlockReference br, BarData bar)
        {
            var ins = br.Position;
            if (bar.Direction == "X")
                return new Point3d(ins.X, ins.Y + bar.BarsSpan, 0);
            else
                return new Point3d(ins.X + bar.BarsSpan, ins.Y + bar.LengthA, 0);
        }

        /// <summary>Grip 2 — koniec ramienia (wydluzanie arm).</summary>
        public static Point3d GripArm(BlockReference br, BarData bar)
        {
            var ins = br.Position;
            if (bar.Direction == "X")
                return new Point3d(ins.X, ins.Y + bar.BarsSpan + bar.ArmTotalLen, 0);
            else
                return new Point3d(ins.X, ins.Y + bar.LengthA + bar.ArmTotalLen, 0);
        }

        // ----------------------------------------------------------------
        // XData
        // Format: [0]AppName [1]Mark [2]LayerCode [3]Count [4]Diameter
        //         [5]Spacing [6]Direction [7]Position [8]LengthA
        //         [9]BarsSpan [10]ArmTotalLen
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
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.BarsSpan),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.ArmTotalLen)
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
            if (v.Length >= 11)
            {
                bd.BarsSpan    = (double)v[9].Value;
                bd.ArmTotalLen = (double)v[10].Value;
            }
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

        private static string ResolveLinetype(Database db, Transaction tr, params string[] preferred)
        {
            var lt = (LinetypeTable)tr.GetObject(db.LinetypeTableId, OpenMode.ForRead);
            foreach (var n in preferred) if (lt.Has(n)) return n;
            return "Continuous";
        }

        private static ObjectId GetTextStyleId(Database db)
        {
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var st = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            return st.Has(LayerManager.AnnotTextStyle) ? st[LayerManager.AnnotTextStyle] : db.Textstyle;
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
