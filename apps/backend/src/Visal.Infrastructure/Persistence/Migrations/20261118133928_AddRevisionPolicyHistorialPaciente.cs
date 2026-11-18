using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionPolicyHistorialPaciente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "enviar_historial_paciente",
                table: "revision_policies",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "historial_max_completas",
                table: "revision_policies",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "historial_max_tokens",
                table: "revision_policies",
                type: "integer",
                nullable: false,
                defaultValue: 60000);

            migrationBuilder.AddColumn<int>(
                name: "historial_meses",
                table: "revision_policies",
                type: "integer",
                nullable: false,
                defaultValue: 8);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "enviar_historial_paciente",
                table: "revision_policies");

            migrationBuilder.DropColumn(
                name: "historial_max_completas",
                table: "revision_policies");

            migrationBuilder.DropColumn(
                name: "historial_max_tokens",
                table: "revision_policies");

            migrationBuilder.DropColumn(
                name: "historial_meses",
                table: "revision_policies");
        }
    }
}
