# =========================================================================
#  deploy-remote.ps1 — gestiona el deploy en TU servidor de produccion
#  desde esta maquina, usando Docker contexts via SSH.
#
#  Uso desde C:\DesarrolloIA\Visal\deploy\docker-prod:
#    .\deploy-remote.ps1 setup-context     # una sola vez
#    .\deploy-remote.ps1 deploy            # pull + up en el server
#    .\deploy-remote.ps1 logs              # tail logs del server
#    .\deploy-remote.ps1 status            # docker compose ps en el server
#    .\deploy-remote.ps1 backup            # dump de Postgres a backups/
#    .\deploy-remote.ps1 restart           # restart visal-app
#    .\deploy-remote.ps1 down              # baja el stack (datos intactos)
#    .\deploy-remote.ps1 shell             # bash dentro de visal-app
#    .\deploy-remote.ps1 psql              # psql dentro de postgres
#    .\deploy-remote.ps1 remove-context    # quita el context (no toca el server)
#
#  Requisitos previos:
#    - SSH a tu server funcionando sin pedir password (clave SSH)
#    - Docker 24+ en el server, con tu usuario en el grupo docker
#    - .env editado en esta carpeta con VISAL_PORT, POSTGRES_*, VISAL_IMAGE
# =========================================================================

[CmdletBinding()]
param(
    [Parameter(Position=0)]
    [ValidateSet("setup-context","deploy","logs","status","backup","restart","down","shell","psql","remove-context")]
    [string]$Command = "deploy",

    # SSH del server. Ej: acuartas@visal.tudominio.com  o  acuartas@10.0.1.42
    [string]$RemoteSsh = $env:VISAL_REMOTE_SSH,

    # Nombre del context Docker. Cambialo solo si manejas multiples servers.
    [string]$ContextName = "visal-prod"
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot

function Step($msg) { Write-Host ""; Write-Host "==> $msg" -ForegroundColor Cyan }
function Ok($msg)   { Write-Host "    $msg" -ForegroundColor Green }
function Warn($msg) { Write-Host "    $msg" -ForegroundColor Yellow }
function Err($msg)  { Write-Host "    $msg" -ForegroundColor Red }

function Assert-ContextExists {
    $exists = docker context ls --format "{{.Name}}" | Where-Object { $_ -eq $ContextName }
    if (-not $exists) {
        Err "El context '$ContextName' no existe."
        Write-Host "    Ejecuta primero: .\deploy-remote.ps1 setup-context -RemoteSsh user@server" -ForegroundColor Yellow
        exit 1
    }
}

function Invoke-Compose {
    param([string[]]$Args)
    # Las llamadas usan --context para no cambiar el default del usuario,
    # y --env-file con el .env local (la BD/network corren en el server).
    docker --context $ContextName compose --env-file "$here\.env" @Args
}

switch ($Command) {

    "setup-context" {
        if (-not $RemoteSsh) {
            Err "Falta -RemoteSsh user@server (o setea `$env:VISAL_REMOTE_SSH antes)."
            exit 1
        }
        Step "Verificando SSH a $RemoteSsh"
        $sshTest = ssh -o BatchMode=yes -o ConnectTimeout=5 $RemoteSsh "echo connected" 2>&1
        if ($LASTEXITCODE -ne 0) {
            Err "SSH fallo: $sshTest"
            Write-Host "    Verifica que tienes clave SSH configurada y puedes hacer: ssh $RemoteSsh" -ForegroundColor Yellow
            exit 1
        }
        Ok "SSH OK: $sshTest"

        Step "Verificando docker en el server"
        $dockerOk = ssh $RemoteSsh "docker version --format '{{.Server.Version}}'" 2>&1
        if ($LASTEXITCODE -ne 0) {
            Err "Docker no respondio: $dockerOk"
            Write-Host "    Verifica que tu usuario este en el grupo docker en el server." -ForegroundColor Yellow
            exit 1
        }
        Ok "Docker server: $dockerOk"

        # Recrear si ya existia
        $exists = docker context ls --format "{{.Name}}" | Where-Object { $_ -eq $ContextName }
        if ($exists) {
            Warn "El context '$ContextName' ya existe; lo recreo."
            docker context rm $ContextName | Out-Null
        }

        Step "Creando context '$ContextName' apuntando a ssh://$RemoteSsh"
        docker context create $ContextName --docker "host=ssh://$RemoteSsh" | Out-Null
        Ok "Context creado."

        Step "Probando llamada remota"
        $info = docker --context $ContextName info --format "{{.Name}} ({{.OperatingSystem}})" 2>&1
        Ok "Conectado al server: $info"

        Write-Host ""
        Ok "Listo. Ya puedes ejecutar:"
        Write-Host "        .\deploy-remote.ps1 deploy" -ForegroundColor White
        Write-Host "        .\deploy-remote.ps1 logs" -ForegroundColor White
        Write-Host "        .\deploy-remote.ps1 status" -ForegroundColor White
    }

    "remove-context" {
        $exists = docker context ls --format "{{.Name}}" | Where-Object { $_ -eq $ContextName }
        if (-not $exists) { Ok "El context no existia."; exit 0 }
        docker context rm $ContextName | Out-Null
        Ok "Context '$ContextName' removido (el server no se toco)."
    }

    "deploy" {
        Assert-ContextExists
        Step "Pull de la imagen en el server"
        Invoke-Compose @("pull")
        if ($LASTEXITCODE -ne 0) { exit 1 }

        Step "Aplicando docker compose up -d (con migrations EF al arrancar)"
        Invoke-Compose @("up","-d","--remove-orphans")
        if ($LASTEXITCODE -ne 0) { exit 1 }

        Step "Estado actual en el server"
        Invoke-Compose @("ps")

        Step "Ultimas 30 lineas del app"
        Invoke-Compose @("logs","--tail=30","visal-app")
    }

    "logs"    { Assert-ContextExists; Invoke-Compose @("logs","-f","--tail=100","visal-app") }
    "status"  { Assert-ContextExists; Invoke-Compose @("ps") }
    "restart" { Assert-ContextExists; Invoke-Compose @("restart","visal-app"); Invoke-Compose @("logs","--tail=30","visal-app") }
    "down"    { Assert-ContextExists; Warn "Esto baja la app y postgres. Los DATOS quedan en el volumen visal-pgdata."; Invoke-Compose @("down") }

    "shell"   { Assert-ContextExists; Invoke-Compose @("exec","visal-app","/bin/bash") }

    "psql" {
        Assert-ContextExists
        # Usa los valores del .env local para autenticar al postgres del server.
        $env:Path | Out-Null
        $envFile = Join-Path $here ".env"
        $envVars = @{}
        Get-Content $envFile | Where-Object { $_ -match "^[A-Z_]+=" } | ForEach-Object {
            $k,$v = $_ -split "=",2; $envVars[$k.Trim()] = $v.Trim()
        }
        Invoke-Compose @("exec","postgres","psql","-U",$envVars["POSTGRES_USER"],"-d",$envVars["POSTGRES_DB"])
    }

    "backup" {
        Assert-ContextExists
        $envFile = Join-Path $here ".env"
        $envVars = @{}
        Get-Content $envFile | Where-Object { $_ -match "^[A-Z_]+=" } | ForEach-Object {
            $k,$v = $_ -split "=",2; $envVars[$k.Trim()] = $v.Trim()
        }
        $backupsDir = Join-Path $here "backups"
        if (-not (Test-Path $backupsDir)) { New-Item -ItemType Directory -Path $backupsDir | Out-Null }
        $stamp = Get-Date -Format "yyyy-MM-dd-HHmm"
        $out = Join-Path $backupsDir "visal-$stamp.sql.gz"
        Step "Generando dump en el server y bajandolo a $out"
        # pg_dump corre en el contenedor del server, el stdout viaja por SSH a tu maquina.
        docker --context $ContextName exec -T visal-postgres-prod pg_dump -U $envVars["POSTGRES_USER"] -d $envVars["POSTGRES_DB"] |
            ForEach-Object { $_ } | Out-File -FilePath "$out.tmp" -Encoding UTF8
        # Comprimir local con .NET (no necesitamos gzip en Windows).
        $bytes = [IO.File]::ReadAllBytes("$out.tmp")
        $fs = [IO.File]::OpenWrite($out)
        $gz = New-Object IO.Compression.GZipStream($fs, [IO.Compression.CompressionLevel]::Optimal)
        $gz.Write($bytes, 0, $bytes.Length)
        $gz.Close(); $fs.Close()
        Remove-Item "$out.tmp" -Force
        $size = (Get-Item $out).Length / 1MB
        Ok ("Backup: $out  ({0:N1} MB)" -f $size)
    }
}
