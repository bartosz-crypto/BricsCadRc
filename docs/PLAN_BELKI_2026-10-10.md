# Plan: automatyczne zbrojenie belek / pogrubień (downstand) — NA Smart RC

Status: E1 + E3 ZROBIONE (staging, 2026-10-10), E2 wstrzymany, E4–E6 do zrobienia. Użytkownik (2026-10-10): „rób do końca, na resztę pytań odpowiem potem” → otwarte pytania rozstrzygnięte DOMYŚLNIE (sekcja 1b), każde jako parametr / łatwe do zmiany. Wszystkie domyślne = **do potwierdzenia przez Bartka**.

## Status realizacji (2026-10-10, staging — do testów Bartka)
- **E1 — zrobione:** model (`RC_BEAM` [1]–[17], `RC_AUTOREBAR_BEAM` [1], warstwa `RC-BEAM`; `BeamStore`), czysta `BeamGeometry`
  (+ `tests/BeamGeometryTests.cs`: 319 / 528-529 / 539, T, skos), Prepare GA (strefa + H z „…THK”, kontrola SSL − NIB TOC + 150,
  ponowne Prepare GA usuwa pręty starych stref), `RC_BEAM` + `BeamDialog` (+ opcja Krawedz), CloneRemap (BeamZone [6],
  RC_AUTOREBAR_BEAM [1]), NibDetector / FindHoles / PrepareSection pomijają RC-BEAM. Zrzut 526 i Mesh 319 / 528 / 539 = HEAD (BRX D*).
- **E3 — zrobione:** strzemiona 51 (BL) + 4 + 4 H16 (BB / BT) na odcinkach osiowych, tylko RC_GENERATE → Beam / RC_GENERATE_BEAM /
  RC_BEAM; skos → „detal ręczny”; końce bez zamknięcia + komunikat E4; numery NextAuto / NextAutoTop; jedna grupa UNDO;
  Blok i Polilinia (`run-planc-beam.ps1`; CloneRemap: COPY / MIRROR / ARRAY / PASTECLIP).
- **E2 — wstrzymany** (siatka przy belce). **E4, E5, E6 — do zrobienia.**
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
1. Strzemię 51 pod OBIEMA warstwami T: A = H − 35 − 12 − 12 − 40, w dół do 5 (450 → 350, 375 → 275, 300 → 200); opcja „jedną warstwą”.
2. Strzemiona H10 co 200 (CP przy palach: LINKS H12@200), 50 od lica końca odcinka, przesunięcie 100 względem UB 03.
3. Zakład H16 = 800 (≈ 50d), w przedziale [800, 1050), maks. 6000, pręty równe (gdy się nie da — ostatni krótszy, osobna pozycja); zakłady dół / góra w tym samym przekroju, wszystkie 4 pręty w jednym przekroju.
4. 4 H16 dołem i górą, w poprzek otulina 40 + strzemię 10 + d/2 (58 … 542 co 161).
5. Pręty podłużne od lica do lica z otuliną 40 (koniec przy krawędzi — lico strefy, czyli linia nibu; pręty dolne nie wchodzą w nib).
6. Nib 135 × 150, U-bary H16 (zapis w rekordzie; użycie w E4 / E5).
7. Wysokość spoza siatki 75 — przyjęta z ostrzeżeniem; brak opisu „…THK” — H z SSL − NIB TOC + 150.
8. Kody warstw BB / BT / BL; warstwa CAD BT → RC-SLAB-TOP-01, BB / BL → RC-SLAB-BOT-01; warstwa stref RC-BEAM (kolor jak SD-Thickening-Zone).
9. RC_BEAM → Krawedz: strefa od linii uskoku (nib z NibDetector) na 600 w głąb.

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
- E5: RC_SECTION z belką + bezpiecznik.
- E6: BBS (arkusze), ARCHITECTURE.md 4i.

## 8. Testy
Jednostkowe: BeamGeometryTests (319, 528, 539), wymiary 51, liczba strzemion, plan H16 z zakładami, odejmowanie strefy, golden + belka, fingerprint (brak belki = ten sam hash). BRX `run-planc-beam.ps1` (kopie DXF): Prepare GA → strefa i H; Generate → liczby, numery ≥ 04; UNDO/REDO; COPY; Blok i Polilinia. S-REG = 0 dla dotychczasowych przypadków. Ręczne: 528-529, 539, 319 (45° → komunikat), RC_SECTION, STRETCH strefy → CHECK NIEAKTUALNY, płyty bez belki (526) bez zmian.

## 9. Ryzyka
UNDO/REDO (jedna grupa, bez cache), COPY/PASTE (CloneRemap dla nowych RegApp — inaczej błąd jak K5), grip (brak watchera), stare rysunki (brak RC_BEAM → zero zmian), nib 135/160, E2 zmienia wynik płyt z belką.
