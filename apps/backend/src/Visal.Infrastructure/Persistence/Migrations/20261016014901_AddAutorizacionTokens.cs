using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAutorizacionTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "autorizacion_fecha_autorizada",
                table: "historias_clinicas",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "autorizacion_fecha_token_id",
                table: "historias_clinicas",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "autorizacion_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    observacion = table.Column<string>(type: "text", nullable: false),
                    generado_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado = table.Column<int>(type: "integer", nullable: false),
                    usado_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    usado_por_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usado_por_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    historia_clinica_id = table.Column<Guid>(type: "uuid", nullable: true),
                    asignacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo_uso = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_autorizacion_tokens", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_autorizacion_tokens_tenant_id_codigo",
                table: "autorizacion_tokens",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_autorizacion_tokens_tenant_id_estado",
                table: "autorizacion_tokens",
                columns: new[] { "tenant_id", "estado" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "autorizacion_tokens");

            migrationBuilder.DropColumn(
                name: "autorizacion_fecha_autorizada",
                table: "historias_clinicas");

            migrationBuilder.DropColumn(
                name: "autorizacion_fecha_token_id",
                table: "historias_clinicas");
        }
    }
}
