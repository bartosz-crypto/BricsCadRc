using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BricsCadRc.Core;

namespace BricsCadRc.Dialogs
{
    /// <summary>
    /// RC_SECTION — parametry przekroju wykryte z rysunku (grubość i SSL z opisu płyty, nib z NibDetector)
    /// do sprawdzenia i poprawy przed narysowaniem.
    /// </summary>
    public class SectionDialog : Window
    {
        private readonly SectionPlanner.Input _inp;
        private readonly TextBox _letter, _thk, _ssl, _nibH, _cb, _ct, _cn, _tol;
        private readonly CheckBox _piles;
        public string Letter { get; private set; }

        public SectionDialog(AutoRebarEngine.SectionContext ctx, string letter)
        {
            _inp = ctx.Input;
            Title = "Przekrój płyty";
            Width = 470; SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            var root = new StackPanel { Margin = new Thickness(12) };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            int row = 0;
            TextBox Field(string label, string value, string hint = null)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 3, 8, 3) };
                Grid.SetRow(l, row); grid.Children.Add(l);
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var tb = new TextBox { Text = value, Width = 110, Margin = new Thickness(0, 3, 6, 3) };
                sp.Children.Add(tb);
                if (hint != null) sp.Children.Add(new TextBlock { Text = hint, Foreground = Brushes.Gray, VerticalAlignment = VerticalAlignment.Center });
                Grid.SetRow(sp, row); Grid.SetColumn(sp, 1); grid.Children.Add(sp);
                row++;
                return tb;
            }
            string F(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

            _letter = Field("Oznaczenie przekroju", letter);
            _thk    = Field("Grubość płyty [mm]", F(_inp.Thickness), ctx.ThicknessSource != null ? "z opisu płyty" : "nie znaleziono");
            _ssl    = Field("SSL", _inp.Ssl ?? "", ctx.SslSource != null ? "z opisu płyty" : "puste = bez znacznika");
            _nibH   = Field("Wysokość nibu [mm]", F(_inp.NibHeight), ctx.NibCount > 0 ? $"nib: {ctx.NibCount} krawędzi" : "brak nibu");
            _cb     = Field("Otulina dołem [mm]", F(_inp.CoverBottom));
            _ct     = Field("Otulina górą [mm]", F(_inp.CoverTop));
            _cn     = Field("Otulina górą w nibie [mm]", F(_inp.CoverNib));
            _tol    = Field("Zapas góra / dół [mm]", F(_inp.UbTolerance), "pierwszy pręt = otulina + zapas");
            root.Children.Add(grid);

            _piles = new CheckBox { Content = "Rysuj przecięte pale", IsChecked = _inp.DrawPiles, Margin = new Thickness(0, 6, 0, 0) };
            root.Children.Add(_piles);

            var info = new List<string>(ctx.Info);
            if (ctx.ThicknessSource != null) info.Add("Opis: " + ctx.ThicknessSource);
            root.Children.Add(new TextBox
            {
                Text = string.Join("\n", info), IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 10, 0, 0), Background = new SolidColorBrush(Color.FromRgb(0xF4, 0xF6, 0xF8))
            });

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "Rysuj", Width = 100, Height = 26, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
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
                MessageBox.Show(this, $"{name}: podaj liczbę {min}–{max}.", "Przekrój płyty", MessageBoxButton.OK, MessageBoxImage.Warning);
                tb.Focus();
                return false;
            }
            if (!Num(_thk, "Grubość płyty", 100, 1000, out double thk)) return;
            if (!Num(_nibH, "Wysokość nibu", 50, thk, out double nibH)) return;
            if (!Num(_cb, "Otulina dołem", 0, 150, out double cb)) return;
            if (!Num(_ct, "Otulina górą", 0, 150, out double ct)) return;
            if (!Num(_cn, "Otulina w nibie", 0, 150, out double cn)) return;
            if (!Num(_tol, "Zapas", 0, 50, out double tol)) return;
            string letter = (_letter.Text ?? "").Trim().ToUpperInvariant();
            if (letter.Length == 0) { MessageBox.Show(this, "Podaj oznaczenie przekroju.", "Przekrój płyty"); return; }

            _inp.Thickness = thk; _inp.NibHeight = nibH;
            _inp.CoverBottom = cb; _inp.CoverTop = ct; _inp.CoverNib = cn; _inp.UbTolerance = tol;
            _inp.Ssl = string.IsNullOrWhiteSpace(_ssl.Text) ? null : _ssl.Text.Trim();
            _inp.DrawPiles = _piles.IsChecked == true;
            Letter = letter;
            DialogResult = true;
        }
    }
}
