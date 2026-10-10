# Plan: automatyczne zbrojenie belek / pogrubień (downstand) — NA Smart RC

Status: E1 + E3 + E4 + E4b + E5 + E6 ZROBIONE (staging, 2026-10-10), E2 wstrzymany. Użytkownik (2026-10-10): „rób do końca, na resztę pytań odpowiem potem” → otwarte pytania rozstrzygnięte DOMYŚLNIE (sekcja 1b), każde jako parametr / łatwe do zmiany. Wszystkie domyślne = **do potwierdzenia przez Bartka**.

## Status realizacji (2026-10-10, staging — do testów Bartka)
- **E1 — zrobione:** model (`RC_BEAM` [1]–[17], `RC_AUTOREBAR_BEAM` [1], warstwa `RC-BEAM`; `BeamStore`), czysta `BeamGeometry`
  (+ `tests/BeamGeometryTests.cs`: 319 / 528-529 / 539, T, skos), Prepare GA (strefa + H z „…THK”, kontrola SSL − NIB TOC + 150,
  ponowne Prepare GA usuwa pręty starych stref), `RC_BEAM` + `BeamDialog` (+ opcja Krawedz), CloneRemap (BeamZone [6],
  RC_AUTOREBAR_BEAM [1]), NibDetector / FindHoles / PrepareSection pomijają RC-BEAM. Zrzut 526 i Mesh 319 / 528 / 539 = HEAD (BRX D*).
- **E3 — zrobione:** strzemiona 51 (BL) + 4 + 4 H16 (BB / BT) na odcinkach osiowych, tylko RC_GENERATE → Beam / RC_GENERATE_BEAM /
  RC_BEAM; skos → „detal ręczny”; końce bez zamknięcia + komunikat E4; numery NextAuto / NextAutoTop; jedna grupa UNDO;
  Blok i Polilinia (`run-planc-beam.ps1`; CloneRemap: COPY / MIRROR / ARRAY / PASTECLIP).
- **E4 — zrobione:** narożniki L (pręty L kształt 11), złącza T (pręty L zakotwione w belce ciągłej), U-bary kształt 21 na końcach przy
  krawędzi i wewnętrznych, strzemiona w kwadracie narożnika (kontynuacja dłuższego odcinka); `BeamGeometry.AssignClosures / PlanLayer`
  (testy jednostkowe 539 / 528 / 319 / T), `AutoRebarEngine.Beam` (BeamBent), BRX `run-planc-beam.ps1` (319: strzemiona 41 / 12,
  539: 13 / 24 / 52, 2 narożniki × 4 pręty L i 4 U-bary na warstwę, 528: 4 U-bary na warstwę, 2 pozycje); kody BB / BT bez zmian.
  Komunikat „E4 w przygotowaniu” usunięty.
- **E4b — zrobione** (wymaganie użytkownika 2026-10-10, dołożone przed E5): pręty boczne **BS** H10 na obu bokach przy ramionach strzemienia,
  rozstaw pionowy ≤ 200, liczba na bok = ceil(odległość osi prętów dołu i góry / 200) − 1, rozstaw oś–oś ≤ 200 (H 600 → 2, 750 → 3, 450 / 375 → 1, 300 → 0), zakres jak
  pręty proste (bez zagięć: lico kwadratu narożnika / złącza T, przed U-barami lico z otuliną), zakład tej samej krotności Ø (H10: 500), w rzucie
  2 pręty (po 1 na bok), opis / BBS — wszystkie (CountDisplay); pręty wzdłuż nibu **BN** w narożach spinki UB 03 — górą H10, dołem H16 (po 1),
  od lica płyty na końcach (nib zawinięty) z otuliną; rzut dolny, seria dolna; parametry w oknie RC_BEAM i rekordzie RC_BEAM [18]–[21]
  (`BeamGeometry.SideBarCount / SideBarsAcross / LapFor / NibBarLine`). 528: BS 2 rozkłady (1 na bok), BN 4; 539: BS 0, BN obie nogi.
