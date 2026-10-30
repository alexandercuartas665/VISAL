using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPacienteUsuarioIngreso : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "usuario_ingreso_id",
                table: "pacientes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "usuario_ingreso_nombre",
                table: "pacientes",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "usuario_ingreso_id",
                table: "pacientes");

            migrationBuilder.DropColumn(
                name: "usuario_ingreso_nombre",
                table: "pacientes");
        }
    }
}
