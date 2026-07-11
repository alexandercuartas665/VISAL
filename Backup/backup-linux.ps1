#requires -Version 5.1
<#
.SYNOPSIS
    Backup completo de Visal PROD (server Linux) desde tu maquina Windows via SSH.

.DESCRIPTION
    Paralelo a deploy/docker-prod/actualizar-linux.ps1 pero para BACKUP en vez de deploy.

    Flujo:
      1) Sube backup-en-linux.sh al server (por si lo cambiaste localmente).
      2) Lo ejecuta remotamente. El script hace pg_dump + tar de uploads +
         copia del compose y produce un .tar.gz en /tmp/ del server.
      3) Baja el .tar.gz con scp al DestinationRoot local.
      4) Localmente: cifra el .env con AES + tu clave, agrega metadata final
         y RESTORE.md, todo comprimido en el ZIP final visal-backup-*.zip.
      5) Borra archivos temporales del server.
      6) Rotacion: mantiene los ultimos N backups locales.

    NO modifica nada del server:
      - pg_dump usa snapshot MVCC (no bloquea escrituras)
      - tar de uploads es lectura pura
      - Cero cambios en BD, volumenes, contenedores o config

.PARAMETER RemoteHost
    IP o nombre del server prod. Default: 10.0.0.3 (mismo del deploy).

.PARAMETER RemoteUser
    Usuario SSH del server. Default: root.

.PARAMETER RemoteDir
    Carpeta remota del deploy. Default: /opt/visal.

.PARAMETER KeyName
    Nombre de la llave SSH en ~/.ssh/. Default: id_ed25519_visal.

.PARAMETER DestinationRoot
    Carpeta local para el ZIP. Default: $env:TEMP\visal-backups.
    Cuando conectes el disco D, pasar "D:\Backups\Visal".

.PARAMETER EnvFile
    .env local a cifrar dentro del ZIP. Default: <repo>\deploy\docker-prod\.env.

.PARAMETER EncryptionPassword
    SecureString con la clave para cifrar el .env. Si se omite, se pide por prompt.

.PARAMETER KeepLast
    Rotacion: backups a conservar en DestinationRoot. Default: 14.

.EXAMPLE
    .\backup-linux.ps1
    # Usa defaults del deploy: root@10.0.0.3, /opt/visal, id_ed25519_visal
    # ZIP a %TEMP%\visal-backups\

.EXAMPLE
    .\backup-linux.ps1 -DestinationRoot "D:\Backups\Visal" -KeepLast 30
#>
[CmdletBinding()]
param(
    [string]$RemoteHost = '10.0.0.3',
    [string]$RemoteUser = 'root',
    [string]$RemoteDir = '/opt/visal',
    [string]$KeyName = 'id_ed25519_visal',
    [string]$DestinationRoot = "$env:TEMP\visal-backups",
    [string]$EnvFile,
    [securestring]$EncryptionPassword,
    [int]$KeepLast = 14
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $EnvFile) { $EnvFile = Join-Path $RepoRoot 'deploy\docker-prod\.env' }

. (Join-Path $PSScriptRoot 'lib\Crypto.ps1')

function Step   { param([string]$msg) Write-Host ""; Write-Host "==> $msg" -ForegroundColor Cyan }
function Ok     { param([string]$msg) Write-Host "    OK    $msg" -ForegroundColor Green }
function Info   { param([string]$msg) Write-Host "    info  $msg" -ForegroundColor Gray }
function Fail   { param([string]$msg) Write-Host "    ERR   $msg" -ForegroundColor Red; exit 1 }

# --- Prereqs ---
Step "Validando prereqs locales"
$keyPath = Join-Path $HOME ".ssh\$KeyName"
if (-not (Test-Path $keyPath)) { Fail "Llave SSH no existe: $keyPath. Corre bootstrap-linux.ps1 primero." }
$sshScript = Join-Path $PSScriptRoot 'backup-en-linux.sh'
if (-not (Test-Path $sshScript)) { Fail "No encuentro backup-en-linux.sh en $PSScriptRoot" }
if (-not (Test-Path $EnvFile)) { Fail "No encuentro .env local en $EnvFile (necesario para cifrar en el ZIP)" }
$target = "$RemoteUser@$RemoteHost"
Ok "llave: $keyPath"
Ok "target: $target"

