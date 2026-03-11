using System.Windows;
using BricsCadRc.Core;

namespace BricsCadRc.Dialogs
{
    /// <summary>
    /// Dialog "Reinforcement description" (FLOW 1, krok 4).
    /// Uzytkownik wpisuje numer pozycji preta.
    /// </summary>
    public partial class BarPositionDialog : Window
    {
        public int PositionNumber { get; private set; }

        public BarPositionDialog(BarData bar, int suggestedPosNr)
        {
            InitializeComponent();
            PreviewText.Text = $"H{bar.Diameter}  –  {bar.ShapeCode}";
            PositionBox.Text = suggestedPosNr.ToString();
            PositionBox.SelectAll();
            PositionBox.Focus();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PositionBox.Text, out int nr) || nr <= 0)
            {
                MessageBox.Show("Enter a valid position number (integer > 0).", "RC SLAB",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            PositionNumber = nr;
            DialogResult   = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
