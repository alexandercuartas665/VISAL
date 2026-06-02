using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotaMedica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "notas_medicas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    historia_clinica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asignacion_turno_id = table.Column<Guid>(type: "uuid", nullable: true),
                    session_no = table.Column<int>(type: "integer", nullable: true),
                    fecha_nota = table.Column<DateOnly>(type: "date", nullable: false),
                    hora_nota = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    codigo_unico = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    contenido = table.Column<string>(type: "text", nullable: false),
                    especialista_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    criticidad = table.Column<int>(type: "integer", nullable: false),
                    firma_data_url = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notas_medicas", x => x.id);
                    table.ForeignKey(
                        name: "fk_notas_medicas_historias_clinicas_historia_clinica_id",
                        column: x => x.historia_clinica_id,
                        principalTable: "historias_clinicas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_notas_medicas_pacientes_paciente_id",
                        column: x => x.paciente_id,
                        principalTable: "pacientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "nota_medica_documentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nota_medica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre_original = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ruta_archivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    tipo_mime = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    tamano = table.Column<long>(type: "bigint", nullable: false),
                    categoria = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    tipo_terapia = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    mes = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    anotaciones = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_nota_medica_documentos", x => x.id);
                    table.ForeignKey(
                        name: "fk_nota_medica_documentos_notas_medicas_nota_medica_id",
                        column: x => x.nota_medica_id,
                        principalTable: "notas_medicas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_nota_medica_documentos_nota_medica_id",
                table: "nota_medica_documentos",
                column: "nota_medica_id");

            migrationBuilder.CreateIndex(
                name: "ix_nota_medica_documentos_tenant_id_nota_medica_id",
                table: "nota_medica_documentos",
                columns: new[] { "tenant_id", "nota_medica_id" });

            migrationBuilder.CreateIndex(
                name: "ix_notas_medicas_historia_clinica_id",
                table: "notas_medicas",
                column: "historia_clinica_id");

            migrationBuilder.CreateIndex(
                name: "ix_notas_medicas_paciente_id",
                table: "notas_medicas",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "ix_notas_medicas_tenant_id_criticidad",
                table: "notas_medicas",
                columns: new[] { "tenant_id", "criticidad" });

            migrationBuilder.CreateIndex(
                name: "ix_notas_medicas_tenant_id_estado",
                table: "notas_medicas",
                columns: new[] { "tenant_id", "estado" });

            migrationBuilder.CreateIndex(
                name: "ix_notas_medicas_tenant_id_historia_clinica_id_fecha_nota",
                table: "notas_medicas",
                columns: new[] { "tenant_id", "historia_clinica_id", "fecha_nota" });

            migrationBuilder.CreateIndex(
                name: "ix_notas_medicas_tenant_id_paciente_id_fecha_nota",
                table: "notas_medicas",
                columns: new[] { "tenant_id", "paciente_id", "fecha_nota" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "nota_medica_documentos");

            migrationBuilder.DropTable(
                name: "notas_medicas");
        }
    }
}