if (-not $EncryptionPassword) {
    $EncryptionPassword = Read-Host "Clave para cifrar el .env dentro del ZIP" -AsSecureString
    if ($EncryptionPassword.Length -lt 1) { Fail "La clave no puede estar vacia." }
}

if (-not (Test-Path $DestinationRoot)) {
    New-Item -ItemType Directory -Path $DestinationRoot | Out-Null
}
Ok "destino local: $DestinationRoot"

# --- Banner ---
Write-Host ""
Write-Host "=========================================================================" -ForegroundColor Cyan
Write-Host " Visal IPS RT - BACKUP prod (Linux) via SSH" -ForegroundColor Cyan
Write-Host "=========================================================================" -ForegroundColor Cyan
Write-Host " Server  : $target"
Write-Host " Remote  : $RemoteDir"
Write-Host " Local   : $DestinationRoot"
Write-Host ""

# --- 1) Subir el script bash ---
Step "Subiendo backup-en-linux.sh al server"
$prevEA = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    & scp -i $keyPath -o BatchMode=yes $sshScript "${target}:${RemoteDir}/backup-en-linux.sh" 2>&1 | Out-Null
    $rc = $LASTEXITCODE
} finally { $ErrorActionPreference = $prevEA }
if ($rc -ne 0) { Fail "scp fallo con codigo $rc" }
Ok "script subido"

# Normalizar LF + permisos
$prevEA = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    & ssh -i $keyPath -o BatchMode=yes $target "sed -i 's/\r`$//' $RemoteDir/backup-en-linux.sh && chmod +x $RemoteDir/backup-en-linux.sh" 2>&1 | Out-Null
} finally { $ErrorActionPreference = $prevEA }

# --- 2) Ejecutar en el server ---
Step "Ejecutando backup-en-linux.sh en el server"
$prevEA = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    $sshOut = & ssh -i $keyPath -o BatchMode=yes $target "cd $RemoteDir && ./backup-en-linux.sh -d $RemoteDir" 2>&1
    $rc = $LASTEXITCODE
} finally { $ErrorActionPreference = $prevEA }

# El script bash imprime logs a stderr y RESULT_* a stdout. Con 2>&1 se mezclan;
# los mostramos completos y luego parseamos las lineas RESULT_*.
$sshOut | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
if ($rc -ne 0) { Fail "backup-en-linux.sh fallo (exit $rc)" }

# Parsear RESULT_*
$resultLines = $sshOut | Where-Object { $_ -match '^RESULT_' }
$results = @{}
foreach ($line in $resultLines) {
    if ($line -match '^RESULT_([A-Z]+)=(.*)$') {
        $results[$Matches[1]] = $Matches[2].Trim()
    }
}
if (-not $results.ContainsKey('FILE')) { Fail "No pude parsear RESULT_FILE del output remoto" }
$remoteFile = $results['FILE']
$remoteSize = [int64]($results['SIZE'])
Ok "generado remoto: $remoteFile ($([math]::Round($remoteSize/1MB, 2)) MB)"
Ok ("BD: {0} tenants, {1} usuarios, {2} pacientes, {3} HCs" -f $results['TENANTS'], $results['USERS'], $results['PACIENTES'], $results['HCS'])
Ok "imagen: $($results['IMAGE'])"

# --- 3) Bajar el .tar.gz con scp ---
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$staging = Join-Path $env:TEMP "visal-backup-download-$stamp"
New-Item -ItemType Directory -Path $staging | Out-Null
$localTar = Join-Path $staging 'remote.tar.gz'

