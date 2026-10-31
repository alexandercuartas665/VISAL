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
            // "Ingreso de Pacientes" va primera para coincidir con la primera pestana del Excel.
            IngresoPacientes(),
            BasePacientes(),
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

    // Hoja "Base de Pacientes": mapea 1:1 las 36 columnas del Excel canonico (A..AJ). El Excel la
    // divide en 3 pestanas por region (Valle/Cauca/Popayan); aqui es UNA sola hoja filtrable por la
    // Sede (y su Departamento operativo) via el parametro de ejecucion. Columnas derivadas/denormalizadas:
    //  - C Mes Ingreso = ano-mes de Fecha Ingreso (derivado, hora local, sin columna nueva).
    //  - Q Puntaje Barthel = BarthelTotal (denormalizado desde la escala PP-FO-46 al cerrarla).
    //  - V Medicamento Controlado = campo SI/NO de la ficha tecnica.
    //  - AC Nombre Familiar Responsable = ContactoEmergencia (contacto principal del paciente).
    private static ReporteHojaDef BasePacientes() =>
        ReporteHojaDef.Detalle("Base de Pacientes", "paciente",
            columnas:
            [
                new("numero_documento", "ID"),                   // A
                new("fecha_ingreso", "Fecha Ingreso"),           // B
                new("mes_ingreso", "Mes Ingreso"),               // C (derivado)
                new("fecha_egreso", "Fecha De Egreso"),          // D
                new("estado", "Estado Paciente"),                // E
                new("contrato_principal", "Contrato"),           // F
                new("clasif_paciente.nombre", "Clasificacion"),  // G
                new("condicion", "Condicion"),                   // H
                new("tipo_documento", "Tipo ID"),                // I
                new("primer_apellido", "Primer Apellido"),       // J
                new("segundo_apellido", "Segundo Apellido"),     // K
                new("primer_nombre", "Primer Nombre"),           // L
                new("segundo_nombre", "Segundo Nombre"),         // M
                new("edad", "Edad"),                             // N
                new("fecha_nacimiento", "Fecha de Nacimiento"),  // O
                new("sexo", "Sexo"),                             // P
                new("puntaje_barthel", "Puntaje Barthel"),       // Q (denormalizado)
                new("cie10", "CIE10"),                           // R
                new("diagnostico_principal", "Diagnostico principal"), // S
                new("cie10_2", "CIE10 - 2"),                     // T
                new("diagnostico_secundario", "Diagnostico Secundario"), // U
                new("med_controlado", "Medicamento Controlado"), // V
                new("tutela", "Tutela"),                         // W
                new("direccion", "Direccion"),                   // X
                new("barrio", "Barrio"),                         // Y
                new("telefono", "Telefono Paciente"),            // Z
                new("departamento.nombre", "Departamento"),      // AA (residencia)
                new("ciudad", "Ciudad"),                         // AB
                new("contacto_emergencia", "Nombre Familiar Responsable"), // AC
                new("email", "Correo Electronico"),              // AD
                new("regimen", "Regimen de Afiliacion"),         // AE
                new("fecha_reingreso", "Fecha Reingreso"),       // AF
                new("observacion_reingreso", "Observacion Reingreso"), // AG
                new("fecha_hospitalizacion", "Fecha de Hospitalizacion"), // AH
                new("observaciones_hospitalizacion", "Observaciones Hospitalizacion"), // AI
                new("observaciones_paciente", "Observaciones"),  // AJ
            ],
            filtros:
            [
                new("sede_id", ReporteFiltroOp.Eq, ParamClave: ParamSede),
                new("fecha_ingreso", ReporteFiltroOp.Between, ParamClave: ParamRangoFecha),
            ],
            orden: [new("primer_apellido"), new("primer_nombre")]);

    // Hoja "Ingreso de Pacientes": sale de la ADMISION (entidad paciente), no de la bitacora.
    // Mapea 1:1 las columnas del Excel canonico (A..AB) usando los campos del paciente, los cruces
    // (IPS, clasificacion, departamento de residencia) y campos DERIVADOS de las dos fechas (mes,
    // hora y minuto de "comentan" y de "respuesta"), que el generador calcula en hora local sin
    // columnas nuevas. K "Departamento" (operativo: Valle/Cauca) viene del cruce a la sede
    // (Sucursal.Departamento). P "Motivo de no ingreso" y Q "Observaciones de Motivo de NO
    // Ingreso" apuntan al MISMO campo (Paciente.MotivoNoIngreso = el motivo que se captura cuando
    // Ingreso=NO); se muestran las dos columnas para igualar el Excel (28 cols A..AB).
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
                new("motivo_no_ingreso", "Observaciones de Motivo de NO Ingreso"), // Q (mismo campo)
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

    // Hoja "Satisfaccion de Usuarios" (encuesta SIAU): mapea 1:1 las 18 columnas del Excel (A..R).
    // Los datos del paciente (A..H) salen del cruce a paciente; la encuesta (I..R) de la propia
    // entidad. "Mes" es el numero de la encuesta (en el Excel se ve como texto "Marzo 2026").
    private static ReporteHojaDef Satisfaccion() =>
        ReporteHojaDef.Detalle("Satisfaccion de Usuarios", "encuesta_satisfaccion",
            columnas:
            [
                new("paciente.numero_documento", "ID"),              // A
                new("paciente.tipo_documento", "Tipo ID"),           // B
                new("paciente.primer_apellido", "Primer Apellido"),  // C
                new("paciente.segundo_apellido", "Segundo Apellido"),// D
                new("paciente.primer_nombre", "Primer Nombre"),      // E
                new("paciente.segundo_nombre", "Segundo Nombre"),    // F
                new("paciente.contrato_principal", "Contrato"),      // G
                new("paciente.telefono", "Telefono Paciente"),       // H
                new("mes", "Mes"),                                   // I
                new("fecha_llamada", "FECHA DE LLAMADA SIAU"),       // J
                new("responsable_llamada", "RESPONSABLE LLAMADA SIAU"), // K
                new("pregunta1", "1. El profesional se presento oportunamente"), // L
                new("pregunta2", "2. Los profesionales se presentan de forma adecuada"), // M
                new("pregunta3", "3. Los profesionales dan indicaciones"), // N
                new("pregunta4", "4. Como calificaria su experiencia global"), // O
                new("pregunta5", "5. Recomendaria a VISAL RT S.A.S a un familiar"), // P
                new("persona_atiende", "PERSONA QUIEN ATIENDE LA LLAMADA"), // Q
                new("observaciones", "OBSERVACIONES LLAMADA"),       // R
            ],
            filtros:
            [
                new("paciente.sede_id", ReporteFiltroOp.Eq, ParamClave: ParamSede),
                new("fecha_llamada", ReporteFiltroOp.Between, ParamClave: ParamRangoFecha),
            ],
            orden: [new("mes")]);

    // Hoja "Indice Morbilidad": 1:1 con el Excel (3 columnas): CIE10, Diagnostico principal y Cant Px's,
    // ordenada descendente por conteo. El Excel la divide por region; aqui es una sola, filtrable por Sede.
    private static ReporteHojaDef IndiceMorbilidad() =>
        ReporteHojaDef.Agregado("Indice Morbilidad", "paciente",
            groupBy:
            [
                new("cie10", "CIE10"),
                new("diagnostico_principal", "Diagnostico principal"),
            ],
            agregados:
            [
                new(ReporteAgregadoFunc.Count, null, "Cant Px's"),
            ],
            filtros:
            [
                new("sede_id", ReporteFiltroOp.Eq, ParamClave: ParamSede),
                new("fecha_ingreso", ReporteFiltroOp.Between, ParamClave: ParamRangoFecha),
            ],
            orden: [new("Cant Px's", Desc: true)]);

    // Hoja "Consolidado Encuesta": MATRIZ fiel al Excel canonico (A..V). El pivote generico no puede
    // expresar dos bloques de pregunta con conteo + % por respuesta mas el total de llamadas efectivas
    // por mes, asi que esta hoja usa un constructor dedicado (ReporteMatrizEncuesta): la consulta base
    // trae las filas crudas (mes, pregunta4, pregunta5) ya acotadas a llamadas EFECTIVAS (Estado
    // "Realizada") y el reshaper arma, por mes, el bloque P4 (calificacion 5..1), el bloque P5
    // (recomendacion, textual) y la columna "Llamadas Efectivas". El % es conteo/total del mes.
    private static ReporteHojaDef ConsolidadoEncuesta() =>
        ReporteHojaDef.Detalle("Consolidado Encuesta", "encuesta_satisfaccion",
            columnas:
            [
                new("mes", "Mes"),                       // idx 0
                new("pregunta4", "P4"),                  // idx 1
                new("pregunta5", "P5"),                  // idx 2
            ],
            filtros:
            [
                new("estado", ReporteFiltroOp.Eq, ["Realizada"]),
                new("paciente.sede_id", ReporteFiltroOp.Eq, ParamClave: ParamSede),
                new("fecha_llamada", ReporteFiltroOp.Between, ParamClave: ParamRangoFecha),
            ],
            orden: [new("mes")]) with
        {
            Matriz = new ReporteMatrizEncuestaDef(
                ColMes: 0, ColP4: 1, ColP5: 2,
                TituloEsquina: "Calificacion",
                TituloMes: "Mes",
                BloqueP4: new ReporteMatrizBloqueDef(
                    "4. Como calificaria su experiencia global",
                    [new(5, "5"), new(4, "4"), new(3, "3"), new(2, "2"), new(1, "1")]),
                BloqueP5: new ReporteMatrizBloqueDef(
                    "5. Recomendaria a VISAL RT S.A.S a un familiar",
                    [
                        new(5, "Definitivamente Si"),
                        new(4, "Probablemente Si"),
                        new(3, "Ni Probable Ni Improbable"),
                        new(2, "Probablemente No"),
                        new(1, "Definitivamente NO"),
                    ]),
                TituloTotal: "Llamadas Efectivas"),
        };
}
