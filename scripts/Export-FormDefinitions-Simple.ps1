# Export-FormDefinitions-Simple.ps1
# Exporta a .xlsx un listado SIMPLE de form_definitions con 4 columnas:
# Id, Codigo, Nombre, Tipo. Sirve para hojas de control / planillas de
# trabajo donde no se necesita el resto de metadatos.
#
# Uso:
#   .\Export-FormDefinitions-Simple.ps1
#   .\Export-FormDefinitions-Simple.ps1 -OutputPath "C:\Users\acuartas\Downloads\mi-archivo.xlsx"

[CmdletBinding()]
param(
    [string]$OutputPath = "C:\Users\acuartas\Downloads\formularios-listado.xlsx",
    [string]$TenantId   = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"

# Las DLLs de ClosedXML estan compiladas para .NET 9, asi que Windows PowerShell
# 5.1 (.NET Framework 4.x) no las puede cargar. Si detectamos esa version
# relanzamos automaticamente con pwsh.exe (PowerShell 7+) preservando los
# parametros que recibimos.
if ($PSVersionTable.PSEdition -eq "Desktop") {
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if (-not $pwsh) {
        throw "Este script requiere PowerShell 7+ (pwsh.exe). Windows PowerShell 5.1 no puede cargar las DLLs .NET 9 de ClosedXML."
    }
    Write-Host "Detectado Windows PowerShell 5.1. Relanzando con pwsh.exe..." -ForegroundColor Yellow
    $args = @("-NoProfile","-File", $PSCommandPath,
              "-OutputPath", $OutputPath,
              "-TenantId", $TenantId,
              "-PgContainer", $PgContainer,
              "-PgUser", $PgUser,
              "-PgDb", $PgDb)
    & $pwsh.Source @args
    return
}

# 1) Cargar DLLs (mismas que el export ampliado)
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

# 2) Consultar BD - solo 4 columnas, ordenadas por tipo + codigo
$query = @"
SELECT id,
       coalesce(codigo,'') AS codigo,
       nombre,
       coalesce(tipo,'') AS tipo
FROM form_definitions
WHERE tenant_id = '$TenantId'
ORDER BY tipo NULLS LAST, codigo NULLS LAST, nombre
"@

$out  = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -F "`t" -c $query
$rows = @()
foreach ($line in ($out -split "`r?`n")) {
    $line = $line.Trim("`r")
    if (-not $line) { continue }
    $cols = $line -split "`t"
    if ($cols.Count -ge 4) {
        $rows += [pscustomobject]@{
            Id     = $cols[0]
            Codigo = $cols[1]
            Nombre = $cols[2]
            Tipo   = $cols[3]
        }
    }
}

Write-Host "Filas leidas de BD: $($rows.Count)" -ForegroundColor Cyan

# 3) Generar .xlsx
$wb = New-Object ClosedXML.Excel.XLWorkbook
$ws = $wb.AddWorksheet("Formularios")

$headers = @("Id", "Codigo", "Nombre", "Tipo")
for ($i = 0; $i -lt $headers.Count; $i++) {
    $cell = $ws.Cell(1, $i + 1)
    $cell.Value = $headers[$i]
    $cell.Style.Font.Bold = $true
    $cell.Style.Fill.BackgroundColor = [ClosedXML.Excel.XLColor]::LightGray
}

for ($r = 0; $r -lt $rows.Count; $r++) {
    $row = $rows[$r]
    $excelRow = $r + 2
    $ws.Cell($excelRow, 1).Value = $row.Id
    $ws.Cell($excelRow, 2).Value = $row.Codigo
    $ws.Cell($excelRow, 3).Value = $row.Nombre
    $ws.Cell($excelRow, 4).Value = $row.Tipo
}

$ws.Columns().AdjustToContents() | Out-Null
$ws.Column(1).Width = 38   # UUID
$ws.Column(2).Width = 22
$ws.Column(3).Width = 60
$ws.Column(4).Width = 24

$ws.RangeUsed().SetAutoFilter() | Out-Null
$ws.SheetView.FreezeRows(1)

# 4) Guardar
$dir = Split-Path $OutputPath -Parent
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$wb.SaveAs($OutputPath)
$wb.Dispose()

Write-Host "OK Excel generado: $OutputPath" -ForegroundColor Green
Write-Host "    Total formularios: $($rows.Count)" -ForegroundColor Green

# Resumen por tipo
$byTipo = $rows | Group-Object Tipo | Sort-Object Count -Descending
Write-Host ""
Write-Host "Distribucion por tipo:" -ForegroundColor Cyan
foreach ($g in $byTipo) {
    $t = if ($g.Name) { $g.Name } else { "(sin tipo)" }
    Write-Host ("  {0,-22}  {1,3}" -f $t, $g.Count)
}
