using System;
using System.Collections.Generic;
using System.Linq;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    /// <summary>
    /// ObjectOverrule na MLeader z XData RC_BAR_LABEL.
    ///
    /// Po każdej modyfikacji MLeadera sprawdza czy grot strzałki (GetFirstVertex) odsunął się
    /// od skojarzonej polilinii pręta o więcej niż MLeaderSnapGeometry.DefaultTolerance (5 mm).
    /// Jeśli tak — po zakończeniu komendy snapuje grot z powrotem na pręt.
    ///
    /// Mechanizm:
    ///   1. Modified() — kolejkuje ObjectId zmodyfikowanego MLeadera
    ///   2. CommandEnded — przetwarza kolejkę: GetFirstVertex → GetClosestPointTo → SetFirstVertex
    ///
    /// IsApplicable filtruje tylko MLeadery z XData RC_BAR_LABEL (nie wpływa na inne MLeadery).
    /// </summary>
    public sealed class RcMLeaderOverrule : ObjectOverrule
    {
        private static RcMLeaderOverrule _instance;

        private readonly HashSet<ObjectId> _pending = new HashSet<ObjectId>();
        private bool _inSnap;  // zapobiega rekurencji w Modified podczas snapowania

        // ----------------------------------------------------------------
        // Rejestracja / wyrejestrowanie
        // ----------------------------------------------------------------

        public static void Register()
        {
            if (_instance != null) return;
            _instance = new RcMLeaderOverrule();

            ObjectOverrule.AddOverrule(RXObject.GetClass(typeof(MLeader)), _instance, true);
            ObjectOverrule.Overruling = true;

            var inst = _instance;
            DocumentWatch.Subscribe("RcMLeaderOverrule",
                d =>
                {
                    d.Database.ObjectModified += inst.OnObjectModified;
                    d.CommandEnded            += inst.OnCommandEnded;
                    d.CommandCancelled        += inst.OnCommandCancelled;
                },
                d =>
                {
                    try { d.Database.ObjectModified -= inst.OnObjectModified; } catch (System.Exception logEx) { Log.Error("RcMLeaderOverrule.Register", logEx); }
                    d.CommandEnded     -= inst.OnCommandEnded;
                    d.CommandCancelled -= inst.OnCommandCancelled;
                });
        }

        public static void Unregister()
        {
            if (_instance == null) return;
            ObjectOverrule.RemoveOverrule(RXObject.GetClass(typeof(MLeader)), _instance);
            DocumentWatch.Unsubscribe("RcMLeaderOverrule");

            _instance._pending.Clear();
            _instance = null;
        }

        // ----------------------------------------------------------------
        // IsApplicable — filtruj tylko MLeadery z RC_BAR_LABEL XData
        // ----------------------------------------------------------------

        public override bool IsApplicable(RXObject subject)
        {
            var ml = subject as MLeader;
            return ml != null
                && !ml.IsErased
                && ml.GetXDataForApplication(SingleBarEngine.XLabelAppName) != null;
        }

        // ----------------------------------------------------------------
        // ObjectModified event — kolejkuje MLeadery z RC_BAR_LABEL XData do przetworzenia
        // (BRX ObjectOverrule nie eksponuje Modified() override — używamy db.ObjectModified)
        // ----------------------------------------------------------------

        private void OnObjectModified(object sender, ObjectEventArgs e)
        {
            try
            {
                if (_inSnap) return;
                if (!(e.DBObject is MLeader ml) || ml.IsErased || ml.IsUndoing) return;
                // 2026-10-30 (zgłoszenie Bartka: strzałkę opisu pręta dało się odciągnąć od pręta): także MLeader bez / z zerwanym
                // RC_BAR_LABEL — pręt szukany po LabelHandle pręta (SnapArrowToBar), link naprawiany
                _pending.Add(ml.ObjectId);
            }
            catch (System.Exception ex) { Log.Error("RcMLeaderOverrule.OnObjectModified", ex); }
        }

        // ----------------------------------------------------------------
        // CommandEnded — przetwarza kolejkę bezpiecznie po zamknięciu transakcji komendy
        // ----------------------------------------------------------------

        private void OnCommandCancelled(object sender, CommandEventArgs e) => _pending.Clear();

        private void OnCommandEnded(object sender, CommandEventArgs e)
        {
            if (_pending.Count == 0) return;
            var doc = sender as Document ?? Application.DocumentManager.MdiActiveDocument;
            if (doc?.Database == null || DocumentWatch.IsUndoCommand(e.GlobalCommandName))
            {
                _pending.Clear();
                return;
            }
            var db = doc.Database;
            var toProcess = new List<ObjectId>();
            foreach (var id in _pending)
                if (id.Database == db) toProcess.Add(id);
            _pending.Clear();

            _inSnap = true;
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                foreach (var mlId in toProcess)
                {
                    if (mlId.IsErased) continue;
                    // Jeden problematyczny MLeader (np. na zablokowanej warstwie)
                    // nie może zablokować snapowania pozostałych.
                    try { SnapArrowToBar(tr, db, mlId); }
                    catch (System.Exception ex) { Log.Error($"RcMLeaderOverrule.Snap {mlId}", ex); }
                }
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("RcMLeaderOverrule.OnCommandEnded", ex); }
            // uwaga Bartka 2026-10-30 („labele mogę zmieniać jak zwykły tekst — to musi być zablokowane”): tekst opisu pręta zawsze
            // z pręta (liczba z rozkładów + numer pozycji) — edycja tekstu (MTEDIT / dwuklik / właściwości) cofana po komendzie
            int restored = 0;
            try
            {
                var bars = new List<string>();
                using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                {
                    foreach (var mlId in toProcess)
                        if (!mlId.IsErased && tr.GetObject(mlId, OpenMode.ForRead) is MLeader ml
                            && ml.GetXDataForApplication(SingleBarEngine.XLabelAppName) != null
                            && SingleBarEngine.ReadBarHandleFromLabel(ml) is string bh && bh.Length > 0)
                            bars.Add(bh);
                    tr.Commit();
                }
                foreach (var bh in bars.Distinct())
                    if (AnnotationEngine.UpdateBarLabelCount(db, bh)) restored++;
            }
            catch (System.Exception ex) { Log.Error("RcMLeaderOverrule.RestoreText", ex); }
            if (restored > 0)
                doc.Editor.WriteMessage($"\n[RC] Opis pręta zmienia się przez pręt (numer, liczba z rozkładów) — tekst opisu przywrócony ({restored}).\n");
            _inSnap = false;   // oba bloki wyżej łapią wszystkie wyjątki
        }

        // ----------------------------------------------------------------
        // SnapArrowToBar — główna logika: GetFirstVertex → GetClosestPointTo → SetFirstVertex
        // ----------------------------------------------------------------

        private static void SnapArrowToBar(Transaction tr, Database db, ObjectId mlId)
        {
            var ml = tr.GetObject(mlId, OpenMode.ForRead) as MLeader;
            if (ml == null || ml.IsErased) return;

            // Odczytaj handle polilinii pręta z XData; brak / zerwany link (kopia, wklejenie z innego rysunku, stary opis) —
            // pręt, którego LabelHandle wskazuje ten opis, i naprawa linku (2026-10-30)
            var handleStr = SingleBarEngine.ReadBarHandleFromLabel(ml);
            // uwaga Bartka 2026-10-30 („kopiowanie RC_BAR psuje pręt — odłączają się labele”): opis sklonowany w tej komendzie
            // (COPY / MIRROR …) wskazuje jeszcze ORYGINAŁ pręta — grot nie może być dociągany do oryginału; pręt-kopia z mapy klonowania
            // (CloneMap), link przepina potem CloneRemap (BarCopyWatcher). Klon bez sklonowanego pręta — bez zmian.
            bool pendingClone = false;
            try
            {
                if (CloneMap.TryResolvePending(db, mlId, handleStr, out string resolved))
                {
                    pendingClone = true;
                    if (string.IsNullOrEmpty(resolved)) return;
                    handleStr = resolved;
                }
            }
            catch (System.Exception ex) { Log.Error("RcMLeaderOverrule.SnapArrowToBar.Pending", ex); }
            var polyId = string.IsNullOrEmpty(handleStr) ? ObjectId.Null : SingleBarEngine.HandleToObjectId(db, handleStr);
            bool linked = !polyId.IsNull && !polyId.IsErased
                          && BarEntity.From(tr.GetObject(polyId, OpenMode.ForRead) as Entity, BarKinds.Polyline | BarKinds.Block) != null;
            if (!linked && pendingClone) return;
            if (!linked)
            {
                var found = FindBarByLabel(tr, db, ml.Handle.Value.ToString("X8"));
                if (found.IsNull)
                {
                    if (!string.IsNullOrEmpty(handleStr))
                        Log.Info($"RcMLeaderOverrule: opis {ml.Handle} — pręt {handleStr} nie istnieje, brak pręta z tym opisem");
                    return;
                }
                polyId = found;
                ml.UpgradeOpen();
                SingleBarEngine.EnsureAppIdRegistered(db);
                ml.XData = new ResultBuffer(
                    new TypedValue((int)DxfCode.ExtendedDataRegAppName, SingleBarEngine.XLabelAppName),
                    XLink.Write(polyId.Handle.Value.ToString("X8")));
                Log.Info($"RcMLeaderOverrule: opis {ml.Handle} — link do pręta naprawiony ({handleStr ?? "brak"} → {polyId.Handle})");
            }

            var barObj = tr.GetObject(polyId, OpenMode.ForRead);
            var pline  = barObj as Polyline;
            // Plan C, etap 3d: pręt-blok v2 — obrys z BTR w WCS (polilinia jak dotąd)
            var barBlk = pline == null ? BarEntity.From(barObj as Entity, BarKinds.Block) : null;
            if (pline == null && barBlk == null) return;

            // Pobierz grot (first vertex leader line 0)
            Teigha.Geometry.Point3d arrowPt;
            try { arrowPt = ml.GetFirstVertex(0); }
            catch (System.Exception ex)
            {
                Log.Error("RcMLeaderOverrule.SnapArrowToBar.GetFirstVertex", ex);
                return;
            }

            // Oblicz najbliższy punkt na polilinii (BRX uwzględnia łuki i segmenty)
            Teigha.Geometry.Point3d closest;
            if (pline != null)
                closest = pline.GetClosestPointTo(arrowPt, false);
            else
            {
                var cp = barBlk.ClosestPoint(tr, arrowPt);
                if (!cp.HasValue) return;
                closest = cp.Value;
            }

            // Sprawdź tolerancję (przez MLeaderSnapGeometry — spójne z testami)
            var arrowG   = new MLeaderSnapGeometry.Point3(arrowPt.X,  arrowPt.Y,  arrowPt.Z);
            var closestG = new MLeaderSnapGeometry.Point3(closest.X,   closest.Y,   closest.Z);

            if (!MLeaderSnapGeometry.NeedsSnap(arrowG, closestG))
                return;

            // Snapuj grot z powrotem na pręt
            ml.UpgradeOpen();
            ml.SetFirstVertex(0, closest);
        }

        /// <summary>Pręt (polilinia albo pręt-blok), którego [13] LabelHandle wskazuje opis o handle labelHex; brak — Null.</summary>
        private static ObjectId FindBarByLabel(Transaction tr, Database db, string labelHex)
        {
            foreach (var be in BarEntity.ScanModelSpace(tr, db, BarKinds.Polyline | BarKinds.Block))
            {
                var b = be.Read();
                if (b != null && XLink.Same(b.LabelHandle, labelHex)) return be.Entity.ObjectId;
            }
            return ObjectId.Null;
        }
    }
}
