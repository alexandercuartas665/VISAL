#requires -Version 5.1
<#
.SYNOPSIS
  Aplica visibleWhen = { field: "sexo", operator: "equals", value: "FEMENINO" }
  a todas las secciones "GINECO OBSTETRICOS" de los formularios HC.

.DESCRIPTION
  El motor de formularios ahora soporta visibilidad condicional por nodo
  (FormNode.VisibleWhen). Este script identifica las secciones cuyo label
  contiene "GINECO" en TODOS los formularios HC y les setea la regla que
  las oculta cuando el paciente no es de sexo femenino. Solo aplica a los
  nodos encontrados; el resto del schema queda intacto.

  Idempotente: si ya tiene visibleWhen setado, lo actualiza igual.
  Ejecuta SET/COMMIT explicito por formulario, con reporte por consola.

.NOTES
  Regla de memoria del repo Visal: usar [System.IO.File] con UTF-8 explicito
  para no romper tildes/enies (nunca Get/Set-Content).
#>

[CmdletBinding()]
param(
    [string]$Container = 'visal-postgres',
    [string]$Database        = 'visal_dev',
    [string]$DatabaseUser    = 'visal',
    [string]$DatabasePass    = 'visal_dev'
)

$ErrorActionPreference = 'Stop'

# --- helpers ---------------------------------------------------------------

function Invoke-Psql {
    param(
        [Parameter(Mandatory)] [string]$Sql,
        [switch]$Raw
    )
    $tmp = [System.IO.Path]::GetTempFileName()
    try {
        [System.IO.File]::WriteAllText($tmp, $Sql, [System.Text.UTF8Encoding]::new($false))
        $args = @(
            'exec', '-i',
            '-e', "PGPASSWORD=$DatabasePass",
            $Container,
            'psql', '-U', $DatabaseUser, '-d', $Database,
            '-v', 'ON_ERROR_STOP=1'
        )
        if ($Raw) { $args += @('-A', '-t') }
        $out = Get-Content $tmp -Raw | & docker @args 2>&1
        if ($LASTEXITCODE -ne 0) { throw "psql fallo: $out" }
        return $out
    }
    finally {
        Remove-Item $tmp -Force -ErrorAction SilentlyContinue
    }
}

function Set-VisibleWhenOnGinecoSections {
    param(
        [Parameter(Mandatory)] $Node,
        [ref]$Counter
    )
    if ($null -eq $Node) { return }

    # Es un objeto (nodo)?
    if ($Node -is [pscustomobject]) {
        # Match "gineco" en label (seccion), content (bloque de texto) o name (campo).
        # Todos los formatos HC de Visal usan esta convencion, ya sea como
        # subheading + campo hermano (HC-FO-21/22) o como seccion completa
        # (HC-FO-08/16).
        $label   = "$($Node.label)".ToUpperInvariant()
        $content = "$($Node.content)".ToUpperInvariant()
        $name    = "$($Node.name)".ToUpperInvariant()
        $hitsGineco = ($label -match 'GINECO') -or `
                     ($content -match 'GINECO') -or `
                     ($name -match 'GINECO')

        if ($hitsGineco) {
            $rule = [pscustomobject]@{
                field    = 'sexo'
                operator = 'equals'
                value    = 'FEMENINO'
            }
            if ($Node.PSObject.Properties.Match('visibleWhen').Count -gt 0) {
                $Node.visibleWhen = $rule
            } else {
                $Node | Add-Member -MemberType NoteProperty -Name 'visibleWhen' -Value $rule -Force
            }
            $Counter.Value++
        }

        # Recurre hijos
        if ($Node.PSObject.Properties.Match('children').Count -gt 0 -and $null -ne $Node.children) {
            foreach ($ch in $Node.children) {
                Set-VisibleWhenOnGinecoSections -Node $ch -Counter $Counter
            }
        }
        return
    }

    # Array
    if ($Node -is [System.Collections.IEnumerable] -and -not ($Node -is [string])) {
        foreach ($ch in $Node) {
            Set-VisibleWhenOnGinecoSections -Node $ch -Counter $Counter
        }
    }
}

# --- 1) traer lista de formularios candidatos -----------------------------

Write-Host "[1/3] Buscando formularios con seccion GINECO..." -ForegroundColor Cyan

$listSql = @"
SELECT id::text || '|' || codigo || '|' || nombre
FROM form_definitions
WHERE upper(schema_json::text) LIKE '%GINECO%'
ORDER BY codigo;
"@

$raw = Invoke-Psql -Sql $listSql -Raw
$rows = ($raw -split "`r?`n") | Where-Object { $_ -match '\|' }

if ($rows.Count -eq 0) {
    Write-Host "  No se encontraron formularios con seccion GINECO. Nada que hacer." -ForegroundColor Yellow
    return
}

Write-Host ("  {0} formularios candidatos:" -f $rows.Count) -ForegroundColor Green
foreach ($r in $rows) {
    $parts = $r -split '\|', 3
    Write-Host ("   - {0} ({1})" -f $parts[1], $parts[2])
}

