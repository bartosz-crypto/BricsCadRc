using System;
using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
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

                var ed = Application.DocumentManager.MdiActiveDocument?.Editor;
                if (BarBlockEngine.IsBarBlock(br))
                {
                    _newBlocks.Add(br.ObjectId);
                    ed?.WriteMessage($"\n[CopyWatcher] ObjAppended BLOCK handle={br.Handle.Value:X} btr={br.BlockTableRecord.Handle.Value:X}");
                }
                else if (AnnotationEngine.IsAnnotation(br))
                {
                    _newAnnots.Add(br.ObjectId);
                    ed?.WriteMessage($"\n[CopyWatcher] ObjAppended ANNOT handle={br.Handle.Value:X}");
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
            var edLog = Application.DocumentManager.MdiActiveDocument?.Editor;
            cmdRaw = cmdRaw ?? "(null)";
            edLog?.WriteMessage($"\n[CopyWatcher] ==> Command{eventType} cmd='{cmdRaw}' queueBlocks={_newBlocks.Count} queueAnnots={_newAnnots.Count}");

            try
            {
                string cmd = cmdRaw.ToUpperInvariant();
                bool isCopyLike =
                       cmd == "COPY" || cmd == "COPYCLIP" || cmd == "PASTECLIP"
                    || cmd == "PASTE" || cmd == "PASTEBLOCK" || cmd == "PASTESPEC"
                    || cmd == "MIRROR" || cmd.StartsWith("ARRAY");

                edLog?.WriteMessage($"\n[CopyWatcher]     isCopyLike={isCopyLike}");

                if (isCopyLike && (_newBlocks.Count > 0 || _newAnnots.Count > 0))
                {
                    edLog?.WriteMessage($"\n[CopyWatcher]     → RemapCopiedPairs()");
                    RemapCopiedPairs();
                }
            }
            catch (System.Exception ex)
            {
                edLog?.WriteMessage($"\n[CopyWatcher] EXC: {ex.Message}");
            }
            finally
            {
                string cmd = cmdRaw.ToUpperInvariant();
                bool shouldClear =
                       cmd == "COPY" || cmd == "COPYCLIP" || cmd == "PASTECLIP"
                    || cmd == "PASTE" || cmd == "PASTEBLOCK" || cmd == "PASTESPEC"
                    || cmd == "MIRROR" || cmd.StartsWith("ARRAY")
                    || cmd == "RC_DISTRIBUTION" || cmd == "RC_BAR_BLOCK" || cmd == "RC_BAR";

                if (shouldClear)
                {
                    edLog?.WriteMessage($"\n[CopyWatcher]     clearing queue");
                    _newBlocks.Clear();
                    _newAnnots.Clear();
                }
                else
                {
                    edLog?.WriteMessage($"\n[CopyWatcher]     (not clearing — not a create-like command)");
                }
            }
        }

        private static void RemapCopiedPairs()
        {
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            var db = doc.Database;

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
        }

        private static void EnsureUniqueBtr(Transaction tr, Database db, BlockReference br)
        {
            if (br == null) return;
            var oldBtrId = br.BlockTableRecord;
            if (oldBtrId.IsNull) return;

            var oldBtr = tr.GetObject(oldBtrId, OpenMode.ForRead) as BlockTableRecord;
            if (oldBtr == null) return;

            var refIds = oldBtr.GetBlockReferenceIds(true, true);
            var ed2 = Application.DocumentManager.MdiActiveDocument?.Editor;
            ed2?.WriteMessage($"\n[EnsureUniqueBtr] br.Handle={br.Handle.Value:X} oldBtrId={oldBtrId.Handle.Value:X} refCount={refIds.Count}");
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
            ed2?.WriteMessage($"\n[EnsureUniqueBtr] SUKCES — br.Handle={br.Handle.Value:X} przepięty na newBtrId={newBtrId.Handle.Value:X}");
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
    }
}
