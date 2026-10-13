using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using BricsCadRc.Core;

namespace BricsCadRc.Dialogs
{
    public partial class EditDistributionDialog : Window
    {
        public int    ResultCount          { get; private set; }
        public double ResultSpacing        { get; private set; }
        public double ResultCover          { get; private set; }
        public double ResultBarsSpan       { get; private set; }
        public bool   ResultRebuildLeader  { get; private set; }
        public string ResultViewingDirection { get; private set; } = "Auto";
        public bool   ResultAddAnnotation  => CbAddAnnotation.IsChecked == true;
        public bool   PreviewApplied       { get; private set; }

        private readonly Action<int, double, double, double> _onPreview;  // (count, spacing, cover, barsSpan)
        private readonly DispatcherTimer _previewTimer;

        private int    _prevCount;
        private double _prevSpacing;
        private double _prevBarsSpan;
        private bool   _suppressRecalc;

        public EditDistributionDialog(BarData bar, Action<int, double, double, double> onPreview, bool annotMissing = false)
        {
            InitializeComponent();
            _onPreview = onPreview;

            _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            _previewTimer.Tick += PreviewTimer_Tick;

            _suppressRecalc = true;
            MarkLabel.Text  = bar.Mark;
            CountBox.Text   = bar.Count.ToString(CultureInfo.InvariantCulture);
            SpacingBox.Text = bar.Spacing.ToString("F0", CultureInfo.InvariantCulture);
            CoverBox.Text   = bar.Cover.ToString("F0", CultureInfo.InvariantCulture);
            SpanBox.Text    = bar.BarsSpan.ToString("F0", CultureInfo.InvariantCulture);
            _origSpan       = bar.BarsSpan;
            _origSpanText   = SpanBox.Text;
            _suppressRecalc = false;

            _prevCount    = bar.Count;
            _prevSpacing  = bar.Spacing;
            _prevBarsSpan = bar.BarsSpan;

            // Safety: stare rozkłady mogą mieć BarsSpan=0 — ustaw minimum
            if (_prevBarsSpan < (bar.Count - 1) * bar.Spacing)
                _prevBarsSpan = (bar.Count - 1) * bar.Spacing;

            var vdItem = ViewDirBox.Items.Cast<ComboBoxItem>()
                .FirstOrDefault(i => (string)i.Content == (bar.ViewingDirection ?? "Auto"));
            ViewDirBox.SelectedItem = vdItem ?? ViewDirBox.Items[0];

            // LostFocus — cross-field rekalkulacja (po zakończeniu edycji)
            CountBox.LostFocus   += OnCountChanged;
            SpacingBox.LostFocus += OnSpacingChanged;
            SpanBox.LostFocus    += OnSpanChanged;

            // TextChanged — tylko timer live preview
            CountBox.TextChanged   += OnAnyTextChanged;
            SpacingBox.TextChanged += OnAnyTextChanged;
            CoverBox.TextChanged   += OnAnyTextChanged;
            SpanBox.TextChanged    += OnAnyTextChanged;

            if (annotMissing)
                CbAddAnnotation.Visibility = Visibility.Visible;
        }

        // decyzja użytkownika 2026-10-13: zakres rozkładu (Span — gripy na krawędziach) NIE zmienia się przy zmianie liczby
        // ani rozstawu. Rozstaw → liczba = ile się zmieści w zakresie (195: nadal 12, 190: wskakuje 13). Liczba większa, niż się
        // mieści → rozstaw zmniejszony (w dół do 5), zakres bez zmian. Zakres zmienia tylko pole „Span” (i gripy).

        // dokładny zakres rozkładu (pole pokazuje F0 — bez edycji pola zakres zostaje co do ułamka, gripy bez drgnięcia)
        private double _origSpan = double.NaN;
        private string _origSpanText;

        private bool TryGetSpan(out double span)
        {
            if (_origSpanText != null && SpanBox.Text == _origSpanText && !double.IsNaN(_origSpan)) { span = _origSpan; return true; }
            return NumberParser.TryParseDouble(SpanBox.Text, out span);
        }

        private double CurrentSpan()
            => TryGetSpan(out var sp) && sp >= 0 ? sp : _prevBarsSpan;

        private static int FitCount(double span, double spacing)
            => spacing > 0 ? Math.Max(1, (int)Math.Floor(span / spacing + 1e-9) + 1) : 1;

        private void OnCountChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressRecalc) return;
            if (!int.TryParse(CountBox.Text, out int newCount) || newCount < 1)
            { RestartPreviewTimer(); return; }

