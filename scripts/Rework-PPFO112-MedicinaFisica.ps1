# Rework-PPFO112-MedicinaFisica.ps1
# Reconstruye PP-FO-112 CONSENTIMIENTO MEDICINA FISICA Y REHABILITACION
# segun docx fiel: tablas seed con columna "Marca" para X, guiones bajos
# como fields de entrada, numerales 2/3/4/5 respetados.
#
# Preserva:
#   - Seccion "Datos del Paciente (auto-llenado)" (id="auto-datos-paciente")
#   - prefill_routes_json (rutas Paciente + Sistema)
#   - Seccion "Cierre"
#
# Agrega:
#   - Seccion "Firmas (auto-llenadas)" al final (patron sistema)
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
function P([string]$content) { @{ id=newId; type="text"; textStyle="paragraph"; content=$content } }
function Field([string]$label, [string]$name, [string]$ft, [int]$width=12, $extra=@{}) {
    $f = @{ id=newId; type="field"; fieldType=$ft; label=$label; name=$name; widthColumns=$width; allowCustom=$false; required=$false }
    foreach ($k in $extra.Keys) { $f[$k] = $extra[$k] }
    return $f
}

# ================ Columnas de tablas ================
$colsProc = @(
    (Col "Procedimiento" "procedimiento" "text"),
    (Col "Descripción"   "descripcion"   "text"),
    (Col "Marca"         "marca"         "text" @{ defaultValue = "" })
)
$colsBenef = @(
    (Col "Beneficio" "beneficio" "text"),
    (Col "Marca"     "marca"     "text" @{ defaultValue = "" })
)
$colsRiesgo = @(
    (Col "Riesgo" "riesgo" "text"),
    (Col "Marca"  "marca"  "text" @{ defaultValue = "" })
)
$colsAltern = @(
    (Col "Alternativa" "alternativa" "text"),
    (Col "Marca"       "marca"       "text" @{ defaultValue = "" })
)
$colsRiesgoNR = @(
    (Col "Riesgo" "riesgo" "text"),
    (Col "Marca"  "marca"  "text" @{ defaultValue = "" })
)

# ================ SeedRows fieles al docx ================
$rowsProc = @(
    @("CONSULTA EN FISIATRÍA",   "Evaluación médica especializada en rehabilitación y funcionalidad, enfocada en la recuperación de alteraciones musculoesqueléticas, neuromusculares y del aparato locomotor.", ""),
    @("VALORACIÓN FUNCIONAL",    "Examen detallado de movilidad articular, fuerza muscular, tono muscular, reflejos, coordinación y propiocepción.", ""),
    @("DIAGNÓSTICO FISIÁTRICO",  "Identificación de disfunciones del sistema musculoesquelético y planificación del tratamiento.", ""),
    @("PLAN DE MANEJO TERAPÉUTICO", "Implementación de estrategias de rehabilitación, recomendaciones ergonómicas y derivación a terapias complementarias según la condición del paciente.", "")
)
$rowsBenef = @(
    @("Valoración integral de la funcionalidad física del paciente.", ""),
    @("Identificación de alteraciones musculoesqueléticas, neuromusculares y del aparato locomotor.", ""),
    @("Detección de limitaciones en movilidad, fuerza muscular, coordinación, equilibrio y desempeño funcional.", ""),
    @("Formulación de un plan de rehabilitación individualizado de acuerdo con la condición clínica del paciente.", ""),
    @("Prevención de complicaciones derivadas de la inmovilidad, discapacidad o deterioro funcional.", ""),
    @("Mejoramiento de la independencia y capacidad para realizar actividades de la vida diaria.", ""),
    @("Seguimiento de la evolución funcional y ajuste de las estrategias de rehabilitación según la respuesta al tratamiento.", "")
)
$rowsRiesgos = @(
    @("Posibilidad de error diagnóstico debido a hallazgos clínicos limitados o evolución de la condición de salud.", ""),
    @("Molestia o dolor leve durante la valoración física o funcional.", ""),
    @("Posibilidad de error en la formulación o planificación del manejo terapéutico.", "")
)
$rowsAltern = @(
    @("Alta voluntaria conforme a decisión del paciente y/o responsable.", ""),
    @("Reprogramación de la valoración fisiátrica según necesidad clínica.", ""),
    @("Continuación del manejo por el médico tratante o remisión a otras especialidades según criterio médico.", "")
)
$rowsRiesgosNR = @(
    @("Persistencia o progresión de las limitaciones funcionales existentes.", ""),
    @("Mayor riesgo de pérdida de movilidad, fuerza muscular o independencia funcional.", ""),
    @("Dificultad para identificar oportunamente alteraciones musculoesqueléticas o neuromusculares.", ""),
    @("Disminución de las oportunidades para mejorar la calidad de vida y el desempeño en las actividades cotidianas.", "")
)

