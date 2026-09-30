# BricsCadRc — instrukcje dla Claude Code

Plugin .NET (net48, C# 9, WPF) do BricsCAD V25: zbrojenie płyt żelbetowych — pręty, rozkłady,
opisy, zestawienie BS 8666, automatyczne zbrojenie płyt (AutoRebar), tagi przebicia.

**Zanim cokolwiek zmienisz, przeczytaj [ARCHITECTURE.md](ARCHITECTURE.md).** Zasady inżynierskie
z sekcji „Zasady AutoRebar” i „Numeracja” są wymaganiami użytkownika — nie wolno ich łamać ani
„upraszczać” bez jego zgody.

## Komunikacja
- Zawsze po polsku.
- Użytkownik (Bartek, konstruktor) testuje w BricsCAD. Po każdej zmianie podaj: co zmieniono,
  które pliki, i krótką listę testów do wykonania w BricsCAD.

## Zespół agentów
- `planista-recenzent` — obmyśla sposób wykonania zadania (plan), potem recenzuje diff
  implementatora. Nie edytuje plików.
- `implementator` — realizuje plan i poprawki recenzenta, buduje projekt.

Przebieg zadania: plan → akceptacja planu przez użytkownika przy większych zmianach →
implementacja + build → recenzja → poprawki (pętla, max 3 rundy) → instrukcja testów dla użytkownika.
Przy sprzecznościach między planem a ARCHITECTURE.md albo niejasnych wymaganiach inżynierskich —
zapytaj użytkownika, nie zgaduj.

## Build
```
dotnet build src/BricsCadRc.csproj -c Release
```
- Nie używaj `build.bat` (ma `pause` — zawiesza się).
- Wynik: `bin\BricsCadRc.dll`. Jeśli build nie może nadpisać DLL — BricsCAD ma go załadowanego;
  poproś użytkownika o zamknięcie BricsCAD, nie obchodź tego.
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
- Zamiast pustych `catch {}` używaj `Log.Error("Klasa.Metoda", ex)` (log: `%LOCALAPPDATA%\BricsCadRc\log.txt`).
- Liczby z/do tekstu: `CultureInfo.InvariantCulture` / `NumberParser`.
- Powiązania między obiektami w XData tylko przez `XLink.Write` / `XLink.Read` / `XLink.Same`.
- Numery pozycji tylko przez `PositionCounter.NextAuto`.
