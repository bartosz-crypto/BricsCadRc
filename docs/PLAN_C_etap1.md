# Plan C, etap 1: próba „go / no-go” pręta jako bloku z gripami (`RC_TEST_BARBLOCK`)

> Autor: planista-recenzent (Claude). Recenzja: ChatGPT (`scripts/recenzja-gpt.ps1 -Plan docs/PLAN_C_etap1.md`).

## 1. Zrozumienie zadania i założenia

Nowa komenda testowa `RC_TEST_BARBLOCK` wstawia jeden pręt prosty (kształt 00, H12) jako `BlockReference` do własnego BTR.
Obrys pręta jest w środku BTR. XData `RC_SINGLE_BAR` (schemat bez zmian) dostaje dopisane pole `[14]` = 2 (wersja formatu)
i leży tylko na `BlockReference`. Własny `GripOverrule` daje 2 gripy: początek i koniec osi. Grip końca zmienia długość.
W trakcie przeciągania jest tylko podgląd (transient), a zapis (przebudowa BTR + XData) następuje w `CommandEnded`.
ESC niczego nie zmienia. Cel: wyłącznie odpowiedź „czy to działa w BricsCAD” przy edycji, kopiowaniu, cofaniu, wklejaniu,
zapisie, otwarciu bez wtyczki i wydruku.

**Założenia domyślne (do potwierdzenia, p. 1a):**
- **Grip [0] (początek) przesuwa cały pręt.** Natywny ruch punktu wstawienia przez `base.MoveGripPointsAt`, tak jak grip [0]
  opisu w `AnnotGripOverrule` (`AnnotMoveOverrule.cs:527`). Długość zmienia tylko grip [1].
- **Grip [1] działa tylko wzdłuż osi.** Składowa prostopadła ruchu jest pomijana, kierunek pręta się nie zmienia.
- **Długość jest zaokrąglana do 5 mm (decyzja użytkownika), minimum 100 mm.** To ręczny pręt testowy, więc nie obowiązuje siatka 250 z AutoRebar.
- **STRETCH oknem jest objęty próbą** przez `GetStretchPoints` / `MoveStretchPointsAt` tego samego `GripOverrule`
  (obie metody istnieją w `TD_Mgd`). Mechanizm ten sam: podgląd, potem zapis w `CommandEnded`.
- **Świadome odstępstwa próby** od ARCHITECTURE.md:
  - (a) komenda **nie używa `PositionCounter.NextAuto`**, mark jest stały: `H12-T`;
  - (b) pręt testowy **nie ma etykiety MLeader** ani rozkładów;
  - (c) pręty testowe **nie biorą udziału w numeracji ani w BBS** (p. 4.7).
  - Kod tymczasowy, oznaczony `// PLAN C ETAP 1 — TEST, do usunięcia/zastąpienia w etapie 3`.

### 1a. Decyzje użytkownika (2026-10-08): grip początku przesuwa pręt; zaokrąglenie 5 mm; grip końca tylko wzdłuż osi; testujemy oba warianty BTR
Implementator może ruszyć z wartościami domyślnymi; odpowiedzi zmieniają tylko stałe.
1. Grip początku: przesuwa cały pręt (domyślnie) czy zmienia długość od strony początku, jak STRETCH?
2. Zaokrąglenie długości przy gripie: 1 mm (domyślnie), 5 mm czy 25 mm?
3. Grip końca: tylko wzdłuż osi (domyślnie) czy swobodnie, z obrotem pręta?
4. Czy próba ma sprawdzić **dwa warianty BTR**: nazwany `RC_BAR_TEST_nnn` i anonimowy `*U` (opcja komendy, p. 3.4)?

## 2. Pliki i metody

