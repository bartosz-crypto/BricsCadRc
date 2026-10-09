using System.Globalization;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// RC_EDIT_BAR po dialogu (Plan C, etap 3d) — wydzielone z BarCommands.EditBar bez zmiany kolejności kroków:
    /// Mark z numeru z dialogu + pola niezmienne → zapis XData → numer pozycji (PositionReconciler) → przebudowa
    /// geometrii → grot etykiety → liczba na etykiecie → rozkłady.
    ///
    /// Polilinia: kroki wzorca 1:1 (RebuildCompanions, GetClosestPointTo polilinii); przebudowa w ramie pręta sprzed
    /// edycji (poprawka #4 — obrót i odbicie zachowane), całość w BarGeometryWatcher.Suppress (poprawka #1).
    /// Pręt-blok v2: zapis przez BarEntity (zachowuje [14] = 2), przebudowa SingleBarBlock.Rebuild (copy-on-write,
    /// punkt wstawienia = axis[0] i obrót zachowane), grot przez BarEntity.ClosestPoint.
    /// Używane też przez tools/RegressionDump (RC_REG_RECONCILE, refleksja) — bez dialogów i bez REGEN.
    /// </summary>
    internal static class BarEditApply
    {
        /// <param name="bar">Dane pręta przed edycją (z XData).</param>
        /// <param name="updated">Wynik dialogu (BarElevationDialog.Result) — uzupełniany i zapisywany.</param>
        /// <param name="posNrStr">Numer pozycji z dialogu (ResultPosNr, np. „09”).</param>
        /// <returns>false, gdy pręta nie da się otworzyć do zapisu (nic nie zmieniono).</returns>
        /// <remarks>
        /// Poprawka #1: całość w BarGeometryWatcher.Suppress() — przebudowa obrysu nie wraca w CommandEnded jako
        /// grip na końcu (wzorzec doliczał zmianę długości drugi raz). Kolejność kroków bez zmian.
        /// </remarks>
        internal static bool Apply(Database db, Editor ed, ObjectId editId, BarData bar, BarData updated, string posNrStr)
        {
            BarGeometryWatcher.IgnoreThisCommand(editId);   // także gdy pręt był w kolejce watchera wcześniej w tej komendzie
            using (BarGeometryWatcher.Suppress())
                return ApplyCore(db, ed, editId, bar, updated, posNrStr);
        }

        private static bool ApplyCore(Database db, Editor ed, ObjectId editId, BarData bar, BarData updated, string posNrStr)
        {
            // Zachowaj niezmienne pola z oryginału
            // Przebuduj Mark z nową średnicą i posNr z dialogu — format H{dia}-{posNr}
            updated.Mark        = BarData.FormatMark(updated.Diameter, int.Parse(posNrStr, CultureInfo.InvariantCulture), 0, 1);
            updated.LayerCode   = bar.LayerCode;
            updated.Position    = bar.Position;
            updated.Direction   = bar.Direction;
            updated.LabelHandle = bar.LabelHandle;

            // Rodzaj pręta (odczyt bez otwierania do zapisu — ścieżka polilinii bez zmian)
            bool isBlock;
            BarFrame? frame = null;
            Point3d? ringStart = null;
            using (var trK = db.TransactionManager.StartOpenCloseTransaction())
            {
                var entK = trK.GetObject(editId, OpenMode.ForRead) as Entity;
                isBlock = BarEntity.KindOf(entK) == BarKind.Block;
                // Poprawka #4: rama pręta-polilinii (obrót, odbicie) z obrysu PRZED zmianą i danych sprzed edycji —
                // przebudowa zachowuje kierunek i odbicie (także przy zmianie kształtu). Pręt poziomy → tożsamość.
                if (!isBlock && entK is Polyline plK)
                {
                    frame = SingleBarEngine.FrameFromOutline(SingleBarEngine.OutlinePoints(plK), bar);
                    // Poprawka #5: zmiana kształtu NA pierścień — pierścień zaczyna się w axis[0] starego pręta
                    if (SingleBarEngine.IsRingShape(updated) && !SingleBarEngine.IsRingShape(bar))
                        ringStart = SingleBarEngine.GetAxisFirstPointFromOutline(plK, bar.ShapeCode ?? "00");
                }
            }

            // Krok 3 — zapisz XData i przebuduj geometrię
            using (var tr2 = db.TransactionManager.StartTransaction())
            {
                if (isBlock)
                {
                    var be = BarEntity.Open(tr2, editId, OpenMode.ForWrite, BarKinds.Block);
                    if (be == null) { tr2.Abort(); return false; }
                    be.Write(updated);
                }
                else
                {
                    var plineRw = tr2.GetObject(editId, OpenMode.ForWrite) as Polyline;
                    if (plineRw == null) { tr2.Abort(); return false; }
                    BarXData.Write(plineRw, updated);
                }
                tr2.Commit();
            }

            // Krok 3a — numer pozycji zostawiony bez zmian, ale wymiary zmienione, a ten sam numer
            // ma inny pręt (np. kopia) → ten pręt dostaje numer pasującej pozycji albo nowy.
            if (int.Parse(posNrStr, CultureInfo.InvariantCulture) == SingleBarEngine.ExtractPosNr(bar.Mark)
                && !PositionReconciler.SameShape(bar, updated))
            {
                string renumbered = PositionReconciler.ReconcileAfterGeometryChange(db, editId);
                if (renumbered != null)
                {
                    ed?.WriteMessage($"\nPozycja {bar.Mark} jest używana przez pręt o innych wymiarach → nowa pozycja {renumbered}.");
                    updated.Mark = renumbered;
                }
            }

            if (isBlock)
                RebuildBlock(db, editId, updated);
            else
                SingleBarEngine.RebuildCompanions(db, editId, updated, ringStart, frame);

            // Krok 3b — napraw grot strzałki etykiety pręta (geometria się zmieniła)
            using (var trFix = db.TransactionManager.StartTransaction())
            {
                var barFix   = trFix.GetObject(editId, OpenMode.ForRead) as Entity;
                var plineFix = barFix as Polyline;
                var beFix    = plineFix == null ? BarEntity.From(barFix, BarKinds.Block) : null;
                if ((plineFix != null || beFix != null) && !string.IsNullOrEmpty(updated.LabelHandle))
                {
                    if (long.TryParse(updated.LabelHandle,
                            NumberStyles.HexNumber,
                            null, out long lblHVal))
                    {
                        var lblHandle = new Handle(lblHVal);
                        if (db.TryGetObjectId(lblHandle, out ObjectId lblId)
                            && !lblId.IsNull && !lblId.IsErased)
                        {
                            // Polilinia: jak wzorzec (od razu do zapisu). Pręt-blok: najpierw odczyt i back-link —
                            // kopia bez wtyczki może wskazywać etykietę oryginału, której nie wolno ruszać.
                            var ml = trFix.GetObject(lblId, beFix != null ? OpenMode.ForRead : OpenMode.ForWrite) as MLeader;
                            if (ml != null && beFix != null)
                            {
                                if (XLink.Same(SingleBarEngine.ReadBarHandleFromLabel(ml), editId.Handle.Value.ToString("X8")))
                                    ml.UpgradeOpen();
                                else
                                    ml = null;
                            }
                            if (ml != null)
                            {
                                try
                                {
                                    var leaderIdxs = ml.GetLeaderIndexes();
                                    if (leaderIdxs != null && leaderIdxs.Count > 0)
                                    {
                                        int li       = (int)leaderIdxs[0];
                                        var lineIdxs = ml.GetLeaderLineIndexes(li);
                                        if (lineIdxs != null && lineIdxs.Count > 0)
                                        {
                                            int lni        = (int)lineIdxs[0];
                                            var currentTip = ml.GetFirstVertex(lni);
                                            var newTip     = plineFix != null
                                                ? plineFix.GetClosestPointTo(currentTip, false)
                                                : beFix.ClosestPoint(trFix, currentTip) ?? currentTip;
                                            ml.SetFirstVertex(lni, newTip);
                                        }
                                    }
                                }
                                catch (System.Exception ex)
                                {
                                    // nie przerywaj, jeśli MLeader nie ma leaderów
                                    Log.Error("BarEditApply.FixLabelArrow", ex);
                                }
                            }
                        }
                    }
                }
                trFix.Commit();
            }

            // Krok 3c — zaktualizuj etykietę pręta (MLeader) z poprawnym count ze wszystkich rozkładów
            AnnotationEngine.UpdateBarLabelCount(db,
                editId.Handle.Value.ToString("X8"), markOverride: updated.Mark);

            // Krok 4 — propaguj zmiany (Mark, Diameter, LengthA) do powiązanych rozkładów.
            // Szukamy po SourceBarHandle (nie po posNr) — handle pręta nie zmienia się gdy user
            // zmienia posNr lub diameter, więc zawsze znajdziemy właściwe rozkłady.
            PositionReconciler.PropagateToDistributions(db, editId, updated);

            ed?.WriteMessage($"\nPręt {updated.Mark} zaktualizowany. Shape: {updated.ShapeCode}\n");
            return true;
        }

        /// <summary>Pręt-blok: przebudowa BTR z danych (copy-on-write); XData zapisane wcześniej.</summary>
        private static void RebuildBlock(Database db, ObjectId barId, BarData bar)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var be = BarEntity.Open(tr, barId, OpenMode.ForWrite, BarKinds.Block);
                if (be?.Entity is BlockReference br)
                    SingleBarBlock.Rebuild(tr, br, bar);   // znacznik geometrii W4d — w SingleBarBlock.Rebuild (D34)
                tr.Commit();
            }
        }
    }
}
