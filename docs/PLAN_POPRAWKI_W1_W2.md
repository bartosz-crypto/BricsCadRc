# Plan W1 i W2: poprawki błędów wzorca aba0ed1 (#1, #2, #4, #5, #7, #3, #6) + S-REG z oczekiwanymi różnicami

> Autor: planista-recenzent (Claude). Recenzja: `scripts/recenzja-gpt.ps1 -Plan docs/PLAN_POPRAWKI_W1_W2.md`.
> Podstawa: CLAUDE.md („Wzorzec — wyjątek”), `docs/POPRAWKI_WZORCA.md`, ARCHITECTURE.md, PLAN_C_etap2/3.
> Numery linii: NASZ kod (2026-10-09). Build tylko staging: `dotnet build src/BricsCadRc.csproj -c Release "-p:OutputPath=..\bin-staging\"`.

## 0-POPRAWKI (recenzja ChatGPT 2026-10-09 + decyzje użytkownika; pierwszeństwo przed resztą)
1. **[BLOKER] W2/4.4:** pary rozkład↔opis ustalone mapą muszą przejść tę samą końcową **normalizację odbicia i przebudowę
   opisu** co dziś w `RemapCopiedPairs` (`BarCopyWatcher.cs:486–535`, jak wzorzec). Rozdzielić „wyszukanie pary” (mapa albo
   heurystyka) od „normalizacji i przebudowy” (zawsze). Testy W2 sprawdzają geometrię opisu po MIRROR, nie tylko handle.
2. **[BLOKER] #5 dotyczy też pręta-bloku:** `SingleBarBlock.Rebuild` zachowuje pozycję referencji, a pierścień ma środek
   lokalny (A/2, A/2) — przy zmianie A środek się przesuwa. Edycja pierścienia-bloku zachowuje ŚRODEK (z obrotem i odbiciem);
   test porównuje polilinię i blok dla tego samego przypadku.
3. **[BLOKER] Schowek:** pochodzenie i mapa wiązane z konkretną operacją schowka (COPYCLIP/CUTCLIP → PASTE*), nie z ostatnim
   `WblockMappingAvailable` — zwykły WBLOCK nie może ich nadpisać. Test: COPYCLIP w A → WBLOCK w B → PASTECLIP do A ⇒ [18] zachowane.
4. **[WAŻNE] Czyszczenie [16] opisu:** `AnnotationEngine.WriteAnnotXData` przy pustym `SourceBlockHandle` odtwarza poprzednią
   wartość (`AnnotationEngine.cs:1701–1712`) — czyścić punktową podmianą pola przez `XLink.Write`, test czyta końcowe XData.
5. **[WAŻNE] Komenda przezroczysta:** przed `CloneMap.Take(db)` sprawdzić zagnieżdżenie komend (jak
   `SingleBarGripOverrule.OtherCommandInProgress` + komendy widoku) — kolejka do końca komendy nadrzędnej. W W2-0 sprawdzić
   `'ZOOM`, ESC i anulowanie kolejnej kopii po zaakceptowaniu pierwszej (COPY wielokrotne).
