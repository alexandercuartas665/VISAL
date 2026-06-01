$fecha = Get-Date -Format 'yyyy-MM-dd-HHmm'

docker run --rm `
  --network visal-net `
  -v "${PWD}\dumps:/dumps" `
  -e PGPASSWORD=visal_local_2026 `
  postgres:16-alpine `
  pg_dump -h visal-postgres -U visal -d visal_dev `
  --no-owner --no-privileges --clean --if-exists -Fc `
  -f /dumps/visal_dev_$fecha.dump

# Verificar que quedo
Get-ChildItem dumps\visal_dev_$fecha.dump | `
  Select-Object Name, @{N='SizeMB';E={[math]::Round($_.Length/1MB,2)}}, LastWriteTime