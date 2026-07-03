# Rework-PPFO22-Psicologia.ps1
# Reconstruye PP-FO-22 CONSENTIMIENTO PSICOLOGIA segun docx fiel.
# 5 tablas seed con columna Marca (X).
#
# Preserva: Datos del Paciente (auto), Firmas (auto), prefill, Cierre.
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
$colsBenef = @( (Col "Beneficio" "beneficio" "text"), (Col "Marca" "marca" "text" @{ defaultValue = "" }) )
$colsRiesgo = @( (Col "Riesgo" "riesgo" "text"), (Col "Marca" "marca" "text" @{ defaultValue = "" }) )
$colsAltern = @( (Col "Alternativa" "alternativa" "text"), (Col "Marca" "marca" "text" @{ defaultValue = "" }) )
$colsConsec = @( (Col "Consecuencia" "consecuencia" "text"), (Col "Marca" "marca" "text" @{ defaultValue = "" }) )

# ================ Descripciones largas (docx literal) ================
$descAsesoria = "Consiste en una conversación entre el profesional de la psicología y el usuario que pretende que el psicólogo ofrezca un análisis de una situación, de acuerdo con los conocimientos profesionales que se derivan de sus estudios en materia de psicología y que con ello, pueda brindarle a la persona que consulta herramientas, ideas, aprendizajes o conocimientos que el usuario pueda aplicar para desenvolverse favorablemente en la situación específica por la que consulta. En la asesoría psicológica no se pretende un cambio en la forma de ser del usuario, ni darle un tratamiento clínico o terapéutico de alguna condición de salud mental. Es una intervención de tipo educativo."
$descConsejeria = "Este tipo de intervención consiste en que el profesional propicie, junto con la participación activa del consultante, una interacción entre ambos para facilitarle al consultante procesos como expresión de sí mismo, análisis de sus experiencias, profundización en los pensamientos, significados, afectos y comportamientos respecto a las circunstancias de su vida. Esto se hace para ayudarle al usuario a desarrollar habilidades de afrontamiento de dichas circunstancias para optimizar su calidad de vida."
$descPsicoterapia = "Como en la consejería, el profesional buscará desarrollar técnicas de interacción con el consultante que le permitan a este último desarrollar habilidades. Estas habilidades son para que el consultante propicie cambios en sí mismo y su entorno para mejorar una circunstancia que afecta o puede afectar el desarrollo saludable de la persona."

# ================ SeedRows ================
$rowsProc = @(
    @("ASESORÍA PSICOLÓGICA", $descAsesoria, ""),
    @("CONSEJERÍA",           $descConsejeria, ""),
    @("PSICOTERAPIA:",        $descPsicoterapia, "")
)

$rowsBenef = @(
    @("Fortalecimiento de habilidades de afrontamiento emocional.", ""),
    @("Mejoramiento del bienestar psicológico y emocional.", ""),
    @("Identificación de factores emocionales, familiares o sociales que afectan la salud del usuario.", ""),
    @("Orientación para el manejo de situaciones de estrés, ansiedad, duelo o crisis emocionales.", ""),
    @("Desarrollo de estrategias para el autocuidado y regulación emocional.", ""),
    @("Fortalecimiento de redes de apoyo familiar y social.", ""),
    @("Mejoramiento de la adaptación frente a enfermedades, cambios o situaciones difíciles.", ""),
    @("Acompañamiento durante procesos terapéuticos y emocionales.", "")
)

$rowsRiesgos = @(
    @("Sentimientos de difícil manejo", ""),
    @("Entrar en contacto con recuerdos desagradables o traumáticos de manera involuntaria", ""),
    @("Malestar, Ansiedad, Tristeza", ""),
    @("Intranquilidad, Miedo", ""),
    @("Frustración", ""),
    @("Rabia", ""),
    @("Incomodidad durante el abordaje de situaciones personales.", "")
)

$rowsAltern = @(
    @("Alta voluntaria conforme a decisión del usuario y/o responsable.", ""),
    @("Reprogramación del proceso terapéutico conforme a necesidad clínica", "")
)

$rowsConsec = @(
    @("Persistencia o empeoramiento del malestar emocional.", ""),
    @("Dificultad en el manejo de situaciones de estrés, ansiedad o duelo.", ""),
    @("Alteración en la calidad de vida y bienestar emocional.", ""),
    @("Dificultades en la adaptación frente a enfermedad o situaciones personales.", ""),
    @("Disminución de estrategias de afrontamiento y autocuidado.", ""),
    @("Riesgo de progresión de alteraciones emocionales o afectación de la salud mental conforme a la condición del usuario.", "")
)

