# Despliegue Visal en otro servidor con Docker

Stack de produccion: **imagen publicada en GHCR** + **Postgres en el mismo Docker** + **Caddy con HTTPS automatico**.

```
Internet
   │  (TLS Let's Encrypt)
   ▼
[ Caddy :80/:443 ]  ──reverse proxy──▶  [ visal-app :8080 ]  ──TCP──▶  [ postgres :5432 ]
                                              ▲                              │
                                              └─── red interna `visal-net` ──┘
                                                         (no expuestos al host)
```

---

## 1. En tu maquina (una sola vez): configurar el registry

### Cuando termines un cambio importante:
```bash
git push origin main
```
El workflow **`.github/workflows/publish-image.yml`** corre automaticamente y publica:
- `ghcr.io/alexandercuartas665/visal/superadmin:latest` (siempre la ultima de main)
- `ghcr.io/alexandercuartas665/visal/superadmin:sha-<7chars>` (immutable por commit)
- Si haces un tag `vX.Y.Z`: tambien `vX.Y.Z` y `vX.Y`.

Mira el progreso en GitHub → Actions tab.

### Hacer la imagen publica o privada
Por defecto las imagenes de GHCR son **privadas**. Para descargar desde el servidor sin loguear, hacerla **publica**:

GitHub → tu perfil → **Packages** → `visal/superadmin` → **Package settings** → "Change visibility" → **Public**.

Si la dejas privada, en el servidor necesitas un `docker login ghcr.io` con un Personal Access Token (PAT) con permiso `read:packages`.

---

## 2. Setup en el servidor (una sola vez)

### Requisitos del servidor
- Docker 24+ con `docker compose v2`
- Puertos 80 y 443 abiertos hacia internet
- DNS: un **A record** de tu dominio (ej. `visal.tudominio.com`) apuntando a la IP del servidor
- 2 GB RAM minimo (4 GB recomendado)

### Pasos

```bash
# 1. Crear carpeta de despliegue
mkdir -p /opt/visal && cd /opt/visal

# 2. Bajar SOLO los archivos de deploy (no necesitas el repo completo):
curl -O https://raw.githubusercontent.com/alexandercuartas665/VISAL/main/deploy/docker-prod/docker-compose.yml
curl -O https://raw.githubusercontent.com/alexandercuartas665/VISAL/main/deploy/docker-prod/Caddyfile
curl -O https://raw.githubusercontent.com/alexandercuartas665/VISAL/main/deploy/docker-prod/.env.example
cp .env.example .env

# 3. Editar .env con tus valores reales:
nano .env
#   VISAL_DOMAIN=visal.tudominio.com
#   ACME_EMAIL=admin@tudominio.com
#   POSTGRES_PASSWORD=<clave larga aleatoria>

# 4. Si la imagen es PRIVADA, login en GHCR (una sola vez):
#    Crea un PAT en github.com/settings/tokens con permiso read:packages
echo "TU_PAT_AQUI" | docker login ghcr.io -u alexandercuartas665 --password-stdin

# 5. Arrancar
docker compose pull
docker compose up -d

# 6. Ver logs en vivo del arranque
docker compose logs -f visal-app
```

Caddy obtendra el certificado Let's Encrypt automaticamente la primera vez que alguien acceda a `https://visal.tudominio.com`.

### Verificar
```bash
docker compose ps                   # los 3 contenedores Healthy/Up
curl -I https://visal.tudominio.com # 200 OK con cabeceras HTTPS
```

Abre `https://visal.tudominio.com/login` en el navegador → debe aparecer la pantalla de login de Visal.

---

## 3. Cuando haya una version nueva

```bash
cd /opt/visal
docker compose pull
docker compose up -d
docker compose logs -f visal-app   # verificar arranque OK
```

Eso es todo. `VISAL_RUN_MIGRATIONS=true` se encarga de aplicar las migraciones EF nuevas en cada arranque. Los datos de Postgres persisten en el volumen `visal-pgdata`.

### Pinear una version concreta (no usar `latest`)
En `.env`:
```env
VISAL_IMAGE=ghcr.io/alexandercuartas665/visal/superadmin:sha-abcd123
```
Luego `docker compose up -d` carga esa version exacta.

---

## 4. Backups de Postgres

El volumen `visal-pgdata` esta en `/var/lib/docker/volumes/visal-prod_visal-pgdata`.

### Dump rapido
```bash
docker exec visal-postgres pg_dump -U visal -d visal | gzip > /backups/visal-$(date +%F).sql.gz
```

### Crontab diario a las 3am
```cron
0 3 * * * docker exec visal-postgres pg_dump -U visal -d visal | gzip > /backups/visal-$(date +\%F).sql.gz
```

Te recomiendo subir los `.sql.gz` a S3/Backblaze/Google Drive con `rclone` o `restic`.

### Restaurar
```bash
gunzip < /backups/visal-2026-05-28.sql.gz | docker exec -i visal-postgres psql -U visal -d visal
```

---

## 5. Operaciones comunes

| Tarea | Comando |
|---|---|
| Ver logs del app | `docker compose logs -f visal-app` |
| Ver logs de Postgres | `docker compose logs -f postgres` |
| Reiniciar solo el app | `docker compose restart visal-app` |
| Detener todo | `docker compose down` (mantiene datos) |
| Borrar TODO incluido los datos | `docker compose down -v` |
| Shell en el contenedor del app | `docker exec -it visal-app /bin/bash` |
| Shell de Postgres | `docker exec -it visal-postgres psql -U visal -d visal` |
| Liberar espacio en disco | `docker system prune -af --volumes` (cuidado con `-v`) |

---

## 6. Troubleshooting

### "no such image" al hacer `docker compose pull`
- La imagen no se publico todavia (revisa GitHub Actions tab).
- O la imagen es **privada** y no hiciste `docker login ghcr.io`.

### Caddy no obtiene certificado
- Revisa que el DNS A record apunte a la IP del servidor (`dig +short visal.tudominio.com`).
- Revisa que los puertos 80 y 443 esten abiertos en el firewall del servidor y del proveedor cloud.
- `docker compose logs caddy` muestra los errores ACME.

### "Connection refused" al app
- Postgres aun no esta listo (el healthcheck tarda ~20-60s en primer arranque). Espera y reintenta.
- `docker compose logs postgres` para ver si arranco bien.

### Migraciones EF fallaron
- `docker compose logs visal-app` muestra el error de EF.
- Si necesitas saltarte una migracion: cambia `VISAL_RUN_MIGRATIONS=false` en `.env` y haz `docker compose up -d` para tener el app sin migrar, luego conectate al postgres y aplica/restaura manualmente.

---

## 7. Apagado limpio para mudanza

```bash
cd /opt/visal
docker exec visal-postgres pg_dump -U visal -d visal | gzip > /tmp/visal-final.sql.gz
docker compose down
tar czf /tmp/visal-deploy.tar.gz docker-compose.yml Caddyfile .env
# Lleva /tmp/visal-final.sql.gz y /tmp/visal-deploy.tar.gz al nuevo servidor.
```

En el nuevo servidor, repites los pasos del paso 2 y luego restauras el dump.
