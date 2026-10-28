using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPacienteCampoRigidoDefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "paciente_campo_rigido_defs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    campo_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    tab_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    ancho = table.Column<int>(type: "integer", nullable: false),
                    oculto = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paciente_campo_rigido_defs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_paciente_campo_rigido_defs_tenant_id_campo_key",
                table: "paciente_campo_rigido_defs",
                columns: new[] { "tenant_id", "campo_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_paciente_campo_rigido_defs_tenant_id_tab_key_orden",
                table: "paciente_campo_rigido_defs",
                columns: new[] { "tenant_id", "tab_key", "orden" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "paciente_campo_rigido_defs");
        }
    }
}
