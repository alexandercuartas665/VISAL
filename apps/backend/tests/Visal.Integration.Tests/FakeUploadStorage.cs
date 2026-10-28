using Visal.Application.Common;

namespace Visal.Integration.Tests;

/// <summary>
/// IUploadStorage no-op para el host de integracion. La implementacion real
/// (WwwRootUploadStorage) vive en el host web y depende de wwwroot/IWebHostEnvironment;
/// en los tests no se necesita persistir binarios, solo que el contenedor resuelva el
/// servicio que consumen varios servicios de Application (FormWebhook, FirmaRemota,
/// EmailIngest, TaskBoard, Curso, ...). Devuelve la ruta web que devolveria el real.
/// </summary>
public sealed class FakeUploadStorage : IUploadStorage
{
    public Task<string> GuardarAsync(string subcarpeta, string nombre, byte[] contenido, CancellationToken cancellationToken = default)
        => Task.FromResult($"/uploads/{subcarpeta}/{nombre}");
}
