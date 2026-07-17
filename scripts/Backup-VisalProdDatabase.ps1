<#
.SYNOPSIS
    Baja un respaldo logico (pg_dumpall) del postgres de PRODUCCION de Visal
    conectando por SSH al server remoto y guarda el archivo comprimido en
    D:\Backups\Produccion\Visal, con retencion por dias.

.DESCRIPTION
    - SSH al server prod (root@10.0.0.3 con la llave id_ed25519_visal).
    - Ejecuta `docker exec visal-postgres-prod pg_dumpall -U <USER>` dentro
      del servidor y comprime el stream con gzip antes de bajarlo.
    - Guarda el .sql.gz en D:\Backups\Produccion\Visal\{yyyy-MM-dd_HHmmss}\.
    - No escribe passwords: pg_dumpall se autentica por trust local dentro
      del propio contenedor postgres (mismo mecanismo del script local).
    - Retencion: borra carpetas mas viejas que N dias.

.NOTES
    Se ejecuta como el usuario que corre la tarea programada; ese usuario
    necesita tener la llave privada en %USERPROFILE%\.ssh\id_ed25519_visal
    y el host key de 10.0.0.3 aceptado en known_hosts (basta con haber
    hecho un `ssh root@10.0.0.3` interactivo una vez).
#>

[CmdletBinding()]
param(
    [string]$BackupRoot     = "D:\Backups\Produccion\Visal",
    [string]$RemoteHost     = "10.0.0.3",
    [string]$RemoteUser     = "root",
    [string]$SshKey         = "$env:USERPROFILE\.ssh\id_ed25519_visal",
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

$outGz = Join-Path $dayFolder "$ContainerName.sql.gz"

# Reglas de negocio:
#  - pg_dumpall corre dentro del contenedor (autenticacion por trust del socket local).
#  - Comprimimos con gzip -9 dentro del server ANTES de bajar por SSH — asi la red
#    transporta ya el stream comprimido (mucho mas rapido en WAN).
#  - StrictHostKeyChecking=accept-new evita prompts pero sigue rechazando MITM tras
#    la primera conexion.
$remoteCmd = "docker exec $ContainerName pg_dumpall -U $PgUser --clean --if-exists | gzip -9"
Write-Log "Ejecutando remoto: $remoteCmd"

try {
    # -T deshabilita alocar TTY (imprescindible para stream binario).
    # -o BatchMode=yes falla en vez de esperar prompt de password.
    & $sshExe -i $SshKey `
        -o BatchMode=yes `
        -o StrictHostKeyChecking=accept-new `
        -o ConnectTimeout=15 `
        -T "$RemoteUser@$RemoteHost" $remoteCmd `
        > $outGz
    if ($LASTEXITCODE -ne 0) { throw "ssh/pg_dumpall salio con codigo $LASTEXITCODE" }
} catch {
    Write-Log "ERROR bajando dump: $($_.Exception.Message)" "ERROR"
    exit 1
}

# Sanity check: el .gz debe pesar mas que una cabecera gzip vacia.
$sizeBytes = (Get-Item $outGz).Length
if ($sizeBytes -lt 200) {
    Write-Log "ERROR: archivo demasiado pequeno ($sizeBytes bytes). Posible dump vacio." "ERROR"
    exit 1
}

$sizeMB = [math]::Round($sizeBytes / 1MB, 2)
Write-Log "OK -> $ContainerName.sql.gz ($sizeMB MB)"

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
