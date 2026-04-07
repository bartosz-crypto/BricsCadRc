using System;
using System.Windows;

namespace BricsCadRc.Dialogs
{
    public partial class EditDistributionDialog : Window
    {
        public int    ResultCount   { get; private set; }
        public double ResultSpacing { get; private set; }
        public double ResultCover   { get; private set; }

        public Action<int, double, double> OnPreview    { get; set; }
        public bool                        PreviewApplied { get; private set; } = false;
        public bool ResultAddAnnotation => CbAddAnnotation.IsChecked == true;

        private System.Windows.Threading.DispatcherTimer _debounce;

        public EditDistributionDialog(string mark, int count, double spacing, double cover,
            bool annotMissing = false)
        {
            InitializeComponent();
            MarkLabel.Text  = mark;
            CountBox.Text   = count.ToString();
            SpacingBox.Text = spacing.ToString("F0");
            CoverBox.Text   = cover.ToString("F0");
            UpdateSpan();
            CountBox.TextChanged   += (s, e) => { UpdateSpan(); SchedulePreview(); };
            SpacingBox.TextChanged += (s, e) => { UpdateSpan(); SchedulePreview(); };
            CoverBox.TextChanged   += (s, e) => SchedulePreview();

            if (annotMissing)
                CbAddAnnotation.Visibility = Visibility.Visible;
        }

        void UpdateSpan()
        {
            if (int.TryParse(CountBox.Text, out int c)
                && double.TryParse(SpacingBox.Text, out double sp) && c > 0 && sp > 0)
                SpanLabel.Text = $"{(c - 1) * sp:F0}";
            else
                SpanLabel.Text = "—";
        }

        void Preview_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(CountBox.Text, out int c) || c < 1) return;
            if (!double.TryParse(SpacingBox.Text, out double sp) || sp <= 0) return;
            if (!double.TryParse(CoverBox.Text, out double cv) || cv < 0) return;

            PreviewApplied = true;
            OnPreview?.Invoke(c, sp, cv);
        }

        void SchedulePreview()
        {
            _debounce?.Stop();
            _debounce = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromMilliseconds(600) };
            _debounce.Tick += (s, e) =>
            {
                _debounce.Stop();
                Preview_Click(s, null!);
            };
            _debounce.Start();
        }

        void OK_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(CountBox.Text, out int c) || c < 1)
            { MessageBox.Show("Nieprawidłowa liczba prętów."); return; }
            if (!double.TryParse(SpacingBox.Text, out double sp) || sp <= 0)
            { MessageBox.Show("Nieprawidłowy rozstaw."); return; }
            if (!double.TryParse(CoverBox.Text, out double cv) || cv < 0)
            { MessageBox.Show("Nieprawidłowa otulina."); return; }

            ResultCount   = c;
            ResultSpacing = sp;
            ResultCover   = cv;
            DialogResult  = true;
        }

        void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
