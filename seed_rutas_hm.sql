WITH hc AS (
    SELECT id, codigo, schema_json,
           COALESCE(prefill_routes_json::jsonb, '{"routes":[]}'::jsonb) AS routes
    FROM form_definitions
    WHERE codigo LIKE 'HC-FO%' AND activo = true
),
campos AS (
    SELECT h.id, h.codigo,
           jsonb_path_query(h.schema_json::jsonb, 'strict $.**.children[*]?(@.type=="field")') AS fld
    FROM hc h
),
hm_match AS (
    SELECT id, codigo,
           fld->>'name' AS field_name,
           CASE
             -- Medicamentos: solo nombres que claramente son receta/orden de medicamentos.
             -- Evitamos "plan" (ambiguo: plan de manejo clinico) y "tratamiento" (libre).
             WHEN LOWER(fld->>'name') ~ 'medicament|formulamed|^receta$|^recetario$'
                  THEN 'medicamentos.lista_numerada'
             WHEN LOWER(fld->>'name') ~ 'remision|interconsulta'
                  THEN 'remisiones.lista_numerada'
             WHEN LOWER(fld->>'name') ~ 'incapacid'
                  THEN 'incapacidades.lista_numerada'
             WHEN LOWER(fld->>'name') ~ 'certificac|certificado'
                  THEN 'certificaciones.lista_numerada'
             -- Ordenes de servicio: laboratorios, insumos, servicios solicitados.
             -- Excluimos "examen" suelto porque suele ser "examen_fisico" del clinico.
             WHEN LOWER(fld->>'name') ~ 'laboratorio|^insumos?$|^servicios$|ordenes_propias|servicios_propios|examenes_solicit|examenes_lab|ayudas_diag'
                  THEN 'ordenes_servicio.lista_numerada'
             ELSE NULL
           END AS source
    FROM campos
),
hm_mappings AS (
    SELECT id, codigo,
           jsonb_agg(jsonb_build_object('source', source, 'target', field_name)) AS mappings
    FROM hm_match
    WHERE source IS NOT NULL AND field_name IS NOT NULL
    GROUP BY id, codigo
),
nueva_ruta AS (
    SELECT m.id, m.codigo,
           jsonb_build_object(
               'id', substr(md5(m.id::text), 1, 8),
               'name', 'Historia Medica',
               'sourceModule', 'historiaMedica',
               'mappings', m.mappings
           ) AS route
    FROM hm_mappings m
),
existing_filtered AS (
    SELECT h.id,
           COALESCE(
               (SELECT jsonb_agg(r)
                FROM jsonb_array_elements(h.routes->'routes') r
                WHERE r->>'sourceModule' IS DISTINCT FROM 'historiaMedica'),
               '[]'::jsonb
           ) AS routes_sin_hm
    FROM hc h
)
UPDATE form_definitions f
SET prefill_routes_json = jsonb_build_object(
    'routes', ef.routes_sin_hm || jsonb_build_array(nr.route)
)
FROM existing_filtered ef, nueva_ruta nr
WHERE f.id = ef.id AND f.id = nr.id;

SELECT f.codigo,
       jsonb_array_length(
           COALESCE(
               (SELECT r->'mappings'
                FROM jsonb_array_elements(f.prefill_routes_json::jsonb -> 'routes') r
                WHERE r->>'sourceModule' = 'historiaMedica'),
               '[]'::jsonb)
       ) AS mappings_hm
FROM form_definitions f
WHERE f.codigo LIKE 'HC-FO%' AND f.activo = true
ORDER BY f.codigo;
