using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaquetePrecioYServicios : Migration
    {
        // Nota: el ModelSnapshot regenerado por EF incluye cambios anteriores del
        // stack de Turnos (turno_programacion_sucursales, cols en asignacion_turnos
        // y asignacion_turno_sesiones) que se aplicaron a mano en dev+prod durante
        // la sesion previa sin generar migracion EF. Esta migracion solo persiste
        // los cambios NUEVOS de Paquetes; el snapshot queda actualizado con todo
        // lo real de la BD para futuras migraciones. Si en el futuro se levanta un
        // ambiente desde cero habra que reproducir los cambios previos con SQL
        // manual antes de correr esta migracion.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "precio",
                table: "paquetes",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "paquete_servicios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    paquete_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    catalogo_servicio_referencia_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paquete_servicios", x => x.id);
                    table.ForeignKey(
                        name: "fk_paquete_servicios_paquetes_paquete_id",
                        column: x => x.paquete_id,
                        principalTable: "paquetes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_paquete_servicios_catalogos_servicio_referencia_catalogo_se",
                        column: x => x.catalogo_servicio_referencia_id,
                        principalTable: "catalogos_servicio_referencia",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_paquete_servicios_paquete_id",
                table: "paquete_servicios",
                column: "paquete_id");

            migrationBuilder.CreateIndex(
                name: "ix_paquete_servicios_catalogo_servicio_referencia_id",
                table: "paquete_servicios",
                column: "catalogo_servicio_referencia_id");

            migrationBuilder.CreateIndex(
                name: "ix_paquete_servicios_tenant_id_paquete_id_codigo",
                table: "paquete_servicios",
                columns: new[] { "tenant_id", "paquete_id", "codigo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "paquete_servicios");
            migrationBuilder.DropColumn(name: "precio", table: "paquetes");
        }
    }
}
