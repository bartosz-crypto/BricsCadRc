# Przekazanie sesji — 2026-10-09

## Gdzie jesteśmy
- **Repo:** `adin2011PL/NaSmartRc` (prywatne), lokalna gałąź `nasmart-main` → `nasmart/main`.
  Repo kolegi `bartosz-crypto/BricsCadRc` (remote `origin`) — tylko odczyt, NIE pushować.
- **DLL w `bin\NaSmartRc.dll`:** wersja z całą pracą (Plan C 1–3f, W1, W2, W4a–h), wszystkie testy PASS.
  Autoload BricsCAD → `bin\NaSmartRc.dll`; stara wtyczka kolegi wyłączona (`scripts\wybierz-wtyczke.ps1`).
- **Tryb prętów:** domyślnie Polilinia; `RC_BAR_FORMAT` → Blok włącza mądre pręty (gripy z przeliczaniem).

## Zrobione (szczegóły: `docs/RANO_DECYZJE.md`, `docs/POPRAWKI_WZORCA.md`)
| Krok | Co |
|---|---|
| Plan C 1–3f | pręt-blok z gripami, BarEntity, edycja, COPY, SCALE, RC_BAR_FORMAT, propagacja numeru/rozkładu/etykiety |
| W1 | poprawki błędów kolegi #1, #2, #4, #5, #7 |
| W2 | mapa klonowania (COPY/MIRROR/ARRAY/PASTE) — #3, #6, 3b |
| W4a–h | BS 8666:2020: formuły, geometria, 44=kapelusz/75=pierścień, 00 do 25 mm, wymiary zewnętrzne (#13), walidacja minimów, kąt z B lub wpisany |
Testy: `dotnet test` 714/0; BricsCAD: E1 376, E2a 41, E2b 22, E2d 43, E3 196, W1 60, W2 100, W4 125, F3 54 (FAIL=0);
S-REG = expected-diffs.

## Otwarte decyzje użytkownika
1. Długość w BBS co 5 mm jako opcja? (teraz 25 mm wg BS 8666 rys. 1 — wymiary co 5, długość co 25).
2. REDO po COPY (D9) — do ustalenia z kolegą (punkt 17 w dokumencie dla niego).
3. Pytanie 1 (ostrzeżenia BBS): domyślnie tylko zmienione formuły.

## Testy ręczne do zrobienia (BricsCAD)
- Etap 1: gripy myszą, ESC, `'ZOOM` w trakcie, 2 pręty z Shift, ARRAY, Ctrl+C/V, wydruk.
- Po 3f (G1–G5): grip w trybie kopiowania (ma odmówić), grip z kopią o tym samym numerze + Ctrl+Z/Y, `'ZOOM`, 2 rysunki,
  RC_BAR_FORMAT → Polilinia + restart.
- Po W4e/W4f/W4h: obrys po zewnętrznej, UB 140×700, RC_SECTION, RC_REBUILD_BARS + Ctrl+Z, pole „Kąt [°]”, uwagi BS w oknie.
Pełne listy: `docs/RANO_DECYZJE.md`.

## Następne kroki (plan)
- Etap 4: gripy prętów giętych (ramiona U-bara, haki).
- Etap 8: panel właściwości (paleta jak ASD). Etap 9: żywa tabela zestawienia.
- W3: #9 komenda przezroczysta w gripie rozkładu, #10 RC_BAR_END, #11 puste catch.
- W4d-3: geometria rzadkich kształtów (23, 25–29, 31, 32, 34–36, 41, 47, 56, 98).
- Etapy 5–7: porządki, konwersja starych rysunków (RC_CONVERT_BARS), eksport blok → polilinia dla kolegi.

## Uwagi techniczne
- Build tylko do staging: `dotnet build src/BricsCadRc.csproj -c Release "-p:OutputPath=..\bin-staging\"`;
  testy dymne `-p:BuildProjectReferences=false` (inaczej nadpisują `bin`). Do `bin` kopiować po testach, przy zamkniętym BricsCAD.
- Recenzent ChatGPT (Codex) — limit do 2026-10-14; do tego czasu recenzje agentem `planista-recenzent`.
- Dokument dla kolegi: `docs/BricsCadRc_uwagi_i_NA_Smart_RC_2026-10-09.docx` (generator w scratchpadzie sesji — przy zmianach
  łatwiej edytować sam .docx).
