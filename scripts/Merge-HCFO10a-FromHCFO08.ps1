# Merge-HCFO10a-FromHCFO08.ps1
# Trae 9 secciones de HC-FO-08 (LECTURA, no modifica HC-FO-08) y las mete en
# HC-FO-10a (FISIOTERAPIA 4F), conservando todo lo fisio-especifico.
#
# Lo que se TRAE de HC-FO-08:
#   DATOS PERSONALES, MOTIVO DE CONSULTA, ENFERMEDAD ACTUAL,
#   ANTECEDENTES FAMILIARES, ANTECEDENTES PERSONALES, GINECO OBSTETRICOS,
#   REVISION POR SISTEMAS, SIGNOS VITALES (con calculos), DIAGNOSTICOS (CIE-11)
#
# Lo que se CONSERVA de HC-FO-10a:
#   - Subheadings fisio-especificos del "Encabezado del documento":
#     VALORACION NEUROLOGICA, EVALUACION FISIOTERAPEUTICA, DOLOR, EDEMA,
#     ESTADO DE LA PIEL, SENSIBILIDAD, MARCHA  (se agrupan en una nueva
#     seccion top-level "EVALUACION CLINICA FISIOTERAPEUTICA")
#   - Secciones top-level: TEST MOVILIDAD ARTICULAR, FUERZA MUSCULAR,
#     VALORACION FISIOTERAPEUTICA, DIAGNOSTICO FISIOTERAPEUTICO,
#     OBJETIVO GENERAL, OBJETIVO ESPECIFICO, PLAN DE TRATAMIENTO,
#     Cierre, PROFESIONAL Y FIRMA
#
# IMPORTANTE: solo escribe HC-FO-10a. HC-FO-08 queda intacto.

[CmdletBinding()]
param(
    [string]$TenantId      = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$CodigoOrigen  = "HC-FO-08",
    [string]$CodigoDestino = "HC-FO-10a",
    [string]$PgContainer   = "visal-postgres",
    [string]$PgUser        = "visal",
    [string]$PgDb          = "visal_dev"
)
$ErrorActionPreference = "Stop"

function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

# 1) Leer schema de HC-FO-08 (origen, SOLO LECTURA)
$rawSrc = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo = '$CodigoOrigen' AND tenant_id = '$TenantId';"
if (-not $rawSrc) { throw "$CodigoOrigen no encontrado." }
$src = $rawSrc | ConvertFrom-Json -AsHashtable

# Indexar sus secciones top-level por label
$srcByLabel = @{}
foreach ($s in $src.children) {
    if ($s.type -eq "section" -and $s.label) { $srcByLabel[$s.label] = $s }
}

# Validar que existan las 9 secciones que vamos a traer
$labelsAImportar = @(
    "DATOS PERSONALES", "MOTIVO DE CONSULTA", "ENFERMEDAD ACTUAL",
    "ANTECEDENTES FAMILIARES", "ANTECEDENTES PERSONALES", "GINECO OBSTETRICOS",
    "REVISION POR SISTEMAS", "SIGNOS VITALES", "DIAGNÓSTICOS"
)
foreach ($l in $labelsAImportar) {
    if (-not $srcByLabel.ContainsKey($l)) { throw "Falta seccion '$l' en $CodigoOrigen." }
}

# 2) Leer schema actual de HC-FO-10a (destino)
$rawDst = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo = '$CodigoDestino' AND tenant_id = '$TenantId';"
if (-not $rawDst) { throw "$CodigoDestino no encontrado." }
$dst = $rawDst | ConvertFrom-Json -AsHashtable

# 3) Localizar el "Encabezado del documento" en destino y extraer SOLO los bloques
# fisio-especificos que viven dentro (a partir del subheading "VALORACION NEUROLOGICA"
# o "VALORACIÓN NEUROLÓGICA"). Los bloques anteriores (DATOS, ANAMNESIS, MOTIVO,
# SIGNOS VITALES, ANTECEDENTES) se descartan porque se reemplazan con las secciones
# de HC-FO-08.
$encabezado = $dst.children | Where-Object { $_.label -eq "Encabezado del documento" } | Select-Object -First 1
$fisioInline = @()
if ($encabezado) {
    $startIdx = -1
    for ($i = 0; $i -lt $encabezado.children.Count; $i++) {
        $c = $encabezado.children[$i]
        if ($c.type -eq "text" -and ($c.content -like "*VALORACION NEUROLOGICA*" -or $c.content -like "*VALORACIÓN NEUROLÓGICA*" -or $c.content -like "*VALORACION NEUROL*")) {
            $startIdx = $i; break
        }
    }
    if ($startIdx -ge 0) {
        for ($i = $startIdx; $i -lt $encabezado.children.Count; $i++) {
            $fisioInline += $encabezado.children[$i]
        }
    }
}
Write-Host "    Bloques fisio preservados del Encabezado: $($fisioInline.Count)" -ForegroundColor Cyan

