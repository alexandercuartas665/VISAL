using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPacienteEstadoAdmision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "estado_admision",
                table: "pacientes",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Abierto");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fecha_cierre_admision",
                table: "pacientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_pacientes_tenant_id_estado_admision",
                table: "pacientes",
                columns: new[] { "tenant_id", "estado_admision" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_pacientes_tenant_id_estado_admision",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "estado_admision",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "fecha_cierre_admision",
                table: "pacientes");
        }
    }
}
