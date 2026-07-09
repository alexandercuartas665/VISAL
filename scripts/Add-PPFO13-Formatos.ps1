# Add-PPFO13-Formatos.ps1
# Crea 2 formatos tipo=OTROS a partir de los docx en OneDrive:
#   PP-FO-13-A -> REGISTRO DE ASISTENCIA (tabla seed 20 sesiones)
#   PP-FO-13-C -> REGISTRO DE ATENCIÓN CONSULTAS (tabla seed 1 fila)
# Ambos con header.logoUrl del branding Visal RT.
#
# Idempotente por codigo: si el codigo ya existe, hace UPDATE del schema
# sin duplicar filas.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }
function P([string]$content) { @{ id=newId; type="text"; textStyle="paragraph"; content=$content } }
function Field([string]$label, [string]$name, [string]$ft, [int]$width=12, $extra=@{}) {
    $f = @{ id=newId; type="field"; fieldType=$ft; label=$label; name=$name; widthColumns=$width; allowCustom=$false; required=$false }
    foreach ($k in $extra.Keys) { $f[$k] = $extra[$k] }
    return $f
}
function Col([string]$label, [string]$name, [string]$ft, $extra=@{}) {
    $c = @{ id=newId; label=$label; name=$name; fieldType=$ft; allowCustom=$false }
    foreach ($k in $extra.Keys) { $c[$k] = $extra[$k] }
    return $c
}
function Tabla([string]$label, [string]$name, $cols, $seedRows, [bool]$lockRows, [int]$widthColumns=12) {
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

# ====================== HEADER comun ======================
function New-Header([string]$titulo) {
    return @{
        campos = @(
            @{ id=newId; label="No Historia" },
            @{ id=newId; label="Consecutivo" },
            @{ id=newId; label="Ciudad y Fecha" }
        )
        titulo = $titulo
        logoUrl = "/uploads/branding/visal-rt-logo.png"
        tagline = ""
        institucion = ""
    }
}

# ====================== Datos del Usuario (comun a los 2) ======================
function New-SecDatosUsuario([bool]$dirYModalidad) {
    $ch = @(
        (Field "Usuario"           "usuario"           "text" 6),
        (Field "Identificación"    "identificacion"    "text" 6),
        (Field "EPS"               "eps"               "text" 6),
        (Field "Paquete autorizado" "paquete_autorizado" "text" 6),
        (Field "Servicio"          "servicio"          "text" 6),
        (Field "No. Autorización"  "no_autorizacion"   "text" 6),
        (Field "Profesional"       "profesional"       "text" 12)
    )
    if ($dirYModalidad) {
        $ch += (Field "Dirección del usuario" "direccion_usuario" "text" 8)
        $ch += (Field "Modalidad" "modalidad" "text" 4)
    } else {
        $ch += (Field "Dirección" "direccion_usuario" "text" 12)
        $ch += (Field "Modalidad" "modalidad" "text" 6)
        $ch += (Field "Fecha y hora de atención" "fecha_hora_atencion" "text" 6)
    }
    return @{ id=newId; type="section"; label="Datos del Usuario"; children=$ch }
}

# ====================== PP-FO-13-A ASISTENCIA (20 sesiones) ======================
$schemaAsist = @{
    header = (New-Header "REGISTRO DE ASISTENCIA")
    children = @(
        (New-SecDatosUsuario $true),
        @{
            id = newId; type = "section"; label = "Sesiones"
            children = @(
                (Tabla "Sesiones" "sesiones" @(
                    (Col "No. Sesión"              "no_sesion"        "text"),
                    (Col "Firma Usuario/Acudiente" "firma_usuario"    "text"),
                    (Col "Hora de atención"        "hora_atencion"    "text"),
                    (Col "Fecha de atención"       "fecha_atencion"   "date")
                ) @(
                    @("1", "", "", ""), @("2", "", "", ""),   @("3", "", "", ""),  @("4", "", "", ""),  @("5", "", "", ""),
                    @("6", "", "", ""), @("7", "", "", ""),   @("8", "", "", ""),  @("9", "", "", ""),  @("10", "", "", ""),
                    @("11", "", "", ""), @("12", "", "", ""), @("13", "", "", ""), @("14", "", "", ""), @("15", "", "", ""),
                    @("16", "", "", ""), @("17", "", "", ""), @("18", "", "", ""), @("19", "", "", ""), @("20", "", "", "")
                ) $true)
            )
        },
        @{
            id = newId; type = "section"; label = "Cierre"
            children = @( (Field "Observaciones" "observaciones_cierre" "textarea" 12 @{ enableVoice = $true }) )
        }
    )
}

# ====================== PP-FO-13-C CONSULTAS (1 fila) ======================
$schemaCons = @{
    header = (New-Header "REGISTRO DE ATENCIÓN")
    children = @(
        (New-SecDatosUsuario $false),
        @{
            id = newId; type = "section"; label = "Atención"
            children = @(
                (Tabla "Atención" "atencion" @(
                    (Col "CANT."                   "cant"          "text"),
                    (Col "Firma Usuario/Acudiente" "firma_usuario" "text"),
                    (Col "HUELLA"                  "huella"        "text")
                ) @(
                    @("1", "", "")
                ) $true)
            )
        },
        @{
            id = newId; type = "section"; label = "Cierre"
            children = @( (Field "Observaciones" "observaciones_cierre" "textarea" 12 @{ enableVoice = $true }) )
        }
    )
}

# ====================== UPSERT ======================
function Upsert-Formato([string]$codigo, [string]$nombre, [hashtable]$schema) {
    $json = ($schema | ConvertTo-Json -Depth 40 -Compress)
    $jsonSql = $json.Replace("'","''")
    $now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")

    $exists = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT 1 FROM form_definitions WHERE codigo='$codigo' AND tenant_id='$TenantId';"
    if ($exists -eq "1") {
        $sql = "UPDATE form_definitions SET nombre='$nombre', tipo='OTROS', schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='$codigo' AND tenant_id='$TenantId';"
        Write-Host "  UPDATE $codigo" -ForegroundColor Yellow
    } else {
        $newIdGuid = [Guid]::NewGuid().ToString()
        $sql = "INSERT INTO form_definitions (id, codigo, nombre, version, tipo, schema_json, activo, created_at, updated_at, tenant_id) VALUES ('$newIdGuid', '$codigo', '$nombre', '01', 'OTROS', '$jsonSql'::jsonb, true, '$now', '$now', '$TenantId');"
        Write-Host "  INSERT $codigo" -ForegroundColor Green
    }

    $tmp = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
    try {
        $copy = "/tmp/visal_fmt_$([Guid]::NewGuid().ToString('N')).sql"
        docker cp $tmp "${PgContainer}:${copy}" | Out-Null
        $env:MSYS_NO_PATHCONV = "1"
        $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
        $exit = $LASTEXITCODE
        docker exec $PgContainer rm $copy 2>$null | Out-Null
        $env:MSYS_NO_PATHCONV = $null
        if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
    } finally { Remove-Item $tmp -ErrorAction SilentlyContinue }
}

Upsert-Formato "PP-FO-13-A" "PP-FO-13 FORMATO REGISTRO DE ASISTENCIA DE ATENCION" $schemaAsist
Upsert-Formato "PP-FO-13-C" "PP-FO-13 FORMATO REGISTRO DE ATENCION CONSULTAS"     $schemaCons

Write-Host "OK ambos formatos creados/actualizados." -ForegroundColor Green
