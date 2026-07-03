# Rework-HCFO19-Plastica.ps1
# HC-FO-19: reemplaza secciones [1..15] por dos secciones nuevas y limpias:
#   - "CONTENIDO CLINICO": MOTIVO + ENFERMEDAD + ANTECEDENTES(tabla) +
#      REVISION SISTEMAS(tabla) + SIGNOS VITALES(HC-FO-08 con formulas) +
#      EXAMEN FISICO(tabla) + Incapacidad + DIAGNOSTICOS(preservada) +
#      ANALISIS + PLAN TERAPEUTICO
#   - "ORDENES Y RECOMENDACIONES": 5 tablas (Servicios, Medicamentos,
#      Remisiones, Incapacidades, Recomendaciones) - patron HC-FO-08.
#
# Tablas de antecedentes: col Observacion es SELECT con opciones
# ["NO REFIERE","REFIERE"] + allowCustom=true.
#
# PRESERVA:
#   - Seccion [0] DATOS PERSONALES completa
#   - La tabla 'diagnosticos' que estaba anidada en la seccion [13]
#   - Secciones Cierre y MEDICO (top-level, ultimas 2)
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

# =========== Columnas ===========
$colsItemObsSelect = @(
    (Col "Ítem"        "item"        "text"),
    (Col "Observación" "observacion" "select" @{
        options = @("NO REFIERE","REFIERE")
        allowCustom = $true
        defaultValue = "NO REFIERE"
    })
)
$colsSistemaHallazgo = @(
    (Col "Nombre del Sistema" "sistema"  "text"),
    (Col "Hallazgo"           "hallazgo" "select" @{
        options = @("NO REFIERE","REFIERE")
        allowCustom = $true
        defaultValue = ""
    })
)
$colsExamen = @(
    (Col "Parte del Cuerpo" "parte"    "text"),
    (Col "Hallazgo"         "hallazgo" "text")
)

# =========== Seed rows ===========
$rowsAntec = @(
    @("PATOLOGICOS",              "NO REFIERE"),
    @("PROCEDIMIENTOS ESTETICOS", "NO REFIERE"),
    @("QUIRURGICOS",              "NO REFIERE"),
    @("HOSPITALARIOS",            "NO REFIERE"),
    @("FARMACÉUTICOS",            "NO REFIERE"),
    @("ALERGICOS",                "NO REFIERE"),
    @("TOXICOLÓGICOS",            "NO REFIERE"),
    @("TRANSFUSIONALES",          "NO REFIERE"),
    @("HÁBITOS",                  "NO REFIERE"),
    @("FAMILIARES",               "NO REFIERE"),
    @("ESCLEROTERAPIA PREVIA",    "NO REFIERE"),
    @("PLANIFICACIÓN",            "NO REFIERE"),
    @("FACTORES AGRAVANTES",      "NO REFIERE"),
    @("OTROS",                    "")
)
$rowsRevisionSistemas = @(
    @("CARDIOVASCULAR",  ""),
    @("DIGESTIVO",       ""),
    @("GENITOURINARIO",  ""),
    @("NEUROLOGICO",     ""),
    @("OCULAR",          ""),
    @("OSTEOMUSCULAR",   ""),
    @("RESPIRATORIO",    ""),
    @("OTROS SISTEMAS",  "")
)
$rowsExamenFisico = @(
    @("ABDOMEN",                 ""),
    @("BOCA",                    ""),
    @("CABEZA Y CUELLO",         ""),
    @("CARA",                    ""),
    @("NARIZ",                   ""),
    @("NEUROLOGICO",             ""),
    @("OIDOS",                   ""),
    @("OJOS",                    ""),
    @("PIEL Y FANERAS",          ""),
    @("S. MUSCULOESQUELETICO",   ""),
    @("S. VASCULAS PERIFERICO",  ""),
    @("TORAX",                   "")
)

