# BricsCadRc — architektura

> Dokument wspólny dla użytkownika i agentów. Aktualizuj go, gdy zmieniasz schemat XData,
> dodajesz moduł albo użytkownik ustala nową zasadę.

Produkt: **NA Smart RC** — DLL `bin\NaSmartRc.dll` (namespace `BricsCadRc` bez zmian, `App/ProductInfo`).
Stara wtyczka kolegi to `BricsCadRc.dll` (te same komendy i XData); wczytana jest jedna naraz — autoload przełącza
`scripts\wybierz-wtyczke.ps1`. Log i ustawienia: `%LOCALAPPDATA%` / `%APPDATA%\NaSmartRc`.

## 1. Model obiektów na rysunku

| Obiekt | Encja | XData (RegApp) | Rola |
|---|---|---|---|
| Pręt (widok/elewacja) | `Polyline` (obrys pręta) albo pręt-blok `BlockReference` do własnego BTR `*U` ([14] = 2, format Blok — `RC_BAR_FORMAT`) | `RC_SINGLE_BAR` | Definicja pozycji: średnica, kształt, wymiary A–E, Mark `H12-03` |
| Etykieta pręta | `MLeader` | `RC_BAR_LABEL` | Opis pręta, np. `24 H12-03` (liczba = suma z rozkładów) |
| Rozkład | `BlockReference` do BTR `RC_SLAB_BARS_nnn` | `RC_BAR_BLOCK` | Linie prętów w rzucie płyty + symbole końców |
| Opis rozkładu | `BlockReference` (linia rozkładu + tekst) | `RC_BAR_ANNOT` | Linia rozkładu, strzałka, tekst `H12-03-200 B1` |
| Towarzysz pręta (stary format) | encje | `RC_BAR_LINK` | Wskazuje główną polilinię pręta |
| Znacznik płyty (AutoRebar) | na rozkładzie | `RC_AUTOREBAR_SLAB` | Handle obrysu płyty, z którego wygenerowano rozkład |
| Tytuł przekroju (głowa) | `MText` „SECTION A-A” (AP-TEXT) | `RC_SECTION` + Xrecord `NA_RC_SECTION` | Przekrój RC_SECTION: GUID, litera, obrys, odcisk, lista części (sekcja 4g) |
| Część przekroju | encje rysunku przekroju i znaczniki na rzutach | `RC_SECTION_PART` | Powiązanie z tytułem, rola, klucz, GUID |
| Strefa belki / pogrubienia | zamknięta `Polyline` na warstwie `RC-BEAM` (kopia `SD-Thickening-Zone` z GA albo RC_BEAM) | `RC_BEAM` | Parametry belki (H, szerokość, nib, strzemiona, pręty), obrys płyty [6] (sekcja 4i) |
| Polilinia ręczna belki | zamknięta `Polyline` użytkownika na warstwie `RC-BEAM-MANUAL` (kolor 30, opis w Layer description) | `RC_BEAM_MANUAL` | Strefa belki rysowana ręcznie — źródło stref RC-BEAM obu rzutów; zostaje (dane użytkownika) — sekcja 4i „Warstwa RC-BEAM-MANUAL” |
| Znacznik belki | na rozkładzie belki | `RC_AUTOREBAR_BEAM` | Handle strefy belki, dla której wygenerowano rozkład (kody BM / BU / BB / BT / BL / BS / BC / BN) |
| Obiekt detalu belki 1:25 (K9) | `Polyline` ANCHOR (kopia strefy), `Line`, `Circle`, `MText`, `MLeader` na warstwie `RC-BEAM-DETAIL` | `RC_BEAM_DETAIL` | Detal zbrojenia belki: miejsce (ANCHOR), linie betonu, pale, „xxxTHK”, tytuł, odnośnik „DETAIL 'n'” na rzutach (sekcja 4i) |

Powiązania:
```
Pręt ──[13] LabelHandle──► Etykieta ──[1] bar handle──► Pręt        (back-link sprawdzany!)
Rozkład ──[18] SourceBarHandle──► Pręt
Rozkład ──[11] AnnotHandle──► Opis rozkładu ──[16] SourceBlockHandle──► Rozkład
Rozkład ──[19]/[20] LabelPolyHandle/LabelTextHandle
Rozkład ──RC_AUTOREBAR_SLAB──► obrys płyty
Strefa belki ──RC_BEAM [6]──► obrys płyty (tego rzutu);  Rozkład belki ──RC_AUTOREBAR_BEAM [1]──► strefa belki
Polilinia ręczna belki ──RC_BEAM_MANUAL [2] / [3]──► strefa na rzucie polilinii / na drugim rzucie
Obiekt detalu belki ──RC_BEAM_DETAIL [2]──► strefa belki (rozkłady detalu — jak wyżej, RC_AUTOREBAR_BEAM)
Tytuł przekroju ──[4]/[5]──► obrys płyty (wskazany / zewnętrzny); [6] ──► sam tytuł (wykrycie kopii)
Tytuł przekroju ══Xrecord PART=handle…══► części ──[1]──► tytuł   (część potwierdzona = w obu kierunkach + [4] GUID)
```

### Zasady powiązań (obowiązkowe)
- Zapis wyłącznie `XLink.Write(hex)` → DXF **1005** (prawdziwy handle). **BricsCAD V25 NIE przemapowuje 1005
  przy COPY / MIRROR / ARRAY** (klony wskazują oryginały — spike W2-0); przy wstawianiu (PASTECLIP, -INSERT) tłumaczy
  odwołania do obiektów wstawianych razem, a pozostałe zeruje. Powiązania kopii przepina mapa klonowania
  (`CloneMap` → `CloneRemap`, W2) — patrz „Reakcje na edycję”.
- Odczyt wyłącznie `XLink.Read(tv)` — akceptuje 1005 i stary tekst 1000, zwraca „X8”.
- Porównanie wyłącznie `XLink.Same(a, b)` (nie `string.Equals`).
- Przed modyfikacją/usunięciem/przeliczeniem etykiety sprawdź **back-link** (etykieta wskazuje
  na ten pręt). Kopia bez etykiety nie może ruszać etykiety oryginału.
- Po COPY / MIRROR / ARRAY / PASTE / INSERT powiązania klonów (także stare rekordy 1000 — zapis 1005) przepina
  `CloneRemap` z mapy oryginał → klon. Heurystyki `BarCopyWatcher.RemapCopiedBarLabels` (grot etykiety na pręcie)
  i `RemapCopiedPairs` (rozkład ↔ opis) — tylko zapas dla obiektów spoza mapy.

### Schematy XData (indeksy pozycyjne — NIE przestawiać, nowe pola tylko na końcu)
- `RC_SINGLE_BAR`: [1]Mark [2]Diameter [3]ShapeCode [4]A [5]B [6]C [7]LayerCode [8]Position
  [9]D [10]E [11]TotalLength [12]LengthOverridden [13]LabelHandle(1005)
  [14]FormatVersion (Int16; 2 = pręt jako BlockReference — Plan C; 1 = polilinia z ogonem (W4c); brak pola = polilinia)
  [15]ShapeStd (Int16; 2020 = kształt wg BS 8666:2020 — W4c, kapelusz 44; **2021 = obrys zbudowany geometrią W4d lub późniejszą**
  (od W4e także z wymiarami zewnętrznymi — bez nowej wartości znacznika, D35: wariant W4d / W4e jednoznacznie z obrysu) —
  W4d runda 2: każdy pręt utworzony (PlaceBar, pręt-blok wtyczki) albo przebudowany (RebuildCompanions — RC_EDIT_BAR, RC_UPDATE_BAR,
  Plan B watchera; pręt-blok: `SingleBarBlock.Rebuild` — RC_EDIT_BAR, RC_UPDATE_BAR / naprawa SCALE, grip końca) przez wtyczkę
  W4d+, `BarXData.MarkW4dGeometry`; pierścień przy przebudowie i pręt
  testowy H12-T — bez; stary pierścień „44” bez znacznika — bez, do migracji; polilinia bez [14] dostaje wtedy [14] = 1;
  znacznik ≥ 2020 nie jest obniżany). **Rekord „44” bez [15] ≥ 2020 = stary pierścień** (rysunek sprzed W4c albo
  zapisany starą wtyczką, która gubi ogon [14+]): parser `ReadBarXData` podaje go w pamięci jako **75** +
  `BarData.LegacyRing44` (pole niezapisywane); pierwszy zapis utrwala „75” (migracja leniwa, UNDO cofa). Rozpoznanie
  po danych, nie po geometrii. Reguły (bez Teigha): `BarRecordRules.IsLegacyRing44 / WithShapeStd / NeedsShapeStd`.
  **Zapis `RC_SINGLE_BAR` tylko przez `BarXData.Write` / `BarEntity.Write`** — składa nagłówek [0]–[13]
  i zachowuje ogon istniejącego rekordu od [14] (FormatVersion i nowsze pola; nigdy nie dubluje [14] ani [15]);
  dla kształtu 44 dopisuje / podmienia znacznik [15].
- **Wymiary kształtu (W4, zasada parametrów):** XData pozycyjne — parametr i kształtu (`BarShape.Parameters[i]`) → pole
  LengthA…E o indeksie i (jak dialogi, formuły i BBS). Geometria czyta wymiary **po nazwie** (`BarShape.IndexOf / Value`),
  np. 25 {A,B,E}: E = pole [6]. Nowe parametry kształtu tylko DOPISYWANE na końcu `Parameters` (W4b: 31 + D, 47 + C;
  W4d: 14 i 15 + D, 24 + D, E, 46 + D → {A,B,C,E,D}); stare rekordy mają tam 0. Bez migracji danych (poza 44 → 75).
  Dialogi (RC_BAR, RC_EDIT_BAR, picker) pokazują wiersze alfabetycznie — `BarShape.DisplayOrder` = mapa wiersz → pole.
  Wymiary określające tylko kąt (W4d, `BarShape.IsAngleParam`) i C dla 51/63 są opcjonalne: puste / 0 = wartość domyślna
  (kąt 45°, dla 24 — 30°) + ostrzeżenie `[RC] … przyjęto kąt domyślny …` w linii poleceń.
  `SingleBarEngine.WriteXData` to cienka nakładka na `BarXData.Write` (też zachowuje [14+]).
- `RC_BAR_BLOCK`: [1]Mark [2]LayerCode [3]Count [4]Diameter [5]Spacing [6]Direction [7]Position
  [8]LengthA [9]BarsSpan [10]Cover [11]AnnotHandle(1005) [12]ShapeCode [13]SymbolSide
  [14]SymbolDirection [15]ViewingDirection [16]ViewSegmentIndex [17]SymbolType
  [18]SourceBarHandle(1005) [19]LabelPolyHandle(1005) [20]LabelTextHandle(1005) [21]VisibilityMode
  [22]VisibleIndices [23]Angle [24]AnnotScale [25]SkewEnd [26]SkewStart [27]CountDisplay
  [28]Flipped [29]ShowSpacing [30]IsLabelManual
- `RC_BAR_ANNOT`: [1]Mark [2]LayerCode [3]Count [4]Diameter [5]Spacing [6]Direction [7]Position
  [8]LengthA [9]BarsSpan [10]ArmTotalLen [11]TextLen [12]LeaderHorizontal [13]LeaderRight
  [14]ArmMidY [15]LeaderUp [16]SourceBlockHandle(1005) [17]LeaderPoints [18]SkewEnd …
  (pełna lista: `AnnotationEngine.WriteAnnotXData`)
- `RC_BAR_LABEL`: [1]bar handle(1005)
- `RC_SECTION` (tytuł przekroju, bezpiecznik — etap 1): [1]FormatVersion (Int16, 2 — odcisk względem P1 cięcia; 1 = wersja robocza etapu 1) [2]GUID [3]litera [4]SlabPicked(1005)
  [5]SlabOuter(1005) [6]Self(1005 — własny handle; ≠ handle obiektu = kopia bez przepięcia). Reszta w Xrecordzie
  `NA_RC_SECTION` w słowniku rozszerzeń tytułu (`SectionRecord`, linie „KLUCZ=wartość”, kod DXF 1): FORMAT, GUID, LETTER,
  P1 / P2 (bezwzględne, rzut wskazany) i P1REL / P2REL (względem MIN), CUTX, LOOK, LOOKPT, TOPLEFT, MIN (MinPoint obrysu),
  parametry okna (THK, NIBH, CB, CT, CN, TOL, SSL, PILES), opis płyty (DESCTHK, DESCSSL) + flagi nadpisania (THKOVR, SSLOVR),
  HASH, DIGEST, VER (SectionPlanner.Version), składniki odcisku `C:klucz=wartość`, części `PART=handle|rola|klucz`.
  Etap 3 (RC_SECTION_UPDATE): `TITLEDEF=x,y` (domyślne położenie tytułu względem TOPLEFT) i na końcu linii części
  `PART=handle|rola|klucz|x,y|rodzaj` — pozycja domyślna względem TOPLEFT (LBL: punkt tekstu, SLAB: wierzchołek [0]) i rodzaj opisu
  (DOT / BAR / TIN); pola dopisywane tylko, gdy są (rekord etapu 1 bez zmian; starszy parser czyta pola 0–2). CloneRemap je przenosi.
  Nowe klucze tylko dopisywane; `SectionRecord.Parse` toleruje starsze / krótsze rekordy (brak klucza = domyślna, nieznane pomijane).
- `RC_SECTION_PART` (część przekroju): [1]tytuł(1005) [2]rola (SLAB, BAR, DOT, PILE, DIM, LBL, LDR, ARW, SSL, MARK,
  MARKTXT, CLOUD) [3]klucz (np. `SEG#0`, `LBL#3|05`, znaczniki `V<rzut>.P1|P2` — V0 = rzut wskazany) [4]GUID przekroju.
  Bez grup (PICKSTYLE). Chmurka (CLOUD) nie jest na liście części w rekordzie.
- `RC_BEAM` (strefa belki, `BeamStore`, plan belek E1): [1]FormatVersion (Int16, 1) [2]H [3]szerokość [4]szer. nibu [5]wys. nibu
  (Real) [6]obrys płyty (1005, XLink) [7]Ø strzemion (Int16) [8]rozstaw strzemion (Real) [9]Ø dołem [10]liczba dołem [11]Ø górą
  [12]liczba górą [13]Ø U-barów (Int16) [14]kod strzemienia (tekst, „51”) [15]zakład [16]maks. długość pręta (Real)
  [17]góra strzemienia pod: 2 = obiema warstwami T, 1 = jedną (Int16) [18]Ø prętów bocznych (Int16, 0 = brak) [19]rozstaw pionowy
  prętów bocznych (Real) [20]Ø pręta w nibie górą (BN „BEAM NIB T”) [21]Ø prętów w nibie (Int16, 0 = brak) — E4b; od decyzji 2026-10-11 [21] = Ø pozycji
  „2+1 … T+B IN NIB” (3 pręty, domyślnie 10 — decyzja użytkownika 2026-10-11; dawne rekordy [21] = 10 zgodne) [22]otulina boczna / czołowa
  [23]otulina dolna (Real; decyzja 2026-10-10: 45 / 50). Brak pola = wartość domyślna `BeamParams` (rekord bez [22] / [23] → 45 / 50).
  **v2 (K2 planu `docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md`, [1] = 2):** [24]otulina górna strzemienia (Real; A1 35 / A2 50) [25]tryb prętów
  bocznych (Int16; 1 B1 brak, 2 B2 zawsze, 3 B3 od progu) [26]próg H prętów bocznych (Real, 450) [27]krotność zakładu (Real, 40 —
  zakład = Ceil25(krotność · Ø): H10 400, H12 500, H16 650) [28]rozstaw nominalny w poprzek w opisie (Real, 150) [29]B+T jedną pozycją
  (Int16 0 / 1) [30]obniżenie U-barów drugiego kierunku (Real, 35) [31]pręty proste w kwadrat narożnika (Int16 0 / 1; 0 — przed kwadratem)
  [32]koniec pręta prostego przy U-barze od lica (Real; 0 = auto: otulina + Ø U-bara) [33]strzemiona przy UB 03 +100 (Int16 0 / 1; 0 —
  niezależnie) [34]strzemiona w kwadracie narożnika (Int16 0 / 1; 1 — dłuższa noga) [35]naddatek ramienia U-bara końca (Real, 90).
  Od v2: [15] = Ceil25(krotność · Ø dołem) (tylko dla starej wtyczki), [17] nieużywane w generowaniu (zostaje dla RC_SECTION — A strzemienia w przekroju dawnym wzorem; K6 cofnięte 2026-10-11).
  **K9 (decyzja użytkownika 2026-10-11):** [36] pręty belki w detalu 1:25 (Int16 1 — domyślne) albo na rzucie dolnym 1:50 (0 — jak przed K9);
  brak pola (rekord sprzed K9, rekord v1 po migracji) = 1 (detal). Pole poza odciskiem przekroju (`BeamParamsText` bez zmian).
  **Decyzja użytkownika 2026-10-11 (moduł belki, punkty B / C):** [37] cięcie strzemion (Int16): 0 = Auto (domyślne; brak pola = Auto),
  2 = 2-cięte, 4 = 4-cięte (`BeamParams.StirrupLegs`; inna wartość = Auto). Pole poza odciskiem (`BeamParamsText` bez zmian; rysunek przekroju
  zmienia się przez rozkład BL — składnik DIST `Lv`).
  **Decyzja użytkownika 2026-10-11 („w generatorze dodaj opcję dodania lub nie dodawania nibu, bo czasem może nie być”):** [38] nib przy belce
  (Int16): 0 = wg geometrii (domyślne; brak pola = 0 — wynik jak dotąd), 1 = belka BEZ nibu (`BeamParams.NoNib`). Nowe pole zamiast [4] / [5] = 0:
  [4] / [5] to wymiary nibu (wpisywane, w odcisku przekroju `NW` / `NH` — zmiana przeterminowałaby przekroje wszystkich belek), a „0” nie odróżnia
  „wg geometrii” od „bez nibu”; dziś [4] nie wpływa na generowanie (szerokość nibu zawsze z geometrii — odległość lica od obrysu), [5] = 0 dałby
  nib o zerowej wysokości w profilu. Pole poza odciskiem przekroju (`BeamParamsText` bez zmian; przekrój zmienia się przez pas belki i nib płyty).
  **Decyzja Bartka 2026-10-23 (belki GB poza płytą, etap 1):** [39] obniżenie góry belki względem SSL (Real, `BeamParams.TopDrop`; SSL − TOC,
  np. 75; brak pola = 0 — wynik jak dotąd). Pole poza odciskiem przekroju (przekrój GB — kolejny etap).
- `RC_BEAM_MANUAL` (polilinia ręczna belki na warstwie `RC-BEAM-MANUAL`, `BeamStore.ManualApp`): [1]FormatVersion (Int16, 1) [2]strefa na rzucie
  polilinii (1005, XLink) [3]strefa na drugim rzucie (1005, XLink; "" gdy brak). Brak rekordu = polilinia nieprzetworzona. CloneRemap: [2] / [3]
  → klony stref, gdy skopiowane razem, inaczej "" (kopia samej polilinii — nowe strefy przy Prepare GA / Beam, nie przejmuje stref oryginału).
  **Rekord v1** ([1] < 2 — tylko wersje testowe): `BeamStore.Read` → `BeamParams.MigrateV1` W PAMIĘCI (geometria i otuliny bez zmian,
  parametry zbrojenia = nowe domyślne, komunikat z listą zmian); zapis v2 dopiero przy OK w RC_BEAM albo przy generowaniu (ta sama grupa UNDO).
- `RC_AUTOREBAR_BEAM` (rozkład belki): [1]strefa belki (1005). Kopia samych prętów (CloneRemap) → "" — przynależność z testu
  punktu w strefie. Rozkłady w detalu 1:25 (K9) — ten sam znacznik (ponowne Beam usuwa rozkłady strefy z rzutu i z detalu).
- `RC_BEAM_DETAIL` (K9, obiekt detalu belki niebędący prętem, `BeamStore.TagDetail`): [1]FormatVersion (Int16, 1) [2]strefa belki (1005, XLink)
  [3]numer detalu (Int16) [4]rola (tekst): ANCHOR (zamknięta kopia polilinii strefy, szara (8), DASHED, LTSCALE 12,5 — lica belki pod płytą;
  jej położenie wyznacza Shift detalu), LINE (przycięte linie betonu, kolor / rodzaj linii obiektu z rzutu), PILE (pal w oknie, DASHED), TEXT
  („xxxTHK”), TITLE („DETAIL 'n'” / „SCALE 1:25”), FRAME (ramka detalu = okno, czerwona (10) DASHED, LTSCALE 25 — od 2026-10-12), PLANREF
  (na rzucie: ramka okna detalu — czerwona DASHED — i odnośnik „DETAIL 'n'” do ramki). CloneRemap: [2] → klon strefy, gdy skopiowana razem, inaczej "" (detal bez strefy — pomijany).
- Odczyt musi tolerować krótsze (starsze) rekordy — sprawdzaj `v.Length`.

## 2. Moduły

### `src/App`
- `PluginApp` — inicjalizacja: rejestracja overrule'ów i watcherów, ribbon, komunikat „Build: …”.
- `RibbonBuilder` — wstążka.

### `src/Commands`
- `BarCommands` — `RC_BAR`, `RC_DISTRIBUTION`, `RC_EDIT_BAR`, `RC_UPDATE_BAR`, `RC_FIX_LABEL`, `RC_SCHEDULE`.
  `RC_DISTRIBUTION` (K4, e7b600d, decyzje D5 / P3): gdy na rysunku są inne pręty o identycznym kształcie
  (`PositionReconciler.SameShape`; polilinie i pręty-bloki, także kopie o tym samym numerze, bez prętów bez numeru) — okno
  OK / Anuluj z listą numerów (`ConfirmIdenticalBars` / `FindIdenticalBars`, teksty `IdenticalBarText`); w trybie bez okien
  (`ScriptMode.IsNonInteractive`: `/automation`, `RC_NO_DIALOGS=1`, bit 4 `CMDACTIVE` = skrypt .scr — LISP / makra pokażą
  okno) komunikat `[RC] Na rysunku jest już pręt identyczny …` w linii poleceń + log. Tylko odczyt.
  **Method = Any** (decyzja użytkownika 2026-10-14, jak „Any” w ASD; wcześniej e565894, usunięte w 54c6efa): okno „Reinforcement
  detailing” — Method Module / Any. Any = JEDEN pręt: blok rozkładu (RC_BAR_BLOCK, `ViewingDirection` = "Any") z całym kształtem
  pręta wzorcowego jako polilinią wypełnioną (szerokość = Ø; `BarGeometryBuilder.AnyAxis` — najdłuższy odcinek na osi prętów od
  punktu wstawienia; [Obróć] 90°, [Lustro] = `SymbolDirection` Down), w miejscu wskazanym; opis jak rozkład (kropka na pręcie,
  bez linii rozkładu). Count = 1, BarsSpan = 0, liczba prętów z okna opisu w `CountDisplay` (zestawienie BBS — `EffectiveCount`),
  rozstaw w `Spacing` i opisie. `BarBlockEngine.BuildBars` → `BuildAny`; grip zakresu i `UpdateBarLength` bez zmian dla Any;
  RC_EDIT_DISTRIBUTION dla Any — okno opisu (liczba, rozstaw, sufiks). Przekrój (RC_SECTION) — Any jak rozkład 1 pręta (do ustalenia:
  pręty boczne belki).
- `BarFormatCommands` (Plan C, krok 3f) — `RC_BAR_FORMAT [Polilinia/Blok]`: format NOWYCH prętów (RC_BAR, AutoRebar,
  przebicie), zapis `%APPDATA%\NaSmartRc\barformat.txt` (`BarFormat`; fallback: plik starej wtyczki); **domyślnie Polilinia**
  (rysunki dla starej wtyczki kolegi — zawsze Polilinia). Przy starcie: `[NA Smart RC] Nowe pręty: polilinia|blok`.
  Istniejące pręty bez zmian — rysunek może mieć oba formaty.
- `RebuildBarsCommands` — `RC_REBUILD_BARS` (W4e, P8): przebudowa obrysów wszystkich prętów przestrzeni modelu wg bieżącej
  geometrii (ścieżki MOVE / RC_UPDATE_BAR: `RebuildCompanions`, `SingleBarBlock.Rebuild`; rama i axis[0] z obrysu), jedna
  grupa UNDO; pomija obrys już bieżący, nierozpoznany (lista w linii poleceń) i pręt testowy; grot etykiety (back-link)
  na nowy obrys. Dane, numery, rozkłady — bez zmian.
- `EditCommands` — `RC_EDIT_DISTRIBUTION`, `RC_EDIT_LABEL`, `RC_BAR_END`, `RC_SET_REPR_BAR`,
  `RC_SHOW_ALL_BARS`, `RC_SCALE_ANNOT`. **2026-10-16:** `RC_SET_REPR_BAR` („Single Bar View”) — wskazane obrysy płyt, wygaszanie jak
  RC_GENERATE (`AutoRebarEngine.ApplyRepresentativeOnSlab`: środkowy pręt, odcinki w pasie przesunięte o rozstaw, otwór — ucięty +
  cały, UB ADD nieparzyste — obok środka, potem `ResolveRepresentativeCollisions`; belki bez zmian) — odwrotność RC_SHOW_ALL_BARS.
  RC_EDIT_LABEL → Manual: klikane pręty podświetlone grubą czerwoną polilinią (tymczasową, usuwaną po Enter). `RC_BAR_END` (poprawka #10, jak e7b600d) — tylko na rozkładzie (symbol końca
  jest cechą rozkładu); wskazany pręt (polilinia / pręt-blok) → `[RC] Symbol końca ustawia się na rozkładzie — wskaż rozkład, nie pręt.`
- `AutoRebarCommands` — `RC_GENERUJ_B1/B2` (Ø10, strefa szablonów `rebar_bottom`),
  `RC_GENERUJ_T1/T2` (Ø12, `rebar_top`). `AutoRebarUBCommands` — `RC_GENERUJ_UB_B1/B2`.
- `GenerateCommands` (`RC_GENERATE_SLAB`), `PunchingTagCommands`, `CountCommands`, jigi (`AnnotLeaderJig`, `DistributionJig`).
- `SectionCommands` — `RC_SECTION` (przekrój z rekordem bezpiecznika, sekcja 4g), `RC_SECTION_CHECK` (aktualność przekrojów:
  lista ze statusem i przyczynami, chmurki „NIEAKTUALNY”), `RC_SECTION_UPDATE` (wskazany przekrój albo [Wszystkie] nieaktualne —
  `SectionUpdater`; przekrój „nieznany” — adopcja). `BbsCommands` — `RC_BBS` pyta przed oknem BBS, gdy są przekroje
  nieaktualne (`SectionGuard.ConfirmBeforeOutput`).

- `MenuCommands` — angielskie komendy wstążki: `RC_GENERATE` (wybór w linii poleceń: Mesh / B1 / B2 / T1 / T2 / UB1 /
  UB2 / UBNib / B3 / Add / Beam, ostatni wybór domyślny; B3 = B3 ADD w narożnikach wklęsłych — sekcja 4h; Beam = pręty belek —
  sekcja 4i), `RC_PREPARE_GA`, `RC_GA_TEXTS`, `RC_OPENING_DETAIL`, `RC_MODEL_3D`.
- `BeamCommands` — `RC_BEAM` (strefa belki: wskazanie polilinii albo [Krawedz] — obrys + 2 punkty krawędzi → strefa 600 od linii
  uskoku; okno `BeamDialog`; zapis na strefach obu rzutów; pręty — opcja okna), `RC_GENERATE_BEAM` (= RC_GENERATE → Beam:
  obrys rzutu dolnego, górny — Enter = automatycznie, przystający obrys). Sekcja 4i.
  Stare nazwy (RC_GENERUJ_*, RC_PRZYGOTUJ_GA, RC_GA_TEKSTY, RC_DETAL_OTWORU, RC_SIATKA_3D) zostają jako aliasy.

### Wstążka (`RibbonBuilder`)
- Po angielsku (etykiety, podpowiedzi); komunikaty w linii poleceń i dialogi — po polsku.
- Panele: Setup (Prepare GA; Import Analysis; GA Texts — „Reinf. Maps” zastąpione przez Import Analysis, komenda
  `RC_IMPORT_MAP` zostaje) · Reinforcement (Generate, Section; Check Sections, Update Section; Opening Detail, Punching; 3D Model,
  Summary Bars) · Bars (New Bar, Distribution; Bar Format) · Edit (kolumny małych przycisków) · Schedule (BBS; Bar Schedule, Count Bars).
- Ikony: `Resources/Icons/<nazwa>_32.png` i `_16.png` (EmbeddedResource `BricsCadRc.Icons.*`), generowane skryptem
  (proste wektorowe, paleta: pomarańczowy = pręty, niebieski = góra/akcje, szary = beton).

### `src/Core` — silniki
- `SingleBarEngine` — pręt (polilinia), etykieta MLeader, XData pręta (`ReadBarXData` — jedyny parser rekordu).
  `RebuildCompanions` przebudowuje obrys w **ramie pręta** (poprawka #4): `FrameFromOutline` (obrót + odbicie z obrysu,
  dane pasujące do TEGO obrysu; zapas: obrys sprzed komendy, cięciwa osi, tożsamość); rama tożsamościowa (pręt poziomy,
  nieodbity) = dokładnie dawna formuła wzorca (bit w bit). Wywołujący podają ramę jawnie: `BarEditApply` (obrys i dane
  SPRZED edycji), watcher `ApplyEndStretch` (rama sprzed komendy), Plan B / zepsuty 00 (bieżący obrys, zapas: sprzed komendy).
  **Pierścień** (poprawka #5; rozpoznanie wyłącznie `BarFrameRules.IsRingShape` / `SingleBarEngine.IsRingShape(bar)` —
  od W4c kod **75** = `BarFrameRules.RingShapeCode`; stary „44” czytany jako 75): `Rebuild44` przebudowuje OBA okręgi (zewnętrzny = polilinia główna, wewnętrzny = `RC_BAR_LINK`),
  ŚRODEK zostaje (z obrotem i odbiciem); zmiana kształtu na pierścień — start w axis[0] starego pręta. Pręt-blok:
  `SingleBarBlock.Rebuild` przesuwa punkt wstawienia tak, by środek pierścienia został przy zmianie A.
- `BarFrameRules` (W1d, bez Teigha, testy `tests/BarFrameRulesTests.cs`) — `LocalOutline`, `TryFit` (wierzchołek po
  wierzchołku, kandydaci bez/z odbiciem, `FitTol` 0,5 mm, kąt < 1e-9 → tożsamość dokładnie), `TryFitRing`, `TryChord`,
  `IsRingShape` / `RingShapeCode` („75”). W4a: `TryFitVariants` — obrys porównywany ze WSZYSTKIMI wariantami geometrii
  (`BarGeometryOptions.FitCandidates`: bieżąca (W4h) → W4e → W4d → W4b → W4a → pozostałe kombinacje flag → Legacy), więc pręty narysowane przed W4
  i w każdym etapie W4 zachowują obrót i odbicie (`FrameFromOutline`); `LegacyLocalOutline`, `LocalOutlineFrom`.
- `BarGeometryBuilder.GetLocalPoints(code, pv, d[, BarGeometryOptions])` (W4) — flagi `{ Bs2020Sharp, TangentArcs,
  ExternalDims, TableRadius }` (16 kombinacji; rzeczywiste etapy: sprzed W4 = Legacy, W4a = TangentArcs, W4b–W4c =
  `BarGeometryOptions.W4b` = TangentArcs + TableRadius — promień Table 2 i haki 51/63 wg 2020; `BarGeometryOptions.W4d` = W4b +
  `Bs2020Sharp`; `BarGeometryOptions.W4e` = W4d + `ExternalDims`; **W4h = `Current`** = W4e + `SlopeFromB`); wszystkie false = **Legacy** = `LegacyBarGeometry` (zamrożona kopia geometrii sprzed W4 — tylko do
  rozpoznania starych obrysów, NIE poprawiać). Bieżąca: łuk styczny t = r·tan(φ/2) (90° → t = r bit w bit),
  odcinki zerowe bez łuku, parametry po nazwie, r z Table 2, haki 51/63 C_eff, ostre punkty wg rysunków Table 5 (W4d):
  12 łuk o promieniu R (≤ 0 → min. promień), 14 / 15 A skośne od wolnego końca + C poziome (14 wstecz, 15 dalej; kąt
  `BarShape.SlopeAngle` = atan2(B, D), brak → 45°), 22 A–B–półkole C (średnica)–D, 24 A poziome, B skos (atan2(D, E), brak → 30°),
  C pion, 33 „bieżnia” (półkola średnicy B, długość A, pręt okrąża 1½ raza, końce w odległości C od prawej skrajni),
  46 A–B skos w dół–C–B skos w górę–E (kąt asin(D / B), brak → 45°), 51 A = wysokość, B = szerokość.
  **W4e (poprawka #13, `ExternalDims`): wymiary A–E są ZEWNĘTRZNE (kl. 7.1)** — `BarDimensionRules.ToAxis(code, pv, d)` (czysta
  tabela per kształt, bez Teigha) przelicza je na oś: ramię wolny koniec → lico −d/2, między dwoma licami (U: B, Z: B, 44: B–D,
  22: B — od lica A do skrajni półkola, strzemiona 51 / 63: A, B) −d, zagięcia ≠ 90° wg rys. 4 / 5 normy (`LegReduction`: φ ≤ 90° — do przecięcia lic zewnętrznych,
  d/2·tan(φ/2); φ > 90° — do stycznej w skrajni łuku, (r + d) − (r + d/2)·tan(φ/2)); łuk osi r + d/2 (12: R + d/2, 13: pętla
  (B − d)/2, 22: półkole (C − d)/2, 33: półkola (B − d)/2); haki 51 / 63 przy pustym C: oś C_eff − d/2; pierścień 75: oś (A − d)/2,
  obrys zewnętrzny A/2, wewnętrzny A/2 − d, środek (A/2, A/2) bez zmian (`SingleBarEngine.BuildVisualEntities`). Kąt skosu
  (14 / 15 / 24 / 46) z wymiarów podanych przez użytkownika (B / D / E zewnętrzne — przybliżenie).
  **W4h (`SlopeFromB`, decyzja użytkownika 2026-10-09):** kąt także z jednego wymiaru — 14 / 15: θ = asin(B/A) (albo acos(D/A)),
  24: asin(D/B) / acos(E/B); z obu jak dotąd; brak → 45° / 30° + ostrzeżenie; rzut ≥ ramię → 90° + ostrzeżenie
  (`BarShape.SlopeAngle(…, fromSingle)`, `DefaultAngleWarning`). Pole „Kąt [°]” w RC_BAR / RC_EDIT_BAR i pickerze
  (`BarShape.AngleToDimensions`): wpisany kąt → B, D (24: D, E; 46: D) co 5 mm w dotychczasowych polach (XData bez zmian).
  Obrysy sprzed W4h (np. 15 z B bez D rysowany 45°) rozpoznaje wariant W4e (`SlopeFromB = false`). Kształty z geometrią do W4d-3
  (25–29, 31, 32, 34–36, 41, 47, 56, 64, 98) — wymiary bez przeliczenia, tylko łuk r + d/2. axis[0] pręta zostaje (P8).
  Obrysy zapisane w każdym etapie rozpoznaje `TryFitVariants` (testy na ZAMROŻONYCH obrysach etapów:
  `tests/FrozenOutlinesW4c.cs`, `tests/FrozenOutlinesW4d.cs`, W4a w `BarW4Round1Tests`).
