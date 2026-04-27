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
        private readonly int     _diameter;
        private readonly int     _posNr;

        public int    BarCount   { get; private set; }
        public double BarSpacing { get; private set; }
        public string Suffix     { get; private set; }

        public DistributionDescDialog(BarData sourceBar, int autoCount, double autoSpacing, string baseMark)
        {
            InitializeComponent();
            _sourceBar = sourceBar;
            _diameter  = sourceBar.Diameter;
            _posNr     = SingleBarEngine.ExtractPosNr(sourceBar.Mark);

            CountBox.Text   = autoCount.ToString();
            SpacingBox.Text = ((int)autoSpacing).ToString();
            SuffixBox.Text  = "";

            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (CountBox == null || SpacingBox == null) return;

            if (!int.TryParse(CountBox.Text,   out int count)   || count   <= 0) return;
            if (!int.TryParse(SpacingBox.Text, out int spacing) || spacing <= 0) return;

            string suffix      = SuffixBox?.Text ?? "";
            string newBaseMark = BarData.FormatMark(_diameter, _posNr, spacing, count);
            string label       = string.IsNullOrWhiteSpace(suffix)
                ? $"{count} {newBaseMark}"
                : $"{count} {newBaseMark} {suffix}";
            PreviewText.Text = label;
        }

        private void Input_Changed(object sender, TextChangedEventArgs e)
            => UpdatePreview();

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
