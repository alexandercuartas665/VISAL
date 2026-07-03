# Rework-HCFO20-Laboral.ps1
# HC-FO-20 HC LABORAL: rediseño de contenido clinico.
#
# PRESERVA (regla user: "no toques el header"):
#   - Seccion [0] DATOS PERSONALES completa (19 hijos)
#   - Seccion [3] FIRMA Y SELLO DEL PROFESIONAL
#   - Seccion [4] Cierre
#   - Seccion [5] MEDICO
#   - Dentro de seccion [1] preserva items originales de:
#       SIGNOS VITALES [26..33]
#       MEDIDAS ANTROPOMETRICAS [34..38]
#       DIAGNOSTICOS + tabla [39..40]
#
# REEMPLAZA:
#   - Contenido de seccion [1] con nueva estructura: MOTIVO+ENFERMEDAD+
#     INFO LABORAL+DATOS LABORALES+ANTECEDENTES+REVISION SISTEMAS+
#     DESCRIPCION TAREA+EXAMEN FISICO + (preservados) + ANALISIS+PLAN+
#     preguntas si/no
#   - Contenido de seccion [2] con 5 tablas de servicios traidas de
#     HC-FO-08 (Servicios, Medicamentos, Remisiones, Incapacidades,
#     Recomendaciones). Renombra la seccion a "ORDENES Y RECOMENDACIONES".
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

# =========== Column sets ===========
$colsItemObs = @(
    (Col "Ítem"        "item"        "text"),
    (Col "Observación" "observacion" "text")
)
$colsSistemaHallazgo = @(
    (Col "Nombre del Sistema" "sistema"  "text"),
    (Col "Hallazgo"           "hallazgo" "text")
)
$colsAntecedente = @(
    (Col "Nombre del Antecedente" "antecedente" "text"),
    (Col "Observación"            "observacion" "text" @{ defaultValue = "NO REFIERE" })
)
$colsTarea = @(
    (Col "Nombre de la tarea" "tarea"    "text"),
    (Col "Hallazgo"           "hallazgo" "text")
)

# =========== SeedRows nuevas secciones ===========
$rowsInfoLaboral = @(
    @("Ingreso",""),
    @("Dominancia",""),
    @("Empleador",""),
    @("Cargo",""),
    @("EPS",""),
    @("Versión del Trabajador",""),
    @("Tiempo de establecimiento",""),
    @("Mecanismo desencadenante",""),
    @("Traumático","")
)
$rowsDatosLaborales = @(
    @("Estado laboral",""),
    @("Antigüedad en la empresa",""),
    @("Fecha de incapacidad hasta","")
)
$rowsAntecedentes = @(
    @("Patológicos",          "NO REFIERE"),
    @("Farmacológicos",       "NO REFIERE"),
    @("Quirúrgicos",          "NO REFIERE"),
    @("Traumatológicos",      "NO REFIERE"),
    @("Ocupacionales",        "NO REFIERE"),
    @("Ginecoobstétricos",    "NO REFIERE"),
    @("Toxicológicos",        "NO REFIERE"),
    @("Alérgicos",            "NO REFIERE"),
    @("Familiares",           "NO REFIERE"),
    @("Observaciones",        "")
)
$rowsRevisionSistemas = @(
    @("Presenta Epilepsia o Convulsiones", "NO"),
    @("Manifiesta tener Deformidades",     "NO"),
    @("Cardiovascular",                    ""),
    @("Dermatológico",                     ""),
    @("Digestivo",                         ""),
    @("Genitourinario",                    ""),
    @("Neurológico",                       ""),
    @("Ocular",                            ""),
    @("Otorrinolaringológico",             ""),
    @("Osteomuscular",                     ""),
    @("Respiratorio",                      ""),
    @("Otros Sistemas",                    ""),
    @("Observaciones",                     "")
)
$rowsDescTarea = @(
    @("Características",       ""),
    @("Jornada Laboral",       ""),
    @("Horas de exposición",   ""),
    @("Plano de trabajo",      ""),
    @("Observaciones",         "")
)
$rowsExamenFisico = @(
    @("Estado general",                  ""),
    @("Descripción examen físico",       ""),
    @("AMAS (Arcos de Movilidad)",       ""),
    @("Fuerza",                          ""),
    @("ROT (Reflejos osteotendinosos)",  ""),
    @("Sensibilidad",                    ""),
    @("Otros",                           "")
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
    (Col "Especialidad" "especialidad" "text"),
    (Col "Observaciones" "observaciones" "text")
)
# Servicios sin seed
$emptySeed = @( @("","","","") )
$emptySeed5 = @( @("","","","","") )
$emptySeed3 = @( @("","","") )
$emptySeed4 = @( @("","","","") )
$emptySeed2 = @( @("","") )

