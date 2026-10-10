# Plan: belki wg praktyki biura (C12 / Holbrook ręczny) + błąd zawiniętego nibu

Status: ZAAKCEPTOWANY do realizacji („wprowadź całość, zdecyduję później”, 2026-10-10). Wszystkie nierozstrzygnięte = parametry z domyślnymi „do potwierdzenia” (sekcja 5). Źródła: scratchpad sesji `rc-reczne\WNIOSKI.md`, `c12\WNIOSKI_BELKI.md`, `c12\detale\*.png`.

## Status realizacji (2026-10-10, staging — do testów Bartka)
- **K1 — zrobione:** `NibStepRules` (wolny koniec: sąsiednia krawędź linii uskoku nie jest nibem i koniec wewnątrz krawędzi zewnętrznej; przy wolnym końcu pręty dołu w [lo, hi], bez ± 1), ostrzeżenie rozstawu < 100. BRX `run-planc-k1.ps1` z bazą HEAD: 458, 461-462, 524, 526, 322 — bit w bit; 321 UB 02 131 → 66 (ręcznie 68), −413 kg; 525 UB 02 117 → 89 (ręcznie 90), −255 kg; 527 UB 01 77 → 74 (ręcznie 75), −35 kg → POPRAWKI_WZORCA 29, S-REG F29 (expected-diffs `29-nib-zawiniety.txt`). Poprawka recenzji 1 („dwie krawędzie-właściciele jednego pasa → UB = pręty góry”) — test `NibStepRulesTests.TwoOwnerEdgesOfOneStrip_UbCountEqualsTopBars` (zachowanie dotychczasowe, bez zmiany kodu).
- **K2 — zrobione:** rekord RC_BEAM v2 [24]–[30] + parametry pytań sekcji 5 [31]–[35] (3: pręty w kwadrat, 4: koniec pręta prostego, 7: strzemiona przy UB 03, 8: strzemiona w kwadracie narożnika, 14: naddatek ramienia U-bara); [15] = Ceil25(40·Ø), [17] nieużywane; migracja v1 w pamięci (prosta — poprawka 9), zapis przy OK / generowaniu; strzemiona H12, A = H − otulina górna − dolna, pierwsze 51 od lica (także przy U-barach), niezależnie od UB 03; bez ostrzeżenia o otulinie UB 03 (Beam.cs, SectionPlanner — złoty `beam`: tylko ta linia WARN); okno `BeamDialog` z polami v2. Tryby prętów bocznych [25] / [26] działają (bramka B1 / B2 / B3, Ø H12); opis „{n}x2 … SIDE” i UB SIDE — K4.
- **K3 — zrobione:** BM „2x4 H16-nn-150 B+T” (CountDisplay, `BarLabelRules.CountText` we wszystkich miejscach opisu, grip — krotność BM / BS), BB / BT tylko przy dół ≠ góra (seria dolna, rzut dolny), pionowe U-bary BU kształt 21 (końce, nogi narożników L z B − 35 na nodze Y, belka dochodząca złącza T), kółko na zagięciu; pręty L / poziome U usunięte; `IsBeamCode` z BM / BU; filtry usuwania: rzut dolny — wszystkie kody belki, rzut górny — stare BT; `ReadUb03` / `ScanUBTemplates` pomijają kody belki. RC_SECTION: BM na obu poziomach, BU pominięte (K6).
- **Poprawki recenzji K1–K3 (2026-10-10):** kryterium końców krawędzi uskoku w całości w `NibStepRules.ClassifyEnd` / `RuleCountOnEdges` — wierzchołek współliniowy linii uskoku = koniec niewolny (jak wzorzec), odsadzka linii uskoku = jak wzorzec, chyba że rozstaw < 100 mm (POPRAWKI 29, pytanie 23); run-planc-k1 bez zmian (66 / 89 / 74, reszta bit w bit). BeamDialog: zakład max(dół, góra) < maks. długość; kopia strefy z rekordem v1 na drugi rzut zachowuje rekord v1 (`BeamStore.WriteSlab` — tylko [6]), v2 zapis przy OK / generowaniu; testy `BarLabelRulesTests`. Pytania 14–23 w sekcji 5.
- **Nie zrealizowane w K1–K3 (świadomie):** pytanie 2 (dwa zachodzące strzemiona 370 dla H ≤ 375) — wymaga decyzji o geometrii (370 nie wynika z prostego wzoru), zostaje jako pytanie; pytanie 5 (zakład U-bara obok / piętrowo) — dotyczy tylko przekroju (K6). K4–K8 — do zrobienia.

