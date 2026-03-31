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

        double              _freeAxis;
        readonly List<Line> _transients = new List<Line>();

        public double LabelPos => _freeAxis;

        public AnnotLabelPositionJig(
            double minFixed, double basePos,
            double barsSpan, double spacing, int count,
            bool horizontal, double dotRadius)
        {
            _minFixed   = minFixed;
            _barsSpan   = barsSpan;
            _spacing    = spacing;
            _count      = count;
            _horizontal = horizontal;
            _dotRadius  = dotRadius;
            _freeAxis   = basePos;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var opts = new JigPromptPointOptions("\nPozycja etykiety [Enter=auto]: ");
            var res  = prompts.AcquirePoint(opts);
            if (res.Status != PromptStatus.OK) return SamplerStatus.NoChange;

            double newFree = _horizontal ? res.Value.X : res.Value.Y;
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
                // X-bars: pionowa dist line przy x=_freeAxis
                double x = _freeAxis;
                AddLine(tm, vpIds, new Point3d(x, _minFixed,             0),
                                   new Point3d(x, _minFixed + _barsSpan, 0), 7);
                int maxDots = Math.Min(_count, 20);
                for (int i = 0; i < maxDots; i++)
                    AddCirclePreview(tm, vpIds, new Point3d(x, _minFixed + i * _spacing, 0), _dotRadius);
            }
            else
            {
                // Y-bars: pozioma dist line przy y=_freeAxis
                double y = _freeAxis;
                AddLine(tm, vpIds, new Point3d(_minFixed,             y, 0),
                                   new Point3d(_minFixed + _barsSpan, y, 0), 7);
                int maxDots = Math.Min(_count, 20);
                for (int i = 0; i < maxDots; i++)
                    AddCirclePreview(tm, vpIds, new Point3d(_minFixed + i * _spacing, y, 0), _dotRadius);
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

        Point3d             _snappedPt;
        LabelDirection      _direction;
        readonly List<Line> _transients = new List<Line>();

        public LabelDirection Direction => _direction;
        public Point3d        KinkPt    => _snappedPt;

        public AnnotLabelDirectionJig(
            Point3d centerPt, double labelPos, double minFixed,
            double barsSpan, bool horizontal,
            double dotRadius, int count, double spacing)
        {
            _centerPt   = centerPt;
            _labelPos   = labelPos;
            _minFixed   = minFixed;
            _barsSpan   = barsSpan;
            _horizontal = horizontal;
            _dotRadius  = dotRadius;
            _count      = count;
            _spacing    = spacing;
            _snappedPt  = centerPt;
            _direction  = LabelDirection.Up;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var opts = new JigPromptPointOptions("\nKierunek etykiety (góra/dół/lewo/prawo): ");
            var res  = prompts.AcquirePoint(opts);
            if (res.Status != PromptStatus.OK) return SamplerStatus.NoChange;

            var    cursor = res.Value;
            double dx     = cursor.X - _centerPt.X;
            double dy     = cursor.Y - _centerPt.Y;

            LabelDirection newDir;
            Point3d        newSnap;

            if (Math.Abs(dy) >= Math.Abs(dx))
            {
                newDir  = dy >= 0 ? LabelDirection.Up   : LabelDirection.Down;
                newSnap = new Point3d(_centerPt.X, cursor.Y, 0);
            }
            else
            {
                newDir  = dx >= 0 ? LabelDirection.Right : LabelDirection.Left;
                newSnap = new Point3d(cursor.X, _centerPt.Y, 0);
            }

            if (_direction == newDir && _snappedPt.IsEqualTo(newSnap, Tolerance.Global))
                return SamplerStatus.NoChange;

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

            // Linia kierunku (kolor 2 = żółty)
            AddLine(tm, vpIds, _centerPt, _snappedPt, 2);

            try { Application.UpdateScreen(); } catch { }
        }

        void DrawDistLine(TransientManager tm, IntegerCollection vpIds)
        {
            if (_horizontal)
            {
                double x = _labelPos;
                AddLine(tm, vpIds, new Point3d(x, _minFixed,             0),
                                   new Point3d(x, _minFixed + _barsSpan, 0), 7);
                int maxDots = Math.Min(_count, 20);
                for (int i = 0; i < maxDots; i++)
                    AddCirclePreview(tm, vpIds, new Point3d(x, _minFixed + i * _spacing, 0), _dotRadius);
            }
            else
            {
                double y = _labelPos;
                AddLine(tm, vpIds, new Point3d(_minFixed,             y, 0),
                                   new Point3d(_minFixed + _barsSpan, y, 0), 7);
                int maxDots = Math.Min(_count, 20);
                for (int i = 0; i < maxDots; i++)
                    AddCirclePreview(tm, vpIds, new Point3d(_minFixed + i * _spacing, y, 0), _dotRadius);
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

        Point3d             _cursor;
        readonly List<Line> _transients = new List<Line>();

        public Point3d CursorPt => _cursor;

        public AnnotLabelBendJig(
            Point3d centerPt, Point3d kinkPt,
            double labelPos, double minFixed, double barsSpan,
            bool horizontal, double dotRadius, int count, double spacing)
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
                AddLine(tm, vpIds, new Point3d(x, _minFixed,             0),
                                   new Point3d(x, _minFixed + _barsSpan, 0), 7);
                int maxDots = Math.Min(_count, 20);
                for (int i = 0; i < maxDots; i++)
                    AddDiamond(tm, vpIds, new Point3d(x, _minFixed + i * _spacing, 0), _dotRadius);
            }
            else
            {
                double y = _labelPos;
                AddLine(tm, vpIds, new Point3d(_minFixed,             y, 0),
                                   new Point3d(_minFixed + _barsSpan, y, 0), 7);
                int maxDots = Math.Min(_count, 20);
                for (int i = 0; i < maxDots; i++)
                    AddDiamond(tm, vpIds, new Point3d(_minFixed + i * _spacing, y, 0), _dotRadius);
            }
        }

        void AddLine(TransientManager tm, IntegerCollection vpIds,
                     Point3d p1, Point3d p2, short color)
        {
            var ln = new Line(p1, p2) { ColorIndex = color };
            try { tm.AddTransient(ln, TransientDrawingMode.DirectTopmost, 128, vpIds); _transients.Add(ln); }
            catch { ln.Dispose(); }
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
