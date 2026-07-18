namespace Visal.Application.Tenancy;

/// <summary>Vista compacta del paquete en el listado. <c>CupsRepresentativoServicioId</c>
/// identifica cual PaqueteServicio va como CUPS al facturar el paquete (Fase 4 Facturacion).
/// Nullable — si no se ha marcado, el builder cae al primer servicio ordenado por codigo.</summary>
public sealed record PaqueteDto(Guid Id, string Codigo, string Nombre, bool Activo, decimal? Precio,
    Guid? CupsRepresentativoServicioId);

/// <summary>Servicio dentro del detalle de un paquete. <c>Nombre</c> viene por JOIN
/// al catalogo de servicios de referencia. Si el catalogo no existe, viene null.</summary>
public sealed record PaqueteServicioDto(
    Guid Id, Guid PaqueteId, string Codigo, string? Nombre, int Cantidad,
    Guid? CatalogoServicioReferenciaId);

/// <summary>Payload de guardado. Precio es opcional (numeric). El detalle de servicios
/// se guarda por separado con <see cref="IPaqueteService.AgregarServicioAsync"/>.</summary>
public sealed record SavePaqueteRequest(Guid? Id, string Codigo, string Nombre, bool Activo, decimal? Precio);

/// <summary>Payload para agregar un servicio al detalle del paquete. El
/// <c>CatalogoServicioReferenciaId</c> es fuertemente recomendado (viene del autocomplete)
/// pero se acepta null para permitir codigos sueltos escritos a mano.</summary>
public sealed record AgregarPaqueteServicioRequest(
    Guid PaqueteId, string Codigo, int Cantidad, Guid? CatalogoServicioReferenciaId);

/// <summary>Fila del autocomplete del catalogo de servicios. Devuelve solo lo necesario
/// para pintar la lista (Codigo + Nombre + Tipo).</summary>
public sealed record CatalogoServicioAutocompleteDto(Guid Id, string Codigo, string Nombre, string Tipo);

/// <summary>Catalogo de paquetes comerciales. Tenant-scoped. Se usa para agrupar
/// servicios de un contrato de aseguradora bajo un paquete opcional.</summary>
public interface IPaqueteService
{
    Task<IReadOnlyList<PaqueteDto>> ListarAsync(string? filtro = null, bool soloActivos = false, CancellationToken ct = default);
    Task<PaqueteDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<PaqueteDto?> SaveAsync(SavePaqueteRequest req, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default);

    /// <summary>Inserta paquetes en bloque (usado por el seed desde el Excel NUEVO PAQUETE
    /// DOMICILIARIO). Idempotente por codigo: si ya existe se ignora. Devuelve la cantidad insertada.</summary>
    Task<int> SembrarAsync(IReadOnlyList<SavePaqueteRequest> items, Guid actor, CancellationToken ct = default);

    // ---------------- Detalle de servicios del paquete ----------------

    /// <summary>Lista los servicios que componen el paquete, con JOIN al catalogo para
    /// resolver el nombre. Orden estable por codigo.</summary>
    Task<IReadOnlyList<PaqueteServicioDto>> ListarServiciosAsync(Guid paqueteId, CancellationToken ct = default);

    /// <summary>Agrega un servicio al detalle del paquete. Si el codigo ya existe en el paquete
    /// tira <see cref="InvalidOperationException"/> (para subir cantidad hay que editar).</summary>
    Task<PaqueteServicioDto> AgregarServicioAsync(AgregarPaqueteServicioRequest req, Guid actor, CancellationToken ct = default);

    /// <summary>Actualiza cantidad de un servicio existente en el paquete.</summary>
    Task<PaqueteServicioDto?> ActualizarCantidadServicioAsync(Guid servicioId, int cantidad, Guid actor, CancellationToken ct = default);

    /// <summary>Quita un servicio del detalle del paquete.</summary>
    Task<bool> QuitarServicioAsync(Guid servicioId, Guid actor, CancellationToken ct = default);

    /// <summary>Busca en el catalogo global de servicios de referencia por codigo o nombre.
    /// Alimenta el autocomplete del editor del paquete. Solo activos.</summary>
    Task<IReadOnlyList<CatalogoServicioAutocompleteDto>> BuscarCatalogoAsync(string filtro, int limite = 20, CancellationToken ct = default);

    /// <summary>Marca (o desmarca con null) el PaqueteServicio que va como CUPS
    /// representativo al facturar el paquete. Solo un servicio por paquete puede ser
    /// representativo — actualiza <see cref="Paquete.CupsRepresentativoServicioId"/>.
    /// Devuelve el paquete actualizado o null si no existe.</summary>
    Task<PaqueteDto?> MarcarRepresentativoAsync(Guid paqueteId, Guid? paqueteServicioId, Guid actor, CancellationToken ct = default);
}
