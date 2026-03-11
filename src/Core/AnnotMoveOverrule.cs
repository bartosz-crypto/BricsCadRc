using System;
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
                gripPoints.Add(BarBlockEngine.GripLateral(br));     // [0] lateral
                gripPoints.Add(BarBlockEngine.GripSpan(br, barBlock)); // [1] span resize
                return;
            }

            // --- Modul annotacji RC_BAR_ANNOT ---
            var barAnnot = AnnotationEngine.ReadAnnotXData(br);
            if (barAnnot != null && barAnnot.BarsSpan > 0 && barAnnot.ArmTotalLen > 0)
            {
                var ins = br.Position;
                gripPoints.Add(ins);  // [0] lateral

                Point3d armTop = barAnnot.Direction == "X"
                    ? new Point3d(ins.X, ins.Y + barAnnot.BarsSpan + barAnnot.ArmTotalLen, 0)
                    : new Point3d(ins.X, ins.Y + barAnnot.LengthA  + barAnnot.ArmTotalLen, 0);
                gripPoints.Add(armTop);  // [1] arm top
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
                    isGrip1 = IsNear(gd.GripPoint, BarBlockEngine.GripSpan(br, barBlock));
                    break;
                }
                var barAnnot = AnnotationEngine.ReadAnnotXData(br);
                if (barAnnot != null)
                {
                    var ins    = br.Position;
                    Point3d armTop = barAnnot.Direction == "X"
                        ? new Point3d(ins.X, ins.Y + barAnnot.BarsSpan + barAnnot.ArmTotalLen, 0)
                        : new Point3d(ins.X, ins.Y + barAnnot.LengthA  + barAnnot.ArmTotalLen, 0);
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
                }
                else
                {
                    entity.TransformBy(Matrix3d.Displacement(ConstrainOffset(barBlock, offset)));
                }
                return;
            }

            // --- RC_BAR_ANNOT ---
            var barAnnot = AnnotationEngine.ReadAnnotXData(br);
            if (barAnnot != null)
            {
                if (isGrip1)
                {
                    double newArmTotalLen = Math.Max(200.0, barAnnot.ArmTotalLen + offset.Y);
                    AnnotationEngine.UpdateArmInBlock(br, newArmTotalLen);
                }
                else
                {
                    // Ruch TYLKO wzdluz osi pretow (X dla X-bars, Y dla Y-bars)
                    // — Y annotacji jest zawsze zablokowane
                    entity.TransformBy(Matrix3d.Displacement(ConstrainOffset(barAnnot, offset)));
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
    // TransformOverrule — ogranicza MOVE do osi zbrojenia
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

            string dir = null;
            var bb = BarBlockEngine.ReadXData(br);
            if (bb != null) dir = bb.Direction;
            else { var ba = AnnotationEngine.ReadAnnotXData(br); if (ba != null) dir = ba.Direction; }

            if (dir == null) { base.TransformBy(entity, transform); return; }

            var t = transform.Translation;
            double tx = dir == "X" ? t.X : 0.0;
            double ty = dir == "Y" ? t.Y : 0.0;

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
