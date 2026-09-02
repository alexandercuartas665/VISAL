using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantRetellConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_retell_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_key_encrypted = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    agent_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    from_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    webhook_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    telnyx_sip_username = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_retell_configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_retell_configs_tenant_id",
                table: "tenant_retell_configs",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_retell_configs_webhook_token",
                table: "tenant_retell_configs",
                column: "webhook_token");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_retell_configs");
        }
    }
}
