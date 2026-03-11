using System.Windows;
using System.Windows.Controls;
using BricsCadRc.Core;
using BricsCadRc.ShapeCodes;

namespace BricsCadRc.Dialogs
{
    /// <summary>
    /// Dialog "Reinforcement – Elevation" (FLOW 1, krok 1).
    /// Uzytkownik wybiera srednice, kod ksztaltu i wymiary preta.
    /// </summary>
    public partial class BarElevationDialog : Window
    {
        public BarData Result { get; private set; }

        public BarElevationDialog()
        {
            InitializeComponent();
            PopulateShapeCodes();
        }

        private void PopulateShapeCodes()
        {
            foreach (var sc in ShapeCodeLibrary.All)
            {
                var item = new ComboBoxItem
                {
                    Content = $"{sc.Code}  –  {sc.Description}",
                    Tag     = sc.Code
                };
                ShapeCombo.Items.Add(item);
            }
            ShapeCombo.SelectedIndex = 0;
        }

        private void ShapeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ShapeCombo.SelectedItem == null) return;
            var code = (string)((ComboBoxItem)ShapeCombo.SelectedItem).Tag;
            var sc   = ShapeCodeLibrary.GetByCode(code);
            if (sc == null) return;

            RowB.Visibility = sc.NeedsB ? Visibility.Visible : Visibility.Collapsed;
            RowC.Visibility = sc.NeedsC ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(ParamABox.Text, out double a) || a <= 0)
            {
                MessageBox.Show("Invalid value for A.", "RC SLAB",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            double b = 0, c = 0;

            if (RowB.Visibility == Visibility.Visible)
            {
                if (!double.TryParse(ParamBBox.Text, out b) || b <= 0)
                {
                    MessageBox.Show("Invalid value for B.", "RC SLAB",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            if (RowC.Visibility == Visibility.Visible)
            {
                if (!double.TryParse(ParamCBox.Text, out c) || c <= 0)
                {
                    MessageBox.Show("Invalid value for C.", "RC SLAB",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            int    diameter  = int.Parse((string)((ComboBoxItem)DiameterCombo.SelectedItem).Tag);
            string shapeCode = (string)((ComboBoxItem)ShapeCombo.SelectedItem).Tag;
            string layerCode = (string)((ComboBoxItem)LayerCombo.SelectedItem).Tag;

            Result = new BarData
            {
                Diameter  = diameter,
                ShapeCode = shapeCode,
                LengthA   = a,
                LengthB   = b,
                LengthC   = c,
                LayerCode = layerCode,
                Position  = layerCode.StartsWith("B") ? "BOT" : "TOP",
                Direction = (layerCode == "B1" || layerCode == "T1") ? "X" : "Y"
            };

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
