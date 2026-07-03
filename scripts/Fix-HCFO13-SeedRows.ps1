# Fix-HCFO13-SeedRows.ps1
# Fix quirurgico: reemplaza el seedRows aplanado (array plano de strings)
# en las 2 tablas bioq_realizados y antropometria por seedRows correctos
# (array de arrays de strings, una fila por sub-array).

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"

$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-13' AND tenant_id='$TenantId';"
$schema = $raw | ConvertFrom-Json -AsHashtable

$enc = $schema.children[0]
$fixed = 0
foreach ($i in 0..($enc.children.Count-1)) {
    $c = $enc.children[$i]
    if ($c["type"] -eq "field" -and $c["fieldType"] -eq "table") {
        $seed = $c["seedRows"]
        if ($null -eq $seed -or $seed.Count -eq 0) { continue }
        # Detectar aplanado: si primer elemento es string en lugar de array
        $primero = $seed[0]
        $esPlano = ($primero -is [string])
        if ($esPlano) {
            # Envolver en array de 1 fila
            $nueva = New-Object System.Collections.ArrayList
            $fila = New-Object System.Collections.ArrayList
            foreach ($v in $seed) { [void]$fila.Add($v) }
            [void]$nueva.Add($fila.ToArray())
            $c["seedRows"] = $nueva.ToArray()
            $fixed++
            Write-Host ("  [{0}] tabla '{1}' seedRows aplanados corregidos ({2} celdas -> 1 fila)" -f $i, $c["name"], $seed.Count) -ForegroundColor Green
        }
    }
}
Write-Host ("Total tablas corregidas: {0}" -f $fixed) -ForegroundColor Cyan

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='HC-FO-13' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_fix13_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
Write-Host "OK." -ForegroundColor Green
