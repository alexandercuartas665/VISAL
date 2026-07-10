using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogoTiposServicio : Migration
    {
        // Refactor de tipos de servicio a catalogo dinamico:
        //   1) crea catalogos_tipo_servicio + tenant_user_tipos_coordinados
        //   2) seed 5 tipos canonicos por tenant
        //   3) normaliza servicios_contrato.modulo y asignaciones.modulo/tipo_servicio
        //      a singular (CONSULTA/TERAPIA/EQUIPOS/ENFERMERIA/INSUMOS)
        //   4) migra CoordinaTerapias/Consultas/Enfermeria/Equipos -> filas N:N
        //   5) dropea las 4 columnas boolean
        // Orden importa: los datos se migran ANTES del drop, si no se pierden.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Crear tablas nuevas.
            migrationBuilder.CreateTable(
                name: "catalogos_tipo_servicio",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    nombre = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    orden = table.Column<int>(type: "integer", nullable: false),
                    activo = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_catalogos_tipo_servicio", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tenant_user_tipos_coordinados",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    codigo = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_user_tipos_coordinados", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_catalogos_tipo_servicio_tenant_id_activo_orden",
                table: "catalogos_tipo_servicio",
                columns: new[] { "tenant_id", "activo", "orden" });

            migrationBuilder.CreateIndex(
                name: "ix_catalogos_tipo_servicio_tenant_id_codigo",
                table: "catalogos_tipo_servicio",
                columns: new[] { "tenant_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_user_tipos_coordinados_tenant_id_tenant_user_id_codi",
                table: "tenant_user_tipos_coordinados",
                columns: new[] { "tenant_id", "tenant_user_id", "codigo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_user_tipos_coordinados_tenant_user_id",
                table: "tenant_user_tipos_coordinados",
                column: "tenant_user_id");

            // 2) Seed: 5 tipos canonicos por cada tenant.
            migrationBuilder.Sql(@"
INSERT INTO catalogos_tipo_servicio (id, tenant_id, codigo, nombre, orden, activo, created_at)
SELECT gen_random_uuid(), t.id, v.codigo, v.nombre, v.orden, TRUE, NOW()
FROM tenants t
CROSS JOIN (VALUES
    ('CONSULTA',   'Consulta',    1),
    ('TERAPIA',    'Terapia',     2),
    ('ENFERMERIA', 'Enfermeria',  3),
    ('EQUIPOS',    'Equipos',     4),
    ('INSUMOS',    'Insumos',     5)
) AS v(codigo, nombre, orden)
ON CONFLICT (tenant_id, codigo) DO NOTHING;
");

            // 3) Normalizar datos historicos a codigos singulares canonicos.
            migrationBuilder.Sql(@"
UPDATE servicios_contrato
SET modulo = CASE UPPER(TRIM(modulo))
    WHEN 'CONSULTAS' THEN 'CONSULTA'
    WHEN 'TERAPIAS'  THEN 'TERAPIA'
    WHEN 'EQUIPO'    THEN 'EQUIPOS'
    ELSE UPPER(TRIM(modulo))
END
WHERE modulo IS NOT NULL AND (
    UPPER(TRIM(modulo)) IN ('CONSULTAS','TERAPIAS','EQUIPO')
    OR modulo <> UPPER(TRIM(modulo))
);

UPDATE asignaciones
SET modulo = CASE UPPER(TRIM(modulo))
    WHEN 'CONSULTAS' THEN 'CONSULTA'
    WHEN 'TERAPIAS'  THEN 'TERAPIA'
    WHEN 'EQUIPO'    THEN 'EQUIPOS'
    ELSE UPPER(TRIM(modulo))
END
WHERE modulo IS NOT NULL AND (
    UPPER(TRIM(modulo)) IN ('CONSULTAS','TERAPIAS','EQUIPO')
    OR modulo <> UPPER(TRIM(modulo))
);

UPDATE asignaciones
SET tipo_servicio = CASE UPPER(TRIM(tipo_servicio))
    WHEN 'CONSULTAS' THEN 'CONSULTA'
    WHEN 'TERAPIAS'  THEN 'TERAPIA'
    WHEN 'EQUIPO'    THEN 'EQUIPOS'
    ELSE UPPER(TRIM(tipo_servicio))
END
WHERE tipo_servicio IS NOT NULL AND (
    UPPER(TRIM(tipo_servicio)) IN ('CONSULTAS','TERAPIAS','EQUIPO')
    OR tipo_servicio <> UPPER(TRIM(tipo_servicio))
);
");

            // 4) Migrar 4 booleans -> filas N:N (mientras las columnas aun existen).
            migrationBuilder.Sql(@"
INSERT INTO tenant_user_tipos_coordinados (id, tenant_id, tenant_user_id, codigo, created_at)
SELECT gen_random_uuid(), tu.tenant_id, tu.id, v.codigo, NOW()
FROM tenant_users tu
CROSS JOIN LATERAL (VALUES
    (tu.coordina_terapias,   'TERAPIA'),
    (tu.coordina_enfermeria, 'ENFERMERIA'),
    (tu.coordina_consultas,  'CONSULTA'),
    (tu.coordina_equipos,    'EQUIPOS')
) AS v(activo, codigo)
WHERE v.activo = TRUE
ON CONFLICT (tenant_id, tenant_user_id, codigo) DO NOTHING;
");

            // 5) Dropear columnas obsoletas.
            migrationBuilder.DropColumn(name: "coordina_consultas",  table: "tenant_users");
            migrationBuilder.DropColumn(name: "coordina_enfermeria", table: "tenant_users");
            migrationBuilder.DropColumn(name: "coordina_equipos",    table: "tenant_users");
            migrationBuilder.DropColumn(name: "coordina_terapias",   table: "tenant_users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "coordina_consultas",  table: "tenant_users",
                type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(
                name: "coordina_enfermeria", table: "tenant_users",
                type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(
                name: "coordina_equipos",    table: "tenant_users",
                type: "boolean", nullable: false, defaultValue: false);
            migrationBuilder.AddColumn<bool>(
                name: "coordina_terapias",   table: "tenant_users",
                type: "boolean", nullable: false, defaultValue: false);

            // Reconstruir los booleans desde la N:N antes de tirar las tablas.
            migrationBuilder.Sql(@"
UPDATE tenant_users SET
    coordina_terapias   = EXISTS(SELECT 1 FROM tenant_user_tipos_coordinados x WHERE x.tenant_user_id=tenant_users.id AND x.codigo='TERAPIA'),
    coordina_enfermeria = EXISTS(SELECT 1 FROM tenant_user_tipos_coordinados x WHERE x.tenant_user_id=tenant_users.id AND x.codigo='ENFERMERIA'),
    coordina_consultas  = EXISTS(SELECT 1 FROM tenant_user_tipos_coordinados x WHERE x.tenant_user_id=tenant_users.id AND x.codigo='CONSULTA'),
    coordina_equipos    = EXISTS(SELECT 1 FROM tenant_user_tipos_coordinados x WHERE x.tenant_user_id=tenant_users.id AND x.codigo='EQUIPOS');
");

            migrationBuilder.DropTable(name: "catalogos_tipo_servicio");
            migrationBuilder.DropTable(name: "tenant_user_tipos_coordinados");
        }
    }
}
