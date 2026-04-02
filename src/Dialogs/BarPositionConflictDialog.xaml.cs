using System.Windows;

namespace BricsCadRc.Dialogs
{
    public partial class BarPositionConflictDialog : Window
    {
        public int ResultPosNr { get; private set; }

        public BarPositionConflictDialog(int requestedNr, int suggestedFreeNr)
        {
            InitializeComponent();
            MessageLabel.Text = $"Numer pozycji {requestedNr:D2} jest już zajęty.\nProponowany wolny numer: {suggestedFreeNr:D2}";
            PosNrBox.Text     = suggestedFreeNr.ToString("D2");
            ResultPosNr       = suggestedFreeNr;
        }

        void OK_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PosNrBox.Text, out int nr) || nr < 1 || nr > 99)
            { MessageBox.Show("Numer pozycji musi być liczbą 1–99."); return; }
            ResultPosNr  = nr;
            DialogResult = true;
        }

        void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
