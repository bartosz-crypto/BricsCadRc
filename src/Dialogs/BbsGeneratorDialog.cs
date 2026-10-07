using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using BricsCadRc.Core;

namespace BricsCadRc.Dialogs
{
    /// <summary>
    /// RC_BBS — podgląd pozycji z rysunku (z ostrzeżeniami) + dane arkusza: przypisanie layoutów BOTTOM/TOP,
    /// nagłówek, akcesoria, szablon. Jak BbsGeneratorDialog z AsdRcSlab, z podglądem przed zapisem.
    /// </summary>
    public class BbsGeneratorDialog : Window
    {
        public BbsGenerationContext Result { get; private set; }
        public string OutputPath   { get; private set; }
        public string TemplatePath { get; private set; }

        private readonly List<BbsLayoutAssignment> _assignments;
        private readonly List<BbsBarRow> _rows;
        private readonly DataGrid _layoutsGrid;
        private readonly TabControl _tabs;
        private readonly TextBox _output, _contract, _addr1, _addr2, _addr3, _rev, _plot, _template;
        private readonly TextBox _tricQty, _hysQty;
        private readonly ComboBox _tricType, _hysType;
        private readonly TextBlock _tricPreview, _hysPreview;

        public BbsGeneratorDialog(BbsGenerationContext initial, List<BbsBarRow> rows, List<string> warnings,
                                  string suggestedOutput, string templatePath, string accessoryHint)
        {
            Title = "Generuj BBS (BS 8666)";
            Width = 1000; Height = 780; MinWidth = 760; MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            _assignments = initial.Assignments;
            _rows = rows;

            var root = new DockPanel { Margin = new Thickness(10) };

            // ---- przyciski
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = "Generuj BBS", Width = 120, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Anuluj", Width = 90, Height = 28, IsCancel = true };
            ok.Click += (s, e) => OnOk();
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            _tabs = new TabControl();
            root.Children.Add(_tabs);

            // =========================== Zakładka 1: podgląd prętów
            var preview = new DockPanel { Margin = new Thickness(6) };
            double kgB = rows.Where(r => r.Layer == BbsLayer.Bottom).Sum(r => r.MassKg);
            double kgT = rows.Where(r => r.Layer == BbsLayer.Top).Sum(r => r.MassKg);
            var summary = new TextBlock
            {
                Margin = new Thickness(0, 0, 0, 6), FontWeight = FontWeights.SemiBold,
                Text = string.Format(CultureInfo.InvariantCulture,
                    "Dół: {0} poz., {1:0} kg   |   Góra: {2} poz., {3:0} kg   |   Razem: {4:0.00} t   (strona szablonu = 26 wierszy)",
                    rows.Count(r => r.Layer == BbsLayer.Bottom), kgB, rows.Count(r => r.Layer == BbsLayer.Top), kgT, (kgB + kgT) / 1000.0)
            };
            DockPanel.SetDock(summary, Dock.Top);
            preview.Children.Add(summary);

            if (warnings.Count > 0)
            {
                var warnBox = new TextBox
                {
                    Text = "Uwagi (" + warnings.Count + "):\n• " + string.Join("\n• ", warnings),
                    IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    MaxHeight = 150, Margin = new Thickness(0, 6, 0, 0),
                    Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0x40, 0x00)),
                    Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF6, 0xE8))
                };
                DockPanel.SetDock(warnBox, Dock.Bottom);
                preview.Children.Add(warnBox);
            }

            var grid = new DataGrid
            {
                ItemsSource = rows, AutoGenerateColumns = false, IsReadOnly = true, CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column, GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                SelectionMode = DataGridSelectionMode.Extended
            };
            void Col(string header, string path, double width, bool right = false)
            {
                var c = new DataGridTextColumn { Header = header, Binding = new Binding(path), Width = width };
                if (right)
                {
                    var st = new Style(typeof(TextBlock));
                    st.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
                    c.ElementStyle = st;
                }
                grid.Columns.Add(c);
            }
            Col("Warstwa", nameof(BbsBarRow.LayerText), 70);
            Col("Poz.", nameof(BbsBarRow.MarkText), 50, true);
            Col("Typ", nameof(BbsBarRow.TypeSize), 50);
            Col("Szt.", nameof(BbsBarRow.Total), 50, true);
            Col("L [mm]", nameof(BbsBarRow.LengthText), 65, true);
            Col("Kod", nameof(BbsBarRow.ShapeCode), 45);
            Col("A", nameof(BbsBarRow.DimA), 55, true);
            Col("B", nameof(BbsBarRow.DimB), 55, true);
            Col("C", nameof(BbsBarRow.DimC), 55, true);
            Col("D", nameof(BbsBarRow.DimD), 55, true);
            Col("E/R", nameof(BbsBarRow.DimE), 55, true);
            Col("Masa [kg]", nameof(BbsBarRow.MassText), 75, true);
            grid.Columns.Add(new DataGridTextColumn { Header = "Uwagi", Binding = new Binding(nameof(BbsBarRow.Note)), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            grid.LoadingRow += (s, e) =>
            {
                var r = e.Row.Item as BbsBarRow;
                e.Row.Background = r != null && !string.IsNullOrEmpty(r.Note)
                    ? new SolidColorBrush(Color.FromRgb(0xFF, 0xE8, 0xCC))
                    : (r != null && r.Layer == BbsLayer.Top ? new SolidColorBrush(Color.FromRgb(0xF2, 0xF6, 0xFF)) : Brushes.White);
            };
            preview.Children.Add(grid);
            _tabs.Items.Add(new TabItem { Header = "Pręty (podgląd)", Content = preview });

            // =========================== Zakładka 2: arkusz
            var sheet = new StackPanel { Margin = new Thickness(6) };

            // --- layouty
            var layBox = new GroupBox { Header = "Layouty rysunku (blok A1-BL) — przypisz BOTTOM / TOP / BottomAndTop / Skip", Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 8) };
            _layoutsGrid = new DataGrid
            {
                ItemsSource = _assignments, AutoGenerateColumns = false, CanUserAddRows = false, CanUserDeleteRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column, MaxHeight = 170
            };
            _layoutsGrid.Columns.Add(new DataGridTextColumn { Header = "Layout", Binding = new Binding(nameof(BbsLayoutAssignment.LayoutName)), IsReadOnly = true, Width = 110 });
            _layoutsGrid.Columns.Add(new DataGridTextColumn { Header = "Numer rysunku", Binding = new Binding(nameof(BbsLayoutAssignment.DrawingNumber)), IsReadOnly = true, Width = 170 });
            _layoutsGrid.Columns.Add(new DataGridTextColumn { Header = "TITLE_3", Binding = new Binding(nameof(BbsLayoutAssignment.Title3)), IsReadOnly = true, Width = 260 });
            _layoutsGrid.Columns.Add(new DataGridComboBoxColumn
            {
                Header = "Przypisanie", Width = 130,
                ItemsSource = Enum.GetValues(typeof(BbsLayerAssignment)),
                SelectedItemBinding = new Binding(nameof(BbsLayoutAssignment.Assignment)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged }
            });
            layBox.Content = _layoutsGrid;
            sheet.Children.Add(layBox);

            // --- nagłówek
            var head = new GroupBox { Header = "Nagłówek arkusza", Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 8) };
            var hg = NewFormGrid();
            _output   = AddRow(hg, "Plik BBS (.xls):", suggestedOutput, BrowseOutputButton());
            _contract = AddRow(hg, "Contract No.:", initial.ContractNo);
            _addr1    = AddRow(hg, "Adres, linia 1:", initial.AddressLine1);
            _addr2    = AddRow(hg, "Adres, linia 2:", initial.AddressLine2);
            _addr3    = AddRow(hg, "Adres, linia 3:", initial.AddressLine3);
            _rev      = AddRow(hg, "Rewizja:", initial.Revision);
            _plot     = AddRow(hg, "Plot (do opisu A3):", initial.PlotSuffix);
            head.Content = hg;
            sheet.Children.Add(head);

            // --- akcesoria
            var acc = new GroupBox { Header = "Akcesoria (tylko na 1. arkuszu)", Padding = new Thickness(6), Margin = new Thickness(0, 0, 0, 8) };
            var ap = new StackPanel();
            _tricType = Combo(new[] { "TT40", "TT50" }, initial.TricTrakType);
            _tricQty = new TextBox { Width = 60, Text = initial.TricTrakQty ?? "", HorizontalContentAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 0, 0, 0) };
            _tricPreview = new TextBlock { Foreground = Brushes.Gray, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            ap.Children.Add(AccRow("TRIC-TRAK:", _tricType, _tricQty, _tricPreview));
            _hysType = Combo(new[] { "DK90", "DK165" }, initial.HystoolsType);
            _hysQty = new TextBox { Width = 60, Text = initial.HystoolsQty ?? "", HorizontalContentAlignment = HorizontalAlignment.Right, Margin = new Thickness(8, 0, 0, 0) };
            _hysPreview = new TextBlock { Foreground = Brushes.Gray, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            ap.Children.Add(AccRow("HYSTOOLS:", _hysType, _hysQty, _hysPreview));
            ap.Children.Add(new TextBlock { Text = accessoryHint ?? "", FontStyle = FontStyles.Italic, Foreground = Brushes.Gray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(110, 4, 0, 0) });
            acc.Content = ap;
            sheet.Children.Add(acc);
            foreach (var cb in new[] { _tricType, _hysType }) cb.SelectionChanged += (s, e) => UpdateAccPreview();
            foreach (var tb in new[] { _tricQty, _hysQty }) tb.TextChanged += (s, e) => UpdateAccPreview();
            UpdateAccPreview();

            // --- szablon
            var tpl = new GroupBox { Header = "Szablon BBS", Padding = new Thickness(6) };
            var tg = NewFormGrid();
            var pick = new Button { Content = "Wybierz…", Width = 80, Margin = new Thickness(6, 0, 0, 0) };
            var builtin = new Button { Content = "Wbudowany", Width = 80, Margin = new Thickness(6, 0, 0, 0) };
            var tplButtons = new StackPanel { Orientation = Orientation.Horizontal };
            tplButtons.Children.Add(pick);
            tplButtons.Children.Add(builtin);
            _template = AddRow(tg, "Plik szablonu:", templatePath ?? "", tplButtons);
            _template.IsReadOnly = true;
            _template.ToolTip = "Puste = wbudowany default-bbs.xls (Speedeck). Wybór jest zapamiętywany.";
            pick.Click += (s, e) =>
            {
                var fd = new Microsoft.Win32.OpenFileDialog { Title = "Szablon BBS", Filter = "Excel 97-2003 (*.xls)|*.xls" };
                if (File.Exists(_template.Text)) { fd.InitialDirectory = Path.GetDirectoryName(_template.Text); fd.FileName = Path.GetFileName(_template.Text); }
                if (fd.ShowDialog(this) == true) _template.Text = fd.FileName;
            };
            builtin.Click += (s, e) => _template.Text = "";
            tpl.Content = tg;
            sheet.Children.Add(tpl);

            _tabs.Items.Add(new TabItem { Header = "Arkusz", Content = new ScrollViewer { Content = sheet, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
            Content = root;
        }

        // ------------------------------------------------------------------

        private Button BrowseOutputButton()
        {
            var b = new Button { Content = "Przeglądaj…", Width = 90, Margin = new Thickness(6, 0, 0, 0) };
            b.Click += (s, e) =>
            {
                var fd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Zapisz BBS jako", Filter = "Excel 97-2003 (*.xls)|*.xls", DefaultExt = ".xls",
                    AddExtension = true, OverwritePrompt = false
                };
                string cur = _output.Text;
                if (!string.IsNullOrWhiteSpace(cur))
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(cur);
                        if (Directory.Exists(dir)) fd.InitialDirectory = dir;
                        fd.FileName = Path.GetFileName(cur);
                    }
                    catch (Exception ex) { Log.Error("BbsGeneratorDialog.Browse", ex); }
                }
                if (fd.ShowDialog(this) == true) _output.Text = fd.FileName;
            };
            return b;
        }

        private static Grid NewFormGrid()
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            return g;
        }

        private static TextBox AddRow(Grid g, string label, string value, FrameworkElement extra = null)
        {
            int r = g.RowDefinitions.Count;
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            var t = new TextBox { Text = value ?? "", Margin = new Thickness(0, 2, 0, 2) };
            Grid.SetRow(l, r); Grid.SetColumn(l, 0);
            Grid.SetRow(t, r); Grid.SetColumn(t, 1);
            g.Children.Add(l); g.Children.Add(t);
            if (extra != null) { Grid.SetRow(extra, r); Grid.SetColumn(extra, 2); g.Children.Add(extra); }
            return t;
        }

        private static ComboBox Combo(string[] items, string selected)
        {
            var cb = new ComboBox { Width = 80, ItemsSource = items };
            int i = Array.FindIndex(items, x => string.Equals(x, (selected ?? "").Trim(), StringComparison.OrdinalIgnoreCase));
            cb.SelectedIndex = i >= 0 ? i : 0;
            return cb;
        }

        private static StackPanel AccRow(string label, ComboBox type, TextBox qty, TextBlock preview)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
            p.Children.Add(new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(type);
            p.Children.Add(qty);
            p.Children.Add(new TextBlock { Text = "No. X 2m", Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            p.Children.Add(preview);
            return p;
        }

        private BbsGenerationContext AccessoryContext() => new BbsGenerationContext
        {
            TricTrakType = _tricType?.SelectedItem as string ?? "TT40",
            TricTrakQty  = (_tricQty?.Text ?? "").Trim(),
            HystoolsType = _hysType?.SelectedItem as string ?? "DK165",
            HystoolsQty  = (_hysQty?.Text ?? "").Trim()
        };

        private void UpdateAccPreview()
        {
            if (_tricPreview == null || _hysPreview == null || _tricQty == null || _hysQty == null) return;
            var a = AccessoryContext();
            _tricPreview.Text = "→ " + a.BuildTricTrakLine();
            _hysPreview.Text  = "→ " + a.BuildHystoolsLine();
        }

        private static bool ValidQty(string s) =>
            string.IsNullOrWhiteSpace(s) || (int.TryParse(s.Trim(), out int n) && n > 0);

        private void Warn(string msg, int tab = 1)
        {
            MessageBox.Show(this, msg, "BBS", MessageBoxButton.OK, MessageBoxImage.Warning);
            _tabs.SelectedIndex = tab;
        }

        private void OnOk()
        {
            _layoutsGrid.CommitEdit(DataGridEditingUnit.Row, true);

            if (!_assignments.Any(a => a.Assignment != BbsLayerAssignment.Skip))
            {
                Warn("Przypisz co najmniej jeden layout jako Bottom, Top albo BottomAndTop (zakładka „Arkusz”).");
                return;
            }

            bool hasB = _assignments.Any(x => x.Assignment == BbsLayerAssignment.Bottom || x.Assignment == BbsLayerAssignment.BottomAndTop);
            bool hasT = _assignments.Any(x => x.Assignment == BbsLayerAssignment.Top || x.Assignment == BbsLayerAssignment.BottomAndTop);
            if (_rows.Any(r => r.Layer == BbsLayer.Bottom) && !hasB) { Warn("Są pozycje dołem (< 100), ale żaden layout nie jest przypisany jako Bottom / BottomAndTop."); return; }
            if (_rows.Any(r => r.Layer == BbsLayer.Top) && !hasT) { Warn("Są pozycje górą (101+), ale żaden layout nie jest przypisany jako Top / BottomAndTop."); return; }

            string output = (_output.Text ?? "").Trim();
            if (output.Length == 0) { Warn("Podaj plik wynikowy BBS."); return; }
            if (!output.EndsWith(".xls", StringComparison.OrdinalIgnoreCase)) output += ".xls";
            string dir;
            try { dir = Path.GetDirectoryName(Path.GetFullPath(output)); }
            catch (Exception) { Warn("Nieprawidłowa ścieżka pliku BBS."); return; }
            if (!Directory.Exists(dir)) { Warn("Folder nie istnieje:\n" + dir); return; }
            if (File.Exists(output) &&
                MessageBox.Show(this, "Plik już istnieje — nadpisać?\n" + output, "BBS", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            if (!ValidQty(_tricQty.Text) || !ValidQty(_hysQty.Text))
            {
                Warn("Ilość TRIC-TRAK / HYSTOOLS: liczba całkowita (np. 52) albo puste pole.");
                return;
            }
            if (_template.Text.Length > 0 && !File.Exists(_template.Text))
            {
                Warn("Nie znaleziono pliku szablonu:\n" + _template.Text);
                return;
            }

            var a = AccessoryContext();
            Result = new BbsGenerationContext
            {
                Assignments  = _assignments.ToList(),
                ContractNo   = _contract.Text.Trim(),
                AddressLine1 = _addr1.Text,
                AddressLine2 = _addr2.Text,
                AddressLine3 = _addr3.Text,
                Revision     = _rev.Text.Trim(),
                PlotSuffix   = _plot.Text.Trim(),
                TricTrakType = a.TricTrakType, TricTrakQty = a.TricTrakQty,
                HystoolsType = a.HystoolsType, HystoolsQty = a.HystoolsQty
            };
            OutputPath   = output;
            TemplatePath = _template.Text.Trim();
            DialogResult = true;
        }
    }
}
