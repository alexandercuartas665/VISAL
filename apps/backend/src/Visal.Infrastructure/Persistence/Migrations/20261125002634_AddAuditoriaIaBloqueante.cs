using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditoriaIaBloqueante : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "auditoria_ia_bloqueo_json",
                table: "revision_policies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "tipo",
                table: "autorizacion_solicitudes",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auditoria_ia_bloqueo_json",
                table: "revision_policies");

            migrationBuilder.DropColumn(
                name: "tipo",
                table: "autorizacion_solicitudes");
        }
    }
}
