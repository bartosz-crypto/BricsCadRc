# Plan: bezpiecznik aktualności przekrojów RC_SECTION (RC_SECTION_CHECK / RC_SECTION_UPDATE)

Status: etapy 0–1 zacommitowane (acfa036); etapy 2–4 zaimplementowane (2026-10-10, staging) — czekają na recenzję diffu i testy ręczne użytkownika.
Decyzje użytkownika (2026-10-10): ostrzeżenie przed BBS i wydrukiem/PDF — TAK; chmurka „NIEAKTUALNY” na niedrukowanej warstwie — TAK; UPDATE zachowuje ręczne poprawki — TAK.

Ustalenia: RC_SECTION jest we wzorcu aba0ed1 → geometria przekroju bit w bit bez zmian; dochodzą tylko dane (XData/Xrecord) i warstwa chmurki. Anulowanie wydruku: `DocumentCollection.DocumentLockModeChanged` + `e.Veto()` (BrxMgd), `PlotReactorManager.BeginPlot`, `Publisher.AboutToBeginPublishing` (bez Cancel); `CommandWillStart` bez anulowania — skuteczność Veto do sprawdzenia spikiem (krok 0).

## Odpowiedzi użytkownika (2026-10-10): P1–P8 — WSZYSTKO WG REKOMENDACJI. Plan zaakceptowany (z zastrzeżeniem uwag recenzji).

## Poprawki po niezależnej recenzji (Claude, 2026-10-10) — mają pierwszeństwo
1. ResultDigest: w kanonicznej serializacji U − UMin (SectionPlanner.cs:142 U bezwzględne) — inaczej MOVE rzutu daje fałszywy alarm.
2. Digest nie obejmuje Views/P1/P2/LookSign/litery (Section.cs:397-421): różnice VIEWS/CUT/litery ZAWSZE → NIEAKTUALNY (rozjemca rozstrzyga tylko składniki wejścia planera).
3. Położenie cięcia przy przeliczeniu: z części MARK (polilinia znacznika na rzucie wskazanym); brak znacznika → wartości bezwzględne. (Nie względem MinPoint.)
4. „Użyty” rozkład oznaczać w chwili dodania kropki/pręta (SectionPlanner.cs:240, 298, 309), PRZED AlignMesh (l.319), referencją do Dist.
5. CHECK wykrywa otwarty nib do domknięcia (sprawdzenie bez zapisu, NibLoopBuilder) → status „do sprawdzenia/NIEAKTUALNY”.
6. Każda część ma [4] GUID przekroju; Store zgłasza „części bez tytułu”; CloneRemap: tytuł sklonowany bez części → usuń RC_SECTION i Xrecord z klonu; cały przekrój sklonowany → nowy GUID.
7. Rekord tytułu trzyma listę handle części (przepinaną w CloneRemap); UPDATE usuwa tylko części potwierdzone w obu kierunkach (back-link).
8. Wydruk: wariant podstawowy do spiku = Veto bez okna w handlerze; pytanie na Idle, po „Tak” ponowienie komendy przez SendStringToExecute z jednorazową flagą obejścia. Okno w handlerze tylko jeśli spike pokaże, że bezpieczne.
9. Bez cache (licznik zmian) w etapie 1 — liczyć na żądanie, logować czas; cache dopiero po pomiarze (wtedy licznik obejmuje też UNDO).
Drobne: normalizacja −0.0 → 0.0; klucz PILE z r, bez nazwy; SectionPlanner.cs do csproj testów; złoty test = zrzut pełnego Result na ≥5 wejściach (nib+UB03+T IN NIB, otwór+urwanie, pal, cięcie pionowe LookSign −1, FitShape) na NIEZMIENIONYM kodzie + przypomnienie o Version; PUBLISH rysunków nieotwartych — ograniczenie; litera w tytule ≠ [3] → ostrzeżenie; położenie przekroju z mediany (tytuł, wierzchołek SLAB) względem topLeft; nowe pliki CRLF; czyste klasy bez Teigha.
**Etapy:** 0 spike (Veto, filtr komend, okno/Idle, chmurka niedrukowana w PDF); 1 złoty zrzut + Used*, czyste klasy + testy, zapis rekordu w RC_SECTION, RC_SECTION_CHECK + chmurka, CloneRemap, RC_BBS ConfirmBeforeOutput, ARCHITECTURE; 2 SectionGuard (wydruk/PDF/PUBLISH, komunikat przy otwarciu), pomiar czasu; 3 RC_SECTION_UPDATE z przesunięciami + UNDO/REDO; 4 adopcja starych przekrojów. Po każdym etapie: build, BRX, w4, S-REG.

