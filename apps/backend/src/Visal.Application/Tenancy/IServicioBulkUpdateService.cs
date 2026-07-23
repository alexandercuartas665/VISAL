namespace Visal.Application.Tenancy;

/// <summary>Operador de busqueda sobre ServicioContrato.Descripcion.</summary>
public enum OperadorBusquedaServicio
{
    /// <summary>LIKE %texto%.</summary>
    Contiene = 0,
    /// <summary>LIKE texto%.</summary>
    EmpiezaCon = 1,
    /// <summary>LIKE %texto.</summary>
    TerminaCon = 2,
    /// <summary>Igualdad exacta case-insensitive.</summary>
    Exacto = 3,
}

/// <summary>Resultado de una busqueda previa al bulk update.</summary>
public sealed record BulkBusquedaResultado(
    int TotalCoincidencias,
    IReadOnlyList<BulkPreviewFila> Preview);

/// <summary>Fila mostrada como preview (max 10) para verificar antes de aplicar.</summary>
public sealed record BulkPreviewFila(
    Guid ServicioId,
    string Aseguradora,
    string CodigoContrato,
    string? Descripcion,
    string? ModalidadFacturacionActual,
    string? GrupoServicioFacturacionActual,
    string? ServicioFacturacionActual);

/// <summary>Entrada para aplicar el bulk update. Valores null = no tocar ese campo.</summary>
public sealed record AplicarBulkRequest(
    OperadorBusquedaServicio Operador,
    string Texto,
    string? NuevaModalidad,
    string? NuevoGrupoServicio,
    string? NuevoServicio,
    string Motivo);

public sealed record BulkUpdateDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    string CreadoPor,
    string OperadorBusqueda,
    string TextoBusqueda,
    string? NuevaModalidad,
    string? NuevoGrupo,
    string? NuevoServicio,
    string Motivo,
    int TotalAfectados,
    string Estado);

/// <summary>
/// Utilidad tenant-scoped para actualizar en masa 3 campos comerciales de
/// facturacion en <see cref="Visal.Domain.Entities.ServicioContrato"/>
/// (ModalidadFacturacion, GrupoServicioFacturacion, ServicioFacturacion).
///
/// Cada ejecucion se persiste con snapshot de los valores previos (ver
/// <see cref="Visal.Domain.Entities.ServicioBulkUpdate"/>) para permitir
/// rollback. Se conservan las ultimas 20 ejecuciones por tenant.
/// </summary>
public interface IServicioBulkUpdateService
{
    /// <summary>Cuenta cuantos servicios matchearian y devuelve preview de 10.</summary>
    Task<BulkBusquedaResultado> BuscarAsync(OperadorBusquedaServicio operador, string texto, CancellationToken ct = default);

    /// <summary>Aplica los cambios a todos los servicios que matcheen, guarda
    /// snapshot en tabla de auditoria, y purga ejecuciones viejas si >20.</summary>
    Task<BulkUpdateDto?> AplicarAsync(AplicarBulkRequest req, Guid actor, CancellationToken ct = default);

    /// <summary>Ultimas 20 ejecuciones (mas nueva primero) del tenant.</summary>
    Task<IReadOnlyList<BulkUpdateDto>> ListarHistorialAsync(CancellationToken ct = default);

    /// <summary>Revierte una ejecucion aplicada: restaura los 3 campos al valor previo.
    /// Marca la ejecucion como "Revertida". Idempotente: si ya esta revertida, no hace nada.</summary>
    Task<bool> RevertirAsync(Guid bulkUpdateId, Guid actor, CancellationToken ct = default);
}
