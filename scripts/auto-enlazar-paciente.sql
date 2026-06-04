-- Auto-enlazar paciente: heuristica masiva replicada de FormDefinitionService.cs.
-- Para cada FormDefinition del tenant Agencia Demo, recorre los nodos hoja del
-- SchemaJson (excluyendo secciones, bloques de texto y tablas repetibles),
-- infiere el campo paciente correspondiente y agrega los mapeos a la ruta
-- "paciente" del PrefillRoutesJson. NO sobrescribe mapeos manuales existentes.

BEGIN;

-- Funcion auxiliar: normaliza un string para matching (minusculas + sin tildes
-- + sin separadores). Se usa para emular la heuristica C# en SQL puro.
CREATE OR REPLACE FUNCTION pg_temp.norm(s text) RETURNS text AS $f$
BEGIN
    IF s IS NULL THEN RETURN ''; END IF;
    RETURN lower(regexp_replace(
        translate(s, 'aeiouAEIOUnN', 'aeiouAEIOUnN'),  -- las tildes ya se removieron con translate abajo
        '[_\- .:]+', '', 'g'
    ));
END;
$f$ LANGUAGE plpgsql IMMUTABLE;

-- Funcion principal: infiere campo paciente desde (name, label).
CREATE OR REPLACE FUNCTION pg_temp.inferir_campo_paciente(p_name text, p_label text)
RETURNS text AS $f$
DECLARE
    n text;
    l text;
