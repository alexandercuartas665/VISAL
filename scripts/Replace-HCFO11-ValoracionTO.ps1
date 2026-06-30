# Replace-HCFO11-ValoracionTO.ps1
# Reemplaza UNICAMENTE el bloque "VALORACION POR TERAPIA OCUPACIONAL" dentro
# del "Encabezado del documento" de HC-FO-11, por una tabla seed locked con
# las 16 filas del docx original (3 columnas: Item | REALIZA | NO REALIZA).
# Conserva todo lo demas EXACTAMENTE como esta. NO TOCA HC-FO-08.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$Codigo      = "HC-FO-11",
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

# 2) Encabezado
$encIndex = -1
for ($i = 0; $i -lt $schema.children.Count; $i++) {
    if ($schema.children[$i].label -eq "Encabezado del documento") { $encIndex = $i; break }
}
if ($encIndex -lt 0) { throw "No encontre 'Encabezado del documento'." }
$enc = $schema.children[$encIndex]

# 3) Localizar el bloque VALORACION POR TERAPIA OCUPACIONAL: empieza en el
# subheading "VALORACIÓN POR TERAPIA OCUPACIONAL" y termina ANTES del campo
# 'profesional' (el primer field con name='profesional' marca el inicio de la firma).
$startIdx = -1
for ($i = 0; $i -lt $enc.children.Count; $i++) {
    $c = $enc.children[$i]
    if ($c.type -eq "text" -and $c.textStyle -eq "subheading" -and
        ($c.content -match 'VALORACI[OÓ]N\s+POR\s+TERAPIA\s+OCUPACIONAL')) {
        $startIdx = $i; break
    }
}
if ($startIdx -lt 0) { throw "No encontre subheading 'VALORACION POR TERAPIA OCUPACIONAL'." }

$endIdx = -1
for ($i = $startIdx + 1; $i -lt $enc.children.Count; $i++) {
    $c = $enc.children[$i]
    if ($c.type -eq "field" -and ($c.name -eq "profesional" -or
        ($c.label -and $c.label.ToString().ToUpperInvariant() -eq "PROFESIONAL"))) {
        $endIdx = $i - 1; break
    }
}
if ($endIdx -lt 0) { $endIdx = $enc.children.Count - 1 }
$oldCount = $endIdx - $startIdx + 1
Write-Host "    Rango VALORACION TO: indices [$startIdx..$endIdx] = $oldCount nodos" -ForegroundColor Cyan

# 4) Tabla nueva (3 columnas: Item, Realiza, No realiza)
$cols = @(
    @{ id = newId; label = "Item";        name = "item";        fieldType = "text"; allowCustom = $false },
    @{ id = newId; label = "Realiza";     name = "realiza";     fieldType = "text"; allowCustom = $false; defaultValue = "" },
    @{ id = newId; label = "No realiza";  name = "no_realiza";  fieldType = "text"; allowCustom = $false; defaultValue = "" }
)
# 16 filas (los items y sub-categorias del docx, en orden)
$seedRows = @(
    @(,"COMPONENTE SENSORIOMOTOR"),
    @(,"INTEGRACION SENSORIAL"),
    @(,"PROCESAMIENTO SENSORIAL: PROPIOCEPCION, EQUILIBRIO"),
    @(,"DESTREZAS PERCEPTUALES: ESQUEMA CORPORAL, DISCRIMINACION DERECHA E IZQUIERDA, FIGURA FONDO, CONSTANCIA DE LA FORMA, GRAFESTESIA"),
    @(,"NEUROMUSCULAR"),
    @(,"CONTROL POSTURAL EN TODAS LAS POSICIONES DE DESARROLLO MOTOR REFLEJOS"),
    @(,"MOTOR"),
    @(,"HABILIDADES MOTORAS GRUESAS: CONTROL CEFALICO, ROLADO, SEDENTE, CUADRUPEDO"),
    @(,"BIPEDO"),
    @(,"HABILIDADES MOTORAS FINAS: PATRONES INTEGRALES"),
    @(,"LATERALIDAD, CRUCE LINEA MEDIA, ORDENES CRUZADAS"),
    @(,"COORDINACION VISOMOTRIZ"),
    @(,"INTEGRACION BILATERAL"),
    @(,"TOLERANCIA AL PERIODO Y TIEMPO DE ACTIVIDAD"),
    @(,"PRAXIAS"),
    @(,"COMPONENTE COGNITIVO")
)
$newBlock = @(
    @{ id = newId; type = "text"; textStyle = "subheading"; content = "VALORACION POR TERAPIA OCUPACIONAL" },
    @{ id = newId; type = "text"; textStyle = "paragraph";  content = "Marque REALIZA o NO REALIZA en cada componente segun la evaluacion clinica." },
    @{
        id = newId
        type = "field"
        fieldType = "table"
        label = "Valoracion por terapia ocupacional"
        name = "valoracion_to"
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

# 5) Reemplazar [startIdx..endIdx] por $newBlock
$head = @()
$tail = @()
if ($startIdx -gt 0) { $head = $enc.children[0..($startIdx - 1)] }
if ($endIdx -lt ($enc.children.Count - 1)) { $tail = $enc.children[($endIdx + 1)..($enc.children.Count - 1)] }
$enc.children = @($head) + @($newBlock) + @($tail)
$schema.children[$encIndex] = $enc

Write-Host "    Sustituidos $oldCount nodos -> $($newBlock.Count) nodos (1 subheading + 1 parrafo + 1 tabla 16 filas)" -ForegroundColor Green
Write-Host "    Hijos totales del Encabezado: $($enc.children.Count)"

# 6) Persistir
$out    = ($schema | ConvertTo-Json -Depth 30 -Compress)
$outSql = $out.Replace("'","''")
$now    = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql    = "UPDATE form_definitions SET schema_json = '$outSql'::jsonb, updated_at = '$now' WHERE codigo = '$Codigo' AND tenant_id = '$TenantId';"

$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_vto_$([Guid]::NewGuid().ToString('N')).sql"
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
