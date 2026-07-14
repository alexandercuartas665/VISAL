using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTurnosProgramaciones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tipos_turno",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    etiqueta = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    horas_default = table.Column<decimal>(type: "numeric(4,1)", nullable: false),
                    color_fondo = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    color_texto = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    color_borde = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tipos_turno", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "turno_programaciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sucursal_id = table.Column<Guid>(type: "uuid", nullable: true),
                    tipo_servicio_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    mes = table.Column<int>(type: "integer", nullable: false),
                    anio = table.Column<int>(type: "integer", nullable: false),
                    descripcion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    grid_data_json = table.Column<string>(type: "jsonb", nullable: false),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_turno_programaciones", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tipos_turno_tenant_id_activo_orden",
                table: "tipos_turno",
                columns: new[] { "tenant_id", "activo", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_tipos_turno_tenant_id_codigo",
                table: "tipos_turno",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_turno_programaciones_tenant_id_sucursal_id_anio_mes",
                table: "turno_programaciones",
                columns: new[] { "tenant_id", "sucursal_id", "anio", "mes" });

            migrationBuilder.CreateIndex(
                name: "ix_turno_programaciones_tenant_id_sucursal_id_anio_mes_nombre",
                table: "turno_programaciones",
                columns: new[] { "tenant_id", "sucursal_id", "anio", "mes", "nombre" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tipos_turno");

            migrationBuilder.DropTable(
                name: "turno_programaciones");
        }
    }
}
