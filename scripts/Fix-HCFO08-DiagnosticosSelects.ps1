# Fix-HCFO08-DiagnosticosSelects.ps1
# Convierte columnas Origen / Tipo / Relacion de la tabla Diagnosticos en HC-FO-08
# a select con las listas del cliente. Preserva CIE-10 autocomplete tal cual.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev",
    [string]$Codigo      = "HC-FO-08"
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
$schema = $raw | ConvertFrom-Json -AsHashtable

# ============ Buscar y actualizar tabla Diagnosticos in-place ============
$found = $false
foreach ($sec in $schema.children) {
    if ($null -eq $sec["children"]) { continue }
    for ($i = 0; $i -lt $sec.children.Count; $i++) {
        $c = $sec.children[$i]
        if (($c["fieldType"] -eq "table") -and ($c["name"] -eq "Diagnosticos")) {
            for ($j = 0; $j -lt $c.columns.Count; $j++) {
                $col = $c.columns[$j]
                $nm = [string]$col["name"]
                $lb = [string]$col["label"]
                switch -Regex ($nm + "|" + $lb) {
                    "Origen"    {
                        $col["fieldType"]   = "select"
                        $col["options"]     = $origenOptions
                        $col["allowCustom"] = $true
                        $col["defaultValue"] = "ENFERMEDAD GENERAL"
                        Write-Host "Columna Origen -> select (15 opciones, defaultValue ENFERMEDAD GENERAL)" -ForegroundColor Green
                        break
                    }
                    "Tipo"      {
                        if ($nm -ne "Origen") {
                            if ([string]::IsNullOrEmpty($nm)) { $col["name"] = "tipo" }
                            $col["fieldType"]   = "select"
                            $col["options"]     = $tipoOptions
                            $col["allowCustom"] = $false
                            $col["defaultValue"] = "1 - IMPRESIÓN DIAGNÓSTICA"
                            Write-Host "Columna Tipo -> select (3 opciones, defaultValue 1 - IMPRESION DIAGNOSTICA)" -ForegroundColor Green
                        }
                        break
                    }
                    "[Rr]elaci" {
                        $col["fieldType"]   = "select"
                        $col["options"]     = $relacionOptions
                        $col["allowCustom"] = $false
                        $col["defaultValue"] = "PRINCIPAL"
                        Write-Host "Columna Relacion -> select (2 opciones, defaultValue PRINCIPAL)" -ForegroundColor Green
                        break
                    }
                }
                $c.columns[$j] = $col
            }
            $sec.children[$i] = $c
            $found = $true
        }
    }
}
if (-not $found) { throw "No encontre tabla Diagnosticos en $Codigo" }

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
