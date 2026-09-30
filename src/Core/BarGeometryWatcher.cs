using System;
using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;

namespace BricsCadRc.Core
{
    /// <summary>
    /// FEATURE E: Automatyczna aktualizacja rozkładów po rozciągnięciu polilinii pręta.
    ///
    /// Mechanizm:
    ///   1. Database.ObjectModified → jeśli RC_SINGLE_BAR zmienił długość → zakolejkuj ObjectId
    ///   2. Document.CommandEnded   → po zakończeniu komendy przetwórz kolejkę:
    ///      - zaktualizuj XData.LengthA na polilinii
    ///      - BarBlockEngine.UpdateBarLength() na każdym powiązanym RC_SLAB_BARS_nnn
    ///
    /// Rejestracja: BarGeometryWatcher.Register() / Unregister() z PluginApp.
    /// </summary>
    public static class BarGeometryWatcher
    {
        private static bool _active;
        private static readonly HashSet<ObjectId> _pending = new HashSet<ObjectId>();

        // Reentrancy guard (licznik, bo wywołania się zagnieżdżają) — > 0 gdy watcher sam
        // modyfikuje obiekty (RebuildCompanions, TranslateLeader, zapis XData).
        private static int _rebuildDepth;

        // Pozycja axis_start pręta ZANIM komenda zaczęła go modyfikować (ObjectOpenedForModify).
        // Wcześniej trzymana była "ostatnia znana pozycja" w pamięci na całą sesję:
        //   - pierwszy MOVE pręta w sesji nie przesuwał leadera (brak wpisu),
        //   - po UNDO wpis był nieaktualny i kolejny stretch przesuwał leader o fałszywą deltę.
        // Teraz baseline jest brany na początku każdej komendy i czyszczony na jej końcu.
        private static readonly Dictionary<ObjectId, Point3d> _preAxisStart =
            new Dictionary<ObjectId, Point3d>();

        // MLeadery zmodyfikowane w bieżącej komendzie przez użytkownika (np. MOVE pręt + etykieta
        // razem) — takich nie przesuwamy drugi raz.
        private static readonly HashSet<ObjectId> _modifiedLeaders = new HashSet<ObjectId>();

        public static void Register()
        {
            if (_active) return;
            DocumentWatch.Subscribe("BarGeometryWatcher",
                d =>
                {
                    d.Database.ObjectOpenedForModify += OnObjectOpenedForModify;
                    d.Database.ObjectModified        += OnObjectModified;
                    d.CommandEnded                   += OnCommandEnded;
                    d.CommandCancelled               += OnCommandCancelled;
                },
                d =>
                {
                    try
                    {
                        d.Database.ObjectOpenedForModify -= OnObjectOpenedForModify;
                        d.Database.ObjectModified        -= OnObjectModified;
                    }
                    catch { }
                    d.CommandEnded     -= OnCommandEnded;
                    d.CommandCancelled -= OnCommandCancelled;
                });
            _active = true;
        }

        public static void Unregister()
        {
            if (!_active) return;
            DocumentWatch.Unsubscribe("BarGeometryWatcher");
            _active = false;
            ResetCommandState();
        }

        private static void ResetCommandState()
        {
            _pending.Clear();
            _preAxisStart.Clear();
            _modifiedLeaders.Clear();
        }

        // ----------------------------------------------------------------
        // ObjectOpenedForModify — zapamiętaj pozycję pręta PRZED modyfikacją
        // ----------------------------------------------------------------
        private static void OnObjectOpenedForModify(object sender, ObjectEventArgs e)
        {
            if (_rebuildDepth > 0) return;
            try
            {
                if (!(e.DBObject is Polyline pl)) return;
                if (pl.IsUndoing || _preAxisStart.ContainsKey(pl.ObjectId)) return;
                if (pl.GetXDataForApplication(SingleBarEngine.XAppName) == null) return;

                var bar = SingleBarEngine.ReadBarXData(pl);
                if (bar == null) return;
                _preAxisStart[pl.ObjectId] =
                    SingleBarEngine.GetAxisFirstPointFromOutline(pl, bar.ShapeCode ?? "00");
            }
            catch (System.Exception ex) { Log.Error("BarGeometryWatcher.OnObjectOpenedForModify", ex); }
        }

