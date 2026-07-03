# Rework-PPFO35-IngresoPAD.ps1
# Reconstruye PP-FO-35 FORMATO INGRESO PROGRAMA DE ATENCION DOMICILIARIA V3.
# Formato de declaracion (sin tablas de procedimientos/beneficios/riesgos).
#
# Preserva: Datos del Paciente (auto), Firmas (auto), Cierre.
# NO TOCA HC-FO-08.

[CmdletBinding()]
param(
    [string]$TenantId    = "019e6b0a-a4d8-70d6-a343-d307ebd24b15",
    [string]$PgContainer = "visal-postgres",
    [string]$PgUser      = "visal",
    [string]$PgDb        = "visal_dev"
)
$ErrorActionPreference = "Stop"
function newId { return [Guid]::NewGuid().ToString("N").Substring(0,8) }
function SH([string]$content) { @{ id=newId; type="text"; textStyle="subheading"; content=$content } }
function P([string]$content) { @{ id=newId; type="text"; textStyle="paragraph"; content=$content } }
function Field([string]$label, [string]$name, [string]$ft, [int]$width=12, $extra=@{}) {
    $f = @{ id=newId; type="field"; fieldType=$ft; label=$label; name=$name; widthColumns=$width; allowCustom=$false; required=$false }
    foreach ($k in $extra.Keys) { $f[$k] = $extra[$k] }
    return $f
}

# ================ Cargar y localizar auto sections ================
$raw = docker exec $PgContainer psql -U $PgUser -d $PgDb -tA -c "SELECT schema_json::text FROM form_definitions WHERE codigo='PP-FO-35' AND tenant_id='$TenantId';"
$schema = $raw | ConvertFrom-Json -AsHashtable

$datosPaciente = $null; $cierre = $null; $firmasAuto = $null
foreach ($sec in $schema.children) {
    $lbl = [string]$sec["label"]
    if ($lbl -eq "Datos del Paciente (auto-llenado)") { $datosPaciente = $sec }
    elseif ($lbl -eq "Cierre") { $cierre = $sec }
    elseif ($lbl -eq "Firmas (auto-llenadas)") { $firmasAuto = $sec }
}
if ($null -eq $datosPaciente) {
    $datosPaciente = @{
        id = "auto-datos-paciente"; type = "section"; label = "Datos del Paciente (auto-llenado)"
        children = @(
            (Field "Nombre del paciente"    "nombre_paciente_consent"  "text"   12),
            (Field "Tipo de documento"      "tipo_documento_consent"   "text"   6),
            (Field "Número de documento"    "numero_documento_consent" "text"   6),
            (Field "Edad"                   "edad_consent"             "number" 4),
            (Field "Fecha de atención"      "fecha_atencion_consent"   "date"   4)
        )
    }
    Write-Host "Seccion Datos del Paciente (auto-llenado) CREADA" -ForegroundColor Yellow
} else { Write-Host "Seccion Datos del Paciente PRESERVADA" -ForegroundColor Green }
if ($null -eq $firmasAuto) {
    $firmasAuto = @{
        id = newId; type = "section"; label = "Firmas (auto-llenadas)"
        children = @(
            (Field "Firma del paciente (URL)"     "firma_paciente_consent"     "text" 12),
            (Field "Firma del profesional (URL)"  "firma_profesional_consent"  "text" 12)
        )
    }
    Write-Host "Seccion Firmas (auto-llenadas) CREADA" -ForegroundColor Yellow
} else { Write-Host "Seccion Firmas (auto-llenadas) PRESERVADA" -ForegroundColor Green }
if ($null -eq $cierre) {
    $cierre = @{
        id = newId; type = "section"; label = "Cierre"
        children = @( (Field "Observaciones / Conclusiones" "observaciones_cierre" "textarea" 12 @{ enableVoice = $true }) )
    }
}

