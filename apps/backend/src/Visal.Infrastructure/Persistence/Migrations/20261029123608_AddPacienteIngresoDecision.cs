using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPacienteIngresoDecision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ingreso",
                table: "pacientes",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_no_ingreso",
                table: "pacientes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ingreso",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "motivo_no_ingreso",
                table: "pacientes");
        }
    }
}
