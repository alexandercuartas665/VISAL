# Deploy de Visal IPS RT en el server (BUILD-FROM-GIT)

Este flujo evita GHCR, SSH y scripts. Solo necesitas:

- RDP (Escritorio Remoto) al server.
- Docker Desktop ya corriendo en el server.
- Salida HTTPS a github.com desde el server.

El server clona la rama del repo, construye la imagen, y levanta el stack.

---

## Pasos en el server (una sola vez)

1. **Conectar por RDP** al `10.0.1.6` con el usuario `bit-admin`.

2. **Crear la carpeta de deploy:**
   ```pwsh
   New-Item -ItemType Directory -Path E:\DOCKER\visal -Force
   cd E:\DOCKER\visal
   ```

3. **Bajar los 2 archivos directamente desde el repo:**
   ```pwsh
   $base = "https://raw.githubusercontent.com/alexandercuartas665/VISAL/main/deploy/docker-prod"
   Invoke-WebRequest -Uri "$base/docker-compose.from-git.yml" -OutFile "docker-compose.yml"
   Invoke-WebRequest -Uri "$base/.env.from-git.example"       -OutFile ".env"
   ```

4. **Editar `.env`** (notepad o VS Code) - al menos cambia el password de Postgres:
   ```pwsh
   notepad .env
   ```
   Genera un password seguro con:
   ```pwsh
   -join ((33..126) | Get-Random -Count 32 | % { [char]$_ })
   ```
   Pega el resultado en `POSTGRES_PASSWORD=...` y guarda.

5. **Build + up:**
   ```pwsh
   docker compose build         # primer build, ~5-10 minutos (descarga .NET SDK + Chromium)
   docker compose up -d
   docker compose ps            # verifica que ambos servicios queden "running" / "healthy"
   docker compose logs -f visal-app
   ```

6. **Comprobar que responde localmente** (desde el server):
   ```pwsh
   curl.exe http://127.0.0.1:5380/login
   # Debe devolver HTML del login.
   ```

7. **Apuntar tu reverse proxy global** (NPM / Caddy / Traefik / nginx) a
   `http://127.0.0.1:5380` con WebSockets habilitados (Blazor Server usa
   SignalR sobre WS).

---

## Updates rutinarios

Cuando hagas commit nuevo en la rama:

```pwsh
cd E:\DOCKER\visal
docker compose build --no-cache    # reclona la rama y reconstruye
docker compose up -d
docker compose logs --tail=50 visal-app
```

Para pinear una version concreta sin rebuild automatico:
- Cambia `VISAL_BRANCH=main` en `.env` por `VISAL_BRANCH=v1.2.3` (un tag) o
  `VISAL_BRANCH=abcd123` (un commit sha) y haz `docker compose build && up -d`.

---

## Backups de Postgres

```pwsh
$stamp = Get-Date -Format "yyyy-MM-dd-HHmm"
docker exec visal-postgres-prod pg_dump -U visal -d visal | Set-Content -Path "E:\DOCKER\visal\backups\visal-$stamp.sql" -Encoding UTF8
```

Sugerencia: programa esto en el Task Scheduler de Windows diariamente y sube
los `.sql` (o `.sql.gz`) a tu storage offsite.

---

## Troubleshooting

### `docker compose build` falla con `failed to clone`
- Verifica desde el server: `git --version` y `Invoke-WebRequest https://github.com`.
- Si hay proxy corporativo, configura `HTTP_PROXY` / `HTTPS_PROXY` en Docker.

### Build OK pero `visal-app` queda `unhealthy`
- `docker compose logs visal-app` muestra el error real.
- Comunes: password Postgres con caracteres conflictivos, puerto 5380 ya
  tomado por otro contenedor.

### El sitio carga pero los clicks no responden
- Casi siempre es WebSockets no habilitados en el reverse proxy global.
- Habilitalos para el host de Visal.

### Puerto 5380 ya esta en uso en el server
- Cambia `VISAL_PORT=5381` (o cualquier libre) en `.env`, despues
  `docker compose up -d`.
