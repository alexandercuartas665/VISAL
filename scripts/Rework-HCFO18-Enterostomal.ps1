# Rework-HCFO18-Enterostomal.ps1
# HC-FO-18: reemplaza el bloque [17..109] del Encabezado por una estructura
# nueva con tablas seed segun docx (AREA AFECTADA, CLASIFICACION HERIDA
# QUIR/NO QUIR, PROFUNDIDAD, TAMANO, TEJIDO, EXUDADO, PIEL CIRC, BORDES,
# OLOR, CONDUCTA, escalas Braden/Morse, ANALISIS, META, PLAN, FOTO).
#
# CONSERVA intacto:
#   - [0..14]  datos personales
#   - [15..16] DIAGNOSTICOS (subheading + tabla)
#   - [110..113] campos legacy PROFESIONAL/FIRMA/NOMBRE/DOC
#   - seccion "Cierre" (children[1]) y seccion top-level "MEDICO" (children[2])
#
# NO TOCA HC-FO-08.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

function Col([string]$label, [string]$name, [string]$ft, $extra = @{}) {
    $c = @{ id=newId; label=$label; name=$name; fieldType=$ft; allowCustom=$false }
    foreach ($k in $extra.Keys) { $c[$k] = $extra[$k] }
    return $c
}
function Tabla([string]$label, [string]$name, $cols, $seedRows, [bool]$lockRows, [int]$widthColumns=12) {
    # Fix bug PS: garantizar seedRows como array-de-arrays incluso con 1 fila
    $rowsArr = New-Object System.Collections.ArrayList
    foreach ($r in $seedRows) {
        $celdas = New-Object System.Collections.ArrayList
        foreach ($v in $r) { [void]$celdas.Add($v) }
        $arr = $celdas.ToArray()
        [void]$rowsArr.Add($arr)
    }
    return @{
        id=newId; type="field"; fieldType="table"
        label=$label; name=$name; widthColumns=$widthColumns
        columns=$cols; seedRows=$rowsArr.ToArray()
        lockRows=$lockRows; allowCustom=$false
        isSection=$false; isText=$false; isTable=$true; required=$false
    }
}
function SH([string]$content) { @{ id=newId; type="text"; textStyle="subheading"; content=$content } }
function Field([string]$label, [string]$name, [string]$ft, [int]$width=12, $extra=@{}) {
    $f = @{ id=newId; type="field"; fieldType=$ft; label=$label; name=$name; widthColumns=$width; allowCustom=$false; required=$false }
    foreach ($k in $extra.Keys) { $f[$k] = $extra[$k] }
    return $f
}

# =========== Column sets ===========
$cols2ItemPresente = @(
    (Col "Ítem"     "item"     "text"),
    (Col "Presente" "presente" "text")
)
$cols4AreaPresente = @(
    (Col "Área"     "area_1"     "text"),
    (Col "Presente" "presente_1" "text"),
    (Col "Área"     "area_2"     "text"),
    (Col "Presente" "presente_2" "text")
)
$cols4Heridas = @(
    (Col "Herida Aguda"  "herida_aguda"    "text"),
    (Col "Presente"      "presente_aguda"  "text"),
    (Col "Herida Crónica" "herida_cronica" "text"),
    (Col "Presente"      "presente_cronica" "text")
)
$cols4Tejido = @(
    (Col "Tejido" "tejido_1"     "text"),
    (Col "%"      "porcentaje_1" "text"),
    (Col "Tejido" "tejido_2"     "text"),
    (Col "%"      "porcentaje_2" "text")
)
$cols2ItemObs = @(
    (Col "Ítem"        "item"        "text"),
    (Col "Observación" "observacion" "text")
)

# =========== SeedRows ===========
$rowsArea = @(
    @("Cabeza","","Glúteo",""),
    @("Cara","","Sacro",""),
    @("Cuello","","Trocánter derecho",""),
    @("Tórax anterior","","Trocánter izquierdo",""),
    @("Espalda","","Genitales",""),
    @("Abdomen","","Miembro inferior izquierdo",""),
    @("Brazo derecho","","Miembro inferior derecho",""),
    @("Brazo izquierdo","","Rodilla derecha",""),
    @("Antebrazo derecho","","Rodilla izquierda",""),
    @("Antebrazo izquierdo","","Maléolo lateral derecho",""),
    @("Mano derecha","","Isquion derecho",""),
    @("Mano izquierda","","Isquion izquierdo","")
)
$rowsQuir = @(
    @("Incisiones","","Herida quirúrgica",""),
    @("Escisiones","","Heridas infectadas",""),
    @("Zonas donantes para injerto","","","")
)
$rowsNoQuir = @(
    @("Quemaduras","","Ulceras por presión",""),
    @("Abrasiones","","Pie diabético",""),
    @("Desgarros de piel","","","")
)
$rowsProfundidad = @(
    @("Superficial",""),
    @("Grosor Parcial",""),
    @("Grosor Total","")
)
$rowsTejido = @(
    @("Granulación","","Epitelización",""),
    @("Necrótico","","Hiper granulación",""),
    @("Fibrinógeno","","","")
)
$rowsCantExud = @(
    @("Seco, mínimo",""),
    @("Ligeramente húmedo",""),
    @("Moderadamente húmedo",""),
    @("Abundantemente mojado","")
)
$rowsTipoExud = @(
    @("Seroso",""),
    @("Serosanguinolento",""),
    @("Sanguinolento Purulento","")
)
$rowsPielCirc = @(
    @("Sana normal",""),
    @("Enrojecimiento / palidez",""),
    @("Eritema",""),
    @("Hiperpigmentación",""),
    @("Maceración",""),
    @("Eritema no blanqueable",""),
    @("Otro","")
)
$rowsBordes = @(
    @("No definido",""),
    @("Adherido",""),
    @("No adherido",""),
    @("Tunelización",""),
    @("Socavación",""),
    @("Enrollado por debajo",""),
    @("Bien definido fibrótico","")
)
$rowsOlor = @(
    @("Después de la curación",""),
    @("Presente",""),
    @("Ausente","")
)
$rowsConducta = @(
    @("Autolítico",""),
    @("Enzimático",""),
    @("Quirúrgico",""),
    @("Otro","")
)

