-- =====================================================================
-- seed_aseguradoras_consola_sql.sql
-- Version para pegar en el modulo "Consola SQL" del sistema Visal.
--
-- COMO USAR:
--   1) En prod entra a /sql-console.
--   2) Primero corre este SELECT para averiguar el UUID de tu tenant:
--
--        SELECT id, name FROM tenants ORDER BY name;
--
--   3) Copia el UUID resultante.
--   4) En este script REEMPLAZA la cadena '<PON_AQUI_EL_UUID_TENANT>'
--      (aparece 1 sola vez, en el ultimo SELECT del INSERT) por el UUID.
--      Ejemplo: '019e6b0a-a4d8-70d6-a343-d307ebd24b15'
--   5) Pega el INSERT completo en la consola y ejecuta.
--
-- IDEMPOTENTE: si el codigo ya existe para ese tenant, actualiza el
-- registro. Puedes correrlo varias veces sin duplicar.
--
-- POR QUE NO HAY BEGIN/COMMIT: la Consola SQL ejecuta cada envio en su
-- propia transaccion implicita. Un INSERT ... ON CONFLICT es atomico
-- por si mismo.
-- =====================================================================

INSERT INTO aseguradoras (
    id, codigo, tipo, nombre, codigo_movilidad, nit, regimen, created_at, tenant_id
)
SELECT
    gen_random_uuid(),
    f.codigo,
    f.tipo,
    f.nombre,
    NULLIF(f.codigo_movilidad, 'N/A'),
    f.nit,
    f.regimen,
    now(),
    '<PON_AQUI_EL_UUID_TENANT>'::uuid
FROM (VALUES
    -- (codigo,           tipo,  nombre,                                                          codigo_movilidad,     nit,        regimen)
    ('EPS001',            'EPS', 'ALIANSALUD EPS',                                                'EPSS01',            '830113831', 'CONTRIBUTIVO'),
    ('EPSI04',            'EPS', 'ANAS WAYUU EPSI',                                               'EPSIC4',            '839000495', 'SUBSIDIADO'),
    ('ESS062',            'EPS', 'ASMET SALUD',                                                   'ESS062',            '900935126', 'SUBSIDIADO'),
    ('ESSC62',            'EPS', 'ASMET SALUD',                                                   'ESSC62',            '900935126', 'SUBSIDIADO'),
    ('EPSI03',            'EPS', 'ASOCIACION INDIGENA DEL CAUCA EPSI',                            'EPSIC3',            '817001773', 'SUBSIDIADO'),
    ('CCF055',            'EPS', 'CAJACOPI ATLANTICO',                                            'CCFC55',            '890102044', 'SUBSIDIADO'),
    ('EPSS34',            'EPS', 'CAPITAL SALUD EPS-S',                                           'EPSC34',            '900298372', 'SUBSIDIADO'),
    ('EPS025',            'EPS', 'CAPRESOCA',                                                     'EPSC25',            '891856000', 'SUBSIDIADO'),
    ('CCF102',            'EPS', 'COMFACHOCO',                                                    'CCFC20',            '891600091', 'SUBSIDIADO'),
    ('CCF050',            'EPS', 'COMFAORIENTE',                                                  'CCFC50',            '890500675', 'SUBSIDIADO'),
    ('EPS012',            'EPS', 'COMFENALCO VALLE',                                              'EPSS12',            '890303093', 'CONTRIBUTIVO'),
    ('EPS008',            'EPS', 'COMPENSAR EPS',                                                 'EPSS08',            '860066942', 'CONTRIBUTIVO'),
    ('ESS024 - EPS042',   'EPS', 'COOSALUD EPS-S',                                                'ESSC24 - EPSS42',   '900226715', 'AMBOS REGÍMENES'),
    ('EPSI01',            'EPS', 'DUSAKAWI EPSI',                                                 'EPSIC1',            '824001398', 'SUBSIDIADO'),
    ('ESS118',            'EPS', 'EMSSANAR E.S.S.',                                               'ESSC18',            '901021565', 'SUBSIDIADO'),
    ('EAS016',            'EPS', 'EPM - EMPRESAS PUBLICAS DE MEDELLIN',                           'N/A',               '890904996', 'CONTRIBUTIVO'),
    ('CCF033',            'EPS', 'EPS FAMILIAR DE COLOMBIA',                                      'CCFC33',            '901543761', 'SUBSIDIADO'),
    ('EPS005',            'EPS', 'EPS SANITAS',                                                   'EPSS05',            '800251440', 'CONTRIBUTIVO'),
    ('EPS010',            'EPS', 'EPS SURA',                                                      'EPSS10',            '800088702', 'CONTRIBUTIVO'),
    ('EPS017',            'EPS', 'FAMISANAR',                                                     'EPSS17',            '830003564', 'CONTRIBUTIVO'),
    ('RES004',            'EPS', 'FIDEICOMISOS PATRIMONIOS AUTONOMOS FIDUCIARIA LA PREVISORA SA', 'RES004',            '830053105', 'ESPECIAL'),
    ('EAS027',            'EPS', 'FONDO DE PASIVO SOCIAL DE FERROCARRILES NACIONALES DE COLOMBIA','N/A',               '800112806', 'CONTRIBUTIVO'),
    ('EPSI05',            'EPS', 'MALLAMAS EPSI',                                                 'EPSIC5',            '837000084', 'SUBSIDIADO'),
    ('ESS207 - EPS048',   'EPS', 'MUTUAL SER',                                                    'ESSC07 - EPSS48',   '806008394', 'AMBOS REGÍMENES'),
    ('EPS037 - EPSS41',   'EPS', 'NUEVA EPS',                                                     'EPSS37 - EPS041',   '900156264', 'AMBOS REGÍMENES'),
    ('EPSI06',            'EPS', 'PIJAOS SALUD EPSI',                                             'EPSIC6',            '809008362', 'SUBSIDIADO'),
    ('RES001',            'EPS', 'POLICIA NACIONAL',                                              'RES001',            '805022186', 'CONTRIBUTIVO'),
    ('EPS047',            'EPS', 'SALUD BÓLIVAR EPS SAS',                                         'EPSS47',            '901438242', 'CONTRIBUTIVO'),
    ('EPS046',            'EPS', 'SALUD MIA',                                                     'EPSS46',            '900914254', 'CONTRIBUTIVO'),
    ('EPS002',            'EPS', 'SALUD TOTAL EPS S.A.',                                          'EPSS02',            '800130907', 'CONTRIBUTIVO'),
    ('EPSS40',            'EPS', 'SAVIA SALUD EPS',                                               'EPS040',            '900604350', 'SUBSIDIADO'),
    ('EPS018',            'EPS', 'SERVICIO OCCIDENTAL DE SALUD EPS SOS',                          'EPSS18',            '805001157', 'CONTRIBUTIVO')
) AS f(codigo, tipo, nombre, codigo_movilidad, nit, regimen)
ON CONFLICT (tenant_id, codigo) DO UPDATE
SET tipo             = EXCLUDED.tipo,
    nombre           = EXCLUDED.nombre,
    codigo_movilidad = EXCLUDED.codigo_movilidad,
    nit              = EXCLUDED.nit,
    regimen          = EXCLUDED.regimen,
    updated_at       = now();

-- Despues del INSERT, si quieres verificar puedes correr en otra ejecucion:
--   SELECT codigo, nombre, tipo, regimen
--     FROM aseguradoras
--    WHERE tenant_id = '<PON_AQUI_EL_UUID_TENANT>'::uuid
--    ORDER BY nombre;
