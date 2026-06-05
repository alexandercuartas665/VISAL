using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FirmaPacienteRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "firma_paciente_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nota_medica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    telefono = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    nombre_contacto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    solicitada_por_tenant_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    image_data_url = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_firma_paciente_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_firma_paciente_requests_tenant_id_nota_medica_id_status",
                table: "firma_paciente_requests",
                columns: new[] { "tenant_id", "nota_medica_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_firma_paciente_requests_tenant_id_paciente_id",
                table: "firma_paciente_requests",
                columns: new[] { "tenant_id", "paciente_id" });

            migrationBuilder.CreateIndex(
                name: "ix_firma_paciente_requests_token",
                table: "firma_paciente_requests",
                column: "token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "firma_paciente_requests");
        }
    }
}
