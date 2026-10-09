# Praca nocna 2026-10-08/09 — podsumowanie i decyzje na rano

> Plik aktualizowany w trakcie nocy. Na końcu: wyniki testów i lista testów ręcznych.

## Zasady nocy
- Nic nie commitowane. Build tylko do `bin-staging`; do `bin` trafia wyłącznie wersja z kompletem testów PASS
  (i tylko gdy BricsCAD jest zamknięty); poprzednie wersje jako `bin\*.bak`.
- Tryb „blok” (`RC_BAR_FORMAT`) zostaje zablokowany do Twoich testów ręcznych etapu 1.
- Przy wątpliwościach wybierany jest wariant bezpieczniejszy — każda taka decyzja jest wpisana niżej.

## Decyzje do potwierdzenia rano

### D1. BS 8666 — kształty (W4) — NIE wprowadzone w nocy, czeka na Ciebie
Pełna analiza: `docs/BS8666_ANALIZA.md`. Najważniejsze:
- **Formuły długości** w kodzie (i w firmowym arkuszu BARSCHED!) są częściowo z BS 8666:**2005**:
  kształty 14, 22 (arkusz), 29, 47, 51/63 (wariant minimalny), 56, 64, 75. Zmiana na 2020 **zmienia BBS**.
  → Pytanie: liczymy wg 2020 (norma) czy wg arkusza firmowego (zgodność z dotychczasowymi BBS)?
- **Wymiary zewnętrzne (kl. 7.1):** wtyczka traktuje A–E jako oś pręta → narysowany pręt „wystaje” o d/2,
  łuki o d/2 za ciasne. Poprawka zmienia wygląd WSZYSTKICH prętów giętych (nie BBS). → Wprowadzać?
- **Kształt 44:** w kodzie to pierścień, w normie „kapelusz” (pierścień = 75). → Zmienić?
- Geometria ok. połowy kształtów niezgodna z rysunkami normy (lista w analizie) — do przepisania etapami.
- Moja rekomendacja: (1) formuły wg 2020 + przekazać autorowi arkusza listę różnic, (2) wymiary zewnętrzne — tak,
  osobnym krokiem z testami, (3) 44 → zgodnie z normą, pierścień jako 75, (4) geometria — kolejno, zaczynając od
  najczęściej używanych (11, 21, 13, 12, 22).
- **DECYZJE UŻYTKOWNIKA (2026-10-09, przed snem):** (1) formuły wg **BS 8666:2020**; (2) wymiary zewnętrzne — **tak,
  osobnym krokiem**; (3) **44 = kapelusz, pierścień = 75** (stare 44-pierścienie rozpoznawane); (4) W1/W2 — wszystkie
  domyślne zaakceptowane, błąd #12 bez poprawki.

### D2. Plan W1/W2 — przyjęte domyślne (docs/PLAN_POPRAWKI_W1_W2.md, sekcja 1)
Nadpisana długość: bez zmian + ostrzeżenie · pierścień: środek zostaje · rozkład↔opis też z mapy · rozkład wklejony do
innego rysunku bez pręta: [18] czyszczone · przepinanie po każdej komendzie klonującej · RC_BAR_LINK z mapy ·
ERASE/EXPLODE pręta bez etykiety usuwa rozkłady · zepsuty obrys: kierunek sprzed komendy · #12 bez poprawki.

### D3. W1b — pręt w kolejce watchera z wcześniejszej części tej samej komendy (plan awaryjny z 3.1)
Sam `Suppress()` nie wystarczył: gdy pręt został utworzony / opisany w tej samej komendzie co RC_EDIT_BAR
(test E3 Z0), watcher miał go już w kolejce z obrysem 3000 i w CommandEnded nadal doliczał przyrost (4000).
Wdrożono plan awaryjny z planu: `BarGeometryWatcher.IgnoreThisCommand(id)` — wołane przez `BarEditApply.Apply`
i RC_UPDATE_BAR (polilinia); pręt pomijany w CommandEnded tej komendy. Grip/STRETCH użytkownika bez zmian.

### D4. W1-0 — zrzut przypadków: komendy i przypadki tylko-polilinia
`RC_REG_FIX_CHECK` wykonuje edycje w kodzie (RC_EDIT_BAR jak REC.bar3, ERASE, zapis XData ścieżki 00) — nazwa z planu,
treść = „krok edycji”. Przypadki tylko-polilinia (F1e, F4d, F7a–c) są tworzone także w przebiegu Block (komendy skryptu
muszą mieć co zaznaczyć), ale ich sekcje są wycinane z porównania Block. F7c: prawdziwe RC_UPDATE_BAR w skrypcie
(wybór punktem po ZOOM) — w obu wersjach ten sam kod komendy.

### D5. W1-0 — expected-diffs pręta-bloku od razu w W1-0
Pręt-blok już przed W1 nie miał błędów #1, #4, #5 (etap 3), więc różnice bar-equiv F1b, F4a–c, F5a–b i REC.bar3 są
wpisane w `expected-diffs/block/01, 04, 05` już w W1-0 (każda linia sprawdzona: wymiary/wierzchołki/pierścienie tych
przypadków; F4c „leader 0/0” — grot etykiety kopii przyciągnięty przez RcMLeaderOverrule do obrysu, który we wzorcu
jest nieodbity). W W1e plik block/05 zaktualizowany: pręt-blok zachowuje środek pierścienia (0-POPRAWKI p.2).

### D6. W1d/W1e — szczegóły ramy
- Zmiana kształtu z pierścienia (44 → inny): przebudowa „dawną ścieżką” (axis[0] = środek v0 i v_ostatni obrysu
  pierścienia), ale w ramie odczytanej z pierścienia (pierścień obrócony → nowy pręt obrócony; nieobrócony → jak wzorzec).
- Pręt-blok 00 → 44: bez przesunięcia (pierścień od axis[0] starego pręta, jak polilinia); 44 → 00: punkt wstawienia bez
  zmian (polilinia liczy axis[0] z obrysu pierścienia — drobna różnica między formatami tylko przy 44 → inny kształt).
- `TryFitRing` wymaga ≥ 8 wierzchołków (prostokąt 00 też ma równe odległości wierzchołków od środka).
- Brak dopasowania obrysu i obrysu sprzed komendy → kierunek z cięciwy osi (bez progu — wg planu).

### D7. Testy W1 — dodatkowy przypadek 1f
Do testów W1 dopisano 1f (pręt + etykieta + rozkład utworzone i edytowane w jednej komendzie) — odtwarza E3 Z0 (D3).
Skrypt testu 4g podaje przesunięcie STRETCH z pełną precyzją (433.01270189222,250); w reg.scr (F4d) zostało 433.0127
(wynik 3499,999998 w obu wersjach, w zrzucie 3500.00 — bez wpływu na porównanie).

### D-W4. Plan W4 (BS 8666) — przyjęte rekomendacje P1–P14 (docs/PLAN_W4_BS8666.md, „Decyzje”) — do potwierdzenia
Najważniejsze, bo zmieniają wyniki: **P6** BBS starych rysunków liczony wg 2020 (z ostrzeżeniem przy każdej rozbieżności z zapisaną
długością); **P7** pręt prosty 00 zaokrąglany w górę do 25 mm w BBS (kl. 7.2; A bez zmian); **P9** kształt 51: A = wysokość
(zmienia proporcje rysunku starych 51, BBS bez zmian); **P4/P5** 64 i 47 bez wymiaru → brak długości + ostrzeżenie.
Rozpoznanie starego 44-pierścienia: znacznik w danych pręta (P1) — stare rysunki z „44” pokażą się jako pierścień 75.
Plan po recenzji ChatGPT: 3 blokery rozwiązane w 0-POPRAWKI (rozkłady Manual bez zmiany długości po W4e, rozpoznanie
geometrii pośrednich, haki 51/63 spójne z formułą).

### D8. W2 — kopia cofnięta „_U” w trakcie COPY usuwała opisy ORYGINAŁÓW (wynik spike'a W2-0)
COPY wielokrotne (`_O _M`) i `_U` w COPY: BricsCAD usuwa klony 2. kopii jeszcze w trakcie komendy, a `BarBlockEraseOverrule`
szedł po nieprzepiętym `[11]` klonu rozkładu do opisu oryginału (błąd istniał też we wzorcu). Od W2 kaskada usuwania
rozkładu-klonu w trakcie komendy tłumaczy `[11]` przez paczkę klonowania (`CloneMap.TryResolvePending`) — opis spoza
klonowania zostaje. Test W2-16.

### D9. W2 — UNDO po COPY usuwało opis oryginału; REDO nie odtwarza przepięcia (ograniczenie BricsCAD) — DO DECYZJI
- `_.UNDO 1` po COPY: przy cofaniu klon rozkładu ma znów stare `[11]` (→ opis oryginału), a `BarBlockEraseOverrule` usuwał
  ten opis. Wprowadzono strażnik `IsUndoing` (usunięcie przez UNDO — bez kaskady rozkład → opis; powiązane obiekty odtwarza
  samo UNDO). Test W2-11U.
- `_.REDO` po COPY odtwarza kopie BEZ zmian z CommandEnded (przepięcia z mapy i — jak we wzorcu — unikalnych BTR): kopia
  rozkładu znów wskazuje oryginał i dzieli BTR. Zmiany z CommandEnded na obiektach już istniejących (3e2 SCALE) są
  ponawiane — na NOWYCH obiektach nie. Wzorzec ma to samo (jego przepinanie też jest w CommandEnded). Poprawka wymagałaby
  zapisów w trakcie COPY (np. przy DeepCloneEnded) — sprzeczne z zasadą „handler mapy bez zapisów”. → **Pytanie: zostawić
  (rzadki przypadek, REDO po COPY) czy szukać rozwiązania?** Test W2-11R — obserwacja (bez asercji na kopiach).

### D10. W2 (3b) — `-ARRAY` na białej liście komend kopiowania
Wzorzec sprawdzał `cmd.StartsWith("ARRAY")`, a skryptowe/klawiaturowe `-ARRAY` ma nazwę `-ARRAY` — po nim nie było ani
przepięcia, ani unikalnych BTR (pręty-bloki: `# BARBLOCK FAIL refs=3` w S-REG Block). Teraz `TrimStart('-')`. Różnice
F3d w expected-diffs `03b` (pary z mapy, `*U`).

### D11. W2 — wklejenie ze schowka w tym samym rysunku = COPY; inny rysunek / plik — powiązania spoza wstawianych obiektów puste
BricsCAD w schowku ZERUJE odwołania do obiektów spoza schowka (`[18]=0` rozkładu wklejonego bez pręta). Dla wklejenia do
rysunku, z którego skopiowano (COPYCLIP/CUTCLIP/COPYBASE → mapa schowka), wartości „bez zmian” brane są z obiektu źródłowego
(kontrola: ta sama aplikacja XData i ta sama Mark [1]) — `[18]` → oryginalny pręt (pyt. 4). Rysunek źródłowy zapamiętany
wskaźnikiem bazy natywnej (WeakReference do wrappera z IdMapping znikał przy GC — test 8b „migał”), zerowany przy zamknięciu
rysunku. Zwykły `-WBLOCK` mapy schowka nie nadpisuje (0-POPRAWKI p.3, test W2-8b). Wartości już puste (0) nie są przepisywane
(brak zbędnych różnic). Klony w definicjach bloków (PASTEBLOCK, 1. faza `-INSERT *`) — pomijane.

