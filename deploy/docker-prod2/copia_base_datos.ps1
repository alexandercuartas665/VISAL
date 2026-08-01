# =========================================================================
#  copia_base_datos.ps1
#  ------------------------------------------------------------------------
#  Genera un dump fresco de visal_dev al lado (./dumps/) para llevar al
#  server. bootstrap-linux.ps1 toma automaticamente el .dump mas reciente.
#
#  Uso:
#    cd C:\DesarrolloIA\Visal\deploy\docker-prod2
#    .\copia_base_datos.ps1
# =========================================================================

$fecha = Get-Date -Format 'yyyy-MM-dd-HHmm'

if (-not (Test-Path .\dumps)) { New-Item -ItemType Directory -Path .\dumps | Out-Null }

docker run --rm `
    --network visal-net `
    -v "${PWD}\dumps:/dumps" `
    -e PGPASSWORD=visal_local_2026 `
    postgres:16-alpine `
    pg_dump -h visal-postgres -U visal -d visal_dev `
    --no-owner --no-privileges --clean --if-exists -Fc `
    -f /dumps/visal_dev_$fecha.dump

if ($LASTEXITCODE -eq 0) {
    Get-ChildItem dumps\visal_dev_$fecha.dump |
        Select-Object Name, @{N='SizeMB';E={[math]::Round($_.Length/1MB,2)}}, LastWriteTime
} else {
    Write-Host "Fallo el dump" -ForegroundColor Red
}
