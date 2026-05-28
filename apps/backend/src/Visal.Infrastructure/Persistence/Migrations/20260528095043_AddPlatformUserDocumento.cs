using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformUserDocumento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "documento",
                table: "platform_users",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_users_documento",
                table: "platform_users",
                column: "documento",
                unique: true,
                filter: "documento IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_platform_users_documento",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "documento",
                table: "platform_users");
        }
    }
}
