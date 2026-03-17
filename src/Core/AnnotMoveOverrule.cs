using System;
using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Teigha.DatabaseServices;
using Teigha.Geometry;
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
        private static readonly Dictionary<long, double>  _dragOrigArm
            = new Dictionary<long, double>();
        private static readonly Dictionary<long, Point3d> _dragOrigPos
            = new Dictionary<long, Point3d>();

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

                var ins = br.Position;
                gripPoints.Add(ins);  // [0] lateral

                // X: arm pionowe — grip na gorze (ins.X, ins.Y + barsSpan + armTotalLen)
                // Y: arm poziome — grip na prawo (ins.X + barsSpan + armTotalLen, ins.Y)
                Point3d armTop;
                if (barAnnot.Direction == "X" && !barAnnot.LeaderHorizontal)
                    armTop = new Point3d(ins.X, ins.Y + barAnnot.BarsSpan + barAnnot.ArmTotalLen, 0);
                else if (barAnnot.Direction == "X" && barAnnot.LeaderHorizontal)
                {
                    double currentMidY = AnnotationEngine.GetArmMidY(br);
                    double hDir2 = barAnnot.LeaderRight ? 1.0 : -1.0;
                    armTop = new Point3d(ins.X + hDir2 * barAnnot.ArmTotalLen, ins.Y + currentMidY, 0);
                }
                else
                    armTop = new Point3d(ins.X + barAnnot.ArmTotalLen, ins.Y + barAnnot.BarsSpan / 2.0, 0);
                gripPoints.Add(armTop);  // [1] arm end
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
            foreach (int idx in indices)
                if (idx == 1) { isGrip1 = true; break; }

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
                    // BricsCAD przekazuje KUMULATYWNY offset od startu dragu.
                    // Uzywamy origArm (sprzed dragu) jako bazy — bez akumulacji.
                    long handle = br.ObjectId.Handle.Value;
                    if (!_dragOrigArm.ContainsKey(handle))
                        _dragOrigArm[handle] = barAnnot.ArmTotalLen;

                    // X pionowe: drag w Y; X poziome (leaderHorizontal): drag w X*hDir; Y: drag w X
                    double hDir = barAnnot.LeaderRight ? 1.0 : -1.0;
                    double delta = (barAnnot.Direction == "X" && !barAnnot.LeaderHorizontal)
                        ? offset.Y
                        : offset.X * hDir;
                    double newArmTotalLen = Math.Max(AnnotationEngine.ArmLength, _dragOrigArm[handle] + delta);

                    if (barAnnot.LeaderHorizontal)
                    {
                        // Grip[1] dla leaderHorizontal: X zmienia długość arm, Y przesuwa arm w bloku
                        if (!_dragOrigPos.ContainsKey(handle))
                        {
                            var barForMid = AnnotationEngine.ReadAnnotXData(br);
                            double initMidY = (barForMid != null && !double.IsNaN(barForMid.ArmMidY))
                                ? barForMid.ArmMidY
                                : AnnotationEngine.GetArmMidY(br);
                            _dragOrigPos[handle] = new Point3d(0, initMidY, 0);
                        }
                        double newMidY = _dragOrigPos[handle].Y + offset.Y;
                        AnnotationEngine.UpdateArmInBlock(br, newArmTotalLen, newMidY);
                        return;
                    }
                    // dla !leaderHorizontal: zostaje stara logika UpdateArmInBlock
                    AnnotationEngine.UpdateArmInBlock(br, newArmTotalLen);
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
                    // Ogranicz ruch do osi zbrojenia, licząc od origPos (nie od aktualnej pozycji)
                    double targetX = (barAnnot.Direction == "X" && barAnnot.LeaderHorizontal)
                        ? origPos.X   // leaderHorizontal: X zablokowany (ramię kontroluje dystans)
                        : origPos.X + offset.X;
                    double targetY = (barAnnot.Direction == "X")
                        ? origPos.Y   // X-bars: Y zawsze zablokowany (romby muszą trafiać w pręty)
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
    // Menedzer rejestracji
    // ----------------------------------------------------------------
    public static class AnnotMoveOverrule
    {
        private static AnnotGripOverrule      _grip;
        private static AnnotTransformOverrule _transform;

        public static void Register()
        {
            if (_grip != null) return;
            _grip      = new AnnotGripOverrule();
            _transform = new AnnotTransformOverrule();
            var cls = RXObject.GetClass(typeof(BlockReference));
            Overrule.AddOverrule(cls, _grip,      false);
            Overrule.AddOverrule(cls, _transform, false);
            Overrule.Overruling = true;
        }

        public static void Unregister()
        {
            if (_grip == null) return;
            var cls = RXObject.GetClass(typeof(BlockReference));
            Overrule.RemoveOverrule(cls, _transform);
            Overrule.RemoveOverrule(cls, _grip);
            _transform.Dispose(); _grip.Dispose();
            _transform = null; _grip = null;
        }
    }
}
