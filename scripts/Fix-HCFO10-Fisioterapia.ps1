# Fix-HCFO10-Fisioterapia.ps1
# Reemplaza el schema_json de HC-FO-10 desde TEST MOVILIDAD ARTICULAR en
# adelante por la estructura real del docx PP-FO-... HC-FO-10 Fisioterapia.
# Mantiene intactas las secciones anteriores (Datos paciente, Anamnesis,
# Revision sistemas, Medidas antropometricas, Examen fisico SIN AMAS/Fuerza).

[CmdletBinding()]
param(
    [string]$TenantId = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$Codigo = "HC-FO-10",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser = "visal",
    [string]$PgDb = "visal_dev"
)
$ErrorActionPreference = "Stop"

function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

# 1) Traer schema actual desde BD
$json = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='$Codigo' AND tenant_id='$TenantId';"
if (-not $json) { throw "HC-FO-10 no encontrado." }
$schema = $json | ConvertFrom-Json -AsHashtable

# 2) Filtrar children: dejar solo las secciones que el user marco como OK.
# Conservamos las que llamaban "Datos del paciente", "Anamnesis",
# "Revision por sistemas", "Medidas antropometricas", "Examen fisico".
# Pero del Examen fisico SACAMOS los campos AMAS y Fuerza (se reemplazan por
# las tablas matriciales nuevas en una seccion aparte).
$keepLabels = @(
    "Datos del paciente","Anamnesis","Revision por sistemas",
    "Medidas antropometricas","Examen fisico"
)
$newChildren = @()
foreach ($s in $schema.children) {
    if ($s.type -ne 'section') { continue }
    $lbl = ($s.label ?? '').Trim()
    if ($keepLabels -notcontains $lbl) { continue }
    # Para Examen fisico, sacar AMAS y Fuerza textareas (se reemplazan)
    if ($lbl -eq "Examen fisico") {
        $s.children = @($s.children | Where-Object {
            -not (($_.type -eq 'field') -and ($_.label -match '^(AMAS|Fuerza)\b'))
        })
    }
    $newChildren += $s
}

# 3) Construir las NUEVAS secciones a partir de TEST MOVILIDAD ARTICULAR.

# --- 3.1 TEST MOVILIDAD ARTICULAR (tabla con 11 articulaciones, locked) ---
$movCols = @(
    @{ id = newId; label = "Articulacion";        name = "articulacion";   fieldType = "text" },
    @{ id = newId; label = "Valoracion inicial";  name = "v_inicial";      fieldType = "text" },
    @{ id = newId; label = "Valoracion final";    name = "v_final";        fieldType = "text" }
)
$movSeed = @(
    @(,"CERVICAL"),
    @(,"DORSO - LUMBAR"),
    @(,"HOMBROS"),
    @(,"CODO Y ANTEBRAZO"),
    @(,"MUNECA"),
    @(,"DEDOS MANO"),
    @(,"PULGAR"),
    @(,"CADERA"),
    @(,"RODILLA"),
    @(,"TOBILLO"),
    @(,"DEDOS PIE")
)
$movTable = @{
    id = newId; type = "field"; fieldType = "table"
    label = "Test de movilidad articular"
    name = "test_movilidad_articular"
    widthColumns = 12
    columns = $movCols
    seedRows = $movSeed
    lockRows = $true
}
$secMovilidad = @{
    id = newId; type = "section"; label = "TEST MOVILIDAD ARTICULAR"
    children = @(
        @{ id = newId; type = "text"; textStyle = "paragraph";
           content = "Registre rangos articulares y limitaciones encontradas. Valore inicial y final." },
        $movTable
    )
}

# --- 3.2 FUERZA MUSCULAR (2 tablas separadas: SUP / INF, locked, escala MRC) ---
$mrcDescriptions = @(
    @("0","Ninguna respuesta muscular"),
    @("1","Musculo realiza contraccion visible/palpable sin movimiento"),
    @("2","Musculo realiza todo el movimiento sin gravedad/sin resistencia"),
    @("3","Musculo realiza todo el movimiento contra gravedad/sin resistencia"),
    @("4","Movimiento en toda amplitud contra gravedad + resistencia moderada"),
    @("5","Musculo soporta resistencia manual maxima, mov completo, contra gravedad")
)
$fzCols = @(
    @{ id = newId; label = "Grado";                name = "grado";          fieldType = "text" },
    @{ id = newId; label = "Descripcion";          name = "descripcion";    fieldType = "text" },
    @{ id = newId; label = "Valoracion inicial";   name = "v_inicial";      fieldType = "text" },
    @{ id = newId; label = "Valoracion final";     name = "v_final";        fieldType = "text" }
)
$fzSeedSup = @(
    @("0","Ninguna respuesta muscular"),
    @("1","Musculo realiza contraccion visible/palpable sin movimiento"),
    @("2","Musculo realiza todo el movimiento sin gravedad/sin resistencia"),
    @("3","Musculo realiza todo el movimiento contra gravedad/sin resistencia"),
    @("4","Movimiento en toda amplitud contra gravedad + resistencia moderada"),
    @("5","Musculo soporta resistencia manual maxima, mov completo, contra gravedad")
)
$fzSeedInf = $fzSeedSup  # mismas filas

