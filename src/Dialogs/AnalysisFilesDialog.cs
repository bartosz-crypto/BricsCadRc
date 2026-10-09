using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace BricsCadRc.Dialogs
{
    /// <summary>
    /// RC_IMPORT_ANALYSIS — wybór plików z analizy: mapy zbrojenia (…_punching_reinf_maps.dxf / dwg)
    /// i raport przebicia (report_punching.xlsx). Wystarczy wskazać jeden — drugi jest szukany w tym samym folderze.
    /// Jedno z pól może zostać puste (wtedy ten krok jest pomijany).
    /// </summary>
    public class AnalysisFilesDialog : Window
    {
        private readonly TextBox _maps, _report;
        private CheckBox _useMaps, _useReport;
        /// <summary>Ścieżka map — pusta, gdy mapy odznaczone.</summary>
        public string MapsPath   => _useMaps.IsChecked == true ? _maps.Text.Trim() : "";
        /// <summary>Ścieżka raportu — pusta, gdy raport odznaczony.</summary>
        public string ReportPath => _useReport.IsChecked == true ? _report.Text.Trim() : "";

        public AnalysisFilesDialog(string mapsPath, string reportPath)
        {
            Title = "Import analizy — mapy zbrojenia + przebicie";
            Width = 700; SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.NoResize;

            var root = new StackPanel { Margin = new Thickness(12) };
            root.Children.Add(new TextBlock
            {
                Text = "Mapy zbrojenia są wklejane od razu (wybór płyty i miejsca). Raport przebicia zostaje zapamiętany "
                     + "w rysunku razem z płytą — potem Punching tylko detaluje (tagi PH, szablony detali, pręty 501 / 502) "
                     + "bez pytania o plik. Odznacz to, czego nie chcesz wczytywać (np. same mapy).",
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            });

            _maps = AddRow(root, "Mapy zbrojenia (dxf / dwg)", mapsPath,
                "Mapy zbrojenia (…_punching_reinf_maps.dxf)", "CAD (*.dxf;*.dwg)|*.dxf;*.dwg", out _useMaps);
            _report = AddRow(root, "Raport przebicia (xlsx)", reportPath,
                "Raport przebicia (report_punching.xlsx)", "Excel (*.xlsx)|*.xlsx", out _useReport);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                                           Margin = new Thickness(0, 12, 0, 0) };
            var ok = new Button { Content = "OK", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Anuluj", Width = 90, IsCancel = true };
            ok.Click += (s, e) =>
            {
                bool m = MapsPath.Length > 0, r = ReportPath.Length > 0;
                if (_useMaps.IsChecked == true && !m) { MessageBox.Show("Wskaż plik map albo odznacz mapy.", Title); return; }
                if (_useReport.IsChecked == true && !r) { MessageBox.Show("Wskaż raport przebicia albo go odznacz.", Title); return; }
                if (!m && !r) { MessageBox.Show("Zaznacz przynajmniej jeden plik do wczytania.", Title); return; }
                if (m && !File.Exists(MapsPath)) { MessageBox.Show("Nie ma pliku map:\n" + MapsPath, Title); return; }
                if (r && !File.Exists(ReportPath)) { MessageBox.Show("Nie ma pliku raportu:\n" + ReportPath, Title); return; }
                DialogResult = true;
            };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            root.Children.Add(buttons);
            Content = root;
        }

        private TextBox AddRow(StackPanel root, string label, string value, string dlgTitle, string filter, out CheckBox use)
        {
            var chk = new CheckBox { Content = label, IsChecked = true, Margin = new Thickness(0, 6, 0, 3), FontWeight = FontWeights.SemiBold };
            root.Children.Add(chk);
            use = chk;
            var row = new DockPanel();
            var browse = new Button { Content = "Przeglądaj…", Width = 100, Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(browse, Dock.Right);
            row.Children.Add(browse);
            var box = new TextBox { Text = value ?? "", VerticalContentAlignment = VerticalAlignment.Center, MinHeight = 24 };
            row.Children.Add(box);
            root.Children.Add(row);
            chk.Checked   += (s, e) => row.IsEnabled = true;
            chk.Unchecked += (s, e) => row.IsEnabled = false;
            browse.Click += (s, e) =>
            {
                var fd = new OpenFileDialog { Title = dlgTitle, Filter = filter };
                string start = DirOf(box.Text) ?? DirOf(_maps?.Text) ?? DirOf(_report?.Text);
                if (start != null) fd.InitialDirectory = start;
                if (fd.ShowDialog() != true) return;
                box.Text = fd.FileName;
                FillPartner(box);
            };
            return box;
        }

        private static string DirOf(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path)) return null;
                var dir = Path.GetDirectoryName(path.Trim());
                return !string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir) ? dir : null;
            }
            catch { return null; }
        }

        /// <summary>Drugi plik z tego samego folderu, jeśli jego pole jest puste.</summary>
        private void FillPartner(TextBox chosen)
        {
            string dir = DirOf(chosen.Text);
            if (dir == null) return;
            try
            {
                if (chosen == _maps && _useReport.IsChecked == true && _report.Text.Trim().Length == 0)
                {
                    var hit = System.IO.Directory.GetFiles(dir, "*.xlsx")
                        .Where(f => !Path.GetFileName(f).StartsWith("~$"))
                        .OrderByDescending(f => Path.GetFileName(f).ToLowerInvariant().Contains("report_punching"))
                        .ThenByDescending(f => Path.GetFileName(f).ToLowerInvariant().Contains("punching"))
                        .FirstOrDefault(f => Path.GetFileName(f).ToLowerInvariant().Contains("punching"));
                    if (hit != null) _report.Text = hit;
                }
                else if (chosen == _report && _useMaps.IsChecked == true && _maps.Text.Trim().Length == 0)
                {
                    var hit = System.IO.Directory.GetFiles(dir)
                        .Where(f => f.EndsWith(".dxf", System.StringComparison.OrdinalIgnoreCase)
                                 || f.EndsWith(".dwg", System.StringComparison.OrdinalIgnoreCase))
                        .FirstOrDefault(f => Path.GetFileName(f).ToLowerInvariant().Contains("reinf_maps"));
                    if (hit != null) _maps.Text = hit;
                }
            }
            catch (System.Exception ex) { BricsCadRc.Core.Log.Error("AnalysisFilesDialog.FillPartner", ex); }
        }
    }
}
