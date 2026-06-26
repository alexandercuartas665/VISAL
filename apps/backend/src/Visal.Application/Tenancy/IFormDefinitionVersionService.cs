namespace Visal.Application.Tenancy;

/// <summary>Snapshot resumido para listado en el modal de Historial.</summary>
public sealed record FormDefinitionSnapshotDto(
    Guid Id,
    Guid FormDefinitionId,
    string Codigo,
    string Nombre,
    string? Version,
    string? Tipo,
    bool Activo,
    DateTimeOffset SnapshotAt,
    string? Motivo,
    Guid? SnapshotBy);

/// <summary>Snapshot completo (incluye schema y rutas) — usado por la accion "Ver".</summary>
public sealed record FormDefinitionSnapshotDetailDto(
    Guid Id,
    Guid FormDefinitionId,
    string Codigo,
    string Nombre,
    string? Version,
    string? Tipo,
    bool Activo,
    string SchemaJson,
    string? PrefillRoutesJson,
    DateTimeOffset SnapshotAt,
    string? Motivo);

/// <summary>
/// Versionado automatico de form_definitions. Cada UPDATE de una fila viva
/// genera un snapshot del estado anterior via trigger Postgres
/// (fn_snapshot_form_definition, BEFORE UPDATE). Este servicio solo expone
/// LECTURA y la operacion de RESTAURACION; la creacion de snapshots NO pasa
/// por el codigo C# — el trigger atrapa todas las vias de cambio (UI del
/// FormBuilder, scripts PowerShell, SQL directo, futuras migrations).
/// </summary>
public interface IFormDefinitionVersionService
{
    /// <summary>Lista los snapshots de un formulario, mas reciente primero.
    /// La rotacion in-trigger garantiza un maximo de 20 filas por formulario.</summary>
    Task<IReadOnlyList<FormDefinitionSnapshotDto>> ListarAsync(
        Guid formDefinitionId,
        CancellationToken cancellationToken = default);

    /// <summary>Obtiene el detalle completo (incluye schema_json) de un snapshot.
    /// Usado por el modal "Ver" en la UI. null si no existe o no pertenece al
    /// tenant activo (query filter global).</summary>
    Task<FormDefinitionSnapshotDetailDto?> ObtenerAsync(
        Guid snapshotId,
        CancellationToken cancellationToken = default);

    /// <summary>Copia el contenido del snapshot a la fila viva de form_definitions.
    /// El UPDATE resultante dispara el trigger, que a su vez genera un nuevo
    /// snapshot del estado que estamos REEMPLAZANDO — asi el restore tambien es
    /// reversible (puedes deshacer una restauracion). Devuelve true si se hizo
    /// el cambio; false si el snapshot no existe o no pertenece al tenant.</summary>
    Task<bool> RestaurarAsync(
        Guid snapshotId,
        Guid actorUserId,
        CancellationToken cancellationToken = default);
}
