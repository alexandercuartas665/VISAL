namespace Visal.Application.Common;

/// <summary>
/// Abstraccion minima de almacenamiento de archivos servibles via wwwroot.
/// La implementacion vive en la app host (que tiene IWebHostEnvironment).
/// Mantiene a la capa Application libre del acoplamiento a ASP.NET Core.
/// </summary>
public interface IUploadStorage
{
    /// <summary>Guarda bytes bajo wwwroot/uploads/{subcarpeta}/{nombre} y devuelve la ruta web
    /// servible (ej. "/uploads/notas/firma-abc.png"). Crea la subcarpeta si no existe.</summary>
    Task<string> GuardarAsync(string subcarpeta, string nombre, byte[] contenido, CancellationToken cancellationToken = default);
}
