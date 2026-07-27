using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAtencionOrdenSecuencial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "completado",
                table: "asignacion_turno_sesiones",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "asignacion_turno_sesion_hcs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sesion_id = table.Column<Guid>(type: "uuid", nullable: false),
                    historia_clinica_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_asignacion_turno_sesion_hcs", x => x.id);
                    table.ForeignKey(
                        name: "fk_asignacion_turno_sesion_hcs_asignacion_turno_sesiones_sesio",
                        column: x => x.sesion_id,
                        principalTable: "asignacion_turno_sesiones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_asignacion_turno_sesion_hcs_historias_clinicas_historia_cli",
                        column: x => x.historia_clinica_id,
                        principalTable: "historias_clinicas",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_turno_sesiones_tenant_id_asignacion_turno_id_com",
                table: "asignacion_turno_sesiones",
                columns: new[] { "tenant_id", "asignacion_turno_id", "completado" });

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_turno_sesion_hcs_historia_clinica_id",
                table: "asignacion_turno_sesion_hcs",
                column: "historia_clinica_id");

            migrationBuilder.CreateIndex(
                name: "ix_asignacion_turno_sesion_hcs_sesion_id_historia_clinica_id",
                table: "asignacion_turno_sesion_hcs",
                columns: new[] { "sesion_id", "historia_clinica_id" },
                unique: true);

            // Backfill idempotente: por cada HC Cerrada intenta ligarla a una sesion
            // del mismo paciente + profesional + mismo dia calendario (America/Bogota).
            // Nota realista: en la data actual de VISAL las HCs historicas se abrieron
            // sin pasar por la coordinacion de turnos, asi que la heuristica va a
            // matchear casi cero. El backfill igual queda para casos futuros y como
            // recurso idempotente (ON CONFLICT DO NOTHING). RAISE NOTICE reporta el
            // conteo para saber si hubo cobertura significativa.
            migrationBuilder.Sql(@"
DO $mig$
DECLARE
    _pivotes_creados int;
    _sesiones_marcadas int;
BEGIN
    WITH nuevos AS (
        INSERT INTO asignacion_turno_sesion_hcs (id, sesion_id, historia_clinica_id, tenant_id, created_at)
        SELECT gen_random_uuid(),
               ses.id,
               hc.id,
               hc.tenant_id,
               now()
        FROM historias_clinicas hc
        JOIN asignaciones asig
          ON asig.tenant_id = hc.tenant_id AND asig.paciente_id = hc.paciente_id
        JOIN asignacion_turnos turno
          ON turno.asignacion_id = asig.id
         AND turno.tenant_id = hc.tenant_id
         AND turno.profesional_id = hc.profesional_id
        JOIN asignacion_turno_sesiones ses
          ON ses.asignacion_turno_id = turno.id
         AND ses.tenant_id = hc.tenant_id
         AND ses.fecha_atencion = (hc.fecha_apertura AT TIME ZONE 'America/Bogota')::date
        WHERE hc.estado = 1
        ON CONFLICT (sesion_id, historia_clinica_id) DO NOTHING
        RETURNING 1
    )
    SELECT COUNT(*) INTO _pivotes_creados FROM nuevos;

    UPDATE asignacion_turno_sesiones ses
       SET completado = true
     WHERE EXISTS (
        SELECT 1
          FROM asignacion_turno_sesion_hcs piv
          JOIN historias_clinicas hc ON hc.id = piv.historia_clinica_id
         WHERE piv.sesion_id = ses.id AND hc.estado = 1)
       AND ses.completado = false;
    GET DIAGNOSTICS _sesiones_marcadas = ROW_COUNT;

    RAISE NOTICE 'Backfill AtencionOrden: % pivotes creados, % sesiones marcadas Completado=true',
        _pivotes_creados, _sesiones_marcadas;
END;
$mig$;
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "asignacion_turno_sesion_hcs");

            migrationBuilder.DropIndex(
                name: "ix_asignacion_turno_sesiones_tenant_id_asignacion_turno_id_com",
                table: "asignacion_turno_sesiones");

            migrationBuilder.DropColumn(
                name: "completado",
                table: "asignacion_turno_sesiones");
        }
    }
}
