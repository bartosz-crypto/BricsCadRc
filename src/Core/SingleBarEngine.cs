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
                catch { }
            }

            var shape    = ShapeCodeLibrary.Get(bar.ShapeCode) ?? ShapeCodeLibrary.Get("00");
            var entities = BuildVisualEntities(shape, bar.ParamValues, bar.Diameter,
                                               insertPt, new Vector3d(1, 0, 0));

            string layerName = LayerManager.GetLayerName(bar.LayerCode);
            foreach (var ent in entities)
            {
                ent.Layer      = layerName;
                ent.ColorIndex = 7;
                ent.LineWeight = LineWeight.LineWeight000;
            }

            using var tr = db.TransactionManager.StartTransaction();
            var space = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);

            space.AppendEntity(entities[0]);
            tr.AddNewlyCreatedDBObject(entities[0], true);
            WriteXData(entities[0], bar);
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
            var pline = tr.GetObject(barPolyId, OpenMode.ForRead) as Polyline;
            if (pline == null) return textPt;

            // Użyj najbliższego punktu na krzywej — grot trafia dokładnie gdzie user kliknął
            try
            {
                return pline.GetClosestPointTo(textPt, false);
            }
            catch
            {
                // Fallback do centrum pręta
                var ext = pline.GeometricExtents;
                return new Point3d(
                    (ext.MinPoint.X + ext.MaxPoint.X) / 2.0,
                    (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0,
                    0);
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
            mt.Contents   = labelText;
            mt.TextHeight = 70.0;
            var stTable   = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
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
            ml.ColorIndex   = 2;   // żółty — jak annotacje ASD

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
                    new TypedValue((int)DxfCode.ExtendedDataAsciiString, barId.Handle.ToString()));
            }

            tr.Commit();
            return ml.ObjectId;
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
            return v.Length >= 2 ? (string)v[1].Value : null;
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
        // BuildVisualEntities — jedna polilinia z ConstantWidth = diameter.
        // BricsCAD automatycznie obsługuje offset i zaokrąglenia rogów.
        //
        // Zwraca listę z jedną encją:
        //   [0] Polyline z ConstantWidth = diameter (główna — XData tu)
        //
        // direction: dla FLOW 1 zawsze (1,0,0) — poziomy pręt w widoku elewacji
        // ----------------------------------------------------------------

        private static readonly HashSet<string> _closedShapes =
            new HashSet<string> { "34", "35", "46", "47" };

        public static List<Entity> BuildVisualEntities(
            BarShape shape, double[] paramValues, double diameter,
            Point3d startPoint, Vector3d direction)
        {
            var localPts = BarGeometryBuilder.GetLocalPoints(shape.Code, paramValues, diameter);

            double len = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            double ax  = len > 1e-9 ? direction.X / len : 1.0;
            double ay  = len > 1e-9 ? direction.Y / len : 0.0;
            double px  = -ay, py = ax;

            var pline = new Polyline();
            pline.ConstantWidth = diameter;
            for (int i = 0; i < localPts.Count; i++)
            {
                double wx = startPoint.X + localPts[i].X * ax + localPts[i].Y * px;
                double wy = startPoint.Y + localPts[i].X * ay + localPts[i].Y * py;
                pline.AddVertexAt(i, new Point2d(wx, wy), 0, 0, 0);
            }
            if (_closedShapes.Contains(shape.Code))
                pline.Closed = true;

            return new List<Entity> { pline };
        }

        // ----------------------------------------------------------------
        // WriteLinkXData — zapisuje RC_BAR_LINK z handleem głównej encji
        // ----------------------------------------------------------------

        public static void WriteLinkXData(Entity entity, string primaryHandle)
        {
            entity.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName,  XLinkAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, primaryHandle));
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
                if (v.Length >= 2 && (string)v[1].Value == primaryHandle)
                    toErase.Add(ent);
            }
            foreach (var ent in toErase)
            {
                ent.UpgradeOpen();
                ent.Erase();
            }
        }

        // ----------------------------------------------------------------
        // RebuildCompanions — aktualizuje geometrię głównej polilinii po edycji.
        // Usuwa stare encje RC_BAR_LINK (compat z poprzednim formatem),
        // potem nadpisuje wierzchołki i ConstantWidth głównej polilinii.
        // Zakłada kierunek poziomy (1,0,0) — FLOW 1 widok elewacji.
        // ----------------------------------------------------------------

        public static void RebuildCompanions(Database db, ObjectId primaryPolyId, BarData bar)
        {
            EnsureAppIdRegistered(db);

            using var tr  = db.TransactionManager.StartTransaction();
            string handle = primaryPolyId.Handle.ToString();
            DeleteLinkedEntities(db, tr, handle);   // usuwa stare towarzyszące (compat)

            var pline = (Polyline)tr.GetObject(primaryPolyId, OpenMode.ForRead);
            var shape = ShapeCodeLibrary.Get(bar.ShapeCode) ?? ShapeCodeLibrary.Get("00");
            var lPts  = BarGeometryBuilder.GetLocalPoints(shape.Code, bar.ParamValues, bar.Diameter);

            // startPoint = pt[0] polilinii, bo ConstantWidth — wierzchołki są na osi
            var pt0        = pline.GetPoint3dAt(0);
            var startPoint = new Point3d(pt0.X - lPts[0].X, pt0.Y - lPts[0].Y, 0);

            // Przebuduj wierzchołki in-place
            pline.UpgradeOpen();
            int oldCount = pline.NumberOfVertices;

            // Dodaj nowe wierzchołki na końcu
            for (int i = 0; i < lPts.Count; i++)
            {
                double wx = startPoint.X + lPts[i].X;
                double wy = startPoint.Y + lPts[i].Y;
                pline.AddVertexAt(oldCount + i, new Point2d(wx, wy), 0, 0, 0);
            }

            // Usuń stare wierzchołki od początku (indeks 0 przesuwa się po każdym usunięciu)
            for (int i = 0; i < oldCount; i++)
                pline.RemoveVertexAt(0);
            pline.ConstantWidth = bar.Diameter;
            pline.Closed = _closedShapes.Contains(shape.Code);

            tr.Commit();
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
            string handleStr = (string)v[1].Value;
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
                catch { /* poza kontekstem dokumentu */ }
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

        public static void WriteXData(Entity entity, BarData bar)
        {
            entity.XData = new ResultBuffer(
                new TypedValue((int)DxfCode.ExtendedDataRegAppName,  XAppName),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Mark ?? ""),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)bar.Diameter),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.ShapeCode ?? "00"),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.LengthA),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.LengthB),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.LengthC),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.LayerCode ?? "B1"),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.Position  ?? "BOT"),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.LengthD),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.LengthE),
                new TypedValue((int)DxfCode.ExtendedDataReal,        bar.TotalLength),
                new TypedValue((int)DxfCode.ExtendedDataInteger16,   (short)(bar.LengthOverridden ? 1 : 0)),
                new TypedValue((int)DxfCode.ExtendedDataAsciiString, bar.LabelHandle ?? "")
            );
        }

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
                LabelHandle      = v.Length >= 14 ? (string)v[13].Value : ""
            };
            bd.Direction = (bd.LayerCode == "B1" || bd.LayerCode == "T1") ? "X" :
                           (bd.LayerCode == "B2" || bd.LayerCode == "T2") ? "Y" : "X";
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

    }
}