# =========== Ensamblar bloque nuevo ===========
$bloque = @(
    (SH "1. AREA Y/O AREAS AFECTADAS"),
    (Tabla "Áreas afectadas" "areas_afectadas" $cols4AreaPresente $rowsArea $true),

    (SH "2. CLASIFICACION DE LA HERIDA"),
    (SH "QUIRÚRGICAS"),
    (Tabla "Heridas quirúrgicas" "heridas_quirurgicas" $cols4Heridas $rowsQuir $true),
    (SH "NO QUIRÚRGICAS"),
    (Tabla "Heridas no quirúrgicas" "heridas_no_quirurgicas" $cols4Heridas $rowsNoQuir $true),

    (SH "3. CLASIFICACION DE LA PROFUNDIDAD"),
    (Tabla "Profundidad" "profundidad" $cols2ItemObs $rowsProfundidad $true),

    (SH "4. MEDICION DE LA HERIDA"),
    (SH "TAMAÑO"),
    (Field "Largo en cm"       "largo_cm"      "number" 4),
    (Field "Ancho en cm"       "ancho_cm"      "number" 4),
    (Field "Profundidad en cm" "profundidad_cm" "number" 4),

    (SH "5. TIPO DE TEJIDO Y PORCENTAJE (La sumatoria de los porcentajes debe totalizar 100%)"),
    (Tabla "Tipo de tejido" "tipo_tejido" $cols4Tejido $rowsTejido $true),

    (SH "6. CANTIDAD DE EXUDADO"),
    (SH "CANTIDAD"),
    (Tabla "Cantidad de exudado" "cantidad_exudado" $cols2ItemPresente $rowsCantExud $true),
    (SH "TIPO DE EXUDADO"),
    (Tabla "Tipo de exudado" "tipo_exudado" $cols2ItemPresente $rowsTipoExud $true),

    (SH "7. PIEL CIRCUNDANTE (Evaluación visual y palpación del área)"),
    (Tabla "Piel circundante" "piel_circundante" $cols2ItemPresente $rowsPielCirc $true),

    (SH "8. BORDES DE LA HERIDA"),
    (Tabla "Bordes de la herida" "bordes_herida" $cols2ItemPresente $rowsBordes $true),
    (SH "OLOR"),
    (Tabla "Olor" "olor" $cols2ItemPresente $rowsOlor $true),

    (SH "9. CONDUCTA"),
    (Tabla "Conducta" "conducta" $cols2ItemPresente $rowsConducta $true),

    (SH "10. PUNTAJE ESCALA DE BRADEN"),
    (Field "Puntaje Braden"       "puntaje_braden"       "number" 4),
    (Field "Observación Braden"   "obs_braden"           "textarea" 8),

    (SH "11. PUNTAJE ESCALA DE MORSE"),
    (Field "Puntaje Morse"        "puntaje_morse"        "number" 4),
    (Field "Observación Morse"    "obs_morse"            "textarea" 8),

    (SH "12. ANALISIS"),
    (Field "Análisis" "analisis" "textarea" 12),

    (SH "13. META TERAPÉUTICA Y/O OBJETIVO TERAPEUTICO"),
    (Field "Meta terapéutica" "meta_terapeutica" "textarea" 12),

    (SH "14. PLAN DE MANEJO"),
    (Field "Plan de manejo" "plan_manejo" "textarea" 12),

    (SH "15. FOTOGRAFÍA DE SEGUIMIENTO"),
    (Field "URL o descripción de la fotografía" "fotografia_seguimiento" "textarea" 12)
)

# =========== Aplicar ===========
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-18' AND tenant_id='$TenantId';"
$schema = $raw | ConvertFrom-Json -AsHashtable
$enc = $schema.children[0]
$start = 17; $end = 109
Write-Host ("Rango a reemplazar: [{0}..{1}] = {2} nodos" -f $start, $end, ($end-$start+1)) -ForegroundColor Yellow

$head = $enc.children[0..($start - 1)]
$tail = $enc.children[($end + 1)..($enc.children.Count - 1)]
$enc.children = @($head) + @($bloque) + @($tail)
$schema.children[0] = $enc
Write-Host ("Encabezado ahora: {0} hijos (antes 114). Bloque nuevo: {1} nodos" -f $enc.children.Count, $bloque.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='HC-FO-18' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_ent_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK HC-FO-18 actualizado." -ForegroundColor Green
