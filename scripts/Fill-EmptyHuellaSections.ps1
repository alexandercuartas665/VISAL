# Fill-EmptyHuellaSections.ps1
# Busca secciones cuyo label es exactamente "HUELLA" y que NO tienen ya un
# field/textarea con label "HUELLA" adentro. Les inserta la caja como primer
# hijo. NO TOCA HC-FO-08.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

$codigos = @("PP-FO-17","PP-FO-18","PP-FO-37","PP-FO-37-PAD","PP-FO-88","PP-FO-89")

foreach ($cod in $codigos) {
    $raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $schema = $raw | ConvertFrom-Json -AsHashtable
    $cambio = $false
    for ($i=0; $i -lt $schema.children.Count; $i++) {
        $sec = $schema.children[$i]
        if ([string]$sec["label"] -ne "HUELLA") { continue }
        # ya tiene textarea HUELLA?
        $tieneCaja = $false
        foreach ($c in $sec["children"]) {
            if ($c["type"] -eq "field" -and $c["fieldType"] -eq "textarea" -and [string]$c["label"] -eq "HUELLA") { $tieneCaja = $true; break }
        }
        if ($tieneCaja) { continue }
        $caja = @{
            id = newId; type = "field"; fieldType = "textarea"
            label = "HUELLA"; name = ("huella_sec_" + $i)
            widthColumns = 3; rows = 4
            placeholder = "Espacio para huella"
            allowCustom = $false; required = $false
        }
        $sec["children"] = @($caja) + @($sec["children"])
        $cambio = $true
    }
    if (-not $cambio) { Write-Host ("  {0}: sin cambios" -f $cod); continue }
    Write-Host ("  {0}: caja HUELLA inyectada" -f $cod) -ForegroundColor Green

    $json = ($schema | ConvertTo-Json -Depth 30 -Compress)
    $jsonSql = $json.Replace("'","''")
    $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
    $sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $tmp = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
    try {
        $copy = "/tmp/visal_h2_$([Guid]::NewGuid().ToString('N')).sql"
        docker cp $tmp "${PgContainer}:${copy}" | Out-Null
        $env:MSYS_NO_PATHCONV = "1"
        $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
        $exit = $LASTEXITCODE
        docker exec $PgContainer rm $copy 2>$null | Out-Null
        $env:MSYS_NO_PATHCONV = $null
        if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
    } finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
}
