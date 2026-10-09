# Plan W4 — kształty prętów wg BS 8666:2020

> Autor: planista-recenzent (Claude). Podstawa: `docs/BS8666_ANALIZA.md`, decyzje D1 (`docs/RANO_DECYZJE.md`):
> formuły 2020, wymiary zewnętrzne osobnym krokiem, 44 = kapelusz / pierścień = 75 (stare 44 rozpoznawane).
> Stan bazowy `dotnet test`: 326 PASS / 11 FAIL (Code12×7, Code13×2, Code24×1, Code22_ZBar_Formula).
> W2 idzie wcześniej — W4 nie dotyka BarCopyWatcher/AnnotMoveOverrule/CloneMap/PluginApp; styki w `tools/` (W4c) zaznaczone.

## 0-POPRAWKI (recenzja ChatGPT 2026-10-09; zweryfikowane; pierwszeństwo przed resztą planu)
1. **[BLOKER] Rozkłady Manual a W4e:** `BarCommands.GetViewLengthManual` (979–996, też `RC_EDIT_DISTRIBUTION`) mierzy odcinek między
   wierzchołkami obrysu — po W4e ta sama ramię dałoby inną długość rozkładu. Rozwiązanie: wybór ramienia jak dziś (geometria
   bieżąca), ale **długość mierzona na obrysie wg `LegacyBarGeometry` (umowa osi sprzed W4) dla tego samego indeksu odcinka** —
   wynik identyczny ze wzorcem dla polilinii i bloku. Test: tworzenie i edycja rozkładu Manual przed/po W4e = te same długości.
2. **[BLOKER] Geometrie pośrednie (zapis DWG między etapami):** `FrameFromOutline` próbuje WSZYSTKICH historycznych wariantów
   geometrii, nie tylko „legacy” i „bieżącej”: kombinacje {ostre punkty legacy / wg normy} × {łuki stare / r·tan(φ/2)} ×
   {oś / wymiary zewnętrzne}. Implementacja: `BarGeometryBuilder.GetLocalPoints(code, pv, d, BarGeometryOptions opt)` z flagami
   `{ Bs2020Sharp, TangentArcs, ExternalDims }`; `LegacyBarGeometry` = wszystkie false. Test: obrys każdego wariantu, obrót 30° +
   MIRROR → rama odzyskana (w tym S-zapis DWG po W4a i otwarcie po W4d).
3. **[BLOKER] 51/63 wariant minimalny:** geometria haka przy C = 0 musi odpowiadać formule 2020 — efektywna długość haka
   `C_eff` wyznaczona z równości formuł: 51: `2(A+B+C_eff) − 2.5r − 5d = 2A + 2B + max(16d,160)` →
   `C_eff = (max(16d,160) + 2.5r + 5d)/2`; 63 analogicznie z `2A+3B+2C_eff−3r−6d` = formuła minimalna. `BarGeometryBuilder` (207, 222)
   używa `C_eff` zamiast `max(16d,160)`. Test: długość osi obrysu (z łukami) ≈ formuła (|Δ| ≤ 0.5d+1) dla C = 0 i C > 0.
4. **[WAŻNE] RC_SCHEDULE / CSV przy wyjątku długości (47 bez C, 64 bez F):** `BarScheduleEngine` (67–71) i eksport CSV obsługują
   błąd pojedynczej pozycji (wiersz z „brak długości” + ostrzeżenie), cała komenda działa dalej. Test rysunku mieszanego.
5. **[WAŻNE] Opcjonalne wymiary kąta w dialogach:** `BarElevationDialog` (246) i picker — wymiary określające kąt (D/E dopisane
   w W4d) oraz C dla 51/63 opcjonalne (puste/0 = wartość domyślna + ostrzeżenie W4f). Test: RC_EDIT_BAR starego pręta 15 (D=0)
   → OK bez zmian geometrii.

