# Insert-HCFO08-General.ps1
# Crea (o reemplaza) el formato HC-FO-08 FORMATO HC GENERAL fiel al docx
# original del cliente. Patron:
#   - Datos personales: grid de campos
#   - MOTIVO/ENFERMEDAD: textareas (NO titulos)
#   - Antecedentes (familiares/personales/gineco): tabla seed con Observacion editable
#   - Revision por sistemas: tabla seed con Hallazgo editable
#   - Actividad fisica / Habitos toxicos: tabla seed con 4 cols editables
#   - Signos vitales: grid de 10 campos
#   - Examen fisico: tabla seed larga
#   - Plan / Servicios / Medicamentos / Remisiones / Lab / Insumos / Incapacidades: tablas repetibles
#   - Firma del medico

[CmdletBinding()]
param(
    [string]$TenantId = "019e6b0a-a4d8-70d6-a343-d307ebd24b15"
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_Escalas-Helpers.ps1")

function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }

# ====================== DATOS PERSONALES ======================
$secDatos = @{
    id = newId; type = "section"; label = "DATOS PERSONALES"
    children = @(
        @{ id = newId; type = "field"; fieldType = "text"; label = "TIPO ID";                         name = "tipo_id";                  widthColumns = 2; required = $true },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Identificacion";                  name = "identificacion";           widthColumns = 3; required = $true },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Nombres y apellidos";             name = "nombre_paciente";          widthColumns = 5; required = $true },
        @{ id = newId; type = "field"; fieldType = "date"; label = "Fecha nacimiento";                name = "fecha_nacimiento";         widthColumns = 2 },
        @{ id = newId; type = "field"; fieldType = "calculated"; label = "Edad (auto)";               name = "edad";                     widthColumns = 2; formula = "edad(fecha_nacimiento)" },
        @{ id = newId; type = "field"; fieldType = "select"; label = "Sexo"; name = "sexo";           widthColumns = 2; catalog = "estatico"; options = @("MASCULINO","FEMENINO","OTRO") },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Estado civil";                    name = "estado_civil";             widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Direccion";                       name = "direccion";                widthColumns = 4 },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Telefono";                        name = "telefono";                 widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Celular";                         name = "celular";                  widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "text"; label = "EPS";                             name = "eps";                      widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Regimen";                         name = "regimen";                  widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Tipo de afiliado";                name = "tipo_afiliado";            widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Ocupacion";                       name = "ocupacion";                widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Nombre de acudiente/responsable"; name = "acudiente_nombre";         widthColumns = 6 },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Parentesco";                      name = "acudiente_parentesco";     widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Telefono acudiente";              name = "acudiente_telefono";       widthColumns = 3 }
    )
}

# ====================== MOTIVO / ENFERMEDAD ======================
$secMotivo = @{
    id = newId; type = "section"; label = "MOTIVO DE CONSULTA"
    children = @(
        @{ id = newId; type = "field"; fieldType = "textarea"; label = "Motivo de consulta"; name = "motivo_consulta"; widthColumns = 12; required = $true; defaultValue = "NO REFIERE" }
    )
}
$secEnf = @{
    id = newId; type = "section"; label = "ENFERMEDAD ACTUAL"
    children = @(
        @{ id = newId; type = "field"; fieldType = "textarea"; label = "Enfermedad actual"; name = "enfermedad_actual"; widthColumns = 12; defaultValue = "NO REFIERE" }
    )
}

# ====================== ANTECEDENTES FAMILIARES ======================
$colsAF = @(
    @{ id = newId; label = "Item";        name = "item";        fieldType = "text" },
    @{ id = newId; label = "Observacion"; name = "observacion"; fieldType = "text"; defaultValue = "NO REFIERE" }
)
$seedAF = @(
    @(,"Hipertension Arterial"),
    @(,"Diabetes"),
    @(,"Cancer"),
    @(,"Otros")
)
$secAF = @{
    id = newId; type = "section"; label = "ANTECEDENTES FAMILIARES"
    children = @(
        @{ id = newId; type = "field"; fieldType = "table"
           label = "Antecedentes familiares"; name = "antecedentes_familiares"
           widthColumns = 12; columns = $colsAF; seedRows = $seedAF; lockRows = $true }
    )
}

