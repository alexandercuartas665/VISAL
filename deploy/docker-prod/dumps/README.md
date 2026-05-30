# dumps/

Dumps de PostgreSQL para inicializar la BD de produccion.

**Los archivos `*.dump` y `*.sql.gz` NO se versionan** (ver `.gitignore`).

Cada vez que quieras llevar tu BD de dev al server:

1. Genera el dump aqui (ver instrucciones en `../README.md` seccion 7).
2. Copia el archivo `.dump` al server por `scp`/`rsync`.
3. Restauralo con `pg_restore` segun la guia.
