using System;
using System.Collections.Generic;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Statyczny rejestr 32 shape code'ów BS8666.
    /// </summary>
    public static class ShapeCodeLibrary
    {
        private static readonly Dictionary<string, BarShape> _shapes =
            new Dictionary<string, BarShape>(StringComparer.Ordinal);

        static ShapeCodeLibrary()
        {
            void Add(BarShape s) => _shapes[s.Code] = s;

            // ── 00 Prosta ────────────────────────────────────────────────────
            Add(new BarShape("00", "Straight",
                new[] { "A" },
                Svg("<line x1=\"5\" y1=\"20\" x2=\"55\" y2=\"20\"/>"),
                (p, d) => p[0]));

            // ── Rodzina L (2 nogi, formuła A+B-0.5r-d) ──────────────────────
            Add(new BarShape("11", "90° hook",
                new[] { "A", "B" },
                Svg("<polyline points=\"5,35 5,10 50,10\"/>"),
                (p, d) => { var r = BarShape.MinBendRadius(d); return p[0] + p[1] - 0.5 * r - d; }));

            Add(new BarShape("12", "Hook both ends",
                new[] { "A", "B", "R" },
                Svg("<polyline points=\"5,35 5,10 50,10 50,25\"/>"),
                (p, d) => p[0] + p[1] - 0.43 * p[2] - 1.2 * d));

            Add(new BarShape("13", "Hook 180°",
                new[] { "A", "B", "C" },
                Svg("<polyline points=\"5,20 40,20 40,30 15,30\"/>"),
                (p, d) => p[0] + 0.57 * p[1] + p[2] - 1.6 * d));

            Add(new BarShape("14", "Hook 45°",
                new[] { "A", "B", "C" },
                Svg("<polyline points=\"5,35 5,10 45,5\"/>"),
                (p, d) => p[0] + p[2] - 4 * d));

            Add(new BarShape("15", "Hook 135°",
                new[] { "A", "B", "C" },
                Svg("<polyline points=\"5,35 5,10 50,10 40,25\"/>"),
                (p, d) => p[0] + p[2]));

            // ── 21 U-kształt (2 asymetryczne nogi, A+B+C-r-2d) ─────────────
            // A = lewe ramię, B = szerokość (odległość między ramionami), C = prawe ramię
            Add(new BarShape("21", "U-bar / stirrup",
                new[] { "A", "B", "C" },
                Svg("<polyline points=\"10,10 10,30 50,30 50,10\"/>"),
                (p, d) => { var r = BarShape.MinBendRadius(d); return p[0] + p[1] + p[2] - r - 2 * d; }));

            // ── Rodzina Z/S ───────────────────────────────────────────────────
            Add(new BarShape("22", "Z-bar",
                new[] { "A", "B", "C", "D" },
                Svg("<polyline points=\"5,30 5,15 35,15 35,5 55,5\"/>"),
                (p, d) => { var r = BarShape.MinBendRadius(d); return p[0] + p[1] + 0.57 * p[2] + p[3] - 0.5 * r - 2.6 * d; }));

            Add(new BarShape("23", "Z-bar variant",
                new[] { "A", "B", "C" },
                Svg("<polyline points=\"5,5 5,20 35,20 35,35 55,35\"/>"),
                (p, d) => { var r = BarShape.MinBendRadius(d); return p[0] + p[1] + p[2] - r - 2 * d; }));

            Add(new BarShape("24", "Crank",
                new[] { "A", "B", "C" },
                Svg("<polyline points=\"5,15 25,15 35,25 55,25\"/>"),
                (p, d) => p[0] + p[1] + p[2]));

            Add(new BarShape("25", "Hook + crank",
                new[] { "A", "B", "E" },
                Svg("<polyline points=\"5,35 5,15 30,15 30,5 55,5\"/>"),
                (p, d) => p[0] + p[1] + p[2]));

            Add(new BarShape("26", "Hook + leg",
                new[] { "A", "B", "C" },
                Svg("<polyline points=\"5,35 5,10 30,10 55,10 55,25\"/>"),
                (p, d) => p[0] + p[1] + p[2]));

            // ── Rodzina (3 nogi, A+B+C-0.5r-2d) ─────────────────────────────
            Add(new BarShape("27", "Hook variant 27",
                new[] { "A", "B", "C" },
                Svg("<polyline points=\"5,30 5,15 35,15 55,5\"/>"),
                (p, d) => { var r = BarShape.MinBendRadius(d); return p[0] + p[1] + p[2] - 0.5 * r - 2 * d; }));

            Add(new BarShape("28", "Hook variant 28",
                new[] { "A", "B", "C" },
                Svg("<polyline points=\"5,30 5,10 35,10 55,20\"/>"),
                (p, d) => { var r = BarShape.MinBendRadius(d); return p[0] + p[1] + p[2] - 0.5 * r - 2 * d; }));

            Add(new BarShape("29", "Hook variant 29",
                new[] { "A", "B", "C" },
                Svg("<polyline points=\"5,35 5,15 35,15 45,5\"/>"),
                (p, d) => { var r = BarShape.MinBendRadius(d); return p[0] + p[1] + p[2] - 0.5 * r - 2 * d; }));

            // ── 31 S-kształt (3 nogi, A+B+C-1.5r-3d) ────────────────────────
            Add(new BarShape("31", "S-bar",
                new[] { "A", "B", "C" },
                Svg("<polyline points=\"5,35 5,25 20,25 20,15 35,15 35,5 55,5\"/>"),
                (p, d) => { var r = BarShape.MinBendRadius(d); return p[0] + p[1] + p[2] - 1.5 * r - 3 * d; }));

            // ── Rodzina 4-nożna (A+B+C+D-1.5r-3d) ───────────────────────────
            Add(new BarShape("32", "4-leg bar",
                new[] { "A", "B", "C", "D" },
                Svg("<polyline points=\"5,35 5,20 20,20 20,10 40,10 40,20 55,20\"/>"),
                (p, d) => { var r = BarShape.MinBendRadius(d); return p[0] + p[1] + p[2] + p[3] - 1.5 * r - 3 * d; }));

            Add(new BarShape("33", "4-leg bar variant",
                new[] { "A", "B", "C" },
                Svg("<polyline points=\"5,20 5,10 30,10 30,30 55,30 55,20\"/>"),
                (p, d) => 2 * p[0] + 1.7 * p[1] + 2 * p[2] - 4 * d));

            // ── Zamknięte prostokąty / kwadraty (formuła A+B+C+E-0.5r-d) ────
            Add(new BarShape("34", "Closed rectangle",
                new[] { "A", "B", "C", "E" },
                Svg("<rect x=\"10\" y=\"8\" width=\"40\" height=\"24\"/>"),
                (p, d) => { var r = BarShape.MinBendRadius(d); return p[0] + p[1] + p[2] + p[3] - 0.5 * r - d; }));

            Add(new BarShape("35", "Closed square",
                new[] { "A", "B", "C", "E" },
                Svg("<rect x=\"10\" y=\"8\" width=\"40\" height=\"24\"/>"),
                (p, d) => { var r = BarShape.MinBendRadius(d); return p[0] + p[1] + p[2] + p[3] - 0.5 * r - d; }));

            Add(new BarShape("36", "Closed shape 36",
                new[] { "A", "B", "C", "D" },
                Svg("<polygon points=\"10,32 10,8 50,8 50,32\"/>"),
                (p, d) => { var r = BarShape.MinBendRadius(d); return p[0] + p[1] + p[2] + p[3] - r - 2 * d; }));

            // ── 41 Zamknięty czworokąt (A+B+C+D+E-2r-4d) ────────────────────
            Add(new BarShape("41", "Closed quadrilateral",
                new[] { "A", "B", "C", "D", "E" },
                Svg("<polygon points=\"10,32 10,8 50,8 50,32\"/>"),
                (p, d) => { var r = BarShape.MinBendRadius(d); return p[0] + p[1] + p[2] + p[3] + p[4] - 2 * r - 4 * d; }));

            // ── 44 Okrąg (A+B+C+D+E-2r-4d) ──────────────────────────────────
            Add(new BarShape("44", "Circle / ring",
                new[] { "A", "B", "C", "D", "E" },
                Svg("<circle cx=\"30\" cy=\"20\" r=\"15\"/>"),
                (p, d) => { var r = BarShape.MinBendRadius(d); return p[0] + p[1] + p[2] + p[3] + p[4] - 2 * r - 4 * d; }));

            // ── 46 Zamknięty prostokąt variant (A+2B+C+E) ────────────────────
            Add(new BarShape("46", "Closed rectangle variant",
                new[] { "A", "B", "C", "E" },
                Svg("<rect x=\"8\" y=\"6\" width=\"44\" height=\"28\"/>"),
                (p, d) => p[0] + 2 * p[1] + p[2] + p[3]));

            // ── 47 Zamknięty trójkąt (2A+B+MAX(21d,240)) ─────────────────────
            Add(new BarShape("47", "Closed triangle",
                new[] { "A", "B" },
                Svg("<polygon points=\"30,5 55,35 5,35\"/>"),
                (p, d) => 2 * p[0] + p[1] + Math.Max(21 * d, 240)));

            // ── 51 Strzemię zamknięte (2(A+B+C)-2.5r-5d; C=0→MAX(16d,160)) ──
            Add(new BarShape("51", "Closed stirrup",
                new[] { "A", "B", "C" },
                Svg("<polyline points=\"5,35 5,20 15,20 15,10 35,10 35,20 45,20 45,35\"/>"),
                (p, d) => { double hook51 = p[2] > 0 ? p[2] : Math.Max(16 * d, 160);
                             double r51    = BarShape.MinBendRadius(d);
                             return 2 * (p[0] + p[1] + hook51) - 2.5 * r51 - 5 * d; }));

            // ── 63 Strzemię zamknięte (2(A+B+C)-2.5r-5d; C=0→MAX(14d,150)) ─
            Add(new BarShape("63", "Closed stirrup 63",
                new[] { "A", "B", "C" },
                Svg("<polyline points=\"5,35 5,20 15,20 15,10 45,10 45,20 55,20 55,35\"/>"),
                (p, d) => { double hook63 = p[2] > 0 ? p[2] : Math.Max(14 * d, 150);
                             double r63    = BarShape.MinBendRadius(d);
                             return 2 * p[0] + 3 * p[1] + 2 * hook63 - 3 * r63 - 6 * d; }));

            // ── 6-nożne (A+B+C+D+E-2.5r-5d) ─────────────────────────────────
            Add(new BarShape("56", "6-leg bar",
                new[] { "A", "B", "C", "D", "E" },
                Svg("<polyline points=\"5,35 5,22 15,22 15,10 25,10 25,22 35,22 35,10 45,10 45,22 55,22\"/>"),
                (p, d) => { var r = BarShape.MinBendRadius(d); return p[0] + p[1] + p[2] + p[3] + p[4] - 2.5 * r - 5 * d; }));

            Add(new BarShape("64", "6-leg bar variant",
                new[] { "A", "B", "C", "D", "E" },
                Svg("<polyline points=\"5,35 5,20 15,20 15,8 30,8 40,20 50,20 50,35\"/>"),
                (p, d) => { var r = BarShape.MinBendRadius(d); return p[0] + p[1] + p[2] + p[3] + p[4] - 2.5 * r - 5 * d; }));

            // ── 75 Helisa (π(A-d)+B) ──────────────────────────────────────────
            Add(new BarShape("75", "Helix",
                new[] { "A", "B" },   // A=średnica, B=n*P (całkowity skok spirali)
                Svg("<path d=\"M10,35 C10,5 30,5 30,20 C30,35 50,35 50,5\" fill=\"none\"/>"),
                (p, d) => Math.PI * (p[0] - d) + p[1]));

            // ── 98 Kształt niestandardowy (A+2B+C+D-2r-4d) ──────────────────
            Add(new BarShape("98", "Special – drawing required",
                new[] { "A", "B", "C", "D" },
                Svg("<line x1=\"5\" y1=\"20\" x2=\"55\" y2=\"20\" stroke-dasharray=\"4,3\"/>"),
                (p, d) => { var r = BarShape.MinBendRadius(d); return p[0] + 2 * p[1] + p[2] + p[3] - 2 * r - 4 * d; }));

            Add(new BarShape("99", "Special – no standard formula",
                new[] { "A" },
                Svg("<line x1=\"5\" y1=\"20\" x2=\"55\" y2=\"20\" stroke-dasharray=\"2,4\"/>"),
                (p, d) => p[0]));
        }

        private static string Svg(string content) =>
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 60 40\"" +
            $" fill=\"none\" stroke=\"#000\" stroke-width=\"2\">{content}</svg>";

        /// <summary>Zwraca kształt o podanym kodzie lub null jeśli nie istnieje.</summary>
        public static BarShape Get(string code) =>
            code != null && _shapes.TryGetValue(code, out var s) ? s : null;

        /// <summary>Zwraca wszystkie zarejestrowane kształty.</summary>
        public static IEnumerable<BarShape> GetAll() => _shapes.Values;

        /// <summary>
        /// Podzbiór shape codes widoczny w ShapePickerDialog przy tworzeniu nowego pręta.
        /// Zachowuje kolejność zdefiniowaną w _pickableCodes (nie kolejność insertion do _shapes).
        /// Pozostałe shape codes są nadal obsługiwane przez Get() — zapewnia kompatybilność
        /// z istniejącymi prętami w starych DWG które używają shape codes spoza tej listy.
        /// </summary>
        public static IEnumerable<BarShape> GetPickable()
        {
            foreach (var code in _pickableCodes)
            {
                if (_shapes.TryGetValue(code, out var shape))
                    yield return shape;
            }
        }

        // Lista shape codes widocznych w pickerze — kolejność = kolejność wyświetlania w UI.
        // Żeby pokazać dodatkowy shape code, dodaj go tutaj (musi istnieć w _shapes).
        // Żeby ukryć shape code, usuń go z tej listy (wpis w _shapes zostaje, pręt nadal działa).
        private static readonly string[] _pickableCodes = new[]
        {
            "00", "11", "13", "15", "21", "33", "44", "46", "51", "63"
        };

        /// <summary>Sprawdza czy podany kod istnieje w rejestrze.</summary>
        public static bool Contains(string code) =>
            code != null && _shapes.ContainsKey(code);
    }
}
