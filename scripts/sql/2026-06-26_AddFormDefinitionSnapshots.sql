-- Migracion: AddFormDefinitionSnapshots
-- Aplica el equivalente de la migracion EF Core 20260626134310 contra una
-- BD prod ya existente. Idempotente: si la tabla / trigger / funcion ya
-- existen, no falla. Tambien registra la migracion en __EFMigrationsHistory
-- para que el siguiente `dotnet ef database update` no la reaplique.
--
-- Uso:
--   docker cp ./2026-06-26_AddFormDefinitionSnapshots.sql visal-postgres-prod:/tmp/m.sql
--   docker exec visal-postgres-prod psql -U <usuario> -d <db> -v ON_ERROR_STOP=1 -f /tmp/m.sql

BEGIN;

-- ============================================================================
-- 1) Tabla form_definition_snapshots
-- ============================================================================
CREATE TABLE IF NOT EXISTS form_definition_snapshots (
    id                   uuid                     NOT NULL,
    form_definition_id   uuid                     NOT NULL,
    codigo               character varying(40)    NOT NULL,
    nombre               character varying(200)   NOT NULL,
    version              character varying(20),
    tipo                 character varying(40),
    schema_json          jsonb                    NOT NULL,
    prefill_routes_json  jsonb,
    activo               boolean                  NOT NULL,
    snapshot_at          timestamp with time zone NOT NULL,
    snapshot_by          uuid,
    motivo               character varying(80),
    created_at           timestamp with time zone NOT NULL,
    created_by           uuid,
    updated_at           timestamp with time zone,
    updated_by           uuid,
    tenant_id            uuid                     NOT NULL,
    CONSTRAINT pk_form_definition_snapshots PRIMARY KEY (id),
    CONSTRAINT fk_form_definition_snapshots_form_definitions_form_definition_
        FOREIGN KEY (form_definition_id) REFERENCES form_definitions(id) ON DELETE CASCADE
);

-- ============================================================================
-- 2) Indice descendente para que el orden "ultimos 20" sea rapido.
-- ============================================================================
CREATE INDEX IF NOT EXISTS ix_form_definition_snapshots_form_definition_id_snapshot_at
    ON form_definition_snapshots (form_definition_id, snapshot_at DESC);

-- ============================================================================
-- 3) Funcion del trigger. Solo dispara si algun campo "de contenido" cambio
--    (IS DISTINCT FROM). UPDATEs que solo tocan updated_at/updated_by NO
--    generan snapshot. Tras insertar, rota in-place para conservar los 20
--    mas recientes por form_definition_id.
-- ============================================================================
CREATE OR REPLACE FUNCTION fn_snapshot_form_definition() RETURNS trigger AS $$
BEGIN
    IF (OLD.schema_json IS DISTINCT FROM NEW.schema_json
        OR OLD.codigo  IS DISTINCT FROM NEW.codigo
        OR OLD.nombre  IS DISTINCT FROM NEW.nombre
        OR OLD.version IS DISTINCT FROM NEW.version
        OR OLD.tipo    IS DISTINCT FROM NEW.tipo
        OR OLD.activo  IS DISTINCT FROM NEW.activo
        OR OLD.prefill_routes_json IS DISTINCT FROM NEW.prefill_routes_json) THEN
        INSERT INTO form_definition_snapshots
            (id, tenant_id, form_definition_id,
             codigo, nombre, version, tipo, schema_json, prefill_routes_json, activo,
             snapshot_at, snapshot_by, motivo,
             created_at, created_by, updated_at, updated_by)
        VALUES
            (gen_random_uuid(), OLD.tenant_id, OLD.id,
             OLD.codigo, OLD.nombre, OLD.version, OLD.tipo,
             OLD.schema_json, OLD.prefill_routes_json, OLD.activo,
             NOW(), NULL, 'auto-trigger',
             NOW(), NULL, NOW(), NULL);

        DELETE FROM form_definition_snapshots
        WHERE form_definition_id = OLD.id
          AND id NOT IN (
            SELECT id FROM form_definition_snapshots
            WHERE form_definition_id = OLD.id
            ORDER BY snapshot_at DESC
            LIMIT 20
          );
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- ============================================================================
-- 4) Trigger BEFORE UPDATE. Re-crear lo deja idempotente.
-- ============================================================================
DROP TRIGGER IF EXISTS trg_form_definition_snapshot ON form_definitions;
CREATE TRIGGER trg_form_definition_snapshot
    BEFORE UPDATE ON form_definitions
    FOR EACH ROW EXECUTE FUNCTION fn_snapshot_form_definition();

-- ============================================================================
-- 5) Registrar la migracion en __EFMigrationsHistory para que el proximo
--    `dotnet ef database update` no intente re-aplicarla.
-- ============================================================================
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260626134310_AddFormDefinitionSnapshots', '9.0.4')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;

-- ============================================================================
-- Verificacion rapida (puedes correrla por separado):
--   \d form_definition_snapshots
--   \df fn_snapshot_form_definition
--   SELECT tgname FROM pg_trigger WHERE tgname = 'trg_form_definition_snapshot';
-- ============================================================================
