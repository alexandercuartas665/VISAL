# Despliegue Visal en otro servidor con Docker

Este compose asume que el servidor **ya tiene un reverse proxy global** (nginx-proxy-manager, traefik, otro Caddy, etc.) escuchando en 80/443 y manejando TLS para todos los servicios. Este stack solo expone visal-app en un **puerto local uncommon** que tu proxy reenvia.

```
Internet
   │  (TLS Let's Encrypt en TU proxy global)
   ▼
[ tu reverse proxy global :80/:443 ]
   │  (HTTP, red local del servidor)
   ▼
[ visal-app : ${VISAL_PORT} ]  ──TCP red interna──▶  [ postgres :5432 ]
   (bindeado a 127.0.0.1 del host,
    inalcanzable desde internet)
```

---

## 1. En tu maquina (una sola vez): publicar la imagen

Cada vez que pushees a `main`, GitHub Actions construye y publica:
- `ghcr.io/alexandercuartas665/visal/superadmin:latest` (ultimo de main)
- `ghcr.io/alexandercuartas665/visal/superadmin:sha-<7chars>` (immutable por commit)
- Si haces tag `vX.Y.Z`: tambien `vX.Y.Z` y `vX.Y`.

Mira el progreso en GitHub → **Actions** tab.

### ¿Imagen privada o publica?
Por default es **privada**. Hacerla publica (sin login en el servidor):
GitHub → tu perfil → **Packages** → `visal/superadmin` → **Package settings** → "Change visibility" → **Public**.

Si la dejas privada, en el servidor necesitas `docker login ghcr.io` con un PAT que tenga `read:packages`.

---

## 2. Setup en el servidor (una sola vez)

### Requisitos
- Docker 24+ con `docker compose v2`
- Tu reverse proxy global ya esta operando en 80/443
- Puerto local que NO este en uso por otros servicios (default `5380`)

### Pasos

```bash
# 1. Carpeta de despliegue
mkdir -p /opt/visal && cd /opt/visal

# 2. Bajar los archivos (no necesitas el repo completo)
curl -O https://raw.githubusercontent.com/alexandercuartas665/VISAL/main/deploy/docker-prod/docker-compose.yml
curl -O https://raw.githubusercontent.com/alexandercuartas665/VISAL/main/deploy/docker-prod/.env.example
curl -O https://raw.githubusercontent.com/alexandercuartas665/VISAL/main/deploy/docker-prod/deploy.sh
chmod +x deploy.sh
cp .env.example .env

# 3. Edita .env: VISAL_PORT, POSTGRES_PASSWORD, etc.
nano .env

# 4. Si la imagen quedo PRIVADA en GHCR, login una sola vez:
echo "TU_PAT_AQUI" | docker login ghcr.io -u alexandercuartas665 --password-stdin

# 5. Arrancar
docker compose pull
docker compose up -d

# 6. Verificar
curl http://127.0.0.1:5380/login          # debe responder HTML del login
docker compose logs -f visal-app
```

---

## 3. Apuntar tu reverse proxy global hacia visal-app

Solo tienes que decirle a TU proxy global "para `visal.midominio.com`, reverse_proxy a `http://localhost:5380`".

