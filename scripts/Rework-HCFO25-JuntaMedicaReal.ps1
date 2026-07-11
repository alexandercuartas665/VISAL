# Rework-HCFO25-JuntaMedicaReal.ps1
# Rehace HC-FO-25 FORMATO JUNTA MEDICA VISAL RT segun el docx real
# "FORMATO JUNTA MEDICA POSITIVA .docx". Reemplaza el schema anterior
# por completo (backup previo).

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev",
    [string]$BackupRoot  = "C:\Users\acuartas\AppData\Local\Temp\claude\C--DesarrolloIA-Visal\3a114262-030a-4135-852f-4f6e57a10abf\scratchpad"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }
function P([string]$content) { @{ id=newId; type="text"; textStyle="paragraph"; content=$content } }
function SH([string]$content) { @{ id=newId; type="text"; textStyle="subheading"; content=$content } }
function Heading([string]$content) { @{ id=newId; type="text"; textStyle="heading"; content=$content } }
function Field([string]$label, [string]$name, [string]$ft, [int]$width=12, $extra=@{}) {
    $f = @{ id=newId; type="field"; fieldType=$ft; label=$label; name=$name; widthColumns=$width; allowCustom=$false; required=$false }
    foreach ($k in $extra.Keys) { $f[$k] = $extra[$k] }
    return $f
}
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

# ============ Backup ============
$stamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
$bkPath = Join-Path $BackupRoot ("backup_hcfo25_$stamp.json")
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-25' AND tenant_id='$TenantId';"
if ([string]::IsNullOrWhiteSpace($raw)) { throw "HC-FO-25 no existe" }
[System.IO.File]::WriteAllText($bkPath, $raw, [System.Text.UTF8Encoding]::new($false))
Write-Host "Backup: $bkPath" -ForegroundColor DarkGray

$schemaOld = $raw | ConvertFrom-Json -AsHashtable

# Preservar MEDICO tal cual si existe (auto-fill del profesional)
$secMedico = $null
foreach ($sec in $schemaOld.children) {
    if (([string]$sec["label"]) -eq "MEDICO") { $secMedico = $sec; break }
}
if ($null -eq $secMedico) {
    $secMedico = @{
        id = newId; type = "section"; label = "MEDICO"
        children = @(
            (Field "Nombre del médico" "medico_nombre" "text" 6 @{ required=$true }),
            (Field "Documento"         "medico_doc"    "text" 3),
            (Field "Registro"          "medico_reg"    "text" 3),
            (Field "Firma"             "medico_firma"  "text" 12)
        )
    }
}

# ============ Header ============
$header = @{
    campos = @(
        @{ id=newId; label="No Historia" },
        @{ id=newId; label="Consecutivo" },
        @{ id=newId; label="Ciudad y Fecha" }
    )
    titulo = "FORMATO JUNTA MEDICA"
    logoUrl = "/uploads/branding/visal-rt-logo.png"
    tagline = ""
    institucion = ""
}

# ============ 1) DATOS DEL PACIENTE ============
$secDatos = @{
    id = newId; type = "section"; label = "DATOS DEL PACIENTE"
    children = @(
        (Field "Nombres y Apellidos" "nombres_apellidos" "text" 8 @{ required=$true }),
        (Field "Identificación"      "identificacion"    "text" 4 @{ required=$true }),
        (Field "Fecha nacimiento"    "fecha_nacimiento"  "date" 4),
        (Field "Edad (auto)"         "edad"              "calculated" 2 @{ formula = "edad(fecha_nacimiento)" }),
        (Field "Empresa"             "empresa"           "text" 6),
        (Field "Sexo"                "sexo"              "select" 3 @{ options = @("Masculino","Femenino","Otro"); defaultValue="Masculino"; allowCustom=$true }),
        (Field "Estado civil"        "estado_civil"      "select" 3 @{ options = @("Soltero","Casado","Union libre","Separado","Divorciado","Viudo"); allowCustom=$true }),
        (Field "Teléfono o celular"  "telefono"          "text" 6),
        (Field "Dirección"           "direccion"         "text" 6),
        (Field "Ciudad"              "ciudad"            "text" 3),
        (Field "Zona"                "zona"              "text" 3),
        (Field "Ocupación"           "ocupacion"         "text" 6),
        (Field "Estudios"            "estudios"          "text" 6),
        (Field "EPS"                 "eps"               "text" 6),
        (Field "Régimen"             "regimen"           "select" 6 @{ options = @("Contributivo","Subsidiado","Especial","Particular"); allowCustom=$true }),
        (SH "En caso de emergencia informar a:"),
        (Field "Nombre contacto emergencia" "emerg_nombre"    "text" 6),
        (Field "Parentesco"                 "emerg_parentesco" "text" 3),
        (Field "Teléfono contacto"          "emerg_telefono"   "text" 3),
        (Field "Dirección contacto"         "emerg_direccion"  "text" 12)
    )
}

