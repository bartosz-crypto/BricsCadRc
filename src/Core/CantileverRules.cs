using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Wsporniki płyty (decyzja Bartka 2026-10-21, etap 1 — wykrywanie): krótka belka GB („GB-600x300mm deep TOC=…”) wystająca poza
    /// krawędź płyty, czasem ze strefą pogrubienia w płycie przy palu („375THK”, przerywany prostokąt). Na rysunkach GA / RC wspornik
    /// jest albo prostokątnym języczkiem zamkniętego obrysu, albo osobnym kawałkiem obrysu w kształcie U (końce na krawędzi płyty).
    /// Do siatki płyty (Mesh, UB, nib, T IN NIB) obrys BEZ języczka — krawędź płyty prosto przez nasadę wspornika; wspornik osobno
    /// (warstwa RC-CANTILEVER, rekord RC_CANT — CantileverStore). Czysta geometria — bez API BricsCAD; tylko obrysy prostokątne (osie X / Y).
    /// Zakresy (poszerzone 2026-10-21 na prośbę Bartka; przykłady: szer. 600, wysięg 788–1525, GB 225 / 300 / 450, szerokości 450–800):
    /// szerokość <see cref="MinWidth"/>–<see cref="MaxWidth"/>, wysięg <see cref="MinDepth"/>–<see cref="MaxDepth"/>.
    /// </summary>
    public static class CantileverRules
    {
        public const double MinWidth = 300.0;
        public const double MaxWidth = 1500.0;
        public const double MinDepth = 300.0;
        public const double MaxDepth = 3000.0;
        /// <summary>Tolerancja geometrii (zgodność końców, prostopadłość) [mm].</summary>
        public const double Tol = 1.0;

        /// <summary>Kształt U: A — B końce (na krawędzi płyty), W — szerokość (bok końcowy), D — wysięg (ramiona), Out — kierunek od końców do boku końcowego.</summary>
        public sealed class UShape
        {
            public (double X, double Y) A, B, C1, C2;   // A → C1 → C2 → B
            public double Width, Depth;
            public (double X, double Y) Out;            // jednostkowy, osie X / Y
        }

        /// <summary>Języczek zamkniętego obrysu: wierzchołki R0 (nasada) → E0 → E1 (koniec) → R1 (nasada); indeksy E0 / E1 w obrysie.</summary>
        public sealed class Tab
        {
            public int IdxE0, IdxE1;
            public UShape Shape;
        }

        // ── U z czterech punktów ─────────────────────────────────────────

        /// <summary>Cztery wierzchołki osiowe A, C1, C2, B tworzące U (ramiona równe i równoległe, bok końcowy prostopadły); inaczej null.</summary>
        public static UShape TryU(IList<(double X, double Y)> v)
        {
            if (v == null || v.Count != 4) return null;
            var a = v[0]; var c1 = v[1]; var c2 = v[2]; var b = v[3];
            var d1 = (c1.X - a.X, c1.Y - a.Y); var d2 = (c2.X - c1.X, c2.Y - c1.Y); var d3 = (b.X - c2.X, b.Y - c2.Y);
            if (!Axis(d1) || !Axis(d2) || !Axis(d3)) return null;
            double l1 = Len(d1), l2 = Len(d2), l3 = Len(d3);
            if (l1 < Tol || l2 < Tol || l3 < Tol) return null;
            // ramiona równoległe, przeciwnie skierowane, równej długości; bok końcowy prostopadły
            if (Math.Abs(l1 - l3) > Tol) return null;
            if (Math.Abs(d1.Item1 / l1 + d3.Item1 / l3) > 1e-3 || Math.Abs(d1.Item2 / l1 + d3.Item2 / l3) > 1e-3) return null;
            if (Math.Abs(d1.Item1 * d2.Item1 + d1.Item2 * d2.Item2) > Tol * l1) return null;
            return new UShape { A = a, C1 = c1, C2 = c2, B = b, Width = l2, Depth = l1, Out = (Math.Round(d1.Item1 / l1), Math.Round(d1.Item2 / l1)) };
        }

        public static bool InRange(UShape u)
            => u != null && u.Width >= MinWidth - Tol && u.Width <= MaxWidth + Tol && u.Depth >= MinDepth - Tol && u.Depth <= MaxDepth + Tol;

        // ── języczki zamkniętego obrysu ───────────────────────────────────

        /// <summary>
        /// Prostokątne języczki zamkniętego obrysu (osie X / Y): dwa kolejne narożniki wypukłe E0, E1 (bok końcowy), sąsiednie R0, R1 —
        /// wklęsłe, ramiona równe; R0 i R1 na jednej prostej (krawędź płyty). W zakresach szerokości / wysięgu.
        /// </summary>
        public static List<Tab> FindTabs(IList<(double X, double Y)> poly)
        {
            var res = new List<Tab>();
            var p = Clean(poly);
            int n = p.Count;
            if (n < 8) return res;
            double s = Math.Sign(SignedArea(p));
            if (s == 0) return res;
            int Turn(int i)
            {
                var a = p[(i - 1 + n) % n]; var b = p[i]; var c = p[(i + 1) % n];
                double cr = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                return Math.Abs(cr) < 1e-6 ? 0 : Math.Sign(cr) * (int)s;   // +1 wypukły, −1 wklęsły
            }
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n, r0 = (i - 1 + n) % n, r1 = (j + 1) % n;
                if (Turn(i) != 1 || Turn(j) != 1 || Turn(r0) != -1 || Turn(r1) != -1) continue;
                var u = TryU(new[] { p[r0], p[i], p[j], p[r1] });
                if (!InRange(u)) continue;
                res.Add(new Tab { IdxE0 = Index(poly, p[i]), IdxE1 = Index(poly, p[j]), Shape = u });
            }
            return res;
        }

        /// <summary>Obrys bez języczków: usunięte końce języczka (E0, E1) i nasady (R0, R1), gdy leżą na prostej krawędzi płyty.</summary>
        public static List<(double X, double Y)> RemoveTabs(IList<(double X, double Y)> poly, IEnumerable<Tab> tabs)
        {
            var drop = new HashSet<(double, double)>();
            foreach (var t in tabs) { drop.Add(t.Shape.C1); drop.Add(t.Shape.C2); }
            var kept = Clean(poly).Where(q => !drop.Contains(q)).ToList();
            // punkty współliniowe (dawne nasady) — usuwane
            bool changed = true;
            while (changed && kept.Count > 3)
            {
                changed = false;
                for (int i = 0; i < kept.Count; i++)
                {
                    var a = kept[(i - 1 + kept.Count) % kept.Count]; var b = kept[i]; var c = kept[(i + 1) % kept.Count];
                    double cr = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
                    double dot = (b.X - a.X) * (c.X - b.X) + (b.Y - a.Y) * (c.Y - b.Y);
                    if (Math.Abs(cr) < 1e-3 * Math.Max(1.0, Len((b.X - a.X, b.Y - a.Y)) * Len((c.X - b.X, c.Y - b.Y))) && dot > 0)
                    { kept.RemoveAt(i); changed = true; break; }
                }
            }
            return kept;
        }

        // ── opisy ────────────────────────────────────────────────────────

        private static readonly Regex GbRx = new Regex(@"\bGB\s*-?\s*(\d{3,4})\s*[xX×]\s*(\d{3,4})", RegexOptions.Compiled);
        private static readonly Regex TocRx = new Regex(@"(?<!NIB\s{0,3})\bTOC\s*=\s*(-?\d+(?:[.,]\d+)?)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        /// <summary>„GB-600x300mm deep” → (600, 300); inaczej null.</summary>
        public static (double Width, double Height)? ParseGb(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var m = GbRx.Match(text);
            if (!m.Success) return null;
            return (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        /// <summary>„TOC=8.525” (nie „NIB TOC=…”) → 8.525 [m]; inaczej null.</summary>
        public static double? ParseToc(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var m = TocRx.Match(text);
            if (!m.Success) return null;
            return double.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        }

        // ── punkty i prostokąty ──────────────────────────────────────────

        /// <summary>Prostokąt U (osiowy): xmin, ymin, xmax, ymax.</summary>
        public static (double X0, double Y0, double X1, double Y1) Box(UShape u)
        {
            var xs = new[] { u.A.X, u.B.X, u.C1.X, u.C2.X }; var ys = new[] { u.A.Y, u.B.Y, u.C1.Y, u.C2.Y };
            return (xs.Min(), ys.Min(), xs.Max(), ys.Max());
        }

        public static bool InBox((double X0, double Y0, double X1, double Y1) b, double x, double y, double inflate = 0)
            => x >= b.X0 - inflate && x <= b.X1 + inflate && y >= b.Y0 - inflate && y <= b.Y1 + inflate;

        /// <summary>Strefa przy palu: U po drugiej stronie tej samej nasady (te same końce A / B w dowolnej kolejności, kierunek przeciwny).</summary>
        public static bool IsRootOf(UShape root, UShape stub)
        {
            if (root == null || stub == null) return false;
            bool same = (Near(root.A, stub.A) && Near(root.B, stub.B)) || (Near(root.A, stub.B) && Near(root.B, stub.A));
            return same && Math.Abs(root.Out.X + stub.Out.X) < 1e-6 && Math.Abs(root.Out.Y + stub.Out.Y) < 1e-6;
        }

        public static string DirCode((double X, double Y) d)
            => d.X > 0.5 ? "+X" : d.X < -0.5 ? "-X" : d.Y > 0.5 ? "+Y" : "-Y";

        // ── pomocnicze ───────────────────────────────────────────────────

        public static bool Near((double X, double Y) a, (double X, double Y) b) => Math.Abs(a.X - b.X) <= Tol && Math.Abs(a.Y - b.Y) <= Tol;

        private static bool Axis((double, double) d) => Math.Abs(d.Item1) <= Tol || Math.Abs(d.Item2) <= Tol;
        private static double Len((double, double) d) => Math.Sqrt(d.Item1 * d.Item1 + d.Item2 * d.Item2);

        private static double SignedArea(IList<(double X, double Y)> p)
        {
            double a = 0;
            for (int i = 0; i < p.Count; i++) { var q = p[i]; var r = p[(i + 1) % p.Count]; a += q.X * r.Y - r.X * q.Y; }
            return a / 2;
        }

        /// <summary>Bez powtórzonego ostatniego punktu i bez punktów zdublowanych.</summary>
        private static List<(double X, double Y)> Clean(IList<(double X, double Y)> poly)
        {
            var p = new List<(double X, double Y)>();
            foreach (var q in poly ?? Array.Empty<(double, double)>())
                if (p.Count == 0 || !Near(p[p.Count - 1], q)) p.Add(q);
            if (p.Count > 1 && Near(p[0], p[p.Count - 1])) p.RemoveAt(p.Count - 1);
            return p;
        }

        private static int Index(IList<(double X, double Y)> poly, (double X, double Y) q)
        {
            for (int i = 0; i < poly.Count; i++) if (Near(poly[i], q)) return i;
            return -1;
        }
    }
}
