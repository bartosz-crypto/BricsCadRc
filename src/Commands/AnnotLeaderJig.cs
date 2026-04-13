using System;
using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;

namespace BricsCadRc.Commands
{
    public enum LabelDirection { Up, Down, Left, Right }

    internal static class Point3dRotate
    {
        // Transformacja lokalny → WCS: WCS = rotCenter + R(angle) × (localX, localY)
        internal static Point3d LocalToWCS(Point3d rotCenter, double angle, double localX, double localY)
        {
            if (Math.Abs(angle) < 1e-6)
                return new Point3d(rotCenter.X + localX, rotCenter.Y + localY, 0);
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            return new Point3d(
                rotCenter.X + localX * cos - localY * sin,
                rotCenter.Y + localX * sin + localY * cos,
                0);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ETAP 1: dist line + romby podążają za kursorem wzdłuż osi prętów.
    //   X-bars (horizontal=true):  free=X, locked=Y=minFixed.
    //   Y-bars (horizontal=false): free=Y, locked=X=minFixed.
    // Output: LabelPos (wartość wolnej osi po kliknięciu).
    // ─────────────────────────────────────────────────────────────────────────
    internal class AnnotLabelPositionJig : DrawJig
    {
        readonly double     _minFixed;    // locked Y (X-bars) or locked X (Y-bars)
        readonly double     _barsSpan;
        readonly double     _spacing;
        readonly int        _count;
        readonly bool       _horizontal;  // true = X-bars (dist line pionowa, free=X)
        readonly double     _dotRadius;
        readonly double     _angle;
        readonly Point3d    _rotCenter;

        double              _freeAxis;
        Point3d             _lastCursor = Point3d.Origin;
        readonly List<Line> _transients = new List<Line>();

        public double LabelPos => _freeAxis;

        public AnnotLabelPositionJig(
            double minFixed, double basePos,
            double barsSpan, double spacing, int count,
            bool horizontal, double dotRadius,
            double angle = 0.0, Point3d rotCenter = default)
        {
            _minFixed   = minFixed;
            _barsSpan   = barsSpan;
            _spacing    = spacing;
            _count      = count;
            _horizontal = horizontal;
            _dotRadius  = dotRadius;
            _angle      = angle;
            _rotCenter  = rotCenter;
            _freeAxis   = basePos;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var opts = new JigPromptPointOptions("\nPozycja etykiety [Enter=auto]: ");
            var res  = prompts.AcquirePoint(opts);
            if (res.Status != PromptStatus.OK) return SamplerStatus.NoChange;

            Point3d cursor = res.Value;
            _lastCursor = cursor;
            double newFree;
            if (Math.Abs(_angle) > 1e-6)
            {
                // Przelicz kursor do układu lokalnego bloku (odwrotna rotacja)
                double dx  = cursor.X - _rotCenter.X;
                double dy  = cursor.Y - _rotCenter.Y;
                double cos = Math.Cos(-_angle);
                double sin = Math.Sin(-_angle);
                double localX = dx * cos - dy * sin;
                double localY = dx * sin + dy * cos;
                newFree = _horizontal ? localX : localY;
            }
            else
            {
                newFree = _horizontal ? cursor.X : cursor.Y;
            }
            if (Math.Abs(newFree - _freeAxis) < 0.01) return SamplerStatus.NoChange;
            _freeAxis = newFree;
            RefreshTransients();
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw) => true;

        void RefreshTransients()
        {
            ClearTransients();
            var tm    = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();

            if (_horizontal)
            {
                // X-bars: pionowa dist line przy x=_freeAxis (lokalna)
                double x = _freeAxis;
                var rp1 = Point3dRotate.LocalToWCS(_rotCenter, _angle, x, _minFixed);
                var rp2 = Point3dRotate.LocalToWCS(_rotCenter, _angle, x, _minFixed + _barsSpan);
                AddLine(tm, vpIds, rp1, rp2, 7);
                if (Math.Abs(_angle) > 1e-6 && _lastCursor != Point3d.Origin)
                {
                    double sinA = Math.Sin(_angle);
                    double cosA = Math.Cos(_angle);
                    double t = (_lastCursor.X - rp1.X) * (-sinA)
                             + (_lastCursor.Y - rp1.Y) * cosA;
                    var cursorOnLine = new Point3d(rp1.X + t * (-sinA), rp1.Y + t * cosA, 0);
                    if (t > _barsSpan)
                        AddLine(tm, vpIds, rp2, cursorOnLine, 8);
                    else if (t < 0)
                        AddLine(tm, vpIds, rp1, cursorOnLine, 8);
                }
                int maxDots = Math.Min(_count, 20);
                for (int i = 0; i < maxDots; i++)
                {
                    var rc = Point3dRotate.LocalToWCS(_rotCenter, _angle, x, _minFixed + i * _spacing);
                    AddCirclePreview(tm, vpIds, rc, _dotRadius);
                }
            }
            else
            {
                // Y-bars: pozioma dist line przy y=_freeAxis (lokalna)
                double y = _freeAxis;
                var rp1 = Point3dRotate.LocalToWCS(_rotCenter, _angle, _minFixed,             y);
                var rp2 = Point3dRotate.LocalToWCS(_rotCenter, _angle, _minFixed + _barsSpan, y);
                AddLine(tm, vpIds, rp1, rp2, 7);
                if (Math.Abs(_angle) > 1e-6 && _lastCursor != Point3d.Origin)
                {
                    double sinA = Math.Sin(_angle);
                    double cosA = Math.Cos(_angle);
                    double t = (_lastCursor.X - rp1.X) * (-sinA)
                             + (_lastCursor.Y - rp1.Y) * cosA;
                    var cursorOnLine = new Point3d(rp1.X + t * (-sinA), rp1.Y + t * cosA, 0);
                    if (t > _barsSpan)
                        AddLine(tm, vpIds, rp2, cursorOnLine, 8);
                    else if (t < 0)
                        AddLine(tm, vpIds, rp1, cursorOnLine, 8);
                }
                int maxDots = Math.Min(_count, 20);
                for (int i = 0; i < maxDots; i++)
                {
                    var rc = Point3dRotate.LocalToWCS(_rotCenter, _angle, _minFixed + i * _spacing, y);
                    AddCirclePreview(tm, vpIds, rc, _dotRadius);
                }
            }

            try { Application.UpdateScreen(); } catch { }
        }

        void AddLine(TransientManager tm, IntegerCollection vpIds,
                     Point3d p1, Point3d p2, short color)
        {
            var ln = new Line(p1, p2) { ColorIndex = color };
            try { tm.AddTransient(ln, TransientDrawingMode.DirectTopmost, 128, vpIds); _transients.Add(ln); }
            catch { ln.Dispose(); }
        }

        void AddCirclePreview(TransientManager tm, IntegerCollection vpIds, Point3d c, double r)
        {
            int segments = 8;
            for (int s = 0; s < segments; s++)
            {
                double a1 = 2 * Math.PI * s       / segments;
                double a2 = 2 * Math.PI * (s + 1) / segments;
                AddLine(tm, vpIds,
                    new Point3d(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1), 0),
                    new Point3d(c.X + r * Math.Cos(a2), c.Y + r * Math.Sin(a2), 0),
                    7);
            }
        }

        public void ClearTransients()
        {
            if (_transients.Count == 0) return;
            var tm    = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();
            foreach (var ln in _transients)
                try { tm.EraseTransient(ln, vpIds); ln.Dispose(); } catch { }
            _transients.Clear();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ETAP 2: kierunek etykiety — linia od środka dist line do kursora,
    // snappowana do 4 kierunków (próg 45°: |dy| >= |dx|).
    // Output: Direction, KinkPt (snapped cursor na osi kierunku).
    // ─────────────────────────────────────────────────────────────────────────
    internal class AnnotLabelDirectionJig : DrawJig
    {
        readonly Point3d    _centerPt;   // środek dist line w world coords
        readonly double     _labelPos;   // labelX (X-bars) lub labelY (Y-bars)
        readonly double     _minFixed;
        readonly double     _barsSpan;
        readonly bool       _horizontal;
        readonly double     _dotRadius;
        readonly int        _count;
        readonly double     _spacing;
        readonly double     _angle;
        readonly Point3d    _rotCenter;

        Point3d             _snappedPt;
        LabelDirection      _direction;
        Point3d             _lastCursor = Point3d.Origin;
        private Point3d     _prevLastCursor;
        readonly List<Line> _transients = new List<Line>();

        public LabelDirection Direction     => _direction;
        public Point3d        KinkPt        => _snappedPt;
        public Point3d        LastCursorPt  { get; private set; }

        public AnnotLabelDirectionJig(
            Point3d centerPt, double labelPos, double minFixed,
            double barsSpan, bool horizontal,
            double dotRadius, int count, double spacing,
            double angle = 0.0, Point3d rotCenter = default)
        {
            _centerPt   = centerPt;
            _labelPos   = labelPos;
            _minFixed   = minFixed;
            _barsSpan   = barsSpan;
            _horizontal = horizontal;
            _dotRadius  = dotRadius;
            _count      = count;
            _spacing    = spacing;
            _angle      = angle;
            _rotCenter  = rotCenter;
            _snappedPt   = centerPt;
            _direction   = LabelDirection.Up;
            LastCursorPt = centerPt;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var opts = new JigPromptPointOptions("\nKierunek etykiety (góra/dół/lewo/prawo): ");
            var res  = prompts.AcquirePoint(opts);
            if (res.Status != PromptStatus.OK) return SamplerStatus.NoChange;

            var    cursor = res.Value;
            _prevLastCursor = _lastCursor;
            _lastCursor   = cursor;
            LastCursorPt  = cursor;
            Point3d effectiveCursor = cursor;
            if (Math.Abs(_angle) > 1e-6)
            {
                // Przelicz kursor do układu lokalnego
                double ddx = cursor.X - _rotCenter.X;
                double ddy = cursor.Y - _rotCenter.Y;
                double cos = Math.Cos(-_angle);
                double sin = Math.Sin(-_angle);
                effectiveCursor = new Point3d(
                    _rotCenter.X + ddx * cos - ddy * sin,
                    _rotCenter.Y + ddx * sin + ddy * cos,
                    0);
            }
            double dx = effectiveCursor.X - _centerPt.X;
            double dy = effectiveCursor.Y - _centerPt.Y;

            LabelDirection newDir = _direction;  // default dla obróconych bloków
            Point3d        newSnap;

            if (Math.Abs(_angle) > 1e-6)
            {
                double sinA = Math.Sin(_angle);
                double cosA = Math.Cos(_angle);
                // Rzut kursora na dist line przez _centerPt (kierunek dist line: -sinA, cosA)
                double t = (cursor.X - _centerPt.X) * (-sinA)
                         + (cursor.Y - _centerPt.Y) * cosA;
                // Clamp: kinkPt musi być na dist line [od pierwszego do ostatniego pręta]
                double halfSpan = _barsSpan / 2.0;
                t = Math.Max(-halfSpan, Math.Min(halfSpan, t));
                newSnap = new Point3d(
                    _centerPt.X + t * (-sinA),
                    _centerPt.Y + t * cosA,
                    0);
            }
            else if (Math.Abs(dy) >= Math.Abs(dx))
            {
                newDir  = dy >= 0 ? LabelDirection.Up   : LabelDirection.Down;
                // Dla X-bars (horizontal=true): dist line pionowa, kink przesuwa się w Y wzdłuż dist line
                // Dla Y-bars (horizontal=false): dist line pozioma, kink przesuwa się w X wzdłuż dist line
                newSnap = _horizontal
                    ? new Point3d(_centerPt.X, cursor.Y, 0)   // X-bars: Y swobodne
                    : new Point3d(cursor.X, _centerPt.Y, 0);  // Y-bars: X swobodne
            }
            else
            {
                newDir  = dx >= 0 ? LabelDirection.Right : LabelDirection.Left;
                // Analogicznie — kink przesuwa się wzdłuż dist line
                newSnap = _horizontal
                    ? new Point3d(_centerPt.X, cursor.Y, 0)   // X-bars: Y swobodne (nawet dla L/R)
                    : new Point3d(cursor.X, _centerPt.Y, 0);  // Y-bars: X swobodne
            }

            if (_direction == newDir && _snappedPt.IsEqualTo(newSnap, Tolerance.Global))
            {
                // Y-bars Up/Down: snap.X zmienia się z cursor.X, ale gdy kursor idzie prosto w górę
                // cursor.X ≈ centerPt.X — snap się nie zmienia. Sprawdź ruch w Y osobno.
                bool yBarsCursorMoved = !_horizontal
                    && (newDir == LabelDirection.Up || newDir == LabelDirection.Down)
                    && Math.Abs(cursor.Y - _prevLastCursor.Y) > 0.01;
                if (!yBarsCursorMoved)
                    return SamplerStatus.NoChange;
                // else: kursor przesunął się w Y — odśwież podgląd
            }

            _direction = newDir;
            _snappedPt = newSnap;
            RefreshTransients();
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw) => true;

        void RefreshTransients()
        {
            ClearTransients();
            var tm    = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();

            // Dist line (zamrożona)
            DrawDistLine(tm, vpIds);

            // Dla obróconych bloków — linia kierunku do kursora (nie do snappedPt na dist line)
            Point3d arrowEnd;
            if (Math.Abs(_angle) > 1e-6)
                arrowEnd = _lastCursor;
            else if (!_horizontal
                     && (_direction == LabelDirection.Up || _direction == LabelDirection.Down))
                // Y-bars Up/Down: pokaż strzałkę idącą pionowo do pozycji kursora
                arrowEnd = new Point3d(_snappedPt.X, _lastCursor.Y, 0);
            else
                arrowEnd = _snappedPt;
            AddLine(tm, vpIds, _centerPt, arrowEnd, 2);

            try { Application.UpdateScreen(); } catch { }
        }

        void DrawDistLine(TransientManager tm, IntegerCollection vpIds)
        {
            if (_horizontal)
            {
                double x = _labelPos;
                var rp1 = Point3dRotate.LocalToWCS(_rotCenter, _angle, x, _minFixed);
                var rp2 = Point3dRotate.LocalToWCS(_rotCenter, _angle, x, _minFixed + _barsSpan);
                AddLine(tm, vpIds, rp1, rp2, 7);
                if (Math.Abs(_angle) > 1e-6 && _lastCursor != Point3d.Origin)
                {
                    double sinA = Math.Sin(_angle);
                    double cosA = Math.Cos(_angle);
                    double t = (_lastCursor.X - rp1.X) * (-sinA)
                             + (_lastCursor.Y - rp1.Y) * cosA;
                    var cursorOnLine = new Point3d(rp1.X + t * (-sinA), rp1.Y + t * cosA, 0);
                    if (t > _barsSpan)
                        AddLine(tm, vpIds, rp2, cursorOnLine, 8);
                    else if (t < 0)
                        AddLine(tm, vpIds, rp1, cursorOnLine, 8);
                }
                int maxDots = Math.Min(_count, 20);
                for (int i = 0; i < maxDots; i++)
                {
                    var rc = Point3dRotate.LocalToWCS(_rotCenter, _angle, x, _minFixed + i * _spacing);
                    AddCirclePreview(tm, vpIds, rc, _dotRadius);
                }
            }
            else
            {
                double y = _labelPos;
                var rp1 = Point3dRotate.LocalToWCS(_rotCenter, _angle, _minFixed,             y);
                var rp2 = Point3dRotate.LocalToWCS(_rotCenter, _angle, _minFixed + _barsSpan, y);
                AddLine(tm, vpIds, rp1, rp2, 7);
                if (Math.Abs(_angle) > 1e-6 && _lastCursor != Point3d.Origin)
                {
                    double sinA = Math.Sin(_angle);
                    double cosA = Math.Cos(_angle);
                    double t = (_lastCursor.X - rp1.X) * (-sinA)
                             + (_lastCursor.Y - rp1.Y) * cosA;
                    var cursorOnLine = new Point3d(rp1.X + t * (-sinA), rp1.Y + t * cosA, 0);
                    if (t > _barsSpan)
                        AddLine(tm, vpIds, rp2, cursorOnLine, 8);
                    else if (t < 0)
                        AddLine(tm, vpIds, rp1, cursorOnLine, 8);
                }
                int maxDots = Math.Min(_count, 20);
                for (int i = 0; i < maxDots; i++)
                {
                    var rc = Point3dRotate.LocalToWCS(_rotCenter, _angle, _minFixed + i * _spacing, y);
                    AddCirclePreview(tm, vpIds, rc, _dotRadius);
                }
            }
        }

        void AddLine(TransientManager tm, IntegerCollection vpIds,
                     Point3d p1, Point3d p2, short color)
        {
            var ln = new Line(p1, p2) { ColorIndex = color };
            try { tm.AddTransient(ln, TransientDrawingMode.DirectTopmost, 128, vpIds); _transients.Add(ln); }
            catch { ln.Dispose(); }
        }

        void AddCirclePreview(TransientManager tm, IntegerCollection vpIds, Point3d c, double r)
        {
            int segments = 8;
            for (int s = 0; s < segments; s++)
            {
                double a1 = 2 * Math.PI * s       / segments;
                double a2 = 2 * Math.PI * (s + 1) / segments;
                AddLine(tm, vpIds,
                    new Point3d(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1), 0),
                    new Point3d(c.X + r * Math.Cos(a2), c.Y + r * Math.Sin(a2), 0),
                    7);
            }
        }

        public void ClearTransients()
        {
            if (_transients.Count == 0) return;
            var tm    = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();
            foreach (var ln in _transients)
                try { tm.EraseTransient(ln, vpIds); ln.Dispose(); } catch { }
            _transients.Clear();
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ETAP 3 (tylko dla Up/Down): zamrożony pionowy segment + podgląd ramienia.
    // Używany gdy direction=Up lub Down po ETAP 2.
    //   Enter → brak złamania (leaderHorizontal=false), HasBend=false.
    //   Klik  → złamanie poziome (leaderHorizontal=true),  HasBend=true.
    // Output: HasBend (false jeśli Enter), CursorPt (pozycja kliknięcia).
    // ─────────────────────────────────────────────────────────────────────────
    internal class AnnotLabelBendJig : DrawJig
    {
        readonly Point3d    _centerPt;   // środek dist line (start pionowego segmentu)
        readonly Point3d    _kinkPt;     // koniec pionowego segmentu (od ETAP 2)
        readonly double     _labelPos;   // labelX
        readonly double     _minFixed;   // minY
        readonly double     _barsSpan;
        readonly bool       _horizontal; // true = X-bars
        readonly double     _dotRadius;
        readonly int        _count;
        readonly double     _spacing;
        readonly double     _angle;
        readonly Point3d    _rotCenter;

        Point3d             _cursor;
        Point3d             _lastCursor = Point3d.Origin;
        readonly List<Line> _transients = new List<Line>();

        public Point3d CursorPt => _cursor;

        public AnnotLabelBendJig(
            Point3d centerPt, Point3d kinkPt,
            double labelPos, double minFixed, double barsSpan,
            bool horizontal, double dotRadius, int count, double spacing,
            double angle = 0.0, Point3d rotCenter = default)
        {
            _centerPt   = centerPt;
            _kinkPt     = kinkPt;
            _labelPos   = labelPos;
            _minFixed   = minFixed;
            _barsSpan   = barsSpan;
            _horizontal = horizontal;
            _dotRadius  = dotRadius;
            _count      = count;
            _spacing    = spacing;
            _angle      = angle;
            _rotCenter  = rotCenter;
            _cursor     = kinkPt;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var opts = new JigPromptPointOptions(
                "\nZłam ramię w bok (klik) lub zatwierdź pionowe (Enter): ");
            var res = prompts.AcquirePoint(opts);
            if (res.Status != PromptStatus.OK) return SamplerStatus.NoChange;
            if (_cursor.IsEqualTo(res.Value, Tolerance.Global)) return SamplerStatus.NoChange;
            _cursor = res.Value;
            _lastCursor = res.Value;
            RefreshTransients();
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw) => true;

        void RefreshTransients()
        {
            ClearTransients();
            var tm    = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();

            // Dist line (zamrożona)
            DrawDistLine(tm, vpIds);

            // _centerPt, _kinkPt, _cursor już w WCS — bez transformacji
            if (_horizontal)
            {
                // X-bars: stem pionowy (centerPt → kinkPt), ramię poziome
                AddLine(tm, vpIds, _centerPt, _kinkPt, 2);
                var armEnd = new Point3d(_cursor.X, _kinkPt.Y, 0);
                AddLine(tm, vpIds, _kinkPt, armEnd, 2);
            }
            else
            {
                // Y-bars: stem poziomy (centerPt → kinkPt), ramię pionowe
                AddLine(tm, vpIds, _centerPt, _kinkPt, 2);
                var armEnd = new Point3d(_kinkPt.X, _cursor.Y, 0);
                AddLine(tm, vpIds, _kinkPt, armEnd, 2);
            }

            try { Application.UpdateScreen(); } catch { }
        }

        void DrawDistLine(TransientManager tm, IntegerCollection vpIds)
        {
            if (_horizontal)
            {
                double x = _labelPos;
                var rp1 = Point3dRotate.LocalToWCS(_rotCenter, _angle, x, _minFixed);
                var rp2 = Point3dRotate.LocalToWCS(_rotCenter, _angle, x, _minFixed + _barsSpan);
                AddLine(tm, vpIds, rp1, rp2, 7);
                if (Math.Abs(_angle) > 1e-6 && _lastCursor != Point3d.Origin)
                {
                    double sinA = Math.Sin(_angle);
                    double cosA = Math.Cos(_angle);
                    double t = (_lastCursor.X - rp1.X) * (-sinA)
                             + (_lastCursor.Y - rp1.Y) * cosA;
                    var cursorOnLine = new Point3d(rp1.X + t * (-sinA), rp1.Y + t * cosA, 0);
                    if (t > _barsSpan)
                        AddLine(tm, vpIds, rp2, cursorOnLine, 8);
                    else if (t < 0)
                        AddLine(tm, vpIds, rp1, cursorOnLine, 8);
                }
                int maxDots = Math.Min(_count, 20);
                for (int i = 0; i < maxDots; i++)
                {
                    var rc = Point3dRotate.LocalToWCS(_rotCenter, _angle, x, _minFixed + i * _spacing);
                    AddCirclePreview(tm, vpIds, rc, _dotRadius);
                }
            }
            else
            {
                double y = _labelPos;
                var rp1 = Point3dRotate.LocalToWCS(_rotCenter, _angle, _minFixed,             y);
                var rp2 = Point3dRotate.LocalToWCS(_rotCenter, _angle, _minFixed + _barsSpan, y);
                AddLine(tm, vpIds, rp1, rp2, 7);
                if (Math.Abs(_angle) > 1e-6 && _lastCursor != Point3d.Origin)
                {
                    double sinA = Math.Sin(_angle);
                    double cosA = Math.Cos(_angle);
                    double t = (_lastCursor.X - rp1.X) * (-sinA)
                             + (_lastCursor.Y - rp1.Y) * cosA;
                    var cursorOnLine = new Point3d(rp1.X + t * (-sinA), rp1.Y + t * cosA, 0);
                    if (t > _barsSpan)
                        AddLine(tm, vpIds, rp2, cursorOnLine, 8);
                    else if (t < 0)
                        AddLine(tm, vpIds, rp1, cursorOnLine, 8);
                }
                int maxDots = Math.Min(_count, 20);
                for (int i = 0; i < maxDots; i++)
                {
                    var rc = Point3dRotate.LocalToWCS(_rotCenter, _angle, _minFixed + i * _spacing, y);
                    AddCirclePreview(tm, vpIds, rc, _dotRadius);
                }
            }
        }

        void AddLine(TransientManager tm, IntegerCollection vpIds,
                     Point3d p1, Point3d p2, short color)
        {
            var ln = new Line(p1, p2) { ColorIndex = color };
            try { tm.AddTransient(ln, TransientDrawingMode.DirectTopmost, 128, vpIds); _transients.Add(ln); }
            catch { ln.Dispose(); }
        }

        void AddCirclePreview(TransientManager tm, IntegerCollection vpIds, Point3d c, double r)
        {
            int segments = 8;
            for (int s = 0; s < segments; s++)
            {
                double a1 = 2 * Math.PI * s       / segments;
                double a2 = 2 * Math.PI * (s + 1) / segments;
                AddLine(tm, vpIds,
                    new Point3d(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1), 0),
                    new Point3d(c.X + r * Math.Cos(a2), c.Y + r * Math.Sin(a2), 0),
                    7);
            }
        }

        void AddDiamond(TransientManager tm, IntegerCollection vpIds, Point3d c, double r)
        {
            AddLine(tm, vpIds, new Point3d(c.X,     c.Y + r, 0), new Point3d(c.X + r, c.Y,     0), 7);
            AddLine(tm, vpIds, new Point3d(c.X + r, c.Y,     0), new Point3d(c.X,     c.Y - r, 0), 7);
            AddLine(tm, vpIds, new Point3d(c.X,     c.Y - r, 0), new Point3d(c.X - r, c.Y,     0), 7);
            AddLine(tm, vpIds, new Point3d(c.X - r, c.Y,     0), new Point3d(c.X,     c.Y + r, 0), 7);
        }

        public void ClearTransients()
        {
            if (_transients.Count == 0) return;
            var tm    = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();
            foreach (var ln in _transients)
                try { tm.EraseTransient(ln, vpIds); ln.Dispose(); } catch { }
            _transients.Clear();
        }
    }
}