## Pytania do użytkownika
- P1 Grubość/SSL w oknie: równe opisowi płyty → CHECK bierze bieżący opis (zmiana → NIEAKTUALNY „225 → 300”); nadpisane ręcznie → zostaje nadpisanie, zmiana opisu = tylko informacja.
- P2 Nowa wersja programu (inny wynik planera przy tym samym wejściu) → „aktualny (nowa wersja rysowałaby inaczej)”, tylko w CHECK.
- P3 Przy otwarciu rysunku tylko komunikat (bez zapisu chmurek — DBMOD/UNDO); chmurki ustawiają CHECK/UPDATE/RC_SECTION/RC_BBS.
- P4 COPY całego przekroju → kopia samodzielnym pilnowanym przekrojem (CloneRemap); CHECK ostrzega „litera B użyta 2×”.
- P5 Opis usunięty ręcznie → UPDATE odtwarza z komunikatem.
- P6 Stare przekroje bez odcisku → „nieznany”; UPDATE = adopcja: cięcie/strona/litera ze znaczników SD-SECTION, obiekty starego przekroju wskazuje użytkownik oknem, okno parametrów; nic nie kasowane po zgadywaniu.
- P7 Warstwa chmurki `RC-SECTION-STATUS`, kolor 1, Plottable = false.
- P8 Gdy Veto nie działa: ostrzeżenie w CommandWillStart „…wybierz Anuluj w oknie wydruku”; -PLOT → Esc.

## Odcisk
Składniki „istotnego wejścia” wybrane przez planer + skrót wyniku jako rozjemca.
- Hash całego Input — odpada (wszystkie rozkłady płyty → fałszywe alarmy). Sam Result — brak przyczyny, zależny od kolejności.
- Składniki: `Result.UsedDists` (rozkłady z ≥1 kropką / prętem w widoku), `UsedPiles`, `LevelDiameters` (dB1, dT1 — z WSZYSTKICH Main warstwy, l.179), `Segments`; klucze: CUT (względem MinPoint obrysu), PARAM (okno), SRC.THK/SSL (+flaga nadpisania), SEG#i, LVL, DIST:<handle> (Mark, PosNr, LayerCode, Kind, AlongX, X0, Y0, LengthA, BarsSpan, Count, Spacing, nominalny z Mark, Diameter, ShapeCode, SymbolSide, A–E wzorcowego, skrót ShapeLocal; zapasowe dopasowanie po PosNr/LayerCode/Kind/kierunku/najbliższym położeniu), PILE:<x,y> (nazwa pala do opisu), VIEWS, META (SectionPlanner.Version, format).
- Serializacja: linie posortowane ordynalnie, `Math.Round(x,1).ToString("0.0", Invariant)`, współrzędne względne (MOVE rzutu bez alarmu), SHA-256 UTF-8; ResultDigest z kanonicznej serializacji Result.
- Werdykt (`SectionFingerprint.Compare`): hash równy → aktualny; różny → porównanie składników z tolerancją 0,15 mm; różnice, ale digest równy → aktualny („zmiany bez wpływu na rysunek”); inaczej NIEAKTUALNY z przyczynami („zmieniony rozkład poz. 07 B1 (rozstaw 200 → 150)”, „nowy/usunięty rozkład”, „wymiary pręta poz. 03 (A 700 → 750)”, „usunięty/przesunięty pal P32104”, „zmieniona grubość”, „zmieniony SSL”, „obrys/nib/otwór na linii cięcia”, „rzuty płyty”); Version różna + składniki równe + digest różny → P2.

## Zapis i identyfikacja
- Głowa = MText tytułu: XData `RC_SECTION` [1] FormatVersion, [2] GUID, [3] litera, [4] SlabPicked (XLink 1005), [5] SlabOuter (XLink), [6] Self (XLink — wykrycie kopii nieprzepiętej). Reszta w Xrecordzie `NA_RC_SECTION` w słowniku rozszerzeń tytułu (limit 16 KB XData): p1, p2 (bezwzgl. + względne), punkt patrzenia, topLeft, okno + flagi nadpisania, MinPoint, hash, ResultDigest, Version, składniki, lista części (rola, klucz, liczba, pozycje domyślne opisów).
- Części: XData `RC_SECTION_PART` [1] link do tytułu (XLink), [2] rola (SLAB, BAR, DOT, PILE, DIM, LBL, LDR, ARW, SSL, MARK, MARKTXT, CLOUD), [3] klucz. Bez grup (PICKSTYLE = 1 utrudniłby przesuwanie opisów).
- CloneRemap: nowe rodzaje SectionHead/SectionPart; część z tytułem sklonowanym razem → klon, bez tytułu → zwykły obiekt (RC_SECTION_PART zdjęte — P-b rozstrzygnięte); [6] → własny handle; [4]/[5] płyta sklonowana → klon, nie → bez zmian w tym samym rysunku, inny rysunek → "". UPDATE przerywa, gdy części danej roli więcej niż zapisano.
- Stara wtyczka kolegi: ignoruje nowe RegApp/słowniki; jej przekroje = „nieznany”.

