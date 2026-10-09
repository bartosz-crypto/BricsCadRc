# S-REG (docs/PLAN_C_etap2.md 6.1, docs/PLAN_C_etap3.md 3.3) — regresja wzorzec (aba0ed1) vs nasza wtyczka w prawdziwym BricsCAD V25.
# Trzy przebiegi BricsCAD /automation ze skryptem reg.scr (każdy zapisuje zrzut surowy i bar-equiv, RC_REG_BAR_EQUIV=1):
#   1) WZORZEC: w rejestrze (HKCU ...\Applications) tymczasowo NaSmartRc LOADCTRLS=0, BricsCadRc LOADCTRLS=2
#      i LOADER = DLL wzorca z worktree,
#   2) NASZ Polyline: NaSmartRc LOADCTRLS=2, LOADER = -PluginDll, RC_REG_BARFORMAT=Polyline → diff SUROWY z wzorcem,
#   3) NASZ Block:    to samo, RC_REG_BARFORMAT=Block → diff BAR-EQUIV z wzorcem (+ asercje „# BARBLOCK”).
# Po każdym przebiegu rejestr przywracany DOKŁADNIE (try/finally). Zmienne środowiskowe trafiają do procesu
# bricscad.exe przez środowisko tego skryptu (ustawiane przed Start-Process, usuwane po przebiegu).
# Kod wyjścia: 0 = PASS, 1 = FAIL / błąd. -BlockInfoOnly: wynik przebiegu Block tylko informacyjnie
# (zostawione dla zgodności; od 3c S-REG Block jest wymagany).
#
# Oczekiwane różnice (docs/PLAN_POPRAWKI_W1_W2.md 5.1, poprawki błędów wzorca — docs/POPRAWKI_WZORCA.md):
#   Każdy przebieg zapisuje DWA zrzuty: główny (RC_REG_DUMP, scenariusz S-REG) i przypadków poprawek (RC_REG_DUMP_FIX,
#   przypadki F…). Diff wzorzec ↔ nasz (multizbiór linii bez „#”) osobno dla „main” i „fix” musi być DOKŁADNIE
#   sumą plików expected-diffs\polyline\*.txt (surowy) i expected-diffs\block\*.txt (bar-equiv). Format pliku:
#       # bug: <nr z POPRAWKI_WZORCA.md, np. 3 albo 3b>
#       # opis: <tekst>
#       # zrzut: main | fix
#       <= <linia dokładnie jak we wzorcu>
#       => <linia dokładnie jak u nas>
#   Raport: NIEOCZEKIWANE (różnice spoza listy = BLOKER) i BRAKUJĄCE (oczekiwane, których nie ma).
#   -ProposeExpected: rzeczywiste różnice do bin\out\proposed-<polyline|block>-<main|fix>.txt (NIGDY do expected-diffs —
#   przypisanie linii do błędu robi człowiek). Przypadki tylko-polilinia ($PolylineOnlyCases) pomijane w porównaniu Block.
#
# Wymaga zbudowanych:
#   C:\Users\test\Desktop\AI\BricsCadRc-wzorzec\bin\BricsCadRc.dll   (git worktree aba0ed1, dotnet build src/BricsCadRc.csproj -c Release)
#   -PluginDll (domyślnie bin\NaSmartRc.dll; np. bin-staging\NaSmartRc.dll, gdy bin\ trzyma działający BricsCAD)
#   tools\RegressionDump\bin\Release\net48\RegressionDump.dll       (dotnet build tools/RegressionDump/RegressionDump.csproj -c Release)

param(
    [int]$TimeoutSec = 600,
    [int]$QuitGraceSec = 30,
    [string]$WzorzecDll = 'C:\Users\test\Desktop\AI\BricsCadRc-wzorzec\bin\BricsCadRc.dll',
    [string]$PluginDll = '',
    [switch]$SkipWzorzec,
    [switch]$SkipNasz,
    [switch]$SkipBlock,
    [switch]$BlockInfoOnly,
    [switch]$ProposeExpected
)

