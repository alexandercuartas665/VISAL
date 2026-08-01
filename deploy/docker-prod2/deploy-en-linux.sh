#!/usr/bin/env bash
# =========================================================================
#  deploy-en-linux.sh
#  ------------------------------------------------------------------------
#  Script para correr DIRECTAMENTE en el server Linux (Ubuntu 22.04+/24.04+).
#  Equivalente Bash del deploy-en-servidor.ps1.
#
#  Hace TODO el despliegue de Visal en un solo paso:
#    1) Valida prereqs (docker, docker compose, archivos presentes).
#    2) Si no existe .env, lo genera con clave Postgres aleatoria (32 bytes).
#    3) Login GHCR si la imagen es privada (opcional).
#    4) docker compose pull && up -d.
#    5) Espera a que Postgres este healthy.
#    6) Restaura el dump dentro del container Postgres.
#    7) Reinicia visal-app para que EF Core suelte conexiones viejas.
#    8) Probe HTTP a /login y resumen final.
#
#  Que tienes que tener en el server, en una carpeta (por ejemplo
#  /opt/visal/), antes de correr esto:
#
#    ./docker-compose.yml          (del repo)
#    ./.env.example                (del repo, opcional)
#    ./deploy-en-linux.sh          (este archivo)
#    ./dumps/visal_dev.dump        (tu dump fresco de dev)
#
#  Uso minimo:
#    cd /opt/visal
#    chmod +x deploy-en-linux.sh
#    ./deploy-en-linux.sh
#
#  Parametros (flags estilo --flag valor):
#    --deploy-dir   carpeta del deploy (default: pwd)
#    --dump         ruta al .dump (default: el .dump mas reciente en ./dumps)
#    --port         puerto local que expone visal-app (default: 5380)
#    --db           nombre de la BD (default: visal)
#    --user         usuario de la BD (default: visal)
#    --image        imagen visal (default: ghcr.io/alexandercuartas665/visal/superadmin:latest)
#    --ghcr-user    usuario GitHub para GHCR si la imagen es privada
#    --ghcr-token   PAT con read:packages
#    --skip-restore no restaurar dump (solo deploy)
#    --force        no preguntar confirmaciones (uso desatendido)
# =========================================================================

set -euo pipefail

# ---------- defaults ----------------------------------------------------
DEPLOY_DIR="$(pwd)"
DUMP_PATH=""
VISAL_PORT="5380"
POSTGRES_DB="visal"
POSTGRES_USER="visal"
VISAL_IMAGE="ghcr.io/alexandercuartas665/visal/superadmin:latest"
GHCR_USER=""
GHCR_TOKEN=""
SKIP_RESTORE=0
FORCE=0

# ---------- parse args --------------------------------------------------
while [[ $# -gt 0 ]]; do
    case "$1" in
        --deploy-dir)   DEPLOY_DIR="$2"; shift 2 ;;
        --dump)         DUMP_PATH="$2"; shift 2 ;;
        --port)         VISAL_PORT="$2"; shift 2 ;;
        --db)           POSTGRES_DB="$2"; shift 2 ;;
        --user)         POSTGRES_USER="$2"; shift 2 ;;
        --image)        VISAL_IMAGE="$2"; shift 2 ;;
        --ghcr-user)    GHCR_USER="$2"; shift 2 ;;
        --ghcr-token)   GHCR_TOKEN="$2"; shift 2 ;;
        --skip-restore) SKIP_RESTORE=1; shift ;;
        --force)        FORCE=1; shift ;;
        -h|--help)
            grep '^#' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *)
            echo "Flag desconocido: $1" >&2
            exit 1
            ;;
    esac
done

# ---------- helpers de salida -------------------------------------------
C_CYAN=$'\e[36m'; C_GREEN=$'\e[32m'; C_GRAY=$'\e[90m'
C_YELLOW=$'\e[33m'; C_RED=$'\e[31m'; C_RESET=$'\e[0m'

step() { echo; echo "${C_CYAN}==> $1${C_RESET}"; }
ok()   { echo "    ${C_GREEN}OK   ${C_RESET} $1"; }
info() { echo "    ${C_GRAY}info ${C_RESET} $1"; }
warn() { echo "    ${C_YELLOW}WARN ${C_RESET} $1"; }
err()  { echo "    ${C_RED}ERR  ${C_RESET} $1" >&2; }

