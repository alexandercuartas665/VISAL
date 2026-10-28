using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixDriftTurnoProgramacionSucursales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reparacion de drift: la entidad TurnoProgramacionSucursal vive en el
            // modelo y en el ModelSnapshot, pero ninguna migracion creaba la tabla.
            // Las BD reales (prod/dev) ya la tienen (creada fuera de la cadena de
            // migraciones en algun punto historico), por eso el SQL es IDEMPOTENTE:
            // no-op donde ya existe, y la crea en BD frescas (tests, recuperacion
            // desde cero, entorno nuevo). El DDL replica exactamente el del modelo.
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS turno_programacion_sucursales (
    id uuid NOT NULL DEFAULT gen_random_uuid(),
    turno_programacion_id uuid NOT NULL,
    sucursal_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    created_at timestamp with time zone NOT NULL DEFAULT now(),
    created_by uuid,
    updated_at timestamp with time zone,
    updated_by uuid,
    CONSTRAINT pk_turno_programacion_sucursales PRIMARY KEY (id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ix_tps_prog_sede
    ON turno_programacion_sucursales (turno_programacion_id, sucursal_id);

CREATE INDEX IF NOT EXISTS ix_tps_tenant_sucursal
    ON turno_programacion_sucursales (tenant_id, sucursal_id);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tps_sucursal') THEN
        ALTER TABLE turno_programacion_sucursales
            ADD CONSTRAINT fk_tps_sucursal FOREIGN KEY (sucursal_id)
            REFERENCES sucursales (id) ON DELETE RESTRICT;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'fk_tps_turno_prog') THEN
        ALTER TABLE turno_programacion_sucursales
            ADD CONSTRAINT fk_tps_turno_prog FOREIGN KEY (turno_programacion_id)
            REFERENCES turno_programaciones (id) ON DELETE CASCADE;
    END IF;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op a proposito: esta migracion solo repara un drift. La tabla
            // preexiste en los entornos reales con datos; revertir NO debe borrarla.
        }
    }
}
