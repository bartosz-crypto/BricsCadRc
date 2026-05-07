using System.Windows;
using BricsCadRc.Core;

namespace BricsCadRc.Dialogs
{
    public partial class PunchingSummaryDialog : Window
    {
        public int                          Count501 { get; private set; }
        public int                          Count502 { get; private set; }
        public PunchingTagEngine.PhCountTotals Totals { get; private set; }

        public bool Wants501 => Count501 > 0;
        public bool Wants502 => Count502 > 0;

        public PunchingSummaryDialog(PunchingTagEngine.PhCountTotals totals)
        {
            InitializeComponent();
            Totals   = totals;
            Count501 = totals.TotalForPos501;
            Count502 = totals.TotalForPos502;
            Populate();
        }

        private void Populate()
        {
            int ph(string z) => Totals.PerPh.TryGetValue(z, out int n) ? n : 0;

            PhCountsBlock.Text =
                $"PH1: {ph("PH1"),-6}  PH4: {ph("PH4"),-6}  PH7: {ph("PH7")}\n" +
                $"PH2: {ph("PH2"),-6}  PH5: {ph("PH5"),-6}  PH8: {ph("PH8")}\n" +
                $"PH3: {ph("PH3"),-6}  PH6: {ph("PH6"),-6}  PH9: {ph("PH9")}";

            string s501 = Count501 > 0
                ? $"Poz. 501: H12 × 2250mm × {Count501}"
                : "Poz. 501: (skip — 0 piles)";
            string s502 = Count502 > 0
                ? $"Poz. 502: H16 × 2500mm × {Count502}"
                : "Poz. 502: (skip — 0 piles)";
            SummaryBlock.Text = s501 + "\n" + s502;

            bool anyActive = Count501 > 0 || Count502 > 0;
            OkButton.IsEnabled = anyActive;
            if (!anyActive)
            {
                SummaryBlock.Text     += "\n\nNo PH zones found — run RC_PUNCHING_TAG first.";
                SummaryBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
            => DialogResult = true;

        private void CancelButton_Click(object sender, RoutedEventArgs e)
            => DialogResult = false;
    }
}
