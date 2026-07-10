using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAtencionColumnaConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "atencion_columna_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    columna_key = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    visible = table.Column<bool>(type: "boolean", nullable: false),
                    alias = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    orden = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_atencion_columna_configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_atencion_columna_configs_tenant_id_columna_key",
                table: "atencion_columna_configs",
                columns: new[] { "tenant_id", "columna_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "atencion_columna_configs");
        }
    }
}
