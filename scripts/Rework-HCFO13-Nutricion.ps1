# Rework-HCFO13-Nutricion.ps1
# Reemplaza en HC-FO-13 el bloque [17..128] del Encabezado por una
# estructura nueva con tablas seed (defaults "NO REFIERE" donde aplica)
# segun el docx original de nutricion.
#
# CONSERVA intacto:
#   - [0..14]  datos personales
#   - [15..16] DIAGNOSTICOS (subheading + tabla)
#   - [129..132] los 4 campos redundantes MEDICO/FIRMA/NOMBRE/DOC (por si el
#                usuario los mantiene)
#   - seccion top-level "MEDICO" (children[2])
#   - seccion "Cierre" (children[1])
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

# Helper: crea columna
function Col([string]$label, [string]$name, [string]$ft, $extra = @{}) {
    $c = @{ id=newId; label=$label; name=$name; fieldType=$ft; allowCustom=$false }
    foreach ($k in $extra.Keys) { $c[$k] = $extra[$k] }
    return $c
}
# Helper: crea tabla
function Tabla([string]$label, [string]$name, $cols, $seedRows, [bool]$lockRows, [int]$widthColumns=12) {
    return @{
        id=newId; type="field"; fieldType="table"
        label=$label; name=$name; widthColumns=$widthColumns
        columns=$cols; seedRows=$seedRows
        lockRows=$lockRows; allowCustom=$false
        isSection=$false; isText=$false; isTable=$true; required=$false
    }
}
function SH([string]$content) {
    return @{ id=newId; type="text"; textStyle="subheading"; content=$content }
}
function P([string]$content) {
    return @{ id=newId; type="text"; textStyle="paragraph"; content=$content }
}
function Field([string]$label, [string]$name, [string]$ft, [int]$width=12, $extra=@{}) {
    $f = @{ id=newId; type="field"; fieldType=$ft; label=$label; name=$name; widthColumns=$width; allowCustom=$false; required=$false }
    foreach ($k in $extra.Keys) { $f[$k] = $extra[$k] }
    return $f
}

# ============ Contenido nuevo del bloque [17..128] ============

$colsItemObs = @(
    (Col "Ítem"        "item"        "text"),
    (Col "Observación" "observacion" "text" @{ defaultValue = "NO REFIERE" })
)

$rowsAF = @(
    @("Hipertensión Arterial", "NO REFIERE"),
    @("Diabetes",              "NO REFIERE"),
    @("Cáncer",                "NO REFIERE"),
    @("Otros",                 "")
)
$rowsAHF = @(
    @("Diabetes Mellitus",       "NO REFIERE"),
    @("Obesidad",                "NO REFIERE"),
    @("Cardiopatías",            "NO REFIERE"),
    @("Dislipidemias",           "NO REFIERE"),
    @("Cáncer",                  "NO REFIERE"),
    @("Hipertensión",            "NO REFIERE"),
    @("Enfermedades Tiroideas",  "NO REFIERE"),
    @("Alergias",                "NO REFIERE"),
    @("Otro",                    "")
)

# PROBLEMAS ACTUALES (APP) - sintomas: 4 col (Item | Presente | Item | Presente)
$colsSintomas4 = @(
    (Col "Ítem"     "item_1"     "text"),
    (Col "Presente" "presente_1" "text"),
    (Col "Ítem"     "item_2"     "text"),
    (Col "Presente" "presente_2" "text")
)
$rowsSintomas = @(
    @("Diarrea",           "", "Vómito",     ""),
    @("Colitis",           "", "Pirosis",    ""),
    @("Estreñimiento",     "", "Dentadura",  ""),
    @("Náuseas",           "", "Ulcera",     ""),
    @("Gastritis",         "", "",           ""),
    @("Otros - ¿Cuáles?",  "", "",           "")
)

# APP - preguntas: 4 col (Item | Si | No | Detalle)
$colsPreguntas = @(
    (Col "Ítem"    "item"    "text"),
    (Col "Si"      "si"      "text"),
    (Col "No"      "no"      "text"),
    (Col "Detalle" "detalle" "text")
)
$rowsPreguntas = @(
    @("¿Le han practicado alguna cirugía?",                                                     "", "", ""),
    @("¿Toma algún medicamento? ¿cuál (es) y en qué dosis?",                                    "", "", ""),
    @("¿Desde cuándo?",                                                                          "", "", ""),
    @("Signos (aspecto general: cabello, ojos, piel, uñas, labios, encías, etc.)",              "", "", ""),
    @("¿Había llevado tratamiento nutriológico antes, con quién y cómo se sintió?",             "", "", "")
)

