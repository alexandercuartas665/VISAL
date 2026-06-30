using System.Security.Cryptography;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Visal.Application.Tenancy;

public sealed class FirmaRemotaService : IFirmaRemotaService
{
    /// <summary>Duracion del link de firma. 2 horas (acordado con negocio).</summary>
    private static readonly TimeSpan _vigencia = TimeSpan.FromHours(2);

    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly IChatService _chat;
    private readonly Common.IUploadStorage _storage;
    private readonly TimeProvider _time;

    public FirmaRemotaService(IApplicationDbContext db, ITenantContext tenant, IChatService chat, Common.IUploadStorage storage, TimeProvider time)
    {
        _db = db;
        _tenant = tenant;
        _chat = chat;
        _storage = storage;
        _time = time;
    }

    public async Task<FirmaRequestDto?> CrearOReutilizarAsync(Guid notaMedicaId, Guid pacienteId, string telefono, string? nombreContacto, Guid actorTenantUserId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var digits = Digits(telefono);
        if (digits.Length == 0) { return null; }

        // Si ya hay una pendiente sin expirar para esta nota, la devolvemos.
        var ahora = _time.GetUtcNow();
        var existente = await _db.FirmaPacienteRequests
            .FirstOrDefaultAsync(r => r.NotaMedicaId == notaMedicaId
                                      && r.Status == FirmaRequestStatus.Pendiente
                                      && r.ExpiresAt > ahora, ct);
        if (existente is not null)
        {
            return Map(existente);
        }

        var req = new FirmaPacienteRequest
        {
            TenantId = tenantId,
            Token = NewToken(),
            PacienteId = pacienteId,
            NotaMedicaId = notaMedicaId,
            Telefono = digits,
            NombreContacto = nombreContacto,
            SolicitadaPorTenantUserId = actorTenantUserId == Guid.Empty ? null : actorTenantUserId,
            CreatedAt = ahora,
            ExpiresAt = ahora + _vigencia,
            Status = FirmaRequestStatus.Pendiente
        };
        _db.FirmaPacienteRequests.Add(req);
        await _db.SaveChangesAsync(ct);
        return Map(req);
    }

    public async Task<ChatSendResult> EnviarPorWhatsAppAsync(Guid solicitudId, Guid lineaId, string urlAbsoluta, Guid actorTenantUserId, CancellationToken ct = default)
    {
        var req = await _db.FirmaPacienteRequests.FirstOrDefaultAsync(r => r.Id == solicitudId, ct);
        if (req is null) { return new ChatSendResult(false, null, "Solicitud no encontrada."); }
        if (req.Status != FirmaRequestStatus.Pendiente) { return new ChatSendResult(false, null, "La solicitud ya no esta pendiente."); }

        // Crear / obtener la conversacion por telefono y enviar el mensaje.
        var conv = await _chat.GetOrCreateByPhoneAsync(req.Telefono, req.NombreContacto, ct);
        if (conv is null) { return new ChatSendResult(false, null, "No se pudo crear la conversacion del paciente."); }

        var saludo = string.IsNullOrWhiteSpace(req.NombreContacto)
            ? "Hola"
            : "Hola " + req.NombreContacto!.Split(' ').FirstOrDefault();
        var texto = $"{saludo}, en IPS Visal RT necesitamos su firma para confirmar la atencion recibida. "
                  + $"Por favor abra este link en su celular y firme con el dedo: {urlAbsoluta} "
                  + $"El link vence en 2 horas.";
        return await _chat.SendViaLineAsync(conv.Id, lineaId, texto, actorTenantUserId, ct);
    }

    public async Task<bool> CancelarAsync(Guid solicitudId, Guid actorTenantUserId, CancellationToken ct = default)
    {
        var req = await _db.FirmaPacienteRequests.FirstOrDefaultAsync(r => r.Id == solicitudId, ct);
        if (req is null || req.Status != FirmaRequestStatus.Pendiente) { return false; }
        req.Status = FirmaRequestStatus.Cancelada;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<FirmaRequestStateDto?> ObtenerEstadoAsync(Guid solicitudId, CancellationToken ct = default)
    {
        var req = await _db.FirmaPacienteRequests.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == solicitudId, ct);
        if (req is null) { return null; }
        // Si esta pendiente pero ya expiro, marcamos antes de devolver (tracking).
        if (req.Status == FirmaRequestStatus.Pendiente && req.ExpiresAt < _time.GetUtcNow())
        {
            var tracked = await _db.FirmaPacienteRequests.FirstAsync(r => r.Id == solicitudId, ct);
            tracked.Status = FirmaRequestStatus.Expirada;
            await _db.SaveChangesAsync(ct);
            req = tracked;
        }
        return new FirmaRequestStateDto(req.Id, req.Status, req.CompletedAt, req.ImageDataUrl);
    }

