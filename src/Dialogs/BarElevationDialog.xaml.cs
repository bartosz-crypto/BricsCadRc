using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using BricsCadRc.Core;

namespace BricsCadRc.Dialogs
{
    /// <summary>
    /// Dialog "Reinforcement – Elevation" (FLOW 1, krok 1).
    /// Uzytkownik wybiera srednice, kod ksztaltu i wymiary preta.
    /// </summary>
    public partial class BarElevationDialog : Window
    {
        public BarData Result { get; private set; }
        public string ResultPosNr { get; private set; } = "01";

        private BarShape _selectedShape;
        private bool     _lengthOverridden;

        // Tablice RowA..RowE i LabelA..LabelE inicjalizowane po InitializeComponent
        private Grid[]      _paramRows;
        private TextBlock[] _paramLabels;
        private TextBox[]   _paramBoxes;

        public BarElevationDialog(int suggestedNr = 1)
        {
            InitializeComponent();

            _paramRows   = new[] { RowA, RowB, RowC, RowD, RowE };
            _paramLabels = new[] { LabelA, LabelB, LabelC, LabelD, LabelE };
            _paramBoxes  = new[] { ParamABox, ParamBBox, ParamCBox, ParamDBox, ParamEBox };

            PosNrBox.Text = suggestedNr.ToString("D2");

            // Domyślny kształt: 00 Straight
            SelectShape(ShapeCodeLibrary.Get("00"), paramValues: null);
        }

        // ── Wypełnienie dialogu istniejącymi danymi (RC_EDIT_BAR) ───────────

        public void LoadExisting(BarData bar)
        {
            // Ustaw średnicę
            foreach (ComboBoxItem item in DiameterCombo.Items)
            {
                if (item.Tag?.ToString() == bar.Diameter.ToString())
                { DiameterCombo.SelectedItem = item; break; }
            }

            // Ustaw posNr z Mark
            var seg = bar.Mark.Split('-');
            PosNrBox.Text = seg.Length >= 2 ? seg[1] : "01";

            // Ustaw shape + wypełnij pola parametrów (SelectShape obsługuje wszystko)
            var shape = ShapeCodeLibrary.Get(bar.ShapeCode);
            double[] pvals = { bar.LengthA, bar.LengthB, bar.LengthC, bar.LengthD, bar.LengthE };
            SelectShape(shape ?? ShapeCodeLibrary.Get("00"), pvals);

            // Override długości — SelectShape resetuje flagę, więc ustawiamy po nim
            if (bar.LengthOverridden)
            {
                OverrideCheck.IsChecked   = true;
                _lengthOverridden         = true;
                TotalLengthBox.IsReadOnly = false;
                TotalLengthBox.Background = System.Windows.Media.Brushes.White;
                TotalLengthBox.Text       = bar.TotalLength.ToString("F0", CultureInfo.InvariantCulture);
            }

            // Ustaw warstwę
            foreach (ComboBoxItem item in LayerCombo.Items)
            {
                if (item.Tag?.ToString() == bar.LayerCode)
                { LayerCombo.SelectedItem = item; break; }
            }
        }

        // ── Wybór kształtu przez ShapePickerDialog ───────────────────────────

        private void ShapeBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new ShapePickerDialog();
            if (dlg.ShowDialog() != true) return;

            // Synchronizuj Diameter z wybranym w ShapePickerDialog
            int pickedDia = (int)dlg.Diameter;
            foreach (ComboBoxItem item in DiameterCombo.Items)
            {
                if (item.Tag is string tag && int.TryParse(tag, out int d) && d == pickedDia)
                {
                    DiameterCombo.SelectedItem = item;
                    break;
                }
            }

            SelectShape(dlg.SelectedShape, dlg.ParameterValues);
        }

