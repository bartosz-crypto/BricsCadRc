# BricsCadRc — architektura

> Dokument wspólny dla użytkownika i agentów. Aktualizuj go, gdy zmieniasz schemat XData,
> dodajesz moduł albo użytkownik ustala nową zasadę.

## 1. Model obiektów na rysunku

| Obiekt | Encja | XData (RegApp) | Rola |
|---|---|---|---|
| Pręt (widok/elewacja) | `Polyline` (obrys pręta) | `RC_SINGLE_BAR` | Definicja pozycji: średnica, kształt, wymiary A–E, Mark `H12-03` |
| Etykieta pręta | `MLeader` | `RC_BAR_LABEL` | Opis pręta, np. `24 H12-03` (liczba = suma z rozkładów) |
| Rozkład | `BlockReference` do BTR `RC_SLAB_BARS_nnn` | `RC_BAR_BLOCK` | Linie prętów w rzucie płyty + symbole końców |
| Opis rozkładu | `BlockReference` (linia rozkładu + tekst) | `RC_BAR_ANNOT` | Linia rozkładu, strzałka, tekst `H12-03-200 B1` |
| Towarzysz pręta (stary format) | encje | `RC_BAR_LINK` | Wskazuje główną polilinię pręta |
| Znacznik płyty (AutoRebar) | na rozkładzie | `RC_AUTOREBAR_SLAB` | Handle obrysu płyty, z którego wygenerowano rozkład |

Powiązania:
```
Pręt ──[13] LabelHandle──► Etykieta ──[1] bar handle──► Pręt        (back-link sprawdzany!)
Rozkład ──[18] SourceBarHandle──► Pręt
Rozkład ──[11] AnnotHandle──► Opis rozkładu ──[16] SourceBlockHandle──► Rozkład
Rozkład ──[19]/[20] LabelPolyHandle/LabelTextHandle
Rozkład ──RC_AUTOREBAR_SLAB──► obrys płyty
```

### Zasady powiązań (obowiązkowe)
- Zapis wyłącznie `XLink.Write(hex)` → DXF **1005** (prawdziwy handle). BricsCAD sam przemapowuje
  1005 przy COPY/MIRROR/ARRAY/PASTE/INSERT, gdy obie strony są kopiowane razem.
- Odczyt wyłącznie `XLink.Read(tv)` — akceptuje 1005 i stary tekst 1000, zwraca „X8”.
- Porównanie wyłącznie `XLink.Same(a, b)` (nie `string.Equals`).
- Przed modyfikacją/usunięciem/przeliczeniem etykiety sprawdź **back-link** (etykieta wskazuje
  na ten pręt). Kopia bez etykiety nie może ruszać etykiety oryginału.
- Stare rysunki (1000): po COPY `BarCopyWatcher.RemapCopiedBarLabels` paruje kopię pręta z kopią
  etykiety (grot etykiety leży na pręcie); `RemapCopiedPairs` robi to samo dla rozkład ↔ opis.

### Schematy XData (indeksy pozycyjne — NIE przestawiać, nowe pola tylko na końcu)
- `RC_SINGLE_BAR`: [1]Mark [2]Diameter [3]ShapeCode [4]A [5]B [6]C [7]LayerCode [8]Position
  [9]D [10]E [11]TotalLength [12]LengthOverridden [13]LabelHandle(1005)
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
- Odczyt musi tolerować krótsze (starsze) rekordy — sprawdzaj `v.Length`.

## 2. Moduły

### `src/App`
- `PluginApp` — inicjalizacja: rejestracja overrule'ów i watcherów, ribbon, komunikat „Build: …”.
- `RibbonBuilder` — wstążka.

### `src/Commands`
- `BarCommands` — `RC_BAR`, `RC_DISTRIBUTION`, `RC_EDIT_BAR`, `RC_UPDATE_BAR`, `RC_FIX_LABEL`, `RC_SCHEDULE`.
- `EditCommands` — `RC_EDIT_DISTRIBUTION`, `RC_EDIT_LABEL`, `RC_BAR_END`, `RC_SET_REPR_BAR`,
  `RC_SHOW_ALL_BARS`, `RC_SCALE_ANNOT`.
- `AutoRebarCommands` — `RC_GENERUJ_B1/B2` (Ø10, strefa szablonów `rebar_bottom`),
  `RC_GENERUJ_T1/T2` (Ø12, `rebar_top`). `AutoRebarUBCommands` — `RC_GENERUJ_UB_B1/B2`.