# ====================== ANTECEDENTES PERSONALES ======================
$seedAP = @(
    @(,"Patologicos"),
    @(,"Farmacologicos"),
    @(,"Alergicos"),
    @(,"Quirurgicos / traumaticos"),
    @(,"Toxicos"),
    @(,"Otros")
)
$colsAP = @(
    @{ id = newId; label = "Item";        name = "item";        fieldType = "text" },
    @{ id = newId; label = "Observacion"; name = "observacion"; fieldType = "text"; defaultValue = "NO REFIERE" }
)
$secAP = @{
    id = newId; type = "section"; label = "ANTECEDENTES PERSONALES"
    children = @(
        @{ id = newId; type = "field"; fieldType = "table"
           label = "Antecedentes personales"; name = "antecedentes_personales"
           widthColumns = 12; columns = $colsAP; seedRows = $seedAP; lockRows = $true }
    )
}

# ====================== GINECO OBSTETRICOS ======================
$seedGO = @(
    @(,"Menarquia"),
    @(,"Ciclo Menstrual"),
    @(,"Gestaciones"),
    @(,"Planificacion"),
    @(,"Menopausia")
)
$colsGO = @(
    @{ id = newId; label = "Item";        name = "item";        fieldType = "text" },
    @{ id = newId; label = "Observacion"; name = "observacion"; fieldType = "text"; defaultValue = "NO REFIERE" }
)
$secGO = @{
    id = newId; type = "section"; label = "GINECO OBSTETRICOS"
    children = @(
        @{ id = newId; type = "field"; fieldType = "table"
           label = "Antecedentes gineco-obstetricos"; name = "gineco_obstetricos"
           widthColumns = 12; columns = $colsGO; seedRows = $seedGO; lockRows = $true }
    )
}

# ====================== REVISION POR SISTEMAS ======================
$seedRS = @(
    @(,"Presenta Epilepsia o Convulsiones"),
    @(,"Manifiesta tener Deformidades / Amputaciones"),
    @(,"Cardiovascular"),
    @(,"Dermatologico"),
    @(,"Digestivo"),
    @(,"Genitourinario"),
    @(,"Neurologico"),
    @(,"Ocular"),
    @(,"Otorrinolaringologico"),
    @(,"Osteomuscular"),
    @(,"Respiratorio"),
    @(,"Otros Sistemas"),
    @(,"Observaciones")
)
$colsRS = @(
    @{ id = newId; label = "Nombre del Sistema"; name = "sistema";   fieldType = "text" },
    @{ id = newId; label = "Hallazgo";           name = "hallazgo";  fieldType = "text"; defaultValue = "NO REFIERE" }
)
$secRS = @{
    id = newId; type = "section"; label = "REVISION POR SISTEMAS"
    children = @(
        @{ id = newId; type = "field"; fieldType = "table"
           label = "Revision por sistemas"; name = "revision_sistemas"
           widthColumns = 12; columns = $colsRS; seedRows = $seedRS; lockRows = $true }
    )
}

# ====================== ACTIVIDAD FISICA ======================
$seedAct = @(
    @(,"Actividades Manuales"),
    @(,"Ejercicios o Deportes"),
    @(,"Deportes de Choque"),
    @(,"Oficios Domesticos")
)
$colsAct = @(
    @{ id = newId; label = "Habito";      name = "habito";      fieldType = "text" },
    @{ id = newId; label = "Observacion"; name = "observacion"; fieldType = "text"; defaultValue = "NO REFIERE" },
    @{ id = newId; label = "Refiere";     name = "refiere";     fieldType = "text"; defaultValue = "NO REFIERE" },
    @{ id = newId; label = "Cantidad";    name = "cantidad";    fieldType = "text"; defaultValue = "NO REFIERE" },
    @{ id = newId; label = "Frecuencia";  name = "frecuencia";  fieldType = "text"; defaultValue = "NO REFIERE" }
)
$secAct = @{
    id = newId; type = "section"; label = "ACTIVIDAD FISICA"
    children = @(
        @{ id = newId; type = "field"; fieldType = "table"
           label = "Actividad fisica"; name = "actividad_fisica"
           widthColumns = 12; columns = $colsAct; seedRows = $seedAct; lockRows = $true }
    )
}