        // ----------------------------------------------------------------
        // ObjectModified — zakolejkuj pręt jeśli jest RC_SINGLE_BAR
        // Nie otwieramy tu transakcji — tylko rejestrujemy id do przetworzenia później
        // ----------------------------------------------------------------
        private static void OnObjectModified(object sender, ObjectEventArgs e)
        {
            if (_rebuildDepth > 0) return; // nie kolejkuj własnych modyfikacji
            try
            {
                if (e.DBObject == null || e.DBObject.IsUndoing) return; // UNDO sam przywraca stan

                if (e.DBObject is MLeader)
                {
                    _modifiedLeaders.Add(e.DBObject.ObjectId);
                    return;
                }

                if (!(e.DBObject is Polyline)) return;
                // Szybkie sprawdzenie XData bez otwierania nowej transakcji
                var xd = e.DBObject.GetXDataForApplication(SingleBarEngine.XAppName);
                if (xd != null)
                    _pending.Add(e.DBObject.ObjectId);
            }
            catch (System.Exception ex) { Log.Error("BarGeometryWatcher.OnObjectModified", ex); }
        }

        private static void OnCommandCancelled(object sender, CommandEventArgs e)
            => ResetCommandState();

        // ----------------------------------------------------------------
        // Walidacja struktury outline dla shape 00/99
        // ----------------------------------------------------------------

        /// <summary>
        /// Sprawdza czy outline polyline dla shape 00/99 ma walidną strukturę prostokąta.
        /// Walidna = dwa endpointy każdej pary są prostopadłe do osi bar-a i mają długość ≈ diameter.
        /// Zepsuta = user ruszył pojedynczy vertex, geometria przestała być prostokątem.
        /// </summary>
        private static bool IsValidStraightBarOutline(Polyline pl, double diameter)
        {
            int total = pl.NumberOfVertices;
            if (total != 4) return false; // shape 00/99 outline ma dokładnie 4 wierzchołki

            var p0 = pl.GetPoint3dAt(0);  // left[0]
            var p1 = pl.GetPoint3dAt(1);  // left[1]
            var p2 = pl.GetPoint3dAt(2);  // right[1]
            var p3 = pl.GetPoint3dAt(3);  // right[0]

            // Oś bar-a
            var axisStart = new Teigha.Geometry.Point3d((p0.X + p3.X) / 2.0, (p0.Y + p3.Y) / 2.0, 0);
            var axisEnd   = new Teigha.Geometry.Point3d((p1.X + p2.X) / 2.0, (p1.Y + p2.Y) / 2.0, 0);
            var axisVec   = axisEnd - axisStart;
            double axisLen = axisVec.Length;
            if (axisLen < 1e-6) return false;
            var axisDir = axisVec / axisLen;

            // Para startowa: p0 - p3 (left[0] - right[0])
            var startPair = p0 - p3;
            double startLen = startPair.Length;
            if (Math.Abs(startLen - diameter) > 1.0) return false; // długość ≠ diameter

            var startDir = startPair / startLen;
            double startDot = Math.Abs(axisDir.DotProduct(startDir));
            if (startDot > 0.01) return false; // nieprostopadła (cosinus > 0.01)

            // Para końcowa: p1 - p2 (left[1] - right[1])
            var endPair = p1 - p2;
            double endLen = endPair.Length;
            if (Math.Abs(endLen - diameter) > 1.0) return false;

            var endDir = endPair / endLen;
            double endDot = Math.Abs(axisDir.DotProduct(endDir));
            if (endDot > 0.01) return false;

            return true;
        }

        // ----------------------------------------------------------------
        // Leader follow — translatuje MLeader razem z bar-em
        // ----------------------------------------------------------------

