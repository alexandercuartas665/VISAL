-- ============================================================================
-- Reparacion: historias BASE duplicadas en el mismo hilo (modo terapia)
--
-- Contexto (Refuerzo 1 / auditoria 2026-10): en varias asignaciones se abrio una
-- historia COMPLETA (formato base) en una sesion que debia ser EVOLUCION, dejando
-- 2+ historias completas en el mismo hilo (paciente, formato base, profesional).
-- La guardia "1 historia base por hilo" (HistoriaClinicaService.ResolverFormatoPorHiloAsync,
-- v0.101.0) evita NUEVOS casos. Este script repara los EXISTENTES: conserva la
-- base mas temprana del hilo y re-tipifica las demas al formato de EVOLUCION.
--
-- SEGURIDAD:
--   * Correr SOLO despues de un backup (actualizar-linux.ps1 ya lo toma; o pg_dump -Fc).
--   * PASO 1 (PREVIEW) es read-only: revisar la lista ANTES de aplicar y excluir a mano
--     cualquier fila que sea una terapia REALMENTE nueva (misma triada pero meses despues,
--     fuera del puente) — esas NO son duplicados.
--   * PASO 2 (APPLY) corre en transaccion y arranca con ROLLBACK. Cambiar a COMMIT solo
--     tras validar el preview.
--   * Cada HC re-tipificada se registra en hc_marcas_error (Marcadas, origen=Auto) con
--     observacion, segun la politica de logueo de reparaciones.
--
-- "Base con evolucion" = FormDefinition.formato_evolucion_codigo no vacio.
-- "No inactiva" = estado <> 2 (0=Abierta, 1=Cerrada, 2=Inactiva).
-- ============================================================================


-- ============================================================================
-- PASO 1 — PREVIEW (read-only). Lista cada hilo con 2+ bases y sus HCs.
-- La columna rn=1 es la base que se CONSERVA; rn>=2 son las que se re-tipificarian.
-- ============================================================================
WITH base_defs AS (
    SELECT id, codigo, formato_evolucion_codigo, meses_puente
    FROM form_definitions
    WHERE formato_evolucion_codigo IS NOT NULL AND formato_evolucion_codigo <> ''
),
base_hcs AS (
    SELECT h.id, h.paciente_id, h.profesional_id, h.form_definition_id,
           bd.codigo AS formato_base, bd.formato_evolucion_codigo AS evo_codigo,
           bd.meses_puente,
           h.estado, h.consecutivo,
           h.fecha_atencion, h.fecha_apertura, h.tenant_id
    FROM historias_clinicas h
    JOIN base_defs bd ON bd.id = h.form_definition_id
    WHERE h.estado <> 2 AND h.profesional_id IS NOT NULL
),
hilos AS (
    SELECT paciente_id, profesional_id, form_definition_id, count(*) AS bases
    FROM base_hcs
    GROUP BY paciente_id, profesional_id, form_definition_id
    HAVING count(*) >= 2
),
ranked AS (
    SELECT bh.*,
           row_number() OVER (
               PARTITION BY bh.paciente_id, bh.profesional_id, bh.form_definition_id
               ORDER BY bh.fecha_atencion NULLS LAST, bh.fecha_apertura, bh.consecutivo
           ) AS rn
    FROM base_hcs bh
    JOIN hilos hi ON hi.paciente_id = bh.paciente_id
                 AND hi.profesional_id = bh.profesional_id
                 AND hi.form_definition_id = bh.form_definition_id
)
SELECT r.rn,
       r.formato_base, r.evo_codigo, r.meses_puente,
       p.numero_documento AS paciente_doc, p.nombre_completo AS paciente,
       pr.nombre_completo AS profesional,
       r.consecutivo AS hc_consecutivo,
       r.estado,
       r.fecha_atencion, r.fecha_apertura,
       r.id AS historia_clinica_id
FROM ranked r
JOIN pacientes p ON p.id = r.paciente_id
LEFT JOIN profesionales pr ON pr.id = r.profesional_id
ORDER BY r.paciente_id, r.profesional_id, r.form_definition_id, r.rn;


-- ============================================================================
-- PASO 2 — APPLY (transaccion). Re-tipifica las bases extra (rn>=2) al formato de
-- evolucion y las registra en Marcadas. Arranca con ROLLBACK: cambiar a COMMIT
-- tras validar el PASO 1.  Verificar antes los enums: hc_marcas_error.estado
-- (0=Pendiente) y origen (Auto). Ajustar si el tenant usa otros valores.
-- ============================================================================
BEGIN;

