# Replace-HC-Diagnosticos-Grupo-B.ps1
# HC-FO-19/20/21/25: tienen un subheading suelto "DIAGNOSTICOS" seguido de
# un field 'Codigo' (c_digo) y a veces 1-2 parrafos huerfanos. Reemplaza
# ese bloque por: subheading + tabla igual a HC-FO-12.
#
# Anclajes seguros (auditados manualmente antes):
#   HC-FO-19: seccion[13] 'OTROS SISTEMAS', hijos [16..18]
#   HC-FO-20: seccion[1]  'INGRESO:',       hijos [39..41]
#   HC-FO-21: seccion[1]  'PLAN DE TRATAMIENTO PLAN DE TRATAMIENTO', hijos [0..2]
#   HC-FO-25: seccion[0]  'DATOS PERSONALES', hijos [51..53]
#
# Antes de reemplazar, VERIFICA que los indices siguen apuntando al
# subheading 'DIAGNOSTICOS'. Si no, aborta esa HC y reporta.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

$targets = @(
    @{ codigo="HC-FO-19"; secIdx=13; startIdx=16; endIdx=18 },
    @{ codigo="HC-FO-20"; secIdx=1;  startIdx=39; endIdx=41 },
    @{ codigo="HC-FO-21"; secIdx=1;  startIdx=0;  endIdx=2  },
    @{ codigo="HC-FO-25"; secIdx=0;  startIdx=51; endIdx=53 }
)

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

foreach ($t in $targets) {
    $cod = $t.codigo
    Write-Host ""
    Write-Host ("========== {0} ==========" -f $cod) -ForegroundColor Cyan
    $raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $schema = $raw | ConvertFrom-Json -AsHashtable
    $sec = $schema.children[$t.secIdx]
    if (-not $sec) { Write-Host "  no encontre seccion $($t.secIdx)"; continue }
    $subhd = $sec.children[$t.startIdx]
    $ok = ($subhd.type -eq "text" -and [string]$subhd.textStyle -eq "subheading" -and ([string]$subhd.content -match "DIAGN"))
    if (-not $ok) {
        Write-Host ("  el indice [$($t.startIdx)] no apunta a subheading DIAGNOSTICOS. Encontrado: type={0} content='{1}' - ABORTO" -f $subhd.type, ([string]$subhd.content).Substring(0,[Math]::Min(40,([string]$subhd.content).Length))) -ForegroundColor Red
        continue
    }
    $nuevo = New-DiagnosticosBlock
    $head = @(); $tail = @()
    if ($t.startIdx -gt 0) { $head = $sec.children[0..($t.startIdx - 1)] }
    if ($t.endIdx -lt ($sec.children.Count - 1)) { $tail = $sec.children[($t.endIdx + 1)..($sec.children.Count - 1)] }
    $reemplazados = $t.endIdx - $t.startIdx + 1
    $sec.children = @($head) + @($nuevo) + @($tail)
    $schema.children[$t.secIdx] = $sec
    Write-Host ("  seccion [{0}] '{1}': reemplazados [{2}..{3}] ({4} nodos) -> {5} nodos (subheading + table). Total hijos ahora: {6}" -f $t.secIdx, $sec.label, $t.startIdx, $t.endIdx, $reemplazados, $nuevo.Count, $sec.children.Count) -ForegroundColor Green

    $json = ($schema | ConvertTo-Json -Depth 30 -Compress)
    $jsonSql = $json.Replace("'","''")
    $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
    $sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='$cod' AND tenant_id='$TenantId';"
    $tmp = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
    try {
        $copy = "/tmp/visal_dxB_$([Guid]::NewGuid().ToString('N')).sql"
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
