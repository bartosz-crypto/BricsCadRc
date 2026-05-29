# BricsCadRc — Instalacja

## Wymagania

- BricsCAD V20 lub nowszy (testowane na V25)
- Windows 64-bit
- .NET Framework 4.8

## Instalacja

1. Pobierz `BricsCadRc-<wersja>.zip` z wewnętrznego dysku zespołu.
2. Rozpakuj. Wewnątrz znajdziesz folder `BricsCadRc.bundle`.
3. **Zamknij BricsCAD** jeśli jest otwarty.
4. Skopiuj folder `BricsCadRc.bundle` (cały folder, nie zawartość) do:
   ```
   %APPDATA%\Bricsys\BricsCAD V25 en_US\ApplicationPlugins\
   ```
   (Wklej tę ścieżkę w pasek Eksploratora Windows — wersja katalogu zależy od zainstalowanego BricsCAD.)
5. Uruchom BricsCAD. Plugin załaduje się automatycznie.
6. Sprawdź: na wstążce powinna pojawić się zakładka **RC SLAB** z komendami.

## Aktualizacja

1. Zamknij BricsCAD.
2. Usuń stary folder `BricsCadRc.bundle` z `ApplicationPlugins\`.
3. Wklej nowy folder z nowej paczki.
4. Uruchom BricsCAD.

## Deinstalacja

1. Zamknij BricsCAD.
2. Usuń `BricsCadRc.bundle` z `ApplicationPlugins\`.

## Ścieżki ApplicationPlugins dla różnych wersji BricsCAD

| Wersja     | Ścieżka                                                         |
|------------|-----------------------------------------------------------------|
| V25        | `%APPDATA%\Bricsys\BricsCAD V25 en_US\ApplicationPlugins\`     |
| V24        | `%APPDATA%\Bricsys\BricsCAD V24 en_US\ApplicationPlugins\`     |
| V23        | `%APPDATA%\Bricsys\BricsCAD V23 en_US\ApplicationPlugins\`     |

## Troubleshooting

**Komendy RC_* nie działają / brak wstążki:**
- Sprawdź czy folder `BricsCadRc.bundle` jest w prawidłowej lokalizacji.
- W BricsCAD wpisz `NETLOAD` ręcznie wskazując `BricsCadRc.dll` z folderu `Contents\` — jeśli komendy działają po ręcznym NETLOAD, problem leży w `PackageContents.xml`.
- Sprawdź logi: `_BRICSCAD > Settings > System > Application Plugin Log`.

**Plugin nie ładuje się po update BricsCAD:**
- Sprawdź czy nowa wersja BricsCAD mieści się w zakresie `SeriesMin`–`SeriesMax` w `PackageContents.xml`.
- Zaktualizuj `SeriesMax` i przebuduj paczkę (`./build-bundle.ps1`).

**Błąd .NET przy ładowaniu:**
- Upewnij się że .NET Framework 4.8 jest zainstalowany (`winver` → sprawdź wersję Windows, .NET 4.8 jest wbudowany w Windows 10 1903+).
