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
        private static readonly Dictionary<long, double>  _dragOrigArm
            = new Dictionary<long, double>();
        private static readonly Dictionary<long, Point3d> _dragOrigPos
            = new Dictionary<long, Point3d>();

        // Transienty podglądu dragu arm — czyszczone przed każdym nowym wywołaniem MoveGripPointsAt
        // i przy GetGripPoints (nowa interakcja).
        private static readonly List<Entity> _gripTransients = new List<Entity>();

        private static void ClearGripTransients()
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
            if (barBlock != null && barBlock.BarsSpan > 0)
            {
                // Nowa interakcja — wyczysc stan dragu pozycji
                _dragOrigPos.Remove(br.ObjectId.Handle.Value);

                gripPoints.Add(BarBlockEngine.GripLateral(br, barBlock)); // [0] krawedz plyty (cover offset)
                gripPoints.Add(BarBlockEngine.GripSpan(br, barBlock)); // [1] span resize
                return;
            }

            // --- Modul annotacji RC_BAR_ANNOT ---
            var barAnnot = AnnotationEngine.ReadAnnotXData(br);
            if (barAnnot != null && barAnnot.BarsSpan > 0 && barAnnot.ArmTotalLen > 0)
            {
                // Wyczysc stan dragu — poczatek nowej interakcji z gripem
                long hv = br.ObjectId.Handle.Value;
                _dragOrigArm.Remove(hv);
                _dragOrigPos.Remove(hv);
                ClearGripTransients();

                var edDbg = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor;
                edDbg?.WriteMessage($"\n[DEBUG XDATA] in GetGrips: ArmTotalLen={barAnnot.ArmTotalLen:F1} ArmMidY={barAnnot.ArmMidY:F1}");

                var ins = br.Position;
                // Grip[0]: środek dist line (nie insertPt)
                Point3d grip0 = (barAnnot.Direction == "X")
                    ? new Point3d(ins.X, ins.Y + barAnnot.BarsSpan / 2.0, 0)
                    : new Point3d(ins.X + barAnnot.BarsSpan / 2.0, ins.Y, 0);
                gripPoints.Add(grip0);  // [0] lateral

                // X: arm pionowe — grip na gorze (ins.X, ins.Y + barsSpan + armTotalLen)
                // Y: arm poziome — grip na prawo (ins.X + barsSpan + armTotalLen, ins.Y)
                Point3d armTop;
                // Etykieta z złamaniem: grip[1] przy końcu tekstu (TextEndLocal)
                if (barAnnot.TextEndLocalX != 0.0 || barAnnot.TextEndLocalY != 0.0)
                {
                    armTop = new Point3d(ins.X + barAnnot.TextEndLocalX, ins.Y + barAnnot.TextEndLocalY, 0);
                }
                else if (barAnnot.Direction == "X" && !barAnnot.LeaderHorizontal)
                    armTop = barAnnot.LeaderUp
                        ? new Point3d(ins.X, ins.Y + barAnnot.BarsSpan + barAnnot.ArmTotalLen, 0)
                        : new Point3d(ins.X, ins.Y + barAnnot.BarsSpan / 2.0 - barAnnot.ArmTotalLen, 0);
                else if (barAnnot.Direction == "X" && barAnnot.LeaderHorizontal)
                {
                    double currentMidY = AnnotationEngine.GetArmMidY(br);
                    double hDir2 = barAnnot.LeaderRight ? 1.0 : -1.0;
                    armTop = new Point3d(ins.X + hDir2 * barAnnot.ArmTotalLen, ins.Y + currentMidY, 0);
                }
                else
                    armTop = new Point3d(ins.X + barAnnot.ArmTotalLen, ins.Y + barAnnot.BarsSpan / 2.0, 0);
                gripPoints.Add(armTop);  // [1] arm end

                var edGrip = Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor;
                edGrip?.WriteMessage($"\n[DEBUG GRIPS] grip[0]=({grip0.X:F1},{grip0.Y:F1}) grip[1]=({armTop.X:F1},{armTop.Y:F1})" +
                                     $" leaderH={barAnnot.LeaderHorizontal} leaderRight={barAnnot.LeaderRight}" +
                                     $" ins=({ins.X:F1},{ins.Y:F1}) armTotalLen={barAnnot.ArmTotalLen:F1} midY={AnnotationEngine.GetArmMidY(br):F1}");
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
            foreach (GripData gd in grips)
            {
                var barBlock = BarBlockEngine.ReadXData(br);
                if (barBlock != null && barBlock.BarsSpan > 0)
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
            var idxList = new System.Text.StringBuilder();
            foreach (int idx in indices)
            {
                idxList.Append(idx).Append(',');
                if (idx == 1) isGrip1 = true;
            }
            Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor
                .WriteMessage($"\n[DEBUG GRIPS] MoveGripPointsAt indices=[{idxList}] isGrip1={isGrip1} offset=({offset.X:F1},{offset.Y:F1})");

            ApplyGripMove(entity, br, offset, isGrip1);
        }

        // ----------------------------------------------------------------
        // ApplyGripMove — wspolna logika dla obu wersji MoveGripPointsAt
        // ----------------------------------------------------------------
        private static void ApplyGripMove(Entity entity, BlockReference br, Vector3d offset, bool isGrip1)
        {
            // --- RC_BAR_BLOCK ---
            var barBlock = BarBlockEngine.ReadXData(br);
            if (barBlock != null && barBlock.BarsSpan > 0)
            {
                if (isGrip1)
                {
                    double delta       = barBlock.Direction == "X" ? offset.Y : offset.X;
                    double newBarsSpan = Math.Max(barBlock.Spacing, barBlock.BarsSpan + delta);
                    BarBlockEngine.RegenerateBarBlock(br, newBarsSpan);
                    // Synchronizuj annotacje — nowa liczba pretow i nowy barsSpan
                    var updatedBar = BarBlockEngine.ReadXData(br);
                    if (updatedBar != null)
                        AnnotationEngine.SyncAnnotation(br.Database, updatedBar);
                }
                else
                {
                    // Grip [0]: swobodny ruch w dowolnym kierunku.
                    // offset jest KUMULATYWNY — uzywamy origPos jako bazy (jak _dragOrigArm).
                    long handle = br.ObjectId.Handle.Value;
                    if (!_dragOrigPos.ContainsKey(handle))
                        _dragOrigPos[handle] = br.Position;

                    var origPos = _dragOrigPos[handle];
                    var target  = new Point3d(origPos.X + offset.X, origPos.Y + offset.Y, origPos.Z);
                    // delta = ile jeszcze trzeba przesunac od aktualnej pozycji do celu
                    entity.TransformBy(Matrix3d.Displacement(target - br.Position));
                }
                return;
            }

            // --- RC_BAR_ANNOT ---
            var barAnnot = AnnotationEngine.ReadAnnotXData(br);
            if (barAnnot != null)
            {
                if (isGrip1)
                {
                    // ArmTotalLen/ArmMidY/ArmOffsetX czytamy świeżo z XData na każde wywołanie.
                    double hDir       = barAnnot.LeaderRight ? 1.0 : -1.0;
                    double origArmLen = barAnnot.ArmTotalLen;

                    // --- Transient preview (live podczas dragu) ---
                    ClearGripTransients();
                    var ins = br.Position;

                    if (barAnnot.LeaderHorizontal)
                    {
                        double newArmTotalLen = Math.Max(AnnotationEngine.ArmLength, origArmLen + offset.X * hDir);
                        double origMidY = !double.IsNaN(barAnnot.ArmMidY)
                            ? barAnnot.ArmMidY
                            : AnnotationEngine.GetArmMidY(br);
                        double newMidY = origMidY + offset.Y;

                        // stem pionowy: środek dist line → kink
                        AddGripTransientLine(
                            new Point3d(ins.X,                         ins.Y + barAnnot.BarsSpan / 2.0, 0),
                            new Point3d(ins.X,                         ins.Y + newMidY,                 0));
                        // arm poziomy: kink → koniec arm
                        AddGripTransientLine(
                            new Point3d(ins.X,                         ins.Y + newMidY, 0),
                            new Point3d(ins.X + hDir * newArmTotalLen, ins.Y + newMidY, 0));

                        Bricscad.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor
                            .WriteMessage($"\n[DEBUG HORIZ GRIP] gripDelta.X={offset.X:F1} gripDelta.Y={offset.Y:F1} newMidY={newMidY:F1} newArmEndX={hDir * newArmTotalLen:F1}");
                        AnnotationEngine.UpdateArmInBlock(br, newArmTotalLen, newMidY);
                    }
                    else if (barAnnot.Direction == "X")
                    {
                        // X-vert: Y → rozciąga arm (kierunek zależy od LeaderUp); X → przesuwa cały blok
                        double dirY = barAnnot.LeaderUp ? 1.0 : -1.0;
                        double newArmTotalLen = Math.Max(AnnotationEngine.ArmLength, origArmLen + offset.Y * dirY);
                        double armEndY = barAnnot.LeaderUp
                            ? ins.Y + barAnnot.BarsSpan + newArmTotalLen
                            : ins.Y + barAnnot.BarsSpan / 2.0 - newArmTotalLen;
                        AddGripTransientLine(
                            new Point3d(ins.X, ins.Y + barAnnot.BarsSpan / 2.0, 0),
                            new Point3d(ins.X, armEndY,                          0));
                        AnnotationEngine.UpdateArmInBlock(br, newArmTotalLen);
                        // X przesuwa cały blok — identycznie jak grip[0]
                        if (Math.Abs(offset.X) > 0.01)
                            entity.TransformBy(Matrix3d.Displacement(new Vector3d(offset.X, 0, 0)));
                    }
                    else
                    {
                        // Y-bars: arm poziomy
                        double newArmTotalLen = Math.Max(AnnotationEngine.ArmLength, origArmLen + offset.X * hDir);
                        AddGripTransientLine(
                            new Point3d(ins.X + barAnnot.BarsSpan,                  ins.Y, 0),
                            new Point3d(ins.X + barAnnot.BarsSpan + newArmTotalLen, ins.Y, 0));
                        AnnotationEngine.UpdateArmInBlock(br, newArmTotalLen);
                    }

                    try { Application.UpdateScreen(); } catch { }
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
                    // X-bars: X wolny (wzdłuż pręta), Y zablokowany
                    // Y-bars: Y wolny, X zablokowany
                    double targetX = (barAnnot.Direction == "X")
                        ? origPos.X + offset.X
                        : origPos.X;
                    double targetY = (barAnnot.Direction == "X")
                        ? origPos.Y
                        : origPos.Y + offset.Y;
                    var target = new Point3d(targetX, targetY, origPos.Z);
                    br.UpgradeOpen();
                    br.Position = new Point3d(targetX, targetY, origPos.Z);
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

    // ----------------------------------------------------------------
    // Flaga bypass — ustawiana przez BarBlockTransformOverrule przed
    // programowym przesunięciem RC_BAR_ANNOT, zdejmowana w finally.
    // Dzięki temu AnnotTransformOverrule nie constraintuje tego ruchu.
    // BricsCAD UI jest jednowątkowe — statyczna flaga jest bezpieczna.
    // ----------------------------------------------------------------
    internal static class AnnotOverruleState
    {
        public static bool BypassConstraint { get; set; } = false;
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
            // Jeśli przesunięcie pochodzi z BarBlockTransformOverrule — nie ograniczaj osi
            if (AnnotOverruleState.BypassConstraint)
            {
                base.TransformBy(entity, transform);
                return;
            }

            var br = entity as BlockReference;
            if (br == null) { base.TransformBy(entity, transform); return; }

            // Blok pretow: swobodny ruch — brak ograniczenia kierunku
            if (BarBlockEngine.IsBarBlock(br)) { base.TransformBy(entity, transform); return; }

            string dir = null;
            var bb = BarBlockEngine.ReadXData(br);
            if (bb != null) dir = bb.Direction;
            else { var ba = AnnotationEngine.ReadAnnotXData(br); if (ba != null) dir = ba.Direction; }

            if (dir == null) { base.TransformBy(entity, transform); return; }

            var t = transform.Translation;

            // leaderHorizontal (arm boczny dla X-bars): blokujemy X, przepuszczamy Y
            bool isLeaderHorizontal = false;
            var barAnnotLH = AnnotationEngine.ReadAnnotXData(br);
            if (barAnnotLH != null) isLeaderHorizontal = barAnnotLH.LeaderHorizontal;

            var (tx, ty) = AnnotLeaderGeometry.ConstrainTranslation(dir, isLeaderHorizontal, t.X, t.Y);

            var elems = new double[16];
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++)
                    elems[row * 4 + col] = transform[row, col];
            elems[3]  = tx;
            elems[7]  = ty;
            elems[11] = 0.0;

            base.TransformBy(entity, new Matrix3d(elems));
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

            if (!(entity is BlockReference br)) return;

            var db  = br.Database;
            var doc = Application.DocumentManager.MdiActiveDocument;
            if (db == null || doc == null) return;

            string annotHandle = ReadAnnotHandle(br);
            if (string.IsNullOrEmpty(annotHandle)) return;

            try
            {
                using var tr = db.TransactionManager.StartTransaction();

                if (!long.TryParse(annotHandle, NumberStyles.HexNumber, null, out long hVal))
                { tr.Commit(); return; }

                var handle = new Handle(hVal);
                if (!db.TryGetObjectId(handle, out ObjectId annotId)
                    || annotId.IsNull || !annotId.IsValid)
                { tr.Commit(); return; }

                var annotBr = tr.GetObject(annotId, OpenMode.ForWrite) as BlockReference;
                if (annotBr == null) { tr.Commit(); return; }

                // Ustaw flagę — AnnotTransformOverrule nie będzie constraintował tego ruchu
                AnnotOverruleState.BypassConstraint = true;
                try
                {
                    annotBr.TransformBy(transform);
                }
                finally
                {
                    AnnotOverruleState.BypassConstraint = false;
                }

                tr.Commit();
            }
            catch
            {
                AnnotOverruleState.BypassConstraint = false;
            }
        }

        public new void SetCustomFilter()
        {
            this.SetXDataFilter("RC_BAR_BLOCK");
        }

        private static string ReadAnnotHandle(BlockReference br)
        {
            var rxd = br.GetXDataForApplication("RC_BAR_BLOCK");
            if (rxd == null) return null;

            foreach (TypedValue tv in rxd)
            {
                if (tv.TypeCode != (int)DxfCode.ExtendedDataAsciiString) continue;
                var s = tv.Value?.ToString() ?? "";
                if (s.Length == 8 && s.All(c =>
                    (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f')))
                    return s;
            }
            return null;
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

        public static void Register()
        {
            if (_grip != null) return;
            _grip               = new AnnotGripOverrule();
            _transform          = new AnnotTransformOverrule();
            _barBlockTransform  = new BarBlockTransformOverrule();
            var cls = RXObject.GetClass(typeof(BlockReference));
            Overrule.AddOverrule(cls, _grip,               false);
            Overrule.AddOverrule(cls, _transform,           false);
            Overrule.AddOverrule(cls, _barBlockTransform,   false);
            _barBlockTransform.SetCustomFilter();
            Overrule.Overruling = true;
        }

        public static void Unregister()
        {
            if (_grip == null) return;
            var cls = RXObject.GetClass(typeof(BlockReference));
            Overrule.RemoveOverrule(cls, _barBlockTransform);
            Overrule.RemoveOverrule(cls, _transform);
            Overrule.RemoveOverrule(cls, _grip);
            _barBlockTransform.Dispose(); _barBlockTransform = null;
            _transform.Dispose();         _transform         = null;
            _grip.Dispose();              _grip              = null;
        }
    }
}
