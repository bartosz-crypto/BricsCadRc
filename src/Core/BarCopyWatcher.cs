using System;
using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    public static class BarCopyWatcher
    {
        private static readonly HashSet<ObjectId> _newBlocks = new HashSet<ObjectId>();
        private static readonly HashSet<ObjectId> _newAnnots = new HashSet<ObjectId>();
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            doc.Database.ObjectAppended += OnObjectAppended;
            doc.CommandEnded            += OnCommandEnded;
            doc.CommandCancelled        += OnCommandCancelled;
            _registered = true;
        }

        public static void Unregister()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc != null)
            {
                try { doc.Database.ObjectAppended -= OnObjectAppended;  } catch { }
                try { doc.CommandEnded            -= OnCommandEnded;    } catch { }
                try { doc.CommandCancelled        -= OnCommandCancelled; } catch { }
            }
            _newBlocks.Clear();
            _newAnnots.Clear();
            _registered = false;
        }

        private static void OnObjectAppended(object sender, ObjectEventArgs e)
        {
            try
            {
                var br = e.DBObject as BlockReference;
                if (br == null) return;

                if (BarBlockEngine.IsBarBlock(br))
                {
                    _newBlocks.Add(br.ObjectId);
                }
                else if (AnnotationEngine.IsAnnotation(br))
                {
                    _newAnnots.Add(br.ObjectId);
                }
            }
            catch { }
        }

        private static void OnCommandEnded(object sender, CommandEventArgs e)
            => HandleCommandFinish(e.GlobalCommandName, "Ended");

        private static void OnCommandCancelled(object sender, CommandEventArgs e)
            => HandleCommandFinish(e.GlobalCommandName, "Cancelled");

        private static void HandleCommandFinish(string cmdRaw, string eventType)
        {
            try
            {
                string cmd = (cmdRaw ?? "").ToUpperInvariant();
                bool isCopyLike =
                       cmd == "COPY" || cmd == "COPYCLIP" || cmd == "PASTECLIP"
                    || cmd == "PASTE" || cmd == "PASTEBLOCK" || cmd == "PASTESPEC"
                    || cmd == "MIRROR" || cmd.StartsWith("ARRAY");

                if (isCopyLike && (_newBlocks.Count > 0 || _newAnnots.Count > 0))
                {
                    RemapCopiedPairs();
                }
            }
            catch { }
            finally
            {
                string cmd = (cmdRaw ?? "").ToUpperInvariant();
                bool shouldClear =
                       cmd == "COPY" || cmd == "COPYCLIP" || cmd == "PASTECLIP"
                    || cmd == "PASTE" || cmd == "PASTEBLOCK" || cmd == "PASTESPEC"
                    || cmd == "MIRROR" || cmd.StartsWith("ARRAY")
                    || cmd == "RC_DISTRIBUTION" || cmd == "RC_BAR_BLOCK" || cmd == "RC_BAR"
                    || cmd.StartsWith("RC_GENERUJ");

                if (shouldClear)
                {
                    _newBlocks.Clear();
                    _newAnnots.Clear();
                }
            }
        }

        private static void RemapCopiedPairs()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;

            // p298+p303: relink'owane pary do normalize + rebuild po commit
            var rebuildPairs = new List<(ObjectId annotId, ObjectId blockId)>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Krok 1: zapewnij niezależne BTR dla każdej skopiowanej pary
                foreach (var id in _newBlocks)
                {
                    if (id.IsNull || id.IsErased) continue;
                    var brBlock = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (brBlock == null) continue;
                    if (!BarBlockEngine.IsBarBlock(brBlock)) continue;
                    EnsureUniqueBtr(tr, db, brBlock);
                }
                foreach (var id in _newAnnots)
                {
                    if (id.IsNull || id.IsErased) continue;
                    var brAnnot = tr.GetObject(id, OpenMode.ForRead) as BlockReference;
                    if (brAnnot == null) continue;
                    if (!AnnotationEngine.IsAnnotation(brAnnot)) continue;
                    EnsureUniqueBtr(tr, db, brAnnot);
                }

                // Krok 2: remap AnnotHandle <-> SourceBlockHandle między kopiami
                foreach (var newBlockId in _newBlocks)
                {
                    if (newBlockId.IsNull || newBlockId.IsErased) continue;

                    var newBlock = tr.GetObject(newBlockId, OpenMode.ForRead) as BlockReference;
                    if (newBlock == null) continue;

                    BarData newBlockData = null;
                    try { newBlockData = BarBlockEngine.ReadXData(newBlock); } catch { }
                    if (newBlockData == null) continue;

                    // Bug B fix: każda kopia rozkładu zwiększa count source bara
                    if (!string.IsNullOrEmpty(newBlockData.SourceBarHandle))
                        PendingLabelUpdates.Add(newBlockData.SourceBarHandle);

                    string oldAnnotHandleStr = newBlockData.AnnotHandle;
                    if (string.IsNullOrEmpty(oldAnnotHandleStr)) continue;

                    if (!TryGetObjectId(db, oldAnnotHandleStr, out var oldAnnotId)) continue;

                    // Jeśli wskazywana annotacja jest już nowa — link jest aktualny, pomijamy
                    if (_newAnnots.Contains(oldAnnotId)) continue;

                    // Stale link — oldAnnot to oryginał. Szukaj pasującej nowej annotacji.
                    var oldAnnot = tr.GetObject(oldAnnotId, OpenMode.ForRead) as BlockReference;
                    if (oldAnnot == null) continue;

                    BarData oldAnnotData = null;
                    try { oldAnnotData = AnnotationEngine.ReadAnnotXData(oldAnnot); } catch { }
                    if (oldAnnotData == null) continue;

                    string oldBlockHandleStr = (oldAnnotData.SourceBlockHandle ?? "").ToUpperInvariant();
                    if (string.IsNullOrEmpty(oldBlockHandleStr)) continue;

                    // Znajdź nową annotację której SourceBlockHandle == oldBlockHandleStr
                    BlockReference matchedNewAnnot     = null;
                    BarData        matchedNewAnnotData = null;
                    foreach (var newAnnotId in _newAnnots)
                    {
                        if (newAnnotId.IsNull || newAnnotId.IsErased) continue;
                        var newAnnot = tr.GetObject(newAnnotId, OpenMode.ForRead) as BlockReference;
                        if (newAnnot == null) continue;

                        BarData newAnnotData = null;
                        try { newAnnotData = AnnotationEngine.ReadAnnotXData(newAnnot); } catch { }
                        if (newAnnotData == null) continue;

                        string sbh = (newAnnotData.SourceBlockHandle ?? "").ToUpperInvariant();
                        if (sbh == oldBlockHandleStr)
                        {
                            matchedNewAnnot     = newAnnot;
                            matchedNewAnnotData = newAnnotData;
                            break;
                        }
                    }

                    if (matchedNewAnnot != null && matchedNewAnnotData != null)
                    {
                        // Relink: nowy block ↔ nowy annot
                        string newBlockHex = newBlock.Handle.Value.ToString("X8");
                        string newAnnotHex = matchedNewAnnot.Handle.Value.ToString("X8");

                        newBlock.UpgradeOpen();
                        newBlockData.AnnotHandle = newAnnotHex;
                        BarBlockEngine.WriteXData(newBlock, newBlockData);

                        matchedNewAnnot.UpgradeOpen();
                        matchedNewAnnotData.SourceBlockHandle = newBlockHex;
                        AnnotationEngine.WriteAnnotXData(matchedNewAnnot, matchedNewAnnotData);
                        rebuildPairs.Add((matchedNewAnnot.ObjectId, newBlockId));
                    }
                    else
                    {
                        // Skopiowano tylko block bez opisu — wyczyść stale AnnotHandle
                        newBlock.UpgradeOpen();
                        newBlockData.AnnotHandle = "";
                        BarBlockEngine.WriteXData(newBlock, newBlockData);
                    }
                }

                tr.Commit();
            }

            // p298+p303: normalize det=-1 -> det=+1, rebuild dist line per pair
            foreach (var (annotId, blockId) in rebuildPairs)
            {
                try
                {
                    // Krok A: normalize ScaleFactors jeśli mirror (det=-1)
                    using (var trNorm = db.TransactionManager.StartTransaction())
                    {
                        var blk = trNorm.GetObject(blockId, OpenMode.ForWrite) as BlockReference;
                        var ann = trNorm.GetObject(annotId, OpenMode.ForWrite) as BlockReference;

                        bool blkNeedsNorm = blk != null && (blk.ScaleFactors.X < 0 || blk.ScaleFactors.Y < 0
                            || System.Math.Abs(System.Math.Abs(blk.Rotation) - System.Math.PI) < 1e-3);
                        if (blkNeedsNorm)
                        {
                            Point3d vMin;
                            try   { vMin = blk.GeometricExtents.MinPoint; }
                            catch { vMin = blk.Position; }
                            blk.ScaleFactors = new Scale3d(1.0, 1.0, 1.0);
                            blk.Rotation     = 0.0;
                            blk.Position     = vMin;
                        }

                        bool annNeedsNorm = ann != null && (ann.ScaleFactors.X < 0 || ann.ScaleFactors.Y < 0
                            || System.Math.Abs(System.Math.Abs(ann.Rotation) - System.Math.PI) < 1e-3);
                        if (annNeedsNorm)
                        {
                            Point3d aVMin;
                            try   { aVMin = ann.GeometricExtents.MinPoint; }
                            catch { aVMin = ann.Position; }
                            ann.ScaleFactors = new Scale3d(1.0, 1.0, 1.0);
                            ann.Rotation     = 0.0;
                            ann.Position     = aVMin;
                        }

                        trNorm.Commit();
                    }

                    // Krok B: rebuild annot BTR (RebuildDistLineInBtr otwiera własną transakcję)
                    using (var trReb = db.TransactionManager.StartTransaction())
                    {
                        var blk = trReb.GetObject(blockId, OpenMode.ForRead) as BlockReference;
                        var ann = trReb.GetObject(annotId, OpenMode.ForWrite) as BlockReference;
                        if (blk != null && ann != null)
                        {
                            var barData = BarBlockEngine.ReadXData(blk);
                            if (barData != null)
                                AnnotationEngine.RebuildDistLineInBtr(ann, barData, db, blk.Position);
                        }
                        trReb.Commit();
                    }
                }
                catch { }
            }
        }

        private static void EnsureUniqueBtr(Transaction tr, Database db, BlockReference br)
        {
            if (br == null) return;
            var oldBtrId = br.BlockTableRecord;
            if (oldBtrId.IsNull) return;

            var oldBtr = tr.GetObject(oldBtrId, OpenMode.ForRead) as BlockTableRecord;
            if (oldBtr == null) return;

            var refIds = oldBtr.GetBlockReferenceIds(true, true);
            if (refIds.Count <= 1) return;  // Już unikalna — nic nie rób

            // Utwórz nową anonimową BTR jako klon starej
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
            var newBtr = new BlockTableRecord
            {
                Name         = "*U",
                Origin       = oldBtr.Origin,
                Units        = oldBtr.Units,
                Explodable   = oldBtr.Explodable,
                BlockScaling = oldBtr.BlockScaling
            };
            var newBtrId = bt.Add(newBtr);
            tr.AddNewlyCreatedDBObject(newBtr, true);

            // Sklonuj zawartość starej BTR do nowej
            var ids = new ObjectIdCollection();
            foreach (ObjectId id in oldBtr)
                if (!id.IsNull && !id.IsErased) ids.Add(id);
            if (ids.Count > 0)
            {
                var map = new IdMapping();
                db.DeepCloneObjects(ids, newBtrId, map, false);
            }

            // Przepnij referencję na nową BTR
            br.UpgradeOpen();
            br.BlockTableRecord = newBtrId;
        }

        private static bool TryGetObjectId(Database db, string handleStr, out ObjectId id)
        {
            id = ObjectId.Null;
            if (string.IsNullOrEmpty(handleStr)) return false;
            try
            {
                long h = Convert.ToInt64(handleStr, 16);
                return db.TryGetObjectId(new Handle(h), out id);
            }
            catch { return false; }
        }

        /// <summary>
        /// True jeśli id należy do świeżo sklonowanego RC_BAR_BLOCK lub RC_BAR_ANNOT
        /// oczekującego na RemapCopiedPairs w CommandEnded.
        /// ATR i BBT pomijają rebuild w tym timing window (BTR shared z oryginałem).
        /// </summary>
        internal static bool IsCopyPending(ObjectId id)
        {
            if (id.IsNull) return false;
            return _newBlocks.Contains(id) || _newAnnots.Contains(id);
        }
    }
}
