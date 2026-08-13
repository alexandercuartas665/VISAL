using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeedReportesFiltroSede : Migration
    {
        // GUIDs fijos de los 2 reportes semilla (ver migracion ReporteCatalogoGlobal).
        private const string GuidPacientes = "019f0000-0000-7000-8000-000000000001";
        private const string GuidHcFormato = "019f0000-0000-7000-8000-000000000002";

        // --- SQL nuevo: agrega el filtro por Sede/Agencia (token {sedeId} -> @_sedeId).
        // Pacientes: sede_atencion_id directo. HCs: via el paciente (JOIN pacientes).
        // Cuando no se elige sede el parametro es NULL y el filtro se ignora.
        private const string SqlPacientesConSede =
            "SELECT numero_documento AS \"Documento\", nombre_completo AS \"Nombre\", to_char(created_at, 'YYYY-MM-DD') AS \"Creado\" FROM pacientes WHERE tenant_id = {tenantId} AND (@_desde IS NULL OR created_at >= {desde}) AND (@_hasta IS NULL OR created_at <= {hasta}) AND (@_sedeId IS NULL OR sede_atencion_id = {sedeId}) ORDER BY created_at DESC LIMIT 200";

        private const string SqlHcFormatoConSede =
            "SELECT fd.codigo AS \"Codigo\", fd.nombre AS \"Nombre\", count(*) AS \"Total\" FROM historias_clinicas hc JOIN form_definitions fd ON fd.id=hc.form_definition_id JOIN pacientes p ON p.id=hc.paciente_id WHERE hc.tenant_id={tenantId} AND (@_desde IS NULL OR hc.fecha_apertura >= {desde}) AND (@_hasta IS NULL OR hc.fecha_apertura <= {hasta}) AND (@_sedeId IS NULL OR p.sede_atencion_id = {sedeId}) GROUP BY fd.codigo, fd.nombre ORDER BY count(*) DESC";

        // --- SQL original (para el Down / rollback), sin filtro de sede.
        private const string SqlPacientesOriginal =
            "SELECT numero_documento AS \"Documento\", nombre_completo AS \"Nombre\", to_char(created_at, 'YYYY-MM-DD') AS \"Creado\" FROM pacientes WHERE tenant_id = {tenantId} AND (@_desde IS NULL OR created_at >= {desde}) AND (@_hasta IS NULL OR created_at <= {hasta}) ORDER BY created_at DESC LIMIT 200";

        private const string SqlHcFormatoOriginal =
            "SELECT fd.codigo AS \"Codigo\", fd.nombre AS \"Nombre\", count(*) AS \"Total\" FROM historias_clinicas hc JOIN form_definitions fd ON fd.id=hc.form_definition_id WHERE hc.tenant_id={tenantId} AND (@_desde IS NULL OR hc.fecha_apertura >= {desde}) AND (@_hasta IS NULL OR hc.fecha_apertura <= {hasta}) GROUP BY fd.codigo, fd.nombre ORDER BY count(*) DESC";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "reporte_catalogos",
                keyColumn: "id",
                keyValue: new Guid(GuidPacientes),
                columns: new[] { "query_sql", "filtra_sede" },
                values: new object[] { SqlPacientesConSede, true });

            migrationBuilder.UpdateData(
                table: "reporte_catalogos",
                keyColumn: "id",
                keyValue: new Guid(GuidHcFormato),
                columns: new[] { "query_sql", "filtra_sede" },
                values: new object[] { SqlHcFormatoConSede, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "reporte_catalogos",
                keyColumn: "id",
                keyValue: new Guid(GuidPacientes),
                columns: new[] { "query_sql", "filtra_sede" },
                values: new object[] { SqlPacientesOriginal, false });

            migrationBuilder.UpdateData(
                table: "reporte_catalogos",
                keyColumn: "id",
                keyValue: new Guid(GuidHcFormato),
                columns: new[] { "query_sql", "filtra_sede" },
                values: new object[] { SqlHcFormatoOriginal, false });
        }
    }
}
