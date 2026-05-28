using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAsignacionLotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asignacion_lotes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    contrato_codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asignacion_lotes", x => x.id);
                    table.ForeignKey(
                        name: "fk_asignacion_lotes_pacientes_paciente_id",
                        column: x => x.paciente_id,
                        principalTable: "pacientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "asignaciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lote_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    servicio_id = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    nombre_servicio = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tipo_servicio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    modulo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    contrato_codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    codigo_autorizacion = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    anio_servicio = table.Column<short>(type: "smallint", nullable: true),
                    mes_vigencia = table.Column<short>(type: "smallint", nullable: false),
                    mes_final = table.Column<short>(type: "smallint", nullable: true),
                    fecha_inicio = table.Column<DateOnly>(type: "date", nullable: false),
                    fecha_final = table.Column<DateOnly>(type: "date", nullable: true),
                    observaciones = table.Column<string>(type: "text", nullable: true),
                    formato_historia = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    estado = table.Column<int>(type: "integer", maxLength: 30, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asignaciones", x => x.id);
                    table.CheckConstraint("ck_asignaciones_cantidad", "cantidad > 0");
                    table.ForeignKey(
                        name: "fk_asignaciones_asignacion_lotes_lote_id",
                        column: x => x.lote_id,
                        principalTable: "asignacion_lotes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asignaciones_pacientes_paciente_id",
                        column: x => x.paciente_id,
                        principalTable: "pacientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_lotes_paciente_id",
                table: "asignacion_lotes",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_lotes_tenant_id_paciente_id",
                table: "asignacion_lotes",
                columns: new[] { "tenant_id", "paciente_id" });

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_lote_id",
                table: "asignaciones",
                column: "lote_id");

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_paciente_id",
                table: "asignaciones",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_tenant_id_estado_mes_vigencia_anio_servicio",
                table: "asignaciones",
                columns: new[] { "tenant_id", "estado", "mes_vigencia", "anio_servicio" });

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_tenant_id_paciente_id",
                table: "asignaciones",
                columns: new[] { "tenant_id", "paciente_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asignaciones");

            migrationBuilder.DropTable(
                name: "asignacion_lotes");
        }
    }
}
