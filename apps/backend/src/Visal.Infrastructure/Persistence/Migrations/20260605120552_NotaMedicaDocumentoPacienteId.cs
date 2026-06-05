using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotaMedicaDocumentoPacienteId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotente: agregar columna solo si no existe (re-deploy seguro).
            migrationBuilder.Sql(@"ALTER TABLE nota_medica_documentos
                ADD COLUMN IF NOT EXISTS paciente_id uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';");

            // Backfill: poblar paciente_id en filas existentes desde la nota relacionada.
            // Hace falta porque la columna NOT NULL con default '0' deja datos historicos
            // sin asociar al paciente, lo que rompe el tab Documentos de Admision.
            migrationBuilder.Sql(@"UPDATE nota_medica_documentos d
                SET paciente_id = n.paciente_id
                FROM notas_medicas n
                WHERE d.nota_medica_id = n.id
                  AND d.paciente_id = '00000000-0000-0000-0000-000000000000';");

            migrationBuilder.Sql(@"CREATE INDEX IF NOT EXISTS ix_nota_medica_documentos_tenant_id_paciente_id
                ON nota_medica_documentos (tenant_id, paciente_id);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_nota_medica_documentos_tenant_id_paciente_id",
                table: "nota_medica_documentos");

            migrationBuilder.DropColumn(
                name: "paciente_id",
                table: "nota_medica_documentos");
        }
    }
}