- **E5 — zrobione:** `SectionPlanner.Input.Beams` (`BeamBand`), profil z belką (spód −H w pasie, nib belki przy spodzie po stronie krawędzi),
  pręty belki (rodzaj `Beam`: kropki dół / góra / boczne / nib, strzemię 51 w widoku; wzdłuż belki — pręty w widoku, strzemiona jako pionowe
  ramiona), wymiary (nib belki, H, szerokość), opisy; `PrepareSection` czyta strefy RC_BEAM i rozkłady BB / BT / BL / BS / BN (tylko jako
  elementy belki); pusta lista = wynik bit w bit (5 złotych przypadków bez zmian, Version 1), szósty złoty przypadek `beam`; odcisk BEAM:<strefa>
  (brak belek = ten sam hash — test z odciskami HEAD), Compare: „belka … nieuwzględniona” (przekrój sprzed E5) = NIEAKTUALNY zawsze,
  „zmieniona belka …” rozstrzyga skrót wyniku; RC_SECTION_UPDATE działa (BRX S528C).
- **E6 — zrobione:** pozycje belki w RC_BBS / RC_SCHEDULE / RC_COUNT_BBS / .xls (BB / BL / BS / BN dół, BT góra; 00 / 11 / 21 / 51; długości
  cięcia BS 8666:2020; sztuki = suma rozkładów; masa .xls = COUNT — BRX S539BBS). Poprawka: szablony belki z TotalLength wg BS 8666:2020
  (wcześniej TL = 0 → ostrzeżenie RC_BBS dla strzemion 51).
- **E2 — wstrzymany** (siatka przy belce).
- Poprawki po recenzji E4b + E5 + E6 (2026-10-10): BN w przekroju w narożach spinki UB 03 z pasa (wcześniej nachodziły na jej ramiona),
  bez UB 03 — wzór + komunikat przy generowaniu; T IN NIB w trzonie belki — poziom jak w płycie; T IN NIB nachodzący na BN — odsunięty do styku;
  liczba prętów bocznych od osi (H 750: 3 na bok, nie 2); CountDisplay BS zachowany po gripie.
- **Odstępstwo / do decyzji Bartka (2026-10-10): BN górą H10 praktycznie dubluje T IN NIB** (Mesh nie widzi belek, E2 wstrzymany — T IN NIB
  płyty zostaje przy belce). Dziś: oba są generowane; przy generowaniu belki komunikat `[RC BEAM] … pręt BN górą może dublować T IN NIB wzdłuż
  tego nibu — do potwierdzenia`; parametr „Nib: pręt górą” = 0 (rekord [20] NibTopDia) wyłącza BN górą. W przekroju drugi pręt T IN NIB
  („150 dalej” — w trzonie belki między licem a strzemieniem) zostaje na poziomie płyty (wybrano „jak w płycie” zamiast pomijania: pręt jest
  w rysunku i w BBS), pierwszy (w nibie) odsunięty od BN górą do styku lic.
- Odstępstwa E5 / E4b (świadome, do potwierdzenia): pręty siatki przy belce w przekroju = to, co jest w rysunku (E2 wstrzymany — przez belkę);
  przekrój „sprzed E5” i „belka dodana później” — jeden komunikat (nie da się ich odróżnić bez podbicia formatu odcisku, które zmieniłoby hash
  przekrojów bez belek); pręty identyczne z prętami siatki dostają wspólny numer (np. BS H10 L5500 = B1, BN H16 L5500 = BB) — zasada „identyczny
  pręt → istniejąca pozycja”; U-bar w przekroju — oba ramiona (drugie z B − d), pręt L — tylko widoczne ramię; opis pręta belki w widoku przy
  braku miejsca — strzałka może przeciąć inne opisy.
- Odstępstwa E4 (świadome, do potwierdzenia): pręty L we WSZYSTKICH rzędach warstwy (nie tylko zewnętrznym i wewnętrznym — pręty
  środkowe krzyżowałyby ramiona L w narożniku), identyczne i przesunięte po przekątnej (jedna pozycja na narożnik); pręty proste przy
  narożniku L kończą się na licu kwadratu narożnika (E3: lico zewnętrzne, krzyżowały się), przy złączu T — na bliższym licu belki ciągłej;
  319 zmienia się także w prawym górnym narożniku L (pręty L, strzemiona góry przez kwadrat: 38 → 41), nie tylko na prawym końcu;
  belka ciągła złącza T nie jest już „detalem ręcznym” (przerwa lica wypełniona strefą = belka ciągła); ramiona prętów L / U-barów
  liczone od rzeczywistych końców prętów prostych (pręt pojedynczy wyśrodkowany → np. 539: U-bary 900 / 850 na końcu wewnętrznym,
  825 / 800 przy krawędzi — po recenzji tył wewnętrznego o Ø + 20 głębiej; L 1450 × 1350 i 1375 × 1375); rysunek w rzucie — jedno ramię + symbol haka (pełny kształt — zmiana BarBlockEngine).
