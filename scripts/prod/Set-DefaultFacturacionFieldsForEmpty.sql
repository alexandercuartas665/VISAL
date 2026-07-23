-- =========================================================================
-- Set-DefaultFacturacionFieldsForEmpty.sql
--
-- Poner valores default 11 / 12 / 13 en los 3 campos de facturacion de
-- servicios_contrato para TODOS los servicios que hoy tienen los 3 campos
-- vacios (NULL). Los servicios que ya tienen algun valor NO se tocan.
--
-- Campos afectados:
--   modalidad_facturacion       -> '11'  (col 33 Snapshot "Modalidad Atencion")
--   grupo_servicio_facturacion  -> '12'  (col 35 Snapshot "Grupo Servicios")
--   servicio_facturacion        -> '13'  (col 36 Snapshot "Servicios")
--
-- Idempotente: si se corre 2 veces, la segunda vez no encuentra registros
-- que matcheen (ya no hay servicios con los 3 NULL) y no hace nada.
--
-- Auditoria: registra la ejecucion en servicio_bulk_updates + items
-- para que aparezca en el tab Historial de /cfg-aseguradoras y se pueda
-- REVERTIR desde ahi (restaura los 3 campos a NULL en los servicios
-- afectados). Marca la ejecucion como 'Aplicada'.
--
-- Antes de ejecutar (opcional): revisa cuantos servicios se verian afectados:
--   SELECT COUNT(*) FROM servicios_contrato
--    WHERE modalidad_facturacion IS NULL
--      AND grupo_servicio_facturacion IS NULL
--      AND servicio_facturacion IS NULL;
--
-- Nota: si prefieres llenar por CADA campo NULL (aunque otros esten llenos),
--       cambia el WHERE de la CTE 'afectados' por:
--         WHERE modalidad_facturacion IS NULL
--            OR grupo_servicio_facturacion IS NULL
--            OR servicio_facturacion IS NULL
--       (pero eso pisaria escenarios donde el operador quiso dejar 1 campo
--        vacio a proposito — la version por defecto es mas conservadora).
-- =========================================================================

BEGIN;

-- 1) Servicios a tocar y bulk_update_id fijo (uno por corrida) --------------
WITH afectados AS (
    SELECT id, tenant_id, modalidad_facturacion, grupo_servicio_facturacion, servicio_facturacion
      FROM servicios_contrato
     WHERE modalidad_facturacion IS NULL
       AND grupo_servicio_facturacion IS NULL
       AND servicio_facturacion IS NULL
), tenant_row AS (
    -- Prod tiene un solo tenant (VISAL). Tomamos el primero — si en tu
    -- entorno hubiera varios y quisieras hacerlo por tenant, envolveria
    -- este script en un loop, pero eso no aplica hoy.
    SELECT tenant_id, COUNT(*) AS total
      FROM afectados
     GROUP BY tenant_id
), bulk_row AS (
    -- Cabecera de la ejecucion (una fila). Registrada como Aplicada.
    INSERT INTO servicio_bulk_updates (
        id, operador_busqueda, texto_busqueda,
        nueva_modalidad_facturacion, nuevo_grupo_servicio_facturacion, nuevo_servicio_facturacion,
        motivo, total_afectados, estado, created_at, tenant_id)
    SELECT
        gen_random_uuid(),
        'Vacio',                        -- operador no soportado por la UI, es un marcador
        '(los 3 campos NULL)',
        '11', '12', '13',
        'Set default 11/12/13 a servicios con los 3 campos de facturacion vacios',
        tr.total,
        'Aplicada',
        NOW(),
        tr.tenant_id
      FROM tenant_row tr
    RETURNING id, tenant_id
)
-- 2) Snapshot de valores previos por servicio afectado -------------------
INSERT INTO servicio_bulk_update_items (
    id, bulk_update_id, servicio_contrato_id,
    modalidad_facturacion_antes, grupo_servicio_facturacion_antes, servicio_facturacion_antes,
    created_at, tenant_id)
SELECT
    gen_random_uuid(),
    br.id,
    a.id,
    NULL, NULL, NULL,               -- todos venian NULL
    NOW(),
    br.tenant_id
  FROM afectados a
  CROSS JOIN bulk_row br;

-- 3) Aplicar los valores default ----------------------------------------
UPDATE servicios_contrato
   SET modalidad_facturacion      = '11',
       grupo_servicio_facturacion = '12',
       servicio_facturacion       = '13',
       updated_at                 = NOW()
 WHERE modalidad_facturacion      IS NULL
   AND grupo_servicio_facturacion IS NULL
   AND servicio_facturacion       IS NULL;

-- 4) Purga historial >20 (misma politica FIFO que el servicio C#) --------
DELETE FROM servicio_bulk_updates
 WHERE id IN (
     SELECT id FROM servicio_bulk_updates
      ORDER BY created_at DESC
      OFFSET 20);

COMMIT;

-- Verificacion (opcional, correr fuera de la transaccion):
--   SELECT COUNT(*) FROM servicios_contrato
--    WHERE modalidad_facturacion = '11'
--      AND grupo_servicio_facturacion = '12'
--      AND servicio_facturacion = '13';
--
--   -- Ver la fila en el historial:
--   SELECT id, texto_busqueda, motivo, total_afectados, estado, created_at
--     FROM servicio_bulk_updates
--    ORDER BY created_at DESC LIMIT 1;
