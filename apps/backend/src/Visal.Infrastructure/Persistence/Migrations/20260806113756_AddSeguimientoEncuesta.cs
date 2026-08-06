using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSeguimientoEncuesta : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "seguimiento_encuestas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    mes = table.Column<int>(type: "integer", nullable: false),
                    estado = table.Column<string>(type: "text", nullable: false),
                    fecha_llamada = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    responsable_llamada_id = table.Column<Guid>(type: "uuid", nullable: true),
                    responsable_llamada_nombre = table.Column<string>(type: "text", nullable: true),
                    pregunta1 = table.Column<int>(type: "integer", nullable: true),
                    pregunta2 = table.Column<int>(type: "integer", nullable: true),
                    pregunta3 = table.Column<int>(type: "integer", nullable: true),
                    pregunta4 = table.Column<int>(type: "integer", nullable: true),
                    pregunta5 = table.Column<int>(type: "integer", nullable: true),
                    persona_atiende = table.Column<string>(type: "text", nullable: true),
                    observaciones = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_seguimiento_encuestas", x => x.id);
                    table.ForeignKey(
                        name: "fk_seguimiento_encuestas_pacientes_paciente_id",
                        column: x => x.paciente_id,
                        principalTable: "pacientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_seguimiento_encuestas_paciente_id",
                table: "seguimiento_encuestas",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "ux_seguimiento_encuestas_tenant_paciente_mes",
                table: "seguimiento_encuestas",
                columns: new[] { "tenant_id", "paciente_id", "mes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_seguimiento_encuestas_tenant_mes_estado",
                table: "seguimiento_encuestas",
                columns: new[] { "tenant_id", "mes", "estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "seguimiento_encuestas");
        }
    }
}
