using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "revision_policies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    auto_trigger_cierre = table.Column<bool>(type: "boolean", nullable: false),
                    pre_revision_ia_auto_trigger = table.Column<bool>(type: "boolean", nullable: false),
                    adopcion_automatica_agente = table.Column<bool>(type: "boolean", nullable: false),
                    umbral_confianza = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                    ventana_asignaciones_relacionadas_dias = table.Column<int>(type: "integer", nullable: false),
                    confirmar_aprobado = table.Column<bool>(type: "boolean", nullable: false),
                    motivo_inactivacion_min_chars = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revision_policies", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_revision_policies_tenant_id",
                table: "revision_policies",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "revision_policies");
        }
    }
}
