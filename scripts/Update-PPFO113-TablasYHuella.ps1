# Update-PPFO113-TablasYHuella.ps1
# Rehace el schema de PP-FO-113 usando TABLAS seed con lockRows=true para
# PROCEDIMIENTOS/BENEFICIOS/RIESGOS/ALTERNATIVAS/RIESGOS DE NO REALIZAR
# tal como estan en el docx original, y reemplaza los prefijos
# "HUELLAHUELLA" de los parrafos por un campo dedicado HUELLA (textarea
# vacio widthColumns=3, rows=4) que actua como caja marca.
#
# Es UPDATE del registro existente PP-FO-113 (no se crea uno nuevo).
# NO TOCA HC-FO-08 ni ningun otro form.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$Codigo      = "PP-FO-113",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

# 0) Verificar que exista
$existe = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT count(*) FROM form_definitions WHERE codigo='$Codigo' AND tenant_id='$TenantId';"
if ([int]$existe -eq 0) { throw "$Codigo no existe en el tenant." }

# ---- Tablas ----
$colProcedimiento = @(
    @{ id=newId; label="Procedimiento"; name="procedimiento"; fieldType="text"; allowCustom=$false },
    @{ id=newId; label="Marque X";       name="marca";          fieldType="text"; allowCustom=$false; defaultValue="" },
    @{ id=newId; label="Descripción";    name="descripcion";    fieldType="text"; allowCustom=$false }
)
$rowsProcedimiento = @(
    @("CONSULTA DE PRIMERA VEZ",         "", "Evaluación inicial con el especialista en cirugía plástica, estética y reconstructiva, incluyendo historia clínica, examen físico y determinación del plan de manejo."),
    @("CONSULTA ESTÉTICA",               "", "Evaluación para procedimientos con fines estéticos, incluyendo armonización facial, contorno corporal y rejuvenecimiento."),
    @("CONSULTA RECONSTRUCTIVA",         "", "Evaluación de defectos congénitos o adquiridos que requieran reconstrucción funcional o estética."),
    @("CONSULTA POSTQUIRÚRGICA",         "", "Valoración y seguimiento después de cirugía plástica reconstructiva o estética."),
    @("MANEJO DE SECUELAS POSTRAUMÁTICAS","", "Evaluación y planificación del tratamiento para lesiones, quemaduras o cicatrices.")
)

$colDosCols = @(
    @{ id=newId; label="Descripción"; name="descripcion"; fieldType="text"; allowCustom=$false },
    @{ id=newId; label="Marque X";    name="marca";       fieldType="text"; allowCustom=$false; defaultValue="" }
)
$rowsBeneficios = @(
    @("Valoración integral por especialista en cirugía plástica, estética y reconstructiva.", ""),
    @("Identificación de alteraciones estéticas, funcionales, congénitas, traumáticas o adquiridas susceptibles de manejo especializado.", ""),
    @("Evaluación clínica mediante historia médica y examen físico para establecer un diagnóstico y plan de manejo.", ""),
    @("Orientación sobre las opciones terapéuticas, reconstructivas o estéticas disponibles según la condición del paciente.", ""),
    @("Definición de conductas diagnósticas, terapéuticas o quirúrgicas según criterio médico especializado.", "")
)
$rowsRiesgos = @(
    @("Posibilidad de error diagnóstico debido a hallazgos clínicos limitados o evolución de la condición clínica.", ""),
    @("Posibilidad de error en la formulación o planificación del manejo terapéutico.", ""),
    @("Necesidad de exámenes complementarios o valoraciones adicionales para confirmar el diagnóstico.", "")
)
$rowsAlternativas = @(
    @("Alta voluntaria conforme a decisión del paciente y/o responsable.", ""),
    @("Reprogramación de la consulta especializada.", "")
)
$rowsRiesgosNoRealizar = @(
    @("Retraso en la identificación de alteraciones que requieran manejo especializado.", ""),
    @("Demora en el inicio de tratamientos médicos, quirúrgicos o reconstructivos oportunos.", ""),
    @("Pérdida de oportunidades terapéuticas para mejorar la condición clínica.", "")
)

function Mesa {
    param($lbl, $name, $cols, $rows)
    return @{
        id = newId; type = "field"; fieldType = "table"
        label = $lbl; name = $name; widthColumns = 12
        columns = $cols; seedRows = $rows
        lockRows = $true; allowCustom = $false
        isSection = $false; isText = $false; isTable = $true; required = $false
    }
}
function CajaHuella {
    param($name)
    return @{
        id = newId; type = "field"; fieldType = "textarea"
        label = "HUELLA"; name = $name
        widthColumns = 3; rows = 4
        placeholder = "Espacio para huella"
        allowCustom = $false; required = $false
    }
}

