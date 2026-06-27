# =========================================================================
#  subir-formularios.ps1
#  ------------------------------------------------------------------------
#  Sube SOLO la tabla form_definitions desde tu BD de dev al server Linux
#  de produccion. NO toca el resto de tablas (usuarios, pacientes,
#  asignaciones, aseguradoras, etc.) - eso queda como esta en prod.
#
#  Modos:
#    - Default (aditivo) : solo INSERT los IDs que NO existen en prod.
#                          Los formularios que ya existen alli quedan intactos.
#                          Cambios en schema_json de forms existentes NO se propagan.
#    - -Upsert           : INSERT los nuevos Y UPDATE los existentes con los
#                          campos cambiados (schema_json, nombre, version, etc).
#                          Las columnas de auditoria (created_at/created_by)
#                          se preservan; el resto se actualiza desde dev.
#    - -Replace          : borra TODOS los formularios de prod y carga los
#                          de dev. Util si dev es la "fuente de verdad" total.
#
#  Flujo:
#    1) Hace backup de la tabla form_definitions actual de prod a backups/.
#    2) Genera dump selectivo de form_definitions desde tu visal-postgres
#       local (Docker network visal-net).
#    3) scp del .sql al server.
#    4) Lo ejecuta dentro de visal-postgres-prod con psql.
#    5) Muestra conteo antes/despues y diferencia.
#
#  Uso minimo:
#    cd C:\DesarrolloIA\Visal\deploy\docker-prod
#    .\subir-formularios.ps1 -RemoteHost 10.0.0.3 -RemoteUser root
#
#  Con upsert (insert nuevos + update existentes):
#    .\subir-formularios.ps1 -RemoteHost 10.0.0.3 -RemoteUser root -Upsert
#
#  Con replace (pisa todo):
#    .\subir-formularios.ps1 -RemoteHost 10.0.0.3 -RemoteUser root -Replace
#
#  Parametros:
#    -RemoteDir       /opt/visal       carpeta del deploy
#    -KeyName         id_ed25519_visal llave SSH dedicada
#    -DevDbContainer  visal-postgres   nombre del container postgres de dev
#    -DevDbName       visal_dev        nombre de la BD de dev
#    -DevDbUser       visal            usuario de la BD de dev
#    -DevDbPassword   visal_local_2026 (si lo cambiaste en el .env de dev)
#    -DryRun          solo genera el SQL local, no sube nada
#    -Replace         modo destructivo: TRUNCATE + carga completa
# =========================================================================

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RemoteHost,
    [Parameter(Mandatory)][string]$RemoteUser,
    [string]$RemoteDir = "/opt/visal",
    [string]$KeyName = "id_ed25519_visal",
    [string]$DevDbContainer = "visal-postgres",
    [string]$DevDbName = "visal_dev",
    [string]$DevDbUser = "visal",
    [string]$DevDbPassword = "visal_local_2026",
    [switch]$DryRun,
    [switch]$Upsert,
    [switch]$Replace
)

if ($Upsert -and $Replace) {
    Write-Host "ERROR: no puedes usar -Upsert y -Replace al mismo tiempo." -ForegroundColor Red
    exit 1
}

$ErrorActionPreference = "Stop"

# ---------- helpers de salida -------------------------------------------
function Step($msg) { Write-Host ""; Write-Host "==> $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "    OK    $msg" -ForegroundColor Green }
function Info($msg) { Write-Host "    info  $msg" -ForegroundColor Gray }
function Warn($msg) { Write-Host "    WARN  $msg" -ForegroundColor Yellow }
function Err($msg)  { Write-Host "    ERR   $msg" -ForegroundColor Red }

function Confirm-Or-Exit($message) {
    Write-Host ""
    $resp = Read-Host "$message  [s/N]"
    if ($resp -notmatch '^[sSyY]') {
        Warn "Cancelado por el usuario."
        exit 1
    }
}

# Helpers ssh/scp que filtran warnings benignos (mismo patron que bootstrap-linux.ps1)
$here = $PSScriptRoot
if (-not $here) { $here = (Get-Location).Path }
$keyPath = Join-Path $HOME ".ssh\$KeyName"
$target = "$RemoteUser@$RemoteHost"

function Invoke-SshKey {
    param([Parameter(Mandatory)][string]$Command, [switch]$AllowFailure)
    $prevEA = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $combined = & ssh -i $keyPath -o BatchMode=yes $target $Command 2>&1
        $sshExit = $LASTEXITCODE
        $stdout = @()
        foreach ($line in $combined) {
            $text = "$line"
            if ($line -is [System.Management.Automation.ErrorRecord]) {
                if ($text -notmatch 'Warning: Permanently added' -and $text -notmatch 'Connection to .* closed') {
                    Write-Host "      ssh: $text" -ForegroundColor DarkYellow
                }
            } else {
                $stdout += $text
            }
        }
        if ($sshExit -ne 0 -and -not $AllowFailure) {
            Err "ssh fallo (exit $sshExit) en: $Command"
            exit 1
        }
        return ($stdout -join "`n")
    } finally {
        $ErrorActionPreference = $prevEA
    }
}