- `GenerateCommands` (`RC_GENERATE_SLAB`), `PunchingTagCommands`, `CountCommands`, jigi (`AnnotLeaderJig`, `DistributionJig`).

- `MenuCommands` — angielskie komendy wstążki: `RC_GENERATE` (wybór w linii poleceń: Mesh / B1 / B2 / T1 / T2 / UB1 /
  UB2 / UBNib, ostatni wybór domyślny), `RC_PREPARE_GA`, `RC_GA_TEXTS`, `RC_OPENING_DETAIL`, `RC_MODEL_3D`.
  Stare nazwy (RC_GENERUJ_*, RC_PRZYGOTUJ_GA, RC_GA_TEKSTY, RC_DETAL_OTWORU, RC_SIATKA_3D) zostają jako aliasy.

### Wstążka (`RibbonBuilder`)
- Po angielsku (etykiety, podpowiedzi); komunikaty w linii poleceń i dialogi — po polsku.
- Panele: Setup (Prepare GA; GA Texts, Reinf. Maps) · Reinforcement (Generate, Opening Detail, Punching; 3D Model,
  Summary Bars) · Bars (New Bar, Distribution) · Edit (kolumny małych przycisków) · Schedule (BBS; Bar Schedule, Count Bars).
- Ikony: `Resources/Icons/<nazwa>_32.png` i `_16.png` (EmbeddedResource `BricsCadRc.Icons.*`), generowane skryptem
  (proste wektorowe, paleta: pomarańczowy = pręty, niebieski = góra/akcje, szary = beton).

### `src/Core` — silniki
- `SingleBarEngine` — pręt (polilinia), etykieta MLeader, XData pręta.
- `BarBlockEngine` — rozkłady (BTR + BlockReference), XData rozkładu, `GenerateFromBounds`, `UpdateBarLength`.
- `AnnotationEngine` — opisy rozkładów, linia rozkładu, `SyncAnnotation`, `UpdateBarLabelCount`.
- `AutoRebarEngine` — automatyczne zbrojenie płyty (sekcja 4).
- `BarGeometryBuilder`, `ShapeCodeLibrary`, `BarShape` — kształty BS 8666 (promień gięcia 3.5d dla d ≥ 20).
- `BarScheduleEngine`, `BbsCounter` — zestawienie (dialog + CSV). `BbsDrawingReader`, `BbsXlsGenerator`, `BbsModels`,
  `BbsSettings` — BBS .xls na szablonie Speedeck (`RC_BBS`, sekcja 4e).
- `PunchingTagEngine` — tagi przebicia (numery 501+).
- `PositionCounter` — **jedyny** przydział numerów pozycji. `PositionReconciler` — numer po zmianie wymiarów.
- `XLink` — powiązania handle. `NumberParser`, `GeometryHelper`, `LayerManager`, `Log`, `DocumentWatch`.

### `src/Core` — reakcje na edycję (event-driven)
- `DocumentWatch` — rejestruje handlery **per dokument** (każdy otwarty rysunek). `IsUndoCommand`.
- `BarGeometryWatcher` — stretch/move pręta → długość w XData, numer pozycji, rozkłady, etykieta za prętem.
- `BarCopyWatcher` — po COPY/PASTE/MIRROR/ARRAY: unikalne BTR, przepięcie par rozkład↔opis i pręt↔etykieta.
- `AnnotMoveOverrule` — gripy rozkładów/opisów (Grip/Transform/Erase overrule).
- `RcMLeaderOverrule`, `BarBlockHighlightManager` — zachowanie etykiet, podświetlenie obrysu rozkładu.

### Pułapki BricsCAD (znane, sprawdzone)
- **Grip drag:** BricsCAD wywołuje `MoveGripPointsAt` także na prawdziwej encji podczas podglądu →
  w trakcie przeciągania żadnych zapisów do bazy; tylko transient preview, zapis w `CommandEnded`
  (`ApplyPendingGripEdits`). ESC = brak zmian.
- **UNDO/REDO:** w `ObjectModified` pomijaj `IsUndoing`; w `CommandEnded` pomijaj komendy undo
  (`DocumentWatch.IsUndoCommand`). Żadnych cache'y stanu geometrii (po Ctrl+Z są nieaktualne).
- **Zapisy z handlerów:** licznik reentrancji (`_rebuildDepth`), żeby własne zmiany nie wracały jako eventy.
- **GripOverrule na natywnej `Polyline` nie działa w BricsCAD** → pręt nie ma własnych gripów.
  Stretch pojedynczego gripu psuje obrys → jest cofany; długość zmienia STRETCH oknem lub `RC_EDIT_BAR`.
