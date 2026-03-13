using System;
using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;

namespace BricsCadRc.Commands
{
    /// <summary>
    /// FEATURE F: real-time preview prętów podczas wyboru drugiego punktu krawędzi.
    /// FEATURE G: klawisz S przełącza pręty na drugą stronę krawędzi (flip).
    ///
    /// Rendering WYŁĄCZNIE przez TransientManager — WorldDraw nic nie rysuje.
    /// Dzięki temu jig nie ingeruje w żaden istniejący kod rysowania.
    ///
    /// Użycie w BarCommands:
    ///   var jig = new DistributionJig(pt1, sourceBar.LengthA);
    ///   var res  = ed.Drag(jig);
    ///   jig.ClearTransients();          // zawsze po Drag
    ///   if (res.Status != OK) return;
    ///   Point3d pt2      = jig.SecondPoint;
    ///   bool edgeHoriz   = jig.EdgeHorizontal;
    ///   bool flipped     = jig.IsFlipped;   // FEATURE G
    /// </summary>
    internal class DistributionJig : DrawJig
    {
        // Wartości domyślne dla podglądu — cover i spacing pytane PO jigu
        const double PreviewCover   = 40.0;
        const double PreviewSpacing = 200.0;

        readonly Point3d _pt1;
        readonly double  _barLength;

        Point3d _pt2;
        bool    _edgeHorizontal;
        bool    _flipped;           // FEATURE G — S toggles side

        readonly List<Line> _transients = new List<Line>();

        public Point3d SecondPoint    => _pt2;
        public bool    EdgeHorizontal => _edgeHorizontal;
        public bool    IsFlipped      => _flipped;

        public DistributionJig(Point3d pt1, double barLength)
        {
            _pt1       = pt1;
            _barLength = barLength;
            _pt2       = pt1;
        }

        // ----------------------------------------------------------------
        // Sampler — śledzi kursor, odświeża transienty
        // ----------------------------------------------------------------

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var opts = new JigPromptPointOptions("\nSecond point along edge [S=flip side]: ");
            opts.UseBasePoint = true;
            opts.BasePoint    = _pt1;
            opts.Keywords.Add("S");

            var res = prompts.AcquirePoint(opts);

            // FEATURE G: klawisz S → przełącz flip i odśwież bez zmiany punktu
            if (res.Status == PromptStatus.Keyword && res.StringResult == "S")
            {
                _flipped = !_flipped;
                RefreshTransients();
                return SamplerStatus.OK;
            }

            if (res.Status != PromptStatus.OK)
                return SamplerStatus.NoChange;

            if (_pt2.IsEqualTo(res.Value, Tolerance.Global))
                return SamplerStatus.NoChange;

            _pt2 = res.Value;
            RefreshTransients();
            return SamplerStatus.OK;
        }

        // ----------------------------------------------------------------
        // WorldDraw — nic nie rysuje (cały rendering przez TransientManager)
        // ----------------------------------------------------------------

        protected override bool WorldDraw(WorldDraw draw)
        {
            return true;
        }

        // ----------------------------------------------------------------
        // RefreshTransients — czyści stare i rysuje nowe linie podglądu
        // ----------------------------------------------------------------

        private void RefreshTransients()
        {
            ClearTransients();

            double dx = _pt2.X - _pt1.X;
            double dy = _pt2.Y - _pt1.Y;
            _edgeHorizontal = Math.Abs(dx) >= Math.Abs(dy);

            var tm    = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();

            if (_edgeHorizontal)
            {
                double edgeY   = (_pt1.Y + _pt2.Y) / 2.0;
                double x0      = Math.Min(_pt1.X, _pt2.X) + PreviewCover;
                double x1Bound = Math.Max(_pt1.X, _pt2.X) - PreviewCover;
                double yStart  = _flipped ? edgeY - _barLength : edgeY;
                double yEnd    = _flipped ? edgeY              : edgeY + _barLength;

                if (x0 >= x1Bound) return;

                for (double x = x0; x <= x1Bound + 0.5; x += PreviewSpacing)
                {
                    double xc = Math.Min(x, x1Bound);
                    AddTransientLine(tm, vpIds,
                        new Point3d(xc, yStart, 0),
                        new Point3d(xc, yEnd,   0));
                    if (xc >= x1Bound) break;
                }
            }
            else
            {
                double edgeX   = (_pt1.X + _pt2.X) / 2.0;
                double y0      = Math.Min(_pt1.Y, _pt2.Y) + PreviewCover;
                double y1Bound = Math.Max(_pt1.Y, _pt2.Y) - PreviewCover;
                double xStart  = _flipped ? edgeX - _barLength : edgeX;
                double xEnd    = _flipped ? edgeX              : edgeX + _barLength;

                if (y0 >= y1Bound) return;

                for (double y = y0; y <= y1Bound + 0.5; y += PreviewSpacing)
                {
                    double yc = Math.Min(y, y1Bound);
                    AddTransientLine(tm, vpIds,
                        new Point3d(xStart, yc, 0),
                        new Point3d(xEnd,   yc, 0));
                    if (yc >= y1Bound) break;
                }
            }

            // Wymuś odświeżenie ekranu — transienty muszą być widoczne natychmiast
            try { Application.UpdateScreen(); } catch { }
        }

        private void AddTransientLine(TransientManager tm, IntegerCollection vpIds,
                                      Point3d p1, Point3d p2)
        {
            var line = new Line(p1, p2) { ColorIndex = 5 };
            try
            {
                tm.AddTransient(line, TransientDrawingMode.DirectTopmost, 128, vpIds);
                _transients.Add(line);
            }
            catch
            {
                line.Dispose();
            }
        }

        /// <summary>Usuwa wszystkie transienty. Wywołać zawsze po ed.Drag(jig).</summary>
        public void ClearTransients()
        {
            if (_transients.Count == 0) return;
            var tm    = TransientManager.CurrentTransientManager;
            var vpIds = new IntegerCollection();
            foreach (var line in _transients)
            {
                try { tm.EraseTransient(line, vpIds); line.Dispose(); }
                catch { }
            }
            _transients.Clear();
        }
    }
}