        /// <summary>
        /// Translatuje leader entity (dowolny typ: MLeader, Line+MText itp.) o podany delta.
        /// Używa TransformBy(Matrix3d.Displacement) — działa dla każdego Entity.
        /// </summary>
        private static void TranslateLeader(Database db, Transaction tr, ObjectId barId, string labelHandle, Vector3d delta)
        {
            if (string.IsNullOrEmpty(labelHandle)) return;
            if (delta.Length < 1e-6) return;

            long h;
            if (!long.TryParse(labelHandle, System.Globalization.NumberStyles.HexNumber, null, out h))
                return;

            ObjectId leaderId;
            try { leaderId = db.GetObjectId(false, new Handle(h), 0); }
            catch { return; }
            if (leaderId.IsNull || leaderId.IsErased) return;
            if (_modifiedLeaders.Contains(leaderId)) return; // user przesunął etykietę razem z prętem

            var ml = tr.GetObject(leaderId, OpenMode.ForRead) as MLeader;
            if (ml == null) return;
            // Przesuwaj tylko etykietę, która naprawdę należy do tego pręta (back-link).
            // Kopia pręta bez etykiety wskazuje na etykietę oryginału — nie wolno jej ruszać.
            if (!XLink.Same(SingleBarEngine.ReadBarHandleFromLabel(ml), barId.Handle.Value.ToString("X8"))) return;
            ml.UpgradeOpen();

            ml.TransformBy(Matrix3d.Displacement(delta));
        }

        // ----------------------------------------------------------------
        // CommandEnded — przetwarza kolejkę bezpiecznie po zamknięciu transakcji komendy
        // ----------------------------------------------------------------
        private static void OnCommandEnded(object sender, CommandEventArgs e)
        {
            var doc = sender as Document;
            if (doc?.Database == null || _pending.Count == 0 || DocumentWatch.IsUndoCommand(e.GlobalCommandName))
            {
                ResetCommandState();
                return;
            }
            var db = doc.Database;

            var toProcess = new List<ObjectId>();
            foreach (var id in _pending)
                if (id.Database == db) toProcess.Add(id);
            var preAxisStart = new Dictionary<ObjectId, Point3d>(_preAxisStart);
            _pending.Clear();
            _preAxisStart.Clear();

            _rebuildDepth++;
            try
            {
                ProcessPending(doc, db, toProcess, preAxisStart);
            }
            finally
            {
                _rebuildDepth--;
                _modifiedLeaders.Clear();
                _preAxisStart.Clear();
            }
        }

