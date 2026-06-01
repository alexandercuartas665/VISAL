# Import-Formatos.ps1
# Procesa los .docx / .xlsx de OneDrive\05. Formatos, los convierte en
# FormDefinition (schema base con texto del documento) y los mueve a PROCESADO.
#
# Uso:
#   .\Import-Formatos.ps1                  # corre todo (modo real)
#   .\Import-Formatos.ps1 -DryRun          # solo lista sin insertar ni mover
#   .\Import-Formatos.ps1 -Only "BARTHEL"  # filtra por substring del nombre
#
# Requisitos:
#   - Docker Postgres "visal-postgres" arriba (puerto 5435).
#   - Tenant Demo ya sembrado.

[CmdletBinding()]
param(
    [string]$SourceDir = "C:\Users\acuartas\OneDrive - Bitcode IT Services S.A.S\Bitcode\13. Proyectos\028. Visal\05. Formatos",
    [string]$ProcessedDir = "C:\Users\acuartas\OneDrive - Bitcode IT Services S.A.S\Bitcode\13. Proyectos\028. Visal\05. Formatos\PROCESADO",
    [string]$TenantId = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser = "visal",
    [string]$PgDb = "visal_dev",
    [switch]$DryRun,
    [string]$Only = "",
    [switch]$Reprocess,
    [switch]$NoMove
)

$ErrorActionPreference = "Stop"

# .NET ZIP assembly (no se carga por defecto en PowerShell)
Add-Type -AssemblyName System.IO.Compression.FileSystem

# --- Helpers ----------------------------------------------------------------

function Get-CellText {
    param($Cell, $Ns)
    $sb = New-Object System.Text.StringBuilder
    foreach ($t in $Cell.SelectNodes(".//w:t", $Ns)) { [void]$sb.Append($t.InnerText) }
    return $sb.ToString().Trim()
}

function Get-ParagraphText {
    param($P, $Ns)
    $sb = New-Object System.Text.StringBuilder
    foreach ($n in $P.SelectNodes(".//w:t | .//w:tab | .//w:br", $Ns)) {
        switch ($n.LocalName) {
            "t"   { [void]$sb.Append($n.InnerText) }
            "tab" { [void]$sb.Append(" ") }
            "br"  { [void]$sb.Append(" ") }
        }
    }
    $txt = ($sb.ToString() -replace '\s+',' ').Trim()
    # Word a veces parte el run en dos w:t identicos consecutivos. Si el texto
    # es exactamente "X" + "X", devolvemos "X". Solo aplica si la primera mitad
    # es igual a la segunda exacta y mide al menos 4 chars.
    if ($txt.Length -ge 8 -and $txt.Length % 2 -eq 0) {
        $half = $txt.Length / 2
        $a = $txt.Substring(0, $half)
        $b = $txt.Substring($half)
        if ($a -ceq $b) { $txt = $a }
    }
    return $txt
}

function Get-FieldTypeForLabel {
    param([string]$Label)
    $l = $Label.ToLowerInvariant()
    # "lugar de nacimiento" NO es fecha; "fecha de nacimiento" SI.
    if ($l -match '\blugar') { return 'text' }
    if ($l -match '\bfecha\b|^fec\.|^f\.\s*nac|^fec\b|\bfup\b|\bfuc\b|\bfur\b') { return 'date' }
    if ($l -match '^edad\b|^peso\b|^talla\b|^imc\b|^per[ií]metro|^frecuencia|^tensi[oó]n|^saturaci[oó]n|^temperatura|\b(kg|cm|mmhg|lpm|rpm)\b') { return 'number' }
    if ($l -match 'observ|diagn[oó]st|^plan\b|anamnesis|tratamiento|evoluci[oó]n|antecedente|examen f[ií]s|justificaci[oó]n|motivo|enfermedad actual|impresi[oó]n|recomendac|^nota\b|descrip|conducta|hallazgo|valoraci[oó]n|conclusi[oó]n|paraclinic|cuidad') { return 'textarea' }
    if ($l -match '\bsexo\b|\bg[eé]nero\b') { return 'select' }
    return 'text'
}

