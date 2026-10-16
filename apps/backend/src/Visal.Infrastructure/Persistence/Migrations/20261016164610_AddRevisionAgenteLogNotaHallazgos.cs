using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionAgenteLogNotaHallazgos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "hallazgos_json",
                table: "revision_agente_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "nota",
                table: "revision_agente_logs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "hallazgos_json",
                table: "revision_agente_logs");

            migrationBuilder.DropColumn(
                name: "nota",
                table: "revision_agente_logs");
        }
    }
}
