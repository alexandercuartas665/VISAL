using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAseguradoraIdToFacturacionSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "aseguradora_id",
                table: "facturacion_snapshots",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_facturacion_snapshots_aseguradora_id",
                table: "facturacion_snapshots",
                column: "aseguradora_id");

            migrationBuilder.CreateIndex(
                name: "ix_facturacion_snapshots_tenant_id_aseguradora_id_estado",
                table: "facturacion_snapshots",
                columns: new[] { "tenant_id", "aseguradora_id", "estado" });

            migrationBuilder.AddForeignKey(
                name: "fk_facturacion_snapshots_aseguradoras_aseguradora_id",
                table: "facturacion_snapshots",
                column: "aseguradora_id",
                principalTable: "aseguradoras",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_facturacion_snapshots_aseguradoras_aseguradora_id",
                table: "facturacion_snapshots");

            migrationBuilder.DropIndex(
                name: "ix_facturacion_snapshots_aseguradora_id",
                table: "facturacion_snapshots");

            migrationBuilder.DropIndex(
                name: "ix_facturacion_snapshots_tenant_id_aseguradora_id_estado",
                table: "facturacion_snapshots");

            migrationBuilder.DropColumn(
                name: "aseguradora_id",
                table: "facturacion_snapshots");
        }
    }
}