function New-FieldNode {
    param([string]$Label, [int]$Width = 6)
    $type = Get-FieldTypeForLabel $Label
    # Nombre interno: solo alfanumericos minusculas, con _ como separador
    $nm = $Label.ToLowerInvariant() -replace '[^a-z0-9]+','_'
    $nm = $nm.Trim('_')
    if ($nm.Length -gt 40) { $nm = $nm.Substring(0,40).Trim('_') }
    if (-not $nm) { $nm = "campo" }

    $node = @{
        id = [Guid]::NewGuid().ToString("N").Substring(0,8)
        type = "field"
        fieldType = $type
        label = $Label.TrimEnd(':',' ')
        name = $nm
        widthColumns = $Width
    }
    if ($type -eq "select" -and $Label -match '(?i)sexo|g[eé]nero') {
        $node.catalog = "estatico"
        $node.options = @("MASCULINO","FEMENINO","OTRO")
    }
    return $node
}

function New-TextNode {
    param([string]$Style, [string]$Content)
    return @{
        id = [Guid]::NewGuid().ToString("N").Substring(0,8)
        type = "text"
        textStyle = $Style
        content = $Content
    }
}

function ConvertTo-RowFields {
    param([string[]]$CellTexts)
    # Recibe el texto de cada celda de una fila. Identifica patrones:
    #   - "Label:" "" -> campo width 12/cantidad-de-pares
    #   - "Label:" "Valor" -> campo con label (descartamos el valor de ejemplo)
    #   - 1 sola celda con texto MAYUSCULAS y sin ":" -> heading de seccion
    #   - Celdas raras (vacias, separadores) -> ignorar
    #
    # Devuelve una lista de nodos (fields o text-heading) o $null si la fila no aporta.
    $nodes = @()
    # Forzamos array (PowerShell colapsa a string si hay un solo elemento).
    $nonEmpty = @($CellTexts | Where-Object { $_ -and $_.Length -gt 0 })

    if ($nonEmpty.Count -eq 0) { return @() }

    # Caso 1: una sola celda con texto MAYUS / negrita - es heading.
    if ($nonEmpty.Count -eq 1) {
        $t = [string]$nonEmpty[0]
        if ($t.Length -lt 80 -and -not $t.Contains(':')) {
            if ($t -ceq $t.ToUpperInvariant() -and ($t -match '[A-ZÁÉÍÓÚÑ]')) {
                return ,@(New-TextNode "subheading" $t)
            }
            return ,@(New-TextNode "paragraph" $t)
        }
    }

    # Caso 2: pares Label/Valor. Pegamos celdas en pares consecutivos.
    $i = 0
    while ($i -lt $CellTexts.Count) {
        $label = $CellTexts[$i]
        $value = if ($i+1 -lt $CellTexts.Count) { $CellTexts[$i+1] } else { "" }

        # Si esta celda es un label (termina en ':' o esta seguida por celda corta),
        # generamos campo y avanzamos 2.
        $looksLabel = ($label -match ':\s*$') -or
                      ($label.Length -gt 0 -and $label.Length -lt 60 -and -not ($label -match '^\d'))
        if ($looksLabel -and $label.Length -gt 0) {
            # Si "value" mide menos de 3 chars y son unidades (kg, cm, ml, %),
            # las agregamos al label.
            $unitMatch = $false
            if ($value -match '^(kg|cm|ml|mmhg|%|mg|g|lts|l)\.?$') {
                $label = "$label ($value)"
                $i = $i + 2
                $unitMatch = $true
            }
            $nodes += New-FieldNode -Label $label.TrimEnd(':') -Width 6
            if (-not $unitMatch) { $i = $i + 2 }
        } else {
            # Celda sin label parseable: descartamos como filler.
            $i = $i + 1
        }
    }

    # Ajustar widthColumns para que sumen 12 por fila
    if (@($nodes).Count -gt 0) {
        $fieldsOnly = $nodes | Where-Object { $_.type -eq 'field' }
        $cnt = @($fieldsOnly).Count
        if ($cnt -gt 0) {
            $w = [Math]::Max(2, [int]([Math]::Floor(12 / $cnt)))
            foreach ($f in $fieldsOnly) { $f.widthColumns = $w }
        }
    }

    return ,$nodes
}

