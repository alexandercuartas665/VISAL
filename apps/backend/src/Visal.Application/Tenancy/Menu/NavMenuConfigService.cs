using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Visal.Application.Common;
using Visal.Domain.Entities;

namespace Visal.Application.Tenancy.Menu;

/// <summary>
/// Arma el arbol del menu lateral del tenant haciendo merge del catalogo built-in
/// (<see cref="NavMenuCatalog"/>) con la configuracion guardada (JSON). Garantiza
/// que las opciones nuevas del codigo aparezcan solas en su grupo por defecto,
/// aunque el tenant tenga una config vieja. Sin config guardada -> arbol default
/// identico al menu hardcodeado previo.
/// </summary>
public sealed class NavMenuConfigService : INavMenuConfigService
{
    private readonly IApplicationDbContext _db;
    private static readonly JsonSerializerOptions J = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public NavMenuConfigService(IApplicationDbContext db) => _db = db;

    public Task<NavMenuTreeDto> GetEffectiveAsync(Guid? tenantId = null, CancellationToken ct = default) => BuildAsync(incluirOcultas: false, paraEditar: false, tenantId, ct);
    public Task<NavMenuTreeDto> GetForEditAsync(CancellationToken ct = default) => BuildAsync(incluirOcultas: true, paraEditar: true, null, ct);

    private async Task<NavMenuTreeDto> BuildAsync(bool incluirOcultas, bool paraEditar, Guid? tenantId, CancellationToken ct)
    {
        var row = tenantId is Guid tid
            ? await _db.TenantNavMenuConfigs.AsNoTracking().IgnoreQueryFilters().FirstOrDefaultAsync(x => x.TenantId == tid, ct)
            : await _db.TenantNavMenuConfigs.AsNoTracking().FirstOrDefaultAsync(ct);
        PersistedTree? saved = null;
        if (row is not null && !string.IsNullOrWhiteSpace(row.ConfigJson) && row.ConfigJson.Trim() != "{}")
        {
            try { saved = JsonSerializer.Deserialize<PersistedTree>(row.ConfigJson, J); } catch { saved = null; }
        }

        var esPersonalizado = saved is not null && saved.Groups is { Count: > 0 };
        var tree = esPersonalizado ? Merge(saved!) : BuildDefault();
        tree.EsPersonalizado = esPersonalizado;

        if (!incluirOcultas)
        {
            foreach (var g in tree.Groups) { g.Items = g.Items.Where(i => !i.Hidden).ToList(); }
        }
        if (!paraEditar)
        {
            tree.Groups = tree.Groups.Where(g => g.Items.Count > 0).ToList();
        }
        return tree;
    }

    private static NavMenuTreeDto BuildDefault()
    {
        var tree = new NavMenuTreeDto();
        foreach (var groupName in NavMenuCatalog.DefaultGroups)
        {
            var items = NavMenuCatalog.Items.Where(i => i.Group == groupName).Select(d => ToDto(d)).ToList();
            if (items.Count == 0) { continue; }
            tree.Groups.Add(new NavMenuGroupDto { Id = Slug(groupName), Label = groupName, IconKey = DefaultGroupIcon(groupName), Items = items });
        }
        return tree;
    }

    private static NavMenuTreeDto Merge(PersistedTree saved)
    {
        var byKey = NavMenuCatalog.Items.ToDictionary(i => i.Key, StringComparer.OrdinalIgnoreCase);
        var placed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tree = new NavMenuTreeDto();

        foreach (var pg in (saved.Groups ?? new()).OrderBy(g => g.Order))
        {
            var group = new NavMenuGroupDto
            {
                Id = string.IsNullOrWhiteSpace(pg.Id) ? Slug(pg.Label ?? "grupo") : pg.Id!,
                Label = pg.Label?.Trim() ?? "",
                IconKey = string.IsNullOrWhiteSpace(pg.IconKey) ? "carpeta" : pg.IconKey!,
            };
            foreach (var pi in (pg.Items ?? new()).OrderBy(x => x.Order))
            {
                if (pi.External)
                {
                    if (string.IsNullOrWhiteSpace(pi.Href)) { continue; }
                    group.Items.Add(new NavMenuItemDto
                    {
                        Key = string.IsNullOrWhiteSpace(pi.Key) ? "link:" + Slug(pi.Label ?? "enlace") : pi.Key!,
                        Label = pi.Label?.Trim() ?? "Enlace",
                        Href = pi.Href!.Trim(),
                        IconKey = string.IsNullOrWhiteSpace(pi.IconKey) ? "enlace" : pi.IconKey!,
                        External = true,
                        Hidden = pi.Hidden,
                        Gate = NavGate.Permission,
                    });
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(pi.Key) || !byKey.TryGetValue(pi.Key!, out var def)) { continue; } // stale key
                    if (!placed.Add(def.Key)) { continue; } // dedup si aparece 2 veces
                    group.Items.Add(ToDto(def, pi.Label, pi.IconKey, pi.Hidden));
                }
            }
            tree.Groups.Add(group);
        }