- `BarEntity` / `BarXData` / `BarRecordRules` (Plan C, etap 2) — jeden punkt odczytu i zapisu pręta niezależnie od encji:
  `BarEntity.KindOf/From/Open` rozpoznaje polilinię (v1) i pręt-blok (`BlockReference`, [14]=2); każde miejsce podaje
  zbiór `BarKinds` (dawny zbiór + `Block`, więc dla polilinii nic się nie zmienia); `OutlineVertices` = wierzchołki obrysu
  w WCS. `BarXData.Write` — zapis rekordu z zachowaniem [14+]. `BarRecordRules` — czysta logika rekordu (bez Teigha,
  testy jednostkowe). Bezstanowe, bez cache.
  Pręt-blok v2 widzą: `BbsDrawingReader` (RC_BBS), `PositionReconciler`, `AnnotationEngine.UpdateBarLabelCount`,
  `UbHeight` (Rebar3dCommands), `RC_DISTRIBUTION` (wybór pręta), `GetViewLengthManual` (Manual, także RC_EDIT_DISTRIBUTION; W4: odcinek wybrany na bieżącym obrysie,
  długość z obrysu WZORCOWEGO dla tego samego odcinka — `BarCommands.ManualSegmentLength` → `BarFrameRules.LegacySegmentLength`,
  mapowanie etykietami ramion / łuków. **Reguła (W4d, rundy 1–2):** (b) pręt ze znacznikiem [15] ≥ 2021 (`BarData.GeometryW4d`),
  obrys W4d (i późniejszy — W4e) i kształt ze zmienionym układem ramion (`BarFrameRules.ArmLayoutChangedInW4d`: 14, 15, 22,
  24, 33, 46, 51) → wskazany odcinek geometrii W4d bez wymiarów zewnętrznych (punkt odniesienia W4d także po W4e);
  W4e: obrys z wymiarami zewnętrznymi i odcinek bez odpowiednika we wzorcu (44, półkola 22 / 33) → ten sam odcinek wariantu
  bez `ExternalDims` (= długość zmierzona przed W4e) — rozkład Manual przed / po W4e bez zmian; pierścień 75 z obrysem W4e
  (promień A/2) → cięciwa tego samego odcinka okręgu sprzed W4e (promień A/2 + d/2); punkt odniesienia W4d = bieżący wariant
  bez `ExternalDims` (`BarGeometryOptions.WithoutExternalDims` — z regułą kąta obrysu);
  (a) w każdym innym przypadku — brak znacznika (pręt sprzed W4d, także gdy jego obrys jest identyczny z W4d, np. 51 A = B;
  dopasowanie najpierw do wariantów historycznych), obrys historyczny, kształt bez zmiany układu ramion (00, 11, 12, 13, 21, …)
  → geometria sprzed W4 (wynik = wzorzec, bit w bit). Istniejące rozkłady nie są przeliczane — reguła działa przy wskazaniu
  ramienia),
  kaskada usuwania (`SingleBarBlockEraseOverrule`), `PositionCounter`, `RC_SCHEDULE`. Tworzenie, AutoRebar, watchery,
  kopiowanie i gripy polilinii — bez zmian (etapy 3–5).
- `SingleBarBlock` / `BarFormat` (Plan C, etap 3) — pręt-blok: własny BTR `*U` (`Explodable = false`), zawartość = obrys
  z `BuildVisualEntities` (warstwa 0, ByBlock, bez XData), XData tylko na referencji (`BarXData.Write(…, 2)`); punkt wstawienia =
  axis[0] w WCS (pierścień 75 — punkt wstawienia polilinii). `Create`, `Rebuild` (zawsze copy-on-write `EnsureOwnBtr`),
  `RepairScale`, `ApplyEndLength` (grip / STRETCH 00 / 99). `BarFormat.Current` (Polyline | Block; zmienna `RC_REG_BARFORMAT`
  tylko dla testów, potem plik ustawienia) — jedyny punkt rozgałęzienia w `SingleBarEngine.PlaceBar`.
- `BarBlockEngine` — rozkłady (BTR + BlockReference), XData rozkładu, `GenerateFromBounds`, `UpdateBarLength`.
  Kółko końca pręta (bar end Circle, r = 35 × skala opisu) leży W długości pręta: krawędź kółka = koniec pręta,
  linia kończy się w środku kółka — długość pręta na rzucie i gripy takie same z kółkiem i bez.
- `AnnotationEngine` — opisy rozkładów, linia rozkładu, `SyncAnnotation`, `UpdateBarLabelCount`. **Linia rozkładu ciągła** (decyzja 2026-10-15,
  `DistLineLinetype`; dawniej _DOT / CENTER — kreskowana w rzutniach); stare opisy: `RC_OPISY_CIAGLE`.
- `AutoRebarEngine` — automatyczne zbrojenie płyty (sekcja 4).
- `BarGeometryBuilder`, `ShapeCodeLibrary`, `BarShape` — kształty BS 8666:2020 (W4b: formuły 2020; `BarShape.MinBendRadius`
  = Table 2 (H25 → 87; poza tabelą 2d / 3.5d), `HookDiameterQ`, `MinEndProjectionP`; 47 bez C, 64 (wymaga F), q dla d = 50
  → wyjątek = „brak długości”). W4c: **44 = „kapelusz”**, **75 = pierścień** (A = średnica, B = zakład), 77 (helisa)
  nieobsługiwany; picker: 00 11 13 15 21 33 44 46 51 63 75. W4d: `SlopeAngle` / `DefaultAngleWarning` / `IsAngleParam`,
  `DisplayOrder`. **W4g: `CalculateTotalLength` zaokrągla także prosty 00 w górę do 25 mm** (kl. 7.2; A bez zmian; tolerancja
  1e-9 kroku na szum z obrysu — **W5: dla wszystkich kodów**, np. 12 H12 115/920/920 = 625, nie 650). **W5:** 27 / 28 =
  A + B + (C) − 0.5r − d (Table 5; dawniej −2d); długość ≤ 0 / NaN / nieskończona → `InvalidBarLengthException`
  (= „brak długości” u wywołujących, `BarShapeValidation` — ostrzeżenie „Brak długości: …”).
- `BarShapeValidation` (W4f, bez Teigha) — `Check(code, pv, d)`: wyłącznie OSTRZEŻENIA BS 8666:2020 — wymiar zerowy (kl. 8.1.4,
  poza opcjonalnymi), brak wymiarów kąta, wolne końce ≥ P (Table 2 — kolumna wg kąta zagięcia końcowego: 51 / 63 z hakami 90°
  „links”, pozostałe — także 47 z hakami 180° — „general bend”), 12 / 13 / 22 / 33 wg Table 5
  (q ≤ pętla ≤ 400 + 2d, ramiona ≥ B/2 + max(5d, 90) itd.), hak 47 / 51 / 63 ≤ boku (przy pustym C — hak minimalny C_eff),
  kl. 8.4: prosty odcinek osi między łukami dwóch zagięć ≥ 4d (np. 22 narożnik–półkole), Table 4 (wymiar między dwoma
  zagięciami ≥ 10d min. 75 / 13d: 21 B, 23 B, 44 B–D, 46 C, 51 / 63 A, B), korby kl. 8.2.4 (26, 46: skos, 46: przesunięcie
  D ≥ 2d), co 5 mm (kl. 7.2), krótszy bok obrysu ≤ 2750 i długość > 12 m (kl. 7.9). Dialogi RC_BAR / RC_EDIT_BAR i picker —
  TextBlock „Uwagi BS 8666:2020” (OK nie blokowane); RC_BBS — ostrzeżenia „Pozycja NN (kształt KK): …” (bez duplikatów). `BarShapePreviewRenderer`: kształty zamknięte / zachodzące wg normy 33 51 52 56 63 64 75
  (Polygon tylko, gdy oś wraca do startu).
- `BarScheduleEngine`, `BbsCounter` — zestawienie (dialog + CSV). W5: długość cięcia z jednej funkcji
  `BarLengthRules.CuttingLength` (zestawienie i tonaż RC_COUNT_BBS — masa z długości cięcia, nie z A). Błąd długości pojedynczej pozycji (W4b) → wiersz
  „brak długości” (`CuttingLength` = NaN, masa 0) + ostrzeżenie `[RC_SCHEDULE] UWAGA`, reszta zestawienia dalej. `BbsDrawingReader`, `BbsXlsGenerator`, `BbsModels`,
  `BbsSettings` — BBS .xls na szablonie Speedeck (`RC_BBS`, sekcja 4e).
- `PunchingTagEngine` — tagi przebicia (numery 501+).
- Holbrook (POPRAWKI_WZORCA 24–26, czyste, testy jednostkowe): `MTextParagraphs` — akapity `MText.Contents` (etykieta płyty z nagłówka map = pierwszy akapit; `MText.Text` w BricsCAD skleja akapity), `OutlineAxisRules` — osiowość obrysu płyty z tolerancją **4 mm** (decyzja użytkownika 2026-10-09), AutoRebar liczy na kopii wyprostowanej (`GeometryHelper.SquaredOrSame`; obrys idealny — te same wierzchołki, obrys w rysunku bez zmian); `NibDetector.Detect` i replika `NibLoopBuilder.LooksLikeNib` prostują obie polilinie nibu tak samo przed analizą (współrzędne nibu wyprostowane), RC_SECTION — też; osiowość = odchyłka krawędzi ≤ 4 mm ORAZ rozrzut wspólnej współrzędnej grupy wierzchołków ≤ 4 mm, `BbsCountRules` — RC_COUNT_BBS z wierszy RC_BBS (`BbsDrawingReader.ReadBars`) + stare RC_BAR.
- `NibStepRules` (POPRAWKI_WZORCA 29, krok K1, bez Teigha, testy `NibStepRulesTests`) — liczba prętów góry / UB 01/02 na krawędzi
  uskoku (reguła ± 1, wolny koniec nibu zawiniętego), plan pasów (`OwnerOf`, `StripPositions`, `UbPositions` — każdy pręt górny ma swój UB),
  bezpiecznik rozstawu < 100 mm. Wołane z `AutoRebarEngine.StepEdgeRuleCount` / `PlanNibStepBars`.
- `BarLabelRules` (krok K3, bez Teigha, testy w `BeamGeometryTests`) — liczba prętów w tekście opisu (`CountText`: zwykle EffectiveCount,
  belka BM „2x4”, K4: BS „2x{n}” (boki × liczba na bok, także „2x1”), BC (UB SIDE) „{n}x1”), kody z krotnością zachowywaną po gripie
  (`KeepsMultiplicity`: BM, BS, BC). `BarData.CountText`.
- `RectilinearPolygons` (K7, bez Teigha, testy `RectilinearPolygonsTests`) — wielokąty osiowe: różnica i suma prostokątów (siatka komórek,
  pętle CCW / otwory CW, współrzędne wejścia bez arytmetyki), odcinki zbioru pętli na prostej, różnica odcinków, części krawędzi poza
  obszarem (`UncoveredParts`, obszar domknięty). Używane przez E2 (siatka przy belce) i K8 (suma prostokątów stref).
- `PlotLabelRules` (POPRAWKI_WZORCA 22, bez Teigha, testy `PlotLabelRulesTests`, `PunchingReportParseTests`) — nazwy płyt
  „PLOT …” / „GARAGE …”: opis w GA (`IsPlotLabel`), nagłówek raportu przebicia (`TryParseReportHeader`; PLOT — dawna logika 1:1),
  klucz płyty RC_PH (`IsReportPlotKey`), mapa ↔ płyta w AutoRebar Add (`MapPlotMatches`, POPRAWKI 28: prefiks + granica — następny znak nie cyfra ani „-”). `PunchingReport.Parse` (wiersze arkusza → płyty) wydzielone z `Read`.
- `NibLoopBuilder` (POPRAWKI_WZORCA 23, bez Teigha, testy `NibLoopBuilderTests`) — automat domykania nibu: otwarte kawałki
  linii uskoku + obrys → zamknięta pętla (sekcja 4 „Nib”); `LooksLikeNib` = replika kryteriów `NibDetector` (zmiana kryteriów
  w jednym miejscu → w obu). `NibAutoClose` — zapis (Teigha): `TryClose` (Prepare GA), `EnsureForCommand` (komendy);
  `WouldClose` — ta sama analiza bez zapisu (RC_SECTION_CHECK: otwarty nib do domknięcia).
- Bezpiecznik przekrojów (sekcja 4g): `SectionFingerprint`, `SectionRecord`, `SectionLabelCarry` (bez Teigha, testy
  `SectionFingerprintTests`, `SectionRecordTests`, `SectionLabelCarryTests`, złoty test planera `SectionPlannerGoldenTests`),
  `SectionStore` (odczyt przekrojów, statusy, chmurki, `CurrentCut`), `SectionGuard` (`ConfirmBeforeOutput` — RC_BBS; etap 2:
  wydruk / PDF / PUBLISH i komunikat przy otwarciu — rejestrowany w `PluginApp`), `SectionUpdater` (RC_SECTION_UPDATE, adopcja).
- Belki (sekcja 4i): `BeamGeometry` (bez Teigha, testy `tests/BeamGeometryTests.cs`, `BeamK4K6Tests`, `RectilinearPolygonsTests` — geometria stref 319 / 528-529 / 539;
  K4 `PlanSideUBars`, K7 `MeshCutRects` / `UbExclusionRects`, K8 `ZonesFromThk` / `ZoneFromNibEdge`):
  odcinki strefy (`Analyze`: pary lic osiowych → prostokąty → odcinki; końce Edge / Internal / Corner (L) / Tee; skos / zmienna
  szerokość = „detal ręczny”), `StirrupLayout`, `PlanBars`, `PlanLayer` (pręty proste), `PlanVerticalUBars` (K3), wymiary strzemienia 51
  (`StirrupHeightCovers` — K2), `ParseThk`, `HeightFromLevels`, kody BM / BU / BB / BT / BL / BS / BN;
  `BeamParams` (parametry z wartościami domyślnymi „do potwierdzenia”). `BeamStore` — rekord RC_BEAM, znacznik RC_AUTOREBAR_BEAM,
  warstwa RC-BEAM, wyszukiwanie stref płyty i prętów belki, sprzątanie przy ponownym Prepare GA. `AutoRebarEngine.Beam` — generowanie
  (`GenerateBeams`), RC_BEAM (`PrepareBeamZone`, `CreateZoneFromEdge`, `SaveBeamParams`), rzut partnerski (`FindPartnerView`).
  K9: `AutoRebarEngine.BeamDetail` — detal belki 1:25 (`PrepareBeamDetail` / `FinishBeamDetail`, `BeamDrawTarget`); `BeamDetailRules`
  (bez Teigha, testy `BeamDetailRulesTests`) — okno detalu, przycinanie Liang–Barsky, miejsce detalu, detal → rzut; `BeamStore` — rekord
  RC_BEAM_DETAIL (`TagDetail` / `ReadDetail` / `FindDetail` / `EraseDetail`).
- `PositionCounter` — **jedyny** przydział numerów pozycji. `PositionReconciler` — numer po zmianie wymiarów.
- `XLink` — powiązania handle. `NumberParser`, `GeometryHelper`, `LayerManager`, `Log`, `DocumentWatch`.
  `Log.Error` (K2, 96809d3): ten sam błąd (miejsce + typ wyjątku) najwyżej raz na 60 s. `ScriptMode` — tryb bez okien (K4).

### `src/Core` — reakcje na edycję (event-driven)
- `DocumentWatch` — rejestruje handlery **per dokument** (każdy otwarty rysunek). `IsUndoCommand`.
- `BarGeometryWatcher` — stretch/move pręta → długość w XData, numer pozycji, rozkłady, etykieta za prętem.
  Po zmianie A (ścieżka 00, RC_UPDATE_BAR) TotalLength wg kształtu (`BarLengthRules.TotalLengthFor`); długość nadpisana
  (`LengthOverridden`) zostaje + ostrzeżenie `[RC AUTO] … ręcznie nadpisaną długość całkowitą …` (poprawka #7).
  `Suppress()` (IDisposable, zawsze w `using`) wycisza watcher na czas zmian pręta przez komendę wtyczki — `BarEditApply.Apply`
  (RC_EDIT_BAR), RC_UPDATE_BAR; `IgnoreThisCommand(id)` pomija pręt w CommandEnded także, gdy trafił do kolejki
  wcześniej w tej samej komendzie (poprawka #1: wzorzec doliczał zmianę długości drugi raz). Grip/STRETCH użytkownika — bez zmian.
- `CloneMap` (W2, poprawki #3, #6, 3b; = 3e3 z PLAN_C_etap3) — zapisuje paczki klonowania z `BeginDeepCloneTranslation`
  / `InsertMappingAvailable` (konteksty Copy, Insert, InsertCopy; deduplikacja; `DeepCloneAborted` odrzuca ostatnią) —
  handler BEZ zapisów do bazy i bez otwierania obiektów. Wblock z komendy schowka (COPYCLIP / CUTCLIP / COPYBASE) → mapa
  schowka (handle w pliku schowka → oryginał) + rysunek źródłowy (wskaźnik bazy natywnej); zwykły WBLOCK jej nie nadpisuje.
  Kontekst Objects (DeepCloneObjects wtyczki, np. EnsureUniqueBtr) — pomijany; `Suppress()` (zawsze w `using`).
  Mapa schowka działa tylko dla paczki komendy wklejania, której klucze par głównych są w mapie schowka (INSERT nigdy);
  rysunek źródłowy = Guid czasu życia dokumentu (niezależny od SAVEAS); zamknięcie rysunku unieważnia tylko „żywe źródło”,
  mapy schowka zostają do następnego COPYCLIP / CUTCLIP (wklejenie do innego rysunku nadal tłumaczy stare 1000).
  Łańcuch paczek (`ChainOf` / `ResolveInChain`): klon, którego klucz jest klonem wcześniejszej paczki tej komendy
  (PASTECLIP: plik schowka → blok pomocniczy → model; COPY A→B, potem B→C w jednej komendzie nadrzędnej); stare odwołania
  tekstowe 1000 (BricsCAD ich nie tłumaczy) — przez klucze korzenia (plik) albo mapę schowka, wynik zapisywany jako 1005.
  `Take(db)` / `Forget(db)` (UNDO/REDO, CommandFailed, zamknięcie rysunku). Zapytania w trakcie komendy:
  `IsPendingCloneRetarget` (strażnik kaskady usuwania pręta), `TryResolvePending` (strażnik kaskady usuwania rozkładu).
- `CloneRemap` — przepięcie powiązań klonów w CommandEnded wg reguł planu 4.3 (docs/PLAN_POPRAWKI_W1_W2.md):
  Cloned / AlreadyClone / NotCloned; pręt [13] tylko para dwustronna z etykietą (#6), etykieta dla > 1 pręta → nikomu;
  rozkład [18] → klon pręta (#3), [11] / opis [16] → klony (3b); RC_BAR_LINK [1] → klon pręta; „ten sam rysunek” (COPY,
  wklejenie ze schowka skopiowanego w tym rysunku = COPY) — wartość obiektu źródłowego; inny rysunek — "". **Znacznik płyty**
  (K5, poprawka 3c): `RC_AUTOREBAR_SLAB` [1] rozkładu i `RC_ADD` [1] wymiaru ADD → klon obrysu płyty, gdy płytę skopiowano
  razem (cel = Polyline); płyta nieskopiowana (COPY samych rozkładów / wymiarów) → "" (decyzja P4b) — przynależność
  rozstrzyga test punktu w obrysie. **Belki** (plan belek E1, jak K5): strefa belki (rodzaj BeamZone) `RC_BEAM` [6] → klon obrysu
  płyty, gdy płytę sklonowano razem, inaczej ""; rozkład belki `RC_AUTOREBAR_BEAM` [1] → klon strefy, inaczej "". **Przekroje** (bezpiecznik, rodzaje SectionHead / SectionPart): tytuł sklonowany z ≥ 1 częścią
  → samodzielny przekrój (nowy GUID, lista części = tylko sklonowane części — handle klonów, [6] = własny handle, [4] / [5] → klon
  obrysu, gdy płytę sklonowano razem, inaczej ten sam rysunek bez zmian / inny ""); tytuł bez części → zwykły tekst (bez
  RC_SECTION i Xrecordu); część z tytułem sklonowanym razem → [1] klon tytułu, [4] jego GUID, bez tytułu → zwykły obiekt (RC_SECTION_PART zdjęte — decyzja P-b; CHECK
  „części bez tytułu” tylko po usunięciu tytułu). Lista części w Xrecordzie to tekst — tłumaczona jak stare 1000. Klony w definicjach
  bloków pomijane. Zapis: pręt — `BarEntity.Write`, rozkład — `BarBlockEngine.WriteXData`, opis / etykieta / RC_BAR_LINK —
  punktowa podmiana pola `XLink.Write`. Pary przepięte po komendach kopiowania — `BarCopyWatcher.NormalizeAndRebuildPair`
  (odbicie → skala +1, przebudowa linii rozkładu, jak wzorzec).
- **MIRROR rozkładu z opisem (decyzja Bartka 2026-10-22):** opis odbija się razem z prętami — linia rozkładu na odbitych prętach,
  odnośnik po drugiej stronie (lustrzanie), tekst ZAWSZE czytelny (nie lustrzany; `AnnotationEngine.NormalizeMirroredAnnot`).
  Kopia (usunięcie źródła = Nie) — `BarCopyWatcher.NormalizeAndRebuildPair`; odbicie w miejscu (usunięcie źródła = Tak) —
  `BarBlockTransformOverrule` / `AnnotTransformOverrule` kolejkują odbite obiekty, po komendzie `ApplyPendingMirrors`
  (rozkład jak kopia: skala +1 + `ApplyMirror`; opis niezaznaczony odbijany razem z prętami). Pionowo i poziomo tak samo.
- `BarCopyWatcher` — po komendzie (CommandEnded/Cancelled najwyższego poziomu; komenda widoku / przezroczysta /
  zagnieżdżona — nic, paczki czekają na komendę nadrzędną; zagnieżdżona komenda kopiowania przenosi kwalifikację „kopiowanie”
  na komendę nadrzędną): 1) UNDO/REDO → `CloneMap.Forget`; 2) `CloneMap.Take`;
  3) komendy kopiowania (COPY, MIRROR, ARRAY / -ARRAY, PASTE*) → unikalne BTR (rozkłady, opisy, pręty-bloki `*U`);
  4) `CloneRemap.Apply` (każda komenda, która sklonowała obiekty); 5)–6) heurystyki wzorca tylko dla obiektów spoza mapy;
  7) komendy kopiowania: kopia pręta (polilinia / pręt-blok) o tym samym numerze i kształcie co pręt spoza kopii →
  komunikat `[RC] Kopia pręta … ten sam numer pozycji …` (K4, e7b600d; tylko odczyt, bez prętów bez numeru, usunięte
  pomijane; `LastSameNumberCopyMessages` — testy).
  Ograniczenie BricsCAD: REDO po COPY nie ponawia zmian z CommandEnded na klonach (przepięcie, unikalne BTR) — jak we wzorcu.
