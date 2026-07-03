# Rework-HCFO22-MedFisica.ps1
# HC-FO-22 MEDICINA FISICA Y REHABILITACION: construye toda la estructura
# desde cero (el schema actual esta esencialmente vacio: 2 secciones
# placeholder "Nueva seccion" + DIAGNOSTICOS + MEDICO).
#
# Estructura nueva:
#   [0] DATOS PERSONALES (18 campos paciente, mismo patron HC-FO-21)
#   [1] CONTENIDO CLINICO (patron HC-FO-21 con SV formulado y todas
#       las tablas seed con select allowCustom NO REFIERE)
#   [2] ORDENES Y RECOMENDACIONES (5 tablas patron HC-FO-08)
#   [3] Cierre
#   [4] MEDICO
#
# Preserva la tabla diagnosticos existente en el schema actual.
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
        options = @("ASINTOMATICO","SINTOMATICO","SI","NO")
        allowCustom = $true
        defaultValue = "ASINTOMATICO"
    })
)
$colsExamenSubItem = @(
    (Col "Ítem"      "item"     "text"),
    (Col "Hallazgo"  "hallazgo" "text")
)

# =========== SeedRows ===========
$rowsAF = @(
    @("Hipertensión Arterial", "NO REFIERE"),
    @("Diabetes",               "NO REFIERE"),
    @("Cáncer",                 "NO REFIERE"),
    @("Otros",                  "NO REFIERE")
)
$rowsAP = @(
    @("HTA",                    "NO REFIERE"),
    @("Diabetes",               "NO REFIERE"),
    @("Enfermedad Renal",       "NO REFIERE"),
    @("Enfermedad Articular",   "NO REFIERE"),
    @("TBC",                    "NO REFIERE"),
    @("Venéreas",               "NO REFIERE"),
    @("Síndrome Convulsivo",    "NO REFIERE"),
    @("Inmunológicos",          "NO REFIERE"),
    @("Hospitalizaciones",      "NO REFIERE"),
    @("Tóxicos Alérgicos",      "NO REFIERE"),
    @("Traumático",             "NO REFIERE"),
    @("Quirúrgicos",            "NO REFIERE"),
    @("Escleroterapia Previa",  "NO REFIERE"),
    @("Factores Agravantes",    "NO REFIERE"),
    @("Otro",                   "")
)
$rowsGineco = @(
    @("Menarquia",       "NO REFIERE"),
    @("Ciclo Menstrual", "NO REFIERE"),
    @("Gestaciones",     "NO REFIERE"),
    @("Partos",          "NO REFIERE"),
    @("Gemelares",       "NO REFIERE"),
    @("Ectópicos",       "NO REFIERE"),
    @("Molas",           "NO REFIERE"),
    @("Abortos",         "NO REFIERE"),
    @("Cesáreas",        "NO REFIERE"),
    @("FUR",             ""),
    @("FUP",             ""),
    @("FUC",             ""),
    @("Planificación",   ""),
    @("Menopausia",      "")
)
$rowsRevSistemas = @(
    @("Presenta Epilepsia o Convulsiones",              "NO"),
    @("Manifiesta tener Deformidades / Amputaciones",   "NO"),
    @("Cardiovascular",       "ASINTOMATICO"),
    @("Dermatológico",        "ASINTOMATICO"),
    @("Digestivo",            "ASINTOMATICO"),
    @("Genitourinario",       "ASINTOMATICO"),
    @("Neurológico",          "ASINTOMATICO"),
    @("Ocular",               "ASINTOMATICO"),
    @("Otorrinolaringológico","ASINTOMATICO"),
    @("Osteomuscular",        "ASINTOMATICO"),
    @("Respiratorio",         "ASINTOMATICO"),
    @("Otros Sistemas",       ""),
    @("Observaciones",        "")
)
$rowsExamenSubItem = @(
    @("AMAS (Arcos de Movilidad Articular / pasivos / activos)", ""),
    @("Fuerza",                                                  ""),
    @("ROT (Reflejos osteotendinosos)",                          ""),
    @("Sensibilidad",                                            ""),
    @("Otros",                                                   "")
)

# =========== Bloque SIGNOS VITALES (HC-FO-08 con formulas) ===========
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

# =========== Tablas de servicios ===========
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
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-22' AND tenant_id='$TenantId';"
$schema = $raw | ConvertFrom-Json -AsHashtable

# Buscar tabla diagnosticos en cualquier ubicacion
$diagTabla = $null
foreach ($sec in $schema.children) {
    foreach ($c in $sec.children) {
        if ($c["type"] -eq "field" -and $c["fieldType"] -eq "table" -and $c["name"] -eq "diagnosticos") {
            $diagTabla = $c
            break
        }
    }
    if ($diagTabla) { break }
}
if ($null -eq $diagTabla) { throw "No encontre tabla diagnosticos" }
Write-Host "Tabla diagnosticos preservada" -ForegroundColor Cyan

# Buscar seccion MEDICO existente para preservarla
$medico = $null
foreach ($sec in $schema.children) {
    if ($sec["label"] -eq "MEDICO") { $medico = $sec; break }
}
if ($null -eq $medico) { throw "No encontre seccion MEDICO" }
Write-Host "Seccion MEDICO preservada" -ForegroundColor Cyan

