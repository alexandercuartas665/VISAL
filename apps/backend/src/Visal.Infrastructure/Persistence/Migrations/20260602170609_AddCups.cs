using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tabla = table.Column<string>(type: "text", nullable: true),
                    codigo = table.Column<string>(type: "text", nullable: true),
                    nombre = table.Column<string>(type: "text", nullable: true),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    habilitado = table.Column<string>(type: "text", nullable: true),
                    aplicacion = table.Column<string>(type: "text", nullable: true),
                    is_standard_gel = table.Column<string>(type: "text", nullable: true),
                    is_standard_msps = table.Column<string>(type: "text", nullable: true),
                    extra_i = table.Column<string>(type: "text", nullable: true),
                    extra_ii = table.Column<string>(type: "text", nullable: true),
                    extra_iii = table.Column<string>(type: "text", nullable: true),
                    extra_iv = table.Column<string>(type: "text", nullable: true),
                    extra_v = table.Column<string>(type: "text", nullable: true),
                    extra_vi = table.Column<string>(type: "text", nullable: true),
                    extra_vii = table.Column<string>(type: "text", nullable: true),
                    extra_viii = table.Column<string>(type: "text", nullable: true),
                    extra_ix = table.Column<string>(type: "text", nullable: true),
                    extra_x = table.Column<string>(type: "text", nullable: true),
                    valor_registro = table.Column<string>(type: "text", nullable: true),
                    usuario_responsable = table.Column<string>(type: "text", nullable: true),
                    fecha_actualizacion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_public_private = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cups", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cups_tenant_id_codigo",
                table: "cups",
                columns: new[] { "tenant_id", "codigo" });

            migrationBuilder.CreateIndex(
                name: "ix_cups_tenant_id_extra_iv",
                table: "cups",
                columns: new[] { "tenant_id", "extra_iv" });

            migrationBuilder.CreateIndex(
                name: "ix_cups_tenant_id_nombre",
                table: "cups",
                columns: new[] { "tenant_id", "nombre" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cups");
        }
    }
}