6. **Decyzje użytkownika (2026-10-09):** wszystkie domyślne z sekcji 1 zaakceptowane; #12 bez poprawki.
   **Kształt 44 → zgodnie z BS 8666 (krok W4): 44 = „kapelusz”, pierścień = 75; stare rysunki z 44-pierścieniem
   rozpoznawane jako pierścień.** W1e (#5) dotyczy więc PIERŚCIENIA — implementować jako logikę „pierścień” (dziś kod 44),
   tak by W4 mogło przenieść ją na 75 bez zmiany zachowania (jedno miejsce rozpoznania `IsRingShape(bar)`).

## 0. Założenia
1. W1: #1, #2, #4, #5, #7 w ścieżce polilinii; dla bloków tylko #2 (wspólna kaskada) — #1/#4/#5/#7 bloków nie dotyczą
   (blok: `SingleBarBlock.Rebuild`), ale dostają testy potwierdzające.
2. W2 = 3e3 (PLAN_C_etap3 0-POPRAWKI p.5) + #3 + #6: powiązania kopii z mapy oryginał→klon (`IdMapping`), dla polilinii
   i bloków jednakowo; heurystyka tylko jako zapas, gdy mapy brak.
3. Każda różnica względem wzorca wpisana w `tools/RegressionDump/expected-diffs`; S-REG PASS ⇔ różnice DOKŁADNIE oczekiwane.
4. API (`TD_Mgd` V25, refleksja): `Database` ma `BeginDeepClone`, `BeginDeepCloneTranslation`, `DeepCloneEnded`,
   `DeepCloneAborted`, `InsertMappingAvailable`, `WblockMappingAvailable`, `BeginInsert`/`InsertEnded`; `IdMapping`
   (`OriginalDatabase`, `DestinationDatabase`, `DeepCloneContext`), `IdPair` (`Key`, `Value`, `IsCloned`, `IsPrimary`).
   **Które zdarzenia BricsCAD wywołuje przy COPY/MIRROR/ARRAY/PASTE — rozstrzyga spike W2-0 (bramka).**
5. BricsCAD przy COPY NIE przemapowuje 1005 (`BarCopyWatcher.cs:162-163`, S13P) — zdanie w ARCHITECTURE.md:31-32 do poprawy w W2.

## 1. Decyzje domyślne (pytania do użytkownika — przyjęte domyślne, wpisane w RANO_DECYZJE.md)
1. #7 przy `LengthOverridden`: TotalLength bez zmian + ostrzeżenie `[RC AUTO] Pręt … ma ręcznie nadpisaną długość całkowitą … — nie zmieniona; sprawdź w RC_EDIT_BAR.`
2. #5: przy przebudowie 44 w miejscu zostaje **środek** pierścienia.
3. W2: pary rozkład↔opis ([11]/[16]) też z mapy (poprawka „3b” — ARRAY/wielokrotne COPY przypinają kilka rozkładów do jednego opisu).
4. Rozkład wklejony do INNEGO rysunku bez pręta: [18] czyszczone (BBS jak dla rozkładu „bez pręta”). Ctrl+V w tym samym rysunku = COPY.
5. Przepinanie z mapy po KAŻDEJ komendzie, która sklonowała obiekty (także grip-copy, Ctrl+przeciąganie); `EnsureUniqueBtr` zostaje na białej liście.
6. `RC_BAR_LINK` (wewnętrzny okrąg 44) po COPY przepinany z mapy (W2).
7. #2: EXPLODE/ERASE pręta bez etykiety usuwa jego rozkłady i opisy.
8. #4 przy zepsutym obrysie: kierunek/odbicie ze stanu sprzed komendy, axis[0] bieżący (jak wzorzec).
9. Nowy błąd wzorca #12 (p. 9.1) — NIE poprawiamy w W1/W2, wpis do POPRAWKI_WZORCA „do decyzji”.

## 2. Kroki (każdy: build staging + `dotnet test` + smoke kroku + S-REG polilinia i blok == expected-diffs)
| Krok | Treść | Zmiana działania |
|---|---|---|
| W1-0 | expected-diffs w `run-regression.ps1`, osobny zrzut przypadków (`RC_REG_FIX_*`), szkielet `PlanCFixW1Tests` | brak |
| W1a | #7 TotalLength | `07-*.txt` |
| W1b | #1 `BarGeometryWatcher.Suppress()` | `01-*.txt` (REC.bar3 4000→3500, F1*) |
| W1c | #2 kaskada bez etykiety | `02-*.txt`; aktualizacja E2D S11 |
| W1d | #4 `BarFrameRules` + ramka w `RebuildCompanions` | `04-*.txt` |
| W1e | #5 przebudowa 44 | `05-*.txt` |
| W2-0 | Spike zdarzeń klonowania (testy + log) | brak — **bramka** |
| W2a | `CloneMap` — tylko zapis map + log | brak |
| W2b | Przepinanie z mapy + strażnik kaskady + zawężenie heurystyk | `03-*.txt`, `06-*.txt` (+`03b`) |
| W2c | Dokumentacja | — |
#2 rozszerza wariant #3 (MIRROR z usunięciem źródła, 4.5) na pręty bez etykiety → **W1 i W2 oddać użytkownikowi razem**.

## 3. W1
### 3.1 #1 — RC_EDIT_BAR dolicza zmianę dwa razy
Mechanizm: `BarEditApply.Apply` (`:39-54`) otwiera polilinię → `BarGeometryWatcher.OnObjectOpenedForModify` (`:91-109`)
zapamiętuje obrys, `OnObjectModified` (`:115-135`) → `_pending`; `RebuildCompanions` (`BarEditApply.cs:72`) przesuwa
koniec; w `OnCommandEnded` (`:406-437`) `TryEndStretch` (`:150-209`) + `ApplyEndStretch` (`:262-292`) dolicza delta.
**Wybór: `BarGeometryWatcher.Suppress()` (IDisposable, zwiększa `_rebuildDepth`, `Dispose` dokładnie raz).** Odrzucone:
reset po komendach `RC_*` (wyłącza watcher dla wszystkich komend wtyczki — różnica spoza listy), `Forget(id)` (po fakcie,
nie obejmuje innych prętów np. Reconcilera).
- `BarEditApply.cs:23-143` — całe `Apply` w `using (BarGeometryWatcher.Suppress())` (polilinia i blok), kolejność bez zmian.
- `BarCommands.cs:1240-1264` (RC_UPDATE_BAR polilinia) — zapis XData, RebuildCompanions, Reconcile, Propagate, LabelCount w `Suppress()`.
- `EditCommands.cs:457-468` (RC_BAR_END polilinia) — zapis w `Suppress()`.
- Bez zmian: AutoRebar (`:1906-1912, 1961-1967`), `PunchingTagCommands.cs:114`, `PositionReconciler`.
- Grip/STRETCH użytkownika nie są wyciszone — `TryEndStretch` działa (testy W1-1e).
- Ryzyko zdarzeń odroczonych (ObjectModified po zakresie): test W1-1a; plan awaryjny `_ignoredThisCommand` (Apply wypełnia
  `editId`, `OnCommandEnded` usuwa z `toProcess`).

### 3.2 #2 — ERASE pręta bez etykiety
`AnnotMoveOverrule.cs:1135-1225` (`BarEraseCascade.Run`): `:1145` → `if (bar == null) return;`; `:1152-1185` (MLeader)
w `if (!string.IsNullOrEmpty(bar.LabelHandle)) { … }` (back-link bez zmian); rozkłady i opisy (`:1187-1219`) zawsze;
komentarz `:1130-1134`. Polilinie i bloki. `PunchingTagEngine.DeleteSummaryBars` (`:182-190`) — jak dziś dla 501/502.
EXPLODE = ERASE (pyt. 7). MIRROR Y — strażnik w W2b. Aktualizacja: `PlanCStage2dTests.cs:260-296` (S11 odwrócić), komentarz `:7`;
ARCHITECTURE.md:115, :348.

### 3.3 #4 — przebudowa zawsze w poziomie
`SingleBarEngine.cs:609-649` `RebuildCompanions` (`direction=(1,0,0)` `:633`, `startPoint = pt0 − lPts[0]` `:631`);
wywołania: `BarEditApply.cs:72`, `BarCommands.cs:1250`, `BarGeometryWatcher.cs:270` (ApplyEndStretch), `:512` (Plan B),
`:531`.
Nowy `src/Core/BarFrameRules.cs` (bez Teigha, CRLF, `<Compile Include>` w tests):
```csharp
internal readonly struct BarFrame2 { public double Angle; public bool Mirrored; }   // obrót lokal→świat; odbicie (y→−y przed obrotem)
internal static class BarFrameRules {
    public const double FitTol = 0.5;     // mm
    public const double SnapAngle = 1e-9; // |kąt| < SnapAngle i bez odbicia → tożsamość DOKŁADNIE (bit w bit z wzorcem)
    public static List<(double X,double Y)> LocalOutline(string shape, double[] pv, double d); // jak ComputeOutlineVertices :464-481
    public static bool TryFit(IReadOnlyList<(double X,double Y)> world, IReadOnlyList<(double X,double Y)> local, out BarFrame2 frame, out double maxDev);
    public static bool TryFitRing(IReadOnlyList<(double X,double Y)> world, out (double X,double Y) center, out BarFrame2 frame);
}
```
`TryFit`: `world.Count == local.Count ≥ 4`; kandydaci mirrored ∈ {false,true} (lokal `(x,−y)`); `k` = wierzchołek najdalszy
od `local[0]`; `Angle = atan2(world[k]−world[0]) − atan2(L[k]−L[0])`; przewidywane `world[0] + R(Angle)(L[i]−L[0])`, `maxDev`;
lepszy kandydat; akceptacja `maxDev ≤ FitTol`; zatrzaśnięcie kąta. Wierzchołek po wierzchołku (rozróżnia odbicie prostego
i symetrycznych, wykrywa zepsuty obrys). `TryFitRing`: środek = średnia; kierunek v0−c; odbicie = cross(v0−c, v1−c) < 0;
poprawny gdy |vi−c| stałe ±FitTol.
`SingleBarEngine`: `ComputeOutlineVertices` (`:447`) i `BuildVisualEntities` (`:508`) + `bool mirrored = false`
(`(px,py) = (ay,−ax)` gdy mirrored); `FrameFromOutline(outline, shapeBar, fallbackOutline = null)` → `BarFrame` (kąt,
Direction, Mirrored, IsIdentity), próby: TryFit (44: TryFitRing) → fallback → kierunek z cięciwy osi → tożsamość;
`RebuildCompanions(db, id, bar, Point3d? axisStartOverride = null, BarFrame? frame = null)`: `frame ??= FrameFromOutline(obecny, bar)`;
`startPoint = pt0 − R(frame)·lPts[0]`; przy `IsIdentity` DOKŁADNIE dawna formuła i `(1,0,0)`; komentarz `:602-607`.
Ramka jawnie: `BarEditApply.Apply` — przed zapisem XData `frame = FrameFromOutline(obrys, bar STARY)` → `RebuildCompanions(…, updated, null, frame)`
(też przy zmianie kształtu); watcher — `preFrame = FrameFromOutline(pre, bar)` PRZED `TryEndStretch` (`:467`; ten zmienia `bar`),
`ApplyEndStretch(…, preFrame)` → `RebuildCompanions(…, axisStart, preFrame)` (`:270`); Plan B (`:512`) i zepsuty 00 (`:531`):
`FrameFromOutline(obecny, bar, fallbackOutline: pre)`; RC_UPDATE_BAR — domyślnie.
**Pręty poziome nieodbite: ramka = tożsamość dokładnie → bit w bit jak wzorzec** (test W1-4f).

### 3.4 #5 — kształt 44
`SingleBarEngine.cs:616` (`DeleteLinkedEntities`) przed `:621-625`. Gałąź 44 przed `DeleteLinkedEntities` → `Rebuild44(tr, pline, bar, frame)`:
ramka `TryFitRing` (zapas: jawna / `pre` / średnia, (1,0), bez odbicia); środek zostaje: `start = c − R(frame)·(A/2, A/2)`;
`BuildVisualEntities(shape44, pv, d, start, dir, mirrored)` → [zewn., wewn.]; zewnętrzny — podmiana wierzchołków (`:638-646`);
wewnętrzny — `RC_BAR_LINK` → główna w `pline.OwnerId`: dokładnie jedna polilinia → przepisz wierzchołki, inaczej usuń
wszystkie i dodaj nową (`SetPropertiesFrom`, `WriteLinkXData`). Zmiana na 44: `start = axis[0]` starego; z 44: dawna ścieżka.
Watcher Plan B (`:500-520`) — pełna przebudowa (MOVE przenosi oba, ROTATE zostaje, SCALE cofany).

### 3.5 #7 — TotalLength
`BarCommands.cs:1244` (RC_UPDATE_BAR polilinia), `BarGeometryWatcher.cs:557` — po `LengthA`: `!LengthOverridden` →
`TotalLength = BarLengthRules.TotalLengthFor(ShapeCode, ParamValues, Diameter)` (try/`Log.Error`); nadpisana → bez zmian +
ostrzeżenie (pyt. 1; ten sam komunikat w `TryEndStretch` przy `:198`). BBS bierze TL tylko przy nadpisaniu
(`BbsDrawingReader.cs:181-199`, `BarScheduleEngine.cs:67`) → w S-REG różnica [11].

## 4. W2 — mapa klonowania
### 4.1 W2-0 spike (bramka; `tools/BrxSmokeTests/PlanCFixW2Spike.cs`, bez zmian we wtyczce)
Podpiąć na `Database`: BeginDeepClone, BeginDeepCloneTranslation, DeepCloneEnded, DeepCloneAborted, InsertMappingAvailable,
WblockMappingAvailable, BeginInsert, InsertEnded, ObjectAppended, ObjectErased. Logować komendę, kolejność, DeepCloneContext,
Original==Destination, pary IsCloned/IsPrimary, handle klucz→wartość, stan [13]/[18]/[1] klonów w CommandEnded.
Komendy: `_.COPY`, `_.MIRROR` (N/Y), `_-ARRAY` 1×3 (ARRAYASSOCIATIVITY=0) + ARRAY asocjacyjny (obserwacja), COPYCLIP+PASTECLIP
w tym samym rysunku, PASTEBLOCK, PASTEORIG, `_-WBLOCK` + `_-INSERT *plik`; kolejność BeginDeepCloneTranslation vs kaskada Erase (MIRROR Y).
Decyzje z wyniku: (a) zdarzenia (domyślnie BeginDeepCloneTranslation + InsertMappingAvailable, deduplikacja), (b) konteksty
(Copy, Insert, InsertCopy; Objects jeśli COPY go używa), (c) przestrzeń handle w XData klonów przy PASTE, (d) ARRAY: mapa
na kopię czy łańcuch. **Brak mapy przy COPY → W2 STOP, pytanie do użytkownika.**

#### 4.1.1 Wynik spike'a (2026-10-09, BricsCAD V25, `run-planc-w2spike.ps1`, log `planc-w2spike-log.txt`) — BRAMKA: PRZESZŁA
BDCT = `BeginDeepCloneTranslation`. „XData klonów” = stan 1005 w CommandEnded (BarCopyWatcher odłączony — surowy BricsCAD).

| Komenda | Zdarzenia (kolejność) | DeepCloneContext | Orig==Dest | XData 1005 klonów |
|---|---|---|---|---|
| `_.COPY` | BeginDeepClone (0 par) → BDCT (10 par, klucz = oryginał, IsPrimary) → ObjectAppended klonów → DeepCloneEnded | Copy | tak | NIE przetłumaczone — wskazują oryginały (potwierdza S13P) |
| `_.MIRROR` N | jak COPY | Copy | tak | nie przetłumaczone |
| `_.MIRROR` Y | **brak zdarzeń klonowania** — BricsCAD V25 odbija obiekty w miejscu (transformacja), nic nie usuwa | — | — | bez zmian (poprawne) |
| `_-ARRAY` R 1×3 | 2 × (BeginDeepClone → BDCT → DeepCloneEnded); w obu paczkach klucze = ORYGINAŁY | Copy | tak | nie przetłumaczone |
| `_.COPYCLIP` | BDCT (Ho→Ht, z zawartością BTR) → DeepCloneEnded; **brak** WblockMappingAvailable / BeginWblock* | Wblock | nie (dest = baza tymczasowa) | odwołania do obiektów spoza schowka → **0** |
| `_.PASTECLIP` (ten sam rysunek) | BeginInsert(A$*.dwg) → BeginDeepClone → InsertMappingAvailable (Ht→Hp, do bloku pomocniczego) → BDCT (te same pary) → DeepCloneEnded → InsertEnded → BeginDeepClone → BDCT **Copy** (Hp→Hd, do modelu) → DeepCloneEnded | InsertCopy, potem Copy | nie, potem tak | wewnątrz schowka przetłumaczone na KOŃCOWE klony; spoza schowka = 0 (np. `[18]=0` rozkładu bez pręta) |
| `_.PASTEBLOCK` | jak 1. faza PASTECLIP (do definicji nowego bloku), bez fazy Copy | InsertCopy | nie | przetłumaczone (obiekty RC wewnątrz bloku) |
| `_.PASTEORIG` (ten sam rysunek) | brak zdarzeń, nic nie wklejono (PASTEORIG niedostępne w rysunku źródłowym) | — | — | — |
| `_-WBLOCK` | BDCT (Ho→Hf) → DeepCloneEnded → ObjectErased oryginałów (WBLOCK usuwa wybrane obiekty) | Wblock | nie | spoza = 0 |
| `_-INSERT *plik` | 2 × (BeginInsert → BeginDeepClone → InsertMappingAvailable → BDCT → DeepCloneEnded → InsertEnded): 1. do definicji bloku, 2. do modelu | InsertCopy | nie | przetłumaczone; spoza = 0 |
| `_.COPY` + `'_.ZOOM` | WillStart ZOOM i Ended ZOOM z CMDNAMES=`COPY` PRZED klonowaniem; potem paczka COPY, Ended COPY (CMDNAMES puste) | Copy | tak | — |
| `_.COPY` `_O _M` (2 kopie) + `_U` w COPY | 2 paczki Copy (klucze = oryginały); `_U` usuwa klony 2. kopii (ObjectErased w trakcie COPY) → `BarBlockEraseOverrule` idzie po NIEPRZEPIĘTYM `[11]` klonu i **usuwa opisy ORYGINAŁÓW** (błąd istniejący, wzorzec też) | Copy | tak | — |
| EnsureUniqueBtr (BarCopyWatcher, CommandEnded) | BeginDeepClone → BDCT → DeepCloneEnded, CMDNAMES puste | Objects | tak | — |
| ESC w COPY | nie da się wysłać ze skryptu — test ręczny | | | |

Decyzje:
- **(a)** Zapis map z `BeginDeepCloneTranslation` (przychodzi we wszystkich przypadkach, także insert — na subskrypcji bazy dokumentu)
  + `InsertMappingAvailable`; deduplikacja (identyczna paczka tej samej bazy docelowej jest pomijana). `DeepCloneAborted` usuwa
  ostatnią paczkę. ObjectAppended niepotrzebne.
- **(b)** Przepinane konteksty: `Copy`, `Insert`, `InsertCopy`. `Wblock` — tylko z komend schowka (CMDNAMES: COPYCLIP / CUTCLIP /
  COPYBASE) → mapa schowka Ht→Ho + `_clipOrigin`; `-WBLOCK` jej nie nadpisuje (0-POPRAWKI p.3). `Objects` (DeepCloneObjects
  wtyczki) — pomijany. Klony poza przestrzenią modelu/papieru (definicje bloków: PASTEBLOCK, 1. faza `-INSERT *`, blok
  pomocniczy PASTECLIP) — pomijane.
- **(c)** Przestrzeń handle: `Copy` w tym samym rysunku — wartości XData klonów = handle ORYGINAŁÓW (klucze paczki);
  `InsertCopy` — wartości już w przestrzeni rysunku docelowego (sklonowane: przetłumaczone, niesklonowane: 0); 2. faza
  PASTECLIP — przetłumaczone na końcowe klony. Reguła: wartość = klon tej komendy → „AlreadyClone”; klucz paczki (tylko
  Orig==Dest) → „Cloned”; inaczej „NotCloned”. Wklejenie ze schowka do TEGO SAMEGO rysunku (Dest == `_clipOrigin`):
  NotCloned odtwarzane z obiektu źródłowego (łańcuch paczek Hd→Hp→Ht + mapa schowka Ht→Ho; kontrola: ta sama aplikacja
  XData i to samo pole [1] Mark) — jak COPY (pyt. 4).
- **(d)** ARRAY: osobna paczka na każdą kopię, klucze = oryginały (nie łańcuch). Łańcuch (root) potrzebny dla PASTECLIP (Hd ← Hp ← Ht).
- **MIRROR Y** nie usuwa źródła w BricsCAD V25 → strażnik kaskady 4.5 zostaje jako zabezpieczenie (dziś bez skutku); F3c = kontrola.
- **Komenda zagnieżdżona (0-POPRAWKI p.5):** w CommandEnded komendy widoku / przezroczystej albo gdy trwa inna komenda
  (`SingleBarGripOverrule.IsViewOrTransparentCommand` / `OtherCommandInProgress`) — bez `Take`, bez czyszczenia zbiorów
  BarCopyWatcher (paczki zostają do końca komendy nadrzędnej).
- **Anulowanie kolejnej kopii w COPY (`_U`)**: kaskada usuwania rozkładu-klonu (`BarBlockEraseOverrule`) w trakcie komendy
  tłumaczy `[11]` przez paczkę (`CloneMap.TryResolvePending`) — opis oryginału nie jest usuwany (RANO_DECYZJE D8).
### 4.2 `src/Core/CloneMap.cs`
`DocumentWatch.Subscribe("CloneMap", …)` w PluginApp PRZED BarCopyWatcher (`PluginApp.cs:24-27`); `Forget(db)` przy odłączeniu.
Handler tylko zapisuje: `Batch { Dest; CrossDb; Ctx; Dictionary<long keyHandle, ObjectId clone>; HashSet<long> cloneHandles }`
(pary IsCloned, Value niepuste; klucz `p.Key.Handle.Value` bez otwierania — działa dla usuniętego później źródła).
Paczki per baza docelowa w kolejności; `root(V) = root(K)` gdy K jest klonem z wcześniejszej paczki tej komendy (łańcuch ARRAY);
dwa klucze o tym samym root w paczce → niejednoznaczność → brak przypisania dla wszystkich. `Suppress()` obejmuje własne
`DeepCloneObjects` (`BarCopyWatcher.EnsureUniqueBtr :574`) i `HandleCommandFinish`. Schowek: `WblockMappingAvailable`
(Wblock/WblockObjects) na bazie dokumentu → `_clipOrigin = WeakReference(db)` + mapa Ho→Ht; wklejanie z Dest == _clipOrigin
= ten sam rysunek. `DeepCloneAborted` usuwa ostatnią paczkę; komendy undo czyszczą. API: `Take(db)`, `Forget(db)`,
`IsPendingCloneRetarget(db, distId, sourceBarHandle)`.
### 4.3 Reguły (`CloneRemap.Apply(db, batches)`)
`resolve(h,b)`: Cloned / AlreadyClone / NotCloned.
| Pole klonu | Cloned | NotCloned, ten sam rysunek | NotCloned, inny rysunek |
|---|---|---|---|
| pręt [13] | klon etykiety TYLKO gdy jej [1] → TEN pręt (para dwustronna, #6); inaczej "" | "" (jak wzorzec) | "" |
| etykieta [1] | klon pręta, gdy jego [13] → ta etykieta; inaczej bez zmian | bez zmian | "" |
| rozkład [18] | klon, gdy prętem (polilinia lub v2) (#3) | **bez zmian (oryginał)** | "" |
| RC_BAR_LINK [1] | klon pręta | bez zmian | "" |
| rozkład [11] / opis [16] | klon (3b) | [11] "", [16] bez zmian (jak wzorzec) | "" |
Etykieta przypisana >1 prętowi → wszystkim "" + `Log.Info`. Zapis: `BarXData.Write`/`BarEntity.Write`, `BarBlockEngine.WriteXData`,
`XLink.Write`; 1000 czytane `XLink.Read`, zapis 1005. `PendingLabelUpdates.Add` dla starego i nowego [18] i zmienionych [13].
Jedna transakcja w `CloneMap.Suppress()` + `BarGeometryWatcher.Suppress()`.
### 4.4 `BarCopyWatcher.HandleCommandFinish` (`:92-122`)
1) undo → `CloneMap.Forget`, koniec; 2) `batches = CloneMap.Take(db)`; 3) isCopyLike → `EnsureUniqueBtr` (rozkłady, opisy,
pręty-bloki `*U`); 4) batches → `CloneRemap.Apply` (niezależnie od isCopyLike); 5) `RemapCopiedPairs` (`:373-540`) tylko dla
rozkładów spoza mapy; 6) `RemapCopiedBarLabels` (`:131-313`) tylko dla prętów spoza mapy (zapas). Puste catch w dotykanych
(`:411, 431, 447, 538, 370, 591`) → `Log.Error`.
### 4.5 Strażnik kaskady (`AnnotMoveOverrule.cs:1192-1219`)
`if (CloneMap.IsPendingCloneRetarget(db, oid, barBlock.SourceBarHandle)) continue;` — sklonowany rozkład, którego pręt też
sklonowano w tej komendzie, nie jest usuwany (MIRROR Y); jego opis też zostaje.
### 4.6 Ryzyka W2
UNDO/REDO (W2-11), grip drag (klony tymczasowe pomijane — IsNull/IsErased/IsValid), wiele rysunków (paczki per Dest,
`_clipOrigin` WeakReference), reentrancja (handler bez zapisów; EnsureUniqueBtr w Suppress), ARRAY asocjacyjny poza zakresem,
stare rysunki (krótkie rekordy rozkładu bez [18] pomijane).

