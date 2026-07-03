using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllowNullNotaMedicaIdInDocumento : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_nota_medica_documentos_notas_medicas_nota_medica_id",
                table: "nota_medica_documentos");

            migrationBuilder.AlterColumn<Guid>(
                name: "nota_medica_id",
                table: "nota_medica_documentos",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddForeignKey(
                name: "fk_nota_medica_documentos_notas_medicas_nota_medica_id",
                table: "nota_medica_documentos",
                column: "nota_medica_id",
                principalTable: "notas_medicas",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_nota_medica_documentos_notas_medicas_nota_medica_id",
                table: "nota_medica_documentos");

            migrationBuilder.AlterColumn<Guid>(
                name: "nota_medica_id",
                table: "nota_medica_documentos",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "fk_nota_medica_documentos_notas_medicas_nota_medica_id",
                table: "nota_medica_documentos",
                column: "nota_medica_id",
                principalTable: "notas_medicas",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
