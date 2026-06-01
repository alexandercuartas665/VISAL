# caddy-opcional/

Archivos de Caddy que **NO se usan** en el deploy actual de Linux (10.0.0.3).

**Por que estan aqui:** el admin del server Linux gestiona el reverse proxy y TLS por su lado (probablemente con su propio Caddy/nginx/Traefik global). Visal solo expone HTTP en `127.0.0.1:5380` y el proxy del admin termina TLS.

**Cuando usarlos:** si en algun momento te toca administrar TU mismo el reverse proxy del server (otro server, otro escenario), puedes mover estos 3 archivos de regreso a `deploy/docker-prod/` y seguir las instrucciones del README-linux.md de la version anterior.

Contenido:

- `docker-compose.caddy.yml` - overlay que anade Caddy al stack.
- `Caddyfile` - config de Caddy con TLS Let's Encrypt + headers de seguridad.
- `encender-caddy.sh` - script que valida DNS, abre puertos, levanta Caddy y emite cert.

Para reactivarlos:

```bash
cd /opt/visal
mv caddy-opcional/Caddyfile .
mv caddy-opcional/docker-compose.caddy.yml .
mv caddy-opcional/encender-caddy.sh .
chmod +x encender-caddy.sh
./encender-caddy.sh visal.tudominio.com
```
