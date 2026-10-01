using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHcMarcaError : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hc_marcas_error",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asignacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_asignacion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    paciente_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    paciente_doc = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    observacion = table.Column<string>(type: "text", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    marcado_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reparado_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reparado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    observacion_reparacion = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hc_marcas_error", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hc_marcas_error_tenant_id_asignacion_id",
                table: "hc_marcas_error",
                columns: new[] { "tenant_id", "asignacion_id" });

            migrationBuilder.CreateIndex(
                name: "ix_hc_marcas_error_tenant_id_estado",
                table: "hc_marcas_error",
                columns: new[] { "tenant_id", "estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hc_marcas_error");
        }
    }
}
