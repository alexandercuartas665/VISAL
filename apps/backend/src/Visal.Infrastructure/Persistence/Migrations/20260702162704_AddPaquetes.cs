using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaquetes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "paquete_id",
                table: "servicios_contrato",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "paquetes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    nombre = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paquetes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_servicios_contrato_paquete_id",
                table: "servicios_contrato",
                column: "paquete_id");

            migrationBuilder.CreateIndex(
                name: "ix_servicios_contrato_tenant_id_paquete_id",
                table: "servicios_contrato",
                columns: new[] { "tenant_id", "paquete_id" });

            migrationBuilder.CreateIndex(
                name: "ix_paquetes_tenant_id_codigo",
                table: "paquetes",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_servicios_contrato_paquetes_paquete_id",
                table: "servicios_contrato",
                column: "paquete_id",
                principalTable: "paquetes",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_servicios_contrato_paquetes_paquete_id",
                table: "servicios_contrato");

            migrationBuilder.DropTable(
                name: "paquetes");

            migrationBuilder.DropIndex(
                name: "ix_servicios_contrato_paquete_id",
                table: "servicios_contrato");

            migrationBuilder.DropIndex(
                name: "ix_servicios_contrato_tenant_id_paquete_id",
                table: "servicios_contrato");

            migrationBuilder.DropColumn(
                name: "paquete_id",
                table: "servicios_contrato");
        }
    }
}
