# Plan C, etap 2: warstwa pośrednia `BarEntity` (jeden punkt odczytu i zapisu pręta)

> Autor: planista-recenzent (Claude). Recenzja: `scripts/recenzja-gpt.ps1 -Plan docs/PLAN_C_etap2.md`.
> Wzorzec zachowania: `aba0ed1` (wersja kolegi). Każda różnica wyniku dla prętów-polilinii to BLOKER.

## 0. Warunki wstępne
- **Etap 1 ma wynik GO** (T0–T16 z `PLAN_C_etap1.md`). Wyjątek: krok 2a (czysta logika + strażnik XData) można robić
  równolegle z testami etapu 1.
- Wzorzec do porównań: `git worktree add ..\BricsCadRc-wzorzec aba0ed1`, build w worktree (własny `bin\`). Tego DLL nie
  wolno ładować w tej samej sesji BricsCAD co DLL etapu 2 (ta sama nazwa assembly).

## 1. Zrozumienie zadania i założenia
`BarEntity` rozpoznaje pręt, czyta i zapisuje `RC_SINGLE_BAR` i udostępnia geometrię potrzebną w etapie 2, dla polilinii (v1)
i bloku (`BlockReference`, `[14]=2`). Dla polilinii zachowanie bit w bit jak dziś. Pręty-bloki z `RC_TEST_BARBLOCK` stają się
widoczne w BBS, `PositionReconciler`, przy wyborze pręta do rozkładu (RC_DISTRIBUTION, RC_EDIT_DISTRIBUTION Manual)
i przy liczeniu etykiet. `PositionCounter` i `RC_SCHEDULE` widzą je już dziś. Tworzenie prętów, AutoRebar, watchery,
kopiowanie i gripy zostają przy polilinii (etapy 3–5).

**Ustalenie kluczowe (sprawdzone w kodzie):** `SingleBarEngine.ReadBarXData(Entity)` czyta XData z każdej encji, więc część
miejsc już dziś rozpoznaje bloki: `PositionCounter.cs:145`, `BarScheduleEngine.cs:61`, `AutoRebarEngine.Section.cs:190`,
`AutoRebarEngine.Add.cs:570`, `EditCommands.cs:217`. Bloki wykluczają tylko jawne filtry: `is/as Polyline`,
`ent is BlockReference`, `AddAllowedClass(Polyline)`.

### 1-POPRAWKI. Po recenzji ChatGPT (zweryfikowane, mają pierwszeństwo przed 1a i 2d)
1. **[BLOKER] Brak numerowanych prętów-bloków dla użytkownika w etapie 2.** Bez synchronizacji po edycji (numer,
   rozkłady — etap 4) kopia wydłużona gripem dałaby dwa różne pręty pod jednym numerem, a BBS rozjechałby się
   z rozkładem. Dlatego: **bez opcji `Pozycja` w `RC_TEST_BARBLOCK`** (pytania 1 i 3 z 1a nieaktualne; #34/#35 tylko
   odmowa z pytania 2). Numerowane pręty-bloki tworzą wyłącznie testy automatyczne (`tools/BrxSmokeTests`, refleksja).
2. **[BLOKER] Numerowany pręt-blok w testach zawsze z etykietą** (`SingleBarEngine.PlaceBarLabel` + `LabelHandle` przez
   `XLink`), tak jak `RC_BAR` we wzorcu — inaczej ERASE nie uruchomi kaskady (quirk 1103) i zostaną osierocone rozkłady.
   Wyjątek „bez etykiety” zostaje tylko dla zgodności starych obiektów (S11). Test pełnej sekwencji: blok z etykietą →
   rozkład → `RC_BBS`-reader i `BarScheduleEngine` → ERASE → kaskada → UNDO/REDO.
3. **[WAŻNE] Nazwa overrule'a:** `BarBlockEraseOverrule` już istnieje (`AnnotMoveOverrule.cs:1024`, rozkłady).
   Nowy: **`SingleBarBlockEraseOverrule`** (filtr `RC_SINGLE_BAR` v2), własna rejestracja, wyrejestrowanie i `Dispose`.

### 1a. Pytania do użytkownika (wartości domyślne w nawiasie) — zob. 1-POPRAWKI
1. **Numerowany pręt testowy.** `H12-T` ma numer 0, więc nie da się nim przetestować BBS, Reconcilera ani rozkładu z bloku.
   *Domyślnie:* nowa opcja `Pozycja` w `RC_TEST_BARBLOCK`: pręt H12/00 z numerem z `PositionCounter.NextAuto` (jak `RC_BAR`),
   Mark `H12-nn`, bez etykiety MLeader. Odstępstwo „poza numeracją” z etapu 1 dotyczy wtedy tylko `H12-T`.
2. **RC_DISTRIBUTION z `H12-T`.** *Domyślnie:* odmowa: `[RC] Pręt testowy bez numeru pozycji — użyj RC_TEST_BARBLOCK → Pozycja.`
3. **Grip na numerowanym pręcie-bloku** zmienia `LengthA`, ale nie przebudowuje rozkładów i nie uruchamia Reconcilera (etap 4).
   *Domyślnie:* akceptujemy; komunikat `[RC TEST] Rozkłady i numer nie są aktualizowane do etapu 4.`
4. **AutoRebar w etapie 2 nie widzi prętów-bloków jako szablonów** (inaczej pręt testowy mógłby zostać szablonem ADD,
   `AutoRebarEngine.Add.cs:332-337`, i zmienić wynik względem wzorca). *Domyślnie:* tak, przełączenie w etapie 3.

## 2. Inwentaryzacja
Decyzje: **2a/2b/2c/2d** — przepiąć w danym kroku; **JUŻ** — działa dla bloku; **ZOSTAW** — tylko polilinia / nie dotyczy;
**E3–E6** — późniejszy etap.

| # | Plik:linie | Co robi | Decyzja | Uzasadnienie |
|---|---|---|---|---|
| 1 | `SingleBarEngine.cs:668-686` `WriteXData` | Zapis [0]–[13] (nadpisuje rekord) | **2a**: deleguje do `BarXData.Write` (zachowuje ogon [14+]) | Ryzyko 4.6 etapu 1; polilinie nie mają ogona → wynik identyczny |
| 2 | `SingleBarEngine.cs:688-716` `ReadBarXData` | Jedyny parser | ZOSTAW; `BarEntity.Read` go woła | Jedno źródło schematu |
| 3 | `TestBarBlockEngine.cs:71-78` `WriteXDataV2` | `WriteXData` + dopisanie `[14]` | **2a**: `BarXData.Write(e, bar, formatVersion: 2)` (podmiana `[14]`) | **BLOKER, jeśli 1 i 3 nie zmienią się razem** (rekord 16-polowy) |
| 4 | `TestBarBlockEngine.cs:50-68` `IsV2` | Rozpoznanie v2 | **2a**: `BarRecordRules.IsBlockFormat` | Jedna reguła |
| 5 | `TestBarBlockEngine.cs:251, 316`; `SingleBarGripOverrule.cs:194, 333, 350, 374, 398, 409` | Odczyt XData bloku | ZOSTAW (E4) | Ścieżka dragu bez zmian |
| 6 | `BbsDrawingReader.cs:54` | Pomija każdy `BlockReference` | **2b**: pomija, chyba że `KindOf == Block` | Uwaga ChatGPT |
| 7 | `PositionReconciler.cs:37-38` | Zmieniony pręt musi być `Polyline` | **2b**: `Polyline \| Block` | Uwaga ChatGPT |
| 8 | `PositionReconciler.cs:47-53` | „Inne” pręty — tylko polilinie | **2b**: `Polyline \| Block` | Numer zajęty przez pręt-blok = konflikt |
| 9 | `PositionReconciler.cs:99-107` | Zapis Mark `as Polyline` | **2b**: `BarEntity.Open(ForWrite).Write` | Zachowuje `[14]` |
| 10 | `PositionReconciler.cs:117-164` | Po handle | JUŻ | — |
| 11 | `AnnotationEngine.cs:1825-1827` `UpdateBarLabelCount` | Pręt `as Polyline` | **2b**: `Polyline \| Block` | Uwaga ChatGPT; pusty `catch {}` (1888) zostaje (kod wzorca) |
| 12 | `Rebar3dCommands.cs:447-461` `UbHeight` | Pręt `is Polyline` | **2b**: `Polyline \| Block` | Tylko odczyt |
| 13 | `PositionCounter.cs:131-167` | Numery z każdej encji | JUŻ | — |
| 14 | `BarScheduleEngine.cs:53-92` | jw. | JUŻ | — |
| 15 | `AutoRebarEngine.Section.cs:180-201`, `AutoRebarEngine.Add.cs:565-572`, `EditCommands.cs:204-218` | Pręt po handle | JUŻ | — |
| 16 | `BarCommands.cs:133-152` RC_DISTRIBUTION | `AddAllowedClass(Polyline)` | **2d**: + `BlockReference`; odmowa dla bloku bez `[14]=2` i numeru ≤ 0 | Uwaga ChatGPT; kąt nadpisuje linia 310 |
| 17 | `BarCommands.cs:934-991` `GetViewLengthManual` | Segmenty `as Polyline` | **2d**: `BarEntity.OutlineVertices()` | Także dla `EditCommands.cs:225` |
| 18 | `AnnotMoveOverrule.cs:1089-1188` `BarPolylineEraseOverrule` (+1297-1302, 1315-1317) | Kaskada usuwania | **2d**: treść do `BarEraseCascade.Run` **dosłownie** (z wczesnym `return` przy pustym `LabelHandle`, 1103) + `BarBlockEraseOverrule` na `BlockReference` | Uwaga ChatGPT; quirk wzorca zachowany; pusty `catch {}` (1181) → `Log.Error` |
| 19 | `BarCommands.cs:115, 1189, 1281`; `EditCommands.cs:465`; `BarGeometryWatcher.cs:267, 558`; `BarCopyWatcher.cs:173, 184`; `AutoRebarEngine.cs:1915, 1970`; `PunchingTagCommands.cs:114` | `SingleBarEngine.WriteXData` | **2c**: mechanicznie → `BarXData.Write` | Jeden punkt zapisu |
| 20 | `BarCommands.cs:26-121` RC_BAR, `SingleBarEngine.cs:63-113, 138-158`, `AutoRebarEngine.cs:1868-1978`, `PunchingTagCommands.cs:92-118` | Tworzenie pręta-polilinii | E3 | — |
| 21 | `BarCommands.cs:1120-1352` RC_UPDATE_BAR / RC_EDIT_BAR | Geometria polilinii | E3 (zapis w 2c) | — |
| 22 | `SingleBarEngine.cs:526-630` (`RC_BAR_LINK`); `BarCommands.cs:1148, 1244` | Towarzysze | ZOSTAW / E6 | Pręt-blok ma zawartość w BTR |
| 23 | `SingleBarEngine.cs:121, 740-809` | Oś z obrysu | ZOSTAW | Tylko `Kind == Polyline` |
| 24 | `BarGeometryWatcher.cs:91-109, 115-135, 262-320, 439-591` | Stretch/MOVE polilinii | E5, **tylko polilinie** | Kolizja z `SingleBarGripOverrule` |
| 25 | `BarCopyWatcher.cs:48-56, 123-189` | Parowanie po COPY | E3 | — |
| 26 | `RcMLeaderOverrule.cs:142-175` | Grot na polilinię | E5 | — |
| 27 | `BarCommands.cs:1013-1118` RC_FIX_LABEL | Grot etykiety | E5 | No-op dla bloku |
| 28 | `AutoRebarEngine.cs:1625-1648, 1750-1795, 2847-2864, 2943-2963`; `AutoRebarEngine.Add.cs:324-340` | Szablony z polilinii | E3 (pytanie 4) | Zero ryzyka dla AutoRebar |
| 29 | `AutoRebarEngine.cs:1566-1593` | Extents | JUŻ | — |
| 30 | `PunchingTagEngine.cs:137-157` | Pręty 501/502 | E3 | — |
| 31 | `EditCommands.cs:402-469` RC_BAR_END | Polilinia/rozkład | ZOSTAW (zapis 465 w 2c) | — |
| 32 | `Rebar3dCommands.cs:315-317` | Ramki detali | ZOSTAW | — |
| 33 | `SlabPicker.cs:82`, `AutoRebarCommands.cs:144`, `GenerateCommands.cs:25`, `SectionCommands.cs:33`, `Rebar3dCommands.cs:242, 283` | Obrysy, otwory | ZOSTAW | Nie dotyczy prętów |
| 34 | `TestBarBlockCommands.cs` | Komenda próby | **2d**: opcja `Pozycja`, odmowy | Testowalność |
| 35 | `SingleBarGripOverrule` → `ApplyLength` | Zapis gripa | **2d**: tylko komunikat (pytanie 3) | — |
| 36 | `TestCommands.cs:59` | Komenda testowa | ZOSTAW | — |
| 37 | `PluginApp.cs` | Rejestracja | ZOSTAW | Nowy overrule w `AnnotMoveOverrule.Register` |

## 3. Projekt API

### 3.1 `src/Core/BarRecordRules.cs` (nowy, CRLF, **bez Teigha** — kompilowany też w `tests`)
```csharp
internal static class BarRecordRules
{
    public const int   HeadCount          = 14;   // [0] AppName … [13] LabelHandle
    public const int   VersionIndex       = 14;
    public const short BlockFormatVersion = 2;
    public const int   Int16TypeCode      = 1070;
    public static bool IsBlockFormat(int recordLength, int typeCodeAt14, object valueAt14);
    /// head (dokładnie 14) + ogon istniejącego rekordu od [14]; setVersion → [14] podmieniony albo dopisany. Nigdy nie dubluje [14].
    public static List<T> Merge<T>(IReadOnlyList<T> head, IReadOnlyList<T> existing, bool setVersion, T version);
}
```

### 3.2 `src/Core/BarEntity.cs` (nowy, CRLF)
```csharp
internal static class BarXData
{
    // Nagłówek [0]–[13] przeniesiony 1:1 z SingleBarEngine.WriteXData, potem BarRecordRules.Merge.
    public static void Write(Entity e, BarData bar, short? formatVersion = null);
}
internal enum BarKind { Polyline, Block, Other }
[Flags] internal enum BarKinds { Polyline = 1, Block = 2, Other = 4 }
internal sealed class BarEntity
{
    public Entity Entity { get; }  public BarKind Kind { get; }
    public static BarKind? KindOf(Entity e);
    public static BarEntity From(Entity e, BarKinds accept);
    public static BarEntity Open(Transaction tr, ObjectId id, OpenMode mode, BarKinds accept);
    public static IEnumerable<BarEntity> ScanModelSpace(Transaction tr, Database db, BarKinds accept);
    public BarData Read();             // = SingleBarEngine.ReadBarXData; bez try/catch
    public void Write(BarData bar);    // BarXData.Write(Entity, bar, Kind == Block ? 2 : null); bez UpgradeOpen
    public IReadOnlyList<Point3d> OutlineVertices(Transaction tr);
}
```
Zasady: bezstanowa, bez cache, bez pustych `catch`, handle tylko przez `XLink`, bez API nieużywanego w etapie 2
(`ScanModelSpace` — jeśli recenzja uzna za nieużywane, przenieść do E3). `csproj`: `InternalsVisibleTo BrxSmokeTests`.

### 3.3 Dlaczego tak
- **Strażnik w najniższej warstwie**: każde przeoczone wywołanie `WriteXData` na bloku zachowa `[14]`.
- **Flagi `BarKinds` per wywołanie**: każde miejsce dostaje dawny zbiór + `Block`, więc dla polilinii nic się nie zmienia.
- **Bez zmian w AutoRebar i watcherach**: najwyższe ryzyko regresji przy najmniejszym zysku.

## 4. Kroki
### 2a — fundament, bez zmiany zachowania
1. `BarRecordRules.cs` + `tests/BarRecordRulesTests.cs` (+ `<Compile Include>` w testach).
2. `BarEntity.cs`. 3. `SingleBarEngine.WriteXData` → `BarXData.Write`. 4. `WriteXDataV2` / `IsV2` → nowe API.
   **Punkty 3 i 4 razem.** 5. `InternalsVisibleTo`. 6. Build + `dotnet test` + smoke S1–S4.
Testy jednostkowe `Merge`: brak → 14; 8 → 14; 14 → 14; 15 bez `setVersion` → 15; 15 + `setVersion` → 15 (**nie 16**);
14 + `setVersion` → 15; 17 → ogon zachowany; `head.Count != 14` → wyjątek. `IsBlockFormat`: Int16 2 → true; Int16 1, 1000 „2”, długość 14 → false.
### 2b — odczyt + Reconciler: #6, #7–9, #11, #12. Smoke S5–S8.
### 2c — zapis mechanicznie: #19; po kroku `SingleBarEngine.WriteXData` tylko w definicji. Smoke S1 + R1–R7.
### 2d — wybory, kaskada, komenda testowa: #16, #17, #18, #34, #35, ARCHITECTURE.md. Smoke S9–S12.
(2e — przepięcie skanów AutoRebar/Punching — niezalecane, razem z etapem 3.)

## 5. Ryzyka
1. Podwójne `[14]` → BLOKER (test „15 + setVersion → 15”, smoke S3).
2. Identyczność XData polilinii — nagłówek 1:1; S1 porównuje pole po polu.
3. Grip drag — `BarEntity` poza ścieżką dragu.
4. UNDO/REDO — kaskada bloku w tej samej grupie undo; bez cache.
5. EXPLODE pręta-bloku = ERASE referencji → kaskada (jak polilinia). Test N9.
6. COPY/PASTE — `[14]` kopiuje się; `BarCopyWatcher` ignoruje bloki (E3).
7. Stare rysunki — odczyt bez zmian, zapis daje 14 pól z 1005 jak dziś (S2, R11).
8. Wiele rysunków — brak stanu globalnego.
9. Reentrancja — nowy overrule nie budzi watcherów.
10. Zmiana UX w RC_DISTRIBUTION — „obcy” blok odrzucany po wyborze, nie przy wyborze.
11. Pręt-blok w rysunku produkcyjnym pojawi się w BBS/numeracji — ostrzeżenie przy wstawieniu.
12. Quirk wzorca: pręt bez etykiety przy ERASE nie kasuje rozkładów (`AnnotMoveOverrule.cs:1103`) — zachowany.

## 6. Testy
### 6.1 Automatyczne (`tools/BrxSmokeTests`, komendy `RCSMOKE_E2_*`)
| # | Krok | Sprawdza |
|---|---|---|
| S1 | 2a/2c | `PlaceBar` (00/21/44) → R0; `Write(Read())` → R1 == R0 |
| S2 | 2a | Rekord 8-polowy i [13] jako 1000 → `Write` → 14 pól, [13] = 1005 |
| S3 | 2a | v2 → 15 pól, [14]=2; `SingleBarEngine.WriteXData(br)` → 15; `WriteXDataV2` 2× → 15 |
| S4 | 2a | `KindOf` dla polilinii / v2 / bloku bez [14] / `Line`; `OutlineVertices` bloku obróconego i odbitego ±1e-6 |
| S5 | 2b | v2 z numerem + rozkład + opis → `BbsDrawingReader.ReadBars`: wiersz poz. nn |
| S6 | 2b | Polilinia 05 L3000 + v2 05 L3500 → Reconciler → nowy numer; z v2 → [14] zachowane |
| S7 | 2b | `UpdateBarLabelCount` (porównanie z S-REG) |
| S8 | 2b | `GetUsedPositionNumbers` zawiera numer v2 |
| S9 | 2d | v2 z etykietą + rozkład + opis → `Erase` → kaskada; `_.U` → przywrócone |
| S10 | 2d | To samo dla polilinii |
| S11 | 2d | v2 bez etykiety → rozkład zostaje |
| S12 | 2d | Dwa rysunki — kaskada w A nie rusza B |
| S-REG | wszystkie | Ten sam scenariusz z DLL wzorca `aba0ed1` i etapu 2 (AutoRebar B1/B2/T1/T2/UB, PlaceBar + rozkład, Reconciler, BBS, Schedule) → znormalizowany zrzut; **diff pusty**, różnica = BLOKER |

Ręcznie: gripy i ESC, jigi i dialogi, STRETCH oknem, wydruk, schowek.

### 6.2 Regresja wzorzec vs etap 2 (R1–R15)
R1 RC_BAR + RC_DISTRIBUTION; R2 Manual; R3 RC_EDIT_DISTRIBUTION; R4 STRETCH polilinii; R5 COPY + STRETCH kopii;
R6 RC_EDIT_BAR / RC_UPDATE_BAR; R7 ERASE z kaskadą + Ctrl+Z; R8 ERASE opisu; R9 RC_GENERATE wszystkie warstwy;
R10 RC_BBS / RC_SCHEDULE; R11 stary rysunek; R12 dwa rysunki; R13 Ctrl+Z/Y; R14 RC_SECTION / RC_MODEL_3D;
R15 testy etapu 1 (Info: długość rekordu 15). Każda różnica = BLOKER.

### 6.3 Nowe zachowanie (N1–N10)
N1 `Pozycja` + RC_BAR (kolejny numer); N2 rozkład z bloku + BBS; N3 odmowa dla `H12-T`; N4 Manual na bloku obróconym;
N5 konflikt numeru polilinia/blok; N6 grip numerowanego bloku (komunikat); N7 COPY blok+rozkład+opis; N8 ERASE bez etykiety;
N9 EXPLODE + Ctrl+Z; N10 zapis, otwarcie, bez wtyczki.

## 7. Checklista recenzji
- Build + `dotnet test` przechodzą. 2a: punkty 3 i 4 razem; nagłówek 1:1; `BarRecordRules` bez Teigha.
- Każde przepięte miejsce: dawny filtr + `Block`, nic więcej.
- Zero zmian w AutoRebar, `BarGeometryWatcher`, `BarCopyWatcher` (poza 2c), `RcMLeaderOverrule`, `PositionCounter`,
  `BarScheduleEngine`, ścieżce dragu.
- Brak nowych pustych `catch {}`; CRLF; komunikaty PL; InvariantCulture; handle przez `XLink`; brak nieużywanego API.

## 8. Commity (robi użytkownik po testach)
2a `BarEntity/BarXData, zapis RC_SINGLE_BAR zachowuje [14+]` · 2b `BBS, PositionReconciler i etykiety widzą pręt-blok v2` ·
2c `zapis XData pręta tylko przez BarXData` · 2d `RC_DISTRIBUTION z pręta-bloku, kaskada usuwania bloku, RC_TEST_BARBLOCK Pozycja`
