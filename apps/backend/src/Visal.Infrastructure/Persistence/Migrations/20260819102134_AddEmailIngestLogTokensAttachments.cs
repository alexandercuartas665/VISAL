using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailIngestLogTokensAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "attachment_count",
                table: "email_ingest_logs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "input_tokens",
                table: "email_ingest_logs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "output_tokens",
                table: "email_ingest_logs",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "attachment_count",
                table: "email_ingest_logs");

            migrationBuilder.DropColumn(
                name: "input_tokens",
                table: "email_ingest_logs");

            migrationBuilder.DropColumn(
                name: "output_tokens",
                table: "email_ingest_logs");
        }
    }
}
