using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml.Linq;
using BricsCadRc.Core;

namespace BricsCadRc.Dialogs
{
    /// <summary>
    /// Dialog wyboru kształtu pręta BS8666 z podglądem SVG, polami parametrów
    /// i live obliczaniem długości całkowitej.
    /// </summary>
    public partial class ShapePickerDialog : Window
    {
        // ── Wynik dialogu ────────────────────────────────────────────────────
        public BarShape    SelectedShape   { get; private set; }
        public double[]    ParameterValues { get; private set; }
        public double      Diameter        { get; private set; }

        // ── Stan wewnętrzny ──────────────────────────────────────────────────
        private BarShape _selectedShape;
        private Border   _selectedBorder;

        private Grid[]      _paramRows;
        private TextBlock[] _paramLabels;
        private TextBox[]   _paramBoxes;

        // Kolor zaznaczenia (#1E5FA8)
        private static readonly SolidColorBrush BrushBlue =
            new SolidColorBrush(Color.FromRgb(0x1E, 0x5F, 0xA8));
        private static readonly SolidColorBrush BrushCellSelected =
            new SolidColorBrush(Color.FromRgb(0xE3, 0xEE, 0xF9));

        // ── Konstruktor ──────────────────────────────────────────────────────
        public ShapePickerDialog()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _paramRows   = new[] { RowA, RowB, RowC, RowD, RowE };
            _paramLabels = new[] { LabelA, LabelB, LabelC, LabelD, LabelE };
            _paramBoxes  = new[] { ParamA, ParamB, ParamC, ParamD, ParamE };

            BuildShapeGrid();

            // Zaznacz pierwszy kształt (00)
            if (ShapeGrid.Children.Count > 0)
                (ShapeGrid.Children[0] as Border)?.RaiseEvent(
                    new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    { RoutedEvent = MouseLeftButtonDownEvent });
        }

        // ── Budowanie gridu miniaturek ────────────────────────────────────────
        private void BuildShapeGrid()
        {
            foreach (var shape in ShapeCodeLibrary.GetAll())
                ShapeGrid.Children.Add(BuildCell(shape));
        }

        private Border BuildCell(BarShape shape)
        {
            var cell = new Border
            {
                BorderBrush     = Brushes.LightGray,
                BorderThickness = new Thickness(1),
                Margin          = new Thickness(2),
                Padding         = new Thickness(4, 4, 4, 4),
                Background      = Brushes.White,
                Cursor          = Cursors.Hand
            };

            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

            // Podgląd kształtu: dynamiczny Canvas lub SVG fallback
            stack.Children.Add(BuildShapePreview(shape));

            // Kod (pogrubiony)
            stack.Children.Add(new TextBlock
            {
                Text                = shape.Code,
                FontWeight          = FontWeights.Bold,
                FontSize            = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin              = new Thickness(0, 2, 0, 0)
            });

            // Nazwa
            stack.Children.Add(new TextBlock
            {
                Text                = shape.Name,
                FontSize            = 9,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextWrapping        = TextWrapping.NoWrap,
                TextTrimming        = TextTrimming.CharacterEllipsis,
                MaxWidth            = 95,
                Margin              = new Thickness(0)
            });

            cell.Child = stack;
            cell.MouseLeftButtonDown += (s, e) => SelectShape(shape, cell);
            return cell;
        }

        // ── Podgląd kształtu ─────────────────────────────────────────────────

        /// <summary>
        /// Zwraca podgląd kształtu: dynamiczny Canvas (BarShapePreviewRenderer)
        /// jeśli BarGeometryBuilder obsługuje kod, albo statyczny SVG jako fallback.
        /// </summary>
        private static FrameworkElement BuildShapePreview(BarShape shape)
        {
            if (BarGeometryBuilder.IsSupported(shape.Code))
                return BarShapePreviewRenderer.RenderDefault(shape, width: 54, height: 36);

            return SvgToVisual(shape.SvgPreview);
        }

