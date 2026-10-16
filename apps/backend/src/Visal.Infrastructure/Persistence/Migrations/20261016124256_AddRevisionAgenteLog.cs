using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionAgenteLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "revision_agente_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision_clinica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    historia_clinica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agente_codigo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    proveedor = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    modelo = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    ok = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    resultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    confianza = table.Column<decimal>(type: "numeric(4,3)", nullable: true),
                    tokens_in = table.Column<int>(type: "integer", nullable: false),
                    tokens_out = table.Column<int>(type: "integer", nullable: false),
                    system_prompt = table.Column<string>(type: "text", nullable: true),
                    contexto_enviado = table.Column<string>(type: "text", nullable: true),
                    campos_legibles_json = table.Column<string>(type: "text", nullable: true),
                    tools_json = table.Column<string>(type: "text", nullable: true),
                    respuesta_cruda = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_revision_agente_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_revision_agente_logs_historia_clinica_id",
                table: "revision_agente_logs",
                column: "historia_clinica_id");

            migrationBuilder.CreateIndex(
                name: "ix_revision_agente_logs_tenant_id_revision_clinica_id",
                table: "revision_agente_logs",
                columns: new[] { "tenant_id", "revision_clinica_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "revision_agente_logs");
        }
    }
}
