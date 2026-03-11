namespace BricsCadRc.Core
{
    /// <summary>
    /// Reprezentuje dane jednego preta / ukladu pretow.
    /// Te dane sa zapisywane w XData na obiekcie Line w rysunku.
    /// </summary>
    public class BarData
    {
        /// <summary>Oznaczenie preta np. "H16-200-B1"</summary>
        public string Mark { get; set; } = "";

        /// <summary>Srednica w mm (np. 8, 10, 12, 16, 20, 25, 32)</summary>
        public int Diameter { get; set; }

        /// <summary>Rozstaw w mm (np. 150, 200, 250)</summary>
        public double Spacing { get; set; }

        /// <summary>Liczba pretow w ukladzie</summary>
        public int Count { get; set; }

        /// <summary>Kod ksztaltu wg BS8666 np. "00", "11", "21"</summary>
        public string ShapeCode { get; set; } = "00";

        /// <summary>Wymiar A w mm (dlugosc calkowita lub pierwszy bok)</summary>
        public double LengthA { get; set; }

        /// <summary>Wymiar B w mm (jesli dotyczy ksztaltu)</summary>
        public double LengthB { get; set; }

        /// <summary>Wymiar C w mm</summary>
        public double LengthC { get; set; }

        /// <summary>Wymiar D w mm</summary>
        public double LengthD { get; set; }

        /// <summary>Wymiar E w mm</summary>
        public double LengthE { get; set; }

        /// <summary>Pozycja: "BOT" lub "TOP"</summary>
        public string Position { get; set; } = "BOT";

        /// <summary>Kod warstwy: "B1", "B2", "T1", "T2"</summary>
        public string LayerCode { get; set; } = "B1";

        /// <summary>
        /// Czy ten pret jest "reprezentatywny" — pokazywany w widoku zamiast calego ukladu.
        /// 0 = normalny, 1 = reprezentatywny (pozostale ukryte)
        /// </summary>
        public int RepresentativeFlag { get; set; } = 0;

        /// <summary>Indeks kolejny preta w grupie (1..N) — potrzebny dla M07 BarFilter</summary>
        public int BarIndex { get; set; } = 0;

        /// <summary>Kierunek preta: "X" (poziome) lub "Y" (pionowe)</summary>
        public string Direction { get; set; } = "X";

        /// <summary>Czy pret ma zaklad (lap splice): 0 = nie, 1 = tak</summary>
        public int IsLap { get; set; } = 0;

        /// <summary>Rozpiętość układu prętów w mm (barsH dla X, barsW dla Y) — zapisywana w XData annotacji</summary>
        public double BarsSpan { get; set; }

        /// <summary>Aktualna całkowita długość ramienia (ArmLength + długość tekstu) w mm — mutable przez grip</summary>
        public double ArmTotalLen { get; set; }

        /// <summary>Zmierzona dlugosc tekstu (z GeometricExtents) — stala po utworzeniu, uzywana w UpdateArmInBlock</summary>
        public double TextLen { get; set; }

        // ----------------------------------------------------------------
        // Obliczenia wg BS8666
        // ----------------------------------------------------------------

        /// <summary>Masa liniowa preta w kg/m wg BS8666</summary>
        public double LinearMass => GetLinearMass(Diameter);

        /// <summary>Dlugosc jednego preta w m (uzywana do obliczen tonazu)</summary>
        public double BarLengthMetres => LengthA / 1000.0;

        /// <summary>Calkowity tonaz ukladu w kg</summary>
        public double TotalMassKg => Count * BarLengthMetres * LinearMass;

        public static double GetLinearMass(int diameter)
        {
            // Masy liniowe wg BS8666 [kg/m]
            return diameter switch
            {
                6  => 0.222,
                8  => 0.395,
                10 => 0.616,
                12 => 0.888,
                16 => 1.579,
                20 => 2.466,
                25 => 3.854,
                32 => 6.313,
                40 => 9.864,
                50 => 15.413,
                _  => 0.0
            };
        }
    }
}
