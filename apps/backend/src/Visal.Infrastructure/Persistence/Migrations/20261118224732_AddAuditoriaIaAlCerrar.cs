using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditoriaIaAlCerrar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "auditoria_ia_al_cerrar",
                table: "revision_policies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "auditoria_ia_formatos_json",
                table: "revision_policies",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "auditoria_ia_timeout_seg",
                table: "revision_policies",
                type: "integer",
                nullable: false,
                defaultValue: 90);

            migrationBuilder.AddColumn<string>(
                name: "sugerencias_json",
                table: "revision_agente_logs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "auditoria_ia_al_cerrar",
                table: "revision_policies");

            migrationBuilder.DropColumn(
                name: "auditoria_ia_formatos_json",
                table: "revision_policies");

            migrationBuilder.DropColumn(
                name: "auditoria_ia_timeout_seg",
                table: "revision_policies");

            migrationBuilder.DropColumn(
                name: "sugerencias_json",
                table: "revision_agente_logs");
        }
    }
}
