using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLlamadasVoz : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "llamadas_voz",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    seguimiento_encuesta_id = table.Column<Guid>(type: "uuid", nullable: true),
                    paciente_id = table.Column<Guid>(type: "uuid", nullable: false),
                    call_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    from_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    to_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    estado = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    agent_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    duracion_segundos = table.Column<int>(type: "integer", nullable: true),
                    costo_usd = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    transcripcion = table.Column<string>(type: "text", nullable: true),
                    analisis_json = table.Column<string>(type: "text", nullable: true),
                    inicio_llamada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    fin_llamada = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_llamadas_voz", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_llamadas_voz_call_id",
                table: "llamadas_voz",
                column: "call_id");

            migrationBuilder.CreateIndex(
                name: "ix_llamadas_voz_tenant_id_seguimiento_encuesta_id",
                table: "llamadas_voz",
                columns: new[] { "tenant_id", "seguimiento_encuesta_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "llamadas_voz");
        }
    }
}
