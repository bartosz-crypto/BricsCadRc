using System;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Model kształtu pręta wg BS8666.
    /// </summary>
    public class BarShape
    {
        private readonly Func<double[], double, double> _lengthFormula;

        public string   Code       { get; }
        public string   Name       { get; }
        public string[] Parameters { get; }
        public string   SvgPreview { get; }

        public BarShape(string code, string name, string[] parameters, string svgPreview,
            Func<double[], double, double> lengthFormula)
        {
            Code           = code;
            Name           = name;
            Parameters     = parameters;
            SvgPreview     = svgPreview;
            _lengthFormula = lengthFormula;
        }

        /// <summary>
        /// Oblicza całkowitą długość pręta wg formuły BS8666.
        /// </summary>
        /// <param name="paramValues">Wartości wymiarów A, B, C, … (mm)</param>
        /// <param name="diameter">Średnica pręta (mm), musi być > 0</param>
        public double CalculateTotalLength(double[] paramValues, double diameter)
        {
            if (diameter <= 0)
                throw new ArgumentException("Średnica musi być większa od 0.", nameof(diameter));
            if (paramValues == null || paramValues.Length != Parameters.Length)
                throw new ArgumentException(
                    $"Oczekiwano {Parameters.Length} parametrów, podano {paramValues?.Length ?? 0}.",
                    nameof(paramValues));

            double raw = _lengthFormula(paramValues, diameter);
            // BS8666: shape 00 bez zaokrąglenia; pozostałe → CEILING do 25mm
            return Code == "00" ? raw : Math.Ceiling(raw / 25.0) * 25.0;
        }

        /// <summary>Minimalny promień gięcia wg BS8666: 2d dla d≤16, 3.5d dla d≤25, 4d dla d>25.</summary>
        public static double MinBendRadius(double d) =>
            d <= 16.0 ? 2.0 * d :
            d <= 25.0 ? 3.5 * d :
            4.0 * d;
    }
}
