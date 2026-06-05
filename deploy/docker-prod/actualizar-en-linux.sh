#!/usr/bin/env bash
# =========================================================================
#  actualizar-en-linux.sh
#  ------------------------------------------------------------------------
#  Actualiza Visal en el server Linux a la ultima imagen GHCR, aplicando
#  migraciones EF Core sobre la BD actual SIN tocar los datos.
#
#  Es lo que corres cuando hiciste cambios en codigo (entidades, migrations,
#  features) en dev y ya hay un nuevo build publicado en GHCR.
#
#  NO restaura dumps. NO pisa datos de prod. Solo:
#    1) Backup defensivo de la BD (por si algo sale mal).
#    2) docker compose pull (trae la imagen nueva).
#    3) docker compose up -d (arranca el container nuevo).
#    4) Espera healthcheck.
#    5) Verifica en logs que las migrations se aplicaron sin error.
#    6) Probe HTTP a /login.
#
#  Uso:
#    cd /opt/visal
#    chmod +x actualizar-en-linux.sh
#    ./actualizar-en-linux.sh
#
#  Flags:
#    --skip-backup     no hacer dump previo (mas rapido, mas riesgo)
#    --tag <sha-XXX>   pinear a un tag especifico de GHCR (default: latest)
#    --ghcr-user X     login GHCR si la imagen es privada
#    --ghcr-token X
# =========================================================================

set -euo pipefail

SKIP_BACKUP=0
IMAGE_TAG=""
GHCR_USER=""
GHCR_TOKEN=""

while [[ $# -gt 0 ]]; do
    case "$1" in
        --skip-backup)  SKIP_BACKUP=1; shift ;;
        --tag)          IMAGE_TAG="$2"; shift 2 ;;
        --ghcr-user)    GHCR_USER="$2"; shift 2 ;;
        --ghcr-token)   GHCR_TOKEN="$2"; shift 2 ;;
        -h|--help)
            grep '^#' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
        *) echo "Flag desconocido: $1" >&2; exit 1 ;;
    esac
done

C_CYAN=$'\e[36m'; C_GREEN=$'\e[32m'; C_GRAY=$'\e[90m'
C_YELLOW=$'\e[33m'; C_RED=$'\e[31m'; C_RESET=$'\e[0m'

step() { echo; echo "${C_CYAN}==> $1${C_RESET}"; }
ok()   { echo "    ${C_GREEN}OK   ${C_RESET} $1"; }
info() { echo "    ${C_GRAY}info ${C_RESET} $1"; }
warn() { echo "    ${C_YELLOW}WARN ${C_RESET} $1"; }
err()  { echo "    ${C_RED}ERR  ${C_RESET} $1" >&2; }

# ---------- Validaciones ------------------------------------------------
step "Validando entorno"

if [[ ! -f "./docker-compose.yml" || ! -f "./.env" ]]; then
    err "Corre este script desde la carpeta del deploy (/opt/visal/)."
    err "Necesito docker-compose.yml y .env presentes."
    exit 1
fi
ok "Compose y .env presentes"

# Leer credenciales del .env
PG_DB="$(grep -E '^POSTGRES_DB=' .env | head -n1 | cut -d= -f2- | xargs)"
PG_USER="$(grep -E '^POSTGRES_USER=' .env | head -n1 | cut -d= -f2- | xargs)"
PG_PASS="$(grep -E '^POSTGRES_PASSWORD=' .env | head -n1 | cut -d= -f2- | xargs)"
info "BD destino: $PG_DB / $PG_USER"

# ---------- Backup defensivo --------------------------------------------
if [[ "$SKIP_BACKUP" -eq 0 ]]; then
    step "Backup defensivo de la BD (por si la migration sale mal)"
    mkdir -p ./backups
    BACKUP_FILE="./backups/pre_update_$(date +%Y-%m-%d_%H%M).dump"
    docker exec -e PGPASSWORD="$PG_PASS" visal-postgres-prod \
        pg_dump -U "$PG_USER" -d "$PG_DB" \
        --no-owner --no-privileges -Fc \
        -f /tmp/pre_update.dump
    docker cp visal-postgres-prod:/tmp/pre_update.dump "$BACKUP_FILE"
    docker exec visal-postgres-prod rm -f /tmp/pre_update.dump >/dev/null 2>&1 || true
    SIZE_MB="$(awk "BEGIN{printf \"%.2f\", $(stat -c%s "$BACKUP_FILE")/1048576}")"
    ok "Backup en $BACKUP_FILE ($SIZE_MB MB)"
    info "Si algo sale mal, restauras con:"
    info "  docker cp $BACKUP_FILE visal-postgres-prod:/tmp/r.dump && \\"
    info "    docker exec -e PGPASSWORD=*** visal-postgres-prod pg_restore -U $PG_USER -d $PG_DB --clean --if-exists /tmp/r.dump"
else
    warn "Backup omitido (--skip-backup). Mas rapido pero sin rollback facil."
fi

# ---------- Imagen actual vs proxima -----------------------------------
step "Comparando imagen actual vs disponible"
IMAGEN_ACTUAL="$(docker inspect visal-app --format '{{.Config.Image}}' 2>/dev/null || echo 'ninguna')"
SHA_ACTUAL="$(docker inspect visal-app --format '{{.Image}}' 2>/dev/null | cut -c1-19 || echo 'ninguna')"
info "Imagen actual : $IMAGEN_ACTUAL"
info "Image ID      : $SHA_ACTUAL"

