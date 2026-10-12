using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCapacitacionProgreso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cuestionario_intentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cuestionario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    puntaje = table.Column<int>(type: "integer", nullable: false),
                    aprobado = table.Column<bool>(type: "boolean", nullable: false),
                    respuestas_json = table.Column<string>(type: "jsonb", nullable: false),
                    fecha_intento = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cuestionario_intentos", x => x.id);
                    table.ForeignKey(
                        name: "fk_cuestionario_intentos_cuestionarios_cuestionario_id",
                        column: x => x.cuestionario_id,
                        principalTable: "cuestionarios",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "curso_progresos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    curso_id = table.Column<Guid>(type: "uuid", nullable: false),
                    usuario_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_inicio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fecha_aprobacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    intentos = table.Column<int>(type: "integer", nullable: false),
                    mejor_nota = table.Column<int>(type: "integer", nullable: false),
                    aprobado = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_curso_progresos", x => x.id);
                    table.ForeignKey(
                        name: "fk_curso_progresos_cursos_curso_id",
                        column: x => x.curso_id,
                        principalTable: "cursos",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cuestionario_intentos_cuestionario_id",
                table: "cuestionario_intentos",
                column: "cuestionario_id");

            migrationBuilder.CreateIndex(
                name: "ix_cuestionario_intentos_tenant_id_cuestionario_id_usuario_id",
                table: "cuestionario_intentos",
                columns: new[] { "tenant_id", "cuestionario_id", "usuario_id" });

            migrationBuilder.CreateIndex(
                name: "ix_curso_progresos_curso_id",
                table: "curso_progresos",
                column: "curso_id");

            migrationBuilder.CreateIndex(
                name: "ix_curso_progresos_tenant_id_curso_id_usuario_id",
                table: "curso_progresos",
                columns: new[] { "tenant_id", "curso_id", "usuario_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cuestionario_intentos");

            migrationBuilder.DropTable(
                name: "curso_progresos");
        }
    }
}
