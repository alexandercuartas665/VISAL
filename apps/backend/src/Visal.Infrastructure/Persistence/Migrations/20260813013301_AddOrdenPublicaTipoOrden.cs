using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrdenPublicaTipoOrden : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ordenes_medicamentos_publicas_tenant_id_historia_clinica_id",
                table: "ordenes_medicamentos_publicas");

            migrationBuilder.AddColumn<string>(
                name: "tipo_orden",
                table: "ordenes_medicamentos_publicas",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "MED");

            migrationBuilder.CreateIndex(
                name: "ix_ordenes_medicamentos_publicas_tenant_id_historia_clinica_id",
                table: "ordenes_medicamentos_publicas",
                columns: new[] { "tenant_id", "historia_clinica_id", "tipo_orden" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_ordenes_medicamentos_publicas_tenant_id_historia_clinica_id",
                table: "ordenes_medicamentos_publicas");

            migrationBuilder.DropColumn(
                name: "tipo_orden",
                table: "ordenes_medicamentos_publicas");

            migrationBuilder.CreateIndex(
                name: "ix_ordenes_medicamentos_publicas_tenant_id_historia_clinica_id",
                table: "ordenes_medicamentos_publicas",
                columns: new[] { "tenant_id", "historia_clinica_id" });
        }
    }
}
