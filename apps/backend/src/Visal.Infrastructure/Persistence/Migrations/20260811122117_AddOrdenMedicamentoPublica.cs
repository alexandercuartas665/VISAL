using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrdenMedicamentoPublica : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ordenes_medicamentos_publicas",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    historia_clinica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_verificacion = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    emitido_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    emitido_por = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_json = table.Column<string>(type: "jsonb", nullable: false),
                    revocada_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revocada_por = table.Column<Guid>(type: "uuid", nullable: true),
                    revocacion_motivo = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ordenes_medicamentos_publicas", x => x.id);
                    table.ForeignKey(
                        name: "fk_ordenes_medicamentos_publicas_historias_clinicas_historia_c",
                        column: x => x.historia_clinica_id,
                        principalTable: "historias_clinicas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "verificacion_orden_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    orden_medicamento_publica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo_consultado = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    user_agent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    consultado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verificacion_orden_logs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_ordenes_medicamentos_publicas_codigo_verificacion",
                table: "ordenes_medicamentos_publicas",
                column: "codigo_verificacion",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ordenes_medicamentos_publicas_historia_clinica_id",
                table: "ordenes_medicamentos_publicas",
                column: "historia_clinica_id");

            migrationBuilder.CreateIndex(
                name: "ix_ordenes_medicamentos_publicas_tenant_id_historia_clinica_id",
                table: "ordenes_medicamentos_publicas",
                columns: new[] { "tenant_id", "historia_clinica_id" });

            migrationBuilder.CreateIndex(
                name: "ix_verificacion_orden_logs_orden_medicamento_publica_id_consul",
                table: "verificacion_orden_logs",
                columns: new[] { "orden_medicamento_publica_id", "consultado_en" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ordenes_medicamentos_publicas");

            migrationBuilder.DropTable(
                name: "verificacion_orden_logs");
        }
    }
}