function Get-DocxStructure {
    param([string]$Path)
    # Devuelve la lista ordenada de "bloques" del documento:
    #   - paragraphs con style heading/subheading/paragraph
    #   - tablas convertidas en secuencias de fields (label + tipo)
    # Las tablas son la clave: ahi vive la estructura real del formulario.
    try {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
        try {
            $entry = $zip.Entries | Where-Object { $_.FullName -eq "word/document.xml" } | Select-Object -First 1
            if (-not $entry) { return @() }
            $r = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8)
            try { $xml = $r.ReadToEnd() } finally { $r.Dispose() }
        } finally { $zip.Dispose() }
    } catch {
        Write-Warning "No se pudo leer $Path : $_"
        return @()
    }

    $doc = New-Object System.Xml.XmlDocument
    try { $doc.LoadXml($xml) } catch { return @() }

    $ns = New-Object System.Xml.XmlNamespaceManager($doc.NameTable)
    $ns.AddNamespace("w","http://schemas.openxmlformats.org/wordprocessingml/2006/main")

    $body = $doc.SelectSingleNode("//w:body", $ns)
    if (-not $body) { return @() }

    $blocks = @()
    $lastHeading = ""
    # Para detectar duplicado en ventana cercana (no solo el inmediato anterior).
    $recentHeadings = New-Object System.Collections.Generic.HashSet[string]
    $seenLabels = New-Object System.Collections.Generic.HashSet[string]

    function Add-Block($node) {
        if ($node.type -eq "text" -and $node.textStyle -eq "heading") {
            # Heading duplicado en cualquier parte: ignorar (las tablas-encabezado
            # de Word se repiten por fila, queremos solo la primera).
            if ($script:recentHeadings.Contains($node.content)) { return }
            [void]$script:recentHeadings.Add($node.content)
            $script:lastHeading = $node.content
        }
        if ($node.type -eq "text" -and $node.textStyle -eq "subheading") {
            if ($node.content -ceq $script:lastHeading) { return }
        }
        if ($node.type -eq "field") {
            # Campo duplicado dentro del mismo heading: ignorar.
            $key = "$($script:lastHeading)|$($node.label.ToLowerInvariant())"
            if ($script:seenLabels.Contains($key)) { return }
            [void]$script:seenLabels.Add($key)
        }
        $script:blocks += $node
    }
    $script:blocks = @()
    $script:lastHeading = ""
    $script:recentHeadings = New-Object System.Collections.Generic.HashSet[string]
    $script:seenLabels = New-Object System.Collections.Generic.HashSet[string]

    foreach ($child in $body.ChildNodes) {
        if ($child.NodeType -ne [System.Xml.XmlNodeType]::Element) { continue }
        switch ($child.LocalName) {
            "p" {
                $txt = Get-ParagraphText $child $ns
                if (-not $txt) { continue }
                $style = "paragraph"
                if ($txt.Length -lt 80 -and $txt -ceq $txt.ToUpperInvariant() -and ($txt -match '[A-ZÁÉÍÓÚÑ]')) {
                    $style = "heading"
                } elseif ($txt.Length -lt 80 -and $txt.EndsWith(':')) {
                    $style = "subheading"
                }
                Add-Block (New-TextNode $style $txt)
            }
            "tbl" {
                $rows = $child.SelectNodes("w:tr", $ns)
                foreach ($row in $rows) {
                    $cells = $row.SelectNodes("w:tc", $ns)
                    $cellTexts = @()
                    foreach ($c in $cells) {
                        $cellTexts += (Get-CellText $c $ns)
                    }
                    $nodes = ConvertTo-RowFields -CellTexts $cellTexts
                    foreach ($n in $nodes) { Add-Block $n }
                }
            }
        }
    }

    return ,$script:blocks
}