## 0. Ustalenia z kodu
1. XData wymiarów **pozycyjne**: parametr i → pole `LengthA…E` [i] (`BarElevationDialog.OK_Click` 241–278); formuły (`p[i]`)
   i BBS (`ColumnOf` po nazwie, 191–195) spójne. Rozjeżdża się tylko `GetSharpPoints` (99–103) — stąd W5 (25: E w polu C; 34/35/46: E w D).
2. Plan B watchera: każda modyfikacja pręta giętego przebudowuje obrys z XData (`BarGeometryWatcher` 585–606); rama z
   `FrameFromOutline` → `TryFit(obrys, LocalOutline(nowa geometria))` (`SingleBarEngine` 627–660). Stary obrys nie pasuje do nowej
   geometrii → zapas „cięciwa” bez odbicia → **pręty po MIRROR straciłyby odbicie** → najpierw W4a-0.
3. AutoRebar używa tylko 00, 21, 13 (+00 w przebiciu i ADD) — formuły i geometria W4b/W4d bez zmian dla nich; S-REG ruszy W4c (F5) i W4e.
4. BBS zawsze przelicza długość z formuły (`BbsDrawingReader.BuildRow` 197–212, `BarScheduleEngine.CalcCuttingLength`); TotalLength
   tylko przy `LengthOverridden` → zmiana formuły od razu zmienia BBS starych rysunków.
5. Do 2026-04-24 picker pokazywał 32 kształty, potem `00 11 13 15 21 33 44 46 51 63` (`ShapeCodeLibrary` 230–233).
6. 51/63 z pustym C dziś nieosiągalne (`BarElevationDialog` 172, 246 odrzuca v ≤ 0).
7. 75 dziś helisa (`SpiralPoints`), formuła traktuje B jako zakład; w 2020 75 = pierścień, helisa = 77.
8. Martwy kod: `src/ShapeCodes/*`, `SingleBarEngine._closedShapes` (507–508). `BarShapePreviewRenderer._closedCodes` (17–19) używane.
9. Table 2 (s. 13):
| d | 6 | 8 | 10 | 12 | 16 | 20 | 25 | 32 | 40 | 50 |
|---|---|---|---|---|---|---|---|---|---|---|
| r | 12 | 16 | 20 | 24 | 32 | 70 | 87 | 112 | 140 | 175 |
| P bend | 110 | 115 | 120 | 125 | 140 | 190 | 235 | 305 | 380 | 475 |
| P links | 110 | 115 | 130 | 155 | 210 | 290 | 365 | 465 | 580 | 725 |
| q | 45 | 60 | 70 | 85 | 115 | 200 | 250 | 320 | 400 | N/A |

**Zasada nadrzędna:** XData pozycyjne jak dziś; nowe parametry kształtu tylko DOPISYWANE na końcu `Parameters`; geometria czyta
parametry po nazwie (indeks w `Parameters`); dialogi pokazują alfabetycznie przez mapę wiersz → slot. Bez migracji danych (poza 44).

## W4a — błędy bez zmiany BBS
- **W4a-0** `src/Core/LegacyBarGeometry.cs` (bez Teigha, w tests): kopia obecnego `GetLocalPoints` — TYLKO do rozpoznania ramy starych
  obrysów. `BarFrameRules`: `LocalOutlineFrom(localPts, d)`, `LegacyLocalOutline`. `FrameFromOutline`: TryFit(bieżący) → TryFit(legacy) →
  fallbackOutline → cięciwa → tożsamość. Dziś legacy = bieżąca → 0 zmian.
- **W4a-1** `BendArcPoints` (348–392): t = r·tan(φ/2); |φ−90°| < 1e-12 → t = r DOKŁADNIE (bit w bit 11/21); φ < 1e-9 → narożnik;
  φ > 179° → narożnik + log. Test 45°: t = 9.941, tp1 (90.059; 0), środek (90.059; 24), koniec łuku (107.029; 7.029).
