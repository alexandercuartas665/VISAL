using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRdaReintentoYTraza : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "reintento_activo",
                table: "interoperabilidad_configs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "reintento_intervalo_min",
                table: "interoperabilidad_configs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "reintento_max_intentos",
                table: "interoperabilidad_configs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "rda_evento_intentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rda_evento_id = table.Column<Guid>(type: "uuid", nullable: false),
                    numero = table.Column<int>(type: "integer", nullable: false),
                    fecha = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    estado_resultado = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    http_status = table.Column<int>(type: "integer", nullable: true),
                    mensaje = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    automatico = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rda_evento_intentos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_rda_evento_intentos_tenant_id_rda_evento_id_fecha",
                table: "rda_evento_intentos",
                columns: new[] { "tenant_id", "rda_evento_id", "fecha" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "rda_evento_intentos");

            migrationBuilder.DropColumn(
                name: "reintento_activo",
                table: "interoperabilidad_configs");

            migrationBuilder.DropColumn(
                name: "reintento_intervalo_min",
                table: "interoperabilidad_configs");

            migrationBuilder.DropColumn(
                name: "reintento_max_intentos",
                table: "interoperabilidad_configs");
        }
    }
}