### D12. W2 — testy „dwa rysunki” i ESC tylko ręcznie
Skrypt BricsCAD nie przełącza rysunków ani nie wysyła ESC. Automatycznie: `-INSERT *` pliku (inny rysunek, 8c/8d).
Ctrl+C w A → Ctrl+V w B oraz ESC w trakcie COPY — w testach ręcznych niżej.

### D13. W2 — S-REG: identyfikatory błędów z literą, podział F5c
`run-regression.ps1` przyjmuje `# bug: 3b` (wcześniej tylko cyfry); `3b` dopisane do POPRAWKI_WZORCA. F5c (COPY 44 + edycja
oryginału) przypisane linia po linii: pierścień kopii (RC_BAR_LINK z mapy) → `03`, przebudowa oryginału → `05c` (#5); linia
`entities=2 → 4` wynika z obu — w `03` (opisane w nagłówku pliku). Nowe przypadki: F3a–F3f, F6, F5c (pasy 15–22).
F3b (COPY samego rozkładu), F3c (MIRROR Y — BricsCAD V25 odbija w miejscu), F3e (-WBLOCK/-INSERT) — bez różnic (kontrola).
Geometria opisu po MIRROR N (F3f) identyczna z wzorcem (normalizacja i przebudowa wspólna — 0-POPRAWKI p.1).

### D14. W2 — testy W2 w klasie częściowej W1
`PlanCFixW2Tests.cs` korzysta z helperów `PlanCFixW1Tests` (partial); plik wyników, prefiks i lista kroków przełączane w
`RCSMOKE_W2_SETUP` (W1 bez zmian działania). Spike: `PlanCFixW2Spike.cs` + `run-planc-w2spike.ps1` (zostaje jako narzędzie).

### D15. W2 — runda poprawek 1 (recenzja ChatGPT, wszystkie 5 uwag trafne)
1. Stare odwołania 1000 przy wstawianiu z innej bazy: pole czytane z kodem DXF; tekst tłumaczony przez klucze korzenia
   łańcucha (plik) albo mapę schowka (oryginał → plik schowka), wynik zapisywany jako 1005 (test W2-22).
2. Zagnieżdżone COPY / MIRROR (komenda wywołująca je przez Editor.Command / LISP): kwalifikacja „komenda kopiowania” przechodzi
   na komendę nadrzędną — unikalne BTR i normalizacja par po jej zakończeniu (test W2-17).
3. Dwa COPY w jednej komendzie (A→B, B→C): wszystkie żywe klony w przestrzeniach są przepinane, powiązania przez pełny łańcuch
   paczek (obiekty pomocnicze PASTECLIP odpadają, bo leżą w bloku pomocniczym) (test W2-19).
4. Strażnik kaskady usuwania pręta chroni rozkład tylko, gdy po przepięciu wskaże INNY żywy pręt (wklejony komplet usunięty
   w tej samej komendzie — rozkład i opis usuwane) (test W2-20).
5. Mapa schowka tylko dla paczki komendy wklejania, której klucze par głównych (encje) są w mapie schowka; INSERT nigdy
   (test W2-21). Odcisk rysunku źródłowego: wskaźnik bazy + nazwa pliku + data utworzenia, czyszczony przy zamknięciu rysunku
   (DocumentToBeDestroyed). Plik schowka po odczycie ma dodatkowe definicje bloków — dlatego porównywane są tylko pary główne.
Testy W2-17…22 nie były uruchamiane na kodzie sprzed poprawki (oczekiwane FAIL wynika z analizy).

### D16. W2 — runda poprawek 2 (obie uwagi trafne)
1. Zamknięcie rysunku źródłowego schowka nie kasuje map schowka — unieważnia tylko „żywe źródło” (Ctrl+V do innego rysunku
   po zamknięciu źródła nadal tłumaczy stare odwołania 1000). Test W2-24 (zamknięcie symulowane wywołaniem handlera).
2. Tożsamość rysunku źródłowego = Guid nadawany dokumentowi przy podpięciu (usuwany przy zamknięciu) — SAVEAS między COPYCLIP
   a PASTECLIP nie psuje „ten sam rysunek”. Test W2-23 (SaveAs przez API; na starym kodzie test dawał FAIL).
3. Przy okazji (test W2-24): kod wtyczki w trakcie wklejania przepisuje rekord opisu (WriteAnnotXData), zamieniając stary tekst 1000
   na 1005 ze STARYM handle — dla wstawiania z innej bazy taka nieprzetłumaczona wartość jest rozwiązywana jak tekst.

### DECYZJE UŻYTKOWNIKA — rano 2026-10-09
- **Pytanie 2 (kąt 14/15 bez D): kąt z wymiaru B** (θ = asin(B/A); z B i D, gdy D podane) **ORAZ alternatywnie wpisany kąt**
  — opcjonalne pole „Kąt [°]” w dialogu pręta: po wpisaniu program liczy B = A·sin θ i D = A·cos θ (zaokrąglone do 5 mm),
  zapis jak dotąd (bez zmiany schematu XData). Brak B, D i kąta → 45° + ostrzeżenie. Dotyczy kształtów z ramieniem skośnym
  (14, 15, 24, 46). Do zrobienia po W4e (krok W4h).
- Pytanie 1 (ostrzeżenia BBS): bez odpowiedzi — zostaje domyślne (1A: tylko zmienione formuły).
- **Pytanie 3 (REDO po COPY): do ustalenia później**, razem z kolegą — dopisane w dokumencie dla niego (punkt 17,
  „do wspólnego ustalenia”, 3 możliwe kierunki). Do tego czasu bez zmian.
- Zaokrąglanie długości: norma (rys. 1, s. 6) — wymiary co 5 mm, długość pręta co 25 mm. Pytanie o opcję 5 mm — czeka.

- **W4h — grip wolnego końca A (14/15 z B bez D): wydłuża A przy STAŁYM kącie** — program zapisuje D (rzut) tak, by kąt
  się nie zmienił; koniec C jak dotąd. **Stare 14/15: przeliczyć wg normy** (kąt asin(B/A) po pierwszej edycji/MOVE —
  świadoma zmiana wyglądu). Drobne z recenzji do zrobienia razem: ostrzeżenia kąta (90° wpisany, rzut poziomy ≥ ramię → 0°),
  sygnalizacja kąta spoza 0–90° w dialogu, doprecyzowanie testu 9 (B wymagane — 45° tylko dla starych prętów bez B/D).
- **Krok 3f — TAK (2026-10-09 rano):** odblokować pręty-bloki komendą `RC_BAR_FORMAT [Polilinia/Blok]`, domyślnie
  **Polilinia**; gripy pręta-bloku 00/99 z przeliczaniem numeru, rozkładu, opisu i BBS (BarChangePropagator). Po W4e/W4h.

### DLL w `bin` (stan na rano)
- 2026-10-09 ~03:40: `bin\NaSmartRc.dll` = **etapy 1–3e2 + W1 + W2** (wszystkie testy PASS, S-REG = expected-diffs).
  Poprzednie: `bin\NaSmartRc.etap3e.dll.bak` (3a–3e2), `bin\NaSmartRc.etap3c.dll.bak`, `bin\NaSmartRc.etap2.dll.bak`.
  (Jeśli rano jest nowsza podmiana — opisana niżej.)
- 2026-10-09 ~05:00: `bin\NaSmartRc.dll` = **+ W4a–c** (formuły BS 8666:2020, łuki styczne, 44 = kapelusz / 75 = pierścień,
  rozkład Manual liczony jak wzorzec; recenzja ChatGPT: AKCEPTUJĘ; `dotnet test` 456 PASS / 0 FAIL / 8 Ignore). Poprzednia: `bin\NaSmartRc.W2.dll.bak`.
  **Uwaga:** od tej wersji BBS liczy długości wg 2020 (kształty 14, 29, 31, 47, 51/63 min., 56, 64, 75) — ostrzeżenie przy
  rozbieżności z zapisaną długością (D17: tylko dla kształtów ze zmienioną formułą). Stary pierścień „44” pokazuje się jako 75.
- 2026-10-09 ~06:40: `bin\NaSmartRc.dll` = **+ W4d + W4g** (geometria 12, 14, 15, 22, 24, 33, 46, 51 wg rysunków normy;
  pręt prosty 00 zaokrąglany w BBS do 25 mm z ostrzeżeniem; znacznik geometrii [15]=2021; `dotnet test` 548 / 0;
  testy BricsCAD: W4 97, W1 60, W2 100, E1–E3 bez FAIL; S-REG = expected-diffs). Poprzednia: `bin\NaSmartRc.W4c.dll.bak`.
  (Incydent D33: wersja W4a–c w `bin` była w nocy odtworzona z tego samego kodu — już nieistotne, zastąpiona W4d.)
- 2026-10-09 ~05:30: **W4d + W4g tylko w `bin-staging\NaSmartRc.dll`** (komplet testów PASS, `bin` NIE podmieniony — decyzja
  o podmianie po przeglądzie). `bin\NaSmartRc.dll` = W4a–c (odtworzony — patrz D33).
- 2026-10-09 ~07:15: **W4e + W4f (+ RC_REBUILD_BARS) tylko w `bin-staging\NaSmartRc.dll`** (komplet testów PASS); `bin\NaSmartRc.dll`
  NIETKNIĘTY (= W4d + W4g, MD5 przed i po pracy identyczne) — podmiana po Twojej decyzji (D38).
- 2026-10-09 ~08:00: **+ krok 3f (RC_BAR_FORMAT, gripy pręta-bloku z propagacją) tylko w `bin-staging\NaSmartRc.dll`**
  (W4e–W4h + 3f; komplet testów PASS — D44). `bin\NaSmartRc.dll` NIETKNIĘTY (MD5 przed i po identyczne).

### D17. W4b — bezpiecznik BBS tylko dla kształtów ze zmienioną formułą — DO POTWIERDZENIA
Plan (P6): ostrzeżenie „długość wg BS 8666:2020 = …, w rysunku …” przy każdej rozbieżności z zapisaną długością. W S-REG
dawało to nowe linie (BLOKER): UB z AutoRebar mają w XData TL = A (700 przy formule 1500 — cecha wzorca, nie skutek W4).
Wdrożono: ostrzeżenie tylko dla kształtów, których formuła zmieniła się w W4b (`BarShape.FormulaChangedIn2020`: 14, 29, 31,
47, 51, 56, 63, 64, 75 oraz każdy gięty H25 — r 87,5 → 87). Stary pierścień 44 ma osobne ostrzeżenie (W4c).
→ **Pytanie: wystarczy, czy ostrzegać przy każdej rozbieżności (wtedy UB z AutoRebar zawsze z ostrzeżeniem i nowa poprawka
w expected-diffs)?**

### D18. W4b — kształt 63: długość lewego haka (błąd geometrii, poprawiony)
Lewy hak 63 zaczynał się w (0, hak) — miał długość A − hak (prawy: hak), więc długość osi w ogóle nie zależała od C i wymóg
0-POPRAWKI p.3 (geometria spójna z formułą) był niespełnialny. Teraz start (0, A − hak): oba haki długości C (albo C_eff).
Stare obrysy 63 rozpoznaje wariant Legacy (rama zachowana). Testy Code63_* poprawione.

### D19. W4b — test „|oś obrysu − formuła| ≤ 0,5d + 1” dla 51/63 możliwy dopiero w W4e
Geometria traktuje wymiary jako oś, formuła — jako wymiary zewnętrzne (−5d, −2,5r); różnica 51 (d12) ≈ 68 mm jest stała.
Zamiast tolerancji: test, że hak przy C = 0 = hak przy C = C_eff (z równości formuł) i że różnica oś ↔ formuła jest TAKA SAMA
dla C = 0 i C > 0 (`BarShapeW4bTests.CEff_*`). Właściwy test tolerancji — po W4e.

### D20. W4a-0 — semantyka `BarGeometryOptions`
Wszystkie flagi false = Legacy = `LegacyBarGeometry` (zamrożona kopia sprzed W4, także r = 87,5 dla H25, haki 51/63 sprzed
2020, 44 = okrąg, 75 = helisa). Każda inna kombinacja = geometria bieżąca: parametry po nazwie, odcinki zerowe, Table 2,
haki C_eff; flaga `TangentArcs` wybiera łuk styczny, `Bs2020Sharp` (W4d) i `ExternalDims` (W4e) są zarezerwowane — dziś bez
efektu. `FitCandidates` = 8 kombinacji: bieżąca, pozostałe, Legacy na końcu (W4d/W4e tylko dopiszą efekt flag).

### D21. W4a-3 / W4b — skutki parametrów po nazwie
25 {A,B,E}: ostatnie ramię 45° = E (dawniej wartość z pola C, a D = 0 dawało NaN — stare 25 i tak były uszkodzone).
33 {A,B,C}: wartość w polu D (spoza kształtu) ignorowana. 31 dostało D, 47 — C (dopisane na końcu `Parameters`); stary 47 bez
C = „brak długości” (P5), stary 31 bez D — bez NaN (odcinek zerowy). 64 zawsze „brak długości” (F poza rekordem — P4,
tylko nadpisanie długości).

### D22. W4b — RC_SCHEDULE / CSV: brak długości jako `CuttingLength = NaN`
Bez nowej właściwości w `BarScheduleEntry` (zrzut S-REG wypisuje wszystkie właściwości — nowa dawała różnice w każdym
wierszu). Wiersz „brak długości”, masa 0, ostrzeżenie `[RC_SCHEDULE] UWAGA: …` w linii poleceń.

### D23. W4c — przypisanie różnic S-REG (expected-diffs 08)
Czyste różnice #8 (pola XData [3] „44” → „75” i [11] TL 225 → 950, 325 → 1250) — nowy plik `08-ksztalt-75-pierscien.txt`
(polyline i block). Wiersze „bar” F5a / F5b / F5c (shape i TL) są łączne z #5 / #3 — zostały w 05, 05c, 03 z nowym tekstem
(opisane w nagłówkach), jak D13. Przypadki F5 tworzą pierścień kodem `RegFixCommands.RingShapeCode` (refleksja:
`BarFrameRules.RingShapeCode`, wzorzec → „44”); testy dymne — `W4Compat.RingCode` / `IsRing`.

### D24. W4b — H25: promień 87 (Table 2) zmienia minimalnie geometrię giętych H25
Stare obrysy H25 rozpoznaje Legacy (zamrożone 87,5). AutoRebar nie używa H25 — S-REG bez zmian.

### D25. W4c — stary pierścień jako pręt-blok
Rekord bloku „44” z [14] = 2, bez [15] → pierścień 75 (test C4). Kopia / ARRAY kapelusza przenosi [14] i [15] (XData kopiowane
przez BricsCAD; `CloneRemap` zapisuje przez `BarEntity.Write`, które zachowuje i uzupełnia znacznik).

### D26. W4 — runda poprawek 1 (recenzja ChatGPT, 4 uwagi trafne)
1. Rozkład Manual (RC_DISTRIBUTION / RC_EDIT_DISTRIBUTION): po W4a łuk styczny zmieniał długość prostych odcinków przy kątach
   ≠ 90° (15 H12: ramię 142 zamiast 190). Teraz (0-POPRAWKI p.1) odcinek wybierany na bieżącym obrysie, długość z obrysu sprzed
   W4 dla TEGO SAMEGO odcinka — mapowanie po etykiecie (ramię k ostrych punktów / łuk w punkcie c, pododcinek m), bo liczba
   wierzchołków wariantów się różni; polilinia i pręt-blok; kąty 90° — bez zmian (bit w bit).
2. Wariant etapu W4a: nowa flaga `TableRadius` (promień Table 2 i haki 51/63 wg 2020); W4a = łuk styczny z r 87,5 dla H25
   i hakami sprzed W4b. Test na zamrożonym obrysie W4a (współrzędne policzone niezależnie).
3. RC_EDIT_BAR starego 47 bez C: przy zaznaczonym nadpisaniu długości brak C dozwolony; bez nadpisania — komunikat o braku długości.
4. RC_SCHEDULE / CSV: wymiary tylko z definicji kształtu (pierścień 75 z „44” bez C–E), XData bez zmian.
Wyniki: `dotnet test` 456 / 0 / 8; `run-planc-w4` 33/0; W1 60, W2 100, E1 376 / 1 SKIP, E2a 41, E2b 22, E2d 43, E3 196 — FAIL=0;
S-REG PASS (bez zmian expected-diffs).

### D27. W4d — kształty 14 / 15: kolejność punktów i kąt — DO POTWIERDZENIA
Oś zaczyna się w wolnym końcu ramienia A (jak rysunek normy: A z lewej u góry w dół do zagięcia), potem C — 15 dalej
w prawo, 14 wstecz pod A. Kąt θ = atan2(B, D) (B — rzut pionowy, D — rzut poziomy A; D dopisane); **brak B albo D → 45°**
(plan: „θ = atan2(B,D) lub 45°”) — B bez D NIE wyznacza kąta (np. asin(B/A)), żeby stare pręty (D = 0) miały jeden, przewidywalny
kąt. Stare pręty 15 / 14 (zawsze z C, bo dialog wymagał C > 0) po MOVE / RC_EDIT_BAR rysują się wg normy: A skośne 45°, C poziome
(dawniej A poziome, B skośne). axis[0] zostaje (P8) — nowy pręt zaczyna się tam, gdzie zaczynał się stary.
→ **Pytanie: czy przy braku D liczyć kąt z B (asin(B/A)), skoro B to w normie rzut pionowy A?**

### D28. W4d — 24: dopisane D i E, 33: „bieżnia” 1½ obwodu
- 24 {A,B,C} → {A,B,C,D,E}: plan każe brać kąt z D/E, więc oba wymiary dopisane (opcjonalne, brak → 30°). 24 nie ma w pickerze.
- 33 wg rysunku i formuły 2A + 1,7B + 2(C) − 4d: pręt okrąża bieżnię 1½ raza (2 proste górą i dołem + 3 półkola = 2A + 1,71B + 2C
  przy osi), oba końce w odległości C od prawej skrajni (górny i dolny „karb” na rysunku normy). Odcinki i półkole prawe
  pokrywają się (kształt „zachodzący”).
- 46: D (wysokość skosu) dopisane ZA E → {A,B,C,E,D}; kąt asin(D/B), brak albo D > B → 45°. 22: półkole o średnicy C (r = C/2),
  D od wolnego końca do skrajni półkola; `AutoRebarEngine.Section.AxisParams` 22 → A − d/2, B − d/2, C − d, D − d/2.

### D29. W4d — rozkład Manual dla kształtów o zmienionym układzie ramion — ROZSTRZYGNIĘTE (runda poprawek 1)
Pierwsza wersja W4d brała długość z ramienia sprzed W4 o tym samym numerze — przy zmienionym układzie ramion to inny odcinek
(33 {900,200,300}: dolna prosta 700 → 152; 46: 287 → 136). **Reguła (recenzja, przyjęta):** (a) obrys historyczny (sprzed W4,
W4a, W4b–W4c) albo kształt bez zmiany układu ramion (00, 11, 12, 13, 21 …) → geometria sprzed W4 (wzorzec, bit w bit);
(b) obrys W4d (także po W4e — punkt odniesienia: geometria W4d bez wymiarów zewnętrznych) kształtu 14, 15, 22, 24, 33, 46, 51
(`BarFrameRules.ArmLayoutChangedInW4d`) → wskazany odcinek W4d. Istniejące rozkłady nie są przepisywane; reguła działa przy
wskazaniu ramienia (RC_DISTRIBUTION, RC_EDIT_DISTRIBUTION — wspólne `BarCommands.ManualSegmentLength`). 12 (zmienia się tylko
promień R) — reguła (a). **Runda 2:** o gałęzi (b) decyduje jawny znacznik w danych pręta (D34), nie samo dopasowanie obrysu.

### D34. W4d runda 2 — znacznik geometrii W4d w XData ([15] = 2021)
Problem (recenzja): obrys etapu W4b–W4c bywa identyczny z W4d (51 H12 A = B = 400, C = 0) — dopasowanie wybierało W4d i stary
pręt dostawał rozkład Manual haka 132 zamiast 168 (wzorzec). Rozwiązanie:
- `RC_SINGLE_BAR[15]` ShapeStd = Int16 **2021** = „obrys zbudowany geometrią W4d (norma 2020)”; 2020 (kapelusz W4c) bez zmian
  znaczenia; „44” z [15] ≥ 2020 = kapelusz. Polilinia bez [14] dostaje [14] = 1 (jak W4c). Zapis tylko przez `BarXData`
  (`MarkW4dGeometry`, `Write` nie obniża znacznika — `BarRecordRules.WithShapeStdAtLeast`), bez dublowania pól; odczyt
  toleruje krótsze rekordy (`BarRecordRules.IsW4dGeometry`, `BarData.GeometryW4d`).
- Znacznik dostaje pręt UTWORZONY przez wtyczkę W4d+ (PlaceBar, pręt-blok) i PRZEBUDOWANY geometrią W4d: RebuildCompanions
  (RC_EDIT_BAR, Plan B watchera po MOVE / ROTATE / MIRROR / STRETCH), pręt-blok przy RC_EDIT_BAR. **Decyzja: przebudowa
  starego pręta (np. MOVE → Plan B) = zapis znacznika** — obrys jest już W4d, więc długość Manual liczona wg W4d (51 A = B: 168
  przed MOVE, 132 po MOVE). Bez znacznika: pierścień przy przebudowie (geometria bez zmian w W4d), pręt testowy H12-T, stary
  pierścień „44” bez znacznika (do migracji na 75), rekord krótszy niż 14 pól.
- Reguła Manual: znacznik ≥ 2021 + obrys W4d + kształt ze zmienionym układem ramion → odcinek W4d; brak znacznika → wzorzec
  (obrys dopasowywany najpierw do wariantów historycznych). Stara wtyczka gubiąca ogon [14+] → pręt wraca do gałęzi historycznej.
- S-REG: każda polilinia pręta utworzona / przebudowana w scenariuszu ma dodatkowe pola [14] = 1, [15] = 2021 — expected-diffs
  `polyline/08b-w4d-znacznik-geometrii-main.txt` (24 linie, 12 prętów) i `08c-w4d-znacznik-geometrii-fix.txt` (60 linii,
  30 prętów), `# bug: 8`, wyłącznie pola [14]/[15]. Block (bar-equiv) — bez nowych różnic; asercja „# BARBLOCK” akceptuje rekord
  16 ze znacznikiem (tools/RegressionDump/DrawingDump.cs).
- Testy dymne etapów 2–3 (E2a, E2b, E2d, E3): oczekiwana długość rekordu prętów z PlaceBar 14 / 15 → 16.
- Runda 3 (uwaga: RepairScale bez znacznika): oznaczanie bloku przeniesione do `SingleBarBlock.Rebuild` — jedno miejsce dla
  wszystkich przebudów bloku. Ścieżki przebudowy obrysu i znacznik:
  polilinia — `SingleBarEngine.PlaceBar` (tak), `RebuildCompanions` (tak: RC_EDIT_BAR, RC_UPDATE_BAR polilinii, Plan B watchera
  po MOVE / ROTATE / MIRROR / STRETCH, koniec gripu `TryEndStretch`), `Rebuild44` (pierścień — nie); ścieżka 00 watchera
  (zapis A z rozciągniętej polilinii, bez przebudowy obrysu — nie);
  pręt-blok — `SingleBarBlock.Create` → `CreateCore(markW4d)` (tak), `SingleBarBlock.Rebuild` (tak, poza pierścieniem i H12-T):
  RC_EDIT_BAR (`BarEditApply.RebuildBlock`), RC_UPDATE_BAR i automatyczna naprawa po SCALE (`RepairScale`), grip końca
  (`ApplyEndLength`); pręt testowy H12-T (`TestBarBlockEngine`) — nie.
  Test W4 H / HV / HU / HR: historyczny blok 51 A = B → RC_UPDATE_BAR i _.SCALE 2 → [14] = 2, [15] = 2021, Manual 132;
  _.U → bez znacznika, 168; _.REDO → znacznik, 132 (spójne).

### D30. W4d — wymiary kąta opcjonalne; ostrzeżenie w linii poleceń (0-POPRAWKI p.5)
D (14, 15, 46) i D, E (24) są opcjonalne w RC_BAR / RC_EDIT_BAR / pickerze (puste / 0 = kąt domyślny). Po dialogu:
`[RC] Pręt H12-05: Kształt 15: brak wymiarów kąta B i D — przyjęto kąt domyślny 45° (BS 8666:2020: kąt ze współrzędnych).`
RC_EDIT_BAR starego 15 z D = 0 → OK, D zostaje 0, geometria jak po MOVE (kąt domyślny) — bez zmiany. Ostrzeżenia w samym
dialogu (TextBlock) — W4f. Wiersze dialogów alfabetycznie (`BarShape.DisplayOrder`; 46: wiersz D → pole 4).
Podgląd w pickerze: zamknięte / zachodzące wg normy (33, 51, 52, 56, 63, 64, 75); Polygon tylko, gdy oś wraca do startu
(51 / 63 bez sztucznego odcinka „hak → hak”); 34–36, 41, 46, 47 rysowane jako linia łamana (wygląd ten sam).

### D31. W4g — tolerancja i pręt testowy
- 00: `Math.Ceiling(A / 25 − 1e-9) · 25` — szum z obrysu (3500,0000000004) nie podbija długości o 25 mm; gięte bez zmian.
- `TestBarBlockEngine.Create` (pręt testowy H12-T, poza BBS) wpisywał TotalLength = A z pominięciem formuły — teraz wg
  `BarLengthRules.TotalLengthFor` (jak po gripie); asercja E1 „TotalLength = LengthA” → „= A w górę do 25 mm (W4g)”.
- S-REG: 0 nowych różnic — w scenariuszu nie ma prętów 00 z TL ≠ wielokrotność 25 (AutoRebar / przebicie / ADD — siatka 250;
  F4d 3499,999998 → 3500 jak dotąd). expected-diffs bez zmian.
- Runda 1, uwaga 2 (P6): bezpiecznik RC_BBS obejmuje 00 (`FormulaChangedIn2020` + `BarShape.StoredLengthDiffers`): stary 00
  A 3010 / TL 3010 → „długość wg BS 8666:2020 = 3025 mm, w rysunku 3010 — sprawdź.”; 3000 i długość nadpisana — bez.
  S-REG (zrzut zawiera ostrzeżenia ReadBars) — 0 nowych linii: TL prętów 00 w scenariuszu (szablony / UB / siatka) to
  wielokrotności 25.

### D32. W4d — 46 / 25 w RC_SCHEDULE (bez zmian, informacyjnie)
RC_SCHEDULE / CSV pokazuje wymiary pozycyjnie (pola A–E), więc 46 ma E w kolumnie „D” i D w kolumnie „E” (tak samo 25 {A,B,E}
— E w kolumnie „C”) — stan sprzed W4d dla E. RC_BBS (.xls) mapuje po nazwach — poprawnie. Nie ruszane (poza zakresem).

### D33. W4d — incydent: `bin\NaSmartRc.dll` nadpisany buildem testów dymnych (naprawione)
`dotnet build tools/BrxSmokeTests/...` buduje przez ProjectReference także wtyczkę z domyślnym OutputPath = `bin\` — W4d trafił
na chwilę do `bin` (bez kompletu testów). Odtworzono W4a–c: kopia `src` w katalogu tymczasowym, edycje W4d cofnięte
(dokładnie odwrócone zamiany tekstu, `src/ShapeCodes` z HEAD), build, kontrola: obrysy 8 kształtów = zamrożone obrysy W4c
(bit w bit do 1e-9) i 00 bez zaokrąglenia → skopiowany do `bin\NaSmartRc.dll` (bez .pdb, jak było). DLL nie jest bajtowo
identyczny z poprzednim (inna ścieżka kompilacji), kod ten sam. Od teraz testy dymne budowane z
`-p:BuildProjectReferences=false` (ARCHITECTURE.md, „Otwarte tematy”).

### D35. W4e — znacznik geometrii: BEZ nowej wartości (2021 zostaje)
Rozważone „2022 = geometria z wymiarami zewnętrznymi”. Nie wprowadzone: (1) obrys W4e różni się od W4d o ≥ d/2 (> tolerancja
dopasowania 0,5 mm) na każdym pręcie giętym — wariant rozpoznawany jednoznacznie z obrysu (inaczej niż 51 A = B w D34);
(2) reguła Manual (D29) dla W4e daje ten sam punkt odniesienia co dla W4d (geometria W4d bez wymiarów zewnętrznych), więc znacznik
nie musi rozróżniać W4d / W4e; (3) nowa wartość zmieniłaby pole [15] wszystkich prętów w S-REG (przepisanie 08b / 08c — różnice
XData). Znaczenie [15] = 2021: „obrys zbudowany geometrią wg rysunków normy (W4d lub późniejszą)”.

### D36. W4e — wymiary zewnętrzne: zakres i przybliżenia
- `BarDimensionRules.ToAxis` — tabela per kształt dla kształtów z geometrią wg normy: 11, 12 (R + d/2), 13, 14, 15, 21, 22, 23, 24,
  33, 44, 46, 51, 63, 75. Zagięcia ≠ 90° wg rys. 4 (kąt ostry: wymiar do stycznej w skrajni łuku) i rys. 5 (kąt rozwarty: do
  przecięcia lic zewnętrznych) — jeden wzór `LegReduction(φ, r, d)`, ciągły w 90° (δ = d/2).
- Kąt skosu (14 / 15 B, D; 24 D, E; 46 D) liczony z wymiarów podanych przez użytkownika (rzuty lic zewnętrznych zamiast osi) —
  przybliżenie, różnica kąta pomijalna; dokładne odwrócenie wymagałoby iteracji.
- Kształty z geometrią do W4d-3 (25–29, 31, 32, 34–36, 41, 47, 56, 64, 98): wymiary bez przeliczenia (geometria i tak niezgodna
  z rysunkami normy), tylko łuk osi r + d/2. Przeliczenie razem z W4d-3.
- Haki 51 / 63 przy pustym C: C_eff traktowane jak wymiar zewnętrzny (oś C_eff − d/2). Po W4e oś ≈ formuła (|Δ| ≤ 0,5d + 1) dla
  51 / 63 z C = 0 i C > 0 — test z D19 możliwy i dodany (`BarGeometryW4eTests.RightAngleShapes_AxisNearFormula`).
- Pierścień 75: obrys zewnętrzny A/2 (dawniej A/2 + d/2), wewnętrzny A/2 − d, środek bez zmian (przebudowa — jak #5).
- axis[0] zostaje (P8): przebudowany pręt zaczyna się w tym samym punkcie, więc „dalsze” ramiona przesuwają się (U: prawe ramię o d).

### D37. W4e — rozkład Manual: odcinek bez odpowiednika we wzorcu (reguła uzupełniona)
Przed W4e `LegacySegmentLength` zwracał null dla odcinków bez odpowiednika w geometrii wzorcowej (44 — wzorzec to pierścień;
półkola 22 / 33 bez znacznika) i wywołujący brał długość ZMIERZONĄ. Po W4e zmierzona na obrysie z wymiarami zewnętrznymi byłaby
inna (0-POPRAWKI p.1 — BLOKER). Teraz: obrys W4e + brak odpowiednika → ten sam odcinek wariantu bez `ExternalDims` (= długość
zmierzona przed W4e). Test: każdy odcinek (ramiona i łuki, obie strony) 11, 12, 13, 21, 44, 14, 15, 22, 24, 33, 46, 51 ze znacznikiem
i bez — długość przed / po W4e identyczna (`Manual_W4eOutline_SameAsBeforeW4e`, smoke WV: 21 / 15 / 44 po _.MOVE).

### D38. W4e — S-REG: etykiety MLeader przesunięte o więcej niż d/2 — szablon UB ROZSTRZYGNIĘTY w rundzie 1 (D42 p.6)
Plan przewidywał w #13 „groty etykiet ≤ d/2”. Rzeczywiste różnice (wszystkie wynikają z geometrii obrysu, XData / TL / rozkłady /
opisy rozkładów / BBS — bez zmian, sprawdzone linia po linii):
- grot etykiety na końcu dalszego ramienia U (21 H10, BARS i F1b / F4c) przesuwa się o d = 10 mm (ramię prawe bliżej o d, bo B
  jest teraz po zewnętrznej, a axis[0] zostaje); tekst etykiet tworzonych „jak RC_BAR” (punkt w połowie parametru obrysu + (−50,
  150) — scenariusz S-REG) przesuwa się razem z końcem ramienia o 10 mm (F4c kopia MIRROR — symetrycznie);
- szablon UB B1 225 z AutoRebar (21 700 / 140 / 700): tekst stoi nad środkiem B mierzonym od osi lewego ramienia (insertX + B/2),
  więc po W4e prawe ramię jest bliżej tekstu niż lewe i grot (najbliższy punkt obrysu) przechodzi z lewego na prawe ramię
  (116 mm); szablon UB B2 225 (13): grot o d = 12 mm (górne ramię pętli niżej o d).
Wpisane do `13-wymiary-zewnetrzne.txt` z opisem w nagłówku. → **Pytanie: akceptujesz, czy wyśrodkować tekst szablonu UB na
obrysie (insertX + B/2 − d/2 — wtedy grot zostaje na lewym ramieniu, ale tekst przesuwa się o d/2)?**
- Linie „=>” istniejących poprawek (01, 03, 04, 05, 05c — wierzchołki, r pierścieni, grot F4c) przepisane na wartości po W4e
  (różnica łączna z #13, nota w nagłówku każdego pliku), jak D23.

### D39. W4f — walidacja minimów: interpretacja normy (Table 4 sprawdzona)
- Table 4 (kl. 8.4) = „Minimum allowances between two bends”: wymiar całkowity między dwoma zagięciami (w tym samym lub przeciwnym
  kierunku) ≥ X = 10d (min. 75) dla d ≤ 16, 13d dla d > 16 (6 → 75, 12 → 120, 20 → 260, 25 → 325). Stosowana do wymiarów
  ograniczonych dwoma zagięciami w kształtach z geometrią wg normy: 21 B, 23 B, 44 B / C / D, 46 C, 51 A / B, 63 A / B
  (pętle 13 / 22 / 33 — reguły q z Table 5).
- P (Table 2): kolumna „general bend” dla wolnych końców; strzemiona 47 / 51 / 63 (hak C) — kolumna „links” (end bend < 150°).
- Korby (kl. 8.2.4, rys. 6): w bibliotece korbą wg normy jest 46 (skos B, przesunięcie D) i 26 (skos B; przesunięcia brak w danych).
- kl. 7.9: krótszy bok prostokąta obrysu > 2750 mm (pręty gięte) i długość > 12 m (także 00) — uwagi.
- kl. 8.1.4: wymiar zerowy (poza opcjonalnymi — kąt, C 51 / 63); 12 R = 0 → „przyjęto r min.”.
- Wyłącznie ostrzeżenia: dialog RC_BAR / RC_EDIT_BAR i picker (TextBlock „Uwagi BS 8666:2020”, OK nie blokowane), RC_BBS (lista
  „Pozycja NN (kształt KK): …”, bez duplikatów; arkusz bez zmian). Pręty AutoRebar (UB 21 / 13, 00 co 250, UB 03 H10 70) — bez uwag
  (test), S-REG — 0 nowych różnic.

### D40. RC_REBUILD_BARS (P8, opcjonalne) — zrobione
Przebudowa obrysów wszystkich prętów przestrzeni modelu wg bieżącej geometrii, jedna grupa UNDO (`_.U` cofa całość — test WRU).
Te same ścieżki co MOVE / RC_UPDATE_BAR. Pomija: obrys już bieżący, obrys nierozpoznany (żaden wariant — np. ręcznie zmieniony;
lista w linii poleceń), pręt testowy H12-T. Znacznik [15] = 2021 zapisywany jak przy MOVE (D34) — stare pręty 14 / 15 / 22 / 24 /
33 / 46 / 51 przechodzą w rozkładzie Manual na odcinki W4d (istniejące rozkłady bez zmian). Grot etykiety (back-link) na nowy
obrys. REGEN w tej samej komendzie (`Editor.Regen`), nie przez SendStringToExecute.

### D41. Testy jednostkowe geometrii W4d po W4e
Testy sprawdzające współrzędne osi w umowie osi (BarGeometryBuilderTests, W4a „90° = wzorzec bit w bit”, W4d: 15 / 14,
51 A = B, Manual W4d) wołały domyślne `Current` — przełączone na jawny wariant `BarGeometryOptions.W4d` (geometria etapu W4d nadal
istnieje — rozpoznawanie starych obrysów). Zmienione oczekiwania: `LocalStart` 22 (A − d/2 = 394), pierścień 75 (oś 144),
`FitCandidates` (W4e, W4d, W4b, W4a, …, Legacy). Nowe: `BarGeometryW4eTests` (+ zamrożone obrysy W4d `FrozenOutlinesW4d`),
`BarShapeValidationTests`.

### D42. W4e / W4f — runda poprawek 1 (recenzja ChatGPT; wszystkie 6 uwag zweryfikowane — trafne)
1. **Pierścień 75 a rozkład Manual (BLOKER):** obrys zewnętrzny po W4e ma promień A/2 (dawniej A/2 + d/2) — cięciwa odcinka
   okręgu (H12 A 300: 40,724 → 39,158) zmieniała długość Manual. `BarFrameRules.LegacySegmentLength`: obrys pierścienia W4e
   (TryFitRing, promień (A − d)/2 + d/2) → cięciwa tego samego odcinka okręgu sprzed W4e; obrys stary → zmierzona (jak dotąd).
   Test jednostkowy (oba kierunki obiegu, kilka odcinków) i smoke W6 (polilinia i pręt-blok: 40,724).
2. **22 — B to wymiar lico–lico (BLOKER):** rysunek Table 5: B od lica zewnętrznego A do skrajni półkola → oś B − d (było B − d/2,
   ten sam błąd miało dawne `AxisParams` z W4d — test RC_SECTION go powielał; 22 usunięte z porównania „jak przed W4e”).
   Pozostałe kształty W4d sprawdzone z rysunkami (11, 12, 13, 14, 15, 21, 24, 33, 44, 46, 51, 63 — zgodne). Nowe testy niezależne
   od tabeli: prostokąt obrysu = wymiary zewnętrzne (11, 12, 13, 21, 22, 33, 44, 51, 63) i ramiona 15 / 24 / 46 do przecięć lic
   zewnętrznych (rys. 5) z osi. S-REG — brak prętów 22 w scenariuszu (0 zmian).
3. **51 / 63 z pustym C:** ostrzeżenie, gdy hak minimalny C_eff jest dłuższy od boku (51 H12 A = B = 120: C_eff 156 > 120); XData bez zmian.
4. **Kolumna P (Table 2) wg kąta zagięcia końcowego:** „links where end bend < 150°” tylko 51 / 63 (haki 90° w geometrii);
   47 (haki 180° ≥ 150°) i pozostałe — „general bend” (H12 P = 125).
5. **kl. 8.4 — prosty odcinek ≥ 4d między łukami zagięć** (kształty z geometrią wg normy, z osi bieżącej; brak / wsteczny odcinek
   = 0): 22 H12 A 125 B 140 C 150 D 165 → ostrzeżenie (oś: 29 mm po poprawce 2; recenzja liczyła 35 przy B − d/2).
6. **Szablon UB (D38):** tekst nad środkiem rzeczywistego obrysu (21: o −d/2), grot od punktu przesuniętego o 0,001 mm w stronę
   początku pręta → lewe ramię, jak wzorzec. S-REG: linia grotu UB B1 wraca do wartości wzorca (tylko punkt tekstu inny),
   doszła para „mleader loc” szablonu (21780,40 → 21774,40); 13 (UB B2) bez zmian. `13-wymiary-zewnetrzne.txt` — tylko te linie.

### D43. W4h — kąt ramienia skośnego (decyzja użytkownika) — zrobione
- `BarShape.SlopeAngle(pv, out isDefault, fromSingle)`: 14 / 15 — z B i D jak dotąd, sam B → asin(B/A), sam D → acos(D/A);
  24 — sam D → asin(D/B), sam E → acos(E/B); 46 bez zmian; brak → 45° / 30° + ostrzeżenie; rzut ≥ ramię → 90° + ostrzeżenie
  „B ≥ A … przyjęto kąt 90°”.
- Pole „Kąt [°] (opcjonalnie)” w RC_BAR / RC_EDIT_BAR i pickerze (tylko 14, 15, 24, 46): kąt → B = A·sin θ, D = A·cos θ
  (24: D, E z B; 46: D z B), co 5 mm, wpisane w pola wymiarów (XData jak dotąd). Ręczna zmiana B / D czyści pole kąta; zmiana
  długości ramienia przy wpisanym kącie przelicza wymiary.
- **Zmiana geometrii starych prętów:** 14 / 15 z B bez D (dotąd 45°) i 24 z jednym z D / E (dotąd 30°) po przebudowie (MOVE,
  RC_EDIT_BAR, RC_REBUILD_BARS) dostają kąt z wymiaru. Rama zachowana — obrys sprzed W4h rozpoznaje wariant W4e (nowa flaga
  `BarGeometryOptions.SlopeFromB`, `FitCandidates` 17: W4h → W4e → W4d → …). Rozkład Manual: punkt odniesienia = geometria
  osiowa z regułą kąta obrysu (`WithoutExternalDims`) — dla takiego pręta po przebudowie długość ramienia może się nieznacznie
  zmienić razem z kątem (inny pręt fizycznie); istniejące rozkłady bez zmian.

### D44. Krok 3f — pręty-bloki dla użytkownika (Twoja decyzja „Krok 3f — TAK”) — zrobione (staging)
- `RC_BAR_FORMAT [Polilinia/Blok]` (przycisk „Bar Format” w panelu Bars): format NOWYCH prętów, zapis w
  `%APPDATA%\NaSmartRc\barformat.txt`, **domyślnie Polilinia**; przy starcie `[NA Smart RC] Nowe pręty: polilinia|blok`.
  Istniejące pręty bez zmian (rysunek może mieć oba formaty). Rysunki dla kolegi (stara wtyczka) — zawsze Polilinia.
- Pręt-blok prosty (00 / 99) — także z numerem pozycji: grip końca i STRETCH zmieniają długość (co 5 mm, min. max(2d, 50)),
  potem jak przy polilinii: numer pozycji (inny pręt z tym numerem → nowy numer), rozkłady, grot i liczba na etykiecie,
  komunikat `[RC AUTO]` (`BarChangePropagator`). Nadpisana długość całkowita zostaje + ostrzeżenie. Gięte — tylko przesunięcie
  (długość przez RC_EDIT_BAR). Jedna operacja = jeden Ctrl+Z; Ctrl+Y przywraca stan po gripie (sprawdzone testem).
- Poprawki po recenzji zastępczej: (1) grip w trybie kopiowania (Ctrl / „Copy”) — gdy w komendzie dopisano kopię pręta
  z oczekującą zmianą długości (ten sam Mark i długość), zmiana jest odrzucana komunikatem `[RC AUTO] Kopiowanie gripem ze zmianą
  długości — użyj COPY, potem grip kopii. Pręty bez zmian.` (wcześniej wydłużałby się ORYGINAŁ); (2) `RC_BAR_FORMAT` przy
  nieudanym zapisie pliku: „Nie zapisano ustawienia — obowiązuje do końca sesji.”; (3) ostrzeżenie, gdy ustawiona zmienna
  `RC_REG_BARFORMAT`; (4) koniec komendy czyści podgląd gripa tylko w swoim rysunku.
- Drobna zmiana przy okazji: `BarFormat` przy (ponownym) odczycie zaczyna od Polilinii — odczyt deterministyczny (dotyczy
  tylko testów, w normalnej pracy ustawienie czytane raz).
- **Bez zmian (do Twojej decyzji):** RC_EDIT_BAR nadal odmawia edycji pręta testowego `H12-T` (bez numeru) z komunikatu
  etapu 3 — uzasadnienie („przed 3f”) już nieaktualne, ale zmiana poza zakresem 3f. Testy ręczne etapu 1 (T0–T16) — plan
  wymagał ich przed 3f; uznałem Twoje „TAK” za zgodę.

### D44. W4h — runda 2 (decyzje użytkownika po recenzji zastępczej)
1. **Grip / STRETCH końca A (14 / 15 z kątem z samego B) — wariant b:** `BarShape.WithFixedSlope` — na kopii wymiarów D = A·cos θ =
   √(A² − B²) (θ z bieżących A, B); `BarGeometryWatcher.TryEndStretch` szuka parametru na tej kopii (kąt z B i D nie zależy od A),
   po znalezieniu A zapisuje nowe A i to D. **D zaokrąglone do 0,01 mm, nie do 5 mm** — zaokrąglenie do 5 mm zmieniałoby kąt
   (do ~0,2°) i przesuwało koniec C; przy 0,01 mm zmiana kąta < 0,002°. Skutek: D bywa niecałkowite — RC_BBS / dialog pokazują
   uwagę „co 5 mm” dla D (informacyjnie). Koniec C — bez zmian (D nie zapisywane). Pręt-blok 14 / 15 — tylko grip przesunięcia,
   jak dotąd (`AllowsLengthEdit` = 00 / 99, bez zmian). Testy: jednostkowe (`Runda2_GripA_KeepsAngle_FixedEndStays`) i smoke WG / WGV.
2. **Stare 14 / 15 wg normy (D43) — bez zmian;** przy przebudowie starego pręta (obrys inny niż bieżący — MOVE / Plan B,
   RC_REBUILD_BARS, RC_EDIT_BAR; polilinia i blok) z kątem skrajnym → komunikat `[RC AUTO] Pręt … przebudowany wg BS 8666:2020:
   Kształt 15: B > A … przyjęto kąt 90°` (`SingleBarEngine.WarnSlopeClampOnRebuild`).
3. `BarShape.SlopeClampWarning`: rzut pionowy > ramię + 0,5 mm → 90° (wpisany kąt 90°: B = A — bez ostrzeżenia); rzut poziomy
   (14 / 15: D, 24: E) ≥ ramię przy braku rzutu pionowego → „przyjęto kąt 0°”.
4. Dialogi: kąt nieczytelny / spoza (0, 90] → czerwona ramka pola i uwaga „Kąt: podaj wartość z zakresu 0–90°”.
5. Test ręczny 9 doprecyzowany (B wymagane dla nowych prętów; 45° tylko dla starych bez B i D lub polem „Kąt”).

## Co zrobione
- W1 (#1, #2, #4, #5, #7) — staging (`bin-staging\NaSmartRc.dll`), szczegóły w `docs/POPRAWKI_WZORCA.md`.
  S-REG z oczekiwanymi różnicami: `tools/RegressionDump/expected-diffs`.
- W2-0 spike zdarzeń klonowania — wynik i decyzje (a)–(d): `docs/PLAN_POPRAWKI_W1_W2.md` 4.1.1 (bramka przeszła: przy COPY
  przychodzi `BeginDeepCloneTranslation` z mapą).
- W2a/W2b (#3, #6, 3b) — staging: `CloneMap` (zapis map), `CloneRemap` (przepięcie wg 4.3), `BarCopyWatcher` (kolejność 4.4,
  heurystyki tylko zapasem, komendy zagnieżdżone), strażniki kaskad (4.5, D8, D9), `-ARRAY` (D10).
- W2c — dokumentacja: ARCHITECTURE.md, POPRAWKI_WZORCA.md, PLAN_C_etap3.md (0-POPRAWKI p.5 rozwiązane), ten plik.
- **W4a, W4b, W4c (BS 8666:2020, poprawka #8) — staging.** W4a: `LegacyBarGeometry` + `BarGeometryOptions` +
  `BarFrameRules.TryFitVariants` (rama obrysów sprzed W4 i z każdego etapu), łuk styczny, odcinki zerowe, parametry po nazwie.
  W4b: formuły 2020 (14, 29, 31 + D, 47 + C, 51, 56, 63, 64, 75), Table 2 (r, q, P), haki 51/63 = C_eff (D18, D19), C opcjonalne
  w dialogach, RC_SCHEDULE / CSV / RC_BBS bez przerwania przy braku długości, bezpiecznik BBS (D17). W4c: 44 = kapelusz
  (znacznik [15] = 2020), 75 = pierścień (dawny okrąg 44, helisa usunięta, 77 nieobsługiwany), stary „44” → 75 w parserze
  (migracja leniwa), picker + 75, komunikaty RC_EDIT_BAR / RC_BBS. Testy dymne: `tools/BrxSmokeTests/PlanCW4Tests.cs`,
  `planc-w4.scr`, `run-planc-w4.ps1`.
- **W4d (W4d-1, W4d-2) i W4g — staging.** W4d: flaga `Bs2020Sharp` jako rzeczywisty etap (`Current` = W4d, `W4b` = etap
  W4b–W4c, `FitCandidates` = W4d → W4b → W4a → … → Legacy); geometria 12, 14, 15, 22, 24, 33, 46, 51 wg rysunków Table 5
  (D27, D28); parametry dopisane na końcu (14 / 15 + D, 24 + D, E, 46 + D); dialogi alfabetycznie, wymiary kąta opcjonalne +
  ostrzeżenie (D30); podgląd zamkniętych / zachodzących wg normy; `AxisParams` 22; usunięte martwe `_closedShapes`
  i `src/ShapeCodes`; testy Code12×7 i Code24 odblokowane. W4g: 00 w górę do 25 mm (D31). W4d-3 — po W4e.

- **W4e (#13) i W4f — staging.** W4e: `BarDimensionRules` (wymiary zewnętrzne → oś, rys. 4 / 5, łuk osi r + d/2, pierścień
  A/2), `BarGeometryOptions.Current` = W4e (`ExternalDims`), `W4d` jako wariant historyczny, `FitCandidates` W4e → W4d → W4b → W4a
  → … → Legacy; RC_SECTION bez `AxisParams`; Manual przed / po W4e bez zmian (D37); znacznik bez nowej wartości (D35);
  `RC_REBUILD_BARS` (D40); expected-diffs `13-wymiary-zewnetrzne.txt` + linie łączne (D38). W4f: `BarShapeValidation` —
  ostrzeżenia w dialogach RC_BAR / RC_EDIT_BAR / picker i RC_BBS (D39). W4d-3 — do zrobienia.

- **Plan C, krok 3f — staging (D44).** `BarFormat` (ustawienie użytkownika włączone), `RC_BAR_FORMAT` + przycisk + komunikat
  przy starcie, `BarChangePropagator` (nowy), `SingleBarGripOverrule` (numerowane 00 / 99 — długość z propagacją; gięte —
  przesunięcie; `[RC AUTO]`), Info `RC_TEST_BARBLOCK` na każdym pręcie-bloku. Testy: `tools/BrxSmokeTests/PlanCStage3fTests.cs`,
  `planc-f3.scr`, `run-planc-f3.ps1`.

## Wyniki testów
- W1 (staging): `run-planc-w1` PASS=54 FAIL=0; E1 376 PASS / 1 SKIP (ARRAY, jak dotąd); E2a 41, E2b 22, E2d 43, E3 196 — FAIL=0.
- S-REG (pełny, wzorzec 2× identyczny): Polyline main 26 / fix 380, Block main 26 / fix 354 — wszystkie = expected-diffs.
- `dotnet test`: 326 PASS, 11 FAIL (stan bazowy — błąd #8, W4).
- **W2 (staging, końcowy):** `run-planc-w2` PASS=63 FAIL=0 (2 przebiegi); W1 PASS=60; E1 376 PASS / 1 SKIP; E2a 41, E2b 22,
  E2d 43, E3 196 (S13/S13P przepisane na mapę) — FAIL=0. S-REG pełny 2× PASS (wzorzec 3× identyczny): Polyline main 26 /
  fix 542, Block main 26 / fix 510 = expected-diffs; BARBLOCK OK. `dotnet test` 326 / 11 (bez zmian).
- **W4 (staging), po każdym kroku:** build 0 ostrzeżeń / 0 błędów; W1 60, W2 100, E1 376 / 1 SKIP, E2a 41, E2b 22, E2d 43,
  E3 196 — FAIL=0 po W4a, W4b i W4c.
  - W4a: `dotnet test` 365 PASS / 0 FAIL / 8 Ignore (Code12×7, Code24 do W4d-2); `run-planc-w4` 7/0; S-REG pełny PASS —
    0 nowych różnic (main 26 / fix 542 / 510).
  - W4b: `dotnet test` 425 / 0 / 8; `run-planc-w4` 12/0; S-REG PASS — 0 nowych różnic (po poprawkach D17 i D22; pierwszy
    przebieg wykrył 26 linii — patrz D17, D22).
  - W4c: `dotnet test` 439 / 0 / 8; `run-planc-w4` 28/0; S-REG pełny PASS (wzorzec od nowa, `RegressionDump` z refleksją):
    Polyline main 26 / fix 558, Block main 26 / fix 526 = expected-diffs (+16 linii 08, 4 linie łączne przepisane — D23);
    BARBLOCK 12/0.

- **W4d + W4g (staging, `bin-staging\NaSmartRc.dll` 05:31):** build 0 ostrzeżeń / 0 błędów; `dotnet test` 532 PASS / 0 FAIL /
  0 Ignore (nowe: `BarGeometryW4dTests` + `FrozenOutlinesW4c` — obrysy etapu W4b–W4c zamrożone kodem tego etapu); `run-planc-w4`
  73/0 (nowe kroki E, EV, ER, EM, EEDIT, EED: 8 kształtów PlaceBar → MOVE → ROTATE 30° → MIRROR → RC_EDIT_BAR, rama zachowana;
  zamrożone obrysy W4c 15 / 51 obrócone i odbite → MOVE → geometria W4d w tej samej ramie; rozkład Manual = długość wzorca;
  pręty-bloki 15 / 22 / 33 / 51 RC_EDIT_BAR); W1 60, W2 100, E1 376 / 1 SKIP, E2a 41, E2b 22, E2d 43, E3 196 — FAIL=0;
  S-REG pełny PASS — 0 nowych różnic (Polyline main 26 / fix 558, Block main 26 / fix 526 = expected-diffs; BARBLOCK 12/0).

- **Runda poprawek 1 W4d / W4g (staging):** build 0 / 0; `dotnet test` 544 / 0 / 0; `run-planc-w4` 85/0 (nowe: B00 — bezpiecznik
  BBS 00; F — rozkład Manual 33 / 15 / 46 na polilinii i pręcie-bloku po ROTATE + MIRROR: tworzenie i edycja = odcinki W4d;
  D — obrys sprzed W4 → wzorzec, W4d → odcinek W4d; pręty-bloki W4d przechodzą MOVE / ROTATE / MIRROR skryptu);
  W1 60, W2 100, E1 376 / 1 SKIP, E2a 41, E2b 22, E2d 43, E3 196 — FAIL=0; S-REG PASS, expected-diffs bez zmian.
  `bin\NaSmartRc.dll` = W4a–c (odtworzony, D33) — w tej rundzie nienaruszony.

- **Runda poprawek 3 W4d (staging):** `dotnet test` 548 / 0; `run-planc-w4` 97/0; W1 60, W2 100, E1 376 / 1 SKIP, E2a 41, E2b 22,
  E2d 43, E3 196 — FAIL=0; S-REG PASS (Polyline main 50 / fix 618, Block 26 / 526, BARBLOCK 12/0) — expected-diffs bez zmian.
- **Runda poprawek 2 W4d (staging):** build 0 / 0; `dotnet test` 548 / 0 / 0; `run-planc-w4` 92/0 (nowe G / GV: 51 A = B, C = 0
  — stary pręt bez znacznika, polilinia i blok po obrocie + odbiciu → Manual 168; nowy → 132; stary po _.MOVE → znacznik 2021 →
  132); W1 60, W2 100, E1 376 / 1 SKIP, E2a 41, E2b 22, E2d 43, E3 196 — FAIL=0; S-REG PASS: Polyline main 50 / fix 618,
  Block main 26 / fix 526 = expected-diffs (+ 08b / 08c), BARBLOCK 12/0. `bin\NaSmartRc.dll` (W4a–c) nietknięty.

- **W4e (staging):** build 0 ostrzeżeń / 0 błędów; `dotnet test` 648 PASS / 0 FAIL (nowe `BarGeometryW4eTests`, zamrożone obrysy W4d
  `FrozenOutlinesW4d`); `run-planc-w4` 113/0 (nowe W / WV / WEDIT / WED: UB 225 z AutoRebar 140 × 700, RC_SECTION bez AxisParams
  140 × 700, zamrożone obrysy W4d 21 / 15 / 44 obrócone i odbite → _.MOVE → rama i axis[0] zachowane, Manual każdego odcinka bez
  zmian, pręt-blok 21 RC_EDIT_BAR → 140 × 750); W1 60, W2 100, E1 376 / 1 SKIP, E2a 41, E2b 22, E2d 43, E3 196 — FAIL=0;
  S-REG PASS: Polyline main 230 / fix 840, Block main 206 / fix 748 = expected-diffs (nowe `13-wymiary-zewnetrzne.txt`:
  180 linii main + 222 fix na wariant; linie łączne przepisane w 01 / 03 / 04 / 05 / 05c — 211 na wariant), BARBLOCK 12/0.
- **W4f + RC_REBUILD_BARS (staging, końcowy `bin-staging\NaSmartRc.dll`):** build 0 / 0; `dotnet test` 684 / 0 (nowe
  `BarShapeValidationTests`); `run-planc-w4` 120/0 (nowe WF — RC_BBS „B < q (85)”, „A < P (125)”; WR / WRV / WRU —
  RC_REBUILD_BARS: obrys W4d → bieżący w ramie, grot etykiety na obrysie, pręt bieżący i zepsuty bez zmian, _.U cofa całość);
  W1 60, W2 100, E1 376 / 1 SKIP, E2a 41, E2b 22, E2d 43, E3 196 — FAIL=0; S-REG pełny (wzorzec od nowa) PASS — 0 nowych
  różnic po W4f. `bin\NaSmartRc.dll` nietknięty.

- **Runda poprawek 1 W4e / W4f + W4h (staging, końcowy `bin-staging\NaSmartRc.dll`):** build 0 / 0; `dotnet test` 711 / 0 (nowe
  `BarSlopeAngleW4hTests`, testy obrysu = wymiary zewnętrzne, pierścień Manual, walidacja C_eff / P 47 / 4d); `run-planc-w4` 123/0
  (nowe W5 — 15 sprzed W4h po _.MOVE: kąt 36,87° w ramie; W6 — pierścień Manual 40,724 polilinia i blok); W1 60, W2 100, E1 376 / 1 SKIP,
  E2a 41, E2b 22, E2d 43, E3 196 — FAIL=0; S-REG pełny (wzorzec od nowa) PASS, BARBLOCK 12/0 — zmiana expected-diffs tylko w 13
  (szablon UB: grot jak wzorzec, para „mleader loc”). `bin\NaSmartRc.dll` nietknięty.

- **Krok 3f (staging, `bin-staging\NaSmartRc.dll` ~08:00):** build 0 ostrzeżeń / 0 błędów; `dotnet test` 711 / 0;
  `run-planc-f3` 50 / 0 (RC_BAR_FORMAT: brak pliku → Polilinia, fallback pliku starej wtyczki, „Blok” → plik + PlaceBar
  = pręt-blok, „Polilinia” → plik + polilinia, plik przywrócony; S16 numerowany pręt-blok + kopia o tym samym numerze →
  L + 500 → nowy numer H12-05, rozkład 3500 z nowym numerem, etykieta „5 H12-05”, kopia bez zmian, BBS dwa wiersze; _.U → stan
  sprzed; S17 21 → 1 grip, ApplyEndLength odrzuca; S18 nadpisana długość 9999 bez zmian + ostrzeżenie; _.STRETCH skryptem
  +503 → 3505 / TL 3525 / nowy numer / rozkład / etykieta, _.UNDO 1 → stan sprzed, _.REDO → stan po; STRETCH końca 21 —
  bez zmian; AutoRebar w trybie Blok = Polilinia: te same numery (1, 2, 4–6, 101–104) i ten sam BBS (9 wierszy, 14 rozkładów));
  E3 196 / 0, E1 376 / 1 SKIP (ARRAY), E2a 41, E2b 22, E2d 43, W1 60, W2 100, W4 123 — FAIL=0; S-REG pełny (wzorzec od nowa)
  PASS — Polyline main 232 / fix 840, Block main 208 / fix 748 = expected-diffs (bez zmian), BARBLOCK 12 / 0.
  `bin\NaSmartRc.dll` nietknięty.

- **W4h runda 2 (staging):** build 0 / 0; `dotnet test` 714 / 0; `run-planc-w4` 125/0 (nowe WG / WGV — STRETCH końca A 15 przy stałym
  kącie); F3 50, W1 60, W2 100, E1 376 / 1 SKIP, E2a 41, E2b 22, E2d 43, E3 196 — FAIL=0; S-REG pełny PASS (Polyline main 232 /
  fix 840 = expected-diffs, Block = expected-diffs, BARBLOCK 12/0) — expected-diffs bez zmian. `bin\NaSmartRc.dll` nietknięty.
- **Krok 3f po poprawkach recenzji (staging ~08:24):** build 0 / 0; `dotnet test` 714 / 0; `run-planc-f3` 54 / 0 (nowy G1 —
  symulacja gripa w trybie kopiowania: oryginał i rozkład bez zmian, kontrola 3500), W4 125 / 0, W1 60, W2 100, E3 196, E1 376 /
  1 SKIP, E2a 41, E2b 22, E2d 43 — FAIL=0; S-REG PASS (232 / 840, 208 / 748 = expected-diffs, BARBLOCK 12 / 0).
  **Uwaga:** w trakcie tych testów `bin\NaSmartRc.dll` został podmieniony z zewnątrz (nie przez implementatora) na ten build
  stagingu (MD5 841e393c…, identyczny), poprzedni zapisany jako `bin\NaSmartRc.W4d.dll.bak`.

## Testy ręczne dla Ciebie
Po W1: RC_EDIT_BAR 3000→3500 (brak „(grip)”); U-bar ostatnie ramię; STRETCH/grip końca jak dotąd; ERASE pręta bez etykiety
usuwa rozkład (Ctrl+Z); ROTATE/MIRROR U-bara zostaje; RC_EDIT_BAR obróconego; 44 MOVE i zmiana średnicy; nadpisana długość +
STRETCH → ostrzeżenie.
Po W2:
1. COPY kompletu (pręt + etykieta + rozkład + opis) → RC_EDIT_BAR kopii zmienia tylko jej rozkład; etykiety n / n.
2. COPY samego rozkładu z opisem → etykieta pręta 2n; COPY samego pręta → kopia bez etykiety.
3. MIRROR (N i Y) kompletu — opis odbicia czytelny (nieodbity tekst), rozkład przy swoim pręcie.
4. ARRAY (okno i `-ARRAY`) 1×3 — każdy opis przy swoim rozkładzie, etykiety n.
5. Ctrl+C / Ctrl+V w tym samym rysunku (rozkład bez pręta → wskazuje oryginalny pręt) i **do innego rysunku** (rozkład bez
   pręta → bez pręta źródłowego; komplet → powiązany sam ze sobą).
6. Kopiowanie gripem (Ctrl przy przeciąganiu) i Ctrl+przeciąganie zaznaczenia — jak COPY.
7. COPY wielokrotne: druga kopia, potem „Cofnij” w COPY i **ESC** — opisy oryginału zostają, pierwsza kopia poprawna.
8. COPY, Ctrl+Z, Ctrl+Y (REDO) — po REDO kopia może wskazywać oryginał (D9 — do decyzji).
Po W4 (a, b, c — BS 8666:2020):
1. Stary rysunek z prętem 15 / 14 / 46 obróconym i odbitym (MIRROR): MOVE, RC_EDIT_BAR — pręt zostaje obrócony i odbity,
   łuk zagięcia ≠ 90° bez „załamania” (nowa geometria).
2. RC_BAR 51 z PUSTYM C (H12, A 400, B 300) → długość 1600, hak krótszy niż dawniej; 63 z pustym C → 1375. 63: oba haki
   tej samej długości (D18).
3. RC_BBS starego rysunku z 47 / 14 / 51 / 63 / 75: ostrzeżenia „długość wg BS 8666:2020 = …, w rysunku …” (D17);
   47 bez C → „brak długości”, reszta arkusza OK. RC_SCHEDULE z takim prętem → wiersz „brak długości”, eksport CSV działa.
4. Stary rysunek z pierścieniem 44: RC_EDIT_BAR → komunikat „stary kształt 44 … = 75”, dialog pokazuje 75 (A, B = zakład);
   po OK pręt zapisany jako 75; Ctrl+Z wraca do 44. MOVE / ROTATE pierścienia — oba okręgi, środek jak dotąd.
5. Nowy 44 (kapelusz) z pickera: A 500, B 400, C 500, D 400, E 300 → kształt „kapelusza”; ROTATE, MIRROR, COPY,
   RC_EDIT_BAR — zostaje kapeluszem (nie pierścieniem). Ten sam rysunek otwarty starą wtyczką kolegi → kapelusz po zapisie
   starą wtyczką stanie się pierścieniem (znane ograniczenie, ogon [14+] gubiony).
6. Picker: 75 (pierścień) dostępny; podgląd 44 = kapelusz, 75 = okrąg.
Po W4d / W4g (`bin-staging`):
1. Picker: podgląd 15 (A skośne + C poziome), 33 (bieżnia), 46 (A–B–C–B–E), 51 (pionowy prostokąt z hakiem). Wiersze wymiarów
   alfabetycznie (46: A, B, C, D, E). 15 z pustym D → OK, w linii poleceń ostrzeżenie „kąt domyślny 45°”; 15 A 500, B 300, C 300,
   D 400 → skos 3:4 (36,9°). 51: A = wysokość.
2. Stary rysunek z prętami 15 / 14 / 46 / 51 / 33 (także obrócone i odbite): MOVE → pręt przerysowany wg normy, obrót i odbicie
   zostają, początek osi (axis[0]) w tym samym miejscu. RC_EDIT_BAR starego 15 (D puste) → OK, kąt 45°, długość bez zmian.
3. Stary rysunek z prętami 12 / 22 / 24 (nie ma ich w pickerze): MOVE → 22 z półkolem o średnicy C, 12 z łukiem R,
   24 z pionowym C (skos 30°, gdy brak D / E).
4. RC_DISTRIBUTION Manual na NOWYM pręcie 33 / 15 / 46 (także obróconym i odbitym) → długość = kliknięty odcinek (D29);
   na pręcie ze starego rysunku (obrys sprzed W4d, przed MOVE) → długość jak dawniej. RC_EDIT_DISTRIBUTION → Manual → inne ramię.
7. RC_BBS starego rysunku z prętem 00 o A = 3010 → ostrzeżenie „= 3025 mm, w rysunku 3010”; pręt z nadpisaną długością — bez.
8. Stary rysunek z prętem 51 (A = B, C puste): RC_DISTRIBUTION Manual na haku → długość jak dawniej (168 dla H12); po MOVE
   pręta → 132 (D34). Ten sam rysunek zapisany starą wtyczką kolegi → znacznik znika, długość jak dawniej.
5. Pręt prosty 00 o długości np. 3010 → TL 3025 w RC_EDIT_BAR / RC_SCHEDULE / RC_BBS (A = 3010 bez zmian).
6. RC_SECTION przez rozkład z prętem 22 — pręt w widoku z półkolem (AxisParams 22).
Po W4e / W4f (`bin-staging`):
1. Nowy pręt 21 (U) H12 A 500, B 300, C 500 → obrys ma po ZEWNĘTRZNEJ dokładnie 300 × 500 (zmierz DIST lico–lico), łuki
   zagięć większe (promień osi r + d/2 = 30). 13 H12 {500, 200, 300} → obrys 500 × 200.
2. AutoRebar UB (RC_GENERATE → UB1): szablon UB 225 ma obrys 140 × 700 (dawniej 152 × 706); etykieta szablonu może wskazywać
   prawe ramię (D38). Rozkłady / opisy / numery / BBS — jak dotąd.
3. RC_SECTION przez UB: pręt w widoku ma te same wymiary zewnętrzne co przed W4e (140 po zewnętrznej), tylko łuki większe.
4. Stary rysunek (pręty z W4d / sprzed W4, także obrócone i odbite): MOVE → pręt przerysowany z wymiarami zewnętrznymi, obrót,
   odbicie i początek osi (axis[0]) bez zmian; ROTATE / MIRROR / RC_EDIT_BAR — rama zachowana.
5. RC_DISTRIBUTION Manual na ramieniu U-bara / 44 / 15 — przed i po MOVE ta sama długość rozkładu.
6. Pierścień 75 A 300: obrys zewnętrzny średnicy 300 (dawniej 312), środek bez zmian przy RC_EDIT_BAR.
7. RC_REBUILD_BARS na starym rysunku: komunikat „Przebudowano N …, aktualnych …, pominięte …”; Ctrl+Z cofa całość; pręt
   z ręcznie zmienionym obrysem — na liście pominiętych.
8. W4f: RC_BAR 13 H12 B = 80 → pod wymiarami „Uwagi BS 8666:2020: B < q (85) …”, OK działa; 11 A = 100 → „A < P (125)”;
   21 B = 100 → „B < X (120)” (Table 4); wymiar 502 → „nie jest wielokrotnością 5 mm”. Picker — to samo pod długością.
   RC_BBS z takim prętem → ostrzeżenie „Pozycja NN (kształt 13): B < q (85) …”, arkusz się tworzy.
9. W4h: RC_BAR 15 H12 A 500, B 300, C 300, D puste → skos 36,9° (asin(B/A)), bez ostrzeżenia. Dla NOWYCH prętów B jest wymagane
   (kl. 8.1.4 — puste B = ostrzeżenie „Wymiar B = 0”); kąt domyślny 45° dotyczy tylko STARYCH prętów bez B i D (ostrzeżenie), a 45°
   dla nowego pręta wpisz polem „Kąt [°]” (= 45 → B 355, D 355). B 600 > A → 90° + ostrzeżenie; wpisany kąt 90° (B = A) — bez
   ostrzeżenia; D ≥ A przy pustym B → 0° + ostrzeżenie. Pole „Kąt [°]” = 30 → B 250, D 435 wpisane same; zmiana A przy wpisanym
   kącie przelicza B, D; ręczna zmiana B czyści kąt; kąt 95 albo „abc” → czerwona ramka i uwaga „kąt 0–90°”. To samo w pickerze.
   Stary rysunek z 15 (B bez D, rysowany 45°): MOVE → skos wg B, obrót / odbicie zostają; stary 15 z B > A → MOVE / RC_REBUILD_BARS
   → komunikat „[RC AUTO] … przyjęto kąt 90°”. Grip / STRETCH wolnego końca A pręta 15 (D puste) o 100 → A 600, D = 400 zapisane,
   kąt bez zmian; koniec C — jak dotąd.
10. Runda 1: 22 H12 A 400 B 600 C 150 D 300 → obrys szerokości 600 (było 606); pierścień 75: RC_DISTRIBUTION Manual na okręgu
   → długość jak przed W4e; szablon UB z AutoRebar — tekst nad środkiem, strzałka na lewym ramieniu.
Po 3f — jak przetestować pręty-bloki (`bin-staging`, rysunek testowy, NIE rysunek dla kolegi):
1. Start BricsCAD: w linii poleceń `[NA Smart RC] Nowe pręty: polilinia`. `RC_BAR_FORMAT` (albo przycisk „Bar Format” w panelu
   Bars) → `Blok` → komunikat „Nowe pręty: blok (zapisane)”. Restart BricsCAD → przy starcie „Nowe pręty: blok”.
2. `RC_BAR`: pręt prosty 00 H12 3000 + etykieta, potem 21 (U) i 13 → kliknięcie pręta zaznacza go w całości (blok);
   `RC_TEST_BARBLOCK` → `Info` → wskaż pręt: Mark, kształt, długość, BTR `*U`, Explodable = nie.
3. `RC_DISTRIBUTION` z pręta 00 i z pręta 21 → rozkład i opis jak dotąd; etykieta pręta „n H12-nn”.
4. Grip końca pręta 00 (prawy kwadrat): przeciągnij o ~500 → podgląd (cyan) w trakcie, po kliknięciu `[RC AUTO] Pręt H12-nn: 3500 mm
   (grip) (1 rozkład(y))`; rozkład i opis mają nową długość, etykieta grotem na pręcie. ESC w trakcie przeciągania — bez zmian.
5. COPY pręta 00 (z etykietą i rozkładem) → grip końca KOPII → kopia dostaje nowy numer (`→ nowa pozycja H12-mm`), jej rozkład
   i etykieta też; oryginał bez zmian. `RC_BBS` → dwa wiersze (stary i nowy numer).
6. Pręt 21 (U): tylko jeden grip (przesunięcie); STRETCH oknem samego końca → komunikat „kształt 21 — długość tylko przez
   RC_EDIT_BAR”, pręt bez zmian; `RC_EDIT_BAR` zmienia wymiary jak dotąd.
7. STRETCH oknem końca pręta 00 (jak dla polilinii) → długość, numer, rozkład jak w p. 4.
8. Ctrl+Z po p. 4 / 5 / 7 → pręt, numer, rozkład, opis i etykieta wracają jednym krokiem; Ctrl+Y → stan po gripie.
9. `RC_GENERATE` (Mesh) w trybie Blok na kopii płyty → te same numery i `RC_BBS` co w trybie Polilinia; ponowne uruchomienie bez
   nowych szablonów.
10. `RC_BAR_FORMAT` → `Polilinia` → `RC_BAR` tworzy znów polilinię; pręty-bloki w rysunku działają dalej. Rysunki dla kolegi
    (stara wtyczka) — tylko w trybie Polilinia (stara wtyczka nie widzi prętów-bloków w BBS i numeracji).
Po 3f — testy z recenzji (G1–G5):
- **G1 grip w trybie kopiowania:** pręt-blok 00 z rozkładem → zaznacz, chwyć grip KOŃCA, w trakcie przeciągania Ctrl (albo opcja
  „Copy”) → kliknij. Oczekiwane: `[RC AUTO] Kopiowanie gripem ze zmianą długości — użyj COPY, potem grip kopii. Pręty bez zmian.`;
  oryginał i jego rozkład bez zmian, kopia taka, jak ją utworzył BricsCAD. (Automatycznie tylko symulacja — skrypt nie wywoła
  trybu kopiowania gripa; test `RCSMOKE_F3_G1`.)
- **G2:** COPY pręta z etykietą i rozkładem → grip końca kopii (+500) → kopia nowy numer, jej rozkład i etykieta; Ctrl+Z → wszystko
  wraca jednym krokiem; Ctrl+Y → stan po gripie.
- **G3:** w trakcie przeciągania gripa końca 'ZOOM (kółko myszy / przezroczysty ZOOM) → podgląd zostaje, po kliknięciu zapis raz.
- **G4:** dwa otwarte rysunki z prętami-blokami — grip w jednym nie zmienia nic w drugim (podgląd / komunikaty tylko w aktywnym).
- **G5:** `RC_BAR_FORMAT` → Polilinia, restart BricsCAD → przy starcie „Nowe pręty: polilinia”; `RC_BAR` tworzy polilinię.
