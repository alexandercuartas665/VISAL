# Rework-HCFO25-JuntaMedica.ps1
# HC-FO-25 JUNTA MEDICA VISAL RT: rediseño completo.
# El schema anterior es una sola seccion DATOS PERSONALES con 55 hijos
# mezclando datos y contenido clinico + 7 secciones vacias con labels
# residuales.
#
# Nueva estructura:
#   [0] DATOS PERSONALES (18 campos paciente)
#   [1] CONTENIDO CLINICO (ANAMNESIS + ANTECEDENTES tabla seed +
#       INCAPACIDAD + EXAMEN FISICO con sub-tabla + Ayudas Diag +
#       DIAGNOSTICOS preservada + ANALISIS + CONDUCTA A SEGUIR)
#   [2] CONCEPTOS DE JUNTA (5 textareas: medico, fisioterapia,
#       ocupacional, psicologia, final)
#   [3] Cierre
#   [4] MEDICO
#
# NO tiene SIGNOS VITALES (el docx no los pide; es un consolidado).
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
        options = @("NO REFIERE","REFIERE","SIN DATOS")
        allowCustom = $true
        defaultValue = ""
    })
)
$colsExamenSubItem = @(
    (Col "Ítem"      "item"     "text"),
    (Col "Hallazgo"  "hallazgo" "text")
)

# =========== SeedRows ===========
$rowsAntecedentes = @(
    @("Empresa que labora",         ""),
    @("Cargo",                       ""),
    @("Dominancia",                  ""),
    @("Antigüedad en la empresa",    ""),
    @("Estado laboral",              ""),
    @("Fecha de incapacidad hasta",  ""),
    @("Antecedentes Patológicos",    "NO REFIERE"),
    @("Antecedente Farmacológico",   "NO REFIERE"),
    @("Antecedentes Quirúrgicos",    "NO REFIERE"),
    @("Partos",                      "NO REFIERE"),
    @("Traumático",                  "NO REFIERE"),
    @("Quirúrgicos",                 "NO REFIERE"),
    @("Actividades de tiempo libre", ""),
    @("Días de práctica por semana", ""),
    @("Jornada de práctica",         "")
)

$rowsExamenSubItem = @(
    @("Dolor",                                                    ""),
    @("AMAS (Arcos de Movilidad Articular / pasivos / activos)",  ""),
    @("Fuerza",                                                    ""),
    @("Flexibilidad",                                              ""),
    @("ROT (Reflejos osteotendinosos)",                            ""),
    @("Sensibilidad",                                              ""),
    @("Pruebas complementarias",                                   ""),
    @("Mecanismo de desplazamiento",                               "")
)

# =========== Cargar schema y extraer tabla diagnosticos ===========
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-25' AND tenant_id='$TenantId';"
$schema = $raw | ConvertFrom-Json -AsHashtable

$diagTabla = $null
foreach ($sec in $schema.children) {
    if (-not $sec.children) { continue }
    foreach ($c in $sec.children) {
        if ($c -is [System.Collections.IDictionary] -and $c["type"] -eq "field" -and $c["fieldType"] -eq "table" -and $c["name"] -eq "diagnosticos") {
            $diagTabla = $c; break
        }
    }
    if ($diagTabla) { break }
}
if ($null -eq $diagTabla) { throw "No encontre tabla diagnosticos" }
Write-Host "Tabla diagnosticos preservada" -ForegroundColor Cyan

# Buscar MEDICO existente
$medico = $null
foreach ($sec in $schema.children) {
    if ($sec["label"] -eq "MEDICO") { $medico = $sec; break }
}
if ($null -eq $medico) { throw "No encontre seccion MEDICO" }
Write-Host "Seccion MEDICO preservada" -ForegroundColor Cyan

# =========== DATOS PERSONALES ===========
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
$contenidoClinico += (SH "ANTECEDENTES PERSONALES")
$contenidoClinico += (Tabla "Antecedentes personales" "antecedentes_personales" $colsItemObsSelect $rowsAntecedentes $true)
$contenidoClinico += (SH "INCAPACIDAD ACTUAL")
$contenidoClinico += (Field "¿Tiene actualmente incapacidad?" "tiene_incapacidad" "select" 4 @{ options=@("Si","No") })
$contenidoClinico += (Field "Desde" "incapacidad_desde" "date" 4)
$contenidoClinico += (Field "Hasta" "incapacidad_hasta" "date" 4)
$contenidoClinico += (SH "EXAMEN FÍSICO")
$contenidoClinico += (Field "Estado General" "estado_general" "textarea" 12)
$contenidoClinico += (Tabla "Examen físico especifico" "examen_fisico_especifico" $colsExamenSubItem $rowsExamenSubItem $true)
$contenidoClinico += (Field "¿Ayudas Diagnosticas?" "ayudas_diag" "select" 4 @{ options=@("Si","No") })
$contenidoClinico += (Field "¿Cuál?" "ayudas_diag_cual" "text" 8)
$contenidoClinico += (SH "DIAGNÓSTICOS")
$contenidoClinico += $diagTabla
$contenidoClinico += (Field "Tipo de Diagnóstico Principal" "tipo_dx_principal" "select" 6 @{ options=@("Impresión diagnóstica","Confirmado nuevo","Confirmado repetido") })
$contenidoClinico += (Field "Causa Externa" "causa_externa" "select" 6 @{ options=@("Accidente de trabajo","Accidente comun","Enfermedad general","Enfermedad profesional","Otra") })
$contenidoClinico += (SH "ANÁLISIS")
$contenidoClinico += (Field "Análisis" "analisis" "textarea" 12)
$contenidoClinico += (SH "CONDUCTA A SEGUIR")
$contenidoClinico += (Field "Conducta a seguir" "conducta_seguir" "textarea" 12)

$secCC = @{
    id = newId; type = "section"; label = "CONTENIDO CLINICO"
    children = $contenidoClinico
}

# =========== CONCEPTOS DE JUNTA ===========
$conceptos = @(
    (SH "CONCEPTO MÉDICO"),
    (Field "Concepto médico" "concepto_medico" "textarea" 12 @{ enableVoice = $true }),
    (SH "CONCEPTO FUNCIONAL - FISIOTERAPIA"),
    (Field "Concepto funcional - Fisioterapia" "concepto_fisioterapia" "textarea" 12 @{ enableVoice = $true }),
    (SH "CONCEPTO OCUPACIONAL - TERAPIA OCUPACIONAL"),
    (Field "Concepto ocupacional - Terapia Ocupacional" "concepto_ocupacional" "textarea" 12 @{ enableVoice = $true }),
    (SH "CONCEPTO PSICOLOGÍA"),
    (Field "Concepto psicología" "concepto_psicologia" "textarea" 12 @{ enableVoice = $true }),
    (SH "CONCEPTO FINAL DE JUNTA"),
    (Field "Concepto final de junta" "concepto_final" "textarea" 12 @{ enableVoice = $true })
)
$secConceptos = @{
    id = newId; type = "section"; label = "CONCEPTOS DE JUNTA"
    children = $conceptos
}

# =========== Cierre ===========
$cierre = @{
    id = newId; type = "section"; label = "Cierre"
    children = @(
        (Field "Observaciones / Conclusiones" "observaciones_cierre" "textarea" 12)
    )
}

# =========== Ensamblar ===========
$schema.children = @($datosPersonales, $secCC, $secConceptos, $cierre, $medico)
Write-Host ("Top-level ahora: {0} secciones" -f $schema.children.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='HC-FO-25' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_junta_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK HC-FO-25 actualizado." -ForegroundColor Green
