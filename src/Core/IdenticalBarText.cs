using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

// Kompilowany także w tests (net8.0, Nullable enable) — kontekst nullable jak we wtyczce (wyłączony).
#nullable disable

namespace BricsCadRc.Core
{
    /// <summary>
    /// Teksty i reguły „identycznego pręta” — czysta logika bez API BricsCAD (kompilowana także w testach jednostkowych).
    /// (a) RC_DISTRIBUTION: okno OK / Anuluj, gdy na rysunku jest już pręt o identycznych wymiarach (także kopia o tym
    ///     samym numerze; bez prętów bez numeru) — e7b600d, decyzje D5 / P3; w trybie skryptowym — komunikat w linii poleceń.
    /// (b) Po COPY / MIRROR / ARRAY / PASTE: komunikat o kopii pręta z tym samym numerem pozycji (e7b600d).
    /// </summary>
    internal static class IdenticalBarText
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>Numer pręta bez sufiksu („H12-03 B1” → „H12-03”); pusty → „?”.</summary>
        public static string MarkKey(string mark)
        {
            string s = (mark ?? "").Trim();
            return s.Length == 0 ? "?" : s.Split(' ')[0];
        }

        /// <summary>Numery z liczbą wystąpień, posortowane po numerze (bez rozróżniania wielkości liter).</summary>
        public static List<KeyValuePair<string, int>> Group(IEnumerable<string> marks)
        {
            var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var m in marks ?? Enumerable.Empty<string>())
            {
                string key = MarkKey(m);
                counts.TryGetValue(key, out int n);
                counts[key] = n + 1;
            }
            return counts.ToList();
        }

        /// <summary>
        /// Wymiary parametrów kształtu (pierwsze <paramref name="paramCount"/> pól A–E — XData pozycyjne, W4) &gt; 0 jako
        /// „A/B/C…” (mm, bez miejsc po przecinku); pola spoza kształtu (np. stare C–E pierścienia) pomijane.
        /// </summary>
        public static string Dims(IEnumerable<double> values, int paramCount)
            => string.Join("/", (values ?? Enumerable.Empty<double>()).Take(Math.Max(0, paramCount))
                                .Where(v => v > 0).Select(v => v.ToString("F0", Inv)));

        private static string Head(string ownMark, int diameter, string shapeCode, IEnumerable<double> dims, int paramCount)
            => $"Na rysunku jest już pręt identyczny z {MarkKey(ownMark)} (H{diameter.ToString(Inv)}, kształt " +
               $"{(string.IsNullOrEmpty(shapeCode) ? "00" : shapeCode)}, {Dims(dims, paramCount)} mm)";

        /// <summary>Treść okna OK / Anuluj w RC_DISTRIBUTION.</summary>
        public static string DialogText(string ownMark, int diameter, string shapeCode, IEnumerable<double> dims,
                                        int paramCount, IEnumerable<string> foundMarks)
        {
            string list = string.Join("\n", Group(foundMarks).Select(kv => $"   • {kv.Key}  ({kv.Value.ToString(Inv)} szt.)"));
            return Head(ownMark, diameter, shapeCode, dims, paramCount) + ":\n\n" + list + "\n\nKontynuować rozkład z wybranego pręta?";
        }

        /// <summary>Komunikat w linii poleceń zamiast okna (tryb skryptowy / bez okien).</summary>
        public static string ScriptText(string ownMark, int diameter, string shapeCode, IEnumerable<double> dims,
                                        int paramCount, IEnumerable<string> foundMarks)
        {
            string list = string.Join(", ", Group(foundMarks).Select(kv => $"{kv.Key} ({kv.Value.ToString(Inv)} szt.)"));
            return "[RC] " + Head(ownMark, diameter, shapeCode, dims, paramCount) + ": " + list + " — tryb skryptowy: rozkład bez pytania.";
        }

        /// <summary>
        /// Komunikaty po kopiowaniu: kopie (z numerem &gt; 0), dla których istnieje pręt spoza kopii o tym samym numerze
        /// i tym samym kształcie — jeden komunikat na numer (z liczbą kopii). Kolejność wg numeru.
        /// </summary>
        public static List<string> SameNumberCopyMessages<T>(IEnumerable<T> copies, IEnumerable<T> others,
                                                             Func<T, string> mark, Func<T, int> posNr,
                                                             Func<T, T, bool> sameShape)
        {
            var otherList = (others ?? Enumerable.Empty<T>()).Where(o => posNr(o) > 0).ToList();
            var hits = new List<string>();
            foreach (var c in copies ?? Enumerable.Empty<T>())
            {
                int nr = posNr(c);
                if (nr <= 0) continue;
                if (otherList.Exists(o => posNr(o) == nr && sameShape(o, c))) hits.Add(mark(c));
            }
            return Group(hits)
                .Select(kv => $"[RC] Kopia pręta {kv.Key} ({kv.Value.ToString(Inv)} szt.) ma identyczne wymiary jak istniejący pręt — " +
                              "ten sam numer pozycji (liczy się do tej samej pozycji w BBS).")
                .ToList();
        }
    }
}
