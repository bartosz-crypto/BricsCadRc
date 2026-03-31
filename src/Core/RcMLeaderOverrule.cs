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

            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc?.Database != null)
                {
                    doc.Database.ObjectModified += _instance.OnObjectModified;
                    doc.CommandEnded            += _instance.OnCommandEnded;
                }
            }
            catch { }
        }

        public static void Unregister()
        {
            if (_instance == null) return;
            ObjectOverrule.RemoveOverrule(RXObject.GetClass(typeof(MLeader)), _instance);

            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc?.Database != null)
                    doc.Database.ObjectModified -= _instance.OnObjectModified;
                if (doc != null)
                    doc.CommandEnded -= _instance.OnCommandEnded;
            }
            catch { }

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
                if (!(e.DBObject is MLeader ml) || ml.IsErased) return;
                if (ml.GetXDataForApplication(SingleBarEngine.XLabelAppName) != null)
                    _pending.Add(ml.ObjectId);
            }
            catch { }
        }

        // ----------------------------------------------------------------
        // CommandEnded — przetwarza kolejkę bezpiecznie po zamknięciu transakcji komendy
        // ----------------------------------------------------------------

        private void OnCommandEnded(object sender, CommandEventArgs e)
        {
            if (_pending.Count == 0) return;
            var toProcess = new List<ObjectId>(_pending);
            _pending.Clear();

            var doc = sender as Document ?? Application.DocumentManager.MdiActiveDocument;
            if (doc?.Database == null) return;
            var db = doc.Database;

            _inSnap = true;
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                foreach (var mlId in toProcess)
                {
                    if (mlId.IsErased) continue;
                    SnapArrowToBar(tr, db, mlId);
                }
                tr.Commit();
            }
            catch { /* nie crashuj w event handlerze */ }
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
