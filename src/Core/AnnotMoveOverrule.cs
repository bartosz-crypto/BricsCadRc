using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    // ----------------------------------------------------------------
    // GripOverrule — ogranicza przeciaganie uchwytow do osi zbrojenia
    // ----------------------------------------------------------------
    public class AnnotGripOverrule : GripOverrule
    {
        public override bool IsApplicable(RXObject overruledSubject)
        {
            try { return overruledSubject is BlockReference br && AnnotationEngine.IsAnnotation(br); }
            catch { return false; }
        }

        public override void MoveGripPointsAt(
            Entity entity,
            GripDataCollection grips,
            Vector3d offset,
            MoveGripPointsFlags bitFlags)
        {
            var constrained = AnnotMoveOverrule.ConstrainOffset(entity as BlockReference, offset);
            // Apply constrained displacement directly
            entity.TransformBy(Matrix3d.Displacement(constrained));
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

            // Rebuild matrix: preserve scale/rotation component, constrain translation
            var elems = new double[16];
            for (int row = 0; row < 4; row++)
                for (int col = 0; col < 4; col++)
                    elems[row * 4 + col] = transform[row, col];
            elems[3]  = tx;   // [0,3] X translation
            elems[7]  = ty;   // [1,3] Y translation
            elems[11] = 0.0;  // [2,3] Z translation

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
            var bar = AnnotMoveOverrule.TryReadXData(br);
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
