using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy;

/// <summary>
/// Emision, revocacion y verificacion publica de ordenes de medicamentos (ORD-MEDI).
/// La emision congela un snapshot inmutable del formulario ya prellenado y mintea un
/// codigo corto legible que se codifica en un QR. La verificacion publica busca por
/// codigo sin tenant scope (IgnoreQueryFilters) y solo expone datos seguros.
/// </summary>
public sealed class OrdenMedicamentoPublicaService(
    IApplicationDbContext db,
    ITenantContext tenant,
    IAuditWriter audit,
    TimeProvider clock) : IOrdenMedicamentoPublicaService
{
    /// <summary>
    /// Clave convencional en el bag del formulario donde se inyecta el codigo de
    /// verificacion. El nodo "qr" del schema ORD-MEDI usa este Name (o lo referencia
    /// via qrSource) para pintar el QR.
    /// </summary>
    public const string QrBagKey = "orden_codigo_verificacion";

    // Base32 sin caracteres ambiguos (sin 0/O/1/I/L). 31 simbolos.
    private const string Alfabeto = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";
    private const int LargoCodigo = 12;

    public async Task<EmitirOrdenResultDto> EmitirAsync(
        EmitirOrdenRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        if (tenant.TenantId is not Guid tid)
        {
            throw new InvalidOperationException("Sin tenant activo.");
        }

        // La HC debe existir y pertenecer al tenant (query scoped).
        var hc = await db.HistoriasClinicas.AsNoTracking()
            .FirstOrDefaultAsync(h => h.Id == req.HistoriaClinicaId, ct)
            ?? throw new InvalidOperationException("La historia clinica no existe.");

        if (req.Items.Count == 0)
        {
            throw new InvalidOperationException("La orden no tiene medicamentos para emitir.");
        }

        var now = clock.GetUtcNow();
        var codigo = await GenerarCodigoUnicoAsync(ct);

        // Inyectar el codigo en el bag para que el nodo qr lo pinte.
        var bag = DeserializarBag(req.BagJson);
        bag[QrBagKey] = codigo;

        var snapshot = new Snapshot(
            bag,
            new PublicoSnap(
                req.FechaEmision,
                req.PacienteIniciales,
                req.ProfesionalNombre,
                req.ProfesionalRegistro,
                req.Items.ToList()));

        var orden = new OrdenMedicamentoPublica
        {
            TenantId = tid,
            HistoriaClinicaId = hc.Id,
            CodigoVerificacion = codigo,
            EmitidoAt = now,
            EmitidoPor = actorUserId,
            SnapshotJson = JsonSerializer.Serialize(snapshot, JsonOpts)
        };
        db.OrdenesMedicamentosPublicas.Add(orden);

        audit.Write(actorUserId, "orden-medicamentos.emitir", nameof(OrdenMedicamentoPublica),
            orden.Id, previousValue: null,
            newValue: new { orden.CodigoVerificacion, orden.HistoriaClinicaId },
            tenantId: tid);

        await db.SaveChangesAsync(ct);
        return new EmitirOrdenResultDto(orden.Id, codigo, now);
    }

    public async Task<bool> RevocarAsync(
        Guid ordenPublicaId, Guid actorUserId, string? motivo, CancellationToken ct = default)
    {
        var orden = await db.OrdenesMedicamentosPublicas
            .FirstOrDefaultAsync(o => o.Id == ordenPublicaId, ct);
        if (orden is null || orden.RevocadaAt is not null)
        {
            return false;
        }

        orden.RevocadaAt = clock.GetUtcNow();
        orden.RevocadaPor = actorUserId;
        orden.RevocacionMotivo = string.IsNullOrWhiteSpace(motivo) ? null : motivo.Trim();

        audit.Write(actorUserId, "orden-medicamentos.revocar", nameof(OrdenMedicamentoPublica),
            orden.Id, previousValue: null,
            newValue: new { orden.CodigoVerificacion, orden.HistoriaClinicaId },
            tenantId: orden.TenantId, reason: orden.RevocacionMotivo);

        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<OrdenPublicaResumenDto?> ObtenerEmisionActivaAsync(
        Guid historiaClinicaId, CancellationToken ct = default)
    {
        var orden = await db.OrdenesMedicamentosPublicas.AsNoTracking()
            .Where(o => o.HistoriaClinicaId == historiaClinicaId && o.RevocadaAt == null)
            .OrderByDescending(o => o.EmitidoAt)
            .FirstOrDefaultAsync(ct);
        if (orden is null) { return null; }

        var snap = DeserializarSnapshot(orden.SnapshotJson);
        var bagJson = JsonSerializer.Serialize(snap.Bag);
        return new OrdenPublicaResumenDto(orden.Id, orden.CodigoVerificacion, orden.EmitidoAt, false, bagJson);
    }

    public async Task<bool> TieneEmisionActivaAsync(Guid historiaClinicaId, CancellationToken ct = default)
    {
        return await db.OrdenesMedicamentosPublicas
            .AnyAsync(o => o.HistoriaClinicaId == historiaClinicaId && o.RevocadaAt == null, ct);
    }

    public async Task<OrdenVerificacionPublicaDto?> VerificarPorCodigoAsync(
        string codigo, string? ip, string? userAgent, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(codigo)) { return null; }
        var norm = codigo.Trim().ToUpperInvariant();

        // Publico: sin tenant scope. El codigo es globalmente unico.
        var orden = await db.OrdenesMedicamentosPublicas.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(o => o.CodigoVerificacion == norm, ct);
        if (orden is null) { return null; }

        var snap = DeserializarSnapshot(orden.SnapshotJson);
        var p = snap.Publico;

        var tenantData = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .Where(t => t.Id == orden.TenantId)
            .Select(t => new { t.Name, t.LogoUrl })
            .FirstOrDefaultAsync(ct);

        // Registrar la consulta (trazabilidad). Entidad global: fijamos TenantId a mano.
        db.VerificacionOrdenLogs.Add(new VerificacionOrdenLog
        {
            OrdenMedicamentoPublicaId = orden.Id,
            TenantId = orden.TenantId,
            CodigoConsultado = norm,
            Ip = Truncar(ip, 64),
            UserAgent = Truncar(userAgent, 500),
            ConsultadoEn = clock.GetUtcNow()
        });
        await db.SaveChangesAsync(ct);

        return new OrdenVerificacionPublicaDto(
            orden.CodigoVerificacion,
            p.FechaEmision,
            p.PacienteIniciales,
            p.ProfesionalNombre,
            p.ProfesionalRegistro,
            p.Items ?? new List<OrdenItemPublicoDto>(),
            orden.RevocadaAt is not null,
            tenantData?.Name,
            tenantData?.LogoUrl);
    }

    // ── Helpers ──

    private async Task<string> GenerarCodigoUnicoAsync(CancellationToken ct)
    {
        for (var intento = 0; intento < 8; intento++)
        {
            var candidato = GenerarCodigo();
            var existe = await db.OrdenesMedicamentosPublicas.IgnoreQueryFilters()
                .AnyAsync(o => o.CodigoVerificacion == candidato, ct);
            if (!existe) { return candidato; }
        }
        throw new InvalidOperationException("No se pudo generar un codigo de verificacion unico.");
    }

    /// <summary>12 caracteres del alfabeto base32 sin ambiguos, con RNG criptografico.</summary>
    public static string GenerarCodigo()
    {
        var chars = new char[LargoCodigo];
        for (var i = 0; i < LargoCodigo; i++)
        {
            chars[i] = Alfabeto[RandomNumberGenerator.GetInt32(Alfabeto.Length)];
        }
        return new string(chars);
    }

    private static string? Truncar(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);

    private static readonly JsonSerializerOptions JsonOpts = new();

    private static Dictionary<string, string?> DeserializarBag(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
            return dict is null
                ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string?>(dict, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static Snapshot DeserializarSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new Snapshot(new Dictionary<string, string?>(), new PublicoSnap(default, "", null, null, new()));
        }
        try
        {
            return JsonSerializer.Deserialize<Snapshot>(json, JsonOpts)
                ?? new Snapshot(new Dictionary<string, string?>(), new PublicoSnap(default, "", null, null, new()));
        }
        catch
        {
            return new Snapshot(new Dictionary<string, string?>(), new PublicoSnap(default, "", null, null, new()));
        }
    }

    private sealed record Snapshot(Dictionary<string, string?> Bag, PublicoSnap Publico);

    private sealed record PublicoSnap(
        DateTimeOffset FechaEmision,
        string PacienteIniciales,
        string? ProfesionalNombre,
        string? ProfesionalRegistro,
        List<OrdenItemPublicoDto> Items);
}
