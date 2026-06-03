using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RelacionFormulario : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "relaciones_formulario",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    formulario_origen_id = table.Column<Guid>(type: "uuid", nullable: false),
                    formulario_destino_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    observacion = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relaciones_formulario", x => x.id);
                    table.ForeignKey(
                        name: "fk_relaciones_formulario_form_definitions_formulario_destino_id",
                        column: x => x.formulario_destino_id,
                        principalTable: "form_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_relaciones_formulario_form_definitions_formulario_origen_id",
                        column: x => x.formulario_origen_id,
                        principalTable: "form_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_relaciones_formulario_formulario_destino_id",
                table: "relaciones_formulario",
                column: "formulario_destino_id");

            migrationBuilder.CreateIndex(
                name: "ix_relaciones_formulario_formulario_origen_id",
                table: "relaciones_formulario",
                column: "formulario_origen_id");

            migrationBuilder.CreateIndex(
                name: "ix_relaciones_formulario_tenant_id_formulario_origen_id_formul",
                table: "relaciones_formulario",
                columns: new[] { "tenant_id", "formulario_origen_id", "formulario_destino_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "relaciones_formulario");
        }
    }
}
