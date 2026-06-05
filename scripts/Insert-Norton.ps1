# Insert-Norton.ps1
# Escala de Norton (modificada por INSALUD) - Riesgo de ulceras por presion.
# 5 items con 4 opciones cada uno (1 a 4 puntos). Total 5-20.
# A MENOR puntaje, MAYOR riesgo (rangos invertidos).

[CmdletBinding()]
param(
    [string]$SourceFile = "C:\Users\acuartas\OneDrive - Bitcode IT Services S.A.S\Bitcode\13. Proyectos\028. Visal\01. Recursos\FORMATO_PENDIENTES\PP-FO-59 FORMATO DE ESCALA DE NORTON.docx",
    [string]$ProcessedDir = "C:\Users\acuartas\OneDrive - Bitcode IT Services S.A.S\Bitcode\13. Proyectos\028. Visal\01. Recursos\FORMATO_PENDIENTES\PROCESADO",
    [string]$TenantId = "019e6b0a-a4d8-70d6-a343-d307ebd24b15"
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_Escalas-Helpers.ps1")

$items = @(
    (New-Select "Estado fisico general" "norton_fisico" @(
        "4 - Bueno",
        "3 - Mediano",
        "2 - Regular",
        "1 - Muy malo"
    )),
    (New-Select "Estado mental" "norton_mental" @(
        "4 - Alerta",
        "3 - Apatico",
        "2 - Confuso",
        "1 - Estuporoso / comatoso"
    )),
    (New-Select "Actividad" "norton_actividad" @(
        "4 - Ambulante",
        "3 - Disminuida",
        "2 - Muy limitada",
        "1 - Inmovil"
    )),
    (New-Select "Movilidad" "norton_movilidad" @(
        "4 - Total",
        "3 - Camina con ayuda",
        "2 - Sentado",
        "1 - Encamado"
    )),
    (New-Select "Incontinencia" "norton_incontinencia" @(
        "4 - Ninguna",
        "3 - Ocasional",
        "2 - Urinaria o fecal",
        "1 - Urinaria y fecal"
    ))
)

$schema = @{
    header = Build-Header "ESCALA DE NORTON (modificada por INSALUD) - Riesgo de UPP"
    children = @(
        (New-PacienteSection),
        (New-Section "INDICE DE NORTON" (@(
            @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Seleccione la opcion que mejor describe el estado actual del paciente. El puntaje total y el nivel de riesgo se calculan automaticamente. ATENCION: a MENOR puntaje, MAYOR riesgo de ulcera por presion (UPP)." }
        ) + $items)),
        (New-Section "RESULTADO" @(
            (New-Calculated "TOTAL Norton (5 - 20)" "norton_total" `
                "sum(norton_fisico, norton_mental, norton_actividad, norton_movilidad, norton_incontinencia)" 4),
            (New-Calculated "Nivel de riesgo (automatico)" "norton_nivel" `
                'cases(norton_total, "5-9=RIESGO MUY ALTO DE PADECER UPP;10-12=RIESGO ALTO DE PADECER UPP;13-14=RIESGO MEDIO DE PADECER UPP;15-20=RIESGO MINIMO O NO RIESGO")' 8),
            @{ id = newId; type = "text"; textStyle = "subheading"; content = "Referencias: 5-9 Riesgo muy alto | 10-12 Alto | 13-14 Medio | >14 Minimo" }
        )),
        (New-FirmaSection)
    )
}

Save-FormDefinition -Codigo "PP-FO-59-ESCALA-NORTON" `
    -Nombre "Escala Norton (INSALUD) - Riesgo de UPP" `
    -Version "1.0" -Tipo "ESCALAS" -Schema $schema -TenantId $TenantId | Out-Null

Move-Origin -SourceFile $SourceFile -ProcessedDir $ProcessedDir
