# =========================================================================
#  bootstrap-linux.ps1 - Orquestador para deploy de Visal en server Linux.
#  ------------------------------------------------------------------------
#  Corre desde TU maquina Windows. Te pide el password SSH UNA SOLA VEZ
#  (cuando copia la llave publica al server). Despues todo es key-auth.
#
#  Que hace en orden:
#    1) Valida que ssh.exe y scp.exe esten en el PATH.
#    2) Genera un par de llaves SSH dedicado para Visal si no existe ya
#       (no pisa tu llave SSH principal).
#    3) Copia la llave publica al server con ssh-copy-id manual.
#       AQUI se te pedira el password SSH; lo escribes TU en tu terminal,
#       no pasa por este script.
#    4) Verifica que el key-auth funcione (intento sin password).
#    5) Confirma que el server tenga docker y docker compose.
#    6) scp de los archivos del deploy al server.
#    7) Ejecuta deploy-en-linux.sh en el server con output streamed.
#
#  Uso:
#    cd C:\DesarrolloIA\Visal\deploy\docker-prod
#    .\bootstrap-linux.ps1 -RemoteHost 10.0.0.3 -RemoteUser root
#
#  Parametros opcionales:
#    -RemoteDir     /opt/visal       carpeta del deploy en el server
#    -KeyName       id_ed25519_visal nombre del par de llaves dedicado
#    -GhcrUser      usuario GitHub para GHCR si la imagen es privada
#    -GhcrToken     PAT con read:packages
#    -SkipRestore   no restaurar dump (solo deploy)
#    -Force         no preguntar confirmaciones
# =========================================================================

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RemoteHost,
    [Parameter(Mandatory)][string]$RemoteUser,
    [string]$RemoteDir = "/opt/visal",
    [string]$KeyName = "id_ed25519_visal",
    [string]$GhcrUser = "",
    [string]$GhcrToken = "",
    [switch]$SkipRestore,
    [switch]$Force
)

$ErrorActionPreference = "Stop"

# PowerShell 5.1 trata cualquier stderr de un native command (ssh.exe) como
# error fatal cuando ErrorActionPreference=Stop. ssh.exe escribe a stderr
# warnings inofensivos ("Permanently added to known hosts", "Connection closed",
# etc.) que NO son errores. Por eso encapsulamos las llamadas a ssh/scp en
# helpers que relajan temporalmente la politica y filtran esas lineas.

# ---------- helpers ------------------------------------------------------
function Step($msg) { Write-Host ""; Write-Host "==> $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "    OK    $msg" -ForegroundColor Green }
function Info($msg) { Write-Host "    info  $msg" -ForegroundColor Gray }
function Warn($msg) { Write-Host "    WARN  $msg" -ForegroundColor Yellow }
function Err($msg)  { Write-Host "    ERR   $msg" -ForegroundColor Red }

function Confirm-Or-Exit($message) {
    if ($Force) { return }
    Write-Host ""
    $resp = Read-Host "$message  [s/N]"
    if ($resp -notmatch '^[sSyY]') {
        Warn "Cancelado por el usuario."
        exit 1
    }
}

$here = $PSScriptRoot
if (-not $here) { $here = (Get-Location).Path }
$keyPath = Join-Path $HOME ".ssh\$KeyName"
$pubKeyPath = "$keyPath.pub"
$target = "$RemoteUser@$RemoteHost"

# Patron de stderr de ssh que se filtran (son warnings benignos, no errores).
$script:SshBenignWarnings = @(
    'Warning: Permanently added',
    'Connection to .* closed',
    'Authenticated to ',
    'No ED25519 host key is known'
)

function Test-IsBenignSshWarning {
    param([string]$Line)
    foreach ($pat in $script:SshBenignWarnings) {
        if ($Line -match $pat) { return $true }
    }
    return $false
}

# Helper para ssh con la llave dedicada. Captura stderr y separa warnings
# benignos de errores reales. Devuelve el stdout limpio.
function Invoke-SshKey {
    param(
        [Parameter(Mandatory)][string]$Command,
        [switch]$AllowFailure
    )
    $prevEA = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $combined = & ssh -i $keyPath -o StrictHostKeyChecking=accept-new -o BatchMode=yes $target $Command 2>&1
        $sshExit = $LASTEXITCODE
        # Separar stdout de warnings; los warnings reales se muestran en gris para depurar.
        $stdout = @()
        foreach ($line in $combined) {
            $text = "$line"
            if ($line -is [System.Management.Automation.ErrorRecord]) {
                if (-not (Test-IsBenignSshWarning $text)) {
                    Write-Host "      ssh: $text" -ForegroundColor DarkYellow
                }
            } else {
                $stdout += $text
            }
        }
        if ($sshExit -ne 0 -and -not $AllowFailure) {
            Err "ssh fallo (exit $sshExit) en: $Command"
            exit 1
        }
        return ($stdout -join "`n")
    } finally {
        $ErrorActionPreference = $prevEA
    }
}

