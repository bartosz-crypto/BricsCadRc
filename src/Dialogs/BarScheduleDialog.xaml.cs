using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using BricsCadRc.Core;
using Microsoft.Win32;

namespace BricsCadRc.Dialogs
{
    /// <summary>
    /// Dialog zestawienia prętów (BBS) — wyświetla DataGrid z obliczonymi długościami
    /// i masami wg BS 8666:2020.
    /// </summary>
    public partial class BarScheduleDialog : Window
    {
        private readonly List<BarScheduleEntry> _entries;

        public BarScheduleDialog(List<BarScheduleEntry> entries)
        {
            InitializeComponent();
            _entries = entries;

            ScheduleGrid.ItemsSource = _entries;

            double total = BarScheduleEngine.TotalMass(_entries);
            TotalMassText.Text = $"{total:F2} kg";
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Title      = "Export BBS to CSV",
                Filter     = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                DefaultExt = ".csv",
                FileName   = "BarSchedule.csv"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("BAR MARK,TYPE & SIZE,NO. OF MEMBERS,NO. IN EACH,TOTAL NO. BARS,LENGTH PER BAR,CODE,A,B,C,D,E/R");

                foreach (var row in _entries)
                {
                    sb.AppendLine(
                        $"{row.Mark},{row.TypeAndSize},{row.NoOfMembers}," +
                        $"{row.TotalCount},{row.TotalCount},{row.ColLength}," +
                        $"{row.ShapeCode},{row.ColA},{row.ColB},{row.ColC},{row.ColD},{row.ColE}");
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show($"Exported {_entries.Count} rows to:\n{dlg.FileName}",
                    "RC SLAB", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Export failed:\n{ex.Message}",
                    "RC SLAB", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
