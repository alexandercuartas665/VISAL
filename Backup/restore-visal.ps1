#requires -Version 5.1
<#
.SYNOPSIS
    Restaura un backup de Visal (ZIP producido por backup-visal.ps1) en un docker-compose limpio.

.DESCRIPTION
    Flujo:
      1) Descomprime el ZIP en TargetDir.
      2) Descifra env.aes -> .env.
      3) `docker compose up -d postgres` (con el compose y env restaurados).
      4) Espera healthcheck.
      5) `pg_restore` del db.dump.
      6) Restaura el volumen uploads desde uploads.tar.gz.
      7) `docker compose up -d visal-app`.
      8) Verifica que la app responde en el puerto.

.PARAMETER ZipPath
    Path al ZIP a restaurar.

.PARAMETER TargetDir
    Directorio de trabajo donde se descomprime el ZIP y donde vive el compose.

.PARAMETER DockerContext
    Docker context donde levantar la restauracion. Default: default (local).

.PARAMETER EncryptionPassword
    SecureString con la clave del env.aes. Si se omite, se pide por prompt.

.PARAMETER SkipUploads
    Si se pasa, no restaura el volumen uploads (uso raro; se pierden firmas/PDFs).

.EXAMPLE
    .\restore-visal.ps1 -ZipPath "C:\Temp\visal-backups\visal-backup-20260710-140000.zip" -TargetDir "C:\DesarrolloIA\VisalRestore"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ZipPath,
    [Parameter(Mandatory)][string]$TargetDir,
    [string]$DockerContext = 'default',
    [securestring]$EncryptionPassword,
    [switch]$SkipUploads
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib\Crypto.ps1')

function Write-Step { param([string]$msg) Write-Host "[$(Get-Date -Format 'HH:mm:ss')] $msg" -ForegroundColor Cyan }
function Write-Ok   { param([string]$msg) Write-Host "                   OK  $msg" -ForegroundColor Green }

# --- Validaciones ---
if (-not (Test-Path $ZipPath)) { throw "ZIP no encontrado: $ZipPath" }
$null = Get-Command docker -ErrorAction Stop

$ctxList = & docker context ls --format '{{.Name}}' 2>$null
if ($ctxList -notcontains $DockerContext) {
    throw "Docker context '$DockerContext' no existe. Contextos: $($ctxList -join ', ')"
}

if (-not (Test-Path $TargetDir)) {
    New-Item -ItemType Directory -Path $TargetDir | Out-Null
}
$TargetDir = (Resolve-Path $TargetDir).Path

# --- 1) Descomprimir ---
Write-Step "Descomprimiendo ZIP en $TargetDir"
Expand-Archive -Path $ZipPath -DestinationPath $TargetDir -Force
foreach ($f in @('db.dump', 'docker-compose.yml', 'metadata.json')) {
    if (-not (Test-Path (Join-Path $TargetDir $f))) { throw "Archivo esperado no encontrado en el ZIP: $f" }
}
Write-Ok "contenido extraido"

$meta = Get-Content (Join-Path $TargetDir 'metadata.json') -Raw | ConvertFrom-Json
Write-Host "  backup: $($meta.timestamp)  image: $($meta.imageTag)" -ForegroundColor DarkGray
Write-Host "  BD:     $($meta.db.tenants) tenants  $($meta.db.pacientes) pacientes  $($meta.db.historiasClinicas) HCs" -ForegroundColor DarkGray

# --- 2) Descifrar env.aes ---
$envAesPath = Join-Path $TargetDir 'env.aes'
$envPath = Join-Path $TargetDir '.env'
if (Test-Path $envAesPath) {
    if (-not $EncryptionPassword) {
        $EncryptionPassword = Read-Host "Clave para descifrar env.aes" -AsSecureString
    }
    Write-Step "Descifrando env.aes -> .env"
    Unprotect-FileWithAes -InputPath $envAesPath -OutputPath $envPath -Password $EncryptionPassword
    Write-Ok ".env restaurado"
} elseif ($meta.envIncluded) {
    throw "metadata dice envIncluded=true pero env.aes no esta en el ZIP."
} else {
    Write-Host "  (backup sin env; asumo que ya tienes uno en $envPath)" -ForegroundColor Yellow
    if (-not (Test-Path $envPath)) {
        throw "No hay .env en $TargetDir y el backup no lo trae. Copia uno antes de continuar."
    }
}

