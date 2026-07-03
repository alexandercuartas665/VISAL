# Rework-PPFO23-Desistimiento.ps1
# Reconstruye PP-FO-23 FORMATO DE DESISTIMIENTO DE SERVICIOS VISAL RT.
# Formato simple: 2 tablas seed (servicios, motivos) + declaraciones + observaciones.
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
$colsSimple = @( (Col "Item" "item" "text"), (Col "Marca" "marca" "text" @{ defaultValue = "" }) )

# ================ SeedRows ================
$rowsServicios = @(
    @("Medicina General", ""),
    @("Medicina laboral", ""),
    @("Nutrición y Dietética", ""),
    @("Psicología", ""),
    @("Terapia Ocupacional", ""),
    @("Terapia Física", ""),
    @("Terapia del Lenguaje", ""),
    @("Enfermería Domiciliaria", ""),
    @("Curaciones", "")
)

$rowsMotivos = @(
    @("Considera que ya no necesita el servicio.", ""),
    @("Cambió de institución prestadora de salud.", ""),
    @("No está conforme con la atención recibida.", ""),
    @("Dificultades en la programación o acceso al servicio.", "")
)

# ================ Cargar y localizar auto sections ================
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='PP-FO-23' AND tenant_id='$TenantId';"
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

# ================ Declaraciones (docx literal) ================
$declPaciente_p1 = "manifiesto de manera libre y voluntaria mi decisión de desistir del servicio seleccionado. He sido informado sobre los posibles riesgos de no continuar con el servicio, así como las alternativas disponibles para su atención."
$declPaciente_p2 = "Entiendo que esta decisión es voluntaria y exime a la IPS VISAL RT de cualquier responsabilidad derivada de la suspensión del servicio solicitado."
$declProfesional = "doy fe de que el paciente ha sido informado sobre los riesgos de su decisión y que el desistimiento ha sido realizado de manera voluntaria."

# ================ Seccion Encabezado (Ciudad/Fecha/Hora/Telefono) ================
$secEncabezado = @{
    id = newId; type = "section"; label = "Encabezado"
    children = @(
        (Field "CIUDAD" "ciudad" "text" 4),
        (Field "FECHA"  "fecha_desistimiento" "date" 4),
        (Field "HORA"   "hora_desistimiento" "text" 4),
        (Field "Teléfono del paciente" "telefono_paciente" "text" 6)
    )
}

# ================ Seccion 2 - Servicio ================
$secServicio = @{
    id = newId; type = "section"; label = "2. SERVICIO DEL QUE DESEA DESISTIR"
    children = @(
        (P "Marque con una X el servicio del cual el paciente decide desistir:"),
        (Tabla "Servicios" "servicios" $colsSimple $rowsServicios $true),
        (Field "Otro (especifique)" "servicio_otro" "text" 12)
    )
}

# ================ Seccion 3 - Motivo ================
$secMotivo = @{
    id = newId; type = "section"; label = "3. MOTIVO DEL DESISTIMIENTO"
    children = @(
        (P "(Seleccione la razón por la cual desiste del servicio)"),
        (Tabla "Motivos" "motivos" $colsSimple $rowsMotivos $true),
        (Field "Otra razón" "otra_razon" "textarea" 12 @{ enableVoice = $true; rows = 3 })
    )
}

# ================ Seccion 4 - Declaracion Paciente ================
$secDeclPaciente = @{
    id = newId; type = "section"; label = "4. DECLARACIÓN DEL PACIENTE O RESPONSABLE"
    children = @(
        (Field "Yo (nombre)" "nombre_declarante" "text" 8),
        (Field "CC/NIT"      "cc_declarante"     "text" 4),
        (P $declPaciente_p1),
        (P $declPaciente_p2),
        (Field "Firma del Paciente o Responsable" "firma_paciente_declaracion" "text" 8),
        (Field "CC"    "cc_paciente_declaracion" "text" 4),
        (Field "Fecha" "fecha_paciente_declaracion" "date" 4)
    )
}

# ================ Seccion 5 - Declaracion Profesional ================
$secDeclProfesional = @{
    id = newId; type = "section"; label = "5. DECLARACIÓN DEL PROFESIONAL QUE RECIBE LA SOLICITUD"
    children = @(
        (Field "Yo (nombre)" "nombre_profesional_declaracion" "text" 8),
        (Field "CC"          "cc_profesional_declaracion"     "text" 4),
        (Field "En calidad de (cargo)" "cargo_profesional" "text" 12),
        (P $declProfesional),
        (Field "Firma del Profesional" "firma_profesional_declaracion" "text" 8),
        (Field "Fecha" "fecha_profesional_declaracion" "date" 4)
    )
}

# ================ Seccion 6 - Observaciones ================
$secObservaciones = @{
    id = newId; type = "section"; label = "6. OBSERVACIONES ADICIONALES (Si aplica)"
    children = @(
        (Field "Observaciones" "observaciones_adicionales" "textarea" 12 @{ enableVoice = $true; rows = 6 })
    )
}

# ================ Ensamblar ================
$schema.children = @(
    $datosPaciente,
    $secEncabezado,
    $secServicio,
    $secMotivo,
    $secDeclPaciente,
    $secDeclProfesional,
    $secObservaciones,
    $firmasAuto,
    $cierre
)
Write-Host ("Top-level ahora: {0} secciones" -f $schema.children.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='PP-FO-23' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_pp23_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK PP-FO-23 actualizado." -ForegroundColor Green
