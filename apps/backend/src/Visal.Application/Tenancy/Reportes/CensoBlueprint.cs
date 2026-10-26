namespace Visal.Application.Tenancy.Reportes;

/// <summary>
/// Plano del reporte CENSO (Ola 4) definido en codigo: materializa el Excel multi-hoja
/// canonico (Censo 2026) como una plantilla lista para el tenant. Reusa el modelo semantico
/// y el motor; solo referencia campos/entidades que YA existen en el catalogo. El tenant lo
/// crea de un clic ("Plantilla CENSO 2026") y luego lo edita/clona como cualquier reporte.
///
/// Cubre los arquetipos del censo:
///  1. Base de Pacientes (detalle)
///  2. Ingreso de Pacientes (detalle con cruce a paciente; bitacora con SLA en minutos)
///  3. Satisfaccion de Usuarios SIAU (detalle con cruce a paciente)
///  4. Indice de Morbilidad (agregado: conteo + % del total, ordenado)
///  5. Consolidado Encuesta (agregado + pivote por mes)
///
/// La hoja "Ingreso de Pacientes" se llena desde la bitacora (modulo Bitacora de Ingresos,
/// Ola 4b); mientras no haya registros capturados, sale vacia.
/// </summary>
public static class CensoBlueprint
{
    public const string Nombre = "Censo 2026";

    public static GuardarPlantillaRequest Crear()
    {
        var hojas = new List<ReporteHojaDef>
        {
            BasePacientes(),
            IngresoPacientes(),
            Satisfaccion(),
            IndiceMorbilidad(),
            ConsolidadoEncuesta(),
        };

        return new GuardarPlantillaRequest(
            Nombre,
            Descripcion: "Censo institucional multi-hoja: base de pacientes, satisfaccion SIAU, indice de morbilidad y consolidado de encuesta.",
            Categoria: "Censo",
            Orden: 0,
            Activo: true,
            Hojas: hojas,
            MostrarEncabezado: true,
            Codigo: "PP-FO-CENSO",
            Version: "1",
            Subtitulo: "Censo 2026");
    }

    private static ReporteHojaDef BasePacientes() =>
        ReporteHojaDef.Detalle("Base de Pacientes", "paciente",
            columnas:
            [
                new("numero_documento", "ID"),
                new("tipo_documento"),
                new("nombre_completo"),
                new("fecha_nacimiento"),
                new("edad"),
                new("sexo"),
                new("estado"),
                new("regimen"),
                new("fecha_ingreso"),
                new("fecha_egreso"),
                new("cie10"),
                new("diagnostico_principal"),
                new("cie10_2"),
                new("diagnostico_secundario"),
                new("tutela"),
                new("direccion"),
                new("barrio"),
                new("ciudad"),
                new("telefono"),
                new("email"),
                new("fecha_reingreso"),
                new("observacion_reingreso"),
                new("fecha_hospitalizacion"),
                new("observaciones_hospitalizacion"),
                new("observaciones_paciente"),
            ],
            orden: [new("nombre_completo")]);

    private static ReporteHojaDef IngresoPacientes() =>
        ReporteHojaDef.Detalle("Ingreso de Pacientes", "ingreso_paciente",
            columnas:
            [
                new("paciente.numero_documento", "ID Paciente"),
                new("paciente.nombre_completo", "Paciente"),
                new("fecha_comentan"),
                new("fecha_respuesta"),
                new("sla_minutos"),
                new("ips_comenta"),
                new("responsable"),
                new("ingreso"),
                new("motivo_no_ingreso"),
                new("observaciones"),
            ],
            orden: [new("fecha_comentan")]);

    private static ReporteHojaDef Satisfaccion() =>
        ReporteHojaDef.Detalle("Satisfaccion de Usuarios", "encuesta_satisfaccion",
            columnas:
            [
                new("mes"),
                new("fecha_llamada"),
                new("responsable_llamada"),
                new("paciente.numero_documento", "ID Paciente"),
                new("paciente.nombre_completo", "Paciente"),
                new("pregunta1"),
                new("pregunta2"),
                new("pregunta3"),
                new("pregunta4"),
                new("pregunta5"),
                new("persona_atiende"),
                new("observaciones"),
            ],
            orden: [new("mes")]);

    private static ReporteHojaDef IndiceMorbilidad() =>
        ReporteHojaDef.Agregado("Indice Morbilidad", "paciente",
            groupBy:
            [
                new("cie10", "CIE10"),
                new("diagnostico_principal", "Diagnostico"),
            ],
            agregados:
            [
                new(ReporteAgregadoFunc.Count, null, "Cant Px's"),
                new(ReporteAgregadoFunc.PctTotal, null, "% del total"),
            ],
            orden: [new("Cant Px's", Desc: true)]);

    private static ReporteHojaDef ConsolidadoEncuesta() =>
        ReporteHojaDef.Agregado("Consolidado Encuesta", "encuesta_satisfaccion",
            groupBy:
            [
                new("pregunta4", "P4 Experiencia global"),
                new("mes", "Mes"),
            ],
            agregados:
            [
                new(ReporteAgregadoFunc.Count, null, "Cantidad"),
            ],
            pivote: new ReportePivoteDef("pregunta4", "mes"));
}
