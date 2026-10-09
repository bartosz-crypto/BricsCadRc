@echo off
echo [NA Smart RC] Budowanie pluginu...
cd /d "%~dp0src"
dotnet build BricsCadRc.csproj -c Release
if errorlevel 1 (
    echo [BLAD] Kompilacja nieudana.
    pause
    exit /b 1
)
echo.
echo [OK] NaSmartRc.dll gotowy w folderze bin\
echo.
echo Aby zaladowac w BricsCAD wpisz: NETLOAD
echo Wybierz plik: %~dp0bin\NaSmartRc.dll
pause
