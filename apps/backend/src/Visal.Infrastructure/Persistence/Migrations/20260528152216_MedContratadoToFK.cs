using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MedContratadoToFK : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "med_contratado",
                table: "pacientes");

            migrationBuilder.AddColumn<Guid>(
                name: "med_contratado_id",
                table: "pacientes",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "med_contratado_id",
                table: "pacientes");

            migrationBuilder.AddColumn<string>(
                name: "med_contratado",
                table: "pacientes",
                type: "character varying(80)",
                maxLength: 80,
                nullable: true);
        }
    }
}
