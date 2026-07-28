using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPacienteContratos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "paciente_contratos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contrato_aseguradora_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paciente_contratos", x => x.id);
                    table.ForeignKey(
                        name: "fk_paciente_contratos_contratos_aseguradora_contrato_asegurado",
                        column: x => x.contrato_aseguradora_id,
                        principalTable: "contratos_aseguradora",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_paciente_contratos_pacientes_paciente_id",
                        column: x => x.paciente_id,
                        principalTable: "pacientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_paciente_contratos_contrato_aseguradora_id",
                table: "paciente_contratos",
                column: "contrato_aseguradora_id");

            migrationBuilder.CreateIndex(
                name: "ix_paciente_contratos_paciente_id",
                table: "paciente_contratos",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "ix_paciente_contratos_tenant_id_paciente_id_contrato_asegurado",
                table: "paciente_contratos",
                columns: new[] { "tenant_id", "paciente_id", "contrato_aseguradora_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_paciente_contratos_tenant_id_paciente_id_orden",
                table: "paciente_contratos",
                columns: new[] { "tenant_id", "paciente_id", "orden" });

            // Backfill: copia contratos de los slots fijos pacientes.contrato_1_id/2_id/3_id
            // a la tabla nueva con orden 1/2/3 respectivamente. Idempotente (ON CONFLICT)
            // para poder re-correr la migracion sin duplicar filas si algun paciente ya
            // tuviera datos en la tabla nueva.
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

DO $$
DECLARE n int;
BEGIN
    SELECT COUNT(*) INTO n FROM paciente_contratos;
    RAISE NOTICE 'Backfill paciente_contratos: % filas', n;
END $$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "paciente_contratos");
        }
    }
}
