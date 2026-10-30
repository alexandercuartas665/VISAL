using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.Reportes;

/// <summary>
/// Tipo logico de un campo reportable. Decide como se formatea el valor del
/// filtro (texto / numero / fecha / bool / guid) antes de pasarlo como parametro
/// de comando. Nunca se concatena el valor en el SQL: siempre va parametrizado.
/// </summary>
public enum ReportFieldKind
{
    Text,
    Integer,
    Number,
    Date,
    DateTime,
    Bool,
    Guid
}

/// <summary>
/// Derivacion de un campo reportable a partir de su columna fisica. Permite exponer partes
/// calculadas de una fecha/hora (mes, hora, minuto) SIN crear columnas nuevas en la base: el
/// generador envuelve la columna de origen en una expresion segura (la columna se resuelve del
/// metamodelo de EF, nunca de texto del usuario). El valor DateTimeOffset se convierte a hora
/// local de Colombia antes de extraer la parte.
/// </summary>
public enum ReportDerivacion
{
    /// <summary>Sin derivacion: la columna se usa tal cual.</summary>
    Ninguna = 0,
    /// <summary>Periodo AAAAMM de una fecha/hora (hora local).</summary>
    AnioMes,
    /// <summary>Hora del dia (0..23) de una fecha/hora (hora local).</summary>
    Hora24,
    /// <summary>Minuto (0..59) de una fecha/hora (hora local).</summary>
    Minuto,
}

/// <summary>
/// Un campo expuesto al constructor de reportes. Referencia una PROPIEDAD CLR de
/// la entidad (no una cadena SQL libre): el generador resuelve el nombre fisico
/// de columna desde el metamodelo de EF, asi nunca entra texto del usuario como
/// identificador SQL.
/// </summary>
/// <param name="Key">Identificador estable usado en las definiciones de hoja.</param>
/// <param name="Label">Etiqueta por defecto para la cabecera de columna.</param>
/// <param name="PropertyName">Nombre de la propiedad CLR en la entidad.</param>
/// <param name="Kind">Tipo logico para formatear filtros.</param>
/// <param name="JsonKey">
/// Si no es null, el campo NO es una columna CLR directa: su valor vive dentro de una
/// columna jsonb (la que resuelve <paramref name="PropertyName"/>) bajo esta clave. El
/// generador emite <c>col -&gt;&gt; 'JsonKey'</c>. Lo usan los campos dinamicos del tenant.
/// </param>
public sealed record ReportableFieldDef(
    string Key,
    string Label,
    string PropertyName,
    ReportFieldKind Kind,
    string? JsonKey = null,
    ReportDerivacion Derivacion = ReportDerivacion.Ninguna);

/// <summary>
/// Un cruce PERMITIDO entre una entidad reportable y otra (join declarado por la
/// plataforma). El constructor solo ofrece estos cruces; no deja unir tablas
/// arbitrarias. El join es LEFT JOIN target ON target.&lt;targetKey&gt; = base.&lt;localFk&gt;.
/// </summary>
/// <param name="Key">Identificador del cruce (prefijo para campos cruzados: "rel.campo").</param>
/// <param name="Label">Etiqueta legible del cruce.</param>
/// <param name="TargetEntityKey">Key de la entidad reportable destino.</param>
/// <param name="LocalPropertyName">Propiedad FK en la entidad base (ej. "PacienteId").</param>
/// <param name="TargetKeyPropertyName">Propiedad clave en el destino (default "Id").</param>
public sealed record ReportableRelationDef(
    string Key,
    string Label,
    string TargetEntityKey,
    string LocalPropertyName,
    string TargetKeyPropertyName = "Id");

