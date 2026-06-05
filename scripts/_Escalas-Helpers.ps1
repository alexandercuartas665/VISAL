# _Escalas-Helpers.ps1
# Funciones comunes usadas por Insert-*.ps1 (Barthel/Morse/Norton/Enfermeria).
# Se hacen dot-source: . .\_Escalas-Helpers.ps1

Add-Type -AssemblyName System.IO.Compression.FileSystem

function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

function New-TextNode {
    param([string]$Style, [string]$Content)
    return @{
        id = newId
        type = "text"
        textStyle = $Style
        content = $Content
    }
}

function New-Section {
    param([string]$Label, $Children)
    return @{
        id = newId
        type = "section"
        label = $Label
        children = @($Children)
    }
}

function New-PacienteSection {
    # Seccion estandar de datos del paciente para escalas. Prefill compatible
    # con las rutas que ya tenemos (nombre_paciente, identificacion, etc).
    return New-Section "DATOS DEL PACIENTE" @(
        @{ id = newId; type = "field"; fieldType = "text";       label = "Nombre del paciente"; name = "nombre_paciente"; widthColumns = 8; required = $true },
        @{ id = newId; type = "field"; fieldType = "text";       label = "Identificacion";      name = "identificacion";   widthColumns = 4; required = $true },
        @{ id = newId; type = "field"; fieldType = "date";       label = "Fecha de nacimiento"; name = "fecha_nacimiento"; widthColumns = 4 },
        @{ id = newId; type = "field"; fieldType = "calculated"; label = "Edad (auto)";         name = "edad";             widthColumns = 2; formula = "edad(fecha_nacimiento)" },
        @{ id = newId; type = "field"; fieldType = "date";       label = "Fecha aplicacion";    name = "fecha_aplicacion"; widthColumns = 3; required = $true },
        @{ id = newId; type = "field"; fieldType = "text";       label = "Hora";                name = "hora_aplicacion";  widthColumns = 3 }
    )
}

function New-FirmaSection {
    return New-Section "OBSERVACIONES Y FIRMA" @(
        @{ id = newId; type = "field"; fieldType = "textarea"; label = "Observaciones del profesional"; name = "observaciones"; widthColumns = 12 },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Profesional que aplica"; name = "profesional"; widthColumns = 8; required = $true },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Registro / N. Tarjeta";  name = "registro";    widthColumns = 4 }
    )
}

function New-Select {
    param([string]$Label, [string]$Name, [string[]]$Options, [int]$Width = 12, [bool]$Required = $true)
    return @{
        id = newId
        type = "field"
        fieldType = "select"
        label = $Label
        name = $Name
        widthColumns = $Width
        required = $Required
        catalog = "estatico"
        options = $Options
    }
}

function New-Calculated {
    param([string]$Label, [string]$Name, [string]$Formula, [int]$Width = 12)
    return @{
        id = newId
        type = "field"
        fieldType = "calculated"
        label = $Label
        name = $Name
        widthColumns = $Width
        formula = $Formula
    }
}

function Build-Header {
    param([string]$Titulo)
    return @{
        institucion = "IPS VISAL RT"
        tagline = "Atencion Humana, Agil y Oportuna"
        titulo = $Titulo
        campos = @(
            @{ id = newId; label = "No Historia" },
            @{ id = newId; label = "Fecha" },
            @{ id = newId; label = "Hora" }
        )
    }
}

function Save-FormDefinition {
    param(
        [string]$Codigo,
        [string]$Nombre,
        [string]$Version,
        [string]$Tipo,
        $Schema,                # objeto: { header = ..., children = ... }
        [string]$TenantId,
        [string]$PgContainer = "visal-postgres",
        [string]$PgUser = "visal",
        [string]$PgDb = "visal_dev"
    )

    $json = ($Schema | ConvertTo-Json -Depth 25 -Compress)
    $jsonSql = $json.Replace("'","''")
    $id = [Guid]::NewGuid().ToString()
    $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")

    $nameSql = $Nombre.Replace("'","''")
    $sql = @"
DELETE FROM form_definitions WHERE codigo = '$Codigo' AND tenant_id = '$TenantId';
INSERT INTO form_definitions
  (id, tenant_id, codigo, nombre, version, tipo, schema_json, prefill_routes_json, activo, created_at, updated_at)
VALUES
  ('$id', '$TenantId', '$Codigo', '$nameSql', '$Version', '$Tipo', '$jsonSql'::jsonb, NULL, true, '$now', '$now');
"@
    $tmp = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
    try {
        $copy = "/tmp/visal_esc_$([Guid]::NewGuid().ToString('N')).sql"
        docker cp $tmp "${PgContainer}:${copy}" | Out-Null
        $out = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
        $exit = $LASTEXITCODE
        docker exec $PgContainer rm $copy 2>$null | Out-Null
        if ($exit -ne 0) { throw "psql fallo (exit=$exit): $($out -join ' | ')" }
    } finally {
        Remove-Item $tmp -ErrorAction SilentlyContinue
    }
    Write-Host "    OK insertado codigo=$Codigo id=$id schema=$($json.Length) bytes" -ForegroundColor Green
    return $id
}

function Move-Origin {
    param([string]$SourceFile, [string]$ProcessedDir)
    if (-not (Test-Path $SourceFile)) {
        Write-Host "    (origen no estaba; quizas ya procesado)" -ForegroundColor Yellow
        return
    }
    if (-not (Test-Path $ProcessedDir)) { New-Item -ItemType Directory -Path $ProcessedDir -Force | Out-Null }
    $target = Join-Path $ProcessedDir (Split-Path $SourceFile -Leaf)
    if (Test-Path $target) {
        $stamp = (Get-Date).ToString("yyyyMMddHHmmss")
        $base = [System.IO.Path]::GetFileNameWithoutExtension($target)
        $ext  = [System.IO.Path]::GetExtension($target)
        $target = Join-Path $ProcessedDir "$base.$stamp$ext"
    }
    Move-Item -Path $SourceFile -Destination $target -Force
    Write-Host "    OK movido a $target" -ForegroundColor Green
}