    public async Task<FirmaRequestDto?> ObtenerActivaPorNotaAsync(Guid notaMedicaId, CancellationToken ct = default)
    {
        var ahora = _time.GetUtcNow();
        var req = await _db.FirmaPacienteRequests.AsNoTracking()
            .Where(r => r.NotaMedicaId == notaMedicaId
                        && (r.Status == FirmaRequestStatus.Pendiente || r.Status == FirmaRequestStatus.Completada))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return req is null ? null : Map(req);
    }

    public async Task<FirmaRequestDto?> CrearLibreParaPacienteAsync(Guid pacienteId, string telefono, string? nombreContacto, Guid actorTenantUserId, CancellationToken ct = default)
    {
        if (_tenant.TenantId is not Guid tenantId) { return null; }
        var digits = Digits(telefono);
        if (digits.Length == 0) { return null; }

        // Reutiliza la solicitud "libre" (NotaMedicaId == null) si todavia esta vigente.
        var ahora = _time.GetUtcNow();
        var existente = await _db.FirmaPacienteRequests
            .FirstOrDefaultAsync(r => r.PacienteId == pacienteId
                                      && r.NotaMedicaId == null
                                      && r.Status == FirmaRequestStatus.Pendiente
                                      && r.ExpiresAt > ahora, ct);
        if (existente is not null) { return Map(existente); }

        var req = new FirmaPacienteRequest
        {
            TenantId = tenantId,
            Token = NewToken(),
            PacienteId = pacienteId,
            NotaMedicaId = null,
            Telefono = digits,
            NombreContacto = nombreContacto,
            SolicitadaPorTenantUserId = actorTenantUserId == Guid.Empty ? null : actorTenantUserId,
            CreatedAt = ahora,
            ExpiresAt = ahora + _vigencia,
            Status = FirmaRequestStatus.Pendiente
        };
        _db.FirmaPacienteRequests.Add(req);
        await _db.SaveChangesAsync(ct);
        return Map(req);
    }

    public async Task<FirmaRequestDto?> ObtenerActivaLibrePorPacienteAsync(Guid pacienteId, CancellationToken ct = default)
    {
        var req = await _db.FirmaPacienteRequests.AsNoTracking()
            .Where(r => r.PacienteId == pacienteId
                        && r.NotaMedicaId == null
                        && (r.Status == FirmaRequestStatus.Pendiente || r.Status == FirmaRequestStatus.Completada))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
        return req is null ? null : Map(req);
    }

