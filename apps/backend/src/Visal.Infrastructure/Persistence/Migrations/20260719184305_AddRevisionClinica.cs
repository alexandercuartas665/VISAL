using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionClinica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "revisiones_clinica",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    historia_clinica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    estado_agregado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    estado_agente = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    solicitada_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    solicitada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    ultima_accion_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    iteracion_actual = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revisiones_clinica", x => x.id);
                    table.ForeignKey(
                        name: "fk_revisiones_clinica_historias_clinicas_historia_clinica_id",
                        column: x => x.historia_clinica_id,
                        principalTable: "historias_clinicas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "revision_clinica_eventos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_clinica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    resultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_usuario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_agente_codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    iteracion = table.Column<int>(type: "integer", nullable: false),
                    motivo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    nota = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    payload_json = table.Column<string>(type: "jsonb", nullable: true),
                    ocurrido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revision_clinica_eventos", x => x.id);
                    table.ForeignKey(
                        name: "fk_revision_clinica_eventos_revisiones_clinica_revision_clinic",
                        column: x => x.revision_clinica_id,
                        principalTable: "revisiones_clinica",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_revision_clinica_eventos_revision_clinica_id_ocurrido_en",
                table: "revision_clinica_eventos",
                columns: new[] { "revision_clinica_id", "ocurrido_en" });

            migrationBuilder.CreateIndex(
                name: "ix_revisiones_clinica_historia_clinica_id",
                table: "revisiones_clinica",
                column: "historia_clinica_id");

            migrationBuilder.CreateIndex(
                name: "ix_revisiones_clinica_tenant_id_estado_agregado_ultima_accion_",
                table: "revisiones_clinica",
                columns: new[] { "tenant_id", "estado_agregado", "ultima_accion_en" });

            migrationBuilder.CreateIndex(
                name: "ix_revisiones_clinica_tenant_id_historia_clinica_id",
                table: "revisiones_clinica",
                columns: new[] { "tenant_id", "historia_clinica_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "revision_clinica_eventos");

            migrationBuilder.DropTable(
                name: "revisiones_clinica");
        }
    }
}