## Kroki
0. Spike BRX (`PlanCSectionSpike.cs`, `planc-sgspike.scr`, `run-planc-sgspike.ps1`): lista komend w DocumentLockModeChanged (PLOT, -PLOT, PRINT, EXPORTPDF, -EXPORTPDF, PUBLISH, -PUBLISH), czy Veto przerywa (-PLOT do PDF → plik nie powstaje), PUBLISH w tle, odczyt bazy i okno w handlerze, PlotReactorManager, czy warstwa non-plottable nie trafia do PDF.
1. `SectionPlanner.cs`: pola na końcu `Dist` (SourceHandle, SourceDims, SourceBarHandle), `Result.UsedDists/UsedPiles/LevelDiameters`, `const int Version = 1`; geometria bez zmian — NAJPIERW złoty ResultDigest 3 wejść na niezmienionym kodzie + test.
2. Czyste klasy (+ csproj testów): `SectionFingerprint`, `SectionRecord` (ToLines/Parse z tolerancją starszych rekordów), `SectionLabelCarry`.
3. `AutoRebarEngine.Section.cs`: PrepareSection wypełnia Source*, SectionContext (+PickedHandle, OuterHandle, MinPoint); `RecomputeSection(db, rec)` (tylko odczyt; ta sama droga co tworzenie); DrawSection z XData części, opcjonalne offsety opisów; `DrawCloud`/`RemoveCloud`; RegApp tylko przy zapisie.
4. `SectionStore.cs`: ReadAll (jeden przebieg; szybkie wyjście bez RegApp i starych tytułów), zapis/odczyt, statusy Aktualny/Nieaktualny/Osierocony (+ podpowiedź obrysu w tym samym miejscu)/Nieznany/Kopia/Uszkodzony.
5. `SectionCommands.cs`: RC_SECTION zapisuje rekord (z już policzonego planu); RC_SECTION_CHECK (lista + synchronizacja chmurek, bez zapisu gdy brak zmiany stanu); RC_SECTION_UPDATE [Wskaż/Wszystkie nieaktualne] (osierocony z kandydatem → pytanie; NibAutoClose; Recompute; offsety opisów; przesunięcie całego przekroju; usunięcie starych części z zabezpieczeniem liczności; DrawSection; nowy rekord; jedna transakcja). Opcjonalnie przyciski na wstążce.
6. `SectionGuard.cs`: licznik zmian per Database (ObjectAppended/Modified/Erased, bez otwierania obiektów) + pamięć wyniku; przy otwarciu (Idle) komunikat (ScriptMode: tylko log, bez SendStringToExecute); wydruk przez DocumentLockModeChanged (filtr komend ze spiku, raz na komendę najwyższego poziomu, `_inGuard`, okno Tak/Nie → Veto; ScriptMode: komunikat) lub fallback P8; RC_BBS: `SectionGuard.ConfirmBeforeOutput`.
7. CloneRemap (reguły wyżej).
8. ARCHITECTURE.md (schematy RC_SECTION, RC_SECTION_PART, Xrecord; moduły; sekcja 4g; Version).

## Ręczne poprawki przy UPDATE
Dopasowanie opisów po tekście i rodzaju (kropka / pręt w widoku / T IN NIB), zachłannie po najbliższej pozycji domyślnej ≤ 300 mm; przesunięcie = bieżąca − domyślna → nowa domyślna + przesunięcie; leader: tekst i początek przesunięte, grot tylko o dU gdy zostaje na pręcie.
Zachowane: przesunięcia opisów numerów, tytułu, liter znaczników, całego przekroju; ręczne dodatki (nie są częściami — UPDATE ich nie dotyka).
Niezachowane: zmieniona treść opisu, edycje geometrii, przesunięte wymiary (ew. etap 2), osobno przesunięte leadery/groty, zmiana warstwy/koloru/stylu, opisy usunięte ręcznie (P5 — odtwarzane), nowe pozycje (domyślnie).

