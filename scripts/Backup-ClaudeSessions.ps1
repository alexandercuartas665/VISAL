<#
.SYNOPSIS
    Respalda las transcripciones de las sesiones de Claude Code -> D:\Backups\Claude

.DESCRIPTION
    Las sesiones de Claude Code viven SOLO en disco local, en
        C:\Users\<user>\.claude\projects\<proyecto>\<session-id>.jsonl
    Si se pierde el equipo o se borra esa carpeta, se pierde todo el historial de trabajo
    (decisiones, contexto, comandos). No hay copia en la nube por defecto.

    Este script comprime las sesiones de CADA proyecto en su propio .zip dentro de una
    carpeta por dia, escribe un log y aplica retencion, igual que los demas respaldos.

    Las transcripciones crecen mucho (una sola sesion larga puede pasar de 200 MB), por eso
    se comprimen: el texto baja alrededor del 60%.
#>

[CmdletBinding()]
param(
    # Raiz de los respaldos de Claude.
    [string]$BackupRoot = "D:\Backups\Claude",

    # Carpeta de sesiones de Claude Code.
    [string]$SourceRoot = (Join-Path $env:USERPROFILE ".claude\projects"),

    # Dias de retencion: carpetas de respaldo mas viejas que esto se eliminan. 0 = no borrar.
    [int]$RetentionDays = 30
)

$ErrorActionPreference = "Stop"

$dayFolder = Join-Path $BackupRoot (Get-Date -Format "yyyy-MM-dd")
$logFile = Join-Path $BackupRoot "backup-claude.log"

function Write-Log {
    param([string]$Message, [string]$Level = "INFO")
    $line = "{0} [{1}] {2}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Level, $Message
    Write-Host $line
    try { Add-Content -Path $logFile -Value $line -ErrorAction SilentlyContinue } catch { }
}

if (-not (Test-Path $SourceRoot)) {
    New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null
    Write-Log "No existe la carpeta de sesiones: $SourceRoot" "ERROR"
    exit 1
}

New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null
New-Item -ItemType Directory -Force -Path $dayFolder | Out-Null

Write-Log "=== Respaldo de sesiones de Claude Code ==="
Write-Log "Origen : $SourceRoot"
Write-Log "Destino: $dayFolder"

$totalOrigenMB = 0
$totalZipMB = 0
$proyectos = 0
$errores = 0

foreach ($carpeta in (Get-ChildItem -Path $SourceRoot -Directory -ErrorAction SilentlyContinue)) {
    $jsonl = Get-ChildItem -Path $carpeta.FullName -Filter *.jsonl -File -ErrorAction SilentlyContinue
    if (-not $jsonl) { continue }

    $mb = [math]::Round(($jsonl | Measure-Object Length -Sum).Sum / 1MB, 2)
    $zip = Join-Path $dayFolder ("{0}.zip" -f $carpeta.Name)

    try {
        Compress-Archive -Path $jsonl.FullName -DestinationPath $zip -CompressionLevel Optimal -Force
        $zipMb = [math]::Round((Get-Item $zip).Length / 1MB, 2)
        $totalOrigenMB += $mb
        $totalZipMB += $zipMb
        $proyectos++
        Write-Log ("{0}: {1} sesion(es), {2} MB -> {3} MB" -f $carpeta.Name, $jsonl.Count, $mb, $zipMb)
    } catch {
        $errores++
        Write-Log ("Fallo al comprimir {0}: {1}" -f $carpeta.Name, $_.Exception.Message) "ERROR"
    }
}

if ($proyectos -eq 0) {
    Write-Log "No se encontraron sesiones para respaldar." "WARN"
}

Write-Log ("Total: {0} proyecto(s), {1} MB -> {2} MB comprimido" -f $proyectos, [math]::Round($totalOrigenMB,2), [math]::Round($totalZipMB,2))

# --- Retencion: borrar carpetas de dias viejos ---
if ($RetentionDays -gt 0) {
    $limite = (Get-Date).AddDays(-$RetentionDays)
    Get-ChildItem $BackupRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt $limite } |
        ForEach-Object {
            Write-Log "Retencion: eliminando respaldo viejo $($_.Name)"
            Remove-Item $_.FullName -Recurse -Force
        }
}

if ($errores -gt 0) {
    Write-Log "Terminado CON ERRORES ($errores)." "ERROR"
    exit 1
}

Write-Log "Terminado OK."
exit 0
