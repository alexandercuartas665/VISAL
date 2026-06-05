-- =============================================================================
-- scripts/consentimientos-auto-paciente-firmas.sql
--
-- Para los 5 formularios tipo CONSENTIMIENTO agrega:
--   1. Seccion "Datos del Paciente (auto)" al INICIO con nombre/doc/edad/fecha.
--   2. Seccion "Firmas (auto)" al FINAL con firma_paciente_consent y
--      firma_profesional_consent (lleva URL del PNG).
--   3. Setea prefill_routes_json con las 3 rutas (paciente, firmaPaciente,
--      firmaProfesional) y sus mappings.
--
-- Idempotente: si las secciones auto ya existen (por su id), se reemplazan
-- (es decir, primero se quitan y luego se vuelven a agregar). Esto permite
-- re-ejecutar el script si ajustamos los campos.
-- =============================================================================

BEGIN;

-- Paso 1: limpiar las secciones auto previas (si las hay) en los 5 forms.
UPDATE form_definitions f
SET schema_json = jsonb_set(
    schema_json,
    '{children}',
    coalesce(
        (SELECT jsonb_agg(c) FROM jsonb_array_elements(schema_json->'children') c
         WHERE coalesce(c->>'id', '') NOT IN ('auto-datos-paciente', 'auto-firmas')),
        '[]'::jsonb
    )
)
WHERE upper(nombre) LIKE '%CONSENT%';

-- Paso 2: agregar la seccion de datos al inicio + la de firmas al final.
UPDATE form_definitions f
SET schema_json = jsonb_set(
    schema_json,
    '{children}',
    jsonb_build_array(
        jsonb_build_object(
            'id', 'auto-datos-paciente',
            'type', 'section',
            'label', 'Datos del Paciente (auto-llenado)',
            'children', jsonb_build_array(
                jsonb_build_object('id','auto-nom','type','field','fieldType','text','name','nombre_paciente_consent','label','Nombre completo','readonly',true,'span',12),
                jsonb_build_object('id','auto-td', 'type','field','fieldType','text','name','tipo_documento_consent', 'label','Tipo doc',         'readonly',true,'span',3),
                jsonb_build_object('id','auto-nd', 'type','field','fieldType','text','name','numero_documento_consent','label','Numero documento','readonly',true,'span',5),
                jsonb_build_object('id','auto-ed', 'type','field','fieldType','number','name','edad_consent',           'label','Edad',            'readonly',true,'span',2),
                jsonb_build_object('id','auto-fa', 'type','field','fieldType','date','name','fecha_atencion_consent', 'label','Fecha atencion',  'readonly',true,'span',2)
            )
        )
    )
    || coalesce(schema_json->'children', '[]'::jsonb)
    || jsonb_build_array(
        jsonb_build_object(
            'id', 'auto-firmas',
            'type', 'section',
            'label', 'Firmas (auto-llenadas)',
            'children', jsonb_build_array(
                jsonb_build_object('id','auto-fp','type','field','fieldType','text','name','firma_paciente_consent',   'label','Firma del paciente (URL)','readonly',true,'span',6),
                jsonb_build_object('id','auto-fr','type','field','fieldType','text','name','firma_profesional_consent','label','Firma del profesional (URL)','readonly',true,'span',6)
            )
        )
    )
)
WHERE upper(nombre) LIKE '%CONSENT%';

-- Paso 3: configurar prefill_routes_json con las 3 rutas y sus mappings.
UPDATE form_definitions f
SET prefill_routes_json = jsonb_build_object(
    'routes', jsonb_build_array(
        jsonb_build_object(
            'id', 'auto-rpac',
            'name', 'Paciente',
            'sourceModule', 'paciente',
            'mappings', jsonb_build_array(
                jsonb_build_object('source','nombreCompleto', 'target','nombre_paciente_consent'),
                jsonb_build_object('source','tipoDocumento',  'target','tipo_documento_consent'),
                jsonb_build_object('source','numeroDocumento','target','numero_documento_consent'),
                jsonb_build_object('source','edad',           'target','edad_consent')
            )
        ),
        jsonb_build_object(
            'id', 'auto-rfp',
            'name', 'Firma del Paciente',
            'sourceModule', 'firmaPaciente',
            'mappings', jsonb_build_array(
                jsonb_build_object('source','url','target','firma_paciente_consent')
            )
        ),
        jsonb_build_object(
            'id', 'auto-rfr',
            'name', 'Firma del Profesional',
            'sourceModule', 'firmaProfesional',
            'mappings', jsonb_build_array(
                jsonb_build_object('source','url','target','firma_profesional_consent')
            )
        )
    )
)
WHERE upper(nombre) LIKE '%CONSENT%';

-- Verificacion: imprimir lo que quedo
SELECT codigo, nombre,
       jsonb_array_length(schema_json->'children') AS secciones,
       jsonb_array_length(prefill_routes_json->'routes') AS rutas
FROM form_definitions
WHERE upper(nombre) LIKE '%CONSENT%'
ORDER BY codigo;

COMMIT;
