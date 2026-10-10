using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BricsCadRc.Core;

namespace BricsCadRc.Dialogs
{
    /// <summary>
    /// RC_BEAM — parametry belki / pogrubienia (rekord RC_BEAM). Wartości domyślne = rozstrzygnięcia planu belek
    /// (docs/PLAN_BELKI_2026-10-10.md 1a / 1b / 1c) — „domyślne, do potwierdzenia”. H z opisu GA („450THK”).
    /// </summary>
    public class BeamDialog : Window
    {
        private readonly BeamParams _p;
        private readonly TextBox _h, _w, _nibW, _nibH, _sDia, _sSp, _bDia, _bCnt, _tDia, _tCnt, _uDia, _shape, _lap, _max;
        private readonly ComboBox _topMode;
        private readonly CheckBox _generate;

        /// <summary>Parametry po OK.</summary>
        public BeamParams Result { get; private set; }
        /// <summary>Po OK: generować pręty belki (strzemiona + pręty podłużne).</summary>
        public bool Generate { get; private set; }

        public BeamDialog(BeamParams p, string heightSource, IList<string> info)
        {
            _p = p.Clone();
            Title = "Belka / pogrubienie";
            Width = 520; SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            var root = new StackPanel { Margin = new Thickness(12) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int row = 0;
            TextBox Field(string label, string value, string hint = null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3) };
                Grid.SetRow(l, row); grid.Children.Add(l);
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var tb = new TextBox { Text = value, Width = 90, Margin = new Thickness(0, 3, 6, 3) };
                sp.Children.Add(tb);
                if (hint != null) sp.Children.Add(new TextBlock { Text = hint, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 190 });
                Grid.SetRow(sp, row); Grid.SetColumn(sp, 1); grid.Children.Add(sp);
                row++;
                return tb;
            }
            string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
            string I(int v) => v.ToString(CultureInfo.InvariantCulture);

            _h    = Field("Wysokość belki H [mm]", _p.Height > 0 ? F(_p.Height) : "",
                          _p.Height > 0 ? (heightSource ?? "") : "nie znaleziono — wpisz");
            _w    = Field("Szerokość [mm]", F(_p.Width), "z geometrii strefy (informacyjnie)");
            _w.IsReadOnly = true;   // pręty liczone z geometrii odcinków strefy
            _nibW = Field("Nib: szerokość [mm]", F(_p.NibWidth), "domyślne 135");
            _nibH = Field("Nib: wysokość [mm]", F(_p.NibHeight), "domyślne 150");
            _sDia = Field("Strzemiona: średnica H", I(_p.StirrupDia), "CP przy palach: H12@200");
            _sSp  = Field("Strzemiona: rozstaw [mm]", F(_p.StirrupSpacing), "domyślne H10 co 200 — decyzja Bartka");
            _shape = Field("Strzemiona: kod kształtu", _p.StirrupShape ?? "51", "51 (zamknięte)");
            _bDia = Field("Pręty dołem: średnica H", I(_p.BottomDia));
            _bCnt = Field("Pręty dołem: liczba", I(_p.BottomCount));
            _tDia = Field("Pręty górą: średnica H", I(_p.TopDia));
            _tCnt = Field("Pręty górą: liczba", I(_p.TopCount));
            _uDia = Field("U-bary na końcach: średnica H", I(_p.UBarDia), "kształt 21, 2 na warstwę");
            _lap  = Field("Zakład [mm]", F(_p.Lap), "domyślne 800, do potwierdzenia");
            _max  = Field("Maks. długość pręta [mm]", F(_p.MaxLength));
            root.Children.Add(grid);

            var modeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            modeRow.Children.Add(new TextBlock { Text = "Góra strzemienia pod:", Width = 210, VerticalAlignment = VerticalAlignment.Center });
            _topMode = new ComboBox { Width = 250 };
            _topMode.Items.Add("obiema warstwami siatki górnej (T1 + T2)");
            _topMode.Items.Add("jedną warstwą siatki górnej (T1)");
            _topMode.SelectedIndex = _p.StirrupTopLayers >= 2 ? 0 : 1;
            modeRow.Children.Add(_topMode);
            root.Children.Add(modeRow);

            _generate = new CheckBox
            {
                Content = "Generuj pręty belki (strzemiona, pręty podłużne, pręty L, U-bary; poprzednie pręty tej belki zostaną zastąpione)",
                IsChecked = true, Margin = new Thickness(0, 8, 0, 0)
            };
            root.Children.Add(_generate);

            var lines = new List<string>(info ?? new List<string>())
            {
                "Wartości domyślne (do potwierdzenia): zakład 800, max 6000, strzemię 51 pod obiema warstwami T, H10 co 200.",
                "Narożniki L / złącza T: pręty L kształt 11 (ramiona z zakładem); końce (krawędź i wewnętrzny): U-bary kształt 21 — domyślne, do potwierdzenia."
            };
            root.Children.Add(new TextBox
            {
                Text = string.Join("\n", lines), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0), Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF8))
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "OK", Width = 100, Height = 26, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Anuluj", Width = 90, Height = 26, IsCancel = true };
            ok.Click += (s, e) => OnOk();
            buttons.Children.Add(ok); buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;
        }

        private void OnOk()
        {
            bool Num(TextBox tb, string name, double min, double max, out double v)
            {
                if (NumberParser.TryParseDouble(tb.Text, out v) && v >= min && v <= max) return true;
                MessageBox.Show(this, $"{name}: podaj liczbę {min}–{max}.", "Belka", MessageBoxButton.OK, MessageBoxImage.Warning);
                tb.Focus();
                return false;
            }
            if (!Num(_h, "Wysokość H", 150, 3000, out double h)) return;
            if (!Num(_w, "Szerokość", 150, 1500, out double w)) return;
            if (!Num(_nibW, "Szerokość nibu", 0, 255, out double nw)) return;
            if (!Num(_nibH, "Wysokość nibu", 0, h, out double nh)) return;
            if (!Num(_sDia, "Średnica strzemion", 6, 25, out double sd)) return;
            if (!Num(_sSp, "Rozstaw strzemion", 50, 400, out double ss)) return;
            if (!Num(_bDia, "Średnica dołem", 8, 40, out double bd)) return;
            if (!Num(_bCnt, "Liczba dołem", 1, 12, out double bc)) return;
            if (!Num(_tDia, "Średnica górą", 8, 40, out double td)) return;
            if (!Num(_tCnt, "Liczba górą", 1, 12, out double tc)) return;
            if (!Num(_uDia, "Średnica U-barów", 8, 40, out double ud)) return;
            if (!Num(_lap, "Zakład", 200, 3000, out double lap)) return;
            if (!Num(_max, "Maks. długość", 1000, 12000, out double max)) return;
            if (lap >= max) { MessageBox.Show(this, "Zakład musi być krótszy niż maks. długość pręta.", "Belka"); return; }
            string shape = (_shape.Text ?? "").Trim();
            if (shape.Length == 0) shape = "51";

            var r = _p.Clone();
            r.Height = h; r.Width = w; r.NibWidth = nw; r.NibHeight = nh;
            r.StirrupDia = (int)sd; r.StirrupSpacing = ss; r.StirrupShape = shape;
            r.BottomDia = (int)bd; r.BottomCount = (int)bc; r.TopDia = (int)td; r.TopCount = (int)tc; r.UBarDia = (int)ud;
            r.Lap = lap; r.MaxLength = max; r.StirrupTopLayers = _topMode.SelectedIndex == 1 ? 1 : 2;
            Result = r;
            Generate = _generate.IsChecked == true;
            DialogResult = true;
        }
    }
}
