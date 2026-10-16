using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTipoServicioLimitesTiempo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "limite_dias_primera_sesion",
                table: "catalogos_tipo_servicio",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "limite_horas_sesiones_siguientes",
                table: "catalogos_tipo_servicio",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "limite_dias_primera_sesion",
                table: "catalogos_tipo_servicio");

            migrationBuilder.DropColumn(
                name: "limite_horas_sesiones_siguientes",
                table: "catalogos_tipo_servicio");
        }
    }
}