| # | Plik | Zmiana |
|---|---|---|
| 1 | `src/Core/TestBarBlockEngine.cs` (**nowy**, CRLF) | Logika pręta-bloku: tworzenie, XData v2, budowa BTR, punkty osi, kopia BTR przy zapisie, zapis długości |
| 2 | `src/Commands/TestBarBlockCommands.cs` (**nowy**, CRLF, `// TEMPORARY — PLAN C ETAP 1`) | Komenda `RC_TEST_BARBLOCK` z opcjami `Info` i `Typ` |
| 3 | `src/Core/SingleBarGripOverrule.cs` (cały plik, dziś stub) | `GripOverrule` na `BlockReference` + własna subskrypcja `CommandEnded` / `CommandCancelled` przez `DocumentWatch` |
| 4 | `src/App/PluginApp.cs:32-33` i `:53` | Odkomentowanie `SingleBarGripOverrule.Register()` / `Unregister()` |
| 5 | `ARCHITECTURE.md` | Pole `[14]FormatVersion` w schemacie, opis modułu, status próby w „Otwarte tematy” |

Csproj w stylu SDK, nowe pliki dołączą się same. **Nie zmieniamy:** `SingleBarEngine`, `BarCopyWatcher`,
`BarGeometryWatcher`, `AnnotMoveOverrule.cs`, BBS, `PositionCounter`, AutoRebar.

### 2.1 `TestBarBlockEngine` (internal static, `BricsCadRc.Core`)

```csharp
internal const int    VersionIndex  = 14;      // pole [14] RC_SINGLE_BAR
internal const short  FormatVersion = 2;       // 2 = pręt jako BlockReference
internal const string NamePrefix    = "RC_BAR_TEST_";
internal const string TestMark      = "H12-T"; // ExtractPosNr → 0, poza numeracją
internal const double MinLength     = 100.0;
internal const double LengthStep    = 5.0;     // zaokrąglenie długości przy gripie/STRETCH (decyzja użytkownika)
```

- **`IsV2(Entity e)`**: `e is BlockReference` i XData `RC_SINGLE_BAR` z `v.Length > 14`, `v[14]` typu `ExtendedDataInteger16`
  równe 2. Bez wyjątków: `try` + `Log.Error`, zwraca false.
- **`WriteXDataV2(Entity e, BarData bar)`**: `SingleBarEngine.WriteXData(e, bar)` (`SingleBarEngine.cs:668`, pola [0]–[13]),
  potem dopisanie `[14]` = 2 na końcu rekordu. Jedno źródło schematu, indeksy tylko dopisywane.
- **`AxisStartWcs(br)`** = `Point3d.Origin.TransformBy(br.BlockTransform)`;
  **`AxisEndWcs(br, bar)`** = `new Point3d(bar.LengthA,0,0).TransformBy(br.BlockTransform)` (obrót, MIRROR, SCALE).
- **`ToLocal(br, wcsOffset)`** = `wcsOffset.TransformBy(br.BlockTransform.Inverse())`, używamy `.X`.
- **`BuildEntities(bar, startX)`**: `SingleBarEngine.BuildVisualEntities(ShapeCodeLibrary.Get("00"), bar.ParamValues,
  bar.Diameter, new Point3d(startX,0,0), Vector3d.XAxis)` (`SingleBarEngine.cs:468`); encje: warstwa `0`, kolor, grubość
  i rodzaj linii ByBlock; **bez XData** (inaczej złapią je `BarGeometryWatcher` / `BarCopyWatcher`). Do BTR i do podglądu.
- **`RebuildContents(tr, btr, bar)`**: usunięcie encji BTR, dodanie `BuildEntities(bar, 0)`.
- **`NextFreeName(bt)`**: `RC_BAR_TEST_001…`, pierwszy wolny. Nie przez `PositionCounter`.
- **`EnsureOwnBtr(tr, db, br)`** (copy-on-write, wzór `BarCopyWatcher.EnsureUniqueBtr`, `BarCopyWatcher.cs:375-413`):
  jeśli BTR ma > 1 referencję — nowy BTR (`*U` albo `NextFreeName`), `br.BlockTableRecord = newId`. Zwraca, czy utworzono.
- **`Create(db, p1, p2, anonymous)`**: rejestracja RegApp i warstw; Z spłaszczone; `len = Round(dist)`, minimum 100;
  `BarData { Mark = "H12-T", Diameter = 12, ShapeCode = "00", LengthA = TotalLength = len, LayerCode = "B1", Position = "BOT" }`;
  BTR + `RebuildContents` + `BlockReference(p1) { Rotation = kąt, Layer = LayerManager.LeaderLayer, ColorIndex = 256 }`;
  `WriteXDataV2`.
