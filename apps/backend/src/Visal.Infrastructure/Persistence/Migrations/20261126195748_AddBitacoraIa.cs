using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBitacoraIa : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "origen",
                table: "revision_agente_logs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            // IMP-72: origen de las corridas viejas. Intento sin ciclo o con calificacion (el pulgar
            // solo existe en el modal de cierre) = Cierre; las vinculadas a un ciclo abierto por el
            // cierre (audit historia-clinica.auditoria-ia-cierre / cerrar-pese-rechazo-ia con su
            // revisionId, misma ventana de tiempo) = Cierre; el resto = Revision (Ordenes / worker).
            migrationBuilder.Sql(@"
UPDATE revision_agente_logs SET origen = 'Cierre'
 WHERE origen IS NULL AND (revision_clinica_id IS NULL OR feedback_util IS NOT NULL);
UPDATE revision_agente_logs l SET origen = 'Cierre'
 WHERE l.origen IS NULL
   AND EXISTS (SELECT 1 FROM super_admin_audit_logs a
                WHERE a.action_name IN ('historia-clinica.auditoria-ia-cierre', 'historia-clinica.cerrar-pese-rechazo-ia')
                  AND a.new_value ? 'revisionId'
                  AND a.new_value->>'revisionId' = l.revision_clinica_id::text
                  AND l.created_at BETWEEN a.created_at - interval '10 minutes' AND a.created_at + interval '2 minutes');
UPDATE revision_agente_logs SET origen = 'Revision' WHERE origen IS NULL;");

            migrationBuilder.CreateTable(
                name: "bitacora_ia_api_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_key_hash = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    api_key_encrypted = table.Column<string>(type: "text", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bitacora_ia_api_configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_revision_agente_logs_tenant_id_created_at",
                table: "revision_agente_logs",
                columns: new[] { "tenant_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bitacora_ia_api_configs_api_key_hash",
                table: "bitacora_ia_api_configs",
                column: "api_key_hash");

            migrationBuilder.CreateIndex(
                name: "ix_bitacora_ia_api_configs_tenant_id",
                table: "bitacora_ia_api_configs",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bitacora_ia_api_configs");

            migrationBuilder.DropIndex(
                name: "ix_revision_agente_logs_tenant_id_created_at",
                table: "revision_agente_logs");

            migrationBuilder.DropColumn(
                name: "origen",
                table: "revision_agente_logs");
        }
    }
}
