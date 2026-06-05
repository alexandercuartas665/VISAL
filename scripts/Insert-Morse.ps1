# Insert-Morse.ps1
# Escala Morse de riesgo de caidas (6 items, 0-125 puntos).
# Items y puntajes segun la escala original validada (referencia OMS / MFS Morse 1989).

[CmdletBinding()]
param(
    [string]$SourceFile = "C:\Users\acuartas\OneDrive - Bitcode IT Services S.A.S\Bitcode\13. Proyectos\028. Visal\01. Recursos\FORMATO_PENDIENTES\PP-FO-50 ESCALA DE RIESGO DE CAIDAS MORSE v2.docx",
    [string]$ProcessedDir = "C:\Users\acuartas\OneDrive - Bitcode IT Services S.A.S\Bitcode\13. Proyectos\028. Visal\01. Recursos\FORMATO_PENDIENTES\PROCESADO",
    [string]$TenantId = "019e6b0a-a4d8-70d6-a343-d307ebd24b15"
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_Escalas-Helpers.ps1")

$items = @(
    (New-Select "1. Historia de caidas (en los ultimos 3 meses)" "morse_historia" @(
        "0 - No",
        "25 - Si, ha tenido caidas recientes"
    )),
    (New-Select "2. Diagnostico secundario (presencia de mas de un diagnostico)" "morse_diagnostico" @(
        "0 - No",
        "15 - Si"
    )),
    (New-Select "3. Ayuda para deambular" "morse_ayuda" @(
        "0 - Ninguna / reposo en cama / silla de ruedas / asistencia de enfermeria",
        "15 - Muletas / baston / andador",
        "30 - Se apoya en los muebles para deambular"
    )),
    (New-Select "4. Terapia intravenosa (acceso venoso periferico o central)" "morse_terapia_iv" @(
        "0 - No",
        "20 - Si"
    )),
    (New-Select "5. Forma de caminar / transferencia" "morse_marcha" @(
        "0 - Normal / inmovil (reposo en cama)",
        "10 - Debil (no se sostiene firmemente)",
        "20 - Alterada (balanceada, cabeza inclinada, requiere apoyo)"
    )),
    (New-Select "6. Estado mental" "morse_mental" @(
        "0 - Conoce sus limitaciones y orientado",
        "15 - Sobreestima sus capacidades / olvida limitaciones"
    ))
)

$schema = @{
    header = Build-Header "ESCALA DE MORSE - Riesgo de caidas"
    children = @(
        (New-PacienteSection),
        (New-Section "INDICE DE MORSE" (@(
            @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Seleccione la opcion que corresponda al estado actual del paciente para cada item. El puntaje total y el nivel de riesgo se calculan automaticamente." }
        ) + $items)),
        (New-Section "RESULTADO" @(
            (New-Calculated "TOTAL Morse (0 - 125)" "morse_total" `
                "sum(morse_historia, morse_diagnostico, morse_ayuda, morse_terapia_iv, morse_marcha, morse_mental)" 4),
            (New-Calculated "Nivel de riesgo (automatico)" "morse_nivel" `
                'cases(morse_total, "0-24=SIN RIESGO;25-50=RIESGO BAJO;51-125=RIESGO ALTO")' 4),
            (New-Calculated "Accion clinica sugerida" "morse_accion" `
                'cases(morse_total, "0-24=Cuidados basicos y educacion a cuidadores primarios;25-50=Implementar plan de prevencion de caidas estandar;51-125=Implementar medidas especiales (acompanante, barandas, supervision continua)")' 12),
            @{ id = newId; type = "text"; textStyle = "subheading"; content = "Referencias: 0-24 Sin riesgo | 25-50 Bajo | >=51 Alto" }
        )),
        (New-FirmaSection)
    )
}

Save-FormDefinition -Codigo "PP-FO-50-ESCALA-MORSE" `
    -Nombre "Escala Morse - Riesgo de caidas" `
    -Version "1.0" -Tipo "ESCALAS" -Schema $schema -TenantId $TenantId | Out-Null

Move-Origin -SourceFile $SourceFile -ProcessedDir $ProcessedDir
