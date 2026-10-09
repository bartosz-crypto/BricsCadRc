using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    // ----------------------------------------------------------------
    // GripOverrule — obsluguje 2 typy blokow:
    //
    //  RC_BAR_BLOCK (modul pretow):
    //    [0] @ insertion point  → ruch boczny wzd. osi pretow
    //    [1] @ (insX, insY+barsSpan) → rozciaganie span, recalc count
    //
    //  RC_BAR_ANNOT (modul annotacji):
    //    [0] @ insertion point  → ruch boczny wzd. osi pretow
    //    [1] @ koniec ramienia  → wydluzenie arm
    //
    // BRX: GetGripPoints dostepne tylko z Point3dCollection API.
    // ----------------------------------------------------------------
    public class AnnotGripOverrule : GripOverrule
    {
        // BricsCAD przekazuje KUMULATYWNY offset od startu dragu (nie inkrementalny).
        //
        // _dragOrigArm  — ArmTotalLen sprzed dragu (annotacje, grip ramienia)
        // _dragOrigPos  — pozycja bloku pretow sprzed dragu (grip [0], swobodny ruch)
        //
        // Czyszczone w GetGripPoints (poczatek nowej interakcji gripem).
        // ArmMidY NIE wymaga osobnego slownika — UpdateArmInBlock zapisuje go do XData
        // po kazdym ruchu, wiec barAnnot.ArmMidY zawsze zawiera aktualny lokalny Y kink.
        static Point3d LocalToWCS(Point3d insertPt, double angle, double localX, double localY)
        {
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            return new Point3d(
                insertPt.X + localX * cos - localY * sin,
                insertPt.Y + localX * sin + localY * cos,
                0);
        }

        private static readonly Dictionary<long, double>  _dragOrigArm
            = new Dictionary<long, double>();
        private static readonly Dictionary<long, Point3d> _dragOrigPos
            = new Dictionary<long, Point3d>();
        private static readonly Dictionary<long, Point3d> _annotDragStart
            = new Dictionary<long, Point3d>();
        private static readonly Dictionary<long, double>  _dragStartSegLen
            = new Dictionary<long, double>();
        private static readonly Dictionary<long, Point3d> _dragStartLastPt
            = new Dictionary<long, Point3d>();
        // Grip [2] annotacji (załamanie): długość odcinka za ostatnim prętem + kierunek na zewnątrz (WCS) ze startu dragu
        private static readonly Dictionary<long, (double ext, Vector3d outWcs)> _dragElbow
            = new Dictionary<long, (double ext, Vector3d outWcs)>();


        // Transienty podglądu dragu arm — czyszczone przed każdym nowym wywołaniem MoveGripPointsAt
        // i przy GetGripPoints (nowa interakcja).
        private static readonly List<Entity> _gripTransients = new List<Entity>();

        internal static void ClearGripTransients()
        {
            if (_gripTransients.Count == 0) return;
            var tm = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();
            foreach (var e in _gripTransients)
            {
                try { tm.EraseTransient(e, vpIds); } catch { }
                try { e.Dispose(); } catch { }
            }
            _gripTransients.Clear();
        }

        private static void AddGripTransientLine(Point3d p1, Point3d p2, short colorIndex = 5)
        {
            var tm = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();
            var ln = new Line(p1, p2) { ColorIndex = colorIndex };
            try
            {
                tm.AddTransient(ln, TransientDrawingMode.DirectTopmost, 128, vpIds);
                _gripTransients.Add(ln);
            }
            catch { ln.Dispose(); }
        }

        // ── Odroczone zmiany grip-dragu ─────────────────────────────────────
        // BricsCAD woła MoveGripPointsAt w trakcie dragu także na PRAWDZIWYM obiekcie,
        // a przy ESC cofa tylko sam blockref — nie przebudowany BTR, annotację ani etykietę.
        // Dlatego w trakcie dragu tylko rysujemy podgląd i zapamiętujemy ostatni stan,
        // a do bazy zapisujemy raz, w CommandEnded (ApplyPendingGripEdits). ESC = nic do cofania.
        private static readonly Dictionary<ObjectId, (double span, double skewEnd)> _pendingSpan
            = new Dictionary<ObjectId, (double span, double skewEnd)>();
        private static readonly Dictionary<ObjectId, Vector3d> _pendingAnnotMove
            = new Dictionary<ObjectId, Vector3d>();
        // Grip [0] rozkładu: przesunięcie początku (pierwszy pręt o otulinę od gripa), drugi koniec bez zmian.
        // shift = przesunięcie początku wzdłuż rozkładu (lokalnie, + = do środka), span = nowy BarsSpan.
        private static readonly Dictionary<ObjectId, (double shift, double span, double skewStart)> _pendingStart
            = new Dictionary<ObjectId, (double shift, double span, double skewStart)>();
        // Ostatni rozkład, dla którego pokazano gripy — gdy drag idzie na klonie (ObjectId.Null).
        private static ObjectId _lastGripOwner = ObjectId.Null;

        /// <summary>Czyści stan dragu i transienty (koniec / anulowanie komendy).</summary>
        internal static void ResetDragState()
        {
            ClearGripTransients();
            _pendingSpan.Clear();
            _pendingAnnotMove.Clear();
            _pendingStart.Clear();
            _dragOrigPos.Clear();
            _annotDragStart.Clear();
        }

        private static ObjectId RealId(BlockReference br)
            => br.ObjectId.IsNull ? _lastGripOwner : br.ObjectId;

        /// <summary>
        /// Zapisuje do bazy zmiany z zakończonego grip-dragu (wołane z CommandEnded).
        /// </summary>
        internal static void ApplyPendingGripEdits(Database db)
        {
            ClearGripTransients();
            if (db == null || (_pendingSpan.Count == 0 && _pendingAnnotMove.Count == 0 && _pendingStart.Count == 0)) return;

            var spans = new Dictionary<ObjectId, (double span, double skewEnd)>(_pendingSpan);
            var moves = new Dictionary<ObjectId, Vector3d>(_pendingAnnotMove);
            var starts = new Dictionary<ObjectId, (double shift, double span, double skewStart)>(_pendingStart);
            _pendingSpan.Clear();
            _pendingAnnotMove.Clear();
            _pendingStart.Clear();

            foreach (var kv in starts)
            {
                if (kv.Key.IsNull || kv.Key.IsErased || kv.Key.Database != db) continue;
                try { ApplyStartStretch(db, kv.Key, kv.Value.shift, kv.Value.span, kv.Value.skewStart); }
                catch (System.Exception ex) { Log.Error($"ApplyPendingGripEdits.Start {kv.Key}", ex); }
            }

            foreach (var kv in spans)
            {
                if (kv.Key.IsNull || kv.Key.IsErased || kv.Key.Database != db) continue;
                try
                {
                    BarData updated = null;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var br = tr.GetObject(kv.Key, OpenMode.ForWrite) as BlockReference;
                        if (br != null)
                        {
                            BarBlockEngine.RegenerateBarBlock(br, kv.Value.span, newSkewEnd: kv.Value.skewEnd);
                            updated = BarBlockEngine.ReadXData(br);
                        }
                        tr.Commit();
                    }
                    if (updated != null)
                    {
                        AnnotationEngine.SyncAnnotation(db, updated);
                        AnnotationEngine.UpdateBarLabelCount(db, updated.SourceBarHandle ?? "",
                                                             markOverride: updated.Mark);
                    }
                }
                catch (System.Exception ex) { Log.Error($"ApplyPendingGripEdits.Span {kv.Key}", ex); }
            }

            foreach (var kv in moves)
            {
                if (kv.Key.IsNull || kv.Key.IsErased || kv.Key.Database != db) continue;
                try
                {
                    using var tr = db.TransactionManager.StartTransaction();
                    var annot = tr.GetObject(kv.Key, OpenMode.ForWrite) as BlockReference;
                    if (annot != null)
                        annot.Position = annot.Position + kv.Value;
                    tr.Commit();
                }
                catch (System.Exception ex) { Log.Error($"ApplyPendingGripEdits.Move {kv.Key}", ex); }
            }
        }

        private static ObjectId ResolveAnnotId(BlockReference br, BarData barBlock)
        {
            if (br.Database == null || string.IsNullOrEmpty(barBlock.AnnotHandle)) return ObjectId.Null;
            if (!long.TryParse(barBlock.AnnotHandle, System.Globalization.NumberStyles.HexNumber,
                               null, out long hv)) return ObjectId.Null;
            if (br.Database.TryGetObjectId(new Handle(hv), out ObjectId id) && !id.IsNull && !id.IsErased)
                return id;
            return ObjectId.Null;
        }

        /// <summary>Podgląd span-grip: linie prętów dla nowego BarsSpan/SkewEnd (bez zapisu do bazy).</summary>
        private static void DrawSpanPreview(BlockReference br, BarData bar, double newBarsSpan, double newSkewEnd)
        {
            ClearGripTransients();
            if (bar.Spacing <= 0) return;
            int count = Math.Max(1, (int)(newBarsSpan / bar.Spacing + 1e-9) + 1);
            if (count > 2000) count = 2000; // bezpiecznik na absurdalny drag
            var xf = br.BlockTransform;
            for (int i = 0; i < count; i++)
            {
                double along = i * bar.Spacing;
                double frac  = count > 1 ? (double)i / (count - 1) : 0.0;
                double shift = bar.SkewStart + frac * (newSkewEnd - bar.SkewStart);
                Point3d a, b;
                if (bar.Direction == "X") { a = new Point3d(shift, along, 0); b = new Point3d(shift + bar.LengthA, along, 0); }
                else                      { a = new Point3d(along, shift, 0); b = new Point3d(along, shift + bar.LengthA, 0); }
                AddGripTransientLine(a.TransformBy(xf), b.TransformBy(xf), 4);
            }
        }

        /// <summary>Podgląd grip [0]: pręty od nowego początku (shift) co rozstaw, do niezmienionego drugiego końca.</summary>
        private static void DrawStartPreview(BlockReference br, BarData bar, double shift, int count, double newSkewStart)
        {
            ClearGripTransients();
            var xf = br.BlockTransform;
            for (int i = 0; i < count; i++)
            {
                double along = shift + i * bar.Spacing;
                double frac  = count > 1 ? (double)i / (count - 1) : 0.0;
                double sk    = newSkewStart + frac * (bar.SkewEnd - newSkewStart);
                Point3d a, b;
                if (bar.Direction == "X") { a = new Point3d(sk, along, 0); b = new Point3d(sk + bar.LengthA, along, 0); }
                else                      { a = new Point3d(along, sk, 0); b = new Point3d(along, sk + bar.LengthA, 0); }
                AddGripTransientLine(a.TransformBy(xf), b.TransformBy(xf), 4);
            }
        }

        /// <summary>
        /// Zapis grip [0]: początek rozkładu przesunięty o shift wzdłuż rozkładu, nowy BarsSpan i SkewStart.
        /// Strefy cięcia (otwory) i ręczna widoczność prętów przeliczone do nowego początku; opis podąża.
        /// </summary>
        private static void ApplyStartStretch(Database db, ObjectId id, double shift, double newSpan, double newSkewStart)
        {
            BarData updated = null;
            Vector3d move;
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var br = tr.GetObject(id, OpenMode.ForWrite) as BlockReference;
                var bar = br != null ? BarBlockEngine.ReadXData(br) : null;
                if (bar == null) { tr.Commit(); return; }

                var alongLocal = bar.Direction == "X" ? Vector3d.YAxis : Vector3d.XAxis;
                move = alongLocal.RotateBy(br.Rotation, Vector3d.ZAxis) * shift;

                // Strefy cięcia są w układzie bloku → po przesunięciu początku s maleje o shift
                if (!string.IsNullOrEmpty(bar.CutZones))
                    bar.CutZones = BarBlockEngine.FormatCutZones(
                        BarBlockEngine.ParseCutZones(bar.CutZones)
                                      .Select(z => (z.s0 - shift, z.s1 - shift, z.a0, z.a1)));
                // Ręcznie widoczne pręty liczone od początku → przesuń indeksy o liczbę dodanych/ubyłych
                if (bar.VisibilityMode == BarVisibilityMode.Manual && !string.IsNullOrEmpty(bar.VisibleIndices))
                {
                    int k = (int)Math.Round(shift / bar.Spacing);
                    var idx = bar.VisibleIndices.Split(',')
                                 .Select(t => int.TryParse(t.Trim(), out int v) ? v - k : -1)
                                 .Where(v => v >= 0).ToList();
                    bar.VisibleIndices = idx.Count > 0 ? string.Join(",", idx) : "0";
                }
                BarBlockEngine.WriteXData(br, bar);

                AnnotOverruleState.InGripDrag = true;   // bez auto-sync annotacji przy przesunięciu
                try { br.Position = br.Position + move; }
                finally { AnnotOverruleState.InGripDrag = false; }

                BarBlockEngine.RegenerateBarBlock(br, newSpan, newSkewStart: newSkewStart);
                updated = BarBlockEngine.ReadXData(br);

                var annotId = ResolveAnnotId(br, updated);
                if (!annotId.IsNull && tr.GetObject(annotId, OpenMode.ForWrite) is BlockReference annot)
                    annot.Position = annot.Position + move;
                tr.Commit();
            }
            if (updated != null)
            {
                AnnotationEngine.SyncAnnotation(db, updated);
                AnnotationEngine.UpdateBarLabelCount(db, updated.SourceBarHandle ?? "", markOverride: updated.Mark);
            }
        }

        /// <summary>Podgląd annotacji przesuniętej o offset — BlockReference poza bazą jako transient.</summary>
        private static void DrawAnnotGhost(Database db, ObjectId annotId, Vector3d offset)
        {
            if (db == null) return;
            using var tr = db.TransactionManager.StartTransaction();
            var annot = tr.GetObject(annotId, OpenMode.ForRead) as BlockReference;
            if (annot != null)
            {
                var ghost = new BlockReference(annot.Position + offset, annot.BlockTableRecord)
                {
                    Rotation     = annot.Rotation,
                    ScaleFactors = annot.ScaleFactors,
                    Normal       = annot.Normal,
                    ColorIndex   = 4,
                };
                try
                {
                    TransientManager.CurrentTransientManager.AddTransient(
                        ghost, TransientDrawingMode.DirectShortTerm, 128, new IntegerCollection());
                    _gripTransients.Add(ghost);
                }
                catch { ghost.Dispose(); }
            }
            tr.Commit();
        }

        public override bool IsApplicable(RXObject overruledSubject)
        {
            try
            {
                if (!(overruledSubject is BlockReference br)) return false;
                return BarBlockEngine.IsBarBlock(br) || AnnotationEngine.IsAnnotation(br);
            }
            catch { return false; }
        }

        public override void GetGripPoints(
            Entity entity,
            Point3dCollection gripPoints,
            IntegerCollection snapModes,
            IntegerCollection geometryIds)
        {
            var br = entity as BlockReference;
            if (br == null) { base.GetGripPoints(entity, gripPoints, snapModes, geometryIds); return; }

            // --- Modul pretow RC_BAR_BLOCK ---
            var barBlock = BarBlockEngine.ReadXData(br);
            if (barBlock != null)
            {
                // Nowa interakcja — wyczysc stan dragu pozycji
                _dragOrigPos.Remove(br.ObjectId.Handle.Value);
                _dragOrigPos.Remove(0L);          // wyczyść też cache klonu z poprzedniego drag
                _annotDragStart.Remove(br.ObjectId.Handle.Value);
                _annotDragStart.Remove(0L);
                if (!br.ObjectId.IsNull) _lastGripOwner = br.ObjectId;
                ClearGripTransients();

                gripPoints.Add(BarBlockEngine.GripLateral(br, barBlock)); // [0] krawedz plyty (cover offset)
                gripPoints.Add(BarBlockEngine.GripSpan(br, barBlock)); // [1] span resize
                return;
            }

            // --- Modul annotacji RC_BAR_ANNOT ---
            var barAnnot = AnnotationEngine.ReadAnnotXData(br);
            if (barAnnot != null && barAnnot.ArmTotalLen > 0)
            {
                // Wyczysc stan dragu — poczatek nowej interakcji z gripem
                long hv = br.ObjectId.Handle.Value;
                _dragOrigArm.Remove(hv);
                _dragOrigPos.Remove(hv);
                _dragStartSegLen.Remove(hv);
                _dragStartSegLen.Remove(0L);
                _dragStartLastPt.Remove(hv);
                _dragStartLastPt.Remove(0L);
                _dragElbow.Remove(hv);
                _dragElbow.Remove(0L);
                ClearGripTransients();


                // Grip[0]: środek skośnej dist line — midSkew uwzględnia SkewStart/SkewEnd
                double lastBar0  = (barAnnot.Count - 1) * barAnnot.Spacing;
                double midAlong0 = lastBar0 / 2.0;
                double midSkew0  = (barAnnot.SkewStart + barAnnot.SkewEnd) / 2.0;

                // [p279] Uwzględnij localOffset (block.Pos - annot.Pos) żeby grip[0] UI był
                // na środku rozkładu prętów w WCS, niezależnie od pozycji annot.
                // Bez tego po MOVE annot solo grip[0] zeskakuje wraz z annot.
                Vector3d localOff0 = new Vector3d(0, 0, 0);
                if (!string.IsNullOrEmpty(barAnnot.SourceBlockHandle))
                {
                    try
                    {
                        long hvSrc = Convert.ToInt64(
                            barAnnot.SourceBlockHandle.TrimStart('0').PadLeft(1, '0'), 16);
                        if (br.Database.TryGetObjectId(new Handle(hvSrc), out ObjectId srcId0)
                            && !srcId0.IsNull && !srcId0.IsErased)
                        {
                            using (var trG = br.Database.TransactionManager.StartTransaction())
                            {
                                var srcBr0 = trG.GetObject(srcId0, OpenMode.ForRead) as BlockReference;
                                if (srcBr0 != null)
                                {
                                    // w układzie bloku annotacji (obrócony opis — wcześniej grip lądował daleko)
                                    var d0 = (srcBr0.Position - br.Position).RotateBy(-br.Rotation, Vector3d.ZAxis);
                                    localOff0 = (barAnnot.Direction == "X")
                                        ? new Vector3d(0, d0.Y, 0)
                                        : new Vector3d(d0.X, 0, 0);
                                }
                                trG.Commit();
                            }
                        }
                    }
                    catch { }
                }

                Point3d localGrip0 = (barAnnot.Direction == "X")
                    ? new Point3d(midSkew0, midAlong0 + localOff0.Y, 0)
                    : new Point3d(midAlong0 + localOff0.X, midSkew0, 0);
                Point3d grip0 = localGrip0.TransformBy(br.BlockTransform);
                gripPoints.Add(grip0);  // [0] lateral

                // Grip[1]: punkt początku tekstu (z uwzględnieniem Down)
                Point3d grip1 = grip0;  // fallback
                var ptsGrip = AnnotationEngine.DecodeLeaderPoints(barAnnot.LeaderPoints);
                if (ptsGrip.Count > 0)
                {
                    var lastLocal = ptsGrip[ptsGrip.Count - 1];
                    grip1 = lastLocal.TransformBy(br.BlockTransform);
                }
                gripPoints.Add(grip1);  // [1] arm end

                // [2] załamanie leadera (rozkład skośny/obrócony) — długość odcinka skośnego
                if (AnnotationEngine.HasElbowGrip(barAnnot, br.Rotation, ptsGrip))
                    gripPoints.Add(ptsGrip[1].TransformBy(br.BlockTransform));

                return;
            }

            base.GetGripPoints(entity, gripPoints, snapModes, geometryIds);
        }

        // ----------------------------------------------------------------
        // MoveGripPointsAt — wersja GripDataCollection (gdy BRX uzywa custom GripData)
        // ----------------------------------------------------------------
        public override void MoveGripPointsAt(
            Entity entity,
            GripDataCollection grips,
            Vector3d offset,
            MoveGripPointsFlags bitFlags)
        {
            var br = entity as BlockReference;
            if (br == null) { base.MoveGripPointsAt(entity, grips, offset, bitFlags); return; }

            // Wyznacz indeksy przeciaganych gripow po ich pozycji
            bool isGrip1 = false;
            {
                var annE = AnnotationEngine.ReadAnnotXData(br);
                if (annE != null)
                {
                    var ptsE = AnnotationEngine.DecodeLeaderPoints(annE.LeaderPoints);
                    if (AnnotationEngine.HasElbowGrip(annE, br.Rotation, ptsE))
                    {
                        var elbowW = ptsE[1].TransformBy(br.BlockTransform);
                        foreach (GripData gd in grips)
                            if (IsNear(gd.GripPoint, elbowW)) { DragElbow(br, offset); return; }
                    }
                }
            }
            foreach (GripData gd in grips)
            {
                var barBlock = BarBlockEngine.ReadXData(br);
                if (barBlock != null)
                {
                    // Grip [0] jest na krawedzi plyty (cover od insertion point), [1] na span
                    bool nearSpan    = IsNear(gd.GripPoint, BarBlockEngine.GripSpan(br, barBlock));
                    bool nearLateral = IsNear(gd.GripPoint, BarBlockEngine.GripLateral(br, barBlock));
                    isGrip1 = nearSpan && !nearLateral;
                    break;
                }
                var barAnnot = AnnotationEngine.ReadAnnotXData(br);
                if (barAnnot != null)
                {
                    var ins = br.Position;
                    Point3d armTop;
                    if (barAnnot.Direction == "X" && !barAnnot.LeaderHorizontal)
                        armTop = new Point3d(ins.X, ins.Y + barAnnot.BarsSpan + barAnnot.ArmTotalLen, 0);
                    else if (barAnnot.Direction == "X" && barAnnot.LeaderHorizontal)
                    {
                        double currentMidY2 = AnnotationEngine.GetArmMidY(br);
                        double hDir2 = barAnnot.LeaderRight ? 1.0 : -1.0;
                        armTop = new Point3d(ins.X + hDir2 * barAnnot.ArmTotalLen, ins.Y + currentMidY2, 0);
                    }
                    else
                        armTop = new Point3d(ins.X + barAnnot.ArmTotalLen, ins.Y + barAnnot.BarsSpan / 2.0, 0);
                    isGrip1 = IsNear(gd.GripPoint, armTop);
                    break;
                }
            }
            // RC_BAR_ANNOT grip[0]: project offset onto bar axis so grip marker follows same path
            if (!isGrip1)
            {
                var barAnnotGdc = AnnotationEngine.ReadAnnotXData(br);
                if (barAnnotGdc != null)
                {
                    double localBarAngle = (barAnnotGdc.Direction == "X") ? 0.0 : Math.PI / 2.0;
                    double wcsAngle = br.Rotation + localBarAngle;
                    double dx = Math.Cos(wcsAngle), dy = Math.Sin(wcsAngle);
                    double proj = offset.X * dx + offset.Y * dy;
                    base.MoveGripPointsAt(entity, grips, new Vector3d(proj * dx, proj * dy, 0), bitFlags);
                    return;
                }
            }
            ApplyGripMove(entity, br, offset, isGrip1);
        }

        // ----------------------------------------------------------------
        // MoveGripPointsAt — wersja IntegerCollection (gdy BRX uzywa Point3dCollection grips)
        // BRX wywoluje TE wersje po GetGripPoints(Point3dCollection), nie GripDataCollection!
        // ----------------------------------------------------------------
        public override void MoveGripPointsAt(
            Entity entity,
            IntegerCollection indices,
            Vector3d offset)
        {
            var br = entity as BlockReference;
            if (br == null) { base.MoveGripPointsAt(entity, indices, offset); return; }

            bool isGrip1 = false;
            foreach (int idx in indices)
            {
                if (idx == 1) isGrip1 = true;
                if (idx == 2 && AnnotationEngine.IsAnnotation(br)) { DragElbow(br, offset); return; }
            }

            // RC_BAR_ANNOT grip[0]: project offset onto bar axis so grip marker follows same path
            if (!isGrip1)
            {
                var barAnnot = AnnotationEngine.ReadAnnotXData(br);
                if (barAnnot != null)
                {
                    double localBarAngle = (barAnnot.Direction == "X") ? 0.0 : Math.PI / 2.0;
                    double wcsAngle = br.Rotation + localBarAngle;
                    double dx = Math.Cos(wcsAngle), dy = Math.Sin(wcsAngle);
                    double proj = offset.X * dx + offset.Y * dy;
                    base.MoveGripPointsAt(entity, indices, new Vector3d(proj * dx, proj * dy, 0));
                    return;
                }
            }
            ApplyGripMove(entity, br, offset, isGrip1);
        }

        /// <summary>Grip [2] annotacji: przesunięcie załamania wzdłuż linii rozkładu (offset kumulatywny od startu dragu).</summary>
        private static void DragElbow(BlockReference br, Vector3d offset)
        {
            if (br.ObjectId.IsNull || br.Database == null) return;   // klon podglądu
            long handle = br.ObjectId.Handle.Value;
            try
            {
                if (!_dragElbow.ContainsKey(handle))
                {
                    BarData a = null;
                    using (var tr = br.Database.TransactionManager.StartTransaction())
                    {
                        var brT = tr.GetObject(br.ObjectId, OpenMode.ForRead) as BlockReference;
                        a = brT != null ? AnnotationEngine.ReadAnnotXData(brT) : null;
                        tr.Commit();
                    }
                    var pts = a != null ? AnnotationEngine.DecodeLeaderPoints(a.LeaderPoints) : null;
                    if (pts == null || pts.Count < 3) return;
                    var ax = pts[1] - pts[0];
                    if (ax.Length < 1e-9) return;
                    var outW = ax.GetNormal().TransformBy(br.BlockTransform);
                    outW = new Vector3d(outW.X, outW.Y, 0).GetNormal();
                    _dragElbow[handle] = (AnnotationEngine.GetElbowExt(a), outW);
                }
                var st = _dragElbow[handle];
                double newExt = st.ext + offset.DotProduct(st.outWcs);
                AnnotationEngine.SetElbowExtension(br, newExt);
            }
            catch (System.Exception ex) { Log.Error("AnnotGripOverrule.DragElbow", ex); }
        }

        // ----------------------------------------------------------------
        // ApplyGripMove — wspolna logika dla obu wersji MoveGripPointsAt
        // ----------------------------------------------------------------
        private static void ApplyGripMove(Entity entity, BlockReference br, Vector3d offset, bool isGrip1)
        {
            // --- RC_BAR_BLOCK ---
            // Zasada: w trakcie dragu (klon LUB prawdziwy obiekt — BricsCAD woła oba) NIE
            // zapisujemy do bazy BTR/annotacji/etykiety — tylko transienty + stan w _pending*.
            // Zapis raz, w CommandEnded (ApplyPendingGripEdits). ESC → ResetDragState, nic do cofania.
            // Wcześniej każda klatka dragu przebudowywała współdzielony BTR, annotację i etykietę
            // (lag, śmieci w UNDO, stan pośredni po ESC).
            var barBlock = BarBlockEngine.ReadXData(br);
            if (barBlock != null)
            {
                if (isGrip1)
                {
                    // Offset w WCS → układ lokalny bloku (obrócone rozkłady rozciągały się
                    // wcześniej w złym kierunku).
                    var localOff = offset.RotateBy(-br.Rotation, Vector3d.ZAxis);

                    // Dekompozycja offsetu: "wzdłuż BarsSpan" + "prostopadle" (skew)
                    double alongDelta, perpDelta;
                    if (barBlock.Direction == "X")
                    {
                        alongDelta = localOff.Y;  // BarsSpan wzdłuż Y
                        perpDelta  = localOff.X;  // skew wzdłuż X (kierunek prętów)
                    }
                    else
                    {
                        alongDelta = localOff.X;  // BarsSpan wzdłuż X
                        perpDelta  = localOff.Y;  // skew wzdłuż Y
                    }

                    // Tylko podgląd + zapamiętanie stanu. XData na blockref jest nietknięte,
                    // więc barBlock to zawsze stan sprzed dragu, a offset jest kumulatywny.
                    double newBarsSpan = Math.Max(0, barBlock.BarsSpan + alongDelta);
                    double newSkewEnd  = barBlock.SkewEnd + perpDelta;
                    DrawSpanPreview(br, barBlock, newBarsSpan, newSkewEnd);

                    var realId = RealId(br);
                    if (!realId.IsNull)
                        _pendingSpan[realId] = (newBarsSpan, newSkewEnd);
                }
                else
                {
                    // Grip [0] = krawędź (obwiednia) po stronie PIERWSZEGO pręta: pierwszy pręt zawsze
                    // dokładnie o otulinę od gripa (płynnie, bez skoku co rozstaw). Drugi koniec (grip [1])
                    // zostaje w miejscu — liczba prętów wynika z odległości, ostatni pręt ma faktyczny
                    // odstęp od krawędzi. Do przesuwania całego rozkładu jest MOVE.
                    var localOff = offset.RotateBy(-br.Rotation, Vector3d.ZAxis);
                    double alongDelta = barBlock.Direction == "X" ? localOff.Y : localOff.X;
                    double perpDelta  = barBlock.Direction == "X" ? localOff.X : localOff.Y;
                    if (barBlock.Spacing <= 0) return;

                    double shift    = Math.Min(alongDelta, barBlock.BarsSpan);   // początek nie za drugi koniec
                    double newSpan  = barBlock.BarsSpan - shift;                  // drugi koniec bez zmian
                    int    newCount = Math.Min(2000, (int)(newSpan / barBlock.Spacing + 1e-9) + 1);
                    double newSkewStart = barBlock.SkewStart + perpDelta;

                    DrawStartPreview(br, barBlock, shift, newCount, newSkewStart);
                    var realId = RealId(br);
                    if (!realId.IsNull) _pendingStart[realId] = (shift, newSpan, newSkewStart);
                }
                return;
            }

            // --- RC_BAR_ANNOT ---
            var barAnnot = AnnotationEngine.ReadAnnotXData(br);
            if (barAnnot != null)
            {
                if (isGrip1)
                {
                    long handle = br.ObjectId.IsNull ? 0L : br.ObjectId.Handle.Value;
                    if (handle == 0) return;  // preview — zostaw BricsCAD

                    // Odczytaj aktualny lastPt z XData
                    List<Point3d> pts = null;
                    using (var trT = br.Database.TransactionManager.StartTransaction())
                    {
                        var brT = trT.GetObject(br.ObjectId, OpenMode.ForRead) as BlockReference;
                        var barT = brT != null ? AnnotationEngine.ReadAnnotXData(brT) : null;
                        if (barT != null && !string.IsNullOrEmpty(barT.LeaderPoints))
                            pts = AnnotationEngine.DecodeLeaderPoints(barT.LeaderPoints);
                        trT.Commit();
                    }

                    if (pts == null || pts.Count < 1)
                    {
                        _dragStartLastPt.Remove(handle);
                        return;
                    }

                    // Zapamiętaj start pozycji tekstu przy pierwszym wywołaniu
                    // (żeby offset był kumulatywny od startu, nie od klatki)
                    var lastLocal = pts[pts.Count - 1];
                    var lastWCS = lastLocal.TransformBy(br.BlockTransform);

                    if (!_dragStartLastPt.ContainsKey(handle))
                        _dragStartLastPt[handle] = lastWCS;
                    var startWCS = _dragStartLastPt[handle];

                    // Kierunek ostatniego segmentu (WCS)
                    if (pts.Count < 2)
                    {
                        AnnotationEngine.UpdateLeaderInBlock(br, startWCS + offset);
                        return;
                    }
                    var segLocal = pts[pts.Count - 1] - pts[pts.Count - 2];
                    var segWCS = segLocal.TransformBy(br.BlockTransform);
                    segWCS = new Vector3d(segWCS.X, segWCS.Y, 0);
                    if (segWCS.Length < 0.01)
                    {
                        AnnotationEngine.UpdateLeaderInBlock(br, startWCS + offset);
                        return;
                    }
                    var segDir = segWCS.GetNormal();
                    var perpDir = new Vector3d(-segDir.Y, segDir.X, 0);

                    // Rozłóż offset od startu dragu
                    double projAlong = offset.X * segDir.X  + offset.Y * segDir.Y;
                    double projPerp  = offset.X * perpDir.X + offset.Y * perpDir.Y;

                    Vector3d perpShift, alongShift;
                    if (pts.Count == 2)
                    {
                        // Leader prosty bez kinku — tylko wydłużanie wzdłuż segmentu
                        perpShift  = new Vector3d(0, 0, 0);
                        alongShift = segDir * projAlong;
                    }
                    else if (Math.Abs(barAnnot.SkewEnd - barAnnot.SkewStart) > 1e-6
                             && (barAnnot.Direction == "X" ? Math.Abs(segLocal.X) < 1e-3 : Math.Abs(segLocal.Y) < 1e-3))
                    {
                        // Rozkład skośny: ramię przypięte do KOŃCA linii rozkładu (ostatni odcinek wzdłuż osi
                        // rozkładu) — przeciąganie tekstu tylko wydłuża/skraca ramię, punkt załamania zostaje.
                        perpShift  = new Vector3d(0, 0, 0);
                        alongShift = segDir * projAlong;
                    }
                    else
                    {
                        // Leader z kinkiem — decompose: perp przesuwa segment, along wydłuża
                        perpShift  = perpDir * projPerp;
                        alongShift = segDir * projAlong;
                    }

                    AnnotationEngine.UpdateLastSegmentWithShift(br, perpShift, alongShift);
                    return;
                }
                else
                {
                    // Grip [0]: ruch boczny wzdłuż osi prętów.
                    // offset jest KUMULATYWNY — tak samo jak dla RC_BAR_BLOCK grip[0].
                    long handle = br.ObjectId.Handle.Value;
                    _dragOrigArm.Remove(handle);
                    if (!_dragOrigPos.ContainsKey(handle))
                        _dragOrigPos[handle] = br.Position;

                    var origPos = _dragOrigPos[handle];

                    Point3d newPos;
                    if (Math.Abs(br.Rotation) > 1e-6)
                    {
                        // Obrócony blok — kierunek prętów w WCS = rotation + lokalna oś pręta
                        double localBarAngle = (barAnnot.Direction == "X") ? 0.0 : Math.PI / 2.0;
                        double wcsAngle = br.Rotation + localBarAngle;
                        double dx = Math.Cos(wcsAngle);
                        double dy = Math.Sin(wcsAngle);
                        double proj = offset.X * dx + offset.Y * dy;
                        newPos = new Point3d(
                            origPos.X + proj * dx,
                            origPos.Y + proj * dy,
                            origPos.Z);
                    }
                    else if (barAnnot.Direction == "X")
                    {
                        // X-bars: pręty wzdłuż X → ruch wzdłuż X (równolegle do prętów)
                        newPos = new Point3d(origPos.X + offset.X, origPos.Y, origPos.Z);
                    }
                    else
                    {
                        // Y-bars: pręty wzdłuż Y → ruch wzdłuż Y (równolegle do prętów)
                        newPos = new Point3d(origPos.X, origPos.Y + offset.Y, origPos.Z);
                    }

                    br.UpgradeOpen();
                    br.Position = newPos;
                }
                return;
            }
        }

        private static bool IsNear(Point3d a, Point3d b) => (a - b).LengthSqrd < 4.0;

        private static Vector3d ConstrainOffset(BarData bar, Vector3d offset)
            => bar.Direction == "X"
                ? new Vector3d(offset.X, 0, 0)
                : new Vector3d(0, offset.Y, 0);
    }

    internal static class AnnotOverruleState
    {
        public static bool InGripDrag { get; set; } = false;
        /// <summary>Obrót annotacji razem z prętami (ApplyPendingRotations) — bez własnej logiki ATR.</summary>
        public static bool SuppressAnnotTransform { get; set; } = false;
    }

    // ----------------------------------------------------------------
    // TransformOverrule — ogranicza MOVE annotacji do osi zbrojenia.
    // Blok pretow (RC_BAR_BLOCK): pass-through (swobodny ruch).
    // UWAGA: IsApplicable zwraca true dla OBU typow — wewnatrz TransformBy
    //        rozrozniamy i dla pretow wywolujemy base bez ograniczen.
    //        (IsApplicable-only exclusion nie dziala niezawodnie w BRX.)
    // ----------------------------------------------------------------
    public class AnnotTransformOverrule : TransformOverrule
    {
        public override bool IsApplicable(RXObject overruledSubject)
        {
            try
            {
                if (!(overruledSubject is BlockReference br)) return false;
                return BarBlockEngine.IsBarBlock(br) || AnnotationEngine.IsAnnotation(br);
            }
            catch { return false; }
        }

        public override void TransformBy(Entity entity, Matrix3d transform)
        {
            // p258 ETAP 1 — Annot przesuwa się swobodnie (ASD-style: independent).
            base.TransformBy(entity, transform);
            if (AnnotOverruleState.SuppressAnnotTransform) return;

            // p265 — Po MOVE annot solo, dist line pozostaje na prętach: rebuild z nowym
            // offsetem = block.Position (niezmienione) − annot.Position (po move).
            if (!(entity is BlockReference annotBr)) return;
            var db = annotBr.Database;
            if (db == null) return;

            // [p273] COPY timing window: BTR jeszcze shared z oryginałem, SourceBlockHandle stale.
            // Rebuild zbędny — block+annot przesunięte o ten sam wektor → localOffset niezmieniony.
            if (BarCopyWatcher.IsCopyPending(annotBr.ObjectId)) return;

            var annotData = AnnotationEngine.ReadAnnotXData(annotBr);
            if (annotData == null || string.IsNullOrEmpty(annotData.SourceBlockHandle)) return;

            try
            {
                long hVal = Convert.ToInt64(
                    annotData.SourceBlockHandle.TrimStart('0').PadLeft(1, '0'), 16);
                if (!db.TryGetObjectId(new Handle(hVal), out ObjectId srcId)
                    || srcId.IsNull || srcId.IsErased) return;

                Point3d blockPos;
                BarData freshBarData;
                using (var tr = db.TransactionManager.StartTransaction())
                {
                    var srcBr = tr.GetObject(srcId, OpenMode.ForRead) as BlockReference;
                    if (srcBr == null) { tr.Commit(); return; }
                    blockPos     = srcBr.Position;
                    freshBarData = BarBlockEngine.ReadXData(srcBr);
                    tr.Commit();
                }

                // [p281] Kink compensation: po MOVE annot solo kink (pts[1..N-2])
                // musi zostać WCS-stable żeby arm nie rósł z odległością tekstu od bloku.
                // Kompensujemy lokalne BTR coords przez -V_local. Text (pts[N-1]) zostaje
                // (intuicyjne: MOVE annot = przesuń tekst). Dla bez-kinka (pts.Count==2)
                // loop pusty — status quo.
                if (transform.Translation.Length > 1e-6 && !BarBlockTransformOverrule.HasRotation(transform))
                {
                    var kinkPts = AnnotationEngine.DecodeLeaderPoints(annotData.LeaderPoints);
                    if (kinkPts.Count >= 3)
                    {
                        var vLocal = transform.Translation.TransformBy(
                            annotBr.BlockTransform.Inverse());
                        for (int i = 1; i < kinkPts.Count - 1; i++)
                            kinkPts[i] = kinkPts[i] - vLocal;
                        annotData.LeaderPoints = AnnotationEngine.EncodeLeaderPoints(kinkPts);
                        using (var trW = db.TransactionManager.StartTransaction())
                        {
                            var brW = trW.GetObject(annotBr.ObjectId, OpenMode.ForWrite)
                                        as BlockReference;
                            if (brW != null)
                                AnnotationEngine.WriteAnnotXData(brW, annotData);
                            trW.Commit();
                        }
                    }
                }

                if (freshBarData != null)
                    AnnotationEngine.RebuildDistLineInBtr(annotBr, freshBarData, db, blockPos);
            }
            catch { }
        }
    }

    // ----------------------------------------------------------------
    // BarBlockTransformOverrule — synchronizuje RC_BAR_ANNOT przy każdym
    // MOVE/COPY/obrót bloku RC_BAR_BLOCK.
    // SetXDataFilter("RC_BAR_BLOCK") ogranicza działanie tylko do bloków
    // z tym XData — inne BlockReference są pomijane bez kosztu.
    // ----------------------------------------------------------------
    internal class BarBlockTransformOverrule : TransformOverrule
    {
        public override void TransformBy(Entity entity, Matrix3d transform)
        {
            base.TransformBy(entity, transform);

            if (AnnotOverruleState.InGripDrag) return;

            if (!(entity is BlockReference br)) return;

            // [p273] COPY timing window: analogicznie jak ATR.
            if (BarCopyWatcher.IsCopyPending(br.ObjectId)) return;

            var db = br.Database;
            if (db == null) return;

            string annotHandle = ReadAnnotHandle(br);
            if (string.IsNullOrEmpty(annotHandle)) return;

            // ROTATE prętów: annotacja obraca się razem z nimi — w CommandEnded (ApplyPendingRotations),
            // bo gdy annotacja też jest zaznaczona, BricsCAD obraca ją sam (nie wolno obrócić drugi raz).
            if (HasRotation(transform))
            {
                if (!br.ObjectId.IsNull)
                    _pendingRot[br.ObjectId] = _pendingRot.TryGetValue(br.ObjectId, out var prev)
                        ? transform * prev : transform;
                return;
            }

            var translation = transform.Translation;
            if (translation.Length < 0.001) return;

            try
            {
                using var tr = db.TransactionManager.StartTransaction();

                if (!long.TryParse(annotHandle,
                        NumberStyles.HexNumber, null, out long hVal)) { tr.Commit(); return; }

                var handle = new Handle(hVal);
                if (!db.TryGetObjectId(handle, out ObjectId annotId)
                    || annotId.IsNull || !annotId.IsValid) { tr.Commit(); return; }

                var annotBr = tr.GetObject(annotId, OpenMode.ForWrite) as BlockReference;
                if (annotBr == null) { tr.Commit(); return; }

                // Safety check: link może być "stale" po COPY — XData kopii zawiera handle oryginału.
                // Prawdziwy link istnieje tylko gdy annot.SourceBlockHandle wskazuje na NAS.
                string myHandleHex = br.Handle.Value.ToString("X8");
                var annotData = AnnotationEngine.ReadAnnotXData(annotBr);
                string annotSourceHex = (annotData?.SourceBlockHandle ?? "").ToUpperInvariant();
                if (!string.Equals(annotSourceHex, myHandleHex, StringComparison.OrdinalIgnoreCase))
                {
                    tr.Commit();
                    return;
                }

                // p259 ETAP 3 — Po move block, dist line/doty rebuildują się żeby
                // trafić w nowe pozycje prętów. annot.Position bez zmian (ASD-style).
                try
                {
                    var freshBarData = BarBlockEngine.ReadXData(br);
                    if (freshBarData != null)
                        AnnotationEngine.RebuildDistLineInBtr(annotBr, freshBarData, db, br.Position);
                }
                catch { }

                tr.Commit();
            }
            catch { }
        }

        public new void SetCustomFilter()
        {
            this.SetXDataFilter("RC_BAR_BLOCK");
        }

        private static string ReadAnnotHandle(BlockReference br)
        {
            // Wcześniej: "pierwszy 8-znakowy hex string w XData" (zgadywanie; psuło się gdy
            // AnnotHandle pusty albo zapisany jako handle 1005). Teraz: właściwe pole [11].
            string h = BarBlockEngine.ReadXData(br)?.AnnotHandle;
            return string.IsNullOrEmpty(h) ? null : h;
        }

        // ── Obrót prętów → obrót annotacji ────────────────────────────────
        private static readonly Dictionary<ObjectId, Matrix3d> _pendingRot = new Dictionary<ObjectId, Matrix3d>();

        internal static bool HasRotation(Matrix3d m)
        {
            var x = Vector3d.XAxis.TransformBy(m);
            var y = Vector3d.YAxis.TransformBy(m);
            if (x.X * y.Y - x.Y * y.X <= 0) return false;   // lustro — nie obrót
            return x.Length > 1e-9 && Math.Abs(Math.Atan2(x.Y, x.X)) > 1e-9;
        }

        internal static void DiscardPendingRotations() => _pendingRot.Clear();

        private static double AngleDiff(double a, double b)
        {
            double d = (a - b) % (2 * Math.PI);
            if (d > Math.PI) d -= 2 * Math.PI;
            if (d < -Math.PI) d += 2 * Math.PI;
            return Math.Abs(d);
        }

        /// <summary>
        /// Po komendzie z obrotem prętów: annotacja (jeśli sama nie była obracana) dostaje ten sam obrót,
        /// kąt rozkładu w XData [23] aktualizowany, opis przebudowany (leader jak w ASD).
        /// </summary>
        internal static void ApplyPendingRotations(Database db)
        {
            if (db == null || _pendingRot.Count == 0) return;
            var items = new Dictionary<ObjectId, Matrix3d>(_pendingRot);
            _pendingRot.Clear();

            foreach (var kv in items)
            {
                if (kv.Key.IsNull || kv.Key.IsErased || kv.Key.Database != db) continue;
                try
                {
                    BarData bd = null;
                    using (var tr = db.TransactionManager.StartTransaction())
                    {
                        var br = tr.GetObject(kv.Key, OpenMode.ForWrite) as BlockReference;
                        bd = br != null ? BarBlockEngine.ReadXData(br) : null;
                        if (bd == null) { tr.Commit(); continue; }
                        bd.Angle = br.Rotation;
                        BarBlockEngine.WriteXData(br, bd);

                        if (!string.IsNullOrEmpty(bd.AnnotHandle)
                            && long.TryParse(bd.AnnotHandle, NumberStyles.HexNumber, null, out long hv)
                            && db.TryGetObjectId(new Handle(hv), out ObjectId annotId)
                            && !annotId.IsNull && !annotId.IsErased
                            && tr.GetObject(annotId, OpenMode.ForWrite) is BlockReference annot)
                        {
                            var ad = AnnotationEngine.ReadAnnotXData(annot);
                            bool mine = ad != null && XLink.Same(ad.SourceBlockHandle, br.Handle.Value.ToString("X8"));
                            if (mine && AngleDiff(annot.Rotation, br.Rotation) > 1e-6)
                            {
                                AnnotOverruleState.SuppressAnnotTransform = true;
                                try { annot.TransformBy(kv.Value); }
                                finally { AnnotOverruleState.SuppressAnnotTransform = false; }
                            }
                        }
                        tr.Commit();
                    }
                    if (bd != null) AnnotationEngine.SyncAnnotation(db, bd);
                }
                catch (System.Exception ex) { Log.Error($"BarBlockTransformOverrule.ApplyPendingRotations {kv.Key}", ex); }
            }
        }
    }

    // ----------------------------------------------------------------
    // EraseOverrule — gdy RC_BAR_BLOCK jest usuwany, automatycznie
    // usuwa powiązany RC_BAR_ANNOT (jeśli istnieje i nie jest już usunięty).
    // SetXDataFilter("RC_BAR_BLOCK") ogranicza działanie tylko do bloków z tą XData.
    // ----------------------------------------------------------------
    internal class BarBlockEraseOverrule : ObjectOverrule
    {
        public override void Erase(DBObject dbObject, bool erasing)
        {
            base.Erase(dbObject, erasing);

            if (!erasing) return;
            if (!(dbObject is BlockReference br)) return;

            var db = br.Database;
            if (db == null) return;

            try
            {
                // Zawsze zarejestruj source bar do aktualizacji etykiety — niezależnie od stanu annotacji.
                var barXd = BarBlockEngine.ReadXData(br);
                if (barXd != null && !string.IsNullOrEmpty(barXd.SourceBarHandle))
                    PendingLabelUpdates.Add(db, barXd.SourceBarHandle);

                BarBlockEngine.ClearSkewCache(br.BlockTableRecord.Handle.Value);

                // UNDO (Ctrl+Z, „Cofnij” w COPY) samo odtwarza powiązane obiekty — bez kaskady.
                // Wcześniej cofnięcie kopii rozkładu kasowało opis ORYGINAŁU (kopia wskazywała jego opis).
                if (dbObject.IsUndoing) return;

                // Opcjonalnie usuń powiązaną annotację (jeśli istnieje).
                string annotHandle = ReadAnnotHandle(br);
                if (string.IsNullOrEmpty(annotHandle)) return;

                using var tr = db.TransactionManager.StartTransaction();

                if (!long.TryParse(annotHandle, NumberStyles.HexNumber, null, out long hVal))
                { tr.Commit(); return; }

                var handle = new Handle(hVal);
                if (!db.TryGetObjectId(handle, out ObjectId annotId)
                    || annotId.IsNull || !annotId.IsValid || annotId.IsErased)
                { tr.Commit(); return; }

                var annotBr = tr.GetObject(annotId, OpenMode.ForRead) as BlockReference;
                if (annotBr == null) { tr.Commit(); return; }

                // Kasuj opis tylko, gdy naprawdę należy do TEGO rozkładu (back-link [16] opisu).
                // Kopia rozkładu skopiowana bez opisu wskazuje opis oryginału — nie wolno go usunąć.
                var annotData = AnnotationEngine.ReadAnnotXData(annotBr);
                if (annotData != null && !string.IsNullOrEmpty(annotData.SourceBlockHandle)
                    && !XLink.Same(annotData.SourceBlockHandle, br.Handle.Value.ToString("X8")))
                { tr.Commit(); return; }

                annotBr.UpgradeOpen();
                annotBr.Erase(true);
                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("BarBlockEraseOverrule.Erase", ex); }
        }

        public new void SetCustomFilter()
        {
            this.SetXDataFilter("RC_BAR_BLOCK");
        }

        private static string ReadAnnotHandle(BlockReference br)
        {
            // Wcześniej: "pierwszy 8-znakowy hex string w XData" (zgadywanie; psuło się gdy
            // AnnotHandle pusty albo zapisany jako handle 1005). Teraz: właściwe pole [11].
            string h = BarBlockEngine.ReadXData(br)?.AnnotHandle;
            return string.IsNullOrEmpty(h) ? null : h;
        }
    }

    // ----------------------------------------------------------------
    // BarPolylineEraseOverrule — usunięcie RC_SINGLE_BAR usuwa MLeadera etykiety
    // ----------------------------------------------------------------

    /// <summary>
    /// Gdy polilinia pręta (RC_SINGLE_BAR) jest usuwana, usuwa też powiązany MLeader etykiety.
    /// </summary>
    internal class BarPolylineEraseOverrule : ObjectOverrule
    {
        public override void Erase(DBObject dbObject, bool erasing)
        {
            base.Erase(dbObject, erasing);

            if (!erasing) return;
            if (dbObject is not Teigha.DatabaseServices.Polyline pline) return;

            var db = pline.Database;
            if (db == null) return;

            // UNDO samo odtwarza etykietę i rozkłady — bez kaskady
            if (dbObject.IsUndoing) return;

            // Odczytaj dane pręta. Pręt BEZ etykiety (np. kopia bez etykiety) też ma kasować swoje
            // rozkłady — wcześniej pusty LabelHandle kończył obsługę i rozkłady zostawały.
            var bar = SingleBarEngine.ReadBarXData(pline);
            if (bar == null) return;

            try
            {
                using var tr = db.TransactionManager.StartTransaction();

                // Obsługuj zarówno hex jak i decimal format handle
                ObjectId lblId = ObjectId.Null;
                if (!string.IsNullOrEmpty(bar.LabelHandle) && long.TryParse(bar.LabelHandle,
                        System.Globalization.NumberStyles.HexNumber,
                        null, out long hValHex))
                {
                    var h = new Handle(hValHex);
                    if (db.TryGetObjectId(h, out ObjectId id) && !id.IsNull && !id.IsErased)
                        lblId = id;
                }
                if (lblId.IsNull && !string.IsNullOrEmpty(bar.LabelHandle) && long.TryParse(bar.LabelHandle,
                        System.Globalization.NumberStyles.Integer,
                        null, out long hValDec))
                {
                    var h = new Handle(hValDec);
                    if (db.TryGetObjectId(h, out ObjectId id) && !id.IsNull && !id.IsErased)
                        lblId = id;
                }

                // Etap 3: erase MLeader warunkowo — mógł być już skasowany przez BricsCAD
                // w multi-select scenario (MLeader erased first, przed RC_SINGLE_BAR overrule).
                // Cascade do distributions wykonuje się zawsze.
                if (!lblId.IsNull)
                {
                    // Kasuj etykietę tylko jeśli naprawdę należy do TEGO pręta (back-link w XData
                    // MLeadera). Kopia pręta skopiowana bez etykiety wskazuje na etykietę oryginału —
                    // wcześniej jej usunięcie kasowało etykietę oryginału.
                    var ml = tr.GetObject(lblId, OpenMode.ForRead) as MLeader;
                    if (ml != null && XLink.Same(SingleBarEngine.ReadBarHandleFromLabel(ml),
                                                 pline.Handle.Value.ToString("X8")))
                    {
                        ml.UpgradeOpen();
                        ml.Erase(true);
                    }
                }

                // Usuń też wszystkie rozkłady (RC_BAR_BLOCK) powiązane z tym prętem
                string plineHandle = pline.Handle.Value.ToString("X8");
                var modelSpace = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

                foreach (ObjectId oid in modelSpace)
                {
                    if (oid.IsErased) continue;
                    var ent = tr.GetObject(oid, OpenMode.ForRead) as BlockReference;
                    if (ent == null) continue;
                    var barBlock = BarBlockEngine.ReadXData(ent);
                    if (barBlock == null) continue;
                    // Sprawdź czy SourceBarHandle wskazuje na ten pręt
                    if (XLink.Same(barBlock.SourceBarHandle, plineHandle))
                    {
                        // Usuń też powiązaną annotację (RC_BAR_ANNOT)
                        if (!string.IsNullOrEmpty(barBlock.AnnotHandle)
                            && long.TryParse(barBlock.AnnotHandle,
                                System.Globalization.NumberStyles.HexNumber,
                                null, out long aHVal))
                        {
                            var aHandle = new Handle(aHVal);
                            if (db.TryGetObjectId(aHandle, out ObjectId aId)
                                && !aId.IsNull && !aId.IsErased)
                            {
                                var annotBr = tr.GetObject(aId, OpenMode.ForWrite) as BlockReference;
                                annotBr?.Erase(true);
                            }
                        }
                        ent.UpgradeOpen();
                        ent.Erase(true);
                    }
                }

                tr.Commit();
            }
            catch (System.Exception ex) { Log.Error("BarPolylineEraseOverrule.Erase", ex); }
        }

        public new void SetCustomFilter()
        {
            this.SetXDataFilter("RC_SINGLE_BAR");
        }
    }

    // ----------------------------------------------------------------
    // AnnotBlockEraseOverrule — usunięcie RC_BAR_ANNOT triggeruje update MLeader
    // ----------------------------------------------------------------
    internal class AnnotBlockEraseOverrule : ObjectOverrule
    {
        public override void Erase(DBObject dbObject, bool erasing)
        {
            base.Erase(dbObject, erasing);

            if (!erasing) return;
            if (!(dbObject is BlockReference br)) return;

            var db = br.Database;
            if (db == null) return;

            var annotXd = AnnotationEngine.ReadAnnotXData(br);
            if (annotXd == null || string.IsNullOrEmpty(annotXd.SourceBlockHandle)) return;

            try
            {
                if (!long.TryParse(annotXd.SourceBlockHandle,
                        NumberStyles.HexNumber, null, out long sbhv)) return;

                if (!db.TryGetObjectId(new Handle(sbhv), out ObjectId sbId)
                    || sbId.IsNull || sbId.IsErased) return;

                using var tr = db.TransactionManager.StartTransaction();
                var sourceBlock = tr.GetObject(sbId, OpenMode.ForRead) as BlockReference;
                if (sourceBlock != null)
                {
                    var barXd = BarBlockEngine.ReadXData(sourceBlock);
                    if (barXd != null && !string.IsNullOrEmpty(barXd.SourceBarHandle))
                        PendingLabelUpdates.Add(db, barXd.SourceBarHandle);
                }
                tr.Commit();
            }
            catch { }
        }

        public new void SetCustomFilter()
        {
            this.SetXDataFilter("RC_BAR_ANNOT");
        }
    }

    // ----------------------------------------------------------------
    // PendingLabelUpdates — opóźniona aktualizacja etykiet po ERASE
    // ----------------------------------------------------------------
    internal static class PendingLabelUpdates
    {
        // Kolejka per rysunek — handle jest unikalny tylko w obrębie jednej bazy.
        static readonly Dictionary<Database, HashSet<string>> _pending = new();

        public static void Add(Database db, string sourceBarHandle)
        {
            if (db == null || string.IsNullOrEmpty(sourceBarHandle)) return;
            if (!_pending.TryGetValue(db, out var set))
                _pending[db] = set = new HashSet<string>();
            set.Add(sourceBarHandle);
        }

        public static void FlushAll(Database db)
        {
            if (db == null || !_pending.TryGetValue(db, out var set)) return;
            _pending.Remove(db);
            foreach (var h in set)
            {
                try { AnnotationEngine.UpdateBarLabelCount(db, h); }
                catch (System.Exception ex) { Log.Error($"PendingLabelUpdates.Flush {h}", ex); }
            }
        }

        public static void Discard(Database db)
        {
            if (db != null) _pending.Remove(db);
        }
    }

    // ----------------------------------------------------------------
    // Menedzer rejestracji
    // ----------------------------------------------------------------
    public static class AnnotMoveOverrule
    {
        private static AnnotGripOverrule            _grip;
        private static AnnotTransformOverrule       _transform;
        private static BarBlockTransformOverrule    _barBlockTransform;
        private static BarBlockEraseOverrule        _barBlockErase;
        private static BarPolylineEraseOverrule     _barPolylineErase;
        private static AnnotBlockEraseOverrule      _annotBlockErase;

        public static void Register()
        {
            if (_grip != null) return;
            _grip               = new AnnotGripOverrule();
            _transform          = new AnnotTransformOverrule();
            _barBlockTransform  = new BarBlockTransformOverrule();
            _barBlockErase      = new BarBlockEraseOverrule();
            var cls = RXObject.GetClass(typeof(BlockReference));
            Overrule.AddOverrule(cls, _grip,               false);
            Overrule.AddOverrule(cls, _transform,           false);
            Overrule.AddOverrule(cls, _barBlockTransform,   false);
            Overrule.AddOverrule(cls, _barBlockErase,       false);
            _barBlockTransform.SetCustomFilter();
            _barBlockErase.SetCustomFilter();
            _annotBlockErase = new AnnotBlockEraseOverrule();
            Overrule.AddOverrule(cls, _annotBlockErase, false);
            _annotBlockErase.SetCustomFilter();
            _barPolylineErase = new BarPolylineEraseOverrule();
            Overrule.AddOverrule(
                RXObject.GetClass(typeof(Teigha.DatabaseServices.Polyline)),
                _barPolylineErase,
                false);
            _barPolylineErase.SetCustomFilter();
            Overrule.Overruling = true;
        }

        public static void Unregister()
        {
            if (_grip == null) return;
            var cls = RXObject.GetClass(typeof(BlockReference));
            Overrule.RemoveOverrule(cls, _barBlockErase);
            Overrule.RemoveOverrule(cls, _annotBlockErase);
            Overrule.RemoveOverrule(cls, _barBlockTransform);
            Overrule.RemoveOverrule(cls, _transform);
            Overrule.RemoveOverrule(cls, _grip);
            Overrule.RemoveOverrule(
                RXObject.GetClass(typeof(Teigha.DatabaseServices.Polyline)),
                _barPolylineErase);
            _barBlockErase.Dispose();     _barBlockErase     = null;
            _annotBlockErase.Dispose();   _annotBlockErase   = null;
            _barBlockTransform.Dispose(); _barBlockTransform = null;
            _transform.Dispose();         _transform         = null;
            _grip.Dispose();              _grip              = null;
            _barPolylineErase.Dispose();  _barPolylineErase  = null;
        }
    }
}
