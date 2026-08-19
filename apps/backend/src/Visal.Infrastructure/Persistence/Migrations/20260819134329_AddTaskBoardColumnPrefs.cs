using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskBoardColumnPrefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "task_board_column_prefs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    platform_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    board_id = table.Column<Guid>(type: "uuid", nullable: false),
                    column_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    visible = table.Column<bool>(type: "boolean", nullable: false),
                    alias = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    orden = table.Column<int>(type: "integer", nullable: true),
                    ancho = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_task_board_column_prefs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_task_board_column_prefs_tenant_id_platform_user_id_board_id",
                table: "task_board_column_prefs",
                columns: new[] { "tenant_id", "platform_user_id", "board_id", "column_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "task_board_column_prefs");
        }
    }
}