function Get-DocxText {
    # Kept para xlsx que usa el flujo viejo. Para .docx vamos por Get-DocxStructure.
    param([string]$Path)
    $structure = Get-DocxStructure $Path
    # Solo extraemos texto plano para reportes/contadores.
    $out = @()
    foreach ($b in $structure) {
        if ($b.type -eq "text") { $out += $b.content }
        elseif ($b.type -eq "field") { $out += "$($b.label): _____" }
    }
    return $out
}

function Get-XlsxSheetsText {
    param([string]$Path)
    # Para xlsx: leemos las celdas con texto inline (<is><t>) + los strings
    # compartidos referenciados por las celdas tipo "s". Vamos en orden de hoja.
    $out = @()
    try {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($Path)
        try {
            # 1) Cargar strings compartidos.
            $shared = @()
            $sst = $zip.Entries | Where-Object { $_.FullName -eq "xl/sharedStrings.xml" } | Select-Object -First 1
            if ($sst) {
                $r = New-Object System.IO.StreamReader($sst.Open(), [System.Text.Encoding]::UTF8)
                try { $sstXml = $r.ReadToEnd() } finally { $r.Dispose() }
                $sstDoc = New-Object System.Xml.XmlDocument
                try {
                    $sstDoc.LoadXml($sstXml)
                    $sstNs = New-Object System.Xml.XmlNamespaceManager($sstDoc.NameTable)
                    $sstNs.AddNamespace("x","http://schemas.openxmlformats.org/spreadsheetml/2006/main")
                    foreach ($si in $sstDoc.SelectNodes("//x:si", $sstNs)) {
                        # <si> puede tener <t> directo o varios <r><t>.
                        $sb = New-Object System.Text.StringBuilder
                        foreach ($t in $si.SelectNodes(".//x:t", $sstNs)) { [void]$sb.Append($t.InnerText) }
                        $shared += $sb.ToString()
                    }
                } catch { Write-Warning "sharedStrings.xml invalido: $_" }
            }

            # 2) Recorrer cada hoja en xl/worksheets/sheet*.xml.
            $sheets = $zip.Entries | Where-Object { $_.FullName -like "xl/worksheets/sheet*.xml" } | Sort-Object FullName
            foreach ($sheet in $sheets) {
                $r = New-Object System.IO.StreamReader($sheet.Open(), [System.Text.Encoding]::UTF8)
                try { $sheetXml = $r.ReadToEnd() } finally { $r.Dispose() }
                $sDoc = New-Object System.Xml.XmlDocument
                try {
                    $sDoc.LoadXml($sheetXml)
                    $sNs = New-Object System.Xml.XmlNamespaceManager($sDoc.NameTable)
                    $sNs.AddNamespace("x","http://schemas.openxmlformats.org/spreadsheetml/2006/main")
                    foreach ($row in $sDoc.SelectNodes("//x:row", $sNs)) {
                        $cells = @()
                        foreach ($c in $row.SelectNodes("x:c", $sNs)) {
                            $type = $c.GetAttribute("t")
                            $val = $null
                            if ($type -eq "s") {
                                $idx = [int]($c.SelectSingleNode("x:v", $sNs).InnerText)
                                if ($idx -lt $shared.Length) { $val = $shared[$idx] }
                            } elseif ($type -eq "inlineStr") {
                                $is = $c.SelectSingleNode("x:is", $sNs)
                                if ($is) {
                                    $sb = New-Object System.Text.StringBuilder
                                    foreach ($t in $is.SelectNodes(".//x:t", $sNs)) { [void]$sb.Append($t.InnerText) }
                                    $val = $sb.ToString()
                                }
                            } else {
                                $v = $c.SelectSingleNode("x:v", $sNs)
                                if ($v) { $val = $v.InnerText }
                            }
                            if ($val -and $val.Trim().Length -gt 0) { $cells += $val.Trim() }
                        }
                        if ($cells.Count -gt 0) { $out += ($cells -join "  |  ") }
                    }
                } catch { Write-Warning "sheet invalido: $_" }
            }
        } finally { $zip.Dispose() }
    } catch {
        Write-Warning "No se pudo leer $Path : $_"
    }
    return $out
}

