# Rework-PPFO113-Fix.ps1
# Repara PP-FO-113 CONSENTIMIENTO CONSULTA DE PRIMERA VEZ:
#  1. Agrega logoUrl al header (venia sin la clave, por eso no salia el
#     logo Visal RT en pantalla).
#  2. Convierte los textos "Firma del Paciente: ____" y "CC: ____" en
#     campos de entrada reales (text) tanto en Paciente como Responsable.
#  3. Convierte "Yo____ como profesional..." en un field "Yo (nombre)" +
#     parrafo del docx + fields Firma/CC/Registro/Cargo.
#  4. Convierte el bloque partido del Responsable en fields nombre/paciente/
#     numero de identificacion + parrafo docx unificado + Firma/CC.
#  5. Agrega numerales 2/3/4/5 en labels de secciones (patron PP-FO-89).
#  6. Agrega seccion "Firmas (auto-llenadas)" antes del Cierre.
#
# Preserva: Datos del Paciente (auto), tabla_procedimientos/beneficios/riesgos/
# alternativas/riesgo_no_realizar, HUELLA, textos de declaraciones, prefill.
#
# Backup previo en scratchpad/backup_ppfo113_<timestamp>.json

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
function Field([string]$label, [string]$name, [string]$ft, [int]$width=12, $extra=@{}) {
    $f = @{ id=newId; type="field"; fieldType=$ft; label=$label; name=$name; widthColumns=$width; allowCustom=$false; required=$false }
    foreach ($k in $extra.Keys) { $f[$k] = $extra[$k] }
    return $f
}

$stamp = (Get-Date).ToString("yyyyMMdd_HHmmss")
$bkPath = Join-Path $BackupRoot ("backup_ppfo113_$stamp.json")
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='PP-FO-113' AND tenant_id='$TenantId';"
if ([string]::IsNullOrWhiteSpace($raw)) { throw "PP-FO-113 no existe" }
[System.IO.File]::WriteAllText($bkPath, $raw, [System.Text.UTF8Encoding]::new($false))
Write-Host "Backup: $bkPath" -ForegroundColor DarkGray

$schema = $raw | ConvertFrom-Json -AsHashtable

# 1) HEADER - agregar logoUrl
if ($null -ne $schema["header"]) {
    $schema["header"]["logoUrl"] = "/uploads/branding/visal-rt-logo.png"
    Write-Host "  header.logoUrl agregado" -ForegroundColor Green
}

# 2) Recorrer secciones y hacer los reemplazos quirurgicos
$declPacTexto1 = "Me han explicado y he comprendido satisfactoriamente la esencia y el propósito de este procedimiento, también me han aclarado todas las dudas y me han dicho los posibles riesgos y complicaciones, así como las otras alternativas de tratamiento."
$declPacTexto2 = "Doy mi consentimiento para que me realicen el procedimiento descrito anteriormente y los procedimientos complementarios que sean necesarios o convenientes mediante la realización de este, a criterio de los profesionales que lo llevan a cabo."
$declResp = "ha sido considerado por ahora incapaz de tomar por sí mismo la decisión de aceptar o rechazar el procedimiento. También se me han explicado los riesgos y complicaciones, así como las otras alternativas de tratamiento. Soy consciente que no existen garantías absolutas de los resultados del procedimiento. He comprendido todo lo anterior perfectamente y por ello doy mi consentimiento para que los profesionales tratantes y el personal auxiliar que precise, le realicen este procedimiento. Puedo revocar este consentimiento cuando en bien del paciente se considere oportuno."
$declProf = "como profesional tratante, he informado al paciente sobre la esencia y el propósito del procedimiento descrito anteriormente, de sus alternativas, posibles riesgos, resultados esperados y que no existen garantías absolutas de los resultados del procedimiento."

# Preservar la huella si ya esta en cada seccion
function Find-Huella($seccion, $namePrefix) {
    foreach ($c in $seccion.children) {
        $nm = [string]$c["name"]
        if ($nm -like "$namePrefix*" -or (($c["fieldType"] -eq "textarea") -and ($c["label"] -eq "HUELLA"))) {
            return $c
        }
    }
    return $null
}