function Invoke-Scp {
    param([Parameter(Mandatory)][string]$Source, [Parameter(Mandatory)][string]$Dest)
    $prevEA = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $out = & scp -i $keyPath -o BatchMode=yes $Source "${target}:${Dest}" 2>&1
        $rc = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prevEA
    }
    if ($rc -ne 0) {
        Err "Fallo scp '$Source' -> '${target}:${Dest}'. Salida: $out"
        exit 1
    }
}

# ---------- 0) Banner ---------------------------------------------------
Write-Host ""
Write-Host "=========================================================================" -ForegroundColor Cyan
Write-Host " Visal - Subir form_definitions a produccion" -ForegroundColor Cyan
Write-Host "=========================================================================" -ForegroundColor Cyan
Write-Host " Origen dev   : $DevDbContainer / $DevDbName"
Write-Host " Destino prod : $target -> visal-postgres-prod"
$modoLabel = if ($Replace) { 'REPLACE (DELETE + INSERT)' }
             elseif ($Upsert) { 'UPSERT (INSERT nuevos + UPDATE existentes)' }
             else { 'ADDITIVE (solo INSERT nuevos)' }
Write-Host " Modo         : $modoLabel"
if ($DryRun) {
    Write-Host " DryRun       : SI - solo genera SQL local, no toca prod" -ForegroundColor Yellow
}
Write-Host ""

# ---------- 1) Validar entorno local -----------------------------------
Step "Validando dev postgres en docker local"

$contRunning = docker ps --filter "name=$DevDbContainer" --format "{{.Names}}" | Select-String -Pattern "^$DevDbContainer$"
if (-not $contRunning) {
    Err "El container '$DevDbContainer' no esta corriendo."
    Err "Levantalo con:  cd C:\DesarrolloIA\Visal\deploy\docker; docker compose up -d"
    exit 1
}
Ok "$DevDbContainer corriendo"

if (-not (Test-Path $keyPath)) {
    if (-not $DryRun) {
        Err "No existe la llave SSH '$keyPath'."
        Err "Corre bootstrap-linux.ps1 primero, o pasa -DryRun para solo generar el SQL."
        exit 1
    }
}

# ---------- 2) Conteo en dev -------------------------------------------
Step "Conteo de form_definitions en DEV"
$countDevRaw = docker exec -e PGPASSWORD=$DevDbPassword $DevDbContainer `
    psql -U $DevDbUser -d $DevDbName -At -c "SELECT COUNT(*) FROM form_definitions;"
$countDev = [int]$countDevRaw.Trim()
Ok "$countDev form_definitions en dev"

if ($countDev -eq 0) {
    Warn "No hay formularios en dev. No tiene sentido subir nada."
    exit 0
}

# ---------- 3) Generar SQL en local -------------------------------------
Step "Generando dump selectivo de form_definitions"

$fecha = Get-Date -Format 'yyyy-MM-dd-HHmm'
$dumpsDir = Join-Path $here "dumps"
if (-not (Test-Path $dumpsDir)) { New-Item -ItemType Directory -Path $dumpsDir | Out-Null }
$sqlFile = Join-Path $dumpsDir "forms_$fecha.sql"

# Flags de pg_dump:
#   --data-only          : solo INSERTs, sin DDL de la tabla
#   --column-inserts     : usa INSERT (column,..) VALUES en vez de COPY
#                          (mas lento pero permite ON CONFLICT)
#   --on-conflict-do-nothing : agrega "ON CONFLICT DO NOTHING" a cada INSERT
#                              (solo PG 16+ lo soporta directamente)
#   -t form_definitions  : solo esta tabla
$pgDumpFlags = "--data-only --column-inserts -t form_definitions"
if (-not $Replace) {
    # Modo aditivo: ON CONFLICT DO NOTHING evita pisar IDs existentes en prod
    $pgDumpFlags += " --on-conflict-do-nothing"
}

# Generar adentro del container y traer el archivo
docker exec -e PGPASSWORD=$DevDbPassword $DevDbContainer `
    sh -c "pg_dump -U $DevDbUser -d $DevDbName $pgDumpFlags -f /tmp/forms.sql" | Out-Null
