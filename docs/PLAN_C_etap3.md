# Plan C, etap 3: nowe pręty jako bloki

> Autor: planista-recenzent (Claude). Recenzja: `scripts/recenzja-gpt.ps1 -Plan docs/PLAN_C_etap3.md`.
> Wzorzec: `aba0ed1`. Zmienia się tylko **typ obiektu pręta**; numery, długości, ilości, BBS, opisy i AutoRebar jak we wzorcu.
> Każda różnica bez zgody użytkownika = BLOKER.

## Status (2026-10-09)
- **3a–3e2, 3e3 (= W2): zrobione** (staging). **3f: zrobione (staging, 2026-10-09)** — decyzja użytkownika „Krok 3f — TAK”
  (docs/RANO_DECYZJE.md): `BarFormat.UserSettingEnabled = true`, `RC_BAR_FORMAT [Polilinia/Blok]` (domyślnie Polilinia,
  `src/Commands/BarFormatCommands.cs`, przycisk „Bar Format” w panelu Bars), komunikat przy starcie, `BarChangePropagator`,
  `SingleBarGripOverrule` bez blokady numerowanych prętów (00/99: grip końca i STRETCH z propagacją; gięte — tylko
  przesunięcie), `[RC TEST]` → `[RC AUTO]`, Info na każdym pręcie-bloku. Testy: `run-planc-f3.ps1` 50 / 0 (S16, S16U, S17,
  S18, STRETCH skryptem + UNDO / REDO, RC_BAR_FORMAT, AutoRebar Blok = Polilinia); E1–E3, W1, W2, W4 bez FAIL; S-REG =
  expected-diffs (bez zmian). Odstępstwa od tekstu planu: S16–S18 kroku 3f mają w testach prefiks `RCSMOKE_F3_*`
  (nazwy S16 / S17 w `planc-e3.scr` są zajęte przez testy 3e); testy ręczne M1–M16 (rozdz. 9) — do wykonania przez użytkownika.
- **3g (dokumentacja):** ARCHITECTURE.md uzupełniony (model obiektów, `RC_BAR_FORMAT`, `SingleBarBlock` / `BarFormat`,
  `SingleBarGripOverrule`, `BarChangePropagator`); do przeglądu przez użytkownika po testach M1–M16.

## 0-POPRAWKI. Po recenzji ChatGPT (zweryfikowane; mają pierwszeństwo przed resztą planu)
1. **[BLOKER] Brak numerowanych prętów-bloków u użytkownika przed 3f.** Komenda `RC_BAR_FORMAT` (i komunikat formatu
   przy starcie) wchodzi dopiero w **3f**. W 3a–3e `BarFormat` da się ustawić wyłącznie w testach (refleksja / zmienna
   środowiskowa w `RegressionDump` i smoke) — użytkownik nie może utworzyć numerowanego bloku. Dodatkowo już w 3a
   `SingleBarGripOverrule` dla pręta-bloku z numerem > 0 daje tylko grip [0] (przesunięcie) i odrzuca zmianę długości
   gripem/STRETCH, dopóki 3f nie doda propagacji.
2. **[BLOKER] SCALE musi być naprawiany automatycznie przed udostępnieniem formatu Blok** (wzorzec: watcher wykrywa zły
   obrys i odbudowuje z parametrów). Nowy krok **3e2**: w `CommandEnded` (nie po undo, z licznikiem reentrancji, przez
   `DocumentWatch`) każdy zmieniony pręt-blok ze skalą ≠ ±1 dostaje `ScaleFactors` (±1, ±1, 1) z zachowaniem znaku i
   punktu wstawienia (axis[0]) + `Rebuild` z XData; komunikat `[RC AUTO]`. Test bez `RC_UPDATE_BAR`: SCALE 2 → od razu
   geometria = XData; Ctrl+Z / Ctrl+Y spójne. 3f (udostępnienie formatu) wymaga 3e2.
3. **[BLOKER] Kształt 44 — położenie referencji:** dla 44 `BlockReference` w `insertPt` (geometria BTR budowana jak
   `BuildVisualEntities` z `Origin`), dla pozostałych `insertPt + lPts[0]` z `LocalStart = −lPts[0]`. S1 porównuje OBA
   pierścienie 44 w WCS przy identycznym punkcie wstawienia (blok vs polilinia + `RC_BAR_LINK`).
