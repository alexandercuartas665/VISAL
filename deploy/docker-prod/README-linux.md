# Deploy Visal en servidor Linux (Ubuntu 24.04)

Guia paso a paso para desplegar Visal en `10.0.0.3` (Ubuntu 24.04 con Docker ya instalado), entrando por SSH desde tu maquina.

## Vista general

```
Internet
   |  https://visal.tudominio.com
   v
[ Reverse proxy del SERVER (lo gestiona el admin de infra) ]
   |  HTTP, red local
   v
[ visal-app :5380 (127.0.0.1) ]   <-- nuestra responsabilidad acaba aqui
   |
   v
[ visal-postgres-prod :5432 ]    <-- BD, solo red interna Docker
```

**Lo que TU haces:** levantar Visal en HTTP, escuchando solo en `127.0.0.1:5380` del server.
**Lo que hace el admin del server:** poner su reverse proxy (Caddy/nginx/Traefik global) por delante, terminar TLS, y reenviar a `127.0.0.1:5380`.

## Fase 0 - Hardening minimo del server (una sola vez, antes de cualquier deploy)

Si el admin ya te entrego un usuario no-root con clave SSH, saltate esta fase. Si todavia estas entrando como `root@10.0.0.3`:

```bash
# 0.1 Crear usuario no-root con sudo
adduser visal-deploy
usermod -aG sudo visal-deploy
usermod -aG docker visal-deploy

# 0.2 Copiar tu llave SSH publica al usuario
mkdir -p /home/visal-deploy/.ssh
# Pega aqui tu llave publica (id_ed25519.pub o id_rsa.pub):
nano /home/visal-deploy/.ssh/authorized_keys
chmod 700 /home/visal-deploy/.ssh
chmod 600 /home/visal-deploy/.ssh/authorized_keys
chown -R visal-deploy:visal-deploy /home/visal-deploy/.ssh

# 0.3 Probar el login con el usuario nuevo desde OTRA terminal:
#     ssh visal-deploy@10.0.0.3
#     Si funciona, sigues. Si no, NO continues.

# 0.4 Deshabilitar login root + password
sed -i 's/^#\?PermitRootLogin .*/PermitRootLogin prohibit-password/' /etc/ssh/sshd_config
sed -i 's/^#\?PasswordAuthentication .*/PasswordAuthentication no/' /etc/ssh/sshd_config
systemctl restart ssh

# 0.5 Rotar la clave de root (la que pusiste en chat queda quemada)
passwd root
```

A partir de aqui entras como `visal-deploy@10.0.0.3` con tu llave SSH.

> **Firewall:** no abras 80/443 desde Visal. El admin del server ya tiene su reverse proxy gestionando esos puertos. Tu app escucha solo en 127.0.0.1.

## Fase 1 - Subir los archivos al server

Desde TU maquina (PowerShell o WSL):

```bash
# 1.1 Crear carpeta destino en el server
ssh visal-deploy@10.0.0.3 'sudo mkdir -p /opt/visal && sudo chown $USER:$USER /opt/visal'

# 1.2 Copiar los archivos necesarios (4 archivos, < 1 MB total)
cd C:/DesarrolloIA/Visal/deploy/docker-prod

scp docker-compose.yml     visal-deploy@10.0.0.3:/opt/visal/
scp .env.example           visal-deploy@10.0.0.3:/opt/visal/
scp deploy-en-linux.sh     visal-deploy@10.0.0.3:/opt/visal/

# 1.3 Copiar el dump de BD
ssh visal-deploy@10.0.0.3 'mkdir -p /opt/visal/dumps'
scp dumps/visal_dev_2026-05-30-fresh.dump  visal-deploy@10.0.0.3:/opt/visal/dumps/visal_dev.dump
```

> **No copies `caddy-opcional/`.** Esos archivos no se usan en este escenario.

## Fase 2 - Primer deploy

SSH al server y corre el script:

```bash
ssh visal-deploy@10.0.0.3

cd /opt/visal
chmod +x deploy-en-linux.sh

# Si la imagen GHCR es publica:
./deploy-en-linux.sh

# Si es privada, login con PAT (necesita scope read:packages):
./deploy-en-linux.sh --ghcr-user alexandercuartas665 --ghcr-token ghp_xxxxxx
```

El script te:
- Genera un `.env` con password Postgres aleatoria de 32 chars (`chmod 600`).
- Hace `docker compose pull && up -d`.
- Espera a que Postgres este healthy.
- Restaura el dump dentro del container.
- Te muestra el conteo de filas (debe coincidir con tu local).
- Reinicia `visal-app`.
- Hace un probe HTTP a `/login`.

