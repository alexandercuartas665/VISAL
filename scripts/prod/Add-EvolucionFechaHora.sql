-- ============================================================================
-- SQL de PRODUCCION — Agregar campos obligatorios de Fecha/Hora/Minutos/AM-PM
-- a los 5 formatos EVOLUCION.
--
-- Como ejecutar:
--   1. Abrir la Consola SQL (menu SuperAdmin -> Consola SQL).
--   2. IMPORTANTE: reemplazar <TENANT_ID> por el GUID del tenant Visal RT.
--   3. Pegar y ejecutar TODO el bloque de abajo.
--   4. Debe reportar "UPDATE 5". Si sale "UPDATE 0", significa que ya se
--      habia ejecutado antes (es idempotente).
--
-- Que hace:
--   Dentro de la seccion "Notas de Evolucion" (o "Nota de Evolucion" en
--   PP-FO-200) inserta al INICIO 5 nodos:
--     - Subheading "Fecha y hora de la Evolucion"
--     - evolucion_fecha   (date, required)
--     - evolucion_hora    (select 01..12, required)
--     - evolucion_minuto  (select 00/15/30/45, required)
--     - evolucion_ampm    (select AM/PM, required)
--   El textarea observaciones_cierre queda DEBAJO sin cambios.
--
-- Idempotencia:
--   El WHERE excluye filas cuyo schema_json ya contenga el string
--   "evolucion_fecha". Ejecutarlo dos veces NO duplica los campos.
--
-- Verificacion tras ejecutar (opcional): correr en la misma Consola SQL
--   SELECT f.codigo, jsonb_array_length(sec->'children') as hijos
--   FROM form_definitions f, jsonb_array_elements(f.schema_json->'children') sec
--   WHERE f.tipo='EVOLUCION' AND (sec->>'label') ~* 'nota.*evoluc'
--     AND f.tenant_id='<TENANT_ID>'
--   ORDER BY f.codigo;
-- Debe salir 6 hijos por cada uno de los 5 codigos.
-- ============================================================================

WITH extra AS (
    SELECT '[
        {"id":"ef00base","type":"text","textStyle":"subheading","content":"Fecha y hora de la Evolución","widthColumns":12},
        {"id":"ef01date","type":"field","fieldType":"date","name":"evolucion_fecha","label":"Fecha de la Evolución","required":true,"widthColumns":3},
        {"id":"ef02hora","type":"field","fieldType":"select","name":"evolucion_hora","label":"Horas","required":true,"widthColumns":3,"allowCustom":false,"options":["01","02","03","04","05","06","07","08","09","10","11","12"]},
        {"id":"ef03mins","type":"field","fieldType":"select","name":"evolucion_minuto","label":"Minutos","required":true,"widthColumns":3,"allowCustom":false,"options":["00","15","30","45"]},
        {"id":"ef04ampm","type":"field","fieldType":"select","name":"evolucion_ampm","label":"AM/PM","required":true,"widthColumns":3,"allowCustom":false,"options":["AM","PM"]}
    ]'::jsonb AS arr
)
UPDATE form_definitions f
SET schema_json = jsonb_set(
    schema_json,
    '{children}',
    (
        SELECT jsonb_agg(
            CASE
                WHEN (sec->>'label') ~* 'nota.*evoluc'
                THEN jsonb_set(sec, '{children}', (SELECT arr FROM extra) || (sec->'children'))
                ELSE sec
            END
        )
        FROM jsonb_array_elements(f.schema_json->'children') sec
    )
),
updated_at = NOW()
WHERE f.tipo = 'EVOLUCION'
  AND f.codigo IN ('PP-FO-200','PP-FO-85','PP-FO-85_F','PP-FO-85_O','PP-FO-85_R')
  AND f.tenant_id = '<TENANT_ID>'
  AND (f.schema_json::text NOT ILIKE '%"evolucion_fecha"%');