4. **[WAŻNE] Skany szablonów pomijają pręty bez numeru** (`ExtractPosNr(Mark) <= 0`, np. `H12-T`) — zarówno w puli, jak i
   w liczbie szablonów (wpływa na `CreateNewTemplate`). Dla polilinii we wzorcu numer zawsze > 0, więc wynik wzorca bez zmian.
   Test: AutoRebar z `H12-T` w strefie `rebar_top`/`rebar_bottom` = ten sam wynik co bez niego.

5. **[ROZWIĄZANE w W2 — docs/PLAN_POPRAWKI_W1_W2.md 4, `CloneMap` + `CloneRemap`]** ~~[OTWARTE, przed 3f]~~ Parowanie kopii pręta-bloku z kopią etykiety po COPY/MIRROR — heurystyka „po pochodzeniu”
   (`BarCopyWatcher.SourceOfCopy`, 3e) ma po 3 rundach recenzji znane luki: MIRROR z usunięciem źródła w zbiorze
   mieszanym (źródło już usunięte → kopia bez etykiety) oraz remis odległości przy dwóch identycznych kopiach
   (wynik zależny od kolejności). Rozwiązanie docelowe: krok **3e3** — mapa oryginał → klon z
   `Database.BeginDeepCloneTranslation` / `IdMapping` (dla prętów, etykiet i rozkładów, także `[18]` rozkładu),
   bez heurystyk odległości; remis/niejednoznaczność → brak przypisania dla wszystkich uczestników. Dotyczy tylko
   zbiorów z prętami-blokami (zbiór samych polilinii = pętla wzorca). Warunek odblokowania formatu Blok (3f).
   **Stan po W2:** mapa z `BeginDeepCloneTranslation` / `InsertMappingAvailable` (spike W2-0) przepina `[13]`/`[1]`, `[18]`,
   `[11]`/`[16]` i `RC_BAR_LINK` dla polilinii i prętów-bloków JEDNAKOWO (poprawki #3, #6, 3b — także zbiór samych polilinii,
   różnice względem wzorca w expected-diffs 03/03b/06); heurystyki `SourceOfCopy` / odległości grota — tylko zapas dla obiektów
   spoza mapy. Warunek 3f spełniony w zakresie parowania (MIRROR Y w BricsCAD V25 odbija w miejscu — nie usuwa źródła).

## 0. Warunki wstępne
- Etap 2 zamknięty (S-REG 0 różnic, smoke E2 PASS).
- **Etap 1, testy ręczne T0–T16 muszą przejść przed krokiem 3f** (gripy dla użytkownika). Kroki 3a–3e mogą iść wcześniej —
  przy domyślnym przełączniku (1.1) nie zmieniają działania wtyczki.
- Wynik T9 (wklejanie, nazwy BTR) potrzebny tylko przy ewentualnej zmianie stałej z p. 2.

## 1. Zadanie, założenia, decyzje
Wszystkie miejsca tworzenia pręta (RC_BAR, szablony AutoRebar B/T/UB/ADD/T IN NIB/trimmer, 501/502 przebicia) tworzą
`BlockReference` do własnego BTR, XData `RC_SINGLE_BAR` [0]–[13] + [14]=2 na referencji, zawartość BTR = obrys (dla 44 dwa),
bez XData. Edycja (RC_EDIT_BAR, RC_UPDATE_BAR, grip) przebudowuje BTR z copy-on-write; COPY nadaje kopii własny BTR;
skany szablonów widzą polilinie i bloki; pręty-polilinie działają jak dziś.

### 1.1 Przełącznik formatu `BarFormat.Current ∈ {Polyline, Block}` — jeden punkt rozgałęzienia w `SingleBarEngine.PlaceBar`
- Zapis `%APPDATA%\NaSmartRc\barformat.txt` (wzór `BbsSettings`), komenda `RC_BAR_FORMAT [Polilinia/Blok]`,
  komunikat przy starcie `[RC] Nowe pręty: polilinia|blok`.
