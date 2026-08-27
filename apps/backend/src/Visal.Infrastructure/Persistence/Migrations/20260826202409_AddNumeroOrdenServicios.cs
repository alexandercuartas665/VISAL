using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNumeroOrdenServicios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_historia_clinica_ordenes_servicio_tenant_id_historia_clinic",
                table: "historia_clinica_ordenes_servicio");

            migrationBuilder.AddColumn<int>(
                name: "numero_orden",
                table: "historia_clinica_ordenes_servicio",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_ordenes_servicio_tenant_id_historia_clinic",
                table: "historia_clinica_ordenes_servicio",
                columns: new[] { "tenant_id", "historia_clinica_id", "numero_orden", "orden" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_historia_clinica_ordenes_servicio_tenant_id_historia_clinic",
                table: "historia_clinica_ordenes_servicio");

            migrationBuilder.DropColumn(
                name: "numero_orden",
                table: "historia_clinica_ordenes_servicio");

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_ordenes_servicio_tenant_id_historia_clinic",
                table: "historia_clinica_ordenes_servicio",
                columns: new[] { "tenant_id", "historia_clinica_id", "orden" });
        }
    }
}
