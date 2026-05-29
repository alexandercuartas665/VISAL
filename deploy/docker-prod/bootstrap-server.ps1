# =========================================================================
#  bootstrap-server.ps1 — setup + deploy + verify EN UN SOLO COMANDO
#  para un servidor Windows con Docker en el que aun no esta Visal.
#
#  Hace todo: prueba SSH, valida Docker, busca puerto libre, genera clave
#  segura, crea carpeta de deploy, escribe el .env y docker-compose.yml en
#  el server, hace login GHCR (opcional), pull + up, y verifica que arranco.
#
#  Uso (una sola vez, despues usas deploy-remote.ps1 para mantenimiento):
#
#    cd C:\DesarrolloIA\Visal\deploy\docker-prod
#    .\bootstrap-server.ps1 -RemoteSsh bit-admin@10.0.1.6 -Password 'TU_PASS'
#
#  Parametros opcionales:
#    -PreferredPort 5380     puerto de inicio para buscar uno libre
#    -DeployDir C:\visal     carpeta en el server para los archivos
#    -GhcrUser ...           si la imagen GHCR es privada, login con PAT
#    -GhcrToken ...
#
#  Requisitos en TU maquina:
#    - PuTTY (plink + pscp) instalado: winget install --id PuTTY.PuTTY
#
#  Requisitos en el SERVER:
#    - Windows con OpenSSH server habilitado
#    - Docker Desktop (Linux containers / WSL2) corriendo
#    - El usuario bit-admin con permisos para ejecutar docker
# =========================================================================

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RemoteSsh,
    [Parameter(Mandatory)][string]$Password,
    [int]$PreferredPort = 5380,
    [string]$DeployDir = "E:\DOCKER\visal",
    [string]$VisalImage = "ghcr.io/alexandercuartas665/visal/superadmin:latest",
    [string]$GhcrUser = "",
    [string]$GhcrToken = ""
)

$ErrorActionPreference = "Stop"

# --- localizar plink y pscp ---
$putty = "C:\Program Files\PuTTY"
if (Test-Path "$putty\plink.exe") { $env:Path += ";$putty" }
$plink = Get-Command plink -ErrorAction SilentlyContinue
$pscp  = Get-Command pscp  -ErrorAction SilentlyContinue
if (-not $plink -or -not $pscp) {
    Write-Host "Falta PuTTY (plink/pscp). Instala con:" -ForegroundColor Red
    Write-Host "    winget install --id PuTTY.PuTTY --silent --accept-source-agreements --accept-package-agreements" -ForegroundColor Yellow
    exit 1
}

function Step($msg) { Write-Host ""; Write-Host "==> $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "    $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "    $msg" -ForegroundColor Yellow }
function Err($msg)  { Write-Host "    $msg" -ForegroundColor Red }

function Invoke-Remote {
    param([Parameter(Mandatory)][string]$Cmd, [switch]$Quiet)
    # Ejecuta un comando en el server via plink. Si es la primera vez,
    # auto-acepta el host key (-batch lo rechaza, asi que sin -batch).
    # Para auto-aceptar: prefijo el cmd con echo y (stdin) -> "y\n"
    $tmpOut = New-TemporaryFile
    $tmpErr = New-TemporaryFile
    try {
        # plink lee la primera linea de stdin como respuesta al host key prompt.
        $stdin = "y`n"
        $stdin | & plink -ssh -pw $Password $RemoteSsh $Cmd 2>$tmpErr.FullName |
            Tee-Object -FilePath $tmpOut.FullName | Out-Null
        $exit = $LASTEXITCODE
        $out = (Get-Content $tmpOut.FullName -Raw -ErrorAction SilentlyContinue)
        $errOut = (Get-Content $tmpErr.FullName -Raw -ErrorAction SilentlyContinue)
        if (-not $Quiet -and $out) { Write-Host $out.TrimEnd() }
        if ($exit -ne 0) {
            Err "Exit $exit ejecutando: $Cmd"
            if ($errOut) { Write-Host $errOut -ForegroundColor DarkRed }
        }
        return [pscustomobject]@{ Exit = $exit; Output = $out; Error = $errOut }
    } finally {
        Remove-Item $tmpOut, $tmpErr -ErrorAction SilentlyContinue
    }
}

