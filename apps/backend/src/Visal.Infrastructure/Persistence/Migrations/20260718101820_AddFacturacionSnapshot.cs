using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFacturacionSnapshot : Migration
    {
        // Nota: EF genero en esta migracion cambios de Paquete (precio +
        // paquete_servicios) y de Asignacion/AsignacionTurno (paquete_codigo,
        // paquete_instancia_id, paquete_valor_pactado) porque migraciones
        // anteriores (PQ1..PQ7) se marcaron a mano en la BD via SQL. La BD y
        // el snapshot de EF quedaron desincronizados en esos campos.
        // Removemos esas lineas de Up/Down para que esta migracion solo aporte
        // el motor de Facturacion. El snapshot del modelo si refleja el estado
        // real de esas tablas.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "facturacion_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    filtros_json = table.Column<string>(type: "jsonb", nullable: false),
                    estado = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    motivo_archivado = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    fecha_archivado = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    archivado_por = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_ejecucion_inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_ejecucion_fin = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duracion_ms = table.Column<int>(type: "integer", nullable: true),
                    total_filas = table.Column<int>(type: "integer", nullable: false),
                    error_mensaje = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facturacion_snapshots", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "facturacion_snapshot_filas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_fila = table.Column<int>(type: "integer", nullable: false),
                    datos_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_facturacion_snapshot_filas", x => x.id);
                    table.ForeignKey(
                        name: "fk_facturacion_snapshot_filas_facturacion_snapshots_snapshot_id",
                        column: x => x.snapshot_id,
                        principalTable: "facturacion_snapshots",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_facturacion_snapshot_filas_snapshot_id_numero_fila",
                table: "facturacion_snapshot_filas",
                columns: new[] { "snapshot_id", "numero_fila" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_facturacion_snapshots_tenant_id_estado_created_at",
                table: "facturacion_snapshots",
                columns: new[] { "tenant_id", "estado", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_facturacion_snapshots_tenant_id_tipo_estado",
                table: "facturacion_snapshots",
                columns: new[] { "tenant_id", "tipo", "estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "facturacion_snapshot_filas");

            migrationBuilder.DropTable(
                name: "facturacion_snapshots");
        }
    }
}