- **W4a-2** odcinki zerowe: len < 1e-9 → bez łuku; `GetLocalPoints` (51–57) usuwa duplikaty przed łukami. Testy 31, 25 bez NaN.
- **W4a-3** parametry po nazwie: `BarShape.IndexOf(name)`, `Value(pv, name)`; `GetSharpPoints` przez `V("A")…`, `V("R")`.
  BBS bez zmian; zmienia geometrię 25, 34, 35.
- **W4a-4** testy: Code13 → 15 pkt, koniec (200,200); Code22_ZBar_Formula → A400 B600 C150 D300 = 1350; Code12×7 i Code24 → `[Ignore]` do W4d-2.
  Opcjonalnie usunąć martwy kod.
- Smoke `tools/BrxSmokeTests/PlanCW4Tests.cs` + `planc-w4.scr` + `run-planc-w4.ps1`: 4a-1 — pręt 15 z obrysem legacy, 30° + MIRROR → MOVE
  → nowa geometria, rama zachowana. S-REG 0 różnic.

## W4b — formuły 2020 (zmienia BBS, nie geometrię)
| Kod | Zmiana | Parametry |
|---|---|---|
| 14 | `A + C` | bez zmian (D w W4d) |
| 29 | `A + B + C` | bez zmian |
| 31 | `A+B+C+D − 1.5r − 3d` | dopisać D |
| 47 | `2A + B + 2C + 2q − 3r − 6d`; d=50 → wyjątek „q N/A” | dopisać C; C=0 → wyjątek |
| 51 | C=0 → `2A+2B+max(16d,160)`; inaczej `2(A+B+C) − 2.5r − 5d` | — |
| 56 | `A+B+C+D+2E − 1.5r − 3d` | — |
| 63 | C=0: d≤16 `2A+3B+max(14d,140)`, d≥20 `2A+3B+13d`; inaczej `2A+3B+2C−3r−6d` | — |
| 64 | F brak pola → wyjątek „64 wymaga F — nadpisz długość” | — |
| 75 | `π(A−d) + B + 25` | (nazwa w W4c) |
`BarShape.MinBendRadius`: Table 2 (25 → 87), poza tabelą 2d/3.5d; `HookDiameterQ(d)`, `MinEndProjectionP(d, links)`.
`BarElevationDialog` (166–178, 241–253): C opcjonalne dla 51/63 (`BarShape.IsOptionalParam`). **Bezpiecznik BBS**
(`BbsDrawingReader.BuildRow` ~204): `!LengthOverridden`, zapisane TL > 0 i ≠ formuła → ostrzeżenie „Pozycja 07 (kształt 47):
długość wg BS 8666:2020 = 1250 mm, w rysunku 1075 — sprawdź.” (tylko odczyt).
Testy (d=12): 14 {400,100,300}=700; 51 {400,300,0}=1600, d8=1575, C210=1700; 63 {300,200,0}=1375, d8=1350, d20=1475, C180=1425;
75 {100,500}=825; 47 {300,200,200}=1250, C0/d50 wyjątek; 31 {300,200,300,200}=950; 29 {300,200,100}=600;
56 {400,300,200,150,100}=1200; 64 wyjątek; 44 {500,400,500,400,300}=2025; 00/11/13/21 bez zmian; MinBendRadius(25)=87.
S-REG 0 różnic (BLOKER inaczej). Smoke: 51 pusty C → 1600; BBS 47 bez C → ostrzeżenie.

