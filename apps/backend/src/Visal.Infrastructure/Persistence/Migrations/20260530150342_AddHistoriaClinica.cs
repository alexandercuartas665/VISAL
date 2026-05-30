using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoriaClinica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "historias_clinicas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profesional_id = table.Column<Guid>(type: "uuid", nullable: true),
                    valores_json = table.Column<string>(type: "jsonb", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    fecha_apertura = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_cierre = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    motivo_inactivacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    especialista_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historias_clinicas", x => x.id);
                    table.ForeignKey(
                        name: "fk_historias_clinicas_form_definitions_form_definition_id",
                        column: x => x.form_definition_id,
                        principalTable: "form_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_historias_clinicas_pacientes_paciente_id",
                        column: x => x.paciente_id,
                        principalTable: "pacientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_historias_clinicas_profesionales_profesional_id",
                        column: x => x.profesional_id,
                        principalTable: "profesionales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_historias_clinicas_form_definition_id",
                table: "historias_clinicas",
                column: "form_definition_id");

            migrationBuilder.CreateIndex(
                name: "ix_historias_clinicas_paciente_id",
                table: "historias_clinicas",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "ix_historias_clinicas_profesional_id",
                table: "historias_clinicas",
                column: "profesional_id");

            migrationBuilder.CreateIndex(
                name: "ix_historias_clinicas_tenant_id_paciente_id_fecha_apertura",
                table: "historias_clinicas",
                columns: new[] { "tenant_id", "paciente_id", "fecha_apertura" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "historias_clinicas");
        }
    }
}
