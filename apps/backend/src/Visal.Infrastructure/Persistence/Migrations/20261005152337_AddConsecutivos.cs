using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddConsecutivos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(name: "asignacion_consecutivo_seq");
            migrationBuilder.CreateSequence(name: "hc_consecutivo_seq");

            // Columnas NULLABLE primero, para poder backfillear de forma DETERMINISTA
            // por fecha de creacion antes de fijar el default/NOT NULL. (Si se agregaran
            // NOT NULL con default nextval, los existentes se numerarian en orden fisico
            // arbitrario, no cronologico.)
            migrationBuilder.AddColumn<long>(
                name: "consecutivo", table: "historias_clinicas", type: "bigint", nullable: true);
            migrationBuilder.AddColumn<long>(
                name: "consecutivo", table: "asignacion_lotes", type: "bigint", nullable: true);

            // Backfill global, ordenado por created_at (desempate por id para ser estable).
            migrationBuilder.Sql(@"
                UPDATE historias_clinicas h SET consecutivo = s.rn
                FROM (SELECT id, ROW_NUMBER() OVER (ORDER BY created_at, id) AS rn
                      FROM historias_clinicas) s
                WHERE h.id = s.id;");
            migrationBuilder.Sql(@"
                UPDATE asignacion_lotes a SET consecutivo = s.rn
                FROM (SELECT id, ROW_NUMBER() OVER (ORDER BY created_at, id) AS rn
                      FROM asignacion_lotes) s
                WHERE a.id = s.id;");

            // Posicionar las secuencias: el proximo nextval = max+1 (o 1 si no hay filas).
            migrationBuilder.Sql(
                "SELECT setval('hc_consecutivo_seq', COALESCE((SELECT MAX(consecutivo) FROM historias_clinicas),0)+1, false);");
            migrationBuilder.Sql(
                "SELECT setval('asignacion_consecutivo_seq', COALESCE((SELECT MAX(consecutivo) FROM asignacion_lotes),0)+1, false);");

            // Default (nextval) + NOT NULL para las filas nuevas.
            migrationBuilder.Sql("ALTER TABLE historias_clinicas ALTER COLUMN consecutivo SET DEFAULT nextval('hc_consecutivo_seq');");
            migrationBuilder.Sql("ALTER TABLE historias_clinicas ALTER COLUMN consecutivo SET NOT NULL;");
            migrationBuilder.Sql("ALTER TABLE asignacion_lotes ALTER COLUMN consecutivo SET DEFAULT nextval('asignacion_consecutivo_seq');");
            migrationBuilder.Sql("ALTER TABLE asignacion_lotes ALTER COLUMN consecutivo SET NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "ix_historias_clinicas_consecutivo",
                table: "historias_clinicas", column: "consecutivo", unique: true);
            migrationBuilder.CreateIndex(
                name: "ix_asignacion_lotes_consecutivo",
                table: "asignacion_lotes", column: "consecutivo", unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_historias_clinicas_consecutivo",
                table: "historias_clinicas");

            migrationBuilder.DropIndex(
                name: "ix_asignacion_lotes_consecutivo",
                table: "asignacion_lotes");

            migrationBuilder.DropColumn(
                name: "consecutivo",
                table: "historias_clinicas");

            migrationBuilder.DropColumn(
                name: "consecutivo",
                table: "asignacion_lotes");

            migrationBuilder.DropSequence(
                name: "asignacion_consecutivo_seq");

            migrationBuilder.DropSequence(
                name: "hc_consecutivo_seq");
        }
    }
}
