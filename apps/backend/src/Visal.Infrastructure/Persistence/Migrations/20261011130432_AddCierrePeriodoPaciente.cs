using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCierrePeriodoPaciente : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "paciente_id",
                table: "cierre_periodos",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "paciente_nombre",
                table: "cierre_periodos",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_cierre_periodos_tenant_id_anio_mes_paciente_id",
                table: "cierre_periodos",
                columns: new[] { "tenant_id", "anio", "mes", "paciente_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_cierre_periodos_tenant_id_anio_mes_paciente_id",
                table: "cierre_periodos");

            migrationBuilder.DropColumn(
                name: "paciente_id",
                table: "cierre_periodos");

            migrationBuilder.DropColumn(
                name: "paciente_nombre",
                table: "cierre_periodos");
        }
    }
}
