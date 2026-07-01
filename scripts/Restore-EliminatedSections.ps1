# Restore-EliminatedSections.ps1
# Restaura las secciones "DATOS DE IDENTIFICACION" que eliminamos en
# Round2 fase A. Las copia desde el snapshot mas antiguo (form_definition_snapshots)
# y las inserta despues de la seccion "Datos del Paciente (auto-llenado)".
#
# Afecta solo: PP-FO-18 (verificar), PP-FO-20, PP-FO-22, PP-FO-88.
# NO TOCA HC-FO-08.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"

$codigos = @("PP-FO-18","PP-FO-20","PP-FO-22","PP-FO-88")

foreach ($cod in $codigos) {
    Write-Host ""
    Write-Host ("=== {0} ===" -f $cod) -ForegroundColor Cyan

    # Buscar en TODOS los snapshots una version que tenga la seccion
    # DATOS DE IDENTIFICACION. Empezar por el mas viejo.
    $rowsRaw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definition_snapshots WHERE codigo='$cod' AND tenant_id='$TenantId' ORDER BY snapshot_at ASC;"
    $lines = $rowsRaw -split "`n" | Where-Object { $_ -and $_.Trim() }

    $seccionOriginal = $null
    foreach ($jsonLine in $lines) {
        try { $sn = $jsonLine | ConvertFrom-Json -AsHashtable } catch { continue }
        foreach ($sec in $sn.children) {
            $lbl = [string]$sec["label"]
            if ($lbl -match "^DATOS DE IDENTIFICACI") {
                $seccionOriginal = $sec
                break
            }
        }
        if ($seccionOriginal) { break }
    }
    if (-not $seccionOriginal) {
        Write-Host "  ningun snapshot tiene DATOS DE IDENTIFICACION - nada que restaurar" -ForegroundColor Yellow
        continue
    }
    Write-Host ("  seccion encontrada en snapshot: label='{0}' hijos={1}" -f $seccionOriginal["label"], (($seccionOriginal["children"] | Measure-Object).Count)) -ForegroundColor Green

    # Cargar el actual
    $rawA = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $A = $rawA | ConvertFrom-Json -AsHashtable

    # Verificar que NO tenga ya una seccion DATOS DE IDENTIFICACION
    $yaTiene = $false
    foreach ($sec in $A.children) {
        if ([string]$sec["label"] -match "^DATOS DE IDENTIFICACI") { $yaTiene = $true; break }
    }
    if ($yaTiene) { Write-Host "  YA tiene DATOS DE IDENTIFICACION, no re-inserta" -ForegroundColor Yellow; continue }

    # Insertar despues de "Datos del Paciente (auto-llenado)"
    $idxAuto = -1
    for ($i=0; $i -lt $A.children.Count; $i++) {
        if ([string]$A.children[$i]["label"] -eq "Datos del Paciente (auto-llenado)") { $idxAuto = $i; break }
    }
    $head = @(); $tail = @()
    if ($idxAuto -ge 0) {
        $head = $A.children[0..$idxAuto]
        if ($idxAuto -lt ($A.children.Count - 1)) { $tail = $A.children[($idxAuto+1)..($A.children.Count-1)] }
    } else {
        # Sin auto: insertar arriba
        $tail = $A.children
    }
    $A.children = @($head) + @($seccionOriginal) + @($tail)
    Write-Host ("  seccion DATOS DE IDENTIFICACION reinsertada. Total secciones: {0}" -f $A.children.Count) -ForegroundColor Green

    # UPDATE
    $json = ($A | ConvertTo-Json -Depth 30 -Compress)
    $jsonSql = $json.Replace("'","''")
    $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
    $sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $tmp = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
    try {
        $copy = "/tmp/visal_restore_$([Guid]::NewGuid().ToString('N')).sql"
        docker cp $tmp "${PgContainer}:${copy}" | Out-Null
        $env:MSYS_NO_PATHCONV = "1"
        $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
        $exit = $LASTEXITCODE
        docker exec $PgContainer rm $copy 2>$null | Out-Null
        $env:MSYS_NO_PATHCONV = $null
        if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
    } finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
    Write-Host "  OK" -ForegroundColor Green
}