function Get-CodigoFromName {
    param([string]$BaseName)
    # Normaliza: mayusculas, sin acentos, espacios -> guion, sin caracteres raros.
    $s = $BaseName.ToUpperInvariant()
    $map = @{ 'Á'='A';'É'='E';'Í'='I';'Ó'='O';'Ú'='U';'Ñ'='N';'À'='A';'È'='E';'Ì'='I';'Ò'='O';'Ù'='U' }
    foreach ($k in $map.Keys) { $s = $s.Replace($k, $map[$k]) }
    $s = ($s -replace '[^A-Z0-9\-_ ]','').Trim()
    $s = $s -replace '\s+','-'
    # La columna codigo es varchar(40); dejamos 4 chars para sufijo -V99 si hay colision.
    if ($s.Length -gt 36) { $s = $s.Substring(0,36).TrimEnd('-','_') }
    return $s
}

function Get-BlocksFromParagraphs {
    param([string[]]$Paragraphs)
    # Construye una lista de FormNode con type=text y textStyle:
    #   - heading si esta TODO EN MAYUSCULAS y < 80 chars
    #   - subheading si termina en ":" y < 80 chars
    #   - paragraph en otros casos
    $nodes = @()
    foreach ($p in $Paragraphs) {
        if ([string]::IsNullOrWhiteSpace($p)) { continue }
        $style = "paragraph"
        if ($p.Length -lt 80 -and $p -ceq $p.ToUpperInvariant() -and ($p -match '[A-Z]')) {
            $style = "heading"
        } elseif ($p.Length -lt 80 -and $p.EndsWith(':')) {
            $style = "subheading"
        }
        $nodes += [pscustomobject]@{
            id        = [Guid]::NewGuid().ToString("N").Substring(0,8)
            type      = "text"
            textStyle = $style
            content   = $p
        }
    }
    return ,$nodes
}

function Build-SchemaJson {
    param([string]$Titulo, $Blocks)

    # Convertimos la lista plana de bloques en secciones: cada heading (text con
    # style=heading) abre una nueva seccion; los demas bloques (fields y texts)
    # se acumulan dentro de la seccion vigente.
    $sections = @()
    $current = $null
    $orphans = @()  # bloques sueltos antes del primer heading -> seccion default

    foreach ($b in $Blocks) {
        if ($b.type -eq "text" -and $b.textStyle -eq "heading") {
            if ($current) { $sections += $current }
            $current = @{
                id = [Guid]::NewGuid().ToString("N").Substring(0,8)
                type = "section"
                label = $b.content
                children = @()
            }
        } else {
            if (-not $current) {
                $orphans += $b
            } else {
                $current.children += $b
            }
        }
    }
    if ($current) { $sections += $current }

    # Si quedaron bloques sin heading al inicio, los metemos en una seccion
    # "Encabezado del documento" antes de las demas.
    if (@($orphans).Count -gt 0) {
        $intro = @{
            id = [Guid]::NewGuid().ToString("N").Substring(0,8)
            type = "section"
            label = "Encabezado del documento"
            children = $orphans
        }
        $sections = @($intro) + $sections
    }

    # Si NO encontramos NADA en el documento, dejamos un placeholder util.
    if (@($sections).Count -eq 0) {
        $sections = @(@{
            id = [Guid]::NewGuid().ToString("N").Substring(0,8)
            type = "section"
            label = "CONTENIDO"
            children = @(@{
                id = [Guid]::NewGuid().ToString("N").Substring(0,8)
                type = "field"
                fieldType = "textarea"
                label = "Contenido del formato"
                name = "contenido"
                widthColumns = 12
            })
        })
    }

    # Siempre agregamos al final una seccion "Cierre" con firma + observaciones.
    $sections += @{
        id = [Guid]::NewGuid().ToString("N").Substring(0,8)
        type = "section"
        label = "Cierre"
        children = @(
            @{
                id = [Guid]::NewGuid().ToString("N").Substring(0,8)
                type = "field"
                fieldType = "textarea"
                label = "Observaciones / Conclusiones"
                name = "observaciones_cierre"
                widthColumns = 12
            }
        )
    }

    $schema = [pscustomobject]@{
        header = [pscustomobject]@{
            institucion = "IPS VISAL RT"
            tagline = "Atencion Humana, Agil y Oportuna"
            titulo = $Titulo
            campos = @(
                @{ id=[Guid]::NewGuid().ToString("N").Substring(0,8); label="No Historia" },
                @{ id=[Guid]::NewGuid().ToString("N").Substring(0,8); label="Consecutivo" },
                @{ id=[Guid]::NewGuid().ToString("N").Substring(0,8); label="Ciudad y Fecha" }
            )
        }
        children = $sections
    }

    return ($schema | ConvertTo-Json -Depth 20 -Compress)
}

