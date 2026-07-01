# Standardize-Consentimientos-DatosPaciente.ps1
# Estandariza la seccion "Datos del Paciente" en TODOS los consentimientos
# que la tienen (o su equivalente antiguo "DATOS DE IDENTIFICACION"),
# usando la misma estructura que PP-FO-17. Configura prefill_routes_json
# con las rutas Paciente + Sistema para que se llene solo al abrir.
#
# Consentimientos con estructura DIFERENTE quedan fuera y se listan al
# final para revision manual. NO TOCA PP-FO-17 (es la plantilla) ni
# HC-FO-08.

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

# 1) Nueva seccion "Datos del Paciente (auto-llenado)" - copia del PP-FO-17
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

# 2) prefill_routes_json base (Paciente + Sistema)
$prefillObj = @{
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
            mappings = @(
                @{ source = "fechaActual"; target = "fecha_atencion_consent" }
            )
        }
    )
}
$prefillJson = ($prefillObj | ConvertTo-Json -Depth 20 -Compress)

# 3) Consentimientos objetivo
$candidatos = @("PP-FO-18","PP-FO-20","PP-FO-22","PP-FO-32","PP-FO-81",
                "PP-FO-88","PP-FO-89","PP-FO-90","PP-FO-96","PP-FO-112","PP-FO-113")

$aplicados = 0; $errores = @()
foreach ($codigo in $candidatos) {
    Write-Host ""
    Write-Host "== $codigo ==" -ForegroundColor Cyan
    try {
        $raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$codigo' AND tenant_id='$TenantId';"
        if (-not $raw) { throw "no existe" }
        $schema = $raw | ConvertFrom-Json -AsHashtable

        # Localizar seccion a reemplazar
        $idxTarget = -1
        for ($i=0; $i -lt $schema.children.Count; $i++) {
            $sec = $schema.children[$i]
            if ($sec.type -ne "section") { continue }
            $lbl = [string]$sec.label
            $lblUp = if ($lbl) { $lbl.ToUpper() } else { "" }
            if ($lblUp -match "DATOS DEL PACIENTE|DATOS DE IDENTIFICAC") { $idxTarget = $i; break }
            $hit = $false
            foreach ($c in $sec.children) {
                if ($c.type -eq "text") {
                    $ct = ([string]$c.content).ToUpper()
                    if ($ct -match "NOMBRE COMPLETO DEL PACIENTE") { $hit = $true; break }
                }
            }
            if ($hit) { $idxTarget = $i; break }
        }
        if ($idxTarget -lt 0) { throw "sin seccion de datos" }

        $lblOrig = [string]$schema.children[$idxTarget].label
        $schema.children[$idxTarget] = (New-DatosSection)
        Write-Host ("    seccion [{0}] '{1}' -> 'Datos del Paciente (auto-llenado)'" -f $idxTarget, $lblOrig) -ForegroundColor Green

        if ($DryRun) { Write-Host "    (dry-run: no persiste)" -ForegroundColor Yellow; continue }

        $json = ($schema | ConvertTo-Json -Depth 30 -Compress)
        $jsonSql = $json.Replace("'","''")
        $prefillSql = $prefillJson.Replace("'","''")
        $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
        $sql = @"
UPDATE form_definitions
SET schema_json = '$jsonSql'::jsonb,
    prefill_routes_json = '$prefillSql'::jsonb,
    updated_at = '$now'
WHERE codigo='$codigo' AND tenant_id='$TenantId';
"@
        $tmp = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
        try {
            $copy = "/tmp/visal_std_$([Guid]::NewGuid().ToString('N')).sql"
            docker cp $tmp "${PgContainer}:${copy}" | Out-Null
            $env:MSYS_NO_PATHCONV = "1"
            $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
            $exit = $LASTEXITCODE
            docker exec $PgContainer rm $copy 2>$null | Out-Null
            $env:MSYS_NO_PATHCONV = $null
            if ($exit -ne 0) { throw ("psql fallo ({0}): {1}" -f $exit, ($r -join ' | ')) }
        } finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

        $aplicados++
        Write-Host "    OK" -ForegroundColor Green
    } catch {
        $errores += "$codigo -> $_"
        Write-Host ("    ERROR: {0}" -f $_) -ForegroundColor Red
    }
}

Write-Host ""
Write-Host ("========== RESUMEN ==========") -ForegroundColor Cyan
Write-Host ("  Aplicados: {0}/{1}" -f $aplicados, $candidatos.Count) -ForegroundColor Green
if ($errores.Count -gt 0) {
    Write-Host "  Errores:" -ForegroundColor Red
    foreach ($e in $errores) { Write-Host "    - $e" -ForegroundColor Red }
}
