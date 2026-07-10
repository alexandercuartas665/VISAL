using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHcMenuConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hc_menu_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo_servicio = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    pestana_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    visible = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hc_menu_configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hc_menu_configs_tenant_id_tipo_servicio_pestana_key",
                table: "hc_menu_configs",
                columns: new[] { "tenant_id", "tipo_servicio", "pestana_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hc_menu_configs");
        }
    }
}
