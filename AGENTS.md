# BricsCadRc — instrukcje dla Codex

Plugin .NET (net48, C# 9, WPF) do BricsCAD V25: zbrojenie płyt żelbetowych — pręty, rozkłady,
opisy, zestawienie BS 8666, automatyczne zbrojenie płyt (AutoRebar), tagi przebicia.

**Zanim cokolwiek zmienisz, przeczytaj [ARCHITECTURE.md](ARCHITECTURE.md).** Zasady inżynierskie
z sekcji „Zasady AutoRebar” i „Numeracja” są wymaganiami użytkownika — nie wolno ich łamać ani
„upraszczać” bez jego zgody.

**Wzorzec:** wersja kolegi z GitHuba (`origin/main`, commit `aba0ed1`) wyznacza zasady inżynierskie i logikę.
Trwa refaktor „Plan C” ([docs/PLAN_C.md](docs/PLAN_C.md)): pręt jako mądry obiekt (blok z gripami).
Zmieniamy typ obiektów, nie wyniki — różnica wyniku na rysunku względem wzorca wymaga zgody użytkownika.
**Wyjątek (decyzja użytkownika 2026-10-09):** błędy wzorca opisane w `docs/Uwagi_dla_kolegi_BricsCadRc_2026-10-08.docx`
poprawiamy u nas (lista i status: `docs/POPRAWKI_WZORCA.md`). Tylko te różnice są dozwolone; każda inna = BLOKER.
S-REG porównuje z wzorcem z listą oczekiwanych różnic (`tools/RegressionDump/expected-diffs`).

## Komunikacja
- Zawsze po polsku.
- Użytkownik (Bartek, konstruktor) testuje w BricsCAD. Po każdej zmianie podaj: co zmieniono,
  które pliki, i krótką listę testów do wykonania w BricsCAD.

## Zespół agentów
- `planista-recenzent` (Codex, `.Codex/agents/`) — obmyśla sposób wykonania zadania (plan).
  Nie edytuje plików. Recenzuje diff tylko wtedy, gdy recenzja ChatGPT jest niedostępna.
- `implementator` (Codex, `.Codex/agents/`) — realizuje plan i poprawki, buduje projekt.
- **Recenzent ChatGPT** (Codex CLI, tylko odczyt) — niezależna recenzja planu i diffu:
  `scripts/recenzja-gpt.ps1` (instrukcja: `scripts/recenzent-gpt.md`), komenda `/recenzja-gpt`.
  Uwagi ChatGPT Codex weryfikuje w kodzie przed przekazaniem implementatorowi — nie przyjmuje ich w ciemno.

Przebieg zadania: plan (planista-recenzent) → recenzja planu przez ChatGPT przy większych zmianach →
akceptacja planu przez użytkownika → implementacja + build → recenzja diffu przez ChatGPT →
weryfikacja uwag → poprawki (pętla, max 3 rundy) → instrukcja testów dla użytkownika.
Przy sprzecznościach między planem a ARCHITECTURE.md albo niejasnych wymaganiach inżynierskich —
zapytaj użytkownika, nie zgaduj.

## Build
```
dotnet build src/BricsCadRc.csproj -c Release
```
- Nie używaj `build.bat` (ma `pause` — zawiesza się).
- Wynik: `bin\NaSmartRc.dll` (produkt „NA Smart RC”; stara wtyczka kolegi to `BricsCadRc.dll` — przełączanie autoloadu: `scripts\wybierz-wtyczke.ps1`). Jeśli build nie może nadpisać DLL — BricsCAD ma go załadowanego;
  poproś użytkownika o zamknięcie BricsCAD, nie obchodź tego.
- Testy w prawdziwym BricsCAD (skrypt, bez GUI): `tools\BrxSmokeTests\run-planc-*.ps1` — wyniki PASS/FAIL do pliku.
- Testy jednostkowe (czysta geometria, bez BricsCAD API):
  `dotnet test tests/BricsCadRc.Tests.csproj`

## Git
- **Nie commituj i nie pushuj.** Commit robi użytkownik po przetestowaniu. Na koniec możesz
  zaproponować treść commita.
- Commit tylko po potwierdzeniu, że poprawka działa.

## Styl kodu
- Komentarze i komunikaty dla użytkownika po polsku (komunikaty w linii poleceń z prefiksem
  np. `[AutoRebar]`, `[RC AUTO]`).
- Zachowuj końce linii pliku (większość plików CRLF).
- Zamiast pustych `catch {}` używaj `Log.Error("Klasa.Metoda", ex)` (log: `%LOCALAPPDATA%\NaSmartRc\log.txt`).
- Liczby z/do tekstu: `CultureInfo.InvariantCulture` / `NumberParser`.
- Powiązania między obiektami w XData tylko przez `XLink.Write` / `XLink.Read` / `XLink.Same`.
- Numery pozycji tylko przez `PositionCounter.NextAuto`.
