using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCierrePeriodos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cierre_periodo_id",
                table: "asignacion_turnos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cierre_periodos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    mes = table.Column<int>(type: "integer", nullable: false),
                    cerrado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    total_servicios = table.Column<int>(type: "integer", nullable: false),
                    total_sesiones = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cierre_periodos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cierre_periodo_detalles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cierre_periodo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_nombre = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    paciente_documento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    servicio_nombre = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    codigo_servicio = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    cantidad_pendiente = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cierre_periodo_detalles", x => x.id);
                    table.ForeignKey(
                        name: "fk_cierre_periodo_detalles_cierre_periodos_cierre_periodo_id",
                        column: x => x.cierre_periodo_id,
                        principalTable: "cierre_periodos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_turnos_cierre_periodo_id",
                table: "asignacion_turnos",
                column: "cierre_periodo_id");

            migrationBuilder.CreateIndex(
                name: "ix_cierre_periodo_detalles_cierre_periodo_id",
                table: "cierre_periodo_detalles",
                column: "cierre_periodo_id");

            migrationBuilder.CreateIndex(
                name: "ix_cierre_periodos_tenant_id_anio_mes",
                table: "cierre_periodos",
                columns: new[] { "tenant_id", "anio", "mes" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cierre_periodo_detalles");

            migrationBuilder.DropTable(
                name: "cierre_periodos");

            migrationBuilder.DropIndex(
                name: "ix_asignacion_turnos_cierre_periodo_id",
                table: "asignacion_turnos");

            migrationBuilder.DropColumn(
                name: "cierre_periodo_id",
                table: "asignacion_turnos");
        }
    }
}