# ================ Cargar y localizar auto sections ================
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='PP-FO-22' AND tenant_id='$TenantId';"
$schema = $raw | ConvertFrom-Json -AsHashtable

$datosPaciente = $null; $cierre = $null; $firmasAuto = $null
foreach ($sec in $schema.children) {
    $lbl = [string]$sec["label"]
    if ($lbl -eq "Datos del Paciente (auto-llenado)") { $datosPaciente = $sec }
    elseif ($lbl -eq "Cierre") { $cierre = $sec }
    elseif ($lbl -eq "Firmas (auto-llenadas)") { $firmasAuto = $sec }
}
if ($null -eq $datosPaciente) { throw "No encontre seccion 'Datos del Paciente (auto-llenado)'" }
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

# ================ Declaraciones textuales (docx literal) ================
$decl_paciente_1 = "Me han explicado y he comprendido satisfactoriamente la esencia y el propósito de este procedimiento, también me han aclarado todas las dudas y me han dicho los posibles riesgos y complicaciones, así como las otras alternativas de tratamiento."
$decl_paciente_2 = "Doy mi consentimiento para que me realicen el procedimiento descrito anteriormente y los procedimientos complementarios que sean necesarios o convenientes mediante la realización de este, a criterio de los profesionales que lo llevan a cabo."
$decl_responsable = "sé que el paciente ha sido considerado por ahora incapaz de tomar por sí mismo la decisión de aceptar o rechazar el procedimiento. También se me han explicado los riesgos y complicaciones, así como las otras alternativas de tratamiento. Soy consciente que no existen garantías absolutas de los resultados del procedimiento. He comprendido todo lo anterior perfectamente y por ello doy mi consentimiento para que los profesionales tratantes y el personal auxiliar que precise, le realicen este procedimiento. Puedo revocar este consentimiento cuando en bien del paciente se considere oportuno."
$decl_profesional = "como profesional tratante, he informado al paciente sobre la esencia y el propósito del procedimiento descrito anteriormente, de sus alternativas, posibles riesgos, resultados esperados y que no existen garantías absolutas de los resultados del procedimiento."

# ================ Seccion 2 ================
$secInfo = @{
    id = newId; type = "section"; label = "2. INFORMACIÓN SOBRE EL PROCEDIMIENTO"
    children = @(
        (P "Marque con X el procedimiento a realizar"),
        (SH "PROCEDIMIENTOS"),
        (Tabla "Procedimientos" "procedimientos" $colsProc $rowsProc $true),
        (SH "BENEFICIOS DEL PROCEDIMIENTO"),
        (Tabla "Beneficios del procedimiento" "beneficios" $colsBenef $rowsBenef $true),
        (P "Marque con X los riesgos a los cuales se expone de acuerdo con el procedimiento:"),
        (SH "RIESGOS DEL PROCEDIMIENTO"),
        (Tabla "Riesgos del procedimiento" "riesgos" $colsRiesgo $rowsRiesgos $true),
        (SH "OTRAS ALTERNATIVAS DISPONIBLES"),
        (Tabla "Otras alternativas disponibles" "alternativas" $colsAltern $rowsAltern $true),
        (SH "CONSECUENCIAS DE NO REALIZAR EL PROCEDIMIENTO"),
        (Tabla "Consecuencias de no realizar el procedimiento" "consecuencias" $colsConsec $rowsConsec $true)
    )
}

# ================ Seccion 3 ================
$secDeclPaciente = @{
    id = newId; type = "section"; label = "3. DECLARACIÓN DEL PACIENTE"
    children = @(
        (P $decl_paciente_1),
        (P $decl_paciente_2),
        (Field "Firma del Paciente" "firma_declaracion_paciente" "text" 8),
        (Field "CC" "cc_declaracion_paciente" "text" 4)
    )
}

# ================ Seccion 4 ================
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

# ================ Seccion 5 ================
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
$schema.children = @($datosPaciente, $secInfo, $secDeclPaciente, $secDeclResponsable, $secDeclProfesional, $firmasAuto, $cierre)
Write-Host ("Top-level ahora: {0} secciones" -f $schema.children.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='PP-FO-22' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_pp22_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK PP-FO-22 actualizado." -ForegroundColor Green
