using System;
using System.Collections.Generic;
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
                if (ml.GetXDataForApplication(SingleBarEngine.XLabelAppName) != null)
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
            finally
            {
                _inSnap = false;
            }
        }

        // ----------------------------------------------------------------
        // SnapArrowToBar — główna logika: GetFirstVertex → GetClosestPointTo → SetFirstVertex
        // ----------------------------------------------------------------

        private static void SnapArrowToBar(Transaction tr, Database db, ObjectId mlId)
        {
            var ml = tr.GetObject(mlId, OpenMode.ForRead) as MLeader;
            if (ml == null || ml.IsErased) return;

            // Odczytaj handle polilinii pręta z XData
            var handleStr = SingleBarEngine.ReadBarHandleFromLabel(ml);
            if (string.IsNullOrEmpty(handleStr)) return;

            var polyId = SingleBarEngine.HandleToObjectId(db, handleStr);
            if (polyId.IsNull || polyId.IsErased) return;

            var pline = tr.GetObject(polyId, OpenMode.ForRead) as Polyline;
            if (pline == null) return;

            // Pobierz grot (first vertex leader line 0)
            Teigha.Geometry.Point3d arrowPt;
            try { arrowPt = ml.GetFirstVertex(0); }
            catch { return; }

            // Oblicz najbliższy punkt na polilinii (BRX uwzględnia łuki i segmenty)
            var closest = pline.GetClosestPointTo(arrowPt, false);

            // Sprawdź tolerancję (przez MLeaderSnapGeometry — spójne z testami)
            var arrowG   = new MLeaderSnapGeometry.Point3(arrowPt.X,  arrowPt.Y,  arrowPt.Z);
            var closestG = new MLeaderSnapGeometry.Point3(closest.X,   closest.Y,   closest.Z);

            if (!MLeaderSnapGeometry.NeedsSnap(arrowG, closestG))
                return;

            // Snapuj grot z powrotem na pręt
            ml.UpgradeOpen();
            ml.SetFirstVertex(0, closest);
        }
    }
}