$fzSupTable = @{
    id = newId; type = "field"; fieldType = "table"
    label = "Fuerza muscular - Miembros superiores"
    name = "fuerza_sup"
    widthColumns = 12
    columns = $fzCols
    seedRows = $fzSeedSup
    lockRows = $true
}
# Necesitamos columnas independientes para la tabla inferior (diferentes ids)
$fzColsInf = @(
    @{ id = newId; label = "Grado";                name = "grado";          fieldType = "text" },
    @{ id = newId; label = "Descripcion";          name = "descripcion";    fieldType = "text" },
    @{ id = newId; label = "Valoracion inicial";   name = "v_inicial";      fieldType = "text" },
    @{ id = newId; label = "Valoracion final";     name = "v_final";        fieldType = "text" }
)
$fzInfTable = @{
    id = newId; type = "field"; fieldType = "table"
    label = "Fuerza muscular - Miembros inferiores"
    name = "fuerza_inf"
    widthColumns = 12
    columns = $fzColsInf
    seedRows = $fzSeedInf
    lockRows = $true
}
$secFuerza = @{
    id = newId; type = "section"; label = "FUERZA MUSCULAR"
    children = @(
        @{ id = newId; type = "text"; textStyle = "paragraph";
           content = "Escala MRC (Medical Research Council) - 0 a 5. Marque la calificacion observada inicial y final." },
        @{ id = newId; type = "text"; textStyle = "subheading"; content = "Miembros superiores" },
        $fzSupTable,
        @{ id = newId; type = "text"; textStyle = "subheading"; content = "Miembros inferiores" },
        $fzInfTable
    )
}

# --- 3.3 VALORACION FISIOTERAPEUTICA (textarea) ---
$secValoracion = @{
    id = newId; type = "section"; label = "VALORACION FISIOTERAPEUTICA"
    children = @(
        @{ id = newId; type = "field"; fieldType = "textarea"
           label = "Valoracion fisioterapeutica"; name = "valoracion_fisioterapeutica"; widthColumns = 12 }
    )
}

# --- 3.4 DIAGNOSTICO FISIOTERAPEUTICO (tabla DOMINIO | PATRON, repetible) ---
$dxCols = @(
    @{ id = newId; label = "Dominio"; name = "dominio"; fieldType = "text" },
    @{ id = newId; label = "Patron";  name = "patron";  fieldType = "text" }
)
$dxTable = @{
    id = newId; type = "field"; fieldType = "table"
    label = "Diagnostico fisioterapeutico"
    name = "diagnostico_fisioterapeutico"
    widthColumns = 12
    columns = $dxCols
    # Sin seedRows: el especialista agrega filas segun necesidad
}
$secDx = @{
    id = newId; type = "section"; label = "DIAGNOSTICO FISIOTERAPEUTICO"
    children = @( $dxTable )
}

# --- 3.5 / 3.6 / 3.7 textareas largos ---
$secObjGen = @{
    id = newId; type = "section"; label = "OBJETIVO GENERAL"
    children = @(
        @{ id = newId; type = "field"; fieldType = "textarea"
           label = "Objetivo general"; name = "objetivo_general"; widthColumns = 12 }
    )
}
$secObjEsp = @{
    id = newId; type = "section"; label = "OBJETIVO ESPECIFICO"
    children = @(
        @{ id = newId; type = "field"; fieldType = "textarea"
           label = "Objetivo especifico"; name = "objetivo_especifico"; widthColumns = 12 }
    )
}
$secPlan = @{
    id = newId; type = "section"; label = "PLAN DE TRATAMIENTO"
    children = @(
        @{ id = newId; type = "field"; fieldType = "textarea"
           label = "Plan de tratamiento"; name = "plan_tratamiento"; widthColumns = 12 }
    )
}

# --- 3.8 FIRMA PROFESIONAL ---
$secFirma = @{
    id = newId; type = "section"; label = "PROFESIONAL Y FIRMA"
    children = @(
        @{ id = newId; type = "field"; fieldType = "text"; label = "Profesional";       name = "profesional_nombre"; widthColumns = 6; required = $true },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Registro / Tarjeta"; name = "profesional_registro"; widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Documento";         name = "profesional_documento"; widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Firma";             name = "profesional_firma";    widthColumns = 12 }
    )
}

# 4) Concatenar y construir el schema final.
$newChildren += $secMovilidad
$newChildren += $secFuerza
$newChildren += $secValoracion
$newChildren += $secDx
$newChildren += $secObjGen
$newChildren += $secObjEsp
$newChildren += $secPlan
$newChildren += $secFirma

$schema.children = $newChildren

$out = ($schema | ConvertTo-Json -Depth 30 -Compress)
$outSql = $out.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")

$sql = "UPDATE form_definitions SET schema_json = '$outSql'::jsonb, updated_at = '$now' WHERE codigo = '$Codigo' AND tenant_id = '$TenantId';"

$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_fix_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally {
    Remove-Item $tmp -ErrorAction SilentlyContinue
}

Write-Host "OK HC-FO-10 actualizado. Nuevo schema: $($out.Length) bytes." -ForegroundColor Green
Write-Host "Secciones finales:" -ForegroundColor Cyan
foreach ($s in $newChildren) { Write-Host "  - $($s.label)" }
