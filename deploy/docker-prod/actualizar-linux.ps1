# =========================================================================
#  actualizar-linux.ps1
#  ------------------------------------------------------------------------
#  Actualiza Visal en el server Linux de produccion desde TU maquina.
#  Asume que ya hiciste bootstrap-linux.ps1 una vez (la llave SSH ya esta).
#
#  Que hace:
#    1) Sube el actualizar-en-linux.sh mas reciente al server.
#    2) Lo ejecuta. El script remoto:
#        - Hace backup defensivo de la BD.
#        - docker compose pull.
#        - docker compose up -d.
#        - Verifica migrations en logs.
#        - Probe HTTP.
#
#  Uso:
#    cd C:\DesarrolloIA\Visal\deploy\docker-prod
#    .\actualizar-linux.ps1 -RemoteHost 10.0.0.3 -RemoteUser root
#
#  Flags:
#    -RemoteDir    /opt/visal       carpeta del deploy
#    -KeyName      id_ed25519_visal llave SSH dedicada
#    -SkipBackup                    omitir backup previo (mas rapido)
#    -Tag <sha>                     pinear imagen a un tag especifico
#    -GhcrUser X                    si la imagen sigue privada
#    -GhcrToken X
# =========================================================================

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RemoteHost,
    [Parameter(Mandatory)][string]$RemoteUser,
    [string]$RemoteDir = "/opt/visal",
    [string]$KeyName = "id_ed25519_visal",
    [switch]$SkipBackup,
    [string]$Tag = "",
    [string]$GhcrUser = "",
    [string]$GhcrToken = ""
)

$ErrorActionPreference = "Stop"

# PowerShell 5.1 trata cualquier stderr de un native command (ssh.exe) como
# error fatal cuando ErrorActionPreference=Stop. Encapsulamos las llamadas
# para evitar que warnings benignos aborten el script.

function Step($msg) { Write-Host ""; Write-Host "==> $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "    OK    $msg" -ForegroundColor Green }
function Info($msg) { Write-Host "    info  $msg" -ForegroundColor Gray }
function Warn($msg) { Write-Host "    WARN  $msg" -ForegroundColor Yellow }
function Err($msg)  { Write-Host "    ERR   $msg" -ForegroundColor Red }

$here = $PSScriptRoot
if (-not $here) { $here = (Get-Location).Path }
$keyPath = Join-Path $HOME ".ssh\$KeyName"
$target = "$RemoteUser@$RemoteHost"

# Validar prereqs locales
if (-not (Test-Path $keyPath)) {
    Err "No existe la llave SSH '$keyPath'."
    Err "Corre primero bootstrap-linux.ps1 para configurarla."
    exit 1
}

$scriptLocal = Join-Path $here "actualizar-en-linux.sh"
if (-not (Test-Path $scriptLocal)) {
    Err "No encuentro actualizar-en-linux.sh en $here"
    exit 1
}

# Banner
Write-Host ""
Write-Host "=========================================================================" -ForegroundColor Cyan
Write-Host " Visal IPS RT - Actualizar deploy Linux" -ForegroundColor Cyan
Write-Host "=========================================================================" -ForegroundColor Cyan
Write-Host " Server  : $target"
Write-Host " Carpeta : $RemoteDir"
Write-Host ""

# Subir actualizar-en-linux.sh (por si lo cambiaste localmente)
Step "Subiendo actualizar-en-linux.sh al server"
$prevEA = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    $scpOut = & scp -i $keyPath -o BatchMode=yes $scriptLocal "${target}:${RemoteDir}/actualizar-en-linux.sh" 2>&1
    $rc = $LASTEXITCODE
} finally {
    $ErrorActionPreference = $prevEA
}
if ($rc -ne 0) {
    Err "Fallo scp del script. Salida: $scpOut"
    exit 1
}
Ok "Script subido"

# Asegurar LF y permisos
$prevEA = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    & ssh -i $keyPath -o BatchMode=yes $target "sed -i 's/\r`$//' $RemoteDir/actualizar-en-linux.sh && chmod +x $RemoteDir/actualizar-en-linux.sh" 2>&1 | Out-Null
} finally {
    $ErrorActionPreference = $prevEA
}

# Armar flags para el script remoto
$flagsRemote = @()
if ($SkipBackup) { $flagsRemote += "--skip-backup" }
if ($Tag)        { $flagsRemote += "--tag '$Tag'" }
if ($GhcrUser -and $GhcrToken) {
    $flagsRemote += "--ghcr-user '$GhcrUser' --ghcr-token '$GhcrToken'"
}
$flagsStr = ($flagsRemote -join " ")

# Ejecutar con output en vivo
Step "Ejecutando actualizar-en-linux.sh en el server"
$prevEA = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    & ssh -i $keyPath -t $target "cd $RemoteDir && ./actualizar-en-linux.sh $flagsStr"
    $deployExit = $LASTEXITCODE
} finally {
    $ErrorActionPreference = $prevEA
}

Write-Host ""
Write-Host "=========================================================================" -ForegroundColor Cyan
if ($deployExit -eq 0) {
    Write-Host " Actualizacion OK" -ForegroundColor Green
} else {
    Write-Host " Actualizacion termino con errores (exit $deployExit)" -ForegroundColor Red
    Write-Host " Logs:  ssh -i $keyPath $target 'docker logs --tail 200 visal-app'" -ForegroundColor Yellow
}
Write-Host "=========================================================================" -ForegroundColor Cyan
Write-Host ""

if ($deployExit -ne 0) { exit $deployExit }