# Przypadki poprawek tylko dla ścieżki polilinii (docs/PLAN_POPRAWKI_W1_W2.md 5.2) — pomijane w porównaniu Block
$PolylineOnlyCases = @('F1e', 'F4d', 'F7a', 'F7b', 'F7c')

$ErrorActionPreference = 'Stop'

$here      = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo      = (Resolve-Path (Join-Path $here '..\..')).Path
$scr       = Join-Path $here 'reg.scr'
$outDir    = Join-Path $here 'bin\out'
$dumpTmp   = Join-Path $outDir 'reg-dump.txt'
$dumpTmpEq = Join-Path $outDir 'reg-dump-bareq.txt'
$fixTmp    = Join-Path $outDir 'reg-dump-fix.txt'
$fixTmpEq  = Join-Path $outDir 'reg-dump-fix-bareq.txt'
$expDir    = Join-Path $here 'expected-diffs'
$poprawki  = Join-Path $repo 'docs\POPRAWKI_WZORCA.md'
$dumpW     = Join-Path $outDir 'dump-wzorzec.txt'
$dumpWEq   = Join-Path $outDir 'dump-wzorzec-bareq.txt'
$dumpN     = Join-Path $outDir 'dump-nasz.txt'
$dumpNEq   = Join-Path $outDir 'dump-nasz-bareq.txt'
$dumpB     = Join-Path $outDir 'dump-nasz-block.txt'
$dumpBEq   = Join-Path $outDir 'dump-nasz-block-bareq.txt'
$diffFile  = Join-Path $outDir 'regression-diff.txt'
$diffFileB = Join-Path $outDir 'regression-diff-block.txt'
$testDll   = Join-Path $here 'bin\Release\net48\RegressionDump.dll'
$ourDll    = Join-Path $repo 'bin\NaSmartRc.dll'
if (-not [string]::IsNullOrWhiteSpace($PluginDll)) {
    if ([System.IO.Path]::IsPathRooted($PluginDll)) { $ourDll = [System.IO.Path]::GetFullPath($PluginDll) }
    else { $ourDll = [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $PluginDll)) }
}
$exe       = 'C:\Program Files\Bricsys\BricsCAD V25 en_US\bricscad.exe'
$template  = Join-Path $env:LOCALAPPDATA 'Bricsys\BricsCAD\V25x64\en_US\Templates\Default-mm.dwt'
$appsKey   = 'HKCU:\Software\Bricsys\BricsCAD\V25x64\en_US\Applications'

function Fail([string]$msg) {
    Write-Host "FAIL: $msg" -ForegroundColor Red
    exit 1
}

foreach ($f in @($exe, $scr, $testDll, $ourDll, $WzorzecDll)) {
    if (-not (Test-Path -LiteralPath $f)) { Fail "brak pliku: $f" }
}
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
Write-Host "Nasza wtyczka: $ourDll"

# --- rejestr: zapis / przywrócenie wartości (z informacją, czy wartość istniała) ---
function Get-RegSnapshot([string]$app) {
    $path = Join-Path $appsKey $app
    $snap = @{ App = $app; KeyExists = (Test-Path -LiteralPath $path); Values = @{} }
    if ($snap.KeyExists) {
        $k = Get-Item -LiteralPath $path
        foreach ($n in @('LOADCTRLS', 'LOADER', 'MANAGED')) {
            if ($k.GetValueNames() -contains $n) {
                $snap.Values[$n] = @{ Exists = $true; Value = $k.GetValue($n); Kind = $k.GetValueKind($n) }
            } else {
                $snap.Values[$n] = @{ Exists = $false }
            }
        }
    }
    return $snap
}