## 5. S-REG: oczekiwane różnice
### 5.1 Mechanizm (W1-0, `tools/RegressionDump/run-regression.ps1`)
Katalogi `expected-diffs/polyline/` (surowy) i `expected-diffs/block/` (bar-equiv); plik `NN-<opis>.txt`:
```
# bug: 1
# opis: RC_EDIT_BAR dolicza zmianę dwa razy (wzorzec 4000, nasz 3500, H12-09)
# zrzut: main | fix
<= <linia dokładnie jak we wzorcu>
=> <linia dokładnie jak u nas>
```
Porównanie jako multizbiór (bez linii `#`), osobno `main` i `fix`. PASS ⇔ rzeczywisty diff == suma oczekiwanych.
Raport: NIEOCZEKIWANE (BLOKER) i BRAKUJĄCE. `# bug:` musi być w `docs/POPRAWKI_WZORCA.md`. `-ProposeExpected` →
`bin/out/proposed-*.txt` (nigdy do `expected-diffs/` — przypisanie robi człowiek). `-BlockInfoOnly` tylko dla zgodności.
### 5.2 Zrzut przypadków poprawek
`reg.scr`: po `RC_REG_DUMP` → `RC_REG_FIX_SETUP` → komendy skryptu → `RC_REG_FIX_CHECK` → `RC_REG_DUMP_FIX`. Pas
`Y = 100000 + 10000·i` na przypadek; `DrawingDump.Run(db, barEquiv, filter)` per przypadek (lokalna numeracja, `@ext:`/`@erased`)
+ wiersze semantyczne `F04a bar Mark=… A=… TL=… ovr=… angle=… mirrored=…`, `dist [18]=#k count=…`, `label "…"`.
Przypadki tylko-polilinia (F1e, F4d, F7a–c) pomijane w przebiegu Block (`$PolylineOnlyCases`). Tylko API obecne w obu wersjach.
### 5.3 Przypadki (wzorzec → nasz)
| Id | Krok | Scenariusz | Wzorzec | Nasz |
|---|---|---|---|---|
| main REC.bar3 | W1b | `RC_REG_RECONCILE` | 4000 | 3500 |
| F1b | W1b | 21 + etykieta + rozkład; C 500→700 | 900 | 700 |
| F1c | W1b | 00 3000→1000 | 1000 | 1000 (kontrola) |
| F1e | W1b | `_.STRETCH` końca 00 +500 | 3500 | 3500 (kontrola wariantu B) |
| F2a | W1c | 00 bez etykiety + rozkład + opis, ERASE | zostają | usunięte |
| F2b | W1c | kopia z [13] → cudza etykieta, ERASE kopii | etykieta zostaje | to samo (kontrola) |
| F4a | W1d | 21 ROTATE 30° | poziomy | 30° |
| F4b | W1d | 00 ROTATE 30°, A 3500 | poziomy | 30°, axis[0] w miejscu |
| F4c | W1d | 21 + etykieta, MIRROR N | kopia nieodbita | odbita |
| F4d | W1d | 00 30°, STRETCH końca | poziomy | 30° |
| F5a | W1e | 44 MOVE | wewn. usunięty | oba przesunięte |
| F5b | W1e | 44 A 300→400 | wewn. usunięty | oba, środek w miejscu |
| F7a | W1a | XData A=2500/TL=2500 (watcher `:557`) | A 3000 TL 2500 | A 3000 TL 3000 |
| F7b | W1a | jw. z LengthOverridden TL 3333 | 3333 | 3333 (kontrola) |
| F7c | W1a | TL 2000, RC_UPDATE_BAR | 2000 | 3000 |
| F3a | W2b | COPY pręt+etykieta+rozkład+opis | [18] kopii → oryginał; 2n / bez liczby | [18] → kopia; n / n |
| F3b | W2b | COPY rozkład + opis | → oryginał | to samo (kontrola) |
| F3c | W2b | MIRROR Y zestawu | odbity rozkład usunięty | zostaje, [18] → odbity pręt |
| F3d | W2b | `_-ARRAY` 1×3 | wszystkie → oryginał | każdy → swój |
| F3e | W2b | WBLOCK rozkładu + INSERT* | handle z pliku | "" |
| F6 | W2b | A bez etykiety, B z etykietą przy A; COPY A, B, etykiety B | etykieta przy A′ | przy B′ |
| F5c | W2b | COPY 44, Apply oryginału | wewn. kopii usunięty | zostaje |
Przed zamrożeniem: przebieg wzorca 3× (deterministyczność F6/F3d); niepowtarzalny → przebudować przypadek.

