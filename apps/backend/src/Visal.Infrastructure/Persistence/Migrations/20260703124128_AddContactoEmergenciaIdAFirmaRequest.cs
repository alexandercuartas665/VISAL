using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactoEmergenciaIdAFirmaRequest : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "contacto_emergencia_id",
                table: "firma_paciente_requests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_firma_paciente_requests_contacto_emergencia_id",
                table: "firma_paciente_requests",
                column: "contacto_emergencia_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_firma_paciente_requests_contacto_emergencia_id",
                table: "firma_paciente_requests");

            migrationBuilder.DropColumn(
                name: "contacto_emergencia_id",
                table: "firma_paciente_requests");
        }
    }
}
