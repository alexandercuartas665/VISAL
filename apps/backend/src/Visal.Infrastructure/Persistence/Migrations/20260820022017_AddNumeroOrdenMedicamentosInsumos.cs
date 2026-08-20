using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNumeroOrdenMedicamentosInsumos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ordenes_medicamentos_publicas_tenant_id_historia_clinica_id",
                table: "ordenes_medicamentos_publicas");

            migrationBuilder.DropIndex(
                name: "ix_historia_clinica_medicamentos_tenant_id_historia_clinica_id",
                table: "historia_clinica_medicamentos");

            migrationBuilder.DropIndex(
                name: "ix_historia_clinica_insumos_tenant_id_historia_clinica_id_orden",
                table: "historia_clinica_insumos");

            migrationBuilder.AddColumn<int>(
                name: "numero_orden",
                table: "ordenes_medicamentos_publicas",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "numero_orden",
                table: "historia_clinica_medicamentos",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "numero_orden",
                table: "historia_clinica_insumos",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "ix_ordenes_medicamentos_publicas_tenant_id_historia_clinica_id",
                table: "ordenes_medicamentos_publicas",
                columns: new[] { "tenant_id", "historia_clinica_id", "tipo_orden", "numero_orden" });

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_medicamentos_tenant_id_historia_clinica_id",
                table: "historia_clinica_medicamentos",
                columns: new[] { "tenant_id", "historia_clinica_id", "numero_orden", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_insumos_tenant_id_historia_clinica_id_nume",
                table: "historia_clinica_insumos",
                columns: new[] { "tenant_id", "historia_clinica_id", "numero_orden", "orden" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ordenes_medicamentos_publicas_tenant_id_historia_clinica_id",
                table: "ordenes_medicamentos_publicas");

            migrationBuilder.DropIndex(
                name: "ix_historia_clinica_medicamentos_tenant_id_historia_clinica_id",
                table: "historia_clinica_medicamentos");

            migrationBuilder.DropIndex(
                name: "ix_historia_clinica_insumos_tenant_id_historia_clinica_id_nume",
                table: "historia_clinica_insumos");

            migrationBuilder.DropColumn(
                name: "numero_orden",
                table: "ordenes_medicamentos_publicas");

            migrationBuilder.DropColumn(
                name: "numero_orden",
                table: "historia_clinica_medicamentos");

            migrationBuilder.DropColumn(
                name: "numero_orden",
                table: "historia_clinica_insumos");

            migrationBuilder.CreateIndex(
                name: "ix_ordenes_medicamentos_publicas_tenant_id_historia_clinica_id",
                table: "ordenes_medicamentos_publicas",
                columns: new[] { "tenant_id", "historia_clinica_id", "tipo_orden" });

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_medicamentos_tenant_id_historia_clinica_id",
                table: "historia_clinica_medicamentos",
                columns: new[] { "tenant_id", "historia_clinica_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_insumos_tenant_id_historia_clinica_id_orden",
                table: "historia_clinica_insumos",
                columns: new[] { "tenant_id", "historia_clinica_id", "orden" });
        }
    }
}