# ================ Textos declaraciones (docx literal) ================
$decl_1 = "Haber entregado a mi médico y/o equipo de salud tratante en forma veraz, completa y fidedigna toda la información vinculada a mi estado de salud e historia clínica y haber sido debida y completamente informado(a) sobre todos los aspectos concernientes a la ASISTENCIA DOMICILIARIA y sus riesgos, además de haberse efectuado una completa valoración clínica y además hemos sido interrogados frente a los antecedentes correspondientes a las patologías que padece. De igual forma, hemos sido informados que la resolución 5261 de 1994 del ministerio de salud, en su artículo 8 establece la ASISTENCIA DOMICILIARIA como: ""aquella que se brinda en la residencia del paciente con el apoyo del personal médico y/o paramédico y la participación de su familia, la que se hará de acuerdo con las guías de atención integral establecidas para tal fin"". Entendemos las ventajas y bondades de los servicios de atención domiciliaria para el tratamiento y bienestar del paciente, así como el significado de la activa participación de la familia cuando contribuye a la recuperación o al mantenimiento de condiciones dignas para la vida del paciente. Tenemos conocimiento que, de acuerdo con la historia clínica del paciente, se requiere atención en forma domiciliaria y su condición ha sido identificada mediante los siguientes diagnósticos y pronóstico:"
$decl_2_intro = "Reportamos como domicilio para la atención del paciente la siguiente dirección:"
$decl_2_cont = "y nos obligamos a informar por escrito a IPS VISAL RT SAS. de manera inmediata, cualquier cambio de domicilio del paciente. A su vez, hemos sido informados que la medicina no es una ciencia exacta y que los tratamientos medico asistenciales comportan riesgos relacionados con la evolución de la enfermedad y las reacciones individuales propias del paciente al tratamiento o tratamientos instaurados para sus necesidades específicas. Tales riesgos pueden llegar a concretarse de diferentes maneras tales como descompensación severa del paciente, perdida del conocimiento dolores sobre agregados a los propios de la enfermedad que padece si estos existieren como fiebre, vómito recurrente, procesos infecciosos, hemorragias, pérdidas de la estabilidad de cualquiera de sus miembros o la muerte."
$decl_3 = "Entendemos y aceptamos que la duración y cobertura de los servicios de atención domiciliaria serán definidos por el médico tratante, conjuntamente con el equipo de salud responsable de dichas atenciones, pertenecientes a la red de prestadores de servicios de mi E.P.S. He comprendido las explicaciones que se me han entregado en un lenguaje claro y sencillo, y el facultativo que ha atendido me ha permitido realizar todas las observaciones y me ha aclarado todas las dudas que le he planteado. Por ello manifiesto que estoy complacido con la información recibida y que comprendo el alcance y los riesgos de la asistencia domiciliaria. También entiendo que, en cualquier momento y sin necesidad de dar ninguna explicación, puedo revocar el consentimiento que ahora presto. Y en tales condiciones CONSIENTO que se efectúe la asistencia domiciliaria del cual es objeto el presente escrito"

$comp_1 = "El cuidador asume la obligación de responder por la vigilancia del paciente y garantizar que en todo momento este permanecerá acompañado por un adulto responsable. Igualmente, nos obligamos a mantener comunicación permanente con IPS VISAL RT S.A.S., atender las citas de seguimiento de manera puntual e informar sobre complicaciones, ingresos hospitalarios necesarios, cambios de plan de manejo, progresos en el tratamiento y desenlace del paciente."
$comp_2 = "Nos comprometemos a cumplir estrictamente las instrucciones, tratamientos y recomendaciones impartidas por el personal tratante de IPS VISAL RT S.A.S.. Tenemos conocimiento y aceptamos que son por cuenta del paciente y su representante los elementos básicos para su higiene personal, su alimentación adecuada y, de la misma forma, cualquier otro insumo no cubierto por el POS, el cual suministraremos oportunamente de acuerdo con las necesidades del paciente."
$comp_3 = "Asimismo, entendemos que, como familiares y responsables del paciente, ""asumimos el compromiso de garantizar que el lugar de egreso hospitalario cumpla con condiciones mínimas de habitabilidad y seguridad, como lo son: acceso geográfico adecuado, disponibilidad de agua potable, energía eléctrica continua y un entorno que permita la atención segura en el domicilio. En caso de que el paciente sea trasladado a zonas rurales donde no se cumplen estos criterios, lo hacemos bajo nuestra responsabilidad, exonerando a la IPS de cualquier afectación derivada de la falta de condiciones aptas para la atención"""
$comp_4 = "Si el paciente requiere oxígeno domiciliario, ""nos comprometemos a tener un contacto efectivo para facilitar el seguimiento del equipo por parte del proveedor. De igual forma, informaremos oportunamente cuando ya no se requiera el equipo de oxígeno y nos responsabilizamos por la gestión de su devolución en buen estado, entendiendo que es un equipo en condición de alquiler."""
$comp_5 = "Al firmar el presente consentimiento, reconocemos que lo hemos leído o que nos ha sido leído y explicado, comprendiendo completamente su contenido. Se nos han dado amplias oportunidades de formular preguntas y todas las preguntas que hemos formulado han sido respondidas o explicadas en forma satisfactoria. Todos los espacios en blanco o frases por completar han sido llenados y todos los puntos en los que no estamos de acuerdo han sido marcados antes de firmar este documento."

$datos_personales = "TRATAMIENTO DE DATOS PERSONALES: Al suscribir el presente consentimiento EL USUARIO declara de manera libre, expresa, inequívoca e informada, que AUTORIZA a IPS VISAL RT SAS para que realice la recolección, almacenamiento, uso, circulación, actualización, supresión y en general, tratamiento de datos personales con el fin de lograr las finalidades relativas al objeto social de IPS VISAL RT SAS. De acuerdo a la ley 1581 de 2012"

# ================ Seccion Encabezado adicional ================
$secEncabezado = @{
    id = newId; type = "section"; label = "Encabezado"
    children = @(
        (Field "FECHA"              "fecha_ingreso_pad" "date" 4),
        (Field "SEXO"               "sexo_paciente"     "select" 4 @{ options = @("Masculino","Femenino","Otro"); defaultValue = "Masculino"; allowCustom = $true }),
        (Field "TELÉFONO"           "telefono_paciente" "text" 4),
        (Field "EPS"                "eps"               "text" 6),
        (Field "FECHA DE NACIMIENTO" "fecha_nacimiento"  "date" 6)
    )
}

