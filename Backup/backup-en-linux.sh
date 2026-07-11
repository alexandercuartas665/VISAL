#!/usr/bin/env bash
# =============================================================================
#  backup-en-linux.sh
#  Corre EN EL SERVER prod. Empaqueta BD + uploads + compose en un .tar.gz.
#
#  NO modifica nada del sistema:
#    - pg_dump usa snapshot MVCC (no bloquea escrituras)
#    - tar del volumen uploads es lectura pura
#    - Solo escribe en /tmp/ y borra al final
#
#  Uso:
#    ./backup-en-linux.sh [-d /opt/visal] [-o /tmp/salida.tar.gz]
#
#  Salida stdout (parseable por el script PowerShell):
#    RESULT_FILE=/tmp/visal-backup-YYYYMMDD-HHMMSS.tar.gz
#    RESULT_SIZE=12345678
#    RESULT_TENANTS=1
#    RESULT_PACIENTES=42
#    RESULT_HCS=137
#    RESULT_USERS=273
#    RESULT_IMAGE=ghcr.io/alexandercuartas665/visal/superadmin:latest
# =============================================================================

set -euo pipefail

REMOTE_DIR="/opt/visal"
OUT_FILE=""

while getopts "d:o:" opt; do
    case $opt in
        d) REMOTE_DIR="$OPTARG" ;;
        o) OUT_FILE="$OPTARG" ;;
        *) echo "Uso: $0 [-d /opt/visal] [-o /tmp/salida.tar.gz]" >&2; exit 1 ;;
    esac
done

STAMP=$(date -u +%Y%m%d-%H%M%S)
STAGING="/tmp/visal-backup-staging-$STAMP"
if [ -z "$OUT_FILE" ]; then
    OUT_FILE="/tmp/visal-backup-$STAMP.tar.gz"
fi

PG_CONTAINER="visal-postgres-prod"
APP_CONTAINER="visal-app"
UPLOADS_VOLUME="visal-prod_visal-uploads"
COMPOSE_FILE="$REMOTE_DIR/docker-compose.yml"

log()  { echo "[$(date -u +%H:%M:%S)] $*" >&2; }
ok()   { echo "                   OK  $*" >&2; }
fail() { echo "                   ERR $*" >&2; exit 1; }

cleanup() {
    if [ -d "$STAGING" ]; then rm -rf "$STAGING"; fi
}
trap cleanup EXIT

# --- 1) Validaciones ---
log "Validando entorno"
command -v docker >/dev/null || fail "docker no esta instalado"
docker inspect "$PG_CONTAINER" >/dev/null 2>&1 || fail "contenedor $PG_CONTAINER no existe"
docker inspect "$APP_CONTAINER" >/dev/null 2>&1 || fail "contenedor $APP_CONTAINER no existe"
[ -f "$COMPOSE_FILE" ] || fail "compose file no existe: $COMPOSE_FILE"
ok "docker, contenedores y compose OK"

# --- 2) Extraer credenciales de la BD del contenedor ---
log "Leyendo credenciales de la BD"
DB_NAME=$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$PG_CONTAINER" | grep -E '^POSTGRES_DB=' | cut -d= -f2-)
DB_USER=$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$PG_CONTAINER" | grep -E '^POSTGRES_USER=' | cut -d= -f2-)
DB_PASS=$(docker inspect --format '{{range .Config.Env}}{{println .}}{{end}}' "$PG_CONTAINER" | grep -E '^POSTGRES_PASSWORD=' | cut -d= -f2-)
[ -n "$DB_NAME" ] && [ -n "$DB_USER" ] && [ -n "$DB_PASS" ] || fail "no pude extraer POSTGRES_* del contenedor"
ok "BD=$DB_NAME USER=$DB_USER"

# --- 3) Staging ---
mkdir -p "$STAGING"
log "Staging: $STAGING"

# --- 4) pg_dump ---
log "pg_dump de $DB_NAME"
docker exec "$PG_CONTAINER" sh -c \
    "PGPASSWORD='$DB_PASS' pg_dump -U '$DB_USER' -d '$DB_NAME' --no-owner --no-privileges --clean --if-exists -Fc -f /tmp/visal.dump"
docker cp "$PG_CONTAINER:/tmp/visal.dump" "$STAGING/db.dump"
docker exec "$PG_CONTAINER" sh -c 'rm -f /tmp/visal.dump' >/dev/null 2>&1 || true
DB_SIZE=$(stat -c%s "$STAGING/db.dump")
ok "db.dump $(( DB_SIZE / 1024 / 1024 )) MB"

# --- 5) uploads.tar.gz (desde el volumen, usando alpine efimero) ---
log "Empaquetando volumen $UPLOADS_VOLUME"
docker run --rm \
    -v "$UPLOADS_VOLUME:/data:ro" \
    -v "$STAGING:/out" \
    alpine sh -c "cd /data && tar -czf /out/uploads.tar.gz ."
UP_SIZE=$(stat -c%s "$STAGING/uploads.tar.gz")
ok "uploads.tar.gz $(( UP_SIZE / 1024 / 1024 )) MB"

# --- 6) docker-compose.yml ---
cp "$COMPOSE_FILE" "$STAGING/docker-compose.yml"
ok "docker-compose.yml"

# --- 7) Conteos + image tag ---
log "Metricas de la BD"
COUNTS=$(docker exec "$PG_CONTAINER" sh -c \
    "PGPASSWORD='$DB_PASS' psql -U '$DB_USER' -d '$DB_NAME' -tAc \"select (select count(*) from tenants) || '|' || (select count(*) from pacientes) || '|' || (select count(*) from historias_clinicas) || '|' || (select count(*) from platform_users)\"" 2>/dev/null || echo "0|0|0|0")
IFS='|' read -r TENANTS PACIENTES HCS USERS <<<"$COUNTS"
IMAGE=$(docker inspect --format '{{.Config.Image}}' "$APP_CONTAINER" 2>/dev/null || echo "unknown")
ok "tenants=$TENANTS pacientes=$PACIENTES hcs=$HCS users=$USERS"
ok "image=$IMAGE"

# Guardar un metadata parcial (el PowerShell agrega mas info)
cat >"$STAGING/metadata.remote.json" <<EOF
{
  "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)",
  "server": "$(hostname)",
  "image": "$IMAGE",
  "db": {
    "name": "$DB_NAME",
    "dumpSizeBytes": $DB_SIZE,
    "tenants": $TENANTS,
    "pacientes": $PACIENTES,
    "historiasClinicas": $HCS,
    "platformUsers": $USERS
  },
  "uploads": {
    "sizeBytes": $UP_SIZE,
    "volume": "$UPLOADS_VOLUME"
  }
}
EOF
ok "metadata.remote.json"

# --- 8) Empaquetar todo ---
log "Empaquetando en $OUT_FILE"
tar -czf "$OUT_FILE" -C "$STAGING" .
OUT_SIZE=$(stat -c%s "$OUT_FILE")
ok "OUT $(( OUT_SIZE / 1024 / 1024 )) MB"

# --- 9) Salida parseable para el PowerShell ---
echo "RESULT_FILE=$OUT_FILE"
echo "RESULT_SIZE=$OUT_SIZE"
echo "RESULT_TENANTS=$TENANTS"
echo "RESULT_PACIENTES=$PACIENTES"
echo "RESULT_HCS=$HCS"
echo "RESULT_USERS=$USERS"
echo "RESULT_IMAGE=$IMAGE"
