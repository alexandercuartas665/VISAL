using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class HistoriaClinicaIncapacidadesCertificaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "historia_clinica_certificaciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    historia_clinica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contenido = table.Column<string>(type: "text", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historia_clinica_certificaciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_historia_clinica_certificaciones_historias_clinicas_histori",
                        column: x => x.historia_clinica_id,
                        principalTable: "historias_clinicas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "historia_clinica_incapacidades",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    historia_clinica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    motivo = table.Column<string>(type: "text", nullable: false),
                    fecha_desde = table.Column<DateOnly>(type: "date", nullable: true),
                    fecha_hasta = table.Column<DateOnly>(type: "date", nullable: true),
                    dias = table.Column<int>(type: "integer", nullable: true),
                    tipo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historia_clinica_incapacidades", x => x.id);
                    table.ForeignKey(
                        name: "fk_historia_clinica_incapacidades_historias_clinicas_historia_",
                        column: x => x.historia_clinica_id,
                        principalTable: "historias_clinicas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_certificaciones_historia_clinica_id",
                table: "historia_clinica_certificaciones",
                column: "historia_clinica_id");

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_certificaciones_tenant_id_historia_clinica",
                table: "historia_clinica_certificaciones",
                columns: new[] { "tenant_id", "historia_clinica_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_incapacidades_historia_clinica_id",
                table: "historia_clinica_incapacidades",
                column: "historia_clinica_id");

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_incapacidades_tenant_id_historia_clinica_i",
                table: "historia_clinica_incapacidades",
                columns: new[] { "tenant_id", "historia_clinica_id", "orden" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "historia_clinica_certificaciones");

            migrationBuilder.DropTable(
                name: "historia_clinica_incapacidades");
        }
    }
}
