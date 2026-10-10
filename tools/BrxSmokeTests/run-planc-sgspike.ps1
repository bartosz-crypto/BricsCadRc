# ETAP 0 — spike bezpiecznika przekrojów (docs/PLAN_BEZPIECZNIK_PRZEKROJOW_2026-10-10.md, krok 0) w prawdziwym BricsCAD V25.
# Przebiegi (każdy = osobna ukryta instancja BricsCAD /automation, zabijana po limicie czasu — tylko własna):
#   main        — planc-sgspike.scr: -PLOT / EXPORTPDF do PDF, warstwa niedrukowana, Veto (LISP i poziom skryptu);
#   dlg-<CMD>   — skrypt generowany: Veto włączone, komenda z oknem (PLOT, PRINT, PUBLISH, -PUBLISH) — gdy Veto
#                 nie zadziała przed oknem, skrypt zawiśnie (limit czasu) i log pokaże, czy zdarzenie w ogóle przyszło.
# Logi: tools\BrxSmokeTests\bin\Release\net48\planc-sgspike-<tag>.txt (+ kopia logu linii poleceń BricsCAD -cmdlog.txt).
# Kod wyjścia: 0 = log main z END; 1 = brak.
#
# Wymaga zbudowanych: wtyczki (domyślnie bin\NaSmartRc.dll; -PluginDll bin-staging\NaSmartRc.dll) i
# tools\BrxSmokeTests\bin\Release\net48\BrxSmokeTests.dll (dotnet build tools/BrxSmokeTests/BrxSmokeTests.csproj -c Release
# -p:BuildProjectReferences=false). Na czas przebiegu LOADER autoload NaSmartRc = ten DLL (przywracany).
# Rysunki NIE są zapisywane (_QUIT → _Y = porzuć zmiany).

param(
    [int]$MainTimeoutSec = 150,
    [int]$DialogTimeoutSec = 45,
    [string]$PluginDll = '',
    [string[]]$DialogCommands = @('PLOT', 'PRINT', 'PUBLISH', '-PUBLISH'),
    [switch]$MainOnly   # tylko przebieg main (bez komend z oknem)
)

$ErrorActionPreference = 'Stop'

$here      = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo      = Resolve-Path (Join-Path $here '..\..')
$scr       = Join-Path $here 'planc-sgspike.scr'
$outDir    = Join-Path $here 'bin\Release\net48'
$pdfDir    = Join-Path ([System.IO.Path]::GetTempPath()) 'rc-sgspike'   # bez spacji — ścieżka jest odpowiedzią w skrypcie
$testDll   = Join-Path $outDir 'BrxSmokeTests.dll'
. (Join-Path $here 'planc-plugin.ps1')
$pluginDll = Resolve-PluginDll $PluginDll $repo
$exe       = 'C:\Program Files\Bricsys\BricsCAD V25 en_US\bricscad.exe'
$template  = Join-Path $env:LOCALAPPDATA 'Bricsys\BricsCAD\V25x64\en_US\Templates\Default-mm.dwt'
$cmdLogDir = Join-Path $env:LOCALAPPDATA 'Bricsys\BricsCAD\V25x64\en_US'

function Fail([string]$msg) {
    Write-Host "FAIL: $msg" -ForegroundColor Red
    exit 1
}

foreach ($f in @($exe, $scr, $testDll, $pluginDll)) {
    if (-not (Test-Path -LiteralPath $f)) { Fail "brak pliku: $f" }
}

New-Item -ItemType Directory -Force -Path $pdfDir | Out-Null
Get-ChildItem -LiteralPath $pdfDir -Filter '*.pdf' -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem -LiteralPath $outDir -Filter 'planc-sgspike-*.txt' -ErrorAction SilentlyContinue | Remove-Item -Force