## Poprawki po niezależnej recenzji (Claude, 2026-10-10) — mają pierwszeństwo
1. K1: test BRX z bazą HEAD (jak run-planc-beam -Baseline) na Holbrook 458, 461-462, 524, 526, 527, 322, 321, 525 — różnice dozwolone tylko na krawędziach uskoku z wolnym końcem; 527 może się zmienić (UB co 169) → wtedy do POPRAWKI 29. Testy NibStepRules: przykłady z ARCHITECTURE §4 (57→55, 8→10, 13→13) bez zmian; nib w środku krawędzi (oba końce wolne); dwie krawędzie-właściciele jednego pasa → liczba UB = liczba prętów góry.
2. K1 kryterium wolnego końca: po układzie — koniec wolny, gdy sąsiednia krawędź nib.Inner w tym wierzchołku nie jest krawędzią nibu (ewentualnie próg = max szer. nibu + 5); przy wolnym końcu pręty dołu w [lo, hi] bez ±1 na tym końcu.
3. K3: IsBeamCode obejmuje BM i BU (+ stare BB/BT/BS/BN) — inaczej BU „… UB” trafi w SectionPlanner.KindOf jako UB płyty (Section.cs:162), pominą go ResolveRepresentativeCollisions (AutoRebarEngine.cs:3590) i BeamStore.FindGenerated. BT na rzucie dolnym: BuildBarData (~4280) BT → TOP (warstwa RC-SLAB-TOP-01) — zostaje (warstwa góry), opisz. Filtry usuwania (Beam.cs:279-280): rzut dolny — wszystkie kody belki, rzut górny — stare BT.
4. K3 „2x4”: wszystkie miejsca składające `$"{EffectiveCount} {Mark}"` — AnnotationEngine.cs:475, :1064; AutoRebarEngine.cs:2204, :3847; EditCommands.cs:607-691; BarCommands.cs:554; SyncAnnotation po PositionReconciler.PropagateToDistributions. Grip BarBlockEngine.cs:626-636 — krotność także BM. ARCHITECTURE §4 (l.400): podłużne z rozstawem nominalnym.
5. Kolejność: K7 PRZED K5 (bez E2 równoległy pręt B leży w nibie, tam gdzie B IN NIB). Mesh na płycie ze strefami → komunikat „ponów Beam” (wspólny numer B IN NIB). Nib częściowy → PlanBars z zakładem 400.
6. K7: wielokąt E2 z JEDNEJ funkcji, używany w GenerateLayer, mainStrips UB (~861), PlanNibStepBars i liczeniu UB 03 z rzutu dolnego; DecomposeStrips (1388-1395) tnie pas na całej szerokości — opisz w expected-diffs 30, porównaj z ręcznymi 528/539; B3 i Add na obrysie płyty (nie E2); strefy także na rzucie górnym.
7. K7: zaktualizuj ARCHITECTURE §4i („siatka nie widzi stref” → E2) i testy BRX D319/D528/D539/M528 (dziś wymagają równości z HEAD).
8. K8: odrzucać THK ≤ grubość płyty (opis płyty / SSL); środek MText z obwiedni (zaczepienie, obrót); MLeader — grot pierwszego odnośnika; zasięg strefy najpierw z linii strefy GA (~760, POPRAWKI 23), 600 od linii uskoku jako zapas; łączenie w L/U tylko gdy sąsiednia krawędź ma własny tekst lub linię strefy (inaczej nib zawinięty 321 → belka na całym obwodzie).
9. K2: [15] dalej = Ceil25(40·Ø główne) (dla starej wtyczki), [17] nieużywane; migracja v1 tylko w pamięci, zapis tylko przy OK w RC_BEAM lub generowaniu; rekordy v1 tylko z wersji testowych → prosta migracja: nowe domyślne + komunikat.
Drobne: ramię U-bara domyślnie A = Ceil25(zakład + 90) (C12 740; Holbrook 770) — pytanie 14; przesunięcie kropek BU o Ø do środka przekroju; noga Y przy B − 35 symetrycznie 17,5 góra/dół (K6); opis prętów bocznych `2x{n} H12-nn SIDE` (boki × liczba, jak C12/„2x4 B+T”); kod warstwy UB SIDE — ustal (np. BS z kształtem 21).
**Kolejność: K1, K2, K3, K7, K5, K4, K6, K8.**

