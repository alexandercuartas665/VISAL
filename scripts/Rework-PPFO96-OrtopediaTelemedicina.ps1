# Rework-PPFO96-OrtopediaTelemedicina.ps1
# Reconstruye PP-FO-96 CONSENTIMIENTO INFORMADO ORTOPEDIA Y TRAUMATOLOGIA MODALIDAD TELEMEDICINA.
# 5 tablas seed con columna Marca. Declaraciones especificas de telemedicina.
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
$descConsulta = "Evaluación inicial con el médico especialista en Ortopedia y Traumatología. Incluye identificación segura del usuario, revisión de antecedentes personales y médicos, motivo de consulta, síntomas actuales, análisis de estudios previos disponibles y orientación diagnóstica inicial, con el fin de establecer un plan de manejo acorde a la condición clínica identificada."
$descVerif = "Revisión y análisis de diagnósticos ortopédicos, traumáticos y demás condiciones médicas asociadas que puedan influir en la evolución clínica, el tratamiento o el pronóstico del paciente."
$descRevTrat = "Evaluación del cumplimiento y respuesta a los tratamientos médicos, farmacológicos, quirúrgicos o de rehabilitación previamente indicados, identificando la necesidad de ajustes terapéuticos según la evolución clínica observada."
$descRevLab = "Análisis e interpretación de estudios complementarios como radiografías, tomografías, resonancias magnéticas, ecografías, laboratorios y demás ayudas diagnósticas aportadas por el paciente, con el fin de apoyar el proceso diagnóstico y la toma de decisiones terapéuticas."

# ================ SeedRows ================
$rowsProc = @(
    @("CONSULTA POR PRIMERA VEZ", $descConsulta, ""),
    @("VERIFICACIÓN DE DIAGNOSTICOS PREVIOS Y COMORBILIDADES ASOCIADAS", $descVerif, ""),
    @("REVISION DE TRATAMIENTOS INSTAURADOS Y ADHERENCIA TERAPEUTICA", $descRevTrat, ""),
    @("REVISION E INTERPRETACION DE LABORATORIOS, IMÁGENES DIAGNÓSTICAS Y DEMAS AYUDAS DIAGNOSTICAS DISPONIBLES", $descRevLab, "")
)

$rowsBenef = @(
    @("Valoración especializada por médico ortopedista y traumatólogo mediante modalidad de telemedicina.", ""),
    @("Identificación de alteraciones osteomusculares, articulares, traumáticas o degenerativas", ""),
    @("Orientación diagnóstica y terapéutica de acuerdo con la condición clínica del paciente.", ""),
    @("Seguimiento de lesiones, patologías musculoesqueléticas y procesos postquirúrgicos.", ""),
    @("Evaluación de la evolución clínica mediante revisión de ayudas diagnósticas.", ""),
    @("Optimización y ajuste de tratamientos instaurados según respuesta clínica.", ""),
    @("Oportunidad para resolver dudas y recibir recomendaciones sobre cuidados, rehabilitación y prevención de complicaciones.", "")
)

$rowsRiesgos = @(
    @("Posibilidad de limitaciones diagnósticas derivadas de la ausencia de examen físico presencial.", ""),
    @("Posibilidad de error diagnóstico debido a información clínica insuficiente o calidad inadecuada de imágenes o documentos aportados.", ""),
    @("Necesidad de valoración presencial complementaria para confirmar diagnósticos o definir conductas.", "")
)

$rowsAltern = @(
    @("Alta voluntaria conforme a decisión del usuario y/o responsable.", ""),
    @("Reprogramación de la consulta por telemedicina.", ""),
    @("Consulta presencial con especialista en Ortopedia y Traumatología.", "")
)

$rowsRiesgoNoRealizar = @(
    @("Persistencia o progresión de la enfermedad o lesión osteomuscular.", ""),
    @("Retraso en el diagnóstico de patologías ortopédicas o traumáticas.", "")
)

