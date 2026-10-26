using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFechaDocumentoDocumentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fecha_documento",
                table: "nota_medica_documentos",
                type: "timestamp with time zone",
                nullable: true);

            // Reparar/backfill todos los documentos ya cargados:
            //  1) Los atados a una HC toman la fecha de atencion de esa HC (fuente unica).
            //  2) El resto (y las HC sin fecha de atencion) caen a su fecha de carga (created_at).
            migrationBuilder.Sql(@"
                UPDATE nota_medica_documentos d
                SET fecha_documento = h.fecha_atencion
                FROM historias_clinicas h
                WHERE d.historia_clinica_id = h.id
                  AND h.fecha_atencion IS NOT NULL
                  AND d.fecha_documento IS NULL;");
            migrationBuilder.Sql(@"
                UPDATE nota_medica_documentos
                SET fecha_documento = created_at
                WHERE fecha_documento IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "fecha_documento",
                table: "nota_medica_documentos");
        }
    }
}
