using System.Collections.Generic;

namespace BricsCadRc.ShapeCodes
{
    /// <summary>
    /// Biblioteka kodow ksztaltu wg BS8666:2020.
    /// Na razie tylko kody najczesciej uzywane w plytach fundamentowych.
    /// </summary>
    public static class ShapeCodeLibrary
    {
        public static readonly IReadOnlyList<ShapeCode> All = new List<ShapeCode>
        {
            new ShapeCode
            {
                Code = "00",
                Description = "Prosty pret",
                TotalLengthFormula = "A"
            },
            new ShapeCode
            {
                Code = "11",
                Description = "Hak na jednym koncu",
                NeedsB = true,
                TotalLengthFormula = "A + B - 0.5d"
            },
            new ShapeCode
            {
                Code = "12",
                Description = "Haki na obu koncach",
                NeedsB = true,
                TotalLengthFormula = "A + 2B - d"
            },
            new ShapeCode
            {
                Code = "13",
                Description = "Zagiecie 90° na jednym koncu",
                NeedsB = true,
                TotalLengthFormula = "A + B - 0.5d"
            },
            new ShapeCode
            {
                Code = "21",
                Description = "L-shape (zagiecie na jednym koncu)",
                NeedsB = true,
                TotalLengthFormula = "A + B - 0.5d"
            },
            new ShapeCode
            {
                Code = "25",
                Description = "U-shape / hairpin",
                NeedsB = true,
                TotalLengthFormula = "2A + B - d"
            },
            new ShapeCode
            {
                Code = "32",
                Description = "Z-shape",
                NeedsB = true,
                NeedsC = true,
                TotalLengthFormula = "A + B + C - d"
            },
            new ShapeCode
            {
                Code = "33",
                Description = "S-shape",
                NeedsB = true,
                NeedsC = true,
                TotalLengthFormula = "A + B + C - d"
            },
            new ShapeCode
            {
                Code = "41",
                Description = "Clamping bar / starter bar",
                NeedsB = true,
                TotalLengthFormula = "A + B - 0.5d"
            },
            new ShapeCode
            {
                Code = "51",
                Description = "Strzup zamkniety",
                NeedsB = true,
                TotalLengthFormula = "2(A + B) - 3d"
            },
            new ShapeCode
            {
                Code = "56",
                Description = "Strzup z hakiem",
                NeedsB = true,
                NeedsC = true,
                TotalLengthFormula = "A + 2B + C - 1.5d"
            },
            new ShapeCode
            {
                Code = "63",
                Description = "Spirala / helisa",
                NeedsB = true,
                NeedsC = true,
                TotalLengthFormula = "C * sqrt((pi*A)^2 + B^2) / (pi*A)"
            },
            new ShapeCode
            {
                Code = "98",
                Description = "Ksztalt niestandardowy (rysunek)",
                TotalLengthFormula = "A"
            },
            new ShapeCode
            {
                Code = "99",
                Description = "Ksztalt z rysunku szczegolowego",
                TotalLengthFormula = "A"
            },
        };

        public static ShapeCode GetByCode(string code)
        {
            foreach (var sc in All)
                if (sc.Code == code) return sc;
            return null;
        }
    }
}
