# Insert-Barthel.ps1
# Crea (o reemplaza) el formato "Escala Barthel" en BD, hecho a mano para que
# funcione como instrumento clinico real: 10 items con opciones puntuadas,
# suma automatica, clasificacion automatica del grado de dependencia.
#
# Codigo: PP-FO-46-ESCALA-BARTHEL
# Mueve el archivo origen a la carpeta PROCESADO al finalizar.

[CmdletBinding()]
param(
    [string]$SourceFile = "C:\Users\acuartas\OneDrive - Bitcode IT Services S.A.S\Bitcode\13. Proyectos\028. Visal\01. Recursos\FORMATO_PENDIENTES\PP-FO-46 ESCALA DE BARTHELv2.docx",
    [string]$ProcessedDir = "C:\Users\acuartas\OneDrive - Bitcode IT Services S.A.S\Bitcode\13. Proyectos\028. Visal\01. Recursos\FORMATO_PENDIENTES\PROCESADO",
    [string]$TenantId = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$Codigo = "PP-FO-46-ESCALA-BARTHEL",
    [string]$Nombre = "Escala Barthel (Indice de actividades basicas de la vida diaria)",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser = "visal",
    [string]$PgDb = "visal_dev"
)

$ErrorActionPreference = "Stop"

function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

# ------------------------- ITEMS BARTHEL -------------------------
# (codigo de campo, etiqueta, opciones con puntaje)
$items = @(
    @{ name = "barthel_comida"; label = "Comida"; opts = @(
        "10 - Independiente. Capaz de comer por si solo en un tiempo razonable.",
        "5 - Necesita ayuda para cortar la carne, extender la mantequilla, pero es capaz de comer solo.",
        "0 - Dependiente. Necesita ser alimentado por otra persona."
    )},
    @{ name = "barthel_lavado"; label = "Lavado (bano)"; opts = @(
        "5 - Independiente. Capaz de lavarse entero y entrar/salir del bano sin ayuda.",
        "0 - Dependiente. Necesita algun tipo de ayuda o supervision."
    )},
    @{ name = "barthel_vestido"; label = "Vestido"; opts = @(
        "10 - Independiente. Capaz de ponerse y quitarse la ropa sin ayuda.",
        "5 - Necesita ayuda. Realiza sin ayuda mas de la mitad de las tareas en tiempo razonable.",
        "0 - Dependiente."
    )},
    @{ name = "barthel_arreglo"; label = "Arreglo personal"; opts = @(
        "5 - Independiente. Realiza todas las actividades personales sin ayuda.",
        "0 - Dependiente. Necesita alguna ayuda."
    )},
    @{ name = "barthel_deposicion"; label = "Deposicion"; opts = @(
        "10 - Continente. No presenta episodios de incontinencia.",
        "5 - Accidente ocasional. Menos de una vez por semana o necesita ayuda para enemas/supositorios.",
        "0 - Incontinente. Mas de un episodio semanal."
    )},
    @{ name = "barthel_miccion"; label = "Miccion"; opts = @(
        "10 - Continente. Capaz de utilizar cualquier dispositivo por si solo.",
        "5 - Accidente ocasional. Maximo un episodio en 24h o requiere ayuda con sondas.",
        "0 - Incontinente. Mas de un episodio en 24h."
    )},
    @{ name = "barthel_ir_al_bano"; label = "Ir al bano"; opts = @(
        "10 - Independiente. Entra y sale solo, no necesita ayuda.",
        "5 - Necesita ayuda. Pequena ayuda; capaz de usar el bano y limpiarse solo.",
        "0 - Dependiente. Incapaz de acceder o usarlo sin ayuda mayor."
    )},
    @{ name = "barthel_transferencia"; label = "Transferencia (traslado cama/sillon)"; opts = @(
        "15 - Independiente. No requiere ayuda para sentarse/levantarse ni para entrar/salir de la cama.",
        "10 - Minima ayuda. Supervision o pequena ayuda fisica.",
        "5 - Gran ayuda. Precisa ayuda de una persona fuerte o entrenada.",
        "0 - Dependiente. Necesita grua o el alzamiento por dos personas."
    )},
    @{ name = "barthel_deambulacion"; label = "Deambulacion"; opts = @(
        "15 - Independiente. Puede andar 50 m o equivalente sin ayuda ni supervision.",
        "10 - Necesita ayuda. Supervision o pequena ayuda fisica, o utiliza andador.",
        "5 - Independiente en silla de ruedas. No requiere ayuda ni supervision.",
        "0 - Dependiente. Es incapaz de caminar."
    )},
    @{ name = "barthel_escaleras"; label = "Subir y bajar escaleras"; opts = @(
        "10 - Independiente. Capaz de subir y bajar un piso sin ayuda ni supervision.",
        "5 - Necesita ayuda. Necesita ayuda o supervision.",
        "0 - Dependiente. Es incapaz de salvar escalones."
    )}
)

# Item -> nodo select
$itemNodes = @()
foreach ($it in $items) {
    $itemNodes += @{
        id = newId
        type = "field"
        fieldType = "select"
        label = $it.label
        name = $it.name
        widthColumns = 12
        required = $true
        catalog = "estatico"
        options = $it.opts
    }
}

