using System.Windows;
using System.Windows.Controls;

namespace BricsCadRc.Dialogs
{
    /// <summary>
    /// Dialog "Reinforcement detailing" (FLOW 2, krok 2).
    /// Viewing direction: Auto / Manual / Any.
    /// </summary>
    public partial class ReinfDetailingDialog : Window
    {
        /// <summary>True gdy użytkownik wybrał "Manual".</summary>
        public bool IsManualViewingDirection
            => (CbViewingDirection.SelectedItem as ComboBoxItem)?.Content as string == "Manual";

        /// <summary>True gdy użytkownik wybrał "Any" — wstaw pojedynczą kopię pręta jako widok legendy.</summary>
        public bool IsAnyViewingDirection
            => (CbViewingDirection.SelectedItem as ComboBoxItem)?.Content as string == "Any";

        public ReinfDetailingDialog(string initialViewingDirection = "Auto")
        {
            InitializeComponent();
            CbViewingDirection.SelectedIndex =
                initialViewingDirection == "Manual" ? 1 :
                initialViewingDirection == "Any"    ? 2 : 0;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
