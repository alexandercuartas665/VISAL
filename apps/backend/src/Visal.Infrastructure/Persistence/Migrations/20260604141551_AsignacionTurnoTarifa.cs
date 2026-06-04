using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AsignacionTurnoTarifa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Usamos SQL crudo con IF NOT EXISTS porque la columna pudo haberse
            // agregado a mano en algunas BD de dev antes de generar la migracion.
            // Asi el script es idempotente y no rompe en esos ambientes.
            migrationBuilder.Sql("ALTER TABLE asignacion_turnos ADD COLUMN IF NOT EXISTS tarifa numeric;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tarifa",
                table: "asignacion_turnos");
        }
    }
}
