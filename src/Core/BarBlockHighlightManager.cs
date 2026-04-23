using System;
using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Polyline = Teigha.DatabaseServices.Polyline;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    public static class BarBlockHighlightManager
    {
        private static readonly Dictionary<ObjectId, Entity>   _transientsByBlock
            = new Dictionary<ObjectId, Entity>();
        // BTR Handle.Value (long) → BlockReference ObjectId — klucz jako long bo ObjectId.Equals()
        // nie zawsze działa poprawnie między różnymi kontekstami (np. OM vs SelectImplied).
        private static readonly Dictionary<long, ObjectId> _btrToBlock
            = new Dictionary<long, ObjectId>();
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            doc.ImpliedSelectionChanged    += OnSelectionChanged;
            doc.Database.ObjectModified    += OnObjectModified;
            doc.Database.ObjectAppended    += OnObjectAppended;
            _registered = true;
        }

        public static void Unregister()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                try { doc.ImpliedSelectionChanged -= OnSelectionChanged;  } catch { }
                try { doc.Database.ObjectModified -= OnObjectModified;    } catch { }
                try { doc.Database.ObjectAppended -= OnObjectAppended;    } catch { }
            }
            ClearTransients();
            _registered = false;
        }

        private static void OnSelectionChanged(object sender, EventArgs e)
        {
            try
            {
                var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage($"\n[HL-SEL-FIRE]");

                ClearTransients();
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc == null) return;

                var sel = doc.Editor.SelectImplied();
                if (sel.Status != PromptStatus.OK || sel.Value == null)
                {
                    ed?.WriteMessage($"\n[HL-SEL-END] transients={_transientsByBlock.Count}");
                    return;
                }

                using (var tr = doc.Database.TransactionManager.StartTransaction())
                {
                    ObjectId ltId = ObjectId.Null;
                    var ltTable = (LinetypeTable)tr.GetObject(doc.Database.LinetypeTableId, OpenMode.ForRead);
                    if (ltTable.Has("DASHED"))  ltId = ltTable["DASHED"];
                    else if (ltTable.Has("_DOT")) ltId = ltTable["_DOT"];

                    foreach (SelectedObject so in sel.Value)
                    {
                        if (so == null || so.ObjectId.IsNull) continue;
                        var br = tr.GetObject(so.ObjectId, OpenMode.ForRead) as BlockReference;
                        if (br == null) continue;
                        if (!BarBlockEngine.IsBarBlock(br)) continue;

                        var outline = BuildOutline(br, ltId);
                        if (outline == null) continue;

                        _transientsByBlock[so.ObjectId] = outline;
                        _btrToBlock[br.BlockTableRecord.Handle.Value] = so.ObjectId;
                        TransientManager.CurrentTransientManager.AddTransient(
                            outline,
                            TransientDrawingMode.DirectShortTerm,
                            128,
                            new IntegerCollection());
                    }
                    tr.Commit();
                }

                ed?.WriteMessage($"\n[HL-SEL-END] transients={_transientsByBlock.Count}");
            }
            catch { }
        }

        private static void OnObjectModified(object sender, ObjectEventArgs e)
        {
            try
            {
                if (e.DBObject == null) return;
                var id = e.DBObject.ObjectId;
                var ownerId = e.DBObject is Entity entOM ? entOM.OwnerId : ObjectId.Null;

                if (_transientsByBlock.ContainsKey(id))
                {
                    RefreshOutlineFor(id);
                    return;
                }
                // Entity inside a BTR of a selected block was modified — refresh the parent block outline.
                if (!ownerId.IsNull && _btrToBlock.TryGetValue(ownerId.Handle.Value, out var blockId))
                    RefreshOutlineFor(blockId);
            }
            catch { }
        }

        private static void OnObjectAppended(object sender, ObjectEventArgs e)
        {
            try
            {
                if (e.DBObject == null) return;
                var ownerId = e.DBObject is Entity entOA ? entOA.OwnerId : ObjectId.Null;

                if (!ownerId.IsNull && _btrToBlock.TryGetValue(ownerId.Handle.Value, out var blockId))
                    RefreshOutlineFor(blockId);
            }
            catch { }
        }

        public static void ShowOutlineFor(ObjectId blockId)
        {
            if (blockId.IsNull || blockId.IsErased) return;
            if (_transientsByBlock.ContainsKey(blockId)) return;

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                var br = tr.GetObject(blockId, OpenMode.ForRead) as BlockReference;
                if (br == null) { tr.Commit(); return; }

                ObjectId ltId = ObjectId.Null;
                var ltTable = (LinetypeTable)tr.GetObject(doc.Database.LinetypeTableId, OpenMode.ForRead);
                if (ltTable.Has("DASHED"))   ltId = ltTable["DASHED"];
                else if (ltTable.Has("_DOT")) ltId = ltTable["_DOT"];

                var outline = BuildOutline(br, ltId);
                if (outline != null)
                {
                    _transientsByBlock[blockId] = outline;
                    _btrToBlock[br.BlockTableRecord.Handle.Value] = blockId;
                    TransientManager.CurrentTransientManager.AddTransient(
                        outline, TransientDrawingMode.DirectShortTerm, 128,
                        new IntegerCollection());
                }
                tr.Commit();
            }
        }

        public static void RefreshOutlineForBlock(ObjectId blockId)
        {
            RefreshOutlineFor(blockId);
        }

        public static void HideOutlineFor(ObjectId blockId)
        {
            if (_transientsByBlock.TryGetValue(blockId, out var d))
            {
                try { TransientManager.CurrentTransientManager.EraseTransient(d, new IntegerCollection()); } catch { }
                try { d.Dispose(); } catch { }
                _transientsByBlock.Remove(blockId);
            }
        }

        public static void HideAllOutlines()
        {
            var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
            ed?.WriteMessage($"\n[HL-HIDE-ALL] hidingCount={_transientsByBlock.Count}");
            ClearTransients();
        }

        private static void RefreshOutlineFor(ObjectId blockId)
        {
            if (_transientsByBlock.TryGetValue(blockId, out var old))
            {
                try { TransientManager.CurrentTransientManager.EraseTransient(old, new IntegerCollection()); } catch { }
                try { old.Dispose(); } catch { }
                _transientsByBlock.Remove(blockId);
            }

            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;

            using (var tr = doc.Database.TransactionManager.StartTransaction())
            {
                if (blockId.IsNull || blockId.IsErased) { tr.Commit(); return; }
                var br = tr.GetObject(blockId, OpenMode.ForRead) as BlockReference;
                if (br == null) { tr.Commit(); return; }

                ObjectId ltId = ObjectId.Null;
                var ltTable = (LinetypeTable)tr.GetObject(doc.Database.LinetypeTableId, OpenMode.ForRead);
                if (ltTable.Has("DASHED"))   ltId = ltTable["DASHED"];
                else if (ltTable.Has("_DOT")) ltId = ltTable["_DOT"];

                var outline = BuildOutline(br, ltId);
                if (outline != null)
                {
                    _transientsByBlock[blockId] = outline;
                    _btrToBlock[br.BlockTableRecord.Handle.Value] = blockId;
                    TransientManager.CurrentTransientManager.AddTransient(
                        outline,
                        TransientDrawingMode.DirectShortTerm,
                        128,
                        new IntegerCollection());
                }
                tr.Commit();
            }
        }

        private static Entity BuildOutline(BlockReference br, ObjectId ltId)
        {
            var bar = BarBlockEngine.ReadXData(br);
            if (bar == null) return null;
            if (bar.Count <= 0 || bar.LengthA <= 0) return null;

            double barsSpan = Math.Max(0, bar.BarsSpan);
            double cover = Math.Max(0, bar.Cover);
            double lenA  = bar.LengthA;

            Point2d p0, p1, p2, p3;
            if (bar.Direction == "X")
            {
                p0 = new Point2d(-cover,        -cover);
                p1 = new Point2d(lenA,          -cover);
                p2 = new Point2d(lenA,           barsSpan + cover);
                p3 = new Point2d(-cover,         barsSpan + cover);
            }
            else
            {
                p0 = new Point2d(-cover,           -cover);
                p1 = new Point2d(barsSpan + cover, -cover);
                p2 = new Point2d(barsSpan + cover,  lenA);
                p3 = new Point2d(-cover,            lenA);
            }

            var poly = new Polyline(4);
            poly.AddVertexAt(0, p0, 0, 0, 0);
            poly.AddVertexAt(1, p1, 0, 0, 0);
            poly.AddVertexAt(2, p2, 0, 0, 0);
            poly.AddVertexAt(3, p3, 0, 0, 0);
            poly.Closed     = true;
            poly.ColorIndex = 3;
            if (!ltId.IsNull) poly.LinetypeId = ltId;

            var mat = Matrix3d.Displacement(br.Position - Point3d.Origin)
                      * Matrix3d.Rotation(br.Rotation, Vector3d.ZAxis, Point3d.Origin);
            poly.TransformBy(mat);

            return poly;
        }

        private static void ClearTransients()
        {
            var tm    = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();
            foreach (var ent in _transientsByBlock.Values)
            {
                try { tm.EraseTransient(ent, vpIds); } catch { }
                try { ent.Dispose(); }                  catch { }
            }
            _transientsByBlock.Clear();
            _btrToBlock.Clear();
        }
    }
}
