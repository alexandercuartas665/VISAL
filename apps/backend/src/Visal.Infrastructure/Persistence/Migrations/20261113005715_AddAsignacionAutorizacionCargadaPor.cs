using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAsignacionAutorizacionCargadaPor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "autorizacion_cargada_en",
                table: "asignaciones",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "autorizacion_cargada_por_nombre",
                table: "asignaciones",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "autorizacion_cargada_por_user_id",
                table: "asignaciones",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "autorizacion_origen_modulo",
                table: "asignaciones",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "autorizacion_cargada_en",
                table: "asignaciones");

            migrationBuilder.DropColumn(
                name: "autorizacion_cargada_por_nombre",
                table: "asignaciones");

            migrationBuilder.DropColumn(
                name: "autorizacion_cargada_por_user_id",
                table: "asignaciones");

            migrationBuilder.DropColumn(
                name: "autorizacion_origen_modulo",
                table: "asignaciones");
        }
    }
}
