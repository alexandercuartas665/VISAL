using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPacienteCondicionYSlaIngreso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "condicion",
                table: "pacientes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "sla_ingreso_minutos",
                table: "pacientes",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "condicion",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "sla_ingreso_minutos",
                table: "pacientes");
        }
    }
}
