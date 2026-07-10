# Backup y restauracion de Visal

Este directorio contiene los scripts PowerShell para hacer copias de seguridad
completas del sistema Visal (BD + uploads + config) capaces de re-implementar
la operacion en otro servidor Docker.

## Contenido

```
Backup/
├── backup-visal.ps1         # crea el ZIP de backup
├── restore-visal.ps1        # restaura un ZIP en un docker-compose limpio
├── config.example.json      # plantilla de config para tarea programada
├── lib/
│   └── Crypto.ps1           # helper AES-256-CBC + PBKDF2 (cifrar/descifrar el .env)
└── README.md                # este archivo
```

## Que se guarda en cada backup

Cada ZIP `visal-backup-YYYYMMDD-HHMMSS.zip` contiene:

| Archivo               | Contenido                                                  |
|-----------------------|------------------------------------------------------------|
| `db.dump`             | `pg_dump --format=custom` de toda la BD                    |
| `uploads.tar.gz`      | volumen `visal-uploads` (firmas, PDFs, logos, adjuntos)    |
| `docker-compose.yml`  | snapshot del compose que estaba activo                     |
| `env.aes`             | el `.env` real cifrado con AES-256 + tu clave              |
| `metadata.json`       | image tag, timestamp, git commit, conteos, tamanos         |
| `RESTORE.md`          | instrucciones paso a paso para restaurar                   |

## Uso rapido (prueba en dev con el docker local)

```powershell
cd C:\DesarrolloIA\Visal\Backup

# 1) Crear backup local
.\backup-visal.ps1

# Preguntara la clave del .env - inventala si es dev (no hay secretos criticos).
# El ZIP queda en:  C:\Users\<tu-user>\AppData\Local\Temp\visal-backups\
```

## Uso en prod (docker remoto via SSH)

Primero configura el docker context SSH (una sola vez):

```powershell
docker context create visal-prod-remote --docker "host=ssh://bit-admin@10.0.1.6"
docker context ls  # verifica que quedo visal-prod-remote
```

Luego:

```powershell
.\backup-visal.ps1 `
    -DockerContext visal-prod-remote `
    -DestinationRoot "D:\Backups\Visal"    # cuando conectes el disco D
```

De momento (sin disco D), el default `C:\Users\<user>\AppData\Local\Temp\visal-backups`
sirve para probar el flujo.

## Restauracion (probar el backup)

Un backup nunca probado NO es un backup. Restaura en un directorio limpio con
el docker local para asegurarte de que el ciclo cierra:

```powershell
.\restore-visal.ps1 `
    -ZipPath "$env:TEMP\visal-backups\visal-backup-YYYYMMDD-HHMMSS.zip" `
    -TargetDir "C:\DesarrolloIA\VisalRestore"
```

Ver `RESTORE.md` dentro del ZIP para instrucciones manuales tambien.

## Tarea programada (Task Scheduler de Windows)

Crea una tarea que corra diario (por ejemplo 3am):

**Trigger:** Diario 03:00

**Action:**
```
Program:   powershell.exe
Arguments: -ExecutionPolicy Bypass -File "C:\DesarrolloIA\Visal\Backup\backup-visal.ps1" -DockerContext visal-prod-remote -DestinationRoot "D:\Backups\Visal" -EncryptionPassword (ConvertTo-SecureString "TU-CLAVE" -AsPlainText -Force)
```

O usa un wrapper que lea la clave de un archivo cifrado con DPAPI:

```powershell
# save-key-once.ps1  (ejecutar UNA vez para guardar la clave)
$sec = Read-Host "Clave" -AsSecureString
$sec | ConvertFrom-SecureString | Set-Content "$env:USERPROFILE\.visal-backup.key"

# run-backup.ps1  (esto es lo que corre la tarea)
$sec = Get-Content "$env:USERPROFILE\.visal-backup.key" | ConvertTo-SecureString
& "C:\DesarrolloIA\Visal\Backup\backup-visal.ps1" -DockerContext visal-prod-remote -DestinationRoot "D:\Backups\Visal" -EncryptionPassword $sec
```

DPAPI ata el archivo al usuario Windows, asi que solo tu cuenta puede leerlo.

## Rotacion

Por default el script mantiene los ultimos **14 backups** en el `DestinationRoot`
y borra los mas viejos. Ajustable con `-KeepLast N`.

Estrategia recomendada cuando conectes el disco D:
- Diario 14 dias -> `D:\Backups\Visal\` (esta carpeta)
- Copia manual mensual del ultimo -> `D:\Backups\Visal\Mensuales\`
- Backup off-site (OneDrive Bitcode / S3 Contabo) semanal -> a definir en fase 2

## Consideraciones importantes

- **Consistencia BD - uploads:** `pg_dump` toma snapshot atomico transaccional
  de la BD, pero mientras se `tar`-ea uploads pueden crearse archivos nuevos
  (ventana de ~30s con visal activo). Para operacion 24/7 esto es aceptable con
  backups diarios; si hace falta cero drift, poner Visal en modo mantenimiento
  antes del script.

- **Secretos:** el `.env` va SIEMPRE cifrado en el ZIP (AES-256-CBC + PBKDF2
  200k). Guarda la clave en un password manager - sin ella no se puede
  descifrar.

- **Tamano:** un tenant con ~100 HC + firmas + PDFs = ~200MB. En 12 meses
  operando 2-5GB es normal. El ZIP con compresion Optimal reduce ~30-40%.

- **Imagen GHCR:** el ZIP no incluye la imagen Docker (seria enorme). Al
  restaurar el server destino hace `docker compose pull` del tag guardado en
  `metadata.imageTag`. Si es privada, `docker login ghcr.io` antes.
