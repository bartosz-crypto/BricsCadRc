using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BricsCadRc.Dialogs
{
    /// <summary>Wybór płyty (PLOT) z raportu przebicia — jak PlotPicker w AsdRcSlab.</summary>
    public class PunchingPlotPickerDialog : Window
    {
        private readonly ListBox _list;
        public int SelectedIndex => _list.SelectedIndex;

        public PunchingPlotPickerDialog(IList<string> items, int defaultIndex,
                                        string title = null, string infoText = null)
        {
            Title = title ?? "Przebicie — wybierz płytę (PLOT)";
            Width = 640; Height = 420;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var root = new DockPanel { Margin = new Thickness(10) };
            var info = new TextBlock
            {
                Text = infoText ?? "Płyty z raportu. Podświetlona: najwięcej pali z podpisem (P617 …) na rysunku.",
                Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap
            };
            DockPanel.SetDock(info, Dock.Top);
            root.Children.Add(info);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                                           Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = "OK", Width = 90, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Anuluj", Width = 90, IsCancel = true };
            ok.Click += (s, e) => { if (_list.SelectedIndex >= 0) DialogResult = true; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            _list = new ListBox { ItemsSource = items, FontFamily = new System.Windows.Media.FontFamily("Consolas") };
            _list.MouseDoubleClick += (s, e) => { if (_list.SelectedIndex >= 0) DialogResult = true; };
            root.Children.Add(_list);

            Content = root;
            Loaded += (s, e) =>
            {
                if (defaultIndex >= 0 && defaultIndex < items.Count)
                {
                    _list.SelectedIndex = defaultIndex;
                    _list.ScrollIntoView(_list.SelectedItem);
                }
                _list.Focus();
                Keyboard.Focus(_list);
            };
        }
    }
}
