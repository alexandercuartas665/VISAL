using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMipresObligatorioAndMedicamentoMipresUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "mipres_obligatorio",
                table: "sucursales",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "mipres_url",
                table: "historia_clinica_medicamentos",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "mipres_obligatorio",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "mipres_url",
                table: "historia_clinica_medicamentos");
        }
    }
}
