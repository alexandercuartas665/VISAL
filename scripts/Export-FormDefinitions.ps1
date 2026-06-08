# Export-FormDefinitions.ps1
# Exporta el listado de form_definitions a un .xlsx con la misma estructura
# que el archivo "formularios-historias" que el usuario me paso. Usa la DLL
# ClosedXML que ya esta en stable-bin (no requiere instalar nada).
#
# Uso:
#   .\Export-FormDefinitions.ps1
#   .\Export-FormDefinitions.ps1 -OutputPath "C:\Users\acuartas\Downloads\formularios.xlsx"

[CmdletBinding()]
param(
    [string]$OutputPath = "C:\Users\acuartas\Downloads\formularios-historias-actualizado.xlsx",
    [string]$TenantId = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser = "visal",
    [string]$PgDb = "visal_dev"
)
$ErrorActionPreference = "Stop"

# 1) Cargar DLLs
$stableBin = "C:\DesarrolloIA\Visal\stable-bin"
foreach ($dll in @(
    "DocumentFormat.OpenXml.dll",
    "DocumentFormat.OpenXml.Framework.dll",
    "ClosedXML.dll",
    "ClosedXML.Parser.dll",
    "ExcelNumberFormat.dll"
)) {
    $p = Join-Path $stableBin $dll
    if (Test-Path $p) { Add-Type -Path $p -ErrorAction SilentlyContinue }
}

# 2) Consultar BD - query consistente con el formato del xlsx anterior
$query = @"
SELECT id,
       coalesce(codigo,'') AS codigo,
       nombre,
       coalesce(tipo,'') AS tipo,
       coalesce(version,'') AS version,
       activo,
       to_char(created_at, 'YYYY-MM-DD HH24:MI:SS') AS creado,
       coalesce(to_char(updated_at, 'YYYY-MM-DD HH24:MI:SS'), '') AS actualizado
FROM form_definitions
WHERE tenant_id = '$TenantId'
ORDER BY tipo NULLS LAST, codigo NULLS LAST, nombre
"@

$out = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -F "`t" -c $query
$rows = @()
foreach ($line in ($out -split "`r?`n")) {
    $line = $line.Trim("`r")
    if (-not $line) { continue }
    $cols = $line -split "`t"
    if ($cols.Count -ge 8) {
        $rows += [pscustomobject]@{
            Id          = $cols[0]
            Codigo      = $cols[1]
            Nombre      = $cols[2]
            Tipo        = $cols[3]
            Version     = $cols[4]
            Activo      = $cols[5]
            Creado      = $cols[6]
            Actualizado = $cols[7]
        }
    }
}

Write-Host "Filas leidas de BD: $($rows.Count)" -ForegroundColor Cyan

# 3) Crear el .xlsx
$wb = New-Object ClosedXML.Excel.XLWorkbook
$ws = $wb.AddWorksheet("FormDefinitions")

# Header - misma estructura que el xlsx anterior + columna EVOLUCION QUE ACTIVA vacia
$headers = @("Id Interno","Codigo Principal","EVOLUCION QUE ACTIVA","Nombre","Tipo","Version","Activo","Creado","Actualizado")
for ($i = 0; $i -lt $headers.Count; $i++) {
    $cell = $ws.Cell(1, $i + 1)
    $cell.Value = $headers[$i]
    $cell.Style.Font.Bold = $true
    $cell.Style.Fill.BackgroundColor = [ClosedXML.Excel.XLColor]::LightGray
}

# Datos
for ($r = 0; $r -lt $rows.Count; $r++) {
    $row = $rows[$r]
    $excelRow = $r + 2
    $ws.Cell($excelRow, 1).Value = $row.Id
    $ws.Cell($excelRow, 2).Value = $row.Codigo
    # Columna 3 (EVOLUCION QUE ACTIVA) la dejamos vacia - el usuario la llena a mano
    $ws.Cell($excelRow, 4).Value = $row.Nombre
    $ws.Cell($excelRow, 5).Value = $row.Tipo
    $ws.Cell($excelRow, 6).Value = $row.Version
    $ws.Cell($excelRow, 7).Value = $row.Activo
    $ws.Cell($excelRow, 8).Value = $row.Creado
    $ws.Cell($excelRow, 9).Value = $row.Actualizado
}

# Ajustar anchos
$ws.Columns().AdjustToContents() | Out-Null
$ws.Column(1).Width = 38   # Id (UUID)
$ws.Column(3).Width = 25   # EVOLUCION QUE ACTIVA
$ws.Column(4).Width = 55   # Nombre

# Auto-filtro y vista congelada en fila 1
$ws.RangeUsed().SetAutoFilter() | Out-Null
$ws.SheetView.FreezeRows(1)

# 4) Guardar
$dir = Split-Path $OutputPath -Parent
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$wb.SaveAs($OutputPath)
$wb.Dispose()

Write-Host "OK Excel generado: $OutputPath" -ForegroundColor Green
Write-Host "    Total formatos: $($rows.Count)" -ForegroundColor Green

# Resumen por tipo
$byTipo = $rows | Group-Object Tipo | Sort-Object Count -Descending
Write-Host ""
Write-Host "Distribucion por tipo:" -ForegroundColor Cyan
foreach ($g in $byTipo) {
    $t = if ($g.Name) { $g.Name } else { "(sin tipo)" }
    Write-Host ("  {0,-22}  {1,3}" -f $t, $g.Count)
}
