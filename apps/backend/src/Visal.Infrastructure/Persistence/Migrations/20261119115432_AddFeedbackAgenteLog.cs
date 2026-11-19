using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackAgenteLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Tope de espera de la auditoria IA al cerrar: 90 s se quedaba corto (auditorias de 90-120 s).
            // Solo sube las politicas que siguen en el default anterior.
            migrationBuilder.Sql("UPDATE revision_policies SET auditoria_ia_timeout_seg = 180 WHERE auditoria_ia_timeout_seg = 90;");

            migrationBuilder.AlterColumn<Guid>(
                name: "revision_clinica_id",
                table: "revision_agente_logs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "feedback_comentario",
                table: "revision_agente_logs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "feedback_en",
                table: "revision_agente_logs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "feedback_usuario_id",
                table: "revision_agente_logs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "feedback_util",
                table: "revision_agente_logs",
                type: "boolean",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE revision_policies SET auditoria_ia_timeout_seg = 90 WHERE auditoria_ia_timeout_seg = 180;");

            migrationBuilder.DropColumn(
                name: "feedback_comentario",
                table: "revision_agente_logs");

            migrationBuilder.DropColumn(
                name: "feedback_en",
                table: "revision_agente_logs");

            migrationBuilder.DropColumn(
                name: "feedback_usuario_id",
                table: "revision_agente_logs");

            migrationBuilder.DropColumn(
                name: "feedback_util",
                table: "revision_agente_logs");

            migrationBuilder.AlterColumn<Guid>(
                name: "revision_clinica_id",
                table: "revision_agente_logs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
