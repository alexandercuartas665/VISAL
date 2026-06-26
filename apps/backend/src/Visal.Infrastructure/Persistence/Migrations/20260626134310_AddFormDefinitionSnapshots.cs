using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFormDefinitionSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "form_definition_snapshots",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    form_definition_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    nombre = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    tipo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    schema_json = table.Column<string>(type: "jsonb", nullable: false),
                    prefill_routes_json = table.Column<string>(type: "jsonb", nullable: true),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    snapshot_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    snapshot_by = table.Column<Guid>(type: "uuid", nullable: true),
                    motivo = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_definition_snapshots", x => x.id);
                    table.ForeignKey(
                        name: "fk_form_definition_snapshots_form_definitions_form_definition_",
                        column: x => x.form_definition_id,
                        principalTable: "form_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_form_definition_snapshots_form_definition_id_snapshot_at",
                table: "form_definition_snapshots",
                columns: new[] { "form_definition_id", "snapshot_at" },
                descending: new[] { false, true });

            // ── Trigger BEFORE UPDATE en form_definitions ──
            // Atrapa cualquier UPDATE (UI, scripts PowerShell, SQL directo, futuras
            // migrations) y guarda el estado anterior como snapshot. Solo dispara si
            // algun campo "de contenido" cambio realmente — UPDATEs que solo tocan
            // updated_at/updated_by NO generan snapshot.
            //
            // Rotacion in-line: tras insertar el snapshot nuevo, borramos cualquier
            // fila de este form_definition_id que caiga fuera del top 20 mas
            // recientes por snapshot_at. Asi la tabla queda acotada sin job externo.
            //
            // BEFORE UPDATE garantiza atomicidad: si el UPDATE de form_definitions
            // falla por cualquier razon (check constraint, RLS, etc.), el INSERT del
            // snapshot tampoco se persiste (van en la misma transaccion implicita).
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION fn_snapshot_form_definition() RETURNS trigger AS $$
BEGIN
    IF (OLD.schema_json IS DISTINCT FROM NEW.schema_json
        OR OLD.codigo  IS DISTINCT FROM NEW.codigo
        OR OLD.nombre  IS DISTINCT FROM NEW.nombre
        OR OLD.version IS DISTINCT FROM NEW.version
        OR OLD.tipo    IS DISTINCT FROM NEW.tipo
        OR OLD.activo  IS DISTINCT FROM NEW.activo
        OR OLD.prefill_routes_json IS DISTINCT FROM NEW.prefill_routes_json) THEN
        INSERT INTO form_definition_snapshots
            (id, tenant_id, form_definition_id,
             codigo, nombre, version, tipo, schema_json, prefill_routes_json, activo,
             snapshot_at, snapshot_by, motivo,
             created_at, created_by, updated_at, updated_by)
        VALUES
            (gen_random_uuid(), OLD.tenant_id, OLD.id,
             OLD.codigo, OLD.nombre, OLD.version, OLD.tipo,
             OLD.schema_json, OLD.prefill_routes_json, OLD.activo,
             NOW(), NULL, 'auto-trigger',
             NOW(), NULL, NOW(), NULL);

        DELETE FROM form_definition_snapshots
        WHERE form_definition_id = OLD.id
          AND id NOT IN (
            SELECT id FROM form_definition_snapshots
            WHERE form_definition_id = OLD.id
            ORDER BY snapshot_at DESC
            LIMIT 20
          );
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

DROP TRIGGER IF EXISTS trg_form_definition_snapshot ON form_definitions;
CREATE TRIGGER trg_form_definition_snapshot
    BEFORE UPDATE ON form_definitions
    FOR EACH ROW EXECUTE FUNCTION fn_snapshot_form_definition();
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS trg_form_definition_snapshot ON form_definitions;
DROP FUNCTION IF EXISTS fn_snapshot_form_definition();
");
            migrationBuilder.DropTable(
                name: "form_definition_snapshots");
        }
    }
}
