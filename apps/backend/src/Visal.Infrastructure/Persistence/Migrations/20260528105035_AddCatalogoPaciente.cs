using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogoPaciente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "clasificacion_grupo_patologia",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "clasificacion_paciente",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "tipo_tutela",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "tipo_usuario",
                table: "pacientes");

            migrationBuilder.AddColumn<Guid>(
                name: "clasificacion_grupo_patologia_id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "clasificacion_paciente_id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tipo_tutela_id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "tipo_usuario_id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "catalogos_paciente",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tipo = table.Column<int>(type: "integer", maxLength: 60, nullable: false),
                    codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogos_paciente", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalogos_paciente_tenant_id_tipo_codigo",
                table: "catalogos_paciente",
                columns: new[] { "tenant_id", "tipo", "codigo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "catalogos_paciente");

            migrationBuilder.DropColumn(
                name: "clasificacion_grupo_patologia_id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "clasificacion_paciente_id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "tipo_tutela_id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "tipo_usuario_id",
                table: "pacientes");

            migrationBuilder.AddColumn<string>(
                name: "clasificacion_grupo_patologia",
                table: "pacientes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "clasificacion_paciente",
                table: "pacientes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tipo_tutela",
                table: "pacientes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tipo_usuario",
                table: "pacientes",
                type: "character varying(60)",
                maxLength: 60,
                nullable: true);
        }
    }
}
