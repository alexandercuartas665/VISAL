using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddModuloCapacitaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cuestionarios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    modulo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    titulo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    puntaje_minimo = table.Column<int>(type: "integer", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cuestionarios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cuestionario_preguntas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuestionario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    enunciado = table.Column<string>(type: "text", nullable: false),
                    opciones_json = table.Column<string>(type: "jsonb", nullable: false),
                    indice_correcto = table.Column<int>(type: "integer", nullable: false),
                    retroalimentacion = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cuestionario_preguntas", x => x.id);
                    table.ForeignKey(
                        name: "fk_cuestionario_preguntas_cuestionarios_cuestionario_id",
                        column: x => x.cuestionario_id,
                        principalTable: "cuestionarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cursos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    cuestionario_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cursos", x => x.id);
                    table.ForeignKey(
                        name: "fk_cursos_cuestionarios_cuestionario_id",
                        column: x => x.cuestionario_id,
                        principalTable: "cuestionarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "curso_modulos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    curso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_curso_modulos", x => x.id);
                    table.ForeignKey(
                        name: "fk_curso_modulos_cursos_curso_id",
                        column: x => x.curso_id,
                        principalTable: "cursos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "curso_lecciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    curso_modulo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    titulo = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ruta_archivo = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    mime = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    tamano_bytes = table.Column<long>(type: "bigint", nullable: true),
                    contenido = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_curso_lecciones", x => x.id);
                    table.ForeignKey(
                        name: "fk_curso_lecciones_curso_modulos_curso_modulo_id",
                        column: x => x.curso_modulo_id,
                        principalTable: "curso_modulos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cuestionario_preguntas_cuestionario_id",
                table: "cuestionario_preguntas",
                column: "cuestionario_id");

            migrationBuilder.CreateIndex(
                name: "ix_cuestionario_preguntas_tenant_id_cuestionario_id_orden",
                table: "cuestionario_preguntas",
                columns: new[] { "tenant_id", "cuestionario_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_cuestionarios_tenant_id_modulo",
                table: "cuestionarios",
                columns: new[] { "tenant_id", "modulo" });

            migrationBuilder.CreateIndex(
                name: "ix_curso_lecciones_curso_modulo_id",
                table: "curso_lecciones",
                column: "curso_modulo_id");

            migrationBuilder.CreateIndex(
                name: "ix_curso_lecciones_tenant_id_curso_modulo_id_orden",
                table: "curso_lecciones",
                columns: new[] { "tenant_id", "curso_modulo_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_curso_modulos_curso_id",
                table: "curso_modulos",
                column: "curso_id");

            migrationBuilder.CreateIndex(
                name: "ix_curso_modulos_tenant_id_curso_id_orden",
                table: "curso_modulos",
                columns: new[] { "tenant_id", "curso_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_cursos_cuestionario_id",
                table: "cursos",
                column: "cuestionario_id");

            migrationBuilder.CreateIndex(
                name: "ix_cursos_tenant_id_titulo",
                table: "cursos",
                columns: new[] { "tenant_id", "titulo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cuestionario_preguntas");

            migrationBuilder.DropTable(
                name: "curso_lecciones");

            migrationBuilder.DropTable(
                name: "curso_modulos");

            migrationBuilder.DropTable(
                name: "cursos");

            migrationBuilder.DropTable(
                name: "cuestionarios");
        }
    }
}