$pdfFwd = $pdfDir.Replace('\', '/')

# Jeden przebieg: skrypt → instancja BricsCAD → czekanie do końca albo limitu (zabicie tylko własnego procesu).
function Invoke-Phase([string]$tag, [string[]]$scriptLines, [int]$timeoutSec) {
    $tmpScr = Join-Path $outDir ("planc-sgspike-" + $tag + ".src.scr")
    [System.IO.File]::WriteAllLines($tmpScr, $scriptLines, (New-Object System.Text.ASCIIEncoding))
    $runScr = New-PluginScript $tmpScr $pluginDll $outDir
    Remove-Item -LiteralPath $tmpScr -Force
    $argList = @('/automation')
    if (Test-Path -LiteralPath $template) { $argList += @('/t', "`"$template`"") }
    $argList += @('/b', "`"$runScr`"")
    $started = Get-Date
    $proc = Start-Process -FilePath $exe -ArgumentList $argList -PassThru
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $timedOut = $false
    try {
        while (-not $proc.HasExited) {
            Start-Sleep -Seconds 2
            if ($sw.Elapsed.TotalSeconds -gt $timeoutSec) {
                $timedOut = $true
                Write-Host "[$tag] limit czasu $timeoutSec s — zamykam własną instancję (PID $($proc.Id))." -ForegroundColor Yellow
                Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
                break
            }
        }
    }
    finally {
        if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
        if ($proc) { try { $proc.WaitForExit(30000) | Out-Null } catch { Write-Host "WaitForExit: $($_.Exception.Message)" } }
    }
    Write-Host ("[{0}] czas {1:0} s{2}" -f $tag, $sw.Elapsed.TotalSeconds, $(if ($timedOut) { ' (LIMIT CZASU)' } else { '' }))
    # Kopia logu linii poleceń (LOGFILEON) z tego przebiegu
    $cl = Get-ChildItem -LiteralPath $cmdLogDir -Filter '*.log' -ErrorAction SilentlyContinue |
          Where-Object { $_.LastWriteTime -ge $started.AddSeconds(-2) -and $_.Name -ne 'bricscad.log' } |
          Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ($cl) { Copy-Item -LiteralPath $cl.FullName -Destination (Join-Path $outDir ("planc-sgspike-" + $tag + "-cmdlog.txt")) -Force }
    return $timedOut
}

$header = @('FILEDIA', '0', 'CMDDIA', '0', 'OSMODE', '0', 'PICKFIRST', '0', 'CMDECHO', '1', 'LOGFILEON',
            '_NETLOAD', ('"' + $pluginDll.Replace('\', '/') + '"'),
            '_NETLOAD', ('"' + $testDll.Replace('\', '/') + '"'))

Show-RunningBricsWarning
$loaderSnap = Set-PluginLoader $pluginDll
$summary = @()
try {
    # 1) main
    $main = (Get-Content -LiteralPath $scr) | ForEach-Object { $_.Replace('@OUT@', $pdfFwd) }
    $to = Invoke-Phase 'main' $main $MainTimeoutSec
    $summary += "main: " + $(if ($to) { 'limit czasu (skrypt przerwany po Veto na poziomie skryptu?)' } else { 'zakończony' })

    # 2) komendy z oknem — Veto włączone
    $dlgList = if ($MainOnly) { @() } else { $DialogCommands }
    foreach ($c in $dlgList) {
        $tag = 'dlg' + ($c -replace '[^A-Za-z]', '')
        if ($c.StartsWith('-')) { $tag += '-dash' }
        $lines = $header + @('RCSPIKE_SG_SETUP', $tag, 'RCSPIKE_SG_VETO', '1', $c, 'RCSPIKE_SG_MARK', ('po-' + $tag),
                             'RCSPIKE_SG_END', 'LOGFILEOFF', '_QUIT', '_Y')
        $to = Invoke-Phase $tag $lines $DialogTimeoutSec
        $summary += "${tag}: " + $(if ($to) { 'limit czasu (okno nie zatrzymane przez Veto)' } else { 'zakończony (Veto przed oknem)' })
    }
}
finally {
    Restore-PluginLoader $loaderSnap
}

Write-Host ''
foreach ($s in $summary) { Write-Host $s }
Write-Host ''
Write-Host 'Pliki PDF:'
foreach ($n in 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j') {
    $p = Join-Path $pdfDir "$n.pdf"
    if (Test-Path -LiteralPath $p) { Write-Host ("  {0}.pdf  {1} B" -f $n, (Get-Item -LiteralPath $p).Length) }
    else { Write-Host ("  {0}.pdf  BRAK" -f $n) }
}
foreach ($f in Get-ChildItem -LiteralPath $outDir -Filter 'planc-sgspike-*.txt' | Where-Object { $_.Name -notlike '*-cmdlog.txt' }) {
    Write-Host ''
    Write-Host "=== $($f.Name)"
    Get-Content -LiteralPath $f.FullName -Encoding UTF8 | ForEach-Object { Write-Host $_ }
}

$mainLog = Join-Path $outDir 'planc-sgspike-main.txt'
if (-not (Test-Path -LiteralPath $mainLog)) { Fail 'brak logu main' }
if (-not ((Get-Content -LiteralPath $mainLog -Encoding UTF8) -match 'END$')) { Fail 'log main bez END' }
Write-Host 'PASS: spike wykonany (wnioski — ręcznie z logów)' -ForegroundColor Green
exit 0
