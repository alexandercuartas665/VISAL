using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCie11Config : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cie10codigo",
                table: "pacientes",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cie11configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    client_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    client_secret = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    search_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    mms_url_base = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cie11configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cie11configs_tenant_id",
                table: "cie11configs",
                column: "tenant_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cie11configs");

            migrationBuilder.DropColumn(
                name: "cie10codigo",
                table: "pacientes");
        }
    }
}