confirm_or_exit() {
    if [[ "$FORCE" -eq 1 ]]; then return; fi
    echo
    read -r -p "$1  [s/N] " resp
    if [[ ! "$resp" =~ ^[sSyY]$ ]]; then
        warn "Cancelado por el usuario."
        exit 1
    fi
}

# ---------- 0) Banner ---------------------------------------------------
echo
echo "${C_CYAN}=========================================================================${C_RESET}"
echo "${C_CYAN} Visal IPS RT - Deploy en server Linux${C_RESET}"
echo "${C_CYAN}=========================================================================${C_RESET}"
echo " DeployDir : $DEPLOY_DIR"
echo " Imagen    : $VISAL_IMAGE"
echo " Puerto    : 127.0.0.1:$VISAL_PORT -> visal-app:8080"
echo " BD        : $POSTGRES_DB (usuario $POSTGRES_USER)"
echo

# ---------- 1) Validaciones de entorno ----------------------------------
step "Validando entorno"

if [[ ! -d "$DEPLOY_DIR" ]]; then
    err "La carpeta '$DEPLOY_DIR' no existe."
    exit 1
fi
cd "$DEPLOY_DIR"
ok "Carpeta de deploy: $DEPLOY_DIR"

# Docker
if ! command -v docker >/dev/null 2>&1; then
    err "Docker no esta instalado. Instala con:"
    err "  curl -fsSL https://get.docker.com | sh"
    exit 1
fi
DOCKER_VERSION="$(docker version --format '{{.Server.Version}}' 2>/dev/null || echo '')"
if [[ -z "$DOCKER_VERSION" ]]; then
    err "Docker no responde. ¿Esta corriendo el daemon? (sudo systemctl status docker)"
    exit 1
fi
ok "Docker server: $DOCKER_VERSION"

# docker compose v2
if ! docker compose version >/dev/null 2>&1; then
    err "docker compose v2 no esta disponible. Instala docker-compose-plugin:"
    err "  sudo apt-get install -y docker-compose-plugin"
    exit 1
fi
ok "docker compose: $(docker compose version --short)"

# Permisos docker sin sudo
if ! docker info >/dev/null 2>&1; then
    err "Tu usuario no tiene permiso para hablar con docker."
    err "Anadelo al grupo:  sudo usermod -aG docker \$USER && newgrp docker"
    exit 1
fi

# docker-compose.yml
if [[ ! -f "./docker-compose.yml" ]]; then
    err "No encuentro docker-compose.yml en $DEPLOY_DIR."
    err "Copia el archivo desde el repo (deploy/docker-prod/docker-compose.yml)."
    exit 1
fi
ok "docker-compose.yml presente"

# Dump
if [[ "$SKIP_RESTORE" -eq 0 ]]; then
    if [[ -z "$DUMP_PATH" ]]; then
        if [[ ! -d "./dumps" ]]; then
            err "No existe la carpeta './dumps'. Crea la carpeta y mete tu .dump,"
            err "o corre con --skip-restore para levantar el stack sin datos."
            exit 1
        fi
        DUMP_PATH="$(ls -t ./dumps/*.dump 2>/dev/null | head -n1 || true)"
        if [[ -z "$DUMP_PATH" ]]; then
            err "No encuentro ningun archivo .dump en ./dumps/"
            err "Copia tu dump o corre con --skip-restore."
            exit 1
        fi
    elif [[ ! -f "$DUMP_PATH" ]]; then
        err "No existe el archivo '$DUMP_PATH'."
        exit 1
    fi
    SIZE_MB="$(awk "BEGIN{printf \"%.3f\", $(stat -c%s "$DUMP_PATH")/1048576}")"
    ok "Dump a restaurar: $DUMP_PATH ($SIZE_MB MB)"
fi

# ---------- 2) Generar / leer .env --------------------------------------
step "Configurando .env"

ENV_PATH="$DEPLOY_DIR/.env"
ENV_FRESCO=0

if [[ ! -f "$ENV_PATH" ]]; then
    warn ".env no existe. Lo genero con clave aleatoria."
    PG_PASSWORD="$(openssl rand -base64 32 | tr -d '/+=' )"
    cat > "$ENV_PATH" <<EOF
# Generado por deploy-en-linux.sh el $(date '+%Y-%m-%d %H:%M:%S')
# NO versionar este archivo. Contiene secretos de produccion.

