using System.Collections.Generic;
using Bricscad.ApplicationServices;
using Bricscad.EditorInput;
using Teigha.DatabaseServices;
using Teigha.Geometry;
using Teigha.GraphicsInterface;
using Polyline = Teigha.DatabaseServices.Polyline;
using BricsCadRc.Core;

namespace BricsCadRc.Commands
{
    /// <summary>
    /// Podgląd wstawiania map (jak ImrInsertionJig w AsdRcSlab): prostokąty ramek T1/T2/B1/B2 idą za kursorem,
    /// kursor = lewy górny róg ramki T1. Rysowanie przez TransientManager (jak DistributionJig).
    /// Użycie: var jig = new MapInsertJig(frames, refPt); var r = ed.Drag(jig); jig.ClearTransients();
    /// </summary>
    internal class MapInsertJig : DrawJig
    {
        private readonly Point3d _ref;
        private readonly List<Polyline> _rects = new List<Polyline>();
        private Point3d _cur;
        private bool _added;

        public Point3d InsertPoint => _cur;

        public MapInsertJig(IEnumerable<MapImportEngine.MapFrame> frames, Point3d refPt)
        {
            _ref = refPt;
            _cur = refPt;
            foreach (var f in frames)
            {
                var pl = new Polyline();
                pl.AddVertexAt(0, new Point2d(f.Frame.MinPoint.X, f.Frame.MinPoint.Y), 0, 0, 0);
                pl.AddVertexAt(1, new Point2d(f.Frame.MaxPoint.X, f.Frame.MinPoint.Y), 0, 0, 0);
                pl.AddVertexAt(2, new Point2d(f.Frame.MaxPoint.X, f.Frame.MaxPoint.Y), 0, 0, 0);
                pl.AddVertexAt(3, new Point2d(f.Frame.MinPoint.X, f.Frame.MaxPoint.Y), 0, 0, 0);
                pl.Closed = true;
                pl.ColorIndex = 7;
                _rects.Add(pl);
            }
        }

        protected override SamplerStatus Sampler(JigPrompts prompts)
        {
            var opts = new JigPromptPointOptions("\n[RC MAPY] Punkt wstawienia (lewy górny róg mapy T1): ");
            var res = prompts.AcquirePoint(opts);
            if (res.Status != PromptStatus.OK) return SamplerStatus.NoChange;
            if (_added && _cur.IsEqualTo(res.Value, Tolerance.Global)) return SamplerStatus.NoChange;
            var delta = res.Value - (_added ? _cur : _ref);
            _cur = res.Value;
            Refresh(Matrix3d.Displacement(delta));
            return SamplerStatus.OK;
        }

        protected override bool WorldDraw(WorldDraw draw) => true;

        private void Refresh(Matrix3d move)
        {
            var tm = TransientManager.CurrentTransientManager;
            var vp = new IntegerCollection();
            foreach (var pl in _rects)
            {
                pl.TransformBy(move);
                try
                {
                    if (_added) tm.UpdateTransient(pl, vp);
                    else tm.AddTransient(pl, TransientDrawingMode.DirectTopmost, 128, vp);
                }
                catch (System.Exception ex) { Log.Error("MapInsertJig.Refresh", ex); }
            }
            _added = true;
            try { Application.UpdateScreen(); }
            catch (System.Exception ex) { Log.Error("MapInsertJig.UpdateScreen", ex); }
        }

        /// <summary>Usuwa podgląd. Wywołać zawsze po ed.Drag(jig).</summary>
        public void ClearTransients()
        {
            var tm = TransientManager.CurrentTransientManager;
            var vp = new IntegerCollection();
            foreach (var pl in _rects)
            {
                try { if (_added) tm.EraseTransient(pl, vp); }
                catch (System.Exception ex) { Log.Error("MapInsertJig.Clear", ex); }
                pl.Dispose();
            }
            _rects.Clear();
            _added = false;
        }
    }
}