- **Domyślnie `Polyline`** do akceptacji etapów 3–5. Powody: wymiana rysunków z kolegą (stara wtyczka, p. 8.1); S-REG tą samą
  DLL w obu trybach; wycofanie bez przebudowy DLL; etapy 4–5 jeszcze nie istnieją.
- `RegressionDump` ustawia `BarFormat` na czas sesji (refleksja, zmienna `RC_REG_BARFORMAT`); we wzorcu krok pomijany.

### 1.2 Gripy: etap 3 z propagacją
- 3f: zapis gripa/STRETCH pręta-bloku 00/99 woła łańcuch jak wzorcowy `BarGeometryWatcher.ApplyEndStretch`
  (`BarGeometryWatcher.cs:262-292`): `PositionReconciler.ReconcileAfterGeometryChange` → `PropagateToDistributions(legacyPosNr)`
  → grot etykiety → `UpdateBarLabelCount` + komunikat `[RC AUTO]`.
- **Kształty gięte i 44:** tylko grip [0] (przesunięcie) i STRETCH całości; długość przez RC_EDIT_BAR.
  **BLOKER do naprawy:** `TestBarBlockEngine.BuildEntities` (`:99`) i `DrawPreview` (`SingleBarGripOverrule.cs:254`) mają na sztywno
  kształt `"00"`, a `IsApplicable` łapie każdy pręt-blok v2 — grip na UB przerobiłby go na prosty.
- Reguły długości jak we wzorcu (`TryEndStretch`, `BarGeometryWatcher.cs:189-206`): minimum `max(2d, 50)`; `TotalLength` przeliczane
  `shape.CalculateTotalLength`, chyba że `LengthOverridden`. Dzisiejsze `ApplyLength` wpisuje `TotalLength = newLength`
  (`TestBarBlockEngine.cs:265`) — do poprawy. Zaokrąglenie 5 mm (decyzja użytkownika; pytanie 5).

### 1.3 Poza zakresem
- E5: etykieta za MOVE pręta-bloku, automatyczne cofanie SCALE, `BarGeometryWatcher` dla bloków. E6: konwersja starych rysunków.
- **Z E5 do E3:** grot etykiety na bloku (`RcMLeaderOverrule`, `RC_FIX_LABEL`) — wspólny helper `ClosestPoint`; ścieżka polilinii bit w bit.

## 2. Nazewnictwo BTR: domyślnie `*U`
- Stała `SingleBarBlock.UseAnonymousBtr = true`. PASTE/INSERT zawsze dają nową kopię `*U` — brak kolizji nazw (ryzyko T9b dla nazwanych).
- Wariant nazwany (tylko gdy T9 wykaże problem z `*U`): nazwy `RC_BAR_<8 hex GUID>`, przy KAŻDEJ przebudowie nowy BTR, nigdy prefiks `RC_SLAB_BARS_`.
- `Explodable = false` na każdym BTR pręta (pytanie 4); copy-on-write i `EnsureUniqueBtr` kopiują flagę (`NewBtr` w `TestBarBlockEngine.cs:144-153` jeszcze nie).

## 3. Pliki i metody
### 3.1 Nowe / scalone
| Plik | Zawartość |
|---|---|
| `src/Core/SingleBarBlock.cs` (nowy) | Silnik pręta-bloku dla wszystkich kształtów (uogólnienie `TestBarBlockEngine`) |
| `src/Core/BarFormat.cs` (nowy) | `enum BarFormatKind`, `Current` (leniwy odczyt), `Set(kind, persist)`, ścieżka jak `BbsSettings` z fallbackiem legacy |
| `src/Commands/BarFormatCommands.cs` (nowy) | `RC_BAR_FORMAT [Polilinia/Blok]` |
| `src/Core/BarChangePropagator.cs` (nowy) | `Run(doc, barId, oldMark, source)`: Reconcile → `PropagateToDistributions(legacy)` → `FixLabelArrow` (przez `BarEntity.ClosestPoint`) → `UpdateBarLabelCount` → komunikat. Kopia logiki `ApplyEndStretch` 272-291. **`BarGeometryWatcher` nietknięty** |
| `src/Core/TestBarBlockEngine.cs` | Cienka nakładka: `TestMark`, `Create` testowy → `SingleBarBlock.Create`, `Describe` → `SingleBarBlock.Describe`; reszta przeniesiona |

