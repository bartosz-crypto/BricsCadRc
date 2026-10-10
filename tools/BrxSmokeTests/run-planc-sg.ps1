# SG — testy dymne bezpiecznika aktualności przekrojów RC_SECTION (docs/PLAN_BEZPIECZNIK_PRZEKROJOW_2026-10-10.md, etapy 1–4)
# w prawdziwym BricsCAD V25 (SG1–SG5, SG8–SG12, SG14, SG15 STRETCH, SG16 zdublowany GUID; etap 3: SG6 / SG7 RC_SECTION_UPDATE + UNDO / REDO;
# etap 4: SG11b adopcja; etap 2: SG13 -PLOT / EXPORTPDF, SGO otwarcie rysunku). Rysunek NIE jest zapisywany (_QUIT → _Y).
# PDF i rysunek SGO: %TEMP%\rc-sg (czyszczony przed przebiegiem; @OUT@ w skrypcie = ten katalog).
# Uruchamia ukrytą instancję BricsCAD ze skryptem planc-sg.scr, czeka na koniec (limit czasu),
# wypisuje planc-sg-results.txt. Kod wyjścia: 0 = wszystko PASS/SKIP, 1 = FAIL / brak raportu / limit czasu.
#
# Wymaga zbudowanych: bin\NaSmartRc.dll (dotnet build src/BricsCadRc.csproj -c Release)
# oraz tools\BrxSmokeTests\bin\Release\net48\BrxSmokeTests.dll
# (dotnet build tools/BrxSmokeTests/BrxSmokeTests.csproj -c Release -p:BuildProjectReferences=false).
#
# -PluginDll <ścieżka>: DLL wtyczki do testu (domyślnie bin\NaSmartRc.dll), np. bin-staging\NaSmartRc.dll,
# gdy bin\ trzyma działający BricsCAD. Na czas przebiegu LOADER autoload NaSmartRc = ten DLL (przywracany).

param(
    [int]$TimeoutSec = 300,
    [int]$QuitGraceSec = 30,
    [string]$PluginDll = ''
)

$ErrorActionPreference = 'Stop'

$here      = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo      = Resolve-Path (Join-Path $here '..\..')
$scr       = Join-Path $here 'planc-sg.scr'
$outDir    = Join-Path $here 'bin\Release\net48'
$results   = Join-Path $outDir 'planc-sg-results.txt'
$pdfDir    = Join-Path ([System.IO.Path]::GetTempPath()) 'rc-sg'   # bez spacji — ścieżka jest odpowiedzią w skrypcie
$testDll   = Join-Path $outDir 'BrxSmokeTests.dll'
. (Join-Path $here 'planc-plugin.ps1')
$pluginDll = Resolve-PluginDll $PluginDll $repo
$exe       = 'C:\Program Files\Bricsys\BricsCAD V25 en_US\bricscad.exe'
$template  = Join-Path $env:LOCALAPPDATA 'Bricsys\BricsCAD\V25x64\en_US\Templates\Default-mm.dwt'

function Fail([string]$msg) {
    Write-Host "FAIL: $msg" -ForegroundColor Red
    exit 1
}

foreach ($f in @($exe, $scr, $testDll, $pluginDll)) {
    if (-not (Test-Path -LiteralPath $f)) { Fail "brak pliku: $f" }
}

if (Test-Path -LiteralPath $results) { Remove-Item -LiteralPath $results -Force }
New-Item -ItemType Directory -Force -Path $pdfDir | Out-Null
Get-ChildItem -LiteralPath $pdfDir -Include '*.pdf', '*.dwg', '*.bak' -File -Recurse -ErrorAction SilentlyContinue | Remove-Item -Force

$argList = @('/automation')
if (Test-Path -LiteralPath $template) {
    $argList += @('/t', "`"$template`"")
    Write-Host "Szablon: $template"
} else {
    Write-Host "Szablon Default-mm.dwt nie znaleziony — start bez /t."
}
# @OUT@ → katalog PDF (ukośniki), potem kopia z NETLOAD wskazanego DLL
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$srcScr = Join-Path $outDir 'planc-sg.src.scr'
$pdfFwd = $pdfDir.Replace('\', '/')
$scrLines = (Get-Content -LiteralPath $scr) | ForEach-Object { $_.Replace('@OUT@', $pdfFwd) }
[System.IO.File]::WriteAllLines($srcScr, [string[]]$scrLines, (New-Object System.Text.ASCIIEncoding))
$runScr = New-PluginScript $srcScr $pluginDll $outDir
Remove-Item -LiteralPath $srcScr -Force
$argList += @('/b', "`"$runScr`"")

Show-RunningBricsWarning
$loaderSnap = Set-PluginLoader $pluginDll
$proc = $null
try {
Write-Host "Start: `"$exe`" $($argList -join ' ')"
$proc = Start-Process -FilePath $exe -ArgumentList $argList -PassThru
$sw = [System.Diagnostics.Stopwatch]::StartNew()
$reportSeenAt = $null
$timedOut = $false

while (-not $proc.HasExited) {
    Start-Sleep -Seconds 2
    if ($null -eq $reportSeenAt -and (Test-Path -LiteralPath $results)) {
        $txt = Get-Content -LiteralPath $results -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
        if ($txt -and $txt -match '(?m)^END\s*$') { $reportSeenAt = $sw.Elapsed.TotalSeconds }
    }
    # Raport gotowy, a BricsCAD nie kończy (np. okno zapytania przy _QUIT) — zamknij po okresie łaski
    if ($null -ne $reportSeenAt -and ($sw.Elapsed.TotalSeconds - $reportSeenAt) -gt $QuitGraceSec) {
        Write-Host "Raport gotowy, BricsCAD nie zakończył się po $QuitGraceSec s — zamykam proces."
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        break
    }
    if ($sw.Elapsed.TotalSeconds -gt $TimeoutSec) {
        $timedOut = $true
        Write-Host "Przekroczono limit czasu $TimeoutSec s — zabijam BricsCAD." -ForegroundColor Red
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        break
    }
}
}
finally {
    # Tylko własna instancja testu (błąd / Ctrl+C) — BricsCAD użytkownika nie jest ruszany
    if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
    if ($proc) { try { $proc.WaitForExit(30000) | Out-Null } catch { Write-Host "WaitForExit: $($_.Exception.Message)" } }
    Restore-PluginLoader $loaderSnap
}

Write-Host ("Czas: {0:0} s" -f $sw.Elapsed.TotalSeconds)
Write-Host ''

if (-not (Test-Path -LiteralPath $results)) { Fail "brak pliku wyników $results (skrypt nie dotarł do RCSMOKE_SG_SETUP?)" }

$lines = Get-Content -LiteralPath $results -Encoding UTF8
foreach ($l in $lines) {
    if ($l -like 'FAIL*')     { Write-Host $l -ForegroundColor Red }
    elseif ($l -like 'SKIP*') { Write-Host $l -ForegroundColor Yellow }
    else                      { Write-Host $l }
}
Write-Host ''

$hasEnd  = ($lines | Where-Object { $_ -eq 'END' }).Count -gt 0
$failCnt = ($lines | Where-Object { $_ -like 'FAIL*' }).Count

if ($timedOut)    { Fail "limit czasu (wyniki częściowe powyżej)" }
if (-not $hasEnd) { Fail "raport niekompletny — brak END (skrypt rozjechał się przed RCSMOKE_SG_REPORT)" }
if ($failCnt -gt 0) { Fail "$failCnt asercji FAIL" }

Write-Host 'PASS: wszystkie asercje PASS/SKIP' -ForegroundColor Green
exit 0