# ====================== HABITOS TOXICOS ======================
$seedHT = @(
    @(,"Consumidor de Alcohol"),
    @(,"Fumador Actual"),
    @(,"Ex Fumador"),
    @(,"Usa Sustancias Psicoactivas")
)
$colsHT = @(
    @{ id = newId; label = "Habito";      name = "habito";      fieldType = "text" },
    @{ id = newId; label = "Observacion"; name = "observacion"; fieldType = "text"; defaultValue = "NO REFIERE" },
    @{ id = newId; label = "Refiere";     name = "refiere";     fieldType = "text"; defaultValue = "NO REFIERE" },
    @{ id = newId; label = "Cantidad";    name = "cantidad";    fieldType = "text"; defaultValue = "NO REFIERE" },
    @{ id = newId; label = "Frecuencia";  name = "frecuencia";  fieldType = "text"; defaultValue = "NO REFIERE" }
)
$secHT = @{
    id = newId; type = "section"; label = "HABITOS TOXICOS"
    children = @(
        @{ id = newId; type = "field"; fieldType = "table"
           label = "Habitos toxicos"; name = "habitos_toxicos"
           widthColumns = 12; columns = $colsHT; seedRows = $seedHT; lockRows = $true }
    )
}

# ====================== SIGNOS VITALES ======================
$secSV = @{
    id = newId; type = "section"; label = "SIGNOS VITALES"
    children = @(
        # --- Tension arterial (sistolica + diastolica + clasificacion auto) ---
        @{ id = newId; type = "text"; textStyle = "subheading"; content = "Tension Arterial (mm Hg)" },
        @{ id = newId; type = "field"; fieldType = "number"; label = "Sistolica";  name = "ta_sistolica";  widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "number"; label = "Diastolica"; name = "ta_diastolica"; widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "calculated"; label = "Clasificacion TA (auto)"; name = "ta_clasificacion"; widthColumns = 6;
           formula = "tensionClass(ta_sistolica, ta_diastolica)" },

        # --- Otros vitales ---
        @{ id = newId; type = "field"; fieldType = "number"; label = "F. Cardiaca (x min)";       name = "fc";                 widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "number"; label = "F. Respiratoria (x min)";   name = "fr";                 widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "number"; label = "Pulsioximetria (%)";        name = "pulsioximetria";     widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "number"; label = "Temperatura (C)";           name = "temperatura";        widthColumns = 3 },

        # --- Peso, talla e IMC con clasificacion automatica ---
        @{ id = newId; type = "field"; fieldType = "number"; label = "Peso (Kg)";                 name = "peso";               widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "number"; label = "Talla (cm)";                name = "talla";              widthColumns = 3 },
        @{ id = newId; type = "field"; fieldType = "calculated"; label = "IMC (auto)";            name = "imc";                widthColumns = 3;
           formula = "imc(peso, talla)" },
        @{ id = newId; type = "field"; fieldType = "calculated"; label = "Clasificacion IMC (auto)"; name = "imc_clasificacion"; widthColumns = 3;
           formula = "imcClass(imc)" },

        # --- Perimetro abdominal con interpretacion segun sexo ---
        @{ id = newId; type = "field"; fieldType = "number"; label = "Perimetro Abdominal (cm)";   name = "perimetro";          widthColumns = 4 },
        @{ id = newId; type = "field"; fieldType = "calculated"; label = "Interpretacion (auto, segun sexo)"; name = "perimetro_riesgo"; widthColumns = 4;
           formula = "perimetroRiesgo(perimetro, sexo)" },

        # --- Lateralidad como select estatico ---
        @{ id = newId; type = "field"; fieldType = "select"; label = "Lateralidad Dominante";     name = "lateralidad";        widthColumns = 4;
           catalog = "estatico"; options = @("DIESTRO","ZURDO","AMBIDIESTRO") }
    )
}

