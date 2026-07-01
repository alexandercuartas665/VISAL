# Replace-HCFO12-Diagnosticos.ps1
# Reemplaza UNICAMENTE los 3 campos planos CIE / ORIGEN / RELACION del
# Encabezado de HC-FO-12 por una tabla de Diagnosticos con la MISMA
# estructura que la de HC-FO-08 (autocomplete cie11 + autocomplete +
# select tipo de diagnostico). NO TOCA HC-FO-08.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$Codigo      = "HC-FO-12",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$Codigo' AND tenant_id='$TenantId';"
if (-not $raw) { throw "$Codigo no encontrado." }
$schema = $raw | ConvertFrom-Json -AsHashtable

$encIdx = -1
for ($i=0; $i -lt $schema.children.Count; $i++) {
    if ($schema.children[$i].label -eq "Encabezado del documento") { $encIdx = $i; break }
}
if ($encIdx -lt 0) { throw "No encontre 'Encabezado del documento'." }
$enc = $schema.children[$encIdx]

# Localizar los 3 campos por name; deben ser consecutivos para considerarlos
# un bloque seguro de reemplazar.
$idxCie = -1; $idxOri = -1; $idxRel = -1
for ($i=0; $i -lt $enc.children.Count; $i++) {
    $c = $enc.children[$i]
    if ($c.type -ne "field") { continue }
    switch ($c.name) {
        "cie"      { $idxCie = $i }
        "origen"   { $idxOri = $i }
        "relacion" { $idxRel = $i }
    }
}
if ($idxCie -lt 0 -or $idxOri -lt 0 -or $idxRel -lt 0) {
    throw "No encontre los 3 campos: cie=$idxCie, origen=$idxOri, relacion=$idxRel"
}
if ($idxOri -ne ($idxCie + 1) -or $idxRel -ne ($idxOri + 1)) {
    throw "Los 3 campos NO son consecutivos (cie=$idxCie, origen=$idxOri, relacion=$idxRel). Aborto."
}
$start = $idxCie; $end = $idxRel
Write-Host "    Rango CIE/ORIGEN/RELACION: indices [$start..$end] = 3 nodos" -ForegroundColor Cyan

$cols = @(
    @{ id = newId; label = "Diagnostico"; name = "diagnostico"
       fieldType = "autocomplete"; catalog = "cie11"; allowCustom = $false; defaultValue = "" },
    @{ id = newId; label = "Origen"; name = "origen"
       fieldType = "autocomplete"; catalog = ""; allowCustom = $false; defaultValue = "" },
    @{ id = newId; label = "Tipo de diagnóstico principal"; name = "tipo_diagnostico"
       fieldType = "select"
       options = @("Impresión diagnóstica", "Confirmado nuevo", "Confirmado repetido")
       allowCustom = $false; defaultValue = "" }
)
$nuevo = @(
    @{ id = newId; type = "text"; textStyle = "subheading"; content = "DIAGNÓSTICOS" },
    @{ id = newId; type = "field"; fieldType = "table"
       label = "Diagnósticos"; name = "diagnosticos"; widthColumns = 12
       columns = $cols; lockRows = $false; allowCustom = $false
       isSection = $false; isText = $false; isTable = $true; required = $false }
)

$head = @(); $tail = @()
if ($start -gt 0) { $head = $enc.children[0..($start - 1)] }
if ($end -lt ($enc.children.Count - 1)) { $tail = $enc.children[($end + 1)..($enc.children.Count - 1)] }
$enc.children = @($head) + @($nuevo) + @($tail)
$schema.children[$encIdx] = $enc

Write-Host "    Sustituidos 3 nodos -> $($nuevo.Count) nodos (subheading + table)" -ForegroundColor Green
Write-Host "    Hijos totales del Encabezado: $($enc.children.Count)"

$out    = ($schema | ConvertTo-Json -Depth 30 -Compress)
$outSql = $out.Replace("'","''")
$now    = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql    = "UPDATE form_definitions SET schema_json = '$outSql'::jsonb, updated_at = '$now' WHERE codigo='$Codigo' AND tenant_id='$TenantId';"

$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_dx12_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host ""
Write-Host "OK $Codigo actualizado. Schema final: $($out.Length) bytes" -ForegroundColor Green
