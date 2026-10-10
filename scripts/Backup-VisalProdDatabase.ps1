<#
.SYNOPSIS
    Baja un respaldo logico (pg_dumpall) del postgres de PRODUCCION de Visal
    conectando por SSH al server remoto y guarda el archivo comprimido en
    D:\Backups\Produccion\Visal, con retencion por dias.

.DESCRIPTION
    - SSH al server prod (root@10.0.0.4 con la llave id_ed25519_visal_test).
    - Ejecuta `docker exec visal-postgres-prod pg_dumpall -U <USER> | gzip` y
      escribe el .gz a un temporal EN EL SERVER (redireccion en bash = binary-safe),
      lo valida con `gzip -t`, y lo BAJA con scp (tambien binary-safe). NO usa la
      redireccion '>' de PowerShell, que corrompia el binario (BOM UTF-16 + re-encode).
    - Guarda el .sql.gz en D:\Backups\Produccion\Visal\{yyyy-MM-dd_HHmmss}\ y valida
      la cabecera gzip (magic bytes) tras la descarga.
    - No escribe passwords: pg_dumpall se autentica por trust local dentro
      del propio contenedor postgres (mismo mecanismo del script local).
    - Retencion: borra carpetas mas viejas que N dias.

.NOTES
    Se ejecuta como el usuario que corre la tarea programada; ese usuario
    necesita tener la llave privada en %USERPROFILE%\.ssh\id_ed25519_visal_test
    y el host key de 10.0.0.4 aceptado en known_hosts (basta con haber
    hecho un `ssh root@10.0.0.4` interactivo una vez).
#>

[CmdletBinding()]
param(
    [string]$BackupRoot     = "D:\Backups\Produccion\Visal",
    # PRODUCCION real = 10.0.0.4 con la llave id_ed25519_visal_test. (Antes apuntaba
    # a 10.0.0.3, que es PRUEBAS: el respaldo "de prod" traia la BD de test.)
    [string]$RemoteHost     = "10.0.0.4",
    [string]$RemoteUser     = "root",
    [string]$SshKey         = "$env:USERPROFILE\.ssh\id_ed25519_visal_test",
    [string]$ContainerName  = "visal-postgres-prod",
    [string]$PgUser         = "visal",
    [int]$RetentionDays     = 30
)

$ErrorActionPreference = "Stop"

$stamp     = Get-Date -Format "yyyy-MM-dd_HHmmss"
$dayFolder = Join-Path $BackupRoot $stamp
$logFile   = Join-Path $dayFolder "backup-prod.log"

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $line = "{0} [{1}] {2}" -f (Get-Date -Format "HH:mm:ss"), $Level, $Message
    Write-Host $line
    if (Test-Path $dayFolder) { Add-Content -Path $logFile -Value $line -Encoding utf8 }
}

# Verificaciones minimas.
if (-not (Test-Path $SshKey)) {
    Write-Host "ERROR: No existe la llave SSH $SshKey" -ForegroundColor Red
    exit 1
}

$sshCmd = Get-Command ssh -ErrorAction SilentlyContinue
if (-not $sshCmd) {
    Write-Host "ERROR: No se encontro ssh.exe en el PATH." -ForegroundColor Red
    exit 1
}
$sshExe = $sshCmd.Source

New-Item -ItemType Directory -Force -Path $dayFolder | Out-Null
Write-Log "Inicio de respaldo prod. Server: $RemoteUser@$RemoteHost  Container: $ContainerName"
Write-Log "Destino: $dayFolder"

$outGz     = Join-Path $dayFolder "$ContainerName.sql.gz"
$remoteTmp = "/tmp/visal_backup_$stamp.sql.gz"

