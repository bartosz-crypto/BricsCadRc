#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Przekrój płyty (RC_SECTION) — czysta geometria, bez BricsCAD API.
    /// Wejście: obrys płyty, otwory, niby, pale i rozkłady prętów w układzie rzutu (WCS rzutu wskazanego),
    /// linia cięcia (pozioma albo pionowa) i kierunek patrzenia. Wyjście: elementy przekroju w układzie
    /// lokalnym (u w prawo, v w górę, v = 0 — wierzch płyty / SSL), rysowane 1:1.
    /// </summary>
    public static class SectionPlanner
    {
        /// <summary>
        /// Wersja logiki planera (bezpiecznik przekrojów, SectionFingerprint): podbić przy KAŻDEJ zmianie wyniku dla tego samego
        /// wejścia (złoty test tests/SectionPlannerGoldenTests). Przekrój narysowany starszą wersją: „aktualny (nowa wersja
        /// rysowałaby inaczej)” — decyzja użytkownika P2.
        /// </summary>
        public const int Version = 1;

        // ── wejście ─────────────────────────────────────────────────────────

        /// <summary>Rodzaj rozkładu; Beam — pręty belki (kody BB / BT / BL, plan belek E5): tylko w pasie belki przeciętym linią cięcia.</summary>
        public enum DistKind { Main, UB, NibUB, TInNib, Add, Beam }

        public sealed class Dist
        {
            public string Mark = "";
            public string PosNr = "";        // np. "05", "107"
            public string LayerCode = "B1";  // B1/B2/T1/T2
            public DistKind Kind = DistKind.Main;
            public bool   AlongX;            // pręty wzdłuż X (rozkład wzdłuż Y)
            public double X0, Y0;            // punkt wstawienia (początek pierwszego pręta), układ rzutu wskazanego
            public double LengthA;           // długość pręta w rzucie (oś pręta)
            public double BarsSpan;          // zakres rozkładu (oś rozkładu)
            public int    Count = 1;
            public double Spacing = 200;
            public int    Diameter = 10;
            public string ShapeCode = "00";
            public string SymbolSide = "";   // UB: "Left" — krawędź przy początku pręta, "Right" — przy końcu
            /// <summary>Oś pręta wzorcowego (RC_SINGLE_BAR) w układzie lokalnym kształtu — „kopia pręta” w widoku; null = schemat.</summary>
            public List<(double X, double Y)> ShapeLocal;

            // ── źródło (bezpiecznik przekrojów; nie wpływa na wynik planera) ──
            /// <summary>Handle rozkładu RC_BAR_BLOCK („X8”), z którego pochodzi wpis; "" gdy brak.</summary>
            public string SourceHandle = "";
            /// <summary>Wymiary A–E pręta wzorcowego (RC_SINGLE_BAR); null gdy brak pręta.</summary>
            public double[] SourceDims;
            /// <summary>Handle pręta wzorcowego rozkładu („X8”); "" gdy brak.</summary>
            public string SourceBarHandle = "";

            // ── belki (plan belek E5; tylko Kind = Beam) ──
            /// <summary>Skos rozkładu (pręty L narożnika): przesunięcie początku pierwszego / ostatniego pręta wzdłuż osi pręta.</summary>
            public double SkewStart, SkewEnd;
            /// <summary>U-bar (kształt 21): drugie ramię w odległości PairOffset (oś–oś, ze znakiem) wzdłuż osi rozkładu; 0 = brak.</summary>
            public double PairOffset;
            /// <summary>E4b: pręty boczne (BS) — liczba prętów na bok (poziomy między prętami dołu i góry); 1 = jeden poziom.</summary>
            public int Layers = 1;
        }

        /// <summary>
        /// Belka / pogrubienie (plan belek E5) przecięte linią cięcia: pas trzonu belki wzdłuż cięcia [S0, S1] (współrzędna wzdłuż
        /// cięcia, układ rzutu wskazanego — jak S0 / S1 wejścia), wysokość H od wierzchu płyty, nib przy spodzie (wysokość NibHeight)
        /// między licem pasu a krawędzią płyty, gdy krawędź jest najwyżej <see cref="BeamNibMaxGap"/> od lica. Pusta lista
        /// <see cref="Input.Beams"/> = wynik planera bit w bit jak przed E5.
        /// </summary>
        public sealed class BeamBand
        {
            /// <summary>Handle strefy RC-BEAM („X8”) — składnik odcisku BEAM:&lt;handle&gt;.</summary>
            public string Handle = "";
            public double S0, S1;
            public double Height;
            public double NibWidth = 135, NibHeight = 150;
            /// <summary>Otulina strzemienia: boczna / czołowa i dolna (spód belki i nibu) [mm] — decyzja użytkownika 2026-10-10: 45 / 50.</summary>
            public double Cover = BeamGeometry.DefaultCoverSide, CoverBottom = BeamGeometry.DefaultCoverBottom;
            /// <summary>Strzemię: średnica i wysokość zewnętrzna A (ze wzoru generowania; rozkład BL w pasie ma pierwszeństwo).</summary>
            public int StirrupDia = 10;
            public double StirrupA;
            /// <summary>Parametry rekordu RC_BEAM (tekst do odcisku, bez wpływu na rysunek).</summary>
            public string Params = "";
        }

        public sealed class NibEdge
        {
            public bool   Vertical;          // krawędź x = const
            public double OuterCoord, InnerCoord, Lo, Hi;
            public double Width => Math.Abs(InnerCoord - OuterCoord);
        }

        public sealed class Input
        {
            public List<(double X, double Y)> Outline = new List<(double, double)>();
            public List<(double MinX, double MinY, double MaxX, double MaxY)> Holes = new List<(double, double, double, double)>();
            public List<NibEdge> Nibs = new List<NibEdge>();
            public List<(double X, double Y, double R)> Piles = new List<(double, double, double)>();
            public List<Dist> Dists = new List<Dist>();
            /// <summary>Belki przecięte linią cięcia (plan belek E5); pusta = przekrój jak przed E5 (bit w bit).</summary>
            public List<BeamBand> Beams = new List<BeamBand>();

            /// <summary>Linia cięcia pozioma (y = CutCoord, przekrój wzdłuż X) albo pionowa (x = CutCoord).</summary>
            public bool   CutAlongX = true;
            public double CutCoord;
            /// <summary>Zakres cięcia wzdłuż linii (współrzędna X albo Y), z punktów wskazanych.</summary>
            public double S0, S1;
            /// <summary>Kierunek patrzenia: +1 — w stronę rosnącej współrzędnej prostopadłej (Y albo X), −1 — malejącej.</summary>
            public int    LookSign = 1;

            public double Thickness = 225;
            public double NibHeight = 150;
            public double CoverBottom = 40, CoverTop = 35, CoverNib = 30;
            /// <summary>Zapas (z każdej strony) do otuliny góra / dół: pierwszy pręt 35 + 5 = 40 od góry, 40 + 5 = 45 od dołu (UB 225: B = 140).</summary>
            public double UbTolerance = 5;
            public string Ssl;               // np. "21.925" (null = bez znacznika)
            public bool   DrawPiles = true;
        }

        // ── wyjście ─────────────────────────────────────────────────────────

        public enum EndKind { Edge, Break, Hole }

        public sealed class Segment            // odcinek betonu wzdłuż cięcia
        {
            public double U0, U1;
            public EndKind End0, End1;
            public double Nib0, Nib1;           // szerokość nibu przy końcu (0 = brak)
            public List<(double U, double V)> Polygon = new List<(double, double)>();
            /// <summary>
            /// Belki (E5): profil betonu odcinka — przedziały u z dołem i wierzchem (spód w pasie belki na −H, nib belki przy spodzie);
            /// null = odcinek bez belki (profil jak przed E5).
            /// </summary>
            public List<(double U0, double U1, double Bot, double Top)> Prof;
        }

        public sealed class Dot
        {
            public double U, V; public int Diameter; public string PosNr; public bool Top; public bool Mesh; public bool TinNib; public double NomSpacing = 200;
            /// <summary>Belki (E5): pręt belki (bez przesuwania przy ramionach prętów w widoku); Bent — ramię pręta giętego (11 / 21).</summary>
            public bool Beam, Bent;
        }

        public sealed class BarLine
        {
            public List<(double U, double V)> Path = new List<(double, double)>();
            public int Diameter; public string PosNr; public bool Top;
            public DistKind Kind;
            /// <summary>Obrys pręta z grubością (zamknięty), jak pręt w elewacji.</summary>
            public List<(double U, double V)> Outline;
        }

        public sealed class Pile
        {
            public double U, Width, Top, Bottom;
            /// <summary>Spód betonu nad palem (belka E5: −H); NaN = spód płyty −T (jak przed E5).</summary>
            public double Base = double.NaN;
        }

        public sealed class Dim
        {
            public double U1, V1, U2, V2;       // punkty mierzone
            public double LineU, LineV;         // punkt na linii wymiarowej
            public bool   Vertical;
        }

        public sealed class Label
        {
            public string Text; public double U, V;   // V = linia bazowa tekstu, U = środek
            public double Height;
            public bool   HasLeader; public double LeaderU, LeaderV;  // grot na pręcie
        }

        public sealed class Result
        {
            public List<Segment> Segments = new List<Segment>();
            public List<Dot> Dots = new List<Dot>();
            public List<BarLine> Bars = new List<BarLine>();
            public List<Pile> Piles = new List<Pile>();
            public List<Dim> Dims = new List<Dim>();
            public List<Label> Labels = new List<Label>();
            public (double U, double V)? SslMark;     // wierzchołek trójkąta poziomu (na wierzchu płyty)
            public string SslText;
            public double UMin, UMax;
            public double NibTop;                     // v wierzchu nibu (0 gdy brak)
            public List<string> Warnings = new List<string>();

            // ── istotne wejście (bezpiecznik przekrojów; nie wpływa na rysunek) ──
            /// <summary>Rozkłady, które dały ≥ 1 kropkę albo pręt w widoku (oznaczane w chwili dodania, przed AlignMesh), kolejność wejścia.</summary>
            public List<Dist> UsedDists = new List<Dist>();
            /// <summary>Pale narysowane w przekroju (wejście X, Y, R), kolejność wejścia.</summary>
            public List<(double X, double Y, double R)> UsedPiles = new List<(double X, double Y, double R)>();
            /// <summary>Średnice wyznaczające poziomy B2 / T2 (dB1, dT1 — ze WSZYSTKICH rozkładów Main warstwy).</summary>
            public (double B1, double T1) LevelDiameters;
            /// <summary>Belki (E5) narysowane w przekroju (pas przecina odcinek betonu), kolejność wejścia — składniki BEAM odcisku.</summary>
            public List<BeamBand> UsedBeams = new List<BeamBand>();
        }

        /// <summary>E4b: Ø spinki UB 03 w nibie belki (pręty BN w jej narożach).</summary>
        public const double BeamNibUbDia = 10;

        /// <summary>E4b: pręt w nibie górą („… BEAM NIB T”); inaczej dołem.</summary>
        private static bool IsNibTop(Dist d) => (d.Mark ?? "").TrimEnd().EndsWith(BeamGeometry.SuffixNibTop, StringComparison.OrdinalIgnoreCase);

        /// <summary>Nib belki: krawędź płyty najwyżej tyle od lica pasu belki (jak BeamGeometry.EdgeEndMaxGap) [mm].</summary>
        public const double BeamNibMaxGap = 260.0;
        /// <summary>Wymiar szerokości / długości belki: linia wymiarowa tyle pod spodem belki [mm].</summary>
        public const double BeamDimBelow = 350.0;

        /// <summary>Pas belki w układzie przekroju (u) na jednym odcinku betonu.</summary>
        private sealed class BandU
        {
            public BeamBand Src;
            public Segment Seg;
            public double U0, U1, H, NibH;
            /// <summary>Nib belki przy lewym / prawym licu pasu (szerokość do krawędzi odcinka; 0 = brak).</summary>
            public double Nib0, Nib1;
            /// <summary>Strzemię w pasie: średnica, wysokość zewnętrzna A (z rozkładu BL w pasie albo ze wzoru).</summary>
            public double LinkDia, LinkA;
            /// <summary>Średnice prętów dołu / góry w pasie (E4b: poziomy prętów bocznych; z rozkładów BB / BT, domyślnie 16).</summary>
            public double MainDiaB = 16, MainDiaT = 16;
            /// <summary>Spinka UB 03 w pasie / nibie belki (poprawka recenzji E4b): osie dolnego i górnego ramienia na wysokości belki
            /// i Ø — pręty BN w jej narożach (wewnątrz U); NaN = brak UB 03 w pasie (poziomy BN ze wzoru).</summary>
            public double UbBot = double.NaN, UbTop = double.NaN, UbDia;
            public double VLinkBot => -H + Src.CoverBottom;            // spód strzemienia (lico zewnętrzne)
            public double VLinkTop => VLinkBot + LinkA;          // wierzch strzemienia (lico zewnętrzne)
        }

        /// <summary>Oznacza rozkład jako użyty (raz, referencja).</summary>
        private static void MarkUsed(Result res, Dist d)
        {
            foreach (var u in res.UsedDists) if (ReferenceEquals(u, d)) return;
            res.UsedDists.Add(d);
        }

        public const double TextHeight   = 50.0;   // 2.5 mm w 1:20
        public const double LabelGap     = 15.0;
        public const double LabelRowStep = 75.0;
        public const double PileEmbed    = 35.0;
        public const double PileDepth    = 230.0;
        /// <summary>Wymiary: linia wymiarowa poziomego (szerokość nibu) nad wierzchem płyty, pionowych — od krawędzi.</summary>
        public const double DimHorizV    = 300.0;
        public const double DimOffset    = 250.0;

        // ── plan przekroju ──────────────────────────────────────────────────

        public static Result Plan(Input inp)
        {
            var res = new Result();
            double T = inp.Thickness;
            double s0 = Math.Min(inp.S0, inp.S1), s1 = Math.Max(inp.S0, inp.S1);
            int sign = inp.CutAlongX ? (inp.LookSign >= 0 ? 1 : -1) : (inp.LookSign >= 0 ? -1 : 1);
            double U(double s) => sign * s;
            double c = inp.CutCoord;

            // 1. Odcinki betonu (w s), z rodzajem końca
            var ivs = Intervals(inp, s0, s1);
            if (ivs.Count == 0) { res.Warnings.Add("Linia cięcia nie przecina płyty."); return res; }

            double vNibTop = -(T - inp.NibHeight);
            res.NibTop = vNibTop;
            foreach (var iv in ivs)
            {
                double nibLo = iv.k0 == EndKind.Edge ? NibWidthAt(inp, iv.a) : 0;
                double nibHi = iv.k1 == EndKind.Edge ? NibWidthAt(inp, iv.b) : 0;
                // w układzie u: lewy/prawy koniec zależnie od kierunku patrzenia
                var seg = sign > 0
                    ? new Segment { U0 = U(iv.a), U1 = U(iv.b), End0 = iv.k0, End1 = iv.k1, Nib0 = nibLo, Nib1 = nibHi }
                    : new Segment { U0 = U(iv.b), U1 = U(iv.a), End0 = iv.k1, End1 = iv.k0, Nib0 = nibHi, Nib1 = nibLo };
                if (seg.Nib0 + seg.Nib1 >= seg.U1 - seg.U0 - 1.0) { seg.Nib0 = seg.Nib1 = 0; }
                seg.Polygon = Profile(seg, T, vNibTop);
                res.Segments.Add(seg);
            }
            res.Segments.Sort((x, y) => x.U0.CompareTo(y.U0));
            res.UMin = res.Segments.First().U0;
            res.UMax = res.Segments.Last().U1;

            // 1a. Belki (plan belek E5): pasy belek na odcinkach betonu → profil (spód w pasie na −H, nib belki przy spodzie
            //     po stronie krawędzi). Brak belek — nic się nie zmienia (wynik bit w bit jak przed E5).
            var bands = new List<BandU>();
            foreach (var bm in inp.Beams ?? new List<BeamBand>())
            {
                if (bm == null || bm.Height <= 0) continue;
                double a = Math.Max(Math.Min(bm.S0, bm.S1), s0), b = Math.Min(Math.Max(bm.S0, bm.S1), s1);
                if (b - a < 1.0) continue;
                double bu0 = Math.Min(U(a), U(b)), bu1 = Math.Max(U(a), U(b));
                foreach (var sg in res.Segments)
                {
                    double x0 = Math.Max(bu0, sg.U0), x1 = Math.Min(bu1, sg.U1);
                    if (x1 - x0 < 1.0) continue;
                    var bu = new BandU { Src = bm, Seg = sg, U0 = x0, U1 = x1, H = bm.Height, NibH = bm.NibHeight,
                                         LinkDia = bm.StirrupDia > 0 ? bm.StirrupDia : 10, LinkA = bm.StirrupA };
                    if (sg.End0 == EndKind.Edge) { double g = x0 - sg.U0; if (g > 1.0 && g <= BeamNibMaxGap) bu.Nib0 = g; }
                    if (sg.End1 == EndKind.Edge) { double g = sg.U1 - x1; if (g > 1.0 && g <= BeamNibMaxGap) bu.Nib1 = g; }
                    bands.Add(bu);
                    if (!res.UsedBeams.Any(x => ReferenceEquals(x, bm))) res.UsedBeams.Add(bm);
                }
            }
            foreach (var sg in res.Segments)
            {
                var bs = bands.Where(x => x.Seg == sg).ToList();
                if (bs.Count == 0) continue;
                sg.Prof = BeamProfile(sg, bs, T, vNibTop);
                sg.Polygon = ProfilePolygon(sg);
            }

            bool InConcrete(double u, double v)
            {
                foreach (var sg in res.Segments)
                {
                    if (u < sg.U0 - 0.5 || u > sg.U1 + 0.5) continue;
                    if (sg.Prof != null)
                    {
                        // belka (E5): profil odcinka
                        foreach (var p in sg.Prof)
                            if (u >= p.U0 - 0.5 && u <= p.U1 + 0.5 && v > p.Bot - 0.5 && v < p.Top + 0.5) return true;
                        continue;
                    }
                    if (v > -T - 0.5 && v < 0.5 && (v < vNibTop + 0.5 || (u >= sg.U0 + sg.Nib0 - 0.5 && u <= sg.U1 - sg.Nib1 + 0.5)))
                        return true;
                }
                return false;
            }
            // spód betonu w punkcie u (belka: najniższy spód profilu); bez belki — dokładnie −T (wynik bit w bit)
            double BotAt(double u) => LocalBottom(res, u, T);
            // UB 03 / T IN NIB w pasie belki albo w jej nibie (E5): poziomy względem spodu belki / wierzchu nibu belki
            double BeamNibShift(double u, bool tin)
            {
                foreach (var b in bands)
                    if (u >= b.U0 - b.Nib0 - 0.5 && u <= b.U1 + b.Nib1 + 0.5)
                    {
                        // T IN NIB w trzonie belki (drugi pręt „150 dalej” — za licem belki): poziom jak w płycie, bez przesunięcia
                        // do nibu belki (pręt jest w rysunku — E2 wstrzymany; nie udaje pręta nibu belki)
                        if (tin && u > b.U0 + 0.5 && u < b.U1 - 0.5) return 0;
                        return tin ? (-b.H + b.NibH) - vNibTop : T - b.H;
                    }
                return 0;
            }

            // 2. Poziomy warstw
            double DiaOf(string code) => inp.Dists.Where(d => d.LayerCode == code && d.Kind == DistKind.Main)
                                                  .Select(d => (double)d.Diameter).DefaultIfEmpty(code.StartsWith("T") ? 12 : 10).Max();
            double dB1 = DiaOf("B1"), dT1 = DiaOf("T1");
            res.LevelDiameters = (dB1, dT1);
            double cbT = inp.CoverBottom + inp.UbTolerance, ctT = inp.CoverTop + inp.UbTolerance;
            string Dir(string code) => code != null && code.Length > 1 ? code.Substring(1) : "1";
            double Level(string code, double d)
            {
                switch (code)
                {
                    // otulina + zapas (np. góra 35 + 5 → pierwszy pręt 40 od krawędzi, dół 40 + 5 → 45)
                    case "B1": return -T + cbT + d / 2;
                    case "B2": return -T + cbT + dB1 + d / 2;
                    case "T1": return -ctT - d / 2;
                    case "T2": return -ctT - dT1 - d / 2;
                    default:   return code.StartsWith("T") ? -ctT - d / 2 : -T + cbT + d / 2;
                }
            }
            double vNibBar(double d) => vNibTop - inp.CoverNib - d / 2;
            // U-bary (UB, UB 03): dolne ramię na otulinie dołem + zapas, wysokość z pręta wzorcowego (wymiar zewnętrzny)
            // ramiona U-bara na poziomie prętów głównych swojego kierunku (B1/T1 albo B2/T2) — pręty jednego kierunku
            // leżą na jednej linii i nachodzą na siebie (jak na rysunkach ASD); wysokość U z pręta wzorcowego
            double vUbBot(Dist x, double d) => Level("B" + Dir(x.LayerCode), d);
            double vUbTop(Dist x, double d) => x.ShapeLocal != null
                ? vUbBot(x, d) + CrossOf(x.ShapeLocal)
                : (x.Kind == DistKind.NibUB ? vNibTop - inp.CoverNib - d / 2 : Level("T" + Dir(x.LayerCode), d));

            // belka (E4b, poprawka recenzji): spinka UB 03 w pasie belki albo w jej nibie — ramiona na wysokości belki (jak niżej
            // w 3. z BeamNibShift); pręty BN leżą w jej narożach. Bez belek pętla pusta (wynik bit w bit).
            foreach (var b in bands)
            {
                double lo = b.U0 - b.Nib0 - 1.0, hi = b.U1 + b.Nib1 + 1.0;
                foreach (var d in inp.Dists.Where(x => x.Kind == DistKind.NibUB))
                {
                    int n = Math.Max(1, d.Count);
                    double sp = n > 1 ? (d.BarsSpan > 0 ? d.BarsSpan / (n - 1) : d.Spacing) : 0;
                    double p0 = d.AlongX ? d.X0 : d.Y0, q0 = d.AlongX ? d.Y0 : d.X0;
                    bool hit;
                    if (d.AlongX != inp.CutAlongX)
                        hit = c >= p0 - 1.0 && c <= p0 + d.LengthA + 1.0
                              && Enumerable.Range(0, n).Any(i => { double u = U(q0 + i * sp); return u >= lo && u <= hi; });
                    else
                    {
                        double best = Enumerable.Range(0, n).Min(i => Math.Abs(q0 + i * sp - c));
                        double tol = Math.Max(n > 1 ? sp / 2.0 : d.Spacing / 2.0, 100.0) + 0.5;
                        double ua = U(p0), ub = U(p0 + d.LengthA);
                        hit = best <= tol && Math.Max(ua, ub) >= lo && Math.Min(ua, ub) <= hi;
                    }
                    if (!hit) continue;
                    double sh = T - b.H;
                    b.UbBot = vUbBot(d, d.Diameter) + sh;
                    b.UbTop = vUbTop(d, d.Diameter) + sh;
                    b.UbDia = d.Diameter;
                    // UB 03 to element płyty (otulina dołem płyty + zapas) — pręt BN dołem liczony od niej. K2 (praktyka biura): bez
                    // ostrzeżenia o otulinie UB 03 mniejszej niż otulina dolna belki (UB 03 przy belce zawsze — tak rysuje biuro)
                    break;
                }
            }

            // 3. Pręty
            foreach (var d in inp.Dists)
            {
                if (d.Kind == DistKind.Beam) continue;            // pręty belki — niżej (3c), tylko w pasie belki
                bool perpendicular = d.AlongX != inp.CutAlongX;   // pręty przecinają płaszczyznę cięcia
                double dia = d.Diameter;
                if (perpendicular)
                {
                    // oś pręta: Y dla prętów Y (cięcie wzdłuż X) — sprawdzamy, czy pręt dochodzi do linii cięcia
                    double p0 = d.AlongX ? d.X0 : d.Y0;              // początek pręta (oś pręta)
                    double q0 = d.AlongX ? d.Y0 : d.X0;              // pierwszy pręt (oś rozkładu = wzdłuż cięcia)
                    if (c < p0 - 1.0 || c > p0 + d.LengthA + 1.0) continue;
                    int n = Math.Max(1, d.Count);
                    double sp = n > 1 ? (d.BarsSpan > 0 ? d.BarsSpan / (n - 1) : d.Spacing) : 0;
                    var levels = new List<(double v, bool top)>();
                    switch (d.Kind)
                    {
                        case DistKind.UB:
                        case DistKind.NibUB:
                            levels.Add((vUbBot(d, dia), false));
                            levels.Add((vUbTop(d, dia), true));
                            break;
                        case DistKind.TInNib:
                            levels.Add((vNibBar(dia), true));
                            break;
                        default:
                            levels.Add((Level(d.LayerCode, dia), d.LayerCode.StartsWith("T")));
                            break;
                    }
                    for (int i = 0; i < n; i++)
                    {
                        double s = q0 + i * sp;
                        if (s < s0 - 0.5 || s > s1 + 0.5) continue;
                        foreach (var (v0, top) in levels)
                        {
                            double v = v0;
                            if (bands.Count > 0 && (d.Kind == DistKind.NibUB || d.Kind == DistKind.TInNib))
                                v += BeamNibShift(U(s), d.Kind == DistKind.TInNib);
                            if (!InConcrete(U(s), v)) continue;
                            res.Dots.Add(new Dot { U = U(s), V = v, Diameter = d.Diameter, PosNr = d.PosNr, Top = top,
                                                   Mesh = d.Kind == DistKind.Main, TinNib = d.Kind == DistKind.TInNib, NomSpacing = NominalSpacing(d) });
                            MarkUsed(res, d);
                        }
                    }
                }
                else
                {
                    // pręty równoległe do cięcia: pręt najbliższy linii cięcia (w granicach ½ rozstawu)
                    double q0 = d.AlongX ? d.Y0 : d.X0;              // oś rozkładu = prostopadle do cięcia
                    int n = Math.Max(1, d.Count);
                    double sp = n > 1 ? (d.BarsSpan > 0 ? d.BarsSpan / (n - 1) : d.Spacing) : 0;
                    double best = double.MaxValue;
                    for (int i = 0; i < n; i++) best = Math.Min(best, Math.Abs(q0 + i * sp - c));
                    double tol = Math.Max(n > 1 ? sp / 2.0 : d.Spacing / 2.0, 100.0) + 0.5;
                    if (best > tol) continue;

                    double a = d.AlongX ? d.X0 : d.Y0, b = a + d.LengthA;   // oś pręta = wzdłuż cięcia
                    if (b < s0 - 1.0 || a > s1 + 1.0) continue;
                    var paths = new List<(List<(double s, double v)> path, bool top)>();
                    bool edgeAtLow = string.Equals(d.SymbolSide, "Left", StringComparison.OrdinalIgnoreCase);
                    double sEdge = edgeAtLow ? a : b, sIn = edgeAtLow ? b : a;
                    switch (d.Kind)
                    {
                        case DistKind.UB:
                        {
                            double vb = vUbBot(d, dia), vt = vUbTop(d, dia);
                            paths.Add((new List<(double, double)> { (sIn, vt), (sEdge, vt), (sEdge, vb), (sIn, vb) }, true));
                            break;
                        }
                        case DistKind.NibUB:
                        {
                            double vb = vUbBot(d, dia), vt = vUbTop(d, dia);
                            if (bands.Count > 0) { double sh = BeamNibShift(U(sEdge), false); vb += sh; vt += sh; }   // belka (E5)
                            paths.Add((new List<(double, double)> { (sIn, vt), (sEdge, vt), (sEdge, vb), (sIn, vb) }, false));
                            break;
                        }
                        case DistKind.TInNib:
                        {
                            double v = vNibBar(dia);
                            paths.Add((new List<(double, double)> { (a, v), (b, v) }, true));
                            break;
                        }
                        default:
                        {
                            double v = Level(d.LayerCode, dia);
                            paths.Add((new List<(double, double)> { (a, v), (b, v) }, d.LayerCode.StartsWith("T")));
                            break;
                        }
                    }
                    foreach (var (path, top) in paths)
                    {
                        // kopia pręta wzorcowego (kształt z RC_SINGLE_BAR) dopasowana do położenia w przekroju
                        bool isU = d.Kind == DistKind.UB || d.Kind == DistKind.NibUB;
                        if (d.ShapeLocal != null && d.ShapeLocal.Count >= 2 && (isU || d.ShapeCode != "00"))
                        {
                            var target = path.Select(p => (U(p.s), p.v)).ToList();
                            var axis = FitShape(d.ShapeLocal, target, isU, top, alignTop: false);
                            if (axis != null && axis.Any(p => p.Item1 >= Math.Min(U(s0), U(s1)) - 1 && p.Item1 <= Math.Max(U(s0), U(s1)) + 1))
                            {
                                res.Bars.Add(new BarLine { Path = axis, Outline = OutlineOf(axis, dia),
                                                           Diameter = d.Diameter, PosNr = d.PosNr, Top = top, Kind = d.Kind });
                                MarkUsed(res, d);
                                continue;
                            }
                        }
                        // przytnij do zakresu cięcia i betonu (otwory, końce cięcia)
                        foreach (var piece in ClipPath(path, s0, s1))
                        {
                            var up = piece.Select(p => (U(p.s), p.v)).ToList();
                            foreach (var part in SplitByConcrete(up, InConcrete))
                                if (part.Count >= 2)
                                {
                                    res.Bars.Add(new BarLine { Path = part, Outline = OutlineOf(part, dia),
                                                               Diameter = d.Diameter, PosNr = d.PosNr, Top = top });
                                    MarkUsed(res, d);
                                }
                        }
                    }
                }
            }

            // 3c. Pręty belki (E5): kropki 4 H16 dół / góra i strzemię 51 w widoku (cięcie prostopadłe do belki), pręty w widoku
            //     i strzemiona jako pionowe ramiona (cięcie wzdłuż belki); tylko w pasie belki przeciętym linią cięcia
            if (bands.Count > 0) PlaceBeamDists(res, inp, bands, U, s0, s1, c, InConcrete);

            // 3a. Siatka w przekroju (schemat jak na rysunkach ASD): dół i góra w TYCH SAMYCH miejscach.
            //     Przy nibie: jeden pręt dołu w nibie (otulina od krawędzi zewnętrznej), pierwszy pręt siatki 150 dalej,
            //     reszta co rozstaw nominalny — jakby nibu nie było. Krawędź bez nibu: pierwszy pręt na otulinie.
            AlignMesh(res, inp, InConcrete);

            // T IN NIB w spince UB 03: tuż pod jej górnym ramieniem (jak na rysunkach ASD)
            foreach (var dot in res.Dots.Where(x => x.TinNib))
            {
                // belka (E5): T IN NIB w trzonie belki zostaje na poziomie płyty (BeamNibShift) — nie pod ramieniem UB 03
                if (bands.Any(b => dot.U > b.U0 + 0.5 && dot.U < b.U1 - 0.5)) continue;
                var hp = res.Bars.Where(b => b.Kind == DistKind.NibUB)
                                 .Where(b => b.Path.Min(q => q.U) - 1 <= dot.U && b.Path.Max(q => q.U) + 1 >= dot.U)
                                 .FirstOrDefault();
                if (hp == null) continue;
                double topLeg = hp.Path.Max(q => q.V);
                dot.V = topLeg - hp.Diameter / 2.0 - dot.Diameter / 2.0;
            }

            // 3b. Kolizje: kropka nie może leżeć na pionowym ramieniu pręta w widoku (np. UB) — przesunięcie w głąb płyty
            ResolveDotCollisions(res);
            // belka (E4b): T IN NIB nie może nachodzić na pręt belki (BN w narożu spinki UB 03) — odsunięcie w poziomie do styku lic
            if (bands.Count > 0) ResolveTinBeamCollisions(res);

            // 4. Pale
            if (inp.DrawPiles)
                foreach (var (px, py, r) in inp.Piles)
                {
                    double perp = inp.CutAlongX ? py : px, along = inp.CutAlongX ? px : py;
                    double dd = Math.Abs(perp - c);
                    if (dd >= r || along < s0 || along > s1) continue;
                    double hw = Math.Sqrt(r * r - dd * dd);
                    if (hw < 50.0) continue;
                    double u = U(along);
                    double bot = BotAt(u);                       // belka (E5): pal pod spodem belki; bez belki = −T
                    if (!InConcrete(u, bot + 1.0)) continue;
                    res.Piles.Add(new Pile { U = u, Width = 2 * hw, Top = bot + PileEmbed, Bottom = bot - PileDepth, Base = bot });
                    res.UsedPiles.Add((px, py, r));
                }

            // 5. Wymiary: grubość przy lewym końcu, nib (szerokość, uskok, wysokość) przy każdym końcu z nibem
            var first = res.Segments.First();
            bool leftHasNib = first.Nib0 > 0;
            foreach (var sg in res.Segments)
            {
                // belka (E5) przy końcu odcinka: wymiary nibu belki zamiast nibu płyty; bez belek — jak przed E5
                var bs = bands.Where(x => x.Seg == sg).ToList();
                var bL = bs.FirstOrDefault(x => x.Nib0 > 0 || x.U0 - sg.U0 <= 1.0);
                var bR = bs.FirstOrDefault(x => x.Nib1 > 0 || sg.U1 - x.U1 <= 1.0);
                if (bL != null) { if (bL.Nib0 > 0) AddNibDims(res, sg.U0, +1, bL.Nib0, bL.H, -bL.H + bL.NibH); }
                else if (sg.Nib0 > 0) AddNibDims(res, sg.U0, +1, sg.Nib0, T, vNibTop);
                if (bR != null) { if (bR.Nib1 > 0) AddNibDims(res, sg.U1, -1, bR.Nib1, bR.H, -bR.H + bR.NibH); }
                else if (sg.Nib1 > 0) AddNibDims(res, sg.U1, -1, sg.Nib1, T, vNibTop);
                foreach (var b in bs) AddBeamDims(res, sg, b);
            }
            var bandL = bands.FirstOrDefault(x => x.Seg == first && (x.Nib0 > 0 || x.U0 - first.U0 <= 1.0));
            double off = bandL != null || leftHasNib ? DimOffset + 200 : DimOffset;
            if (bandL == null)
                res.Dims.Add(new Dim { U1 = first.U0, V1 = 0, U2 = first.U0, V2 = -T, LineU = first.U0 - off, LineV = -T / 2, Vertical = true });
            else
            {
                // belka przy lewym końcu (E5): grubość płyty przy prawym końcu, gdy tam belki nie ma; inaczej przy licu wewnętrznym belki
                var last = res.Segments.Last();
                bool rightBeam = bands.Any(x => x.Seg == last && (x.Nib1 > 0 || last.U1 - x.U1 <= 1.0));
                if (!rightBeam)
                {
                    double offR = last.Nib1 > 0 ? DimOffset + 200 : DimOffset;
                    res.Dims.Add(new Dim { U1 = last.U1, V1 = 0, U2 = last.U1, V2 = -T, LineU = last.U1 + offR, LineV = -T / 2, Vertical = true });
                }
                else
                    res.Dims.Add(new Dim { U1 = bandL.U1, V1 = 0, U2 = bandL.U1, V2 = -T, LineU = bandL.U1 + DimOffset, LineV = -T / 2, Vertical = true });
            }

            // 6. Poziom SSL
            if (!string.IsNullOrWhiteSpace(inp.Ssl))
            {
                res.SslMark = (first.U0 - off - 260.0, 0);
                res.SslText = inp.Ssl.Trim();
            }

            // 7. Opisy
            PlaceLabels(res, T);
            return res;
        }

        /// <summary>
        /// Wymiary belki (E5): H — przy końcu z nibem od wierzchu przy licu pasu do spodu przy krawędzi (dalej niż łańcuch uskok + nib),
        /// przy końcu bez nibu na krawędzi, w środku odcinka — przy prawym licu pasu; szerokość (długość wzdłuż cięcia) — pod spodem belki.
        /// </summary>
        private static void AddBeamDims(Result res, Segment sg, BandU b)
        {
            double far = DimOffset + 200;
            if (b.Nib0 > 0)
                res.Dims.Add(new Dim { U1 = b.U0, V1 = 0, U2 = sg.U0, V2 = -b.H, LineU = sg.U0 - far, LineV = -b.H / 2, Vertical = true });
            else if (b.Nib1 > 0)
                res.Dims.Add(new Dim { U1 = b.U1, V1 = 0, U2 = sg.U1, V2 = -b.H, LineU = sg.U1 + far, LineV = -b.H / 2, Vertical = true });
            else if (b.U0 - sg.U0 <= 1.0)
                res.Dims.Add(new Dim { U1 = sg.U0, V1 = 0, U2 = sg.U0, V2 = -b.H, LineU = sg.U0 - DimOffset, LineV = -b.H / 2, Vertical = true });
            else if (sg.U1 - b.U1 <= 1.0)
                res.Dims.Add(new Dim { U1 = sg.U1, V1 = 0, U2 = sg.U1, V2 = -b.H, LineU = sg.U1 + DimOffset, LineV = -b.H / 2, Vertical = true });
            else
                res.Dims.Add(new Dim { U1 = b.U1, V1 = 0, U2 = b.U1, V2 = -b.H, LineU = b.U1 + DimOffset, LineV = -b.H / 2, Vertical = true });
            res.Dims.Add(new Dim { U1 = b.U0, V1 = -b.H, U2 = b.U1, V2 = -b.H, LineU = (b.U0 + b.U1) / 2, LineV = -b.H - BeamDimBelow, Vertical = false });
        }

        // ── belki (plan belek E5) ───────────────────────────────────────

        /// <summary>Spód betonu w punkcie u: odcinek z profilem belki — najniższy spód przedziałów zawierających u; inaczej −T.</summary>
        private static double LocalBottom(Result res, double u, double T)
        {
            foreach (var sg in res.Segments)
            {
                if (sg.Prof == null || u < sg.U0 - 0.5 || u > sg.U1 + 0.5) continue;
                double bot = double.NaN;
                foreach (var p in sg.Prof)
                    if (u >= p.U0 - 0.5 && u <= p.U1 + 0.5) bot = double.IsNaN(bot) ? p.Bot : Math.Min(bot, p.Bot);
                if (!double.IsNaN(bot)) return bot;
            }
            return -T;
        }

        /// <summary>Wierzch betonu w punkcie u dla odcinka z profilem belki (najwyższy wierzch przedziałów zawierających u).</summary>
        private static double ProfTop(Segment sg, double u)
        {
            double top = double.NaN;
            foreach (var p in sg.Prof)
                if (u >= p.U0 - 0.5 && u <= p.U1 + 0.5) top = double.IsNaN(top) ? p.Top : Math.Max(top, p.Top);
            return double.IsNaN(top) ? 0 : top;
        }

        /// <summary>
        /// Profil odcinka z belką: przedziały u (punkty podziału — końce, niby płyty, lica pasów i ich nibów) z dołem i wierzchem:
        /// płyta −T..0 (nib płyty — wierzch vNibTop), pas belki −H..0, nib belki −H..−H + NibH. Sąsiednie równe przedziały scalane.
        /// </summary>
        private static List<(double U0, double U1, double Bot, double Top)> BeamProfile(Segment sg, List<BandU> bs, double T, double vNibTop)
        {
            var xs = new SortedSet<double> { sg.U0, sg.U1 };
            void X(double x) { if (x > sg.U0 + 1e-6 && x < sg.U1 - 1e-6) xs.Add(x); }
            if (sg.Nib0 > 0) X(sg.U0 + sg.Nib0);
            if (sg.Nib1 > 0) X(sg.U1 - sg.Nib1);
            foreach (var b in bs)
            {
                X(b.U0); X(b.U1);
                if (b.Nib0 > 0) X(b.U0 - b.Nib0);
                if (b.Nib1 > 0) X(b.U1 + b.Nib1);
            }
            var list = xs.ToList();
            var prof = new List<(double U0, double U1, double Bot, double Top)>();
            for (int i = 0; i + 1 < list.Count; i++)
            {
                double a = list[i], c = list[i + 1];
                if (c - a < 1e-6) continue;
                double m = (a + c) / 2;
                double bot = -T, top = 0;
                if (m < sg.U0 + sg.Nib0 || m > sg.U1 - sg.Nib1) top = vNibTop;
                foreach (var b in bs)
                {
                    if (m >= b.U0 && m <= b.U1) { bot = Math.Min(bot, -b.H); top = 0; }
                    else if (b.Nib0 > 0 && m >= b.U0 - b.Nib0 && m < b.U0) { bot = -b.H; top = -b.H + b.NibH; }
                    else if (b.Nib1 > 0 && m > b.U1 && m <= b.U1 + b.Nib1) { bot = -b.H; top = -b.H + b.NibH; }
                }
                if (prof.Count > 0 && Math.Abs(prof[prof.Count - 1].Bot - bot) < 1e-9 && Math.Abs(prof[prof.Count - 1].Top - top) < 1e-9)
                    prof[prof.Count - 1] = (prof[prof.Count - 1].U0, c, bot, top);
                else
                    prof.Add((a, c, bot, top));
            }
            return prof;
        }

        /// <summary>Obrys betonu odcinka z profilu belki: dół od lewej do prawej, prawy koniec (urwanie — zygzak), wierzch do lewej.</summary>
        private static List<(double U, double V)> ProfilePolygon(Segment sg)
        {
            var pts = new List<(double U, double V)>();
            void Add(double u, double v)
            {
                if (pts.Count > 0 && Math.Abs(pts[pts.Count - 1].U - u) < 1e-6 && Math.Abs(pts[pts.Count - 1].V - v) < 1e-6) return;
                pts.Add((u, v));
            }
            var prof = sg.Prof;
            foreach (var p in prof) { Add(p.U0, p.Bot); Add(p.U1, p.Bot); }
            var last = prof[prof.Count - 1];
            if (sg.End1 == EndKind.Break) foreach (var p in Zigzag(sg.U1, -last.Bot, up: true)) Add(p.U, p.V);
            Add(sg.U1, last.Top);
            for (int i = prof.Count - 1; i >= 0; i--) { Add(prof[i].U1, prof[i].Top); Add(prof[i].U0, prof[i].Top); }
            var firstP = prof[0];
            if (sg.End0 == EndKind.Break) foreach (var p in Zigzag(sg.U0, -firstP.Bot, up: false)) Add(p.U, p.V);
            // punkty współliniowe (przedziały różniące się tylko wierzchem / spodem) — usunięte
            var r = new List<(double U, double V)>();
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[(i - 1 + pts.Count) % pts.Count]; var b = pts[i]; var c = pts[(i + 1) % pts.Count];
                double cross = (b.U - a.U) * (c.V - b.V) - (b.V - a.V) * (c.U - b.U);
                if (Math.Abs(cross) > 1e-6) r.Add(b);
            }
            if (r.Count > 0 && Math.Abs(r[r.Count - 1].U - r[0].U) < 1e-6 && Math.Abs(r[r.Count - 1].V - r[0].V) < 1e-6) r.RemoveAt(r.Count - 1);
            return r;
        }

        /// <summary>
        /// Odcinki linii cięcia (współrzędna wzdłuż cięcia) wewnątrz wielokąta — np. strefy belki (PrepareSection, E5). Reguła półotwarta
        /// jak <see cref="Intervals"/>; odcinki krótsze niż 1 mm pomijane.
        /// </summary>
        public static List<(double A, double B)> CutIntervals(IList<(double X, double Y)> poly, bool cutAlongX, double c)
        {
            var xs = new List<double>();
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Count];
                double pc = cutAlongX ? p.Y : p.X, qc = cutAlongX ? q.Y : q.X;
                double ps = cutAlongX ? p.X : p.Y, qs = cutAlongX ? q.X : q.Y;
                if (Math.Abs(pc - qc) < 1e-9) continue;
                if (c < Math.Min(pc, qc) || c >= Math.Max(pc, qc)) continue;
                double t = (c - pc) / (qc - pc);
                xs.Add(ps + t * (qs - ps));
            }
            xs.Sort();
            var res = new List<(double A, double B)>();
            for (int i = 0; i + 1 < xs.Count; i += 2)
                if (xs[i + 1] - xs[i] > 1.0) res.Add((xs[i], xs[i + 1]));
            return res;
        }

        /// <summary>
        /// Pręty belki (Kind = Beam; E5) — tylko w pasie belki (u w pasie). Poziomy w pasie: strzemię 51 na otulinie od spodu belki
        /// (wysokość zewnętrzna A z rozkładu BL w pasie, inaczej ze wzoru), pręty dołu (BB) na strzemieniu, góry (BT) pod jego górnym
        /// ramieniem; ramiona prętów giętych (11 / 21) w zakładzie z prętami prostymi „piętrowo”: dół — nad, góra — pod prętem prostym.
        /// Pręty przecinające płaszczyznę cięcia → kropki (strzemiona — pionowe ramię w widoku); równoległe → pręt najbliższy linii
        /// cięcia (≤ ½ rozstawu) w widoku: strzemię — kształt pręta wzorcowego (haki u góry), pręty podłużne — linia na swoim poziomie.
        /// </summary>
        private static void PlaceBeamDists(Result res, Input inp, List<BandU> bands, Func<double, double> U, double s0, double s1, double c,
                                           Func<double, double, bool> inConcrete)
        {
            BandU BandAt(double u) => bands.FirstOrDefault(b => u >= b.U0 - 1.0 && u <= b.U1 + 1.0);
            BandU BandOver(double ua, double ub)
            {
                double lo = Math.Min(ua, ub), hi = Math.Max(ua, ub);
                return bands.Where(b => hi > b.U0 + 1.0 && lo < b.U1 - 1.0)
                            .OrderByDescending(b => Math.Min(hi, b.U1) - Math.Max(lo, b.U0)).FirstOrDefault();
            }
            double Sp(Dist d, int n) => n > 1 ? (d.BarsSpan > 0 ? d.BarsSpan / (n - 1) : d.Spacing) : 0;
            double Skew(Dist d, int i, int n) => n > 1 ? d.SkewStart + (d.SkewEnd - d.SkewStart) * i / (n - 1) : d.SkewStart;
            bool IsLink(Dist d) => d.LayerCode == BeamGeometry.CodeLinks;

            // strzemię pasu: rozkład BL w pasie (wymiar A pręta wzorcowego, średnica) ma pierwszeństwo przed wzorem
            foreach (var d in inp.Dists.Where(x => x.Kind == DistKind.Beam && IsLink(x)))
            {
                int n = Math.Max(1, d.Count);
                double q0 = d.AlongX ? d.Y0 : d.X0, a = d.AlongX ? d.X0 : d.Y0;
                var hit = d.AlongX != inp.CutAlongX
                    ? Enumerable.Range(0, n).Select(i => BandAt(U(q0 + i * Sp(d, n)))).FirstOrDefault(b => b != null)
                    : BandOver(U(a), U(a + d.LengthA));
                if (hit == null) continue;
                foreach (var b in bands.Where(x => x.Src == hit.Src))
                {
                    b.LinkDia = d.Diameter > 0 ? d.Diameter : b.LinkDia;
                    if (d.SourceDims != null && d.SourceDims.Length > 0 && d.SourceDims[0] > 0) b.LinkA = d.SourceDims[0];
                }
            }
            foreach (var b in bands)
                if (b.LinkA <= 0) b.LinkA = BeamGeometry.StirrupHeight(b.H, coverBottom: b.Src.CoverBottom);   // brak A — wzór 1b.1
            // średnice prętów dołu / góry pasu (poziomy prętów bocznych E4b)
            foreach (var d in inp.Dists.Where(x => x.Kind == DistKind.Beam && (x.ShapeCode ?? "00") == "00"
                                                   && (x.LayerCode == BeamGeometry.CodeBottom || x.LayerCode == BeamGeometry.CodeTop
                                                       || x.LayerCode == BeamGeometry.CodeBoth)))
            {
                int n = Math.Max(1, d.Count);
                double q0 = d.AlongX ? d.Y0 : d.X0, a = d.AlongX ? d.X0 : d.Y0;
                var hit = d.AlongX != inp.CutAlongX
                    ? Enumerable.Range(0, n).Select(i => BandAt(U(q0 + i * Sp(d, n)))).FirstOrDefault(b => b != null)
                    : BandOver(U(a), U(a + d.LengthA));
                if (hit == null) continue;
                foreach (var b in bands.Where(x => x.Src == hit.Src))
                {
                    if (d.LayerCode == BeamGeometry.CodeTop || d.LayerCode == BeamGeometry.CodeBoth) b.MainDiaT = d.Diameter;
                    if (d.LayerCode != BeamGeometry.CodeTop) b.MainDiaB = d.Diameter;
                }
            }

            // poziomy (v) pręta belki w pasie: dół — na strzemieniu, góra — pod jego górnym ramieniem, boczne (E4b) — równo między
            // prętami dołu i góry (Layers poziomów), w nibie (E4b) — w narożach spinki UB 03 (dołem na otulinie, górą pod wierzchem nibu)
            List<double> Levels(BandU b, Dist d)
            {
                double half = d.Diameter / 2.0;
                switch (d.LayerCode)
                {
                    case BeamGeometry.CodeTop: return new List<double> { b.VLinkTop - b.LinkDia - half };
                    case BeamGeometry.CodeSide:
                    {
                        double vb = b.VLinkBot + b.LinkDia + b.MainDiaB / 2.0, vt = b.VLinkTop - b.LinkDia - b.MainDiaT / 2.0;
                        int n = Math.Max(1, d.Layers);
                        return Enumerable.Range(1, n).Select(i => vb + i * (vt - vb) / (n + 1)).ToList();
                    }
                    case BeamGeometry.CodeNib:
                        // naroża spinki UB 03 w pasie (wewnątrz U): ramię ∓ (Ø spinki / 2 + d / 2) — lica w styku, bez nakładania
                        if (!double.IsNaN(b.UbBot))
                            return new List<double> { IsNibTop(d) ? b.UbTop - b.UbDia / 2.0 - half : b.UbBot + b.UbDia / 2.0 + half };
                        return new List<double> { IsNibTop(d) ? -b.H + b.NibH - inp.CoverNib - BeamNibUbDia - half
                                                              : -b.H + b.Src.CoverBottom + BeamNibUbDia + half };
                    // K3: pręty podłużne dół = góra jedną pozycją (BM „2x4 … B+T”) — kropki na obu poziomach
                    case BeamGeometry.CodeBoth: return new List<double> { b.VLinkBot + b.LinkDia + half, b.VLinkTop - b.LinkDia - half };
                    default: return new List<double> { b.VLinkBot + b.LinkDia + half };
                }
            }
            // K3: BM — poziom górny (nad połową strzemienia) to warstwa górna
            bool IsBothTop(BandU b, Dist d, double v) => d.LayerCode == BeamGeometry.CodeBoth && v > (b.VLinkBot + b.VLinkTop) / 2.0;
            // pas belki z nibem (pręty BN leżą w nibie, poza trzonem)
            BandU BandAtNib(double u) => bands.FirstOrDefault(b => u >= b.U0 - b.Nib0 - 1.0 && u <= b.U1 + b.Nib1 + 1.0);

            foreach (var d in inp.Dists.Where(x => x.Kind == DistKind.Beam))
            {
                // K3: pionowe U-bary belki (BU) — w przekroju dopiero w kroku K6 (kropki dół / góra przesunięte o Ø, w cięciu wzdłuż A–B–A)
                if (d.LayerCode == BeamGeometry.CodeUBar) continue;
                // K4: UB SIDE (BC, poziomy U przy końcu belki) — w przekroju pominięty (bez kropek w złych miejscach; przekrój
                // U-barów cofnięty 2026-10-11 razem z K6 — do decyzji użytkownika)
                if (d.LayerCode == BeamGeometry.CodeSideU) continue;
                bool link = IsLink(d);
                bool nib = d.LayerCode == BeamGeometry.CodeNib;
                bool top = d.LayerCode == BeamGeometry.CodeTop || (nib && IsNibTop(d));
                bool bent = (d.ShapeCode ?? "00") != "00" && !link;
                int n = Math.Max(1, d.Count);
                double sp = Sp(d, n);
                bool perpendicular = d.AlongX != inp.CutAlongX;
                if (perpendicular)
                {
                    double p0 = d.AlongX ? d.X0 : d.Y0, q0 = d.AlongX ? d.Y0 : d.X0;
                    for (int i = 0; i < n; i++)
                    {
                        double sk = Skew(d, i, n);
                        if (c < p0 + sk - 1.0 || c > p0 + sk + d.LengthA + 1.0) continue;
                        var spos = new List<double> { q0 + i * sp };
                        if (Math.Abs(d.PairOffset) > 1e-6) spos.Add(q0 + i * sp + d.PairOffset);
                        foreach (double s in spos)
                        {
                            if (s < s0 - 0.5 || s > s1 + 0.5) continue;
                            double u = U(s);
                            var b = nib ? BandAtNib(u) : BandAt(u);
                            if (b == null) continue;
                            if (link)
                            {
                                // strzemię przecięte wzdłuż belki: pionowe ramię w widoku (oś od dolnego do górnego ramienia)
                                double hl = b.LinkDia / 2.0;
                                var path = new List<(double U, double V)> { (u, b.VLinkBot + hl), (u, b.VLinkTop - hl) };
                                res.Bars.Add(new BarLine { Path = path, Outline = OutlineOf(path, b.LinkDia), Diameter = d.Diameter,
                                                           PosNr = d.PosNr, Top = false, Kind = DistKind.Beam });
                                MarkUsed(res, d);
                            }
                            else
                            {
                                foreach (double v in Levels(b, d))
                                {
                                    if (!inConcrete(u, v)) continue;
                                    res.Dots.Add(new Dot { U = u, V = v, Diameter = d.Diameter, PosNr = d.PosNr, Top = top || IsBothTop(b, d, v),
                                                           Beam = true, Bent = bent, NomSpacing = NominalSpacing(d) });
                                    MarkUsed(res, d);      // tylko gdy kropka powstała (poza betonem — rozkład nieużyty)
                                }
                            }
                        }
                    }
                }
                else
                {
                    // pręty równoległe do cięcia: pręt najbliższy linii cięcia (w granicach ½ rozstawu, min. 100)
                    double q0 = d.AlongX ? d.Y0 : d.X0;
                    int ib = -1; double best = double.MaxValue;
                    for (int i = 0; i < n; i++) { double dd = Math.Abs(q0 + i * sp - c); if (dd < best) { best = dd; ib = i; } }
                    double tol = Math.Max(n > 1 ? sp / 2.0 : d.Spacing / 2.0, 100.0) + 0.5;
                    if (ib < 0 || best > tol) continue;
                    double a = (d.AlongX ? d.X0 : d.Y0) + Skew(d, ib, n), bEnd = a + d.LengthA;
                    if (bEnd < s0 - 1.0 || a > s1 + 1.0) continue;
                    var band = BandOver(U(Math.Max(a, s0)), U(Math.Min(bEnd, s1)));
                    if (band == null) continue;
                    if (link)
                    {
                        // strzemię w widoku (cięcie prostopadłe do belki): kształt pręta wzorcowego, haki u góry; oś zewnętrznego
                        // lica strzemienia na otulinie od lica belki i od spodu
                        double hl = band.LinkDia / 2.0;
                        double uLo = Math.Min(U(a), U(bEnd)) + hl, uHi = Math.Max(U(a), U(bEnd)) - hl;
                        double vBot = band.VLinkBot + hl, vTop = band.VLinkTop - hl;
                        List<(double U, double V)> axis;
                        if (d.ShapeLocal != null && d.ShapeLocal.Count >= 2)
                        {
                            double mx = d.ShapeLocal.Min(p => p.X), my = d.ShapeLocal.Min(p => p.Y);
                            axis = d.ShapeLocal.Select(p => (U: uLo + (p.X - mx), V: vBot + (p.Y - my))).ToList();
                        }
                        else
                            axis = new List<(double U, double V)> { (uLo, vBot), (uHi, vBot), (uHi, vTop), (uLo, vTop), (uLo, vBot) };
                        res.Bars.Add(new BarLine { Path = axis, Outline = OutlineOf(axis, band.LinkDia), Diameter = d.Diameter,
                                                   PosNr = d.PosNr, Top = false, Kind = DistKind.Beam });
                        MarkUsed(res, d);
                        continue;
                    }
                    foreach (double v0 in Levels(band, d))
                    {
                        double v = v0;
                        if (bent) v += top ? -d.Diameter : d.Diameter;      // ramię pręta giętego „piętrowo” nad / pod prętem prostym
                        var pathS = new List<(double s, double v)> { (a, v), (bEnd, v) };
                        foreach (var piece in ClipPath(pathS, s0, s1))
                        {
                            var up = piece.Select(p => (U(p.s), p.v)).ToList();
                            foreach (var part in SplitByConcrete(up, inConcrete))
                                if (part.Count >= 2)
                                {
                                    res.Bars.Add(new BarLine { Path = part, Outline = OutlineOf(part, d.Diameter), Diameter = d.Diameter,
                                                               PosNr = d.PosNr, Top = top || IsBothTop(band, d, v0), Kind = DistKind.Beam });
                                    MarkUsed(res, d);
                                }
                        }
                    }
                }
            }

            // ramiona prętów giętych w zakładzie z prętem prostym (ta sama kropka) — „piętrowo”: dół nad, góra pod prętem prostym
            foreach (var dt in res.Dots.Where(x => x.Beam && x.Bent).ToList())
                if (res.Dots.Any(o => o != dt && o.Beam && !o.Bent && o.Top == dt.Top && Math.Abs(o.U - dt.U) < 1.0 && Math.Abs(o.V - dt.V) < 1.0))
                    dt.V += dt.Top ? -dt.Diameter : dt.Diameter;
        }

        private static void AddNibDims(Result res, double uEdge, int inward, double w, double T, double vNibTop)
        {
            double uStep = uEdge + inward * w;
            // szerokość nibu — nad płytą
            res.Dims.Add(new Dim { U1 = uEdge, V1 = vNibTop, U2 = uStep, V2 = 0, LineU = (uEdge + uStep) / 2, LineV = DimHorizV, Vertical = false });
            // uskok i wysokość nibu — łańcuch na zewnątrz krawędzi
            double lu = uEdge - inward * DimOffset;
            res.Dims.Add(new Dim { U1 = uStep, V1 = 0, U2 = uEdge, V2 = vNibTop, LineU = lu, LineV = vNibTop / 2, Vertical = true });
            res.Dims.Add(new Dim { U1 = uEdge, V1 = vNibTop, U2 = uEdge, V2 = -T, LineU = lu, LineV = (vNibTop - T) / 2, Vertical = true });
        }

        // ── opisy numerów pozycji ───────────────────────────────────────────

        /// <summary>Wymiar poprzeczny kształtu (oś–oś) — mniejszy z wymiarów obwiedni osi pręta.</summary>
        private static double CrossOf(List<(double X, double Y)> local)
        {
            double w = local.Max(p => p.X) - local.Min(p => p.X), h = local.Max(p => p.Y) - local.Min(p => p.Y);
            return Math.Min(w, h);
        }

        private static bool OnHorizontal(BarLine bar, double u, double v, double tol)
        {
            var p = bar.Path;
            for (int i = 0; i + 1 < p.Count; i++)
            {
                if (Math.Abs(p[i].V - p[i + 1].V) > 0.5) continue;
                if (u < Math.Min(p[i].U, p[i + 1].U) - 1 || u > Math.Max(p[i].U, p[i + 1].U) + 1) continue;
                if (Math.Abs(p[i].V - v) < tol) return true;
            }
            return false;
        }

        private static double NibTopOf(Result res, double T)
            => res.NibTop;

        private static double TextWidth(string t) => Math.Max(1, t.Length) * TextHeight * 0.8 + 10;

        private static void PlaceLabels(Result res, double T)
        {
            var occupied = new List<(double u0, double v0, double u1, double v1)>();
            bool Free(double u0, double v0, double u1, double v1)
                => !occupied.Any(o => u0 < o.u1 + 10 && u1 > o.u0 - 10 && v0 < o.v1 + 5 && v1 > o.v0 - 5);

            // strefy zakazane: beton i przestrzeń wymiarów nad płytą
            double vNibTop = res.Segments.Count > 0 ? NibTopOf(res, T) : 0;
            foreach (var sg in res.Segments)
            {
                if (sg.Prof != null)
                {
                    // belka (E5): przedziały profilu
                    foreach (var p in sg.Prof) occupied.Add((p.U0, p.Bot, p.U1, p.Top));
                    continue;
                }
                occupied.Add((sg.U0 + sg.Nib0, -T, sg.U1 - sg.Nib1, 0));
                occupied.Add((sg.U0, -T, sg.U1, vNibTop));
            }
            int fixedCount = occupied.Count;
            double TopAt(double u)
            {
                foreach (var sg in res.Segments)
                    if (u >= sg.U0 - 0.5 && u <= sg.U1 + 0.5)
                    {
                        if (sg.Prof != null) return ProfTop(sg, u);
                        return (u < sg.U0 + sg.Nib0 || u > sg.U1 - sg.Nib1) ? vNibTop : 0;
                    }
                return 0;
            }
            foreach (var dm in res.Dims.Where(x => !x.Vertical))
                occupied.Add((Math.Min(dm.U1, dm.U2) - 20, dm.LineV - 12, Math.Max(dm.U1, dm.U2) + 20, dm.LineV + TextHeight + 30));

            // T IN NIB: numery w pustym miejscu nad nibem — pierwszy nad nibem (między uskokiem a krawędzią),
            // kolejne dalej na zewnątrz płyty, w jednym rzędzie (jak na rysunkach ASD)
            var tinDone = new HashSet<Dot>();
            foreach (var sg in res.Segments)
                foreach (var (nibW, uEdge, outward) in new[] { (sg.Nib0, sg.U0, -1), (sg.Nib1, sg.U1, 1) })
                {
                    if (nibW <= 0) continue;
                    double uStep = uEdge - outward * nibW;
                    var tins = res.Dots.Where(d => d.TinNib && Math.Abs(d.U - uEdge) < nibW + 400)
                                       .OrderBy(d => Math.Abs(d.U - uEdge)).ToList();
                    double vb = vNibTop + LabelGap;
                    double cursor = (uStep + uEdge) / 2;                  // środek pierwszego numeru
                    foreach (var dot in tins)
                    {
                        double w = TextWidth(dot.PosNr);
                        occupied.Add((cursor - w / 2, vb, cursor + w / 2, vb + TextHeight));
                        res.Labels.Add(new Label { Text = dot.PosNr, U = cursor, V = vb, Height = TextHeight });
                        tinDone.Add(dot);
                        cursor += outward * (w + 25);
                    }
                }

            // kropki: numer nad (góra) / pod (dół) kropką, kolejne rzędy przy kolizji
            foreach (var dot in res.Dots.Where(d => !tinDone.Contains(d)).OrderBy(d => d.Top ? 0 : 1).ThenBy(d => d.U))
            {
                double w = TextWidth(dot.PosNr);
                for (int row = 0; row < 8; row++)
                {
                    double vb = dot.Top ? TopAt(dot.U) + LabelGap + row * LabelRowStep : LocalBottom(res, dot.U, T) - LabelGap - TextHeight - row * LabelRowStep;
                    if (!Free(dot.U - w / 2, vb, dot.U + w / 2, vb + TextHeight)) continue;
                    occupied.Add((dot.U - w / 2, vb, dot.U + w / 2, vb + TextHeight));
                    res.Labels.Add(new Label { Text = dot.PosNr, U = dot.U, V = vb, Height = TextHeight });
                    break;
                }
            }

            // pręty w widoku: numer ze strzałką, nad płytą (góra) albo pod płytą (dół)
            foreach (var bar in res.Bars)
            {
                var horiz = new List<(double u0, double u1, double v)>();
                for (int i = 0; i + 1 < bar.Path.Count; i++)
                    if (Math.Abs(bar.Path[i].V - bar.Path[i + 1].V) < 0.5 && Math.Abs(bar.Path[i].U - bar.Path[i + 1].U) > 60)
                        horiz.Add((Math.Min(bar.Path[i].U, bar.Path[i + 1].U), Math.Max(bar.Path[i].U, bar.Path[i + 1].U), bar.Path[i].V));
                if (horiz.Count == 0) continue;
                var seg = bar.Top ? horiz.OrderByDescending(h => h.v).First() : horiz.OrderBy(h => h.v).First();
                double w = TextWidth(bar.PosNr);
                bool done = false;
                // belka (E5 / E4b): pod belką gęsto od opisów kropek — trzecie przejście bez warunku „strzałka nie tnie opisów”
                int passes = bar.Kind == DistKind.Beam ? 3 : 2;
                for (int pass = 0; pass < passes && !done; pass++)
                for (int row = 2; row < 12 && !done; row++)
                {
                    foreach (double f in new[] { 0.3, 0.5, 0.7, 0.15, 0.85, 0.4, 0.6 })
                    {
                        double u = seg.u0 + f * (seg.u1 - seg.u0);
                        // spód betonu pod opisem (belka E5: pod spodem belki); bez belki = −T
                        double vb = bar.Top ? LabelGap + row * LabelRowStep : LocalBottom(res, u, T) - LabelGap - TextHeight - row * LabelRowStep;
                        if (!Free(u - w / 2, vb, u + w / 2, vb + TextHeight)) continue;
                        // grot tylko tam, gdzie pręt jest jednoznaczny (żaden inny pręt w widoku nie leży obok)
                        if (pass == 0 && res.Bars.Any(o => o != bar && OnHorizontal(o, u, seg.v, 25.0))) continue;
                        // pas pod/nad tekstem do pręta nie może ciąć innych opisów
                        double lv0 = bar.Top ? seg.v : vb + TextHeight, lv1 = bar.Top ? vb : seg.v;
                        if (pass < 2 && occupied.Skip(fixedCount)
                                    .Any(o => u > o.u0 - 5 && u < o.u1 + 5 && lv0 < o.v1 && lv1 > o.v0)) continue;
                        occupied.Add((u - w / 2, vb, u + w / 2, vb + TextHeight));
                        res.Labels.Add(new Label
                        {
                            Text = bar.PosNr, U = u, V = vb, Height = TextHeight, HasLeader = true,
                            LeaderU = u, LeaderV = seg.v + (bar.Top ? bar.Diameter / 2.0 : -bar.Diameter / 2.0)
                        });
                        done = true;
                        break;
                    }
                }
            }

            // belki (E5): strzemiona przecięte wzdłuż belki (pionowe ramiona w widoku) — jeden opis na pozycję, ze strzałką do ramienia
            // środkowego, pod spodem belki
            foreach (var g in res.Bars.Where(b => b.Kind == DistKind.Beam && b.Path.Count == 2 && Math.Abs(b.Path[0].U - b.Path[1].U) < 0.5)
                                      .GroupBy(b => b.PosNr))
            {
                var list = g.OrderBy(b => b.Path[0].U).ToList();
                var bar = list[list.Count / 2];
                double u = bar.Path[0].U, vLow = Math.Min(bar.Path[0].V, bar.Path[1].V);
                double w = TextWidth(bar.PosNr);
                double bot = MinBottom(res, u, u, T);
                bool done = false;
                for (int row = 2; row < 12 && !done; row++)
                {
                    double vb = bot - LabelGap - TextHeight - row * LabelRowStep;
                    if (!Free(u - w / 2, vb, u + w / 2, vb + TextHeight)) continue;
                    occupied.Add((u - w / 2, vb, u + w / 2, vb + TextHeight));
                    res.Labels.Add(new Label { Text = bar.PosNr, U = u, V = vb, Height = TextHeight, HasLeader = true,
                                               LeaderU = u, LeaderV = vLow - bar.Diameter / 2.0 });
                    done = true;
                }
            }
        }

        /// <summary>Najniższy spód betonu w zakresie [u0, u1] (belka E5); bez profilu belki — dokładnie −T.</summary>
        private static double MinBottom(Result res, double u0, double u1, double T)
        {
            double r = -T;
            foreach (var sg in res.Segments)
            {
                if (sg.Prof == null || u1 < sg.U0 - 0.5 || u0 > sg.U1 + 0.5) continue;
                foreach (var p in sg.Prof)
                    if (u1 >= p.U0 - 0.5 && u0 <= p.U1 + 0.5 && p.Bot < r) r = p.Bot;
            }
            return r;
        }

        // ── geometria ───────────────────────────────────────────────────────

        private static List<(double U, double V)> Profile(Segment sg, double T, double vNibTop)
        {
            var pts = new List<(double U, double V)>();
            void Add(double u, double v)
            {
                if (pts.Count > 0 && Math.Abs(pts[pts.Count - 1].U - u) < 1e-6 && Math.Abs(pts[pts.Count - 1].V - v) < 1e-6) return;
                pts.Add((u, v));
            }
            double l = sg.U0, r = sg.U1;
            // dół od lewej do prawej
            Add(l, -T); Add(r, -T);
            // prawy koniec w górę
            if (sg.End1 == EndKind.Break) foreach (var p in Zigzag(r, T, up: true)) Add(p.U, p.V);
            if (sg.Nib1 > 0) { Add(r, vNibTop); Add(r - sg.Nib1, vNibTop); Add(r - sg.Nib1, 0); }
            else Add(r, 0);
            // wierzch do lewej
            if (sg.Nib0 > 0) { Add(l + sg.Nib0, 0); Add(l + sg.Nib0, vNibTop); Add(l, vNibTop); }
            else Add(l, 0);
            // lewy koniec w dół
            if (sg.End0 == EndKind.Break) foreach (var p in Zigzag(l, T, up: false)) Add(p.U, p.V);
            return pts;
        }

        /// <summary>Linia urwania (zygzak) na końcu cięcia wewnątrz płyty.</summary>
        private static List<(double U, double V)> Zigzag(double u, double T, bool up)
        {
            var z = new List<(double U, double V)>
            {
                (u, -T * 0.62), (u + 25, -T * 0.55), (u - 25, -T * 0.45), (u, -T * 0.38)
            };
            if (!up) z.Reverse();
            return z;
        }

        private static double NibWidthAt(Input inp, double s)
        {
            foreach (var n in inp.Nibs)
            {
                // krawędź prostopadła do cięcia: cięcie wzdłuż X → krawędź pionowa x = s
                if (n.Vertical != inp.CutAlongX) continue;
                if (Math.Abs(n.OuterCoord - s) > 1.0) continue;
                if (inp.CutCoord < n.Lo - 1.0 || inp.CutCoord > n.Hi + 1.0) continue;
                return n.Width;
            }
            return 0;
        }

        /// <summary>Odcinki płyty na linii cięcia (współrzędna s), przycięte do [s0, s1], bez otworów.</summary>
        private static List<(double a, double b, EndKind k0, EndKind k1)> Intervals(Input inp, double s0, double s1)
        {
            double c = inp.CutCoord;
            var xs = new List<double>();
            var poly = inp.Outline;
            for (int i = 0; i < poly.Count; i++)
            {
                var p = poly[i]; var q = poly[(i + 1) % poly.Count];
                double pc = inp.CutAlongX ? p.Y : p.X, qc = inp.CutAlongX ? q.Y : q.X;
                double ps = inp.CutAlongX ? p.X : p.Y, qs = inp.CutAlongX ? q.X : q.Y;
                if (Math.Abs(pc - qc) < 1e-9) continue;                     // krawędź równoległa do cięcia
                if ((c < Math.Min(pc, qc)) || (c >= Math.Max(pc, qc))) continue;  // półotwarty — wierzchołki raz
                double t = (c - pc) / (qc - pc);
                xs.Add(ps + t * (qs - ps));
            }
            xs.Sort();
            var raw = new List<(double a, double b)>();
            for (int i = 0; i + 1 < xs.Count; i += 2)
                if (xs[i + 1] - xs[i] > 1.0) raw.Add((xs[i], xs[i + 1]));

            // otwory
            var cuts = new List<(double a, double b)>();
            foreach (var h in inp.Holes)
            {
                double hc0 = inp.CutAlongX ? h.MinY : h.MinX, hc1 = inp.CutAlongX ? h.MaxY : h.MaxX;
                if (c <= hc0 || c >= hc1) continue;
                cuts.Add((inp.CutAlongX ? h.MinX : h.MinY, inp.CutAlongX ? h.MaxX : h.MaxY));
            }

            var res = new List<(double a, double b, EndKind k0, EndKind k1)>();
            foreach (var (a0, b0) in raw)
            {
                var pieces = new List<(double a, double b, EndKind k0, EndKind k1)> { (a0, b0, EndKind.Edge, EndKind.Edge) };
                foreach (var (ha, hb) in cuts.OrderBy(x => x.a))
                {
                    var next = new List<(double a, double b, EndKind k0, EndKind k1)>();
                    foreach (var pc in pieces)
                    {
                        if (hb <= pc.a || ha >= pc.b) { next.Add(pc); continue; }
                        if (ha - pc.a > 1.0) next.Add((pc.a, ha, pc.k0, EndKind.Hole));
                        if (pc.b - hb > 1.0) next.Add((hb, pc.b, EndKind.Hole, pc.k1));
                    }
                    pieces = next;
                }
                foreach (var pc in pieces)
                {
                    double a = pc.a, b = pc.b; var k0 = pc.k0; var k1 = pc.k1;
                    if (b <= s0 + 1.0 || a >= s1 - 1.0) continue;
                    if (a < s0 - 1.0) { a = s0; k0 = EndKind.Break; }
                    if (b > s1 + 1.0) { b = s1; k1 = EndKind.Break; }
                    if (b - a > 1.0) res.Add((a, b, k0, k1));
                }
            }
            return res;
        }

        private static List<List<(double s, double v)>> ClipPath(List<(double s, double v)> path, double s0, double s1)
        {
            // przycina łamaną do pasa s0..s1 (odcinki pionowe zostają albo znikają w całości)
            var result = new List<List<(double s, double v)>>();
            var cur = new List<(double s, double v)>();
            for (int i = 0; i + 1 < path.Count; i++)
            {
                var p = path[i]; var q = path[i + 1];
                double lo = Math.Min(p.s, q.s), hi = Math.Max(p.s, q.s);
                if (hi < s0 - 0.5 || lo > s1 + 0.5)
                {
                    if (cur.Count >= 2) result.Add(cur);
                    cur = new List<(double s, double v)>();
                    continue;
                }
                var a = (s: Clamp(p.s, s0, s1), p.v); var b = (s: Clamp(q.s, s0, s1), q.v);
                if (cur.Count == 0 || Math.Abs(cur[cur.Count - 1].s - a.s) > 1e-6 || Math.Abs(cur[cur.Count - 1].v - a.v) > 1e-6)
                {
                    if (cur.Count >= 2) result.Add(cur);
                    cur = new List<(double s, double v)> { a };
                }
                cur.Add(b);
            }
            if (cur.Count >= 2) result.Add(cur);
            return result;
        }

        private static double Clamp(double x, double lo, double hi) => x < lo ? lo : x > hi ? hi : x;

        /// <summary>Dzieli łamaną na części leżące w betonie (zagęszczenie co 10 mm, potem uproszczenie).</summary>
        private static List<List<(double U, double V)>> SplitByConcrete(List<(double U, double V)> path, Func<double, double, bool> inConcrete)
        {
            var dense = new List<(double U, double V)>();
            for (int i = 0; i + 1 < path.Count; i++)
            {
                var p = path[i]; var q = path[i + 1];
                double len = Math.Sqrt((q.U - p.U) * (q.U - p.U) + (q.V - p.V) * (q.V - p.V));
                int n = Math.Max(1, (int)Math.Ceiling(len / 10.0));
                for (int k = (i == 0 ? 0 : 1); k <= n; k++)
                {
                    double t = (double)k / n;
                    dense.Add((p.U + t * (q.U - p.U), p.V + t * (q.V - p.V)));
                }
            }
            var parts = new List<List<(double U, double V)>>();
            var cur = new List<(double U, double V)>();
            foreach (var pt in dense)
            {
                if (inConcrete(pt.U, pt.V)) cur.Add(pt);
                else { if (cur.Count >= 2) parts.Add(Simplify(cur)); cur = new List<(double U, double V)>(); }
            }
            if (cur.Count >= 2) parts.Add(Simplify(cur));
            return parts;
        }

        private static List<(double U, double V)> Simplify(List<(double U, double V)> pts)
        {
            var r = new List<(double U, double V)> { pts[0] };
            for (int i = 1; i + 1 < pts.Count; i++)
            {
                var a = r[r.Count - 1]; var b = pts[i]; var c = pts[i + 1];
                double cross = (b.U - a.U) * (c.V - b.V) - (b.V - a.V) * (c.U - b.U);
                if (Math.Abs(cross) > 1e-6) r.Add(b);
            }
            r.Add(pts[pts.Count - 1]);
            return r;
        }

        // ── siatka w przekroju ──────────────────────────────────────────────

        public const double MeshSideCover = 40.0;
        public const double NibBarOffset  = 150.0;

        /// <summary>Rozstaw nominalny z opisu („H10-05-200 B1” → 200); inaczej rozstaw rozkładu.</summary>
        public static double NominalSpacing(Dist d)
        {
            var m = System.Text.RegularExpressions.Regex.Match(d.Mark ?? "", @"H\d+\s*-\s*\d+\s*-\s*(\d+)");
            if (m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0) return v;
            return d.Spacing > 0 ? d.Spacing : 200;
        }

        private static void AlignMesh(Result res, Input inp, Func<double, double, bool> inConcrete)
        {
            foreach (var sg in res.Segments)
            {
                // kotwy siatki przy końcach: krawędź z nibem → pręt w nibie + 150, krawędź / otwór → otulina, urwanie → brak
                double? nibL = null, nibR = null, anchorL = null, anchorR = null;
                if (sg.End0 != EndKind.Break)
                {
                    if (sg.Nib0 > 0) { nibL = sg.U0 + MeshSideCover; anchorL = nibL + NibBarOffset; }
                    else anchorL = sg.U0 + MeshSideCover;
                }
                if (sg.End1 != EndKind.Break)
                {
                    if (sg.Nib1 > 0) { nibR = sg.U1 - MeshSideCover; anchorR = nibR - NibBarOffset; }
                    else anchorR = sg.U1 - MeshSideCover;
                }

                var inSeg = res.Dots.Where(d => d.Mesh && d.U >= sg.U0 - 1 && d.U <= sg.U1 + 1).ToList();
                if (inSeg.Count == 0) continue;
                double sp = inSeg.Where(d => !d.Top).Select(d => d.NomSpacing).DefaultIfEmpty(inSeg[0].NomSpacing).First();
                if (sp <= 0) sp = 200;

                // wspólne położenia siatki
                var grid = new List<double>();
                if (anchorL.HasValue && anchorR.HasValue)
                {
                    double L = anchorL.Value, R = anchorR.Value;
                    if (R <= L + 1) grid.Add((L + R) / 2);
                    else
                    {
                        int n = Math.Max(1, (int)Math.Round((R - L) / sp));
                        for (int i = 0; i <= n; i++) grid.Add(L + (R - L) * i / n);
                    }
                }
                else if (anchorL.HasValue)
                    for (double p = anchorL.Value; p <= sg.U1 - 10; p += sp) grid.Add(p);
                else if (anchorR.HasValue)
                    for (double p = anchorR.Value; p >= sg.U0 + 10; p -= sp) grid.Insert(0, p);
                else
                    continue;   // oba końce urwane — położenia z rozkładów

                foreach (var level in inSeg.GroupBy(d => Math.Round(d.V, 1)).ToList())
                {
                    var orig = level.OrderBy(d => d.U).ToList();
                    bool top = orig[0].Top;
                    var pts = new List<double>(grid);
                    if (!top) { if (nibL.HasValue) pts.Insert(0, nibL.Value); if (nibR.HasValue) pts.Add(nibR.Value); }
                    var fresh = new List<Dot>();
                    foreach (double u in pts)
                    {
                        if (!inConcrete(u, orig[0].V)) continue;
                        var near = orig.OrderBy(d => Math.Abs(d.U - u)).First();
                        fresh.Add(new Dot { U = u, V = near.V, Diameter = near.Diameter, PosNr = near.PosNr, Top = top,
                                            Mesh = true, NomSpacing = near.NomSpacing });
                    }
                    if (fresh.Count == 0) continue;
                    foreach (var d in orig) res.Dots.Remove(d);
                    res.Dots.AddRange(fresh);
                }
            }
        }

        /// <summary>Kropka na pionowym ramieniu pręta w widoku (np. UB) → odsunięcie od ramienia po swojej stronie.</summary>
        private static void ResolveDotCollisions(Result res)
        {
            foreach (var dot in res.Dots)
            {
                if (dot.Beam) continue;                 // pręty belki (E5) — położenia z geometrii strzemienia, bez przesuwania
                double u0 = dot.U;
                for (int iter = 0; iter < 3; iter++)
                {
                    bool moved = false;
                    foreach (var bar in res.Bars)
                    {
                        var p = bar.Path;
                        for (int i = 0; i + 1 < p.Count; i++)
                        {
                            double du = p[i + 1].U - p[i].U, dv = p[i + 1].V - p[i].V;
                            if (Math.Abs(dv) < 1e-6 || Math.Abs(du) > Math.Abs(dv) * 0.3) continue;   // tylko (prawie) pionowe odcinki
                            double vLo = Math.Min(p[i].V, p[i + 1].V), vHi = Math.Max(p[i].V, p[i + 1].V);
                            if (dot.V < vLo - dot.Diameter / 2.0 || dot.V > vHi + dot.Diameter / 2.0) continue;
                            double segU = (p[i].U + p[i + 1].U) / 2;
                            double need = dot.Diameter / 2.0 + bar.Diameter / 2.0 + 8.0;
                            double gap = Math.Abs(dot.U - segU);
                            if (gap >= need) continue;
                            double side = Math.Abs(dot.U - segU) > 0.5 ? Math.Sign(dot.U - segU) : Math.Sign(p.Average(q => q.U) - segU);
                            if (side == 0) side = 1;
                            dot.U = segU + side * need;   // odsunięcie po tej stronie ramienia, po której już leży
                            moved = true;
                        }
                    }
                    if (!moved) break;
                }
                // siatka: góra i dół w jednej kolumnie — przesuń razem
                if (dot.Mesh && Math.Abs(dot.U - u0) > 0.5)
                    foreach (var o in res.Dots)
                        if (o != dot && o.Mesh && Math.Abs(o.U - u0) < 0.5) o.U = dot.U;
            }
        }

        /// <summary>
        /// Belka (E4b, poprawka recenzji): kropka T IN NIB nachodząca na kropkę pręta belki (np. BN górą w narożu spinki UB 03) —
        /// odsunięcie T IN NIB w poziomie po tej stronie, po której już leży (w głąb odcinka, gdy w jednej osi), do styku lic.
        /// Pręty belki się nie przesuwają (położenia z geometrii strzemienia / spinki).
        /// </summary>
        private static void ResolveTinBeamCollisions(Result res)
        {
            foreach (var t in res.Dots.Where(x => x.TinNib))
                foreach (var o in res.Dots.Where(x => x.Beam))
                {
                    double need = (t.Diameter + o.Diameter) / 2.0;
                    double du = t.U - o.U, dv = t.V - o.V;
                    if (du * du + dv * dv >= need * need - 1e-6) continue;
                    double side = Math.Abs(du) > 0.5 ? Math.Sign(du) : 0;
                    if (side == 0)
                    {
                        var sg = res.Segments.FirstOrDefault(s => t.U >= s.U0 - 0.5 && t.U <= s.U1 + 0.5);
                        side = sg != null && sg.U1 - t.U < t.U - sg.U0 ? -1 : 1;
                    }
                    t.U = o.U + side * Math.Sqrt(Math.Max(0.0, need * need - dv * dv));
                }
        }

        // ── kopia pręta wzorcowego ──────────────────────────────────────────

        /// <summary>
        /// Dopasowuje oś pręta wzorcowego (układ lokalny kształtu) do położenia w przekroju: jedna z 8 orientacji
        /// (obroty o 90° i odbicie), bez skalowania. U (UB, UB 03): ramię przy krawędzi po stronie krawędzi, dolne ramię
        /// na poziomie dolnym celu. Pozostałe: najdłuższy odcinek poziomo na poziomie celu, od początku pręta,
        /// haki / odgięcia do wnętrza płyty (dół — w górę, góra — w dół).
        /// </summary>
        public static List<(double U, double V)> FitShape(List<(double X, double Y)> local, List<(double U, double V)> target, bool isU, bool top, bool alignTop = false)
        {
            List<(double U, double V)> best = null;
            double bestScore = double.MaxValue;
            double tMinU = target.Min(p => p.U), tMaxU = target.Max(p => p.U), tMinV = target.Min(p => p.V);
            for (int k = 0; k < 8; k++)
            {
                var pts = local.Select(p =>
                {
                    double x = p.X, y = p.Y;
                    if ((k & 4) != 0) { double t = x; x = y; y = t; }
                    if ((k & 1) != 0) x = -x;
                    if ((k & 2) != 0) y = -y;
                    return (U: x, V: y);
                }).ToList();
                double minX = pts.Min(p => p.U), maxX = pts.Max(p => p.U), minY = pts.Min(p => p.V), maxY = pts.Max(p => p.V);
                double dx, dy, score = 0;
                if (isU && target.Count >= 4)
                {
                    double uEdge = target[1].U;
                    bool edgeRight = uEdge > target[0].U;
                    dx = edgeRight ? uEdge - maxX : uEdge - minX;
                    dy = alignTop ? target.Max(p => p.V) - maxY : tMinV - minY;   // UB 03: górne ramię pod T IN NIB
                    var moved = pts.Select(p => (U: p.U + dx, V: p.V + dy)).ToList();
                    // ramię pionowe przy krawędzi, ramiona do wnętrza
                    foreach (var t in new[] { target[1], target[2], (U: uEdge, V: (target[1].V + target[2].V) / 2),
                                              (U: (target[0].U + target[1].U) / 2, V: target[0].V),
                                              (U: (target[2].U + target[3].U) / 2, V: target[3].V) })
                        score += DistToPolyline(moved, t.U, t.V);
                    score += Math.Abs((maxX - minX) - (tMaxU - tMinU)) * 0.01;
                    if (score < bestScore) { bestScore = score; best = moved; }
                }
                else
                {
                    int iLong = -1; double len = 0;
                    for (int i = 0; i + 1 < pts.Count; i++)
                    {
                        double l = Math.Abs(pts[i + 1].U - pts[i].U);
                        if (Math.Abs(pts[i + 1].V - pts[i].V) < 1e-6 && l > len) { len = l; iLong = i; }
                    }
                    if (iLong < 0) continue;
                    double segMin = Math.Min(pts[iLong].U, pts[iLong + 1].U), segY = pts[iLong].V;
                    dx = tMinU - segMin; dy = target[0].V - segY;
                    var moved = pts.Select(p => (U: p.U + dx, V: p.V + dy)).ToList();
                    double v = target[0].V;
                    foreach (var p in moved)
                        if (top ? p.V > v + 1 : p.V < v - 1) score += 1000;
                    score += Math.Abs(len - (tMaxU - tMinU));
                    if (score < bestScore) { bestScore = score; best = moved; }
                }
            }
            return best;
        }

        private static double DistToPolyline(List<(double U, double V)> pl, double u, double v)
        {
            double best = double.MaxValue;
            for (int i = 0; i + 1 < pl.Count; i++)
            {
                double ax = pl[i].U, ay = pl[i].V, bx = pl[i + 1].U, by = pl[i + 1].V;
                double dx = bx - ax, dy = by - ay, l2 = dx * dx + dy * dy;
                double t = l2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((u - ax) * dx + (v - ay) * dy) / l2));
                double px = ax + t * dx - u, py = ay + t * dy - v;
                best = Math.Min(best, Math.Sqrt(px * px + py * py));
            }
            return pl.Count == 1 ? Math.Sqrt((pl[0].U - u) * (pl[0].U - u) + (pl[0].V - v) * (pl[0].V - v)) : best;
        }

        /// <summary>Obrys pręta o średnicy d wokół osi (lewa krawędź + odwrócona prawa) — jak pręt w elewacji.</summary>
        public static List<(double U, double V)> OutlineOf(List<(double U, double V)> axis, double d)
        {
            int n = axis.Count;
            if (n < 2) return null;
            double h = d / 2.0;
            var left = new List<(double U, double V)>(); var right = new List<(double U, double V)>();
            for (int i = 0; i < n; i++)
            {
                var a = axis[Math.Max(0, i - 1)]; var b = axis[Math.Min(n - 1, i + 1)];
                if (i == 0) { a = axis[0]; b = axis[1]; } else if (i == n - 1) { a = axis[n - 2]; b = axis[n - 1]; }
                double tx = b.U - a.U, ty = b.V - a.V, tl = Math.Sqrt(tx * tx + ty * ty);
                if (tl < 1e-9) { tx = 1; ty = 0; } else { tx /= tl; ty /= tl; }
                double nx = -ty, ny = tx;
                // narożnik ostry (schemat bez łuków): przesunięcie po dwusiecznej, żeby grubość była stała
                double scale = 1.0;
                if (i > 0 && i < n - 1)
                {
                    double ax = axis[i].U - axis[i - 1].U, ay = axis[i].V - axis[i - 1].V, al = Math.Sqrt(ax * ax + ay * ay);
                    if (al > 1e-9) { double c = (-ay / al) * nx + (ax / al) * ny; if (c > 0.3) scale = 1.0 / c; }
                }
                left.Add((axis[i].U + nx * h * scale, axis[i].V + ny * h * scale));
                right.Add((axis[i].U - nx * h * scale, axis[i].V - ny * h * scale));
            }
            right.Reverse();
            left.AddRange(right);
            return left;
        }

        // ── parsowanie opisu płyty ──────────────────────────────────────────

        /// <summary>Grubość płyty z opisu („225mm THK SLAB”, „SLAB THICKNESS = 225”); null gdy brak.</summary>
        public static double? ParseThickness(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(text, @"(\d{3})\s*mm\s*THK|SLAB\s+THICKNESS\s*=?\s*(\d{3})",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            string g = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            return double.Parse(g, CultureInfo.InvariantCulture);
        }

        /// <summary>SSL z opisu („SSL=21.925”); null gdy brak.</summary>
        public static string ParseSsl(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var m = System.Text.RegularExpressions.Regex.Match(text, @"SSL\s*[=:]?\s*\+?(-?\d+(?:[.,]\d+)?)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value.Replace(',', '.') : null;
        }

        /// <summary>Numer pozycji z opisu rozkładu („H10-05-200 B1” → „05”).</summary>
        public static string PosNrOf(string mark)
        {
            if (string.IsNullOrEmpty(mark)) return "?";
            var m = System.Text.RegularExpressions.Regex.Match(mark, @"H\d+\s*-\s*(\d+)");
            return m.Success ? m.Groups[1].Value : mark.Trim();
        }

        public static DistKind KindOf(string mark, string posNr, int nibUbPosNr)
        {
            string m = (mark ?? "").ToUpperInvariant();
            if (m.Contains("T IN NIB")) return DistKind.TInNib;
            if (System.Text.RegularExpressions.Regex.IsMatch(m, @"\bADD\b")) return DistKind.Add;
            if (System.Text.RegularExpressions.Regex.IsMatch(m, @"\bUB\b"))
                return int.TryParse(posNr, out int p) && p == nibUbPosNr ? DistKind.NibUB : DistKind.UB;
            return DistKind.Main;
        }
    }
}