function Restore-RegSnapshot($snap) {
    $path = Join-Path $appsKey $snap.App
    if (-not $snap.KeyExists) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Recurse -Force }
        return
    }
    if (-not (Test-Path -LiteralPath $path)) { New-Item -Path $path -Force | Out-Null }
    foreach ($n in @('LOADCTRLS', 'LOADER', 'MANAGED')) {
        $v = $snap.Values[$n]
        if ($v.Exists) {
            New-ItemProperty -LiteralPath $path -Name $n -Value $v.Value -PropertyType $v.Kind -Force | Out-Null
        } else {
            Remove-ItemProperty -LiteralPath $path -Name $n -ErrorAction SilentlyContinue
        }
    }
}

function Show-Reg([string]$title) {
    Write-Host $title
    foreach ($app in @('NaSmartRc', 'BricsCadRc')) {
        $path = Join-Path $appsKey $app
        if (Test-Path -LiteralPath $path) {
            $p = Get-ItemProperty -LiteralPath $path
            Write-Host ("  {0}: LOADCTRLS={1} LOADER={2}" -f $app, $p.LOADCTRLS, $p.LOADER)
        } else {
            Write-Host "  ${app}: brak klucza"
        }
    }
}

# Autoload: $loadApp ładowany przy starcie (LOADCTRLS=2, LOADER=$dll), $offApp wyłączony (LOADCTRLS=0, gdy klucz istnieje)
function Set-Autoload([string]$loadApp, [string]$dll, [string]$offApp) {
    $pOn  = Join-Path $appsKey $loadApp
    $pOff = Join-Path $appsKey $offApp
    if (Test-Path -LiteralPath $pOff) {
        New-ItemProperty -LiteralPath $pOff -Name 'LOADCTRLS' -Value 0 -PropertyType DWord -Force | Out-Null
    }
    if (-not (Test-Path -LiteralPath $pOn)) {
        New-Item -Path $pOn -Force | Out-Null
        New-ItemProperty -LiteralPath $pOn -Name 'MANAGED' -Value 1 -PropertyType DWord -Force | Out-Null
    }
    New-ItemProperty -LiteralPath $pOn -Name 'LOADCTRLS' -Value 2 -PropertyType DWord -Force | Out-Null
    New-ItemProperty -LiteralPath $pOn -Name 'LOADER' -Value $dll -PropertyType String -Force | Out-Null
}

# Nazwa zrzutu przypadków poprawek dla zrzutu głównego: dump-nasz.txt → dump-nasz-fix.txt, …-bareq.txt → …-fix-bareq.txt
function FixOf([string]$f) { return ($f -replace '(-bareq)?\.txt$', '-fix$1.txt') }

