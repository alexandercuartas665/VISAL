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

        // Parametros de ejecucion (globales): se piden al descargar y se aplican a TODAS las
        // hojas via los filtros enlazados por ParamClave. Si se dejan vacios, esa hoja no se
        // filtra por ese parametro (censo completo).
        var parametros = new List<ReporteParametroDef>
        {
            new("sede", "Sede", ReporteParametroTipo.Lista, ReporteParametroFuente.Sedes),
            new("rango_fecha", "Rango de fechas (inicial / final)", ReporteParametroTipo.RangoFecha),
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
            Subtitulo: "Censo 2026",
            Parametros: parametros);
    }

    // Claves de los parametros de ejecucion (una sola fuente de verdad para los filtros).
    private const string ParamSede = "sede";
    private const string ParamRangoFecha = "rango_fecha";

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
            filtros:
            [
                new("sede_id", ReporteFiltroOp.Eq, ParamClave: ParamSede),
                new("fecha_ingreso", ReporteFiltroOp.Between, ParamClave: ParamRangoFecha),
            ],
            orden: [new("nombre_completo")]);

    // Hoja "Ingreso de Pacientes": sale de la ADMISION (entidad paciente), no de la bitacora.
    // Mapea 1:1 las columnas del Excel canonico (A..AB) usando los campos del paciente, los cruces
    // (IPS, clasificacion, departamento de residencia) y campos DERIVADOS de las dos fechas (mes,
    // hora y minuto de "comentan" y de "respuesta"), que el generador calcula en hora local sin
    // columnas nuevas. K "Departamento" (operativo: Valle/Cauca) viene del cruce a la sede
    // (Sucursal.Departamento). Q "Observaciones de Motivo de NO Ingreso" del Excel es el mismo
    // campo que P en nuestro modelo (un solo motivo/observacion = Paciente.MotivoNoIngreso), por
    // eso no se duplica como columna aparte.
    private static ReporteHojaDef IngresoPacientes() =>
        ReporteHojaDef.Detalle("Ingreso de Pacientes", "paciente",
            columnas:
            [
                new("mes_comentan", "Mes"),                       // A
                new("sla_ingreso_minutos", "Mins"),              // B
                new("fecha_comentan", "Fecha Comentan Paciente"), // C
                new("comentan_hora", "Comentan (24 H)"),         // D
                new("comentan_minuto", "Comentan (Minutos)"),    // E
                new("fecha_atencion", "Fecha Respuesta"),        // F
                new("respuesta_hora", "Respuesta (24 H)"),       // G
                new("respuesta_minuto", "Respuesta (Minutos)"),  // H
                new("ips_comenta.nombre", "IPS que comenta"),    // I
                new("usuario_ingreso", "Responsable del Ingreso"), // J
                new("sede.departamento", "Departamento"),        // K (operativo: Valle/Cauca)
                new("contrato_principal", "Contrato"),           // L
                new("clasif_paciente.nombre", "Clasificacion"),  // M
                new("condicion", "Condicion"),                   // N
                new("ingreso", "Ingreso"),                       // O
                new("motivo_no_ingreso", "Motivo de no ingreso"), // P
                new("tipo_documento", "Tipo ID"),                // R
                new("numero_documento", "ID"),                   // S
                new("primer_apellido", "Primer Apellido"),       // T
                new("segundo_apellido", "Segundo Apellido"),     // U
                new("primer_nombre", "Primer Nombre"),           // V
                new("segundo_nombre", "Segundo Nombre"),         // W
                new("direccion", "Direccion"),                   // X
                new("barrio", "Barrio"),                         // Y
                new("telefono", "Telefono Paciente"),            // Z
                new("departamento.nombre", "Departamento"),      // AA
                new("ciudad", "Ciudad"),                         // AB
            ],
            filtros:
            [
                new("sede_id", ReporteFiltroOp.Eq, ParamClave: ParamSede),
                new("fecha_comentan", ReporteFiltroOp.Between, ParamClave: ParamRangoFecha),
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
            filtros:
            [
                new("paciente.sede_id", ReporteFiltroOp.Eq, ParamClave: ParamSede),
                new("fecha_llamada", ReporteFiltroOp.Between, ParamClave: ParamRangoFecha),
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
            filtros:
            [
                new("sede_id", ReporteFiltroOp.Eq, ParamClave: ParamSede),
                new("fecha_ingreso", ReporteFiltroOp.Between, ParamClave: ParamRangoFecha),
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
            filtros:
            [
                new("paciente.sede_id", ReporteFiltroOp.Eq, ParamClave: ParamSede),
                new("fecha_llamada", ReporteFiltroOp.Between, ParamClave: ParamRangoFecha),
            ],
            pivote: new ReportePivoteDef("pregunta4", "mes"));
}
