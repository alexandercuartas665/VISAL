# Fix-HCFO10a-Fisioterapia.ps1
# Toma el schema actual de HC-FO-10a (FORMATO HC FISIOTERAPIA 4F), trunca
# el contenido a partir del nodo que mencione "TEST MOVILIDAD ARTICULAR"
# (o el primer "MOVILIDAD ARTICULAR" que aparezca) y reemplaza por las
# secciones reales del docx: TEST MOVILIDAD, FUERZA MUSCULAR, VALORACION,
# DIAGNOSTICO, OBJETIVOS, PLAN y FIRMA.

[CmdletBinding()]
param(
    [string]$TenantId = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$Id = "f3b4b8da-76fd-4025-9e21-7024c5e6460f",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser = "visal",
    [string]$PgDb = "visal_dev"
)
$ErrorActionPreference = "Stop"

function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

# 1) Cargar schema actual
$json = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE id='$Id' AND tenant_id='$TenantId';"
if (-not $json) { throw "HC-FO-10a no encontrado." }
$schema = $json | ConvertFrom-Json -AsHashtable

# 2) Detectar la estructura. HC-FO-10a parece tener una sola seccion grande
# "Encabezado del documento" con todo adentro. Truncamos los children de esa
# seccion en el punto donde aparezca MOVILIDAD ARTICULAR (subheading o paragraph).
function Trim-At-Movilidad {
    param($nodes)
    $cut = -1
    for ($i = 0; $i -lt $nodes.Count; $i++) {
        $n = $nodes[$i]
        $textContent = ""
        if ($n.type -eq "text") { $textContent = $n.content }
        elseif ($n.type -eq "field") { $textContent = $n.label }
        if ($textContent -match 'MOVILIDAD\s*ARTICULAR') { $cut = $i; break }
    }
    if ($cut -ge 0) {
        Write-Host "  Encontrado MOVILIDAD ARTICULAR en indice $cut. Truncando." -ForegroundColor Cyan
        return ,@($nodes[0..($cut-1)])
    }
    Write-Host "  No se encontro MOVILIDAD ARTICULAR; se mantiene todo." -ForegroundColor Yellow
    return ,$nodes
}

# Hay 1 seccion (Encabezado del documento); truncamos sus children
$newChildren = @()
foreach ($s in $schema.children) {
    if ($s.type -eq 'section') {
        $kept = Trim-At-Movilidad $s.children
        $s.children = $kept
        $newChildren += $s
    } else {
        $newChildren += $s
    }
}

# 3) Construir las nuevas secciones tipo "section" anexas (no quedan dentro del
# Encabezado del documento, son hermanas).

# --- TEST MOVILIDAD ARTICULAR ---
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
$secMovilidad = @{
    id = newId; type = "section"; label = "TEST MOVILIDAD ARTICULAR"
    children = @(
        @{ id = newId; type = "text"; textStyle = "paragraph";
           content = "Registre rangos articulares y limitaciones encontradas. Compare valoracion inicial y final." },
        @{
            id = newId; type = "field"; fieldType = "table"
            label = "Test de movilidad articular"
            name = "test_movilidad_articular"
            widthColumns = 12
            columns = $movCols
            seedRows = $movSeed
            lockRows = $true
        }
    )
}