# =========== DATOS PERSONALES (nueva, patron HC-FO-21) ===========
$datosPersonales = @{
    id = newId; type = "section"; label = "DATOS PERSONALES"
    children = @(
        (Field "Nombres y Apellidos" "nombres_y_apellidos" "text" 8),
        (Field "Identificación" "identificacion" "text" 4),
        (Field "Fecha nacimiento" "fecha_nacimiento" "date" 3),
        (Field "Lugar nacimiento" "lugar_nacimiento" "text" 3),
        (Field "Edad" "edad" "number" 2),
        (Field "Sexo" "sexo" "select" 2 @{ options=@("MASCULINO","FEMENINO","OTRO") }),
        (Field "Estado civil" "estado_civil" "text" 2),
        (Field "Teléfono o celular" "tel_celular" "text" 4),
        (Field "Dirección" "direccion" "text" 4),
        (Field "Ciudad" "ciudad" "text" 2),
        (Field "Zona" "zona" "select" 2 @{ options=@("URBANA","RURAL") }),
        (Field "Ocupación" "ocupacion" "text" 3),
        (Field "Estudios" "estudios" "text" 3),
        (Field "EPS" "eps" "text" 3),
        (Field "Régimen" "regimen" "text" 3),
        (Field "En caso de emergencia informar a" "contacto_emergencia" "text" 4),
        (Field "Parentesco" "parentesco" "text" 4),
        (Field "Teléfono" "telefono_emergencia" "text" 4)
    )
}

# =========== CONTENIDO CLINICO ===========
$contenidoClinico = @()
$contenidoClinico += (SH "ANAMNESIS")
$contenidoClinico += (SH "MOTIVO DE LA CONSULTA")
$contenidoClinico += (Field "Motivo de la consulta" "motivo_consulta" "textarea" 12)
$contenidoClinico += (SH "ENFERMEDAD ACTUAL")
$contenidoClinico += (Field "Enfermedad actual" "enfermedad_actual" "textarea" 12)
$contenidoClinico += (SH "ANTECEDENTES FAMILIARES")
$contenidoClinico += (Tabla "Antecedentes familiares" "antecedentes_familiares" $colsItemObsSelect $rowsAF $true)
$contenidoClinico += (SH "ANTECEDENTES PERSONALES")
$contenidoClinico += (Tabla "Antecedentes personales" "antecedentes_personales" $colsItemObsSelect $rowsAP $true)
$contenidoClinico += (SH "GINECO OBSTÉTRICOS")
$contenidoClinico += (Tabla "Gineco obstétricos" "gineco_obstetricos" $colsItemObsSelect $rowsGineco $true)
$contenidoClinico += (SH "REVISIÓN POR SISTEMAS")
$contenidoClinico += (Tabla "Revisión por sistemas" "revision_sistemas" $colsSistemaHallazgo $rowsRevSistemas $true)
$contenidoClinico += (SH "INCAPACIDAD ACTUAL")
$contenidoClinico += (Field "¿Tiene actualmente incapacidad?" "tiene_incapacidad" "select" 4 @{ options=@("Si","No") })
$contenidoClinico += (Field "Desde" "incapacidad_desde" "date" 4)
$contenidoClinico += (Field "Hasta" "incapacidad_hasta" "date" 4)
$contenidoClinico += $bloqueSV
$contenidoClinico += (SH "EXAMEN FÍSICO")
$contenidoClinico += (Field "¿Ayudas Diagnosticas?" "ayudas_diag" "select" 4 @{ options=@("Si","No") })
$contenidoClinico += (Field "¿Cuál?" "ayudas_diag_cual" "text" 8)
$contenidoClinico += (Field "Estado General" "estado_general" "textarea" 12)
$contenidoClinico += (Tabla "Examen físico especifico" "examen_fisico_especifico" $colsExamenSubItem $rowsExamenSubItem $true)
$contenidoClinico += (SH "ANÁLISIS")
$contenidoClinico += (Field "Análisis" "analisis" "textarea" 12)
$contenidoClinico += (SH "PLAN")
$contenidoClinico += (Field "Plan" "plan" "textarea" 12)
$contenidoClinico += (Field "¿Requiere manejo quirúrgico?" "requiere_quirurgico" "select" 4 @{ options=@("Si","No") })
$contenidoClinico += (Field "¿Se otorga incapacidad?" "otorga_incapacidad" "select" 4 @{ options=@("Si","No") })
$contenidoClinico += (Field "¿Alta médica?" "alta_medica" "select" 4 @{ options=@("Si","No") })
$contenidoClinico += (SH "DIAGNÓSTICOS")
$contenidoClinico += $diagTabla
$contenidoClinico += (Field "Tipo de Diagnóstico Principal" "tipo_dx_principal" "select" 6 @{ options=@("Impresión diagnóstica","Confirmado nuevo","Confirmado repetido") })
$contenidoClinico += (Field "Causa Externa" "causa_externa" "text" 6)

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

# =========== Cierre ===========
$cierre = @{
    id = newId; type = "section"; label = "Cierre"
    children = @(
        (Field "Observaciones / Conclusiones" "observaciones_cierre" "textarea" 12)
    )
}

# =========== Ensamblar ===========
$schema.children = @($datosPersonales, $secCC, $secOR, $cierre, $medico)
Write-Host ("Top-level ahora: {0} secciones" -f $schema.children.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='HC-FO-22' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_mf_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK HC-FO-22 actualizado." -ForegroundColor Green
