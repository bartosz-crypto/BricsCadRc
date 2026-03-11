using System.Windows;
using System.Windows.Controls;
using BricsCadRc.Core;

namespace BricsCadRc.Dialogs
{
    /// <summary>
    /// Dialog "Reinforcement description" dla rozkładu (FLOW 2, krok 6).
    /// Użytkownik może nadpisać liczbę prętów, rozstaw i dodać sufiks.
    /// </summary>
    public partial class DistributionDescDialog : Window
    {
        private readonly BarData _sourceBar;
        private readonly string  _baseMark;

        public int    BarCount   { get; private set; }
        public double BarSpacing { get; private set; }
        public string Suffix     { get; private set; }

        public enum VisibilityMode { All, MiddleOnly, FirstAndLast, Manual }
        public VisibilityMode BarVisibility { get; private set; } = VisibilityMode.All;

        public DistributionDescDialog(BarData sourceBar, int autoCount, double autoSpacing, string baseMark)
        {
            InitializeComponent();
            _sourceBar = sourceBar;
            _baseMark  = baseMark;

            CountBox.Text   = autoCount.ToString();
            SpacingBox.Text = ((int)autoSpacing).ToString();
            SuffixBox.Text  = "";

            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (CountBox == null || SpacingBox == null) return;

            int.TryParse(CountBox.Text,   out int count);
            int.TryParse(SpacingBox.Text, out int spacing);
            string suffix = SuffixBox?.Text ?? "";

            string label = $"{count} {_baseMark}";
            if (!string.IsNullOrWhiteSpace(suffix)) label += $" {suffix}";
            PreviewText.Text = label;
        }

        private void Input_Changed(object sender, TextChangedEventArgs e)
            => UpdatePreview();

        private void Visibility_Changed(object sender, RoutedEventArgs e)
        {
            if      (RadioAll       .IsChecked == true) BarVisibility = VisibilityMode.All;
            else if (RadioMiddle    .IsChecked == true) BarVisibility = VisibilityMode.MiddleOnly;
            else if (RadioFirstLast .IsChecked == true) BarVisibility = VisibilityMode.FirstAndLast;
            else                                        BarVisibility = VisibilityMode.Manual;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(CountBox.Text, out int count) || count <= 0)
            {
                MessageBox.Show("Invalid number of bars.", "RC SLAB",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!double.TryParse(SpacingBox.Text, out double spacing) || spacing <= 0)
            {
                MessageBox.Show("Invalid spacing value.", "RC SLAB",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BarCount   = count;
            BarSpacing = spacing;
            Suffix     = SuffixBox.Text.Trim();

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
