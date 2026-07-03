# Rework-PPFO37PAD-DesescalonamientoPAD.ps1
# Reconstruye PP-FO-37-PAD CONSENTIMIENTO DE DESESCALONAMIENTO PAD (solo desescalonamiento, no egreso).
# Formato de declaracion (sin tablas seed).
#
# Preserva: Datos del Paciente (auto), Firmas (auto), Cierre.
# NO TOCA HC-FO-08. NO TOCA PP-FO-37 (el de egreso).

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
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='PP-FO-37-PAD' AND tenant_id='$TenantId';"
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

# ================ Textos definiciones/declaraciones (docx literal) ================
$def_pad = "PROGRAMA DE ATENCIÓN DOMICILIARIA (PAD): (modalidad de atención médica orientada a brindar servicios especializados de carácter hospitalario, a los pacientes en su hogar, alternativa hospitalaria que está dirigida a adultos mayores de edad, enfermos crónicos, pacientes en postoperatorio o personas con discapacidad según lo requieran)."
$def_deses = "DESESCALONAMIENTO: (descenso o disminución graduales en la extensión, intensidad o magnitud de una situación crítica, o de las medidas para combatirla)."
$def_general = "Desescalonamiento del PAD, se informa de manera clara y concisa a paciente y/o familiares que usuario(a) requiere continuar manejo médico y plan terapéutico según corresponda de manera presencial y/o ambulatoria en su EPS, se dan recomendaciones médicas y signos de alarma, se orienta en las rutas de servicios de salud de su Microred."
$decl_final = "de forma libre, en pleno uso de mis facultades mentales y sin limitaciones de carácter médico o legal, declaro que acepto y entiendo lo anteriormente expuesto y las recomendaciones realizadas por el personal de salud, para constancia firman los que en este documento intervienen."

# ================ Encabezado ================
$secEncabezado = @{
    id = newId; type = "section"; label = "Encabezado"
    children = @(
        (Field "CIUDAD" "ciudad_encabezado_desespad" "text" 4),
        (Field "FECHA"  "fecha_encabezado_desespad"  "date" 4),
        (Field "HORA"   "hora_encabezado_desespad"   "text" 4)
    )
}

# ================ Definiciones y contexto ================
$secDefiniciones = @{
    id = newId; type = "section"; label = "Definiciones y Contexto"
    children = @(
        (P $def_pad),
        (P $def_deses),
        (P $def_general)
    )
}

# ================ Declaracion del Paciente / Familiar ================
$secDeclaracion = @{
    id = newId; type = "section"; label = "Declaración del Paciente / Familiar"
    children = @(
        (Field "Yo (nombre)"        "nombre_declarante_desespad" "text" 8),
        (Field "Tipo de documento"  "tipo_doc_declarante_desespad" "select" 2 @{ options = @("CC","CE","TI","PP","Otro"); defaultValue = "CC"; allowCustom = $true }),
        (Field "Número"             "num_doc_declarante_desespad"  "text" 2),
        (Field "Expedido en la ciudad de" "ciudad_expedicion_desespad" "text" 6),
        (Field "En condición de"    "condicion_declarante_desespad" "select" 6 @{ options = @("Paciente","Familiar","Otro"); defaultValue = "Paciente"; allowCustom = $true }),
        (Field "Otro (especifique)" "condicion_otro_desespad"       "text" 12),
        (P $decl_final)
    )
}

# ================ Firmas Manuscritas ================
$secFirmasManu = @{
    id = newId; type = "section"; label = "Firmas Manuscritas"
    children = @(
        (Field "Firma del Paciente y/o Cuidador Primario" "firma_paciente_cuidador_desespad" "text" 8),
        (Field "CC del Paciente/Cuidador"                 "cc_paciente_cuidador_desespad"    "text" 4),
        (P "Profesional que realiza desescalonamiento"),
        (Field "Nombre del Profesional" "nombre_profesional_desespad" "text" 6),
        (Field "Firma del Profesional"  "firma_profesional_desespad"  "text" 6)
    )
}

# ================ Ensamblar ================
$schema.children = @(
    $datosPaciente,
    $secEncabezado,
    $secDefiniciones,
    $secDeclaracion,
    $secFirmasManu,
    $firmasAuto,
    $cierre
)
Write-Host ("Top-level ahora: {0} secciones" -f $schema.children.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='PP-FO-37-PAD' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_pp37pad_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK PP-FO-37-PAD actualizado." -ForegroundColor Green