- Pełny refactor (pręt jako BlockReference, „Plan C”) — odłożony.

## 3. Numeracja pozycji
- **UB zawsze 01 (UB B1), 02 (UB B2) i 03 (UB w nibie).** Numeracja automatyczna od **04** (jak ASD;
  03 zarezerwowane także w płytach bez nibu).
- `PositionCounter.NextAuto(db, used)` = max(licznik w rysunku, max użyty < 500) + 1, min. 4,
  pomija zajęte. Seria **501+** (przebicie) nie podbija licznika.
- Jeden numer = jeden kształt pręta. Pręt, któremu zmieniono wymiary, a jego numer ma inny pręt
  o innych wymiarach → dostaje numer istniejącej identycznej pozycji albo nowy (`PositionReconciler`).
  UB i 501+ nie są przenumerowywane.

## 4. AutoRebar — zasady (wymagania użytkownika)
Ogólne:
- Program **sam tworzy pręty-szablony** (strefa szablonów obok płyty: proste od lewej, UB od prawej).
  Nie wymagamy „podkładki” rysowanej ręcznie.
- **Siatka długości 250 mm. NIE dopuszczać prętów co 50 mm.**
- Rozstaw nominalny 200 mm, otulina 40 mm. **Opisy zawsze z rozstawem nominalnym** (np. „-200”),
  nie rzeczywistym po korekcie.
- Pręt min. **2500 mm**, chyba że geometria płyty wymusza krótszy (wtedy min. 1250). Max 6000.
- **Mniej zakładów zdecydowanie lepiej.** Brak mijania zakładów w obrębie jednej warstwy.
- Opisy rozkładów nie mogą na siebie nachodzić (`AvoidLabelCollision`, odstęp 150 mm).
- **Rozkład z jednym prętem: opis bez rozstawu** (np. `1 H12-01 UB`, nie `1 H12-01-200 UB`) — `ApplySingleBarMark`.

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
  tej płyty i tej warstwy.

Nib (uskok przy krawędzi, wys. 150, szer. zwykle 115–135, zawsze < 255 mm) — `NibDetector`:
- Wykrycie: druga zamknięta polilinia wewnątrz obrysu, równoległa do krawędzi w odległości 50–255 mm na jakiejś
  długości (linia uskoku); gdzie nibu nie ma, pokrywa się z obrysem. Można wskazać obrys albo linię uskoku.
- **Dół (B1/B2) do krawędzi zewnętrznej** — pręty dolne mieszczą się w nibie.
- **Góra (T1/T2) do linii uskoku** — pręty górne nie mieszczą się w nibie, więc liczba prętów góry na krawędzi
  wynika z obrysu uskoku (np. nib z obu stron: dół 31 → góra 29).
- **UB 01/02 na uskoku** (liczba jak góra), **UB 03 przy krawędzi zewnętrznej** (liczba jak dół):
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
- Płytę (PLOT) wybiera użytkownik z listy (podświetlona ta z największą liczbą trafień). Bez wskazywania obrysu:
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

## 4d. Przygotowanie rysunku RC z GA (RC_PRZYGOTUJ_GA)
- W otwartym pliku RC (default): wybór pliku GA (.dwg/.dxf) i płyty z listy (opis „PLOT …” na SD-Text wewnątrz
  zamkniętego obrysu SD-PILED-RAFT; obrys zewnętrzny = największy zawierający opis).
- Kopiowane z obszaru obrys + 1500 mm: SD-PILED-RAFT (obrys, linia uskoku, door threshold, linie stopni),
  koła SD-Pile, teksty SD-Text **bez „NIB TOC=…”** (opisy belek itp. zostają); podpisy pali (SD-Pile Text*)
  **tylko na rzucie górnym**. Wymiary, architektura, poziomy, kanalizacja — nie.
- Opis płyty przebudowany: nr plotu, SSL, grubość („225mm THK SLAB”, bez „ON”); ramka dopasowana (+70 mm).
- Rzut dolny nad tytułem „…BOTTOM LAYER” (środek, 2400 mm nad tytułem), rzut górny 7500 mm obok, tytuł „…TOP LAYER”
  pod nim; ramki rebar_bottom / rebar_top 7500 mm po bokach (przesuwane razem z zawartością).
