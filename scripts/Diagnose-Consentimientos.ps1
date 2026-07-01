# Diagnose-Consentimientos.ps1
# READ-ONLY. Compara el estado ACTUAL de cada consentimiento contra:
#   1) El backup en disco /tmp/consent_backups/*.schema.json (tomado hoy)
#   2) El snapshot MAS ANTIGUO en form_definition_snapshots (historico)
# Reporta: numero de secciones, numero de campos con name, y campos que
# aparecen en backup/snapshot pero YA NO estan en el actual (FALTANTES).

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"

function Collect-Names {
    param($nodo, [System.Collections.Generic.HashSet[string]]$acc)
    if ($nodo -is [System.Collections.IDictionary]) {
        if ($nodo["type"] -eq "field" -and $nodo["name"]) { [void]$acc.Add([string]$nodo["name"]) }
        if ($nodo["columns"]) { foreach ($col in $nodo["columns"]) { if ($col["name"]) { [void]$acc.Add([string]$col["name"]) } } }
        foreach ($k in $nodo.Keys) {
            $v = $nodo[$k]
            if ($v -is [System.Collections.IDictionary] -or $v -is [System.Collections.IList]) { Collect-Names $v $acc }
        }
    } elseif ($nodo -is [System.Collections.IList]) {
        foreach ($it in $nodo) { if ($it -is [System.Collections.IDictionary] -or $it -is [System.Collections.IList]) { Collect-Names $it $acc } }
    }
}

function Count-Sections { param($schema) if ($schema.children) { return $schema.children.Count } else { return 0 } }
function Load-Schema { param($json) try { return $json | ConvertFrom-Json -AsHashtable } catch { return $null } }

$codigos = (docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT codigo FROM form_definitions WHERE tenant_id='$TenantId' AND tipo='CONSENTIMIENTO' ORDER BY codigo;") -split "`n"

$rotos = @()
foreach ($cod in $codigos) {
    $cod = $cod.Trim(); if (-not $cod) { continue }
    Write-Host ""
    Write-Host ("=== {0} ===" -f $cod) -ForegroundColor Cyan

    # Actual
    $rawA = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $A = Load-Schema $rawA
    if (-not $A) { Write-Host "  ACTUAL: ilegible" -ForegroundColor Red; $rotos += $cod; continue }
    $nA = New-Object System.Collections.Generic.HashSet[string]; Collect-Names $A $nA
    $secA = Count-Sections $A
    Write-Host ("  ACTUAL:   secciones={0}  campos-name={1}  bytes={2}" -f $secA, $nA.Count, $rawA.Length)

    # Backup local
    $bkpPath = "/tmp/consent_backups/${cod}.schema.json"
    $bkpWin = $bkpPath.Replace("/tmp/","C:/Users/acuartas/AppData/Local/Temp/")
    if (Test-Path $bkpWin) {
        $rawB = (Get-Content $bkpWin -Raw).Trim()
        $B = Load-Schema $rawB
        if ($B) {
            $nB = New-Object System.Collections.Generic.HashSet[string]; Collect-Names $B $nB
            $secB = Count-Sections $B
            $faltan = @()
            foreach ($n in $nB) { if (-not $nA.Contains($n)) { $faltan += $n } }
            $col = if ($faltan.Count -gt 0 -or $secA -lt $secB) { "Yellow" } else { "Green" }
            Write-Host ("  BACKUP:   secciones={0}  campos-name={1}  bytes={2}" -f $secB, $nB.Count, $rawB.Length) -ForegroundColor $col
            if ($faltan.Count -gt 0) {
                Write-Host ("    FALTANTES vs backup ({0}): {1}" -f $faltan.Count, ($faltan -join ", ")) -ForegroundColor Red
                $rotos += $cod
            }
            if ($secA -lt $secB) {
                Write-Host ("    SECCIONES perdidas: {0} -> {1}" -f $secB, $secA) -ForegroundColor Red
                if ($rotos -notcontains $cod) { $rotos += $cod }
            }
        } else { Write-Host "  BACKUP: ilegible" -ForegroundColor Red }
    } else { Write-Host "  BACKUP: no existe archivo" -ForegroundColor Yellow }

    # Snapshot mas antiguo (historico)
    $rawS = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definition_snapshots WHERE codigo='$cod' AND tenant_id='$TenantId' ORDER BY snapshot_at ASC LIMIT 1;"
    if ($rawS) {
        $S = Load-Schema $rawS
        if ($S) {
            $nS = New-Object System.Collections.Generic.HashSet[string]; Collect-Names $S $nS
            $secS = Count-Sections $S
            $faltanS = @()
            foreach ($n in $nS) { if (-not $nA.Contains($n)) { $faltanS += $n } }
            $col = if ($faltanS.Count -gt 0) { "Yellow" } else { "Green" }
            Write-Host ("  SNAPSHOT: secciones={0}  campos-name={1}  bytes={2}  (mas antiguo)" -f $secS, $nS.Count, $rawS.Length) -ForegroundColor $col
            if ($faltanS.Count -gt 0) {
                Write-Host ("    FALTANTES vs snapshot antiguo ({0}): {1}" -f $faltanS.Count, ($faltanS -join ", ")) -ForegroundColor Yellow
            }
        }
    } else { Write-Host "  SNAPSHOT: sin historico" -ForegroundColor Yellow }
}

Write-Host ""
Write-Host ("=========== DIAGNOSTICO ===========") -ForegroundColor Cyan
if ($rotos.Count -eq 0) {
    Write-Host "  Todos los consentimientos preservan los campos del ultimo backup." -ForegroundColor Green
} else {
    Write-Host ("  Consentimientos con campos FALTANTES vs backup ({0}):" -f $rotos.Count) -ForegroundColor Red
    foreach ($r in $rotos) { Write-Host ("    - {0}" -f $r) -ForegroundColor Red }
}
