#requires -Version 5.1
<#
.SYNOPSIS
  Siembra el catalogo de paquetes desde el archivo NUEVO PAQUETE DOMICILIARIO
  2024-ofertado LMO.xlsx. Un paquete por hoja: el codigo viene del nombre de
  la hoja (ej. "E890167") y el nombre del titulo de la primera fila.

.NOTES
  Regla de memoria: usar [System.IO.File] con UTF-8 explicito en scripts del
  repo Visal para no romper tildes/enies. Idempotente por codigo.
#>

[CmdletBinding()]
param(
    [string]$Xlsx = 'C:\Users\acuartas\Downloads\NUEVO PAQUETE DOMICILIARIO 2024-ofertado LMO.xlsx',
    [string]$Container = 'visal-postgres',
    [string]$Database  = 'visal_dev',
    [string]$DbUser    = 'visal',
    [string]$DbPass    = 'visal_dev'
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

if (-not (Test-Path -LiteralPath $Xlsx)) {
    throw "No se encontro el archivo: $Xlsx"
}

Write-Host "[1/3] Abriendo $Xlsx..." -ForegroundColor Cyan

$zip = [System.IO.Compression.ZipFile]::OpenRead($Xlsx)
try {
    # ---- Shared strings -------------------------------------------------
    $shared = New-Object 'System.Collections.Generic.List[string]'
    $ssEntry = $zip.GetEntry('xl/sharedStrings.xml')
    if ($ssEntry) {
        $sr = New-Object System.IO.StreamReader($ssEntry.Open(), [System.Text.Encoding]::UTF8)
        $doc = [xml]$sr.ReadToEnd(); $sr.Close()
        $ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
        $ns.AddNamespace('n','http://schemas.openxmlformats.org/spreadsheetml/2006/main')
        foreach ($si in $doc.SelectNodes('//n:si', $ns)) {
            $shared.Add($si.InnerText)
        }
    }

    # ---- Workbook: mapa (sheetName -> rId) y (rId -> path) --------------
    $wb = $zip.GetEntry('xl/workbook.xml')
    $sr = New-Object System.IO.StreamReader($wb.Open(), [System.Text.Encoding]::UTF8)
    $wbDoc = [xml]$sr.ReadToEnd(); $sr.Close()
    $ns = New-Object System.Xml.XmlNamespaceManager($wbDoc.NameTable)
    $ns.AddNamespace('n','http://schemas.openxmlformats.org/spreadsheetml/2006/main')
    $ns.AddNamespace('r','http://schemas.openxmlformats.org/officeDocument/2006/relationships')

    $rels = $zip.GetEntry('xl/_rels/workbook.xml.rels')
    $sr = New-Object System.IO.StreamReader($rels.Open(), [System.Text.Encoding]::UTF8)
    $relsDoc = [xml]$sr.ReadToEnd(); $sr.Close()
    $ridToPath = @{}
    foreach ($rel in $relsDoc.Relationships.Relationship) {
        $ridToPath[$rel.Id] = "xl/" + $rel.Target.Replace('\','/')
    }

    # ---- Recorrer cada hoja y extraer titulo ----------------------------
    $paquetes = New-Object 'System.Collections.Generic.List[object]'
    $sheets = $wbDoc.SelectNodes('//n:sheets/n:sheet', $ns)
    foreach ($s in $sheets) {
        $sheetName = $s.name.Trim()
        $rId = $s.GetAttribute('id', 'http://schemas.openxmlformats.org/officeDocument/2006/relationships')
        $sheetPath = $ridToPath[$rId]
        if (-not $sheetPath) { continue }

        $entry = $zip.GetEntry($sheetPath)
        if (-not $entry) { continue }
        $sr = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
        $sdoc = [xml]$sr.ReadToEnd(); $sr.Close()
        $sns = New-Object System.Xml.XmlNamespaceManager($sdoc.NameTable)
        $sns.AddNamespace('n','http://schemas.openxmlformats.org/spreadsheetml/2006/main')

        # Titulo = primera celda de texto larga (>50 chars) que contenga "CODIGO" o el codigo del nombre de hoja
        $titulo = $null
        foreach ($cell in $sdoc.SelectNodes('//n:c', $sns)) {
            $val = $cell.SelectSingleNode('n:v', $sns)
            if (-not $val) { continue }
            $t = $cell.GetAttribute('t')
            $text = if ($t -eq 's') {
                $idx = [int]$val.InnerText
                if ($idx -ge 0 -and $idx -lt $shared.Count) { $shared[$idx] } else { '' }
            } else { $val.InnerText }
            $text = $text.Trim()
            if ($text.Length -lt 40) { continue }
            # Si contiene el codigo del nombre de la hoja, o la palabra CODIGO/CODIGO
            if ($text -match [regex]::Escape($sheetName) -or $text -match '(?i)C[ÓO]DIGO') {
                $titulo = $text -replace '\s+', ' '
                break
            }
        }
        if (-not $titulo) { continue }

        # Codigo = el sheet name (E######) OR extraerlo del titulo si aparece
        $codigo = $sheetName
        if ($titulo -match '(?i)C[ÓO]DIGO\s+([A-Z0-9]+)') { $codigo = $matches[1] }

        $paquetes.Add([pscustomobject]@{
            Codigo = $codigo
            Nombre = $titulo
        })
    }
    $global:__paquetes = $paquetes

    Write-Host ("  {0} paquetes extraidos del Excel." -f $paquetes.Count) -ForegroundColor Green
    foreach ($p in $paquetes) {
        $preview = if ($p.Nombre.Length -gt 90) { $p.Nombre.Substring(0, 87) + '...' } else { $p.Nombre }
        Write-Host ("   {0}  |  {1}" -f $p.Codigo.PadRight(10), $preview)
    }
} finally {
    $zip.Dispose()
}

if ($__paquetes.Count -eq 0) {
    Write-Host "Nada que sembrar." -ForegroundColor Yellow
    return
}

# ---- Generar SQL de insert idempotente y ejecutarlo --------------------
Write-Host "`n[2/3] Generando SQL de insert idempotente..." -ForegroundColor Cyan

$tenantIdSql = @"
SELECT tenant_id FROM aseguradoras LIMIT 1;
"@
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $tenantIdSql, [System.Text.UTF8Encoding]::new($false))
$tenantOut = Get-Content $tmp -Raw | & docker exec -i -e "PGPASSWORD=$DbPass" $Container psql -U $DbUser -d $Database -Aqt 2>&1
Remove-Item $tmp -Force -ErrorAction SilentlyContinue
$tenantId = ($tenantOut -split "`n" | Where-Object { $_ -match '^[0-9a-f-]{36}$' } | Select-Object -First 1).Trim()
if (-not $tenantId) { throw "No se pudo obtener tenant_id. Salida psql: $tenantOut" }
Write-Host ("  Tenant destino: {0}" -f $tenantId) -ForegroundColor DarkGray

$sql = New-Object System.Text.StringBuilder
[void]$sql.AppendLine("BEGIN;")
foreach ($p in $__paquetes) {
    $codigo = $p.Codigo.Replace("'", "''")
    $nombre = $p.Nombre.Replace("'", "''")
    [void]$sql.AppendLine("INSERT INTO paquetes (id, tenant_id, codigo, nombre, activo, created_at, updated_at) VALUES (gen_random_uuid(), '$tenantId', '$codigo', '$nombre', true, now(), now()) ON CONFLICT (tenant_id, codigo) DO UPDATE SET nombre = EXCLUDED.nombre, updated_at = now();")
}
[void]$sql.AppendLine("SELECT count(*) as total_paquetes FROM paquetes;")
[void]$sql.AppendLine("COMMIT;")

$sqlText = $sql.ToString()
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sqlText, [System.Text.UTF8Encoding]::new($false))

Write-Host "`n[3/3] Aplicando en la BD..." -ForegroundColor Cyan
$out = Get-Content $tmp -Raw | & docker exec -i -e "PGPASSWORD=$DbPass" $Container psql -U $DbUser -d $Database -v ON_ERROR_STOP=1 2>&1
Remove-Item $tmp -Force -ErrorAction SilentlyContinue

Write-Host $out
Write-Host "`n============================================================" -ForegroundColor Green
Write-Host ("Paquetes procesados: {0}" -f $__paquetes.Count) -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