function Copy-Remote {
    param([Parameter(Mandatory)][string]$Local, [Parameter(Mandatory)][string]$Remote)
    $stdin = "y`n"
    $stdin | & pscp -pw $Password $Local "${RemoteSsh}:${Remote}" 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "pscp fallo subiendo $Local a $Remote" }
}

# 1) ---------- SSH ----------
Step "1) Validando SSH a $RemoteSsh"
$r = Invoke-Remote 'echo CONNECTED && whoami && hostname' -Quiet
if ($r.Exit -ne 0) {
    Err "No pude conectarme. Verifica usuario/IP/password."
    exit 1
}
$lines = $r.Output -split "`r?`n" | Where-Object { $_ }
$connected = $lines | Where-Object { $_ -eq "CONNECTED" }
if (-not $connected) {
    Err "SSH no devolvio CONNECTED. Salida:"; Write-Host $r.Output
    exit 1
}
Ok ("Conectado como: " + ($lines | Where-Object { $_ -ne "CONNECTED" } | Select-Object -First 1))
Ok ("Hostname: " + ($lines | Where-Object { $_ -ne "CONNECTED" } | Select-Object -Skip 1 -First 1))

# 2) ---------- Docker ----------
Step "2) Validando Docker en el server"
$r = Invoke-Remote 'docker version --format "{{.Server.Version}}"' -Quiet
if ($r.Exit -ne 0) {
    Err "Docker no respondio en el server."
    Write-Host "    Verifica:"
    Write-Host "    - Docker Desktop esta encendido"
    Write-Host "    - El usuario $RemoteSsh tiene permisos (grupo 'docker-users' en Windows)"
    exit 1
}
$dockerVer = ($r.Output -split "`r?`n" | Where-Object { $_ } | Select-Object -Last 1).Trim()
Ok "Docker server: $dockerVer"