    public async Task<FirmaRequestPublicDto?> ObtenerPorTokenPublicoAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) { return null; }

        // Token es globalmente unico. IgnoreQueryFilters porque la pagina /firma/{token}
        // se sirve anonimamente (sin claim tenant_id). El tenant lo obtenemos del propio
        // registro de la solicitud, no del usuario.
        var req = await _db.FirmaPacienteRequests
            .IgnoreQueryFilters()
            .Where(r => r.Token == token)
            .Select(r => new
            {
                r.Id, r.Token, r.PacienteId, r.NotaMedicaId, r.TenantId,
                r.ExpiresAt, r.Status, r.SolicitadaPorTenantUserId
            })
            .FirstOrDefaultAsync(ct);
        if (req is null) { return null; }

        // Si esta pendiente pero ya expiro, marcamos antes de devolver.
        if (req.Status == FirmaRequestStatus.Pendiente && req.ExpiresAt < _time.GetUtcNow())
        {
            var tracked = await _db.FirmaPacienteRequests.IgnoreQueryFilters().FirstAsync(r => r.Id == req.Id, ct);
            tracked.Status = FirmaRequestStatus.Expirada;
            await _db.SaveChangesAsync(ct);
        }

        var paciente = await _db.Pacientes.IgnoreQueryFilters()
            .Where(p => p.Id == req.PacienteId)
            .Select(p => p.NombreCompleto)
            .FirstOrDefaultAsync(ct);

        string? profesional = null;
        if (req.SolicitadaPorTenantUserId is Guid uid)
        {
            profesional = await _db.TenantUsers.IgnoreQueryFilters()
                .Where(u => u.Id == uid)
                .Select(u => u.PlatformUser != null ? u.PlatformUser.DisplayName : null)
                .FirstOrDefaultAsync(ct);
        }

        var tenantName = await _db.Tenants.IgnoreQueryFilters()
            .Where(t => t.Id == req.TenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct);

        return new FirmaRequestPublicDto(
            req.Id, req.Token,
            paciente ?? "Paciente",
            profesional,
            tenantName,
            req.ExpiresAt,
            req.Status);
    }

    public async Task<bool> GuardarFirmaPorTokenAsync(string token, string imageDataUrl, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(imageDataUrl)) { return false; }
        // Validacion basica del formato data URL.
        if (!imageDataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) { return false; }

        var req = await _db.FirmaPacienteRequests
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Token == token, ct);
        if (req is null) { return false; }
        if (req.Status != FirmaRequestStatus.Pendiente) { return false; }
        if (req.ExpiresAt < _time.GetUtcNow())
        {
            req.Status = FirmaRequestStatus.Expirada;
            await _db.SaveChangesAsync(ct);
            return false;
        }

        // Persistir la firma en la solicitud y en la nota medica (campo oficial).
        req.ImageDataUrl = imageDataUrl;
        req.Status = FirmaRequestStatus.Completada;
        req.CompletedAt = _time.GetUtcNow();

        // Si la solicitud esta atada a una nota, replicamos la firma en el campo
        // oficial de la nota y dejamos el doc adjunto. Las solicitudes "libres"
        // (pedidas desde el panel WhatsApp del paciente sin nota especifica)
        // solo dejan la imagen en FirmaPacienteRequest.ImageDataUrl + el
        // NotaMedicaDocumento queda atado al paciente sin NotaMedica.
        var notaIdParaDoc = req.NotaMedicaId;
        if (req.NotaMedicaId is Guid notaId)
        {
            var nota = await _db.NotasMedicas.IgnoreQueryFilters()
                .FirstOrDefaultAsync(n => n.Id == notaId, ct);
            if (nota is not null)
            {
                nota.FirmaPacienteDataUrl = imageDataUrl;
            }
        }

        // Adicional: guardar la firma como PNG fisico. Si hay nota, queda como
        // NotaMedicaDocumento de esa nota (visible desde "Documentos Externos");
        // si no hay nota, queda como adjunto del paciente sin asociar a nota.
        try
        {
            var bytes = DecodeDataUrl(imageDataUrl);
            if (bytes is not null && bytes.Length > 0 && notaIdParaDoc is Guid notaDocId)
            {
                var nombre = $"firma-paciente-{req.Id:N}.png";
                var rutaWeb = await _storage.GuardarAsync("notas", nombre, bytes, ct);

                _db.NotaMedicaDocumentos.Add(new NotaMedicaDocumento
                {
                    TenantId = req.TenantId,
                    NotaMedicaId = notaDocId,
                    PacienteId = req.PacienteId,
                    NombreOriginal = $"Firma remota del paciente ({_time.GetUtcNow().LocalDateTime:dd-MM-yyyy HH:mm}).png",
                    RutaArchivo = rutaWeb,
                    TipoMime = "image/png",
                    Tamano = bytes.Length,
                    Categoria = "Firma del Paciente",
                    Anotaciones = "Firma capturada remotamente por WhatsApp."
                });
            }
        }
        catch { /* el doc externo es secundario: si falla, la firma del campo ya quedo */ }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Decodifica el payload base64 de una data URL ("data:image/png;base64,…") a bytes.</summary>
    private static byte[]? DecodeDataUrl(string dataUrl)
    {
        var idx = dataUrl.IndexOf(',');
        if (idx < 0 || idx == dataUrl.Length - 1) { return null; }
        try { return Convert.FromBase64String(dataUrl[(idx + 1)..]); }
        catch { return null; }
    }

    // ===== Helpers =====
    private static string Digits(string s) =>
        new string((s ?? string.Empty).Where(char.IsDigit).ToArray());

    /// <summary>32 caracteres hex aleatorios (~128 bits). Imposible de adivinar.</summary>
    private static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    private static FirmaRequestDto Map(FirmaPacienteRequest r) =>
        new(r.Id, r.PacienteId, r.NotaMedicaId, r.Token, r.Telefono, r.NombreContacto,
            r.CreatedAt, r.ExpiresAt, r.CompletedAt, r.Status, $"/firma/{r.Token}");
}