# Construir nueva seccion "EVALUACION CLINICA FISIOTERAPEUTICA"
$secEvalFisio = @{
    id = newId; type = "section"; label = "EVALUACION CLINICA FISIOTERAPEUTICA"
    isSection = $true; isText = $false; isTable = $false
    lockRows = $false; required = $false; allowCustom = $false; widthColumns = 12
    children = $fisioInline
}

# 4) Conservar las secciones top-level fisio-especificas existentes (todas las
# que NO son "Encabezado del documento").
$labelsFisioConservar = @(
    "TEST MOVILIDAD ARTICULAR", "FUERZA MUSCULAR",
    "VALORACION FISIOTERAPEUTICA", "DIAGNOSTICO FISIOTERAPEUTICO",
    "OBJETIVO GENERAL", "OBJETIVO ESPECIFICO", "PLAN DE TRATAMIENTO",
    "Cierre", "PROFESIONAL Y FIRMA"
)
$dstByLabel = @{}
foreach ($s in $dst.children) {
    if ($s.label) { $dstByLabel[$s.label] = $s }
}

# 5) Armar el orden final
$nuevasSecciones = @()
# Primero: las 8 secciones traidas de HC-FO-08 (en este orden)
foreach ($l in @(
    "DATOS PERSONALES", "MOTIVO DE CONSULTA", "ENFERMEDAD ACTUAL",
    "ANTECEDENTES FAMILIARES", "ANTECEDENTES PERSONALES", "GINECO OBSTETRICOS",
    "REVISION POR SISTEMAS", "SIGNOS VITALES"
)) {
    $nuevasSecciones += $srcByLabel[$l]
}
# Despues: evaluacion fisio inline (la seccion nueva que armamos arriba)
if ($fisioInline.Count -gt 0) { $nuevasSecciones += $secEvalFisio }
# Despues: las 7 secciones fisio top-level existentes (en el orden actual)
foreach ($l in @(
    "TEST MOVILIDAD ARTICULAR", "FUERZA MUSCULAR",
    "VALORACION FISIOTERAPEUTICA", "DIAGNOSTICO FISIOTERAPEUTICO",
    "OBJETIVO GENERAL", "OBJETIVO ESPECIFICO", "PLAN DE TRATAMIENTO"
)) {
    if ($dstByLabel.ContainsKey($l)) { $nuevasSecciones += $dstByLabel[$l] }
}
# DIAGNOSTICOS (CIE-11) de HC-FO-08
$nuevasSecciones += $srcByLabel["DIAGNÓSTICOS"]
# Cierre + PROFESIONAL Y FIRMA al final
foreach ($l in @("Cierre", "PROFESIONAL Y FIRMA")) {
    if ($dstByLabel.ContainsKey($l)) { $nuevasSecciones += $dstByLabel[$l] }
}

# 6) Reemplazar children
$dst.children = $nuevasSecciones

# 7) Persistir SOLO HC-FO-10a
$out    = ($dst | ConvertTo-Json -Depth 30 -Compress)
$outSql = $out.Replace("'","''")
$now    = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql    = "UPDATE form_definitions SET schema_json = '$outSql'::jsonb, updated_at = '$now' WHERE codigo = '$CodigoDestino' AND tenant_id = '$TenantId';"

$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_merge_hcfo10a_$([Guid]::NewGuid().ToString('N')).sql"
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
Write-Host "OK $CodigoDestino actualizado. Schema final: $($out.Length) bytes" -ForegroundColor Green
Write-Host "HC-FO-08 NO se modifico (solo lectura)." -ForegroundColor Yellow
Write-Host ""
Write-Host "Secciones top-level finales:" -ForegroundColor Cyan
foreach ($s in $nuevasSecciones) {
    Write-Host ("  - {0} ({1} hijos)" -f $s.label, $s.children.Count)
}
