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
        public const double TextCharWidth     = 65.0;   // txt.shx XScale=0.70 @ H=125
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
            bool leaderHorizontal,
            int posNr,
            Point3d? customInsertPt = null,
            bool barsHorizontal = true,
            bool leaderRight = true)
        {
            var res = new LeaderResult();
            if (!barResult.IsValid || bar.Count == 0) return res;

            EnsureAppIdRegistered(db);

            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            string ltName = ResolveLinetype(db, tr, "_DOT", "CENTER");

            string blockName = $"RC_ANNOT_{posNr:D3}_{Guid.NewGuid():N}".Substring(0, 32);
            var blockTable   = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);

            var btr   = new BlockTableRecord { Name = blockName };
            var btrId = blockTable.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            // barsHorizontal decyduje o geometrii prętów (BuildHorizontal vs BuildVertical)
            double armTotalLen = barsHorizontal
                ? BuildHorizontal(tr, btr, db, bar, ltName, leaderHorizontal, leaderRight)
                : BuildVertical  (tr, btr, db, bar, ltName);

            bar.ArmTotalLen = armTotalLen;

            // Punkt wstawienia: customInsertPt (klik użytkownika) lub auto (wg kierunku prętów)
            Point3d insertPt = customInsertPt ?? (barsHorizontal
                ? new Point3d(barResult.MaxPoint.X + AnnotDefaultOffset, barResult.MinPoint.Y, 0)
                : new Point3d(barResult.MinPoint.X, barResult.MaxPoint.Y, 0));

            // Dla X-bars z boku: X = krawędź rozkładu, Y = dół rozkładu + otulina
            if (barsHorizontal && leaderHorizontal && customInsertPt.HasValue)
            {
                bool cursorRight = customInsertPt.Value.X >= (barResult.MinPoint.X + barResult.MaxPoint.X) / 2.0;
                insertPt = new Point3d(
                    cursorRight ? barResult.MaxPoint.X : barResult.MinPoint.X,
                    barResult.MinPoint.Y,
                    0);
            }

            bar.LeaderHorizontal = leaderHorizontal;
            bar.LeaderRight      = leaderRight;
            if (leaderHorizontal && double.IsNaN(bar.ArmMidY))
                bar.ArmMidY = bar.BarsSpan / 2.0;   // inicjalizacja przy pierwszym zapisie

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
        //   Romb i : center=(0, i*spacing),  i=0..count-1
        //   Dist   : x=0, y: 0 → barsSpan   (linetype _DOT)
        //   Tekst  : (-TextArmOffset, barsSpan+ArmLength), rot=90, TextLeft
        //            → tekst rosnie W GORE od barsSpan+ArmLength
        //   Arm    : x=0, y: barsSpan → barsSpan+ArmLength+realTextLen
        //            → koniec ramienia DOKLADNIE przy ostatnim znaku ("B1")
        //
        // Zwraca rzeczywiste armTotalLen (ArmLength + zmierzony textLen).
        // ----------------------------------------------------------------
        private static double BuildHorizontal(
            Transaction tr, BlockTableRecord btr, Database db,
            BarData bar, string ltName, bool leaderHorizontal = false, bool leaderRight = true)
        {
            double barsSpan = (bar.Count - 1) * bar.Spacing;

            // 1. Dist line — zawsze pionowa
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

            double armTotalLen;

            if (!leaderHorizontal)
            {
                // Etykieta z góry/dołu — geometria pionowa (oryginalna)
                // tekst obrócony 90°, ramię w górę od końca dist line
                var textPos = new Point3d(-TextArmOffset, barsSpan + ArmLength, 0);
                var dbText = new DBText
                {
                    TextString     = $"{bar.Count} {bar.Mark}",
                    Layer          = LayerManager.AnnotLayer,
                    Height         = DefaultTextHeight,
                    Position       = textPos,
                    Rotation       = Math.PI / 2.0,
                    HorizontalMode = TextHorizontalMode.TextLeft,
                    VerticalMode   = TextVerticalMode.TextBase,
                    TextStyleId    = GetTextStyleId(db)
                };
                btr.AppendEntity(dbText);
                tr.AddNewlyCreatedDBObject(dbText, true);

                double textLen;
                try
                {
                    var ext = dbText.GeometricExtents;
                    textLen = ext.MaxPoint.Y - textPos.Y;
                    if (textLen <= 0) throw new InvalidOperationException();
                }
                catch { textLen = dbText.TextString.Length * TextCharWidth; }

                bar.TextLen = textLen;
                armTotalLen = ArmLength + textLen;

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
            }
            else
            {
                // Etykieta z boku — geometria pozioma
                double midY = barsSpan / 2.0;
                double hDir = leaderRight ? 1.0 : -1.0;

                // textLen znany przed rysowaniem (szacunek) — potrzebny do wyznaczenia armTotalLen i textPos
                double textLen = $"{bar.Count} {bar.Mark}".Length * TextCharWidth;
                bar.TextLen = textLen;
                armTotalLen = ArmLength + textLen;

                var textPos = new Point3d(hDir * armTotalLen, midY + TextArmOffset, 0);
                var dbText = new DBText
                {
                    TextString  = $"{bar.Count} {bar.Mark}",
                    Layer       = LayerManager.AnnotLayer,
                    Height      = DefaultTextHeight,
                    Position    = textPos,
                    Rotation    = 0.0,
                    TextStyleId = GetTextStyleId(db)
                };
                btr.AppendEntity(dbText);
                tr.AddNewlyCreatedDBObject(dbText, true);

                var arm = new Line(
                    new Point3d(0, midY, 0),
                    new Point3d(hDir * armTotalLen, midY, 0))
                {
                    Layer      = LayerManager.LeaderLayer,
                    LineWeight = LineWeight.LineWeight018,
                    Linetype   = "Continuous"
                };
                btr.AppendEntity(arm);
                tr.AddNewlyCreatedDBObject(arm, true);

            }

            return armTotalLen;
        }

        // ----------------------------------------------------------------
        // BuildVertical — prety pionowe (Direction="Y")
        //
        // Symetryczna wersja BuildHorizontal, obrocona o 90° CW:
        //
        //   Block insert = (minX, maxY) — lewa-gorna krawedz pretow
        //   Romb i : (i*spacing, 0)          — na gorze pretow (y=0 = maxY w world)
        //   Dist   : y=0, x: 0 → barsSpan    (linetype _DOT, pozioma)
        //   Tekst  : (barsSpan+ArmLength, TextArmOffset), rot=0, TextLeft
        //            → tekst rosnie W PRAWO od barsSpan+ArmLength
        //   Arm    : y=0, x: barsSpan → barsSpan+ArmLength+realTextLen
        //            → koniec ramienia DOKLADNIE przy ostatnim znaku tekstu
        //
        // Zwraca rzeczywiste armTotalLen (ArmLength + zmierzona szerokosc tekstu).
        // ----------------------------------------------------------------
        private static double BuildVertical(
            Transaction tr, BlockTableRecord btr, Database db,
            BarData bar, string ltName)
        {
            double barsSpan = (bar.Count - 1) * bar.Spacing;

            // 1. Dist line — pozioma przy y=0 (gorna krawedz pretow)
            var distLine = new Line(new Point3d(0, 0, 0), new Point3d(barsSpan, 0, 0))
            {
                Layer      = LayerManager.LeaderLayer,
                LineWeight = LineWeight.LineWeight018,
                Linetype   = ltName
            };
            btr.AppendEntity(distLine);
            tr.AddNewlyCreatedDBObject(distLine, true);

            // 2. Romby — na pozycjach pretow: (i*spacing, 0), i=0..count-1
            for (int i = 0; i < bar.Count; i++)
                AddDot(tr, btr, new Point3d(i * bar.Spacing, 0, 0), DotRadius);

            // 3. Tekst — tworzony PRZED ramieniem, zeby zmierzyc jego szerokosc
            //    TextArmOffset POWYZEJ ramienia (y=TextArmOffset), rot=0 (poziomy)
            //    Poczatek tekstu: barsSpan + ArmLength od lewej krawedzi
            var textPos = new Point3d(barsSpan + ArmLength, TextArmOffset, 0);
            var dbText = new DBText
            {
                TextString     = $"{bar.Count} {bar.Mark}",
                Layer          = LayerManager.AnnotLayer,
                Height         = DefaultTextHeight,
                Position       = textPos,
                Rotation       = 0.0,
                HorizontalMode = TextHorizontalMode.TextLeft,
                VerticalMode   = TextVerticalMode.TextBase,
                TextStyleId    = GetTextStyleId(db)
            };
            btr.AppendEntity(dbText);
            tr.AddNewlyCreatedDBObject(dbText, true);

            // Zmierz rzeczywista szerokosc tekstu z GeometricExtents
            double textLen;
            try
            {
                var ext = dbText.GeometricExtents;
                textLen = ext.MaxPoint.X - textPos.X;
                if (textLen <= 0) throw new InvalidOperationException();
            }
            catch { textLen = dbText.TextString.Length * TextCharWidth; }

            bar.TextLen    = textLen;       // stala szerokosc tekstu — zapisana w XData
            double armTotalLen = ArmLength + textLen;

            // 4. Ramie — poziome od (barsSpan,0) do (barsSpan+armTotalLen,0)
            //    Koniec dokladnie przy ostatnim znaku tekstu
            var arm = new Line(
                new Point3d(barsSpan, 0, 0),
                new Point3d(barsSpan + armTotalLen, 0, 0))
            {
                Layer      = LayerManager.LeaderLayer,
                LineWeight = LineWeight.LineWeight018,
                Linetype   = "Continuous"
            };
            btr.AppendEntity(arm);
            tr.AddNewlyCreatedDBObject(arm, true);

            return armTotalLen;
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
        // GetArmMidY — odczytuje aktualną pozycję Y linii arm z BTR bloku
        // ----------------------------------------------------------------

        public static double GetArmMidY(BlockReference br)
        {
            var bar = ReadAnnotXData(br);
            if (bar != null && !double.IsNaN(bar.ArmMidY))
                return bar.ArmMidY;

            using var tr = br.Database.TransactionManager.StartTransaction();
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            foreach (ObjectId oid in btr)
            {
                if (oid.IsErased) continue;
                var obj = tr.GetObject(oid, OpenMode.ForRead);
                if (obj is Line ln
                    && Math.Abs(ln.StartPoint.X) < 1.0
                    && Math.Abs(ln.StartPoint.Y - ln.EndPoint.Y) < 1.0
                    && Math.Abs(ln.EndPoint.X) > 1.0)
                { tr.Commit(); return ln.StartPoint.Y; }
            }
            tr.Commit();
            return bar?.BarsSpan / 2.0 ?? 0;
        }

        // ----------------------------------------------------------------
        // UpdateArmInBlock — grip arm-top
        // ----------------------------------------------------------------

        public static void UpdateArmInBlock(BlockReference br, double newArmTotalLen, double newMidY = double.NaN)
        {
            var bar = ReadAnnotXData(br);
            if (bar == null || bar.BarsSpan <= 0) return;

            double clampedNew = Math.Max(50.0, newArmTotalLen);
            bool   xHoriz     = bar.Direction == "X" && bar.LeaderHorizontal;
            double midY       = !double.IsNaN(newMidY) ? newMidY : (!double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : bar.BarsSpan / 2.0);

            bar.ArmTotalLen = clampedNew;
            if (xHoriz) bar.ArmMidY = midY;   // persist ArmMidY w XData
            WriteAnnotXData(br, bar);

            using var tr = br.Database.TransactionManager.StartTransaction();
            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);

            double textLen = bar.TextLen > 10 ? bar.TextLen : (bar.ArmTotalLen - ArmLength);

            Line   armLine = null;
            DBText armText = null;
            foreach (ObjectId oid in btr)
            {
                if (oid.IsErased) continue;
                var obj = tr.GetObject(oid, OpenMode.ForRead);
                if (obj is Line ln && armLine == null)
                {
                    if (xHoriz)
                    {
                        // Arm poziome X-bars: Start.X=0, pozioma (Start.Y==End.Y), EndPoint.X!=0
                        // (dist line jest pionowa — Start.X=End.X=0; arm ma EndPoint.X != 0)
                        if (Math.Abs(ln.StartPoint.X) < 1.0
                            && Math.Abs(ln.StartPoint.Y - ln.EndPoint.Y) < 1.0
                            && Math.Abs(ln.EndPoint.X) > 1.0)
                            armLine = ln;
                    }
                    else if (bar.Direction == "X")
                    {
                        // Arm pionowe: Start.Y = barsSpan, End.Y > barsSpan
                        if (Math.Abs(ln.StartPoint.Y - bar.BarsSpan) < 1.0
                            && ln.EndPoint.Y > bar.BarsSpan + 1.0)
                            armLine = ln;
                    }
                    else
                    {
                        // Arm poziome Y-bars: Start.Y = 0, Start.X = barsSpan (> 0)
                        if (Math.Abs(ln.StartPoint.Y) < 1.0 && ln.StartPoint.X > 1.0)
                            armLine = ln;
                    }
                }
                else if (obj is DBText txt && armText == null)
                    armText = txt;
                if (armLine != null && armText != null) break;
            }

            if (armLine != null)
            {
                armLine.UpgradeOpen();
                if (xHoriz)
                {
                    double hDir        = bar.LeaderRight ? 1.0 : -1.0;
                    armLine.StartPoint = new Point3d(0, midY, 0);
                    armLine.EndPoint   = new Point3d(hDir * clampedNew, midY, 0);
                }
                else if (bar.Direction == "X")
                {
                    armLine.StartPoint = new Point3d(0, bar.BarsSpan, 0);
                    armLine.EndPoint   = new Point3d(0, bar.BarsSpan + clampedNew, 0);
                }
                else
                {
                    armLine.StartPoint = new Point3d(bar.BarsSpan, 0, 0);
                    armLine.EndPoint   = new Point3d(bar.BarsSpan + clampedNew, 0, 0);
                }
            }

            if (armText != null)
            {
                armText.UpgradeOpen();
                if (xHoriz)
                {
                    double hDir       = bar.LeaderRight ? 1.0 : -1.0;
                    double textStartX = hDir * clampedNew;
                    armText.Position  = new Point3d(textStartX, midY + TextArmOffset, 0);
                }
                else if (bar.Direction == "X")
                {
                    // Tekst pionowy (rot=90°): poczatek tekstu = koniec ramienia - textLen
                    double textStartY = bar.BarsSpan + clampedNew - textLen;
                    armText.Position  = new Point3d(-TextArmOffset, textStartY, 0);
                }
                else
                {
                    // Tekst poziomy (rot=0°): poczatek tekstu = koniec ramienia - textLen
                    double textStartX = bar.BarsSpan + clampedNew - textLen;
                    armText.Position  = new Point3d(textStartX, TextArmOffset, 0);
                }
            }

            // Connector — pionowa linia łącząca krawędź dist line z arm gdy midY poza [0, barsSpan]
            // Dotyczy tylko xHoriz. Szukamy istniejącego connectora po charakterystyce:
            //   Start.X ≈ 0, End.X ≈ 0, pionowy (Start.X == End.X), != dist line i != arm
            if (xHoriz)
            {
                double barsSpanLocal = bar.BarsSpan;
                Line connectorLine = null;
                foreach (ObjectId oid in btr)
                {
                    if (oid.IsErased) continue;
                    var obj = tr.GetObject(oid, OpenMode.ForRead);
                    if (obj is Line ln
                        && Math.Abs(ln.StartPoint.X) < 1.0
                        && Math.Abs(ln.EndPoint.X) < 1.0
                        && Math.Abs(ln.StartPoint.Y - ln.EndPoint.Y) > 1.0  // pionowa
                        && !(Math.Abs(ln.StartPoint.Y) < 1.0 && Math.Abs(ln.EndPoint.Y - barsSpanLocal) < 1.0)  // nie dist line
                        && !(Math.Abs(ln.StartPoint.Y - barsSpanLocal) < 1.0 && Math.Abs(ln.EndPoint.Y) < 1.0)) // nie dist line odwrotnie
                    {
                        connectorLine = ln;
                        break;
                    }
                }

                bool needConnector = midY < -1.0 || midY > barsSpanLocal + 1.0;

                if (!needConnector && connectorLine != null)
                {
                    // midY w zakresie — connector zbędny, usuń
                    connectorLine.UpgradeOpen();
                    connectorLine.Erase();
                }
                else if (needConnector)
                {
                    double edgeY    = midY < 0 ? 0 : barsSpanLocal;
                    var btrWrite    = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
                    if (connectorLine != null)
                    {
                        // Zaktualizuj istniejący
                        connectorLine.UpgradeOpen();
                        connectorLine.StartPoint = new Point3d(0, edgeY, 0);
                        connectorLine.EndPoint   = new Point3d(0, midY,  0);
                    }
                    else
                    {
                        // Stwórz nowy
                        var conn = new Line(new Point3d(0, edgeY, 0), new Point3d(0, midY, 0))
                        {
                            Layer      = LayerManager.LeaderLayer,
                            LineWeight = LineWeight.LineWeight018,
                            Linetype   = "Continuous"
                        };
                        btrWrite.AppendEntity(conn);
                        tr.AddNewlyCreatedDBObject(conn, true);
                    }
                }
            }

            tr.Commit();
            try { br.RecordGraphicsModified(true); } catch { }
        }

        // ----------------------------------------------------------------
        // SyncAnnotation — wywolywane po zmianie Count/BarsSpan w RC_BAR_BLOCK
        // Szuka bloku RC_ANNOT_nnn z pasujacym Markiem i przebudowuje jego BTR.
        // ----------------------------------------------------------------

        public static void SyncAnnotation(Database db, BarData updatedBar)
        {
            // Szukaj po AnnotHandle (unikalne dla kazdego rozkladu) — bez handle fallback na Mark
            var annotId = !string.IsNullOrEmpty(updatedBar.AnnotHandle)
                ? FindAnnotationIdByHandle(db, updatedBar.AnnotHandle)
                : FindAnnotationIdByMark  (db, updatedBar.Mark);
            if (annotId == ObjectId.Null) return;

            // BarsSpan musi byc zgodne z nowym Count
            updatedBar.BarsSpan = (updatedBar.Count - 1) * updatedBar.Spacing;

            using var tr = db.TransactionManager.StartTransaction();
            var annotBr = (BlockReference)tr.GetObject(annotId, OpenMode.ForWrite);

            // Zachowaj ArmTotalLen z istniejacych XData (uzytkownik mogl zmienic grip)
            var existingAnnot = ReadAnnotXData(annotBr);
            if (existingAnnot != null)
                updatedBar.ArmTotalLen = existingAnnot.ArmTotalLen;

            var btr = (BlockTableRecord)tr.GetObject(annotBr.BlockTableRecord, OpenMode.ForWrite);

            // Wymazanie calej zawartosci BTR
            var ids = new List<ObjectId>();
            foreach (ObjectId oid in btr)
                if (!oid.IsErased) ids.Add(oid);
            foreach (var oid in ids)
                ((DBObject)tr.GetObject(oid, OpenMode.ForWrite)).Erase();

            // Przebudowa — BuildH/V remierzy tekst i ustawia TextLen
            string ltName     = ResolveLinetype(db, tr, "_DOT", "CENTER");
            double armTotalLen = updatedBar.Direction == "X"
                ? BuildHorizontal(tr, btr, db, updatedBar, ltName)
                : BuildVertical  (tr, btr, db, updatedBar, ltName);

            updatedBar.ArmTotalLen = armTotalLen;
            WriteAnnotXData(annotBr, updatedBar);

            tr.Commit();
            try { annotBr.RecordGraphicsModified(true); } catch { }
        }

        /// <summary>
        /// Szuka annotacji po hex-stringu handle'a ObjectId — O(1), unikalny klucz.
        /// Uzywane gdy RC_BAR_BLOCK ma zapisany AnnotHandle (od momentu utworzenia annotacji).
        /// </summary>
        private static ObjectId FindAnnotationIdByHandle(Database db, string handleHex)
        {
            try
            {
                long val = Convert.ToInt64(handleHex.TrimStart('0').PadLeft(1, '0'), 16);
                var  h   = new Handle(val);
                if (db.TryGetObjectId(h, out ObjectId id) && !id.IsNull && !id.IsErased)
                    return id;
            }
            catch { }
            return ObjectId.Null;
        }

        private static ObjectId FindAnnotationIdByMark(Database db, string mark)
        {
            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId oid in space)
            {
                if (oid.IsErased) continue;
                if (!(tr.GetObject(oid, OpenMode.ForRead) is BlockReference br)) continue;
                var data = ReadAnnotXData(br);
                if (data != null && data.Mark == mark) return oid;
            }
            return ObjectId.Null;
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
        // [9]BarsSpan [10]ArmTotalLen [11]TextLen [12]LeaderHorizontal [13]LeaderRight
        // [14]ArmMidY
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
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.ArmTotalLen),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.TextLen),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)(bar.LeaderHorizontal ? 1 : 0)),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)(bar.LeaderRight ? 1 : 0)),
                new TypedValue((int)DxfCode.ExtendedDataReal,        !double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : bar.BarsSpan / 2.0)
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
            if (v.Length >= 12) bd.TextLen = (double)v[11].Value;
            if (v.Length >= 13) bd.LeaderHorizontal = (short)v[12].Value == 1;
            if (v.Length >= 14) bd.LeaderRight       = (short)v[13].Value == 1;
            if (v.Length >= 15) bd.ArmMidY           = (double)v[14].Value;
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
