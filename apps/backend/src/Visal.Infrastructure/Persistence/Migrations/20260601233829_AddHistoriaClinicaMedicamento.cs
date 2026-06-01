using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoriaClinicaMedicamento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "historia_clinica_medicamentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    historia_clinica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    medicamento_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nombre_medicamento = table.Column<string>(type: "text", nullable: false),
                    cantidad = table.Column<string>(type: "text", nullable: true),
                    frecuencia = table.Column<string>(type: "text", nullable: true),
                    dias = table.Column<string>(type: "text", nullable: true),
                    posologia = table.Column<string>(type: "text", nullable: true),
                    observacion = table.Column<string>(type: "text", nullable: true),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historia_clinica_medicamentos", x => x.id);
                    table.ForeignKey(
                        name: "fk_historia_clinica_medicamentos_historias_clinicas_historia_c",
                        column: x => x.historia_clinica_id,
                        principalTable: "historias_clinicas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_historia_clinica_medicamentos_medicamentos_medicamento_id",
                        column: x => x.medicamento_id,
                        principalTable: "medicamentos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_medicamentos_historia_clinica_id",
                table: "historia_clinica_medicamentos",
                column: "historia_clinica_id");

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_medicamentos_medicamento_id",
                table: "historia_clinica_medicamentos",
                column: "medicamento_id");

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_medicamentos_tenant_id_historia_clinica_id",
                table: "historia_clinica_medicamentos",
                columns: new[] { "tenant_id", "historia_clinica_id", "orden" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "historia_clinica_medicamentos");
        }
    }
}
