using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BricsCadRc.Dialogs
{
    /// <summary>Wybór płyty (PLOT / GARAGE) z raportu przebicia — jak PlotPicker w AsdRcSlab.</summary>
    public class PunchingPlotPickerDialog : Window
    {
        private readonly ListBox _list;
        public int SelectedIndex => _list.SelectedIndex;
        /// <summary>Wybór wielu pozycji (multiSelect — prośba Bartka 2026-10-26: Prepare GA kilku płyt naraz), w kolejności listy.</summary>
        public List<int> SelectedIndices
        {
            get
            {
                var res = new List<int>();
                for (int i = 0; i < _list.Items.Count; i++) if (_list.SelectedItems.Contains(_list.Items[i])) res.Add(i);
                return res;
            }
        }

        public PunchingPlotPickerDialog(IList<string> items, int defaultIndex,
                                        string title = null, string infoText = null, bool multiSelect = false)
        {
            Title = title ?? "Przebicie — wybierz płytę (PLOT / GARAGE)";
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
            if (multiSelect)
            {
                var all = new Button { Content = "Zaznacz wszystkie", Width = 130, Margin = new Thickness(0, 0, 8, 0) };
                all.Click += (s, e) => { _list.SelectAll(); _list.Focus(); };
                buttons.Children.Add(all);
            }
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            _list = new ListBox { ItemsSource = items, FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                                  SelectionMode = multiSelect ? SelectionMode.Extended : SelectionMode.Single };
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
