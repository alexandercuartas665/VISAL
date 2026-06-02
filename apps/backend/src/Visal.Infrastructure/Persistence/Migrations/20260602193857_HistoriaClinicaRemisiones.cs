using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HistoriaClinicaRemisiones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "historia_clinica_remisiones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    historia_clinica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    capitulo = table.Column<string>(type: "text", nullable: false),
                    especialidad_codigo = table.Column<string>(type: "text", nullable: true),
                    especialidad_nombre = table.Column<string>(type: "text", nullable: false),
                    motivo = table.Column<string>(type: "text", nullable: true),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historia_clinica_remisiones", x => x.id);
                    table.ForeignKey(
                        name: "fk_historia_clinica_remisiones_historias_clinicas_historia_cli",
                        column: x => x.historia_clinica_id,
                        principalTable: "historias_clinicas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_remisiones_historia_clinica_id",
                table: "historia_clinica_remisiones",
                column: "historia_clinica_id");

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_remisiones_tenant_id_historia_clinica_id_o",
                table: "historia_clinica_remisiones",
                columns: new[] { "tenant_id", "historia_clinica_id", "orden" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "historia_clinica_remisiones");
        }
    }
}
