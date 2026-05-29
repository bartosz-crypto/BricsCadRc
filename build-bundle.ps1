# build-bundle.ps1 - tworzy paczkę .bundle do dystrybucji w zespole (BricsCAD)
param(
    [string]$Configuration = "Release",
    [string]$Version = "2026.05"
)

$ErrorActionPreference = "Stop"

# === Ścieżki ===
$bundleSrc  = "$PSScriptRoot\bundle-template\BricsCadRc.bundle"
$bundleOut  = "$PSScriptRoot\dist\BricsCadRc.bundle"
$zipOut     = "$PSScriptRoot\dist\BricsCadRc-$Version.zip"

# === Czyszczenie dist/ ===
if (Test-Path "$PSScriptRoot\dist") { Remove-Item "$PSScriptRoot\dist" -Recurse -Force }
New-Item -ItemType Directory -Path "$PSScriptRoot\dist" -Force | Out-Null

# === Build ===
Write-Host "Building $Configuration..." -ForegroundColor Cyan
dotnet build "$PSScriptRoot\src\BricsCadRc.csproj" -c $Configuration
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

# === Kopiowanie struktury bundle ===
Write-Host "Copying bundle template..." -ForegroundColor Cyan
Copy-Item -Path $bundleSrc -Destination $bundleOut -Recurse

# === Kopiowanie DLL + zależności do Contents/ ===
# OutputPath pluginu to bin\ (bez podfolderu konfiguracji — patrz csproj)
$binPath     = "$PSScriptRoot\bin"
$contentsPath = "$bundleOut\Contents"

Write-Host "Copying DLLs from $binPath..." -ForegroundColor Cyan

# Główna DLL pluginu
Copy-Item "$binPath\BricsCadRc.dll" "$contentsPath\"
Write-Host "  + BricsCadRc.dll"

# Zależności — wszystko poza BricsCAD SDK (BrxMgd, TD_Mgd - są w katalogu BricsCAD)
$skipList = @("BrxMgd.dll", "TD_Mgd.dll")

Get-ChildItem "$binPath\*.dll" | Where-Object {
    $_.Name -ne "BricsCadRc.dll" -and
    $skipList -notcontains $_.Name
} | ForEach-Object {
    Write-Host "  + $($_.Name)"
    Copy-Item $_.FullName "$contentsPath\"
}

# === Aktualizacja wersji w PackageContents.xml ===
$pkgXml  = "$bundleOut\PackageContents.xml"
$content = Get-Content $pkgXml -Raw
$content = $content -replace 'FriendlyVersion="[^"]+"', "FriendlyVersion=`"$Version`""
Set-Content -Path $pkgXml -Value $content -Encoding utf8

# === ZIP do dystrybucji ===
Write-Host "Creating ZIP..." -ForegroundColor Cyan
Compress-Archive -Path $bundleOut -DestinationPath $zipOut

$zipSize = [math]::Round((Get-Item $zipOut).Length / 1KB, 1)

Write-Host ""
Write-Host "==============================================" -ForegroundColor Green
Write-Host " Bundle gotowy:" -ForegroundColor Green
Write-Host "   $bundleOut" -ForegroundColor Yellow
Write-Host "   $zipOut  ($zipSize KB)" -ForegroundColor Yellow
Write-Host "==============================================" -ForegroundColor Green
Write-Host ""
Write-Host "Instalacja (BricsCAD V25):" -ForegroundColor Cyan
Write-Host '  Skopiuj BricsCadRc.bundle do:' -ForegroundColor White
Write-Host '  %APPDATA%\Bricsys\BricsCAD V25 en_US\ApplicationPlugins\' -ForegroundColor Yellow
