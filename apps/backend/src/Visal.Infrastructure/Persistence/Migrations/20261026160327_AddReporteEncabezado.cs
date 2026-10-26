using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReporteEncabezado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "codigo",
                table: "reporte_plantillas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "mostrar_encabezado",
                table: "reporte_plantillas",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "subtitulo",
                table: "reporte_plantillas",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "version",
                table: "reporte_plantillas",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "codigo",
                table: "reporte_plantillas");

            migrationBuilder.DropColumn(
                name: "mostrar_encabezado",
                table: "reporte_plantillas");

            migrationBuilder.DropColumn(
                name: "subtitulo",
                table: "reporte_plantillas");

            migrationBuilder.DropColumn(
                name: "version",
                table: "reporte_plantillas");
        }
    }
}
