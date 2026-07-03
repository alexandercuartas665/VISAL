using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRipsHcYCatalogos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "rips_causa_externa_codigo",
                table: "historias_clinicas",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rips_causa_externa_nombre",
                table: "historias_clinicas",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rips_finalidad_codigo",
                table: "historias_clinicas",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rips_finalidad_nombre",
                table: "historias_clinicas",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rips_via_ingreso_codigo",
                table: "historias_clinicas",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rips_via_ingreso_nombre",
                table: "historias_clinicas",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rips_causa_externa_codigo",
                table: "historias_clinicas");

            migrationBuilder.DropColumn(
                name: "rips_causa_externa_nombre",
                table: "historias_clinicas");

            migrationBuilder.DropColumn(
                name: "rips_finalidad_codigo",
                table: "historias_clinicas");

            migrationBuilder.DropColumn(
                name: "rips_finalidad_nombre",
                table: "historias_clinicas");

            migrationBuilder.DropColumn(
                name: "rips_via_ingreso_codigo",
                table: "historias_clinicas");

            migrationBuilder.DropColumn(
                name: "rips_via_ingreso_nombre",
                table: "historias_clinicas");
        }
    }
}
