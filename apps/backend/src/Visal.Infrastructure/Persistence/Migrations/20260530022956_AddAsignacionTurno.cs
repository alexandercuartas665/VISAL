using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAsignacionTurno : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asignacion_turnos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asignacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profesional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cantidad = table.Column<int>(type: "integer", nullable: false),
                    horas_por_turno = table.Column<decimal>(type: "numeric", nullable: true),
                    fecha_inicio = table.Column<DateOnly>(type: "date", nullable: true),
                    mes_asignar = table.Column<short>(type: "smallint", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asignacion_turnos", x => x.id);
                    table.ForeignKey(
                        name: "fk_asignacion_turnos_asignaciones_asignacion_id",
                        column: x => x.asignacion_id,
                        principalTable: "asignaciones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asignacion_turnos_profesionales_profesional_id",
                        column: x => x.profesional_id,
                        principalTable: "profesionales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_turnos_asignacion_id",
                table: "asignacion_turnos",
                column: "asignacion_id");

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_turnos_profesional_id",
                table: "asignacion_turnos",
                column: "profesional_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asignacion_turnos");
        }
    }
}
