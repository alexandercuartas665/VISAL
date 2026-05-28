using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRolesUsuariosSucursalesPacientes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "rol_id",
                table: "tenant_users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "sucursal_id",
                table: "tenant_users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "es_global",
                table: "platform_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "pacientes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero_documento = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    tipo_documento = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    primer_nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    segundo_nombre = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    primer_apellido = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    segundo_apellido = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    nombre_completo = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    fecha_nacimiento = table.Column<DateOnly>(type: "date", nullable: true),
                    sexo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    estado_civil = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    telefono = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    email = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    direccion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ciudad = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    zona = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ocupacion = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    regimen = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    aseguradora_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contacto_emergencia = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    parentesco = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    telefono_emergencia = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pacientes", x => x.id);
                    table.ForeignKey(
                        name: "fk_pacientes_aseguradoras_aseguradora_id",
                        column: x => x.aseguradora_id,
                        principalTable: "aseguradoras",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "sucursales",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    direccion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    ciudad = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    telefono = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sucursales", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "rol_permisos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol_id = table.Column<Guid>(type: "uuid", nullable: false),
                    modulo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    ver = table.Column<bool>(type: "boolean", nullable: false),
                    crear = table.Column<bool>(type: "boolean", nullable: false),
                    editar = table.Column<bool>(type: "boolean", nullable: false),
                    eliminar = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rol_permisos", x => x.id);
                    table.ForeignKey(
                        name: "fk_rol_permisos_roles_rol_id",
                        column: x => x.rol_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tenant_users_rol_id",
                table: "tenant_users",
                column: "rol_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_users_sucursal_id",
                table: "tenant_users",
                column: "sucursal_id");

            migrationBuilder.CreateIndex(
                name: "ix_pacientes_aseguradora_id",
                table: "pacientes",
                column: "aseguradora_id");

            migrationBuilder.CreateIndex(
                name: "ix_pacientes_tenant_id_numero_documento",
                table: "pacientes",
                columns: new[] { "tenant_id", "numero_documento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_rol_permisos_rol_id_modulo",
                table: "rol_permisos",
                columns: new[] { "rol_id", "modulo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_roles_tenant_id_nombre",
                table: "roles",
                columns: new[] { "tenant_id", "nombre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sucursales_tenant_id_codigo",
                table: "sucursales",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_tenant_users_roles_rol_id",
                table: "tenant_users",
                column: "rol_id",
                principalTable: "roles",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_tenant_users_sucursales_sucursal_id",
                table: "tenant_users",
                column: "sucursal_id",
                principalTable: "sucursales",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_tenant_users_roles_rol_id",
                table: "tenant_users");

            migrationBuilder.DropForeignKey(
                name: "fk_tenant_users_sucursales_sucursal_id",
                table: "tenant_users");

            migrationBuilder.DropTable(
                name: "pacientes");

            migrationBuilder.DropTable(
                name: "rol_permisos");

            migrationBuilder.DropTable(
                name: "sucursales");

            migrationBuilder.DropTable(
                name: "roles");

            migrationBuilder.DropIndex(
                name: "ix_tenant_users_rol_id",
                table: "tenant_users");

            migrationBuilder.DropIndex(
                name: "ix_tenant_users_sucursal_id",
                table: "tenant_users");

            migrationBuilder.DropColumn(
                name: "rol_id",
                table: "tenant_users");

            migrationBuilder.DropColumn(
                name: "sucursal_id",
                table: "tenant_users");

            migrationBuilder.DropColumn(
                name: "es_global",
                table: "platform_users");
        }
    }
}
