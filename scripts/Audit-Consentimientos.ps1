# Audit-Consentimientos.ps1
# Auditoria READ-ONLY de todos los form_definitions tipo=CONSENTIMIENTO:
# - Muestra estructura de secciones (label + count hijos)
# - Muestra targets del prefill_routes_json
# - Verifica que cada target del prefill existe realmente como campo del
#   schema (por 'name'), y reporta huerfanos (target sin campo).

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"

function Collect-FieldNames {
    param($nodo, [System.Collections.Generic.HashSet[string]]$acc)
    if ($nodo -is [System.Collections.IDictionary]) {
        if ($nodo["type"] -eq "field" -and $nodo["name"]) { [void]$acc.Add([string]$nodo["name"]) }
        # Columnas de tablas
        if ($nodo["columns"]) {
            foreach ($col in $nodo["columns"]) { if ($col["name"]) { [void]$acc.Add([string]$col["name"]) } }
        }
        foreach ($k in $nodo.Keys) {
            $v = $nodo[$k]
            if ($v -is [System.Collections.IDictionary] -or $v -is [System.Collections.IList]) { Collect-FieldNames $v $acc }
        }
    } elseif ($nodo -is [System.Collections.IList]) {
        foreach ($it in $nodo) { if ($it -is [System.Collections.IDictionary] -or $it -is [System.Collections.IList]) { Collect-FieldNames $it $acc } }
    }
}

$codigos = (docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT codigo FROM form_definitions WHERE tenant_id='$TenantId' AND tipo='CONSENTIMIENTO' ORDER BY codigo;") -split "`n"

$totalOK = 0; $totalHuerfano = 0; $sinPrefill = @()
foreach ($cod in $codigos) {
    $cod = $cod.Trim(); if (-not $cod) { continue }
    $rawS = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $rawP = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT coalesce(prefill_routes_json::text,'null') FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    Write-Host ""
    Write-Host ("=== {0} ===" -f $cod) -ForegroundColor Cyan
    try {
        $schema = $rawS | ConvertFrom-Json -AsHashtable
        $names = New-Object System.Collections.Generic.HashSet[string]
        Collect-FieldNames $schema $names
        Write-Host ("  Secciones top-level:")
        for ($i=0; $i -lt $schema.children.Count; $i++) {
            $sec = $schema.children[$i]
            $hijos = if ($sec.children) { $sec.children.Count } else { 0 }
            Write-Host ("    [{0}] {1} (hijos={2}, type={3})" -f $i, $sec.label, $hijos, $sec.type)
        }
        Write-Host ("  Campos con 'name' (total): {0}" -f $names.Count)
    } catch { Write-Host ("  ERROR parseando schema: {0}" -f $_) -ForegroundColor Red; continue }

    if ($rawP -eq "null" -or -not $rawP) {
        Write-Host "  prefill_routes_json: NULL" -ForegroundColor Yellow
        $sinPrefill += $cod
        continue
    }
    try {
        $prefill = $rawP | ConvertFrom-Json -AsHashtable
        if (-not $prefill.routes) { Write-Host "  prefill_routes_json: sin 'routes'" -ForegroundColor Yellow; continue }
        $huerf = @(); $ok = 0
        foreach ($r in $prefill.routes) {
            $rn = $r.name; $sm = $r.sourceModule
            if (-not $r.mappings) { continue }
            foreach ($m in $r.mappings) {
                $t = [string]$m.target
                if (-not $t) { continue }
                if ($names.Contains($t)) { $ok++ } else { $huerf += "$rn -> $t" }
            }
        }
        Write-Host ("  prefill_routes: rutas={0}, mappings OK={1}, HUERFANOS={2}" -f $prefill.routes.Count, $ok, $huerf.Count) -ForegroundColor $(if ($huerf.Count -gt 0) { "Yellow" } else { "Green" })
        foreach ($h in ($huerf | Select-Object -Unique)) { Write-Host ("    huerfano: {0}" -f $h) -ForegroundColor Yellow }
        $totalOK += $ok; $totalHuerfano += $huerf.Count
    } catch { Write-Host ("  ERROR parseando prefill: {0}" -f $_) -ForegroundColor Red }
}
Write-Host ""
Write-Host ("=========== RESUMEN ===========") -ForegroundColor Cyan
Write-Host ("  Mappings OK totales: {0}" -f $totalOK)
Write-Host ("  Mappings HUERFANOS totales: {0}" -f $totalHuerfano) -ForegroundColor $(if ($totalHuerfano -gt 0) { "Yellow" } else { "Green" })
if ($sinPrefill.Count -gt 0) {
    Write-Host ("  Consentimientos SIN prefill: {0}" -f ($sinPrefill -join ", ")) -ForegroundColor Yellow
}
