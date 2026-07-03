# Rework-PPFO24-Covid.ps1
# Reconstruye PP-FO-24 CONSENTIMIENTO INFORMADO CORONAVIRUS COVID 19.
# Formato de declaracion del acompanante (sin tablas seed).
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
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='PP-FO-24' AND tenant_id='$TenantId';"
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

# ================ Declaraciones textuales (docx literal) ================
$decl_1 = "Que de manera detallada se me ha suministrado información completa, suficiente, con un lenguaje sencillo y claro. El profesional de la salud me ha explicado la naturaleza de la enfermedad, acerca del significado de caso sospechoso o confirmado del coronavirus COVID-19 en cuanto a su presentación clínica, modo de contagio, medidas para contenerla, posibilidad de sufrir la enfermedad, complicaciones o muerte, mientras permanezca como acompañante del paciente."
$decl_2 = "Que he podido hacer las preguntas relacionadas con dicha enfermedad y se me han respondido en forma satisfactoria; así mismo se me ha explicado que voy a estar en riesgo de contagiarme mientras permanezca junto a él."
$decl_3 = "Que tras haberse cumplido lo anterior, doy mi consentimiento para permanecer como acompañante mientras dure el proceso de la enfermedad de mi acompañado en la VISAL RT S.A.S. atendiendo el estricto cumplimiento de las normas de la entidad."
$decl_4 = "Certifico que el contenido de este consentimiento me ha sido explicado en su totalidad, que lo he leído o me lo han leído y que entiendo perfectamente su contenido."

# ================ Seccion Encabezado (del cuadro del docx) ================
$secEncabezado = @{
    id = newId; type = "section"; label = "Encabezado"
    children = @(
        (Field "Fecha (Día)" "fecha_dia" "text" 2),
        (Field "Fecha (Mes)" "fecha_mes" "text" 2),
        (Field "Fecha (Año)" "fecha_anio" "text" 2),
        (Field "Ciudad"       "ciudad_encabezado" "text" 6),
        (Field "HC No."       "hc_no" "text" 6),
        (Field "Tipo de identificación" "tipo_identificacion_encabezado" "select" 3 @{ options = @("CC","CE","TI","Otro"); defaultValue = "CC"; allowCustom = $true }),
        (Field "Nº de identificación" "numero_identificacion_encabezado" "text" 3)
    )
}

# ================ Seccion Datos del Acompañante ================
$secAcomp = @{
    id = newId; type = "section"; label = "Datos del Acompañante"
    children = @(
        (P "Yo, ______, con identificación __ Nº _____ de _____, actuando en calidad de acompañante del paciente _____, por medio del presente documento manifiesto:"),
        (Field "Nombre del acompañante" "nombre_acompanante" "text" 8),
        (Field "Tipo de identificación" "tipo_id_acompanante" "select" 2 @{ options = @("CC","CE"); defaultValue = "CC"; allowCustom = $true }),
        (Field "Nº de identificación" "numero_id_acompanante" "text" 3),
        (Field "de (ciudad de expedición)" "ciudad_expedicion_acompanante" "text" 4),
        (Field "Nombre del paciente al que acompaña" "nombre_paciente_acompanado" "text" 8)
    )
}

# ================ Seccion Declaración ================
$secDeclaracion = @{
    id = newId; type = "section"; label = "Declaración del Acompañante"
    children = @(
        (P $decl_1),
        (P $decl_2),
        (P $decl_3),
        (P $decl_4)
    )
}

# ================ Seccion Firmas Manuscritas (del docx) ================
$secFirmasManuscritas = @{
    id = newId; type = "section"; label = "Firmas Manuscritas"
    children = @(
        (Field "Firma del acompañante" "firma_acompanante" "text" 6),
        (Field "CC del acompañante"    "cc_firma_acompanante" "text" 3),
        (Field "de (ciudad de expedición)" "ciudad_firma_acompanante" "text" 3),
        (Field "Nombre personal de la institución de Salud" "nombre_personal_institucion" "text" 12),
        (Field "Firma de personal de institución de Salud" "firma_personal_institucion" "text" 8),
        (Field "C.C personal de institución de Salud" "cc_personal_institucion" "text" 4),
        (Field "Cargo" "cargo_personal_institucion" "text" 6)
    )
}

# ================ Ensamblar ================
$schema.children = @(
    $datosPaciente,
    $secEncabezado,
    $secAcomp,
    $secDeclaracion,
    $secFirmasManuscritas,
    $firmasAuto,
    $cierre
)
Write-Host ("Top-level ahora: {0} secciones" -f $schema.children.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='PP-FO-24' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_pp24_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK PP-FO-24 actualizado." -ForegroundColor Green