        private static void ProcessPending(Document doc, Database db, List<ObjectId> toProcess,
                                           Dictionary<ObjectId, Point3d> preAxisStart)
        {
            foreach (var oid in toProcess)
            {
                try
                {
                    if (oid.IsNull || oid.IsErased) continue;

                    // 1. Odczytaj aktualną długość i mark
                    double newLength;
                    string mark;
                    string shapeCode;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var pline = tr.GetObject(oid, OpenMode.ForRead) as Polyline;
                        if (pline == null) { tr.Commit(); continue; }
                        var bar = SingleBarEngine.ReadBarXData(pline);
                        if (bar == null) { tr.Commit(); continue; }
                        shapeCode = bar.ShapeCode ?? "00";
                        mark      = bar.Mark;

                        // Detekcja translacji bar-a (move entire entity).
                        // Porównuje current axis_start z pozycją sprzed komendy (preAxisStart).
                        // Jeśli delta > 10mm → bar został przesunięty → translatuj leader.
                        // Threshold 10mm: rozróżnia prawdziwy move (>> 10mm) od drobnego drgania
                        // przy niewalidnym grip na shape 11+ (zazwyczaj < 10mm).
                        var currentAxisStart = SingleBarEngine.GetAxisFirstPointFromOutline(pline, shapeCode);
                        if (preAxisStart.TryGetValue(oid, out Point3d oldAxisStart))
                        {
                            var moveDelta = currentAxisStart - oldAxisStart;
                            if (moveDelta.Length > 10.0)
                            {
                                try
                                {
                                    _rebuildDepth++;
                                    TranslateLeader(db, tr, oid, bar.LabelHandle, moveDelta);
                                }
                                catch (System.Exception ex) { Log.Error("BarGeometryWatcher.TranslateLeader", ex); }
                                finally
                                {
                                    _rebuildDepth--;
                                }
                            }
                        }

                        // Tylko shape 00/99 (prosta) — dla innych pline.Length ≠ parametr A
                        if (shapeCode != "00" && shapeCode != "99")
                        {
                            // Shape 11+ (zgięte) — nie ma czystego mapowania vertex outline → parametr bar-a.
                            // Plan B: wycofujemy ruch przez regenerację outline z istniejących parametrów XData.
                            // Outline "odskakuje" do stanu sprzed grip move. Zmiana parametrów bar-a tylko
                            // przez dialog RC_EDIT_BAR.
                            tr.Commit();

                            try
                            {
                                _rebuildDepth++;
                                SingleBarEngine.RebuildCompanions(db, oid, bar);
                            }
                            catch (System.Exception ex) { Log.Error("BarGeometryWatcher.RebuildCompanions", ex); }
                            finally
                            {
                                _rebuildDepth--;
                            }
                            continue;
                        }

                        // Shape 00/99 — walidacja struktury outline.
                        // Jeśli user ruszył pojedynczy vertex psując prostokąt → wycofaj ruch przez RebuildCompanions.
                        // Jeśli struktura jest walidna (stretch wzdłuż osi z zachowaniem diameter) → akceptuj nową długość.
                        if (!IsValidStraightBarOutline(pline, bar.Diameter))
                        {
                            tr.Commit();
                            try
                            {
                                _rebuildDepth++;
                                SingleBarEngine.RebuildCompanions(db, oid, bar);
                            }
                            catch (System.Exception ex) { Log.Error("BarGeometryWatcher.RebuildCompanions(invalid outline)", ex); }
                            finally
                            {
                                _rebuildDepth--;
                            }
                            continue;
                        }

                        // Struktura walidna — akceptuj stretch
                        newLength = SingleBarEngine.GetStraightBarAxisLength(pline);
                        // Pomiń jeśli zmiana < 1mm (numeryczna niedokładność)
                        if (Math.Abs(newLength - bar.LengthA) < 1.0) { tr.Commit(); continue; }
                        tr.Commit();
                    }

                    // 2. Zaktualizuj XData.LengthA na polilinii (tylko shape 00/99)
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var pline = tr.GetObject(oid, OpenMode.ForWrite) as Polyline;
                        if (pline != null)
                        {
                            var bar = SingleBarEngine.ReadBarXData(pline);
                            if (bar != null)
                            {
                                bar.LengthA = newLength;
                                SingleBarEngine.WriteXData(pline, bar);
                            }
                        }
                        tr.Commit();
                    }

                    // 3. Numer pozycji: jeśli ten sam numer ma inny pręt o innych wymiarach
                    //    (np. kopia, której zmieniono długość) → ten pręt dostaje inny numer.
                    int    oldPosNr = SingleBarEngine.ExtractPosNr(mark);
                    string newMark  = PositionReconciler.ReconcileAfterGeometryChange(db, oid);
                    bool   renumbered = newMark != null;

                    // 4. Rozkłady TEGO pręta (po SourceBarHandle). Stare rozkłady bez powiązania
                    //    bierzemy po numerze tylko gdy pręt był jedyny w swojej pozycji.
                    BarData cur;
                    using (var tr = db.TransactionManager.StartOpenCloseTransaction())
                    {
                        var pline = tr.GetObject(oid, OpenMode.ForRead) as Polyline;
                        cur = pline != null ? SingleBarEngine.ReadBarXData(pline) : null;
                    }
                    if (cur == null) continue;
                    int nDist = PositionReconciler.PropagateToDistributions(
                        db, oid, cur, legacyPosNr: renumbered ? 0 : oldPosNr);

                    // 5. Etykieta pręta (nowy numer / liczba sztuk)
                    AnnotationEngine.UpdateBarLabelCount(db, oid.Handle.Value.ToString("X8"), markOverride: cur.Mark);

                    doc.Editor?.WriteMessage(renumbered
                        ? $"\n[RC AUTO] Pręt {mark} zmieniony na {newLength:F0} mm → nowa pozycja {cur.Mark}  ({nDist} rozkład(y))\n"
                        : $"\n[RC AUTO] Pręt {mark}: {newLength:F0} mm  ({nDist} rozkład(y))\n");
                }
                catch (System.Exception ex) { Log.Error($"BarGeometryWatcher.ProcessPending {oid}", ex); }
            }
        }
    }
}
