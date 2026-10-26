using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReportePlantillas : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reporte_plantillas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    categoria = table.Column<string>(type: "text", nullable: true),
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
                    table.PrimaryKey("pk_reporte_plantillas", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reporte_hojas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporte_plantilla_id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    entidad_key = table.Column<string>(type: "text", nullable: false),
                    modo = table.Column<string>(type: "text", nullable: false),
                    definicion_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reporte_hojas", x => x.id);
                    table.ForeignKey(
                        name: "fk_reporte_hojas_reporte_plantillas_reporte_plantilla_id",
                        column: x => x.reporte_plantilla_id,
                        principalTable: "reporte_plantillas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reporte_hojas_reporte_plantilla_id",
                table: "reporte_hojas",
                column: "reporte_plantilla_id");

            migrationBuilder.CreateIndex(
                name: "ix_reporte_hojas_tenant_id_reporte_plantilla_id_orden",
                table: "reporte_hojas",
                columns: new[] { "tenant_id", "reporte_plantilla_id", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_reporte_plantillas_tenant_id_orden",
                table: "reporte_plantillas",
                columns: new[] { "tenant_id", "orden" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reporte_hojas");

            migrationBuilder.DropTable(
                name: "reporte_plantillas");
        }
    }
}
