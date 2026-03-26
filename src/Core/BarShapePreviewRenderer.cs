using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Renderuje dynamiczny podgląd kształtu pręta BS8666 jako WPF Canvas,
    /// korzystając z geometrii BarGeometryBuilder.
    /// Fallback do SVG jeśli kształt nie jest zaimplementowany w BarGeometryBuilder.
    /// </summary>
    public static class BarShapePreviewRenderer
    {
        // Shape codes renderowane jako Polygon (IsClosed) zamiast Polyline
        private static readonly HashSet<string> _closedCodes =
            new HashSet<string>(StringComparer.Ordinal)
            { "34", "35", "36", "41", "44", "46", "47" };

        // Domyślne parametry podglądu indeksowane liczbą parametrów (0–5)
        private static readonly double[][] _defaultSample =
        {
            new double[0],
            new[] { 100.0 },
            new[] { 100.0, 60.0 },
            new[] { 100.0, 60.0, 100.0 },
            new[] { 100.0, 60.0, 100.0, 60.0 },
            new[] { 100.0, 60.0, 100.0, 60.0, 40.0 },
        };

        // Nadpisania dla kształtów, których BarGeometryBuilder oczekuje
        // innych parametrów niż ShapeCodeLibrary (np. spirala)
        private static readonly Dictionary<string, double[]> _sampleOverrides =
            new Dictionary<string, double[]>(StringComparer.Ordinal)
            {
                // 75: BarGeometryBuilder używa A=diam, B=nTurns, C=pitch
                // (ShapeCodeLibrary ma ["A","B"] gdzie B=n×P, ale geometria nie zmieniała się)
                { "75", new[] { 100.0, 3.0, 30.0 } }
            };

        /// <summary>Średnica używana wyłącznie do obliczeń geometrii w podglądzie.</summary>
        public const double PreviewDiameter = 10.0;

        private const double Margin = 8.0;

        private static readonly Brush _stroke =
            new SolidColorBrush(Color.FromRgb(0x29, 0xB6, 0xF6));  // #29B6F6

        // ── API publiczne ─────────────────────────────────────────────────────

        /// <summary>
        /// Renderuje kształt z domyślnymi parametrami próbkowania.
        /// Jeśli kształt nie jest obsługiwany przez BarGeometryBuilder, zwraca null
        /// (caller powinien użyć SVG fallback).
        /// </summary>
        public static Canvas RenderDefault(BarShape barShape, double width, double height)
        {
            double[] sample = _sampleOverrides.TryGetValue(barShape.Code, out double[] ov)
                ? ov
                : _defaultSample[Math.Min(barShape.Parameters.Length, _defaultSample.Length - 1)];

            return Render(barShape, sample, PreviewDiameter, width, height);
        }

        /// <summary>
        /// Renderuje kształt na WPF Canvas o podanych wymiarach.
        /// Punkty skalowane i centrowane z marginesem 8px.
        /// </summary>
        public static Canvas Render(BarShape barShape, double[] sampleParams, double diameter,
                                    double width, double height)
        {
            var canvas = new Canvas { Width = width, Height = height };

            try
            {
                var pts = BarGeometryBuilder.GetLocalPoints(barShape.Code, sampleParams, diameter);
                if (pts == null || pts.Count < 2) return canvas;

                // ── Bounding box ──────────────────────────────────────────────
                double minX = double.MaxValue, minY = double.MaxValue;
                double maxX = double.MinValue, maxY = double.MinValue;
                foreach (var p in pts)
                {
                    if (p.X < minX) minX = p.X;
                    if (p.X > maxX) maxX = p.X;
                    if (p.Y < minY) minY = p.Y;
                    if (p.Y > maxY) maxY = p.Y;
                }

                // ── Skalowanie uniformne z centrowaniem ───────────────────────
                double drawW  = width  - 2.0 * Margin;
                double drawH  = height - 2.0 * Margin;
                double bboxW  = maxX - minX;
                double bboxH  = maxY - minY;
                double scaleX = bboxW > 1e-9 ? drawW / bboxW : 1.0;
                double scaleY = bboxH > 1e-9 ? drawH / bboxH : 1.0;
                double scale  = Math.Min(scaleX, scaleY);

                double offsetX = Margin + (drawW - bboxW * scale) * 0.5;
                double offsetY = Margin + (drawH - bboxH * scale) * 0.5;

                // ── Konwersja do współrzędnych WPF (oś Y odwrócona) ───────────
                var wpfPts = new PointCollection(pts.Count);
                foreach (var p in pts)
                    wpfPts.Add(new System.Windows.Point(
                        offsetX + (p.X - minX) * scale,
                        offsetY + (maxY - p.Y) * scale));

                // ── Kształt zamknięty → Polygon; otwarty → Polyline ───────────
                Shape rendered;
                if (_closedCodes.Contains(barShape.Code))
                {
                    var pg = new Polygon { Points = wpfPts, Fill = Brushes.Transparent };
                    // Usuń duplikat punktu zamknięcia (last == first po aproksymacji łuków)
                    int cnt = pg.Points.Count;
                    if (cnt > 1)
                    {
                        System.Windows.Point first = pg.Points[0];
                        System.Windows.Point last  = pg.Points[cnt - 1];
                        if (Math.Abs(first.X - last.X) < 0.5 && Math.Abs(first.Y - last.Y) < 0.5)
                            pg.Points.RemoveAt(cnt - 1);
                    }
                    rendered = pg;
                }
                else
                {
                    rendered = new Polyline { Points = wpfPts, Fill = Brushes.Transparent };
                }

                rendered.Stroke             = _stroke;
                rendered.StrokeThickness    = 1.5;
                rendered.StrokeLineJoin     = PenLineJoin.Round;
                rendered.StrokeStartLineCap = PenLineCap.Round;
                rendered.StrokeEndLineCap   = PenLineCap.Round;

                canvas.Children.Add(rendered);
            }
            catch { /* niepoprawna geometria → pusty canvas */ }

            return canvas;
        }
    }
}
