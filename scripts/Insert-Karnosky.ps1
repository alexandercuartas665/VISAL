# Insert-Karnosky.ps1
# Escala Karnofsky (KPS) - Capacidad funcional del paciente.
# Una sola dimension: nivel global de 0 (muerto) a 100 (normal sin sintomas).
# Tradicionalmente NO se suma: es un valor unico elegido por el clinico.

[CmdletBinding()]
param(
    [string]$TenantId = "019e6b0a-a4d8-70d6-a343-d307ebd24b15"
)
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "_Escalas-Helpers.ps1")

$opts = @(
    "100 - Normal. Sin quejas. Sin evidencia de enfermedad.",
    "90 - Capaz de actividad normal. Signos o sintomas menores de enfermedad.",
    "80 - Actividad normal con esfuerzo. Algunos signos o sintomas de enfermedad.",
    "70 - Se cuida solo. Incapaz de actividad normal o trabajo activo.",
    "60 - Requiere ocasional asistencia, pero capaz de cuidarse en la mayoria de sus necesidades.",
    "50 - Requiere asistencia considerable y cuidado medico frecuente.",
    "40 - Discapacitado. Requiere cuidado y asistencia especial.",
    "30 - Severamente discapacitado. Hospitalizacion indicada aunque la muerte no sea inminente.",
    "20 - Muy enfermo. Hospitalizacion necesaria. Tratamiento de soporte activo necesario.",
    "10 - Moribundo. Procesos fatales progresando rapidamente.",
    "0 - Muerto"
)

$schema = @{
    header = Build-Header "ESCALA DE KARNOFSKY (KPS) - Capacidad funcional"
    children = @(
        (New-PacienteSection),
        (New-Section "INDICE DE KARNOFSKY" @(
            @{ id = newId; type = "text"; textStyle = "paragraph"; content = "Seleccione UNA opcion que mejor describa la capacidad funcional global del paciente. La escala va de 100 (capacidad funcional normal sin sintomas) a 0 (muerto). La clasificacion clinica se calcula automaticamente." },
            (New-Select "Nivel funcional Karnofsky" "karnofsky_nivel" $opts 12),
            (New-Calculated "Puntaje KPS" "karnofsky_puntaje" `
                "sum(karnofsky_nivel)" 4),
            (New-Calculated "Clasificacion clinica" "karnofsky_clasificacion" `
                'cases(karnofsky_puntaje, "0-39=Incapaz de cuidarse. Requiere hospitalizacion o equivalente;40-69=Incapaz de trabajar. Asistencia variable necesaria;70-100=Capaz de llevar actividad normal y de trabajar. No requiere cuidados especiales")' 8)
        )),
        (New-FirmaSection)
    )
}

Save-FormDefinition -Codigo "PP-FO-92-ESCALA-KARNOFSKY" `
    -Nombre "Escala Karnofsky (KPS) - Capacidad funcional" `
    -Version "1.0" -Tipo "ESCALAS" -Schema $schema -TenantId $TenantId | Out-Null
