# Plan: przeniesienie wybranych zmian kolegi (aba0ed1..bd3efa2) do `nasmart-main`

Status: PROJEKT — czeka na recenzję i akceptację użytkownika. Decyzje D1–D5: `docs/ANALIZA_ZMIAN_KOLEGI_2026-10-09.md`.

## Założenia
- Przenosimy ręcznie (bez merge), 8 kroków; każdy budowalny i testowalny osobno, commit robi użytkownik po teście.
- Kroki bez zmiany wyniku najpierw (K1–K4), potem zaakceptowane zmiany wyniku (K5 przepięcie znacznika płyty przy COPY, K6 #12, K7 B3), na końcu dokumenty (K8).
- Cherry-pick tylko `git cherry-pick -n <sha>` (agent nie commituje); przy konflikcie z `-n` po rozwiązaniu `git cherry-pick --quit`.
- Build do staging: `dotnet build src/BricsCadRc.csproj -c Release "-p:OutputPath=..\bin-staging\"`; BRX/S-REG z `-PluginDll bin-staging\NaSmartRc.dll`; smoke tests z `-p:BuildProjectReferences=false`.
- Wzorzec S-REG zostaje `aba0ed1`; B3 jako oczekiwana różnica.

Ustalenia z kodu:
- `#12` dla pręta-bloku już działa (`SingleBarGripOverrule.DragEnd/RecordEdit` 328–348 bez zapisu w dragu; `SingleBarBlock.ApplyEndLength` przy `TooShort` 376–383 wychodzi bez zapisu) — dla bloku tylko test; poprawiamy polilinię.
- `IsUndoing` jest tylko w `BarBlockEraseOverrule` (AnnotMoveOverrule.cs:1034), **nie w `BarEraseCascade.Run`** (1151) — patrz P2.
- Nowe pliki Planu C nie mają pustych `catch {}`.
- Brak wykrywania trybu skryptowego w kodzie — dodajemy (K4).
- `CloneRemap` **nie przepina `RC_AUTOREBAR_SLAB`**; komentarz AutoRebarEngine.cs:1704 jest nieprawdziwy.

## Pytania do użytkownika
- **P1 (D3):** pręt-polilinia: długość z gripa/STRETCH bez zaokrąglania jak wzorzec (`BarGeometryWatcher` 547–548, 636–637)? 5 mm dziś tylko w bloku. Rekomendacja: polilinia bez zmian (zaokrąglanie = nowa różnica wyniku F1e/F4d).
- **P2:** dodać `if (dbObject.IsUndoing) return;` do kaskady usuwania pręta (polilinia + blok), jak w e7b600d? Rekomendacja: tak, w K3 (+ ponowny W1 2A_UNDO/REDO).
- **P3 (D5):** okno „identyczny pręt” pokazuje też pręty o tym samym numerze (kopie)? Rekomendacja: tak, bez prętów bez numeru (`ExtractPosNr ≤ 0`).

## Odpowiedzi użytkownika (2026-10-09) — plan ZAAKCEPTOWANY („zrób całość”)
- P1: TAK — polilinia bez zaokrąglania długości z gripa (5 mm tylko blok).
- P2: TAK — `IsUndoing` w kaskadzie usuwania pręta (polilinia + blok).
- P3: TAK — okno pokazuje też kopie o tym samym numerze (bez prętów bez numeru).
- P4: TAK — K5: najpierw test P3c na obecnym kodzie; jeśli błąd jest — poprawka (z wymiarami ADD).
- P4b: (b) — COPY samych rozkładów bez płyty: znacznik płyty czyszczony na "" → rozstrzyga test punktu w obrysie.

## Poprawki po niezależnej recenzji (Claude, 2026-10-09) — obowiązują ponad treścią kroków
1. **K5 wstrzymany → pytanie P4.** Nie jest objęty D1–D5. Najpierw test BRX P3c na OBECNYM kodzie — musi pokazać błąd (BricsCAD czasem sam tłumaczy 1005). Jeśli nie pokaże: K5 = tylko test. Jeśli K5 wchodzi: objąć też wymiary ADD (`XAddDimApp`, AutoRebarEngine.Add.cs:418–421, kasowane w `EraseOldAdd` 603–620).
2. **K3 back-link [16]:** w gałęzi `CloneMap.TryResolvePending` (AnnotMoveOverrule.cs:1054–1059) NIE sprawdzać [16] (opis-klon ma jeszcze [16] oryginału) albo akceptować [16] = źródło klonu. Test: COPY → usunięcie kopii przed końcem komendy → brak osieroconego opisu.
3. **K6:** zamiast `SetPointAt(pre[i])` z WCS (`_preOutline` = `GetPoint3dAt`, BarGeometryWatcher.cs:165–167) — zapamiętywać `GetPoint2dAt(i)` + `GetBulgeAt(i)` (OCS) i przywracać oba, albo `RebuildCompanions` z niezmienionego XData (`_preAxisStart`, rama sprzed). Gałąź `tooShort` → `continue` przed `TranslateLeader` (572–589) i przed przyjęciem długości (637–640). Pręty gięte: sprawdzić w S-REG, czy 08b/08c (znacznik [15]) się nie zmieniają. Test ręczny: Ctrl+Z po odrzuceniu → pręt sprzed gripa.
4. **K7:** po cherry-picku w `AutoRebarEngine.B3.cs` usunąć `if (posNr <= 0) posNr = 1;` (omija NextAuto, 01 = UB) → `Log.Info` + `return ObjectId.Null`.
5. **K2:** lista celowych catch + AutoRebarEngine.cs:3081 (`LoadLineTypeFile`); sprawdzić nazwy w `Log.Error` w automatycznie scalonych miejscach.
6. **K4:** okno przez `Application.ShowModalWindow` (nie `MessageBox` bez rodzica); wywołanie po sprawdzeniu `testBarWithoutNumber` (BarCommands.cs:174–178), przed oknem w 181–182. Bit 4 CMDACTIVE = tylko .scr (LISP/makra pokażą okno).
7. **CLAUDE.md:** wzorzec = `aba0ed1` + B3 (`f854684`).

## Kroki

### K0. Stan wyjściowy
`git status` czysty dla `src/`. Wyniki bazowe: `dotnet test` (714/0), wszystkie `run-planc-*.ps1`, S-REG.

### K1. bd3efa2 Import Analysis — cherry-pick 1:1 (wynik bez zmian)
- `git cherry-pick -n bd3efa2` (AnalysisImportCommands, AnalysisStore, AnalysisFilesDialog, MapImportCommands, PunchingTagCommands, RibbonBuilder, ARCHITECTURE 4c').
- Ręcznie ARCHITECTURE.md:109: Setup = Prepare GA; Import Analysis; GA Texts („Reinf. Maps” znika z wstążki, `RC_IMPORT_MAP` zostaje).
- NOD `RC_ANALYSIS` — stara wtyczka ignoruje.
- Testy: build, `dotnet test`, S-REG 0 nowych różnic.

### K2. 96809d3 puste catch → Log.Error z limitem (wynik bez zmian)
- `git cherry-pick -n 96809d3`; w PluginApp.cs, BarCommands.cs, BarCopyWatcher.cs, SingleBarEngine.cs: `git checkout --ours` + ręczna zamiana na `catch (System.Exception logEx) { Log.Error("Klasa.Metoda", logEx); }` (miejsca: PluginApp:16; BarCommands:463,648,844,874,896; BarCopyWatcher:32; SingleBarEngine:78,903).
- `Log.cs`: tylko metoda `Error` z limitem 60 s; zostają `#nullable disable` i `ProductInfo.FolderName` (kompilacja w tests/ net8). Dodać czyszczenie `_lastByKey` po >1000 kluczy.
- Tylko `catch { }` i `catch { /* … */ }`; `catch { continue; }`/`return` zostają.
- Kontrola grepem — zostają tylko celowe: `Log.cs` (Write), `LayerManager.cs:110`, `BbsXlsGenerator.cs:240`.
- POPRAWKI #11 → zrobione.
- Testy: build, `dotnet test`, pełny BrxSmokeTests, S-REG 0 różnic.

### K3. #10 RC_BAR_END, back-link [16], dokumentacja bc2055e (bez zmiany wyniku)
- `EditCommands.BarEnd` (395–475): prompt „Wybierz rozkład:”; pręt (Polyline/Block wg `BarEntity.KindOf`) → `[RC] Symbol końca ustawia się na rozkładzie — wskaż rozkład, nie pręt.`; usunąć martwą gałąź zapisu do polilinii (457–471). ARCHITECTURE:212–213 bez RC_BAR_END w `Suppress()`. POPRAWKI #10 → zrobione.
- Back-link [16]: `BarBlockEraseOverrule.Erase` (1071–1074) i `BarEraseCascade.Run` (1223–1235) usuwają opis tylko gdy `[16]` puste lub `XLink.Same([16], handle rozkładu)` — wspólny helper `AnnotBelongsTo(tr, annotId, distHandle)`.
- P2 (jeśli TAK): `IsUndoing` w `BarPolylineEraseOverrule.Erase` (1103) i `SingleBarBlockEraseOverrule.Erase` (1125).
- bc2055e: ARCHITECTURE 31–34 i XLink.cs 11–13 już poprawne; poprawić komentarz AutoRebarEngine.cs:1704.
- Testy BRX: P16a, P16b; W1, W2, E3 bez regresji; S-REG 0.

### K4. e7b600d (a)+(b): okno „identyczny pręt” + komunikat po COPY (UI)
- `src/Core/ScriptMode.cs`: `IsNonInteractive()` = `/automation` w argumentach procesu LUB `RC_NO_DIALOGS=1` LUB bit 4 `CMDACTIVE`.
- Czysta klasa `IdenticalBarText` (bez Teigha) + test jednostkowy.
- (a) `BarCommands.Distribution` przed `ReinfDetailingDialog` (183): `ConfirmIdenticalBars`; `FindIdenticalBars` przez `BarEntity.ScanModelSpace(Polyline|Block)`, pomija siebie i `ExtractPosNr ≤ 0`, `PositionReconciler.SameShape`; w trybie skryptowym komunikat w linii poleceń + `Log.Info`; okno OK/Anuluj.
- (b) `BarCopyWatcher.HandleCommandFinish`: kopia `_newBars` przed krokiem 5 (`RemapCopiedBarLabels` je usuwa); po kroku 6 przy `isCopyLike` → `ReportSameNumberCopies` na `BarEntity`; `SameNumberCopyMessages` do testu.
- Tylko odczyt, filtr `Database == db`, nic po UNDO.
- Testy BRX: P-IDENT, P-SCRIPT, P-COPYMSG; S-REG 0.

### K5. Przepięcie `RC_AUTOREBAR_SLAB` przy COPY (zmiana wyniku → POPRAWKI 3c)
- `AutoRebarEngine.SlabAppName` (internal const).
- `CloneRemap.Relink` `case Kind.Dist`: `Resolve` znacznika płyty; gdy cel to Polyline → `Hex(t)`, inaczej `Keep` (ten sam rysunek: oryginał; inny: "" → zapasowy test punktu w obrysie); `SetLinkIfChanged`.
- Komentarz CloneRemap 15–21, ARCHITECTURE 228–231.
- Dziś: ponowne generowanie na oryginale kasuje rozkłady skopiowanej płyty.
- S-REG: F3g + expected-diffs `{polyline,block}/03c-copy-znacznik-plyty.txt` (`# bug: 3c`). Test BRX P3c.

### K6. #12: odrzucony grip przywraca pręt (D4)
- `BarGeometryWatcher.TryEndStretch` (224): `out bool tooShort` (gałąź 269–273).
- `ProcessPending` (565): przy `tooShort` i zgodnej liczbie wierzchołków → `SetPointAt(i, pre[i])`, commit, continue; komunikat bez zmian.
- Inne odrzucenia (gięte, „nie da się gripem”) bez zmian (expected-diffs 08b/08c).
- Pręt-blok — tylko test. ARCHITECTURE 249–252, 279–280; POPRAWKI #12 → zrobione.
- S-REG: F12 + expected-diffs `{polyline,block}/12-grip-za-krotki.txt`.

### K7. f854684 B3 ADD (D1)
- `git cherry-pick -n f854684` (AutoRebarEngine.B3.cs, AutoRebarCommands, MenuCommands, AutoRebarEngine ~2758, ARCHITECTURE 4h — przy konflikcie wstawić ręcznie przed „## 5”).
- Sprawdzone: numer przez `CreateNewTemplate` → `PositionCounter.NextAutoFree` → `NextAuto`; skan przez `BarEntity.ScanModelSpace`; pręt przez `SingleBarEngine.PlaceBar` (Blok → pręt-blok); warstwa jak we wzorcu.
- ARCHITECTURE sekcja 4: reguła B3 ADD (D1); 103: B3 w słowach kluczowych RC_GENERATE. CLAUDE.md „Wyjątek”: zmiany przyjęte decyzją użytkownika (D1). POPRAWKI wiersz 14.
- S-REG: F14 (płyta L), expected-diffs `{polyline,block}/14-b3-add.txt`.
- Kontrola zgodności z kolegą: worktree `f854684`, `run-regression.ps1 -WzorzecDll … -ProposeExpected` — brak linii F14 w `proposed-*`.
- Testy: build, `dotnet test`, E3, F3, S-REG.

### K8. Dokumenty
- `docs/UWAGI_DLA_KOLEGI_2026-10-09b.md`: kształt 46 w 9d565be `{A,B,C,E}` → `{A,B,C,D,E}` — stare E odczytane jako D, E=0, błędna długość/BBS bez ostrzeżenia; parametry tylko dopisywać na końcu lub migrować. Opcjonalnie: komentarz `TagWithSlab`.
- Status w `ANALIZA_ZMIAN_KOLEGI_2026-10-09.md`.

## Ryzyka
UNDO/REDO (zapisy w CommandEnded, nic po undo; REDO po COPY nie odtwarza przepięcia — znane), grip (K6 dopiero w CommandEnded), COPY/PASTE (XLink, Resolve), stare rysunki (`ReadField`), wiele rysunków (filtr db), reentrancja (K4 tylko odczyt; K5 pod Suppress).

## Testy automatyczne
Nowy runner `tools\BrxSmokeTests\run-planc-port.ps1` + `planc-port.scr` + `PlanCPortTests.cs`: P16a, P16b, P-IDENT, P-SCRIPT, P-COPYMSG, P12, P3c. Po każdym kroku: `dotnet test`, runner kroku, S-REG; po K2 i K7 pełny zestaw. `RegFixCommands.FixCases`: F12 (23), F14 (24), F3g (25) na końcu.

## Testy ręczne w BricsCAD
1. K1: Import Analysis → okno, mapy; Punching bez pytań; RC_IMPORT_MAP osobno.
2. K2: zwykła praca → brak nowych błędów; log bez serii.
3. K3: RC_BAR_END na pręcie → „wskaż rozkład”; na rozkładzie działa; kopia bez opisu usunięta → opis oryginału zostaje; Ctrl+Z.
4. K4: RC_DISTRIBUTION na pręcie z „bliźniakiem” (też Blok) → okno; Anuluj/OK; COPY → komunikat; MIRROR z usunięciem → brak.
5. K5: B1 na płycie, COPY płyty z rozkładami, B1 ponownie na oryginale → rozkłady kopii zostają; Ctrl+Z.
   RC_SECTION (i T1/T2 liczone od rzutu dolnego) na oryginale i na kopii — każda płyta widzi tylko swoje rozkłady.
6. K6: pręt 00 grip/STRETCH poniżej 50 mm → wraca; Ctrl+Z/Ctrl+Y; blok; pręt obrócony 30°.
7. K7: płyta L i U → B3 i Mesh → B3 ADD w narożnikach; ponownie → zastępuje; Opening Detail → 4 narożniki; Blok; RC_BBS.
