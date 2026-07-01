# Audit-Huellas.ps1
# Audita en TODOS los consentimientos donde aparece la cadena "HUELLAHUELLA"
# (en label de seccion, en content de text/paragraph, en label de field) para
# saber cuantos sitios hay que sanear.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"

function Walk-FindHuella {
    param($nodo, [string]$path, [System.Collections.ArrayList]$hits)
    if ($nodo -is [System.Collections.IDictionary]) {
        # Label de seccion o de field
        $lbl = [string]$nodo["label"]
        if ($lbl -and $lbl -match "HUELLA") {
            [void]$hits.Add([pscustomobject]@{ Kind = "label"; Path = $path; Value = $lbl; NodeType = [string]$nodo["type"] })
        }
        # Content de text
        $ct = [string]$nodo["content"]
        if ($ct -and $ct -match "HUELLA") {
            [void]$hits.Add([pscustomobject]@{ Kind = "content"; Path = $path; Value = $ct; NodeType = [string]$nodo["type"] })
        }
        foreach ($k in $nodo.Keys) {
            $v = $nodo[$k]
            if ($v -is [System.Collections.IDictionary] -or $v -is [System.Collections.IList]) { Walk-FindHuella $v "$path/$k" $hits }
        }
    } elseif ($nodo -is [System.Collections.IList]) {
        for ($i=0; $i -lt $nodo.Count; $i++) {
            $v = $nodo[$i]
            if ($v -is [System.Collections.IDictionary] -or $v -is [System.Collections.IList]) { Walk-FindHuella $v "$path[$i]" $hits }
        }
    }
}

$codigos = (docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT codigo FROM form_definitions WHERE tenant_id='$TenantId' AND tipo='CONSENTIMIENTO' ORDER BY codigo;") -split "`n"

$totalHits = 0
foreach ($cod in $codigos) {
    $cod = $cod.Trim(); if (-not $cod) { continue }
    $raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    try { $schema = $raw | ConvertFrom-Json -AsHashtable } catch { continue }
    $hits = New-Object System.Collections.ArrayList
    Walk-FindHuella $schema "" $hits
    if ($hits.Count -eq 0) { continue }
    Write-Host ""
    Write-Host ("=== {0}: {1} hit(s) con 'HUELLA' ===" -f $cod, $hits.Count) -ForegroundColor Cyan
    foreach ($h in $hits) {
        $preview = $h.Value
        if ($preview.Length -gt 100) { $preview = $preview.Substring(0,100) + "..." }
        $tag = if ($h.Value -match "HUELLAHUELLA") { "[HUELLAHUELLA]" } else { "[huella]" }
        Write-Host ("    {0} {1} ({2}, {3}): {4}" -f $tag, $h.Kind, $h.NodeType, $h.Path, $preview)
    }
    $totalHits += $hits.Count
}
Write-Host ""
Write-Host ("Total hits: {0}" -f $totalHits) -ForegroundColor Cyan
