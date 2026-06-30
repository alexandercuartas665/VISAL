# Replace-HCFO10a-Antecedentes.ps1
# Reemplaza UNICAMENTE el subheading "ANTECEDENTES" + sus 9 campos planos
# dentro de "Encabezado del documento" en HC-FO-10a, por una tabla seed
# locked con 9 filas predefinidas y default "No refiere" en la columna
# Observacion. Conserva todo lo demas EXACTAMENTE como esta. NO TOCA HC-FO-08.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$Codigo      = "HC-FO-10a",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

# 1) Cargar schema actual
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo = '$Codigo' AND tenant_id = '$TenantId';"
if (-not $raw) { throw "$Codigo no encontrado." }
$schema = $raw | ConvertFrom-Json -AsHashtable

# 2) Encabezado del documento
$encIndex = -1
for ($i = 0; $i -lt $schema.children.Count; $i++) {
    if ($schema.children[$i].label -eq "Encabezado del documento") { $encIndex = $i; break }
}
if ($encIndex -lt 0) { throw "No encontre 'Encabezado del documento'." }
$enc = $schema.children[$encIndex]

# 3) Rango [start..end] del subheading "ANTECEDENTES" (solo el exacto, no "ANTECEDENTES FAMILIARES" etc.)
$startIdx = -1; $endIdx = -1
for ($i = 0; $i -lt $enc.children.Count; $i++) {
    $c = $enc.children[$i]
    if ($c.type -eq "text" -and $c.textStyle -eq "subheading") {
        if ($startIdx -lt 0 -and ($c.content -eq "ANTECEDENTES")) {
            $startIdx = $i
        } elseif ($startIdx -ge 0) {
            $endIdx = $i - 1
            break
        }
    }
}
if ($startIdx -lt 0) { throw "No encontre subheading 'ANTECEDENTES'." }
if ($endIdx -lt 0) { $endIdx = $enc.children.Count - 1 }
$oldCount = $endIdx - $startIdx + 1
Write-Host "    Rango ANTECEDENTES actual: indices [$startIdx..$endIdx] = $oldCount nodos" -ForegroundColor Cyan

# 4) Definicion de la tabla
$cols = @(
    @{ id = newId; label = "Item";        name = "item";        fieldType = "text"; allowCustom = $false },
    @{ id = newId; label = "Observacion"; name = "observacion"; fieldType = "text"; allowCustom = $false; defaultValue = "No refiere" }
)
# Cada seedRow es un array paralelo a columns; col[0] es seed (texto fijo),
# col[1] queda null para que el viewer la trate como editable y aplique el
# defaultValue de la columna.
$seedRows = @(
    @(,"Patologicos, personales y familiares"),
    @(,"Quirurgicos"),
    @(,"Farmacologicos"),
    @(,"Traumatologicos"),
    @(,"Toxicologicos"),
    @(,"Alergicos"),
    @(,"Alimenticios"),
    @(,"Gineco obstetricos"),
    @(,"Otros")
)

$newBlock = @(
    @{ id = newId; type = "text"; textStyle = "subheading"; content = "ANTECEDENTES" },
    @{
        id = newId
        type = "field"
        fieldType = "table"
        label = "Antecedentes"
        name = "antecedentes"
        widthColumns = 12
        columns = $cols
        seedRows = $seedRows
        lockRows = $true
        isSection = $false
        isText = $false
        isTable = $true
        allowCustom = $false
        required = $false
    }
)

# 5) Reemplazar en su lugar
$head = @()
$tail = @()
if ($startIdx -gt 0) { $head = $enc.children[0..($startIdx - 1)] }
if ($endIdx -lt ($enc.children.Count - 1)) { $tail = $enc.children[($endIdx + 1)..($enc.children.Count - 1)] }
$enc.children = @($head) + @($newBlock) + @($tail)
$schema.children[$encIndex] = $enc

Write-Host "    Sustituidos $oldCount nodos -> $($newBlock.Count) nodos (1 subheading + 1 tabla con 9 seedRows)" -ForegroundColor Green
Write-Host "    Hijos totales del Encabezado: $($enc.children.Count)"

# 6) Persistir solo HC-FO-10a
$out    = ($schema | ConvertTo-Json -Depth 30 -Compress)
$outSql = $out.Replace("'","''")
$now    = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql    = "UPDATE form_definitions SET schema_json = '$outSql'::jsonb, updated_at = '$now' WHERE codigo = '$Codigo' AND tenant_id = '$TenantId';"

$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_a_hcfo10a_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally {
    Remove-Item $tmp -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "OK $Codigo actualizado. Schema final: $($out.Length) bytes" -ForegroundColor Green
Write-Host "Solo se toco el bloque ANTECEDENTES." -ForegroundColor Yellow
