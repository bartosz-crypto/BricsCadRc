using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BricsCadRc.Dialogs
{
    /// <summary>Podgląd tekstu (np. lista zmian) z przyciskami OK / Anuluj.</summary>
    public class TextConfirmDialog : Window
    {
        public TextConfirmDialog(string title, string text, string okText = "OK", string cancelText = "Anuluj")
        {
            Title = title;
            Width = 820; Height = 600; MinWidth = 500; MinHeight = 300;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResizeWithGrip;

            var root = new DockPanel { Margin = new Thickness(10) };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = okText, MinWidth = 110, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = cancelText, MinWidth = 90, Height = 28, IsCancel = true };
            ok.Click += (s, e) => DialogResult = true;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            DockPanel.SetDock(buttons, Dock.Bottom);
            root.Children.Add(buttons);

            root.Children.Add(new TextBox
            {
                Text = text ?? "", IsReadOnly = true, FontFamily = new FontFamily("Consolas"),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
            });
            Content = root;
        }
    }
}
