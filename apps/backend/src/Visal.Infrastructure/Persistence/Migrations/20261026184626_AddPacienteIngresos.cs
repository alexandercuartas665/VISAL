using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPacienteIngresos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "paciente_ingresos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_comentan = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fecha_respuesta = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    sla_minutos = table.Column<int>(type: "integer", nullable: true),
                    ips_comenta = table.Column<string>(type: "text", nullable: true),
                    responsable_ingreso = table.Column<string>(type: "text", nullable: true),
                    ingreso = table.Column<bool>(type: "boolean", nullable: false),
                    motivo_no_ingreso = table.Column<string>(type: "text", nullable: true),
                    observaciones = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paciente_ingresos", x => x.id);
                    table.ForeignKey(
                        name: "fk_paciente_ingresos_pacientes_paciente_id",
                        column: x => x.paciente_id,
                        principalTable: "pacientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_paciente_ingresos_paciente_id",
                table: "paciente_ingresos",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "ix_paciente_ingresos_tenant_id_fecha_comentan",
                table: "paciente_ingresos",
                columns: new[] { "tenant_id", "fecha_comentan" });

            migrationBuilder.CreateIndex(
                name: "ix_paciente_ingresos_tenant_id_paciente_id",
                table: "paciente_ingresos",
                columns: new[] { "tenant_id", "paciente_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "paciente_ingresos");
        }
    }
}
