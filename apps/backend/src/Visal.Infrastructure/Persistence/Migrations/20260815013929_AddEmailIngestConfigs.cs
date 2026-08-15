using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailIngestConfigs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "email_ingest_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    message_id = table.Column<string>(type: "character varying(998)", maxLength: 998, nullable: false),
                    imap_uid = table.Column<long>(type: "bigint", nullable: true),
                    from_address = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    from_name = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    subject = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resultado = table.Column<int>(type: "integer", nullable: false),
                    task_card_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo_pqrs = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    classification_json = table.Column<string>(type: "text", nullable: true),
                    error_message = table.Column<string>(type: "text", nullable: true),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_email_ingest_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_email_ingest_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    imap_host = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    imap_port = table.Column<int>(type: "integer", nullable: false),
                    imap_use_ssl = table.Column<bool>(type: "boolean", nullable: false),
                    email_address = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    app_password_encrypted = table.Column<string>(type: "text", nullable: true),
                    folder = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    classifier_agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_board_id = table.Column<Guid>(type: "uuid", nullable: true),
                    target_column_id = table.Column<Guid>(type: "uuid", nullable: true),
                    poll_interval_minutes = table.Column<int>(type: "integer", nullable: false),
                    lookback_days = table.Column<int>(type: "integer", nullable: false),
                    max_por_corrida = table.Column<int>(type: "integer", nullable: false),
                    only_unread = table.Column<bool>(type: "boolean", nullable: false),
                    mark_as_read = table.Column<bool>(type: "boolean", nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_polled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    last_result_summary = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_email_ingest_configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_email_ingest_logs_config_id_message_id",
                table: "email_ingest_logs",
                columns: new[] { "config_id", "message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_email_ingest_configs_tenant_id",
                table: "tenant_email_ingest_configs",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "email_ingest_logs");

            migrationBuilder.DropTable(
                name: "tenant_email_ingest_configs");
        }
    }
}
