using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
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
            bar.Cover    = cover;

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
            string barLayer  = LayerManager.GetLayerName(bar.LayerCode);
            var    lw        = DiameterToLineWeight(bar.Diameter);
            var    cat       = ResolveSymbolCat(bar);
            var    visibleSet = GetVisibleIndices(bar.VisibilityMode, bar.VisibleIndices, count);

            for (int i = 0; i < count; i++)
            {
                if (!visibleSet.Contains(i)) continue;
                double y   = i * bar.Spacing;
                var    ptS = new Point3d(0, y, 0);
                var    ptE = new Point3d(barWidth, y, 0);
                var    line = new Line(ptS, ptE) { Layer = barLayer, LineWeight = lw };
                btr.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
                AddBarSymbols(tr, btr, barLayer, cat, ptS, ptE,
                              bar.SymbolSide, bar.SymbolDirection);
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
            string barLayer   = LayerManager.GetLayerName(bar.LayerCode);
            var    lw         = DiameterToLineWeight(bar.Diameter);
            var    cat        = ResolveSymbolCat(bar);
            var    visibleSet = GetVisibleIndices(bar.VisibilityMode, bar.VisibleIndices, count);

            for (int i = 0; i < count; i++)
            {
                if (!visibleSet.Contains(i)) continue;
                double x   = i * bar.Spacing;
                var    ptS = new Point3d(x, 0, 0);
                var    ptE = new Point3d(x, barHeight, 0);
                var    line = new Line(ptS, ptE) { Layer = barLayer, LineWeight = lw };
                btr.AppendEntity(line);
                tr.AddNewlyCreatedDBObject(line, true);
                AddBarSymbols(tr, btr, barLayer, cat, ptS, ptE,
                              bar.SymbolSide, bar.SymbolDirection);
            }
        }

        // ----------------------------------------------------------------
        // BuildAny — tryb ViewingDirection="Any": pełny kształt pręta + 1 kółko.
        // Geometria od Origin (lokalny układ BTR), kierunek (1,0,0).
        // bar.Count/Spacing są poprawne w XData (dla annotacji i zestawienia),
        // ale wizualnie rysowany jest 1 egzemplarz pręta.
        // ----------------------------------------------------------------
        private static void BuildAny(
            Transaction tr, BlockTableRecord btr,
            Database db, BarData bar)
        {
            string barLayer = LayerManager.GetLayerName(bar.LayerCode);

            // 1. Geometria pręta (polilinia kształtu) od Origin
            var shape    = ShapeCodeLibrary.Get(bar.ShapeCode) ?? ShapeCodeLibrary.Get("00");
            var entities = SingleBarEngine.BuildVisualEntities(
                shape, bar.ParamValues, bar.Diameter,
                startPoint: Point3d.Origin,
                direction:  new Vector3d(1, 0, 0));

            foreach (var ent in entities)
            {
                ent.Layer      = barLayer;
                ent.ColorIndex = 7; // biały (jak RC_SINGLE_BAR)
                btr.AppendEntity(ent);
                tr.AddNewlyCreatedDBObject(ent, true);
            }

            // 2. Jedno kółko w środku pierwszego segmentu polilinii
            var pline  = entities.OfType<Polyline>().FirstOrDefault();
            Point3d dotPos = Point3d.Origin;
            if (pline != null && pline.NumberOfVertices >= 2)
            {
                var p0 = pline.GetPoint3dAt(0);
                var p1 = pline.GetPoint3dAt(1);
                dotPos = new Point3d((p0.X + p1.X) / 2.0, (p0.Y + p1.Y) / 2.0, 0);
            }
            var circle = new Circle(dotPos, Vector3d.ZAxis, AnnotationEngine.DotRadius)
            {
                Layer      = barLayer,
                ColorIndex = 7
            };
            btr.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);
        }

        public static HashSet<int> GetVisibleIndicesPublic(BarVisibilityMode mode, string customIndices, int count)
            => GetVisibleIndices(mode, customIndices, count);

        private static HashSet<int> GetVisibleIndices(BarVisibilityMode mode, string customIndices, int count)
        {
            switch (mode)
            {
                case BarVisibilityMode.MiddleOnly:
                    return new HashSet<int> { count / 2 };
                case BarVisibilityMode.FirstLast:
                    var fl = new HashSet<int> { 0 };
                    if (count > 1) fl.Add(count - 1);
                    return fl;
                case BarVisibilityMode.Manual:
                    var manual = new HashSet<int>();
                    foreach (var s in (customIndices ?? "").Split(','))
                        if (int.TryParse(s.Trim(), out int idx) && idx >= 0 && idx < count)
                            manual.Add(idx);
                    return manual.Count > 0 ? manual : new HashSet<int> { 0 };
                default: // All
                    var all = new HashSet<int>();
                    for (int i = 0; i < count; i++) all.Add(i);
                    return all;
            }
        }

        // ── Symbol category lookup ─────────────────────────────────────────

        private static readonly HashSet<string> _linkCodes =
            new HashSet<string> { "51","63","34","35","36","41","46","47" };
        private static readonly HashSet<string> _ubarCodes =
            new HashSet<string> { "21","13","12","22","23","24","25","26" };
        private static readonly HashSet<string> _lbarCodes =
            new HashSet<string> { "11","14","15","27","28","29","31" };

        public enum BarSymbolCategory { Straight, Link, UBar, LBar }

        private static BarSymbolCategory GetSymbolCat(string code) =>
            code != null && _linkCodes.Contains(code) ? BarSymbolCategory.Link  :
            code != null && _ubarCodes.Contains(code) ? BarSymbolCategory.UBar  :
            code != null && _lbarCodes.Contains(code) ? BarSymbolCategory.LBar  :
            BarSymbolCategory.Straight;

        /// <summary>Zwraca kategorię symbolu pręta w rzucie z góry.</summary>
        public static BarSymbolCategory GetSymbolCategory(string shapeCode)
            => GetSymbolCat(shapeCode);

        /// <summary>
        /// Zwraca kategorię symbolu z uwzględnieniem pola SymbolType z XData.
        /// "None"   → Straight, "Circle" → UBar, "Hook" → LBar, "Auto" → z kodu kształtu.
        /// </summary>
        private static BarSymbolCategory ResolveSymbolCat(BarData bar)
        {
            switch (bar.SymbolType)
            {
                case "None":   return BarSymbolCategory.Straight;
                case "Circle": return BarSymbolCategory.UBar;
                case "Hook":   return BarSymbolCategory.LBar;
                default:       return GetSymbolCat(bar.ShapeCode);
            }
        }

        /// <summary>
        /// Dodaje symbol na końcach linii preta w rzucie z góry.
        ///   Link     → okrąg r=35 na OBU końcach
        ///   UBar     → okrąg r=35; symbolSide: Left=startPt / Right=endPt / Both=oba
        ///   LBar     → linia 45° dl.100mm; symbolSide: Left/Right; symbolDir: Up/Down
        ///   Straight → brak symbolu
        /// </summary>
        private static void AddBarSymbols(
            Transaction tr, BlockTableRecord btr, string layer,
            BarSymbolCategory cat, Point3d startPt, Point3d endPt,
            string symbolSide, string symbolDir)
        {
            const double SymR    = 35.0;
            const double HookLen = 100.0;
            const double Cos45   = 0.7071067811865476;

            switch (cat)
            {
                case BarSymbolCategory.Link:
                    AppendCircle(tr, btr, layer, startPt, SymR);
                    AppendCircle(tr, btr, layer, endPt,   SymR);
                    break;

                case BarSymbolCategory.UBar:
                {
                    string side = string.IsNullOrEmpty(symbolSide) ? "Right" : symbolSide;
                    if (side == "Left"  || side == "Both") AppendCircle(tr, btr, layer, startPt, SymR);
                    if (side == "Right" || side == "Both") AppendCircle(tr, btr, layer, endPt,   SymR);
                    break;
                }

                case BarSymbolCategory.LBar:
                {
                    // Jednostkowy kierunek linii (startPt → endPt)
                    double dx  = endPt.X - startPt.X;
                    double dy  = endPt.Y - startPt.Y;
                    double len = Math.Sqrt(dx * dx + dy * dy);
                    if (len < 1e-9) break;
                    double ux = dx / len, uy = dy / len;

                    // Kierunek prostopadły: Up = CCW 90° = (-uy, ux);  Down = CW 90° = (uy, -ux)
                    bool   isUp = string.IsNullOrEmpty(symbolDir) || symbolDir != "Down";
                    double px   = isUp ? -uy :  uy;
                    double py   = isUp ?  ux : -ux;

                    // Baza haka i składowe kierunkowe zależne od strony
                    bool    isLeft = !string.IsNullOrEmpty(symbolSide) && symbolSide == "Left";
                    Point3d hookBase;
                    double  bx, by;
                    if (isLeft)
                    {
                        hookBase = startPt;
                        // Przy lewym końcu: hak biegnie WZDŁUŻ (do przodu) + prostopadle → kąt ostry
                        bx = ux * Cos45 + px * Cos45;
                        by = uy * Cos45 + py * Cos45;
                    }
                    else   // Right (domyślnie)
                    {
                        hookBase = endPt;
                        // Przy prawym końcu: hak COFA SIĘ (wstecz) + prostopadle → kąt ostry
                        bx = -ux * Cos45 + px * Cos45;
                        by = -uy * Cos45 + py * Cos45;
                    }

                    var hookTip  = new Point3d(hookBase.X + HookLen * bx,
                                               hookBase.Y + HookLen * by, 0);

                    try
                    {
                        var dbgEd = Bricscad.ApplicationServices.Application
                            .DocumentManager.MdiActiveDocument?.Editor;
                        dbgEd?.WriteMessage(
                            $"\n[DEBUG L-BAR] startPt=({startPt.X:F1},{startPt.Y:F1})" +
                            $" endPt=({endPt.X:F1},{endPt.Y:F1})" +
                            $" lineDirection=({dx:F3},{dy:F3})\n");
                        dbgEd?.WriteMessage(
                            $"[DEBUG L-BAR] hookBasePt=({hookBase.X:F1},{hookBase.Y:F1})" +
                            $" hookEndPt=({hookTip.X:F1},{hookTip.Y:F1})\n");
                    }
                    catch { }

                    var hookLine = new Line(hookBase, hookTip) { Layer = layer };
                    btr.AppendEntity(hookLine);
                    tr.AddNewlyCreatedDBObject(hookLine, true);
                    break;
                }

                // Straight: brak symbolu
            }
        }

        private static void AppendCircle(Transaction tr, BlockTableRecord btr,
            string layer, Point3d center, double radius)
        {
            var circle = new Circle(center, Vector3d.ZAxis, radius) { Layer = layer };
            btr.AppendEntity(circle);
            tr.AddNewlyCreatedDBObject(circle, true);
        }

        // ----------------------------------------------------------------
        // GenerateFromBounds — jak Generate, ale bez polilinii.
        // x0,y0,x1,y1 to juz granice po otulinie (obliczone przez wywolujacego).
        // ----------------------------------------------------------------

        public static BarBlockResult GenerateFromBounds(
            Database db,
            double   x0, double y0,
            double   x1, double y1,
            BarData  bar,
            bool     horizontal,
            int      posNr,
            int?     overrideCount   = null,
            double?  overrideSpacing = null)
        {
            var empty = new BarBlockResult();
            if (x0 >= x1 || y0 >= y1) return empty;

            EnsureAppIdRegistered(db);
            LayerManager.EnsureLayersExist(db);

            double barLength, rawSpan;
            if (horizontal) { barLength = x1 - x0; rawSpan = y1 - y0; }
            else             { barLength = y1 - y0; rawSpan = x1 - x0; }

            if (overrideCount.HasValue)
            {
                // Tryb Any / override z zewnątrz — użyj podanych wartości bezpośrednio
                bar.Count   = overrideCount.Value;
                bar.Spacing = overrideSpacing ?? bar.Spacing;
                bar.BarsSpan = (bar.Count - 1) * bar.Spacing;
            }
            else if (bar.Count > 1)
            {
                // Zachowaj count/spacing przekazane z zewnątrz
                bar.BarsSpan = (bar.Count - 1) * bar.Spacing;
            }
            else
            {
                bar.Count    = Math.Max(1, (int)(rawSpan / bar.Spacing) + 1);
                bar.BarsSpan = (bar.Count - 1) * bar.Spacing;
            }

            bar.LengthA  = barLength;

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

            if (bar.ViewingDirection == "Any")
                BuildAny(tr, btr, db, bar);
            else if (horizontal)
                BuildHorizontal(tr, btr, bar, barLength, bar.Count);
            else
                BuildVertical(tr, btr, bar, barLength, bar.Count);

            // Dla ukośnych prętów: insertPt = Pt1X/Pt1Y (krawędź kliknięta przez użytkownika po cover)
            // Dla prostych (angle≈0): zachowaj stary lewy-dolny róg AABB
            var insertPt = Math.Abs(bar.Angle) > 1e-6
                ? new Point3d(bar.Pt1X, bar.Pt1Y, 0)
                : new Point3d(x0, y0, 0);
            var blockRef = new BlockReference(insertPt, btrId) { Layer = "0" };
            if (Math.Abs(bar.Angle) > 1e-6)
                blockRef.Rotation = bar.Angle;
            space.AppendEntity(blockRef);
            tr.AddNewlyCreatedDBObject(blockRef, true);

            WriteXData(blockRef, bar);

            // MinPoint/MaxPoint z GeometricExtents uwzględnia obrót bloku
            Extents3d ext;
            try   { ext = blockRef.GeometricExtents; }
            catch { ext = new Extents3d(new Point3d(x0, y0, 0), new Point3d(x1, y1, 0)); }

            tr.Commit();

            return new BarBlockResult
            {
                BlockRefId = blockRef.ObjectId,
                MinPoint   = ext.MinPoint,
                MaxPoint   = ext.MaxPoint
            };
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
        // RebuildVisibility — zmienia VisibilityMode/VisibleIndices i przebudowuje BTR.
        // Wywoływane przez RC_EDIT_LABEL po zmianie bar visibility.
        // ----------------------------------------------------------------
        public static void RebuildVisibility(
            Database db, ObjectId blockRefId,
            BarVisibilityMode mode, string visibleIndices)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var br = tr.GetObject(blockRefId, OpenMode.ForWrite) as BlockReference;
            if (br == null) { tr.Commit(); return; }

            var bar = ReadXData(br);
            if (bar == null) { tr.Commit(); return; }

            bar.VisibilityMode = mode;
            bar.VisibleIndices = visibleIndices ?? "";
            WriteXData(br, bar);

            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
            EraseAllInBtr(tr, btr);

            if (bar.Direction == "X") BuildHorizontal(tr, btr, bar, bar.LengthA, bar.Count);
            else                      BuildVertical  (tr, btr, bar, bar.LengthA, bar.Count);

            tr.Commit();
        }

        // ----------------------------------------------------------------
        // RebuildBarEndStyle — zmienia SymbolType/SymbolSide/SymbolDirection i przebudowuje BTR.
        // Wywoływane przez RC_BAR_END.
        // ----------------------------------------------------------------
        public static void RebuildBarEndStyle(
            Database db, ObjectId blockRefId,
            string symType, string symSide, string symDir)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var br = tr.GetObject(blockRefId, OpenMode.ForWrite) as BlockReference;
            if (br == null) { tr.Commit(); return; }

            var bar = ReadXData(br);
            if (bar == null) { tr.Commit(); return; }

            bar.SymbolType      = symType;
            bar.SymbolSide      = symSide;
            bar.SymbolDirection = symDir;
            WriteXData(br, bar);

            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
            EraseAllInBtr(tr, btr);

            if (bar.Direction == "X") BuildHorizontal(tr, btr, bar, bar.LengthA, bar.Count);
            else                      BuildVertical  (tr, btr, bar, bar.LengthA, bar.Count);

            tr.Commit();
        }

        // ----------------------------------------------------------------
        // RebuildWithNewViewLength — zmienia długość prętów (viewLength) i przebudowuje BTR.
        // Wywoływane przez RC_EDIT_DISTRIBUTION po zmianie Viewing Direction.
        // ----------------------------------------------------------------
        public static void RebuildWithNewViewLength(
            Database db, ObjectId blockRefId,
            double newBarLength, string viewingDirection, int viewSegmentIndex)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var br  = tr.GetObject(blockRefId, OpenMode.ForWrite) as BlockReference;
            if (br == null) { tr.Commit(); return; }

            var bar = ReadXData(br);
            if (bar == null) { tr.Commit(); return; }

            bar.LengthA          = newBarLength;
            bar.ViewingDirection = viewingDirection ?? "Auto";
            bar.ViewSegmentIndex = viewSegmentIndex;
            WriteXData(br, bar);

            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
            EraseAllInBtr(tr, btr);

            if (bar.Direction == "X") BuildHorizontal(tr, btr, bar, bar.LengthA, bar.Count);
            else                      BuildVertical  (tr, btr, bar, bar.LengthA, bar.Count);

            tr.Commit();
        }

        // ----------------------------------------------------------------
        // RebuildWithNewLayout — zmienia Count/Spacing/Cover i przebudowuje BTR.
        // Wywoływane przez RC_EDIT_DISTRIBUTION (EditDistributionDialog).
        // ----------------------------------------------------------------
        public static void RebuildWithNewLayout(
            Database db, ObjectId blockRefId,
            int newCount, double newSpacing, double newCover,
            double? newLengthA = null,
            string newViewingDir = null, int newViewSegIdx = -1)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var br = tr.GetObject(blockRefId, OpenMode.ForWrite) as BlockReference;
            if (br == null) { tr.Commit(); return; }

            var bar = ReadXData(br);
            if (bar == null) { tr.Commit(); return; }

            bar.Count    = newCount;
            bar.Spacing  = newSpacing;
            bar.Cover    = newCover;
            if (newLengthA.HasValue && newLengthA.Value > 0)
                bar.LengthA = newLengthA.Value;
            if (newViewingDir != null)
                bar.ViewingDirection = newViewingDir;
            if (newViewSegIdx >= 0)
                bar.ViewSegmentIndex = newViewSegIdx;
            bar.BarsSpan = (newCount - 1) * newSpacing;

            // Sync VisibilityMode z ViewingDirection (przed WriteXData)
            if (bar.ViewingDirection == "Any")
            {
                bar.VisibilityMode = BarVisibilityMode.Manual;
                bar.VisibleIndices = "0";
            }
            else if (bar.ViewingDirection == "Auto" || bar.ViewingDirection == "Manual")
            {
                // Wróć do All jeśli poprzednio był Any
                if (bar.VisibilityMode == BarVisibilityMode.Manual
                    && bar.VisibleIndices == "0")
                {
                    bar.VisibilityMode = BarVisibilityMode.All;
                    bar.VisibleIndices = "";
                }
            }

            WriteXData(br, bar);

            var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
            EraseAllInBtr(tr, btr);

            if (bar.Direction == "X") BuildHorizontal(tr, btr, bar, bar.LengthA, newCount);
            else                      BuildVertical  (tr, btr, bar, bar.LengthA, newCount);

            tr.Commit();
        }

        // ----------------------------------------------------------------
        // Pozycje gripow
        // ----------------------------------------------------------------

        /// <summary>
        /// Grip [0] — przesuniiety o otulinie PRZED pierwszym pretem.
        /// Gdy uzytkownik dociagnie grip do krawedzi plyty, pierwszy pret
        /// pozostaje w odleglosci cover od tej krawedzi (jak w ASD).
        /// </summary>
        public static Point3d GripLateral(BlockReference br, BarData bar)
        {
            var ins = br.Position;
            double c = bar.Cover;
            // X-bars: prety rozpinaja sie w Y, otulina w kierunku -Y (dol ukladu)
            // Y-bars: prety rozpinaja sie w X, otulina w kierunku -X (lewy bok)
            return bar.Direction == "X"
                ? new Point3d(ins.X, ins.Y - c, 0)
                : new Point3d(ins.X - c, ins.Y, 0);
        }

        public static Point3d GripSpan(BlockReference br, BarData bar)
        {
            var ins = br.Position;
            double c = bar.Cover;
            // Grip na KRAWEDZI PLYTY za ostatnim pretem (ostatni pret + cover)
            return bar.Direction == "X"
                ? new Point3d(ins.X, ins.Y + bar.BarsSpan + c, 0)
                : new Point3d(ins.X + bar.BarsSpan + c, ins.Y, 0);
        }

        // ----------------------------------------------------------------
        // XData: [0]AppName [1]Mark [2]LayerCode [3]Count [4]Diameter
        //        [5]Spacing [6]Direction [7]Position [8]LengthA [9]BarsSpan
        //        [10]Cover  [11]AnnotHandle (hex string bloku RC_ANNOT)
        //        [12]ShapeCode  [13]SymbolSide  [14]SymbolDirection
        //        [15]ViewingDirection  [16]ViewSegmentIndex  [17]SymbolType  [18]SourceBarHandle
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
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.Cover),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.AnnotHandle     ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.ShapeCode       ?? "00"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.SymbolSide        ?? "Right"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.SymbolDirection   ?? "Up"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.ViewingDirection  ?? "Auto"),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)bar.ViewSegmentIndex),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.SymbolType        ?? "Auto"), // [17]
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.SourceBarHandle   ?? ""),    // [18]
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.LabelPolyHandle   ?? ""),    // [19]
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.LabelTextHandle   ?? ""),    // [20]
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)bar.VisibilityMode),      // [21]
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.VisibleIndices    ?? ""),    // [22]
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.Angle)                      // [23]
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
            if (v.Length >= 10) bd.BarsSpan    = (double)v[9].Value;
            if (v.Length >= 11) bd.Cover       = (double)v[10].Value;
            if (v.Length >= 12) bd.AnnotHandle = (string)v[11].Value;
            if (v.Length >= 13) bd.ShapeCode        = (string)v[12].Value;
            if (v.Length >= 14) bd.SymbolSide       = (string)v[13].Value;
            if (v.Length >= 15) bd.SymbolDirection  = (string)v[14].Value;
            if (v.Length >= 16) bd.ViewingDirection = (string)v[15].Value;
            if (v.Length >= 17) bd.ViewSegmentIndex = (short)v[16].Value;
            if (v.Length >= 18) bd.SymbolType       = (string)v[17].Value;
            if (v.Length >= 19) bd.SourceBarHandle  = (string)v[18].Value;
            if (v.Length >= 20) bd.LabelPolyHandle  = (string)v[19].Value;
            if (v.Length >= 21) bd.LabelTextHandle  = (string)v[20].Value;
            if (v.Length >= 22) bd.VisibilityMode   = (BarVisibilityMode)(short)v[21].Value;
            if (v.Length >= 23) bd.VisibleIndices   = (string)v[22].Value ?? "";
            if (v.Length >= 24) bd.Angle            = (double)v[23].Value;
            return bd;
        }

        /// <summary>
        /// Zapisuje handle annotacji w XData bloku pretow — wywolywane po CreateLeader.
        /// Umozliwia SyncAnnotation znalezienie WLASCIWEJ annotacji bez szukania po Mark.
        /// </summary>
        public static void LinkAnnotation(Database db, ObjectId barBlockId, ObjectId annotId)
        {
            if (barBlockId.IsNull || annotId.IsNull) return;
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var br = tr.GetObject(barBlockId, OpenMode.ForWrite) as BlockReference;
                if (br == null) { tr.Commit(); return; }
                var bar = ReadXData(br);
                if (bar == null) { tr.Commit(); return; }
                bar.AnnotHandle = annotId.Handle.Value.ToString("X8");
                WriteXData(br, bar);

                // Referencja wsteczna: RC_BAR_ANNOT zna swój RC_BAR_BLOCK
                var annotBr = tr.GetObject(annotId, OpenMode.ForWrite) as BlockReference;
                if (annotBr != null)
                {
                    var annotBar = AnnotationEngine.ReadAnnotXData(annotBr);
                    if (annotBar != null)
                    {
                        annotBar.SourceBlockHandle = barBlockId.Handle.Value.ToString("X8");
                        AnnotationEngine.WriteAnnotXData(annotBr, annotBar);
                    }
                }

                tr.Commit();
            }
            catch { }
        }

        /// <summary>
        /// Zapisuje handle'e Polyline i DBText etykiety nowego stylu w XData rozkładu.
        /// Działa zarówno dla BlockReference (RC_SLAB_BARS) jak i Polyline (ViewingDirection="Any").
        /// </summary>
        public static void StoreLabelHandles(Database db, ObjectId barEntityId, ObjectId polyId, ObjectId textId)
        {
            if (barEntityId.IsNull) return;
            try
            {
                using var tr  = db.TransactionManager.StartTransaction();
                var ent = tr.GetObject(barEntityId, OpenMode.ForWrite) as Entity;
                if (ent == null) { tr.Commit(); return; }
                var bar = ReadXData(ent);
                if (bar == null) { tr.Commit(); return; }
                bar.LabelPolyHandle = polyId.IsNull ? "" : polyId.Handle.Value.ToString("X8");
                bar.LabelTextHandle = textId.IsNull ? "" : textId.Handle.Value.ToString("X8");
                WriteXData(ent, bar);
                tr.Commit();
            }
            catch { }
        }

        public static bool IsBarBlock(Entity entity)
            => entity.GetXDataForApplication(XAppName) != null;

        // ----------------------------------------------------------------
        // FindDistributionsByPosNr — szuka wszystkich RC_BAR_BLOCK dla danego posNr
        // ----------------------------------------------------------------

        public static List<ObjectId> FindDistributionsByPosNr(Database db, int posNr)
        {
            var result = new List<ObjectId>();
            if (posNr <= 0) return result;

            using var tr = db.TransactionManager.StartOpenCloseTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            foreach (ObjectId oid in space)
            {
                if (oid.IsErased) continue;
                if (!(tr.GetObject(oid, OpenMode.ForRead) is BlockReference br)) continue;
                var xd = ReadXData(br);
                if (xd == null) continue;
                if (SingleBarEngine.ExtractPosNr(xd.Mark) == posNr)
                    result.Add(oid);
            }
            return result;
        }

        // ----------------------------------------------------------------
        // UpdateBarLength — przebudowuje linie prętów w BTR z nową długością
        // ----------------------------------------------------------------

        public static bool UpdateBarLength(Database db, ObjectId blockRefId, double newLengthA)
        {
            if (blockRefId.IsNull || blockRefId.IsErased) return false;
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var br = tr.GetObject(blockRefId, OpenMode.ForWrite) as BlockReference;
                if (br == null) { tr.Commit(); return false; }

                var bar = ReadXData(br);
                if (bar == null) { tr.Commit(); return false; }

                bar.LengthA = newLengthA;
                WriteXData(br, bar);

                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
                EraseAllInBtr(tr, btr);

                if (bar.Direction == "X") BuildHorizontal(tr, btr, bar, newLengthA, bar.Count);
                else                      BuildVertical  (tr, btr, bar, newLengthA, bar.Count);

                tr.Commit();
                return true;
            }
            catch { return false; }
        }

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