`SingleBarBlock`: `LocalStart(bar)` = `−lPts[0]` (z `BarGeometryBuilder.GetLocalPoints`) dla ≠44, `Origin` dla 44 — **punkt wstawienia =
axis[0] w WCS**, przebudowa zachowuje axis[0] (jak `RebuildCompanions`, `SingleBarEngine.cs:590-591`); `BuildEntities(bar, db)` przez
`BuildVisualEntities` (warstwa 0, ByBlock, bez XData); `Create(db, bar, insertPt)` — BTR `*U` `Explodable=false`, `BlockReference(insertPt + lPts[0])`
bez `SetDatabaseDefaults` (jak polilinia), `Layer = LeaderLayer`, `ColorIndex = 256`, `LineWeight000`, `BarXData.Write(br, bar, 2)`, jedna transakcja;
`Rebuild(tr, br, bar)` — **zawsze** `EnsureOwnBtr` → `RebuildContents` → `RecordGraphicsModified`; `AxisStartWcs`, `AxisEndWcs`,
`AxisPointsWcs`, `IsLengthGripShape` (`00`/`99`), `ApplyEndLength` (reguły 1.2, przez `Rebuild`), `SameTransform`, `Describe`.

`BarEntity` (rozszerzenie): `ScanModelSpace(tr, db, accept)` (kolejność MS, bez usuniętych); `FirstOutlineVertex(tr)` (polilinia: `GetPoint3dAt(0)`;
blok: pierwszy wierzchołek pierwszej polilinii BTR × `BlockTransform`); `WithWcsOutline<T>(tr, f)` (blok: `GetTransformedCopy` pierwszej
polilinii BTR, `Dispose`); `ClosestPoint(tr, pt)`, `MidPoint(tr)`.

### 3.2 Zmiany w istniejących plikach
| # E2 | Plik:linie | Zmiana | Krok |
|---|---|---|---|
| 20 | `SingleBarEngine.cs:63-113` `PlaceBar` | po `EnsureAppIdRegistered`/`EnsureLayersExist`/ostrzeżeniu: `if (BarFormat.Current == Block) return SingleBarBlock.Create(...)`; ścieżka polilinii bez zmian | 3a |
| 20 | `SingleBarEngine.cs:138-158` `GetBarArrowTip` | `BarEntity.Open(Polyline\|Block)` → `ClosestPoint`; pusty `catch` (149) → `Log.Error` + fallback | 3c |
| 28 | `AutoRebarEngine.cs:1625-1648, 1750-1772, 1775-1795, 2847-2864, 2951-2963`; `AutoRebarEngine.Add.cs:324-340` | `ScanModelSpace(Polyline\|Block)`; `insPt = FirstOutlineVertex` (nie `br.Position`) | 3b |
| 30 | `PunchingTagEngine.cs:137-157` `DeleteSummaryBars` | `ScanModelSpace(Polyline\|Block)` | 3b |
| 20 | `AutoRebarEngine.cs:1868-1978`, `PunchingTagCommands.cs:92-118` | bez zmian w kodzie (przez `PlaceBar`, `GetBarArrowTip`); testy | 3c |
| 20 | `BarCommands.cs:64-117` RC_BAR | `barCenter` → `MidPoint`; `axisPts` → polilinia: `GetAxisPointsFromOutline`, blok: `AxisPointsWcs` (44: `OutlineVertices`) | 3c |
| 21 | `BarCommands.cs:1252-1386` RC_EDIT_BAR | wybór + `BlockReference`; logikę po dialogu (1301-1382) wydzielić do `BarEditApply.Apply(...)`; `RebuildCompanions` → dla bloku `SingleBarBlock.Rebuild`; grot → `ClosestPoint`; **kolejność kroków bez zmian**; pusty `catch` (1367) → `Log.Error` | 3d |
| 21 | `BarCommands.cs:1154-1246` RC_UPDATE_BAR | blok: długość z XData, `ScaleFactors` → (±1, ±1, 1) ze znakiem, `Rebuild`, potem Reconcile/Propagate/LabelCount jak polilinia | 3d |
| 27 | `BarCommands.cs:1130-1144` RC_FIX_LABEL | `as Curve` → `BarEntity.ClosestPoint` | 3d |
| 26 | `RcMLeaderOverrule.cs:154-163` | `as Polyline` → `BarEntity.Open(Polyline\|Block)` + `ClosestPoint` | 3d |
| 25 | `BarCopyWatcher.cs:48-56, 94-101, 123-189` | bloki-pręty do `_newBars` (przed `IsBarBlock`); `EnsureUniqueBtr` dla nich przy copy-like; `RemapCopiedBarLabels` przez `BarEntity`; pusty `catch` (164) → `Log.Error` | 3e |
| 5, 35 | `SingleBarGripOverrule.cs` | gięte: tylko [0]; `DrawPreview` z kształtu `bar`; `OnCommandEnded` → `ApplyEndLength` + `BarChangePropagator.Run` (numer > 0, w `_applying`, `LockDocument`); `[RC TEST]` → `[RC AUTO]` | 3f |
| 34 | `TestBarBlockCommands.cs` | `Info` na każdym pręcie-bloku; `Typ` do decyzji po T9 | 3f |
| — | `PluginApp.cs` | komunikat formatu (jedna linia) | 3a |
| — | `ARCHITECTURE.md` | moduły, przełącznik, `*U`, `Explodable`, lista miejsc | 3g |