if ($LASTEXITCODE -ne 0) {
    Err "Fallo pg_dump dentro del container dev."
    exit 1
}

docker cp "${DevDbContainer}:/tmp/forms.sql" $sqlFile | Out-Null
docker exec $DevDbContainer rm -f /tmp/forms.sql | Out-Null

if (-not (Test-Path $sqlFile)) {
    Err "No se genero el archivo SQL local."
    exit 1
}

# Quitar los meta-comandos \restrict y \unrestrict que pg_dump 16.7+ agrega.
# Son defense-in-depth (anti-injection) pero rompen psql en versiones anteriores
# a 16.7. Sin ellos el dump funciona igual; los quitamos para portabilidad.
#
# IMPORTANTE: leemos con encoding UTF-8 explicito. Get-Content sin -Encoding
# en PowerShell 5.1 usa el codepage del sistema (Windows-1252 en ES) y rompe
# las tildes/eñes que pg_dump emite en UTF-8. Usamos [System.IO.File] para
# que el comportamiento sea identico en PS 5.1 y PS 7+.
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$lineasRaw = [System.IO.File]::ReadAllLines($sqlFile, [System.Text.Encoding]::UTF8)
$lineas = $lineasRaw | Where-Object { $_ -notmatch '^\\(un)?restrict\s' }

# Modo UPSERT: reemplazar "ON CONFLICT DO NOTHING" por
# "ON CONFLICT (id) DO UPDATE SET col1=EXCLUDED.col1, col2=EXCLUDED.col2, ..."
# de modo que los formularios con campos cambiados se actualicen.
if ($Upsert) {
    # Extraer la lista de columnas del primer INSERT (todas las filas usan la misma).
    $firstInsert = $lineas | Where-Object { $_ -match '^INSERT INTO\s+\S+\s*\(([^)]+)\)' } | Select-Object -First 1
    if (-not $firstInsert) {
        Err "No pude encontrar la lista de columnas en el SQL generado."
        exit 1
    }
    [void]($firstInsert -match '^INSERT INTO\s+\S+\s*\(([^)]+)\)')
    $cols = $matches[1] -split ',' | ForEach-Object { $_.Trim() }
    Info "Columnas detectadas: $($cols -join ', ')"

    # No actualizamos PK ni columnas de auditoria del original.
    $excluidas = @('id', 'created_at', 'created_by')
    $colsAUpdate = $cols | Where-Object { $excluidas -notcontains $_ }
    if ($colsAUpdate.Count -eq 0) {
        Err "No hay columnas para actualizar (despues de excluir id/created_*)."
        exit 1
    }

    $setClause = ($colsAUpdate | ForEach-Object { "$_ = EXCLUDED.$_" }) -join ', '
    $onConflictNew = "ON CONFLICT (id) DO UPDATE SET $setClause"

    Info "Reescribiendo ON CONFLICT con UPDATE de $($colsAUpdate.Count) columnas"
    $lineas = $lineas | ForEach-Object {
        $_ -replace 'ON CONFLICT DO NOTHING', $onConflictNew
    }
}

# Escribir UTF-8 SIN BOM. psql tolera BOM pero algunos clientes / encadenamientos
# se confunden; sin BOM es lo mas portable.
[System.IO.File]::WriteAllLines($sqlFile, [string[]]$lineas, $utf8NoBom)

$sqlSize = [math]::Round((Get-Item $sqlFile).Length / 1KB, 2)
$sqlLines = (Get-Content $sqlFile | Measure-Object -Line).Lines
Ok "SQL generado: $sqlFile ($sqlSize KB, $sqlLines lineas)"
Info "Meta-comandos \restrict/\unrestrict removidos (compatibilidad pg_dump < 16.7)"
Info "Encoding del archivo: UTF-8 sin BOM (preserva tildes y eñes)"

# Si es modo Replace, anteponer un TRUNCATE para limpiar la tabla en prod
if ($Replace) {
    $contenido = [System.IO.File]::ReadAllText($sqlFile, [System.Text.Encoding]::UTF8)
    $prefijo = @"
-- Generado por subir-formularios.ps1 en modo REPLACE el $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')
-- Borra todos los form_definitions de prod antes de cargar los de dev.
BEGIN;
DELETE FROM form_definitions;
"@
    $sufijo = @"

COMMIT;
"@
    [System.IO.File]::WriteAllText($sqlFile, ($prefijo + "`n" + $contenido + $sufijo), $utf8NoBom)
    Info "Modo REPLACE: prefijado DELETE + envuelto en transaccion"
}

