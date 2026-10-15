using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BricsCadRc.Dialogs
{
    /// <summary>RC_PRZYGOTUJ_GA (2026-10-15): inicjały „Drawn” / „Checked” do arkusza RC (A1-BL i tabela rewizji By / Chkd).</summary>
    public class SheetPeopleDialog : Window
    {
        private readonly TextBox _drawn, _checked;
        public string Drawn => (_drawn.Text ?? "").Trim().ToUpperInvariant();
        public string Checked => (_checked.Text ?? "").Trim().ToUpperInvariant();

        public SheetPeopleDialog(string drawn, string chk)
        {
            Title = "Arkusz RC — Drawn / Checked";
            SizeToContent = SizeToContent.WidthAndHeight;
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var grid = new Grid { Margin = new Thickness(14) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            for (int i = 0; i < 4; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.Children.Add(Cell(new TextBlock
            {
                Text = "Inicjały do bloku tytułowego i tabeli rewizji (By / Chkd).\nPuste pole — bez zmian (jak w szablonie).",
                Margin = new Thickness(0, 0, 0, 10)
            }, 0, 0, 2));
            grid.Children.Add(Cell(new TextBlock { Text = "Drawn:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 6) }, 1, 0));
            _drawn = new TextBox { Text = drawn ?? "", CharacterCasing = CharacterCasing.Upper, Margin = new Thickness(0, 0, 0, 6), Height = 24 };
            grid.Children.Add(Cell(_drawn, 1, 1));
            grid.Children.Add(Cell(new TextBlock { Text = "Checked:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 6) }, 2, 0));
            _checked = new TextBox { Text = chk ?? "", CharacterCasing = CharacterCasing.Upper, Margin = new Thickness(0, 0, 0, 6), Height = 24 };
            grid.Children.Add(Cell(_checked, 2, 1));

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            var ok = new Button { Content = "OK", MinWidth = 90, Height = 28, IsDefault = true, Margin = new Thickness(0, 0, 8, 0) };
            var cancel = new Button { Content = "Anuluj", MinWidth = 90, Height = 28, IsCancel = true };
            ok.Click += (s, e) => DialogResult = true;
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            grid.Children.Add(Cell(buttons, 3, 0, 2));

            Content = grid;
            Loaded += (s, e) => { _drawn.Focus(); _drawn.SelectAll(); Keyboard.Focus(_drawn); };
        }

        private static UIElement Cell(UIElement e, int row, int col, int span = 1)
        {
            Grid.SetRow(e, row); Grid.SetColumn(e, col);
            if (span > 1) Grid.SetColumnSpan(e, span);
            return e;
        }
    }
}
