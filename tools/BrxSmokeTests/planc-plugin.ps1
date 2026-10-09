# Wspólne funkcje skryptów run-planc-*.ps1: wybór DLL wtyczki (-PluginDll, np. bin-staging\NaSmartRc.dll).
# Dot-source: . (Join-Path $here 'planc-plugin.ps1')
#
# Na czas przebiegu: LOADER klucza autoload HKCU\...\Applications\NaSmartRc = wskazany DLL (gdy klucz istnieje),
# skrypt .scr — kopia z NETLOAD wskazanego DLL. Po przebiegu rejestr przywracany DOKŁADNIE (Restore-PluginLoader w finally).
# BricsCAD użytkownika (już działający) nie jest ruszany — test uruchamia osobną instancję /automation.

$script:PlancAppsKey = 'HKCU:\Software\Bricsys\BricsCAD\V25x64\en_US\Applications'

function Resolve-PluginDll([string]$requested, [string]$repoRoot) {
    if ([string]::IsNullOrWhiteSpace($requested)) { return (Join-Path $repoRoot 'bin\NaSmartRc.dll') }
    if ([System.IO.Path]::IsPathRooted($requested)) { return [System.IO.Path]::GetFullPath($requested) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $requested))
}

# Zapamiętuje LOADER klucza NaSmartRc (czy klucz i wartość istniały) i ustawia go na $dll.
function Set-PluginLoader([string]$dll) {
    $path = Join-Path $script:PlancAppsKey 'NaSmartRc'
    $snap = @{ KeyExists = (Test-Path -LiteralPath $path); Exists = $false; Value = $null; Kind = $null }
    if ($snap.KeyExists) {
        $k = Get-Item -LiteralPath $path
        if ($k.GetValueNames() -contains 'LOADER') {
            $snap.Exists = $true
            $snap.Value  = $k.GetValue('LOADER')
            $snap.Kind   = $k.GetValueKind('LOADER')
        }
        New-ItemProperty -LiteralPath $path -Name 'LOADER' -Value $dll -PropertyType String -Force | Out-Null
        Write-Host "Autoload NaSmartRc na czas testu: LOADER=$dll"
    } else {
        Write-Host "Brak klucza autoload NaSmartRc — wtyczkę wczytuje tylko NETLOAD ze skryptu."
    }
    return $snap
}

function Restore-PluginLoader($snap) {
    if ($null -eq $snap -or -not $snap.KeyExists) { return }
    $path = Join-Path $script:PlancAppsKey 'NaSmartRc'
    if (-not (Test-Path -LiteralPath $path)) { New-Item -Path $path -Force | Out-Null }
    if ($snap.Exists) {
        New-ItemProperty -LiteralPath $path -Name 'LOADER' -Value $snap.Value -PropertyType $snap.Kind -Force | Out-Null
        Write-Host "Autoload NaSmartRc przywrócony: LOADER=$($snap.Value)"
    } else {
        Remove-ItemProperty -LiteralPath $path -Name 'LOADER' -ErrorAction SilentlyContinue
        Write-Host "Autoload NaSmartRc przywrócony: bez LOADER"
    }
}

# Kopia skryptu .scr z NETLOAD wskazanego DLL wtyczki (linia z ...\NaSmartRc.dll w cudzysłowie).
function New-PluginScript([string]$scrPath, [string]$dll, [string]$outDir) {
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null
    $fwd = $dll.Replace('\', '/')
    $lines = Get-Content -LiteralPath $scrPath
    $n = 0
    $out = foreach ($l in $lines) {
        if ($l -match '^".*[\\/]NaSmartRc\.dll"\s*$') { $n++; '"' + $fwd + '"' } else { $l }
    }
    if ($n -ne 1) { throw "skrypt $scrPath : oczekiwano 1 linii NETLOAD NaSmartRc.dll, znaleziono $n" }
    $target = Join-Path $outDir ([System.IO.Path]::GetFileNameWithoutExtension($scrPath) + '.run.scr')
    [System.IO.File]::WriteAllLines($target, [string[]]$out, (New-Object System.Text.ASCIIEncoding))
    Write-Host "Skrypt przebiegu: $target (NETLOAD $fwd)"
    return $target
}

# Ostrzeżenie (nie błąd), gdy działa już BricsCAD użytkownika — test startuje osobną instancję.
function Show-RunningBricsWarning {
    $running = Get-Process -Name bricscad -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "Uwaga: BricsCAD już działa (PID: $($running.Id -join ', ')) — test uruchamia osobną instancję; nie zamykam jej." -ForegroundColor Yellow
    }
}
