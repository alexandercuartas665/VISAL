using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReporteCatalogoGlobal : Migration
    {
        // GUIDs fijos de los 2 reportes semilla del catalogo (idempotencia / mapeo).
        private const string GuidPacientes = "019f0000-0000-7000-8000-000000000001";
        private const string GuidHcFormato = "019f0000-0000-7000-8000-000000000002";

        private const string SqlPacientes =
            "SELECT numero_documento AS \"Documento\", nombre_completo AS \"Nombre\", to_char(created_at, 'YYYY-MM-DD') AS \"Creado\" FROM pacientes WHERE tenant_id = {tenantId} AND (@_desde IS NULL OR created_at >= {desde}) AND (@_hasta IS NULL OR created_at <= {hasta}) ORDER BY created_at DESC LIMIT 200";

        private const string SqlHcFormato =
            "SELECT fd.codigo AS \"Codigo\", fd.nombre AS \"Nombre\", count(*) AS \"Total\" FROM historias_clinicas hc JOIN form_definitions fd ON fd.id=hc.form_definition_id WHERE hc.tenant_id={tenantId} AND (@_desde IS NULL OR hc.fecha_apertura >= {desde}) AND (@_hasta IS NULL OR hc.fecha_apertura <= {hasta}) GROUP BY fd.codigo, fd.nombre ORDER BY count(*) DESC";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var stamp = new DateTimeOffset(2026, 8, 7, 0, 0, 0, TimeSpan.Zero);

            // ---- 1) Nuevas tablas -------------------------------------------------
            migrationBuilder.CreateTable(
                name: "reporte_catalogos",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    categoria = table.Column<string>(type: "text", nullable: true),
                    query_sql = table.Column<string>(type: "text", nullable: false),
                    filtra_sede = table.Column<bool>(type: "boolean", nullable: false),
                    filtra_fechas = table.Column<bool>(type: "boolean", nullable: false),
                    habilitado = table.Column<bool>(type: "boolean", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reporte_catalogos", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reporte_tenant_activaciones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reporte_catalogo_id = table.Column<Guid>(type: "uuid", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reporte_tenant_activaciones", x => x.id);
                });

            // ---- 2) Semilla del catalogo (global, 2 reportes de arranque) ---------
            migrationBuilder.InsertData(
                table: "reporte_catalogos",
                columns: new[] { "id", "nombre", "descripcion", "categoria", "query_sql", "filtra_sede", "filtra_fechas", "habilitado", "orden", "created_at" },
                values: new object[] { Guid.Parse(GuidPacientes), "Pacientes admitidos", "Ultimos pacientes con fecha desde/hasta", "Pacientes", SqlPacientes, false, true, true, 1, stamp });

            migrationBuilder.InsertData(
                table: "reporte_catalogos",
                columns: new[] { "id", "nombre", "descripcion", "categoria", "query_sql", "filtra_sede", "filtra_fechas", "habilitado", "orden", "created_at" },
                values: new object[] { Guid.Parse(GuidHcFormato), "HCs por formato", "Total HCs por formato", "Historias clinicas", SqlHcFormato, false, true, true, 2, stamp });

            // ---- 3) Migrar los reportes viejos por-tenant a activaciones ----------
            // Cada (tenant, reporte) que existia en reporte_configs queda activado, para
            // no perder visibilidad. En prod (reporte_configs vacio) no inserta nada.
            migrationBuilder.Sql($@"
                INSERT INTO reporte_tenant_activaciones (id, tenant_id, reporte_catalogo_id, activo, created_at)
                SELECT gen_random_uuid(), rc.tenant_id,
                       CASE rc.nombre
                            WHEN 'Pacientes admitidos' THEN '{GuidPacientes}'::uuid
                            WHEN 'HCs por formato'     THEN '{GuidHcFormato}'::uuid
                       END,
                       true, now()
                  FROM (SELECT DISTINCT tenant_id, nombre FROM reporte_configs) rc
                 WHERE rc.nombre IN ('Pacientes admitidos', 'HCs por formato');");

            // ---- 4) Retirar el modelo viejo --------------------------------------
            migrationBuilder.DropForeignKey(
                name: "fk_reporte_usuarios_reporte_configs_reporte_config_id",
                table: "reporte_usuarios");

            migrationBuilder.DropIndex(
                name: "ix_reporte_usuarios_reporte_config_id",
                table: "reporte_usuarios");

            migrationBuilder.DropTable(
                name: "reporte_configs");

            // reporte_usuarios ahora referencia el catalogo (tabla estaba vacia).
            migrationBuilder.RenameColumn(
                name: "reporte_config_id",
                table: "reporte_usuarios",
                newName: "reporte_catalogo_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "reporte_catalogo_id",
                table: "reporte_usuarios",
                newName: "reporte_config_id");

            migrationBuilder.CreateTable(
                name: "reporte_configs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    descripcion = table.Column<string>(type: "text", nullable: true),
                    filtra_fechas = table.Column<bool>(type: "boolean", nullable: false),
                    filtra_sede = table.Column<bool>(type: "boolean", nullable: false),
                    habilitado = table.Column<bool>(type: "boolean", nullable: false),
                    nombre = table.Column<string>(type: "text", nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    query_sql = table.Column<string>(type: "text", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reporte_configs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reporte_usuarios_reporte_config_id",
                table: "reporte_usuarios",
                column: "reporte_config_id");

            migrationBuilder.AddForeignKey(
                name: "fk_reporte_usuarios_reporte_configs_reporte_config_id",
                table: "reporte_usuarios",
                column: "reporte_config_id",
                principalTable: "reporte_configs",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropTable(
                name: "reporte_catalogos");

            migrationBuilder.DropTable(
                name: "reporte_tenant_activaciones");
        }
    }
}
