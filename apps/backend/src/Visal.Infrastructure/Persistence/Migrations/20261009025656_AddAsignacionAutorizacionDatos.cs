using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAsignacionAutorizacionDatos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "asignacion_autorizacion_datos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    asignacion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    tipo_documento = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    documento = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    numero_autorizacion = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    telefonos_csv = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    correo = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    direccion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    raw_json = table.Column<string>(type: "text", nullable: true),
                    extraido_en = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asignacion_autorizacion_datos", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_autorizacion_datos_asignacion_id",
                table: "asignacion_autorizacion_datos",
                column: "asignacion_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_autorizacion_datos_tenant_id",
                table: "asignacion_autorizacion_datos",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asignacion_autorizacion_datos");
        }
    }
}
