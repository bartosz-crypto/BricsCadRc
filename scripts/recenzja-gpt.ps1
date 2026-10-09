<#
.SYNOPSIS
  Niezalezna recenzja ChatGPT (Codex CLI) - wylacznie do odczytu.

.DESCRIPTION
  Uruchamia "codex exec" w piaskownicy read-only z instrukcja z scripts\recenzent-gpt.md.
  Wynik zapisuje do %LOCALAPPDATA%\BricsCadRc\recenzje\ i wypisuje na ekran.
  Wymaga: Codex CLI (npm i -g @openai/codex) zalogowanego kontem ChatGPT (codex login).

.EXAMPLE
  .\scripts\recenzja-gpt.ps1                       # recenzja niezacommitowanych zmian (git diff HEAD)
  .\scripts\recenzja-gpt.ps1 -Baza origin/main     # recenzja zmian wzgledem origin/main
  .\scripts\recenzja-gpt.ps1 -Plan docs\PLAN_C.md  # recenzja planu
  .\scripts\recenzja-gpt.ps1 -Model gpt-5.6-sol -Rozumowanie medium  # inny model (lista: ~\.codex\models_cache.json)
#>
param(
    [string]$Plan,
    [string]$Baza = "HEAD",
    [string]$Uwagi,
    [string]$Model = "gpt-6-astra",
    [ValidateSet("low", "medium", "high", "xhigh")]
    [string]$Rozumowanie = "high"
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$utf8 = New-Object System.Text.UTF8Encoding($false)
$OutputEncoding = $utf8
[Console]::OutputEncoding = $utf8

if (-not (Get-Command codex -ErrorAction SilentlyContinue)) {
    throw "Brak Codex CLI. Instalacja: npm i -g @openai/codex, potem: codex login"
}

$instrukcja = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'recenzent-gpt.md'), $utf8)

if ($Plan) {
    $sciezka = (Resolve-Path $Plan).Path
    $zadanie = "TRYB: PLAN. Zrecenzuj plan z pliku '$sciezka'. Odnies go do aktualnego kodu w repozytorium."
} else {
    $zadanie = "TRYB: DIFF. Zrecenzuj zmiany wzgledem '$Baza': uruchom 'git diff $Baza' oraz 'git status --short'; " +
               "nowe, nieśledzone pliki (??) przeczytaj w całości. Pomiń katalogi bin, obj, tools."
}
if ($Uwagi) { $zadanie += "`nKontekst od zespołu: $Uwagi" }

$prompt = $instrukcja + "`n`n## Zadanie`n" + $zadanie

$dir = Join-Path $env:LOCALAPPDATA 'BricsCadRc\recenzje'
New-Item -ItemType Directory -Force $dir | Out-Null
$stamp = "{0:yyyyMMdd-HHmmss}" -f (Get-Date)
$out = Join-Path $dir "recenzja-$stamp.md"
$log = Join-Path $dir "recenzja-$stamp.log"

$codexArgs = @('exec', '-s', 'read-only', '-C', $repo, '--ephemeral', '--color', 'never', '-o', $out)
$codexArgs += @('-m', $Model, '-c', "model_reasoning_effort=$Rozumowanie")
$codexArgs += '-'

Write-Host "[Recenzja GPT] Start ($(if ($Plan) { 'plan' } else { "diff wzgledem $Baza" }))..."
# Przebieg pracy Codexa (komendy, odczyty plikow) idzie do logu; na ekran tylko wynik.
$ErrorActionPreference = 'Continue'
$prompt | & codex @codexArgs *> $log
$kod = $LASTEXITCODE
$ErrorActionPreference = 'Stop'
if ($kod -ne 0 -or -not (Test-Path $out)) {
    Get-Content $log -Tail 15
    throw "[Recenzja GPT] Codex zakonczyl sie bledem (kod $kod). Log: $log"
}

Write-Host "[Recenzja GPT] Wynik: $out"
Write-Host ""
[IO.File]::ReadAllText($out, $utf8)