- Odstępstwa od tekstu planu (świadome): strzemiona przy UB 03 — położenia UB 03 + 100 i rozstaw UB 03 (zamiast wzoru n — wzór gdy UB 03
  nie ma; liczba może się różnić o 1–2, np. 528: 52 → 50); zakład w przedziale [800, 1050) (po recenzji 2026-10-10, poprawka kolejności: najpierw N prętów
  równych na siatce 250, potem N prętów — N − 1 równych + ostatni krótszy, osobna pozycja; dopiero potem N + 1 równych — mniej zakładów
  zdecydowanie lepiej, np. 6251 → 3750 + 3500, nie 3 × 2750; dawniej nadmiar zaokrąglenia szedł w zakład — do ~1250);
  odcinek z końcem bez złącza przylegającym do odcinka „detal ręczny” też „detal ręczny” (L z krótką nogą 600 × 1200 — obie nogi bez prętów);
  pojedynczy pręt (≤ 6000) wyśrodkowany — równa otulina na końcach;
  wymiar B strzemienia też w dół do 5 mm; pręty podłużne przy narożniku L dochodzą do lica zewnętrznego narożnika (oba kierunki — krzyżują
  się w narożniku do E4); strzemiona tylko w zakresie własnym odcinka (kwadrat narożnika bez strzemion do E4); szerokość [3] informacyjna
  (generowanie z geometrii strefy); pręty belki pomijane w `ResolveRepresentativeCollisions` (Mesh ich nie przesuwa); opisy siatki
  omijają opisy belek (zasada „opisy nie nachodzą”).

### Wartości DOMYŚLNE do potwierdzenia przez Bartka
0. (E4b) Pręty boczne H10, rozstaw pionowy ≤ 200, liczba na bok ceil(odległość osi dołu i góry / 200) − 1; proste, bez zagięć w narożnikach / na końcach;
   pręty w nibie: górą H10 + dołem H16 (po 1, w narożach spinki UB 03) — interpretacja „brakuje 2 prętów H10 wzdłuż nibu górą oraz dołem
   w narożu nibu też dajmy H16” do potwierdzenia; zakład prętów H10 = 500 (ta sama krotność Ø co 800 / H16).
1. Strzemię 51 pod OBIEMA warstwami T: A = H − 35 − 12 − 12 − 40, w dół do 5 (450 → 350, 375 → 275, 300 → 200); opcja „jedną warstwą”.
2. Strzemiona H10 co 200 (CP przy palach: LINKS H12@200), 50 od lica końca odcinka, przesunięcie 100 względem UB 03.
3. Zakład H16 = 800 (≈ 50d), w przedziale [800, 1050), maks. 6000, pręty równe (gdy się nie da — ostatni krótszy, osobna pozycja); zakłady dół / góra w tym samym przekroju, wszystkie 4 pręty w jednym przekroju.
4. 4 H16 dołem i górą, w poprzek otulina 40 + strzemię 10 + d/2 (58 … 542 co 161).
5. Pręty podłużne od lica do lica z otuliną 40 (koniec przy krawędzi — lico strefy, czyli linia nibu; pręty dolne nie wchodzą w nib).
6. Nib 135 × 150, U-bary H16 (zapis w rekordzie; użycie w E4 / E5).
7. Wysokość spoza siatki 75 — przyjęta z ostrzeżeniem; brak opisu „…THK” — H z SSL − NIB TOC + 150.
8. Kody warstw BB / BT / BL; warstwa CAD BT → RC-SLAB-TOP-01, BB / BL → RC-SLAB-BOT-01; warstwa stref RC-BEAM (kolor jak SD-Thickening-Zone).
9. RC_BEAM → Krawedz: strefa od linii uskoku (nib z NibDetector) na 600 w głąb.
10. (E4) Narożnik L: pręty L kształt 11 we wszystkich rzędach dołu i góry, identyczne, przesunięte po przekątnej; ramiona z zakładem
    ≥ 800 z prętami prostymi każdej nogi (w górę do 25 mm); strzemiona w kwadracie — dłuższy odcinek (równe — odcinek wzdłuż X).