        private void SelectShape(BarShape shape, double[] paramValues)
        {
            if (shape == null) return;
            _selectedShape = shape;

            ShapeBtn.Content = $"[{shape.Code}] {shape.Name} ▼";

            UpdateParamRows(shape);

            // Pre-wypełnij pola jeśli mamy wartości (np. z ShapePickerDialog)
            if (paramValues != null)
            {
                for (int i = 0; i < _paramBoxes.Length && i < paramValues.Length; i++)
                    _paramBoxes[i].Text = paramValues[i].ToString("F0", CultureInfo.InvariantCulture);
            }

            // Reset override i przelicz długość
            OverrideCheck.IsChecked = false;
            _lengthOverridden       = false;
            TotalLengthBox.IsReadOnly = true;
            TotalLengthBox.Background = System.Windows.Media.Brushes.WhiteSmoke;

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

        private void ParamBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateLength();

        private void DiameterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateLength();

        private void TotalLengthBox_TextChanged(object sender, TextChangedEventArgs e) { /* handled by override check */ }

        private void OverrideCheck_Changed(object sender, RoutedEventArgs e)
        {
            _lengthOverridden         = OverrideCheck.IsChecked == true;
            TotalLengthBox.IsReadOnly = !_lengthOverridden;
            TotalLengthBox.Background = _lengthOverridden
                ? System.Windows.Media.Brushes.White
                : System.Windows.Media.Brushes.WhiteSmoke;

            if (!_lengthOverridden)
                UpdateLength();
        }

        private void UpdateLength()
        {
            if (_lengthOverridden || _selectedShape == null || _paramBoxes == null) return;

            int paramCount = _selectedShape.Parameters.Length;
            var values     = new double[paramCount];

            for (int i = 0; i < paramCount; i++)
            {
                string txt = _paramBoxes[i].Text ?? "";
                if (!double.TryParse(txt, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double v) || v <= 0)
                {
                    TotalLengthBox.Text = "";
                    return;
                }
                values[i] = v;
            }

            try
            {
                double len = _selectedShape.CalculateTotalLength(values, GetDiameter());
                TotalLengthBox.Text = len.ToString("F0", CultureInfo.InvariantCulture);
            }
            catch
            {
                TotalLengthBox.Text = "";
            }
        }

        private int GetDiameter()
        {
            if (DiameterCombo.SelectedItem is ComboBoxItem item
                && int.TryParse((string)item.Tag, out int d))
                return d;
            return 12;
        }

        // ── OK / Cancel ──────────────────────────────────────────────────────

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedShape == null)
            {
                MessageBox.Show("Select a shape code.", "RC BAR",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!int.TryParse(PosNrBox.Text, out int pnr) || pnr < 1 || pnr > 99)
            { MessageBox.Show("Numer pozycji musi być liczbą 1-99."); return; }
            ResultPosNr = pnr.ToString("D2");

            int paramCount = _selectedShape.Parameters.Length;
            var paramVals  = new double[5]; // A..E

            for (int i = 0; i < paramCount; i++)
            {
                if (!double.TryParse(_paramBoxes[i].Text, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double v) || v <= 0)
                {
                    MessageBox.Show($"Invalid value for {_selectedShape.Parameters[i]}.", "RC BAR",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                paramVals[i] = v;
            }

            if (!double.TryParse(TotalLengthBox.Text, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double totalLen) || totalLen <= 0)
            {
                MessageBox.Show("Total length could not be calculated. Fill in all parameters.", "RC BAR",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string layerCode = (string)((ComboBoxItem)LayerCombo.SelectedItem).Tag;

            Result = new BarData
            {
                Diameter         = GetDiameter(),
                ShapeCode        = _selectedShape.Code,
                LengthA          = paramVals[0],
                LengthB          = paramVals[1],
                LengthC          = paramVals[2],
                LengthD          = paramVals[3],
                LengthE          = paramVals[4],
                TotalLength      = totalLen,
                LengthOverridden = _lengthOverridden,
                LayerCode        = layerCode,
                Position         = layerCode == "BOT" ? "BOT" : "TOP",
                Direction        = "X"
            };

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
