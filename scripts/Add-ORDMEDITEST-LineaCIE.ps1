# Add-ORDMEDITEST-LineaCIE.ps1
# Agrega a ORD-MEDI-TEST:
#   - Nueva seccion "Diagnosticos" entre "Datos del paciente" y
#     "Medicamentos prescritos" con un field text 'diagnosticos_linea_cie'
#     que renderizara la lista de CIE-10 de la HC padre.
#   - prefill_routes_json con un mapping desde el source
#     'diagnosticos.linea_cie' hacia ese target.
#
# Cuando el motor (HistoriaMedicaPrefillHelper) implemente el source
# 'diagnosticos.linea_cie' — pendiente en dev — el campo empezara a
# rellenarse automaticamente sin mas cambios de schema.
#
# Backup previo en scratchpad.

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
$bkPath = Join-Path $BackupRoot ("backup_ordmeditest_lineacie_$stamp.json")
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='ORD-MEDI-TEST' AND tenant_id='$TenantId';"
if ([string]::IsNullOrWhiteSpace($raw)) { throw "ORD-MEDI-TEST no existe" }
[System.IO.File]::WriteAllText($bkPath, $raw, [System.Text.UTF8Encoding]::new($false))
Write-Host "Backup schema: $bkPath" -ForegroundColor DarkGray

$prefRaw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT COALESCE(prefill_routes_json::text, 'null') FROM form_definitions WHERE codigo='ORD-MEDI-TEST' AND tenant_id='$TenantId';"
$bkPref = Join-Path $BackupRoot ("backup_ordmeditest_prefill_$stamp.json")
[System.IO.File]::WriteAllText($bkPref, $prefRaw, [System.Text.UTF8Encoding]::new($false))
Write-Host "Backup prefill: $bkPref" -ForegroundColor DarkGray

$schema = $raw | ConvertFrom-Json -AsHashtable

# ========= Nueva seccion Diagnosticos =========
$secDx = @{
    id = "sec-dx"
    type = "section"
    label = "Diagnosticos"
    isSection = $true
    children = @(
        @{
            id = "f-dx-linea"
            type = "field"
            fieldType = "text"
            name = "diagnosticos_linea_cie"
            label = "CIE 10"
            widthColumns = 12
            allowCustom = $false
            required = $false
            placeholder = "Se llena automatico con los diagnosticos de la HC (source: diagnosticos.linea_cie)"
            hideIfEmpty = $true
        }
    )
}

# Insertar despues de "Datos del paciente" y antes de "Medicamentos prescritos"
$newSecs = New-Object System.Collections.ArrayList
$inserted = $false
foreach ($sec in $schema.children) {
    [void]$newSecs.Add($sec)
    if (-not $inserted -and ([string]$sec["label"]) -eq "Datos del paciente") {
        [void]$newSecs.Add($secDx)
        $inserted = $true
    }
}
if (-not $inserted) {
    # fallback: insertar al inicio
    $tmp = New-Object System.Collections.ArrayList
    [void]$tmp.Add($secDx)
    foreach ($sec in $schema.children) { [void]$tmp.Add($sec) }
    $newSecs = $tmp
}
$schema["children"] = $newSecs.ToArray()

# ========= Agregar prefill route =========
if ($prefRaw -eq "null" -or [string]::IsNullOrWhiteSpace($prefRaw)) {
    $prefill = @{ routes = @() }
} else {
    $prefill = $prefRaw | ConvertFrom-Json -AsHashtable
    if ($null -eq $prefill["routes"]) { $prefill["routes"] = @() }
}

# Buscar ruta Historia Medica existente
$rutaHM = $null
foreach ($r in $prefill.routes) {
    if (([string]$r["sourceModule"]) -eq "historiaMedica") { $rutaHM = $r; break }
}
if ($null -eq $rutaHM) {
    $rutaHM = @{
        id = newId
        name = "Historia Medica"
        sourceModule = "historiaMedica"
        mappings = @()
    }
    $prefill.routes = @($prefill.routes) + @($rutaHM)
}

# Agregar mapping (idempotente)
$yaExiste = $false
foreach ($m in $rutaHM.mappings) {
    if (([string]$m["source"]) -eq "diagnosticos.linea_cie" -and ([string]$m["target"]) -eq "diagnosticos_linea_cie") {
        $yaExiste = $true; break
    }
}
if (-not $yaExiste) {
    $rutaHM.mappings = @($rutaHM.mappings) + @(@{
        source = "diagnosticos.linea_cie"
        target = "diagnosticos_linea_cie"
    })
    Write-Host "  Mapping diagnosticos.linea_cie -> diagnosticos_linea_cie AGREGADO" -ForegroundColor Green
} else {
    Write-Host "  Mapping ya existia (idempotente)" -ForegroundColor Yellow
}

# ========= Persistir ambos =========
$schemaJson = ($schema  | ConvertTo-Json -Depth 40 -Compress).Replace("'","''")
$prefJson   = ($prefill | ConvertTo-Json -Depth 40 -Compress).Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$schemaJson'::jsonb, prefill_routes_json='$prefJson'::jsonb, updated_at='$now' WHERE codigo='ORD-MEDI-TEST' AND tenant_id='$TenantId';"

$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_ordcie_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK ORD-MEDI-TEST actualizado." -ForegroundColor Green
