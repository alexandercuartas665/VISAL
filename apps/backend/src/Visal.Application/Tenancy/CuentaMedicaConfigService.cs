using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;
using Visal.Domain.Enums;

namespace Visal.Application.Tenancy;

/// <summary>
/// CRUD de la configuracion "Cuenta medica" por aseguradora. Fase 1: solo
/// captura de datos; el generador de PDF/ZIP es fase 2.
/// </summary>
public sealed class CuentaMedicaConfigService : ICuentaMedicaConfigService
{
    private readonly IApplicationDbContext _db;
    private readonly ITenantContext _tenant;

    public CuentaMedicaConfigService(IApplicationDbContext db, ITenantContext tenant)
    {
        _db = db;
        _tenant = tenant;
    }

    private Guid RequireTenant()
    {
        if (_tenant.TenantId is not Guid tid) { throw new InvalidOperationException("Sin tenant activo."); }
        return tid;
    }

    public async Task<CuentaMedicaConfigDto> GetOrCreateAsync(
        Guid aseguradoraId, CancellationToken ct = default)
    {
        var cfg = await GetOrCreateEntityAsync(aseguradoraId, ct);
        return MapConfig(cfg);
    }

    private async Task<AseguradoraCuentaMedicaConfig> GetOrCreateEntityAsync(
        Guid aseguradoraId, CancellationToken ct)
    {
        var tid = RequireTenant();
        var cfg = await _db.AseguradoraCuentaMedicaConfigs
            .FirstOrDefaultAsync(x => x.AseguradoraId == aseguradoraId, ct);
        if (cfg is not null) { return cfg; }

        // Verifica que la aseguradora exista en el tenant activo antes de crear.
        var existe = await _db.Aseguradoras.AsNoTracking()
            .AnyAsync(a => a.Id == aseguradoraId, ct);
        if (!existe) { throw new InvalidOperationException("Aseguradora no encontrada."); }

        cfg = new AseguradoraCuentaMedicaConfig
        {
            AseguradoraId = aseguradoraId,
            TenantId = tid,
            PortadaHabilitada = false,
            IndiceHabilitado = false,
        };
        _db.AseguradoraCuentaMedicaConfigs.Add(cfg);
        await _db.SaveChangesAsync(ct);
        return cfg;
    }

    public async Task<CuentaMedicaConfigDto> GuardarPortadaAsync(
        GuardarPortadaRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var cfg = await GetOrCreateEntityAsync(req.AseguradoraId, ct);
        cfg.PortadaHabilitada = req.PortadaHabilitada;
        cfg.PortadaLogoUrl = string.IsNullOrWhiteSpace(req.PortadaLogoUrl) ? null : req.PortadaLogoUrl.Trim();
        cfg.PortadaTitulo = NullIfBlank(req.PortadaTitulo);
        cfg.PortadaSubtitulo = NullIfBlank(req.PortadaSubtitulo);
        cfg.PortadaTextoLegal = NullIfBlank(req.PortadaTextoLegal);
        cfg.IndiceHabilitado = req.IndiceHabilitado;
        cfg.PatronNombreDefault = NullIfBlank(req.PatronNombreDefault);
        await _db.SaveChangesAsync(ct);
        return MapConfig(cfg);
    }

