using System.Windows;

namespace BricsCadRc.Dialogs
{
    public partial class BarPositionConflictDialog : Window
    {
        public int ResultPosNr { get; private set; }

        /// <param name="usedBy">Opcjonalnie: numery prętów, które już mają ten numer (RC_EDIT_BAR, inny kształt / wymiary).</param>
        public BarPositionConflictDialog(int requestedNr, int suggestedFreeNr, string usedBy = null)
        {
            InitializeComponent();
            MessageLabel.Text = string.IsNullOrEmpty(usedBy)
                ? $"Numer pozycji {requestedNr:D2} jest już zajęty.\nProponowany wolny numer: {suggestedFreeNr:D2}"
                : $"Numer pozycji {requestedNr:D2} ma już pręt o innym kształcie / wymiarach ({usedBy}).\n" +
                  $"Jeden numer = jeden kształt.\nProponowany wolny numer: {suggestedFreeNr:D2}";
            PosNrBox.Text     = suggestedFreeNr.ToString("D2");
            ResultPosNr       = suggestedFreeNr;
        }

        void OK_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(PosNrBox.Text, out int nr) || nr < 1)
            { MessageBox.Show("Numer pozycji musi być >= 1."); return; }
            ResultPosNr  = nr;
            DialogResult = true;
        }

        void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
