using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixDriftAsignacionPaqueteColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Reparacion de drift de COLUMNAS: estas columnas viven en el modelo y en
            // el ModelSnapshot, pero ninguna migracion las creaba. Las BD reales
            // (prod/dev) ya las tienen (se agregaron fuera de la cadena de migraciones
            // en algun punto historico), por eso el SQL es IDEMPOTENTE: no-op donde ya
            // existen, y las crea en BD frescas (tests con Testcontainers, recuperacion
            // desde cero, entorno nuevo). Los tipos replican el ModelSnapshot.
            migrationBuilder.Sql(@"
-- asignaciones (snapshot: varchar(40) / uuid / numeric(14,2))
ALTER TABLE asignaciones ADD COLUMN IF NOT EXISTS paquete_codigo character varying(40);
ALTER TABLE asignaciones ADD COLUMN IF NOT EXISTS paquete_instancia_id uuid;
ALTER TABLE asignaciones ADD COLUMN IF NOT EXISTS paquete_valor_pactado numeric(14,2);

-- asignacion_turnos (snapshot: text / uuid / numeric / uuid / text)
ALTER TABLE asignacion_turnos ADD COLUMN IF NOT EXISTS paquete_codigo text;
ALTER TABLE asignacion_turnos ADD COLUMN IF NOT EXISTS paquete_instancia_id uuid;
ALTER TABLE asignacion_turnos ADD COLUMN IF NOT EXISTS paquete_valor_pactado numeric;
ALTER TABLE asignacion_turnos ADD COLUMN IF NOT EXISTS turno_programacion_id uuid;
ALTER TABLE asignacion_turnos ADD COLUMN IF NOT EXISTS turno_row_nombre text;

CREATE INDEX IF NOT EXISTS ix_asignacion_turnos_turno_programacion_id
    ON asignacion_turnos (turno_programacion_id);

DO $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM pg_constraint
                   WHERE conname = 'fk_asignacion_turnos_turno_programaciones_turno_programacion_id') THEN
        ALTER TABLE asignacion_turnos
            ADD CONSTRAINT fk_asignacion_turnos_turno_programaciones_turno_programacion_id
            FOREIGN KEY (turno_programacion_id)
            REFERENCES turno_programaciones (id) ON DELETE SET NULL;
    END IF;
END $$;

-- asignacion_turno_sesiones (snapshot: numeric / text)
ALTER TABLE asignacion_turno_sesiones ADD COLUMN IF NOT EXISTS horas numeric;
ALTER TABLE asignacion_turno_sesiones ADD COLUMN IF NOT EXISTS tipo_turno_codigo text;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op a proposito: esta migracion solo repara un drift de columnas. Las
            // columnas preexisten en los entornos reales con datos; revertir NO debe
            // borrarlas.
        }
    }
}