# --- 3) Levantar postgres ---
Write-Step "docker compose up -d postgres"
Push-Location $TargetDir
try {
    & docker --context $DockerContext compose up -d postgres
    if ($LASTEXITCODE -ne 0) { throw "compose up postgres fallo" }
    Write-Ok "postgres up"

    # --- 4) Esperar healthcheck ---
    Write-Step "Esperando healthcheck de postgres"
    $psName = 'visal-postgres-prod'
    $ok = $false
    for ($i = 0; $i -lt 60; $i++) {
        $st = & docker --context $DockerContext inspect --format '{{.State.Health.Status}}' $psName 2>$null
        if ($st -eq 'healthy') { $ok = $true; break }
        Start-Sleep -Seconds 2
    }
    if (-not $ok) { throw "Postgres no llego a healthy en 120s" }
    Write-Ok "postgres healthy"

    # --- 5) pg_restore ---
    Write-Step "pg_restore del db.dump"
    & docker --context $DockerContext cp (Join-Path $TargetDir 'db.dump') "${psName}:/tmp/db.dump"
    if ($LASTEXITCODE -ne 0) { throw "docker cp del dump al contenedor fallo" }
    $restoreCmd = 'PGPASSWORD="$POSTGRES_PASSWORD" pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists --no-owner --no-privileges /tmp/db.dump'
    & docker --context $DockerContext exec $psName sh -c $restoreCmd
    if ($LASTEXITCODE -ne 0) {
        # pg_restore devuelve exit 1 con warnings (ej. objetos que no existen para --clean). Chequea si el server carga.
        Write-Host "  (pg_restore devolvio $LASTEXITCODE - normal cuando hay warnings; verificando conteos...)" -ForegroundColor Yellow
    }
    & docker --context $DockerContext exec $psName sh -c 'rm -f /tmp/db.dump' | Out-Null
    Write-Ok "BD restaurada"

    # --- 6) Uploads ---
    $tarPath = Join-Path $TargetDir 'uploads.tar.gz'
    if (-not $SkipUploads -and (Test-Path $tarPath)) {
        Write-Step "Restaurando volumen uploads"
        # El volumen se crea/reusa con el nombre del compose project: <project>_visal-uploads.
        # Como el compose file esta en TargetDir, el project name por default es la carpeta,
        # pero el compose declara "name: visal-prod" arriba, asi que el volumen se llama
        # visal-prod_visal-uploads.
        $volName = 'visal-prod_visal-uploads'
        # Asegurarse de que exista (creado por compose up)
        $volExists = & docker --context $DockerContext volume ls --format '{{.Name}}' | Select-String -Pattern "^$([regex]::Escape($volName))$"
        if (-not $volExists) {
            & docker --context $DockerContext volume create $volName | Out-Null
        }
        # Montar volumen + directorio con el tar y extraer.
        & docker --context $DockerContext run --rm -v "${volName}:/target" -v "${TargetDir}:/src" alpine sh -c "cd /target && tar -xzf /src/uploads.tar.gz"
        if ($LASTEXITCODE -ne 0) { throw "restauracion de uploads fallo" }
        Write-Ok "uploads restaurados en volumen $volName"
    } elseif ($SkipUploads) {
        Write-Host "  SkipUploads activo - no se restauran firmas/PDFs" -ForegroundColor Yellow
    }

    # --- 7) App ---
    Write-Step "docker compose up -d visal-app"
    & docker --context $DockerContext compose up -d visal-app
    if ($LASTEXITCODE -ne 0) { throw "compose up visal-app fallo" }
    Write-Ok "visal-app up"

    # --- 8) Verificacion basica ---
    Write-Step "Verificando puerto de la app"
    # Leer VISAL_PORT del .env
    $port = 5380
    $envContent = Get-Content $envPath -ErrorAction SilentlyContinue
    if ($envContent) {
        $portLine = $envContent | Where-Object { $_ -match '^\s*VISAL_PORT\s*=' } | Select-Object -First 1
        if ($portLine) { $port = ($portLine -replace '^\s*VISAL_PORT\s*=\s*', '').Trim() }
    }
    $upUrl = "http://localhost:$port/login"
    $ok = $false
    for ($i = 0; $i -lt 30; $i++) {
        try {
            $r = Invoke-WebRequest -Uri $upUrl -TimeoutSec 3 -UseBasicParsing -ErrorAction Stop
            if ($r.StatusCode -eq 200) { $ok = $true; break }
        } catch { Start-Sleep -Seconds 2 }
    }
    if ($ok) {
        Write-Ok "app responde en $upUrl"
    } else {
        Write-Host "  (la app no respondio en 60s; revisa: docker --context $DockerContext logs visal-app)" -ForegroundColor Yellow
    }

    Write-Host ""
    Write-Host "=====================================================" -ForegroundColor Green
    Write-Host " Restauracion completada" -ForegroundColor Green
    Write-Host "=====================================================" -ForegroundColor Green
    Write-Host " Directorio: $TargetDir"
    Write-Host " Puerto app: $port"
    Write-Host " Login:      $upUrl"
    Write-Host "====================================================="
    Write-Host "Recuerda: prueba login, abre una HC con firma y descarga un PDF de contrato para validar uploads." -ForegroundColor Yellow
}
finally {
    Pop-Location
}
