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
| Znacznik belki | na rozkładzie belki | `RC_AUTOREBAR_BEAM` | Handle strefy belki, dla której wygenerowano rozkład (kody BM / BU / BB / BT / BL / BS / BC / BN) |

Powiązania:
```
Pręt ──[13] LabelHandle──► Etykieta ──[1] bar handle──► Pręt        (back-link sprawdzany!)
Rozkład ──[18] SourceBarHandle──► Pręt
Rozkład ──[11] AnnotHandle──► Opis rozkładu ──[16] SourceBlockHandle──► Rozkład
Rozkład ──[19]/[20] LabelPolyHandle/LabelTextHandle
Rozkład ──RC_AUTOREBAR_SLAB──► obrys płyty
Strefa belki ──RC_BEAM [6]──► obrys płyty (tego rzutu);  Rozkład belki ──RC_AUTOREBAR_BEAM [1]──► strefa belki
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
  prętów bocznych (Real) [20]Ø pręta w nibie górą [21]Ø pręta w nibie dołem (Int16, 0 = brak) — E4b [22]otulina boczna / czołowa
  [23]otulina dolna (Real; decyzja 2026-10-10: 45 / 50). Brak pola = wartość domyślna `BeamParams` (rekord bez [22] / [23] → 45 / 50).
  **v2 (K2 planu `docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md`, [1] = 2):** [24]otulina górna strzemienia (Real; A1 35 / A2 50) [25]tryb prętów
  bocznych (Int16; 1 B1 brak, 2 B2 zawsze, 3 B3 od progu) [26]próg H prętów bocznych (Real, 450) [27]krotność zakładu (Real, 40 —
  zakład = Ceil25(krotność · Ø): H10 400, H12 500, H16 650) [28]rozstaw nominalny w poprzek w opisie (Real, 150) [29]B+T jedną pozycją
  (Int16 0 / 1) [30]obniżenie U-barów drugiego kierunku (Real, 35) [31]pręty proste w kwadrat narożnika (Int16 0 / 1; 0 — przed kwadratem)
  [32]koniec pręta prostego przy U-barze od lica (Real; 0 = auto: otulina + Ø U-bara) [33]strzemiona przy UB 03 +100 (Int16 0 / 1; 0 —
  niezależnie) [34]strzemiona w kwadracie narożnika (Int16 0 / 1; 1 — dłuższa noga) [35]naddatek ramienia U-bara końca (Real, 90).
  Od v2: [15] = Ceil25(krotność · Ø dołem) (tylko dla starej wtyczki), [17] nieużywane (od K6 także w RC_SECTION — A strzemienia z otuliny górnej [24]; zapis bez zmian dla starej wtyczki).
  **Rekord v1** ([1] < 2 — tylko wersje testowe): `BeamStore.Read` → `BeamParams.MigrateV1` W PAMIĘCI (geometria i otuliny bez zmian,
  parametry zbrojenia = nowe domyślne, komunikat z listą zmian); zapis v2 dopiero przy OK w RC_BEAM albo przy generowaniu (ta sama grupa UNDO).
- `RC_AUTOREBAR_BEAM` (rozkład belki): [1]strefa belki (1005). Kopia samych prętów (CloneRemap) → "" — przynależność z testu
  punktu w strefie.
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
- `BarFormatCommands` (Plan C, krok 3f) — `RC_BAR_FORMAT [Polilinia/Blok]`: format NOWYCH prętów (RC_BAR, AutoRebar,
  przebicie), zapis `%APPDATA%\NaSmartRc\barformat.txt` (`BarFormat`; fallback: plik starej wtyczki); **domyślnie Polilinia**
  (rysunki dla starej wtyczki kolegi — zawsze Polilinia). Przy starcie: `[NA Smart RC] Nowe pręty: polilinia|blok`.
  Istniejące pręty bez zmian — rysunek może mieć oba formaty.
- `RebuildBarsCommands` — `RC_REBUILD_BARS` (W4e, P8): przebudowa obrysów wszystkich prętów przestrzeni modelu wg bieżącej
  geometrii (ścieżki MOVE / RC_UPDATE_BAR: `RebuildCompanions`, `SingleBarBlock.Rebuild`; rama i axis[0] z obrysu), jedna
  grupa UNDO; pomija obrys już bieżący, nierozpoznany (lista w linii poleceń) i pręt testowy; grot etykiety (back-link)
  na nowy obrys. Dane, numery, rozkłady — bez zmian.
- `EditCommands` — `RC_EDIT_DISTRIBUTION`, `RC_EDIT_LABEL`, `RC_BAR_END`, `RC_SET_REPR_BAR`,
  `RC_SHOW_ALL_BARS`, `RC_SCALE_ANNOT`. `RC_BAR_END` (poprawka #10, jak e7b600d) — tylko na rozkładzie (symbol końca
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
- `AnnotationEngine` — opisy rozkładów, linia rozkładu, `SyncAnnotation`, `UpdateBarLabelCount`.
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
  pomija zajęte. Seria **501+** (przebicie) nie podbija licznika.
- Jeden numer = jeden kształt pręta. Pręt, któremu zmieniono wymiary, a jego numer ma inny pręt
  o innych wymiarach → dostaje numer istniejącej identycznej pozycji albo nowy (`PositionReconciler`).
  UB i 501+ nie są przenumerowywane.

## 4. AutoRebar — zasady (wymagania użytkownika)
Ogólne:
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
  „{n}x1 H12-nn UB SIDE”, BN dołem (K5) „1 H10-nn B IN NIB”.

Dół (B1/B2, Ø10):
- Zakład 400–650 mm, preferowany **450–550**.

Góra (T1/T2, Ø12, te same parametry co dół poza średnicą):
- Zakład min. 500, preferowany **550–650**, górna granica 700 (przyjęta — do potwierdzenia).
- **Ta sama liczba prętów na danej krawędzi co dół** (liczba prętów w rozkładzie / rozstaw). Liczba odcinków
  wzdłuż pasa (rozkładów, zakładów) góry MOŻE być inna niż dołem — np. 4 pręty zamiast 3, żeby zakłady ominęły pale.
- Zakłady góry ≥ 500 mm od lica pali (SD-Pile); gdy żadna liczba odcinków (min … min+4) nie pozwala —
  zakład najdalej od pali + ostrzeżenie.
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
- **T1/T2 = UB 01/02 — każdy pręt górny ma swój UB** (`PlanNibStepBars`, wspólny dla góry i UB): pas góry bierze
  położenia z krawędzi uskoku na końcu prętów, której zakres najlepiej pasuje do pasa (reguła ± 1 powyżej);
  UB na krawędzi = pręty góry wszystkich pasów dochodzących do niej (długa krawędź przez kilka pasów = suma pasów).
- **UB 01/02 na uskoku**, **UB 03 przy krawędzi zewnętrznej** (liczba jak dół):
  H10, shape 13, 610-70-610, @200 — `RC_GENERUJ_UB_NIB` (przycisk) i w `RC_GENERUJ_SIATKA`.
- **T IN NIB** (z T1/T2): przy każdej krawędzi z nibem 2 pręty H12 co 150 wzdłuż krawędzi — pierwszy w nibie
  (otulina 40 od krawędzi zewnętrznej), drugi 150 dalej, za uskokiem. Długości jak góra (siatka 250, maks. 6000,
  zakłady 500–700), pozycje wspólne z serią 101+. Opis `2 H12-101-150 T IN NIB`.
  Za końcem nibu pręty wchodzą **600 mm w płytę** (liczone od końca nibu), jeśli płyta tam jest (narożnik wklęsły);
  przy narożniku wypukłym pręt zaczyna się na otulinie 40. Jeden pręt: długość siatka 250 **w górę** (lepiej za długi),
  **min. 1500** (twardo 1250), nie dłuższy niż miejsce w płycie. Opis na zewnątrz krawędzi, ramię **o 200 mm dłuższe**
  niż zwykłe; prosto, chyba że po drodze jest płyta albo wzdłuż krawędzi jest bliżej poza płytę — wtedy załamanie
  200 mm za krawędzią i dalej wzdłuż krawędzi.
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
- Pręty do BBS: 501 = H12 L2250 × 14 na detal PH1–3; 502 = H16 L2500 × 14 (PH4–6) / × 28 (PH7–9).
  Poprzednie 501/502 usuwane dopiero po wskazaniu punktów (Esc nic nie kasuje).

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
- W otwartym pliku RC (default): wybór pliku GA (.dwg/.dxf) i płyty z listy (opis „PLOT …” albo „GARAGE n…” na SD-Text
  wewnątrz zamkniętego obrysu SD-PILED-RAFT — `PlotLabelRules.IsPlotLabel`; obrys zewnętrzny = największy zawierający opis).
  Lista: najpierw PLOT, potem GARAGE (domyślnie pierwszy PLOT). Garaż: TITLE_1 / DRAWING_NUMBER z layoutu GA (GA3215 → RC3215);
  garaż bez DRAWING_NUMBER nie ma numeru zapasowego (`GaTitleEngine` szuka „PLOT n” w TITLE_1).
- Kopiowane z obszaru obrys + 1500 mm: SD-PILED-RAFT (obrys, linia uskoku, door threshold, linie stopni),
  koła SD-Pile, teksty SD-Text **bez „NIB TOC=…”** (opisy belek itp. zostają); podpisy pali (SD-Pile Text*)
  **tylko na rzucie górnym**. Wymiary, architektura, poziomy, kanalizacja — nie.
- Opis płyty przebudowany: nr plotu, SSL, grubość („225mm THK SLAB”, bez „ON”); ramka dopasowana (+70 mm).
- Rzut dolny nad tytułem „…BOTTOM LAYER” (środek, 2400 mm nad tytułem), rzut górny 7500 mm obok, tytuł „…TOP LAYER”
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
  prostokąt strefy. Strefy zachodzące wzdłuż prętów i bliżej niż 400 mm w osi rozkładu → jeden rozkład.
- Średnica: As ≤ 2·393 → H10, ≤ 393+565 → **H12**, dalej **H16** (komunikat); > 393+1005 → „zaprojektuj ręcznie”.
- Pozycja: istniejąca prosta o tej średnicy i długości (także z siatki dołem) albo nowa w strefie szablonów dołu.
- Opis „H10-nn-200 B1 ADD” (jak siatka, pręt reprezentatywny), położenie zaokrąglone do 50 mm względem najbliższego pala;
  **wymiary** (SPEEDECK-1-50 RC, SD-PILED-RAFT, XData RC_ADD): pal → koniec prętów i pal → skrajny pręt; bez pala — od krawędzi.
- Ponowne wywołanie zastępuje dozbrojenie tej płyty (rozkłady „… ADD” i wymiary RC_ADD). Siatka B1/B2 nie kasuje ADD.
- **Każdy rozkład z „ADD” w opisie jest cyan** (pręty w bloku kolor 4) — także ręczny opis i „B+T ADD” w detalach.

- **Mapy T1 / T2 → UB ADD** (pręty górne bez zmian): dla każdej strefy mapy najbliższa krawędź z UB tej warstwy
  (T1 → UB 01 na krawędziach pionowych, T2 → UB 02 na poziomych; krawędź = koniec prętów, przy nibie linia uskoku),
  odległość strefy (kontur, inaczej prostokąt) od krawędzi: mapa ≤ 750 i odległość ≤ 400 → UB ADD; mapa > 750 i ≤ 600 → UB ADD;
  dalej — nic. UB ADD = ten sam pręt co UB (ta sama pozycja), ZAWSZE 7 sztuk w połowie między istniejącymi UB, wyśrodkowane na strefie,
  opis „n H12-02-200 UB ADD” (cyan) jak przy prętach prostych: linia rozkładu w płycie na 3/4 ramienia UB od krawędzi
  (opis UB jest w połowie), odnośnik ze środka linii rozkładu wzdłuż prętów za krawędź (600 za płytą), tekst wzdłuż odnośnika; widoczny (reprezentatywny) pręt UB ADD obok środka, nie pod odnośnikiem. Wymaga wygenerowanych UB; po ponownym UB uruchom ADD jeszcze raz.

## 4g. Przekrój płyty (RC_SECTION) — rzeczywiste cięcie
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
  Siatka płyty przy belce = to, co jest w rysunku (od K7 — E2: prostopadłe do linii uskoku, równoległe przed licem belki): pręty siatki proste na swoich poziomach przez belkę (przycięte do betonu),
  schemat kropek siatki także w belce; UB 03 / T IN NIB w pasie albo nibie belki — poziomy względem spodu belki / wierzchu nibu belki;
  T IN NIB w trzonie belki (drugi pręt „150 dalej”, za licem) — poziom jak w płycie (bez przesunięcia do nibu belki i nie pod ramieniem UB 03);
  kropka T IN NIB nachodząca na kropkę pręta belki (BN górą) — odsunięta w poziomie do styku lic (`ResolveTinBeamCollisions`).
  Pręty BN w przekroju: gdy w pasie / nibie belki jest UB 03 — w narożach spinki wewnątrz U (ramię ∓ (Ø spinki / 2 + d / 2), lica w styku);
  bez UB 03 — wzór (dołem otulina dolna belki + 10 + d/2 od spodu, górą wierzch nibu − otulina nibu − 10 − d/2). UB 03 to element płyty
  (otulina dołem płyty 40 + 5 = 45, bez zmian) — gdy jej otulina dołem w belce jest mniejsza niż otulina dolna belki, ostrzeżenie planera
  „UB 03 płyty w nibie belki: otulina dołem 45 < otulina dolna belki 50 …” (BN dołem nadal od UB 03). Rozkład belki jest „użyty” (odcisk) tylko,
  gdy powstała kropka / pręt. Odcisk BEAM: otulina boczna `Cv` i dolna `CvB` (zmiana otuliny → „zmieniona belka …”, rozstrzyga skrót wyniku).
  Wymiary: nib belki (szerokość, uskok H − NibH, wysokość nibu) zamiast nibu płyty, H belki (dalej od krawędzi niż łańcuch), szerokość /
  długość pasa pod belką (`BeamDimBelow` 350); grubość płyty — przy końcu bez belki.
- **K6 (SectionPlanner.Version 2, plan PLAN_BELKI_PRAKTYKA):** pionowe U-bary belki BU — w cięciu poprzecznym kropki na poziomach prętów
  podłużnych przesunięte w poprzek o Ø do środka belki, w cięciu wzdłuż kolumna najbliższa linii cięcia jako kształt A–B–A (tył przy symbolu
  zagięcia rozkładu, ramiona dół / góra); B mniejsze od nominalnego (noga Y narożnika, belka dochodząca T: B − 35) — ramiona zsunięte symetrycznie;
  UB SIDE (BC) na poziomach prętów bocznych (drugie ramię `PairOffset`); A strzemienia (wejście, odcisk) = H − otulina górna [24] − dolna;
  `BeamParamsText` z polami v2. **Opisy w odcinku z belką (wersja 2, przy `BarEndTicks`; płyty bez belek i złote wejścia bez zmian):** kropki
  pręta belki tej samej pozycji obok siebie (odstęp ≤ 400) — jeden numer ze strzałką (strzałka omija miejsca numerów siatki), T IN NIB w nibie belki —
  numer nad swoją kropką nad betonem (nie w rzędzie na zewnątrz), numery kropek siatki mogą przesunąć się w bok ≤ 40. **Siatka przy belce w przekroju (poprawka E2, bez podbicia `Version`):** poziom kropek siatki, którego rozkład nie ma pręta w trzonie
  belki (E2 — równoległe stop przed licem), nie dostaje kropek w pasie belki (trzon + nib belki); poza pasem schemat `AlignMesh`, ale kotwa po stronie belki
  = rzeczywisty pierwszy pręt rozkładu (`MeshLevelBesideBeams`); siatka przechodząca przez belkę i odcinki bez belki — bez zmian. Opisy (wersja 2, belka):
  nie nachodzą na linie pomocnicze wymiarów poziomych, wymiar szerokości / długości pasu pod belką schodzi pod najniższy opis w swoim zakresie (opisy nigdy pod linią wymiaru), opis strzemion w cięciu wzdłuż — ramię najbliższe środka z wolnym miejscem. **Zakończenia prętów (wymaganie użytkownika 2026-10-10, POPRAWKI 31):** pręt prosty w widoku, który kończy się
  w przekroju (`BarLine.End0 / End1` — koniec kawałka = koniec pręta; nie urwanie, koniec cięcia, otwór), ma odgięcie 45° w głąb elementu
  (35 + 35 mm; górny w dół, pozostałe w górę) — jak rysunki biura (Holbrook 528 / 539 / 321, C12 C-C / E-E; konwencja w planie, sekcja K6);
  `Input.BarEndTicks` = true w RC_SECTION / RC_SECTION_UPDATE (domyślnie false — złote wejścia wzorca bit w bit; złoty `beamK6`). Przekroje
  wersji 1 — RC_SECTION_CHECK: aktualny „nowa wersja programu rysowałaby ten przekrój inaczej” (P2); z belką — przy zapisie wersji 1 porównanie BEAM pomija
  „LA” (nowy wzór A strzemienia) i nowe pola „P” (rekord v2), parametry wersji 1 (pierwsze 9 pól „P”), H, nib, otuliny, Ø strzemion — jak dotąd (zmiana → NIEAKTUALNY).
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
Status: **E1 + E3 + E4 + E4b + E5 + E6 zrobione (staging)**; E2 (siatka przy belce) — **K7 (staging)**.
**Praktyka biura (docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md): K1–K8 zrobione (staging, do testów Bartka)** — opis E3–E4 niżej jest
HISTORYCZNY tam, gdzie K2 / K3 go zmieniają (strzemiona, pręty podłużne, zamknięcia końców, numeracja):
- **K2 — strzemiona i rekord v2:** strzemię 51 **H12** co 200, **A = H − otulina górna [24] − otulina dolna** (A1 35 → 450: 365, 375: 290,
  300: 215; A2 50 → 450: 350), B = szer. − 2 · 45 (510), oba w dół do 5 (`BeamGeometry.StirrupHeightCovers`); **pierwsze (i ostatnie)
  strzemię w osi otulina + Ø/2 = 51 od lica** zakresu strzemion — także przy U-barach (`StirrupFirstOffset`; usunięte `StirrupEndOffsetAtUBars`
  / 125); strzemiona **niezależnie od UB 03** (parametr [33]; opcja +100); w kwadracie narożnika L — dłuższa noga ([34]; opcja brak).
  Zakład 40·Ø ([27]). Pręty boczne — tryb [25] / [26] (B3: H ≤ 450 brak; opis i UB SIDE — K4), BN górą 0, BN dołem H10 (B IN NIB — K5).
  Bez ostrzeżenia o otulinie UB 03 (generowanie i RC_SECTION; złoty `beam` — zmieniony tylko o tę linię WARN). Okno `BeamDialog`: wszystkie
  pola v2 (A1 / A2, tryby, krotność zakładu, opcje narożnika). Rekord v1 — migracja w pamięci (sekcja 1).
- **K3 — pręty podłużne i pionowe U-bary** (`BeamGeometry.PlanLayer` + `PlanVerticalUBars`, `AutoRebarEngine.GenerateOneBeam` /
  `BeamVerticalUBar`; WSZYSTKO na rzucie dolnym, seria dolna; stare BT na rzucie górnym usuwane przy ponownym generowaniu):
  - dół = góra (Ø, liczba) i [29] → **BM** jeden rozkład 4 prętów (rysowana jedna warstwa), CountDisplay 8, opis `2x4 H16-nn-150 B+T`;
    inaczej BB i BT (oba w serii dolnej, komunikat); zakres prętów prostych `StraightRange`: koniec z U-barami — lico ∓ (otulina + Ø U-bara)
    = 61 ([32]), narożnik L / belka dochodząca złącza T — lico kwadratu / bliższe lico belki ciągłej (przed kwadratem — C12, domyślne; [31] —
    w kwadrat do lica zewnętrznego ∓ 61), złącze z odcinkiem ręcznym — lico z otuliną 45; plan `PlanBars` z zakładem 650 (528: 5500 + 5250);
  - **pionowe U-bary BU** kształt 21 na każdym końcu z zamknięciem (koniec przy krawędzi i wewnętrzny, każda noga narożnika L, belka
    dochodząca złącza T; belka ciągła złącza T — bez przerwy, bez U-barów w węźle): po jednym w kolumnie prętów (4, osie jak pręty podłużne),
    **B = A strzemienia − 2·Ø strzemienia** (528: 265, 319: 340, 539: 190), noga Y narożnika L i belka dochodząca T — **B − 35** ([30]);
    tył na otulinie 45 od lica (narożnik — lica zewnętrznego narożnika, T — dalszego lica belki ciągłej); **A = C**: koniec —
    Ceil25(max(zakład + 90 [35], tył → pręt prosty + zakład)) = 750, narożnik / T — Ceil25(tył → początek prętów prostych nogi + zakład)
    (539: 1225 / 1300, 319: 1225 / 1325); ramię przycinane do zakresu prętów prostych + komunikat; ramiona z obu końców nachodzą — komunikat;
    opis `4 H16-nn-150 UB`; w rzucie linia ramienia od tyłu U w głąb belki z **kółkiem na zagięciu** (SymbolType Circle, jak UB płyty);
  - pręty L (11) i poziome U-bary E4 **usunięte**; RC_SECTION: BM — kropki na obu poziomach (górne z flagą warstwy górnej), BU i A strzemienia — od K6 (sekcja 4g).
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
  `PlanNibStepBars` (krawędź uskoku w > 50 % przy belce — bez reguły UB 01/02; pręty góry z położeń dołu). Reguły (domyślne, pytanie 11):
  pręty **równoległe** do odcinka belki — dołem od lica wewnętrznego + d (pierwszy 40 + d = 50), górą od lica + (s/2 − 40) (pierwszy s/2 = 100);
  **prostopadłe** dołem — odjęty pas nibu przed belką (pręty prosto do linii uskoku − 40 — „wchodzą do lica zewnętrznego belki”), górą bez zmian;
  na końcach przy krawędzi i w narożnikach L odcięcie do obrysu (nib zawinięty). UB 03 i T IN NIB bez zmian (UB 03 z prętów dołu rzutu dolnego);
  B3 ADD i Add — na obrysie płyty; odcinki „detal ręczny” — siatka jak bez belki. **Płyta bez stref — bit w bit** (odcięcia = null, dawny kod;
  test BRX D526). Obszar nie jest zapamiętywany (bez cache — UNDO). Testy BRX D319 / D528 / D539 (asercje E2, podsumowanie `beam-e2-<płyta>.txt`),
  M528 (belka → Mesh = Mesh bez belki tego przebiegu), S-REG FBEAM. Mesh / B1 / B2 na płycie ze strefami: komunikat „ponów Beam” (B IN NIB).
  `NibDetector.Detect` pomija warstwy RC-BEAM i `SD-Thickening-Zone` (strefa z GA przed Prepare GA), `FindHoles` — RC-BEAM.
- **K5 — B IN NIB:** BN górą domyślnie 0; BN dołem H10 „1 H10-nn B IN NIB” w narożu UB 03 — podział (początki, długości) i numer jak rozkład siatki dolnej
  równoległy do odcinka, najbliższy belce (`MeshRowNearBeam`: ta sama średnica, rząd pokrywa nib ± 300; szablon `FindOrCreateBeamTemplate` = pręt
  siatki); nib częściowy / brak siatki / siatka po belce — `PlanBars` z zakładem 40·Ø = 400 + komunikat. Kolejność: Mesh → Beam.
- **K4 — pręty boczne i UB SIDE:** BS H12 (tryb [25] / [26]; B3 — od 525: 525 → 1, 600 → 2, 675 → 2, 750 → 3 na bok) proste do UB SIDE (lico ∓ (45 + 12)),
  opis „2x{n} H12-nn SIDE” (CountDisplay = 2n); UB SIDE — kod **BC**, kształt 21 poziomo, 1 na poziom na koniec / narożnik L / złącze T
  (`BeamGeometry.PlanSideUBars`): tył na otulinie od lica (narożnik — lica zewnętrznego), B = Floor5(B strzemienia − 2·Ø strzemienia) = 485,
  A = C = Ceil25(tył → początek prętów bocznych + zakład 500) (koniec 525), przycięte do zakresu prętów bocznych; w rzucie ramię przy jednym boku
  z hakiem na zagięciu w stronę drugiego ramienia; opis „{n}x1 H12-nn UB SIDE”.
  Pręty belki (gdy już są) nie są przeszkodą w `ResolveRepresentativeCollisions`; opisy siatki je omijają (zasada „opisy nie nachodzą”).
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
  z D528 tego przebiegu, S528 — B IN NIB = pręt B2 (numer, długość), S539 — BU A–B–A i odgięcia 45°, G528S (H 600: BS / UB SIDE), S319 / S319PDF,
  C17 / C17PDF / C3 (K8 na kopiach GA C12 — folder `RC_BEAM_C12`, domyślnie scratchpad `c12ga`); jednostkowe `RectilinearPolygonsTests`,
  `BeamK4K6Tests` (K4 UB SIDE, K6 odgięcia / BU, K8 PLOT 17 / 3), złoty `beamK6`.

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
