using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedTipoDocumentoCatalogo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Siembra los tipos de documento estandar (tipo=11 =
            // CatalogoPacienteTipo.TipoDocumento) para TODOS los tenants. Antes
            // estaban hardcodeados en el <select> de Admision.razor. Es una
            // migracion de datos (no de esquema) porque en produccion los seeders
            // no corren; solo se aplican migraciones. Idempotente: inserta unicamente
            // los que falten. Cada tenant luego agrega/edita/inactiva los suyos en
            // /cfg-pacientes.
            migrationBuilder.Sql(@"
                INSERT INTO catalogos_paciente (id, tenant_id, tipo, codigo, nombre, activo, created_at)
                SELECT gen_random_uuid(), t.id, 11, v.codigo, v.nombre, true, now()
                FROM tenants t
                CROSS JOIN (VALUES
                    ('CC', 'Cedula de Ciudadania'),
                    ('CE', 'Cedula de Extranjeria'),
                    ('TI', 'Tarjeta de Identidad'),
                    ('RC', 'Registro Civil'),
                    ('PA', 'Pasaporte'),
                    ('MS', 'Menor Sin Identificacion')
                ) AS v(codigo, nombre)
                WHERE NOT EXISTS (
                    SELECT 1 FROM catalogos_paciente c
                    WHERE c.tenant_id = t.id AND c.tipo = 11 AND c.codigo = v.codigo
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback: elimina los 6 estandar sembrados. No toca los tipos de
            // documento propios que cada tenant haya agregado (otros codigos).
            migrationBuilder.Sql(@"
                DELETE FROM catalogos_paciente
                WHERE tipo = 11 AND codigo IN ('CC','CE','TI','RC','PA','MS');
            ");
        }
    }
}
