using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TenantSlogan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotente: si la columna ya existe (re-deploy o BD ya alterada por hotfix),
            // la migracion no falla. PostgreSQL: IF NOT EXISTS en ALTER TABLE.
            migrationBuilder.Sql("ALTER TABLE tenants ADD COLUMN IF NOT EXISTS slogan text NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE tenants DROP COLUMN IF EXISTS slogan;");
        }
    }
}
