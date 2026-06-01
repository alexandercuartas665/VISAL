#!/usr/bin/env bash
# =========================================================================
#  encender-caddy.sh
#  ------------------------------------------------------------------------
#  Activa Caddy + HTTPS automatico encima del stack de Visal ya corriendo.
#
#  Uso:
#    ./encender-caddy.sh visal.tudominio.com
#
#  Que hace:
#    1) Valida que el dominio resuelve al IP de este server.
#    2) Valida que 80 y 443 estan libres (no hay otro proxy escuchando).
#    3) Anade VISAL_DOMAIN al .env si no esta.
#    4) Levanta el stack incluyendo el overlay de Caddy.
#    5) Tail de logs de Caddy para que veas como emite el cert Let's Encrypt.
# =========================================================================

set -euo pipefail

if [[ $# -lt 1 ]]; then
    echo "Uso: $0 <dominio>"
    echo "Ej:  $0 visal.bitcode.com.co"
    exit 1
fi

DOMINIO="$1"
DEPLOY_DIR="$(pwd)"

C_CYAN=$'\e[36m'; C_GREEN=$'\e[32m'; C_GRAY=$'\e[90m'
C_YELLOW=$'\e[33m'; C_RED=$'\e[31m'; C_RESET=$'\e[0m'

step() { echo; echo "${C_CYAN}==> $1${C_RESET}"; }
ok()   { echo "    ${C_GREEN}OK   ${C_RESET} $1"; }
info() { echo "    ${C_GRAY}info ${C_RESET} $1"; }
warn() { echo "    ${C_YELLOW}WARN ${C_RESET} $1"; }
err()  { echo "    ${C_RED}ERR  ${C_RESET} $1" >&2; }

# ---------- 1) Validar archivos ----------------------------------------
step "Validando archivos del overlay"
for f in docker-compose.yml docker-compose.caddy.yml Caddyfile .env; do
    if [[ ! -f "$f" ]]; then
        err "Falta '$f' en $DEPLOY_DIR. Copialo desde el repo y reintenta."
        exit 1
    fi
done
ok "docker-compose.yml, docker-compose.caddy.yml, Caddyfile, .env presentes"

# ---------- 2) Validar DNS ---------------------------------------------
step "Validando que '$DOMINIO' resuelve al IP de este server"

IP_PUBLICO="$(curl -fsS -m 5 https://ifconfig.me 2>/dev/null || echo '')"
IP_DOMINIO="$(dig +short A "$DOMINIO" 2>/dev/null | head -n1)"

if [[ -z "$IP_PUBLICO" ]]; then
    warn "No pude detectar el IP publico (sin internet?). Sigo sin validar."
elif [[ -z "$IP_DOMINIO" ]]; then
    err "El dominio '$DOMINIO' no tiene registro A (o el DNS aun no propago)."
    err "Crea el registro A apuntando a $IP_PUBLICO y espera 5-10 min."
    exit 1
elif [[ "$IP_DOMINIO" != "$IP_PUBLICO" ]]; then
    warn "El dominio resuelve a $IP_DOMINIO pero este server es $IP_PUBLICO."
    warn "Si es por proxy (Cloudflare, etc.), puede estar OK. Si no, Let's Encrypt fallara."
    read -r -p "    Sigo de todas formas? [s/N] " resp
    [[ ! "$resp" =~ ^[sSyY]$ ]] && exit 1
else
    ok "DNS OK: $DOMINIO -> $IP_DOMINIO (= este server)"
fi

# ---------- 3) Validar puertos 80/443 libres ---------------------------
step "Validando que 80 y 443 no esten ocupados por otro proceso"
for puerto in 80 443; do
    if ss -tln | awk '{print $4}' | grep -E ":${puerto}\$" >/dev/null; then
        # Si es Caddy de Visal ya corriendo de un intento anterior, no es problema.
        CONTAINER_EN_PUERTO="$(docker ps --filter "publish=$puerto" --format '{{.Names}}' 2>/dev/null | head -n1)"
        if [[ "$CONTAINER_EN_PUERTO" == "visal-caddy" ]]; then
            info "Puerto $puerto ya lo tiene visal-caddy (deploy previo). OK."
        else
            err "Puerto $puerto esta ocupado por: ${CONTAINER_EN_PUERTO:-otro proceso}"
            err "Para Caddy necesito ambos puertos libres. Detenlo y reintenta."
            exit 1
        fi
    else
        ok "Puerto $puerto libre"
    fi
done

# ---------- 4) Anadir VISAL_DOMAIN al .env -----------------------------
step "Asegurando VISAL_DOMAIN=$DOMINIO en .env"
if grep -E '^VISAL_DOMAIN=' .env >/dev/null 2>&1; then
    # Reemplazar
    sed -i.bak "s|^VISAL_DOMAIN=.*|VISAL_DOMAIN=$DOMINIO|" .env
    rm -f .env.bak
    info "Reemplazado VISAL_DOMAIN existente"
else
    echo "VISAL_DOMAIN=$DOMINIO" >> .env
    info "Anadido VISAL_DOMAIN"
fi
ok ".env actualizado"

# ---------- 5) Levantar overlay -----------------------------------------
step "Levantando Caddy (docker compose up -d)"
docker compose --env-file ./.env \
    -f docker-compose.yml -f docker-compose.caddy.yml \
    up -d

ok "Stack levantado con Caddy"

# ---------- 6) Tail de logs de Caddy ------------------------------------
step "Logs de Caddy (Ctrl+C para salir; el cert tarda ~30-60 seg en emitir)"
echo
echo "${C_GRAY}Estate atento a estas lineas en los logs:${C_RESET}"
echo "${C_GRAY}  - 'certificate obtained successfully'   <- cert emitido${C_RESET}"
echo "${C_GRAY}  - 'serving initial configuration'        <- listo${C_RESET}"
echo
sleep 2
docker logs -f --tail 30 visal-caddy
