using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Siembra en la galeria de Reportes el informe "Gestion PQRS-F": lee las tarjetas del
    /// tablero "PQR" y expone las 24 columnas de la MATRIZ DE GESTION PQRS-F (via los campos
    /// dinamicos guardados en task_cards.field_values_json). El catalogo es de plataforma
    /// (id fijo para idempotencia) y queda ACTIVADO para el tenant VISAL. Sin asignacion de
    /// usuarios, por lo que todos los usuarios del tenant pueden consultarlo. Filtra por tenant
    /// via el token {tenantId} (parametrizado por el runner). Idempotente.
    /// </summary>
    public partial class SeedReportePqrs : Migration
    {
        // Catalogo con id fijo (0e9b7a10-0000-4000-8000-000000000001) y tenant VISAL
        // (019e6b0a-a4d8-70d6-a343-d307ebd24b15); ambos van inline en el SQL de abajo.

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Catalogo del reporte (plataforma). query_sql en dollar-quoting para no escapar comillas.
            migrationBuilder.Sql("""
INSERT INTO reporte_catalogos (id, nombre, descripcion, categoria, query_sql, filtra_sede, filtra_fechas, habilitado, orden, created_at)
SELECT '0e9b7a10-0000-4000-8000-000000000001',
       'Gestion PQRS-F',
       'Matriz de gestion de PQRS-F: tarjetas del tablero PQR con los 24 campos del formato.',
       'PQRS',
       $q$
SELECT
  c.field_values_json->>'n_consecutivo'              AS "N°",
  c.field_values_json->>'n_radicado'                 AS "N° Radicado o folio",
  c.field_values_json->>'mes'                        AS "Mes",
  c.field_values_json->>'fecha_radicacion'           AS "Fecha de radicacion en atencion del usuario",
  c.field_values_json->>'nombres_usuario'            AS "Nombres del usuario",
  c.field_values_json->>'identificacion'             AS "Identificacion",
  c.field_values_json->>'celular'                    AS "Celular",
  c.field_values_json->>'email'                      AS "Email",
  c.field_values_json->>'fecha_diligenciamiento'     AS "Fecha de diligenciamiento del formato",
  c.field_values_json->>'entidad_salud'              AS "Entidad de salud",
  c.field_values_json->>'servicio_pqrs'              AS "Servicio al que corresponde la PQRS",
  c.field_values_json->>'tipo_pqrs_f'                AS "Tipo PQRS-F",
  c.field_values_json->>'medio_llegada'              AS "Medio por donde llega la PQRS",
  c.field_values_json->>'descripcion_pqrsf'          AS "Descripcion de la PQRSF",
  c.field_values_json->>'atributo_calidad'           AS "Atributo de calidad",
  c.field_values_json->>'fecha_envio_lider'          AS "Fecha de envio a lider de area pertinente",
  c.field_values_json->>'fecha_respuesta_lider'      AS "Fecha de respuesta del lider del area a SAC",
  c.field_values_json->>'descripcion_respuesta_area' AS "Descripcion respuesta del area / plan de accion",
  c.field_values_json->>'plan_de_mejora'             AS "Plan de mejora",
  c.field_values_json->>'fecha_respuesta_usuario'    AS "Fecha de respuesta al usuario por SAC",
  c.field_values_json->>'oport_identificacion'       AS "Oportunidad identificacion PQRSF por SAC (dias)",
  c.field_values_json->>'oport_respuesta_lider'      AS "Oportunidad respuesta al SAC por lider (dias)",
  c.field_values_json->>'oport_respuesta_usuario'    AS "Oportunidad respuesta al usuario (dias)",
  c.field_values_json->>'estado_solicitud'           AS "Estado de la solicitud",
  col.name                                           AS "Etapa (tablero)",
  c.title                                            AS "Tarjeta"
FROM task_cards c
JOIN task_boards b ON b.id = c.board_id
JOIN task_board_columns col ON col.id = c.column_id
WHERE b.tenant_id = {tenantId}
  AND upper(b.name) = 'PQR'
  AND NOT c.is_archived
  AND ({desde} IS NULL OR (
        c.field_values_json->>'fecha_radicacion' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}'
        AND (c.field_values_json->>'fecha_radicacion')::date >= {desde}::date))
  AND ({hasta} IS NULL OR (
        c.field_values_json->>'fecha_radicacion' ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}'
        AND (c.field_values_json->>'fecha_radicacion')::date <= {hasta}::date))
ORDER BY (CASE WHEN c.field_values_json->>'n_consecutivo' ~ '^[0-9]+$'
               THEN (c.field_values_json->>'n_consecutivo')::int ELSE 2147483647 END),
         c.created_at
$q$,
       false, true, true, 100, now()
WHERE NOT EXISTS (SELECT 1 FROM reporte_catalogos c WHERE c.id = '0e9b7a10-0000-4000-8000-000000000001');
""");

            // 2) Activacion para el tenant VISAL (todos los usuarios lo veran: sin asignacion).
            migrationBuilder.Sql("""
INSERT INTO reporte_tenant_activaciones (id, reporte_catalogo_id, activo, created_at, tenant_id)
SELECT gen_random_uuid(), '0e9b7a10-0000-4000-8000-000000000001', true, now(), '019e6b0a-a4d8-70d6-a343-d307ebd24b15'
WHERE NOT EXISTS (
  SELECT 1 FROM reporte_tenant_activaciones a
  WHERE a.reporte_catalogo_id = '0e9b7a10-0000-4000-8000-000000000001'
    AND a.tenant_id = '019e6b0a-a4d8-70d6-a343-d307ebd24b15');
""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM reporte_tenant_activaciones WHERE reporte_catalogo_id = '0e9b7a10-0000-4000-8000-000000000001';");
            migrationBuilder.Sql("DELETE FROM reporte_usuarios WHERE reporte_catalogo_id = '0e9b7a10-0000-4000-8000-000000000001';");
            migrationBuilder.Sql("DELETE FROM reporte_catalogos WHERE id = '0e9b7a10-0000-4000-8000-000000000001';");
        }
    }
}
