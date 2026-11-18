using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPatronCierreYSolicitudesAutorizacion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "horas_limite_cierre",
                table: "catalogos_tipo_servicio",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "patron_cierre",
                table: "catalogos_tipo_servicio",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "autorizacion_solicitudes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    historia_clinica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asignacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fecha_atencion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detalle = table.Column<string>(type: "text", nullable: true),
                    concepto_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    motivo = table.Column<string>(type: "text", nullable: false),
                    solicitado_por_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    solicitado_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    resuelto_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resuelto_por_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    resuelto_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    respuesta = table.Column<string>(type: "text", nullable: true),
                    token_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_autorizacion_solicitudes", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_autorizacion_solicitudes_tenant_id_estado_created_at",
                table: "autorizacion_solicitudes",
                columns: new[] { "tenant_id", "estado", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_autorizacion_solicitudes_tenant_id_historia_clinica_id",
                table: "autorizacion_solicitudes",
                columns: new[] { "tenant_id", "historia_clinica_id" });

            // Permiso "historias.sin-control-cierre" para los roles Coordinador de cada agencia
            // (cierran sin patron de cierre). Owner/Admin lo tienen implicito; los demas roles se
            // marcan en Configuracion > Roles. Idempotente.
            migrationBuilder.Sql(@"
INSERT INTO rol_permisos (id, rol_id, modulo, ver, crear, editar, eliminar, created_at, tenant_id)
SELECT gen_random_uuid(), r.id, 'historias.sin-control-cierre', true, false, false, false, now(), r.tenant_id
FROM roles r
WHERE lower(r.nombre) = 'coordinador'
  AND NOT EXISTS (SELECT 1 FROM rol_permisos p WHERE p.rol_id = r.id AND p.modulo = 'historias.sin-control-cierre');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM rol_permisos WHERE modulo = 'historias.sin-control-cierre';");

            migrationBuilder.DropTable(
                name: "autorizacion_solicitudes");

            migrationBuilder.DropColumn(
                name: "horas_limite_cierre",
                table: "catalogos_tipo_servicio");

            migrationBuilder.DropColumn(
                name: "patron_cierre",
                table: "catalogos_tipo_servicio");
        }
    }
}
