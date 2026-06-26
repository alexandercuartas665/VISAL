# Merge-HCFO08-FromProd.ps1
# Toma el JSON de produccion (exportado via consola SQL como CSV),
# reemplaza UNICAMENTE la seccion "SIGNOS VITALES" por la version local con
# calculados (tensionClass / imc / imcClass / perimetroRiesgo), y aplica el
# UPDATE sobre HC-FO-08 en la BD local. Todo el resto del schema queda igual
# al de prod (DATOS PERSONALES, ANTECEDENTES, GINECO, REVISION, EXAMEN FISICO,
# ACTIVIDAD, HABITOS, ANALISIS, DIAGNOSTICOS CIE-11, PLAN MANEJO, EDUCACION,
# SERVICIOS, MEDICAMENTOS, REMISIONES, LABORATORIOS, INSUMOS, INCAPACIDADES,
# MEDICO con todos los cambios manuales que el usuario ya habia hecho).

[CmdletBinding()]
param(
    [string]$CsvPath  = "C:\Users\acuartas\Downloads\consulta-sql-20260626-140425.csv",
    [string]$TenantId = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$Codigo   = "HC-FO-08",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser   = "visal",
    [string]$PgDb     = "visal_dev"
)
$ErrorActionPreference = "Stop"

function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

# 1) Leer el CSV y extraer el JSON. La fila 1 es el header "schema_json".
#    La fila 2 es el dato envuelto en comillas dobles, con "" como escape
#    para una comilla literal.
if (-not (Test-Path $CsvPath)) { throw "No encuentro $CsvPath" }
$lines = Get-Content $CsvPath -Encoding UTF8
# Algunos exports rompen el JSON en varias lineas; juntamos desde la 2da hasta
# la ultima no vacia (preservando contenido).
$raw = ($lines | Select-Object -Skip 1) -join "`n"
$raw = $raw.Trim()
if ($raw.StartsWith('"')) {
    # Quitar comilla inicial y final (CSV envuelve el campo entre dobles comillas)
    $raw = $raw.Substring(1)
    if ($raw.EndsWith('"')) { $raw = $raw.Substring(0, $raw.Length - 1) }
    # Desescapar "" -> "
    $raw = $raw -replace '""','"'
}
$schema = $raw | ConvertFrom-Json -AsHashtable

Write-Host "==> Schema leido de prod" -ForegroundColor Cyan
Write-Host ("    Secciones top-level: {0}" -f $schema.children.Count)

# 2) Localizar la seccion SIGNOS VITALES por label.
$sigIndex = -1
for ($i = 0; $i -lt $schema.children.Count; $i++) {
    if ($schema.children[$i].label -eq "SIGNOS VITALES") { $sigIndex = $i; break }
}
if ($sigIndex -lt 0) { throw "No se encontro la seccion 'SIGNOS VITALES' en el JSON de prod." }
$origSectionId = $schema.children[$sigIndex].id
Write-Host ("    SIGNOS VITALES esta en indice [{0}], id={1}" -f $sigIndex, $origSectionId) -ForegroundColor Green

# 3) Construir la nueva seccion (mismo id que tenia en prod, contenido nuevo)
$newSig = @{
    id        = $origSectionId
    type      = "section"
    label     = "SIGNOS VITALES"
    isSection = $true
    isText    = $false
    isTable   = $false
    lockRows  = $false
    required  = $false
    allowCustom = $false
    widthColumns = 12
    children  = @(
        # --- Tension arterial ---
        @{ id = newId; type = "text"; textStyle = "subheading"; content = "Tension Arterial (mm Hg)" },
        @{ id = newId; type = "field"; fieldType = "number";     label = "Sistolica";              name = "ta_sistolica";      widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "number";     label = "Diastolica";             name = "ta_diastolica";     widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "calculated"; label = "Clasificacion TA (auto)"; name = "ta_clasificacion";  widthColumns = 6;
           formula = "tensionClass(ta_sistolica, ta_diastolica)" },

        # --- Otros vitales ---
        @{ id = newId; type = "field"; fieldType = "number"; label = "F. Cardiaca (x min)";     name = "fc";              widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "number"; label = "F. Respiratoria (x min)"; name = "fr";              widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "number"; label = "Pulsioximetria (%)";      name = "pulsioximetria";  widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "number"; label = "Temperatura (C)";         name = "temperatura";     widthColumns = 3 },

        # --- Peso, talla, IMC, clasificacion IMC ---
        @{ id = newId; type = "field"; fieldType = "number";     label = "Peso (Kg)";                name = "peso";              widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "number";     label = "Talla (cm)";               name = "talla";             widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "calculated"; label = "IMC (auto)";               name = "imc";               widthColumns = 3;
           formula = "imc(peso, talla)" },
        @{ id = newId; type = "field"; fieldType = "calculated"; label = "Clasificacion IMC (auto)"; name = "imc_clasificacion"; widthColumns = 3;
           formula = "imcClass(imc)" },

        # --- Perimetro abdominal + interpretacion segun sexo ---
        @{ id = newId; type = "field"; fieldType = "number";     label = "Perimetro Abdominal (cm)"; name = "perimetro";          widthColumns = 4 },
        @{ id = newId; type = "field"; fieldType = "calculated"; label = "Interpretacion (auto, segun sexo)"; name = "perimetro_riesgo"; widthColumns = 4;
           formula = "perimetroRiesgo(perimetro, sexo)" },

        # --- Lateralidad ---
        @{ id = newId; type = "field"; fieldType = "select"; label = "Lateralidad Dominante"; name = "lateralidad"; widthColumns = 4;
           catalog = "estatico"; options = @("DIESTRO","ZURDO","AMBIDIESTRO") }
    )
}

# 4) Reemplazar en su MISMO indice (no mover de posicion)
$schema.children[$sigIndex] = $newSig

# 5) Persistir
$out    = ($schema | ConvertTo-Json -Depth 30 -Compress)
$outSql = $out.Replace("'","''")
$now    = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql    = "UPDATE form_definitions SET schema_json = '$outSql'::jsonb, updated_at = '$now' WHERE codigo = '$Codigo' AND tenant_id = '$TenantId';"

$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_merge_hcfo08_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally {
    Remove-Item $tmp -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "OK HC-FO-08 actualizado. Schema final: $($out.Length) bytes" -ForegroundColor Green
Write-Host "Secciones top-level (orden de prod conservado):" -ForegroundColor Cyan
foreach ($s in $schema.children) {
    $tag = if ($s.label -eq "SIGNOS VITALES") { " <-- REEMPLAZADA con calculados" } else { "" }
    Write-Host ("  - {0}{1}" -f $s.label, $tag)
}
