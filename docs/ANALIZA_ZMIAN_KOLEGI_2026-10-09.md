# Analiza zmian kolegi aba0ed1..bd3efa2 (2026-10-09)

Wspólny przodek `nasmart-main` i `origin/main`: `aba0ed1`. Pełny merge: 17 plików z konfliktami — **merge odradzamy, przenosimy ręcznie wybrane fragmenty.**

## Podsumowanie

| Commit | Temat | Zmienia wynik? | Relacja do nas | Rekomendacja | Praca |
|---|---|---|---|---|---|
| bd3efa2 | Import Analysis (mapy + raport przebicia w jednym oknie) | nie | niezależny, czysty | **cherry-pick 1:1** | ~0,5 h |
| 96809d3 | puste `catch {}` → `Log.Error` z limitem 1/60 s | nie | dubluje nasz pkt 11 (do zrobienia), 4 konflikty | **adaptacja**: `Log.cs` 1:1, reszta ręcznie + nasze nowe pliki | ~1–1,5 h |
| f854684 | B3 ADD w narożnikach wklęsłych i otworach | **tak** (nowe rozkłady/opisy/BBS) | niezależny, czysty | **decyzja D1**, potem cherry-pick + expected-diffs | ~1–2 h |
| 9d565be | kształty BS 8666 (wymiary zewn., kalkulator firmowy) | **tak** | **koliduje z W4** (11 konfliktów) | nie przenosić kodu; **decyzja D2** → ewentualne korekty w W4 | ~2–4 h |
| e7b600d | okno „identyczny pręt”, grip/STRETCH co 50 mm, drobne poprawki | częściowo | część dubluje W1b/D8/W1c; krok 50 mm koliduje z 5 mm bloku | adaptacja wybranych części; **decyzje D3–D5** | średnia |
| bc2055e | COPY: przepinanie powiązań wg mapy klonowania | tak (= nasze #3/#3b/#6) | mamy pełniejsze W2 (`CloneMap`/`CloneRemap`) | pominąć kod; wziąć opis do ARCHITECTURE.md/XLink; sprawdzić link rozkład→płyta (`RC_AUTOREBAR_SLAB`) | mała |
| 2523164 | ROTATE/MIRROR zachowuje układ pręta | tak (= nasze #4) | mamy W1d (`BarFrame`) + blok | pominąć; opcjonalnie `IsRigidMotion` jako optymalizacja (S-REG 0 różnic) | mała |

Kolega naprawił u siebie część błędów z `POPRAWKI_WZORCA.md` (#3, #3b, #4, #5, #6, #11, część #8/#13) — nasze wersje są pełniejsze i mają testy/expected-diffs.

## Szczegóły ważniejszych punktów

### 9d565be — kształty BS 8666 vs nasze W4
Rozbieżności merytoryczne:
- źródło: kolega — firmowy kalkulator BS8666_Calculator / BS 8666:2000; my — BS 8666:2020 (Table 2, Table 5),
- `MinBendRadius` d>25: kolega 4d (H32 r=128), my Table 2 (H32 r=112); wzorzec sam usunął 4d jako błąd,
- 63 przy C=0: kolega `max(14d,150)`, my `max(14d,140)` d≤16 / `13d` d≥20 (H20: 280 vs 260),
- 00: kolega bez zaokrąglenia, my w górę do 25 mm (W4g),
- 44/75: kolega zostawia stary pierścień jako 44 (heurystyka `IsLegacyRing`), my migrujemy do 75,
- **46: kolega wstawia D przed E w liście parametrów (A,B,C,D,E)** — stare rysunki odczytają dawne E jako D (łamie „indeksy tylko dopisywane”); u nas A,B,C,E,D,
- 15: kolega A,B,C; my A,B,C,D,
- wymiary zewnętrzne: kolega tylko 10 kodów, my wszystkie (`BarDimensionRules.ToAxis`).
Wspólne: łuk stycznymi, pomijanie zerowych odcinków, poprawka podwójnego przeliczenia w RC_SECTION, 44 jako kapelusz.
Do ewentualnego wzięcia: nazwy kształtów, `IsOptionalParam` (dialog).

### e7b600d — rozbicie
- (a) okno OK/Anuluj przy identycznym pręcie w RC_DISTRIBUTION, (b) komunikat po COPY o tym samym numerze — przenieść, ale przepisać na `BarEntity.ScanModelSpace` (u niego tylko polilinie); okno modalne zablokuje testy /automation,
- (c) krok gripa/STRETCH 50 mm — koliduje z naszym 5 mm (`BarLengthRules.LengthStep`),
- (d) przywracanie obrysu po odrzuconym gripie — to #12, które zostawiliśmy „bez poprawki”,
- (e) `Forget()` w RC_UPDATE_BAR/RC_EDIT_BAR — mamy (W1b),
- (f) RC_BAR_END tylko na rozkładzie — realizuje nasz #10 (do zrobienia) → przenieść,
- (g) erase overrule: UNDO i pręt bez etykiety — mamy (D8, W1c); **sprawdzenie back-linku [16] przed usunięciem opisu** — przenieść.

## Decyzje użytkownika

- **D1.** Czy B3 ADD (2 H10-100, L=1250 w narożnikach wklęsłych i w narożnikach otworu) to nowe wymaganie inżynierskie → przesuwamy wzorzec i dopisujemy regułę do ARCHITECTURE.md?
- **D2.** Promienie gięcia i długości: BS 8666:2020 (nasze W4) czy firmowy kalkulator (kolega)? (H32 r 112 vs 128; 63 H20 C=0: 260 vs 280; 00 zaokrąglane do 25 mm czy nie)
- **D3.** Krok gripa/STRETCH: 50 mm (kolega) czy 5 mm (nasz blok)? Jednakowy dla bloku i polilinii?
- **D4.** Cofamy „#12 bez poprawki” i przywracamy obrys sprzed odrzuconej zmiany gripem?
- **D5.** Okno „identyczny pręt” zawsze, czy z możliwością wyłączenia (testy skryptowe)?

### Odpowiedzi użytkownika (2026-10-09)
- **D1: TAK** — B3 ADD przyjmujemy (przesunięcie wzorca, reguła do ARCHITECTURE.md, expected-diffs).
- **D2: BS 8666:2020** — zostają nasze wzory W4; z 9d565be nie bierzemy wzorów kalkulatora.
- **D3: 5 mm** — krok gripa/STRETCH bez zmian (dla bloku i polilinii).
- **D4: A** — poprawiamy #12: odrzucona zmiana gripem (odcinek poniżej minimum) przywraca pręt sprzed przeciągnięcia.
- **D5: TAK** — okno „identyczny pręt” w RC_DISTRIBUTION; w trybie skryptowym (/automation) pomijane.

## Status przeniesienia (2026-10-09, plan `docs/PLAN_PRZENIESIENIA_2026-10-09.md`, staging — bez commita)

| Krok | Zakres | Status |
|---|---|---|
| K1 | bd3efa2 Import Analysis (cherry-pick 1:1) | zrobione — S-REG 0 nowych różnic |
| K2 | 96809d3 puste catch → `Log.Error` z limitem 60 s (adaptacja + nasze pliki) | zrobione — POPRAWKI #11 |
| K3 | e7b600d (f) RC_BAR_END tylko na rozkładzie, (g) back-link [16] opisu, `IsUndoing` w kaskadzie pręta (P2); komentarz bc2055e | zrobione — POPRAWKI #10, #16; test BRX P16A/B/C |
| K4 | e7b600d (a) okno „identyczny pręt” (`BarEntity`, kopie o tym samym numerze — P3; tryb skryptowy — D5), (b) komunikat po COPY | zrobione — testy BRX IDENT / SCRIPT / COPYMSG, test jednostkowy `IdenticalBarText` |
| K5 | przepięcie `RC_AUTOREBAR_SLAB` (+ wymiary ADD) przy COPY (P4, P4b) | zrobione — test P3c na kodzie sprzed zmiany pokazał błąd (3/3 rozkłady kopii usunięte); POPRAWKI 3c, expected-diffs 03c |
| K6 | e7b600d (d) = #12: odrzucony grip przywraca pręt-polilinię (D4); krok 50 mm (c) — NIE (D3: 5 mm, polilinia bez zaokrąglania — P1) | zrobione — POPRAWKI #12, expected-diffs 12 |
| K7 | f854684 B3 ADD (D1) | zrobione — POPRAWKI 14, expected-diffs 14 |
| — | 9d565be kształty (D2: zostaje BS 8666:2020 / W4), 2523164 (mamy W1d), bc2055e kod (mamy W2), e7b600d (e) (mamy W1b) | nie przenoszone; uwagi dla kolegi: `docs/UWAGI_DLA_KOLEGI_2026-10-09b.md` |

## Proponowana kolejność
1. bd3efa2 (cherry-pick) + 96809d3 (adaptacja) — bez zmian wyniku.
2. Mały krok z e7b600d: #10 RC_BAR_END, back-link [16], dokumentacja z bc2055e.
3. f854684 B3 — po D1.
4. Korekty W4 — po D2.
5. Okno identycznego pręta / komunikat COPY / krok gripa — po D3–D5.

Każdy krok: build, testy jednostkowe, BrxSmokeTests, S-REG z expected-diffs.
