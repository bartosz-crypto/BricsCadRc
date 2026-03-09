namespace BricsCadRc.ShapeCodes
{
    /// <summary>
    /// Definicja ksztaltu preta wg BS8666.
    /// </summary>
    public class ShapeCode
    {
        public string Code { get; set; }
        public string Description { get; set; }

        /// <summary>Ktore wymiary sa wymagane dla tego ksztaltu (A, B, C, D, E)</summary>
        public bool NeedsB { get; set; }
        public bool NeedsC { get; set; }
        public bool NeedsD { get; set; }
        public bool NeedsE { get; set; }

        /// <summary>Wzor na dlugosc calkowita (do obliczen BBS)</summary>
        public string TotalLengthFormula { get; set; }
    }
}
