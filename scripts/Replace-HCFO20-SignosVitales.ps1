# Replace-HCFO20-SignosVitales.ps1
# Cirugia SOLO en la seccion SIGNOS VITALES + MEDIDAS ANTROPOMETRICAS de
# HC-FO-20 dentro de la seccion 'CONTENIDO CLINICO' (children[1]).
# Reemplaza los indices [16..28] (13 nodos: SV + antropo) por el bloque
# unificado de HC-FO-08 con los mismos 16 nodos formulados que ya usamos
# en HC-FO-10a, HC-FO-11, HC-FO-12.
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

$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-20' AND tenant_id='$TenantId';"
$schema = $raw | ConvertFrom-Json -AsHashtable
$sec = $schema.children[1]

# Verificar que [16] sea el subheading SIGNOS VITALES
$sv = $sec.children[16]
if (-not ($sv.type -eq "text" -and [string]$sv.textStyle -eq "subheading" -and [string]$sv.content -match "SIGNOS VITALES")) {
    throw "El indice [16] no es SIGNOS VITALES. Encontrado type=$($sv.type) content='$($sv.content)'"
}

# Bloque nuevo (mismo patron que Replace-SignosVitales genérico)
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

$start = 16; $end = 28
$head = $sec.children[0..($start - 1)]
$tail = $sec.children[($end + 1)..($sec.children.Count - 1)]
$sec.children = @($head) + @($newBlock) + @($tail)
$schema.children[1] = $sec
Write-Host ("Reemplazados {0} nodos [{1}..{2}] por {3} nodos (SV + antropo unificado con calculos). Seccion ahora: {4} hijos" -f ($end-$start+1), $start, $end, $newBlock.Count, $sec.children.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='HC-FO-20' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_sv20_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK HC-FO-20 actualizado." -ForegroundColor Green