function Invoke-Scp {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Dest)
    $prevEA = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $combined = & scp -i $keyPath -o StrictHostKeyChecking=accept-new -o BatchMode=yes $Source "${target}:${Dest}" 2>&1
        $rc = $LASTEXITCODE
        foreach ($line in $combined) {
            $text = "$line"
            if ($line -is [System.Management.Automation.ErrorRecord] -and -not (Test-IsBenignSshWarning $text)) {
                Write-Host "      scp: $text" -ForegroundColor DarkYellow
            }
        }
        if ($rc -ne 0) {
            Err "Fallo el scp de '$Source' a '${target}:${Dest}' (exit $rc)"
            exit 1
        }
    } finally {
        $ErrorActionPreference = $prevEA
    }
}

# ---------- 0) Banner ----------------------------------------------------
Write-Host ""
Write-Host "=========================================================================" -ForegroundColor Cyan
Write-Host " Visal IPS RT - Bootstrap deploy Linux" -ForegroundColor Cyan
Write-Host "=========================================================================" -ForegroundColor Cyan
Write-Host " Server  : $target"
Write-Host " Carpeta : $RemoteDir"
Write-Host " Llave   : $keyPath"
Write-Host ""

# ---------- 1) Validar ssh.exe ------------------------------------------
Step "Validando ssh.exe y scp.exe en este Windows"
foreach ($exe in @("ssh.exe","scp.exe","ssh-keygen.exe")) {
    if (-not (Get-Command $exe -ErrorAction SilentlyContinue)) {
        Err "$exe no esta en el PATH. Instala OpenSSH client desde Settings > Apps > Optional features."
        exit 1
    }
}
Ok "ssh.exe / scp.exe / ssh-keygen.exe disponibles"

# ---------- 2) Generar llave dedicada si no existe -----------------------
Step "Llave SSH dedicada para Visal"
if (Test-Path $keyPath) {
    Ok "Ya existe: $keyPath (la reutilizo)"
} else {
    Info "No existe. La genero ahora (sin passphrase para que la automatizacion sea limpia)."
    New-Item -ItemType Directory -Force -Path (Split-Path $keyPath) | Out-Null
    & ssh-keygen -t ed25519 -f $keyPath -N '""' -C "visal-deploy@$env:USERNAME" | Out-Null
    if (-not (Test-Path $keyPath)) {
        Err "Fallo la generacion de la llave."
        exit 1
    }
    Ok "Llave generada: $keyPath"
    Warn "Guarda esta llave como secreto. La carpeta ~\.ssh\ ya tiene permisos restrictivos en Windows."
}

# ---------- 3) Copiar llave publica al server (UNICA vez que pide pass) -
Step "Copiando llave publica al server"
Info "AQUI se te pedira el password SSH del usuario $RemoteUser en $RemoteHost."
Info "Escribelo en este mismo terminal cuando ssh lo pida. Sera la unica vez."
Write-Host ""

# Verificar primero si ya tenemos key-auth funcionando (para no preguntar al user).
# Usamos Invoke-SshKey con -AllowFailure: si no funciona, no abortamos, solo seguimos al copy.
$preCheck = Invoke-SshKey -Command "echo OK" -AllowFailure
if ($preCheck.Trim() -eq "OK") {
    Ok "Key-auth ya funciona, no necesito copiar nada."
} else {
    $pubKey = (Get-Content $pubKeyPath -Raw).Trim()

    # Comando que crea ~/.ssh con permisos y anade nuestra clave (idempotente: no duplica)
    $remoteCmd = "mkdir -p ~/.ssh && chmod 700 ~/.ssh && touch ~/.ssh/authorized_keys && chmod 600 ~/.ssh/authorized_keys && grep -qxF '$pubKey' ~/.ssh/authorized_keys || echo '$pubKey' >> ~/.ssh/authorized_keys"

    # Llamada SIN BatchMode para que pueda preguntar password.
    # StrictHostKeyChecking=accept-new auto-acepta el host key la primera vez.
    # Relajamos EA preference para que el warning de "added to known hosts" no aborte.
    $prevEA = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & ssh -o StrictHostKeyChecking=accept-new $target $remoteCmd
        $copyExit = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prevEA
    }
    if ($copyExit -ne 0) {
        Err "Fallo la copia de la llave publica. Verifica password y conectividad."
        exit 1
    }

    # Confirmar que ahora key-auth si funciona
    $confirm = Invoke-SshKey -Command "echo OK" -AllowFailure
    if ($confirm.Trim() -ne "OK") {
        Err "Llave copiada pero key-auth no funciona. Algo raro paso."
        exit 1
    }
    Ok "Key-auth funcionando. A partir de aqui no se pedira password."
}

# ---------- 4) Validar prereqs en el server -----------------------------
Step "Validando prereqs en el server remoto"

$check = Invoke-SshKey -Command "command -v docker >/dev/null && docker version --format '{{.Server.Version}}' && docker compose version --short && echo SEP && whoami && echo SEP && uname -a"
if ($LASTEXITCODE -ne 0) {
    Err "Algun prereq falla en el server."
    Err "Salida: $check"
    exit 1
}
Info "Server responde:"
$check -split "`n" | ForEach-Object { Write-Host "      $_" -ForegroundColor DarkGray }
Ok "Docker y docker compose disponibles"

