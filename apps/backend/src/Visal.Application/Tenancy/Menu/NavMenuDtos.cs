namespace Visal.Application.Tenancy.Menu;

/// <summary>Opcion del menu ya resuelta (merge de catalogo + overrides del tenant).</summary>
public sealed class NavMenuItemDto
{
    /// <summary>Key del catalogo, o "link:xxxx" para enlaces externos propios.</summary>
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Href { get; set; } = "";
    public string IconKey { get; set; } = "";
    /// <summary>Slug de permiso (solo built-in con Gate=Permission). Null = sin permiso especifico.</summary>
    public string? Permission { get; set; }
    public bool ExactMatch { get; set; }
    public NavGate Gate { get; set; } = NavGate.Permission;
    /// <summary>True = enlace externo propio del tenant (se abre en pestana nueva).</summary>
    public bool External { get; set; }
    /// <summary>True = el admin la oculto (no se ve en el menu, pero sigue en el editor).</summary>
    public bool Hidden { get; set; }
}

public sealed class NavMenuGroupDto
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string IconKey { get; set; } = "carpeta";
    public List<NavMenuItemDto> Items { get; set; } = new();
}

public sealed class NavMenuTreeDto
{
    public List<NavMenuGroupDto> Groups { get; set; } = new();
    /// <summary>True si el tenant tiene una configuracion guardada (no es el default).</summary>
    public bool EsPersonalizado { get; set; }
}

/// <summary>
/// Servicio de configuracion del menu lateral por tenant. GetEffective alimenta al
/// NavMenu (excluye ocultas; el caller aplica el gate de permisos). GetForEdit
/// alimenta a la pagina de edicion (incluye ocultas + grupos vacios).
/// </summary>
public interface INavMenuConfigService
{
    /// <summary>Arbol efectivo para render. Si se pasa tenantId, resuelve la config
    /// con ese tenant explicito (ignora el query filter), util para cargar el menu
    /// desde un scope propio sin depender del TenantAmbient del circuito.</summary>
    Task<NavMenuTreeDto> GetEffectiveAsync(Guid? tenantId = null, CancellationToken ct = default);
    Task<NavMenuTreeDto> GetForEditAsync(CancellationToken ct = default);
    Task SaveAsync(NavMenuTreeDto tree, CancellationToken ct = default);
    Task ResetAsync(CancellationToken ct = default);
}
