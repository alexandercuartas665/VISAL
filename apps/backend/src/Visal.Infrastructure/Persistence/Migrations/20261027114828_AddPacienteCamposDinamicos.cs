using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPacienteCamposDinamicos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "campos_dinamicos_json",
                table: "pacientes",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "paciente_campo_defs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tab_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    campo_key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    label = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    opciones = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ancho = table.Column<int>(type: "integer", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    descripcion = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    requerido = table.Column<bool>(type: "boolean", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    es_sistema = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paciente_campo_defs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "paciente_ficha_defs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tab_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    titulo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    oculto = table.Column<bool>(type: "boolean", nullable: false),
                    es_sistema = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_paciente_ficha_defs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_paciente_campo_defs_tenant_id_campo_key",
                table: "paciente_campo_defs",
                columns: new[] { "tenant_id", "campo_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_paciente_campo_defs_tenant_id_tab_key_orden",
                table: "paciente_campo_defs",
                columns: new[] { "tenant_id", "tab_key", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_paciente_ficha_defs_tenant_id_orden",
                table: "paciente_ficha_defs",
                columns: new[] { "tenant_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_paciente_ficha_defs_tenant_id_tab_key",
                table: "paciente_ficha_defs",
                columns: new[] { "tenant_id", "tab_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "paciente_campo_defs");

            migrationBuilder.DropTable(
                name: "paciente_ficha_defs");

            migrationBuilder.DropColumn(
                name: "campos_dinamicos_json",
                table: "pacientes");
        }
    }
}