# ================ Cargar schema actual y localizar auto sections ================
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='PP-FO-112' AND tenant_id='$TenantId';"
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

# Si no existe la seccion Firmas auto, la creamos
if ($null -eq $firmasAuto) {
    $firmasAuto = @{
        id = newId; type = "section"; label = "Firmas (auto-llenadas)"
        children = @(
            (Field "Firma del paciente (URL)"     "firma_paciente_consent"     "text" 12),
            (Field "Firma del profesional (URL)"  "firma_profesional_consent"  "text" 12)
        )
    }
    Write-Host "Seccion Firmas (auto-llenadas) CREADA" -ForegroundColor Yellow
} else {
    Write-Host "Seccion Firmas (auto-llenadas) PRESERVADA" -ForegroundColor Green
}
if ($null -eq $cierre) {
    $cierre = @{
        id = newId; type = "section"; label = "Cierre"
        children = @( (Field "Observaciones / Conclusiones" "observaciones_cierre" "textarea" 12 @{ enableVoice = $true }) )
    }
}

# Declaraciones textuales (respetando el docx)
$decl_paciente_1 = "Me han explicado y he comprendido satisfactoriamente la esencia y el propósito de este procedimiento, también me han aclarado todas las dudas y me han dicho los posibles riesgos y complicaciones, así como las otras alternativas de tratamiento."
$decl_paciente_2 = "Doy mi consentimiento para que me realicen el procedimiento descrito anteriormente y los procedimientos complementarios que sean necesarios o convenientes mediante la realización de este, a criterio de los profesionales que lo llevan a cabo."
$decl_responsable = "sé que el paciente ha sido considerado por ahora incapaz de tomar por sí mismo la decisión de aceptar o rechazar el procedimiento. También se me han explicado los riesgos y complicaciones, así como las otras alternativas de tratamiento. Soy consciente que no existen garantías absolutas de los resultados del procedimiento. He comprendido todo lo anterior perfectamente y por ello doy mi consentimiento para que los profesionales tratantes y el personal auxiliar que precise, le realicen este procedimiento. Puedo revocar este consentimiento cuando en bien del paciente se considere oportuno."
$decl_profesional = "como profesional tratante, he informado al paciente sobre la esencia y el propósito del procedimiento descrito anteriormente, de sus alternativas, posibles riesgos, resultados esperados y que no existen garantías absolutas de los resultados del procedimiento."

# ================ Seccion 2: INFORMACION SOBRE EL PROCEDIMIENTO ================
$secInfo = @{
    id = newId; type = "section"; label = "2. INFORMACIÓN SOBRE EL PROCEDIMIENTO"
    children = @(
        (P "Marque con X el procedimiento a realizar"),
        (SH "PROCEDIMIENTOS"),
        (Tabla "Procedimientos" "procedimientos" $colsProc $rowsProc $true),

        (SH "BENEFICIOS"),
        (Tabla "Beneficios" "beneficios" $colsBenef $rowsBenef $true),

        (P "Marque con X los riesgos a los cuales se expone de acuerdo al procedimiento:"),
        (SH "RIESGOS"),
        (Tabla "Riesgos" "riesgos" $colsRiesgo $rowsRiesgos $true),
        (Field "Otro (especifique)" "riesgo_otro" "text" 12),

        (SH "OTRAS ALTERNATIVAS DISPONIBLES"),
        (Tabla "Otras alternativas disponibles" "alternativas" $colsAltern $rowsAltern $true),

        (SH "RIESGOS DE NO REALIZAR EL PROCEDIMIENTO"),
        (Tabla "Riesgos de no realizar el procedimiento" "riesgos_no_realizar" $colsRiesgoNR $rowsRiesgosNR $true)
    )
}

# ================ Seccion 3: DECLARACION DEL PACIENTE ================
$secDeclPaciente = @{
    id = newId; type = "section"; label = "3. DECLARACIÓN DEL PACIENTE"
    children = @(
        (P $decl_paciente_1),
        (P $decl_paciente_2),
        (Field "Firma del Paciente" "firma_declaracion_paciente" "text" 8),
        (Field "CC" "cc_declaracion_paciente" "text" 4)
    )
}

# ================ Seccion 4: DECLARACION DEL RESPONSABLE ================
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

# ================ Seccion 5: DECLARACION DEL PROFESIONAL TRATANTE ================
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
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='PP-FO-112' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_pp112_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK PP-FO-112 actualizado." -ForegroundColor Green