# --- jeden przebieg BricsCAD (zrzut surowy → $dumpOut, bar-equiv → $dumpOutEq; przypadki poprawek → FixOf(...)) ---
function Invoke-BricsRun([string]$label, [string]$dumpOut, [string]$dumpOutEq, [string]$barFormat) {
    foreach ($f in @($dumpTmp, $dumpTmpEq, $fixTmp, $fixTmpEq, $dumpOut, $dumpOutEq, (FixOf $dumpOut), (FixOf $dumpOutEq))) {
        if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force }
    }

    $argList = @('/automation')
    if (Test-Path -LiteralPath $template) { $argList += @('/t', "`"$template`"") }
    $argList += @('/b', "`"$scr`"")

    Write-Host ""
    Write-Host "=== Przebieg: $label ===" -ForegroundColor Cyan
    Write-Host "Start: `"$exe`" $($argList -join ' ')"
    $oldFmt = $env:RC_REG_BARFORMAT
    $oldEq  = $env:RC_REG_BAR_EQUIV
    $proc = $null
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $seenAt = $null
    $timedOut = $false
    try {
    # Środowisko procesu bricscad.exe = środowisko tego skryptu w chwili Start-Process
    $env:RC_REG_BAR_EQUIV = '1'
    if ($barFormat) { $env:RC_REG_BARFORMAT = $barFormat } else { Remove-Item Env:\RC_REG_BARFORMAT -ErrorAction SilentlyContinue }
    Write-Host "Środowisko: RC_REG_BARFORMAT=$($env:RC_REG_BARFORMAT) RC_REG_BAR_EQUIV=$($env:RC_REG_BAR_EQUIV)"
    $proc = Start-Process -FilePath $exe -ArgumentList $argList -PassThru
    while (-not $proc.HasExited) {
        Start-Sleep -Seconds 2
        # Ostatni zapisywany plik = zrzut przypadków poprawek (bar-equiv)
        if ($null -eq $seenAt -and (Test-Path -LiteralPath $fixTmpEq)) {
            $txt = Get-Content -LiteralPath $fixTmpEq -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
            if ($txt -and $txt -match '(?m)^END\s*$') { $seenAt = $sw.Elapsed.TotalSeconds }
        }
        if ($null -ne $seenAt -and ($sw.Elapsed.TotalSeconds - $seenAt) -gt $QuitGraceSec) {
            Write-Host "Zrzut gotowy, BricsCAD nie zakończył się po $QuitGraceSec s — zamykam proces."
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
        # Tylko własny proces (błąd / Ctrl+C) — inne instancje BricsCAD nie są ruszane
        if ($proc -and -not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue }
        # Poczekaj na faktyczne zakończenie procesu (przed przywróceniem rejestru)
        if ($proc) { try { $proc.WaitForExit(30000) | Out-Null } catch { Write-Host "WaitForExit: $($_.Exception.Message)" } }
        if ($null -ne $oldFmt) { $env:RC_REG_BARFORMAT = $oldFmt } else { Remove-Item Env:\RC_REG_BARFORMAT -ErrorAction SilentlyContinue }
        if ($null -ne $oldEq)  { $env:RC_REG_BAR_EQUIV = $oldEq }  else { Remove-Item Env:\RC_REG_BAR_EQUIV -ErrorAction SilentlyContinue }
    }
    Write-Host ("Czas: {0:0} s" -f $sw.Elapsed.TotalSeconds)

    foreach ($pair in @(@($dumpTmp, $dumpOut), @($dumpTmpEq, $dumpOutEq), @($fixTmp, (FixOf $dumpOut)), @($fixTmpEq, (FixOf $dumpOutEq)))) {
        if (-not (Test-Path -LiteralPath $pair[0])) { throw "[$label] brak zrzutu $($pair[0]) (skrypt nie dotarł do RC_REG_DUMP / RC_REG_DUMP_FIX?)" }
        Move-Item -LiteralPath $pair[0] -Destination $pair[1] -Force
        $lines = Get-Content -LiteralPath $pair[1] -Encoding UTF8
        if (-not ($lines -contains 'END')) { throw "[$label] zrzut niekompletny (brak END): $($pair[1])" }
    }
    if ($timedOut) { Write-Host "[$label] uwaga: limit czasu, ale zrzuty kompletne." -ForegroundColor Yellow }
}

# Przebieg naszej wtyczki: autoload NaSmartRc = $ourDll na czas przebiegu, potem rejestr przywrócony
function Invoke-OurRun([string]$label, [string]$dumpOut, [string]$dumpOutEq, [string]$barFormat) {
    $snapN = Get-RegSnapshot 'NaSmartRc'
    $snapB = Get-RegSnapshot 'BricsCadRc'
    try {
        Set-Autoload 'NaSmartRc' $ourDll 'BricsCadRc'
        Show-Reg "Rejestr na czas przebiegu ${label}:"
        Invoke-BricsRun $label $dumpOut $dumpOutEq $barFormat
    }
    finally {
        Restore-RegSnapshot $snapN
        Restore-RegSnapshot $snapB
        Show-Reg 'Rejestr po przywróceniu:'
    }
}

$running = Get-Process -Name bricscad -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Uwaga: BricsCAD już działa (PID: $($running.Id -join ', ')) — test uruchamia osobne instancje /automation; działający BricsCAD nie jest ruszany (rejestr autoload czyta tylko przy starcie)." -ForegroundColor Yellow
}

Show-Reg 'Rejestr przed testem:'

# --- 1) WZORZEC ---
if (-not $SkipWzorzec) {
    $wzErr = $null
    $snapN = Get-RegSnapshot 'NaSmartRc'
    $snapB = Get-RegSnapshot 'BricsCadRc'
    try {
        Set-Autoload 'BricsCadRc' $WzorzecDll 'NaSmartRc'
        Show-Reg 'Rejestr na czas przebiegu WZORCA:'
        Invoke-BricsRun 'WZORZEC' $dumpW $dumpWEq $null
    }
    catch {
        Write-Host "Błąd przebiegu WZORCA: $($_.Exception.Message)" -ForegroundColor Red
        $wzErr = $_.Exception.Message
    }
    finally {
        Restore-RegSnapshot $snapN
        Restore-RegSnapshot $snapB
        Show-Reg 'Rejestr po przywróceniu:'
    }
    if ($wzErr) { Fail "przebieg WZORCA: $wzErr" }
}

# --- 2) NASZ Polyline, 3) NASZ Block ---
if (-not $SkipNasz) {
    try { Invoke-OurRun 'NASZ Polyline' $dumpN $dumpNEq 'Polyline' }
    catch { Fail "przebieg NASZ Polyline: $($_.Exception.Message)" }
    if (-not $SkipBlock) {
        try { Invoke-OurRun 'NASZ Block' $dumpB $dumpBEq 'Block' }
        catch { Fail "przebieg NASZ Block: $($_.Exception.Message)" }
    }
}

# --- porównanie ---
function Read-Dump([string]$f) {
    if (-not (Test-Path -LiteralPath $f)) { Fail "brak zrzutu $f" }
    return @(Get-Content -LiteralPath $f -Encoding UTF8)
}

function Get-Header($all, [string]$prefix) { return (($all | Where-Object { $_ -like "$prefix*" }) -join ' ') }

function Get-Exc($lines) {
    return @($lines | Where-Object { $_ -match '^\S+ EXC ' -or $_ -like 'EXC *' -or $_ -match '^\S+\| EXC ' -or $_ -like 'FATAL *' })
}

# Id przypadku poprawki z linii zrzutu „fix” („== FIX F1b”, „F1b bar …”, „F1b| …”); $null = linia wspólna
function Get-CaseId([string]$l) {
    if ($l -match '^== FIX (F\w+)$') { return $Matches[1] }
    if ($l -match '^(F\w+)(\||\s)') { return $Matches[1] }
    return $null
}

# Zrzut „fix” bez przypadków tylko-polilinia (porównanie Block)
function Remove-PolylineOnly($lines) {
    return @($lines | Where-Object { $id = Get-CaseId $_; -not ($id -and ($PolylineOnlyCases -contains $id)) })
}

# Multizbiór różnic (bez linii „#”): „<= linia” (nadmiar we wzorcu), „=> linia” (nadmiar u nas)
function Get-MultisetDiff($ref, $new) {
    $cnt = New-Object 'System.Collections.Generic.Dictionary[string,int]' ([StringComparer]::Ordinal)
    $order = New-Object System.Collections.Generic.List[string]
    foreach ($l in $ref) {
        if ($l.StartsWith('#')) { continue }
        if (-not $cnt.ContainsKey($l)) { $cnt[$l] = 0; $order.Add($l) }
        $cnt[$l] = $cnt[$l] + 1
    }
    foreach ($l in $new) {
        if ($l.StartsWith('#')) { continue }
        if (-not $cnt.ContainsKey($l)) { $cnt[$l] = 0; $order.Add($l) }
        $cnt[$l] = $cnt[$l] - 1
    }
    $out = New-Object System.Collections.Generic.List[string]
    foreach ($l in $order) {
        $c = $cnt[$l]
        for ($i = 0; $i -lt [Math]::Abs($c); $i++) { $out.Add($(if ($c -gt 0) { "<= $l" } else { "=> $l" })) }
    }
    return ,$out
}

# Pliki expected-diffs\<kind>\*.txt → lista wpisów @{ Zrzut; Line; File; Bug }; błąd formatu = FAIL
function Read-Expected([string]$kind) {
    $dir = Join-Path $expDir $kind
    $list = New-Object System.Collections.Generic.List[object]
    if (-not (Test-Path -LiteralPath $dir)) { return ,$list }
    $known = @()
    if (Test-Path -LiteralPath $poprawki) {
        $known = @(Get-Content -LiteralPath $poprawki -Encoding UTF8 | ForEach-Object { if ($_ -match '^\|\s*(\d+[a-z]?)\s*\|') { $Matches[1] } })
    }
    foreach ($f in (Get-ChildItem -LiteralPath $dir -Filter '*.txt' | Sort-Object Name)) {
        $bug = $null; $zrzut = $null
        foreach ($l in (Get-Content -LiteralPath $f.FullName -Encoding UTF8)) {
            if ($l -match '^#\s*bug:\s*(\d+[a-z]?)\s*$')   { $bug = $Matches[1]; continue }   # np. 3b (W2)
            if ($l -match '^#\s*zrzut:\s*(\S+)\s*$') { $zrzut = $Matches[1]; continue }
            if ($l.StartsWith('#') -or $l.Trim().Length -eq 0) { continue }
            if ($l -notmatch '^(<=|=>) ') { Fail "expected-diffs\$kind\$($f.Name): linia bez '<= ' / '=> ': $l" }
            if (-not $bug)   { Fail "expected-diffs\$kind\$($f.Name): brak '# bug:' przed różnicami" }
            if ($known -notcontains $bug) { Fail "expected-diffs\$kind\$($f.Name): błędu $bug nie ma w docs\POPRAWKI_WZORCA.md" }
            if ($zrzut -ne 'main' -and $zrzut -ne 'fix') { Fail "expected-diffs\$kind\$($f.Name): '# zrzut:' musi być main albo fix" }
            $list.Add(@{ Zrzut = $zrzut; Line = $l; File = $f.Name; Bug = $bug })
        }
    }
    return ,$list
}

# Porównanie z listą oczekiwanych: zwraca liczbę NIEOCZEKIWANYCH + BRAKUJĄCYCH + wyjątków; raport do $outFile
function Compare-Expected([string]$label, [string]$kind, [string]$zrzut, $ref, $new, $expected, [string]$outFile) {
    $actual = Get-MultisetDiff $ref $new
    $exp = @($expected | Where-Object { $_.Zrzut -eq $zrzut } | ForEach-Object { $_.Line })
    # rzeczywiste − oczekiwane (multizbiór)
    $pool = New-Object 'System.Collections.Generic.Dictionary[string,int]' ([StringComparer]::Ordinal)
    foreach ($l in $exp) { if ($pool.ContainsKey($l)) { $pool[$l]++ } else { $pool[$l] = 1 } }
    $unexpected = New-Object System.Collections.Generic.List[string]
    foreach ($l in $actual) {
        if ($pool.ContainsKey($l) -and $pool[$l] -gt 0) { $pool[$l]-- } else { $unexpected.Add($l) }
    }
    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($kv in $pool.GetEnumerator()) { for ($i = 0; $i -lt $kv.Value; $i++) { $missing.Add($kv.Key) } }

    $out = New-Object System.Collections.Generic.List[string]
    $out.Add("S-REG $label ($kind, zrzut $zrzut): '<=' tylko we WZORCU, '=>' tylko w NASZYM")
    $out.Add("Linie: wzorzec=$(@($ref | Where-Object { -not $_.StartsWith('#') }).Count) nasz=$(@($new | Where-Object { -not $_.StartsWith('#') }).Count) różnice=$($actual.Count) oczekiwane=$($exp.Count)")
    $out.Add("== NIEOCZEKIWANE ($($unexpected.Count))")
    foreach ($l in $unexpected) { $out.Add($l) }
    $out.Add("== BRAKUJĄCE ($($missing.Count))")
    foreach ($l in $missing) { $out.Add($l) }
    $out.Add("== WSZYSTKIE RÓŻNICE ($($actual.Count))")
    foreach ($l in $actual) { $out.Add($l) }
    [System.IO.File]::WriteAllLines($outFile, $out, (New-Object System.Text.UTF8Encoding($true)))

    if ($ProposeExpected) {
        $prop = Join-Path $outDir "proposed-$kind-$zrzut.txt"
        $p = New-Object System.Collections.Generic.List[string]
        $p.Add("# bug: ?   (przypisz każdą linię do błędu z docs\POPRAWKI_WZORCA.md — tylko linie wynikające z poprawki)")
        $p.Add("# opis: propozycja z przebiegu $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
        $p.Add("# zrzut: $zrzut")
        foreach ($l in $actual) { $p.Add($l) }
        [System.IO.File]::WriteAllLines($prop, $p, (New-Object System.Text.UTF8Encoding($false)))
        Write-Host "Propozycja (nie do expected-diffs bez przypisania): $prop"
    }

    Write-Host ""
    $exc = @(Get-Exc $ref) + @(Get-Exc $new)
    if ($exc.Count -gt 0) {
        Write-Host "[$label $zrzut] scenariusz niekompletny — wyjątki w krokach:" -ForegroundColor Red
        $exc | Select-Object -Unique | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    }
    if ($unexpected.Count -eq 0 -and $missing.Count -eq 0) {
        Write-Host "[$label $zrzut] różnice = oczekiwane ($($actual.Count) linii). Raport: $outFile" -ForegroundColor Green
    } else {
        Write-Host "[$label $zrzut] NIEOCZEKIWANE=$($unexpected.Count) BRAKUJĄCE=$($missing.Count) (raport: $outFile)" -ForegroundColor Red
        $unexpected | Select-Object -First 40 | ForEach-Object { Write-Host "  NIEOCZEKIWANE $_" }
        $missing | Select-Object -First 40 | ForEach-Object { Write-Host "  BRAKUJĄCE    $_" }
    }
    return ($unexpected.Count + $missing.Count + $exc.Count)
}

$allW    = Read-Dump $dumpW
$allWEq  = Read-Dump $dumpWEq
$allN    = Read-Dump $dumpN
$allWF   = Read-Dump (FixOf $dumpW)
$allWFEq = Read-Dump (FixOf $dumpWEq)
$allNF   = Read-Dump (FixOf $dumpN)

# Sprawdzenie, że załadowała się właściwa wtyczka i właściwy format
$hdrW = Get-Header $allW '# plugin='
$hdrN = Get-Header $allN '# plugin='
Write-Host ""
Write-Host "WZORZEC:       $hdrW"
Write-Host "NASZ Polyline: $hdrN  $(Get-Header $allN '# barformat=')"
if ($hdrW -notlike '# plugin=BricsCadRc |*' -or $hdrW -notlike "*$WzorzecDll*") { Fail "przebieg WZORCA nie wczytał DLL wzorca ($hdrW)" }
if ($hdrN -notlike '# plugin=NaSmartRc |*' -or $hdrN -notlike "*$ourDll*") { Fail "przebieg NASZ Polyline nie wczytał $ourDll ($hdrN)" }
if ((Get-Header $allN '# barformat=') -notlike '# barformat=Polyline*') { Fail "przebieg NASZ Polyline: format prętów nie Polyline" }

Write-Host ""
Write-Host "Kroki (NASZ Polyline):"
foreach ($all in @(,$allN) + @(,$allNF)) {
    $inSteps = $false
    foreach ($l in $all) {
        if ($l -eq '== KROKI' -or $l -eq '== FIX-KROKI') { $inSteps = $true; continue }
        if ($l.StartsWith('== ')) { if ($inSteps) { break } }
        if ($inSteps -and -not $l.StartsWith('#')) { if ($l -match ' EXC ') { Write-Host "  $l" -ForegroundColor Yellow } else { Write-Host "  $l" } }
    }
}

$expPoly  = Read-Expected 'polyline'
$expBlock = Read-Expected 'block'
Write-Host ""
Write-Host "Oczekiwane różnice: polyline=$($expPoly.Count) linii, block=$($expBlock.Count) linii ($expDir)"

$badPoly  = Compare-Expected 'Polyline (surowy)' 'polyline' 'main' $allW $allN $expPoly $diffFile
$badPoly += Compare-Expected 'Polyline (surowy)' 'polyline' 'fix' $allWF $allNF $expPoly (FixOf $diffFile)

$badBlock = 0
if (-not $SkipBlock) {
    $allBEq  = Read-Dump $dumpBEq
    $allBFEq = Read-Dump (FixOf $dumpBEq)
    $hdrB = Get-Header $allBEq '# plugin='
    Write-Host ""
    Write-Host "NASZ Block:    $hdrB  $(Get-Header $allBEq '# barformat=')"
    if ($hdrB -notlike '# plugin=NaSmartRc |*' -or $hdrB -notlike "*$ourDll*") { Fail "przebieg NASZ Block nie wczytał $ourDll ($hdrB)" }
    if ((Get-Header $allBEq '# barformat=') -notlike '# barformat=Block*') { Fail "przebieg NASZ Block: format prętów nie Block (zmienna RC_REG_BARFORMAT nie dotarła?)" }
    $bb = @($allBEq | Where-Object { $_ -like '# BARBLOCK*' }) + @($allBFEq | Where-Object { $_ -like '# BARBLOCK FAIL *' })
    $bb | ForEach-Object { if ($_ -like '# BARBLOCK FAIL *') { Write-Host "  $_" -ForegroundColor Red } else { Write-Host "  $_" } }
    $bbFail = @($bb | Where-Object { $_ -like '# BARBLOCK FAIL *' }).Count
    $badBlock  = Compare-Expected 'Block (bar-equiv)' 'block' 'main' $allWEq $allBEq $expBlock $diffFileB
    $badBlock += Compare-Expected 'Block (bar-equiv)' 'block' 'fix' (Remove-PolylineOnly $allWFEq) (Remove-PolylineOnly $allBFEq) $expBlock (FixOf $diffFileB)
    $badBlock += $bbFail
}

Write-Host ""
if ($badPoly -gt 0) { Fail "S-REG Polyline (surowy): $badPoly różnic nieoczekiwanych/brakujących/wyjątków — $diffFile, $(FixOf $diffFile)" }
if ($badBlock -gt 0) {
    if ($BlockInfoOnly) {
        Write-Host "S-REG Block (bar-equiv): $badBlock różnic/wyjątków/BARBLOCK FAIL — tylko informacyjnie (-BlockInfoOnly), $diffFileB" -ForegroundColor Yellow
    } else {
        Fail "S-REG Block (bar-equiv): $badBlock różnic nieoczekiwanych/brakujących/wyjątków/BARBLOCK FAIL — $diffFileB, $(FixOf $diffFileB)"
    }
}
Write-Host "PASS: S-REG Polyline (surowy) = expected-diffs$(if ($SkipBlock) { '' } elseif ($badBlock -eq 0) { ', Block (bar-equiv) = expected-diffs' } else { '' })" -ForegroundColor Green
exit 0
