# Replace-HC-Diagnosticos-Grupo-C.ps1
# HC-FO-18 y HC-FO-22: no tenian bloque de diagnostico visible. INSERTA la
# tabla en el punto natural sin eliminar campos existentes.
#
#   HC-FO-18: encabezado hijos, INSERTAR despues del indice 14 (fin de
#             DATOS PERSONALES, antes de AREA Y/O AREAS AFECTADAS).
#             Bloque: subheading DIAGNOSTICOS + tabla.
#   HC-FO-22: insertar como NUEVA seccion top-level 'DIAGNOSTICOS' con
#             la tabla dentro, ANTES de la seccion MEDICO.
#
# Aditivo: NO borra nada. Backups previos ya estan.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

function New-DiagnosticosBlock {
    $cols = @(
        @{ id=newId; label="Diagnostico"; name="diagnostico"
           fieldType="autocomplete"; catalog="cie11"; allowCustom=$false; defaultValue="" },
        @{ id=newId; label="Origen"; name="origen"
           fieldType="autocomplete"; catalog=""; allowCustom=$false; defaultValue="" },
        @{ id=newId; label="Tipo de diagnóstico principal"; name="tipo_diagnostico"
           fieldType="select"
           options=@("Impresión diagnóstica","Confirmado nuevo","Confirmado repetido")
           allowCustom=$false; defaultValue="" }
    )
    return @(
        @{ id=newId; type="text"; textStyle="subheading"; content="DIAGNÓSTICOS" },
        @{ id=newId; type="field"; fieldType="table"
           label="Diagnósticos"; name="diagnosticos"; widthColumns=12
           columns=$cols; lockRows=$false; allowCustom=$false
           isSection=$false; isText=$false; isTable=$true; required=$false }
    )
}

# ============ HC-FO-18 ============
Write-Host ""
Write-Host "========== HC-FO-18 ==========" -ForegroundColor Cyan
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-18' AND tenant_id='$TenantId';"
$schema = $raw | ConvertFrom-Json -AsHashtable
$encIdx = -1
for ($i=0; $i -lt $schema.children.Count; $i++) {
    if ($schema.children[$i].label -eq "Encabezado del documento") { $encIdx = $i; break }
}
$enc = $schema.children[$encIdx]
# Insertar despues del indice 14 (Parentesco), antes de [15] (AREA...)
$insertPos = 15
$nuevo = New-DiagnosticosBlock
$head = $enc.children[0..($insertPos - 1)]
$tail = $enc.children[$insertPos..($enc.children.Count - 1)]
$enc.children = @($head) + @($nuevo) + @($tail)
$schema.children[$encIdx] = $enc
Write-Host ("  Insertados 2 nodos (subheading + table) despues del indice 14. Encabezado ahora: {0} hijos" -f $enc.children.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='HC-FO-18' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_dxC18_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit)" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
Write-Host "  OK" -ForegroundColor Green

# ============ HC-FO-22 ============
Write-Host ""
Write-Host "========== HC-FO-22 ==========" -ForegroundColor Cyan
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-22' AND tenant_id='$TenantId';"
$schema = $raw | ConvertFrom-Json -AsHashtable
# Localizar la seccion MEDICO
$idxMed = -1
for ($i=0; $i -lt $schema.children.Count; $i++) {
    if ($schema.children[$i].label -eq "MEDICO") { $idxMed = $i; break }
}
$nuevaSec = @{
    id = newId; type = "section"; label = "DIAGNÓSTICOS"
    children = (New-DiagnosticosBlock)
}
$head = if ($idxMed -gt 0) { $schema.children[0..($idxMed - 1)] } else { @() }
$tail = $schema.children[$idxMed..($schema.children.Count - 1)]
$schema.children = @($head) + @($nuevaSec) + @($tail)
Write-Host ("  Insertada seccion top-level 'DIAGNOSTICOS' antes de MEDICO. Total secciones: {0}" -f $schema.children.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='HC-FO-22' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_dxC22_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit)" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
Write-Host "  OK" -ForegroundColor Green