**Bez zmian:** `BarGeometryWatcher` (E5), `EditCommands.cs:402-469` RC_BAR_END, `AutoRebarEngine.Section.cs:180-201`, `Add.cs:565-572`, kaskada usuwania.

### 3.3 Narzędzia testowe
- `tools/RegressionDump/DrawingDump.cs` tryb **bar-equiv** (`RC_REG_BAR_EQUIV=1`, dla obu zrzutów): pręt (polilinia z `RC_SINGLE_BAR` albo blok v2)
  renderowany kanonicznie (warstwa, efektywne C/LT/LW, XData [0]–[13], pierścienie wierzchołków WCS 0.01 z bulge; `RC_BAR_LINK` dołączane
  jako pierścienie pręta głównego); BTR pręta nie renderowany; [14] i typ encji pomijane. Osobne asercje dla bloków: [14]=2, rekord 15,
  `*U`, `Explodable=false`, refs=1, zawartość bez XData, warstwa 0/ByBlock.
- `RegCommands.cs`: `RC_REG_SETUP` ustawia `BarFormat`; nowe `RC_REG_EDIT` (`BarEditApply.Apply`; we wzorcu te same kroki publiczne),
  `RC_REG_PUNCH` (`PlaceSummaryBars` 2×).
- `run-regression.ps1`: trzy przebiegi — wzorzec, nasz Polyline (diff surowy), nasz Block (diff bar-equiv).
- `tools/BrxSmokeTests/PlanCStage3Tests.cs` + `planc-e3.scr` + `run-planc-e3.ps1` (`RCSMOKE_E3_*`).

## 4. Dlaczego tak
Jeden punkt rozgałęzienia w `PlaceBar`; punkt wstawienia = axis[0]; `FirstOutlineVertex` w skanach (decyzja „szablon w strefie” co do mm);
copy-on-write w `Rebuild` + unikalny BTR przy COPY. Odrzucone: E3+E4 razem (za duży diff), wydanie bez propagacji (łamie 1-POPRAWKI etapu 2),
`SetXDataFilter` w grip overrule.

