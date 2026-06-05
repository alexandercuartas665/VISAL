# Insert-Enfermeria.ps1
# Escala Visal de medicion de requerimiento de enfermeria.
# 15 items con calificacion 0/1/2 (o 0/1 segun rango) y ponderador individual.
# TOTAL = suma(calificacion * ponderado).
# Interpretacion: define las horas de CBE (Cuidado Basico de Enfermeria).

[CmdletBinding()]
param(
    [string]$SourceFile = "",
    [string]$ProcessedDir = "C:\Users\acuartas\OneDrive - Bitcode IT Services S.A.S\Bitcode\13. Proyectos\028. Visal\01. Recursos\FORMATO_PENDIENTES\PROCESADO",
    [string]$TenantId = "019e6b0a-a4d8-70d6-a343-d307ebd24b15"
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_Escalas-Helpers.ps1")

# Resolver path con tilde (path con caracteres especiales)
if (-not $SourceFile) {
    $found = Get-ChildItem "C:\Users\acuartas\OneDrive - Bitcode IT Services S.A.S\Bitcode\13. Proyectos\028. Visal\01. Recursos\FORMATO_PENDIENTES\" -Filter "*ENFERM*" -File | Select-Object -First 1
    if ($found) { $SourceFile = $found.FullName }
}

# Tres tipos de opciones (0/1/2 y 0/1) - presentados como select.
$opt012 = @("0 - No / no aplica", "1 - Parcialmente", "2 - Si / requerimiento alto")
$opt01  = @("0 - No / no aplica", "1 - Si")

$items = @(
    # name, label, options, ponderado, categoria
    @{ name="enf_dependencia";        cat="ACTIVIDADES BASICAS";    label="Paciente dependiente (Barthel)";                                                          opts=$opt012; pond=0.5 },
    @{ name="enf_braden";             cat="ACTIVIDADES BASICAS";    label="Prevencion de ulceras por presion (Braden)";                                              opts=$opt012; pond=0.5 },

    @{ name="enf_ventil_invasiva";    cat="APOYO VENTILATORIO";     label="Soporte ventilatorio modalidad invasiva";                                                 opts=$opt012; pond=2.0 },
    @{ name="enf_traqueostomia";      cat="APOYO VENTILATORIO";     label="Cuidados de traqueostomia";                                                               opts=$opt01;  pond=0.3 },
    @{ name="enf_aspiracion";         cat="APOYO VENTILATORIO";     label="Aspiracion de secreciones (via diferente a traqueostomia)";                                opts=$opt01;  pond=0.3 },

    @{ name="enf_cateter";            cat="CIRCULACION";            label="Manejo de cateter PICC, subclavio - monitoreo permanente";                                opts=$opt01;  pond=0.3 },

    @{ name="enf_parenterales";       cat="INTERVENCIONES";         label="Suministro de medicamentos parenterales y LEV";                                            opts=$opt01;  pond=0.3 },
    @{ name="enf_abdomen_heridas";    cat="INTERVENCIONES";         label="Abdomen abierto o heridas con alta produccion de exudado / fistula de alto gasto";       opts=$opt01;  pond=0.5 },
    @{ name="enf_patologia_descomp";  cat="INTERVENCIONES";         label="Patologia base descompensada (IAM reciente / DM / EPOC / IRC / HTA)";                     opts=$opt012; pond=0.3 },
    @{ name="enf_hospitalizaciones";  cat="INTERVENCIONES";         label="Ingreso a hospitalizacion / urgencias 3+ veces ultimo trimestre o ultima hospitalizacion mayor a 30 dias continuos"; opts=$opt01; pond=0.4 },
    @{ name="enf_dolor";              cat="INTERVENCIONES";         label="Medicamentos de control del dolor cada 4 horas o mas con dosis de rescate";              opts=$opt01;  pond=0.3 },
    @{ name="enf_morse";              cat="INTERVENCIONES";         label="Puntuacion total escala de Morse (riesgo de caidas) alta";                                opts=$opt01;  pond=0.3 },
    @{ name="enf_enteral";            cat="INTERVENCIONES";         label="Alimentacion por via enteral - uso de bomba de infusion continua";                        opts=$opt01;  pond=0.3 },
    @{ name="enf_fractura_cadera";    cat="INTERVENCIONES";         label="Fractura de cadera inferior a 3 meses (diligenciar fecha del evento/cirugia)";           opts=$opt01;  pond=0.2 },
    @{ name="enf_demencial";          cat="INTERVENCIONES";         label="Trastorno demencial";                                                                     opts=$opt01;  pond=0.2 }
)

# Construir nodos agrupando por categoria con sub-heading
$prev = ""
$children = @(
    @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Califique cada item segun el estado actual del paciente. Cada item tiene un ponderador clinico fijo. El TOTAL se calcula automaticamente y determina las horas de CBE (Cuidado Basico de Enfermeria) requeridas." }
)
foreach ($it in $items) {
    if ($it.cat -ne $prev) {
        $children += @{ id = newId; type = "text"; textStyle = "subheading"; content = "$($it.cat) (ponderador clinico fijo)" }
        $prev = $it.cat
    }
    $lbl = "$($it.label)  -  ponderador: $($it.pond)"
    $children += (New-Select $lbl $it.name $it.opts)
}

# Formula sumprod(item1:pond1, item2:pond2, ...)
$sumprodParts = ($items | ForEach-Object { "$($_.name):$($_.pond.ToString([System.Globalization.CultureInfo]::InvariantCulture))" }) -join ", "
$sumprodFormula = "sumprod($sumprodParts)"

$schema = @{
    header = Build-Header "ESCALA DE MEDICION DE REQUERIMIENTO DE ENFERMERIA"
    children = @(
        (New-PacienteSection),
        (New-Section "ESCALA DE REQUERIMIENTO DE ENFERMERIA" $children),
        (New-Section "RESULTADO" @(
            (New-Calculated "TOTAL ponderado" "enf_total" $sumprodFormula 4),
            (New-Calculated "Horas de CBE sugeridas" "enf_horas" `
                'cases(enf_total, "0-2.99=No requiere CBE;3.0-4.99=8 horas de CBE;5.0-7.49=12 horas de CBE;7.5-99=24 horas de CBE")' 8),
            @{ id = newId; type = "text"; textStyle = "subheading"; content = "Referencias: >=7.5 -> 24h CBE | 5.0-7.4 -> 12h CBE | 3.0-4.9 -> 8h CBE | <3.0 -> No requiere" }
        )),
        (New-Section "ACEPTACION" @(
            (New-Select "Aceptacion del cuidador / acudiente" "enf_aceptacion" @("SI","NO") 4 $true),
            @{ id = newId; type = "field"; fieldType = "text"; label = "Nombre del cuidador / acudiente"; name = "enf_cuidador"; widthColumns = 8 }
        )),
        (New-FirmaSection)
    )
}

Save-FormDefinition -Codigo "PP-FO-53-ESCALA-ENFERMERIA" `
    -Nombre "Escala Visal - Medicion de requerimiento de enfermeria" `
    -Version "2.0" -Tipo "ESCALAS" -Schema $schema -TenantId $TenantId | Out-Null

if ($SourceFile -and (Test-Path $SourceFile)) {
    Move-Origin -SourceFile $SourceFile -ProcessedDir $ProcessedDir
}
