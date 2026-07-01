# Fix-Huellas.ps1
# Sanea los sitios con "HUELLAHUELLA" en los consentimientos:
#   - text/paragraph cuyo content contiene "HUELLAHUELLA":
#       * corta el prefijo o la ocurrencia
#       * inserta ANTES un field/textarea con label "HUELLA"
#         (widthColumns=3, rows=4) que actua como caja marca
#   - section cuyo label empieza por "HUELLAHUELLA":
#       * limpia el label
#       * inserta como PRIMER hijo un field/textarea HUELLA
# NO TOCA HC-FO-08. Backups previos ya en /tmp/consent_backups/.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

function New-CajaHuella {
    param([int]$n)
    return @{
        id = newId; type = "field"; fieldType = "textarea"
        label = "HUELLA"; name = ("huella_" + $n)
        widthColumns = 3; rows = 4
        placeholder = "Espacio para huella"
        allowCustom = $false; required = $false
    }
}

# Recorre un array de nodos. Para cada text/paragraph con "HUELLAHUELLA" en
# content: quita esa cadena e inserta una caja huella justo antes. Recursivo
# sobre 'children' de cada nodo. Retorna un nuevo array.
function Process-NodesArray {
    param($arr, [ref]$counter, [ref]$changes)
    $out = @()
    foreach ($n in $arr) {
        if ($n -is [System.Collections.IDictionary]) {
            # section con label HUELLAHUELLA*
            if ($n["type"] -eq "section" -and [string]$n["label"] -match "^HUELLAHUELLA") {
                $newLabel = ([string]$n["label"]) -replace "^HUELLAHUELLA",""
                if (-not $newLabel) { $newLabel = "HUELLA" }
                $n["label"] = $newLabel
                $counter.Value++
                $caja = (New-CajaHuella $counter.Value)
                $kids = @($caja) + @($n["children"])
                $n["children"] = $kids
                $changes.Value++
            }

            # text/paragraph con HUELLAHUELLA en content
            if ($n["type"] -eq "text" -and [string]$n["content"] -match "HUELLAHUELLA") {
                $counter.Value++
                $caja = (New-CajaHuella $counter.Value)
                $out += $caja
                $n["content"] = ([string]$n["content"]) -replace "HUELLAHUELLA",""
                $changes.Value++
            }

            # Recursion sobre children
            if ($n["children"] -is [System.Collections.IList]) {
                $n["children"] = Process-NodesArray $n["children"] $counter $changes
            }
        }
        $out += $n
    }
    return $out
}

$codigos = @("PP-FO-112","PP-FO-113","PP-FO-17","PP-FO-18","PP-FO-20","PP-FO-22",
             "PP-FO-23","PP-FO-24","PP-FO-32","PP-FO-35","PP-FO-37","PP-FO-37-PAD",
             "PP-FO-66","PP-FO-69","PP-FO-81","PP-FO-88","PP-FO-89","PP-FO-90","PP-FO-96")

$totalCambios = 0
foreach ($cod in $codigos) {
    $raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    if (-not $raw) { Write-Host ("  {0}: no existe" -f $cod) -ForegroundColor Red; continue }
    $schema = $raw | ConvertFrom-Json -AsHashtable

    $counter = [ref](0); $changes = [ref](0)
    $schema.children = Process-NodesArray $schema.children $counter $changes
    if ($changes.Value -eq 0) { Write-Host ("  {0}: sin cambios" -f $cod); continue }

    Write-Host ("  {0}: {1} cambios (huella insertada)" -f $cod, $changes.Value) -ForegroundColor Green
    $totalCambios += $changes.Value

    $json = ($schema | ConvertTo-Json -Depth 30 -Compress)
    $jsonSql = $json.Replace("'","''")
    $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
    $sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $tmp = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
    try {
        $copy = "/tmp/visal_huella_$([Guid]::NewGuid().ToString('N')).sql"
        docker cp $tmp "${PgContainer}:${copy}" | Out-Null
        $env:MSYS_NO_PATHCONV = "1"
        $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
        $exit = $LASTEXITCODE
        docker exec $PgContainer rm $copy 2>$null | Out-Null
        $env:MSYS_NO_PATHCONV = $null
        if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
    } finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
}
Write-Host ""
Write-Host ("Total cambios: {0}" -f $totalCambios) -ForegroundColor Cyan
