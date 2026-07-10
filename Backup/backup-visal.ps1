#requires -Version 5.1
<#
.SYNOPSIS
    Backup completo del sistema Visal (BD + uploads + config + metadata) en un ZIP
    portable capaz de re-implementar el sistema en otro servidor Docker.

.DESCRIPTION
    Empaqueta:
      1) db.dump         - pg_dump --format=custom de toda la BD
      2) uploads.tar.gz  - contenido del volumen visal-uploads (firmas, PDFs, logos, adjuntos)
      3) docker-compose.yml - snapshot del compose actual
      4) env.aes         - el .env cifrado con AES-256 (clave del operador)
      5) metadata.json   - image tag, timestamp, git commit, tamanos, conteos
      6) RESTORE.md      - instrucciones paso a paso para restaurar

    Todo va a un ZIP: visal-backup-YYYYMMDD-HHMMSS.zip
    Con rotacion: mantiene los ultimos N backups (default 14).

.PARAMETER DockerContext
    Nombre del docker context donde corre Visal. "default" = local (para pruebas).
    Prod: pasa el name del context SSH configurado (ej. "visal-prod-remote").

.PARAMETER DestinationRoot
    Carpeta donde guardar el ZIP. Default: $env:TEMP\visal-backups (para pruebas).
    Cuando este el disco D conectado, cambiar a "D:\Backups\Visal".

.PARAMETER PostgresContainer
    Nombre del contenedor postgres. Default: visal-postgres-prod (segun docker-compose.yml).

.PARAMETER AppContainer
    Nombre del contenedor de la app (para leer el volumen uploads). Default: visal-app.

.PARAMETER UploadsMountPath
    Ruta dentro del contenedor app donde esta montado el volumen uploads.
    Default: /app/wwwroot/uploads

.PARAMETER ComposeFile
    Path al docker-compose.yml. Default: <repo>\deploy\docker-prod\docker-compose.yml

.PARAMETER EnvFile
    Path al .env que se cifrara dentro del ZIP. Default: <repo>\deploy\docker-prod\.env

.PARAMETER EncryptionPassword
    SecureString con la clave para cifrar el .env. Si se omite, se pide por prompt.

.PARAMETER KeepLast
    Cuantos backups anteriores conservar en DestinationRoot (rotacion). Default: 14.

.PARAMETER SkipUploads
    Si se pasa, no incluye el volumen de uploads (solo BD + config). Backup mucho mas rapido.

.EXAMPLE
    # Backup local rapido (contra el docker default) al C:\Temp:
    .\backup-visal.ps1

.EXAMPLE
    # Backup de prod (SSH context) al disco D:
    .\backup-visal.ps1 -DockerContext visal-prod-remote -DestinationRoot "D:\Backups\Visal"

.EXAMPLE
    # Task Scheduler (no interactivo): pasar la clave como SecureString serializada:
    $sec = ConvertTo-SecureString "mi-clave" -AsPlainText -Force
    .\backup-visal.ps1 -EncryptionPassword $sec
#>
[CmdletBinding()]
param(
    [string]$DockerContext = 'default',
    [string]$DestinationRoot = "$env:TEMP\visal-backups",
    [string]$PostgresContainer = 'visal-postgres-prod',
    [string]$AppContainer = 'visal-app',
    [string]$UploadsMountPath = '/app/wwwroot/uploads',
    [string]$ComposeFile,
    [string]$EnvFile,
    [securestring]$EncryptionPassword,
    [int]$KeepLast = 14,
    [switch]$SkipUploads
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# --- Preludio: paths por default relativos al repo ---
$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $ComposeFile) { $ComposeFile = Join-Path $RepoRoot 'deploy\docker-prod\docker-compose.yml' }
if (-not $EnvFile)     { $EnvFile     = Join-Path $RepoRoot 'deploy\docker-prod\.env' }

# Cargar helper de cifrado
. (Join-Path $PSScriptRoot 'lib\Crypto.ps1')