    public async Task<IReadOnlyList<InformeItemDto>> ListarItemsAsync(
        Guid aseguradoraId, CancellationToken ct = default)
    {
        var cfg = await GetOrCreateEntityAsync(aseguradoraId, ct);

        var items = await _db.AseguradoraInformeItems.AsNoTracking()
            .Where(i => i.ConfigId == cfg.Id)
            .OrderBy(i => i.Orden).ThenBy(i => i.Alias)
            .ToListAsync(ct);
        if (items.Count == 0) { return Array.Empty<InformeItemDto>(); }

        var itemIds = items.Select(i => i.Id).ToList();
        var contenidos = await _db.AseguradoraInformeContenidos.AsNoTracking()
            .Where(c => itemIds.Contains(c.ItemId))
            .OrderBy(c => c.Orden)
            .ToListAsync(ct);

        // Nombres de tipologia para pintar los chips sin round-trips por fila.
        var tipIds = contenidos.Where(c => c.TipologiaArchivoId != null)
            .Select(c => c.TipologiaArchivoId!.Value).Distinct().ToList();
        var tipNombres = tipIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.TipologiaArchivos.AsNoTracking()
                .Where(t => tipIds.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id, t => t.Nombre, ct);

        var contPorItem = contenidos.GroupBy(c => c.ItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        return items.Select(i => new InformeItemDto(
            i.Id, i.ConfigId, i.Orden, i.Seccion, i.Alias, i.Descripcion,
            i.PatronNombre, i.Obligatorio,
            (contPorItem.TryGetValue(i.Id, out var cs) ? cs : new())
                .Select(c => new InformeContenidoDto(
                    c.Id, c.Orden, c.Origen, c.TipologiaArchivoId,
                    c.TipologiaArchivoId is Guid tg && tipNombres.TryGetValue(tg, out var nm) ? nm : null,
                    c.SoloUltimo))
                .ToList()))
            .ToList();
    }

    public async Task<InformeItemDto> GuardarItemAsync(
        GuardarItemRequest req, Guid actorUserId, CancellationToken ct = default)
    {
        var alias = (req.Alias ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(alias)) { throw new InvalidOperationException("El codigo/alias es obligatorio."); }
        if (alias.Length > 20) { throw new InvalidOperationException("Codigo muy largo (max 20)."); }

        var contenidos = req.Contenidos ?? Array.Empty<GuardarContenidoDto>();
        if (contenidos.Count == 0)
        {
            throw new InvalidOperationException("El archivo debe tener al menos un contenido.");
        }

        var cfg = await GetOrCreateEntityAsync(req.AseguradoraId, ct);
        var tid = RequireTenant();

        AseguradoraInformeItem item;
        if (req.Id is Guid iid)
        {
            var existente = await _db.AseguradoraInformeItems
                .FirstOrDefaultAsync(x => x.Id == iid && x.ConfigId == cfg.Id, ct);
            if (existente is null) { throw new InvalidOperationException("Archivo no encontrado."); }
            item = existente;

            // Reemplaza los contenidos existentes (borrar + insertar).
            var viejos = await _db.AseguradoraInformeContenidos
                .Where(c => c.ItemId == item.Id).ToListAsync(ct);
            if (viejos.Count > 0) { _db.AseguradoraInformeContenidos.RemoveRange(viejos); }
        }
        else
        {
            var maxOrden = await _db.AseguradoraInformeItems
                .Where(x => x.ConfigId == cfg.Id)
                .Select(x => (int?)x.Orden)
                .MaxAsync(ct) ?? -1;
            item = new AseguradoraInformeItem
            {
                ConfigId = cfg.Id,
                TenantId = tid,
                Orden = maxOrden + 1,
            };
            _db.AseguradoraInformeItems.Add(item);
        }

        item.Seccion = NullIfBlank(req.Seccion);
        item.Alias = alias;
        item.Descripcion = NullIfBlank(req.Descripcion);
        item.PatronNombre = NullIfBlank(req.PatronNombre);
        item.Obligatorio = req.Obligatorio;
        // Campos legacy (1 item = 1 origen): reflejan el primer contenido por
        // compatibilidad; la fuente de verdad es la tabla de contenidos.
        var primero = contenidos[0];
        item.Origen = primero.Origen;
        item.TipologiaArchivoId = PideTipologia(primero.Origen) ? primero.TipologiaArchivoId : null;
        item.SoloUltimo = primero.SoloUltimo;

        var orden = 0;
        foreach (var c in contenidos)
        {
            _db.AseguradoraInformeContenidos.Add(new AseguradoraInformeContenido
            {
                Item = item,
                TenantId = tid,
                Orden = orden++,
                Origen = c.Origen,
                TipologiaArchivoId = PideTipologia(c.Origen) ? c.TipologiaArchivoId : null,
                SoloUltimo = c.SoloUltimo,
            });
        }

        await _db.SaveChangesAsync(ct);

        // Relee para devolver el DTO enriquecido con nombres de tipologia.
        var recargado = (await ListarItemsAsync(req.AseguradoraId, ct))
            .FirstOrDefault(x => x.Id == item.Id);
        return recargado ?? new InformeItemDto(item.Id, item.ConfigId, item.Orden,
            item.Seccion, item.Alias, item.Descripcion, item.PatronNombre,
            item.Obligatorio, Array.Empty<InformeContenidoDto>());
    }

    private static bool PideTipologia(OrigenInformeItem o) => o
        is OrigenInformeItem.DocumentoHc
        or OrigenInformeItem.DocumentoPacienteLibre
        or OrigenInformeItem.DocumentoNota;

    public async Task<bool> EliminarItemAsync(
        Guid itemId, Guid actorUserId, CancellationToken ct = default)
    {
        var e = await _db.AseguradoraInformeItems.FirstOrDefaultAsync(x => x.Id == itemId, ct);
        if (e is null) { return false; }
        _db.AseguradoraInformeItems.Remove(e);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task ReordenarItemsAsync(
        Guid aseguradoraId, IReadOnlyList<Guid> itemIdsEnOrden,
        Guid actorUserId, CancellationToken ct = default)
    {
        if (itemIdsEnOrden is null || itemIdsEnOrden.Count == 0) { return; }
        var cfg = await GetOrCreateEntityAsync(aseguradoraId, ct);
        var items = await _db.AseguradoraInformeItems
            .Where(x => x.ConfigId == cfg.Id).ToListAsync(ct);
        var idx = 0;
        foreach (var id in itemIdsEnOrden)
        {
            var it = items.FirstOrDefault(x => x.Id == id);
            if (it is not null) { it.Orden = idx++; }
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AseguradoraConConfigDto>> ListarAseguradorasConConfigAsync(
        Guid excluirAseguradoraId, CancellationToken ct = default)
    {
        // Aseguradoras que tienen config + al menos 1 item, ordenadas por nombre.
        var q = from a in _db.Aseguradoras.AsNoTracking()
                join c in _db.AseguradoraCuentaMedicaConfigs.AsNoTracking()
                    on a.Id equals c.AseguradoraId
                where a.Id != excluirAseguradoraId
                let count = _db.AseguradoraInformeItems.Count(i => i.ConfigId == c.Id)
                where count > 0
                orderby a.Nombre
                select new AseguradoraConConfigDto(a.Id, a.Nombre, count);
        return await q.ToListAsync(ct);
    }

    public async Task<CuentaMedicaConfigDto> CopiarDeAsync(
        Guid origenAseguradoraId, Guid destinoAseguradoraId,
        Guid actorUserId, CancellationToken ct = default)
    {
        if (origenAseguradoraId == destinoAseguradoraId)
        {
            throw new InvalidOperationException("Origen y destino no pueden ser la misma aseguradora.");
        }
        var origen = await _db.AseguradoraCuentaMedicaConfigs
            .FirstOrDefaultAsync(x => x.AseguradoraId == origenAseguradoraId, ct);
        if (origen is null) { throw new InvalidOperationException("La aseguradora origen no tiene configuracion."); }

        var destino = await GetOrCreateEntityAsync(destinoAseguradoraId, ct);
        var tid = RequireTenant();

        // Portada: sobreescribe todos los campos.
        destino.PortadaHabilitada = origen.PortadaHabilitada;
        destino.PortadaLogoUrl = origen.PortadaLogoUrl;
        destino.PortadaTitulo = origen.PortadaTitulo;
        destino.PortadaSubtitulo = origen.PortadaSubtitulo;
        destino.PortadaTextoLegal = origen.PortadaTextoLegal;
        destino.IndiceHabilitado = origen.IndiceHabilitado;
        destino.PatronNombreDefault = origen.PatronNombreDefault;

        // Items: borra los del destino (cascade borra sus contenidos), clona los
        // del origen con sus contenidos preservando orden.
        var destinoItems = await _db.AseguradoraInformeItems
            .Where(x => x.ConfigId == destino.Id).ToListAsync(ct);
        if (destinoItems.Count > 0) { _db.AseguradoraInformeItems.RemoveRange(destinoItems); }

        var origenItems = await _db.AseguradoraInformeItems.AsNoTracking()
            .Where(x => x.ConfigId == origen.Id).OrderBy(x => x.Orden).ToListAsync(ct);
        var origenItemIds = origenItems.Select(x => x.Id).ToList();
        var origenContenidos = origenItemIds.Count == 0
            ? new List<AseguradoraInformeContenido>()
            : await _db.AseguradoraInformeContenidos.AsNoTracking()
                .Where(c => origenItemIds.Contains(c.ItemId))
                .OrderBy(c => c.Orden).ToListAsync(ct);
        var contPorItem = origenContenidos.GroupBy(c => c.ItemId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var src in origenItems)
        {
            var nuevo = new AseguradoraInformeItem
            {
                ConfigId = destino.Id,
                TenantId = tid,
                Orden = src.Orden,
                Seccion = src.Seccion,
                Origen = src.Origen,
                TipologiaArchivoId = src.TipologiaArchivoId,
                Alias = src.Alias,
                Descripcion = src.Descripcion,
                PatronNombre = src.PatronNombre,
                Obligatorio = src.Obligatorio,
                SoloUltimo = src.SoloUltimo,
            };
            if (contPorItem.TryGetValue(src.Id, out var cs))
            {
                foreach (var c in cs)
                {
                    nuevo.Contenidos.Add(new AseguradoraInformeContenido
                    {
                        Item = nuevo,
                        TenantId = tid,
                        Orden = c.Orden,
                        Origen = c.Origen,
                        TipologiaArchivoId = c.TipologiaArchivoId,
                        SoloUltimo = c.SoloUltimo,
                    });
                }
            }
            _db.AseguradoraInformeItems.Add(nuevo);
        }
        await _db.SaveChangesAsync(ct);
        return MapConfig(destino);
    }

    // ================ helpers ================

    private static string? NullIfBlank(string? s) =>
        string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static CuentaMedicaConfigDto MapConfig(AseguradoraCuentaMedicaConfig e) => new(
        e.Id, e.AseguradoraId, e.PortadaHabilitada, e.PortadaLogoUrl,
        e.PortadaTitulo, e.PortadaSubtitulo, e.PortadaTextoLegal,
        e.IndiceHabilitado, e.PatronNombreDefault);
}
