using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    // ----------------------------------------------------------------
    // GripData dla annotacji — dwa rodzaje gripow:
    //   IsArmEnd = false  → przesuwa CALY blok wzdluz osi pretow
    //   IsArmEnd = true   → rozciaga TYLKO ramie (bez ruszania dotow)
    // ----------------------------------------------------------------
    internal class AnnotGripData : GripData
    {
        public bool IsArmEnd { get; set; }
    }

    // ----------------------------------------------------------------
    // GripOverrule — dostarcza 2 gripy + obsluguje ich przeciaganie
    //
    // GetGripPoints uzywa starszego API Point3dCollection (BRX nie ma
    // przeciazenia z GripDataCollection).
    // Grip identyfikowany w MoveGripPointsAt po pozycji Y.
    // ----------------------------------------------------------------
    public class AnnotGripOverrule : GripOverrule
    {
        public override bool IsApplicable(RXObject overruledSubject)
        {
            try { return overruledSubject is BlockReference br && AnnotationEngine.IsAnnotation(br); }
            catch { return false; }
        }

        // Zwroc 2 gripy: [0] lateral (insertion point), [1] arm top
        public override void GetGripPoints(
            Entity entity,
            Point3dCollection gripPoints,
            IntegerCollection snapModes,
            IntegerCollection geometryIds)
        {
            var br = entity as BlockReference;
            if (br == null) { base.GetGripPoints(entity, gripPoints, snapModes, geometryIds); return; }

            var bar = AnnotMoveOverrule.TryReadXData(br);
            if (bar == null || bar.BarsSpan <= 0 || bar.ArmTotalLen <= 0)
            {
                base.GetGripPoints(entity, gripPoints, snapModes, geometryIds);
                return;
            }

            // Grip 0: ruch boczny wzdluz osi pretow
            gripPoints.Add(br.Position);

            // Grip 1: koniec ramienia (do rozciagania arm)
            Point3d armTop = bar.Direction == "X"
                ? new Point3d(br.Position.X, br.Position.Y + bar.BarsSpan + bar.ArmTotalLen, 0)
                : new Point3d(br.Position.X, br.Position.Y + bar.ArmTotalLen, 0);
            gripPoints.Add(armTop);
        }

        public override void MoveGripPointsAt(
            Entity entity,
            GripDataCollection grips,
            Vector3d offset,
            MoveGripPointsFlags bitFlags)
        {
            var br = entity as BlockReference;
            if (br == null) { base.MoveGripPointsAt(entity, grips, offset, bitFlags); return; }

            var bar = AnnotMoveOverrule.TryReadXData(br);
            if (bar == null) { base.MoveGripPointsAt(entity, grips, offset, bitFlags); return; }

            foreach (GripData gd in grips)
            {
                // Grip arm-end: jego Y jest znacznie powyzej punktu wstawienia
                bool isArmEnd = gd.GripPoint.Y > br.Position.Y + 1.0;

                if (isArmEnd)
                {
                    double delta          = bar.Direction == "X" ? offset.Y : offset.X;
                    double newArmTotalLen = Math.Max(200.0, bar.ArmTotalLen + delta);
                    AnnotationEngine.UpdateArmInBlock(br, newArmTotalLen);
                }
                else
                {
                    var constrained = AnnotMoveOverrule.ConstrainOffset(br, offset);
                    entity.TransformBy(Matrix3d.Displacement(constrained));
                }
            }
        }
    }

    // ----------------------------------------------------------------
    // TransformOverrule — ogranicza komende MOVE/STRETCH do osi zbrojenia
    // ----------------------------------------------------------------
    public class AnnotTransformOverrule : TransformOverrule
    {
        public override bool IsApplicable(RXObject overruledSubject)
        {
            try { return overruledSubject is BlockReference br && AnnotationEngine.IsAnnotation(br); }
            catch { return false; }
        }

        public override void TransformBy(Entity entity, Matrix3d transform)
        {
            var br = entity as BlockReference;
            if (br == null) { base.TransformBy(entity, transform); return; }

            var bar = AnnotMoveOverrule.TryReadXData(br);
            if (bar == null) { base.TransformBy(entity, transform); return; }

            var t = transform.Translation;
            double tx = bar.Direction == "X" ? t.X : 0.0;
            double ty = bar.Direction == "Y" ? t.Y : 0.0;

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
    // Menedzer rejestracji obu overruli
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

            var targetClass = RXObject.GetClass(typeof(BlockReference));
            Overrule.AddOverrule(targetClass, _grip,      false);
            Overrule.AddOverrule(targetClass, _transform, false);
            Overrule.Overruling = true;
        }

        public static void Unregister()
        {
            if (_grip == null) return;

            var targetClass = RXObject.GetClass(typeof(BlockReference));
            Overrule.RemoveOverrule(targetClass, _transform);
            Overrule.RemoveOverrule(targetClass, _grip);

            _transform.Dispose();
            _grip.Dispose();
            _transform = null;
            _grip      = null;
        }

        // ----------------------------------------------------------------
        // Helpers wspoldzielone
        // ----------------------------------------------------------------

        internal static Vector3d ConstrainOffset(BlockReference br, Vector3d offset)
        {
            if (br == null) return offset;
            var bar = TryReadXData(br);
            if (bar == null) return offset;
            return bar.Direction == "X"
                ? new Vector3d(offset.X, 0, 0)
                : new Vector3d(0, offset.Y, 0);
        }

        internal static BarData TryReadXData(BlockReference br)
        {
            try { return AnnotationEngine.ReadAnnotXData(br); }
            catch { return null; }
        }
    }
}
