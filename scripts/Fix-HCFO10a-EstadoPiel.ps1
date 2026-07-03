# Fix-HCFO10a-EstadoPiel.ps1
# Reemplaza los 9 fields incorrectos bajo el subheading ESTADO DE LA PIEL
# en HC-FO-10a (Fisioterapia 4F) por los 5 correctos con defaults editables.
# Solo aplica a HC-FO-10a.
#
# Backup previo en scratchpad/backup_piel_hcfo10a_<timestamp>.json

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

$stamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
$bkPath = Join-Path $BackupRoot ("backup_piel_hcfo10a_$stamp.json")

# BACKUP
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-10a' AND tenant_id='$TenantId';"
if ([string]::IsNullOrWhiteSpace($raw)) { throw "HC-FO-10a no existe" }
[System.IO.File]::WriteAllText($bkPath, $raw, [System.Text.UTF8Encoding]::new($false))
Write-Host "Backup: $bkPath" -ForegroundColor DarkGray

$schema = $raw | ConvertFrom-Json -AsHashtable

function Field([string]$label, [string]$name, [string]$ft, [int]$width, [string]$default) {
    return @{
        id=newId; type="field"; fieldType=$ft; label=$label; name=$name
        widthColumns=$width; allowCustom=$false; required=$false
        defaultValue=$default
    }
}

# Nuevos 5 fields (mismos que HC-FO-10, con prefijo piel_ para evitar colision)
$nuevos = @(
    (Field "Integridad de la piel"          "piel_integridad"   "text" 4 "Conservada"),
    (Field "Coloración"                     "piel_coloracion"   "text" 4 "Normocrómica."),
    (Field "Temperatura"                    "piel_temperatura"  "text" 4 "Normotérmica."),
    (Field "Presencia de Úlceras por presión" "piel_ulceras"    "text" 6 "No se evidencian."),
    (Field "Localización"                   "piel_localizacion" "text" 6 "Sin alteraciones aparentes.")
)

# Localizar la seccion Encabezado del documento
$secIdx = -1
for ($i = 0; $i -lt $schema.children.Count; $i++) {
    if (([string]$schema.children[$i]["label"]) -eq "Encabezado del documento") { $secIdx = $i; break }
}
if ($secIdx -lt 0) { throw "No encontre seccion 'Encabezado del documento'" }
$sec = $schema.children[$secIdx]
$hijos = $sec.children

# Encontrar indice del subheading ESTADO DE LA PIEL
$pielIdx = -1
for ($i = 0; $i -lt $hijos.Count; $i++) {
    $c = $hijos[$i]
    if (([string]$c["type"]) -eq "text" -and ([string]$c["content"]) -eq "ESTADO DE LA PIEL") {
        $pielIdx = $i; break
    }
}
if ($pielIdx -lt 0) { throw "No encontre subheading ESTADO DE LA PIEL" }

# Encontrar donde termina el bloque (proximo subheading o fin)
$endIdx = $hijos.Count
for ($i = $pielIdx + 1; $i -lt $hijos.Count; $i++) {
    $c = $hijos[$i]
    if (([string]$c["type"]) -eq "text" -and ([string]$c["textStyle"]) -in @("subheading","heading")) {
        $endIdx = $i; break
    }
}
$oldCount = $endIdx - $pielIdx - 1
Write-Host "Bloque ESTADO DE LA PIEL: idx $pielIdx (subheading) + $oldCount fields antiguos" -ForegroundColor Cyan

# Construir nueva lista de hijos
$newHijos = New-Object System.Collections.ArrayList
for ($i = 0; $i -le $pielIdx; $i++) { [void]$newHijos.Add($hijos[$i]) }  # incluir subheading
foreach ($n in $nuevos) { [void]$newHijos.Add($n) }
for ($i = $endIdx; $i -lt $hijos.Count; $i++) { [void]$newHijos.Add($hijos[$i]) }

$sec["children"] = $newHijos.ToArray()
$schema.children[$secIdx] = $sec

Write-Host ("$oldCount fields antiguos removidos, 5 nuevos insertados") -ForegroundColor Green

# Persistir
$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='HC-FO-10a' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_piel10a_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK HC-FO-10a actualizado." -ForegroundColor Green
