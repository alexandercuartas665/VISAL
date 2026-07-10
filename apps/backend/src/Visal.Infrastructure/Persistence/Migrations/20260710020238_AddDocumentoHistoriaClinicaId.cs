using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentoHistoriaClinicaId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "historia_clinica_id",
                table: "nota_medica_documentos",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_nota_medica_documentos_tenant_id_historia_clinica_id",
                table: "nota_medica_documentos",
                columns: new[] { "tenant_id", "historia_clinica_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_nota_medica_documentos_tenant_id_historia_clinica_id",
                table: "nota_medica_documentos");

            migrationBuilder.DropColumn(
                name: "historia_clinica_id",
                table: "nota_medica_documentos");
        }
    }
}
