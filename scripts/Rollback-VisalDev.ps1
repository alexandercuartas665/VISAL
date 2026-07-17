<#
.SYNOPSIS
    Revierte visal_dev al snapshot pre-restore mas reciente
    (o al que le pases por parametro).

.DESCRIPTION
    - Detiene Visal si esta corriendo.
    - DROP DATABASE visal_dev.
    - CREATE + pg_restore del snapshot.
    - Vuelve a poner el password del rol visal en la clave dev.
    - Arranca Visal.

.PARAMETER Snapshot
    Ruta al .dump (formato pg_dump -Fc). Por defecto lee
    D:\Backups\Local-Snapshots\LAST_SNAPSHOT.txt.
#>

[CmdletBinding()]
param(
    [string]$Snapshot,
    [switch]$NoStart
)

$ErrorActionPreference = "Stop"

if (-not $Snapshot) {
    $ptr = "D:\Backups\Local-Snapshots\LAST_SNAPSHOT.txt"
    if (-not (Test-Path $ptr)) { throw "No se encontro $ptr. Pasa -Snapshot con la ruta al .dump." }
    $Snapshot = (Get-Content $ptr -Raw).Trim()
}
if (-not (Test-Path $Snapshot)) { throw "El snapshot $Snapshot no existe." }

Write-Host "==> Snapshot: $Snapshot" -ForegroundColor Cyan

# 1) Detener Visal si esta corriendo
Write-Host "==> Deteniendo Visal" -ForegroundColor Cyan
if (Test-Path "C:\DesarrolloIA\Visal\stop-visal.ps1") {
    & "C:\DesarrolloIA\Visal\stop-visal.ps1" 2>&1 | Out-Null
} else {
    Get-Process "Visal.SuperAdmin" -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}
Start-Sleep -Seconds 2

# 2) DROP + CREATE + pg_restore
$snapName = Split-Path $Snapshot -Leaf
Write-Host "==> Copiando snapshot al container" -ForegroundColor Cyan
docker cp $Snapshot "visal-postgres:/tmp/$snapName"

Write-Host "==> DROP + CREATE + restore" -ForegroundColor Cyan
$sqlKick = "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='visal_dev' AND pid <> pg_backend_pid();"
docker exec visal-postgres psql -U visal -d postgres -c $sqlKick | Out-Null
docker exec visal-postgres psql -U visal -d postgres -c "DROP DATABASE IF EXISTS visal_dev;"
docker exec visal-postgres psql -U visal -d postgres -c "CREATE DATABASE visal_dev OWNER visal;"

docker exec -e PGPASSWORD=visal_local_2026 visal-postgres pg_restore -U visal -d visal_dev --no-owner --no-privileges "/tmp/$snapName"
docker exec visal-postgres rm -f "/tmp/$snapName"

$sqlReset = "ALTER ROLE visal WITH PASSWORD 'visal_local_2026';"
docker exec visal-postgres psql -U visal -d postgres -c $sqlReset | Out-Null

Write-Host "==> Verificando" -ForegroundColor Cyan
docker exec -e PGPASSWORD=visal_local_2026 visal-postgres psql -U visal -d visal_dev -c "SELECT id, name FROM tenants;"

# 3) Arrancar Visal
if (-not $NoStart) {
    Write-Host "==> Arrancando Visal" -ForegroundColor Cyan
    & "C:\DesarrolloIA\Visal\start-visal.ps1" -NoBuild -NoBrowser
}

Write-Host "==> Rollback listo" -ForegroundColor Green