function Write-Step { param([string]$msg) Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $msg" -ForegroundColor Cyan }
function Write-Ok   { param([string]$msg) Write-Host "                   OK  $msg" -ForegroundColor Green }
function Write-Warn2 { param([string]$msg) Write-Host "                   !!  $msg" -ForegroundColor Yellow }

# --- 1) Validaciones ---
Write-Step "Validando entorno"

# Docker CLI disponible
$null = Get-Command docker -ErrorAction Stop
Write-Ok "docker CLI disponible"

# Context existe
$ctxList = & docker context ls --format '{{.Name}}' 2>$null
if ($ctxList -notcontains $DockerContext) {
    throw "Docker context '$DockerContext' no existe. Contextos disponibles: $($ctxList -join ', ')"
}
Write-Ok "docker context '$DockerContext' existe"

# Contenedor postgres UP en ese context
$psOut = & docker --context $DockerContext ps --filter "name=^${PostgresContainer}$" --format '{{.Names}} {{.Status}}' 2>$null
if (-not $psOut) {
    throw "Contenedor '$PostgresContainer' no esta corriendo en el context '$DockerContext'. Backup abortado (no vale la pena hacer copias sin la BD)."
}
Write-Ok "$psOut"

# Compose file existe
if (-not (Test-Path $ComposeFile)) {
    throw "docker-compose.yml no encontrado en: $ComposeFile"
}
Write-Ok "compose file: $ComposeFile"

# .env
$envExists = Test-Path $EnvFile
if ($envExists) {
    Write-Ok ".env: $EnvFile"
} else {
    Write-Warn2 ".env no encontrado en $EnvFile - el backup NO incluira secretos (necesitaras armarlo a mano al restaurar)."
}

# Password para cifrar .env (solo si hay .env real)
if ($envExists -and -not $EncryptionPassword) {
    $EncryptionPassword = Read-Host "Clave para cifrar el .env dentro del ZIP" -AsSecureString
    if ($EncryptionPassword.Length -lt 1) {
        throw "La clave de cifrado no puede estar vacia."
    }
}

# Destino
if (-not (Test-Path $DestinationRoot)) {
    New-Item -ItemType Directory -Path $DestinationRoot | Out-Null
}
Write-Ok "destino: $DestinationRoot"

# --- 2) Extraer POSTGRES_* del compose environment (via container inspect) ---
# Preferimos leer del contenedor real (no del .env) porque el contenedor puede
# haberse arrancado con otra .env-file y ese es el estado autoritativo.
Write-Step "Leyendo credenciales de la BD desde el contenedor"
$envJson = & docker --context $DockerContext inspect --format '{{json .Config.Env}}' $PostgresContainer
$envArr = $envJson | ConvertFrom-Json
$dbName = ($envArr | Where-Object { $_ -match '^POSTGRES_DB=' }) -replace '^POSTGRES_DB=', ''
$dbUser = ($envArr | Where-Object { $_ -match '^POSTGRES_USER=' }) -replace '^POSTGRES_USER=', ''
$dbPass = ($envArr | Where-Object { $_ -match '^POSTGRES_PASSWORD=' }) -replace '^POSTGRES_PASSWORD=', ''
if (-not $dbName -or -not $dbUser -or -not $dbPass) {
    throw "No pude extraer POSTGRES_DB/USER/PASSWORD del contenedor '$PostgresContainer'."
}
Write-Ok "BD=$dbName USER=$dbUser"

# --- 3) Staging temporal ---
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$staging = Join-Path $env:TEMP "visal-backup-staging-$stamp"
New-Item -ItemType Directory -Path $staging | Out-Null
Write-Step "Staging temporal: $staging"

try {
    # --- 4) pg_dump ---
    Write-Step "pg_dump de $dbName"
    # Se ejecuta DENTRO del contenedor postgres (usa el pg_dump del mismo Postgres 16 y evita necesidad de cliente local).
    $dumpCmd = "PGPASSWORD='$dbPass' pg_dump -U '$dbUser' -d '$dbName' --no-owner --no-privileges --clean --if-exists -Fc -f /tmp/visal.dump"
    & docker --context $DockerContext exec $PostgresContainer sh -c $dumpCmd
    if ($LASTEXITCODE -ne 0) { throw "pg_dump fallo con codigo $LASTEXITCODE" }
    # Copiar el dump al host staging
    & docker --context $DockerContext cp "${PostgresContainer}:/tmp/visal.dump" (Join-Path $staging 'db.dump')
    if ($LASTEXITCODE -ne 0) { throw "docker cp del dump fallo" }
    # Limpiar dentro del contenedor
    & docker --context $DockerContext exec $PostgresContainer sh -c 'rm -f /tmp/visal.dump' | Out-Null

    $dbSize = (Get-Item (Join-Path $staging 'db.dump')).Length
    Write-Ok ("db.dump  {0:N2} MB" -f ($dbSize / 1MB))

    # --- 5) uploads.tar.gz ---
    if (-not $SkipUploads) {
        Write-Step "Empaquetando volumen uploads (via $AppContainer)"
        # tar dentro del contenedor app y volcar por stdout al host (no depende de que exista mount local)
        $tarFile = Join-Path $staging 'uploads.tar.gz'
        # Cuidado: el path host se pasa al redirect de PowerShell, no al comando docker.
        & docker --context $DockerContext exec $AppContainer sh -c "cd $UploadsMountPath && tar -czf - ." | Set-Content -Path $tarFile -Encoding Byte
        if ($LASTEXITCODE -ne 0) { throw "tar de uploads fallo (exit $LASTEXITCODE)" }
        $upSize = (Get-Item $tarFile).Length
        Write-Ok ("uploads.tar.gz  {0:N2} MB" -f ($upSize / 1MB))
    } else {
        Write-Warn2 "SkipUploads activo - el backup NO incluye firmas/PDFs/logos."
    }

    # --- 6) docker-compose.yml ---
    Copy-Item -Path $ComposeFile -Destination (Join-Path $staging 'docker-compose.yml')
    Write-Ok "docker-compose.yml copiado"

    # --- 7) .env cifrado ---
    if ($envExists) {
        Write-Step "Cifrando .env"
        Protect-FileWithAes -InputPath $EnvFile -OutputPath (Join-Path $staging 'env.aes') -Password $EncryptionPassword
        Write-Ok "env.aes (AES-256-CBC, PBKDF2 200k)"
    }

    # --- 8) metadata.json ---
    Write-Step "Generando metadata.json"
    # Imagen actual del contenedor visal-app
    $imageTag = & docker --context $DockerContext inspect --format '{{.Config.Image}}' $AppContainer 2>$null
    # Conteos rapidos de la BD (best-effort; si no hay tablas, ignoramos error)
    $countCmd = "PGPASSWORD='$dbPass' psql -U '$dbUser' -d '$dbName' -tAc `"select (select count(*) from tenants) || '|' || (select count(*) from pacientes) || '|' || (select count(*) from historias_clinicas) || '|' || (select count(*) from platform_users)`" 2>/dev/null"
    $counts = & docker --context $DockerContext exec $PostgresContainer sh -c $countCmd 2>$null
    $tenants = 0; $pacientes = 0; $hcs = 0; $users = 0
    if ($counts) {
        $parts = $counts.Trim().Split('|')
        if ($parts.Count -eq 4) {
            $tenants = [int]$parts[0]; $pacientes = [int]$parts[1]; $hcs = [int]$parts[2]; $users = [int]$parts[3]
        }
    }
    $gitCommit = $null
    Push-Location $RepoRoot
    try { $gitCommit = (& git rev-parse HEAD 2>$null).Trim() } catch { }
    Pop-Location

    $meta = [ordered]@{
        version = '1.0'
        timestamp = (Get-Date -Format 'yyyy-MM-ddTHH:mm:sszzz')
        dockerContext = $DockerContext
        imageTag = $imageTag
        gitCommit = $gitCommit
        db = @{
            name = $dbName
            user = $dbUser
            dumpSizeBytes = $dbSize
            tenants = $tenants
            pacientes = $pacientes
            historiasClinicas = $hcs
            platformUsers = $users
        }
        uploads = @{
            skipped = [bool]$SkipUploads
            sizeBytes = if ($SkipUploads) { 0 } else { (Get-Item (Join-Path $staging 'uploads.tar.gz')).Length }
        }
        envIncluded = $envExists
    }
    $meta | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $staging 'metadata.json') -Encoding UTF8
    Write-Ok "metadata.json"

    # --- 9) RESTORE.md ---
    $restoreMd = @'
# Restaurar Visal desde este ZIP

Este ZIP contiene el estado completo del sistema Visal en el momento indicado en `metadata.json`.

## Requisitos del servidor destino

- Docker Engine + Docker Compose v2
- Puerto libre para la app (el mismo del compose original o uno alternativo)
- PowerShell 5.1+ (si vas a usar `restore-visal.ps1`)

## Restauracion automatica (recomendado)

Desde tu maquina, con el ZIP y los scripts de la carpeta `Backup/` a mano:

```powershell
cd C:\DesarrolloIA\Visal\Backup
.\restore-visal.ps1 -ZipPath "C:\ruta\al\visal-backup-YYYYMMDD-HHMMSS.zip" -TargetDir "C:\DesarrolloIA\VisalRestore"
```

El script pedira la clave del `env.aes` y hara todo el proceso: descomprime, descifra, levanta postgres, restaura la BD, restaura uploads, arranca la app.

## Restauracion manual (paso a paso)

1. Descomprimir el ZIP a un directorio de trabajo (ej. `/opt/visal-restore/`).
2. Descifrar `env.aes` a `.env` (con la clave que usaste al hacer el backup):
   ```powershell
   . .\lib\Crypto.ps1
   $sec = Read-Host "Clave" -AsSecureString
   Unprotect-FileWithAes -InputPath env.aes -OutputPath .env -Password $sec
   ```
3. Levantar solo Postgres:
   ```bash
   docker compose up -d postgres
   ```
4. Esperar healthcheck (`docker compose ps` hasta que aparezca `healthy`).
5. Restaurar la BD:
   ```bash
   docker cp db.dump visal-postgres-prod:/tmp/db.dump
   docker exec -it visal-postgres-prod sh -c 'PGPASSWORD="$POSTGRES_PASSWORD" pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists /tmp/db.dump'
   ```
6. Restaurar uploads (montaje temporal alpine para acceder al volumen):
   ```bash
   docker run --rm -v visal-prod_visal-uploads:/target -v "$(pwd):/src" alpine sh -c "cd /target && tar -xzf /src/uploads.tar.gz"
   ```
7. Levantar la app:
   ```bash
   docker compose up -d visal-app
   ```
8. Verificar: navegar a la URL configurada, login, revisar HC con firmas y descargar un PDF de contrato.

## Notas

- La imagen registrada en `metadata.json` (imageTag) puede requerir `docker login ghcr.io` si es privada.
- Si el servidor destino usa un puerto distinto, edita `VISAL_PORT` en el `.env` antes del `up -d`.
- Los timestamps de la BD se guardan en UTC en disco; el rendering es Colombia (`TZ=America/Bogota` en compose).
'@
    $restoreMd | Set-Content -Path (Join-Path $staging 'RESTORE.md') -Encoding UTF8
    Write-Ok "RESTORE.md"

    # --- 10) Zip final ---
    $zipName = "visal-backup-$stamp.zip"
    $zipPath = Join-Path $DestinationRoot $zipName
    Write-Step "Comprimiendo a $zipPath"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zipPath -CompressionLevel Optimal
    $zipSize = (Get-Item $zipPath).Length
    Write-Ok ("ZIP listo  {0:N2} MB" -f ($zipSize / 1MB))

    # --- 11) Rotacion: borrar backups viejos ---
    if ($KeepLast -gt 0) {
        $viejos = Get-ChildItem -Path $DestinationRoot -Filter 'visal-backup-*.zip' |
                  Sort-Object LastWriteTime -Descending |
                  Select-Object -Skip $KeepLast
        if ($viejos) {
            Write-Step ("Rotacion: eliminando {0} backup(s) viejos (KeepLast={1})" -f $viejos.Count, $KeepLast)
            foreach ($v in $viejos) {
                Remove-Item -Path $v.FullName -Force
                Write-Ok ("borrado: {0}" -f $v.Name)
            }
        }
    }

    Write-Host ""
    Write-Host "=====================================================" -ForegroundColor Green
    Write-Host " Backup completado" -ForegroundColor Green
    Write-Host "=====================================================" -ForegroundColor Green
    Write-Host " Archivo : $zipPath"
    Write-Host (" Tamano  : {0:N2} MB" -f ($zipSize / 1MB))
    Write-Host " BD      : $tenants tenants, $users usuarios, $pacientes pacientes, $hcs historias clinicas"
    Write-Host "====================================================="
}
finally {
    # Limpieza del staging (con o sin exito)
    if (Test-Path $staging) {
        Remove-Item -Path $staging -Recurse -Force -ErrorAction SilentlyContinue
    }
}
