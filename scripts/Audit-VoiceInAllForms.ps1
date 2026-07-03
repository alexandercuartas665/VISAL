# Audit-VoiceInAllForms.ps1
# READ-ONLY. Recorre TODOS los form_definitions del tenant.
# Para cada uno, cuenta textareas recursivamente y cuantos tienen
# enableVoice=true. Reporta cualquier formulario con textareas sin voice.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"

$script:cur = @{ total=0; conVoice=0; sinVoice=@() }

function Walk-Count {
    param($nodo)
    if ($nodo -is [System.Collections.IDictionary]) {
        if ($nodo["type"] -eq "field" -and $nodo["fieldType"] -eq "textarea") {
            $script:cur.total++
            if ($nodo["enableVoice"] -eq $true) {
                $script:cur.conVoice++
            } else {
                $script:cur.sinVoice += [string]$nodo["name"]
            }
        }
        foreach ($k in $nodo.Keys) {
            $v = $nodo[$k]
            if ($v -is [System.Collections.IDictionary] -or $v -is [System.Collections.IList]) { Walk-Count $v }
        }
    } elseif ($nodo -is [System.Collections.IList]) {
        foreach ($it in $nodo) {
            if ($it -is [System.Collections.IDictionary] -or $it -is [System.Collections.IList]) { Walk-Count $it }
        }
    }
}

$codigos = (docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT codigo FROM form_definitions WHERE tenant_id='$TenantId' ORDER BY codigo;") -split "`n"

$okForms = 0; $sinTAForms = 0; $problemForms = @()
$totalTAs = 0; $totalConVoice = 0
foreach ($cod in $codigos) {
    $cod = $cod.Trim(); if (-not $cod) { continue }
    $raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    if (-not $raw) { continue }
    try { $schema = $raw | ConvertFrom-Json -AsHashtable } catch { Write-Host "  $cod : ilegible" -ForegroundColor Red; continue }
    $script:cur = @{ total=0; conVoice=0; sinVoice=@() }
    Walk-Count $schema
    $totalTAs += $script:cur.total
    $totalConVoice += $script:cur.conVoice
    if ($script:cur.total -eq 0) { $sinTAForms++; continue }
    if ($script:cur.sinVoice.Count -eq 0) {
        Write-Host ("  {0,-14} {1}/{1} ✓" -f $cod, $script:cur.total) -ForegroundColor Green
        $okForms++
    } else {
        Write-Host ("  {0,-14} {1}/{2} - FALTAN: {3}" -f $cod, $script:cur.conVoice, $script:cur.total, ($script:cur.sinVoice -join ", ")) -ForegroundColor Red
        $problemForms += $cod
    }
}
Write-Host ""
Write-Host ("=========== RESUMEN AUDITORIA ===========") -ForegroundColor Cyan
Write-Host ("  Formularios con TODOS los textareas con enableVoice: {0}" -f $okForms) -ForegroundColor Green
Write-Host ("  Formularios sin textareas:                            {0}" -f $sinTAForms) -ForegroundColor DarkGray
Write-Host ("  Formularios con textareas SIN voice (problematicos):  {0}" -f $problemForms.Count) -ForegroundColor $(if ($problemForms.Count -gt 0) { "Red" } else { "Green" })
if ($problemForms.Count -gt 0) {
    Write-Host "  Detalle:"
    foreach ($p in $problemForms) { Write-Host "    - $p" -ForegroundColor Red }
}
Write-Host ("  Textareas totales:                                    {0}" -f $totalTAs)
Write-Host ("  Textareas con enableVoice=true:                        {0}" -f $totalConVoice) -ForegroundColor Green
Write-Host ("  Cobertura:                                             {0:P1}" -f ($(if ($totalTAs -gt 0) { $totalConVoice / $totalTAs } else { 1 }))) -ForegroundColor $(if ($totalConVoice -eq $totalTAs) { "Green" } else { "Yellow" })
