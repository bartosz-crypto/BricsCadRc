using System.Windows;
using System.Windows.Controls;

namespace BricsCadRc.Dialogs
{
    /// <summary>
    /// Dialog "Reinforcement detailing" (FLOW 2, krok 2).
    /// Viewing direction: Auto / Manual.
    /// </summary>
    public partial class ReinfDetailingDialog : Window
    {
        /// <summary>True gdy użytkownik wybrał "Manual".</summary>
        public bool IsManualViewingDirection
            => (CbViewingDirection.SelectedItem as ComboBoxItem)?.Content as string == "Manual";

        public ReinfDetailingDialog(string initialViewingDirection = "Auto")
        {
            InitializeComponent();
            CbViewingDirection.SelectedIndex =
                initialViewingDirection == "Manual" ? 1 : 0;
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
