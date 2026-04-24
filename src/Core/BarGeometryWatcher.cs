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

        // Reentrancy guard — true gdy watcher sam modyfikuje polyline (RebuildCompanions).
        // OnObjectModified sprawdza tę flagę żeby nie zakolejkować własnych zmian.
        private static volatile bool _isRebuilding = false;

        // Ostatnia znana pozycja axis_start każdego bara (klucz = ObjectId).
        // Używana do detekcji translacji (move entire entity) → leader follow.
        // In-memory tylko; zerowana przy Unregister (NETUNLOAD).
        private static readonly Dictionary<ObjectId, Point3d> _lastAxisStart =
            new Dictionary<ObjectId, Point3d>();

        public static void Register()
        {
            if (_active) return;
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc?.Database == null) return;
                doc.Database.ObjectModified += OnObjectModified;
                doc.CommandEnded            += OnCommandEnded;
                _active = true;
            }
            catch { }
        }

        public static void Unregister()
        {
            if (!_active) return;
            try
            {
                var doc = Application.DocumentManager.MdiActiveDocument;
                if (doc?.Database != null)
                    doc.Database.ObjectModified -= OnObjectModified;
                if (doc != null)
                    doc.CommandEnded -= OnCommandEnded;
            }
            catch { }
            _active = false;
            _pending.Clear();
            _lastAxisStart.Clear();
        }

        // ----------------------------------------------------------------
        // ObjectModified — zakolejkuj pręt jeśli jest RC_SINGLE_BAR
        // Nie otwieramy tu transakcji — tylko rejestrujemy id do przetworzenia później
        // ----------------------------------------------------------------
        private static void OnObjectModified(object sender, ObjectEventArgs e)
        {
            if (_isRebuilding) return; // nie kolejkuj własnych modyfikacji z RebuildCompanions
            try
            {
                if (!(e.DBObject is Polyline)) return;
                // Szybkie sprawdzenie XData bez otwierania nowej transakcji
                var xd = e.DBObject.GetXDataForApplication(SingleBarEngine.XAppName);
                if (xd != null)
                    _pending.Add(e.DBObject.ObjectId);
            }
            catch { }
        }

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
        private static void TranslateLeader(Database db, Transaction tr, string labelHandle, Vector3d delta)
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

            var ml = tr.GetObject(leaderId, OpenMode.ForWrite) as Entity;
            if (ml == null) return;

            ml.TransformBy(Matrix3d.Displacement(delta));
        }

        // ----------------------------------------------------------------
        // CommandEnded — przetwarza kolejkę bezpiecznie po zamknięciu transakcji komendy
        // ----------------------------------------------------------------
        private static void OnCommandEnded(object sender, CommandEventArgs e)
        {
            if (_pending.Count == 0) return;
            var toProcess = new List<ObjectId>(_pending);
            _pending.Clear();

            var doc = sender as Document;
            if (doc?.Database == null) return;
            var db = doc.Database;

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
                        // Porównuje current axis_start z ostatnią zapamiętaną pozycją (_lastAxisStart).
                        // Jeśli delta > 10mm → bar został przesunięty → translatuj leader.
                        // Threshold 10mm: rozróżnia prawdziwy move (>> 10mm) od drobnego drgania
                        // przy niewalidnym grip na shape 11+ (zazwyczaj < 10mm).
                        var currentAxisStart = SingleBarEngine.GetAxisFirstPointFromOutline(pline, shapeCode);
                        if (_lastAxisStart.TryGetValue(oid, out Point3d oldAxisStart))
                        {
                            var moveDelta = currentAxisStart - oldAxisStart;
                            if (moveDelta.Length > 10.0)
                            {
                                try
                                {
                                    _isRebuilding = true;
                                    TranslateLeader(db, tr, bar.LabelHandle, moveDelta);
                                }
                                catch { /* defensive */ }
                                finally
                                {
                                    _isRebuilding = false;
                                }
                            }
                        }
                        _lastAxisStart[oid] = currentAxisStart;

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
                                _isRebuilding = true;
                                SingleBarEngine.RebuildCompanions(db, oid, bar);
                            }
                            catch { /* defensive — nie crash watchera na błędzie rebuild */ }
                            finally
                            {
                                _isRebuilding = false;
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
                                _isRebuilding = true;
                                SingleBarEngine.RebuildCompanions(db, oid, bar);
                            }
                            catch { }
                            finally
                            {
                                _isRebuilding = false;
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

                    // 3. Znajdź powiązane rozkłady i przebuduj linie prętów
                    int posNr   = SingleBarEngine.ExtractPosNr(mark);
                    var distIds = BarBlockEngine.FindDistributionsByPosNr(db, posNr);
                    foreach (var distId in distIds)
                        BarBlockEngine.UpdateBarLength(db, distId, newLength);

                    doc.Editor?.WriteMessage(
                        $"\n[RC AUTO] Bar {mark} updated: {newLength:F0} mm  ({distIds.Count} distribution(s))\n");
                }
                catch { }
            }
        }
    }
}