### Si tu proxy global es **nginx-proxy-manager**
1. Proxy Hosts → Add Proxy Host
2. Domain Names: `visal.midominio.com`
3. Scheme: `http`
4. Forward Hostname/IP: `host.docker.internal` o `127.0.0.1`
5. Forward Port: `5380`
6. ✅ Block Common Exploits
7. ✅ **Websockets Support** (CRITICO — Blazor Server usa WS para el circuito SignalR)
8. Tab SSL → Request a new SSL Certificate (Let's Encrypt) → Force SSL ON

### Si tu proxy global es **Caddy** (en otra parte del servidor)
En el `Caddyfile` global:
```caddy
visal.midominio.com {
    reverse_proxy 127.0.0.1:5380
}
```
Caddy maneja TLS + WebSockets automaticamente.

### Si tu proxy global es **Traefik** (con docker labels)
Mueve el `labels:` al servicio `visal-app` en el compose:
```yaml
visal-app:
  ...
  labels:
    - "traefik.enable=true"
    - "traefik.http.routers.visal.rule=Host(`visal.midominio.com`)"
    - "traefik.http.routers.visal.entrypoints=websecure"
    - "traefik.http.routers.visal.tls.certresolver=le"
    - "traefik.http.services.visal.loadbalancer.server.port=8080"
  # En este caso quita el ports: del servicio (Traefik llega por red interna).
```
Y conecta visal-app a la red de Traefik.

### Si tu proxy global es **nginx clasico**
```nginx
server {
    listen 443 ssl http2;
    server_name visal.midominio.com;
    ssl_certificate ...;
    ssl_certificate_key ...;

    location / {
        proxy_pass http://127.0.0.1:5380;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;     # WebSockets para SignalR
        proxy_set_header Connection "upgrade";       #
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 86400;                    # Blazor mantiene la conexion
    }
}
```

> **Importante en todos los casos:** asegurate de que el proxy soporte y permita **WebSockets** para `visal.midominio.com`. Sin eso, Blazor Server cae a long-polling (funciona pero lento).

---

## 4. Updates rutinarios

```bash
cd /opt/visal
./deploy.sh           # pull + up + ultimas 30 lineas de logs
./deploy.sh logs      # tail logs del app
./deploy.sh backup    # dump de postgres a ./backups/
./deploy.sh status    # docker compose ps
```

`VISAL_RUN_MIGRATIONS=true` aplica las migraciones EF nuevas al arrancar. Los datos persisten en el volumen `visal-pgdata`.

### Pinear una version concreta
En `.env`:
```env
VISAL_IMAGE=ghcr.io/alexandercuartas665/visal/superadmin:sha-abcd123
```
Luego `docker compose up -d`.

---

## 5. Backups de Postgres

### Dump manual
```bash
./deploy.sh backup
# crea backups/visal-YYYY-MM-DD-HHMM.sql.gz
```

### Crontab diario a las 3am
```cron
0 3 * * * cd /opt/visal && ./deploy.sh backup >/dev/null 2>&1
```

Sube los `.sql.gz` a S3/Backblaze/Drive con `rclone` o `restic`.

### Restaurar
```bash
gunzip < backups/visal-2026-05-28-0300.sql.gz | docker exec -i visal-postgres-prod psql -U visal -d visal
```

---

## 6. Troubleshooting

### "no such image" al hacer `docker compose pull`
- La imagen aun no se publico (revisa GitHub Actions).
- O la imagen es privada y no hiciste `docker login ghcr.io`.

### El sitio carga pero los clicks no responden
- Casi siempre es **WebSockets no habilitado** en el reverse proxy global. Habilitalos para el host de Visal.

### Puerto 5380 ya esta en uso
- Cambia `VISAL_PORT` en `.env` por otro libre (ej. `5381`, `8765`, etc.) y `docker compose up -d` de nuevo.
- Para ver que tienes ocupado: `ss -tlnp | grep LISTEN` o `docker ps --format "{{.Ports}}"`.

### Migraciones EF fallaron
- `docker compose logs visal-app` muestra el error.
- Para arrancar sin migrar (debugging): set `VISAL_RUN_MIGRATIONS=false` en `.env` y `docker compose up -d`.

### Postgres no arranca
- `docker compose logs postgres` para ver el detalle.
- Si los datos quedaron corruptos: `docker compose down -v` BORRA TODO. Restaura desde backup despues.

---

## 7. Apagado limpio para mudanza

```bash
cd /opt/visal
./deploy.sh backup
docker compose down
tar czf /tmp/visal-deploy.tar.gz docker-compose.yml .env deploy.sh backups/
# Lleva /tmp/visal-deploy.tar.gz al nuevo servidor.
```

En el nuevo servidor, repites el setup del paso 2 y restauras el dump.