## Ryzyka
Fałszywe alarmy (ograniczone: składniki z planera, tolerancja, rozjemca digest, współrzędne względne, dopasowanie po PosNr); wydajność (szybkie wyjście, licznik zmian, log czasu, komunikat > 2 s); UNDO/REDO (zapisy tylko w transakcji komendy); grip (brak overrule); COPY/PASTE (CloneRemap, [6], zabezpieczenie liczności); stare rysunki (nieznany, parser z tolerancją); wiele rysunków (stan per Database, e.Document); reentrancja (filtr komend, `_inGuard`, PUBLISH zagnieżdżony); zapis przy otwarciu (P3).

## S-REG
Zero różnic: RegressionDump nie woła RC_SECTION; brak RegApp → szybkie wyjście, brak zapisów i nowej warstwy; pola planera nie zmieniają Result (złoty digest). Bez nowych expected-diffs.

## Testy
Jednostkowe: SectionFingerprintTests (determinizm, pl-PL, SHA stałe, rozstaw → NIEAKTUALNY, daleki rozkład → aktualny, dB1, pale, grubość, segment, regeneracja = „zmieniony”, granica zaokrąglenia, digest rozjemca, Version), SectionPlannerGoldenTests, SectionLabelCarryTests, SectionRecordTests.
BRX `PlanCSectionGuardTests.cs`, `planc-sg.scr`, `run-planc-sg.ps1`: SG1 utworzenie, SG2 rozstaw, SG3 daleki rozkład, SG4 ERASE obrysu + UNDO, SG5 pal, SG6 MOVE opisu + UPDATE, SG7 UNDO/REDO po UPDATE, SG8 COPY przekroju / samego opisu, SG9 COPY płyty, SG10 dwa rysunki, SG11 stary przekrój, SG12 chmurka non-plottable i brak zapisu bez zmiany stanu, SG13 -PLOT w ScriptMode (+ Veto w trybie testowym), SG14 RC_BBS w ScriptMode; potem run-planc-w4.ps1 + S-REG.
Ręczne (Bartek): 12 scenariuszy — CHECK aktualny; zmiana rozstawu przeciętego rozkładu → NIEAKTUALNY + chmurka; daleka zmiana → aktualny; PLOT/EXPORTPDF/PUBLISH → pytanie, PDF bez chmurki; RC_BBS → pytanie; UPDATE z przesuniętymi opisami + Ctrl+Z/Ctrl+Y; usunięty obrys → osierocony; COPY przekroju; stary rysunek → nieznany/adopcja; dwa rysunki; komunikat przy otwarciu; stara wtyczka kolegi bez błędów.

## Kolejność
Krok 0 → złoty digest + krok 1 → krok 2 + testy → kroki 3–5 → UPDATE → krok 6 → krok 7 → BRX + w4 + S-REG → krok 8.

## Wyniki spiku (etap 0, 2026-10-10)
Spike: `tools/BrxSmokeTests/PlanCSectionSpike.cs`, `planc-sgspike.scr`, `run-planc-sgspike.ps1` (BricsCAD V25 /automation, osobne instancje,
logi `bin/Release/net48/planc-sgspike-*.txt` + log linii poleceń `*-cmdlog.txt`; PDF w `%TEMP%\rc-sgspike`).
1. **Nazwy komend w `DocumentLockModeChanged` (GlobalCommandName):** `PLOT`, `-PLOT`, `PRINT`, `EXPORTPDF`, `PUBLISH`, `-PUBLISH` — dokładnie
   takie (bez „_”/„.”). Zdarzenie przychodzi PRZED `CommandWillStart`, z `MyPreviousMode = Write → MyCurrentMode = Write`; koniec komendy =
   zdarzenie z nazwą z prefiksem `#` (np. `#-PLOT`, odblokowanie) — filtr musi pomijać `#…`. Komenda z LISP `(command "-PLOT" …)` daje
   najpierw zdarzenie z nazwą = całe wyrażenie LISP, potem `-PLOT`. `-EXPORTPDF` NIE istnieje w BricsCAD V25 („Unable to recognize command”).