# ====================== EXAMEN FISICO ======================
$seedEF = @(
    @(,"Atrofia"), @(,"Adenopatias"), @(,"Ingurgitacion Yugular"), @(,"Masas"),
    @(,"Movilidad"), @(,"Expansion Toracica"), @(,"Ganglios Axilares"),
    @(,"Mamas y Pezon"), @(,"Cuero Cabelludo"),
    @(,"Auscultacion Pulmonar"), @(,"Ruidos Cardiacos"), @(,"Auscultacion abdominal"),
    @(,"Inspeccion"), @(,"Palpacion"),
    @(,"Genitales Externos"),
    @(,"Escleras Color"), @(,"Estrabismo"), @(,"Hiperemia Conjuntival"),
    @(,"Pupilas - Normo reactivas a la luz"),
    @(,"Fuerza Muscular"), @(,"Sensibilidad"),
    @(,"Audicion"), @(,"Otoscopia"), @(,"Pabellon"),
    @(,"Rinorrea"), @(,"Sangrado (epistaxis)"), @(,"Tabique"),
    @(,"Dentadura"), @(,"Mucosa Oral"),
    @(,"Deformidad"), @(,"Edemas"),
    @(,"Inspeccion miembros"), @(,"Articulaciones"),
    @(,"Observaciones")
)
$colsEF = @(
    @{ id = newId; label = "Aspecto evaluado"; name = "aspecto"; fieldType = "text" },
    @{ id = newId; label = "Hallazgo";         name = "hallazgo"; fieldType = "text"; defaultValue = "NO REFIERE" }
)
$secEF = @{
    id = newId; type = "section"; label = "EXAMEN FISICO"
    children = @(
        @{ id = newId; type = "field"; fieldType = "table"
           label = "Examen fisico"; name = "examen_fisico"
           widthColumns = 12; columns = $colsEF; seedRows = $seedEF; lockRows = $true }
    )
}

# ====================== ANALISIS ======================
$secAnalisis = @{
    id = newId; type = "section"; label = "ANALISIS"
    children = @(
        @{ id = newId; type = "field"; fieldType = "textarea"; label = "Analisis clinico"; name = "analisis"; widthColumns = 12; defaultValue = "NO REFIERE" },
        @{ id = newId; type = "text"; textStyle = "subheading"; content = "Escalas aplicadas en esta valoracion (cuando corresponda)" },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Escala Barthel";  name = "esc_barthel"; widthColumns = 3; defaultValue = "NO REFIERE" },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Escala Morse";   name = "esc_morse";   widthColumns = 3; defaultValue = "NO REFIERE" },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Escala Norton";  name = "esc_norton";  widthColumns = 3; defaultValue = "NO REFIERE" },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Escala Braden";  name = "esc_braden";  widthColumns = 3; defaultValue = "NO REFIERE" }
    )
}

# ====================== PLAN ======================
$secPlan = @{
    id = newId; type = "section"; label = "PLAN"
    children = @(
        @{ id = newId; type = "field"; fieldType = "textarea"; label = "Plan de manejo"; name = "plan"; widthColumns = 12; defaultValue = "NO REFIERE" }
    )
}

# ====================== SERVICIOS ======================
$secSrv = @{
    id = newId; type = "section"; label = "SERVICIOS"
    children = @(
        @{ id = newId; type = "field"; fieldType = "table"; label = "Servicios solicitados"; name = "servicios"; widthColumns = 12
          columns = @(
              @{ id = newId; label = "Codigo";      name = "codigo";      fieldType = "text" },
              @{ id = newId; label = "Descripcion"; name = "descripcion"; fieldType = "text" },
              @{ id = newId; label = "Obs";         name = "obs";         fieldType = "text" },
              @{ id = newId; label = "Tipo";        name = "tipo";        fieldType = "text" },
              @{ id = newId; label = "Sub";         name = "sub";         fieldType = "text" },
              @{ id = newId; label = "Cantidad";    name = "cantidad";    fieldType = "number" },
              @{ id = newId; label = "Mes";         name = "mes";         fieldType = "text" },
              @{ id = newId; label = "Ano";         name = "ano";         fieldType = "text" }
          )
        }
    )
}

# ====================== MEDICAMENTOS ======================
$secMed = @{
    id = newId; type = "section"; label = "MEDICAMENTOS"
    children = @(
        @{ id = newId; type = "field"; fieldType = "table"; label = "Medicamentos"; name = "medicamentos"; widthColumns = 12
          columns = @(
              @{ id = newId; label = "Descripcion";       name = "descripcion"; fieldType = "text" },
              @{ id = newId; label = "Via Administracion"; name = "via";         fieldType = "text" },
              @{ id = newId; label = "Observaciones";      name = "obs";         fieldType = "text" },
              @{ id = newId; label = "Cantidad Total";     name = "cantidad";    fieldType = "number" }
          )
        }
    )
}

