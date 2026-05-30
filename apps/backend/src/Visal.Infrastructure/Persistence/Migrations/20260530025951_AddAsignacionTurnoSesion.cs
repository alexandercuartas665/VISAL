using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAsignacionTurnoSesion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asignacion_turno_sesiones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asignacion_turno_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_no = table.Column<int>(type: "integer", nullable: false),
                    fecha_atencion = table.Column<DateOnly>(type: "date", nullable: false),
                    nota_texto = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asignacion_turno_sesiones", x => x.id);
                    table.ForeignKey(
                        name: "fk_asignacion_turno_sesiones_asignacion_turnos_asignacion_turn",
                        column: x => x.asignacion_turno_id,
                        principalTable: "asignacion_turnos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_turno_sesiones_asignacion_turno_id",
                table: "asignacion_turno_sesiones",
                column: "asignacion_turno_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asignacion_turno_sesiones");
        }
    }
}
