namespace BricsCadRc.Core
{
    /// <summary>
    /// Jeden wiersz zestawienia prętów (BBS — Bar Bending Schedule).
    /// Agreguje wszystkie rozkłady tego samego pręta (tego samego SourceBarHandle).
    /// </summary>
    public class BarScheduleEntry
    {
        /// <summary>Numer pozycji, np. "01"</summary>
        public string PosNr { get; set; } = "";

        /// <summary>Oznaczenie pręta, np. "H12-01"</summary>
        public string Mark { get; set; } = "";

        /// <summary>Kod kształtu wg BS 8666:2020, np. "00", "11"</summary>
        public string ShapeCode { get; set; } = "00";

        /// <summary>Średnica w mm</summary>
        public int Diameter { get; set; }

        /// <summary>Wymiar A [mm]</summary>
        public double A { get; set; }

        /// <summary>Wymiar B [mm] — 0 jeśli nie dotyczy</summary>
        public double B { get; set; }

        /// <summary>Wymiar C [mm] — 0 jeśli nie dotyczy</summary>
        public double C { get; set; }

        /// <summary>Wymiar D [mm] — 0 jeśli nie dotyczy</summary>
        public double D { get; set; }

        /// <summary>Wymiar E [mm] — 0 jeśli nie dotyczy</summary>
        public double E { get; set; }

        /// <summary>Obliczona długość cięcia jednego pręta [mm] wg BS 8666</summary>
        public double CuttingLength { get; set; }

        /// <summary>Łączna liczba prętów (suma ze wszystkich rozkładów)</summary>
        public int TotalCount { get; set; }

        /// <summary>Masa liniowa [kg/m]</summary>
        public double LinearMass { get; set; }

        /// <summary>Łączna masa zestawu [kg] = TotalCount × CuttingLength/1000 × LinearMass</summary>
        public double TotalMassKg => TotalCount * (CuttingLength / 1000.0) * LinearMass;

        /// <summary>Kolumna wyświetlana — A [mm]</summary>
        public string ColA => A > 0 ? ((int)A).ToString() : "";

        /// <summary>Kolumna wyświetlana — B [mm]</summary>
        public string ColB => B > 0 ? ((int)B).ToString() : "";

        /// <summary>Kolumna wyświetlana — C [mm]</summary>
        public string ColC => C > 0 ? ((int)C).ToString() : "";

        /// <summary>Kolumna wyświetlana — D [mm]</summary>
        public string ColD => D > 0 ? ((int)D).ToString() : "";

        /// <summary>Kolumna wyświetlana — E [mm]</summary>
        public string ColE => E > 0 ? ((int)E).ToString() : "";

        /// <summary>Kolumna wyświetlana — długość cięcia [mm]</summary>
        public string ColLength => ((int)CuttingLength).ToString();

        /// <summary>Kolumna wyświetlana — masa [kg], 2 miejsca dziesiętne</summary>
        public string ColMass => TotalMassKg.ToString("F2");
    }
}
