using System.Windows;

namespace BricsCadRc.Dialogs
{
    /// <summary>
    /// Dialog "Reinforcement detailing" (FLOW 2, krok 2).
    /// Pokazuje statyczne ustawienia: Type=Linear, Method=Module, Direction=Top.
    /// Zawsze te same — uzytkownik klika OK lub Cancel.
    /// </summary>
    public partial class ReinfDetailingDialog : Window
    {
        public ReinfDetailingDialog()
        {
            InitializeComponent();
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
