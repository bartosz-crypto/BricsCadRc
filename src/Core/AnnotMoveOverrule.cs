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

        // Klucz: ObjectId annotacji, wartość: oryginalna pozycja przed dragiem.
        // Czyszczone przy final commit (sukces) lub w OnCommandCancelled (ESC).
        internal static readonly Dictionary<ObjectId, Point3d> PendingAnnotRestore
            = new Dictionary<ObjectId, Point3d>();

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
                _dragOrigPos.Remove(0L);          // wyczyść też cache klonu z poprzedniego drag
                _annotDragStart.Remove(br.ObjectId.Handle.Value);
                _annotDragStart.Remove(0L);

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
                _dragStartSegLen.Remove(hv);
                _dragStartSegLen.Remove(0L);
                ClearGripTransients();


                var ins = br.Position;
                // Grip[0]: środek dist line (nie insertPt)
                Point3d grip0 = (barAnnot.Direction == "X")
                    ? new Point3d(ins.X, ins.Y + barAnnot.BarsSpan / 2.0, 0)
                    : new Point3d(ins.X + barAnnot.BarsSpan / 2.0, ins.Y, 0);
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
                if (idx == 1) isGrip1 = true;

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
                    {
                        AnnotationEngine.SyncAnnotation(br.Database, updatedBar);

                        // Zaktualizuj etykietę pręta-źródłowego (MLeader) — nowa liczba prętów
                        AnnotationEngine.UpdateBarLabelCount(
                            br.Database,
                            updatedBar.SourceBarHandle ?? "",
                            markOverride: updatedBar.Mark);
                    }
                }
                else
                {
                    long handle = br.ObjectId.Handle.Value;
                    if (!_dragOrigPos.ContainsKey(handle))
                        _dragOrigPos[handle] = br.Position;
                    var origPos = _dragOrigPos[handle];
                    var target  = new Point3d(origPos.X + offset.X, origPos.Y + offset.Y, origPos.Z);
                    var disp    = target - br.Position;

                    // Wyłącz auto-sync annotacji w BarBlockTransformOverrule (używamy własnej logiki)
                    AnnotOverruleState.InGripDrag = true;
                    try { entity.TransformBy(Matrix3d.Displacement(disp)); }
                    finally { AnnotOverruleState.InGripDrag = false; }

                    // handle=0 → klon BricsCAD (rubber-band preview) → synchronizuj annotację
                    // handle≠0 → prawdziwy obiekt (final commit) → annotacja już na miejscu, pomijamy
                    bool isPreviewClone = (handle == 0);

                    if (isPreviewClone)
                    {
                        if (!string.IsNullOrEmpty(barBlock.AnnotHandle) &&
                            long.TryParse(barBlock.AnnotHandle,
                                System.Globalization.NumberStyles.HexNumber,
                                null, out long annotHVal))
                        {
                            var annotHandle2 = new Handle(annotHVal);
                            if (br.Database.TryGetObjectId(annotHandle2, out ObjectId annotId2)
                                && !annotId2.IsNull && !annotId2.IsErased)
                            {
                                using var trAnnot = br.Database.TransactionManager.StartTransaction();
                                var annotBr2 = trAnnot.GetObject(annotId2, OpenMode.ForWrite) as BlockReference;
                                if (annotBr2 != null)
                                {
                                    if (!_annotDragStart.ContainsKey(handle))
                                        _annotDragStart[handle] = annotBr2.Position;

                                    // Zapamiętaj oryginalną pozycję do ewentualnego restore przy ESC
                                    if (!PendingAnnotRestore.ContainsKey(annotId2))
                                        PendingAnnotRestore[annotId2] = annotBr2.Position;

                                    var origAnnotPos = _annotDragStart[handle];

                                    AnnotOverruleState.BypassConstraint = true;
                                    try
                                    {
                                        annotBr2.Position = new Point3d(
                                            origAnnotPos.X + offset.X,
                                            origAnnotPos.Y + offset.Y,
                                            origAnnotPos.Z);
                                    }
                                    finally
                                    {
                                        AnnotOverruleState.BypassConstraint = false;
                                    }
                                }
                                trAnnot.Commit();
                            }
                        }
                    }
                    else
                    {
                        // Final commit — wyczyść PendingAnnotRestore, restore nie będzie potrzebny
                        var barFinal = BarBlockEngine.ReadXData(br);
                        if (barFinal != null && !string.IsNullOrEmpty(barFinal.AnnotHandle))
                        {
                            if (long.TryParse(barFinal.AnnotHandle,
                                    System.Globalization.NumberStyles.HexNumber, null, out long hv))
                            {
                                if (br.Database.TryGetObjectId(new Handle(hv), out ObjectId finalAnnotId))
                                    PendingAnnotRestore.Remove(finalAnnotId);
                            }
                        }
                    }
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

                    // Odczytaj świeży snapshot LeaderPoints z XData
                    List<Point3d> pts = null;
                    using (var trT = br.Database.TransactionManager.StartTransaction())
                    {
                        var brT = trT.GetObject(br.ObjectId, OpenMode.ForRead) as BlockReference;
                        var barT = brT != null ? AnnotationEngine.ReadAnnotXData(brT) : null;
                        if (barT != null && !string.IsNullOrEmpty(barT.LeaderPoints))
                            pts = AnnotationEngine.DecodeLeaderPoints(barT.LeaderPoints);
                        trT.Commit();
                    }

                    if (pts == null || pts.Count < 2)
                    {
                        // Fallback: przesuń o offset bez constraina
                        if (pts != null && pts.Count > 0)
                        {
                            var lastWCS0 = pts[pts.Count - 1].TransformBy(br.BlockTransform);
                            AnnotationEngine.UpdateLeaderInBlock(br, lastWCS0 + offset);
                        }
                        return;
                    }

                    // Constrain wzdłuż osi ostatniego segmentu
                    var lastSegDirLocal = (pts[pts.Count - 1] - pts[pts.Count - 2]).GetNormal();
                    var wcsDir = lastSegDirLocal.TransformBy(br.BlockTransform);
                    wcsDir = new Vector3d(wcsDir.X, wcsDir.Y, 0).GetNormal();

                    var prevWCS = pts[pts.Count - 2].TransformBy(br.BlockTransform);
                    var lastWCS = pts[pts.Count - 1].TransformBy(br.BlockTransform);

                    // Zapamiętaj startSegLen przy pierwszym wywołaniu dla tego handle
                    long h = br.ObjectId.Handle.Value;
                    if (!_dragStartSegLen.ContainsKey(h))
                        _dragStartSegLen[h] = (lastWCS - prevWCS).DotProduct(wcsDir);
                    double startSegLen = _dragStartSegLen[h];

                    // newSegLen = startSegLen + kumulatywny offset wzdłuż wcsDir
                    double proj = offset.X * wcsDir.X + offset.Y * wcsDir.Y;
                    double newSegLen = startSegLen + proj;
                    if (newSegLen < 10.0) newSegLen = 10.0;

                    var newTextWCS = prevWCS + wcsDir * newSegLen;
                    AnnotationEngine.UpdateLeaderInBlock(br, newTextWCS);
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
                        // Obrócony blok — ruch WZDŁUŻ prętów (cos θ, sin θ)
                        // (nie wzdłuż dist line, bo to oddala kółka od prętów)
                        double angle = br.Rotation;
                        double dx = Math.Cos(angle);
                        double dy = Math.Sin(angle);
                        double proj = offset.X * dx + offset.Y * dy;
                        newPos = new Point3d(
                            origPos.X + proj * dx,
                            origPos.Y + proj * dy,
                            origPos.Z);
                    }
                    else if (barAnnot.Direction == "X")
                    {
                        newPos = new Point3d(origPos.X + offset.X, origPos.Y, origPos.Z);
                    }
                    else
                    {
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

    // ----------------------------------------------------------------
    // Flaga bypass — ustawiana przez BarBlockTransformOverrule przed
    // programowym przesunięciem RC_BAR_ANNOT, zdejmowana w finally.
    // Dzięki temu AnnotTransformOverrule nie constraintuje tego ruchu.
    // BricsCAD UI jest jednowątkowe — statyczna flaga jest bezpieczna.
    // ----------------------------------------------------------------
    internal static class AnnotOverruleState
    {
        public static bool BypassConstraint { get; set; } = false;
        public static bool InGripDrag       { get; set; } = false;
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
            // Jeśli przesunięcie pochodzi z BarBlockTransformOverrule — nie ograniczaj osi
            if (AnnotOverruleState.BypassConstraint)
            {
                base.TransformBy(entity, transform);
                return;
            }

            if (br == null) { base.TransformBy(entity, transform); return; }

            // Blok pretow: swobodny ruch — brak ograniczenia kierunku
            if (BarBlockEngine.IsBarBlock(br)) { base.TransformBy(entity, transform); return; }

            // Sprawdź kąt obrotu bloku annotacji
            double annotAngle = 0.0;
            var barAnnotForAngle = AnnotationEngine.ReadAnnotXData(br);
            if (barAnnotForAngle != null)
                annotAngle = br.Rotation;

            // Jeśli blok jest obrócony (ukośne pręty) — przepuść transform bez constraintu
            if (Math.Abs(annotAngle) > 1e-6)
            {
                base.TransformBy(entity, transform);
                return;
            }

            // Standardowy constraint dla poziomych/pionowych prętów
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

            if (AnnotOverruleState.InGripDrag) return;

            if (!(entity is BlockReference br)) return;

            var db = br.Database;
            if (db == null) return;

            string annotHandle = ReadAnnotHandle(br);
            if (string.IsNullOrEmpty(annotHandle)) return;

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

                // MOVE/COPY — stosujemy ten sam transform co na bar block
                AnnotOverruleState.BypassConstraint = true;
                try { annotBr.TransformBy(transform); }
                finally { AnnotOverruleState.BypassConstraint = false; }

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

            string annotHandle = ReadAnnotHandle(br);
            if (string.IsNullOrEmpty(annotHandle)) return;

            try
            {
                using var tr = db.TransactionManager.StartTransaction();

                if (!long.TryParse(annotHandle, NumberStyles.HexNumber, null, out long hVal))
                { tr.Commit(); return; }

                var handle = new Handle(hVal);
                if (!db.TryGetObjectId(handle, out ObjectId annotId)
                    || annotId.IsNull || !annotId.IsValid || annotId.IsErased)
                { tr.Commit(); return; }

                var annotBr = tr.GetObject(annotId, OpenMode.ForWrite) as BlockReference;
                if (annotBr == null) { tr.Commit(); return; }

                annotBr.Erase(true);

                // Zaktualizuj etykietę pręta-źródłowego — przelicz sumę pozostałych rozkładów
                var barXdErase = BarBlockEngine.ReadXData(br);
                tr.Commit();
                if (barXdErase != null && !string.IsNullOrEmpty(barXdErase.SourceBarHandle))
                    PendingLabelUpdates.Add(barXdErase.SourceBarHandle);
            }
            catch { /* nie przerywaj głównego usuwania */ }
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

            // Odczytaj LabelHandle z XData RC_SINGLE_BAR
            var bar = SingleBarEngine.ReadBarXData(pline);
            if (bar == null || string.IsNullOrEmpty(bar.LabelHandle)) return;

            try
            {
                using var tr = db.TransactionManager.StartTransaction();

                // Obsługuj zarówno hex jak i decimal format handle
                ObjectId lblId = ObjectId.Null;
                if (long.TryParse(bar.LabelHandle,
                        System.Globalization.NumberStyles.HexNumber,
                        null, out long hValHex))
                {
                    var h = new Handle(hValHex);
                    if (db.TryGetObjectId(h, out ObjectId id) && !id.IsNull && !id.IsErased)
                        lblId = id;
                }
                if (lblId.IsNull && long.TryParse(bar.LabelHandle,
                        System.Globalization.NumberStyles.Integer,
                        null, out long hValDec))
                {
                    var h = new Handle(hValDec);
                    if (db.TryGetObjectId(h, out ObjectId id) && !id.IsNull && !id.IsErased)
                        lblId = id;
                }

                if (lblId.IsNull) { tr.Commit(); return; }

                var ml = tr.GetObject(lblId, OpenMode.ForWrite) as MLeader;
                ml?.Erase(true);

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
                    if (string.Equals(barBlock.SourceBarHandle, plineHandle,
                            StringComparison.OrdinalIgnoreCase))
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
            catch { }
        }

        public new void SetCustomFilter()
        {
            this.SetXDataFilter("RC_SINGLE_BAR");
        }
    }

    // ----------------------------------------------------------------
    // PendingLabelUpdates — opóźniona aktualizacja etykiet po ERASE
    // ----------------------------------------------------------------
    internal static class PendingLabelUpdates
    {
        static readonly HashSet<string> _pendingHandles = new();

        public static void Add(string sourceBarHandle)
        {
            if (!string.IsNullOrEmpty(sourceBarHandle))
                _pendingHandles.Add(sourceBarHandle);
        }

        public static void FlushAll(Database db)
        {
            foreach (var h in _pendingHandles)
                AnnotationEngine.UpdateBarLabelCount(db, h);
            _pendingHandles.Clear();
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
            Overrule.RemoveOverrule(cls, _barBlockTransform);
            Overrule.RemoveOverrule(cls, _transform);
            Overrule.RemoveOverrule(cls, _grip);
            Overrule.RemoveOverrule(
                RXObject.GetClass(typeof(Teigha.DatabaseServices.Polyline)),
                _barPolylineErase);
            _barBlockErase.Dispose();     _barBlockErase     = null;
            _barBlockTransform.Dispose(); _barBlockTransform = null;
            _transform.Dispose();         _transform         = null;
            _grip.Dispose();              _grip              = null;
            _barPolylineErase.Dispose();  _barPolylineErase  = null;
        }
    }
}
