using System.Collections.Generic;
using System.Text;
using System.Windows;
using BricsCadRc.Core;

namespace BricsCadRc.Dialogs
{
    public partial class PunchingTagDialog : Window
    {
        public string SelectedPath { get; private set; }
        public PunchingTagEngine.SourceData Source { get; private set; }
        public PunchingTagEngine.MappingResult Mapping { get; private set; }

        public PunchingTagDialog()
        {
            InitializeComponent();
        }

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title  = "Select source punching analysis file",
                Filter = "CAD Files|*.dwg;*.dxf|" +
                         "DWG (*.dwg)|*.dwg|" +
                         "DXF (*.dxf)|*.dxf"
            };
            if (dlg.ShowDialog() != true) return;

            PathBox.Text       = dlg.FileName;
            OkButton.IsEnabled = false;
            SelectedPath       = null;
            Source             = null;
            Mapping            = null;

            try
            {
                PreviewBlock.Foreground = System.Windows.Media.Brushes.Gray;
                PreviewBlock.Text = "Reading source...";
                // Flush UI before the potentially slow ReadSource call.
                Dispatcher.Invoke(new System.Action(() => { }),
                    System.Windows.Threading.DispatcherPriority.Background);

                var src = PunchingTagEngine.ReadSource(dlg.FileName);
                var map = PunchingTagEngine.BuildMapping(src);

                var sb = new StringBuilder();
                sb.AppendLine($"{src.Circles.Count} piles, " +
                              $"{src.Ids.Count} pile-id texts, " +
                              $"{src.PhLabels.Count} PH labels found.");
                sb.AppendLine($"Mappings: {map.Mapped} piles -> PH, " +
                              $"{map.Skipped} skipped (no PH match).");

                var hist = new SortedDictionary<string, int>();
                foreach (var kv in map.PileIdToPh)
                {
                    int cur;
                    hist.TryGetValue(kv.Value, out cur);
                    hist[kv.Value] = cur + 1;
                }
                if (hist.Count > 0)
                {
                    var parts = new List<string>();
                    foreach (var kv in hist)
                        parts.Add($"{kv.Key}={kv.Value}");
                    sb.AppendLine("Distribution: " + string.Join(", ", parts));
                }

                if (map.Warnings.Count > 0)
                    sb.AppendLine($"Warnings: {map.Warnings.Count} " +
                                  "(see command line after OK).");

                PreviewBlock.Text = sb.ToString().TrimEnd();
                PreviewBlock.Foreground = System.Windows.Media.Brushes.Black;

                SelectedPath       = dlg.FileName;
                Source             = src;
                Mapping            = map;
                OkButton.IsEnabled = true;
            }
            catch (System.Exception ex)
            {
                PreviewBlock.Text = "Error reading source: " + ex.Message;
                PreviewBlock.Foreground = System.Windows.Media.Brushes.Red;
                OkButton.IsEnabled = false;
            }
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
