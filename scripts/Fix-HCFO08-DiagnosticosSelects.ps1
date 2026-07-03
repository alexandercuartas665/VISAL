# Fix-HCFO08-DiagnosticosSelects.ps1
# Convierte columnas Origen / Tipo / Relacion de la tabla Diagnosticos en un HC
# a select con las listas del cliente. Preserva CIE-11 autocomplete tal cual.
#
# Detecta tabla por name lower = 'diagnosticos'.
# Actualiza cada columna solo si existe. Reporta lo omitido.

[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)]
    [string]$Codigo,
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"

# ============ Listas cliente ============
$origenOptions = @(
    "ENFERMEDAD GENERAL",
    "ENFERMEDAD PROFESIONAL",
    "ACCIDENTE TRABAJO",
    "ACCIDENTE TRÁNSITO",
    "ACCIDENTE OFÍDICO",
    "ACCIDENTE RÁBICO",
    "EVENTO CATASTRÓFICO",
    "LESION AGRESION",
    "LESION AUTO INFLINGIDA",
    "SOSPECHA ABUSO SEXUAL",
    "SOSPECHA MALTRATO EMOCIONAL",
    "SOSPECHA MALTRATO FÍSICO",
    "SOSPECHA VIOLENCIA SEXUAL",
    "OTRA",
    "OTRO TIPO ACCIDENTE"
)
$tipoOptions = @(
    "1 - IMPRESIÓN DIAGNÓSTICA",
    "2 - DIAGNÓSTICO CONFIRMADO NUEVO",
    "3 - DIAGNÓSTICO CONFIRMADO REPETIDO"
)
$relacionOptions = @("PRINCIPAL","RELACIONADO")

# ============ Cargar schema ============
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$Codigo' AND tenant_id='$TenantId';"
if ([string]::IsNullOrWhiteSpace($raw)) { throw "No existe $Codigo" }
$schema = $raw | ConvertFrom-Json -AsHashtable

# ============ Buscar y actualizar tabla Diagnosticos in-place ============
$foundTable = $false
$updOrigen = $false; $updTipo = $false; $updRelacion = $false
foreach ($sec in $schema.children) {
    if ($null -eq $sec["children"]) { continue }
    for ($i = 0; $i -lt $sec.children.Count; $i++) {
        $c = $sec.children[$i]
        if (($c["fieldType"] -ne "table")) { continue }
        $tblName = ([string]$c["name"]).ToLowerInvariant()
        if ($tblName -ne "diagnosticos") { continue }
        $foundTable = $true
        for ($j = 0; $j -lt $c.columns.Count; $j++) {
            $col = $c.columns[$j]
            $nm = ([string]$col["name"]).ToLowerInvariant()
            $lb = ([string]$col["label"]).ToLowerInvariant()
            # Origen: name o label empieza con 'origen'
            if ($nm.StartsWith("origen") -or $lb.StartsWith("origen")) {
                $col["fieldType"]    = "select"
                $col["options"]      = $origenOptions
                $col["allowCustom"]  = $true
                $col["defaultValue"] = "ENFERMEDAD GENERAL"
                $updOrigen = $true
            }
            # Tipo: label empieza con 'tipo' (incluye 'Tipo de diagnostico principal')
            elseif ($nm.StartsWith("tipo") -or $lb.StartsWith("tipo")) {
                if ([string]::IsNullOrEmpty($col["name"])) { $col["name"] = "tipo" }
                $col["fieldType"]    = "select"
                $col["options"]      = $tipoOptions
                $col["allowCustom"]  = $false
                $col["defaultValue"] = "1 - IMPRESIÓN DIAGNÓSTICA"
                $updTipo = $true
            }
            # Relacion: name o label empieza con 'relaci'
            elseif ($nm.StartsWith("relaci") -or $lb.StartsWith("relaci")) {
                $col["fieldType"]    = "select"
                $col["options"]      = $relacionOptions
                $col["allowCustom"]  = $false
                $col["defaultValue"] = "PRINCIPAL"
                $updRelacion = $true
            }
            $c.columns[$j] = $col
        }
        $sec.children[$i] = $c
    }
}
if (-not $foundTable) {
    Write-Host "[$Codigo] SIN tabla 'diagnosticos' — omitido" -ForegroundColor Yellow
    return
}

Write-Host ("[$Codigo] Origen={0}, Tipo={1}, Relacion={2}" -f $updOrigen, $updTipo, $updRelacion) -ForegroundColor Green

# ============ Persistir ============
$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='$Codigo' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_fix_diag_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK $Codigo actualizado." -ForegroundColor Green