## Ustalenia
- Wzorzec aba0ed1 ma ten sam błąd zawiniętego nibu (`StepEdgeRuleCount`/`PlanNibStepBars` identyczne; `nOuter` liczy pręty dołu na CAŁEJ krawędzi zewnętrznej → 33 pręty na 643 mm).
- B U-bara pionowego = A strzemienia − 2·Ø strzemienia (w dół do 5): Holbrook A1 528→265, 319→340, 539→190; C12 A2 450→325, 600→475, 750→625.
- Zakład = Ceil25(40·Ø): H10 400, H12 500, H16 650 (nota 12 biura).

## Kroki
**K1 — zawinięty nib.** `StepEdgeRuleCount` (AutoRebarEngine.cs ~2292): koniec krawędzi uskoku „wolny”, gdy leży wewnątrz zakresu krawędzi zewnętrznej o > szer. nibu + 5 → zakres liczenia przycięty do [lo,hi], dLow/dHigh = 0, nOuter = pręty dołu w przyciętym zakresie (bez rzutu dolnego: floor((hi−lo−2c)/200)+1); zwykłe krawędzie bez zmian (reguła ±1). Czysta `NibStepRules.RuleCount` + testy. Ostrzeżenie „rozstaw < 100 mm”. POPRAWKI_WZORCA 29 (błąd wzorca), S-REG F29 + expected-diffs 29-nib-zawiniety.

**K2 — rekord RC_BEAM v2.** FormatVersion 2; pola [24] otulina górna strzemienia (35 = A1 / 50 = A2), [25] tryb prętów bocznych (1 B1, 2 B2, 3 B3), [26] próg H prętów bocznych (450), [27] krotność zakładu (40), [28] rozstaw nominalny w poprzek (150), [29] B+T jedną pozycją (1), [30] obniżenie U-barów drugiego kierunku (35). Migracja v1 (`BeamParams.MigrateV1`): pola równe starym domyślnym → nowe domyślne (Ø strzemion 10→12, zakład 800→40·Ø, BS H10→B3, BN górą 10→0, BN dołem 16→10); zmienione przez użytkownika zostają + komunikat. A = H − otulina górna − otulina dolna (w dół do 5). BeamDialog: nowe pola, A1/A2. Strzemiona H12, pierwsze w osi otulina + Ø/2 od lica (także przy U-barach). Usunąć ostrzeżenie o otulinie UB 03 (Beam.cs, SectionPlanner).

**K3 — B+T, pionowe U-bary, narożniki L/T.** Kody: BM (dół = góra, jedna pozycja, seria dolna, rzut dolny), BU (pionowe U-bary); BB/BT tylko gdy dół ≠ góra (oba w serii dolnej, rzut dolny); stare BT usuwane przy ponownym generowaniu. Opis BM `2x4 H16-nn-150 B+T` przez CountDisplay = 2·4 (`BarLabelRules.CountText`), AnnotationEngine, BarBlockEngine (grip — krotność BM/BS); rozstaw w opisie nominalny 150, ostrzeżenie gdy rzeczywisty > 175. Usunąć CornerRun/TeeRuns/URuns i kod poziomych U-barów/prętów L; nowe `VUBarRuns`: koniec — pręty proste do lico − (otulina + Ø_U), U 21: B = A_strz − 2Ø_strz, tył na otulinie 45, A = Ceil25(start pręta − tył + Lap), 4 szt. w kolumnach prętów, opis `4 H16-nn-150 UB`; narożnik L — pręty proste nogi do lica kwadratu narożnika, U-bary nogi z tyłem na licu zewnętrznym narożnika, A ≈ szer. drugiej nogi + zakład, noga Y: B − 35; złącze T — belka ciągła bez przerwy, dochodząca jak noga narożnika. Rzut: U-bar jak UB płyty (kółko na zagięciu). PlanBars z Lap(d).

