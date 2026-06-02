-- =====================================================================
--  seed-forms-ordenes.sql
--  Crea (si no existen) 5 plantillas base de "Ordenes" en el modulo
--  Formularios. Cada una arranca con un schema minimo (header + una
--  seccion con un titulo) para que el usuario pueda editarlas desde
--  el disenador /formularios. La regla del producto es "un formulario
--  activo por tipo", entonces estos 5 codigos son los canonicos:
--    ORDEN_MEDICAMENTOS / ORDEN_SERVICIOS / REMISIONES /
--    INCAPACIDADES / CERTIFICADOS
-- =====================================================================

DO $$
DECLARE
    v_tenant uuid := (SELECT id FROM tenants LIMIT 1);
    v_seed jsonb;
BEGIN
    -- Helper inline: schema "minimo viable" parametrizable por subtitulo.
    -- Se reemplaza por jsonb_build_object para cada insert (no hay funciones temporales).

    -- 1) ORDEN_MEDICAMENTOS
    IF NOT EXISTS (SELECT 1 FROM form_definitions WHERE tenant_id = v_tenant AND tipo = 'ORDEN_MEDICAMENTOS') THEN
        v_seed := jsonb_build_object(
            'header', jsonb_build_object(
                'institucion', 'IPS VISAL RT',
                'subtitulo', 'Orden de Medicamentos',
                'mostrarNoHistoria', true,
                'mostrarFecha', true
            ),
            'children', jsonb_build_array(
                jsonb_build_object(
                    'id', 'sec-cab',
                    'type', 'section',
                    'label', 'Datos del paciente',
                    'children', jsonb_build_array(
                        jsonb_build_object('id','f-nombre','type','text','label','Nombre del paciente','name','paciente_nombre'),
                        jsonb_build_object('id','f-doc','type','text','label','Documento','name','paciente_doc'),
                        jsonb_build_object('id','f-edad','type','text','label','Edad','name','paciente_edad')
                    )
                ),
                jsonb_build_object(
                    'id', 'sec-items',
                    'type', 'section',
                    'label', 'Items de la orden',
                    'children', jsonb_build_array(
                        jsonb_build_object('id','t-help','type','text','isText',true,
                            'content','Listado de medicamentos prescritos en la atencion (Fase 2: se llena automaticamente con los items guardados en la HC).')
                    )
                ),
                jsonb_build_object(
                    'id', 'sec-firma',
                    'type', 'section',
                    'label', 'Firma del profesional',
                    'children', jsonb_build_array(
                        jsonb_build_object('id','f-prof','type','text','label','Profesional','name','profesional_nombre'),
                        jsonb_build_object('id','f-reg','type','text','label','Registro medico','name','profesional_registro')
                    )
                )
            )
        );
        INSERT INTO form_definitions (id, tenant_id, codigo, codigo_secundario, nombre, version, tipo, schema_json, activo, created_at)
        VALUES (gen_random_uuid(), v_tenant, 'ORD-MEDICAMENTOS', 'ORD-MED', 'Orden de Medicamentos', '1.0',
                'ORDEN_MEDICAMENTOS', v_seed, true, NOW());
        RAISE NOTICE '+ Orden de Medicamentos creada';
    ELSE
        RAISE NOTICE '= Orden de Medicamentos ya existe';
    END IF;

    -- 2) ORDEN_SERVICIOS
    IF NOT EXISTS (SELECT 1 FROM form_definitions WHERE tenant_id = v_tenant AND tipo = 'ORDEN_SERVICIOS') THEN
        v_seed := jsonb_build_object(
            'header', jsonb_build_object(
                'institucion', 'IPS VISAL RT',
                'subtitulo', 'Orden de Servicios',
                'mostrarNoHistoria', true,
                'mostrarFecha', true
            ),
            'children', jsonb_build_array(
                jsonb_build_object('id','sec-cab','type','section','label','Datos del paciente',
                    'children', jsonb_build_array(
                        jsonb_build_object('id','f-nombre','type','text','label','Nombre del paciente','name','paciente_nombre'),
                        jsonb_build_object('id','f-doc','type','text','label','Documento','name','paciente_doc')
                    )
                ),
                jsonb_build_object('id','sec-items','type','section','label','Servicios solicitados',
                    'children', jsonb_build_array(
                        jsonb_build_object('id','t-help','type','text','isText',true,
                            'content','Listado de servicios / CUPS prescritos (Fase 2: pre-llenado con los items guardados en la HC).')
                    )
                ),
                jsonb_build_object('id','sec-firma','type','section','label','Firma del profesional',
                    'children', jsonb_build_array(
                        jsonb_build_object('id','f-prof','type','text','label','Profesional','name','profesional_nombre'),
                        jsonb_build_object('id','f-reg','type','text','label','Registro medico','name','profesional_registro')
                    )
                )
            )
        );
        INSERT INTO form_definitions (id, tenant_id, codigo, codigo_secundario, nombre, version, tipo, schema_json, activo, created_at)
        VALUES (gen_random_uuid(), v_tenant, 'ORD-SERVICIOS', 'ORD-SRV', 'Orden a Servicios', '1.0',
                'ORDEN_SERVICIOS', v_seed, true, NOW());
        RAISE NOTICE '+ Orden a Servicios creada';
    ELSE
        RAISE NOTICE '= Orden a Servicios ya existe';
    END IF;

    -- 3) REMISIONES
    IF NOT EXISTS (SELECT 1 FROM form_definitions WHERE tenant_id = v_tenant AND tipo = 'REMISIONES') THEN
        v_seed := jsonb_build_object(
            'header', jsonb_build_object('institucion','IPS VISAL RT','subtitulo','Orden de Remision','mostrarNoHistoria',true,'mostrarFecha',true),
            'children', jsonb_build_array(
                jsonb_build_object('id','sec-cab','type','section','label','Datos del paciente',
                    'children', jsonb_build_array(
                        jsonb_build_object('id','f-nombre','type','text','label','Nombre del paciente','name','paciente_nombre'),
                        jsonb_build_object('id','f-doc','type','text','label','Documento','name','paciente_doc')
                    )
                ),
                jsonb_build_object('id','sec-rem','type','section','label','Remision',
                    'children', jsonb_build_array(
                        jsonb_build_object('id','t-help','type','text','isText',true,
                            'content','Especialidad destino, motivo de la remision y observaciones (Fase 2: pre-llenado con datos de la HC).')
                    )
                ),
                jsonb_build_object('id','sec-firma','type','section','label','Firma del profesional',
                    'children', jsonb_build_array(
                        jsonb_build_object('id','f-prof','type','text','label','Profesional','name','profesional_nombre'),
                        jsonb_build_object('id','f-reg','type','text','label','Registro medico','name','profesional_registro')
                    )
                )
            )
        );
        INSERT INTO form_definitions (id, tenant_id, codigo, codigo_secundario, nombre, version, tipo, schema_json, activo, created_at)
        VALUES (gen_random_uuid(), v_tenant, 'ORD-REMISIONES', 'ORD-REM', 'Orden de Remision', '1.0',
                'REMISIONES', v_seed, true, NOW());
        RAISE NOTICE '+ Orden de Remision creada';
    ELSE
        RAISE NOTICE '= Orden de Remision ya existe';
    END IF;

    -- 4) INCAPACIDADES
    IF NOT EXISTS (SELECT 1 FROM form_definitions WHERE tenant_id = v_tenant AND tipo = 'INCAPACIDADES') THEN
        v_seed := jsonb_build_object(
            'header', jsonb_build_object('institucion','IPS VISAL RT','subtitulo','Orden de Incapacidad','mostrarNoHistoria',true,'mostrarFecha',true),
            'children', jsonb_build_array(
                jsonb_build_object('id','sec-cab','type','section','label','Datos del paciente',
                    'children', jsonb_build_array(
                        jsonb_build_object('id','f-nombre','type','text','label','Nombre del paciente','name','paciente_nombre'),
                        jsonb_build_object('id','f-doc','type','text','label','Documento','name','paciente_doc')
                    )
                ),
                jsonb_build_object('id','sec-inc','type','section','label','Incapacidad',
                    'children', jsonb_build_array(
                        jsonb_build_object('id','t-help','type','text','isText',true,
                            'content','Motivo, fecha desde / hasta, dias, tipo (Enfermedad General / Accidente de Trabajo). Fase 2: pre-llenado.')
                    )
                ),
                jsonb_build_object('id','sec-firma','type','section','label','Firma del profesional',
                    'children', jsonb_build_array(
                        jsonb_build_object('id','f-prof','type','text','label','Profesional','name','profesional_nombre'),
                        jsonb_build_object('id','f-reg','type','text','label','Registro medico','name','profesional_registro')
                    )
                )
            )
        );
        INSERT INTO form_definitions (id, tenant_id, codigo, codigo_secundario, nombre, version, tipo, schema_json, activo, created_at)
        VALUES (gen_random_uuid(), v_tenant, 'ORD-INCAPACIDADES', 'ORD-INC', 'Orden de Incapacidad', '1.0',
                'INCAPACIDADES', v_seed, true, NOW());
        RAISE NOTICE '+ Orden de Incapacidad creada';
    ELSE
        RAISE NOTICE '= Orden de Incapacidad ya existe';
    END IF;

    -- 5) CERTIFICADOS
    IF NOT EXISTS (SELECT 1 FROM form_definitions WHERE tenant_id = v_tenant AND tipo = 'CERTIFICADOS') THEN
        v_seed := jsonb_build_object(
            'header', jsonb_build_object('institucion','IPS VISAL RT','subtitulo','Certificacion Medica','mostrarNoHistoria',true,'mostrarFecha',true),
            'children', jsonb_build_array(
                jsonb_build_object('id','sec-cab','type','section','label','Datos del paciente',
                    'children', jsonb_build_array(
                        jsonb_build_object('id','f-nombre','type','text','label','Nombre del paciente','name','paciente_nombre'),
                        jsonb_build_object('id','f-doc','type','text','label','Documento','name','paciente_doc')
                    )
                ),
                jsonb_build_object('id','sec-cert','type','section','label','Cuerpo del certificado',
                    'children', jsonb_build_array(
                        jsonb_build_object('id','t-help','type','text','isText',true,
                            'content','Texto libre del certificado (asistencia, constancia medica, etc.). Fase 2: pre-llenado con titulo + contenido guardado en la HC.')
                    )
                ),
                jsonb_build_object('id','sec-firma','type','section','label','Firma del profesional',
                    'children', jsonb_build_array(
                        jsonb_build_object('id','f-prof','type','text','label','Profesional','name','profesional_nombre'),
                        jsonb_build_object('id','f-reg','type','text','label','Registro medico','name','profesional_registro')
                    )
                )
            )
        );
        INSERT INTO form_definitions (id, tenant_id, codigo, codigo_secundario, nombre, version, tipo, schema_json, activo, created_at)
        VALUES (gen_random_uuid(), v_tenant, 'ORD-CERTIFICADOS', 'ORD-CERT', 'Certificacion Medica', '1.0',
                'CERTIFICADOS', v_seed, true, NOW());
        RAISE NOTICE '+ Certificacion Medica creada';
    ELSE
        RAISE NOTICE '= Certificacion Medica ya existe';
    END IF;

END $$;

-- Verificacion
SELECT codigo, nombre, tipo, activo FROM form_definitions
WHERE tipo IN ('ORDEN_MEDICAMENTOS','ORDEN_SERVICIOS','REMISIONES','INCAPACIDADES','CERTIFICADOS')
ORDER BY tipo;
