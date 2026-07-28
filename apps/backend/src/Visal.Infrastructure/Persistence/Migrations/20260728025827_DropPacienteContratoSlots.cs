using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DropPacienteContratoSlots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Safety net: pacientes creados entre PC-1 y PC-4 con solo dual-write
            // podrian tener slots poblados pero faltar filas en paciente_contratos
            // (por ej. Save con dto.Contratos=null). Backfilleamos de nuevo antes
            // de dropear, idempotente por unique (tenant, paciente, contrato).
            migrationBuilder.Sql(@"
INSERT INTO paciente_contratos(id, paciente_id, contrato_aseguradora_id, orden, created_at, tenant_id)
SELECT gen_random_uuid(), p.id, p.contrato1id, 1, now(), p.tenant_id
FROM pacientes p
JOIN contratos_aseguradora c ON c.id = p.contrato1id AND c.tenant_id = p.tenant_id
WHERE p.contrato1id IS NOT NULL
ON CONFLICT (tenant_id, paciente_id, contrato_aseguradora_id) DO NOTHING;

INSERT INTO paciente_contratos(id, paciente_id, contrato_aseguradora_id, orden, created_at, tenant_id)
SELECT gen_random_uuid(), p.id, p.contrato2id, 2, now(), p.tenant_id
FROM pacientes p
JOIN contratos_aseguradora c ON c.id = p.contrato2id AND c.tenant_id = p.tenant_id
WHERE p.contrato2id IS NOT NULL
ON CONFLICT (tenant_id, paciente_id, contrato_aseguradora_id) DO NOTHING;

INSERT INTO paciente_contratos(id, paciente_id, contrato_aseguradora_id, orden, created_at, tenant_id)
SELECT gen_random_uuid(), p.id, p.contrato3id, 3, now(), p.tenant_id
FROM pacientes p
JOIN contratos_aseguradora c ON c.id = p.contrato3id AND c.tenant_id = p.tenant_id
WHERE p.contrato3id IS NOT NULL
ON CONFLICT (tenant_id, paciente_id, contrato_aseguradora_id) DO NOTHING;
");

            migrationBuilder.DropColumn(
                name: "contrato1id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "contrato2id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "contrato3id",
                table: "pacientes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "contrato1id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "contrato2id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "contrato3id",
                table: "pacientes",
                type: "uuid",
                nullable: true);
        }
    }
}