2. **`e.Veto()` przerywa komendę:** -PLOT i EXPORTPDF zawetowane (z poziomu skryptu i z LISP) — plik PDF nie powstaje; przychodzi
   `DocumentLockModeChangeVetoed`. PLOT, PRINT, PUBLISH, -PUBLISH (komendy z oknem) zawetowane — okno się NIE pojawia, skrypt idzie dalej
   (Veto działa przed oknem). Uwaga dla skryptów: po zawetowaniu odpowiedzi do promptów (-PLOT, EXPORTPDF) trafiają do linii poleceń jako
   komendy („_N” = QNEW, nieznana komenda przerywa skrypt) — dla użytkownika interaktywnego bez znaczenia; w ScriptMode NIE wetować (tylko komunikat).
3. **Odczyt bazy w handlerze:** OK (OpenCloseTransaction, przestrzeń modelu) we wszystkich zdarzeniach, także dla komend z oknem.
   Okna w handlerze NIE sprawdzono (w /automation nie da się kliknąć) — zostaje wariant planu: Veto bez okna w handlerze, pytanie na Idle,
   po „Tak” ponowienie komendy (SendStringToExecute) z jednorazową flagą obejścia; okno w handlerze — tylko po teście ręcznym.
4. **Warstwa `IsPlottable = false` nie trafia do PDF:** 3000 linii na warstwie niedrukowanej — -PLOT 1496 B (bez nich 1496 B),
   EXPORTPDF 1497 B (bez nich 1497 B); ta sama warstwa drukowana: -PLOT 41 499 B, EXPORTPDF 42 718 B. Chmurka na `RC-SECTION-STATUS`
   (Plottable = false) nie będzie w wydruku ani w PDF.
5. **PlotReactorManager:** `BeginPlot` / `EndPlot` tylko dla -PLOT (typ Plot); EXPORTPDF ich NIE wywołuje — jedynym wspólnym punktem
   jest `DocumentLockModeChanged`. **PUBLISH:** `BACKGROUNDPLOT = 0` w /automation; zdarzeń `Publisher` (AboutToBeginPublishing,
   …Background…) nie zaobserwowano — PUBLISH zawetowany przed oknem. Publikacja w tle i rysunki nieotwarte — do testu ręcznego (ograniczenie).
6. Wniosek dla etapu 2: SectionGuard wydruku = `DocumentLockModeChanged` z filtrem {PLOT, -PLOT, PRINT, EXPORTPDF, PUBLISH, -PUBLISH}
   (bez `#…`), raz na komendę najwyższego poziomu, w ScriptMode tylko komunikat; fallback P8 niepotrzebny.

## Realizacja etapów 2–4 (2026-10-10)
- Etap 2: `SectionGuard.Register` (PluginApp) — DocumentLockModeChanged wg spiku, Veto + pytanie na Idle + ponowienie komendy;
  ScriptMode tylko komunikat; `RC_SG_TEST=veto`; komunikat przy otwarciu na Idle (bez zapisów). Pomiar: czas w logu, bez cache.
- Etap 3: `RC_SECTION_UPDATE` (`SectionUpdater`, `SectionLabelCarry`), rekord z pozycjami domyślnymi (`TITLEDEF`, `PART …|x,y|rodzaj`).
- Etap 4: adopcja przekroju „nieznany” (znaczniki SD-SECTION, obiekty wskazane oknem, `RC_SG_ADOPT` / `RC_SG_PICK` w trybie skryptu).
- Odstępstwa: (1) przesunięcie całego przekroju — mediana próbek betonu, tytuł tylko jako rozjemca przy ≥ 2 odcinkach (przy jednym
  odcinku mediana dwóch próbek = średnia — tytuł przesunięty osobno przesuwałby przekrój); (2) adopcja usuwa także znaczniki tej
  litery na rzutach (rozpoznane dokładnie po kształcie i literze — nie są wskazywane oknem); (3) przekrój bez znaczników (kopia) —
  UPDATE nie dorysowuje znaczników; (4) test otwarcia (SGO): w /automation Idle przychodzi dopiero po skrypcie — test sprawdza
  zaplanowanie przy podpięciu rysunku i woła tę samą procedurę (`SectionGuard.ProcessPending`); samo zdarzenie Idle — test ręczny.
- Testy BRX: `run-planc-sg.ps1` — SG6 / SG7 (UPDATE + UNDO / REDO), SG11b (adopcja), SG13 (-PLOT / EXPORTPDF, Veto testowe), SGO.

## Decyzja P-b (użytkownik, 2026-10-10)
Kopia części przekroju bez jej tytułu (np. COPY samego opisu) → CloneRemap zdejmuje z klonu `RC_SECTION_PART` (zwykły obiekt).
Status „części bez tytułu” zostaje tylko dla części, których tytuł usunięto (ERASE).
