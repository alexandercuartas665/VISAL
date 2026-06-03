using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HistoriaClinicaEscala : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "historia_clinica_escalas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    historia_clinica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    valores_json = table.Column<string>(type: "jsonb", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    fecha_apertura = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_cierre = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    especialista_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historia_clinica_escalas", x => x.id);
                    table.ForeignKey(
                        name: "fk_historia_clinica_escalas_form_definitions_form_definition_id",
                        column: x => x.form_definition_id,
                        principalTable: "form_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_historia_clinica_escalas_historias_clinicas_historia_clinic",
                        column: x => x.historia_clinica_id,
                        principalTable: "historias_clinicas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_escalas_form_definition_id",
                table: "historia_clinica_escalas",
                column: "form_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_escalas_historia_clinica_id",
                table: "historia_clinica_escalas",
                column: "historia_clinica_id");

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_escalas_tenant_id_historia_clinica_id_fech",
                table: "historia_clinica_escalas",
                columns: new[] { "tenant_id", "historia_clinica_id", "fecha_apertura" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "historia_clinica_escalas");
        }
    }
}
