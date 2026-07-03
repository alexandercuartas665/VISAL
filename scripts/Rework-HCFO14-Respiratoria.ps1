# Rework-HCFO14-Respiratoria.ps1
# HC-FO-14: reemplaza el bloque [30..82] del Encabezado (PLAN + EVALUACION +
# ACTIVIDAD) por una estructura nueva con tablas seed segun docx.
#
# CONSERVA intacto:
#   - [0..14]  datos personales
#   - [15..16] DIAGNOSTICOS (subheading + tabla)
#   - [17..29] ANAMNESIS + MOTIVO + SIGNOS VITALES (header segun user)
#   - [83..86] campos legacy MEDICO/FIRMA/NOMBRE/DOC
#   - seccion "Cierre" (children[1])
#   - seccion top-level "MEDICO" (children[2])
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
    # Fix del bug: garantizar seedRows como array-de-arrays incluso con 1 fila
    $rowsArr = New-Object System.Collections.ArrayList
    foreach ($r in $seedRows) {
        $celdas = New-Object System.Collections.ArrayList
        foreach ($v in $r) { [void]$celdas.Add($v) }
        [void]$rowsArr.Add($celdas.ToArray())
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

# Column set reutilizado: 2 col Item | Observacion
$colsItemObs = @(
    (Col "Ítem"        "item"        "text"),
    (Col "Observación" "observacion" "text")
)

# ---- Definir seedRows por seccion ----
$rowsSignosDif = @( @("Aleteo Nasal",""), @("Tiraje Subcostal",""), @("Braquipnea",""),
                    @("Cianosis",""), @("Taquipnea",""), @("Soporte Oxígeno","") )
$rowsPatron    = @( @("Torácico",""), @("Abdominal",""), @("Mixto",""), @("D. Ritmo Respiratorio","") )
$rowsInspeccion= @( @("Llenado Capilar",""), @("Movilidad torácica",""), @("Expansibilidad torácica","") )
$rowsPalpacion = @( @("Masas",""), @("Fracturas",""), @("Puntos dolorosos","") )
$rowsAuscult   = @( @("Ruidos Auscultados",""), @("Murmullo Vesicular",""), @("Ruidos Sobreagregados","") )
$rowsPercusion = @( @("Resonancia",""), @("Hiperresonancia",""), @("Matidez",""), @("Submatidez","") )
$rowsActividad = @( @("Percusión",""), @("Aspiración",""), @("Limpieza Nasal",""),
                    @("Movilizaciones tóxicas",""), @("Tos Dirigida",""), @("Tos Provocada",""),
                    @("Tos Asistida",""), @("Vibración",""), @("Fortalecimiento","") )

# FRECUENCIAS: 3 col (Item | Llegada | Salida)
$colsFrec = @(
    (Col "Ítem"    "item"    "text"),
    (Col "Llegada" "llegada" "text"),
    (Col "Salida"  "salida"  "text")
)
$rowsFrec = @( @("Frecuencia Cardiaca","",""), @("Frecuencia Respiratoria","","") )

# BRONCODILATADOR: 4 col (Item | Dosis | Temperatura | Grados)
$colsBronco = @(
    (Col "Ítem"        "item"        "text"),
    (Col "Dosis"       "dosis"       "text"),
    (Col "Temperatura" "temperatura" "text"),
    (Col "Grados"      "grados"      "text")
)
$rowsBronco = @( @("Broncodilatador","","","") )

# ============ Ensamblar bloque nuevo ============
$bloque = @(
    (SH "PLAN DE TRATAMIENTO"),
    (Field "Plan de tratamiento" "plan_tratamiento" "textarea" 12),
    (SH "OBJETIVOS DEL TRATAMIENTO"),
    (Field "Objetivos del tratamiento" "objetivos_tratamiento" "textarea" 12),
    (SH "CONCLUSIONES"),
    (Field "Conclusiones" "conclusiones" "textarea" 12),

    (SH "EVALUACIÓN"),
    (SH "OBSERVACIÓN"),

    (SH "SIGNOS DE DIFICULTAD RESPIRATORIA"),
    (Tabla "Signos de dificultad respiratoria" "signos_dif_respiratoria" $colsItemObs $rowsSignosDif $true),

    (SH "PATRÓN RESPIRATORIO"),
    (Tabla "Patrón respiratorio" "patron_respiratorio" $colsItemObs $rowsPatron $true),

    (SH "INSPECCIÓN"),
    (Tabla "Inspección" "inspeccion" $colsItemObs $rowsInspeccion $true),

    (SH "PALPACIÓN"),
    (Tabla "Palpación" "palpacion" $colsItemObs $rowsPalpacion $true),

    (SH "AUSCULTACIÓN"),
    (Tabla "Auscultación" "auscultacion" $colsItemObs $rowsAuscult $true),

    (SH "PERCUSIÓN"),
    (Tabla "Percusión" "percusion" $colsItemObs $rowsPercusion $true),

    (Field "Nebulización" "nebulizacion" "select" 4 @{ options=@("Si","No") }),
    (Field "Tipo de Tos" "tipo_tos" "text" 4),
    (Field "Tipo de secreción" "tipo_secrecion" "text" 4),

    (SH "FRECUENCIA CARDIACA / FRECUENCIA RESPIRATORIA"),
    (Tabla "Frecuencias" "frecuencias" $colsFrec $rowsFrec $true),

    (SH "BRONCODILATADOR"),
    (Tabla "Broncodilatador" "broncodilatador" $colsBronco $rowsBronco $true),

    (SH "ACTIVIDAD"),
    (Tabla "Actividad" "actividad" $colsItemObs $rowsActividad $true)
)

# ============ Aplicar ============
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-14' AND tenant_id='$TenantId';"
$schema = $raw | ConvertFrom-Json -AsHashtable
$enc = $schema.children[0]
$start = 30; $end = 82
Write-Host ("Rango a reemplazar: [{0}..{1}] = {2} nodos" -f $start, $end, ($end-$start+1)) -ForegroundColor Yellow

$head = $enc.children[0..($start - 1)]
$tail = $enc.children[($end + 1)..($enc.children.Count - 1)]
$enc.children = @($head) + @($bloque) + @($tail)
$schema.children[0] = $enc
Write-Host ("Encabezado ahora: {0} hijos (antes 87). Bloque nuevo: {1} nodos" -f $enc.children.Count, $bloque.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='HC-FO-14' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_resp_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK HC-FO-14 actualizado." -ForegroundColor Green