Al final imprime el resumen. **Apunta la password Postgres que aparece** (la necesitas para hacer backups manuales).

## Fase 3 - Verificacion antes del handoff

Antes de pedirle al admin que conecte su proxy, comprueba que la app responde en local:

```bash
# Desde el server (SSH):
curl -I http://localhost:5380/login
# Esperado: HTTP/1.1 200 OK

# Desde TU maquina, abriendo un tunel SSH temporal:
ssh -L 5380:localhost:5380 visal-deploy@10.0.0.3
# Y en tu navegador: http://localhost:5380/login
# Login con tu cedula + tu password real.
```

Si entras y ves Pacientes / Asignaciones con tus datos, **la fase tuya esta lista**.

## Fase 4 - Handoff al admin del server

Pasale al admin estos datos para que enganche su reverse proxy:

| Item | Valor |
|---|---|
| **Backend HTTP** | `http://127.0.0.1:5380` |
| **Host/IP del server** | `10.0.0.3` (mismo donde corre el proxy) |
| **WebSockets** | **Requeridos.** Blazor Server usa SignalR con WebSockets para todo. Si el proxy no los pasa, la UI se queda con un overlay "Reconnecting...". |
| **Cookies de sesion** | Marcadas `Secure`. La app ya respeta `X-Forwarded-Proto`, asi que mientras el proxy mande el header bien, las cookies funcionan. |
| **Headers que el proxy debe agregar** | `X-Forwarded-For`, `X-Forwarded-Proto`, `X-Forwarded-Host`. El app ya tiene `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true`. |
| **Health check sugerido** | `GET /login` -> debe devolver 200. **NO uses** `/health` (no existe aun). |
| **Timeouts** | Read/Write idealmente `>= 1h` para mantener conexiones SignalR vivas. |
| **HTTP/3** | Opcional. Caddy 2 lo activa solo si esta en su config. Blazor funciona bien con HTTP/1.1 y HTTP/2 tambien. |

### Snippet de ejemplo (por si el admin pide referencia)

**Caddy:**
```caddy
visal.tudominio.com {
    encode gzip zstd
    reverse_proxy 127.0.0.1:5380 {
        header_up X-Forwarded-For {remote_host}
        header_up X-Forwarded-Proto {scheme}
        transport http { read_timeout 1h; write_timeout 1h }
    }
}
```

**nginx:**
```nginx
server {
    listen 443 ssl http2;
    server_name visal.tudominio.com;
    # ssl_certificate / ssl_certificate_key ...

    location / {
        proxy_pass http://127.0.0.1:5380;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 3600s;
        proxy_send_timeout 3600s;
    }
}
```

Cuando el admin confirme que enchufo el proxy, abre `https://visal.tudominio.com/login` y deberia mostrar la app. Si la UI muestra "Reconnecting..." sin parar, **el proxy no esta pasando WebSockets**.

## Operacion diaria

```bash
# Logs de la app
docker logs -f --tail 100 visal-app

# Estado del stack
docker compose ps

# Bajar el stack (datos se conservan en el volumen)
docker compose down

# Actualizar a nueva version (despues de push a main + nuevo build GHCR)
docker compose pull && docker compose up -d

# Backup manual de BD (guarda en /opt/visal/dumps/)
docker exec -e PGPASSWORD=<la-pass-del-env> visal-postgres-prod \
    pg_dump -U visal -d visal --no-owner --no-privileges -Fc \
    -f /tmp/backup_$(date +%F).dump
docker cp visal-postgres-prod:/tmp/backup_$(date +%F).dump ./dumps/
docker exec visal-postgres-prod rm /tmp/backup_$(date +%F).dump
```

## Solucion de problemas

| Sintoma | Diagnostico |
|---|---|
| `pg_restore` exit 1 con `does not exist, skipping` | Normal en BD vacia. No es error. |
| `pg_restore` exit > 1 | Error real. Mira logs arriba. |
| `curl http://localhost:5380/login` da connection refused | El container `visal-app` no esta arriba. `docker logs visal-app`. |
| Login OK en local pero 502/504 desde internet | Reverse proxy del admin no encuentra `127.0.0.1:5380` o no pasa WebSockets. |
| UI carga pero "Reconnecting..." sin parar | WebSockets bloqueados en el proxy. |
| Cookies no persisten / `localhost:5380` redirige a HTTPS | El app cree que es HTTPS por `X-Forwarded-Proto`. Solo pasa por el proxy del admin con HTTPS, no por curl local. |

## Pendiente despues del primer login

Entra al sistema y **cambia los passwords** de:

- `admin@visal.travels`
- `demo-admin@visal.travels`

El dump trae los hash de dev (`Admin123*`). En prod **no puede quedar asi**.
