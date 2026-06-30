# Apply-HCFO10a-Defaults.ps1
# Aplica defaultValue (sacados del docx HC-FO-10a) a los campos existentes
# del schema. NO agrega ni quita campos. NO toca estructura. NO toca HC-FO-08.
#
# Mapeo por 'name' (no por indice) para que sobreviva a reordenamientos.
# Para la tabla 'test_movilidad_articular' completa el segundo elemento de
# cada seedRow (la columna 'Valoracion inicial').
#
# Dudas que el script NO resuelve y deja anotadas:
# - DOLOR.Frecuencia es 'number' en el schema; el docx pone "Ocasional".
#   No se cambia el tipo. Queda vacio.
# - ESTADO DE LA PIEL: el docx evalua Integridad/Coloracion/Temperatura/
#   Ulceras/Localizacion. El schema tiene Normal/Abierta/Cerrada/Suturas/
#   Exudados/Adherencia/Nodulos/Hipersensibilidad/Queloidea (catalogo
#   distinto). No se aplican defaults aqui.
# - Hay 'Localizacion' del DOLOR y luego 'Localizacion' de EDEMA y de
#   SENSIBILIDAD en el docx, pero el schema solo tiene UN campo 'localizaci_n'
#   (del DOLOR). Se le pone "Niega dolor". Las otras Localizacion no existen
#   en el schema y se dejan asi.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$Codigo      = "HC-FO-10a",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"

# Defaults por 'name' del campo
$defaults = @{
    # VALORACION NEUROLOGICA - ya tienen, sobrescribimos por idempotencia.
    "estado_mental"        = "Alerta, consciente y orientado en persona, tiempo, lugar y espacio."
    "coordinaci_n"         = "Coordinación motora conservada."
    "equilibrio"           = "Equilibrio estático y dinámico conservado."
    "reflejo"              = "Normorreflexia bilateral."
    "tono_muscular"        = "Normotonía."

    # DOLOR
    "localizaci_n"         = "Niega dolor"
    "escala_num_rica"      = "0/10"
    "evaluaci_n_inicial"   = "Sin dolor"
    "caracter_sticas"      = "Cansado"
    "reposo"               = "Niega"
    "espasmo"              = "Niega"
    "qu_movimiento"        = "Niega"
    "tipo"                 = "Niega"
    "presencia_de_v_rtigo" = "Niega"

    # EDEMA
    "cent_metros"          = "No se observa"
    "calificaci_n"         = "No aplica"

    # SENSIBILIDAD
    "sensibilidad"         = "Superficial y profunda conservadas."

    # MARCHA
    "marcha"               = "Funcional e independiente."
    "otras"                = "No presenta alteraciones."
    "postura"              = "Bipedestación con alineación postural adecuada."
}

# Valoracion inicial por nombre de articulacion en la tabla
$tmaInicial = @{
    "cervical"         = "MOVILIDAD ARTICULAR CONSERVADA."
    "dorso - lumbar"   = "MOVILIDAD ARTICULAR CONSERVADA."
    "hombros"          = "MOVILIDAD ARTICULAR CONSERVADA BILATERAL."
    "codo y antebrazo" = "MOVILIDAD ARTICULAR CONSERVADA BILATERAL."
    "muneca"           = "MOVILIDAD ARTICULAR CONSERVADA BILATERAL."
    "dedos mano"       = "MOVILIDAD ARTICULAR CONSERVADA BILATERAL."
    "pulgar"           = "MOVILIDAD ARTICULAR CONSERVADA BILATERAL."
    "cadera"           = "MOVILIDAD ARTICULAR CONSERVADA BILATERAL."
    "rodilla"          = "MOVILIDAD ARTICULAR CONSERVADA BILATERAL."
    "tobillo"          = "MOVILIDAD ARTICULAR CONSERVADA BILATERAL."
    "dedos pie"        = "MOVILIDAD ARTICULAR CONSERVADA BILATERAL."
}

# 1) Cargar schema
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$Codigo' AND tenant_id='$TenantId';"
if (-not $raw) { throw "$Codigo no encontrado." }
$schema = $raw | ConvertFrom-Json -AsHashtable

