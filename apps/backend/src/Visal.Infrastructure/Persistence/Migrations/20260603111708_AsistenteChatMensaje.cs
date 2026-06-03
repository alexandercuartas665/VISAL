using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AsistenteChatMensaje : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asistente_chat_mensajes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rol = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    texto = table.Column<string>(type: "text", nullable: false),
                    cuando = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    historia_clinica_id = table.Column<Guid>(type: "uuid", nullable: true),
                    nota_medica_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agente_id = table.Column<Guid>(type: "uuid", nullable: true),
                    agente_nombre_snapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asistente_chat_mensajes", x => x.id);
                    table.ForeignKey(
                        name: "fk_asistente_chat_mensajes_pacientes_paciente_id",
                        column: x => x.paciente_id,
                        principalTable: "pacientes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asistente_chat_mensajes_paciente_id",
                table: "asistente_chat_mensajes",
                column: "paciente_id");

            migrationBuilder.CreateIndex(
                name: "ix_asistente_chat_mensajes_tenant_id_paciente_id_cuando",
                table: "asistente_chat_mensajes",
                columns: new[] { "tenant_id", "paciente_id", "cuando" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asistente_chat_mensajes");
        }
    }
}