# 3) ---------- Puerto libre ----------
Step "3) Buscando puerto libre desde $PreferredPort"
$port = $null
for ($p = $PreferredPort; $p -le ($PreferredPort + 50); $p++) {
    # netstat es nativo en Windows; -ano: numerico + pid
    $cmd = "powershell -Command `"if (Get-NetTCPConnection -LocalPort $p -ErrorAction SilentlyContinue) { 'BUSY' } else { 'FREE' }`""
    $r = Invoke-Remote $cmd -Quiet
    $verdict = ($r.Output -split "`r?`n" | Where-Object { $_ -match "FREE|BUSY" } | Select-Object -Last 1)
    if ($verdict -eq "FREE") { $port = $p; break }
    Warn "Puerto $p ocupado"
}
if (-not $port) {
    Err "No encontre puerto libre en el rango."
    exit 1
}
Ok "Puerto elegido: $port"

# 4) ---------- Generar clave Postgres ----------
Step "4) Generando clave Postgres aleatoria (32 chars)"
function New-Password([int]$len) {
    $chars = (33..126) | ForEach-Object { [char]$_ } | Where-Object { $_ -notmatch "['\""\\``\$]" }
    -join (1..$len | ForEach-Object { $chars | Get-Random })
}
$pgPass = New-Password 32
Ok ("Generada (no se imprime). Hash inicial: " + ([System.BitConverter]::ToString([System.Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($pgPass)))[0..15] -join "").Replace("-","")

# 5) ---------- Carpeta de deploy ----------
Step "5) Preparando carpeta '$DeployDir' en el server"
# Forward slashes funcionan en cmd y para pscp.
$dirNoSlash = $DeployDir -replace '\\','/'
# Usar `mkdir` con flag PowerShell -Force crea TODA la cadena de carpetas (E:\DOCKER\visal aunque E:\DOCKER no exista).
$mkdirCmd = "powershell -NoProfile -Command `"New-Item -ItemType Directory -Path '$DeployDir' -Force | Out-Null; Test-Path '$DeployDir'`""
$r = Invoke-Remote $mkdirCmd -Quiet
$exists = ($r.Output -split "`r?`n" | Where-Object { $_ -match "True|False" } | Select-Object -Last 1)
if ($exists -ne "True") {
    Err "No pude crear la carpeta $DeployDir. Revisa si la unidad E:\ existe en el server."
    exit 1
}
Ok "Carpeta lista."

# 6) ---------- Generar .env + docker-compose.yml ----------
Step "6) Generando .env y docker-compose.yml locales"
$here = $PSScriptRoot
$composeLocal = Join-Path $here "docker-compose.yml"
if (-not (Test-Path $composeLocal)) { Err "No encontre docker-compose.yml en $here"; exit 1 }

$envTmp = New-TemporaryFile
@"
VISAL_IMAGE=$VisalImage
VISAL_PORT=$port
POSTGRES_DB=visal
POSTGRES_USER=visal
POSTGRES_PASSWORD=$pgPass
"@ | Set-Content -Path $envTmp.FullName -Encoding ASCII

Ok ".env y compose listos."

# 7) ---------- Subir archivos ----------
Step "7) Subiendo archivos al server"
Copy-Remote -Local $composeLocal -Remote ("$dirNoSlash/docker-compose.yml")
Copy-Remote -Local $envTmp.FullName -Remote ("$dirNoSlash/.env")
Remove-Item $envTmp.FullName -Force
Ok "Archivos en $DeployDir"

# 8) ---------- (Opcional) login GHCR ----------
if ($GhcrUser -and $GhcrToken) {
    Step "8) Login en GHCR como $GhcrUser"
    $cmd = "echo $GhcrToken | docker login ghcr.io -u $GhcrUser --password-stdin"
    $r = Invoke-Remote $cmd
    if ($r.Exit -ne 0) { Err "Login GHCR fallo"; exit 1 }
    Ok "Login OK"
}

# 9) ---------- Pull + up ----------
Step "9) docker compose pull"
$r = Invoke-Remote "cd /d `"$DeployDir`" && docker compose pull"
if ($r.Exit -ne 0) {
    Err "Pull fallo. Si la imagen es privada en GHCR, pasa -GhcrUser y -GhcrToken al script."
    exit 1
}

Step "10) docker compose up -d"
$r = Invoke-Remote "cd /d `"$DeployDir`" && docker compose up -d --remove-orphans"
if ($r.Exit -ne 0) { Err "up -d fallo. Mira la salida arriba."; exit 1 }
Ok "Stack arriba."

# 11) ---------- Healthcheck ----------
Step "11) Esperando que visal-app este healthy (hasta 90s)"
$ok = $false
for ($i = 0; $i -lt 18; $i++) {
    Start-Sleep -Seconds 5
    $r = Invoke-Remote ('curl.exe -s -o NUL -w "%{http_code}" http://127.0.0.1:' + $port + '/login') -Quiet
    $code = ($r.Output -split "`r?`n" | Where-Object { $_ -match '^\d+$' } | Select-Object -Last 1)
    if ($code -eq "200") {
        Ok "HTTP 200 OK en http://127.0.0.1:$port/login"
        $ok = $true; break
    } else {
        Warn ("Intento {0}/18: HTTP {1}" -f ($i+1), ($code | ForEach-Object { if ($_) { $_ } else { '...' } }))
    }
}
if (-not $ok) {
    Err "El app no respondio 200 despues de 90s. Mira logs en el server:"
    Invoke-Remote "cd /d `"$DeployDir`" && docker compose logs --tail=40 visal-app"
    exit 1
}

# 12) ---------- Reporte final ----------
Write-Host ""
Write-Host "================================================================" -ForegroundColor Green
Write-Host "  DEPLOY EXITOSO" -ForegroundColor Green
Write-Host "================================================================" -ForegroundColor Green
Write-Host ""
Write-Host "  Server      : $RemoteSsh ($dockerVer)" -ForegroundColor White
Write-Host "  Carpeta     : $DeployDir" -ForegroundColor White
Write-Host "  Imagen      : $VisalImage" -ForegroundColor White
Write-Host "  Puerto local: $port  (bindeado a 127.0.0.1 del server)" -ForegroundColor White
Write-Host "  Postgres    : clave guardada en $DeployDir\.env" -ForegroundColor White
Write-Host ""
Write-Host "  Apunta tu reverse proxy global a:" -ForegroundColor Yellow
Write-Host "      http://127.0.0.1:$port" -ForegroundColor White
Write-Host "  (con WebSockets habilitados para Blazor)" -ForegroundColor DarkGray
Write-Host ""
Write-Host "  Updates futuros: usa deploy-remote.ps1 o ssh y cd $DeployDir" -ForegroundColor DarkGray
Write-Host "================================================================" -ForegroundColor Green
