using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GupshupProvider : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "gupshup_app_id",
                table: "whats_app_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "inbound_token",
                table: "whats_app_lines",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            // Las lineas existentes son historicamente Evolution; el default
            // vacio que sugiere EF romperia queries que filtran por provider.
            migrationBuilder.AddColumn<string>(
                name: "provider",
                table: "whats_app_lines",
                type: "character varying(40)",
                maxLength: 40,
                nullable: false,
                defaultValue: "Evolution");

            migrationBuilder.CreateTable(
                name: "tenant_gupshup_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_id = table.Column<Guid>(type: "uuid", nullable: false),
                    app_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    waba_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    phone_number = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    api_key_encrypted = table.Column<string>(type: "text", nullable: false),
                    partner_token_encrypted = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    last_validated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_gupshup_configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_whats_app_lines_inbound_token",
                table: "whats_app_lines",
                column: "inbound_token",
                unique: true,
                filter: "inbound_token IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_whats_app_lines_tenant_id_provider",
                table: "whats_app_lines",
                columns: new[] { "tenant_id", "provider" });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_gupshup_configs_tenant_id_app_id",
                table: "tenant_gupshup_configs",
                columns: new[] { "tenant_id", "app_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tenant_gupshup_configs");

            migrationBuilder.DropIndex(
                name: "ix_whats_app_lines_inbound_token",
                table: "whats_app_lines");

            migrationBuilder.DropIndex(
                name: "ix_whats_app_lines_tenant_id_provider",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "gupshup_app_id",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "inbound_token",
                table: "whats_app_lines");

            migrationBuilder.DropColumn(
                name: "provider",
                table: "whats_app_lines");
        }
    }
}
