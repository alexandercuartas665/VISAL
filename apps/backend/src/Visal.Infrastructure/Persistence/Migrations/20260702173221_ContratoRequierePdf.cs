using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContratoRequierePdf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "requiere_pdf_autorizacion",
                table: "contratos_aseguradora",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "pdf_autorizacion_url",
                table: "asignaciones",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "requiere_pdf_autorizacion",
                table: "contratos_aseguradora");

            migrationBuilder.DropColumn(
                name: "pdf_autorizacion_url",
                table: "asignaciones");
        }
    }
}
