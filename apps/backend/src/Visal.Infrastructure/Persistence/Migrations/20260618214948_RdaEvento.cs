using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RdaEvento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "rda_eventos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    historia_clinica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    profesional_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modalidad = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    ambiente = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    bundle_json = table.Column<string>(type: "text", nullable: false),
                    bundle_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    estado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    ultimo_intento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fecha_generacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    fecha_envio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    errores_json = table.Column<string>(type: "jsonb", nullable: true),
                    referencia_minsalud = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rda_eventos", x => x.id);
                    table.ForeignKey(
                        name: "fk_rda_eventos_historias_clinicas_historia_clinica_id",
                        column: x => x.historia_clinica_id,
                        principalTable: "historias_clinicas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rda_eventos_pacientes_paciente_id",
                        column: x => x.paciente_id,
                        principalTable: "pacientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_rda_eventos_profesionales_profesional_id",
                        column: x => x.profesional_id,
                        principalTable: "profesionales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_rda_eventos_sucursales_sucursal_id",
                        column: x => x.sucursal_id,
                        principalTable: "sucursales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rda_eventos_historia_clinica_id",
                table: "rda_eventos",
                column: "historia_clinica_id");

            migrationBuilder.CreateIndex(
                name: "ix_rda_eventos_paciente_id",
                table: "rda_eventos",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "ix_rda_eventos_profesional_id",
                table: "rda_eventos",
                column: "profesional_id");

            migrationBuilder.CreateIndex(
                name: "ix_rda_eventos_sucursal_id",
                table: "rda_eventos",
                column: "sucursal_id");

            migrationBuilder.CreateIndex(
                name: "ix_rda_eventos_tenant_id_bundle_hash",
                table: "rda_eventos",
                columns: new[] { "tenant_id", "bundle_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rda_eventos_tenant_id_estado_fecha_generacion",
                table: "rda_eventos",
                columns: new[] { "tenant_id", "estado", "fecha_generacion" });

            migrationBuilder.CreateIndex(
                name: "ix_rda_eventos_tenant_id_historia_clinica_id_estado",
                table: "rda_eventos",
                columns: new[] { "tenant_id", "historia_clinica_id", "estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rda_eventos");
        }
    }
}
