using System.Windows;
using System.Windows.Controls;

namespace BricsCadRc.Dialogs
{
    public partial class BarEndStyleDialog : Window
    {
        public string SymbolType      => (CbSymbolType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "None";
        public string SymbolSide      => (CbSide.SelectedItem      as ComboBoxItem)?.Content?.ToString() ?? "Right";
        public string SymbolDirection => (CbHookDir.SelectedItem   as ComboBoxItem)?.Content?.ToString() ?? "Up";

        public BarEndStyleDialog(string symType = "None", string symSide = "Right", string symDir = "Up")
        {
            InitializeComponent();
            SelectItem(CbSymbolType, symType);
            SelectItem(CbSide,       symSide);
            SelectItem(CbHookDir,    symDir);
            UpdateHookDirEnabled();
        }

        private static void SelectItem(ComboBox cb, string value)
        {
            foreach (ComboBoxItem item in cb.Items)
                if (item.Content?.ToString() == value) { cb.SelectedItem = item; return; }
        }

        private void CbSymbolType_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => UpdateHookDirEnabled();

        private void UpdateHookDirEnabled()
        {
            if (CbHookDir == null || TbHookDir == null) return;
            bool isHook = SymbolType == "Hook";
            CbHookDir.IsEnabled = isHook;
            TbHookDir.Opacity   = isHook ? 1.0 : 0.4;
        }

        private void OK_Click(object sender, RoutedEventArgs e)     { DialogResult = true;  Close(); }
        private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; Close(); }
    }
}
