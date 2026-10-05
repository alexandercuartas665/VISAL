using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHcMarcaErrorOrigen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "historia_clinica_id",
                table: "hc_marcas_error",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "origen",
                table: "hc_marcas_error",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_hc_marcas_error_tenant_id_historia_clinica_id",
                table: "hc_marcas_error",
                columns: new[] { "tenant_id", "historia_clinica_id" });

            migrationBuilder.CreateIndex(
                name: "ix_hc_marcas_error_tenant_id_origen_estado",
                table: "hc_marcas_error",
                columns: new[] { "tenant_id", "origen", "estado" });

            // Backfill: las marcas creadas por la reparacion de datos B1 (v0.75.0)
            // se insertaron antes de existir la columna 'origen'. Se identifican por el
            // nombre del marcador y se reclasifican como AutoReparacion (1) para que NO
            // inflen el badge (que cuenta solo Pendientes Manuales) y salgan con el
            // filtro de origen "Auto".
            migrationBuilder.Sql(
                "UPDATE hc_marcas_error SET origen = 1 " +
                "WHERE marcado_por_nombre = 'Sistema (ajuste automatico)';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_hc_marcas_error_tenant_id_historia_clinica_id",
                table: "hc_marcas_error");

            migrationBuilder.DropIndex(
                name: "ix_hc_marcas_error_tenant_id_origen_estado",
                table: "hc_marcas_error");

            migrationBuilder.DropColumn(
                name: "historia_clinica_id",
                table: "hc_marcas_error");

            migrationBuilder.DropColumn(
                name: "origen",
                table: "hc_marcas_error");
        }
    }
}