VISAL_IMAGE=$VISAL_IMAGE
VISAL_PORT=$VISAL_PORT
POSTGRES_DB=$POSTGRES_DB
POSTGRES_USER=$POSTGRES_USER
POSTGRES_PASSWORD=$PG_PASSWORD
EOF
    chmod 600 "$ENV_PATH"
    ENV_FRESCO=1
    ok ".env generado (chmod 600). Password Postgres: $PG_PASSWORD"
    warn "Guarda esta clave en un gestor de secretos AHORA."
else
    ok ".env ya existe (lo respeto, no lo toco)"
    # Leer POSTGRES_DB y POSTGRES_USER del archivo
    POSTGRES_DB="$(grep -E '^POSTGRES_DB=' "$ENV_PATH" | head -n1 | cut -d= -f2- | tr -d '"' | tr -d "'" | xargs || echo "$POSTGRES_DB")"
    POSTGRES_USER="$(grep -E '^POSTGRES_USER=' "$ENV_PATH" | head -n1 | cut -d= -f2- | tr -d '"' | tr -d "'" | xargs || echo "$POSTGRES_USER")"
    info "BD efectiva (del .env): $POSTGRES_DB / $POSTGRES_USER"
fi

PG_PASSWORD_EFECTIVA="$(grep -E '^POSTGRES_PASSWORD=' "$ENV_PATH" | head -n1 | cut -d= -f2- | xargs)"

# ---------- 3) Login GHCR (opcional) ------------------------------------
if [[ -n "$GHCR_USER" && -n "$GHCR_TOKEN" ]]; then
    step "Login en GHCR"
    if echo "$GHCR_TOKEN" | docker login ghcr.io -u "$GHCR_USER" --password-stdin; then
        ok "Login GHCR OK"
    else
        err "Fallo el login en GHCR. Revisa el PAT (necesita scope read:packages)."
        exit 1
    fi
else
    info "Sin credenciales GHCR. Si la imagen es privada, fallara el pull."
    info "Para login despues:  echo \$PAT | docker login ghcr.io -u tu-usuario --password-stdin"
fi

# ---------- 4) Pull + up ------------------------------------------------
step "Descargando imagenes (docker compose pull)"
docker compose --env-file ./.env pull
ok "Imagenes al dia"

step "Levantando stack (docker compose up -d)"
docker compose --env-file ./.env up -d
ok "Stack levantado"

# ---------- 5) Esperar a Postgres ---------------------------------------
step "Esperando a que Postgres este listo"

MAX_INTENTOS=30
INTENTO=0
ESTADO=""
while [[ $INTENTO -lt $MAX_INTENTOS ]]; do
    INTENTO=$((INTENTO+1))
    ESTADO="$(docker inspect visal-postgres-prod --format '{{.State.Health.Status}}' 2>/dev/null || echo 'starting')"
    if [[ "$ESTADO" == "healthy" ]]; then
        ok "Postgres healthy en ~$((INTENTO*2)) seg"
        break
    fi
    sleep 2
done
if [[ "$ESTADO" != "healthy" ]]; then
    err "Postgres no llego a 'healthy' en $((MAX_INTENTOS*2)) seg."
    err "Revisa con:  docker logs visal-postgres-prod --tail 100"
    exit 1
fi

