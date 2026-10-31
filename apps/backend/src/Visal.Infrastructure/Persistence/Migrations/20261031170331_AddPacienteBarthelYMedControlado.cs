using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPacienteBarthelYMedControlado : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "barthel_actualizado_en",
                table: "pacientes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "barthel_clasificacion",
                table: "pacientes",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "barthel_datos_json",
                table: "pacientes",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "barthel_total",
                table: "pacientes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "medicamento_controlado",
                table: "pacientes",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "barthel_actualizado_en",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "barthel_clasificacion",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "barthel_datos_json",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "barthel_total",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "medicamento_controlado",
                table: "pacientes");
        }
    }
}
