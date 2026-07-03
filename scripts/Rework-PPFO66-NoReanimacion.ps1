# Rework-PPFO66-NoReanimacion.ps1
# Reconstruye PP-FO-66 FORMATO NO REANIMACION.
# 1 tabla seed (procedimientos que el paciente NO autoriza) con columna Marca.
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
$colsSimple = @( (Col "Procedimiento a NO autorizar" "procedimiento" "text"), (Col "Marca" "marca" "text" @{ defaultValue = "" }) )

# ================ SeedRows ================
$rowsProc = @(
    @("Reanimación Cardio-cerebro-pulmonar", ""),
    @("Traslado a instituciones de mayor nivel o UCI", ""),
    @("Procedimientos Invasivos", "")
)

# ================ Cargar y localizar auto sections ================
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='PP-FO-66' AND tenant_id='$TenantId';"
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
$intro_decl = "Declaro por medio del presente documento y en pleno uso de mis facultades mentales de manera libre y autónoma, no autorizar la realización del (los) procedimiento (s):"
$decl_riesgos = "Comprendo y acepto los riesgos que esto pueda ocasionar, incluso fallecimiento, he recibido plena consejería al igual que se me han dado oportunidad de formular preguntas y que todas las preguntas que he formulado han sido explicadas en forma satisfactoria"
$decl_lectura = "Al firmar este documento reconozco que lo he leído o que se me ha leído y explicado y que comprendo perfectamente su contenido"
$decl_reafirma = "Declaro por medio del presente documento y en pleno uso de mis facultades mentales de manera libre y autónoma, no autorizar la realización del (los) procedimiento(s), anteriormente mencionados"
$decl_responsabilidad = "Afirmo que bajo mi responsabilidad decido no autorizar la realización de este procedimiento y en consecuencia declaro que ni la institución, ni su personal serán responsables en caso de complicaciones."
$nota = "NOTA: Cuando se trate de un menor de edad o el paciente no esté en capacidad de otorgar el disentimiento, será la persona que lo representa, la encargada de firmar el presente documento"

# ================ Encabezado ================
$secEncabezado = @{
    id = newId; type = "section"; label = "Encabezado"
    children = @(
        (Field "Fecha" "fecha_no_reanimacion" "date" 4),
        (Field "Hora"  "hora_no_reanimacion"  "text" 4)
    )
}

# ================ Datos del Declarante ================
$secDeclarante = @{
    id = newId; type = "section"; label = "Datos del Declarante"
    children = @(
        (Field "Yo (nombre)"                     "nombre_declarante_nr" "text" 8),
        (Field "Tipo de documento del declarante" "tipo_doc_declarante_nr" "select" 2 @{ options = @("CC","CE","TI","PP","Otro"); defaultValue = "CC"; allowCustom = $true }),
        (Field "No. del declarante"              "num_doc_declarante_nr" "text" 2),
        (P "como paciente o como acudiente (responsable) del paciente llamado:"),
        (Field "Nombre del paciente representado" "nombre_paciente_repre" "text" 8),
        (Field "Tipo de documento del paciente"   "tipo_doc_paciente_repre" "select" 2 @{ options = @("CC","CE","TI","RC","Otro"); defaultValue = "CC"; allowCustom = $true }),
        (Field "No. del paciente"                 "num_doc_paciente_repre"  "text" 2)
    )
}

# ================ Declaración y procedimientos NO autorizados ================
$secDeclaracion = @{
    id = newId; type = "section"; label = "Declaración de No Reanimación"
    children = @(
        (P $intro_decl),
        (Tabla "Procedimientos que NO autoriza" "procedimientos_no_autorizados" $colsSimple $rowsProc $true),
        (P $decl_riesgos),
        (P $decl_lectura),
        (P $decl_reafirma),
        (P $decl_responsabilidad),
        (P $nota)
    )
}

# ================ Firmas Manuscritas ================
$secFirmasManu = @{
    id = newId; type = "section"; label = "Firmas Manuscritas"
    children = @(
        (Field "Firma del paciente o responsable" "firma_paciente_responsable_nr" "text" 8),
        (Field "ID"                                "id_paciente_responsable_nr"    "text" 4),
        (Field "Firma del profesional de salud"    "firma_profesional_nr"          "text" 8),
        (Field "REGISTRO MEDICO"                   "registro_medico_nr"            "text" 4)
    )
}

# ================ Ensamblar ================
$schema.children = @(
    $datosPaciente,
    $secEncabezado,
    $secDeclarante,
    $secDeclaracion,
    $secFirmasManu,
    $firmasAuto,
    $cierre
)
Write-Host ("Top-level ahora: {0} secciones" -f $schema.children.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='PP-FO-66' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_pp66_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK PP-FO-66 actualizado." -ForegroundColor Green
