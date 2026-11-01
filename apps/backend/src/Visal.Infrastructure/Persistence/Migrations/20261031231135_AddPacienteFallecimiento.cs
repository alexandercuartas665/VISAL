using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPacienteFallecimiento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "certificado_visal",
                table: "pacientes",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fallecimiento_reportado_por",
                table: "pacientes",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "fallecimiento_reportado_por_id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "fecha_fallecimiento",
                table: "pacientes",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "certificado_visal",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "fallecimiento_reportado_por",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "fallecimiento_reportado_por_id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "fecha_fallecimiento",
                table: "pacientes");
        }
    }
}