## 6. Testy automatyczne
### 6.1 `PlanCFixW1Tests.cs` + `planc-w1.scr` + `run-planc-w1.ps1` (`RCSMOKE_W1_*`)
1a 00+etykieta+rozkład Apply 3500 → po CommandEnded A=TL=3500, rozkład [8]=3500, oś obrysu 3500, Mark bez zmian; 1b 21 C dokładnie;
1c 3000→1000; 1d blok 3500; 1e STRETCH końca 00 +500 → 3500 + rozkład; obrócony 30° → A+500, kąt 30°.
2a polilinia bez etykiety ERASE → usunięte; `_.U` → przywrócone; `_.REDO` → usunięte; 2b blok (zastępuje E2D S11); 2c kopia z [13]→cudza etykieta.
4a 21 ROTATE 30° ±1e-6; 4b MIRROR N indeksami; 4c 00 30° Apply → kąt, axis[0], grot; 4d odbity 21 Apply B → odbity;
4e 21 30° jeden wierzchołek przesunięty → pręt 30° w bieżącym axis[0]; 4f poziomy Apply → obrys == `ComputeOutlineVertices(…,(1,0,0))`.
5a 44 MOVE → 2 pierścienie; 5b A 400 → promienie 200±d/2, środek, 1 RC_BAR_LINK; 5c ROTATE 45°; 5d `_.U`; 5e 00↔44 bez wyjątku.
7a/7b/7c. R: E1/E2/E3 PASS (aktualizacja E2D S11, E3 Z0 → asercja 3500).
Jednostkowe `tests/BarFrameRulesTests.cs`: 00/11/21/13/99 obrót 0/30/90/180/−45, odbicie, przesunięcie; prosty odbity; zepsuty;
kąt 1e-12 → tożsamość; rozciągnięty → false; TryFitRing.
### 6.2 `PlanCFixW2Tests.cs` + `planc-w2.scr` + `run-planc-w2.ps1` (`RCSMOKE_W2_*`, polilinia i blok)
0 spike; 1 COPY zestawu; 2 COPY rozkładu → oryginał, 2n; 3 COPY pręta → [13]=""; 4 MIRROR N; 5 MIRROR Y; 6 `_-ARRAY` 1×3;
7 #6; 8 WBLOCK+INSERT*; 9 rekordy 1000 → 1005; 10 44 COPY; 11 `_.U`/`_.REDO`; 12 dwa rysunki; 13 EnsureUniqueBtr bez paczki;
14 E3 S13/S16/S17 na mapę, S13P → asercja.