# Construccion del schema
$schema = @{
    header = @{
        institucion = "IPS VISAL RT"
        tagline = "Atencion Humana, Agil y Oportuna"
        titulo = "INDICE DE BARTHEL - Actividades basicas de la vida diaria"
        campos = @(
            @{ id = newId; label = "No Historia" },
            @{ id = newId; label = "Fecha" },
            @{ id = newId; label = "Hora" }
        )
    }
    children = @(
        @{
            id = newId
            type = "section"
            label = "DATOS DEL PACIENTE"
            children = @(
                @{ id = newId; type = "field"; fieldType = "text";   label = "Nombre del paciente"; name = "nombre_paciente"; widthColumns = 8; required = $true },
                @{ id = newId; type = "field"; fieldType = "text";   label = "Identificacion";      name = "identificacion";   widthColumns = 4; required = $true },
                @{ id = newId; type = "field"; fieldType = "date";   label = "Fecha de nacimiento"; name = "fecha_nacimiento"; widthColumns = 4 },
                @{ id = newId; type = "field"; fieldType = "calculated"; label = "Edad (auto)";     name = "edad";             widthColumns = 2; formula = "edad(fecha_nacimiento)" },
                @{ id = newId; type = "field"; fieldType = "date";   label = "Fecha de aplicacion"; name = "fecha_aplicacion"; widthColumns = 3; required = $true },
                @{ id = newId; type = "field"; fieldType = "text";   label = "Hora";                name = "hora_aplicacion";  widthColumns = 3 }
            )
        },
        @{
            id = newId
            type = "section"
            label = "INDICE BARTHEL"
            children = @(
                @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Para cada item, seleccione la opcion que mejor describe la capacidad funcional del paciente. El puntaje total y la clasificacion del grado de dependencia se calculan automaticamente al final." }
            ) + $itemNodes
        },
        @{
            id = newId
            type = "section"
            label = "RESULTADO"
            children = @(
                @{
                    id = newId
                    type = "field"
                    fieldType = "calculated"
                    label = "TOTAL (suma 0 - 100)"
                    name = "barthel_total"
                    widthColumns = 4
                    formula = "sum(barthel_comida, barthel_lavado, barthel_vestido, barthel_arreglo, barthel_deposicion, barthel_miccion, barthel_ir_al_bano, barthel_transferencia, barthel_deambulacion, barthel_escaleras)"
                },
                @{
                    id = newId
                    type = "field"
                    fieldType = "calculated"
                    label = "Grado de dependencia (automatico)"
                    name = "barthel_clasificacion"
                    widthColumns = 8
                    formula = 'cases(barthel_total, "0-19=DEPENDENCIA TOTAL;20-35=DEPENDENCIA SEVERA;40-55=DEPENDENCIA MODERADA;60-95=DEPENDENCIA LEVE;100-100=INDEPENDENCIA")'
                },
                @{ id = newId; type = "text"; textStyle = "subheading"; content = "Referencias: <20 Dependencia total | 20-35 Severa | 40-55 Moderada | 60-95 Leve | 100 Independencia" }
            )
        },
        @{
            id = newId
            type = "section"
            label = "OBSERVACIONES Y FIRMA"
            children = @(
                @{ id = newId; type = "field"; fieldType = "textarea"; label = "Observaciones del profesional"; name = "observaciones"; widthColumns = 12 },
                @{ id = newId; type = "field"; fieldType = "text"; label = "Profesional que aplica"; name = "profesional"; widthColumns = 8; required = $true },
                @{ id = newId; type = "field"; fieldType = "text"; label = "Registro / N. Tarjeta";  name = "registro";    widthColumns = 4 }
            )
        }
    )
}

# Serializar y sanitizar para SQL
$json = ($schema | ConvertTo-Json -Depth 25 -Compress)
$jsonSql = $json.Replace("'","''")

# Borrar + insertar
$id = [Guid]::NewGuid().ToString()
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")

$sql = @"
DELETE FROM form_definitions WHERE codigo = '$Codigo' AND tenant_id = '$TenantId';
INSERT INTO form_definitions
  (id, tenant_id, codigo, nombre, version, tipo, schema_json, prefill_routes_json, activo, created_at, updated_at)
VALUES
  ('$id', '$TenantId', '$Codigo', '$($Nombre.Replace("'","''"))', '1.0', 'ESCALAS', '$jsonSql'::jsonb, NULL, true, '$now', '$now');
"@

$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_barthel_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $out = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    if ($exit -ne 0) { throw "psql fallo (exit=$exit): $($out -join ' | ')" }
} finally {
    Remove-Item $tmp -ErrorAction SilentlyContinue
}
Write-Host "    OK Escala Barthel insertada como '$Codigo' (id=$id, schema=$($json.Length) bytes)" -ForegroundColor Green

# Mover origen a PROCESADO
if (Test-Path $SourceFile) {
    if (-not (Test-Path $ProcessedDir)) { New-Item -ItemType Directory -Path $ProcessedDir -Force | Out-Null }
    $target = Join-Path $ProcessedDir (Split-Path $SourceFile -Leaf)
    if (Test-Path $target) {
        $stamp = (Get-Date).ToString("yyyyMMddHHmmss")
        $base = [System.IO.Path]::GetFileNameWithoutExtension($target)
        $ext  = [System.IO.Path]::GetExtension($target)
        $target = Join-Path $ProcessedDir "$base.$stamp$ext"
    }
    Move-Item -Path $SourceFile -Destination $target -Force
    Write-Host "    OK Origen movido a $target" -ForegroundColor Green
} else {
    Write-Host "    (origen no estaba; quizas ya fue procesado)" -ForegroundColor Yellow
}