## W4c — 44 = kapelusz, 75 = pierścień
**Rozpoznanie starego 44-pierścienia:** znacznik w ogonie `RC_SINGLE_BAR` **[15] ShapeStd = Int16 2020** tylko dla nowych 44
(polilinia bez [14] → dopisać [14] = Int16 1). Parser `ReadBarXData` (901–929): „44” bez [15]=2020 → w pamięci `ShapeCode = "75"`
+ `BarData.LegacyRing44 = true` (niezapisywane); pierwszy zapis utrwala „75” (migracja leniwa, UNDO cofa). Nie po geometrii
(BarEditApply czyta pręt ponownie w trakcie; zepsuty pierścień; blok wymaga BTR). Ograniczenie: stara wtyczka gubi [14+].
Zmiany: `BarRecordRules` (`ShapeStdIndex=15`, `ShapeStd2020`, `PolylineFormatVersion=1`, `IsLegacyRing44`, `WithShapeStd` — bez
dublowania); `BarXData.Write` dla „44” → znacznik; `BarFrameRules.IsRingShape => code == RingShapeCode ("75")`; wszystkie literały
„44” → `IsRingShape` (`BarGeometryBuilder` 43, `SingleBarEngine` 452, 523, 955, 1002, `BarLengthRules.LocalStart` 66,
`BarGeometryWatcher` 230, `BarCommands` 91); kapelusz: (0,0),(A,0),(A,−B),(A+C,−B),(A+C,−B+D),(A+C+E,−B+D); 75 = dawny
`CirclePoints`, `SpiralPoints` usunąć, 77 nieobsługiwane; `ShapeCodeLibrary`: 44 „Hat” + SVG, 75 „Ring (lap B)” {A,B} + SVG,
picker + 75; `BarShapePreviewRenderer`; `PositionReconciler.SameShape` tylko `Take(Parameters.Length)`; `GetViewLength` 75 → p[0];
komunikaty (BBS i RC_EDIT_BAR dla `LegacyRing44`). Zmigrowany pierścień zachowuje A i B (B = zakład).
Testy: jednostkowe (IsRingShape, LocalOutline, LocalStart, kapelusz 30 pkt (0,0)→(1300,0), 75 9 pkt, BarRecordRules ogon);
smoke (stary pierścień → 75 + LegacyRing44, MOVE, Apply, UNDO; nowy 44 ze znacznikiem przez MOVE/ROTATE/MIRROR/EDIT/COPY;
blok 44 bez [15] → pierścień; BBS ostrzeżenie); aktualizacja smoke (`PlanCFixW1Tests` 470–491, `PlanCStage2aTests` 132,
`PlanCStage3Tests` 206, 269–293, 618; ⚠ `PlanCFixW2Spike.cs:226` po W2); S-REG ⚠ `RegFixCommands.cs:124, :274` refleksją
`RingShapeCode`/`IsRingShape`; expected-diffs `08-ksztalt-75-pierscien.txt` (`# bug: 8`) — tylko F5a/F5b: shape 44→75 i TL.

## W4d — geometria wg rysunków normy (umowa OSI jak dziś)
Stare pręty chroni W4a-0. Nowe parametry dopisywane; wymiar geometryczny 0 → kąt domyślny + ostrzeżenie W4f.
- **W4d-1 (picker):** 15 (A skośne, C poziome, B pion, D rzut; θ = atan2(B,D) lub 45°; dopisać D), 33 (bieżnia: półkola średnicy B,
  długość A, zakład C), 46 (A–B skos–C–B skos–E; kąt z D; dopisać D ZA E → {A,B,C,E,D}), 51 (A = wysokość, B = szerokość).
- **W4d-2:** 12 (łuk R = V("R"), ≤0 → MinBendRadius; {500,300,100}: 9 pkt, (0,0)→(500,300), styczne (400,0),(500,100), środek (400,100);
  750), 22 (A, B, półkole C, D; {400,600,150,300}: 22 pkt, (0,−400)→(300,−150), r 75 środek (525,−75)), 24 (A poziome, B skos, C pion;
  kąt z D/E lub 30°; {500,400,300} θ30 → (846.41, 500.00)), 14 (C poziome, A skośne wstecz; kąt z B/D lub 45°; dopisać D);
  odblokować `[Ignore]`; `AxisParams` case 22 → A−d/2, B−d/2, C−d, D−d/2.