        // ── Zaznaczanie kształtu ─────────────────────────────────────────────
        private void SelectShape(BarShape shape, Border cell)
        {
            if (_selectedBorder != null)
            {
                _selectedBorder.BorderBrush = Brushes.LightGray;
                _selectedBorder.Background  = Brushes.White;
            }

            _selectedBorder         = cell;
            cell.BorderBrush        = BrushBlue;
            cell.Background         = BrushCellSelected;
            _selectedShape          = shape;

            UpdateParamRows(shape);
            UpdatePlaceholderC();
            UpdateLength();
        }

        private void UpdateParamRows(BarShape shape)
        {
            for (int i = 0; i < _paramRows.Length; i++)
            {
                bool show = i < shape.Parameters.Length;
                _paramRows[i].Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show)
                    _paramLabels[i].Text = $"{shape.Parameters[i]} [mm]:";
            }
        }

        // ── Live obliczanie długości ─────────────────────────────────────────
        private void ParamBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender == ParamC) UpdatePlaceholderC();
            UpdateLength();
        }

        private void DiameterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateLength();

        /// <summary>Zwraca true dla opcjonalnego parametru C w shape codes 51 i 63.</summary>
        private bool IsOptionalParam(int paramIndex) =>
            paramIndex == 2 && (_selectedShape?.Code == "51" || _selectedShape?.Code == "63");

        /// <summary>Pokazuje/ukrywa placeholder w polu C dla shape codes 51 i 63.</summary>
        private void UpdatePlaceholderC()
        {
            if (PlaceholderC == null || _selectedShape == null) return;

            if (_selectedShape.Code == "51" || _selectedShape.Code == "63")
            {
                PlaceholderC.Text = _selectedShape.Code == "51"
                    ? "domyślnie MAX(16d,160)"
                    : "domyślnie MAX(14d,150)";
                PlaceholderC.Visibility = string.IsNullOrEmpty(ParamC.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            else
            {
                PlaceholderC.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateLength()
        {
            if (_selectedShape == null || TotalLengthLabel == null) return;

            double diameter   = GetDiameter();
            int    paramCount = _selectedShape.Parameters.Length;
            var    values     = new double[paramCount];

            for (int i = 0; i < paramCount; i++)
            {
                string txt      = _paramBoxes[i].Text ?? "";
                bool   optional = IsOptionalParam(i);

                if (optional && string.IsNullOrWhiteSpace(txt))
                {
                    values[i] = 0;   // C puste → użyj wartości domyślnej w formule
                    continue;
                }

                if (!double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                    || v < 0 || (!optional && v <= 0))
                {
                    TotalLengthLabel.Text = "Total length: –";
                    OkButton.IsEnabled    = false;
                    return;
                }
                values[i] = v;
            }

            try
            {
                double len = _selectedShape.CalculateTotalLength(values, diameter);
                TotalLengthLabel.Text = $"Total length: {len:F0} mm";
                OkButton.IsEnabled    = true;
            }
            catch
            {
                TotalLengthLabel.Text = "Total length: –";
                OkButton.IsEnabled    = false;
            }
        }

        private double GetDiameter()
        {
            if (DiameterCombo.SelectedItem is ComboBoxItem item
                && double.TryParse((string)item.Tag, out double d))
                return d;
            return 12.0;
        }

        // ── Przyciski ────────────────────────────────────────────────────────
        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedShape == null) return;

            int    paramCount = _selectedShape.Parameters.Length;
            var    values     = new double[paramCount];

            for (int i = 0; i < paramCount; i++)
            {
                string txt      = _paramBoxes[i].Text ?? "";
                bool   optional = IsOptionalParam(i);

                if (optional && string.IsNullOrWhiteSpace(txt))
                {
                    values[i] = 0;
                    continue;
                }

                if (!double.TryParse(txt, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                    || v < 0 || (!optional && v <= 0))
                    return;

                values[i] = v;
            }

            SelectedShape   = _selectedShape;
            ParameterValues = values;
            Diameter        = GetDiameter();

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ── Renderer SVG → WPF Canvas ────────────────────────────────────────
        //
        // Obsługuje elementy używane w ShapeCodeLibrary.SvgPreview:
        //   <line>, <polyline>, <polygon>, <rect>, <circle>, <path>
        // SVG viewBox=60×40, skalowanie przez Viewbox do 54×36.
        // ─────────────────────────────────────────────────────────────────────
        private static FrameworkElement SvgToVisual(string svgStr)
        {
            var canvas = new Canvas { Width = 60, Height = 40 };

            try
            {
                var doc = XDocument.Parse(svgStr);
                var root = doc.Root;
                if (root == null) return Wrap(canvas);

                var defaultStroke = new SolidColorBrush(Colors.Black);

                foreach (var el in root.Elements())
                {
                    Shape shape = null;
                    string tag  = el.Name.LocalName.ToLower();

                    switch (tag)
                    {
                        case "line":
                            shape = MakeLine(el);
                            break;
                        case "polyline":
                            shape = MakePolyOrPolygon(el, closed: false);
                            break;
                        case "polygon":
                            shape = MakePolyOrPolygon(el, closed: true);
                            break;
                        case "rect":
                            shape = MakeRect(el);
                            break;
                        case "circle":
                            shape = MakeCircle(el);
                            break;
                        case "path":
                            shape = MakePath(el);
                            break;
                    }

                    if (shape == null) continue;

                    shape.Stroke          = defaultStroke;
                    shape.StrokeThickness = 2.0;
                    shape.Fill            = Brushes.Transparent;

                    string dashAttr = Attr(el, "stroke-dasharray");
                    if (!string.IsNullOrEmpty(dashAttr))
                        shape.StrokeDashArray = ParseDashArray(dashAttr);

                    canvas.Children.Add(shape);
                }
            }
            catch { /* niepoprawny SVG – pusty canvas */ }

            return Wrap(canvas);
        }

        private static FrameworkElement Wrap(Canvas c) =>
            new Viewbox { Width = 54, Height = 36, Stretch = Stretch.Fill, Child = c };

        private static Line MakeLine(XElement el) =>
            new Line { X1 = Dbl(el, "x1"), Y1 = Dbl(el, "y1"),
                       X2 = Dbl(el, "x2"), Y2 = Dbl(el, "y2") };

        private static Shape MakePolyOrPolygon(XElement el, bool closed)
        {
            var pts = ParsePoints(Attr(el, "points"));
            if (closed)
            {
                var pg = new Polygon();
                foreach (var p in pts) pg.Points.Add(p);
                return pg;
            }
            var pl = new Polyline();
            foreach (var p in pts) pl.Points.Add(p);
            return pl;
        }

        private static Shape MakeRect(XElement el)
        {
            var r = new Rectangle
            {
                Width  = Dbl(el, "width"),
                Height = Dbl(el, "height")
            };
            Canvas.SetLeft(r, Dbl(el, "x"));
            Canvas.SetTop (r, Dbl(el, "y"));
            return r;
        }

        private static Shape MakeCircle(XElement el)
        {
            double radius = Dbl(el, "r");
            var e = new Ellipse { Width = 2 * radius, Height = 2 * radius };
            Canvas.SetLeft(e, Dbl(el, "cx") - radius);
            Canvas.SetTop (e, Dbl(el, "cy") - radius);
            return e;
        }

        private static Shape MakePath(XElement el)
        {
            string d = Attr(el, "d");
            if (string.IsNullOrEmpty(d)) return null;
            try
            {
                return new System.Windows.Shapes.Path { Data = Geometry.Parse(d) };
            }
            catch { return null; }
        }

        // ── Parsowanie pomocnicze ────────────────────────────────────────────
        private static string Attr(XElement el, string name) =>
            (string)el.Attribute(name) ?? "";

        private static double Dbl(XElement el, string name)
        {
            string v = Attr(el, name);
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
        }

        private static Point[] ParsePoints(string pointsStr)
        {
            var tokens = pointsStr.Trim().Split(
                new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);

            int count = tokens.Length / 2;
            var pts   = new Point[count];

            for (int i = 0; i < count; i++)
            {
                double.TryParse(tokens[i * 2],     NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double x);
                double.TryParse(tokens[i * 2 + 1], NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double y);
                pts[i] = new Point(x, y);
            }
            return pts;
        }

        private static DoubleCollection ParseDashArray(string dashStr)
        {
            var dc = new DoubleCollection();
            foreach (var token in dashStr.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    dc.Add(v);
            return dc;
        }
    }
}