Step "Bajando .tar.gz del server (scp)"
$prevEA = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    & scp -i $keyPath -o BatchMode=yes "${target}:${remoteFile}" $localTar 2>&1 | Out-Null
    $rc = $LASTEXITCODE
} finally { $ErrorActionPreference = $prevEA }
if ($rc -ne 0) { Fail "scp de descarga fallo (exit $rc)" }
$dlSize = (Get-Item $localTar).Length
Ok ("descarga OK: {0:N2} MB" -f ($dlSize / 1MB))

# --- 4) Extraer el .tar.gz remoto en el staging ---
Step "Extrayendo contenido remoto"
$extractDir = Join-Path $staging 'extract'
New-Item -ItemType Directory -Path $extractDir | Out-Null
# En Windows 10+ tar.exe esta disponible por defecto. Fallback a Expand nada porque
# tar.gz no lo maneja nativamente.
$null = Get-Command tar.exe -ErrorAction Stop
& tar.exe -xzf $localTar -C $extractDir
if ($LASTEXITCODE -ne 0) { Fail "tar -xzf fallo" }
Ok "contenido extraido"

# --- 5) Cifrar el .env local + generar metadata final + RESTORE.md ---
Step "Cifrando .env local"
Protect-FileWithAes -InputPath $EnvFile -OutputPath (Join-Path $extractDir 'env.aes') -Password $EncryptionPassword
Ok "env.aes (AES-256-CBC, PBKDF2 200k)"

# Metadata final (mezcla la del remoto + info local)
Step "Generando metadata.json final"
$remoteMetaPath = Join-Path $extractDir 'metadata.remote.json'
$remoteMeta = if (Test-Path $remoteMetaPath) { Get-Content $remoteMetaPath -Raw | ConvertFrom-Json } else { $null }
$gitCommit = $null
Push-Location $RepoRoot
try { $gitCommit = (& git rev-parse HEAD 2>$null).Trim() } catch { }
Pop-Location

$meta = [ordered]@{
    version = '1.0'
    timestamp = (Get-Date -Format 'yyyy-MM-ddTHH:mm:sszzz')
    server = @{
        host = $RemoteHost
        user = $RemoteUser
        remoteDir = $RemoteDir
        hostname = if ($remoteMeta) { $remoteMeta.server } else { $null }
    }
    imageTag = $results['IMAGE']
    gitCommit = $gitCommit
    db = @{
        name = if ($remoteMeta) { $remoteMeta.db.name } else { $null }
        dumpSizeBytes = if ($remoteMeta) { $remoteMeta.db.dumpSizeBytes } else { 0 }
        tenants = [int]$results['TENANTS']
        pacientes = [int]$results['PACIENTES']
        historiasClinicas = [int]$results['HCS']
        platformUsers = [int]$results['USERS']
    }
    uploads = @{
        sizeBytes = if ($remoteMeta) { $remoteMeta.uploads.sizeBytes } else { 0 }
        volume = if ($remoteMeta) { $remoteMeta.uploads.volume } else { 'visal-prod_visal-uploads' }
    }
    envIncluded = $true
    remoteTimestamp = if ($remoteMeta) { $remoteMeta.timestamp } else { $null }
}
$meta | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $extractDir 'metadata.json') -Encoding UTF8
Remove-Item -Path $remoteMetaPath -Force -ErrorAction SilentlyContinue
Ok "metadata.json"

# RESTORE.md (mismo que el backup-visal.ps1 local)
Step "Generando RESTORE.md"
$restoreMd = @'
# Restaurar Visal desde este ZIP

Este ZIP fue generado por `Backup/backup-linux.ps1` contra un server prod Linux.
Contiene el estado completo del sistema en el momento indicado en `metadata.json`.

## Requisitos del servidor destino

- Docker Engine + Docker Compose v2
- Puerto libre para la app (el mismo del compose original o uno alternativo)
- PowerShell 7 (Windows/Linux) si vas a usar `restore-visal.ps1`

## Restauracion automatica (recomendado)

Desde tu maquina, con el ZIP y los scripts de la carpeta `Backup/` a mano:

