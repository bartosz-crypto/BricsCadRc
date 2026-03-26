using System;
using System.Windows;
using BricsCadRc.Core;
using Teigha.DatabaseServices;
using Teigha.Runtime;

namespace BricsCadRc.Dialogs
{
    /// <summary>
    /// Dialog "Reinforcement description" — pokazuje i pozwala edytowac
    /// wlasciwosci wybranej annotacji / grupy pretow RC SLAB.
    /// Odpowiednik pop-up z ASD.
    /// </summary>
    public partial class BarPropertiesDialog : Window
    {
        private BarData _bar;
        private ObjectId _annotId;
        private Database _db;

        public BarPropertiesDialog(BarData bar, ObjectId annotId, Database db)
        {
            InitializeComponent();
            _bar     = bar;
            _annotId = annotId;
            _db      = db;

            Populate();
        }

        private void Populate()
        {
            // Pasek opisu
            DescriptionText.Text = $"{_bar.Count} {_bar.Mark} {_bar.LayerCode}";

            // Numer pozycji (wyciagamy z marka: H12-01-200 → "01")
            PositionBox.Text = ExtractPositionNr(_bar.Mark);

            // Wlasciwosci
            CountText.Text     = $"{_bar.Count} szt";
            DiameterText.Text  = $"Ø{_bar.Diameter} mm";
            SpacingBox.Text    = $"{(int)_bar.Spacing}";
            LengthText.Text    = _bar.LengthA > 0 ? $"{_bar.LengthA:F0}" : "—";
            MassText.Text      = _bar.LengthA > 0
                ? $"{_bar.Count * (_bar.LengthA / 1000.0) * BarData.GetLinearMass(_bar.Diameter):F2} kg"
                : "—";

            PositionText.Text  = _bar.Position == "BOT" ? "Bottom" : "Top";
            DirectionText.Text = _bar.Direction == "X" ? "X (poziome)" : "Y (pionowe)";
            LayerCodeText.Text = _bar.LayerCode;
            LayerNameText.Text = LayerManager.GetLayerName(_bar.LayerCode);
            ShapeCodeText.Text = _bar.ShapeCode;
        }

        private static string ExtractPositionNr(string mark)
        {
            // Mark format: H12-01-200  → zwroc "01"
            var parts = mark.Split('-');
            return parts.Length >= 2 ? parts[1] : "—";
        }

        // ----------------------------------------------------------------
        // Zdarzenia
        // ----------------------------------------------------------------

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(SpacingBox.Text, out int newSpacing) || newSpacing <= 0)
            {
                MessageBox.Show("Nieprawidlowy rozstaw.", "RC SLAB", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Zapisz zmieniony rozstaw do XData annotacji
            try
            {
                _bar.Spacing = newSpacing;

                using var tr = _db.TransactionManager.StartTransaction();
                var entity = (Entity)tr.GetObject(_annotId, OpenMode.ForWrite);

                // Aktualizuj tekst annotacji
                if (entity is DBText dbText)
                {
                    dbText.TextString = $"{_bar.Count} {_bar.Mark}";
                }

                // Aktualizuj XData annotacji
                AnnotationEngine.EnsureAppIdRegistered(_db);
                var xdata = entity.GetXDataForApplication(AnnotationEngine.AnnotAppName);
                if (xdata != null)
                {
                    // Odczytaj i zaktualizuj spacing
                    var vals = xdata.AsArray();
                    if (vals.Length >= 6)
                    {
                        vals[5] = new TypedValue(
                            (int)DxfCode.ExtendedDataReal, (double)newSpacing);
                        entity.XData = new ResultBuffer(vals);
                    }
                }

                tr.Commit();

                // Odswiez podglad
                Populate();

                MessageBox.Show(
                    $"Zaktualizowano rozstaw: {newSpacing} mm.\nPrzebuduj uklad komenda RC_GENERATE_SLAB aby przeliczyc prety.",
                    "RC SLAB", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Blad: {ex.Message}", "RC SLAB", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