            if (!NumberParser.TryParseDouble(SpacingBox.Text, out var spacing) || spacing <= 0)
            { _prevCount = newCount; RestartPreviewTimer(); return; }

            double span = CurrentSpan();
            if (newCount > 1 && (newCount - 1) * spacing > span + 1e-6)
            {
                // nie poszerzamy zakresu — zagęszczamy (rozstaw w dół do 5 mm)
                double sp = Math.Floor(span / (newCount - 1) / 5.0 + 1e-9) * 5.0;
                if (sp <= 0) { newCount = FitCount(span, spacing); sp = spacing; }
                _suppressRecalc = true;
                SpacingBox.Text = sp.ToString("F0", CultureInfo.InvariantCulture);
                CountBox.Text   = newCount.ToString(CultureInfo.InvariantCulture);
                _suppressRecalc = false;
                _prevSpacing = sp;
            }

            _prevCount = newCount;
            RestartPreviewTimer();
        }

        private void OnSpacingChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressRecalc) return;
            if (!NumberParser.TryParseDouble(SpacingBox.Text, out var newSpacing) || newSpacing <= 0)
            { RestartPreviewTimer(); return; }

            // liczba = ile się zmieści w niezmienionym zakresie
            int fit = FitCount(CurrentSpan(), newSpacing);
            _suppressRecalc = true;
            CountBox.Text = fit.ToString(CultureInfo.InvariantCulture);
            _suppressRecalc = false;
            _prevCount = fit;

            _prevSpacing = newSpacing;
            RestartPreviewTimer();
        }

        private void OnSpanChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressRecalc) return;
            if (!NumberParser.TryParseDouble(SpanBox.Text, out var newSpan) || newSpan < 0)
            { RestartPreviewTimer(); return; }

            // 2026-10-13: nowy zakres → liczba = ile się zmieści przy obecnym rozstawie (w obie strony)
            if (NumberParser.TryParseDouble(SpacingBox.Text, out var spacing) && spacing > 0)
            {
                int fit = FitCount(newSpan, spacing);
                _suppressRecalc = true;
                CountBox.Text = fit.ToString(CultureInfo.InvariantCulture);
                _suppressRecalc = false;
                _prevCount = fit;
            }

            _prevBarsSpan = newSpan;
            RestartPreviewTimer();
        }

        private void OnAnyTextChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressRecalc) return;
            RestartPreviewTimer();
        }

        private void RestartPreviewTimer()
        {
            _previewTimer.Stop();
            _previewTimer.Start();
        }

        private void PreviewTimer_Tick(object sender, EventArgs e)
        {
            _previewTimer.Stop();
            if (!int.TryParse(CountBox.Text, out int count) || count < 1) return;
            if (!NumberParser.TryParseDouble(SpacingBox.Text, out var spacing) || spacing <= 0) return;
            if (!NumberParser.TryParseDouble(CoverBox.Text, out var cover))   return;
            if (!TryGetSpan(out var span))    return;
            if (count > 1 && (count - 1) * spacing > span + 1e-6) count = FitCount(span, spacing);   // zakres bez zmian
            PreviewApplied = true;
            _onPreview?.Invoke(count, spacing, cover, span);
        }

        void OK_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(CountBox.Text, out int count) || count < 1)
            { MessageBox.Show("Liczba prętów musi być ≥ 1", "Edycja rozkładu"); return; }
            if (!NumberParser.TryParseDouble(SpacingBox.Text, out var spacing) || spacing <= 0)
            { MessageBox.Show("Rozstaw musi być > 0", "Edycja rozkładu"); return; }
            if (!NumberParser.TryParseDouble(CoverBox.Text, out var cover) || cover < 0)
            { MessageBox.Show("Otulina ≥ 0", "Edycja rozkładu"); return; }
            if (!TryGetSpan(out var span) || span < 0)
            { MessageBox.Show("Rozpiętość ≥ 0", "Edycja rozkładu"); return; }

            // 2026-10-13: zakres bez zmian — za dużo prętów → tyle, ile się zmieści
            if (count > 1 && (count - 1) * spacing > span + 1e-6)
                count = FitCount(span, spacing);

            ResultCount          = count;
            ResultSpacing        = spacing;
            ResultCover          = cover;
            ResultBarsSpan       = span;
            ResultRebuildLeader  = CbRebuildLeader?.IsChecked == true;
            ResultViewingDirection = (ViewDirBox.SelectedItem as ComboBoxItem)?.Content as string ?? "Auto";

            DialogResult = true;
        }

        void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
