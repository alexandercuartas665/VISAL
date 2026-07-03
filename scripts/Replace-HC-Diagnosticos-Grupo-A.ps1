# Replace-HC-Diagnosticos-Grupo-A.ps1
# Para HC-FO-13, HC-FO-14, HC-FO-15: reemplaza los 3 campos planos
# CIE / ORIGEN / RELACION (que estan en el Encabezado del documento como
# hijos consecutivos, tipicamente indices 15..17) por:
#   - subheading "DIAGNOSTICOS"
#   - field/table 'Diagnosticos' con las 3 columnas iguales a HC-FO-12
#     (autocomplete cie11, autocomplete origen, select tipo de diagnostico).
#
# Aditivo cero: NO borra ni mueve nada mas. Si los 3 campos no son
# consecutivos por lo que sea, aborta y no toca la HC. Backups previos
# ya estan en /tmp/hc_backups/*.pre_dxtable.json.
#
# NO TOCA HC-FO-08 ni HC-FO-16.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

$codigos = @("HC-FO-13","HC-FO-14","HC-FO-15")

function New-DiagnosticosBlock {
    $cols = @(
        @{ id=newId; label="Diagnostico"; name="diagnostico"
           fieldType="autocomplete"; catalog="cie11"; allowCustom=$false; defaultValue="" },
        @{ id=newId; label="Origen"; name="origen"
           fieldType="autocomplete"; catalog=""; allowCustom=$false; defaultValue="" },
        @{ id=newId; label="Tipo de diagnóstico principal"; name="tipo_diagnostico"
           fieldType="select"
           options=@("Impresión diagnóstica","Confirmado nuevo","Confirmado repetido")
           allowCustom=$false; defaultValue="" }
    )
    return @(
        @{ id=newId; type="text"; textStyle="subheading"; content="DIAGNÓSTICOS" },
        @{ id=newId; type="field"; fieldType="table"
           label="Diagnósticos"; name="diagnosticos"; widthColumns=12
           columns=$cols; lockRows=$false; allowCustom=$false
           isSection=$false; isText=$false; isTable=$true; required=$false }
    )
}

foreach ($cod in $codigos) {
    Write-Host ""
    Write-Host ("========== {0} ==========" -f $cod) -ForegroundColor Cyan
    $raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $schema = $raw | ConvertFrom-Json -AsHashtable

    $encIdx = -1
    for ($i=0; $i -lt $schema.children.Count; $i++) {
        if ($schema.children[$i].label -eq "Encabezado del documento") { $encIdx = $i; break }
    }
    if ($encIdx -lt 0) { Write-Host "  no encontre 'Encabezado del documento' - SALTO" -ForegroundColor Red; continue }
    $enc = $schema.children[$encIdx]

    $idxCie=-1; $idxOri=-1; $idxRel=-1
    for ($i=0; $i -lt $enc.children.Count; $i++) {
        $c = $enc.children[$i]
        if ($c.type -ne "field") { continue }
        switch ($c.name) {
            "cie"      { $idxCie = $i }
            "origen"   { $idxOri = $i }
            "relacion" { $idxRel = $i }
        }
    }
    if ($idxCie -lt 0 -or $idxOri -lt 0 -or $idxRel -lt 0) {
        Write-Host ("  no localice los 3 campos (cie={0} origen={1} relacion={2}) - SALTO" -f $idxCie,$idxOri,$idxRel) -ForegroundColor Red; continue
    }
    if ($idxOri -ne ($idxCie + 1) -or $idxRel -ne ($idxOri + 1)) {
        Write-Host ("  los 3 campos NO son consecutivos (cie={0} origen={1} relacion={2}) - SALTO" -f $idxCie,$idxOri,$idxRel) -ForegroundColor Red; continue
    }
    $start = $idxCie; $end = $idxRel
    Write-Host ("  rango CIE/ORIGEN/RELACION en encabezado: [{0}..{1}] = 3 nodos" -f $start, $end) -ForegroundColor Yellow

    $nuevo = New-DiagnosticosBlock
    $head = @(); $tail = @()
    if ($start -gt 0) { $head = $enc.children[0..($start - 1)] }
    if ($end -lt ($enc.children.Count - 1)) { $tail = $enc.children[($end + 1)..($enc.children.Count - 1)] }
    $enc.children = @($head) + @($nuevo) + @($tail)
    $schema.children[$encIdx] = $enc
    Write-Host ("  Sustituidos 3 nodos -> {0} nodos (subheading + table). Encabezado ahora: {1} hijos" -f $nuevo.Count, $enc.children.Count) -ForegroundColor Green

    $json = ($schema | ConvertTo-Json -Depth 30 -Compress)
    $jsonSql = $json.Replace("'","''")
    $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
    $sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $tmp = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
    try {
        $copy = "/tmp/visal_dxA_$([Guid]::NewGuid().ToString('N')).sql"
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
