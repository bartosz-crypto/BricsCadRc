# Uwagi dla autora BricsCadRc — zmiany aba0ed1..bd3efa2 (2026-10-09, część b)

Przy przenoszeniu Twoich zmian do NA Smart RC (`docs/PLAN_PRZENIESIENIA_2026-10-09.md`) znaleźliśmy trzy rzeczy, które
warto poprawić także u Ciebie.

## 1. Kształt 46 (9d565be) — nowy parametr wstawiony w środek listy

W 9d565be lista parametrów kształtu 46 zmieniła się z `{A, B, C, E}` na `{A, B, C, D, E}`. XData pręta
(`RC_SINGLE_BAR`) jest **pozycyjne**: parametr i kształtu → pole LengthA…E o indeksie i ([4] A, [5] B, [6] C, [9] D, [10] E
w kolejności `Parameters`). Rysunek zapisany przed 9d565be ma dla 46 w czwartym polu wymiar **E**:

- po aktualizacji to samo pole jest czytane jako **D** (głębokość korby), a **E = 0**,
- długość cięcia `A + 2B + C + E` liczy się z E = 0 — **za krótki pręt w BBS, bez żadnego ostrzeżenia**,
- obrys przebudowany (MOVE, RC_EDIT_BAR) dostaje D = dawne E i brak ostatniej półki.

Zasada, której się trzymamy u siebie: **nowe parametry kształtu tylko dopisywać na końcu listy** (u nas 46 =
`{A, B, C, E, D}`; stare rekordy mają w nowym polu 0 → wartość domyślna), albo — jeśli kolejność musi się zmienić —
jawna migracja rekordów przy odczycie (np. po znaczniku wersji kształtu na końcu rekordu), z ostrzeżeniem w BBS.

## 2. Komentarz przy `TagWithSlab` i znacznik płyty przy COPY

W `AutoRebarEngine.TagWithSlab` komentarz mówi „1005: COPY płyty z rozkładami przemapuje znacznik”. W BricsCAD V25 tak
nie jest: przy COPY / MIRROR / ARRAY pola 1005 klonów wskazują oryginały (sprawdzone testem w prawdziwym BricsCAD).
Skutek: po COPY płyty razem z rozkładami znacznik `RC_AUTOREBAR_SLAB` kopii wskazuje obrys **oryginału**, więc ponowne
generowanie (np. B1) na oryginale **kasuje rozkłady skopiowanej płyty** (u nas test: 3 z 3 rozkładów kopii usunięte).
Twoje bc2055e (mapa klonowania) tego znacznika nie przepina. U nas (K5): znacznik kopii → klon obrysu, gdy płytę
skopiowano razem; kopia samych rozkładów → znacznik pusty (przynależność z testu punktu w obrysie). To samo dotyczy
wymiarów ADD (`RC_ADD` [1]).

## 3. Płyty „GARAGE …” (Prepare GA i raport przebicia)

Na projekcie Holbrook garaże są osobnymi płytami z opisem „GARAGE 321” (SD-Text w obrysie SD-PILED-RAFT, layout GA3215,
w raporcie przebicia nagłówek „GARAGE 321 (4 piles)”, pale G32101…). U Ciebie:

- `GaImportEngine.IsPlotLabel` (`^PLOTS?\s+\S`) nie widzi garaży — RC_PRZYGOTUJ_GA nie pokazuje ich na liście płyt,
- `PunchingReport.Read` rozpoznaje nagłówek płyty tylko po „PLOT” (`PlotRx` + `StartsWith("PLOT")`) — wiersz
  „GARAGE 321 (4 piles)” jest pomijany, a pale garaży **dopisują się do poprzedniej płyty**: w raporcie part 1 PLOT 538 ma
  23 pale zamiast 10 (+ ostrzeżenie „nagłówek mówi 10 pali, w tabeli 23”), a tagi PH dla G32101… lądują na PLOT 538,
- `PunchingAutoEngine.Apply` kasuje każdą notatkę MANUAL-NOTE, której płyta nie zaczyna się od „PLOT”
  (`!plot0.StartsWith("PLOT")`) — notatka garażu zniknęłaby przy przebiciu dowolnej innej płyty.

U nas (POPRAWKI_WZORCA 22): wspólne reguły nazw `PlotLabelRules` — opis GA `^(PLOTS?\s+\S|GARAGES?\s+\d)`, nagłówek
raportu: PLOT dokładnie jak dawniej + „GARAGE … (n piles)”, klucz płyty w RC_PH: „PLOT …” albo „GARAGE n…” (inny / pusty =
stara notatka bez płyty — sprzątana jak dawniej). Lista płyt w GA: najpierw PLOT, potem GARAGE (domyślnie pierwszy PLOT).
Uwaga przy okazji: `GaTitleEngine` szuka numeru płyty `\bPLOT\s+(\d+)` w TITLE_1 — garaż bez DRAWING_NUMBER w GA nie dostanie
zapasowego numeru rysunku RC.
