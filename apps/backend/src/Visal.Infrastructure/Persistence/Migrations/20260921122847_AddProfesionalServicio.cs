using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProfesionalServicio : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "profesional_servicios",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    profesional_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    nombre = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    catalogo_servicio_referencia_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profesional_servicios", x => x.id);
                    table.ForeignKey(
                        name: "fk_profesional_servicios_catalogos_servicio_referencia_catalog",
                        column: x => x.catalogo_servicio_referencia_id,
                        principalTable: "catalogos_servicio_referencia",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_profesional_servicios_profesionales_profesional_id",
                        column: x => x.profesional_id,
                        principalTable: "profesionales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_profesional_servicios_catalogo_servicio_referencia_id",
                table: "profesional_servicios",
                column: "catalogo_servicio_referencia_id");

            migrationBuilder.CreateIndex(
                name: "ix_profesional_servicios_profesional_id",
                table: "profesional_servicios",
                column: "profesional_id");

            migrationBuilder.CreateIndex(
                name: "ix_profesional_servicios_tenant_id_codigo",
                table: "profesional_servicios",
                columns: new[] { "tenant_id", "codigo" });

            migrationBuilder.CreateIndex(
                name: "ix_profesional_servicios_tenant_id_profesional_id_codigo",
                table: "profesional_servicios",
                columns: new[] { "tenant_id", "profesional_id", "codigo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "profesional_servicios");
        }
    }
}
