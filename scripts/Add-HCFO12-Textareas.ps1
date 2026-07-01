# Add-HCFO12-Textareas.ps1
# Inserta un textarea (widthColumns=12) DESPUES de cada subheading
# evaluable de HC-FO-12: 6 en RESUMEN HISTORIA CLINICA + 15 en AREAS
# DE EVALUACION = 21 textareas. Headers de grupo (RESUMEN HISTORIA
# CLINICA, AREAS DE EVALUACION, LENGUAJE, AUDICION, HABLA, VOZ)
# quedan sin textarea. NO TOCA HC-FO-08.

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

# Mapa: etiqueta subheading -> name del textarea. Lo que NO esta aqui es
# header de grupo y se conserva sin textarea debajo.
$mapa = [ordered]@{
    # RESUMEN HISTORIA CLINICA
    "ANTECEDENTES PERSONALES"                                     = "antecedentes_personales"
    "ANTECEDENTES FAMILIARES"                                     = "antecedentes_familiares"
    "ANTECEDENTES CLÍNICOS"                                       = "antecedentes_clinicos"
    "ÁREAS POR VALORAR EN EVALUACIÓN FONOAUDIOLOGÍA"               = "areas_por_valorar"
    "OBJETIVOS DEL TRATAMIENTO"                                   = "objetivos_tratamiento"
    "OBSERVACIONES"                                               = "observaciones"
    # LENGUAJE
    "FONÉTICO - FONOLÓGICO"                                       = "fonetico_fonologico"
    "SEMÁNTICO"                                                   = "semantico"
    "SINTÁCTICO"                                                  = "sintactico"
    "PRAGMÁTICO"                                                  = "pragmatico"
    "LENGUAJE EN EL ADULTO"                                       = "lenguaje_adulto"
    "APRENDIZAJE"                                                 = "aprendizaje"
    # AUDICION
    "DETECCIÓN DISCRIMINACIÓN IDENTIFICACIÓN Y COMPRENSIÓN"        = "deteccion_discriminacion"
    "MOVIMIENTOS OCULARES EQUILIBRIO ESTÁTICO O DINÁMICO Y CEFALOMOTORES" = "movimientos_oculares"
    # HABLA
    "PRAXIAS OROFACIALES"                                         = "praxias_orofaciales"
    "ARTICULACIÓN"                                                = "articulacion"
    "ALIMENTACIÓN"                                                = "alimentacion"
    # VOZ
    "CUALIDADES ACÚSTICAS Y PERCEPTUALES"                         = "cualidades_acusticas"
    "ALTERACIÓN DEL RITMO Y VELOCIDAD DEL HABLA"                  = "alteracion_ritmo_velocidad"
    "RESPIRACIÓN"                                                 = "respiracion"
}

# El rango a recorrer va desde "RESUMEN HISTORIA CLÍNICA" (inclusive) hasta
# justo antes del primer field con name='profesional' (firma).
$inicio = "RESUMEN HISTORIA CLÍNICA"

$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$Codigo' AND tenant_id='$TenantId';"
if (-not $raw) { throw "$Codigo no encontrado." }
$schema = $raw | ConvertFrom-Json -AsHashtable

$encIdx = -1
for ($i=0; $i -lt $schema.children.Count; $i++) {
    if ($schema.children[$i].label -eq "Encabezado del documento") { $encIdx = $i; break }
}
if ($encIdx -lt 0) { throw "No encontre 'Encabezado del documento'." }
$enc = $schema.children[$encIdx]

# Localizar [start..end-1] = del subheading 'RESUMEN HISTORIA CLINICA'
# hasta justo antes de 'profesional' field.
$start = -1; $end = -1
for ($i=0; $i -lt $enc.children.Count; $i++) {
    $c = $enc.children[$i]
    if ($c.type -eq "text" -and $c.textStyle -eq "subheading") {
        $txt = ($c.content -as [string]).Trim()
        if ($start -lt 0 -and $txt -eq $inicio) { $start = $i }
    }
    if ($start -ge 0 -and $c.type -eq "field" -and $c.name -eq "profesional") {
        $end = $i - 1; break
    }
}
if ($start -lt 0) { throw "No encontre subheading '$inicio'." }
if ($end -lt 0)   { $end = $enc.children.Count - 1 }
Write-Host "    Rango RESUMEN/AREAS: indices [$start..$end] = $($end - $start + 1) nodos" -ForegroundColor Cyan

# Reconstruir el bloque insertando textareas cuando corresponda
$nuevo = @()
foreach ($i in $start..$end) {
    $c = $enc.children[$i]
    $nuevo += $c
    if ($c.type -eq "text" -and $c.textStyle -eq "subheading") {
        $key = ($c.content -as [string]).Trim()
        if ($mapa.Contains($key)) {
            $nuevo += @{
                id = newId; type = "field"; fieldType = "textarea"
                label = $key; name = $mapa[$key]
                widthColumns = 12; rows = 3
                allowCustom = $false; required = $false
            }
        }
    }
}

$head = @(); $tail = @()
if ($start -gt 0) { $head = $enc.children[0..($start - 1)] }
if ($end -lt ($enc.children.Count - 1)) { $tail = $enc.children[($end + 1)..($enc.children.Count - 1)] }
$enc.children = @($head) + @($nuevo) + @($tail)
$schema.children[$encIdx] = $enc

$agregados = $nuevo.Count - ($end - $start + 1)
Write-Host "    Textareas agregados: $agregados" -ForegroundColor Green
Write-Host "    Hijos totales del Encabezado: $($enc.children.Count)"

$out    = ($schema | ConvertTo-Json -Depth 30 -Compress)
$outSql = $out.Replace("'","''")
$now    = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql    = "UPDATE form_definitions SET schema_json = '$outSql'::jsonb, updated_at = '$now' WHERE codigo='$Codigo' AND tenant_id='$TenantId';"

$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_ta12_$([Guid]::NewGuid().ToString('N')).sql"
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
