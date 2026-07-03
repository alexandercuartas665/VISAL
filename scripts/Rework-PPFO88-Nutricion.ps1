# Rework-PPFO88-Nutricion.ps1
# Reconstruye PP-FO-88 CONSENTIMIENTO INFORMADO NUTRICION segun docx fiel.
# 5 tablas seed con columna Marca.
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

# ================ SeedRows ================
$rowsProc = @(
    @("EVALUACIÓN NUTRICIONAL", "Evaluación del estado nutricional mediante indicadores antropométricos, bioquímicos y clínicos.", ""),
    @("PLAN DE ALIMENTACIÓN PERSONALIZADO", "Desarrollo de un plan nutricional ajustado a las necesidades del paciente según su condición de salud.", ""),
    @("EDUCACIÓN NUTRICIÓN", "Asesoría sobre hábitos de alimentación saludables según el estado de salud del paciente.", ""),
    @("SEGUIMIENTO", "Seguimiento a los tratamientos de los pacientes, a través de consultas fijadas", "")
)

$rowsBenef = @(
    @("Identificación del estado nutricional y riesgo de desnutrición.", ""),
    @("Seguimiento y control nutricional conforme a la condición clínica del usuario.", ""),
    @("Desarrollo de planes de alimentación personalizados.", ""),
    @("Educación alimentaria al paciente y/o cuidador.", ""),
    @("Prevención de complicaciones asociadas a malnutrición.", ""),
    @("Apoyo en el mejoramiento del estado general y calidad de vida del usuario.", ""),
    @("Orientación nutricional conforme a patologías de base y requerimientos clínicos.", "")
)

$rowsRiesgos = @(
    @("Error en la interpretación o diagnóstico nutricional.", ""),
    @("Intolerancia o baja adherencia al plan de alimentación indicado.", ""),
    @("Reacciones gastrointestinales asociadas a cambios en alimentación.", ""),
    @("Riesgo de desnutrición o alteraciones metabólicas relacionadas con enfermedad de base.", ""),
    @("Persistencia de hábitos alimentarios inadecuados.", "")
)

$rowsAltern = @(
    @("Alta voluntaria conforme a decisión del usuario y/o responsable.", ""),
    @("Reprogramación de la consulta nutricional conforme a necesidad clínica.", "")
)

$rowsConsec = @(
    @("Deterioro del estado nutricional del usuario.", ""),
    @("Riesgo de desnutrición o complicaciones metabólicas.", ""),
    @("Disminución de la tolerancia alimentaria", ""),
    @("Retraso en el proceso de recuperación clínica y funcional.", ""),
    @("Alteración en la adherencia y continuidad del tratamiento integral", "")
)

# ================ Cargar y localizar auto sections ================
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='PP-FO-88' AND tenant_id='$TenantId';"
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
$decl_paciente_1 = "Me han explicado y he comprendido satisfactoriamente el propósito del procedimiento nutricional, así como los posibles riesgos, beneficios y alternativas, también me han aclarado todas las dudas y me han dicho los posibles riesgos y complicaciones, así como las otras alternativas de tratamiento."
$decl_paciente_2 = "Doy mi consentimiento para que me realicen el procedimiento descrito anteriormente y los procedimientos complementarios que sean necesarios o convenientes mediante la realización de este, a criterio de los profesionales que lo llevan a cabo."
$decl_responsable = "sé que el paciente ha sido considerado por ahora incapaz de tomar por sí mismo la decisión de aceptar o rechazar el procedimiento. También se me han explicado los riesgos y complicaciones, así como las otras alternativas de tratamiento. Soy consciente que no existen garantías absolutas de los resultados del procedimiento. He comprendido todo lo anterior perfectamente y por ello doy mi consentimiento para que los profesionales tratantes y el personal auxiliar que precise, le realicen este procedimiento. Puedo revocar este consentimiento cuando en bien del paciente se considere oportuno."
$decl_profesional = "como profesional tratante, he informado al paciente sobre la esencia y el propósito del procedimiento descrito anteriormente, de sus alternativas, posibles riesgos, resultados esperados y que no existen garantías absolutas de los resultados del procedimiento."

# ================ Seccion Fecha superior ================
$secFecha = @{
    id = newId; type = "section"; label = "Encabezado"
    children = @( (Field "FECHA" "fecha_nut" "date" 4) )
}

# ================ Seccion 2 - Informacion ================
$secInfo = @{
    id = newId; type = "section"; label = "2. INFORMACIÓN SOBRE EL PROCEDIMIENTO"
    children = @(
        (P "Marque con X el procedimiento a realizar"),
        (SH "PROCEDIMIENTOS"),
        (Tabla "Procedimientos" "procedimientos" $colsProc $rowsProc $true),
        (SH "BENEFICIOS DEL PROCEDIMIENTO"),
        (Tabla "Beneficios del procedimiento" "beneficios" $colsSimple $rowsBenef $true),
        (P "Marque con X los riesgos a los cuales se expone de acuerdo al procedimiento:"),
        (SH "RIESGOS DEL PROCEDIMIENTO"),
        (Tabla "Riesgos del procedimiento" "riesgos" $colsSimple $rowsRiesgos $true),
        (SH "OTRAS ALTERNATIVAS DISPONIBLES"),
        (Tabla "Otras alternativas disponibles" "alternativas" $colsSimple $rowsAltern $true),
        (SH "CONSECUENCIAS DE NO REALIZAR EL PROCEDIMIENTO"),
        (Tabla "Consecuencias de no realizar el procedimiento" "consecuencias" $colsSimple $rowsConsec $true)
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
        (Field "No Registro Profesional"  "reg_profesional"                 "text" 6)
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
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='PP-FO-88' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_pp88_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK PP-FO-88 actualizado." -ForegroundColor Green