/// <summary>
/// Entidad reportable del modelo semantico curado. Mapea a un tipo CLR (y por tanto,
/// via EF, a una tabla fisica) y declara que campos y cruces se exponen. Definida en
/// codigo (no es dato editable) porque referencia el esquema real: curarla es trabajo
/// de plataforma, versionado en el repositorio.
/// </summary>
public sealed class ReportableEntityDef
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required Type ClrType { get; init; }
    public required IReadOnlyList<ReportableFieldDef> Fields { get; init; }
    public IReadOnlyList<ReportableRelationDef> Relations { get; init; } = [];

    public ReportableFieldDef? FindField(string key) =>
        Fields.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

    public ReportableRelationDef? FindRelation(string key) =>
        Relations.FirstOrDefault(r => string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Catalogo de entidades reportables expuestas al constructor. La plataforma lo cura;
/// el tenant arma reportes sobre lo que aqui se declara (y nada mas).
/// </summary>
public interface IReportSemanticModel
{
    IReadOnlyList<ReportableEntityDef> Entities { get; }
    ReportableEntityDef? Find(string key);
}

/// <summary>
/// Catalogo inicial (Ola 1): Pacientes, Encuesta de satisfaccion (SIAU) y Diagnosticos,
/// con el cruce Encuesta -> Paciente. Se crece agregando entidades/campos/cruces aqui.
/// </summary>
public sealed class ReportSemanticModel : IReportSemanticModel
{
    public IReadOnlyList<ReportableEntityDef> Entities { get; }

    public ReportSemanticModel()
    {
        var paciente = new ReportableEntityDef
        {
            Key = "paciente",
            Label = "Pacientes",
            ClrType = typeof(Paciente),
            Fields =
            [
                new("numero_documento", "ID", nameof(Paciente.NumeroDocumento), ReportFieldKind.Text),
                new("tipo_documento", "Tipo ID", nameof(Paciente.TipoDocumento), ReportFieldKind.Text),
                new("primer_nombre", "Primer Nombre", nameof(Paciente.PrimerNombre), ReportFieldKind.Text),
                new("segundo_nombre", "Segundo Nombre", nameof(Paciente.SegundoNombre), ReportFieldKind.Text),
                new("primer_apellido", "Primer Apellido", nameof(Paciente.PrimerApellido), ReportFieldKind.Text),
                new("segundo_apellido", "Segundo Apellido", nameof(Paciente.SegundoApellido), ReportFieldKind.Text),
                new("nombre_completo", "Nombre Completo", nameof(Paciente.NombreCompleto), ReportFieldKind.Text),
                new("fecha_nacimiento", "Fecha de Nacimiento", nameof(Paciente.FechaNacimiento), ReportFieldKind.Date),
                new("edad", "Edad", nameof(Paciente.Edad), ReportFieldKind.Integer),
                new("sexo", "Sexo", nameof(Paciente.Sexo), ReportFieldKind.Text),
                new("estado", "Estado Paciente", nameof(Paciente.Estado), ReportFieldKind.Text),
                new("regimen", "Regimen de Afiliacion", nameof(Paciente.Regimen), ReportFieldKind.Text),
                new("fecha_comentan", "Fecha Comentan", nameof(Paciente.FechaComentan), ReportFieldKind.DateTime),
                new("fecha_atencion", "Fecha de Atencion", nameof(Paciente.FechaAtencion), ReportFieldKind.DateTime),
                // Partes derivadas de las dos fechas (hora local Colombia). No son columnas nuevas:
                // el generador las calcula desde fecha_comentan / fecha_atencion (hoja Ingreso CENSO).
                new("mes_comentan", "Mes", nameof(Paciente.FechaComentan), ReportFieldKind.Text, Derivacion: ReportDerivacion.AnioMes),
                new("comentan_hora", "Comentan (24 H)", nameof(Paciente.FechaComentan), ReportFieldKind.Integer, Derivacion: ReportDerivacion.Hora24),
                new("comentan_minuto", "Comentan (Minutos)", nameof(Paciente.FechaComentan), ReportFieldKind.Integer, Derivacion: ReportDerivacion.Minuto),
                new("respuesta_hora", "Respuesta (24 H)", nameof(Paciente.FechaAtencion), ReportFieldKind.Integer, Derivacion: ReportDerivacion.Hora24),
                new("respuesta_minuto", "Respuesta (Minutos)", nameof(Paciente.FechaAtencion), ReportFieldKind.Integer, Derivacion: ReportDerivacion.Minuto),
                new("fecha_ingreso", "Fecha Ingreso", nameof(Paciente.FechaIngresoPad), ReportFieldKind.Date),
                new("fecha_egreso", "Fecha De Egreso", nameof(Paciente.FechaEgresoPad), ReportFieldKind.Date),
                new("cie10", "CIE10", nameof(Paciente.Cie10Codigo), ReportFieldKind.Text),
                new("diagnostico_principal", "Diagnostico principal", nameof(Paciente.DiagnosticoPrincipal), ReportFieldKind.Text),
                new("cie10_2", "CIE10-2", nameof(Paciente.Cie10Codigo2), ReportFieldKind.Text),
                new("diagnostico_secundario", "Diagnostico secundario", nameof(Paciente.DiagnosticoSecundario), ReportFieldKind.Text),
                new("tutela", "Tutela", nameof(Paciente.Tutela), ReportFieldKind.Text),
                new("direccion", "Direccion", nameof(Paciente.Direccion), ReportFieldKind.Text),
                new("barrio", "Barrio", nameof(Paciente.Barrio), ReportFieldKind.Text),
                new("ciudad", "Ciudad", nameof(Paciente.Ciudad), ReportFieldKind.Text),
                new("telefono", "Telefono Paciente", nameof(Paciente.Telefono), ReportFieldKind.Text),
                new("email", "Correo Electronico", nameof(Paciente.Email), ReportFieldKind.Text),
                new("fecha_reingreso", "Fecha Reingreso", nameof(Paciente.FechaReingreso), ReportFieldKind.Date),
                new("observacion_reingreso", "Observacion Reingreso", nameof(Paciente.ObservacionReingreso), ReportFieldKind.Text),
                new("fecha_hospitalizacion", "Fecha Hospitalizacion", nameof(Paciente.FechaHospitalizacion), ReportFieldKind.Date),
                new("observaciones_hospitalizacion", "Observaciones Hospitalizacion", nameof(Paciente.ObservacionesHospitalizacion), ReportFieldKind.Text),
                new("observaciones_paciente", "Observaciones", nameof(Paciente.Observaciones), ReportFieldKind.Text),
                new("estado_admision", "Estado Admision", nameof(Paciente.EstadoAdmision), ReportFieldKind.Text),
                new("ingreso", "Ingreso", nameof(Paciente.Ingreso), ReportFieldKind.Bool),
                new("motivo_no_ingreso", "Motivo de no ingreso", nameof(Paciente.MotivoNoIngreso), ReportFieldKind.Text),
                new("usuario_ingreso", "Usuario del ingreso", nameof(Paciente.UsuarioIngresoNombre), ReportFieldKind.Text),
                new("contrato_principal", "Contrato", nameof(Paciente.ContratoPrincipal), ReportFieldKind.Text),
                new("condicion", "Condicion", nameof(Paciente.Condicion), ReportFieldKind.Text),
                new("sla_ingreso_minutos", "Mins (SLA)", nameof(Paciente.SlaIngresoMinutos), ReportFieldKind.Integer),
                new("activo", "Activo", nameof(Paciente.Activo), ReportFieldKind.Bool),
                new("sede_id", "Sede (ID)", nameof(Paciente.SedeAtencionId), ReportFieldKind.Guid),
            ],
            Relations =
            [
                new("sede", "Sede de atencion", "sede", nameof(Paciente.SedeAtencionId)),
                // Cruces para mostrar/filtrar por NOMBRE los FK del paciente (hoja Ingreso de Pacientes del CENSO).
                new("ips_comenta", "IPS que comenta", "aseguradora", nameof(Paciente.IpsComentaId)),
                new("clasif_paciente", "Clasificacion del paciente", "catalogo_paciente", nameof(Paciente.ClasificacionPacienteId)),
                new("departamento", "Departamento de residencia", "departamento", nameof(Paciente.DepartamentoId)),
            ],
        };

        var encuesta = new ReportableEntityDef
        {
            Key = "encuesta_satisfaccion",
            Label = "Encuesta de satisfaccion (SIAU)",
            ClrType = typeof(SeguimientoEncuesta),
            Fields =
            [
                new("mes", "Mes", nameof(SeguimientoEncuesta.Mes), ReportFieldKind.Integer),
                new("estado", "Estado", nameof(SeguimientoEncuesta.Estado), ReportFieldKind.Text),
                new("fecha_llamada", "Fecha de llamada SIAU", nameof(SeguimientoEncuesta.FechaLlamada), ReportFieldKind.DateTime),
                new("responsable_llamada", "Responsable llamada SIAU", nameof(SeguimientoEncuesta.ResponsableLlamadaNombre), ReportFieldKind.Text),
                new("pregunta1", "P1 Profesional puntual", nameof(SeguimientoEncuesta.Pregunta1), ReportFieldKind.Integer),
                new("pregunta2", "P2 Presentacion / EPP", nameof(SeguimientoEncuesta.Pregunta2), ReportFieldKind.Integer),
                new("pregunta3", "P3 Indicaciones", nameof(SeguimientoEncuesta.Pregunta3), ReportFieldKind.Integer),
                new("pregunta4", "P4 Experiencia global", nameof(SeguimientoEncuesta.Pregunta4), ReportFieldKind.Integer),
                new("pregunta5", "P5 Recomendaria", nameof(SeguimientoEncuesta.Pregunta5), ReportFieldKind.Integer),
                new("persona_atiende", "Persona quien atiende", nameof(SeguimientoEncuesta.PersonaAtiende), ReportFieldKind.Text),
                new("observaciones", "Observaciones llamada", nameof(SeguimientoEncuesta.Observaciones), ReportFieldKind.Text),
            ],
            Relations =
            [
                new("paciente", "Paciente", "paciente", nameof(SeguimientoEncuesta.PacienteId)),
            ],
        };

        var diagnostico = new ReportableEntityDef
        {
            Key = "diagnostico",
            Label = "Diagnosticos (CIE10)",
            ClrType = typeof(Diagnostico),
            Fields =
            [
                new("codigo", "CIE10", nameof(Diagnostico.Codigo), ReportFieldKind.Text),
                new("nombre", "Diagnostico", nameof(Diagnostico.Nombre), ReportFieldKind.Text),
                new("descripcion", "Descripcion", nameof(Diagnostico.Descripcion), ReportFieldKind.Text),
                new("fuente", "Fuente", nameof(Diagnostico.Fuente), ReportFieldKind.Text),
                new("habilitado", "Habilitado", nameof(Diagnostico.Habilitado), ReportFieldKind.Bool),
            ],
        };

        var ingreso = new ReportableEntityDef
        {
            Key = "ingreso_paciente",
            Label = "Ingreso de Pacientes (bitacora SLA)",
            ClrType = typeof(PacienteIngreso),
            Fields =
            [
                new("fecha_comentan", "Fecha Comentan", nameof(PacienteIngreso.FechaComentan), ReportFieldKind.DateTime),
                new("fecha_respuesta", "Fecha Respuesta", nameof(PacienteIngreso.FechaRespuesta), ReportFieldKind.DateTime),
                new("sla_minutos", "Mins (SLA)", nameof(PacienteIngreso.SlaMinutos), ReportFieldKind.Integer),
                new("ips_comenta", "IPS que comenta", nameof(PacienteIngreso.IpsComenta), ReportFieldKind.Text),
                new("responsable", "Responsable del Ingreso", nameof(PacienteIngreso.ResponsableIngreso), ReportFieldKind.Text),
                new("ingreso", "Ingreso", nameof(PacienteIngreso.Ingreso), ReportFieldKind.Bool),
                new("motivo_no_ingreso", "Motivo de no ingreso", nameof(PacienteIngreso.MotivoNoIngreso), ReportFieldKind.Text),
                new("observaciones", "Observaciones", nameof(PacienteIngreso.Observaciones), ReportFieldKind.Text),
            ],
            Relations =
            [
                new("paciente", "Paciente", "paciente", nameof(PacienteIngreso.PacienteId)),
            ],
        };

        // Sede (sucursal) como entidad destino de cruce, para mostrar/filtrar por nombre de sede.
        // Es TenantEntity, asi que el join hereda el filtro por tenant.
        var sede = new ReportableEntityDef
        {
            Key = "sede",
            Label = "Sedes (sucursales)",
            ClrType = typeof(Sucursal),
            Fields =
            [
                new("nombre", "Sede", nameof(Sucursal.Nombre), ReportFieldKind.Text),
                new("ciudad", "Ciudad de la sede", nameof(Sucursal.Ciudad), ReportFieldKind.Text),
                new("departamento", "Departamento (operativo)", nameof(Sucursal.Departamento), ReportFieldKind.Text),
            ],
        };

        // Aseguradora/IPS como destino de cruce (IPS que comenta + Entidad aseguradora). TenantEntity.
        var aseguradora = new ReportableEntityDef
        {
            Key = "aseguradora",
            Label = "Aseguradora / IPS",
            ClrType = typeof(Aseguradora),
            Fields =
            [
                new("nombre", "Nombre", nameof(Aseguradora.Nombre), ReportFieldKind.Text),
                new("codigo", "Codigo", nameof(Aseguradora.Codigo), ReportFieldKind.Text),
            ],
        };

        // Catalogo de paciente como destino de cruce (clasificacion, tipo usuario, etc.). TenantEntity.
        var catalogoPaciente = new ReportableEntityDef
        {
            Key = "catalogo_paciente",
            Label = "Catalogo de paciente",
            ClrType = typeof(CatalogoPaciente),
            Fields =
            [
                new("nombre", "Nombre", nameof(CatalogoPaciente.Nombre), ReportFieldKind.Text),
                new("codigo", "Codigo", nameof(CatalogoPaciente.Codigo), ReportFieldKind.Text),
            ],
        };

        // Departamento (geografia) como destino de cruce. Es catalogo GLOBAL (BaseEntity, no tenant):
        // el generador lo une por PK sin filtro de tenant.
        var departamento = new ReportableEntityDef
        {
            Key = "departamento",
            Label = "Departamentos",
            ClrType = typeof(Departamento),
            Fields =
            [
                new("nombre", "Departamento", nameof(Departamento.Nombre), ReportFieldKind.Text),
            ],
        };

        Entities = [paciente, encuesta, diagnostico, ingreso, sede, aseguradora, catalogoPaciente, departamento];
    }

    public ReportableEntityDef? Find(string key) =>
        Entities.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
}
