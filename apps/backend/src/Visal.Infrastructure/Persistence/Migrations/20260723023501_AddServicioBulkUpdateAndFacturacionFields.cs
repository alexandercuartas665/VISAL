using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServicioBulkUpdateAndFacturacionFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "grupo_servicio_facturacion",
                table: "servicios_contrato",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "modalidad_facturacion",
                table: "servicios_contrato",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "servicio_facturacion",
                table: "servicios_contrato",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "servicio_bulk_updates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operador_busqueda = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    texto_busqueda = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    nueva_modalidad_facturacion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    nuevo_grupo_servicio_facturacion = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    nuevo_servicio_facturacion = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    motivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    total_afectados = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    fecha_reversion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revertido_por = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_servicio_bulk_updates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "servicio_bulk_update_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    bulk_update_id = table.Column<Guid>(type: "uuid", nullable: false),
                    servicio_contrato_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modalidad_facturacion_antes = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    grupo_servicio_facturacion_antes = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    servicio_facturacion_antes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_servicio_bulk_update_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_servicio_bulk_update_items_servicio_bulk_updates_bulk_updat",
                        column: x => x.bulk_update_id,
                        principalTable: "servicio_bulk_updates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_servicio_bulk_update_items_bulk_update_id",
                table: "servicio_bulk_update_items",
                column: "bulk_update_id");

            migrationBuilder.CreateIndex(
                name: "ix_servicio_bulk_update_items_tenant_id_bulk_update_id",
                table: "servicio_bulk_update_items",
                columns: new[] { "tenant_id", "bulk_update_id" });

            migrationBuilder.CreateIndex(
                name: "ix_servicio_bulk_update_items_tenant_id_servicio_contrato_id",
                table: "servicio_bulk_update_items",
                columns: new[] { "tenant_id", "servicio_contrato_id" });

            migrationBuilder.CreateIndex(
                name: "ix_servicio_bulk_updates_tenant_id_created_at",
                table: "servicio_bulk_updates",
                columns: new[] { "tenant_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "servicio_bulk_update_items");

            migrationBuilder.DropTable(
                name: "servicio_bulk_updates");

            migrationBuilder.DropColumn(
                name: "grupo_servicio_facturacion",
                table: "servicios_contrato");

            migrationBuilder.DropColumn(
                name: "modalidad_facturacion",
                table: "servicios_contrato");

            migrationBuilder.DropColumn(
                name: "servicio_facturacion",
                table: "servicios_contrato");
        }
    }
}
