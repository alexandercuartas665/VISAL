# Update-FormDefinitionsFromXlsx.ps1
# Aplica el listado del xlsx "formularios-historias" sobre form_definitions:
# para cada fila con Id Interno que ya exista en BD, actualiza
# codigo / nombre / tipo / version.
# NO toca la columna C (EVOLUCION QUE ACTIVA).
# NO inserta filas nuevas, solo UPDATE.

[CmdletBinding()]
param(
    [string]$XlsxFile = "C:\Users\acuartas\Downloads\formularios-historias (2).xlsx",
    [string]$TenantId = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser = "visal",
    [string]$PgDb = "visal_dev",
    [switch]$DryRun
)
$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.IO.Compression.FileSystem

# 1) Leer shared strings + sheet1
$zip = [System.IO.Compression.ZipFile]::OpenRead($XlsxFile)
try {
    $shared = @()
    $sst = $zip.Entries | Where-Object { $_.FullName -eq "xl/sharedStrings.xml" } | Select-Object -First 1
    if ($sst) {
        $r = New-Object System.IO.StreamReader($sst.Open(), [System.Text.Encoding]::UTF8)
        $xml = $r.ReadToEnd(); $r.Dispose()
        $d = New-Object System.Xml.XmlDocument; $d.LoadXml($xml)
        $n = New-Object System.Xml.XmlNamespaceManager($d.NameTable)
        $n.AddNamespace("x","http://schemas.openxmlformats.org/spreadsheetml/2006/main")
        foreach ($si in $d.SelectNodes("//x:si", $n)) {
            $sb = New-Object System.Text.StringBuilder
            foreach ($t in $si.SelectNodes(".//x:t", $n)) { [void]$sb.Append($t.InnerText) }
            $shared += $sb.ToString()
        }
    }

    $sheet = $zip.Entries | Where-Object { $_.FullName -eq "xl/worksheets/sheet1.xml" } | Select-Object -First 1
    $r = New-Object System.IO.StreamReader($sheet.Open(), [System.Text.Encoding]::UTF8)
    $xml = $r.ReadToEnd(); $r.Dispose()
    $d2 = New-Object System.Xml.XmlDocument; $d2.LoadXml($xml)
    $n2 = New-Object System.Xml.XmlNamespaceManager($d2.NameTable)
    $n2.AddNamespace("x","http://schemas.openxmlformats.org/spreadsheetml/2006/main")

    # 2) Recorrer cada fila salvo el header (R0).
    #    Estructura esperada: A=Id, B=Codigo, C=Evolucion (IGNORAR), D=Nombre,
    #    E=Tipo, F=Version, G=Activo
    $rows = @()
    $rIdx = 0
    foreach ($row in $d2.SelectNodes("//x:row", $n2)) {
        $cells = @{}
        foreach ($c in $row.SelectNodes("x:c", $n2)) {
            $type = $c.GetAttribute("t")
            $val = $null
            if ($type -eq "s") {
                $v = $c.SelectSingleNode("x:v", $n2)
                if ($v) { $idx = [int]$v.InnerText; if ($idx -lt $shared.Length) { $val = $shared[$idx] } }
            } elseif ($type -eq "inlineStr") {
                $sb = New-Object System.Text.StringBuilder
                foreach ($t in $c.SelectNodes(".//x:t", $n2)) { [void]$sb.Append($t.InnerText) }
                $val = $sb.ToString()
            } else {
                $v = $c.SelectSingleNode("x:v", $n2)
                if ($v) { $val = $v.InnerText }
            }
            # $c.GetAttribute("r") es algo como "A2", "B2", etc. La letra es la columna.
            $colRef = $c.GetAttribute("r") -replace '\d',''
            $cells[$colRef] = ($val ?? '').Trim()
        }
        if ($rIdx -gt 0 -and $cells['A']) {
            $rows += [pscustomobject]@{
                Id      = $cells['A']
                Codigo  = if ($cells.ContainsKey('B')) { $cells['B'] } else { '' }
                Nombre  = if ($cells.ContainsKey('D')) { $cells['D'] } else { '' }
                Tipo    = if ($cells.ContainsKey('E')) { $cells['E'] } else { '' }
                Version = if ($cells.ContainsKey('F')) { $cells['F'] } else { '' }
            }
        }
        $rIdx++
    }
} finally {
    $zip.Dispose()
}

Write-Host ""
Write-Host "==> Filas leidas del xlsx: $($rows.Count)" -ForegroundColor Cyan

# 3) Para cada fila: verificar si el id existe en BD.
$existingIds = @{}
$queryIds = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT id FROM form_definitions WHERE tenant_id='$TenantId';"
foreach ($line in ($queryIds -split "`n")) {
    $t = $line.Trim()
    if ($t) { $existingIds[$t] = $true }
}

$updates = @()
$missing = @()
foreach ($r in $rows) {
    if ($existingIds.ContainsKey($r.Id)) {
        $updates += $r
    } else {
        $missing += $r
    }
}

Write-Host "    Coincidencias en BD: $($updates.Count)" -ForegroundColor Green
if ($missing.Count -gt 0) {
    Write-Host "    Ids del xlsx que NO existen en BD: $($missing.Count) (se omiten):" -ForegroundColor Yellow
    foreach ($m in $missing) {
        Write-Host "      - $($m.Id)  $($m.Nombre)" -ForegroundColor DarkYellow
    }
}

if ($updates.Count -eq 0) { Write-Host "Nada que actualizar." -ForegroundColor Yellow; return }

# 4) Construir un solo archivo SQL con todos los UPDATEs.
$sb = New-Object System.Text.StringBuilder
foreach ($u in $updates) {
    $setParts = New-Object System.Collections.Generic.List[string]
    # codigo: solo si la celda B no esta vacia (sino dejamos lo que ya esta)
    if ($u.Codigo) {
        $setParts.Add("codigo = '$(($u.Codigo).Replace("'","''"))'")
    }
    # nombre: siempre que venga
    if ($u.Nombre) {
        $setParts.Add("nombre = '$(($u.Nombre).Replace("'","''"))'")
    }
    # tipo: siempre que venga
    if ($u.Tipo) {
        $setParts.Add("tipo = '$(($u.Tipo).Replace("'","''"))'")
    }
    # version: si viene la seteamos, si esta vacio en xlsx ponemos NULL
    if ($u.Version) {
        $setParts.Add("version = '$(($u.Version).Replace("'","''"))'")
    } else {
        $setParts.Add("version = NULL")
    }
    # updated_at: marca de tiempo nueva
    $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
    $setParts.Add("updated_at = '$now'")
    $set = $setParts -join ", "
    [void]$sb.AppendLine("UPDATE form_definitions SET $set WHERE id = '$($u.Id)' AND tenant_id = '$TenantId';")
}

if ($DryRun) {
    Write-Host ""
    Write-Host "==> [DRY RUN] SQL a ejecutar:" -ForegroundColor Yellow
    Write-Host $sb.ToString()
    return
}

# 5) Ejecutar.
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_update_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $out = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    if ($exit -ne 0) { throw "psql fallo (exit=$exit): $($out -join ' | ')" }
} finally {
    Remove-Item $tmp -ErrorAction SilentlyContinue
}
Write-Host ""
Write-Host "==> Actualizados $($updates.Count) registros" -ForegroundColor Green
