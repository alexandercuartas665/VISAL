-- ============================================================================
-- SQL de PRODUCCION — Agregar campos obligatorios de Fecha/Hora/Minutos/AM-PM
-- a los 5 formatos EVOLUCION.
--
-- COMO EJECUTAR:
--   1. En el navegador ya logueado como SuperAdmin, ir a la Consola SQL.
--   2. Copiar todo el bloque de abajo (desde WITH hasta el punto y coma).
--   3. Pegar y ejecutar.
--   4. Debe reportar "UPDATE 5".
--      - Si sale "UPDATE 0" significa que ya se aplico antes (idempotente).
--      - Si sale un numero distinto de 5 o 0, NO seguir y avisar.
--
-- QUE HACE:
--   Dentro de la seccion "Notas de Evolucion" de los 5 formatos EVOLUCION
--   (PP-FO-200, PP-FO-85, PP-FO-85_F, PP-FO-85_O, PP-FO-85_R) inserta al
--   INICIO 5 nodos:
--     - Subheading "Fecha y hora de la Evolucion"
--     - evolucion_fecha   (date, required)
--     - evolucion_hora    (select 01..12, required)
--     - evolucion_minuto  (select 00/15/30/45, required)
--     - evolucion_ampm    (select AM/PM, required)
--   El textarea observaciones_cierre queda DEBAJO sin cambios.
--
-- NO REQUIERE reemplazar el tenant: el filtro por codigo + tipo=EVOLUCION es
-- unico entre tenants. En prod solo existe un tenant cliente, asi que las
-- 5 filas afectadas son las esperadas. Ejecutado en dev sin problemas.
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
  AND (f.schema_json::text NOT ILIKE '%"evolucion_fecha"%');


-- (Opcional) VERIFICACION post-UPDATE — pegar y ejecutar a continuacion.
-- Debe salir 6 hijos por cada uno de los 5 codigos.
--
-- SELECT f.codigo, jsonb_array_length(sec->'children') as hijos
-- FROM form_definitions f, jsonb_array_elements(f.schema_json->'children') sec
-- WHERE f.tipo='EVOLUCION' AND (sec->>'label') ~* 'nota.*evoluc'
-- ORDER BY f.codigo;
