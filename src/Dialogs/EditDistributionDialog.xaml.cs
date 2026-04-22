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

        private void OnCountChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressRecalc) return;
            if (!int.TryParse(CountBox.Text, out int newCount) || newCount < 1)
            { RestartPreviewTimer(); return; }

            if (!double.TryParse(SpacingBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var spacing) || spacing <= 0)
            { _prevCount = newCount; RestartPreviewTimer(); return; }

            if (newCount > _prevCount)
            {
                double needed = (newCount - 1) * spacing;
                if (!double.TryParse(SpanBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var currSpan))
                    currSpan = 0;
                if (needed > currSpan)
                {
                    _suppressRecalc = true;
                    SpanBox.Text = needed.ToString("F0", CultureInfo.InvariantCulture);
                    _suppressRecalc = false;
                    _prevBarsSpan = needed;
                }
            }

            _prevCount = newCount;
            RestartPreviewTimer();
        }

        private void OnSpacingChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressRecalc) return;
            if (!double.TryParse(SpacingBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var newSpacing) || newSpacing <= 0)
            { RestartPreviewTimer(); return; }

            if (!int.TryParse(CountBox.Text, out int count))
            { _prevSpacing = newSpacing; RestartPreviewTimer(); return; }

            if (newSpacing > _prevSpacing)
            {
                double needed = (count - 1) * newSpacing;
                if (!double.TryParse(SpanBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var currSpan))
                    currSpan = 0;
                if (needed > currSpan)
                {
                    _suppressRecalc = true;
                    SpanBox.Text = needed.ToString("F0", CultureInfo.InvariantCulture);
                    _suppressRecalc = false;
                    _prevBarsSpan = needed;
                }
            }

            _prevSpacing = newSpacing;
            RestartPreviewTimer();
        }

        private void OnSpanChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressRecalc) return;
            if (!double.TryParse(SpanBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var newSpan) || newSpan < 0)
            { RestartPreviewTimer(); return; }

            if (newSpan < _prevBarsSpan)
            {
                if (!int.TryParse(CountBox.Text, out int count))
                { _prevBarsSpan = newSpan; RestartPreviewTimer(); return; }
                if (!double.TryParse(SpacingBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var spacing) || spacing <= 0)
                { _prevBarsSpan = newSpan; RestartPreviewTimer(); return; }

                int newCount = Math.Max(1, (int)(newSpan / spacing) + 1);
                if (newCount < count)
                {
                    _suppressRecalc = true;
                    CountBox.Text = newCount.ToString(CultureInfo.InvariantCulture);
                    _suppressRecalc = false;
                    _prevCount = newCount;
                }
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
            if (!double.TryParse(SpacingBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var spacing) || spacing <= 0) return;
            if (!double.TryParse(CoverBox.Text,   NumberStyles.Float, CultureInfo.InvariantCulture, out var cover))   return;
            if (!double.TryParse(SpanBox.Text,    NumberStyles.Float, CultureInfo.InvariantCulture, out var span))    return;
            PreviewApplied = true;
            _onPreview?.Invoke(count, spacing, cover, span);
        }

        void OK_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(CountBox.Text, out int count) || count < 1)
            { MessageBox.Show("Liczba prętów musi być ≥ 1", "Edycja rozkładu"); return; }
            if (!double.TryParse(SpacingBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var spacing) || spacing <= 0)
            { MessageBox.Show("Rozstaw musi być > 0", "Edycja rozkładu"); return; }
            if (!double.TryParse(CoverBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var cover) || cover < 0)
            { MessageBox.Show("Otulina ≥ 0", "Edycja rozkładu"); return; }
            if (!double.TryParse(SpanBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var span) || span < 0)
            { MessageBox.Show("Rozpiętość ≥ 0", "Edycja rozkładu"); return; }

            if ((count - 1) * spacing > span)
                span = (count - 1) * spacing;

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