# Reglas de negocio:
#  - pg_dumpall corre dentro del contenedor (autenticacion por trust del socket local).
#  - El dump+gzip+redireccion a archivo ocurre ENTERO en el server (bash remoto), que
#    SI es binary-safe. NO se usa la redireccion '>' de PowerShell, que trataba el
#    stream gzip como texto (BOM UTF-16 + re-encode) y dejaba los .gz CORRUPTOS e
#    irrestaurables (bug historico). La descarga se hace con scp, tambien binary-safe.
#  - set -o pipefail: si pg_dumpall falla, el pipe falla (no deja un .gz vacio "ok").
#  - gzip -t valida la integridad del .gz EN EL SERVER antes de bajarlo.
#  - StrictHostKeyChecking=accept-new evita prompts pero sigue rechazando MITM tras
#    la primera conexion.
$remoteCmd = "set -o pipefail; docker exec $ContainerName pg_dumpall -U $PgUser --clean --if-exists | gzip -9 > $remoteTmp && gzip -t $remoteTmp && stat -c%s $remoteTmp"
Write-Log "Ejecutando remoto (dump + gzip + verify): $remoteCmd"

try {
    # -T deshabilita alocar TTY. -o BatchMode=yes falla en vez de esperar password.
    $remoteSize = & $sshExe -i $SshKey `
        -o BatchMode=yes `
        -o StrictHostKeyChecking=accept-new `
        -o ConnectTimeout=15 `
        -T "$RemoteUser@$RemoteHost" $remoteCmd
    if ($LASTEXITCODE -ne 0) { throw "dump/gzip/verify remoto salio con codigo $LASTEXITCODE" }
    Write-Log "Dump remoto OK y verificado (gzip -t). Tamanio remoto: $remoteSize bytes"
} catch {
    Write-Log "ERROR en dump remoto: $($_.Exception.Message)" "ERROR"
    & $sshExe -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new -T "$RemoteUser@$RemoteHost" "rm -f $remoteTmp" 2>$null | Out-Null
    exit 1
}

# Descarga binary-safe con scp (no re-codifica). Luego se borra el temporal remoto.
try {
    & scp -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new "${RemoteUser}@${RemoteHost}:$remoteTmp" "$outGz"
    if ($LASTEXITCODE -ne 0) { throw "scp salio con codigo $LASTEXITCODE" }
} catch {
    Write-Log "ERROR bajando dump por scp: $($_.Exception.Message)" "ERROR"
    & $sshExe -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new -T "$RemoteUser@$RemoteHost" "rm -f $remoteTmp" 2>$null | Out-Null
    exit 1
}
& $sshExe -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new -T "$RemoteUser@$RemoteHost" "rm -f $remoteTmp" 2>$null | Out-Null

# Sanity check 1: tamanio minimo.
$sizeBytes = (Get-Item $outGz).Length
if ($sizeBytes -lt 200) {
    Write-Log "ERROR: archivo demasiado pequeno ($sizeBytes bytes). Posible dump vacio." "ERROR"
    exit 1
}

# Sanity check 2: cabecera gzip (magic bytes 1F 8B). Detecta cualquier corrupcion de
# descarga/encoding sin cargar el archivo entero en memoria.
$fs = [System.IO.File]::OpenRead($outGz)
try { $b0 = $fs.ReadByte(); $b1 = $fs.ReadByte() } finally { $fs.Dispose() }
if ($b0 -ne 0x1F -or $b1 -ne 0x8B) {
    Write-Log ("ERROR: el .gz no tiene cabecera gzip valida (bytes {0:X2} {1:X2}). Backup corrupto." -f $b0, $b1) "ERROR"
    exit 1
}

$sizeMB = [math]::Round($sizeBytes / 1MB, 2)
Write-Log "OK -> $ContainerName.sql.gz ($sizeMB MB), cabecera gzip valida."

# Retencion.
if ($RetentionDays -gt 0) {
    $limite = (Get-Date).AddDays(-$RetentionDays)
    Get-ChildItem $BackupRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt $limite } |
        ForEach-Object {
            Write-Log "Retencion: eliminando respaldo viejo $($_.Name)"
            Remove-Item $_.FullName -Recurse -Force
        }
}

Write-Log "Respaldo prod finalizado correctamente."