**K4 — pręty boczne + UB SIDE.** Tryby: B3 (domyślny: H ≤ 450 brak, od 525 H12), B1 brak, B2 zawsze. Liczba SideBarCount (A = H−85: 525→1, 600→2, 675→2, 750→3); opis `{n}x2 H12-nn SIDE`. UB SIDE H12 kształt 21 poziomy, 1 na poziom na koniec/narożnik, B = B_strz − 2Ø_strz (485; C12 480), A = start − tył + Lap(12); opis `{n}x1 H12-nn UB SIDE`; rzut: ramię + hak (pełny U w rzucie — później).

**K5 — B IN NIB.** BN górą = 0. BN dołem H10 `1 H10-nn B IN NIB`: podział = kopia podziału rozkładu siatki dolnej równoległego do nibu, najbliższego belce → wspólny numer (FindOrCreateBeamTemplate); bez siatki PlanBars z zakładem 400. Kolejność Mesh → Beam (sprawdzić RC_GENERATE).

**K6 — przekrój i odcisk.** PlaceBeamDists: BM — dwa poziomy; BU — w cięciu poprzecznym kropki dół/góra przesunięte w poprzek o Ø, w cięciu wzdłuż kształt A–B–A; BS/21 PairOffset. Section.cs: StirrupA nowym wzorem; BeamParamsText z nowymi polami. Złoty `beam` aktualizowany, 5 pozostałych bit w bit.

**K7 — E2 siatka przy belce.** Czysta `RectilinearPolygons` (różnica/suma prostokątów) + testy. GenerateLayer: gdy płyta ma strefy — wielokąt pasów = obrys − segmenty: równoległy dół do lica wewn. + d (pierwszy 40 + d), równoległy góra do lica + (s/2 − 40), prostopadły dół — odjęty pas nibu przed belką (koniec na linii uskoku − 40), prostopadły góra bez zmian. UB 01/02: bez krawędzi przy belce; PlanNibStepBars: krawędzie uskoku przy belce bez reguły UB 01/02; UB 03 i T IN NIB bez zmian. Brak stref = bit w bit. S-REG F-BEAM + expected-diffs 30-e2-siatka-przy-belce; POPRAWKI 30 (zmiana przyjęta decyzją użytkownika).

**K8 — belki z opisów „xxxTHK”.** CloneInto: MLeader na SD-Text (MText.Contents → TextLines). Gdy brak SD-Thickening-Zone: ProcessBeamTexts — tekst z H (ParseThk), punkt (MText środek / MLeader grot) w obrysie, w pasie [linia uskoku, +750] krawędzi nibu → strefa 600 od linii uskoku (czysta `BeamGeometry.ZoneFromNibEdge`, wspólna z CreateZoneFromEdge), sąsiednie krawędzie → suma (L/U); kontrola SSL − NIB TOC + 150. Odrzucenia z komunikatem: THK poza pasem nibu, „GB-…” (detal ręczny), teksty spoza obrysu; H z poziomów bez tekstu — tylko ostrzeżenie. Ryzyko: linie uskoku C12 w blokach/xrefach.

Dokumenty: ARCHITECTURE §1 (RC_BEAM [24]–[30], BM/BU), §3 Numeracja (wszystkie pręty belki w serii dolnej — zmiana zasady na żądanie użytkownika), §4, §4i; PLAN_BELKI (status).

## Ryzyka
UNDO (jedna grupa), grip (krotność BM/BS), COPY (bez nowych RegApp), stare rysunki (v1 → nowe domyślne; stare BB/BT/BS/BN/11/21 usuwane przy ponownym generowaniu; przekroje z belką → NIEAKTUALNY; siatka 319/528/539 zmienia się — E2), sufiks „UB” U-barów belki (każde EndsWith(" UB")/UBSuffix — ReadUb03, skany UB, PositionReconciler, BBS — musi sprawdzać kod), kolizja UB 03 ze strzemieniem (domyślnie niezależnie, opcja +100).

