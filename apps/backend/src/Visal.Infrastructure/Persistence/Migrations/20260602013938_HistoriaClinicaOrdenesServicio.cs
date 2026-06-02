using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HistoriaClinicaOrdenesServicio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "historia_clinica_ordenes_servicio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    historia_clinica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    servicio_contrato_id = table.Column<Guid>(type: "uuid", nullable: true),
                    codigo_servicio = table.Column<string>(type: "text", nullable: true),
                    descripcion = table.Column<string>(type: "text", nullable: false),
                    cantidad = table.Column<string>(type: "text", nullable: true),
                    observaciones = table.Column<string>(type: "text", nullable: true),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historia_clinica_ordenes_servicio", x => x.id);
                    table.ForeignKey(
                        name: "fk_historia_clinica_ordenes_servicio_historias_clinicas_histor",
                        column: x => x.historia_clinica_id,
                        principalTable: "historias_clinicas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_historia_clinica_ordenes_servicio_servicios_contrato_servic",
                        column: x => x.servicio_contrato_id,
                        principalTable: "servicios_contrato",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_ordenes_servicio_historia_clinica_id",
                table: "historia_clinica_ordenes_servicio",
                column: "historia_clinica_id");

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_ordenes_servicio_servicio_contrato_id",
                table: "historia_clinica_ordenes_servicio",
                column: "servicio_contrato_id");

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_ordenes_servicio_tenant_id_historia_clinic",
                table: "historia_clinica_ordenes_servicio",
                columns: new[] { "tenant_id", "historia_clinica_id", "orden" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "historia_clinica_ordenes_servicio");
        }
    }
}
