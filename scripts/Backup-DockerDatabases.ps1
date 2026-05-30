<#
.SYNOPSIS
    Recorre los contenedores Docker en ejecucion, detecta el motor de base de datos
    de cada uno y genera un respaldo logico (dump) por contenedor en el disco D:,
    organizado por fecha.

.DESCRIPTION
    - Detecta el motor por la imagen del contenedor (postgres, mysql/mariadb, mongo, redis).
    - Lee las credenciales desde las variables de entorno del propio contenedor
      (POSTGRES_USER/PASSWORD, MYSQL_ROOT_PASSWORD, etc.), no se escriben aqui.
    - Postgres  -> pg_dumpall (todas las bases del cluster, incluye roles).
    - MySQL/MariaDB -> mysqldump --all-databases.
    - Mongo     -> mongodump (archivo comprimido).
    - Redis     -> fuerza SAVE y copia el dump.rdb.
    - Cada respaldo se comprime a .zip cuando aplica y se guarda con la fecha.
    - Escribe un log de la corrida y aplica retencion (borra respaldos viejos).

.NOTES
    Requiere Docker Desktop ENCENDIDO y los contenedores corriendo
    (un dump logico necesita la base activa).
    No necesita permisos de administrador.
#>

[CmdletBinding()]
param(
    # Carpeta raiz donde se guardan los respaldos.
    [string]$BackupRoot = "D:\Backups\Docker",

    # Dias de retencion: carpetas de respaldo mas viejas que esto se eliminan. 0 = no borrar.
    [int]$RetentionDays = 30
)

$ErrorActionPreference = "Stop"

# --- Marca de tiempo de la corrida (sin Date.now prohibido: aqui es PowerShell real) ---
$stamp     = Get-Date -Format "yyyy-MM-dd_HHmmss"
$dayFolder = Join-Path $BackupRoot $stamp
$logFile   = Join-Path $dayFolder "backup.log"

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $line = "{0} [{1}] {2}" -f (Get-Date -Format "HH:mm:ss"), $Level, $Message
    Write-Host $line
    if (Test-Path $dayFolder) { Add-Content -Path $logFile -Value $line }
}

# --- Verificar que Docker responde ---
try {
    docker info *> $null
    if ($LASTEXITCODE -ne 0) { throw "docker info devolvio codigo $LASTEXITCODE" }
} catch {
    Write-Host "ERROR: Docker no responde. Abre Docker Desktop y vuelve a intentar." -ForegroundColor Red
    exit 1
}

# --- Crear carpeta de la corrida ---
New-Item -ItemType Directory -Force -Path $dayFolder | Out-Null
Write-Log "Inicio de respaldo. Carpeta: $dayFolder"

# --- Listar contenedores en ejecucion ---
$ids = docker ps --format "{{.ID}}"
if (-not $ids) {
    Write-Log "No hay contenedores en ejecucion. Nada que respaldar." "WARN"
    exit 0
}

# Helper: obtener una variable de entorno de un contenedor (o valor por defecto)
function Get-ContainerEnv {
    param([string]$Id, [string]$Name, [string]$Default = "")
    $envs = docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' $Id
    foreach ($e in $envs) {
        if ($e -like "$Name=*") { return $e.Substring($Name.Length + 1) }
    }
    return $Default
}

# Helper: nombre de archivo seguro
function Get-SafeName { param([string]$s) ($s -replace '[^\w\.\-]', '_') }

$summary = @()