# ================ Seccion Representante Legal ================
$secRepresentante = @{
    id = newId; type = "section"; label = "Representante Legal / Tutor"
    children = @(
        (Field "Nombre del representante (Señor/a)" "nombre_representante" "text" 8),
        (Field "C.C. representante"                 "cc_representante"     "text" 4),
        (P "mayor de edad, actuando en calidad de representante legal y/o tutor.")
    )
}

# ================ Seccion Declaracion Libre y Voluntaria ================
$secDeclaracion = @{
    id = newId; type = "section"; label = "DECLARO LIBRE Y VOLUNTARIAMENTE"
    children = @(
        (P $decl_1),
        (Field "Diagnósticos y pronóstico" "diagnosticos_pronostico" "textarea" 12 @{ enableVoice = $true; rows = 4 }),
        (P $decl_2_intro),
        (Field "Dirección de domicilio" "direccion_domicilio" "text" 8),
        (Field "Ciudad"                 "ciudad_domicilio"   "text" 4),
        (P $decl_2_cont),
        (P $decl_3)
    )
}

# ================ Seccion Compromisos del Familiar ================
$secCompromisos = @{
    id = newId; type = "section"; label = "COMPROMISOS DEL FAMILIAR Y/O CUIDADOR PRINCIPAL AL MOMENTO DEL EGRESO HOSPITALARIO E INGRESO AL PAD"
    children = @(
        (P $comp_1),
        (P $comp_2),
        (P $comp_3),
        (P $comp_4),
        (P $comp_5)
    )
}

# ================ Seccion Tratamiento de Datos Personales ================
$secDatos = @{
    id = newId; type = "section"; label = "TRATAMIENTO DE DATOS PERSONALES"
    children = @( (P $datos_personales) )
}

# ================ Seccion Cierre de firma ================
$secCierreFirma = @{
    id = newId; type = "section"; label = "Cierre de Firma"
    children = @(
        (P "Se firma a los ___ del mes ___ del año ___ de la ciudad de ___."),
        (Field "Día"    "cierre_dia"    "text" 2),
        (Field "Mes"    "cierre_mes"    "text" 3),
        (Field "Año"    "cierre_anio"   "text" 2),
        (Field "Ciudad" "cierre_ciudad" "text" 5)
    )
}

# ================ Seccion Firmas Manuscritas (Paciente / Acudiente / Profesional) ================
$secFirmasManu = @{
    id = newId; type = "section"; label = "Firmas Manuscritas"
    children = @(
        (Field "Nombre del Paciente"    "nombre_paciente_manu"   "text" 6),
        (Field "N° ID Paciente"         "id_paciente_manu"       "text" 3),
        (Field "Firma del Paciente"     "firma_paciente_manu"    "text" 3),
        (Field "Nombre del Acudiente"   "nombre_acudiente_manu"  "text" 6),
        (Field "N° ID Acudiente"        "id_acudiente_manu"      "text" 3),
        (Field "Firma del Acudiente"    "firma_acudiente_manu"   "text" 3),
        (Field "Nombre del Profesional" "nombre_profesional_manu" "text" 6),
        (Field "N° ID Profesional"      "id_profesional_manu"    "text" 3),
        (Field "Firma del Profesional"  "firma_profesional_manu" "text" 3)
    )
}

# ================ Ensamblar ================
$schema.children = @(
    $datosPaciente,
    $secEncabezado,
    $secRepresentante,
    $secDeclaracion,
    $secCompromisos,
    $secDatos,
    $secCierreFirma,
    $secFirmasManu,
    $firmasAuto,
    $cierre
)
Write-Host ("Top-level ahora: {0} secciones" -f $schema.children.Count) -ForegroundColor Green

$json = ($schema | ConvertTo-Json -Depth 30 -Compress)
$jsonSql = $json.Replace("'","''")
$now = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss.fffzzz")
$sql = "UPDATE form_definitions SET schema_json='$jsonSql'::jsonb, updated_at='$now' WHERE codigo='PP-FO-35' AND tenant_id='$TenantId';"
$tmp = [System.IO.Path]::GetTempFileName()
[System.IO.File]::WriteAllText($tmp, $sql, [System.Text.UTF8Encoding]::new($false))
try {
    $copy = "/tmp/visal_pp35_$([Guid]::NewGuid().ToString('N')).sql"
    docker cp $tmp "${PgContainer}:${copy}" | Out-Null
    $env:MSYS_NO_PATHCONV = "1"
    $r = docker exec $PgContainer psql -U $PgUser -d $PgDb -v ON_ERROR_STOP=1 -f $copy 2>&1
    $exit = $LASTEXITCODE
    docker exec $PgContainer rm $copy 2>$null | Out-Null
    $env:MSYS_NO_PATHCONV = $null
    if ($exit -ne 0) { throw "psql fallo ($exit): $($r -join ' | ')" }
} finally { Remove-Item $tmp -ErrorAction SilentlyContinue }

Write-Host "OK PP-FO-35 actualizado." -ForegroundColor Green
