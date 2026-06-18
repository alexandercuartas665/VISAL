using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InteroperabilidadConfigYCredencialesSede : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "interoperabilidad_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_sandbox = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    endpoint_produccion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    azure_tenant_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    scope = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    apim_subskey_sandbox_cifrada = table.Column<string>(type: "text", nullable: true),
                    apim_subskey_produccion_cifrada = table.Column<string>(type: "text", nullable: true),
                    ambiente_activo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interoperabilidad_configs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "interoperabilidad_credenciales_sede",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ambiente = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    codigo_habilitacion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    nombre_llave = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    client_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    client_secret_cifrado = table.Column<string>(type: "text", nullable: true),
                    fecha_expiracion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_interoperabilidad_credenciales_sede", x => x.id);
                    table.ForeignKey(
                        name: "fk_interoperabilidad_credenciales_sede_sucursales_sucursal_id",
                        column: x => x.sucursal_id,
                        principalTable: "sucursales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_interoperabilidad_configs_tenant_id",
                table: "interoperabilidad_configs",
                column: "tenant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_interoperabilidad_credenciales_sede_sucursal_id",
                table: "interoperabilidad_credenciales_sede",
                column: "sucursal_id");

            migrationBuilder.CreateIndex(
                name: "ix_interoperabilidad_credenciales_sede_tenant_id_sucursal_id_a",
                table: "interoperabilidad_credenciales_sede",
                columns: new[] { "tenant_id", "sucursal_id", "ambiente" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "interoperabilidad_configs");

            migrationBuilder.DropTable(
                name: "interoperabilidad_credenciales_sede");
        }
    }
}