# --- 2) por cada formulario, patch del JSON -------------------------------

Write-Host "`n[2/3] Aplicando visibleWhen a secciones GINECO..." -ForegroundColor Cyan

$totalUpdatedForms = 0
$totalPatchedNodes = 0
$scratch = [System.IO.Path]::Combine($env:TEMP, 'visal-form-schema-patch')
if (-not (Test-Path $scratch)) { New-Item -ItemType Directory -Path $scratch | Out-Null }

foreach ($r in $rows) {
    $parts  = $r -split '\|', 3
    $formId = $parts[0].Trim()
    $codigo = $parts[1].Trim()

    # Extraer schema_json crudo (base64 para evitar problemas de escape)
    $extract = "SELECT encode(convert_to(schema_json::text, 'UTF8'), 'base64') FROM form_definitions WHERE id = '$formId';"
    $b64 = (Invoke-Psql -Sql $extract -Raw).Trim()
    if (-not $b64) {
        Write-Host "  [$codigo] no se pudo leer schema, se omite" -ForegroundColor Yellow
        continue
    }
    $b64Clean = ($b64 -replace '\s', '')
    $jsonText = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($b64Clean))

    $schema  = $jsonText | ConvertFrom-Json -Depth 100
    $counter = [ref] 0
    if ($null -ne $schema.children) {
        foreach ($top in $schema.children) {
            Set-VisibleWhenOnGinecoSections -Node $top -Counter $counter
        }
    }

    if ($counter.Value -eq 0) {
        Write-Host "  [$codigo] no encontro secciones GINECO en el arbol (posible falso positivo en el LIKE). Se omite." -ForegroundColor Yellow
        continue
    }

    $updatedJson = $schema | ConvertTo-Json -Depth 100 -Compress

    # Guardar a archivo temporal y aplicar UPDATE parametrizado via psql \copy-ish.
    # Simple: usamos jsonb_build_object indirectamente; en la practica basta con \gset
    # o pasar el JSON como argumento. Aqui usamos un tmp + \set y COPY.
    $tmpFile = Join-Path $scratch ("{0}.json" -f $codigo)
    [System.IO.File]::WriteAllText($tmpFile, $updatedJson, [System.Text.UTF8Encoding]::new($false))

    # Copiar al contenedor
    & docker cp $tmpFile "${Container}:/tmp/visal_patch.json" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "docker cp fallo para $codigo" }

    $updSql = @"
UPDATE form_definitions
SET schema_json = pg_read_file('/tmp/visal_patch.json')::jsonb,
    updated_at = now()
WHERE id = '$formId';
"@
    # pg_read_file requiere superuser en la version 16; si no funciona, usamos \copy con lo.
    try {
        $updRes = Invoke-Psql -Sql $updSql
        Write-Host "  [$codigo] visibleWhen aplicado a $($counter.Value) seccion(es) GINECO" -ForegroundColor Green
        $totalUpdatedForms++
        $totalPatchedNodes += $counter.Value
    }
    catch {
        # Fallback: leer el archivo desde host, encodearlo base64 y pasarlo via SQL.
        Write-Host "    pg_read_file bloqueado; probando via base64 inline..." -ForegroundColor DarkYellow
        $b64Update = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($updatedJson))
        $updSql2 = @"
UPDATE form_definitions
SET schema_json = convert_from(decode('$b64Update','base64'),'UTF8')::jsonb,
    updated_at = now()
WHERE id = '$formId';
"@
        Invoke-Psql -Sql $updSql2 | Out-Null
        Write-Host "  [$codigo] visibleWhen aplicado a $($counter.Value) seccion(es) GINECO (via base64)" -ForegroundColor Green
        $totalUpdatedForms++
        $totalPatchedNodes += $counter.Value
    }
}

# --- 3) verificacion --------------------------------------------------------

Write-Host "`n[3/3] Verificando (conteo de nodos con visibleWhen sexo=FEMENINO)..." -ForegroundColor Cyan
$verify = @"
SELECT codigo,
       (regexp_matches(schema_json::text, '"visibleWhen":\{[^}]*"field":"sexo"[^}]*"value":"FEMENINO"', 'g'))[1] IS NOT NULL AS tiene_regla,
       (length(schema_json::text) - length(replace(schema_json::text, '"field":"sexo"', ''))) / length('"field":"sexo"') AS ocurrencias
FROM form_definitions
WHERE upper(schema_json::text) LIKE '%GINECO%'
ORDER BY codigo;
"@
try {
    $verifyOut = Invoke-Psql -Sql $verify
    Write-Host $verifyOut
}
catch {
    Write-Host "  (verificacion opcional fallo: $($_.Exception.Message))" -ForegroundColor DarkYellow
}

Write-Host "`n============================================================" -ForegroundColor Green
Write-Host ("Formularios actualizados : {0}" -f $totalUpdatedForms) -ForegroundColor Green
Write-Host ("Secciones GINECO parchadas: {0}" -f $totalPatchedNodes) -ForegroundColor Green
Write-Host "============================================================" -ForegroundColor Green
