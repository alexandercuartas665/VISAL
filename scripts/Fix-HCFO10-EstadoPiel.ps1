# Fix-HCFO10-EstadoPiel.ps1
# Agrega la sub-seccion ESTADO DE LA PIEL a HC-FO-10 (Fisiatria Completa)
# con 5 campos y valores por defecto que el medico puede editar.
# Solo aplica a HC-FO-10.
#
# Backup previo en scratchpad/backup_piel_hcfo10_<timestamp>.json

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
$bkPath = Join-Path $BackupRoot ("backup_piel_hcfo10_$stamp.json")

# BACKUP
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-10' AND tenant_id='$TenantId';"
if ([string]::IsNullOrWhiteSpace($raw)) { throw "HC-FO-10 no existe" }
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

# Campos ESTADO DE LA PIEL (labels en negrita, defaults del usuario)
$subheading = @{ id=newId; type="text"; textStyle="subheading"; content="ESTADO DE LA PIEL" }
$nuevos = @(
    $subheading,
    (Field "Integridad de la piel"          "piel_integridad"   "text" 4 "Conservada"),
    (Field "Coloración"                     "piel_coloracion"   "text" 4 "Normocrómica."),
    (Field "Temperatura"                    "piel_temperatura"  "text" 4 "Normotérmica."),
    (Field "Presencia de Úlceras por presión" "piel_ulceras"    "text" 6 "No se evidencian."),
    (Field "Localización"                   "piel_localizacion" "text" 6 "Sin alteraciones aparentes.")
)

# Insertar al final de la seccion 'Examen fisico'
$found = $false
for ($si = 0; $si -lt $schema.children.Count; $si++) {
    $sec = $schema.children[$si]
    if (([string]$sec["label"]) -eq "Examen fisico") {
        $newChildren = New-Object System.Collections.ArrayList
        foreach ($ch in $sec.children) { [void]$newChildren.Add($ch) }
        foreach ($n  in $nuevos)        { [void]$newChildren.Add($n) }
        $sec["children"] = $newChildren.ToArray()
        $schema.children[$si] = $sec
        $found = $true
        break
    }
}
if (-not $found) { throw "No encontre seccion 'Examen fisico' en HC-FO-10" }
Write-Host "ESTADO DE LA PIEL agregada al final de Examen fisico (5 campos + subheading)" -ForegroundColor Green

# Persistir
$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='HC-FO-10' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_piel10_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK HC-FO-10 actualizado." -ForegroundColor Green
