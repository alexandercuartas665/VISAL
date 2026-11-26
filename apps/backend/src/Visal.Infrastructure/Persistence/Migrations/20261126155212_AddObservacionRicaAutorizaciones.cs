using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddObservacionRicaAutorizaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "observacion_html",
                table: "autorizacion_tokens",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "respuesta_html",
                table: "autorizacion_solicitudes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "observacion_html",
                table: "autorizacion_tokens");

            migrationBuilder.DropColumn(
                name: "respuesta_html",
                table: "autorizacion_solicitudes");
        }
    }
}
