using System.Globalization;
using System.Linq;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Plan C, krok 3f — skutki zmiany wymiarów pręta (pręt-blok: grip końca / STRETCH) dla numeru pozycji, rozkładów
    /// i etykiety. Kopia łańcucha wzorcowego BarGeometryWatcher.ApplyEndStretch (bez zapisu wymiarów i przebudowy obrysu —
    /// to robi wywołujący, np. SingleBarBlock.ApplyEndLength):
    ///   PositionReconciler.ReconcileAfterGeometryChange → PropagateToDistributions(legacyPosNr) →
    ///   grot etykiety (BarEntity.ClosestPoint, tylko z back-linkiem) → AnnotationEngine.UpdateBarLabelCount → komunikat [RC AUTO].
    /// BarGeometryWatcher (pręty-polilinie) bez zmian. Wołać poza dragiem (CommandEnded, nie po UNDO), z zablokowanym dokumentem.
    /// </summary>
    internal static class BarChangePropagator
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Wynik propagacji (do komunikatów i testów).</summary>
        internal sealed class Result
        {
            public bool   Done;            // pręt odczytany, łańcuch wykonany
            public bool   Renumbered;      // pręt dostał inny numer pozycji
            public string NewMark = "";    // Mark po propagacji
            public int    Distributions;   // zaktualizowane rozkłady
            public string Message = "";    // komunikat [RC AUTO] (bez znaków nowej linii)
        }

        /// <summary>
        /// Łańcuch po zmianie wymiarów pręta barId (wymiary i obrys już zapisane). oldMark — Mark sprzed zmiany
        /// (rozkłady bez powiązania [18] brane po starym numerze, gdy pręt nie zmienił numeru). source — skąd zmiana
        /// (np. „grip”, „STRETCH”) — tylko do komunikatu. Komunikat wypisywany w linii poleceń doc (gdy doc != null).
        /// </summary>
        internal static Result Run(Document doc, ObjectId barId, string oldMark, string source)
        {
            var res = new Result();
            if (barId.IsNull || barId.IsErased) return res;
            var db = barId.Database;

            int    oldPosNr   = SingleBarEngine.ExtractPosNr(oldMark);
            string newMark    = PositionReconciler.ReconcileAfterGeometryChange(db, barId);
            bool   renumbered = newMark != null;

            BarData cur;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                cur = BarEntity.Open(tr, barId, OpenMode.ForRead, BarKinds.Polyline | BarKinds.Block)?.Read();
            if (cur == null) return res;

            int nDist = PositionReconciler.PropagateToDistributions(db, barId, cur, legacyPosNr: renumbered ? 0 : oldPosNr);
            FixLabelArrow(db, barId, cur.LabelHandle);
            AnnotationEngine.UpdateBarLabelCount(db, barId.Handle.Value.ToString("X8"), markOverride: cur.Mark);

            string dims = string.Join("/", new[] { cur.LengthA, cur.LengthB, cur.LengthC, cur.LengthD, cur.LengthE }
                                            .Where(v => v > 0).Select(v => v.ToString("F0", Inv)));
            res.Done          = true;
            res.Renumbered    = renumbered;
            res.NewMark       = cur.Mark ?? "";
            res.Distributions = nDist;
            res.Message = renumbered
                ? $"[RC AUTO] Pręt {oldMark} zmieniony ({source}: {dims} mm) → nowa pozycja {cur.Mark}  ({nDist} rozkład(y))"
                : $"[RC AUTO] Pręt {oldMark}: {dims} mm ({source})  ({nDist} rozkład(y))";
            doc?.Editor?.WriteMessage("\n" + res.Message + "\n");
            Log.Info(res.Message + $" (uchwyt {barId.Handle})");
            return res;
        }

        /// <summary>Grot etykiety pręta (tylko gdy etykieta wskazuje z powrotem na ten pręt) → najbliższy punkt obrysu.</summary>
        private static void FixLabelArrow(Database db, ObjectId barId, string labelHandle)
        {
            if (string.IsNullOrEmpty(labelHandle)) return;
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                var lblId = SingleBarEngine.HandleToObjectId(db, labelHandle);
                if (!lblId.IsNull && !lblId.IsErased && tr.GetObject(lblId, OpenMode.ForRead) is MLeader ml
                    && XLink.Same(SingleBarEngine.ReadBarHandleFromLabel(ml), barId.Handle.Value.ToString("X8")))
                {
                    var be = BarEntity.From(tr.GetObject(barId, OpenMode.ForRead) as Entity, BarKinds.Polyline | BarKinds.Block);
                    var leaders = ml.GetLeaderIndexes();
                    if (be != null && leaders != null && leaders.Count > 0)
                    {
                        var lines = ml.GetLeaderLineIndexes((int)leaders[0]);
                        if (lines != null && lines.Count > 0)
                        {
                            int lni = (int)lines[0];
                            Point3d tip = ml.GetFirstVertex(lni);
                            Point3d? nt = be.ClosestPoint(tr, tip);
                            if (nt.HasValue && nt.Value.DistanceTo(tip) > 1e-9)
                            {
                                ml.UpgradeOpen();
                                ml.SetFirstVertex(lni, nt.Value);
                            }
                        }
                    }
                }
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("BarChangePropagator.FixLabelArrow", ex); }
        }
    }
}
