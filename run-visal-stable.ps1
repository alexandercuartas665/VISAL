# run-visal-stable.ps1
# Arranca la instancia ESTABLE de Visal en :5081 contra el mismo Postgres
# que usa dev (:5080). Esta instancia NO se recompila ni reinicia cuando
# trabajo cambios en codigo, asi que tu sesion no se corta.
#
# Uso:
#   .\run-visal-stable.ps1                  # arranca en :5081
#   .\run-visal-stable.ps1 -Port 5082       # otro puerto
#   .\run-visal-stable.ps1 -Republish       # rehace el publish desde fuentes actuales
#                                            # (usar cuando quieras consolidar una
#                                            # nueva version estable; ojo: corta la
#                                            # sesion mientras se republica)
#   .\run-visal-stable.ps1 -Stop            # baja el estable
#
# Requisitos:
#   - Stack Docker arriba (Postgres puerto 5435)
#   - Ya existe C:\DesarrolloIA\Visal\stable-bin\ con un publish previo

[CmdletBinding()]
param(
    [int]$Port = 5081,
    [string]$Environment = "Production",
    [switch]$Republish,
    [switch]$Stop
)

$ErrorActionPreference = "Stop"
$repo = $PSScriptRoot
$backend = Join-Path $repo "apps\backend"
$stableDir = Join-Path $repo "stable-bin"
$exe = Join-Path $stableDir "Visal.SuperAdmin.exe"

function Step($m) { Write-Host ""; Write-Host "==> $m" -ForegroundColor Cyan }
function Ok($m)   { Write-Host "    $m" -ForegroundColor Green }
function Warn($m) { Write-Host "    $m" -ForegroundColor Yellow }

# Conexion Postgres compartida con dev.
$dbConn = "Host=localhost;Port=5435;Database=visal_dev;Username=visal;Password=visal_local_2026"

# 1) -Stop: matar instancia previa y salir
if ($Stop) {
    Step "Deteniendo instancias estables previas (PID con working dir = stable-bin)"
    $stoppedAny = $false
    $procs = Get-Process -Name "Visal.SuperAdmin" -ErrorAction SilentlyContinue
    foreach ($p in $procs) {
        try {
            $mainModule = $p.MainModule.FileName
            if ($mainModule -and ($mainModule -like "$stableDir*")) {
                Stop-Process -Id $p.Id -Force
                Ok "Detenida instancia estable PID $($p.Id)"
                $stoppedAny = $true
            }
        } catch { }
    }
    if (-not $stoppedAny) { Ok "No habia instancia estable corriendo." }
    return
}

# 2) -Republish: regenerar carpeta stable-bin desde fuentes actuales
if ($Republish) {
    Step "Republicando binario estable desde fuentes actuales"
    # Detener instancia previa para no chocar con archivos en uso.
    Get-Process -Name "Visal.SuperAdmin" -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $mainModule = $_.MainModule.FileName
            if ($mainModule -and ($mainModule -like "$stableDir*")) {
                Stop-Process -Id $_.Id -Force
                Ok "Detenida instancia estable PID $($_.Id) para republish"
                Start-Sleep -Milliseconds 600
            }
        } catch { }
    }
    & dotnet publish (Join-Path $backend "src\Visal.SuperAdmin\Visal.SuperAdmin.csproj") `
        -c Release -o $stableDir --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Publish fallo." }
    Ok "Publish completado en $stableDir"
}

# 3) Verificar que existe el exe
if (-not (Test-Path $exe)) {
    Warn "No existe $exe. Corre '.\run-visal-stable.ps1 -Republish' primero."
    return
}

# 4) Verificar que no haya OTRA instancia estable ya escuchando :5081
$enUso = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue
if ($enUso) {
    Warn "Puerto $Port ya ocupado por PID $($enUso[0].OwningProcess). Saliendo."
    return
}

# 5) Arrancar la instancia estable (en una nueva ventana de PowerShell para que
#    no se cuelgue de esta sesion).
Step "Arrancando Visal ESTABLE en http://localhost:$Port"
$env:VISAL_DB_CONNECTION = $dbConn
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$env:ASPNETCORE_ENVIRONMENT = $Environment

$cmd = "`$env:VISAL_DB_CONNECTION='$dbConn'; `$env:ASPNETCORE_URLS='http://localhost:$Port'; `$env:ASPNETCORE_ENVIRONMENT='$Environment'; & '$exe'"
Start-Process -FilePath "pwsh" -ArgumentList "-NoExit","-Command", $cmd `
    -WorkingDirectory $stableDir -WindowStyle Minimized

Start-Sleep -Seconds 4
# Verificacion de arranque
try {
    $code = (Invoke-WebRequest -Uri "http://localhost:$Port/login" -UseBasicParsing -TimeoutSec 5).StatusCode
    Ok "Visal estable respondiendo (HTTP $code) en http://localhost:$Port"
    Write-Host ""
    Write-Host "  Dev:     http://localhost:5080  (puede recompilarse libre)" -ForegroundColor DarkGray
    Write-Host "  Estable: http://localhost:$Port  (no se reinicia)" -ForegroundColor DarkGray
} catch {
    Warn "No respondio aun. Esperar unos segundos mas y probar http://localhost:$Port"
}
