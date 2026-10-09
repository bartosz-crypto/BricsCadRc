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
            foreach (var shape in ShapeCodeLibrary.GetPickable())
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
            UpdateAngleRow(shape);
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
                    _paramLabels[i].Text = $"{shape.Parameters[shape.DisplayOrder[i]]} [mm]:";
            }
        }

        /// <summary>
        /// W4d: wiersz → indeks parametru kształtu (BarShape.DisplayOrder — wiersze alfabetycznie, np. 46 {A,B,C,E,D}:
        /// wiersz D → pole 4). ParameterValues zostają w kolejności Parameters (pola XData).
        /// </summary>
        private int Slot(int row)
            => _selectedShape != null && row < _selectedShape.Parameters.Length ? _selectedShape.DisplayOrder[row] : row;

        // ── Live obliczanie długości ─────────────────────────────────────────
        private void ParamBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender == ParamC) UpdatePlaceholderC();
            AngleAfterParamChanged(sender);
            UpdateLength();
        }

        private void DiameterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateLength();

        /// <summary>Parametr opcjonalny (BarShape.IsOptionalParam — W4b: C dla 51 i 63).</summary>
        private bool IsOptionalParam(int paramIndex) =>
            _selectedShape != null && _selectedShape.IsOptionalParam(paramIndex);

        /// <summary>Pokazuje/ukrywa placeholder w polu C dla shape codes 51 i 63.</summary>
        private void UpdatePlaceholderC()
        {
            if (PlaceholderC == null || _selectedShape == null) return;

            if (IsOptionalParam(Slot(2)) && _selectedShape.Parameters[Slot(2)] == "C")
            {
                // W4b: wariant minimalny wg BS 8666:2020
                PlaceholderC.Text = _selectedShape.Code == "51"
                    ? "domyślnie min. BS 8666 (16d, 160)"
                    : "domyślnie min. BS 8666 (14d / 13d)";
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
            UpdateWarnings();
            if (_selectedShape == null || TotalLengthLabel == null) return;

            double diameter   = GetDiameter();
            int    paramCount = _selectedShape.Parameters.Length;
            var    values     = new double[paramCount];

            for (int row = 0; row < paramCount; row++)
            {
                int    i        = Slot(row);   // W4d: wiersz → pole
                string txt      = _paramBoxes[row].Text ?? "";
                bool   optional = IsOptionalParam(i);

                if (optional && string.IsNullOrWhiteSpace(txt))
                {
                    values[i] = 0;   // puste → wartość domyślna (C 51/63: wariant minimalny; wymiar kąta: kąt domyślny)
                    continue;
                }

                if (!NumberParser.TryParseDouble(txt, out double v)
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

        /// <summary>
        /// Plan W4f: ostrzeżenia BS 8666:2020 (BarShapeValidation.Check) pod długością — tylko informacja, OK nie blokowane
        /// (OK zależy wyłącznie od tego, czy da się policzyć długość). Pole puste = 0; wartość nieczytelna → brak ostrzeżeń.
        /// </summary>
        private void UpdateWarnings()
        {
            if (WarningText == null || _selectedShape == null || _paramBoxes == null) return;
            int paramCount = _selectedShape.Parameters.Length;
            var values = new double[paramCount];
            bool readable = true;
            for (int row = 0; row < paramCount && readable; row++)
            {
                int i = Slot(row);   // W4d: wiersz → pole
                string txt = _paramBoxes[row].Text ?? "";
                if (string.IsNullOrWhiteSpace(txt)) { values[i] = 0; continue; }
                if (NumberParser.TryParseDouble(txt, out double v)) values[i] = v;
                else readable = false;
            }
            var warnings = new System.Collections.Generic.List<string>(
                readable ? BarShapeValidation.Check(_selectedShape.Code, values, GetDiameter()) : new string[0]);
            // W4h runda 2: kąt spoza (0, 90] / nieczytelny — nie ignorujemy po cichu
            if (_angleInvalid) warnings.Add("Kąt: podaj wartość z zakresu 0–90° (pole kąta pominięte).");
            if (warnings == null || warnings.Count == 0)
            {
                WarningText.Text = "";
                WarningText.Visibility = Visibility.Collapsed;
                return;
            }
            WarningText.Text = "Uwagi BS 8666:2020 (nie blokują OK):" + Environment.NewLine + "• "
                             + string.Join(Environment.NewLine + "• ", warnings);
            WarningText.Visibility = Visibility.Visible;
        }

        private double GetDiameter()
        {
            if (DiameterCombo.SelectedItem is ComboBoxItem item
                && NumberParser.TryParseDouble((string)item.Tag, out double d))
                return d;
            return 12.0;
        }

        // ── W4h: kąt ramienia skośnego (14, 15, 24, 46) ─────────────────────

        private bool _settingFromAngle;
        private bool _angleInvalid;

        /// <summary>W4h runda 2: kąt nieczytelny / spoza (0, 90] — czerwona ramka pola i dopisek w uwagach.</summary>
        private void SetAngleInvalid(bool invalid)
        {
            _angleInvalid = invalid;
            if (AngleBox != null)
            {
                if (invalid) AngleBox.BorderBrush = System.Windows.Media.Brushes.Red;
                else AngleBox.ClearValue(Control.BorderBrushProperty);
            }
            UpdateWarnings();
        }

        /// <summary>
        /// W4h (decyzja użytkownika 2026-10-09): wpisany kąt [°] → wymiary kąta (BarShape.AngleToDimensions: 14 / 15 — B, D;
        /// 24 — D, E; 46 — D), zaokrąglone do 5 mm, wpisane w pola (zapis jak dotąd, bez zmiany XData). Puste / nieczytelne /
        /// spoza (0, 90] albo brak długości ramienia skośnego — bez zmian.
        /// </summary>
        private void AngleBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_settingFromAngle || AngleBox == null) return;
            string txt = AngleBox.Text ?? "";
            bool parsed = NumberParser.TryParseDouble(txt, out double deg);
            SetAngleInvalid(!string.IsNullOrWhiteSpace(txt) && (!parsed || deg <= 0 || deg > 90));
            if (_selectedShape == null || _paramBoxes == null || !_selectedShape.HasSlopeAngleInput) return;
            if (string.IsNullOrWhiteSpace(txt) || !parsed) return;
            int n = _selectedShape.Parameters.Length;
            var values = new double[n];
            for (int row = 0; row < n; row++)
            {
                NumberParser.TryParseDouble(_paramBoxes[row].Text ?? "", out double v);
                values[Slot(row)] = v;
            }
            var res = _selectedShape.AngleToDimensions(values, deg);
            if (res == null) return;
            _settingFromAngle = true;
            try
            {
                for (int row = 0; row < n; row++)
                {
                    int i = Slot(row);
                    if (_selectedShape.IsSlopeDimension(i) && System.Math.Abs(res[i] - values[i]) > 1e-9)
                        _paramBoxes[row].Text = res[i].ToString("F0", CultureInfo.InvariantCulture);
                }
            }
            finally { _settingFromAngle = false; }
        }

        /// <summary>
        /// W4h: zmiana wymiaru przez użytkownika — wymiar kąta wpisany ręcznie kasuje pole kąta (wymiary mają pierwszeństwo);
        /// zmiana długości ramienia skośnego przy wpisanym kącie przelicza wymiary kąta.
        /// </summary>
        private void AngleAfterParamChanged(object sender)
        {
            if (_settingFromAngle || AngleBox == null || _selectedShape == null || _paramBoxes == null
                || !_selectedShape.HasSlopeAngleInput || string.IsNullOrWhiteSpace(AngleBox.Text)) return;
            int row = System.Array.IndexOf(_paramBoxes, sender as TextBox);
            if (row < 0) return;
            if (_selectedShape.IsSlopeDimension(Slot(row)))
            {
                _settingFromAngle = true;
                try { AngleBox.Text = ""; }
                finally { _settingFromAngle = false; }
                SetAngleInvalid(false);
            }
            else AngleBox_TextChanged(AngleBox, null);
        }

        /// <summary>W4h: wiersz kąta tylko dla kształtów z ramieniem skośnym; przy zmianie kształtu — pusty.</summary>
        private void UpdateAngleRow(BarShape shape)
        {
            if (RowAngle == null || AngleBox == null) return;
            RowAngle.Visibility = shape != null && shape.HasSlopeAngleInput ? Visibility.Visible : Visibility.Collapsed;
            _settingFromAngle = true;
            try { AngleBox.Text = ""; }
            finally { _settingFromAngle = false; }
            SetAngleInvalid(false);
        }

        // ── Przyciski ────────────────────────────────────────────────────────
        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedShape == null) return;

            int    paramCount = _selectedShape.Parameters.Length;
            var    values     = new double[paramCount];

            for (int row = 0; row < paramCount; row++)
            {
                int    i        = Slot(row);   // W4d: wiersz → pole
                string txt      = _paramBoxes[row].Text ?? "";
                bool   optional = IsOptionalParam(i);

                if (optional && string.IsNullOrWhiteSpace(txt))
                {
                    values[i] = 0;
                    continue;
                }

                if (!NumberParser.TryParseDouble(txt, out double v)
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
