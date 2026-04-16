namespace BricsCadRc.Core
{
    public enum BarVisibilityMode { All, MiddleOnly, FirstLast, Manual }

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

        /// <summary>Obliczona lub ręcznie nadpisana długość całkowita pręta w mm.</summary>
        public double TotalLength { get; set; }

        /// <summary>True gdy użytkownik ręcznie nadpisał obliczoną długość całkowitą.</summary>
        public bool LengthOverridden { get; set; }

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

        /// <summary>Otulina w mm uzyta przy generowaniu — offset gripa [0] od pierwszego preta</summary>
        public double Cover { get; set; }

        /// <summary>Aktualna całkowita długość ramienia (ArmLength + długość tekstu) w mm — mutable przez grip</summary>
        public double ArmTotalLen { get; set; }

        /// <summary>Zmierzona dlugosc tekstu (z GeometricExtents) — stala po utworzeniu, uzywana w UpdateArmInBlock</summary>
        public double TextLen { get; set; }

        /// <summary>Czy etykieta wychodzi z boku rozkładu (true) czy z góry/dołu (false)</summary>
        public bool LeaderHorizontal { get; set; }

        /// <summary>Czy etykieta wychodzi w prawo (true) lub w lewo (false) — dotyczy leaderHorizontal=true</summary>
        public bool LeaderRight { get; set; } = true;

        /// <summary>Pozycja Y linii arm w układzie lokalnym bloku. -1 = użyj BarsSpan/2.</summary>
        public double ArmMidY { get; set; } = double.NaN;

        /// <summary>Kierunek etykiety pionowej: true=góra (default), false=dół.</summary>
        public bool LeaderUp { get; set; } = true;

        /// <summary>Punkt końcowy tekstu w układzie lokalnym bloku X (dla etykiety z złamaniem). 0 = brak.</summary>
        public double TextEndLocalX { get; set; } = 0.0;

        /// <summary>Punkt końcowy tekstu w układzie lokalnym bloku Y (dla etykiety z złamaniem). 0 = brak.</summary>
        public double TextEndLocalY { get; set; } = 0.0;

        /// <summary>
        /// Handle (hex string) bloku RC_ANNOT powiazanego z tym ukladem pretow.
        /// Zapisywany w XData RC_BAR_BLOCK po utworzeniu annotacji.
        /// Uzywany przez SyncAnnotation do unikalnego odnalezienia wlasciwej annotacji
        /// (zamiast wyszukiwania po Mark, ktore moze znalezc zly blok gdy ten sam pret
        /// jest rozlozony wiele razy).
        /// </summary>
        public string AnnotHandle { get; set; } = "";

        /// <summary>Handle (hex) etykiety MLeader powiązanej z prętem (FLOW 1). "" jeśli brak.</summary>
        public string LabelHandle { get; set; } = "";

        /// <summary>
        /// Po której stronie linii rozkładu rysuje się symbol (okrąg lub haczyk).
        /// "Left" = startPt, "Right" = endPt, "Both" = oba końce.
        /// Dotyczy rozkładu (RC_DISTRIBUTION): L-bar → Left/Right; U-bar → Left/Right/Both.
        /// </summary>
        public string SymbolSide { get; set; } = "Right";

        /// <summary>
        /// Override symbolu końca pręta: "Auto" = z kodu kształtu, "None" / "Circle" / "Hook" = explicit.
        /// </summary>
        public string SymbolType { get; set; } = "Auto";

        /// <summary>Handle źródłowej polilinii pręta (RC_SINGLE_BAR) — tylko dla ViewingDirection="Any".</summary>
        public string SourceBarHandle { get; set; } = "";

        /// <summary>Handle (hex) bloku RC_BAR_BLOCK powiązanego z tym RC_BAR_ANNOT. "" jeśli brak.</summary>
        public string SourceBlockHandle { get; set; } = "";

        /// <summary>Handle (hex) Polyline etykiety nowego stylu (DistAnnotEngine). "" jeśli brak.</summary>
        public string LabelPolyHandle { get; set; } = "";

        /// <summary>Handle (hex) DBText etykiety nowego stylu (DistAnnotEngine). "" jeśli brak.</summary>
        public string LabelTextHandle { get; set; } = "";

        /// <summary>"Auto" / "Manual" / "Any" — jak wybrano długość widoku rozkładu.</summary>
        public string ViewingDirection { get; set; } = "Auto";

        /// <summary>Indeks segmentu polilinii użyty w trybie Manual (-1 = Auto).</summary>
        public int ViewSegmentIndex { get; set; } = -1;

        /// <summary>
        /// Kierunek haczyka L-bar (prostopadle do linii rozkładu): "Up" lub "Down".
        /// "Up" = CCW 90° od kierunku linii; "Down" = CW 90°.
        /// </summary>
        public string SymbolDirection { get; set; } = "Up";

        /// <summary>Tryb widoczności prętów w bloku RC_SLAB_BARS: All/MiddleOnly/FirstLast/Manual.</summary>
        public BarVisibilityMode VisibilityMode { get; set; } = BarVisibilityMode.All;

        /// <summary>Dla Manual: indeksy widocznych prętów jako "0,2,4". "" = wszystkie.</summary>
        public string VisibleIndices { get; set; } = "";

        /// <summary>Kąt obrotu układu prętów w radianach (0 = poziomy, π/2 = pionowy, inne = ukośny).</summary>
        public double Angle { get; set; } = 0.0;

        /// <summary>Punkt wstawienia X (w WCS) dla ukośnych prętów — przekazywany do GenerateFromBounds.</summary>
        public double Pt1X { get; set; } = 0.0;

        /// <summary>Punkt wstawienia Y (w WCS) dla ukośnych prętów — przekazywany do GenerateFromBounds.</summary>
        public double Pt1Y { get; set; } = 0.0;

        /// <summary>Zakodowane punkty leadera w układzie lokalnym bloku: "x1,y1;x2,y2;...". "" = domyślny arm.</summary>
        public string LeaderPoints { get; set; } = "";

        /// <summary>Wartości parametrów A–E jako tablica, potrzebna przez SingleBarEngine.Build().</summary>
        public double[] ParamValues => new[] { LengthA, LengthB, LengthC, LengthD, LengthE };

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
