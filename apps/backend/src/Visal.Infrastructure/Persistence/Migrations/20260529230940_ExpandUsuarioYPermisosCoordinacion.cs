using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ExpandUsuarioYPermisosCoordinacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "coordina_consultas",
                table: "tenant_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "coordina_enfermeria",
                table: "tenant_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "coordina_equipos",
                table: "tenant_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "coordina_terapias",
                table: "tenant_users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "celular",
                table: "platform_users",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ciudad",
                table: "platform_users",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "direccion",
                table: "platform_users",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fijo",
                table: "platform_users",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "primer_apellido",
                table: "platform_users",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "primer_nombre",
                table: "platform_users",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "segundo_apellido",
                table: "platform_users",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "segundo_nombre",
                table: "platform_users",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "username",
                table: "platform_users",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_users_username",
                table: "platform_users",
                column: "username",
                unique: true,
                filter: "username IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_platform_users_username",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "coordina_consultas",
                table: "tenant_users");

            migrationBuilder.DropColumn(
                name: "coordina_enfermeria",
                table: "tenant_users");

            migrationBuilder.DropColumn(
                name: "coordina_equipos",
                table: "tenant_users");

            migrationBuilder.DropColumn(
                name: "coordina_terapias",
                table: "tenant_users");

            migrationBuilder.DropColumn(
                name: "celular",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "ciudad",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "direccion",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "fijo",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "primer_apellido",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "primer_nombre",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "segundo_apellido",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "segundo_nombre",
                table: "platform_users");

            migrationBuilder.DropColumn(
                name: "username",
                table: "platform_users");
        }
    }
}
