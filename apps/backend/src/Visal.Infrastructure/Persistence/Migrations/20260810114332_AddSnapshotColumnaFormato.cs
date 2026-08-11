using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSnapshotColumnaFormato : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "formato_patron",
                table: "facturacion_snapshot_columna_configs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "formato_tipo",
                table: "facturacion_snapshot_columna_configs",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "formato_patron",
                table: "facturacion_snapshot_columna_configs");

            migrationBuilder.DropColumn(
                name: "formato_tipo",
                table: "facturacion_snapshot_columna_configs");
        }
    }
}
