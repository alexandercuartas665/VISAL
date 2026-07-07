# Fix-HCFO10-MergeFromHCFO08.ps1
# Trae de HC-FO-08 a HC-FO-10 las secciones clinicas que faltan y las que
# estan mal, preservando el resto de HC-FO-10 tal cual:
#
# 1. Reemplaza "Revision por sistemas" (HC-FO-10)          <- REVISION POR SISTEMAS (HC-FO-08)
# 2. Reemplaza "Medidas antropometricas" (HC-FO-10)        <- SIGNOS VITALES (HC-FO-08)
# 3. Agrega antes de MEDICO las 6 secciones de servicios de HC-FO-08:
#    SERVICIOS, MEDICAMENTOS, REMISIONES, LABORATORIOS, INSUMOS, INCAPACIDADES
#
# Preserva secciones: Datos del paciente, Anamnesis, Examen fisico,
# Analisis, Diagnosticos, MEDICO. Genera IDs nuevos al copiar (no colision).
#
# Backup previo en scratchpad/backup_merge_hcfo10_<timestamp>.json

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

# Regenera IDs recursivamente en cualquier hashtable/lista para evitar
# colision con el schema original de HC-FO-10.
function Regen-Ids([object]$node) {
    if ($node -is [hashtable]) {
        if ($node.ContainsKey("id")) { $node["id"] = newId }
        foreach ($k in @($node.Keys)) {
            $v = $node[$k]
            if ($v -is [hashtable] -or $v -is [System.Collections.IList]) {
                Regen-Ids $v
            }
        }
    } elseif ($node -is [System.Collections.IList]) {
        foreach ($v in $node) {
            if ($v -is [hashtable] -or $v -is [System.Collections.IList]) {
                Regen-Ids $v
            }
        }
    }
}

# BACKUP
$stamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
$bkPath = Join-Path $BackupRoot ("backup_merge_hcfo10_$stamp.json")
$raw10 = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-10' AND tenant_id='$TenantId';"
if ([string]::IsNullOrWhiteSpace($raw10)) { throw "HC-FO-10 no existe" }
[System.IO.File]::WriteAllText($bkPath, $raw10, [System.Text.UTF8Encoding]::new($false))
Write-Host "Backup HC-FO-10: $bkPath" -ForegroundColor DarkGray

# Cargar HC-FO-08 (fuente)
$raw08 = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-08' AND tenant_id='$TenantId';"
if ([string]::IsNullOrWhiteSpace($raw08)) { throw "HC-FO-08 no existe" }
$schema08 = $raw08 | ConvertFrom-Json -AsHashtable

# Extraer las secciones que queremos de HC-FO-08
$labelsQueremos = @("REVISION POR SISTEMAS","SIGNOS VITALES","SERVICIOS","MEDICAMENTOS","REMISIONES","LABORATORIOS","INSUMOS","INCAPACIDADES")
$secsFuente = @{}
foreach ($sec in $schema08.children) {
    $lbl = [string]$sec["label"]
    if ($labelsQueremos -contains $lbl) { $secsFuente[$lbl] = $sec }
}
foreach ($lbl in $labelsQueremos) {
    if (-not $secsFuente.ContainsKey($lbl)) { throw "Falta seccion '$lbl' en HC-FO-08" }
}
Write-Host ("Copiando de HC-FO-08: {0}" -f ($labelsQueremos -join ', ')) -ForegroundColor Cyan

# Cargar HC-FO-10 (destino)
$schema10 = $raw10 | ConvertFrom-Json -AsHashtable

# Construir nueva lista de secciones para HC-FO-10
$newSecs = New-Object System.Collections.ArrayList
foreach ($sec in $schema10.children) {
    $lbl = [string]$sec["label"]
    switch -Exact ($lbl) {
        "Revision por sistemas" {
            $copia = $secsFuente["REVISION POR SISTEMAS"] | ConvertTo-Json -Depth 40 -Compress | ConvertFrom-Json -AsHashtable
            Regen-Ids $copia
            [void]$newSecs.Add($copia)
            Write-Host "  reemplazado: Revision por sistemas -> REVISION POR SISTEMAS" -ForegroundColor Green
        }
        "Medidas antropometricas" {
            $copia = $secsFuente["SIGNOS VITALES"] | ConvertTo-Json -Depth 40 -Compress | ConvertFrom-Json -AsHashtable
            Regen-Ids $copia
            [void]$newSecs.Add($copia)
            Write-Host "  reemplazado: Medidas antropometricas -> SIGNOS VITALES" -ForegroundColor Green
        }
        "MEDICO" {
            # Antes de MEDICO insertamos las 6 secciones de servicios
            foreach ($extra in @("SERVICIOS","MEDICAMENTOS","REMISIONES","LABORATORIOS","INSUMOS","INCAPACIDADES")) {
                $copia = $secsFuente[$extra] | ConvertTo-Json -Depth 40 -Compress | ConvertFrom-Json -AsHashtable
                Regen-Ids $copia
                [void]$newSecs.Add($copia)
                Write-Host "  agregado antes de MEDICO: $extra" -ForegroundColor Green
            }
            [void]$newSecs.Add($sec)
        }
        default {
            [void]$newSecs.Add($sec)
        }
    }
}
$schema10["children"] = $newSecs.ToArray()

Write-Host ("Total secciones HC-FO-10: {0}" -f $newSecs.Count) -ForegroundColor Cyan

# Persistir
$json = ($schema10 | ConvertTo-Json -Depth 40 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='HC-FO-10' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_merge10_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK HC-FO-10 actualizado." -ForegroundColor Green