# --- FUERZA MUSCULAR (sup + inf, escala MRC 0-5) ---
$fzColsSup = @(
    @{ id = newId; label = "Grado";              name = "grado";          fieldType = "text" },
    @{ id = newId; label = "Descripcion";        name = "descripcion";    fieldType = "text" },
    @{ id = newId; label = "Valoracion inicial"; name = "v_inicial";      fieldType = "text" },
    @{ id = newId; label = "Valoracion final";   name = "v_final";        fieldType = "text" }
)
$fzColsInf = @(
    @{ id = newId; label = "Grado";              name = "grado";          fieldType = "text" },
    @{ id = newId; label = "Descripcion";        name = "descripcion";    fieldType = "text" },
    @{ id = newId; label = "Valoracion inicial"; name = "v_inicial";      fieldType = "text" },
    @{ id = newId; label = "Valoracion final";   name = "v_final";        fieldType = "text" }
)
$fzSeed = @(
    @("0","Ninguna respuesta muscular"),
    @("1","Musculo realiza contraccion visible/palpable sin movimiento"),
    @("2","Musculo realiza todo el movimiento sin gravedad / sin resistencia"),
    @("3","Musculo realiza todo el movimiento contra gravedad / sin resistencia"),
    @("4","Movimiento en toda amplitud contra gravedad + resistencia moderada"),
    @("5","Musculo soporta resistencia manual maxima, movimiento completo contra gravedad")
)
$secFuerza = @{
    id = newId; type = "section"; label = "FUERZA MUSCULAR"
    children = @(
        @{ id = newId; type = "text"; textStyle = "paragraph";
           content = "Escala MRC (Medical Research Council) - de 0 a 5. Marque el valor observado inicial y final por grupo muscular." },
        @{ id = newId; type = "text"; textStyle = "subheading"; content = "Miembros superiores" },
        @{
            id = newId; type = "field"; fieldType = "table"
            label = "Fuerza muscular - Miembros superiores"
            name = "fuerza_sup"
            widthColumns = 12
            columns = $fzColsSup
            seedRows = $fzSeed
            lockRows = $true
        },
        @{ id = newId; type = "text"; textStyle = "subheading"; content = "Miembros inferiores" },
        @{
            id = newId; type = "field"; fieldType = "table"
            label = "Fuerza muscular - Miembros inferiores"
            name = "fuerza_inf"
            widthColumns = 12
            columns = $fzColsInf
            seedRows = $fzSeed
            lockRows = $true
        }
    )
}

# --- VALORACION FISIOTERAPEUTICA ---
$secValoracion = @{
    id = newId; type = "section"; label = "VALORACION FISIOTERAPEUTICA"
    children = @(
        @{ id = newId; type = "field"; fieldType = "textarea"
           label = "Valoracion fisioterapeutica"; name = "valoracion_fisioterapeutica"; widthColumns = 12 }
    )
}

# --- DIAGNOSTICO FISIOTERAPEUTICO (DOMINIO | PATRON repetible) ---
$dxCols = @(
    @{ id = newId; label = "Dominio"; name = "dominio"; fieldType = "text" },
    @{ id = newId; label = "Patron";  name = "patron";  fieldType = "text" }
)
$secDx = @{
    id = newId; type = "section"; label = "DIAGNOSTICO FISIOTERAPEUTICO"
    children = @(
        @{
            id = newId; type = "field"; fieldType = "table"
            label = "Diagnostico fisioterapeutico"
            name = "diagnostico_fisioterapeutico"
            widthColumns = 12
            columns = $dxCols
        }
    )
}

# --- OBJETIVOS + PLAN ---
$secObjGen = @{
    id = newId; type = "section"; label = "OBJETIVO GENERAL"
    children = @(@{ id = newId; type = "field"; fieldType = "textarea"; label = "Objetivo general"; name = "objetivo_general"; widthColumns = 12 })
}
$secObjEsp = @{
    id = newId; type = "section"; label = "OBJETIVO ESPECIFICO"
    children = @(@{ id = newId; type = "field"; fieldType = "textarea"; label = "Objetivo especifico"; name = "objetivo_especifico"; widthColumns = 12 })
}
$secPlan = @{
    id = newId; type = "section"; label = "PLAN DE TRATAMIENTO"
    children = @(@{ id = newId; type = "field"; fieldType = "textarea"; label = "Plan de tratamiento"; name = "plan_tratamiento"; widthColumns = 12 })
}

# --- FIRMA ---
$secFirma = @{
    id = newId; type = "section"; label = "PROFESIONAL Y FIRMA"
    children = @(
        @{ id = newId; type = "field"; fieldType = "text"; label = "Profesional";        name = "profesional_nombre";    widthColumns = 6; required = $true },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Registro / Tarjeta"; name = "profesional_registro"; widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Documento";          name = "profesional_documento"; widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Firma";              name = "profesional_firma";     widthColumns = 12 }
    )
}

# 4) Append y persistir
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
$sql = "UPDATE form_definitions SET schema_json = '$outSql'::jsonb, updated_at = '$now' WHERE id = '$Id' AND tenant_id = '$TenantId';"

$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_fix10a_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host ""
Write-Host "OK HC-FO-10a actualizado. Schema final: $($out.Length) bytes" -ForegroundColor Green
Write-Host "Secciones top-level:" -ForegroundColor Cyan
foreach ($s in $newChildren) {
    if ($s.type -eq 'section') { Write-Host "  - SECCION: $($s.label)  ($($s.children.Count) hijos)" }
    else { Write-Host "  - $($s.type): $($s.label)" }
}