# ====================== REMISIONES ======================
$secRem = @{
    id = newId; type = "section"; label = "REMISIONES"
    children = @(
        @{ id = newId; type = "field"; fieldType = "table"; label = "Remisiones"; name = "remisiones"; widthColumns = 12
          columns = @(
              @{ id = newId; label = "Codigo";       name = "codigo";      fieldType = "text" },
              @{ id = newId; label = "Descripcion";  name = "descripcion"; fieldType = "text" },
              @{ id = newId; label = "Observaciones"; name = "obs";         fieldType = "text" }
          )
        }
    )
}

# ====================== LABORATORIOS ======================
$secLab = @{
    id = newId; type = "section"; label = "LABORATORIOS"
    children = @(
        @{ id = newId; type = "field"; fieldType = "table"; label = "Laboratorios"; name = "laboratorios"; widthColumns = 12
          columns = @(
              @{ id = newId; label = "Codigo";       name = "codigo";      fieldType = "text" },
              @{ id = newId; label = "Descripcion";  name = "descripcion"; fieldType = "text" },
              @{ id = newId; label = "Observaciones"; name = "obs";         fieldType = "text" }
          )
        }
    )
}

# ====================== INSUMOS ======================
$secIns = @{
    id = newId; type = "section"; label = "INSUMOS"
    children = @(
        @{ id = newId; type = "field"; fieldType = "table"; label = "Insumos"; name = "insumos"; widthColumns = 12
          columns = @(
              @{ id = newId; label = "Codigo";       name = "codigo";      fieldType = "text" },
              @{ id = newId; label = "Descripcion";  name = "descripcion"; fieldType = "text" },
              @{ id = newId; label = "Observaciones"; name = "obs";         fieldType = "text" },
              @{ id = newId; label = "Cantidad";     name = "cantidad";    fieldType = "number" }
          )
        }
    )
}

# ====================== INCAPACIDADES ======================
$secInc = @{
    id = newId; type = "section"; label = "INCAPACIDADES"
    children = @(
        @{ id = newId; type = "field"; fieldType = "table"; label = "Incapacidades"; name = "incapacidades"; widthColumns = 12
          columns = @(
              @{ id = newId; label = "Motivo";          name = "motivo"; fieldType = "text" },
              @{ id = newId; label = "Desde";           name = "desde";  fieldType = "date" },
              @{ id = newId; label = "Hasta";           name = "hasta";  fieldType = "date" },
              @{ id = newId; label = "Dias Incapacidad"; name = "dias";  fieldType = "number" }
          )
        }
    )
}

# ====================== MEDICO / FIRMA ======================
$secMedico = @{
    id = newId; type = "section"; label = "MEDICO"
    children = @(
        @{ id = newId; type = "field"; fieldType = "text"; label = "Nombre";   name = "medico_nombre"; widthColumns = 6; required = $true; defaultValue = "NO REFIERE" },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Documento"; name = "medico_doc";   widthColumns = 3; defaultValue = "NO REFIERE" },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Registro"; name = "medico_reg";   widthColumns = 3; defaultValue = "NO REFIERE" },
        @{ id = newId; type = "field"; fieldType = "text"; label = "Firma";    name = "medico_firma"; widthColumns = 12; defaultValue = "NO REFIERE" }
    )
}

# ====================== ARMAR SCHEMA ======================
$schema = @{
    header = Build-Header "HISTORIA CLINICA GENERAL"
    children = @(
        $secDatos, $secMotivo, $secEnf,
        $secAF, $secAP, $secGO,
        $secRS, $secAct, $secHT,
        $secSV, $secEF,
        $secAnalisis, $secPlan,
        $secSrv, $secMed, $secRem, $secLab, $secIns, $secInc,
        $secMedico
    )
}

Save-FormDefinition -Codigo "HC-FO-08" `
    -Nombre "HC-FO-08 FORMATO HC GENERAL" `
    -Version "1.0" -Tipo "HISTORIA CLINICA" -Schema $schema -TenantId $TenantId | Out-Null

Write-Host ""
Write-Host "Secciones top-level:" -ForegroundColor Cyan
foreach ($s in $schema.children) {
    Write-Host ("  - {0} ({1} hijos)" -f $s.label, $s.children.Count)
}
