using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddIncapacidadCamposYSedeNit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "nit",
                table: "sucursales",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "inicial_o_prorroga",
                table: "historia_clinica_incapacidades",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "retroactiva",
                table: "historia_clinica_incapacidades",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "nit",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "inicial_o_prorroga",
                table: "historia_clinica_incapacidades");

            migrationBuilder.DropColumn(
                name: "retroactiva",
                table: "historia_clinica_incapacidades");
        }
    }
}
