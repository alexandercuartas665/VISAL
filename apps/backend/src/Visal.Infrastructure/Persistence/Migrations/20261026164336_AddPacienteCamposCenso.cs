using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPacienteCamposCenso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "cie10codigo2",
                table: "pacientes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "diagnostico_secundario",
                table: "pacientes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_hospitalizacion",
                table: "pacientes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "fecha_reingreso",
                table: "pacientes",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "observacion_reingreso",
                table: "pacientes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "observaciones",
                table: "pacientes",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "observaciones_hospitalizacion",
                table: "pacientes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "cie10codigo2",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "diagnostico_secundario",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "fecha_hospitalizacion",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "fecha_reingreso",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "observacion_reingreso",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "observaciones",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "observaciones_hospitalizacion",
                table: "pacientes");
        }
    }
}
