using System.Windows;
using System.Windows.Controls;

namespace BricsCadRc.Dialogs
{
    /// <summary>
    /// Dialog "Reinforcement detailing" (FLOW 2, krok 2).
    /// Method: Module / Any; Viewing direction: Auto / Manual.
    /// </summary>
    public partial class ReinfDetailingDialog : Window
    {
        /// <summary>True gdy użytkownik wybrał "Manual".</summary>
        public bool IsManualViewingDirection
            => (CbViewingDirection.SelectedItem as ComboBoxItem)?.Content as string == "Manual";

        /// <summary>
        /// True gdy Method = „Any” (decyzja 2026-10-14, jak w ASD): jeden pręt (cały kształt, wypełniony) w miejscu wskazanym,
        /// liczba prętów i rozstaw tylko w opisie / zestawieniu.
        /// </summary>
        public bool IsAnyMethod
            => (CbMethod?.SelectedItem as ComboBoxItem)?.Content as string == "Any";

        public ReinfDetailingDialog(string initialViewingDirection = "Auto")
        {
            InitializeComponent();
            CbViewingDirection.SelectedIndex =
                initialViewingDirection == "Manual" ? 1 : 0;
        }

        // Any: kierunek widoku nie dotyczy (rysowany cały kształt pręta)
        private void CbMethod_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CbViewingDirection != null && CbMethod != null) CbViewingDirection.IsEnabled = !IsAnyMethod;
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
