using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlantillasAgenda : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plantillas_agenda",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    descripcion = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    activa = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plantillas_agenda", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plantilla_agenda_turnos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    plantilla_agenda_id = table.Column<Guid>(type: "uuid", nullable: false),
                    dia_semana = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    hora_inicio = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    hora_fin = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    intervalo_minutos = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plantilla_agenda_turnos", x => x.id);
                    table.ForeignKey(
                        name: "fk_plantilla_agenda_turnos_plantillas_agenda_plantilla_agenda_",
                        column: x => x.plantilla_agenda_id,
                        principalTable: "plantillas_agenda",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_plantilla_agenda_turnos_plantilla_agenda_id",
                table: "plantilla_agenda_turnos",
                column: "plantilla_agenda_id");

            migrationBuilder.CreateIndex(
                name: "ix_plantilla_agenda_turnos_tenant_id_plantilla_agenda_id_dia_s",
                table: "plantilla_agenda_turnos",
                columns: new[] { "tenant_id", "plantilla_agenda_id", "dia_semana" });

            migrationBuilder.CreateIndex(
                name: "ix_plantillas_agenda_tenant_id_activa",
                table: "plantillas_agenda",
                columns: new[] { "tenant_id", "activa" });

            migrationBuilder.CreateIndex(
                name: "ix_plantillas_agenda_tenant_id_nombre",
                table: "plantillas_agenda",
                columns: new[] { "tenant_id", "nombre" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plantilla_agenda_turnos");

            migrationBuilder.DropTable(
                name: "plantillas_agenda");
        }
    }
}
