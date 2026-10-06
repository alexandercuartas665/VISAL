namespace Visal.Application.Common;

/// <summary>Genera un PDF o imagen a partir de una URL (la pagina publica de la cotizacion) usando un motor headless.</summary>
public interface IQuotePdfRenderer
{
    Task<byte[]> RenderUrlToPdfAsync(string url, CancellationToken cancellationToken = default);

    /// <summary>Como <see cref="RenderUrlToPdfAsync(string,CancellationToken)"/> pero
    /// en vez de esperar "network idle" espera a que aparezca <paramref name="waitForSelector"/>.
    /// Necesario para paginas Blazor Server interactivas: su WebSocket mantiene la red
    /// activa (nunca idle), asi que se espera el elemento que marca el render terminado.</summary>
    Task<byte[]> RenderUrlToPdfAsync(string url, string waitForSelector, CancellationToken cancellationToken = default);

    /// <summary>Renderiza varias URLs a PDF reusando UN SOLO navegador headless, con
    /// hasta <paramref name="maxConcurrency"/> paginas en paralelo. Evita lanzar Chrome
    /// por cada documento y aprovecha el navegador para rendir en lote. Espera
    /// <paramref name="waitForSelector"/> en cada pagina. Devuelve un PDF por URL, EN
    /// ORDEN; si una URL falla, su posicion queda como arreglo vacio (no aborta el lote).</summary>
    Task<IReadOnlyList<byte[]>> RenderUrlsToPdfAsync(IReadOnlyList<string> urls, string waitForSelector, int maxConcurrency = 4, IProgress<int>? onProgress = null, CancellationToken cancellationToken = default);

    /// <summary>Genera una imagen PNG de pagina completa de la URL (para enviar la cotizacion como imagen).</summary>
    Task<byte[]> RenderUrlToImageAsync(string url, CancellationToken cancellationToken = default);
}
