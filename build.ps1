param(
    [switch]$SkipTests,
    [switch]$Run,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

Write-Host "== Compilando ($Configuration) ==" -ForegroundColor Cyan
dotnet build -c $Configuration -v minimal
if ($LASTEXITCODE -ne 0) { throw "La compilación falló." }

if (-not $SkipTests) {
    Write-Host "== Pruebas internas ==" -ForegroundColor Cyan
    dotnet run -c $Configuration --no-build -- --selftest
    if ($LASTEXITCODE -ne 0) { throw "El selftest falló. Revisa selftest.txt." }
}

Write-Host "== Publicando ejecutable autocontenido ==" -ForegroundColor Cyan

# === Regla /dis: conservar los JSON de configuración del usuario ===
# build.ps1 borra dist/ para publicar limpio, pero NO debe perder los JSON de
# configuración (estadisticas.json, trainer-config.json) salvo que exista una
# versión nueva de esos archivos que reemplazarlos. Respaldo lo que haya en
# dist/ antes de borrar y lo restauro después de publicar.
$configJsons = @("estadisticas.json", "trainer-config.json", "coleccionables.json")
$backupDir = Join-Path $env:TEMP ("phykro-dist-backup-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
$preserved = @()
foreach ($cfg in $configJsons) {
    $src = Join-Path dist $cfg
    if (Test-Path $src) {
        Copy-Item $src (Join-Path $backupDir $cfg) -Force
        $preserved += $cfg
        Write-Host ("  Preservando " + $cfg + " (sin versión nueva que lo reemplace).") -ForegroundColor Yellow
    }
}

# === maps/ NO se copia a dist/: la extracción (GameMapExtractor) genera
# maps\map-calibration.json + maps\*.png en el primer arranque del usuario. ===
if (Test-Path dist) { Remove-Item dist -Recurse -Force }
dotnet publish -c $Configuration -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o dist
if ($LASTEXITCODE -ne 0) { throw "La publicación falló." }

Copy-Item README.md (Join-Path dist "LEEME.md") -Force

# Restaurar los JSON preexistentes. Si tras publicar existe YA uno en dist/,
# se considera que hay una versión nueva y no se sobreescribe.
foreach ($cfg in $preserved) {
    $dst = Join-Path dist $cfg
    if (-not (Test-Path $dst)) {
        Copy-Item (Join-Path $backupDir $cfg) $dst -Force
        Write-Host ("  Restaurado " + $cfg + " en dist/.") -ForegroundColor Yellow
    }
}
Remove-Item $backupDir -Recurse -Force -ErrorAction SilentlyContinue

$exe = Join-Path dist "Phykro-Trainer.exe"
if (-not (Test-Path $exe)) { throw "No se generó $exe" }

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Listo: $exe ($size MB)" -ForegroundColor Green

if ($Run) { Start-Process $exe }