# 2) Recorrer recursivamente y aplicar defaults por name
$visitados = 0; $aplicados = 0
$nombresVistos = New-Object System.Collections.Generic.HashSet[string]
function Apply-Defaults {
    param($nodo)
    if ($nodo -is [System.Collections.IDictionary]) {
        if ($nodo.ContainsKey("type") -and $nodo["type"] -eq "field" -and $nodo.ContainsKey("name")) {
            $script:visitados++
            $n = [string]$nodo["name"]
            $null = $script:nombresVistos.Add($n)
            if ($defaults.ContainsKey($n)) {
                $valor = $defaults[$n]
                # Solo aplica a fieldType compatible con string libre. Saltar
                # number / select / calculated / date / table.
                $ft = [string]$nodo["fieldType"]
                if ($ft -in @("text","textarea")) {
                    $nodo["defaultValue"] = $valor
                    $script:aplicados++
                } else {
                    Write-Host ("    SKIP (tipo {0}): {1} = '{2}'" -f $ft, $n, $valor) -ForegroundColor Yellow
                }
            }
        }
        foreach ($k in @($nodo.Keys)) {
            $v = $nodo[$k]
            if ($v -is [System.Collections.IDictionary] -or $v -is [System.Collections.IList]) { Apply-Defaults $v }
        }
    } elseif ($nodo -is [System.Collections.IList]) {
        foreach ($it in $nodo) {
            if ($it -is [System.Collections.IDictionary] -or $it -is [System.Collections.IList]) { Apply-Defaults $it }
        }
    }
}
Apply-Defaults $schema

# 3) Tabla TEST MOVILIDAD ARTICULAR - rellenar columna 'Valoracion inicial'
$tabla = $null
foreach ($sec in $schema.children) {
    if ($sec -is [System.Collections.IDictionary] -and $sec.ContainsKey("children")) {
        foreach ($c in $sec.children) {
            if ($c -is [System.Collections.IDictionary] -and
                $c["type"] -eq "field" -and $c["fieldType"] -eq "table" -and
                $c["name"] -eq "test_movilidad_articular") {
                $tabla = $c; break
            }
        }
    }
    if ($tabla) { break }
}
$tmaActualizadas = 0
if ($tabla -and $tabla.ContainsKey("seedRows")) {
    for ($r = 0; $r -lt $tabla.seedRows.Count; $r++) {
        $row = $tabla.seedRows[$r]
        # row es un array; el primer item es la articulacion (texto seed)
        if ($row.Count -ge 1) {
            $articulacion = [string]$row[0]
            $key = $articulacion.ToLowerInvariant()
            if ($tmaInicial.ContainsKey($key)) {
                $valor = $tmaInicial[$key]
                # Si la fila tiene 1 sola celda, la extendemos a 2
                if ($row.Count -lt 2) {
                    $newRow = @($articulacion, $valor)
                    $tabla.seedRows[$r] = $newRow
                } else {
                    $tabla.seedRows[$r][1] = $valor
                }
                $tmaActualizadas++
            }
        }
    }
    Write-Host ("    TMA filas con Valoracion inicial: {0}" -f $tmaActualizadas) -ForegroundColor Green
}

Write-Host ""
Write-Host ("    Campos field/* visitados : {0}" -f $visitados) -ForegroundColor Cyan
Write-Host ("    Defaults aplicados        : {0}" -f $aplicados) -ForegroundColor Green
Write-Host ("    Defaults definidos en mapa: {0}" -f $defaults.Count) -ForegroundColor Cyan
$faltantes = @()
foreach ($k in $defaults.Keys) { if (-not $nombresVistos.Contains($k)) { $faltantes += $k } }
if ($faltantes.Count -gt 0) {
    Write-Host ("    AVISO - names del mapa que NO existen en el schema: " + ($faltantes -join ", ")) -ForegroundColor Yellow
}

# 4) Persistir
$out    = ($schema | ConvertTo-Json -Depth 30 -Compress)
$outSql = $out.Replace("'","''")
$now    = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql    = "UPDATE form_definitions SET schema_json = '$outSql'::jsonb, updated_at = '$now' WHERE codigo='$Codigo' AND tenant_id='$TenantId';"

$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_dv_$([Guid]::NewGuid().ToString('N')).sql"
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
