using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutorizacionPendiente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "autorizacion_pendiente",
                table: "asignaciones",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_tenant_id_autorizacion_pendiente",
                table: "asignaciones",
                columns: new[] { "tenant_id", "autorizacion_pendiente" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_asignaciones_tenant_id_autorizacion_pendiente",
                table: "asignaciones");

            migrationBuilder.DropColumn(
                name: "autorizacion_pendiente",
                table: "asignaciones");
        }
    }
}
