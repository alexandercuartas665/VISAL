namespace Visal.Application.Tenancy;

/// <summary>Resumen de un formulario para listados.</summary>
public sealed record FormDefinitionDto(
    Guid Id,
    string Codigo,
    string Nombre,
    string? Version,
    string? Tipo,
    bool Activo,
    DateTimeOffset? UpdatedAt,
    string? CodigoSecundario = null);

/// <summary>Detalle completo (incluye el esquema JSON del disenador y las rutas de prefill).</summary>
public sealed record FormDefinitionDetailDto(
    Guid Id,
    string Codigo,
    string Nombre,
    string? Version,
    string? Tipo,
    bool Activo,
    string SchemaJson,
    string? PrefillRoutesJson,
    string? CodigoSecundario = null);

/// <summary>Alta o actualizacion. Si <see cref="Id"/> es null se crea; si no, se actualiza.</summary>
public sealed record SaveFormDefinitionRequest(
    Guid? Id,
    string Codigo,
    string Nombre,
    string? Version,
    string? Tipo,
    string SchemaJson,
    bool Activo,
    string? PrefillRoutesJson = null,
    string? CodigoSecundario = null);

/// <summary>Gestion de definiciones de formularios (Motor de Formularios, 2.M10), tenant-scoped.</summary>
public interface IFormDefinitionService
{
    Task<IReadOnlyList<FormDefinitionDto>> ListAsync(CancellationToken cancellationToken = default);
    Task<FormDefinitionDetailDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<FormDefinitionDetailDto?> SaveAsync(SaveFormDefinitionRequest request, Guid actorUserId, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(Guid id, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Actualiza solo las rutas de prefill del formulario, sin tocar schema ni metadatos.</summary>
    Task<bool> UpdatePrefillRoutesAsync(Guid id, string? prefillRoutesJson, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Devuelve el formulario activo del tenant cuyo Tipo coincide con el parametro.
    /// Lo usa la impresion de ordenes para resolver "que formulario imprimo cuando
    /// el profesional pide Orden de Medicamentos / Servicios / etc.". Si hay varios
    /// activos del mismo tipo, devuelve el ultimo actualizado (mas reciente).
    /// </summary>
    Task<FormDefinitionDetailDto?> GetActivoByTipoAsync(string tipo, CancellationToken cancellationToken = default);

    /// <summary>
    /// Busca un formulario activo del tenant cuyo <see cref="FormDefinition.CodigoSecundario"/>
    /// coincida exactamente con el parametro. Se usa como clave estable en la impresion
    /// del paquete: por convencion cada orden clasica (medicamentos, servicios, etc.)
    /// guarda ahi un valor como "ORDEN_MEDICAMENTOS" independientemente de su Tipo
    /// (que puede quedar en "FORMATO" para agruparlas en el listado).
    /// </summary>
    Task<FormDefinitionDetailDto?> GetActivoByCodigoSecundarioAsync(string codigoSecundario, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recorre todos los formularios del tenant y, por cada uno, infiere mapeos de
    /// prefill desde el modulo "paciente" basado en el Name/Label de los campos.
    /// Preserva los mapeos manuales que ya existen (no los sobrescribe). Devuelve
    /// estadisticas del proceso.
    /// </summary>
    Task<AutoEnlazarPacienteResultDto> AutoEnlazarPacienteEnTodosAsync(Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Como <see cref="AutoEnlazarPacienteEnTodosAsync"/> pero limitado a UN formulario
    /// del tenant. Preserva los mapeos manuales existentes. Devuelve null si el
    /// formulario no existe o no pertenece al tenant activo.
    /// </summary>
    Task<AutoEnlazarPacienteResultDto?> AutoEnlazarPacienteEnFormAsync(Guid formDefinitionId, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Bytes UTF-8 con el JSON de exportacion del formulario. Incluye
    /// codigo, tipo, schema deserializado y opcionalmente las rutas de prefill.
    /// Cuando <paramref name="incluirRutasPrefill"/> es false, el JSON exportado
    /// NO trae la clave "prefillRoutes" — util cuando el tenant destino ya tiene
    /// las rutas configuradas y no queremos sobrescribirlas al importar.
    /// Devuelve null si el formulario no existe. El nombre sugerido del archivo
    /// va aparte via GetExportFileNameAsync.</summary>
    Task<byte[]?> ExportarJsonAsync(Guid id, bool incluirRutasPrefill = true, CancellationToken cancellationToken = default);

    /// <summary>Nombre de archivo sugerido para la descarga (codigo o Id + .json).</summary>
    Task<string?> GetExportFileNameAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Crea (o actualiza por codigo) un formulario a partir del JSON exportado.
    /// Si el codigo ya existe en el tenant y <paramref name="sobrescribir"/> es true,
    /// actualiza el existente; si es false, agrega sufijo "-import" al codigo para
    /// no colisionar. Devuelve el detalle del formulario resultante.</summary>
    Task<FormDefinitionDetailDto?> ImportarJsonAsync(byte[] jsonBytes, bool sobrescribir, Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Copia una o mas secciones del formulario <paramref name="origenId"/> al
    /// final del formulario <paramref name="destinoId"/>. Regenera el Id del
    /// nodo pero preserva su Name — asi las rutas de prefill que apuntan a esos
    /// campos siguen funcionando. Ademas, si el origen tiene mappings de
    /// prefill que apuntan a alguno de los campos copiados y el destino NO
    /// tiene ya un mapping para ese mismo target, copia esos mappings a las
    /// rutas correspondientes (creando la ruta en destino si no existia).
    /// El schema y las rutas se persisten en la misma transaccion.
    /// </summary>
    Task<CopiarSeccionesResultDto?> CopiarSeccionesAsync(
        Guid destinoId, Guid origenId, IReadOnlyList<string> seccionIds,
        Guid actorUserId, CancellationToken cancellationToken = default);

    /// <summary>Listado ligero para el selector "Copiar desde otro formulario":
    /// codigo + nombre + tipo. No incluye el schema para que el request sea liviano.</summary>
    Task<IReadOnlyList<FormDefinitionLiteDto>> ListLiteAsync(CancellationToken cancellationToken = default);

    /// <summary>Solo las secciones (nodos Type=section) de un formulario, con su
    /// Id, Label y cantidad de campos hijos. Para renderizar los checkboxes en
    /// el modal de copiar secciones sin traer todo el schema al frontend.</summary>
    Task<IReadOnlyList<SeccionResumenDto>> ListSeccionesAsync(Guid formDefinitionId, CancellationToken cancellationToken = default);
}

/// <summary>Item ligero de un FormDefinition, para dropdowns.</summary>
public sealed record FormDefinitionLiteDto(Guid Id, string Codigo, string Nombre, string? Tipo);

/// <summary>Resumen de una seccion de un formulario para el modal de copia.</summary>
public sealed record SeccionResumenDto(string Id, string Label, int CantidadCampos);

/// <summary>Reporte del resultado de copiar secciones.</summary>
public sealed record CopiarSeccionesResultDto(
    int SeccionesCopiadas,
    int CamposCopiados,
    int RutasPrefillCopiadas,
    int RutasPrefillOmitidasPorConflicto,
    IReadOnlyList<string> NombresDuplicados);

public sealed record AutoEnlazarPacienteResultDto(
    int FormulariosRevisados,
    int FormulariosActualizados,
    int MapeosAgregados,
    int FormulariosSinCambios);