11. (E4) Złącze T: pręty belki dochodzącej do bliższego lica belki ciągłej; pręty L: ramię przez belkę ciągłą do jej rzędów (zakład ≥ 800
    z prętami prostymi), ramię wzdłuż belki ciągłej = 800; połowa rzędów zagięta w lewo, połowa w prawo.
12. (E4) U-bary H16 kształt 21: 2 na warstwę i koniec (zagnieżdżone: rzędy skrajne B = 500, środkowe B = 175), A = C z zakładem ≥ 800,
    tył na otulinie 40, wewnętrzny o Ø + 20 = 36 głębiej (prześwit między tyłami ≥ 20 — recenzja E4; 528: A = 800 / 775); pierwsze
    strzemię przy końcu z U-barami za tyłami U-barów z prześwitem 20 (oś 120 od lica zamiast 50; 528: 52 → 51 strzemion); koniec
    wewnętrzny jak przy krawędzi; nieparzysta liczba prętów — środkowy bez U-bara, jeden pręt — koniec bez U-bara (komunikaty).
13. (E4) Opisy: `4 H16-08 BEAM CORNER`, `1 H16-09 BEAM END`; kody BB / BT; w rzucie jedno ramię z symbolem haka.
14. (E4, recenzja) Ułożenie zakładów prętów L / U-barów z prętami prostymi „piętrowo”: ramię w tym samym rzędzie co pręt prosty,
    w styku w pionie — dołem ramię NAD prętem dolnym, górą POD prętem górnym (w rzucie bez przesunięcia w poprzek).
15. (E4, recenzja) Skrzyżowanie „+” (strefa po obu stronach przerwy w licu) — oba odcinki „detal ręczny” z komunikatem (bez prętów
    i strzemion, bez dublowania strzemion w węźle). Krótki odcinek, na którym ramiona z obu końców nachodzą — ostrzeżenie [RC BEAM].

### Do decyzji Bartka przed wydaniem
- **(a) Rysowanie pręta giętego w rzucie.** Dziś: widoczne jedno ramię + symbol haka 45° na końcu przy zagięciu. Koliduje to
  z konwencją płyty, gdzie ten symbol oznacza pręt odgięty pionowo (UB, T IN NIB). Warianty:
  1. ramię zakładu przesunięte w poprzek o d (pręt gięty obok prostego — widać oba) + symbol jak dziś;
  2. pełny kształt L / U w rzucie (oba ramiona i tył U) — wymaga zmian w `BarBlockEngine` (nowy typ linii pręta w rozkładzie);
  3. bez zmian (hak 45° — z legendą „pręt gięty w poziomie”).
- **(b) Ułożenie piętrowe zakładów** (punkt 14) — czy przyjmujemy, czy zakład obok (w poziomie, przesunięcie w poprzek o d).
- **(c) Ramiona dłuższe niż zakład i pręty L we wszystkich 4 rzędach.** Ramiona prętów L / U-barów liczone od rzeczywistych końców
  prętów prostych (pręt wyśrodkowany) wychodzą 825–1450 (np. 539: L 1450 × 1350, 1375 × 1375; U-bary 900 / 850, 825 / 800), a nie
  równo 800; pręty L we wszystkich 4 rzędach (nie tylko zewnętrznym i wewnętrznym). Do decyzji: zostawić, czy pręty proste dosuwać
  do zamknięcia (ramiona = zakład), i czy rzędy środkowe mają mieć pręty L.
- Pierwsze strzemię 120 od lica przy końcu z U-barami (punkt 12) i prześwit 20 między tyłami U-barów — potwierdzić.

## 0. Dane (Holbrook GA P02, CP)
- Strefa pogrubienia = zamknięta polilinia na warstwie `SD-Thickening-Zone` (po jednej na plot), między linią uskoku a linią 760 mm od krawędzi → 600 mm od lica nad nibem. Wysokość: tekst SD-Text „450THK”/„375THK”/„300THK” w strefie; kontrola krzyżowa `SSL − NIB TOC + 150` (319: 450, 528: 375, 539: 300).
- Kształty: 319 — U z odcinkami 45° (jeden koniec na krawędzi, drugi wewnątrz); 528–529 — prosta krawędź-krawędź; 539 — L+L (koniec wewnętrzny + koniec na krawędzi). Nib (160 = 135 + 25) tylko wzdłuż belki, zawinięty 600 mm na końcach przy krawędzi.
- Detale GA (D04 = 450, D07 = 300, D08 = 375, warstwa S-REBAR): strzemię zamknięte H12 tylko w trzonie 600, otulina ~40, nib nieobjęty; w nibie spinka H10 (UB 03) wchodząca ~550 w belkę; pręty podłużne niepokazane.
- CP (319, 528): przy palach „LINKS H12@200 2-LEG”, d = 390 / 315.
- Kod 51 (ShapeCodeLibrary.cs:168: A wysokość, B szerokość, C hak; wymiary zewnętrzne; C = 0 → 2A + 2B + max(16d,160)) — pasuje; 63 alternatywą przy kolizji haka z siatką; 33 nie.

