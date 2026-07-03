# Inspect-HC-DiagContext.ps1
# Read-only. Para cada HC, muestra 5 hijos alrededor de la palabra
# "DIAGNOSTICOS" (texto suelto) para entender el contexto y decidir
# como insertar la tabla.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"

$targets = @{
    "HC-FO-18" = @{ sec = -1; idx = -1 }   # no encontro nada
    "HC-FO-19" = @{ sec =  13; idx = 16 }
    "HC-FO-20" = @{ sec =  1;  idx = 39 }
    "HC-FO-21" = @{ sec =  1;  idx = 0  }
    "HC-FO-22" = @{ sec = -1; idx = -1 }
    "HC-FO-25" = @{ sec =  0;  idx = 51 }
}

foreach ($cod in $targets.Keys | Sort-Object) {
    Write-Host ""
    Write-Host ("========== {0} ==========" -f $cod) -ForegroundColor Cyan
    $raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $schema = $raw | ConvertFrom-Json -AsHashtable

    # Top-level:
    Write-Host "  Top-level (labels):"
    for ($i=0; $i -lt $schema.children.Count; $i++) {
        $sec = $schema.children[$i]
        $hijos = if ($sec.children) { $sec.children.Count } else { 0 }
        Write-Host ("    [{0}] {1} (h={2})" -f $i, $sec.label, $hijos)
    }
    $target = $targets[$cod]
    if ($target.sec -lt 0) {
        Write-Host "  (sin hit de DIAGNOSTICOS - hay que ver si insertar)" -ForegroundColor Yellow
        continue
    }
    $sec = $schema.children[$target.sec]
    if (-not $sec.children) { Write-Host "  seccion sin children"; continue }
    $from = [Math]::Max(0, $target.idx - 3)
    $to = [Math]::Min($sec.children.Count-1, $target.idx + 5)
    Write-Host ("  Contexto seccion [{0}] '{1}', hijos [{2}..{3}]:" -f $target.sec, $sec.label, $from, $to) -ForegroundColor Green
    for ($j=$from; $j -le $to; $j++) {
        $c = $sec.children[$j]
        $marker = if ($j -eq $target.idx) { "  >>" } else { "    " }
        if ($c.type -eq "text") {
            $preview = ([string]$c.content); if ($preview.Length -gt 70) { $preview = $preview.Substring(0,70)+"..." }
            Write-Host ("{0}[{1}] text/{2}: {3}" -f $marker, $j, $c.textStyle, $preview)
        } elseif ($c.type -eq "field") {
            Write-Host ("{0}[{1}] field/{2}: '{3}' name={4}" -f $marker, $j, $c.fieldType, $c.label, $c.name)
        } elseif ($c.type -eq "section") {
            Write-Host ("{0}[{1}] SECTION: '{2}'" -f $marker, $j, $c.label)
        }
    }
}
