using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPivoteSesionRestrict : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_asignacion_turno_sesion_hcs_asignacion_turno_sesiones_sesio",
                table: "asignacion_turno_sesion_hcs");

            migrationBuilder.AddForeignKey(
                name: "fk_asignacion_turno_sesion_hcs_asignacion_turno_sesiones_sesio",
                table: "asignacion_turno_sesion_hcs",
                column: "sesion_id",
                principalTable: "asignacion_turno_sesiones",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_asignacion_turno_sesion_hcs_asignacion_turno_sesiones_sesio",
                table: "asignacion_turno_sesion_hcs");

            migrationBuilder.AddForeignKey(
                name: "fk_asignacion_turno_sesion_hcs_asignacion_turno_sesiones_sesio",
                table: "asignacion_turno_sesion_hcs",
                column: "sesion_id",
                principalTable: "asignacion_turno_sesiones",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
