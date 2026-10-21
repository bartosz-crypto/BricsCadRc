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

        // Cache świeżych wartości skew podczas drag gripa — DB XData jest nieaktualne
        // gdy br jest klonem. RefreshOutlineFor czyta stąd zamiast z DB XData.
        private static readonly Dictionary<long, (double skewStart, double skewEnd)> _skewCache
            = new Dictionary<long, (double, double)>();

        public static void ClearSkewCache(long handleValue) => _skewCache.Remove(handleValue);

        public static (double skewStart, double skewEnd)? TryGetCachedSkew(long handleValue)
            => _skewCache.TryGetValue(handleValue, out var v) ? v : ((double, double)?)null;

        // ----------------------------------------------------------------
        // Wynik generowania
        // ----------------------------------------------------------------

        public struct BarBlockResult
        {
            public ObjectId BlockRefId;
            public Point3d  MinPoint;   // (x0, y0) po otulinie
            public Point3d  MaxPoint;   // (x1, y1) po otulinie
            public Point3d  BarOrigin;  // insertPt bloku = local(0,0) w WCS, niezależnie od dekoracji
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

            bar.Count    = count;
            bar.LengthA  = barLength;
            bar.BarsSpan = rawSpan;
            bar.Cover    = cover;

            // Uwaga: x0/y0 już zawierają otulinę (minX + cover). Wcześniej był tu dodatkowy
            // "x0 += cover" — pręty zaczynały się na 2×cover, a kończyły na krawędzi (0 otuliny).

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
                else blockName = $"RC_SLAB_BARS_{posNr:D3}_{Guid.NewGuid():N}";
            }

            var btr   = new BlockTableRecord { Name = blockName };
            var btrId = blockTable.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            BuildBars(tr, btr, bar, horizontal, barLength, count);

            var insertPt = new Point3d(x0, y0, 0);
            var blockRef = new BlockReference(insertPt, btrId) { Layer = "0" };
            blockRef.ColorIndex = 256;
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
        // Rozkład ANY (decyzja 2026-10-14, jak „Any” w ASD): w bloku JEDEN pręt — cały kształt pręta wzorcowego jako polilinia
        // wypełniona (szerokość = Ø); liczba prętów i rozstaw tylko w opisie i zestawieniu (Count = 1, BarsSpan = 0,
        // CountDisplay = liczba z okna). Kształt: BarGeometryBuilder.AnyAxis (najdłuższy odcinek na osi prętów od punktu wstawienia),
        // SymbolDirection „Down” — zagięcia po drugiej stronie, SymbolSide „Left” — zagięcie przy początku odcinka (UB SIDE, lustro wzdłuż).
        // ----------------------------------------------------------------
        public const string AnyMethod = "Any";

        public static bool IsAny(BarData bar)
            => bar != null && string.Equals(bar.ViewingDirection, AnyMethod, StringComparison.OrdinalIgnoreCase);

        private static void BuildBars(Transaction tr, BlockTableRecord btr, BarData bar, bool horizontal, double length, int count)
        {
            if (IsAny(bar)) BuildAny(tr, btr, bar, !horizontal);
            else if (horizontal) BuildHorizontal(tr, btr, bar, length, count);
            else BuildVertical(tr, btr, bar, length, count);
        }

        private static void BuildAny(Transaction tr, BlockTableRecord btr, BarData bar, bool alongY)
        {
            // wymiary A–E z pręta wzorcowego (XData rozkładu ma tylko A — po przebudowie, np. RC_SCALE_ANNOT / MIRROR, U-bar tracił B i C
            // i zostawało jedno ramię; 2026-10-14); brak pręta — wymiary rozkładu
            double[] dims = bar.ParamValues;
            string shape = bar.ShapeCode ?? "00";
            try
            {
                if (!string.IsNullOrEmpty(bar.SourceBarHandle))
                {
                    var sid = SingleBarEngine.HandleToObjectId(btr.Database, bar.SourceBarHandle);
                    if (!sid.IsNull && !sid.IsErased && tr.GetObject(sid, OpenMode.ForRead) is Entity se
                        && SingleBarEngine.ReadBarXData(se) is BarData sb && sb.LengthA > 0
                        && string.Equals(sb.ShapeCode ?? "00", shape, StringComparison.OrdinalIgnoreCase))
                        dims = sb.ParamValues;
                }
            }
            catch (System.Exception ex) { Log.Error("BarBlockEngine.BuildAny", ex); }
            var pts = BarGeometryBuilder.AnyAxis(shape, dims, bar.Diameter,
                                                 mirror: string.Equals(bar.SymbolDirection, "Down", StringComparison.OrdinalIgnoreCase), alongY: alongY,
                                                 reverse: string.Equals(bar.SymbolSide, "Left", StringComparison.OrdinalIgnoreCase));
            if (pts.Count < 2) return;
            // biały jak RC_SINGLE_BAR (warstwa B1 / B2 ma kolor — Bartek 2026-10-14); ADD — cyan (ColorizeAdd)
            var pl = new Polyline { Layer = LayerManager.GetLayerName(bar.LayerCode), ColorIndex = 7 };
            for (int i = 0; i < pts.Count; i++) pl.AddVertexAt(i, new Point2d(pts[i].X, pts[i].Y), 0, 0, 0);
            pl.ConstantWidth = Math.Max(1, bar.Diameter);
            if (pts.Count > 2 && Math.Abs(pts[0].X - pts[pts.Count - 1].X) < 1e-6 && Math.Abs(pts[0].Y - pts[pts.Count - 1].Y) < 1e-6)
                pl.Closed = true;   // pierścień
            btr.AppendEntity(pl);
            tr.AddNewlyCreatedDBObject(pl, true);
            ColorizeAdd(tr, btr, bar);
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
            var    cuts       = ParseCutZones(bar.CutZones);

            for (int i = 0; i < count; i++)
            {
                if (!visibleSet.Contains(i)) continue;
                double y        = i * bar.Spacing;
                double skewFrac = count > 1 ? (double)i / (count - 1) : 0.0;
                double xShift   = bar.SkewStart + skewFrac * (bar.SkewEnd - bar.SkewStart);
                var    ptS = new Point3d(xShift,            y, 0);
                var    ptE = new Point3d(barWidth + xShift, y, 0);
                // Kółko końca pręta mieści się W długości pręta: linia kończy się w jego środku (koniec − r)
                var (trimS, trimE) = CircleTrims(cat, bar.SymbolSide, bar.AnnotScale, barWidth);
                foreach (var (a0, a1) in BarPiecesAfterCuts(cuts, y, xShift + trimS, barWidth + xShift - trimE))
                {
                    var line = new Line(new Point3d(a0, y, 0), new Point3d(a1, y, 0))
                        { Layer = barLayer, ColorIndex = 256, LineWeight = lw };
                    btr.AppendEntity(line);
                    tr.AddNewlyCreatedDBObject(line, true);
                }
                AddBarSymbols(tr, btr, barLayer, cat, ptS, ptE,
                              bar.SymbolSide, bar.SymbolDirection, bar.AnnotScale);
            }
            AddPairBar(tr, btr, bar, barLayer, lw, cat, visibleSet, count, barWidth, alongX: true);
            ColorizeAdd(tr, btr, bar);
        }

        /// <summary>
        /// Strzemiona 4-cięte (decyzja użytkownika 2026-10-14): drugie (zachodzące) strzemię — pręt o jeden rozstaw dalej od pierwszego
        /// widocznego, przesunięty wzdłuż osi o <see cref="BarData.PairShift"/> (od drugiego lica belki), z symbolami końców jak widoczny.
        /// </summary>
        private static void AddPairBar(Transaction tr, BlockTableRecord btr, BarData bar, string barLayer, LineWeight lw,
                                       BarSymbolCategory cat, HashSet<int> visibleSet, int count, double length, bool alongX)
        {
            if (Math.Abs(bar.PairShift) < 1.0 || count < 2 || visibleSet.Count == 0) return;
            int j = visibleSet.Min() + 1;
            if (j >= count) j = visibleSet.Min() - 1;
            if (j < 0 || visibleSet.Contains(j)) return;
            double t = j * bar.Spacing;
            double a0 = bar.PairShift, a1 = bar.PairShift + length;
            Point3d P(double along) => alongX ? new Point3d(along, t, 0) : new Point3d(t, along, 0);
            var (trimS, trimE) = CircleTrims(cat, bar.SymbolSide, bar.AnnotScale, length);
            var line = new Line(P(a0 + trimS), P(a1 - trimE)) { Layer = barLayer, ColorIndex = 256, LineWeight = lw };
            btr.AppendEntity(line);
            tr.AddNewlyCreatedDBObject(line, true);
            AddBarSymbols(tr, btr, barLayer, cat, P(a0), P(a1), bar.SymbolSide, bar.SymbolDirection, bar.AnnotScale);
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
            var    cuts       = ParseCutZones(bar.CutZones);

            for (int i = 0; i < count; i++)
            {
                if (!visibleSet.Contains(i)) continue;
                double x        = i * bar.Spacing;
                double skewFrac = count > 1 ? (double)i / (count - 1) : 0.0;
                double yShift   = bar.SkewStart + skewFrac * (bar.SkewEnd - bar.SkewStart);
                var    ptS = new Point3d(x, yShift,             0);
                var    ptE = new Point3d(x, barHeight + yShift, 0);
                // Kółko końca pręta mieści się W długości pręta: linia kończy się w jego środku (koniec − r)
                var (trimS, trimE) = CircleTrims(cat, bar.SymbolSide, bar.AnnotScale, barHeight);
                foreach (var (a0, a1) in BarPiecesAfterCuts(cuts, x, yShift + trimS, barHeight + yShift - trimE))
                {
                    var line = new Line(new Point3d(x, a0, 0), new Point3d(x, a1, 0))
                        { Layer = barLayer, ColorIndex = 256, LineWeight = lw };
                    btr.AppendEntity(line);
                    tr.AddNewlyCreatedDBObject(line, true);
                }
                AddBarSymbols(tr, btr, barLayer, cat, ptS, ptE,
                              bar.SymbolSide, bar.SymbolDirection, bar.AnnotScale);
            }
            AddPairBar(tr, btr, bar, barLayer, lw, cat, visibleSet, count, barHeight, alongX: false);
            ColorizeAdd(tr, btr, bar);
        }

        // ----------------------------------------------------------------
        // Dozbrojenie (opis z „ADD”, np. „H10-08-200 B1 ADD”, „B+T ADD”) — pręty rozkładu w kolorze CYAN (4)
        // ----------------------------------------------------------------
        public const short AddColor = 4;

        public static bool IsAddMark(string mark) =>
            !string.IsNullOrEmpty(mark) && System.Text.RegularExpressions.Regex.IsMatch(mark, @"\bADD\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>Kolor prętów i symboli w bloku rozkładu: ADD → cyan, inaczej ByLayer (inne kolory ręczne zostają).</summary>
        private static void ColorizeAdd(Transaction tr, BlockTableRecord btr, BarData bar)
        {
            short want = IsAddMark(bar?.Mark) ? AddColor : (short)256;
            foreach (ObjectId id in btr)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e)) continue;
                if (e.ColorIndex == want || (e.ColorIndex != 256 && e.ColorIndex != AddColor)) continue;
                e.UpgradeOpen();
                e.ColorIndex = want;
            }
        }

        /// <summary>Wszystkie rozkłady w rysunku: ADD → cyan (np. starsze rysunki, ręczne opisy). Zwraca liczbę zmienionych.</summary>
        public static int RecolorAllAdd(Database db)
        {
            int n = 0;
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var ms = (BlockTableRecord)tr.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (id.IsErased || !(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                    var bar = ReadXData(br);
                    if (bar == null || !IsAddMark(bar.Mark)) continue;
                    var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                    bool any = false;
                    foreach (ObjectId cid in btr)
                        if (tr.GetObject(cid, OpenMode.ForRead) is Entity e && e.ColorIndex == 256) { any = true; break; }
                    if (!any) continue;
                    ColorizeAdd(tr, btr, bar);
                    n++;
                }
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("BarBlockEngine.RecolorAllAdd", ex); }
            return n;
        }

        /// <summary>Po zmianie opisu (np. dopisanie „ADD” w edycji etykiety) — przekolorowanie bloku rozkładu.</summary>
        private static void RecolorAdd(BlockReference br, BarData bar)
        {
            try
            {
                var tr = br.Database?.TransactionManager.TopTransaction;
                if (tr == null || br.BlockTableRecord.IsNull) return;
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                ColorizeAdd(tr, btr, bar);
            }
            catch (System.Exception ex) { Log.Error("BarBlockEngine.RecolorAdd", ex); }
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
        /// <summary>"s0,s1,a0,a1;..." → lista stref cięcia (układ lokalny bloku).</summary>
        public static List<(double s0, double s1, double a0, double a1)> ParseCutZones(string zones)
        {
            var list = new List<(double, double, double, double)>();
            if (string.IsNullOrWhiteSpace(zones)) return list;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            foreach (var part in zones.Split(';'))
            {
                var f = part.Split(',');
                if (f.Length != 4) continue;
                if (double.TryParse(f[0], System.Globalization.NumberStyles.Float, ci, out double s0)
                    && double.TryParse(f[1], System.Globalization.NumberStyles.Float, ci, out double s1)
                    && double.TryParse(f[2], System.Globalization.NumberStyles.Float, ci, out double a0)
                    && double.TryParse(f[3], System.Globalization.NumberStyles.Float, ci, out double a1))
                    list.Add((s0, s1, a0, a1));
            }
            return list;
        }

        public static string FormatCutZones(IEnumerable<(double s0, double s1, double a0, double a1)> zones)
        {
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            return string.Join(";", zones.Select(z =>
                string.Join(",", z.s0.ToString("0.###", ci), z.s1.ToString("0.###", ci),
                                 z.a0.ToString("0.###", ci), z.a1.ToString("0.###", ci))));
        }

        /// <summary>Czy pręt o położeniu poprzecznym s jest przecięty którąś strefą.</summary>
        public static bool IsBarCut(List<(double s0, double s1, double a0, double a1)> cuts, double s)
        {
            foreach (var z in cuts) if (s >= z.s0 - 1e-6 && s <= z.s1 + 1e-6) return true;
            return false;
        }

        /// <summary>Odcinki pręta [lo, hi] po odjęciu stref cięcia, które go dotyczą.</summary>
        public static List<(double, double)> BarPiecesAfterCuts(
            List<(double s0, double s1, double a0, double a1)> cuts, double s, double lo, double hi)
        {
            var pieces = new List<(double, double)> { (lo, hi) };
            foreach (var z in cuts)
            {
                if (s < z.s0 - 1e-6 || s > z.s1 + 1e-6) continue;
                var next = new List<(double, double)>();
                foreach (var (p0, p1) in pieces)
                {
                    if (z.a1 <= p0 || z.a0 >= p1) { next.Add((p0, p1)); continue; }
                    if (z.a0 - p0 > 1.0) next.Add((p0, z.a0));
                    if (p1 - z.a1 > 1.0) next.Add((z.a1, p1));
                }
                pieces = next;
            }
            return pieces;
        }

        /// <summary>Promień kółka końca pręta (w układzie bloku, ze skalą opisu).</summary>
        public static double EndCircleRadius(double annotScale) => Scaled(35.0, annotScale);

        /// <summary>
        /// Skrócenie linii pręta przy kółku (start, koniec): kółko leży w długości pręta (krawędź kółka = koniec pręta),
        /// linia dochodzi do jego środka — długość pręta na rzucie i gripy bez zmian, z kółkiem czy bez.
        /// </summary>
        private static (double start, double end) CircleTrims(BarSymbolCategory cat, string symbolSide, double annotScale, double length)
        {
            double r = EndCircleRadius(annotScale);
            if (length <= 4 * r) return (0, 0);   // bardzo krótki pręt — bez skracania
            switch (cat)
            {
                case BarSymbolCategory.Link: return (r, r);
                case BarSymbolCategory.UBar:
                {
                    string side = string.IsNullOrEmpty(symbolSide) ? "Right" : symbolSide;
                    return (side == "Left" || side == "Both" ? r : 0, side == "Right" || side == "Both" ? r : 0);
                }
                default: return (0, 0);
            }
        }

        private static void AddBarSymbols(
            Transaction tr, BlockTableRecord btr, string layer,
            BarSymbolCategory cat, Point3d startPt, Point3d endPt,
            string symbolSide, string symbolDir, double annotScale = 1.0)
        {
            double SymR    = Scaled(35.0,  annotScale);
            double HookLen = Scaled(100.0, annotScale);
            const double Cos45   = 0.7071067811865476;

            // Kółka: środek o promień do środka pręta (krawędź kółka = koniec pręta), jak w CircleTrims
            Point3d cS = startPt, cE = endPt;
            {
                double ddx = endPt.X - startPt.X, ddy = endPt.Y - startPt.Y, dl = Math.Sqrt(ddx * ddx + ddy * ddy);
                if (dl > 4 * SymR)
                {
                    var u = new Vector3d(ddx / dl, ddy / dl, 0);
                    cS = startPt + u * SymR;
                    cE = endPt - u * SymR;
                }
            }

            switch (cat)
            {
                case BarSymbolCategory.Link:
                    AppendCircle(tr, btr, layer, cS, SymR);
                    AppendCircle(tr, btr, layer, cE, SymR);
                    break;

                case BarSymbolCategory.UBar:
                {
                    string side = string.IsNullOrEmpty(symbolSide) ? "Right" : symbolSide;
                    if (side == "Left"  || side == "Both") AppendCircle(tr, btr, layer, cS, SymR);
                    if (side == "Right" || side == "Both") AppendCircle(tr, btr, layer, cE, SymR);
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

                    var hookLine = new Line(hookBase, hookTip) { Layer = layer, ColorIndex = 256 };
                    btr.AppendEntity(hookLine);
                    tr.AddNewlyCreatedDBObject(hookLine, true);
                    break;
                }

                // Straight: brak symbolu
            }
        }

        private static double Scaled(double baseValue, double annotScale)
            => baseValue * (annotScale > 0 ? annotScale : 1.0);

        private static void AppendCircle(Transaction tr, BlockTableRecord btr,
            string layer, Point3d center, double radius)
        {
            var circle = new Circle(center, Vector3d.ZAxis, radius) { Layer = layer, ColorIndex = 256 };
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
            double   maxSpacing = 0)
        {
            var empty = new BarBlockResult();
            // Zerowy zakres w kierunku rozkładu = 1 pręt (AutoRebar: wąski pas, "1 bar centered").
            // Wcześniej y0>=y1 odrzucało taki przypadek i pas znikał bez śladu.
            if (x0 > x1 || y0 > y1) return empty;

            EnsureAppIdRegistered(db);
            LayerManager.EnsureLayersExist(db);

            double barLength, rawSpan;
            if (horizontal) { barLength = x1 - x0; rawSpan = y1 - y0; }
            else             { barLength = y1 - y0; rawSpan = x1 - x0; }

            if (barLength <= 0) return empty;

            bar.BarsSpan = rawSpan;
            if (bar.Count <= 1)
                // p375 fix: FP safety. When bar.Spacing was computed as (availableSpan / N)
                // upstream (e.g., AutoRebarEngine ContinuousInternal mode), back-division
                // can give N - epsilon instead of N, causing truncation undershoot.
                // Tiny additive epsilon ensures integer conversion behaves correctly
                // without affecting genuine fractional ratios.
                bar.Count = Math.Max(1, (int)(rawSpan / bar.Spacing + 1e-9) + 1);
            // zasada Bartka 2026-10-22: rozstaw rzeczywisty siatki / UB NIGDY większy niż maxSpacing (205) — najwyżej mniejszy
            // (np. 3 pręty co 297,5 na 595 → 4 co 198,3); opis bez zmian (rozstaw nominalny)
            if (maxSpacing > 0 && bar.Count >= 2 && bar.Spacing > maxSpacing + 0.01 && rawSpan > 0)
            {
                int n = Math.Max(1, (int)Math.Ceiling(rawSpan / maxSpacing - 1e-9));
                bar.Count = n + 1;
                bar.Spacing = rawSpan / n;
            }

            // ANY (2026-10-14): A to wymiar kształtu pręta wzorcowego — zakres x0..x1 tylko wyznacza punkt wstawienia
            if (!IsAny(bar)) bar.LengthA = barLength;

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
                else blockName = $"RC_SLAB_BARS_{posNr:D3}_{Guid.NewGuid():N}";
            }

            var btr   = new BlockTableRecord { Name = blockName };
            var btrId = blockTable.Add(btr);
            tr.AddNewlyCreatedDBObject(btr, true);

            BuildBars(tr, btr, bar, horizontal, barLength, bar.Count);

            // Dla ukośnych prętów: insertPt = Pt1X/Pt1Y (krawędź kliknięta przez użytkownika po cover)
            // Dla prostych (angle≈0): zachowaj stary lewy-dolny róg AABB
            var insertPt = Math.Abs(bar.Angle) > 1e-6
                ? new Point3d(bar.Pt1X, bar.Pt1Y, 0)
                : new Point3d(x0, y0, 0);
            var blockRef = new BlockReference(insertPt, btrId) { Layer = "0" };
            blockRef.ColorIndex = 256;
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
                MaxPoint   = ext.MaxPoint,
                BarOrigin  = insertPt
            };
        }

        // ----------------------------------------------------------------
        // RegenerateBarBlock — wywolywany przez grip span
        // ----------------------------------------------------------------
        public static void RegenerateBarBlock(BlockReference br, double newBarsSpan,
                                               double? newSkewStart = null,
                                               double? newSkewEnd   = null)
        {
            var bar = ReadXData(br);
            if (bar == null || bar.Spacing <= 0) return;
            if (IsAny(bar)) return;   // ANY: jeden pręt w bloku — liczba w opisie (RC_EDIT_DISTRIBUTION), grip zakresu bez zmian

            newBarsSpan = Math.Max(0, newBarsSpan);
            // +1e-9: 2999.9999/200 ma dać 16 prętów, nie 15 (błąd zmiennoprzecinkowy po dragu)
            int newCount = bar.Spacing > 0
                ? (int)(newBarsSpan / bar.Spacing + 1e-9) + 1
                : 1;
            if (newCount < 1) newCount = 1;

            // belka: pręty boczne BS (E4b — w rzucie po jednym na bok, CountDisplay = liczba w rzucie × liczba na bok) i pręty
            // podłużne B+T (BM, K3 — w rzucie jedna warstwa, CountDisplay = 2 × liczba, opis „2x4”); po zmianie liczby gripem
            // krotność zostaje (pozostałe kody — jak dotąd: reset)
            int perDrawn = BarLabelRules.KeepsMultiplicity(bar.LayerCode) && bar.CountDisplay.HasValue && bar.Count > 0
                ? Math.Max(1, bar.CountDisplay.Value / bar.Count) : 1;

            bar.Count    = newCount;
            bar.BarsSpan = newBarsSpan;
            if (newSkewStart.HasValue) bar.SkewStart = newSkewStart.Value;
            if (newSkewEnd.HasValue)   bar.SkewEnd   = newSkewEnd.Value;

            bar.CountDisplay = perDrawn > 1 ? newCount * perDrawn : (int?)null;  // reset count override po stretch — EffectiveCount = canonical Count (BS: krotność)

            BarData.PromoteMarkIfPurePrefix(bar);

            // _skewCache NIE jest już aktualizowany: służył do podglądu, gdy drag zapisywał
            // do bazy klatka po klatce. Teraz zapis jest jeden (po puszczeniu gripa), XData jest
            // źródłem prawdy, a cache zostawał nieaktualny po Ctrl+Z (obrys skośny mimo cofnięcia).
            ClearSkewCache(br.BlockTableRecord.Handle.Value);

            // Zaktualizuj XData na blockref (jest juz otwarty w grip-op)
            WriteXData(br, bar);

            // Przebuduj BTR
            using var tr = br.Database.TransactionManager.StartTransaction();
            var btr = OwnBtrForWrite(tr, br);

            EraseAllInBtr(tr, btr);

            BuildBars(tr, btr, bar, bar.Direction == "X", bar.LengthA, newCount);

            tr.Commit();
        }

        // ----------------------------------------------------------------
        // ApplyMirror — po MIRROR (kopia znormalizowana do skali +1, uwaga Bartka 2026-10-13: gripy zostawały po starej stronie):
        // odbicie wzdłuż prętów — Flipped (gripy krawędzi / zakresu) i SymbolSide Left ↔ Right; odbicie w poprzek (kolejność prętów)
        // — SymbolDirection Up ↔ Down, SkewStart ↔ SkewEnd, widoczne indeksy i → n − 1 − i. BTR przebudowany. br — otwarty do zapisu.
        // ----------------------------------------------------------------
        public static void ApplyMirror(BlockReference br, bool flipAlong, bool flipAcross)
        {
            if (!flipAlong && !flipAcross) return;
            var bar = ReadXData(br);
            if (bar == null) return;
            if (flipAlong)
            {
                bar.Flipped = !bar.Flipped;
                bar.PairShift = -bar.PairShift;   // strzemiona 4-cięte: drugie przy drugim licu
                if (bar.SymbolSide == "Left") bar.SymbolSide = "Right";
                else if (bar.SymbolSide == "Right") bar.SymbolSide = "Left";
            }
            if (flipAcross)
            {
                bar.SymbolDirection = bar.SymbolDirection == "Down" ? "Up" : "Down";
                double s0 = bar.SkewStart; bar.SkewStart = bar.SkewEnd; bar.SkewEnd = s0;
                if (bar.Count > 1 && !string.IsNullOrWhiteSpace(bar.VisibleIndices))
                {
                    var idx = new List<string>();
                    foreach (var part in bar.VisibleIndices.Split(','))
                        idx.Add(int.TryParse(part.Trim(), out int i) ? (bar.Count - 1 - i).ToString(System.Globalization.CultureInfo.InvariantCulture)
                                                                     : part.Trim());
                    bar.VisibleIndices = string.Join(",", idx);
                }
            }
            ClearSkewCache(br.BlockTableRecord.Handle.Value);
            WriteXData(br, bar);
            using var tr = br.Database.TransactionManager.StartTransaction();
            var btr = OwnBtrForWrite(tr, br);
            EraseAllInBtr(tr, btr);
            BuildBars(tr, btr, bar, bar.Direction == "X", bar.LengthA, bar.Count);
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

            var btr = OwnBtrForWrite(tr, br);
            EraseAllInBtr(tr, btr);

            BuildBars(tr, btr, bar, bar.Direction == "X", bar.LengthA, bar.Count);

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

            var btr = OwnBtrForWrite(tr, br);
            EraseAllInBtr(tr, btr);

            BuildBars(tr, btr, bar, bar.Direction == "X", bar.LengthA, bar.Count);

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

            var btr = OwnBtrForWrite(tr, br);
            EraseAllInBtr(tr, btr);

            BuildBars(tr, btr, bar, bar.Direction == "X", bar.LengthA, bar.Count);

            tr.Commit();
        }

        // ----------------------------------------------------------------
        // RebuildWithNewLayout — zmienia Count/Spacing/Cover i przebudowuje BTR.
        // Wywoływane przez RC_EDIT_DISTRIBUTION (EditDistributionDialog).
        // ----------------------------------------------------------------
        public static void RebuildWithNewLayout(
            Database db, ObjectId blockRefId,
            int newCount, double newSpacing, double newCover,
            double? newBarsSpan = null,
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
            BarData.TryUpdateAutoMarkSpacing(bar, newSpacing);  // p331: aktualizuj spacing w Mark
            bar.Cover    = newCover;
            if (newLengthA.HasValue && newLengthA.Value > 0)
                bar.LengthA = newLengthA.Value;
            if (newViewingDir != null)
                bar.ViewingDirection = newViewingDir;
            if (newViewSegIdx >= 0)
                bar.ViewSegmentIndex = newViewSegIdx;
            if (newBarsSpan.HasValue)
                bar.BarsSpan = newBarsSpan.Value;
            double minSpan = (newCount - 1) * newSpacing;
            if (bar.BarsSpan < minSpan)
                bar.BarsSpan = minSpan;

            BarData.PromoteMarkIfPurePrefix(bar);

            WriteXData(br, bar);

            var btr = OwnBtrForWrite(tr, br);
            EraseAllInBtr(tr, btr);

            BuildBars(tr, btr, bar, bar.Direction == "X", bar.LengthA, newCount);

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
            double c = bar.Cover;
            Point3d local;
            if (bar.Direction == "X")
            {
                // non-flipped: edge at local X=0   → grip just before edge (X=-c), below first bar (Y=-c)
                // flipped:     edge at local X=LengthA → grip just past edge (X=LengthA+c), below first bar (Y=-c)
                // 2026-10-13: odbity (Flipped) — też z SkewStart (grip przy końcu pierwszego pręta, jak GripSpan z SkewEnd);
                // wcześniej po skosie gripem grip zostawał w starym miejscu
                local = bar.Flipped
                    ? new Point3d(bar.SkewStart + bar.LengthA + c, -c, 0)
                    : new Point3d(bar.SkewStart - c, -c, 0);
            }
            else
            {
                // non-flipped: edge at local Y=0   → grip left of first bar (X=-c), just before edge (Y=-c)
                // flipped:     edge at local Y=LengthA → grip left of first bar (X=-c), just past edge (Y=LengthA+c)
                local = bar.Flipped
                    ? new Point3d(-c, bar.SkewStart + bar.LengthA + c, 0)
                    : new Point3d(-c, bar.SkewStart - c, 0);
            }
            return local.TransformBy(br.BlockTransform);
        }

        public static Point3d GripSpan(BlockReference br, BarData bar)
        {
            // Dla count=1 BarsSpan=0 — grip ląduje na górnym końcu lineExt (35mm)
            // żeby był wizualnie odseparowany od grip[0] (który jest przy cover poniżej origin).
            double c = bar.Cover;
            double localEnd = bar.Count >= 2 ? bar.BarsSpan : 35.0;
            Point3d local;
            if (bar.Direction == "X")
            {
                // align with outline p3 (non-flipped) / p2 (flipped) — include SkewEnd offset
                local = bar.Flipped
                    ? new Point3d(bar.SkewEnd + bar.LengthA + c, localEnd + c, 0)
                    : new Point3d(bar.SkewEnd - c,               localEnd + c, 0);
            }
            else
            {
                // align with outline p3 (non-flipped) / p2 (flipped) — include SkewEnd offset
                local = bar.Flipped
                    ? new Point3d(localEnd + c, bar.SkewEnd + bar.LengthA + c, 0)
                    : new Point3d(localEnd + c, bar.SkewEnd - c,               0);
            }
            return local.TransformBy(br.BlockTransform);
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
                XLink.Write(bar.AnnotHandle),                                                  // [11] handle 1005
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.ShapeCode       ?? "00"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.SymbolSide        ?? "Right"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.SymbolDirection   ?? "Up"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.ViewingDirection  ?? "Auto"),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)bar.ViewSegmentIndex),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.SymbolType        ?? "Auto"), // [17]
                XLink.Write(bar.SourceBarHandle),                                              // [18] handle 1005
                XLink.Write(bar.LabelPolyHandle),                                              // [19] handle 1005
                XLink.Write(bar.LabelTextHandle),                                              // [20] handle 1005
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)bar.VisibilityMode),      // [21]
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.VisibleIndices    ?? ""),    // [22]
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.Angle),                     // [23]
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.AnnotScale),                // [24] AnnotScale
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.SkewEnd),                   // [25] SkewEnd (dawny SkewOffset)
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.SkewStart),                 // [26] SkewStart
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)(bar.CountDisplay ?? -1)), // [27] CountDisplay (-1 = null)
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)(bar.Flipped ? 1 : 0)),    // [28] Flipped
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)(bar.ShowSpacing    ? 1 : 0)), // [29] ShowSpacing
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)(bar.IsLabelManual ? 1 : 0)),  // [30] IsLabelManual
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.CutZones ?? ""),                  // [31] CutZones (otwory)
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.PairShift)                        // [32] PairShift (strzemiona 4-cięte)
            );
            if (entity is BlockReference br) RecolorAdd(br, bar);
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
            if (v.Length >= 12) bd.AnnotHandle = XLink.Read(v[11]);
            if (v.Length >= 13) bd.ShapeCode        = (string)v[12].Value;
            if (v.Length >= 14) bd.SymbolSide       = (string)v[13].Value;
            if (v.Length >= 15) bd.SymbolDirection  = (string)v[14].Value;
            if (v.Length >= 16) bd.ViewingDirection = (string)v[15].Value;
            if (v.Length >= 17) bd.ViewSegmentIndex = (short)v[16].Value;
            if (v.Length >= 18) bd.SymbolType       = (string)v[17].Value;
            if (v.Length >= 19) bd.SourceBarHandle  = XLink.Read(v[18]);
            if (v.Length >= 20) bd.LabelPolyHandle  = XLink.Read(v[19]);
            if (v.Length >= 21) bd.LabelTextHandle  = XLink.Read(v[20]);
            if (v.Length >= 22) bd.VisibilityMode   = (BarVisibilityMode)(short)v[21].Value;
            if (v.Length >= 23) bd.VisibleIndices   = (string)v[22].Value ?? "";
            if (v.Length >= 24) bd.Angle            = (double)v[23].Value;
            if (v.Length >= 25)
            {
                try { bd.AnnotScale = Convert.ToDouble(v[24].Value); }
                catch { bd.AnnotScale = 1.0; }
                if (bd.AnnotScale <= 0) bd.AnnotScale = 1.0;
            }
            else
            {
                bd.AnnotScale = 1.0;  // fallback dla starych bloków
            }
            if (v.Length >= 26)
            {
                try { bd.SkewEnd = Convert.ToDouble(v[25].Value); }
                catch { bd.SkewEnd = 0.0; }
            }
            else bd.SkewEnd = 0.0;

            if (v.Length >= 27)
            {
                try { bd.SkewStart = Convert.ToDouble(v[26].Value); }
                catch { bd.SkewStart = 0.0; }
            }
            else bd.SkewStart = 0.0;

            if (v.Length >= 28)
            {
                try
                {
                    short cd = Convert.ToInt16(v[27].Value);
                    bd.CountDisplay = cd == -1 ? (int?)null : (int)cd;
                }
                catch { bd.CountDisplay = null; }
            }
            else bd.CountDisplay = null;

            if (v.Length >= 29)
            {
                try { bd.Flipped = (short)v[28].Value != 0; }
                catch { bd.Flipped = false; }
            }
            else bd.Flipped = false;

            if (v.Length >= 30)
            {
                try { bd.ShowSpacing = (short)v[29].Value != 0; }
                catch { bd.ShowSpacing = true; }
            }
            else bd.ShowSpacing = true;

            if (v.Length >= 31)
            {
                try { bd.IsLabelManual = (short)v[30].Value != 0; }
                catch { bd.IsLabelManual = false; }
            }
            else bd.IsLabelManual = false;

            if (v.Length >= 32)
            {
                try { bd.CutZones = v[31].Value as string ?? ""; }
                catch { bd.CutZones = ""; }
            }
            if (v.Length >= 33)
            {
                try { bd.PairShift = Convert.ToDouble(v[32].Value); }
                catch { bd.PairShift = 0; }
            }

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
            catch (System.Exception logEx) { Log.Error("BarBlockEngine.LinkAnnotation", logEx); }
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
            catch (System.Exception logEx) { Log.Error("BarBlockEngine.StoreLabelHandles", logEx); }
        }

        public static bool IsBarBlock(Entity entity)
            => entity.GetXDataForApplication(XAppName) != null;

        public static bool IsAnnotAlive(Database db, string annotHandle)
        {
            if (string.IsNullOrEmpty(annotHandle)) return false;
            try
            {
                if (long.TryParse(annotHandle,
                        System.Globalization.NumberStyles.HexNumber, null, out long hv))
                {
                    if (db.TryGetObjectId(new Handle(hv), out ObjectId oid))
                        return !oid.IsErased && oid.IsValid;
                }
            }
            catch (System.Exception logEx) { Log.Error("BarBlockEngine.IsAnnotAlive", logEx); }
            return false;
        }

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
                if (IsAny(bar)) { tr.Commit(); return false; }   // ANY: kształt z wymiarów pręta, nie z długości widoku

                bar.LengthA = newLengthA;
                WriteXData(br, bar);

                var btr = OwnBtrForWrite(tr, br);
                EraseAllInBtr(tr, btr);

                BuildBars(tr, btr, bar, bar.Direction == "X", newLengthA, bar.Count);

                tr.Commit();
                return true;
            }
            catch { return false; }
        }

        // ----------------------------------------------------------------
        // Helpers
        // ----------------------------------------------------------------

        private static readonly System.Text.RegularExpressions.Regex OwnBtrPrefixRx =
            new System.Text.RegularExpressions.Regex(@"^(RC_SLAB_BARS_\d{3}|RC_ANNOT_\d{3})");

        /// <summary>
        /// BTR bloku do zapisu — własny (zgłoszenie kolegi 2026-10-16: wygaszenie / przebudowa jednego rozkładu zmieniała wszystkie
        /// kopie). Gdy z BTR korzysta więcej niż jedna referencja (kopia przeciągnięciem z Ctrl, ROTATE / SCALE z opcją Copy,
        /// REDO po COPY — poza listą komend BarCopyWatcher), BTR jest klonowany z zawartością do nowego, a <paramref name="br"/>
        /// przepięty na klon. Nazwa: prefiks RC_SLAB_BARS_nnn / RC_ANNOT_nnn + GUID (wybór po nazwie właściciela działa dalej).
        /// </summary>
        internal static BlockTableRecord OwnBtrForWrite(Transaction tr, BlockReference br)
        {
            var oldBtr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
            try
            {
                var db = br.Database;
                if (db == null || br.ObjectId.IsNull) return (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
                var refs = oldBtr.GetBlockReferenceIds(true, false);
                int live = 0;
                foreach (ObjectId r in refs) if (!r.IsErased) live++;
                if (live <= 1) return (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);

                var m = OwnBtrPrefixRx.Match(oldBtr.Name ?? "");
                string name = m.Success ? $"{m.Value}_{Guid.NewGuid():N}" : "*U";
                if (name.Length > 255) name = name.Substring(0, 255);
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                var newBtr = new BlockTableRecord
                {
                    Name         = name,
                    Origin       = oldBtr.Origin,
                    Units        = oldBtr.Units,
                    Explodable   = oldBtr.Explodable,
                    BlockScaling = oldBtr.BlockScaling
                };
                var newId = bt.Add(newBtr);
                tr.AddNewlyCreatedDBObject(newBtr, true);
                var ids = new ObjectIdCollection();
                foreach (ObjectId id in oldBtr) if (!id.IsNull && !id.IsErased) ids.Add(id);
                if (ids.Count > 0) db.DeepCloneObjects(ids, newId, new IdMapping(), false);
                if (!br.IsWriteEnabled) br.UpgradeOpen();
                br.BlockTableRecord = newId;
                Log.Info($"BarBlockEngine.OwnBtrForWrite: {oldBtr.Name} ({live} ref.) → własny {name} dla {br.Handle}");
                return (BlockTableRecord)tr.GetObject(newId, OpenMode.ForWrite);
            }
            catch (System.Exception ex)
            {
                Log.Error("BarBlockEngine.OwnBtrForWrite", ex);
                return (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForWrite);
            }
        }

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
