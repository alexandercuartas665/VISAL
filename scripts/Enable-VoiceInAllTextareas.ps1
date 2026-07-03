# Enable-VoiceInAllTextareas.ps1
# Walk recursivo por schema_json de CADA form_definition del tenant.
# Para cada campo con fieldType='textarea', setea enableVoice=true.
# NO modifica ninguna otra propiedad.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev",
    [switch]$DryRun
)
$ErrorActionPreference = "Stop"

# Contadores globales (usar script scope para el recursivo)
$script:totalTextareas = 0
$script:totalYaHabilitados = 0
$script:totalActualizados = 0

function Walk-EnableVoice {
    param($nodo)
    if ($nodo -is [System.Collections.IDictionary]) {
        if ($nodo["type"] -eq "field" -and $nodo["fieldType"] -eq "textarea") {
            $script:totalTextareas++
            if ($nodo["enableVoice"] -eq $true) {
                $script:totalYaHabilitados++
            } else {
                $nodo["enableVoice"] = $true
                $script:totalActualizados++
            }
        }
        foreach ($k in $nodo.Keys) {
            $v = $nodo[$k]
            if ($v -is [System.Collections.IDictionary] -or $v -is [System.Collections.IList]) { Walk-EnableVoice $v }
        }
    } elseif ($nodo -is [System.Collections.IList]) {
        foreach ($it in $nodo) {
            if ($it -is [System.Collections.IDictionary] -or $it -is [System.Collections.IList]) { Walk-EnableVoice $it }
        }
    }
}

$codigos = (docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT codigo FROM form_definitions WHERE tenant_id='$TenantId' ORDER BY codigo;") -split "`n"

$fmsCambiados = 0; $fmsSinTA = 0; $fmsYaOk = 0
foreach ($cod in $codigos) {
    $cod = $cod.Trim(); if (-not $cod) { continue }
    $raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    if (-not $raw) { continue }
    try { $schema = $raw | ConvertFrom-Json -AsHashtable } catch { Write-Host "  $cod : schema ilegible" -ForegroundColor Red; continue }

    $preTotal = $script:totalTextareas
    $preAct = $script:totalActualizados
    Walk-EnableVoice $schema
    $ta = $script:totalTextareas - $preTotal
    $act = $script:totalActualizados - $preAct

    if ($ta -eq 0) { $fmsSinTA++; continue }
    if ($act -eq 0) { $fmsYaOk++; Write-Host ("  {0,-14} ({1} textareas, ya OK)" -f $cod, $ta) -ForegroundColor DarkGray; continue }
    Write-Host ("  {0,-14} textareas={1}  actualizados={2}" -f $cod, $ta, $act) -ForegroundColor Green
    $fmsCambiados++

    if ($DryRun) { continue }

    $json = ($schema | ConvertTo-Json -Depth 30 -Compress)
    $jsonSql = $json.Replace("'","''")
    $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
    $sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $tmp = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
    try {
        $copy = "/tmp/visal_voice_$([Guid]::NewGuid().ToString('N')).sql"
        docker cp $tmp "${PgContainer}:${copy}" | Out-Null
        $env:MSYS_NO_PATHCONV = "1"
        $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
        $exit = $LASTEXITCODE
        docker exec $PgContainer rm $copy 2>$null | Out-Null
        $env:MSYS_NO_PATHCONV = $null
        if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
    } finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
}

Write-Host ""
Write-Host ("=========== RESUMEN ===========") -ForegroundColor Cyan
Write-Host ("  Formularios con textareas actualizados: {0}" -f $fmsCambiados) -ForegroundColor Green
Write-Host ("  Formularios que ya tenian todo OK:      {0}" -f $fmsYaOk) -ForegroundColor DarkGray
Write-Host ("  Formularios sin textareas:              {0}" -f $fmsSinTA) -ForegroundColor DarkGray
Write-Host ("  Textareas totales:                       {0}" -f $script:totalTextareas)
Write-Host ("  Textareas ya con enableVoice=true:       {0}" -f $script:totalYaHabilitados)
Write-Host ("  Textareas actualizados a enableVoice:    {0}" -f $script:totalActualizados) -ForegroundColor Green
