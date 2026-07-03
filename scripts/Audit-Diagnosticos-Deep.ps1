# Audit-Diagnosticos-Deep.ps1
# Busca en TODO el schema (recursivo) cualquier nodo cuyo label/name/content
# contenga "DIAGN", "CIE-", "CIE " o palabras clave de diagnostico.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"

function Walk-Find {
    param($nodo, [string]$path, [System.Collections.ArrayList]$hits)
    if ($nodo -is [System.Collections.IDictionary]) {
        $lbl = [string]$nodo["label"]
        $nm  = [string]$nodo["name"]
        $ct  = [string]$nodo["content"]
        $joined = "$lbl|$nm|$ct".ToUpper()
        if ($joined -match "DIAGN|\bCIE\b|IMPRESI" ) {
            $t = [string]$nodo["type"]; $ft = [string]$nodo["fieldType"]
            [void]$hits.Add([pscustomobject]@{ Path=$path; type=$t; ft=$ft; label=$lbl; name=$nm; content=($ct.Substring(0,[Math]::Min(60,$ct.Length))) })
        }
        foreach ($k in $nodo.Keys) {
            $v = $nodo[$k]
            if ($v -is [System.Collections.IDictionary] -or $v -is [System.Collections.IList]) { Walk-Find $v "$path/$k" $hits }
        }
    } elseif ($nodo -is [System.Collections.IList]) {
        for ($i=0; $i -lt $nodo.Count; $i++) {
            $v = $nodo[$i]
            if ($v -is [System.Collections.IDictionary] -or $v -is [System.Collections.IList]) { Walk-Find $v "$path[$i]" $hits }
        }
    }
}

$codigos = @("HC-FO-18","HC-FO-19","HC-FO-20","HC-FO-21","HC-FO-22","HC-FO-25")
foreach ($cod in $codigos) {
    Write-Host ""
    Write-Host ("========== {0} ==========" -f $cod) -ForegroundColor Cyan
    $raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $schema = $raw | ConvertFrom-Json -AsHashtable
    $hits = New-Object System.Collections.ArrayList
    Walk-Find $schema "" $hits
    if ($hits.Count -eq 0) { Write-Host "  (sin hits de DIAGN/CIE)"; continue }
    foreach ($h in $hits) {
        Write-Host ("    {0}: type={1} ft={2} label='{3}' name='{4}' content='{5}'" -f $h.Path, $h.type, $h.ft, $h.label, $h.name, $h.content)
    }
}