foreach ($id in $ids) {
    $name  = (docker inspect --format '{{.Name}}' $id).TrimStart('/')
    $image = docker inspect --format '{{.Config.Image}}' $id
    $safe  = Get-SafeName $name
    $img   = $image.ToLower()

    Write-Log "Contenedor: $name  (imagen: $image)"

    try {
        # ---------------- POSTGRES ----------------
        if ($img -match 'postgres|postgis|timescale') {
            $user = Get-ContainerEnv $id 'POSTGRES_USER' 'postgres'
            $pass = Get-ContainerEnv $id 'POSTGRES_PASSWORD'
            $out  = Join-Path $dayFolder "$safe.sql"
            Write-Log "  -> pg_dumpall como usuario '$user'"
            # PGPASSWORD se pasa al proceso del contenedor, no se loguea.
            docker exec -e PGPASSWORD=$pass $id pg_dumpall -U $user | Out-File -FilePath $out -Encoding utf8
            if ($LASTEXITCODE -ne 0) { throw "pg_dumpall fallo" }
            Compress-Archive -Path $out -DestinationPath "$out.zip" -Force
            Remove-Item $out -Force
            $size = [math]::Round((Get-Item "$out.zip").Length/1MB, 2)
            Write-Log "  OK -> $safe.sql.zip ($size MB)"
            $summary += [pscustomobject]@{ Contenedor=$name; Motor='postgres'; Estado='OK'; MB=$size }
        }
        # ---------------- MYSQL / MARIADB ----------------
        elseif ($img -match 'mysql|mariadb') {
            $pass = Get-ContainerEnv $id 'MYSQL_ROOT_PASSWORD'
            if (-not $pass) { $pass = Get-ContainerEnv $id 'MARIADB_ROOT_PASSWORD' }
            $out = Join-Path $dayFolder "$safe.sql"
            Write-Log "  -> mysqldump --all-databases"
            docker exec -e MYSQL_PWD=$pass $id sh -c 'exec mysqldump -uroot --all-databases --single-transaction --routines --triggers' | Out-File -FilePath $out -Encoding utf8
            if ($LASTEXITCODE -ne 0) { throw "mysqldump fallo" }
            Compress-Archive -Path $out -DestinationPath "$out.zip" -Force
            Remove-Item $out -Force
            $size = [math]::Round((Get-Item "$out.zip").Length/1MB, 2)
            Write-Log "  OK -> $safe.sql.zip ($size MB)"
            $summary += [pscustomobject]@{ Contenedor=$name; Motor='mysql'; Estado='OK'; MB=$size }
        }
        # ---------------- MONGODB ----------------
        elseif ($img -match 'mongo') {
            $user = Get-ContainerEnv $id 'MONGO_INITDB_ROOT_USERNAME'
            $pass = Get-ContainerEnv $id 'MONGO_INITDB_ROOT_PASSWORD'
            $out  = Join-Path $dayFolder "$safe.archive.gz"
            $auth = if ($user) { "--username `"$user`" --password `"$pass`" --authenticationDatabase admin" } else { "" }
            Write-Log "  -> mongodump (archive gzip)"
            docker exec $id sh -c "exec mongodump $auth --archive --gzip" | Set-Content -Path $out -Encoding Byte
            if ($LASTEXITCODE -ne 0) { throw "mongodump fallo" }
            $size = [math]::Round((Get-Item $out).Length/1MB, 2)
            Write-Log "  OK -> $safe.archive.gz ($size MB)"
            $summary += [pscustomobject]@{ Contenedor=$name; Motor='mongo'; Estado='OK'; MB=$size }
        }
        # ---------------- REDIS ----------------
        elseif ($img -match 'redis') {
            $out = Join-Path $dayFolder "$safe-dump.rdb"
            Write-Log "  -> SAVE + copia de dump.rdb"
            docker exec $id sh -c 'redis-cli SAVE > /dev/null 2>&1 || true'
            # Ubicacion tipica del rdb dentro del contenedor
            docker cp "${id}:/data/dump.rdb" $out 2>$null
            if (Test-Path $out) {
                $size = [math]::Round((Get-Item $out).Length/1MB, 2)
                Write-Log "  OK -> $safe-dump.rdb ($size MB)"
                $summary += [pscustomobject]@{ Contenedor=$name; Motor='redis'; Estado='OK'; MB=$size }
            } else {
                Write-Log "  Redis sin dump.rdb (probablemente sin persistencia). Omitido." "WARN"
                $summary += [pscustomobject]@{ Contenedor=$name; Motor='redis'; Estado='OMITIDO'; MB=0 }
            }
        }
        # ---------------- SIN BASE DE DATOS RECONOCIDA ----------------
        else {
            Write-Log "  Sin motor de BD reconocido. Omitido." "INFO"
        }
    } catch {
        Write-Log "  ERROR respaldando $name : $($_.Exception.Message)" "ERROR"
        $summary += [pscustomobject]@{ Contenedor=$name; Motor='?'; Estado='ERROR'; MB=0 }
    }
}

# --- Resumen ---
Write-Log "----- RESUMEN -----"
$summary | Format-Table -AutoSize | Out-String | ForEach-Object { Write-Log $_ }

$totalMB = ($summary | Measure-Object -Property MB -Sum).Sum
Write-Log ("Total respaldado: {0} MB en {1}" -f [math]::Round($totalMB,2), $dayFolder)

# --- Retencion: borrar carpetas viejas ---
if ($RetentionDays -gt 0) {
    $limite = (Get-Date).AddDays(-$RetentionDays)
    Get-ChildItem $BackupRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt $limite } |
        ForEach-Object {
            Write-Log "Retencion: eliminando respaldo viejo $($_.Name)"
            Remove-Item $_.FullName -Recurse -Force
        }
}

Write-Log "Respaldo finalizado correctamente."
