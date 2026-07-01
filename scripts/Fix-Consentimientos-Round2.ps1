# Fix-Consentimientos-Round2.ps1
# Segunda pasada sobre consentimientos:
#   A) PP-FO-18/20/22/88: quitar la seccion vieja "DATOS DE IDENTIFICACION"
#      que quedo duplicada al lado de "Datos del Paciente (auto-llenado)".
#   B) PP-FO-23/24/37/37-PAD/66/69: insertar seccion "Datos del Paciente
#      (auto-llenado)" al principio + setear prefill_routes_json base.
#   C) PP-FO-17: dedup mappings duplicados en rutas Firmas/HistoriaMedica/Sistema.
#
# NO TOCA HC-FO-08. Backups previos ya realizados a /tmp/consent_backups/.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

function New-DatosSection {
    return @{
        id = "auto-datos-paciente"
        type = "section"
        label = "Datos del Paciente (auto-llenado)"
        children = @(
            @{ id = newId; type = "field"; fieldType = "text";   label = "Nombre completo";  name = "nombre_paciente_consent";   widthColumns = 12 },
            @{ id = newId; type = "field"; fieldType = "text";   label = "Tipo doc";         name = "tipo_documento_consent";    widthColumns = 12 },
            @{ id = newId; type = "field"; fieldType = "text";   label = "Numero documento"; name = "numero_documento_consent";  widthColumns = 12 },
            @{ id = newId; type = "field"; fieldType = "number"; label = "Edad";             name = "edad_consent";              widthColumns = 12 },
            @{ id = newId; type = "field"; fieldType = "date";   label = "Fecha atencion";   name = "fecha_atencion_consent";    widthColumns = 12 }
        )
    }
}

$prefillBase = @{
    routes = @(
        @{
            id = "auto-rpac"; name = "Paciente"; sourceModule = "paciente"
            mappings = @(
                @{ source = "nombreCompleto";  target = "nombre_paciente_consent" },
                @{ source = "tipoDocumento";   target = "tipo_documento_consent" },
                @{ source = "numeroDocumento"; target = "numero_documento_consent" },
                @{ source = "edad";            target = "edad_consent" }
            )
        },
        @{
            id = "auto-rsis"; name = "Sistema"; sourceModule = "sistema"
            mappings = @( @{ source = "fechaActual"; target = "fecha_atencion_consent" } )
        }
    )
}

function Update-Row {
    param([string]$Codigo, $Schema, $Prefill)
    $json = ($Schema | ConvertTo-Json -Depth 30 -Compress)
    $jsonSql = $json.Replace("'","''")
    $prefillSql = if ($Prefill) { ($Prefill | ConvertTo-Json -Depth 20 -Compress).Replace("'","''") } else { $null }
    $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")

    if ($prefillSql) {
        $sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, prefill_routes_json='$prefillSql'::jsonb, updated_at='$now' WHERE codigo='$Codigo' AND tenant_id='$TenantId';"
    } else {
        $sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='$Codigo' AND tenant_id='$TenantId';"
    }

    $tmp = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
    try {
        $copy = "/tmp/visal_r2_$([Guid]::NewGuid().ToString('N')).sql"
        docker cp $tmp "${PgContainer}:${copy}" | Out-Null
        $env:MSYS_NO_PATHCONV = "1"
        $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
        $exit = $LASTEXITCODE
        docker exec $PgContainer rm $copy 2>$null | Out-Null
        $env:MSYS_NO_PATHCONV = $null
        if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
    } finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
}

function Get-Schema {
    param([string]$Codigo)
    $raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$Codigo' AND tenant_id='$TenantId';"
    return ($raw | ConvertFrom-Json -AsHashtable)
}

# ============ FASE A: quitar seccion "DATOS DE IDENTIFICACION" duplicada ============
Write-Host "" ; Write-Host "###### FASE A: eliminar 'DATOS DE IDENTIFICACION' duplicada ######" -ForegroundColor Cyan
foreach ($cod in @("PP-FO-18","PP-FO-20","PP-FO-22","PP-FO-88")) {
    Write-Host ""
    Write-Host "== $cod ==" -ForegroundColor Cyan
    $schema = Get-Schema $cod
    $before = $schema.children.Count
    $filtradas = @()
    foreach ($sec in $schema.children) {
        $lbl = [string]$sec.label
        $lblUp = if ($lbl) { $lbl.ToUpper() } else { "" }
        # Eliminar solo si el label es EXACTAMENTE la vieja
        if ($lblUp -match "^DATOS DE IDENTIFICACI") { continue }
        $filtradas += $sec
    }
    $schema.children = $filtradas
    $after = $schema.children.Count
    if ($before -eq $after) { Write-Host "  sin cambios" -ForegroundColor Yellow; continue }
    Write-Host "  secciones: $before -> $after (quitada 1 DATOS DE IDENTIFICACION duplicada)" -ForegroundColor Green
    Update-Row -Codigo $cod -Schema $schema -Prefill $null
    Write-Host "  OK" -ForegroundColor Green
}

# ============ FASE B: agregar seccion + prefill a los 5 sin prefill ============
Write-Host "" ; Write-Host "###### FASE B: agregar Datos + prefill a consentimientos sin ellos ######" -ForegroundColor Cyan
foreach ($cod in @("PP-FO-23","PP-FO-24","PP-FO-37","PP-FO-37-PAD","PP-FO-66","PP-FO-69")) {
    Write-Host ""
    Write-Host "== $cod ==" -ForegroundColor Cyan
    $schema = Get-Schema $cod
    # Insertar la nueva seccion como primera hijo
    $nueva = New-DatosSection
    $rest = @($schema.children)
    $schema.children = @($nueva) + $rest
    Write-Host ("  secciones: {0} -> {1} (agregada 'Datos del Paciente' arriba)" -f $rest.Count, $schema.children.Count) -ForegroundColor Green
    Update-Row -Codigo $cod -Schema $schema -Prefill $prefillBase
    Write-Host "  OK" -ForegroundColor Green
}

# ============ FASE C: dedup mappings en PP-FO-17 ============
Write-Host "" ; Write-Host "###### FASE C: dedup mappings en PP-FO-17 ######" -ForegroundColor Cyan
$cod = "PP-FO-17"
$rawP = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT prefill_routes_json::text FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
$prefill = $rawP | ConvertFrom-Json -AsHashtable
$totalAntes = 0; $totalDespues = 0
foreach ($r in $prefill.routes) {
    $totalAntes += $r.mappings.Count
    $vistos = New-Object System.Collections.Generic.HashSet[string]
    $unicos = @()
    foreach ($m in $r.mappings) {
        $clave = "{0}=>{1}" -f $m.source, $m.target
        if ($vistos.Add($clave)) { $unicos += $m }
    }
    $r.mappings = $unicos
    $totalDespues += $unicos.Count
}
Write-Host ("  mappings totales: {0} -> {1} (dedup)" -f $totalAntes, $totalDespues) -ForegroundColor Green
$schema17 = Get-Schema $cod
Update-Row -Codigo $cod -Schema $schema17 -Prefill $prefill
Write-Host "  OK" -ForegroundColor Green

Write-Host ""
Write-Host "########## FIN ##########" -ForegroundColor Cyan