```powershell
cd C:\DesarrolloIA\Visal\Backup
.\restore-visal.ps1 -ZipPath "C:\ruta\al\visal-backup-YYYYMMDD-HHMMSS.zip" -TargetDir "C:\DesarrolloIA\VisalRestore"
```

## Restauracion manual (paso a paso)

1. Descomprimir el ZIP a un directorio de trabajo (ej. `/opt/visal-restore/`).
2. Descifrar `env.aes` a `.env` con la clave que usaste al hacer el backup:
   ```powershell
   . .\lib\Crypto.ps1
   $sec = Read-Host "Clave" -AsSecureString
   Unprotect-FileWithAes -InputPath env.aes -OutputPath .env -Password $sec
   ```
3. `docker compose up -d postgres`  (esperar `healthy`).
4. Restaurar la BD:
   ```bash
   docker cp db.dump visal-postgres-prod:/tmp/db.dump
   docker exec visal-postgres-prod sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists --no-owner --no-privileges /tmp/db.dump'
   ```
5. Restaurar uploads:
   ```bash
   docker run --rm -v visal-prod_visal-uploads:/target -v "$(pwd):/src" alpine sh -c "cd /target && tar -xzf /src/uploads.tar.gz"
   ```
6. `docker compose up -d visal-app`.
7. Verificar: login, HC con firma, descargar PDF de contrato.

## Notas

- La imagen registrada en `metadata.imageTag` puede requerir `docker login ghcr.io` si es privada.
- Los timestamps de la BD se guardan en UTC en disco; el rendering es Colombia.
'@
$restoreMd | Set-Content -Path (Join-Path $extractDir 'RESTORE.md') -Encoding UTF8
Ok "RESTORE.md"

# --- 6) Comprimir a ZIP final ---
$zipName = "visal-backup-prod-$stamp.zip"
$zipPath = Join-Path $DestinationRoot $zipName
Step "Comprimiendo ZIP final: $zipPath"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $extractDir '*') -DestinationPath $zipPath -CompressionLevel Optimal
$zipSize = (Get-Item $zipPath).Length
Ok ("ZIP final: {0:N2} MB" -f ($zipSize / 1MB))

# --- 7) Limpieza remota ---
Step "Limpiando archivos temporales en el server"
$prevEA = $ErrorActionPreference
$ErrorActionPreference = 'Continue'
try {
    & ssh -i $keyPath -o BatchMode=yes $target "rm -f '$remoteFile' && rm -rf /tmp/visal-backup-staging-*" 2>&1 | Out-Null
} finally { $ErrorActionPreference = $prevEA }
Ok "server limpio"

# Limpieza local
Remove-Item -Path $staging -Recurse -Force -ErrorAction SilentlyContinue

# --- 8) Rotacion ---
if ($KeepLast -gt 0) {
    $viejos = Get-ChildItem -Path $DestinationRoot -Filter 'visal-backup-prod-*.zip' |
              Sort-Object LastWriteTime -Descending |
              Select-Object -Skip $KeepLast
    if ($viejos) {
        Step ("Rotacion: eliminando {0} backup(s) viejos" -f $viejos.Count)
        foreach ($v in $viejos) {
            Remove-Item -Path $v.FullName -Force
            Ok ("borrado: {0}" -f $v.Name)
        }
    }
}

Write-Host ""
Write-Host "=========================================================================" -ForegroundColor Green
Write-Host " Backup PROD completado" -ForegroundColor Green
Write-Host "=========================================================================" -ForegroundColor Green
Write-Host " Archivo : $zipPath"
Write-Host (" Tamano  : {0:N2} MB" -f ($zipSize / 1MB))
Write-Host (" BD      : {0} tenants, {1} usuarios, {2} pacientes, {3} HCs" -f $results['TENANTS'], $results['USERS'], $results['PACIENTES'], $results['HCS'])
Write-Host " Imagen  : $($results['IMAGE'])"
Write-Host "========================================================================="