BEGIN
    -- Normalizar: minusculas + sin tildes + sin separadores ni puntos.
    n := lower(regexp_replace(
        translate(coalesce(p_name,''), 'áéíóúüÁÉÍÓÚÜñÑ', 'aeiouuAEIOUUnN'),
        '[_\- .:]+', '', 'g'));
    l := lower(regexp_replace(
        translate(coalesce(p_label,''), 'áéíóúüÁÉÍÓÚÜñÑ', 'aeiouuAEIOUUnN'),
        '[_\- .:]+', '', 'g'));

    IF n IN ('cc','ti','ce','rc','pa','pep','ms') THEN RETURN 'numeroDocumento'; END IF;
    IF n IN ('tipodoc','tipid','tipoid','tipdocide','tipoide') THEN RETURN 'tipoDocumento'; END IF;

    IF n = 'numerodocumento' THEN RETURN 'numeroDocumento'; END IF;
    IF n = 'tipodocumento' THEN RETURN 'tipoDocumento'; END IF;
    IF n = 'nombrecompleto' THEN RETURN 'nombreCompleto'; END IF;
    IF n = 'fechanacimiento' THEN RETURN 'fechaNacimiento'; END IF;
    IF n = 'sexo' THEN RETURN 'sexo'; END IF;
    IF n = 'estadocivil' THEN RETURN 'estadoCivil'; END IF;
    IF n = 'telefono' THEN RETURN 'telefono'; END IF;
    IF n = 'email' THEN RETURN 'email'; END IF;
    IF n = 'direccion' THEN RETURN 'direccion'; END IF;
    IF n = 'ciudad' THEN RETURN 'ciudad'; END IF;
    IF n = 'zona' THEN RETURN 'zona'; END IF;
    IF n = 'ocupacion' THEN RETURN 'ocupacion'; END IF;
    IF n = 'regimen' THEN RETURN 'regimen'; END IF;
    IF n = 'parentesco' THEN RETURN 'parentesco'; END IF;
    IF n = 'contactoemergencia' THEN RETURN 'contactoEmergencia'; END IF;
    IF n = 'telefonoemergencia' THEN RETURN 'telefonoEmergencia'; END IF;
    IF n = 'sede' THEN RETURN 'sede'; END IF;

    IF n LIKE '%fechanacimiento%' OR l LIKE '%fechanacimiento%' THEN RETURN 'fechaNacimiento'; END IF;
    IF n LIKE '%fechadenacimiento%' OR l LIKE '%fechadenacimiento%' THEN RETURN 'fechaNacimiento'; END IF;
    IF n LIKE '%fecnacimiento%' OR l LIKE '%fecnacimiento%' THEN RETURN 'fechaNacimiento'; END IF;
    IF n LIKE '%fechanac%' OR l LIKE '%fechanac%' THEN RETURN 'fechaNacimiento'; END IF;
    IF n LIKE '%fecnac%' OR l LIKE '%fecnac%' THEN RETURN 'fechaNacimiento'; END IF;
    IF n LIKE '%nacimiento%' OR l LIKE '%nacimiento%' THEN RETURN 'fechaNacimiento'; END IF;
    IF n LIKE '%estadocivil%' OR l LIKE '%estadocivil%' THEN RETURN 'estadoCivil'; END IF;
    IF n LIKE '%estciv%' OR l LIKE '%estciv%' THEN RETURN 'estadoCivil'; END IF;
    IF n LIKE '%edocivil%' OR l LIKE '%edocivil%' THEN RETURN 'estadoCivil'; END IF;

    IF n LIKE '%telefonoemergencia%' OR l LIKE '%telefonoemergencia%' THEN RETURN 'telefonoEmergencia'; END IF;
    IF n LIKE '%telacudiente%' OR l LIKE '%telacudiente%' THEN RETURN 'telefonoEmergencia'; END IF;
    IF n LIKE '%telefonoacudiente%' OR l LIKE '%telefonoacudiente%' THEN RETURN 'telefonoEmergencia'; END IF;
    IF n LIKE '%celularacudiente%' OR l LIKE '%celularacudiente%' THEN RETURN 'telefonoEmergencia'; END IF;
    IF n LIKE '%contactoemergencia%' OR l LIKE '%contactoemergencia%' THEN RETURN 'contactoEmergencia'; END IF;
    IF n LIKE '%acudiente%' OR l LIKE '%acudiente%' THEN RETURN 'contactoEmergencia'; END IF;
    IF n LIKE '%acompanante%' OR l LIKE '%acompanante%' THEN RETURN 'contactoEmergencia'; END IF;
    IF n LIKE '%responsable%' OR l LIKE '%responsable%' THEN RETURN 'contactoEmergencia'; END IF;
    IF n LIKE '%encasodeemergencia%' OR l LIKE '%encasodeemergencia%' THEN RETURN 'contactoEmergencia'; END IF;
    IF n LIKE '%parentesco%' OR l LIKE '%parentesco%' THEN RETURN 'parentesco'; END IF;

    IF n LIKE '%primernombre%' OR l LIKE '%primernombre%' THEN RETURN 'primerNombre'; END IF;
    IF n LIKE '%segundonombre%' OR l LIKE '%segundonombre%' THEN RETURN 'segundoNombre'; END IF;
    IF n LIKE '%primerapellido%' OR l LIKE '%primerapellido%' THEN RETURN 'primerApellido'; END IF;
    IF n LIKE '%segundoapellido%' OR l LIKE '%segundoapellido%' THEN RETURN 'segundoApellido'; END IF;
    IF n LIKE '%nombrecompleto%' OR l LIKE '%nombrecompleto%' THEN RETURN 'nombreCompleto'; END IF;
    IF n LIKE '%nombresyapellidos%' OR l LIKE '%nombresyapellidos%' THEN RETURN 'nombreCompleto'; END IF;
    IF n LIKE '%nombresapellidos%' OR l LIKE '%nombresapellidos%' THEN RETURN 'nombreCompleto'; END IF;
    IF n LIKE '%nomape%' OR l LIKE '%nomape%' THEN RETURN 'nombreCompleto'; END IF;
    IF n LIKE '%nompaciente%' OR l LIKE '%nompaciente%' THEN RETURN 'nombreCompleto'; END IF;
    IF n LIKE '%nombrepaciente%' OR l LIKE '%nombrepaciente%' THEN RETURN 'nombreCompleto'; END IF;
    IF n LIKE '%nombres%' OR l LIKE '%nombres%' THEN RETURN 'nombreCompleto'; END IF;
    IF n LIKE '%nombre%' OR l LIKE '%nombre%' THEN RETURN 'nombreCompleto'; END IF;
    IF n LIKE '%apellidos%' OR l LIKE '%apellidos%' THEN RETURN 'nombreCompleto'; END IF;
    IF n LIKE '%apellido%' OR l LIKE '%apellido%' THEN RETURN 'nombreCompleto'; END IF;

    IF n LIKE '%tipodocumento%' OR l LIKE '%tipodocumento%' THEN RETURN 'tipoDocumento'; END IF;
    IF n LIKE '%tipodeidentificacion%' OR l LIKE '%tipodeidentificacion%' THEN RETURN 'tipoDocumento'; END IF;
    IF n LIKE '%tipodocide%' OR l LIKE '%tipodocide%' THEN RETURN 'tipoDocumento'; END IF;
    IF n LIKE '%tipdocide%' OR l LIKE '%tipdocide%' THEN RETURN 'tipoDocumento'; END IF;
    IF n LIKE '%tipoid%' OR l LIKE '%tipoid%' THEN RETURN 'tipoDocumento'; END IF;
    IF n LIKE '%tipdoc%' OR l LIKE '%tipdoc%' THEN RETURN 'tipoDocumento'; END IF;
    IF n LIKE '%numerodeidentificacion%' OR l LIKE '%numerodeidentificacion%' THEN RETURN 'numeroDocumento'; END IF;
    IF n LIKE '%numerodocumento%' OR l LIKE '%numerodocumento%' THEN RETURN 'numeroDocumento'; END IF;
    IF n LIKE '%nrodocumento%' OR l LIKE '%nrodocumento%' THEN RETURN 'numeroDocumento'; END IF;
    IF n LIKE '%nrodoc%' OR l LIKE '%nrodoc%' THEN RETURN 'numeroDocumento'; END IF;
    IF n LIKE '%numdoc%' OR l LIKE '%numdoc%' THEN RETURN 'numeroDocumento'; END IF;
    IF n LIKE '%nodoc%' OR l LIKE '%nodoc%' THEN RETURN 'numeroDocumento'; END IF;
    IF n LIKE '%documentoidentidad%' OR l LIKE '%documentoidentidad%' THEN RETURN 'numeroDocumento'; END IF;
    IF n LIKE '%documento%' OR l LIKE '%documento%' THEN RETURN 'numeroDocumento'; END IF;
    IF n LIKE '%identificacion%' OR l LIKE '%identificacion%' THEN RETURN 'numeroDocumento'; END IF;
    IF n LIKE '%cedula%' OR l LIKE '%cedula%' THEN RETURN 'numeroDocumento'; END IF;

    IF n LIKE '%residencia%' OR l LIKE '%residencia%' THEN RETURN 'direccion'; END IF;
    IF n LIKE '%direccion%' OR l LIKE '%direccion%' THEN RETURN 'direccion'; END IF;
    IF n LIKE '%direccin%' OR l LIKE '%direccin%' THEN RETURN 'direccion'; END IF;
    IF n LIKE '%ciudad%' OR l LIKE '%ciudad%' THEN RETURN 'ciudad'; END IF;
    IF n LIKE '%municipio%' OR l LIKE '%municipio%' THEN RETURN 'ciudad'; END IF;
    IF n LIKE '%urbanorural%' OR l LIKE '%urbanorural%' THEN RETURN 'zona'; END IF;
    IF n LIKE '%zona%' OR l LIKE '%zona%' THEN RETURN 'zona'; END IF;
    IF n LIKE '%sede%' OR l LIKE '%sede%' THEN RETURN 'sede'; END IF;
    IF n LIKE '%sucursal%' OR l LIKE '%sucursal%' THEN RETURN 'sede'; END IF;

    IF n LIKE '%ocupacion%' OR l LIKE '%ocupacion%' THEN RETURN 'ocupacion'; END IF;
    IF n LIKE '%ocupacin%' OR l LIKE '%ocupacin%' THEN RETURN 'ocupacion'; END IF;
    IF n LIKE '%profesion%' OR l LIKE '%profesion%' THEN RETURN 'ocupacion'; END IF;
    IF n LIKE '%regimen%' OR l LIKE '%regimen%' THEN RETURN 'regimen'; END IF;

    IF n LIKE '%telefonofijo%' OR l LIKE '%telefonofijo%' THEN RETURN 'telefono'; END IF;
    IF n LIKE '%telcontacto%' OR l LIKE '%telcontacto%' THEN RETURN 'telefono'; END IF;
    IF n LIKE '%notelefono%' OR l LIKE '%notelefono%' THEN RETURN 'telefono'; END IF;
    IF n LIKE '%telefono%' OR l LIKE '%telefono%' THEN RETURN 'telefono'; END IF;
    IF n LIKE '%telfono%' OR l LIKE '%telfono%' THEN RETURN 'telefono'; END IF;
    IF n LIKE '%celular%' OR l LIKE '%celular%' THEN RETURN 'telefono'; END IF;
    IF n LIKE '%celpaciente%' OR l LIKE '%celpaciente%' THEN RETURN 'telefono'; END IF;
    IF n LIKE '%movil%' OR l LIKE '%movil%' THEN RETURN 'telefono'; END IF;
    IF n LIKE '%correoelectronico%' OR l LIKE '%correoelectronico%' THEN RETURN 'email'; END IF;
    IF n LIKE '%email%' OR l LIKE '%email%' THEN RETURN 'email'; END IF;
    IF n LIKE '%correo%' OR l LIKE '%correo%' THEN RETURN 'email'; END IF;

    IF n LIKE '%sexo%' OR l LIKE '%sexo%' THEN RETURN 'sexo'; END IF;
    IF n LIKE '%genero%' OR l LIKE '%genero%' THEN RETURN 'sexo'; END IF;

    RETURN NULL;
