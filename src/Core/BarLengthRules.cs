using System;
using System.Linq;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Reguły długości pręta przy zmianie końca (grip / STRETCH pręta-bloku) — czysta logika bez API BricsCAD
    /// (kompilowana także w testach jednostkowych). Plan C, etap 3 (1.2).
    ///
    /// Jak wzorzec BarGeometryWatcher.TryEndStretch: odcinek krótszy niż max(2d, 50) → zmiana cofnięta;
    /// TotalLength przeliczana shape.CalculateTotalLength (gdy nie LengthOverridden).
    /// Różnica względem wzorca (decyzja użytkownika): nowa długość zaokrąglana do 5 mm.
    /// </summary>
    internal static class BarLengthRules
    {
        /// <summary>Krok zaokrąglenia długości przy gripie / STRETCH [mm].</summary>
        public const double LengthStep = 5.0;

        /// <summary>Najkrótszy dopuszczalny odcinek końcowy: max(2d, 50) — jak TryEndStretch.</summary>
        public static double MinSegment(double diameter) => Math.Max(2.0 * diameter, 50.0);

        /// <summary>Zaokrąglenie do LengthStep (połówki od zera).</summary>
        public static double RoundToStep(double length)
            => Math.Round(length / LengthStep, MidpointRounding.AwayFromZero) * LengthStep;

        /// <summary>
        /// Nowa długość odcinka końcowego po przesunięciu końca o delta (rzut na oś, na zewnątrz = +).
        /// Wynik zaokrąglony do 5 mm; null = za krótki (&lt; max(2d, 50)) → zmiana odrzucona.
        /// </summary>
        public static double? StretchedLength(double oldLength, double delta, double diameter)
        {
            double v = RoundToStep(oldLength + delta);
            return v < MinSegment(diameter) ? (double?)null : v;
        }

        /// <summary>
        /// Długość całkowita wg kształtu (jak TryEndStretch: tyle wartości A–E, ile parametrów ma kształt).
        /// Nieznany kształt → jak "00". Wyjątek z CalculateTotalLength przechodzi do wywołującego.
        /// </summary>
        public static double TotalLengthFor(string shapeCode, double[] paramValues, double diameter)
        {
            var shape = ShapeCodeLibrary.Get(shapeCode ?? "00") ?? ShapeCodeLibrary.Get("00");
            var p = (paramValues ?? new double[0]).Take(shape.Parameters.Length).ToArray();
            return shape.CalculateTotalLength(p, diameter);
        }

        /// <summary>
        /// Poprawka #7 (docs/POPRAWKI_WZORCA.md): po zmianie wymiaru (RC_UPDATE_BAR, STRETCH / grip) pręt z ręcznie
        /// nadpisaną długością całkowitą (LengthOverridden) zachowuje TotalLength — komunikat dla użytkownika
        /// (bez znaków nowej linii; wywołujący dokleja „\n”).
        /// </summary>
        public static string OverriddenTotalLengthWarning(string mark, double totalLength)
            => "[RC AUTO] Pręt " + mark + " ma ręcznie nadpisaną długość całkowitą ("
             + totalLength.ToString("F0", System.Globalization.CultureInfo.InvariantCulture)
             + " mm) — nie zmieniona; sprawdź w RC_EDIT_BAR.";

        /// <summary>
        /// Początek geometrii pręta-bloku w układzie lokalnym BTR (punkt startowy BuildVisualEntities):
        /// kształt nie-pierścień → −lPts[0] (oś pręta zaczyna się w (0,0) bloku = punkt wstawienia = axis[0] w WCS);
        /// pierścień (W4c: 75, BarFrameRules.IsRingShape) → (0,0) (referencja w punkcie wstawienia polilinii, geometria jak
        /// BuildVisualEntities z Origin).
        /// Nieznany kształt → jak "00" (jak SingleBarEngine.PlaceBar).
        /// </summary>
        public static (double X, double Y) LocalStart(string shapeCode, double[] paramValues, double diameter)
        {
            var shape = ShapeCodeLibrary.Get(shapeCode) ?? ShapeCodeLibrary.Get("00");
            if (BarFrameRules.IsRingShape(shape.Code)) return (0.0, 0.0);
            var lPts = BarGeometryBuilder.GetLocalPoints(shape.Code, paramValues, diameter);
            if (lPts == null || lPts.Count == 0) return (0.0, 0.0);
            return (-lPts[0].X, -lPts[0].Y);
        }
    }
}