## 7. Testy ręczne (dla użytkownika)
Po W1: RC_EDIT_BAR 3000→3500 (brak „(grip)”); U-bar ostatnie ramię; STRETCH/grip końca jak dotąd; ERASE pręta bez etykiety usuwa
rozkład (Ctrl+Z); ROTATE/MIRROR U-bara zostaje; RC_EDIT_BAR obróconego; 44 MOVE i zmiana średnicy; nadpisana długość + STRETCH → ostrzeżenie.
Po W2: COPY kompletu → edycja kopii zmienia tylko jej rozkład, etykiety n/n; COPY samego rozkładu → 2n; MIRROR Y; ARRAY 1×3;
Ctrl+C/Ctrl+V do innego rysunku; kopiowanie gripem i Ctrl+przeciąganie.

## 8. Dokumentacja
ARCHITECTURE.md (:31-38 mapa zamiast „BricsCAD przemapowuje”, :112-115 kaskada, :129-131 Suppress, moduły BarFrameRules/CloneMap,
p. 6 i :348), POPRAWKI_WZORCA.md (statusy, 3b, #12), PLAN_C_etap3.md (0-POPRAWKI p.5 rozwiązane w W2; §5 polilinia zgodna z blokiem),
`RegCommands.cs:175-177`.

## 9. Poza zakresem
9.1 **Nowy błąd wzorca #12** (`BarGeometryWatcher.cs:189-194, 474, 541-562`): grip skracający odcinek poniżej max(2d,50) —
`TryEndStretch` wypisuje „zmiana cofnięta”, ale `ProcessPending` ścieżką 00 PRZYJMUJE krótką długość. Nie poprawiamy bez zgody.

## 10. Checklista recenzji
Build, `dotnet test`, `run-planc-w1/w2` PASS, E1–E3 PASS; S-REG == expected-diffs; poziome nieodbite bit w bit (W1-4f);
zero zapisów w podglądzie gripa; `Suppress` zawsze w `using`; handler CloneMap bez zapisów; XData przez BarXData/BarEntity/XLink;
AutoRebar, PositionCounter, BarScheduleEngine, BbsDrawingReader bez zmian; CRLF, PL, InvariantCulture, brak nowych pustych catch.
