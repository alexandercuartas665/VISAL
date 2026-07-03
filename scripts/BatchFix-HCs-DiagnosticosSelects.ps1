# BatchFix-HCs-DiagnosticosSelects.ps1
# Aplica Fix-HCFO08-DiagnosticosSelects a TODAS las HCs con tabla 'diagnosticos'.
# Antes de tocar cada HC, exporta un backup completo del schema_json al directorio
# scratchpad/backups_diagnosticos_<timestamp>/. Si algo sale mal se puede restaurar
# desde ahi con psql copy directa.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev",
    [string]$BackupRoot  = "C:\Users\acuartas\AppData\Local\Temp\claude\C--DesarrolloIA-Visal\3a114262-030a-4135-852f-4f6e57a10abf\scratchpad"
)
$ErrorActionPreference = "Stop"

$stamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
$backupDir = Join-Path $BackupRoot ("backups_diagnosticos_$stamp")
[System.IO.Directory]::CreateDirectory($backupDir) | Out-Null
Write-Host "Backups a: $backupDir" -ForegroundColor Cyan

# Lista HCs con tabla llamada 'diagnosticos' (cualquier case)
$listado = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT DISTINCT f.codigo FROM form_definitions f, jsonb_array_elements(f.schema_json->'children') sec, jsonb_array_elements(sec->'children') c WHERE f.tipo='HISTORIA CLINICA' AND f.tenant_id='$TenantId' AND c->>'fieldType'='table' AND LOWER(c->>'name')='diagnosticos' ORDER BY f.codigo;"
$codigos = $listado -split "`n" | Where-Object { $_ -ne "" }
Write-Host ("HCs objetivo ({0}): {1}" -f $codigos.Count, ($codigos -join ", ")) -ForegroundColor Cyan

$fixScript = Join-Path (Split-Path $PSCommandPath) "Fix-HCFO08-DiagnosticosSelects.ps1"
$hechos = @(); $omitidos = @(); $errores = @()

foreach ($codigo in $codigos) {
    Write-Host ""
    Write-Host "=== $codigo ===" -ForegroundColor White

    # BACKUP
    $bkPath = Join-Path $backupDir "$codigo.json"
    $raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$codigo' AND tenant_id='$TenantId';"
    if ([string]::IsNullOrWhiteSpace($raw)) {
        Write-Host "[$codigo] backup vacio, salto" -ForegroundColor Yellow
        $omitidos += $codigo
        continue
    }
    [System.IO.File]::WriteAllText($bkPath, $raw, [System.Text.UTF8Encoding]::new($false))
    Write-Host "  backup OK ($((Get-Item $bkPath).Length) bytes)" -ForegroundColor DarkGray

    # APLICAR
    try {
        & $fixScript -Codigo $codigo -TenantId $TenantId -PgContainer $PgContainer -PgUser $PgUser -PgDb $PgDb
        $hechos += $codigo
    } catch {
        Write-Host "  ERROR: $_" -ForegroundColor Red
        $errores += "$codigo : $_"
    }
}

Write-Host ""
Write-Host "==================== RESUMEN ====================" -ForegroundColor Cyan
Write-Host ("Actualizados : {0}" -f $hechos.Count) -ForegroundColor Green
$hechos | ForEach-Object { Write-Host "  $_" }
if ($omitidos.Count -gt 0) {
    Write-Host ("Omitidos     : {0}" -f $omitidos.Count) -ForegroundColor Yellow
    $omitidos | ForEach-Object { Write-Host "  $_" }
}
if ($errores.Count -gt 0) {
    Write-Host ("Errores      : {0}" -f $errores.Count) -ForegroundColor Red
    $errores | ForEach-Object { Write-Host "  $_" }
}
Write-Host ""
Write-Host "Restore individual: psql UPDATE con jsonb literal desde $backupDir\<codigo>.json" -ForegroundColor DarkGray
