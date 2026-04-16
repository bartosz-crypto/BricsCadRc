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
            bool leaderRight = true,
            bool leaderUp = true)
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

            // Init ArmMidY przed budowaniem geometrii (cursor Y dostępny tylko tutaj)
            if (barsHorizontal && leaderHorizontal && double.IsNaN(bar.ArmMidY))
            {
                double barsSpanInit  = (bar.Count - 1) * bar.Spacing;
                double barCenterY    = barResult.MinPoint.Y + barsSpanInit / 2.0;
                bool   initLeaderUp  = !customInsertPt.HasValue || customInsertPt.Value.Y >= barCenterY;
                bar.ArmMidY = barsSpanInit / 2.0 + (initLeaderUp ? ArmLength : -ArmLength);
            }

            // Init ArmMidY dla Y-bars z zagięciem (leaderVertical=true)
            if (!barsHorizontal && leaderHorizontal && double.IsNaN(bar.ArmMidY))
            {
                double barsSpanInit = (bar.Count - 1) * bar.Spacing;
                double barCenterX   = barResult.MinPoint.X + barsSpanInit / 2.0;
                bool   initRight    = !customInsertPt.HasValue || customInsertPt.Value.X >= barCenterX;
                bar.ArmMidY = barsSpanInit / 2.0 + (initRight ? ArmLength : -ArmLength);
            }

            // barsHorizontal decyduje o geometrii prętów (BuildHorizontal vs BuildVertical)
            double armTotalLen = barsHorizontal
                ? BuildHorizontal(tr, btr, db, bar, ltName, leaderHorizontal, leaderRight, leaderUp)
                : BuildVertical  (tr, btr, db, bar, ltName,
                    leaderVertical: leaderHorizontal,
                    leaderRight:    leaderRight,
                    leaderUp:       leaderUp);

            bar.ArmTotalLen = armTotalLen;

            // Punkt wstawienia: customInsertPt (klik użytkownika) lub auto (wg kierunku prętów)
            Point3d insertPt = customInsertPt ?? (barsHorizontal
                ? new Point3d(barResult.MaxPoint.X + AnnotDefaultOffset, barResult.MinPoint.Y,              0)
                : new Point3d(barResult.MinPoint.X,                      barResult.MaxPoint.Y + AnnotDefaultOffset, 0));

            bar.LeaderHorizontal = leaderHorizontal;
            bar.LeaderRight      = leaderRight;
            bar.LeaderUp         = leaderUp;
            // ArmMidY już zainicjalizowane powyżej (przed BuildHorizontal)

            var blockRef = new BlockReference(insertPt, btrId) { Layer = "0" };
            if (Math.Abs(bar.Angle) > 1e-6)
                blockRef.Rotation = bar.Angle;
            space.AppendEntity(blockRef);
            tr.AddNewlyCreatedDBObject(blockRef, true);

            WriteAnnotXData(blockRef, bar);
            tr.Commit();

            res.BlockRefId = blockRef.ObjectId;
            return res;
        }

        // ----------------------------------------------------------------
        // GetDistributionAxisMidPoint — środek linii rozkładu w układzie świata
        // ----------------------------------------------------------------
        public static Point3d GetDistributionAxisMidPoint(
            BarBlockEngine.BarBlockResult barResult, bool horizontal)
        {
            if (horizontal)
                return new Point3d(
                    barResult.MaxPoint.X,
                    barResult.MinPoint.Y + (barResult.MaxPoint.Y - barResult.MinPoint.Y) / 2.0,
                    0);
            else
                return new Point3d(
                    barResult.MinPoint.X + (barResult.MaxPoint.X - barResult.MinPoint.X) / 2.0,
                    barResult.MaxPoint.Y,
                    0);
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
            BarData bar, string ltName, bool leaderHorizontal = false, bool leaderRight = true, bool leaderUp = true)
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
            var visSetH = BarBlockEngine.GetVisibleIndicesPublic(bar.VisibilityMode, bar.VisibleIndices, bar.Count);
            for (int i = 0; i < bar.Count; i++)
            {
                bool isFirst = (i == 0);
                bool isLast  = (i == bar.Count - 1);

                if (bar.Count > 3 && isFirst)
                    AddArrow(tr, btr, new Point3d(0, i * bar.Spacing, 0), -Vector3d.YAxis);
                else if (bar.Count > 3 && isLast)
                    AddArrow(tr, btr, new Point3d(0, i * bar.Spacing, 0), Vector3d.YAxis);
                else if (bar.Count <= 3 || visSetH.Contains(i))
                    AddDot(tr, btr, new Point3d(0, i * bar.Spacing, 0), DotRadius);
            }

            if (bar.Count > 3)
            {
                double tickLen = DotRadius * 3;
                double topY    = (bar.Count - 1) * bar.Spacing;
                AddEndTick(tr, btr, new Point3d(0, 0,    0), Vector3d.XAxis, tickLen);
                AddEndTick(tr, btr, new Point3d(0, topY, 0), Vector3d.XAxis, tickLen);
            }

            double armTotalLen;

            if (!leaderHorizontal)
            {
                // Etykieta z góry/dołu — geometria pionowa.
                // leaderUp=true  → arm idzie w górę  (powyżej barsSpan)
                // leaderUp=false → arm idzie w dół   (poniżej barsSpan/2)
                var dbText = new DBText
                {
                    TextString     = $"{bar.Count} {bar.Mark}",
                    Layer          = LayerManager.AnnotLayer,
                    Height         = DefaultTextHeight,
                    Position       = new Point3d(-TextArmOffset, barsSpan + ArmLength, 0), // pozycja tymczasowa
                    Rotation       = Math.PI / 2.0,
                    HorizontalMode = TextHorizontalMode.TextLeft,
                    VerticalMode   = TextVerticalMode.TextBase,
                    TextStyleId    = GetTextStyleId(db)
                };
                btr.AppendEntity(dbText);
                tr.AddNewlyCreatedDBObject(dbText, true);

                // Zmierz rzeczywistą długość tekstu przez GeometricExtents (Rotation=90° → mierzymy Y)
                double textLen;
                try
                {
                    var extTxt = dbText.GeometricExtents;
                    textLen = extTxt.MaxPoint.Y - extTxt.MinPoint.Y;
                    if (textLen <= 0) throw new InvalidOperationException();
                }
                catch { textLen = dbText.TextString.Length * TextCharWidth; }

                bar.TextLen     = textLen;
                armTotalLen     = ArmLength + textLen;
                bar.ArmTotalLen = armTotalLen;

                Point3d armStart, armEnd, textFinal;
                double armBase = barsSpan / 2.0;
                if (leaderUp)
                {
                    // Góra: arm od armBase w górę; tekst zaczyna się tuż nad ArmLength
                    armStart  = new Point3d(0, armBase,              0);
                    armEnd    = new Point3d(0, armBase + armTotalLen, 0);
                    textFinal = new Point3d(-TextArmOffset, armBase + ArmLength, 0);
                }
                else
                {
                    // Dół: arm od armBase w dół; tekst na końcu arm (rośnie do góry od arm.End)
                    armStart  = new Point3d(0, armBase,              0);
                    armEnd    = new Point3d(0, armBase - armTotalLen, 0);
                    textFinal = new Point3d(-TextArmOffset, armBase - armTotalLen, 0);
                }

                dbText.Position = textFinal;

                var arm = new Line(armStart, armEnd)
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
                // Etykieta z boku — geometria z zagięciem 90°: segment pionowy + poziomy
                double midY = !double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : barsSpan / 2.0 + ArmLength;
                double hDir = leaderRight ? 1.0 : -1.0;

                // Segment 1: pionowy od środka dist line (barsSpan/2) do punktu zagięcia (midY)
                var stem = new Line(
                    new Point3d(0, barsSpan / 2.0, 0),
                    new Point3d(0, midY, 0))
                {
                    Layer      = LayerManager.LeaderLayer,
                    LineWeight = LineWeight.LineWeight018,
                    Linetype   = "Continuous"
                };
                btr.AppendEntity(stem);
                tr.AddNewlyCreatedDBObject(stem, true);

                // Segment 2: poziomy — najpierw wstaw tekst, zmierz rzeczywistą szerokość,
                // potem narysuj arm dopasowany do prawdziwej długości tekstu.
                // hDir=+1 (prawo): TextLeft, Position = lewa krawędź tekstu przy ArmLength od kink
                // hDir=-1 (lewo): TextRight, AlignmentPoint = prawa krawędź tekstu przy ArmLength od kink
                var alignPt = new Point3d(hDir * ArmLength, midY + TextArmOffset, 0);
                var dbText = new DBText
                {
                    TextString  = $"{bar.Count} {bar.Mark}",
                    Layer       = LayerManager.AnnotLayer,
                    Height      = DefaultTextHeight,
                    Position    = alignPt,
                    Rotation    = 0.0,
                    TextStyleId = GetTextStyleId(db)
                };
                if (hDir < 0)
                {
                    dbText.HorizontalMode = TextHorizontalMode.TextRight;
                    dbText.AlignmentPoint = alignPt;
                }
                btr.AppendEntity(dbText);
                tr.AddNewlyCreatedDBObject(dbText, true);

                // Zmierz rzeczywistą szerokość tekstu przez GeometricExtents
                double textLen;
                try
                {
                    var extTxt = dbText.GeometricExtents;
                    textLen = Math.Abs(extTxt.MaxPoint.X - extTxt.MinPoint.X);
                    if (textLen <= 0) throw new InvalidOperationException();
                }
                catch { textLen = dbText.TextString.Length * TextCharWidth; }

                bar.TextLen = textLen;
                armTotalLen = ArmLength + textLen;

                // arm: od kink (0,midY) do końca tekstu (zmierzona długość)
                var armSP = new Point3d(0, midY, 0);
                var armEP = new Point3d(hDir * (ArmLength + textLen), midY, 0);
                var arm = new Line(armSP, armEP)
                {
                    Layer      = LayerManager.LeaderLayer,
                    LineWeight = LineWeight.LineWeight018,
                    Linetype   = "Continuous"
                };
                btr.AppendEntity(arm);
                tr.AddNewlyCreatedDBObject(arm, true);

            }

            // Kółko w punkcie styku arma z dist line (złamanie)
            if (leaderHorizontal)
            {
                double midY = !double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : barsSpan / 2.0;
                if (midY >= 0 && midY <= barsSpan)
                    AddKinkCircle(tr, btr, new Point3d(0, midY, 0), DotRadius);
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
            BarData bar, string ltName,
            bool leaderVertical = false, bool leaderRight = true, bool leaderUp = true)
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
            var visSetV = BarBlockEngine.GetVisibleIndicesPublic(bar.VisibilityMode, bar.VisibleIndices, bar.Count);
            for (int i = 0; i < bar.Count; i++)
            {
                bool isFirst = (i == 0);
                bool isLast  = (i == bar.Count - 1);

                if (bar.Count > 3 && isFirst)
                    AddArrow(tr, btr, new Point3d(i * bar.Spacing, 0, 0), -Vector3d.XAxis);
                else if (bar.Count > 3 && isLast)
                    AddArrow(tr, btr, new Point3d(i * bar.Spacing, 0, 0), Vector3d.XAxis);
                else if (bar.Count <= 3 || visSetV.Contains(i))
                    AddDot(tr, btr, new Point3d(i * bar.Spacing, 0, 0), DotRadius);
            }

            if (bar.Count > 3)
            {
                double tickLen  = DotRadius * 3;
                double rightX   = (bar.Count - 1) * bar.Spacing;
                AddEndTick(tr, btr, new Point3d(0,      0, 0), Vector3d.YAxis, tickLen);
                AddEndTick(tr, btr, new Point3d(rightX, 0, 0), Vector3d.YAxis, tickLen);
            }

            double armTotalLen;

            if (!leaderVertical)
            {
                // Etykieta z lewej/prawej — geometria pozioma
                // leaderRight=true  → arm idzie w prawo (za ostatnim prętem)
                // leaderRight=false → arm idzie w lewo  (przed pierwszym prętem)
                var dbText = new DBText
                {
                    TextString     = $"{bar.Count} {bar.Mark}",
                    Layer          = LayerManager.AnnotLayer,
                    Height         = DefaultTextHeight,
                    Position       = new Point3d(barsSpan + ArmLength, TextArmOffset, 0),
                    Rotation       = 0.0,
                    HorizontalMode = TextHorizontalMode.TextLeft,
                    VerticalMode   = TextVerticalMode.TextBase,
                    TextStyleId    = GetTextStyleId(db)
                };
                btr.AppendEntity(dbText);
                tr.AddNewlyCreatedDBObject(dbText, true);

                double textLen;
                try
                {
                    var extTxt = dbText.GeometricExtents;
                    textLen = extTxt.MaxPoint.X - extTxt.MinPoint.X;
                    if (textLen <= 0) throw new InvalidOperationException();
                }
                catch { textLen = dbText.TextString.Length * TextCharWidth; }

                bar.TextLen     = textLen;
                armTotalLen     = ArmLength + textLen;
                bar.ArmTotalLen = armTotalLen;

                Point3d armStart, armEnd, textFinal;
                double armBase = barsSpan / 2.0;
                if (leaderRight)
                {
                    armStart  = new Point3d(armBase,              0, 0);
                    armEnd    = new Point3d(armBase + armTotalLen, 0, 0);
                    textFinal = new Point3d(armBase + ArmLength,   TextArmOffset, 0);
                }
                else
                {
                    armStart  = new Point3d(armBase,              0, 0);
                    armEnd    = new Point3d(armBase - armTotalLen, 0, 0);
                    textFinal = new Point3d(armBase - armTotalLen, TextArmOffset, 0);
                    dbText.HorizontalMode = TextHorizontalMode.TextLeft;
                }

                dbText.Position = textFinal;

                var arm = new Line(armStart, armEnd)
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
                // Etykieta z góry/dołu — geometria z zagięciem 90°: segment poziomy + pionowy
                double midX = !double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : barsSpan / 2.0 + ArmLength;
                double vDir = leaderUp ? 1.0 : -1.0;

                // Segment 1: poziomy od środka dist line (barsSpan/2) do punktu zagięcia (midX)
                var stem = new Line(
                    new Point3d(barsSpan / 2.0, 0, 0),
                    new Point3d(midX,           0, 0))
                {
                    Layer      = LayerManager.LeaderLayer,
                    LineWeight = LineWeight.LineWeight018,
                    Linetype   = "Continuous"
                };
                btr.AppendEntity(stem);
                tr.AddNewlyCreatedDBObject(stem, true);

                // Segment 2: pionowy arm + tekst po lewej stronie arma
                // Najpierw wstaw tekst na pozycji tymczasowej, zmierz textLen, potem przestaw

                var dbText = new DBText
                {
                    TextString  = $"{bar.Count} {bar.Mark}",
                    Layer       = LayerManager.AnnotLayer,
                    Height      = DefaultTextHeight,
                    Position    = new Point3d(midX - TextArmOffset, 0, 0),  // tymczasowa
                    Rotation    = Math.PI / 2.0,
                    TextStyleId = GetTextStyleId(db)
                };
                btr.AppendEntity(dbText);
                tr.AddNewlyCreatedDBObject(dbText, true);

                double textLen;
                try
                {
                    var extTxt = dbText.GeometricExtents;
                    textLen = Math.Abs(extTxt.MaxPoint.Y - extTxt.MinPoint.Y);
                    if (textLen <= 0) throw new InvalidOperationException();
                }
                catch { textLen = dbText.TextString.Length * TextCharWidth; }

                bar.TextLen = textLen;
                armTotalLen = ArmLength + textLen;

                // Rotation=90° → tekst zawsze rośnie w +Y od Position.
                // vDir= 1 (góra): tekst od +ArmLength do +armTotalLen → Position.Y = +ArmLength
                // vDir=-1 (dół):  tekst od -armTotalLen do -ArmLength → Position.Y = -armTotalLen
                double textStartY = vDir > 0
                    ? ArmLength
                    : -(armTotalLen);

                dbText.Position = new Point3d(midX - TextArmOffset, textStartY, 0);

                // Arm pionowy: od kink (midX,0) do (midX, vDir*armTotalLen)
                var arm = new Line(
                    new Point3d(midX, 0,                   0),
                    new Point3d(midX, vDir * armTotalLen,  0))
                {
                    Layer      = LayerManager.LeaderLayer,
                    LineWeight = LineWeight.LineWeight018,
                    Linetype   = "Continuous"
                };
                btr.AppendEntity(arm);
                tr.AddNewlyCreatedDBObject(arm, true);
            }

            // Kółko w punkcie styku arma z dist line (złamanie)
            if (leaderVertical)
            {
                double midX = !double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : barsSpan / 2.0;
                if (midX >= 0 && midX <= barsSpan)
                    AddKinkCircle(tr, btr, new Point3d(midX, 0, 0), DotRadius);
            }

            return armTotalLen;
        }

        // ----------------------------------------------------------------
        // KinkCircle — puste kółko w punkcie styku arma z dist line
        // ----------------------------------------------------------------

        private static void AddKinkCircle(Transaction tr, BlockTableRecord btr, Point3d center, double r)
        {
            var circle = new Circle(center, Vector3d.ZAxis, r);
            circle.Layer = LayerManager.LeaderLayer;
            btr.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);
        }

        // ----------------------------------------------------------------
        // Dot (romb Solid)
        // ----------------------------------------------------------------

        private static void AddDot(Transaction tr, BlockTableRecord btr, Point3d c, double r)
        {
            var circle = new Circle(c, Vector3d.ZAxis, r);
            circle.Layer = LayerManager.LeaderLayer;
            btr.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);

            var hatch = new Hatch();
            hatch.Layer = LayerManager.LeaderLayer;
            hatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
            hatch.Associative = false;
            btr.AppendEntity(hatch);
            tr.AddNewlyCreatedDBObject(hatch, true);

            hatch.AppendLoop(HatchLoopTypes.Outermost,
                new ObjectIdCollection { circle.ObjectId });
            hatch.EvaluateHatch(true);
        }

        private static void AddArrow(Transaction tr, BlockTableRecord btr, Point3d tip, Vector3d dir, double halfWidth = 22.5, double height = 151)
        {
            dir = dir.GetNormal();
            var perp = Vector3d.ZAxis.CrossProduct(dir).GetNormal();

            var p1 = tip;
            var p2 = tip - dir * height + perp * halfWidth;
            var p3 = tip - dir * height - perp * halfWidth;

            var solid = new Solid();
            solid.SetPointAt(0, new Point3d(p1.X, p1.Y, 0));
            solid.SetPointAt(1, new Point3d(p2.X, p2.Y, 0));
            solid.SetPointAt(2, new Point3d(p3.X, p3.Y, 0));
            solid.SetPointAt(3, new Point3d(p3.X, p3.Y, 0));
            solid.Layer = LayerManager.LeaderLayer;
            btr.AppendEntity(solid);
            tr.AddNewlyCreatedDBObject(solid, true);
        }

        private static void AddEndTick(Transaction tr, BlockTableRecord btr, Point3d center, Vector3d perpDir, double halfLen)
        {
            var line = new Line(
                center - perpDir * halfLen,
                center + perpDir * halfLen
            );
            line.Layer = LayerManager.LeaderLayer;
            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
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
            bool   yVert      = bar.Direction == "Y" && !bar.LeaderHorizontal;  // Y-bars, arm pionowy
            bool   yHoriz     = bar.Direction == "Y" && bar.LeaderHorizontal;   // Y-bars, arm ze złamaniem
            double midY       = !double.IsNaN(newMidY) ? newMidY : (!double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : bar.BarsSpan / 2.0);

            // textLen pochodzi z XData (stała długość tekstu) — PRZED nadpisaniem ArmTotalLen
            double textLen = bar.TextLen > 0 ? bar.TextLen : (bar.ArmTotalLen - ArmLength);

            bar.ArmTotalLen = clampedNew;
            if (xHoriz) bar.ArmMidY = midY;
            if (yHoriz)
            {
                double midXVal = !double.IsNaN(newMidY) ? newMidY
                               : (!double.IsNaN(bar.ArmMidY) ? bar.ArmMidY
                               : bar.BarsSpan / 2.0 + ArmLength);
                bar.ArmMidY = midXVal;
            }

            // Otwieramy br przez własną transakcję (ForWrite) — bezpośredni zapis br.XData
            // na parametrze z systemu gripów może nie trafić do trwałej transakcji BricsCAD.
            var objId = br.ObjectId;
            var db    = br.Database;
            using var tr = db.TransactionManager.StartTransaction();
            var brRw = (BlockReference)tr.GetObject(objId, OpenMode.ForWrite);
            WriteAnnotXData(brRw, bar);

            var btr = (BlockTableRecord)tr.GetObject(brRw.BlockTableRecord, OpenMode.ForRead);

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
                        // Arm pionowe: x=0 na obu końcach; góra EndY > barsSpan, dół EndY < barsSpan/2
                        if (Math.Abs(ln.StartPoint.X) < 1.0
                            && Math.Abs(ln.EndPoint.X) < 1.0
                            && (ln.EndPoint.Y > bar.BarsSpan + 1.0
                                || ln.EndPoint.Y < bar.BarsSpan / 2.0 - 1.0))
                            armLine = ln;
                    }
                    else if (yVert)
                    {
                        // Arm poziomy Y-bars (prosty): StartPoint.Y ≈ 0, EndPoint.Y ≈ 0
                        // StartPoint.X ≈ barsSpan/2 (środek dist line), EndPoint.X różni się
                        if (Math.Abs(ln.StartPoint.Y) < 1.0
                            && Math.Abs(ln.EndPoint.Y) < 1.0
                            && Math.Abs(ln.StartPoint.X - bar.BarsSpan / 2.0) < 5.0
                            && Math.Abs(ln.EndPoint.X - bar.BarsSpan / 2.0) > 5.0)
                            armLine = ln;
                    }
                    else if (yHoriz)
                    {
                        // Arm pionowe Y-bars ze złamaniem: StartPoint.Y ≈ 0, EndPoint.Y ≠ 0
                        // Nie może być dist line (X=0→barsSpan) ani tick mark (StartPoint≈EndPoint.X)
                        if (Math.Abs(ln.StartPoint.Y) < 1.0
                            && Math.Abs(ln.EndPoint.Y) > 1.0
                            && Math.Abs(ln.StartPoint.X) > 5.0                      // nie przy X=0
                            && Math.Abs(ln.StartPoint.X - bar.BarsSpan) > 5.0)      // nie przy X=barsSpan
                            armLine = ln;
                    }
                    else
                    {
                        // fallback
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
                    // arm poziomy: długość = clampedNew (zmienia się przez X drag)
                    armLine.StartPoint = new Point3d(0, midY, 0);
                    armLine.EndPoint   = new Point3d(hDir * clampedNew, midY, 0);
                }
                else if (bar.Direction == "X")
                {
                    armLine.StartPoint = new Point3d(0, bar.BarsSpan / 2.0, 0);
                    armLine.EndPoint   = bar.LeaderUp
                        ? new Point3d(0, bar.BarsSpan + clampedNew, 0)
                        : new Point3d(0, bar.BarsSpan / 2.0 - clampedNew, 0);
                }
                else if (yVert)
                {
                    // Arm poziomy: od (barsSpan/2, 0) w prawo lub lewo
                    armLine.StartPoint = new Point3d(bar.BarsSpan / 2.0, 0, 0);
                    armLine.EndPoint   = bar.LeaderRight
                        ? new Point3d(bar.BarsSpan + clampedNew, 0, 0)
                        : new Point3d(bar.BarsSpan / 2.0 - clampedNew, 0, 0);
                }
                else if (yHoriz)
                {
                    // Y-bars ze złamaniem: arm pionowy od (midXVal,0) do (midXVal, vDir*clampedNew)
                    double vDir    = bar.LeaderUp ? 1.0 : -1.0;
                    double midXVal = !double.IsNaN(newMidY) ? newMidY
                                   : (!double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : bar.BarsSpan / 2.0 + ArmLength);
                    armLine.StartPoint = new Point3d(midXVal, 0,                  0);
                    armLine.EndPoint   = new Point3d(midXVal, vDir * clampedNew,  0);
                }
                else
                {
                    // fallback poziomy
                    armLine.StartPoint = new Point3d(bar.BarsSpan, 0, 0);
                    armLine.EndPoint   = new Point3d(bar.BarsSpan + clampedNew, 0, 0);
                }
            }

            if (armText != null)
            {
                armText.UpgradeOpen();
                if (xHoriz)
                {
                    double hDir    = bar.LeaderRight ? 1.0 : -1.0;
                    // hDir=+1: TextLeft  → Position      = lewa krawędź (armEnd - textLen od kink)
                    // hDir=-1: TextRight → AlignmentPoint = prawa krawędź (ArmLength od kink)
                    var newTextPos = new Point3d(hDir * (clampedNew - textLen), midY + TextArmOffset, 0);
                    if (hDir > 0)
                        armText.Position = newTextPos;
                    else
                        armText.AlignmentPoint = newTextPos;
                }
                else if (bar.Direction == "X")
                {
                    Point3d newTextPos;
                    if (bar.LeaderUp)
                    {
                        // Góra: tekst zaczyna się ArmLength nad barsSpan, rośnie w górę
                        newTextPos = new Point3d(-TextArmOffset, bar.BarsSpan + clampedNew - textLen, 0);
                    }
                    else
                    {
                        // Dół: tekst przy końcu arm, rośnie w górę (od arm.End do arm.End + textLen)
                        newTextPos = new Point3d(-TextArmOffset, bar.BarsSpan / 2.0 - clampedNew, 0);
                    }
                    armText.Position = newTextPos;
                }
                else if (yVert)
                {
                    // Arm poziomy Y-bars: tekst poziomy (Rotation=0°), nad armem
                    double textStartX = bar.LeaderRight
                        ? bar.BarsSpan + clampedNew - textLen   // prawo: tekst przy końcu arma
                        : bar.BarsSpan / 2.0 - clampedNew;      // lewo: tekst od końca arma w prawo
                    armText.Position = new Point3d(textStartX, TextArmOffset, 0);
                }
                else if (yHoriz)
                {
                    // Y-bars ze złamaniem: tekst pionowy (Rotation=90°), odsunięty od arma w -X
                    double vDir = bar.LeaderUp ? 1.0 : -1.0;
                    // Tekst (Rotation=90°) zawsze rośnie w +Y od Position.
                    // Musi sięgać dokładnie do końca arma:
                    // vDir= 1 (góra): arm kończy się na +clampedNew → tekst od (clampedNew - textLen) do clampedNew
                    // vDir=-1 (dół):  arm kończy się na -clampedNew → tekst od -clampedNew do -(clampedNew - textLen)
                    double midXVal    = !double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : bar.BarsSpan / 2.0 + ArmLength;
                    double textStartY = vDir > 0
                        ? clampedNew - textLen   // góra: tekst kończy się przy końcu arma
                        : -clampedNew;           // dół: tekst zaczyna się przy końcu arma i rośnie w górę
                    armText.Position = new Point3d(midXVal - TextArmOffset, textStartY, 0);
                }
                else
                {
                    // fallback poziomy
                    double textStartX = bar.BarsSpan + clampedNew - textLen;
                    var newTextPos    = new Point3d(textStartX, TextArmOffset, 0);
                    armText.Position  = newTextPos;
                }
            }
            else
            {
            }

            // Zaktualizuj kółko złamania — usuń stare (jeśli istnieje) i dodaj nowe
            if (xHoriz || yHoriz)
            {
                var btrForCircles = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);

                // Zbierz i usuń kółka złamania (nie-prętowe)
                var circlesToErase = new List<Circle>();
                foreach (ObjectId oid in btrForCircles)
                {
                    if (oid.IsErased) continue;
                    var obj = tr.GetObject(oid, OpenMode.ForRead);
                    if (obj is Circle c)
                    {
                        bool isBarCircle = false;
                        if (xHoriz)
                        {
                            // Kółka prętów: center.X ≈ 0, center.Y = i*spacing (wielokrotność spacing)
                            // Kółko złamania: center.X ≈ 0, center.Y = midY (dowolna wartość)
                            double rem = bar.Spacing > 0 ? c.Center.Y % bar.Spacing : -1;
                            isBarCircle = Math.Abs(rem) < 5.0 || Math.Abs(rem - bar.Spacing) < 5.0;
                        }
                        else // yHoriz
                        {
                            double rem = bar.Spacing > 0 ? c.Center.X % bar.Spacing : -1;
                            isBarCircle = Math.Abs(rem) < 5.0 || Math.Abs(rem - bar.Spacing) < 5.0;
                        }
                        if (!isBarCircle)
                            circlesToErase.Add(c);
                    }
                }
                foreach (var c in circlesToErase)
                {
                    c.UpgradeOpen();
                    c.Erase();
                }

                // Dodaj nowe kółko złamania tylko gdy midY/midX w zakresie dist line
                if (xHoriz && midY >= 0 && midY <= bar.BarsSpan)
                    AddKinkCircle(tr, btrForCircles, new Point3d(0, midY, 0), DotRadius);
                else if (yHoriz)
                {
                    double midXK = !double.IsNaN(newMidY) ? newMidY
                                 : (!double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : bar.BarsSpan / 2.0 + ArmLength);
                    if (midXK >= 0 && midXK <= bar.BarsSpan)
                        AddKinkCircle(tr, btrForCircles, new Point3d(midXK, 0, 0), DotRadius);
                }
            }

            // Pionowy stem — od środka dist line (barsSpan/2) do punktu zagięcia (midY).
            // Dotyczy tylko xHoriz. Zawsze obecny (nie tylko gdy midY poza dist line).
            if (xHoriz)
            {
                double barsSpanLocal = bar.BarsSpan;
                Line stemLine = null;
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
                        stemLine = ln;
                        break;
                    }
                }

                var btrWriteStem = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
                if (stemLine != null)
                {
                    stemLine.UpgradeOpen();
                    stemLine.StartPoint = new Point3d(0, barsSpanLocal / 2.0, 0);
                    stemLine.EndPoint   = new Point3d(0, midY, 0);
                }
                else
                {
                    var stem = new Line(new Point3d(0, barsSpanLocal / 2.0, 0), new Point3d(0, midY, 0))
                    {
                        Layer      = LayerManager.LeaderLayer,
                        LineWeight = LineWeight.LineWeight018,
                        Linetype   = "Continuous"
                    };
                    btrWriteStem.AppendEntity(stem);
                    tr.AddNewlyCreatedDBObject(stem, true);
                }
            }

            if (yHoriz)
            {
                double midX        = !double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : bar.BarsSpan / 2.0 + ArmLength;
                double barsSpanLoc = bar.BarsSpan;
                Line stemLine = null;
                foreach (ObjectId oid in btr)
                {
                    if (oid.IsErased) continue;
                    var obj = tr.GetObject(oid, OpenMode.ForRead);
                    if (obj is Line ln
                        && Math.Abs(ln.StartPoint.Y) < 1.0      // pozioma
                        && Math.Abs(ln.EndPoint.Y)   < 1.0
                        && Math.Abs(ln.StartPoint.X - ln.EndPoint.X) > 1.0   // nie punkt
                        && !(Math.Abs(ln.StartPoint.X) < 5.0
                             && Math.Abs(ln.EndPoint.X - barsSpanLoc) < 5.0) // wykluczamy dist line (0→barsSpan)
                        && !(Math.Abs(ln.EndPoint.X) < 5.0
                             && Math.Abs(ln.StartPoint.X - barsSpanLoc) < 5.0) // wykluczamy dist line odwrotnie
                        && !(Math.Abs(ln.StartPoint.X - barsSpanLoc / 2.0) < 5.0
                             && Math.Abs(ln.EndPoint.X - barsSpanLoc / 2.0) < 5.0)) // wykluczamy linie symetryczne (nie stem)
                    {
                        stemLine = ln;
                        break;
                    }
                }

                var btrWriteStem2 = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
                if (stemLine != null)
                {
                    stemLine.UpgradeOpen();
                    stemLine.StartPoint = new Point3d(barsSpanLoc / 2.0, 0, 0);
                    stemLine.EndPoint   = new Point3d(midX,               0, 0);
                }
                else
                {
                    var stem = new Line(
                        new Point3d(barsSpanLoc / 2.0, 0, 0),
                        new Point3d(midX,               0, 0))
                    {
                        Layer      = LayerManager.LeaderLayer,
                        LineWeight = LineWeight.LineWeight018,
                        Linetype   = "Continuous"
                    };
                    btrWriteStem2.AppendEntity(stem);
                    tr.AddNewlyCreatedDBObject(stem, true);
                }
            }

            tr.Commit();

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
            {
                // Zachowaj całą geometrię leadera — użytkownik mógł ją zmieniać gripami
                updatedBar.ArmTotalLen      = existingAnnot.ArmTotalLen;
                updatedBar.LeaderHorizontal = existingAnnot.LeaderHorizontal;
                updatedBar.LeaderRight      = existingAnnot.LeaderRight;
                updatedBar.LeaderUp         = existingAnnot.LeaderUp;
                updatedBar.ArmMidY          = existingAnnot.ArmMidY;
                updatedBar.TextLen          = existingAnnot.TextLen;
            }

            var btr = (BlockTableRecord)tr.GetObject(annotBr.BlockTableRecord, OpenMode.ForWrite);

            // Wymazanie calej zawartosci BTR
            var ids = new List<ObjectId>();
            foreach (ObjectId oid in btr)
                if (!oid.IsErased) ids.Add(oid);
            foreach (var oid in ids)
                ((DBObject)tr.GetObject(oid, OpenMode.ForWrite)).Erase();

            // Przebudowa — BuildH/V remierzy tekst i ustawia TextLen
            string ltName      = ResolveLinetype(db, tr, "_DOT", "CENTER");
            double armTotalLen;

            if (updatedBar.Direction == "X")
                armTotalLen = BuildHorizontal(tr, btr, db, updatedBar, ltName,
                    updatedBar.LeaderHorizontal, updatedBar.LeaderRight, updatedBar.LeaderUp);
            else
                armTotalLen = BuildVertical(tr, btr, db, updatedBar, ltName,
                    leaderVertical: updatedBar.LeaderHorizontal,
                    leaderRight:    updatedBar.LeaderRight,
                    leaderUp:       updatedBar.LeaderUp);

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
        // [14]ArmMidY [15]LeaderUp [16]SourceBlockHandle
        // ----------------------------------------------------------------

        internal static void WriteAnnotXData(Entity entity, BarData bar)
        {
            // Jeśli bar.SourceBlockHandle jest pusty (stary format annotacji bez tego slotu),
            // zachowaj istniejącą wartość z XData zamiast nadpisywać pustym stringiem.
            string sourceHandle = bar.SourceBlockHandle;
            if (string.IsNullOrEmpty(sourceHandle))
            {
                var existing = entity.GetXDataForApplication(AnnotAppName);
                if (existing != null)
                {
                    var ev = existing.AsArray();
                    if (ev.Length >= 17)
                        sourceHandle = ev[16].Value?.ToString() ?? "";
                }
            }

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
                new TypedValue((int)DxfCode.ExtendedDataReal,        !double.IsNaN(bar.ArmMidY) ? bar.ArmMidY : bar.BarsSpan / 2.0),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)(bar.LeaderUp ? 1 : 0)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, sourceHandle)
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
            if (v.Length >= 15) bd.ArmMidY   = (double)v[14].Value;
            if (v.Length >= 16) bd.LeaderUp           = (short)v[15].Value == 1;
            if (v.Length >= 17) bd.SourceBlockHandle  = v[16].Value?.ToString() ?? "";
            return bd;
        }

        public static bool IsAnnotation(Entity entity)
            => entity.GetXDataForApplication(AnnotAppName) != null;

        // ----------------------------------------------------------------
        // UpdateBarLabelCount — suma prętów ze wszystkich rozkładów powiązanych z prętem
        // ----------------------------------------------------------------

        /// <summary>
        /// Aktualizuje tekst MLeadera pręta na podstawie sumy prętów ze wszystkich powiązanych rozkładów.
        /// </summary>
        public static void UpdateBarLabelCount(Database db, string sourceBarHandle,
                                               string markOverride = null)
        {
            if (string.IsNullOrEmpty(sourceBarHandle)) return;

            try
            {
                // Krok 1 — znajdź polilnię pręta
                if (!long.TryParse(sourceBarHandle,
                        System.Globalization.NumberStyles.HexNumber,
                        null, out long srcHVal)) return;
                var srcHandle = new Handle(srcHVal);
                if (!db.TryGetObjectId(srcHandle, out ObjectId srcId) || srcId.IsErased) return;

                using var tr = db.TransactionManager.StartTransaction();

                var srcPline = tr.GetObject(srcId, OpenMode.ForRead)
                    as Teigha.DatabaseServices.Polyline;
                var srcBar = srcPline != null ? SingleBarEngine.ReadBarXData(srcPline) : null;
                if (srcBar == null || string.IsNullOrEmpty(srcBar.LabelHandle))
                { tr.Commit(); return; }

                // Krok 2 — zsumuj pręty ze wszystkich rozkładów powiązanych z tym prętem
                int totalCount = 0;
                var modelSpace = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

                foreach (ObjectId oid in modelSpace)
                {
                    if (oid.IsErased) continue;
                    var br2 = tr.GetObject(oid, OpenMode.ForRead) as BlockReference;
                    if (br2 == null) continue;
                    var barBlock = BarBlockEngine.ReadXData(br2);
                    if (barBlock == null) continue;
                    if (string.Equals(barBlock.SourceBarHandle, sourceBarHandle,
                            StringComparison.OrdinalIgnoreCase))
                        totalCount += barBlock.Count;
                }

                // Krok 3 — zaktualizuj MLeadera
                if (!long.TryParse(srcBar.LabelHandle,
                        System.Globalization.NumberStyles.HexNumber,
                        null, out long lblHVal))
                { tr.Commit(); return; }
                var lblHandle = new Handle(lblHVal);
                if (!db.TryGetObjectId(lblHandle, out ObjectId lblId) || lblId.IsErased)
                { tr.Commit(); return; }

                var ml = tr.GetObject(lblId, OpenMode.ForWrite) as MLeader;
                if (ml?.ContentType == ContentType.MTextContent)
                {
                    var mt = ml.MText?.Clone() as MText;
                    if (mt != null)
                    {
                        int spaceIdx = srcBar.Mark.IndexOf(' ');
                        string barBase = !string.IsNullOrEmpty(markOverride)
                            ? markOverride
                            : (spaceIdx >= 0 ? srcBar.Mark.Substring(0, spaceIdx) : srcBar.Mark);
                        string sfx = spaceIdx >= 0
                            ? " " + srcBar.Mark.Substring(spaceIdx + 1) : "";

                        if (totalCount == 0)
                        {
                            // Brak rozkładów — pokaż sam Mark bez liczby
                            mt.Contents = $"{barBase}{sfx}";
                        }
                        else
                        {
                            mt.Contents = $"{totalCount} {barBase}{sfx}";
                        }
                        ml.MText = mt;
                    }
                }

                tr.Commit();
            }
            catch { }
        }

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
