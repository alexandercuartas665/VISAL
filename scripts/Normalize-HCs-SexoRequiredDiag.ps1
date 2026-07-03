# Normalize-HCs-SexoRequiredDiag.ps1
# Dos operaciones quirurgicas en todas las tipo=HISTORIA CLINICA:
#
# 1) Uniformar 'genero' -> 'sexo' en el campo SelectSexo (name/label) y en
#    formulas calculated que lo referencian (ej. perimetroRiesgo(perimetro, genero)
#    -> perimetroRiesgo(perimetro, sexo)).
#
# 2) Marcar required=true en las 4 columnas de la tabla 'diagnosticos'
#    (Diagnostico | Origen | Tipo | Relacion).
#
# Backup previo por HC en scratchpad/backups_normsex_req_<timestamp>/<codigo>.json

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev",
    [string]$BackupRoot  = "C:\Users\acuartas\AppData\Local\Temp\claude\C--DesarrolloIA-Visal\3a114262-030a-4135-852f-4f6e57a10abf\scratchpad"
)
$ErrorActionPreference = "Stop"

$stamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
$backupDir = Join-Path $BackupRoot ("backups_normsex_req_$stamp")
[System.IO.Directory]::CreateDirectory($backupDir) | Out-Null
Write-Host "Backups a: $backupDir" -ForegroundColor Cyan

$listado = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT codigo FROM form_definitions WHERE tipo='HISTORIA CLINICA' AND tenant_id='$TenantId' ORDER BY codigo;"
$codigos = $listado -split "`n" | Where-Object { $_ -ne "" }
Write-Host ("HCs a procesar ({0}): {1}" -f $codigos.Count, ($codigos -join ", ")) -ForegroundColor Cyan

function Set-RequiredDiagCols($tabla) {
    if ($tabla["fieldType"] -ne "table") { return $false }
    if (([string]$tabla["name"]).ToLowerInvariant() -ne "diagnosticos") { return $false }
    $changed = $false
    for ($j = 0; $j -lt $tabla.columns.Count; $j++) {
        $col = $tabla.columns[$j]
        if ($col["required"] -ne $true) {
            $col["required"] = $true
            $tabla.columns[$j] = $col
            $changed = $true
        }
    }
    return $changed
}

function Rename-GeneroToSexo($node) {
    # Recursivo. Renombra name='genero'->'sexo' y label='Genero'->'Sexo' donde encuentre.
    # Ajusta 'formula' si contiene 'genero' como argumento.
    $changed = $false
    if ($node -is [hashtable]) {
        if ($node.ContainsKey("name") -and ([string]$node["name"]) -eq "genero") {
            $node["name"] = "sexo"; $changed = $true
        }
        if ($node.ContainsKey("label") -and ([string]$node["label"]) -eq "Genero") {
            $node["label"] = "Sexo"; $changed = $true
        }
        if ($node.ContainsKey("formula") -and ($node["formula"] -is [string])) {
            $orig = [string]$node["formula"]
            # Reemplaza 'genero' como identificador completo (bordes de palabra)
            $ajustada = [Regex]::Replace($orig, "\bgenero\b", "sexo")
            if ($ajustada -ne $orig) { $node["formula"] = $ajustada; $changed = $true }
        }
        foreach ($k in @($node.Keys)) {
            $v = $node[$k]
            if ($v -is [hashtable] -or $v -is [System.Collections.IList]) {
                if (Rename-GeneroToSexo $v) { $changed = $true }
            }
        }
    } elseif ($node -is [System.Collections.IList]) {
        for ($i = 0; $i -lt $node.Count; $i++) {
            $v = $node[$i]
            if ($v -is [hashtable] -or $v -is [System.Collections.IList]) {
                if (Rename-GeneroToSexo $v) { $changed = $true }
            }
        }
    }
    return $changed
}

function Mark-DiagRequired($schema) {
    $changed = $false
    foreach ($sec in $schema.children) {
        if ($null -eq $sec["children"]) { continue }
        for ($i = 0; $i -lt $sec.children.Count; $i++) {
            $c = $sec.children[$i]
            if (Set-RequiredDiagCols $c) {
                $sec.children[$i] = $c
                $changed = $true
            }
        }
    }
    return $changed
}

$hechos = @(); $errores = @()
foreach ($codigo in $codigos) {
    Write-Host ""
    Write-Host "=== $codigo ===" -ForegroundColor White
    $bkPath = Join-Path $backupDir "$codigo.json"
    $rawBk = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$codigo' AND tenant_id='$TenantId';"
    if ([string]::IsNullOrWhiteSpace($rawBk)) { Write-Host "  no existe" -ForegroundColor Yellow; continue }
    [System.IO.File]::WriteAllText($bkPath, $rawBk, [System.Text.UTF8Encoding]::new($false))

    try {
        $schema = $rawBk | ConvertFrom-Json -AsHashtable
        $renamed = Rename-GeneroToSexo $schema
        $marked  = Mark-DiagRequired $schema
        if (-not ($renamed -or $marked)) { Write-Host "  sin cambios necesarios" -ForegroundColor DarkGray; continue }
        Write-Host ("  renombrar_genero_sexo={0}, marcar_diag_required={1}" -f $renamed, $marked) -ForegroundColor Green

        $json = ($schema | ConvertTo-Json -Depth 30 -Compress)
        $jsonSql = $json.Replace("'","''")
        $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
        $sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='$codigo' AND tenant_id='$TenantId';"
        $tmp = [System.IO.Path]::GetTempFileName()
        [System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
        try {
            $copy = "/tmp/visal_normsex_$([Guid]::NewGuid().ToString('N')).sql"
            docker cp $tmp "${PgContainer}:${copy}" | Out-Null
            $env:MSYS_NO_PATHCONV = "1"
            $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
            $exit = $LASTEXITCODE
            docker exec $PgContainer rm $copy 2>$null | Out-Null
            $env:MSYS_NO_PATHCONV = $null
            if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
        } finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
        $hechos += $codigo
    } catch {
        Write-Host "  ERROR: $_" -ForegroundColor Red
        $errores += "$codigo : $_"
    }
}

Write-Host ""
Write-Host "==================== RESUMEN ====================" -ForegroundColor Cyan
Write-Host ("Actualizados : {0}" -f $hechos.Count) -ForegroundColor Green
$hechos | ForEach-Object { Write-Host "  $_" }
if ($errores.Count -gt 0) {
    Write-Host ("Errores      : {0}" -f $errores.Count) -ForegroundColor Red
    $errores | ForEach-Object { Write-Host "  $_" }
}
Write-Host ("Backups en   : {0}" -f $backupDir) -ForegroundColor DarkGray