- **W4d-3 (opcjonalnie, po W4e):** 23, 25–29, 31, 32, 34–36, 41, 47, 56, 98.
- Zamknięte/zachodzące: 33, 51, 52, 56, 63, 64, 75; usunąć `_closedShapes`. Dialogi alfabetycznie (`BarShape.DisplayOrder`).
S-REG 0 różnic. Smoke: PlaceBar → MOVE → ROTATE 30° → MIRROR → RC_EDIT_BAR, rama zachowana; legacy obrys → MOVE → nowa geometria.

## W4e — wymiary zewnętrzne (kl. 7.1) i łuk osi r + d/2 (najwyższe ryzyko) — nowa poprawka #13
`GetLocalPoints(code, pv, d, BarGeometryOptions opt = Bs2020)` `{ ExternalDims, AxisRadiusPlusHalfD }`; `Axis = {false,false}` do
dopasowania obrysów W4a–d. Czysta tabela `BarDimensionRules.ToAxis(code, pv, d)`: ramię wolny koniec → lico: −d/2; między dwoma
licami (U: B, 13: B, 33: B, strzemiona): −d; wolny–wolny: 0; kąty ostre/rozwarte wg rys. 4/5; łuk osi r + d/2 (12: R + d/2;
13: (B−d)/2; 22: (C−d)/2); 75: oś A−d. Pierścień: promień osi (A−d)/2, obrys zewn. A/2. `AutoRebarEngine.Section`: USUNĄĆ `AxisParams`
(207–220), użyć `GetLocalPoints` (193–194). `FrameFromOutline`: kandydaci Bs2020 → Axis → Legacy. Blok: przez `GetLocalPoints`.
`docs/POPRAWKI_WZORCA.md` #13; expected-diffs `13-wymiary-zewnetrzne.txt` (`# bug: 13`) — tylko wierzchołki obrysów prętów giętych,
groty etykiet ≤ d/2, łuki w przekroju; różnice długości/XData/rozkładów/opisów = BLOKER. Opcja `RC_REBUILD_BARS` (jedna grupa UNDO).
Testy: 21 {500,140,500} d12 → bbox 140×500, łuk osi r 30, oś 1090.25 vs 1092 (|Δ| ≤ 0.5d+1); 13 {500,200,300} → bbox 500×200,
pętla r 94, koniec (A−C, B−d); kształty 90°: |oś − formuła| ≤ 0.5d+1; Note 3 ≤ 3%; 75 {300} d12 → r zewn. 150; łańcuch FrameFromOutline.
Smoke: UB 225 (obrys = A×B XData), RC_SECTION wymiary jak przed, stary 21 obrócony/odbity → MOVE → rama, blok 21 RC_EDIT_BAR.

## W4f — walidacja minimów (ostrzeżenia)
`src/Core/BarShapeValidation.cs` `Check(code, pv, d)`: wymiar 0; A/C ≥ P; 13: q ≤ B ≤ 400+2d, A,C ≥ B/2 + max(5d,90); 12: A,B ≥ (R+d) +
max(5d,90); 22: q ≤ C ≤ 400+2d, D ≥ C/2 + max(5d,90); 33: B ≤ 400+2d; korby (przesunięcie ≥ 2d, skos ≥ 10d min 75 / 13d); co 5 mm;
krótszy bok ≤ 2750, długość > 12 m; brakujące D/E kąta; Table 4 — sprawdzić w normie. Dialogi: TextBlock z ostrzeżeniami, OK nie
blokowane; RC_BBS: ostrzeżenia. Testy: 13 B=80 → „B < q (85)”, B=430 → „B > 400+2d (424)”; 11 A=100 → „A < P (125)”.

