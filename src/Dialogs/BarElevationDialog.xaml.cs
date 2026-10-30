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
        private bool     _userTouchedLayer = false;
        private bool     _isAutoUpdating   = false;

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

            // Auto-layer na podstawie suggestedNr (przed podpięciem eventów)
            SetAutoLayer(suggestedNr);

            PosNrBox.TextChanged          += PosNrBox_TextChanged;
            LayerCombo.SelectionChanged   += LayerCombo_SelectionChanged;
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

            // Końcówki „do środka” (2026-10-30)
            TicksCheck.IsChecked = bar.EndTicks;
            foreach (ComboBoxItem item in TickSideCombo.Items)
                if (item.Tag?.ToString() == (bar.EndTickSide >= 0 ? "1" : "-1")) { TickSideCombo.SelectedItem = item; break; }
            UpdateTickControls();

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
            UpdateAngleRow(shape);
            UpdateTickControls();

            // Pre-wypełnij pola jeśli mamy wartości (np. z ShapePickerDialog). W4d: wiersz r ↔ pole Slot(r) (alfabetycznie)
            if (paramValues != null)
            {
                for (int i = 0; i < _paramBoxes.Length; i++)
                {
                    int slot = Slot(i);
                    if (slot < paramValues.Length)
                        _paramBoxes[i].Text = paramValues[slot].ToString("F0", CultureInfo.InvariantCulture);
                }
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
                    _paramLabels[i].Text = $"{shape.Parameters[shape.DisplayOrder[i]]} [mm]:";
            }
        }

        /// <summary>
        /// W4d: wiersz dialogu → indeks parametru kształtu (= pole XData LengthA…E). Wiersze alfabetycznie
        /// (BarShape.DisplayOrder, np. 46 {A,B,C,E,D}: wiersz D → pole 4); wiersze ukryte — bez zmian (wiersz = pole).
        /// </summary>
        private int Slot(int row)
            => _selectedShape != null && row < _selectedShape.Parameters.Length ? _selectedShape.DisplayOrder[row] : row;

        // ── Live obliczanie długości ─────────────────────────────────────────

        private void ParamBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            AngleAfterParamChanged(sender);
            UpdateLength();
        }

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
            UpdateWarnings();
            if (_lengthOverridden || _selectedShape == null || _paramBoxes == null) return;

            int paramCount = _selectedShape.Parameters.Length;
            var values     = new double[paramCount];

            for (int row = 0; row < paramCount; row++)
            {
                int i = Slot(row);   // W4d: wiersz → pole
                string txt = _paramBoxes[row].Text ?? "";
                // Plan W4 (0-POPRAWKI p.5): wymiar opcjonalny (C dla 51/63, wymiary kąta W4d) — puste / 0 = wartość domyślna
                if (_selectedShape.IsOptionalParam(i) && string.IsNullOrWhiteSpace(txt)) { values[i] = 0; continue; }
                if (!NumberParser.TryParseDouble(txt, out double v) || v < 0 || (v == 0 && !_selectedShape.IsOptionalParam(i)))
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
            catch (System.Exception ex)
            {
                // Np. 47 bez C, 64 (wymaga F) — brak długości; użytkownik może ją nadpisać
                Log.Error("BarElevationDialog.UpdateLength", ex);
                TotalLengthBox.Text = "";
            }
        }

        /// <summary>
        /// Plan W4f: ostrzeżenia BS 8666:2020 (BarShapeValidation.Check) pod wymiarami — tylko informacja, OK nie blokowane.
        /// Pole puste = 0 (wymiar opcjonalny: wartość domyślna); wartość nieczytelna → brak ostrzeżeń (komunikat da OK).
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
            WarningText.Text = "Uwagi BS 8666:2020 (nie blokują zapisu):" + System.Environment.NewLine + "• "
                             + string.Join(System.Environment.NewLine + "• ", warnings);
            WarningText.Visibility = Visibility.Visible;
        }

        private int GetDiameter()
        {
            if (DiameterCombo.SelectedItem is ComboBoxItem item
                && int.TryParse((string)item.Tag, out int d))
                return d;
            return 12;
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

        // ── Auto-layer na podstawie posNr ───────────────────────────────────

        private void PosNrBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_userTouchedLayer) return;
            if (!int.TryParse(PosNrBox.Text, out int nr) || nr < 1) return;
            SetAutoLayer(nr);
        }

        private void LayerCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isAutoUpdating) return;
            _userTouchedLayer = true;
        }

        private void SetAutoLayer(int posNr)
        {
            string targetTag = posNr >= 100 ? "TOP" : "BOT";
            _isAutoUpdating = true;
            foreach (ComboBoxItem item in LayerCombo.Items)
            {
                if (item.Tag?.ToString() == targetTag)
                { LayerCombo.SelectedItem = item; break; }
            }
            _isAutoUpdating = false;
        }

        // ── Końcówki „do środka” (2026-10-30) ───────────────────────────────

        private void TicksCheck_Changed(object sender, RoutedEventArgs e) => UpdateTickControls();

        /// <summary>Pierścień / strzemię zamknięte — bez końcówek; strona tylko dla pręta prostego z włączonymi końcówkami.</summary>
        private void UpdateTickControls()
        {
            if (TicksCheck == null || TickSideCombo == null) return;
            string code = _selectedShape?.Code ?? "00";
            bool supported = BarTicks.Supports(code);
            TicksCheck.IsEnabled = supported;
            TickSideCombo.Visibility = SingleBarEngine.IsStraightShape(code) ? Visibility.Visible : Visibility.Collapsed;
            TickSideCombo.IsEnabled = supported && TicksCheck.IsChecked == true;
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

            if (!int.TryParse(PosNrBox.Text, out int pnr) || pnr < 1)
            { MessageBox.Show("Numer pozycji musi być >= 1."); return; }
            ResultPosNr = pnr.ToString("D2");

            int paramCount = _selectedShape.Parameters.Length;
            var paramVals  = new double[5]; // A..E

            for (int row = 0; row < paramCount; row++)
            {
                int i = Slot(row);   // W4d: wiersz → pole
                var box = _paramBoxes[row];
                // Plan W4 (0-POPRAWKI p.5): wymiar opcjonalny (C dla 51/63, wymiary kąta W4d) — puste / 0 = wartość domyślna
                if (_selectedShape.IsOptionalParam(i) && string.IsNullOrWhiteSpace(box.Text)) { paramVals[i] = 0; continue; }
                // Plan W4 (runda 1, uwaga 3): wymiar potrzebny tylko formule (47: C) — brak dozwolony przy nadpisanej
                // długości (stary pręt 47 bez C); bez nadpisania — komunikat o braku długości.
                if (_selectedShape.IsRequiredOnlyByFormula(i)
                    && (string.IsNullOrWhiteSpace(box.Text)
                        || (NumberParser.TryParseDouble(box.Text, out double vf) && vf == 0)))
                {
                    if (_lengthOverridden) { paramVals[i] = 0; continue; }
                    MessageBox.Show($"Kształt {_selectedShape.Code} wymaga wymiaru {_selectedShape.Parameters[i]} (BS 8666:2020) — "
                                    + "brak długości. Podaj wymiar albo nadpisz długość całkowitą.", "RC BAR",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!NumberParser.TryParseDouble(box.Text, out double v) || v < 0
                    || (v == 0 && !_selectedShape.IsOptionalParam(i)))
                {
                    MessageBox.Show($"Invalid value for {_selectedShape.Parameters[i]}.", "RC BAR",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                paramVals[i] = v;
            }

            if (!NumberParser.TryParseDouble(TotalLengthBox.Text, out double totalLen) || totalLen <= 0)
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
                Direction        = "X",
                // końcówki „do środka” (2026-10-30)
                EndTicks         = TicksCheck.IsChecked == true && BarTicks.Supports(_selectedShape.Code),
                EndTickSide      = (TickSideCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "1" ? 1 : -1
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