## 1a. Decyzje użytkownika (2026-10-10)
- Szerokość typowo 600, wysokość co 75 (300/375/450/…), 4 H16 dołem + 4 H16 górą, końce zamknięte U-barami H16, nib belki 135 × 150.
- Siatka B: prostopadłe do belki wchodzą w belkę, równoległe nie. Siatka T: leży NAD zbrojeniem belki; tylko prostopadłe wchodzą w belkę.
- Strzemiona tylko w belce, kod 51, równomiernie na całej długości, na razie H10 co 200 (parametr w oknie, bez logiki H10/H12).

## 1b. Rozstrzygnięcia DOMYŚLNE (do potwierdzenia)
1. Wysokość strzemienia: góra strzemienia POD obiema warstwami T (wierzch − 35 − dT1 − dT2), dół na otulinie 40 → A = H − 35 − dT1 − dT2 − 40 (450, T H12+H12: 351 → zaokrąglenie wymiaru do 5 mm w dół). Parametr „góra strzemienia pod: obie warstwy T / jedną warstwę”.
2. Dolne pręty prostopadłe: prosto, na poziomie siatki płyty, przez strzemiona, koniec na licu belki z otuliną (bez odgięcia); przy 300, gdy wchodzą w nib — koniec na licu nibu z otuliną.
3. Pierwszy pręt równoległy przy belce: dół — 40 + d od wewnętrznego lica belki; góra — s/2 od linii belki.
4. UB 01/02 na krawędziach z belką — pominięte; UB 03 w nibie belki — zostaje (610-70-610 jak D04); T IN NIB przy belce — zostaje.
5. Pręty podłużne H16: max 6000 (jak płyta); zakład wg firmowej tabeli zakotwień, jeśli da się odczytać, inaczej 40d → 650 (zaokr. 25); zakłady niemijane (parametr).
6. Narożniki L/T: pręty L (kształt 11, ramiona = zakład); odcinki nieosiowe (45°, 319) → ostrzeżenie „detal ręczny”, bez prętów na tym odcinku.
7. U-bary H16 na końcach: poziome w rzucie, 2 szt. na warstwę (dół i góra), ramiona = zakład.
8. Koniec wewnętrzny belki zamykany tak samo jak koniec na krawędzi.
9. Numeracja: dolne pręty belki, strzemiona i U-bary dół — seria dolna (NextAuto); górne pręty i U-bary góra — seria górna (NextAutoTop); opisy `4 H16-05 BEAM B`, `4 H16-103 BEAM T`, `nn H10-07-200 LINKS`, `H16-08 BEAM END`.
10. Wysokość spoza siatki 75 → przyjęta z ostrzeżeniem; belki wewnętrzne bez nibu → poza zakresem (później).
11. Strzemię nie obejmuje nibu; kod 51.
12. Belka bez strefy w GA (np. pod ścianą) → tylko ręcznie (`RC_BEAM`).

