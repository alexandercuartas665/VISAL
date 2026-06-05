using Microsoft.AspNetCore.Hosting;
using Visal.Application.Common;

namespace Visal.SuperAdmin.RealTime;

/// <summary>Implementacion de IUploadStorage que escribe en wwwroot/uploads del host.</summary>
public sealed class WwwRootUploadStorage : IUploadStorage
{
    private readonly IWebHostEnvironment _env;

    public WwwRootUploadStorage(IWebHostEnvironment env) { _env = env; }

    public async Task<string> GuardarAsync(string subcarpeta, string nombre, byte[] contenido, CancellationToken cancellationToken = default)
    {
        var sub = (subcarpeta ?? "misc").Trim('/', '\\');
        var dir = Path.Combine(_env.WebRootPath, "uploads", sub);
        Directory.CreateDirectory(dir);
        var rutaDisco = Path.Combine(dir, nombre);
        await File.WriteAllBytesAsync(rutaDisco, contenido, cancellationToken);
        return $"/uploads/{sub}/{nombre}";
    }
}