        // Opciones del catalogo que NO estaban en la config guardada -> a su grupo
        // por defecto (creandolo si no existe). Asi los modulos nuevos aparecen solos.
        foreach (var def in NavMenuCatalog.Items)
        {
            if (placed.Contains(def.Key)) { continue; }
            var g = tree.Groups.FirstOrDefault(x => string.Equals(x.Label, def.Group, StringComparison.OrdinalIgnoreCase));
            if (g is null)
            {
                g = new NavMenuGroupDto { Id = Slug(def.Group), Label = def.Group, IconKey = DefaultGroupIcon(def.Group) };
                tree.Groups.Add(g);
            }
            g.Items.Add(ToDto(def));
            placed.Add(def.Key);
        }
        return tree;
    }

    public async Task SaveAsync(NavMenuTreeDto tree, CancellationToken ct = default)
    {
        var byKey = NavMenuCatalog.Items.ToDictionary(i => i.Key, StringComparer.OrdinalIgnoreCase);
        var persisted = new PersistedTree { Groups = new() };
        var gOrder = 0;
        foreach (var g in tree.Groups ?? new())
        {
            var pg = new PersistedGroup
            {
                Id = string.IsNullOrWhiteSpace(g.Id) ? Slug(g.Label) : g.Id,
                Label = g.Label?.Trim() ?? "",
                IconKey = string.IsNullOrWhiteSpace(g.IconKey) ? "carpeta" : g.IconKey,
                Order = gOrder++,
                Items = new(),
            };
            var iOrder = 0;
            foreach (var it in g.Items ?? new())
            {
                if (it.External)
                {
                    if (string.IsNullOrWhiteSpace(it.Href)) { continue; }
                    var key = !string.IsNullOrWhiteSpace(it.Key) && it.Key.StartsWith("link:", StringComparison.Ordinal)
                        ? it.Key
                        : "link:" + Guid.NewGuid().ToString("N")[..8];
                    pg.Items.Add(new PersistedItem
                    {
                        Key = key,
                        Label = it.Label?.Trim(),
                        IconKey = string.IsNullOrWhiteSpace(it.IconKey) ? "enlace" : it.IconKey,
                        Href = it.Href.Trim(),
                        External = true,
                        Hidden = it.Hidden,
                        Order = iOrder++,
                    });
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(it.Key) || !byKey.TryGetValue(it.Key!, out var def)) { continue; }
                    // Guardamos override de etiqueta/icono solo si difieren del default,
                    // asi si el codigo renombra un modulo el cambio se propaga.
                    var labelOv = string.Equals(it.Label?.Trim(), def.Label, StringComparison.Ordinal) ? null : it.Label?.Trim();
                    var iconOv = string.Equals(it.IconKey, def.IconKey, StringComparison.Ordinal) ? null : it.IconKey;
                    pg.Items.Add(new PersistedItem
                    {
                        Key = def.Key,
                        Label = labelOv,
                        IconKey = iconOv,
                        External = false,
                        Hidden = it.Hidden,
                        Order = iOrder++,
                    });
                }
            }
            persisted.Groups.Add(pg);
        }

        var json = JsonSerializer.Serialize(persisted, J);
        var row = await _db.TenantNavMenuConfigs.FirstOrDefaultAsync(ct);
        if (row is null)
        {
            row = new TenantNavMenuConfig { ConfigJson = json };
            _db.TenantNavMenuConfigs.Add(row);
        }
        else
        {
            row.ConfigJson = json;
        }
        await _db.SaveChangesAsync(ct);
    }

    public async Task ResetAsync(CancellationToken ct = default)
    {
        var row = await _db.TenantNavMenuConfigs.FirstOrDefaultAsync(ct);
        if (row is not null)
        {
            _db.TenantNavMenuConfigs.Remove(row);
            await _db.SaveChangesAsync(ct);
        }
    }

    private static NavMenuItemDto ToDto(NavMenuItemDef def, string? labelOverride = null, string? iconOverride = null, bool hidden = false) => new()
    {
        Key = def.Key,
        Label = string.IsNullOrWhiteSpace(labelOverride) ? def.Label : labelOverride!,
        Href = def.Href,
        IconKey = string.IsNullOrWhiteSpace(iconOverride) ? def.IconKey : iconOverride!,
        Permission = def.Permission,
        ExactMatch = def.ExactMatch,
        Gate = def.Gate,
        External = false,
        Hidden = hidden,
    };

    private static string Slug(string? s)
    {
        var chars = (s ?? "").ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray();
        return new string(chars).Trim('-');
    }

    private static string DefaultGroupIcon(string group) => group switch
    {
        "Operacion Clinica" => "estetoscopio",
        "Interoperabilidad" => "rda",
        "Facturacion" => "factura",
        "Infraestructura & IA" => "robot",
        "Configuracion del Sistema" => "engrane",
        "Configuracion de la Entidad" => "edificio",
        "Mi agencia" => "persona",
        _ => "carpeta",
    };

    // ===== Modelo persistido (JSON) =====
    private sealed class PersistedTree { public List<PersistedGroup>? Groups { get; set; } }

    private sealed class PersistedGroup
    {
        public string? Id { get; set; }
        public string? Label { get; set; }
        public string? IconKey { get; set; }
        public int Order { get; set; }
        public List<PersistedItem>? Items { get; set; }
    }

    private sealed class PersistedItem
    {
        public string? Key { get; set; }
        public string? Label { get; set; }
        public string? IconKey { get; set; }
        public string? Href { get; set; }
        public bool External { get; set; }
        public bool Hidden { get; set; }
        public int Order { get; set; }
    }
}
