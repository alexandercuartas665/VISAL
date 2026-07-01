# Validate-EveryConsentimiento.ps1
# READ-ONLY. Muestra el estado detallado de cada consentimiento tras la
# ronda de correcciones. Para cada uno reporta: secciones (labels),
# total de campos con name, presencia de la seccion auto de datos, count
# de cajas HUELLA, y rutas de prefill.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"

function Collect-Fields {
    param($nodo, [System.Collections.ArrayList]$acc)
    if ($nodo -is [System.Collections.IDictionary]) {
        if ($nodo["type"] -eq "field" -and $nodo["name"]) { [void]$acc.Add([pscustomobject]@{ name=[string]$nodo["name"]; label=[string]$nodo["label"]; fieldType=[string]$nodo["fieldType"] }) }
        foreach ($k in $nodo.Keys) { $v = $nodo[$k]; if ($v -is [System.Collections.IDictionary] -or $v -is [System.Collections.IList]) { Collect-Fields $v $acc } }
    } elseif ($nodo -is [System.Collections.IList]) {
        foreach ($it in $nodo) { if ($it -is [System.Collections.IDictionary] -or $it -is [System.Collections.IList]) { Collect-Fields $it $acc } }
    }
}

$codigos = (docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT codigo FROM form_definitions WHERE tenant_id='$TenantId' AND tipo='CONSENTIMIENTO' ORDER BY codigo;") -split "`n"

$totalPas = 0
foreach ($cod in $codigos) {
    $cod = $cod.Trim(); if (-not $cod) { continue }
    $rawS = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $rawP = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT coalesce(prefill_routes_json::text,'null') FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $schema = $rawS | ConvertFrom-Json -AsHashtable
    $fields = New-Object System.Collections.ArrayList; Collect-Fields $schema $fields
    Write-Host ""
    Write-Host ("========== {0} ==========" -f $cod) -ForegroundColor Cyan

    Write-Host ("  Secciones ({0}):" -f $schema.children.Count)
    $hayAuto = $false; $huellasSecciones = 0
    for ($i=0; $i -lt $schema.children.Count; $i++) {
        $s = $schema.children[$i]
        $hijos = if ($s.children) { $s.children.Count } else { 0 }
        Write-Host ("    [{0}] {1}  (hijos={2})" -f $i, $s.label, $hijos)
        if ([string]$s.label -eq "Datos del Paciente (auto-llenado)") { $hayAuto = $true }
        if ([string]$s.label -eq "HUELLA") { $huellasSecciones++ }
    }
    $huellasCampo = ($fields | Where-Object { $_.name -match "huella" }).Count
    $consentFields = ($fields | Where-Object { $_.name -match "_consent$" }).Count
    Write-Host ("  Total campos con 'name': {0}" -f $fields.Count)
    Write-Host ("    - Campos *_consent (datos auto): {0}" -f $consentFields) -ForegroundColor $(if ($consentFields -ge 5) { "Green" } else { "Yellow" })
    Write-Host ("    - Cajas HUELLA (field): {0}" -f $huellasCampo) -ForegroundColor Green
    Write-Host ("    - Secciones HUELLA: {0}" -f $huellasSecciones)
    Write-Host ("  Seccion 'Datos del Paciente (auto-llenado)': {0}" -f $(if ($hayAuto) { "SI" } else { "NO" })) -ForegroundColor $(if ($hayAuto) { "Green" } else { "Yellow" })

    # Prefill
    if ($rawP -eq "null" -or -not $rawP) {
        Write-Host "  prefill_routes_json: NULL" -ForegroundColor Yellow
        continue
    }
    $prefill = $rawP | ConvertFrom-Json -AsHashtable
    $mapsOk = 0; $mapsHuerf = 0
    $names = New-Object System.Collections.Generic.HashSet[string]
    foreach ($f in $fields) { [void]$names.Add($f.name) }
    foreach ($r in $prefill.routes) {
        foreach ($m in $r.mappings) {
            $t = [string]$m.target
            if (-not $t) { continue }
            if ($names.Contains($t)) { $mapsOk++ } else { $mapsHuerf++ }
        }
    }
    Write-Host ("  prefill_routes: rutas={0}  mappings OK={1}  huerfanos={2}" -f $prefill.routes.Count, $mapsOk, $mapsHuerf) -ForegroundColor $(if ($mapsHuerf -eq 0) { "Green" } else { "Red" })
    if ($mapsHuerf -eq 0 -and $hayAuto -and $huellasCampo -ge 1) { $totalPas++; Write-Host "  ESTADO: OK" -ForegroundColor Green }
    else { Write-Host "  ESTADO: revisar" -ForegroundColor Yellow }
}
Write-Host ""
Write-Host ("Consentimientos que pasan las 3 validaciones (auto + huella + prefill OK): {0}/{1}" -f $totalPas, ($codigos | Where-Object { $_.Trim() }).Count) -ForegroundColor Cyan
