# Add-PPFO113-Consentimiento.ps1
# Inserta el consentimiento PP-FO-113 FORMATO CONSENTIMIENTO INFORMADO
# PARA CONSULTA DE PRIMERA VEZ replicando la estructura del template
# PP-FO-112 (secciones + textos + campos). Contenido tomado del docx
# oficial del cliente. NO TOCA HC-FO-08 ni ningun otro form.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$Codigo      = "PP-FO-113",
    [string]$Nombre      = "PP-FO-113 FORMATO CONSENTIMIENTO INFORMADO PARA CONSULTA DE PRIMERA VEZ",
    [string]$Tipo        = "CONSENTIMIENTO",
    [string]$Version     = "1.0",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

# 0) Guard: si ya existe, no insertamos por segunda vez.
$existe = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT count(*) FROM form_definitions WHERE codigo='$Codigo' AND tenant_id='$TenantId';"
if ([int]$existe -gt 0) { throw "$Codigo ya existe en el tenant. Elimina el registro antes de re-crearlo o usa un codigo distinto." }

# 1) Armar schema (mismo patron que PP-FO-112)
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

               @{ id = newId; type = "field"; fieldType = "text"; label = "PROCEDIMIENTO"; name = "procedimiento"; widthColumns = 12 },

               @{ id = newId; type = "field"; fieldType = "text";     label = "CONSULTA DE PRIMERA VEZ";        name = "consulta_primera_vez";     widthColumns = 12 },
               @{ id = newId; type = "field"; fieldType = "textarea"; label = "Descripción consulta primera vez"; name = "desc_consulta_primera_vez"; widthColumns = 12;
                  defaultValue = "Evaluación inicial con el especialista en cirugía plástica, estética y reconstructiva, incluyendo historia clínica, examen físico y determinación del plan de manejo." },

               @{ id = newId; type = "field"; fieldType = "text";     label = "CONSULTA ESTÉTICA"; name = "consulta_estetica"; widthColumns = 12 },
               @{ id = newId; type = "field"; fieldType = "textarea"; label = "Descripción consulta estética"; name = "desc_consulta_estetica"; widthColumns = 12;
                  defaultValue = "Evaluación para procedimientos con fines estéticos, incluyendo armonización facial, contorno corporal y rejuvenecimiento." },

               @{ id = newId; type = "field"; fieldType = "text";     label = "CONSULTA RECONSTRUCTIVA"; name = "consulta_reconstructiva"; widthColumns = 12 },
               @{ id = newId; type = "field"; fieldType = "textarea"; label = "Descripción consulta reconstructiva"; name = "desc_consulta_reconstructiva"; widthColumns = 12;
                  defaultValue = "Evaluación de defectos congénitos o adquiridos que requieran reconstrucción funcional o estética." },

               @{ id = newId; type = "field"; fieldType = "text";     label = "CONSULTA POSTQUIRÚRGICA"; name = "consulta_postquirurgica"; widthColumns = 12 },
               @{ id = newId; type = "field"; fieldType = "textarea"; label = "Descripción consulta postquirúrgica"; name = "desc_consulta_postquirurgica"; widthColumns = 12;
                  defaultValue = "Valoración y seguimiento después de cirugía plástica reconstructiva o estética." },

               @{ id = newId; type = "field"; fieldType = "text";     label = "MANEJO DE SECUELAS POSTRAUMÁTICAS"; name = "manejo_secuelas_postraumaticas"; widthColumns = 12 },
               @{ id = newId; type = "field"; fieldType = "textarea"; label = "Descripción manejo de secuelas"; name = "desc_manejo_secuelas"; widthColumns = 12;
                  defaultValue = "Evaluación y planificación del tratamiento para lesiones, quemaduras o cicatrices." },

               @{ id = newId; type = "text"; textStyle = "subheading"; content = "BENEFICIOS" },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Valoración integral por especialista en cirugía plástica, estética y reconstructiva." },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Identificación de alteraciones estéticas, funcionales, congénitas, traumáticas o adquiridas susceptibles de manejo especializado." },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Evaluación clínica mediante historia médica y examen físico para establecer un diagnóstico y plan de manejo." },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Orientación sobre las opciones terapéuticas, reconstructivas o estéticas disponibles según la condición del paciente." },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Definición de conductas diagnósticas, terapéuticas o quirúrgicas según criterio médico especializado." },

               @{ id = newId; type = "text"; textStyle = "subheading"; content = "Marque con X los riesgos a los cuales se expone de acuerdo al procedimiento:" },
               @{ id = newId; type = "text"; textStyle = "subheading"; content = "RIESGOS" },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Posibilidad de error diagnóstico debido a hallazgos clínicos limitados o evolución de la condición clínica." },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Posibilidad de error en la formulación o planificación del manejo terapéutico." },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Necesidad de exámenes complementarios o valoraciones adicionales para confirmar el diagnóstico." },
               @{ id = newId; type = "field"; fieldType = "text"; label = "Otro: _________________________________________"; name = "otro"; widthColumns = 12 },

               @{ id = newId; type = "text"; textStyle = "subheading"; content = "OTRAS ALTERNATIVAS DISPONIBLES" },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Alta voluntaria conforme a decisión del paciente y/o responsable." },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Reprogramación de la consulta especializada." },

               @{ id = newId; type = "text"; textStyle = "subheading"; content = "RIESGOS DE NO REALIZAR EL PROCEDIMIENTO" },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Retraso en la identificación de alteraciones que requieran manejo especializado." },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Demora en el inicio de tratamientos médicos, quirúrgicos o reconstructivos oportunos." },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Pérdida de oportunidades terapéuticas para mejorar la condición clínica." }
           ) },

        @{ id = newId; type = "section"; label = "DECLARACIÓN DEL PACIENTE"
           children = @(
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Me han explicado y he comprendido satisfactoriamente la esencia y el propósito de este procedimiento, también me han aclarado todas las dudas y me han dicho los posibles riesgos y complicaciones, así como las otras alternativas de tratamiento." },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "HUELLAHUELLADoy mi consentimiento para que me realicen el procedimiento descrito anteriormente y los procedimientos complementarios que sean necesarios o convenientes mediante la realización de este, a criterio de los profesionales que lo llevan a cabo." },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Firma del Paciente: _________________________" }
           ) },

        @{ id = newId; type = "section"; label = "CC: ______________________"
           children = @(
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "DECLARACIÓN DEL RESPONSABLE DEL PACIENTE (Solo en caso de Incapacidad del Paciente)" },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Yo ____________________________ sé que el paciente _______________ ___________con No de identificación______________________ ha sido considerado por ahora incapaz de tomar por sí mismo la decisión de aceptar o rechazar el procedimiento. También se me han explicado los riesgos y complicaciones," },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "HUELLAHUELLAasí como las otras alternativas de tratamiento. Soy consciente que no existen garantías absolutas de los resultados del procedimiento. He comprendido todo lo anterior perfectamente y por ello doy mi consentimiento para que los profesionales tratantes y el personal auxiliar que precise, le realicen este procedimiento. Puedo revocar este consentimiento cuando en bien del paciente se considere oportuno." },
               @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Firma del Paciente: _________________________" }
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

# 2) INSERT
$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$sql = @"
INSERT INTO form_definitions
(id, codigo, nombre, version, tipo, schema_json, activo, created_at, updated_at, tenant_id)
VALUES (gen_random_uuid(), '$Codigo', '$Nombre', '$Version', '$Tipo', '$jsonSql'::jsonb, true, now(), now(), '$TenantId');
"@

$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_pp113_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host ""
Write-Host "OK $Codigo insertado. Schema: $($json.Length) bytes" -ForegroundColor Green
Write-Host "    Secciones: $($schema.children.Count)"