END;
$f$ LANGUAGE plpgsql IMMUTABLE;

-- Funcion recursiva: extrae pares (name, label) de los nodos hoja del schema.
CREATE OR REPLACE FUNCTION pg_temp.extraer_nodos(p_children jsonb)
RETURNS TABLE(name text, label text) AS $f$
DECLARE
    nodo jsonb;
    tipo text;
    ft text;
BEGIN
    IF p_children IS NULL OR jsonb_typeof(p_children) <> 'array' THEN RETURN; END IF;
    FOR nodo IN SELECT * FROM jsonb_array_elements(p_children) LOOP
        tipo := COALESCE(nodo->>'type','field');
        IF tipo = 'section' THEN
            RETURN QUERY SELECT * FROM pg_temp.extraer_nodos(nodo->'children');
            CONTINUE;
        END IF;
        IF tipo = 'text' THEN CONTINUE; END IF;
        ft := nodo->>'fieldType';
        IF ft = 'table' THEN CONTINUE; END IF;
        IF (nodo->>'name') IS NULL OR (nodo->>'name') = '' THEN CONTINUE; END IF;
        name := nodo->>'name';
        label := nodo->>'label';
        RETURN NEXT;
    END LOOP;
END;
$f$ LANGUAGE plpgsql IMMUTABLE;

