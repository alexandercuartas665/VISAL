-- Migra el formato de SeedRowCellOptions del HC-FO-08 examen_fisico a Dictionary.
-- Solo guarda celdas con opciones (col 1 = hallazgo). Las celdas sin opciones
-- se omiten para no ensuciar el JSON.

UPDATE form_definitions
SET schema_json = jsonb_set(
    schema_json::jsonb,
    '{children,10,children,0,seedRowCellOptions}',
    '{
      "0_1":  ["NO SE OBSERVA", "PRESENTE", "LEVE", "MODERADA", "SEVERA"],
      "1_1":  ["NO", "SI", "MULTIPLES", "AISLADAS"],
      "2_1":  ["NO", "SI", "LEVE", "MODERADA"],
      "3_1":  ["NO", "SI", "UNICA", "MULTIPLES"],
      "4_1":  ["NORMAL", "DISMINUIDA", "AUSENTE", "DOLOROSA"],
      "5_1":  ["SIMETRICA NORMAL", "ASIMETRICA", "DISMINUIDA"],
      "6_1":  ["NEGATIVO", "POSITIVO", "PALPABLES"],
      "7_1":  ["NORMAL", "ALTERADO", "NODULO PALPABLE"],
      "8_1":  ["NORMAL", "LESIONES", "DESCAMACION", "ALOPECIA"],
      "9_1":  ["RUIDOS RESPIRATORIOS NORMALES SIN AGREGADOS", "ESTERTORES", "ROCES", "SIBILANCIAS", "DISMINUCION MURMULLO VESICULAR"],
      "10_1": ["RITMICOS, BIEN TIMBRADOS, SIN SOPLOS", "ARRITMICOS", "SOPLO PRESENTE", "TONOS DISMINUIDOS"],
      "11_1": ["RUIDOS INTESTINALES PRESENTES NORMALES", "AUSENTES", "AUMENTADOS", "DISMINUIDOS"],
      "12_1": ["NORMAL", "ALTERADO", "CICATRIZ", "DISTENDIDO"],
      "13_1": ["BLANDO, NO DOLOROSO, NO MASAS, NO MEGALIAS", "DOLOROSO", "MASA PALPABLE", "HEPATOMEGALIA", "ESPLENOMEGALIA", "GLOBO VESICAL"],
      "14_1": ["NORMAL", "ALTERADO", "NO EVALUADO"],
      "15_1": ["ANICTERICAS", "ICTERICAS", "PALIDAS"],
      "16_1": ["NO", "SI", "DUDOSO"],
      "17_1": ["NO", "SI", "BILATERAL", "UNILATERAL"],
      "18_1": ["SI", "NO", "MIOSIS", "MIDRIASIS"],
      "19_1": ["NORMAL", "5/5", "4/5", "3/5", "2/5", "1/5", "0/5", "DISMINUIDA"],
      "20_1": ["CONSERVADA NORMAL", "DISMINUIDA", "AUSENTE", "PARESTESIAS"],
      "21_1": ["NORMAL", "HIPOACUSIA", "DISMINUIDA", "ANACUSIA"],
      "22_1": ["NORMAL", "CERUMEN", "OTITIS", "PERFORACION"],
      "23_1": ["NORMAL", "MALFORMACION", "INFECCION"],
      "24_1": ["NORMAL", "HIALINA", "PURULENTA", "SANGUINOLENTA"],
      "25_1": ["NO", "SI", "OCASIONAL", "FRECUENTE"],
      "26_1": ["NORMAL", "DESVIADO", "PERFORACION"],
      "27_1": ["COMPLETA", "INCOMPLETA", "EDENTULO PARCIAL", "EDENTULO TOTAL"],
      "28_1": ["HUMEDA", "SECA", "PALIDA", "CIANOTICA"],
      "29_1": ["NO", "SI", "CONGENITA", "ADQUIRIDA"],
      "30_1": ["NO", "SI", "DE MIEMBROS INFERIORES", "GENERALIZADO"],
      "31_1": ["SIMETRICAS, EUTROFICAS", "ASIMETRICAS", "ATROFICAS", "HIPERTROFICAS"],
      "32_1": ["NORMAL", "DOLORIDAS", "LIMITACION FUNCIONAL", "DEFORMIDAD"]
    }'::jsonb,
    true
)
WHERE codigo = 'HC-FO-08';

SELECT 'OK Dictionary v2' as r;
