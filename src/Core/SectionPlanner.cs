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
        /// 3 (decyzja użytkownika 2026-10-11, zasady Z1–Z6): symbole zakończeń prętów w widoku (<see cref="Input.EndStyle"/>) —
        /// rzeczywiste końce prętów prostych i wolne końce ramion prętów giętych, zakład z odsunięciem w poprzek; płyty i belki.
        /// (2 — cofnięte K6, nie używać ponownie.)
        /// 4 (decyzja użytkownika 2026-10-11 „pręty płyty nie wchodzą w belkę”, „w nibie 2 pręty górą i 1 dołem”): kropki siatki płyty
        /// poziomu, którego rozkład nie wchodzi w trzon belki, nie są dorysowywane w pasie belki (trzon + nib belki) — AlignMesh /
        /// MeshLevelBesideBeams; bez belek wynik bez zmian. Decyzja 2026-10-11 „pręty prostopadłe płyty wchodzą w belkę do 40mm side
        /// cover” zmienia tylko WEJŚCIE (rozkłady siatki prostopadłe do belki krawędziowej sięgają 40 od lica zewnętrznego — kropki /
        /// pręty w widoku w trzonie belki z rzeczywistych rozkładów; poziom z kropkami w trzonie — schemat jak bez belki), logika bez
        /// zmian — wersja nadal 4 (przekrój ze starymi rozkładami wykrywa odcisk DIST). Decyzja 2026-10-11 „jedna pozycja bottom in nib
        /// i top” — nowy rodzaj rozkładu BN „2+1 … T+B IN NIB” (3 kropki); rozkłady starsze (T IN NIB, B IN NIB) bez zmian — wersja nadal 4.
        /// Wersja 5 (2026-10-14): bez odsunięcia zakładów Z2 — pręty jednego kierunku (proste i U-bary) na jednej linii poziomu;
        /// obrys pręta w narożnikach (zagięcia, kreski S1) — dokładne ścięcie po dwusiecznej normalnych (krawędzie równoległe do osi).
        /// </summary>
        public const int Version = 5;

        /// <summary>
        /// Styl symbolu zakończenia pręta w widoku (Z1–Z6, decyzja użytkownika 2026-10-11): None — bez symboli (dawny rysunek, złote testy
        /// wzorca bit w bit), Tick (S1 „UK tick”, decyzja użytkownika 2026-10-11) — kreska 45° do wnętrza przekroju, cała w betonie, długości
        /// <see cref="DefaultEndSymbolLength"/>. Styl NIE jest zapisywany w przekroju — w programie jeden styl (<see cref="DefaultEndStyle"/>).
        /// </summary>
        public enum EndStyle { None = 0, Tick = 1 }

        /// <summary>Styl zakończeń prętów w RC_SECTION / RC_SECTION_UPDATE / RC_SECTION_CHECK (PrepareSection) — jeden w programie.</summary>
        public const EndStyle DefaultEndStyle = EndStyle.Tick;

        /// <summary>Krótka nazwa stylu zakończeń do komunikatów: „S1” / „brak”.</summary>
        public static string EndStyleShort(EndStyle e) => e == EndStyle.Tick ? "S1" : "brak";

        /// <summary>Z1: długość kreski zakończenia [mm] — na stałe 30 (decyzja użytkownika 2026-10-11; w 1:20 = 1,5 mm na papierze).</summary>
        public const double DefaultEndSymbolLength = 30.0;
        /// <summary>Z2: odsunięcie pręta zakładu w poprzek (do wnętrza przekroju) [mm].</summary>
        public const double LapOffset = 25.0;

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
            /// <summary>E4b: pręty boczne (BS) — liczba prętów na bok (poziomy między prętami dołu i góry); 1 = jeden poziom.
            /// Strzemiona BL (decyzja użytkownika 2026-10-11): 2 = 4-cięte — dwa identyczne strzemiona przy obu licach (rozkład na całą
            /// szerokość, pręt wzorcowy węższy); 1 = jedno strzemię (jak dotąd).</summary>
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
            /// <summary>
            /// Z1–Z6 (wersja 3): styl symboli zakończeń prętów w widoku. None = bez symboli i bez odsunięcia zakładów (wynik bit w bit
            /// jak wersja 1 — złote testy wzorca); PrepareSection ustawia <see cref="DefaultEndStyle"/>.
            /// </summary>
            public EndStyle EndStyle = EndStyle.None;
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
            /// <summary>
            /// Z1 / Z3 (wersja 3): początek / koniec ścieżki to rzeczywisty koniec pręta (pręt prosty) albo wolny koniec ramienia
            /// (pręt gięty) — nie urwanie, nie koniec cięcia, nie zagięcie (Z4). Symbol zakończenia tylko przy <see cref="Input.EndStyle"/> ≠ None.
            /// </summary>
            public bool End0, End1;
            /// <summary>Wersja 3: droga pręta sprzed symboli zakończeń (do położenia opisu); null = bez symboli (Path).</summary>
            internal List<(double U, double V)> LabelPath;
            /// <summary>Obrys pręta z grubością (zamknięty), jak pręt w elewacji.</summary>
            public List<(double U, double V)> Outline;
            /// <summary>Bez własnego opisu (drugie strzemię 4-cięte — jeden opis pozycji; decyzja użytkownika 2026-10-11).</summary>
            internal bool NoLabel;
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

        /// <summary>Decyzja użytkownika 2026-10-11: pręty w nibie jedną pozycją („2+1 … T+B IN NIB”) — w rzucie 2 pręty górą, w przekroju
        /// 3 kropki: obie górą pod górnym ramieniem spinki UB 03, trzecia dołem w dolnym narożu (pod prętem bliższym krawędzi płyty).</summary>
        private static bool IsNibPair(Dist d) => (d.Mark ?? "").TrimEnd().EndsWith(BeamGeometry.SuffixNibBoth, StringComparison.OrdinalIgnoreCase);

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
                        // T IN NIB w trzonie belki (rysunki sprzed decyzji 2026-10-11 — drugi pręt „150 dalej”, za licem belki): poziom
                        // jak w płycie, bez przesunięcia do nibu belki; od 2026-10-11 oba pręty T IN NIB przy belce leżą w nibie
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
            // UB 03 w pasie belki albo w jej nibie (decyzja 2026-10-14): dolne ramię licuje się ze strzemieniem — lico zewnętrzne dolnego
            // ramienia na spodzie strzemienia (otulina dolna belki), wysokość U bez zmian; poza belką — 0 (wynik bez zmian)
            double UbBeamShift(double u, Dist x, double d)
            {
                foreach (var b in bands)
                    if (u >= b.U0 - b.Nib0 - 0.5 && u <= b.U1 + b.Nib1 + 0.5)
                        return b.VLinkBot + d / 2 - vUbBot(x, d);
                return 0;
            }

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
                    double sh = b.VLinkBot + d.Diameter / 2.0 - vUbBot(d, d.Diameter);   // licuje się ze strzemieniem (2026-10-14)
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
                            if (bands.Count > 0 && d.Kind == DistKind.TInNib) v += BeamNibShift(U(s), true);
                            else if (bands.Count > 0 && d.Kind == DistKind.NibUB) v += UbBeamShift(U(s), d, dia);
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
                            if (bands.Count > 0) { double sh = UbBeamShift(U(sEdge), d, dia); vb += sh; vt += sh; }   // belka (E5; lico strzemienia 2026-10-14)
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
                                // Z3: wolne końce ramion pręta giętego (kształt otwarty)
                                bool open = Dist2(axis[0], axis[axis.Count - 1]) > 1.0;
                                res.Bars.Add(new BarLine { Path = axis, Outline = OutlineOf(axis, dia),
                                                           Diameter = d.Diameter, PosNr = d.PosNr, Top = top, Kind = d.Kind,
                                                           End0 = open, End1 = open });
                                MarkUsed(res, d);
                                continue;
                            }
                        }
                        // przytnij do zakresu cięcia i betonu (otwory, końce cięcia)
                        var endA = (U: U(path[0].s), V: path[0].v);
                        var endB = (U: U(path[path.Count - 1].s), V: path[path.Count - 1].v);
                        foreach (var piece in ClipPath(path, s0, s1))
                        {
                            var up = piece.Select(p => (U(p.s), p.v)).ToList();
                            foreach (var part in SplitByConcrete(up, InConcrete))
                                if (part.Count >= 2)
                                {
                                    res.Bars.Add(RealEnds(new BarLine { Path = part, Outline = OutlineOf(part, dia),
                                                                        Diameter = d.Diameter, PosNr = d.PosNr, Top = top }, endA, endB));
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
            AlignMesh(res, inp, InConcrete, bands);

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

            // 6a. Z1–Z6 (wersja 3): zakład — odsunięcie w poprzek, symbole zakończeń prętów w widoku; styl None — dawny rysunek
            List<(double u0, double v0, double u1, double v1)> symbolBoxes = null;
            if (inp.EndStyle != EndStyle.None) symbolBoxes = ApplyEndSymbols(res, DefaultEndSymbolLength, InConcrete);

            // 7. Opisy (wersja 3: symbole zakończeń jako przeszkody)
            PlaceLabels(res, T, symbolBoxes);
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
                        // pręty w nibie 2T+1B (decyzja 2026-10-11): oba poziomy — [0] górą, [1] dołem (dołem tylko pręt bliższy krawędzi)
                        // decyzja 2026-10-14 (UB 03 licuje się ze strzemieniem): pręty górą leżą NA górnym ramieniu spinki, dołem — wewnątrz
                        // na dolnym ramieniu
                        if (IsNibPair(d))
                            return !double.IsNaN(b.UbBot)
                                ? new List<double> { b.UbTop + b.UbDia / 2.0 + half, b.UbBot + b.UbDia / 2.0 + half }
                                : new List<double> { -b.H + b.NibH - inp.CoverNib - BeamNibUbDia - half, -b.H + b.Src.CoverBottom + BeamNibUbDia + half };
                        if (!double.IsNaN(b.UbBot))
                            return new List<double> { IsNibTop(d) ? b.UbTop + b.UbDia / 2.0 + half : b.UbBot + b.UbDia / 2.0 + half };
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
                bool nibPair = nib && IsNibPair(d);
                bool bent = (d.ShapeCode ?? "00") != "00" && !link;
                int n = Math.Max(1, d.Count);
                double sp = Sp(d, n);
                bool perpendicular = d.AlongX != inp.CutAlongX;
                if (perpendicular)
                {
                    double p0 = d.AlongX ? d.X0 : d.Y0, q0 = d.AlongX ? d.Y0 : d.X0;
                    // pręty w nibie 2T+1B: pręt dołem pod prętem górą najdalej od trzonu belki (w narożu spinki UB 03, przy krawędzi płyty)
                    int outerIdx = -1;
                    if (nibPair)
                    {
                        double far = double.MinValue;
                        for (int i = 0; i < n; i++)
                        {
                            double u = U(q0 + i * sp);
                            var bb = BandAtNib(u);
                            if (bb == null) continue;
                            double o = Math.Max(bb.U0 - u, u - bb.U1);
                            if (o > far) { far = o; outerIdx = i; }
                        }
                    }
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
                                var lv = Levels(b, d);
                                for (int li = 0; li < lv.Count; li++)
                                {
                                    double v = lv[li];
                                    if (nibPair && li > 0 && i != outerIdx) continue;     // 2T+1B: dołem tylko pod prętem przy krawędzi
                                    if (!inConcrete(u, v)) continue;
                                    res.Dots.Add(new Dot { U = u, V = v, Diameter = d.Diameter, PosNr = d.PosNr,
                                                           Top = top || IsBothTop(b, d, v) || (nibPair && li == 0),
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
                        // strzemiona 4-cięte (Layers 2, decyzja użytkownika 2026-10-11): drugie identyczne strzemię przy drugim licu (zachodzące),
                        // bez własnego opisu; tylko gdy pręt wzorcowy węższy od rozkładu
                        if (d.Layers == 2 && axis.Count >= 2)
                        {
                            double ax0 = axis.Min(p => p.U), ax1 = axis.Max(p => p.U);
                            double sh = uHi - ax1;
                            if (sh > 1.0 && ax0 + sh > uLo + 1.0)
                            {
                                var axis2 = axis.Select(p => (U: p.U + sh, p.V)).ToList();
                                res.Bars.Add(new BarLine { Path = axis2, Outline = OutlineOf(axis2, band.LinkDia), Diameter = d.Diameter,
                                                           PosNr = d.PosNr, Top = false, Kind = DistKind.Beam, NoLabel = true });
                            }
                        }
                        MarkUsed(res, d);
                        continue;
                    }
                    var lvs = Levels(band, d);
                    for (int li = 0; li < lvs.Count; li++)
                    {
                        double v0 = lvs[li], v = v0;
                        if (bent) v += top ? -d.Diameter : d.Diameter;      // ramię pręta giętego „piętrowo” nad / pod prętem prostym
                        var pathS = new List<(double s, double v)> { (a, v), (bEnd, v) };
                        foreach (var piece in ClipPath(pathS, s0, s1))
                        {
                            var up = piece.Select(p => (U(p.s), p.v)).ToList();
                            foreach (var part in SplitByConcrete(up, inConcrete))
                                if (part.Count >= 2)
                                {
                                    var bl = new BarLine { Path = part, Outline = OutlineOf(part, d.Diameter), Diameter = d.Diameter,
                                                           PosNr = d.PosNr, Top = top || IsBothTop(band, d, v0) || (nibPair && li == 0),
                                                           Kind = DistKind.Beam };
                                    // Z1: rzeczywiste końce pręta prostego belki; ramię pręta giętego (11 / 21) rysowane jako linia — bez
                                    // rozróżnienia końca wolnego i zagięcia, więc bez symbolu
                                    res.Bars.Add(bent ? bl : RealEnds(bl, (U(a), v), (U(bEnd), v)));
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

        // ── zakończenia prętów w widoku (wersja 3, zasady Z1–Z6) ──────────────

        private static double Dist2((double U, double V) a, (double U, double V) b)
            => Math.Sqrt((a.U - b.U) * (a.U - b.U) + (a.V - b.V) * (a.V - b.V));

        private static bool Near((double U, double V) a, (double U, double V) b) => Math.Abs(a.U - b.U) < 0.5 && Math.Abs(a.V - b.V) < 0.5;

        /// <summary>
        /// Z1 / Z4: końce kawałka pręta (po przycięciu do zakresu cięcia i betonu) będące rzeczywistymi końcami pręta (<paramref name="endA"/>,
        /// <paramref name="endB"/> — końce ścieżki pręta w u, v). Koniec na urwaniu, na końcu cięcia albo przycięty do betonu — bez flagi.
        /// </summary>
        private static BarLine RealEnds(BarLine b, (double U, double V) endA, (double U, double V) endB)
        {
            if (b.Path.Count >= 2)
            {
                var a = b.Path[0]; var z = b.Path[b.Path.Count - 1];
                b.End0 = Near(a, endA) || Near(a, endB);
                b.End1 = Near(z, endA) || Near(z, endB);
            }
            return b;
        }

        /// <summary>Pręt zamknięty (strzemię w widoku) — bez symboli i bez zakładu.</summary>
        private static bool IsClosed(BarLine b) => b.Path.Count >= 3 && Dist2(b.Path[0], b.Path[b.Path.Count - 1]) <= 1.0;

        /// <summary>Odcinek ścieżki przy końcu (początek / koniec) jest poziomy (pręt w widoku leży wzdłuż cięcia).</summary>
        private static bool EndHorizontal(BarLine b, bool atStart, out (double U, double V) end)
        {
            int n = b.Path.Count;
            var p = atStart ? b.Path[0] : b.Path[n - 1];
            var q = atStart ? b.Path[1] : b.Path[n - 2];
            end = p;
            return Math.Abs(p.V - q.V) < 0.5 && Math.Abs(p.U - q.U) > 1.0;
        }

        /// <summary>
        /// Z2: zakład — dwa pręty na tym samym poziomie, z których co najmniej jeden kończy się (rzeczywisty koniec, odcinek poziomy)
        /// wewnątrz poziomego odcinka drugiego. Jeden z nich odsunięty w poprzek o <see cref="LapOffset"/> do wnętrza przekroju (górny —
        /// w dół, dolny — w górę): prosty obok giętego (UB / L) — prosty; dwa proste — leżący dalej w prawo (większe u początku);
        /// dwa gięte — bez odsunięcia. Każdy pręt odsuwany najwyżej raz; odsunięcie wychodzące z betonu — pominięte.
        /// </summary>
        private static void OffsetLaps(Result res, Func<double, double, bool> inConcrete)
        {
            var shifted = new HashSet<BarLine>();
            var bars = res.Bars.Where(b => b.Path.Count >= 2 && !IsClosed(b)).ToList();
            bool HorizSegAt(BarLine y, double u, double v)
            {
                var p = y.Path;
                for (int i = 0; i + 1 < p.Count; i++)
                {
                    if (Math.Abs(p[i].V - p[i + 1].V) > 0.5 || Math.Abs(p[i].V - v) > 0.5) continue;
                    double lo = Math.Min(p[i].U, p[i + 1].U), hi = Math.Max(p[i].U, p[i + 1].U);
                    if (u > lo + 1.0 && u < hi - 1.0) return true;
                }
                return false;
            }
            foreach (var x in bars)
                foreach (bool atStart in new[] { true, false })
                {
                    if (!(atStart ? x.End0 : x.End1)) continue;
                    if (!EndHorizontal(x, atStart, out var e)) continue;
                    foreach (var y in bars)
                    {
                        if (ReferenceEquals(x, y) || !HorizSegAt(y, e.U, e.V)) continue;
                        bool sx = x.Path.Count == 2, sy = y.Path.Count == 2;
                        BarLine mv;
                        if (sx && !sy) mv = x;
                        else if (sy && !sx) mv = y;
                        else if (sx && sy) mv = y.Path.Min(q => q.U) > x.Path.Min(q => q.U) ? y : x;
                        else continue;
                        if (shifted.Contains(x) || shifted.Contains(y)) continue;
                        double dv = (mv.Top ? -1 : 1) * LapOffset;
                        var np = mv.Path.Select(q => (q.U, q.V + dv)).ToList();
                        if (!np.All(q => inConcrete(q.Item1, q.Item2))) continue;
                        mv.Path = np;
                        mv.Outline = OutlineOf(np, mv.Diameter);
                        shifted.Add(mv);
                    }
                }
        }

        /// <summary>
        /// Z1–Z6 (wersja 3): zakłady (<see cref="OffsetLaps"/>), potem symbol na każdym oznaczonym końcu (<see cref="BarLine.End0"/> /
        /// <see cref="BarLine.End1"/>) — do wnętrza przekroju (pręt prosty: górny w dół, dolny w górę; pręt gięty: prostopadle do ramienia
        /// w stronę środka obwiedni pręta), CAŁY w betonie i bez wychodzenia poza koniec pręta:
        /// S1 „UK tick” — ramię skrócone o s = L / √2 (L = <paramref name="L"/>, <see cref="DefaultEndSymbolLength"/> = 30 mm), kreska 45° od skróconego
        /// końca do punktu koniec + s · n (zasięg pręta bez zmian).
        /// Ramię krótsze niż s + 1 (kreska) albo symbol poza betonem — bez symbolu. Zwraca prostokąty symboli (przeszkody dla opisów;
        /// strona przy pręcie bez marginesu, żeby nie blokować strzałki opisu samego pręta).
        /// </summary>
        private static List<(double u0, double v0, double u1, double v1)> ApplyEndSymbols(Result res, double L, Func<double, double, bool> inConcrete)
        {
            // 2026-10-14 (uwaga Bartka: „przekrój miał być w 1 warstwie” — pręty jednego kierunku na jednej linii, jak ASD): bez
            // odsunięcia zakładów Z2 (od 5895ac7 pręt prosty przy U-barze / L odsuwany o 25 w głąb → B1 nad B2, kropki na prętach)
            // OffsetLaps(res, inConcrete);
            var boxes = new List<(double u0, double v0, double u1, double v1)>();
            double s = L / Math.Sqrt(2.0);
            foreach (var b in res.Bars)
            {
                if (!(b.End0 || b.End1) || b.Path.Count < 2 || IsClosed(b)) continue;
                var p = new List<(double U, double V)>(b.Path);
                bool straight = p.Count == 2 && Math.Abs(p[0].V - p[1].V) < 0.5;
                double cu = (p.Min(q => q.U) + p.Max(q => q.U)) / 2, cv = (p.Min(q => q.V) + p.Max(q => q.V)) / 2;
                bool changed = false;
                foreach (bool atStart in new[] { false, true })
                {
                    if (!(atStart ? b.End0 : b.End1)) continue;
                    int n = p.Count;
                    var P = atStart ? p[0] : p[n - 1];
                    var prev = atStart ? p[1] : p[n - 2];
                    double len = Dist2(P, prev);
                    if (len < 1e-6) continue;
                    double tu = (P.U - prev.U) / len, tv = (P.V - prev.V) / len;      // kierunek wzdłuż ramienia, na zewnątrz pręta
                    // normalna do wnętrza: pręt prosty — wg warstwy; gięty — w stronę środka obwiedni pręta
                    var cands = new List<(double U, double V)>();
                    if (straight) cands.Add((0, b.Top ? -1 : 1));
                    else
                    {
                        var n1 = (U: -tv, V: tu);
                        double dot = n1.U * (cu - P.U) + n1.V * (cv - P.V);
                        if (Math.Abs(dot) < 1e-6) dot = b.Top ? -n1.V : n1.V;
                        var nIn = dot >= 0 ? n1 : (U: -n1.U, V: -n1.V);
                        cands.Add(nIn); cands.Add((-nIn.U, -nIn.V));
                    }
                    if (len < s + 1.0) continue;
                    foreach (var nn in cands)
                    {
                        var tip = (U: P.U + s * nn.U, V: P.V + s * nn.V);
                        var root = (U: P.U - s * tu, V: P.V - s * tv);
                        var mid = (U: (root.U + tip.U) / 2, V: (root.V + tip.V) / 2);
                        if (!inConcrete(tip.U, tip.V) || !inConcrete(mid.U, mid.V)) continue;
                        if (atStart) { p[0] = root; p.Insert(0, tip); }
                        else { p[n - 1] = root; p.Add(tip); }
                        // prostokąt symbolu: margines 5 mm poza stroną przy pręcie
                        double u0 = Math.Min(root.U, tip.U) - 5, u1 = Math.Max(root.U, tip.U) + 5;
                        double v0 = Math.Min(root.V, tip.V), v1 = Math.Max(root.V, tip.V);
                        if (nn.V > 0.5) v1 += 5;
                        else if (nn.V < -0.5) v0 -= 5;
                        else { v0 -= 5; v1 += 5; }
                        boxes.Add((u0, v0, u1, v1));
                        changed = true;
                        break;
                    }
                }
                if (changed) { b.LabelPath = b.Path; b.Path = p; b.Outline = OutlineOf(p, b.Diameter); }
            }
            return boxes;
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

        private static void PlaceLabels(Result res, double T, List<(double u0, double v0, double u1, double v1)> obstacles = null)
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
            // wersja 3: symbole zakończeń prętów — przeszkody (strzałka opisu nie przechodzi przez symbol); bez symboli — bez zmian
            if (obstacles != null) occupied.AddRange(obstacles);

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

            // decyzja 2026-10-14: pręt górą w dolnej części belki (pręty w nibie belki na spince UB 03, pod spodem płyty) — opis w rzędzie
            // uskoku nibu (nad prętem, w obrysie belki), nie w rzędzie nad wierzchem belki
            bool LowBeamTop(Dot d) => d.Beam && d.Top && d.V < -T - 0.5 && TopAt(d.U) > d.V + T / 2;
            bool FreeOfLabels(double u0, double v0, double u1, double v1)
                => !occupied.Skip(fixedCount).Any(o => u0 < o.u1 + 10 && u1 > o.u0 - 10 && v0 < o.v1 + 5 && v1 > o.v0 - 5);

            // kropki: numer nad (góra) / pod (dół) kropką, kolejne rzędy przy kolizji
            foreach (var dot in res.Dots.Where(d => !tinDone.Contains(d) && !LowBeamTop(d)).OrderBy(d => d.Top ? 0 : 1).ThenBy(d => d.U))
            {
                double w = TextWidth(dot.PosNr);
                bool placed = false;
                for (int row = 0; row < 8 && !placed; row++)
                {
                    double vb = dot.Top ? TopAt(dot.U) + LabelGap + row * LabelRowStep : LocalBottom(res, dot.U, T) - LabelGap - TextHeight - row * LabelRowStep;
                    // decyzja 2026-10-14: opis dołem przy licu belki (koliduje tylko z betonem) — przesunięty w bok w swoim rzędzie,
                    // zamiast spadać pod spód belki
                    var shifts = !dot.Top && row == 0 && !Free(dot.U - w / 2, vb, dot.U + w / 2, vb + TextHeight)
                                 && FreeOfLabels(dot.U - w / 2, vb, dot.U + w / 2, vb + TextHeight)
                        ? new[] { 0.0, -15, 15, -30, 30, -45, 45, -60, 60 } : new[] { 0.0 };
                    foreach (double du in shifts)
                    {
                        double u = dot.U + du;
                        if (!Free(u - w / 2, vb, u + w / 2, vb + TextHeight)) continue;
                        occupied.Add((u - w / 2, vb, u + w / 2, vb + TextHeight));
                        res.Labels.Add(new Label { Text = dot.PosNr, U = u, V = vb, Height = TextHeight });
                        placed = true;
                        break;
                    }
                }
            }
            foreach (var dot in res.Dots.Where(d => !tinDone.Contains(d) && LowBeamTop(d)).OrderBy(d => d.U))
            {
                double w = TextWidth(dot.PosNr);
                // rząd uskoku: najbliższy wierzch profilu nad prętem (wierzch nibu belki); bez nibu — tuż nad prętem
                double ledge = double.NaN;
                foreach (var sg in res.Segments.Where(x => x.Prof != null && dot.U >= x.U0 - 0.5 && dot.U <= x.U1 + 0.5))
                    foreach (var pp in sg.Prof)
                        if (pp.Top > dot.V && pp.Top < -T / 2 && (double.IsNaN(ledge) || pp.Top < ledge)) ledge = pp.Top;
                double vb = double.IsNaN(ledge) ? dot.V + dot.Diameter / 2.0 + LabelGap : ledge + LabelGap;
                bool placed = false;
                foreach (double du in new[] { 0.0, -30, 30, -60, 60, -90, 90 })
                {
                    double u = dot.U + du;
                    if (!FreeOfLabels(u - w / 2, vb, u + w / 2, vb + TextHeight)) continue;
                    // tekst nie przecina pionowych ramion prętów w widoku (strzemię, spinka)
                    if (res.Bars.Any(b => CrossesVertical(b, u - w / 2, u + w / 2, vb, vb + TextHeight))) continue;
                    occupied.Add((u - w / 2, vb, u + w / 2, vb + TextHeight));
                    res.Labels.Add(new Label { Text = dot.PosNr, U = u, V = vb, Height = TextHeight });
                    placed = true;
                    break;
                }
                if (placed) continue;
                for (int row = 0; row < 8; row++)
                {
                    double vr = TopAt(dot.U) + LabelGap + row * LabelRowStep;
                    if (!Free(dot.U - w / 2, vr, dot.U + w / 2, vr + TextHeight)) continue;
                    occupied.Add((dot.U - w / 2, vr, dot.U + w / 2, vr + TextHeight));
                    res.Labels.Add(new Label { Text = dot.PosNr, U = dot.U, V = vr, Height = TextHeight });
                    break;
                }
            }

            // pręty w widoku: numer ze strzałką, nad płytą (góra) albo pod płytą (dół)
            foreach (var bar in res.Bars)
            {
                if (bar.NoLabel) continue;
                var horiz = new List<(double u0, double u1, double v)>();
                // wersja 3: położenia opisu liczone z drogi pręta sprzed symboli zakończeń (te same kandydaty co bez symboli)
                var lp = bar.LabelPath ?? bar.Path;
                for (int i = 0; i + 1 < lp.Count; i++)
                    if (Math.Abs(lp[i].V - lp[i + 1].V) < 0.5 && Math.Abs(lp[i].U - lp[i + 1].U) > 60)
                        horiz.Add((Math.Min(lp[i].U, lp[i + 1].U), Math.Max(lp[i].U, lp[i + 1].U), lp[i].V));
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
                        // wersja 3: grot na rysowanej części pręta (nie na skróconym końcu pod kreską zakończenia)
                        if (bar.LabelPath != null && !OnHorizontal(bar, u, seg.v, 1.0)) continue;
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

        /// <summary>Pionowy odcinek drogi pręta w widoku przecina prostokąt [u0, u1] × [v0, v1].</summary>
        private static bool CrossesVertical(BarLine b, double u0, double u1, double v0, double v1)
        {
            var p = b.Path;
            for (int i = 0; i + 1 < p.Count; i++)
            {
                double du = p[i + 1].U - p[i].U, dv = p[i + 1].V - p[i].V;
                if (Math.Abs(dv) < 1e-6 || Math.Abs(du) > Math.Abs(dv) * 0.3) continue;
                double su = (p[i].U + p[i + 1].U) / 2;
                double lo = Math.Min(p[i].V, p[i + 1].V), hi = Math.Max(p[i].V, p[i + 1].V);
                if (su > u0 - 5 && su < u1 + 5 && hi > v0 && lo < v1) return true;
            }
            return false;
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

        private static void AlignMesh(Result res, Input inp, Func<double, double, bool> inConcrete, List<BandU> bands = null)
        {
            foreach (var sg in res.Segments)
            {
                // belka (wersja 4; E2 — pręty siatki RÓWNOLEGŁE do belki nie wchodzą w belkę): pasy belek tego odcinka; bez belek — null
                // (dawny kod bit w bit)
                var segBands = bands == null ? null : bands.Where(b => b.Seg == sg).ToList();
                if (segBands != null && segBands.Count == 0) segBands = null;
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

                // decyzja 2026-10-14: przy belce siatka górna i dolna w jednej linii — kotwa po stronie belki wspólna dla poziomów
                // odcinka (z poziomu górnego, liczonego najpierw); bez belek kolejność i wynik bez zmian
                var levels = inSeg.GroupBy(d => Math.Round(d.V, 1)).ToList();
                if (segBands != null) levels = levels.OrderBy(g => g.First().Top ? 0 : 1).ToList();
                var shared = new Dictionary<(double, double), (double? L, double? R)>();
                foreach (var level in levels)
                {
                    var orig = level.OrderBy(d => d.U).ToList();
                    bool top = orig[0].Top;
                    if (segBands != null)
                    {
                        // siatka równoległa do belki nie wchodzi w belkę (E2): poziom bez żadnej kropki z rozkładu w trzonie belki
                        // nie dostaje kropek w pasie belki (trzon + nib belki); poza pasem kotwa przy belce = rzeczywisty pierwszy pręt
                        // rozkładu (nie siatka od kotwy nibu płyty)
                        var cut = segBands.Where(b => !orig.Any(d => d.U > b.U0 + 0.5 && d.U < b.U1 - 0.5)).ToList();
                        if (cut.Count > 0)
                        {
                            var freshB = MeshLevelBesideBeams(res, sg, orig, cut, anchorL, anchorR, nibL, nibR, sp, top, inConcrete, shared);
                            if (freshB.Count == 0) continue;
                            foreach (var d in orig) res.Dots.Remove(d);
                            res.Dots.AddRange(freshB);
                            continue;
                        }
                    }
                    var pts = new List<double>(grid);
                    if (!top) { if (nibL.HasValue) pts.Insert(0, nibL.Value); if (nibR.HasValue) pts.Add(nibR.Value); }
                    var fresh = new List<Dot>();
                    var gaps = new List<double>();
                    foreach (double u in pts)
                    {
                        if (!inConcrete(u, orig[0].V)) continue;
                        if (!CoveredByBars(u, orig, sp, u == nibL || u == nibR)) { gaps.Add(u); continue; }
                        var near = orig.OrderBy(d => Math.Abs(d.U - u)).First();
                        fresh.Add(new Dot { U = u, V = near.V, Diameter = near.Diameter, PosNr = near.PosNr, Top = top,
                                            Mesh = true, NomSpacing = near.NomSpacing });
                    }
                    WarnGaps(res, gaps, top, sp);
                    if (fresh.Count == 0) continue;
                    foreach (var d in orig) res.Dots.Remove(d);
                    res.Dots.AddRange(fresh);
                }
            }
        }

        /// <summary>
        /// Położenie schematu siatki ma pokrycie w rzeczywistych prętach poziomu (decyzja 2026-10-14): kropka schematu tylko tam, gdzie
        /// w pobliżu (≤ ¾ rozstawu) leży pręt z rozkładu — po usunięciu rozkładu luka w zbrojeniu zostaje w przekroju pusta (bez kropek
        /// podpisanych sąsiednim rozkładem). Pręt w nibie (krawędź z nibem) — tolerancja większa o odsunięcie siatki od nibu.
        /// </summary>
        private static bool CoveredByBars(double u, List<Dot> orig, double sp, bool nibBar)
        {
            double tol = 0.75 * sp + (nibBar ? NibBarOffset : 0) + 1.0;
            return orig.Any(d => Math.Abs(d.U - u) <= tol);
        }

        /// <summary>Komunikat o luce w zbrojeniu poziomu (kolejne pominięte położenia schematu scalone w jeden odcinek).</summary>
        private static void WarnGaps(Result res, List<double> gaps, bool top, double sp)
        {
            if (gaps.Count == 0) return;
            gaps.Sort();
            double a = gaps[0], b = gaps[0]; int n = 1;
            void Flush() => res.Warnings.Add(string.Format(CultureInfo.InvariantCulture,
                "Luka w zbrojeniu ({0}): brak prętów z rozkładów na odcinku ok. {1:0} mm — {2} kropek schematu pominięto (usunięty rozkład?).",
                top ? "góra" : "dół", b - a + sp, n));
            for (int i = 1; i < gaps.Count; i++)
            {
                if (gaps[i] - b <= 1.5 * sp + 1.0) { b = gaps[i]; n++; }
                else { Flush(); a = b = gaps[i]; n = 1; }
            }
            Flush();
        }

        /// <summary>
        /// Siatka w odcinku z belką (wersja 4; jak poprawka b5d7e7f cofnięta z K6 — przywrócona decyzją użytkownika 2026-10-11): poziom
        /// kropek siatki, którego rozkład nie wchodzi w trzon belki (E2 — pręty równoległe do belki; prostopadłe do belki krawędziowej
        /// wchodzą w nią do 40 od lica zewnętrznego — decyzja 2026-10-11 — i ich poziom ma kropki w trzonie). Pas belki (trzon + nib belki do
        /// krawędzi) — bez kropek; poza pasem — schemat jak <see cref="AlignMesh"/> (rozstaw nominalny, góra i dół od kotew), ale kotwa po
        /// stronie belki = rzeczywisty skrajny pręt rozkładu przy belce; kotwy przy krawędzi / otworze / nibie płyty — jak dotąd. Zakres
        /// bez kotew (urwanie i belka) — położenia z rozkładu.
        /// </summary>
        private static List<Dot> MeshLevelBesideBeams(Result res, Segment sg, List<Dot> orig, List<BandU> cut, double? anchorL, double? anchorR,
                                                      double? nibL, double? nibR, double sp, bool top, Func<double, double, bool> inConcrete,
                                                      Dictionary<(double, double), (double? L, double? R)> shared = null)
        {
            // przedziały zakazane (trzon + nib belki), scalone
            var forb = new List<(double Lo, double Hi)>();
            foreach (var b in cut.OrderBy(b => b.U0 - b.Nib0))
            {
                double lo = b.U0 - b.Nib0, hi = b.U1 + b.Nib1;
                if (forb.Count > 0 && lo <= forb[forb.Count - 1].Hi + 1e-6)
                    forb[forb.Count - 1] = (forb[forb.Count - 1].Lo, Math.Max(forb[forb.Count - 1].Hi, hi));
                else forb.Add((lo, hi));
            }
            var ranges = new List<(double Lo, double Hi, bool SegL, bool SegR)>();
            double start = sg.U0; bool startSeg = true;
            foreach (var f in forb)
            {
                ranges.Add((start, f.Lo, startSeg, false));
                start = f.Hi; startSeg = false;
            }
            ranges.Add((start, sg.U1, startSeg, true));

            double v = orig[0].V;
            var fresh = new List<Dot>();
            var gaps = new List<double>();
            foreach (var r in ranges)
            {
                if (r.Hi - r.Lo <= 1.0) continue;
                var inR = orig.Where(d => d.U >= r.Lo - 0.5 && d.U <= r.Hi + 0.5).OrderBy(d => d.U).ToList();
                if (inR.Count == 0) continue;
                double? L = r.SegL ? anchorL : inR[0].U;
                double? R = r.SegR ? anchorR : inR[inR.Count - 1].U;
                // kotwa przy belce wspólna dla siatki górnej i dolnej (2026-10-14) — pierwszy liczony poziom (górny) wyznacza położenia
                var key = (Math.Round(r.Lo, 1), Math.Round(r.Hi, 1));
                if (shared != null)
                {
                    if (shared.TryGetValue(key, out var sh))
                    {
                        if (!r.SegL && sh.L.HasValue) L = sh.L;
                        if (!r.SegR && sh.R.HasValue) R = sh.R;
                    }
                    else shared[key] = (L, R);
                }
                var pts = new List<double>();
                if (L.HasValue && R.HasValue)
                {
                    if (R.Value <= L.Value + 1) pts.Add((L.Value + R.Value) / 2);
                    else
                    {
                        int n = Math.Max(1, (int)Math.Round((R.Value - L.Value) / sp));
                        for (int i = 0; i <= n; i++) pts.Add(L.Value + (R.Value - L.Value) * i / n);
                    }
                }
                else if (L.HasValue)
                    for (double p = L.Value; p <= r.Hi - 10; p += sp) pts.Add(p);
                else if (R.HasValue)
                    for (double p = R.Value; p >= r.Lo + 10; p -= sp) pts.Insert(0, p);
                else
                    pts.AddRange(inR.Select(d => d.U));      // urwanie po obu stronach — położenia z rozkładu
                if (!top) { if (r.SegL && nibL.HasValue) pts.Insert(0, nibL.Value); if (r.SegR && nibR.HasValue) pts.Add(nibR.Value); }
                foreach (double u in pts)
                {
                    if (u < r.Lo - 0.5 || u > r.Hi + 0.5 || !inConcrete(u, v)) continue;
                    if (!CoveredByBars(u, inR, sp, u == nibL || u == nibR)) { gaps.Add(u); continue; }
                    var near = inR.OrderBy(d => Math.Abs(d.U - u)).First();
                    fresh.Add(new Dot { U = u, V = near.V, Diameter = near.Diameter, PosNr = near.PosNr, Top = top,
                                        Mesh = true, NomSpacing = near.NomSpacing });
                }
            }
            WarnGaps(res, gaps, top, sp);
            return fresh;
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
            // normalna jednostkowa odcinka k (axis[k] → axis[k+1]); odcinek zerowy — normalna sąsiada
            (double X, double Y)? SegNormal(int k)
            {
                if (k < 0 || k >= n - 1) return null;
                double tx = axis[k + 1].U - axis[k].U, ty = axis[k + 1].V - axis[k].V, tl = Math.Sqrt(tx * tx + ty * ty);
                if (tl < 1e-9) return null;
                return (-ty / tl, tx / tl);
            }
            for (int i = 0; i < n; i++)
            {
                var n1 = SegNormal(i - 1); var n2 = SegNormal(i);
                for (int k = i - 2; n1 == null && k >= 0; k--) n1 = SegNormal(k);
                for (int k = i + 1; n2 == null && k < n - 1; k++) n2 = SegNormal(k);
                var a = n1 ?? n2 ?? (0.0, 1.0); var c = n2 ?? n1 ?? (0.0, 1.0);
                // 2026-10-14 (uwaga Bartka: krzywe linie U-barów i kresek na końcach): narożnik — dokładne ścięcie (miter) po dwusiecznej
                // normalnych obu odcinków, krawędzie obrysu równoległe do osi; wcześniej normalna z cięciwy a[i−1]–a[i+1], przy odcinkach
                // o różnej długości (ramię 640 + kreska 35) obrys skośny
                double bx = a.Item1 + c.Item1, by = a.Item2 + c.Item2, bl = Math.Sqrt(bx * bx + by * by);
                double nx, ny, scale = 1.0;
                if (bl < 1e-9) { nx = a.Item1; ny = a.Item2; }
                else
                {
                    nx = bx / bl; ny = by / bl;
                    double cos = nx * a.Item1 + ny * a.Item2;
                    if (cos > 0.3) scale = 1.0 / cos;
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
