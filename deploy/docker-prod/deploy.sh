#!/usr/bin/env bash
# Helper para el servidor de produccion.
# Uso:
#   ./deploy.sh           -> pull + up (update normal)
#   ./deploy.sh restart   -> reinicia solo el app sin re-pull
#   ./deploy.sh logs      -> tail logs del app
#   ./deploy.sh backup    -> dump de la BD a ./backups/
#   ./deploy.sh status    -> ps de los contenedores
set -euo pipefail

cd "$(dirname "$0")"

cmd="${1:-update}"
case "$cmd" in
  update)
    echo "==> Pulling latest image..."
    docker compose pull
    echo "==> Restarting stack..."
    docker compose up -d
    echo "==> OK. Logs:"
    docker compose logs --tail=30 visal-app
    ;;
  restart)
    docker compose restart visal-app
    docker compose logs --tail=30 visal-app
    ;;
  logs)
    docker compose logs -f visal-app
    ;;
  backup)
    mkdir -p backups
    f="backups/visal-$(date +%F-%H%M).sql.gz"
    docker exec visal-postgres pg_dump -U "${POSTGRES_USER:-visal}" -d "${POSTGRES_DB:-visal}" | gzip > "$f"
    echo "Backup: $f ($(du -h "$f" | cut -f1))"
    ;;
  status)
    docker compose ps
    ;;
  *)
    echo "Comandos: update | restart | logs | backup | status"
    exit 1
    ;;
esac
