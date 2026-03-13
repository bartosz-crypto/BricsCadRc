using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;

namespace BricsCadRc.Commands
{
    /// <summary>
    /// FEATURE I: Interaktywne umieszczanie etykiety po GenerateFromBounds.
    ///
    /// Kursor jest constrainowany do osi prostopadłej do prętów:
    ///   X-bars (horizontal=true) : kursor może się ruszać swobodnie w X,
    ///                              Y jest zablokowane do barMinCoord (Y pierwszego pręta)
    ///   Y-bars (horizontal=false): kursor może się ruszać swobodnie w Y,
    ///                              X jest zablokowane do barMinCoord (X pierwszego pręta)
    ///
    /// Dzięki temu romby dist line są zawsze wyrównane do prętów niezależnie od
    /// gdzie kliknie użytkownik — tak jak w ASD.
    ///
    /// InsertPt = constrained cursor → poprawny insertPt dla CreateLeader.
    /// </summary>
    internal class AnnotLeaderJig : DrawJig
    {
        readonly bool   _horizontal;
        readonly double _barsSpan;
        readonly double _armPreviewLen;
        readonly double _barMinCoord;   // Y pierwszego pręta (X-bars) lub X pierwszego pręta (Y-bars)

        Point3d _cursor;
        readonly List<Line> _transients = new List<Line>();

        /// <summary>Constrained cursor = poprawny insertPt dla CreateLeader.</summary>
        public Point3d InsertPt => _cursor;

        /// <param name="startCursor">Pozycja startowa kursora (np. obok bloku prętów).</param>
        /// <param name="horizontal">true = X-bars (dist line pionowa), false = Y-bars (pozioma).</param>
        /// <param name="barsSpan">Długość dist line = (count-1)*spacing.</param>
        /// <param name="barMinCoord">MinPoint.Y dla X-bars lub MinPoint.X dla Y-bars (wyrównanie romby).</param>
        /// <param name="armPreviewLen">Szacunkowa długość ramienia + tekstu.</param>
        public AnnotLeaderJig(
            Point3d startCursor,
            bool    horizontal,
            double  barsSpan,
            double  barMinCoord,
            double  armPreviewLen = 700.0)
        {
            _horizontal    = horizontal;
            _barsSpan      = barsSpan;
            _barMinCoord   = barMinCoord;
            _armPreviewLen = armPreviewLen;
            _cursor        = Constrain(startCursor);
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var opts = new JigPromptPointOptions(
                "\nClick annotation position (Enter = automatic): ");

            var res = prompts.AcquirePoint(opts);
            if (res.Status != PromptStatus.OK) return SamplerStatus.NoChange;

            var constrained = Constrain(res.Value);
            if (_cursor.IsEqualTo(constrained, Tolerance.Global)) return SamplerStatus.NoChange;

            _cursor = constrained;
            RefreshTransients();
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw) => true;

        /// <summary>Constrainuje punkt do właściwej osi.</summary>
        private Point3d Constrain(Point3d p)
        {
            return _horizontal
                ? new Point3d(p.X, _barMinCoord, 0)   // X-bars: X swobodnie, Y=barMinCoord
                : new Point3d(_barMinCoord, p.Y, 0);  // Y-bars: Y swobodnie, X=barMinCoord
        }

        private void RefreshTransients()
        {
            ClearTransients();
            var tm    = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();

            Point3d distEnd, armEnd;
            if (_horizontal)
            {
                // X-bars: dist line pionowa w górę od kursora, ramię dalej w górę
                distEnd = new Point3d(_cursor.X, _cursor.Y + _barsSpan,                0);
                armEnd  = new Point3d(_cursor.X, _cursor.Y + _barsSpan + _armPreviewLen, 0);
            }
            else
            {
                // Y-bars: dist line pozioma w prawo od kursora, ramię dalej w prawo
                distEnd = new Point3d(_cursor.X + _barsSpan,                _cursor.Y, 0);
                armEnd  = new Point3d(_cursor.X + _barsSpan + _armPreviewLen, _cursor.Y, 0);
            }

            // Dist line (kolor 7 = biały, jak LeaderLayer)
            AddLine(tm, vpIds, _cursor, distEnd, 7);
            // Ramię (kolor 2 = żółty, jak AnnotLayer)
            AddLine(tm, vpIds, distEnd, armEnd,  2);

            try { Application.UpdateScreen(); } catch { }
        }

        private void AddLine(TransientManager tm, IntegerCollection vpIds,
                             Point3d p1, Point3d p2, short color)
        {
            var line = new Line(p1, p2) { ColorIndex = color };
            try
            {
                tm.AddTransient(line, TransientDrawingMode.DirectTopmost, 128, vpIds);
                _transients.Add(line);
            }
            catch { line.Dispose(); }
        }

        /// <summary>Usuwa wszystkie transienty. Wywołać zawsze po ed.Drag(jig).</summary>
        public void ClearTransients()
        {
            if (_transients.Count == 0) return;
            var tm    = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();
            foreach (var line in _transients)
                try { tm.EraseTransient(line, vpIds); line.Dispose(); } catch { }
            _transients.Clear();
        }
    }
}
