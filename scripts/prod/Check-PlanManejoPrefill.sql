-- ============================================================================
-- SQL de CONSULTA (Consola SQL de PRODUCCION) — SOLO LECTURA
--
-- Lista todas las HISTORIA CLINICA que TIENEN al menos un textarea con
-- name tipo plan/tratamiento/manejo/terapeutico/conducta, y marca:
--   ya_tiene_prefill = true  ==> ya esta enlazado con todo.lista_completa
--   ya_tiene_prefill = false ==> FALTA enlazar
--
-- La columna 'target_faltante' te dice el name exacto del campo al que
-- deberias apuntar el prefill.
--
-- El source que buscamos es literalmente 'todo.lista_completa' en cualquier
-- ruta del prefill_routes_json.
-- ============================================================================

WITH plan_fields AS (
    SELECT
        f.id,
        f.codigo,
        f.nombre,
        c->>'name' AS target,
        sec->>'label' AS seccion,
        c->>'label' AS field_label,
        f.prefill_routes_json
    FROM form_definitions f
    CROSS JOIN LATERAL jsonb_array_elements(f.schema_json->'children') sec
    CROSS JOIN LATERAL jsonb_array_elements(sec->'children') c
    WHERE f.tipo = 'HISTORIA CLINICA'
      AND c->>'fieldType' = 'textarea'
      AND (
        LOWER(c->>'name') SIMILAR TO '%(plan|tratamiento|manejo|conducta|terapeutic|intervenc)%'
        OR LOWER(c->>'label') SIMILAR TO '%(plan|tratamiento|manejo|conducta|terapeutic|intervenc)%'
      )
),
mapped AS (
    SELECT
        p.codigo,
        p.nombre,
        p.seccion,
        p.target,
        p.field_label,
        EXISTS (
            SELECT 1
            FROM jsonb_array_elements(COALESCE(p.prefill_routes_json->'routes', '[]'::jsonb)) r,
                 jsonb_array_elements(COALESCE(r->'mappings', '[]'::jsonb)) m
            WHERE m->>'source' = 'todo.lista_completa'
              AND m->>'target' = p.target
        ) AS ya_tiene_prefill
    FROM plan_fields p
)
SELECT
    codigo,
    nombre,
    seccion,
    target AS target_faltante,
    field_label,
    ya_tiene_prefill
FROM mapped
ORDER BY ya_tiene_prefill ASC, codigo, target;

-- Uso: ya_tiene_prefill = false => son los que TIENES QUE ENLAZAR a mano.
--      ya_tiene_prefill = true  => estan OK, no toques.