## Testy
Jednostkowe: NibStepRules (321/525 → rozstaw ≈ 200), RectilinearPolygons, U-bary 265/340/190 i 325/475/625, narożnik, zakłady 400/500/650, B1/B2/B3, MigrateV1, CountText, ZoneFromNibEdge, ParseThk MLeader. BRX run-planc-beam (528: BM, 2×BU, strzemię H12 51 290×510, brak BT/BS/BN górą, B IN NIB = numer B2, brak UB 01/02 przy belce; 539 narożniki; UNDO/REDO, COPY, Blok/Polilinia; rekord v1). S-REG: F29, F-BEAM, reszta 0 nowych różnic. Porównanie z ręcznym BBS Holbrook 528/539/319, tonaż 321/525, C12 PLOT 17.

## 5. Parametry „do potwierdzenia” (pytania do Bartka)
1. Otulina górna strzemienia A1 35 (H−85, domyślnie) / A2 50 (H−100).
2. H ≤ 375: 2 zachodzące strzemiona 370 (Holbrook) — opcja?
3. Narożnik: pręty proste przed kwadratem (C12, domyślnie) / w kwadrat (Holbrook); B − 35 na nodze Y.
4. Koniec pręta prostego: otulina + Ø_U (61, domyślnie) / ≈ 90 (C12).
5. Zakład U-bara obok pręta prostego (domyślnie) / piętrowo.
6. „-150” w opisie przy rzeczywistym ~157.
7. Strzemiona niezależnie od UB 03 (domyślnie) / +100.
8. Strzemiona w kwadracie narożnika: dłuższa noga (domyślnie) / brak.
9. Pręty boczne: próg 450, H12, 200; UB SIDE B = 485 (C12 480).
10. B IN NIB = podział prętów B (wspólny numer).
11. E2: pierwszy równoległy dół 40 + d, góra s/2; prostopadły dół do linii uskoku − 40.
12. „THK”: strefa tylko z tekstem; GB poza płytą — ręcznie.
13. Dół ≠ góra: BB i BT w serii dolnej na rzucie dolnym.
14. Ramię U-bara końca: A = Ceil25(zakład + 90) → 750 dla H16 (Holbrook ręcznie 770, C12 740) — naddatek [35].
15. Numeracja: BT (dół ≠ góra) i cała belka (BM, BU, strzemiona, BS, BN) w serii dolnej — zmiana zasady §3 Numeracja (dotąd góra w serii górnej). Potwierdzić.
16. U-bary belki BU z sufiksem „UB” i numerem ≥ 04 — zasada „UB = 01 / 02 / 03” dotyczy płyty; czy belka może mieć „04 UB”, czy inny sufiks?
17. BT na rzucie dolnym rysowany na warstwie RC-SLAB-TOP-01 (warstwa góry) — zostaje, czy warstwa rzutu dolnego?
18. Narożnik L: ramię 1225 (nasze) vs 1250 (C12 liczy od lica zewnętrznego narożnika) — od którego lica?
19. Strzemiona 528: 52 (nasze) vs 51 (ręcznie) — sposób liczenia (pierwsze 51 od lica, zaokrąglenie liczby).
20. Podział prętów prostych: 5500 + 5250 (nasze, 2 pręty) vs 3 pręty (ręcznie) — przy jakiej długości / zakładzie trzeci pręt?
21. Belka szer. 300: H16 (domyślne) vs H12 (ręcznie) — średnica zależna od szerokości / H?
22. (= 2) H ≤ 375: dwa zachodzące strzemiona 370 (Holbrook) — nie zrealizowane (geometria 370 nie wynika z prostego wzoru); opcja?
23. Nib (POPRAWKI_WZORCA 29, recenzja K1): wierzchołek współliniowy na linii uskoku — dziś jak wzorzec (każdy odcinek liczony z całej krawędzi zewnętrznej, ~2× gęściej); odsadzka linii uskoku (zmiana szerokości nibu) — jak wzorzec, chyba że rozstaw < 100 mm (wtedy koniec przy odsadzce wolny). Liczyć oba przypadki jak jedną krawędź?

Kolejność: K1, K2, K3, K5, K4, K6, K7, K8.
