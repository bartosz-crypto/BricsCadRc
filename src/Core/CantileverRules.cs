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
        public const double MinDepth = 200.0;   // uwaga Bartka 2026-10-26 (PLOT 357-362): krótkie wsporniki — języczek 276 za krawędzią (dawniej 300)
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
            // uwaga Bartka 2026-10-26 (PLOT 362): wierzchołek współliniowy na boku języczka (GA: …677,−368522 między narożnikami) —
            // narożniki końca nie sąsiadowały i wspornik nie był wykrywany; punkty współliniowe pomijane (indeksy E0 / E1 — w obrysie)
            var p = Straight(Clean(poly));
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
            var kept = Straight(Clean(poly)).Where(q => !drop.Contains(q)).ToList();   // punkt współliniowy na końcu języczka (PLOT 362) — też poza
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
        /// <summary>
        /// Opis krótkiego wspornika bez „GB-…” (uwaga Bartka 2026-10-26, PLOT 357-362): „300THK / TOC=21.250” — wysokość 300 od TOC; null —
        /// brak (THK bez TOC to grubość strefy przy palu, nie wspornik).
        /// </summary>
        public static double? ParseThkToc(string text)
        {
            if (string.IsNullOrEmpty(text) || ParseToc(text) == null) return null;
            foreach (var l in text.Split('\n'))
                if (BeamGeometry.ParseThk(l) is int t) return t;
            return null;
        }

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

        // ── obrys płyty BEZ zmian na rysunku (uwaga Bartka 2026-10-22: „Prepare GA zmienia faktyczny obrys płyty!”) ─────────────

        /// <summary>
        /// XData na polilinii obrysu: języczki wsporników (kierunek, szerokość, wysięg) — obrys na rysunku zostaje z języczkiem jak w GA,
        /// a program (GeometryHelper.GetPolylineVertices) czyta obrys bez nich (siatka, UB, nib, belki — krawędź prosto przez nasadę).
        /// Bez współrzędnych — MOVE / COPY obrysu nie psuje dopasowania.
        /// </summary>
        public const string TabApp = "RC_CANT_TAB";

        public sealed class TabRec
        {
            public string Dir;
            public double Width, Depth;
            public override string ToString()
                => string.Format(CultureInfo.InvariantCulture, "{0};{1:0.###};{2:0.###}", Dir, Width, Depth);
            public static TabRec Parse(string s)
            {
                var p = (s ?? "").Split(';');
                if (p.Length != 3) return null;
                if (!double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double w)) return null;
                if (!double.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return null;
                return new TabRec { Dir = p[0], Width = w, Depth = d };
            }
            public static TabRec Of(UShape u) => new TabRec { Dir = DirCode(u.Out), Width = u.Width, Depth = u.Depth };
            public bool Matches(UShape u)
                => u != null && DirCode(u.Out) == Dir && Math.Abs(u.Width - Width) <= Tol && Math.Abs(u.Depth - Depth) <= Tol;
        }

        /// <summary>Języczki obrysu dopasowane do rekordu (każdy wpis rekordu — najwyżej jeden języczek).</summary>
        public static List<Tab> RecordedTabs(IList<(double X, double Y)> raw, IEnumerable<TabRec> rec)
        {
            var take = new List<Tab>();
            var left = (rec ?? Enumerable.Empty<TabRec>()).Where(r => r != null).ToList();
            if (left.Count == 0 || raw == null) return take;
            foreach (var t in FindTabs(raw))
            {
                var m = left.FirstOrDefault(r => r.Matches(t.Shape));
                if (m == null) continue;
                left.Remove(m);
                take.Add(t);
            }
            return take;
        }

        /// <summary>Obrys do obliczeń: bez języczków z rekordu; null — nic do usunięcia (obrys jak na rysunku).</summary>
        public static List<(double X, double Y)> EffectiveOutline(IList<(double X, double Y)> raw, IEnumerable<TabRec> rec)
        {
            var take = RecordedTabs(raw, rec);
            return take.Count == 0 ? null : RemoveTabs(raw, take);
        }

        // ── obrys z języczkiem (detal 1:25 — linie betonu i ramka obejmują wspornik) ───────────────────

        /// <summary>
        /// Obrys z powrotem z języczkiem: krawędź obrysu, na której leżą nasady A i B, dostaje wierzchołki nasady i końca wspornika
        /// (kolejność wg kierunku krawędzi). Gdy A / B nie leżą na jednej krawędzi — obrys bez zmian.
        /// </summary>
        public static List<(double X, double Y)> InsertTab(IList<(double X, double Y)> poly, UShape u)
        {
            var p = Clean(poly);
            int n = p.Count;
            for (int i = 0; i < n; i++)
            {
                var a = p[i]; var b = p[(i + 1) % n];
                double ta = OnSeg(a, b, u.A), tb = OnSeg(a, b, u.B);
                if (double.IsNaN(ta) || double.IsNaN(tb)) continue;
                bool aFirst = ta <= tb;
                var near = aFirst ? u.A : u.B; var far = aFirst ? u.B : u.A;
                var cNear = aFirst ? u.C1 : u.C2; var cFar = aFirst ? u.C2 : u.C1;
                var res = new List<(double X, double Y)>(p.Take(i + 1)) { near, cNear, cFar, far };
                res.AddRange(p.Skip(i + 1));
                return Clean(res);
            }
            return p;
        }

        private static double OnSeg((double X, double Y) a, (double X, double Y) b, (double X, double Y) q)
        {
            double ex = b.X - a.X, ey = b.Y - a.Y, L2 = ex * ex + ey * ey;
            if (L2 < 1e-9) return double.NaN;
            double t = ((q.X - a.X) * ex + (q.Y - a.Y) * ey) / L2;
            if (t < -1e-9 || t > 1 + 1e-9) return double.NaN;
            double dx = a.X + ex * t - q.X, dy = a.Y + ey * t - q.Y;
            return Math.Sqrt(dx * dx + dy * dy) <= Tol ? t : double.NaN;
        }

        // ── etap 2: plan prętów wspornika (decyzje Bartka 2026-10-21) ───────────────────────────

        /// <summary>Pręty proste wchodzące w płytę ze wspornika bez strefy przy palu — średnica i zakotwienie za krawędzią [mm].</summary>
        public const int StraightIntoSlabDia = 12;
        public const double StraightIntoSlab = 1000.0;
        /// <summary>Ramię U-bara dłuższe niż to — U-bary + pręty proste (decyzja Bartka 2026-10-21).</summary>
        public const double MaxUBarArm = 1300.0;
        /// <summary>Pręty główne / U-bary wspornika (Speedeck: min. 4H16 górą i dołem).</summary>
        public const int MainDia = 16;
        /// <summary>Pręty boczne (jak belki: BeamParams.SideDia).</summary>
        public const int SideDia = 12;
        /// <summary>Największy rozstaw kolumn prętów w poprzek (600 → 4, 450 → 3, 800 → 5); opis „-150”.</summary>
        public const double MaxColumnSpacing = 175.0, NominalColumnSpacing = 150.0;
        /// <summary>Dodatek ramienia U-bara ponad zakład przy prętach prostych (jak koniec belki, BeamGeometry).</summary>
        public const double UBarArmExtra = 90.0;
        /// <summary>Najmniejszy zakład U-barów głównych wspornika (decyzja Bartka 2026-10-23, test10: „min. 700”; 40·Ø H16 = 640 za mało).</summary>
        public const double UBarMinLap = 700.0;
        /// <summary>
        /// Wspornik poziomy (oś X, decyzja Bartka 2026-10-23, test10-poprawiony): w strefie przy palu JEDNA pozycja strzemion w obu kierunkach —
        /// A = najwyższe możliwe − 15 mm (375: 290 → 275), co 150. Wspornik pionowy (oś Y) — bez zmian (zewnętrzne + wewnętrzne A − 2·Ø).
        /// </summary>
        public const double RootSingleLinkDrop = 15.0, RootSingleLinkSpacing = 150.0;
        /// <summary>Krótki wspornik (uwagi Bartka 2026-10-26, PLOT 357-362: wysięg 411 od uskoku): ZAMIAST U-barów H16 — strzemiona
        /// w kierunku głównym (pręty wzdłuż osi) na całej długości (strefa przy palu + wysięg) co 120 dokładnie; strzemiona strefy przy palu
        /// w obu kierunkach — standardowo (co 150).</summary>
        public const double ShortReachMax = 600.0, ShortMainLinkSpacing = 120.0;
        /// <summary>Strefa wspornika bez strefy przy palu — w płycie na długość zakotwienia prętów prostych (magenta nakłada się na płytę).</summary>
        public const double NoRootExtension = StraightIntoSlab;
        /// <summary>
        /// Kształt strzemion (decyzja Bartka 2026-10-21): elementy W PŁYCIE (strefa przy palu / pile cap, belki i pogrubienia w płycie) —
        /// zawsze 51; POZA PŁYTĄ (wspornik) — zawsze 63.
        /// </summary>
        public const string LinkShapeInSlab = "51", LinkShapeOutside = "63";
        /// <summary>Uskok między strefą przy palu a wspornikiem (linia uskoku nibu) — najdalej tyle od krawędzi płyty w głąb [mm].</summary>
        public const double StepSearch = 300.0;

        /// <summary>
        /// Uskok na nasadzie wspornika: przecięcie osi strefy z linią uskoku nibu (<paramref name="cuts"/> — współrzędne wzdłuż osi) po stronie
        /// płyty od krawędzi <paramref name="root"/> (przeciwnie do końca <paramref name="end"/>), najbliżej krawędzi, w odległości (1, StepSearch].
        /// Null — brak (wspornik liczony od krawędzi płyty).
        /// </summary>
        public static double? StepCut(IEnumerable<double> cuts, double root, double end, double maxDist = StepSearch)
        {
            int inward = end > root ? -1 : 1;
            double? best = null;
            foreach (var c in cuts ?? Enumerable.Empty<double>())
            {
                double d = (c - root) * inward;
                if (d <= 1.0 || d > maxDist + 0.5) continue;
                if (best == null || d < (best.Value - root) * inward) best = c;
            }
            return best;
        }

        /// <summary>
        /// Zagnieżdżenie strzemion strefy przy palu w układzie GLOBALNYM (uwaga Bartka 2026-10-22, test7 — wspornik poziomy): strzemiona
        /// z prętami wzdłuż X — zewnętrzne (pełne A), z prętami wzdłuż Y — wewnętrzne (A − 2·Ø w dół do 5); jak belki (odcinek wzdłuż X —
        /// strzemiona druga warstwa). PlanBars liczy „w poprzek osi = zewnętrzne”, co pasuje do wspornika w osi Y; wspornik w osi X —
        /// wysokości obu kierunków zamienione.
        /// </summary>
        public static void NestRootLinksGlobal(Plan p, bool axisAlongX)
        {
            if (p == null || !axisAlongX || p.SingleRootLink) return;
            var across = p.Links.FirstOrDefault(l => !l.AlongAxis && l.Shape == LinkShapeInSlab && l.S1 <= 0.5);
            var along = p.Links.FirstOrDefault(l => l.AlongAxis && l.Shape == LinkShapeInSlab);
            if (across == null || along == null) return;
            // w poprzek osi X = pręty wzdłuż Y → wewnętrzne; wzdłuż osi X = pręty wzdłuż X → zewnętrzne
            double outerA = Math.Max(across.A, along.A);
            along.A = outerA;
            across.A = SecondDirLinkA(outerA, across.Dia);
            p.Messages.Add($"strefa przy palu (wspornik w osi X): strzemiona z prętami wzdłuż X zewnętrzne (A {along.A:F0}), wzdłuż Y wewnętrzne " +
                           $"(A {across.A:F0}).");
        }

        /// <summary>
        /// Wysokość A strzemion drugiego kierunku — WEWNĄTRZ pierwszych (osobna pozycja; jak U-bary w strzemionach belek):
        /// A − 2·Ø strzemienia, W DÓŁ do 5 mm — wewnętrzne nie może być wyższe niż miejsce w środku (290, H12 → 266 → 265; 665 → 641 → 640).
        /// Uwaga Bartka 2026-10-22.
        /// </summary>
        public static double SecondDirLinkA(double a, int dia) => Math.Floor((a - 2.0 * dia) / 5.0 + 1e-9) * 5.0;

        /// <summary>Strzemiona wg Speedeck_Anchorage (4-leg): h ≤ 225 → H10 @100, h ≤ 300 → H12 @150, wyżej → H12 @200.</summary>
        public static (int Dia, double Spacing) LinkFor(double h)
            => h <= 225 + 0.5 ? (10, 100.0) : h <= 300 + 0.5 ? (12, 150.0) : (12, 200.0);

        public sealed class LinkRun
        {
            public double S0, S1, First, Step, A, B, Spacing;
            public int Count, Dia, Legs;
            public string Note;
            /// <summary>Kształt (51 — w płycie, 63 — poza płytą).</summary>
            public string Shape = LinkShapeOutside;
            /// <summary>
            /// Drugi kierunek (strefa przy palu): pręty strzemion WZDŁUŻ osi wysięgu na odcinku L0…L1 (s), rozstawione W POPRZEK —
            /// S0…S1, First, Step w t (0 … szerokość). Inaczej (false) — pręty w poprzek osi, rozstawione wzdłuż s.
            /// </summary>
            public bool AlongAxis;
            public double L0, L1;
        }

        public sealed class UBarRun
        {
            /// <summary>Lico zewnętrzne tyłu U (s) i kierunek ramion (+1 — w stronę rosnącego s).</summary>
            public double Back;
            public int ArmDir;
            public double A, B;
            public int Dia;
            public double LineStart => ArmDir > 0 ? Back : Back - A;
            public double LineEnd => ArmDir > 0 ? Back + A : Back;
        }

        public sealed class StraightRun
        {
            public double S0, S1;
            public int Dia;
        }

        public sealed class Plan
        {
            public int Columns;
            /// <summary>Oś pierwszej kolumny od boku wspornika (t) i rozstaw kolumn.</summary>
            public double T0, ColumnSpacing;
            public List<LinkRun> Links = new List<LinkRun>();
            public List<UBarRun> UBars = new List<UBarRun>();
            public List<StraightRun> Straight = new List<StraightRun>();
            public List<string> Messages = new List<string>();
            /// <summary>
            /// Pręty boczne (zasady belek — uwaga Bartka 2026-10-22): poziomów na bok z wysokości GB (≤ 375 brak, 450–525: 1, 600–675: 2 …),
            /// U-bary „UB SIDE” (kształt 21 poziomo) na końcach z zakładem; długi — pręty proste „SIDE” między nimi.
            /// </summary>
            public int SideLevels, SideDia;
            /// <summary>Osie prętów bocznych w poprzek od boku c0 (t) — przy ramionach strzemienia.</summary>
            public double SideT0, SideT1;
            public List<UBarRun> SideUBars = new List<UBarRun>();
            public List<StraightRun> SideStraight = new List<StraightRun>();
            /// <summary>Strefa przy palu — jedna pozycja strzemion w obu kierunkach (wspornik poziomy, <see cref="RootSingleLinkDrop"/>).</summary>
            public bool SingleRootLink;
            /// <summary>Krótki wspornik — strzemiona wzdłuż osi na całej długości zamiast U-barów (<see cref="ShortReachMax"/>).</summary>
            public bool ShortMainLinks;
        }

        /// <summary>
        /// Plan prętów wspornika w układzie lokalnym: s wzdłuż wysięgu (0 = krawędź płyty na nasadzie, reach = koniec wspornika, ujemne —
        /// w płycie), t w poprzek (0 … width). Ze strefą przy palu (rootLen &gt; 0, grubość rootH): U-bar na końcu wspornika i U-bar na końcu
        /// strefy, ramiona z zakładem; ramię &gt; 1300 → U-bary z ramieniem zakład + 90 i pręty proste H16 między nimi. Bez strefy: U-bar
        /// tylko we wsporniku (ramię do nasady, maks. 1300) + pręty proste H12 górą i dołem do 1000 za krawędź (zakład z ramieniem).
        /// Strzemiona 4-leg (2 zachodzące) wg <see cref="LinkFor"/> — osobno wysięg (h) i strefa przy palu (rootH).
        /// </summary>
        public static Plan PlanBars(double reach, double width, double h, double rootLen, double rootH, double maxLen = 6000,
                                    bool singleRootLink = false)
        {
            var pl = new Plan();
            double cover = BeamGeometry.DefaultCoverSide;
            var (dLink, sLink) = LinkFor(h);
            // kolumny prętów głównych w poprzek
            double off = cover + dLink + MainDia / 2.0;
            double inner = width - 2 * off;
            int cols = inner <= 0 ? 1 : Math.Max(2, (int)Math.Ceiling(inner / MaxColumnSpacing - 1e-9) + 1);
            var (t0, sp) = BeamGeometry.BarsAcross(0, width, cols, MainDia, dLink, cover);
            pl.Columns = cols; pl.T0 = t0; pl.ColumnSpacing = sp;
            // zakład U-barów głównych: 40·Ø, nie mniej niż 700 (decyzja Bartka 2026-10-23)
            double lap = Math.Max(BeamGeometry.Ceil25(40.0 * MainDia), UBarMinLap);

            // strzemiona
            // lo…hi — zakres rozstawu; span — wymiar przekroju w poprzek prętów strzemienia (szerokość / długość strefy), ncol — pręty
            // wewnątrz; along — drugi kierunek (pręty wzdłuż osi na L0…L1)
            void Links(double lo, double hi, double hh, string where, string shape, double span, int ncol, bool along = false,
                       double l0 = 0, double l1 = 0, double spacing = double.NaN, double drop = 0)
            {
                var (d, s) = LinkFor(hh);
                if (!double.IsNaN(spacing)) s = spacing;
                double a = BeamGeometry.StirrupHeightCovers(hh, BeamGeometry.CoverTopA1, BeamGeometry.DefaultCoverBottom);
                if (drop > 0) a = Math.Floor((a - drop) / 5.0 + 1e-9) * 5.0;
                // uwaga Bartka 2026-10-22 („te same zasady co w belkach”): Auto jak belki — H ≤ 375 dwa zachodzące (4-leg, B 370),
                // wyżej 2-cięte na pełną szerokość (600 → B 510)
                var legs = BeamGeometry.PlanLinkLegs(BeamParams.LegsAuto, hh, span, cover, BeamGeometry.DefaultCoverBottom, d,
                                                     new[] { (ncol, MainDia) }, edgeBeam: true);
                var (first, step, n, _) = BeamGeometry.StirrupLayout(lo, hi, s, BeamGeometry.StirrupFirstOffset(d, BeamGeometry.LinkEndCover));
                if (!double.IsNaN(spacing) && n > 1)
                {
                    // rozstaw dokładnie zadany (co 150), reszta po równo z obu stron (test10-poprawiony: 4 co 150 zamiast 4 co 169)
                    double av = (n - 1) * step;
                    int m = (int)Math.Floor(av / spacing + 1e-9) + 1;
                    first += (av - (m - 1) * spacing) / 2.0; step = spacing; n = m;
                }
                if (a <= 0 || n <= 0) { pl.Messages.Add($"{where}: strzemiona pominięte (h = {hh:F0})."); return; }
                pl.Links.Add(new LinkRun { S0 = lo, S1 = hi, First = first, Step = step, Count = n, Dia = d, A = a, Spacing = s,
                                           Legs = legs.Legs, B = legs.Legs == 4 ? legs.B4 : legs.BOuter, Note = $"{where}: {legs.Note}",
                                           Shape = shape, AlongAxis = along, L0 = l0, L1 = l1 });
            }
            Links(0, reach, h, "wysięg", LinkShapeOutside, width, cols);
            bool root = rootLen > 1.0 && rootH > 0;
            if (root)
            {
                // strefa przy palu (pile cap, w płycie): strzemiona 51 w obu kierunkach (decyzja Bartka 2026-10-21, wzór PLOT 28 — 12 i 14)
                // wspornik poziomy (decyzja Bartka 2026-10-23): jedna pozycja w obu kierunkach, A − 15, co 150
                double rs = singleRootLink ? RootSingleLinkSpacing : double.NaN, rd = singleRootLink ? RootSingleLinkDrop : 0;
                pl.SingleRootLink = singleRootLink;
                Links(-rootLen, 0, rootH, "strefa przy palu", LinkShapeInSlab, width, cols, spacing: rs, drop: rd);
                double inner2 = rootLen - 2 * off;
                int cols2 = inner2 <= 0 ? 1 : Math.Max(2, (int)Math.Ceiling(inner2 / MaxColumnSpacing - 1e-9) + 1);
                Links(0, width, rootH, "strefa przy palu (drugi kierunek)",
                      LinkShapeInSlab, rootLen, cols2, along: true, l0: -rootLen, l1: 0, spacing: rs, drop: rd);
                // uwaga Bartka 2026-10-22: oba kierunki tej samej wysokości kolidują — drugi kierunek niższy o Ø strzemienia, w dół do 5
                // — drugi kierunek wchodzi w pierwszy: A − 2·Ø w dół do 5 (375: 290 − 24 = 266 → 265); jedna pozycja — bez zagnieżdżenia
                var second = pl.Links.LastOrDefault();
                if (!singleRootLink && second != null && second.AlongAxis) second.A = SecondDirLinkA(second.A, second.Dia);
                if (singleRootLink)
                    pl.Messages.Add($"strefa przy palu (wspornik poziomy): jedna pozycja strzemion w obu kierunkach — A najwyższe − {RootSingleLinkDrop:F0} " +
                                    $"({pl.Links.Last().A:F0}), co {RootSingleLinkSpacing:F0}.");
            }

            // U-bary: B = A strzemienia − 2·Ø strzemienia W DÓŁ do 5 (jak pionowe U-bary belki, Floor5; zasada ogólna 2026-10-22: element
            // wchodzący w inny — lepiej mniej niż kolizja; 215 − 24 = 191 → 190, jak rysunek biura PLOT 28)
            double aLink = BeamGeometry.StirrupHeightCovers(h, BeamGeometry.CoverTopA1, BeamGeometry.DefaultCoverBottom);
            double bU = Math.Floor((aLink - 2 * dLink) / 5.0 + 1e-9) * 5.0;
            double backOut = reach - cover;
            if (root && reach <= ShortReachMax + 0.5)
            {
                // krótki wspornik (uwaga Bartka 2026-10-26): zamiast U-barów H16 strzemiona zamknięte z prętami wzdłuż osi na całej długości
                // (od końca strefy przy palu do końca wspornika), w środku strzemion wysięgu (A − 2·Ø w dół do 5 — jak B U-bara), co 120
                double len = Math.Floor((rootLen + reach - 2 * cover) / 5.0 + 1e-9) * 5.0;
                var (first, step, n, _) = BeamGeometry.StirrupLayout(0, width, ShortMainLinkSpacing,
                                                                     BeamGeometry.StirrupFirstOffset(dLink, BeamGeometry.LinkEndCover));
                if (n > 1)
                {
                    double av = (n - 1) * step;
                    int m = (int)Math.Floor(av / ShortMainLinkSpacing + 1e-9) + 1;
                    first += (av - (m - 1) * ShortMainLinkSpacing) / 2.0; step = ShortMainLinkSpacing; n = m;
                }
                if (bU > 0 && len > 0 && n > 0)
                {
                    pl.Links.Add(new LinkRun { S0 = 0, S1 = width, First = first, Step = step, Count = n, Dia = dLink, A = bU, B = len,
                                               Spacing = ShortMainLinkSpacing, Legs = 2, Shape = LinkShapeInSlab, AlongAxis = true,
                                               L0 = -rootLen, L1 = reach,
                                               Note = $"krótki wspornik (wysięg {reach:F0} ≤ {ShortReachMax:F0}): zamiast U-barów H{MainDia} " +
                                                      $"strzemiona wzdłuż osi na całej długości {len:F0}, co {ShortMainLinkSpacing:F0}" });
                    pl.ShortMainLinks = true;
                }
            }
            else if (root)
            {
                // tył U przy końcu strefy przy palu — W ŚRODKU strzemion drugiego kierunku (ich ramię przy licu końca strefy):
                // otulina + Ø strzemienia strefy (bez kolizji, zasada 2026-10-22)
                double backIn = -rootLen + cover + LinkFor(rootH).Dia;
                double span = backOut - backIn;
                double arm = BeamGeometry.Ceil25((span + lap) / 2.0);
                if (arm <= MaxUBarArm + 0.5)
                {
                    arm = BeamGeometry.UBarArmForModule(arm, bU, MainDia);   // długość całkowita w module 50 (2026-10-23)
                    pl.UBars.Add(new UBarRun { Back = backOut, ArmDir = -1, A = arm, B = bU, Dia = MainDia });
                    pl.UBars.Add(new UBarRun { Back = backIn, ArmDir = +1, A = arm, B = bU, Dia = MainDia });
                    pl.Messages.Add($"U-bary H{MainDia} A = {arm:F0} na obu końcach (zakład {2 * arm - span:F0} ≥ {lap:F0}).");
                }
                else
                {
                    double a2 = BeamGeometry.UBarArmForModule(BeamGeometry.Ceil25(lap + UBarArmExtra), bU, MainDia);
                    pl.UBars.Add(new UBarRun { Back = backOut, ArmDir = -1, A = a2, B = bU, Dia = MainDia });
                    pl.UBars.Add(new UBarRun { Back = backIn, ArmDir = +1, A = a2, B = bU, Dia = MainDia });
                    double s0 = backIn + a2 - lap, s1 = backOut - a2 + lap;
                    double need = BeamGeometry.Ceil25(s1 - s0);   // jeden pręt — w górę do 25 (zakłady nie mniejsze niż zakład)
                    if (need <= maxLen + 0.5)
                        pl.Straight.Add(new StraightRun { S0 = (s0 + s1 - need) / 2.0, S1 = (s0 + s1 + need) / 2.0, Dia = MainDia });
                    else
                        foreach (var (o, len) in BeamGeometry.PlanBars(s1 - s0, maxLen, lap))
                            pl.Straight.Add(new StraightRun { S0 = s0 + o, S1 = s0 + o + len, Dia = MainDia });
                    pl.Messages.Add($"ramię U-bara {arm:F0} > {MaxUBarArm:F0} — U-bary A = {a2:F0} + pręty proste H{MainDia}.");
                }
            }
            else
            {
                double armMax = Math.Min(BeamGeometry.Ceil25(backOut), MaxUBarArm);
                double arm = BeamGeometry.UBarArmForModule(armMax, bU, MainDia, maxArm: armMax);
                pl.UBars.Add(new UBarRun { Back = backOut, ArmDir = -1, A = arm, B = bU, Dia = MainDia });
                // pręty proste H12 górą i dołem: od 1000 za krawędź (w płycie) do zakładu z ramieniem U-bara (zakład wg większej średnicy)
                double end = backOut - arm + lap;
                double len = BeamGeometry.Ceil25(end + StraightIntoSlab);
                pl.Straight.Add(new StraightRun { S0 = end - len, S1 = end, Dia = StraightIntoSlabDia });
                pl.Messages.Add($"bez strefy przy palu — U-bar H{MainDia} A = {arm:F0} we wsporniku + pręty proste H{StraightIntoSlabDia} " +
                                $"{len:F0} ({StraightIntoSlab:F0} za krawędź, zakład {lap:F0}).");
            }

            // pręty boczne — zasady belek (tryb B3): poziomy z wysokości GB, U-bary UB SIDE na końcach, B = B strzemienia − 2·Ø w dół do 5
            int levels = BeamGeometry.SideLevelsForHeight(h);
            if (levels > 0)
            {
                int dS = SideDia;
                double lapS = BeamGeometry.Ceil25(40.0 * dS);
                double bS = Math.Floor((BeamGeometry.StirrupWidth(width, cover) - 2 * dLink) / 5.0 + 1e-9) * 5.0;
                pl.SideLevels = levels; pl.SideDia = dS;
                pl.SideT0 = cover + dLink + dS / 2.0; pl.SideT1 = width - pl.SideT0;
                // uwaga Bartka 2026-10-23 (przekrój A-A — kolizja): tył UB SIDE W ŚRODKU tyłu U-barów głównych (o Ø U-bara)
                double sOut = backOut - MainDia;
                if (root)
                {
                    double backIn = -rootLen + cover + LinkFor(rootH).Dia + MainDia;   // UB SIDE — w środku strzemion strefy i U-bara
                    double span = sOut - backIn;
                    double arm = BeamGeometry.Ceil25((span + lapS) / 2.0);
                    if (arm <= MaxUBarArm + 0.5)
                    {
                        pl.SideUBars.Add(new UBarRun { Back = sOut, ArmDir = -1, A = arm, B = bS, Dia = dS });
                        pl.SideUBars.Add(new UBarRun { Back = backIn, ArmDir = +1, A = arm, B = bS, Dia = dS });
                    }
                    else
                    {
                        double a2 = BeamGeometry.Ceil25(lapS + dS);   // jak koniec belki (UB SIDE 525)
                        pl.SideUBars.Add(new UBarRun { Back = sOut, ArmDir = -1, A = a2, B = bS, Dia = dS });
                        pl.SideUBars.Add(new UBarRun { Back = backIn, ArmDir = +1, A = a2, B = bS, Dia = dS });
                        double s0 = backIn + a2 - lapS, s1 = sOut - a2 + lapS;
                        foreach (var (o, len) in BeamGeometry.PlanBars(s1 - s0, maxLen, lapS))
                            pl.SideStraight.Add(new StraightRun { S0 = s0 + o, S1 = s0 + o + len, Dia = dS });
                    }
                }
                else
                {
                    // bez strefy przy palu — UB SIDE tylko we wsporniku (ramię do nasady, maks. 1300); w płytę bez prętów bocznych
                    double arm = Math.Min(BeamGeometry.Ceil25(sOut), MaxUBarArm);
                    pl.SideUBars.Add(new UBarRun { Back = sOut, ArmDir = -1, A = arm, B = bS, Dia = dS });
                }
                pl.Messages.Add($"pręty boczne H{dS}: {levels} na bok (h {h:F0}, jak belki) — UB SIDE " +
                                string.Join(" + ", pl.SideUBars.Select(u => $"A {u.A:F0} B {u.B:F0}")) +
                                (pl.SideStraight.Count > 0 ? $", pręty proste {pl.SideStraight.Count}" : "") + ".");
            }
            return pl;
        }

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

        /// <summary>Bez punktów współliniowych na prostych odcinkach (kierunek bez zmiany, odchyłka ≤ Tol).</summary>
        private static List<(double X, double Y)> Straight(List<(double X, double Y)> p)
        {
            bool changed = true;
            while (changed && p.Count > 3)
            {
                changed = false;
                for (int i = 0; i < p.Count; i++)
                {
                    var a = p[(i - 1 + p.Count) % p.Count]; var b = p[i]; var c = p[(i + 1) % p.Count];
                    var ac = (c.X - a.X, c.Y - a.Y); double L = Len(ac);
                    if (L < 1e-9) continue;
                    double dev = Math.Abs((b.X - a.X) * ac.Item2 - (b.Y - a.Y) * ac.Item1) / L;
                    double t = ((b.X - a.X) * ac.Item1 + (b.Y - a.Y) * ac.Item2) / (L * L);
                    if (dev <= Tol && t > 0 && t < 1) { p.RemoveAt(i); changed = true; break; }
                }
            }
            return p;
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