- Otwarte kawałki obrysu/uskoku tworzące pętlę łączone w zamkniętą polilinię (wykrywanie nibu).
- XData RC_GA [PLOT, B|T] — ponowne wywołanie zastępuje płytę.
- Teksty z layoutów GA (`GaTitleEngine` / `GaNotesText`, jak ASD-GAI) — na końcu RC_PRZYGOTUJ_GA (podgląd, „Pomiń”)
  i osobno `RC_GA_TEKSTY`: A1-BL CLIENT_1..3, PROJ_1..3, APPROVED 1:1; TITLE_1 = prefiks GA przed „GENERAL ARRANGEMENT”
  + „REINFORCEMENT DETAILS …”; DRAWING_NUMBER = prefiks GA + RC + numer pierwszego GA (GA0090 → RC0090, RC0091 wg zakładek).
  SLAB NOTES (MText ze „SLAB AREA” na layoutach RC): ogony akapitów AREA / PERIMETER / THICKNESS / CONCRETE VOLUME
  i blok „CONCRETE TO BE DESIGNATED|DESIGNED … CERTIFICATE.” z GA (biały kolor), HYSTOOLS 225 → DK90, 300 → DK165.
  Layout GA wybranej płyty ma pierwszeństwo (TITLE_1 „PLOT …”).

## 4e. BBS (RC_BBS) — jak ASD-BBS w AsdRcSlab, prosto z rysunku
- Źródło: rozkłady RC_BAR_BLOCK **z żywym opisem** (bez opisu nie liczone — ostrzeżenie) → pręt RC_SINGLE_BAR;
  ilość = suma EffectiveCount. Pozycja z Mark (`H12-03` → 3). Dół = pozycje < 100, góra = 101+ (także 501/502).
- Długość cięcia jak w programie (`ShapeCodeLibrary`, zaokrąglenie 25 mm dla giętych; długość nadpisana ma pierwszeństwo).
  Wymiary do kolumn I–M wg nazw parametrów kształtu (A, B, C, D, E/R). Prosty: kod „00”, A = „STR”.
- Ta sama pozycja z różnym prętem → osobne wiersze + ostrzeżenie. Rozkład bez pręta źródłowego: prosty liczony z danych
  rozkładu, gięty pominięty (ostrzeżenie).
- Szablon: wbudowany `Resources/default-bbs.xls` (EmbeddedResource) albo plik użytkownika (zapamiętany w
  %APPDATA%\BricsCadRc\bbs.txt). Strona = wiersze od „BOTTOM LAYER” do „Accessories” (26). Zapis .xls przez **NPOI 2.5.6**.
- Layouty z blokiem A1-BL przypisuje użytkownik (Skip / Bottom / Top / BottomAndTop). Nagłówek: Contract = DRAWING_NUMBER
  przed „-”, adres PROJ_1..3, plot = TITLE_1 przed kropką, rewizja z REV. Wszystkie BottomAndTop → jedna kartka, gdy się mieści.
- Akcesoria tylko na 1. arkuszu: ilość = ROUNDUP(SLAB AREA / 2 × 1,1); HYSTOOLS DK z notatki, inaczej 225 → DK90, 300 → DK165.

## 5. Proces pracy
1. `planista-recenzent`: plan (pliki, podejście, ryzyka, testy w BricsCAD).
2. Użytkownik akceptuje plan (przy większych zmianach).
3. `implementator`: zmiany + `dotnet build` bez błędów (+ `dotnet test`, jeśli dotyczy geometrii).
4. `planista-recenzent`: recenzja diffu wg tego dokumentu → lista poprawek albo „AKCEPTUJĘ”.
5. Pętla poprawek (max 3 rundy), potem instrukcja testów dla użytkownika.
6. Użytkownik testuje w BricsCAD → commit robi sam.

## 6. Otwarte tematy
- Zestawienie: rozkłady bez opisu nie są liczone.
- Rozkłady ze starych rysunków (1000) skopiowane razem z prętem wskazują na oryginalny pręt.
- Kształt 44 (pierścień) — odtwarzanie osi z obrysu; kąt 30° w `BarGeometryBuilder` do weryfikacji z BS 8666;
  brak ostrzeżenia dla B/2 < min. promień gięcia.
- „Plan C”: pręt jako BlockReference z własnymi gripami (na razie wariant B: grip na końcu obrysu polilinii
  zmienia odcinek końcowy — `BarGeometryWatcher.TryEndStretch`; ruch w środku pręta jest cofany).