# Si pinearon un tag, actualizamos VISAL_IMAGE en el .env
if [[ -n "$IMAGE_TAG" ]]; then
    # Quita el tag anterior y pega el nuevo
    NEW_IMAGE="ghcr.io/alexandercuartas665/visal/superadmin:${IMAGE_TAG}"
    if grep -E '^VISAL_IMAGE=' .env >/dev/null; then
        sed -i.bak "s|^VISAL_IMAGE=.*|VISAL_IMAGE=$NEW_IMAGE|" .env
    else
        echo "VISAL_IMAGE=$NEW_IMAGE" >> .env
    fi
    rm -f .env.bak
    info "Pineado a $NEW_IMAGE en .env"
fi

# ---------- Login GHCR si toca -----------------------------------------
if [[ -n "$GHCR_USER" && -n "$GHCR_TOKEN" ]]; then
    step "Login GHCR"
    echo "$GHCR_TOKEN" | docker login ghcr.io -u "$GHCR_USER" --password-stdin
    ok "Login OK"
fi

# ---------- Pull --------------------------------------------------------
step "Descargando nueva imagen (docker compose pull)"
docker compose --env-file ./.env pull
ok "Pull terminado"

SHA_NUEVA="$(docker inspect $(grep -E '^VISAL_IMAGE=' .env | cut -d= -f2-) --format '{{.Id}}' 2>/dev/null | cut -c1-19 || echo 'desconocida')"
info "Image ID nueva: $SHA_NUEVA"

if [[ "$SHA_ACTUAL" == "$SHA_NUEVA" && -z "$IMAGE_TAG" ]]; then
    warn "La imagen NO cambio (mismo ID). No hay nada nuevo en :latest."
    warn "Si esperabas un cambio, verifica que GitHub Actions ya publico el nuevo build."
    warn "Sigo con el restart de todas formas, por si es la primera ejecucion despues de un push."
fi

# ---------- Up + esperar healthcheck -----------------------------------
step "Arrancando container nuevo (docker compose up -d)"
docker compose --env-file ./.env up -d
ok "Compose up terminado"

step "Esperando que Postgres este healthy"
for i in $(seq 1 30); do
    ESTADO="$(docker inspect visal-postgres-prod --format '{{.State.Health.Status}}' 2>/dev/null || echo starting)"
    [[ "$ESTADO" == "healthy" ]] && { ok "Postgres healthy"; break; }
    sleep 2
done

# ---------- Verificar migrations en logs -------------------------------
step "Verificando migrations en logs de visal-app"
sleep 5  # darle tiempo al container a arrancar y empezar a migrar
# Buscar patrones tipicos de EF Core en el output
LOGS="$(docker logs visal-app --since 60s 2>&1 || true)"

if echo "$LOGS" | grep -qiE 'Applying migration'; then
    echo "$LOGS" | grep -iE 'Applying migration' | while read -r line; do
        ok "  $line"
    done
elif echo "$LOGS" | grep -qiE 'No migrations were applied'; then
    info "EF Core dice: no hay migrations nuevas para aplicar."
elif echo "$LOGS" | grep -qiE 'Database is up to date'; then
    info "BD ya estaba al dia."
else
    warn "No pude detectar migrations en los logs de los ultimos 60s."
    warn "Mira manualmente:  docker logs visal-app --tail 200"
fi

# Buscar errores criticos
if echo "$LOGS" | grep -qiE 'Migration.*failed|Failed executing|Error applying'; then
    err "PARECE QUE UNA MIGRATION FALLO. Revisa logs YA:"
    err "  docker logs visal-app --tail 200"
    err "Para rollback: para el container, restaura el backup en ./backups/, y vuelve a la imagen anterior."
    exit 1
fi

# ---------- Probe HTTP --------------------------------------------------
step "Probe HTTP a localhost"
VISAL_PORT="$(grep -E '^VISAL_PORT=' .env | head -n1 | cut -d= -f2- | xargs || echo 5380)"
for i in $(seq 1 20); do
    if curl -fsS -o /dev/null -m 5 "http://localhost:$VISAL_PORT/login"; then
        ok "App responde en http://localhost:$VISAL_PORT/login"
        break
    fi
    sleep 3
done

# ---------- Resumen -----------------------------------------------------
echo
echo "${C_CYAN}=========================================================================${C_RESET}"
echo "${C_CYAN} Actualizacion terminada${C_RESET}"
echo "${C_CYAN}=========================================================================${C_RESET}"
echo " Imagen vieja  : $IMAGEN_ACTUAL ($SHA_ACTUAL)"
echo " Imagen nueva  : $(grep -E '^VISAL_IMAGE=' .env | cut -d= -f2-) ($SHA_NUEVA)"
if [[ "$SKIP_BACKUP" -eq 0 ]]; then
    echo " Backup        : $BACKUP_FILE"
fi
echo
echo "${C_GRAY} Logs en vivo:  docker logs -f --tail 100 visal-app${C_RESET}"
echo "${C_GRAY} Rollback a backup si algo se rompio:${C_RESET}"
echo "${C_GRAY}   1) docker compose down${C_RESET}"
echo "${C_GRAY}   2) sed -i 's|^VISAL_IMAGE=.*|VISAL_IMAGE=<imagen-vieja>|' .env${C_RESET}"
echo "${C_GRAY}   3) docker compose up -d${C_RESET}"
echo "${C_GRAY}   4) (si fue mal schema) restaura el dump de ./backups/${C_RESET}"
echo