# Vista previa (primeras 30 lineas) - leer con UTF-8 para que se muestre correctamente
Info "Preview del SQL:"
[System.IO.File]::ReadAllLines($sqlFile, [System.Text.Encoding]::UTF8) | Select-Object -First 30 | ForEach-Object {
    Write-Host "      $_" -ForegroundColor DarkGray
}

if ($DryRun) {
    Write-Host ""
    Warn "DryRun activo. El SQL quedo en:"
    Write-Host "  $sqlFile" -ForegroundColor Yellow
    Warn "Revisalo y vuelve a correr sin -DryRun para subirlo."
    exit 0
}

Confirm-Or-Exit "Subir y aplicar este SQL en produccion?"

# ---------- 4) Backup defensivo en prod --------------------------------
Step "Backup de form_definitions actual en prod"

# Leer credenciales del .env del server
$envContent = Invoke-SshKey -Command "cat $RemoteDir/.env"
$prodDb = ($envContent -split "`n" | Select-String '^POSTGRES_DB=').ToString().Split('=',2)[1].Trim()
$prodUser = ($envContent -split "`n" | Select-String '^POSTGRES_USER=').ToString().Split('=',2)[1].Trim()
$prodPass = ($envContent -split "`n" | Select-String '^POSTGRES_PASSWORD=').ToString().Split('=',2)[1].Trim()
Info "BD destino: $prodDb / $prodUser"

