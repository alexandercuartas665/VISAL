namespace Visal.Application.Tenancy.Reportes;

/// <summary>Una fila de la bitacora de ingreso (con datos del paciente ya resueltos).</summary>
public sealed record PacienteIngresoDto(
    Guid Id,
    Guid PacienteId,
    string PacienteNombre,
    string? PacienteDocumento,
    DateTimeOffset? FechaComentan,
    DateTimeOffset? FechaRespuesta,
    int? SlaMinutos,
    string? IpsComenta,
    string? ResponsableIngreso,
    bool Ingreso,
    string? MotivoNoIngreso,
    string? Observaciones);

/// <summary>Resultado de buscar un paciente para asociarlo a un ingreso.</summary>
public sealed record PacienteBusquedaDto(Guid Id, string NombreCompleto, string? NumeroDocumento);

public sealed record GuardarIngresoRequest(
    Guid PacienteId,
    DateTimeOffset? FechaComentan,
    DateTimeOffset? FechaRespuesta,
    string? IpsComenta,
    string? ResponsableIngreso,
    bool Ingreso,
    string? MotivoNoIngreso,
    string? Observaciones);

/// <summary>
/// Captura y consulta de la bitacora de ingreso (Ola 4b). Es la fuente de datos de la hoja
/// "Ingreso de Pacientes" del CENSO. El SLA en minutos lo calcula el servicio al guardar.
/// Todo tenant-scoped: solo ve/escribe ingresos (y pacientes) del tenant actual.
/// </summary>
public interface IPacienteIngresoService
{
    Task<IReadOnlyList<PacienteIngresoDto>> ListarAsync(CancellationToken ct = default);
    Task<IReadOnlyList<PacienteBusquedaDto>> BuscarPacientesAsync(string termino, CancellationToken ct = default);
    Task<Guid> GuardarAsync(Guid? id, GuardarIngresoRequest req, CancellationToken ct = default);
    Task<bool> EliminarAsync(Guid id, CancellationToken ct = default);
}
