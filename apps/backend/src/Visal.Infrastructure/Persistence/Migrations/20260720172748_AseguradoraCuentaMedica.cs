using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AseguradoraCuentaMedica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "aseguradora_cuenta_medica_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    aseguradora_id = table.Column<Guid>(type: "uuid", nullable: false),
                    portada_habilitada = table.Column<bool>(type: "boolean", nullable: false),
                    portada_logo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    portada_titulo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    portada_subtitulo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    portada_texto_legal = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    indice_habilitado = table.Column<bool>(type: "boolean", nullable: false),
                    patron_nombre_default = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aseguradora_cuenta_medica_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "aseguradora_informe_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    config_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    seccion = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    origen = table.Column<int>(type: "integer", nullable: false),
                    tipologia_archivo_id = table.Column<Guid>(type: "uuid", nullable: true),
                    alias = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    patron_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    obligatorio = table.Column<bool>(type: "boolean", nullable: false),
                    solo_ultimo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_aseguradora_informe_items", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_aseguradora_cuenta_medica_configs_tenant_id_aseguradora_id",
                table: "aseguradora_cuenta_medica_configs",
                columns: new[] { "tenant_id", "aseguradora_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_aseguradora_informe_items_tenant_id_config_id_orden",
                table: "aseguradora_informe_items",
                columns: new[] { "tenant_id", "config_id", "orden" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "aseguradora_cuenta_medica_configs");

            migrationBuilder.DropTable(
                name: "aseguradora_informe_items");
        }
    }
}