# ---------- 4.b) Pre-DDL: asegurar tablas de soporte que el dump necesita ---
# El UPDATE/INSERT de form_definitions dispara el trigger
# trg_form_definition_snapshot, que escribe en form_definition_snapshots. Si
# prod aun no tiene esa tabla (migracion no aplicada), el dump revienta. Lo
# creamos aqui de forma idempotente: IF NOT EXISTS, no toca nada si ya esta.
Step "Asegurando tabla de soporte form_definition_snapshots en prod"
$preDdl = @"
CREATE TABLE IF NOT EXISTS form_definition_snapshots (
    id                   uuid                     NOT NULL,
    form_definition_id   uuid                     NOT NULL,
    codigo               character varying(40)    NOT NULL,
    nombre               character varying(200)   NOT NULL,
    version              character varying(20),
    tipo                 character varying(40),
    schema_json          jsonb                    NOT NULL,
    prefill_routes_json  jsonb,
    activo               boolean                  NOT NULL,
    snapshot_at          timestamp with time zone NOT NULL,
    snapshot_by          uuid,
    motivo               character varying(80),
    created_at           timestamp with time zone NOT NULL,
    created_by           uuid,
    updated_at           timestamp with time zone,
    updated_by           uuid,
    tenant_id            uuid                     NOT NULL,
    CONSTRAINT pk_form_definition_snapshots PRIMARY KEY (id),
    CONSTRAINT fk_form_definition_snapshots_form_definitions
        FOREIGN KEY (form_definition_id) REFERENCES form_definitions(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_form_definition_snapshots_form_definition_id_snapshot_at
    ON form_definition_snapshots (form_definition_id, snapshot_at DESC);
"@
$preDdlFile = Join-Path $dumpsDir "preddl_$fecha.sql"
[System.IO.File]::WriteAllText($preDdlFile, $preDdl, $utf8NoBom)
Invoke-Scp -Source $preDdlFile -Dest "$RemoteDir/dumps/preddl_$fecha.sql"
$preDdlCmd = "docker cp $RemoteDir/dumps/preddl_$fecha.sql visal-postgres-prod:/tmp/preddl.sql && docker exec -e PGPASSWORD='$prodPass' visal-postgres-prod psql -U $prodUser -d $prodDb -v ON_ERROR_STOP=1 -f /tmp/preddl.sql && docker exec visal-postgres-prod rm -f /tmp/preddl.sql"
$preDdlOut = Invoke-SshKey -Command $preDdlCmd -AllowFailure
if ($LASTEXITCODE -ne 0) {
    Err "Fallo el pre-DDL. Salida:"
    Write-Host $preDdlOut -ForegroundColor Red
    exit 1
}
Ok "form_definition_snapshots lista (CREATE TABLE IF NOT EXISTS)"

# Conteo antes
$countProdAntesRaw = Invoke-SshKey -Command "docker exec -e PGPASSWORD='$prodPass' visal-postgres-prod psql -U $prodUser -d $prodDb -At -c 'SELECT COUNT(*) FROM form_definitions;'"
$countProdAntes = [int]$countProdAntesRaw.Trim()
Info "Antes en prod: $countProdAntes form_definitions"

# Dump de respaldo de la tabla
$backupName = "forms_pre_$fecha.sql"
Invoke-SshKey -Command "mkdir -p $RemoteDir/backups && docker exec -e PGPASSWORD='$prodPass' visal-postgres-prod pg_dump -U $prodUser -d $prodDb --data-only --column-inserts -t form_definitions -f /tmp/$backupName && docker cp visal-postgres-prod:/tmp/$backupName $RemoteDir/backups/$backupName && docker exec visal-postgres-prod rm -f /tmp/$backupName" | Out-Null
Ok "Backup en $RemoteDir/backups/$backupName"

# ---------- 5) Subir el SQL --------------------------------------------
Step "Subiendo SQL al server"
Invoke-Scp -Source $sqlFile -Dest "$RemoteDir/dumps/forms_$fecha.sql"
Ok "Subido"

# ---------- 6) Ejecutar en prod ----------------------------------------
Step "Ejecutando SQL en visal-postgres-prod"

# Copiamos al container y ejecutamos psql -1 -f (-1 = transaccion implicita en modo aditivo)
$execCmd = "docker cp $RemoteDir/dumps/forms_$fecha.sql visal-postgres-prod:/tmp/forms_in.sql && docker exec -e PGPASSWORD='$prodPass' visal-postgres-prod psql -U $prodUser -d $prodDb -v ON_ERROR_STOP=1 -f /tmp/forms_in.sql && docker exec visal-postgres-prod rm -f /tmp/forms_in.sql"

$execOut = Invoke-SshKey -Command $execCmd -AllowFailure
$execExit = $LASTEXITCODE
if ($execExit -ne 0) {
    Err "Algo fallo al aplicar el SQL. Output:"
    Write-Host $execOut -ForegroundColor Red
    Err "Tu tabla quedo COMO ESTABA (psql -1 hace rollback en error en modo aditivo,"
    Err "o el BEGIN/COMMIT del modo Replace tampoco commiteo)."
    Err "Backup defensivo en: $RemoteDir/backups/$backupName"
    exit 1
}

# Mostrar las lineas INSERT del output (numero de filas afectadas)
$inserts = ($execOut -split "`n") | Where-Object { $_ -match '^INSERT 0 \d+|^DELETE \d+|^BEGIN|^COMMIT' }
if ($inserts) {
    Info "Comandos ejecutados:"
    $inserts | Select-Object -First 10 | ForEach-Object {
        Write-Host "      $_" -ForegroundColor DarkGray
    }
}

Ok "SQL aplicado"

# ---------- 7) Conteo despues ------------------------------------------
Step "Conteo final en prod"
$countProdDespuesRaw = Invoke-SshKey -Command "docker exec -e PGPASSWORD='$prodPass' visal-postgres-prod psql -U $prodUser -d $prodDb -At -c 'SELECT COUNT(*) FROM form_definitions;'"
$countProdDespues = [int]$countProdDespuesRaw.Trim()
$delta = $countProdDespues - $countProdAntes
$signo = if ($delta -ge 0) { "+" } else { "" }
Ok "Despues en prod: $countProdDespues form_definitions ($signo$delta vs antes)"

# ---------- 8) Resumen --------------------------------------------------
Write-Host ""
Write-Host "=========================================================================" -ForegroundColor Cyan
Write-Host " Listo" -ForegroundColor Green
Write-Host "=========================================================================" -ForegroundColor Cyan
Write-Host " Modo            : $(if($Replace){'REPLACE'}else{'ADDITIVE'})"
Write-Host " En dev          : $countDev"
Write-Host " En prod antes   : $countProdAntes"
Write-Host " En prod despues : $countProdDespues   ($signo$delta)"
Write-Host " Backup prod     : $RemoteDir/backups/$backupName"
Write-Host " SQL aplicado    : $sqlFile (local), $RemoteDir/dumps/forms_$fecha.sql (server)"
Write-Host ""
Write-Host " Para rollback rapido (si pisaste algo que no querias):" -ForegroundColor Gray
Write-Host "   ssh -i $keyPath $target" -ForegroundColor Gray
Write-Host "   cd $RemoteDir" -ForegroundColor Gray
Write-Host "   docker exec -e PGPASSWORD='$prodPass' visal-postgres-prod psql -U $prodUser -d $prodDb -c 'DELETE FROM form_definitions;'" -ForegroundColor Gray
Write-Host "   docker cp backups/$backupName visal-postgres-prod:/tmp/r.sql" -ForegroundColor Gray
Write-Host "   docker exec -e PGPASSWORD='$prodPass' visal-postgres-prod psql -U $prodUser -d $prodDb -f /tmp/r.sql" -ForegroundColor Gray
Write-Host ""
