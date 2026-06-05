# run-visal.ps1 — compilar y correr Visal en local
#
# Uso:
#   .\run-visal.ps1            -> build + run en http://localhost:5080
#   .\run-visal.ps1 -Build     -> solo compilar
#   .\run-visal.ps1 -Test      -> compilar + correr todos los tests
#   .\run-visal.ps1 -Clean     -> borrar bin/obj y recompilar desde cero
#   .\run-visal.ps1 -Port 5090 -> correr en otro puerto
#
# Requisitos previos:
#   - .NET 9 SDK instalado (dotnet --version >= 9)
#   - Docker stack levantado:  cd deploy\docker ; docker compose up -d
#
# Variables: VISAL_DB_CONNECTION apunta al postgres local del stack (puerto 5435).

[CmdletBinding()]
param(
    [switch]$Build,
    [switch]$Test,
    [switch]$Clean,
    [int]$Port = 5080,
    [string]$Environment = "Development"
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot
$backend = Join-Path $repo "apps\backend"
$startup = Join-Path $backend "src\Visal.SuperAdmin"

# Conexion a Postgres del stack docker local (puertos del CLAUDE.md).
$env:VISAL_DB_CONNECTION = "Host=localhost;Port=5435;Database=visal_dev;Username=visal;Password=visal_local_2026"
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$env:ASPNETCORE_ENVIRONMENT = $Environment

function Step($msg) { Write-Host ""; Write-Host "==> $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "    $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "    $msg" -ForegroundColor Yellow }

# 1) Limpiar bin/obj si -Clean
if ($Clean) {
    Step "Limpiando bin/obj de todos los proyectos"
    Get-ChildItem -Path $backend -Include bin, obj -Recurse -Directory -ErrorAction SilentlyContinue |
        Remove-Item -Recurse -Force
    Ok "Listo."
}

# 2) Matar instancias previas de DEV solamente (las que corren desde bin\Debug).
# La instancia ESTABLE (corre desde stable-bin\) NO la tocamos para no cortarle
# la sesion al usuario.
Step "Cerrando instancias previas de DEV de Visal.SuperAdmin"
$devBin = (Join-Path $startup "bin").ToLowerInvariant()
$stoppedCount = 0
foreach ($p in (Get-Process -Name "Visal.SuperAdmin" -ErrorAction SilentlyContinue)) {
    try {
        $path = $p.MainModule.FileName
        if ($path -and $path.ToLowerInvariant().StartsWith($devBin)) {
            Stop-Process -Id $p.Id -Force
            $stoppedCount++
        }
    } catch { }
}
if ($stoppedCount -gt 0) {
    Start-Sleep -Milliseconds 800
    Ok "Procesos dev detenidos: $stoppedCount (estable intacto)."
} else {
    Ok "No habia dev previo."
}

# 3) Verificar que el puerto este libre
$enUso = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue
if ($enUso) {
    Warn "Puerto $Port ocupado por PID $($enUso[0].OwningProcess). Intentando liberar..."
    Stop-Process -Id $enUso[0].OwningProcess -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

# 4) Build
Step "Compilando Visal.SuperAdmin (puerto $Port, env $Environment)"
dotnet build "$startup\Visal.SuperAdmin.csproj" -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Build fallo." }
Ok "Build verde."

if ($Build) { return }

# 5) Tests (opcional)
if ($Test) {
    Step "Corriendo todos los tests"
    Push-Location $backend
    try {
        dotnet test --no-restore -nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "Tests fallaron." }
        Ok "Todos los tests verdes."
    } finally { Pop-Location }
    return
}

# 6) Run
Step "Arrancando Visal en http://localhost:$Port  (Ctrl+C para detener)"
Write-Host "    Stack Docker (Postgres 5435, Redis 6382, Rabbit 5674) debe estar arriba."
Write-Host "    Si esta caido:  cd deploy\docker ; docker compose up -d" -ForegroundColor DarkGray
Write-Host ""

Push-Location $startup
try {
    dotnet run --no-build --no-launch-profile
} finally {
    Pop-Location
}
