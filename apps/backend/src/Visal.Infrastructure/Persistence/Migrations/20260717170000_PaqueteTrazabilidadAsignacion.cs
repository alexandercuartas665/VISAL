using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PaqueteTrazabilidadAsignacion : Migration
    {
        // Aplica las 3 columnas de trazabilidad de paquete a asignaciones + asignacion_turnos.
        // Aplicado ya en dev (2026-07-17) y prod (fecha por confirmar) via SQL directo — esta
        // migracion queda como registro EF para trazabilidad y para ambientes nuevos.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<System.Guid>(name: "paquete_instancia_id", table: "asignaciones", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<string>(name: "paquete_codigo", table: "asignaciones", type: "character varying(40)", maxLength: 40, nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "paquete_valor_pactado", table: "asignaciones", type: "numeric(14,2)", precision: 14, scale: 2, nullable: true);
            migrationBuilder.CreateIndex(name: "ix_asignaciones_paquete_instancia_id", table: "asignaciones", column: "paquete_instancia_id");

            migrationBuilder.AddColumn<System.Guid>(name: "paquete_instancia_id", table: "asignacion_turnos", type: "uuid", nullable: true);
            migrationBuilder.AddColumn<string>(name: "paquete_codigo", table: "asignacion_turnos", type: "character varying(40)", maxLength: 40, nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "paquete_valor_pactado", table: "asignacion_turnos", type: "numeric(14,2)", precision: 14, scale: 2, nullable: true);
            migrationBuilder.CreateIndex(name: "ix_asignacion_turnos_paquete_instancia_id", table: "asignacion_turnos", column: "paquete_instancia_id");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "ix_asignacion_turnos_paquete_instancia_id", table: "asignacion_turnos");
            migrationBuilder.DropColumn(name: "paquete_valor_pactado", table: "asignacion_turnos");
            migrationBuilder.DropColumn(name: "paquete_codigo", table: "asignacion_turnos");
            migrationBuilder.DropColumn(name: "paquete_instancia_id", table: "asignacion_turnos");

            migrationBuilder.DropIndex(name: "ix_asignaciones_paquete_instancia_id", table: "asignaciones");
            migrationBuilder.DropColumn(name: "paquete_valor_pactado", table: "asignaciones");
            migrationBuilder.DropColumn(name: "paquete_codigo", table: "asignaciones");
            migrationBuilder.DropColumn(name: "paquete_instancia_id", table: "asignaciones");
        }
    }
}
