# Add-PPFO51-Braden.ps1
# Carga PP-FO-51 ESCALA DE BRADEN (tipo=ESCALAS) siguiendo el patron de
# PP-FO-59 Norton (select por dominio + suma + cases). 6 subescalas.
# Header con logo Visal RT.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }
function P([string]$content) { @{ id=newId; type="text"; textStyle="paragraph"; content=$content } }
function SH([string]$content) { @{ id=newId; type="text"; textStyle="subheading"; content=$content } }
function Field([string]$label, [string]$name, [string]$ft, [int]$width=12, $extra=@{}) {
    $f = @{ id=newId; type="field"; fieldType=$ft; label=$label; name=$name; widthColumns=$width; allowCustom=$false; required=$false }
    foreach ($k in $extra.Keys) { $f[$k] = $extra[$k] }
    return $f
}
function SelDominio([string]$label, [string]$name, $options) {
    return @{
        id=newId; type="field"; fieldType="select"; catalog="estatico"
        label=$label; name=$name; widthColumns=12; required=$true
        options=$options; allowCustom=$false
    }
}

# ========== Header con logo ==========
$header = @{
    campos = @(
        @{ id=newId; label="No Historia" },
        @{ id=newId; label="Consecutivo" },
        @{ id=newId; label="Ciudad y Fecha" }
    )
    titulo = "ESCALA DE BRADEN"
    logoUrl = "/uploads/branding/visal-rt-logo.png"
    tagline = ""
    institucion = ""
}

# ========== DATOS DEL PACIENTE ==========
$secDatos = @{
    id = newId; type = "section"; label = "DATOS DEL PACIENTE"
    children = @(
        (Field "Nombre del paciente" "nombre_paciente"   "text" 8 @{ required=$true }),
        (Field "Tipo y N° Identificación" "identificacion" "text" 4 @{ required=$true }),
        (Field "Fecha de nacimiento" "fecha_nacimiento" "date" 4),
        (Field "Edad (auto)" "edad" "calculated" 2 @{ formula = "edad(fecha_nacimiento)" }),
        (Field "Fecha de atención" "fecha_atencion" "date" 3 @{ required=$true }),
        (Field "Hora de atención"  "hora_atencion"  "text" 3)
    )
}

# ========== INDICE DE BRADEN ==========
$secBraden = @{
    id = newId; type = "section"; label = "INDICE DE BRADEN"
    children = @(
        (P "Seleccione la opcion que mejor describe el estado actual del paciente. El puntaje total y el nivel de riesgo se calculan automaticamente. ATENCION: a MENOR puntaje, MAYOR riesgo de ulcera por presion (UPP)."),
        (SelDominio "1. Percepción sensorial (capacidad para responder al malestar)" "braden_percepcion" @(
            "1 - Completamente limitada",
            "2 - Muy limitada",
            "3 - Levemente limitada",
            "4 - Sin limitaciones"
        )),
        (SelDominio "2. Humedad (exposición de la piel a la humedad)" "braden_humedad" @(
            "1 - Constantemente húmeda",
            "2 - A menudo húmeda",
            "3 - Ocasionalmente húmeda",
            "4 - Raramente húmeda"
        )),
        (SelDominio "3. Actividad (nivel de actividad física)" "braden_actividad" @(
            "1 - Encamado",
            "2 - En silla",
            "3 - Deambula ocasionalmente",
            "4 - Deambula frecuentemente"
        )),
        (SelDominio "4. Movilidad (capacidad de cambiar y controlar la posición corporal)" "braden_movilidad" @(
            "1 - Completamente inmóvil",
            "2 - Muy limitada",
            "3 - Levemente limitada",
            "4 - Sin limitaciones"
        )),
        (SelDominio "5. Nutrición (patrón usual de ingesta de alimentos)" "braden_nutricion" @(
            "1 - Muy pobre",
            "2 - Probablemente inadecuada",
            "3 - Adecuada",
            "4 - Excelente"
        )),
        (SelDominio "6. Fricción y cizallamiento" "braden_friccion" @(
            "1 - Problema",
            "2 - Problema potencial",
            "3 - No aparente problema"
        ))
    )
}

# ========== RESULTADO ==========
$secResultado = @{
    id = newId; type = "section"; label = "RESULTADO"
    children = @(
        (Field "TOTAL Braden (6 - 23)" "braden_total" "calculated" 4 @{
            formula = "sum(braden_percepcion, braden_humedad, braden_actividad, braden_movilidad, braden_nutricion, braden_friccion)"
        }),
        (Field "Nivel de riesgo (automatico)" "braden_nivel" "calculated" 8 @{
            formula = 'cases(braden_total, "6-12=RIESGO ALTO;13-14=RIESGO MODERADO;15-18=RIESGO BAJO;19-23=SIN RIESGO")'
        }),
        (SH "Referencias: <=12 Riesgo alto | 13-14 Moderado | 15-18 Bajo | >=19 Sin riesgo"),
        (Field "Interpretación / Plan de intervención" "interpretacion" "textarea" 12 @{ enableVoice=$true; rows=3 })
    )
}

# ========== FIRMA ==========
$secFirma = @{
    id = newId; type = "section"; label = "OBSERVACIONES Y FIRMA"
    children = @(
        (Field "Observaciones del profesional" "observaciones" "textarea" 12 @{ enableVoice=$true; rows=3 }),
        (Field "Nombre del profesional" "profesional_nombre" "text" 8),
        (Field "Registro profesional" "profesional_registro" "text" 4),
        (Field "Firma del profesional (URL)" "firma_profesional" "text" 12)
    )
}

# ========== Ensamblar ==========
$schema = @{
    header = $header
    children = @($secDatos, $secBraden, $secResultado, $secFirma)
}

# ========== UPSERT ==========
$json = ($schema | ConvertTo-Json -Depth 40 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")

$nombre = "PP-FO-51 ESCALA DE BRADEN - RIESGO DE UPP"
$exists = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT 1 FROM form_definitions WHERE codigo='PP-FO-51' AND tenant_id='$TenantId';"
if ($exists -eq "1") {
    $sql = "UPDATE form_definitions SET nombre='$nombre', tipo='ESCALAS', schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='PP-FO-51' AND tenant_id='$TenantId';"
    Write-Host "UPDATE PP-FO-51" -ForegroundColor Yellow
} else {
    $newIdGuid = [Guid]::NewGuid().ToString()
    $sql = "INSERT INTO form_definitions (id, codigo, nombre, version, tipo, schema_json, activo, created_at, updated_at, tenant_id) VALUES ('$newIdGuid', 'PP-FO-51', '$nombre', '01', 'ESCALAS', '$jsonSql'::jsonb, true, '$now', '$now', '$TenantId');"
    Write-Host "INSERT PP-FO-51" -ForegroundColor Green
}

$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_braden_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK PP-FO-51 cargado." -ForegroundColor Green