# ---- Schema completo ----
$schema = @{
    id = newId
    label = "Historia clinica"
    isSection = $true
    children = @(
        @{ id = newId; type = "section"; label = "FECHA: ____________________"
           children = @() },

        @{ id = newId; type = "section"; label = "DATOS DE IDENTIFICACIÓN"
           children = @(
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Nombre Completo del paciente: _________________________________" },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "N° Identificación: ____________________________________" },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Edad: _____________" }
           ) },

        @{ id = newId; type = "section"; label = "INFORMACIÓN SOBRE EL PROCEDIMIENTO"
           children = @(
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Marque con X el procedimiento a realizar" },
               (Mesa "Procedimientos disponibles" "tabla_procedimientos" $colProcedimiento $rowsProcedimiento),

               @{ id = newId; type = "text"; textStyle = "subheading"; content = "BENEFICIOS" },
               (Mesa "Beneficios" "tabla_beneficios" $colDosCols $rowsBeneficios),

               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Marque con X los riesgos a los cuales se expone de acuerdo al procedimiento:" },
               @{ id = newId; type = "text"; textStyle = "subheading"; content = "RIESGOS" },
               (Mesa "Riesgos" "tabla_riesgos" $colDosCols $rowsRiesgos),
               @{ id = newId; type = "field"; fieldType = "text"; label = "Otro"; name = "otro_riesgo"; widthColumns = 12; placeholder = "Descripción del otro riesgo" },

               @{ id = newId; type = "text"; textStyle = "subheading"; content = "OTRAS ALTERNATIVAS DISPONIBLES" },
               (Mesa "Otras alternativas disponibles" "tabla_alternativas" $colDosCols $rowsAlternativas),

               @{ id = newId; type = "text"; textStyle = "subheading"; content = "RIESGOS DE NO REALIZAR EL PROCEDIMIENTO" },
               (Mesa "Riesgos de no realizar" "tabla_riesgos_no_realizar" $colDosCols $rowsRiesgosNoRealizar)
           ) },

        @{ id = newId; type = "section"; label = "DECLARACIÓN DEL PACIENTE"
           children = @(
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Me han explicado y he comprendido satisfactoriamente la esencia y el propósito de este procedimiento, también me han aclarado todas las dudas y me han dicho los posibles riesgos y complicaciones, así como las otras alternativas de tratamiento." },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Doy mi consentimiento para que me realicen el procedimiento descrito anteriormente y los procedimientos complementarios que sean necesarios o convenientes mediante la realización de este, a criterio de los profesionales que lo llevan a cabo." },
               (CajaHuella "huella_paciente"),
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Firma del Paciente: _________________________" },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "CC: ______________________" }
           ) },

        @{ id = newId; type = "section"; label = "DECLARACIÓN DEL RESPONSABLE DEL PACIENTE (Solo en caso de Incapacidad del Paciente)"
           children = @(
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Yo ____________________________ sé que el paciente _______________ ___________ con No de identificación______________________ ha sido considerado por ahora incapaz de tomar por sí mismo la decisión de aceptar o rechazar el procedimiento. También se me han explicado los riesgos y complicaciones," },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "así como las otras alternativas de tratamiento. Soy consciente que no existen garantías absolutas de los resultados del procedimiento. He comprendido todo lo anterior perfectamente y por ello doy mi consentimiento para que los profesionales tratantes y el personal auxiliar que precise, le realicen este procedimiento. Puedo revocar este consentimiento cuando en bien del paciente se considere oportuno." },
               (CajaHuella "huella_responsable"),
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Firma del Paciente: _________________________" },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "CC: ______________________" }
           ) },

        @{ id = newId; type = "section"; label = "DECLARACIÓN DEL PROFESIONAL TRATANTE"
           children = @(
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Yo____________________________________ como profesional tratante, he informado al paciente sobre la esencia y el propósito del procedimiento descrito anteriormente, de sus alternativas, posibles riesgos, resultados esperados y que no existen garantías absolutas de los resultados del procedimiento." },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Firma del profesional: ____________________________ CC: __________________" },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "No Registro Profesional: __________________________ Cargo: _________________" }
           ) },

        @{ id = newId; type = "section"; label = "Cierre"
           children = @(
               @{ id = newId; type = "field"; fieldType = "textarea"; label = "Observaciones / Conclusiones"; name = "observaciones_cierre"; widthColumns = 12 }
           ) }
    )
}

# ---- UPDATE ----
$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json = '$jsonSql'::jsonb, updated_at = '$now' WHERE codigo='$Codigo' AND tenant_id='$TenantId';"

$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_pp113upd_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host ""
Write-Host "OK $Codigo actualizado. Schema: $($json.Length) bytes" -ForegroundColor Green
Write-Host "    Secciones: $($schema.children.Count)"
Write-Host "    Tablas seed: 5 (procedimientos, beneficios, riesgos, alternativas, riesgos no realizar)"
Write-Host "    Cajas HUELLA: 2 (paciente, responsable)"
