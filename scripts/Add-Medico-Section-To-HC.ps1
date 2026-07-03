# Add-Medico-Section-To-HC.ps1
# Garantiza que TODA historia clinica (tipo=HISTORIA CLINICA) tenga como
# ULTIMA seccion la seccion "MEDICO" con 4 campos (Nombre, Documento,
# Registro, Firma), copiada tal cual la tiene HC-FO-08.
#
# - HC-FO-08 NO se toca.
# - Si una HC ya tiene una seccion "MEDICO" con los 4 nombres correctos,
#   solo se garantiza que este al FINAL (si no lo esta, se mueve al final).
# - Si no la tiene, se agrega al final.
# - Si tiene una seccion incompleta con esa label, se DEJA y se agrega
#   una nueva al final para no destruir datos del usuario. Se reporta.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev",
    [switch]$DryRun
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

$EXPECTED_NAMES = @("medico_nombre","medico_doc","medico_reg","medico_firma")

function New-MedicoSection {
    return @{
        id = "sec-medico"
        type = "section"
        label = "MEDICO"
        children = @(
            @{ id = newId; type = "field"; fieldType = "text"; label = "Nombre";    name = "medico_nombre"; widthColumns = 6; required = $true },
            @{ id = newId; type = "field"; fieldType = "text"; label = "Documento"; name = "medico_doc";    widthColumns = 3; required = $false },
            @{ id = newId; type = "field"; fieldType = "text"; label = "Registro";  name = "medico_reg";    widthColumns = 3; required = $false },
            @{ id = newId; type = "field"; fieldType = "text"; label = "Firma";     name = "medico_firma";  widthColumns = 12; required = $false }
        )
    }
}

$codigos = @("HC-FO-10","HC-FO-10a","HC-FO-11","HC-FO-12","HC-FO-13","HC-FO-14",
             "HC-FO-15","HC-FO-16","HC-FO-18","HC-FO-19","HC-FO-20","HC-FO-21",
             "HC-FO-22","HC-FO-25")

$sinCambios = @(); $agregadas = @(); $movidas = @(); $duplicadas = @()

foreach ($cod in $codigos) {
    Write-Host ""
    Write-Host ("=== {0} ===" -f $cod) -ForegroundColor Cyan
    $raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    if (-not $raw) { Write-Host "  no existe" -ForegroundColor Red; continue }
    $schema = $raw | ConvertFrom-Json -AsHashtable

    # Localizar secciones con label MEDICO (por si hay varias)
    $medicoIdx = @()
    for ($i=0; $i -lt $schema.children.Count; $i++) {
        if ([string]$schema.children[$i]["label"] -eq "MEDICO") { $medicoIdx += $i }
    }
    $lastIdx = $schema.children.Count - 1

    if ($medicoIdx.Count -eq 0) {
        # No existe: agregar
        Write-Host "  no tiene MEDICO -> agregar al final" -ForegroundColor Yellow
        if (-not $DryRun) {
            $schema.children += (New-MedicoSection)
        }
        $agregadas += $cod
    } elseif ($medicoIdx.Count -eq 1) {
        $idx = $medicoIdx[0]
        $sec = $schema.children[$idx]
        # verificar que tenga los 4 names esperados
        $namesTiene = @()
        foreach ($c in $sec["children"]) { if ($c["name"]) { $namesTiene += [string]$c["name"] } }
        $faltantes = @()
        foreach ($n in $EXPECTED_NAMES) { if ($namesTiene -notcontains $n) { $faltantes += $n } }
        if ($faltantes.Count -gt 0) {
            # Seccion existe pero incompleta: agregar una nueva al final SIN quitar la vieja
            Write-Host ("  MEDICO existe [{0}] pero le faltan: {1}. Agrego una nueva al final SIN borrar la existente." -f $idx, ($faltantes -join ", ")) -ForegroundColor Yellow
            if (-not $DryRun) {
                $schema.children += (New-MedicoSection)
            }
            $duplicadas += $cod
        } elseif ($idx -ne $lastIdx) {
            # existe completa pero no al final: mover al final
            Write-Host ("  MEDICO existe en [{0}] pero no es la ultima ({1}). Muevo al final." -f $idx, $lastIdx) -ForegroundColor Yellow
            if (-not $DryRun) {
                $medico = $schema.children[$idx]
                $nueva = @()
                for ($j=0; $j -lt $schema.children.Count; $j++) { if ($j -ne $idx) { $nueva += $schema.children[$j] } }
                $nueva += $medico
                $schema.children = $nueva
            }
            $movidas += $cod
        } else {
            Write-Host ("  MEDICO ya esta al final y completa. OK.") -ForegroundColor Green
            $sinCambios += $cod
            continue
        }
    } else {
        Write-Host ("  MEDICO aparece {0} veces (indices: {1}). NO toco, reviso." -f $medicoIdx.Count, ($medicoIdx -join ", ")) -ForegroundColor Yellow
        $duplicadas += $cod
        continue
    }

    if ($DryRun) { continue }

    # UPDATE
    $json = ($schema | ConvertTo-Json -Depth 30 -Compress)
    $jsonSql = $json.Replace("'","''")
    $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
    $sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $tmp = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
    try {
        $copy = "/tmp/visal_med_$([Guid]::NewGuid().ToString('N')).sql"
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

Write-Host ""
Write-Host ("========== RESUMEN ==========") -ForegroundColor Cyan
Write-Host ("  Sin cambios (ya estaba OK): {0}" -f $sinCambios.Count) -ForegroundColor Green
Write-Host ("  MEDICO agregada al final:   {0}" -f $agregadas.Count)
if ($agregadas.Count -gt 0) { foreach ($c in $agregadas) { Write-Host "    - $c" } }
Write-Host ("  MEDICO movida al final:     {0}" -f $movidas.Count)
if ($movidas.Count -gt 0) { foreach ($c in $movidas) { Write-Host "    - $c" } }
Write-Host ("  Casos con duplicados:       {0}" -f $duplicadas.Count) -ForegroundColor Yellow
if ($duplicadas.Count -gt 0) { foreach ($c in $duplicadas) { Write-Host "    - $c" } }
