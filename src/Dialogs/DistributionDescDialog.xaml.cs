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
        /// <summary>Krotność z wpisu „kxn” (np. „2x28” → BarCount 56, BarMult 2; 2026-10-29, jak RC_EDIT_LABEL); 0 — zwykła liczba.</summary>
        public int    BarMult    { get; private set; }
        /// <summary>Rozstaw w opisie; 0 — pole puste, opis bez rozstawu (prośba Bartka 2026-10-29, jak RC_EDIT_LABEL bez „Show spacing”).</summary>
        public double BarSpacing { get; private set; }
        public bool   ShowSpacing => BarSpacing > 0;
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

            int m0 = sourceBar.CountMult;
            CountBox.Text   = m0 >= 2 && autoCount % m0 == 0 ? $"{m0}x{autoCount / m0}" : autoCount.ToString();
            SpacingBox.Text = autoSpacing > 0 ? ((int)autoSpacing).ToString() : "";   // 0 — opis bez rozstawu
            SuffixBox.Text  = initialSuffix ?? "";

            UpdatePreview();
        }

        private void UpdatePreview()
        {
            if (CountBox == null || SpacingBox == null) return;

            var pc = BarLabelRules.ParseCountInput(CountBox.Text);
            if (pc == null) return;
            int count = pc.Value.Total;
            if (!TryReadSpacing(out double spD)) return;
            int spacing = (int)spD;
            string countText = pc.Value.Mult >= 2 ? $"{pc.Value.Mult}x{count / pc.Value.Mult}" : count.ToString();

            string suffix      = SuffixBox?.Text ?? "";
            string newBaseMark = BarData.FormatMark(_diameter, _posNr, spacing,
                BarBlockEngine.IsAny(_sourceBar) ? System.Math.Max(2, count) : count);   // Any: rozstaw zawsze w opisie
            string label       = string.IsNullOrWhiteSpace(suffix)
                ? $"{countText} {newBaseMark}"
                : $"{countText} {newBaseMark} {suffix}";
            PreviewText.Text = label;
        }

        /// <summary>Pole rozstawu: puste (lub 0) → 0 = opis bez rozstawu; liczba &gt; 0 → rozstaw; inny tekst → false.</summary>
        private bool TryReadSpacing(out double spacing)
        {
            spacing = 0;
            string t = SpacingBox?.Text?.Trim() ?? "";
            if (t.Length == 0) return true;
            if (!NumberParser.TryParseDouble(t, out spacing) || spacing < 0) { spacing = 0; return false; }
            return true;
        }

        private void Input_Changed(object sender, TextChangedEventArgs e)
            => UpdatePreview();

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            var pcOk = BarLabelRules.ParseCountInput(CountBox.Text);
            if (pcOk == null)
            {
                MessageBox.Show("Invalid number of bars (e.g. 28 or 2x28).", "RC SLAB",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!TryReadSpacing(out double spacing))
            {
                MessageBox.Show("Invalid spacing value (leave empty for no spacing in the label).", "RC SLAB",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BarCount   = pcOk.Value.Total;
            BarMult    = pcOk.Value.Mult;
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