# =========== Bloque nuevo seccion [1] ===========
$bloqueSec1 = @(
    (SH "MOTIVO DE LA CONSULTA"),
    (Field "Motivo de la consulta" "motivo_consulta" "textarea" 12),

    (SH "ENFERMEDAD ACTUAL"),
    (Field "Enfermedad actual" "enfermedad_actual" "textarea" 12),

    (SH "INFORMACIÓN LABORAL"),
    (Tabla "Información laboral" "info_laboral" $colsItemObs $rowsInfoLaboral $true),

    (SH "DATOS LABORALES"),
    (Tabla "Datos laborales" "datos_laborales" $colsSistemaHallazgo $rowsDatosLaborales $true),

    (SH "ANTECEDENTES PERSONALES"),
    (Tabla "Antecedentes personales" "antecedentes_personales" $colsAntecedente $rowsAntecedentes $true),

    (SH "REVISIÓN POR SISTEMAS"),
    (Tabla "Revisión por sistemas" "revision_sistemas" $colsSistemaHallazgo $rowsRevisionSistemas $true),

    (SH "DESCRIPCIÓN DE LA TAREA LABORAL"),
    (Tabla "Descripción de la tarea laboral" "desc_tarea_laboral" $colsTarea $rowsDescTarea $true),

    (SH "EXAMEN FÍSICO"),
    (Tabla "Examen físico" "examen_fisico" $colsItemObs $rowsExamenFisico $true)
)

# Los "preservados" [26..40] del schema actual se insertan aqui (tal cual estaban)
# Los agregamos despues por indice desde el schema cargado.

$bloqueSec1_post = @(
    (SH "ANÁLISIS"),
    (Field "Análisis" "analisis" "textarea" 12),

    (SH "PLAN DE TRATAMIENTO"),
    (Field "Plan de tratamiento" "plan_tratamiento" "textarea" 12),

    (Field "¿Requiere manejo quirúrgico?"  "requiere_quirurgico"  "select" 4 @{ options=@("Si","No") }),
    (Field "¿Se otorga incapacidad?"       "otorga_incapacidad"   "select" 4 @{ options=@("Si","No") }),
    (Field "¿Alta médica?"                 "alta_medica"          "select" 4 @{ options=@("Si","No") })
)

# =========== Bloque nuevo seccion [2] (ordenes y recomendaciones) ===========
$bloqueSec2 = @(
    (SH "ORDEN DE SERVICIOS"),
    (Tabla "Servicios solicitados" "servicios" $colsServ $emptySeed $false),
    (SH "ORDEN DE MEDICAMENTOS"),
    (Tabla "Medicamentos" "medicamentos" $colsMed $emptySeed5 $false),
    (SH "ORDEN DE REMISIÓN A ESPECIALISTA"),
    (Tabla "Remisiones" "remisiones" $colsRem $emptySeed3 $false),
    (SH "ORDEN DE INCAPACIDAD"),
    (Tabla "Incapacidades" "incapacidades" $colsInc $emptySeed4 $false),
    (SH "RECOMENDACIONES"),
    (Tabla "Recomendaciones" "recomendaciones" $colsReco $emptySeed2 $false)
)

# =========== Aplicar ===========
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-20' AND tenant_id='$TenantId';"
$schema = $raw | ConvertFrom-Json -AsHashtable

$sec1 = $schema.children[1]
# Preservar items [26..40] tal cual (SIGNOS VITALES + MEDIDAS ANTROP + DIAGNOSTICOS)
$preservados = $sec1.children[26..40]
Write-Host ("Preservando {0} nodos originales [26..40] (SIGNOS VITALES + MEDIDAS + DIAGNOSTICOS)" -f $preservados.Count) -ForegroundColor Cyan

# Construir contenido nuevo de seccion [1]
$nuevoSec1 = @($bloqueSec1) + @($preservados) + @($bloqueSec1_post)
$sec1.children = $nuevoSec1
$sec1.label = "CONTENIDO CLINICO"
$schema.children[1] = $sec1
Write-Host ("Seccion [1] renombrada a 'CONTENIDO CLINICO' con {0} hijos" -f $sec1.children.Count) -ForegroundColor Green

# Reemplazar contenido de seccion [2]
$sec2 = $schema.children[2]
$sec2.children = $bloqueSec2
$sec2.label = "ORDENES Y RECOMENDACIONES"
$schema.children[2] = $sec2
Write-Host ("Seccion [2] renombrada a 'ORDENES Y RECOMENDACIONES' con {0} hijos" -f $sec2.children.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='HC-FO-20' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_lab_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK HC-FO-20 actualizado." -ForegroundColor Green
