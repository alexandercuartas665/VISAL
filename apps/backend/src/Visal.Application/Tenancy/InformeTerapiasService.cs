using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;

namespace Visal.Application.Tenancy;

public sealed class InformeTerapiasService : IInformeTerapiasService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;
    private readonly ISecretProtector _protector;

    public InformeTerapiasService(IApplicationDbContext db, ITenantContext tenant, ISecretProtector protector)
    {
        _db = db;
        _tenant = tenant;
        _protector = protector;
    }

    // El token lleva tenant + vencimiento, cifrado con ISecretProtector. Prefijo
    // "inf1" para poder versionar/validar el formato al desproteger.
    public string GenerarEnlace(string baseUri, Guid? tenantId = null, int diasValidez = 30)
    {
        var tid = tenantId ?? _tenant.TenantId
            ?? throw new InvalidOperationException("Sin tenant activo para generar el enlace.");
        var exp = DateTimeOffset.UtcNow.AddDays(diasValidez <= 0 ? 30 : diasValidez).ToUnixTimeSeconds();
        var token = _protector.Protect($"inf1|{tid:N}|{exp}");
        var b = (baseUri ?? "").TrimEnd('/');
        return $"{b}/informe/terapias-pendientes?t={Uri.EscapeDataString(token)}";
    }

    public Guid? ValidarToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) { return null; }
        string plain;
        try { plain = _protector.Unprotect(token); }
        catch { return null; }
        var parts = plain.Split('|');
        if (parts.Length != 3 || parts[0] != "inf1") { return null; }
        if (!Guid.TryParse(parts[1], out var tid)) { return null; }
        if (!long.TryParse(parts[2], out var exp)) { return null; }
        if (DateTimeOffset.FromUnixTimeSeconds(exp) < DateTimeOffset.UtcNow) { return null; }
        return tid;
    }

    public async Task<InformeTerapiasResult?> ObtenerAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Tenant (no es tenant-scoped): se consulta directo por Id.
        var tenant = await _db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.Name, t.LogoUrl })
            .FirstOrDefaultAsync(ct);
        if (tenant is null) { return null; }

        var hoy = DateOnly.FromDateTime(DateTime.Now);

        // Consultas con IgnoreQueryFilters + filtro explicito por tenant: la pagina
        // es anonima (sin cookie de tenant), asi que el filtro global no aplica.
        var turnos = await _db.AsignacionTurnos.AsNoTracking().IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId)
            .Select(t => new { t.Id, t.AsignacionId, t.ProfesionalId, t.Cantidad })
            .ToListAsync(ct);
        if (turnos.Count == 0)
        {
            return new InformeTerapiasResult(tenant.Name, tenant.LogoUrl, hoy, Array.Empty<TerapiaPendienteDto>());
        }

        var turnoIds = turnos.Select(t => t.Id).ToList();
        var asigIds = turnos.Select(t => t.AsignacionId).Distinct().ToList();

        var asigs = await _db.Asignaciones.AsNoTracking().IgnoreQueryFilters()
            .Where(a => a.TenantId == tenantId && asigIds.Contains(a.Id))
            .Select(a => new { a.Id, a.PacienteId, a.NombreServicio, a.TipoServicio, a.Modulo })
            .ToListAsync(ct);

        // Solo terapias: por tipo/modulo == TERAPIA o por nombre que contenga TERAPIA
        // (cubre FISIOTERAPIA, TERAPIA RESPIRATORIA/OCUPACIONAL/FISICA, etc.).
        static bool EsTerapia(string? tipo, string? modulo, string? nombre)
            => Contiene(tipo, "TERAPIA") || Contiene(modulo, "TERAPIA") || Contiene(nombre, "TERAPIA");
        var asigTerapia = asigs.Where(a => EsTerapia(a.TipoServicio, a.Modulo, a.NombreServicio)).ToList();
        if (asigTerapia.Count == 0)
        {
            return new InformeTerapiasResult(tenant.Name, tenant.LogoUrl, hoy, Array.Empty<TerapiaPendienteDto>());
        }
        var asigTerapiaIds = asigTerapia.Select(a => a.Id).ToHashSet();

        var sesionesRaw = await _db.AsignacionTurnoSesiones.AsNoTracking().IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId && turnoIds.Contains(s.AsignacionTurnoId))
            .Select(s => new { s.AsignacionTurnoId, s.Completado, s.FechaAtencion })
            .ToListAsync(ct);
        var sesionesPorTurno = sesionesRaw
            .GroupBy(s => s.AsignacionTurnoId)
            .ToDictionary(g => g.Key, g => g.Select(x => (Completado: x.Completado, Fecha: x.FechaAtencion)).ToList());

        var pacIds = asigTerapia.Select(a => a.PacienteId).Distinct().ToList();
        var pacientes = (await _db.Pacientes.AsNoTracking().IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && pacIds.Contains(p.Id))
            .Select(p => new { p.Id, p.NombreCompleto, p.NumeroDocumento })
            .ToListAsync(ct))
            .ToDictionary(p => p.Id, p => (Nombre: p.NombreCompleto, Doc: p.NumeroDocumento));

        var profIds = turnos.Where(t => t.ProfesionalId != Guid.Empty).Select(t => t.ProfesionalId).Distinct().ToList();
        var profesionales = profIds.Count == 0
            ? new Dictionary<Guid, string>()
            : (await _db.Profesionales.AsNoTracking().IgnoreQueryFilters()
                .Where(p => p.TenantId == tenantId && profIds.Contains(p.Id))
                .Select(p => new { p.Id, p.NombreCompleto })
                .ToListAsync(ct))
                .ToDictionary(p => p.Id, p => p.NombreCompleto);

        // Turnos agrupados por asignacion.
        var turnosPorAsig = turnos.Where(t => asigTerapiaIds.Contains(t.AsignacionId))
            .GroupBy(t => t.AsignacionId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var filas = new List<TerapiaPendienteDto>();
        foreach (var a in asigTerapia)
        {
            if (!turnosPorAsig.TryGetValue(a.Id, out var ts)) { continue; }

            var totalEsperado = ts.Sum(t => t.Cantidad);
            var sesionesDeAsig = ts
                .SelectMany(t => sesionesPorTurno.TryGetValue(t.Id, out var l) ? l : new List<(bool Completado, DateOnly Fecha)>())
                .ToList();
            var completadas = sesionesDeAsig.Count(x => x.Completado);
            if (totalEsperado <= 0) { totalEsperado = sesionesDeAsig.Count; }
            var pendientes = totalEsperado - completadas;
            if (pendientes <= 0) { continue; }

            DateOnly? ultima = sesionesDeAsig.Where(x => x.Completado)
                .Select(x => (DateOnly?)x.Fecha)
                .DefaultIfEmpty(null)
                .Max();

            var pac = pacientes.TryGetValue(a.PacienteId, out var pi) ? pi : (Nombre: "(sin paciente)", Doc: "");
            var profId = ts.Select(t => t.ProfesionalId).FirstOrDefault(id => id != Guid.Empty);
            var prof = profId != Guid.Empty && profesionales.TryGetValue(profId, out var pn) ? pn : "";

            filas.Add(new TerapiaPendienteDto(pac.Nombre, pac.Doc, a.NombreServicio, pendientes, ultima, prof));
        }

        filas = filas.OrderBy(f => f.Paciente, StringComparer.OrdinalIgnoreCase).ToList();
        return new InformeTerapiasResult(tenant.Name, tenant.LogoUrl, hoy, filas);
    }

    private static bool Contiene(string? s, string term)
        => !string.IsNullOrWhiteSpace(s) && s.Contains(term, StringComparison.OrdinalIgnoreCase);
}
