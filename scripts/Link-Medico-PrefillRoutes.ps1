# Link-Medico-PrefillRoutes.ps1
# Agrega/actualiza mappings de prefill para que MEDICO_NOMBRE y MEDICO_FIRMA
# se llenen SOLOS al abrir la HC con datos del DOCTOR LOGUEADO:
#   - sistema.usuarioNombre  -> medico_nombre
#   - firmaProfesional.url   -> medico_firma
#
# Estrategia: aditiva. NO borra las rutas existentes. Si una ruta 'sistema' o
# 'firmaProfesional' ya existe, agrega el mapping (si aun no esta). Si no,
# crea la ruta con ese mapping.
#
# medico_doc y medico_reg NO se pueden enlazar hoy porque el catalogo tiene
# sourceModule "profesional" pero NO existe helper de runtime que lo aplique.
# Requiere agregar ProfesionalPrefillHelper.cs en el server — fuera de scope
# de este script.
#
# NO TOCA HC-FO-08 (regla del user).

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

$codigos = @("HC-FO-10","HC-FO-10a","HC-FO-11","HC-FO-12","HC-FO-13","HC-FO-14",
             "HC-FO-15","HC-FO-16","HC-FO-18","HC-FO-19","HC-FO-20","HC-FO-21",
             "HC-FO-22","HC-FO-25")
if ($env:VISAL_ONLY_CODIGOS) { $codigos = $env:VISAL_ONLY_CODIGOS -split "," }

function Ensure-Route {
    param($routes, [string]$sourceModule, [string]$name, [string]$source, [string]$target)
    $ruta = $null
    foreach ($r in $routes) {
        if ([string]$r["sourceModule"] -eq $sourceModule) { $ruta = $r; break }
    }
    if ($null -eq $ruta) {
        $ruta = @{
            id = newId; name = $name; sourceModule = $sourceModule
            mappings = @( @{ source = $source; target = $target } )
        }
        $out = New-Object System.Collections.ArrayList
        foreach ($r in $routes) { [void]$out.Add($r) }
        [void]$out.Add($ruta)
        return $out.ToArray()
    }
    # Ya existe la ruta: chequea si el mapping ya esta
    $tiene = $false
    foreach ($m in $ruta.mappings) {
        if ([string]$m.source -eq $source -and [string]$m.target -eq $target) { $tiene = $true; break }
    }
    if (-not $tiene) {
        $ruta.mappings = @($ruta.mappings) + @{ source = $source; target = $target }
    }
    return $routes
}

$cambiados = 0
foreach ($cod in $codigos) {
    Write-Host ""
    Write-Host ("=== {0} ===" -f $cod) -ForegroundColor Cyan
    $rawP = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT coalesce(prefill_routes_json::text,'null') FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    if (-not $rawP -or $rawP -eq "null") {
        $prefill = @{ routes = @() }
    } else {
        $prefill = $rawP | ConvertFrom-Json -AsHashtable
        if (-not $prefill.routes) { $prefill.routes = @() }
    }

    $antes = ($prefill.routes | ForEach-Object { $_.mappings.Count } | Measure-Object -Sum).Sum

    $prefill.routes = Ensure-Route $prefill.routes "sistema"          "Sistema"                "usuarioNombre" "medico_nombre"
    $prefill.routes = Ensure-Route $prefill.routes "firmaProfesional" "Firma del Profesional" "url"           "medico_firma"

    $despues = ($prefill.routes | ForEach-Object { $_.mappings.Count } | Measure-Object -Sum).Sum
    Write-Host ("  rutas ahora: {0}  mappings totales: {1} -> {2}" -f $prefill.routes.Count, $antes, $despues) -ForegroundColor Green

    if ($despues -eq $antes) { Write-Host "  (sin cambios: ya tenia ambos mappings)" -ForegroundColor Yellow; continue }
    $cambiados++

    if ($DryRun) { Write-Host "  DRY-RUN: no persiste" -ForegroundColor Yellow; continue }

    $json = ($prefill | ConvertTo-Json -Depth 20 -Compress)
    $jsonSql = $json.Replace("'","''")
    $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
    $sql = "UPDATE form_definitions SET prefill_routes_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $tmp = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
    try {
        $copy = "/tmp/visal_link_$([Guid]::NewGuid().ToString('N')).sql"
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
Write-Host ("Total cambiados: {0}/{1}" -f $cambiados, $codigos.Count) -ForegroundColor Cyan
