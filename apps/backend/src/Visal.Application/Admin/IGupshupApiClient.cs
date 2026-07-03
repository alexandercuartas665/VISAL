namespace Visal.Application.Admin;

/// <summary>Resultado uniforme de un send Gupshup. MessageId es el id que
/// devuelve la API en el body de exito (para correlar acuses y logs).</summary>
public sealed record GupshupSendResult(bool Ok, string? Error, string? MessageId = null);

/// <summary>Metadatos de una plantilla HSM tal como Gupshup los devuelve en
/// GET /wa/app/{appName}/template. La UI la muestra como tarjeta.</summary>
/// <param name="Id">UUID interno de la plantilla en Gupshup. Requerido para enviarla.</param>
/// <param name="ElementName">Nombre corto (slug, sin espacios). Meta lo referencia asi.</param>
/// <param name="LanguageCode">ej. "es", "es_CO", "en".</param>
/// <param name="Category">MARKETING | UTILITY | AUTHENTICATION.</param>
/// <param name="Status">APPROVED | PENDING | REJECTED | PAUSED | DELETED.</param>
/// <param name="Body">Texto del cuerpo con placeholders {{1}}, {{2}}, ...</param>
/// <param name="ParameterCount">Cantidad de placeholders detectados en el body.</param>
public sealed record GupshupTemplateInfo(
    string Id, string ElementName, string LanguageCode, string Category,
    string Status, string Body, int ParameterCount);

/// <summary>Resultado de un listado de plantillas.</summary>
public sealed record GupshupTemplateListResult(bool Ok, string? Error, IReadOnlyList<GupshupTemplateInfo> Templates);

/// <summary>
/// Payload para crear una plantilla en Gupshup. Cumplir las reglas Meta:
/// nombre kebab/snake_case, cuerpo &lt;=1024 chars, placeholders {{1}}{{2}}...
/// contiguos, ejemplos alineados. Meta puede rechazar aun cumpliendo.
/// </summary>
public sealed record GupshupCreateTemplateRequest(
    string ElementName, string LanguageCode, string Category,
    string TemplateType, string Content, string Example,
    string? ExampleHeader = null, string? Header = null, string? Footer = null,
    IReadOnlyList<string>? Buttons = null);

/// <summary>Resultado de crear una plantilla. Status inicial siempre PENDING
/// (Meta la revisa 1-24h). Id es el UUID que Gupshup asigna.</summary>
public sealed record GupshupCreateTemplateResult(bool Ok, string? Error, string? Id, string? Status);

/// <summary>Cliente HTTP contra api.gupshup.io. Solo mensajes salientes (send
/// text/media/template). Templates listing+create llegan en G5. Autenticacion
/// via header 'apikey' con la clave de la App (TenantGupshupConfig.ApiKey).
/// No loguea la apikey ni el destination completo (regla seguridad Visal).</summary>
public interface IGupshupApiClient
{
    /// <summary>
    /// Envia un mensaje de texto de sesion (dentro de la ventana 24h que
    /// abrio el cliente). Endpoint POST /wa/api/v1/msg. Fuera de la ventana
    /// hay que usar SendTemplateAsync.
    /// </summary>
    /// <param name="apiKey">apikey descifrada de la App Gupshup.</param>
    /// <param name="source">Numero de la App (E.164 sin '+'). Debe coincidir con el WABA registrado.</param>
    /// <param name="destination">Numero destino (E.164 sin '+').</param>
    /// <param name="text">Cuerpo del mensaje. Gupshup limita a 4096 chars.</param>
    Task<GupshupSendResult> SendTextAsync(
        string apiKey, string source, string destination, string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Envia media (image/video/document/audio) por URL publica. Gupshup NO
    /// acepta base64 -- exige URL alcanzable desde internet. El caller debe
    /// haber subido el archivo antes.
    /// </summary>
    /// <param name="mediaType">'image' | 'video' | 'audio' | 'file' (Gupshup usa 'file' para PDF/doc).</param>
    /// <param name="publicUrl">URL absoluta HTTPS accesible desde internet.</param>
    /// <param name="caption">Opcional. Aplica a image/video/file. Audio la ignora.</param>
    /// <param name="fileName">Requerido para 'file'; ignorado para el resto.</param>
    Task<GupshupSendResult> SendMediaAsync(
        string apiKey, string source, string destination,
        string mediaType, string publicUrl, string? caption, string? fileName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Envia un template HSM aprobado (mensaje iniciado por el negocio fuera
    /// de la ventana 24h). Endpoint POST /wa/api/v2/template/msg.
    /// </summary>
    /// <param name="templateId">UUID de la plantilla en Gupshup (no el nombre).</param>
    /// <param name="parameters">Valores para los placeholders {{1}}, {{2}}, ... en orden. Vacio si no tiene.</param>
    Task<GupshupSendResult> SendTemplateAsync(
        string apiKey, string source, string destination,
        string templateId, IReadOnlyList<string> parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lista las plantillas HSM del WABA asociado a la App. Usa User API
    /// (header apikey). Devuelve estados actuales (APPROVED/PENDING/etc)
    /// tal como Meta los tiene.
    /// </summary>
    /// <param name="apiKey">apikey de la App.</param>
    /// <param name="appName">Nombre de la App tal como esta en Gupshup dashboard.</param>
    Task<GupshupTemplateListResult> ListTemplatesAsync(
        string apiKey, string appName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Crea una plantilla HSM. El body inicial se manda en PENDING y Meta la
    /// revisa (~1-24h). No hay forma de forzar aprobacion desde aca.
    /// </summary>
    Task<GupshupCreateTemplateResult> CreateTemplateAsync(
        string apiKey, string appName, GupshupCreateTemplateRequest request,
        CancellationToken cancellationToken = default);
}
