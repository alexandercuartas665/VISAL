# Normalize-HCs-DiagnosticosColumns.ps1
# Iguala la tabla 'diagnosticos' de TODAS las HCs a la estructura de HC-FO-08:
#   4 columnas: Diagnostico (CIE-11 autocomplete) | Origen | Tipo | Relacion
# - Preserva la columna Diagnostico existente (id, catalog, name original).
# - Reemplaza Origen y Tipo por las de HC-FO-08 (misma name/label/options).
# - Agrega la columna Relacion si no existe.
# Backup automatico a scratchpad/backups_normalize_diag_<timestamp>/<codigo>.json

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev",
    [string]$BackupRoot  = "C:\Users\acuartas\AppData\Local\Temp\claude\C--DesarrolloIA-Visal\3a114262-030a-4135-852f-4f6e57a10abf\scratchpad"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

$stamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
$backupDir = Join-Path $BackupRoot ("backups_normalize_diag_$stamp")
[System.IO.Directory]::CreateDirectory($backupDir) | Out-Null
Write-Host "Backups a: $backupDir" -ForegroundColor Cyan

# ============ Definiciones canonicas (mismas de HC-FO-08) ============
$origenOptions = @(
    "ENFERMEDAD GENERAL","ENFERMEDAD PROFESIONAL","ACCIDENTE TRABAJO",
    "ACCIDENTE TRÁNSITO","ACCIDENTE OFÍDICO","ACCIDENTE RÁBICO",
    "EVENTO CATASTRÓFICO","LESION AGRESION","LESION AUTO INFLINGIDA",
    "SOSPECHA ABUSO SEXUAL","SOSPECHA MALTRATO EMOCIONAL",
    "SOSPECHA MALTRATO FÍSICO","SOSPECHA VIOLENCIA SEXUAL","OTRA","OTRO TIPO ACCIDENTE"
)
$tipoOptions = @(
    "1 - IMPRESIÓN DIAGNÓSTICA",
    "2 - DIAGNÓSTICO CONFIRMADO NUEVO",
    "3 - DIAGNÓSTICO CONFIRMADO REPETIDO"
)
$relacionOptions = @("PRINCIPAL","RELACIONADO")

function New-OrigenCol {
    return @{
        id = newId; name = "Origen"; label = "Origen"; fieldType = "select"
        options = $origenOptions; allowCustom = $true; defaultValue = "ENFERMEDAD GENERAL"
    }
}
function New-TipoCol {
    return @{
        id = newId; name = "tipo"; label = "Tipo "; fieldType = "select"
        options = $tipoOptions; allowCustom = $false; defaultValue = "1 - IMPRESIÓN DIAGNÓSTICA"
    }
}
function New-RelacionCol {
    return @{
        id = newId; name = "relacion"; label = "Relación"; fieldType = "select"
        options = $relacionOptions; allowCustom = $false; defaultValue = "PRINCIPAL"
    }
}

# ============ Enumerar HCs ============
$listado = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT DISTINCT f.codigo FROM form_definitions f, jsonb_array_elements(f.schema_json->'children') sec, jsonb_array_elements(sec->'children') c WHERE f.tipo='HISTORIA CLINICA' AND f.tenant_id='$TenantId' AND c->>'fieldType'='table' AND LOWER(c->>'name')='diagnosticos' ORDER BY f.codigo;"
$codigos = $listado -split "`n" | Where-Object { $_ -ne "" }
Write-Host ("HCs objetivo ({0}): {1}" -f $codigos.Count, ($codigos -join ", ")) -ForegroundColor Cyan

$hechos = @(); $errores = @()
foreach ($codigo in $codigos) {
    Write-Host ""
    Write-Host "=== $codigo ===" -ForegroundColor White

    # Backup
    $bkPath = Join-Path $backupDir "$codigo.json"
    $rawBk = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$codigo' AND tenant_id='$TenantId';"
    if ([string]::IsNullOrWhiteSpace($rawBk)) {
        Write-Host "  no existe, salto" -ForegroundColor Yellow; continue
    }
    [System.IO.File]::WriteAllText($bkPath, $rawBk, [System.Text.UTF8Encoding]::new($false))
    Write-Host "  backup OK" -ForegroundColor DarkGray

    try {
        $schema = $rawBk | ConvertFrom-Json -AsHashtable
        $touched = $false
        foreach ($sec in $schema.children) {
            if ($null -eq $sec["children"]) { continue }
            for ($i = 0; $i -lt $sec.children.Count; $i++) {
                $c = $sec.children[$i]
                if ($c["fieldType"] -ne "table") { continue }
                if (([string]$c["name"]).ToLowerInvariant() -ne "diagnosticos") { continue }

                # Localizar la columna diagnostico existente (autocomplete CIE)
                $diagCol = $null
                foreach ($col in $c.columns) {
                    $nm = ([string]$col["name"]).ToLowerInvariant()
                    $lb = ([string]$col["label"]).ToLowerInvariant()
                    if ($nm.StartsWith("diagnostico") -or $lb.StartsWith("diagnostico")) {
                        $diagCol = $col; break
                    }
                }
                if ($null -eq $diagCol) {
                    # Nunca deberia pasar, pero por si acaso
                    $diagCol = @{ id = newId; name = "diagnostico"; label = "Diagnostico"; catalog = "cie11"; fieldType = "autocomplete"; allowCustom = $false }
                }

                # Reemplazar el arreglo columnas por el canonico
                $newCols = New-Object System.Collections.ArrayList
                [void]$newCols.Add($diagCol)
                [void]$newCols.Add((New-OrigenCol))
                [void]$newCols.Add((New-TipoCol))
                [void]$newCols.Add((New-RelacionCol))
                $c["columns"] = $newCols.ToArray()
                $sec.children[$i] = $c
                $touched = $true
                Write-Host "  columnas renormalizadas -> 4 (Diagnostico|Origen|Tipo|Relacion)" -ForegroundColor Green
            }
        }
        if (-not $touched) { Write-Host "  sin tabla diagnosticos, salto" -ForegroundColor Yellow; continue }

        # Persistir
        $json = ($schema | ConvertTo-Json -Depth 30 -Compress)
        $jsonSql = $json.Replace("'","''")
        $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
        $sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='$codigo' AND tenant_id='$TenantId';"
        $tmp = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
        try {
            $copy = "/tmp/visal_normdiag_$([Guid]::NewGuid().ToString('N')).sql"
            docker cp $tmp "${PgContainer}:${copy}" | Out-Null
            $env:MSYS_NO_PATHCONV = "1"
            $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
            $exit = $LASTEXITCODE
            docker exec $PgContainer rm $copy 2>$null | Out-Null
            $env:MSYS_NO_PATHCONV = $null
            if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
        } finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

        $hechos += $codigo
        Write-Host "  OK persistido" -ForegroundColor Green
    } catch {
        Write-Host "  ERROR: $_" -ForegroundColor Red
        $errores += "$codigo : $_"
    }
}

Write-Host ""
Write-Host "==================== RESUMEN ====================" -ForegroundColor Cyan
Write-Host ("Normalizados : {0}" -f $hechos.Count) -ForegroundColor Green
$hechos | ForEach-Object { Write-Host "  $_" }
if ($errores.Count -gt 0) {
    Write-Host ("Errores      : {0}" -f $errores.Count) -ForegroundColor Red
    $errores | ForEach-Object { Write-Host "  $_" }
}
Write-Host ("Backups en   : {0}" -f $backupDir) -ForegroundColor DarkGray