# INDICADORES BIOQUIMICOS - primera tabla (Item | Si | No | Cuales)
$colsBioq1 = @(
    (Col "Ítem"     "item"     "text"),
    (Col "Si"       "si"       "text"),
    (Col "No"       "no"       "text"),
    (Col "¿Cuáles?" "cuales"   "text")
)
$rowsBioq1 = @(
    @("Se realizaron exámenes", "", "", "")
)

# INDICADORES BIOQUIMICOS - examenes (Examenes | Si | No | Resultado)
$colsBioq2 = @(
    (Col "Exámenes" "examen"     "text"),
    (Col "Si"       "si"         "text"),
    (Col "No"       "no"         "text"),
    (Col "Resultado" "resultado" "text")
)
$rowsBioq2 = @(
    @("Glicemia",     "", "", ""),
    @("Creatinina",   "", "", ""),
    @("Triglicéridos","", "", ""),
    @("Ácido Úrico",  "", "", ""),
    @("Colesterol",   "", "", ""),
    @("HDL",          "", "", ""),
    @("LDL",          "", "", ""),
    @("Hemograma",    "", "", "")
)

# INDICADORES ANTROPOMETRICOS (Fecha | Peso Actual | IMC | % grasa | % musculo | Dieta) - dinamica
$colsAntropo = @(
    (Col "Fecha"        "fecha"     "date"),
    (Col "Peso Actual"  "peso"      "number"),
    (Col "IMC"          "imc"       "number"),
    (Col "% de grasa"   "grasa"     "text"),
    (Col "% de músculo" "musculo"   "text"),
    (Col "Dieta"        "dieta"     "text")
)
$rowsAntropo = @( @("", "", "", "", "", "") )

# INDICADORES DIETETICOS: 2 col Indicadores | Observaciones
$colsIndObs = @(
    (Col "Indicadores"   "indicador"   "text"),
    (Col "Observaciones" "observacion" "text")
)
$rowsDieteticos = @(
    @("Desayuno",     ""),
    @("Media Mañana", ""),
    @("Almuerzo",     ""),
    @("Media Tarde",  ""),
    @("Cena",         "")
)

# CONSUMO DE ALIMENTOS: 4 col (Alimento | Cantidad | Alimento | Cantidad)
$colsConsumo = @(
    (Col "Alimento" "alimento_1" "text"),
    (Col "Cantidad" "cantidad_1" "text"),
    (Col "Alimento" "alimento_2" "text"),
    (Col "Cantidad" "cantidad_2" "text")
)
$rowsConsumo = @(
    @("Carne",    "", "Bolillo",        ""),
    @("Huevos",   "", "Cereal",         ""),
    @("Queso",    "", "Pan dulce",      ""),
    @("Pescado",  "", "Pan integral",   ""),
    @("Pollo",    "", "Sopa de pastas", ""),
    @("Frijoles", "", "Verduras",       ""),
    @("Tortilla", "", "Frutas",         "")
)

# HABITOS: 2 col Habitos | Observaciones
$colsHabitos = @(
    (Col "Hábitos"       "habito"      "text"),
    (Col "Observaciones" "observacion" "text")
)
$rowsHabitos = @(
    @("Alcohol",   ""),
    @("Tabaco",    ""),
    @("Café o té", ""),
    @("Refresco",  "")
)

