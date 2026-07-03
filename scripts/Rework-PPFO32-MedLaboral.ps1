# Rework-PPFO32-MedLaboral.ps1
# Reconstruye PP-FO-32 CONSENTIMIENTO INFORMADO MEDICINA LABORAL segun docx fiel.
# 4 tablas seed con columna Marca (procedimientos, beneficios, riesgos, alternativas, riesgo_no_realizar).
#
# Preserva: Datos del Paciente (auto), Firmas (auto), Cierre.
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
        [void]$rowsArr.Add($celdas.ToArray())
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
function P([string]$content) { @{ id=newId; type="text"; textStyle="paragraph"; content=$content } }
function Field([string]$label, [string]$name, [string]$ft, [int]$width=12, $extra=@{}) {
    $f = @{ id=newId; type="field"; fieldType=$ft; label=$label; name=$name; widthColumns=$width; allowCustom=$false; required=$false }
    foreach ($k in $extra.Keys) { $f[$k] = $extra[$k] }
    return $f
}

# ================ Columnas ================
$colsSimple = @( (Col "Item" "item" "text"), (Col "Marca" "marca" "text" @{ defaultValue = "" }) )

# ================ SeedRows ================
$rowsProc = @(
    @("Entrevista médica (anamnesis) para recopilar antecedentes personales y laborales.", ""),
    @("Examen físico general y específico según los riesgos asociados al cargo.", ""),
    @("Emisión de un concepto médico ocupacional sobre la aptitud para el cargo, incluyendo recomendaciones, si aplica.", "")
)
$rowsBenef = @(
    @("Valoración integral del estado de salud del trabajador en relación con las funciones de su cargo.", ""),
    @("Identificación temprana de condiciones de salud que puedan afectar el desempeño laboral.", ""),
    @("Detección de factores de riesgo ocupacionales y posibles efectos sobre la salud.", ""),
    @("Determinación de la aptitud médica para desempeñar las actividades laborales asignadas.", ""),
    @("Emisión de recomendaciones orientadas a la promoción y prevención en salud ocupacional.", ""),
    @("Contribución a la prevención de accidentes de trabajo y enfermedades laborales.", "")
)
$rowsRiesgos = @(
    @("Posibilidad de error diagnóstico debido a información clínica incompleta o hallazgos limitados.", ""),
    @("Posibilidad de error en la emisión del concepto médico ocupacional.", ""),
    @("Incomodidad emocional al abordar antecedentes personales, médicos o laborales.", "")
)
$rowsAltern = @(
    @("Alta voluntaria conforme a decisión del trabajador.", ""),
    @("Reprogramación de la evaluación médica ocupacional.", "")
)
$rowsRiesgoNoRealizar = @(
    @("Imposibilidad de determinar la aptitud médica para el cargo.", ""),
    @("Dificultad para identificar oportunamente condiciones de salud relacionadas con el trabajo.", ""),
    @("Mayor riesgo de accidentes laborales o agravamiento de condiciones preexistentes.", ""),
    @("Falta de recomendaciones preventivas orientadas a proteger la salud del trabajador.", ""),
    @("Incumplimiento de requisitos normativos relacionados con la vigilancia de la salud ocupacional.", "")
)

# ================ Cargar y localizar auto sections ================
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='PP-FO-32' AND tenant_id='$TenantId';"
$schema = $raw | ConvertFrom-Json -AsHashtable

$datosPaciente = $null; $cierre = $null; $firmasAuto = $null
foreach ($sec in $schema.children) {
    $lbl = [string]$sec["label"]
    if ($lbl -eq "Datos del Paciente (auto-llenado)") { $datosPaciente = $sec }
    elseif ($lbl -eq "Cierre") { $cierre = $sec }
    elseif ($lbl -eq "Firmas (auto-llenadas)") { $firmasAuto = $sec }
}
if ($null -eq $datosPaciente) { throw "No encontre 'Datos del Paciente (auto-llenado)'" }
Write-Host "Seccion Datos del Paciente PRESERVADA" -ForegroundColor Green
if ($null -eq $firmasAuto) {
    $firmasAuto = @{
        id = newId; type = "section"; label = "Firmas (auto-llenadas)"
        children = @(
            (Field "Firma del paciente (URL)"     "firma_paciente_consent"     "text" 12),
            (Field "Firma del profesional (URL)"  "firma_profesional_consent"  "text" 12)
        )
    }
    Write-Host "Seccion Firmas (auto-llenadas) CREADA" -ForegroundColor Yellow
} else { Write-Host "Seccion Firmas (auto-llenadas) PRESERVADA" -ForegroundColor Green }
if ($null -eq $cierre) {
    $cierre = @{
        id = newId; type = "section"; label = "Cierre"
        children = @( (Field "Observaciones / Conclusiones" "observaciones_cierre" "textarea" 12 @{ enableVoice = $true }) )
    }
}

# ================ Textos declaraciones (docx literal) ================
$autoriz_1 = "autorizo expresamente a VISAL RT; para que me realice el examen médico y/o demás exámenes clínicos y paraclínicos para conocer mi estado de salud."
$autoriz_2 = "Si y solo si es necesario también autorizo a VISAL RT, para que informe y suministre una copia de mis registros al área de Salud Ocupacional, específicamente al Médico Ocupacional que asesora la empresa, y al área médica de la Administradora de Riesgos Laborales (ARL) a la cual estoy afiliado(a)."
$autoriz_3 = "Certifico que he comprendido el propósito, los beneficios y la interpretación de la(s) evaluación(es) y examen(es) clínicos y paraclínicos explicados."
$autoriz_4 = "Entiendo que la realización de éstos es voluntaria y que puedo retirar mi consentimiento en cualquier momento antes de que sea realizada. Fui informado de las medidas que tomará para proteger la confidencialidad de mis resultados."

