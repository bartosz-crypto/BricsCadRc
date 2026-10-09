# Plan C — pręt jako blok z własnymi gripami

Cel: zachowanie pręta zbliżone do ASD (własne gripy, przeliczanie długości, pozycji, rozkładów i BBS)
bez własnych typów obiektów C++ — na zwykłych blokach + overrule'ach. Bez wtyczki rysunek nadal
zawiera zwykłe bloki (bez proxy).

Gałąź bazowa: `main-origin` (= `origin/main`, wersja kolegi z GitHuba).

**Zasada nadrzędna:** wersja kolegi (`origin/main` z 2026-10-08, commit `aba0ed1`) jest wzorcem zasad
inżynierskich i logiki. Plan C zmienia wyłącznie to, CZYM jest pręt na rysunku (blok z gripami zamiast polilinii),
a nie to, CO program liczy i rysuje. Numeracja, rozkłady, opisy, AutoRebar, BBS, otuliny, zakłady — wynik na
rysunku ma być taki sam jak u kolegi. Każda różnica wyniku względem wzorca to błąd (BLOKER), chyba że
użytkownik zgodził się na nią wprost.

## Stan wyjściowy
- Pręt = `Polyline` z XData `RC_SINGLE_BAR`; odwołuje się do niej 16 plików w `src`.
- Rozkłady i opisy to już `BlockReference` z działającymi gripami (`AnnotGripOverrule`).
- `GripOverrule` na natywnej `Polyline` nie działa w BricsCAD; na `BlockReference` działa.
  Szkielet: `src/Core/SingleBarGripOverrule.cs`.
- Wariant B (obecny): grip końca obrysu zmienia odcinek końcowy (`BarGeometryWatcher.TryEndStretch`),
  ruch w środku pręta jest cofany.

## Etapy
Każdy etap: build → recenzja ChatGPT → test użytkownika w BricsCAD → commit użytkownika.

1. **Próba (go / no-go).** Testowa komenda `RC_TEST_BARBLOCK`: jeden pręt jako blok z gripami na końcach.
   Test: grip drag, ESC, UNDO/REDO, COPY, MIRROR, ARRAY, kopiowanie między rysunkami, STRETCH oknem,
   wydruk, otwarcie bez wtyczki. Istniejący kod nietknięty.
2. **Warstwa pośrednia `BarEntity`** — jeden punkt odczytu/zapisu pręta (oś, obrys, XData, etykieta,
   encje towarzyszące `RC_BAR_LINK`) dla polilinii i bloku. Zachowanie wtyczki bez zmian.
   Obowiązkowo przepiąć też **filtry i skanowanie rysunku**, które dziś zakładają polilinię, m.in.:
   `BbsDrawingReader.cs:54` (pomija `BlockReference`), `PositionReconciler.cs:37/50` (tylko polilinie),
   `BarCommands.cs:136` (wybór pręta do rozkładu), AutoRebar, aktualizacja etykiet, overrule'e usuwania.
   Pełną listę daje wyszukanie `RC_SINGLE_BAR` / `Polyline` w `src`.
3. **Nowe pręty jako bloki.** `SingleBarEngine` tworzy `BlockReference` do BTR `RC_BAR_nnn`
   (wstawienie = początek pręta, obrót = kierunek). XData `RC_SINGLE_BAR` bez zmian + `[14]` wersja formatu = 2.
   `RC_EDIT_BAR` / `RC_UPDATE_BAR` przebudowują zawartość BTR — **ale najpierw sprawdzają, czy BTR nie jest
   współdzielony** (kopia zrobiona COPY albo bez wtyczki); jeśli jest, pręt dostaje własny BTR przed przebudową.
   `BarCopyWatcher` od tego etapu nadaje kopiom prętów unikalne BTR (jak dziś rozkładom).
   Test przed odbiorem: rysunek mieszany (polilinie + bloki): konflikt pozycji, rozkład z nowego pręta,
   ilości i długości w BBS; edycja kopii nie zmienia oryginału.
4. **Gripy pręta.** Końce pręta, końce odcinków prętów giętych. W trakcie przeciągania tylko podgląd,
   zapis w `CommandEnded`, ESC bez zmian. Potem: długość, `PositionReconciler`, rozkłady, etykieta, BBS.
5. **Reakcje na edycję.** `BarGeometryWatcher`, overrule usuwania bloku pręta (kaskada jak dla polilinii,
   z back-linkiem), grot etykiety MLeader.
6. **Stare rysunki.** Odczyt pręta-polilinii zostaje na stałe. `RC_CONVERT_BARS` zamienia stare pręty
   na bloki w **jednej transakcji**: utworzenie bloku (razem z encjami towarzyszącymi `RC_BAR_LINK`, np. kształt 44)
   → przepięcie powiązań przez `XLink` (etykieta z back-linkiem, rozkłady `[18]`) → dopiero potem usunięcie
   starej polilinii **z wyłączoną kaskadą** `BarPolylineEraseOverrule` (inaczej skasuje etykietę i rozkłady).
   Błąd = wycofanie całej transakcji. Test: konwersja z rozkładami, kształt 44, MOVE, ERASE, UNDO/REDO.
7. **Porządki.** ARCHITECTURE.md, testy jednostkowe geometrii gripów/długości, opcjonalnie „napraw powiązania”.

## Cel docelowy: zachowanie jak w ASD (decyzja użytkownika 2026-10-08)
Zasady i wyniki — wzorzec kolegi; zachowanie obiektów — jak ASD. Priorytety użytkownika (wszystkie cztery):
gripy pręta z przeliczaniem (etapy 3f–4), gripy prętów giętych (etap 4), **panel właściwości** i **żywa tabela zestawienia**.

8. **Panel właściwości „NA Smart RC”** (dokowana paleta WPF, `PaletteSet`): zaznaczony pręt / rozkład / opis →
   pola (średnica, kształt, wymiary A–E, warstwa B1/B2/T1/T2, rozstaw, liczba, opis) z edycją. Zapis przez tę samą
   logikę co `RC_EDIT_BAR` / `RC_EDIT_DISTRIBUTION` (`BarEditApply`, etap 3d) — numeracja, rozkłady, opis, etykieta,
   BBS przeliczane jak przy komendzie. Działa dla polilinii i bloków (przez `BarEntity`). Odświeżanie przy zmianie
   zaznaczenia (`ImpliedSelectionChanged`), bez zapisów w trakcie wyboru; jedna edycja = jeden Ctrl+Z.
   (Standardowe okno Properties BricsCAD wymaga COM/OPM — odrzucone na start.)
9. **Żywa tabela zestawienia na rysunku:** `Table` z danymi z `BbsDrawingReader` / `BarScheduleEngine`, oznaczona
   XData, przebudowywana po komendach zmieniających pręty/rozkłady (`CommandEnded`, z licznikiem reentrancji,
   nie po undo), format jak BBS kolegi; ręczne „odśwież” jako zapas.

Kolejność: 3b–3f → 4 → 8 → 9 → 5–7 (porządki i konwersja na końcu). Etapy 8–9 mogą iść równolegle z 4,
bo nie zależą od formatu pręta (działają przez `BarEntity`).

## Ryzyka
- Bloki zagnieżdżone (pręty w detalach), liczba definicji bloków w dużych rysunkach.
- STRETCH oknem działa na bloki inaczej niż na polilinie — rozstrzyga etap 1.
- Zmiana uchwytów przy konwersji starych rysunków — powiązania tylko przez `XLink`.