# ================ Cargar y localizar auto sections ================
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='PP-FO-96' AND tenant_id='$TenantId';"
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

# ================ Declaraciones textuales de TELEMEDICINA (docx literal) ================
$decl_paciente = "Me han explicado y he comprendido satisfactoriamente la esencia y el propósito de este procedimiento realizado mediante la modalidad de telemedicina. También me han aclarado todas las dudas y me han informado los posibles riesgos, limitaciones, beneficios y alternativas disponibles. Entiendo que la atención se realizará a través de tecnologías de información y comunicación y que, en caso de ser necesario, podrá requerirse una valoración presencial complementaria. Doy mi consentimiento para que me realicen el procedimiento descrito anteriormente y los procedimientos complementarios que sean necesarios o convenientes a criterio del profesional tratante."
$decl_responsable_1 = "sé que el paciente ha sido considerado por ahora incapaz de tomar por sí mismo la decisión de aceptar o rechazar el procedimiento. También se me ha informado que la atención se realizará mediante modalidad de telemedicina, incluyendo sus beneficios, limitaciones, riesgos y alternativas disponibles. Comprendo que, dependiendo de la condición clínica del paciente, podrá requerirse valoración presencial complementaria."
$decl_responsable_2 = "Soy consciente que no existen garantías absolutas de los resultados del procedimiento. He comprendido todo lo anterior perfectamente y por ello doy mi consentimiento para que los profesionales tratantes y el personal auxiliar que precise, le realicen este procedimiento. Puedo revocar este consentimiento cuando en bien del paciente se considere oportuno."
$decl_profesional = "como profesional tratante, he informado al paciente sobre la esencia y el propósito del procedimiento descrito anteriormente, de sus alternativas, posibles riesgos, resultados esperados y que no existen garantías absolutas de los resultados del procedimiento."

# ================ Secciones ================
$secFecha = @{ id = newId; type = "section"; label = "Encabezado"; children = @( (Field "FECHA" "fecha_tel" "date" 4) ) }

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
        (Field "Otros (especifique)" "riesgo_otros" "text" 12),
        (SH "OTRAS ALTERNATIVAS DISPONIBLES"),
        (Tabla "Otras alternativas disponibles" "alternativas" $colsSimple $rowsAltern $true),
        (SH "RIESGOS DE NO REALIZAR EL PROCEDIMIENTO"),
        (Tabla "Riesgos de no realizar el procedimiento" "riesgo_no_realizar" $colsSimple $rowsRiesgoNoRealizar $true)
    )
}

$secDeclPaciente = @{
    id = newId; type = "section"; label = "3. DECLARACIÓN DEL PACIENTE"
    children = @(
        (P $decl_paciente),
        (Field "Firma del Paciente" "firma_declaracion_paciente" "text" 8),
        (Field "CC" "cc_declaracion_paciente" "text" 4)
    )
}

$secDeclResponsable = @{
    id = newId; type = "section"; label = "4. DECLARACIÓN DEL RESPONSABLE DEL PACIENTE (Solo en caso de Incapacidad del Paciente)"
    children = @(
        (Field "Yo (nombre del responsable)" "nombre_responsable" "text" 12),
        (Field "Nombre del paciente" "nombre_paciente_incap" "text" 8),
        (Field "N° de identificación del paciente" "no_id_paciente_incap" "text" 4),
        (P $decl_responsable_1),
        (P $decl_responsable_2),
        (Field "Firma del Paciente / Responsable" "firma_responsable" "text" 8),
        (Field "CC" "cc_responsable" "text" 4)
    )
}

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
    $datosPaciente, $secFecha, $secInfo,
    $secDeclPaciente, $secDeclResponsable, $secDeclProfesional,
    $firmasAuto, $cierre
)
Write-Host ("Top-level ahora: {0} secciones" -f $schema.children.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='PP-FO-96' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_pp96_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK PP-FO-96 actualizado." -ForegroundColor Green
