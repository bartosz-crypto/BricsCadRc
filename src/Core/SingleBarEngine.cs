using System;
using System.Collections.Generic;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Tworzy i przechowuje pojedynczy pret w widoku elewacji (FLOW 1 — RC_BAR).
    /// XData: RC_SINGLE_BAR na głównej polilinii (offset +r); towarzyszące poly/łuki
    /// mają XData RC_BAR_LINK z handleem głównego obiektu.
    /// </summary>
    public static class SingleBarEngine
    {
        public const string XAppName      = "RC_SINGLE_BAR";
        public const string XLinkAppName  = "RC_BAR_LINK";
        public const string XLabelAppName = "RC_BAR_LABEL";

        // ----------------------------------------------------------------
        // Rejestracja AppId
        // ----------------------------------------------------------------

        public static void EnsureAppIdRegistered(Database db)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var regTable = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);

            bool needBar   = !regTable.Has(XAppName);
            bool needLink  = !regTable.Has(XLinkAppName);
            bool needLabel = !regTable.Has(XLabelAppName);
            if (needBar || needLink || needLabel)
            {
                regTable.UpgradeOpen();
                if (needBar)
                {
                    var rec = new RegAppTableRecord { Name = XAppName };
                    regTable.Add(rec);
                    tr.AddNewlyCreatedDBObject(rec, true);
                }
                if (needLink)
                {
                    var rec = new RegAppTableRecord { Name = XLinkAppName };
                    regTable.Add(rec);
                    tr.AddNewlyCreatedDBObject(rec, true);
                }
                if (needLabel)
                {
                    var rec = new RegAppTableRecord { Name = XLabelAppName };
                    regTable.Add(rec);
                    tr.AddNewlyCreatedDBObject(rec, true);
                }
            }
            tr.Commit();
        }

        // ----------------------------------------------------------------
        // PlaceBar — wstawia polilinie preta w Model Space.
        // Zwraca ObjectId polilinii (główna encja z RC_SINGLE_BAR XData).
        // Etykieta (MLeader) dodawana osobno przez PlaceBarLabel.
        // ----------------------------------------------------------------

        public static ObjectId PlaceBar(Database db, BarData bar, Point3d insertPt)
        {
            EnsureAppIdRegistered(db);
            LayerManager.EnsureLayersExist(db);

            if (!BarGeometryBuilder.IsSupported(bar.ShapeCode))
            {
                try
                {
                    Bricscad.ApplicationServices.Application
                        .DocumentManager.MdiActiveDocument?.Editor
                        .WriteMessage(
                            $"\nRC_BAR: shape code '{bar.ShapeCode}' not yet implemented – " +
                            $"drawing as straight line.\n");
                }
                catch (System.Exception logEx) { Log.Error("SingleBarEngine.PlaceBar", logEx); }
            }

            // Plan C, etap 3: jedyny punkt rozgałęzienia formatu nowych prętów (domyślnie polilinia — bez zmian)
            if (BarFormat.Current == BarFormatKind.Block)
                return SingleBarBlock.Create(db, bar, insertPt);

            var shape    = ShapeCodeLibrary.Get(bar.ShapeCode) ?? ShapeCodeLibrary.Get("00");
            var entities = BuildVisualEntities(shape, bar.ParamValues, bar.Diameter,
                                               insertPt, new Vector3d(1, 0, 0));

            // RC_BAR template (elevation view) on LeaderLayer (white, color 7).
            // BarData.LayerCode drives distribution bars inside RC_BAR_BLOCK,
            // not the elevation template.
            foreach (var ent in entities)
            {
                ent.Layer      = LayerManager.LeaderLayer;
                ent.ColorIndex = 256;
                ent.LineWeight = LineWeight.LineWeight000;
            }

            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            space.AppendEntity(entities[0]);
            tr.AddNewlyCreatedDBObject(entities[0], true);
            WriteXData(entities[0], bar);
            BarXData.MarkW4dGeometry(entities[0]);   // W4d (runda 2): obrys zbudowany geometrią W4d
            ObjectId barId = entities[0].ObjectId;

            string primaryHandle = entities[0].Handle.ToString();
            for (int i = 1; i < entities.Count; i++)
            {
                space.AppendEntity(entities[i]);
                tr.AddNewlyCreatedDBObject(entities[i], true);
                WriteLinkXData(entities[i], primaryHandle);
            }

            tr.Commit();
            return barId;
        }

        // ----------------------------------------------------------------
        // GetBarAngle — kąt pierwszego segmentu polilinii pręta w radianach.
        // Uwzględnia obrót przez ROTATE (wierzchołki są w WCS).
        // 0 = poziomy (obrót 0°), π/2 = pionowy, inne = ukośny.
        // ----------------------------------------------------------------

        public static double GetBarAngle(Polyline pline)
        {
            if (pline == null || pline.NumberOfVertices < 2) return 0.0;
            var p0 = pline.GetPoint3dAt(0);
            var p1 = pline.GetPoint3dAt(1);
            double dx = p1.X - p0.X;
            double dy = p1.Y - p0.Y;
            if (Math.Abs(dx) < 1e-6 && Math.Abs(dy) < 1e-6) return 0.0;
            return Math.Atan2(dy, dx);
        }

        // ----------------------------------------------------------------
        // GetBarArrowTip — punkt na krawędzi pręta najbliżej textPt.
        // Używa GeometricExtents (bbox uwzględnia ConstantWidth).
        // Dla shape codes zamkniętych zawsze zwraca środek górnej krawędzi.
        // ----------------------------------------------------------------

        public static Point3d GetBarArrowTip(ObjectId barPolyId, BarData bar,
                                             Point3d textPt, Transaction tr)
        {
            var ent = tr.GetObject(barPolyId, OpenMode.ForRead) as Entity;

            // Polilinia — ścieżka wzorca bez zmian (każda polilinia, jak dawne `as Polyline`)
            if (ent is Polyline pline)
            {
                // Użyj najbliższego punktu na krzywej — grot trafia dokładnie gdzie user kliknął
                try
                {
                    return pline.GetClosestPointTo(textPt, false);
                }
                catch (System.Exception ex)
                {
                    Log.Error("SingleBarEngine.GetBarArrowTip", ex);
                    // Fallback do centrum pręta (jak we wzorcu)
                    var ext = pline.GeometricExtents;
                    return new Point3d(
                        (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                        (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                        0);
                }
            }

            // Plan C, etap 3c: pręt-blok — najbliższy punkt obrysu w WCS
            var be = BarEntity.From(ent, BarKinds.Block);
            if (be == null) return textPt;
            try
            {
                var p = be.ClosestPoint(tr, textPt);
                if (p.HasValue) return p.Value;
            }
            catch (System.Exception ex)
            {
                Log.Error("SingleBarEngine.GetBarArrowTip (blok)", ex);
            }
            return ExtentsCenter(ent, textPt);   // fallback do centrum pręta
        }

        /// <summary>Środek GeometricExtents encji (Z = 0); przy błędzie — fallback.</summary>
        private static Point3d ExtentsCenter(Entity ent, Point3d fallback)
        {
            try
            {
                var ext = ent.GeometricExtents;
                return new Point3d(
                    (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                    0);
            }
            catch (System.Exception ex)
            {
                Log.Error("SingleBarEngine.ExtentsCenter", ex);
                return fallback;
            }
        }

        // ----------------------------------------------------------------
        // PlaceBarLabel — wstawia MLeader (lider + grot + tekst) dla preta.
        // arrowTip:  punkt na pręcie (koniec strzałki)
        // textPt:    gdzie wyląduje tekst
        // labelText: np. "01 H12-01"
        // barId:     ObjectId polilinii pręta — zapisywany w XData MLeadera (RC_BAR_LABEL)
        // Zwraca ObjectId utworzonego MLeadera (ObjectId.Null przy błędzie).
        // ----------------------------------------------------------------

        public static ObjectId PlaceBarLabel(Database db, Point3d arrowTip, Point3d textPt,
                                             string labelText, ObjectId barId = default)
        {
            EnsureAppIdRegistered(db);
            LayerManager.EnsureLayersExist(db);

            using var tr  = db.TransactionManager.StartTransaction();
            var space     = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var styleId   = EnsureMLeaderStyle(db, tr);

            var mt = new MText();
            mt.SetDatabaseDefaults(db);
            mt.Contents    = labelText;
            mt.TextHeight  = 70.0;
            mt.ColorIndex  = 256;
            var stTable    = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (stTable.Has(LayerManager.AnnotTextStyle))
                mt.TextStyleId = stTable[LayerManager.AnnotTextStyle];
            mt.Location = textPt;

            var ml = new MLeader();
            ml.SetDatabaseDefaults(db);
            ml.MLeaderStyle = styleId;
            ml.ContentType  = ContentType.MTextContent;
            ml.MText        = mt;
            ml.TextLocation = textPt;
            ml.Layer        = LayerManager.AnnotLayer;
            ml.ColorIndex   = 256;

            int li  = ml.AddLeader();
            int lni = ml.AddLeaderLine(li);
            ml.AddFirstVertex(lni, arrowTip);
            ml.AddLastVertex(lni, textPt);

            space.AppendEntity(ml);
            tr.AddNewlyCreatedDBObject(ml, true);

            // Zapisz handle pręta w XData MLeadera
            if (!barId.IsNull)
            {
                ml.XData = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName,  XLabelAppName),
                    XLink.Write(barId.Handle.ToString()));   // handle 1005 — kopia pręta+etykiety przemapuje się sama
            }

            tr.Commit();
            return ml.ObjectId;
        }

        // ----------------------------------------------------------------
        // PlaceNoteLeader — opis tekstowy (np. „REBARS CUT TO SUIT”) z kilkoma strzałkami,
        // w stylu opisów rozkładów (wysokość 125, kolor żółty, styl opisów).
        // AddNoteArrows — dopisuje strzałki do istniejącego opisu.
        // ----------------------------------------------------------------

        public static ObjectId PlaceNoteLeader(Database db, IList<Point3d> arrowTips, Point3d textPt,
                                               string text, double textHeight = 125.0, short colorIndex = 2,
                                               string textStyle = null, string layer = null)
        {
            if (arrowTips == null || arrowTips.Count == 0) return ObjectId.Null;
            LayerManager.EnsureLayersExist(db);
            if (textStyle != null) EnsureTextStyle(db, textStyle);
            if (layer != null) EnsureLayer(db, layer, colorIndex);

            using var tr  = db.TransactionManager.StartTransaction();
            var space     = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            var styleId   = EnsureMLeaderStyle(db, tr);

            var mt = new MText();
            mt.SetDatabaseDefaults(db);
            mt.Contents   = text;
            mt.TextHeight = textHeight;
            mt.ColorIndex = colorIndex;
            var stTable   = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            string styleName = textStyle ?? LayerManager.AnnotTextStyle;
            if (stTable.Has(styleName))
                mt.TextStyleId = stTable[styleName];
            mt.Location = textPt;

            var ml = new MLeader();
            ml.SetDatabaseDefaults(db);
            ml.MLeaderStyle = styleId;
            ml.ContentType  = ContentType.MTextContent;
            ml.MText        = mt;
            ml.TextLocation = textPt;
            ml.Layer        = layer ?? LayerManager.AnnotLayer;
            ml.ColorIndex   = 256;

            int li = ml.AddLeader();
            foreach (var tip in arrowTips)
            {
                int lni = ml.AddLeaderLine(li);
                ml.AddFirstVertex(lni, tip);
                ml.AddLastVertex(lni, textPt);
            }

            space.AppendEntity(ml);
            tr.AddNewlyCreatedDBObject(ml, true);
            tr.Commit();
            return ml.ObjectId;
        }

        /// <summary>Style tekstu wg wzorca rysunku: ROMANS NARROW (romans.shx, 0.75), ROMANS (romans.shx, 1.0).</summary>
        public static ObjectId EnsureTextStyle(Database db, string name)
        {
            double xs = name.IndexOf("NARROW", StringComparison.OrdinalIgnoreCase) >= 0 ? 0.75 : 1.0;
            using var tr = db.TransactionManager.StartTransaction();
            var st = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
            if (st.Has(name)) { var id0 = st[name]; tr.Commit(); return id0; }
            st.UpgradeOpen();
            var rec = new TextStyleTableRecord { Name = name, FileName = "romans.shx", XScale = xs, TextSize = 0 };
            var id = st.Add(rec);
            tr.AddNewlyCreatedDBObject(rec, true);
            tr.Commit();
            return id;
        }

        public static void EnsureLayer(Database db, string name, short color)
        {
            using var tr = db.TransactionManager.StartTransaction();
            var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
            if (!lt.Has(name))
            {
                lt.UpgradeOpen();
                var rec = new LayerTableRecord { Name = name };
                if (color > 0 && color < 256)
                    rec.Color = Teigha.Colors.Color.FromColorIndex(Teigha.Colors.ColorMethod.ByAci, color);
                lt.Add(rec);
                tr.AddNewlyCreatedDBObject(rec, true);
            }
            tr.Commit();
        }

        public static void AddNoteArrows(Database db, ObjectId mleaderId, IList<Point3d> arrowTips)
        {
            if (mleaderId.IsNull || arrowTips == null || arrowTips.Count == 0) return;
            using var tr = db.TransactionManager.StartTransaction();
            if (!(tr.GetObject(mleaderId, OpenMode.ForWrite) is MLeader ml)) { tr.Commit(); return; }
            var idxs = ml.GetLeaderIndexes();
            int li = idxs != null && idxs.Count > 0 ? (int)idxs[0] : ml.AddLeader();
            var textPt = ml.TextLocation;
            foreach (var tip in arrowTips)
            {
                int lni = ml.AddLeaderLine(li);
                ml.AddFirstVertex(lni, tip);
                ml.AddLastVertex(lni, textPt);
            }
            tr.Commit();
        }

        // ----------------------------------------------------------------
        // ReadBarHandleFromLabel — odczytuje handle preta z XData MLeadera.
        // Zwraca null jeśli brak.
        // ----------------------------------------------------------------

        public static string ReadBarHandleFromLabel(MLeader ml)
        {
            var xdata = ml.GetXDataForApplication(XLabelAppName);
            if (xdata == null) return null;
            var v = xdata.AsArray();
            return v.Length >= 2 ? XLink.Read(v[1]) : null;
        }

        // ----------------------------------------------------------------
        // HandleToObjectId — zamienia hex handle string na ObjectId.
        // Zwraca ObjectId.Null przy błędzie.
        // ----------------------------------------------------------------

        public static ObjectId HandleToObjectId(Database db, string handleStr)
        {
            if (string.IsNullOrEmpty(handleStr)) return ObjectId.Null;
            try
            {
                long hVal = Convert.ToInt64(handleStr, 16);
                return db.GetObjectId(false, new Handle(hVal), 0);
            }
            catch { return ObjectId.Null; }
        }

        // ----------------------------------------------------------------
        // EnsureMLeaderStyle — tworzy styl "RC_BAR_LABEL" jeśli nie istnieje
        // ----------------------------------------------------------------

        private static ObjectId EnsureMLeaderStyle(Database db, Transaction tr)
        {
            const string StyleName = "RC_BAR_LABEL";
            var dict = (DBDictionary)tr.GetObject(db.MLeaderStyleDictionaryId, OpenMode.ForRead);
            if (dict.Contains(StyleName))
                return dict.GetAt(StyleName);

            dict.UpgradeOpen();
            var style = new MLeaderStyle();
            style.ContentType  = ContentType.MTextContent;
            style.EnableDogleg = true;
            style.DoglegLength = 50.0;
            style.ArrowSize    = 75.0;
            style.TextHeight   = 70.0;

            // Grot solid arrow (_CLOSED_FILLED = standardowy blok AutoCAD/BricsCAD)
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            if (bt.Has("_CLOSED_FILLED"))
                style.ArrowSymbolId = bt["_CLOSED_FILLED"];

            var styleId = dict.SetAt(StyleName, style);
            tr.AddNewlyCreatedDBObject(style, true);
            return styleId;
        }

        // ----------------------------------------------------------------
        // Build — oś pręta jako cienka polilinia (zachowane dla kompatybilności / testów)
        // ----------------------------------------------------------------

        public static Polyline Build(BarShape shape, double[] paramValues, double diameter,
                                     Point3d startPoint, Vector3d direction)
        {
            var localPts = BarGeometryBuilder.GetLocalPoints(shape.Code, paramValues, diameter);

            double len = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            double ax  = len > 1e-9 ? direction.X / len : 1.0;
            double ay  = len > 1e-9 ? direction.Y / len : 0.0;
            double px  = -ay, py = ax;

            var pline = new Polyline();
            for (int i = 0; i < localPts.Count; i++)
            {
                double wx = startPoint.X + localPts[i].X * ax + localPts[i].Y * px;
                double wy = startPoint.Y + localPts[i].X * ay + localPts[i].Y * py;
                pline.AddVertexAt(i, new Point2d(wx, wy), 0, 0, 0);
            }
            return pline;
        }

        // ----------------------------------------------------------------
        // ComputeOutlineVertices — oblicza wierzchołki outline polyline w WCS.
        // Wynik: left[0..n-1] + reverse(right[0..n-1]) = 2n punktów.
        // Zwraca null dla shape 44 (osobna logika 2 okręgów).
        // mirrored (poprawka #4): oś poprzeczna odwrócona (y lokalne → −y przed obrotem) — pręt odbity (MIRROR).
        // ----------------------------------------------------------------

        public static List<Point2d> ComputeOutlineVertices(
            BarShape shape, double[] paramValues, double diameter,
            Point3d startPoint, Vector3d direction, bool mirrored = false)
        {
            if (BarFrameRules.IsRingShape(shape.Code)) return null;   // W4c: pierścień = 75

            var localPts = BarGeometryBuilder.GetLocalPoints(shape.Code, paramValues, diameter);
            int n = localPts.Count;
            if (n < 2) return null;

            double len = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            double ax  = len > 1e-9 ? direction.X / len : 1.0;
            double ay  = len > 1e-9 ? direction.Y / len : 0.0;
            double px  = -ay, py = ax;
            if (mirrored) { px = ay; py = -ax; }

            double halfD = diameter / 2.0;

            var leftLocal  = new (double X, double Y)[n];
            var rightLocal = new (double X, double Y)[n];

            for (int i = 0; i < n; i++)
            {
                double tx, ty;
                if      (i == 0)     { tx = localPts[1].X   - localPts[0].X;   ty = localPts[1].Y   - localPts[0].Y; }
                else if (i == n - 1) { tx = localPts[n-1].X - localPts[n-2].X; ty = localPts[n-1].Y - localPts[n-2].Y; }
                else                 { tx = localPts[i+1].X - localPts[i-1].X; ty = localPts[i+1].Y - localPts[i-1].Y; }

                double tlen = Math.Sqrt(tx * tx + ty * ty);
                if (tlen < 1e-9) { tx = 1; ty = 0; }
                else             { tx /= tlen; ty /= tlen; }

                double nx = -ty, ny = tx;
                leftLocal[i]  = (localPts[i].X + nx * halfD, localPts[i].Y + ny * halfD);
                rightLocal[i] = (localPts[i].X - nx * halfD, localPts[i].Y - ny * halfD);
            }

            var result = new List<Point2d>(2 * n);
            for (int i = 0; i < n; i++)
            {
                double wx = startPoint.X + leftLocal[i].X * ax + leftLocal[i].Y * px;
                double wy = startPoint.Y + leftLocal[i].X * ay + leftLocal[i].Y * py;
                result.Add(new Point2d(wx, wy));
            }
            for (int i = n - 1; i >= 0; i--)
            {
                double wx = startPoint.X + rightLocal[i].X * ax + rightLocal[i].Y * px;
                double wy = startPoint.Y + rightLocal[i].X * ay + rightLocal[i].Y * py;
                result.Add(new Point2d(wx, wy));
            }
            return result;
        }

        // ----------------------------------------------------------------
        // BuildVisualEntities — outline polyline (ConstantWidth=0, Closed=true).
        // Shape 44: dwa osobne okręgi. Wszystkie inne: ComputeOutlineVertices.
        // direction: dla FLOW 1 zawsze (1,0,0) — poziomy pręt w widoku elewacji
        // ----------------------------------------------------------------

        public static List<Entity> BuildVisualEntities(
            BarShape shape, double[] paramValues, double diameter,
            Point3d startPoint, Vector3d direction, bool mirrored = false)
        {
            double len = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            double ax  = len > 1e-9 ? direction.X / len : 1.0;
            double ay  = len > 1e-9 ? direction.Y / len : 0.0;
            double px  = -ay, py = ax;
            if (mirrored) { px = ay; py = -ax; }   // poprawka #4: pręt odbity

            double halfD = diameter / 2.0;

            // ── Pierścień (W4c: 75; dawniej 44): dwa osobne zamknięte okręgi (outer + inner ring) ──
            if (BarFrameRules.IsRingShape(shape.Code))
            {
                double ringDiam   = paramValues != null && paramValues.Length > 0
                    ? paramValues[0] : diameter;
                // W4e (poprawka #13): A = średnica ZEWNĘTRZNA (BS 8666:2020 kl. 7.1) — promień osi (A − d)/2, obrys
                // zewnętrzny A/2, wewnętrzny A/2 − d; środek (A/2, A/2) bez zmian (dawniej oś o promieniu A/2)
                double ringRadius = BarDimensionRules.RingAxisRadius(ringDiam, diameter);
                double cxLocal    = ringDiam / 2.0;
                double cyLocal    = ringDiam / 2.0;

                const int circleSteps = 24;
                var result44 = new List<Entity>(2);
                foreach (double r in new[] { ringRadius + halfD, ringRadius - halfD })
                {
                    var pline44 = new Polyline();
                    pline44.ConstantWidth = 0;
                    for (int i = 0; i < circleSteps; i++)
                    {
                        double angle = 2.0 * Math.PI * i / circleSteps;
                        double lx    = cxLocal + r * Math.Cos(angle);
                        double ly    = cyLocal + r * Math.Sin(angle);
                        double wx    = startPoint.X + lx * ax + ly * px;
                        double wy    = startPoint.Y + lx * ay + ly * py;
                        pline44.AddVertexAt(i, new Point2d(wx, wy), 0, 0, 0);
                    }
                    pline44.Closed = true;
                    result44.Add(pline44);
                }
                return result44;
            }

            // ── Wszystkie pozostałe: outline przez ComputeOutlineVertices ──────
            var outlinePts = ComputeOutlineVertices(shape, paramValues, diameter, startPoint, direction, mirrored);
            if (outlinePts == null || outlinePts.Count < 4)
                return new List<Entity>();

            var outline = new Polyline();
            outline.ConstantWidth = 0.0;
            for (int i = 0; i < outlinePts.Count; i++)
                outline.AddVertexAt(i, outlinePts[i], 0, 0, 0);
            outline.Closed = true;
            return new List<Entity> { outline };
        }

        // ----------------------------------------------------------------
        // WriteLinkXData — zapisuje RC_BAR_LINK z handleem głównej encji
        // ----------------------------------------------------------------

        public static void WriteLinkXData(Entity entity, string primaryHandle)
        {
            entity.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName,  XLinkAppName),
                XLink.Write(primaryHandle));
        }

        // ----------------------------------------------------------------
        // DeleteLinkedEntities — usuwa encje z RC_BAR_LINK → primaryHandle
        // Musi być wywołane wewnątrz otwartej transakcji.
        // ----------------------------------------------------------------

        public static void DeleteLinkedEntities(Database db, Transaction tr, string primaryHandle)
        {
            var space    = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForRead);
            var toErase  = new List<Entity>();
            foreach (ObjectId id in space)
            {
                Entity ent;
                try   { ent = tr.GetObject(id, OpenMode.ForRead) as Entity; }
                catch { continue; }
                if (ent == null || ent.IsErased) continue;

                var xdata = ent.GetXDataForApplication(XLinkAppName);
                if (xdata == null) continue;
                var v = xdata.AsArray();
                if (v.Length >= 2 && XLink.Same(XLink.Read(v[1]), primaryHandle))
                    toErase.Add(ent);
            }
            foreach (var ent in toErase)
            {
                ent.UpgradeOpen();
                ent.Erase();
            }
        }

        // ----------------------------------------------------------------
        // FrameFromOutline — obrót i odbicie pręta-polilinii z obrysu (poprawka #4, BarFrameRules)
        // ----------------------------------------------------------------

        /// <summary>Pręt-pierścień (poprawka #5) — jedyne rozpoznanie: BarFrameRules.IsRingShape (W4: kod 75).</summary>
        public static bool IsRingShape(BarData bar)
            => BarFrameRules.IsRingShape((ShapeCodeLibrary.Get(bar?.ShapeCode) ?? ShapeCodeLibrary.Get("00")).Code);

        /// <summary>Wierzchołki polilinii (WCS, 3D z Z = elewacja).</summary>
        public static List<Point3d> OutlinePoints(Polyline pl)
        {
            var r = new List<Point3d>(pl.NumberOfVertices);
            for (int i = 0; i < pl.NumberOfVertices; i++) r.Add(pl.GetPoint3dAt(i));
            return r;
        }

        /// <summary>
        /// Rama pręta z obrysu, którego wymiary opisuje shapeBar (dane pasujące do TEGO obrysu — przy edycji: dane
        /// sprzed zmiany). Obrys porównywany ze wszystkimi wariantami geometrii (BarFrameRules.TryFitVariants — obrysy
        /// sprzed W4 i z etapów W4). Próby: dopasowanie obrysu (pierścień: TryFitRing) → to samo dla fallbackOutline (np. obrys
        /// sprzed komendy, gdy bieżący jest zepsuty) → kierunek z cięciwy osi bieżącego obrysu → tożsamość.
        /// Pręt poziomy nieodbity → tożsamość DOKŁADNIE (przebudowa bit w bit jak wzorzec).
        /// </summary>
        public static BarFrame FrameFromOutline(IReadOnlyList<Point3d> outline, BarData shapeBar,
                                                IReadOnlyList<Point3d> fallbackOutline = null)
        {
            try
            {
                var shape = ShapeCodeLibrary.Get(shapeBar?.ShapeCode) ?? ShapeCodeLibrary.Get("00");
                if (BarFrameRules.IsRingShape(shape.Code))
                {
                    if (BarFrameRules.TryFitRing(Xy(outline), out _, out var rf)) return new BarFrame(rf);
                    if (BarFrameRules.TryFitRing(Xy(fallbackOutline), out _, out var rf2)) return new BarFrame(rf2);
                    return BarFrame.Identity;
                }

                var local = BarFrameRules.LocalOutline(shape.Code, shapeBar.ParamValues, shapeBar.Diameter);
                if (local == null) return BarFrame.Identity;
                // Plan W4 (0-POPRAWKI p.2): obrys z dowolnego etapu W4 (bieżąca geometria → inne warianty → sprzed W4)
                if (BarFrameRules.TryFitVariants(Xy(outline), shape.Code, shapeBar.ParamValues, shapeBar.Diameter, out var f, out _))
                    return new BarFrame(f);
                if (BarFrameRules.TryFitVariants(Xy(fallbackOutline), shape.Code, shapeBar.ParamValues, shapeBar.Diameter, out var f2, out _))
                    return new BarFrame(f2);

                // Cięciwa osi: axis[0] → axis[n−1] obrysu wobec lPts[0] → lPts[n−1]
                var lPts = BarGeometryBuilder.GetLocalPoints(shape.Code, shapeBar.ParamValues, shapeBar.Diameter);
                if (outline != null && outline.Count >= 4 && outline.Count % 2 == 0 && lPts != null && lPts.Count >= 2)
                {
                    int t = outline.Count, n = t / 2;
                    var a0 = ((outline[0].X + outline[t - 1].X) / 2.0, (outline[0].Y + outline[t - 1].Y) / 2.0);
                    var an = ((outline[n - 1].X + outline[n].X) / 2.0, (outline[n - 1].Y + outline[n].Y) / 2.0);
                    if (BarFrameRules.TryChord(a0, an, lPts[0], lPts[lPts.Count - 1], out var fc)) return new BarFrame(fc);
                }
            }
            catch (System.Exception ex)
            {
                Log.Error("SingleBarEngine.FrameFromOutline", ex);
            }
            return BarFrame.Identity;
        }

        /// <summary>
        /// W4h runda 2 (decyzja 2): przebudowa STAREGO pręta 14 / 15 / 24 (obrys inny niż geometria bieżąca — MOVE / Plan B,
        /// RC_REBUILD_BARS, RC_EDIT_BAR), gdy kąt z jednego rzutu wychodzi skrajny (B &gt; A → 90°, D ≥ A → 0° —
        /// BarShape.SlopeClampWarning) → komunikat [RC AUTO] w linii poleceń. Tylko odczyt.
        /// </summary>
        internal static void WarnSlopeClampOnRebuild(BarData bar, IReadOnlyList<Point3d> outlineBefore)
        {
            try
            {
                var shape = ShapeCodeLibrary.Get(bar?.ShapeCode);
                string w = shape?.SlopeClampWarning(bar.ParamValues);
                if (w == null) return;
                if (outlineBefore != null
                    && BarFrameRules.TryFitVariants(Xy(outlineBefore), shape.Code, bar.ParamValues, bar.Diameter, out _, out var matched)
                    && matched.SameAs(BarGeometryOptions.Current)) return;   // obrys już bieżący — nie stary pręt
                Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor?
                    .WriteMessage(Environment.NewLine + "[RC AUTO] Pręt " + bar.Mark + " przebudowany wg BS 8666:2020: " + w
                                  + Environment.NewLine);
            }
            catch (System.Exception ex) { Log.Error("SingleBarEngine.WarnSlopeClampOnRebuild", ex); }
        }

        private static List<(double X, double Y)> Xy(IReadOnlyList<Point3d> pts)
        {
            if (pts == null) return null;
            var r = new List<(double X, double Y)>(pts.Count);
            foreach (var p in pts) r.Add((p.X, p.Y));
            return r;
        }

        // ----------------------------------------------------------------
        // RebuildCompanions — aktualizuje geometrię głównej polilinii po edycji.
        // Usuwa stare encje RC_BAR_LINK (compat z poprzednim formatem),
        // potem nadpisuje wierzchołki i ConstantWidth głównej polilinii.
        // Poprawka #4 (W1d): obrys w ramie pręta (obrót + odbicie) — wzorzec zakładał kierunek poziomy (1,0,0).
        // frame == null → rama z bieżącego obrysu i danych bar (FrameFromOutline). Rama tożsamościowa (pręt poziomy,
        // nieodbity) → DOKŁADNIE dawna formuła: start = axis[0] − lPts[0], kierunek (1,0,0) — wynik bit w bit z wzorcem.
        // ----------------------------------------------------------------

        public static void RebuildCompanions(Database db, ObjectId primaryPolyId, BarData bar,
                                             Point3d? axisStartOverride = null, BarFrame? frame = null)
        {
            EnsureAppIdRegistered(db);

            using var tr  = db.TransactionManager.StartTransaction();
            string handle = primaryPolyId.Handle.ToString();
            var shape = ShapeCodeLibrary.Get(bar.ShapeCode) ?? ShapeCodeLibrary.Get("00");

            // Poprawka #5 (W1e): pierścień — przebudowa obu okręgów z zachowaniem środka (wzorzec kasował wewnętrzny
            // okrąg RC_BAR_LINK i nie przebudowywał). Gałąź PRZED DeleteLinkedEntities — wewnętrzny okrąg jest częścią pręta.
            if (BarFrameRules.IsRingShape(shape.Code))
            {
                var ring = (Polyline)tr.GetObject(primaryPolyId, OpenMode.ForRead);
                Rebuild44(tr, ring, bar, shape, axisStartOverride, frame);
                tr.Commit();
                return;
            }

            DeleteLinkedEntities(db, tr, handle);

            var pline = (Polyline)tr.GetObject(primaryPolyId, OpenMode.ForRead);

            var lPts = BarGeometryBuilder.GetLocalPoints(shape.Code, bar.ParamValues, bar.Diameter);
            if (lPts == null || lPts.Count < 2) { tr.Commit(); return; }

            WarnSlopeClampOnRebuild(bar, OutlinePoints(pline));   // W4h runda 2: stary 14 / 15 / 24 z rzutem > ramię
            var pt0 = axisStartOverride ?? GetAxisFirstPointFromOutline(pline, bar.ShapeCode);
            var fr  = frame ?? FrameFromOutline(OutlinePoints(pline), bar);

            Point3d  startPoint;
            Vector3d direction;
            if (fr.IsIdentity)
            {
                // Dawna formuła wzorca (bit w bit)
                startPoint = new Point3d(pt0.X - lPts[0].X, pt0.Y - lPts[0].Y, 0);
                direction  = new Vector3d(1.0, 0.0, 0.0);
            }
            else
            {
                var off    = fr.ToWorld(lPts[0].X, lPts[0].Y);
                startPoint = new Point3d(pt0.X - off.X, pt0.Y - off.Y, 0);
                direction  = fr.Direction;
            }

            var outlinePts = ComputeOutlineVertices(shape, bar.ParamValues, bar.Diameter, startPoint, direction, fr.Mirrored);
            if (outlinePts == null || outlinePts.Count < 4) { tr.Commit(); return; }

            pline.UpgradeOpen();
            int oldCount = pline.NumberOfVertices;
            for (int i = 0; i < outlinePts.Count; i++)
                pline.AddVertexAt(oldCount + i, outlinePts[i], 0, 0, 0);
            for (int i = 0; i < oldCount; i++)
                pline.RemoveVertexAt(0);

            pline.ConstantWidth = 0.0;
            pline.Closed = true;
            BarXData.MarkW4dGeometry(pline);   // W4d (runda 2, D34): obrys przebudowany geometrią W4d → znacznik [15] = 2021

            tr.Commit();
        }

        /// <summary>
        /// Poprawka #5 (W1e) — przebudowa pręta-pierścienia (rozpoznanie: BarFrameRules.IsRingShape) z danych bar:
        /// ŚRODEK zostaje (z obrotem i odbiciem) — ramka i środek z bieżącego okręgu zewnętrznego (TryFitRing; zapas:
        /// jawna rama, środek = średnia wierzchołków), start = c − R·(A/2, A/2). startOverride (zmiana kształtu NA pierścień)
        /// → start = axis[0] starego pręta. Zewnętrzny okrąg = polilinia główna (podmiana wierzchołków); wewnętrzny —
        /// RC_BAR_LINK w tej samej przestrzeni: dokładnie jedna polilinia → podmiana wierzchołków, inaczej usunięcie
        /// wszystkich i nowa (właściwości jak polilinia główna, WriteLinkXData).
        /// </summary>
        private static void Rebuild44(Transaction tr, Polyline pline, BarData bar, BarShape shape,
                                      Point3d? startOverride, BarFrame? frame)
        {
            var cur = OutlinePoints(pline);
            BarFrame fr;
            Point3d start;
            if (startOverride.HasValue)
            {
                fr    = frame ?? BarFrame.Identity;
                start = new Point3d(startOverride.Value.X, startOverride.Value.Y, 0);
            }
            else
            {
                double cx, cy;
                if (BarFrameRules.TryFitRing(Xy(cur), out var c, out var f))
                {
                    fr = new BarFrame(f);
                    cx = c.X; cy = c.Y;
                }
                else
                {
                    fr = frame ?? BarFrame.Identity;
                    cx = 0; cy = 0;
                    foreach (var p in cur) { cx += p.X; cy += p.Y; }
                    if (cur.Count > 0) { cx /= cur.Count; cy /= cur.Count; }
                }
                double half = (bar.ParamValues != null && bar.ParamValues.Length > 0 ? bar.ParamValues[0] : bar.Diameter) / 2.0;
                var off = fr.ToWorld(half, half);
                start   = new Point3d(cx - off.X, cy - off.Y, 0);
            }

            var ents = BuildVisualEntities(shape, bar.ParamValues, bar.Diameter, start, fr.Direction, fr.Mirrored);
            try
            {
                if (ents.Count < 2 || !(ents[0] is Polyline outer) || !(ents[1] is Polyline inner)) return;

                // Zewnętrzny: podmiana wierzchołków polilinii głównej (jak RebuildCompanions)
                pline.UpgradeOpen();
                ReplaceVertices(pline, outer);
                pline.ConstantWidth = 0.0;
                pline.Closed = true;

                // Wewnętrzny: RC_BAR_LINK → ta polilinia, w przestrzeni właściciela
                string handle = pline.Handle.ToString();
                var owner = (BlockTableRecord)tr.GetObject(pline.OwnerId, OpenMode.ForRead);
                var links = new List<Entity>();
                foreach (ObjectId id in owner)
                {
                    if (id.IsErased) continue;
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity e) || e.IsErased) continue;
                    using (var xd = e.GetXDataForApplication(XLinkAppName))
                    {
                        var v = xd?.AsArray();
                        if (v != null && v.Length >= 2 && XLink.Same(XLink.Read(v[1]), handle)) links.Add(e);
                    }
                }
                if (links.Count == 1 && links[0] is Polyline linkPl)
                {
                    linkPl.UpgradeOpen();
                    ReplaceVertices(linkPl, inner);
                    linkPl.ConstantWidth = 0.0;
                    linkPl.Closed = true;
                }
                else
                {
                    foreach (var e in links) { e.UpgradeOpen(); e.Erase(); }
                    owner.UpgradeOpen();
                    inner.SetPropertiesFrom(pline);
                    owner.AppendEntity(inner);
                    tr.AddNewlyCreatedDBObject(inner, true);
                    WriteLinkXData(inner, handle);
                    ents.RemoveAt(1);   // dodany do bazy — nie zwalniać
                }
            }
            finally
            {
                foreach (var e in ents) if (e.ObjectId.IsNull) e.Dispose();
            }
        }

        /// <summary>Wierzchołki target := wierzchołki source (dopisanie nowych, potem usunięcie starych — jak RebuildCompanions).</summary>
        private static void ReplaceVertices(Polyline target, Polyline source)
        {
            int oldCount = target.NumberOfVertices;
            for (int i = 0; i < source.NumberOfVertices; i++)
                target.AddVertexAt(oldCount + i, source.GetPoint2dAt(i), 0, 0, 0);
            for (int i = 0; i < oldCount; i++)
                target.RemoveVertexAt(0);
        }

        // ----------------------------------------------------------------
        // ResolvePrimaryId — jeśli encja to RC_BAR_LINK, zwraca ObjectId głównej
        // Zwraca ObjectId.Null jeśli nie jest towarzyszącą encją.
        // ----------------------------------------------------------------

        public static ObjectId ResolvePrimaryId(Entity entity)
        {
            var xdata = entity.GetXDataForApplication(XLinkAppName);
            if (xdata == null) return ObjectId.Null;
            var v = xdata.AsArray();
            if (v.Length < 2) return ObjectId.Null;
            string handleStr = XLink.Read(v[1]);
            try
            {
                long hVal  = Convert.ToInt64(handleStr, 16);
                var  h     = new Handle(hVal);
                return entity.Database.GetObjectId(false, h, 0);
            }
            catch { return ObjectId.Null; }
        }

        // ----------------------------------------------------------------
        // GetShapePoints — zachowane dla kompatybilności (deleguje do BarGeometryBuilder)
        // ----------------------------------------------------------------

        public static List<Point3d> GetShapePoints(BarData bar)
        {
            if (!BarGeometryBuilder.IsSupported(bar.ShapeCode))
            {
                // Ostrzeżenie widoczne w command line BricsCAD
                try
                {
                    var ed = Bricscad.ApplicationServices.Application
                        .DocumentManager.MdiActiveDocument?.Editor;
                    ed?.WriteMessage(
                        $"\nRC_BAR: shape code '{bar.ShapeCode}' not yet implemented – " +
                        $"drawing as straight line (shape 00).\n");
                }
                catch (System.Exception logEx) { Log.Error("SingleBarEngine.GetShapePoints", logEx); /* poza kontekstem dokumentu */ }
            }

            double[] paramValues = { bar.LengthA, bar.LengthB, bar.LengthC, bar.LengthD, bar.LengthE };
            var local = BarGeometryBuilder.GetLocalPoints(bar.ShapeCode, paramValues, bar.Diameter);

            var result = new List<Point3d>(local.Count);
            foreach (var p in local)
                result.Add(new Point3d(p.X, p.Y, 0));
            return result;
        }

        // ----------------------------------------------------------------
        // XData: [0]AppName  [1]Mark     [2]Diameter    [3]ShapeCode
        //        [4]LengthA  [5]LengthB  [6]LengthC     [7]LayerCode  [8]Position
        //        [9]LengthD  [10]LengthE [11]TotalLength [12]LengthOverridden
        //        [13]LabelHandle (hex handle MLeadera — opcjonalne, FLOW 1)
        // ----------------------------------------------------------------

        /// <summary>
        /// Zapis [0]–[13] przez BarXData.Write (Plan C, etap 2a): ogon rekordu od [14] (np. FormatVersion
        /// pręta-bloku) zostaje zachowany. Polilinie nie mają ogona → wynik jak dotąd (14 pól).
        /// </summary>
        public static void WriteXData(Entity entity, BarData bar)
            => BarXData.Write(entity, bar);

        public static BarData ReadBarXData(Entity entity)
        {
            var xdata = entity.GetXDataForApplication(XAppName);
            if (xdata == null) return null;
            var v = xdata.AsArray();
            if (v.Length < 8) return null;

            var bd = new BarData
            {
                Mark             = (string)v[1].Value,
                Diameter         = (short)v[2].Value,
                ShapeCode        = (string)v[3].Value,
                LengthA          = (double)v[4].Value,
                LengthB          = (double)v[5].Value,
                LengthC          = (double)v[6].Value,
                LayerCode        = (string)v[7].Value,
                Position         = v.Length >= 9  ? (string)v[8].Value  : "BOT",
                LengthD          = v.Length >= 10 ? (double)v[9].Value  : 0,
                LengthE          = v.Length >= 11 ? (double)v[10].Value : 0,
                TotalLength      = v.Length >= 12 ? (double)v[11].Value : 0,
                LengthOverridden = v.Length >= 13 && (short)v[12].Value != 0,
                LabelHandle      = v.Length >= 14 ? XLink.Read(v[13]) : ""
            };
            bd.Direction = (bd.LayerCode == "B1" || bd.LayerCode == "T1") ? "X" :
                           (bd.LayerCode == "B2" || bd.LayerCode == "T2") ? "Y" : "X";
            // Plan W4c: „44” bez znacznika [15] ShapeStd = 2020 → stary pierścień → w pamięci 75 (BS 8666:2020)
            var t15 = v.Length > BarRecordRules.ShapeStdIndex ? v[BarRecordRules.ShapeStdIndex] : default(TypedValue);
            if (BarRecordRules.IsLegacyRing44(bd.ShapeCode, v.Length, t15.TypeCode, t15.Value))
            {
                bd.ShapeCode    = BarFrameRules.RingShapeCode;
                bd.LegacyRing44 = true;
            }
            // W4d (runda 2): znacznik [15] ≥ 2021 = obrys zbudowany geometrią W4d (długość rozkładu Manual — D29/D34)
            bd.GeometryW4d = BarRecordRules.IsW4dGeometry(v.Length, t15.TypeCode, t15.Value);
            // Fallback dla starych XData bez TotalLength
            if (bd.TotalLength <= 0) bd.TotalLength = bd.LengthA;
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
        // Outline geometry helpers — odtwarzanie osi z outline polyline
        // ----------------------------------------------------------------

        /// <summary>
        /// Zwraca pierwszy punkt osi (axis[0]) SingleBar polyline outline w układzie świata.
        /// Działa dla wszystkich shape codes z outline left+reverse(right) — tj. shape != 44.
        /// Dla shape 44 (ring) zwraca pline.GetPoint3dAt(0) jako fallback (TODO: osobny fix).
        /// </summary>
        public static Point3d GetAxisFirstPointFromOutline(Polyline pl, string shapeCode)
        {
            if (BarFrameRules.IsRingShape(shapeCode))   // W4c: pierścień = 75
                return pl.GetPoint3dAt(0); // TODO: ring axis recovery — osobny case do zaimplementowania

            int total = pl.NumberOfVertices;
            if (total < 4 || total % 2 != 0)
                return pl.GetPoint3dAt(0); // defensive fallback

            var left0     = pl.GetPoint3dAt(0);
            var rightLast = pl.GetPoint3dAt(total - 1);
            return new Point3d(
                (left0.X + rightLast.X) / 2.0,
                (left0.Y + rightLast.Y) / 2.0,
                0);
        }

        /// <summary>
        /// Zwraca długość osi prostego SingleBar (shape 00/99) z outline polyline.
        /// Oś prostego bar-a = segment od axis[0] do axis[n-1], gdzie n = NumberOfVertices/2.
        /// NIE używać dla shape z zagięciami (shape 11+) — dla nich oś nie jest prostym segmentem.
        /// </summary>
        public static double GetStraightBarAxisLength(Polyline pl)
        {
            int total = pl.NumberOfVertices;
            if (total < 4 || total % 2 != 0) return 0.0;
            int n = total / 2;

            var left0      = pl.GetPoint3dAt(0);
            var rightLast  = pl.GetPoint3dAt(total - 1);
            var leftLast   = pl.GetPoint3dAt(n - 1);
            var rightFirst = pl.GetPoint3dAt(n);

            var axisStart = new Point3d((left0.X + rightLast.X) / 2.0, (left0.Y + rightLast.Y) / 2.0, 0);
            var axisEnd   = new Point3d((leftLast.X + rightFirst.X) / 2.0, (leftLast.Y + rightFirst.Y) / 2.0, 0);
            return axisStart.DistanceTo(axisEnd);
        }

        /// <summary>
        /// Zwraca wszystkie axis points pręta z jego outline polylinii.
        /// Outline ma 2N vertices: pierwsze N to "left" (od start do end),
        /// drugie N to "right" (od end do start). axis[i] = mid(left[i], right[N-1-i]).
        /// Dla shape 44 (ring) — fallback na pierwsze N punktów outline.
        /// </summary>
        public static List<Point3d> GetAxisPointsFromOutline(Polyline pl, string shapeCode)
        {
            var result = new List<Point3d>();
            if (pl == null || pl.NumberOfVertices < 2) return result;
            int total = pl.NumberOfVertices;
            if (BarFrameRules.IsRingShape(shapeCode))   // W4c: pierścień = 75
            {
                for (int i = 0; i < total; i++)
                    result.Add(pl.GetPoint3dAt(i));
                return result;
            }
            if (total % 2 != 0)
            {
                for (int i = 0; i < total; i++)
                    result.Add(pl.GetPoint3dAt(i));
                return result;
            }
            int n = total / 2;
            for (int i = 0; i < n; i++)
            {
                Point3d left  = pl.GetPoint3dAt(i);
                Point3d right = pl.GetPoint3dAt(total - 1 - i);
                result.Add(new Point3d((left.X + right.X) * 0.5, (left.Y + right.Y) * 0.5, 0));
            }
            return result;
        }

    }

    /// <summary>
    /// Rama pręta-polilinii (poprawka #4): kąt obrotu układu lokalnego, odbicie (y lokalne → −y przed obrotem),
    /// kierunek osi X w świecie. Tożsamość = pręt poziomy nieodbity (dawne zachowanie wzorca, kierunek (1,0,0) dokładnie).
    /// </summary>
    public readonly struct BarFrame
    {
        public readonly double Angle;
        public readonly bool Mirrored;

        public BarFrame(double angle, bool mirrored)
        {
            Angle = angle;
            Mirrored = mirrored;
        }

        internal BarFrame(BarFrame2 f) : this(f.Angle, f.Mirrored) { }

        public static BarFrame Identity => new BarFrame(0.0, false);

        public bool IsIdentity => Angle == 0.0 && !Mirrored;

        /// <summary>Kierunek osi X pręta w świecie; dla tożsamości dokładnie (1,0,0).</summary>
        public Vector3d Direction => IsIdentity ? new Vector3d(1.0, 0.0, 0.0) : new Vector3d(Math.Cos(Angle), Math.Sin(Angle), 0.0);

        /// <summary>Wektor lokalny (x, y) → świat (R · M · v).</summary>
        public Vector3d ToWorld(double x, double y)
        {
            var (wx, wy) = new BarFrame2(Angle, Mirrored).Apply(x, y);
            return new Vector3d(wx, wy, 0.0);
        }

        public override string ToString()
            => "kąt=" + (Angle * 180.0 / Math.PI).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
             + "° odbicie=" + (Mirrored ? "tak" : "nie");
    }
}
