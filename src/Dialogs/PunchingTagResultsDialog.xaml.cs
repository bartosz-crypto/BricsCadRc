using System.Collections.Generic;
using System.Text;
using System.Windows;

namespace BricsCadRc.Dialogs
{
    public partial class PunchingTagResultsDialog : Window
    {
        private readonly string _summaryText;
        private readonly List<string> _warningLines;

        public PunchingTagResultsDialog(string summary, List<string> warnings)
        {
            InitializeComponent();
            _summaryText  = summary ?? string.Empty;
            _warningLines = warnings ?? new List<string>();

            SummaryBlock.Text        = _summaryText;
            WarningsHeader.Text      = $"Warnings ({_warningLines.Count}):";
            WarningsList.ItemsSource = _warningLines.Count > 0
                ? (IEnumerable<string>)_warningLines
                : new[] { "(no warnings)" };
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            var sb = new StringBuilder();
            sb.AppendLine(_summaryText);
            sb.AppendLine();
            sb.AppendLine($"Warnings ({_warningLines.Count}):");
            foreach (var w in _warningLines)
                sb.AppendLine("  " + w);

            try
            {
                Clipboard.SetText(sb.ToString());
                CopyButton.Content = "Copied!";
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = System.TimeSpan.FromSeconds(1.5)
                };
                timer.Tick += (s, args) =>
                {
                    CopyButton.Content = "Copy to clipboard";
                    timer.Stop();
                };
                timer.Start();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Clipboard copy failed: " + ex.Message,
                                "Error", MessageBoxButton.OK,
                                MessageBoxImage.Warning);
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
