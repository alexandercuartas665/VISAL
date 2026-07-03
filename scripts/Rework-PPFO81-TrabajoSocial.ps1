# Rework-PPFO81-TrabajoSocial.ps1
# Reconstruye PP-FO-81 CONSENTIMIENTO INFORMADO TRABAJO SOCIAL segun docx fiel.
# 5 tablas seed con columna Marca (procedimientos, beneficios, riesgos, alternativas, riesgo_no_realizar).
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
$colsProc = @(
    (Col "Procedimiento" "procedimiento" "text"),
    (Col "Descripción"   "descripcion"   "text"),
    (Col "Marca"         "marca"         "text" @{ defaultValue = "" })
)
$colsSimple = @( (Col "Item" "item" "text"), (Col "Marca" "marca" "text" @{ defaultValue = "" }) )

# ================ Descripciones largas (docx literal) ================
$descObservacion = "Se realiza en el lugar donde se desarrollan los fenómenos observables. Es un procedimiento que dirige la atención a un hecho de la realidad, encontrando sentido a lo observado, en el que el/la trabajador/a social deberá examinar, registrar, analizar y elaborar conclusiones."
$descEntrevista = "Es la técnica más importante en el Trabajo Social individualizado, porque representa la relación interpersonal de apoyo profesional entre el/la usuario/a y el/la trabajador/a social, a través de la cual se intercambia información, constituyéndose como el elemento básico para garantizar un cambio en la situación problemática del caso."
$descVisita = "Es una variante de la entrevista, que permite al profesional completar la valoración del caso social utilizando la información obtenida en el contexto natural de la persona usuaria, o en el lugar de residencia habitual. La información obtenida utilizando esta técnica permite verificar la situación real del caso, ya que los datos facilitados en las entrevistas de despacho, pueden diferir de los recogidos en el domicilio. La visita a domicilio requiere de ciertas habilidades sociales y de comunicación: mirada analítica, escucha activa, preguntas precisas, habilidad para controlar situaciones imprevistas, capacidad de relación."

# ================ SeedRows ================
$rowsProc = @(
    @("OBSERVACIÓN",       $descObservacion, ""),
    @("ENTREVISTA",        $descEntrevista, ""),
    @("VISITA DOMICILIARIA", $descVisita, "")
)

$rowsBenef = @(
    @("Identificación de necesidades, fortalezas y recursos del usuario, su familia y entorno.", ""),
    @("Valoración integral de las condiciones sociales, familiares y ambientales que pueden influir en su situación actual.", ""),
    @("Orientación sobre rutas institucionales, programas, beneficios y servicios de apoyo disponibles.", ""),
    @("Fortalecimiento de las redes de apoyo familiares, comunitarias e institucionales.", ""),
    @("Promoción de la participación activa del usuario en la búsqueda de soluciones a las situaciones identificadas.", ""),
    @("Obtención de información relevante para la formulación de un plan de intervención social acorde con las necesidades del usuario.", ""),
    @("Contribución al mejoramiento de las condiciones sociales y familiares que puedan afectar su bienestar.", ""),
    @("Acompañamiento y seguimiento social durante el proceso de atención.", "")
)

$rowsRiesgos = @(
    @("Sentimientos de difícil manejo al abordar situaciones personales o familiares.", ""),
    @("Recordar experiencias difíciles relacionadas con la situación social o familiar.", ""),
    @("Incomodidad al proporcionar información personal, familiar o socioeconómica.", ""),
    @("Frustración ante situaciones sociales que no puedan resolverse de manera inmediata.", ""),
    @("Incomodidad derivada de la visita domiciliaria o de la observación del entorno.", "")
)

$rowsAltern = @(
    @("Alta voluntaria conforme a decisión del usuario y/o responsable.", ""),
    @("Reprogramación de la entrevista, observación o visita domiciliaria de acuerdo con las necesidades del caso.", "")
)

$rowsRiesgoNoRealizar = @(
    @("Limitación en la identificación de necesidades, riesgos o factores sociales que puedan afectar el bienestar del usuario.", ""),
    @("Dificultad para realizar una valoración integral de la situación sociofamiliar.", ""),
    @("Menor acceso a orientación sobre recursos, programas o servicios de apoyo disponibles.", ""),
    @("Limitación para la formulación de planes de intervención social ajustados a las necesidades identificadas.", ""),
    @("Dificultad para el seguimiento y acompañamiento de situaciones sociales que requieran intervención profesional.", ""),
    @("Posible permanencia de factores sociales o familiares que afectan la calidad de vida y el bienestar del usuario.", "")
)

# ================ Cargar y localizar auto sections ================
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='PP-FO-81' AND tenant_id='$TenantId';"
$schema = $raw | ConvertFrom-Json -AsHashtable

$datosPaciente = $null; $cierre = $null; $firmasAuto = $null
foreach ($sec in $schema.children) {
    $lbl = [string]$sec["label"]
    if ($lbl -eq "Datos del Paciente (auto-llenado)") { $datosPaciente = $sec }
    elseif ($lbl -eq "Cierre") { $cierre = $sec }
    elseif ($lbl -eq "Firmas (auto-llenadas)") { $firmasAuto = $sec }
}
if ($null -eq $datosPaciente) {
    $datosPaciente = @{
        id = "auto-datos-paciente"; type = "section"; label = "Datos del Paciente (auto-llenado)"
        children = @(
            (Field "Nombre del paciente"    "nombre_paciente_consent"  "text"   12),
            (Field "Tipo de documento"      "tipo_documento_consent"   "text"   6),
            (Field "Número de documento"    "numero_documento_consent" "text"   6),
            (Field "Edad"                   "edad_consent"             "number" 4),
            (Field "Fecha de atención"      "fecha_atencion_consent"   "date"   4)
        )
    }
    Write-Host "Seccion Datos del Paciente CREADA" -ForegroundColor Yellow
} else { Write-Host "Seccion Datos del Paciente PRESERVADA" -ForegroundColor Green }
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