# ============ 2) ANAMNESIS / ENFERMEDAD ACTUAL ============
$secAnamnesis = @{
    id = newId; type = "section"; label = "ANAMNESIS"
    children = @(
        (SH "ENFERMEDAD ACTUAL"),
        (Field "Enfermedad actual" "enfermedad_actual" "textarea" 12 @{ enableVoice=$true; rows=5 })
    )
}

# ============ 3) JUNTA RHI ============
$secJuntaRhi = @{
    id = newId; type = "section"; label = "JUNTA RHI"
    children = @(
        (Field "Siniestro"                            "junta_siniestro"    "text" 6),
        (Field "Fecha AT"                             "junta_fecha_at"     "date" 3),
        (Field "Empresa"                              "junta_empresa"      "text" 3),
        (Field "Diagnósticos aceptados en plataforma" "junta_dx_aceptados" "textarea" 12 @{ enableVoice=$true; rows=3 })
    )
}

# ============ 4) ANTECEDENTES PERSONALES (tabla seed 2 col con defaults) ============
$colsAnt = @(
    (Col "Ítem" "item" "text"),
    (Col "Observación" "observacion" "select" @{
        options = @("NIEGA","NO REFIERE","REFIERE"); allowCustom = $true; defaultValue = "NIEGA"
    })
)
$rowsAnt = @(
    @("Antecedentes Patológicos",   "NIEGA"),
    @("Antecedente Farmacológico",  "NIEGA"),
    @("Antecedentes Quirúrgicos",   "NIEGA"),
    @("Partos",                     "NIEGA"),
    @("Traumático",                 "NIEGA")
)
$secAnt = @{
    id = newId; type = "section"; label = "ANTECEDENTES PERSONALES"
    children = @(
        (Tabla "Antecedentes Personales" "antecedentes_personales" $colsAnt $rowsAnt $true)
    )
}

# ============ 5) EXAMEN FISICO ============
$secExamen = @{
    id = newId; type = "section"; label = "EXAMEN FISICO"
    children = @(
        (Field "Dolor"                              "ef_dolor"          "textarea" 12 @{ enableVoice=$true; rows=2 }),
        (Field "AMAS (Arcos de Movilidad Articular / pasivo)" "ef_amas" "textarea" 12 @{ enableVoice=$true; rows=2 }),
        (Field "Fuerza"                             "ef_fuerza"         "textarea" 12 @{ enableVoice=$true; rows=2 }),
        (Field "Flexibilidad"                       "ef_flexibilidad"   "textarea" 12 @{ enableVoice=$true; rows=2 }),
        (Field "ROT (Reflejos osteotendinosos)"     "ef_rot"            "textarea" 12 @{ enableVoice=$true; rows=2 }),
        (Field "Sensibilidad"                       "ef_sensibilidad"   "textarea" 12 @{ enableVoice=$true; rows=2 }),
        (Field "Pruebas complementarias"            "ef_pruebas"        "textarea" 12 @{ enableVoice=$true; rows=2 })
    )
}

# ============ 6) ESTADO GENERAL ============
$secEstadoGral = @{
    id = newId; type = "section"; label = "ESTADO GENERAL"
    children = @(
        (Field "Estado general del paciente" "estado_general" "textarea" 12 @{ enableVoice=$true; rows=4 })
    )
}