function Test-CodigoExists {
    param([string]$Codigo)
    $sql = "SELECT 1 FROM form_definitions WHERE codigo = '$Codigo' AND tenant_id = '$TenantId' LIMIT 1;"
    $res = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c $sql 2>$null
    if ($null -eq $res) { return $false }
    return ((($res -join "`n").Trim()).Length -gt 0)
}

function Remove-FormDefinition {
    param([string]$Codigo)
    $sql = "DELETE FROM form_definitions WHERE codigo = '$Codigo' AND tenant_id = '$TenantId';"
    docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -c $sql 2>&1 | Out-Null
}

function Insert-FormDefinition {
    param(
        [string]$Codigo,
        [string]$Nombre,
        [string]$Version,
        [string]$Tipo,
        [string]$SchemaJson
    )
    $id = [Guid]::NewGuid().ToString()
    $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")

    # Escapamos comillas simples doblando.
    $nameSql    = $Nombre.Replace("'","''")
    $verSql     = if ($Version) { "'" + $Version.Replace("'","''") + "'" } else { "NULL" }
    $tipoSql    = if ($Tipo)    { "'" + $Tipo.Replace("'","''")    + "'" } else { "NULL" }
    $schemaSql  = $SchemaJson.Replace("'","''")

    $sql = @"
INSERT INTO form_definitions
  (id, tenant_id, codigo, nombre, version, tipo, schema_json, prefill_routes_json, activo, created_at, updated_at)
VALUES
  ('$id', '$TenantId', '$Codigo', '$nameSql', $verSql, $tipoSql, '$schemaSql'::jsonb, NULL, true, '$now', '$now');
"@

    # Pasamos por stdin para evitar limites de cmdline.
    $tmp = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
    try {
        $copy = "/tmp/visal_$([Guid]::NewGuid().ToString('N')).sql"
        docker cp $tmp "${PgContainer}:${copy}" | Out-Null
        # ON_ERROR_STOP=1 hace que psql devuelva error si cualquier statement falla.
        $out = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
        $exit = $LASTEXITCODE
        docker exec $PgContainer rm $copy 2>$null | Out-Null
        if ($exit -ne 0) {
            throw "psql fallo (exit=$exit): $($out -join ' | ')"
        }
    } finally {
        Remove-Item $tmp -ErrorAction SilentlyContinue
    }
    return $id
}

