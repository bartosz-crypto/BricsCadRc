using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using BricsCadRc.Core;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;

namespace BricsCadRc.Commands
{
    /// <summary>
    /// FEATURE J: Interaktywne umieszczanie etykiety — podgląd złamanego leadera.
    /// Geometria obliczana przez AnnotLeaderGeometry (testowalny, bez BricsCAD API).
    /// </summary>
    internal class AnnotLeaderJig : DrawJig
    {
        readonly Point3d _anchorPt;
        readonly double  _barsSpan;
        readonly double  _armPreviewLen;
        readonly double  _barMinCoordH;     // MinPoint.Y prętów (dla X-bars)
        readonly double  _barMinCoordV;     // MinPoint.X prętów (dla Y-bars)
        readonly bool    _barsHorizontal;   // orientacja prętów — STAŁA

        bool    _leaderHorizontal;          // kierunek etykiety — aktualizowany przez Compute
        Point3d _cursor;
        readonly List<Line> _transients = new List<Line>();

        /// <summary>Punkt wstawienia bloku annotacji po kliknięciu.</summary>
        public Point3d InsertPt
        {
            get
            {
                var p = AnnotLeaderGeometry.ComputeInsertPt(
                    _cursor.X, _cursor.Y,
                    _anchorPt.X, _anchorPt.Y,
                    _barMinCoordH, _barMinCoordV,
                    _barsHorizontal, _barsSpan);
                return new Point3d(p.X, p.Y, 0);
            }
        }

        /// <summary>Kierunek etykiety — ustalony po zakończeniu jiga.</summary>
        public bool LeaderHorizontal => _leaderHorizontal;

        /// <summary>Etykieta idzie w prawo (cursor.X >= anchorPt.X).</summary>
        public bool LeaderRight => _cursor.X >= _anchorPt.X;

        /// <summary>Ostatnia pozycja kursora — używana jako punkt złamania (bend).</summary>
        public Point3d LastCursorPt => _cursor;

        public AnnotLeaderJig(
            Point3d anchorPt,
            double  barsSpan,
            double  barMinCoordH,
            double  barMinCoordV,
            bool    horizontal,
            double  armPreviewLen = 700.0)
        {
            _anchorPt         = anchorPt;
            _barsSpan         = barsSpan;
            _barMinCoordH     = barMinCoordH;
            _barMinCoordV     = barMinCoordV;
            _barsHorizontal   = horizontal;
            _leaderHorizontal = horizontal;
            _armPreviewLen    = armPreviewLen;
            _cursor           = anchorPt;
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var opts = new JigPromptPointOptions(
                "\nClick annotation position (Enter = automatic): ");

            var res = prompts.AcquirePoint(opts);
            if (res.Status != PromptStatus.OK) return SamplerStatus.NoChange;

            if (_cursor.IsEqualTo(res.Value, Tolerance.Global)) return SamplerStatus.NoChange;

            _cursor = res.Value;
            RefreshTransients();
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw) => true;

        private void RefreshTransients()
        {
            ClearTransients();
            var tm    = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();

            var pts = AnnotLeaderGeometry.Compute(
                _anchorPt.X, _anchorPt.Y,
                _barsSpan,
                _cursor.X, _cursor.Y,
                _barsHorizontal,
                _armPreviewLen);

            // Aktualizuj kierunek etykiety na podstawie obliczonej geometrii
            double dx = System.Math.Abs(_cursor.X - _anchorPt.X);
            double dy = System.Math.Abs(_cursor.Y - _anchorPt.Y);
            _leaderHorizontal = !(dy > dx * 2.0);

            AddLine(tm, vpIds,
                new Point3d(pts.DistPreviewStart.X, pts.DistPreviewStart.Y, 0),
                new Point3d(pts.Seg1Start.X,         pts.Seg1Start.Y,        0), 7);
            AddLine(tm, vpIds,
                new Point3d(pts.Seg1Start.X, pts.Seg1Start.Y, 0),
                new Point3d(pts.Seg1End.X,   pts.Seg1End.Y,   0), 7);
            AddLine(tm, vpIds,
                new Point3d(pts.Seg1End.X, pts.Seg1End.Y, 0),
                new Point3d(pts.ArmEnd.X,  pts.ArmEnd.Y,  0), 2);

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
