using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentoHcConsecutivo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Columna nullable primero (para backfill ordenado; igual que AddConsecutivos).
            migrationBuilder.AddColumn<long>(
                name: "consecutivo",
                table: "historia_clinica_documentos",
                type: "bigint",
                nullable: true);

            // 2) Backfill: asigna consecutivos de la MISMA secuencia global de HC, en orden
            //    cronologico (created_at). Comparten numeracion con las historias clinicas,
            //    por eso no colisionan (secuencia unica y monotonica).
            migrationBuilder.Sql(@"
DO $$
DECLARE r RECORD;
BEGIN
  FOR r IN SELECT id FROM historia_clinica_documentos WHERE consecutivo IS NULL ORDER BY created_at, id LOOP
    UPDATE historia_clinica_documentos SET consecutivo = nextval('hc_consecutivo_seq') WHERE id = r.id;
  END LOOP;
END $$;");

            // 3) Default (nextval) + NOT NULL para filas nuevas.
            migrationBuilder.Sql("ALTER TABLE historia_clinica_documentos ALTER COLUMN consecutivo SET DEFAULT nextval('hc_consecutivo_seq');");
            migrationBuilder.Sql("ALTER TABLE historia_clinica_documentos ALTER COLUMN consecutivo SET NOT NULL;");

            // 4) Unico (dentro de documentos; globalmente unico via la secuencia compartida).
            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_documentos_consecutivo",
                table: "historia_clinica_documentos",
                column: "consecutivo",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_historia_clinica_documentos_consecutivo",
                table: "historia_clinica_documentos");

            migrationBuilder.DropColumn(
                name: "consecutivo",
                table: "historia_clinica_documentos");
        }
    }
}
