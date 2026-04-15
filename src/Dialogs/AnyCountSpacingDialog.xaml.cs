using System.Windows;

namespace BricsCadRc.Dialogs
{
    /// <summary>
    /// Dialog dla trybu ViewingDirection="Any" — użytkownik podaje Count i Spacing
    /// używane wyłącznie do etykiety (MLeader). Fizycznie wstawiany jest 1 kształt pręta.
    /// </summary>
    public partial class AnyCountSpacingDialog : Window
    {
        public int    ResultCount   { get; private set; } = 1;
        public double ResultSpacing { get; private set; } = 200.0;

        public AnyCountSpacingDialog(int defaultCount = 1, double defaultSpacing = 200.0)
        {
            InitializeComponent();
            CountBox.Text   = defaultCount.ToString();
            SpacingBox.Text = ((int)defaultSpacing).ToString();
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(CountBox.Text.Trim(), out int c) || c < 1)
            {
                MessageBox.Show("Count must be a positive integer.", "Invalid input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!double.TryParse(SpacingBox.Text.Trim(), out double s) || s <= 0)
            {
                MessageBox.Show("Spacing must be a positive number.", "Invalid input",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ResultCount   = c;
            ResultSpacing = s;
            DialogResult  = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