## 1c. Poprawki po niezależnej recenzji (Claude, 2026-10-10) — mają pierwszeństwo
- **E2 WSTRZYMANY** do decyzji Bartka (1b.2, 1b.4 — zakotwienie prętów płyty w belce krawędziowej na palach; w praktyce UK górny pręt zwykle zaginany w dół w belkę). Do tego czasu Mesh/B1…T2 IGNORUJĄ strefy belek (wynik płyt 319/528/539 jak wzorzec). Pręty belki tylko jawnie: RC_GENERATE → Beam / RC_BEAM.
- 1b.5 zmienione: domyślny zakład H16 = **800** (≈ 50d, firmowa praktyka 45–55d), parametr; rysunki z belkami do potwierdzenia przed wydaniem. Do decyzji: zakłady górnych prętów belki z dala od pali, odstęp zakładów dół/góra, zakłady wszystkich 4 prętów w jednym przekroju.
- Liczba strzemion: n = ceil((L − 2·50)/200) + 1 (rozstaw ≤ 200), opis z nominalnym 200. Strzemiona przesunięte o 100 względem UB 03 (kolizja dolnych ramion na otulinie 40).
- Kody warstw prętów belki: ustalić (np. `BB`/`BT`/`BL` — NIE zaczynające się od „B1/B2/T1/T2”, żeby EraseOldDistributions i gałęzie `StartsWith("T"/"B")` — 23 miejsca w AutoRebarEngine.cs, m.in. CreateNewTemplate l.1904 i SectionPlanner.cs:211/225/262/317 — działały świadomie); przejrzeć każde miejsce; ARCHITECTURE §3/§4.
- Rzut: dół belki i strzemiona → rzut dolny (seria < 100); góra i U-bary górne → rzut górny (101+).
- U-bary zagnieżdżone = różne B → osobne pozycje (jeden numer = jeden kształt).
- Ponowne RC_PRZYGOTUJ_GA: sprzątanie prętów belki z martwym znacznikiem (przez znacznik płyty / test punktu w strefie).
- CloneRemap: RC_AUTOREBAR_BEAM [1] + RC_BEAM [6] + KindOf/AppOf (CloneRemap.cs:441/481); testy MIRROR, ARRAY, PASTECLIP.
- E5: poza Profile — schemat siatki przy nibie, UB 01/02, UB 03/T IN NIB przy belce; SectionFingerprint.Compare: porównanie składnika BEAM (różnica → przyczyna; przekrój sprzed E5 na płycie z belką → NIEAKTUALNY „belka nieuwzględniona”).
- Drobne: regex `\d{3,4}\s*THK`, MText przez TextLines, 3 teksty w 319 — zgodność; PrepareSection pomija teksty w strefach RC-BEAM (grubość płyty); NibDetector.Detect i FindHoles pomijają warstwę RC-BEAM; tryb opisu bez rozstawu „4 H16-05 BEAM B” do ARCHITECTURE; test BRX E1: zrzut płyty 526 przed/po identyczny i Mesh 319 bez zmian (S-REG nie woła Prepare GA).
- Kolejność: E1 → E3 (parametry, opt-in) → E4 → E5 → E6; E2 po decyzji. Uwaga w oknie: CP przy palach podaje H12@200 (domyślnie H10@200 — decyzja Bartka).

## 2. Przekrój (belka krawędziowa 450)
```
 wierzch 0 ┬──── T (prostopadłe) ═══════════════════════════════╗ 35 ot.
           │  T równoległe kończą się        ┌──strzemię 51──┐   ║
     −225  │  przed belką   (płyta 225)      │ ● ● ● ●  4H16T│   ║
 płyta ────┘ B ═ prostopadłe ════════════════╪═══════════════╪═══╣
  B równoległe kończą się przed licem        │               │   ║
                                    lico wewn│ ● ● ● ●  4H16B│   ║
                                       −300 ─┼───────────────┼───╚══╗ NIB TOC
                                             │  UB03 H10 ⊂═══╪══════╗║ nib 135(160)×150
                                       −450 ─└───────────────┴──────╝╝ 40 ot.
                                             |←── 600 ──────→|←135→|
```
Strzemię 51: B = 600 − 2·40 = 520; 4 H16 w warstwie, osie co ≈ 161; dół 40 + 10 + 8 od spodu.

## 3. Model danych
- Strefa: zamknięta polilinia `RC-BEAM` (kopia z `SD-Thickening-Zone`), RegApp `RC_BEAM`: [1] FormatVersion, [2] H, [3] szer., [4] szer. nibu, [5] wys. nibu, [6] obrys płyty (XLink 1005), [7–8] Ø/rozstaw strzemion, [9–10] Ø/liczba dołem, [11–12] Ø/liczba górą, [13] Ø U-barów, [14] kod strzemienia; pola tylko dopisywane, odczyt toleruje krótsze rekordy.
- Wygenerowane obiekty: `RC_AUTOREBAR_BEAM` [1] = handle strefy (XLink); ponowne generowanie usuwa tylko obiekty tej belki; CloneRemap jak K5 (klon strefy → klon znacznika; kopia samych prętów → "").
- Czysta klasa `BeamGeometry` (bez Teigha): strefa + obrys + nib → odcinki osi z szerokością, końce Edge/Internal, złącza L/T, odcinki nieosiowe = ręcznie.

