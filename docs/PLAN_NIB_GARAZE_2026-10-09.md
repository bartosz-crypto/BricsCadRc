# Plan: A — automat domykania nibu, B — garaże „GARAGE …” jako osobne płyty

Status: PROJEKT — czeka na recenzję i akceptację użytkownika. Analityka: scratchpad sesji `nibplan\` (nib2.py, nib3.py, rep.py, gar.py).

## Odpowiedzi użytkownika (2026-10-09)
- A9.1: tolerancja przerwy **4 mm** (GapTol = 4.0; wszędzie zamiast 2 mm, także w testach i komunikatach).
- A9.2: zawinięcia nibu — „nie wiem, sprawdźmy potem”: odtwarzać dosłownie z GA (wariant planu), ocenić na płytach 321/527 w teście ręcznym; ewentualna zmiana później.
- A9.3: AutoRebar domyka **automatycznie** z komunikatem (bez pytania).
- A9.4: zamknięte (płyta jak w GA, bez offsetu).
- A9.5: RC_SECTION **też domyka** (krok A4.3 wchodzi).

## Poprawki po niezależnej recenzji (Claude) — mają pierwszeństwo nad treścią niżej
1. **BLOKER (319):** `JoinLoops` (GaImportEngine.cs:287) skleja linię nibu z linią strefy 760 mm w zamknięty pas 600 mm (Detect go pomija) i kasuje kawałki. W `CloneInto` NAJPIERW zbierać kawałki (Polyline/Line/Arc) i `TryClose`, DOPIERO POTEM `JoinLoops` na pozostałych. Różnica dla 319 → wpis POPRAWKI 23; test ręczny: stary rysunek 319 → ponów Prepare GA.
2. **Kontrola po zapisie:** o zapisie decyduje czysta `LooksLikeNib`. Po `Commit` → `Detect`; gdy null → druga transakcja: usuń nową polilinię, przywróć kawałki (`Erase(false)`), `Log.Error` + komunikat. Nie wołać `Detect` przed Commit (fałszywy null przez catch w NibDetector.cs:82).
3. **Filtr kawałków:** WSZYSTKIE próbki kawałka muszą spełniać warunek (pas ≤ maxWidth+1 od obrysu, w obrysie / ≤ GapTol). Kawałek „efektywnie zamknięty” (`IsEffectivelyClosed` / pierwszy = ostatni punkt — progi drzwi 324-325, 528-529, 539) pomijać.
4. **Cisza:** 0 kawałków w pasie → bez komunikatu; są kawałki, ale nie da się domknąć → komunikat.
5. **Podpięcie:** NIE w `SlabPicker.PickOrDraw`. `EnsureForCommand` wołać w komendach korzystających z nibu, PO zebraniu wszystkich wskazań: Mesh (dół i góra, po obu wskazaniach), B1, B2, T1, T2, UB*, oraz RC_SECTION (decyzja A9.5). B3 i Add — nie domykają (nie korzystają z nibu; do potwierdzenia przez użytkownika).
6. **BRX:** scenariusze 319 (po pkt 1) i 528-529 (linia 760 z końcami w wierzchołkach nibu) — S-REG automatu nie sprawdza.
7. Bezpiecznik: |pole mniejszej pętli| > 0,4·|pole obrysu| → komunikat zamiast zgadywania. Styki „T” (koniec w środku boku) — opisane ograniczenie.
8. **B:** `TryParseReportHeader` zachowuje dzisiejszy label 1:1 (np. „PLOT 459-460 (13 piles) + 532-533”, piles 0; „PLOT 530 (8 piles) + 545”); „PLOTS 3-4” w raporcie — nadal nierozpoznane (jak dziś). Testy na te przypadki.
9. Kolejność płyt w GA (GaImportEngine.cs:129, domyślny indeks GaImportCommands.cs:42): domyślnie pierwszy PLOT, nie GARAGE.
10. Ryzyko: garaż bez DRAWING_NUMBER w GA nie ma numeru zapasowego (GaTitleEngine.cs:196) — opisać.
11. NibDetector.cs: komentarz „zmiana kryteriów tu → zmiana w NibLoopBuilder.LooksLikeNib”.

## 0. Stan dziś
- `NibDetector.Detect` (src/Core/NibDetector.cs 50–83): tylko zamknięty obrys + druga ZAMKNIĘTA polilinia (dowolna warstwa), bbox w obrysie z wcięciem −1…256 (`Within` l.160), wierzchołki ≤256 mm od obrysu, pary krawędzi osiowych 50–256 mm (`Build` 94–138). Otwarte linie ignorowane. Wołany: AutoRebarEngine.cs:194,544,576; .Add.cs:45; .B3.cs:79; .Section.cs:45; AutoRebarCommands.cs:103; S-REG RegCommands.cs:127.
- Prepare GA (`GaImportEngine`): `Classify` l.229; `CloneInto` 250–291 zbiera tylko otwarte polilinie; `JoinLoops` 369–419 (tol 1,0 mm, bez łuków) łączy kawałki tylko między sobą; obrysu nie używa; LINE/ARC pomija.
- Dane Holbrook P02: linia nibu = otwarta LWPOLYLINE bez łuków, 160 mm od krawędzi (garaże 140), oba końce DOKŁADNIE na obrysie (przerwa 0,000). 458/461-462: 2 punkty; 321: 6 wierzch. z zawinięciami 800/1477 mm; 525: 4; 527: 6 (koniec w wierzchołku obrysu); GARAGE 321/322: U na 3 bokach; 324-325: LWPL+LINE; 466 (P01): 2 LWPL; 319/528-529/539: dodatkowe linie strefy 760 mm. Door threshold — efektywnie zamknięta LWPL, wierzchołek 19,8 mm w płycie.

## A. Automat domykania nibu
**A1.** Tylko gdy `NibDetector.Detect(db, obrys) == null` → rysunki z zamkniętym nibem bit w bit bez zmian (NibDetector i JoinLoops nietknięte). Wynik: zamknięta polilinia na warstwie obrysu (SD-PILED-RAFT), użyte kawałki kasowane. `GapTol = 2,0 mm`.

**A2. `src/Core/NibLoopBuilder.cs`** (czysty, net48 C#9 + net8, punkty `(double X,double Y)`):
`Build(outline, outlineBulges, pieces, minWidth, maxWidth, gapTol=2.0)` → `Result {Ok, Loop, LoopBulges, Used, Problems}`; stałe min/max z `NibDetector`. Komunikaty po polsku, InvariantCulture.
Algorytm: (1) czyszczenie obrysu; (2) filtr kawałków — próbki (wierzchołki + środki) w obrysie lub ≤gapTol od niego i ≤maxWidth+1 od obrysu, reszta pomijana cicho (linie 760 mm, door threshold); (3) graf końców (klastry ≤gapTol), stopień ≥3 → „rozgałęzienie”; łączenie przy przerwie: przecięcie prostych lub środek; (4) składowa zamknięta = kandydat; (5) końce ≤gapTol od obrysu przyciągane (przecięcie z bokiem / rzut); koniec na łuku obrysu → komunikat; wolny koniec → „Nib: przerwa X mm w punkcie (x, y) — linia uskoku nie dochodzi do obrysu płyty (tolerancja 2 mm).”; (6) łańcuch z końcami na obrysie: kontrola przecięć, dwie pętle, zostaje WIĘKSZA |pole|; (7) kilka łańcuchów: przedziały rozłączne, jedno obejście obrysu; łuki przenoszone (odwrócenie → zmiana znaku bulge); (8) scalanie współliniowych; (9) `LooksLikeNib` = replika kryteriów NibDetector → inaczej „linia uskoku X mm od krawędzi — poza zakresem nibu 50–255 mm”.

**A3. `src/Core/NibAutoClose.cs`**: `TryClose(db, outlineId, pieceIds|null, tag, msgs)`: obrys zamknięty, Normal.Z ≥ 0.999; bramka `Detect != null → false`; kawałki Line/Arc/otwarta Polyline (+Z), przy skanie: model space, warstwa obrysu; Build; jedna transakcja: nowa zamknięta Polyline (warstwa obrysu, właściwości z 1. kawałka, Elevation obrysu), tag / kopia XData RC_GA, Erase kawałków; kontrola `Detect != null` po zapisie. `EnsureForCommand(doc, slabId, prefix)` — LockDocument, try/Log.Error, komunikaty z prefiksem.

**A4. Podpięcie:** (1) `GaImportEngine.CloneInto` — zebranie kawałków na SlabLayer, po Commit `TryClose`, ostrzeżenia z „(rzut dolny/górny)”, `Result.NibClosed`, komunikat w `GaImportCommands.PrepareFromGa`; (2) `SlabPicker.PickOrDraw` l.49 — po wskazaniu obrysu `EnsureForCommand(..., "[AutoRebar]")` (wszystkie RC_GENERATE / RC_GENERUJ_*; tryb Draw pomijany); sukces: „[AutoRebar] Nib: domknięto linię uskoku krawędzią obrysu płyty (…)”, porażka: „[AutoRebar] Nib: nie domknięto — … Płyta bez nibu.”; (3) opcjonalnie `SectionCommands.Section` przed `PrepareSection` l.51.
Odrzucone: „wirtualny” nib w `NibDetector.Detect` (8 wywołań + S-REG, niewidoczny dla użytkownika).

**A5. Wynik:** zamknięty nib — bez zmian; zmienia się tylko płyta z otwartą linią uskoku (dziś „bez nibu”). S-REG: 0 nowych różnic (do potwierdzenia przebiegiem).

**A6. Testy jednostkowe** `tests/NibLoopBuilderTests.cs` (+ Compile Include w csproj) — 23 przypadki: 458, 321, 527, GARAGE 321, 324-325, 466, 319, 528-529, 539 (dane z nib2.py), przerwa 1,5 mm (domyka) / 12,5 mm (komunikat, także przy pl-PL), przesunięcie 1 mm w narożniku, rozgałęzienie, linia 20 mm od krawędzi, brak kawałków, dwie otwarte polilinie w pętlę, nib lewy+prawy, przedziały zachodzące, przecięcie obrysu, łuk (bulge, odwrócenie), koniec na łuku obrysu, permutacja kawałków, scalanie współliniowych.

**A7. Testy BRX** `PlanCNibTests.cs`, `planc-nib.scr`, `run-planc-nib.ps1`: N1 (458), N2 (ręczny zamknięty nib — bez zmian), N3 (przerwa 12,5), N4 (Polyline+Line), N5 (UNDO), N6 (mini-GA DXF → Import, NibClosed == 2, brak duplikatów przy powtórzeniu).

**A8. Ryzyka:** UNDO (zapis tylko w komendzie, w grupie UNDO komendy; domknięcie zostaje, gdy generowanie się nie uda — cofa Ctrl+Z); brak XLink; stare rysunki bez zmian schematu; brak stanu statycznego; fałszywy nib (dowolna otwarta linia na warstwie obrysu w pasie ≤256 mm z końcami na obrysie) — w danych Holbrook brak; FindHoles nie myli; Normal −Z; linie na innej warstwie pomijane.

**A9. Pytania do użytkownika:** (1) tolerancja 2 mm? (2) zawinięcia nibu na sąsiedniej krawędzi (321: 800/1477 mm) odtwarzane dosłownie z GA → UB / T IN NIB / ±1 jak przy ręcznym nibie — OK? (3) AutoRebar: domykać automatycznie z komunikatem (rekomendacja) czy pytać? (4) szerokość nibu z GA — ROZSTRZYGNIĘTE: płyta jak w GA, bez offsetu. (5) RC_SECTION też domyka?

## B. Garaże
**B1. Ustalenia:** `IsPlotLabel` (l.558, `^PLOTS?\s+\S`) — garaży nie widać; `PunchingReport.PlotRx` (l.39) + `StartsWith("PLOT")` (l.69) pomijają „GARAGE 321 (4 piles)” → w raporcie p1 13 pali G32101…G52605 dopisuje się do PLOT 538 (23 zamiast 10). Błąd wtórny: `PunchingAutoEngine.Apply` l.265 kasuje MANUAL-NOTE płyty nie-„PLOT” przy przebiciu innej płyty. Działa bez zmian: mapy (GuessPlot), AnalysisStore, NormPlot, Add l.90, GaTitleEngine (TITLE_1, GA3215→RC3215), BbsModels.ExtractPlotSuffix, GaNotesText, tagi RC_GA/RC_PH, ID pali. `GaTitleEngine` l.134 (`\bPLOT\s+(\d+)`) bez zmian (zapas).
**B2. Zmiany:** nowy `src/Core/PlotLabelRules.cs` (czysty): `IsPlotLabel` `^(?:PLOTS?\s+\S|GARAGES?\s+\d)` IgnoreCase; `TryParseReportHeader` (PLOT — dokładnie dzisiejsza logika; + GARAGE z „(n piles)”); `IsReportPlotKey`. `GaImportEngine` l.87/89 → PlotLabelRules, usunąć prywatne IsPlotLabel, komunikat l.128. `PunchingReport`: wydzielić `Parse(rows, warnings)`, l.68–74 → `TryParseReportHeader`, PlotRx do PlotLabelRules. `PunchingAutoEngine` l.265 → `IsReportPlotKey`, l.165 komunikat. Teksty: MapImportCommands:51, PunchingPlotPickerDialog, GaImportCommands:43.
**B3. Testy jednostkowe:** csproj + PlotLabelRules.cs, PunchingReport.cs, XlsxReader.cs; `PlotLabelRulesTests` (PLOT/PLOTS/GARAGE/GARAGES; fałsz „GARAGE DOOR”, „SSL=…”; nagłówki raportu); `PunchingReportParseTests` (ogon p1: 4 płyty, PLOT 538 = 10 pali, garaże 4/4/5, brak ostrzeżeń; raport tylko PLOT — identyczny jak dziś).
**B4. BRX:** G1 (mini-GA PLOT 321 + GARAGE 321 → 2 płyty), G2 (MANUAL-NOTE garażu zostaje).
**B5.** S-REG bez scenariusza GA/przebicia → 0 różnic (potwierdzić). Stare rysunki bez zmian.

## C. POPRAWKI_WZORCA.md
- 22: Płyty „GARAGE …” w Prepare GA i raporcie przebicia (pale garaży dopisywane do poprzedniego plotu, MANUAL-NOTE kasowana) — błąd wzorca.
- 23: Automat domykania nibu — nowa funkcja (życzenie użytkownika), działa tylko gdy NibDetector nie znajduje zamkniętego nibu.
- Punkt B do `docs/UWAGI_DLA_KOLEGI_2026-10-09b.md`.

## D. ARCHITECTURE.md
Sekcja 2 (NibLoopBuilder, NibAutoClose, PlotLabelRules), 4 „Nib” (automat, 2 mm, bramka), 4b „PLOT / GARAGE”, 4d (l.443).

## E. Testy ręczne
A: Prepare GA 458 → „nib domknięty: 2”, zamknięta polilinia 4 wierzch.; Mesh → UB NIB, T IN NIB; 321/527 zawinięcia; 324-325, 525, GARAGE 321, 319; bez zmian dla ręcznego nibu; przerwa 10 mm → komunikat, 1 mm → domyka; Ctrl+Z; brak duplikatów przy ponownym Prepare GA.
B: Prepare GA GARAGE 321 (lista, opis, TITLE_1, RC3215); Import Analysis + Punching GARAGE 321 (tagi G32101–04); PLOT 538 → 10 pali bez ostrzeżenia; stary rysunek PLOT bez zmian.

## F. Kolejność
B → A (NibLoopBuilder + testy → NibAutoClose → podpięcie) → BRX → dokumenty → build, dotnet test, S-REG (gdy BricsCAD wolny). CRLF w src.
