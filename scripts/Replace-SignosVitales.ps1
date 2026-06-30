# Replace-SignosVitales.ps1
# Genérico: reemplaza UNICAMENTE el subheading "SIGNOS VITALES" + sus campos
# planos dentro del "Encabezado del documento" de cualquier HC-FO-*, por los
# 16 nodos nuevos con calculos (tensionClass / imc / imcClass / perimetroRiesgo).
#
# Conserva todo lo demas EXACTAMENTE como esta. NO TOCA HC-FO-08.
#
# Uso:
#   .\Replace-SignosVitales.ps1 -Codigo HC-FO-11
#   .\Replace-SignosVitales.ps1 -Codigo HC-FO-12

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Codigo,
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev",
    [string]$EncabezadoLabel = "Encabezado del documento"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

# 1) Cargar schema actual
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo = '$Codigo' AND tenant_id = '$TenantId';"
if (-not $raw) { throw "$Codigo no encontrado." }
$schema = $raw | ConvertFrom-Json -AsHashtable

# 2) Localizar la seccion contenedora (Encabezado del documento por defecto).
$encIndex = -1
for ($i = 0; $i -lt $schema.children.Count; $i++) {
    if ($schema.children[$i].label -eq $EncabezadoLabel) { $encIndex = $i; break }
}
if ($encIndex -lt 0) { throw "No encontre la seccion '$EncabezadoLabel' en $Codigo." }
$enc = $schema.children[$encIndex]

# 3) Localizar el rango [start..end] del subheading "SIGNOS VITALES"
$startIdx = -1; $endIdx = -1
for ($i = 0; $i -lt $enc.children.Count; $i++) {
    $c = $enc.children[$i]
    if ($c.type -eq "text" -and $c.textStyle -eq "subheading") {
        if ($startIdx -lt 0 -and ($c.content -match 'SIGNOS\s+VITALES')) {
            $startIdx = $i
        } elseif ($startIdx -ge 0) {
            $endIdx = $i - 1
            break
        }
    }
}
if ($startIdx -lt 0) { throw "No encontre subheading 'SIGNOS VITALES' en $Codigo." }
if ($endIdx -lt 0) { $endIdx = $enc.children.Count - 1 }
$oldCount = $endIdx - $startIdx + 1
Write-Host "    Rango SIGNOS VITALES en ${Codigo}: indices [$startIdx..$endIdx] = $oldCount nodos" -ForegroundColor Cyan

# 4) Bloque nuevo (mismo que el de HC-FO-10a / HC-FO-08).
$newBlock = @(
    @{ id = newId; type = "text"; textStyle = "subheading"; content = "SIGNOS VITALES" },
    @{ id = newId; type = "text"; textStyle = "paragraph";  content = "Tension Arterial (mm Hg)" },
    @{ id = newId; type = "field"; fieldType = "number";     label = "Sistolica";              name = "ta_sistolica";      widthColumns = 3 },
    @{ id = newId; type = "field"; fieldType = "number";     label = "Diastolica";             name = "ta_diastolica";     widthColumns = 3 },
    @{ id = newId; type = "field"; fieldType = "calculated"; label = "Clasificacion TA (auto)"; name = "ta_clasificacion";  widthColumns = 6;
       formula = "tensionClass(ta_sistolica, ta_diastolica)" },

    @{ id = newId; type = "field"; fieldType = "number"; label = "F. Cardiaca (x min)";     name = "fc";             widthColumns = 3 },
    @{ id = newId; type = "field"; fieldType = "number"; label = "F. Respiratoria (x min)"; name = "fr";             widthColumns = 3 },
    @{ id = newId; type = "field"; fieldType = "number"; label = "Pulsioximetria (%)";      name = "pulsioximetria"; widthColumns = 3 },
    @{ id = newId; type = "field"; fieldType = "number"; label = "Temperatura (C)";         name = "temperatura";    widthColumns = 3 },

    @{ id = newId; type = "field"; fieldType = "number";     label = "Peso (Kg)";                name = "peso";              widthColumns = 3 },
    @{ id = newId; type = "field"; fieldType = "number";     label = "Talla (cm)";               name = "talla";             widthColumns = 3 },
    @{ id = newId; type = "field"; fieldType = "calculated"; label = "IMC (auto)";               name = "imc";               widthColumns = 3;
       formula = "imc(peso, talla)" },
    @{ id = newId; type = "field"; fieldType = "calculated"; label = "Clasificacion IMC (auto)"; name = "imc_clasificacion"; widthColumns = 3;
       formula = "imcClass(imc)" },

    @{ id = newId; type = "field"; fieldType = "number";     label = "Perimetro Abdominal (cm)"; name = "perimetro";          widthColumns = 4 },
    @{ id = newId; type = "field"; fieldType = "calculated"; label = "Interpretacion (auto, segun sexo)"; name = "perimetro_riesgo"; widthColumns = 4;
       formula = "perimetroRiesgo(perimetro, sexo)" },

    @{ id = newId; type = "field"; fieldType = "select"; label = "Lateralidad Dominante"; name = "lateralidad"; widthColumns = 4;
       catalog = "estatico"; options = @("DIESTRO","ZURDO","AMBIDIESTRO") }
)

# 5) head + newBlock + tail
$head = @()
$tail = @()
if ($startIdx -gt 0) { $head = $enc.children[0..($startIdx - 1)] }
if ($endIdx -lt ($enc.children.Count - 1)) { $tail = $enc.children[($endIdx + 1)..($enc.children.Count - 1)] }
$enc.children = @($head) + @($newBlock) + @($tail)
$schema.children[$encIndex] = $enc

Write-Host "    Sustituidos $oldCount nodos -> $($newBlock.Count) nodos (con calculos)" -ForegroundColor Green
Write-Host "    Hijos totales del Encabezado: $($enc.children.Count)"

# 6) Persistir solo el codigo indicado
$out    = ($schema | ConvertTo-Json -Depth 30 -Compress)
$outSql = $out.Replace("'","''")
$now    = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql    = "UPDATE form_definitions SET schema_json = '$outSql'::jsonb, updated_at = '$now' WHERE codigo = '$Codigo' AND tenant_id = '$TenantId';"

$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_sv_$([Guid]::NewGuid().ToString('N')).sql"
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
Write-Host "Solo se toco el bloque SIGNOS VITALES. Resto del esquema intacto." -ForegroundColor Yellow
