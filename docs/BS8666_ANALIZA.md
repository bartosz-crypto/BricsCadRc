# Kształty prętów a BS 8666:2020 — analiza (2026-10-09)

Źródła: norma BS 8666:2020 (kl. 7, Table 2, 4, 5), `kbshapecodes.pdf` (2020), firmowy `BARSCHED-BS8666 2020 .xlsx`
(dysk H:, `00_PRZYKLADY_POMOCE\BarSchedule`). Kod: `ShapeCodeLibrary.cs`, `BarGeometryBuilder.cs`, `BarShape.cs`.

**Wniosek:** formuły w kodzie są mieszanką BS 8666:2005 (jak firmowy arkusz) i 2020; geometria rysowana dla ok. połowy
kodów nie odpowiada rysunkom normy. **Zmiany formuł zmieniają BBS — decyzja użytkownika (patrz RANO_DECYZJE.md).**

## Problemy wspólne
- **W1 wymiary zewnętrzne:** wg kl. 7.1 A–E mierzone po zewnętrznej stronie pręta; kod traktuje je jako oś
  i odsuwa obrys ±d/2 → narysowany pręt „wystaje” o d/2 (przeliczenie na oś jest tylko w `AutoRebarEngine.Section.AxisParams`).
- **W2 promień łuku:** r z Table 2 to promień wewnętrzny; łuk osi powinien mieć r + d/2 (kod: r).
- **W3 `BendArcPoints`:** punkt styczności zawsze w odległości r od narożnika zamiast r·tan(φ/2) → załamanie obrysu
  dla zagięć ≠ 90° (kształty 14, 15, 24, 25, 41).
- **W4 NaN:** odcinek zerowej długości → dzielenie przez 0 (25: geometria używa D=0; 31: brak D).
- **W5 parametry po pozycji:** 25 {A,B,E} bierze C jako E; 34/35 {A,B,C,E} bierze D jako E.
- **W6 MinBendRadius:** 2d (d≤16) / 3.5d (d≥20) zgodne z Table 2 poza d=25 (norma 87, kod 87.5 — pomijalne).
  Brak walidacji minimów (P, q, Table 4, B ≤ 400+2d).
- **W7 zaokrąglanie 00:** kl. 7.2 — długość całkowita w górę do 25 mm także dla 00 (arkusz tak, kod nie) — NIEPEWNE.

## Formuły (różnice kod ↔ norma 2020)
| Kod | Norma 2020 | Kod dziś | Arkusz |
|---|---|---|---|
| 14 | A+(C) | A+C−4d | A+C−4d (2005) |
| 22 | A+B+0.57C+(D)−0.5r−2.6d | = norma | A+B+C+D−1.5r−3d (2005) |
| 29 | A+B+(C) | A+B+C−0.5r−2d | A+B+C−r−2d (2005) |
| 31 | A+B+C+(D)−1.5r−3d | bez D | z D |
| 47 | 2A+B+2(C)+2q−3r−6d | 2A+B+max(21d,240) | jak kod (2005) |
| 51 (min C) | 2A+2B+max(16d,160) | 2(A+B+max(16d,160))−2.5r−5d | = norma |
| 56 | A+B+C+D+2(E)−1.5r−3d | A+B+C+D+E−2.5r−5d | A+B+C+D+2E−2.5r−5d |
| 63 (min C) | d≤16: 2A+3B+max(14d,140); d≥20: 2A+3B+13d | 2A+3B+2·max(14d,150)−3r−6d | 2A+3B+max(14d,150) |
| 64 | A+B+C+2D+E+(F)−3r−6d | A+B+C+D+E−2.5r−5d | F z kolumny P_ (błąd) |
| 75 | π(A−d)+B+25 | π(A−d)+B | π(A−d)+B |
Zgodne: 00, 11, 12, 13, 15, 21, 23, 24, 25, 26, 27, 28, 32–36, 41, 44, 46, 98, 99 (formuły). Brak w kodzie: 01, 48, 52, 67, 77.

## Geometria (niezgodna z rysunkiem normy)
12 (łuk = MinBendRadius zamiast R = trzeci parametr), 13 (B jako oś–oś zamiast zewnętrzne), 14/15 (kąty zamienione,
drugie ramię = B zamiast C), 22 (U z 3 ramionami zamiast A–B–półkole C–D), 24 (zamienione ramiona, C nie pionowo,
kąt 30° na sztywno — w normie z wymiarów D/E), 25, 26, 27, 28, 29, 31, 32, 33, 34, 35, 36, 41, **44 (w kodzie pierścień,
w normie „kapelusz” — pierścień to 75)**, 46, 47, 56, 64, 75. `_closedShapes {34,35,46,47}` niezgodne — zamknięte/
zachodzące są 33, 51, 52, 56, 63, 64, 75. Zgodne: 00, 11, 21, 23, 63 (geometria), 51 (poza orientacją A).

## Testy (11 FAIL) — wartości do aktualizacji (minimalna poprawka, bez W1/W2)
- Code12: dane {500,300,R=100} → 9 pkt, start (0,0), koniec (500,300), styczne (400,0) i (500,100), łuk środek (400,100) r=100;
  formuła 742.6 → 750.
- Code13: {500,200,300} → 15 pkt, koniec (200,200) (po W1: (200,194), r=94); formuła 575 poprawna.
- Code22: dane A=400,B=600,C=150,D=300 → 1342.3 → 1350; geometria 22 pkt, start (0,−400), koniec (300,−150), półkole r=75 środek (525,−75).
- Code24: {500,400,300}, θ=30° → koniec (846.41, 500.00).
- Po poprawkach formuł: Code14 700 (było 675), Code51 1600/1575 (1675/1650), Code63 1375/1350 (+ nowy d=20: 1475),
  Code75 825 (800), Code47 1250, Code31 950.

Firmowy arkusz też ma formuły 2005 (14, 22, 29, 47, 56, 63, 75) i błąd w 64 — warto przekazać autorowi arkusza.