# ============ 7) CONCEPTOS DE JUNTA (3 textareas) ============
$secConceptos = @{
    id = newId; type = "section"; label = "CONCEPTOS DE JUNTA"
    children = @(
        (SH "CONCEPTO FUNCIONAL - FISIOTERAPIA"),
        (Field "Concepto de fisioterapia" "concepto_fisioterapia" "textarea" 12 @{ enableVoice=$true; rows=4 }),
        (SH "CONCEPTO OCUPACIONAL - TERAPIA OCUPACIONAL"),
        (Field "Concepto de terapia ocupacional" "concepto_to" "textarea" 12 @{ enableVoice=$true; rows=4 }),
        (SH "CONCEPTO PSICOLOGIA"),
        (Field "Concepto de psicología" "concepto_psicologia" "textarea" 12 @{ enableVoice=$true; rows=4 })
    )
}

# ============ 8) PRONOSTICO FUNCIONAL Y OCUPACIONAL ============
$secPronostico = @{
    id = newId; type = "section"; label = "PRONOSTICO FUNCIONAL Y OCUPACIONAL"
    children = @(
        (Field "Pronóstico funcional"    "pronostico_funcional"   "textarea" 12 @{ enableVoice=$true; rows=3 }),
        (Field "Pronóstico ocupacional"  "pronostico_ocupacional" "textarea" 12 @{ enableVoice=$true; rows=3 })
    )
}

# ============ 9) FIRMAS DE JUNTA (4 profesionales editables) ============
$secFirmasJunta = @{
    id = newId; type = "section"; label = "EN CONSTANCIA DE LO ANTERIOR FIRMAN"
    children = @(
        (SH "Fisiatra"),
        (Field "Nombre Fisiatra"           "firma_fisiatra_nombre" "text" 6 @{ defaultValue="ALEXANDER BENAVIDES" }),
        (Field "Firma Fisiatra (URL)"      "firma_fisiatra_url"    "text" 6),
        (SH "Fisioterapeuta"),
        (Field "Nombre Fisioterapeuta"      "firma_fisio_nombre"   "text" 6 @{ defaultValue="JUAN CAMILO PANTOJA" }),
        (Field "Firma Fisioterapeuta (URL)" "firma_fisio_url"      "text" 6),
        (SH "Terapeuta Ocupacional"),
        (Field "Nombre Terapeuta Ocupacional" "firma_to_nombre" "text" 6 @{ defaultValue="ANDRES BRAVO" }),
        (Field "Cargo / Registro"             "firma_to_cargo"  "text" 6 @{ defaultValue="Terapeuta Ocupacional - Especialista en Seguridad y Salud en el Trabajo - Lic. 14" }),
        (Field "Firma T.O. (URL)"             "firma_to_url"    "text" 12),
        (SH "Psicóloga"),
        (Field "Nombre Psicóloga"     "firma_psico_nombre" "text" 6 @{ defaultValue="EVELIN TREJOS" }),
        (Field "Firma Psicóloga (URL)" "firma_psico_url"    "text" 6)
    )
}

# ============ 10) CIERRE ============
$secCierre = @{
    id = newId; type = "section"; label = "Cierre"
    children = @( (Field "Observaciones / Conclusiones" "observaciones_cierre" "textarea" 12 @{ enableVoice = $true }) )
}

# ============ Ensamblar schema ============
$schema = @{
    header = $header
    children = @(
        $secDatos,
        $secAnamnesis,
        $secJuntaRhi,
        $secAnt,
        $secExamen,
        $secEstadoGral,
        $secConceptos,
        $secPronostico,
        $secFirmasJunta,
        $secCierre,
        $secMedico
    )
}

Write-Host ("Secciones nuevas: {0}" -f $schema.children.Count) -ForegroundColor Cyan

# ============ Persistir ============
$json = ($schema | ConvertTo-Json -Depth 40 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='HC-FO-25' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_hcfo25_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK HC-FO-25 actualizado." -ForegroundColor Green