# ---------- 6) Restaurar dump -------------------------------------------
if [[ "$SKIP_RESTORE" -eq 0 ]]; then
    step "Verificando si la BD ya tiene datos"

    COUNT_TABLAS="$(docker exec -e PGPASSWORD="$PG_PASSWORD_EFECTIVA" visal-postgres-prod \
        psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At \
        -c "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema='public';" 2>/dev/null | tr -d '[:space:]')"
    COUNT_TABLAS="${COUNT_TABLAS:-0}"

    if [[ "$COUNT_TABLAS" -gt 0 ]]; then
        warn "La BD '$POSTGRES_DB' ya tiene $COUNT_TABLAS tablas en el schema public."
        warn "El restore con --clean --if-exists va a DROPEAR y recrear TODO."
        confirm_or_exit "Estas seguro que quieres reemplazar los datos actuales por los del dump?"
    else
        info "BD vacia (0 tablas). Restore limpio."
    fi

    step "Copiando dump adentro del container Postgres"
    docker cp "$DUMP_PATH" visal-postgres-prod:/tmp/restore.dump
    ok "Dump dentro del container: /tmp/restore.dump"

    step "Ejecutando pg_restore"
    set +e
    docker exec -e PGPASSWORD="$PG_PASSWORD_EFECTIVA" visal-postgres-prod \
        pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" \
        --no-owner --no-privileges --clean --if-exists \
        /tmp/restore.dump
    RC=$?
    set -e
    if [[ $RC -gt 1 ]]; then
        err "pg_restore fallo con exit code $RC."
        exit 1
    fi
    if [[ $RC -eq 1 ]]; then
        warn "pg_restore termino con warnings (exit 1)."
        warn "Normal si la BD estaba vacia. Revisa arriba: 'does not exist, skipping' es OK."
    fi
    ok "Restore terminado"

    docker exec visal-postgres-prod rm -f /tmp/restore.dump >/dev/null 2>&1 || true

    step "Verificando conteo de filas restauradas"
    VER_SQL="SELECT 'platform_users   ' || COUNT(*) FROM platform_users
UNION ALL SELECT 'tenant_users     ' || COUNT(*) FROM tenant_users
UNION ALL SELECT 'tenants          ' || COUNT(*) FROM tenants
UNION ALL SELECT 'aseguradoras     ' || COUNT(*) FROM aseguradoras
UNION ALL SELECT 'sucursales       ' || COUNT(*) FROM sucursales
UNION ALL SELECT 'pacientes        ' || COUNT(*) FROM pacientes
UNION ALL SELECT 'profesionales    ' || COUNT(*) FROM profesionales
UNION ALL SELECT 'roles            ' || COUNT(*) FROM roles;"
    docker exec -e PGPASSWORD="$PG_PASSWORD_EFECTIVA" visal-postgres-prod \
        psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -At -c "$VER_SQL"
fi

# ---------- 7) Restart de la app ----------------------------------------
step "Reiniciando visal-app (EF Core suelta caches/conexiones)"
docker restart visal-app >/dev/null
sleep 5
ok "visal-app reiniciado"

# ---------- 8) Health probe HTTP ----------------------------------------
step "Probe HTTP a http://localhost:$VISAL_PORT/login"
PROBE_OK=0
for i in $(seq 1 20); do
    if curl -fsS -o /dev/null -m 5 "http://localhost:$VISAL_PORT/login"; then
        ok "La app responde en ~$((i*3)) seg."
        PROBE_OK=1
        break
    fi
    sleep 3
done
if [[ $PROBE_OK -eq 0 ]]; then
    warn "La app no respondio en ~60 seg. Puede estar aplicando migrations EF."
    warn "Revisa con:  docker logs visal-app --tail 100 -f"
fi

# ---------- 9) Resumen final --------------------------------------------
echo
echo "${C_CYAN}=========================================================================${C_RESET}"
echo "${C_CYAN} Deploy terminado${C_RESET}"
echo "${C_CYAN}=========================================================================${C_RESET}"
echo " URL local del server  : http://localhost:$VISAL_PORT/login"
echo " Container app         : visal-app"
echo " Container Postgres    : visal-postgres-prod (no expuesto al host)"
echo " .env                  : $ENV_PATH (chmod 600)"
echo
echo "${C_YELLOW} Siguientes pasos:${C_RESET}"
echo "   1) Pasale al admin del server estos datos para que conecte su reverse proxy:"
echo "        Backend HTTP       : http://127.0.0.1:$VISAL_PORT"
echo "        WebSockets         : SI, requeridos (Blazor SignalR)"
echo "        Headers que envia  : ASPNETCORE_FORWARDEDHEADERS_ENABLED=true"
echo "                             (el app ya respeta X-Forwarded-Proto y X-Forwarded-For)"
echo "   2) Cambia los passwords de:"
echo "        admin@visal.travels"
echo "        demo-admin@visal.travels"
echo "      (el dump trae los hash de dev con Admin123*; en prod no puede quedar)."
echo
echo "${C_GRAY} Logs:           docker logs visal-app -f --tail 100${C_RESET}"
echo "${C_GRAY} Bajar stack:    docker compose down       (datos se conservan)${C_RESET}"
echo "${C_GRAY} Actualizar:     docker compose pull && docker compose up -d${C_RESET}"
echo
