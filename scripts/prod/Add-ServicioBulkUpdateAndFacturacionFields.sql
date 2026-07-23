-- =========================================================================
-- Add-ServicioBulkUpdateAndFacturacionFields.sql
-- Migracion EF: 20260723023501_AddServicioBulkUpdateAndFacturacionFields
--
-- Aplica a produccion 2 cambios:
--   (1) 3 columnas nuevas en servicios_contrato:
--       - modalidad_facturacion        VARCHAR(80)  NULL
--       - grupo_servicio_facturacion   VARCHAR(120) NULL
--       - servicio_facturacion         VARCHAR(200) NULL
--   (2) 2 tablas nuevas para la utilidad "Actualizar en masa"
--       (buscar servicios por Descripcion + aplicar los 3 campos + rollback):
--       - servicio_bulk_updates        (ejecuciones, cabecera)
--       - servicio_bulk_update_items   (snapshot antes-de: 1 fila por servicio afectado)
--
-- Idempotente: usa IF NOT EXISTS. Se puede ejecutar N veces sin efecto.
-- Registra la migracion en "__EFMigrationsHistory" para que EF no la
-- intente re-aplicar al proximo `dotnet ef database update`.
-- =========================================================================

BEGIN;

-- (1) servicios_contrato: 3 columnas facturacion ---------------------------
ALTER TABLE servicios_contrato
    ADD COLUMN IF NOT EXISTS modalidad_facturacion      VARCHAR(80);

ALTER TABLE servicios_contrato
    ADD COLUMN IF NOT EXISTS grupo_servicio_facturacion VARCHAR(120);

ALTER TABLE servicios_contrato
    ADD COLUMN IF NOT EXISTS servicio_facturacion       VARCHAR(200);

-- (2a) Tabla cabecera de ejecuciones bulk ---------------------------------
CREATE TABLE IF NOT EXISTS servicio_bulk_updates (
    id                                uuid                        NOT NULL,
    operador_busqueda                 VARCHAR(20)                 NOT NULL,
    texto_busqueda                    VARCHAR(300)                NOT NULL,
    nueva_modalidad_facturacion       VARCHAR(80)                 NULL,
    nuevo_grupo_servicio_facturacion  VARCHAR(120)                NULL,
    nuevo_servicio_facturacion        VARCHAR(200)                NULL,
    motivo                            VARCHAR(500)                NOT NULL,
    total_afectados                   integer                     NOT NULL,
    estado                            VARCHAR(20)                 NOT NULL,
    fecha_reversion                   timestamp with time zone    NULL,
    revertido_por                     uuid                        NULL,
    created_at                        timestamp with time zone    NOT NULL,
    created_by                        uuid                        NULL,
    updated_at                        timestamp with time zone    NULL,
    updated_by                        uuid                        NULL,
    tenant_id                         uuid                        NOT NULL,
    CONSTRAINT pk_servicio_bulk_updates PRIMARY KEY (id)
);

CREATE INDEX IF NOT EXISTS ix_servicio_bulk_updates_tenant_id_created_at
    ON servicio_bulk_updates (tenant_id, created_at);

-- (2b) Tabla items (snapshot valores previos por servicio afectado) -------
CREATE TABLE IF NOT EXISTS servicio_bulk_update_items (
    id                                    uuid                        NOT NULL,
    bulk_update_id                        uuid                        NOT NULL,
    servicio_contrato_id                  uuid                        NOT NULL,
    modalidad_facturacion_antes           VARCHAR(80)                 NULL,
    grupo_servicio_facturacion_antes      VARCHAR(120)                NULL,
    servicio_facturacion_antes            VARCHAR(200)                NULL,
    created_at                            timestamp with time zone    NOT NULL,
    created_by                            uuid                        NULL,
    updated_at                            timestamp with time zone    NULL,
    updated_by                            uuid                        NULL,
    tenant_id                             uuid                        NOT NULL,
    CONSTRAINT pk_servicio_bulk_update_items PRIMARY KEY (id),
    CONSTRAINT fk_servicio_bulk_update_items_servicio_bulk_updates_bulk_updat
        FOREIGN KEY (bulk_update_id)
        REFERENCES servicio_bulk_updates (id)
        ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_servicio_bulk_update_items_bulk_update_id
    ON servicio_bulk_update_items (bulk_update_id);

CREATE INDEX IF NOT EXISTS ix_servicio_bulk_update_items_tenant_id_bulk_update_id
    ON servicio_bulk_update_items (tenant_id, bulk_update_id);

CREATE INDEX IF NOT EXISTS ix_servicio_bulk_update_items_tenant_id_servicio_contrato_id
    ON servicio_bulk_update_items (tenant_id, servicio_contrato_id);

-- Registrar en __EFMigrationsHistory (evita re-aplicacion por EF) ---------
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260723023501_AddServicioBulkUpdateAndFacturacionFields', '9.0.11')
ON CONFLICT ("MigrationId") DO NOTHING;

COMMIT;

-- Verificacion rapida (opcional, correr fuera de la transaccion):
-- SELECT column_name, data_type, character_maximum_length
--   FROM information_schema.columns
--  WHERE table_name = 'servicios_contrato'
--    AND column_name IN ('modalidad_facturacion','grupo_servicio_facturacion','servicio_facturacion');
--
-- SELECT tablename FROM pg_tables
--  WHERE tablename IN ('servicio_bulk_updates','servicio_bulk_update_items');
--
-- SELECT "MigrationId" FROM "__EFMigrationsHistory"
--  WHERE "MigrationId" = '20260723023501_AddServicioBulkUpdateAndFacturacionFields';
