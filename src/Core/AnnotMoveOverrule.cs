using System;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.Runtime;

namespace BricsCadRc.Core
{
    // ----------------------------------------------------------------
    // GripOverrule — 3 gripy dla RC_BAR_BLOCK, 2 dla starego RC_BAR_ANNOT
    //
    //  RC_BAR_BLOCK (nowy):
    //    [0] insertion point      → ruch boczny wzdluz osi pretow
    //    [1] koniec dist line     → rozciaga span (recalc bar count)
    //    [2] koniec ramienia      → wydluzenie ramienia
    //
    //  RC_BAR_ANNOT (stary, backward compat):
    //    [0] insertion point      → ruch boczny
    //    [1] koniec ramienia      → wydluzenie ramienia
    //
    // UWAGA: BRX GetGripPoints ma tylko Point3dCollection API.
    //        Gripy identyfikowane po pozycji w MoveGripPointsAt.
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

            // --- Nowy blok RC_BAR_BLOCK ---
            var bar = BarBlockEngine.ReadXData(br);
            if (bar != null && bar.BarsSpan > 0 && bar.ArmTotalLen > 0)
            {
                gripPoints.Add(BarBlockEngine.GripLateral(br));  // [0]
                gripPoints.Add(BarBlockEngine.GripSpan(br, bar));  // [1]
                gripPoints.Add(BarBlockEngine.GripArm(br, bar));   // [2]
                return;
            }

            // --- Stary blok RC_BAR_ANNOT ---
            var barOld = AnnotMoveOverrule.TryReadOldXData(br);
            if (barOld != null && barOld.BarsSpan > 0 && barOld.ArmTotalLen > 0)
            {
                gripPoints.Add(br.Position);  // [0] lateral
                gripPoints.Add(new Point3d(   // [1] arm top
                    br.Position.X,
                    br.Position.Y + barOld.BarsSpan + barOld.ArmTotalLen,
                    0));
                return;
            }

            base.GetGripPoints(entity, gripPoints, snapModes, geometryIds);
        }

        public override void MoveGripPointsAt(
            Entity entity,
            GripDataCollection grips,
            Vector3d offset,
            MoveGripPointsFlags bitFlags)
        {
            var br = entity as BlockReference;
            if (br == null) { base.MoveGripPointsAt(entity, grips, offset, bitFlags); return; }

            foreach (GripData gd in grips)
            {
                // --- Nowy blok RC_BAR_BLOCK ---
                var bar = BarBlockEngine.ReadXData(br);
                if (bar != null && bar.BarsSpan > 0)
                {
                    HandleNewBlock(entity, br, bar, gd, offset);
                    continue;
                }

                // --- Stary blok RC_BAR_ANNOT ---
                var barOld = AnnotMoveOverrule.TryReadOldXData(br);
                if (barOld != null)
                {
                    HandleOldBlock(entity, br, barOld, gd, offset);
                    continue;
                }
            }
        }

        // ----------------------------------------------------------------
        // Obsluga nowego RC_BAR_BLOCK (3 gripy)
        // ----------------------------------------------------------------
        private static void HandleNewBlock(
            Entity entity, BlockReference br, BarData bar, GripData gd, Vector3d offset)
        {
            var gripLateral = BarBlockEngine.GripLateral(br);
            var gripSpan    = BarBlockEngine.GripSpan(br, bar);
            var gripArm     = BarBlockEngine.GripArm(br, bar);

            bool isSpan = IsNear(gd.GripPoint, gripSpan);
            bool isArm  = IsNear(gd.GripPoint, gripArm);

            if (isSpan)
            {
                // Rozciaganie span → recalculate bar count
                double delta = bar.Direction == "X" ? offset.Y : offset.X;
                double newBarsSpan = Math.Max(bar.Spacing, bar.BarsSpan + delta);
                BarBlockEngine.RegenerateBarBlock(br, newBarsSpan);
            }
            else if (isArm)
            {
                // Wydluzanie ramienia
                double delta = bar.Direction == "X" ? offset.Y : offset.Y;
                double newArmTotalLen = Math.Max(200.0, bar.ArmTotalLen + delta);
                BarBlockEngine.UpdateArm(br, newArmTotalLen);
            }
            else
            {
                // Ruch boczny wzdluz osi pretow (grip lateral lub nieznany)
                var constrained = ConstrainOffset(bar, offset);
                entity.TransformBy(Matrix3d.Displacement(constrained));
            }
        }

        // ----------------------------------------------------------------
        // Obsluga starego RC_BAR_ANNOT (2 gripy)
        // ----------------------------------------------------------------
        private static void HandleOldBlock(
            Entity entity, BlockReference br, BarData bar, GripData gd, Vector3d offset)
        {
            bool isArm = gd.GripPoint.Y > br.Position.Y + 1.0;
            if (isArm)
            {
                double delta = offset.Y;
                double newArmTotalLen = Math.Max(200.0, bar.ArmTotalLen + delta);
                AnnotationEngine.UpdateArmInBlock(br, newArmTotalLen);
            }
            else
            {
                var constrained = ConstrainOffset(bar, offset);
                entity.TransformBy(Matrix3d.Displacement(constrained));
            }
        }

        private static bool IsNear(Point3d a, Point3d b) => (a - b).LengthSqrd < 4.0; // 2mm tolerancja

        private static Vector3d ConstrainOffset(BarData bar, Vector3d offset)
            => bar.Direction == "X"
                ? new Vector3d(offset.X, 0, 0)
                : new Vector3d(0, offset.Y, 0);
    }

    // ----------------------------------------------------------------
    // TransformOverrule — ogranicza MOVE/STRETCH do osi zbrojenia
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

            string direction = null;
            var barNew = BarBlockEngine.ReadXData(br);
            if (barNew != null) direction = barNew.Direction;
            else
            {
                var barOld = AnnotMoveOverrule.TryReadOldXData(br);
                if (barOld != null) direction = barOld.Direction;
            }

            if (direction == null) { base.TransformBy(entity, transform); return; }

            var t  = transform.Translation;
            double tx = direction == "X" ? t.X : 0.0;
            double ty = direction == "Y" ? t.Y : 0.0;

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
        // Helpers
        // ----------------------------------------------------------------

        /// <summary>Czyta XData starego formatu RC_BAR_ANNOT (backward compat).</summary>
        internal static BarData TryReadOldXData(BlockReference br)
        {
            try { return AnnotationEngine.ReadAnnotXData(br); }
            catch { return null; }
        }
    }
}
