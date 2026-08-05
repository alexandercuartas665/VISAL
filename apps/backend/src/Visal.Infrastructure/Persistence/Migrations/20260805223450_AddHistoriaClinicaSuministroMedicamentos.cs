using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoriaClinicaSuministroMedicamentos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "historia_clinica_suministro_medicamentos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    historia_clinica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fecha_hora = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    presentacion = table.Column<string>(type: "text", nullable: false),
                    dosis = table.Column<string>(type: "text", nullable: true),
                    cantidad = table.Column<string>(type: "text", nullable: true),
                    via = table.Column<string>(type: "text", nullable: true),
                    usuario_creacion_id = table.Column<Guid>(type: "uuid", nullable: true),
                    usuario_creacion_nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_historia_clinica_suministro_medicamentos", x => x.id);
                    table.ForeignKey(
                        name: "fk_historia_clinica_suministro_medicamentos_historias_clinicas",
                        column: x => x.historia_clinica_id,
                        principalTable: "historias_clinicas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_suministro_medicamentos_historia_clinica_id",
                table: "historia_clinica_suministro_medicamentos",
                column: "historia_clinica_id");

            migrationBuilder.CreateIndex(
                name: "ix_historia_clinica_suministro_medicamentos_tenant_id_historia",
                table: "historia_clinica_suministro_medicamentos",
                columns: new[] { "tenant_id", "historia_clinica_id", "fecha_hora" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "historia_clinica_suministro_medicamentos");
        }
    }
}