# ============ Ensamblar el bloque nuevo ============
$bloque = @(
    (SH "ANAMNESIS"),
    (SH "MOTIVO DE CONSULTA"),
    (Field "Motivo de consulta" "motivo_consulta" "textarea" 12),

    (SH "ANTECEDENTES FAMILIARES"),
    (Tabla "Antecedentes familiares" "antecedentes_familiares" $colsItemObs $rowsAF $true),

    (SH "ANTECEDENTES SALUD / ENFERMEDAD (AHF)"),
    (Tabla "Antecedentes salud / enfermedad" "antecedentes_ahf" $colsItemObs $rowsAHF $true),

    (SH "PROBLEMAS ACTUALES (APP)"),
    (Tabla "Síntomas" "app_sintomas" $colsSintomas4 $rowsSintomas $true),
    (Tabla "Preguntas" "app_preguntas" $colsPreguntas $rowsPreguntas $true),

    (SH "INDICADORES BIOQUÍMICOS"),
    (Tabla "Se realizaron exámenes" "bioq_realizados" $colsBioq1 $rowsBioq1 $true),
    (Tabla "Exámenes" "bioq_examenes" $colsBioq2 $rowsBioq2 $true),
    (Field "Observaciones" "observaciones_bioq" "textarea" 12),

    (SH "INDICADORES ANTROPOMÉTRICOS"),
    (Tabla "Antropometría" "antropometria" $colsAntropo $rowsAntropo $false),

    (SH "INDICADORES DIETÉTICOS"),
    (Tabla "Indicadores dietéticos" "indicadores_dieteticos" $colsIndObs $rowsDieteticos $true),
    (Field "¿En dónde?" "en_donde_1" "text" 6),
    (Field "¿Quién prepara sus alimentos?" "quien_prepara" "text" 6),
    (Field "¿Ha modificado su alimentación en los últimos 6 meses?" "modificado_alimentacion" "select" 4 @{ options=@("Si","No") }),
    (Field "¿Por qué?" "por_que_modificacion" "text" 4),
    (Field "¿En dónde?" "en_donde_2" "text" 4),

    (SH "CONSUMO DE ALIMENTOS"),
    (Tabla "Consumo de alimentos" "consumo_alimentos" $colsConsumo $rowsConsumo $true),

    (Field "¿Consume leche?" "consume_leche" "select" 3 @{ options=@("Si","No") }),
    (Field "Tipo" "tipo_leche" "text" 3),
    (Field "¿Agrega sal a la comida ya preparada?" "agrega_sal" "select" 6 @{ options=@("Si","No") }),
    (Field "Si tuviera que elegir entre un alimento salado y uno dulce, ¿cuál elegiría?" "prefiere_salado_dulce" "select" 6 @{ options=@("Dulce","Salado") }),
    (Field "¿Qué grasa utilizan en casa para preparar su comida?" "tipo_grasa" "select" 6 @{ options=@("Margarina","Aceite vegetal","Manteca","Mantequilla","Otros") }),
    (Field "¿Cuántos vasos de agua (240ml) consume en promedio diariamente?" "vasos_agua" "number" 4),
    (Field "¿Cómo es su apetito?" "apetito" "select" 4 @{ options=@("Bueno","Regular","Malo") }),
    (Field "¿Su consumo varía cuando esta triste, nervioso o ansioso?" "consumo_ansiedad" "select" 4 @{ options=@("Si","No") }),
    (Field "¿A qué hora tiene más hambre?" "hora_hambre" "text" 4),
    (Field "¿Cómo varía?" "como_varia" "text" 4),
    (Field "Alimentos preferidos" "alimentos_preferidos" "textarea" 12),
    (Field "Alimentos que no le agradan / no acostumbra a consumir" "alimentos_no_agradan" "textarea" 12),
    (Field "Alimentos a los que es alérgico o le causan malestar" "alimentos_alergicos" "textarea" 12),
    (Field "Toma algún suplemento / complemento" "suplementos" "textarea" 12),
    (Field "Alergias" "alergias" "select" 4 @{ options=@("Si","No") }),
    (Field "Ansiedad" "ansiedad" "select" 4 @{ options=@("Si","No") }),
    (Field "Horas de sueño" "horas_sueno" "number" 4),
    (Field "Frecuencia de la actividad física" "frecuencia_actividad" "text" 6),
    (Field "Duración de la actividad física" "duracion_actividad" "text" 6),

    (SH "HÁBITOS"),
    (Tabla "Hábitos" "habitos" $colsHabitos $rowsHabitos $true),

    (SH "ANÁLISIS"),
    (Field "Análisis" "analisis" "textarea" 12),

    (SH "PLAN"),
    (Field "Plan" "plan" "textarea" 12)
)

# ============ Aplicar ============
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='HC-FO-13' AND tenant_id='$TenantId';"
$schema = $raw | ConvertFrom-Json -AsHashtable

$encIdx = 0
$enc = $schema.children[$encIdx]
$start = 17; $end = 128
Write-Host ("Rango a reemplazar: [{0}..{1}] = {2} nodos" -f $start, $end, ($end-$start+1)) -ForegroundColor Yellow

$head = $enc.children[0..($start - 1)]
$tail = $enc.children[($end + 1)..($enc.children.Count - 1)]
$enc.children = @($head) + @($bloque) + @($tail)
$schema.children[$encIdx] = $enc
Write-Host ("Encabezado ahora: {0} hijos (antes 133). Bloque nuevo: {1} nodos" -f $enc.children.Count, $bloque.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='HC-FO-13' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_nutricion_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK HC-FO-13 actualizado." -ForegroundColor Green
