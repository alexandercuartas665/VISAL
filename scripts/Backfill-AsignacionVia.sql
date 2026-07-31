-- =========================================================================
-- Backfill: asignaciones.rips_via_ingreso_codigo/nombre
--
-- Contexto: la Via de ingreso RIPS (Res. 202/2021) se movio del modal RIPS
-- de HistoriaClinica hacia Asignacion (se captura una sola vez por servicio
-- contratado y viaja al HC como snapshot inmutable al iniciar la historia).
--
-- Este script copia la Via de la HC mas reciente vinculada a cada asignacion
-- (via asignacion_turnos -> asignacion_turno_sesiones -> asignacion_turno_sesion_hcs)
-- hacia las columnas nuevas de asignaciones. Si la asignacion no tiene HC
-- vinculada, queda en NULL — el usuario capturara la Via al reeditar.
--
-- Idempotente: solo actualiza filas donde rips_via_ingreso_codigo IS NULL.
-- Seguro para re-ejecutar: no sobrescribe Vias ya capturadas manualmente.
--
-- Aplicar en dev antes del deploy. Correr manualmente en prod despues del
-- deploy (junto con la migracion EF AddAsignacionViaIngreso).
-- =========================================================================

BEGIN;

WITH hc_por_asignacion AS (
    -- Para cada asignacion, tomar la HC con via_ingreso mas reciente.
    -- Camino: asignacion -> turnos -> sesiones -> pivote HC -> historia
    -- El ROW_NUMBER garantiza una sola HC por asignacion (la ultima cerrada
    -- o abierta, ordenada por fecha_apertura desc).
    SELECT
        a.id AS asignacion_id,
        hc.rips_via_ingreso_codigo,
        hc.rips_via_ingreso_nombre,
        ROW_NUMBER() OVER (
            PARTITION BY a.id
            ORDER BY hc.fecha_apertura DESC NULLS LAST, hc.id DESC
        ) AS rn
    FROM asignaciones a
    JOIN asignacion_turnos t ON t.asignacion_id = a.id
    JOIN asignacion_turno_sesiones s ON s.asignacion_turno_id = t.id
    JOIN asignacion_turno_sesion_hcs p ON p.sesion_id = s.id
    JOIN historias_clinicas hc ON hc.id = p.historia_clinica_id
    WHERE a.rips_via_ingreso_codigo IS NULL
      AND hc.rips_via_ingreso_codigo IS NOT NULL
      AND hc.rips_via_ingreso_codigo <> ''
),
mejor_por_asignacion AS (
    SELECT asignacion_id, rips_via_ingreso_codigo, rips_via_ingreso_nombre
    FROM hc_por_asignacion
    WHERE rn = 1
)
UPDATE asignaciones a
SET rips_via_ingreso_codigo = m.rips_via_ingreso_codigo,
    rips_via_ingreso_nombre = m.rips_via_ingreso_nombre
FROM mejor_por_asignacion m
WHERE a.id = m.asignacion_id
  AND a.rips_via_ingreso_codigo IS NULL;

-- Verificacion.
SELECT
    COUNT(*) FILTER (WHERE rips_via_ingreso_codigo IS NOT NULL) AS con_via,
    COUNT(*) FILTER (WHERE rips_via_ingreso_codigo IS NULL) AS sin_via,
    COUNT(*) AS total
FROM asignaciones;

COMMIT;
