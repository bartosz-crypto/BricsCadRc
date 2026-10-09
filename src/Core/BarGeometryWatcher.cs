using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>Długość odcinka zmienianego gripem / STRETCH zaokrąglana do pełnych 50 mm.</summary>
        public const double GripLengthStep = 50.0;

        private static double SnapLength(double v) => Math.Round(v / GripLengthStep) * GripLengthStep;
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

        // Obrys pręta (wierzchołki) przed komendą — do rozpoznania gripa na KOŃCU pręta
        // (przesunięty narożnik/bok końca → zmiana długości odcinka końcowego zamiast cofania).
        private static readonly Dictionary<ObjectId, List<Point3d>> _preOutline =
            new Dictionary<ObjectId, List<Point3d>>();

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
                    catch (System.Exception logEx) { Log.Error("BarGeometryWatcher.Register", logEx); }
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

        /// <summary>
        /// Komenda wtyczki sama zmieniła pręt i propaguje zmiany (RC_EDIT_BAR, RC_UPDATE_BAR) — watcher ma ten
        /// pręt pominąć w CommandEnded. Inaczej różnicę obrysu (stary → przebudowany) brał za grip na końcu
        /// i doliczał ją drugi raz do już nowej długości (3000 → 3500 dawało 4000).
        /// </summary>
        public static void Forget(ObjectId id)
        {
            _pending.Remove(id);
            _preAxisStart.Remove(id);
            _preOutline.Remove(id);
        }

        private static void ResetCommandState()
        {
            _pending.Clear();
            _preAxisStart.Clear();
            _preOutline.Clear();
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
                var verts = new List<Point3d>(pl.NumberOfVertices);
                for (int i = 0; i < pl.NumberOfVertices; i++) verts.Add(pl.GetPoint3dAt(i));
                _preOutline[pl.ObjectId] = verts;
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
        // Grip na końcu pręta
        // ----------------------------------------------------------------

        /// <summary>
        /// Rozpoznaje ruch tylko wierzchołków KOŃCA obrysu (start: 0 i ostatni; koniec: n−1 i n) i liczy
        /// nową długość odcinka końcowego (rzut przesunięcia na oś pręta). Parametr kształtu wybierany
        /// automatycznie: ten, którego zmiana wydłuża tylko ten koniec (dla 00 — A, dla 21 — A albo C itd.).
        /// False → zwykła ścieżka (MOVE / STRETCH całości / cofnięcie złej edycji).
        /// </summary>
        private static bool TryEndStretch(Polyline pl, BarData bar, List<Point3d> pre,
                                          out BarData result, out Point3d newAxisStart, out string why,
                                          out SingleBarEngine.OutlineFrame? frame)
        {
            result = null; newAxisStart = Point3d.Origin; why = null; frame = null;
            string shapeCode = bar.ShapeCode ?? "00";
            int total = pl.NumberOfVertices;
            if (BarGeometryBuilder.IsLegacyRing(shapeCode, bar.ParamValues) || total < 4 || total % 2 != 0 || pre == null || pre.Count != total) return false;
            int n = total / 2;

            var moved = new List<int>();
            for (int i = 0; i < total; i++)
                if (pl.GetPoint3dAt(i).DistanceTo(pre[i]) > 0.01) moved.Add(i);
            if (moved.Count == 0 || moved.Count == total) return false;   // brak zmiany / MOVE

            var startSet = new HashSet<int> { 0, total - 1 };
            var endSet   = new HashSet<int> { n - 1, n };
            bool atStart = moved.All(startSet.Contains);
            bool atEnd   = moved.All(endSet.Contains);
            if (!atStart && !atEnd) return false;   // ruch w środku pręta → cofnięcie jak dotąd

            // Oś pręta sprzed edycji
            var axis = new List<Point3d>(n);
            for (int i = 0; i < n; i++)
                axis.Add(new Point3d((pre[i].X + pre[total - 1 - i].X) / 2, (pre[i].Y + pre[total - 1 - i].Y) / 2, 0));
            var outward = atStart ? axis[0] - axis[1] : axis[n - 1] - axis[n - 2];
            if (outward.Length < 1e-6) return false;
            outward = outward.GetNormal();

            double delta = moved.Average(i => (pl.GetPoint3dAt(i) - pre[i]).DotProduct(outward));
            if (Math.Abs(delta) < 0.5) return false;

            var shape = ShapeCodeLibrary.Get(shapeCode) ?? ShapeCodeLibrary.Get("00");
            int k = FindEndParam(shape, bar.ParamValues, bar.Diameter, atStart, delta);
            if (k < 0)
            {
                why = $"Pręt {bar.Mark} (kształt {shapeCode}): tego końca nie da się zmienić gripem — użyj RC_EDIT_BAR.";
                return false;
            }
            var pv = bar.ParamValues;
            double newVal = SnapLength(pv[k] + delta);   // co 50 mm
            delta = newVal - pv[k];
            if (newVal < Math.Max(2.0 * bar.Diameter, 50.0))
            {
                why = $"Pręt {bar.Mark}: za krótki odcinek ({newVal:F0} mm) — zmiana cofnięta.";
                return false;
            }

            // układ pręta (obrót / odbicie) z obrysu sprzed edycji, dla wymiarów sprzed zmiany
            if (SingleBarEngine.TryGetOutlineFrame(pre, bar, out var fr)) frame = fr;

            result = bar;   // świeży odczyt XData — można zmieniać
            SetParam(result, k, newVal);
            if (!result.LengthOverridden)
            {
                try
                {
                    var p = result.ParamValues.Take(shape.Parameters.Length).ToArray();
                    result.TotalLength = shape.CalculateTotalLength(p, result.Diameter);
                }
                catch (System.Exception ex) { Log.Error("BarGeometryWatcher.TotalLength", ex); }
            }
            newAxisStart = atStart ? axis[0] + outward * delta : axis[0];
            return true;
        }

        /// <summary>
        /// Pręt prosty: wydłuża / skraca obrys o <paramref name="add"/> wzdłuż osi po stronie końca, który
        /// przesunął użytkownik (porównanie z obrysem sprzed komendy; domyślnie koniec osi).
        /// </summary>
        private static void SnapStraightOutline(Polyline pl, double add, List<Point3d> pre)
        {
            int total = pl.NumberOfVertices;
            if (total < 4 || total % 2 != 0) return;
            int n = total / 2;
            Point3d Mid(int a, int b) { var p = pl.GetPoint3dAt(a); var q = pl.GetPoint3dAt(b);
                                        return new Point3d((p.X + q.X) / 2, (p.Y + q.Y) / 2, 0); }
            var axis = Mid(n - 1, n) - Mid(0, total - 1);
            if (axis.Length < 1e-6) return;
            axis = axis.GetNormal();
            bool moveStart = false;
            if (pre != null && pre.Count == total)
            {
                double ds = pl.GetPoint3dAt(0).DistanceTo(pre[0]) + pl.GetPoint3dAt(total - 1).DistanceTo(pre[total - 1]);
                double de = pl.GetPoint3dAt(n - 1).DistanceTo(pre[n - 1]) + pl.GetPoint3dAt(n).DistanceTo(pre[n]);
                moveStart = ds > de + 0.01;
            }
            var shift = axis * (moveStart ? -add : add);
            foreach (int i in moveStart ? new[] { 0, total - 1 } : new[] { n - 1, n })
            {
                var p = pl.GetPoint3dAt(i) + shift;
                pl.SetPointAt(i, new Point2d(p.X, p.Y));
            }
        }

        /// <summary>Obrys przystający do obrysu sprzed komendy (MOVE / ROTATE / MIRROR) — odległości zachowane.</summary>
        private static bool IsRigidMotion(Polyline pl, List<Point3d> pre)
        {
            if (pre == null || pre.Count != pl.NumberOfVertices || pre.Count < 2) return false;
            var p0 = pl.GetPoint3dAt(0);
            for (int i = 1; i < pre.Count; i++)
            {
                var pi = pl.GetPoint3dAt(i);
                if (Math.Abs(p0.DistanceTo(pi) - pre[0].DistanceTo(pre[i])) > 0.5) return false;
                var pj = pl.GetPoint3dAt(i - 1);
                if (Math.Abs(pj.DistanceTo(pi) - pre[i - 1].DistanceTo(pre[i])) > 0.5) return false;
            }
            return true;
        }

        /// <summary>Układ pręta z obrysu sprzed komendy (cofanie złej edycji wierzchołka) — null gdy brak.</summary>
        private static SingleBarEngine.OutlineFrame? FrameOf(List<Point3d> pre, BarData bar)
            => pre != null && SingleBarEngine.TryGetOutlineFrame(pre, bar, out var f) ? f : (SingleBarEngine.OutlineFrame?)null;

        /// <summary>Parametr, którego zmiana o delta wydłuża tylko dany koniec (reszta kształtu bez zmian).</summary>
        private static int FindEndParam(BarShape shape, double[] pv, double d, bool atStart, double delta)
        {
            var old = BarGeometryBuilder.GetLocalPoints(shape.Code, pv, d);
            if (old == null || old.Count < 2) return -1;
            int n = old.Count;
            (double X, double Y) Sub((double X, double Y) a, (double X, double Y) b) => (a.X - b.X, a.Y - b.Y);
            double Len((double X, double Y) v) => Math.Sqrt(v.X * v.X + v.Y * v.Y);
            var dir = atStart ? Sub(old[0], old[1]) : Sub(old[n - 1], old[n - 2]);
            double dl = Len(dir);
            if (dl < 1e-9) return -1;
            dir = (dir.X / dl, dir.Y / dl);

            int count = Math.Min(pv.Length, shape.Parameters.Length);
            for (int i = 0; i < count; i++)
            {
                var q = (double[])pv.Clone();
                q[i] += delta;
                List<(double X, double Y)> nw;
                try { nw = BarGeometryBuilder.GetLocalPoints(shape.Code, q, d); }
                catch (System.Exception ex) { Log.Error("BarGeometryWatcher.FindEndParam", ex); continue; }
                if (nw == null || nw.Count != n) continue;

                // przesunięcie wyrównujące stały koniec
                var shift = atStart ? Sub(old[n - 1], nw[n - 1]) : Sub(old[0], nw[0]);
                bool ok = true;
                for (int j = 0; j < n && ok; j++)
                {
                    var pj = (nw[j].X + shift.X, nw[j].Y + shift.Y);
                    bool free = atStart ? j == 0 : j == n - 1;
                    var expect = free ? (old[j].X + dir.X * delta, old[j].Y + dir.Y * delta) : old[j];
                    if (Len(Sub(pj, expect)) > 0.05) ok = false;
                }
                if (ok) return i;
            }
            return -1;
        }

        private static void SetParam(BarData b, int k, double v)
        {
            switch (k)
            {
                case 0: b.LengthA = v; break;
                case 1: b.LengthB = v; break;
                case 2: b.LengthC = v; break;
                case 3: b.LengthD = v; break;
                case 4: b.LengthE = v; break;
            }
        }

        /// <summary>Zapis nowych wymiarów, odbudowa obrysu od nowego początku osi, numeracja, rozkłady, etykieta.</summary>
        private static void ApplyEndStretch(Document doc, Database db, ObjectId oid, string oldMark,
                                            BarData stretched, Point3d axisStart,
                                            SingleBarEngine.OutlineFrame? frame = null)
        {
            using (var tr = db.TransactionManager.StartTransaction())
            {
                if (tr.GetObject(oid, OpenMode.ForWrite) is Polyline pl) SingleBarEngine.WriteXData(pl, stretched);
                tr.Commit();
            }
            SingleBarEngine.RebuildCompanions(db, oid, stretched, axisStart, frame);

            int oldPosNr = SingleBarEngine.ExtractPosNr(oldMark);
            string newMark = PositionReconciler.ReconcileAfterGeometryChange(db, oid);
            bool renumbered = newMark != null;

            BarData cur;
            using (var tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                var pline = tr.GetObject(oid, OpenMode.ForRead) as Polyline;
                cur = pline != null ? SingleBarEngine.ReadBarXData(pline) : null;
            }
            if (cur == null) return;
            int nDist = PositionReconciler.PropagateToDistributions(db, oid, cur, legacyPosNr: renumbered ? 0 : oldPosNr);
            FixLabelArrow(db, oid, cur.LabelHandle);
            AnnotationEngine.UpdateBarLabelCount(db, oid.Handle.Value.ToString("X8"), markOverride: cur.Mark);

            string dims = string.Join("/", new[] { cur.LengthA, cur.LengthB, cur.LengthC, cur.LengthD, cur.LengthE }
                                            .Where(v => v > 0).Select(v => v.ToString("F0")));
            doc.Editor?.WriteMessage(renumbered
                ? $"\n[RC AUTO] Pręt {oldMark} zmieniony gripem ({dims} mm) → nowa pozycja {cur.Mark}  ({nDist} rozkład(y))\n"
                : $"\n[RC AUTO] Pręt {oldMark}: {dims} mm (grip)  ({nDist} rozkład(y))\n");
        }

        /// <summary>Grot etykiety pręta na najbliższy punkt nowego obrysu (jak RC_EDIT_BAR).</summary>
        private static void FixLabelArrow(Database db, ObjectId barId, string labelHandle)
        {
            if (string.IsNullOrEmpty(labelHandle)
                || !long.TryParse(labelHandle, System.Globalization.NumberStyles.HexNumber, null, out long h)) return;
            try
            {
                using var tr = db.TransactionManager.StartTransaction();
                if (db.TryGetObjectId(new Handle(h), out ObjectId lid) && !lid.IsErased
                    && tr.GetObject(lid, OpenMode.ForWrite) is MLeader ml
                    && tr.GetObject(barId, OpenMode.ForRead) is Polyline pl)
                {
                    var leaders = ml.GetLeaderIndexes();
                    if (leaders != null && leaders.Count > 0)
                    {
                        var lines = ml.GetLeaderLineIndexes((int)leaders[0]);
                        if (lines != null && lines.Count > 0)
                        {
                            int li = (int)lines[0];
                            ml.SetFirstVertex(li, pl.GetClosestPointTo(ml.GetFirstVertex(li), false));
                        }
                    }
                }
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("BarGeometryWatcher.FixLabelArrow", ex); }
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
            var preOutline   = new Dictionary<ObjectId, List<Point3d>>(_preOutline);
            _pending.Clear();
            _preAxisStart.Clear();
            _preOutline.Clear();

            _rebuildDepth++;
            try
            {
                ProcessPending(doc, db, toProcess, preAxisStart, preOutline);
            }
            finally
            {
                _rebuildDepth--;
                _modifiedLeaders.Clear();
                _preAxisStart.Clear();
                _preOutline.Clear();
            }
        }

        private static void ProcessPending(Document doc, Database db, List<ObjectId> toProcess,
                                           Dictionary<ObjectId, Point3d> preAxisStart,
                                           Dictionary<ObjectId, List<Point3d>> preOutline)
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

                        // Grip na KOŃCU pręta (narożnik albo bok końca) → nowa długość odcinka końcowego.
                        // Drugi koniec zostaje na miejscu; wymiar zaokrąglany do 50 mm (GripLengthStep).
                        string why = null;
                        BarData stretched = null;
                        Point3d newAxisStart = Point3d.Origin;
                        SingleBarEngine.OutlineFrame? endFrame = null;
                        if (preOutline.TryGetValue(oid, out var pre)
                            && TryEndStretch(pline, bar, pre, out stretched, out newAxisStart, out why, out endFrame))
                        {
                            tr.Commit();
                            ApplyEndStretch(doc, db, oid, mark, stretched, newAxisStart, endFrame);
                            continue;
                        }
                        if (why != null)
                        {
                            // Odrzucona zmiana (za krótki odcinek / koniec nieedytowalny gripem) — naprawdę cofnij:
                            // obrys z danych pręta (wcześniej ścieżka 00 przyjmowała krótką długość z obrysu).
                            doc.Editor?.WriteMessage($"\n[RC AUTO] {why}\n");
                            try
                            {
                                _rebuildDepth++;
                                if (pre != null && pre.Count == pline.NumberOfVertices)
                                {
                                    // obrys sprzed komendy (kierunek pręta zachowany)
                                    pline.UpgradeOpen();
                                    for (int i = 0; i < pre.Count; i++)
                                        pline.SetPointAt(i, new Point2d(pre[i].X, pre[i].Y));
                                    tr.Commit();
                                }
                                else
                                {
                                    tr.Commit();
                                    SingleBarEngine.RebuildCompanions(db, oid, bar);
                                }
                            }
                            catch (System.Exception ex) { Log.Error("BarGeometryWatcher.RejectStretch", ex); }
                            finally { _rebuildDepth--; }
                            continue;
                        }

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
                            // MOVE / ROTATE / MIRROR (ruch sztywny) — kształt bez zmian, nic nie przebudowujemy
                            // (wcześniej przebudowa kładła obrócony pręt z powrotem poziomo).
                            if (IsRigidMotion(pline, pre)) { tr.Commit(); continue; }
                            var revertFrame = FrameOf(pre, bar);
                            tr.Commit();

                            try
                            {
                                _rebuildDepth++;
                                SingleBarEngine.RebuildCompanions(db, oid, bar,
                                    revertFrame?.AxisStart, revertFrame);
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
                            var revertFrame00 = FrameOf(pre, bar);
                            tr.Commit();
                            try
                            {
                                _rebuildDepth++;
                                SingleBarEngine.RebuildCompanions(db, oid, bar,
                                    revertFrame00?.AxisStart, revertFrame00);
                            }
                            catch (System.Exception ex) { Log.Error("BarGeometryWatcher.RebuildCompanions(invalid outline)", ex); }
                            finally
                            {
                                _rebuildDepth--;
                            }
                            continue;
                        }

                        // Struktura walidna — akceptuj stretch (długość co 50 mm)
                        double rawLength = SingleBarEngine.GetStraightBarAxisLength(pline);
                        // Pomiń jeśli zmiana < 1mm (numeryczna niedokładność)
                        if (Math.Abs(rawLength - bar.LengthA) < 1.0) { tr.Commit(); continue; }
                        newLength = Math.Max(SnapLength(rawLength), GripLengthStep);
                        if (Math.Abs(newLength - rawLength) > 0.5)
                        {
                            // obrys do zaokrąglonej długości: przesunięty koniec dociągnięty wzdłuż osi
                            // (kierunek pręta zachowany — także dla prętów obróconych)
                            try
                            {
                                _rebuildDepth++;
                                pline.UpgradeOpen();
                                SnapStraightOutline(pline, newLength - rawLength,
                                    preOutline.TryGetValue(oid, out var preS) ? preS : null);
                            }
                            catch (System.Exception ex) { Log.Error("BarGeometryWatcher.SnapStraightOutline", ex); }
                            finally { _rebuildDepth--; }
                        }
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
