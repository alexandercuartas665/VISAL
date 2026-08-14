using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Repara los tableros que quedaron con owner_platform_user_id = Guid.Empty. Ese valor venia
    /// del bug del claim (las paginas de Tableros leian "user_id"/"sub" en vez de NameIdentifier,
    /// asi que el actor efectivo era Guid.Empty y todo tablero creado quedaba con ese dueno). Ahora
    /// que el login emite el claim "user_id", el actor es el id real y esos tableros quedarian sin
    /// dueno accesible. Se les asigna el primer usuario activo del tenant. Idempotente.
    /// </summary>
    public partial class FixTableroOwnersVacios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE task_boards b
SET owner_platform_user_id = (
        SELECT tu.platform_user_id FROM tenant_users tu
        WHERE tu.tenant_id = b.tenant_id AND tu.status = 'Active'
        ORDER BY tu.created_at LIMIT 1),
    updated_at = now()
WHERE b.owner_platform_user_id = '00000000-0000-0000-0000-000000000000'
  AND EXISTS (SELECT 1 FROM tenant_users tu WHERE tu.tenant_id = b.tenant_id AND tu.status = 'Active');
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No hay reverso fiable: el owner original (Guid.Empty) era un artefacto del bug.
        }
    }
}
