using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAsignacionConsecutivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Consecutivo publico UNICO POR SERVICIO (AS-000045). Antes el numero visible salia
            // del LOTE (asignacion_lotes.consecutivo): todos los servicios de un guardado
            // compartian el mismo AS. Ahora cada asignacion (servicio) tiene el suyo.
            migrationBuilder.CreateSequence(name: "asignacion_item_consecutivo_seq");

            // Columna NULLABLE primero, para backfillear de forma DETERMINISTA por fecha de
            // creacion ANTES de fijar default/NOT NULL (si se agrega NOT NULL con default
            // nextval, los existentes se numeran en orden fisico arbitrario, no cronologico).
            migrationBuilder.AddColumn<long>(
                name: "consecutivo", table: "asignaciones", type: "bigint", nullable: true);

            // Renumeracion global ordenada por created_at (desempate por id, estable).
            migrationBuilder.Sql(@"
                UPDATE asignaciones a SET consecutivo = s.rn
                FROM (SELECT id, ROW_NUMBER() OVER (ORDER BY created_at, id) AS rn
                      FROM asignaciones) s
                WHERE a.id = s.id;");

            // Posicionar la secuencia: proximo nextval = max+1 (o 1 si no hay filas).
            migrationBuilder.Sql(
                "SELECT setval('asignacion_item_consecutivo_seq', COALESCE((SELECT MAX(consecutivo) FROM asignaciones),0)+1, false);");

            // Default (nextval) + NOT NULL para las filas nuevas (cualquier insercion queda numerada).
            migrationBuilder.Sql("ALTER TABLE asignaciones ALTER COLUMN consecutivo SET DEFAULT nextval('asignacion_item_consecutivo_seq');");
            migrationBuilder.Sql("ALTER TABLE asignaciones ALTER COLUMN consecutivo SET NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_consecutivo",
                table: "asignaciones",
                column: "consecutivo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_asignaciones_consecutivo",
                table: "asignaciones");

            migrationBuilder.DropColumn(
                name: "consecutivo",
                table: "asignaciones");

            migrationBuilder.DropSequence(
                name: "asignacion_item_consecutivo_seq");
        }
    }
}
