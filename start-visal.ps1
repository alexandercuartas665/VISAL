# =========================================================================
#  start-visal.ps1 - arranca Visal detached y abre el navegador.
#
#  A diferencia de run-visal.ps1 (que se queda atado a la consola), este
#  arranca dotnet como proceso oculto en background, redirige stdout/stderr
#  a un archivo de log, espera hasta que http://localhost:5080/login
#  responda 200, y abre el navegador. Despues TERMINA la ventana
#  PowerShell. Cerrar la consola NO mata Visal.
#
#  Uso (mas comodo): doble click en Start-Visal.cmd.
#  Tambien sirve:    powershell -ExecutionPolicy Bypass -File start-visal.ps1
#
#  Para detener Visal:  .\stop-visal.ps1   (o cerrar el proceso dotnet)
# =========================================================================

[CmdletBinding()]
param(
    [int]$Port = 5080,
    [string]$Environment = "Development",
    [switch]$NoBuild,
    [switch]$NoBrowser
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot
$backend = Join-Path $repo "apps\backend"
$startup = Join-Path $backend "src\Visal.SuperAdmin"
$logDir = Join-Path $repo "logs"
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$logOut = Join-Path $logDir "visal-$stamp.out.log"
$logErr = Join-Path $logDir "visal-$stamp.err.log"
$pidFile = Join-Path $repo ".visal.pid"

function Info($m) { Write-Host "    $m" -ForegroundColor Green }
function Warn($m) { Write-Host "    $m" -ForegroundColor Yellow }
function Step($m) { Write-Host ""; Write-Host "==> $m" -ForegroundColor Cyan }

# 1) Matar instancia previa si esta corriendo
Step "Cerrando instancias previas (si quedaron colgadas)"
$busy = Get-Process -Name "Visal.SuperAdmin" -ErrorAction SilentlyContinue
if ($busy) {
    $busy | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
    Info "Cerrados $($busy.Count) proceso(s)."
} else {
    Info "No habia procesos previos."
}

# Tambien matar lo que tenga el puerto ocupado
$inUse = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue
if ($inUse) {
    Warn "Puerto $Port ocupado por PID $($inUse[0].OwningProcess), liberando..."
    Stop-Process -Id $inUse[0].OwningProcess -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

# 2) Build (a menos que pidan -NoBuild)
if (-not $NoBuild) {
    Step "Compilando Visal.SuperAdmin"
    Push-Location $startup
    try {
        & dotnet build "Visal.SuperAdmin.csproj" -nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "Build fallo. Aborta." }
        Info "Build verde."
    } finally { Pop-Location }
} else {
    Step "Saltando build (--NoBuild)"
}

# 3) Configurar entorno y lanzar dotnet detached
Step "Arrancando Visal detached en http://localhost:$Port (env $Environment)"
[System.Environment]::SetEnvironmentVariable(
    "VISAL_DB_CONNECTION",
    "Host=localhost;Port=5435;Database=visal_dev;Username=visal;Password=visal_local_2026")
[System.Environment]::SetEnvironmentVariable("ASPNETCORE_URLS", "http://localhost:$Port")
[System.Environment]::SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", $Environment)

$p = Start-Process -FilePath "dotnet" `
    -ArgumentList @("run","--no-build","--no-launch-profile") `
    -WorkingDirectory $startup `
    -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput $logOut -RedirectStandardError $logErr

$p.Id | Out-File -FilePath $pidFile -Encoding ascii -Force
Info "PID $($p.Id) (guardado en $pidFile)"
Info "Log stdout: $logOut"
Info "Log stderr: $logErr"

# 4) Esperar hasta que el endpoint responda 200
Step "Esperando a que Visal responda en /login (max 90s)"
$ready = $false
for ($i = 0; $i -lt 45; $i++) {
    Start-Sleep -Seconds 2
    try {
        $r = Invoke-WebRequest -Uri "http://localhost:$Port/login" -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
        if ($r.StatusCode -eq 200) { $ready = $true; break }
    } catch { }
    if ($p.HasExited) {
        Warn "El proceso dotnet termino antes de subir. Revisa $logErr"
        break
    }
}

if ($ready) {
    Info "Visal listo en http://localhost:$Port"
    if (-not $NoBrowser) {
        Start-Process "http://localhost:$Port/login"
        Info "Navegador abierto."
    }
    Write-Host ""
    Write-Host "Ya puedes cerrar esta ventana - Visal sigue corriendo en background." -ForegroundColor Cyan
    Write-Host "Para detenerlo:  .\stop-visal.ps1" -ForegroundColor DarkGray
    Start-Sleep -Seconds 4
} else {
    Warn "Visal no respondio en 90s. Revisa:"
    Warn "  $logOut"
    Warn "  $logErr"
    Write-Host ""
    Write-Host "Presiona Enter para cerrar..." -ForegroundColor Yellow
    [void][System.Console]::ReadLine()
}
