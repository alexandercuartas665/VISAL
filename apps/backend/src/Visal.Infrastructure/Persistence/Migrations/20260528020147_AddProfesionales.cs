using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfesionales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sub_categorias_profesional",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sub_categorias_profesional", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tipos_profesional",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tipos_profesional", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "profesionales",
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
                    tipo_profesional_id = table.Column<Guid>(type: "uuid", nullable: true),
                    registro_medico = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    ciudad = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    celular = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    firma_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profesionales", x => x.id);
                    table.ForeignKey(
                        name: "fk_profesionales_tipos_profesional_tipo_profesional_id",
                        column: x => x.tipo_profesional_id,
                        principalTable: "tipos_profesional",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "profesional_agencias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profesional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agencia = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profesional_agencias", x => x.id);
                    table.ForeignKey(
                        name: "fk_profesional_agencias_profesionales_profesional_id",
                        column: x => x.profesional_id,
                        principalTable: "profesionales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "profesional_sub_categorias",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profesional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sub_categoria_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profesional_sub_categorias", x => x.id);
                    table.ForeignKey(
                        name: "fk_profesional_sub_categorias_profesionales_profesional_id",
                        column: x => x.profesional_id,
                        principalTable: "profesionales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_profesional_sub_categorias_sub_categorias_profesional_sub_c",
                        column: x => x.sub_categoria_id,
                        principalTable: "sub_categorias_profesional",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_profesional_agencias_profesional_id",
                table: "profesional_agencias",
                column: "profesional_id");

            migrationBuilder.CreateIndex(
                name: "ix_profesional_agencias_tenant_id_profesional_id",
                table: "profesional_agencias",
                columns: new[] { "tenant_id", "profesional_id" });

            migrationBuilder.CreateIndex(
                name: "ix_profesional_sub_categorias_profesional_id",
                table: "profesional_sub_categorias",
                column: "profesional_id");

            migrationBuilder.CreateIndex(
                name: "ix_profesional_sub_categorias_sub_categoria_id",
                table: "profesional_sub_categorias",
                column: "sub_categoria_id");

            migrationBuilder.CreateIndex(
                name: "ix_profesional_sub_categorias_tenant_id_profesional_id",
                table: "profesional_sub_categorias",
                columns: new[] { "tenant_id", "profesional_id" });

            migrationBuilder.CreateIndex(
                name: "ix_profesionales_tenant_id_numero_documento",
                table: "profesionales",
                columns: new[] { "tenant_id", "numero_documento" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_profesionales_tipo_profesional_id",
                table: "profesionales",
                column: "tipo_profesional_id");

            migrationBuilder.CreateIndex(
                name: "ix_sub_categorias_profesional_tenant_id_nombre",
                table: "sub_categorias_profesional",
                columns: new[] { "tenant_id", "nombre" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tipos_profesional_tenant_id_nombre",
                table: "tipos_profesional",
                columns: new[] { "tenant_id", "nombre" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "profesional_agencias");

            migrationBuilder.DropTable(
                name: "profesional_sub_categorias");

            migrationBuilder.DropTable(
                name: "profesionales");

            migrationBuilder.DropTable(
                name: "sub_categorias_profesional");

            migrationBuilder.DropTable(
                name: "tipos_profesional");
        }
    }
}
