using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContratoSucursales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contrato_sucursales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_aseguradora_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contrato_sucursales", x => x.id);
                    table.ForeignKey(
                        name: "fk_contrato_sucursales_contratos_aseguradora_contrato_asegurad",
                        column: x => x.contrato_aseguradora_id,
                        principalTable: "contratos_aseguradora",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_contrato_sucursales_sucursales_sucursal_id",
                        column: x => x.sucursal_id,
                        principalTable: "sucursales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contrato_sucursales_contrato_aseguradora_id",
                table: "contrato_sucursales",
                column: "contrato_aseguradora_id");

            migrationBuilder.CreateIndex(
                name: "ix_contrato_sucursales_sucursal_id",
                table: "contrato_sucursales",
                column: "sucursal_id");

            migrationBuilder.CreateIndex(
                name: "ix_contrato_sucursales_tenant_id_contrato_aseguradora_id_sucur",
                table: "contrato_sucursales",
                columns: new[] { "tenant_id", "contrato_aseguradora_id", "sucursal_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contrato_sucursales");
        }
    }
}
