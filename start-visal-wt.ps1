# =========================================================================
#  start-visal-wt.ps1 - arranca el dev de ESTE worktree detached.
#
#  Diferencias con start-visal.ps1 (que es para el checkout principal):
#   - Puerto por defecto 5085 (no 5080): asi convive con el dev de main.
#   - NO mata procesos por nombre (Visal.SuperAdmin). Solo libera SU propio
#     puerto, y solo matando el PID exacto que lo ocupa. Respeta la regla
#     de no tumbar otras instancias dotnet / otros worktrees.
#   - Usa el build de ESTE worktree (bin propio), asi no choca el lock del
#     DLL con el dev de main.
#   - Lanzado detached via WMI (Win32_Process.Create): sobrevive al cierre
#     del shell del agente y al fin del turno.
#
#  Misma base de datos de dev que main (visal_dev en localhost:5435).
#
#  Uso:    powershell -ExecutionPolicy Bypass -File start-visal-wt.ps1
#  Parar:  powershell -ExecutionPolicy Bypass -File start-visal-wt.ps1 -Stop
# =========================================================================

[CmdletBinding()]
param(
    [int]$Port = 5085,
    [string]$Environment = "Development",
    [switch]$NoBuild,
    [switch]$Stop
)

$ErrorActionPreference = "Stop"
$repo    = $PSScriptRoot
$startup = Join-Path $repo "apps\backend\src\Visal.SuperAdmin"
$exe     = Join-Path $startup "bin\Debug\net9.0\Visal.SuperAdmin.exe"
$logDir  = Join-Path $repo "logs"
$pidFile = Join-Path $repo ".visal-wt.pid"
$dbConn  = "Host=localhost;Port=5435;Database=visal_dev;Username=visal;Password=visal_local_2026"

function Info($m) { Write-Host "    $m" -ForegroundColor Green }
function Warn($m) { Write-Host "    $m" -ForegroundColor Yellow }
function Step($m) { Write-Host ""; Write-Host "==> $m" -ForegroundColor Cyan }

# ---- Parar: solo mata el PID guardado (nunca por nombre) ----------------
if ($Stop) {
    Step "Deteniendo dev del worktree"
    if (Test-Path $pidFile) {
        $savedPid = (Get-Content $pidFile | Select-Object -First 1).Trim()
        $proc = Get-Process -Id $savedPid -ErrorAction SilentlyContinue
        if ($proc -and $proc.ProcessName -like "*Visal.SuperAdmin*") {
            Stop-Process -Id $savedPid -Force
            Info "Detenido PID $savedPid."
        } else {
            Warn "El PID $savedPid ya no corre o no es Visal.SuperAdmin; no se toca nada."
        }
        Remove-Item $pidFile -ErrorAction SilentlyContinue
    } else {
        Warn "No hay $pidFile; nada que detener."
    }
    return
}

if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
$stamp  = Get-Date -Format "yyyyMMdd-HHmmss"
$logOut = Join-Path $logDir "visal-wt-$stamp.out.log"

# ---- Liberar SOLO el propio puerto (por PID exacto) ---------------------
$inUse = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
if ($inUse) {
    $owner = Get-Process -Id $inUse[0].OwningProcess -ErrorAction SilentlyContinue
    if ($owner -and $owner.ProcessName -like "*Visal.SuperAdmin*") {
        Warn "Puerto $Port ocupado por un Visal.SuperAdmin previo (PID $($owner.Id)); liberando."
        Stop-Process -Id $owner.Id -Force -ErrorAction SilentlyContinue
        Start-Sleep -Milliseconds 600
    } else {
        throw "El puerto $Port lo ocupa un proceso ajeno (PID $($inUse[0].OwningProcess)). Elige otro puerto con -Port; no mato procesos que no sean mios."
    }
}

# ---- Build del worktree (bin propio) ------------------------------------
if (-not $NoBuild) {
    Step "Compilando Visal.SuperAdmin (worktree)"
    Push-Location $startup
    try {
        & dotnet build "Visal.SuperAdmin.csproj" -nologo -v q
        if ($LASTEXITCODE -ne 0) { throw "Build fallo. Aborta." }
        Info "Build verde."
    } finally { Pop-Location }
} else {
    Step "Saltando build (-NoBuild)"
}

# ---- Lanzar detached via WMI (sobrevive al shell) -----------------------
Step "Arrancando dev del worktree en http://localhost:$Port (env $Environment)"

# Comando in-process: setea env y corre el server. Se codifica en base64
# (-EncodedCommand) para pasarlo intacto a traves de WMI.
# Corre el EXE ya compilado directamente (no "dotnet run"): el launcher-host de
# "dotnet run" cuelga de forma intermitente al arrancar detached via WMI (log vacio,
# sin escuchar); el exe arranca en segundos y es predecible.
$inner = @"
`$env:VISAL_DB_CONNECTION = '$dbConn'
`$env:ASPNETCORE_URLS = 'http://localhost:$Port'
`$env:ASPNETCORE_ENVIRONMENT = '$Environment'
Set-Location '$startup'
& '$exe' *> '$logOut'
"@
$encoded = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($inner))
$cmdLine = "powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encoded"

$result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $cmdLine }
if ($result.ReturnValue -ne 0) { throw "WMI Win32_Process.Create fallo (ReturnValue=$($result.ReturnValue))." }

# El PID que devuelve WMI es el del powershell host; el dotnet real es hijo.
# Esperamos a que aparezca el Visal.SuperAdmin escuchando y guardamos SU pid.
Info "Host PowerShell PID $($result.ProcessId). Log: $logOut"

# ---- Esperar a que /login responda 200 ----------------------------------
Step "Esperando a que el dev responda en /login (max 120s)"
$ready = $false
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep -Seconds 2
    try {
        $r = Invoke-WebRequest -Uri "http://localhost:$Port/login" -UseBasicParsing -TimeoutSec 3 -ErrorAction Stop
        if ($r.StatusCode -eq 200) { $ready = $true; break }
    } catch { }
}

if ($ready) {
    $srv = Get-Process -Name "Visal.SuperAdmin" -ErrorAction SilentlyContinue |
           Sort-Object StartTime -Descending | Select-Object -First 1
    if ($srv) { $srv.Id | Out-File -FilePath $pidFile -Encoding ascii -Force }
    Info "Dev del worktree listo en http://localhost:$Port (PID $($srv.Id))."
    Info "Para detenerlo: start-visal-wt.ps1 -Stop"
} else {
    Warn "No respondio en 120s. Revisa el log: $logOut"
}
