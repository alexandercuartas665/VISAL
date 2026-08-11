using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WebhookFormularios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "form_webhook_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dedup_hash = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    lead_id = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_webhook_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_form_webhook_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    token_encrypted = table.Column<string>(type: "text", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_form_webhook_configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_form_webhook_events_received_at",
                table: "form_webhook_events",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ix_form_webhook_events_tenant_id_dedup_hash",
                table: "form_webhook_events",
                columns: new[] { "tenant_id", "dedup_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_form_webhook_configs_tenant_id",
                table: "tenant_form_webhook_configs",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_form_webhook_configs_token_hash",
                table: "tenant_form_webhook_configs",
                column: "token_hash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "form_webhook_events");

            migrationBuilder.DropTable(
                name: "tenant_form_webhook_configs");
        }
    }
}
