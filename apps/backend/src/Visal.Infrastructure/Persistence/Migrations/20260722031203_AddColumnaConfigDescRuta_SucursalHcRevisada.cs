using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddColumnaConfigDescRuta_SucursalHcRevisada : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "exigir_hc_revisada_para_facturar",
                table: "sucursales",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "descripcion",
                table: "facturacion_snapshot_columna_configs",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ruta_origen",
                table: "facturacion_snapshot_columna_configs",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "exigir_hc_revisada_para_facturar",
                table: "sucursales");

            migrationBuilder.DropColumn(
                name: "descripcion",
                table: "facturacion_snapshot_columna_configs");

            migrationBuilder.DropColumn(
                name: "ruta_origen",
                table: "facturacion_snapshot_columna_configs");
        }
    }
}