## W4h — kąt ramienia skośnego (decyzja użytkownika 2026-10-09, RANO_DECYZJE „DECYZJE UŻYTKOWNIKA — rano”)
14, 15: θ = asin(B/A) przy braku D (acos(D/A) przy braku B), z B i D jak dotąd; 24: asin(D/B) / acos(E/B); 46 bez zmian
(asin(D/B)); brak wymiarów → 45° / 30° + ostrzeżenie; rzut ≥ ramię → 90° + ostrzeżenie. Pole „Kąt [°]” (RC_BAR, RC_EDIT_BAR,
picker): wpisany kąt → 14 / 15: B = A·sin θ, D = A·cos θ; 24: D = B·sin θ, E = B·cos θ; 46: D = B·sin θ — co 5 mm, zapis w polach
(bez zmiany XData). Flaga `BarGeometryOptions.SlopeFromB` (Current = W4h; obrysy sprzed W4h rozpoznaje wariant W4e).
Skutek: stare 14 / 15 z B bez D (dotąd 45°) i 24 z jednym z D / E po przebudowie (MOVE, RC_EDIT_BAR, RC_REBUILD_BARS) dostają
kąt z wymiaru; rama zachowana. Testy: `tests/BarSlopeAngleW4hTests.cs`, smoke W5 (W / WV).

## W4g — zaokrąglanie 00 do 25 mm (kl. 7.2)
`BarShape.CalculateTotalLength` (42–43): 00 w górę do 25 (A bez zmian). AutoRebar/przebicie/ADD bez zmian (wielokrotności 25).
S-REG: tylko TL prętów 00 niebędących wielokrotnością 25 → `# bug: 8`. Test {3010} → 3025, {3000} → 3000.

## Decyzje (P1–P14) — przyjęte rekomendacje (użytkownik śpi; do potwierdzenia rano, RANO_DECYZJE D-W4)
P1 znacznik [15] (+[14]=1 polilinia) · P2 B zmigrowanego = zakład + ostrzeżenie · P3 44 i 75 w pickerze · P4 64 tylko przez nadpisanie ·
P5 47 bez C → brak długości + ostrzeżenie · P6 BBS starych rysunków wg 2020 z ostrzeżeniem · P7 00 zaokrąglane do 25 w BBS ·
P8 axis[0] zostaje; `RC_REBUILD_BARS` opcjonalnie · P9 51: A = wysokość · P10 W4d-3 po W4e · P11 r(25)=87 · P12 75 = pierścień,
77 nieobsługiwane · P13 `# bug: 8` dla W4a–d,g, #13 dla W4e · P14 kody 01, 48, 52, 67, 77 poza zakresem.

## Ryzyka przekrojowe
UNDO (brak nowych handlerów/cache; migracja 44→75 tylko w zapisach), grip drag (czyste funkcje), COPY/XLink (BarXData.Write zachowuje
[14+]; grep, że nikt nie składa RC_SINGLE_BAR z pominięciem BarXData — także CloneMap/W2), stare rysunki (legacy, znacznik, bezpiecznik
BBS), wiele rysunków (stan w XData), stara wtyczka kolegi (gubi [14+]; 75 rysuje jako helisę).

## Lista dla autora arkusza BARSCHED
14: `A+C−4d` → `A+(C)` · 22: `A+B+C+D−1.5r−3d` → `A+B+0.57C+(D)−0.5r−2.6d` · 29: `A+B+C−r−2d` → `A+B+(C)` ·
47: `2A+B+max(21d,240)` → `2A+B+2(C)+2q−3r−6d` (q z Table 2) · 56: `A+B+C+D+2E−2.5r−5d` → `A+B+C+D+2(E)−1.5r−3d` ·
63 min: `max(14d,150)` → d≤16 `max(14d,140)`, d≥20 `13d` · 75: `+25` · 64 (błąd): F z kolumny P_ zamiast wymiaru F;
2020 `A+B+C+2D+E+(F)−3r−6d`. Zgodne: 51 min, r(25)=87, zaokrąglanie 00.

## Kontrola po każdym kroku
Build, `dotnet test` (0 FAIL poza `[Ignore]` do W4d-2), W1 + E1/E2a/b/d/E3 bez regresji, `run-planc-w4.ps1` PASS,
S-REG = expected-diffs. Każdy krok osobno odwracalny.
