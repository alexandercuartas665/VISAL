using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInteroperabilidadNumeroSede : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "numero_sede",
                table: "interoperabilidad_credenciales_sede",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "numero_sede",
                table: "interoperabilidad_credenciales_sede");
        }
    }
}