- **`ApplyLength(db, id, shiftLocal, newLength)`** (tylko z `CommandEnded`): `br` ForWrite, `IsV2`, `ReadBarXData`;
  brak zmiany → wyjście; `EnsureOwnBtr`; przy `shiftLocal != 0` przesunięcie `Position` wzdłuż osi; nowa długość do XData;
  `RebuildContents`; `RecordGraphicsModified(true)`. Zwraca (stara L, nowa L, czy nowy BTR) do komunikatu `[RC TEST]`.
- **`Describe(tr, br)`** (opcja `Info`): uchwyt, nazwa BTR, `IsAnonymous`, liczba referencji BTR, XData (Mark, LengthA,
  TotalLength, [14], długość rekordu), długość z geometrii BTR, Position, Rotation, ScaleFactors. Liczby w InvariantCulture.
  Główne narzędzie oceny testów.

### 2.2 `RC_TEST_BARBLOCK` (`CommandFlags.Modal`)
- `_anonymous = false` pamiętane w sesji.
- `[RC TEST] Początek pręta lub [Info/Typ]:`
  - **Typ**: Nazwany / Anonimowy.
  - **Info**: wybór `BlockReference`, `IsV2`, `Describe`; inaczej `[RC TEST] To nie jest pręt testowy (brak RC_SINGLE_BAR [14]=2).`
  - **Punkt**: drugi punkt z gumką, `Create`, komunikat
    `[RC TEST] Wstawiono pręt H12-T L=3000 (BTR RC_BAR_TEST_001, uchwyt 2A1F). Pręt testowy — poza numeracją i BBS.`
- Wyjątki: `Log.Error("TestBarBlockCommands.RC_TEST_BARBLOCK", ex)` + komunikat.

### 2.3 `SingleBarGripOverrule` (zastępuje stub)

| Element w `AnnotMoveOverrule.cs` | W nowej klasie |
|---|---|
| Transienty podglądu (61–89) | Kopia z własną listą; obrys z `BuildEntities` przekształcony `BlockTransform`, kolor 4; błąd → `Log.Error` + `Dispose` |
| `_pendingSpan`, `_lastGripOwner`, `RealId` (96–119) | `Dictionary<ObjectId,(double shiftLocal, double newLength)> _pending`, `_lastGripOwner`, `RealId` |
| `ResetDragState` (108–116) | Kopia: transienty + `_pending` |
| `ApplyPendingGripEdits` (124–182) | Kopia schematu, w pętli `TestBarBlockEngine.ApplyLength`; flaga `_applying` w `try/finally` |
| `IsApplicable` (309–317) | `BlockReference && TestBarBlockEngine.IsV2`, bez `SetXDataFilter` |
| `GetGripPoints` (319–343) | `[0]` = początek osi, `[1]` = koniec osi, bez `base` |
| `MoveGripPointsAt(IntegerCollection)` (502–532) | indeks 1 → `DragEnd`; indeks 0 → `base` (przesunięcie) |
| `MoveGripPointsAt(GripDataCollection)` (427–496) | grip przy końcu osi → `DragEnd`, inaczej `base` |
| `ApplyGripMove`, gałąź rozkładu (576–628) | `DragEnd`: długość = A + rzut offsetu na oś (offset kumulatywny od stanu sprzed dragu), podgląd, wpis do `_pending`. **Zero zapisów do bazy.** |
| `PluginApp.OnCommandEnded/Cancelled` | Własne handlery przez `DocumentWatch.Subscribe("SingleBarGripOverrule", …)` |
| `Register/Unregister` (1280–1324) | Kopia dla jednej instancji |

Handlery: `CommandEnded` → po komendach undo pomiń; inaczej (z lockiem dokumentu) apply pending + log nazwy komendy,
na końcu reset. `CommandCancelled` (ESC) → reset.