function Move-ToProcessed {
    param([string]$SourceFile, [string]$SourceRoot, [string]$ProcessedRoot)
    $rel = $SourceFile.Substring($SourceRoot.Length).TrimStart('\','/')
    $target = Join-Path $ProcessedRoot $rel
    $targetDir = Split-Path $target -Parent
    if (-not (Test-Path $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }
    if (Test-Path $target) {
        $stamp = (Get-Date).ToString("yyyyMMddHHmmss")
        $ext = [System.IO.Path]::GetExtension($target)
        $base = [System.IO.Path]::GetFileNameWithoutExtension($target)
        $target = Join-Path $targetDir ("$base.$stamp$ext")
    }
    Move-Item -Path $SourceFile -Destination $target -Force
    return $target
}

# --- Main -------------------------------------------------------------------

Write-Host ""
Write-Host "==> Inventario en $SourceDir" -ForegroundColor Cyan

$files = Get-ChildItem -Path $SourceDir -Recurse -File |
    Where-Object {
        ($NoMove -or ($_.FullName -notlike "$ProcessedDir*")) -and
        ($_.Extension -ieq ".docx" -or $_.Extension -ieq ".xlsx")
    } |
    Sort-Object FullName

if ($Only) {
    $files = $files | Where-Object { $_.Name -match $Only }
}

Write-Host "    Archivos a procesar: $($files.Count)" -ForegroundColor Green

$ok = 0; $skip = 0; $err = 0
foreach ($f in $files) {
    $rel = $f.FullName.Substring($SourceDir.Length).TrimStart('\','/')
    Write-Host ""
    Write-Host "==> [$($ok+$skip+$err+1)/$($files.Count)] $rel" -ForegroundColor Cyan

    try {
        $base = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
        $codBase = Get-CodigoFromName $base
        $codigo = $codBase
        $ver = $null
        if ($Reprocess) {
            # En modo reprocess pisamos el registro existente con el mismo codigo
            # (sin crear -V2). Mantenemos la version null.
            if (Test-CodigoExists $codigo) {
                Remove-FormDefinition $codigo
                Write-Host "    (reprocess) borrado registro previo con codigo $codigo" -ForegroundColor DarkYellow
            }
        } else {
            $n = 2
            while (Test-CodigoExists $codigo) {
                $codigo = "$codBase-V$n"
                $ver = "v$n"
                $n++
            }
        }

        # Para docx: extraemos estructura real (tablas -> campos, parrafos -> textos)
        # Para xlsx: extraemos filas como parrafos y las convertimos a bloques.
        $blocks = if ($f.Extension -ieq ".xlsx") {
            $rows = Get-XlsxSheetsText $f.FullName
            $tmp = @()
            foreach ($p in $rows) {
                $style = "paragraph"
                if ($p.Length -lt 80 -and $p -ceq $p.ToUpperInvariant() -and ($p -match '[A-Z]')) { $style = "heading" }
                elseif ($p.Length -lt 80 -and $p.EndsWith(':')) { $style = "subheading" }
                $tmp += New-TextNode $style $p
            }
            ,$tmp
        } else {
            Get-DocxStructure $f.FullName
        }
        $fieldCount = (@($blocks) | Where-Object { $_.type -eq 'field' }).Count
        $textCount  = (@($blocks) | Where-Object { $_.type -eq 'text' }).Count
        Write-Host "    Bloques extraidos: $($blocks.Count) (campos=$fieldCount textos=$textCount)"

        # Tipo segun subcarpeta (varchar(40) en BD)
        $tipo = $rel.Split([char[]]@('\','/'))[0]
        if (-not $tipo) { $tipo = "FORMATO" }
        if ($tipo.Length -gt 40) { $tipo = $tipo.Substring(0,40) }

        $schemaJson = Build-SchemaJson -Titulo $base -Blocks $blocks

        if ($DryRun) {
            Write-Host "    [DRY] codigo=$codigo  nombre=$base  tipo=$tipo  bytes_schema=$($schemaJson.Length)" -ForegroundColor Yellow
            $skip++
            continue
        }

        $newId = Insert-FormDefinition -Codigo $codigo -Nombre $base -Version $ver -Tipo $tipo -SchemaJson $schemaJson
        Write-Host "    insertado id=$newId codigo=$codigo" -ForegroundColor Green

        if (-not $NoMove) {
            $moved = Move-ToProcessed -SourceFile $f.FullName -SourceRoot $SourceDir -ProcessedRoot $ProcessedDir
            Write-Host "    movido a $moved" -ForegroundColor Green
        }

        $ok++
    } catch {
        Write-Host "    ERROR: $_" -ForegroundColor Red
        Write-Host "    @ $($_.ScriptStackTrace)" -ForegroundColor DarkRed
        $err++
    }
}

Write-Host ""
Write-Host "==> Resumen: insertados=$ok  saltados=$skip  errores=$err" -ForegroundColor Cyan
