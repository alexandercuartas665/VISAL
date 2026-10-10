<#
.SYNOPSIS
    Respaldo INCREMENTAL de los archivos subidos (uploads) de PRODUCCION de Visal
    (firmas, PDFs de autorizacion, documentos de notas, branding, tableros) a tu PC.

.DESCRIPTION
    El volumen Docker `visal-uploads` (/app/wwwroot/uploads, ~2.8 GB) NO lo captura el
    backup de BD (pg_dumpall). Este script lo respalda aparte.

    Como el PC no tiene rsync, se usa el INCREMENTAL nativo de GNU tar
    (--listed-incremental): se mantiene un archivo "snapshot" (uploads.snar) EN EL
    SERVER; la primera corrida (o con -Full) baja TODO (nivel 0), y las siguientes
    bajan SOLO lo nuevo/cambiado (deltas pequenos). La descarga es binary-safe (scp).

    Flujo:
      1) SSH a prod (10.0.0.4, llave id_ed25519_visal_test).
      2) tar --listed-incremental del volumen -> /tmp/uploads-<stamp>.tar.gz en el server.
      3) Verifica el .tar.gz con gzip -t EN EL SERVER.
      4) Lo baja con scp a D:\Backups\Produccion\Visal-uploads\<stamp>\ y borra el temporal remoto.
      5) Valida magic gzip (1F 8B) tras la descarga.

    RESTAURAR (en orden): extraer el FULL (nivel 0) y luego cada INCREMENTAL por fecha:
      tar --listed-incremental=/dev/null -xzf uploads-<fecha-full>-full.tar.gz   -C <destino>
      tar --listed-incremental=/dev/null -xzf uploads-<fecha+1>-incr.tar.gz      -C <destino>
      ... (aplicar todos los incr de la cadena, en orden cronologico)

.NOTES
    Recomendado: correr con -Full periodicamente (p. ej. semanal) para empezar una
    cadena nueva (chain corta = restore mas simple), e incremental el resto de dias.
    No hay borrado automatico de respaldos viejos para no romper una cadena por error;
    cuando arranques una cadena nueva con -Full, puedes borrar a mano las carpetas de
    la cadena anterior.
#>

[CmdletBinding()]
param(
    [string]$BackupRoot    = "D:\Backups\Produccion\Visal-uploads",
    [string]$RemoteHost    = "10.0.0.4",
    [string]$RemoteUser    = "root",
    [string]$SshKey        = "$env:USERPROFILE\.ssh\id_ed25519_visal_test",
    # Ruta del _data del volumen Docker en el server.
    [string]$VolumeData    = "/var/lib/docker/volumes/visal-prod_visal-uploads/_data",
    [string]$SnarPath      = "/opt/visal/backups/uploads.snar",
    # Fuerza nivel 0 (cadena nueva): borra el .snar antes de empacar.
    [switch]$Full
)

$ErrorActionPreference = "Stop"

$stamp     = Get-Date -Format "yyyy-MM-dd_HHmmss"
$dayFolder = Join-Path $BackupRoot $stamp
$logFile   = Join-Path $dayFolder "backup-uploads.log"

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $line = "{0} [{1}] {2}" -f (Get-Date -Format "HH:mm:ss"), $Level, $Message
    Write-Host $line
    if (Test-Path $dayFolder) { Add-Content -Path $logFile -Value $line -Encoding utf8 }
}

if (-not (Test-Path $SshKey)) { Write-Host "ERROR: no existe la llave SSH $SshKey" -ForegroundColor Red; exit 1 }
$sshExe = (Get-Command ssh -ErrorAction SilentlyContinue).Source
if (-not $sshExe) { Write-Host "ERROR: no se encontro ssh.exe" -ForegroundColor Red; exit 1 }

New-Item -ItemType Directory -Force -Path $dayFolder | Out-Null

# Determinar nivel: full si -Full, o si aun no existe el .snar en el server.
$snarExiste = (& $sshExe -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new -T "$RemoteUser@$RemoteHost" "test -f $SnarPath && echo yes || echo no").Trim()
$esFull = $Full -or ($snarExiste -ne "yes")
$nivel  = if ($esFull) { "full" } else { "incr" }
$remoteTmp = "/tmp/uploads-$stamp-$nivel.tar.gz"
$outFile   = Join-Path $dayFolder "uploads-$stamp-$nivel.tar.gz"

Write-Log "Inicio respaldo uploads prod. Server: $RemoteUser@$RemoteHost  Nivel: $nivel"
Write-Log "Volumen: $VolumeData  Snar: $SnarPath  Destino: $dayFolder"

# Si -Full, resetea la cadena borrando el snapshot previo.
$resetSnar = if ($esFull) { "rm -f $SnarPath; " } else { "" }

# tar incremental -> /tmp del server; verifica con gzip -t; reporta tamanio.
$remoteCmd = "set -o pipefail; mkdir -p /opt/visal/backups; " +
             "${resetSnar}tar --listed-incremental=$SnarPath -czf $remoteTmp -C $VolumeData . && gzip -t $remoteTmp && stat -c%s $remoteTmp"

Write-Log "Empacando en el server (tar incremental + verify)..."
try {
    $remoteSize = & $sshExe -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new -o ConnectTimeout=15 -T "$RemoteUser@$RemoteHost" $remoteCmd
    if ($LASTEXITCODE -ne 0) { throw "tar/gzip/verify remoto salio con codigo $LASTEXITCODE" }
    Write-Log "Tar remoto OK y verificado. Tamanio remoto: $remoteSize bytes"
} catch {
    Write-Log "ERROR empacando en el server: $($_.Exception.Message)" "ERROR"
    & $sshExe -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new -T "$RemoteUser@$RemoteHost" "rm -f $remoteTmp" 2>$null | Out-Null
    exit 1
}

Write-Log "Descargando por scp (binary-safe)..."
try {
    & scp -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new "${RemoteUser}@${RemoteHost}:$remoteTmp" "$outFile"
    if ($LASTEXITCODE -ne 0) { throw "scp salio con codigo $LASTEXITCODE" }
} catch {
    Write-Log "ERROR bajando por scp: $($_.Exception.Message)" "ERROR"
    & $sshExe -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new -T "$RemoteUser@$RemoteHost" "rm -f $remoteTmp" 2>$null | Out-Null
    exit 1
}
& $sshExe -i $SshKey -o BatchMode=yes -o StrictHostKeyChecking=accept-new -T "$RemoteUser@$RemoteHost" "rm -f $remoteTmp" 2>$null | Out-Null

# Validacion: tamanio + magic gzip.
$sizeBytes = (Get-Item $outFile).Length
if ($sizeBytes -lt 100) { Write-Log "ERROR: archivo demasiado pequeno ($sizeBytes bytes)." "ERROR"; exit 1 }
$fs = [System.IO.File]::OpenRead($outFile)
try { $b0 = $fs.ReadByte(); $b1 = $fs.ReadByte() } finally { $fs.Dispose() }
if ($b0 -ne 0x1F -or $b1 -ne 0x8B) {
    Write-Log ("ERROR: no es gzip valido (bytes {0:X2} {1:X2})." -f $b0, $b1) "ERROR"; exit 1
}
$sizeMB = [math]::Round($sizeBytes / 1MB, 2)
Write-Log "OK -> uploads-$stamp-$nivel.tar.gz ($sizeMB MB), cabecera gzip valida."
Write-Log "Respaldo de uploads finalizado correctamente."