$newSecs = New-Object System.Collections.ArrayList
$firmasAutoInsertada = $false
foreach ($sec in $schema.children) {
    $lbl = [string]$sec["label"]
    switch -Regex ($lbl) {
        "^Datos del Paciente" {
            [void]$newSecs.Add($sec)
        }
        "^INFORMACIÓN SOBRE EL PROCEDIMIENTO$" {
            $sec["label"] = "2. INFORMACIÓN SOBRE EL PROCEDIMIENTO"
            [void]$newSecs.Add($sec)
        }
        "^DECLARACIÓN DEL PACIENTE$" {
            $huella = Find-Huella $sec "huella_paciente"
            $newChildren = New-Object System.Collections.ArrayList
            [void]$newChildren.Add((P $declPacTexto1))
            [void]$newChildren.Add((P $declPacTexto2))
            if ($null -ne $huella) { [void]$newChildren.Add($huella) }
            [void]$newChildren.Add((Field "Firma del Paciente" "firma_declaracion_paciente" "text" 8))
            [void]$newChildren.Add((Field "CC" "cc_declaracion_paciente" "text" 4))
            $sec["label"] = "3. DECLARACIÓN DEL PACIENTE"
            $sec["children"] = $newChildren.ToArray()
            [void]$newSecs.Add($sec)
            Write-Host "  3. DECLARACION DEL PACIENTE: 2 parrafos + huella + Firma + CC" -ForegroundColor Green
        }
        "^DECLARACIÓN DEL RESPONSABLE" {
            $huella = Find-Huella $sec "huella_responsable"
            $newChildren = New-Object System.Collections.ArrayList
            [void]$newChildren.Add((Field "Yo (nombre del responsable)" "nombre_responsable" "text" 12))
            [void]$newChildren.Add((Field "Nombre del paciente" "nombre_paciente_incap" "text" 8))
            [void]$newChildren.Add((Field "N° de identificación del paciente" "no_id_paciente_incap" "text" 4))
            [void]$newChildren.Add((P $declResp))
            if ($null -ne $huella) { [void]$newChildren.Add($huella) }
            [void]$newChildren.Add((Field "Firma del Paciente / Responsable" "firma_responsable" "text" 8))
            [void]$newChildren.Add((Field "CC" "cc_responsable" "text" 4))
            $sec["label"] = "4. DECLARACIÓN DEL RESPONSABLE DEL PACIENTE (Solo en caso de Incapacidad del Paciente)"
            $sec["children"] = $newChildren.ToArray()
            [void]$newSecs.Add($sec)
            Write-Host "  4. DECLARACION DEL RESPONSABLE: nombres + parrafo docx + huella + Firma + CC" -ForegroundColor Green
        }
        "^DECLARACIÓN DEL PROFESIONAL" {
            $newChildren = New-Object System.Collections.ArrayList
            [void]$newChildren.Add((Field "Yo (nombre del profesional)" "nombre_profesional_declaracion" "text" 12))
            [void]$newChildren.Add((P $declProf))
            [void]$newChildren.Add((Field "Firma del profesional" "firma_declaracion_profesional" "text" 8))
            [void]$newChildren.Add((Field "CC" "cc_declaracion_profesional" "text" 4))
            [void]$newChildren.Add((Field "No Registro Profesional" "reg_profesional" "text" 6))
            [void]$newChildren.Add((Field "Cargo" "cargo_profesional" "text" 6))
            $sec["label"] = "5. DECLARACIÓN DEL PROFESIONAL TRATANTE"
            $sec["children"] = $newChildren.ToArray()
            [void]$newSecs.Add($sec)
            Write-Host "  5. DECLARACION DEL PROFESIONAL: nombre + parrafo docx + Firma + CC + Registro + Cargo" -ForegroundColor Green
        }
        "^Cierre$" {
            if (-not $firmasAutoInsertada) {
                $firmas = @{
                    id = newId; type = "section"; label = "Firmas (auto-llenadas)"
                    children = @(
                        (Field "Firma del paciente (URL)"     "firma_paciente_consent"     "text" 12),
                        (Field "Firma del profesional (URL)"  "firma_profesional_consent"  "text" 12)
                    )
                }
                [void]$newSecs.Add($firmas)
                $firmasAutoInsertada = $true
                Write-Host "  Firmas (auto-llenadas) insertadas antes de Cierre" -ForegroundColor Green
            }
            [void]$newSecs.Add($sec)
        }
        default {
            [void]$newSecs.Add($sec)
        }
    }
}
if (-not $firmasAutoInsertada) {
    $firmas = @{
        id = newId; type = "section"; label = "Firmas (auto-llenadas)"
        children = @(
            (Field "Firma del paciente (URL)"     "firma_paciente_consent"     "text" 12),
            (Field "Firma del profesional (URL)"  "firma_profesional_consent"  "text" 12)
        )
    }
    [void]$newSecs.Add($firmas)
    Write-Host "  Firmas (auto-llenadas) insertadas al final" -ForegroundColor Green
}
$schema["children"] = $newSecs.ToArray()

# Persistir
$json = ($schema | ConvertTo-Json -Depth 40 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='PP-FO-113' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_pp113_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK PP-FO-113 actualizado." -ForegroundColor Green
