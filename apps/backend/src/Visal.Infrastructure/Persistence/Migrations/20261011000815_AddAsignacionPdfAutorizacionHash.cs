using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAsignacionPdfAutorizacionHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pdf_autorizacion_hash",
                table: "asignaciones",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_asignaciones_tenant_id_pdf_autorizacion_hash",
                table: "asignaciones",
                columns: new[] { "tenant_id", "pdf_autorizacion_hash" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_asignaciones_tenant_id_pdf_autorizacion_hash",
                table: "asignaciones");

            migrationBuilder.DropColumn(
                name: "pdf_autorizacion_hash",
                table: "asignaciones");
        }
    }
}
