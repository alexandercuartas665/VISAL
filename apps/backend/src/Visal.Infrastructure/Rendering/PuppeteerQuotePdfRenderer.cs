using Visal.Application.Common;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PuppeteerSharp;
using PuppeteerSharp.Media;

namespace Visal.Infrastructure.Rendering;

/// <summary>
/// Render de PDF de cotizaciones con un Chromium headless (PuppeteerSharp). Navega a la pagina publica
/// de la cotizacion e imprime a PDF. El ejecutable de Chrome se resuelve por env (VISAL_CHROME_PATH),
/// rutas comunes del SO, o se descarga con BrowserFetcher como ultimo recurso.
/// </summary>
public sealed class PuppeteerQuotePdfRenderer : IQuotePdfRenderer
{
    private readonly IConfiguration _config;
    private readonly ILogger<PuppeteerQuotePdfRenderer> _log;

    public PuppeteerQuotePdfRenderer(IConfiguration config, ILogger<PuppeteerQuotePdfRenderer> log)
    {
        _config = config;
        _log = log;
    }

    public async Task<byte[]> RenderUrlToPdfAsync(string url, CancellationToken cancellationToken = default)
    {
        await using var browser = await LaunchAsync();
        await using var page = await browser.NewPageAsync();
        await page.GoToAsync(url, new NavigationOptions
        {
            WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
            Timeout = 30000
        });
        return await page.PdfDataAsync(new PdfOptions
        {
            Format = PaperFormat.A4,
            PrintBackground = true,
            // Margen de pagina ("padding" del formato) para que el contenido no quede pegado al borde.
            MarginOptions = new MarginOptions { Top = "12mm", Bottom = "12mm", Left = "10mm", Right = "10mm" }
        });
    }

    public async Task<byte[]> RenderUrlToPdfAsync(string url, string waitForSelector, CancellationToken cancellationToken = default)
    {
        await using var browser = await LaunchAsync();
        await using var page = await browser.NewPageAsync();
        return await RenderPageAsync(page, url, waitForSelector);
    }

    public async Task<IReadOnlyList<byte[]>> RenderUrlsToPdfAsync(IReadOnlyList<string> urls, string waitForSelector, int maxConcurrency = 4, IProgress<int>? onProgress = null, CancellationToken cancellationToken = default)
    {
        if (urls.Count == 0) { return Array.Empty<byte[]>(); }
        var hechos = 0;

        // UN SOLO navegador para todo el lote (evita ~N arranques de Chrome). Las
        // paginas se rinden en paralelo hasta 'maxConcurrency' a la vez. El resultado
        // conserva el orden de 'urls'; una URL que falle queda como arreglo vacio para
        // no abortar todo el lote (p. ej. una HC que no renderiza no tumba el ZIP mensual).
        var res = new byte[urls.Count][];
        var grado = Math.Max(1, maxConcurrency);
        await using var browser = await LaunchAsync();
        using var gate = new SemaphoreSlim(grado);

        var tasks = new List<Task>(urls.Count);
        for (var i = 0; i < urls.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var idx = i;
            var url = urls[i];
            await gate.WaitAsync(cancellationToken);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await using var page = await browser.NewPageAsync();
                    res[idx] = await RenderPageAsync(page, url, waitForSelector);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "RenderUrlsToPdf: fallo el render de {Url}; se omite esa pagina", url);
                    res[idx] = Array.Empty<byte>();
                }
                finally
                {
                    gate.Release();
                    onProgress?.Report(Interlocked.Increment(ref hechos));
                }
            }, cancellationToken));
        }

        await Task.WhenAll(tasks);
        return res;
    }

    // DOMContentLoaded (no Networkidle0): la pagina Blazor mantiene el WebSocket
    // abierto, asi que esperamos el selector que aparece cuando el render termino.
    private static async Task<byte[]> RenderPageAsync(IPage page, string url, string waitForSelector)
    {
        await page.GoToAsync(url, new NavigationOptions
        {
            WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded },
            Timeout = 30000
        });
        await page.WaitForSelectorAsync(waitForSelector, new WaitForSelectorOptions { Timeout = 25000 });
        return await page.PdfDataAsync(new PdfOptions
        {
            Format = PaperFormat.A4,
            PrintBackground = true,
            MarginOptions = new MarginOptions { Top = "12mm", Bottom = "12mm", Left = "10mm", Right = "10mm" }
        });
    }

    public async Task<byte[]> RenderUrlToImageAsync(string url, CancellationToken cancellationToken = default)
    {
        await using var browser = await LaunchAsync();
        await using var page = await browser.NewPageAsync();
        // Ancho fijo acorde al diseno de la cotizacion (tarjeta ~800px) para una imagen consistente y nitida.
        await page.SetViewportAsync(new ViewPortOptions { Width = 820, Height = 1160, DeviceScaleFactor = 2 });
        await page.GoToAsync(url, new NavigationOptions
        {
            WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
            Timeout = 30000
        });
        return await page.ScreenshotDataAsync(new ScreenshotOptions
        {
            FullPage = true,
            Type = ScreenshotType.Png
        });
    }

    /// <summary>
    /// Descarga (BrowserFetcher) el Chromium que ESTA version de PuppeteerSharp espera,
    /// en <paramref name="installPath"/>, y devuelve la ruta del ejecutable. Se usa en
    /// tiempo de build (Dockerfile) para HORNEAR el navegador compatible en la imagen y
    /// evitar la descarga en runtime y la incompatibilidad con el chromium del sistema
    /// ("Invalid referrerPolicy"). No lanza el navegador: solo lo deja en disco.
    /// </summary>
    public static async Task<string> DownloadBrowserAsync(string installPath)
    {
        var fetcher = new BrowserFetcher(new BrowserFetcherOptions { Path = installPath });
        var installed = await fetcher.DownloadAsync();
        return installed.GetExecutablePath();
    }

    // Lanza un Chromium headless (sistema si esta configurado, o el de BrowserFetcher).
    private async Task<IBrowser> LaunchAsync()
    {
        var options = new LaunchOptions
        {
            Headless = true,
            Args = new[] { "--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage" }
        };

        var exe = ConfiguredChromePath();
        if (exe is not null)
        {
            options.ExecutablePath = exe;
        }
        else
        {
            await new BrowserFetcher().DownloadAsync();
        }

        return await Puppeteer.LaunchAsync(options);
    }

    private string? ConfiguredChromePath()
    {
        var configured = Environment.GetEnvironmentVariable("VISAL_CHROME_PATH") ?? _config["Chrome:ExecutablePath"];
        return !string.IsNullOrWhiteSpace(configured) && File.Exists(configured) ? configured : null;
    }
}
