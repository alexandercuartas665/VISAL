namespace Visal.Application.Tenancy;

/// <summary>Modulo del sistema sobre el que se definen permisos.</summary>
public sealed record ModuloInfo(string Key, string Label, string Grupo);

public static class ModuloCatalogo
{
    public static readonly IReadOnlyList<ModuloInfo> Todos = new List<ModuloInfo>
    {
        new("pacientes", "Pacientes / Admision", "Operacion Clinica"),
        new("asignacion", "Asignacion de Servicios", "Operacion Clinica"),
        new("coordinacion", "Coordinacion", "Operacion Clinica"),
        new("profesionales", "Profesionales (atencion)", "Operacion Clinica"),
        new("turnos", "Turnos", "Operacion Clinica"),
        new("historias", "Historias Clinicas", "Operacion Clinica"),
        // Permiso administrativo: reabrir una HC previamente cerrada (marcarla
        // como Abierta y limpiar FechaCierre para permitir editarla de nuevo).
        // Sin este permiso, el rol solo puede consultar las HC cerradas. Los
        // roles tenant Owner/Admin y el SuperAdmin de plataforma tienen este
        // permiso implicitamente aunque el rol no lo tenga marcado.
        new("historias.reabrir", "Historias Clinicas - Reabrir HC cerrada", "Operacion Clinica"),
        new("notas", "Notas Medicas", "Operacion Clinica"),
        new("ordenes", "Ordenes Clinicas", "Operacion Clinica"),
        new("cie11", "Configuracion CIE-11", "Configuracion del Sistema"),
        new("formularios", "Motor de Formularios", "Operacion Clinica"),
        new("formularios.versionado", "Formularios - Historial y restaurar versiones", "Operacion Clinica"),
        new("lineas", "Lineas WhatsApp", "Infraestructura & IA"),
        new("agentes", "Agentes IA", "Infraestructura & IA"),
        new("automatizaciones", "Automatizaciones", "Infraestructura & IA"),
        new("metricas", "Metricas", "Infraestructura & IA"),
        new("cfg-aseguradoras", "Entidades Aseguradoras", "Configuracion del Sistema"),
        new("cfg-profesionales", "Profesionales (catalogo)", "Configuracion del Sistema"),
        new("cfg-servicios", "Servicios", "Configuracion del Sistema"),
        new("cfg-pacientes", "Configuracion Pacientes", "Configuracion del Sistema"),
        new("cfg-turnos", "Configuracion de Turnos", "Configuracion del Sistema"),
        new("cfg-empresa", "Configuracion de Empresa", "Configuracion de la Entidad"),
        new("cfg-interoperabilidad", "Interoperabilidad", "Configuracion de la Entidad"),
        new("cfg-roles", "Roles y Permisos", "Configuracion de la Entidad"),
        new("cfg-usuarios", "Administracion de Usuarios", "Configuracion de la Entidad"),
    };
}

public sealed record RolDto(Guid Id, string Nombre, string? Descripcion, bool Activo);

public sealed record PermisoDto(string Modulo, bool Ver, bool Crear, bool Editar, bool Eliminar);

public sealed record RolDetailDto(Guid Id, string Nombre, string? Descripcion, bool Activo, IReadOnlyList<PermisoDto> Permisos);

public interface IRolService
{
    Task<IReadOnlyList<RolDto>> ListAsync(CancellationToken ct = default);
    Task<RolDetailDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<RolDto?> SaveAsync(Guid? id, string nombre, string? descripcion, bool activo, Guid actor, CancellationToken ct = default);
    Task<bool> DeleteAsync(Guid id, Guid actor, CancellationToken ct = default);
    Task SavePermisosAsync(Guid rolId, IReadOnlyList<PermisoDto> permisos, Guid actor, CancellationToken ct = default);
}