# =========== Bloque SIGNOS VITALES (HC-FO-08 con formulas, unificado) ===========
$bloqueSV = @(
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

# =========== Tablas de servicios (patron HC-FO-08) ===========
$colsServ = @(
    (Col "Codigo"      "codigo"      "text"),
    (Col "Descripcion" "descripcion" "text"),
    (Col "Obs"         "obs"         "text"),
    (Col "Cantidad"    "cantidad"    "number")
)
$colsMed = @(
    (Col "Codigo"             "Cod_medicamento" "text"),
    (Col "Descripcion"        "descripcion"     "text"),
    (Col "Via Administracion" "via"             "text"),
    (Col "Observaciones"      "obs"             "text"),
    (Col "Cantidad Total"     "cantidad"        "number")
)
$colsRem = @(
    (Col "Codigo"        "codigo"      "text"),
    (Col "Descripcion"   "descripcion" "text"),
    (Col "Observaciones" "obs"         "text")
)
$colsInc = @(
    (Col "Motivo"           "motivo" "text"),
    (Col "Desde"            "desde"  "date"),
    (Col "Hasta"            "hasta"  "date"),
    (Col "Dias Incapacidad" "dias"   "number")
)
$colsReco = @(
    (Col "Especialidad"  "especialidad"  "text"),
    (Col "Observaciones" "observaciones" "text")
)
$seedServ = @( @("","","","") )
$seedMed  = @( @("","","","","") )
$seedRem  = @( @("","","") )
$seedInc  = @( @("","","","") )
$seedReco = @( @("","") )

# =========== Cargar schema y extraer tabla diagnosticos existente ===========
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-19' AND tenant_id='$TenantId';"
$schema = $raw | ConvertFrom-Json -AsHashtable

# La tabla diagnosticos esta en children[13].children[17]
$diagTabla = $schema.children[13].children[17]
if ($diagTabla["name"] -ne "diagnosticos" -or $diagTabla["fieldType"] -ne "table") {
    throw "No encontre la tabla diagnosticos en la posicion esperada. Encontrado: name=$($diagTabla['name']) ft=$($diagTabla['fieldType'])"
}
Write-Host "Tabla diagnosticos existente preservada (name=$($diagTabla['name']))" -ForegroundColor Cyan

# =========== Contenido nuevo de CONTENIDO CLINICO ===========
$contenidoClinico = @()
$contenidoClinico += (SH "MOTIVO DE LA CONSULTA")
$contenidoClinico += (Field "Motivo de la consulta" "motivo_consulta" "textarea" 12)
$contenidoClinico += (SH "ENFERMEDAD ACTUAL")
$contenidoClinico += (Field "Enfermedad actual" "enfermedad_actual" "textarea" 12)
$contenidoClinico += (SH "ANTECEDENTES")
$contenidoClinico += (Tabla "Antecedentes" "antecedentes" $colsItemObsSelect $rowsAntec $true)
$contenidoClinico += (SH "REVISIÓN POR SISTEMAS")
$contenidoClinico += (Tabla "Revisión por sistemas" "revision_sistemas" $colsSistemaHallazgo $rowsRevisionSistemas $true)
$contenidoClinico += $bloqueSV
$contenidoClinico += (SH "EXAMEN FÍSICO")
$contenidoClinico += (Tabla "Examen físico" "examen_fisico" $colsExamen $rowsExamenFisico $true)
$contenidoClinico += (SH "INCAPACIDAD ACTUAL")
$contenidoClinico += (Field "¿Tiene actualmente incapacidad?" "tiene_incapacidad" "select" 4 @{ options=@("Si","No") })
$contenidoClinico += (Field "Desde" "incapacidad_desde" "date" 4)
$contenidoClinico += (Field "Hasta" "incapacidad_hasta" "date" 4)
$contenidoClinico += (SH "DIAGNÓSTICOS")
$contenidoClinico += $diagTabla
$contenidoClinico += (SH "ANÁLISIS")
$contenidoClinico += (Field "Análisis" "analisis" "textarea" 12)
$contenidoClinico += (SH "PLAN TERAPEUTICO")
$contenidoClinico += (Field "Plan terapeutico" "plan_terapeutico" "textarea" 12)

$secCC = @{
    id = newId; type = "section"; label = "CONTENIDO CLINICO"
    children = $contenidoClinico
}

# =========== ORDENES Y RECOMENDACIONES ===========
$secOR = @{
    id = newId; type = "section"; label = "ORDENES Y RECOMENDACIONES"
    children = @(
        (SH "ORDEN DE SERVICIOS"),
        (Tabla "Servicios solicitados" "servicios" $colsServ $seedServ $false),
        (SH "ORDEN DE MEDICAMENTOS"),
        (Tabla "Medicamentos" "medicamentos" $colsMed $seedMed $false),
        (SH "ORDEN DE REMISIÓN A ESPECIALISTA"),
        (Tabla "Remisiones" "remisiones" $colsRem $seedRem $false),
        (SH "ORDEN DE INCAPACIDAD"),
        (Tabla "Incapacidades" "incapacidades" $colsInc $seedInc $false),
        (SH "RECOMENDACIONES"),
        (Tabla "Recomendaciones" "recomendaciones" $colsReco $seedReco $false)
    )
}

# =========== Ensamblar top-level ===========
# Conservar: [0] DATOS PERSONALES, [16] Cierre, [17] MEDICO
# Reemplazar: [1..15] con $secCC + $secOR
$datosPersonales = $schema.children[0]
$cierre = $schema.children[16]
$medico = $schema.children[17]

$schema.children = @($datosPersonales, $secCC, $secOR, $cierre, $medico)
Write-Host ("Top-level ahora: {0} secciones (antes 18)" -f $schema.children.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='HC-FO-19' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_plas_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK HC-FO-19 actualizado." -ForegroundColor Green