STRETCH: `GetStretchPoints` → [0] początek, [1] koniec. `MoveStretchPointsAt`: oba → natywne przesunięcie;
tylko 1 → podgląd + `_pending = (0, A + dx)`; tylko 0 → `_pending = (dx, A − dx)` z minimum. Zapis w `CommandEnded`.

Diagnostyka (do `log.txt`): pierwsze wywołanie w dragu loguje, czy obiekt był klonem (`ObjectId.IsNull`), i nazwę komendy.

## 3. Podejście

1. **Wzorzec `AnnotGripOverrule`** — sprawdzony na `BlockReference` w BricsCAD, rozwiązuje pułapkę zapisów w podglądzie.
2. **Osobny `TestBarBlockEngine`** — istniejący kod nietknięty; wywołujemy tylko publiczne funkcje bez skutków ubocznych.
3. **COPY: copy-on-write przy edycji + obserwacja.** Po COPY/ARRAY/MIRROR kopia wskazuje ten sam BTR (obserwujemy `Info`);
   dopiero zapis edycji daje edytowanemu prętowi własny BTR. Działa też dla kopii zrobionych bez wtyczki, a tego wymaga etap 3.
4. **Dwa warianty nazwy BTR** — wklejenie nazwanego bloku do rysunku z inną definicją o tej samej nazwie najpewniej użyje
   definicji docelowej (zły kształt przy dobrym XData). `*U` są przemianowywane. Test rozstrzyga nazewnictwo w etapie 3.
5. **Rozpoznanie pręta** = BlockReference + `RC_SINGLE_BAR` + `[14]==2`. Utrata XData = zniknięcie gripów, od razu widoczne.

## 4. Ryzyka
1. Zapisy w podglądzie — w ścieżce dragu żadnych `UpgradeOpen`, `XData=`, ForWrite (wyjątek: natywny ruch gripu [0]).
2. ESC — `CommandCancelled` czyści pending.
3. UNDO/REDO — zapis w `CommandEnded` powinien wejść do grupy undo komendy gripa (T4); brak cache'y geometrii.
4. Komenda przezroczysta w trakcie dragu (`'ZOOM`) może wywołać `CommandEnded` w połowie — logujemy nazwę komendy, zawężenie w etapie 4.
5. COPY/PASTE — pręt testowy nie ma powiązań; ryzyko kolizji nazw BTR (p. 3.4); osierocone BTR czyści PURGE.
6. Samo `SingleBarEngine.WriteXData` na bloku zgubiłoby `[14]` — zabezpieczyć w `BarEntity` (etap 2).
7. Izolacja (sprawdzona w kodzie): `ExtractPosNr("H12-T")` = 0; `BarScheduleEngine` usuwa wpisy z `TotalCount == 0`;
   `BbsDrawingReader.cs:54` pomija bloki; watchery reagują tylko na `Polyline`; overrule'e rozkładów filtrują `RC_BAR_BLOCK`;
   `RC_DISTRIBUTION` przyjmuje tylko polilinię; nazwa BTR nie może zaczynać się od `RC_SLAB_BARS_`.
8. Wiele rysunków — handlery przez `DocumentWatch`, apply filtruje po bazie; transienty czyszczone w reset.
9. Reentrancja — `_applying`; własne zapisy nie wywołują naszych handlerów.
10. MIRROR / SCALE — oś przez `BlockTransform` działa przy ujemnej skali; SCALE ≠ 1 daje rozjazd (obserwacja, decyzja w etapie 3).
11. ARRAY asocjacyjny / bloki zagnieżdżone — obserwacja.
12. EXPLODE — polilinia bez XData; decyzja `Explodable` w etapie 3.
13. Wydajność `IsApplicable` — jak dziś w AnnotGrip, pomijalna.

## 5. Kolejność kroków
1. `TestBarBlockEngine.cs`. 2. `TestBarBlockCommands.cs`, build. 3. `SingleBarGripOverrule.cs` (gripy → pending/apply →
STRETCH → diagnostyka). 4. `PluginApp.cs` Register/Unregister. 5. `ARCHITECTURE.md`. 6. `dotnet build` bez błędów.

