using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Visal.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Deja el tablero "PQR" (creado por el webhook de formularios) listo para gestionar PQRS-F:
    /// (1) le asigna un dueno real si quedo con owner vacio (Guid.Empty) para que sea visible/editable,
    /// (2) renombra sus 4 columnas genericas al flujo PQRS (Radicado -> En gestion -> Respondido ->
    ///     Cerrado, ultima = final), solo si conservan los nombres por defecto, y
    /// (3) siembra los 24 campos dinamicos de la MATRIZ DE GESTION PQRS-F 2026 agrupados en 3
    ///     secciones (via separadores). Todo idempotente y acotado a tableros llamados 'PQR'; no toca
    ///     otros tableros ni pisa personalizaciones del tenant.
    /// </summary>
    public partial class SeedPqrTableroCamposPqrs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // NOTA: NO tocamos owner_platform_user_id del tablero PQR. El modulo Tableros resuelve
            // el actor desde el claim ClaimTypes.NameIdentifier, pero las paginas leen "user_id"/"sub"
            // (que no existen), por lo que el actor efectivo es Guid.Empty y el tablero se creo con ese
            // owner. Reasignar el owner a un usuario real lo volveria invisible en /tableros. Se deja
            // el owner tal como lo creo el webhook.

            // Columnas al flujo PQRS de 4 etapas. Solo renombra si conservan los nombres por
            //    defecto del webhook, para no pisar columnas ya personalizadas. Se mapea por sort_order.
            migrationBuilder.Sql(@"
UPDATE task_board_columns c
SET name = CASE c.sort_order
             WHEN 0 THEN 'Radicado'
             WHEN 1 THEN 'En gestion'
             WHEN 2 THEN 'Respondido'
             WHEN 3 THEN 'Cerrado'
             ELSE c.name END,
    is_done = (c.sort_order = 3)
FROM task_boards b
WHERE c.board_id = b.id
  AND upper(b.name) = 'PQR'
  AND b.is_archived = false
  AND c.sort_order BETWEEN 0 AND 3
  AND c.name IN ('Por hacer','En progreso','En revision','Completado');
");

            // 3) 24 campos + 3 separadores de seccion. Solo si el tablero PQR aun no tiene campos.
            migrationBuilder.Sql(@"
INSERT INTO task_field_definitions
    (id, board_id, field_key, label, field_type, show_in_filter, ""column"", sort_order,
     options, description, allow_multiple, multi_with_detail, total_source_keys, repeat_with_field_key,
     created_at, created_by, tenant_id)
SELECT gen_random_uuid(), b.id, v.field_key, v.label, v.field_type, v.show_in_filter, v.col, v.sort_order,
       v.options, NULL, false, false, NULL, NULL,
       now(), b.owner_platform_user_id, b.tenant_id
FROM task_boards b
CROSS JOIN (VALUES
    -- Seccion 1: Informacion basica del usuario
    ('sep_info_basica',            'Informacion basica del usuario',                        'Separator', false, 1,  0, NULL::text),
    ('n_consecutivo',              'N°',                                                    'Number',    false, 1,  1, NULL),
    ('n_radicado',                 'N° Radicado o folio',                                   'Text',      true,  1,  2, NULL),
    ('mes',                        'Mes',                                                   'Text',      true,  1,  3, NULL),
    ('fecha_radicacion',           'Fecha de radicacion en atencion del usuario',           'Date',      false, 1,  4, NULL),
    ('nombres_usuario',            'Nombres del usuario',                                   'Text',      false, 3,  5, NULL),
    ('identificacion',             'Identificacion',                                        'Text',      false, 1,  6, NULL),
    ('celular',                    'Celular',                                               'Phone',     false, 1,  7, NULL),
    ('email',                      'Email',                                                 'Email',     false, 1,  8, NULL),
    ('fecha_diligenciamiento',     'Fecha de diligenciamiento del formato',                 'Date',      false, 1,  9, NULL),
    ('entidad_salud',              'Entidad de salud',                                      'Text',      true,  1, 10, NULL),
    ('servicio_pqrs',              'Servicio al que corresponde la PQRS',                   'Text',      false, 3, 11, NULL),
    -- Seccion 2: Gestion de la PQRSF
    ('sep_gestion',                'Gestion de la PQRSF',                                   'Separator', false, 1, 12, NULL),
    ('tipo_pqrs_f',                'Tipo PQRS-F',                                           'Select',    true,  1, 13, E'TUTELA\nPQRS-F'),
    ('medio_llegada',              'Medio por donde llega la PQRS',                         'Text',      false, 1, 14, NULL),
    ('descripcion_pqrsf',          'Descripcion de la PQRSF',                               'TextArea',  false, 3, 15, NULL),
    ('atributo_calidad',           'Atributo de calidad',                                   'Select',    true,  1, 16, E'ACCESIBILIDAD\nOPORTUNIDAD\nCONTINUIDAD\nSATISFACCION DEL USUARIO\nPERTINENCIA\nSEGURIDAD'),
    ('fecha_envio_lider',          'Fecha de envio a lider de area pertinente',             'Date',      false, 1, 17, NULL),
    ('fecha_respuesta_lider',      'Fecha de respuesta por el lider del area a SAC',        'Date',      false, 1, 18, NULL),
    ('descripcion_respuesta_area', 'Descripcion respuesta del area / plan de accion',       'TextArea',  false, 3, 19, NULL),
    ('plan_de_mejora',             'Plan de mejora',                                        'TextArea',  false, 3, 20, NULL),
    ('fecha_respuesta_usuario',    'Fecha de respuesta al usuario por SAC',                 'Date',      false, 1, 21, NULL),
    -- Seccion 3: Indicadores de oportunidad
    ('sep_indicadores',            'Indicadores de oportunidad',                            'Separator', false, 1, 22, NULL),
    ('oport_identificacion',       'Oportunidad de identificacion de PQRSF por el SAC (dias)',           'Number', false, 1, 23, NULL),
    ('oport_respuesta_lider',      'Oportunidad de respuesta de PQRSF al SAC por el lider de area (dias)','Number', false, 1, 24, NULL),
    ('oport_respuesta_usuario',    'Oportunidad de respuesta de PQRSF al usuario (dias)',                'Number', false, 1, 25, NULL),
    ('estado_solicitud',           'Estado de la solicitud',                                'Select',    true,  1, 26, E'ABIERTO\nCERRADO')
) AS v(field_key, label, field_type, show_in_filter, col, sort_order, options)
WHERE upper(b.name) = 'PQR'
  AND b.is_archived = false
  AND NOT EXISTS (SELECT 1 FROM task_field_definitions f WHERE f.board_id = b.id);
");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Borra los campos sembrados de los tableros PQR (por su field_key conocido).
            migrationBuilder.Sql(@"
DELETE FROM task_field_definitions f
USING task_boards b
WHERE f.board_id = b.id
  AND upper(b.name) = 'PQR'
  AND f.field_key IN (
      'sep_info_basica','n_consecutivo','n_radicado','mes','fecha_radicacion','nombres_usuario',
      'identificacion','celular','email','fecha_diligenciamiento','entidad_salud','servicio_pqrs',
      'sep_gestion','tipo_pqrs_f','medio_llegada','descripcion_pqrsf','atributo_calidad',
      'fecha_envio_lider','fecha_respuesta_lider','descripcion_respuesta_area','plan_de_mejora',
      'fecha_respuesta_usuario','sep_indicadores','oport_identificacion','oport_respuesta_lider',
      'oport_respuesta_usuario','estado_solicitud');
");

            // Revierte los nombres de columna al set generico del webhook.
            migrationBuilder.Sql(@"
UPDATE task_board_columns c
SET name = CASE c.sort_order
             WHEN 0 THEN 'Por hacer'
             WHEN 1 THEN 'En progreso'
             WHEN 2 THEN 'En revision'
             WHEN 3 THEN 'Completado'
             ELSE c.name END
FROM task_boards b
WHERE c.board_id = b.id
  AND upper(b.name) = 'PQR'
  AND c.sort_order BETWEEN 0 AND 3
  AND c.name IN ('Radicado','En gestion','Respondido','Cerrado');
");
        }
    }
}
