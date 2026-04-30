using System.Windows;
using BricsCadRc.Core;

namespace BricsCadRc.Dialogs
{
    public partial class EditLabelDialog : Window
    {
        private readonly int    _diameter;
        private readonly string _posNr;
        private readonly int    _count;

        public int                ResultCount       { get; private set; }
        public double             ResultSpacing     { get; private set; }
        public string             ResultMark        { get; private set; } = "";
        public BarVisibilityMode  ResultVisibility  { get; private set; } = BarVisibilityMode.All;
        public bool               ResultShowSpacing { get; private set; } = true;

        // mark = pełny aktualny mark np. "H12-01-200 B1"
        // diameter i spacing używane do odbudowania baseMark
        public EditLabelDialog(int count, string mark, int diameter, double spacing,
            BarVisibilityMode currentVisibility = BarVisibilityMode.All,
            bool showSpacing = true)
        {
            InitializeComponent();
            _diameter = diameter;
            _count    = count;

            switch (currentVisibility)
            {
                case BarVisibilityMode.MiddleOnly: RbMiddle.IsChecked    = true; break;
                case BarVisibilityMode.FirstLast:  RbFirstLast.IsChecked = true; break;
                case BarVisibilityMode.Manual:     RbManual.IsChecked    = true; break;
                default:                           RbAll.IsChecked       = true; break;
            }

            // Rozbij mark na coreOfMark i suffix
            // Format: "H12-01-200" lub "H12-01-200 B1"
            var parts      = mark.Split(' ');
            string coreOfMark = parts[0];
            string suffix     = parts.Length > 1
                ? string.Join(" ", parts, 1, parts.Length - 1)
                : "";

            // Wyodrębnij posNr z coreOfMark (środkowy segment między myślnikami)
            var segments = coreOfMark.Split('-');
            _posNr = segments.Length >= 2 ? segments[1] : "01";

            CountBox.Text = count.ToString();

            // Prefill z coreOfMark (np. "H12-01-200" → 200), fallback na parametr spacing.
            // Używamy segments (z coreOfMark.Split('-')), nie full mark — unika problemu z suffixem
            // (np. "200 UB" jako ostatni segment przy mark.Split('-') nie parsuje się na double).
            double spacingFromMark = spacing;
            if (segments.Length >= 3
                && double.TryParse(segments[segments.Length - 1],
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double sp))
            {
                spacingFromMark = sp;
            }
            SpacingBox.Text = spacingFromMark.ToString("F0");
            SuffixBox.Text     = suffix;
            BaseMarkLabel.Text = coreOfMark;
            ShowSpacingCheck.IsChecked = showSpacing;
            SpacingBox.IsEnabled = showSpacing;
            UpdateBaseMark();
            UpdatePreview();

            CountBox.TextChanged   += (s, e) => UpdatePreview();
            SpacingBox.TextChanged += (s, e) => { UpdateBaseMark(); UpdatePreview(); };
            SuffixBox.TextChanged  += (s, e) => UpdatePreview();
        }

        void UpdateBaseMark()
        {
            bool show = ShowSpacingCheck?.IsChecked == true;
            if (show && double.TryParse(SpacingBox.Text, out double parsedSp) && parsedSp > 0)
            {
                // ShowSpacing ON + valid spacing → inline (omija guard count≤1 w FormatMark)
                BaseMarkLabel.Text = $"H{_diameter}-{int.Parse(_posNr):D2}-{(int)parsedSp}";
            }
            else
            {
                // ShowSpacing OFF lub brak spacing → prefix bez spacing
                BaseMarkLabel.Text = BarData.FormatMark(_diameter, int.Parse(_posNr), 0, _count);
            }
        }

        void ShowSpacingCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (SpacingBox != null)
                SpacingBox.IsEnabled = ShowSpacingCheck.IsChecked == true;
            UpdateBaseMark();
            UpdatePreview();
        }

        void UpdatePreview()
        {
            string suffix  = SuffixBox.Text.Trim();
            string preview = string.IsNullOrEmpty(suffix)
                ? $"{CountBox.Text} {BaseMarkLabel.Text}"
                : $"{CountBox.Text} {BaseMarkLabel.Text} {suffix}";
            PreviewLabel.Text = preview;
        }

        void OK_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(CountBox.Text, out int c) || c < 1)
            { MessageBox.Show("Nieprawidłowa liczba prętów."); return; }
            if (!double.TryParse(SpacingBox.Text, out double sp) || sp <= 0)
            { MessageBox.Show("Nieprawidłowy rozstaw."); return; }

            string suffix   = SuffixBox.Text.Trim();
            string baseMark = BaseMarkLabel.Text;
            ResultCount   = c;
            ResultSpacing = sp;
            ResultMark        = string.IsNullOrEmpty(suffix) ? baseMark : $"{baseMark} {suffix}";
            ResultShowSpacing = ShowSpacingCheck?.IsChecked == true;
            ResultVisibility = RbMiddle.IsChecked    == true ? BarVisibilityMode.MiddleOnly
                             : RbFirstLast.IsChecked == true ? BarVisibilityMode.FirstLast
                             : RbManual.IsChecked    == true ? BarVisibilityMode.Manual
                             : BarVisibilityMode.All;
            DialogResult  = true;
        }

        void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
