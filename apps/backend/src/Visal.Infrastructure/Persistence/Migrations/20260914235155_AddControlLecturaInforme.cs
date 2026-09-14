using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddControlLecturaInforme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "profesional_id",
                table: "alerta_envios",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "informe_accesos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profesional_id = table.Column<Guid>(type: "uuid", nullable: true),
                    accedido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    token_tipo = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_informe_accesos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alerta_envios_tenant_id_profesional_id_periodo",
                table: "alerta_envios",
                columns: new[] { "tenant_id", "profesional_id", "periodo" });

            migrationBuilder.CreateIndex(
                name: "ix_informe_accesos_tenant_id_profesional_id_accedido_en",
                table: "informe_accesos",
                columns: new[] { "tenant_id", "profesional_id", "accedido_en" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "informe_accesos");

            migrationBuilder.DropIndex(
                name: "ix_alerta_envios_tenant_id_profesional_id_periodo",
                table: "alerta_envios");

            migrationBuilder.DropColumn(
                name: "profesional_id",
                table: "alerta_envios");
        }
    }
}