# ---------- 5) Crear carpeta de deploy y subcarpeta dumps ---------------
Step "Preparando carpeta $RemoteDir en el server"
Invoke-SshKey -Command "mkdir -p $RemoteDir/dumps && chmod 755 $RemoteDir" | Out-Null
Ok "Carpeta lista"

# ---------- 6) Subir archivos -------------------------------------------
Step "Subiendo archivos del deploy"

$archivos = @(
    "docker-compose.yml",
    ".env.example",
    "deploy-en-linux.sh"
)

foreach ($f in $archivos) {
    $src = Join-Path $here $f
    if (-not (Test-Path $src)) {
        Err "No encuentro '$src' en local. Ejecuta desde la carpeta deploy/docker-prod/"
        exit 1
    }
    Info "subiendo $f"
    Invoke-Scp -Source $src -Dest "$RemoteDir/$f"
}

# Detectar el dump mas reciente
$dumps = Get-ChildItem (Join-Path $here "dumps") -Filter "*.dump" -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending
if (-not $dumps) {
    if (-not $SkipRestore) {
        Err "No hay archivos .dump en .\dumps\ y no pasaste -SkipRestore."
        Err "Genera el dump con copia_base_datos.ps1 o usa -SkipRestore."
        exit 1
    }
    Warn "Sin dump local. -SkipRestore activo: deploy sin datos."
} else {
    $dumpSrc = $dumps[0].FullName
    $dumpName = $dumps[0].Name
    $sizeMB = [math]::Round($dumps[0].Length / 1MB, 3)
    Info "subiendo dump $dumpName ($sizeMB MB)"
    Invoke-Scp -Source $dumpSrc -Dest "$RemoteDir/dumps/visal_dev.dump"
}

# Asegurar que el .sh tenga LF (no CRLF) y sea ejecutable
Invoke-SshKey -Command "sed -i 's/\r$//' $RemoteDir/deploy-en-linux.sh && chmod +x $RemoteDir/deploy-en-linux.sh" | Out-Null
Ok "Archivos subidos y script marcado ejecutable"

# ---------- 7) Confirmar antes de ejecutar -------------------------------
Write-Host ""
Write-Host "Voy a ejecutar deploy-en-linux.sh en el server. Esto:" -ForegroundColor Yellow
Write-Host "  - Generara .env con password Postgres aleatoria (si no existe)"
Write-Host "  - docker compose pull + up -d"
if (-not $SkipRestore -and $dumps) {
    Write-Host "  - Restaurara el dump (si hay tablas, te pedira confirmacion en el server)"
}
Write-Host "  - Reiniciara visal-app"
Write-Host ""
Confirm-Or-Exit "Continuo?"

# ---------- 8) Ejecutar deploy remoto -----------------------------------
Step "Ejecutando deploy-en-linux.sh en el server"

$flagsRemote = @()
if ($GhcrUser -and $GhcrToken) {
    $flagsRemote += "--ghcr-user '$GhcrUser' --ghcr-token '$GhcrToken'"
}
if ($SkipRestore) { $flagsRemote += "--skip-restore" }
if ($Force)       { $flagsRemote += "--force" }
$flagsStr = ($flagsRemote -join " ")

# Sin BatchMode aqui: si el script pide confirmacion del usuario (BD ya tiene datos),
# pueden contestar desde este terminal. Relajamos EA preference para que stderr de
# ssh.exe (que es normal cuando el script remoto escribe diagnosticos) no aborte.
$prevEA = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    & ssh -i $keyPath -o StrictHostKeyChecking=accept-new -t $target "cd $RemoteDir && ./deploy-en-linux.sh $flagsStr"
    $deployExit = $LASTEXITCODE
} finally {
    $ErrorActionPreference = $prevEA
}

# ---------- 9) Resumen ---------------------------------------------------
Write-Host ""
Write-Host "=========================================================================" -ForegroundColor Cyan
if ($deployExit -eq 0) {
    Write-Host " Bootstrap terminado OK" -ForegroundColor Green
} else {
    Write-Host " Bootstrap termino con errores (exit $deployExit)" -ForegroundColor Red
}
Write-Host "=========================================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host " Para volver a entrar al server sin password:" -ForegroundColor Gray
Write-Host "   ssh -i $keyPath $target" -ForegroundColor Gray
Write-Host ""
Write-Host " Si tu admin del server ya engancho el reverse proxy, abre:" -ForegroundColor Gray
Write-Host "   https://<tu-dominio>" -ForegroundColor Gray
Write-Host ""
Write-Host " Si todavia no, prueba con tunel SSH:" -ForegroundColor Gray
Write-Host "   ssh -i $keyPath -L 5380:localhost:5380 $target" -ForegroundColor Gray
Write-Host "   (y abre http://localhost:5380 en tu navegador)" -ForegroundColor Gray
Write-Host ""

if ($deployExit -ne 0) { exit $deployExit }
