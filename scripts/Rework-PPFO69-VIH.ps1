# Rework-PPFO69-VIH.ps1
# Reconstruye PP-FO-69 CONSENTIMIENTO PRUEBA DIAGNOSTICA DE VIH.
# Formato de declaracion + constancia + revocatoria (sin tablas seed).
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
function SH([string]$content) { @{ id=newId; type="text"; textStyle="subheading"; content=$content } }
function P([string]$content) { @{ id=newId; type="text"; textStyle="paragraph"; content=$content } }
function Field([string]$label, [string]$name, [string]$ft, [int]$width=12, $extra=@{}) {
    $f = @{ id=newId; type="field"; fieldType=$ft; label=$label; name=$name; widthColumns=$width; allowCustom=$false; required=$false }
    foreach ($k in $extra.Keys) { $f[$k] = $extra[$k] }
    return $f
}

# ================ Cargar y localizar auto sections ================
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='PP-FO-69' AND tenant_id='$TenantId';"
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
    Write-Host "Seccion Datos del Paciente (auto-llenado) CREADA" -ForegroundColor Yellow
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

# ================ Textos declaraciones (docx literal) ================
$intro_1 = "Como medida preventiva es necesario tomarle exámenes de laboratorio para conocer si usted padece de infecciones que puedan ser transmitidas por contacto con sangre u otros líquidos que se producen en su cuerpo."
$intro_2 = "Los resultados de estos exámenes son de manejo confidencial y solamente será utilizado para fines médicos; los resultados correspondientes le serán informados por su médico tratante."
$intro_3 = "Por la realización de estos exámenes usted no tendrá que asumir costo alguno. Para efectuar estas pruebas de laboratorio se requiere su autorización por escrito por lo cual se le solicita que diligencie la información descrita a continuación y firme autorizando el procedimiento."
$intro_4 = "Las pruebas de laboratorio que le serán practicadas son:"
$intro_5 = "➢ ELISA para detección de VIH (Virus de Inmunodeficiencia Humana)"
$intro_6 = "Para lo anterior, solamente se requiere una muestra de sangre que será tomada por personal de nuestra IPS Visal."

$const_1 = "Autorizo para realizarme las pruebas de ELISA para VIH según el plan de seguimiento explicado a través del laboratorio que se designe para ello y hago constar que conozco el significado de esta prueba."
$const_beneficios = "He sido informado de los siguientes beneficios que representa para mi condición médica la realización de este procedimiento:"
$const_riesgos = "Igualmente, he sido informado de los siguientes riesgos para mi condición médica por la realización de este procedimiento:"

$revoc_1 = "manifiesto que, en pleno uso de mis facultades, y por mi propia voluntad, he decidido revocar el consentimiento que había otorgado previamente para la realización de prueba diagnóstica VIH."
$revoc_2 = "Y que he sido suficientemente informado sobre los riesgos y las posibles consecuencias de este cambio en mi decisión"

$nota_parentesco_1 = "Parentesco si firma una persona que no sea el paciente (como en el caso del representante legal de menores de edad o incapaces)"
$nota_parentesco_2 = "* Parentesco si firma una persona que no sea el paciente (como en el caso del representante legal de menores de edad o incapaces)"

# ================ Seccion Introducción ================
$secIntro = @{
    id = newId; type = "section"; label = "Introducción"
    children = @(
        (P "Respetado Señor (a)"),
        (P $intro_1),
        (P $intro_2),
        (P $intro_3),
        (P $intro_4),
        (P $intro_5),
        (P $intro_6)
    )
}

# ================ Seccion Constancia de Aceptación ================
$secConstancia = @{
    id = newId; type = "section"; label = "CONSTANCIA DE ACEPTACIÓN PARA LA REALIZACIÓN DEL EXAMEN"
    children = @(
        (Field "Yo (nombre)" "nombre_declarante_vih" "text" 8),
        (Field "C.C"         "cc_declarante_vih"     "text" 4),
        (P $const_1),
        (P $const_beneficios),
        (Field "Beneficios" "beneficios_vih" "textarea" 12 @{ enableVoice = $true; rows = 3 }),
        (P $const_riesgos),
        (Field "Riesgos"    "riesgos_vih"    "textarea" 12 @{ enableVoice = $true; rows = 3 })
    )
}

# ================ Seccion Datos de los Firmantes ================
$secDatosFirmantes = @{
    id = newId; type = "section"; label = "Datos de los Firmantes"
    children = @(
        (Field "Firma del paciente o persona responsable" "firma_paciente_vih" "text" 6),
        (Field "Documento de identificación (paciente)"   "doc_id_paciente_vih" "text" 6),
        (Field "Nombre completo y claro (Paciente)"       "nombre_paciente_vih" "text" 12),
        (Field "Nombre completo y claro (Familiar)"       "nombre_familiar_vih" "text" 6),
        (Field "Documento de identificación (Familiar)"   "doc_id_familiar_vih" "text" 6),
        (Field "Parentesco"                                "parentesco_vih"     "text" 6),
        (Field "Dirección de residencia"                   "direccion_vih"      "text" 6),
        (Field "Teléfono de residencia"                    "telefono_vih"       "text" 6),
        (P $nota_parentesco_1),
        (Field "Fecha"                                     "fecha_firma_vih"    "date" 4),
        (Field "Firma del Profesional que da la Sensibilización" "firma_profesional_sensib" "text" 8),
        (Field "Nombre completo (Profesional)"             "nombre_profesional_sensib" "text" 12)
    )
}

# ================ Seccion Revocatoria ================
$secRevocatoria = @{
    id = newId; type = "section"; label = "REVOCATORIA AL CONSENTIMIENTO INFORMADO"
    children = @(
        (Field "Fecha de revocatoria (DD)"   "revoc_dd"   "text" 2),
        (Field "Fecha de revocatoria (MM)"   "revoc_mm"   "text" 2),
        (Field "Fecha de revocatoria (AAAA)" "revoc_aaaa" "text" 3),
        (Field "Yo (nombre)"                  "nombre_revocante"        "text" 8),
        (Field "CC"                           "cc_revocante"            "text" 4),
        (Field "de (ciudad de expedición)"    "ciudad_expedicion_revoc" "text" 6),
        (P $revoc_1),
        (P $revoc_2),
        (Field "Firma del paciente o de la persona responsable" "firma_paciente_revoc" "text" 6),
        (Field "Firma del testigo"                              "firma_testigo_revoc"  "text" 6),
        (Field "Nombre Completo y Claro (Paciente/Responsable)" "nombre_paciente_revoc" "text" 6),
        (Field "Nombre Completo y Claro (Testigo)"              "nombre_testigo_revoc"  "text" 6),
        (Field "Parentesco"                                     "parentesco_revoc"     "text" 6),
        (Field "Documento de Identificación (Paciente)"         "doc_id_paciente_revoc" "text" 6),
        (Field "Documento de Identificación (Testigo)"          "doc_id_testigo_revoc"  "text" 6),
        (P $nota_parentesco_2)
    )
}

# ================ Ensamblar ================
$schema.children = @(
    $datosPaciente,
    $secIntro,
    $secConstancia,
    $secDatosFirmantes,
    $secRevocatoria,
    $firmasAuto,
    $cierre
)
Write-Host ("Top-level ahora: {0} secciones" -f $schema.children.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='PP-FO-69' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_pp69_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK PP-FO-69 actualizado." -ForegroundColor Green
