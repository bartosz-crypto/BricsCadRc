# K1 — nib zawinięty (POPRAWKI_WZORCA 29, docs/PLAN_BELKI_PRAKTYKA_2026-10-10.md): Holbrook 458, 461-462, 524, 526, 527, 322, 321, 525 —
# Prepare GA + Mesh (225), zrzut rozkładów porównany z wtyczką HEAD. Najpierw przebieg bazowy:
#   run-planc-k1.ps1 -Baseline -PluginDll <DLL wtyczki HEAD>   (RC_K1_TAG=head),
# potem właściwy:  run-planc-k1.ps1 -PluginDll bin-staging\NaSmartRc.dll   (zrzuty „new” porównane z „head”).
# Różnice dozwolone tylko w T1 / T2 / UB 01 / 02 przy krawędziach uskoku z wolnym końcem; bezpiecznik rozstawu < 100 mm.
# GA (DXF, tylko odczyt): -GaDir albo zmienna RC_BEAM_GA. Ukryta instancja BricsCAD (/automation), wyniki planc-k1-results.txt.
# Kod wyjścia: 0 = wszystko PASS/SKIP, 1 = FAIL / brak raportu / limit czasu.
# Na czas przebiegu LOADER autoload NaSmartRc = ten DLL (przywracany w finally).

param(
    [int]$TimeoutSec = 900,
    [int]$QuitGraceSec = 30,
    [string]$PluginDll = '',
    [string]$GaDir = '',
    [switch]$Baseline
)

$ErrorActionPreference = 'Stop'

$here      = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo      = Resolve-Path (Join-Path $here '..\..')
$scr       = Join-Path $here $(if ($Baseline) { 'planc-k1.scr' } else { 'planc-k1.scr' })
$outDir    = Join-Path $here 'bin\Release\net48'
$results   = Join-Path $outDir $(if ($Baseline) { 'planc-k1-base-results.txt' } else { 'planc-k1-results.txt' })
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

# Dane GA i znacznik zrzutów (zmienne dziedziczy proces BricsCAD)
if ([string]::IsNullOrWhiteSpace($GaDir)) { $GaDir = $env:RC_BEAM_GA }
if ([string]::IsNullOrWhiteSpace($GaDir)) {
    $GaDir = Join-Path $env:LOCALAPPDATA 'Temp\claude\C--Users-test-Desktop-AI-BricsCadRc---BJ\234c7608-bb95-42da-b7e6-ea57a103e39e\scratchpad\holbrook\data\ga\p02dxf'
}
$env:RC_BEAM_GA = $GaDir
$env:RC_K1_TAG = $(if ($Baseline) { 'head' } else { 'new' })
Write-Host "GA: $GaDir  (zrzuty: $env:RC_K1_TAG)"

$argList = @('/automation')
if (Test-Path -LiteralPath $template) {
    $argList += @('/t', "`"$template`"")
    Write-Host "Szablon: $template"
} else {
    Write-Host "Szablon Default-mm.dwt nie znaleziony — start bez /t."
}
$runScr = New-PluginScript $scr $pluginDll $outDir
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

if (-not (Test-Path -LiteralPath $results)) { Fail "brak pliku wyników $results (skrypt nie dotarł do RCSMOKE_K1_SETUP?)" }

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
if (-not $hasEnd) { Fail "raport niekompletny — brak END (skrypt rozjechał się przed RCSMOKE_K1_REPORT)" }
if ($failCnt -gt 0) { Fail "$failCnt asercji FAIL" }

Write-Host 'PASS: wszystkie asercje PASS/SKIP' -ForegroundColor Green
exit 0
