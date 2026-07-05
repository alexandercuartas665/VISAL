# Fix-HCFO10a-DomPatronAPTA.ps1
# Convierte las columnas 'dominio' y 'patron' de la tabla
# diagnostico_fisioterapeutico (HC-FO-10a) en:
#   - dominio: select con 4 opciones fijas del catalogo APTA
#   - patron : select dependiente (optionsMapKey='dominio') con las
#              25 opciones del catalogo APTA distribuidas por dominio.
#
# El JSON del catalogo se lee de scratchpad/apta_map.json (extraido del xlsx
# maestro Dominios_y_Patrones_APTA_Fisioterapia.xlsx).
#
# Backup previo en scratchpad/backup_dompat_hcfo10a_<timestamp>.json

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev",
    [string]$BackupRoot  = "C:\Users\acuartas\AppData\Local\Temp\claude\C--DesarrolloIA-Visal\3a114262-030a-4135-852f-4f6e57a10abf\scratchpad",
    [string]$MapPath     = "C:\Users\acuartas\AppData\Local\Temp\claude\C--DesarrolloIA-Visal\3a114262-030a-4135-852f-4f6e57a10abf\scratchpad\apta_map.json"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

if (-not (Test-Path $MapPath)) { throw "No existe $MapPath" }
$mapRaw = [System.IO.File]::ReadAllText($MapPath, [System.Text.UTF8Encoding]::new($false))
$optionsMap = $mapRaw | ConvertFrom-Json -AsHashtable
$dominios = @($optionsMap.Keys)
Write-Host ("Catalogo cargado: {0} dominios, {1} patrones totales" -f $dominios.Count, ($optionsMap.Values | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum) -ForegroundColor Cyan

$stamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
$bkPath = Join-Path $BackupRoot ("backup_dompat_hcfo10a_$stamp.json")

# BACKUP
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-10a' AND tenant_id='$TenantId';"
if ([string]::IsNullOrWhiteSpace($raw)) { throw "HC-FO-10a no existe" }
[System.IO.File]::WriteAllText($bkPath, $raw, [System.Text.UTF8Encoding]::new($false))
Write-Host "Backup: $bkPath" -ForegroundColor DarkGray

$schema = $raw | ConvertFrom-Json -AsHashtable

# Buscar y actualizar la tabla diagnostico_fisioterapeutico
$found = $false
foreach ($sec in $schema.children) {
    if ($null -eq $sec["children"]) { continue }
    for ($i = 0; $i -lt $sec.children.Count; $i++) {
        $c = $sec.children[$i]
        if ($c["fieldType"] -ne "table") { continue }
        if (([string]$c["name"]) -ne "diagnostico_fisioterapeutico") { continue }

        $newCols = New-Object System.Collections.ArrayList
        foreach ($col in $c.columns) {
            $nm = [string]$col["name"]
            if ($nm -eq "dominio") {
                $newCol = @{
                    id = if ($col["id"]) { $col["id"] } else { newId }
                    name = "dominio"; label = "Dominio"; fieldType = "select"
                    options = $dominios
                    allowCustom = $false
                    defaultValue = ""
                }
                [void]$newCols.Add($newCol)
                Write-Host "  columna 'dominio' -> select (4 opciones)" -ForegroundColor Green
            } elseif ($nm -eq "patron") {
                $newCol = @{
                    id = if ($col["id"]) { $col["id"] } else { newId }
                    name = "patron"; label = "Patron"; fieldType = "select"
                    optionsMapKey = "dominio"
                    optionsMap = $optionsMap
                    allowCustom = $false
                    defaultValue = ""
                    placeholder = "Selecciona un dominio primero"
                }
                [void]$newCols.Add($newCol)
                Write-Host "  columna 'patron' -> select dependiente (optionsMapKey=dominio, 25 opciones)" -ForegroundColor Green
            } else {
                [void]$newCols.Add($col)
            }
        }
        $c["columns"] = $newCols.ToArray()
        $sec.children[$i] = $c
        $found = $true
    }
}
if (-not $found) { throw "No encontre tabla 'diagnostico_fisioterapeutico' en HC-FO-10a" }

# Persistir
$json = ($schema | ConvertTo-Json -Depth 40 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='HC-FO-10a' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_dompat_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK HC-FO-10a actualizado." -ForegroundColor Green
