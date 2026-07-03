# Rework-HCFO15-TrabajoSocial.ps1
# HC-FO-15 TRABAJO SOCIAL: reemplaza bloque [17..43] del Encabezado por
# nueva estructura con tablas seed (col Observacion como select con
# opciones + allowCustom=true para que el operador pueda escribir texto
# libre encima).
#
# NO INCLUYE SIGNOS VITALES: el docx original no los pide.
#
# PRESERVA:
#   - [0..14]  datos personales
#   - [15..16] DIAGNOSTICOS + tabla
#   - [44..47] campos legacy MEDICO/FIRMA/NOMBRE/DOC
#   - Cierre y seccion MEDICO top-level
#
# NO TOCA HC-FO-08.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

function Col([string]$label, [string]$name, [string]$ft, $extra=@{}) {
    $c = @{ id=newId; label=$label; name=$name; fieldType=$ft; allowCustom=$false }
    foreach ($k in $extra.Keys) { $c[$k] = $extra[$k] }
    return $c
}
function Tabla([string]$label, [string]$name, $cols, $seedRows, [bool]$lockRows, [int]$widthColumns=12) {
    $rowsArr = New-Object System.Collections.ArrayList
    foreach ($r in $seedRows) {
        $celdas = New-Object System.Collections.ArrayList
        foreach ($v in $r) { [void]$celdas.Add($v) }
        $arr = $celdas.ToArray()
        [void]$rowsArr.Add($arr)
    }
    return @{
        id=newId; type="field"; fieldType="table"
        label=$label; name=$name; widthColumns=$widthColumns
        columns=$cols; seedRows=$rowsArr.ToArray()
        lockRows=$lockRows; allowCustom=$false
        isSection=$false; isText=$false; isTable=$true; required=$false
    }
}
function SH([string]$content) { @{ id=newId; type="text"; textStyle="subheading"; content=$content } }
function Field([string]$label, [string]$name, [string]$ft, [int]$width=12, $extra=@{}) {
    $f = @{ id=newId; type="field"; fieldType=$ft; label=$label; name=$name; widthColumns=$width; allowCustom=$false; required=$false }
    foreach ($k in $extra.Keys) { $f[$k] = $extra[$k] }
    return $f
}

# =========== Columnas: Item + Observacion (select con allowCustom) ===========
# La columna Observacion es un SELECT con opciones NO REFIERE/REFIERE pero
# allowCustom=true, para que el operador pueda escribir texto libre encima.
$colsItemObsSelect = @(
    (Col "Ítem"        "item"        "text"),
    (Col "Observación" "observacion" "select" @{
        options = @("NO REFIERE","REFIERE")
        allowCustom = $true
        defaultValue = "NO REFIERE"
    })
)

# =========== Seed rows ===========
$rowsAF = @(
    @("Hipertensión Arterial", "NO REFIERE"),
    @("Diabetes",               "NO REFIERE"),
    @("Cáncer",                 "NO REFIERE"),
    @("Otros",                  "")
)

$rowsAP = @(
    @("HTA",                  "NO REFIERE"),
    @("Diabetes",             "NO REFIERE"),
    @("Enfermedad Renal",     "NO REFIERE"),
    @("Enfermedad Articular", "NO REFIERE"),
    @("TBC",                  "NO REFIERE"),
    @("Venéreas",             "NO REFIERE"),
    @("Síndrome Convulsivo",  "NO REFIERE"),
    @("Inmunológicos",        "NO REFIERE"),
    @("Hospitalizaciones",    "NO REFIERE"),
    @("Tóxicos Alérgicos",    "NO REFIERE"),
    @("Traumático",           "NO REFIERE"),
    @("Quirúrgicos",          "NO REFIERE"),
    @("Escleroterapia Previa","NO REFIERE"),
    @("Factores Agravantes",  "NO REFIERE"),
    @("Otro",                 "")
)

# =========== Ensamblar bloque nuevo ===========
$bloque = @(
    (SH "ANAMNESIS"),
    (SH "MOTIVO DE CONSULTA"),
    (Field "Motivo de consulta" "motivo_consulta" "textarea" 12),

    (SH "ANTECEDENTES FAMILIARES"),
    (Tabla "Antecedentes familiares" "antecedentes_familiares" $colsItemObsSelect $rowsAF $true),

    (SH "ANTECEDENTES PERSONALES"),
    (Tabla "Antecedentes personales" "antecedentes_personales" $colsItemObsSelect $rowsAP $true),

    (SH "DESARROLLO DESCRIPTIVO DE LA SESIÓN"),
    (SH "DATOS FORMATIVOS LABORALES"),
    (Field "Datos formativos laborales" "datos_formativos_laborales" "textarea" 12),
    (SH "DATOS SOCIO-ECONÓMICOS"),
    (Field "Datos socio-económicos" "datos_socio_economicos" "textarea" 12),
    (SH "PARTICIPACIÓN SOCIAL COMUNITARIA"),
    (Field "Participación social comunitaria" "participacion_social_comunitaria" "textarea" 12)
)

# =========== Aplicar ===========
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-15' AND tenant_id='$TenantId';"
$schema = $raw | ConvertFrom-Json -AsHashtable
$enc = $schema.children[0]
$start = 17; $end = 43
Write-Host ("Rango a reemplazar: [{0}..{1}] = {2} nodos" -f $start, $end, ($end-$start+1)) -ForegroundColor Yellow

$head = $enc.children[0..($start - 1)]
$tail = $enc.children[($end + 1)..($enc.children.Count - 1)]
$enc.children = @($head) + @($bloque) + @($tail)
$schema.children[0] = $enc
Write-Host ("Encabezado ahora: {0} hijos. Bloque nuevo: {1} nodos" -f $enc.children.Count, $bloque.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='HC-FO-15' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_ts_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK HC-FO-15 actualizado." -ForegroundColor Green
