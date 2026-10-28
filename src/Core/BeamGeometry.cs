#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BricsCadRc.Core
{
    /// <summary>
    /// Parametry belki / pogrubienia (rekord RC_BEAM — sekcja 4i ARCHITECTURE.md). Czysta klasa (bez Teigha).
    /// Wartości domyślne = praktyka biura (C12 / Holbrook ręczny — docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md, K2 / K3) i rozstrzygnięcia
    /// planu belek — „domyślne, do potwierdzenia” przez użytkownika (sekcja 5 planu praktyki); każda do zmiany w oknie RC_BEAM.
    /// Rekord v2 (FormatVersion 2) — pola [24]–[35]; rekord v1 (wersje testowe) migrowany w pamięci (<see cref="MigrateV1"/>).
    /// </summary>
    public sealed class BeamParams
    {
        /// <summary>Wersja formatu rekordu RC_BEAM zapisywana przez tę wersję.</summary>
        public const short CurrentFormat = 2;

        /// <summary>Wysokość belki od wierzchu płyty [mm] (z opisu „450THK” w GA; 0 = nieznana).</summary>
        public double Height;
        /// <summary>Szerokość trzonu belki [mm] (z geometrii strefy; typowo 600).</summary>
        public double Width = 600;
        /// <summary>Nib belki: szerokość × wysokość [mm] (decyzja 1a: 135 × 150).</summary>
        public double NibWidth = 135, NibHeight = 150;
        /// <summary>Strzemiona: średnica i rozstaw (K2: H12 co 200 — Holbrook / C12, CP „LINKS H12@200”).</summary>
        public int StirrupDia = 12;
        public double StirrupSpacing = 200;
        /// <summary>Pręty podłużne: dół i góra (4 H16 + 4 H16).</summary>
        public int BottomDia = 16, BottomCount = 4, TopDia = 16, TopCount = 4;
        /// <summary>Pionowe U-bary na końcach i w narożnikach (K3, kształt 21) — średnica (Holbrook: jak pręty podłużne).</summary>
        public int UBarDia = 16;
        /// <summary>Kod kształtu strzemienia (51).</summary>
        public string StirrupShape = "51";
        /// <summary>Rekord [15] — zakład prętów głównych [mm]; od v2 tylko zapisywany (= Ceil25(krotność · Ø dołem), dla starej
        /// wtyczki). Zakład liczony z <see cref="LapMultiple"/> (<see cref="LapFor"/>).</summary>
        public double Lap = 650;
        /// <summary>Maksymalna długość pręta [mm] (1b.5: jak płyta, 6000).</summary>
        public double MaxLength = 6000;
        /// <summary>Rekord [17] — od v2 nieużywane w generowaniu (wysokość strzemienia z otuliny górnej [24]); zostaje dla starej
        /// wtyczki i przekroju (RC_SECTION — krok K6).</summary>
        public int StirrupTopLayers = 2;
        /// <summary>Pręty boczne (BS): średnica (0 = brak) i rozstaw pionowy. K2: H12 (C12); tryb <see cref="SideMode"/>.</summary>
        public int SideDia = 12;
        public double SideSpacing = 200;
        /// <summary>Pręty wzdłuż nibu belki (BN): górą (0 = brak — K2: dubluje pręty w nibie) i [21] — pręty w nibie jedną pozycją
        /// (decyzje użytkownika 2026-10-11: 3 identyczne pręty H10, 2 górą + 1 dołem, „2+1 H10-nn T+B IN NIB”; dawniej K5 — 1 H10 „B IN NIB”).</summary>
        public int NibTopDia = 0, NibBottomDia = 10;
        /// <summary>Otulina strzemion belki: boczna / czołowa i dolna [mm] (decyzja użytkownika 2026-10-10: 45 / 50).</summary>
        public double CoverSide = BeamGeometry.DefaultCoverSide, CoverBottom = BeamGeometry.DefaultCoverBottom;

        // ── v2 (K2, docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md) — domyślne do potwierdzenia ──
        /// <summary>[24] Otulina górna strzemienia [mm]: A1 = 35 (A = H − 35 − dolna, Holbrook; domyślne) / A2 = 50 (C12).</summary>
        public double CoverTop = BeamGeometry.CoverTopA1;
        /// <summary>[25] Tryb prętów bocznych: 1 = B1 (brak), 2 = B2 (zawsze), 3 = B3 (od progu wysokości; domyślne).</summary>
        public int SideMode = 3;
        /// <summary>[26] Próg H trybu B3 [mm]: H ≤ próg — bez prętów bocznych (450; od 525 — H12).</summary>
        public double SideThreshold = 450;
        /// <summary>[27] Krotność zakładu (zakład = Ceil25(krotność · Ø): H10 400, H12 500, H16 650 — nota 12 biura).</summary>
        public double LapMultiple = 40;
        /// <summary>[28] Rozstaw nominalny prętów w poprzek belki w opisie [mm] („2x4 H16-nn-150 B+T”, „4 H16-nn-150 UB”).</summary>
        public double AcrossNominal = 150;
        /// <summary>[29] Dół = góra (Ø i liczba) → jedna pozycja „B+T” (kod BM); false — osobno BB / BT.</summary>
        public bool BothOneMark = true;
        /// <summary>[30] Obniżenie U-barów zagnieżdżonych [mm]: noga X narożnika L i belka dochodząca złącza T (B − obniżenie);
        /// 0 = auto: 2·Ø U-bara w górę do 5 (H16 → 35, H12 → 25) — decyzja użytkownika 2026-10-13.</summary>
        public double UBarDrop = 0;
        /// <summary>[31] Narożnik L / złącze T: pręty proste nogi w kwadrat narożnika do lica zewnętrznego (Holbrook); false — przed
        /// kwadratem (C12, domyślne).</summary>
        public bool CornerStraightIntoSquare;
        /// <summary>[32] Koniec pręta prostego przy U-barze: odsunięcie od lica [mm]; 0 = auto (otulina + Ø U-bara = 61; C12 ≈ 90).</summary>
        public double StraightEndOffset;
        /// <summary>[33] Strzemiona względem UB 03: false — niezależnie (domyślne), true — położenia UB 03 + 100.</summary>
        public bool StirrupsAtUb03;
        /// <summary>[34] Strzemiona w kwadracie narożnika L: true — kontynuacja dłuższej nogi (domyślne), false — brak.</summary>
        public bool CornerSquareLinks = false;   // decyzja użytkownika 2026-10-13: zawsze brak (pole nieużywane w generowaniu)
        /// <summary>[35] Ramię U-bara końca: A = Ceil25(zakład + naddatek) [mm] (90: C12 740, Holbrook 770 — pytanie 14).</summary>
        public double UBarArmExtra = 0;   // decyzja użytkownika 2026-10-12: ramię = zakład standardowy (H16: 650); wcześniej + 90
        /// <summary>[36] K9 (decyzja użytkownika 2026-10-11): pręty belki w detalu 1:25 (osobny widok w modelu; true — domyślne) albo
        /// na rzucie dolnym 1:50 (false — jak przed K9). Brak pola w rekordzie = detal.</summary>
        public bool DetailView = true;
        /// <summary>[37] Cięcie strzemion (decyzja użytkownika 2026-10-11): 0 = Auto (EN 1992-1-1 §9.2.2(8): rozstaw poprzeczny ramion
        /// ≤ 0,75·d ≤ 600 — <see cref="BeamGeometry.PlanLinkLegs"/>; domyślne, także brak pola w rekordzie), 2 = 2-cięte (jedno strzemię 51),
        /// 4 = 4-cięte (dwa identyczne zachodzące strzemiona 51 jedną pozycją, opis „2x29 H12-nn-200 LINKS”).</summary>
        public int StirrupLegs = LegsAuto;
        /// <summary>[38] Nib przy belce (decyzja użytkownika 2026-10-11 „opcja dodania lub nie dodawania nibu, bo czasem może nie być”):
        /// false — wg geometrii (domyślne, także brak pola: linia uskoku przed licem zewnętrznym belki → belka z nibem, strefa przy krawędzi
        /// płyty → bez nibu; wynik jak dotąd), true — belka BEZ nibu mimo linii uskoku: lico zewnętrzne belki = krawędź płyty
        /// (<see cref="BeamGeometry.ZoneWithoutNib"/>), bez UB 03 / T IN NIB / prętów w nibie przy tej belce.</summary>
        public bool NoNib;
        /// <summary>[39] Belka gruntowa GB poza płytą (decyzja Bartka 2026-10-23, etap 1 — PLOT 304–307): obniżenie góry belki względem SSL
        /// (SSL − TOC, np. 75). 0 = brak (brak pola). Część strefy POZA obrysami płyt: strzemiona 63, A z wysokości H − TopDrop; części w płycie
        /// (narożne strefy 525THK): strzemiona 51, A z H. Pręty B+T prosto na poziomie GB; U-bary i pręty boczne z wysokości H − TopDrop.</summary>
        public double TopDrop;

        public const int LegsAuto = 0, Legs2 = 2, Legs4 = 4;

        /// <summary>Okno RC_BEAM (decyzja użytkownika 2026-10-11): średnice prętów głównych dołem / górą i pionowych U-barów — lista.</summary>
        public static readonly int[] MainDiameters = { 12, 16, 20, 22, 25, 32 };
        /// <summary>Okno RC_BEAM: średnice strzemion — lista.</summary>
        public static readonly int[] LinkDiameters = { 10, 12 };

        /// <summary>
        /// Pozycje listy rozwijanej średnic: lista + wartość bieżąca spoza listy jako „H{n} (nietypowa)” (stary rekord — wartość pokazana,
        /// nie zmieniana po cichu). Wynik posortowany rosnąco; current ≤ 0 — bez dopisku.
        /// </summary>
        public static List<(int Dia, string Text)> DiameterItems(IList<int> list, int current)
        {
            var res = list.Select(d => (d, "H" + d.ToString(CultureInfo.InvariantCulture))).ToList();
            if (current > 0 && !list.Contains(current))
                res.Add((current, "H" + current.ToString(CultureInfo.InvariantCulture) + " (nietypowa)"));
            return res.OrderBy(x => x.Item1).ToList();
        }

        /// <summary>Normalizacja pola [37] (nieznana wartość → Auto).</summary>
        public static int NormalizeLegs(int v) => v == Legs2 || v == Legs4 ? v : LegsAuto;

        /// <summary>Tekst trybu cięcia strzemion: „Auto” / „2-cięte” / „4-cięte”.</summary>
        public static string LegsText(int v) => v == Legs2 ? "2-cięte" : v == Legs4 ? "4-cięte" : "Auto";

        /// <summary>Odczytano rekord v1 (wersje testowe) — parametry zbrojenia zastąpione nowymi domyślnymi w pamięci (niezapisywane).</summary>
        public bool MigratedFromV1;
        /// <summary>Komunikat migracji v1 (lista zmian; null gdy brak).</summary>
        public string MigrationNote;

        public BeamParams Clone() => (BeamParams)MemberwiseClone();

        /// <summary>Zakład pręta o średnicy d: Ceil25(krotność · d) (H10 400, H12 500, H16 650).</summary>
        public double LapFor(int d) => d > 0 && LapMultiple > 0 ? BeamGeometry.Ceil25(LapMultiple * d) : Lap;

        /// <summary>Dół = góra (Ø i liczba) i opcja „B+T jedną pozycją” → kod BM.</summary>
        public bool UsesBothMark => BothOneMark && BottomDia == TopDia && BottomCount == TopCount;

        /// <summary>Pręty boczne przy wysokości H (tryb [25] / próg [26]).</summary>
        public bool SideBarsActive(double h)
            => SideDia > 0 && (SideMode == 2 || (SideMode == 3 && BeamGeometry.SideLevelsForHeight(h) > 0));

        /// <summary>Koniec pręta prostego przy U-barze od lica: [32] albo otulina + Ø U-bara.</summary>
        public double StraightEndOffsetAuto => StraightEndOffset > 0 ? StraightEndOffset : CoverSide + UBarDia;

        /// <summary>
        /// Migracja rekordu v1 (wersje testowe — poprawka 9 recenzji: prosta migracja): geometria i otuliny bez zmian (H, szerokość,
        /// nib, otulina boczna / dolna), parametry zbrojenia = nowe domyślne. Komunikat z listą zmienionych wartości (tylko w pamięci —
        /// zapis przy OK w RC_BEAM albo przy generowaniu).
        /// </summary>
        public static BeamParams MigrateV1(BeamParams v1)
        {
            var n = new BeamParams
            {
                Height = v1.Height, Width = v1.Width, NibWidth = v1.NibWidth, NibHeight = v1.NibHeight,
                CoverSide = v1.CoverSide, CoverBottom = v1.CoverBottom, MigratedFromV1 = true
            };
            var ch = new List<string>();
            string I(int x) => x.ToString(CultureInfo.InvariantCulture);
            string F(double x) => x.ToString("0.#", CultureInfo.InvariantCulture);
            if (v1.StirrupDia != n.StirrupDia) ch.Add($"strzemiona H{I(v1.StirrupDia)} → H{I(n.StirrupDia)}");
            if (Math.Abs(v1.StirrupSpacing - n.StirrupSpacing) > 0.5) ch.Add($"rozstaw strzemion {F(v1.StirrupSpacing)} → {F(n.StirrupSpacing)}");
            if (v1.BottomDia != n.BottomDia || v1.BottomCount != n.BottomCount)
                ch.Add($"dołem {I(v1.BottomCount)} H{I(v1.BottomDia)} → {I(n.BottomCount)} H{I(n.BottomDia)}");
            if (v1.TopDia != n.TopDia || v1.TopCount != n.TopCount)
                ch.Add($"górą {I(v1.TopCount)} H{I(v1.TopDia)} → {I(n.TopCount)} H{I(n.TopDia)}");
            if (v1.UBarDia != n.UBarDia) ch.Add($"U-bary H{I(v1.UBarDia)} → H{I(n.UBarDia)} (pionowe)");
            if (Math.Abs(v1.Lap - n.LapFor(n.BottomDia)) > 0.5)
                ch.Add($"zakład {F(v1.Lap)} → {F(n.LapMultiple)}·Ø (H{I(n.BottomDia)}: {F(n.LapFor(n.BottomDia))})");
            if (Math.Abs(v1.MaxLength - n.MaxLength) > 0.5) ch.Add($"maks. długość {F(v1.MaxLength)} → {F(n.MaxLength)}");
            ch.Add(v1.SideDia > 0
                ? $"pręty boczne H{I(v1.SideDia)} → tryb B3 (H ≤ {F(n.SideThreshold)} brak, wyżej H{I(n.SideDia)})"
                : $"pręty boczne: brak → tryb B3 (H ≤ {F(n.SideThreshold)} brak, wyżej H{I(n.SideDia)})");
            if (v1.NibTopDia != n.NibTopDia) ch.Add($"nib górą H{I(v1.NibTopDia)} → {(n.NibTopDia > 0 ? "H" + I(n.NibTopDia) : "brak")}");
            if (v1.NibBottomDia != n.NibBottomDia) ch.Add($"nib dołem H{I(v1.NibBottomDia)} → pręty w nibie 2T+1B H{I(n.NibBottomDia)}");
            ch.Add($"strzemię A = H − {F(n.CoverTop)} − {F(n.CoverBottom)} (otulina górna A1)");
            n.MigrationNote = "rekord RC_BEAM v1 (wersja testowa) — parametry zbrojenia zastąpione nowymi domyślnymi (do potwierdzenia): "
                              + string.Join("; ", ch) + ". Zapis przy OK w RC_BEAM albo przy generowaniu.";
            return n;
        }
    }

    /// <summary>
    /// Belki / pogrubienia (downstand) — czysta geometria (bez Teigha), testy `tests/BeamGeometryTests.cs`.
    /// Strefa = zamknięta polilinia (warstwa RC-BEAM, kopia SD-Thickening-Zone z GA): pas szerokości ~600 wzdłuż krawędzi
    /// płyty (U, L+L, prosty). Analiza: pary równoległych, przeciwnie skierowanych krawędzi osiowych w odległości
    /// MinWidth–MaxWidth → prostokąty; prostokąty o wspólnym licu → odcinki (nogi); odcinek z przerwą / zmienną
    /// szerokością (skos 45°, PLOT 319) = „detal ręczny”. Narożnik L = kwadrat między końcami dwóch prostopadłych odcinków,
    /// T = koniec odcinka na licu bocznym innego. Pozostałe końce: Edge (lico końca ≤ nib od obrysu płyty) albo Internal.
    /// Liczby strzemion, plan prętów podłużnych, wymiary strzemienia 51 — też tutaj (testowalne).
    /// </summary>
    public static class BeamGeometry
    {
        /// <summary>Kody warstw prętów belki (NIE zaczynają się od B1/B2/T1/T2 — EraseOldDistributions płyty ich nie widzi).
        /// K3 (praktyka biura): BM — pręty podłużne dół = góra jedną pozycją („B+T”), BU — pionowe U-bary (kształt 21); BB / BT — tylko
        /// gdy dół ≠ góra; BL — strzemiona; E4b: BS — pręty boczne, BN — pręty wzdłuż nibu belki. Wszystkie w serii dolnej, rzut dolny
        /// (BT — warstwa CAD góry RC-SLAB-TOP-01; stare BT na rzucie górnym usuwane przy ponownym generowaniu).</summary>
        public const string CodeBottom = "BB", CodeTop = "BT", CodeLinks = "BL", CodeSide = "BS", CodeNib = "BN";
        public const string CodeBoth = "BM", CodeUBar = "BU";
        /// <summary>K4: poziomy U-bar zamykający pręty boczne na końcu / w narożniku („UB SIDE”, kształt 21) — kod BC (nie BS: pręty
        /// boczne proste i U-bary to różne kształty i opisy; nie BU: pionowe U-bary prętów podłużnych).</summary>
        public const string CodeSideU = "BC", SuffixSideU = "UB SIDE";
        /// <summary>Sufiksy opisów (K3): „2x4 H16-nn-150 B+T” (BM), „4 H16-nn-150 B” / „4 H16-nn-150 T” (BB / BT, dół ≠ góra),
        /// „29 H12-nn-200 LINKS”, „4 H16-nn-150 UB” (BU — kod BU, nie UB płyty: skany UB płyty porównują kod B1 / B2).</summary>
        public const string SuffixBoth = "B+T", SuffixBottom = "B", SuffixTop = "T", SuffixLinks = "LINKS", SuffixUBar = "UB";
        /// <summary>Opisy prętów bocznych i w nibie: K4 — „2x1 H12-nn SIDE” (BS); E4b — „1 H10-11 BEAM NIB T” (BN górą, domyślnie brak);
        /// K5 — „1 H10-nn B IN NIB” (BN dołem; rysunki sprzed decyzji 2026-10-11); od decyzji użytkownika 2026-10-11 — „2+1 H10-nn T+B IN NIB”
        /// (BN: jedna pozycja w nibie, 3 identyczne pręty — 2 górą pod górnym ramieniem UB 03 + 1 dołem w dolnym narożu UB 03; liczba „2+1”
        /// — BarLabelRules.CountText).</summary>
        public const string SuffixSide = "SIDE", SuffixNibTop = "BEAM NIB T", SuffixNibBottom = "B IN NIB", SuffixNibBoth = "T+B IN NIB";

        /// <summary>Kod rozkładu belki (także stare BB / BT / BS / BN z wersji testowych) — pręty belki, nie siatki płyty.</summary>
        public static bool IsBeamCode(string code)
            => code == CodeBottom || code == CodeTop || code == CodeLinks || code == CodeSide || code == CodeNib
            || code == CodeBoth || code == CodeUBar || code == CodeSideU;

        /// <summary>Otuliny belki (decyzja użytkownika 2026-10-10): boczna / czołowa strzemion 45 (40 + odchyłka), dolna 50, górna 35
        /// (pod siatką T, bez zmian). Domyślne parametrów BeamParams.CoverSide / CoverBottom.</summary>
        // decyzja użytkownika 2026-10-12: otuliny belki górna 35, dolna 40, boczna 40 — każda + 5 mm zapasu (jak UB płyty) → 40 / 45 / 45
        public const double DefaultCoverSide = 45.0, DefaultCoverBottom = 45.0, DefaultCoverTop = 40.0;
        /// <summary>K2: otulina górna strzemienia — A1 35 (Holbrook, A = H − 85; domyślne) / A2 50 (C12, A = H − 100).</summary>
        public const double CoverTopA1 = 40.0, CoverTopA2 = 50.0;   // A1: 35 + 5 zapasu (decyzja 2026-10-12; wcześniej 35)
        /// <summary>Otulina spinki UB 03 od krawędzi płyty (element płyty — bez zmian, 40): położenie prętów BN w poprzek (naroża spinki).</summary>
        public const double NibUbEdgeCover = 40.0;

        public const double MinWidth = 150.0, MaxWidth = 1500.0;
        /// <summary>Krawędź osiowa z tolerancją 4 mm (jak obrys płyty — OutlineAxisRules).</summary>
        public const double AxisTol = 4.0;
        /// <summary>Styk prostokątów / narożników / wspólne lico [mm].</summary>
        public const double JoinTol = 5.0;
        /// <summary>Koniec przy krawędzi: lico końca najwyżej tyle od obrysu płyty (nib ≤ 255 + 5).</summary>
        public const double EdgeEndMaxGap = 260.0;
        /// <summary>Największa przerwa między kawałkami jednego odcinka (skos) [mm].</summary>
        public const double MaxLegGap = 3000.0;
        /// <summary>Rozstaw rzeczywisty prętów w poprzek większy niż ten próg przy opisie nominalnym (150) — ostrzeżenie (K3).</summary>
        public const double AcrossSpacingWarn = 175.0;

        public enum EndKind { Edge, Internal, Corner, Tee }

        /// <summary>
        /// Zamknięcie końca odcinka (K3 — praktyka biura): None — pręty proste do lica końca z otuliną (złącze z odcinkiem „detal ręczny”),
        /// LBar — narożnik L: każda noga zamknięta własnymi pionowymi U-barami z tyłem na licu zewnętrznym narożnika, Tee — belka
        /// dochodząca złącza T (jak noga narożnika: U-bary z tyłem na dalszym licu belki ciągłej; belka ciągła bez przerwy),
        /// UBar — koniec przy krawędzi albo wewnętrzny: pionowe U-bary kształt 21. Domyślne — do potwierdzenia przez Bartka.
        /// </summary>
        public enum Closure { None, LBar, Tee, UBar }


        /// <summary>Złącze odcinków: narożnik L (A — odcinek wzdłuż X, B — wzdłuż Y) albo T (A — belka ciągła, B — dochodząca).</summary>
        public sealed class Joint
        {
            public bool IsTee;
            public Segment A, B;
            /// <summary>Który koniec odcinka leży w złączu (Lo / Hi); ALo — tylko narożnik L.</summary>
            public bool ALo, BLo;
        }

        /// <summary>Odcinek belki (noga strefy) — oś wzdłuż X (AlongX) albo Y.</summary>
        public sealed class Segment
        {
            public bool AlongX;
            /// <summary>Lica boczne (y dla odcinka wzdłuż X, x dla pionowego).</summary>
            public double AcrossLo, AcrossHi;
            /// <summary>Własny zakres odcinka wzdłuż osi (bez kwadratów narożników) — strefa strzemion.</summary>
            public double OwnLo, OwnHi;
            /// <summary>Lica końców (narożnik L — lico zewnętrzne narożnika, T — dalsze lico belki poprzecznej).</summary>
            public double FaceLo, FaceHi;
            public EndKind LoKind = EndKind.Internal, HiKind = EndKind.Internal;
            /// <summary>E4: zamknięcie końców (AssignClosures po analizie).</summary>
            public Closure LoClose = Closure.None, HiClose = Closure.None;
            /// <summary>E4: zakres strzemion wzdłuż osi — własny zakres, w narożniku L dłuższego odcinka do lica zewnętrznego
            /// (kontynuacja jednego kierunku w kwadracie narożnika, bez zdublowania).</summary>
            public double LinkLo, LinkHi;
            /// <summary>Lico boczne przy krawędzi płyty: −1 = AcrossLo, +1 = AcrossHi, 0 = żadne.</summary>
            public int OuterSide;
            /// <summary>Odcinek nieosiowy / o zmiennej szerokości — bez prętów, komunikat „detal ręczny”.</summary>
            public bool Manual;
            public string ManualReason;

            public double Width => AcrossHi - AcrossLo;
            public double Axis => (AcrossLo + AcrossHi) / 2.0;
            public double Length => FaceHi - FaceLo;

            public override string ToString()
                => string.Format(CultureInfo.InvariantCulture, "{0} {1:0.#}..{2:0.#} @{3:0.#}..{4:0.#} {5}/{6}{7}",
                    AlongX ? "X" : "Y", FaceLo, FaceHi, AcrossLo, AcrossHi, LoKind, HiKind, Manual ? " MANUAL" : "");
        }

        public sealed class Result
        {
            public List<Segment> Segments = new List<Segment>();
            /// <summary>Złącza: narożniki L i złącza T (E4).</summary>
            public List<Joint> Joints = new List<Joint>();
            /// <summary>Komunikaty po polsku (bez prefiksu), liczby InvariantCulture.</summary>
            public List<string> Messages = new List<string>();
            /// <summary>Typowa szerokość (najczęstsza wśród odcinków osiowych; 0 = brak).</summary>
            public double TypicalWidth;
        }

        // ── wewnętrzne ──────────────────────────────────────────────────

        private sealed class Edge
        {
            public bool Horizontal;      // y = C
            public double C, Lo, Hi;
            public int Inward;           // +1: wnętrze po stronie rosnącej współrzędnej
        }

        private sealed class Rect
        {
            public bool AlongX;
            public double Lo, Hi;        // wzdłuż osi
            public double CLo, CHi;      // w poprzek
        }

        private sealed class Leg
        {
            public bool AlongX;
            public List<Rect> Rects = new List<Rect>();
            public double Lo, Hi, CLo, CHi;
            public bool Regular;
            /// <summary>Przyczyna „detalu ręcznego” ustalona przy grupowaniu (skrzyżowanie „+”); null — z analizy skosu.</summary>
            public string Reason;
            public Segment Seg;
            public (double lo, double hi) EndAcross(bool atLo)
            {
                var r = atLo ? Rects.OrderBy(x => x.Lo).First() : Rects.OrderByDescending(x => x.Hi).First();
                return (r.CLo, r.CHi);
            }
        }

        /// <summary>
        /// Analiza strefy: odcinki z końcami Edge / Internal / Corner (L) / Tee. outline = obrys zewnętrzny płyty
        /// (null — końce Internal, bez lica zewnętrznego). Strefa bez krawędzi osiowych → komunikat, brak odcinków.
        /// </summary>
        public static Result Analyze(IList<(double X, double Y)> zone, IList<(double X, double Y)> outline)
        {
            var res = new Result();
            var poly = Clean(zone);
            if (poly.Count < 4)
            {
                res.Messages.Add("strefa belki: za mało wierzchołków — pominięto.");
                return res;
            }
            if (SignedArea(poly) < 0) poly.Reverse();
            var outl = outline != null ? Clean(outline) : null;

            var (axisEdges, diagonals) = Edges(poly);
            var rects = Pair(axisEdges, poly);
            if (rects.Count == 0)
            {
                res.Messages.Add("strefa belki bez par krawędzi osiowych (strefa nieosiowa) — detal ręczny.");
                return res;
            }

            var legs = GroupLegs(rects, poly);
            foreach (var leg in legs)
            {
                var s = new Segment
                {
                    AlongX = leg.AlongX, AcrossLo = leg.CLo, AcrossHi = leg.CHi,
                    OwnLo = leg.Lo, OwnHi = leg.Hi, FaceLo = leg.Lo, FaceHi = leg.Hi, Manual = !leg.Regular
                };
                if (s.Manual)
                {
                    bool skew = diagonals.Any(d => NearLeg(d, leg));
                    s.ManualReason = leg.Reason ?? (skew ? "odcinek nieosiowy (skos)" : "zmienna szerokość / przerwa w strefie");
                }
                leg.Seg = s;
                res.Segments.Add(s);
            }

            Joints(legs, poly, res);

            // Końce bez złącza: Edge / Internal; lico boczne przy krawędzi płyty
            foreach (var leg in legs)
            {
                var s = leg.Seg;
                if (s.LoKind == EndKind.Internal && outl != null && EndAtEdge(s, atLo: true, outl)) s.LoKind = EndKind.Edge;
                if (s.HiKind == EndKind.Internal && outl != null && EndAtEdge(s, atLo: false, outl)) s.HiKind = EndKind.Edge;
                if (outl != null) s.OuterSide = OuterSideOf(s, outl);
            }

            var widths = res.Segments.Where(x => !x.Manual).Select(x => Math.Round(x.Width)).ToList();
            res.TypicalWidth = widths.Count > 0
                ? widths.GroupBy(w => w).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key
                : Math.Round(rects.Min(r => r.CHi - r.CLo));

            PropagateManual(legs);
            AssignClosures(res);

            // Kolejność deterministyczna: najpierw wzdłuż X, potem Y; po położeniu
            res.Segments = res.Segments.OrderBy(x => x.AlongX ? 0 : 1).ThenBy(x => x.AcrossLo).ThenBy(x => x.FaceLo).ToList();
            foreach (var s in res.Segments.Where(x => x.Manual))
                res.Messages.Add(string.Format(CultureInfo.InvariantCulture,
                    "odcinek {0} ({1:0}..{2:0}): {3} — detal ręczny, pręty pominięte.",
                    s.AlongX ? "poziomy" : "pionowy", s.FaceLo, s.FaceHi, s.ManualReason));
            return res;
        }

        private static List<(double X, double Y)> Clean(IList<(double X, double Y)> pts)
        {
            var r = new List<(double X, double Y)>();
            foreach (var p in pts)
                if (r.Count == 0 || Dist(r[r.Count - 1], p) > 1e-3) r.Add(p);
            if (r.Count > 2 && Dist(r[0], r[r.Count - 1]) < 1e-3) r.RemoveAt(r.Count - 1);
            // współliniowe wierzchołki (osiowe, z tolerancją) — usuń
            bool changed = true;
            while (changed && r.Count > 3)
            {
                changed = false;
                for (int i = 0; i < r.Count; i++)
                {
                    var a = r[(i - 1 + r.Count) % r.Count]; var b = r[i]; var c = r[(i + 1) % r.Count];
                    bool h = Math.Abs(a.Y - b.Y) <= AxisTol && Math.Abs(b.Y - c.Y) <= AxisTol;
                    bool v = Math.Abs(a.X - b.X) <= AxisTol && Math.Abs(b.X - c.X) <= AxisTol;
                    if (h || v) { r.RemoveAt(i); changed = true; break; }
                }
            }
            return r;
        }

        private static (List<Edge> axis, List<((double X, double Y) a, (double X, double Y) b)> diag)
            Edges(List<(double X, double Y)> poly)
        {
            var axis = new List<Edge>();
            var diag = new List<((double X, double Y), (double X, double Y))>();
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                if (Math.Abs(dy) <= AxisTol && Math.Abs(dx) > AxisTol)
                    // CCW: wnętrze po lewej; krawędź w +X → wnętrze +Y
                    axis.Add(new Edge { Horizontal = true, C = (a.Y + b.Y) / 2, Lo = Math.Min(a.X, b.X), Hi = Math.Max(a.X, b.X), Inward = dx > 0 ? 1 : -1 });
                else if (Math.Abs(dx) <= AxisTol && Math.Abs(dy) > AxisTol)
                    axis.Add(new Edge { Horizontal = false, C = (a.X + b.X) / 2, Lo = Math.Min(a.Y, b.Y), Hi = Math.Max(a.Y, b.Y), Inward = dy > 0 ? -1 : 1 });
                else if (Math.Abs(dx) > AxisTol || Math.Abs(dy) > AxisTol)
                    diag.Add((a, b));
            }
            return (axis, diag);
        }

        /// <summary>Pary krawędzi „lico — lico”: dla każdej krawędzi najbliższa przeciwległa w zakresie MinWidth–MaxWidth.</summary>
        private static List<Rect> Pair(List<Edge> edges, List<(double X, double Y)> poly)
        {
            var rects = new List<Rect>();
            foreach (var e in edges)
            {
                var cands = edges.Where(f => f != e && f.Horizontal == e.Horizontal && f.Inward == -e.Inward)
                                 .Select(f => (f, d: (f.C - e.C) * e.Inward))
                                 .Where(x => x.d >= MinWidth && x.d <= MaxWidth)
                                 .OrderBy(x => x.d).ToList();
                var free = new List<(double lo, double hi)> { (e.Lo, e.Hi) };
                foreach (var (f, d) in cands)
                {
                    var next = new List<(double lo, double hi)>();
                    foreach (var (lo, hi) in free)
                    {
                        double olo = Math.Max(lo, f.Lo), ohi = Math.Min(hi, f.Hi);
                        if (ohi - olo <= 1.0) { next.Add((lo, hi)); continue; }
                        // tylko raz na parę (krawędź o mniejszej współrzędnej) — druga da ten sam prostokąt
                        if (e.C < f.C)
                        {
                            var r = new Rect { AlongX = e.Horizontal, Lo = olo, Hi = ohi, CLo = e.C, CHi = f.C };
                            var mid = r.AlongX ? ((r.Lo + r.Hi) / 2, (r.CLo + r.CHi) / 2) : ((r.CLo + r.CHi) / 2, (r.Lo + r.Hi) / 2);
                            if (Inside(poly, mid)) rects.Add(r);
                        }
                        if (olo - lo > 1.0) next.Add((lo, olo));
                        if (hi - ohi > 1.0) next.Add((ohi, hi));
                    }
                    free = next;
                    if (free.Count == 0) break;
                }
            }
            return rects;
        }

        /// <summary>Prostokąty tej samej orientacji o wspólnym licu (przerwa w strefie) → jeden odcinek.</summary>
        private static List<Leg> GroupLegs(List<Rect> rects, List<(double X, double Y)> poly)
        {
            int n = rects.Count;
            var parent = Enumerable.Range(0, n).ToArray();
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    var a = rects[i]; var b = rects[j];
                    if (a.AlongX != b.AlongX) continue;
                    bool sideLo = Math.Abs(a.CLo - b.CLo) <= JoinTol, sideHi = Math.Abs(a.CHi - b.CHi) <= JoinTol;
                    if (!sideLo && !sideHi) continue;
                    double gap = Math.Max(a.Lo, b.Lo) - Math.Min(a.Hi, b.Hi);
                    if (gap > MaxLegGap) continue;
                    if (gap > JoinTol)
                    {
                        // przerwa musi leżeć w strefie (np. skos) — próbka przy wspólnym licu
                        double g = (Math.Min(a.Hi, b.Hi) + Math.Max(a.Lo, b.Lo)) / 2;
                        double c = sideLo ? a.CLo + 1.0 : a.CHi - 1.0;
                        if (!Inside(poly, a.AlongX ? (g, c) : (c, g))) continue;
                    }
                    parent[Find(i)] = Find(j);
                }
            var legs = new List<Leg>();
            foreach (var grp in Enumerable.Range(0, n).GroupBy(Find))
            {
                var rs = grp.Select(i => rects[i]).OrderBy(r => r.Lo).ToList();
                var leg = new Leg { AlongX = rs[0].AlongX, Rects = rs, Lo = rs.Min(r => r.Lo), Hi = rs.Max(r => r.Hi) };
                bool sameSide = rs.All(r => Math.Abs(r.CLo - rs[0].CLo) <= JoinTol && Math.Abs(r.CHi - rs[0].CHi) <= JoinTol);
                bool contiguous = true, crossing = false;
                for (int k = 1; k < rs.Count; k++)
                {
                    if (rs[k].Lo - rs[k - 1].Hi <= JoinTol) continue;
                    // E4: przerwa w licu wypełniona strefą na całej szerokości (złącze T — belka dochodząca) — belka ciągła przez
                    // węzeł, nie „detal ręczny”. Strefa po OBU stronach przerwy (skrzyżowanie „+”) — „detal ręczny” (strzemiona obu
                    // kierunków w węźle dublowałyby się; recenzja E4)
                    if (!sameSide || !GapFilled(poly, rs[0].AlongX, rs[k - 1].Hi, rs[k].Lo, rs[0].CLo, rs[0].CHi)) contiguous = false;
                    else if (IsCrossing(poly, rs[0].AlongX, rs[k - 1].Hi, rs[k].Lo, rs[0].CLo, rs[0].CHi)) crossing = true;
                }
                leg.Regular = sameSide && contiguous && !crossing;
                if (crossing && sameSide && contiguous) leg.Reason = "skrzyżowanie belek („+”) — węzeł do rozwiązania ręcznie";
                leg.CLo = leg.Regular ? rs.Average(r => r.CLo) : rs.Min(r => r.CLo);
                leg.CHi = leg.Regular ? rs.Average(r => r.CHi) : rs.Max(r => r.CHi);
                legs.Add(leg);
            }
            return legs;
        }

        /// <summary>Przerwa [g0, g1] wzdłuż osi na szerokości [c0, c1] w całości w strefie (próbki 3 × 3 wewnątrz).</summary>
        private static bool GapFilled(List<(double X, double Y)> poly, bool alongX, double g0, double g1, double c0, double c1)
        {
            foreach (double g in new[] { g0 + 1.0, (g0 + g1) / 2, g1 - 1.0 })
                foreach (double c in new[] { c0 + 1.0, (c0 + c1) / 2, c1 - 1.0 })
                    if (!Inside(poly, alongX ? (g, c) : (c, g))) return false;
            return true;
        }

        /// <summary>
        /// Przerwa [g0, g1] wypełniona strefą także tuż za OBOMA licami [c0, c1] (próbka w środku przerwy, 1 mm za licem) —
        /// skrzyżowanie „+” (w złączu T strefa jest tylko po stronie belki dochodzącej).
        /// </summary>
        private static bool IsCrossing(List<(double X, double Y)> poly, bool alongX, double g0, double g1, double c0, double c1)
        {
            double g = (g0 + g1) / 2;
            return Inside(poly, alongX ? (g, c0 - 1.0) : (c0 - 1.0, g)) && Inside(poly, alongX ? (g, c1 + 1.0) : (c1 + 1.0, g));
        }

        private static bool NearLeg(((double X, double Y) a, (double X, double Y) b) d, Leg leg)
        {
            double mx = (d.a.X + d.b.X) / 2, my = (d.a.Y + d.b.Y) / 2;
            double along = leg.AlongX ? mx : my, across = leg.AlongX ? my : mx;
            return along >= leg.Lo - JoinTol && along <= leg.Hi + JoinTol
                && across >= leg.CLo - MaxWidth && across <= leg.CHi + MaxWidth;
        }

        /// <summary>Narożniki L (kwadrat między końcami dwóch prostopadłych odcinków) i złącza T.</summary>
        private static void Joints(List<Leg> legs, List<(double X, double Y)> poly, Result res)
        {
            foreach (var a in legs.Where(l => l.AlongX))
                foreach (var b in legs.Where(l => !l.AlongX))
                    foreach (bool aLo in new[] { true, false })
                        foreach (bool bLo in new[] { true, false })
                        {
                            var (ya0, ya1) = a.EndAcross(aLo);
                            var (xb0, xb1) = b.EndAcross(bLo);
                            bool aTouch = aLo ? Math.Abs(xb1 - a.Lo) <= JoinTol : Math.Abs(xb0 - a.Hi) <= JoinTol;
                            bool bTouch = bLo ? Math.Abs(ya1 - b.Lo) <= JoinTol : Math.Abs(ya0 - b.Hi) <= JoinTol;
                            if (!aTouch || !bTouch) continue;
                            if (!Inside(poly, ((xb0 + xb1) / 2, (ya0 + ya1) / 2))) continue;
                            if (aLo) { a.Seg.LoKind = EndKind.Corner; a.Seg.FaceLo = xb0; }
                            else     { a.Seg.HiKind = EndKind.Corner; a.Seg.FaceHi = xb1; }
                            if (bLo) { b.Seg.LoKind = EndKind.Corner; b.Seg.FaceLo = ya0; }
                            else     { b.Seg.HiKind = EndKind.Corner; b.Seg.FaceHi = ya1; }
                            res.Joints.Add(new Joint { A = a.Seg, B = b.Seg, ALo = aLo, BLo = bLo });
                        }

            // T: koniec odcinka na licu bocznym innego (prostopadłego), wewnątrz jego zakresu
            foreach (var b in legs)
                foreach (bool bLo in new[] { true, false })
                {
                    if ((bLo ? b.Seg.LoKind : b.Seg.HiKind) != EndKind.Internal) continue;
                    double end = bLo ? b.Lo : b.Hi;
                    var (c0, c1) = b.EndAcross(bLo);
                    foreach (var a in legs.Where(l => l.AlongX != b.AlongX))
                    {
                        if (c0 < a.Lo + JoinTol || c1 > a.Hi - JoinTol) continue;
                        double face;
                        if (bLo && Math.Abs(end - a.CHi) <= JoinTol) face = a.CLo;        // b nad a — dalsze lico a
                        else if (!bLo && Math.Abs(end - a.CLo) <= JoinTol) face = a.CHi;
                        else continue;
                        if (bLo) { b.Seg.LoKind = EndKind.Tee; b.Seg.FaceLo = face; }
                        else     { b.Seg.HiKind = EndKind.Tee; b.Seg.FaceHi = face; }
                        res.Joints.Add(new Joint { IsTee = true, A = a.Seg, B = b.Seg, BLo = bLo });
                        break;
                    }
                }
        }

        /// <summary>
        /// Odcinek, którego koniec bez rozpoznanego złącza (Internal / Edge — nie Corner / Tee) przylega (wspólny koniec / naroże,
        /// tolerancja JoinTol) do odcinka „detal ręczny”, też staje się „detal ręczny” (bez prętów i strzemion) — inaczej pręty
        /// kończyłyby się w środku węzła (np. L z krótką nogą 600 × 1200). Powtarzane do ustalenia (łańcuch odcinków).
        /// Złącza rozpoznane (narożnik L / T z odcinkiem ręcznym — PLOT 319) bez zmian.
        /// </summary>
        private static void PropagateManual(List<Leg> legs)
        {
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var leg in legs)
                {
                    var s = leg.Seg;
                    if (s.Manual) continue;
                    foreach (bool atLo in new[] { true, false })
                    {
                        var kind = atLo ? s.LoKind : s.HiKind;
                        if (kind == EndKind.Corner || kind == EndKind.Tee) continue;
                        double along = atLo ? s.OwnLo : s.OwnHi;
                        // lico końca jako odcinek (prostokąt zdegenerowany) w układzie X / Y
                        double x0, x1, y0, y1;
                        if (s.AlongX) { x0 = x1 = along; y0 = s.AcrossLo; y1 = s.AcrossHi; }
                        else          { y0 = y1 = along; x0 = s.AcrossLo; x1 = s.AcrossHi; }
                        var hit = legs.FirstOrDefault(m => m != leg && m.Seg.Manual && m.Rects.Any(r =>
                        {
                            double rx0 = r.AlongX ? r.Lo : r.CLo, rx1 = r.AlongX ? r.Hi : r.CHi;
                            double ry0 = r.AlongX ? r.CLo : r.Lo, ry1 = r.AlongX ? r.CHi : r.Hi;
                            return x0 <= rx1 + JoinTol && x1 >= rx0 - JoinTol && y0 <= ry1 + JoinTol && y1 >= ry0 - JoinTol;
                        }));
                        if (hit == null) continue;
                        s.Manual = true;
                        s.ManualReason = "przylega do odcinka „detal ręczny” (węzeł nierozpoznany)";
                        changed = true;
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Zamknięcia końców odcinków osiowych i zakres strzemion (K3 — praktyka biura; domyślne, do potwierdzenia przez Bartka).
        /// Koniec Edge / Internal → pionowe U-bary (koniec wewnętrzny tak samo jak przy krawędzi); narożnik L dwóch odcinków osiowych →
        /// każda noga własnymi pionowymi U-barami (tył na licu zewnętrznym narożnika), strzemiona dłuższego odcinka (równe — odcinka
        /// wzdłuż X) do lica zewnętrznego narożnika (kontynuacja jednego kierunku, bez zdublowania; parametr [34]); złącze T z osiową
        /// belką ciągłą → belka ciągła bez przerwy, dochodząca jak noga narożnika (U-bary z tyłem na dalszym licu belki ciągłej).
        /// Złącze z odcinkiem „detal ręczny” — bez zamknięcia (pręty do lica, jak E3).
        /// </summary>
        private static void AssignClosures(Result res)
        {
            foreach (var s in res.Segments)
            {
                s.LinkLo = s.OwnLo; s.LinkHi = s.OwnHi;
                s.LoClose = s.HiClose = Closure.None;
                if (s.Manual) continue;
                if (s.LoKind == EndKind.Edge || s.LoKind == EndKind.Internal) s.LoClose = Closure.UBar;
                if (s.HiKind == EndKind.Edge || s.HiKind == EndKind.Internal) s.HiClose = Closure.UBar;
            }
            foreach (var j in res.Joints)
            {
                if (j.A.Manual || j.B.Manual) continue;
                if (j.IsTee) { SetClose(j.B, j.BLo, Closure.Tee); continue; }
                SetClose(j.A, j.ALo, Closure.LBar);
                SetClose(j.B, j.BLo, Closure.LBar);
                var (m, lo) = j.B.Length > j.A.Length + JoinTol ? (j.B, j.BLo) : (j.A, j.ALo);
                if (lo) m.LinkLo = m.FaceLo; else m.LinkHi = m.FaceHi;
            }
        }

        private static void SetClose(Segment s, bool lo, Closure c)
        {
            if (lo) s.LoClose = c; else s.HiClose = c;
        }

        private static bool EndAtEdge(Segment s, bool atLo, List<(double X, double Y)> outline)
        {
            double along = atLo ? s.OwnLo : s.OwnHi;
            int dir = atLo ? -1 : 1;
            var p = s.AlongX ? (along, s.Axis) : (s.Axis, along);
            var v = s.AlongX ? ((double)dir, 0.0) : (0.0, (double)dir);
            double d = RayHit(outline, p, v);
            return d <= EdgeEndMaxGap;
        }

        private static int OuterSideOf(Segment s, List<(double X, double Y)> outline)
        {
            double mid = (s.OwnLo + s.OwnHi) / 2;
            double best = double.MaxValue; int side = 0;
            foreach (int sgn in new[] { -1, 1 })
            {
                double c = sgn < 0 ? s.AcrossLo : s.AcrossHi;
                var p = s.AlongX ? (mid, c) : (c, mid);
                var v = s.AlongX ? (0.0, (double)sgn) : ((double)sgn, 0.0);
                double d = RayHit(outline, p, v);
                if (d <= EdgeEndMaxGap && d < best) { best = d; side = sgn; }
            }
            return side;
        }

        /// <summary>Odległość od p wzdłuż kierunku v (jednostkowego, osiowego) do najbliższego przecięcia z obrysem; ∞ gdy brak.</summary>
        private static double RayHit(List<(double X, double Y)> poly, (double X, double Y) p, (double X, double Y) v)
        {
            double best = double.PositiveInfinity;
            for (int i = 0; i < poly.Count; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % poly.Count];
                double ex = b.X - a.X, ey = b.Y - a.Y;
                double den = v.X * ey - v.Y * ex;
                if (Math.Abs(den) < 1e-12) continue;
                double wx = a.X - p.X, wy = a.Y - p.Y;
                double t = (wx * ey - wy * ex) / den;      // wzdłuż promienia
                double u = (wx * v.Y - wy * v.X) / den;    // wzdłuż krawędzi
                if (t >= -1e-6 && u >= -1e-9 && u <= 1 + 1e-9 && t < best) best = Math.Max(0, t);
            }
            return best;
        }

        // ── strzemiona, plan prętów, wymiary ─────────────────────────────

        /// <summary>
        /// Strzemiona w zakresie [lo, hi] (własny zakres odcinka): bez UB 03 — n = ceil((L − 2·endOffset)/s) + 1,
        /// równo od lo + endOffset do hi − endOffset (rozstaw ≤ s, plan 1c). Z UB 03 wzdłuż tego odcinka (położenia
        /// ub03, rozstaw ≤ s) — położenia UB 03 + shift (domyślnie 100: w połowie między U-barami, kolizja dolnych ramion),
        /// rozstaw jak UB 03, w granicach [lo + endOffset, hi − endOffset]. Wynik: pierwszy, rozstaw, liczba.
        /// endOffsetHi — odsunięcie przy końcu hi, gdy inne niż przy lo (np. inna otulina końca); NaN = endOffset.
        /// </summary>
        public static (double First, double Step, int Count, bool FromUb03) StirrupLayout(
            double lo, double hi, double spacing = 200, double endOffset = 50, IList<double> ub03 = null, double shift = 100,
            double endOffsetHi = double.NaN)
        {
            double offLo = endOffset, offHi = double.IsNaN(endOffsetHi) ? endOffset : endOffsetHi;
            double L = hi - lo;
            if (L <= 0 || spacing <= 0) return (lo, 0, 0, false);
            if (L <= offLo + offHi + 1e-6) return ((lo + hi) / 2, 0, 1, false);

            if (ub03 != null && ub03.Count >= 2)
            {
                var u = ub03.OrderBy(x => x).ToList();
                double su = (u[u.Count - 1] - u[0]) / (u.Count - 1);
                if (su > 0.5 * spacing && su <= spacing + 0.5)
                {
                    double b = u[0] + shift;
                    int kmin = (int)Math.Ceiling((lo + offLo - b) / su - 1e-9);
                    int kmax = (int)Math.Floor((hi - offHi - b) / su + 1e-9);
                    int cnt = kmax - kmin + 1;
                    if (cnt >= 2) return (b + kmin * su, su, cnt, true);
                }
            }

            // decyzja użytkownika 2026-10-12: „strzemion tyle, ile się zmieści na długości” — rozstaw = s, liczba w dół;
            // 2026-10-13: pierwsze strzemię zawsze przy licu (otulina 40), reszta przy drugim końcu (wcześniej po równo na obu
            // końcach — pierwsze np. 106 od lica)
            // 2026-10-13 (2): ostatnie strzemię dalej niż LinkMaxEndGap (80) od lica końca (np. 166 przy kwadracie narożnika) —
            // +1 strzemię, rozstaw równy ≤ s, oba końce przy otulinie (jak UB: zagęszczamy, nigdy nie rozszerzamy)
            double span = L - offLo - offHi;
            int n = (int)Math.Floor(span / spacing + 1e-9) + 1;
            double rest = span - (n - 1) * spacing;
            if (n >= 1 && offHi + rest > LinkMaxEndGap + 1e-6)
            {
                n++;
                return (lo + offLo, span / (n - 1), n, false);
            }
            return (lo + offLo, n > 1 ? spacing : 0, n, false);
        }

        /// <summary>
        /// K2 (praktyka biura): oś pierwszego (i ostatniego) strzemienia od lica końca zakresu strzemion = otulina boczna / czołowa
        /// + Ø strzemienia / 2 (45 + 6 = 51 dla H12) — także przy końcach z U-barami (C12: strzemiona na całej długości, także
        /// w strefie U-barów; wcześniej E4: 125 za tyłami poziomych U-barów).
        /// </summary>
        public static double StirrupFirstOffset(int dLink, double cover = DefaultCoverSide) => cover + dLink / 2.0;

        /// <summary>Otulina strzemion od lica końca zakresu (decyzja użytkownika 2026-10-13 „zaczynają się 40 mm od krawędzi belki”):
        /// oś pierwszego strzemienia = 40 + Ø/2 (H12: 46).</summary>
        public const double LinkEndCover = 40.0;

        /// <summary>Największa odległość osi ostatniego strzemienia od lica końca zakresu bez dodatkowego strzemienia [mm]
        /// (decyzja użytkownika 2026-10-13: „80 od krawędzi ok, więcej — zagęszczamy”).</summary>
        public const double LinkMaxEndGap = 80.0;

        /// <summary>
        /// Plan prętów podłużnych (proste, siatka 250, maks. maxLen, zakład w przedziale [lap, lap + grid) — decyzja 1c: 800,
        /// zakład 800 … &lt; 1050; domyślne, do potwierdzenia).
        /// available ≤ maxLen → jeden pręt przycięty w dół do siatki (jak ComputeDistributionPlan; krótszy niż minLen — do 25 mm),
        /// WYŚRODKOWANY (offset = (available − L)/2 — równa otulina na obu końcach).
        /// Dłużej (N = najmniejsza liczba prętów; mniej zakładów zdecydowanie lepiej): a) N prętów równych (jedna pozycja) na siatce
        /// (≤ maxLen), równe zakłady (N·L − available)/(N−1) w przedziale; b) N prętów: N − 1 równych na siatce (zakład = lap)
        /// + ostatni krótszy na siatce (osobna pozycja), jego zakład w przedziale; c) dopiero potem N + 1 prętów równych.
        /// Np. 6251 → 3750 + 3500 (zakład 999), nie 3 × 2750.
        /// Wynik: (początek względem lico + otulina, długość).
        /// </summary>
        public static List<(double Offset, double Length)> PlanBars(double available, double maxLen = 6000, double lap = 800,
                                                                    double grid = 250, double minLen = 1250)
        {
            var res = new List<(double, double)>();
            if (available <= 0 || maxLen <= 0 || grid <= 0) return res;
            if (available <= maxLen + 0.5)
            {
                double L = Math.Floor(available / grid + 1e-9) * grid;
                if (L < minLen) L = Math.Floor(available / 25.0 + 1e-9) * 25.0;
                L = Math.Min(L, maxLen);
                if (L > 0) res.Add((Math.Max(0.0, (available - L) / 2.0), L));
                return res;
            }
            if (lap >= maxLen) return res;
            double lapHi = lap + grid;                                    // zakład < lap + grid
            double gridMax = Math.Floor(maxLen / grid + 1e-9) * grid;     // najdłuższy pręt na siatce
            int n0 = Math.Max(2, (int)Math.Ceiling((available - lap) / (maxLen - lap) - 1e-9));
            // Mniej zakładów zdecydowanie lepiej (zasada AutoRebar): dla N prętów najpierw a) równe, potem b) N − 1 równych +
            // ostatni krótszy; dopiero gdy przy N nie wyjdzie żaden wariant — c) N + 1 równych (i dalej kolejne N).
            for (int N = n0; N <= n0 + 20; N++)
            {
                if (TryEqualBars(available, N, lap, lapHi, grid, gridMax, res)) return res;      // a) N równych
                // b) N − 1 prętów równych (zakład = lap) + ostatni krótszy na siatce (zakład w przedziale)
                {
                    double L = Math.Ceiling((available + (N - 1) * lap) / N / grid - 1e-9) * grid;
                    if (L <= gridMax + 1e-6)
                    {
                        double end = (N - 1) * L - (N - 2) * lap;             // koniec (N−1)-ego pręta
                        double need = available - end + lap;                  // ostatni pręt z zakładem lap
                        double Llast = Math.Ceiling(need / grid - 1e-9) * grid;
                        if (need > lap + 1e-6                                 // N − 1 prętów nie wystarcza
                            && Llast <= L + 1e-6 && Llast <= gridMax + 1e-6)
                        {
                            for (int i = 0; i < N - 1; i++) res.Add((i * (L - lap), L));
                            res.Add((available - Llast, Llast));
                            return res;
                        }
                    }
                }
                if (TryEqualBars(available, N + 1, lap, lapHi, grid, gridMax, res)) return res;  // c) N + 1 równych
            }
            return res;
        }

        /// <summary>
        /// Pręty równe (jedna pozycja): n prętów na siatce (≤ gridMax), równe zakłady (n·L − available)/(n − 1) w [lap, lapHi).
        /// Dopisuje plan do res i zwraca true; gdy się nie da — false (res bez zmian).
        /// </summary>
        private static bool TryEqualBars(double available, int n, double lap, double lapHi, double grid, double gridMax,
                                         List<(double, double)> res)
        {
            if (n < 2) return false;
            double L = Math.Ceiling((available + (n - 1) * lap) / n / grid - 1e-9) * grid;
            if (L > gridMax + 1e-6) return false;
            double O = (n * L - available) / (n - 1);
            if (O < lap - 1e-6 || O >= lapHi - 1e-6) return false;
            for (int i = 0; i < n; i++) res.Add((i * (L - O), L));
            return true;
        }

        /// <summary>Odsunięcie zakładów prętów bocznych od zakładów prętów głównych (uwaga Bartka 2026-10-28): min. odstęp w świetle.</summary>
        public const double LapStaggerGap = 500.0;

        /// <summary>Strefy zakładów planu [początek następnego pręta, koniec poprzedniego] (układ planu 0 … available).</summary>
        public static List<(double Lo, double Hi)> LapZones(List<(double Offset, double Length)> plan)
        {
            var r = new List<(double, double)>();
            if (plan == null) return r;
            for (int i = 1; i < plan.Count; i++) r.Add((plan[i].Offset, plan[i - 1].Offset + plan[i - 1].Length));
            return r;
        }

        /// <summary>
        /// Uwaga Bartka 2026-10-28 (detal GB PLOT 344-346): zakłady prętów (np. bocznych SIDE) NIE w tym samym miejscu co zakłady prętów
        /// głównych — wszystkie złącza planu przesunięte o to samo delta (pierwszy pręt dłuższy, ostatni krótszy), najmniejsze |delta|
        /// (najpierw co <paramref name="grid"/>, potem co 50), tak by każda strefa zakładu była ≥ <paramref name="minGap"/> w świetle od każdej
        /// strefy z <paramref name="avoid"/>; pręty ≤ maxLen i ≥ minLen. Gdy się nie da albo nie trzeba — plan bez zmian.
        /// </summary>
        public static List<(double Offset, double Length)> StaggerLaps(List<(double Offset, double Length)> plan, double maxLen,
                                                                       IList<(double Lo, double Hi)> avoid, double minGap = LapStaggerGap,
                                                                       double grid = 250, double minLen = 1000)
        {
            if (plan == null || plan.Count < 2 || avoid == null || avoid.Count == 0) return plan;
            bool Ok(List<(double Offset, double Length)> p)
            {
                foreach (var (lo, hi) in LapZones(p))
                    foreach (var (a, b) in avoid)
                        if (Math.Max(a - hi, lo - b) < minGap - 1e-6) return false;
                return true;
            }
            if (Ok(plan)) return plan;
            List<(double Offset, double Length)> Shift(double d)
            {
                var p = new List<(double Offset, double Length)>();
                int n = plan.Count;
                for (int i = 0; i < n; i++)
                {
                    double o = i == 0 ? plan[i].Offset : plan[i].Offset + d;
                    double len = plan[i].Length + (i == 0 ? d : 0) - (i == n - 1 ? d : 0);
                    if (n == 1) len = plan[i].Length;
                    if (len > maxLen + 1e-6 || len < minLen - 1e-6) return null;
                    p.Add((o, len));
                }
                return p;
            }
            foreach (double step in new[] { grid, 50.0 })
                for (int k = 1; k * step <= maxLen; k++)
                    foreach (double d in new[] { k * step, -k * step })
                    {
                        var p = Shift(d);
                        if (p != null && Ok(p)) return p;
                    }
            return plan;
        }

        /// <summary>Zakład planu (z dwóch pierwszych prętów); 0 dla jednego pręta.</summary>
        public static double LapOf(List<(double Offset, double Length)> plan)
            => plan != null && plan.Count > 1 ? plan[0].Offset + plan[0].Length - plan[1].Offset : 0;

        /// <summary>Wszystkie zakłady planu (między kolejnymi prętami); pusta lista dla jednego pręta.</summary>
        public static List<double> LapsOf(List<(double Offset, double Length)> plan)
        {
            var r = new List<double>();
            if (plan == null) return r;
            for (int i = 1; i < plan.Count; i++) r.Add(plan[i - 1].Offset + plan[i - 1].Length - plan[i].Offset);
            return r;
        }

        // ── K3: plan warstwy — pręty proste; pionowe U-bary (końce, narożniki L, złącza T) ──

        /// <summary>Pręt prosty warstwy (jeden rozkład): odcinek, numer odcinka (pręt reprezentatywny), początek wzdłuż osi, długość.</summary>
        public sealed class StraightRun
        {
            public Segment Seg;
            public int SegIndex, Index;
            public double Start, Length;
        }

        public sealed class LayerPlan
        {
            public List<StraightRun> Straight = new List<StraightRun>();
            /// <summary>Zakres prętów prostych odcinka (początek pierwszego, koniec ostatniego pręta) — zakład U-barów.</summary>
            public Dictionary<Segment, (double Lo, double Hi)> Bars = new Dictionary<Segment, (double Lo, double Hi)>();
            /// <summary>Komunikaty po polsku (bez prefiksu), liczby InvariantCulture.</summary>
            public List<string> Messages = new List<string>();
        }

        /// <summary>
        /// Zakres prętów prostych odcinka wzdłuż osi (K3): koniec z U-barami (UBar) — lico ∓ endOffset (otulina + Ø U-bara = 61,
        /// parametr [32]; NaN = otulina); narożnik L / belka dochodząca złącza T — lico własnego zakresu (pręty proste kończą się PRZED
        /// kwadratem narożnika / na bliższym licu belki ciągłej — C12, domyślne) albo, gdy <paramref name="intoSquare"/>, w kwadrat do
        /// lica zewnętrznego (dalszego lica belki ciągłej) ∓ endOffset (Holbrook); złącze z odcinkiem „detal ręczny” — lico z otuliną.
        /// </summary>
        public static (double Lo, double Hi) StraightRange(Segment s, double cover = DefaultCoverSide, double endOffset = double.NaN,
                                                           bool intoSquare = false)
        {
            double eo = double.IsNaN(endOffset) ? cover : endOffset;
            double End(bool atLo)
            {
                var c = atLo ? s.LoClose : s.HiClose;
                int dir = atLo ? 1 : -1;
                double face = atLo ? s.FaceLo : s.FaceHi, own = atLo ? s.OwnLo : s.OwnHi;
                switch (c)
                {
                    case Closure.UBar: return face + dir * eo;
                    // decyzja użytkownika 2026-10-12: narożnik L — noga pionowa (wzdłuż Y) wchodzi prętami w kwadrat do lica zewnętrznego,
                    // noga pozioma (wzdłuż X) kończy się przed kwadratem; [31] intoSquare — obie nogi w kwadrat
                    case Closure.LBar: return intoSquare || !s.AlongX ? face + dir * eo : own;
                    case Closure.Tee: return intoSquare ? face + dir * eo : own;
                    default: return face + dir * cover;
                }
            }
            return (End(true), End(false));
        }

        /// <summary>Osie prętów warstwy w poprzek odcinka (rosnąco), jak BarsAcross.</summary>
        public static List<double> RowsAcross(Segment s, int count, double d, double dLink = 10, double cover = DefaultCoverSide)
        {
            var (f, sp) = BarsAcross(s.AcrossLo, s.AcrossHi, count, d, dLink, cover);
            return Enumerable.Range(0, Math.Max(1, count)).Select(i => f + i * sp).ToList();
        }

        /// <summary>Wymiar w górę do 25 mm (zakład ≥ zadany).</summary>
        public static double Ceil25(double v) => Math.Ceiling(v / 25.0 - 1e-6) * 25.0;

        /// <summary>Moduł długości całkowitej U-barów (kształt 21) — decyzja Bartka 2026-10-23: „total length co 50 mm”.</summary>
        public const double UBarTotalModule = 50.0;

        /// <summary>
        /// Ramię A = C U-bara (kształt 21, B bez zmian) dobrane tak, żeby długość całkowita BS 8666 była w module 50 mm (decyzja Bartka
        /// 2026-10-23, wsporniki i belki): ramię W GÓRĘ co 5 mm (zakład tylko rośnie, 1275 / 175 → 1290: 2675 → 2700); gdy w górę
        /// nie wolno (<paramref name="maxArm"/> — ramię ograniczone geometrią) — w dół co 5 mm, najwyżej o 50. Bez rozwiązania — bez zmian.
        /// </summary>
        public static double UBarArmForModule(double arm, double b, int dia, double maxArm = double.PositiveInfinity)
        {
            if (!(arm > 0) || !(b > 0) || dia <= 0) return arm;
            bool Ok(double a)
            {
                try
                {
                    double len = BarLengthRules.TotalLengthFor("21", new[] { a, b, a }, dia);
                    return Math.Abs(len / UBarTotalModule - Math.Round(len / UBarTotalModule)) < 1e-6;
                }
                catch (Exception ex) { Log.Error("BeamGeometry.UBarArmForModule", ex); return false; }
            }
            for (int k = 0; k <= 20; k++)
            {
                double a = arm + 5.0 * k;
                if (a > maxArm + 1e-6) break;
                if (Ok(a)) return a;
            }
            for (int k = 1; k <= 10; k++)
            {
                double a = arm - 5.0 * k;
                if (a <= 0) break;
                if (Ok(a)) return a;
            }
            return arm;
        }

        private static double Floor25(double v) => Math.Floor(v / 25.0 + 1e-6) * 25.0;

        private static double Floor5(double v) => Math.Floor(v / 5.0 + 1e-6) * 5.0;
        /// <summary>W górę do 5 mm (2·Ø: H12 24 → 25, H16 32 → 35).</summary>
        public static double Ceil5(double v) => Math.Ceiling(v / 5.0 - 1e-6) * 5.0;

        /// <summary>
        /// Warstwy strzemion (decyzja użytkownika 2026-10-13): odcinki wzdłuż Y — pierwsza warstwa, A = H − otuliny (z zapasem);
        /// odcinki wzdłuż X — druga warstwa, A − 15 (strzemiona odsunięte od górnej siatki o pręt górny 12 + 3 zapasu).
        /// </summary>
        public const double SecondLayerLinkDrop = 15.0;

        /// <summary>Wysokość strzemienia odcinka: <paramref name="firstLayerA"/> (wzdłuż Y) albo − 15 (wzdłuż X, druga warstwa).</summary>
        public static double LinkHeightFor(double firstLayerA, bool alongX)
            => alongX ? Math.Max(0, firstLayerA - SecondLayerLinkDrop) : firstLayerA;

        /// <summary>
        /// K3: pręty proste jednej warstwy (BM / BB / BT) wszystkich odcinków osiowych — PlanBars na zakresie
        /// <see cref="StraightRange"/> (zakład = lap, maks. maxLen). Bez prętów giętych w poziomie (pręty L i poziome U-bary usunięte
        /// — praktyka biura: końce i narożniki zamykają pionowe U-bary, <see cref="PlanVerticalUBars"/>).
        /// </summary>
        public static LayerPlan PlanLayer(Result an, int d, double lap, double maxLen, double cover = DefaultCoverSide,
                                          double endOffset = double.NaN, bool intoSquare = false)
        {
            var lp = new LayerPlan();
            if (an == null || d <= 0) return lp;
            var segs = an.Segments.Where(x => !x.Manual).ToList();
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                var (lo, hi) = StraightRange(s, cover, endOffset, intoSquare);
                var plan = PlanBars(hi - lo, maxLen, lap);
                for (int k = 0; k < plan.Count; k++)
                    lp.Straight.Add(new StraightRun { Seg = s, SegIndex = i, Index = k, Start = lo + plan[k].Offset, Length = plan[k].Length });
                if (plan.Count > 0)
                    lp.Bars[s] = (lo + plan[0].Offset, lo + plan[plan.Count - 1].Offset + plan[plan.Count - 1].Length);
                else
                {
                    lp.Bars[s] = (lo, hi);
                    lp.Messages.Add(string.Format(CultureInfo.InvariantCulture,
                        "odcinek {0} ({1:0}..{2:0}): brak miejsca na pręty proste — U-bary bez zakładu z prętami prostymi.",
                        s.AlongX ? "poziomy" : "pionowy", s.FaceLo, s.FaceHi));
                }
            }
            foreach (var j in an.Joints.Where(j => j.A.Manual || j.B.Manual))
                lp.Messages.Add(j.IsTee ? "złącze T z odcinkiem „detal ręczny” — bez U-barów, pręty proste do lica (jak E3)."
                                        : "narożnik L z odcinkiem „detal ręczny” — bez U-barów, pręty proste do lica (jak E3).");
            return lp;
        }

        /// <summary>
        /// Pionowy U-bar (kształt 21 w płaszczyźnie przekroju) jednego końca odcinka — jeden rozkład: po jednym na kolumnę prętów
        /// (4 szt.), w rzucie linia ramienia (od tyłu U wzdłuż osi belki) z kółkiem na zagięciu (konwencja UB płyty).
        /// </summary>
        public sealed class VUBarRun
        {
            /// <summary>"E" — koniec (krawędź / wewnętrzny), "L" — noga narożnika L, "T" — belka dochodząca złącza T.</summary>
            public string Kind;
            public Segment Seg;
            public bool AtLo;
            public int Dia, Count;
            /// <summary>Wymiary BS 8666:2020 (zewnętrzne): A = C ramiona, B — wysokość zewnętrzna U (A strzemienia − 2·Ø strzemienia).</summary>
            public double A, B, C;
            /// <summary>Obniżenie B (noga Y narożnika L / belka dochodząca złącza T — zestawy krzyżują się piętrowo).</summary>
            public bool Dropped;
            /// <summary>Linie ramion wzdłuż X.</summary>
            public bool AlongX;
            /// <summary>Oś pierwszej kolumny w poprzek (najmniejsza współrzędna) i rozstaw kolumn.</summary>
            public double Across0, Spacing;
            /// <summary>Lico zewnętrzne tyłu U (zagięcie) wzdłuż osi; linia ramienia od tyłu w głąb belki na długość A.</summary>
            public double Back, LineStart, LineLength;
            /// <summary>Kółko (zagięcie) na początku linii (koniec Lo) — inaczej na końcu.</summary>
            public bool BendAtStart;
            /// <summary>Najmniejszy zakład z prętami prostymi [mm] (NaN — brak prętów prostych).</summary>
            public double Overlap;

            public override string ToString()
                => string.Format(CultureInfo.InvariantCulture, "{0} 21 H{1} A={2:0} B={3:0} C={4:0} ×{5}{6} zakład {7:0}",
                    Kind, Dia, A, B, C, Count, Dropped ? " (B − obniżenie)" : "", Overlap);
        }

        /// <summary>
        /// K3 (praktyka biura C12 / Holbrook — docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md; domyślne do potwierdzenia): pionowe U-bary
        /// kształt 21 na każdym końcu z zamknięciem UBar / LBar / Tee, po jednym na kolumnę prętów (columns), w osiach kolumn:
        ///   • (decyzja użytkownika 2026-10-13) B = A strzemienia własnej warstwy − 2·Ø strzemienia W DÓŁ do 5 (Floor5; 341 → 340):
        ///     odcinek wzdłuż Y (1. warstwa, A = stirrupA: 450 → 365 → 340), wzdłuż X (2. warstwa, A − 15: 350 → 325);
        ///     noga X narożnika L i belka dochodząca złącza T — wewnątrz U-barów 1. warstwy: B(Y) − 2·Ø U-bara (odejmowane 2·Ø w górę do 5, wynik w dół do 5)
        ///     (drop; 0 = auto, H16 → 35: 340 → 305);
        ///   • tył U na otulinie czołowej od lica końca (narożnik L — lica zewnętrznego narożnika, złącze T — dalszego lica belki ciągłej);
        ///   • ramię A = C: koniec — Ceil25(max(zakład + armExtra, tył → pręt prosty + zakład)) (zakład 650 + 90 → 750; C12 740);
        ///     narożnik L / złącze T — Ceil25(tył → początek prętów prostych nogi + zakład) (≈ szerokość drugiej nogi + zakład);
        ///     ograniczone do zakresu prętów prostych odcinka (krótki odcinek — komunikat), ostrzeżenie gdy ramiona z obu końców nachodzą.
        /// Linia w rzucie: od tyłu (kółko na zagięciu) w głąb belki na długość A.
        /// </summary>
        public static List<VUBarRun> PlanVerticalUBars(Result an, LayerPlan straight, int columns, int dMain, int uDia, double stirrupA,
                                                       int dLink, double lap, double armExtra, double drop, double maxLen,
                                                       double cover, double endOffset, bool intoSquare, List<string> msgs,
                                                       bool noSecondLayerDrop = false, Func<Segment, bool, double> endCover = null)
        {
            var res = new List<VUBarRun>();
            if (an == null || columns <= 0 || uDia <= 0) return res;
            double b0 = Floor5(stirrupA - 2 * dLink);                                          // 1. warstwa (wzdłuż Y)
            // GB poza płytą (2026-10-23): bez obniżenia drugiej warstwy — otulina równo 50
            double bX = noSecondLayerDrop ? b0 : Floor5(LinkHeightFor(stirrupA, true) - 2 * dLink);   // 2. warstwa (wzdłuż X)
            double nest = drop > 0 ? drop : Ceil5(2.0 * uDia);
            if (b0 <= 0 || bX <= 0)
            {
                msgs?.Add(string.Format(CultureInfo.InvariantCulture,
                    "U-bary: wysokość U ≤ 0 (A strzemienia {0:0}) — U-bary pominięte.", stirrupA));
                return res;
            }
            var reach = new Dictionary<(Segment, bool), double>();
            foreach (var s in an.Segments.Where(x => !x.Manual))
                foreach (bool atLo in new[] { true, false })
                {
                    var c = atLo ? s.LoClose : s.HiClose;
                    if (c != Closure.UBar && c != Closure.LBar && c != Closure.Tee) continue;
                    string kind = c == Closure.UBar ? "E" : c == Closure.LBar ? "L" : "T";
                    int sE = atLo ? 1 : -1;
                    double face = atLo ? s.FaceLo : s.FaceHi;
                    // otulina czołowa końca (2026-10-28: koniec GB w narożniku w płycie — 45 zamiast 50)
                    double back = face + sE * (endCover != null ? endCover(s, atLo) : cover);
                    (double Lo, double Hi) bars = (double.NaN, double.NaN);
                    bool hasBars = straight != null && straight.Straight.Any(x => x.Seg == s) && straight.Bars.TryGetValue(s, out bars);
                    double start = hasBars ? (atLo ? bars.Lo : bars.Hi) : back;
                    double baseLen = Math.Max(0, sE * (start - back));
                    double A = kind == "E" ? Ceil25(Math.Max(lap + armExtra, baseLen + lap)) : Ceil25(baseLen + lap);
                    // ramię nie dalej niż zakres prętów prostych odcinka (krótki odcinek)
                    var (lo, hi) = StraightRange(s, cover, endOffset, intoSquare);
                    double limit = atLo ? hi : lo;
                    double max = Floor25(sE * (limit - back));
                    if (A > max + 1e-6)
                    {
                        double cut = Math.Max(25.0, max);
                        msgs?.Add(string.Format(CultureInfo.InvariantCulture,
                            "U-bar ({0}): ramię {1:0} przycięte do {2:0} (krótki odcinek) — zakład mniejszy niż zadany.",
                            kind == "E" ? "koniec" : kind == "L" ? "narożnik L" : "złącze T", A, cut));
                        A = cut;
                    }
                    bool dropped = (kind == "L" && s.AlongX) || kind == "T";
                    double B = dropped ? Floor5(b0 - nest) : s.AlongX ? bX : b0;
                    // długość całkowita w module 50 (decyzja Bartka 2026-10-23) — ramię w górę, nie dalej niż zakres prętów prostych
                    A = UBarArmForModule(A, B, uDia, maxArm: Math.Max(A, max));
                    var rows = RowsAcross(s, columns, dMain, dLink, cover);
                    double end = back + sE * A;
                    reach[(s, atLo)] = end;
                    res.Add(new VUBarRun
                    {
                        Kind = kind, Seg = s, AtLo = atLo, Dia = uDia, Count = columns, A = A, B = B, C = A, Dropped = dropped,
                        AlongX = s.AlongX, Across0 = rows[0], Spacing = columns > 1 ? rows[1] - rows[0] : 0,
                        Back = back, LineStart = atLo ? back : back - A, LineLength = A, BendAtStart = atLo,
                        Overlap = hasBars ? sE * (end - start) : double.NaN
                    });
                }

            // Krótki odcinek: ramiona z obu końców nachodzą na siebie — ostrzeżenie
            foreach (var s in an.Segments.Where(x => !x.Manual))
                if (reach.TryGetValue((s, true), out double rLo) && reach.TryGetValue((s, false), out double rHi) && rLo > rHi + 1e-6)
                    msgs?.Add(string.Format(CultureInfo.InvariantCulture,
                        "odcinek {0} ({1:0}..{2:0}): ramiona U-barów z obu końców nachodzą na siebie ({3:0} mm, krótki odcinek) — sprawdź / detal ręczny.",
                        s.AlongX ? "poziomy" : "pionowy", s.FaceLo, s.FaceHi, rLo - rHi));

            foreach (var r in res)
            {
                double len;
                try { len = BarLengthRules.TotalLengthFor("21", new[] { r.A, r.B, r.C }, r.Dia); }
                catch (System.Exception ex) { Log.Error("BeamGeometry.PlanVerticalUBars", ex); continue; }
                if (len > maxLen + 0.5)
                    msgs?.Add(string.Format(CultureInfo.InvariantCulture,
                        "U-bar (kształt 21, A={0:0}, B={1:0}) dłuższy niż {2:0} ({3:0}) — sprawdź.", r.A, r.B, maxLen, len));
            }
            return res;
        }

        /// <summary>
        /// Wysokość strzemienia 51 (K2): A = H − otulina górna (A1 35 / A2 50) − otulina dolna (50), w dół do 5 mm
        /// (A1: 450 → 365, 375 → 290, 300 → 215 — Holbrook; A2: 450 → 350, 600 → 500 — C12).
        /// </summary>
        public static double StirrupHeightCovers(double h, double coverTop = CoverTopA1, double coverBottom = DefaultCoverBottom)
        {
            double a = h - coverTop - coverBottom;
            return a > 0 ? Floor5(a) : 0;
        }

        /// <summary>
        /// Wysokość strzemienia 51 (wymiar zewnętrzny A, 1b.1): H − otulina górą − dT1 (− dT2 przy topLayers = 2) − otulina dołem,
        /// zaokrąglona W DÓŁ do 5 mm. Np. 450, H12 + H12: 450 − 35 − 24 − 50 = 341 → 340 (otulina dolna 50 — decyzja 2026-10-10).
        /// </summary>
        public static double StirrupHeight(double h, int topLayers = 2, double dT1 = 12, double dT2 = 12,
                                           double coverTop = DefaultCoverTop, double coverBottom = DefaultCoverBottom)
        {
            double a = h - coverTop - dT1 - (topLayers >= 2 ? dT2 : 0) - coverBottom;
            return a > 0 ? Math.Floor(a / 5.0 + 1e-9) * 5.0 : 0;
        }

        /// <summary>
        /// Szerokość strzemienia 51 (wymiar zewnętrzny B): szerokość − 2 · otulina boczna, w dół do 5 mm (600 → 510; 600.4 → 510 —
        /// strefa z GA z szumem rysunku nie daje osobnej pozycji).
        /// </summary>
        public static double StirrupWidth(double width, double coverSide = DefaultCoverSide)
        {
            double b = width - 2 * coverSide;
            return b > 0 ? Math.Floor(b / 5.0 + 1e-9) * 5.0 : 0;
        }

        // ── strzemiona 4-cięte (decyzja użytkownika 2026-10-11) ─────────────

        /// <summary>
        /// Strzemiona 4-cięte: dwa identyczne strzemiona 51 zachodzące na siebie — pierwsze obejmuje pręty 1..k, drugie (n − k + 1)..n
        /// (symetrycznie). Reguła: k = round(1 + 2(n − 1)/3) — ramiona wewnętrzne możliwie blisko 1/3 i 2/3 szerokości — z warunkiem
        /// zachodzenia na co najmniej 1 pręt (n − k + 1 ≤ k) i k &lt; n: n = 3 → 2 (1–2, 2–3), 4 → 3 (1–3, 2–4), 5 → 4, 6 → 4 (1–4, 3–6),
        /// 7 → 5, 8 → 6. 0 — 4-cięte niemożliwe (n ≤ 2).
        /// </summary>
        public static int FourLegK(int n)
        {
            if (n < 3) return 0;
            int k = (int)Math.Round(1 + 2.0 * (n - 1) / 3.0, MidpointRounding.AwayFromZero);
            int kMin = (n + 2) / 2;          // ceil((n + 1) / 2): n − k + 1 ≤ k
            if (k < kMin) k = kMin;
            if (k >= n) k = n - 1;
            return k >= kMin ? k : 0;
        }

        /// <summary>
        /// Szerokość zewnętrzna B strzemienia 4-ciętego (wymiar zewnętrzny BS 8666) obejmującego pręty 1..k warstwy n prętów Ø d
        /// (rzędy jak <see cref="BarsAcross"/>: oś pierwszego = otulina + Ø strzemienia + d/2 od lica): oś pręta k − oś pręta 1 + d + 2·Ø
        /// strzemienia, w górę do 5 mm. 600, 4 H16, H12, otulina 45: (470/3)·2 + 16 + 24 = 353,3 → 355. 0 — gdy k = 0.
        /// </summary>
        public static double FourLegWidth(double width, int n, int d, int k, int dLink, double coverSide = DefaultCoverSide)
        {
            if (k <= 0 || n < 2) return 0;
            double off = coverSide + dLink + d / 2.0;
            double span = width - 2 * off;
            if (span <= 0) return 0;
            double sp = span / (n - 1);
            return Math.Ceiling(((k - 1) * sp + d + 2.0 * dLink) / 5.0 - 1e-6) * 5.0;
        }

        /// <summary>
        /// Największy rozstaw poprzeczny ramion (oś–oś) strzemion o szerokości zewnętrznej bOuter (2-cięte) albo dwóch strzemion b4
        /// przy obu licach (4-cięte, b4 &gt; 0): ramiona w osiach dLink/2 od lica zewnętrznego strzemienia.
        /// 2-cięte: bOuter − dLink (510, H12 → 498). 4-cięte 510 / 355: 6, 161, 349, 504 → 188.
        /// </summary>
        public static double LegSpacing(double bOuter, double b4, int dLink)
        {
            double h = dLink / 2.0;
            if (b4 <= 0) return bOuter - dLink;
            var legs = new List<double> { h, b4 - h, bOuter - b4 + h, bOuter - h };
            legs.Sort();
            double max = 0;
            for (int i = 1; i < legs.Count; i++) max = Math.Max(max, legs[i] - legs[i - 1]);
            return max;
        }

        /// <summary>
        /// EN 1992-1-1 §9.2.2(8) (UK NA — wartość zalecana): największy rozstaw poprzeczny ramion strzemion s_t,max = 0,75·d ≤ 600 mm;
        /// d = H − otulina dolna − Ø strzemienia − Ø/2 pręta głównego dołem. 600 × 600 (50, H12, H16): d = 530, s_t,max = 397,5.
        /// </summary>
        public static (double D, double StMax) LinkLegLimit(double h, double coverBottom, int dLink, int dMain)
        {
            double d = h - coverBottom - dLink - dMain / 2.0;
            return (d, Math.Max(0, Math.Min(0.75 * d, 600.0)));
        }

        /// <summary>Wynik wyboru cięcia strzemion odcinka belki (<see cref="PlanLinkLegs"/>).</summary>
        public sealed class LinkLegsPlan
        {
            /// <summary>Wybrane cięcie: 2 albo 4.</summary>
            public int Legs = 2;
            /// <summary>Tryb z rekordu [37] (0 Auto / 2 / 4).</summary>
            public int Mode;
            /// <summary>Belka krawędziowa (OuterSide ≠ 0) — próg normy w trybie Auto; belka w płycie — Auto zawsze 2-cięte.</summary>
            public bool EdgeBeam = true;
            public double D, StMax;
            /// <summary>B strzemienia 2-ciętego (zewnętrzne) i rozstaw jego ramion (B − Ø strzemienia).</summary>
            public double BOuter, Spacing2;
            /// <summary>4-cięte: k (pręty 1..k i n−k+1..n warstwy dolnej), B jednego strzemienia, największy rozstaw ramion; K = 0 — niemożliwe.</summary>
            public int K, N;
            public double B4, Spacing4;
            /// <summary>Rozstaw ramion wybranego cięcia &gt; s_t,max — ostrzeżenie (nie blokada).</summary>
            public bool Exceeded;
            /// <summary>Komunikat do linii poleceń (bez prefiksu).</summary>
            public string Note;
            /// <summary>B strzemienia (szablonu): 2-cięte — BOuter, 4-cięte — B4.</summary>
            public double TemplateB => Legs == 4 ? B4 : BOuter;
        }

        /// <summary>
        /// Cięcie strzemion odcinka belki szerokości width i wysokości h (decyzja użytkownika 2026-10-11, punkt C): s_t,max z
        /// <see cref="LinkLegLimit"/>; 2-cięte — rozstaw ramion B − Ø strzemienia; Auto: &gt; s_t,max → 4-cięte (rozstaw ramion 4-ciętych też
        /// sprawdzany — ostrzeżenie, gdy nadal &gt; s_t,max). Ręczne 2-cięte przy przekroczeniu — ostrzeżenie; ręczne 4-cięte przy n ≤ 2 —
        /// 2-cięte z komunikatem. layers — (liczba, Ø) prętów warstw (dół pierwszy): B 4-ciętego obejmuje pręty 1..k każdej warstwy (max).
        /// Doprecyzowanie użytkownika 2026-10-11: próg normy w trybie Auto tylko dla belek krawędziowych (edgeBeam — OuterSide ≠ 0); belka
        /// w płycie — Auto zawsze 2-cięte (komunikat informacyjny, bez ostrzeżenia); ręczne 2 / 4 nadpisuje Auto (ostrzeżenie tylko krawędziowej).
        /// </summary>
        /// <summary>Belka niska (decyzja użytkownika 2026-10-12): H ≤ 375 — dwa zachodzące strzemiona (Auto) i pytanie H12 / H16 przy generowaniu.</summary>
        public const double ShallowBeamMaxH = 375.0;
        /// <summary>Strzemiona zachodzące (4-cięte): szerokość zewnętrzna B jednego strzemienia — sztywno 370 (decyzja użytkownika 2026-10-12).</summary>
        public const double FourLegFixedB = 370.0;

        public static LinkLegsPlan PlanLinkLegs(int mode, double h, double width, double coverSide, double coverBottom, int dLink,
                                                IList<(int n, int d)> layers, bool edgeBeam = true)
        {
            var r = new LinkLegsPlan { Mode = BeamParams.NormalizeLegs(mode), EdgeBeam = edgeBeam };
            int dB = layers != null && layers.Count > 0 ? layers[0].d : 16;
            (r.D, r.StMax) = LinkLegLimit(h, coverBottom, dLink, dB);
            r.BOuter = StirrupWidth(width, coverSide);
            r.Spacing2 = LegSpacing(r.BOuter, 0, dLink);
            if (layers != null && layers.Count > 0)
            {
                r.N = layers[0].n;
                r.K = FourLegK(r.N);
                double b4 = 0;
                foreach (var (n, d) in layers)
                {
                    int k = FourLegK(n);
                    if (k <= 0) { b4 = 0; r.K = 0; break; }
                    b4 = Math.Max(b4, FourLegWidth(width, n, d, k, dLink, coverSide));
                }
                // decyzja użytkownika 2026-10-12: strzemiona zachodzące (4-cięte) — szerokość jednego strzemienia sztywno 370
                // (Holbrook; wcześniej obejmujące pręty 1..k: 355); belka węższa, w której 370 się nie mieści — B jak dotąd (pręty 1..k)
                if (b4 > 0 && FourLegFixedB < r.BOuter - 2.0 * dLink) b4 = Math.Max(b4, FourLegFixedB);
                if (r.K > 0 && b4 > 0 && b4 < r.BOuter - 2.0 * dLink)
                {
                    r.B4 = b4;
                    r.Spacing4 = LegSpacing(r.BOuter, b4, dLink);
                }
                else r.K = 0;
            }
            string F(double x) => x.ToString("0.#", CultureInfo.InvariantCulture);
            string head = $"{F(width)}×{F(h)}: d = {F(r.D)}, s_t,max = 0,75·d = {F(r.StMax)} (EN 1992-1-1 §9.2.2(8)), rozstaw ramion 2-ciętych {F(r.Spacing2)}";
            bool over2 = r.Spacing2 > r.StMax + 1e-6;
            // decyzja użytkownika 2026-10-12: belki niskie (H ≤ 375) — Auto zawsze dwa zachodzące strzemiona (4-cięte, Holbrook)
            bool shallow = r.Mode == BeamParams.LegsAuto && h <= ShallowBeamMaxH + 0.5;
            // doprecyzowanie użytkownika 2026-10-11: belka w płycie (OuterSide = 0) — Auto zawsze 2-cięte, bez ostrzeżenia normowego
            if (!edgeBeam && r.Mode != BeamParams.Legs4 && !shallow)
            {
                r.Legs = 2;
                r.Note = $"{F(width)}×{F(h)}: belka w płycie — 2-cięte" + (r.Mode == BeamParams.Legs2 ? " (wybór ręczny)." : " (Auto: próg normy tylko dla belek krawędziowych).");
                return r;
            }
            if (r.Mode == BeamParams.Legs2)
            {
                r.Legs = 2;
                r.Exceeded = over2;
                r.Note = head + (over2 ? $" > {F(r.StMax)} — wybrano 2-cięte ręcznie: *** OSTRZEŻENIE *** rozstaw ramion przekracza s_t,max (rozważ 4-cięte)."
                                       : $" ≤ {F(r.StMax)} → 2-cięte (wybór ręczny).");
                return r;
            }
            bool manual4 = r.Mode == BeamParams.Legs4;
            if (!edgeBeam) head = $"{F(width)}×{F(h)}: belka w płycie";
            // decyzja użytkownika 2026-10-12: Auto — 4-cięte TYLKO belki niskie (H ≤ 375); wyższe standardowo 2-cięte (przekroczenie normy —
            // tylko ostrzeżenie)
            bool want4 = manual4 || shallow;
            string how = manual4 ? "; 4-cięte (wybór ręczny)"
                       : shallow ? $"; belka niska (H ≤ {F(ShallowBeamMaxH)}) → 2 zachodzące strzemiona"
                       : over2 ? $" > {F(r.StMax)} → 2-cięte (Auto: zachodzące tylko dla H ≤ {F(ShallowBeamMaxH)})"
                       : $" ≤ {F(r.StMax)} → 2-cięte (Auto)";
            if (!want4)
            {
                r.Legs = 2;
                r.Exceeded = over2 && edgeBeam;
                r.Note = head + how + (r.Exceeded ? $" — *** OSTRZEŻENIE *** rozstaw ramion przekracza s_t,max (EN 1992-1-1 §9.2.2(8)); w razie potrzeby 4-cięte ręcznie w RC_BEAM." : ".");
                return r;
            }
            if (r.K <= 0)
            {
                r.Legs = 2;
                r.Exceeded = over2 && edgeBeam;
                r.Note = head + how + $" niemożliwe ({r.N} pręty w warstwie — potrzeba ≥ 3) — 2-cięte"
                         + (r.Exceeded ? ": *** OSTRZEŻENIE *** rozstaw ramion przekracza s_t,max." : ".");
                return r;
            }
            r.Legs = 4;
            r.Exceeded = edgeBeam && r.Spacing4 > r.StMax + 1e-6;
            string kk = r.K.ToString(CultureInfo.InvariantCulture), n2 = (r.N - r.K + 1).ToString(CultureInfo.InvariantCulture),
                   nn = r.N.ToString(CultureInfo.InvariantCulture);
            r.Note = head + how
                     + $" (2 × strzemię 51 B = {F(r.B4)} obejmujące pręty 1–{kk} i {n2}–{nn}, jedna pozycja; rozstaw ramion {F(r.Spacing4)}"
                     + (!edgeBeam ? ")." : r.Exceeded ? $" > {F(r.StMax)} — *** OSTRZEŻENIE *** nadal przekracza s_t,max)." : $" ≤ {F(r.StMax)}).");
            return r;
        }

        /// <summary>
        /// Pręty podłużne w poprzek belki: pierwszy / ostatni w osi otulina + strzemię + d/2 od lica, liczba prętów równo
        /// (600, otulina 45, H10, H16: 63 … 537, co 158). Zwraca (pierwszy, rozstaw).
        /// </summary>
        public static (double First, double Spacing) BarsAcross(double lo, double hi, int count, double d,
                                                                double dLink = 10, double coverSide = DefaultCoverSide)
        {
            double off = coverSide + dLink + d / 2.0;
            double a = lo + off, b = hi - off;
            if (count <= 1 || b <= a) return ((lo + hi) / 2, 0);
            return (a, (b - a) / (count - 1));
        }

        // ── E4b: pręty boczne i pręty w nibie belki (domyślne, do potwierdzenia przez Bartka) ──

        /// <summary>
        /// Liczba prętów bocznych na bok: odległość OSI prętów dołu i góry (wewnątrz strzemienia o wysokości zewnętrznej A:
        /// A − 2·dLink − (dB + dT)/2) / rozstaw w górę, minus 1, min. 0 — rozstaw oś–oś ≤ spacing (poprawka recenzji; wcześniej
        /// od światła — H 750: 3 odstępy po ~205). Np. H 600 (A 500): 464 / 200 → 3 − 1 = 2; 750 (A 650): 614 → 3; 450 (A 350): 1;
        /// 375: 1; 300: 0.
        /// </summary>
        /// <summary>
        /// Liczba prętów bocznych na bok (= poziomów UB SIDE) z wysokości belki (decyzja użytkownika 2026-10-14, tryb B3): H ≤ 375 — brak,
        /// 450–525 — 1, 600–675 — 2, 750–825 — 3, 900–975 — 4 i tak dalej co 150 (⌊(H − 300) / 150⌋).
        /// </summary>
        public static int SideLevelsForHeight(double h)
            => h <= 375.5 ? 0 : Math.Max(0, (int)Math.Floor((h - 300.0) / 150.0 + 1e-9));

        /// <summary>Rozstaw w opisie UB SIDE — wpisany na stałe (decyzja użytkownika 2026-10-14: „rozstaw z ręki — 150”).</summary>
        public const double UbSideLabelSpacing = 150.0;

        /// <summary>Rozstaw pionowy prętów bocznych (opis UB SIDE): osie dołu / góry podzielone na n + 1 równych części, w dół do 5 mm.</summary>
        public static double SideLevelSpacing(double stirrupA, double dLink, double dBottom, double dTop, int n)
        {
            double axes = stirrupA - 2 * dLink - (dBottom + dTop) / 2.0;
            return n > 0 && axes > 0 ? Floor5(axes / (n + 1)) : 0;
        }

        public static int SideBarCount(double stirrupA, double dLink, double dBottom, double dTop, double spacing)
        {
            if (stirrupA <= 0 || spacing <= 0) return 0;
            double axes = stirrupA - 2 * dLink - (dBottom + dTop) / 2.0;
            if (axes <= 0) return 0;
            return Math.Max(0, (int)Math.Ceiling(axes / spacing - 1e-9) - 1);
        }

        /// <summary>
        /// K4: poziomy U-bar „UB SIDE” (kształt 21 w rzucie) jednego końca odcinka — zamyka pręty boczne obu boków na jednym poziomie;
        /// jeden na poziom (Levels — liczba prętów bocznych na bok). W rzucie: ramię przy jednym boku (oś Across0) od tyłu U w głąb
        /// belki na długość A, hak na zagięciu w stronę drugiego ramienia (pełny U w rzucie — później).
        /// </summary>
        public sealed class SideUBarRun
        {
            public string Kind;
            public Segment Seg;
            public bool AtLo;
            public int Dia, Levels;
            /// <summary>Wymiary zewnętrzne: A = C ramiona, B — rozstaw zewnętrzny ramion (B strzemienia − 2·Ø strzemienia).</summary>
            public double A, B, C;
            public bool AlongX;
            /// <summary>Osie ramion w poprzek (pręty boczne obu boków).</summary>
            public double Across0, Across1;
            public double Back, LineStart, LineLength;
            public bool BendAtStart;

            public override string ToString()
                => string.Format(CultureInfo.InvariantCulture, "{0} 21 H{1} A={2:0} B={3:0} C={4:0} ×{5}", Kind, Dia, A, B, C, Levels);
        }

        /// <summary>
        /// K4 (praktyka biura C12; domyślne do potwierdzenia — pytanie 9): U-bary „UB SIDE” na każdym końcu z zamknięciem UBar / LBar /
        /// Tee (jak pionowe U-bary): tył na otulinie od lica końca (narożnik — lica zewnętrznego narożnika, T — dalszego lica belki
        /// ciągłej), B = Floor5(B strzemienia − 2·Ø strzemienia) (510 → 485; C12 480), A = C = Ceil25(tył → początek prętów bocznych
        /// + zakład) (koniec 525, narożnik ≈ szerokość drugiej nogi + zakład), przycięte do zakresu prętów bocznych odcinka (komunikat).
        /// sideBars — zakres prętów bocznych odcinka (początek pierwszego, koniec ostatniego pręta).
        /// </summary>
        public static List<SideUBarRun> PlanSideUBars(Result an, IDictionary<Segment, (double Lo, double Hi)> sideBars, int levels,
                                                      int dSide, int dLink, double lap, double cover, List<string> msgs,
                                                      Func<Segment, bool, double> backOffset = null)
        {
            var res = new List<SideUBarRun>();
            if (an == null || levels <= 0 || dSide <= 0) return res;
            foreach (var s in an.Segments.Where(x => !x.Manual))
            {
                if (sideBars == null || !sideBars.TryGetValue(s, out var bars)) continue;
                double b = Floor5(StirrupWidth(s.Width, cover) - 2 * dLink);
                if (b <= 0) continue;
                var (c0, c1) = SideBarsAcross(s, dSide, dLink, cover);
                foreach (bool atLo in new[] { true, false })
                {
                    var c = atLo ? s.LoClose : s.HiClose;
                    if (c != Closure.UBar && c != Closure.LBar && c != Closure.Tee) continue;
                    string kind = c == Closure.UBar ? "E" : c == Closure.LBar ? "L" : "T";
                    int sE = atLo ? 1 : -1;
                    // tył od lica: otulina; backOffset (2026-10-28, PLOT 344-346) — W ŚRODKU tyłu pionowego U-bara (otulina + Ø U-bara)
                    double back = (atLo ? s.FaceLo : s.FaceHi) + sE * (backOffset != null ? backOffset(s, atLo) : cover);
                    double start = atLo ? bars.Lo : bars.Hi;
                    double a = Ceil25(Math.Max(0, sE * (start - back)) + lap);
                    double max = Floor25(sE * ((atLo ? bars.Hi : bars.Lo) - back));
                    if (a > max + 1e-6)
                    {
                        double cut = Math.Max(25.0, max);
                        msgs?.Add(string.Format(CultureInfo.InvariantCulture,
                            "UB SIDE ({0}): ramię {1:0} przycięte do {2:0} (krótki odcinek) — zakład mniejszy niż zadany.",
                            kind == "E" ? "koniec" : kind == "L" ? "narożnik L" : "złącze T", a, cut));
                        a = cut;
                    }
                    res.Add(new SideUBarRun
                    {
                        Kind = kind, Seg = s, AtLo = atLo, Dia = dSide, Levels = levels, A = a, B = b, C = a, AlongX = s.AlongX,
                        Across0 = c0, Across1 = c1, Back = back, LineStart = atLo ? back : back - a, LineLength = a, BendAtStart = atLo
                    });
                }
            }
            return res;
        }

        /// <summary>Osie prętów bocznych w poprzek (przy ramionach strzemienia): otulina + strzemię + d/2 od każdego lica.</summary>
        public static (double Lo, double Hi) SideBarsAcross(Segment s, double d, double dLink = 10, double cover = DefaultCoverSide)
            => (s.AcrossLo + cover + dLink + d / 2.0, s.AcrossHi - cover - dLink - d / 2.0);

        /// <summary>Zakład pręta o średnicy d przy zakładzie lap prętów głównych średnicy dMain — ta sama krotność średnicy, w górę do 25 (800 / 16 → H10: 500).</summary>
        public static double LapFor(double lap, int dMain, int d) => dMain > 0 && d > 0 ? Ceil25(lap * d / dMain) : lap;

        /// <summary>
        /// Pręt wzdłuż nibu belki (E4b): oś w poprzek — w narożu spinki UB 03 (krawędź płyty − coverAcross − Ø spinki − d/2; UB 03 to
        /// element płyty — otulina 40 bez zmian), zakres wzdłuż — koniec przy krawędzi i narożnik L (nib zawinięty) do lica płyty na końcu
        /// z otuliną czołową belki (cover, 45), koniec wewnętrzny do lica belki z otuliną, złącze T — lico własnego zakresu. Null, gdy
        /// odcinek nie ma lica przy krawędzi płyty (nib ≤ EdgeEndMaxGap).
        /// </summary>
        public static (double Across, double Lo, double Hi)? NibBarLine(Segment s, IList<(double X, double Y)> outline, double d,
                                                                       double dUb = 10, double cover = DefaultCoverSide,
                                                                       double coverAcross = NibUbEdgeCover)
        {
            if (s == null || s.Manual || s.OuterSide == 0 || outline == null || outline.Count < 3) return null;
            var poly = outline.ToList();
            double mid = (s.OwnLo + s.OwnHi) / 2;
            double face = s.OuterSide < 0 ? s.AcrossLo : s.AcrossHi;
            int sg = s.OuterSide < 0 ? -1 : 1;
            var p = s.AlongX ? (mid, face) : (face, mid);
            var v = s.AlongX ? (0.0, (double)sg) : ((double)sg, 0.0);
            double gap = RayHit(poly, p, v);
            if (double.IsInfinity(gap) || gap > EdgeEndMaxGap || gap < 1.0) return null;
            double across = face + sg * gap - sg * (coverAcross + dUb + d / 2.0);
            double End(bool atLo)
            {
                var kind = atLo ? s.LoKind : s.HiKind;
                double own = atLo ? s.OwnLo : s.OwnHi;
                int dir = atLo ? -1 : 1;
                if (kind == EndKind.Edge || kind == EndKind.Corner)
                {
                    var q = s.AlongX ? (own, across) : (across, own);
                    var w = s.AlongX ? ((double)dir, 0.0) : (0.0, (double)dir);
                    double h = RayHit(poly, q, w);
                    if (!double.IsInfinity(h)) return own + dir * (h - cover);
                }
                if (kind == EndKind.Tee) return own;
                return (atLo ? s.FaceLo : s.FaceHi) - dir * cover;
            }
            double lo = End(true), hi = End(false);
            return hi - lo > 100 ? (across, lo, hi) : ((double, double, double)?)null;
        }

        /// <summary>Pręty w nibie jedną pozycją: rozstaw osi dwóch prętów górą — jak T IN NIB płyty (decyzja użytkownika 2026-10-12
        /// „zróbmy tak jak już rozkładamy T IN NIB — czyli co 150 w płytę”; opis „2+1 H10-nn-150 T+B IN NIB”).</summary>
        public const double NibPairSpacing = 150.0;

        /// <summary>
        /// Pręty w nibie jedną pozycją (decyzja użytkownika 2026-10-11 „jedna pozycja bottom in nib i top — 3 pręty te same”): osie
        /// w poprzek dwóch prętów górą (w rzucie rysowane 2 pręty, opis / BBS 3). <paramref name="across1"/> — oś pręta w narożu spinki UB 03
        /// (<see cref="NibBarLine"/>; pod nim, w dolnym narożu, trzeci pręt dołem); drugi pręt górą <see cref="NibPairSpacing"/> (150) dalej
        /// w płytę — jak drugi pręt T IN NIB (decyzja użytkownika 2026-10-12; wcześniej przy licu zewnętrznym belki − 40).
        /// Wynik: Outer — pręt w narożu (z prętem dołem), Inner — pręt 150 dalej w płytę.
        /// </summary>
        public static (double Outer, double Inner) NibPairAcross(Segment s, double across1)
        {
            int sg = s.OuterSide < 0 ? -1 : 1;                           // kierunek od belki do krawędzi płyty
            return (across1, across1 - sg * NibPairSpacing);
        }

        // ── K7 (E2): siatka płyty przy belce (docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md; domyślne, do potwierdzenia — pytanie 11) ──

        /// <summary>Zasięg odcięcia poza lico belki (nib przed belką / nib zawinięty na końcu): nib ≤ EdgeEndMaxGap + 5.</summary>
        public const double MeshCutReach = EdgeEndMaxGap + 5.0;

        /// <summary>
        /// Otulina boczna belki dla końców prętów siatki płyty PROSTOPADŁYCH do belki krawędziowej (decyzja użytkownika 2026-10-11 „pręty
        /// prostopadłe płyty powinny wchodzić w belkę jak normalnie do 40mm side cover”): pręt B / T przechodzi przez belkę i kończy się
        /// 40 mm od lica zewnętrznego belki. Element siatki płyty — inna wartość niż otulina boczna strzemion belki (<see cref="DefaultCoverSide"/> 45).
        /// </summary>
        public const double MeshBeamSideCover = 40.0;

        /// <summary>
        /// Prostokąty odejmowane od wielokąta siatki płyty (E2) dla warstwy dolnej (top = false, wielokąt = obrys zewnętrzny) albo
        /// górnej (top = true, wielokąt = linia uskoku), pręty wzdłuż X (barsAlongX) albo Y; każdy odcinek osiowy belki (odcinki
        /// „detal ręczny” — bez odcięcia, siatka jak bez belki):
        ///   • pręty RÓWNOLEGŁE do odcinka — dół: od strony krawędzi płyty (nib przed belką) do lica wewnętrznego + d (pierwszy pręt
        ///     40 + d od lica), góra: tak samo (decyzja 2026-10-14; dawniej s/2 od lica); belka bez krawędzi płyty
        ///     (OuterSide = 0) — oba lica;
        ///   • pręty PROSTOPADŁE (decyzja użytkownika 2026-10-11 „pręty prostopadłe płyty powinny wchodzić w belkę jak normalnie do
        ///     40 mm side cover” — zastępuje decyzję z 60e8a9c „do lica wewnętrznego”) — dół i góra: przez belkę, koniec
        ///     <see cref="MeshBeamSideCover"/> (40) od LICA ZEWNĘTRZNEGO belki (strona krawędzi płyty): odjęty tylko nib przed belką,
        ///     granica odcięcia = lico zewnętrzne ± (40 − otulina siatki), bo pas daje koniec pręta na granicy pasa ± otulina. Lico
        ///     zewnętrzne ogranicza pręt na obu poziomach: przed nim jest tylko nib belki przy jej spodzie (profil RC_SECTION), a górą
        ///     linia uskoku (wielokąt siatki górnej) pokrywa się z licem zewnętrznym (Holbrook 528 / 539) — góra wychodzi jak dawniej
        ///     (do linii uskoku − 40), dół jak K7 przed 60e8a9c (do linii uskoku − 40); belka bez krawędzi płyty (OuterSide = 0) — bez
        ///     odcięcia (pręty przez belkę).
        /// Wzdłuż: lica końców odcinka (FaceLo..FaceHi), na końcu przy krawędzi i w narożniku L dalej o <see cref="MeshCutReach"/>
        /// (nib zawinięty / nib narożnika). d — średnica prętów warstwy, spacing — rozstaw nominalny, cover — otulina siatki.
        /// </summary>
        /// <summary>Zakotwienie pręta siatki w belce na jej końcu wewnętrznym (decyzja użytkownika 2026-10-13: H10 450–550,
        /// H12 550–700): 40·Ø w górę do 50 + 50 (H10 450, H12 550, H16 700).</summary>
        public static double MeshAnchorIntoBeam(double d) => Math.Ceiling(40.0 * d / 50.0 - 1e-9) * 50.0 + 50.0;

        public static List<RectilinearPolygons.Rect> MeshCutRects(Result an, bool top, bool barsAlongX, double d,
                                                                  double spacing = 200, double cover = 40)
        {
            var res = new List<RectilinearPolygons.Rect>();
            if (an == null) return res;
            foreach (var s in an.Segments)
            {
                if (s.Manual) continue;
                double eLo = s.FaceLo - (s.LoKind == EndKind.Edge || s.LoKind == EndKind.Corner ? MeshCutReach : 0);
                double eHi = s.FaceHi + (s.HiKind == EndKind.Edge || s.HiKind == EndKind.Corner ? MeshCutReach : 0);
                double cLo, cHi;
                if (s.AlongX == barsAlongX)
                {
                    // 2026-10-13 (uwaga Bartka): koniec wewnętrzny belki (belka kończy się w płycie) — pręty siatki równoległe do belki,
                    // dochodzące do jej końca, kotwią się W BELCE na MeshAnchorIntoBeam (H10 450, H12 550) — odcięcie pasa krótsze
                    double anch = MeshAnchorIntoBeam(d) + cover;
                    if (s.LoKind == EndKind.Internal) eLo = s.FaceLo + anch;
                    if (s.HiKind == EndKind.Internal) eHi = s.FaceHi - anch;
                    if (eHi - eLo < 1.0) continue;
                    // decyzja 2026-10-14: góra jak dół — pierwszy pręt 40 + d od lica wewnętrznego (siatka górna i dolna w jednej linii,
                    // między belkami ta sama liczba prętów; dawniej góra s/2 od lica)
                    double off = d;
                    if (s.OuterSide < 0) { cLo = s.AcrossLo - MeshCutReach; cHi = s.AcrossHi + off; }
                    else if (s.OuterSide > 0) { cLo = s.AcrossLo - off; cHi = s.AcrossHi + MeshCutReach; }
                    else { cLo = s.AcrossLo - off; cHi = s.AcrossHi + off; }
                }
                else
                {
                    if (s.OuterSide == 0) continue;
                    // koniec pręta 40 od lica zewnętrznego (w belce): granica pasu = lico zewnętrzne ± (40 − otulina) (pas: pręt od granicy
                    // + otulina); przy otulinie siatki 40 — dokładnie lico zewnętrzne (= linia uskoku, jak K7 przed 60e8a9c)
                    double inBeam = MeshBeamSideCover - cover;
                    if (s.OuterSide < 0) { cLo = s.AcrossLo - MeshCutReach; cHi = s.AcrossLo + inBeam; }
                    else { cLo = s.AcrossHi - inBeam; cHi = s.AcrossHi + MeshCutReach; }
                }
                res.Add(s.AlongX ? new RectilinearPolygons.Rect(eLo, cLo, eHi, cHi) : new RectilinearPolygons.Rect(cLo, eLo, cHi, eHi));
            }
            return res;
        }

        /// <summary>
        /// E2: obszary krawędzi „przy belce” — krawędź obrysu (uskoku) leżąca w nich nie dostaje UB 01/02 (koniec belki zamykają
        /// pionowe U-bary BU, pręty siatki prostopadłe kończą się w belce): odcinek osiowy belki (lica boczne, lica końców) rozszerzony
        /// o <see cref="MeshCutReach"/> po stronie krawędzi płyty (nib przed belką) i na końcach przy krawędzi / w narożniku L
        /// (nib zawinięty). Odcinki „detal ręczny” — bez zmian (UB jak bez belki).
        /// </summary>
        public static List<RectilinearPolygons.Rect> UbExclusionRects(Result an)
        {
            var res = new List<RectilinearPolygons.Rect>();
            if (an == null) return res;
            foreach (var s in an.Segments)
            {
                if (s.Manual) continue;
                double eLo = s.FaceLo - (s.LoKind == EndKind.Edge || s.LoKind == EndKind.Corner ? MeshCutReach : 0);
                double eHi = s.FaceHi + (s.HiKind == EndKind.Edge || s.HiKind == EndKind.Corner ? MeshCutReach : 0);
                double cLo = s.AcrossLo - (s.OuterSide < 0 ? MeshCutReach : 0);
                double cHi = s.AcrossHi + (s.OuterSide > 0 ? MeshCutReach : 0);
                res.Add(s.AlongX ? new RectilinearPolygons.Rect(eLo, cLo, eHi, cHi) : new RectilinearPolygons.Rect(cLo, eLo, cHi, eHi));
            }
            return res;
        }

        // ── Nib przy belce — opcja okna RC_BEAM, rekord [38] (decyzja użytkownika 2026-10-11 „opcja dodania lub nie dodawania nibu”) ──

        /// <summary>
        /// Odległość lica zewnętrznego odcinka (strona <see cref="Segment.OuterSide"/>) od obrysu płyty w połowie długości odcinka —
        /// szerokość nibu przed belką (0: odcinek bez lica przy krawędzi albo lico na obrysie). Jak <see cref="NibBarLine"/>.
        /// </summary>
        public static double OuterGap(Segment s, IList<(double X, double Y)> outline)
        {
            if (s == null || s.OuterSide == 0 || outline == null || outline.Count < 3) return 0;
            double mid = (s.OwnLo + s.OwnHi) / 2;
            double face = s.OuterSide < 0 ? s.AcrossLo : s.AcrossHi;
            var p = s.AlongX ? (mid, face) : (face, mid);
            var v = s.AlongX ? (0.0, (double)s.OuterSide) : ((double)s.OuterSide, 0.0);
            double g = RayHit(outline.ToList(), p, v);
            return double.IsInfinity(g) ? 0 : g;
        }

        /// <summary>
        /// Geometria strefy ma nib (domyślne pola „Nib przy belce” w oknie RC_BEAM): odcinek osiowy z licem zewnętrznym w odległości
        /// 1 … <see cref="EdgeEndMaxGap"/> od obrysu płyty (kryterium jak <see cref="NibBarLine"/> — pręty w nibie). Strefa przy samej krawędzi
        /// (RC-BEAM-MANUAL bez linii uskoku) albo belka w płycie — false.
        /// </summary>
        public static bool HasNibGeometry(Result an, IList<(double X, double Y)> outline)
            => an != null && an.Segments.Any(s => !s.Manual && OuterGap(s, outline) is double g && g >= 1.0 && g <= EdgeEndMaxGap);

        /// <summary>
        /// Strefa belki BEZ nibu (rekord [38] — <see cref="BeamParams.NoNib"/>; decyzja użytkownika 2026-10-11 „zostaw szerokość 600, chyba że
        /// będzie inaczej na GA”): każda krawędź osiowa strefy, której normalna zewnętrzna trafia w obrys płyty w odległości 1 … <see cref="EdgeEndMaxGap"/>
        /// (lico zewnętrzne przed nibem, lico końca przy nibie zawiniętym), przesunięta do obrysu — lico zewnętrzne belki = krawędź płyty.
        /// Lico boczne PRZESUWA całą nogę: lico wewnętrzne tej nogi (krawędź równoległa, zwrócona przeciwnie, w odległości szerokości
        /// <see cref="MinWidth"/> … <see cref="MaxWidth"/>, zakresy nachodzą) przesuwa się o to samo — szerokość z GA bez zmian (528: 600 przy
        /// krawędzi). Lico końca przy nibie zawiniętym — koniec belki do krawędzi płyty (bez pary: przeciwny koniec dalej niż szerokość). Krawędź
        /// z różną odległością na długości (obrys z uskokiem) — bez zmian + komunikat w <paramref name="msgs"/>. Strefa bez takich krawędzi —
        /// kopia wejścia (geometria bez zmian).
        /// </summary>
        public static List<(double X, double Y)> ZoneWithoutNib(IList<(double X, double Y)> zone, IList<(double X, double Y)> outline,
                                                                List<string> msgs = null)
        {
            var poly = Clean(zone);
            if (poly.Count < 4 || outline == null || outline.Count < 3) return zone.ToList();
            var outl = outline.ToList();
            bool ccw = SignedArea(poly) > 0;
            int n = poly.Count;
            // krawędzie osiowe: orientacja, współrzędna, zakres, normalna zewnętrzna
            var hor = new bool[n]; var ax = new bool[n]; var c = new double[n]; var lo = new double[n]; var hi = new double[n]; var sg = new int[n];
            var move = new double?[n];
            for (int i = 0; i < n; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % n];
                double dx = b.X - a.X, dy = b.Y - a.Y;
                bool h = Math.Abs(dy) <= AxisTol && Math.Abs(dx) > AxisTol;
                bool v = Math.Abs(dx) <= AxisTol && Math.Abs(dy) > AxisTol;
                if (!h && !v) continue;
                ax[i] = true; hor[i] = h;
                c[i] = h ? (a.Y + b.Y) / 2 : (a.X + b.X) / 2;
                lo[i] = h ? Math.Min(a.X, b.X) : Math.Min(a.Y, b.Y);
                hi[i] = h ? Math.Max(a.X, b.X) : Math.Max(a.Y, b.Y);
                // normalna zewnętrzna (CCW: wnętrze po lewej, na zewnątrz po prawej), osiowa
                sg[i] = v ? Math.Sign(dy) * (ccw ? 1 : -1) : -Math.Sign(dx) * (ccw ? 1 : -1);
                var dir = h ? (0.0, (double)sg[i]) : ((double)sg[i], 0.0);
                var gaps = new[] { 0.25, 0.5, 0.75 }.Select(t => RayHit(outl, (a.X + t * dx, a.Y + t * dy), dir)).ToList();
                double g = gaps[1];
                if (double.IsInfinity(g) || g < 1.0 || g > EdgeEndMaxGap) continue;
                if (gaps.Any(x => double.IsInfinity(x) || Math.Abs(x - g) > 1.0))
                {
                    msgs?.Add(string.Format(CultureInfo.InvariantCulture,
                        "belka bez nibu: lico ({0:0},{1:0})–({2:0},{3:0}) w różnej odległości od krawędzi płyty ({4}) — bez zmian, sprawdź.",
                        a.X, a.Y, b.X, b.Y, string.Join(" / ", gaps.Select(x => double.IsInfinity(x) ? "∞" : x.ToString("0", CultureInfo.InvariantCulture)))));
                    continue;
                }
                move[i] = sg[i] * g;
            }
            if (move.All(m => m == null)) return zone.ToList();
            // lico wewnętrzne nogi — przesunięcie razem z licem zewnętrznym (szerokość z GA bez zmian)
            var outer = Enumerable.Range(0, n).Where(i => move[i] != null).ToList();
            foreach (int i in outer)
            {
                int best = -1; double bestD = double.MaxValue;
                for (int j = 0; j < n; j++)
                {
                    if (j == i || !ax[j] || hor[j] != hor[i] || sg[j] != -sg[i] || move[j] != null) continue;
                    double d = (c[i] - c[j]) * sg[i];
                    if (d < MinWidth - JoinTol || d > MaxWidth + JoinTol) continue;
                    if (Math.Min(hi[i], hi[j]) - Math.Max(lo[i], lo[j]) <= 1.0) continue;
                    if (hi[i] - lo[i] < d) continue;             // lico końca (krótsze niż szerokość) — koniec belki, bez pary
                    if (d < bestD) { bestD = d; best = j; }
                }
                if (best >= 0) move[best] = move[i];
            }
            var xs = poly.Select(p => p.X).ToArray();
            var ys = poly.Select(p => p.Y).ToArray();
            for (int i = 0; i < n; i++)
            {
                if (move[i] == null) continue;
                double nc = c[i] + move[i].Value;
                if (hor[i]) ys[i] = ys[(i + 1) % n] = nc; else xs[i] = xs[(i + 1) % n] = nc;
            }
            return Clean(Enumerable.Range(0, n).Select(i => (xs[i], ys[i])).ToList());
        }

        /// <summary>Krawędź nibu płyty (kopia NibDetector.NibEdge bez Teigha): krawędź zewnętrzna, linia uskoku, zakres wzdłuż, strona wnętrza.</summary>
        public sealed class NibSpan
        {
            public bool Vertical;
            public double OuterCoord, InnerCoord, Lo, Hi;
            public int InwardSign;
        }

        /// <summary>
        /// Krawędzie nibu płyty bez części przy belkach BEZ nibu (rekord [38]): zakres krawędzi minus pokrycie pasa nibu (prosta w połowie
        /// szerokości nibu) przez pętle <paramref name="beamLoops"/> (suma <see cref="UbExclusionRects"/> tych belek — kryterium „krawędź
        /// przy belce” jak UB 01/02, T IN NIB, UB 03 w detalu). Kawałki krótsze niż 1 mm pomijane; bez pętli — kopia wejścia.
        /// </summary>
        public static List<NibSpan> NibSpansOutside(IList<NibSpan> spans, IList<List<(double X, double Y)>> beamLoops)
        {
            var res = new List<NibSpan>();
            foreach (var e in spans ?? new List<NibSpan>())
            {
                double c = (e.OuterCoord + e.InnerCoord) / 2;
                var parts = beamLoops == null || beamLoops.Count == 0
                    ? new List<(double Lo, double Hi)> { (e.Lo, e.Hi) }
                    : RectilinearPolygons.UncoveredParts(e.Lo, e.Hi, beamLoops, horizontal: !e.Vertical, c);
                foreach (var (lo, hi) in parts)
                    res.Add(new NibSpan { Vertical = e.Vertical, OuterCoord = e.OuterCoord, InnerCoord = e.InnerCoord, Lo = lo, Hi = hi, InwardSign = e.InwardSign });
            }
            return res;
        }

        // ── K8: belki z opisów „xxxTHK” bez warstwy SD-Thickening-Zone (GA C12; domyślne do potwierdzenia — pytanie 12) ──

        /// <summary>Krawędź linii uskoku (nib): Vertical — x = C; Inward — wnętrze płyty po stronie +1 / −1; Lo..Hi — zakres krawędzi linii uskoku.</summary>
        public sealed class ThkStepEdge
        {
            public bool Vertical;
            public double C, Lo, Hi;
            public int Inward;
        }

        /// <summary>Opis z GA: treść (linie złączone), punkt (MText — środek obwiedni, MLeader — grot pierwszego odnośnika), MLeader.</summary>
        public sealed class ThkText
        {
            public string Text;
            public double X, Y;
            public bool Leader;
        }

        /// <summary>Strefa belki z opisów: wielokąt (CCW), H, źródła (komunikaty).</summary>
        public sealed class ThkZone
        {
            public List<(double X, double Y)> Polygon = new List<(double X, double Y)>();
            public int Height;
            public string Note;
        }

        /// <summary>Pas szukania opisu od linii uskoku w głąb płyty [mm].</summary>
        public const double ThkBand = 750.0;
        /// <summary>Linia strefy w GA: równoległa do linii uskoku w tej odległości w głąb (C12 / Holbrook: 600) [mm].</summary>
        public const double ThkLineMin = 300.0, ThkLineMax = 1000.0;

        /// <summary>
        /// Strefa belki = prostokąt wzdłuż krawędzi linii uskoku (nib): od linii uskoku <paramref name="c"/> w głąb płyty (inward) na
        /// <paramref name="depth"/>, wzdłuż lo..hi. Wspólne z RC_BEAM → Krawedz (AutoRebarEngine.CreateZoneFromEdge). Wierzchołki CCW.
        /// </summary>
        public static List<(double X, double Y)> ZoneFromNibEdge(bool vertical, double c, int inward, double lo, double hi, double depth)
        {
            double c0 = c, c1 = c + inward * depth;
            var r = vertical
                ? new List<(double X, double Y)> { (c0, lo), (c1, lo), (c1, hi), (c0, hi) }
                : new List<(double X, double Y)> { (lo, c0), (hi, c0), (hi, c1), (lo, c1) };
            if (SignedArea(r) < 0) r.Reverse();
            return r;
        }

        /// <summary>
        /// K8 (plan praktyki biura, poprawka 8 recenzji): strefy belek z opisów „xxxTHK” (MText / MLeader na SD-Text), gdy GA nie ma
        /// warstwy SD-Thickening-Zone:
        ///   • odrzucenia (komunikat): „GB-…” (belka gruntowa poza płytą — detal ręczny), H ≤ grubość płyty (opis płyty), punkt poza
        ///     obrysem, punkt poza pasem [linia uskoku, +750] żadnej krawędzi nibu; MLeader bez linii strefy w GA (pole lokalne, np. styk GB);
        ///   • zasięg w głąb: linia strefy z GA (odcinek równoległy do linii uskoku 300–1000 w głąb, najbliższy 600), bez niej — 600;
        ///   • zasięg wzdłuż: krawędź linii uskoku, podzielona odcinkami prostopadłymi w pasie (granice stref o różnym H, PLOT 3: 600 / 675);
        ///   • H przedziału: z opisów MText (najczęstsze), MLeader tylko gdy brak MText (inny H z MLeader — ostrzeżenie);
        ///   • sąsiednia krawędź (narożnik) — dołączona (L / U) tylko, gdy ma własny opis albo linię strefy (na ≥ 50 % krawędzi —
        ///     cała krawędź do dzielnika; albo tylko przy narożniku — od narożnika do dzielnika / końca linii strefy);
        ///   • przedziały tego samego H stykające się → jeden wielokąt (suma prostokątów).
        /// </summary>
        public static List<ThkZone> ZonesFromThk(IList<ThkStepEdge> edges, IList<ThkText> texts,
                                                 IList<((double X, double Y) A, (double X, double Y) B)> lines,
                                                 IList<(double X, double Y)> outline, double slabThickness, List<string> msgs,
                                                 double defaultDepth = 600)
        {
            var res = new List<ThkZone>();
            if (edges == null || texts == null) return res;
            string F(double v) => v.ToString("0", CultureInfo.InvariantCulture);

            // linia strefy i dzielniki krawędzi
            double? LineDepth(ThkStepEdge e)
            {
                double best = double.NaN;
                foreach (var (a, b) in lines ?? new List<((double X, double Y), (double X, double Y))>())
                {
                    bool vert = Math.Abs(a.X - b.X) < 1.0, hor = Math.Abs(a.Y - b.Y) < 1.0;
                    if (e.Vertical ? !vert : !hor) continue;
                    double cc = e.Vertical ? (a.X + b.X) / 2 : (a.Y + b.Y) / 2;
                    double l0 = e.Vertical ? Math.Min(a.Y, b.Y) : Math.Min(a.X, b.X), l1 = e.Vertical ? Math.Max(a.Y, b.Y) : Math.Max(a.X, b.X);
                    double off = (cc - e.C) * e.Inward;
                    if (off < ThkLineMin || off > ThkLineMax) continue;
                    if (Math.Min(l1, e.Hi) - Math.Max(l0, e.Lo) < 0.5 * (e.Hi - e.Lo)) continue;
                    if (double.IsNaN(best) || Math.Abs(off - defaultDepth) < Math.Abs(best - defaultDepth)) best = off;
                }
                return double.IsNaN(best) ? (double?)null : best;
            }
            List<double> Dividers(ThkStepEdge e, double depth)
            {
                var r = new List<double>();
                foreach (var (a, b) in lines ?? new List<((double X, double Y), (double X, double Y))>())
                {
                    bool vert = Math.Abs(a.X - b.X) < 1.0, hor = Math.Abs(a.Y - b.Y) < 1.0;
                    if (e.Vertical ? !hor : !vert) continue;        // prostopadłe do krawędzi
                    double along = e.Vertical ? (a.Y + b.Y) / 2 : (a.X + b.X) / 2;
                    if (along <= e.Lo + 1.0 || along >= e.Hi - 1.0) continue;
                    double q0 = (e.Vertical ? a.X : a.Y), q1 = (e.Vertical ? b.X : b.Y);
                    double o0 = Math.Min((q0 - e.C) * e.Inward, (q1 - e.C) * e.Inward), o1 = Math.Max((q0 - e.C) * e.Inward, (q1 - e.C) * e.Inward);
                    if (o0 <= 5.0 && o1 >= depth - 5.0) r.Add(along);   // odcinek od linii uskoku do linii strefy
                }
                return r.OrderBy(x => x).ToList();
            }

            // linia strefy sąsiedniej krawędzi tylko przy narożniku: równoległa, 300–1000 w głąb, bliższy koniec w zasięgu strefy
            // narożnika (cornerDepth + 50), długość ≥ 100; (głębokość, dalszy koniec wzdłuż krawędzi)
            (double depth, double far)? CornerLine(ThkStepEdge e, bool atLo, double cornerDepth)
            {
                (double depth, double far)? best = null;
                foreach (var (a, b) in lines ?? new List<((double X, double Y), (double X, double Y))>())
                {
                    bool vert = Math.Abs(a.X - b.X) < 1.0, hor = Math.Abs(a.Y - b.Y) < 1.0;
                    if (e.Vertical ? !vert : !hor) continue;
                    double cc = e.Vertical ? (a.X + b.X) / 2 : (a.Y + b.Y) / 2;
                    double l0 = Math.Max(e.Lo, e.Vertical ? Math.Min(a.Y, b.Y) : Math.Min(a.X, b.X));
                    double l1 = Math.Min(e.Hi, e.Vertical ? Math.Max(a.Y, b.Y) : Math.Max(a.X, b.X));
                    double off = (cc - e.C) * e.Inward;
                    if (off < ThkLineMin || off > ThkLineMax || l1 - l0 < 100.0) continue;
                    double near = atLo ? l0 - e.Lo : e.Hi - l1;
                    if (near > cornerDepth + 50.0) continue;
                    if (best == null || Math.Abs(off - defaultDepth) < Math.Abs(best.Value.depth - defaultDepth))
                        best = (off, atLo ? l1 : l0);
                }
                return best;
            }

            var entries = new Dictionary<(int e, double lo, double hi), (List<int> hText, List<int> hLeader, double depth, bool line)>();
            foreach (var t in texts)
            {
                string txt = t.Text ?? "";
                if (Regex.IsMatch(txt, @"\bGB\s*-", RegexOptions.IgnoreCase))
                {
                    msgs?.Add($"„{txt}” — belka gruntowa poza płytą: detal ręczny (strefa nie utworzona).");
                    continue;
                }
                int? h = null;
                foreach (var l in txt.Split('\n')) if (ParseThk(l) is int hh) { h = hh; break; }
                if (!h.HasValue) continue;
                if (h.Value <= slabThickness + 0.5)
                { msgs?.Add($"„{txt}”: {h} ≤ grubość płyty {F(slabThickness)} — pominięty."); continue; }
                if (outline != null && outline.Count >= 3 && !Inside(outline, (t.X, t.Y)))
                { msgs?.Add($"„{txt}” ({F(t.X)}, {F(t.Y)}): poza obrysem płyty — pominięty."); continue; }
                int best = -1; double bestD = double.MaxValue;
                for (int i = 0; i < edges.Count; i++)
                {
                    var e = edges[i];
                    double across = e.Vertical ? t.X : t.Y, along = e.Vertical ? t.Y : t.X;
                    double d = (across - e.C) * e.Inward;
                    if (d < -5.0 || d > ThkBand || along < e.Lo - 5.0 || along > e.Hi + 5.0) continue;
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best < 0)
                { msgs?.Add($"„{txt}” ({F(t.X)}, {F(t.Y)}): poza pasem {F(ThkBand)} mm od linii uskoku nibu — pominięty (detal ręczny / RC_BEAM)."); continue; }
                var eg = edges[best];
                double? ld = LineDepth(eg);
                if (!ld.HasValue && t.Leader)
                {
                    msgs?.Add($"„{txt}” (odnośnik): brak linii strefy w GA przy tej krawędzi — pole lokalne (np. styk GB), strefa nie utworzona; " +
                              "w razie potrzeby RC_BEAM.");
                    continue;
                }
                double depth = ld ?? defaultDepth;
                double al = eg.Vertical ? t.Y : t.X;
                var dv = Dividers(eg, depth);
                double lo = eg.Lo, hi = eg.Hi;
                foreach (var x in dv) { if (x < al) lo = Math.Max(lo, x); else hi = Math.Min(hi, x); }
                var key = (best, Math.Round(lo, 3), Math.Round(hi, 3));
                if (!entries.TryGetValue(key, out var en)) en = (new List<int>(), new List<int>(), depth, ld.HasValue);
                (t.Leader ? en.hLeader : en.hText).Add(h.Value);
                entries[key] = en;
                if (!ld.HasValue)
                    msgs?.Add($"„{txt}”: brak linii strefy w GA — strefa {F(defaultDepth)} od linii uskoku (do sprawdzenia).");
            }

            // H przedziałów
            var zoned = new List<(int e, double lo, double hi, int h, double depth)>();
            foreach (var kv in entries)
            {
                var (ht, hl, depth, _) = kv.Value;
                int H = ht.Count > 0 ? ht.GroupBy(x => x).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key
                                     : hl.GroupBy(x => x).OrderByDescending(g => g.Count()).ThenByDescending(g => g.Key).First().Key;
                if (ht.Count > 0 && hl.Any(x => x != H))
                    msgs?.Add($"odnośnik „{string.Join("/", hl.Distinct())}THK” w strefie {H}THK — lokalne pogłębienie: detal ręczny (przyjęto {H}).");
                if (ht.Distinct().Count() > 1) msgs?.Add($"różne wysokości w opisach jednej strefy ({string.Join("/", ht.Distinct())}) — przyjęto {H}.");
                zoned.Add((kv.Key.e, kv.Key.lo, kv.Key.hi, H, depth));
            }

            // sąsiednie krawędzie (narożnik L / U) z linią strefy — dołączone z tym samym H
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var z in zoned.ToList())
                {
                    var e = edges[z.e];
                    foreach (double endAlong in new[] { z.lo, z.hi })
                    {
                        if (Math.Abs(endAlong - e.Lo) > 1.0 && Math.Abs(endAlong - e.Hi) > 1.0) continue;
                        for (int j = 0; j < edges.Count; j++)
                        {
                            var f = edges[j];
                            if (f.Vertical == e.Vertical || zoned.Any(x => x.e == j)) continue;
                            if (Math.Abs(f.C - endAlong) > 1.0) continue;
                            if (Math.Abs(f.Lo - e.C) > 1.0 && Math.Abs(f.Hi - e.C) > 1.0) continue;
                            bool atLo = Math.Abs(f.Lo - e.C) <= 1.0;
                            double lo = f.Lo, hi = f.Hi;
                            double? fd = LineDepth(f);
                            if (fd.HasValue)
                            {
                                var dv = Dividers(f, fd.Value);
                                if (dv.Count > 0) { if (atLo) hi = dv[0]; else lo = dv[dv.Count - 1]; }
                            }
                            else
                            {
                                // 2026-10-13: linia strefy tylko przy narożniku (krótka noga L, np. 375THK wzdłuż góry i ~2 m w dół
                                // prawej krawędzi) — linia równoległa zaczynająca się przy narożniku; zasięg do dzielnika albo do jej końca
                                var cl = CornerLine(f, atLo, z.depth);
                                if (!cl.HasValue) continue;
                                fd = cl.Value.depth;
                                var dv = Dividers(f, fd.Value);
                                double far = cl.Value.far;
                                if (atLo) { hi = Math.Min(f.Hi, far); foreach (var x in dv) if (x > f.Lo + 1.0) { hi = Math.Min(hi, x); break; } }
                                else { lo = Math.Max(f.Lo, far); for (int q = dv.Count - 1; q >= 0; q--) if (dv[q] < f.Hi - 1.0) { lo = Math.Max(lo, dv[q]); break; } }
                                if (hi - lo < 100.0) continue;
                            }
                            zoned.Add((j, lo, hi, z.h, fd.Value));
                            msgs?.Add($"krawędź uskoku {(f.Vertical ? "x" : "y")}={F(f.C)}: linia strefy w GA bez opisu — dołączona do strefy {z.h}THK (L / U).");
                            changed = true;
                        }
                    }
                }
            }

            // prostokąty → grupy tego samego H (stykające się) → wielokąt
            var rects = zoned.Select(z =>
            {
                var e = edges[z.e];
                var p = ZoneFromNibEdge(e.Vertical, e.C, e.Inward, z.lo, z.hi, z.depth);
                return (z.h, rect: new RectilinearPolygons.Rect(p.Min(q => q.X), p.Min(q => q.Y), p.Max(q => q.X), p.Max(q => q.Y)));
            }).ToList();
            var parent = Enumerable.Range(0, rects.Count).ToArray();
            int Find(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
            for (int i = 0; i < rects.Count; i++)
                for (int j = i + 1; j < rects.Count; j++)
                {
                    if (rects[i].h != rects[j].h) continue;
                    var a = rects[i].rect; var b = rects[j].rect;
                    if (a.X0 <= b.X1 + 1.0 && b.X0 <= a.X1 + 1.0 && a.Y0 <= b.Y1 + 1.0 && b.Y0 <= a.Y1 + 1.0) parent[Find(i)] = Find(j);
                }
            foreach (var g in Enumerable.Range(0, rects.Count).GroupBy(Find))
            {
                var loops = RectilinearPolygons.Union(g.Select(i => rects[i].rect));
                if (loops.Count == 0) continue;
                // Union: pętle zewnętrzne CCW, otwory CW — każda pętla zewnętrzna = osobna strefa (np. prostokąty stykające się
                // tylko w tolerancji 1 mm); otwór w sumie (U zamknięte w pierścień) — pomijany z komunikatem
                var outers = loops.Where(l => RectilinearPolygons.SignedArea(l) > 0).ToList();
                int h = rects[g.First()].h;
                if (loops.Count > outers.Count) msgs?.Add($"strefa {h}THK: suma prostokątów z otworem — otwór pominięty, sprawdź strefę.");
                if (outers.Count > 1) msgs?.Add($"strefa {h}THK: {outers.Count} rozłączne części — osobne strefy.");
                foreach (var o in outers)
                    res.Add(new ThkZone { Polygon = o, Height = h, Note = g.Count() > 1 ? $"{g.Count()} krawędzie (L / U)" : null });
            }
            return res;
        }

        /// <summary>Wysokość belki z opisu GA („450THK”, „375 THK”; bez „mm” — to grubość płyty); null gdy brak.</summary>
        public static int? ParseThk(string line)
        {
            if (string.IsNullOrEmpty(line) || Regex.IsMatch(line, @"SLAB", RegexOptions.IgnoreCase)) return null;
            var m = Regex.Match(line, @"(?<![\d.,])(\d{3,4})\s*THK\b", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        /// <summary>Kontrola krzyżowa wysokości: SSL − NIB TOC + wysokość nibu (319: 44.375 − 44.075 + 150 = 450).</summary>
        public static int HeightFromLevels(double ssl, double nibToc, double nibHeight = 150)
            => (int)Math.Round((ssl - nibToc) * 1000.0 + nibHeight, MidpointRounding.AwayFromZero);

        /// <summary>„NIB TOC=44.075” → 44.075; null gdy brak.</summary>
        public static double? ParseNibToc(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var m = Regex.Match(text, @"NIB\s*TOC\s*[=:]?\s*\+?(-?\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            return double.Parse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        // ── pomocnicze ──────────────────────────────────────────────────

        public static bool Inside(IList<(double X, double Y)> poly, (double X, double Y) p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                var a = poly[i]; var b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
            }
            return inside;
        }

        private static double SignedArea(List<(double X, double Y)> v)
        {
            double a = 0;
            for (int i = 0; i < v.Count; i++) { var p = v[i]; var q = v[(i + 1) % v.Count]; a += p.X * q.Y - q.X * p.Y; }
            return a / 2;
        }

        private static double Dist((double X, double Y) a, (double X, double Y) b)
            => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    }
}