## 6. Testy w BricsCAD
Przygotowanie: NETLOAD `bin\BricsCadRc.dll` (komunikat „Build: …” z bieżącą godziną). Weryfikacja: `RC_TEST_BARBLOCK` → `Info`.

| # | Test | Kroki | GO | NO-GO |
|---|---|---|---|---|
| T0 | Wstawienie | (0,0)→(3000,0); drugi (0,1000)→(2000,3000) | Wygląda jak pręt z `RC_BAR`; dokładnie 2 gripy na końcach osi; Info: L=3000, [14]=2, refs=1 | Brak gripów albo natywny grip wstawienia, błąd |
| T1 | Grip końca | Koniec do x=3500 | W trakcie cyan podgląd, oryginał bez zmian; po kliku 3500, XData = geometria; też ukośny | Zmiana w trakcie dragu, rozjazd danych, „skacząca” długość |
| T1b | W bok / za początek | Prostopadle; za początek | Rzut na oś, kierunek bez zmian; minimum 100 mm | Obrót, ujemna długość, wyjątek |
| T2 | Grip początku | Przeciągnij | Pręt się przesuwa, długość bez zmian | Deformacja |
| T3 | ESC | Ciągnij koniec, ESC / prawy klik | Bez zmian, podgląd znika, kolejna komenda niczego nie zmienia | Zmiana po ESC, cyan śmieć |
| T4 | UNDO/REDO | Po T1 Ctrl+Z, Ctrl+Y; 3 edycje i 3× Ctrl+Z | 1 Ctrl+Z = 1 edycja, geometria i XData razem | Rozjazd; 2× Ctrl+Z (WAŻNE); crash |
| T5 | COPY + edycja kopii | COPY; Info; edytuj kopię; Info; edytuj oryginał; Ctrl+Z ×2 | Po COPY refs=2; po edycji kopia ma własny BTR, oryginał bez zmian; UNDO poprawne | Edycja kopii zmienia oryginał |
| T6 | MIRROR | Ukośny pręt, z/bez usuwania źródła | Gripy na końcach; wydłużanie na zewnątrz; Info zgodne | Grip w złym miejscu, odwrotny kierunek |
| T7 | ARRAY | Nieasocjacyjny 3×1, edytuj środkowy; asocjacyjny | Niezależna edycja; asocjacyjny — obserwacja | Edycja jednego zmienia wszystkie |
| T8 | STRETCH oknem | Okno: (a) koniec, (b) początek, (c) całość | (a) dłuższy, (b) początek się przesuwa, (c) przesunięcie; 1 Ctrl+Z | (a)/(b) brak efektu → no-go dla STRETCH; rozjazd danych = BLOKER |
| T9 | Ctrl+C / Ctrl+V do innego rysunku | (a) do pustego; (b) do rysunku z innym `RC_BAR_TEST_001`; (c) jak (b), wariant Anonimowy | (a) działa jak w źródle; (c) zachowuje swoją długość | (a) utrata XData/gripów; (b) zła długość → nazwy stałe odpadają; (c) zła długość = BLOKER planu |
| T10 | Zapis, ponowne otwarcie, 2 rysunki | Zapis/otwarcie; edycje w dwóch rysunkach | Gripy i edycja w obu, bez wpływu na siebie | Brak gripów, edycja w złym rysunku |
| T11 | Bez wtyczki | Otwórz bez NETLOAD; AUDIT; MOVE; zapisz; otwórz z wtyczką | Widoczny identycznie, bez proxy; AUDIT czysty; po powrocie działa | Proxy, utrata XData |
| T12 | Wydruk | Obok pręt z `RC_BAR`; podgląd/PDF | Identyczny kolor, grubość, linia | Różnica, niewidoczny |
| T13 | SCALE / EXPLODE / ERASE | SCALE 2 + Info; EXPLODE + Ctrl+Z; ERASE + Ctrl+Z | Obserwacja; ERASE+UNDO przywraca z gripami | Crash, utrata pręta po UNDO |
| T14 | Dwa pręty naraz | Shift na gripach końców obu | Oba wydłużone, Info zgodne | Tylko jeden |
| T15 | Izolacja | `RC_BAR` (numer bez zmian), `RC_SCHEDULE`, `RC_BBS`, `RC_DISTRIBUTION`, `RC_EDIT_DISTRIBUTION` | Pręt testowy niewidoczny dla numeracji, BBS, rozkładów | Pręt w BBS/numeracji, wyjątek |
| T16 | Regresja | Gripy istniejących rozkładów i opisów, ESC, Ctrl+Z | Bez zmian | Jakakolwiek zmiana (BLOKER) |