- `AnnotMoveOverrule` — gripy rozkładów/opisów (Grip/Transform/Erase overrule). Kaskada usuwania pręta
  (`BarEraseCascade.Run`: etykieta z back-linkiem, rozkłady wskazujące na pręt i ich opisy) — wołana przez
  `BarPolylineEraseOverrule` (polilinia) i `SingleBarBlockEraseOverrule` (pręt-blok v2; nie mylić z `BarBlockEraseOverrule`
  = usunięcie rozkładu). Pręt bez `LabelHandle` też usuwa swoje rozkłady i opisy (poprawka #2; wzorzec je zostawiał);
  etykieta — tylko z back-linkiem; opis rozkładu (w kaskadzie pręta i przy usunięciu rozkładu) — tylko gdy back-link [16]
  opisu jest pusty albo wskazuje TEN rozkład (`BarEraseCascade.AnnotBelongsTo`, e7b600d; kopia rozkładu bez opisu nie
  kasuje opisu oryginału; opis-klon z paczki `TryResolvePending` — bez sprawdzania, ma jeszcze [16] oryginału).
  Usunięcie pręta przez UNDO / REDO (`IsUndoing`) — bez kaskady (P2). EXPLODE = ERASE. W2: rozkład sklonowany w tej komendzie razem ze swoim prętem nie jest
  usuwany z kaskadą źródła (`CloneMap.IsPendingCloneRetarget`); usunięcie rozkładu-klonu w trakcie komendy (np. `_U` w COPY)
  tłumaczy `[11]` przez paczkę (`TryResolvePending` — opis oryginału zostaje); usunięcie przez UNDO (`IsUndoing`) — bez kaskady
  rozkład → opis (powiązane obiekty odtwarza UNDO).
- `SingleBarGripOverrule` (Plan C; etap 1 — próba, krok 3f — dla użytkownika) — gripy KAŻDEGO pręta-bloku (BlockReference
  z `RC_SINGLE_BAR` [14]=2): [0] początek osi = przesunięcie; kształt 00 / 99 — także [1] koniec osi = długość wzdłuż osi
  (`SingleBarBlock.ApplyEndLength`: co 5 mm, minimum max(2d, 50) → zmiana cofnięta (pręt bez zmian), TotalLength wg kształtu, nadpisana —
  bez zmian + ostrzeżenie `[RC AUTO]`) i STRETCH oknem (koniec / początek / całość). Kształty gięte: tylko grip [0] i STRETCH
  całości; częściowy STRETCH odrzucony z komunikatem (długość przez RC_EDIT_BAR). W dragu tylko podgląd (transient, kształt
  z danych pręta), ZERO zapisów; zapis w `CommandEnded` (`ApplyLengthEdit`, w `_applying`, `LockDocument`), gdy nie trwa inna
  komenda (przezroczysty 'ZOOM nie zapisuje); po U / UNDO / REDO — nic; ESC = reset. Pręt z numerem pozycji po zapisie →
  `BarChangePropagator.Run`; pręt testowy `H12-T` (bez numeru) — bez propagacji. Jedna komenda = jeden Ctrl+Z (zmiany
  z CommandEnded w tej samej grupie UNDO; Ctrl+Y odtwarza stan po gripie). Komunikaty `[RC AUTO]`. Grip w trybie kopiowania
  (Ctrl / „Copy”): pręt-blok dopisany w trakcie dragu (`ObjectAppended`, tylko zapamiętanie) o Mark i długości pręta z pending
  → zmiana długości odrzucona (`GripCopyRefusedMessage`), kopia zostaje jak ją utworzył BricsCAD. Koniec komendy czyści
  podgląd tylko w swoim rysunku.
  Komenda testowa: `TestBarBlockCommands` (`RC_TEST_BARBLOCK`, opcje Info — opis dowolnego pręta-bloku / Typ).
  Pręt testowy `H12-T` — poza numeracją i BBS.
- `BarChangePropagator` (krok 3f) — skutki zmiany wymiarów pręta-bloku (grip / STRETCH): `PositionReconciler.ReconcileAfterGeometryChange`
  → `PropagateToDistributions(legacyPosNr)` → grot etykiety (`BarEntity.ClosestPoint`, tylko z back-linkiem) →
  `UpdateBarLabelCount` → komunikat `[RC AUTO] Pręt … → nowa pozycja …  (n rozkład(y))`. Kopia łańcucha wzorcowego
  `BarGeometryWatcher.ApplyEndStretch` (watcher prętów-polilinii bez zmian).
- `RcMLeaderOverrule`, `BarBlockHighlightManager` — zachowanie etykiet, podświetlenie obrysu rozkładu.

### Pułapki BricsCAD (znane, sprawdzone)
- **Grip drag:** BricsCAD wywołuje `MoveGripPointsAt` także na prawdziwej encji podczas podglądu →
  w trakcie przeciągania żadnych zapisów do bazy; tylko transient preview, zapis w `CommandEnded`
  (`ApplyPendingGripEdits`). ESC = brak zmian.
- **UNDO/REDO:** w `ObjectModified` pomijaj `IsUndoing`; w `CommandEnded` pomijaj komendy undo
  (`DocumentWatch.IsUndoCommand`). Żadnych cache'y stanu geometrii (po Ctrl+Z są nieaktualne).
- **Zapisy z handlerów:** licznik reentrancji (`_rebuildDepth`), żeby własne zmiany nie wracały jako eventy.
  Komendy wtyczki zmieniające pręt-polilinię: `using (BarGeometryWatcher.Suppress())` + `IgnoreThisCommand(id)`.
  Zbiór pomijanych czyszczony przy CommandEnded / CommandCancelled / CommandFailed komendy najwyższego poziomu
  (koniec przezroczystego 'ZOOM go nie czyści); CommandFailed czyści stan komendy jak anulowanie.
- **GripOverrule na natywnej `Polyline` nie działa w BricsCAD** → pręt nie ma własnych gripów.
  Stretch pojedynczego gripu psuje obrys → jest cofany; długość zmienia STRETCH oknem lub `RC_EDIT_BAR`.
  Grip / STRETCH końca pręta-polilinii skracający odcinek poniżej max(2d, 50) (poprawka #12, decyzja D4): zmiana odrzucona,
  pręt wraca do stanu sprzed przeciągnięcia — wierzchołki i wybrzuszenia w OCS zapamiętane w `ObjectOpenedForModify`
  (`_preOutlineOcs`), przywracane w CommandEnded (ta sama grupa UNDO), bez przesuwania etykiety i bez zmiany długości
  (wzorzec pisał „zmiana cofnięta”, a ścieżka 00 przyjmowała krótką długość). Długość z gripa polilinii bez zaokrąglania
  (jak wzorzec, decyzja P1; 5 mm — tylko pręt-blok).
- Pręt jako BlockReference („Plan C”, etap 3) — format Blok do wyboru (`RC_BAR_FORMAT`), domyślnie Polilinia.
  Pręt-blok ma własne gripy (`SingleBarGripOverrule`); pręt-polilinia — jak wyżej.
- **Stara wtyczka kolegi gubi ogon RC_SINGLE_BAR [14+]**: kapelusz 44 staje się pierścieniem (W4c), a pręt traci znacznik
  geometrii W4d ([15] = 2021) — długość rozkładu Manual liczona wtedy jak dla pręta historycznego (wzorzec); znacznik wraca
  przy następnej przebudowie obrysu (MOVE / RC_EDIT_BAR).

## 3. Numeracja pozycji
- **UB zawsze 01 (UB B1), 02 (UB B2) i 03 (UB w nibie).** Numeracja automatyczna od **04** (jak ASD;
  03 zarezerwowane także w płytach bez nibu).
- Pręty belki (sekcja 4i): **wszystkie w serii dolnej** `NextAuto` (≥ 04) — BM (dół = góra „B+T”), BB / BT (dół ≠ góra), BU (pionowe
  U-bary), BL (strzemiona), BS, BN — **zmiana zasady na żądanie użytkownika (K3, praktyka biura: w BBS cała belka w BOTTOM LAYER; dawniej BT
  w serii górnej 101+)**; identyczny pręt (kształt, Ø, wymiary, seria) → istniejąca pozycja — także pręt siatki (np. pręt
  boczny H10 L5500 = B1 H10 L5500 siatki, pręt w nibie H16 L5500 = BB H16 L5500 — wspólny numer; BTR rozkładu siatki dostaje wtedy
  sufiks unikalnej nazwy).
- `PositionCounter.NextAuto(db, used)` = max(licznik w rysunku, max użyty < 500) + 1, min. 4,
  pomija zajęte. Seria **501+** (dawne pręty przebicia; od 2026-10-16 przebicie w serii 101+) nie podbija licznika.
- Jeden numer = jeden kształt pręta. Pręt, któremu zmieniono wymiary, a jego numer ma inny pręt
  o innych wymiarach → dostaje numer istniejącej identycznej pozycji albo nowy (`PositionReconciler`).
  UB i 501+ nie są przenumerowywane.

## 4. AutoRebar — zasady (wymagania użytkownika)
Ogólne:
- **Add z map (2026-10-22, test6):** mapy bez XData RC_MAP (wklejone bez RC_IMPORT_MAP) — rozpoznanie po ramkach PH-FRAME i warstwach
  (`ReadUntaggedMaps`; brak PH-xx-SLAB w ramce → obrys innej mapy przesunięty o różnicę ramek). Dopasowanie mapy do płyty
  (`MatchMap`): obrys płyty Z RYSUNKU (z wspornikami), obwiednia ±100, przesunięcie z rogu obwiedni z największym udziałem wierzchołków
  mapy na obrysie płyty (≥ 50%; test6: wsporniki na mapie o 25 dłuższe — dopasowanie po górnej krawędzi). Obwiednie równe — jak dawniej.
- **Rozstaw rzeczywisty siatki i UB płyty — NAJWYŻEJ 205 mm (zasada Bartka 2026-10-22: „NIGDY więcej, jak już to mniej”):**
  `BarBlockEngine.GenerateFromBounds(maxSpacing)` z siatki (GenerateLayer) i UB (GenerateUBDistribution) — rozstaw > 205 → więcej
  prętów na tym samym zakresie (test5: 3 co 297,5 na 595 → 4 co 198,3); opis nominalny bez zmian. Bez limitu: pręty belek /
  wsporników (kody belki), B3 ADD, UB ADD. Zmiana wyniku względem wzorca tylko tam, gdzie rozstaw był > 205 (błąd).
  Krawędź zewnętrzna (AdjustedExternal): ostatni pręt > 70 od krawędzi, a zagęszczenie o 1 pręt daje < 192 — dawniej odrzucenie
  (luka przy krawędzi, test5: 3 B2 przy 4 UB 02 na krawędzi 675), od 2026-10-22 zagęszczenie do 150 (`MinDenserSpacing`, ostrzeżenie)
  — siatka, UB 02 i UB 03 tej krawędzi mają tę samą liczbę prętów. Zmiana względem wzorca (S-REG — oczekiwana różnica).
- **Opisy UB z boku płyty (2026-10-22, wzorzec Bartka test9-poprawiony):** domyślnie odnośnik wzdłuż linii rozkładu do bliższego boku
  obwiedni (także przy wnękach / uskokach obrysu). Opis prostopadle do linii rozkładu (wzdłuż prętów, za płytę) przy swoim rozkładzie tylko,
  gdy po drodze do boku leży inny UB na tej samej linii krawędzi (np. UB 03 między wspornikiem a zatoką) albo bok jest dalej niż 2500 mm
  (`SideLeaderAlongEdge`, `SideLabelMaxGap`). Linia rozkładu UB 03 blisko krawędzi: domyślnie 250 mm za krawędzią, przy kolizji najpierw
  drobne przesunięcia co 50 mm (200 / 300 / 150 / 350 …), dopiero potem dalej (`PlaceLabel(alongTries)`; wcześniej skoki 595 / 940 / 1285).
- **U-bary (kształt 21) — długość całkowita w module 50 mm (decyzja Bartka 2026-10-23, wsporniki i belki):** ramiona A = C w górę co 5 mm
  (zakład tylko rośnie; 1275 / 175 → 1285: 2675 → 2700), B bez zmian; ramię ograniczone geometrią — w dół (`BeamGeometry.UBarArmForModule`,
  `PlanVerticalUBars`, `CantileverRules.PlanBars`). UB SIDE i UB płyty — bez zmian. Zmiana wyniku belek względem wzorca (S-REG — oczekiwana).
- **RC_DISTRIBUTION — otuliny (decyzja Bartka 2026-10-23):** końce prętów (wzdłuż pręta, od klikniętej krawędzi) — ZAWSZE 40;
  prompt „Position of first bar along edge” — tylko położenie PIERWSZEGO pręta (przypięty, grip [0]; `BarData.Cover`); drugi koniec
  wolny — przy tworzeniu 40, dalej dowolnie gripem (grip [1] i obrys o otulinę końców, nie o Cover);
  otulina końców w XData [33] `CoverEnd` (gripy i obrys podświetlenia wzdłuż pręta — `EndCover`; stare rozkłady — jak Cover).
- **Widoczność prętów i opis rozkładu (prośba Bartka 2026-10-28):** RC_EDIT_LABEL i RC_EDIT_DISTRIBUTION → „Dodaj opis rozkładu”
  (okno „Reinforcement description” — `DistributionDescDialog(askVisibility)`): kolejność Manualny, Wszystkie, Środkowy, Pierwszy+Ostatni,
  domyślnie ZAWSZE Manualny; po opisie — `EditCommands.ApplyDistributionVisibility` (Manualny: wszystkie widoczne na czas klikania).
  Podgląd kropek w jigach opisu do 200 prętów (`JigPreview.MaxDots`, dawniej 20 — podgląd „w połowie rozkładu”). ETAP 3 odnośnika
  (wszystkie rozkłady): przed pierwszym kliknięciem stopa na linii rozkładu idzie za kursorem (prostopadle, w granicach rozkładu),
  bez gumki BricsCAD od stałego punktu; rozkład obrócony / ukośny — bez zmian.
- **RC_EDIT_LABEL — liczba „kxn” (decyzja Bartka 2026-10-23):** wpis „2x5” (też 2X5, 2×5, 2*5) → opis „2x5”, BBS liczy 10
  (CountDisplay = 10, `CountMult` = 2 — XData rozkładu [34], opisu [24]; `BarLabelRules.ParseCountInput`); krotność zostaje po gripie.
- **Bez kolizji — wymiar „wchodzący w środek” zawsze W DÓŁ (zasada ogólna Bartka 2026-10-22, KAŻDA sytuacja na płycie):**
  element, który musi się zmieścić w innym (strzemię drugiego kierunku w strzemieniu, U-bar w strzemieniu, noga w nodze…), ma
  wymiar = miejsce w środku (np. A − 2·Ø) zaokrąglony **w dół** do 5 mm — lepiej mniej wysokości niż kolizja (290, H12 → 266 → 265;
  215 → 191 → 190). Odejmowane naddatki (np. 2·Ø U-bara przy zagnieżdżeniu) — w górę. Wymiary, które muszą OBJĄĆ pręty
  (szerokość strzemienia 4-leg `FourLegWidth`) — w górę.
- **B3 ADD (decyzja użytkownika D1, 2026-10-09; f854684):** w każdym narożniku wklęsłym płyty i w 4 narożnikach otworu
  (detal) 2 H10 co 100, L = 1250, opis „2 H10-nn-100 B3 ADD” — szczegóły w sekcji 4h. Zmiana wyniku względem wzorca
  aba0ed1 przyjęta (expected-diffs 14).
- Program **sam tworzy pręty-szablony** (strefa szablonów obok płyty: proste od lewej, UB od prawej). Etykieta szablonu UB:
  tekst nad środkiem rzeczywistego obrysu, grot na lewym ramieniu (jak wzorzec; W4e runda 1).
  Nie wymagamy „podkładki” rysowanej ręcznie.
- **Siatka długości 250 mm. NIE dopuszczać prętów co 50 mm.**
- Rozstaw nominalny 200 mm, otulina 40 mm. **Opisy zawsze z rozstawem nominalnym** (np. „-200”),
  nie rzeczywistym po korekcie.
- Pręt min. **2500 mm**, chyba że geometria płyty wymusza krótszy (wtedy min. 1250). Max 6000.
- **Mniej zakładów zdecydowanie lepiej.** Brak mijania zakładów w obrębie jednej warstwy.
- Opisy rozkładów nie mogą na siebie nachodzić (`PlaceLabel`, odstęp 150 mm); we wszystkich generowaniach tekst nie leży
  na widocznym pręcie, a leader nie biegnie wzdłuż innej linii opisu (≤ 60 mm).
- **Rozkład z jednym prętem: opis bez rozstawu** (np. `1 H12-01 UB`, nie `1 H12-01-200 UB`) — `ApplySingleBarMark`.
- **Pręty podłużne belki (K3): opis z rozstawem NOMINALNYM w poprzek** ([28] = 150, geometria — rzeczywisty rozstaw, ostrzeżenie gdy
  > 175): `2x4 H16-nn-150 B+T` (BM, CountDisplay = 2 · 4 — `BarLabelRules.CountText`), `4 H16-nn-150 B` / `4 H16-nn-150 T` (dół ≠ góra),
  pionowe U-bary `4 H16-nn-150 UB`; strzemiona z rozstawem nominalnym (`52 H12-nn-200 LINKS`); pręty boczne / w nibie — bez rozstawu
  (`noSpacingMark`). Tekst liczby we WSZYSTKICH miejscach składania opisu (`AnnotationEngine` — opis i jego długość, `PlaceLabel`
  w `AutoRebarEngine`, RC_EDIT_LABEL) — `BarData.CountText`; grip rozkładu zachowuje krotność BM / BS.
- **Kody warstw prętów:** siatka B1 / B2 / T1 / T2, B3 ADD — B3, belki — **BM** (dół = góra, jedna pozycja „B+T”, K3), **BB** / **BT**
  (dół ≠ góra; od K3 oba na rzucie dolnym w serii dolnej), **BU** (pionowe U-bary kształt 21, sufiks „UB” — NIE UB płyty: skany UB płyty
  porównują kod B1 / B2, `ReadUb03` i `ScanUBTemplates` pomijają kody belki), **BL** (strzemiona), **BS** (pręty boczne, E4b), **BN** (pręty
  wzdłuż nibu belki, E4b); pręty L i poziome U-bary E4 — usunięte w K3 (stare rozkłady usuwane przy ponownym generowaniu). Kody belek
  NIE zaczynają się od „B1/B2/T1/T2”: skany siatki (`ScanOldDistributions`, `EraseOldDistributions`, `FindBottomView`, UB, ADD) porównują
  kod dokładnie, więc prętów belki nie widzą. Gałęzie `StartsWith("B"/"T")` przejrzane: `BuildBarData` — BT = TOP; `CutBarPositionsAtHole`
  — bez kodów belek; `SectionPlanner` / `PrepareSection` — pręty belek jako rodzaj `Beam` (tylko w pasie belki przeciętym linią cięcia;
  poziomy siatki dB1 / dT1 liczone wyłącznie z B1 / T1 — sekcja 4g); `ResolveRepresentativeCollisions` — pręty belek pomijane
  (`IsBeamCode` — obejmuje BM / BU / BC i stare BB / BT / BS / BN); `GenerateLayer` / `CreateNewTemplate` / model 3D — tylko kody siatki. Warstwa
  CAD: BT → RC-SLAB-TOP-01 (także na rzucie dolnym od K3 — warstwa góry), BM / BU / BB / BL / BS / BC / BN → RC-SLAB-BOT-01 (`BuildBarData`: kod
  na „B” poza BT = BOT). **K4: BC** — poziomy U-bar „UB SIDE” (kształt 21) zamykający pręty boczne BS; opisy: BS „2x{n} H12-nn SIDE”, BC
  „{n}x1 H12-nn UB SIDE”, pręty w nibie (decyzja 2026-10-11) BN „2+1 H10-nn T+B IN NIB” (dawniej K5 — „1 H10-nn B IN NIB”).

Dół (B1/B2, Ø10):
- Zakład 400–650 mm, preferowany **450–550**.

Góra (T1/T2, Ø12, te same parametry co dół poza średnicą):
- Zakład min. 500, preferowany **550–650**, górna granica 700 (przyjęta — do potwierdzenia).
- **Ta sama liczba prętów na danej krawędzi co dół** (liczba prętów w rozkładzie / rozstaw). Liczba odcinków
  wzdłuż pasa (rozkładów, zakładów) góry MOŻE być inna niż dołem — np. 4 pręty zamiast 3, żeby zakłady ominęły pale.
- Zakłady góry ≥ 500 mm od lica pali (SD-Pile); gdy wzór „pierwszy, środkowe równe, ostatni” przy żadnej liczbie odcinków
  (min … min+4) nie pozwala — **pręty różnej długości** (siatka 250, każdy zakład osobno 500–700, `TopLapSearch`, decyzja
  2026-10-15: płyta z 14 palami miała zakład na palu); dopiero gdy i to nie pozwala — zakład najdalej od pali + ostrzeżenie.
  **„Najpierw góra” (decyzja 2026-10-15):** przy palach DÓŁ (B1/B2) wybiera plan, przy którym zakłady góry mogą ominąć pale
  (`ComputeJointPlan` z zakazami pali w układzie góry — przy nibie góra krótsza o szerokość nibu z każdej strony), także kosztem
  większej liczby prętów dołu; bez pali — bit w bit jak dawniej. Góra dalej liczona względem rzeczywistego dołu.
  **„Najpierw góra” do końca (decyzja Bartka 2026-10-20):** gdy żaden dół ze wzoru nie pozwala — `TopLapSearch.TopFirst`: góra swobodnie (pręty różnej długości, zakłady 500–700 ≥ 500 od lica pali, bez dołu), potem dół swobodnie (zakłady 400–650 ≥ 750 od zakładów góry, bez pali, do nB0+5 prętów). Przykład Drawing2 (pale co ~2250): wcześniej zakład góry 93 mm od lica i pręty 5750/3250/3250/3250/5750; plan awaryjny „najdalej od pali” dopiero gdy i to nie wychodzi.
- **Zakłady góry ≥ 750 mm w świetle od zakładów dołu.**
- Góra i dół są na **osobnych rzutach** (dwa obrysy tej samej płyty). Góra liczona względem
  **rzeczywistych** rozkładów dołu: najpierw ten sam obrys, potem obrys przystający (przesunięty)
  z rozkładami B1/B2. Brak dołu → plan teoretyczny (`ComputeJointPlan`) + komunikat.

UB (Ø12, pozycje 01/02):
- **Jeden rozkład na krawędź, jeden opis.** Liczba UB = liczba prętów głównych dochodzących do tej krawędzi.
- Krawędź zewnętrzna wykrywana przez próbkowanie obrysu.
- **Każda krawędź obrysu ma UB**, także krótkie uskoki (np. 60 mm) — bardzo krótka: 1 UB w środku krawędzi.

Geometria:
- Pas z jedną krawędzią zewnętrzną i jedną wewnętrzną (np. przy uskoku): pierwszy pręt na otulinie 40 od
  krawędzi ZEWNĘTRZNEJ, rozstaw nominalny, resztka do środka płyty — o ile odstęp do pręta sąsiedniego pasa ≤ rozstaw
  (inaczej dotychczasowe dopasowanie rozstawu).
- Obrys → pasy (`DecomposeStrips`, scanline po rzeczywistym wielokącie; wiele przęseł; ukośne
  krawędzie w pasmach 1000 mm). Pierwszy pręt: otulina przy krawędzi zewnętrznej, s/2 przy wewnętrznej.
- Rozkłady znakowane obrysem płyty (`RC_AUTOREBAR_SLAB`) — ponowne generowanie usuwa tylko rozkłady
  tej płyty i tej warstwy. COPY płyty z rozkładami: znacznik kopii → obrys kopii (`CloneRemap`, K5 — wcześniej kopia
  wskazywała oryginał i ponowne generowanie na oryginale kasowało rozkłady kopii); COPY samych rozkładów → znacznik "" (test
  punktu w obrysie). Tak samo wymiary ADD (`RC_ADD` [1], odczyt `XLink.Read`). Znacznik rozstrzyga też `CollectBottom`
  (rzut dolny dla góry T1/T2) i RC_SECTION (`AutoRebarEngine.Section`): po COPY płyty rozkłady kopii należą do kopii —
  rzut dolny / przekrój oryginału ich nie liczy, kopii — liczy (wzorzec przypisywał je oryginałowi).

Nib (uskok przy krawędzi, wys. 150, szer. zwykle 115–135, zawsze < 255 mm) — `NibDetector`:
- Wykrycie: druga zamknięta polilinia wewnątrz obrysu, równoległa do krawędzi w odległości 50–255 mm na jakiejś
  długości (linia uskoku); gdzie nibu nie ma, pokrywa się z obrysem. Można wskazać obrys albo linię uskoku.
- **Automat domykania (POPRAWKI_WZORCA 23, decyzje użytkownika 2026-10-09):** gdy `NibDetector.Detect` nie znajduje
  zamkniętego nibu (bramka — zamknięty nib bit w bit bez zmian), OTWARTA linia uskoku (polilinie / linie / łuki na warstwie
  obrysu, końce na obrysie, np. z GA) jest domykana krawędzią obrysu: zamknięta polilinia na warstwie obrysu (płyta jak w GA,
  bez offsetu; zawinięcia nibu na sąsiedniej krawędzi — dosłownie z GA), użyte kawałki usunięte. `NibLoopBuilder`: kawałek
  w pasie nibu = WSZYSTKIE próbki (wierzchołki + środki odcinków) ≤ 256 mm od obrysu i w obrysie (albo ≤ 4 mm od niego);
  efektywnie zamknięte (progi drzwi), leżące na obrysie i dalsze (linie strefy 760 mm) — pomijane; przerwa ≤ **4 mm**
  (między kawałkami: przecięcie prostych / środek; do obrysu: przecięcie z bokiem / rzut); rozgałęzienie, przerwa > 4 mm,
  koniec na łuku obrysu, przecięcie obrysu, zachodzące linie, pas > 0,4 pola obrysu, linia poza 50–255 mm → komunikat
  „Nib: nie domknięto — … Płyta bez nibu.”; 0 kawałków w pasie — cisza. Zapis tylko gdy `LooksLikeNib`; po zapisie `Detect`
  ≠ null, inaczej wycofanie (polilinia usunięta, kawałki przywrócone, `Log.Error`). Wołane: Prepare GA (`GaImportEngine.CloneInto`,
  PRZED `JoinLoops`), AutoRebar Mesh (oba rzuty, po obu wskazaniach) / B1 / B2 / T1 / T2 / UB1 / UB2 / UBNib (`[AutoRebar] Nib: …`;
  obrys rysowany — pomijany), RC_SECTION (`[RC SECTION] Nib: …`); B3 i Add — nie. Zapis w grupie UNDO komendy (Ctrl+Z cofa);
  domknięcie zostaje, gdy dalsze generowanie się nie uda. Ograniczenie: styk „T” (koniec kawałka w środku boku innego) — komunikat o przerwie.
- **Dół (B1/B2) do krawędzi zewnętrznej** — pręty dolne mieszczą się w nibie.
- **Góra (T1/T2) do linii uskoku** — pręty górne nie mieszczą się w nibie, więc liczba prętów góry na krawędzi
  wynika z obrysu uskoku (np. nib z obu stron: dół 31 → góra 29).
- **UB 01/02 na uskoku = UB 03 krawędzi zewnętrznej ± 1 na każdy koniec**: narożnik wypukły (uskok krótszy) −1,
  wklęsły (uskok dłuższy, np. wcięcie) +1, ta sama długość → ta sama liczba; rozłożone równo na uskoku.
  Przykład: krawędź z nibem po obu stronach (wypukłe) 57 → 55, wcięcie 8 → 10, uskok 1 wypukły + 1 wklęsły 13 → 13.
  **Wolny koniec (POPRAWKI_WZORCA 29, K1 — błąd wzorca):** koniec krawędzi uskoku leżący wewnątrz zakresu krawędzi zewnętrznej (> 1 mm od jej
  końców), gdy sąsiednia krawędź linii uskoku w tym wierzchołku NIE jest krawędzią nibu (nib zawinięty 600 mm na sąsiedniej krawędzi, nib
  w środku krawędzi) — pręty dołu liczone tylko do końca uskoku, bez ± 1 na tym końcu (`NibStepRules`; wzorzec liczył całą krawędź
  zewnętrzną: 321 — 33 UB na 643 mm). Rozkład UB / prętów góry na uskoku o rozstawie < 100 mm — `*** WARNING ***`.
  Koniec NIE jest wolny także przy **wierzchołku współliniowym** linii uskoku (w końcu styka się krawędź nibu tej samej orientacji
  i InnerCoord — `NibDetector` nie scala odcinków; wynik jak wzorzec). **Odsadzka linii uskoku** (zmiana szerokości nibu wzdłuż tej
  samej krawędzi zewnętrznej): jak wzorzec, chyba że wzorzec daje rozstaw < 100 mm — wtedy koniec przy odsadzce wolny
  (`NibStepRules.ClassifyEnd` / `RuleCountOnEdges`; pytanie do użytkownika — POPRAWKI 29).
- **T1/T2 = UB 01/02 — każdy pręt górny ma swój UB** (`PlanNibStepBars`, wspólny dla góry i UB): pas góry przy nibie bierze
  **położenia RZECZYWISTYCH prętów dołu** leżących w pasie góry (linia uskoku ± otulina; decyzja Bartka 2026-10-22, test8: „musi być
  tyle samo”, „2 mniej T1 niż B1”) — pręty dołu w nibie odpadają, rozstaw jak dołem; góra = dół − 1 na każdy koniec z nibem, UB 01/02 =
  UB 03 przy tej samej długości odcinka (reguła ± 1 wychodzi sama). Bez rzutu dolnego — położenia z krawędzi uskoku na końcu prętów
  (reguła ± 1 powyżej, równo na uskoku). Pas przy belce (E2) — bez zmian. UB na krawędzi = pręty góry wszystkich pasów dochodzących do
  niej (długa krawędź przez kilka pasów = suma pasów).
- **UB 01/02 na uskoku**, **UB 03 przy krawędzi zewnętrznej** (liczba jak dół):
  H10, shape 13, 610-70-610, @200 — `RC_GENERUJ_UB_NIB` (przycisk) i w `RC_GENERUJ_SIATKA`.
- **T IN NIB** (z T1/T2): przy każdej krawędzi z nibem 2 pręty H12 co 150 wzdłuż krawędzi — pierwszy w nibie
  (otulina 40 od krawędzi zewnętrznej), drugi 150 dalej, za uskokiem. Długości jak góra (siatka 250, maks. 6000,
  zakłady 500–700), pozycje wspólne z serią 101+. Opis `2 H12-101-150 T IN NIB`.
  **Przy belce (decyzje użytkownika 2026-10-11: „w nibie 2 pręty w górze nibu i 1 na dole”, potem „rób jedną pozycję bottom in nib i top,
  wtedy mamy 3 pręty te same”):** krawędź, której linia uskoku leży w > 50 % w obszarze belki (`BeamMeshRegion` / `UbCuts`, kryterium jak
  `PlanNibStepBars`) — **bez T IN NIB** (komunikat `[AutoRebar] T IN NIB: krawędź … przy belce — bez T IN NIB …`); pręty w nibie belki
  generuje RC_GENERATE → Beam JEDNĄ pozycją (sekcja 4i, „Pręty w nibie 2T+1B”): 3 identyczne pręty H10 — 2 górą pod górnym ramieniem UB 03
  (w narożu zagięcia UB 03: krawędź + 40 + Ø UB 03 10 + d/2 = 55; przy licu belki: linia uskoku − 40; odstęp min. 40) + 1 dołem w dolnym
  narożu UB 03 (pod prętem górą przy krawędzi), opis `2+1 H10-nn T+B IN NIB`. Zastępuje 2 × T IN NIB w nibie (60e8a9c) i B IN NIB (K5).
  Płyty bez stref belek i krawędzie bez belki — bit w bit jak dotąd.
  Za końcem nibu pręty wchodzą **600 mm w płytę** (liczone od końca nibu; w narożniku wklęsłym z nibem na krawędzi prostopadłej — od linii uskoku tamtej krawędzi, decyzja Bartka 2026-10-20), jeśli płyta tam jest (narożnik wklęsły);
  przy narożniku wypukłym pręt zaczyna się na otulinie 40. Jeden pręt: długość siatka 250 **w górę** (lepiej za długi),
  **min. 1500** (twardo 1250), nie dłuższy niż miejsce w płycie. Opis na zewnątrz krawędzi, ramię **o 200 mm dłuższe**
  niż zwykłe; prosto, chyba że po drodze jest płyta albo wzdłuż krawędzi jest bliżej poza płytę — wtedy załamanie
  200 mm za krawędzią i dalej wzdłuż krawędzi.
- **Odległość opisów od płyty (decyzja użytkownika 2026-10-16):** koniec tekstu zwykłego opisu (siatka B/T, UB) **2000 mm** za
  krawędzią płyty (`LeaderArmExtension`, dawniej 2400); opisy „… ADD” (B1 / B2 ADD, UB ADD, B3 ADD — `AddLabelExtra`) i T IN NIB
  (`NibTopLabelExtra`) — **200 mm dalej** (2200; ADD nie krócej niż cały tekst za krawędzią). Detale belek 1:25 — bez zmian
  (`BeamDetailLeaderArm` 2400 × skala). S-REG pokaże przesunięte opisy — oczekiwana różnica.
- **Kropka zejścia odnośnika (decyzja 2026-10-16):** rozkład ze strzałkami (> 3 pręty), odnośnik biegnie najpierw wzdłuż linii rozkładu
  (opis przesunięty w bok) i schodzi z niej MIĘDZY strzałkami → kropka jak przy pręcie w punkcie zejścia (`AnnotationEngine.AddJunctionDot`,
  XData RC_BAR_ANNOT [JDOT], przebudowywana z odnośnikiem). Od 2026-10-19 także odnośnik prosto z linii (np. UB ADD ze środka); liczone z geometrii BTR opisu (linia rozkładu, koła prętów). Bez kropki: odnośnik wzdłuż linii rozkładu za strzałkę (siatka), zejście za strzałką, w miejscu kropki pręta, rozkład skośny, ≤ 3 pręty.
- **Poprawki ze zgłoszeń kolegi (2026-10-16):**
  - przebudowa rozkładu / opisu (`RebuildVisibility`, `Regenerate…`, `Rebuild…`, przebudowy odnośnika, tekst w RC_EDIT_LABEL) na
    WŁASNYM BTR — `BarBlockEngine.OwnBtrForWrite` klonuje BTR współdzielony przez kopie (przeciągnięcie z Ctrl, ROTATE/SCALE Copy,
    REDO po COPY) — wygaszenie jednego rozkładu nie zmienia kopii;
  - `SyncAnnotation` bez opisu (puste [11]) nie szuka już opisu po samym Mark (tylko opis bez [16], jedyny z tym Mark — stare rysunki);
    RC_EDIT_DISTRIBUTION: [11] wskazujące opis innego rozkładu → odłączone, opcja „Dodaj opis”; RC_EDIT_LABEL: opis bez pary
    z rozkładem — tylko tekst, bez zmiany widoczności;
  - RC_EDIT_LABEL Manual: indeks klikniętego pręta i podświetlenie w układzie ROZKŁADU (BlockTransform bloku prętów);
  - znacznik płyty nieaktualny (obiekt usunięty / polilinia w całości w obrysie — dawny obrys = linia uskoku) → rozkład po położeniu
    (`SlabTagIsStale`: przekrój, `BelongsToCurrentSlab`); znacznik innej istniejącej płyty — pominięty z komunikatem;
    **RC_RELINK** („Relink Bars”) przepina rozkłady w obrysie na wskazany obrys;
  - grip rozkładu przy kilku zaznaczonych — właściciel klonu po BTR + pozycji (`_gripOwners`), nie ostatni z gripami; rozkład Any —
    bez podglądu zmiany liczby (liczba w RC_EDIT_DISTRIBUTION);
  - przenumerowanie kopii pręta bez etykiety — komunikat (etykieta obok należy do oryginału);
  - Prepare GA: linie SD-PILED-RAFT też sklejane w pętle (tylko pętla obejmująca obrys); otwarte kawałki tuż za obrysem — ostrzeżenie
    „nib nie rozpoznany”.
  - Przekrój: poziom pręta z kodu warstwy (B1/B2/T1/T2) niezależnie od kierunku — bez zmian i bez komunikatu (decyzja użytkownika).
- **Opis z odnośnikiem prostopadłym do linii rozkładu** (UB ADD: ze środka linii wzdłuż prętów, 2 punkty) — grip tekstu w bok
  wstawia kink na linii rozkładu i przesuwa go po niej (2026-10-19, jak opis ręczny); kropka zejścia wg reguły wyżej. Kink w samym
  środku (ramię zerowe) ślizga się po osi rozkładu. Generowanie — wszystkie ścieżki tworzą opisy przez `AnnotationEngine.CreateLeader`.
  MOVE / COPY / MIRROR opisu albo rozkładu (uwaga Bartka 2026-10-26, LINKS w detalu GB — leader się łamał): `RebuildDistLineInBtr`
  → `AlongBarsLeader` — leader prosty albo L (ostatni odcinek wzdłuż prętów) dostaje stopę NA linii rozkładu na wysokości tekstu
  i ostatni odcinek prosto wzdłuż prętów (jak grip tekstu); leader wzdłuż linii rozkładu (siatka) i trasy z > 1 załamaniem — bez zmian.
- Kolizje opisów (AutoRebar, `PlaceLabel`): przeszkody = teksty (opisów i rysunku) **oraz linie opisów** (leadery,
  linie rozkładów); nowy leader nie może przecinać tekstów. Kolejność: warianty leadera (druga strona płyty; T IN NIB —
  prosto / załamanie wzdłuż krawędzi w obie strony, krótsza droga najpierw), każdy bez przesunięcia i z przesunięciem
  **w bok** (grubość tekstu + 150); potem drobne wydłużanie ramienia (co 100 mm, maks. 4 m); na końcu dawna drabinka.
- **UB 03: linia rozkładu poza płytą** (250 mm przed krawędzią zewnętrzną), opis prosto poza obrys.

## 4a. Model 3D (RC_SIATKA_3D)
- Model poglądowy budowany z rozkładów 2D (2D jest źródłem); ponowne wywołanie kasuje poprzedni model płyty.
- Położenie: 60 000 mm na prawo od prawej krawędzi siatki górnej (bez góry — od dolnej).
- Detale (otwory, w przyszłości belki): użytkownik zaznacza ramki detali; rozkłady z rysunku detalu
  przenoszone są na plan. Ramka detalu ma XData `RC_DETAIL` [nr, dx, dy] (punkt planu = punkt detalu − (dx, dy)).
  Pręty „B+T ADD” → kopia w warstwie dolnej i górnej o tym samym kierunku.

## 4b. Przebicie z raportu (RC_PUNCHING_AUTO)
- Źródło: report_punching.xlsx, arkusz „Punching EC2” (wartości, nie formuły); kolumny po nagłówkach.
  Czytnik xlsx własny (`XlsxReader`, zip + XML) — bez zewnętrznych pakietów.
- Płytę (PLOT / GARAGE) wybiera użytkownik z listy (podświetlona ta z największą liczbą trafień). Nagłówki płyt w raporcie:
  „PLOT … (n piles)” i „GARAGE … (n piles)” (`PlotLabelRules.TryParseReportHeader`; POPRAWKI_WZORCA 22 — dawniej pale garaży
  dopisywały się do poprzedniej płyty); „PLOTS 3-4” nadal nierozpoznane. Sprzątanie notatek MANUAL-NOTE: notatka innej płyty
  „PLOT …” / „GARAGE …” zostaje, notatka bez płyty (stara wersja) — usuwana (`IsReportPlotKey`). Bez wskazywania obrysu:
  pal = koło SD-Pile z podpisem obok (≤ max(8R, 1500)) w całym modelu; dokładne ID na dowolnej warstwie,
  znormalizowane (617 ≡ P617) tylko na SD-Pile Text*. XData `RC_PH` [kod, pal, PLOT].
- PH = 3·(H12@200 | H16@200 | H16@100) + (Internal | Edge | Corner) + 1.
  MANUAL: Util > 100 %, Reentrant, FAIL / SHEAR RAILS, nieznane zbrojenie — tag MANUAL (czerwony) + notatka nad płytą.
- Ponowne uruchomienie czyści poprzednie tagi/kreskowanie tej płyty i przekreślenia szablonów.
- Szablony detali (MText AP-TEXT): „(nNo LOCATIONS)”, „APPLICABLE FOR PILES …” (tylko do końca akapitu).
- Pręty do BBS: H12 L2250 × 14 na detal PH1–3; H16 L2500 × 14 (PH4–6) / × 28 (PH7–9).
  Poprzednie pręty zbiorcze usuwane dopiero po wskazaniu punktów (Esc nic nie kasuje).
- **Numery prętów zbiorczych (decyzja użytkownika 2026-10-16; dawniej 501 / 502):** seria górna 101+ — istniejący pręt prosty
  o tym samym Ø i długości w serii 101–499 (także poprzedni pręt zbiorczy) → ten sam numer, inaczej następny wolny
  (`NextAutoTop`; ostatni 105 → 106 / 107). Numery liczone przed usunięciem poprzednich prętów (`ResolveSummaryNumbers`).
  Pręt w elewacji ma XData `RC_PH` [SUMMARY, Ø] — po nim usuwane przy ponownym uruchomieniu (dawne „H12-501” / „H16-502” też).
- Detale PH (`PhDetailRules`, `PunchingAutoEngine.UpdateDetails`, komenda **RC_PH_UPDATE**, wstążka „Update PH”):
  opisy prętów „7 H12-113-200 T” (warstwa „AutoCAD_Structural_Detailing_Distribution description”, obszar detali =
  obwiednia tytułów PH −2500/+6000 X, −2000/+6000 Y) dostają numer pręta zbiorczego tej średnicy z rysunku (tag SUMMARY,
  potem pręt H12 L2250 / H16 L2500 serii 101+, potem 501+). Wywoływane też na końcu RC_PUNCHING_AUTO i SUMMARY_BARS.
  Tytuły: „… PILE CONDITION PHn” w 1. linii, „EXTRA BARS (…)” od 2., wszystkie akapity wyśrodkowane (q* → qc, bez ^I) —
  także w szablonie rc-template.dxf.gz. Przekreślenie nieużytego detalu po zakresie samego tekstu (rozbite fragmenty MText),
  nie ramki MText (PH1 ma szerszą ramkę).

## 4c. Import map zbrojenia (RC_IMPORT_MAP) — jak ASD-IMR
- Plik …_punching_reinf_maps.dxf: kolumny płyt (PH-SLAB-HEADER „PLOT …”), ramki PH-FRAME, rodzaj mapy wg
  obrysu PH-<T1|T2|B1|B2|PH>-SLAB w ramce. Płyta z listy (domyślnie ta, której nazwa jest na rysunku).
- Wklejane ramki T1, T2, B1, B2 z całą zawartością (bez nagłówka płyty, bez mapy PH) w miejscu wskazanym
  przez użytkownika; podgląd ramek w jigu; punkt = lewy górny róg ramki T1. NIE na obrys płyty.
- XData RC_MAP [PLOT, mapa]; ponowny import tej samej płyty zastępuje poprzedni.

## 4c'. Import analizy razem (RC_IMPORT_ANALYSIS, wstążka „Import Analysis”)
- Jedno okno z dwoma plikami: mapy (…reinf_maps.dxf/dwg) i raport przebicia (report_punching.xlsx), każdy z polem
  wyboru (np. same mapy); po wskazaniu jednego drugi szukany w tym samym folderze. Odznaczony plik nie kasuje
  zapamiętanego. Na wstążce zastępuje przycisk „Reinf. Maps” (komenda RC_IMPORT_MAP zostaje).
- Mapy wklejane od razu (`MapImportCommands.RunImport`: wybór płyty, ramki). Pliki i płyta zapisywane w rysunku
  (`AnalysisStore`, NOD „RC_ANALYSIS”: [mapy, raport, PLOT]).
- RC_PUNCHING_AUTO (Punching) tylko detaluje: gdy w rysunku jest raport z Import Analysis — bez pytania o plik
  i o płytę (`RunPunching` z zapamiętaną płytą); inaczej jak dawniej wybór xlsx. RC_IMPORT_MAP działa dalej osobno.

## 4d. Przygotowanie rysunku RC z GA (RC_PRZYGOTUJ_GA)
- **Szablon RC (decyzja użytkownika 2026-10-14, `RcTemplateEngine`):** rysunek bez elementów „pliku RC default” dostaje je z zasobu
  wtyczki `Resources/rc-template.dxf.gz` (EmbeddedResource; budowany z przykładowego rysunku RC skryptem
  `tools/RcTemplate/build_template.py`): tytuły rzutów „REINFORCEMENT DETAILS … BOTTOM / TOP LAYER” (przed wstawieniem płyty),
  **czysty arkusz RC zawsze na nowo** (2026-10-14, poprawka: dawniej tylko gdy brak A1-BL) — nazwa wg GA: `BuildDrawingSuffix`
  (GA0300… → RC0300) + rewizja z A1-BL szablonu (REV, „C1”) → „RC0300C1”, pierwsza zakładka; layout o tej nazwie i puste
  „Layout1/2” usuwane; bez rzutni, notatek „Note:” i chmurek rewizji (A1-BL, ramki, notatki ogólne, tabela rewizji; wydruk jak
  w szablonie — pc3, A1, ctb, skala); detale PH1–PH9 (po wstawieniu płyty, PlanGap na prawo od rzutu górnego
  i ramki rebar_top) z opisami „EXTRA BARS (N/A)” / „APPLICABLE FOR PILES N/A” — wypełnia je RC_PUNCHING_AUTO. Style, warstwy,
  bloki — razem z obiektami (WblockCloneObjects, istniejące bez zmian). Element już obecny — pomijany. Blok A1-BL nowego layoutu
  wypełnia dalej `GaTitleEngine` (teksty z GA). **Drawn / Checked / data (2026-10-15):** po wyborze płyty okno (`SheetPeopleDialog`)
  „Drawn:” / „Checked:” (podpowiedź = ostatnio wpisane, %APPDATA%\NaSmartRc\sheet.txt; Anuluj przerywa) → A1-BL DRAWN / CHECKED i w tabeli
  rewizji kolumny By / Chkd (MText na TEXTNOTES równe dawnym DRAWN / CHECKED); A1-BL DATE = bieżący miesiąc i rok („OCTOBER 2026”),
  data w tabeli rewizji = dzisiejsza („15.10.26”) — `RcTemplateEngine.FillSheetInfo`. Wydruk arkusza: kopia ustawień 1:1 z layoutu
  szablonu (`PlotSettings.CopyFrom`) na aktywnym layoucie, zapasowo PlotSettingsValidator.
  **Ustawienia wydruku jak we wzorcu RC (322-324, decyzja 2026-10-15, `PlotSetupEngine`, koniec RC_PRZYGOTUJ_GA i komenda
  `RC_USTAW_WYDRUK`):** PSLTSCALE = 0 na każdym layoucie (u nas było 1 — typy linii w rzutniach kreskowane), rzutnie (poza rzutnią
  arkusza) na PCN-VIEWPORT (niedrukowana, 0,00 mm) i zablokowane, znaczniki przekroju SD-SECTION kolor 2 / 0,00 mm (warstwa 0,00 mm),
  opis płyty z GA zachowuje grubość / typ linii źródła (0,00 mm).
  **speedeck-mono.ctb** (z ASD, zasób `Resources/speedeck-mono.ctb`): wszystko czarne, grubość wyłącznie wg KOLORU (grubości obiektów
  pomijane): 1 → 0,20, 2 → 0,30, 3 → 0,40, 4 → 0,53, 5 → 0,80, 6 → 0,53, 7 → 0,30, 8 → 0,20, 9 → 0,13 mm. Warstwy prętów
  RC-SLAB-BOT/TOP-01/02 — **niebieskie (5)** jak w ASD (próba z magentą 6 cofnięta 2026-10-15; RC_USTAW_WYDRUK przywraca 5);
  plik kopiowany do PlotStyles BricsCAD, gdy go brak.
  **Tekst opisów rozkładów jak ASD (2026-10-15, porównanie z 322-324):** styl `PH-LABEL` (romans.shx, styl 0,70) + współczynnik
  szerokości tekstu 0,80, wys. 125, kolor 2 (dawniej StandardNarrow: txt.shx 0,70); istniejące opisy przestawia RC_USTAW_WYDRUK.
- W otwartym pliku RC (default): wybór pliku GA (.dwg/.dxf) i płyty z listy (opis „PLOT …” albo „GARAGE n…” na SD-Text
  wewnątrz zamkniętego obrysu SD-PILED-RAFT — `PlotLabelRules.IsPlotLabel`; obrys zewnętrzny = największy zawierający opis).
  Lista: najpierw PLOT, potem GARAGE (domyślnie pierwszy PLOT). Garaż: TITLE_1 / DRAWING_NUMBER z layoutu GA (GA3215 → RC3215);
  garaż bez DRAWING_NUMBER nie ma numeru zapasowego (`GaTitleEngine` szuka „PLOT n” w TITLE_1).
- Kopiowane z obszaru obrys + 1500 mm: SD-PILED-RAFT (obrys, linia uskoku, door threshold, linie stopni),
  koła SD-Pile, teksty SD-Text **bez „NIB TOC=…”** (opisy belek itp. zostają); podpisy pali (SD-Pile Text*)
  **tylko na rzucie górnym**. Wymiary, architektura, poziomy, kanalizacja — nie.
- Opis płyty przebudowany: nr plotu, SSL, grubość („225mm THK SLAB”, bez „ON”); ramka dopasowana (+70 mm).
- Rzut dolny nad tytułem „…BOTTOM LAYER” (środek; dolna krawędź płyty **3000 mm nad górą tytułu** — decyzja 2026-10-15, miejsce na
  opisy prętów; cały rysunek płyty co najmniej 2400 nad tytułem), rzut górny 7500 mm obok, tytuł „…TOP LAYER”
  pod nim; ramki rebar_bottom / rebar_top 7500 mm po bokach (przesuwane razem z zawartością).
- Nib: NAJPIERW automat domykania otwartej linii uskoku krawędzią obrysu (`NibAutoClose.TryClose`, sekcja 4 „Nib”; nowa
  polilinia z tagiem RC_GA — ponowne wywołanie ją zastępuje; „nib domknięty: n” w komunikacie, problemy z „(rzut dolny/górny)”),
  POTEM otwarte kawałki tworzące pętlę łączone w zamkniętą polilinię (`JoinLoops`, tol 1 mm, bez łuków) — kolejność ważna:
  `JoinLoops` sklejał linię uskoku z linią strefy 760 mm w zamknięty pas (PLOT 319).
- XData RC_GA [PLOT, B|T] — ponowne wywołanie zastępuje płytę.
- **Belki (plan belek E1):** zamknięta polilinia `SD-Thickening-Zone` → oba rzuty, warstwa `RC-BEAM` + `RC_BEAM` ([6] = obrys tego rzutu)
  — po domknięciu nibu, przed `JoinLoops`. H z opisu „450THK” w strefie (regex `\d{3,4}\s*THK`, bez „SLAB”; MText przez TextLines;
  punkt wstawienia albo środek tekstu w strefie); kontrola krzyżowa SSL (opis płyty) − NIB TOC (tekst GA najbliżej strefy) + 150 →
  ostrzeżenie przy różnicy; brak opisu → H z kontroli; brak obu → H = 0 (RC_BEAM). H spoza siatki 75 — ostrzeżenie. Teksty „…THK” kopiowane
  jak dotąd (SD-Text). Ponowne wywołanie usuwa też pręty belek starych stref (znacznik albo punkt w strefie). Płyta bez warstwy
  `SD-Thickening-Zone` — bit w bit bez zmian (test BRX D526). Holbrook P02: strefy tylko w 319 (450), 528-529 (375), 539 (300).
- **K8 — belki z opisów „xxxTHK” (GA bez SD-Thickening-Zone, C12):** MLeader na SD-Text z opisem belki („…THK” / „GB-…”) kopiowany na oba rzuty;
  po `JoinLoops` (`ProcessBeamTexts`): krawędzie linii uskoku z `NibDetector`, a gdy obrys z GA jest linią uskoku (krawędź zewnętrzna nibu —
  otwarte kawałki) — krawędzie obrysu z równoległym kawałkiem 50–255 mm na zewnątrz; czysta `BeamGeometry.ZonesFromThk`: punkt opisu (MText —
  środek rzeczywistej obwiedni: ActualWidth / Height, zaczepienie, obrót; MLeader — grot pierwszego odnośnika) w pasie [linia uskoku, +750]
  krawędzi nibu, w głąb do linii strefy z GA (odcinek równoległy 300–1000, najbliższy 600) albo 600, wzdłuż — krawędź uskoku podzielona
  odcinkami prostopadłymi (granice stref o różnym H), H z MText (MLeader tylko bez MText; inny H — ostrzeżenie), sąsiednia krawędź (L / U) —
  tylko z własnym opisem albo linią strefy, przedziały tego samego H stykające się → jeden wielokąt (`RectilinearPolygons.Union`). Odrzucenia
  z komunikatem `[RC BEAM] Opisy THK: …`: „GB-…” (detal ręczny), H ≤ grubość płyty z opisu, punkt poza obrysem, poza pasem, MLeader bez linii
  strefy (pole lokalne — styk GB). Strefa RC-BEAM + RC_BEAM ([6] = obrys rzutu), znacznik RC_GA; kontrola SSL − NIB TOC + 150; NIB TOC
  wskazujący pogrubienie bez opisu — tylko ostrzeżenie. `BeamGeometry.ZoneFromNibEdge` wspólna z RC_BEAM → Krawedz. C12 (kopie GA P06 bez
  ramek arkusza): PLOT 17 — 450, PLOT 3 — 600 / 675 / 975. Ograniczenie: Prepare GA na oryginalnym GA C12 wybiera ramkę arkusza
  (SD-PILED-RAFT) jako obrys; obrys płyty w RC = linia uskoku (krawędź zewnętrzna nibu nie jest domykana).
- Teksty z layoutów GA (`GaTitleEngine` / `GaNotesText`, jak ASD-GAI) — na końcu RC_PRZYGOTUJ_GA (podgląd, „Pomiń”)
  i osobno `RC_GA_TEKSTY`: A1-BL CLIENT_1..3, PROJ_1..3, APPROVED 1:1; TITLE_1 = prefiks GA przed „GENERAL ARRANGEMENT”
  + „REINFORCEMENT DETAILS …”; DRAWING_NUMBER = prefiks GA + RC + numer pierwszego GA (GA0090 → RC0090, RC0091 wg zakładek).
  SLAB NOTES (MText ze „SLAB AREA” na layoutach RC): ogony akapitów AREA / PERIMETER / THICKNESS / CONCRETE VOLUME
  i blok „CONCRETE TO BE DESIGNATED|DESIGNED … CERTIFICATE.” z GA (biały kolor), HYSTOOLS 225 → DK90, 300 → DK165.
  Layout GA wybranej płyty ma pierwszeństwo (TITLE_1 „PLOT …”).

## 4e. BBS (RC_BBS) — jak ASD-BBS w AsdRcSlab, prosto z rysunku
- Źródło: rozkłady RC_BAR_BLOCK **z żywym opisem** (bez opisu nie liczone — ostrzeżenie) → pręt RC_SINGLE_BAR;
  ilość = suma EffectiveCount. Pozycja z Mark (`H12-03` → 3). Dół = pozycje < 100, góra = 101+ (także 501/502).
- Długość cięcia jak w programie (`ShapeCodeLibrary`, zaokrąglenie w górę do 25 mm — od W4g także prosty 00; długość nadpisana ma pierwszeństwo).
  W4b: liczona wg BS 8666:2020 także dla starych rysunków; **bezpiecznik** (tylko odczyt): dla kształtów ze zmienioną
  formułą (`BarShape.FormulaChangedIn2020`) zapisane TL ≠ formuła → ostrzeżenie „Pozycja NN (kształt KK): długość wg
  BS 8666:2020 = … mm, w rysunku … — sprawdź.”; brak długości (47 bez C, 64) → wiersz „brak długości” + ostrzeżenie;
  stary pierścień 44 → ostrzeżenie „liczony jako 75 … B = zakład” (W4c). W4g: bezpiecznik obejmuje też prosty 00
  (stare TL = A, np. 3010 → 3025 z ostrzeżeniem; długość nadpisana — bez) — `BarShape.StoredLengthDiffers`.
  Wymiary do kolumn I–M wg nazw parametrów kształtu (A, B, C, D, E/R). Prosty: kod „00”, A = „STR”.
  W4f: ostrzeżenia walidacji minimów (`BarShapeValidation`) dla każdej pozycji — tylko lista, arkusz bez zmian.
- Ta sama pozycja z różnym prętem → osobne wiersze + ostrzeżenie. Rozkład bez pręta źródłowego: prosty liczony z danych
  rozkładu, gięty pominięty (ostrzeżenie).
- Szablon: wbudowany `Resources/default-bbs.xls` (EmbeddedResource) albo plik użytkownika (zapamiętany w
  %APPDATA%\BricsCadRc\bbs.txt). Strona = wiersze od „BOTTOM LAYER” do „Accessories” (26). Zapis .xls przez **NPOI 2.5.6**.
- Layouty z blokiem A1-BL przypisuje użytkownik (Skip / Bottom / Top / BottomAndTop). Nagłówek: Contract = DRAWING_NUMBER
  przed „-”, adres PROJ_1..3, plot = TITLE_1 przed kropką, rewizja z REV. Wszystkie BottomAndTop → jedna kartka, gdy się mieści.
- Akcesoria tylko na 1. arkuszu: ilość = ROUNDUP(SLAB AREA / 2 × 1,1); HYSTOOLS DK z notatki, inaczej 225 → DK90, 300 → DK165.

## 4f. Dozbrojenie dołem z map (RC_GENERATE → Add, RC_GENERATE_ADD)
- Tylko mapy **B1 / B2** (RC_MAP [PLOT, mapa]); T1 / T2 bez zmian. Mapa dopasowana do wskazanej płyty (rzut dolny) po obrysie
  PH-B?-SLAB (ten sam wymiar; przy kilku — PLOT płyty z RC_GA), przesunięcie mapa → plan.
- Strefa = prostokąt SD-B? ADD (zakres w osi rozkładu) + kontur (faktyczny obszar) + wartość As [mm²/m] (tekst w strefie).
- Pręt: kontur + **≥ 600 mm** z każdej strony, **min. 2000**, długość co 250, maks. 6000; rozkład **co 200**, pokrywa cały
  prostokąt strefy, **min. 5 prętów** (decyzja 2026-10-15; przy brzegu płyty rozkład przesuwany do środka, mniej tylko gdy płyta za wąska). Strefy zachodzące wzdłuż prętów i bliżej niż 400 mm w osi rozkładu → jeden rozkład.
- Wartość As strefy: tekst SD-<mapa> ADD w prostokącie strefy; tekst wyrównany (Middle Center z map) — po punkcie wyrównania, nie po
  `Position` (lewy dół wychodził poza wąską strefę → brak wartości → 786 → UB ADD przy 512 mm; poprawka 2026-10-16).
- Średnica: As ≤ 2·393 → H10, ≤ 393+565 → **H12**, dalej **H16** (komunikat); > 393+1005 → „zaprojektuj ręcznie”.
- Pozycja: istniejąca prosta o tej średnicy i długości (także z siatki dołem) albo nowa w strefie szablonów dołu.
- Opis „H10-nn-200 B1 ADD” (jak siatka, pręt reprezentatywny), położenie zaokrąglone do 50 mm względem najbliższego pala.
  **Opis (2026-10-15):** B1 ADD — odnośnik POD płytę (B2 — bliższa krawędź), ramię = cała długość tekstu + 300 za krawędzią (tekst nie
  wchodzi w płytę ani na wymiary), dalej ramiona +400 i druga strona; każdy wariant z przesunięciem w bok — opisy nie nachodzą na siebie;
  **wymiary** (SPEEDECK-1-50 RC, SD-PILED-RAFT, XData RC_ADD): pal → koniec prętów i pal → skrajny pręt; bez pala — od krawędzi.
- Ponowne wywołanie zastępuje dozbrojenie tej płyty (rozkłady „… ADD” i wymiary RC_ADD). Siatka B1/B2 nie kasuje ADD.
- **Każdy rozkład z „ADD” w opisie jest cyan** (pręty w bloku kolor 4) — także ręczny opis i „B+T ADD” w detalach.

- **Mapy T1 / T2 → UB ADD** (pręty górne bez zmian): dla każdej strefy mapy najbliższa krawędź z UB tej warstwy
  (T1 → UB 01 na krawędziach pionowych, T2 → UB 02 na poziomych; krawędź = koniec prętów, przy nibie linia uskoku),
  odległość strefy (kontur, inaczej prostokąt) od krawędzi: mapa ≤ 750 i odległość ≤ 400 → UB ADD; mapa > 750 i ≤ 600 → UB ADD;
  dalej — nic. UB ADD = ten sam pręt co UB (ta sama pozycja), ZAWSZE 7 sztuk w połowie między istniejącymi UB, wyśrodkowane na strefie,
  opis „n H12-02-200 UB ADD” (cyan) jak przy prętach prostych: linia rozkładu w płycie na 3/4 ramienia UB od krawędzi
  (opis UB jest w połowie), odnośnik ze środka linii rozkładu wzdłuż prętów za krawędź (cały tekst za płytą, min. 600 — 2026-10-15), tekst wzdłuż odnośnika; widoczny (reprezentatywny) pręt UB ADD obok środka, nie pod odnośnikiem. Wymaga wygenerowanych UB; po ponownym UB uruchom ADD jeszcze raz.

## 4g. Przekrój płyty (RC_SECTION) — rzeczywiste cięcie
**Decyzja Bartka 2026-10-16 (SectionPlanner.Version 6):** pale — cięcie przecina pal → rzeczywiste położenie (cięciwa); nie przecina,
a pal jest ≤ 4000 od znacznika (wzdłuż odcinka cięcia) → jeden pal (pełna średnica) z osią 500 od najbliższej krawędzi płyty
(jak na typowych A-A / B-B). Styl pala jak na typowych przekrojach: głowica 30 w betonie (czerwona przerywana: góra skala 3,
boki skala 1), trzon 226 pod spodem (s-Pile), urwanie — łuki w dół na całej szerokości + łuk w górę na lewej połowie, oś CENTER
skala 7,5. Opisy kropek pod kropką; nie przecinają linii pala (boki i oś są przeszkodami — opis między linią a osią dozwolony).
T IN NIB w nibie płyty — pręty dwóch kierunków warstwami: spinka UB 03 w widoku z kierunku 1 (kod …1) → kropki T IN NIB LEŻĄ NA
jej górnym ramieniu; z kierunku 2 (kod …2) → kropki POD górnym ramieniem (w środku spinki). Przekrój z belką bez zmian (pod ramieniem
UB 03, decyzja 2026-10-14).
Kropka T IN NIB nie wchodzi w inny pręt w widoku (zagięcie / ramię UB 02 w przekroju w drugim kierunku) — odsunięcie wzdłuż
ramienia UB 03 do styku lic (`ClearDotOfBars`). Przekrój z nibem: nad przekrojem opis „NOMINAL TOP COVER TO NIB REINFORCEMENT- 30mm”
(SD-Text, kolor 2, ROMANS NARROW 60, ramka 1107 × 217, jak na typowym A-A; względem krawędzi nibu i wierzchu płyty).
**Decyzja Bartka 2026-10-14 (SectionPlanner.Version 5):** przekrój „w 1 warstwie” — pręty jednego kierunku (proste siatki i ramiona
U-barów) na jednej linii poziomu swojej warstwy, bez odsunięcia zakładów Z2 (`OffsetLaps` wyłączone; od 5895ac7 pręt prosty przy
U-barze odsuwany o 25 w głąb — B1 lądował nad B2, kropki na prętach). Symbole zakończeń S1 (kreska 45°) bez zmian. Obrys pręta
(`OutlineOf`) w narożnikach — ścięcie po dwusiecznej normalnych obu odcinków (wcześniej normalna z cięciwy sąsiednich punktów: przy
ramieniu 640 + kreska 35 krawędzie U-bara skośne, kreski „krzywe”). Luka w zbrojeniu (np. usunięty rozkład): schemat siatki
(`AlignMesh` / `MeshLevelBesideBeams`) wstawia kropkę tylko tam, gdzie ≤ ¾ rozstawu od niej leży rzeczywisty pręt rozkładu
(`CoveredByBars`; pręt w nibie — tolerancja + 150) — w luce brak kropek i podpisów sąsiednim rozkładem, a RC_SECTION /
RC_SECTION_UPDATE wypisuje komunikat „Luka w zbrojeniu (dół/góra) …” (`WarnGaps`).
Przekrój przez belkę (2026-10-14): UB 03 w pasie belki / jej nibie licuje się ze strzemieniem (lico zewnętrzne dolnego ramienia na
`VLinkBot`, wysokość U bez zmian — `UbBeamShift`); pręty w nibie belki 2T+1B: górą NA górnym ramieniu spinki, dołem wewnątrz na dolnym
(screen Bartka). Opisy: pręt górą w dolnej części belki (pod spodem płyty) — w rzędzie uskoku nibu belki nad prętem (w obrysie, bez
przecinania pionowych ramion prętów); opis dołem kolidujący tylko z betonem (pręt płyty przy licu belki) — przesunięty w bok o ≤ 60 w
rzędzie pod płytą zamiast spadać pod belkę. Okno RC_SECTION: wysokość belki z rekordu (tylko do wglądu). Znaczniki przekroju także na
detalach belek (`SectionContext.DetailMarks`): cięcie → rzut strefy + Shift detalu, przycięte do ramki FRAME; skala znacznika = skala z
tytułu detalu („SCALE 1:25” → 0,5 rzutu 1:50); klucze `D<nr>.P1/P2` (litera przesuwana ręcznie — `MarkTextDefault` ze skalą).
Siatka przy belce (2026-10-14): siatka górna i dolna w jednej linii — w `MeshLevelBesideBeams` kotwa po stronie belki wspólna dla
poziomów odcinka (wyznacza ją poziom górny, liczony najpierw); bez belek bez zmian.
- Wskazanie: obrys płyty (rzut dolny albo górny), 2 punkty linii cięcia (pozioma / pionowa wg dłuższej składowej),
  strona patrzenia, okno parametrów (`SectionDialog`), punkt wstawienia = lewy górny narożnik płyty (poziom SSL).
- **Belki (plan belek E5; wartości domyślne do potwierdzenia przez Bartka):** `PrepareSection` czyta strefy RC_BEAM rzutów płyty (punkt
  wewnątrz strefy w rzucie, przesunięcie do rzutu wskazanego, bez duplikatów dół / góra — pierwszeństwo rzutu wskazanego) i tnie je linią
  cięcia (`SectionPlanner.CutIntervals`) → `Input.Beams` (`BeamBand`: pas [S0, S1] wzdłuż cięcia, H, nib, otulina boczna `Cover` (45) i dolna `CoverBottom` (50) z rekordu, Ø strzemion, A strzemienia
  ze wzoru, parametry rekordu). Rozkłady BB / BT / BL / BS / BN → `Dist` rodzaju **`Beam`** (skos prętów L, drugie ramię U-bara 21 —
  `PairOffset` = ±(B − d) w stronę symbolu zagięcia, pręty boczne — `Layers` = CountDisplay / Count); teksty w strefach („450THK”) nie są
  grubością płyty. **Pusta lista `Beams` = wynik planera bit w bit** (5 złotych przypadków bez zmian, `Version` = 1 bez podbicia).
  Planer: odcinek betonu z pasem belki dostaje profil `Segment.Prof` (przedziały u: płyta −T…0, nib płyty — wierzch vNibTop, trzon belki
  −H…0, nib belki −H…−H + NibH między licem pasu a krawędzią odcinka, gdy krawędź ≤ 260 mm — `BeamNibMaxGap`) i obrys z profilu (urwanie —
  zygzak na lokalnej wysokości); `InConcrete`, spód lokalny (`LocalBottom` — bez belki dokładnie −T), opisy pod spodem belki, pal pod belką
  (`Pile.Base`), tytuł poniżej spodu i wymiarów belki. Pręty belki tylko w pasie (kropki / pręty w widoku poza pasem — pomijane, bez
  wpływu na odcisk): strzemię 51 na otulinie dolnej (50) od spodu (lica — z rozkładu BL, otulina boczna 45), wysokość zewnętrzna A z rozkładu BL w pasie (inaczej wzór 1b.1), dół (BB)
  na strzemieniu, góra (BT) pod jego górnym ramieniem, ramiona prętów giętych 11 / 21 w zakładzie „piętrowo” (dół nad, góra pod prętem
  prostym), pręty boczne (BS) równo między osiami dołu i góry (Layers poziomów, przy ramionach strzemienia), pręty w nibie (BN) w narożach
  spinki UB 03 (dołem: −H + otulina dolna 50 + 10 + d/2, górą: wierzch nibu − 30 − 10 − d/2). **Cięcie prostopadłe do belki** — kropki 4 H16 dół / góra
  (+ boczne, nib), strzemię 51 w widoku (kształt pręta wzorcowego, haki u góry). **Cięcie wzdłuż belki** — pręty podłużne w widoku (linie na
  swoich poziomach), strzemiona jako pionowe ramiona (jeden opis pozycji ze strzałką do ramienia środkowego). Kropki belki nie są przesuwane
  przy ramionach prętów w widoku; opis pręta belki w widoku — trzecie przejście bez warunku „strzałka nie tnie opisów” (pod belką gęsto).
  Siatka płyty przy belce = to, co jest w rysunku (E2: równoległe kończą się przed licem wewnętrznym belki; prostopadłe do belki krawędziowej
  — decyzja 2026-10-11 „do 40mm side cover” — przez belkę do 40 od lica zewnętrznego): pręty siatki proste na swoich poziomach (przycięte do
  betonu; prostopadłe w widoku sięgają w trzon belki); **kropki siatki (schemat AlignMesh) nie są
  dorysowywane w pasie belki (trzon + nib belki), gdy rozkład poziomu nie ma pręta w trzonie** — kotwa przy belce = rzeczywisty pierwszy
  pręt rozkładu (`MeshLevelBesideBeams`, `SectionPlanner.Version` 4; rysunek ze siatką przez belkę — schemat jak dotąd; przekrój wzdłuż
  belki: kropki prętów prostopadłych w trzonie — schemat jak bez belki; decyzja „do 40mm side cover” zmienia tylko wejście — wersja 4 bez zmian);
  UB 03 / T IN NIB w pasie albo nibie belki — poziomy względem spodu belki / wierzchu nibu belki; pręty w nibie 2T+1B (BN „2+1 … T+B IN NIB”,
  decyzja 2026-10-11) — 3 kropki jednej pozycji: 2 pod górnym ramieniem UB 03, 1 w dolnym narożu pod prętem bliższym krawędzi (cięcie
  wzdłuż nibu: pręt najbliższy linii cięcia na obu poziomach), numer przy każdej kropce (jak BM); rysunki sprzed decyzji: T IN NIB w nibie
  pod górnym ramieniem UB 03, B IN NIB w dolnym narożu;
  T IN NIB w trzonie belki (rysunki sprzed 2026-10-11: drugi pręt „150 dalej”, za licem) — poziom jak w płycie (bez przesunięcia do nibu belki i nie pod ramieniem UB 03);
  kropka T IN NIB nachodząca na kropkę pręta belki (BN górą) — odsunięta w poziomie do styku lic (`ResolveTinBeamCollisions`).
  Pręty BN w przekroju: gdy w pasie / nibie belki jest UB 03 — w narożach spinki wewnątrz U (ramię ∓ (Ø spinki / 2 + d / 2), lica w styku);
  bez UB 03 — wzór (dołem otulina dolna belki + 10 + d/2 od spodu, górą wierzch nibu − otulina nibu − 10 − d/2). UB 03 to element płyty
  (otulina dołem płyty 40 + 5 = 45, bez zmian) — gdy jej otulina dołem w belce jest mniejsza niż otulina dolna belki, ostrzeżenie planera
  „UB 03 płyty w nibie belki: otulina dołem 45 < otulina dolna belki 50 …” (BN dołem nadal od UB 03). Rozkład belki jest „użyty” (odcisk) tylko,
  gdy powstała kropka / pręt. Odcisk BEAM: otulina boczna `Cv` i dolna `CvB` (zmiana otuliny → „zmieniona belka …”, rozstrzyga skrót wyniku).
  Wymiary: nib belki (szerokość, uskok H − NibH, wysokość nibu) zamiast nibu płyty, H belki (dalej od krawędzi niż łańcuch), szerokość /
  długość pasa pod belką (`BeamDimBelow` 350); grubość płyty — przy końcu bez belki.
- **K6 — COFNIĘTE 2026-10-11 (decyzja użytkownika: „przekroje teraz źle wychodzą”):** RC_SECTION wrócił do stanu e1644bd
  (`SectionPlanner.Version` = 1): bez odgięć 45° na końcach prętów, bez rysowania pionowych U-barów BU (pominięte w przekroju),
  UB SIDE (BC, kod z generowania K4) — też pominięty (bezpiecznie: bez kropek w złych miejscach), opisy przy belce i wymiary jak
  w e1644bd (bez grupowania ze strzałką, bez przesuwania wymiaru pod belką), A strzemienia w przekroju dawnym wzorem
  (`StirrupHeight`, nadpisywane wymiarem A rozkładu BL), `BeamParamsText` bez pól v2. Poprawka siatki przy belce w przekroju
  (b5d7e7f, `MeshLevelBesideBeams`) — cofnięta razem z K6, **przywrócona decyzją użytkownika 2026-10-11** („bez kropek siatki w trzonie
  belki”, `SectionPlanner.Version` 4). Generowanie (K1–K5, K7, K8) bez zmian.
- **Zakończenia prętów w widoku (zasady Z1–Z6, decyzja użytkownika 2026-10-11; `SectionPlanner.Version` 3, POPRAWKI_WZORCA 32):** `Input.EndStyle`
  — tylko None / Tick (None = dawny rysunek — złote wejścia wzorca bit w bit; `PrepareSection` ustawia `SectionPlanner.DefaultEndStyle` = S1 „UK tick”
  — JEDEN styl w programie, wybrany przez użytkownika 2026-10-11; bez pola w oknie i bez zapisu w rekordzie / odcisku; style S2 „krótki hak” / S3 „kreska + kropka”
  odrzucone i usunięte z kodu). Kreska 45° (długość na stałe `SectionPlanner.DefaultEndSymbolLength` = 30 mm — wybrane przez użytkownika 2026-10-11 z arkusza
  porównawczego 50 / 40 / 30; bez pola w oknie, bez parametru wejścia i bez zapisu) na rzeczywistym końcu pręta prostego i wolnych końcach ramion pręta giętego (`BarLine.End0 / End1`),
  do wnętrza (górny w dół, dolny w górę; gięty — w stronę środka obwiedni pręta), cały w betonie, zasięg pręta bez zmian (kreska kończy się na końcu
  pręta — ramię skrócone o L/√2); urwanie / koniec cięcia / przycięcie do betonu — bez symbolu; strzemiona, kropki, ramiona prętów giętych belki — bez.
  Zakład (koniec pręta wewnątrz poziomego odcinka innego pręta na tym samym poziomie): pręt prosty odsunięty o `LapOffset` 25 mm do wnętrza
  (`ApplyEndSymbols` / `OffsetLaps`, po kolizjach kropek, przed opisami). Opisy: kandydaci położenia z drogi sprzed symboli (`BarLine.LabelPath`),
  symbole — przeszkody dla strzałek. Przekroje wersji 1 / 2 → P2 („nowa wersja rysowałaby inaczej”), bez alarmu.
- **Pręty belki w detalu 1:25 (K9):** `PrepareSection` czyta ANCHOR-y detali (RC_BEAM_DETAIL, rola ANCHOR — nie są obrysami rzutów) → strefa
  po handle; ANCHOR nieprzystający do strefy przesuniętej o Shift → `ctx.Info` „Detal belki n nie pasuje do strefy … — ponów Beam” i pominięcie
  jego prętów. Rozkład belki (także UB 03 przy belce w detalu — decyzja 2026-10-11) ze znacznikiem strefy ANCHOR-a i środkiem w oknie detalu (+ 600) → położenie rzutu pos − Shift, dalej jak rozkład
  na rzucie (− przesunięcie rzutu strefy). `ctx.Views` / odcisk VIEWS bez zmian — wejście planera jak z prętami na rzucie (`SectionPlanner.Version`
  bez zmian); RecomputeSection / CHECK / UPDATE tą samą drogą (przekrój z prętami na rzucie → Beam w detalu → aktualny).
- Dane z rysunku (`AutoRebarEngine.PrepareSection`): obrys zewnętrzny (NibDetector), niby, otwory (`FindHoles`),
  pale (`FindPiles`), rozkłady RC_BAR_BLOCK ze WSZYSTKICH rzutów tej płyty (polilinie o tym samym kształcie, przesunięte —
  jak `FindBottomView`), grubość i SSL z opisu płyty („SSL=21.925”, „225mm THK SLAB”). Wszystko do poprawy w oknie;
  wysokość nibu domyślnie 150 (edytowalna), otuliny: dół 40, góra 35, góra w nibie 30, boczna 40.
- Geometria czysta (`SectionPlanner`, testowalna): odcinki betonu na linii cięcia (otwory = przerwy, koniec cięcia w płycie =
  linia urwania), nib na krawędziach przeciętych przez linię (uskok = grubość − wys. nibu). Poziomy: B1 = otulina dołem,
  B2 nad B1, T1 = otulina górą, T2 pod T1; UB — nogi na poziomach B/T swojego kierunku, UB 03 i T IN NIB — w nibie
  (otulina w nibie). Pręty prostopadłe do cięcia → kropki w rzeczywistych położeniach; równoległe → pręt najbliższy linii
  cięcia (≤ ½ rozstawu) w widoku (prosty / U). Pale przecięte linią: cięciwa, przerywana część w płycie (35), urwanie.
- Siatka w przekroju to SCHEMAT (jak w ASD), nie położenia z rzutu: pręty dołu i góry (prostopadłe do cięcia) w tych samych
  miejscach. Przy krawędzi z nibem: jeden pręt dołu w nibie (otulina 40 od krawędzi zewnętrznej), pierwszy pręt siatki 150 dalej,
  reszta co rozstaw nominalny z opisu (jakby nibu nie było); krawędź bez nibu / otwór — pierwszy pręt na otulinie 40.
  Kropka na pionowym ramieniu pręta w widoku (UB) — kolumna (góra + dół) odsunięta o średnice + 8 mm.
- Pręty w widoku = kopia pręta wzorcowego rozkładu (RC_SINGLE_BAR: kod kształtu, A–E, średnica), obrys z grubością;
  orientacja dopasowana (U: zagięcie przy krawędzi, dolne ramię na poziomie dołu). Wymiary: CADS_DIM_20 albo własny
  „RC SECTION 1-20” (nigdy „RBCT DIM 20”).
- Pręty w widoku mają DOKŁADNIE wymiary pręta wzorcowego z rysunku: wymiary BS 8666 są zewnętrzne, więc oś = ramiona − d/2,
  wymiar poprzeczny U / spinki − d (UB 225: B = 225 − 40 − 35 − 5 − 5 = 140 po zewnętrznej) — od W4e liczy to wspólna geometria
  (`BarGeometryBuilder.GetLocalPoints` z `ExternalDims`; dawne `AutoRebarEngine.AxisParams` usunięte — liczyło też 22 B − d/2
  zamiast − d; łuk osi r + d/2). U-bary (UB 01/02 i UB 03):
  dolne ramię na otulinie dołem + 5 mm zapasu, wysokość z pręta. T IN NIB — w spince UB 03, tuż pod jej górnym ramieniem.
- Otulina + ZAPAS 5 mm (okno: „Zapas góra / dół”) dotyczy WSZYSTKICH prętów: pierwszy pręt góry 35 + 5 = 40 od wierzchu,
  dołu 40 + 5 = 45 od spodu. Pręty jednego kierunku (główne i ramiona UB tego kierunku) leżą na jednej linii i nachodzą
  na siebie: UB 01 z B1/T1, UB 02 z B2/T2. Litera znacznika przekroju obrócona wzdłuż linii cięcia (cięcie pionowe → 90°).
- Nib: numery T IN NIB
  w jednym rzędzie nad nibem (pierwszy między uskokiem a krawędzią, kolejne dalej na zewnątrz).
- Styl rysunku jak ASD: SSL (trójkąt, linia, tekst) żółty; oś pala SD-Pile, CENTER, skala linii 2, czerwona; część pala
  w płycie DASHED skala 3; DBText z szerokością ze stylu (ROMANS NARROW 0.8); wymiar szerokości nibu 300 nad płytą,
  łańcuch uskok/wysokość 250 od krawędzi, tekst wymiaru między liniami pomocniczymi (DIMTIX); opisy ze strzałką od rzędu 2,
  grot tam, gdzie obok nie leży inny pręt w widoku.
- Rysunek 1:1 w modelu, opis „SCALE 1:20” (tekst 50, wymiary CADS_DIM_20 jeśli jest), warstwy jak na rysunkach ASD:
  s-Slab, s-Pile, 0-25TEXT, AP-TEXT, DIM, SD-SECTION (znaczniki), RC-SECTION-BARS (pręty). Numery pozycji nad / pod
  kropkami (kolejne rzędy przy kolizji), pręty w widoku — numer ze strzałką. Znaczniki przekroju na wszystkich rzutach płyty.
- Etap 2 (do zrobienia): kształty prętów w widoku wg kodu kształtu (haki, odgięcia), zakłady, ponowne generowanie przekroju.
- **Bezpiecznik aktualności przekrojów** (docs/PLAN_BEZPIECZNIK_PRZEKROJOW_2026-10-10.md; etap 0 = spike, etapy 1–4 zrobione):
  - Geometria przekroju bit w bit jak wzorzec — dochodzą tylko dane (XData `RC_SECTION` / `RC_SECTION_PART`, Xrecord, sekcja 1)
    i chmurka. `DrawSection(…, rec)` znakuje części w chwili dodania (kolejność obiektów bez zmian); `rec = null` = dawny rysunek.
  - Planer oznacza „istotne wejście” bez wpływu na wynik: `Result.UsedDists` (rozkłady z ≥ 1 kropką / prętem w widoku, w chwili
    dodania — przed AlignMesh, referencją), `UsedPiles`, `LevelDiameters` (dB1, dT1); `Dist.SourceHandle / SourceDims /
    SourceBarHandle` (PrepareSection). **`SectionPlanner.Version`** — podbić przy KAŻDEJ zmianie wyniku planera dla tego samego
    wejścia (złoty test `SectionPlannerGoldenTests` — 5 wejść, pełny Result bit w bit); starszy przekrój = „aktualny (nowa wersja
    rysowałaby inaczej)” (P2).
  - Odcisk (`SectionFingerprint`): składniki META, VIEWS (zbiór przesunięć rzutów; nowy / usunięty rzut z przesunięciem), CUT (tylko kierunek, strona i długość), LETTER, PARAM, SRC (opis płyty + flagi
    nadpisania), SEG#i, LVL, DIST:<handle>, PILE:<x,y>, **BEAM:<handle strefy>** (E5: tylko belka przecięta linią cięcia — `Result.UsedBeams`;
    pas S0 / S1 względem P1, H, nib, otulina, Ø i A strzemienia, parametry rekordu; DIST prętów belki — dodatkowo skos, drugie ramię, Layers;
    **brak belek = odcisk bit w bit jak przed E5**, test z odciskami HEAD ecaa083); Compare: zapisany odcisk bez żadnego BEAM, bieżący z belką →
    zawsze NIEAKTUALNY „belka … nieuwzględniona w przekroju (przekrój sprzed obsługi belek albo belka dodana później)”; zmieniona / usunięta /
    nowa belka — przyczyna, rozstrzyga skrót wyniku (np. „zmieniona belka 0000610D (H 375 → 450, …)”; same parametry — „bez wpływu na rysunek”); współrzędne względem P1 — punktu cięcia na rzucie wskazanym (nie MinPoint obrysu: STRETCH krawędzi daleko od cięcia bez alarmu, MOVE rzutu ze znacznikami bez alarmu; przesunięcie cięcia względem płyty wyłapują DIST / SEG i skrót wyniku); liczby `Math.Round(x, 1)` „0.0” Invariant (−0 → 0), SHA-256; skrót wyniku
    (ResultDigest, U − UMin) = rozjemca. Werdykt: hash równy → aktualny; VIEWS / CUT / LETTER różne → zawsze NIEAKTUALNY; inne
    różnice (tolerancja 0,15 mm, rozkłady dopasowane po handle, zapasowo po pozycji / warstwie / rodzaju / kierunku i najbliższym
    położeniu) przy równym skrócie wyniku → aktualny („zmiany bez wpływu na rysunek”), przy różnym → NIEAKTUALNY z przyczynami.
  - Przeliczenie (`AutoRebarEngine.RecomputeSection`, tylko odczyt): cięcie ze znaczników rzutu wskazanego (`V0.P1/P2`, wierzchołek
    [2]; brak → wartości bezwzględne z rekordu), ta sama droga co tworzenie (PrepareSection → Plan), grubość / SSL z bieżącego
    opisu płyty, chyba że w oknie nadpisane ręcznie (P1); otwarty nib do domknięcia (`NibAutoClose.WouldClose`) → NIEAKTUALNY.
  - Statusy (`SectionStore`): aktualny, NIEAKTUALNY, osierocony (obrys usunięty; podpowiedź obrysu w tym samym miejscu), nieznany
    (stary tytuł „SECTION X-X” bez danych), kopia ([6] ≠ handle albo ten sam GUID co tytuł o mniejszym handle — PASTECLIP / INSERT tłumaczą [6]; części kopii wg [1]), części bez tytułu (tytuł usunięty — ERASE), uszkodzony; ostrzeżenia: litera w tytule ≠ [3],
    „litera X użyta n×” (przekrój bez znaczników na rzucie: „kopia przekroju X (bez znaczników na rzucie) — litera użyta n×”). `ReadAll` — jeden przebieg; bez RegApp przekrojów (i bez szukania starych tytułów) — szybkie wyjście.
    Bez cache (etap 1) — czas w logu (`SectionStore.CheckAll`); pomiar Holbrook PLOT 321 (1408 obiektów, 4 przekroje): CheckAll 45 ms, RC_SECTION 70–280 ms.
  - Chmurka „NIEAKTUALNY”: polilinia z łukami + MText na warstwie `RC-SECTION-STATUS` (kolor 1, **Plottable = false** — spike:
    nie trafia do -PLOT ani EXPORTPDF), części roli CLOUD. Ustawiają ją RC_SECTION_CHECK, RC_SECTION (wszystkie przekroje) i RC_BBS;
    zapis tylko przy zmianie stanu (CHECK bez zmian = zero zapisów).
  - RC_BBS: `SectionGuard.ConfirmBeforeOutput` przed oknem BBS — okno Tak / Nie w komendzie; ScriptMode — komunikat i dalej.
  - **Wydruk / PDF / PUBLISH (etap 2, `SectionGuard.Register`):** `DocumentCollection.DocumentLockModeChanged` raz na aplikację;
    komendy PLOT, -PLOT, PRINT, EXPORTPDF, PUBLISH, -PUBLISH (nazwy ze spiku; „#…” = odblokowanie i wyrażenia LISP „(…” pomijane);
    tylko pierwsze zdarzenie komendy najwyższego poziomu (flaga per dokument, kasowana w CommandEnded / Cancelled / Failed komendy
    najwyższego poziomu), `_inGuard`, `e.Document`. Sprawdzenie tylko do odczytu (`SectionStore.CheckAll`, bez chmurek — chmurki
    ustawiają komendy), czas w logu, bez cache. Przekroje nieaktualne / osierocone / uszkodzone / kopie: interaktywnie Veto bez okna,
    pytanie na Idle (`TextConfirmDialog` przez `ShowModalWindow`: „Tak, drukuj” / „Nie”), po „Tak” ta sama komenda przez
    `SendStringToExecute` z jednorazowym obejściem; tryb bez okien — tylko komunikat, BEZ Veto (spike: odpowiedzi skryptu stałyby się
    komendami); `RC_SG_TEST=veto` — Veto także w trybie bez okien (testy). PUBLISH rysunków nieotwartych — nie sprawdzane (ograniczenie).
    **Wywołanie z LISP** (zdarzenie blokady z nazwą „(…” w tym rysunku — kasowane przy „#(…” i na końcu komendy — albo bit 32
    `CMDACTIVE`): jak tryb bez okien — tylko komunikat, bez Veto (Veto zgubiłoby argumenty `(command …)`: reszta trafiłaby do linii
    poleceń jako komendy, np. „_N” = QNEW). **Ograniczenie:** makra menu / przycisków bez LISP (np. `^C^C-PLOT;_N;…`) nie da się
    odróżnić od wpisania komendy — dostaną Veto i pytanie, a dalsze odpowiedzi makra trafią do linii poleceń jako komendy.
    Obejście „Tak, drukuj” ważne 10 s (kasowane na końcu komendy po tym czasie). `RC_SG_TEST=interactive` — testy: zachowanie
    interaktywne w /automation (bez okna).
  - **Otwarcie rysunku (etap 2):** po podpięciu dokumentu (DocumentWatch) jednorazowe sprawdzenie na Idle aktywnego rysunku: bez
    RegApp przekrojów — szybkie wyjście; komunikat `[RC SECTION] Rysunek ma przekroje nieaktualne …` w linii poleceń; bez zapisów (P3)
    i bez SendStringToExecute. W /automation Idle przychodzi dopiero po skrypcie (test SGO woła `SectionGuard.ProcessPending`).
  - **RC_SECTION_UPDATE (etap 3, `SectionUpdater`):** [Wskaż / Wszystkie nieaktualne]; przeliczenie tą samą drogą co RC_SECTION
    (`NibAutoClose.EnsureForCommand`, `SectionStore.CurrentCut` — cięcie i litera ze znaczników, `RecomputeSection`); przesunięcie
    całego przekroju = mediana próbek betonu (wierzchołek [0] odcinka względem TOPLEFT + Def), tytuł jako rozjemca przy ≥ 2 odcinkach,
    sam tytuł — gdy betonu brak (`SectionLabelCarry.SectionShift`); ręczne przesunięcia opisów (`SectionLabelCarry.Carry`: tekst + rodzaj,
    zachłannie po najbliższej pozycji domyślnej ≤ 300 mm; leader: tekst i początek przesunięte, grot o dU tylko, gdy zostaje na pręcie),
    tytułu i liter znaczników (względem polilinii znacznika) → `DrawSectionWith(…, SectionDrawOptions)` (opcje null = RC_SECTION bit w bit);
    usuwane tylko części potwierdzone w obu kierunkach (+ chmurki, stary tytuł); opisy usunięte ręcznie odtwarzane z komunikatem (P5);
    przekrój bez znaczników (kopia) — bez dorysowania znaczników; nowy rekord z tym samym GUID i flagami nadpisania (P1); jedna
    transakcja w komendzie (jeden Ctrl+Z / Ctrl+Y). Obiekty z [1] → tytuł spoza listy rekordu → przerwanie (zabezpieczenie liczności).
    Kopia / uszkodzony → odmowa; osierocony z obrysem w tym samym miejscu → pytanie Tak / Nie (tryb bez okien: pominięty).
    Komunikat `[RC SECTION] B-B zaktualizowany: przesunięte opisy zachowane: n; bez dopasowania: k (poz. …).`
    Rekord etapu 1 (bez pozycji domyślnych): przesunięcie przekroju z betonu względem nowego planu, opisy i tytuł domyślnie (komunikat).
  - **Adopcja starych przekrojów (etap 4, P6):** RC_SECTION_UPDATE na przekroju „nieznany” (wskazany stary tytuł): znaczniki SD-SECTION
    tej litery rozpoznane dokładnie (kształt DrawSection 83 / 294 / 300 mm ± 2, litera ≤ 300 mm od miejsca domyślnego, bez
    RC_SECTION_PART), pary P1–P2 spójne (kierunek, strona, długość) — inaczej odmowa; obrys płyty = jedyny zewnętrzny zamknięty obrys
    przecięty linią cięcia (inaczej wskazanie; tryb bez okien — odmowa); obiekty starego przekroju wskazuje użytkownik oknem (tryb bez
    okien: handle w zmiennej `RC_SG_ADOPT`; wskazanie tytułu — `RC_SG_PICK`); litera użyta przez > 1 tytuł → odmowa („adopcja
    niejednoznaczna”); z wskazanych usuwane TYLKO obiekty rysunku przekroju: warstwy DrawSection (s-Slab, s-Pile, RC-SECTION-BARS,
    0-25TEXT, AP-TEXT, DIM), typy Polyline / Line / DBText / MText / Solid / Circle / Dimension, bez XData żadnej aplikacji „RC_…”;
    na SD-Pile tylko linia CENTER (oś pala przekroju) w obwiedni betonu / pali starego przekroju (+ 100 mm) — pale rzutu zostają;
    reszta liczona jako „pominięte wskazane obiekty”;
    punkt wstawienia = lewy górny narożnik betonu (s-Slab) wskazanych obiektów (brak → wskazanie); okno parametrów (`SectionDialog`)
    z wartościami z rysunku; jedna transakcja: usunięcie wskazanych obiektów, tytułu i znaczników tej litery + nowy przekrój z rekordem.

## 4h. B3 ADD — pręty ukośne w narożnikach wklęsłych (AutoRebarEngine.B3.cs)
- Wymaganie użytkownika: w każdym narożniku wklęsłym (kąt wewnętrzny > 180°) **2 H10 co 100, L = 1250**, opis
  „2 H10-nn-100 B3 ADD” (kod warstwy B3, warstwa CAD jak B1, cyan jak każde ADD).
- Pręty prostopadle do dwusiecznej narożnika, wyśrodkowane na niej; pierwszy 75 mm od wierzchołka (B3CornerOffset),
  drugi 100 dalej w płytę. Rozkład obrócony (Angle w (-90°, 90°], Pt1 = początek pierwszego pręta).
- Plan: narożniki wklęsłe obrysu dolnego (przy nibie — obrys zewnętrzny); RC_GENERATE → B3 (RC_GENERUJ_B3) i na końcu Mesh.
  Ponowne wywołanie zastępuje B3 ADD płyty. Opis w pustce za narożnikiem: linia rozkładu przedłużona przez wierzchołek,
  ramię pionowe (poziome, gdy dwusieczna bez składowej pionowej). Pręt wychodzący poza płytę — komunikat.
- Detal otworu: 4 narożniki otworu (dwusieczna 45°, na zewnątrz otworu); opis od strony dalszego pręta, ramię pionowe za
  ramkę detalu (dolne w dół, górne w górę), ≥ DetailLabelSep od pozostałych opisów pionowych.

## 4i. Belki / pogrubienia (downstand) — plan docs/PLAN_BELKI_2026-10-10.md
**Decyzje Bartka 2026-10-12 (odpowiedzi na „pytania do Bartka”, docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md §5) — obowiązują ponad opisem niżej:**
otuliny belki górna 35 / dolna 40 / boczna 40, każda + 5 mm zapasu → `DefaultCoverTop` / `CoverTopA1` 40, `DefaultCoverBottom` 45, `DefaultCoverSide`
45 (A strzemienia = H − 85 jak dotąd); belki niskie H ≤ 375 (`ShallowBeamMaxH`) — Auto zawsze dwa zachodzące strzemiona (4-cięte, także w płycie)
(B jednego strzemienia sztywno 370 — `FourLegFixedB`; belki wyższe Auto ZAWSZE 2-cięte, przekroczenie s_t,max EN 1992-1-1 tylko jako
ostrzeżenie; w rzucie pręt BL rysowany na szerokość strzemienia z szablonu — 370 przy 4-ciętych) i pytanie przy generowaniu „pręty główne [H12/H16]” (zapis w rekordzie obu rzutów; U-bary pionowe tak samo); narożnik L — noga pionowa (Y)
prętami prostymi w kwadrat do lica zewnętrznego, noga pozioma (X) przed kwadratem (`StraightRange`; [31] — obie w kwadrat); ramię pionowego
U-bara końca = zakład standardowy (40·Ø, [35] domyślnie 0 zamiast 90); strzemiona „tyle, ile się zmieści” — rozstaw = s, liczba w dół,
reszta po równo na końcach (528: 51); opis „-150” przy rzeczywistym ~157 — zostaje; strzemiona niezależnie od UB 03 — zostaje; cała belka
w serii dolnej — potwierdzone; U-bary belki „nn UB” (≥ 04) — OK; nib w przekroju jak w rzeczywistości (geometria). Pręty boczne i UB SIDE —
później. Po UB 03 w detalu (`GenerateUBLayerCore`) — ponownie `ResolveRepresentativeCollisions` w ramce detalu (UB 03 i strzemiona
nie na sobie). Rekordy stref zapisane wcześniej trzymają swoje otuliny / naddatek — nowe wartości po ponownym Prepare GA albo w oknie RC_BEAM.
**Decyzje Bartka 2026-10-13 (wysokości — warstwy):** strzemiona odcinków wzdłuż Y — 1. warstwa, A = H − 40 − 45 (450: 365); wzdłuż X —
2. warstwa, A − 15 (350; odsunięte od górnej siatki o pręt 12 + 3 zapasu) — `BeamGeometry.LinkHeightFor` / `SecondLayerLinkDrop`, osobne
pozycje strzemion X / Y. Pionowe U-bary (`PlanVerticalUBars`): B = A strzemienia własnej warstwy − 2·Ø strzemienia **w dół** do 5 (`Floor5` — tak liczy kod;
koniec X 325, koniec / narożnik Y 340); noga X narożnika L i belka dochodząca złącza T — wewnątrz U-barów 1. warstwy: 340 − 2·Ø U-bara
w górę do 5 (H16 → 305; [30] `UBarDrop` 0 = auto, dawniej 35 na nodze Y). Zakład U-bara nogi X liczony od końca prętów prostych przed
kwadratem narożnika (bez zmian). Strefa z opisów THK (`ZonesFromThk`): sąsiednia krawędź z linią strefy tylko przy narożniku (krótka noga L)
— dołączona od narożnika do dzielnika / końca linii strefy.
**Decyzje Bartka 2026-10-13 (detal):** ramka detalu = offset strefy (`BeamDetailRules.FramePolygon`: strefa ⊕ 600 ∩ obrys ⊕ 300,
kwadratowe naroża — belka L / U daje ramkę L / U, ten sam styl: czerwona przerywana, na rzucie i w detalu); linie rzutu, obrys, uskok
i pale przycinane do wielokąta ramki (`ClipToPolygon`); okno (`DetailWindow`) = obwiednia ramki. UB 03: krawędź, przy której strefa
w detalu leży tylko na części (krótka noga L) — część przy belce w detalu, reszta na rzucie (`SplitEdgeByDetailZones`). UB 03 wzdłuż belki w detalu: krawędź = zakres strzemion odcinka (Own) + nib na
końcu belki przy krawędzi (szerokość nibu mierzona, ≤ 260) → strzemiona + 1 na każdy taki koniec (od krawędzi do krawędzi + 2,
jeden koniec + 1); inaczej (narożnik L, strzemiona przed krawędzią) — od otuliny do otuliny, rozstaw ≤ 200 (n = ⌈L / 200⌉ + 1).
Strzemiona: pierwsze zawsze przy licu zakresu — oś 40 + Ø/2 (`LinkEndCover`, H12: 46), rozstaw s, liczba w dół (tyle, ile się
zmieści), reszta przy drugim końcu (wcześniej otulina 45 i reszta po równo na obu końcach); gdy ostatnie wypada dalej niż 80 od lica
(`LinkMaxEndGap`, np. 166 przy kwadracie narożnika) — +1 strzemię, rozstaw równy ≤ s, oba końce 40 + Ø/2.
Koniec belki przy krawędzi (UB 03 co 150 + pojedynczy UB 03 drugiego kierunku, `BeamEndUbPortion`) — dla każdego odcinka strefy
(także noga L / U; wcześniej tylko strefy prostokątne). Strzemiona przy załamaniu (narożnik L) — obie nogi kończą się przed kwadratem
narożnika (kwadrat zbrojony U-barami; [34] nieużywany, w oknie wyłączony). Pionowe U-bary narożnika L / złącza T — też 1 pręt widoczny (wcześniej wszystkie); w detalu rozkłady bez wygaszenia są przeszkodą
w `ResolveRepresentativeCollisions`.
**Decyzje Bartka 2026-10-13 (siatka przy końcu belki w płycie):** koniec WEWNĘTRZNY odcinka belki — pręty siatki równoległe do belki
(dół i góra), dochodzące do jej końca, kotwią się W BELCE na `MeshAnchorIntoBeam` (40·Ø w górę do 50 + 50: H10 450, H12 550; zakres
Bartka H10 450–550, H12 550–700) — `MeshCutRects` skraca odcięcie pasa o kotwienie + otulinę. T IN NIB na krawędzi, przy której belka
leży tylko na części — tylko poza belką, do linii uskoku w nibie (lico końca belki; dalej pręty w nibie belki w detalu). Pojedynczy UB 03
na uskoku w nibie przy końcu wewnętrznym belki krawędziowej (pręty wzdłuż nibu, zagięcie na uskoku, ramiona w stronę belki) — w detalu
(`endExtras`); po stronie PŁYTY drugi pojedynczy UB 03 na tym samym uskoku — na rzucie dolnym (`planSteps`, też przy końcu belki przy
krawędzi; tylko pełny przebieg Mesh / UB). UB 03 przy belce w trybie detalu, gdy detalu jeszcze nie ma — NIE na rzucie (`NibUbTarget.Skip`;
wcześniej rysowane na rzucie i znikały po Beam); Beam rysuje je w detalu.
Status: **E1 + E3 + E4 + E4b + E5 + E6 zrobione (staging)**; E2 (siatka przy belce) — **K7 (staging)**.
**Praktyka biura (docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md): K1–K9 zrobione (staging, do testów Bartka; K9 — detal 1:25, niżej)** — opis E3–E4 niżej jest
HISTORYCZNY tam, gdzie K2 / K3 go zmieniają (strzemiona, pręty podłużne, zamknięcia końców, numeracja):
- **K2 — strzemiona i rekord v2:** strzemię 51 **H12** co 200, **A = H − otulina górna [24] − otulina dolna** (A1 35 → 450: 365, 375: 290,
  300: 215; A2 50 → 450: 350), B = szer. − 2 · 45 (510), oba w dół do 5 (`BeamGeometry.StirrupHeightCovers`); **pierwsze (i ostatnie)
  strzemię w osi otulina + Ø/2 = 51 od lica** zakresu strzemion — także przy U-barach (`StirrupFirstOffset`; usunięte `StirrupEndOffsetAtUBars`
  / 125); strzemiona **niezależnie od UB 03** (parametr [33]; opcja +100); w kwadracie narożnika L — dłuższa noga ([34]; opcja brak).
  Zakład 40·Ø ([27]). Pręty boczne — tryb [25] / [26] (B3: H ≤ 450 brak; opis i UB SIDE — K4), BN górą 0, [21] — pręty w nibie (od 2026-10-11
  2T+1B H10; dawniej BN dołem H10 B IN NIB — K5).
  Bez ostrzeżenia o otulinie UB 03 (generowanie i RC_SECTION; złoty `beam` — zmieniony tylko o tę linię WARN). Okno `BeamDialog`: wszystkie
  pola v2 (A1 / A2, tryby, krotność zakładu, opcje narożnika). Rekord v1 — migracja w pamięci (sekcja 1).
- **K3 — pręty podłużne i pionowe U-bary** (`BeamGeometry.PlanLayer` + `PlanVerticalUBars`, `AutoRebarEngine.GenerateOneBeam` /
  `BeamVerticalUBar`; WSZYSTKO na rzucie dolnym, seria dolna; stare BT na rzucie górnym usuwane przy ponownym generowaniu):
  - dół = góra (Ø, liczba) i [29] → **BM** jeden rozkład 4 prętów (rysowana jedna warstwa), CountDisplay 8, opis `2x4 H16-nn-150 B+T`;
    inaczej BB i BT (oba w serii dolnej, komunikat); zakres prętów prostych `StraightRange`: koniec z U-barami — lico ∓ (otulina + Ø U-bara)
    = 61 ([32]), narożnik L / belka dochodząca złącza T — lico kwadratu / bliższe lico belki ciągłej (przed kwadratem — C12, domyślne; [31] —
    w kwadrat do lica zewnętrznego ∓ 61), złącze z odcinkiem ręcznym — lico z otuliną 45; plan `PlanBars` z zakładem 650 (528: 5500 + 5250);
  - **pionowe U-bary BU** kształt 21 na każdym końcu z zamknięciem (koniec przy krawędzi i wewnętrzny, każda noga narożnika L, belka
    dochodząca złącza T; belka ciągła złącza T — bez przerwy, bez U-barów w węźle): po jednym w kolumnie prętów (liczba = kolumny prętów
    głównych: BM — liczba prętów warstwy, BB / BT — mniejsza; 4, osie jak pręty podłużne); **koniec belki (przy krawędzi i wewnętrzny) — w rzucie /
    detalu widoczny 1 pręt reprezentatywny** (decyzja użytkownika 2026-10-11 „U-bary zamykające pokazuj 1 a nie 4”, korekta „tyle samo co prętów
    głównych, więc 4” — `BeamVerticalUBar` repr 0; opis i BBS nadal wszystkie: `4 H16-nn-150 UB`, 528 / 539 — 8 szt. na belkę); narożniki L /
    złącza T — wszystkie 4 widoczne (bez zmian),
    **B = A strzemienia − 2·Ø strzemienia** (528: 265, 319: 340, 539: 190), noga Y narożnika L i belka dochodząca T — **B − 35** ([30]);
    tył na otulinie 45 od lica (narożnik — lica zewnętrznego narożnika, T — dalszego lica belki ciągłej); **A = C**: koniec —
    Ceil25(max(zakład + 90 [35], tył → pręt prosty + zakład)) = 750, narożnik / T — Ceil25(tył → początek prętów prostych nogi + zakład)
    (539: 1225 / 1300, 319: 1225 / 1325); ramię przycinane do zakresu prętów prostych + komunikat; ramiona z obu końców nachodzą — komunikat;
    opis `4 H16-nn-150 UB`; w rzucie linia ramienia od tyłu U w głąb belki z **kółkiem na zagięciu** (SymbolType Circle, jak UB płyty);
  - pręty L (11) i poziome U-bary E4 **usunięte**; RC_SECTION: BM — kropki na obu poziomach (górne z flagą warstwy górnej), BU i BC — pominięte, A strzemienia — dawny wzór (K6 cofnięte 2026-10-11, sekcja 4g).
domyślne = rozstrzygnięcia 1b / 1c planu i wymagania 2026-10-10 (E4b) — **do potwierdzenia przez Bartka**
(parametry w oknie RC_BEAM, stałe w `BeamParams` / `AutoRebarEngine.Beam`).
- **Otuliny belki (decyzja użytkownika 2026-10-10 — ustalone):** boczna / czołowa strzemion **45** (40 + odchyłka; boki i czoła belki:
  strzemię, pręty podłużne, boczne, U-bary i ich tyły, pręty L, pierwsze strzemię za U-barami, końce prętów BN), dolna **50** (spód belki
  i nibu belki), górna **35** bez zmian (pod siatką T). Parametry `BeamParams.CoverSide` / `CoverBottom` (stałe domyślne
  `BeamGeometry.DefaultCoverSide` / `DefaultCoverBottom`), okno RC_BEAM („Otulina boczna / czołowa”, „Otulina dolna”), rekord RC_BEAM [22] / [23];
  rekord bez tych pól (sprzed decyzji) czytany z NOWYMI domyślnymi 45 / 50 (ponowne generowanie belki daje nowe otuliny). Położenie prętów BN
  w poprzek — od spinki UB 03 płyty (`BeamGeometry.NibUbEdgeCover` = 40, element płyty bez zmian); generowanie zgłasza, gdy UB 03 płyty
  (otulina dołem 45) leży niżej niż otulina dolna belki (od K2 — bez tego komunikatu). E4 (historycznie): 528 strzemię 51 A = 265, B = 510;
  U-bary 800 / 775 × B 490 / 170; od K2 / K3: A = 290, U-bary pionowe 750 / 265 / 750.
- **K7 — siatka płyty przy belce (E2, POPRAWKI 30 — zmiana przyjęta decyzją użytkownika):** JEDNA funkcja obszaru `AutoRebarEngine.BeamMeshRegion`
  (strefy RC_BEAM tego rzutu — Prepare GA / RC_BEAM zapisują je na obu rzutach; bez szukania po obrysie przystającym; analiza odcinków względem obrysu zewnętrznego) → odcięcia pasów (`BeamGeometry.MeshCutRects`,
  suma `RectilinearPolygons.Union`) i obszary krawędzi bez UB 01/02 (`UbExclusionRects`); używane w `GenerateLayer` (B1…T2 — `DecomposeStrips`
  z odcięciami), `GenerateUBLayerCore` (UB 01/02: pasy prętów głównych na tym samym obszarze, krawędź przy belce — bez UB, komunikat) i
  `PlanNibStepBars` (krawędź uskoku w > 50 % przy belce — bez reguły UB 01/02; pręty góry z położeń dołu). Reguły (pytanie 11 — rozstrzygnięte
  decyzjami użytkownika 2026-10-11):
  pręty **równoległe** do odcinka belki — dołem od lica wewnętrznego + d (pierwszy 40 + d = 50), górą tak samo (**decyzja 2026-10-14**:
  dawniej górą s/2 = 100 — między belkami góra miała o 2 pręty mniej; teraz pas góry przy belce (`StripTouchesCut`) bierze położenia
  prętów dołu jak przy nibie (`BottomPositionsIn`, tolerancja 15 na różnicę Ø), pręty dołu w nibie pominięte: góra 49 + 3, dół 49 + 4);
  **prostopadłe** dołem i górą (decyzja 2026-10-11 „pręty prostopadłe płyty powinny wchodzić w belkę jak normalnie do 40mm side cover” —
  zastępuje wcześniejszą tego dnia „nie wchodzą w belkę”, 60e8a9c: koniec na licu wewnętrznym) — **przez belkę, koniec 40 od lica zewnętrznego
  belki** (`BeamGeometry.MeshBeamSideCover` = 40 — otulina boczna siatki w belce, inna niż otulina strzemion 45; `MeshCutRects` odcina tylko
  nib przed belką, granica = lico zewnętrzne ± (40 − otulina siatki)). Lico zewnętrzne = linia uskoku (strefa z GA / RC_BEAM [Krawedz] od linii
  uskoku; Holbrook 528 / 539) i ogranicza pręt na obu poziomach: przed nim jest tylko nib belki przy jej spodzie (−H … −H + 150, poniżej
  poziomu siatki dolnej płyty), góra i tak kończy się na linii uskoku − 40 (wielokąt siatki górnej). Wynik jak K7 przed 60e8a9c (dół do linii
  uskoku − 40, góra do linii uskoku − 40). Belka bez krawędzi płyty (OuterSide = 0) — prostopadłe przez belkę bez zmian.
  Na końcach przy krawędzi i w narożnikach L odcięcie do obrysu (nib zawinięty). **UB 03 przy belce:** pręty dołu prostopadłe kończą się
  40 od lica zewnętrznego belki (w nibie ≤ 260 + 40 od krawędzi) — liczba i położenia UB 03 = pręty siatki dolnej w oknie ± 300 od krawędzi,
  jak bez belki (jak K7 przed 60e8a9c; okno wydłużane do lica wewnętrznego — `BeamReachFromEdge` z 60e8a9c — usunięte, bo pręty znów
  dochodzą do nibu); brak siatki — dawny plan teoretyczny z pasów obrysu (rozstaw nominalny). **T IN NIB przy belce:** oba pręty w nibie
  zastąpione decyzją 2026-10-11 „jedna pozycja bottom in nib i top” — przy belce bez T IN NIB i B IN NIB, pręty w nibie 2T+1B z belki (K5 niżej).
  Zasada „liczba UB = liczba prętów” bez zmian (UB 01/02 — pasy na tym samym wielokącie co siatka; przy belce UB 01/02 brak);
  B3 ADD i Add — na obrysie płyty; odcinki „detal ręczny” — siatka jak bez belki. **Płyta bez stref — bit w bit** (odcięcia = null, dawny kod;
  test BRX D526). Obszar nie jest zapamiętywany (bez cache — UNDO). Testy BRX D319 / D528 / D539 (asercje E2, podsumowanie `beam-e2-<płyta>.txt`),
  M528 (belka → Mesh = Mesh bez belki tego przebiegu), S-REG FBEAM (Mesh bez Beam — przy belce bez T IN NIB). Komunikat Mesh „ponów Beam” (B IN NIB z siatki B2) — usunięty (pręty w nibie
  nie zależą już od siatki dolnej).
  `NibDetector.Detect` pomija warstwy RC-BEAM, RC-BEAM-DETAIL (ramki detalu na rzucie) i `SD-Thickening-Zone` (strefa z GA przed Prepare GA),
  `FindHoles` — RC-BEAM i RC-BEAM-DETAIL.
- **Detal 1:25 (K9, decyzja użytkownika 2026-10-11 „rób wg rekomendacji”; `AutoRebarEngine.BeamDetail`, czyste `BeamDetailRules`):** pręty
  belki rysowane TYLKO w detalu — rzut belki 1:25 w przestrzeni modelu w rzeczywistych wymiarach, z dala od rzutów (przekroje zostają osobno
  w 1:20, RC_SECTION bez zmian rysunku). Na rzutach 1:50: strefa, „xxxTHK”; **tylko na rzucie dolnym ramka okna detalu (czerwona przerywana)
  i odnośnik „DETAIL 'n'” z grotem na ramce** (rzut górny bez odnośnika — jak detal otworu) (decyzja użytkownika 2026-10-12; `PlaceNoteLeader`, wys. 140, bok ramki
  poza płytą: góra, dół, prawo, lewo; wcześniej grot w strefie). Siatka płyty — na rzucie; UB 03 przy belce — w detalu (niżej, „UB 03 w detalu”). Detal domyślny;
  opcja okna RC_BEAM „Pręty belki w detalu 1:25” (rekord [36]; odznaczone = rzut jak przed K9, przełączenie detal → rzut usuwa detal przy
  generowaniu). Jeden detal na strefę (L / U jako jeden), także dla belek identycznych; numer wspólny z detalami otworów (`NextDetailNumber`).
  - **Okno** (`BeamDetailRules.Window`): obwiednia strefy; bok, przy którym na zewnątrz (≤ 600) leży równoległa krawędź obrysu płyty —
    do krawędzi + 300 (widać nib i krawędź; także koniec przy krawędzi), inaczej bok strefy + 600 (lico wewnętrzne, koniec wewnętrzny).
  - **Miejsce:** istniejący ANCHOR strefy → numer i Shift = MinPoint(ANCHOR) − MinPoint(strefy) (bez cache — MOVE detalu i ponowne Beam:
    detal zostaje w nowym miejscu z tym samym numerem); inaczej **użytkownik wskazuje środek detalu** (decyzja 2026-10-12 „jak przy detalu
    otworu — sami klikamy miejsce”; Esc — pręty belki pominięte), Enter albo tryb bez okien (`ScriptMode`) — nowe miejsce nad rzutami
    (`BeamDetailRules.Place`: od lewego brzegu rzutów, 3000 nad nimi, w prawo aż obwiednia okna + 3000 na opisy + kolumna szablonów,
    + 1000 na tytuł, nie nachodzi z odstępem 1000 na żaden obiekt modelu).
  - **Zawartość (wygląd wg wzoru Bartka 2026-10-12):** ramka detalu = okno (czerwona przerywana, w odległości od belki — lico wewnętrzne
    + 600, krawędź płyty + 300), ANCHOR (lica belki — przerywana w kolorze obrysu płyty), przycięte linie betonu w kolorach i rodzajach linii
    z rzutu (obrys zewnętrzny i linia uskoku — osobne `Line`, nigdy zamknięty obrys; **wszystkie pozostałe linie rzutu w oknie** (Line /
    Polyline / Arc, dowolna warstwa — „co jest w czerwonej obramówce”, decyzja 2026-10-12) poza obiektami programu (XData RC_… inne niż RC_GA,
    warstwy RC-BEAM*, rebar_*) i SD-Text / SD-Pile… / SD-Thickening-Zone, np. uskok w nibie na końcu belki — `OtherPlanLines`), pale w oknie
    (przerywane okręgi), teksty „xxxTHK” strefy (62,5), wszystkie pręty belki (BM, LINKS, BU, BS / BC, 2+1 T+B IN NIB) z opisami, tytuł pod
    najniższym opisem (jak detal otworu: 125 / 0,8×), bez wymiarów i bez znaczników przekroju (później). Warstwa `RC-BEAM-DETAIL` — nic na
    SD-PILED-RAFT, SD-Pile, SD-Text, RC-BEAM (czytają je NibDetector, NibAutoClose, FindPiles, przebicie, ZonesOfSlab, PrepareSection).
  - **Pręty:** `BeamDrawTarget` (Shift, okno, obrys przesunięty, skala) w `BeamDistribution` / `BeamLongitudinal` / `BeamVerticalUBar` /
    `BeamSideUBar` / `NibPairLeader` — położenie rzutu + Shift, linie odniesienia za oknem detalu, skala opisów 0,5 (`_annotScaleOverride` =
    25 / 50, ramię opisu 2400 · 0,5 — opisy 2,5 mm na papierze), przywracana po każdej belce. Szablony prętów (nowe pozycje) — **kolumna obok
    detalu, 3000 na prawo od ramki** (jak detal otworu; decyzja 2026-10-12 — wcześniej strefa rebar_bottom rzutu; `BeamDrawTarget.TemplateZone`),
    skala 1.0; identyczny pręt = istniejąca pozycja — pręt prosty tylko z siatki dolnej (strefa rebar_bottom) albo pręt belki (decyzja
    2026-10-12), gięty — jak dotąd; Mesh (warstwy dolne) przy szukaniu szablonu bierze też proste pręty belki (bez duplikatu tej samej długości). Tryb rzutu (target = null) — bit w bit jak przed K9. BBS liczy raz (pręty tylko w detalu). Do modelu 3D — później.
  - **Usuwanie:** ponowne Beam — rozkłady strefy (znacznik) z rzutu i z detalu + obiekty RC_BEAM_DETAIL strefy; ponowne Prepare GA
    (`BeamStore.EraseGeneratedForZones`) — także detale i UB 03 w detalu (`FindDetailSlabDists`). Jedna komenda = jedna grupa UNDO.
  - **Zbrojenie nibu w detalu (decyzja użytkownika 2026-10-12):** krawędź wzdłuż belki z nibem na całej długości strzemion — UB 03 =
    liczba strzemion (BL strefy w detalu, Count) + 2, od otuliny do otuliny krawędzi (np. 55 → 57); koniec belki przy krawędzi, gdy na
    krawędzi leży szerokość belki + nib (bok strefy ≤ 260 od narożnika; tylko strefy prostokątne) — część krawędzi od narożnika do uskoku
    w nibie (lico wewnętrzne belki) → UB 03 co 150 w detalu (`BeamEndUbPortion`, opis „n H10-03-150 UB”), reszta krawędzi jak dotąd;
    w nibie takiego końca zamiast T+B IN NIB — 1 UB 03 prętem równoległym do krawędzi końca (zagięcie przy uskoku, oś 55 od krawędzi,
    „1 H10-03 UB”, generowany w przebiegu UB 03 drugiego kierunku). Nib końca dłuższy niż szerokość belki + nib (np. 2 m) — do zrobienia
    (decyzja: wtedy 2+1 T+B IN NIB co 150).
    UB 03 w detalu powstają też przy Beam bez siatki (`RefreshNibUbAtBeams` → `GenerateNibUBLayer(onlyBeamDetail)`: tylko krawędzie w detalu,
    UB 03 rzutu robi Mesh); uskok w nibie na końcu belki — z rzutu (linia z GA, `OtherPlanLines`), program go nie dorysowuje.
  - **UB 03 w detalu (decyzja użytkownika 2026-10-11 „UB w nib też pokazuj na detalu”):** UB 03 generuje
    nadal `GenerateNibUBLayer` (Mesh / UB NIB; liczba i położenia z siatki dolnej jak dotąd). Krawędź nibu, której linia uskoku leży w > 50 % przy strefie
    (`UbExclusionRects` strefy — kryterium jak T IN NIB), strefa w trybie detalu ([36] = 1) z ANCHOR-em przystającym do strefy (`NibUbZones` /
    `NibUbTargetFor`) → rozkład w detalu: położenie rzutu + Shift, linie odniesienia za oknem detalu, skala opisów 0,5, linia rozkładu 250 · 0,5 poza
    krawędzią, znacznik RC_AUTOREBAR_BEAM = strefa (obok znacznika płyty); na rzucie przy belce — bez UB 03. Liczba UB 03 jak na rzucie, także
    w nibie zawiniętym przy końcu belki (528: 53 + 4 + 4; reguła „1 zamiast 4” dla UB 03 — wprowadzona i COFNIĘTA 2026-10-11 korektą użytkownika:
    „1 a nie 4” dotyczy pionowych U-barów belki BU, sekcja K3 wyżej). Detal jeszcze nie istnieje — UB 03 na rzucie jak dotąd (komunikat); RC_GENERATE → Beam po belkach wywołuje `RefreshNibUbAtBeams`:
    gdy płyta ma nib i UB 03, a strefa jest w trybie detalu albo UB 03 leżą w detalu — `GenerateNibUBLayer` (pręt reprezentatywny) jeszcze raz
    (UB 03 przy belce → detal; po przełączeniu detal → rzut — z powrotem na rzut). Kolejność Mesh / Beam dowolna (ten sam wynik). Tytuł detalu
    obniżany pod UB 03 dodane po Beam (`UpdateBeamDetailTitle`). Rozkład UB 03 z 1 prętem „H10-03 UB” (opis bez rozstawu): numer z pierwszego członu opisu — tylko
    dla rozkładów ze znacznikiem strefy (`ScanOldUBDistributions`; reszta bit w bit). `ReadUb03` (Beam) liczy UB 03 z detalu w położeniu rzutu.
    Tryb rzutu ([36] = 0), krawędzie bez belki i płyty bez stref — bit w bit (S-REG bez zmian). BBS poz. 03 bez zmian (528: 61, 539: 88).
- **Pręty w nibie 2T+1B (decyzja użytkownika 2026-10-11 „rób jedną pozycję bottom in nib i top, wtedy mamy 3 pręty te same, opisz może to
  odpowiednio też”, potem „średnica H10”, „opis 2+1 … T+B IN NIB”, „pozycja na rzucie dolnym z nibem” — zastępuje K5 i T IN NIB przy belce):**
  kod BN, rzut dolny, seria dolna; JEDNA pozycja — 3 identyczne pręty Ø [21] (domyślnie **H10**): 2 górą pod górnym ramieniem UB 03 (oś w narożu
  spinki UB 03 — `NibBarLine`, krawędź − 40 − 10 − d/2 = 55; drugi
  **150 dalej w płytę, jak drugi pręt T IN NIB** — decyzja użytkownika 2026-10-12, `BeamGeometry.NibPairSpacing`; wcześniej przy licu zewnętrznym
  belki − 40) i 1 dołem w dolnym narożu UB 03 pod prętem przy krawędzi. W rzucie rozkład 2 prętów (górnych; pręt dołem pod prętem przy
  krawędzi), Count 2, **CountDisplay 3**, opis z rozstawem jak T IN NIB (2026-10-12) `2+1 H10-nn-150 T+B IN NIB` (sufiks `BeamGeometry.SuffixNibBoth` = „T+B IN NIB”; liczba „2+1” — `BarLabelRules.CountText`: BN z CountDisplay > Count
  → „Count+reszta”), BBS — 3 sztuki na rozkład. Opis przy nibie (`NibPairLeader`, poprawka 2026-10-11 — wcześniej linia odniesienia biegła przez
  całą płytę i opisy 539 wypadały poza okno layoutu): prosto na zewnątrz krawędzi płyty po stronie nibu (ramię jak T IN NIB, potem +500 … +2000,
  bo tuż za krawędzią leży linia rozkładu UB 03 i opisy belki), dopiero potem załamanie wzdłuż krawędzi. Zakres
  wzdłuż jak dawny BN (`NibBarLine`: koniec przy krawędzi / narożnik L — lico płyty − 45, wewnętrzny — lico belki − 45, T — własny zakres), podział
  `PlanBars` z zakładem 40·Ø (H10: 400), wszystkie 3 pręty dzielone tak samo (jeden rozkład na kawałek). Numer `FindOrCreateBeamTemplate`
  (identyczny pręt serii dolnej → ta sama pozycja, sekcja 3 — **także pręt siatki B1 / B2 H10 tej samej długości**: 528 — poz. 46 = B1 / B2 H10
  L5500, wiersz BBS = siatka + 3 × rozkłady w nibie; bez dopasowania podziału do siatki — `MeshRowNearBeam` usunięte). BN górą ([20], „BEAM NIB T”) — bez zmian (domyślnie 0). Kolejność Mesh / Beam
  dowolna (Mesh przy belce nie generuje T IN NIB, Beam nie czyta siatki). Ograniczenie: krawędź uskoku „przy belce” (Mesh) a odcinek belki bez
  nibu w `NibBarLine` (np. nib > 260) — brak prętów w nibie; odcinek „detal ręczny” — T IN NIB płyty jak bez belki (UbCuts bez odcinków ręcznych).
  Rysunki sprzed decyzji (T IN NIB w nibie, „1 H10-nn B IN NIB”) — bez migracji; ponowne Mesh (T1 / T2) i Beam je zastępują.
- **Nib przy belce — opcja okna RC_BEAM (decyzja użytkownika 2026-10-11 „w generatorze dodaj opcję dodania lub nie dodawania nibu, bo czasem
  może nie być”; rekord [38]):** pole „Nib przy belce” (tak / nie) w oknie RC_BEAM — opcja per belka (RC_GENERATE bez globalnego przełącznika;
  RC_GENERATE → Beam czyta rekord strefy). Domyślnie wg geometrii (`BeamGeometry.HasNibGeometry`: lico zewnętrzne odcinka 1 … 260 mm od obrysu —
  nib z GA / Krawedz → tak; strefa przy krawędzi płyty, np. RC-BEAM-MANUAL bez linii uskoku, albo belka w płycie → pole nieaktywne, bez nibu);
  zaznaczone = [38] 0, odznaczone = [38] 1. Belka bez nibu przy geometrii z nibem (decyzja użytkownika 2026-10-11 „zostaw szerokość 600,
  chyba że będzie inaczej na GA; jeśli nie ma nibu, to na GA też go nie będzie ani linii uskoku”): **belka przesunięta do krawędzi płyty,
  szerokość z GA** (`BeamGeometry.ZoneWithoutNib`: krawędzie osiowe strefy z normalną zewnętrzną trafiającą obrys w 1 … 260 mm przesunięte do
  obrysu; lico boczne przesuwa całą nogę — lico wewnętrzne nogi (krawędź równoległa, przeciwnie zwrócona, w odległości 150 … 1500) o to samo;
  lico końca przy nibie zawiniętym (krótsze niż szerokość, bez pary) — koniec belki do krawędzi; 528: x 0..600 od krawędzi, strzemię 51 290 × 510,
  belka dłuższa o 2 × 160; krawędź z różną odległością — bez zmian + komunikat). Strefa z GA bez nibu (przy krawędzi, bez linii uskoku) — jak
  dotąd, szerokość z geometrii. Jedna droga: `AutoRebarEngine.EffectiveZoneVerts` / `EffectiveZoneOf` w `GenerateOneBeam` (komunikat „bez nibu —
  belka przesunięta do krawędzi … szer. 600 (z GA 600) … ponów Mesh”), `BeamMeshRegion` (prostopadłe B / T do 40 od krawędzi płyty, równoległe
  od NOWEGO lica wewnętrznego + 40 + d / + s/2; obszar „przy belce” bez UB 01/02 = strefa przesunięta + strefa z GA) i `PrepareSection` (pas
  belki od krawędzi na szerokość z GA — profil bez nibu); nib płyty bez części przy takich belkach — `AutoRebarEngine.WithoutBeamNibs` (pas
  `UbExclusionRects` strefy z GA i przesuniętej; linia uskoku = suma z pasem w obrysie — `RectilinearPolygons.AddClipped`; krawędzie nibu bez
  tych odcinków — `BeamGeometry.NibSpansOutside`; `NibInfo.BeamNibsRemoved`) w `DetectNibSquared` (siatka T do krawędzi, UB 03, T IN NIB,
  reguła UB 01/02 na uskoku), `PrepareSection` i `PrepareBeamDetail` (detal bez linii uskoku przy belce). **ANCHOR detalu = strefa efektywna**
  (przesunięta, 600 przy krawędzi); dopasowanie ANCHOR ↔ strefa (Shift, Congruent) wszędzie względem strefy efektywnej (`EffectiveZoneOf` —
  obrys z [6]): `PrepareBeamDetail`, `BeamDetailShifts`, `NibUbZones`, `PrepareSection` (mapowanie detal → rzut); teksty „xxxTHK” detalu — ze
  strefy z GA. Skutek: brak prętów w nibie 2+1 (`NibBarLine` — lico na obrysie), brak UB 03 i T IN NIB przy belce (stare UB 03 usuwane), Auto
  cięcia strzemion wg normy dla szerokości z GA (belka nadal krawędziowa, bez wyjątku). `RefreshNibUbAtBeams` po Beam przelicza UB 03 także
  przy strefie bez nibu. Linia uskoku z GA na rzucie zostaje (dane GA). Belki z nibem i rekordy bez [38] — bit w bit (BRX kroki sprzed decyzji
  bez zmian). BRX L528NN (528: z nibem → [38] = 1: komunikat, 600 od krawędzi / strzemię 510, B2 / T2 od nowego lica + 50 / + 100, bez BN /
  UB 03 / T IN NIB / UB 01/02 przy belce, B1 / T1 40 od krawędzi, przekrój — trzon 600 od krawędzi bez nibu, ANCHOR 600 przy krawędzi, detal bez
  linii uskoku, BBS z / bez nibu, PDF); jednostkowe `BeamNoNibTests`.
  „Tak” przy strefie bez linii uskoku — niepotrzebne (decyzja 2026-10-11; pole nieaktywne, program nie dorysowuje nibu).
- **Okno RC_BEAM — listy (decyzja użytkownika 2026-10-11, punkt A):** średnice prętów głównych dołem / górą i pionowych U-barów — lista
  H12 H16 H20 H22 H25 H32 (domyślnie H16), strzemiona — H10 / H12 (domyślnie H12), cięcie strzemion — Auto / 2-cięte / 4-cięte (domyślnie Auto;
  podpowiedź z wynikiem Auto dla belki krawędziowej); liczby i rozstaw — pola jak dotąd. Wartość rekordu spoza listy (stary rysunek) — dopisana
  jako „H{n} (nietypowa)” i wybrana, bez zmiany po cichu (`BeamParams.DiameterItems`). Zakład Ceil25(40·Ø): H20 800, H22 900, H25 1000, H32 1300;
  promienie gięcia BS 8666:2020 Tabela 2 (H20 70, H25 87, H32 112; **H22 spoza tabeli normy — 3,5·d = 77**, P / q brak). Zmiana Ø i ponowne Beam
  przelicza pręty, U-bary (A = C z zakładem), opisy, przekrój i BBS (BRX LH20).
- **Strzemiona 4-cięte (decyzja użytkownika 2026-10-11, punkt B):** dwa IDENTYCZNE strzemiona 51 zachodzące na siebie — pierwsze obejmuje
  pręty 1..k, drugie (n − k + 1)..n warstwy (symetrycznie), **jedna pozycja** (szablon 51: A jak 2-cięte, B = oś pręta k − oś pręta 1 + d + 2·Ø
  strzemienia, w górę do 5 — `BeamGeometry.FourLegWidth`; 600, 4 H16, H12: 355; dół ≠ góra — większe B z obu warstw). Reguła `FourLegK`:
  k = round(1 + 2(n − 1)/3) (ramiona wewnętrzne blisko 1/3 i 2/3 szerokości), zachodzenie ≥ 1 pręt (n − k + 1 ≤ k), k < n — n = 3: 1–2 / 2–3,
  4: 1–3 / 2–4, 5: 1–4 / 2–5, 6: 1–4 / 3–6, 7: 1–5 / 3–7, 8: 1–6 / 3–8; n ≤ 2 — 4-cięte niemożliwe (2-cięte + komunikat). Rozkład BL jak dotąd
  (linia rozkładu na całą szerokość, `LengthA` = B strzemienia 2-ciętego, pręt reprezentatywny), Count = liczba rozstawów, **CountDisplay = 2 · Count**:
  opis „2x29 H12-nn-200 LINKS” (`BarLabelRules.CountText` — BL jak BM; bez dopisku 4-LEG — praktyka biura: Holbrook 528 „51 H12-10-200 LINKS” ×2,
  C12 PLOT 10 „24 H12-26-200 LINKS” ×2), BBS — 2 × liczba rozstawów, grip zachowuje krotność (`KeepsMultiplicity`). Przekrój: `PrepareSection`
  — BL z CountDisplay = 2 · Count → `Dist.Layers` = 2; planer — w cięciu prostopadłym drugie identyczne strzemię przy drugim licu (przesunięte,
  zachodzące; tylko gdy pręt wzorcowy węższy od rozkładu), bez własnego opisu (`BarLine.NoLabel`); cięcie wzdłuż belki — bez zmian (ramiona
  pokrywają się). 2-cięte (Layers 1) — wynik bit w bit (`SectionPlanner.Version` 4 bez podbicia; złote testy bez zmian).
- **Próg 4-ciętych (decyzja użytkownika 2026-10-11, punkt C + doprecyzowanie tego dnia):** EN 1992-1-1 §9.2.2(8) (UK NA — wartość zalecana):
  s_t,max = 0,75·d ≤ 600, d = H − otulina dolna − Ø strzemienia − Ø/2 pręta dołem (`LinkLegLimit`); rozstaw ramion 2-ciętych = B − Ø strzemienia,
  4-ciętych — największy odstęp osi 4 ramion (`LegSpacing`). **Auto tylko dla belek krawędziowych (odcinek z OuterSide ≠ 0):** rozstaw 2-ciętych
  > s_t,max → 4-cięte (rozstaw 4-ciętych też sprawdzany — `*** OSTRZEŻENIE ***`, gdy nadal > s_t,max, np. 539: 188 > 172,5); **belka w płycie
  (OuterSide = 0) — Auto zawsze 2-cięte** (komunikat informacyjny „belka w płycie — 2-cięte”, bez ostrzeżenia normowego); 300 / 375THK bez wyjątku.
  Ręczny wybór w oknie nadpisuje Auto: 2-cięte przy przekroczeniu (krawędziowa) — ostrzeżenie, nie blokada; 4-cięte — także w płycie. Komunikat
  w linii poleceń na odcinek (różne szerokości osobno), np. `[RC BEAM] belka …: 600×600: d = 530, s_t,max = 0,75·d = 397.5 (EN 1992-1-1 §9.2.2(8)),
  rozstaw ramion 2-ciętych 498 > 397.5 → 4-cięte (2 × strzemię 51 B = 355 obejmujące pręty 1–3 i 2–4, jedna pozycja; rozstaw ramion 188 ≤ 397.5).`
  Czyste `BeamGeometry.PlanLinkLegs` (testy `BeamLinkLegsTests`). **Skutek:** belki krawędziowe szer. 600 z H < ~735 (Holbrook 319 / 528 / 539,
  C12 PLOT 17) — Auto 4-cięte (praktyka biura 319 / C12 PLOT 17: 2-cięte; Holbrook 528 / 539: 2 zachodzące strzemiona 370).
- **Warstwa RC-BEAM-MANUAL (decyzja użytkownika 2026-10-11, punkt D):** tworzona przez RC_BEAM (start komendy) i RC_PRZYGOTUJ_GA (kolor 30,
  opis w Layer description). Zamknięta polilinia użytkownika (≥ 4 wierzchołki) na tej warstwie, z punktem wewnętrznym w obrysie płyty
  (SD-PILED-RAFT) = strefa belki: `AutoRebarEngine.ConvertManualBeams` / `ConvertManualBeam` — strefa RC-BEAM + RC_BEAM (nowa polilinia, [6] =
  obrys) na rzucie polilinii i (przesunięta) na drugim rzucie (`FindPartnerView`); H z opisu „…THK” w strefie (rzut polilinii, potem drugi),
  inaczej H = 0 + komunikat („ustaw w RC_BEAM”; bez H pręty nie powstają). Polilinia ręczna zostaje (bez RC_GA — ponowne Prepare GA jej nie
  kasuje) z XData RC_BEAM_MANUAL (sekcja 1). Bez duplikatów: strefy ze znacznika żywe i przystające — tylko przepięcie [6] na bieżące obrysy
  (ponowne Prepare GA; strefy z polilinii ręcznej NIE mają RC_GA — parametry z okna RC_BEAM i ich pręty zostają); strefa nieprzystająca
  (polilinia zmieniona) — zastąpiona (parametry przeniesione, jej rozkłady i detal usunięte, komunikat); przystająca strefa bez znacznika —
  przejęta; polilinia nachodząca na inną strefę belki — pominięta z komunikatem. Wołane: RC_PRZYGOTUJ_GA (po wstawieniu obu rzutów, polilinie
  w nowych obrysach; `Result.ManualBeams`), RC_GENERATE → Beam (przed zbieraniem stref; RC_BEAM jednej strefy — bez), RC_BEAM (wskazana
  polilinia ręczna → strefa, okno dla niej). NibDetector, `FindHoles` i teksty „THK” w `PrepareSection` traktują RC-BEAM-MANUAL jak RC-BEAM
  (nie linia uskoku, nie otwór, nie grubość płyty); ZonesOfSlab / PrepareSection widzą tylko strefy z RC_BEAM. Zrzut Prepare GA płyty bez
  belek różni się tylko wierszem warstwy RC-BEAM-MANUAL (BRX D526).
- **K4 — pręty boczne i UB SIDE:** BS H12 (tryb [25] / [26]; B3 — od 525: 525 → 1, 600 → 2, 675 → 2, 750 → 3 na bok) proste do UB SIDE (lico ∓ (45 + 12)),
  opis „2x{n} H12-nn SIDE” (CountDisplay = 2n); UB SIDE — kod **BC**, kształt 21 poziomo, 1 na poziom na koniec / narożnik L / złącze T
  (`BeamGeometry.PlanSideUBars`): tył na otulinie od lica (narożnik — lica zewnętrznego), B = Floor5(B strzemienia − 2·Ø strzemienia) = 485,
  A = C = Ceil25(tył → początek prętów bocznych + zakład 500) (koniec 525), przycięte do zakresu prętów bocznych; w rzucie ramię przy jednym boku
  z hakiem na zagięciu w stronę drugiego ramienia; opis „{n}x1 H12-nn UB SIDE”.
  **Decyzja 2026-10-14:** liczba prętów bocznych na bok (= poziomów UB SIDE) w trybie B3 z wysokości belki — H ≤ 375 brak, 450–525: 1,
  600–675: 2, 750–825: 3, 900–975: 4 … (`BeamGeometry.SideLevelsForHeight` = ⌊(H − 300) / 150⌋; próg [26] nieużywany); UB SIDE jako
  rozkład **Any** (cały U wypełniony w rzucie — `BuildAny`, SymbolSide „Left” = tył U przy początku odcinka, SymbolDirection = strona
  drugiego ramienia), opis „{n} H12-nn-{rozstaw pionowy} UB SIDE” (rozstaw = `SideLevelSpacing`, osie dół / góra / (n + 1), w dół do 5).
  **Decyzja 2026-10-14 (2):** przekrój — opis pręta bocznego BS obok każdej kropki w obrysie belki (lewy bok na prawo, prawy na lewo,
  `Dot.Side`); strzemiona 4-cięte: drugie strzemię przy drugim licu PASA belki (rozkład w rzucie ma szerokość B4) i z własnym opisem
  ze strzałką; rzut — w bloku rozkładu drugi pręt o jeden rozstaw dalej od widocznego, przesunięty wzdłuż o `BarData.PairShift`
  (XData [32] = B 2-ciętego − B4, od drugiego lica), z kółkami jak widoczny (`AddPairBar`; MIRROR wzdłuż — znak odwrócony).
  **Decyzja 2026-10-14 (3):** rozstaw w opisie UB SIDE na stałe 150 (`UbSideLabelSpacing`); opis UB SIDE blisko pręta — krótka
  linia odniesienia prostopadle do ramienia (600…1600 × skala opisu, najpierw na zewnątrz U). Any: wymiary A–E przy przebudowie bloku
  z pręta wzorcowego (XData rozkładu ma tylko A — po RC_SCALE_ANNOT zostawało jedno ramię). Opis rozkładu: XData RC_BAR_ANNOT [23] =
  AnnotScale (przesunięcie opisu po RC_SCALE_ANNOT wracało do 1:50); opisy bez [23] — skala z rozkładu-źródła.
  Pręty belki (gdy już są) nie są przeszkodą w `ResolveRepresentativeCollisions` na rzucie; opisy siatki je omijają (zasada „opisy nie nachodzą”).
  **Detal 1:25 (decyzja 2026-10-12 „wygaszajmy tak, żeby pręty na siebie nie nachodziły, jak na Generate Mesh”):** `FinishBeamDetail` woła
  `ResolveRepresentativeCollisions(ramka detalu, 250 · 0,5, beamDetail: true)` — pręty belki (BM, BU końca, BN, BS…) i UB 03 w ramce: widoczny
  pręt przesuwany na inny indeks, gdy leży bliżej niż 125 od widocznego pręta innego rozkładu; pręty w nibie 2+1 wygaszane do 1 pręta
  reprezentatywnego — widoczny pręt w narożu UB 03 przy krawędzi (nie ten 150 dalej, przy prętach głównych belki).
- Strefa: `RC-BEAM` + `RC_BEAM` (sekcja 1). Prepare GA (sekcja 4d) albo `RC_BEAM` (polilinia → strefa; [Krawedz] — obrys + 2 punkty
  → prostokąt od linii uskoku na szerokość 600). Strefa na drugim rzucie (przystający obrys) — znaleziona albo utworzona (kopia).
  `RC_BEAM` odmawia (`[RC BEAM]`) dla obrysu płyty / uskoku (SD-PILED-RAFT), pręta (polilinia z `RC_SINGLE_BAR`) i otworu (krzyżyk
  przekątnych jak `FindHoles`). `RC_GENERATE_BEAM`: wskazany jako dolny obrys rzutu górnego (`IsTopView`) → komunikat i przerwanie
  (także w trybie bez okien).
- Geometria (`BeamGeometry.Analyze`): pary równoległych lic osiowych 150–1500 mm → odcinki; koniec **Edge** (lico końca ≤ 260 mm od
  obrysu — nib), **Internal**, **Corner** (L — kwadrat między końcami dwóch prostopadłych odcinków; lico zewnętrzne narożnika),
  **Tee**; odcinek ze skosem / zmienną szerokością (PLOT 319, zatoka 45°) → „detal ręczny”, bez prętów. Odcinek, którego koniec
  bez rozpoznanego złącza (Internal / Edge) przylega (wspólny koniec / naroże) do odcinka „detal ręczny”, też jest „detal ręczny”
  (bez prętów i strzemion, komunikat „przylega do odcinka „detal ręczny”…”) — np. L z krótką nogą 600 × 1200: obie nogi bez prętów;
  narożnik L / T rozpoznany z odcinkiem ręcznym (PLOT 319) — bez zmian. Przerwa w licu odcinka (te same lica po obu stronach) wypełniona
  strefą na całej szerokości (złącze T — w tym miejscu dochodzi belka poprzeczna) = belka ciągła przez węzeł, nie
  „detal ręczny” (E4; wcześniej belka ciągła złącza T była „detalem ręcznym”). Strefa po OBU stronach przerwy (skrzyżowanie „+”) —
  oba odcinki „detal ręczny” z komunikatem „skrzyżowanie belek („+”)…” (bez prętów i strzemion — bez dublowania strzemion w węźle). Złącza: `Result.Joints` (narożnik L: A — odcinek wzdłuż X,
  B — wzdłuż Y; T: A — belka ciągła, B — dochodząca).
- **Generowanie (E3 + E4) wyłącznie na żądanie:** `RC_GENERATE → Beam` / `RC_GENERATE_BEAM` / `RC_BEAM` (opcja okna). Na odcinek osiowy:
  - strzemiona **51** (BL, rzut dolny, seria < 100): A = H − 35 − dT1 − dT2 − otulina dolna 50 (pod obiema warstwami T, dT = 12; opcja: jedną;
    450 → 340, 375 → 265, 300 → 190), B = szer. − 2 · otulina boczna 45 (600 → 510), oba w dół do 5 mm; C = 0 (hak domyślny); w zakresie strzemion odcinka (E4: własny, w narożniku L dłuższy
    odcinek także w kwadracie narożnika — zob. E4 niżej):
    n = ceil((L − 2·50)/200) + 1, rozstaw ≤ 200, opis nominalny `nn H10-07-200 LINKS`; gdy wzdłuż odcinka są UB 03 (rozstaw ≤ 200) —
    położenia UB 03 + 100 (w połowie między U-barami), rozstaw jak UB 03; symbol strzemienia (kółka na obu końcach); przy końcu
    z U-barami (E4) pierwsze strzemię za tyłami U-barów z prześwitem 20 (`StirrupEndOffsetAtUBars`: 4 H16 — oś 125 od lica zamiast 50
    (otulina czołowa 45; przy 40 było 120); 528: 52 → 51, 539 dolna noga 24 → 23) — tyły U-barów nie leżą w płaszczyźnie strzemienia (do potwierdzenia);
  - pręty podłużne **4 H16 dołem** (BB, rzut dolny, seria < 100) i **4 H16 górą** (BT, rzut górny — strefa partnerska, 101+): zakres
    `BeamGeometry.StraightRange` — koniec z U-barami (krawędź, wewnętrzny) i złącze z odcinkiem „detal ręczny” — lico końca z otuliną czołową 45
    (jak E3: narożnik z odcinkiem ręcznym — lico zewnętrzne narożnika, T — dalsze lico belki ciągłej), narożnik L z prętami L — lico
    kwadratu narożnika (bez otuliny), złącze T — bliższe lico belki ciągłej (bez otuliny); w poprzek otulina + strzemię + d/2 (63 … 537,
    co 158); plan `BeamGeometry.PlanBars`: ≤ 6000 — jeden pręt w dół do siatki 250, **wyśrodkowany** (równa otulina na obu końcach);
    dłużej — **zakład w przedziale [lap, lap + 250)** (domyślnie 800 … < 1050; parametr, do potwierdzenia przez Bartka): najpierw pręty
    równe (jedna pozycja) na siatce 250, ≤ 6000, z N (najmniejsza liczba) — równe zakłady w przedziale; gdy się nie da — N prętów:
    N − 1 równych (zakład = lap) + ostatni krótszy na siatce (osobna pozycja, zakład w przedziale); dopiero potem N + 1 prętów równych
    (**mniej zakładów zdecydowanie lepiej**), np. 6251 → 3750 + 3500 (zakład 999, nie 3 × 2750), 6800 → 4000 + 3750 (950),
    17777 → 3 × 5250 + 4500 (800 / 800 / 873, nie 5 × 4250); zakłady dół / góra w tym samym miejscu (do decyzji); opis `4 H16-05 BEAM B` (bez rozstawu);
  - rozkłady przez wspólną ścieżkę siatki (`GenerateDistributionWithLeaderAtOffset`: opis bez kolizji, pręt reprezentatywny), bez znacznika
    płyty, ze znacznikiem `RC_AUTOREBAR_BEAM`; szablony w strefach rebar_bottom / rebar_top pod najniższym prętem strefy.
  - **E4 — zamknięcie końców** (`BeamGeometry.AssignClosures` / `PlanLayer`; domyślne 1b.6–1b.8, **do potwierdzenia przez Bartka**),
    osobno dla dołu (BB, rzut dolny, seria < 100) i góry (BT, rzut górny, 101+), numery `NextAuto` / `NextAutoTop`, identyczny pręt →
    istniejąca pozycja (`FindOrCreateBeamTemplate` porównuje kształt, Ø, A, B, C i serię):
    - **narożnik L** dwóch odcinków osiowych — pręty L **kształt 11** we WSZYSTKICH rzędach warstwy (od zewnętrznego do wewnętrznego;
      interpretacja „rząd zewnętrzny i wewnętrzny” — do potwierdzenia), identyczne (jedna pozycja na narożnik i warstwę), przesunięte
      po przekątnej o rozstaw rzędów (bez krzyżowania); ramiona (zewnętrzne, w górę do 25 mm) z zakładem ≥ zakład (800) z pierwszym
      prętem prostym każdej nogi (rząd zewnętrzny — zakład najkrótszy), np. 539: 1450 × 1350 i 1375 × 1350; opis `4 H16-08 BEAM CORNER`;
      strzemiona w kwadracie narożnika — kontynuacja **dłuższego** odcinka (równe — odcinek wzdłuż X) do lica zewnętrznego (`LinkLo/LinkHi`),
      drugi odcinek — zakres własny (bez zdublowania); narożnik z odcinkiem „detal ręczny” (319 lewy) — bez prętów L, jak E3 + komunikat;
    - **złącze T** — pręty proste belki dochodzącej do bliższego lica belki ciągłej + pręty L kształt 11: ramię wzdłuż belki dochodzącej
      (zakład ≥ 800) przez belkę ciągłą do jej rzędów (od dalszego lica), ramię wzdłuż belki ciągłej = zakład (800); rzędy jednej połowy
      zagięte w jedną stronę, drugiej — w drugą (zagnieżdżone: rząd skrajny najpłycej), jedna pozycja, dwa rozkłady;
    - **koniec przy krawędzi i koniec wewnętrzny** (tak samo) — U-bary **kształt 21** poziome w rzucie, zagnieżdżone: para rzędów skrajnych,
      para następnych … (4 pręty → **2 U-bary na warstwę i koniec**; nieparzysta liczba — pręt środkowy bez U-bara + komunikat); tył U
      na otulinie czołowej 45 od lica końca, kolejny (wewnętrzny) U-bar o Ø U-bara + 20 głębiej (prześwit między tyłami ≥ 20, `UBarBackOffset`),
      A = C z zakładem ≥ 800 z prętami prostymi (w górę do 25 mm; 528: 800 / 775, 539: 825 / 800 przy krawędzi, 900 / 850 wewnętrzny),
      B = rozstaw osi rzędów + Ø U-bara (w dół do 5 mm: 490 / 170); różne B = osobne pozycje; opis `1 H16-09 BEAM END`;
      319 (prawa noga, otulina 45): pręt prosty 2000 (było 2250 — dostępne 2247,5 < 2250), ramiona U-barów / pręta L dłuższe
      i nachodzą na siebie o 93 mm — komunikat „krótki odcinek … sprawdź / detal ręczny”;
      jeden pręt w warstwie — koniec bez U-bara + komunikat;
    - **ułożenie zakładów (domyślne, do potwierdzenia przez Bartka):** ramiona prętów L / U-barów leżą „piętrowo” względem prętów
      prostych, z którymi mają zakład — w tym samym rzędzie w rzucie, w styku w pionie (dół: ramię nad prętem prostym, góra: ramię pod
      prętem prostym); geometria w rzucie bez przesunięcia w poprzek;
    - w rzucie (rozkład RC_BAR_BLOCK, bez nowych pól): **widoczne jedno ramię** (pręty L — ramię wzdłuż odcinka X / belki dochodzącej,
      U-bar — ramię przy rzędzie o mniejszej współrzędnej) z symbolem **haka** (SymbolType `Hook`, [13]/[14] strona i kierunek — w stronę
      drugiego ramienia) na końcu przy zagięciu; pręty L — skos rozkładu ([25]/[26]: przesunięcie po przekątnej); wszystkie pręty widoczne;
      sposób rysowania — do potwierdzenia (pełny kształt L / U w rzucie wymagałby zmian w `BarBlockEngine`). Ścieżka: opcjonalny
      `customizeBar` w `GenerateDistributionWithLeaderAtOffset` (domyślnie bez zmian dla siatki).
  - Komunikaty `[RC BEAM]`: „detal ręczny”, „zamknięcie końców — narożniki L: n, złącza T: n, końce z U-barami: n …”, narożnik / złącze
    z odcinkiem ręcznym, skrzyżowanie „+”, ramię przycięte (krótki odcinek), ramiona prętów giętych z obu końców nachodzą (krótki odcinek),
    koniec bez U-bara (jeden pręt / pręt środkowy), pierwsze strzemię odsunięte za U-bary, pręt dłuższy niż maks., brak H, brak strefy na rzucie górnym.
  - Strefy cięcia przy otworach (`ComputeCutZones`) uwzględniają skos rozkładu (rzeczywisty zasięg prętów L) — tylko ścieżka belki
    (`customizeBar`); siatka płyt bez zmian. Generowanie belki nie ma dziś listy otworów (`_currentHoles` pusta), więc cięć nie ma.
  - **E4b — pręty boczne i pręty w nibie belki** (wymaganie użytkownika 2026-10-10; domyślne, do potwierdzenia przez Bartka; parametry w oknie
    RC_BEAM i rekordzie [18]–[21]): **BS** — H10 na obu bokach przy ramionach strzemienia (oś: otulina + strzemię + d/2 od lica), rozstaw
    pionowy oś–oś ≤ 200, liczba na bok `BeamGeometry.SideBarCount` = ceil(odległość osi prętów dołu i góry / 200) − 1, min. 0 (H 600 → 2,
    750 → 3, 450 / 375 → 1, 300 → 0); zakres jak pręty proste (`StraightRange`: narożnik L / złącze T — lico kwadratu / własnego zakresu, koniec z U-barami — lico
    z otuliną czołową 45), bez zagięć; plan `PlanBars` z zakładem tej samej krotności Ø co pręty główne (`LapFor`: 800 / 16 → H10: 500), maks. 6000;
    jeden rozkład na pręt planu: w rzucie 2 pręty (po jednym na bok), opis / BBS — wszystkie (`CountDisplay` = 2 × liczba na bok, gdy > 1 na bok;
    zmiana liczby gripem — `RegenerateBarBlock` zachowuje krotność CountDisplay / Count tylko dla kodu BS),
    opis `2 H10-nn BEAM SIDE`; **BN** — po 1 pręcie wzdłuż nibu belki w narożach spinki UB 03 (Ø 10): górą H10 (`… BEAM NIB T`), dołem H16
    (`… BEAM NIB B`), oś w poprzek = krawędź płyty − 40 − 10 − d/2 (naroże spinki UB 03 płyty — otulina 40 bez zmian; `BeamGeometry.NibBarLine`;
    odcinek z licem przy krawędzi ≤ 260 mm), wzdłuż — koniec przy krawędzi i narożnik L (nib zawinięty) do lica płyty z otuliną czołową 45,
    koniec wewnętrzny — do lica belki z otuliną, złącze T — lico
    własnego zakresu; zakład jak BS (H16: 800, H10: 500). Oba: rzut dolny, seria dolna, znacznik `RC_AUTOREBAR_BEAM`, komunikaty `[RC BEAM]`
    („pręty boczne H10: n na bok …”, „pręty wzdłuż nibu belki … — domyślne, do potwierdzenia”, „brak UB 03 wzdłuż nibu belki …” — gdy odcinek
    nie ma UB 03, „UB 03 płyty w nibie belki ma otulinę dołem 45 < otulina dolna belki 50 …” — BN dołem przy UB 03, „pręt BN górą … może dublować T IN NIB wzdłuż tego nibu — do potwierdzenia” — gdy na rzucie górnym jest rozkład T IN NIB
    równoległy, ≤ 250 w poprzek od osi BN, zakresy nachodzą; „Nib: pręt górą = 0” w oknie RC_BEAM wyłącza BN górą). Interpretacja „brakuje 2 prętów H10 wzdłuż nibu
    górą oraz dołem w narożu nibu też dajmy H16” (1 + 1 pręt) — **do potwierdzenia**. Kopia samych prętów BN bez strefy (CloneRemap → "") —
    przynależność z testu punktu w strefie, a BN leżą w nibie (poza strefą) — ograniczenie jak dotąd.
  - **E5 — RC_SECTION z belką** (sekcja 4g): profil betonu, pręty belki, wymiary, odcisk BEAM; RC_SECTION_CHECK / RC_SECTION_UPDATE (SectionUpdater
    tą samą drogą) działają z belką.
  - **E6 — BBS z belką:** pozycje belki w RC_BBS / RC_SCHEDULE / RC_COUNT_BBS jak każdy pręt RC_SINGLE_BAR z rozkładami: BB / BL / BS / BN
    (< 100) — arkusz dolny, BT (101+) — górny; długość cięcia BS 8666:2020 z jednej funkcji (`BarLengthRules.CuttingLength`: 51 — 2A + 2B +
    max(16d, 160), 11 — A + B − 0,5r − d, 21 — A + B + C − r − 2d, w górę do 25); sztuki = suma `EffectiveCount` rozkładów (4 pręty bez
    rozstawu, strzemiona, BS — CountDisplay). Poprawka E6: szablony belki zapisują **TotalLength wg BS 8666:2020** (`FindOrCreateBeamTemplate`;
    wcześniej TL = 0 → odczyt A → fałszywe ostrzeżenie RC_BBS „długość wg BS 8666:2020 = 1600 mm, w rysunku 200” dla strzemion 51).
- Ponowne generowanie belki usuwa tylko jej rozkłady (znacznik = strefa; bez znacznika — punkt w strefie); jedna komenda = jedna grupa UNDO.
- Testy: jednostkowe `BeamGeometryTests` (E4: zamknięcia 319 / 528 / 539 / T, plan warstwy, wymiary prętów L / U-barów, brak krzyżowania,
  pręty w strefie, długości 11 / 21 wg BS 8666:2020, prześwit tyłów U-barów ≥ 20 i strzemię za nimi, skrzyżowanie „+”, ramiona nachodzące,
  jeden pręt — bez U-bara, zagięcia złącza T na rzędach belki ciągłej); BRX `tools/BrxSmokeTests/run-planc-beam.ps1` (najpierw `-Baseline -PluginDll <DLL HEAD>` —
  zrzuty bazowe D526 / D319 / D528 / D539, potem właściwy: Prepare GA 319 / 528-529 / 539, liczby, numery, UNDO / REDO, COPY / MIRROR / ARRAY / PASTECLIP,
  ponowne Prepare GA, UB 03, format Blok i Polilinia, M528 — belka, potem Mesh; E4: pręty L / U-bary, wymiary szablonów, skos, hak w stronę
  drugiego ramienia, linie w strefie; E4b / E5 / E6: S528 — RC_SECTION przez belkę 528 prostopadle (profil, kropki dół / góra / boczne / nib,
  strzemię w widoku, wymiary, opisy, BS / BN na rysunku), S528C — przekrój „sprzed E5” → „belka nieuwzględniona”, H 375 → 450 → „zmieniona
  belka”, RC_SECTION_UPDATE → aktualne, S539 — przekrój wzdłuż belki 539, S539BBS — RC_BBS / RC_SCHEDULE / RC_COUNT_BBS / .xls, S528PDF /
  S539PDF — kopie rysunku z layoutami do PDF, `-PdfDir`); jednostkowe `SectionBeamTests` (+ szósty złoty przypadek `beam` w
  `SectionPlannerGoldenTests`, odciski HEAD bez belek, długości 51 / 11 / 21, E4b). **Od K2 / K3:** asercje G* / S* przepisane (BM 2x4, BU,
  strzemiona H12 51 od lica, seria dolna, brak prętów L / BT na rzucie górnym, U-bary w strefie i kółka na zagięciu), nowy krok G528V1
  (rekord v1 → komunikat migracji, zapis v2); przebieg bazowy `-Baseline` z DLL **HEAD + K1** (K1 zmienia siatkę płyt z nibem zawiniętym —
  zrzuty D319 / D528 / D539); jednostkowe `BeamGeometryTests` K2_* / K3_* (A1 / A2, zakłady, tryby BS, migracja v1, `CountText`, U-bary
  Holbrook / C12, 528 / 539 / 319 / T / krótki odcinek). **K4–K8:** D319 / D528 / D539 — asercje E2 (zamiast równości z HEAD), M528 — porównanie
  z D528 tego przebiegu, S528 — pręty w nibie 2T+1B (BN H10 „2+1 … T+B IN NIB”, Count 2 / CountDisplay 3, 3 kropki: 2 górą + 1 dołem, opis poza płytą po stronie
  nibu, numer wspólny z B2 tylko przy pręcie identycznym; S539BBS — wiersz = 3 × rozkłady w nibie + inne rozkłady tej pozycji), S539 — jak e1644bd (BU pominięte, bez odgięć; K6 cofnięte 2026-10-11; siatka równoległa w trzonie belki S528 / C17 — od decyzji 2026-10-11 asercja: brak kropek, brak kropek T IN NIB przy belce, S528 — pręty siatki prostopadłe w widoku do 40 od lica zewnętrznego; D319 / D528 / D539 — prostopadłe do 40 od lica zewnętrznego, UB 03 co 100…250, bez T IN NIB przy belce), G528S (H 600: BS / UB SIDE), S319 / S319PDF,
  C17 / C17PDF / C3 (K8 na kopiach GA C12 — folder `RC_BEAM_C12`, domyślnie scratchpad `c12ga`); jednostkowe `RectilinearPolygonsTests`,
  `BeamK4K6Tests` (K4 UB SIDE, K8 PLOT 17 / 3, BC / BU pominięte w przekroju, pręty w nibie 2T+1B — 3 kropki), `SectionBeamTests`
  (`NibPairAcross`); złoty `beamK6` usunięty (K6 cofnięte). **UB 03 w detalu (2026-10-11):** BRX G528UB3 / G539UB3 (UB 03 przy belce tylko
  w detalu, liczby jak na rzucie, kolejność Mesh / Beam, tryb rzutu, przekrój A detal = rzut, BBS poz. 03 61 / 88); BU końca belki — 4 w rozkładzie,
  1 widoczny (BRX G528 / G539: `VisibleUBarLines`, kółko na każdym widocznym pręcie).
  **Moduł belki 2026-10-11 (A–D):** kroki BRX sprzed decyzji generują strzemiona 2-cięte ([37] = 2 w helperze `GenerateBeams`); nowe L600 (528
  jako 600 × 600 Auto → 4-cięte: komunikat, jedna pozycja BL 515 × 355, „2x52 …”, BBS 2 ×, przekrój 2 strzemiona / 1 opis, PDF rzut + przekrój
  i detal), L528A / L539A (Auto na Holbrook, ręczne 2-cięte — ostrzeżenie, PDF), LH20 (H16 → H20 i ponowne Beam), LMAN (RC-BEAM-MANUAL na 526 →
  strefy obu rzutów, belka w płycie — 2-cięte, bez duplikatów, ponowne Prepare GA); jednostkowe `BeamLinkLegsTests`.

## 4j. Gotowe arkusze (RC_LAYOUT, wstążka Schedule → „Layouts”) — decyzja użytkownika 2026-10-19

Wzór: arkusze PLOT 30 / 20 / 206 / 9-10 (pliki Bartka). Silnik `LayoutEngine` (BricsCAD), czyste reguły `LayoutRules` (testy `LayoutRulesTests`).
- **Zawartość** (przestrzeń modelu): rzut dolny / górny 1:50 = obiekty RC_GA danej płyty i rzutu (obrys itd.) + tytuł „REINFORCEMENT DETAILS … LAYER” +
  obiekty w całości w obrysie powiększonym o `PlanMargin` 3500 (granica w połowie odległości do drugiego rzutu); przekroje 1:20 = tytuł RC_SECTION + części
  (bez MARK / MARKTXT / CLOUD); detale PH 1:25 — tylko UŻYTE (tytuł bez „APPLICABLE FOR PILES N/A”), obiekty przypisane do najbliższego tytułu PH (strefa
  tytułu −700…+1500 w X (od max(prawa krawędź tytułu, lewa + 3800)), −300…+4200 w Y; nieużyte detale też są kotwicami, przekreślenia AP-NOTUSED pominięte); detale belek (RC_BEAM_DETAIL bez PLANREF)
  i otworów (ramka RC_DETAIL + tytuł „DETAIL 'n'”/„SCALE”) 1:25 + obiekty w całości w ramce powiększonej o `DetailMargin` 2500 (strefa szablonów detalu
  otworu zaczyna się 3000 od ramki — poza zasięgiem). Kolejność przypisań: przekroje → PH → detale → rzuty (obiekt należy do jednego elementu).
- **Arkusz**: pole rysunku A1 x 11…672, y 11…582 (na prawo kolumna notatek i tabelka); margines rzutni 2 mm, odstęp 6 mm. Rzut dolny — lewy górny róg,
  górny — prawy górny róg (jeden rzut na arkuszu — lewy górny róg); przekroje i detale od góry/lewej (MaxRects), PH od dołu/lewej.
- **Podział** (gdy wszystko nie mieści się na jednym): 1) rzut dolny + przekroje (najpierw z nibem) + detale, które się zmieszczą; 2) reszta przekrojów
  i detali (kolejne arkusze); 3) rzut górny + PH; 4) nadmiar PH — osobne arkusze. Numery rysunków po kolei (ostatnia grupa cyfr +1), nazwa layoutu =
  część po ostatnim „-” + REV (RC0091C1). TITLE_3 wg zawartości („BOTTOM LAYER, TOP LAYER, DETAILS & SECTIONS.”, „TOP LAYER & PH DETAILS.”, same PH —
  „PH DETAILS.”; same detale PH bez detali belek / otworów — zawsze „PH DETAILS”, np. „BOTTOM LAYER, TOP LAYER & PH DETAILS.” — decyzja Bartka 2026-10-19), SCALE („1:50, 1:25 & 1:20 @ A1”). Notatki ogólne / płyty / pali / zbrojenia — bez zmian (z Prepare GA).
- **Arkusz bazowy** = layout z A1-BL bez znacznika RC_LAYOUT (bieżący, jeśli taki; inaczej pierwszy). Kolejne arkusze: `RcTemplateEngine.CreateSheetFrom`
  (ustawienia wydruku z bazowego, DeepClone treści papieru bez rzutni i obiektów RC_LAYOUT); ich A1-BL dostaje XData RC_LAYOUT [1]="SHEET".
- **Notatka pod przekrojem z nibem** (MText SD-Text, ROMANS NARROW 4, szer. 115,7, TopLeft pod rzutnią). Nib: składniki rekordu (SEG… N0/N1 > 0
  albo DIST… Kind:NibUB) lub część NIBNOTE. Decyzja Bartka 2026-10-19: notatka „A-A” („fixed to the bottom of the U-bars, and on top of the U-bars”)
  — przekrój w kierunku 1, notatka „B-B” („fixed to the top of the U-bars, and to the underside”) — w kierunku 2. Kierunek przekroju
  (`LayoutRules.SectionDir`): Lay prętów głównych leżących w płaszczyźnie cięcia (AX = kierunek cięcia); same kropki — kierunek przeciwny;
  brak prętów — cięcie wzdłuż X = 1.
- **Zakresy MText / MLeader** TYLKO z fragmentów po rozbiciu (GeometricExtents MText w BricsCAD bywa za mały — ucięte tytuły i opisy detali PH —
  albo za duży — zadana wysokość ramki, rzutnia PH1 „schodziła” w dół); GeometricExtents tylko, gdy rozbicie nic nie da.
- **Skala opisowa rzutni** = skala rzutni (CustomScale + AnnotationScale 1:n tylko z listy skal rysunku — od 2026-10-20 NIE dodajemy nowych skal
  (podejrzenie uszkodzenia rysunku: błędy rysowania kreskowań, „Failed to save”); ANNOALLVISIBLE nie ustawiane (eInvalidInput)
  (sama ViewHeight zostawiała 1:1 — obiekty opisowe rzutu górnego niewidoczne).
- Rzutnia: SetDatabaseDefaults, widok z góry, styl wizualny 2D Wireframe (kreskowania nie rysowały się); warstwy map zbrojenia (RC_MAP)
  zamrożone w rzutniach (kreskowania map ANSI31 dawały „error while rendering entity”).
- **Rzutnie**: wszystkie warstwy odmrożone w każdej rzutni RC_LAYOUT (warstwy „VP Freeze w nowych rzutniach” chowały pale, tagi PH, opisy,
  okręgi detali PH). Nowy arkusz: rzutnia całego modelu z „Create viewport in new layouts” usuwana (zostaje tylko rzutnia arkusza).
- **Odsyłacze** przy kilku arkuszach (decyzja Bartka 2026-10-19): każdy inny arkusz w OSOBNEJ ramce (warstwa 0, x 721,1…810,3), ramki jedna
  nad drugą od y 252,8 (nad CONSTRUCTION ISSUE), pierwszy arkusz na górze. Tekst MText SD-Text ROMANS NARROW 4: „<TITLE_3> SEE” łamane po słowach
  do 28 znaków + „DRG. <numer>” w ostatniej linii (`LayoutRules.CrossRefBlock`), wysokość ramki 10,03 + 6,67·(linie − 1) (2 linie = 16,7).
- **Przekrój z linią cięcia na detalu** (znacznik MARK w zakresie detalu belki / otworu) — `Item.Group` = klucz detalu: przy podziale na arkusze
  detal i jego przekroje są jedną jednostką (na tym samym arkuszu; SCALE np. „1:25 & 1:20”).
- **Warstwy map** (RC_MAP) zamrażane w rzutniach tylko wtedy, gdy są na nich WYŁĄCZNIE obiekty map.
- **Fatal error po przejściu na layout (2026-10-20, rysunek kolegi PLOTS 376-381):** BricsCAD przy tworzeniu layoutu („Create viewport in new
  layouts”) dokłada rzutnię całego modelu z widokiem skopiowanym z modelu — po RC_MODEL_3D / SHADEMODE styl „Building modeling” (render 6,
  widok 3D). Rysowanie modelu w takiej rzutni: „error while rendering entity” przy kreskowaniach i zawieszenie BricsCAD. Poprawki:
  `RcTemplateEngine.EraseAutoViewports` (Prepare GA i RC_LAYOUT — zostaje tylko rzutnia arkusza), `PlotSetupEngine.Apply` (RC_USTAW_WYDRUK, koniec
  Prepare GA / RC_LAYOUT) przełącza każdą rzutnię (poza rzutnią arkusza) na 2D Wireframe.
- **XData RC_LAYOUT** [1] rola (VP / NOTE / XREF / SHEET), [2] klucz. Ponowne RC_LAYOUT: usuwa layouty z A1-BL „SHEET”, obiekty RC_LAYOUT arkusza
  bazowego, rzutnie ręczne — tylko po potwierdzeniu; layout o nazwie kolejnego arkusza zrobiony ręcznie — zastępowany tylko po potwierdzeniu.
- Na końcu `PlotSetupEngine.Apply` (rzutnie na PCN-VIEWPORT, zablokowane, tagi PH odmrożone). Bez notatki „Quantity of members … typical detail”
  (liczba elementów detalu nie jest zapisana) — v2.

### RC_LAYOUT_TEXTS (wstążka Schedule → „Layout Texts”) — opisy istniejących arkuszy (decyzja Bartka 2026-10-19)
Arkusze = layouty z A1-BL wg zakładek (ręczne albo z RC_LAYOUT). Zawartość tak jak RC_LAYOUT (`LayoutEngine.Collect`); w każdej rzutni (poza
rzutnią arkusza) element „pokazany”, gdy środek jego zakresu leży w oknie rzutni (ViewTarget + ViewCenter, ViewHeight, bez obrotu); skala rzutni
= ViewHeight / Height (zaokrąglona). Zapis: TITLE_3 i SCALE wg pokazanych elementów i skal rzutni, DRAWING_NUMBER po kolei od numeru pierwszego
arkusza, nazwa layoutu = numer + REV (za zgodą; zmiana w dwóch krokach przez „~rc”), notatki pod przekrojami z nibem (pod dolną krawędzią rzutni,
od lewej krawędzi przekroju; pominięte, gdy layout ma już tekst „Section X-X has longitudinal”), odsyłacze w ramkach. Poprzednie odsyłacze /
notatki RC_LAYOUT (XData RC_LAYOUT NOTE / XREF) usuwane. Rzutni nie zmienia.

### Poprawki 2026-10-27 — RC_LAYOUT_TEXTS wg arkuszy ręcznych PLOT 357-362 (SO302HH001-DR-RC3570-3575, wzór Bartka)
- **Rzut = największe skupisko obiektów RC_GA** (`ClusterBoxes`, odstęp 10 m) — obrysy skopiowane na plan sytuacyjny 1:250 mają znacznik
  rzutu i rozciągały rzut na pół modelu (środek poza rzutniami, arkusze nierozpoznane, stary TITLE_3 zostawał). Pozostałe skupiska — ostrzeżenie.
- **Element pokazany w rzutni**: środek w oknie ALBO część wspólna ≥ 25 % elementu ALBO ≥ 25 % okna (rzut na dwóch arkuszach).
- **SCALE — skale wszystkich rzutni arkusza** (także 1:250 planu sytuacyjnego): „1:250 & 1:50 @ A1”, „1:250, 1:25 & 1:20 @ A1”.
- **Rzut na kilku arkuszach**: „BOTTOM LAYER 1/2.”, „2/2.” (kolejność wg lewej krawędzi widocznej części); same detale PH bez rzutu górnego —
  „TOP LAYER PH DETAILS.” (zmiana decyzji 2026-10-19 wg wzoru). Odsyłacze (`LayoutRules.CrossRefsFor`): na innych arkuszach jeden z zakresem
  („BOTTOM LAYER SEE DRG. …RC3570-3571”), na arkuszu tej samej grupy pozostałe części osobno („BOTTOM LAYER 2/2 SEE DRG. …RC3571”).

### Poprawki 2026-10-20 (decyzje Bartka)
- **Notatka „Section X-X has longitudinal lacer bars…”** (RC_LAYOUT / RC_LAYOUT_TEXTS) — nie przy przekrojach przez belkę (`LayoutRules.IsBeamSection`:
  składnik BEAM:… albo rozkład Kind:Beam).
- **Opis „NOMINAL TOP COVER TO NIB…” (NIBNOTE) w przekroju przez belkę** — tylko gdy żaden inny przekrój w rysunku go nie ma
  (`AutoRebarEngine.OtherSectionHasNibNote`); przekroje nibu bez belki — jak dotąd zawsze.
- **Style z szablonu** (`RcTemplateEngine.EnsureStyles`): Prepare GA kopiuje z rc-template wszystkie brakujące style tekstu, style wymiarowe i typy
  linii (istniejące bez zmian); przed wymiarami ADD i przed rysowaniem przekroju — to samo, gdy brakuje któregoś stylu wtyczki
  (SPEEDECK-1-50 RC, CADS_DIM_20, ROMANS, ROMANS NARROW, PH-LABEL; raz na rysunek w sesji). U kolegi wymiary ADD były niewidoczne (styl Standard).
- **Wstążka**: Layouts / Layout Texts — małe przyciski w jednej kolumnie; „Bar Format” zdjęty ze wstążki (RC_BAR_FORMAT zostaje jako komenda).
- **Rzutnia arkusza (2026-10-20, „Failed to save the file” + błędy rysowania kreskowań, też w bloku A1-BL):** rozpoznawana WYŁĄCZNIE po
  geometrii (`PlotSetupEngine.OverallViewportId`: wysokość widoku = wysokość rzutni i środek widoku = środek rzutni; dokładnie jedna taka),
  nie po uchwycie ani Layout.GetViewports — usunięcie rzutni arkusza (EraseAutoViewports przy nowym layoucie) psuło layout. Bez pewności:
  nic nie usuwamy (Prepare GA, RC_LAYOUT), PlotSetupEngine pomija layout, RC_LAYOUT usuwa tylko własne rzutnie (XData RC_LAYOUT).
- **Kreskowania z baz DXF (2026-10-20, „Failed to save the file” po imporcie map, logo A1-BL bez wypełnień):** `HatchRepair` — ponowne
  ustawienie wzoru (standardowe: kąt, skala, SetHatchPattern) i EvaluateHatch w rysunku docelowym. Wołane: po imporcie map (klony kreskowań),
  na końcu Prepare GA (wszystkie kreskowania rysunku), ręcznie RC_HATCH_FIX / RC_NAPRAW_KRESKOWANIA (naprawa starszych rysunków).
  Prepare GA usuwa też layouty z A1-BL poprzedniej płyty (decyzja Bartka).
- **Format prętów (decyzja Bartka 2026-10-20): zawsze polilinia, jak dawniej.** `BarFormat.UserSettingEnabled = false` (plik barformat.txt
  ignorowany), `RC_BAR_FORMAT` tylko usuwa stary plik i potwierdza polilinię. Pręt-blok (Plan C) — wyłącznie w testach (Set / RC_REG_BARFORMAT).
- **Wymiary ADD (2026-10-20):** `SetDatabaseDefaults` w BricsCAD wpisuje do wymiaru nadpisania z bieżącego stylu rysunku (ISO-25: DIMSCALE 1, tekst / strzałki 2,5 mm — wymiary niewidoczne). `AddDimensions` ustawia jawnie wielkości ze stylu „SPEEDECK-1-50 RC” (Dimscale, Dimtxt, Dimasz, Dimexo, Dimexe, Dimgap, Dimdli, Dimcen, styl tekstu, Dimtad, Dimdec). Stare wymiary — ponowne Generate → Add.
- **Pojedyncze UB 03 (2026-10-20):** opis „H10-03 UB” (1 pręt, bez rozstawu) — numer pozycji przez `UbPosNr` (pierwszy człon opisu). Wcześniej tylko dla UB 03 w detalu: pojedyncze UB 03 rzutu (uskok po stronie płyty, 2026-10-13) nie były usuwane przy ponownym Mesh / UB (dublowały się) i ADD brało je za krawędź UB 01/02.
- **Znaczniki przekroju na detalu belki (decyzja Bartka 2026-10-20):** w tym samym miejscu co na rzucie — koniec linii cięcia przesunięty do detalu, także poza ramką detalu (`BeamDetailRules.DetailMarkEnd`: koniec ≤ 1500 za ramką — jak na rzucie; dalej — 300 za ramką). Wcześniej znaczniki na ramce (w środku czerwonej obwódki).

## 4k. Wsporniki płyty (GB poza krawędzią) — decyzja Bartka 2026-10-21
**Etap 1 (zrobiony, staging): wykrywanie i wyłączenie z obrysu siatki.** Wspornik = krótka belka „GB-{s}x{h}mm deep TOC=…” wystająca poza
krawędź płyty (szer. zwykle 600, bywa 450–800; wysięg 788–1525; h 225 / 300 / 450), czasem ze strefą pogrubienia w płycie przy palu
(„375THK” / „300THK” / „450THK” / „525THK”, przerywany U w płycie po drugiej stronie nasady; nie zawsze — wspornik może mieć wysokość płyty).
Przykłady: PLOT 28 / 29 / 60 / 308 / 313-314 / 13-14 / 67-75 / 101-102, BLOCK C (24 w rzędzie), BLOCK D (1115 × 1800), Plot 13 CNH (GB 600 × 450).
- Czyste `CantileverRules` (testy `CantileverRulesTests`): `FindTabs` — prostokątne języczki zamkniętego obrysu (dwa narożniki wypukłe,
  sąsiednie wklęsłe, ramiona równe; szer. 300–1500, wysięg 300–3000 — zakresy poszerzone na prośbę Bartka), `RemoveTabs` (krawędź prosto
  przez nasadę, nasady współliniowe usunięte), `TryU` (kawałek U obrysu / strefy), `IsRootOf` (strefa przy palu: te same końce, kierunek
  przeciwny), `ParseGb` / `ParseToc` (nie „NIB TOC”). Występy okien 2128 × 788 i wcięcia 645 × 100 — poza zakresem.
- `CantileverEngine` (warstwa **RC-CANTILEVER** — magenta jak RC-BEAM, rekord **RC_CANT**: [1] wersja 1 [2] szerokość [3] h z GB (0 = brak) [4] TOC [m] [5] grubość
  strefy przy palu (0 = brak) [6] długość strefy [7] kierunek wysięgu „±X / ±Y”; bez handle — CloneRemap niepotrzebny; znacznik RC_GA z obrysu):
  wspornik potwierdzony = opis „GB-…” ≤ 2500 od prostokąta (przypisany najbliższemu) albo strefa przy palu; inne języczki — komunikat
  (Prepare GA) / pytanie Tak/Nie (RC_CANTILEVER). Obrys: ~~wierzchołki na nowo bez języczka~~ — **od 2026-10-22 obrys na rysunku BEZ
  zmian** (uwaga Bartka: „Prepare GA zmienia faktyczny obrys płyty!” — linia na nasadzie, której w GA nie ma): języczek zostaje,
  XData `RC_CANT_TAB` na obrysie = lista „kierunek;szerokość;wysięg” (bez współrzędnych — MOVE / COPY bez skutków);
  **`GeometryHelper.GetPolylineVertices` / `PolylineBbox` zwracają obrys do obliczeń bez tych języczków** (`CantileverRules.EffectiveOutline`)
  — siatka, UB, nib, belki, przekrój liczą krawędź prosto przez nasadę jak dotąd; wierzchołki z rysunku: `GetRawPolylineVertices`.
  `RestoreOutlineTabs` (Prepare GA po JoinLoops — wspornik z kawałka U; RC_CANTILEVER; RC_GENERATE → Cantilever) wstawia języczek
  z powrotem w obrys przecięty (rysunki z poprzedniej wersji). Prostokąt wspornika
  na RC-CANTILEVER; strefa przy palu → RC-CANTILEVER (rodzaj linii bez zmian).
- **RC_PREPARE_GA:** przed domknięciem nibu i JoinLoops — `ProcessOpenPieces`: wspornik jako osobny kawałek U obrysu (PLOT 13-14, 67-75;
  końce na końcach innych kawałków albo na obrysie, bok końcowy poza obrysem, linia ciągła — przerywane U to strefy / ramki detali) → w obrysie
  prosty odcinek nasady (2-wierzchołkowa polilinia), U → prostokąt RC-CANTILEVER; po JoinLoops — `ProcessClosedOutlines` na zamkniętych
  SD-PILED-RAFT rzutu (obrys z GA i pętle). Opisy „GB-…” i „xxxTHK” strefy przy palu wyjmowane z kandydatów K8 (bez strefy belki RC-BEAM
  w nasadzie). Komunikaty tylko dla rzutu dolnego; w podsumowaniu „wsporniki (wyłączone z obrysu siatki): n”.
- **RC_CANTILEVER / RC_WSPORNIK:** istniejący rysunek — wskazane obrysy (Enter — wszystkie zamknięte SD-PILED-RAFT), opisy z całego modelu,
  strefy przy palu z otwartych SD-PILED-RAFT; ostrzeżenie o strefach RC-BEAM leżących na wsporniku / w strefie przy palu (z K8). Potem Mesh od nowa.
- NibDetector i FindHoles pomijają RC-CANTILEVER.
- Szablon `rc-template.dxf.gz` (2026-10-21): 24 kreskowania detali PH bez obiektów obwiedni — asocjatywność 0 (było „Hatch associativity
  removed” ×24 przy Prepare GA); 11 kreskowań logo w A1-BL — bez flagi „derived” ścieżki i bez XData GradientColor / ACAD (błędy rysowania logo).
- **Strefa wspornika nakłada się na płytę (uwaga Bartka 2026-10-21 „jak BEAM”):** prostokąt RC_CANT od końca wspornika do końca strefy przy palu
  (bez strefy — `NoRootExtension` = 1000, zasięg prętów prostych); rekord [8] wysięg poza krawędź, [9] długość w płycie. RC_CANTILEVER
  przedłuża wsporniki z pierwszej wersji (`UpgradeOldStubs`).
**Etap 2 (zrobiony, staging): RC_GENERATE → Cantilever / RC_GENERATE_CANTILEVER** (`AutoRebarEngine.Cantilever.cs`, czysty plan
`CantileverRules.PlanBars`, testy): pręty w detalu 1:25 ścieżką belek (PrepareBeamDetail / FinishBeamDetail — ramka, „DETAIL 'n'” na rzucie
dolnym, detal z obrysem z języczkiem `InsertTab`; znacznik RC_AUTOREBAR_BEAM = handle wspornika, ponowne generowanie usuwa tylko jego
pręty; kody BL / BU / BM — seria dolna, BBS jak belki). Geometria: oś = kierunek [7], nasada = przecięcie osi strefy z obrysem zewnętrznym.
- Kolumny prętów w poprzek: rozstaw ≤ 175 (opis „-150”): 450 → 3, 600 → 4, 800 → 5; pierwsza oś 45 + Ø strzemienia + 8.
- Strzemiona (decyzja Bartka): 4-leg (2 zachodzące, `PlanLinkLegs` Legs4, B 370 przy 600), h ≤ 225 → H10 @100, ≤ 300 → H12 @150, wyżej → H12 @200;
  A = h − 40 − 45 (300 → 215, 375 → 290, 225 → 140); osobno wysięg (h z GB) i strefa przy palu (grubość „xxxTHK”, brak opisu → h + 75).
- U-bary H16 kształt 21 (B = A strzemienia − 2·Ø strzemienia **w dół** do 5, jak belki — 215 → 190, jak rysunek biura): ze strefą — na końcu wspornika i końcu strefy (tył 45 od lica),
  ramię = Ceil25((rozpiętość + zakład 650) / 2) (PLOT 28: 1050); ramię > 1300 → U-bary A = 750 + pręty proste H16 między nimi (zakład 650).
  Bez strefy (wspornik wysokości płyty): U-bar tylko we wsporniku (ramię do nasady, maks. 1300) + pręty proste **H12** górą i dołem
  do 1000 za krawędź (decyzja Bartka), zakład z ramieniem 650; jedna pozycja „2xn … B+T”.
- **Poprawki po teście PLOT 28 (uwagi Bartka 2026-10-21):**
  - **Uskok:** wspornik liczony od linii uskoku nibu, gdy przecina oś strefy ≤ 300 w głąb od krawędzi (`CantileverRules.StepCut`; PLOT 28:
    strefa przy palu 600, wspornik 923 — jak rysunek biura); strefa przy palu = [6] − uskok. W detalu linia uskoku w poprzek wspornika
    (gdy linia nibu jej tam nie rysuje).
  - **Kształt strzemion (zasada ogólna):** elementy W PŁYCIE (pile cap / strefa przy palu, belki i pogrubienia w płycie) — zawsze **51**;
    POZA PŁYTĄ (wsporniki, belki poza płytą) — zawsze **63**. Strefa przy palu — strzemiona w **obu kierunkach** (wzór PLOT 28: 12 i 14);
    **zagnieżdżenie globalne (test7, 2026-10-22):** strzemiona z prętami wzdłuż X — zewnętrzne, wzdłuż Y — wewnętrzne (jak belki); wspornik
    w osi Y — „w poprzek osi” zewnętrzne, w osi X — zamienione (`NestRootLinksGlobal`); przekrój: U-bar leży na strzemionach w poprzek siebie
    (`UBarSeatInset`).     drugi kierunek (osobna pozycja) WCHODZI W PIERWSZY: A − 2·Ø strzemienia **w dół** do 5 — wewnętrzne musi się zmieścić (375: 290 − 24 = 266 → **265**; 750: 665 → 640)
    (uwaga Bartka 2026-10-22; ~~275~~, ~~270~~ — kolizja). Wariant „jedna pozycja” (oba kierunki tej samej wysokości
    obniżone o Ø — jeden przy spodzie, drugi przy wierzchu, zachodzą na siebie) — nie wybrany.
  - **Wspornik POZIOMY (oś X) — jedna pozycja strzemion w strefie przy palu (decyzja Bartka 2026-10-23, test10-poprawiony):** oba kierunki
    ta sama pozycja, A = najwyższe możliwe − **15** (375: 290 → **275**), rozstaw dokładnie **150** (reszta po równo z obu stron: 4 co 150
    zamiast 4 co 169). Przekrój: strzemiona z prętami wzdłuż X przy wierzchu (podniesione o 15), wzdłuż Y przy spodzie — krzyżują się
    (`SectionPlanner.SingleLinkLift` / `SingleRootLinkDrop`). Wspornik pionowy (oś Y) — jak wyżej (zewnętrzne + wewnętrzne A − 2·Ø)
    (`CantileverRules.PlanBars(singleRootLink)`; pytanie Bartka 2026-10-23 — „tylko wspornik poziomy”).
  - **U-bary główne wspornika: zakład ≥ 700** (`UBarMinLap`, 2026-10-23; 40·Ø = 640 za mało); UB SIDE — jak dotąd (40·Ø).
  - **Przekrój wspornika / strefy przy palu:** opis strzemienia w widoku NAD nim, strzałka do górnego ramienia (test10-poprawiony D-D / C-C).
  - **Przekrój wspornika (uwagi Bartka 2026-10-23):** najpierw pręty główne przekroju (kropki prętów wspornika / strefy przy palu) w rzędzie
    opisów najbliżej betonu, siatka i nib w tym miejscu — dalsze rzędy; tył U-bara w widoku licuje się z pionowym ramieniem strzemienia
    (lico zewnętrzne = lico zewnętrzne, `FlushCantUBarsToLinks`, ≤ 30 mm). **UB SIDE — tył w środku tyłu U-barów głównych** (o Ø U-bara,
    plan `PlanBars`; wcześniej kolizja kropki z U-barem w A-A).
  - **Ramka detalu:** sam offset strefy (`FramePolygon(strefa, null)`, ⊕600) — bez przycinania do obrysu z języczkiem (dziwny kształt).
  - **Opisy w detalu:** odnośniki „GB-…mm deep / TOC=…” i „xxxTHK” z grotem w strefie — kopia w detalu (grot + Shift, tekst w połowie
    odległości, wysokość 62,5, grot ×0,5) — `CopyCantileverLabels`.
  - **UB 03:** na odcinku krawędzi, przez który przechodzi wspornik (szerokość strefy RC-CANTILEVER), bez UB 03; UB 01/02 bez zmian
    (`CantileverSpansOnEdge` w `GenerateUBLayerCore`, nibUB). Bez wsporników — bez zmian.
- **Zasady belek dla strzemion i prętów bocznych (uwaga Bartka 2026-10-22, test4: GB 600×525, pile cap 600THK):** cięcie strzemion
  Auto jak belki (`PlanLinkLegs` LegsAuto, krawędziowa): H ≤ 375 — dwa zachodzące (B 370), wyżej 2-cięte na pełną szerokość (600 → B 510;
  ~~zawsze 4-leg~~ — „05, 06 za wąskie”). Pręty boczne jak belki (tryb B3, z wysokości GB: ≤ 375 brak, 450–525: 1 na bok, 600–675: 2 …):
  U-bary „UB SIDE” H12 kształt 21 poziomo na końcu wspornika i końcu strefy przy palu, B = B strzemienia − 2·Ø w dół do 5 (485),
  ramię Ceil25((rozpiętość + zakład 500) / 2); > 1300 → UB SIDE 525 + pręty proste „SIDE” między nimi; bez strefy — UB SIDE tylko
  we wsporniku.
- **Kształt strzemion — zasada ogólna także w belkach (2026-10-22):** strefa belki w płycie → 51, poza płytą → 63 (kod z okna RC_BEAM
  [14] nie decyduje — komunikat, gdy inny). Opis UB SIDE wspornika — standardowa linia odniesienia (stała odległość za oknem detalu,
  jak pozostałe opisy; belki — bez zmian: krótka prostopadła, decyzja 2026-10-14).
- **RC_SECTION przez wspornik (etap 3, 2026-10-22):** PrepareSection tnie obrys JAK NA RYSUNKU (`GetRawPolylineVertices`, z języczkami);
  strefy RC_CANT rzutów → `BeamBand` z `Cantilever = true`: wspornik (s 0…wysięg od uskoku, `CantGeometry` — wspólna z generowaniem)
  Height = obniżenie + h, `TopDrop` = (SSL − TOC)·1000 (PLOT 28: 75; brak TOC/SSL — 0), strefa przy palu Height = „xxxTHK”, TopDrop 0.
  Planer: profil pasu z wierzchem −TopDrop; wymiary pasu wspornika (wysokość od wierzchu pasu, obniżenie, długość pod spodem);
  U-bary BU wspornika — kropki dół / góra (jak BM, bez dubli z zakładu U-barów), w widoku kształt U (tył przy zagięciu z SymbolSide);
  strzemię o mniejszym A (drugi kierunek) w środku strzemienia zewnętrznego pasu (`NestInset`). Pręty detalu wspornika mapowane
  z ANCHOR-a strefy RC_CANT (okno = strefa ⊕ 600). Belki bez wsporników — wynik bit w bit (złote testy). Poprawka 2026-10-22 (uwaga Bartka „kwadratowe pręty,
  złe wymiary — ma być jak belki”): U-bar w widoku = kopia pręta wzorcowego 21 (gięcia), wysokość = WŁASNE B (ramię dołem na
  strzemieniu — U-bar od strony pile capa nie bierze wysokości pasu strefy), drugi U-bar tej pozycji w środku pierwszego (o Ø, B − 2·Ø);
  wysokość / obniżenie tylko przy końcu odcinka (bez linii wymiarowej w betonie wspornika); C-C — bez grubości płyty, znacznik TOC.
  Poprawka 2 (uwaga Bartka 2026-10-22 „zgodnie z prawdą, prawdziwe wymiary prętów”): strzemiona wspornika / strefy przecięte
  wzdłuż osi — KROPKI dołem i górą (płaszczyzna cięcia przez ramiona; wysokość z A pręta wzorcowego), nie pionowe ramię; U-bary
  obu końców — ten sam pręt, bez zmniejszania (~~drugi o 2·Ø niższy~~ — wymysł). Generowanie: tył U-bara / UB SIDE przy końcu
  strefy przy palu w środku strzemion drugiego kierunku (otulina + Ø strzemienia; PLOT 28: −678).
  Poprawka 3 (2026-10-22): UB SIDE wspornika w przekroju (w poprzek — kropki obu ramion, wzdłuż — kropki tyłu; poziomy z pasa
  samego wspornika, liczba z rozkładu), wolne końce U-barów = rzeczywiste końce (symbole Z1), tył UB płyty przy ramieniu strzemienia
  wspornika dosunięty do styku (`FlushUbToCantileverLinks`, szczelina ≤ 30), siatka płyty: pas samego wspornika zawsze zakazany
  (wspólny schemat góry i dołu — w jednej linii), pręty siatki w nasadzie wspornika — prawdziwe położenia (`BeamBand.PileCap`).
  Opisy (wzór Bartka 2026-10-22): kropki dołem w środku betonu (siatka dolna płyty w strefie przy palu) — numer tuż pod kropką, w betonie
  (`InnerLabelMinDepth` 150; tylko przekrój z wspornikiem). Rzut: kilka UB 03 na jednej linii (krawędź przerwana wspornikiem) — opis
  prostopadle do linii rozkładu (wzdłuż prętów, za płytę), nie w jednym rzędzie z boku płyty.
  Poprawka B-B (2026-10-22): strzemię pasu wspornika (A, Ø) tylko z rozkładu, który linia cięcia przecina / przy którym leży (dawniej
  strzemię strefy przy palu „podnosiło” strzemię wspornika — U-bary wychodziły poza nie); przekrój samego wspornika — bez wymiaru
  obniżenia 75, znacznik TOC na wierzchu wspornika (rysunek bierze V znacznika); pal w pobliżu cięcia — w swoim położeniu (rzut osi)
  albo na środku odcinka, nie „500 od krawędzi”.
- **Pal w przekroju zawsze pełną średnicą (2026-10-22, wszystkie przekroje):** cięcie poza osią pala — szerokość = średnica (pale 300),
  nie cięciwa (dawniej np. 196); złoty test „piles” zaktualizowany (drugi pal 566 → 600). Do zrobienia: znacznik TOC
  przy wsporniku, UB SIDE w przekroju.
- Do zrobienia: wspólny detal identycznych wsporników („DETAIL 'n' nNo.”), przekroje przez wspornik (RC_SECTION — prośba Bartka 2026-10-22:
  przekrój ma rysować wsporniki i wszystko na płycie; wzór PLOT 28: D-D wzdłuż wspornika — wspornik 923 × 300 z wierzchem TOC 75 niżej
  niż płyta, strefa przy palu 600 × 375, płyta 225 + nib; C-C w poprzek — 600 × 300, 4 kolumny U-barów, strzemiona 4-leg), wspornik
  w narożniku (Plot 13 CNH).

## 4l. Szereg płyt z belkami GB poza płytą — decyzja Bartka 2026-10-23 (etap 1, wzorzec PLOT 304–307 / test11)
- **GA:** jeden obrys zewnętrzny SD-PILED-RAFT obejmuje kilka płyt; w nim zamknięte linie uskoku płyt (każda z opisem „PLOT …”), prostokątne
  pustki między płytami, otwarte linie stref narożnych (735 × 600, opis „525THK”) i pasy betonu między płytami z opisem
  „GB-600x450mm deep / TOC=23.350”.
- **Lista Prepare GA (`GaImportEngine.MergeTerraces`):** opisy płyt w jednym obrysie, każdy we WŁASNEJ zamkniętej linii uskoku → jedna pozycja
  `PlotLabelRules.MergeLabels` („PLOT 304-307”; ten sam przedrostek — min–max numerów, inaczej „ + ”). `GaPlot.Members` — opisy, ramki, linie
  uskoku płyt (kolejność od lewej). Inne przypadki (kilka opisów bez własnych linii uskoku) — bez zmian.
- **Model RC „3 płyty + 4 belki GB” (`GaImportEngine.SplitTerrace`, czysta logika `TerraceRules`, testy `TerraceRulesTests`):**
  obrys płyty = linia uskoku odsunięta na zewnątrz każdą krawędzią do najbliższej równoległej krawędzi obrysu / pustki (50–255 — nib; brak —
  krawędź bez nibu; krawędzie prawie osiowe — tolerancja 4 mm, test12: x 33885,71 → 33885,76); RC_GA [3] = etykieta tej płyty ([1] = etykieta łączna — arkusze, EraseOld bez zmian). Pas = beton obrysu poza płytami
  i pustkami (prostokąt 200–1500 szer.; współrzędne sklejane ≤ 5 — GA 27857 / 27858); belka tylko z opisem GB w pasie (bez opisu — komunikat).
  Strefa belki = pas + strefy narożne w płytach (linia strefy z GA; bez niej opis „…THK” → nib + 600; inaczej do krawędzi płyty) — jedna belka
  od narożnika do narożnika (test11: 735 + 6650 + 735 = 8120). H = głębokość GB + (SSL − TOC) (bez TOC: THK narożnika − głębokość),
  [39] = SSL − TOC, [6] = płyta przy początku pasa (strefę generuje RC_GENERATE → Beam tej płyty). Obrys zewnętrzny i pustki GA zostają
  (od 2026-10-28 — warstwa rysunkowa, niżej); opisy GB / „…THK” stref nie idą do K8 ani wsporników. Dalej każda płyta jak zwykła
  (`ProcessSlab`: wsporniki, nib, JoinLoops, K8 — kawałki / opisy w obwiedni płyty + 300). PH / ramki: obrys najbardziej na prawo.
- **Rzuty przystające (PLOT 304 i 307 tego samego kształtu):** `FindPartnerView`, `FindBottomView` (siatka górna) i widoki przekroju biorą
  tylko obrys z tym samym RC_GA [3]; mapy (Add) — dopasowanie po [3].
- **Generowanie (`GenerateOneBeam`, [39] > 0):** zakres strzemion odcinka dzielony wzdłuż osi obrysami płyt rzutu (`ViewSlabLoops` — zamknięte
  SD-PILED-RAFT z tym samym znacznikiem, bez linii uskoku; `TerraceRules.SplitParts`): poza płytą strzemiona 63, A z H − [39] (450 → 365),
  w płycie 51, A z H (525 → 440); pierwsze strzemię od granicy części jak od lica. Pręty B+T prosto (rzut bez zmian — poziom GB tylko
  w przekroju), U-bary (B) i pręty boczne z H − [39]. Belki bez [39] — bit w bit jak dotąd.
- **Uwagi Bartka 2026-10-23 (test13, wzór biura PLOT 304–307):** strefa narożna GB w płycie = jak pogrubienie przy wsporniku —
  siatka B1/B2/T1/T2 i UB 01/02 bez zmian (`BeamStore.MeshZonesOfSlab` pomija strefy z [39] > 0 w E2, UB 03 w detalu, T IN NIB),
  UB 03 przerwane na szerokości GB na każdej krawędzi, przez którą GB przechodzi albo którą pokrywa (`GroundBeamSpansOnEdge`, także
  płyta bez [6] tej belki — wcześniej 305-306 miało UB 03 przez belkę). Strefy GB z Prepare GA: detal 1:25 (decyzja Bartka — detal,
  nie rzut), otulina RÓWNO 50 dookoła (góra / dół / boki, bez zapasu 5 mm i bez obniżenia − 15 drugiej warstwy strzemion i U-barów —
  `PlanVerticalUBars(noSecondLayerDrop)`); detal GB — ramka z offsetu całej strefy, pale w całej ramce, opisy „GB-…” i „525THK”
  z rzutu (`CopyCantileverLabels`, bez łączenia identycznych), opisy strzemion części (`GbLinkLeader`): narożniki — prosto na zewnątrz
  końca okna, część GB — wzdłuż rozkładu do środka części i w poprzek poza okno (nie na jednej linii). Strzemiona części
  w płycie co 150 dokładnie, reszta po równo (wzór: „4 H12-12-150 LINKS”), poza płytą — rozstaw z rekordu (200).
- **Uwagi Bartka 2026-10-23 (cd.):** podział strzemion GB na części — po LINII USKOKU płyt (`ViewSlabLoops`: linie uskoku, obrys
  tylko bez uskoku; wzór: naroże 600 → „4 H12-12-150”, GB z nibami 6920 → „35 H12-15-200”); w części narożnej także strzemiona
  w drugim kierunku (pręty wzdłuż osi, B = część − 2 · 50, w poprzek co 150 dokładnie — wzór „4 H12-14-150 LINKS”).
- **Identyczne belki / wsporniki jednym detalem (uwaga Bartka 2026-10-23):** podpis elementu (`BeamSignature`: parametry RC_BEAM bez [6],
  strefa względem naroża — zaokrąglenie 10, odcinki z zamknięciami, części strzemion GB; wspornik: kierunek, wysięg, szerokość, h, strefa
  przy palu, uskok) w ANCHOR-ze (RC_BEAM_DETAIL [5], [6] = strefa detalu głównego). Pierwszy element = detal główny (tytuł, szablony),
  kolejny o tym samym podpisie — na rzucie ramka i „DETAIL ’n’” detalu głównego, jego rozkłady (cały detal bez tytułu) nad detalem
  głównym w miejscu k · (ramka + 2 ramiona opisów + 1500), wokół kopii ramka z przekątnymi (rola COPYBOX, odświeżana po każdym
  generowaniu). BBS liczy kopie (zwykłe rozkłady). Tylko przesunięcie — obrót / odbicie = inny detal.
- **Uwagi Bartka 2026-10-23 (cd. 2):** belka wewnętrzna vs krawędziowa z nibem — inne zbrojenie (wzór): `TerraceRules.PlanGbLinks` —
  narożnik = strefa ∩ linia uskoku; na całą szerokość (wewnętrzna) — strzemiona GB (63) przerwane (35 co 200 na 6920), węższy (krawędziowa:
  nib przy krawędzi płyty w narożu, 600 × 465) — strzemiona GB przez całą belkę (41 co 200); w narożniku 51 w obu kierunkach co 150
  (wewnętrzna 4 + 4 jedną pozycją, krawędziowa 4 (B 365) + 3 (B 500)); GB co 200 DOKŁADNIE (`ExactLayout`). Podpis wspólnego
  detalu z narożnikami (wewnętrzna ≠ krawędziowa). Ramka detalu (otwór, belka, wspornik): offset 450 (dawniej 600 / otwór 500), za
  krawędzią płyty 150 (dawniej 300) — `BeamDetailRules.Margin / OutsideEdge`, `HoleDetailFrameOffset = Margin`.
- **Przekrój GB (RC_SECTION, 2026-10-23):** strefy [39] > 0 dotykające rzutu (narożnik w obrysie) — beton = obrys ∪ strefy GB
  (`RectilinearPolygons.AddClipped`), pręty GB z punktu w strefie (także z detalu — rzut strefy z narożnika), pale w strefach; pasy:
  narożnik w płycie — jak strefa przy palu (Cantilever + PileCap, wierzch 0, spód −H), reszta — jak wspornik (wierzch −[39], spód −H,
  znacznik TOC = SSL − [39]); otulina / A strzemion z rekordu. Testy `SectionGroundBeamTests` (D-D w poprzek, C-C wzdłuż).
- **Kilka płyt naraz (prośba Bartka 2026-10-26, PLOT 357-362 — szereg w osobnych obrysach):** lista Prepare GA z wyborem wielu
  pozycji (Ctrl / Shift, „Zaznacz wszystkie”) → `GaImportEngine.Group` (etykieta łączna `MergeLabels`, obwiednia obrysów i regionów,
  `GaPlot.Parts`). Jeden rysunek RC: każda część — własny obrys (RC_GA [3] = etykieta części), opis, SSL / grubość
  (`BeamGaContext.For`), szereg w części (np. PLOT 357-358) dzielony jak dotąd (`SplitTerrace(tagLabel)`); wsporniki, nib, K8 per płyta
  (kawałki w obwiedni + 300). Belki GB MIĘDZY osobnymi obrysami (języczki 276 + przerwa, różne SSL) — jeszcze nie (kolejny etap).
  Uwagi Bartka 2026-10-26: opisy i ramki WSZYSTKICH płyt (główna, szereg, grupa — `GaPlot.LabelKeys`) kopiowane i przebudowane
  (dawniej opis z „NIB TOC=…” innej płyty ginął); odnośnik bez treści (strzałka opisu MText) kopiowany; w szeregu beton przy JEDNEJ
  płycie (języczek 276 × 600) zostaje w jej obrysie (`TerraceRules` — nie pas GB), wspornik wykrywa CantileverEngine; krótkie
  wsporniki: `CantileverRules.MinDepth` 200 (dawniej 300), opis „300THK / TOC=…” (`ParseThkToc` — wysokość od TOC; jeden opis
  dla dwóch wsporników), opisy wsporników ze wszystkich opisów rzutu; wysięg ≤ 600 od uskoku — strzemiona strefy przy palu
  w kierunku głównym co 120 dokładnie („5 H12 … LINKS”, `ShortMainLinkSpacing`).
- **Siatka grupy płyt (prośba Bartka 2026-10-26):** RC_GENERUJ_SIATKA — wskazana płyta z RC_GA [3] (grupa / szereg z Prepare GA) →
  pytanie „zazbroić także pozostałe płyty grupy? [Tak/Nie]”; `AutoRebarEngine.GroupMeshPairs` (ten sam RC_GA [1] i rzut, inna [3];
  góra przez `FindPartnerView`) → ta sama sekwencja B1, B2, T1, T2, UB, UB NIB, kolizje, B3 ADD dla każdej pary (`GenerateMeshPair`,
  grubość pytana raz). Płyta bez rzutu górnego — ostrzeżenie i pominięcie. Płyty przystające bez pali / otworów w innych miejscach
  dostają ten sam układ (planowanie deterministyczne); różnice wynikają z omijania pali i otworów.
- **Zakład przy szerokości „między siatką” (uwaga Bartka 2026-10-26, PLOT 357, szer. ~6500 — 3 rozkłady T1 zamiast 2):** okno zakładu
  góry 500–700 (200) jest węższe niż siatka długości 250, więc przy części szerokości żadna para długości z siatki nie trafia w okno
  i plan przeskakiwał na N + 1 prętów. Teraz przy danym N bez rozwiązania z siatki ostatni pręt ma dowolną długość z siatki 25
  (≥ minLen; `PlanCandidatesCore` — ranking za planami z samej siatki; `TopLapSearch.Find` — to samo przy palach).
- **Języczek z wierzchołkiem współliniowym (uwaga Bartka 2026-10-26, PLOT 362):** w GA obrys miał dodatkowy wierzchołek na boku
  końcowym języczka — `CantileverRules.FindTabs` / `RemoveTabs` pomijają punkty współliniowe (`Straight`, odchyłka ≤ Tol), więc taki
  wspornik jest wykrywany jak pozostałe (indeksy E0 / E1 nadal w oryginalnym obrysie).
- **Grupa płyt w RC_GENERATE_BEAM / RC_GENERATE_CANTILEVER (uwaga Bartka 2026-10-26, PLOT 357-362):** jak siatka — wskazana płyta
  grupy (RC_GA [3]) → pytanie o pozostałe płyty (`Commands/GroupPick.Others`, wspólne z RC_GENERUJ_SIATKA) → belki / wsporniki
  każdej płyty grupy; identyczne wsporniki różnych płyt dzielą jeden detal (kopie nad detalem głównym, fix95).
- **Opisy w detalach 1:25 (uwaga Bartka 2026-10-26):** wariant „druga strona” w `GenerateDistributionWithLeaderAtOffset` ma ramię
  `LeaderArm` (w detalu 2400 · 0,5) jak wariant domyślny — dawniej stałe 2000, więc opis po drugiej stronie (np. drugi UB GB /
  wspornika) odlatywał ok. 1,7 × dalej; na rzucie bez zmian (LeaderArm = 2000). UB SIDE belek w detalu — opis standardowy
  (`standardLeader`, jak przy wspornikach), na rzucie bez detalu — jak dotąd tuż przy pręcie.
- **Tytuł detalu z liczbą elementów (wzór Bartka 2026-10-26):** detal główny z kopiami identycznych elementów — tytuł
  „DETAIL 'n'” / „{liczba}No.” (podkreślone) / „SCALE 1:25” (`BeamDetailTitle`, aktualizowany w `RefreshCopyBox` przy każdej kopii).
- **Linie pogrubień z GA zostają (uwaga Bartka 2026-10-26):** `SplitTerrace` nie usuwa linii stref narożnych przy GB (przerywane U
  SD-PILED-RAFT) — tylko wyjmuje je z list kawałków obrysu / uskoku; wspornik: strefa przy palu z GA zostaje na swojej warstwie
  (dawniej przenoszona na RC-CANTILEVER). Fioletowa strefa belki / prostokąt wspornika nakładane na wierzch, rysunek GA bez zmian.
- **Import map / analizy kilku płyt (prośba Bartka 2026-10-26, PLOT 357-362):** RC_IMPORT_MAP / RC_IMPORT_ANALYSIS — lista płyt z wyborem
  wielu (Ctrl / Shift, „Zaznacz wszystkie”), jedno wskazanie (lewy górny róg T1 pierwszej), pozostałe w układzie z pliku map; każda płyta
  z własnym RC_MAP [PLOT]. Kilka płyt — w AnalysisStore bez płyty (Punching zapyta). Generate ADD: mapa z kilkoma obrysami płyt
  („PLOT 357, 358”) — dopasowanie do każdego obrysu, strefy / opisy tylko z obrysu dopasowanej płyty (`MapData.Slabs`, `OnlyIn`).
- **Punching i ADD dla grupy płyt (uwaga Bartka 2026-10-26, 357-362.dwg):** RC_PUNCHING_AUTO — lista płyt raportu z wyborem wielu
  (`PunchingAutoEngine.Analyze` z listą indeksów): pale wszystkich wybranych płyt w jednym wyniku — tagi, notatka MANUAL i szablony
  PH1–PH9 („APPLICABLE FOR PILES …”, „nNo LOCATIONS”) dla całości (Plot = etykiety „ + ”). RC_GENERATE_ADD — pytanie o pozostałe
  płyty grupy (`GroupPick`). Mapa kilku płyt jednym obrysem („PLOT 357, 358” = 357 + GB + 358): `MatchMap` — numery z etykiety mapy
  (`PlotLabelRules.PlotNumbers`) → obwiednia tych płyt grupy (RC_GA [3], `GroupMemberBoxes`) = obwiednia obrysu mapy (± 100),
  przesunięcie z rogu, wierzchołki płyty na obrysie mapy ≥ 50 %; strefy tylko z części tej płyty (`OnlyIn`).
- **Krótki wspornik — strzemiona zamiast U-barów (uwaga Bartka 2026-10-26, poprawka fix98):** wysięg ≤ 600 ze strefą przy palu —
  bez U-barów H16; strzemiona 51 z prętami wzdłuż osi na całej długości (koniec strefy przy palu … koniec wspornika, B = długość − 2·otulina),
  A = A strzemion wysięgu − 2·Ø (jak B U-bara), co 120 dokładnie (`Plan.ShortMainLinks`); strzemiona strefy przy palu — standardowo co 150.
- **Przekrój bez stref belek / wsporników (uwaga Bartka 2026-10-26, 357-362.dwg):** belka GB i wspornik w przekroju biorą się ze stref
  RC-BEAM / RC-CANTILEVER (fiolet); detal (RC-BEAM-DETAIL) bez swojej strefy — `PrepareSection` wypisuje UWAGĘ (DETAIL 'n' bez strefy —
  przywróć Prepare GA, Beam / Cantilever) zamiast cicho rysować samą płytę.
- Do zrobienia (kolejne etapy): przekrój GB — sprawdzenie prętów w BricsCAD, belki stopnia (dwa SSL), złącze GB–płyta 975/900THK z NIB STEP,
  stopa 1070 z prętami lacer.

### Poprawka 2026-10-28 — Prepare GA nie zmienia geometrii płyt (KARDYNALNE, Bartek: „chcę mieć DOKŁADNIE taką geometrię, jak GA”)
- PLOT 344-346: obrysy płyt szeregu (`SplitTerrace`) miały krawędź w poprzek wylotu pasa GB (linia, której w GA nie ma) + dorysowane lica pasów
  — psuło rysunek i detal belki. Teraz:
  - obrys zewnętrzny GA i pustki — **bez zmian geometrii**, przeniesione na warstwę rysunkową `SD-PILED-RAFT-GA` (`GaImportEngine.IsDisplayLayer`;
    kolor / rodzaj / grubość linii, drukowanie jak warstwa źródłowa; „JakWarstwa” zapisane jawnie), znacznik RC_GA (EraseOld je usuwa);
  - obrysy płyt szeregu (SD-PILED-RAFT, RC_GA [3]) — tylko obliczeniowe, **niewidoczne** (`Visible = false`); lica pasów nie są dorysowywane;
  - `NibDetector.Detect` pomija warstwę rysunkową (obrys GA nie jest „obrysem zewnętrznym” linii uskoku);
  - wskazanie obrysu (`GaImportEngine.ResolveSlabPick`: SlabPicker, belki, wsporniki, przekroje, adopcja przekroju, 3D, RC_GENERATE_SLAB):
    obiekt warstwy rysunkowej → niewidoczny obrys płyty tego samego [1]/[2], zawierający punkt wskazania (inaczej najbliższy);
  - detal belki / wspornika (`PrepareBeamDetail`): przy niewidocznym obrysie linie betonu nie z obrysu, tylko z rysunku (`OtherPlanLines`
    bierze obrys / pustki GA, pomija obiekty niewidoczne).
  Płyty pojedyncze (bez szeregu) — bez zmian.
  - (fix112) warstwa `SD-PILED-RAFT-GA`: rekord najpierw dodany do tabeli warstw, potem kolor / rodzaj / grubość linii (bez przezroczystości
    i drukowania) — zgłoszenie „nie mogę zapisać” po Prepare GA. Podgląd odnośnika (`LeaderTransientDrawer`): bez `Linetype` na obiekcie
    chwilowym (eNoDatabase w logu).

## 5. Proces pracy
1. `planista-recenzent`: plan (pliki, podejście, ryzyka, testy w BricsCAD). Przy większych zmianach
   plan recenzuje ChatGPT: `scripts/recenzja-gpt.ps1 -Plan <plik>`.
2. Użytkownik akceptuje plan (przy większych zmianach).
3. `implementator`: zmiany + `dotnet build` bez błędów (+ `dotnet test`, jeśli dotyczy geometrii).
4. Recenzja diffu przez ChatGPT (`scripts/recenzja-gpt.ps1`, tylko odczyt) wg tego dokumentu →
   Claude weryfikuje każdą uwagę → lista poprawek albo „AKCEPTUJĘ”.
5. Pętla poprawek (max 3 rundy), potem instrukcja testów dla użytkownika.
6. Użytkownik testuje w BricsCAD → commit robi sam.

## 6. Otwarte tematy
- Zestawienie: rozkłady bez opisu nie są liczone.
- (Rozwiązane w W2) Rozkłady skopiowane razem z prętem (także stare rekordy 1000) wskazują na kopię pręta — mapa klonowania.
- REDO po COPY odtwarza kopie bez przepięcia z CommandEnded (ograniczenie BricsCAD, jak wzorzec) — do decyzji (RANO_DECYZJE).
- Kształty BS 8666 (W4, `docs/PLAN_W4_BS8666.md`): W4a–W4c, W4d (geometria wg rysunków normy: 12, 14, 15, 22, 24, 33, 46,
  51), W4g (zaokrąglanie 00 do 25 mm), W4e (wymiary zewnętrzne, łuk osi r + d/2 — #13, `RC_REBUILD_BARS`) i W4f (walidacja
  minimów — ostrzeżenia) i W4h (kąt ramienia skośnego z B albo wpisany) zrobione (staging) — pierścień = 75, 44 = kapelusz.
  Do zrobienia: W4d-3 (23, 25–29, 31, 32, 34–36, 41, 47, 56, 98 — geometria wg rysunków i tabela `BarDimensionRules`).
- Build testów dymnych: `dotnet build tools/BrxSmokeTests/BrxSmokeTests.csproj -c Release -p:BuildProjectReferences=false`
  (bez tej opcji ProjectReference buduje wtyczkę do `bin\` — nadpisuje DLL użytkownika).
- Poprawki błędów wzorca (`docs/POPRAWKI_WZORCA.md`): S-REG (`tools/RegressionDump/run-regression.ps1`) porównuje zrzut
  główny i zrzut przypadków poprawek (`RC_REG_FIX_*`, pasy Y = 100000 + 10000·i) z wzorcem; różnice muszą być DOKŁADNIE
  sumą `tools/RegressionDump/expected-diffs/{polyline,block}/NN-*.txt` (`# bug:` z POPRAWKI_WZORCA, np. 3 albo 3b). Testy W1:
  `tools/BrxSmokeTests/run-planc-w1.ps1`; W2: `run-planc-w2.ps1`, spike zdarzeń klonowania: `run-planc-w2spike.ps1`.
  Przeniesienie zmian kolegi (`docs/PLAN_PRZENIESIENIA_2026-10-09.md`, K3–K6: P16, IDENT, SCRIPT, COPYMSG, P12, P3c):
  `run-planc-port.ps1`; przypadki S-REG F12 / F3g / F14 (expected-diffs 12, 03c, 14, 08d).
- K1 (nib zawinięty, POPRAWKI 29): `tools/BrxSmokeTests/run-planc-k1.ps1` (najpierw `-Baseline -PluginDll <DLL HEAD>`) — Holbrook 458, 461-462,
  524, 526, 527, 322, 321, 525: Prepare GA + Mesh, zrzut rozkładów = baza poza T1 / T2 / UB 01 / 02 przy krawędziach z wolnym końcem
  (`AutoRebarEngine.NibFreeStepEdges` — diagnostyka), bezpiecznik rozstawu ≥ 100, informacyjnie UB / metry góry / masa; S-REG F29
  (expected-diffs 29).
- „Plan C” (plan etapów: `docs/PLAN_C.md`): pręt jako BlockReference z własnymi gripami (na razie wariant B: grip na końcu obrysu polilinii
  zmienia odcinek końcowy — `BarGeometryWatcher.TryEndStretch`; ruch w środku pręta jest cofany).
  Etap 1 (próba go / no-go, `docs/PLAN_C_etap1.md`): zaimplementowany `RC_TEST_BARBLOCK`, czeka na testy T0–T16 w BricsCAD.
  Etap 2 (`docs/PLAN_C_etap2.md`, warstwa `BarEntity`) — decyzje z 1-POPRAWKI:
  - ~~Brak numerowanych prętów-bloków dla użytkownika~~ — **zniesione w etapie 3, krok 3f** (`docs/PLAN_C_etap3.md`):
    `RC_BAR_FORMAT` → Blok, gripy / STRETCH z propagacją (`BarChangePropagator`). `RC_TEST_BARBLOCK` bez opcji `Pozycja`.
  - `RC_DISTRIBUTION` z pręta-bloku bez numeru (np. `H12-T`) — odmowa `[RC] Pręt testowy bez numeru pozycji.`
  - Numerowany pręt-blok w testach zwykle z etykietą (od W1c kaskada ERASE działa też bez etykiety — poprawka #2).
  - AutoRebar od etapu 3 (3b) widzi pręty-bloki jako szablony; w formacie Blok tworzy szablony jako pręty-bloki (3c).
  Etap 3 (`docs/PLAN_C_etap3.md`) — kroki 3a–3f zrobione (staging). Testy 3f: `tools/BrxSmokeTests/run-planc-f3.ps1`
  (RC_BAR_FORMAT, grip / STRETCH numerowanego pręta-bloku z kopią → nowy numer, rozkład, etykieta, BBS; UNDO / REDO;
  pręt gięty; LengthOverridden; AutoRebar Blok = Polilinia).
