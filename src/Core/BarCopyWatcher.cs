using System;
using System.Collections.Generic;
using System.Linq;
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
        // Pojedyncze pręty (Polyline RC_SINGLE_BAR) i ich etykiety (MLeader RC_BAR_LABEL)
        private static readonly HashSet<ObjectId> _newBars   = new HashSet<ObjectId>();
        private static readonly HashSet<ObjectId> _newLabels = new HashSet<ObjectId>();
        private static bool _registered;

        public static void Register()
        {
            if (_registered) return;
            DocumentWatch.Subscribe("BarCopyWatcher",
                d =>
                {
                    d.Database.ObjectAppended += OnObjectAppended;
                    d.CommandEnded            += OnCommandEnded;
                    d.CommandCancelled        += OnCommandCancelled;
                },
                d =>
                {
                    try { d.Database.ObjectAppended -= OnObjectAppended; } catch (System.Exception logEx) { Log.Error("BarCopyWatcher.Register", logEx); }
                    d.CommandEnded     -= OnCommandEnded;
                    d.CommandCancelled -= OnCommandCancelled;
                });
            _registered = true;
        }

        public static void Unregister()
        {
            DocumentWatch.Unsubscribe("BarCopyWatcher");
            _newBlocks.Clear();
            _newAnnots.Clear();
            _newBars.Clear();
            _newLabels.Clear();
            _deferredCopyLike.Clear();
            _registered = false;
        }

        private static void OnObjectAppended(object sender, ObjectEventArgs e)
        {
            try
            {
                if (e.DBObject is Polyline pl)
                {
                    if (pl.GetXDataForApplication(SingleBarEngine.XAppName) != null) _newBars.Add(pl.ObjectId);
                    return;
                }
                if (e.DBObject is MLeader ml)
                {
                    if (ml.GetXDataForApplication(SingleBarEngine.XLabelAppName) != null) _newLabels.Add(ml.ObjectId);
                    return;
                }

                var br = e.DBObject as BlockReference;
                if (br == null) return;

                // Plan C, etap 3e: pręt-blok v2 (RC_SINGLE_BAR [14] = 2) — jak pręt-polilinia (przed IsBarBlock)
                if (BarEntity.KindOf(br) == BarKind.Block)
                {
                    _newBars.Add(br.ObjectId);
                    return;
                }

                if (BarBlockEngine.IsBarBlock(br))
                {
                    _newBlocks.Add(br.ObjectId);
                }
                else if (AnnotationEngine.IsAnnotation(br))
                {
                    _newAnnots.Add(br.ObjectId);
                }
            }
            catch (System.Exception ex) { Log.Error("BarCopyWatcher.OnObjectAppended", ex); }
        }

        // Rysunki, w których zakończyła się ZAGNIEŻDŻONA komenda kopiowania — kwalifikacja przechodzi na komendę nadrzędną
        private static readonly HashSet<Database> _deferredCopyLike = new HashSet<Database>();

        /// <summary>Komendy kopiowania (biała lista EnsureUniqueBtr i normalizacji par). W2 (3b): także -ARRAY.</summary>
        private static bool IsCopyLikeCommand(string cmdRaw)
        {
            string cmd = (cmdRaw ?? "").Trim().TrimStart('\'').ToUpperInvariant();
            return cmd == "COPY" || cmd == "COPYCLIP" || cmd == "PASTECLIP"
                || cmd == "PASTE" || cmd == "PASTEBLOCK" || cmd == "PASTESPEC"
                || cmd == "PASTEORIG" || cmd == "MIRROR" || cmd.TrimStart('-').StartsWith("ARRAY");
        }

        private static void OnCommandEnded(object sender, CommandEventArgs e)
            => HandleCommandFinish(sender as Document, e.GlobalCommandName);

        private static void OnCommandCancelled(object sender, CommandEventArgs e)
            => HandleCommandFinish(sender as Document, e.GlobalCommandName);

        private static void HandleCommandFinish(Document doc, string cmdRaw)
        {
            // W2 (0-POPRAWKI p.5): komenda widoku / przezroczysta albo zagnieżdżona (np. 'ZOOM w trakcie COPY) —
            // bez przepinania i bez czyszczenia: paczki CloneMap i zbiory nowych obiektów zostają do końca komendy nadrzędnej.
            // Zagnieżdżona komenda kopiowania (np. COPY / MIRROR wywołane z komendy nadrzędnej) — czynności komend
            // kopiowania (unikalne BTR, normalizacja par) wykonywane po zakończeniu komendy nadrzędnej.
            try
            {
                if (doc != null && (SingleBarGripOverrule.IsViewOrTransparentCommand(cmdRaw)
                                    || SingleBarGripOverrule.OtherCommandInProgress(doc, cmdRaw ?? "", out _)))
                {
                    if (doc.Database != null && IsCopyLikeCommand(cmdRaw)) _deferredCopyLike.Add(doc.Database);
                    return;
                }
            }
            catch (System.Exception ex) { Log.Error("BarCopyWatcher.HandleCommandFinish.Nested", ex); }

            try
            {
                string cmd = (cmdRaw ?? "").ToUpperInvariant();
                var db = doc?.Database;
                bool deferredCopyLike = db != null && _deferredCopyLike.Remove(db);

                // 1) Po U/UNDO/REDO nic nie przepinamy (każda zmiana kasuje REDO) — paczki odrzucone
                if (DocumentWatch.IsUndoCommand(cmd))
                {
                    CloneMap.Forget(db);
                    return;
                }

                bool isCopyLike = IsCopyLikeCommand(cmd) || deferredCopyLike;
                LastSameNumberCopyMessages = new List<string>();
                // e7b600d (b): nowe pręty zapamiętane przed krokiem 5 (RemapCopiedBarLabels zawęża _newBars)
                var copiedBars = isCopyLike ? new List<ObjectId>(_newBars) : null;

                // 2) Mapa klonowania tej komendy (W2, docs/PLAN_POPRAWKI_W1_W2.md 4.4)
                var batches = CloneMap.Take(db);
                if (db == null) return;

                var mapped = new HashSet<ObjectId>();
                using (CloneMap.Suppress())   // DeepCloneObjects w EnsureUniqueBtr nie tworzy paczek
                {
                    // 3) Własne BTR kopii: rozkłady, opisy, pręty-bloki *U (biała lista komend kopiowania — jak dotąd)
                    if (isCopyLike) EnsureUniqueBtrs(db);

                    // 4) Przepięcie z mapy — po każdej komendzie, która sklonowała obiekty (pyt. 5)
                    if (batches.Count > 0) mapped = CloneRemap.Apply(db, batches, isCopyLike);

                    // 5) i 6) Heurystyki — tylko zapas dla obiektów spoza mapy
                    if (isCopyLike && (_newBlocks.Count > 0 || _newAnnots.Count > 0))
                        RemapCopiedPairs(db, mapped);
                    if (isCopyLike && _newBars.Count > 0)
                        RemapCopiedBarLabels(db, mapped);
                }

                // 7) Komunikat o kopiach pręta z tym samym numerem pozycji (e7b600d (b)) — tylko odczyt
                if (isCopyLike && copiedBars.Count > 0) ReportSameNumberCopies(doc, db, copiedBars);
            }
            catch (System.Exception ex) { Log.Error($"BarCopyWatcher.HandleCommandFinish {cmdRaw}", ex); }
            finally
            {
                // Czyść ZAWSZE po każdej komendzie. Wcześniej tylko po białej liście komend —
                // zalegające ID (np. po INSERT, RC_EDIT_*) powodowały, że IsCopyPending()
                // zwracało true i MOVE nie przebudowywał linii rozkładu.
                _newBlocks.Clear();
                _newAnnots.Clear();
                _newBars.Clear();
                _newLabels.Clear();
            }
        }

        /// <summary>
        /// Naprawia powiązanie pręt ↔ etykieta po COPY / PASTE / MIRROR / ARRAY (heurystyka wzorca).
        /// BricsCAD V25 przy COPY / MIRROR / ARRAY NIE przemapowuje 1005 (spike W2-0) — kopie wskazują ORYGINAŁ;
        /// od W2 przepina je mapa klonowania (CloneRemap), ta metoda jest zapasem dla obiektów spoza mapy.
        /// Parowanie: grot etykiety leży na pręcie. Kopia pręta bez etykiety → powiązanie czyszczone.
        /// W2: pręty i etykiety przepięte z mapy klonowania (mapped) są pomijane — heurystyka tylko jako zapas.
        /// </summary>
        private static void RemapCopiedBarLabels(Database db, HashSet<ObjectId> mapped)
        {
            _newBars.RemoveWhere(id => id.Database != db || id.IsErased || mapped.Contains(id));
            _newLabels.RemoveWhere(id => id.Database != db || id.IsErased || mapped.Contains(id));
            if (_newBars.Count == 0) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Etykiety z nowych obiektów: id → grot
                var labelTips = new Dictionary<ObjectId, Point3d>();
                foreach (var lid in _newLabels)
                {
                    var ml = tr.GetObject(lid, OpenMode.ForRead) as MLeader;
                    if (ml == null) continue;
                    if (TryGetArrowTip(ml, out var tip)) labelTips[lid] = tip;
                }
                var usedLabels = new HashSet<ObjectId>();

                // Plan C, etap 3e (poprawka): gdy wśród kopii są pręty-bloki —
                // 1) PRZED parowaniem geometrycznym rezerwacja etykiet z poprawnym dwustronnym powiązaniem
                //    (pręt [13] → MLeader, back-link MLeadera → ten pręt) — nie mogą trafić do innego pręta;
                // 2) parowanie po POCHODZENIU, nie po odległości: BricsCAD przy COPY nie przemapowuje 1005 (sprawdzone:
                //    [13] i [18] kopii wskazują oryginał), więc kopia pręta C ma [13] = etykieta oryginału L_X, a kopia
                //    etykiety — back-link = oryginalny pręt X. Źródło C: X = back-link(L_X), spójne, gdy X.[13] = L_X.
                //    Kandydaci dla C: tylko kopie etykiet z tego zbioru z back-linkiem = X. Brak / niespójne źródło →
                //    C bez etykiety (czyste [13]), nigdy etykieta innego źródła. Odległość grota (< 50 mm) rozdziela
                //    tylko kilka kopii tego samego źródła (ARRAY, COPY wielokrotne); remis (< 1e-6) → bez przypisania.
                // Zbiór samych polilinii — bez zmian (parowanie w kolejności prętów, wynik bit w bit jak wzorzec aba0ed1).
                bool anyBlockBar = false;
                foreach (var bid in _newBars)
                    if (BarEntity.KindOf(tr.GetObject(bid, OpenMode.ForRead) as Entity) == BarKind.Block) { anyBlockBar = true; break; }
                Dictionary<ObjectId, ObjectId> assigned = null;
                if (anyBlockBar)
                {
                    var linked = new HashSet<ObjectId>();
                    foreach (var bid in _newBars)
                    {
                        var rb = BarEntity.From(tr.GetObject(bid, OpenMode.ForRead) as Entity, BarKinds.Polyline | BarKinds.Block)?.Read();
                        if (rb == null) continue;
                        if (TryGetObjectId(db, rb.LabelHandle, out var rl) && !rl.IsErased
                            && tr.GetObject(rl, OpenMode.ForRead) is MLeader rml
                            && XLink.Same(SingleBarEngine.ReadBarHandleFromLabel(rml), bid.Handle.Value.ToString("X8")))
                        {
                            usedLabels.Add(rl);
                            linked.Add(bid);
                        }
                    }

                    // Kopie etykiet według źródła (back-link = oryginalny pręt)
                    var labelsBySrc = new Dictionary<string, List<ObjectId>>(System.StringComparer.Ordinal);
                    foreach (var kv in labelTips)
                    {
                        if (usedLabels.Contains(kv.Key)) continue;
                        if (!(tr.GetObject(kv.Key, OpenMode.ForRead) is MLeader lm)) continue;
                        string src = XLink.Norm(SingleBarEngine.ReadBarHandleFromLabel(lm));
                        if (src.Length == 0) continue;
                        if (!labelsBySrc.TryGetValue(src, out var lst)) labelsBySrc[src] = lst = new List<ObjectId>();
                        lst.Add(kv.Key);
                    }

                    var pairs = new List<(double d, ObjectId bar, ObjectId label)>();
                    foreach (var bid in _newBars)
                    {
                        if (linked.Contains(bid)) continue;
                        var pent = tr.GetObject(bid, OpenMode.ForRead) as Entity;
                        var pbe  = BarEntity.From(pent, BarKinds.Polyline | BarKinds.Block);
                        var rb   = pbe?.Read();
                        if (rb == null) continue;
                        string src = SourceOfCopy(tr, db, rb.LabelHandle);
                        if (src == null || !labelsBySrc.TryGetValue(src, out var cands)) continue;
                        var ppl  = pent as Polyline;
                        var pblk = ppl == null ? pbe : null;
                        foreach (var lid in cands)
                        {
                            double? pd = TipDistance(tr, ppl, pblk, labelTips[lid]);
                            if (pd.HasValue && pd.Value < 50.0) pairs.Add((pd.Value, bid, lid));
                        }
                    }

                    // Najbliższe pary pierwsze, każdy pręt i każda etykieta najwyżej raz; remis dla tego samego
                    // pręta albo etykiety → bez przypisania (nie rozstrzygamy kolejnością)
                    assigned = new Dictionary<ObjectId, ObjectId>();
                    var takenBars   = new HashSet<ObjectId>();
                    var takenLabels = new HashSet<ObjectId>();
                    var sorted = pairs.OrderBy(x => x.d).ToList();
                    foreach (var p in sorted)
                    {
                        if (takenBars.Contains(p.bar) || takenLabels.Contains(p.label)) continue;
                        bool tie = sorted.Any(q => (q.bar != p.bar || q.label != p.label)
                                                   && (q.bar == p.bar || q.label == p.label)
                                                   && !takenBars.Contains(q.bar) && !takenLabels.Contains(q.label)
                                                   && System.Math.Abs(q.d - p.d) < 1e-6);
                        takenBars.Add(p.bar);
                        takenLabels.Add(p.label);
                        if (tie)
                        {
                            Log.Info($"BarCopyWatcher: niejednoznaczne parowanie pręt {p.bar.Handle} – etykieta {p.label.Handle} (remis odległości) — bez przypisania.");
                            continue;
                        }
                        assigned[p.bar] = p.label;
                    }
                }

                foreach (var bid in _newBars)
                {
                    var ent = tr.GetObject(bid, OpenMode.ForRead) as Entity;
                    var pl  = ent as Polyline;
                    // Plan C, etap 3e: pręt-blok v2 przez BarEntity (polilinia jak dotąd)
                    var blk = pl == null ? BarEntity.From(ent, BarKinds.Block) : null;
                    var bar = pl != null ? SingleBarEngine.ReadBarXData(pl) : blk?.Read();
                    if (bar == null) continue;
                    string myHex = bid.Handle.Value.ToString("X8");

                    // Czy obecne powiązanie jest poprawne (etykieta wskazuje z powrotem na ten pręt)?
                    if (TryGetObjectId(db, bar.LabelHandle, out var curLbl) && !curLbl.IsErased
                        && tr.GetObject(curLbl, OpenMode.ForRead) is MLeader curMl
                        && XLink.Same(SingleBarEngine.ReadBarHandleFromLabel(curMl), myHex))
                    {
                        usedLabels.Add(curLbl);
                        continue;
                    }

                    // Szukaj nowej etykiety, której grot leży na tym pręcie
                    ObjectId best = ObjectId.Null;
                    double bestD = 50.0;   // mm
                    if (assigned != null)
                        best = assigned.TryGetValue(bid, out var asg) ? asg : ObjectId.Null;   // pręty-bloki w zbiorze: parowanie globalne
                    else   // zbiór samych polilinii — jak wzorzec (pierwsza w kolejności prętów)
                    foreach (var kv in labelTips)
                    {
                        if (usedLabels.Contains(kv.Key)) continue;
                        double d;
                        try
                        {
                            if (pl != null)
                                d = pl.GetClosestPointTo(kv.Value, false).DistanceTo(kv.Value);
                            else
                            {
                                var cp = blk.ClosestPoint(tr, kv.Value);
                                if (!cp.HasValue) continue;
                                d = cp.Value.DistanceTo(kv.Value);
                            }
                        }
                        catch (System.Exception ex)
                        {
                            Log.Error("BarCopyWatcher.RemapCopiedBarLabels.ClosestPoint", ex);
                            continue;
                        }
                        if (d < bestD) { bestD = d; best = kv.Key; }
                    }

                    if (pl != null) pl.UpgradeOpen();
                    if (!best.IsNull)
                    {
                        usedLabels.Add(best);
                        bar.LabelHandle = best.Handle.Value.ToString("X8");
                        WriteBar(pl, blk, bar);

                        var ml = (MLeader)tr.GetObject(best, OpenMode.ForWrite);
                        ml.XData = new ResultBuffer(
                            new TypedValue((int)DxfCode.ExtendedDataRegAppName, SingleBarEngine.XLabelAppName),
                            XLink.Write(myHex));
                    }
                    else if (!string.IsNullOrEmpty(bar.LabelHandle))
                    {
                        // Pręt skopiowany bez etykiety — nie może wskazywać na etykietę oryginału
                        bar.LabelHandle = "";
                        WriteBar(pl, blk, bar);
                    }
                }
                tr.Commit();
            }
        }

        /// <summary>
        /// Źródło kopii pręta (Plan C, etap 3e): [13] kopii = etykieta oryginału L_X, X = back-link(L_X).
        /// Spójne, gdy X istnieje, jest prętem i jego [13] wskazuje L_X. Zwraca X („X8”) albo null.
        /// </summary>
        private static string SourceOfCopy(Transaction tr, Database db, string labelHandle)
        {
            if (!TryGetObjectId(db, labelHandle, out var lx) || lx.IsNull || lx.IsErased) return null;
            if (!(tr.GetObject(lx, OpenMode.ForRead) is MLeader ml)) return null;
            string x = XLink.Norm(SingleBarEngine.ReadBarHandleFromLabel(ml));
            if (x.Length == 0 || !TryGetObjectId(db, x, out var xid) || xid.IsNull || xid.IsErased) return null;
            var xb = BarEntity.From(tr.GetObject(xid, OpenMode.ForRead) as Entity, BarKinds.Polyline | BarKinds.Block)?.Read();
            if (xb == null || !XLink.Same(xb.LabelHandle, lx.Handle.Value.ToString("X8"))) return null;
            return x;
        }

        /// <summary>Odległość grota od obrysu pręta (polilinia albo pręt-blok w WCS); null przy braku obrysu / błędzie.</summary>
        private static double? TipDistance(Transaction tr, Polyline pl, BarEntity blk, Point3d tip)
        {
            try
            {
                if (pl != null) return pl.GetClosestPointTo(tip, false).DistanceTo(tip);
                var cp = blk?.ClosestPoint(tr, tip);
                return cp.HasValue ? cp.Value.DistanceTo(tip) : (double?)null;
            }
            catch (System.Exception ex)
            {
                Log.Error("BarCopyWatcher.TipDistance", ex);
                return null;
            }
        }

        /// <summary>Zapis rekordu pręta: polilinia — BarXData.Write (jak dotąd); pręt-blok — BarEntity.Write ([14] = 2).</summary>
        private static void WriteBar(Polyline pl, BarEntity blk, BarData bar)
        {
            if (pl != null)
            {
                BarXData.Write(pl, bar);
                return;
            }
            if (!blk.Entity.IsWriteEnabled) blk.Entity.UpgradeOpen();
            blk.Write(bar);
        }

        /// <summary>Komunikaty ostatniej komendy najwyższego poziomu (ReportSameNumberCopies) — do testów BRX.</summary>
        internal static List<string> LastSameNumberCopyMessages { get; private set; } = new List<string>();

        /// <summary>
        /// e7b600d (b): kopia pręta o identycznych wymiarach ma ten sam numer pozycji co istniejący pręt (jeden numer =
        /// jeden kształt) — komunikat w linii poleceń, że kopia liczy się do tej samej pozycji. Polilinie i pręty-bloki
        /// (<see cref="BarEntity"/>), tylko ten rysunek, bez prętów bez numeru, usunięte (MIRROR z usunięciem) pomijane.
        /// Tylko odczyt.
        /// </summary>
        private static void ReportSameNumberCopies(Document doc, Database db, List<ObjectId> copiedBars)
        {
            try
            {
                var copySet = new HashSet<ObjectId>(copiedBars.Where(id => !id.IsNull && id.Database == db && !id.IsErased));
                if (copySet.Count == 0) return;
                var copies = new List<BarData>();
                var others = new List<BarData>();
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    foreach (var be in BarEntity.ScanModelSpace(tr, db, BarKinds.Polyline | BarKinds.Block))
                    {
                        var bar = be.Read();
                        if (bar == null) continue;
                        (copySet.Contains(be.Entity.ObjectId) ? copies : others).Add(bar);
                    }
                    tr.Commit();
                }
                var msgs = IdenticalBarText.SameNumberCopyMessages(copies, others, b => b.Mark,
                    b => SingleBarEngine.ExtractPosNr(b.Mark), PositionReconciler.SameShape);
                LastSameNumberCopyMessages = msgs;
                foreach (var m in msgs) doc?.Editor?.WriteMessage("\n" + m);
                if (msgs.Count > 0) doc?.Editor?.WriteMessage("\n");
            }
            catch (System.Exception ex) { Log.Error("BarCopyWatcher.ReportSameNumberCopies", ex); }
        }

        private static bool TryGetArrowTip(MLeader ml, out Point3d tip)
        {
            tip = Point3d.Origin;
            try
            {
                var li = ml.GetLeaderIndexes();
                if (li == null || li.Count == 0) return false;
                var lni = ml.GetLeaderLineIndexes((int)li[0]);
                if (lni == null || lni.Count == 0) return false;
                tip = ml.GetFirstVertex((int)lni[0]);
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Error("BarCopyWatcher.TryGetArrowTip", ex);
                return false;
            }
        }

        /// <summary>
        /// Własne BTR kopii (krok 3 planu 4.4, przed przepięciem): rozkłady i opisy (jak dawny krok 1 RemapCopiedPairs)
        /// oraz pręty-bloki z BTR *U (Plan C, etap 3e — klon zachowuje Explodable). Nazwane BTR prętów testowych etapu 1
        /// (RC_BAR_TEST_nnn) — bez zmian: wspólny BTR do pierwszej edycji, potem copy-on-write w SingleBarBlock.
        /// </summary>
        private static void EnsureUniqueBtrs(Database db)
        {
            // Tylko obiekty z rysunku, w którym zakończyła się komenda
            _newBlocks.RemoveWhere(id => id.Database != db);
            _newAnnots.RemoveWhere(id => id.Database != db);
            if (_newBlocks.Count == 0 && _newAnnots.Count == 0 && _newBars.Count == 0) return;

            using (var tr = db.TransactionManager.StartTransaction())
            {
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
                foreach (var bid in _newBars)
                {
                    if (bid.IsNull || bid.IsErased || bid.Database != db) continue;
                    if (!(tr.GetObject(bid, OpenMode.ForRead) is BlockReference bbr)) continue;
                    if (BarEntity.KindOf(bbr) != BarKind.Block) continue;
                    if (!(tr.GetObject(bbr.BlockTableRecord, OpenMode.ForRead) is BlockTableRecord bbtr) || !bbtr.IsAnonymous) continue;
                    EnsureUniqueBtr(tr, db, bbr);
                }
                tr.Commit();
            }
        }

        /// <summary>
        /// Heurystyczne przepięcie par rozkład ↔ opis (wzorzec). W2: tylko rozkłady spoza mapy klonowania (mapped) —
        /// pary z mapy przepina CloneRemap (z tą samą normalizacją i przebudową opisu, NormalizeAndRebuildPair).
        /// </summary>
        private static void RemapCopiedPairs(Database db, HashSet<ObjectId> mapped)
        {
            // Tylko obiekty z rysunku, w którym zakończyła się komenda
            _newBlocks.RemoveWhere(id => id.Database != db);
            _newAnnots.RemoveWhere(id => id.Database != db);

            // p298+p303: relink'owane pary do normalize + rebuild po commit
            var rebuildPairs = new List<(ObjectId annotId, ObjectId blockId)>();

            using (var tr = db.TransactionManager.StartTransaction())
            {
                // Krok 1 (własne BTR) — EnsureUniqueBtrs, przed przepięciem z mapy.
                // Krok 2: remap AnnotHandle <-> SourceBlockHandle między kopiami
                foreach (var newBlockId in _newBlocks)
                {
                    if (newBlockId.IsNull || newBlockId.IsErased) continue;
                    if (mapped.Contains(newBlockId)) continue;   // W2: przepięty z mapy

                    var newBlock = tr.GetObject(newBlockId, OpenMode.ForRead) as BlockReference;
                    if (newBlock == null) continue;

                    BarData newBlockData = null;
                    try { newBlockData = BarBlockEngine.ReadXData(newBlock); }
                    catch (System.Exception ex) { Log.Error("BarCopyWatcher.RemapCopiedPairs.ReadXData", ex); }
                    if (newBlockData == null) continue;

                    // Bug B fix: każda kopia rozkładu zwiększa count source bara
                    if (!string.IsNullOrEmpty(newBlockData.SourceBarHandle))
                        PendingLabelUpdates.Add(db, newBlockData.SourceBarHandle);

                    string oldAnnotHandleStr = newBlockData.AnnotHandle;
                    if (string.IsNullOrEmpty(oldAnnotHandleStr)) continue;

                    if (!TryGetObjectId(db, oldAnnotHandleStr, out var oldAnnotId) || oldAnnotId.IsErased) continue;

                    // Jeśli wskazywana annotacja jest już nowa — link jest aktualny, pomijamy
                    if (_newAnnots.Contains(oldAnnotId)) continue;

                    // Stale link — oldAnnot to oryginał. Szukaj pasującej nowej annotacji.
                    var oldAnnot = tr.GetObject(oldAnnotId, OpenMode.ForRead) as BlockReference;
                    if (oldAnnot == null) continue;

                    BarData oldAnnotData = null;
                    try { oldAnnotData = AnnotationEngine.ReadAnnotXData(oldAnnot); }
                    catch (System.Exception ex) { Log.Error("BarCopyWatcher.RemapCopiedPairs.ReadAnnot", ex); }
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
                        try { newAnnotData = AnnotationEngine.ReadAnnotXData(newAnnot); }
                        catch (System.Exception ex) { Log.Error("BarCopyWatcher.RemapCopiedPairs.ReadNewAnnot", ex); }
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
                NormalizeAndRebuildPair(db, annotId, blockId);
        }

        /// <summary>
        /// Końcowa normalizacja i przebudowa pary rozkład ↔ opis (p298+p303): odbicie (det = −1) → skala +1, przebudowa
        /// linii rozkładu w BTR opisu. Wspólna dla heurystyki (RemapCopiedPairs) i pary z mapy (CloneRemap, 0-POPRAWKI p.1).
        /// </summary>
        internal static void NormalizeAndRebuildPair(Database db, ObjectId annotId, ObjectId blockId)
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
                        // 2026-10-13: odbicie (lokalne osie bloku po MIRROR) — po normalizacji do +1 dane rozkładu muszą to odbicie
                        // przejąć (gripy krawędzi, symbol końca, kolejność prętów); wcześniej gripy zostawały po starej stronie
                        var ex = Vector3d.XAxis.TransformBy(blk.BlockTransform);
                        var ey = Vector3d.YAxis.TransformBy(blk.BlockTransform);
                        bool flipX = ex.X < -0.5, flipY = ey.Y < -0.5;
                        var bd = BarBlockEngine.ReadXData(blk);
                        bool alongX = bd?.Direction == "X";
                        Point3d vMin;
                        try   { vMin = blk.GeometricExtents.MinPoint; }
                        catch { vMin = blk.Position; }
                        if (bd != null && Math.Abs(bd.SkewStart) < 1e-6 && Math.Abs(bd.SkewEnd) < 1e-6)
                        {
                            // naroże prostokąta prętów (bez symboli końców — kółko / hak poszerza obwiednię o promień)
                            double la = bd.LengthA, ls = bd.BarsSpan;
                            var loc = alongX
                                ? new[] { new Point3d(0, 0, 0), new Point3d(la, 0, 0), new Point3d(0, ls, 0), new Point3d(la, ls, 0) }
                                : new[] { new Point3d(0, 0, 0), new Point3d(ls, 0, 0), new Point3d(0, la, 0), new Point3d(ls, la, 0) };
                            var w = loc.Select(q => q.TransformBy(blk.BlockTransform)).ToList();
                            vMin = new Point3d(w.Min(q => q.X), w.Min(q => q.Y), blk.Position.Z);
                        }
                        blk.ScaleFactors = new Scale3d(1.0, 1.0, 1.0);
                        blk.Rotation     = 0.0;
                        blk.Position     = vMin;
                        if (bd != null)
                            BarBlockEngine.ApplyMirror(blk, flipAlong: alongX ? flipX : flipY, flipAcross: alongX ? flipY : flipX);
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
            catch (System.Exception ex) { Log.Error("BarCopyWatcher.NormalizeAndRebuildPair", ex); }
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
            catch (System.Exception ex)
            {
                Log.Error("BarCopyWatcher.TryGetObjectId", ex);
                return false;
            }
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
