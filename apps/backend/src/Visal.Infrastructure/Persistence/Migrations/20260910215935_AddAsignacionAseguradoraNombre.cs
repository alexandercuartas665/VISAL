using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAsignacionAseguradoraNombre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "aseguradora_nombre",
                table: "asignaciones",
                type: "text",
                nullable: true);

            // Backfill de asignaciones existentes (para que las ordenes viejas tambien
            // impriman la EPS). Corre una vez por base al aplicar la migracion.
            //
            // 1) Fuente historica correcta: la aseguradora del CONTRATO bajo el que se
            //    asigno el servicio (a.contrato_codigo -> contratos_aseguradora, scope
            //    por tenant). DISTINCT ON desempata determinísticamente un codigo de
            //    contrato que apunte a mas de una aseguradora (gana el mas reciente).
            migrationBuilder.Sql(@"
                UPDATE asignaciones a
                SET aseguradora_nombre = sub.nombre
                FROM (
                    SELECT DISTINCT ON (ca.tenant_id, ca.codigo_contrato)
                           ca.tenant_id, ca.codigo_contrato, ase.nombre
                    FROM contratos_aseguradora ca
                    JOIN aseguradoras ase ON ase.id = ca.aseguradora_id
                    ORDER BY ca.tenant_id, ca.codigo_contrato, ca.created_at DESC
                ) sub
                WHERE a.contrato_codigo = sub.codigo_contrato
                  AND a.tenant_id = sub.tenant_id
                  AND a.aseguradora_nombre IS NULL;");

            // 2) Fallback para las que no cruzan con un contrato: la aseguradora actual
            //    del paciente (mejor mostrar algo que nada).
            migrationBuilder.Sql(@"
                UPDATE asignaciones a
                SET aseguradora_nombre = ase.nombre
                FROM pacientes p
                JOIN aseguradoras ase ON ase.id = p.aseguradora_id
                WHERE a.paciente_id = p.id
                  AND a.aseguradora_nombre IS NULL
                  AND p.aseguradora_id IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "aseguradora_nombre",
                table: "asignaciones");
        }
    }
}
