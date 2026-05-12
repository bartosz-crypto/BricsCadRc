using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Bricscad.EditorInput;
using Teigha.Geometry;
using System;
using System.Collections.Generic;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Slab outline picker — used by AutoRebar commands to obtain slab polyline.
    /// User can: (a) select existing polyline on target layer, or (b) draw new
    /// polyline (Mode A: multi-point closed polyline). Drawn polylines are
    /// created on temp layer (RC-TEMP-OUTLINE), used for generation, then erased.
    /// </summary>
    public static class SlabPicker
    {
        private const string TempLayer = "RC-TEMP-OUTLINE";

        /// <summary>
        /// Prompt user: select existing slab outline OR draw new polyline.
        /// Returns ObjectId of polyline + isDrawn flag (true if user drew new).
        /// Returns ObjectId.Null if user cancels.
        /// </summary>
        public static ObjectId PickOrDraw(Document doc, string existingLayer, out bool isDrawn)
        {
            isDrawn = false;
            var ed = doc.Editor;
            var db = doc.Database;

            var modeOpts = new PromptKeywordOptions(
                "\nWybor obrysu plyty [Existing/Draw] <Existing>: ")
                { AllowNone = true };
            modeOpts.Keywords.Add("Existing");
            modeOpts.Keywords.Add("Draw");
            modeOpts.Keywords.Default = "Existing";
            var modeRes = ed.GetKeywords(modeOpts);
            if (modeRes.Status == PromptStatus.Cancel) return ObjectId.Null;

            string mode = (modeRes.Status == PromptStatus.OK)
                ? modeRes.StringResult : "Existing";

            if (mode == "Draw")
            {
                isDrawn = true;
                return DrawPolyline(doc, TempLayer);
            }

            return SelectExisting(doc, existingLayer);
        }

        /// <summary>
        /// Erase polyline (called by command after generation when isDrawn=true).
        /// </summary>
        public static void Cleanup(Database db, ObjectId polyId)
        {
            if (polyId.IsNull || polyId.IsErased) return;
            try
            {
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(polyId, OpenMode.ForWrite) is Entity ent)
                        ent.Erase();
                    tr.Commit();
                }
            }
            catch { /* swallow */ }
        }

        // ---- Internal ----

        private static ObjectId SelectExisting(Document doc, string targetLayer)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            while (true)
            {
                var opts = new PromptEntityOptions(
                    $"\nWybierz obrys plyty (LWPOLYLINE na warstwie {targetLayer}):");
                opts.SetRejectMessage("\nMusi byc to LWPOLYLINE.\n");
                opts.AddAllowedClass(typeof(Polyline), true);
                var res = ed.GetEntity(opts);
                if (res.Status == PromptStatus.Cancel) return ObjectId.Null;
                if (res.Status != PromptStatus.OK) continue;

                bool layerOk = false;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    if (tr.GetObject(res.ObjectId, OpenMode.ForRead) is Polyline pl)
                    {
                        if (pl.Layer == targetLayer) layerOk = true;
                        else ed.WriteMessage(
                            $"\nPolilinia na warstwie '{pl.Layer}', wymagana '{targetLayer}'.\n");
                    }
                    tr.Commit();
                }
                if (!layerOk) continue;
                return res.ObjectId;
            }
        }

        private static ObjectId DrawPolyline(Document doc, string tempLayer)
        {
            var ed = doc.Editor;
            var db = doc.Database;

            EnsureLayer(db, tempLayer);

            var points = new List<Point2d>();

            var firstOpts = new PromptPointOptions("\nWskaz pierwszy punkt obrysu: ");
            var firstRes = ed.GetPoint(firstOpts);
            if (firstRes.Status != PromptStatus.OK) return ObjectId.Null;
            points.Add(new Point2d(firstRes.Value.X, firstRes.Value.Y));

            while (true)
            {
                var nextOpts = new PromptPointOptions(
                    "\nWskaz kolejny punkt [Close] (lub Enter zeby zamknac): ")
                    { AllowNone = true, UseBasePoint = true,
                      BasePoint = new Point3d(points[points.Count - 1].X,
                                              points[points.Count - 1].Y, 0) };
                nextOpts.Keywords.Add("Close");
                var nextRes = ed.GetPoint(nextOpts);

                if (nextRes.Status == PromptStatus.Keyword
                    && nextRes.StringResult == "Close") break;
                if (nextRes.Status == PromptStatus.None) break;
                if (nextRes.Status != PromptStatus.OK) return ObjectId.Null;

                points.Add(new Point2d(nextRes.Value.X, nextRes.Value.Y));
            }

            if (points.Count < 3)
            {
                ed.WriteMessage("\nWymagane min. 3 punkty dla zamknietego obrysu.\n");
                return ObjectId.Null;
            }

            ObjectId polyId;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                var ms = (BlockTableRecord)tr.GetObject(
                    bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

                var pl = new Polyline();
                pl.SetDatabaseDefaults();
                pl.Layer = tempLayer;
                pl.Closed = true;
                for (int i = 0; i < points.Count; i++)
                    pl.AddVertexAt(i, points[i], 0, 0, 0);

                polyId = ms.AppendEntity(pl);
                tr.AddNewlyCreatedDBObject(pl, true);
                tr.Commit();
            }

            ed.WriteMessage($"\n[SlabPicker] Utworzono obrys ({points.Count} punktow) na warstwie {tempLayer}.\n");
            return polyId;
        }

        private static void EnsureLayer(Database db, string layerName)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var lt = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!lt.Has(layerName))
                {
                    lt.UpgradeOpen();
                    var ltr = new LayerTableRecord
                    {
                        Name = layerName,
                        Color = Teigha.Colors.Color.FromColorIndex(
                            Teigha.Colors.ColorMethod.ByAci, 4)  // cyan
                    };
                    lt.Add(ltr);
                    tr.AddNewlyCreatedDBObject(ltr, true);
                }
                tr.Commit();
            }
        }
    }
}
