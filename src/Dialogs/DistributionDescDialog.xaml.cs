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
        /// <summary>Widoczność prętów (tylko gdy <c>askVisibility</c>; domyślnie Manualny — prośba Bartka 2026-10-28).</summary>
        public BarVisibilityMode ResultVisibility { get; private set; } = BarVisibilityMode.Manual;
        public bool AskVisibility { get; }

        public DistributionDescDialog(BarData sourceBar, int autoCount, double autoSpacing, string baseMark, string initialSuffix = "",
                                      bool askVisibility = false)
        {
            InitializeComponent();
            AskVisibility = askVisibility;
            if (askVisibility) VisibilityPanel.Visibility = System.Windows.Visibility.Visible;
            _sourceBar = sourceBar;
            _diameter  = sourceBar.Diameter;
            _posNr     = SingleBarEngine.ExtractPosNr(sourceBar.Mark);

            CountBox.Text   = autoCount.ToString();
            SpacingBox.Text = ((int)autoSpacing).ToString();
            SuffixBox.Text  = initialSuffix ?? "";

            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (CountBox == null || SpacingBox == null) return;

            if (!int.TryParse(CountBox.Text,   out int count)   || count   <= 0) return;
            if (!int.TryParse(SpacingBox.Text, out int spacing) || spacing <= 0) return;

            string suffix      = SuffixBox?.Text ?? "";
            string newBaseMark = BarData.FormatMark(_diameter, _posNr, spacing,
                BarBlockEngine.IsAny(_sourceBar) ? System.Math.Max(2, count) : count);   // Any: rozstaw zawsze w opisie
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
            if (!NumberParser.TryParseDouble(SpacingBox.Text, out double spacing) || spacing <= 0)
            {
                MessageBox.Show("Invalid spacing value.", "RC SLAB",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BarCount   = count;
            BarSpacing = spacing;
            Suffix     = SuffixBox.Text.Trim();
            ResultVisibility = RbAll.IsChecked == true ? BarVisibilityMode.All
                        : RbMiddle.IsChecked == true ? BarVisibilityMode.MiddleOnly
                        : RbFirstLast.IsChecked == true ? BarVisibilityMode.FirstLast
                        : BarVisibilityMode.Manual;

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