-- Por cada formulario del tenant: construir la ruta paciente con todos los mapeos
-- inferidos (descartando los nulls y los duplicados de target). Preserva mapeos
-- manuales existentes: si el target ya esta en la ruta paciente, lo respeta.
WITH formularios AS (
    SELECT
        fd.id,
        fd.codigo,
        fd.prefill_routes_json,
        -- Lista de mapeos inferidos (source-target) para los Names del schema.
        COALESCE((
            SELECT jsonb_agg(DISTINCT jsonb_build_object('source', src, 'target', tgt))
            FROM (
                SELECT
                    pg_temp.inferir_campo_paciente(n.name, n.label) AS src,
                    n.name AS tgt
                FROM pg_temp.extraer_nodos(fd.schema_json->'children') n
            ) sub
            WHERE src IS NOT NULL
        ), '[]'::jsonb) AS mapeos_inferidos
    FROM form_definitions fd
    WHERE fd.tenant_id = '019e6b0a-a4d8-70d6-a343-d307ebd24b15'
),
con_ruta_paciente AS (
    SELECT
        f.id,
        f.codigo,
        f.mapeos_inferidos,
        -- Indice de la ruta paciente existente (NULL si no existe).
        (
            SELECT idx::int - 1
            FROM jsonb_array_elements(COALESCE(f.prefill_routes_json->'routes','[]'::jsonb)) WITH ORDINALITY AS r(elem, idx)
            WHERE lower(elem->>'sourceModule') = 'paciente'
            LIMIT 1
        ) AS idx_ruta_paciente,
        f.prefill_routes_json
    FROM formularios f
),
ruta_final AS (
    SELECT
        c.id,
        c.codigo,
        c.idx_ruta_paciente,
        c.prefill_routes_json,
        c.mapeos_inferidos,
        -- Mapeos existentes de la ruta paciente (si existia).
        COALESCE(
            (c.prefill_routes_json->'routes'->c.idx_ruta_paciente->'mappings'),
            '[]'::jsonb
        ) AS mapeos_existentes
    FROM con_ruta_paciente c
),
ruta_combinada AS (
    SELECT
        r.id,
        r.codigo,
        r.idx_ruta_paciente,
        r.prefill_routes_json,
        -- Mapeos finales: existentes + inferidos cuyo target NO este en existentes.
        (
            SELECT COALESCE(jsonb_agg(m), '[]'::jsonb)
            FROM (
                SELECT m FROM jsonb_array_elements(r.mapeos_existentes) m
                UNION ALL
                SELECT m FROM jsonb_array_elements(r.mapeos_inferidos) m
                WHERE NOT EXISTS (
                    SELECT 1 FROM jsonb_array_elements(r.mapeos_existentes) e
                    WHERE lower(e->>'target') = lower(m->>'target')
                )
            ) all_m
        ) AS mapeos_finales
    FROM ruta_final r
)
UPDATE form_definitions fd
SET prefill_routes_json = jsonb_build_object(
    'routes',
    CASE
        WHEN rc.idx_ruta_paciente IS NULL THEN
            -- No habia ruta paciente: agregarla al final.
            COALESCE(rc.prefill_routes_json->'routes','[]'::jsonb) ||
            jsonb_build_array(jsonb_build_object(
                'id', substr(md5(random()::text),1,8),
                'name','Paciente',
                'sourceModule','paciente',
                'mappings', rc.mapeos_finales
            ))
        ELSE
            -- Ya habia ruta paciente: reemplazar sus mappings con los combinados.
            jsonb_set(
                rc.prefill_routes_json->'routes',
                ('{'|| rc.idx_ruta_paciente ||',mappings}')::text[],
                rc.mapeos_finales,
                true
            )
    END
)
FROM ruta_combinada rc
WHERE fd.id = rc.id
  AND jsonb_array_length(rc.mapeos_finales) > 0;

-- Reporte final.
SELECT
    codigo,
    jsonb_array_length(prefill_routes_json->'routes'->0->'mappings') AS mapeos_paciente
FROM form_definitions
WHERE tenant_id = '019e6b0a-a4d8-70d6-a343-d307ebd24b15'
ORDER BY codigo;

COMMIT;
