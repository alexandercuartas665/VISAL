using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertasEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "alerta_envios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    regla_id = table.Column<Guid>(type: "uuid", nullable: false),
                    asignacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    periodo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    canal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    destinatario = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    contacto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    fecha_envio = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    exito = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    external_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alerta_envios", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "alerta_reglas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    condicion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    filtro_modulo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    disparo_tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    dias_del_mes = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    meses_despues = table.Column<int>(type: "integer", nullable: true),
                    ancla_relativa = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    destinatario = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    usuario_sistema_id = table.Column<Guid>(type: "uuid", nullable: true),
                    canal = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    asunto = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cuerpo = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    hsm_line_id = table.Column<Guid>(type: "uuid", nullable: true),
                    hsm_template_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    hsm_template_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    hsm_parameter_count = table.Column<int>(type: "integer", nullable: false),
                    hsm_parametros_json = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alerta_reglas", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alerta_envios_tenant_id_regla_id_asignacion_id_periodo",
                table: "alerta_envios",
                columns: new[] { "tenant_id", "regla_id", "asignacion_id", "periodo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_alerta_reglas_tenant_id_activa",
                table: "alerta_reglas",
                columns: new[] { "tenant_id", "activa" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerta_envios");

            migrationBuilder.DropTable(
                name: "alerta_reglas");
        }
    }
}
