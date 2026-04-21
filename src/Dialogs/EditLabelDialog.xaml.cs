using System.Windows;
using BricsCadRc.Core;

namespace BricsCadRc.Dialogs
{
    public partial class EditLabelDialog : Window
    {
        private readonly int    _diameter;
        private readonly string _posNr;
        private readonly int    _count;

        public int                ResultCount      { get; private set; }
        public double             ResultSpacing    { get; private set; }
        public string             ResultMark       { get; private set; } = "";
        public BarVisibilityMode  ResultVisibility { get; private set; } = BarVisibilityMode.All;

        // mark = pełny aktualny mark np. "H12-01-200 B1"
        // diameter i spacing używane do odbudowania baseMark
        public EditLabelDialog(int count, string mark, int diameter, double spacing,
            BarVisibilityMode currentVisibility = BarVisibilityMode.All)
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

            // Prefill z Mark (np. "H12-01-190" → 190), fallback na parametr spacing
            double spacingFromMark = spacing;
            if (!string.IsNullOrEmpty(mark))
            {
                var mParts = mark.Split('-');
                if (mParts.Length >= 3
                    && double.TryParse(mParts[mParts.Length - 1],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out double sp))
                {
                    spacingFromMark = sp;
                }
            }
            SpacingBox.Text = spacingFromMark.ToString("F0");
            SuffixBox.Text     = suffix;
            BaseMarkLabel.Text = coreOfMark;
            UpdatePreview();

            CountBox.TextChanged   += (s, e) => UpdatePreview();
            SpacingBox.TextChanged += (s, e) => { UpdateBaseMark(); UpdatePreview(); };
            SuffixBox.TextChanged  += (s, e) => UpdatePreview();
        }

        void UpdateBaseMark()
        {
            if (double.TryParse(SpacingBox.Text, out double sp) && sp > 0)
                BaseMarkLabel.Text = BarData.FormatMark(_diameter, int.Parse(_posNr), sp, _count);
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
            ResultMark    = string.IsNullOrEmpty(suffix) ? baseMark : $"{baseMark} {suffix}";
            ResultVisibility = RbMiddle.IsChecked    == true ? BarVisibilityMode.MiddleOnly
                             : RbFirstLast.IsChecked == true ? BarVisibilityMode.FirstLast
                             : RbManual.IsChecked    == true ? BarVisibilityMode.Manual
                             : BarVisibilityMode.All;
            DialogResult  = true;
        }

        void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
