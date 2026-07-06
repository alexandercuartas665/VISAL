namespace Visal.Application.Tenancy;

/// <summary>
/// Servicio sin autenticacion que lista las sedes activas (a traves de todos los tenants)
/// para alimentar el dropdown del login. No expone datos sensibles: solo id, nombre y ciudad.
/// </summary>
public interface ISedeCatalogoPublicoService
{
    Task<IReadOnlyList<SedePublicaDto>> ListAsync(CancellationToken ct = default);

    /// <summary>
    /// Devuelve las sedes visibles para un usuario dado (por email, username o documento).
    /// Regla de fallback (evita enumeracion de usuarios): si el usuario no existe o no tiene
    /// membresias activas, se devuelve el mismo listado que <see cref="ListAsync"/> con
    /// <see cref="SedesParaUsuarioDto.MostrarGlobal"/> = true. Un usuario existente sin
    /// EsGlobal solo ve las sedes que tiene asignadas (o su tenant completo si no tiene
    /// asignacion explicita), sin la opcion GLOBAL.
    /// </summary>
    Task<SedesParaUsuarioDto> ListParaUsuarioAsync(string usuario, CancellationToken ct = default);
}

public sealed record SedePublicaDto(Guid Id, string Nombre, string? Ciudad);

/// <summary>Respuesta del endpoint /api/login/sedes?usuario=. La marca MostrarGlobal
/// permite al cliente decidir si incluye la opcion GLOBAL en el dropdown.</summary>
public sealed record SedesParaUsuarioDto(IReadOnlyList<SedePublicaDto> Sedes, bool MostrarGlobal);