-- 2.1 Resolver las bases extra y su FormDefinition de evolucion destino.
CREATE TEMP TABLE _extra AS
WITH base_defs AS (
    SELECT id, codigo, formato_evolucion_codigo
    FROM form_definitions
    WHERE formato_evolucion_codigo IS NOT NULL AND formato_evolucion_codigo <> ''
),
base_hcs AS (
    SELECT h.id, h.paciente_id, h.profesional_id, h.form_definition_id,
           bd.formato_evolucion_codigo AS evo_codigo,
           h.fecha_atencion, h.fecha_apertura, h.consecutivo, h.tenant_id
    FROM historias_clinicas h
    JOIN base_defs bd ON bd.id = h.form_definition_id
    WHERE h.estado <> 2 AND h.profesional_id IS NOT NULL
),
hilos AS (
    SELECT paciente_id, profesional_id, form_definition_id
    FROM base_hcs
    GROUP BY paciente_id, profesional_id, form_definition_id
    HAVING count(*) >= 2
),
ranked AS (
    SELECT bh.*,
           row_number() OVER (
               PARTITION BY bh.paciente_id, bh.profesional_id, bh.form_definition_id
               ORDER BY bh.fecha_atencion NULLS LAST, bh.fecha_apertura, bh.consecutivo
           ) AS rn
    FROM base_hcs bh
    JOIN hilos hi ON hi.paciente_id = bh.paciente_id
                 AND hi.profesional_id = bh.profesional_id
                 AND hi.form_definition_id = bh.form_definition_id
)
SELECT r.id AS hc_id, r.tenant_id, r.consecutivo,
       r.form_definition_id AS base_def_id,
       evo.id AS evo_def_id
FROM ranked r
JOIN form_definitions evo
     ON (evo.codigo = r.evo_codigo OR evo.codigo_secundario = r.evo_codigo)
    AND evo.activo
WHERE r.rn >= 2;

-- 2.2 Resolver asignacion + codigo + datos de paciente de cada HC (para Marcadas).
CREATE TEMP TABLE _extra_ctx AS
SELECT e.hc_id, e.tenant_id, e.consecutivo, e.base_def_id, e.evo_def_id,
       t.asignacion_id,
       LEFT(t.asignacion_id::text, 8) AS codigo_asignacion,
       p.nombre_completo AS paciente_nombre,
       p.numero_documento AS paciente_doc
FROM _extra e
JOIN historias_clinicas h ON h.id = e.hc_id
JOIN pacientes p ON p.id = h.paciente_id
LEFT JOIN asignacion_turno_sesion_hcs piv ON piv.historia_clinica_id = e.hc_id
LEFT JOIN asignacion_turno_sesiones s ON s.id = piv.sesion_id
LEFT JOIN asignacion_turnos t ON t.id = s.asignacion_turno_id;

-- 2.3 Registrar en Marcadas ANTES de cambiar (deja rastro del estado previo).
--     Solo las que tienen asignacion (NOT NULL exige asignacion_id). Las HCs sueltas
--     sin asignacion se re-tipifican igual pero no se loguean aqui (revisar aparte).
INSERT INTO hc_marcas_error
    (id, asignacion_id, codigo_asignacion, paciente_nombre, paciente_doc,
     observacion, estado, origen, historia_clinica_id, tenant_id, created_at)
SELECT gen_random_uuid(), c.asignacion_id, c.codigo_asignacion,
       c.paciente_nombre, c.paciente_doc,
       'Reparacion Refuerzo 1: historia base duplicada en el hilo re-tipificada a evolucion (consecutivo HC '
         || c.consecutivo || '). Revisar que el contenido clinico sea coherente con el formato de evolucion.',
       0,   -- estado Pendiente (verificar enum del tenant)
       1,   -- origen Auto (verificar enum del tenant)
       c.hc_id, c.tenant_id, now()
FROM _extra_ctx c
WHERE c.asignacion_id IS NOT NULL;

-- 2.4 Re-tipificar: la base extra pasa a usar el FormDefinition de evolucion.
UPDATE historias_clinicas h
SET form_definition_id = e.evo_def_id,
    updated_at = now()
FROM _extra e
WHERE h.id = e.hc_id;

-- 2.5 Verificacion: cuantas se tocaron.
SELECT (SELECT count(*) FROM _extra) AS bases_retipificadas,
       (SELECT count(*) FROM _extra_ctx WHERE asignacion_id IS NOT NULL) AS logueadas_marcadas,
       (SELECT count(*) FROM _extra_ctx WHERE asignacion_id IS NULL) AS sueltas_sin_log;

-- Revisar los conteos. Si todo OK, cambiar a COMMIT; si no, dejar ROLLBACK.
ROLLBACK;
-- COMMIT;
