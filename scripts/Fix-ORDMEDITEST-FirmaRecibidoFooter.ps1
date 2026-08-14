# Fix-ORDMEDITEST-FirmaRecibidoFooter.ps1
# Reestructura la seccion Verificacion de ORD-MEDI-TEST y agrega bloques
# de firma del especialista, recibido y footer para que se parezca al
# formato Bioflie de la imagen.
#
# Cambios:
#   - Seccion 'Verificacion' -> renombrada a 'Firma y Verificacion' con
#     3 columnas: Firma profesional + Nombre + Registro | QR (centro) |
#     Recibido / Telefono / Direccion (derecha).
#   - Nueva seccion 'Pie de impresion' con footer.
#
# Backup previo en scratchpad.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev",
    [string]$BackupRoot  = "C:\Users\acuartas\AppData\Local\Temp\claude\C--DesarrolloIA-Visal\3a114262-030a-4135-852f-4f6e57a10abf\scratchpad"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }
function P([string]$content) { @{ id=newId; type="text"; textStyle="paragraph"; content=$content } }
function SH([string]$content) { @{ id=newId; type="text"; textStyle="subheading"; content=$content } }
function Field([string]$label, [string]$name, [string]$ft, [int]$width=12, $extra=@{}) {
    $f = @{ id=newId; type="field"; fieldType=$ft; label=$label; name=$name; widthColumns=$width; allowCustom=$false; required=$false }
    foreach ($k in $extra.Keys) { $f[$k] = $extra[$k] }
    return $f
}

$stamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
$bkPath = Join-Path $BackupRoot ("backup_ordmeditest_$stamp.json")
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='ORD-MEDI-TEST' AND tenant_id='$TenantId';"
if ([string]::IsNullOrWhiteSpace($raw)) { throw "ORD-MEDI-TEST no existe" }
[System.IO.File]::WriteAllText($bkPath, $raw, [System.Text.UTF8Encoding]::new($false))
Write-Host "Backup: $bkPath" -ForegroundColor DarkGray

$schema = $raw | ConvertFrom-Json -AsHashtable

# Localizar la seccion Verificacion existente para preservar el QR (con su name y config)
$qrNode = $null
$firmaUrlNode = $null
foreach ($sec in $schema.children) {
    if (([string]$sec["label"]) -eq "Verificacion" -or ([string]$sec["label"]) -eq "Firma y Verificacion") {
        foreach ($c in $sec.children) {
            if (([string]$c["fieldType"]) -eq "qr") { $qrNode = $c }
            elseif (([string]$c["name"]) -eq "firma_profesional") { $firmaUrlNode = $c }
        }
    }
}
if ($null -eq $qrNode) { throw "No encontre nodo QR en ORD-MEDI-TEST" }

# Ajustar QR a widthColumns=4 (queda centrado entre los 2 bloques)
$qrNode["widthColumns"] = 4

# Preservar el field firma_profesional (URL). Si no existe lo crea.
if ($null -eq $firmaUrlNode) {
    $firmaUrlNode = Field "Firma profesional (URL)" "firma_profesional" "text" 4
} else {
    $firmaUrlNode["widthColumns"] = 4
    $firmaUrlNode["label"] = "Firma profesional (URL)"
}

# ============ Nueva seccion Firma y Verificacion (3 columnas) ============
$secFirmaVer = @{
    id = newId; type = "section"; label = "Firma y Verificacion"
    children = @(
        # --- Bloque izquierda: firma + nombre + registro (col 4)
        $firmaUrlNode,
        # QR centro
        $qrNode,
        # Bloque derecha: recibido / telefono / direccion
        (Field "Recibido"  "recibido_por"   "text" 4),
        # --- Segunda fila: metadatos del profesional bajo la firma + telefono bajo Recibido
        (Field "Nombre del profesional" "profesional_nombre" "text" 4),
        (P " "),
        (Field "Teléfono" "recibido_telefono" "text" 4),
        # --- Tercera fila: registro medico + espacio + direccion
        (Field "R.M. Registro Médico" "profesional_registro" "text" 4),
        (P " "),
        (Field "Dirección" "recibido_direccion" "text" 4)
    )
}

# ============ Nueva seccion Pie de impresion ============
$secPie = @{
    id = newId; type = "section"; label = "Pie de impresión"
    children = @(
        (P "Impreso el {fecha_impresion} — Software para el sector salud — VISAL RT S.A.S.")
    )
}

# Rebuild children: reemplazar Verificacion por secFirmaVer, agregar Pie al final
$newSecs = New-Object System.Collections.ArrayList
foreach ($sec in $schema.children) {
    $lbl = [string]$sec["label"]
    if ($lbl -eq "Verificacion" -or $lbl -eq "Firma y Verificacion") {
        [void]$newSecs.Add($secFirmaVer)
    } elseif ($lbl -eq "Pie de impresión") {
        # skip; se reagrega al final
    } else {
        [void]$newSecs.Add($sec)
    }
}
[void]$newSecs.Add($secPie)
$schema["children"] = $newSecs.ToArray()

# Persistir
$json = ($schema | ConvertTo-Json -Depth 40 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='ORD-MEDI-TEST' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_ordfix_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK ORD-MEDI-TEST actualizado." -ForegroundColor Green