## 5. Przypadki brzegowe (zgoda użytkownika, p. 11)
- RC_EDIT_BAR na obróconym pręcie: wzorzec odbudowuje poziomo (`SingleBarEngine.cs:593`), blok zachowuje obrót.
- RC_EDIT_BAR na kształcie 44: wzorzec nie przebudowuje (`SingleBarEngine.cs:581-585`), blok przebudowuje.
- Grip 5 mm vs wzorzec dokładnie.
- (Od W1/W2, docs/POPRAWKI_WZORCA.md) RC_EDIT_BAR obróconego pręta, kształt 44 i powiązania kopii (COPY/MIRROR/ARRAY) — polilinia
  zachowuje się jak blok (poprawki #3, #4, #5, #6); dwa pierwsze punkty powyżej to już tylko opis wzorca.

## 6. Kroki (każdy: build + `dotnet test` + smoke + S-REG; odwracalny)
- **3a — fundament, zero zmian w działaniu (domyślnie Polyline):** `SingleBarBlock`, `BarFormat` + `RC_BAR_FORMAT`, rozgałęzienie w `PlaceBar`,
  rozszerzenia `BarEntity`, `TestBarBlockEngine` → nakładka, `DrawingDump` bar-equiv, komunikat w `PluginApp`. Jednostkowe: `E3-U1` `LocalStart`
  (00/11/21/13/44), `E3-U2` reguły długości (`BarLengthRules`). Smoke **S1**: `PlaceBar` Block vs Polyline dla 00/11/21/13/44/99 —
  `OutlineVertices` ±1e-6, XData [0]–[13] równe, [14]=2, rekord 15, BTR `*U` bez XData, `Explodable=false`, efektywne właściwości równe
  przy niestandardowych CLAYER/CECOLOR/CELTYPE/CELTSCALE; **S2**: testy E1 nadal PASS. S-REG Polyline surowy: 0.
- **3b — skany widzą bloki:** S3 `ScanTemplates` (blok H10-04 w strefie `rebar_bottom`), S4 `IsExistingPosNrUB`/`ScanUBTemplates` dla 21,
  S5 `DeleteSummaryBars` + Ctrl+Z. S-REG Polyline: 0.
- **3c — tworzenie w trybie Block:** S6 pełny AutoRebar w trybie Block (wszystkie szablony bloki, ponowne uruchomienie bez nowych szablonów
  i numerów), S7 Punching 2× = jeden komplet 501/502, S8 grot etykiety blok vs polilinia ±1e-6. **S-REG Block bar-equiv: 0** (warunek 3d).
- **3d — edycja:** S9 copy-on-write przy `Apply` na kopii (oryginał bez zmian, kopia nowy BTR i numer, rozkład kopii zaktualizowany),
  S10 zmiana kształtu 00→21 (axis[0] w miejscu, grot na obrysie), S11 RC_UPDATE_BAR po SCALE 2, S12 `_.U` po Apply. S-REG (+`RC_REG_EDIT`) obu trybów: 0.
- **3e — COPY/PASTE:** S13 COPY pręt+etykieta+rozkład+opis (refs=1, back-link, `[18]` kopii), S14 COPY samego pręta, S15 MIRROR pręta 21.
- **3f — gripy dla użytkownika** (warunek: etap 1 GO): S16 numerowany blok z rozkładem + kopia → apply L+500 → nowy numer, rozkład, etykieta,
  BBS dwa wiersze; S17 blok 21 → 1 grip, `ApplyEndLength` odrzuca; S18 `LengthOverridden`.
- **3g — dokumentacja.**

## 7. Ryzyka
1. Grip drag — propagacja tylko z `OnCommandEnded`; zero zapisów w `Move*`. 2. UNDO — jedna komenda = jeden Ctrl+Z (M7).
3. COPY/PASTE — BricsCAD przy COPY NIE przemapowuje 1005 (spike W2-0); od W2 mapa klonowania (`CloneMap`); `*U` przy wklejaniu nowe. 4. EXPLODE — `Explodable=false`. 5. SCALE — XData bez zmian, widok do
`RC_UPDATE_BAR`; MIRROR (`GetTransformedCopy`) — S15. 6. Stare rysunki — bez zmian. 7. Wiele rysunków — `BarFormat` globalny.
8. Reentrancja — `Rebuild` w `CommandEnded` nie budzi watcherów. 9. Wydajność — setki `*U` jak rozkłady. 10. Handle się przesuwają —
nic nie sortuje po `Handle.Value` (grep). 11. Zaokrąglenia S-REG — w razie przeskoku tolerancja 0.02. 12. Bez wtyczki — zwykły blok.

### 8.1 Rysunek z prętami-blokami u kolegi (stara `BricsCadRc.dll`) — WAŻNE
| Funkcja starej wtyczki | Skutek na pręcie-bloku |
|---|---|
| `BbsDrawingReader.cs:54` pomija bloki | gięte (UB 01/02/03) **wypadają z BBS**, proste liczone „bez pręta” |
| `UpdateBarLabelCount` | etykieta nie aktualizuje liczby |
| `PositionReconciler` | konflikt numerów niewykryty |
| AutoRebar skany | **duplikaty szablonów z nowymi numerami** |
| Kaskada usuwania | ERASE pręta-bloku nie kasuje etykiety ani rozkładów |
| RC_DISTRIBUTION / RC_EDIT_BAR / RC_UPDATE_BAR | nie da się wybrać pręta-bloku |
| Zapis `RC_SINGLE_BAR` | tylko na poliliniach — [14] przetrwa |
| Wygląd, wydruk, MOVE, COPY | OK |

Wniosek: dopóki kolega ma starą wtyczkę, rysunki wymieniane z nim — format **Polilinia** (domyślny).

## 9. Testy ręczne (po 3f; `RC_BAR_FORMAT` → Blok, rysunek testowy)
M1 RC_BAR 00/21/13/44 + Info · M2 RC_DISTRIBUTION z 21 → RC_BBS · M3 COPY + RC_EDIT_BAR kopii · M4 grip końca 00 z rozkładem (`[RC AUTO]`, ESC) ·
M5 grip na UB (tylko przesunięcie) · M6 STRETCH końca · M7 Ctrl+Z/Y po M3/M4/M6 · M8 RC_GENERATE w trybie Blok vs Polilinia (identyczne BBS,
ponowne bez nowych szablonów) · M9 przebicia 2× · M10 Ctrl+C/V do innego rysunku · M11 MIRROR 21, SCALE 2 + RC_UPDATE_BAR · M12 EXPLODE (odmowa) ·
M13 bez wtyczki · M14 rysunek w starej wtyczce (potwierdzenie 8.1) · M15 tryb Polilinia = wzorzec · M16 rysunek mieszany.

## 10. Checklista recenzji
Build + `dotnet test` + `run-planc-e3.ps1` PASS; S-REG Polyline surowy 0 i Block bar-equiv 0; ścieżka polilinii bit w bit (gałąź
`Kind == Polyline` z dawnym wywołaniem); copy-on-write tylko przez `SingleBarBlock.Rebuild`; zero zapisów w `Move*`/`GetGripPoints`;
`[14]` przez `BarXData.Write(…, 2)`; handle przez `XLink`, back-link; numery tylko `PositionCounter`; BTR `*U`, `Explodable=false`,
bez XData, warstwa 0/ByBlock; brak nowych pustych `catch`; InvariantCulture; CRLF; komunikaty PL; `BarGeometryWatcher`, `PositionCounter`,
`BarScheduleEngine`, logika AutoRebar (poza skanami) bez zmian.

## 11. Pytania do użytkownika (domyślne w nawiasach)
1. Przełącznik `RC_BAR_FORMAT` per użytkownik *(tak; Polilinia do akceptacji etapów 3–5, potem Blok)*.
2. Kolega i stara wtyczka *(wymieniane rysunki w trybie Polilinia; w E6 także konwersja blok → polilinia)*.
3. Nazwa BTR *(`*U`)*. 4. EXPLODE *(zablokowany)*. 5. Grip 5 mm *(tak; minimum i `TotalLength` jak wzorzec)*.
6. Gięte w etapie 3 tylko przesunięcie *(tak)*. 7. RC_EDIT_BAR zachowuje obrót *(zgoda)*. 8. RC_EDIT_BAR przebudowuje 44 *(zgoda)*.
9. MOVE pręta-bloku bez etykiety do E5 *(akceptowane)*. 10. Domyślny format Blok dopiero po E5 *(tak)*.

## 12. Commity (robi użytkownik po testach)
3a fundament + przełącznik · 3b skany · 3c tworzenie w trybie Blok · 3d edycja · 3e COPY/PASTE · 3f gripy z propagacją · 3g dokumentacja.