# ================ Declaraciones textuales (docx literal) ================
$decl_paciente_1 = "Me han explicado y he comprendido satisfactoriamente la esencia y el propósito de este procedimiento, también me han aclarado todas las dudas y me han dicho los posibles riesgos y complicaciones, así como las otras alternativas de tratamiento."
$decl_paciente_2 = "Doy mi consentimiento para que me realicen el procedimiento descrito anteriormente y los procedimientos complementarios que sean necesarios o convenientes mediante la realización de este, a criterio de los profesionales que lo llevan a cabo."
$decl_responsable = "sé que el paciente ha sido considerado por ahora incapaz de tomar por sí mismo la decisión de aceptar o rechazar el procedimiento. También se me han explicado los riesgos y complicaciones, así como las otras alternativas de tratamiento. Soy consciente que no existen garantías absolutas de los resultados del procedimiento. He comprendido todo lo anterior perfectamente y por ello doy mi consentimiento para que los profesionales tratantes y el personal auxiliar que precise, le realicen este procedimiento. Puedo revocar este consentimiento cuando en bien del paciente se considere oportuno."
$decl_profesional = "como profesional tratante, he informado al paciente sobre la esencia y el propósito del procedimiento descrito anteriormente, de sus alternativas, posibles riesgos, resultados esperados y que no existen garantías absolutas de los resultados del procedimiento."

# ================ Seccion Fecha superior ================
$secFecha = @{
    id = newId; type = "section"; label = "Encabezado"
    children = @( (Field "FECHA" "fecha_ts" "date" 4) )
}

# ================ Seccion 2 - Informacion ================
$secInfo = @{
    id = newId; type = "section"; label = "2. INFORMACIÓN SOBRE EL PROCEDIMIENTO"
    children = @(
        (P "Marque con X el procedimiento a realizar"),
        (SH "PROCEDIMIENTOS"),
        (Tabla "Procedimientos" "procedimientos" $colsProc $rowsProc $true),
        (SH "BENEFICIOS"),
        (Tabla "Beneficios" "beneficios" $colsSimple $rowsBenef $true),
        (P "Marque con X los riesgos a los cuales se expone de acuerdo al procedimiento:"),
        (SH "RIESGOS"),
        (Tabla "Riesgos" "riesgos" $colsSimple $rowsRiesgos $true),
        (Field "Otro (especifique)" "riesgo_otro" "text" 12),
        (SH "OTRAS ALTERNATIVAS DISPONIBLES"),
        (Tabla "Otras alternativas disponibles" "alternativas" $colsSimple $rowsAltern $true),
        (SH "RIESGOS DE NO REALIZAR EL PROCEDIMIENTO"),
        (Tabla "Riesgos de no realizar el procedimiento" "riesgo_no_realizar" $colsSimple $rowsRiesgoNoRealizar $true)
    )
}

# ================ Seccion 3 - Declaracion Paciente ================
$secDeclPaciente = @{
    id = newId; type = "section"; label = "3. DECLARACIÓN DEL PACIENTE"
    children = @(
        (P $decl_paciente_1),
        (P $decl_paciente_2),
        (Field "Firma del Paciente" "firma_declaracion_paciente" "text" 8),
        (Field "CC" "cc_declaracion_paciente" "text" 4)
    )
}

# ================ Seccion 4 - Declaracion Responsable ================
$secDeclResponsable = @{
    id = newId; type = "section"; label = "4. DECLARACIÓN DEL RESPONSABLE DEL PACIENTE (Solo en caso de Incapacidad del Paciente)"
    children = @(
        (Field "Yo (nombre del responsable)" "nombre_responsable" "text" 12),
        (Field "Nombre del paciente" "nombre_paciente_incap" "text" 8),
        (Field "N° de identificación del paciente" "no_id_paciente_incap" "text" 4),
        (P $decl_responsable),
        (Field "Firma del Paciente / Responsable" "firma_responsable" "text" 8),
        (Field "CC" "cc_responsable" "text" 4)
    )
}

# ================ Seccion 5 - Declaracion Profesional ================
$secDeclProfesional = @{
    id = newId; type = "section"; label = "5. DECLARACIÓN DEL PROFESIONAL TRATANTE"
    children = @(
        (Field "Yo (nombre del profesional)" "nombre_profesional_declaracion" "text" 12),
        (P $decl_profesional),
        (Field "Firma del profesional" "firma_declaracion_profesional" "text" 8),
        (Field "CC" "cc_declaracion_profesional" "text" 4),
        (Field "No Registro Profesional" "reg_profesional" "text" 6),
        (Field "Cargo" "cargo_profesional" "text" 6)
    )
}

# ================ Ensamblar ================
$schema.children = @(
    $datosPaciente,
    $secFecha,
    $secInfo,
    $secDeclPaciente,
    $secDeclResponsable,
    $secDeclProfesional,
    $firmasAuto,
    $cierre
)
Write-Host ("Top-level ahora: {0} secciones" -f $schema.children.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='PP-FO-81' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_pp81_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK PP-FO-81 actualizado." -ForegroundColor Green