Po testach: wysłać `%LOCALAPPDATA%\BricsCadRc\log.txt` (wpisy `[RC TEST]`: nazwy komend gripa i STRETCH, klon czy oryginał).

**Decyzja:** GO, gdy przechodzą T0–T6 (z T3b), T10–T12, T14, T15, T16. T8 i T9 rozstrzygają warianty (STRETCH, nazewnictwo BTR).
T7 (asocjacyjny) i T13 — obserwacje do `PLAN_C.md`. **NO-GO dla Planu C:** utrata XData przy kopiowaniu lub zapisie,
brak podglądu bez zapisu, UNDO rozjeżdżające geometrię z XData, proxy bez wtyczki.

## 6a. Poprawki po recenzji ChatGPT (zweryfikowane w kodzie, obowiązują implementatora)
1. **[BLOKER] Komenda przezroczysta w trakcie dragu** (`'ZOOM`, `'PAN`): `CommandEnded` komendy zagnieżdżonej
   NIE może zapisywać ani czyścić `_pending`. Zapis tylko wtedy, gdy po zakończeniu komendy w dokumencie nie trwa już
   żadna inna komenda (`Document.CommandInProgress` puste) — inaczej pending zostaje. Pending przypisany do dokumentu.
   Nowy obowiązkowy test **T3b**: przeciągaj koniec → `'ZOOM` → zakończ zoom → ESC ⇒ pręt bez zmian;
   to samo z kliknięciem zamiast ESC ⇒ jedna zmiana, jeden Ctrl+Z. Powtórzyć z `'OSNAP` zamiast `'ZOOM`
   (recenzja ChatGPT, runda 2: ochrona zależy od formatu `CommandInProgress` — jeśli T3b z `'OSNAP` nie przejdzie,
   w etapie 4 śledzenie zagnieżdżenia komend przez `CommandWillStart`).
2. **[WAŻNE] Klon podglądu przy kilku prętach:** `RealId` z jednym `_lastGripOwner` może przypisać podgląd do złego pręta.
   Mapowanie klon → oryginał jednoznaczne (np. po `BlockTransform` + długości z XData względem prętów, dla których
   pokazano gripy); gdy niejednoznaczne — edycję odrzucić z komunikatem `[RC TEST]` i wpisem w logu.
   **T14 z prętami o różnych długościach i kontrolą UNDO jest warunkiem GO.**
3. **[WAŻNE] STRETCH początku:** najpierw ograniczona i zaokrąglona `newLength = Max(MinLength, Round5(A − dx))`,
   potem `shiftLocal = A − newLength` — koniec spoza okna nigdy się nie rusza (jak `AnnotMoveOverrule.cs:619`).
   T8 rozszerzony o: początek przeciągnięty za koniec, pręt ukośny i odbity lustrzanie.
4. **[WAŻNE] Grubość linii jak w `RC_BAR`:** `BlockReference.LineWeight = LineWeight.LineWeight000`
   (jak `SingleBarEngine.cs:92`), encje w BTR ByBlock. T12 również przy warstwie z niestandardową grubością.

## 7. Checklista recenzji po implementacji
- Zero zapisów do bazy w ścieżce dragu.
- `[14]` dopisany na końcu przez `WriteXDataV2`; [0]–[13] przez `SingleBarEngine.WriteXData`.
- Brak XData na encjach w BTR; nazwa BTR nie zaczyna się od `RC_SLAB_BARS_`.
- Brak pustych `catch {}`; liczby w InvariantCulture; CRLF; komunikaty po polsku z `[RC TEST]`.
- `PluginApp` zmieniony tylko w Register/Unregister.