## 4. Wykrywanie
- `GaImportEngine.Classify` (l.233–250): `SD-Thickening-Zone` zamknięta → oba rzuty; w `CloneInto` (po NibAutoClose, przed JoinLoops): warstwa `RC-BEAM` + `RC_BEAM`; H z `^\s*(\d{3})\s*THK\s*$` w strefie (NumberParser), kontrola krzyżowa → ostrzeżenie. Ponowne Prepare GA zastępuje (tag RC_GA). Płyta bez warstwy → bit w bit bez zmian. „450THK” nie może trafić do ParseThickness płyty (tekst bez „SLAB”).
- Komenda `RC_BEAM`: wskaż zamkniętą polilinię (albo obrys płyty + 2 punkty krawędzi → strefa 600 od linii uskoku); okno `BeamDialog` z domyślnymi.

## 5. Generowanie (`AutoRebarEngine.Beam.cs`)
- Strzemiona: szablon 51 (CreateNewTemplate, PlaceBar — Blok/Polilinia), jeden rozkład na prosty odcinek, n = floor((L − 2·50)/200) + 1, opis z rozstawem nominalnym.
- Podłużne: plan prętów prostych jak ComputeDistributionPlan (siatka 250, min. zakładów, max 6000), rozkład 4 prętów w poprzek (ApplySingleBarMark); narożniki i końce wg 1b.6–8.
- Numery wyłącznie PositionCounter.NextAuto / NextAutoTop; identyczny pręt → istniejąca pozycja; 01/02/03 nietknięte.
- Opisy PlaceLabel z przeszkodami; komenda RC_GENERATE → opcja `Beam` + wywołanie na końcu Mesh, gdy płyta ma strefy; jedna grupa UNDO.
- Siatka (GenerateLayer l.195): dla kierunku równoległego do odcinka belki wielokąt pasów = obrys − strefa (odcinki osiowe); prostopadły bez zmian (góra do linii uskoku, dół wg 1b.2); UB wg 1b.4.

## 6. Przekrój i bezpiecznik
- SectionPlanner.Input: lista `Beams` (pas, H, nib); Profile obniża spód w pasie belki + nib przy spodzie; pusta lista = wynik bit w bit (złoty test bez zmian, Version bez podbicia); szósty złoty przypadek z belką.
- SectionFingerprint: składnik `BEAM:<handle>` tylko gdy belka jest (brak → ten sam hash).

## 7. Etapy
- E1: model, BeamGeometry + testy, Prepare GA, RC_BEAM + okno (bez prętów). S-REG = 0.
- E2: siatka przy belce (wycinanie równoległych, UB) — zmienia wynik tylko płyt z belką; nowy przypadek S-REG F-BEAM + expected-diffs.
- E3: strzemiona 51 + podłużne 4 + 4 (proste odcinki).
- E4: narożniki L/T, U-bary, koniec wewnętrzny.
- E4b: pręty boczne (BS) i pręty wzdłuż nibu (BN) — wymaganie 2026-10-10 (przed E5).
- E5: RC_SECTION z belką + bezpiecznik.
- E6: BBS (arkusze), ARCHITECTURE.md 4i.

## 8. Testy
Jednostkowe: BeamGeometryTests (319, 528, 539), wymiary 51, liczba strzemion, plan H16 z zakładami, odejmowanie strefy, golden + belka, fingerprint (brak belki = ten sam hash). BRX `run-planc-beam.ps1` (kopie DXF): Prepare GA → strefa i H; Generate → liczby, numery ≥ 04; UNDO/REDO; COPY; Blok i Polilinia. S-REG = 0 dla dotychczasowych przypadków. Ręczne: 528-529, 539, 319 (45° → komunikat), RC_SECTION, STRETCH strefy → CHECK NIEAKTUALNY, płyty bez belki (526) bez zmian.

## 9. Ryzyka
UNDO/REDO (jedna grupa, bez cache), COPY/PASTE (CloneRemap dla nowych RegApp — inaczej błąd jak K5), grip (brak watchera), stare rysunki (brak RC_BEAM → zero zmian), nib 135/160, E2 zmienia wynik płyt z belką.
