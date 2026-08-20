namespace Visal.Application.Tenancy;

/// <summary>Item de medicamento tal como se muestra en la verificacion publica (sin datos sensibles).</summary>
public sealed record OrdenItemPublicoDto(
    string Nombre,
    string? Codigo,
    string? Cantidad,
    string? Frecuencia,
    string? Dias,
    string? Posologia);

/// <summary>
/// Peticion para emitir (firmar) una orden de medicamentos. El bag es el diccionario
/// de valores del formulario ORD-MEDI ya prellenado (paciente, diagnosticos, items,
/// firma) serializado en JSON; el servicio le inyecta el codigo de verificacion.
/// La proyeccion publica se guarda aparte para que la pagina publica muestre solo
/// campos seguros sin depender de los Names del schema.
/// </summary>
public sealed record EmitirOrdenRequest(
    Guid HistoriaClinicaId,
    string BagJson,
    DateTimeOffset FechaEmision,
    string PacienteIniciales,
    string? ProfesionalNombre,
    string? ProfesionalRegistro,
    IReadOnlyList<OrdenItemPublicoDto> Items,
    // Tipo de orden emitida ("MED" por defecto para no romper la fórmula médica).
    string TipoOrden = "MED",
    // Numero de la orden (grupo) dentro de la HC: cada orden de medicamentos/insumos
    // emite su propio QR. Default 1 (compatibilidad con la orden unica anterior).
    int NumeroOrden = 1);

/// <summary>Resultado de emitir: el codigo minteado y el id de la fila.</summary>
public sealed record EmitirOrdenResultDto(Guid Id, string Codigo, DateTimeOffset EmitidoAt);

/// <summary>
/// Resumen de la emision activa de una HC para el visor/impresion: incluye el bag
/// congelado (con el codigo ya inyectado) para renderizar el documento schema-driven.
/// </summary>
public sealed record OrdenPublicaResumenDto(
    Guid Id,
    string Codigo,
    DateTimeOffset EmitidoAt,
    bool Revocada,
    string BagJson);

/// <summary>
/// Proyeccion publica y segura de una orden verificada. NO incluye documento
/// completo, direccion, telefono ni el numero de documento del paciente — solo
/// iniciales, items prescritos, profesional y registro medico.
/// </summary>
public sealed record OrdenVerificacionPublicaDto(
    string Codigo,
    DateTimeOffset FechaEmision,
    string PacienteIniciales,
    string? ProfesionalNombre,
    string? ProfesionalRegistro,
    IReadOnlyList<OrdenItemPublicoDto> Items,
    bool Revocada,
    string? TenantNombre,
    string? TenantLogoUrl,
    // Tipo ("MED","SRV",...) + titulo legible para el encabezado publico.
    string TipoOrden = "MED",
    string TituloOrden = "Orden de Medicamentos");

/// <summary>
/// Emision, revocacion y verificacion publica de ordenes de medicamentos con QR.
/// La emision congela un snapshot inmutable y mintea un codigo unico; la
/// verificacion es publica (por codigo, sin tenant) y solo expone datos seguros.
/// </summary>
public interface IOrdenMedicamentoPublicaService
{
    /// <summary>Emite (firma) la orden: mintea codigo unico, congela snapshot, audita. Devuelve el codigo.</summary>
    Task<EmitirOrdenResultDto> EmitirAsync(EmitirOrdenRequest req, Guid actorUserId, CancellationToken ct = default);

    /// <summary>Revoca una orden emitida (baja logica; el snapshot se conserva). Audita.</summary>
    Task<bool> RevocarAsync(Guid ordenPublicaId, Guid actorUserId, string? motivo, CancellationToken ct = default);

    /// <summary>Emision de MEDICAMENTOS vigente (no revocada) mas reciente de la HC, o null. Incluye el bag congelado.</summary>
    Task<OrdenPublicaResumenDto?> ObtenerEmisionActivaAsync(Guid historiaClinicaId, CancellationToken ct = default);

    /// <summary>Emision vigente mas reciente de la HC PARA UN TIPO de orden (MED/SRV/REM/...), o null.</summary>
    Task<OrdenPublicaResumenDto?> ObtenerEmisionActivaAsync(Guid historiaClinicaId, string tipoOrden, CancellationToken ct = default);

    /// <summary>Emision vigente mas reciente de la HC para un tipo Y numero de orden (grupo), o null.</summary>
    Task<OrdenPublicaResumenDto?> ObtenerEmisionActivaAsync(Guid historiaClinicaId, string tipoOrden, int numeroOrden, CancellationToken ct = default);

    /// <summary>True si la HC tiene una emision de MEDICAMENTOS vigente (usado para bloquear edicion de items).</summary>
    Task<bool> TieneEmisionActivaAsync(Guid historiaClinicaId, CancellationToken ct = default);

    /// <summary>True si la HC tiene una emision vigente para el tipo de orden dado.</summary>
    Task<bool> TieneEmisionActivaAsync(Guid historiaClinicaId, string tipoOrden, CancellationToken ct = default);

    /// <summary>Titulo legible del documento por tipo de orden (para el encabezado publico y la impresion).</summary>
    static string TituloPorTipo(string? tipoOrden) => (tipoOrden ?? "MED").ToUpperInvariant() switch
    {
        "SRV" => "Orden a Servicios",
        "REM" => "Ordenes de Remision",
        "INS" => "Orden de Insumos",
        "INC" => "Orden de Incapacidad",
        "CERT" => "Certificacion Medica",
        "RXEXT" => "Orden RX Imagenologia",
        "LABEXT" => "Orden de Laboratorios",
        "SRVEXT" => "Orden de Servicios Externos",
        "INSEXT" => "Orden de Insumos Externos",
        _ => "Orden de Medicamentos",
    };

    /// <summary>
    /// Verificacion PUBLICA por codigo (sin autenticacion, sin tenant scope). Registra
    /// la consulta (ip/ua). Devuelve null si el codigo no existe. Si existe pero esta
    /// revocada, el DTO lo indica con <see cref="OrdenVerificacionPublicaDto.Revocada"/>.
    /// </summary>
    Task<OrdenVerificacionPublicaDto?> VerificarPorCodigoAsync(
        string codigo, string? ip, string? userAgent, CancellationToken ct = default);
}