$legal_1 = "En cumplimiento de la Ley 23 de 1981 sobre normas de ética médica, la Ley 1581 de 2012 sobre protección de datos personales, y la Resolución 2346 de 2007 que regula las evaluaciones médicas ocupacionales, este documento tiene como propósito informar al trabajador sobre los alcances, objetivos y procedimientos de las evaluaciones médicas ocupacionales que serán realizadas, así como solicitar su autorización explícita para proceder."
$legal_2 = "VISAL RT, identificada con NIT 901210787-7, en cumplimiento de la Ley 1581 de 2012 y el Decreto Reglamentario 1377 de 2013 Régimen General de Protección de Datos reglamentarios de los derechos constitucionales de Habeas Data, le informa que la información personal y sensible que es recolectada, es tratada conforme a la normatividad vigente que establece la Protección de Datos Personales. Los Datos recolectados han sido y serán utilizados exclusivamente para la adecuada prestación del servicio. Como titular y de acuerdo con la Ley 1581 los derechos que le asisten son: derecho de acceso, actualización, rectificación y cancelación sobre la misma y puede ejercer su derecho de Habeas Data dada en canales habilitados de la institución."

# ================ Seccion Fecha superior ================
$secFecha = @{
    id = newId; type = "section"; label = "Encabezado"
    children = @( (Field "FECHA" "fecha_ml" "date" 4) )
}

# ================ Seccion 2 - Informacion ================
$secInfo = @{
    id = newId; type = "section"; label = "2. INFORMACIÓN SOBRE EL PROCEDIMIENTO"
    children = @(
        (P "Marque con X el procedimiento a realizar"),
        (SH "PROCEDIMIENTO: ATENCIÓN EN MEDICINA DEL TRABAJO Y MEDICINA LABORAL"),
        (Tabla "Descripción del procedimiento" "procedimientos" $colsSimple $rowsProc $true)
    )
}

# ================ Seccion Autorización ================
$secAutorizacion = @{
    id = newId; type = "section"; label = "Autorización del Trabajador"
    children = @(
        (Field "Yo (nombre)"          "nombre_autoriza" "text" 8),
        (Field "Documento No."        "doc_autoriza"    "text" 4),
        (Field "de (ciudad expedición)" "ciudad_autoriza" "text" 6),
        (P $autoriz_1),
        (P $autoriz_2),
        (Field "Empresa"                          "empresa_autoriza" "text" 6),
        (Field "Administradora de Riesgos Laborales (ARL)" "arl_autoriza"     "text" 6),
        (P $autoriz_3),
        (P $autoriz_4)
    )
}

# ================ Seccion Beneficios ================
$secBeneficios = @{
    id = newId; type = "section"; label = "BENEFICIOS"
    children = @( (Tabla "Beneficios" "beneficios" $colsSimple $rowsBenef $true) )
}

# ================ Seccion Riesgos ================
$secRiesgos = @{
    id = newId; type = "section"; label = "RIESGOS"
    children = @(
        (P "Marque con X los riesgos a los cuales se expone de acuerdo al procedimiento:"),
        (Tabla "Riesgos" "riesgos" $colsSimple $rowsRiesgos $true),
        (Field "Otro (especifique)" "riesgo_otro" "text" 12)
    )
}

# ================ Seccion Alternativas ================
$secAlternativas = @{
    id = newId; type = "section"; label = "OTRAS ALTERNATIVAS DISPONIBLES"
    children = @( (Tabla "Otras alternativas disponibles" "alternativas" $colsSimple $rowsAltern $true) )
}

# ================ Seccion Riesgo de no realizar ================
$secRiesgoNoRealizar = @{
    id = newId; type = "section"; label = "RIESGO DE NO REALIZAR EL PROCEDIMIENTO"
    children = @( (Tabla "Riesgo de no realizar el procedimiento" "riesgo_no_realizar" $colsSimple $rowsRiesgoNoRealizar $true) )
}

# ================ Seccion Consentimiento Informado (declaración legal) ================
$secLegal = @{
    id = newId; type = "section"; label = "CONSENTIMIENTO INFORMADO"
    children = @( (P $legal_1), (P $legal_2) )
}

# ================ Seccion Firmas del Trabajador ================
$secFirmasTrabajador = @{
    id = newId; type = "section"; label = "Firma del Trabajador"
    children = @(
        (Field "Nombre Completo y Firma del Trabajador" "nombre_firma_trabajador" "text" 12),
        (Field "No. Documento" "no_doc_trabajador" "text" 6),
        (Field "de (ciudad expedición)" "ciudad_doc_trabajador" "text" 6),
        (Field "Fecha de atención" "fecha_atencion_trabajador" "date" 6)
    )
}

# ================ Ensamblar ================
$schema.children = @(
    $datosPaciente,
    $secFecha,
    $secInfo,
    $secAutorizacion,
    $secBeneficios,
    $secRiesgos,
    $secAlternativas,
    $secRiesgoNoRealizar,
    $secLegal,
    $secFirmasTrabajador,
    $firmasAuto,
    $cierre
)
Write-Host ("Top-level ahora: {0} secciones" -f $schema.children.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='PP-FO-32' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_pp32_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK PP-FO-32 actualizado." -ForegroundColor Green
